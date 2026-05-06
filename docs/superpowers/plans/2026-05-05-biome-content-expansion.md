# Biome Content Expansion Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add one NPC with a quest to each of the four biomes, give two village NPCs quest-giving dialogue options, add six new kill-objective quests, and introduce a `give_quest:` dialogue action.

**Architecture:** A single static `DataRegistry.BuildQuest(QuestTemplate)` helper centralises QuestTemplate → Quest conversion (used by both WorldGenerator and the new dialogue action handler). All other changes are JSON data: NPC blueprints, quest files, item blueprints, and area wiring. One new `case` in `MonoRogueGame.ExecuteOption` handles the `give_quest:` dialogue action.

**Tech Stack:** C# / .NET 10, MonoGame, xUnit, immutable record types, JSON data files.

---

## File Map

| File | Action | What changes |
|---|---|---|
| `src/Data/DataRegistry.cs` | Modify | Add `public static Quest BuildQuest(QuestTemplate)` helper |
| `src/Core/Generation/WorldGenerator.cs` | Modify | Replace inline quest-building with `DataRegistry.BuildQuest` call |
| `src/Shell/MonoRogueGame.cs` | Modify | Add `give_quest:` case to `ExecuteOption` |
| `tests/MonoRogue.Tests/QuestSystemTests.cs` | Modify | Add `QuestBuildTests` class |
| `Content/Data/blueprints/blueprints_items.json` | Modify | Add `item_ancient_scroll`, `item_swamp_herb` |
| `Content/Data/quests/quest_cull_boars.json` | Create | Hermit quest: kill 4 boars |
| `Content/Data/quests/quest_clear_caverns.json` | Create | Prospector quest: kill 5 goblins |
| `Content/Data/quests/quest_orc_cleansing.json` | Create | Scholar quest: kill 4 orcs |
| `Content/Data/quests/quest_swamp_trolls.json` | Create | Exile quest: kill 3 trolls |
| `Content/Data/quests/quest_cave_scout.json` | Create | Elder quest: kill 3 rats |
| `Content/Data/quests/quest_boar_cull.json` | Create | Guard quest: kill 3 boars |
| `Content/Data/blueprints/blueprints_npcs.json` | Modify | Add 4 biome NPCs; convert Elder + Guard to dialogPool |
| `src/Core/Generation/WorldGenerator.cs` | Modify (×2) | Refactor quest building (Task 1); add Elder + Guard to village spawn pool (Task 8) |
| `Content/Data/areas/areas.json` | Modify | Wire biome NPCs into forest, cave, swamp |

---

## Task 1: DataRegistry.BuildQuest helper + tests

**Files:**
- Modify: `src/Data/DataRegistry.cs`
- Modify: `src/Core/Generation/WorldGenerator.cs`
- Modify: `tests/MonoRogue.Tests/QuestSystemTests.cs`

- [ ] **Step 1: Write two failing tests for `DataRegistry.BuildQuest`**

Add a new test class at the bottom of `tests/MonoRogue.Tests/QuestSystemTests.cs`:

```csharp
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
    }
}
```

- [ ] **Step 2: Run tests to confirm they fail**

```bash
dotnet test tests/MonoRogue.Tests/ --filter "QuestBuildTests"
```

Expected: build error — `DataRegistry.BuildQuest` does not exist yet.

- [ ] **Step 3: Add `BuildQuest` to DataRegistry and required using**

In `src/Data/DataRegistry.cs`, add `using MonoRogue.Core.Model;` at the top and add the method to the `DataRegistry` record (after the `Empty()` factory):

```csharp
using System.Collections.Immutable;
using System.Text.Json;
using MonoRogue.Core.Model;
```

