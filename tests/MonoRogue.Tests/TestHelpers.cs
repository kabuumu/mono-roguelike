namespace MonoRogue.Tests;

using System.Collections.Immutable;
using MonoRogue.Core.Events;
using MonoRogue.Core.Model;
using MonoRogue.Data;

/// <summary>
/// Static factory methods shared across all test classes.
/// Every factory returns a minimal, valid object — add `with { }` to customise.
/// </summary>
internal static class TestHelpers
{
    // ── Entity factories ──────────────────────────────────────────────────────

    public static Entity MakePlayer(Position pos) => new(
        Id:       Guid.NewGuid(),
        Identity: new IdentityComponent("Player", "player"),
        Spatial:  new SpatialComponent(pos, BlocksMovement: true),
        Health:   new HealthComponent(Current: 20, Max: 20),
        CombatStats: new CombatStatsComponent(Attack: 5, Defense: 1),
        Level:    new LevelComponent(Level: 1, Xp: 0, XpToNextLevel: 100),
        Inventory: new InventoryComponent(ImmutableArray<Guid>.Empty, MaxSlots: 10),
        Equipment: new EquipmentComponent(),
        Player:   new PlayerTag()
    );

    public static Entity MakeEnemy(Position pos, int hp = 10, int attack = 3, int xpValue = 5) => new(
        Id:       Guid.NewGuid(),
        Identity: new IdentityComponent("Rat", "enemy_rat"),
        Spatial:  new SpatialComponent(pos, BlocksMovement: true),
        Health:   new HealthComponent(Current: hp, Max: hp),
        CombatStats: new CombatStatsComponent(Attack: attack, Defense: 0, XpValue: xpValue),
        Ai:       new AiComponent()
    );

    public static Entity MakeNpc(
        Position pos,
        Dictionary<string, string[]>? pool = null) => new(
        Id:       Guid.NewGuid(),
        Identity: new IdentityComponent("Villager", "npc_villager"),
        Spatial:  new SpatialComponent(pos, BlocksMovement: true),
        Dialogue: MakeDialogue(pool ?? new Dictionary<string, string[]> { ["default"] = ["Hello!"] }),
        Peaceful: new PeacefulTag()
    );

    public static DialogueComponent MakeDialogue(Dictionary<string, string[]> pool) =>
        new(pool.ToImmutableDictionary(kvp => kvp.Key, kvp => kvp.Value.ToImmutableArray()),
            pool.Keys.ToImmutableArray());

    public static Entity MakeItem(
        Position pos,
        ItemType type    = ItemType.Consumable,
        int healAmount   = 5,
        int attackBonus  = 0,
        int defenseBonus = 0,
        int value        = 0) => new(
        Id:       Guid.NewGuid(),
        Identity: new IdentityComponent("Potion", "item_potion"),
        Spatial:  new SpatialComponent(pos, BlocksMovement: false),
        Item:     new ItemComponent(type,
            AttackBonus:  attackBonus,
            DefenseBonus: defenseBonus,
            HealAmount:   healAmount,
            Value:        value)
    );

    public static Entity MakeWeapon(Position pos, int atkBonus = 3) =>
        MakeItem(pos, ItemType.Weapon, healAmount: 0, attackBonus: atkBonus);

    public static Entity MakeArmor(Position pos, int defBonus = 2) =>
        MakeItem(pos, ItemType.Armor, healAmount: 0, defenseBonus: defBonus);

    // ── State factory ─────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a minimal, fully walkable GameState containing the supplied
    /// entities. The first entity that IsPlayer() becomes the PlayerEntityId.
    /// </summary>
    public static GameState MakeState(int w, int h, params Entity[] entities)
    {
        var dict = entities
            .ToImmutableDictionary(e => e.Id, e => e);

        var playerId = entities.FirstOrDefault(e => e.IsPlayer())?.Id ?? Guid.Empty;

        return GameState.Empty(w, h) with
        {
            Entities       = dict,
            PlayerEntityId = playerId,
            TurnNumber     = 1,
        };
    }

