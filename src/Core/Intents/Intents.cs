namespace MonoRogue.Core.Intents;

using MonoRogue.Core.Model;

// ── Intent discriminated union ───────────────────────────────────────────────
// An Intent is a *request* — it has not been validated.
// Emit these from InputHandler (player) or AI systems (NPCs).
// Never mutate state from Intent-emitting code.
// ────────────────────────────────────────────────────────────────────────────

public abstract record Intent;

/// <summary>Entity wishes to move by a unit direction vector.</summary>
public sealed record MoveIntent(
    Guid     EntityId,
    Position Direction
) : Intent;

/// <summary>Entity takes no action this turn.</summary>
public sealed record WaitIntent(Guid EntityId) : Intent;

/// <summary>
/// Request to instantiate a blueprint from the registry.
/// Used by map gen, item drops, NPC spawners.
/// </summary>
public sealed record SpawnIntent(
    string   TemplateId,
    Position Position,
    bool     IsPlayer = false
) : Intent;

/// <summary>
/// Player wants to interact with adjacent tiles (gather, etc.).
/// When <see cref="TargetEntityId"/> is set, only that specific entity is processed.
/// When null, the system auto-selects the first interactable neighbour (legacy).
/// </summary>
public sealed record InteractIntent(Guid EntityId, Guid? TargetEntityId = null) : Intent;

/// <summary>
/// Entity tries to pick up an item at its current position.
/// </summary>
public sealed record PickupIntent(Guid EntityId) : Intent;

/// <summary>
/// Entity tries to use a specific item from its inventory.
/// </summary>
public sealed record UseItemIntent(Guid EntityId, Guid ItemId) : Intent;

/// <summary>
/// Player tries to descend to the next floor via stairs.
/// Validated and handled by the Shell (not by GameLoop) since it
/// requires full dungeon generation.
/// </summary>
public sealed record DescendIntent(Guid EntityId) : Intent;

/// <summary>
/// Player wants to talk to an adjacent NPC.
/// DialogueSystem scans all 8 neighbours for the nearest talker.
/// </summary>
public sealed record TalkIntent(Guid EntityId) : Intent;

/// <summary>Advance the active conversation to the next line.</summary>
public sealed record AdvanceDialogueIntent : Intent;

/// <summary>Close the active conversation immediately.</summary>
public sealed record CloseDialogueIntent : Intent;

/// <summary>Signals that the player has requested a world-save.</summary>
public sealed record SaveIntent : Intent;

/// <summary>Request to craft an item at a workstation.</summary>
public sealed record CraftIntent(Guid EntityId, string RecipeId) : Intent;

/// <summary>
/// Explicit melee attack request — used when the attacker knows the target
/// without having to move into them (e.g. AI adjacent to player).
/// Combat events are resolved by <see cref="CombatSystem"/>.
/// </summary>
public sealed record AttackIntent(Guid SourceId, Guid TargetId) : Intent;

/// <summary>
/// Request to buy one item from a shopkeeper's inventory.
/// The buyer pays <c>Item.Value * Quantity</c> gold from their Gold counter.
/// Validated by <see cref="EconomySystem"/>.
/// </summary>
public sealed record TradeIntent(
    Guid BuyerId,
    Guid SellerId,
    Guid ItemId,
    int  Quantity = 1
) : Intent;

/// <summary>
/// Player wants to equip or unequip an item from their inventory.
/// If the item is already in its slot, it is unequipped back to inventory.
/// If the slot is occupied by a different item, that item is swapped back
/// to inventory before the new one is equipped.
/// Consumables are rejected with a message.
/// </summary>
public sealed record EquipItemIntent(Guid EntityId, Guid ItemId) : Intent;
