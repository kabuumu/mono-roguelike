namespace MonoRogue.Tests;

using System.Collections.Immutable;
using MonoRogue.Core;
using MonoRogue.Core.Events;
using MonoRogue.Core.Intents;
using MonoRogue.Core.Model;
using MonoRogue.Core.Systems;
using MonoRogue.Data;
using Xunit;

using static TestHelpers;

/// <summary>
/// Tests for QuestSystem — kill-tracking, completion, and reward emission.
/// Uses an inline DataRegistry; no JSON files required.
/// </summary>
public sealed class QuestSystemTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static Entity MakeTargetEnemy(string templateId) => new(
        Id:       Guid.NewGuid(),
        Identity: new IdentityComponent("Rat", templateId),
        Spatial:  new SpatialComponent(new Position(2, 2), BlocksMovement: true),
        Health:   new HealthComponent(Current: 0, Max: 5), // already dead for DiedEvent
        CombatStats: new MonoRogue.Core.Model.CombatStatsComponent(Attack: 1, Defense: 0, XpValue: 5),
        Ai:       new AiComponent());

    private static ImmutableArray<GameEvent> Process(
        GameState                 state,
        DataRegistry              registry,
        params GameEvent[]        frameEvents) =>
        QuestSystem.ProcessEvents(
            state,
            frameEvents.ToImmutableArray(),
            registry);

    // ── Kill tracking ─────────────────────────────────────────────────────────

    [Fact]
    public void Kill_matching_target_emits_progressed_event()
    {
        var player  = MakePlayer(new Position(1, 1));
        var enemy   = MakeTargetEnemy("enemy_rat");
        var quest   = MakeActiveQuest("q1", "enemy_rat", required: 3);
        var state   = MakeState(20, 20, player, enemy) with
        {
            ActiveQuests = ImmutableList.Create(quest)
        };

        var events = Process(state, EmptyRegistry(), new DiedEvent(enemy.Id));

        Assert.Contains(events, e => e is QuestProgressedEvent p && p.QuestId == "q1");
    }

    [Fact]
    public void Kill_non_matching_target_is_ignored()
    {
        var player = MakePlayer(new Position(1, 1));
        var enemy  = MakeTargetEnemy("enemy_wolf"); // quest tracks "enemy_rat"
        var quest  = MakeActiveQuest("q1", "enemy_rat", required: 3);
        var state  = MakeState(20, 20, player, enemy) with
        {
            ActiveQuests = ImmutableList.Create(quest)
        };

        var events = Process(state, EmptyRegistry(), new DiedEvent(enemy.Id));

        Assert.DoesNotContain(events, e => e is QuestProgressedEvent);
    }

    [Fact]
    public void Kill_completing_objective_emits_quest_completed_event()
    {
        var player = MakePlayer(new Position(1, 1));
        var enemy  = MakeTargetEnemy("enemy_rat");
        // Quest requires only 1 kill; first kill completes it
        var quest = MakeActiveQuest("q1", "enemy_rat", required: 1);
        var state = MakeState(20, 20, player, enemy) with
        {
            ActiveQuests = ImmutableList.Create(quest)
        };

        var events = Process(state, EmptyRegistry(), new DiedEvent(enemy.Id));

        Assert.Contains(events, e => e is QuestCompletedEvent c && c.QuestId == "q1");
        Assert.Contains(events, e => e is MessageLoggedEvent m && m.Message.Contains("Quest complete"));
    }

    // ── Rewards ───────────────────────────────────────────────────────────────

    [Fact]
    public void Completing_quest_with_gold_reward_emits_gold_changed()
    {
        var player  = MakePlayer(new Position(1, 1));
        var enemy   = MakeTargetEnemy("enemy_rat");
        var quest   = MakeActiveQuest("q1", "enemy_rat", required: 1);
        var state   = MakeState(20, 20, player, enemy) with
        {
            ActiveQuests = ImmutableList.Create(quest)
        };
        // Registry has the quest template with a gold reward
        var registry = RegistryWithQuest("q1", "enemy_rat", required: 1, rewardGold: 50);

        var events = Process(state, registry, new DiedEvent(enemy.Id));

        Assert.Contains(events, e => e is GoldChangedEvent g && g.Delta == 50);
    }

    // ── Guards ────────────────────────────────────────────────────────────────

    [Fact]
    public void No_active_quests_returns_empty()
    {
        var player  = MakePlayer(new Position(1, 1));
        var enemy   = MakeTargetEnemy("enemy_rat");
        var state   = MakeState(20, 20, player, enemy) with
        {
            ActiveQuests = ImmutableList<Quest>.Empty
        };

        var events = Process(state, EmptyRegistry(), new DiedEvent(enemy.Id));

        Assert.Empty(events);
    }

    [Fact]
    public void Non_died_events_are_ignored()
    {
        var player = MakePlayer(new Position(1, 1));
        var quest  = MakeActiveQuest("q1", "enemy_rat", required: 3);
        var state  = MakeState(20, 20, player) with
        {
            ActiveQuests = ImmutableList.Create(quest)
        };

        // Pass a non-DiedEvent — should produce nothing
        var events = Process(state, EmptyRegistry(),
            new MessageLoggedEvent("nothing to see here"),
            new TurnAdvancedEvent());

        Assert.Empty(events);
    }

    [Fact]
    public void No_player_in_state_returns_empty()
    {
        // State deliberately has no player entity
        var enemy  = MakeTargetEnemy("enemy_rat");
        var quest  = MakeActiveQuest("q1", "enemy_rat", required: 1);
        var state  = GameState.Empty(20, 20) with
        {
            Entities     = ImmutableDictionary<Guid, Entity>.Empty.Add(enemy.Id, enemy),
            ActiveQuests = ImmutableList.Create(quest),
            // PlayerEntityId = Guid.Empty (the default in Empty())
        };

        var events = Process(state, EmptyRegistry(), new DiedEvent(enemy.Id));

        Assert.Empty(events);
    }
}

