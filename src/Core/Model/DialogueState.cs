namespace MonoRogue.Core.Model;

using System.Collections.Immutable;

/// <summary>
/// Snapshot of an in-progress conversation.
/// Stored directly on <see cref="GameState"/> so the renderer and input
/// handler can both inspect it without sharing mutable state.
///
/// null = no conversation active.
/// </summary>
public sealed record DialogueState(
    Guid                   NpcId,
    string                 NpcName,
    ImmutableArray<string> Lines,
    int                    CurrentLine = 0
)
{
    public string CurrentText =>
        Lines.IsEmpty ? "" : Lines[Math.Clamp(CurrentLine, 0, Lines.Length - 1)];

    public bool IsLastLine => CurrentLine >= Lines.Length - 1;

    /// <summary>Returns next state with line advanced, or null if conversation is over.</summary>
    public DialogueState? Advance() =>
        IsLastLine ? null : this with { CurrentLine = CurrentLine + 1 };
}
