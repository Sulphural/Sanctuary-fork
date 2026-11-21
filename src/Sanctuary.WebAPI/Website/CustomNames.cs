using System;
using System.Linq;

using Sanctuary.Core.Helpers;
using Sanctuary.Game;

namespace Sanctuary.WebAPI.Website;

public static class CustomNames
{
    private const int MinLength = 3;
    private const int MaxLength = 14;

    public static string? Validate(string? raw, IResourceManager resourceManager, CharacterNames names, out string firstName, out string lastName)
    {
        firstName = string.Empty;
        lastName = string.Empty;

        var parts = (raw ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (parts.Length == 0)
            return "empty";

        if (parts.Length > 2)
            return "words";

        firstName = parts[0];
        lastName = parts.Length == 2 ? parts[1] : string.Empty;

        if (firstName.Length < MinLength)
            return "firstShort";

        if (firstName.Length > MaxLength)
            return "firstLong";

        if (lastName.Length > 0 && lastName.Length < MinLength)
            return "lastShort";

        if (lastName.Length > MaxLength)
            return "lastLong";

        var fullName = lastName.Length > 0 ? $"{firstName} {lastName}" : firstName;

        if (CharacterNameHelper.ContainsIllegalCharacters(fullName))
            return "illegal";

        if (parts.Any(part => !names.IsReelPart(part) && resourceManager.NameFilter.Any(token => part.Contains(token, StringComparison.OrdinalIgnoreCase))))
            return "profane";

        return null;
    }
}
