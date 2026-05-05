# Equip Items from Inventory — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let players manually equip and unequip weapons/armour from the inventory screen (I), with an "Equipped" section displayed at the top of the panel.

**Architecture:** A new `EquipItemIntent` flows through the standard intent → system → event → reducer pipeline. `InventorySystem.ProcessEquipItem` validates the request and emits `ItemEquippedEvent` / `ItemUnequippedEvent`. The Reducer handles the new unequip event. The shell wires up the `E` key and updates the inventory panel rendering.

**Tech Stack:** C# / MonoGame, xUnit, immutable data model (record + `with`)

---

## File Map

| File | Change |
|---|---|
| `src/Core/Intents/Intents.cs` | Add `EquipItemIntent` |
| `src/Core/Events/Events.cs` | Add `ItemUnequippedEvent` |
| `src/Core/Systems/InventorySystem.cs` | Add `ProcessEquipItem` method |
| `src/Core/Reducer.cs` | Handle `ItemUnequippedEvent` |
| `src/Core/GameLoop.cs` | Route `EquipItemIntent` to `InventorySystem.ProcessEquipItem` |
| `src/Shell/MonoRogueGame.cs` | Handle `E` key in inventory navigation block |
| `src/Shell/AsciiRenderer.cs` | Add Equipped section + `*` markers + updated key hint in `DrawInventoryBox` |
| `tests/MonoRogue.Tests/InventorySystemTests.cs` | Add `ProcessEquipItem` tests |
| `tests/MonoRogue.Tests/ReducerTests.cs` | Add `ItemUnequippedEvent` tests |

---

### Task 1: Add EquipItemIntent and ItemUnequippedEvent

**Files:**
- Modify: `src/Core/Intents/Intents.cs`
- Modify: `src/Core/Events/Events.cs`

- [ ] **Step 1: Add EquipItemIntent to Intents.cs**

  Open `src/Core/Intents/Intents.cs`. After the `TradeIntent` record at the bottom, add:

  ```csharp
  /// <summary>
  /// Player wants to equip or unequip an item from their inventory.
  /// If the item is already in its slot, it is unequipped back to inventory.
  /// If the slot is occupied by a different item, that item is swapped back
  /// to inventory before the new one is equipped.
  /// Consumables are rejected with a message.
  /// </summary>
  public sealed record EquipItemIntent(Guid EntityId, Guid ItemId) : Intent;
  ```

- [ ] **Step 2: Add ItemUnequippedEvent to Events.cs**

  Open `src/Core/Events/Events.cs`. After the `ItemEquippedEvent` line in the Inventory section, add:

  ```csharp
  /// <summary>An item was unequipped from a slot. It remains in InventoryComponent.Items.</summary>
  public sealed record ItemUnequippedEvent(Guid EntityId, Guid ItemId, EquipSlot Slot) : GameEvent;
  ```

- [ ] **Step 3: Build to verify no compile errors**

  ```bash
  cd /Users/robhawkes/mono-roguelike
  dotnet build --no-restore -v q 2>&1 | tail -5
  ```

  Expected: `Build succeeded.`

- [ ] **Step 4: Commit**

  ```bash
  git add src/Core/Intents/Intents.cs src/Core/Events/Events.cs
  git commit -m "feat: add EquipItemIntent and ItemUnequippedEvent

  Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>"
  ```

---

### Task 2: InventorySystem.ProcessEquipItem — tests first

**Files:**
- Test: `tests/MonoRogue.Tests/InventorySystemTests.cs`
- Modify: `src/Core/Systems/InventorySystem.cs`

- [ ] **Step 1: Write failing tests**

  Open `tests/MonoRogue.Tests/InventorySystemTests.cs`. After the existing `UseItem_*` tests, add a new region:

  ```csharp
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
  ```

- [ ] **Step 2: Run tests to verify they fail**

  ```bash
  cd /Users/robhawkes/mono-roguelike
  dotnet test tests/MonoRogue.Tests --no-build -v q 2>&1 | grep -E "FAIL|error|ProcessEquipItem"
  ```

  Expected: Build error — `ProcessEquipItem` does not exist yet.

