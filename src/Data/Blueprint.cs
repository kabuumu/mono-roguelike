namespace MonoRogue.Data;

using MonoRogue.Core.Model;

/// <summary>
/// Serialized, static definition of an entity archetype.
/// Deserialized from JSON — live entities store a TemplateId back-reference
/// and query the registry rather than duplicating static data.
/// </summary>
public sealed record EntityTemplate(
    string Id,
    string Name,
    string SpriteKey,
    string ColorTint      = "#FFFFFF",
    bool   BlocksMovement = false,
    int    MaxHealth      = 0,
    bool   IsTile         = false,
    bool   IsStairs       = false,
    // Combat
    int    Attack         = 0,
    int    Defense        = 0,
    int    XpValue        = 0,
    // AI
    bool   HasAi          = false,
    string AiBehavior     = "",
    string Faction        = "",
    // Loot drops  (null = no drops)
    DropEntry[]? Drops    = null,
    // Item data
    bool   IsItem         = false,
    string ItemType       = "",
    int    AttackBonus    = 0,
    int    DefenseBonus   = 0,
    int    HealAmount     = 0,
    int    Value          = 0,
    // Inventory (for player / container / shopkeeper entities)
    bool   HasInventory   = false,
    int    InventorySlots = 10,
    int    StartingGold   = 0,
    bool   HasEquipment   = false,
    // Shop (template IDs of items for sale)
    string[]? ShopInventory = null,
    // Dialogue
    bool     HasDialogue    = false,
    bool     IsPeaceful     = false,
    string[] DialogueLines  = null!,
    Dictionary<string, string[]> DialogPool = null!
);

/// <summary>One entry in a creature's loot table.</summary>
public sealed record DropEntry(
    string? ItemId     = null,
    int     GoldAmount = 0,
    float   Chance     = 1.0f
);

public sealed record BackgroundTemplate(
    string Id,
    string Name,
    string Description,
    Dictionary<string, int> StartingItems,
    Dictionary<string, int> FactionReputation,
    string[] StartingQuests = null!
);

public sealed record FactionTemplate(
    string Id,
    string Name,
    int BaselineDisposition
);

public sealed record QuestTemplate(
    string Id,
    string Name,
    string Description,
    Dictionary<string, int> RequiredItems,
    string CompletionText,
    // Kill-quest objectives (null for item-delivery quests)
    QuestObjectiveTemplate[]? Objectives = null,
    int      RewardGold    = 0,
    string[]? RewardItems  = null,
    // NPC template ID the player must talk to in order to turn in item-delivery quests
    string? TurnInNpcId   = null
);

/// <summary>A single objective in a quest template (e.g. kill N of X).</summary>
public sealed record QuestObjectiveTemplate(
    string Type,
    string TargetId,
    int    RequiredCount
);
