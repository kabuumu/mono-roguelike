namespace MonoRogue.Tests;

using System.Collections.Immutable;
using MonoRogue.Core.Events;
using MonoRogue.Core.Intents;
using MonoRogue.Core.Model;
using MonoRogue.Core.Systems;
using Xunit;

using static TestHelpers;

/// <summary>
/// Tests for InventorySystem — item pickup, auto-equip, and consumable use.
/// All tests are pure: no file I/O, no MonoGame.
/// </summary>
public sealed class InventorySystemTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static ImmutableArray<GameEvent> TryPickup(GameState state, Entity picker) =>
        InventorySystem.ProcessPickups(
            state,
            ImmutableArray.Create(new PickupIntent(picker.Id)));

    private static ImmutableArray<GameEvent> TryUse(
        GameState state, Entity user, Guid itemId) =>
        InventorySystem.ProcessUseItem(
            state,
            ImmutableArray.Create(new UseItemIntent(user.Id, itemId)));

    // ── Pickup ────────────────────────────────────────────────────────────────

    [Fact]
    public void Pickup_item_on_same_tile_emits_pickup_event()
    {
        var player = MakePlayer(new Position(3, 3));
        var item   = MakeItem(new Position(3, 3));
        var state  = MakeState(20, 20, player, item);

        var events = TryPickup(state, player);

        Assert.Contains(events, e => e is ItemPickedUpEvent p && p.ItemId == item.Id);
    }

    [Fact]
    public void Pickup_emits_message_with_item_name()
    {
        var player = MakePlayer(new Position(3, 3));
        var item   = MakeItem(new Position(3, 3));
        var state  = MakeState(20, 20, player, item);

        var events = TryPickup(state, player);

        Assert.Contains(events, e => e is MessageLoggedEvent m &&
            m.Message.Contains("Potion", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Pickup_when_no_item_on_tile_emits_nothing()
    {
        var player = MakePlayer(new Position(3, 3));
        var item   = MakeItem(new Position(9, 9)); // different tile
        var state  = MakeState(20, 20, player, item);

        var events = TryPickup(state, player);

        Assert.Empty(events);
    }

    [Fact]
    public void Pickup_when_inventory_full_emits_warning_not_pickup()
    {
        // Fill inventory to capacity
        var fullItems = Enumerable.Range(0, 10).Select(_ => Guid.NewGuid()).ToImmutableArray();
        var player = MakePlayer(new Position(3, 3)) with
        {
            Inventory = new InventoryComponent(fullItems, MaxSlots: 10)
        };
        var item  = MakeItem(new Position(3, 3));
        var state = MakeState(20, 20, player, item);

        var events = TryPickup(state, player);

        Assert.DoesNotContain(events, e => e is ItemPickedUpEvent);
        Assert.Contains(events, e => e is MessageLoggedEvent m && m.Message.Contains("full"));
    }

    // ── Auto-equip on pickup ──────────────────────────────────────────────────

    [Fact]
    public void Pickup_weapon_with_empty_slot_auto_equips()
    {
        var player = MakePlayer(new Position(3, 3)); // Equipment.WeaponId = null
        var weapon = MakeWeapon(new Position(3, 3));
        var state  = MakeState(20, 20, player, weapon);

        var events = TryPickup(state, player);

        Assert.Contains(events, e => e is ItemEquippedEvent eq &&
            eq.Slot == EquipSlot.Weapon && eq.ItemId == weapon.Id);
    }

    [Fact]
    public void Pickup_armor_with_empty_slot_auto_equips()
    {
        var player = MakePlayer(new Position(3, 3));
        var armor  = MakeArmor(new Position(3, 3));
        var state  = MakeState(20, 20, player, armor);

        var events = TryPickup(state, player);

        Assert.Contains(events, e => e is ItemEquippedEvent eq &&
            eq.Slot == EquipSlot.Armor && eq.ItemId == armor.Id);
    }

    [Fact]
    public void Pickup_weapon_when_slot_full_does_not_auto_equip()
    {
        var existingWeaponId = Guid.NewGuid();
        var player = MakePlayer(new Position(3, 3)) with
        {
            Equipment = new EquipmentComponent(WeaponId: existingWeaponId)
        };
        var newWeapon = MakeWeapon(new Position(3, 3));
        var state     = MakeState(20, 20, player, newWeapon);

        var events = TryPickup(state, player);

        // Pickup should happen
        Assert.Contains(events, e => e is ItemPickedUpEvent);
        // But auto-equip should NOT
        Assert.DoesNotContain(events, e => e is ItemEquippedEvent);
    }

    // ── UseItem ───────────────────────────────────────────────────────────────

    [Fact]
    public void UseItem_consumable_emits_used_heal_and_message_events()
    {
        var potion = MakeItem(new Position(0, 0), healAmount: 10);
        var player = MakePlayer(new Position(3, 3)) with
        {
            Health    = new HealthComponent(Current: 5, Max: 20),
            Inventory = new InventoryComponent(
                ImmutableArray<Guid>.Empty.Add(potion.Id), MaxSlots: 10)
        };
        var state = MakeState(20, 20, player, potion);

        var events = TryUse(state, player, potion.Id);

        Assert.Contains(events, e => e is ItemUsedEvent);
        Assert.Contains(events, e => e is MessageLoggedEvent m && m.Message.Contains("use"));
        Assert.Contains(events, e => e is MessageLoggedEvent m && m.Message.Contains("10 HP"));
    }

    [Fact]
    public void UseItem_at_full_hp_emits_warning_not_used()
    {
        var potion = MakeItem(new Position(0, 0), healAmount: 10);
        var player = MakePlayer(new Position(3, 3)) with
        {
            // Already at max HP
            Health    = new HealthComponent(Current: 20, Max: 20),
            Inventory = new InventoryComponent(
                ImmutableArray<Guid>.Empty.Add(potion.Id), MaxSlots: 10)
        };
        var state = MakeState(20, 20, player, potion);

        var events = TryUse(state, player, potion.Id);

        Assert.DoesNotContain(events, e => e is ItemUsedEvent);
        Assert.Contains(events, e => e is MessageLoggedEvent m &&
            m.Message.Contains("already", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void UseItem_not_in_inventory_is_ignored()
    {
        var potion = MakeItem(new Position(0, 0), healAmount: 10);
        // Player doesn't have the item in their inventory
        var player = MakePlayer(new Position(3, 3));
        var state  = MakeState(20, 20, player, potion);

        var events = TryUse(state, player, potion.Id);

        Assert.Empty(events);
    }

    [Fact]
    public void UseItem_non_consumable_is_ignored()
    {
        var weapon = MakeWeapon(new Position(0, 0));
        var player = MakePlayer(new Position(3, 3)) with
        {
            Inventory = new InventoryComponent(
                ImmutableArray<Guid>.Empty.Add(weapon.Id), MaxSlots: 10)
        };
        var state = MakeState(20, 20, player, weapon);

        var events = TryUse(state, player, weapon.Id);

        Assert.Empty(events);
    }

    [Fact]
    public void UseItem_unknown_entity_id_is_ignored()
    {
        var player = MakePlayer(new Position(3, 3));
        var state  = MakeState(20, 20, player);

        var events = TryUse(state, player, Guid.NewGuid());

        Assert.Empty(events);
    }

    // ── EquipItem ─────────────────────────────────────────────────────────────

    private static ImmutableArray<GameEvent> TryEquip(
        GameState state, Entity equipper, Guid itemId) =>
        InventorySystem.ProcessEquipItem(
            state,
            ImmutableArray.Create(new EquipItemIntent(equipper.Id, itemId)));

    [Fact]
    public void EquipItem_weapon_into_empty_slot_emits_equipped_event()
    {
        var weapon = MakeWeapon(new Position(0, 0));
        var player = MakePlayer(new Position(3, 3)) with
        {
            Inventory = new InventoryComponent(
                ImmutableArray<Guid>.Empty.Add(weapon.Id), MaxSlots: 10)
        };
        var state = MakeState(20, 20, player, weapon);

        var events = TryEquip(state, player, weapon.Id);

        Assert.Contains(events, e => e is ItemEquippedEvent eq &&
            eq.Slot == EquipSlot.Weapon && eq.ItemId == weapon.Id);
    }

    [Fact]
    public void EquipItem_armor_into_empty_slot_emits_equipped_event()
    {
        var armor  = MakeArmor(new Position(0, 0));
        var player = MakePlayer(new Position(3, 3)) with
        {
            Inventory = new InventoryComponent(
                ImmutableArray<Guid>.Empty.Add(armor.Id), MaxSlots: 10)
        };
        var state = MakeState(20, 20, player, armor);

        var events = TryEquip(state, player, armor.Id);

        Assert.Contains(events, e => e is ItemEquippedEvent eq &&
            eq.Slot == EquipSlot.Armor && eq.ItemId == armor.Id);
    }

    [Fact]
    public void EquipItem_weapon_when_slot_occupied_emits_unequip_then_equip()
    {
        var oldWeapon = MakeWeapon(new Position(0, 0));
        var newWeapon = MakeWeapon(new Position(0, 0));
        var player = MakePlayer(new Position(3, 3)) with
        {
            Inventory = new InventoryComponent(
                ImmutableArray<Guid>.Empty.Add(oldWeapon.Id).Add(newWeapon.Id), MaxSlots: 10),
            Equipment = new EquipmentComponent(WeaponId: oldWeapon.Id)
        };
        var state = MakeState(20, 20, player, oldWeapon, newWeapon);

        var events = TryEquip(state, player, newWeapon.Id);

        Assert.Contains(events, e => e is ItemUnequippedEvent u &&
            u.ItemId == oldWeapon.Id && u.Slot == EquipSlot.Weapon);
        Assert.Contains(events, e => e is ItemEquippedEvent eq &&
            eq.ItemId == newWeapon.Id && eq.Slot == EquipSlot.Weapon);
        // Unequip must precede equip so Reducer applies them in correct order
        int unequipIdx = events.ToList().FindIndex(e => e is ItemUnequippedEvent);
        int equipIdx   = events.ToList().FindIndex(e => e is ItemEquippedEvent);
        Assert.True(unequipIdx < equipIdx);
    }

    [Fact]
    public void EquipItem_already_equipped_item_emits_unequip_only()
    {
        var weapon = MakeWeapon(new Position(0, 0));
        var player = MakePlayer(new Position(3, 3)) with
        {
            Inventory = new InventoryComponent(
                ImmutableArray<Guid>.Empty.Add(weapon.Id), MaxSlots: 10),
            Equipment = new EquipmentComponent(WeaponId: weapon.Id)
        };
        var state = MakeState(20, 20, player, weapon);

        var events = TryEquip(state, player, weapon.Id);

        Assert.Contains(events, e => e is ItemUnequippedEvent u &&
            u.ItemId == weapon.Id && u.Slot == EquipSlot.Weapon);
        Assert.DoesNotContain(events, e => e is ItemEquippedEvent);
    }

    [Fact]
    public void EquipItem_consumable_emits_cannot_equip_message()
    {
        var potion = MakeItem(new Position(0, 0), healAmount: 5);
        var player = MakePlayer(new Position(3, 3)) with
        {
            Inventory = new InventoryComponent(
                ImmutableArray<Guid>.Empty.Add(potion.Id), MaxSlots: 10)
        };
        var state = MakeState(20, 20, player, potion);

        var events = TryEquip(state, player, potion.Id);

        Assert.DoesNotContain(events, e => e is ItemEquippedEvent);
        Assert.Contains(events, e => e is MessageLoggedEvent m &&
            m.Message.Contains("can't equip", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void EquipItem_not_in_inventory_is_ignored()
    {
        var weapon = MakeWeapon(new Position(0, 0));
        // Player has no items in inventory
        var player = MakePlayer(new Position(3, 3));
        var state  = MakeState(20, 20, player, weapon);

        var events = TryEquip(state, player, weapon.Id);

        Assert.Empty(events);
    }
}
