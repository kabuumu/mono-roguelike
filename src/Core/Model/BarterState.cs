namespace MonoRogue.Core.Model;

/// <summary>
/// Shell-managed UI state for the barter screen.
/// Null on GameState = barter is closed.
/// </summary>
public sealed record BarterState(
    Guid   NpcId,
    string NpcName,
    int    SelectedIndex = 0
);
