namespace MonoRogue.Core.Generation;

using System.Collections.Immutable;
using MonoRogue.Core;
using MonoRogue.Core.Model;
using MonoRogue.Core.Systems;
using MonoRogue.Data;

/// <summary>
/// Parameters for world generation — village dimensions and viewport base size.
/// </summary>
public sealed record WorldGenParams(
    int Seed,
    /// <summary>Viewport tile width (used as base unit for biome sizing).</summary>
    int ViewportWidth,
    /// <summary>Viewport tile height (used as base unit for biome sizing).</summary>
    int ViewportHeight,
    /// <summary>Village region width multiplier (of viewport).</summary>
    float VillageWidthMul  = 2.0f,
    /// <summary>Village region height multiplier (of viewport).</summary>
    float VillageHeightMul = 2.0f,
    int TargetBuildingCount = 10,
    int MinBuildingSize     = 4,
    int MaxBuildingSize     = 6,
    int GuaranteedNearCount = 4,
    int NearZoneRadius      = 14
);

/// <summary>
/// Generates a single large seamless world map with the village at centre
/// and biomes placed in cardinal directions around it. The player walks
/// between areas without loading screens — CDDA-style.
/// </summary>
public static class WorldGenerator
{
    private readonly record struct Region(int X, int Y, int Width, int Height)
    {
        public int Right  => X + Width;
        public int Bottom => Y + Height;
    }

