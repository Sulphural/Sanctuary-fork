using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Sanctuary.Game;

namespace Sanctuary.WebAPI.Website;

public sealed class CharacterNames
{
    public static readonly string NamesFile = Path.Combine(ResourceManager.BaseDirectory, "CharacterCreate", "CharacterCreateNames.txt");

    public IReadOnlyList<string> FemaleFirstNames { get; }
    public IReadOnlyList<string> MaleFirstNames { get; }
    public IReadOnlyList<string> LastNamePrefixes { get; }
    public IReadOnlyList<string> LastNameSuffixes { get; }

    private readonly HashSet<string> _femaleFirstNames;
    private readonly HashSet<string> _maleFirstNames;
    private readonly HashSet<string> _lastNamePrefixes;
    private readonly HashSet<string> _lastNameSuffixes;

    public CharacterNames()
    {
        var female = new List<string>();
        var male = new List<string>();
        var prefixes = new List<string>();
        var suffixes = new List<string>();

        foreach (var line in File.ReadLines(NamesFile))
        {
            if (line.StartsWith('#'))
                continue;

            var parts = line.Split('^');

            if (parts.Length < 2 || string.IsNullOrWhiteSpace(parts[1]))
                continue;

            var key = parts[0];
            var name = parts[1].Trim();

            if (key.StartsWith("female.first.name.", StringComparison.Ordinal))
                female.Add(name);
            else if (key.StartsWith("male.first.name.", StringComparison.Ordinal))
                male.Add(name);
            else if (key.StartsWith("last.name.prefix.", StringComparison.Ordinal))
                prefixes.Add(name);
            else if (key.StartsWith("last.name.suffix.", StringComparison.Ordinal))
                suffixes.Add(name);
        }

        FemaleFirstNames = Sorted(female);
        MaleFirstNames = Sorted(male);
        LastNamePrefixes = Sorted(prefixes);
        LastNameSuffixes = Sorted(suffixes);

        _femaleFirstNames = [.. FemaleFirstNames];
        _maleFirstNames = [.. MaleFirstNames];
        _lastNamePrefixes = [.. LastNamePrefixes];
        _lastNameSuffixes = [.. LastNameSuffixes];
    }

    public IReadOnlyList<string> FirstNamesFor(string gender) => gender == "male" ? MaleFirstNames : FemaleFirstNames;

    public bool IsValidFirstName(string gender, string firstName) => (gender == "male" ? _maleFirstNames : _femaleFirstNames).Contains(firstName);

    public bool IsValidPrefix(string prefix) => _lastNamePrefixes.Contains(prefix);

    public bool IsValidSuffix(string suffix) => _lastNameSuffixes.Contains(suffix);

    public static string ComposeLastName(string prefix, string suffix)
    {
        var lastName = prefix + suffix;

        return lastName.Length == 0 ? lastName : char.ToUpperInvariant(lastName[0]) + lastName[1..];
    }

    private static List<string> Sorted(IEnumerable<string> names) => [.. names.Distinct(StringComparer.Ordinal).Order(StringComparer.OrdinalIgnoreCase)];
}
