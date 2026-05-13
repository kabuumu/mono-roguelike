# Skill Levelling System Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add three use-based skills (Melee, Block, Barter) that start at level 0, accumulate XP from combat and trading, grant flat stat bonuses per level, and are displayed on the character screen.

**Architecture:** One new `SkillsComponent(SkillData Melee, SkillData Block, SkillData Barter)` component on the player entity. A single `SkillXpGainedEvent` feeds into the reducer. `CombatSystem.Resolve` emits melee/block XP; `EconomySystem.ProcessTrades` emits barter XP and applies a per-level discount. Skill levels feed into the existing `GetEffectiveAttack/Defense` helpers. All follows the existing event → reducer → immutable state pattern.

**Tech Stack:** C# / .NET 10, MonoGame, xUnit, immutable record types.

---

## File Map

| File | Action | What changes |
|---|---|---|
| `src/Core/Model/Components.cs` | Modify | Add `SkillType` enum, `SkillData` record, `SkillsComponent` record |
| `src/Core/Model/Entity.cs` | Modify | Add `SkillsComponent? Skills = null` field |
| `src/Core/Events/Events.cs` | Modify | Add `SkillXpGainedEvent` |
| `src/Core/Reducer.cs` | Modify | Add `SkillXpGainedEvent` case + `ApplySkillXpGained` method |
| `src/Core/Systems/CombatSystem.cs` | Modify | Emit Melee/Block XP events; add skill bonuses to `GetEffectiveAttack/Defense` |
| `src/Core/Systems/EconomySystem.cs` | Modify | Apply Barter discount; emit Barter XP event |
| `src/Shell/MonoRogueGame.cs` | Modify | Add `Skills` to `CreateFreshPlayer()` |
| `src/Shell/AsciiRenderer.cs` | Modify | Add SKILLS section to `DrawCharacterScreen` |
| `tests/MonoRogue.Tests/TestHelpers.cs` | Modify | Add `Skills` to `MakePlayer` factory |
| `tests/MonoRogue.Tests/SkillSystemTests.cs` | Create | All skill system tests |

---

## Task 1: Data model — SkillType, SkillData, SkillsComponent, Entity field

**Files:**
- Modify: `src/Core/Model/Components.cs`
- Modify: `src/Core/Model/Entity.cs`
- Modify: `tests/MonoRogue.Tests/TestHelpers.cs`
- Create: `tests/MonoRogue.Tests/SkillSystemTests.cs`

- [ ] **Step 1: Write failing tests for the new records**

Create `tests/MonoRogue.Tests/SkillSystemTests.cs`:

```csharp
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
```

- [ ] **Step 2: Run tests to confirm they fail (compile error)**

```bash
cd /Users/robhawkes/mono-roguelike && dotnet test tests/MonoRogue.Tests/ --filter "SkillDataTests" 2>&1 | grep -E "error|Error|Passed|Failed"
```

Expected: build error — `SkillData`, `SkillsComponent` not defined yet.

- [ ] **Step 3: Add SkillType enum, SkillData, and SkillsComponent to Components.cs**

In `src/Core/Model/Components.cs`, find the existing enums section at the top (after `public enum AiState`). Add `SkillType` there:

```csharp
public enum SkillType { Melee, Block, Barter }
```

Then at the very end of the file (after the `PeacefulTag` record), add:

```csharp
// ─────────────────────────────────────────────────
//  SKILLS  (player only)
// ─────────────────────────────────────────────────

/// <summary>XP and level for one skill. XpToNextLevel = (Level + 1) * 20.</summary>
public sealed record SkillData(int Level = 0, int Xp = 0, int XpToNextLevel = 20);

/// <summary>All three use-based skills for the player entity.</summary>
public sealed record SkillsComponent(
    SkillData Melee,
    SkillData Block,
    SkillData Barter
);
```

- [ ] **Step 4: Add Skills field to Entity**

In `src/Core/Model/Entity.cs`, add one line after `BackgroundComponent? Background = null`:

