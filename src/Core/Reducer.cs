namespace MonoRogue.Core;

using System.Collections.Immutable;
using MonoRogue.Core.Events;
using MonoRogue.Core.Model;

/// <summary>
/// The single, authoritative state-transformation function.
///
/// Fold semantics: takes a GameState and a sequence of validated events,
/// returns a brand-new GameState plus any side-effects for the Shell.
///
/// Rules:
///  • Never throws — invalid events are skipped defensively.
///  • Never produces side-effects — SideEffects are extracted and returned.
///  • Order of events in the input array determines order of application.
/// </summary>
public static class Reducer
{
    public static (GameState NewState, ImmutableArray<SideEffect> SideEffects) Reduce(
        GameState                 state,
        ImmutableArray<GameEvent> events)
    {
        var sideEffects = ImmutableArray.CreateBuilder<SideEffect>();

        foreach (var evt in events)
        {
            state = evt switch
            {
                MovedEvent            moved   => ApplyMoved(state,   moved),
                SpawnedEvent          spawned => ApplySpawned(state, spawned),
                DamagedEvent          damaged => ApplyDamaged(state, damaged),
                DiedEvent             died    => ApplyDied(state,    died),
                ItemPickedUpEvent     pickup  => ApplyItemPickedUp(state,  pickup),
                ItemUsedEvent         used    => ApplyItemUsed(state,    used),
                ItemEquippedEvent     equip   => ApplyItemEquipped(state, equip),
                InventoryChangedEvent inv     => ApplyInventoryChanged(state, inv),
                QuestCompletedEvent   quest   => ApplyQuestCompleted(state, quest),
                QuestProgressedEvent  prog    => ApplyQuestProgressed(state, prog),
                TradeCompletedEvent   trade   => ApplyTradeCompleted(state, trade),
                GoldChangedEvent      gold    => ApplyGoldChanged(state, gold),
                XpGainedEvent         xp      => ApplyXpGained(state,    xp),
                FovUpdatedEvent  fov     => state with
                {
                    VisibleTiles  = fov.Visible,
                    ExploredTiles = fov.AllExplored,
                },
                DialogueOpenedEvent opened  => state with
                {
                    ActiveDialogue = new DialogueState(
                        opened.NpcId, opened.NpcName, opened.Lines)
                },
                DialogueAdvancedEvent => ApplyDialogueAdvanced(state),
                DialogueClosedEvent   => state with { ActiveDialogue = null },
                GameEndedEvent   end     => state with
                {
                    Phase = end.Victory
                        ? new VictoryPhase()
                        : new PlayerDeadPhase(end.Message),
                },
                TurnAdvancedEvent => state with { TurnNumber = state.TurnNumber + 1 },
                MessageLoggedEvent msg => state.AppendMessage(msg.Message),
                SideEffect fx  => Capture(state, fx, sideEffects),
                _              => state
            };
        }

        return (state, sideEffects.ToImmutable());
    }

    // ── Movement ──────────────────────────────────────────────────────────────

    private static GameState ApplyMoved(GameState state, MovedEvent evt)
    {
        if (!state.Entities.TryGetValue(evt.EntityId, out var entity)) return state;
        if (entity.Spatial is null) return state;

        return state with
        {
            Entities = state.Entities.SetItem(evt.EntityId, entity.MoveTo(evt.NewPosition))
        };
    }

    // ── Spawning ──────────────────────────────────────────────────────────────

    private static GameState ApplySpawned(GameState state, SpawnedEvent evt)
    {
        var entities = state.Entities.SetItem(evt.NewEntity.Id, evt.NewEntity);
        var playerId = evt.IsPlayer ? evt.NewEntity.Id : state.PlayerEntityId;

        var nextState = state with { Entities = entities, PlayerEntityId = playerId };

        if (evt.NewEntity.BlocksMovement() && evt.NewEntity.Spatial is { } spatial)
            nextState = nextState.WithTileBlocked(spatial.Position);

        return nextState;
    }

    // ── Damage ────────────────────────────────────────────────────────────────

    private static GameState ApplyDamaged(GameState state, DamagedEvent evt)
    {
        if (!state.Entities.TryGetValue(evt.EntityId, out var entity)) return state;
        if (entity.Health is null) return state;

        var newHp    = Math.Max(0, entity.Health.Current - evt.Amount);
        var updated  = entity with { Health = entity.Health with { Current = newHp } };
        return state with { Entities = state.Entities.SetItem(evt.EntityId, updated) };
    }

    // ── Death ─────────────────────────────────────────────────────────────────

