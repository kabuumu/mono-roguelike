namespace MonoRogue.Core.Systems;

using System.Collections.Immutable;
using MonoRogue.Core.Model;

/// <summary>
/// Bresenham ray-casting field-of-view.
///
/// Strategy: cast one ray from the origin to every integer point on the
/// perimeter of the bounding square (radius × radius).  Walking each ray
/// with Bresenham's line algorithm guarantees no skipped cells.  A final
/// Euclidean-distance filter clips the square into a true circle.
///
/// Properties:
///   • Origin is always visible.
///   • Opaque tiles (walls) ARE visible — they block the tiles behind them.
///   • Result is symmetric: rotating the open map 90° produces the same set.
///   • Pure function — no side effects.
/// </summary>
public static class FovSystem
{
    /// <summary>
    /// Returns all positions visible from <paramref name="origin"/> within
    /// <paramref name="radius"/> tiles (Euclidean distance).
    /// </summary>
    public static ImmutableHashSet<Position> Compute(
        GameState state,
        Position  origin,
        int       radius)
    {
        var visible = ImmutableHashSet.CreateBuilder<Position>();
        visible.Add(origin);

        // Cast rays to every integer point on the perimeter of the bounding square.
        // Using i from -r to r covers all four edges; corner points are hit twice
        // but that is harmless — correctness over micro-optimisation.
        int r = radius;
        for (int i = -r; i <= r; i++)
        {
            CastRay(state, visible, origin,  i, -r, r);   // top edge
            CastRay(state, visible, origin,  i,  r, r);   // bottom edge
            CastRay(state, visible, origin, -r,  i, r);   // left edge
            CastRay(state, visible, origin,  r,  i, r);   // right edge
        }

        return visible.ToImmutable();
    }

    // ── Bresenham ray ─────────────────────────────────────────────────────────

    /// <summary>
    /// Walks a Bresenham line from <paramref name="origin"/> toward the point
    /// <c>(origin + (dx, dy))</c>, adding each cell to <paramref name="visible"/>
    /// until it leaves bounds, passes <paramref name="radius"/>, or hits an
    /// opaque tile (which is itself added before stopping).
    /// </summary>
    private static void CastRay(
        GameState                         state,
        ImmutableHashSet<Position>.Builder visible,
        Position                          origin,
        int                               dx,
        int                               dy,
        int                               radius)
    {
        if (dx == 0 && dy == 0) return;

        int x = origin.X, y = origin.Y;
        int targetX = origin.X + dx, targetY = origin.Y + dy;

        // Standard Bresenham setup.
        // absDy is stored negative (dy_neg = -|dy|) so the two error-advance
        // conditions read naturally as e2 >= dy_neg and e2 <= absDx.
        int absDx  = Math.Abs(dx), sx = dx > 0 ? 1 : -1;
        int dy_neg = -Math.Abs(dy), sy = dy > 0 ? 1 : -1;
        int err = absDx + dy_neg;
        int r2  = radius * radius;

        while (true)
        {
            // Advance to next cell (origin already added outside this method).
            int e2 = 2 * err;
            if (e2 >= dy_neg) { err += dy_neg; x += sx; }
            if (e2 <= absDx)  { err += absDx;  y += sy; }

            var pos = new Position(x, y);

            if (!state.InBounds(pos)) break;

            // Euclidean clip: only include cells within the circular radius.
            int ox = x - origin.X, oy = y - origin.Y;
            if (ox * ox + oy * oy <= r2)
                visible.Add(pos);

            // Opaque tile: visible (wall shows) but blocks everything behind it.
            if (state.IsOpaque(pos)) break;

            // Reached the perimeter target — stop advancing.
            if (x == targetX && y == targetY) break;
        }
    }
}