```csharp
public sealed record Entity(
    Guid Id,
    IdentityComponent?   Identity    = null,
    SpatialComponent?    Spatial     = null,
    RenderComponent?     Render      = null,
    HealthComponent?     Health      = null,
    PlayerTag?           Player      = null,
    TileTag?             Tile        = null,
    CombatStatsComponent? CombatStats = null,
    LevelComponent?      Level       = null,
    ItemComponent?       Item        = null,
    InventoryComponent?  Inventory   = null,
    EquipmentComponent?  Equipment   = null,
    AiComponent?         Ai          = null,
    StairsDownTag?       StairsDown  = null,
    DialogueComponent?   Dialogue    = null,
    PeacefulTag?         Peaceful    = null,
    BackgroundComponent? Background  = null,
    SkillsComponent?     Skills      = null
);
```

- [ ] **Step 5: Update TestHelpers.MakePlayer to include Skills**

In `tests/MonoRogue.Tests/TestHelpers.cs`, add `Skills` to `MakePlayer` so all existing tests get a player with skills initialised:

```csharp
public static Entity MakePlayer(Position pos) => new(
    Id:       Guid.NewGuid(),
    Identity: new IdentityComponent("Player", "player"),
    Spatial:  new SpatialComponent(pos, BlocksMovement: true),
    Health:   new HealthComponent(Current: 20, Max: 20),
    CombatStats: new CombatStatsComponent(Attack: 5, Defense: 1),
    Level:    new LevelComponent(Level: 1, Xp: 0, XpToNextLevel: 100),
    Inventory: new InventoryComponent(ImmutableArray<Guid>.Empty, MaxSlots: 10),
    Equipment: new EquipmentComponent(),
    Player:   new PlayerTag(),
    Skills:   new SkillsComponent(new SkillData(), new SkillData(), new SkillData())
);
```

- [ ] **Step 6: Run tests to confirm they pass**

```bash
cd /Users/robhawkes/mono-roguelike && dotnet test tests/MonoRogue.Tests/ --filter "SkillDataTests" 2>&1 | grep -E "Passed|Failed|passed|failed"
```

Expected: 4 tests pass.

- [ ] **Step 7: Run full suite to confirm no regressions**

```bash
cd /Users/robhawkes/mono-roguelike && dotnet test tests/MonoRogue.Tests/ 2>&1 | grep -E "Passed|Failed|passed|failed"
```

Expected: all 146 existing tests + 4 new = 150 pass.

- [ ] **Step 8: Commit**

```bash
cd /Users/robhawkes/mono-roguelike
git add src/Core/Model/Components.cs src/Core/Model/Entity.cs tests/MonoRogue.Tests/TestHelpers.cs tests/MonoRogue.Tests/SkillSystemTests.cs
git commit -m "feat: add SkillType, SkillData, SkillsComponent to data model"
```

---

## Task 2: SkillXpGainedEvent and Reducer handler

**Files:**
- Modify: `src/Core/Events/Events.cs`
- Modify: `src/Core/Reducer.cs`
- Modify: `tests/MonoRogue.Tests/SkillSystemTests.cs`

- [ ] **Step 1: Write failing reducer tests**

Add a new test class to `tests/MonoRogue.Tests/SkillSystemTests.cs` (append below the existing class):

```csharp
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
```

- [ ] **Step 2: Run to confirm compile error**

```bash
cd /Users/robhawkes/mono-roguelike && dotnet test tests/MonoRogue.Tests/ --filter "SkillXpReducerTests" 2>&1 | grep -E "error|Error|Passed|Failed"
```

Expected: build error — `SkillXpGainedEvent` not defined.

- [ ] **Step 3: Add SkillXpGainedEvent to Events.cs**

In `src/Core/Events/Events.cs`, find the `// ── Levelling` section (which contains `XpGainedEvent`). Add the new event immediately after `XpGainedEvent`:

```csharp
// ── Levelling ─────────────────────────────────────────────────────────────────

public sealed record XpGainedEvent(Guid EntityId, int Amount) : GameEvent;

/// <summary>A skill gained XP from use: melee hit dealt, damage taken, or trade completed.</summary>
public sealed record SkillXpGainedEvent(Guid EntityId, SkillType Skill, int Amount) : GameEvent;
```