    public static GameState Generate(
        WorldGenParams genParams,
        DataRegistry   registry,
        string         backgroundId,
        Entity         player)
    {
        var rng = new SeededRandom(genParams.Seed);

        // ── 1. Compute region layout ─────────────────────────────────────────
        int vpW = genParams.ViewportWidth;
        int vpH = genParams.ViewportHeight;

        int villageW = (int)(vpW * genParams.VillageWidthMul);
        int villageH = (int)(vpH * genParams.VillageHeightMul);

        var areas = registry.Areas.Values.ToList();
        int northH = 0, southH = 0, westW = 0, eastW = 0;

        foreach (var area in areas)
        {
            int bw = (int)(vpW * area.MapWidthMultiplier);
            int bh = (int)(vpH * area.MapHeightMultiplier);
            switch (area.Direction.ToLowerInvariant())
            {
                case "north": northH = Math.Max(northH, bh); break;
                case "south": southH = Math.Max(southH, bh); break;
                case "west":  westW  = Math.Max(westW,  bw); break;
                case "east":  eastW  = Math.Max(eastW,  bw); break;
            }
        }

        int totalW = westW + villageW + eastW;
        int totalH = northH + villageH + southH;

        var villageRegion = new Region(westW, northH, villageW, villageH);

        // ── 2. Allocate buffers ──────────────────────────────────────────────
        var entities    = ImmutableDictionary.CreateBuilder<Guid, Entity>();
        var walkability = new bool[totalW * totalH];
        // O(1) tile lookup: position → entity ID (only tiles tracked here)
        var tileIndex   = new Dictionary<(int, int), Guid>(totalW * totalH / 2);

        void PlaceTile(string templateId, int x, int y)
        {
            if (x < 0 || x >= totalW || y < 0 || y >= totalH) return;
            if (!registry.Templates.TryGetValue(templateId, out var template)) return;

            // Remove existing tile at this position (O(1) via index)
            var key = (x, y);
            if (tileIndex.TryGetValue(key, out var oldId))
            {
                entities.Remove(oldId);
                tileIndex.Remove(key);
            }

            var entity = SpawnSystem.BuildEntity(template, new Position(x, y));
            entities[entity.Id] = entity;

            if (template.IsTile)
                tileIndex[key] = entity.Id;

            walkability[y * totalW + x] = !template.BlocksMovement || !template.IsTile;
        }

        // Non-tile entities (creatures, NPCs, items) — no index removal needed
        void SpawnEntity(string templateId, int x, int y)
        {
            if (x < 0 || x >= totalW || y < 0 || y >= totalH) return;
            if (!registry.Templates.TryGetValue(templateId, out var template)) return;
            var entity = SpawnSystem.BuildEntity(template, new Position(x, y));
            entities[entity.Id] = entity;
            if (template.BlocksMovement && template.IsTile)
                walkability[y * totalW + x] = false;
        }

        // ── 3. Generate village region ───────────────────────────────────────
        GenerateVillageRegion(
            villageRegion, genParams, registry, entities, walkability, totalW,
            tileIndex, rng.Split("village"), PlaceTile, SpawnEntity);

        // ── 4. Generate biome regions ────────────────────────────────────────
        foreach (var area in areas)
        {
            var biomeRegion = ComputeBiomeRegion(area, vpW, vpH, villageRegion);
            switch (area.GeneratorType.ToLowerInvariant())
            {
                case "wilderness":
                    GenerateWildernessRegion(biomeRegion, area, entities, walkability,
                        totalW, totalH, registry, rng.Split(area.Id), PlaceTile, SpawnEntity);
                    break;
                case "dungeon":
                    GenerateDungeonRegion(biomeRegion, area, entities, walkability,
                        totalW, totalH, registry, rng.Split(area.Id), PlaceTile, SpawnEntity);
                    break;
            }
        }

        // ── 5. Blend edges between village and biomes ────────────────────────
        BlendEdges(villageRegion, areas, walkability, totalW, totalH,
            vpW, vpH, rng.Split("blend"), PlaceTile);

        // ── 6. Player placement (centre of village) ──────────────────────────
        int plazaCx = villageRegion.X + villageRegion.Width / 2;
        int plazaCy = villageRegion.Y + villageRegion.Height / 2;
        var playerPos = new Position(plazaCx - 2, plazaCy);

        player = player with
        {
            Spatial = player.Spatial != null
                ? player.Spatial with { Position = playerPos }
                : new SpatialComponent(playerPos, true)
        };
        entities[player.Id] = player;

        // ── 7. Background (quests + starting items) ──────────────────────────
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

        // ── 8. Populate shops ────────────────────────────────────────────────
        PopulateShops(entities, registry);

        var state = GameState.Empty(totalW, totalH) with
        {
            Entities       = entities.ToImmutable(),
            WalkabilityMap = [.. walkability],
            ActiveQuests   = activeQuests.ToImmutable(),
            PlayerEntityId = player.Id,
            FloorLevel     = 0,
        };

        state = state.AppendMessage("Welcome to Millhaven. Explore the surrounding lands or press T to talk.");

        // Pre-explore the village region only
        var vis = FovSystem.Compute(state, playerPos, 10);
        var villageTiles = new HashSet<Position>();
        foreach (var kvp in tileIndex)
        {
            int tx = kvp.Key.Item1, ty = kvp.Key.Item2;
            if (tx >= villageRegion.X && tx < villageRegion.Right
                && ty >= villageRegion.Y && ty < villageRegion.Bottom)
                villageTiles.Add(new Position(tx, ty));
        }

        return state with { VisibleTiles = vis, ExploredTiles = villageTiles.ToImmutableHashSet() };
    }

    // ── Region computation ───────────────────────────────────────────────────

    private static Region ComputeBiomeRegion(
        AreaTemplate area, int vpW, int vpH, Region village)
    {
        int bw = (int)(vpW * area.MapWidthMultiplier);
        int bh = (int)(vpH * area.MapHeightMultiplier);

        return area.Direction.ToLowerInvariant() switch
        {
            "north" => new Region(village.X, village.Y - bh, village.Width, bh),
            "south" => new Region(village.X, village.Bottom, village.Width, bh),
            "west"  => new Region(village.X - bw, village.Y, bw, village.Height),
            "east"  => new Region(village.Right, village.Y, bw, village.Height),
            _       => new Region(village.X, village.Y - bh, village.Width, bh)
        };
    }

    // ── Village generation ───────────────────────────────────────────────────

