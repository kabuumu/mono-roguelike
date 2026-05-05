namespace MonoRogue.Tests;

using System.Collections.Immutable;
using MonoRogue.Core.Events;
using MonoRogue.Core.Model;
using Xunit;

using static TestHelpers;

/// <summary>
/// Tests every Apply* branch of Reducer.Reduce in isolation.
/// Each test constructs an event, feeds it through Reduce(), and
/// verifies the returned GameState — no GPU or file I/O required.
/// </summary>
public sealed class ReducerTests
{
    // ── Movement ──────────────────────────────────────────────────────────────

    [Fact]
    public void MovedEvent_updates_entity_position()
    {
        var player    = MakePlayer(new Position(5, 5));
        var state     = MakeState(20, 20, player);
        var newPos    = new Position(6, 5);

        var next = Reduce(state, new MovedEvent(player.Id, newPos));

        Assert.Equal(newPos, next.Entities[player.Id].Spatial!.Position);
    }

    [Fact]
    public void MovedEvent_for_unknown_entity_is_ignored()
    {
        var state = MakeState(20, 20, MakePlayer(new Position(5, 5)));
        var next  = Reduce(state, new MovedEvent(Guid.NewGuid(), new Position(1, 1)));

        // State unchanged: same entity count
        Assert.Equal(state.Entities.Count, next.Entities.Count);
    }

    // ── Spawning ──────────────────────────────────────────────────────────────

    [Fact]
    public void SpawnedEvent_adds_entity_to_state()
    {
        var state  = MakeState(20, 20);
        var enemy  = MakeEnemy(new Position(3, 3));

        var next = Reduce(state, new SpawnedEvent(enemy));

        Assert.True(next.Entities.ContainsKey(enemy.Id));
    }

    [Fact]
    public void SpawnedEvent_with_IsPlayer_sets_PlayerEntityId()
    {
        var state  = MakeState(20, 20);
        var player = MakePlayer(new Position(1, 1));

        var next = Reduce(state, new SpawnedEvent(player, IsPlayer: true));

        Assert.Equal(player.Id, next.PlayerEntityId);
    }

    [Fact]
    public void SpawnedEvent_blocking_entity_marks_tile_non_walkable()
    {
        var pos   = new Position(4, 4);
        var state = MakeState(20, 20);
        var wall  = MakeEnemy(pos); // enemies BlocksMovement = true

        var next = Reduce(state, new SpawnedEvent(wall));

        Assert.False(next.IsWalkable(pos));
    }

    // ── Damage ────────────────────────────────────────────────────────────────

    [Fact]
    public void DamagedEvent_reduces_hp_by_amount()
    {
        var enemy = MakeEnemy(new Position(1, 1), hp: 10);
        var state = MakeState(20, 20, enemy);

        var next = Reduce(state, new DamagedEvent(enemy.Id, 4, Guid.NewGuid()));

        Assert.Equal(6, next.Entities[enemy.Id].Health!.Current);
    }

    [Fact]
    public void DamagedEvent_clamps_hp_to_zero()
    {
        var enemy = MakeEnemy(new Position(1, 1), hp: 3);
        var state = MakeState(20, 20, enemy);

        var next = Reduce(state, new DamagedEvent(enemy.Id, 999, Guid.NewGuid()));

        Assert.Equal(0, next.Entities[enemy.Id].Health!.Current);
    }

    // ── Death ─────────────────────────────────────────────────────────────────

    [Fact]
    public void DiedEvent_removes_entity_from_state()
    {
        var enemy = MakeEnemy(new Position(2, 2));
        var state = MakeState(20, 20, enemy);

        var next = Reduce(state, new DiedEvent(enemy.Id));

        Assert.False(next.Entities.ContainsKey(enemy.Id));
    }

    [Fact]
    public void DiedEvent_blocking_entity_reopens_tile()
    {
        var pos   = new Position(5, 5);
        var enemy = MakeEnemy(pos);
        // Spawn first so tile is blocked, then kill
        var state = Reduce(MakeState(20, 20), new SpawnedEvent(enemy));
        Assert.False(state.IsWalkable(pos));

        var next = Reduce(state, new DiedEvent(enemy.Id));

        Assert.True(next.IsWalkable(pos));
    }

