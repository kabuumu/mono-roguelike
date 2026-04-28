namespace MonoRogue.Tests;

using System.Collections.Immutable;
using MonoRogue.Core.Events;
using MonoRogue.Core.Intents;
using MonoRogue.Core.Model;
using MonoRogue.Core.Systems;
using Xunit;

using static TestHelpers;

/// <summary>
/// Tests for DialogueSystem — TalkIntent scanning, background key selection,
/// advance/close guards. No file I/O, no DataRegistry needed.
/// </summary>
public sealed class DialogueSystemTests
{
    // ── Talk (open dialogue) ──────────────────────────────────────────────────

    [Fact]
    public void Talk_adjacent_npc_opens_dialogue()
    {
        var player = MakePlayer(new Position(4, 5));
        var npc    = MakeNpc(new Position(5, 5)); // East neighbour
        var state  = MakeState(20, 20, player, npc);

        var events = DialogueSystem.ProcessTalk(
            state,
            ImmutableArray.Create(new TalkIntent(player.Id)));

        Assert.Contains(events, e => e is DialogueOpenedEvent);
    }

    [Fact]
    public void Talk_with_no_adjacent_npc_returns_empty()
    {
        var player = MakePlayer(new Position(5, 5));
        // No NPC in the state
        var state  = MakeState(20, 20, player);

        var events = DialogueSystem.ProcessTalk(
            state,
            ImmutableArray.Create(new TalkIntent(player.Id)));

        Assert.Empty(events);
    }

    [Fact]
    public void Talk_npc_on_diagonal_is_found()
    {
        var player = MakePlayer(new Position(4, 4));
        var npc    = MakeNpc(new Position(5, 5)); // South-East diagonal
        var state  = MakeState(20, 20, player, npc);

        var events = DialogueSystem.ProcessTalk(
            state,
            ImmutableArray.Create(new TalkIntent(player.Id)));

        Assert.Contains(events, e => e is DialogueOpenedEvent);
    }

    // ── Background-specific dialogue selection ────────────────────────────────

    [Fact]
    public void Talk_picks_background_specific_lines_over_default()
    {
        var player = MakePlayer(new Position(4, 5)) with
        {
            Background = new BackgroundComponent("blacksmith_child")
        };
        var npc = MakeNpc(new Position(5, 5), pool: new()
        {
            ["requires_background:blacksmith_child"] = ["Special line!"],
            ["default"]                              = ["Generic line."],
        });
        var state = MakeState(20, 20, player, npc);

        var events = DialogueSystem.ProcessTalk(
            state,
            ImmutableArray.Create(new TalkIntent(player.Id)));

        var opened = events.OfType<DialogueOpenedEvent>().First();
        Assert.Contains("Special line!", opened.Lines);
        Assert.DoesNotContain("Generic line.", opened.Lines);
    }

    [Fact]
    public void Talk_falls_back_to_default_pool_when_no_background_match()
    {
        var player = MakePlayer(new Position(4, 5)) with
        {
            Background = new BackgroundComponent("unknown_bg")
        };
        var npc = MakeNpc(new Position(5, 5), pool: new()
        {
            ["requires_background:blacksmith_child"] = ["Blacksmith only."],
            ["default"]                              = ["Hello traveller!"],
        });
        var state = MakeState(20, 20, player, npc);

        var events = DialogueSystem.ProcessTalk(
            state,
            ImmutableArray.Create(new TalkIntent(player.Id)));

        var opened = events.OfType<DialogueOpenedEvent>().First();
        Assert.Contains("Hello traveller!", opened.Lines);
    }

    [Fact]
    public void Talk_falls_back_to_ellipsis_when_pool_empty()
    {
        var player = MakePlayer(new Position(4, 5));
        // NPC with completely empty pool
        var npc = MakeNpc(new Position(5, 5), pool: new Dictionary<string, string[]>());
        var state = MakeState(20, 20, player, npc);

        var events = DialogueSystem.ProcessTalk(
            state,
            ImmutableArray.Create(new TalkIntent(player.Id)));

        var opened = events.OfType<DialogueOpenedEvent>().First();
        Assert.Equal("...", opened.Lines[0]);
    }

    [Fact]
    public void Talk_sets_npc_name_in_opened_event()
    {
        var player = MakePlayer(new Position(4, 5));
        var npc    = MakeNpc(new Position(5, 5));   // Identity.Name = "Villager"
        var state  = MakeState(20, 20, player, npc);

        var events = DialogueSystem.ProcessTalk(
            state,
            ImmutableArray.Create(new TalkIntent(player.Id)));

        var opened = events.OfType<DialogueOpenedEvent>().First();
        Assert.Equal("Villager", opened.NpcName);
    }

    // ── Quest-completion-conditional dialogue ─────────────────────────────────