    private static void GenerateVillageRegion(
        Region       region,
        WorldGenParams genParams,
        DataRegistry registry,
        ImmutableDictionary<Guid, Entity>.Builder entities,
        bool[]       walkability,
        int          mapW,
        Dictionary<(int, int), Guid> tileIndex,
        SeededRandom rng,
        Action<string, int, int> placeTile,
        Action<string, int, int> spawnEntity)
    {
        int plazaCx = region.X + region.Width / 2;
        int plazaCy = region.Y + region.Height / 2;
        int riverX  = Math.Min(plazaCx + 20, region.Right - 3);

        // Place buildings
        var buildings = PlaceBuildings(
            region, genParams.TargetBuildingCount,
            genParams.MinBuildingSize, genParams.MaxBuildingSize,
            genParams.GuaranteedNearCount, genParams.NearZoneRadius,
            plazaCx, plazaCy, rng.Split("placement"));

        var buildingRng = rng.Split("buildings");
        var villagerRng = rng.Split("villagers");

        bool InAnyBuilding(int x, int y) =>
            buildings.Any(r => x >= r.X && x < r.X + r.W && y >= r.Y && y < r.Y + r.H);

        // Base layer: grass + river
        for (int y = region.Y; y < region.Bottom; y++)
        for (int x = region.X; x < region.Right; x++)
        {
            walkability[y * mapW + x] = true;
            if (x == riverX || x == riverX + 1)
                placeTile("env_river_tile", x, y);
            else if (!InAnyBuilding(x, y))
                placeTile("tile_grass", x, y);
        }

        // Forge + blacksmith
        placeTile("env_forge", plazaCx, plazaCy);
        spawnEntity("npc_blacksmith", plazaCx - 1, plazaCy);
        spawnEntity("item_empty_bucket", plazaCx, plazaCy + 1);

        // Stamp buildings
        foreach (var rect in buildings)
            StampBuilding(rect, placeTile, buildingRng);

        // NPCs per building
        string[] traderPool = ["npc_herbalist", "npc_trader"];
        for (int i = 0; i < buildings.Count; i++)
        {
            var rect = buildings[i];
            string npcTemplate = i < traderPool.Length ? traderPool[i] : "npc_villager";
            int vx = villagerRng.Next(rect.X + 1, rect.X + rect.W - 1);
            int vy = villagerRng.Next(rect.Y + 1, rect.Y + rect.H - 1);
            spawnEntity(npcTemplate, vx, vy);
        }

        // Wilderness perimeter (trees + creatures)
        PlaceVillageWilderness(
            region, plazaCx, plazaCy, riverX, buildings,
            genParams.NearZoneRadius, genParams.Seed,
            entities, walkability, mapW, registry, rng.Split("wilderness"));

        // Dungeon stairs at south edge of village
        int stairsX = plazaCx;
        int stairsY = region.Bottom - 5;
        placeTile("tile_stairs_down", stairsX, stairsY);
        walkability[stairsY * mapW + stairsX] = true;
    }

    // ── Wilderness biome generation ──────────────────────────────────────────

