namespace MonoRogue.Tests;

using System.Collections.Immutable;
using MonoRogue.Core.Events;
using MonoRogue.Core.Intents;
using MonoRogue.Core.Model;
using MonoRogue.Core.Systems;
using Xunit;

using static TestHelpers;

/// <summary>
/// Tests for CombatSystem — damage formula, event sequencing, XP grants.
/// All variance is derived from a deterministic hash so outcomes are stable.
/// </summary>
public sealed class CombatSystemTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    /// Run a combat resolution and return the resulting events.
    private static ImmutableArray<GameEvent> ResolveAttack(
        Entity attacker, Entity target, int turn = 1)
    {
        var state = MakeState(20, 20, attacker, target) with { TurnNumber = turn };
        return CombatSystem.Resolve(state, attacker.Id, target.Id);
    }

    // ── Basic event structure ─────────────────────────────────────────────────

    [Fact]
    public void Resolve_emits_message_and_damaged_events()
    {
        var player = MakePlayer(new Position(1, 1));
        var enemy  = MakeEnemy(new Position(2, 2), hp: 20);

        var events = ResolveAttack(player, enemy);

        Assert.Contains(events, e => e is MessageLoggedEvent);
        Assert.Contains(events, e => e is DamagedEvent);
    }

    [Fact]
    public void Resolve_damage_is_at_least_1()
    {
        // Enemy with very high defense — floor must clamp at 1
        var weakPlayer = MakePlayer(new Position(1, 1)) with
        {
            CombatStats = new MonoRogue.Core.Model.CombatStatsComponent(Attack: 1, Defense: 0)
        };
        var tankEnemy = MakeEnemy(new Position(2, 2), hp: 100, attack: 1) with
        {
            CombatStats = new MonoRogue.Core.Model.CombatStatsComponent(Attack: 1, Defense: 999)
        };

        var events = ResolveAttack(weakPlayer, tankEnemy);

        var dmg = events.OfType<DamagedEvent>().First();
        Assert.True(dmg.Amount >= 1, $"Damage should be at least 1, was {dmg.Amount}");
    }

    [Fact]
    public void Resolve_for_unknown_attacker_returns_empty()
    {
        var state = MakeState(20, 20, MakeEnemy(new Position(1, 1)));

        var events = CombatSystem.Resolve(state, Guid.NewGuid(), Guid.NewGuid());

        Assert.Empty(events);
    }

    [Fact]
    public void Resolve_for_dead_target_returns_empty()
    {
        var player    = MakePlayer(new Position(1, 1));
        var deadEnemy = MakeEnemy(new Position(2, 2), hp: 0); // already dead
        var state     = MakeState(20, 20, player, deadEnemy);

        var events = CombatSystem.Resolve(state, player.Id, deadEnemy.Id);

        Assert.Empty(events);
    }

    // ── Kill events ───────────────────────────────────────────────────────────

    [Fact]
    public void Resolve_kill_emits_died_and_death_message()
    {
        // High-attack player, 1-HP enemy → guaranteed kill
        var bigPlayer = MakePlayer(new Position(1, 1)) with
        {
            CombatStats = new MonoRogue.Core.Model.CombatStatsComponent(Attack: 999, Defense: 0)
        };
        var fragileEnemy = MakeEnemy(new Position(2, 2), hp: 1);

        var events = ResolveAttack(bigPlayer, fragileEnemy);

        Assert.Contains(events, e => e is DiedEvent d && d.EntityId == fragileEnemy.Id);
        Assert.Contains(events, e => e is MessageLoggedEvent m && m.Message.Contains("dies"));
    }

    [Fact]
    public void Resolve_kill_by_player_emits_xp_gained_event()
    {
        var bigPlayer    = MakePlayer(new Position(1, 1)) with
        {
            CombatStats = new MonoRogue.Core.Model.CombatStatsComponent(Attack: 999, Defense: 0)
        };
        var xpEnemy = MakeEnemy(new Position(2, 2), hp: 1, xpValue: 15);

        var events = ResolveAttack(bigPlayer, xpEnemy);

        var xpEvent = events.OfType<XpGainedEvent>().FirstOrDefault();
        Assert.NotNull(xpEvent);
        Assert.Equal(15, xpEvent.Amount);
    }

    [Fact]
    public void Resolve_kill_by_enemy_does_not_emit_xp_gained()
    {
        var strongEnemy = MakeEnemy(new Position(2, 2)) with
        {
            CombatStats = new MonoRogue.Core.Model.CombatStatsComponent(Attack: 999, Defense: 0, XpValue: 5)
        };
        var weakPlayer = MakePlayer(new Position(1, 1)) with
        {
            Health = new MonoRogue.Core.Model.HealthComponent(Current: 1, Max: 1)
        };
        // State needs both; treat enemy as attacker of player
        var state = MakeState(20, 20, weakPlayer, strongEnemy) with { TurnNumber = 1 };

        var events = CombatSystem.Resolve(state, strongEnemy.Id, weakPlayer.Id);

        Assert.DoesNotContain(events, e => e is XpGainedEvent);
    }

    // ── Effective stat helpers ────────────────────────────────────────────────

    [Fact]
    public void GetEffectiveAttack_adds_weapon_bonus()
    {
        var weapon = MakeWeapon(new Position(0, 0), atkBonus: 5);
        var player = MakePlayer(new Position(1, 1)) with
        {
            CombatStats = new MonoRogue.Core.Model.CombatStatsComponent(Attack: 3, Defense: 0),
            Equipment   = new MonoRogue.Core.Model.EquipmentComponent(WeaponId: weapon.Id),
        };
        var state = MakeState(20, 20, player, weapon);

        var effective = CombatSystem.GetEffectiveAttack(state, state.Entities[player.Id]);

        Assert.Equal(8, effective); // 3 base + 5 weapon
    }

    [Fact]
    public void GetEffectiveDefense_adds_armor_bonus()
    {
        var armor  = MakeArmor(new Position(0, 0), defBonus: 4);
        var player = MakePlayer(new Position(1, 1)) with
        {
            CombatStats = new MonoRogue.Core.Model.CombatStatsComponent(Attack: 3, Defense: 1),
            Equipment   = new MonoRogue.Core.Model.EquipmentComponent(ArmorId: armor.Id),
        };
        var state = MakeState(20, 20, player, armor);

        var effective = CombatSystem.GetEffectiveDefense(state, state.Entities[player.Id]);

        Assert.Equal(5, effective); // 1 base + 4 armor
    }

    // ── Batch intents ─────────────────────────────────────────────────────────

    [Fact]
    public void ProcessAttackIntents_resolves_all_intents()
    {
        var player = MakePlayer(new Position(1, 1)) with
        {
            CombatStats = new MonoRogue.Core.Model.CombatStatsComponent(Attack: 999, Defense: 0)
        };
        var enemy1 = MakeEnemy(new Position(2, 1), hp: 1);
        var enemy2 = MakeEnemy(new Position(3, 1), hp: 1);
        var state  = MakeState(20, 20, player, enemy1, enemy2);

        var intents = ImmutableArray.Create(
            new AttackIntent(player.Id, enemy1.Id),
            new AttackIntent(player.Id, enemy2.Id));

        var events = CombatSystem.ProcessAttackIntents(state, intents);

        // At least two damage events — one per enemy
        Assert.True(events.OfType<DamagedEvent>().Count() >= 2);
    }

    // ── Determinism ──────────────────────────────────────────────────────────

    [Fact]
    public void Resolve_is_deterministic_same_inputs_same_damage()
    {
        var player = MakePlayer(new Position(1, 1));
        var enemy  = MakeEnemy(new Position(2, 2), hp: 50);

        var events1 = ResolveAttack(player, enemy, turn: 7);
        var events2 = ResolveAttack(player, enemy, turn: 7);

        var dmg1 = events1.OfType<DamagedEvent>().First().Amount;
        var dmg2 = events2.OfType<DamagedEvent>().First().Amount;
        Assert.Equal(dmg1, dmg2);
    }
}
