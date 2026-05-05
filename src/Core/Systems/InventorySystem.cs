namespace MonoRogue.Core.Systems;

using System.Collections.Immutable;
using MonoRogue.Core.Events;
using MonoRogue.Core.Intents;
using MonoRogue.Core.Model;

/// <summary>
/// Pure validation for inventory-related intents.
/// Pickup, use, and auto-equip logic all lives here.
/// </summary>
public static class InventorySystem
{
    // ── Pickup ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Attempts to pick up the top item at the entity's current position.
    /// Emits ItemPickedUpEvent and, for equippable items with an empty slot,
    /// also emits ItemEquippedEvent.
    /// </summary>
    public static ImmutableArray<GameEvent> ProcessPickups(
        GameState                   state,
        ImmutableArray<PickupIntent> intents)
    {
        var events = ImmutableArray.CreateBuilder<GameEvent>();

        foreach (var intent in intents)
        {
            if (!state.Entities.TryGetValue(intent.EntityId, out var picker)) continue;
            if (picker.Spatial is null)    continue;
            if (picker.Inventory is null)  continue;

            var item = state.EntitiesAt(picker.Spatial.Position)
                            .FirstOrDefault(e => e.IsItem() && e.Spatial is not null);
            if (item is null) continue;

            if (picker.Inventory.Items.Length >= picker.Inventory.MaxSlots)
            {
                events.Add(new MessageLoggedEvent("Your pack is full!"));
                continue;
            }

            events.Add(new ItemPickedUpEvent(intent.EntityId, item.Id));
            events.Add(new MessageLoggedEvent(
                $"You pick up the {item.Identity?.Name ?? "item"}."));

            // Auto-equip into an empty slot
            if (item.Item?.Type == ItemType.Weapon &&
                picker.Equipment?.WeaponId is null)
            {
                events.Add(new ItemEquippedEvent(intent.EntityId, item.Id, EquipSlot.Weapon));
                events.Add(new MessageLoggedEvent(
                    $"You equip the {item.Identity?.Name ?? "weapon"}."));
            }
            else if (item.Item?.Type == ItemType.Armor &&
                     picker.Equipment?.ArmorId is null)
            {
                events.Add(new ItemEquippedEvent(intent.EntityId, item.Id, EquipSlot.Armor));
                events.Add(new MessageLoggedEvent(
                    $"You equip the {item.Identity?.Name ?? "armour"}."));
            }
        }

        return events.ToImmutable();
    }

    // ── Use item ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Attempts to use a specific item from the entity's inventory.
    /// Only consumables are usable this way.
    /// </summary>
    public static ImmutableArray<GameEvent> ProcessUseItem(
        GameState                     state,
        ImmutableArray<UseItemIntent>  intents)
    {
        var events = ImmutableArray.CreateBuilder<GameEvent>();

        foreach (var intent in intents)
        {
            if (!state.Entities.TryGetValue(intent.EntityId, out var user)) continue;
            if (user.Inventory is null) continue;
            if (!state.Entities.TryGetValue(intent.ItemId, out var item))   continue;
            if (item.Item?.Type != ItemType.Consumable)                     continue;
            if (!user.Inventory.Items.Contains(intent.ItemId))              continue;

            // Already at full HP?
            if (user.Health is { } hp && hp.Current >= hp.Max && item.Item.HealAmount > 0)
            {
                events.Add(new MessageLoggedEvent("You are already at full health."));
                continue;
            }

            events.Add(new ItemUsedEvent(intent.EntityId, intent.ItemId, item.Item.HealAmount));
            events.Add(new MessageLoggedEvent(
                $"You use the {item.Identity?.Name ?? "item"}."));

            if (item.Item.HealAmount > 0)
                events.Add(new MessageLoggedEvent(
                    $"You recover {item.Item.HealAmount} HP."));
        }

        return events.ToImmutable();
    }

    // ── Equip / Unequip ──────────────────────────────────────────────────────

    /// <summary>
    /// Equips or unequips an item from the entity's inventory.
    /// - Consumables: rejected with a message.
    /// - Item already equipped in its slot: unequipped back to inventory (slot cleared).
    /// - Item not equipped, slot empty: equipped.
    /// - Item not equipped, slot occupied: old item unequipped, new item equipped.
    /// The item must already be in the entity's InventoryComponent.Items.
    /// </summary>
    public static ImmutableArray<GameEvent> ProcessEquipItem(
        GameState                       state,
        ImmutableArray<EquipItemIntent> intents)
    {
        var events = ImmutableArray.CreateBuilder<GameEvent>();

        foreach (var intent in intents)
        {
            if (!state.Entities.TryGetValue(intent.EntityId, out var equipper)) continue;
            if (equipper.Inventory is null)  continue;
            if (equipper.Equipment is null)  continue;
            if (!state.Entities.TryGetValue(intent.ItemId, out var item)) continue;
            if (!equipper.Inventory.Items.Contains(intent.ItemId)) continue;

            var itemType = item.Item?.Type;
            if (itemType == ItemType.Consumable || itemType is null)
            {
                events.Add(new MessageLoggedEvent("You can't equip that."));
                continue;
            }

            var slot      = itemType == ItemType.Weapon ? EquipSlot.Weapon : EquipSlot.Armor;
            var currentId = slot == EquipSlot.Weapon
                ? equipper.Equipment.WeaponId
                : equipper.Equipment.ArmorId;
            var name = item.Identity?.Name ?? "item";

            if (currentId == intent.ItemId)
            {
                // Toggle: already equipped — unequip it
                events.Add(new ItemUnequippedEvent(intent.EntityId, intent.ItemId, slot));
                events.Add(new MessageLoggedEvent($"You unequip the {name}."));
            }
            else
            {
                // Swap out current occupant first (if any)
                if (currentId.HasValue)
                    events.Add(new ItemUnequippedEvent(intent.EntityId, currentId.Value, slot));

                events.Add(new ItemEquippedEvent(intent.EntityId, intent.ItemId, slot));
                events.Add(new MessageLoggedEvent($"You equip the {name}."));
            }
        }

        return events.ToImmutable();
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Finds the first consumable item in the entity's inventory.
    /// Returns null if none found.
    /// </summary>
    public static Guid? FindFirstConsumable(GameState state, Entity entity)
    {
        if (entity.Inventory is null) return null;

        foreach (var itemId in entity.Inventory.Items)
        {
            if (state.Entities.TryGetValue(itemId, out var item) &&
                item.Item?.Type == ItemType.Consumable)
                return itemId;
        }
        return null;
    }
}