    private static void GenerateWildernessRegion(
        Region       region,
        AreaTemplate area,
        ImmutableDictionary<Guid, Entity>.Builder entities,
        bool[]       walkability,
        int          mapW,
        int          mapH,
        DataRegistry registry,
        SeededRandom rng,
        Action<string, int, int> placeTile,
        Action<string, int, int> spawnEntity)
    {
        int seed = rng.Seed;

        // Fill region with base tile + scattered wall tiles
        for (int y = region.Y; y < region.Bottom; y++)
        for (int x = region.X; x < region.Right; x++)
        {
            if (x < 0 || x >= mapW || y < 0 || y >= mapH) continue;

            int   hash  = HashCode.Combine(x, y, seed, 0x3A7E_91C4);
            float noise = (((hash % 100) + 100) % 100) / 100f;

            if (noise < area.WallDensity)
            {
                placeTile(area.WallTile, x, y);
            }
            else
            {
                placeTile(area.BaseTile, x, y);
            }
        }

        // Carve clearings
        int minClearings = area.ClearingCount is { Length: >= 2 } ? area.ClearingCount[0] : 3;
        int maxClearings = area.ClearingCount is { Length: >= 2 } ? area.ClearingCount[1] : 6;
        int minSize      = area.ClearingSize  is { Length: >= 2 } ? area.ClearingSize[0]  : 5;
        int maxSize      = area.ClearingSize  is { Length: >= 2 } ? area.ClearingSize[1]  : 12;

        int clearingCount = rng.Next(minClearings, maxClearings + 1);
        var clearingPositions = new List<Position>();

        for (int i = 0; i < clearingCount; i++)
        {
            int radius = rng.Next(minSize, maxSize + 1) / 2;
            int cx = rng.Next(region.X + radius + 2, Math.Max(region.X + radius + 3, region.Right - radius - 2));
            int cy = rng.Next(region.Y + radius + 2, Math.Max(region.Y + radius + 3, region.Bottom - radius - 2));

            for (int y = cy - radius; y <= cy + radius; y++)
            for (int x = cx - radius; x <= cx + radius; x++)
            {
                if (x < 0 || x >= mapW || y < 0 || y >= mapH) continue;
                if ((x - cx) * (x - cx) + (y - cy) * (y - cy) > radius * radius) continue;

                // Replace wall with base tile (O(1) via PlaceTile's index)
                placeTile(area.BaseTile, x, y);
                walkability[y * mapW + x] = true;
                clearingPositions.Add(new Position(x, y));
            }
        }

        // Spawn enemies
        if (area.Enemies is not null)
        {
            foreach (var enemy in area.Enemies)
            {
                int minCount = enemy.Count is { Length: >= 2 } ? enemy.Count[0] : 1;
                int maxCount = enemy.Count is { Length: >= 2 } ? enemy.Count[1] : 3;
                int count    = rng.Next(minCount, maxCount + 1);
                SpawnInPositions(enemy.TemplateId, count, clearingPositions, entities, registry, rng);
            }
        }

        // Scatter items
        if (area.Items is not null)
        {
            foreach (var item in area.Items)
            {
                int rolls = clearingCount;
                for (int i = 0; i < rolls; i++)
                {
                    if (rng.NextBool(item.Chance) && clearingPositions.Count > 0)
                    {
                        int idx = rng.Next(clearingPositions.Count);
                        var pos = clearingPositions[idx];
                        spawnEntity(item.TemplateId, pos.X, pos.Y);
                        clearingPositions.RemoveAt(idx);
                    }
                }
            }
        }

        // Spawn NPCs
        if (area.Npcs is not null)
        {
            foreach (var npc in area.Npcs)
            {
                int minCount = npc.Count is { Length: >= 2 } ? npc.Count[0] : 0;
                int maxCount = npc.Count is { Length: >= 2 } ? npc.Count[1] : 1;
                int count    = rng.Next(minCount, maxCount + 1);
                SpawnInPositions(npc.TemplateId, count, clearingPositions, entities, registry, rng);
            }
        }
    }

    // ── Dungeon biome generation ─────────────────────────────────────────────