    [Fact]
    public void DiedEvent_for_player_transitions_to_dead_phase()
    {
        var player = MakePlayer(new Position(1, 1));
        var state  = MakeState(20, 20, player);

        var next = Reduce(state, new DiedEvent(player.Id));

        Assert.True(next.IsPlayerDead);
    }

    // ── Item pickup ───────────────────────────────────────────────────────────

    [Fact]
    public void ItemPickedUpEvent_adds_item_to_pickers_inventory()
    {
        var player = MakePlayer(new Position(1, 1));
        var item   = MakeItem(new Position(1, 1));
        var state  = MakeState(20, 20, player, item);

        var next = Reduce(state, new ItemPickedUpEvent(player.Id, item.Id));

        Assert.Contains(item.Id, next.Entities[player.Id].Inventory!.Items);
    }

    [Fact]
    public void ItemPickedUpEvent_nulls_item_spatial()
    {
        var player = MakePlayer(new Position(1, 1));
        var item   = MakeItem(new Position(1, 1));
        var state  = MakeState(20, 20, player, item);

        var next = Reduce(state, new ItemPickedUpEvent(player.Id, item.Id));

        // Item is still in Entities but has no world position
        Assert.Null(next.Entities[item.Id].Spatial);
    }

    // ── Item use ──────────────────────────────────────────────────────────────

    [Fact]
    public void ItemUsedEvent_heals_user()
    {
        var player = MakePlayer(new Position(1, 1)) with
        {
            Health    = new HealthComponent(Current: 10, Max: 20),
            Inventory = new InventoryComponent(ImmutableArray<Guid>.Empty, MaxSlots: 10),
        };
        var item  = MakeItem(new Position(0, 0), healAmount: 5);
        // Put item in inventory manually
        var playerWithItem = player with
        {
            Inventory = player.Inventory! with { Items = player.Inventory.Items.Add(item.Id) }
        };
        var state = MakeState(20, 20, playerWithItem, item);

        var next = Reduce(state, new ItemUsedEvent(playerWithItem.Id, item.Id, HealAmount: 5));

        Assert.Equal(15, next.Entities[playerWithItem.Id].Health!.Current);
    }

    [Fact]
    public void ItemUsedEvent_caps_hp_at_max()
    {
        var player = MakePlayer(new Position(1, 1)) with
        {
            Health = new HealthComponent(Current: 18, Max: 20)
        };
        var item  = MakeItem(new Position(0, 0), healAmount: 50);
        var playerWithItem = player with
        {
            Inventory = player.Inventory! with { Items = player.Inventory.Items.Add(item.Id) }
        };
        var state = MakeState(20, 20, playerWithItem, item);

        var next = Reduce(state, new ItemUsedEvent(playerWithItem.Id, item.Id, HealAmount: 50));

        Assert.Equal(20, next.Entities[playerWithItem.Id].Health!.Current);
    }

    [Fact]
    public void ItemUsedEvent_removes_item_from_inventory_and_entities()
    {
        var player = MakePlayer(new Position(1, 1));
        var item   = MakeItem(new Position(0, 0), healAmount: 5);
        var playerWithItem = player with
        {
            Inventory = player.Inventory! with { Items = player.Inventory.Items.Add(item.Id) }
        };
        var state = MakeState(20, 20, playerWithItem, item);

        var next = Reduce(state, new ItemUsedEvent(playerWithItem.Id, item.Id, HealAmount: 5));

        Assert.DoesNotContain(item.Id, next.Entities[playerWithItem.Id].Inventory!.Items);
        Assert.False(next.Entities.ContainsKey(item.Id));
    }

    // ── Item equip ────────────────────────────────────────────────────────────

    [Fact]
    public void ItemEquippedEvent_sets_weapon_slot()
    {
        var player = MakePlayer(new Position(1, 1));
        var weapon = MakeWeapon(new Position(0, 0));
        var state  = MakeState(20, 20, player, weapon);

        var next = Reduce(state, new ItemEquippedEvent(player.Id, weapon.Id, EquipSlot.Weapon));

        Assert.Equal(weapon.Id, next.Entities[player.Id].Equipment!.WeaponId);
    }

