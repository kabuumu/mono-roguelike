namespace MonoRogue.Core.Generation;

using System.Collections.Generic;
using System.Collections.Immutable;
using MonoRogue.Core;
using MonoRogue.Core.Model;
using MonoRogue.Core.Systems;
using MonoRogue.Data;

// ── Generation parameters ─────────────────────────────────────────────────────

/// <summary>Immutable seed bag for VillageGenerator.Generate() — keeps the
/// function pure and the parameter set testable in isolation.</summary>
public sealed record VillageGenParams(
    int Seed,
    int MapWidth,
    int MapHeight,
    int TargetBuildingCount = 5,
    int MinBuildingSize     = 4,
    int MaxBuildingSize     = 8,
    /// <summary>How many of the total buildings must land inside the near-zone
    /// so the player is surrounded by structures from turn one.</summary>
    int GuaranteedNearCount = 3,
    /// <summary>Half-width/height (in tiles) of the near-zone box centred on
    /// the forge. Keep ≥ MaxBuildingSize so at least one building can fit.</summary>
    int NearZoneRadius      = 14
);

// ── Generator ─────────────────────────────────────────────────────────────────

public static class VillageGenerator
{
    // ── Spawn entry (kept for any external callers) ───────────────────────────

    public readonly record struct VillageSpawn(string TemplateId, Position Position);

    public sealed record VillageMap(
        int Width,
        int Height,
        /// <summary>Template ID for each tile (row-major).</summary>
        ImmutableArray<string> TileIds,
        ImmutableArray<VillageSpawn> NpcSpawns,
        Position PlayerSpawn
    )
    {
        public string TileAt(int x, int y) =>
            x >= 0 && x < Width && y >= 0 && y < Height
                ? TileIds[y * Width + x]
                : "tile_stone_wall";
    }

    // ── Public API ────────────────────────────────────────────────────────────

    public static GameState Generate(
        VillageGenParams genParams,
        DataRegistry     registry,
        string           backgroundId,
        Entity           player)
    {
        int width  = genParams.MapWidth;
        int height = genParams.MapHeight;
        var rng    = new SeededRandom(genParams.Seed);

        var entities    = ImmutableDictionary.CreateBuilder<Guid, Entity>();
        var walkability = new bool[width * height];

        void SpawnAt(string templateId, int x, int y)
        {
            if (!registry.Templates.TryGetValue(templateId, out var template)) return;
            var entity = SpawnSystem.BuildEntity(template, new Position(x, y));
            entities[entity.Id] = entity;
            if (template.BlocksMovement && template.IsTile)
                walkability[y * width + x] = false;
        }

        // ── 1. Plan building layout before filling tiles ──────────────────────
        // Buildings are computed first so the grass fill can skip their footprints,
        // ensuring each cell has exactly one tile entity (no grass hidden under walls).
        int plazaCx = (width - 2) / 2;
        int plazaCy = height / 2;
        // River runs 20 tiles east of the forge — reachable with a short walk
        int riverX  = Math.Min(plazaCx + 20, width - 3);

        var buildings   = PlaceBuildings(genParams, plazaCx, plazaCy, rng.Split("placement"));
        var buildingRng = rng.Split("buildings");
        var villagerRng = rng.Split("villagers");

        bool InAnyBuilding(int x, int y) =>
            buildings.Any(r => x >= r.X && x < r.X + r.Width && y >= r.Y && y < r.Y + r.Height);

        // ── 2. Base layer: grass + river (skip building footprints) ───────────
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width;  x++)
        {
            walkability[y * width + x] = true;
            if (x == riverX || x == riverX + 1)
                SpawnAt("env_river_tile", x, y);
            else if (!InAnyBuilding(x, y))
                SpawnAt("tile_grass", x, y);
        }

        // ── 3. Central forge + blacksmith + starting items ───────────────────
        SpawnAt("env_forge",      plazaCx,     plazaCy);
        SpawnAt("npc_blacksmith", plazaCx - 1, plazaCy);
        SpawnAt("item_empty_bucket", plazaCx, plazaCy + 1);

        // ── 4. Stamp buildings ────────────────────────────────────────────────
        foreach (var rect in buildings)
            StampBuilding(rect, SpawnAt, buildingRng);

        // ── 4. One villager per building ──────────────────────────────────────
        foreach (var rect in buildings)
        {
            int vx = villagerRng.Next(rect.X + 1, rect.X + rect.Width  - 1);
            int vy = villagerRng.Next(rect.Y + 1, rect.Y + rect.Height - 1);
            SpawnAt("npc_villager", vx, vy);
        }