    private static void GenerateDungeonRegion(
        Region       region,
        AreaTemplate area,
        ImmutableDictionary<Guid, Entity>.Builder entities,
        bool[]       walkability,
        int          mapW,
        int          mapH,
        DataRegistry registry,
        SeededRandom rng,
        Action<string, int, int> placeTile,
        Action<string, int, int> spawnEntity)
    {
        var dungeon = BspGenerator.Generate(region.Width, region.Height, rng, 1);

        // Stamp tiles offset by region origin
        for (int ly = 0; ly < region.Height; ly++)
        for (int lx = 0; lx < region.Width; lx++)
        {
            int wx = region.X + lx;
            int wy = region.Y + ly;
            if (wx < 0 || wx >= mapW || wy < 0 || wy >= mapH) continue;

            bool isFloor = dungeon.IsFloor(lx, ly);
            placeTile(isFloor ? area.BaseTile : area.WallTile, wx, wy);
        }

        // Spawn enemies in rooms (skip first and last)
        var rooms = dungeon.Rooms;
        if (area.Enemies is not null && rooms.Length > 2)
        {
            for (int i = 1; i < rooms.Length - 1; i++)
            {
                foreach (var enemy in area.Enemies)
                {
                    int minCount = enemy.Count is { Length: >= 2 } ? enemy.Count[0] : 1;
                    int maxCount = enemy.Count is { Length: >= 2 } ? enemy.Count[1] : 3;
                    int perRoom  = Math.Max(1, rng.Next(minCount, maxCount + 1) / Math.Max(1, rooms.Length - 2));

                    for (int e = 0; e < perRoom; e++)
                    {
                        var pos = RandomFloorInRoom(dungeon, rooms[i], rng);
                        if (pos.HasValue)
                            spawnEntity(enemy.TemplateId, region.X + pos.Value.X, region.Y + pos.Value.Y);
                    }
                }
            }
        }

        // Scatter items
        if (area.Items is not null && rooms.Length > 2)
        {
            for (int i = 1; i < rooms.Length - 1; i++)
            {
                foreach (var item in area.Items)
                {
                    if (rng.NextBool(item.Chance))
                    {
                        var pos = RandomFloorInRoom(dungeon, rooms[i], rng);
                        if (pos.HasValue)
                            spawnEntity(item.TemplateId, region.X + pos.Value.X, region.Y + pos.Value.Y);
                    }
                }
            }
        }

        // Spawn NPCs
        if (area.Npcs is not null && rooms.Length > 1)
        {
            foreach (var npc in area.Npcs)
            {
                int minCount = npc.Count is { Length: >= 2 } ? npc.Count[0] : 0;
                int maxCount = npc.Count is { Length: >= 2 } ? npc.Count[1] : 1;
                int count    = rng.Next(minCount, maxCount + 1);

                for (int n = 0; n < count; n++)
                {
                    int roomIdx = rng.Next(1, rooms.Length);
                    var pos = RandomFloorInRoom(dungeon, rooms[roomIdx], rng);
                    if (pos.HasValue)
                        spawnEntity(npc.TemplateId, region.X + pos.Value.X, region.Y + pos.Value.Y);
                }
            }
        }
    }

    // ── Edge blending ────────────────────────────────────────────────────────

    private static void BlendEdges(
        Region             village,
        List<AreaTemplate> areas,
        bool[]             walkability,
        int                mapW,
        int                mapH,
        int                vpW,
        int                vpH,
        SeededRandom       rng,
        Action<string, int, int> placeTile)
    {
        const int BlendWidth = 5;

        foreach (var area in areas)
        {
            switch (area.Direction.ToLowerInvariant())
            {
                case "north":
                    BlendStrip(village.X, village.Right, village.Y - BlendWidth, village.Y + BlendWidth,
                        true, "tile_grass", area, walkability, mapW, mapH, rng, placeTile);
                    break;
                case "south":
                    BlendStrip(village.X, village.Right, village.Bottom - BlendWidth, village.Bottom + BlendWidth,
                        true, "tile_grass", area, walkability, mapW, mapH, rng, placeTile);
                    break;
                case "west":
                    BlendStrip(village.Y, village.Bottom, village.X - BlendWidth, village.X + BlendWidth,
                        false, "tile_grass", area, walkability, mapW, mapH, rng, placeTile);
                    break;
                case "east":
                    BlendStrip(village.Y, village.Bottom, village.Right - BlendWidth, village.Right + BlendWidth,
                        false, "tile_grass", area, walkability, mapW, mapH, rng, placeTile);
                    break;
            }
        }
    }

    private static void BlendStrip(
        int startMajor, int endMajor, int startMinor, int endMinor,
        bool horizontal,
        string villageTile, AreaTemplate area,
        bool[] walkability, int mapW, int mapH,
        SeededRandom rng,
        Action<string, int, int> placeTile)
    {
        int range = Math.Max(1, endMinor - startMinor);

        for (int major = startMajor; major < endMajor; major++)
        for (int minor = startMinor; minor < endMinor; minor++)
        {
            int x = horizontal ? major : minor;
            int y = horizontal ? minor : major;
            if (x < 0 || x >= mapW || y < 0 || y >= mapH) continue;

            float t = (float)(minor - startMinor) / range;
            int hash = HashCode.Combine(x, y, rng.Seed, 0xBEEF);
            float noise = (((hash % 100) + 100) % 100) / 100f;

            bool placeWall = noise < area.WallDensity * t * 0.3f;

            if (placeWall)
            {
                placeTile(area.WallTile, x, y);
            }
            else
            {
                string tile = t < 0.5f ? villageTile : area.BaseTile;
                placeTile(tile, x, y);
            }
        }
    }

