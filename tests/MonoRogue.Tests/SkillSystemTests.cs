namespace MonoRogue.Tests;

using System.Linq;
using MonoRogue.Core.Events;
using MonoRogue.Core.Model;
using Xunit;

using static TestHelpers;

public sealed class SkillDataTests
{
    [Fact]
    public void SkillData_defaults_to_level_zero_xp_zero_threshold_twenty()
    {
        var skill = new SkillData();
        Assert.Equal(0, skill.Level);
        Assert.Equal(0, skill.Xp);
        Assert.Equal(20, skill.XpToNextLevel);
    }

    [Fact]
    public void SkillsComponent_exposes_all_three_named_skills()
    {
        var comp = new SkillsComponent(new SkillData(), new SkillData(), new SkillData());
        Assert.Equal(0, comp.Melee.Level);
        Assert.Equal(0, comp.Block.Level);
        Assert.Equal(0, comp.Barter.Level);
    }

    [Fact]
    public void Entity_Skills_is_null_by_default()
    {
        var entity = new Entity(Guid.NewGuid());
        Assert.Null(entity.Skills);
    }

    [Fact]
    public void Entity_with_expression_sets_Skills()
    {
        var entity = new Entity(Guid.NewGuid());
        var skills = new SkillsComponent(new SkillData(), new SkillData(), new SkillData());
        var updated = entity with { Skills = skills };
        Assert.NotNull(updated.Skills);
        Assert.Equal(0, updated.Skills.Melee.Level);
    }
}

public sealed class SkillXpReducerTests
{
    [Fact]
    public void SkillXpGained_accumulates_xp_below_threshold()
    {
        var player = MakePlayer(Position.Zero);
        var state  = MakeState(10, 10, player);

        var result = Reduce(state, new SkillXpGainedEvent(player.Id, SkillType.Melee, 10));

        Assert.Equal(10, result.Entities[player.Id].Skills!.Melee.Xp);
        Assert.Equal(0,  result.Entities[player.Id].Skills!.Melee.Level);
    }

    [Fact]
    public void SkillXpGained_levels_up_when_xp_reaches_threshold()
    {
        var player = MakePlayer(Position.Zero);
        var state  = MakeState(10, 10, player);

        var result = Reduce(state, new SkillXpGainedEvent(player.Id, SkillType.Melee, 20));

        Assert.Equal(1,  result.Entities[player.Id].Skills!.Melee.Level);
        Assert.Equal(0,  result.Entities[player.Id].Skills!.Melee.Xp);
        Assert.Equal(40, result.Entities[player.Id].Skills!.Melee.XpToNextLevel); // (1+1)*20
    }

    [Fact]
    public void SkillXpGained_xp_carrying_over_after_levelup()
    {
        var player = MakePlayer(Position.Zero);
        var state  = MakeState(10, 10, player);

        // Give 25 XP: 20 triggers level-up, 5 carries to next level
        var result = Reduce(state, new SkillXpGainedEvent(player.Id, SkillType.Melee, 25));

        Assert.Equal(1, result.Entities[player.Id].Skills!.Melee.Level);
        Assert.Equal(5, result.Entities[player.Id].Skills!.Melee.Xp);
    }

    [Fact]
    public void SkillXpGained_logs_level_up_message()
    {
        var player = MakePlayer(Position.Zero);
        var state  = MakeState(10, 10, player);

        var result = Reduce(state, new SkillXpGainedEvent(player.Id, SkillType.Melee, 20));

        Assert.Contains(result.MessageLog, m => m.Contains("Melee") && m.Contains("level 1"));
    }

    [Fact]
    public void SkillXpGained_block_and_barter_update_correct_field()
    {
        var player = MakePlayer(Position.Zero);
        var state  = MakeState(10, 10, player);

        var s1 = Reduce(state, new SkillXpGainedEvent(player.Id, SkillType.Block,  7));
        var s2 = Reduce(s1,    new SkillXpGainedEvent(player.Id, SkillType.Barter, 3));

        Assert.Equal(7, s2.Entities[player.Id].Skills!.Block.Xp);
        Assert.Equal(3, s2.Entities[player.Id].Skills!.Barter.Xp);
        Assert.Equal(0, s2.Entities[player.Id].Skills!.Melee.Xp);
    }