```csharp
    /// <summary>Converts a QuestTemplate (JSON data) into a runtime Quest record.</summary>
    public static Quest BuildQuest(QuestTemplate tpl)
    {
        var reqItems = tpl.RequiredItems?.ToImmutableDictionary()
                       ?? ImmutableDictionary<string, int>.Empty;

        ImmutableArray<QuestObjective>? objectives = null;
        if (tpl.Objectives?.Length > 0)
            objectives = tpl.Objectives
                .Select(o => new QuestObjective(o.Type, o.TargetId, o.RequiredCount))
                .ToImmutableArray();

        return new Quest(
            tpl.Id, tpl.Name, tpl.Description, reqItems,
            tpl.CompletionText ?? "",
            Objectives:  objectives,
            TurnInNpcId: tpl.TurnInNpcId);
    }
```

- [ ] **Step 4: Run tests to verify they pass**

```bash
dotnet test tests/MonoRogue.Tests/ --filter "QuestBuildTests"
```

Expected: 2 tests pass.

- [ ] **Step 5: Refactor WorldGenerator to use DataRegistry.BuildQuest**

In `src/Core/Generation/WorldGenerator.cs`, find the inline quest-building block (lines ~162–184) and replace it:

```csharp
// BEFORE
foreach (var qId in bg.StartingQuests)
{
    if (registry.Quests.TryGetValue(qId, out var qTpl))
    {
        var reqItems = qTpl.RequiredItems?.ToImmutableDictionary()
                       ?? ImmutableDictionary<string, int>.Empty;

        ImmutableArray<QuestObjective>? objectives = null;
        if (qTpl.Objectives?.Length > 0)
        {
            objectives = qTpl.Objectives
                .Select(o => new QuestObjective(o.Type, o.TargetId, o.RequiredCount))
                .ToImmutableArray();
        }

        activeQuests.Add(new Quest(
            qTpl.Id, qTpl.Name, qTpl.Description, reqItems,
            qTpl.CompletionText ?? "",
            Objectives:  objectives,
            TurnInNpcId: qTpl.TurnInNpcId));
    }
}
```

```csharp
// AFTER
foreach (var qId in bg.StartingQuests)
{
    if (registry.Quests.TryGetValue(qId, out var qTpl))
        activeQuests.Add(DataRegistry.BuildQuest(qTpl));
}
```

- [ ] **Step 6: Run all tests to confirm nothing regressed**

```bash
dotnet test tests/MonoRogue.Tests/
```

Expected: all tests pass.

- [ ] **Step 7: Commit**

```bash
git add src/Data/DataRegistry.cs src/Core/Generation/WorldGenerator.cs tests/MonoRogue.Tests/QuestSystemTests.cs
git commit -m "feat: extract DataRegistry.BuildQuest helper, add tests"
```

---

## Task 2: give_quest: dialogue action

**Files:**
- Modify: `src/Shell/MonoRogueGame.cs`

- [ ] **Step 1: Add `give_quest:` case to `ExecuteOption`**

In `src/Shell/MonoRogueGame.cs`, find the `ExecuteOption` method (around line 544) and replace it:

```csharp
private void ExecuteOption(Core.Model.DialogueOption option, Guid npcId, string npcName)
{
    _state = _state with { ActiveDialogue = null };
    switch (option.Action)
    {
        case "open_shop":
            _state = _state with { ActiveBarter = new BarterState(npcId, npcName) };
            break;

        case string s when s.StartsWith("give_quest:"):
        {
            var questId          = s["give_quest:".Length..];
            var alreadyActive    = _state.ActiveQuests?.Any(q => q.Id == questId) ?? false;
            var alreadyCompleted = _state.CompletedQuestIds?.Contains(questId) ?? false;
            if (!alreadyActive && !alreadyCompleted &&
                _registry.Quests.TryGetValue(questId, out var qTpl))
            {
                var quest = DataRegistry.BuildQuest(qTpl);
                _state = _state with
                {
                    ActiveQuests = (_state.ActiveQuests ?? ImmutableList<Quest>.Empty).Add(quest)
                };
                _state = _state.AppendMessage($"Quest accepted: \"{quest.Name}\".");
            }
            break;
        }
    }
}
```