    [Fact]
    public void ItemEquippedEvent_sets_armor_slot()
    {
        var player = MakePlayer(new Position(1, 1));
        var armor  = MakeArmor(new Position(0, 0));
        var state  = MakeState(20, 20, player, armor);

        var next = Reduce(state, new ItemEquippedEvent(player.Id, armor.Id, EquipSlot.Armor));

        Assert.Equal(armor.Id, next.Entities[player.Id].Equipment!.ArmorId);
    }

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

    // ── Inventory resources ────────────────────────────────────────────────────

    [Fact]
    public void InventoryChangedEvent_adds_new_resource_key()
    {
        var player = MakePlayer(new Position(1, 1));
        var state  = MakeState(20, 20, player);

        var next = Reduce(state, new InventoryChangedEvent(player.Id, "wood", 3));

        Assert.Equal(3, next.Entities[player.Id].Inventory!.Resources!["wood"]);
    }

    [Fact]
    public void InventoryChangedEvent_increments_existing_resource()
    {
        var player = MakePlayer(new Position(1, 1)) with
        {
            Inventory = new InventoryComponent(
                ImmutableArray<Guid>.Empty,
                Resources: ImmutableDictionary<string, int>.Empty.Add("wood", 2))
        };
        var state = MakeState(20, 20, player);

        var next = Reduce(state, new InventoryChangedEvent(player.Id, "wood", 3));

        Assert.Equal(5, next.Entities[player.Id].Inventory!.Resources!["wood"]);
    }

    [Fact]
    public void InventoryChangedEvent_removes_key_when_quantity_reaches_zero()
    {
        var player = MakePlayer(new Position(1, 1)) with
        {
            Inventory = new InventoryComponent(
                ImmutableArray<Guid>.Empty,
                Resources: ImmutableDictionary<string, int>.Empty.Add("wood", 2))
        };
        var state = MakeState(20, 20, player);

        var next = Reduce(state, new InventoryChangedEvent(player.Id, "wood", -2));

        Assert.False(next.Entities[player.Id].Inventory!.Resources!.ContainsKey("wood"));
    }

    // ── XP & levelling ────────────────────────────────────────────────────────

    [Fact]
    public void XpGainedEvent_below_threshold_accumulates()
    {
        var player = MakePlayer(new Position(1, 1));
        var state  = MakeState(20, 20, player);

        var next = Reduce(state, new XpGainedEvent(player.Id, 30));

        var level = next.Entities[player.Id].Level!;
        Assert.Equal(1, level.Level);
        Assert.Equal(30, level.Xp);
    }

    [Fact]
    public void XpGainedEvent_crossing_threshold_levels_up()
    {
        var player = MakePlayer(new Position(1, 1));
        var state  = MakeState(20, 20, player);

        var next = Reduce(state, new XpGainedEvent(player.Id, 100));

        var level = next.Entities[player.Id].Level!;
        Assert.Equal(2, level.Level);
    }

    [Fact]
    public void XpGainedEvent_level_up_increases_max_hp_and_attack()
    {
        var player      = MakePlayer(new Position(1, 1));
        var baseHp      = player.Health!.Max;
        var baseAttack  = player.CombatStats!.Attack;
        var state       = MakeState(20, 20, player);

        var next = Reduce(state, new XpGainedEvent(player.Id, 100));

        var e = next.Entities[player.Id];
        Assert.Equal(baseHp + 10,     e.Health!.Max);
        Assert.Equal(baseAttack + 2,  e.CombatStats!.Attack);
    }

    [Fact]
    public void XpGainedEvent_multiple_level_ups_chain_correctly()
    {
        var player = MakePlayer(new Position(1, 1));
        var state  = MakeState(20, 20, player);

        // 250 XP: level 1→2 at 100xp, level 2→3 needs 200xp (150 left → not enough)
        var next = Reduce(state, new XpGainedEvent(player.Id, 250));

        var level = next.Entities[player.Id].Level!;
        Assert.Equal(2, level.Level); // 250 > 100 → level 2; 150 < 200 → stays at 2
        Assert.Equal(150, level.Xp);
    }