- [ ] **Step 3: Implement ProcessEquipItem in InventorySystem**

  Open `src/Core/Systems/InventorySystem.cs`. After the `ProcessUseItem` method and before the `// ── Helpers` section, add:

  ```csharp
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
          if (itemType == ItemType.Consumable)
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
  ```

- [ ] **Step 4: Run tests to verify they pass**

  ```bash
  cd /Users/robhawkes/mono-roguelike
  dotnet test tests/MonoRogue.Tests -v q 2>&1 | tail -10
  ```

  Expected: All tests pass, no failures.

- [ ] **Step 5: Commit**

  ```bash
  git add src/Core/Systems/InventorySystem.cs tests/MonoRogue.Tests/InventorySystemTests.cs
  git commit -m "feat: add InventorySystem.ProcessEquipItem with tests

  Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>"
  ```

---

### Task 3: Reducer — handle ItemUnequippedEvent

**Files:**
- Modify: `src/Core/Reducer.cs`
- Test: `tests/MonoRogue.Tests/ReducerTests.cs`

- [ ] **Step 1: Write failing tests**

  Open `tests/MonoRogue.Tests/ReducerTests.cs`. Find the Inventory section (search for `ItemEquippedEvent`) and add after it:

  ```csharp
  [Fact]
  public void ItemUnequippedEvent_clears_weapon_slot()
  {
      var weapon = MakeWeapon(new Position(0, 0));
      var player = MakePlayer(new Position(3, 3)) with
      {
          Inventory = new InventoryComponent(
              ImmutableArray<Guid>.Empty.Add(weapon.Id), MaxSlots: 10),
          Equipment = new EquipmentComponent(WeaponId: weapon.Id)
      };
      var state = MakeState(20, 20, player, weapon);

      var next = Reduce(state, new ItemUnequippedEvent(player.Id, weapon.Id, EquipSlot.Weapon));

      Assert.Null(next.Entities[player.Id].Equipment!.WeaponId);
  }

  [Fact]
  public void ItemUnequippedEvent_clears_armor_slot()
  {
      var armor  = MakeArmor(new Position(0, 0));
      var player = MakePlayer(new Position(3, 3)) with
      {
          Inventory = new InventoryComponent(
              ImmutableArray<Guid>.Empty.Add(armor.Id), MaxSlots: 10),
          Equipment = new EquipmentComponent(ArmorId: armor.Id)
      };
      var state = MakeState(20, 20, player, armor);

      var next = Reduce(state, new ItemUnequippedEvent(player.Id, armor.Id, EquipSlot.Armor));

      Assert.Null(next.Entities[player.Id].Equipment!.ArmorId);
  }

  [Fact]
  public void ItemUnequippedEvent_item_stays_in_inventory()
  {
      var weapon = MakeWeapon(new Position(0, 0));
      var player = MakePlayer(new Position(3, 3)) with
      {
          Inventory = new InventoryComponent(
              ImmutableArray<Guid>.Empty.Add(weapon.Id), MaxSlots: 10),
          Equipment = new EquipmentComponent(WeaponId: weapon.Id)
      };
      var state = MakeState(20, 20, player, weapon);

      var next = Reduce(state, new ItemUnequippedEvent(player.Id, weapon.Id, EquipSlot.Weapon));

      Assert.Contains(weapon.Id, next.Entities[player.Id].Inventory!.Items);
  }
  ```

- [ ] **Step 2: Run tests to verify they fail**

  ```bash
  cd /Users/robhawkes/mono-roguelike
  dotnet test tests/MonoRogue.Tests -v q 2>&1 | grep -E "FAIL|ItemUnequipped"
  ```

  Expected: Build error — `ItemUnequippedEvent` not handled in Reducer switch.

