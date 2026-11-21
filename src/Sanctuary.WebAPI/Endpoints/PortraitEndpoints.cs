using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;

using Sanctuary.Core.Helpers;
using Sanctuary.WebAPI.Website;

namespace Sanctuary.WebAPI.Endpoints;

public static class PortraitEndpoints
{
    private const int ThumbnailWidth = 70;
    private const int ThumbnailHeight = 70;
    private const int PortraitWidth = 180;
    private const int PortraitHeight = 330;

    private static readonly DecoderOptions UploadDecoderOptions = new() { SkipMetadata = true };

    private static ILogger _logger = null!;

    public static void MapPortraitEndpoints(this WebApplication app)
    {
        var loggerFactory = app.Services.GetRequiredService<ILoggerFactory>();

        _logger = loggerFactory.CreateLogger(nameof(PortraitEndpoints));

        app.MapPost("/image", ImageHandlerAsync).DisableAntiforgery().RequireRateLimiting(WebsiteSecurity.WritePolicy);
    }

    private static async Task<IResult> ImageHandlerAsync(
        HttpContext context,
        [FromForm] string imageType,
        [FromForm] ulong characterId,
        CancellationToken cancellationToken,
        [FromForm] IFormFileCollection files)
    {
        // ContentType and Boundary are hardcoded in the client.
        if (context.Request.ContentType != "multipart/form-data; boundary=AaBb432101234bBaA")
        {
            _logger.LogWarning("Invalid Content-Type: {ContentType}", context.Request.ContentType);

            return Results.BadRequest("Invalid Content-Type");
        }

        // ImageType is hardcoded in the client.
        if (imageType != "portrait")
        {
            _logger.LogWarning("Invalid imageType: {ImageType}", imageType);

            return Results.BadRequest("Invalid imageType.");
        }

        if (characterId == 0)
        {
            _logger.LogWarning("Invalid characterId: {CharacterId}", characterId);

            return Results.BadRequest("Invalid characterId.");
        }

        var uploads = new Dictionary<string, Image<Rgba32>>();
        uint? portraitCrc = null;

        try
        {
            foreach (var file in files)
            {
                if (file.Length == 0 || file.ContentType != "image/png")
                {
                    _logger.LogWarning("Invalid file name: {FileName}", file.FileName);

                    return Results.BadRequest("Invalid file name.");
                }

                // Names are hardcoded in the client.
                var fileName = file.Name switch
                {
                    "thumbnailFile" => "headshot.png",
                    "imageFile" => "portrait.png",
                    _ => null
                };

                if (fileName is null || Path.GetFileName(file.FileName) != fileName || uploads.ContainsKey(fileName))
                {
                    _logger.LogWarning("Invalid name: {Name} {FileName}", file.Name, file.FileName);

                    return Results.BadRequest("Invalid name.");
                }

                using var stream = file.OpenReadStream();

                using var imageBuffer = new MemoryStream();
                await stream.CopyToAsync(imageBuffer, cancellationToken);
                imageBuffer.Position = 0;

                var expectedWidth = file.Name == "thumbnailFile" ? ThumbnailWidth : PortraitWidth;
                var expectedHeight = file.Name == "thumbnailFile" ? ThumbnailHeight : PortraitHeight;

                if (!await HasSizeAsync(imageBuffer, expectedWidth, expectedHeight, cancellationToken))
                {
                    _logger.LogWarning("Invalid {Name} size for character {CharacterId}.", file.Name, characterId);

                    return Results.BadRequest("Invalid image size.");
                }

                imageBuffer.Position = 0;

                var image = await TryDecodePngAsync(imageBuffer, cancellationToken);

                if (image is null)
                    return Results.BadRequest("Invalid image format.");

                uploads[fileName] = image;

                if (file.Name == "imageFile")
                    portraitCrc = PortraitStorage.GetPortraitCrc(imageBuffer.GetBuffer().AsSpan(0, (int)imageBuffer.Length));
            }

            if (portraitCrc is null)
            {
                _logger.LogWarning("Portrait upload for character {CharacterId} has no imageFile.", characterId);

                return Results.BadRequest("Missing imageFile.");
            }

            PortraitStorage.DeleteStalePendingUploads(characterId, TimeSpan.FromMinutes(2));

            var pendingDirectory = PortraitStorage.GetPendingDirectory(characterId, portraitCrc.Value);

            Directory.CreateDirectory(pendingDirectory);

            foreach (var (fileName, image) in uploads)
                await image.SaveAsPngAsync(Path.Combine(pendingDirectory, fileName), cancellationToken);

            await File.WriteAllBytesAsync(Path.Combine(pendingDirectory, "portrait.crc"), BitConverter.GetBytes(portraitCrc.Value), cancellationToken);

            _logger.LogDebug("Stored pending portrait {Crc} for character {Character}.", portraitCrc.Value, characterId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving uploaded file for character {CharacterId}", characterId);

            return Results.StatusCode(500);
        }
        finally
        {
            foreach (var image in uploads.Values)
                image.Dispose();
        }

        return Results.Ok();
    }

    private static async Task<bool> HasSizeAsync(Stream stream, int width, int height, CancellationToken cancellationToken)
    {
        try
        {
            var info = await PngDecoder.Instance.IdentifyAsync(UploadDecoderOptions, stream, cancellationToken);

            return info.Width == width && info.Height == height;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static async Task<Image<Rgba32>?> TryDecodePngAsync(Stream stream, CancellationToken cancellationToken)
    {
        try
        {
            return await PngDecoder.Instance.DecodeAsync<Rgba32>(UploadDecoderOptions, stream, cancellationToken);
        }
        catch (ImageFormatException ex)
        {
            _logger.LogWarning("Invalid image format: {Message}", ex.Message);
            return null;
        }
    }
}
