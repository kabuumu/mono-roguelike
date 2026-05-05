# Equip Items from Inventory — Design

**Date:** 2026-05-05

## Problem

The game already auto-equips items into empty slots when picked up, and the character screen (P) shows what is equipped. However, the inventory screen (I) provides no way to manually equip, swap, or unequip weapons and armour. Players cannot change gear once a slot is filled.

## Proposed Solution

Add a manual equip/unequip action to the inventory screen using Option A: a new `EquipItemIntent` flowing through the standard intent → system → event → reducer pipeline.

---

## Data & Events

### New intent (`Intents.cs`)
```csharp
public sealed record EquipItemIntent(Guid EntityId, Guid ItemId) : Intent;
```

### New event (`Events.cs`)
```csharp
public sealed record ItemUnequippedEvent(Guid EntityId, Guid ItemId, EquipSlot Slot) : GameEvent;
```

No changes to `EquipmentComponent`, `ItemComponent`, or `EquipSlot`. `ItemEquippedEvent` and its Reducer handler already exist and are reused unchanged.

---

## Logic — InventorySystem

New method `ProcessEquipItem(GameState state, ImmutableArray<EquipItemIntent> intents)`:

1. Validate the item is in the entity's inventory. Reject silently if not.
2. Determine the target slot from `ItemType`: `Weapon` → `EquipSlot.Weapon`, `Armor` → `EquipSlot.Armor`. Consumables emit a "You can't equip that." message and return.
3. **Toggle unequip:** If the item is already in the target slot, emit `ItemUnequippedEvent` + log "You unequip the {name}."
4. **Swap:** If the slot holds a different item, emit `ItemUnequippedEvent` for the old item (it remains in inventory), then emit `ItemEquippedEvent` for the new item + log "You equip the {name}."
5. **Empty slot:** Emit `ItemEquippedEvent` + log "You equip the {name}."

---

## Reducer

Add a case for `ItemUnequippedEvent`:
- Clears `WeaponId` or `ArmorId` on the equipper's `EquipmentComponent` (set to `null`).
- The item itself stays in `InventoryComponent.Items` — no entity removal.

---

## Input — MonoRogueGame.Update

In the inventory navigation block:
- **`E`** key on selected weapon/armour → close inventory stays open, dispatch `EquipItemIntent`.
- **`E`** on a consumable → append message "You can't equip that."

---

## UI — DrawInventoryBox (AsciiRenderer)

### Equipped section (new, above Items list)
```
INVENTORY
─────────────────────────
Gold: 42g

Equipped:
  Weapon: Iron Sword
  Armor:  —

Items:
  * Iron Sword          ← asterisk marks currently equipped items
    Health Potion
```

- Separator line between Equipped and Items sections.
- Empty slots display `—`.
- Items that are currently equipped (WeaponId or ArmorId matches) display a `*` prefix.
- Key hint updated: `[Up/Down] Select   [E] Equip/Unequip   [U/Enter] Use   [I/ESC] Close`

---

## Files Changed

| File | Change |
|---|---|
| `src/Core/Intents/Intents.cs` | Add `EquipItemIntent` |
| `src/Core/Events/Events.cs` | Add `ItemUnequippedEvent` |
| `src/Core/Systems/InventorySystem.cs` | Add `ProcessEquipItem` |
| `src/Core/Reducer.cs` | Handle `ItemUnequippedEvent` |
| `src/Core/GameLoop.cs` | Route `EquipItemIntent` to `InventorySystem.ProcessEquipItem` |
| `src/Shell/MonoRogueGame.cs` | Handle `E` key in inventory mode, dispatch `EquipItemIntent` |
| `src/Shell/AsciiRenderer.cs` | Add Equipped section + asterisk markers + updated key hint |