    // ── Quest tracking ────────────────────────────────────────────────────────

    [Fact]
    public void QuestCompletedEvent_moves_quest_to_completed_set()
    {
        var player = MakePlayer(new Position(1, 1));
        var state  = MakeState(20, 20, player) with
        {
            ActiveQuests = ImmutableList.Create(
                TestHelpers.MakeActiveQuest("q1", "enemy_rat", 1))
        };

        var next = Reduce(state, new QuestCompletedEvent(player.Id, "q1"));

        Assert.Contains("q1", next.CompletedQuestIds!);
        Assert.DoesNotContain(next.ActiveQuests!, q => q.Id == "q1");
    }

    [Fact]
    public void QuestProgressedEvent_updates_kill_count()
    {
        var player = MakePlayer(new Position(1, 1));
        var quest  = TestHelpers.MakeActiveQuest("q1", "enemy_rat", 3);
        var state  = MakeState(20, 20, player) with
        {
            ActiveQuests = ImmutableList.Create(quest)
        };

        var next = Reduce(state, new QuestProgressedEvent(player.Id, "q1", "enemy_rat", 1));

        var q = next.ActiveQuests!.First(q => q.Id == "q1");
        Assert.Equal(1, q.Progress!["enemy_rat"]);
    }

    // ── Economy ───────────────────────────────────────────────────────────────

    [Fact]
    public void GoldChangedEvent_adds_gold()
    {
        var player = MakePlayer(new Position(1, 1));
        var state  = MakeState(20, 20, player);

        var next = Reduce(state, new GoldChangedEvent(player.Id, 50));

        Assert.Equal(50, next.Entities[player.Id].Inventory!.Gold);
    }

    [Fact]
    public void GoldChangedEvent_never_goes_below_zero()
    {
        var player = MakePlayer(new Position(1, 1));
        var state  = MakeState(20, 20, player);

        var next = Reduce(state, new GoldChangedEvent(player.Id, -999));

        Assert.Equal(0, next.Entities[player.Id].Inventory!.Gold);
    }

    [Fact]
    public void TradeCompletedEvent_transfers_gold_and_item()
    {
        var buyer  = MakePlayer(new Position(1, 1)) with
        {
            Inventory = new InventoryComponent(ImmutableArray<Guid>.Empty, MaxSlots: 10, Gold: 100)
        };
        var item   = MakeItem(new Position(2, 2), value: 30);
        var seller = new Entity(
            Id:       Guid.NewGuid(),
            Identity: new IdentityComponent("Merchant", "npc_merchant"),
            Spatial:  new SpatialComponent(new Position(2, 2), false),
            Inventory: new InventoryComponent(
                ImmutableArray<Guid>.Empty.Add(item.Id), MaxSlots: 20, Gold: 0));

        var state = MakeState(20, 20, buyer, seller, item);

        var next = Reduce(state,
            new TradeCompletedEvent(buyer.Id, seller.Id, item.Id, Cost: 30));

        Assert.Equal(70, next.Entities[buyer.Id].Inventory!.Gold);
        Assert.Equal(30, next.Entities[seller.Id].Inventory!.Gold);
        Assert.Contains(item.Id, next.Entities[buyer.Id].Inventory.Items);
        Assert.DoesNotContain(item.Id, next.Entities[seller.Id].Inventory!.Items);
    }

    // ── Dialogue ──────────────────────────────────────────────────────────────

    [Fact]
    public void DialogueOpenedEvent_sets_active_dialogue()
    {
        var state = MakeState(20, 20, MakePlayer(new Position(1, 1)));
        var npcId = Guid.NewGuid();

        var next = Reduce(state,
            new DialogueOpenedEvent(npcId, "Bob", ImmutableArray.Create("Hi!")));

        Assert.True(next.InDialogue);
        Assert.Equal("Bob", next.ActiveDialogue!.NpcName);
    }