        // ── 5. Wilderness: forest perimeter + creature spawns ─────────────────
        PlaceWilderness(
            genParams, plazaCx, plazaCy, riverX, buildings,
            entities, walkability, registry, rng.Split("wilderness"));

        // ── 6. Player placement ───────────────────────────────────────────────
        var playerPos = new Position(plazaCx - 2, plazaCy);
        player = player with
        {
            Spatial = player.Spatial != null
                ? player.Spatial with { Position = playerPos }
                : new SpatialComponent(playerPos, true)
        };
        entities[player.Id] = player;

        // ── 7. Background (quests + starting items) ───────────────────────────
        var activeQuests = ImmutableList.CreateBuilder<Quest>();

        if (backgroundId != null && registry.Backgrounds.TryGetValue(backgroundId, out var bg))
        {
            player = player with { Background = new BackgroundComponent(backgroundId) };

            if (bg.StartingQuests != null)
            {
                foreach (var qId in bg.StartingQuests)
                {
                    if (registry.Quests.TryGetValue(qId, out var qTpl))
                    {
                        var reqItems = qTpl.RequiredItems?.ToImmutableDictionary()
                                       ?? ImmutableDictionary<string, int>.Empty;

                        ImmutableArray<QuestObjective>? objectives = null;
                        if (qTpl.Objectives?.Length > 0)
                        {
                            objectives = qTpl.Objectives
                                .Select(o => new QuestObjective(o.Type, o.TargetId, o.RequiredCount))
                                .ToImmutableArray();
                        }

                        activeQuests.Add(new Quest(
                            qTpl.Id, qTpl.Name, qTpl.Description, reqItems,
                            qTpl.CompletionText ?? "",
                            Objectives:  objectives,
                            TurnInNpcId: qTpl.TurnInNpcId));
                    }
                }
            }

            if (bg.StartingItems is not null)
            {
                var inventoryItems     = ImmutableArray.CreateBuilder<Guid>();
                var inventoryResources = ImmutableDictionary.CreateBuilder<string, int>();

                if (player.Inventory?.Resources != null)
                    inventoryResources.AddRange(player.Inventory.Resources);

                foreach (var kvp in bg.StartingItems)
                {
                    if (!registry.Templates.TryGetValue(kvp.Key, out var itemTpl)) continue;

                    if (itemTpl.IsItem && (itemTpl.ItemType == "Weapon" || itemTpl.ItemType == "Armor"))
                    {
                        var item = SpawnSystem.BuildEntity(itemTpl, new Position(-1, -1)) with { Spatial = null };
                        entities[item.Id] = item;
                        inventoryItems.Add(item.Id);
                    }
                    else
                    {
                        inventoryResources[kvp.Key] = inventoryResources.TryGetValue(kvp.Key, out var cur)
                            ? cur + kvp.Value
                            : kvp.Value;
                    }
                }

                player = player with
                {
                    Inventory = (player.Inventory ?? new InventoryComponent([], 10)) with
                    {
                        Items     = inventoryItems.ToImmutable(),
                        Resources = inventoryResources.ToImmutable()
                    }
                };
            }
        }

        entities[player.Id] = player;

        // ── 8. Populate shop inventories for NPCs that have shopInventory ──────
        PopulateShops(entities, registry);

        var state = GameState.Empty(width, height) with
        {
            Entities       = entities.ToImmutable(),
            WalkabilityMap = [.. walkability],
            ActiveQuests   = activeQuests.ToImmutable(),
            PlayerEntityId = player.Id,
            FloorLevel     = 0,
        };

