using System.Collections.Generic;

using Sanctuary.Packet.Common;

namespace Sanctuary.WebAPI.Website;

public sealed record CharacterPreset(
    int Id,
    string Gender,
    int Model,
    int Head,
    int SkinTone,
    int Hair,
    int HairColor,
    int EyeColor,
    int ModelCustomization,
    int FacePaint,
    int ItemChest,
    int ItemLegs,
    int ItemFeet);

public static class CharacterPresets
{
    public static readonly IReadOnlyDictionary<int, CharacterPreset> All = new Dictionary<int, CharacterPreset>
    {
        [1] = new(1, "female", 61, 1, 3, 17, 86, 69, 7, 16, 413, 934, 414),
        [2] = new(2, "female", 60, 1, 3, 30, 24, 50, 0, 16, 1000, 1005, 1578),
        [3] = new(3, "male", 1, 1, 3, 4, 78, 63, 2, 0, 1057, 960, 954),
        [4] = new(4, "male", 2, 1, 3, 33, 27, 58, 13, 0, 962, 958, 420),
    };

    public static CharacterData ToCharacterData(CharacterPreset preset, string firstName, string lastName, int tintChest, int tintLegs, int tintFeet)
    {
        return new CharacterData
        {
            FirstName = firstName,
            LastName = lastName,
            Model = preset.Model,
            Head = preset.Head,
            SkinTone = preset.SkinTone,
            Hair = preset.Hair,
            HairColor = preset.HairColor,
            EyeColor = preset.EyeColor,
            ModelCustomization = preset.ModelCustomization,
            FacePaint = preset.FacePaint,
            ItemChest = preset.ItemChest,
            ItemLegs = preset.ItemLegs,
            ItemFeet = preset.ItemFeet,
            TintChest = tintChest,
            TintLegs = tintLegs,
            TintFeet = tintFeet
        };
    }
}
