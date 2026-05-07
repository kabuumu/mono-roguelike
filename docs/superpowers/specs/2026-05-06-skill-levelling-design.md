# Skill Levelling System — Design

**Date:** 2026-05-06

## Problem

The player has a character level that advances on enemy kills, but no skill-based progression. Combat and trading feel the same at hour one and hour ten. Three activities are already fully implemented — melee combat, taking damage, and buying from NPCs — but none of them improve the player through use.

## Solution

Add three skills (Melee, Block, Barter) that level up from use, grant flat per-level stat bonuses, and are displayed on the existing character screen. All changes follow the existing immutable record / event / reducer pattern. The feature requires one new component, one new event, and small additions to three systems.

---

## Data Model

### New records in `src/Core/Model/Components.cs`

```csharp
public enum SkillType { Melee, Block, Barter }

public sealed record SkillData(int Level = 0, int Xp = 0, int XpToNextLevel = 20);

public sealed record SkillsComponent(
    SkillData Melee,
    SkillData Block,
    SkillData Barter
);
```

`SkillData` defaults: Level 0, 0 XP, threshold 20 (level 0 → 1 costs 20 XP).

XP threshold formula: `(currentLevel + 1) * 20`
- Level 0 → 1: 20 XP
- Level 1 → 2: 40 XP
- Level 2 → 3: 60 XP
- Level N → N+1: (N+1) × 20 XP

### Entity change (`src/Core/Model/Entity.cs`)

Add one optional field:

```csharp
SkillsComponent? Skills = null
```

### Player initialisation

`WorldGenerator.GenerateVillageRegion` (and `VillageGenerator.Generate`) initialises the player entity with:

```csharp
Skills: new SkillsComponent(
    Melee:  new SkillData(),
    Block:  new SkillData(),
    Barter: new SkillData()
)
```

All three skills start at level 0, 0 XP, threshold 20.

---

## Event

### New event in `src/Core/Events/Events.cs`

```csharp
public sealed record SkillXpGainedEvent(Guid EntityId, SkillType Skill, int Amount) : GameEvent;
```

---

## Reducer

### New case in `src/Core/Reducer.cs`

```csharp
SkillXpGainedEvent skillXp => ApplySkillXpGained(state, skillXp),
```

### `ApplySkillXpGained` logic

1. Look up the entity; return unchanged if entity not found or has no `SkillsComponent`.
2. Select the correct `SkillData` field using the `SkillType` enum.
3. Add `Amount` to `Xp`.
4. While `newXp >= XpToNextLevel`:
   - Increment `Level`
   - Reset `Xp` to 0
   - Set new `XpToNextLevel = (newLevel + 1) * 20`
   - Append message: `"Your {skill} skill reached level {newLevel}!"`
5. Write updated `SkillsComponent` back to the entity with `with` expressions.

---

## XP Sources

| Skill  | Trigger | XP per event | System |
|--------|---------|-------------|--------|
| Melee  | Player deals damage to an enemy | +1 | `CombatSystem.Resolve` — emitted alongside `DamagedEvent` when source is player |
| Block  | Player takes damage from an enemy | +1 | `CombatSystem.Resolve` — emitted alongside `DamagedEvent` when target is player |
| Barter | A trade completes with player as buyer | +5 | `EconomySystem.ProcessTrades` — emitted alongside `TradeCompletedEvent` |

**Pacing at these rates:**
- Melee level 1: ~20 hits dealt
- Block level 1: ~20 hits taken
- Barter level 1: 4 completed trades
- Each subsequent level costs proportionally more.

Block XP fires whenever the player receives a `DamagedEvent`. It does not require a special "block succeeded" check — the existing combat system already ensures damage dealt is always at least 1 (no zero-damage hits).

---

## Stat Effects

Skills feed into existing calculation methods. All lookups are null-safe so NPCs (which have no `SkillsComponent`) are unaffected.

### Melee — `CombatSystem.GetEffectiveAttack`

```csharp
var meleeBonus = entity.Skills?.Melee.Level ?? 0;
return baseAttack + weaponBonus + meleeBonus;
```

Level 3 Melee = +3 attack on top of base and weapon bonuses.

### Block — `CombatSystem.GetEffectiveDefense`

```csharp
var blockBonus = entity.Skills?.Block.Level ?? 0;
return baseDefense + armorBonus + blockBonus;
```

Level 3 Block = +3 defense on top of base and armour bonuses.

### Barter — `EconomySystem.ProcessTrades`

Buy price is discounted before gold validation:

```csharp
var barterLevel  = buyer.Skills?.Barter.Level ?? 0;
var effectivePrice = price - (price * barterLevel * 5 / 100);
if (effectivePrice < 1) effectivePrice = 1;
```

5% discount per level. Level 1 = 5% off, level 4 = 20% off. Price never drops below 1 gold.

---

## Character Screen

Skills are displayed at the bottom of the Statistics column in `AsciiRenderer.DrawCharacterScreen`, after the equipped items section.

Format per skill (one row each):

```
SKILLS
------------------
Melee    Lv 2  [####......]  45/60 XP
Block    Lv 0  [..........]   8/20 XP
Barter   Lv 1  [######....]  12/40 XP
```

A 10-segment bar uses `#` for filled segments and `.` for empty — both standard ASCII characters supported by the game's SpriteFont. If the player has no `SkillsComponent`, the section is omitted silently.

---

## Files Changed

| File | Change |
|------|--------|
| `src/Core/Model/Components.cs` | Add `SkillType` enum, `SkillData` record, `SkillsComponent` record |
| `src/Core/Model/Entity.cs` | Add `SkillsComponent? Skills` field |
| `src/Core/Events/Events.cs` | Add `SkillXpGainedEvent` |
| `src/Core/Reducer.cs` | Add `SkillXpGainedEvent` case + `ApplySkillXpGained` method |
| `src/Core/Systems/CombatSystem.cs` | Emit `SkillXpGainedEvent` for Melee and Block; add skill bonuses to `GetEffectiveAttack/Defense` |
| `src/Core/Systems/EconomySystem.cs` | Emit `SkillXpGainedEvent` for Barter; apply barter discount |
| `src/Core/Generation/WorldGenerator.cs` | Initialise `SkillsComponent` on player entity |
| `src/Core/Generation/VillageGenerator.cs` | Initialise `SkillsComponent` on player entity |
| `src/Shell/AsciiRenderer.cs` | Add SKILLS section to `DrawCharacterScreen` |

---

## What This Does Not Include

- Skill-gated content (e.g., "requires Melee 5 to enter")
- Skill decay or penalties
- Skills for other activities (ranged, magic, etc.) — these can be added later as named fields on `SkillsComponent`
- Skill data in save/load (save system not yet implemented)