    // ── Village wilderness (perimeter trees + creatures) ─────────────────────

    private static void PlaceVillageWilderness(
        Region     region,
        int        plazaCx,
        int        plazaCy,
        int        riverX,
        List<(int X, int Y, int W, int H)> buildings,
        int        nearZoneRadius,
        int        seed,
        ImmutableDictionary<Guid, Entity>.Builder entities,
        bool[]     walkability,
        int        mapW,
        DataRegistry registry,
        SeededRandom rng)
    {
        var wildPositions = new List<Position>();

        for (int y = region.Y; y < region.Bottom; y++)
        for (int x = region.X; x < region.Right; x++)
        {
            if (x == riverX || x == riverX + 1) continue;

            int dx = Math.Abs(x - plazaCx);
            int dy = Math.Abs(y - plazaCy);
            if (dx <= nearZoneRadius && dy <= nearZoneRadius) continue;

            if (buildings.Any(b => x >= b.X && x < b.X + b.W
                                && y >= b.Y && y < b.Y + b.H)) continue;

            int   hash  = HashCode.Combine(x, y, seed, 0x57A1_F3B2);
            float noise = (((hash % 100) + 100) % 100) / 100f;

            if (noise < 0.20f)
            {
                if (!registry.Templates.TryGetValue("env_tree", out var treeTpl)) continue;
                var tree = SpawnSystem.BuildEntity(treeTpl, new Position(x, y));
                entities[tree.Id] = tree;
                walkability[y * mapW + x] = false;
            }
            else if (walkability[y * mapW + x])
            {
                wildPositions.Add(new Position(x, y));
            }
        }

        const int MaxCreatureRadius = 35;
        var nearWild = wildPositions
            .Where(p => Math.Max(Math.Abs(p.X - plazaCx), Math.Abs(p.Y - plazaCy)) <= MaxCreatureRadius)
            .ToList();

        SpawnInPositions("creature_wolf", rng.Next(3, 6), nearWild, entities, registry, rng.Split("wolves"));
        SpawnInPositions("creature_boar", rng.Next(2, 5), nearWild, entities, registry, rng.Split("boars"));
    }

    // ── Building placement ───────────────────────────────────────────────────

    private static List<(int X, int Y, int W, int H)> PlaceBuildings(
        Region       region,
        int          targetCount,
        int          minSize,
        int          maxSize,
        int          nearCount,
        int          nearRadius,
        int          forgeX,
        int          forgeY,
        SeededRandom rng)
    {
        var buildings = new List<(int X, int Y, int W, int H)>();
        var obstacles = new List<(int X, int Y, int W, int H)>
        {
            (forgeX - 2, forgeY - 2, 5, 5)
        };

        int nearTarget = Math.Min(nearCount, targetCount);
        const int MaxAttempts = 800;

        bool Intersects((int X, int Y, int W, int H) a, (int X, int Y, int W, int H) b, int margin) =>
            a.X - margin < b.X + b.W && a.X + a.W + margin > b.X &&
            a.Y - margin < b.Y + b.H && a.Y + a.H + margin > b.Y;

        // Phase 1: near-zone
        for (int i = 0; i < MaxAttempts && buildings.Count < nearTarget; i++)
        {
            int w = rng.Next(minSize, maxSize + 1);
            int h = rng.Next(minSize, maxSize + 1);

            int minX = Math.Max(region.X + 1, forgeX - nearRadius);
            int maxX = Math.Min(region.Right - 3, forgeX + nearRadius) - w;
            int minY = Math.Max(region.Y + 1, forgeY - nearRadius);
            int maxY = Math.Min(region.Bottom - 2, forgeY + nearRadius) - h;

            if (maxX < minX || maxY < minY) continue;

            int x = rng.Next(minX, maxX + 1);
            int y = rng.Next(minY, maxY + 1);
            var candidate = (x, y, w, h);

            bool blocked = buildings.Any(b => Intersects(candidate, b, 1))
                        || obstacles.Any(o => Intersects(candidate, o, 1));
            if (!blocked)
                buildings.Add(candidate);
        }

        // Phase 2: full region
        for (int i = 0; i < MaxAttempts && buildings.Count < targetCount; i++)
        {
            int w = rng.Next(minSize, maxSize + 1);
            int h = rng.Next(minSize, maxSize + 1);

            int maxX = region.Right - 2 - w;
            int maxY = region.Bottom - 1 - h;
            if (maxX < region.X + 1 || maxY < region.Y + 1) continue;

            int x = rng.Next(region.X + 1, maxX + 1);
            int y = rng.Next(region.Y + 1, maxY + 1);
            var candidate = (x, y, w, h);

            bool blocked = buildings.Any(b => Intersects(candidate, b, 1))
                        || obstacles.Any(o => Intersects(candidate, o, 1));
            if (!blocked)
                buildings.Add(candidate);
        }

        return buildings;
    }

