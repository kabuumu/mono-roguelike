namespace MonoRogue.Tests;

using System.Collections.Immutable;
using MonoRogue.Core.Events;
using MonoRogue.Core.Intents;
using MonoRogue.Core.Model;
using MonoRogue.Core.Systems;
using Xunit;

using static TestHelpers;

/// <summary>
/// Tests for MovementSystem.Process — legal movement, boundary guards,
/// wall collision, tile-claiming, NPC bump-to-talk, and melee bump.
/// </summary>
public sealed class MovementSystemTests
{
    private static ImmutableArray<GameEvent> Move(
        GameState state, Entity mover, Position direction) =>
        MovementSystem.Process(
            state,
            ImmutableArray.Create(new MoveIntent(mover.Id, direction)));

    // ── Basic movement ────────────────────────────────────────────────────────

    [Fact]
    public void Move_to_open_tile_emits_moved_event()
    {
        var player = MakePlayer(new Position(5, 5));
        var state  = MakeState(20, 20, player);

        var events = Move(state, player, Position.East);

        var moved = Assert.Single(events.OfType<MovedEvent>());
        Assert.Equal(new Position(6, 5), moved.NewPosition);
    }

    [Fact]
    public void Move_out_of_bounds_emits_nothing()
    {
        // Player at (0,0) moving North would be (0,-1) — out of bounds
        var player = MakePlayer(new Position(0, 0));
        var state  = MakeState(20, 20, player);

        var events = Move(state, player, Position.North);

        Assert.Empty(events);
    }

    [Fact]
    public void Move_into_wall_emits_nothing()
    {
        var wallPos = new Position(5, 4);
        var player  = MakePlayer(new Position(5, 5));
        var state   = MakeState(20, 20, player).WithTileBlocked(wallPos);

        var events = Move(state, player, Position.North);

        Assert.Empty(events);
    }

    [Fact]
    public void Move_for_unknown_entity_is_ignored()
    {
        var state  = MakeState(20, 20);
        var events = MovementSystem.Process(
            state,
            ImmutableArray.Create(new MoveIntent(Guid.NewGuid(), Position.East)));

        Assert.Empty(events);
    }

    // ── Tile-claiming (same-frame collision) ──────────────────────────────────

    [Fact]
    public void Two_entities_moving_to_same_tile_only_first_succeeds()
    {
        var e1 = MakePlayer(new Position(3, 5));
        var e2 = MakeEnemy(new Position(7, 5));
        var state = MakeState(20, 20, e1, e2);
        // Both want (5,5)
        var intents = ImmutableArray.Create(
            new MoveIntent(e1.Id, Position.East),   // (3,5)→(4,5) — different target, let's use a closer position
            new MoveIntent(e2.Id, Position.West));  // (7,5)→(6,5) — also different

        // For a true claim test: put both entities at positions that target the same tile
        var a = MakePlayer(new Position(4, 5));
        var b = MakeEnemy(new Position(6, 5));
        var s2 = MakeState(20, 20, a, b);
        // Both move to (5,5)
        var intents2 = ImmutableArray.Create(
            new MoveIntent(a.Id, Position.East),  // 4→5
            new MoveIntent(b.Id, Position.West)); // 6→5

        var events = MovementSystem.Process(s2, intents2);

        var moves = events.OfType<MovedEvent>().ToList();
        Assert.Single(moves); // Only one should reach (5,5)
        Assert.Equal(new Position(5, 5), moves[0].NewPosition);
    }

    // ── NPC bump-to-talk ──────────────────────────────────────────────────────

    [Fact]
    public void Player_bump_into_npc_emits_bump_event_not_combat()
    {
        var player = MakePlayer(new Position(4, 5));
        var npc    = MakeNpc(new Position(5, 5));  // peaceful NPC directly East
        var state  = MakeState(20, 20, player, npc);

        var events = Move(state, player, Position.East);

        // MovementSystem signals the bump; DialogueSystem.ProcessBumps converts it to DialogueOpenedEvent
        Assert.Contains(events, e => e is NpcBumpedEvent { } b && b.NpcId == npc.Id);
        Assert.DoesNotContain(events, e => e is DialogueOpenedEvent);
        Assert.DoesNotContain(events, e => e is DamagedEvent);
        Assert.DoesNotContain(events, e => e is MovedEvent);
    }

    // ── Melee bump ────────────────────────────────────────────────────────────

    [Fact]
    public void Player_bump_into_hostile_enemy_emits_combat_events()
    {
        var player = MakePlayer(new Position(4, 5));
        // Enemy: has Ai but no Peaceful tag — combatant
        var enemy = new Entity(
            Id:       Guid.NewGuid(),
            Identity: new IdentityComponent("Goblin", "enemy_goblin"),
            Spatial:  new SpatialComponent(new Position(5, 5), BlocksMovement: true),
            Health:   new HealthComponent(Current: 10, Max: 10),
            CombatStats: new MonoRogue.Core.Model.CombatStatsComponent(Attack: 2, Defense: 0),
            Ai:       new AiComponent());
        var state = MakeState(20, 20, player, enemy);

        var events = Move(state, player, Position.East);

        Assert.Contains(events, e => e is DamagedEvent);
        Assert.DoesNotContain(events, e => e is MovedEvent);
    }

    [Fact]
    public void Enemy_bump_into_player_emits_combat_events()
    {
        var player = MakePlayer(new Position(5, 5));
        var enemy  = new Entity(
            Id:       Guid.NewGuid(),
            Identity: new IdentityComponent("Goblin", "enemy_goblin"),
            Spatial:  new SpatialComponent(new Position(4, 5), BlocksMovement: true),
            Health:   new HealthComponent(Current: 10, Max: 10),
            CombatStats: new MonoRogue.Core.Model.CombatStatsComponent(Attack: 2, Defense: 0),
            Ai:       new AiComponent());
        var state = MakeState(20, 20, player, enemy);

        var events = MovementSystem.Process(
            state,
            ImmutableArray.Create(new MoveIntent(enemy.Id, Position.East)));

        Assert.Contains(events, e => e is DamagedEvent);
    }

    [Fact]
    public void Player_bump_into_peaceful_npc_does_not_trigger_combat()
    {
        var player     = MakePlayer(new Position(4, 5));
        var peacefulNpc = MakeNpc(new Position(5, 5)); // MakeNpc adds PeacefulTag
        var state      = MakeState(20, 20, player, peacefulNpc);

        var events = Move(state, player, Position.East);

        Assert.DoesNotContain(events, e => e is DamagedEvent);
    }
}
