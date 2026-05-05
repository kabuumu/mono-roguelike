# Biome Content Expansion — Design

**Date:** 2026-05-05

## Problem

The world map has four biomes (forest, cave, ruins, swamp) reachable without loading screens, but they contain nothing beyond enemy spawns. There is nobody to talk to outside the village, no reasons to explore any particular area, and only 3 quests (all tied to one background). The game lacks a fetch-and-return loop that gives the player purpose in the open world.

## Solution

Add one NPC to each biome, give each a quest, add dialogue options to two existing village NPCs, and introduce 8 new kill-objective quests. One small code addition (`give_quest:` dialogue action) unlocks the mechanic. Everything else is JSON data.

---

## Characters

### Biome NPCs (new)

| Template ID | Name | Biome | Personality |
|---|---|---|---|
| `npc_hermit` | Old Gareth | Darkwood Forest (north) | Reclusive woodsman, decades alone in the forest, camp harassed by boars |
| `npc_prospector` | Yenna the Prospector | Crystal Caverns (east) | Miner driven back by goblins, sitting on a stash of finds, wants her dig site back |
| `npc_scholar` | Brother Aldric | Ancient Ruins (south) | Academic studying the ruins, cautious around orcs, sells recovered artefacts |
| `npc_exile` | Willa the Exile | Fetid Swamp (west) | Banished from Millhaven long ago, bitter but knowledgeable, sells swamp herbs |

All four are peaceful, have dialogue, and offer a quest via a `give_quest:` dialogue option. Yenna, Aldric, and Willa also have shops.

### Village NPCs (extended)

**Elder Maren** — gains `dialogOptions` so she can send the player to scout the cave.
**Guard Ollen** — gains `dialogOptions` so he can give a boar-culling bounty.

Both existing NPCs are converted from `dialogueLines` to `dialogPool` format (existing lines become the `default` key) and gain a `dialogOptions` array with a `give_quest:` option and a `farewell` option.

---

## Quests

All quests use `kill_target` objectives — no new quest-system code required.

### Biome NPC quests

| Quest ID | Giver | Objective | Reward |
|---|---|---|---|
| `quest_cull_boars` | Old Gareth | Kill 4 `creature_boar` | 35 gold |
| `quest_clear_caverns` | Yenna | Kill 5 `creature_goblin` | 40 gold + `item_chainmail` |
| `quest_orc_cleansing` | Brother Aldric | Kill 4 `creature_orc` | 60 gold |
| `quest_swamp_trolls` | Willa | Kill 3 `creature_troll` | 50 gold + `item_health_potion` |

### Village NPC quests

| Quest ID | Giver | Objective | Reward |
|---|---|---|---|
| `quest_cave_scout` | Elder Maren | Kill 3 `creature_rat` (proof of entry) | 20 gold + `item_health_potion` |
| `quest_boar_cull` | Guard Ollen | Kill 3 `creature_boar` | 25 gold |

Quest giving is guarded: if the quest is already in `ActiveQuests` or `CompletedQuestIds`, the `give_quest:` action silently does nothing (no duplicate quests).

After a quest is completed, the NPC shows distinct dialogue via the existing `after_quest_completed:<questId>` pool key.

---

## New Items

Two new consumable items for the biome shops:

| Item ID | Name | Heals | Value | Sold by |
|---|---|---|---|---|
| `item_ancient_scroll` | Ancient Scroll | 15 HP | 20g | Brother Aldric |
| `item_swamp_herb` | Swamp Herb | 12 HP | 8g | Willa the Exile |

Yenna's shop stocks existing items (`item_chainmail`, `item_health_potion`). No new items needed for her.

---

## Code Change

### `MonoRogueGame.ExecuteOption` (src/Shell/MonoRogueGame.cs)

Add one case to the existing `switch (option.Action)` block:

```csharp
case string s when s.StartsWith("give_quest:"):
    var questId = s["give_quest:".Length..];
    var alreadyActive    = _state.ActiveQuests?.Any(q => q.Id == questId) ?? false;
    var alreadyCompleted = _state.CompletedQuestIds?.Contains(questId) ?? false;
    if (!alreadyActive && !alreadyCompleted && _registry.Quests.TryGetValue(questId, out var qTpl))
    {
        // Build Quest record from template (same logic as WorldGenerator)
        var quest = BuildQuest(qTpl);
        _state = _state with { ActiveQuests = (_state.ActiveQuests ?? []).Add(quest) };
        _state = _state.AppendMessage($"Quest accepted: \"{quest.Name}\".");
    }
    break;
```

A private `BuildQuest(QuestTemplate)` helper is extracted (reuses the same logic already in `WorldGenerator`).

---

## Data Files Changed

| File | Change |
|---|---|
| `Content/Data/blueprints/blueprints_npcs.json` | Add 4 biome NPC templates; convert Elder + Guard to `dialogPool` + add `dialogOptions` |
| `Content/Data/blueprints/blueprints_items.json` | Add `item_ancient_scroll`, `item_swamp_herb` |
| `Content/Data/areas/areas.json` | Add `npcs` entries to forest, cave, swamp (ruins already has `npc_trader`) |
| `Content/Data/quests/quest_cull_boars.json` | New |
| `Content/Data/quests/quest_clear_caverns.json` | New |
| `Content/Data/quests/quest_orc_cleansing.json` | New |
| `Content/Data/quests/quest_swamp_trolls.json` | New |
| `Content/Data/quests/quest_cave_scout.json` | New |
| `Content/Data/quests/quest_boar_cull.json` | New |
| `src/Shell/MonoRogueGame.cs` | Add `give_quest:` case to `ExecuteOption`; extract `BuildQuest` helper |

---

## What This Does Not Include

- NPC-to-NPC quest chains (e.g. "bring something from the hermit to the elder")
- Dialogue that changes while a quest is in progress (only default and post-completion states)
- New area types or map changes
- Save/load, day-night cycle, or skill system
