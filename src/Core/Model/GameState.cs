namespace MonoRogue.Core.Model;

using System.Collections.Immutable;

/// <summary>
/// The complete, immutable snapshot of game state at a single turn.
/// All changes produce a new <see cref="GameState"/> via
/// <c>with</c> expressions — the previous snapshot is never modified.
/// </summary>
public sealed record GameState(
    ImmutableDictionary<Guid, Entity> Entities,
    int MapWidth,
    int MapHeight,
    /// <summary>
    /// Flat row-major array: index = Y * MapWidth + X.
    /// true = tile is walkable.
    /// </summary>
    ImmutableArray<bool> WalkabilityMap,
    /// <summary>Cached player entity ID to avoid a linear scan each tick.</summary>
    Guid PlayerEntityId,
    int TurnNumber,
    /// <summary>Log of recent messages emitted this session (newest last).</summary>
    ImmutableList<string> MessageLog,
    /// <summary>Tiles the player can currently see (recomputed each turn).</summary>
    ImmutableHashSet<Position> VisibleTiles,
    /// <summary>All tiles the player has ever seen (persists across turns).</summary>
    ImmutableHashSet<Position> ExploredTiles,
    /// <summary>Current dungeon floor (1-based).</summary>
    int FloorLevel,
    /// <summary>Current game phase (playing, dead, victory).</summary>
    GamePhase Phase,
    /// <summary>Active NPC conversation, or null when not talking.</summary>
    DialogueState? ActiveDialogue = null,
    /// <summary>Currently active quests.</summary>
    ImmutableList<Quest>? ActiveQuests = null,
    /// <summary>IDs of completed quests.</summary>
    ImmutableHashSet<string>? CompletedQuestIds = null,
    /// <summary>Is the inventory screen open?</summary>
    bool IsInventoryOpen = false,
    /// <summary>Selected row index in the open inventory panel (0 = first item).</summary>
    int InventorySelectedIndex = 0,
    /// <summary>
    /// When non-null, the interaction choice menu is open.
    /// Each entry is an adjacent interactable entity the player can choose.
    /// </summary>
    ImmutableList<InteractionChoice>? InteractionTargets = null,
    /// <summary>Selected row index in the interaction choice menu.</summary>
    int InteractionTargetIndex = 0,
    /// <summary>Non-null when the barter screen is open.</summary>
    BarterState? ActiveBarter = null,
    /// <summary>Is the character screen open?</summary>
    bool IsCharacterScreenOpen = false,
    /// <summary>Display name of the current area shown in the HUD.</summary>
    string LocationName = "Unknown Lands"
)
{
    // ── Factory ────────────────────────────────────────────────────────────
    public static GameState Empty(int width, int height) => new(
        Entities:       ImmutableDictionary<Guid, Entity>.Empty,
        MapWidth:       width,
        MapHeight:      height,
        WalkabilityMap: [.. Enumerable.Repeat(true, width * height)],
        PlayerEntityId: Guid.Empty,
        TurnNumber:     0,
        MessageLog:     ImmutableList<string>.Empty,
        VisibleTiles:   ImmutableHashSet<Position>.Empty,
        ExploredTiles:  ImmutableHashSet<Position>.Empty,
        FloorLevel:      1,
        Phase:           new PlayingPhase(),
        ActiveDialogue:  null,
        ActiveQuests:    ImmutableList<Quest>.Empty,
        CompletedQuestIds: ImmutableHashSet<string>.Empty
    );

    // ── Spatial queries ─────────────────────────────────────────────────────
    public bool InBounds(Position p) =>
        p.X >= 0 && p.X < MapWidth && p.Y >= 0 && p.Y < MapHeight;

    public bool IsWalkable(Position p) =>
        InBounds(p) && WalkabilityMap[p.Y * MapWidth + p.X];

    /// <summary>True if the position blocks line-of-sight (wall, out of bounds).</summary>
    public bool IsOpaque(Position p) =>
        !InBounds(p) || !WalkabilityMap[p.Y * MapWidth + p.X];

    public bool IsOccupied(Position p) =>
        Entities.Values.Any(e => e.Spatial?.Position == p && e.BlocksMovement());

    // ── Entity helpers ──────────────────────────────────────────────────────
    public Entity? TryGetPlayer() =>
        PlayerEntityId != Guid.Empty && Entities.TryGetValue(PlayerEntityId, out var e)
            ? e : null;

    public IEnumerable<Entity> EntitiesAt(Position p) =>
        Entities.Values.Where(e => e.Spatial?.Position == p);

    // ── WalkabilityMap helpers ──────────────────────────────────────────────
    public GameState WithTileBlocked(Position p) =>
        InBounds(p)
            ? this with { WalkabilityMap = WalkabilityMap.SetItem(p.Y * MapWidth + p.X, false) }
            : this;

    public GameState WithTileOpen(Position p) =>
        InBounds(p)
            ? this with { WalkabilityMap = WalkabilityMap.SetItem(p.Y * MapWidth + p.X, true) }
            : this;

    // ── Message log ─────────────────────────────────────────────────────────
    public GameState AppendMessage(string msg) =>
        this with { MessageLog = MessageLog.Add(msg) };

    // ── Phase helpers ────────────────────────────────────────────────────────
    public bool IsMainMenu   => Phase is MainMenuPhase;
    public bool IsPlaying    => Phase is PlayingPhase;
    public bool IsPlayerDead => Phase is PlayerDeadPhase;
    public bool IsVictory    => Phase is VictoryPhase;

    // ── Dialogue helper ──────────────────────────────────────────────────────
    public bool InDialogue => ActiveDialogue is not null;

    // ── Interaction menu helper ──────────────────────────────────────────────
    public bool IsInteractionMenuOpen => InteractionTargets is not null;
    public bool IsBarterOpen          => ActiveBarter       is not null;
}

/// <summary>One option shown in the "Interact With" selection — carries world position for map highlighting.</summary>
public sealed record InteractionChoice(Guid EntityId, string Label, Position WorldPosition);