// ── Item-delivery turn-in (CheckItemTurnIn) ───────────────────────────────────

/// <summary>
/// Tests for QuestSystem.CheckItemTurnIn — item-delivery quest completion
/// when the player talks to the designated NPC while holding the required resources.
/// </summary>
public sealed class QuestItemTurnInTests
{
    private const string SmithId = "npc_blacksmith";
    private const string QuestId = "quest_fetch_water";
    private const string ItemId  = "item_water_bucket";

    private static ImmutableArray<GameEvent> TurnIn(
        GameState    state,
        Entity       actor,
        string       npcTemplateId,
        DataRegistry registry) =>
        QuestSystem.CheckItemTurnIn(state, actor, npcTemplateId, registry);

    // ── Happy path ────────────────────────────────────────────────────────────

    [Fact]
    public void Player_with_required_resource_talking_to_correct_npc_completes_quest()
    {
        var player = MakePlayerWithResources(new Position(1, 1),
            new Dictionary<string, int> { [ItemId] = 1 });
        var quest  = MakeItemDeliveryQuest(QuestId, SmithId,
            new Dictionary<string, int> { [ItemId] = 1 });
        var state  = MakeState(20, 20, player) with { ActiveQuests = ImmutableList.Create(quest) };

        var events = TurnIn(state, player, SmithId, EmptyRegistry());

        Assert.Contains(events, e => e is QuestCompletedEvent c && c.QuestId == QuestId);
    }

    [Fact]
    public void Turn_in_consumes_required_resources()
    {
        var player = MakePlayerWithResources(new Position(1, 1),
            new Dictionary<string, int> { [ItemId] = 1 });
        var quest  = MakeItemDeliveryQuest(QuestId, SmithId,
            new Dictionary<string, int> { [ItemId] = 1 });
        var state  = MakeState(20, 20, player) with { ActiveQuests = ImmutableList.Create(quest) };

        var events = TurnIn(state, player, SmithId, EmptyRegistry());

        Assert.Contains(events, e =>
            e is InventoryChangedEvent inv &&
            inv.ItemTemplateId == ItemId &&
            inv.DeltaQuantity == -1);
    }

