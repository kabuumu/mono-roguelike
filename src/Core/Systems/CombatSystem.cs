namespace MonoRogue.Core.Systems;

using System.Collections.Immutable;
using MonoRogue.Core.Events;
using MonoRogue.Core.Intents;
using MonoRogue.Core.Model;
using MonoRogue.Data;

/// <summary>
/// Pure melee combat resolution.
/// All functions are static — no hidden state, no RNG object stored.
/// Variance is derived deterministically from a hash of attacker + target + turn,
/// so the same seed always produces the same battle outcome.
/// </summary>
public static class CombatSystem
{
    /// <summary>
    /// Resolves one melee attack from <paramref name="attackerId"/> against
    /// <paramref name="targetId"/> and returns the resulting events.
    ///
    /// Events emitted (in order):
    ///   MessageLoggedEvent  (attack narrative)
    ///   DamagedEvent
    ///   [DiedEvent + MessageLoggedEvent + XpGainedEvent]  (if target dies)
    /// </summary>
    public static ImmutableArray<GameEvent> Resolve(
        GameState state,
        Guid      attackerId,
        Guid      targetId)
    {
        if (!state.Entities.TryGetValue(attackerId, out var attacker)) return [];
        if (!state.Entities.TryGetValue(targetId,   out var target))   return [];
        if (!target.IsAlive()) return [];

        var attackPower  = GetEffectiveAttack(state,  attacker);
        var defensePower = GetEffectiveDefense(state, target);

        // Deterministic ±1 variance — no hidden RNG state
        var raw      = HashCode.Combine(attackerId, targetId, state.TurnNumber);
        var variance = ((raw % 3) + 3) % 3 - 1; // −1, 0, or +1

        var damage = Math.Max(1, attackPower - defensePower + variance);

        var attackerName = attacker.Identity?.Name ?? "Unknown";
        var targetName   = target.Identity?.Name   ?? "Unknown";

        var events = ImmutableArray.CreateBuilder<GameEvent>(4);
        events.Add(new MessageLoggedEvent(
            $"{attackerName} hits {targetName} for {damage} damage."));
        events.Add(new DamagedEvent(targetId, damage, attackerId));

        if (attacker.IsPlayer())
            events.Add(new SkillXpGainedEvent(attackerId, SkillType.Melee, 1));
        if (target.IsPlayer())
            events.Add(new SkillXpGainedEvent(targetId, SkillType.Block, 1));

        // Check if target would die
        var remainingHp = Math.Max(0, (target.Health?.Current ?? 0) - damage);
        if (remainingHp <= 0)
        {
            events.Add(new DiedEvent(targetId));
            events.Add(new MessageLoggedEvent($"{targetName} dies!"));

            // XP grant to player when they kill an enemy
            if (attacker.IsPlayer() && (target.CombatStats?.XpValue ?? 0) > 0)
                events.Add(new XpGainedEvent(attackerId, target.CombatStats!.XpValue));
        }

        return events.ToImmutable();
    }

    /// <summary>
    /// Resolves explicit <see cref="AttackIntent"/>s — used when the attacker
    /// already has a known target (e.g. a bumping AI that is adjacent to the player).
    /// Loot drops are handled separately by <see cref="RollAllLoot"/>.
    /// </summary>
    public static ImmutableArray<GameEvent> ProcessAttackIntents(
        GameState                    state,
        ImmutableArray<AttackIntent> intents)
    {
        var events = ImmutableArray.CreateBuilder<GameEvent>();
        foreach (var intent in intents)
            events.AddRange(Resolve(state, intent.SourceId, intent.TargetId));
        return events.ToImmutable();
    }

    /// <summary>
    /// Event-listener pass: scans <paramref name="frameEvents"/> for
    /// <see cref="DiedEvent"/>s, looks up each dead entity's loot table,
    /// and returns <see cref="SpawnedEvent"/>s (items) and
    /// <see cref="GoldChangedEvent"/>s for the player.
    ///
    /// Call this in GameLoop AFTER all combat events are collected,
    /// BEFORE calling Reducer.Reduce — so loot enters the same reduction pass.
    /// </summary>
    public static ImmutableArray<GameEvent> RollAllLoot(
        GameState                    state,
        ImmutableArray<GameEvent>    frameEvents,
        DataRegistry                 registry,
        SeededRandom                 rng)
    {
        var loot = ImmutableArray.CreateBuilder<GameEvent>();

        foreach (var evt in frameEvents)
        {
            if (evt is not DiedEvent died) continue;
            if (!state.Entities.TryGetValue(died.EntityId, out var entity)) continue;

            loot.AddRange(RollEntityLoot(entity, state, registry, rng));
        }

        return loot.ToImmutable();
    }

    // ── Loot helper ──────────────────────────────────────────────────────────

    private static ImmutableArray<GameEvent> RollEntityLoot(
        Entity       entity,
        GameState    state,
        DataRegistry registry,
        SeededRandom rng)
    {
        if (entity.Identity is null || entity.Spatial is null) return [];
        if (!registry.Templates.TryGetValue(entity.Identity.TemplateId, out var template)) return [];
        if (template.Drops is null || template.Drops.Length == 0) return [];

        var events  = ImmutableArray.CreateBuilder<GameEvent>();
        var dropPos = entity.Spatial.Position;

        foreach (var drop in template.Drops)
        {
            var dropRng = rng.Split($"{entity.Id}_{drop.ItemId ?? "gold"}");
            if (!dropRng.NextBool(drop.Chance)) continue;

            if (drop.GoldAmount > 0 && state.PlayerEntityId != Guid.Empty)
            {
                events.Add(new GoldChangedEvent(state.PlayerEntityId, drop.GoldAmount));
                events.Add(new MessageLoggedEvent($"You find {drop.GoldAmount} gold."));
            }
            else if (!string.IsNullOrEmpty(drop.ItemId) &&
                     registry.Templates.TryGetValue(drop.ItemId, out var itemTpl))
            {
                var lootEntity = SpawnSystem.BuildEntity(itemTpl, dropPos);
                events.Add(new SpawnedEvent(lootEntity));
                events.Add(new MessageLoggedEvent(
                    $"A {lootEntity.Identity?.Name ?? drop.ItemId} drops to the ground."));
            }
        }

        return events.ToImmutable();
    }

    // ── Effective stat helpers ───────────────────────────────────────────────

    public static int GetEffectiveAttack(GameState state, Entity e)
    {
        var baseAtk = e.CombatStats?.Attack ?? 1;
        if (e.Equipment?.WeaponId is { } wId &&
            state.Entities.TryGetValue(wId, out var weapon))
            baseAtk += weapon.Item?.AttackBonus ?? 0;
        baseAtk += e.Skills?.Melee.Level ?? 0;
        return baseAtk;
    }

    public static int GetEffectiveDefense(GameState state, Entity e)
    {
        var baseDef = e.CombatStats?.Defense ?? 0;
        if (e.Equipment?.ArmorId is { } aId &&
            state.Entities.TryGetValue(aId, out var armor))
            baseDef += armor.Item?.DefenseBonus ?? 0;
        baseDef += e.Skills?.Block.Level ?? 0;
        return baseDef;
    }
}
