using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.EntityFrameworkCore;

using Sanctuary.Database;
using Sanctuary.Game;

namespace Sanctuary.WebAPI.Website;

public static class Usernames
{
    private const int MaxLength = 50;
    private const int SuggestionCount = 3;
    private const string FallbackBase = "freerealms";
    private const string Letters = "abcdefghijklmnopqrstuvwxyz";
    private const string Digits = "0123456789";

    public static bool IsBlocked(string username, IResourceManager resourceManager)
    {
        var lettersOnly = new string(username.Where(char.IsLetter).ToArray());
        var readAsLetters = new string(username.Select(ReadAsLetter).Where(char.IsLetter).ToArray());

        return resourceManager.NameFilter.Any(token =>
            username.Contains(token, StringComparison.OrdinalIgnoreCase) ||
            lettersOnly.Contains(token, StringComparison.OrdinalIgnoreCase) ||
            readAsLetters.Contains(token, StringComparison.OrdinalIgnoreCase));
    }

    private static char ReadAsLetter(char c) => c switch
    {
        '0' => 'o',
        '1' => 'i',
        '3' => 'e',
        '4' => 'a',
        '5' => 's',
        '7' => 't',
        '8' => 'b',
        _ => c
    };

    public static async Task<List<string>> SuggestAsync(DatabaseContext dbContext, string username, IResourceManager resourceManager, CancellationToken cancellationToken)
    {
        var baseName = new string(username.Where(c => char.IsAsciiLetterOrDigit(c) || c == '_' || c == '.').ToArray());

        if (baseName.Length < 3 || IsBlocked(baseName, resourceManager))
            baseName = FallbackBase;

        var candidates = new List<string>();

        for (var attempt = 0; attempt < 60 && candidates.Count < SuggestionCount * 4; attempt++)
        {
            var candidate = Decorate(baseName);

            if (!candidates.Contains(candidate) && !IsBlocked(candidate, resourceManager))
                candidates.Add(candidate);
        }

        var taken = await dbContext.Users
            .Where(x => candidates.Contains(x.Username))
            .Select(x => x.Username)
            .ToListAsync(cancellationToken);

        return candidates.Where(x => !taken.Contains(x)).Take(SuggestionCount).ToList();
    }

    private static string Decorate(string baseName)
    {
        var extra = RandomNumberGenerator.GetInt32(2, 5);
        var trimmed = baseName.Length + extra > MaxLength ? baseName[..(MaxLength - extra)] : baseName;
        var stem = trimmed.TrimEnd(Digits.ToCharArray());

        if (stem.Length < 3)
            stem = trimmed;

        var keep = RandomNumberGenerator.GetInt32(stem.Length, trimmed.Length + 1);
        var chars = new char[extra];

        for (var i = 0; i < extra; i++)
        {
            var pool = i % 2 == 0 ? Letters : Digits;
            chars[i] = pool[RandomNumberGenerator.GetInt32(pool.Length)];
        }

        RandomNumberGenerator.Shuffle(chars.AsSpan());

        return trimmed[..keep] + new string(chars);
    }
}