        return state.AppendMessage("Welcome to Millhaven. Press T or walk into villagers to talk.");
    }

    // ── Value types ───────────────────────────────────────────────────────────

    private readonly record struct Rect(int X, int Y, int Width, int Height)
    {
        /// <summary>True if this rect and <paramref name="other"/> overlap when
        /// this rect is expanded by <paramref name="margin"/> on every side.</summary>
        public bool IntersectsWithMargin(Rect other, int margin) =>
            X - margin          < other.X + other.Width  &&
            X + Width  + margin > other.X                &&
            Y - margin          < other.Y + other.Height &&
            Y + Height + margin > other.Y;
    }

    // ── AABB placement ────────────────────────────────────────────────────────

    private static List<Rect> PlaceBuildings(
        VillageGenParams p,
        int              forgeX,
        int              forgeY,
        SeededRandom     rng)
    {
        var buildings = new List<Rect>();
        // Forge zone reserved as an obstacle so no building lands on top of it.
        var obstacles = new List<Rect> { new Rect(forgeX - 2, forgeY - 2, 5, 5) };

        int nearTarget = Math.Min(p.GuaranteedNearCount, p.TargetBuildingCount);
        int fullTarget = p.TargetBuildingCount;

        const int MaxAttempts = 800;

        // ── Phase 1: guaranteed near-zone buildings ───────────────────────────
        // Constrain candidates to a box around the forge so the player is
        // surrounded by structures within the initial FOV radius.
        for (int i = 0; i < MaxAttempts && buildings.Count < nearTarget; i++)
        {
            int w = rng.Next(p.MinBuildingSize, p.MaxBuildingSize + 1);
            int h = rng.Next(p.MinBuildingSize, p.MaxBuildingSize + 1);

            int minX = Math.Max(1,              forgeX - p.NearZoneRadius);
            int maxX = Math.Min(p.MapWidth - 3, forgeX + p.NearZoneRadius) - w;
            int minY = Math.Max(1,              forgeY - p.NearZoneRadius);
            int maxY = Math.Min(p.MapHeight - 2, forgeY + p.NearZoneRadius) - h;

            if (maxX < minX || maxY < minY) continue;

            int x = rng.Next(minX, maxX + 1);
            int y = rng.Next(minY, maxY + 1);
            var candidate = new Rect(x, y, w, h);

            bool blocked = buildings.Any(b => candidate.IntersectsWithMargin(b, 1))
                        || obstacles.Any(o => candidate.IntersectsWithMargin(o, 1));
            if (!blocked)
                buildings.Add(candidate);
        }

        // ── Phase 2: remaining buildings across the full map ──────────────────
        for (int i = 0; i < MaxAttempts && buildings.Count < fullTarget; i++)
        {
            int w = rng.Next(p.MinBuildingSize, p.MaxBuildingSize + 1);
            int h = rng.Next(p.MinBuildingSize, p.MaxBuildingSize + 1);

            int maxX = p.MapWidth - 2 - w;
            int maxY = p.MapHeight - 1 - h;
            if (maxX < 1 || maxY < 1) continue;

            int x = rng.Next(1, maxX + 1);
            int y = rng.Next(1, maxY + 1);
            var candidate = new Rect(x, y, w, h);

            bool blocked = buildings.Any(b => candidate.IntersectsWithMargin(b, 1))
                        || obstacles.Any(o => candidate.IntersectsWithMargin(o, 1));
            if (!blocked)
                buildings.Add(candidate);
        }

        return buildings;
    }

    // ── Building stamping ─────────────────────────────────────────────────────

    private static void StampBuilding(
        Rect                    rect,
        Action<string, int, int> spawnAt,
        SeededRandom             rng)
    {
        int right  = rect.X + rect.Width;
        int bottom = rect.Y + rect.Height;

        // Interior floors
        for (int y = rect.Y + 1; y < bottom - 1; y++)
        for (int x = rect.X + 1; x < right  - 1; x++)
            spawnAt("tile_wood_floor", x, y);

        // Collect non-corner wall positions as door candidates.
        var doorCandidates = new List<(int x, int y)>();

        for (int x = rect.X + 1; x < right - 1; x++)
        {
            doorCandidates.Add((x, rect.Y));        // top edge
            doorCandidates.Add((x, bottom - 1));    // bottom edge
        }
        for (int y = rect.Y + 1; y < bottom - 1; y++)
        {
            doorCandidates.Add((rect.X,    y));     // left edge
            doorCandidates.Add((right - 1, y));     // right edge
        }

        var door = doorCandidates[rng.Next(doorCandidates.Count)];

        void Wall(int x, int y) =>
            spawnAt(x == door.x && y == door.y ? "tile_door_closed" : "tile_wood_wall", x, y);

        // Stamp perimeter
        for (int x = rect.X; x < right; x++)
        {
            Wall(x, rect.Y);        // top row
            Wall(x, bottom - 1);    // bottom row
        }
        for (int y = rect.Y + 1; y < bottom - 1; y++)
        {
            Wall(rect.X,    y);     // left col
            Wall(right - 1, y);     // right col
        }
    }

    // ── Wilderness ────────────────────────────────────────────────────────────

    /// <summary>
    /// Replaces outer grass tiles (beyond the near-zone) with trees using
    /// deterministic hash noise, then spawns wilderness creatures.
    /// </summary>
    private static void PlaceWilderness(
        VillageGenParams           genParams,
        int                        plazaCx,
        int                        plazaCy,
        int                        riverX,
        List<Rect>                 buildings,
        ImmutableDictionary<Guid, Entity>.Builder entities,
        bool[]                                    walkability,
        DataRegistry                              registry,
        SeededRandom                              rng)
    {
        int width  = genParams.MapWidth;
        int height = genParams.MapHeight;
        int seed   = genParams.Seed;

        var wildPositions = new List<Position>();

        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++) // skip the river columns
        {
            // Skip river columns
            if (x == riverX || x == riverX + 1) continue;

            // Inside the near-zone: leave as village
            int dx = Math.Abs(x - plazaCx);
            int dy = Math.Abs(y - plazaCy);
            if (dx <= genParams.NearZoneRadius && dy <= genParams.NearZoneRadius) continue;

            // Don't overwrite any building tile
            if (buildings.Any(b => x >= b.X && x < b.X + b.Width
                                && y >= b.Y && y < b.Y + b.Height)) continue;

            // Hash-based noise (~40% tree density in wilderness)
            int   hash  = HashCode.Combine(x, y, seed, 0x57A1_F3B2);
            float noise = (((hash % 100) + 100) % 100) / 100f;

            if (noise < 0.40f)
            {
                // Place tree; mark tile as blocking
                if (!registry.Templates.TryGetValue("env_tree", out var treeTpl)) continue;
                var tree = SpawnSystem.BuildEntity(treeTpl, new Position(x, y));
                entities[tree.Id] = tree;
                walkability[y * width + x] = false;
            }
            else if (walkability[y * width + x])
            {
                wildPositions.Add(new Position(x, y));
            }
        }

        // Spawn 3-5 wolves and 2-4 boars near the village edge, not deep in the wilderness.
        // Without this cap, creatures scatter across the full map (~23 000 positions)
        // and the player statistically never encounters them.
        const int MaxCreatureRadius = 35;
        var nearWild = wildPositions
            .Where(p => Math.Max(Math.Abs(p.X - plazaCx), Math.Abs(p.Y - plazaCy)) <= MaxCreatureRadius)
            .ToList();

        SpawnWildCreatures("creature_wolf", rng.Next(3, 6), nearWild,
            entities, registry, rng.Split("wolves"));
        SpawnWildCreatures("creature_boar", rng.Next(2, 5), nearWild,
            entities, registry, rng.Split("boars"));
    }

    private static void SpawnWildCreatures(
        string                     templateId,
        int                        count,
        List<Position>             candidates,
        ImmutableDictionary<Guid, Entity>.Builder entities,
        DataRegistry                              registry,
        SeededRandom                              rng)
    {
        if (candidates.Count == 0) return;
        if (!registry.Templates.TryGetValue(templateId, out var template)) return;

        var remaining = new List<Position>(candidates);
        for (int i = 0; i < count && remaining.Count > 0; i++)
        {
            int idx  = rng.Next(remaining.Count);
            var pos  = remaining[idx];
            remaining.RemoveAt(idx);

            var creature = SpawnSystem.BuildEntity(template, pos);
            entities[creature.Id] = creature;
        }
    }

    // ── Shop population ───────────────────────────────────────────────────────

    /// <summary>
    /// For every entity whose template has a ShopInventory, spawn item entities
    /// and add them to that entity's inventory.
    /// Called after the main entity dictionary is built.
    /// </summary>
    private static void PopulateShops(
        ImmutableDictionary<Guid, Entity>.Builder entities,
        DataRegistry                              registry)
    {
        // Collect shopkeeper IDs first to avoid mutating while iterating
        var shopkeepers = entities.Keys
            .Where(id => entities[id].Identity is not null
                      && registry.Templates.TryGetValue(entities[id].Identity!.TemplateId, out var t)
                      && t.ShopInventory?.Length > 0)
            .ToList();

        foreach (var shopkeeperId in shopkeepers)
        {
            var shopkeeper = entities[shopkeeperId];
            if (shopkeeper.Identity is null) continue;
            if (!registry.Templates.TryGetValue(shopkeeper.Identity.TemplateId, out var tpl)) continue;
            if (tpl.ShopInventory is null || tpl.ShopInventory.Length == 0) continue;

            var shopItems = ImmutableArray.CreateBuilder<Guid>();
            foreach (var itemTemplateId in tpl.ShopInventory)
            {
                if (!registry.Templates.TryGetValue(itemTemplateId, out var itemTpl)) continue;
                // Spawn item with no world position — already in inventory
                var item = SpawnSystem.BuildEntity(itemTpl, new Position(-1, -1))
                           with { Spatial = null };
                entities[item.Id] = item;
                shopItems.Add(item.Id);
            }

            // Merge with any pre-existing inventory (from BuildEntity's StartingGold)
            var existingInv = shopkeeper.Inventory
                              ?? new MonoRogue.Core.Model.InventoryComponent([], tpl.InventorySlots, null, tpl.StartingGold);

            entities[shopkeeperId] = shopkeeper with
            {
                Inventory = existingInv with
                {
                    Items = existingInv.Items.AddRange(shopItems)
                }
            };
        }
    }
}

