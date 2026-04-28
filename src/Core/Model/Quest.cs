namespace MonoRogue.Core.Model;

using System.Collections.Immutable;

/// <summary>
/// A single objective within a quest (e.g. "kill 5 wolves").
/// </summary>
public sealed record QuestObjective(
    string Type,
    string TargetId,
    int    RequiredCount
);

/// <summary>
/// Represents an active quest in the player's quest log.
/// <see cref="Progress"/> tracks kill counts for kill_target objectives.
/// </summary>
public sealed record Quest(
    string Id,
    string Name,
    string Description,
    ImmutableDictionary<string, int> RequiredItems,
    string CompletionText,
    ImmutableArray<QuestObjective>?       Objectives  = null,
    ImmutableDictionary<string, int>?     Progress    = null,
    // NPC template ID the player must talk to in order to turn in item-delivery quests
    string?                               TurnInNpcId = null
);