- [ ] **Step 4: Add SkillXpGainedEvent case and ApplySkillXpGained to Reducer.cs**

In `src/Core/Reducer.cs`, add one line to the switch expression after the `XpGainedEvent` case:

```csharp
XpGainedEvent         xp      => ApplyXpGained(state,    xp),
SkillXpGainedEvent    skillXp => ApplySkillXpGained(state, skillXp),
```

Then add the new method after the `ApplyXpGained` method (around line 315, after the closing `}` of `ApplyXpGained`):

```csharp
// ── Skill XP + levelling ──────────────────────────────────────────────────

private static GameState ApplySkillXpGained(GameState state, SkillXpGainedEvent evt)
{
    if (!state.Entities.TryGetValue(evt.EntityId, out var entity)) return state;
    if (entity.Skills is null) return state;

    var skills = entity.Skills;
    var data = evt.Skill switch
    {
        SkillType.Melee  => skills.Melee,
        SkillType.Block  => skills.Block,
        SkillType.Barter => skills.Barter,
        _                => null
    };
    if (data is null) return state;

    var newXp        = data.Xp + evt.Amount;
    var newLevel     = data.Level;
    var newThreshold = data.XpToNextLevel;

    while (newXp >= newThreshold)
    {
        newXp        -= newThreshold;
        newLevel     += 1;
        newThreshold  = (newLevel + 1) * 20;
        state = state.AppendMessage($"Your {evt.Skill} skill reached level {newLevel}!");
    }

    var updated = new SkillData(newLevel, newXp, newThreshold);
    var newSkills = evt.Skill switch
    {
        SkillType.Melee  => skills with { Melee  = updated },
        SkillType.Block  => skills with { Block  = updated },
        SkillType.Barter => skills with { Barter = updated },
        _                => skills
    };

    entity = entity with { Skills = newSkills };
    return state with { Entities = state.Entities.SetItem(evt.EntityId, entity) };
}
```

- [ ] **Step 5: Run tests to confirm they pass**

```bash
cd /Users/robhawkes/mono-roguelike && dotnet test tests/MonoRogue.Tests/ --filter "SkillXpReducerTests" 2>&1 | grep -E "Passed|Failed|passed|failed"
```

Expected: 6 tests pass.

- [ ] **Step 6: Run full suite**

```bash
cd /Users/robhawkes/mono-roguelike && dotnet test tests/MonoRogue.Tests/ 2>&1 | grep -E "Passed|Failed|passed|failed"
```

Expected: all 156 pass (150 + 6 new).

- [ ] **Step 7: Commit**

```bash
cd /Users/robhawkes/mono-roguelike
git add src/Core/Events/Events.cs src/Core/Reducer.cs tests/MonoRogue.Tests/SkillSystemTests.cs
git commit -m "feat: add SkillXpGainedEvent and reducer handler"
```

---

## Task 3: CombatSystem — emit skill XP, add stat bonuses

**Files:**
- Modify: `src/Core/Systems/CombatSystem.cs`
- Modify: `tests/MonoRogue.Tests/SkillSystemTests.cs`

- [ ] **Step 1: Write failing tests**

Append to `tests/MonoRogue.Tests/SkillSystemTests.cs`:

```csharp
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
```

- [ ] **Step 2: Run to confirm 7 failures**

```bash
cd /Users/robhawkes/mono-roguelike && dotnet test tests/MonoRogue.Tests/ --filter "CombatSkillTests" 2>&1 | grep -E "Passed|Failed|passed|failed"
```