Add the required using at the top of the file if not already present:
```csharp
using MonoRogue.Data;
using System.Collections.Immutable;
```

- [ ] **Step 2: Build to confirm it compiles**

```bash
dotnet build
```

Expected: Build succeeded with 0 errors.

- [ ] **Step 3: Commit**

```bash
git add src/Shell/MonoRogueGame.cs
git commit -m "feat: add give_quest: dialogue action to ExecuteOption"
```

---

## Task 3: New item blueprints

**Files:**
- Modify: `Content/Data/blueprints/blueprints_items.json`

- [ ] **Step 1: Add two new consumable items**

Open `Content/Data/blueprints/blueprints_items.json` and add the following entries before the closing `]` of the array:

```json
  // ── Biome-shop consumables ─────────────────────────────────────────────────
  {
    "id":             "item_ancient_scroll",
    "name":           "Ancient Scroll",
    "spriteKey":      "item_scroll",
    "colorTint":      "#FFEE99",
    "blocksMovement": false,
    "isItem":         true,
    "itemType":       "Consumable",
    "healAmount":     15,
    "value":          20
  },
  {
    "id":             "item_swamp_herb",
    "name":           "Swamp Herb",
    "spriteKey":      "item_herb",
    "colorTint":      "#88CC44",
    "blocksMovement": false,
    "isItem":         true,
    "itemType":       "Consumable",
    "healAmount":     12,
    "value":          8
  }
```

- [ ] **Step 2: Build and run tests to confirm JSON loads**

```bash
dotnet build && dotnet test tests/MonoRogue.Tests/
```

Expected: all tests pass (DataRegistry will load the new items without error).

- [ ] **Step 3: Commit**

```bash
git add Content/Data/blueprints/blueprints_items.json
git commit -m "feat: add item_ancient_scroll and item_swamp_herb"
```

---

## Task 4: Biome quest files (4 new quests)

**Files:**
- Create: `Content/Data/quests/quest_cull_boars.json`
- Create: `Content/Data/quests/quest_clear_caverns.json`
- Create: `Content/Data/quests/quest_orc_cleansing.json`
- Create: `Content/Data/quests/quest_swamp_trolls.json`

- [ ] **Step 1: Create quest_cull_boars.json**

```json
[
  {
    "id": "quest_cull_boars",
    "name": "Boar Problem",
    "description": "Boars keep raiding my camp. Kill four of them and maybe I'll get some sleep.",
    "requiredItems": {},
    "completionText": "Good. Peace at last. Here, take this.",
    "objectives": [
      {
        "type": "kill_target",
        "targetId": "creature_boar",
        "requiredCount": 4
      }
    ],
    "rewardGold": 35
  }
]
```

- [ ] **Step 2: Create quest_clear_caverns.json**

```json
[
  {
    "id": "quest_clear_caverns",
    "name": "Clear the Caverns",
    "description": "Goblins overran my dig site. Kill five of them and I can finally get back to work.",
    "requiredItems": {},
    "completionText": "The site is clear! Take this as thanks — I found it in the rocks.",
    "objectives": [
      {
        "type": "kill_target",
        "targetId": "creature_goblin",
        "requiredCount": 5
      }
    ],
    "rewardGold": 40,
    "rewardItems": ["item_chainmail"]
  }
]
```

- [ ] **Step 3: Create quest_orc_cleansing.json**

```json
[
  {
    "id": "quest_orc_cleansing",
    "name": "Drive Out the Orcs",
    "description": "Orcs are smashing through the inner sanctum. Kill four of them before they destroy what's left.",
    "requiredItems": {},
    "completionText": "The sanctum is safe. The inscriptions survived. You have my gratitude.",
    "objectives": [
      {
        "type": "kill_target",
        "targetId": "creature_orc",
        "requiredCount": 4
      }
    ],
    "rewardGold": 60
  }
]
```

