namespace MonoRogue.Data;

using System.Collections.Immutable;
using System.Text.Json;

/// <summary>
/// Immutable registry of all game data blueprints, loaded once at startup.
/// The registry is shared read-only across all game loop ticks —
/// it is never modified after construction.
/// </summary>
public sealed record DataRegistry(
    ImmutableDictionary<string, EntityTemplate> Templates,
    ImmutableDictionary<string, BackgroundTemplate> Backgrounds,
    ImmutableDictionary<string, FactionTemplate> Factions,
    ImmutableDictionary<string, QuestTemplate> Quests
)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling         = JsonCommentHandling.Skip,
        AllowTrailingCommas         = true,
    };

    /// <summary>
    /// Deserializes a directory of JSON files into immutable registries.
    /// File names should prefix the type (e.g., blueprints_, backgrounds_, factions_, quests_).
    /// </summary>
    public static DataRegistry LoadFrom(string directoryPath)
    {
        var entities = new Dictionary<string, EntityTemplate>();
        var backgrounds = new Dictionary<string, BackgroundTemplate>();
        var factions = new Dictionary<string, FactionTemplate>();
        var quests = new Dictionary<string, QuestTemplate>();

        // Load blueprints (entities)
        var blueprintFiles = Directory.GetFiles(directoryPath, "blueprint*.json", SearchOption.AllDirectories);
        foreach (var file in blueprintFiles)
        {
            var json = File.ReadAllText(file);
            var list = JsonSerializer.Deserialize<List<EntityTemplate>>(json, JsonOptions) ?? [];
            foreach (var t in list) entities[t.Id] = t;
        }

        // Load backgrounds
        var backgroundFiles = Directory.GetFiles(directoryPath, "background*.json", SearchOption.AllDirectories);
        foreach (var file in backgroundFiles)
        {
            var json = File.ReadAllText(file);
            var bgs = JsonSerializer.Deserialize<List<BackgroundTemplate>>(json, JsonOptions) ?? [];
            foreach (var b in bgs) backgrounds[b.Id] = b;
        }

        // Load factions
        var factionFiles = Directory.GetFiles(directoryPath, "faction*.json", SearchOption.AllDirectories);
        foreach (var file in factionFiles)
        {
            var json = File.ReadAllText(file);
            var facs = JsonSerializer.Deserialize<List<FactionTemplate>>(json, JsonOptions) ?? [];
            foreach (var f in facs) factions[f.Id] = f;
        }

        // Load quests
        var questFiles = Directory.GetFiles(directoryPath, "quest*.json", SearchOption.AllDirectories);
        foreach (var file in questFiles)
        {
            var json = File.ReadAllText(file);
            var qs = JsonSerializer.Deserialize<List<QuestTemplate>>(json, JsonOptions) ?? [];
            foreach (var q in qs) quests[q.Id] = q;
        }

        return new DataRegistry(
            entities.ToImmutableDictionary(),
            backgrounds.ToImmutableDictionary(),
            factions.ToImmutableDictionary(),
            quests.ToImmutableDictionary()
        );
    }

    /// <summary>Returns an empty registry for testing.</summary>
    public static DataRegistry Empty() =>
        new(ImmutableDictionary<string, EntityTemplate>.Empty,
            ImmutableDictionary<string, BackgroundTemplate>.Empty,
            ImmutableDictionary<string, FactionTemplate>.Empty,
            ImmutableDictionary<string, QuestTemplate>.Empty);
}
