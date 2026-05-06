namespace MonoRogue.Data;

using System.Collections.Immutable;
using System.Text.Json;
using MonoRogue.Core.Model;

/// <summary>
/// Immutable registry of all game data blueprints, loaded once at startup.
/// The registry is shared read-only across all game loop ticks —
/// it is never modified after construction.
/// </summary>
public sealed record DataRegistry(
    ImmutableDictionary<string, EntityTemplate> Templates,
    ImmutableDictionary<string, BackgroundTemplate> Backgrounds,
    ImmutableDictionary<string, FactionTemplate> Factions,
    ImmutableDictionary<string, QuestTemplate> Quests,
    ImmutableDictionary<string, AreaTemplate> Areas
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
        var areas = new Dictionary<string, AreaTemplate>();

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

        // Load areas
        var areaFiles = Directory.GetFiles(directoryPath, "area*.json", SearchOption.AllDirectories);
        foreach (var file in areaFiles)
        {
            var json = File.ReadAllText(file);
            var areaList = JsonSerializer.Deserialize<List<AreaTemplate>>(json, JsonOptions) ?? [];
            foreach (var a in areaList) areas[a.Id] = a;
        }

        return new DataRegistry(
            entities.ToImmutableDictionary(),
            backgrounds.ToImmutableDictionary(),
            factions.ToImmutableDictionary(),
            quests.ToImmutableDictionary(),
            areas.ToImmutableDictionary()
        );
    }

    /// <summary>Returns an empty registry for testing.</summary>
    public static DataRegistry Empty() =>
        new(ImmutableDictionary<string, EntityTemplate>.Empty,
            ImmutableDictionary<string, BackgroundTemplate>.Empty,
            ImmutableDictionary<string, FactionTemplate>.Empty,
            ImmutableDictionary<string, QuestTemplate>.Empty,
            ImmutableDictionary<string, AreaTemplate>.Empty);

    /// <summary>Converts a QuestTemplate (JSON data) into a runtime Quest record.</summary>
    public static Quest BuildQuest(QuestTemplate tpl)
    {
        var reqItems = tpl.RequiredItems?.ToImmutableDictionary()
                       ?? ImmutableDictionary<string, int>.Empty;

        ImmutableArray<QuestObjective>? objectives = null;
        if (tpl.Objectives?.Length > 0)
            objectives = tpl.Objectives
                .Select(o => new QuestObjective(o.Type, o.TargetId, o.RequiredCount))
                .ToImmutableArray();

        return new Quest(
            tpl.Id, tpl.Name, tpl.Description, reqItems,
            tpl.CompletionText ?? "",
            Objectives:  objectives,
            TurnInNpcId: tpl.TurnInNpcId);
    }
}