- [ ] **Step 4: Create quest_swamp_trolls.json**

```json
[
  {
    "id": "quest_swamp_trolls",
    "name": "Troll Toll",
    "description": "Three trolls have staked out the paths through the swamp. Kill them and the way opens up.",
    "requiredItems": {},
    "completionText": "Finally. I can move around again. Here — I've been saving this.",
    "objectives": [
      {
        "type": "kill_target",
        "targetId": "creature_troll",
        "requiredCount": 3
      }
    ],
    "rewardGold": 50,
    "rewardItems": ["item_health_potion"]
  }
]
```

- [ ] **Step 5: Build to confirm all four files load**

```bash
dotnet build
```

Expected: Build succeeded with 0 errors.

- [ ] **Step 6: Commit**

```bash
git add Content/Data/quests/quest_cull_boars.json Content/Data/quests/quest_clear_caverns.json Content/Data/quests/quest_orc_cleansing.json Content/Data/quests/quest_swamp_trolls.json
git commit -m "feat: add four biome NPC quest files"
```

---

## Task 5: Village quest files (2 new quests)

**Files:**
- Create: `Content/Data/quests/quest_cave_scout.json`
- Create: `Content/Data/quests/quest_boar_cull.json`

- [ ] **Step 1: Create quest_cave_scout.json**

```json
[
  {
    "id": "quest_cave_scout",
    "name": "Into the Darkness",
    "description": "We need to know what lurks in the Crystal Caverns to the east. Enter and kill three rats as proof you made it inside.",
    "requiredItems": {},
    "completionText": "You returned. Not everyone does. Here — take this for your courage.",
    "objectives": [
      {
        "type": "kill_target",
        "targetId": "creature_rat",
        "requiredCount": 3
      }
    ],
    "rewardGold": 20,
    "rewardItems": ["item_health_potion"]
  }
]
```

- [ ] **Step 2: Create quest_boar_cull.json**

```json
[
  {
    "id": "quest_boar_cull",
    "name": "Boars at the Gate",
    "description": "Boars have been breaking into the food stores at night. Kill three of them.",
    "requiredItems": {},
    "completionText": "Good work. The stores are safe for another season.",
    "objectives": [
      {
        "type": "kill_target",
        "targetId": "creature_boar",
        "requiredCount": 3
      }
    ],
    "rewardGold": 25
  }
]
```

- [ ] **Step 3: Build and run tests**

```bash
dotnet build && dotnet test tests/MonoRogue.Tests/
```

Expected: all tests pass.

- [ ] **Step 4: Commit**

```bash
git add Content/Data/quests/quest_cave_scout.json Content/Data/quests/quest_boar_cull.json
git commit -m "feat: add village NPC quest files (cave scout, boar cull)"
```

---

## Task 6: Biome NPC blueprints

**Files:**
- Modify: `Content/Data/blueprints/blueprints_npcs.json`

- [ ] **Step 1: Add four biome NPC templates**

Open `Content/Data/blueprints/blueprints_npcs.json` and add the following section before the closing `]`:

```json
  // ── Biome NPCs ─────────────────────────────────────────────────────────────
  {
    "id": "npc_hermit",
    "name": "Old Gareth",
    "spriteKey": "npc_villager",
    "colorTint": "#AA9977",
    "blocksMovement": true,
    "isPeaceful": true,
    "hasDialogue": true,
    "dialogPool": {
      "after_quest_completed:quest_cull_boars": [
        "You did it. Quiet nights again.",
        "I haven't slept this well in months. The forest is yours to wander."
      ],
      "default": [
        "Didn't think I'd see another soul out here.",
        "These boars have been raiding my camp for weeks. I can't keep this up.",
        "If you're brave enough, kill four of them. I'll make it worth your while."
      ]
    },
    "dialogOptions": [
      { "id": "hunt_boars", "label": "I'll handle the boars.", "action": "give_quest:quest_cull_boars" },
      { "id": "farewell",   "label": "Farewell.",              "action": "close" }
    ]
  },
  {
    "id": "npc_prospector",
    "name": "Yenna the Prospector",
    "spriteKey": "npc_villager",
    "colorTint": "#CCAA66",
    "blocksMovement": true,
    "isPeaceful": true,
    "hasDialogue": true,
    "hasInventory": true,
    "inventorySlots": 15,
    "startingGold": 60,
    "shopInventory": [
      "item_chainmail",
      "item_health_potion",
      "item_health_potion"
    ],
    "dialogPool": {
      "after_quest_completed:quest_clear_caverns": [
        "The site is mine again. I can finally finish what I started.",
        "Come find me when you need gear — I'll keep a good stock from now on."
      ],
      "default": [
        "Goblins drove me out of my own dig site. Five of those wretches, at least.",
        "Clear them out and I'll give you something from my pack.",
        "I've also got gear to sell, if you need it."
      ]
    },
    "dialogOptions": [
      { "id": "clear_cave",  "label": "I'll clear the goblins.",    "action": "give_quest:quest_clear_caverns" },
      { "id": "barter",      "label": "What are you selling?",      "action": "open_shop" },
      { "id": "farewell",    "label": "Farewell.",                   "action": "close" }
    ]
  },
  {
    "id": "npc_scholar",
    "name": "Brother Aldric",
    "spriteKey": "npc_villager",
    "colorTint": "#AAAAEE",
    "blocksMovement": true,
    "isPeaceful": true,
    "hasDialogue": true,
    "hasInventory": true,
    "inventorySlots": 15,
    "startingGold": 80,
    "shopInventory": [
      "item_ancient_scroll",
      "item_ancient_scroll",
      "item_health_potion"
    ],
    "dialogPool": {
      "after_quest_completed:quest_orc_cleansing": [
        "The sanctum is quiet again. The inscriptions are intact.",
        "I'll be here for months yet. Come back if you need supplies."
      ],
      "default": [
        "I've been studying these ruins for six months. Remarkable civilisation.",
        "The orcs arrived last week. Four of them are smashing through the inner sanctum.",
        "Kill them and I'll share what I've found — and I have scrolls that are worth carrying."
      ]
    },
    "dialogOptions": [
      { "id": "orc_cleanse", "label": "I'll drive out the orcs.",   "action": "give_quest:quest_orc_cleansing" },
      { "id": "barter",      "label": "What do you have to sell?",  "action": "open_shop" },
      { "id": "farewell",    "label": "Farewell.",                   "action": "close" }
    ]
  },
  {
    "id": "npc_exile",
    "name": "Willa the Exile",
    "spriteKey": "npc_villager",
    "colorTint": "#99BB99",
    "blocksMovement": true,
    "isPeaceful": true,
    "hasDialogue": true,
    "hasInventory": true,
    "inventorySlots": 15,
    "startingGold": 30,
    "shopInventory": [
      "item_swamp_herb",
      "item_swamp_herb",
      "item_swamp_herb",
      "item_bandage",
      "item_bandage"
    ],
    "dialogPool": {
      "after_quest_completed:quest_swamp_trolls": [
        "First time in years I've walked the south path without looking over my shoulder.",
        "The herbs grow thick near the old stone. Come back whenever you need patching up."
      ],
      "default": [
        "Millhaven threw me out. The swamp kept me alive. I don't owe either of them a thing.",
        "Three trolls have blocked every path through here. I'm running low on everything.",
        "Clear them and I'll sell you whatever I've got. Herbs mostly, but they work."
      ]
    },
    "dialogOptions": [
      { "id": "troll_toll", "label": "I'll deal with the trolls.", "action": "give_quest:quest_swamp_trolls" },
      { "id": "barter",     "label": "Show me what you have.",     "action": "open_shop" },
      { "id": "farewell",   "label": "Farewell.",                   "action": "close" }
    ]
  }
```

