namespace MonoRogue.Core.Model;

/// <summary>
/// Discriminated union representing the current phase of the game run.
/// </summary>
public abstract record GamePhase;

/// <summary>Game is at the main menu.</summary>
public sealed record MainMenuPhase : GamePhase;

/// <summary>Game is in progress — player is alive and acting.</summary>
public sealed record PlayingPhase : GamePhase;

/// <summary>The player died — run is over.</summary>
public sealed record PlayerDeadPhase(string Cause) : GamePhase;

/// <summary>Player reached the victory condition (e.g. reached floor 10).</summary>
public sealed record VictoryPhase : GamePhase;
