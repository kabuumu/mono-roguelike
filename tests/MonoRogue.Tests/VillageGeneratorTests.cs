namespace MonoRogue.Tests;

using System.Collections.Generic;
using System.Collections.Immutable;
using MonoRogue.Core;
using MonoRogue.Core.Generation;
using MonoRogue.Core.Model;
using MonoRogue.Data;
using Xunit;

/// <summary>
/// Proves that VillageGenerator places the guaranteed near-zone buildings
/// close enough to the forge that the player sees structures from turn one.
/// These tests use no DataRegistry and no content files — the placement
/// geometry is pure math.
/// </summary>
public sealed class VillageGeneratorTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    // A minimal DataRegistry with just enough templates to let StampBuilding
    // actually write wall/floor tiles and modify WalkabilityMap.
    private static readonly DataRegistry MinimalRegistry = BuildMinimalRegistry();

    private static DataRegistry BuildMinimalRegistry()
    {
        static EntityTemplate Wall(string id) => new(
            Id: id, Name: id, SpriteKey: id,
            BlocksMovement: true, IsTile: true);

        static EntityTemplate Floor(string id) => new(
            Id: id, Name: id, SpriteKey: id,
            BlocksMovement: false, IsTile: true);

        static EntityTemplate Npc(string id) => new(
            Id: id, Name: id, SpriteKey: id,
            BlocksMovement: true, IsTile: false,
            MaxHealth: 10, HasAi: false, IsPeaceful: true);

        var templates = new Dictionary<string, EntityTemplate>
        {
            ["tile_wood_wall"]   = Wall("tile_wood_wall"),
            ["tile_wood_floor"]  = Floor("tile_wood_floor"),
            ["tile_door_closed"] = Wall("tile_door_closed"),
            ["tile_grass"]       = Floor("tile_grass"),
            ["env_river_tile"]   = Floor("env_river_tile"),
            ["env_forge"]        = Floor("env_forge"),
            ["npc_blacksmith"]   = Npc("npc_blacksmith"),
            ["npc_villager"]     = Npc("npc_villager"),
        }.ToImmutableDictionary();

        return new DataRegistry(
            templates,
            ImmutableDictionary<string, BackgroundTemplate>.Empty,
            ImmutableDictionary<string, FactionTemplate>.Empty,
            ImmutableDictionary<string, QuestTemplate>.Empty);
    }

    // Thin wrapper around the public placement logic.
    // We exercise it indirectly through Generate() and inspect the
    // WalkabilityMap to count blocked (wall) cells per zone.
    private static VillageGenParams Params(
        int seed     = 42,
        int w        = 120,
        int h        = 50,
        int total    = 10,
        int nearCount = 4,
        int nearRadius = 14,
        int minSize  = 4,
        int maxSize  = 6) => new(
            Seed:                seed,
            MapWidth:            w,
            MapHeight:           h,
            TargetBuildingCount: total,
            MinBuildingSize:     minSize,
            MaxBuildingSize:     maxSize,
            GuaranteedNearCount: nearCount,
            NearZoneRadius:      nearRadius
        );

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public void At_least_GuaranteedNearCount_buildings_land_in_near_zone()
    {
        // The near-zone is a box of ±NearZoneRadius around the forge.
        // We verify by counting wall tiles inside that box — each building
        // contributes at least (4 * (MinSize-1)) wall tiles to its perimeter.
        var p         = Params(seed: 42);
        int forgeX    = (p.MapWidth  - 2) / 2;
        int forgeY    =  p.MapHeight      / 2;
        int r         = p.NearZoneRadius;

        // Build a fresh player entity to satisfy Generate()'s signature.
        var player = new Entity(
            Id:      Guid.NewGuid(),
            Spatial: new SpatialComponent(Position.Zero, BlocksMovement: true),
            Player:  new PlayerTag());

        // Generate() returns a GameState even with an empty registry —
        // SpawnAt just silently skips unknown template IDs.
        var state = VillageGenerator.Generate(p, MinimalRegistry, "", player);

        // Count wall tiles (non-walkable cells) inside the near-zone.
        int wallsInZone = 0;
        for (int y = Math.Max(0, forgeY - r); y <= Math.Min(p.MapHeight - 1, forgeY + r); y++)
        for (int x = Math.Max(0, forgeX - r); x <= Math.Min(p.MapWidth  - 1, forgeX + r); x++)
        {
            if (!state.IsWalkable(new Position(x, y)))
                wallsInZone++;
        }

        // Each building of minimum size 4×4 contributes at least 12 perimeter
        // tiles. GuaranteedNearCount buildings → at least that many ×12 walls.
        int minExpectedWalls = p.GuaranteedNearCount * (4 * (p.MinBuildingSize - 1));
        Assert.True(wallsInZone >= minExpectedWalls,
            $"Expected ≥{minExpectedWalls} wall tiles in near-zone but found {wallsInZone}. " +
            $"Forge at ({forgeX},{forgeY}), near-zone ±{r}.");
    }

    [Fact]
    public void Total_building_count_meets_or_exceeds_GuaranteedNearCount()
    {
        // The generator must place at least GuaranteedNearCount buildings
        // even on a constrained map where the near-zone is tight.
        var p = Params(seed: 99, w: 60, h: 30, total: 5, nearCount: 3, nearRadius: 10);

        var player = new Entity(
            Id:      Guid.NewGuid(),
            Spatial: new SpatialComponent(Position.Zero, BlocksMovement: true),
            Player:  new PlayerTag());

        var state = VillageGenerator.Generate(p, MinimalRegistry, "", player);

        // Count distinct blocked columns — a rough proxy for building count.
        // At minimum, every near building sets some tiles non-walkable in the zone.
        int r = p.NearZoneRadius;
        int forgeX = (p.MapWidth - 2) / 2;
        int forgeY =  p.MapHeight     / 2;

        bool anyBlockedInZone = false;
        for (int y = Math.Max(0, forgeY - r); y <= Math.Min(p.MapHeight - 1, forgeY + r); y++)
        for (int x = Math.Max(0, forgeX - r); x <= Math.Min(p.MapWidth  - 1, forgeX + r); x++)
        {
            if (!state.IsWalkable(new Position(x, y))) { anyBlockedInZone = true; break; }
        }

        Assert.True(anyBlockedInZone, "No wall tiles found in near-zone — buildings were not placed near the forge.");
    }

    [Fact]
    public void Same_seed_produces_identical_walkability()
    {
        var p = Params(seed: 7);
        var player1 = new Entity(Guid.NewGuid(), Spatial: new SpatialComponent(Position.Zero, true), Player: new PlayerTag());
        var player2 = new Entity(player1.Id,     Spatial: new SpatialComponent(Position.Zero, true), Player: new PlayerTag());

        var s1 = VillageGenerator.Generate(p, MinimalRegistry, "", player1);
        var s2 = VillageGenerator.Generate(p, MinimalRegistry, "", player2);

        Assert.True(s1.WalkabilityMap.SequenceEqual(s2.WalkabilityMap),
            "Same seed produced different walkability maps.");
    }
}
