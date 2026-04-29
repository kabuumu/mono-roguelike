namespace MonoRogue.Data;

/// <summary>
/// JSON-deserialized area/biome definition. Each entry in areas.json describes
/// a procedurally generated biome region placed around the village on the world map.
/// </summary>
public sealed record AreaTemplate(
    string Id,
    string Name,
    /// <summary>Cardinal direction from village: "north", "east", "south", "west".</summary>
    string Direction = "north",
    /// <summary>"wilderness" or "dungeon"</summary>
    string GeneratorType = "wilderness",
    float MapWidthMultiplier  = 1.5f,
    float MapHeightMultiplier = 1.5f,
    string BaseTile           = "tile_floor",
    string WallTile           = "tile_wall",
    /// <summary>Fraction of tiles filled with wallTile (wilderness only).</summary>
    float WallDensity         = 0.25f,
    /// <summary>[min, max] number of clearings (wilderness only).</summary>
    int[]? ClearingCount      = null,
    /// <summary>[min, max] clearing radius in tiles (wilderness only).</summary>
    int[]? ClearingSize       = null,
    AreaSpawnEntry[]? Enemies = null,
    AreaItemEntry[]? Items    = null,
    AreaSpawnEntry[]? Npcs    = null,
    string EntryMessage       = ""
);

/// <summary>An enemy or NPC spawn entry: template ID + count range [min, max].</summary>
public sealed record AreaSpawnEntry(
    string TemplateId,
    int[]? Count = null
);

/// <summary>An item spawn entry: template ID + per-clearing chance.</summary>
public sealed record AreaItemEntry(
    string TemplateId,
    float Chance = 0.3f
);
