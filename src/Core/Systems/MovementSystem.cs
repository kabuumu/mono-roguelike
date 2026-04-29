namespace MonoRogue.Core.Systems;

using System.Collections.Immutable;
using MonoRogue.Core.Events;
using MonoRogue.Core.Intents;
using MonoRogue.Core.Model;

/// <summary>
/// Pure Phase-2 validation for movement intents.
///
/// Handles two cases when the target tile is occupied:
///   1. Target is a living combatant (not a tile) → melee attack via CombatSystem.
///   2. Target is a wall or inert blocker → silently skip (no event).
///
/// Uses a local claimed-position accumulator so two entities cannot both
/// move to the same tile in the same tick.
/// </summary>
public static class MovementSystem
{
    public static ImmutableArray<GameEvent> Process(
        GameState                  state,
        ImmutableArray<MoveIntent> intents)
    {
        var claimed = ImmutableHashSet<Position>.Empty;
        var events  = ImmutableArray.CreateBuilder<GameEvent>(intents.Length * 2);

        foreach (var intent in intents)
        {
            if (!state.Entities.TryGetValue(intent.EntityId, out var mover)) continue;
            if (mover.Spatial is null) continue;

            var target = mover.Spatial.Position + intent.Direction;

            // Guard: out of bounds
            if (!state.InBounds(target)) continue;

            // Player bumps a closed door → open it (costs the turn, no movement)
            if (mover.IsPlayer())
            {
                var door = state.EntitiesAt(target)
                    .FirstOrDefault(e => e.Identity?.TemplateId == "tile_door_closed");
                if (door is not null)
                {
                    events.Add(new DoorOpenedEvent(door.Id));
                    events.Add(new MessageLoggedEvent("You open the door."));
                    continue;
                }
            }

            // Guard: static blocking tile (wall)
            if (!state.IsWalkable(target)) continue;

            // Check if target is occupied by a living combatant
            var occupant = state.EntitiesAt(target)
                .FirstOrDefault(e => e.BlocksMovement() && !e.IsTile());

            if (occupant is not null)
            {
                // Player bumps an NPC → signal DialogueSystem to open conversation
                if (mover.IsPlayer() && occupant.IsNpc())
                {
                    events.Add(new NpcBumpedEvent(occupant.Id));
                    continue;
                }

                // Only resolve as melee attack if one side is the player
                // and the occupant is not peaceful (villagers don't fight)
                bool playerMelee = mover.IsPlayer() && !occupant.IsPlayer() && !occupant.IsPeaceful();
                bool enemyMelee  = !mover.IsPlayer() && occupant.IsPlayer();

                if ((playerMelee || enemyMelee) && occupant.IsAlive())
                    events.AddRange(CombatSystem.Resolve(state, intent.EntityId, occupant.Id));

                continue; // Never "move into" an occupied tile
            }

            // Guard: another intent in this batch already claimed the tile
            if (claimed.Contains(target)) continue;

            claimed = claimed.Add(target);
            events.Add(new MovedEvent(intent.EntityId, target));
        }

        return events.ToImmutable();
    }
}