    [Fact]
    public void SkillXpGained_silently_ignores_entity_without_skills_component()
    {
        // Enemy has no SkillsComponent — should not throw
        var enemy = MakeEnemy(Position.Zero);
        var state = MakeState(10, 10, enemy);

        var result = Reduce(state, new SkillXpGainedEvent(enemy.Id, SkillType.Melee, 20));

        Assert.Null(result.Entities[enemy.Id].Skills);
    }
}

public sealed class CombatSkillTests
{
    [Fact]
    public void Resolve_emits_melee_xp_event_when_player_attacks_enemy()
    {
        var player = MakePlayer(new Position(1, 1));
        var enemy  = MakeEnemy(new Position(2, 2), hp: 20);
        var state  = MakeState(10, 10, player, enemy);

        var events = MonoRogue.Core.Systems.CombatSystem.Resolve(state, player.Id, enemy.Id);

        Assert.Contains(events, e =>
            e is MonoRogue.Core.Events.SkillXpGainedEvent xp &&
            xp.Skill    == SkillType.Melee &&
            xp.EntityId == player.Id &&
            xp.Amount   == 1);
    }

    [Fact]
    public void Resolve_does_not_emit_melee_xp_when_enemy_attacks()
    {
        var player = MakePlayer(new Position(1, 1));
        var enemy  = MakeEnemy(new Position(2, 2), hp: 20);
        var state  = MakeState(10, 10, player, enemy);

        var events = MonoRogue.Core.Systems.CombatSystem.Resolve(state, enemy.Id, player.Id);

        Assert.DoesNotContain(events, e =>
            e is MonoRogue.Core.Events.SkillXpGainedEvent xp && xp.Skill == SkillType.Melee);
    }

    [Fact]
    public void Resolve_emits_block_xp_event_when_player_takes_damage()
    {
        var player = MakePlayer(new Position(1, 1));
        var enemy  = MakeEnemy(new Position(2, 2), hp: 20);
        var state  = MakeState(10, 10, player, enemy);

        var events = MonoRogue.Core.Systems.CombatSystem.Resolve(state, enemy.Id, player.Id);

        Assert.Contains(events, e =>
            e is MonoRogue.Core.Events.SkillXpGainedEvent xp &&
            xp.Skill    == SkillType.Block &&
            xp.EntityId == player.Id &&
            xp.Amount   == 1);
    }

    [Fact]
    public void Resolve_does_not_emit_block_xp_when_enemy_takes_damage()
    {
        var player = MakePlayer(new Position(1, 1));
        var enemy  = MakeEnemy(new Position(2, 2), hp: 20);
        var state  = MakeState(10, 10, player, enemy);

        var events = MonoRogue.Core.Systems.CombatSystem.Resolve(state, player.Id, enemy.Id);

        Assert.DoesNotContain(events, e =>
            e is MonoRogue.Core.Events.SkillXpGainedEvent xp && xp.Skill == SkillType.Block);
    }

    [Fact]
    public void GetEffectiveAttack_adds_melee_skill_level_as_bonus()
    {
        var player = MakePlayer(Position.Zero) with
        {
            Skills = new SkillsComponent(
                Melee:  new SkillData(Level: 3, Xp: 0, XpToNextLevel: 80),
                Block:  new SkillData(),
                Barter: new SkillData())
        };
        var state = MakeState(10, 10, player);

        var atk = MonoRogue.Core.Systems.CombatSystem.GetEffectiveAttack(state, player);

        // Base attack is 5 (from MakePlayer), melee level 3 adds +3
        Assert.Equal(8, atk);
    }

    [Fact]
    public void GetEffectiveDefense_adds_block_skill_level_as_bonus()
    {
        var player = MakePlayer(Position.Zero) with
        {
            Skills = new SkillsComponent(
                Melee:  new SkillData(),
                Block:  new SkillData(Level: 2, Xp: 0, XpToNextLevel: 60),
                Barter: new SkillData())
        };
        var state = MakeState(10, 10, player);

        var def = MonoRogue.Core.Systems.CombatSystem.GetEffectiveDefense(state, player);

        // Base defense is 1 (from MakePlayer), block level 2 adds +2
        Assert.Equal(3, def);
    }