    [Fact]
    public void Turn_in_emits_completion_message()
    {
        var player = MakePlayerWithResources(new Position(1, 1),
            new Dictionary<string, int> { [ItemId] = 1 });
        var quest  = MakeItemDeliveryQuest(QuestId, SmithId,
            new Dictionary<string, int> { [ItemId] = 1 });
        var state  = MakeState(20, 20, player) with { ActiveQuests = ImmutableList.Create(quest) };

        var events = TurnIn(state, player, SmithId, EmptyRegistry());

        Assert.Contains(events, e => e is MessageLoggedEvent m && m.Message.Contains("Quest complete"));
    }

    [Fact]
    public void Turn_in_emits_gold_reward_from_registry()
    {
        var player   = MakePlayerWithResources(new Position(1, 1),
            new Dictionary<string, int> { [ItemId] = 1 });
        var quest    = MakeItemDeliveryQuest(QuestId, SmithId,
            new Dictionary<string, int> { [ItemId] = 1 });
        var state    = MakeState(20, 20, player) with { ActiveQuests = ImmutableList.Create(quest) };
        var registry = RegistryWithItemQuest(QuestId, SmithId,
            new Dictionary<string, int> { [ItemId] = 1 }, rewardGold: 10);

        var events = TurnIn(state, player, SmithId, registry);

        Assert.Contains(events, e => e is GoldChangedEvent g && g.Delta == 10);
    }

    // ── Negative cases ────────────────────────────────────────────────────────

    [Fact]
    public void Wrong_npc_template_does_not_complete_quest()
    {
        var player = MakePlayerWithResources(new Position(1, 1),
            new Dictionary<string, int> { [ItemId] = 1 });
        var quest  = MakeItemDeliveryQuest(QuestId, SmithId,
            new Dictionary<string, int> { [ItemId] = 1 });
        var state  = MakeState(20, 20, player) with { ActiveQuests = ImmutableList.Create(quest) };

        var events = TurnIn(state, player, "npc_villager", EmptyRegistry());

        Assert.DoesNotContain(events, e => e is QuestCompletedEvent);
    }

    [Fact]
    public void Missing_required_resource_does_not_complete_quest()
    {
        // Player has 0 water buckets
        var player = MakePlayer(new Position(1, 1));
        var quest  = MakeItemDeliveryQuest(QuestId, SmithId,
            new Dictionary<string, int> { [ItemId] = 1 });
        var state  = MakeState(20, 20, player) with { ActiveQuests = ImmutableList.Create(quest) };

        var events = TurnIn(state, player, SmithId, EmptyRegistry());

        Assert.DoesNotContain(events, e => e is QuestCompletedEvent);
    }

    [Fact]
    public void Partial_resource_count_does_not_complete_quest()
    {
        // Quest needs 4 planks; player only has 2
        var player = MakePlayerWithResources(new Position(1, 1),
            new Dictionary<string, int> { ["item_plank"] = 2 });
        var quest  = MakeItemDeliveryQuest("quest_chop_wood", SmithId,
            new Dictionary<string, int> { ["item_plank"] = 4 });
        var state  = MakeState(20, 20, player) with { ActiveQuests = ImmutableList.Create(quest) };

        var events = TurnIn(state, player, SmithId, EmptyRegistry());

        Assert.DoesNotContain(events, e => e is QuestCompletedEvent);
    }

    [Fact]
    public void No_active_quests_returns_empty()
    {
        var player = MakePlayerWithResources(new Position(1, 1),
            new Dictionary<string, int> { [ItemId] = 1 });
        var state  = MakeState(20, 20, player) with { ActiveQuests = ImmutableList<Quest>.Empty };

        var events = TurnIn(state, player, SmithId, EmptyRegistry());

        Assert.Empty(events);
    }
}

// ── JSON data integrity ────────────────────────────────────────────────────────

/// <summary>
/// Loads the real Content/Data JSON files and asserts that quest templates carry
/// the TurnInNpcId that drives the dynamic turn-in system.
/// </summary>
public sealed class QuestDataIntegrityTests
{
    private static DataRegistry LoadRegistry()
    {
        var contentDir = Path.Combine(AppContext.BaseDirectory, "Content", "Data");
        return DataRegistry.LoadFrom(contentDir);
    }