    // ── Reducer shorthand ─────────────────────────────────────────────────────

    /// <summary>Applies a batch of events and returns only the new GameState.</summary>
    public static GameState Reduce(GameState state, params GameEvent[] events) =>
        MonoRogue.Core.Reducer.Reduce(state, events.ToImmutableArray()).NewState;

    // ── DataRegistry helpers ──────────────────────────────────────────────────

    public static DataRegistry EmptyRegistry() => DataRegistry.Empty();

    public static DataRegistry RegistryWithQuest(
        string questId,
        string targetId,
        int    required,
        int    rewardGold = 0)
    {
        var quest = new QuestTemplate(
            Id:            questId,
            Name:          "Test Quest",
            Description:   "Kill things.",
            RequiredItems: new Dictionary<string, int>(),
            CompletionText: "Done!",
            Objectives:    [new QuestObjectiveTemplate("kill_target", targetId, required)],
            RewardGold:    rewardGold);

        return new DataRegistry(
            ImmutableDictionary<string, EntityTemplate>.Empty,
            ImmutableDictionary<string, BackgroundTemplate>.Empty,
            ImmutableDictionary<string, FactionTemplate>.Empty,
            ImmutableDictionary<string, QuestTemplate>.Empty.Add(questId, quest),
            ImmutableDictionary<string, AreaTemplate>.Empty);
    }

    public static DataRegistry RegistryWithItemQuest(
        string questId,
        string turnInNpcId,
        Dictionary<string, int> requiredItems,
        int rewardGold = 0)
    {
        var quest = new QuestTemplate(
            Id:            questId,
            Name:          "Test Quest",
            Description:   "Gather things.",
            RequiredItems: requiredItems,
            CompletionText: "Thanks!",
            TurnInNpcId:   turnInNpcId,
            RewardGold:    rewardGold);

        return new DataRegistry(
            ImmutableDictionary<string, EntityTemplate>.Empty,
            ImmutableDictionary<string, BackgroundTemplate>.Empty,
            ImmutableDictionary<string, FactionTemplate>.Empty,
            ImmutableDictionary<string, QuestTemplate>.Empty.Add(questId, quest),
            ImmutableDictionary<string, AreaTemplate>.Empty);
    }

    /// <summary>Convenience: create an active Quest from a QuestTemplate.</summary>
    public static Quest MakeActiveQuest(
        string questId,
        string targetId,
        int    required) => new(
        Id:          questId,
        Name:        "Test Quest",
        Description: "Kill things.",
        RequiredItems: ImmutableDictionary<string, int>.Empty,
        CompletionText: "Done!",
        Objectives:  ImmutableArray.Create(
            new QuestObjective("kill_target", targetId, required)));

    public static Quest MakeItemDeliveryQuest(
        string questId,
        string turnInNpcId,
        Dictionary<string, int> requiredItems) => new(
        Id:          questId,
        Name:        "Test Quest",
        Description: "Gather things.",
        RequiredItems: requiredItems.ToImmutableDictionary(),
        CompletionText: "Thanks!",
        TurnInNpcId: turnInNpcId);

    /// <summary>Returns a player with the given resource stash already applied.</summary>
    public static Entity MakePlayerWithResources(Position pos, Dictionary<string, int> resources) =>
        MakePlayer(pos) with
        {
            Inventory = new InventoryComponent(
                ImmutableArray<Guid>.Empty,
                MaxSlots:  10,
                Resources: resources.ToImmutableDictionary())
        };

    /// <summary>Returns an NPC with a specific template ID (e.g. "npc_blacksmith").</summary>
    public static Entity MakeNpcWithTemplate(Position pos, string templateId) =>
        MakeNpc(pos) with
        {
            Identity = new IdentityComponent(templateId, templateId)
        };
}