    // ── Building stamping ────────────────────────────────────────────────────

    private static void StampBuilding(
        (int X, int Y, int W, int H) rect,
        Action<string, int, int> placeTile,
        SeededRandom rng)
    {
        int right  = rect.X + rect.W;
        int bottom = rect.Y + rect.H;

        for (int y = rect.Y + 1; y < bottom - 1; y++)
        for (int x = rect.X + 1; x < right  - 1; x++)
            placeTile("tile_wood_floor", x, y);

        var doorCandidates = new List<(int x, int y)>();
        for (int x = rect.X + 1; x < right - 1; x++)
        {
            doorCandidates.Add((x, rect.Y));
            doorCandidates.Add((x, bottom - 1));
        }
        for (int y = rect.Y + 1; y < bottom - 1; y++)
        {
            doorCandidates.Add((rect.X, y));
            doorCandidates.Add((right - 1, y));
        }

        var door = doorCandidates[rng.Next(doorCandidates.Count)];

        void Wall(int x, int y) =>
            placeTile(x == door.x && y == door.y ? "tile_door_closed" : "tile_wood_wall", x, y);

        for (int x = rect.X; x < right; x++)
        {
            Wall(x, rect.Y);
            Wall(x, bottom - 1);
        }
        for (int y = rect.Y + 1; y < bottom - 1; y++)
        {
            Wall(rect.X, y);
            Wall(right - 1, y);
        }
    }

    // ── Shared helpers ───────────────────────────────────────────────────────

    private static void SpawnInPositions(
        string templateId,
        int count,
        List<Position> candidates,
        ImmutableDictionary<Guid, Entity>.Builder entities,
        DataRegistry registry,
        SeededRandom rng)
    {
        if (candidates.Count == 0) return;
        if (!registry.Templates.TryGetValue(templateId, out var template)) return;

        for (int i = 0; i < count && candidates.Count > 0; i++)
        {
            int idx = rng.Next(candidates.Count);
            var pos = candidates[idx];
            candidates.RemoveAt(idx);
            var creature = SpawnSystem.BuildEntity(template, pos);
            entities[creature.Id] = creature;
        }
    }

    private static Position? RandomFloorInRoom(DungeonMap dungeon, RoomRect room, SeededRandom rng)
    {
        for (int attempt = 0; attempt < 10; attempt++)
        {
            var x = rng.Next(room.X, room.Right);
            var y = rng.Next(room.Y, room.Bottom);
            if (dungeon.IsFloor(x, y))
                return new Position(x, y);
        }
        return null;
    }

    private static void PopulateShops(
        ImmutableDictionary<Guid, Entity>.Builder entities,
        DataRegistry registry)
    {
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
                var item = SpawnSystem.BuildEntity(itemTpl, new Position(-1, -1)) with { Spatial = null };
                entities[item.Id] = item;
                shopItems.Add(item.Id);
            }

            var existingInv = shopkeeper.Inventory
                              ?? new InventoryComponent([], tpl.InventorySlots, null, tpl.StartingGold);

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
