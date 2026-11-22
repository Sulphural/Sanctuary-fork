using System;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Sanctuary.Game;
using Sanctuary.WebAPI.Options;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Sanctuary.WebAPI.Website;

public sealed class ItemIcons
{
    private const int LargeIconType = 7;
    private const int MediumIconType = 6;
    private const int SmallIconType = 5;

    private sealed record PackEntry(string Pack, long Offset, int Length);

    private sealed class Catalog
    {
        public Dictionary<int, string> Images { get; } = [];
        public Dictionary<int, Dictionary<int, int>> ImageSets { get; } = [];
        public Dictionary<int, (float R, float G, float B)> Tints { get; } = [];
        public Dictionary<string, PackEntry> Files { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private readonly ILogger<ItemIcons> _logger;
    private readonly IResourceManager _resourceManager;
    private readonly string? _clientFolder;
    private readonly Lazy<Catalog?> _catalog;
    private readonly ConcurrentDictionary<int, byte[]?> _cache = new();

    public ItemIcons(ILogger<ItemIcons> logger, IResourceManager resourceManager, IOptions<WebAPIOptions> options)
    {
        _logger = logger;
        _resourceManager = resourceManager;
        _clientFolder = options.Value.ClientFolder;
        _catalog = new Lazy<Catalog?>(LoadCatalog);
    }

    public byte[]? GetIconPng(int itemDefinitionId)
    {
        return _cache.GetOrAdd(itemDefinitionId, Render);
    }

    private byte[]? Render(int itemDefinitionId)
    {
        var catalog = _catalog.Value;

        if (catalog is null || !_resourceManager.ClientItemDefinitions.TryGetValue(itemDefinitionId, out var definition))
            return null;

        if (!catalog.ImageSets.TryGetValue(definition.Icon.Id, out var set))
            return null;

        foreach (var type in new[] { LargeIconType, MediumIconType, SmallIconType })
        {
            if (!set.TryGetValue(type, out var imageId) || !catalog.Images.TryGetValue(imageId, out var fileName))
                continue;

            var dds = ReadFile(catalog, fileName);
            if (dds is null)
                continue;

            try
            {
                using var image = DecodeDds(dds);

                if (definition.Icon.TintId != 0 && catalog.Tints.TryGetValue(definition.Icon.TintId, out var tint))
                    ApplyTint(image, tint);

                using var output = new MemoryStream();
                image.SaveAsPng(output);
                return output.ToArray();
            }
            catch (Exception ex) when (ex is InvalidDataException or NotSupportedException or IndexOutOfRangeException or ArgumentException)
            {
                _logger.LogWarning("Could not decode icon {File} for item {Item}: {Message}", fileName, itemDefinitionId, ex.Message);
            }
        }

        return null;
    }

    private static void ApplyTint(Image<Rgba32> image, (float R, float G, float B) tint)
    {
        image.ProcessPixelRows(rows =>
        {
            for (var y = 0; y < rows.Height; y++)
            {
                var row = rows.GetRowSpan(y);

                for (var x = 0; x < row.Length; x++)
                {
                    ref var pixel = ref row[x];

                    var max = Math.Max(pixel.R, Math.Max(pixel.G, pixel.B));
                    var min = Math.Min(pixel.R, Math.Min(pixel.G, pixel.B));

                    if (pixel.A == 0 || max - min > 24)
                        continue;

                    var gray = (pixel.R + pixel.G + pixel.B) / 3f / 255f;

                    pixel.R = Overlay(gray, tint.R);
                    pixel.G = Overlay(gray, tint.G);
                    pixel.B = Overlay(gray, tint.B);
                }
            }
        });
    }

    private static byte Overlay(float gray, float tint)
    {
        var value = gray < 0.5f ? 2f * gray * tint : 1f - 2f * (1f - gray) * (1f - tint);
        return (byte)Math.Clamp(value * 255f, 0, 255);
    }

    private static Image<Rgba32> DecodeDds(byte[] data)
    {
        if (data.Length < 128 || BinaryPrimitives.ReadUInt32LittleEndian(data) != 0x20534444)
            throw new InvalidDataException("Not a DDS file.");

        var height = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(12));
        var width = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(16));
        var flags = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(80));
        var fourCc = System.Text.Encoding.ASCII.GetString(data, 84, 4);

        if (width <= 0 || height <= 0 || width > 1024 || height > 1024)
            throw new InvalidDataException("Unexpected DDS size.");

        var pixels = new Rgba32[width * height];
        var body = data.AsSpan(128);