    private static GameState ApplyDied(GameState state, DiedEvent evt)
    {
        if (!state.Entities.TryGetValue(evt.EntityId, out var entity)) return state;

        var nextState = state with { Entities = state.Entities.Remove(evt.EntityId) };
        if (entity.Spatial is { } spatial && entity.BlocksMovement())
            nextState = nextState.WithTileOpen(spatial.Position);

        // Player death → transition to dead phase
        if (evt.EntityId == state.PlayerEntityId)
            nextState = nextState with { Phase = new PlayerDeadPhase("You have been slain!") };

        return nextState;
    }

    // ── Item pickup ───────────────────────────────────────────────────────────

    private static GameState ApplyItemPickedUp(GameState state, ItemPickedUpEvent evt)
    {
        if (!state.Entities.TryGetValue(evt.PickerId, out var picker)) return state;
        if (!state.Entities.TryGetValue(evt.ItemId,   out var item))   return state;
        if (picker.Inventory is null) return state;

        // Remove item from the map (null out Spatial)
        var itemInInventory = item with { Spatial = null };

        // Add item ID to picker's inventory
        var newInventory = picker.Inventory with
        {
            Items = picker.Inventory.Items.Add(evt.ItemId)
        };
        var updatedPicker = picker with { Inventory = newInventory };

        return state with
        {
            Entities = state.Entities
                .SetItem(evt.ItemId,   itemInInventory)
                .SetItem(evt.PickerId, updatedPicker)
        };
    }

    // ── Item use ──────────────────────────────────────────────────────────────

    private static GameState ApplyItemUsed(GameState state, ItemUsedEvent evt)
    {
        if (!state.Entities.TryGetValue(evt.UserId, out var user)) return state;
        if (user.Inventory is null) return state;

        // Heal the user
        var healed = user;
        if (evt.HealAmount > 0 && user.Health is not null)
        {
            var newHp   = Math.Min(user.Health.Max, user.Health.Current + evt.HealAmount);
            healed      = user with { Health = user.Health with { Current = newHp } };
        }

        // Remove item from inventory and Entities
        var newItems    = healed.Inventory!.Items.Remove(evt.ItemId);
        var finalUser   = healed with { Inventory = healed.Inventory with { Items = newItems } };

        return state with
        {
            Entities = state.Entities
                .Remove(evt.ItemId)
                .SetItem(evt.UserId, finalUser)
        };
    }

    // ── Item equip ────────────────────────────────────────────────────────────

    private static GameState ApplyItemEquipped(GameState state, ItemEquippedEvent evt)
    {
        if (!state.Entities.TryGetValue(evt.EquipperId, out var equipper)) return state;
        if (equipper.Equipment is null) return state;

        var newEquip = evt.Slot == EquipSlot.Weapon
            ? equipper.Equipment with { WeaponId = evt.ItemId }
            : equipper.Equipment with { ArmorId  = evt.ItemId };

        var updated = equipper with { Equipment = newEquip };
        return state with { Entities = state.Entities.SetItem(evt.EquipperId, updated) };
    }

    // ── Inventory Changes ──────────────────────────────────────────────────────

    private static GameState ApplyInventoryChanged(GameState state, InventoryChangedEvent evt)
    {
        if (!state.Entities.TryGetValue(evt.EntityId, out var entity)) return state;
        if (entity.Inventory is null) return state;

        var resources = entity.Inventory.Resources ?? ImmutableDictionary<string, int>.Empty;
        var current = resources.GetValueOrDefault(evt.ItemTemplateId, 0);
        var next = current + evt.DeltaQuantity;

        if (next <= 0)
            resources = resources.Remove(evt.ItemTemplateId);
        else
            resources = resources.SetItem(evt.ItemTemplateId, next);

        return state with
        {
            Entities = state.Entities.SetItem(
                entity.Id,
                entity with { Inventory = entity.Inventory with { Resources = resources } })
        };
    }

    // ── Quests ────────────────────────────────────────────────────────────────

    private static GameState ApplyQuestCompleted(GameState state, QuestCompletedEvent evt)
    {
        var completed = state.CompletedQuestIds ?? ImmutableHashSet<string>.Empty;
        var active = state.ActiveQuests ?? ImmutableList<Quest>.Empty;

        return state with
        {
            CompletedQuestIds = completed.Add(evt.QuestId),
            // Optionally remove from ActiveQuests
            ActiveQuests = active.RemoveAll(q => q.Id == evt.QuestId)
        };
    }

    // ── XP + levelling ────────────────────────────────────────────────────────

