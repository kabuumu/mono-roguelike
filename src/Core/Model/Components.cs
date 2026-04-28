namespace MonoRogue.Core.Model;

using System.Collections.Immutable;

// ─────────────────────────────────────────────────
//  ENUMS
// ─────────────────────────────────────────────────

public enum ItemType  { Consumable, Weapon, Armor }
public enum EquipSlot { Weapon, Armor }
public enum AiState   { Wander, Chase }

// ─────────────────────────────────────────────────
//  RENDERING
// ─────────────────────────────────────────────────

/// <summary>
/// Rendering hint — SpriteKey is a semantic string used by the shell
/// to look up either a glyph (ASCII mode) or a texture region (sprite mode).
/// </summary>
public sealed record RenderComponent(
    string SpriteKey,
    string ColorTint = "#FFFFFF"
);

// ─────────────────────────────────────────────────
//  SPATIAL
// ─────────────────────────────────────────────────

public sealed record SpatialComponent(
    Position Position,
    bool BlocksMovement = false
);

// ─────────────────────────────────────────────────
//  IDENTITY
// ─────────────────────────────────────────────────

public sealed record IdentityComponent(
    string Name,
    string TemplateId
);

// ─────────────────────────────────────────────────
//  VITALS
// ─────────────────────────────────────────────────

public sealed record HealthComponent(
    int Current,
    int Max
);

// ─────────────────────────────────────────────────
//  COMBAT STATS
// ─────────────────────────────────────────────────

/// <summary>
/// Base combat statistics for any entity that can fight.
/// XpValue is how much XP the player gains when this entity dies.
/// </summary>
public sealed record CombatStatsComponent(
    int Attack,
    int Defense,
    int XpValue = 0
);

// ─────────────────────────────────────────────────
//  LEVELING  (player only)
// ─────────────────────────────────────────────────

public sealed record LevelComponent(
    int Level          = 1,
    int Xp             = 0,
    int XpToNextLevel  = 100
);

// ─────────────────────────────────────────────────
//  ITEMS
// ─────────────────────────────────────────────────

/// <summary>Marks an entity as a collectible item with associated stats.</summary>
public sealed record ItemComponent(
    ItemType Type,
    int AttackBonus  = 0,
    int DefenseBonus = 0,
    int HealAmount   = 0,
    int Value        = 0
);

/// <summary>
/// Stores IDs of items currently held by this entity.
/// Items with null Spatial are in-inventory (not rendered on map).
/// Also tracks abstract resources by template ID -> quantity.
/// </summary>
public sealed record InventoryComponent(
    ImmutableArray<Guid> Items,
    int MaxSlots = 10,
    ImmutableDictionary<string, int>? Resources = null,
    int Gold = 0
);

/// <summary>Currently equipped weapon and armour (by entity ID).</summary>
public sealed record EquipmentComponent(
    Guid? WeaponId = null,
    Guid? ArmorId  = null
);

// ─────────────────────────────────────────────────
//  AI
// ─────────────────────────────────────────────────

public sealed record AiComponent(AiState State = AiState.Wander, string Faction = "");

// ─────────────────────────────────────────────────
//  DIALOGUE
// ─────────────────────────────────────────────────

/// <summary>
/// Marks an entity as an NPC that can be spoken to.
/// Lines cycle when the player advances; conversation ends after the last line.
///
/// <c>KeyOrder</c> preserves the original JSON insertion order of pool keys so that
/// <c>DialogueSystem</c> can resolve the first matching rule top-to-bottom.
/// Supported key prefixes (in order of typical specificity):
///   after_quest_completed:{questId}   — matches when that quest is in CompletedQuestIds
///   requires_background:{backgroundId} — matches when player has that background
///   default                           — always matches
/// </summary>
public sealed record DialogueComponent(
    ImmutableDictionary<string, ImmutableArray<string>> DialogPool,
    ImmutableArray<string> KeyOrder
);

// ─────────────────────────────────────────────────
//  BACKGROUND & QUESTS
// ─────────────────────────────────────────────────

public sealed record BackgroundComponent(string BackgroundId);

// ─────────────────────────────────────────────────
//  MARKER TAGS  (zero-size, presence implies meaning)
// ─────────────────────────────────────────────────

public sealed record PlayerTag;
public sealed record TileTag;
/// <summary>Marks the tile as downward stairs to the next floor.</summary>
public sealed record StairsDownTag;
/// <summary>Marks an NPC that should not engage in combat.</summary>
public sealed record PeacefulTag;