    [Fact]
    public void GetEffectiveAttack_returns_base_attack_when_no_skills_component()
    {
        var enemy = MakeEnemy(Position.Zero, attack: 4); // no Skills component
        var state = MakeState(10, 10, enemy);

        var atk = MonoRogue.Core.Systems.CombatSystem.GetEffectiveAttack(state, enemy);

        Assert.Equal(4, atk);
    }
}

public sealed class BarterSkillTests
{
    private static (GameState State, MonoRogue.Core.Intents.TradeIntent Intent) MakeTradeSetup(
        int barterLevel,
        int itemValue)
    {
        var item = MakeItem(new Position(5, 5), ItemType.Consumable, value: itemValue);

        var seller = new Entity(
            Id:       Guid.NewGuid(),
            Identity: new IdentityComponent("Merchant", "npc_merchant"),
            Inventory: new InventoryComponent(
                Items: System.Collections.Immutable.ImmutableArray.Create(item.Id),
                MaxSlots: 10,
                Gold: 0));

        var barterSkill = new SkillData(
            Level:         barterLevel,
            Xp:            0,
            XpToNextLevel: (barterLevel + 1) * 20);

        var buyer = MakePlayer(Position.Zero) with
        {
            Skills    = new SkillsComponent(new SkillData(), new SkillData(), barterSkill),
            Inventory = new MonoRogue.Core.Model.InventoryComponent(
                System.Collections.Immutable.ImmutableArray<Guid>.Empty, MaxSlots: 10, Gold: 1000)
        };

        var state  = MakeState(10, 10, buyer, seller, item);
        var intent = new MonoRogue.Core.Intents.TradeIntent(buyer.Id, seller.Id, item.Id);
        return (state, intent);
    }

    [Fact]
    public void ProcessTrades_emits_barter_xp_on_successful_trade()
    {
        var (state, intent) = MakeTradeSetup(barterLevel: 0, itemValue: 10);

        var events = MonoRogue.Core.Systems.EconomySystem.ProcessTrades(
            state,
            System.Collections.Immutable.ImmutableArray.Create(intent));

        Assert.Contains(events, e =>
            e is MonoRogue.Core.Events.SkillXpGainedEvent xp &&
            xp.Skill  == SkillType.Barter &&
            xp.Amount == 5);
    }

    [Fact]
    public void ProcessTrades_no_discount_at_barter_level_zero()
    {
        var (state, intent) = MakeTradeSetup(barterLevel: 0, itemValue: 100);

        var events = MonoRogue.Core.Systems.EconomySystem.ProcessTrades(
            state,
            System.Collections.Immutable.ImmutableArray.Create(intent));

        var trade = events.OfType<MonoRogue.Core.Events.TradeCompletedEvent>().Single();
        Assert.Equal(100, trade.Cost);
    }

    [Fact]
    public void ProcessTrades_applies_5_percent_discount_at_barter_level_1()
    {
        var (state, intent) = MakeTradeSetup(barterLevel: 1, itemValue: 100);

        var events = MonoRogue.Core.Systems.EconomySystem.ProcessTrades(
            state,
            System.Collections.Immutable.ImmutableArray.Create(intent));

        var trade = events.OfType<MonoRogue.Core.Events.TradeCompletedEvent>().Single();
        Assert.Equal(95, trade.Cost);
    }

    [Fact]
    public void ProcessTrades_applies_20_percent_discount_at_barter_level_4()
    {
        var (state, intent) = MakeTradeSetup(barterLevel: 4, itemValue: 100);

        var events = MonoRogue.Core.Systems.EconomySystem.ProcessTrades(
            state,
            System.Collections.Immutable.ImmutableArray.Create(intent));

        var trade = events.OfType<MonoRogue.Core.Events.TradeCompletedEvent>().Single();
        Assert.Equal(80, trade.Cost);
    }

    [Fact]
    public void ProcessTrades_minimum_effective_price_is_1_gold()
    {
        var (state, intent) = MakeTradeSetup(barterLevel: 100, itemValue: 1);

        var events = MonoRogue.Core.Systems.EconomySystem.ProcessTrades(
            state,
            System.Collections.Immutable.ImmutableArray.Create(intent));

        var trade = events.OfType<MonoRogue.Core.Events.TradeCompletedEvent>().Single();
        Assert.Equal(1, trade.Cost);
    }
}
