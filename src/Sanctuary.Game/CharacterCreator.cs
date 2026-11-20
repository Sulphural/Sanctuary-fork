using System.Linq;

using Microsoft.Extensions.Logging;

using Sanctuary.Database;
using Sanctuary.Database.Entities;
using Sanctuary.Packet.Common;

namespace Sanctuary.Game;

public enum CharacterCreateResult
{
    Success = 1,
    InvalidAppearance = 2,
    Failed = 3,
    NameTaken = 4,
    MaxCharacters = 6
}

public sealed class CharacterCreationSettings
{
    public int DefaultTitleId { get; set; }
    public int DefaultProfileId { get; set; }

    public int StartingCoins { get; set; }
    public int StartingStationCash { get; set; }

    public bool UnlockAllTitles { get; set; }
    public bool UnlockAllProfiles { get; set; }
}

public static class CharacterCreator
{
    public static CharacterCreateResult Create(
        IResourceManager resourceManager,
        DatabaseContext dbContext,
        CharacterCreationSettings settings,
        ulong userId,
        CharacterData characterData,
        ILogger logger,
        out ulong characterId)
    {
        characterId = 0;

        // TODO: Validate name against CharacterCreateItemClothingSets.txt, CharacterCreateItemMappings.txt

        if (!resourceManager.Models.TryGetValue(characterData.Model, out var model))
        {
            logger.LogError("Failed to create character, invalid character model. {id}", characterData.Model);
            return CharacterCreateResult.InvalidAppearance;
        }

        if (!resourceManager.HeadMappings.TryGetValue(characterData.Head, out var head))
        {
            logger.LogError("Failed to create character, invalid character head. {id}", characterData.Head);
            return CharacterCreateResult.InvalidAppearance;
        }

        if (!resourceManager.HairMappings.TryGetValue(characterData.Hair, out var hair))
        {
            logger.LogError("Failed to create character, invalid character hair. {id}", characterData.Hair);
            return CharacterCreateResult.InvalidAppearance;
        }

        if (!resourceManager.SkinToneMappings.TryGetValue(characterData.SkinTone, out var skinTone))
        {
            logger.LogError("Failed to create character, invalid character skin tone. {id}", characterData.SkinTone);
            return CharacterCreateResult.InvalidAppearance;
        }

        if (!resourceManager.FacePaintMappings.TryGetValue(characterData.FacePaint, out var facePaint))
        {
            logger.LogError("Failed to create character, invalid character face paint. {id}", characterData.FacePaint);
            return CharacterCreateResult.InvalidAppearance;
        }

        string? modelCustomization = null;

        if (characterData.ModelCustomization > 0 && !resourceManager.ModelCustomizationMappings.TryGetValue(characterData.ModelCustomization, out modelCustomization))
        {
            logger.LogError("Failed to create character, invalid character model customization. {id}", characterData.ModelCustomization);
            return CharacterCreateResult.InvalidAppearance;
        }

        if (!resourceManager.ClientItemDefinitions.TryGetValue(characterData.ItemFeet, out var itemFeet))
        {
            logger.LogError("Failed to create character, invalid character item feet. {id}", characterData.ItemFeet);
            return CharacterCreateResult.InvalidAppearance;
        }

        if (!resourceManager.ClientItemDefinitions.TryGetValue(characterData.ItemLegs, out var itemLegs))
        {
            logger.LogError("Failed to create character, invalid character item legs. {id}", characterData.ItemLegs);
            return CharacterCreateResult.InvalidAppearance;
        }

        if (!resourceManager.ClientItemDefinitions.TryGetValue(characterData.ItemChest, out var itemChest))
        {
            logger.LogError("Failed to create character, invalid character item chest. {id}", characterData.ItemChest);
            return CharacterCreateResult.InvalidAppearance;
        }

        var nameTaken = dbContext.Characters.Any(x => x.FirstName == characterData.FirstName && x.LastName == characterData.LastName);

        if (nameTaken)
        {
            logger.LogInformation("Failed to create character, name taken.");
            return CharacterCreateResult.NameTaken;
        }

        var dbUser = dbContext.Users.SingleOrDefault(x => x.Id == userId);

        if (dbUser is null)
        {
            logger.LogInformation("Failed to create character, unknown user.");
            return CharacterCreateResult.Failed;
        }

        var characterCount = dbContext.Characters.Count(x => x.UserId == userId);

        if (characterCount >= dbUser.MaxCharacters)
        {
            logger.LogInformation("Failed to create character, max characters.");
            return CharacterCreateResult.MaxCharacters;
        }

        var dbCharacter = new DbCharacter
        {
            FirstName = characterData.FirstName ?? string.Empty,
            LastName = characterData.LastName ?? string.Empty,
            Model = characterData.Model,
            Head = head,
            HeadId = characterData.Head,
            Hair = hair,
            HairId = characterData.Hair,
            ModelCustomization = modelCustomization,
            ModelCustomizationId = characterData.ModelCustomization,
            FacePaint = facePaint,
            FacePaintId = characterData.FacePaint,
            SkinTone = skinTone,
            SkinToneId = characterData.SkinTone,
            EyeColor = characterData.EyeColor,
            HairColor = characterData.HairColor,

            Gender = model.Gender,

            MembershipStatus = dbUser.IsMember ? 2 : 0,

            UserId = userId
        };

        dbCharacter.Coins = settings.StartingCoins;
        dbCharacter.StationCash = settings.StartingStationCash;

        if (settings.DefaultTitleId > 0)
        {
            if (resourceManager.PlayerTitles.TryGetValue(settings.DefaultTitleId, out var playerTitle))
            {
                var dbCharacterTitle = new DbTitle
                {
                    Id = playerTitle.Id
                };

                dbCharacter.Titles.Add(dbCharacterTitle);

                dbCharacter.ActiveTitleId = playerTitle.Id;
            }
        }

        if (!resourceManager.Profiles.TryGetValue(settings.DefaultProfileId, out var profileData))
        {
            logger.LogError("Failed to create character, invalid default profile.");
            return CharacterCreateResult.Failed;
        }

        var dbProfile = new DbProfile
        {
            Id = profileData.Id,
            Level = 20
        };

        dbCharacter.ActiveProfileId = dbProfile.Id;

        var chestItem = new DbItem
        {
            Id = dbCharacter.Items.Count + 1,
            Definition = itemChest.Id,
            Tint = characterData.TintChest,
            Count = 1
        };

        dbCharacter.Items.Add(chestItem);

        var legItem = new DbItem
        {
            Id = dbCharacter.Items.Count + 1,
            Definition = itemLegs.Id,
            Tint = characterData.TintLegs,
            Count = 1
        };

        dbCharacter.Items.Add(legItem);

        var feetItem = new DbItem
        {
            Id = dbCharacter.Items.Count + 1,
            Definition = itemFeet.Id,
            Tint = characterData.TintFeet,
            Count = 1
        };

        dbCharacter.Items.Add(feetItem);

        dbProfile.Items.Add(chestItem);
        dbProfile.Items.Add(legItem);
        dbProfile.Items.Add(feetItem);

        dbCharacter.Profiles.Add(dbProfile);

        if (settings.UnlockAllTitles)
            UnlockAllTitles(resourceManager, dbCharacter);

        if (settings.UnlockAllProfiles)
            UnlockAllProfiles(resourceManager, dbCharacter, chestItem, legItem, feetItem);

        dbContext.Characters.Add(dbCharacter);

        if (dbContext.SaveChanges() <= 0)
        {
            logger.LogError("Failed to create character, database save failed.");
            return CharacterCreateResult.Failed;
        }

        characterId = dbCharacter.Id;

        return CharacterCreateResult.Success;
    }

