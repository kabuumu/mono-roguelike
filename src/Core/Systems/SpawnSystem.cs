namespace MonoRogue.Core.Systems;

using System.Collections.Immutable;
using System.Linq;
using MonoRogue.Core.Events;
using MonoRogue.Core.Intents;
using MonoRogue.Core.Model;
using MonoRogue.Data;

/// <summary>
/// Pure Phase-2 validation for spawn intents.
/// Translates a template ID + position into a fully-formed <see cref="SpawnedEvent"/>.
/// The registry is injected — the system never loads files directly.
/// </summary>
public static class SpawnSystem
{
    public static ImmutableArray<GameEvent> Process(
        GameState                   state,
        ImmutableArray<SpawnIntent> intents,
        DataRegistry                registry)
    {
        var events = ImmutableArray.CreateBuilder<GameEvent>(intents.Length);

        foreach (var intent in intents)
        {
            if (!registry.Templates.TryGetValue(intent.TemplateId, out var template))
                continue;

            var entity = BuildEntity(template, intent.Position, intent.IsPlayer);
            events.Add(new SpawnedEvent(entity, intent.IsPlayer));
        }

        return events.ToImmutable();
    }

    // ── Entity builder ────────────────────────────────────────────────────────

    public static Entity BuildEntity(EntityTemplate t, Position pos, bool isPlayer = false)
    {
        // Parse item type
        ItemType? parsedItemType = t.ItemType?.ToLowerInvariant() switch
        {
            "consumable" => ItemType.Consumable,
            "weapon"     => ItemType.Weapon,
            "armor"      => ItemType.Armor,
            _            => null
        };

        bool needsInventory = t.HasInventory || isPlayer || (t.ShopInventory?.Length > 0);

        return new Entity(
            Id:          Guid.NewGuid(),
            Identity:    new IdentityComponent(t.Name, t.Id),
            Spatial:     new SpatialComponent(pos, t.BlocksMovement),
            Render:      new RenderComponent(t.SpriteKey, t.ColorTint),
            Health:      t.MaxHealth > 0
                             ? new HealthComponent(t.MaxHealth, t.MaxHealth)
                             : null,
            Player:      isPlayer    ? new PlayerTag()     : null,
            Tile:        t.IsTile    ? new TileTag()       : null,
            StairsDown:  t.IsStairs  ? new StairsDownTag() : null,
            CombatStats: t.Attack > 0 || t.Defense > 0 || t.XpValue > 0 || isPlayer
                             ? new CombatStatsComponent(t.Attack, t.Defense, t.XpValue)
                             : null,
            Level:       isPlayer
                             ? new LevelComponent(Level: 1, Xp: 0, XpToNextLevel: 100)
                             : null,
            Item:        t.IsItem && parsedItemType.HasValue
                             ? new ItemComponent(parsedItemType.Value,
                                 t.AttackBonus, t.DefenseBonus, t.HealAmount, t.Value)
                             : null,
            Inventory:   needsInventory
                             ? new InventoryComponent([], t.InventorySlots, null, t.StartingGold)
                             : null,
            Equipment:   t.HasEquipment || isPlayer
                             ? new EquipmentComponent()
                             : null,
            Ai:          t.HasAi
                             ? new AiComponent(AiState.Wander, t.Faction)
                             : null,
            Dialogue:    t.HasDialogue && t.DialogPool?.Count > 0
                             ? new DialogueComponent(
                                 t.DialogPool.ToImmutableDictionary(
                                     kvp => kvp.Key,
                                     kvp => kvp.Value.ToImmutableArray()),
                                 t.DialogPool.Keys.ToImmutableArray())
                             : null,
            Peaceful:    t.IsPeaceful ? new PeacefulTag() : null
        );
    }
}
