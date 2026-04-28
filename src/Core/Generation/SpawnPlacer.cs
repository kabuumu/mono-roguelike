namespace MonoRogue.Core.Generation;

using System.Collections.Immutable;
using MonoRogue.Core;
using MonoRogue.Core.Model;

/// <summary>
/// Decides where to place enemies and items after the geometry is generated.
/// Keeps placement logic separate from map geometry (single-responsibility).
///
/// Enemies: 1-3 per room (excluding the spawn room and exit room).
/// Items:   0-1 per room, weighted toward later floors having better loot.
/// </summary>
public static class SpawnPlacer
{
    public readonly record struct SpawnEntry(string TemplateId, Position Position);

    private static readonly string[] EarlyEnemies  = ["creature_goblin", "creature_rat"];
    private static readonly string[] MidEnemies    = ["creature_goblin", "creature_orc"];
    private static readonly string[] LateEnemies   = ["creature_orc",    "creature_troll"];

    private static readonly string[] ConsumablePool = ["item_health_potion"];
    private static readonly string[] WeaponPool     = ["item_sword", "item_dagger"];
    private static readonly string[] ArmorPool      = ["item_shield", "item_leather_armor"];

    public static ImmutableArray<SpawnEntry> GetSpawns(
        DungeonMap   dungeon,
        SeededRandom rng,
        int          floorLevel)
    {
        var spawns = ImmutableArray.CreateBuilder<SpawnEntry>();
        var rooms  = dungeon.Rooms;
        if (rooms.Length <= 1) return spawns.ToImmutable();

        var enemyPool = floorLevel <= 2 ? EarlyEnemies
                      : floorLevel <= 5 ? MidEnemies
                      : LateEnemies;

        // Skip first room (player spawn) and last room (exit)
        for (int i = 1; i < rooms.Length - 1; i++)
        {
            var room    = rooms[i];
            int enemies = rng.Next(1, Math.Min(4, floorLevel + 2));

            for (int e = 0; e < enemies; e++)
            {
                var pos = RandomFloorInRoom(dungeon, room, rng);
                if (pos.HasValue)
                    spawns.Add(new SpawnEntry(enemyPool[rng.Next(enemyPool.Length)], pos.Value));
            }

            // ~40% chance of an item per room
            if (rng.NextBool(0.4))
            {
                var itemId = ChooseItem(rng, floorLevel);
                var pos    = RandomFloorInRoom(dungeon, room, rng);
                if (pos.HasValue)
                    spawns.Add(new SpawnEntry(itemId, pos.Value));
            }
        }

        // Guaranteed health potion in the first non-spawn room
        if (rooms.Length >= 3)
        {
            var pos = RandomFloorInRoom(dungeon, rooms[1], rng);
            if (pos.HasValue)
                spawns.Add(new SpawnEntry("item_health_potion", pos.Value));
        }

        return spawns.ToImmutable();
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static Position? RandomFloorInRoom(DungeonMap dungeon, RoomRect room, SeededRandom rng)
    {
        // Try up to 10 times to find a floor tile inside the room
        for (int attempt = 0; attempt < 10; attempt++)
        {
            var x = rng.Next(room.X, room.Right);
            var y = rng.Next(room.Y, room.Bottom);
            if (dungeon.IsFloor(x, y))
                return new Position(x, y);
        }
        return null;
    }

    private static string ChooseItem(SeededRandom rng, int floorLevel)
    {
        // Weighted: potions more common early, weapons/armor more common late
        int roll = rng.Next(100);

        if (roll < 50)
            return ConsumablePool[rng.Next(ConsumablePool.Length)];
        if (roll < 75)
            return WeaponPool[rng.Next(WeaponPool.Length)];
        return ArmorPool[rng.Next(ArmorPool.Length)];
    }
}
