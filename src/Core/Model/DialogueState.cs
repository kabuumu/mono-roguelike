namespace MonoRogue.Core.Model;

using System.Collections.Immutable;

/// <summary>
/// Snapshot of an in-progress NPC conversation.
/// ShowingOptions is derived: true whenever the player is on the last line
/// and the NPC has dialogue choices — no separate mode transition needed.
/// </summary>
public sealed record DialogueState(
    Guid                           NpcId,
    string                         NpcName,
    ImmutableArray<string>         Lines,
    int                            CurrentLine    = 0,
    ImmutableArray<DialogueOption> Options        = default,
    int                            SelectedOption = 0
)
{
    public bool HasOptions    => !Options.IsDefaultOrEmpty;
    public bool IsLastLine    => CurrentLine >= Lines.Length - 1;
    public bool ShowingOptions => IsLastLine && HasOptions;
    public string CurrentText =>
        Lines.IsEmpty ? "" : Lines[Math.Clamp(CurrentLine, 0, Lines.Length - 1)];

    /// <summary>
    /// Advances the dialogue state machine:
    ///   Not last line          → next line
    ///   Last line, options     → no-op (shell navigates/selects)
    ///   Last line, no options  → null (close)
    /// </summary>
    public DialogueState? Advance()
    {
        if (ShowingOptions) return this;
        if (IsLastLine) return null;
        return this with { CurrentLine = CurrentLine + 1 };
    }
}