- [ ] **Step 2: Build to confirm JSON loads**

```bash
dotnet build
```

Expected: Build succeeded with 0 errors.

- [ ] **Step 3: Run tests**

```bash
dotnet test tests/MonoRogue.Tests/
```

Expected: all tests pass.

- [ ] **Step 4: Commit**

```bash
git add Content/Data/blueprints/blueprints_npcs.json
git commit -m "feat: add four biome NPC blueprints (hermit, prospector, scholar, exile)"
```

---

## Task 7: Wire biome NPCs into areas.json

**Files:**
- Modify: `Content/Data/areas/areas.json`

- [ ] **Step 1: Add npcs entries to forest, cave, and swamp**

Open `Content/Data/areas/areas.json`. Update the three areas as follows (the ruins entry already has `npc_trader` and remains unchanged):

For `"id": "forest"`, change `"npcs": []` to:
```json
    "npcs": [
      { "templateId": "npc_hermit", "count": [1, 1] }
    ],
```

For `"id": "cave"`, change `"npcs": []` to:
```json
    "npcs": [
      { "templateId": "npc_prospector", "count": [1, 1] }
    ],
```

For `"id": "swamp"`, change `"npcs": []` to:
```json
    "npcs": [
      { "templateId": "npc_exile", "count": [1, 1] }
    ],
```

- [ ] **Step 2: Build and run tests**

```bash
dotnet build && dotnet test tests/MonoRogue.Tests/
```

Expected: all tests pass.

- [ ] **Step 3: Commit**

```bash
git add Content/Data/areas/areas.json
git commit -m "feat: wire biome NPCs into forest, cave, and swamp areas"
```

---

## Task 8: Update Elder Maren and Guard Ollen, wire into WorldGenerator

**Context:** Elder Maren and Guard Ollen are defined in `blueprints_npcs.json` but currently use a `dialogueLines` format that `SpawnSystem.BuildEntity` cannot use (it only reads `dialogPool`). They are also not spawned anywhere in `WorldGenerator.GenerateVillageRegion`. This task converts their dialogue and adds them to the village spawn pool.

**Files:**
- Modify: `Content/Data/blueprints/blueprints_npcs.json`
- Modify: `src/Core/Generation/WorldGenerator.cs`

- [ ] **Step 1: Convert npc_elder to dialogPool format and add dialogOptions**

Find the `npc_elder` entry in `Content/Data/blueprints/blueprints_npcs.json`. Replace it entirely:

```json
  {
    "id":             "npc_elder",
    "name":           "Elder Maren",
    "spriteKey":      "npc_elder",
    "colorTint":      "#FFDD88",
    "blocksMovement": true,
    "isPeaceful":     true,
    "hasDialogue":    true,
    "dialogPool": {
      "after_quest_completed:quest_cave_scout": [
        "You went in and came back out. That's more than most.",
        "Stay safe out there. The caverns go deeper than anyone knows."
      ],
      "default": [
        "Welcome to Millhaven, adventurer.",
        "Dark creatures have crept up from the depths beneath our town.",
        "The Crystal Caverns to the east concern me most — we need to know what stirs in there.",
        "Return when you have reached the sixth level of the dungeon. Only then will we be safe.",
        "May fortune guide your blade."
      ]
    },
    "dialogOptions": [
      { "id": "scout_cave", "label": "I'll scout the caverns.",  "action": "give_quest:quest_cave_scout" },
      { "id": "farewell",   "label": "Farewell.",                "action": "close" }
    ]
  },
```

- [ ] **Step 2: Convert npc_guard to dialogPool format and add dialogOptions**

Find the `npc_guard` entry in `Content/Data/blueprints/blueprints_npcs.json`. Replace it entirely:

```json
  {
    "id":             "npc_guard",
    "name":           "Guard Ollen",
    "spriteKey":      "npc_guard",
    "colorTint":      "#8899CC",
    "blocksMovement": true,
    "isPeaceful":     true,
    "hasDialogue":    true,
    "dialogPool": {
      "after_quest_completed:quest_boar_cull": [
        "The food stores are intact. Good work.",
        "Stay out of trouble — or don't, I suppose that's the point."
      ],
      "default": [
        "Halt! ...Actually, go ahead.",
        "We are stretched thin. Half the guard went down and didn't come back.",
        "Boars have been breaking into the food stores at the north edge of town. Three of them.",
        "If you deal with that, I'll make it worth your while."
      ]
    },
    "dialogOptions": [
      { "id": "boar_cull", "label": "I'll handle the boars.", "action": "give_quest:quest_boar_cull" },
      { "id": "farewell",  "label": "Farewell.",              "action": "close" }
    ]
  },
```

- [ ] **Step 3: Add Elder and Guard to the village NPC spawn pool in WorldGenerator**

In `src/Core/Generation/WorldGenerator.cs`, find the `GenerateVillageRegion` method and update the `traderPool` line:

```csharp
// BEFORE
string[] traderPool = ["npc_herbalist", "npc_trader"];
```

```csharp
// AFTER
string[] traderPool = ["npc_herbalist", "npc_trader", "npc_elder", "npc_guard"];
```

This places Elder Maren in the 3rd building and Guard Ollen in the 4th building; all remaining buildings still receive generic villagers.

- [ ] **Step 4: Build and run tests**

```bash
dotnet build && dotnet test tests/MonoRogue.Tests/
```

Expected: all tests pass.

- [ ] **Step 5: Commit**

```bash
git add Content/Data/blueprints/blueprints_npcs.json src/Core/Generation/WorldGenerator.cs
git commit -m "feat: add Elder Maren and Guard Ollen to village with quest-giving dialogue"
```

---

## Task 9: Data integrity tests for new content

**Files:**
- Modify: `tests/MonoRogue.Tests/QuestSystemTests.cs`

- [ ] **Step 1: Add data integrity assertions for the new quests and NPCs**

Add to the `QuestDataIntegrityTests` class in `tests/MonoRogue.Tests/QuestSystemTests.cs`:

```csharp
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
```

- [ ] **Step 2: Run new integrity tests**

```bash
dotnet test tests/MonoRogue.Tests/ --filter "QuestDataIntegrityTests"
```

Expected: all tests pass.

- [ ] **Step 3: Run full test suite**

```bash
dotnet test tests/MonoRogue.Tests/
```

Expected: all tests pass.

- [ ] **Step 4: Commit**

```bash
git add tests/MonoRogue.Tests/QuestSystemTests.cs
git commit -m "test: add data integrity tests for biome content expansion"
```

---

## Verification Checklist

After all tasks are complete, verify the following in-game:

- [ ] Start a new game. Walk north into Darkwood Forest — Old Gareth appears in a clearing.
- [ ] Talk to Old Gareth. Select "I'll handle the boars." — message log shows `Quest accepted: "Boar Problem".`
- [ ] Kill 4 boars. Kill-count messages appear. Quest completes and 35 gold is awarded.
- [ ] Talk to Old Gareth again — post-completion dialogue appears instead of quest offer.
- [ ] Walk east into Crystal Caverns — Yenna the Prospector is present and has a shop.
- [ ] Walk south into Ancient Ruins — Brother Aldric is present with scrolls for sale.
- [ ] Walk west into Fetid Swamp — Willa the Exile is present with herbs for sale.
- [ ] Talk to Elder Maren in the village — "I'll scout the caverns." option is present.
- [ ] Talk to Guard Ollen — "I'll handle the boars." option is present.
- [ ] Accepting a quest twice (re-talk to NPC) does not add a duplicate quest.