    private static GameState ApplyXpGained(GameState state, XpGainedEvent evt)
    {
        if (!state.Entities.TryGetValue(evt.EntityId, out var entity)) return state;
        if (entity.Level is null) return state;

        var level  = entity.Level;
        var newXp  = level.Xp + evt.Amount;

        // Handle one or more level-ups
        while (newXp >= level.XpToNextLevel)
        {
            newXp -= level.XpToNextLevel;
            var newLevelNum    = level.Level + 1;
            var newXpThreshold = newLevelNum * 100;

            // Stat bonuses per level
            var newMaxHp  = (entity.Health?.Max     ?? 10) + 10;
            var newAttack = (entity.CombatStats?.Attack  ?? 3) + 2;
            var newDef    = (entity.CombatStats?.Defense ?? 0) + 1;

            entity = entity with
            {
                Health = entity.Health is not null
                    ? entity.Health with
                    {
                        Max     = newMaxHp,
                        Current = Math.Min(entity.Health.Current + 10, newMaxHp)
                    }
                    : entity.Health,
                CombatStats = entity.CombatStats is not null
                    ? entity.CombatStats with { Attack = newAttack, Defense = newDef }
                    : entity.CombatStats,
            };

            level = new LevelComponent(
                Level:         newLevelNum,
                Xp:            0,
                XpToNextLevel: newXpThreshold);

            state = state.AppendMessage(
                $"You reached level {newLevelNum}! HP +10, ATK +2, DEF +1.");
        }

        entity = entity with { Level = level with { Xp = newXp } };
        return state with { Entities = state.Entities.SetItem(evt.EntityId, entity) };
    }

    // ── Dialogue ──────────────────────────────────────────────────────────────

    private static GameState ApplyDialogueAdvanced(GameState state)
    {
        if (state.ActiveDialogue is null) return state;
        var next = state.ActiveDialogue.Advance();  // null = past last line → close
        return state with { ActiveDialogue = next };
    }

    // ── Quest progress ────────────────────────────────────────────────────────

    private static GameState ApplyQuestProgressed(GameState state, QuestProgressedEvent evt)
    {
        if (state.ActiveQuests is null) return state;

        // Find the quest by ID and update its Progress dict
        int idx = -1;
        for (int i = 0; i < state.ActiveQuests.Count; i++)
            if (state.ActiveQuests[i].Id == evt.QuestId) { idx = i; break; }

        if (idx < 0) return state;

        var quest    = state.ActiveQuests[idx];
        var progress = quest.Progress ?? ImmutableDictionary<string, int>.Empty;
        progress     = progress.SetItem(evt.TargetId, evt.NewCount);

        return state with
        {
            ActiveQuests = state.ActiveQuests.SetItem(idx, quest with { Progress = progress })
        };
    }

    // ── Economy ───────────────────────────────────────────────────────────────

    private static GameState ApplyTradeCompleted(GameState state, TradeCompletedEvent evt)
    {
        if (!state.Entities.TryGetValue(evt.BuyerId,  out var buyer))  return state;
        if (!state.Entities.TryGetValue(evt.SellerId, out var seller)) return state;
        if (buyer.Inventory is null || seller.Inventory is null)        return state;

        // Transfer gold
        var buyerGold  = Math.Max(0, buyer.Inventory.Gold - evt.Cost);
        var sellerGold = seller.Inventory.Gold + evt.Cost;

        // Move item from seller to buyer
        var sellerItems = seller.Inventory.Items.Remove(evt.ItemId);
        var buyerItems  = buyer.Inventory.Items.Add(evt.ItemId);

        var updatedBuyer  = buyer  with { Inventory = buyer.Inventory  with { Items = buyerItems,  Gold = buyerGold  } };
        var updatedSeller = seller with { Inventory = seller.Inventory with { Items = sellerItems, Gold = sellerGold } };

        var entities = state.Entities
            .SetItem(evt.BuyerId,  updatedBuyer)
            .SetItem(evt.SellerId, updatedSeller);

        // Remove item from the world map (null Spatial = in-inventory)
        if (entities.TryGetValue(evt.ItemId, out var item) && item.Spatial is not null)
            entities = entities.SetItem(evt.ItemId, item with { Spatial = null });

        return state with { Entities = entities };
    }

    private static GameState ApplyGoldChanged(GameState state, GoldChangedEvent evt)
    {
        if (!state.Entities.TryGetValue(evt.EntityId, out var entity)) return state;
        if (entity.Inventory is null) return state;

        var newGold = Math.Max(0, entity.Inventory.Gold + evt.Delta);
        return state with
        {
            Entities = state.Entities.SetItem(
                evt.EntityId,
                entity with { Inventory = entity.Inventory with { Gold = newGold } })
        };
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static GameState Capture(
        GameState                        state,
        SideEffect                       fx,
        ImmutableArray<SideEffect>.Builder builder)
    {
        builder.Add(fx);
        return state;
    }
}
