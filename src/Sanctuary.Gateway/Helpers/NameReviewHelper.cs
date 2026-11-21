using System.Linq;

using Sanctuary.Database;

namespace Sanctuary.Gateway.Helpers;

public static class NameReviewHelper
{
    public static bool IsTaken(DatabaseContext dbContext, string firstName, string lastName, ulong exceptCharacterId)
    {
        var lastNameOrNull = lastName.Length == 0 ? null : lastName;

        return dbContext.Characters.Any(x => x.Id != exceptCharacterId && x.FirstName == firstName
                && (x.LastName == lastName || x.LastName == lastNameOrNull))
            || dbContext.NameReviews.Any(x => x.CharacterId != exceptCharacterId
                && x.RequestedFirstName == firstName && x.RequestedLastName == lastName);
    }
}