    [Fact]
    public void quest_fetch_water_has_turn_in_npc_set_to_blacksmith()
    {
        var registry = LoadRegistry();
        Assert.True(registry.Quests.TryGetValue("quest_fetch_water", out var q),
            "quest_fetch_water not found in registry");
        Assert.Equal("npc_blacksmith", q.TurnInNpcId);
    }

    [Fact]
    public void quest_chop_wood_has_turn_in_npc_set_to_blacksmith()
    {
        var registry = LoadRegistry();
        Assert.True(registry.Quests.TryGetValue("quest_chop_wood", out var q),
            "quest_chop_wood not found in registry");
        Assert.Equal("npc_blacksmith", q.TurnInNpcId);
    }

    [Fact]
    public void quest_fetch_water_requires_item_water_bucket()
    {
        var registry = LoadRegistry();
        Assert.True(registry.Quests.TryGetValue("quest_fetch_water", out var q));
        Assert.True(q.RequiredItems.ContainsKey("item_water_bucket"),
            "quest_fetch_water should require item_water_bucket");
    }

    [Fact]
    public void Village_generator_propagates_turn_in_npc_to_active_quest()
    {
        var registry = LoadRegistry();
        var player   = new Entity(
            Guid.NewGuid(),
            Spatial:   new SpatialComponent(Position.Zero, BlocksMovement: true),
            Player:    new PlayerTag(),
            Inventory: new InventoryComponent([], 10));

        var state = MonoRogue.Core.Generation.VillageGenerator.Generate(
            new MonoRogue.Core.Generation.VillageGenParams(Seed: 1, MapWidth: 80, MapHeight: 40),
            registry,
            "background_blacksmith_child",
            player);

        var fetchWater = state.ActiveQuests?.FirstOrDefault(q => q.Id == "quest_fetch_water");
        Assert.NotNull(fetchWater);
        Assert.Equal("npc_blacksmith", fetchWater.TurnInNpcId);
    }

    [Fact]
    public void All_six_new_kill_quests_load_from_registry()
    {
        var registry = LoadRegistry();
        string[] expectedIds =
        [
            "quest_cull_boars",
            "quest_clear_caverns",
            "quest_orc_cleansing",
            "quest_swamp_trolls",
            "quest_cave_scout",
            "quest_boar_cull",
        ];
        foreach (var id in expectedIds)
            Assert.True(registry.Quests.ContainsKey(id), $"{id} not found in registry");
    }

    [Fact]
    public void Biome_quests_have_kill_objectives_with_correct_targets()
    {
        var registry = LoadRegistry();

        Assert.True(registry.Quests.TryGetValue("quest_cull_boars",    out var q1));
        Assert.True(registry.Quests.TryGetValue("quest_clear_caverns", out var q2));
        Assert.True(registry.Quests.TryGetValue("quest_orc_cleansing", out var q3));
        Assert.True(registry.Quests.TryGetValue("quest_swamp_trolls",  out var q4));
        Assert.True(registry.Quests.TryGetValue("quest_cave_scout",    out var q5));
        Assert.True(registry.Quests.TryGetValue("quest_boar_cull",     out var q6));

        Assert.Equal("creature_boar",   q1!.Objectives?[0].TargetId);
        Assert.Equal("creature_goblin", q2!.Objectives?[0].TargetId);
        Assert.Equal("creature_orc",    q3!.Objectives?[0].TargetId);
        Assert.Equal("creature_troll",  q4!.Objectives?[0].TargetId);
        Assert.Equal("creature_rat",    q5!.Objectives?[0].TargetId);
        Assert.Equal("creature_boar",   q6!.Objectives?[0].TargetId);
    }

    [Fact]
    public void All_four_biome_npc_templates_load_from_registry()
    {
        var registry = LoadRegistry();
        string[] expectedIds = ["npc_hermit", "npc_prospector", "npc_scholar", "npc_exile"];
        foreach (var id in expectedIds)
            Assert.True(registry.Templates.ContainsKey(id), $"{id} not found in registry");
    }

    [Fact]
    public void New_consumable_items_load_from_registry()
    {
        var registry = LoadRegistry();
        Assert.True(registry.Templates.ContainsKey("item_ancient_scroll"),
            "item_ancient_scroll not found");
        Assert.True(registry.Templates.ContainsKey("item_swamp_herb"),
            "item_swamp_herb not found");
    }
}

