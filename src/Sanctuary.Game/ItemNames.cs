using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

using Microsoft.Extensions.Logging;

using Sanctuary.Game.Helpers;

namespace Sanctuary.Game;

public sealed class ItemNames
{
    private readonly ILogger<ItemNames> _logger;
    private readonly IResourceManager _resourceManager;
    private readonly Lazy<Dictionary<int, string>> _names;

    public ItemNames(ILogger<ItemNames> logger, IResourceManager resourceManager)
    {
        _logger = logger;
        _resourceManager = resourceManager;
        _names = new Lazy<Dictionary<int, string>>(Load);
    }

    public string NameOf(int definition) => _names.Value.TryGetValue(definition, out var name) ? name : $"Item {definition}";

    public bool IsGrantable(int definition)
    {
        return _resourceManager.ClientItemDefinitions.TryGetValue(definition, out var item)
            && item.Type != ItemDefinitionHelper.PetItemType
            && item.Type != ItemDefinitionHelper.MountItemType;
    }

    public IEnumerable<(int Id, string Name)> Search(string query, int take)
    {
        if (int.TryParse(query, out var id))
            return IsGrantable(id) ? [(id, NameOf(id))] : [];

        return _names.Value
            .Where(x => x.Value.Contains(query, StringComparison.OrdinalIgnoreCase) && IsGrantable(x.Key))
            .OrderBy(x => x.Value.Length)
            .ThenBy(x => x.Value, StringComparer.OrdinalIgnoreCase)
            .Take(take)
            .Select(x => (x.Key, x.Value));
    }

    private Dictionary<int, string> Load()
    {
        var names = new Dictionary<int, string>();

        foreach (var file in new[] { "ClientItemDefinitions.json", "UnlistedClientItemDefinitions.json" })
        {
            var path = Path.Combine(ResourceManager.BaseDirectory, file);

            if (!File.Exists(path))
                continue;

            try
            {
                using var stream = File.OpenRead(path);
                using var document = JsonDocument.Parse(stream);

                foreach (var element in document.RootElement.EnumerateArray())
                {
                    if (!element.TryGetProperty("Id", out var idElement) || !idElement.TryGetInt32(out var id))
                        continue;

                    if (element.TryGetProperty("Comment", out var comment) && comment.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(comment.GetString()))
                        names.TryAdd(id, comment.GetString()!.Trim());
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not read item names from {File}.", path);
            }
        }

        return names;
    }
}