Expected: 7 tests fail (melee/block XP events not emitted yet; stat methods don't include skill bonuses yet).

- [ ] **Step 3: Emit Melee and Block XP events in CombatSystem.Resolve**

In `src/Core/Systems/CombatSystem.cs`, find the line that adds `DamagedEvent`:

```csharp
events.Add(new DamagedEvent(targetId, damage, attackerId));
```

Add these two lines immediately after it:

```csharp
events.Add(new DamagedEvent(targetId, damage, attackerId));

// Melee XP for the attacker when they are the player
if (attacker.IsPlayer())
    events.Add(new SkillXpGainedEvent(attackerId, SkillType.Melee, 1));

// Block XP for the target when they are the player
if (target.IsPlayer())
    events.Add(new SkillXpGainedEvent(targetId, SkillType.Block, 1));
```

- [ ] **Step 4: Add Melee bonus to GetEffectiveAttack**

Replace the existing `GetEffectiveAttack` method:

```csharp
public static int GetEffectiveAttack(GameState state, Entity e)
{
    var baseAtk = e.CombatStats?.Attack ?? 1;
    if (e.Equipment?.WeaponId is { } wId &&
        state.Entities.TryGetValue(wId, out var weapon))
        baseAtk += weapon.Item?.AttackBonus ?? 0;
    baseAtk += e.Skills?.Melee.Level ?? 0;
    return baseAtk;
}
```

- [ ] **Step 5: Add Block bonus to GetEffectiveDefense**

Replace the existing `GetEffectiveDefense` method:

```csharp
public static int GetEffectiveDefense(GameState state, Entity e)
{
    var baseDef = e.CombatStats?.Defense ?? 0;
    if (e.Equipment?.ArmorId is { } aId &&
        state.Entities.TryGetValue(aId, out var armor))
        baseDef += armor.Item?.DefenseBonus ?? 0;
    baseDef += e.Skills?.Block.Level ?? 0;
    return baseDef;
}
```

- [ ] **Step 6: Run tests to confirm 7 pass**

```bash
cd /Users/robhawkes/mono-roguelike && dotnet test tests/MonoRogue.Tests/ --filter "CombatSkillTests" 2>&1 | grep -E "Passed|Failed|passed|failed"
```

Expected: 7 tests pass.

- [ ] **Step 7: Run full suite**

```bash
cd /Users/robhawkes/mono-roguelike && dotnet test tests/MonoRogue.Tests/ 2>&1 | grep -E "Passed|Failed|passed|failed"
```

Expected: all tests pass.

- [ ] **Step 8: Commit**

```bash
cd /Users/robhawkes/mono-roguelike
git add src/Core/Systems/CombatSystem.cs tests/MonoRogue.Tests/SkillSystemTests.cs
git commit -m "feat: emit melee/block XP from CombatSystem, add skill bonuses to attack/defense"
```

---

## Task 4: EconomySystem — barter discount and XP

**Files:**
- Modify: `src/Core/Systems/EconomySystem.cs`
- Modify: `tests/MonoRogue.Tests/SkillSystemTests.cs`

- [ ] **Step 1: Write failing tests**

Append to `tests/MonoRogue.Tests/SkillSystemTests.cs`:

```csharp
public sealed class BarterSkillTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

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

    // ── Tests ─────────────────────────────────────────────────────────────────

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
        Assert.Equal(95, trade.Cost); // 100 - 5%
    }

    [Fact]
    public void ProcessTrades_applies_20_percent_discount_at_barter_level_4()
    {
        var (state, intent) = MakeTradeSetup(barterLevel: 4, itemValue: 100);

        var events = MonoRogue.Core.Systems.EconomySystem.ProcessTrades(
            state,
            System.Collections.Immutable.ImmutableArray.Create(intent));

        var trade = events.OfType<MonoRogue.Core.Events.TradeCompletedEvent>().Single();
        Assert.Equal(80, trade.Cost); // 100 - 20%
    }

    [Fact]
    public void ProcessTrades_minimum_effective_price_is_1_gold()
    {
        // Barter level 100 on a 1g item — should not go below 1g
        var (state, intent) = MakeTradeSetup(barterLevel: 100, itemValue: 1);

        var events = MonoRogue.Core.Systems.EconomySystem.ProcessTrades(
            state,
            System.Collections.Immutable.ImmutableArray.Create(intent));

        var trade = events.OfType<MonoRogue.Core.Events.TradeCompletedEvent>().Single();
        Assert.Equal(1, trade.Cost);
    }
}
```

- [ ] **Step 2: Run to confirm failures**

```bash
cd /Users/robhawkes/mono-roguelike && dotnet test tests/MonoRogue.Tests/ --filter "BarterSkillTests" 2>&1 | grep -E "Passed|Failed|passed|failed"
```

Expected: tests fail (no discount logic, no barter XP event).

- [ ] **Step 3: Update EconomySystem.ProcessTrades**

In `src/Core/Systems/EconomySystem.cs`, find the block that computes `totalCost` and adds `TradeCompletedEvent`. Replace the relevant section:

```csharp
// BEFORE
var unitValue = item.Item?.Value ?? 0;
var totalCost = unitValue * Math.Max(1, intent.Quantity);

if (totalCost <= 0)
{
    events.Add(new MessageLoggedEvent("That item has no price set."));
    continue;
}

// Buyer must have enough gold
var buyerGold = buyer.Inventory?.Gold ?? 0;
if (buyerGold < totalCost)
{
    events.Add(new MessageLoggedEvent(
        $"You need {totalCost} gold. You have {buyerGold}."));
    continue;
}

var itemName = item.Identity?.Name ?? "item";
events.Add(new TradeCompletedEvent(intent.BuyerId, intent.SellerId, intent.ItemId, totalCost));
events.Add(new MessageLoggedEvent(
    $"You buy {itemName} for {totalCost} gold. ({buyerGold - totalCost} gold remaining)"));
```

```csharp
// AFTER
var unitValue = item.Item?.Value ?? 0;
var totalCost = unitValue * Math.Max(1, intent.Quantity);

if (totalCost <= 0)
{
    events.Add(new MessageLoggedEvent("That item has no price set."));
    continue;
}

// Apply barter discount: 5% off per barter skill level, minimum 1 gold
var barterLevel    = buyer.Skills?.Barter.Level ?? 0;
var effectivePrice = totalCost - (totalCost * barterLevel * 5 / 100);
if (effectivePrice < 1) effectivePrice = 1;

// Buyer must have enough gold
var buyerGold = buyer.Inventory?.Gold ?? 0;
if (buyerGold < effectivePrice)
{
    events.Add(new MessageLoggedEvent(
        $"You need {effectivePrice} gold. You have {buyerGold}."));
    continue;
}

var itemName = item.Identity?.Name ?? "item";
events.Add(new TradeCompletedEvent(intent.BuyerId, intent.SellerId, intent.ItemId, effectivePrice));
events.Add(new SkillXpGainedEvent(intent.BuyerId, SkillType.Barter, 5));
events.Add(new MessageLoggedEvent(
    $"You buy {itemName} for {effectivePrice} gold. ({buyerGold - effectivePrice} gold remaining)"));
```

- [ ] **Step 4: Run tests to confirm 5 pass**

```bash
cd /Users/robhawkes/mono-roguelike && dotnet test tests/MonoRogue.Tests/ --filter "BarterSkillTests" 2>&1 | grep -E "Passed|Failed|passed|failed"
```

Expected: 5 tests pass.

- [ ] **Step 5: Run full suite**

```bash
cd /Users/robhawkes/mono-roguelike && dotnet test tests/MonoRogue.Tests/ 2>&1 | grep -E "Passed|Failed|passed|failed"
```

Expected: all tests pass.

- [ ] **Step 6: Commit**

```bash
cd /Users/robhawkes/mono-roguelike
git add src/Core/Systems/EconomySystem.cs tests/MonoRogue.Tests/SkillSystemTests.cs
git commit -m "feat: add barter discount and XP to EconomySystem"
```

---

## Task 5: Player initialisation and character screen display

**Files:**
- Modify: `src/Shell/MonoRogueGame.cs`
- Modify: `src/Shell/AsciiRenderer.cs`

- [ ] **Step 1: Add Skills to CreateFreshPlayer in MonoRogueGame.cs**

In `src/Shell/MonoRogueGame.cs`, find `CreateFreshPlayer()` (around line 574). Add the `Skills` field:

```csharp
private Entity CreateFreshPlayer() => new(
    Id:          Guid.NewGuid(),
    Identity:    new IdentityComponent("Adventurer", "player"),
    Spatial:     new SpatialComponent(Position.Zero, BlocksMovement: true),
    Render:      new RenderComponent("creature_player", "#00FF88"),
    Health:      new HealthComponent(30, 30),
    Player:      new PlayerTag(),
    CombatStats: new CombatStatsComponent(Attack: 3, Defense: 0, XpValue: 0),
    Level:       new LevelComponent(Level: 1, Xp: 0, XpToNextLevel: 100),
    Inventory:   new InventoryComponent([], MaxSlots: 10),
    Equipment:   new EquipmentComponent(),
    Skills:      new SkillsComponent(new SkillData(), new SkillData(), new SkillData())
);
```

- [ ] **Step 2: Build to confirm it compiles**

```bash
cd /Users/robhawkes/mono-roguelike && dotnet build monogame-rpg.csproj 2>&1 | grep -E "error|Build succeeded|FAILED"
```

Expected: Build succeeded.

- [ ] **Step 3: Add SKILLS section to DrawCharacterScreen in AsciiRenderer.cs**

In `src/Shell/AsciiRenderer.cs`, find `DrawCharacterScreen`. After the last line of the Statistics column (the equipped armour Print, around line 587):

```csharp
Print($"  Armor:  {EquippedName(state, equip?.ArmorId)}",  col1X, y, TextSecondary);
```

Add the skills section immediately after:

```csharp
Print($"  Armor:  {EquippedName(state, equip?.ArmorId)}",  col1X, y, TextSecondary);

// ── Skills ────────────────────────────────────────────────────────────────
var skills = player.Skills;
if (skills is not null)
{
    y += 28;
    Print("SKILLS", col1X, y, TextPrimary);
    DrawRect(col1X, y + 18, colInnerW, 1, BorderSide);
    y += 30;

    DrawSkillRow("Melee",  skills.Melee,  col1X, ref y);
    DrawSkillRow("Block",  skills.Block,  col1X, ref y);
    DrawSkillRow("Barter", skills.Barter, col1X, ref y);
}
```

Then add the private helper method anywhere in the `AsciiRenderer` class (e.g., near the other `DrawCharacterScreen` helpers):

```csharp
private void DrawSkillRow(string name, SkillData skill, int x, ref int y)
{
    int filled = skill.XpToNextLevel > 0
        ? Math.Min(10, (int)(10.0 * skill.Xp / skill.XpToNextLevel))
        : 10;
    var bar = "[" + new string('#', filled) + new string('.', 10 - filled) + "]";
    Print($"{name,-8} Lv {skill.Level,2}  {bar}  {skill.Xp}/{skill.XpToNextLevel} XP",
          x, y, TextSecondary);
    y += 22;
}
```

- [ ] **Step 4: Build to confirm it compiles**

```bash
cd /Users/robhawkes/mono-roguelike && dotnet build monogame-rpg.csproj 2>&1 | grep -E "error|Build succeeded|FAILED"
```

Expected: Build succeeded.

- [ ] **Step 5: Run full test suite**

```bash
cd /Users/robhawkes/mono-roguelike && dotnet test tests/MonoRogue.Tests/ 2>&1 | grep -E "Passed|Failed|passed|failed"
```

Expected: all tests pass.

- [ ] **Step 6: Commit**

```bash
cd /Users/robhawkes/mono-roguelike
git add src/Shell/MonoRogueGame.cs src/Shell/AsciiRenderer.cs
git commit -m "feat: initialise player skills and display on character screen"
```

---

## Verification Checklist

After all tasks are complete, start a new game and verify in-game:

- [ ] Open the character screen (C key) — a SKILLS section appears below STATISTICS with Melee, Block, Barter all at Lv 0, XP bars empty.
- [ ] Attack an enemy — message log shows hits. After ~20 hits, message log shows `"Your Melee skill reached level 1!"` and the Melee bar updates on the character screen.
- [ ] Take hits from an enemy — after ~20 hits taken, `"Your Block skill reached level 1!"` appears.
- [ ] Buy an item from a shop NPC — after 4 purchases, `"Your Barter skill reached level 1!"`. At Barter level 1, a 100g item costs 95g.
- [ ] Melee level 1 player has base attack = 3 + 1 = 4 (visible on character screen as Attack: 4).
- [ ] Block level 1 player has base defense = 0 + 1 = 1.