    [Fact]
    public void DialogueAdvancedEvent_increments_current_line()
    {
        var state = MakeState(20, 20, MakePlayer(new Position(1, 1))) with
        {
            ActiveDialogue = new DialogueState(Guid.NewGuid(), "Bob",
                ImmutableArray.Create("Line 1", "Line 2"), CurrentLine: 0)
        };

        var next = Reduce(state, new DialogueAdvancedEvent());

        Assert.Equal(1, next.ActiveDialogue!.CurrentLine);
    }

    [Fact]
    public void DialogueAdvancedEvent_past_last_line_closes_dialogue()
    {
        var state = MakeState(20, 20, MakePlayer(new Position(1, 1))) with
        {
            ActiveDialogue = new DialogueState(Guid.NewGuid(), "Bob",
                ImmutableArray.Create("Only line"), CurrentLine: 0)
        };

        var next = Reduce(state, new DialogueAdvancedEvent());

        Assert.False(next.InDialogue);
    }

    [Fact]
    public void DialogueClosedEvent_clears_active_dialogue()
    {
        var state = MakeState(20, 20, MakePlayer(new Position(1, 1))) with
        {
            ActiveDialogue = new DialogueState(Guid.NewGuid(), "Bob",
                ImmutableArray.Create("Hi!"), CurrentLine: 0)
        };
        Assert.True(state.InDialogue);

        var next = Reduce(state, new DialogueClosedEvent());

        Assert.False(next.InDialogue);
    }

    // ── Turn / Message ────────────────────────────────────────────────────────

    [Fact]
    public void TurnAdvancedEvent_increments_turn_number()
    {
        var state = MakeState(20, 20) with { TurnNumber = 5 };

        var next = Reduce(state, new TurnAdvancedEvent());

        Assert.Equal(6, next.TurnNumber);
    }

    [Fact]
    public void MessageLoggedEvent_appends_to_message_log()
    {
        var state = MakeState(20, 20);

        var next = Reduce(state, new MessageLoggedEvent("Hello world"));

        Assert.Contains("Hello world", next.MessageLog);
    }

    [Fact]
    public void FovUpdatedEvent_sets_visible_and_explored_tiles()
    {
        var state   = MakeState(20, 20);
        var visible = ImmutableHashSet.Create(new Position(1, 1), new Position(2, 2));
        var all     = visible.Add(new Position(0, 0));

        var next = Reduce(state, new FovUpdatedEvent(visible, all));

        Assert.Equal(visible, next.VisibleTiles);
        Assert.Equal(all,     next.ExploredTiles);
    }

    // ── Game phase ────────────────────────────────────────────────────────────

    [Fact]
    public void GameEndedEvent_victory_sets_victory_phase()
    {
        var state = MakeState(20, 20);

        var next = Reduce(state, new GameEndedEvent(Victory: true, "You win!"));

        Assert.True(next.IsVictory);
    }

    [Fact]
    public void GameEndedEvent_defeat_sets_dead_phase()
    {
        var state = MakeState(20, 20);

        var next = Reduce(state, new GameEndedEvent(Victory: false, "You died."));

        Assert.True(next.IsPlayerDead);
    }

    // ── Side-effects & unknown events ─────────────────────────────────────────

    [Fact]
    public void SideEffect_is_captured_not_applied_to_state()
    {
        var state = MakeState(20, 20) with { TurnNumber = 3 };

        var (next, sideEffects) = MonoRogue.Core.Reducer.Reduce(
            state,
            ImmutableArray.Create<GameEvent>(
                new PlaySoundEffect("beep"),
                new TurnAdvancedEvent()));

        // TurnNumber still incremented (non-side-effect event was applied)
        Assert.Equal(4, next.TurnNumber);
        // Side effect was captured, not applied
        Assert.Single(sideEffects);
        Assert.IsType<PlaySoundEffect>(sideEffects[0]);
    }

    [Fact]
    public void Multiple_events_applied_in_order()
    {
        var state = MakeState(20, 20) with { TurnNumber = 1 };

        var next = Reduce(state,
            new TurnAdvancedEvent(),
            new TurnAdvancedEvent(),
            new TurnAdvancedEvent());

        Assert.Equal(4, next.TurnNumber);
    }
}
