using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.EntityFrameworkCore;

using Sanctuary.Database;

namespace Sanctuary.WebAPI.Website;

public static class PendingNames
{
    public static bool NeedsReview(CharacterNames names, string firstName, string lastName)
    {
        return !names.IsReelPart(firstName) || (lastName.Length > 0 && !names.IsReelPart(lastName));
    }

    public static Task<bool> IsHeldAsync(DatabaseContext dbContext, string firstName, string lastName, CancellationToken cancellationToken)
    {
        return dbContext.NameReviews.AnyAsync(x => x.RequestedFirstName == firstName && x.RequestedLastName == lastName, cancellationToken);
    }

    public static async Task<bool> IsTakenAsync(DatabaseContext dbContext, string firstName, string lastName, ulong exceptCharacterId, CancellationToken cancellationToken)
    {
        var lastNameOrNull = lastName.Length == 0 ? null : lastName;

        return await dbContext.Characters.AnyAsync(x => x.Id != exceptCharacterId && x.FirstName == firstName
                && (x.LastName == lastName || x.LastName == lastNameOrNull), cancellationToken)
            || await dbContext.NameReviews.AnyAsync(x => x.CharacterId != exceptCharacterId
                && x.RequestedFirstName == firstName && x.RequestedLastName == lastName, cancellationToken);
    }

    public static async Task<(string FirstName, string LastName)?> PickFreeReelNameAsync(DatabaseContext dbContext, CharacterNames names, bool male, CancellationToken cancellationToken)
    {
        var parts = await PickFreeReelPartsAsync(dbContext, names, male, cancellationToken);

        return parts is null ? null : (parts.Value.FirstName, CharacterNames.ComposeLastName(parts.Value.Prefix, parts.Value.Suffix));
    }

    public static async Task<(string FirstName, string Prefix, string Suffix)?> PickFreeReelPartsAsync(DatabaseContext dbContext, CharacterNames names, bool male, CancellationToken cancellationToken)
    {
        var firstNames = names.FirstNamesFor(male ? "male" : "female");

        for (var attempt = 0; attempt < 50; attempt++)
        {
            var firstName = firstNames[Random.Shared.Next(firstNames.Count)];
            var prefix = names.LastNamePrefixes[Random.Shared.Next(names.LastNamePrefixes.Count)];
            var suffix = names.LastNameSuffixes[Random.Shared.Next(names.LastNameSuffixes.Count)];

            if (!await IsTakenAsync(dbContext, firstName, CharacterNames.ComposeLastName(prefix, suffix), 0, cancellationToken))
                return (firstName, prefix, suffix);
        }

        return null;
    }

    public static async Task<bool> IsFreeReelNameAsync(DatabaseContext dbContext, CharacterNames names, bool male, string? firstName, string? prefix, string? suffix, CancellationToken cancellationToken)
    {
        if (firstName is null || prefix is null || suffix is null)
            return false;

        if (!names.IsValidFirstName(male ? "male" : "female", firstName) || !names.IsValidPrefix(prefix) || !names.IsValidSuffix(suffix))
            return false;

        return !await IsTakenAsync(dbContext, firstName, CharacterNames.ComposeLastName(prefix, suffix), 0, cancellationToken);
    }

    public static async Task<List<(string FirstName, string Prefix, string Suffix)>> SuggestReelNamesAsync(DatabaseContext dbContext, CharacterNames names, bool male, string firstName, string prefix, string suffix, CancellationToken cancellationToken)
    {
        var firstNames = names.FirstNamesFor(male ? "male" : "female");
        var wanted = new (Func<(string, string, string)> Make, int Count)[]
        {
            (() => (firstName, Pick(names.LastNamePrefixes), suffix), 2),
            (() => (firstName, prefix, Pick(names.LastNameSuffixes)), 1),
            (() => (firstName, Pick(names.LastNamePrefixes), Pick(names.LastNameSuffixes)), 1),
            (() => (Pick(firstNames), prefix, suffix), 1)
        };

        var suggestions = new List<(string FirstName, string Prefix, string Suffix)>();

        foreach (var (make, count) in wanted)
        {
            var found = 0;

            for (var attempt = 0; attempt < 15 && found < count; attempt++)
            {
                var candidate = make();
                var lastName = CharacterNames.ComposeLastName(candidate.Item2, candidate.Item3);

                if (suggestions.Any(x => x.FirstName == candidate.Item1 && CharacterNames.ComposeLastName(x.Prefix, x.Suffix) == lastName))
                    continue;

                if (await IsTakenAsync(dbContext, candidate.Item1, lastName, 0, cancellationToken))
                    continue;

                suggestions.Add(candidate);
                found++;
            }
        }

        return suggestions;
    }

    private static string Pick(IReadOnlyList<string> list) => list[Random.Shared.Next(list.Count)];
}
