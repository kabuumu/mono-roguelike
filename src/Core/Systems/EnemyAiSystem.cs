namespace MonoRogue.Core.Systems;

using System.Collections.Immutable;
using MonoRogue.Core.Intents;
using MonoRogue.Core.Model;
using MonoRogue.Core;

/// <summary>
/// Generates movement intents for all AI-controlled entities.
///
/// Behaviour rules:
///   • If the enemy's tile is visible to the player (i.e. in VisibleTiles),
///     the enemy knows where the player is and moves toward them.
///   • Otherwise the enemy wanders randomly (30% chance to move each turn).
///
/// Combat is handled by <see cref="MovementSystem"/> when a MoveIntent targets
/// an occupied tile — there is no separate AttackIntent from the AI.
/// </summary>
public static class EnemyAiSystem
{
    private const int ActivationRadius = 30;

    private static readonly Position[] CardinalDirs =
        [Position.North, Position.South, Position.East, Position.West];

    public static ImmutableArray<Intent> GenerateIntents(
        GameState    state,
        SeededRandom rng)
    {
        var player = state.TryGetPlayer();
        if (player?.Spatial is null) return [];

        var playerPos = player.Spatial.Position;
        var intents   = ImmutableArray.CreateBuilder<Intent>();

        foreach (var entity in state.Entities.Values)
        {
            if (entity.Ai is null || entity.Spatial is null) continue;

            var pos = entity.Spatial.Position;

            // Skip entities far from the player — they don't need AI ticks.
            // This avoids processing thousands of distant enemies on the large world map.
            int dx = Math.Abs(pos.X - playerPos.X);
            int dy = Math.Abs(pos.Y - playerPos.Y);
            if (dx > ActivationRadius || dy > ActivationRadius) continue;

            if (!entity.IsAlive()) continue;

            // Enemies wake up when the player can see them
            bool alertToPlayer = state.VisibleTiles.Contains(pos);

            if (alertToPlayer)
            {
                var dir = ChaseDirection(pos, playerPos, state);
                if (dir != Position.Zero)
                    intents.Add(new MoveIntent(entity.Id, dir));
            }
            else if (rng.NextBool(0.3))
            {
                // Random wander
                var dir = CardinalDirs[rng.Next(4)];
                intents.Add(new MoveIntent(entity.Id, dir));
            }
        }

        return intents.ToImmutable();
    }

    // ── Pathfinding helpers ──────────────────────────────────────────────────

    /// <summary>
    /// Returns the best single-step direction toward <paramref name="target"/>.
    /// Tries the diagonal first, then falls back to the dominant axis,
    /// then the secondary axis. Returns <see cref="Position.Zero"/> if stuck.
    /// </summary>
    private static Position ChaseDirection(
        Position  from,
        Position  to,
        GameState state)
    {
        var dx = Math.Sign(to.X - from.X);
        var dy = Math.Sign(to.Y - from.Y);

        // Try diagonal
        if (dx != 0 && dy != 0)
        {
            var diag = new Position(dx, dy);
            if (IsPassable(state, from + diag)) return diag;
        }

        // Try dominant axis
        if (Math.Abs(to.X - from.X) >= Math.Abs(to.Y - from.Y))
        {
            if (dx != 0 && IsPassable(state, from + new Position(dx, 0))) return new Position(dx, 0);
            if (dy != 0 && IsPassable(state, from + new Position(0, dy))) return new Position(0, dy);
        }
        else
        {
            if (dy != 0 && IsPassable(state, from + new Position(0, dy))) return new Position(0, dy);
            if (dx != 0 && IsPassable(state, from + new Position(dx, 0))) return new Position(dx, 0);
        }

        return Position.Zero;
    }

    /// <summary>
    /// Passable = walkable tile. Occupied tiles are still "passable" for the AI
    /// because bumping into the player resolves as a melee attack via MovementSystem.
    /// </summary>
    private static bool IsPassable(GameState state, Position p) =>
        state.IsWalkable(p);
}