    private static void UnlockAllTitles(IResourceManager resourceManager, DbCharacter dbCharacter)
    {
        foreach (var playerTitle in resourceManager.PlayerTitles.Values)
        {
            if (dbCharacter.Titles.Any(x => x.Id == playerTitle.Id))
                continue;

            var dbCharacterTitle = new DbTitle
            {
                Id = playerTitle.Id
            };

            dbCharacter.Titles.Add(dbCharacterTitle);
        }
    }

    private static void UnlockAllProfiles(IResourceManager resourceManager, DbCharacter dbCharacter, DbItem chestItem, DbItem legItem, DbItem feetItem)
    {
        foreach (var profileData in resourceManager.Profiles.Values)
        {
            if (dbCharacter.Profiles.Any(x => x.Id == profileData.Id))
                continue;

            var dbProfile = new DbProfile
            {
                Id = profileData.Id,
                Level = 20
            };

            // TODO
            if (profileData.DefaultItems.Count == 0)
            {
                dbProfile.Items.Add(chestItem);
                dbProfile.Items.Add(legItem);
                dbProfile.Items.Add(feetItem);
            }
            else
            {
                foreach (var defaultItemId in profileData.DefaultItems)
                {
                    if (!resourceManager.ClientItemDefinitions.TryGetValue(defaultItemId, out var defaultClientItemDefinition))
                        continue;

                    if (defaultClientItemDefinition.GenderUsage != 0 && defaultClientItemDefinition.GenderUsage != dbCharacter.Gender)
                        continue;

                    var dbItem = dbCharacter.Items.SingleOrDefault(x => x.Definition == defaultItemId);

                    if (dbItem is null)
                    {
                        dbItem = new DbItem
                        {
                            Id = dbCharacter.Items.Count + 1,
                            Definition = defaultClientItemDefinition.Id,
                            Tint = defaultClientItemDefinition.Icon.TintId,
                            Count = 1
                        };

                        dbCharacter.Items.Add(dbItem);
                    }

                    dbProfile.Items.Add(dbItem);
                }
            }

            dbCharacter.Profiles.Add(dbProfile);
        }
    }
}
