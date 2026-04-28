namespace MonoRogue.Core.Model;

/// <summary>
/// Immutable 2D grid coordinate. A value type — no heap allocation.
/// Arithmetic operators allow expressing direction + position cleanly.
/// </summary>
public readonly record struct Position(int X, int Y)
{
    public static Position Zero    => new(0, 0);
    public static Position North   => new(0, -1);
    public static Position South   => new(0,  1);
    public static Position West    => new(-1, 0);
    public static Position East    => new( 1, 0);

    public static Position operator +(Position a, Position b) => new(a.X + b.X, a.Y + b.Y);
    public static Position operator -(Position a, Position b) => new(a.X - b.X, a.Y - b.Y);

    public override string ToString() => $"({X},{Y})";
}
