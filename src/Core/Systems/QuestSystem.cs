namespace MonoRogue.Core.Systems;

using System.Collections.Immutable;
using MonoRogue.Core.Events;
using MonoRogue.Core.Model;
using MonoRogue.Data;

/// <summary>
/// Event-listener system: runs AFTER all other validation systems have emitted
/// their events but BEFORE the Reducer, so quest progress and rewards enter the
/// same reduction pass as the kills that triggered them.
///
/// Scans frame events for <see cref="DiedEvent"/>s, cross-references active kill
/// quests, and emits <see cref="QuestProgressedEvent"/> and (when complete)
/// <see cref="QuestCompletedEvent"/> plus reward events.
/// </summary>
public static class QuestSystem
{
    public static ImmutableArray<GameEvent> ProcessEvents(
        GameState                 state,
        ImmutableArray<GameEvent> frameEvents,
        DataRegistry              registry)
    {
        if (state.ActiveQuests is null || state.ActiveQuests.Count == 0) return [];
        if (state.PlayerEntityId == Guid.Empty) return [];

        var output = ImmutableArray.CreateBuilder<GameEvent>();

        foreach (var evt in frameEvents)
        {
            if (evt is not DiedEvent died) continue;

            // Entity must still exist (pre-reduce state)
            if (!state.Entities.TryGetValue(died.EntityId, out var entity)) continue;
            var templateId = entity.Identity?.TemplateId ?? "";
            if (string.IsNullOrEmpty(templateId)) continue;

            foreach (var quest in state.ActiveQuests)
            {
                if (quest.Objectives is null) continue;

                foreach (var obj in quest.Objectives)
                {
                    if (obj.Type != "kill_target" || obj.TargetId != templateId) continue;

                    var currentCount = quest.Progress?.GetValueOrDefault(obj.TargetId, 0) ?? 0;
                    var newCount     = currentCount + 1;

                    output.Add(new QuestProgressedEvent(
                        state.PlayerEntityId, quest.Id, obj.TargetId, newCount));

                    if (newCount < obj.RequiredCount)
                    {
                        var label = System.Globalization.CultureInfo.CurrentCulture.TextInfo
                            .ToTitleCase(obj.TargetId.Replace("creature_", "").Replace("_", " "));
                        output.Add(new MessageLoggedEvent(
                            $"{label} killed ({newCount}/{obj.RequiredCount})."));
                    }

                    if (newCount >= obj.RequiredCount)
                    {
                        output.Add(new QuestCompletedEvent(state.PlayerEntityId, quest.Id));
                        output.Add(new MessageLoggedEvent($"Quest complete: \"{quest.Name}\"!"));

                        // Emit rewards from registry
                        if (registry.Quests.TryGetValue(quest.Id, out var questTpl))
                            output.AddRange(BuildRewardEvents(state, questTpl, registry));
                    }
                }
            }
        }

        return output.ToImmutable();
    }

    // ── Item-delivery turn-in ─────────────────────────────────────────────────

    /// <summary>
    /// Called when the player opens dialogue with <paramref name="npcTemplateId"/>.
    /// Checks every active item-delivery quest whose TurnInNpcId matches, verifies
    /// the actor has all RequiredItems in their resource stash, and — if so — emits
    /// consumption, completion, and reward events for each satisfied quest.
    /// </summary>
    public static ImmutableArray<GameEvent> CheckItemTurnIn(
        GameState    state,
        Entity       actor,
        string       npcTemplateId,
        DataRegistry registry)
    {
        if (state.ActiveQuests is null || state.ActiveQuests.Count == 0) return [];
        if (actor.Inventory is null) return [];

        var output    = ImmutableArray.CreateBuilder<GameEvent>();
        var resources = actor.Inventory.Resources ?? ImmutableDictionary<string, int>.Empty;

        foreach (var quest in state.ActiveQuests)
        {
            if (quest.TurnInNpcId != npcTemplateId) continue;
            if (quest.RequiredItems.Count == 0) continue;

            bool hasAll = true;
            foreach (var (itemId, needed) in quest.RequiredItems)
            {
                if (resources.GetValueOrDefault(itemId, 0) < needed) { hasAll = false; break; }
            }
            if (!hasAll) continue;

            foreach (var (itemId, count) in quest.RequiredItems)
                output.Add(new InventoryChangedEvent(actor.Id, itemId, -count));

            output.Add(new QuestCompletedEvent(actor.Id, quest.Id));
            output.Add(new MessageLoggedEvent(quest.CompletionText));
            output.Add(new MessageLoggedEvent($"Quest complete: \"{quest.Name}\"!"));

            if (registry.Quests.TryGetValue(quest.Id, out var questTpl))
                output.AddRange(BuildRewardEvents(state, questTpl, registry));
        }

        return output.ToImmutable();
    }

    // ── Reward helpers ────────────────────────────────────────────────────────

    private static ImmutableArray<GameEvent> BuildRewardEvents(
        GameState     state,
        QuestTemplate questTpl,
        DataRegistry  registry)
    {
        var rewards = ImmutableArray.CreateBuilder<GameEvent>();
        var playerId = state.PlayerEntityId;

        if (questTpl.RewardGold > 0)
        {
            rewards.Add(new GoldChangedEvent(playerId, questTpl.RewardGold));
            rewards.Add(new MessageLoggedEvent($"You receive {questTpl.RewardGold} gold."));
        }

        if (questTpl.RewardItems is null) return rewards.ToImmutable();

        foreach (var itemId in questTpl.RewardItems)
        {
            if (!registry.Templates.TryGetValue(itemId, out var itemTpl)) continue;

            // Spawn the item with no world position (Spatial = null → in-inventory)
            var rewardItem = SpawnSystem.BuildEntity(itemTpl, new Position(-1, -1))
                             with { Spatial = null };

            rewards.Add(new SpawnedEvent(rewardItem));
            rewards.Add(new ItemPickedUpEvent(playerId, rewardItem.Id));
            rewards.Add(new MessageLoggedEvent(
                $"You receive {rewardItem.Identity?.Name ?? itemId}."));
        }

        return rewards.ToImmutable();
    }
}