        if ((flags & 4) != 0 && fourCc is "DXT1" or "DXT3" or "DXT5")
            DecodeBlocks(body, width, height, fourCc, pixels);
        else if (BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(88)) == 32)
            DecodeArgb(body, width, height, pixels);
        else
            throw new NotSupportedException($"DDS format {fourCc} is not supported.");

        return Image.LoadPixelData<Rgba32>(pixels, width, height);
    }

    private static void DecodeArgb(ReadOnlySpan<byte> body, int width, int height, Rgba32[] pixels)
    {
        for (var i = 0; i < width * height; i++)
        {
            var b = body[i * 4];
            var g = body[i * 4 + 1];
            var r = body[i * 4 + 2];
            var a = body[i * 4 + 3];
            pixels[i] = new Rgba32(r, g, b, a);
        }
    }

    private static void DecodeBlocks(ReadOnlySpan<byte> body, int width, int height, string format, Rgba32[] pixels)
    {
        var blockSize = format == "DXT1" ? 8 : 16;
        var blocksWide = (width + 3) / 4;
        var blocksHigh = (height + 3) / 4;
        Span<Rgba32> colors = stackalloc Rgba32[4];
        Span<byte> alphas = stackalloc byte[16];

        for (var by = 0; by < blocksHigh; by++)
        {
            for (var bx = 0; bx < blocksWide; bx++)
            {
                var block = body.Slice((by * blocksWide + bx) * blockSize, blockSize);
                var colorBlock = format == "DXT1" ? block : block[8..];

                for (var i = 0; i < 16; i++)
                    alphas[i] = 255;

                if (format == "DXT3")
                {
                    for (var i = 0; i < 16; i++)
                    {
                        var nibble = (block[i / 2] >> ((i % 2) * 4)) & 0xF;
                        alphas[i] = (byte)(nibble * 17);
                    }
                }
                else if (format == "DXT5")
                {
                    DecodeDxt5Alpha(block, alphas);
                }

                var c0 = BinaryPrimitives.ReadUInt16LittleEndian(colorBlock);
                var c1 = BinaryPrimitives.ReadUInt16LittleEndian(colorBlock[2..]);
                colors[0] = Rgb565(c0);
                colors[1] = Rgb565(c1);

                if (c0 > c1 || format != "DXT1")
                {
                    colors[2] = Mix(colors[0], colors[1], 2, 1);
                    colors[3] = Mix(colors[0], colors[1], 1, 2);
                }
                else
                {
                    colors[2] = Mix(colors[0], colors[1], 1, 1);
                    colors[3] = new Rgba32(0, 0, 0, 0);
                }

                var indices = BinaryPrimitives.ReadUInt32LittleEndian(colorBlock[4..]);

                for (var i = 0; i < 16; i++)
                {
                    var x = bx * 4 + i % 4;
                    var y = by * 4 + i / 4;

                    if (x >= width || y >= height)
                        continue;

                    var color = colors[(int)((indices >> (i * 2)) & 3)];
                    if (format != "DXT1" || color.A != 0)
                        color.A = alphas[i];

                    pixels[y * width + x] = color;
                }
            }
        }
    }

    private static void DecodeDxt5Alpha(ReadOnlySpan<byte> block, Span<byte> alphas)
    {
        Span<byte> table = stackalloc byte[8];
        table[0] = block[0];
        table[1] = block[1];

        if (table[0] > table[1])
        {
            for (var i = 1; i < 7; i++)
                table[i + 1] = (byte)(((7 - i) * table[0] + i * table[1]) / 7);
        }
        else
        {
            for (var i = 1; i < 5; i++)
                table[i + 1] = (byte)(((5 - i) * table[0] + i * table[1]) / 5);
            table[6] = 0;
            table[7] = 255;
        }

        ulong bits = 0;
        for (var i = 0; i < 6; i++)
            bits |= (ulong)block[2 + i] << (8 * i);

        for (var i = 0; i < 16; i++)
            alphas[i] = table[(int)((bits >> (3 * i)) & 7)];
    }

    private static Rgba32 Rgb565(ushort value)
    {
        var r = (value >> 11) & 0x1F;
        var g = (value >> 5) & 0x3F;
        var b = value & 0x1F;
        return new Rgba32((byte)(r * 255 / 31), (byte)(g * 255 / 63), (byte)(b * 255 / 31), 255);
    }

    private static Rgba32 Mix(Rgba32 a, Rgba32 b, int wa, int wb)
    {
        var total = wa + wb;
        return new Rgba32((byte)((a.R * wa + b.R * wb) / total), (byte)((a.G * wa + b.G * wb) / total), (byte)((a.B * wa + b.B * wb) / total), 255);
    }

    private byte[]? ReadFile(Catalog catalog, string fileName)
    {
        var compressed = false;

        if (!catalog.Files.TryGetValue(fileName, out var entry))
        {
            if (!catalog.Files.TryGetValue(fileName + ".z", out entry))
                return null;
            compressed = true;
        }

        using var stream = File.OpenRead(entry.Pack);
        stream.Seek(entry.Offset, SeekOrigin.Begin);

        var buffer = new byte[entry.Length];
        stream.ReadExactly(buffer);

        if (!compressed)
            return buffer;

        var start = buffer.Length > 8 && BinaryPrimitives.ReadUInt32BigEndian(buffer) == 0xA1B2C3D4 ? 8 : 0;

        using var input = new MemoryStream(buffer, start, buffer.Length - start);
        using var zlib = new ZLibStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        zlib.CopyTo(output);
        return output.ToArray();
    }

    private Catalog? LoadCatalog()
    {
        if (string.IsNullOrWhiteSpace(_clientFolder) || !Directory.Exists(_clientFolder))
        {
            _logger.LogInformation("Item icons are off because WebAPI:ClientFolder is not set to the game client folder.");
            return null;
        }

        var catalog = new Catalog();

        try
        {
            foreach (var row in ReadTable(Path.Combine(_clientFolder, "Resources", "Images", "Images.txt")))
            {
                if (row.Length >= 2 && int.TryParse(row[0], out var id))
                    catalog.Images[id] = row[1];
            }

            foreach (var row in ReadTable(Path.Combine(_clientFolder, "Resources", "Images", "ImageSetMappings.txt")))
            {
                if (row.Length >= 3 && int.TryParse(row[0], out var setId) && int.TryParse(row[1], out var type) && int.TryParse(row[2], out var imageId))
                {
                    if (!catalog.ImageSets.TryGetValue(setId, out var types))
                        catalog.ImageSets[setId] = types = [];
                    types[type] = imageId;
                }
            }

            var tintsPath = Path.Combine(_clientFolder, "Resources", "Tints.xml");
            if (File.Exists(tintsPath))
            {
                foreach (var tint in XDocument.Load(tintsPath).Descendants("TintVal"))
                {
                    if (int.TryParse((string?)tint.Attribute("id"), out var id)
                        && float.TryParse((string?)tint.Attribute("r"), NumberStyles.Float, CultureInfo.InvariantCulture, out var r)
                        && float.TryParse((string?)tint.Attribute("g"), NumberStyles.Float, CultureInfo.InvariantCulture, out var g)
                        && float.TryParse((string?)tint.Attribute("b"), NumberStyles.Float, CultureInfo.InvariantCulture, out var b))
                        catalog.Tints[id] = (r, g, b);
                }
            }

            foreach (var pack in Directory.GetFiles(_clientFolder, "Assets*.pack").OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                IndexPack(catalog, pack);

            _logger.LogInformation("Item icons ready: {Files} files in the client packs, {Sets} image sets.", catalog.Files.Count, catalog.ImageSets.Count);
            return catalog;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Xml.XmlException)
        {
            _logger.LogWarning(ex, "Could not read the game client for item icons.");
            return null;
        }
    }

    private static IEnumerable<string[]> ReadTable(string path)
    {
        if (!File.Exists(path))
            yield break;

        foreach (var line in File.ReadLines(path, System.Text.Encoding.Latin1))
        {
            if (line.StartsWith('#'))
                continue;

            yield return line.Split('^');
        }
    }

    private static void IndexPack(Catalog catalog, string pack)
    {
        using var stream = File.OpenRead(pack);
        using var reader = new BinaryReader(stream);
        long offset = 0;

        while (true)
        {
            stream.Seek(offset, SeekOrigin.Begin);
            var next = ReadUInt32BigEndian(reader);
            var count = ReadUInt32BigEndian(reader);

            for (var i = 0; i < count; i++)
            {
                var nameLength = (int)ReadUInt32BigEndian(reader);
                var name = System.Text.Encoding.Latin1.GetString(reader.ReadBytes(nameLength));
                var fileOffset = ReadUInt32BigEndian(reader);
                var length = ReadUInt32BigEndian(reader);
                ReadUInt32BigEndian(reader);

                catalog.Files.TryAdd(name, new PackEntry(pack, fileOffset, (int)length));
            }

            if (next == 0)
                break;

            offset = next;
        }
    }

    private static uint ReadUInt32BigEndian(BinaryReader reader)
    {
        Span<byte> bytes = stackalloc byte[4];
        if (reader.Read(bytes) != 4)
            throw new EndOfStreamException();
        return BinaryPrimitives.ReadUInt32BigEndian(bytes);
    }
}