- [ ] **Step 3: Add ApplyItemUnequipped to Reducer**

  Open `src/Core/Reducer.cs`. In the main `switch` expression (around line 37, where `ItemEquippedEvent` is handled), add a case alongside it:

  ```csharp
  ItemUnequippedEvent unequip => ApplyItemUnequipped(state, unequip),
  ```

  Then add the method near `ApplyItemEquipped` (around line 201):

  ```csharp
  private static GameState ApplyItemUnequipped(GameState state, ItemUnequippedEvent evt)
  {
      if (!state.Entities.TryGetValue(evt.EntityId, out var entity)) return state;
      if (entity.Equipment is null) return state;

      var newEquip = evt.Slot == EquipSlot.Weapon
          ? entity.Equipment with { WeaponId = null }
          : entity.Equipment with { ArmorId  = null };

      var updated = entity with { Equipment = newEquip };
      return state with { Entities = state.Entities.SetItem(evt.EntityId, updated) };
  }
  ```

- [ ] **Step 4: Run tests to verify they pass**

  ```bash
  cd /Users/robhawkes/mono-roguelike
  dotnet test tests/MonoRogue.Tests -v q 2>&1 | tail -10
  ```

  Expected: All tests pass.

- [ ] **Step 5: Commit**

  ```bash
  git add src/Core/Reducer.cs tests/MonoRogue.Tests/ReducerTests.cs
  git commit -m "feat: reducer handles ItemUnequippedEvent

  Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>"
  ```

---

### Task 4: Route EquipItemIntent through GameLoop

**Files:**
- Modify: `src/Core/GameLoop.cs`

- [ ] **Step 1: Add equips collection and routing**

  Open `src/Core/GameLoop.cs`. In the `Tick` method, find the block that collects intent types (around line 48). Add `equips` alongside the others:

  ```csharp
  var equips    = intents.OfType<EquipItemIntent>().ToImmutableArray();
  ```

  Then in Phase 2a, after the `InventorySystem.ProcessUseItem` call, add:

  ```csharp
  allEvents.AddRange(InventorySystem.ProcessEquipItem(state, equips));
  ```

  The full Phase 2a block should look like:

  ```csharp
  var moveEvents = MovementSystem.Process(state, moves);
  allEvents.AddRange(moveEvents);
  allEvents.AddRange(DialogueSystem.ProcessBumps(state, moveEvents));
  allEvents.AddRange(SpawnSystem.Process(state, spawns, registry));
  allEvents.AddRange(InventorySystem.ProcessPickups(state, pickups));
  allEvents.AddRange(InventorySystem.ProcessUseItem(state, useItems));
  allEvents.AddRange(InventorySystem.ProcessEquipItem(state, equips));
  allEvents.AddRange(InteractionSystem.Process(state, interacts));
  ```

- [ ] **Step 2: Build and run all tests**

  ```bash
  cd /Users/robhawkes/mono-roguelike
  dotnet test tests/MonoRogue.Tests -v q 2>&1 | tail -10
  ```

  Expected: All tests pass.

- [ ] **Step 3: Commit**

  ```bash
  git add src/Core/GameLoop.cs
  git commit -m "feat: route EquipItemIntent through GameLoop

  Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>"
  ```

---

### Task 5: Shell input — E key in inventory mode

**Files:**
- Modify: `src/Shell/MonoRogueGame.cs`

- [ ] **Step 1: Add E key handler in inventory navigation block**

  Open `src/Shell/MonoRogueGame.cs`. Find the `// ── Inventory navigation ──────` block (around line 194). After the existing `if (_input.WasPressed(Keys.U) || _input.WasPressed(Keys.Enter))` block (around line 226), insert before the final `// Inventory is open — swallow all other input` comment:

  ```csharp
  if (_input.WasPressed(Keys.E))
  {
      if (player?.Inventory != null && _state.InventorySelectedIndex < itemCount)
      {
          var selectedId = player.Inventory.Items[_state.InventorySelectedIndex];
          if (_state.Entities.TryGetValue(selectedId, out var selectedItem) &&
              selectedItem.Item?.Type != ItemType.Consumable)
          {
              // Keep inventory open so player can see the change
              ExecutePlayerIntent(new EquipItemIntent(_state.PlayerEntityId, selectedId));
          }
          else
          {
              _state = _state.AppendMessage("You can't equip that.");
          }
      }
      base.Update(gameTime); return;
  }
  ```

- [ ] **Step 2: Build to verify no compile errors**

  ```bash
  cd /Users/robhawkes/mono-roguelike
  dotnet build --no-restore -v q 2>&1 | tail -5
  ```

  Expected: `Build succeeded.`

