namespace MonoRogue.Tests;

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
