using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Sanctuary.WebAPI.Options;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Sanctuary.WebAPI.Website;

public sealed class PortraitCutouts
{
    private const int ExactTolerance = 4;
    private const int EstimateTolerance = 24;
    private const int EstimateFlatness = 6;

    private sealed record Plate(int Width, int Height, Rgba32[] Pixels, bool UseEstimates);

    private readonly ILogger<PortraitCutouts> _logger;
    private readonly string? _plateFolder;
    private readonly ConcurrentDictionary<string, Plate?> _plates = new();
    private readonly ConcurrentDictionary<string, (DateTime Stamp, byte[] Png)> _cache = new();

    public PortraitCutouts(ILogger<PortraitCutouts> logger, IOptions<WebAPIOptions> options)
    {
        _logger = logger;
        _plateFolder = options.Value.PortraitPlateFolder;
    }

    public byte[]? GetCutout(string path, string kind)
    {
        if (!File.Exists(path))
            return null;

        var stamp = File.GetLastWriteTimeUtc(path);

        if (_cache.TryGetValue(path, out var cached) && cached.Stamp == stamp)
            return cached.Png;

        var plate = _plates.GetOrAdd(kind, LoadPlate);
        byte[] png;

        try
        {
            png = plate is null ? File.ReadAllBytes(path) : Cut(path, plate);
        }
        catch (Exception ex) when (ex is IOException or UnknownImageFormatException or InvalidImageContentException)
        {
            _logger.LogWarning("Could not cut out portrait {Path}: {Message}", path, ex.Message);
            png = File.ReadAllBytes(path);
        }

        _cache[path] = (stamp, png);
        return png;
    }

    private Plate? LoadPlate(string kind)
    {
        if (string.IsNullOrWhiteSpace(_plateFolder))
            return null;

        var path = Path.Combine(_plateFolder, kind + ".png");

        if (!File.Exists(path))
            return null;

        using var image = Image.Load<Rgba32>(path);
        var pixels = new Rgba32[image.Width * image.Height];
        image.CopyPixelDataTo(pixels);

        return new Plate(image.Width, image.Height, pixels, kind == "portrait");
    }

    private static byte[] Cut(string path, Plate plate)
    {
        using var image = Image.Load<Rgba32>(path);

        if (image.Width != plate.Width || image.Height != plate.Height)
        {
            using var original = new MemoryStream();
            image.SaveAsPng(original);
            return original.ToArray();
        }

        var width = image.Width;
        var height = image.Height;
        var pixels = new Rgba32[width * height];
        image.CopyPixelDataTo(pixels);

        var backgroundLike = new bool[pixels.Length];

        for (var i = 0; i < pixels.Length; i++)
        {
            var reference = plate.Pixels[i];
            var known = reference.A > 127;

            if (!known && !plate.UseEstimates)
                continue;

            var difference = Distance(pixels[i], reference);

            if (known)
            {
                backgroundLike[i] = difference <= ExactTolerance;
                continue;
            }

            if (difference > EstimateTolerance)
                continue;

            var x = i % width;
            var y = i / width;
            var flat = (x == 0 || Distance(pixels[i], pixels[i - 1]) <= EstimateFlatness)
                && (x == width - 1 || Distance(pixels[i], pixels[i + 1]) <= EstimateFlatness)
                && (y == 0 || Distance(pixels[i], pixels[i - width]) <= EstimateFlatness)
                && (y == height - 1 || Distance(pixels[i], pixels[i + width]) <= EstimateFlatness);

            backgroundLike[i] = flat;
        }

        var background = new bool[pixels.Length];
        var queue = new Queue<int>();

        void Seed(int x, int y)
        {
            var index = y * width + x;
            if (backgroundLike[index] && !background[index])
            {
                background[index] = true;
                queue.Enqueue(index);
            }
        }

        for (var x = 0; x < width; x++)
        {
            Seed(x, 0);
            Seed(x, height - 1);
        }

        for (var y = 0; y < height; y++)
        {
            Seed(0, y);
            Seed(width - 1, y);
        }

        while (queue.Count > 0)
        {
            var index = queue.Dequeue();
            var x = index % width;
            var y = index / width;

            if (x > 0) Seed(x - 1, y);
            if (x < width - 1) Seed(x + 1, y);
            if (y > 0) Seed(x, y - 1);
            if (y < height - 1) Seed(x, y + 1);
        }

        var alpha = new byte[pixels.Length];

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var foreground = !background[y * width + x];

                if (foreground)
                {
                    for (var dy = -1; dy <= 1 && foreground; dy++)
                    {
                        for (var dx = -1; dx <= 1 && foreground; dx++)
                        {
                            var nx = x + dx;
                            var ny = y + dy;
                            if (nx >= 0 && ny >= 0 && nx < width && ny < height && background[ny * width + nx])
                                foreground = false;
                        }
                    }
                }

                alpha[y * width + x] = foreground ? (byte)255 : (byte)0;
            }
        }

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var sum = 0;
                var count = 0;

                for (var dy = -1; dy <= 1; dy++)
                {
                    for (var dx = -1; dx <= 1; dx++)
                    {
                        var nx = x + dx;
                        var ny = y + dy;
                        if (nx < 0 || ny < 0 || nx >= width || ny >= height)
                            continue;

                        sum += alpha[ny * width + nx];
                        count++;
                    }
                }

                var index = y * width + x;
                var smooth = (byte)(sum / count);
                pixels[index].A = Math.Min(pixels[index].A, alpha[index] == 255 ? (byte)Math.Max(smooth, (byte)200) : smooth < 100 ? (byte)0 : smooth);
            }
        }

        using var result = Image.LoadPixelData<Rgba32>(pixels, width, height);
        using var output = new MemoryStream();
        result.SaveAsPng(output);
        return output.ToArray();
    }

    private static int Distance(Rgba32 a, Rgba32 b)
    {
        return Math.Max(Math.Abs(a.R - b.R), Math.Max(Math.Abs(a.G - b.G), Math.Abs(a.B - b.B)));
    }
}