- [ ] **Step 3: Commit**

  ```bash
  git add src/Shell/MonoRogueGame.cs
  git commit -m "feat: E key equips/unequips selected item in inventory

  Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>"
  ```

---

### Task 6: UI — Equipped section in inventory panel

**Files:**
- Modify: `src/Shell/AsciiRenderer.cs`

- [ ] **Step 1: Update DrawInventoryBox**

  Open `src/Shell/AsciiRenderer.cs`. Find `DrawInventoryBox` (around line 369).

  **1a. Increase box height** from 500 to 580 to accommodate the new section:

  ```csharp
  const int boxH   = 580;
  ```

  **1b. Replace the Gold + Items section** (from `int drawY = boxY + 60;` down to `Print("Items:", innerX, drawY, TextDim);`) with the following, which inserts the Equipped section between Gold and Items:

  ```csharp
  int drawY = boxY + 60;
  Print($"Gold: {player.Inventory.Gold}g", innerX, drawY, TextPrimary);
  drawY += 30;

  // ── Equipped section ──────────────────────────────────────────────────
  Print("Equipped:", innerX, drawY, TextDim);
  drawY += 24;

  var equip = player.Equipment;

  string weaponName = equip?.WeaponId is Guid wId &&
      state.Entities.TryGetValue(wId, out var wEnt)
      ? wEnt.Identity?.Name ?? "Unknown"
      : "—";

  string armorName = equip?.ArmorId is Guid aId &&
      state.Entities.TryGetValue(aId, out var aEnt)
      ? aEnt.Identity?.Name ?? "Unknown"
      : "—";

  Print($"  Weapon: {weaponName}", innerX, drawY, TextSecondary); drawY += 22;
  Print($"  Armor:  {armorName}", innerX, drawY, TextSecondary); drawY += 22;
  drawY += 8;
  DrawRect(innerX, drawY, boxW - 48, 1, Separator);
  drawY += 12;

  // ── Items list ────────────────────────────────────────────────────────
  Print("Items:", innerX, drawY, TextDim);
  drawY += 24;
  ```

  **1c. Update item row rendering** to show a `*` prefix for equipped items. Replace the inner `for` loop body:

  ```csharp
  for (int i = 0; i < player.Inventory.Items.Length; i++)
  {
      var itemId     = player.Inventory.Items[i];
      var itemEnt    = state.Entities.TryGetValue(itemId, out var ie) ? ie : null;
      var name       = itemEnt?.Identity?.Name ?? "Unknown Item";
      bool isEquipped = player.Equipment?.WeaponId == itemId ||
                        player.Equipment?.ArmorId  == itemId;
      bool sel        = i == selectedIndex;

      string prefix = isEquipped ? "*" : " ";
      if (sel)
      {
          DrawRect(boxX + 4, drawY - 2, boxW - 8, 22, new Color(80, 60, 20, 180));
          Print($">{prefix} {name}", innerX - 4, drawY, TextPrimary);
      }
      else
      {
          Print($" {prefix} {name}", innerX, drawY, TextSecondary);
      }
      drawY += 24;
  }
  ```

  **1d. Update the key hint** at the bottom of the method:

  ```csharp
  Print("[Up/Down] Select   [E] Equip/Unequip   [U/Enter] Use   [I/ESC] Close",
        boxX + 8, boxY + boxH - 24, TextDim);
  ```

- [ ] **Step 2: Build to verify no compile errors**

  ```bash
  cd /Users/robhawkes/mono-roguelike
  dotnet build --no-restore -v q 2>&1 | tail -5
  ```

  Expected: `Build succeeded.`

- [ ] **Step 3: Run full test suite**

  ```bash
  cd /Users/robhawkes/mono-roguelike
  dotnet test tests/MonoRogue.Tests -v q 2>&1 | tail -10
  ```

  Expected: All tests pass.

- [ ] **Step 4: Commit**

  ```bash
  git add src/Shell/AsciiRenderer.cs
  git commit -m "feat: inventory screen shows equipped slots and marks equipped items

  Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>"
  ```