    [Fact]
    public void Talk_picks_quest_completed_lines_when_quest_is_done()
    {
        var player = MakePlayer(new Position(4, 5));
        var npc = MakeNpc(new Position(5, 5), pool: new()
        {
            ["after_quest_completed:quest_fetch_water"] = ["Thanks for the water!"],
            ["default"]                                 = ["Hello."],
        });
        var state = MakeState(20, 20, player, npc) with
        {
            CompletedQuestIds = ImmutableHashSet.Create("quest_fetch_water")
        };

        var events = DialogueSystem.ProcessTalk(
            state, ImmutableArray.Create(new TalkIntent(player.Id)));

        var opened = events.OfType<DialogueOpenedEvent>().First();
        Assert.Contains("Thanks for the water!", opened.Lines);
        Assert.DoesNotContain("Hello.", opened.Lines);
    }

    [Fact]
    public void Talk_falls_back_to_default_when_quest_not_yet_completed()
    {
        var player = MakePlayer(new Position(4, 5));
        var npc = MakeNpc(new Position(5, 5), pool: new()
        {
            ["after_quest_completed:quest_fetch_water"] = ["Thanks for the water!"],
            ["default"]                                 = ["Bring me water."],
        });
        var state = MakeState(20, 20, player, npc) with
        {
            CompletedQuestIds = ImmutableHashSet<string>.Empty
        };

        var events = DialogueSystem.ProcessTalk(
            state, ImmutableArray.Create(new TalkIntent(player.Id)));

        var opened = events.OfType<DialogueOpenedEvent>().First();
        Assert.Contains("Bring me water.", opened.Lines);
    }

    [Fact]
    public void Talk_uses_highest_priority_quest_key_in_json_order()
    {
        // Both quests complete — chop_wood key appears first in the pool,
        // so it should win even though fetch_water is also done.
        var player = MakePlayer(new Position(4, 5));
        var npc = MakeNpc(new Position(5, 5), pool: new()
        {
            ["after_quest_completed:quest_chop_wood"]   = ["Forge is ready!"],
            ["after_quest_completed:quest_fetch_water"] = ["Water received."],
            ["default"]                                 = ["Hello."],
        });
        var state = MakeState(20, 20, player, npc) with
        {
            CompletedQuestIds = ImmutableHashSet.Create("quest_chop_wood", "quest_fetch_water")
        };

        var events = DialogueSystem.ProcessTalk(
            state, ImmutableArray.Create(new TalkIntent(player.Id)));

        var opened = events.OfType<DialogueOpenedEvent>().First();
        Assert.Contains("Forge is ready!", opened.Lines);
    }

    [Fact]
    public void Talk_quest_key_does_not_match_incomplete_quest()
    {
        var player = MakePlayer(new Position(4, 5));
        var npc = MakeNpc(new Position(5, 5), pool: new()
        {
            ["after_quest_completed:quest_chop_wood"] = ["Forge is ready!"],
            ["default"]                               = ["Chop some wood."],
        });
        // quest_chop_wood is NOT in CompletedQuestIds
        var state = MakeState(20, 20, player, npc) with
        {
            CompletedQuestIds = ImmutableHashSet<string>.Empty
        };

        var events = DialogueSystem.ProcessTalk(
            state, ImmutableArray.Create(new TalkIntent(player.Id)));

        var opened = events.OfType<DialogueOpenedEvent>().First();
        Assert.Contains("Chop some wood.", opened.Lines);
    }

    // ── Advance ───────────────────────────────────────────────────────────────

    [Fact]
    public void Advance_emits_dialogue_advanced_event()
    {
        var state = MakeState(20, 20, MakePlayer(new Position(1, 1))) with
        {
            ActiveDialogue = new DialogueState(Guid.NewGuid(), "Bob",
                ImmutableArray.Create("Line 1", "Line 2"), CurrentLine: 0)
        };

        var events = DialogueSystem.ProcessAdvance(
            state,
            ImmutableArray.Create(new AdvanceDialogueIntent()));

        Assert.Contains(events, e => e is DialogueAdvancedEvent);
    }

    [Fact]
    public void Advance_with_no_active_dialogue_returns_empty()
    {
        var state = MakeState(20, 20, MakePlayer(new Position(1, 1)));
        // ActiveDialogue is null by default

        var events = DialogueSystem.ProcessAdvance(
            state,
            ImmutableArray.Create(new AdvanceDialogueIntent()));

        Assert.Empty(events);
    }

    // ── Close ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Close_emits_dialogue_closed_event()
    {
        var state = MakeState(20, 20, MakePlayer(new Position(1, 1))) with
        {
            ActiveDialogue = new DialogueState(Guid.NewGuid(), "Bob",
                ImmutableArray.Create("Bye!"), CurrentLine: 0)
        };

        var events = DialogueSystem.ProcessClose(
            state,
            ImmutableArray.Create(new CloseDialogueIntent()));

        Assert.Contains(events, e => e is DialogueClosedEvent);
    }

    [Fact]
    public void Close_with_no_active_dialogue_returns_empty()
    {
        var state = MakeState(20, 20, MakePlayer(new Position(1, 1)));

        var events = DialogueSystem.ProcessClose(
            state,
            ImmutableArray.Create(new CloseDialogueIntent()));

        Assert.Empty(events);
    }
}