// ── Full pipeline: bump NPC → dialogue → turn-in ─────────────────────────────

/// <summary>
/// Integration tests through GameLoop.Tick verifying that bumping a quest-giver
/// NPC while holding the required resources triggers quest completion in the
/// same tick — covering MovementSystem → NpcBumpedEvent → DialogueSystem.ProcessBumps
/// → DialogueOpenedEvent → QuestSystem.CheckItemTurnIn → QuestCompletedEvent.
/// Also covers the TalkIntent path (player presses T near NPC).
/// </summary>
public sealed class QuestTurnInPipelineTests
{
    private const string SmithTemplateId = "npc_blacksmith";
    private const string QuestId         = "quest_fetch_water";
    private const string ItemId          = "item_water_bucket";

    [Fact]
    public void Bumping_quest_giver_npc_with_required_items_completes_quest()
    {
        // Player at (4,5), blacksmith directly East at (5,5)
        var player = MakePlayerWithResources(new Position(4, 5),
            new Dictionary<string, int> { [ItemId] = 1 });
        var smith  = MakeNpcWithTemplate(new Position(5, 5), SmithTemplateId);
        var quest  = MakeItemDeliveryQuest(QuestId, SmithTemplateId,
            new Dictionary<string, int> { [ItemId] = 1 });
        var registry = RegistryWithItemQuest(QuestId, SmithTemplateId,
            new Dictionary<string, int> { [ItemId] = 1 }, rewardGold: 10);

        var state = MakeState(20, 20, player, smith) with
        {
            ActiveQuests = ImmutableList.Create(quest)
        };

        var intents = ImmutableArray.Create<Intent>(new MoveIntent(player.Id, Position.East));
        var (newState, _) = GameLoop.Tick(state, intents, registry);

        Assert.Contains(QuestId, newState.CompletedQuestIds ?? ImmutableHashSet<string>.Empty);
    }

    [Fact]
    public void Bumping_quest_giver_without_items_does_not_complete_quest()
    {
        var player = MakePlayer(new Position(4, 5)); // no resources
        var smith  = MakeNpcWithTemplate(new Position(5, 5), SmithTemplateId);
        var quest  = MakeItemDeliveryQuest(QuestId, SmithTemplateId,
            new Dictionary<string, int> { [ItemId] = 1 });
        var registry = RegistryWithItemQuest(QuestId, SmithTemplateId,
            new Dictionary<string, int> { [ItemId] = 1 });

        var state = MakeState(20, 20, player, smith) with
        {
            ActiveQuests = ImmutableList.Create(quest)
        };

        var intents = ImmutableArray.Create<Intent>(new MoveIntent(player.Id, Position.East));
        var (newState, _) = GameLoop.Tick(state, intents, registry);

        Assert.DoesNotContain(QuestId,
            newState.CompletedQuestIds ?? ImmutableHashSet<string>.Empty);
    }

    [Fact]
    public void Completing_quest_consumes_resources_from_player_inventory()
    {
        var player = MakePlayerWithResources(new Position(4, 5),
            new Dictionary<string, int> { [ItemId] = 1 });
        var smith  = MakeNpcWithTemplate(new Position(5, 5), SmithTemplateId);
        var quest  = MakeItemDeliveryQuest(QuestId, SmithTemplateId,
            new Dictionary<string, int> { [ItemId] = 1 });
        var registry = RegistryWithItemQuest(QuestId, SmithTemplateId,
            new Dictionary<string, int> { [ItemId] = 1 });

        var state = MakeState(20, 20, player, smith) with
        {
            ActiveQuests = ImmutableList.Create(quest)
        };

        var intents = ImmutableArray.Create<Intent>(new MoveIntent(player.Id, Position.East));
        var (newState, _) = GameLoop.Tick(state, intents, registry);

        var updatedPlayer = newState.TryGetPlayer();
        var waterCount = updatedPlayer?.Inventory?.Resources?.GetValueOrDefault(ItemId, 0) ?? 0;
        Assert.Equal(0, waterCount);
    }

