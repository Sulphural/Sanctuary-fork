using System;
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
        var firstNames = names.FirstNamesFor(male ? "male" : "female");

        for (var attempt = 0; attempt < 50; attempt++)
        {
            var firstName = firstNames[Random.Shared.Next(firstNames.Count)];
            var lastName = CharacterNames.ComposeLastName(
                names.LastNamePrefixes[Random.Shared.Next(names.LastNamePrefixes.Count)],
                names.LastNameSuffixes[Random.Shared.Next(names.LastNameSuffixes.Count)]);

            if (!await IsTakenAsync(dbContext, firstName, lastName, 0, cancellationToken))
                return (firstName, lastName);
        }

        return null;
    }
}
