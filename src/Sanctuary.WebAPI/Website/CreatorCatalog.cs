using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

using Microsoft.Extensions.Options;

using Sanctuary.WebAPI.Options;

namespace Sanctuary.WebAPI.Website;

public sealed class CharacterCustomizationModel
{
    public int CharacterType { get; set; }
    public int SkinTone { get; set; }
    public int FaceType { get; set; }
    public int HairStyle { get; set; }
    public int HairColor { get; set; }
    public int EyeColor { get; set; }
    public int FacePaint { get; set; }
    public int FacialHair { get; set; }
    public int Wings { get; set; }
    public int Clothes { get; set; }
}

public sealed record CustomCharacter(
    string Gender,
    int Model,
    int Head,
    int SkinTone,
    int Hair,
    int HairColor,
    int EyeColor,
    int FacePaint,
    int ModelCustomization,
    int ItemChest,
    int ItemLegs,
    int ItemFeet,
    int TintChest,
    int TintLegs,
    int TintFeet);

public sealed class CreatorCatalog
{
    private sealed class CatalogFile
    {
        [JsonPropertyName("characterTypes")]
        public List<CatalogType> CharacterTypes { get; set; } = new();
    }

    private sealed class CatalogType
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("gender")]
        public string Gender { get; set; } = string.Empty;

        [JsonPropertyName("model")]
        public int Model { get; set; }

        [JsonPropertyName("options")]
        public Dictionary<string, List<CatalogOption>> Options { get; set; } = new();

        [JsonPropertyName("defaults")]
        public Dictionary<string, int> Defaults { get; set; } = new();

        [JsonPropertyName("quickPlay")]
        public CatalogQuickPlay? QuickPlay { get; set; }
    }

    private sealed class CatalogQuickPlay
    {
        [JsonPropertyName("hair")]
        public CatalogOption? Hair { get; set; }

        [JsonPropertyName("clothes")]
        public CatalogOption? Clothes { get; set; }
    }

    private sealed class CatalogOption
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("value")]
        public int Value { get; set; }

        [JsonPropertyName("pieces")]
        public Dictionary<string, CatalogPiece>? Pieces { get; set; }
    }

    private sealed class CatalogPiece
    {
        [JsonPropertyName("item")]
        public int Item { get; set; }

        [JsonPropertyName("tintId")]
        public int TintId { get; set; }
    }

    private readonly IOptionsMonitor<WebAPIOptions> _options;
    private readonly object _lock = new();
    private string? _loadedPath;
    private DateTime _loadedStamp;
    private CatalogFile? _catalog;

    public CreatorCatalog(IOptionsMonitor<WebAPIOptions> options)
    {
        _options = options;
    }

    private CatalogFile? Load()
    {
        var root = _options.CurrentValue.WebsiteRoot;

        if (string.IsNullOrWhiteSpace(root))
            return null;

        var path = Path.Combine(root, "creator", "catalog.json");

        if (!File.Exists(path))
            return null;

        var stamp = File.GetLastWriteTimeUtc(path);

        lock (_lock)
        {
            if (_catalog is not null && _loadedPath == path && _loadedStamp == stamp)
                return _catalog;

            _catalog = JsonSerializer.Deserialize<CatalogFile>(File.ReadAllText(path));
            _loadedPath = path;
            _loadedStamp = stamp;

            return _catalog;
        }
    }

    public bool TryDescribe(
        int model,
        int head,
        int skinTone,
        int hair,
        int hairColor,
        int eyeColor,
        int facePaint,
        int modelCustomization,
        IReadOnlyCollection<(int Definition, int Tint)> equipped,
        out int characterType,
        out Dictionary<string, int>? choices)
    {
        characterType = 0;
        choices = null;

        var type = Load()?.CharacterTypes.FirstOrDefault(x => x.Model == model);

        if (type is null)
            return false;

        characterType = type.Id;
        choices = new Dictionary<string, int>();

        int Pick(string key, int value)
        {
            if (!type.Options.TryGetValue(key, out var list) || list.Count == 0)
                return 0;

            var match = list.FirstOrDefault(x => x.Value == value);

            if (match is not null)
                return match.Id;

            if (key == "hairStyle" && type.QuickPlay?.Hair is { } quickHair && quickHair.Value == value)
                return -1;

            return type.Defaults.TryGetValue(key, out var fallback) ? fallback : list[0].Id;
        }

        choices["skinTone"] = Pick("skinTone", skinTone);
        choices["faceType"] = Pick("faceType", head);
        choices["hairStyle"] = Pick("hairStyle", hair);
        choices["hairColor"] = Pick("hairColor", hairColor);
        choices["eyeColor"] = Pick("eyeColor", eyeColor);
        choices["facePaint"] = Pick("facePaint", facePaint);

        if (type.Options.ContainsKey("facialHair"))
            choices["facialHair"] = Pick("facialHair", modelCustomization);

        if (type.Options.ContainsKey("wings"))
            choices["wings"] = Pick("wings", modelCustomization);

        if (type.Options.TryGetValue("clothes", out var clothes) && clothes.Count > 0)
        {
            var best = clothes[0];
            var bestScore = -1;
            var candidates = type.QuickPlay?.Clothes?.Pieces is { } quickPieces
                ? clothes.Append(new CatalogOption { Id = -1, Pieces = quickPieces })
                : clothes;

            foreach (var option in candidates)
            {
                if (option.Pieces is null)
                    continue;

                var score = 0;

                foreach (var piece in option.Pieces.Values)
                {
                    if (equipped.Any(x => x.Definition == piece.Item))
                        score += 2;

                    if (equipped.Any(x => x.Definition == piece.Item && x.Tint == piece.TintId))
                        score += 1;
                }

                if (score > bestScore)
                {
                    best = option;
                    bestScore = score;
                }
            }

            choices["clothes"] = bestScore > 0 || !type.Defaults.TryGetValue("clothes", out var defaultClothes) ? best.Id : defaultClothes;
        }

        return true;
    }

    public bool TryBuild(CharacterCustomizationModel model, out CustomCharacter? character)
    {
        character = null;

        var type = Load()?.CharacterTypes.FirstOrDefault(x => x.Id == model.CharacterType);

        if (type is null)
            return false;

        CatalogOption? Find(string key, int id)
        {
            if (!type.Options.TryGetValue(key, out var list))
                return null;

            return list.FirstOrDefault(x => x.Id == id);
        }

        var skin = Find("skinTone", model.SkinTone);
        var face = Find("faceType", model.FaceType);
        var hair = Find("hairStyle", model.HairStyle);
        var hairColor = Find("hairColor", model.HairColor);
        var eyeColor = Find("eyeColor", model.EyeColor);
        var facePaint = Find("facePaint", model.FacePaint);
        var clothes = Find("clothes", model.Clothes);

        if (skin is null || face is null || hair is null || hairColor is null || eyeColor is null || facePaint is null || clothes?.Pieces is null)
            return false;

        var customization = 0;

        if (type.Options.ContainsKey("facialHair"))
        {
            var facial = Find("facialHair", model.FacialHair);

            if (facial is null)
                return false;

            customization = facial.Value;
        }
        else if (type.Options.ContainsKey("wings"))
        {
            var wings = Find("wings", model.Wings);

            if (wings is null)
                return false;

            customization = wings.Value;
        }

        if (!clothes.Pieces.TryGetValue("chest", out var chest)
            || !clothes.Pieces.TryGetValue("legs", out var legs)
            || !clothes.Pieces.TryGetValue("feet", out var feet))
            return false;

        character = new CustomCharacter(
            type.Gender.Equals("Female", StringComparison.OrdinalIgnoreCase) ? "female" : "male",
            type.Model,
            face.Value,
            skin.Value,
            hair.Value,
            hairColor.Value,
            eyeColor.Value,
            facePaint.Value,
            customization,
            chest.Item,
            legs.Item,
            feet.Item,
            chest.TintId,
            legs.TintId,
            feet.TintId);

        return true;
    }
}
