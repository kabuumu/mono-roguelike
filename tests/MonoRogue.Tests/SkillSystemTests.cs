namespace MonoRogue.Tests;

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