    [Fact]
    public void Completing_quest_awards_gold_reward()
    {
        var player = MakePlayerWithResources(new Position(4, 5),
            new Dictionary<string, int> { [ItemId] = 1 });
        var smith  = MakeNpcWithTemplate(new Position(5, 5), SmithTemplateId);
        var quest  = MakeItemDeliveryQuest(QuestId, SmithTemplateId,
            new Dictionary<string, int> { [ItemId] = 1 });
        var registry = RegistryWithItemQuest(QuestId, SmithTemplateId,
            new Dictionary<string, int> { [ItemId] = 1 }, rewardGold: 10);

        var state = MakeState(20, 20, player, smith) with
        {
            ActiveQuests = ImmutableList.Create(quest)
        };

        var intents = ImmutableArray.Create<Intent>(new MoveIntent(player.Id, Position.East));
        var (newState, _) = GameLoop.Tick(state, intents, registry);

        var updatedPlayer = newState.TryGetPlayer();
        Assert.Equal(10, updatedPlayer?.Inventory?.Gold ?? 0);
    }

    [Fact]
    public void TalkIntent_adjacent_to_quest_giver_with_items_completes_quest()
    {
        // Same scenario but triggered by TalkIntent (T key) instead of bump
        var player = MakePlayerWithResources(new Position(4, 5),
            new Dictionary<string, int> { [ItemId] = 1 });
        var smith  = MakeNpcWithTemplate(new Position(5, 5), SmithTemplateId);
        var quest  = MakeItemDeliveryQuest(QuestId, SmithTemplateId,
            new Dictionary<string, int> { [ItemId] = 1 });
        var registry = RegistryWithItemQuest(QuestId, SmithTemplateId,
            new Dictionary<string, int> { [ItemId] = 1 }, rewardGold: 10);

        var state = MakeState(20, 20, player, smith) with
        {
            ActiveQuests = ImmutableList.Create(quest)
        };

        var intents = ImmutableArray.Create<Intent>(new TalkIntent(player.Id));
        var (newState, _) = GameLoop.Tick(state, intents, registry);

        Assert.Contains(QuestId, newState.CompletedQuestIds ?? ImmutableHashSet<string>.Empty);
    }
}

// ── DataRegistry.BuildQuest helper ────────────────────────────────────────────

public sealed class QuestBuildTests
{
    [Fact]
    public void BuildQuest_converts_kill_quest_template_correctly()
    {
        var tpl = new QuestTemplate(
            Id:            "quest_cull_boars",
            Name:          "Boar Problem",
            Description:   "Kill 4 boars.",
            RequiredItems: new Dictionary<string, int>(),
            CompletionText: "Thank you!",
            Objectives:    [new QuestObjectiveTemplate("kill_target", "creature_boar", 4)],
            RewardGold:    35);

        var quest = DataRegistry.BuildQuest(tpl);

        Assert.Equal("quest_cull_boars", quest.Id);
        Assert.Equal("Boar Problem", quest.Name);
        Assert.NotNull(quest.Objectives);
        Assert.Single(quest.Objectives.Value);
        Assert.Equal("creature_boar", quest.Objectives.Value[0].TargetId);
        Assert.Equal(4, quest.Objectives.Value[0].RequiredCount);
        Assert.Equal("kill_target", quest.Objectives.Value[0].Type);
    }

    [Fact]
    public void BuildQuest_handles_null_objectives_for_item_delivery_quest()
    {
        var tpl = new QuestTemplate(
            Id:            "quest_fetch_water",
            Name:          "Fetch Water",
            Description:   "Get water.",
            RequiredItems: new Dictionary<string, int> { ["item_water_bucket"] = 1 },
            CompletionText: "Thanks!",
            Objectives:    null,
            TurnInNpcId:   "npc_blacksmith");

        var quest = DataRegistry.BuildQuest(tpl);

        Assert.Equal("quest_fetch_water", quest.Id);
        Assert.Null(quest.Objectives);
        Assert.Equal("npc_blacksmith", quest.TurnInNpcId);
        Assert.True(quest.RequiredItems.ContainsKey("item_water_bucket"));
    }
}
