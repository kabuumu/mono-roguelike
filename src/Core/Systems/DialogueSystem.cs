namespace MonoRogue.Core.Systems;

using System.Collections.Immutable;
using MonoRogue.Core.Events;
using MonoRogue.Core.Intents;
using MonoRogue.Core.Model;
using System.Linq;

/// <summary>
/// Pure Phase-2 validation for dialogue intents.
///
/// TalkIntent    → scans all 8 neighbours for an NPC with a DialogueComponent.
/// AdvanceDialogue → steps the current dialogue forward or closes it.
/// CloseDialogue  → closes the current dialogue unconditionally.
/// </summary>
public static class DialogueSystem
{
    private static ImmutableArray<string> GetDialogueLines(GameState state, DialogueComponent dialogue)
    {
        var player          = state.Entities.Values.FirstOrDefault(e => e.Player != null);
        var backgroundId    = player?.Background?.BackgroundId;
        var completedQuests = state.CompletedQuestIds;

        // Walk keys in their original JSON order — first match wins.
        // Supported prefixes:
        //   after_quest_completed:{questId}    — player has that quest in CompletedQuestIds
        //   requires_background:{backgroundId} — player has that background
        //   default                            — unconditional fallback
        foreach (var key in dialogue.KeyOrder)
        {
            if (key.StartsWith("after_quest_completed:"))
            {
                var questId = key["after_quest_completed:".Length..];
                if (completedQuests?.Contains(questId) == true &&
                    dialogue.DialogPool.TryGetValue(key, out var questLines))
                    return questLines;
            }
            else if (key.StartsWith("requires_background:"))
            {
                var bgId = key["requires_background:".Length..];
                if (bgId == backgroundId &&
                    dialogue.DialogPool.TryGetValue(key, out var bgLines))
                    return bgLines;
            }
            else if (key == "default" && dialogue.DialogPool.TryGetValue("default", out var defaultLines))
            {
                return defaultLines;
            }
        }

        return ImmutableArray.Create("...");
    }
    private static readonly Position[] AllNeighbours =
    [
        Position.North, Position.South, Position.East, Position.West,
        Position.North + Position.East, Position.North + Position.West,
        Position.South + Position.East, Position.South + Position.West,
    ];

    // ── Talk (open) ───────────────────────────────────────────────────────────

    public static ImmutableArray<GameEvent> ProcessTalk(
        GameState               state,
        ImmutableArray<TalkIntent> intents)
    {
        if (intents.IsEmpty) return [];

        foreach (var intent in intents)
        {
            if (!state.Entities.TryGetValue(intent.EntityId, out var talker)) continue;
            if (talker.Spatial is null) continue;

            foreach (var dir in AllNeighbours)
            {
                var npc = state.EntitiesAt(talker.Spatial.Position + dir)
                               .FirstOrDefault(e => e.IsNpc());
                if (npc?.Dialogue is null) continue;

                var lines = GetDialogueLines(state, npc.Dialogue);
                return
                [
                    new DialogueOpenedEvent(
                        npc.Id,
                        npc.Identity?.Name ?? "Stranger",
                        lines)
                ];
            }
        }

        return [];
    }

    // ── Open from bump (NpcBumpedEvent produced by MovementSystem) ───────────

    public static ImmutableArray<GameEvent> ProcessBumps(
        GameState                        state,
        ImmutableArray<GameEvent>        frameEvents)
    {
        var result = ImmutableArray.CreateBuilder<GameEvent>();
        foreach (var evt in frameEvents.OfType<NpcBumpedEvent>())
        {
            if (!state.Entities.TryGetValue(evt.NpcId, out var npc)) continue;
            if (npc.Dialogue is null) continue;
            var lines = GetDialogueLines(state, npc.Dialogue);
            result.Add(new DialogueOpenedEvent(npc.Id, npc.Identity?.Name ?? "Stranger", lines));
        }
        return result.ToImmutable();
    }

    // ── Advance / close ───────────────────────────────────────────────────────

    public static ImmutableArray<GameEvent> ProcessAdvance(
        GameState                          state,
        ImmutableArray<AdvanceDialogueIntent> intents)
    {
        if (intents.IsEmpty || state.ActiveDialogue is null) return [];
        // DialogueState.Advance() returns null when past the last line
        return [new DialogueAdvancedEvent()];
    }

    public static ImmutableArray<GameEvent> ProcessClose(
        GameState                        state,
        ImmutableArray<CloseDialogueIntent> intents)
    {
        if (intents.IsEmpty || state.ActiveDialogue is null) return [];
        return [new DialogueClosedEvent()];
    }
}
