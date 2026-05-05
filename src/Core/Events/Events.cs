namespace MonoRogue.Core.Events;

using System.Collections.Immutable;
using MonoRogue.Core.Model;

// ── Event discriminated union ────────────────────────────────────────────────
// Events are *validated facts* — they will happen exactly as described.
// The Reducer consumes GameEvents to build the new GameState.
// SideEffects are extracted by the Shell for rendering / audio / logging.
// ────────────────────────────────────────────────────────────────────────────

public abstract record GameEvent;

// ── Movement ─────────────────────────────────────────────────────────────────

public sealed record MovedEvent(Guid EntityId, Position NewPosition) : GameEvent;

/// <summary>Player opened a door tile — it becomes passable and its glyph changes.</summary>
public sealed record DoorOpenedEvent(Guid DoorEntityId) : GameEvent;

// ── Spawning ─────────────────────────────────────────────────────────────────

public sealed record SpawnedEvent(Entity NewEntity, bool IsPlayer = false) : GameEvent;

// ── Combat ────────────────────────────────────────────────────────────────────

public sealed record DamagedEvent(Guid EntityId, int Amount, Guid SourceId) : GameEvent;

public sealed record DiedEvent(Guid EntityId) : GameEvent;

// ── Inventory ─────────────────────────────────────────────────────────────────

/// <summary>
/// An entity picked up an item. The item's Spatial is nulled out and its ID
/// is appended to the picker's InventoryComponent.
/// </summary>
public sealed record ItemPickedUpEvent(Guid PickerId, Guid ItemId) : GameEvent;

/// <summary>
/// A consumable item was used. It is removed from inventory and Entities.
/// </summary>
public sealed record ItemUsedEvent(Guid UserId, Guid ItemId, int HealAmount) : GameEvent;

/// <summary>An item was equipped into a weapon or armour slot.</summary>
public sealed record ItemEquippedEvent(Guid EquipperId, Guid ItemId, EquipSlot Slot) : GameEvent;

/// <summary>An item was unequipped from a slot. It remains in InventoryComponent.Items.</summary>
public sealed record ItemUnequippedEvent(Guid EntityId, Guid ItemId, EquipSlot Slot) : GameEvent;

/// <summary>A stacked resource quantity changed (e.g. gathering, crafting).</summary>
public sealed record InventoryChangedEvent(Guid EntityId, string ItemTemplateId, int DeltaQuantity) : GameEvent;

// ── Quests ───────────────────────────────────────────────────────────────────

public sealed record QuestAdvancedEvent(Guid EntityId, Quest Quest) : GameEvent;
public sealed record QuestCompletedEvent(Guid EntityId, string QuestId) : GameEvent;

// ── Levelling ─────────────────────────────────────────────────────────────────

public sealed record XpGainedEvent(Guid EntityId, int Amount) : GameEvent;

// ── FOV ──────────────────────────────────────────────────────────────────────

public sealed record FovUpdatedEvent(
    ImmutableHashSet<Position> Visible,
    ImmutableHashSet<Position> AllExplored
) : GameEvent;

// ── Turn bookkeeping ──────────────────────────────────────────────────────────

public sealed record TurnAdvancedEvent : GameEvent;

// ── Messages ─────────────────────────────────────────────────────────────────

public sealed record MessageLoggedEvent(string Message) : GameEvent;

// ── Game phase ────────────────────────────────────────────────────────────────

public sealed record GameEndedEvent(bool Victory, string Message) : GameEvent;

// ── Dialogue ─────────────────────────────────────────────────────────────────

/// <summary>Player walked into an NPC — requests that DialogueSystem open a conversation.</summary>
public sealed record NpcBumpedEvent(Guid NpcId) : GameEvent;

/// <summary>A conversation with an NPC has started.</summary>
public sealed record DialogueOpenedEvent(
    Guid                           NpcId,
    string                         NpcName,
    ImmutableArray<string>         Lines,
    ImmutableArray<DialogueOption> Options = default
) : GameEvent;

/// <summary>Player pressed advance — current line index incremented (or dialogue closed).</summary>
public sealed record DialogueAdvancedEvent : GameEvent;

/// <summary>Dialogue was explicitly closed.</summary>
public sealed record DialogueClosedEvent : GameEvent;

// ── Economy ───────────────────────────────────────────────────────────────────

/// <summary>
/// A trade was validated and will execute: the item moves from seller to buyer
/// and <c>Cost</c> gold transfers from buyer to seller.
/// </summary>
public sealed record TradeCompletedEvent(
    Guid BuyerId,
    Guid SellerId,
    Guid ItemId,
    int  Cost
) : GameEvent;

/// <summary>
/// The entity's Gold counter changes by <c>Delta</c> (positive = gain, negative = spend).
/// Gold never goes below zero — the Reducer clamps to 0.
/// </summary>
public sealed record GoldChangedEvent(Guid EntityId, int Delta) : GameEvent;

// ── Quest progress ────────────────────────────────────────────────────────────

/// <summary>
/// A kill-target objective ticked up: <c>NewCount</c> is the updated kill total.
/// Emitted by <see cref="QuestSystem"/> when it detects a matching DiedEvent.
/// </summary>
public sealed record QuestProgressedEvent(
    Guid   PlayerId,
    string QuestId,
    string TargetId,
    int    NewCount
) : GameEvent;

// ── Side-effect events (consumed by Shell only, never by Reducer) ─────────────

public abstract record SideEffect : GameEvent;

public sealed record PlaySoundEffect(string SoundKey) : SideEffect;

public sealed record SpawnVisualEffect(string EffectKey, Position At) : SideEffect;

/// <summary>Show a narrative text box overlay over the game.</summary>
public sealed record DialogEvent(string Speaker, string Text) : SideEffect;
