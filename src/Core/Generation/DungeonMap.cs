namespace MonoRogue.Core.Generation;

using System.Collections.Immutable;
using MonoRogue.Core.Model;

/// <summary>
/// Axis-aligned rectangle used for BSP room bounds.
/// A value type — no heap allocation.
/// </summary>
public readonly record struct RoomRect(int X, int Y, int Width, int Height)
{
    public int Right  => X + Width;
    public int Bottom => Y + Height;
    public Position Center => new(X + Width / 2, Y + Height / 2);

    /// <summary>Checks whether a position is inside the room (inclusive walls).</summary>
    public bool Contains(int x, int y) =>
        x >= X && x < Right && y >= Y && y < Bottom;
}

/// <summary>
/// The result of one dungeon generation pass.
/// Immutable — produced by <see cref="BspGenerator"/> and consumed by the Shell
/// to build the initial <see cref="MonoRogue.Core.Model.GameState"/> for a floor.
/// </summary>
public sealed record DungeonMap(
    int Width,
    int Height,
    /// <summary>
    /// Flat row-major tile grid. true = walkable floor, false = solid wall.
    /// Index = Y * Width + X.
    /// </summary>
    ImmutableArray<bool> Tiles,
    ImmutableArray<RoomRect> Rooms,
    Position PlayerSpawn,
    Position ExitPosition,
    int FloorLevel
)
{
    public bool IsFloor(int x, int y) =>
        x >= 0 && x < Width && y >= 0 && y < Height && Tiles[y * Width + x];

    public bool IsFloor(Position p) => IsFloor(p.X, p.Y);
}
