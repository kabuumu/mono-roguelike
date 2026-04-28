namespace MonoRogue.Tests;

using System.Collections.Immutable;
using MonoRogue.Core.Model;
using MonoRogue.Core.Systems;
using Xunit;

/// <summary>
/// Proves correctness of FovSystem.Compute (Bresenham ray-casting FOV).
///
/// Each test constructs a minimal GameState with no entities — just a
/// WalkabilityMap — so the tests are fast, pure, and require no GPU.
/// GameState.Empty() gives a fully walkable map; WithTileBlocked() places walls.
/// </summary>
public sealed class FovSystemTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static GameState OpenMap(int w, int h) =>
        GameState.Empty(w, h);

    private static GameState MapWithWalls(int w, int h, params Position[] walls)
    {
        var state = GameState.Empty(w, h);
        foreach (var wall in walls)
            state = state.WithTileBlocked(wall);
        return state;
    }

    private static bool InCircle(Position origin, Position p, int radius) =>
        (p.X - origin.X) * (p.X - origin.X) +
        (p.Y - origin.Y) * (p.Y - origin.Y) <= radius * radius;

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Origin_is_always_visible()
    {
        var origin = new Position(10, 10);
        var vis    = FovSystem.Compute(OpenMap(20, 20), origin, 5);
        Assert.Contains(origin, vis);
    }

    [Fact]
    public void Open_field_all_tiles_within_radius_are_visible()
    {
        // Every cell inside the Euclidean circle should appear in the visible set;
        // every cell outside should not.
        int w = 30, h = 30, r = 8;
        var origin = new Position(15, 15);
        var vis    = FovSystem.Compute(OpenMap(w, h), origin, r);

        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            var pos = new Position(x, y);
            Assert.Equal(InCircle(origin, pos, r), vis.Contains(pos));
        }
    }

    [Fact]
    public void Wall_tile_is_visible_but_tiles_behind_it_are_not()
    {
        // Setup: player at (10,10), wall at (10,8), radius 5.
        // The wall cell itself should be lit; (10,7) is directly behind and blocked.
        var wall   = new Position(10, 8);
        var behind = new Position(10, 7);
        var origin = new Position(10, 10);
        var state  = MapWithWalls(20, 20, wall);
        var vis    = FovSystem.Compute(state, origin, 5);

        Assert.Contains(wall,   vis);   // wall is visible
        Assert.DoesNotContain(behind, vis); // blocked by wall
    }

    [Fact]
    public void Horizontal_wall_row_blocks_everything_behind_it()
    {
        // Three-wide wall row at y=7; player at (10,10) looking north.
        var origin = new Position(10, 10);
        var state  = MapWithWalls(20, 20,
            new Position(9,  7),
            new Position(10, 7),
            new Position(11, 7));
        var vis = FovSystem.Compute(state, origin, 6);

        // The wall row itself must be visible.
        Assert.Contains(new Position(10, 7), vis);

        // Direct line-of-sight is blocked: nothing in the column beyond the wall.
        Assert.DoesNotContain(new Position(10, 6), vis);
        Assert.DoesNotContain(new Position(10, 5), vis);
    }

    [Fact]
    public void No_visible_tile_exceeds_the_radius()
    {
        // Even in a fully open map no tile outside the radius should be added.
        var origin = new Position(15, 15);
        int radius = 7;
        var vis = FovSystem.Compute(OpenMap(35, 35), origin, radius);

        foreach (var pos in vis)
            Assert.True(InCircle(origin, pos, radius),
                $"Tile {pos} is outside radius {radius} but was marked visible.");
    }

    [Fact]
    public void Open_field_visibility_is_symmetric_in_all_quadrants()
    {
        // Rotating the origin's surroundings by 90°/180°/270° on an empty map
        // must produce identical visible sets.
        var origin = new Position(15, 15);
        var vis    = FovSystem.Compute(OpenMap(31, 31), origin, 8);

        for (int dx = 1; dx <= 8; dx++)
        for (int dy = 1; dy <= 8; dy++)
        {
            bool q1 = vis.Contains(new Position(origin.X + dx, origin.Y + dy));
            bool q2 = vis.Contains(new Position(origin.X - dx, origin.Y + dy));
            bool q3 = vis.Contains(new Position(origin.X + dx, origin.Y - dy));
            bool q4 = vis.Contains(new Position(origin.X - dx, origin.Y - dy));

            Assert.True(q1 == q2 && q2 == q3 && q3 == q4,
                $"Visibility not symmetric at offset ({dx},{dy}): {q1},{q2},{q3},{q4}");
        }
    }

    [Fact]
    public void Corner_wall_does_not_block_adjacent_open_cells()
    {
        // A single wall cell should only block tiles directly behind it in the
        // ray's direction, not the open cells around the corner.
        //
        //   . . . . .
        //   . . . . .
        //   . . # . .   <- wall at (12,8), origin at (10,10)
        //   . . . . .
        //   . . @ . .   <- origin
        //
        var origin = new Position(10, 10);
        var wall   = new Position(12, 8);
        var state  = MapWithWalls(20, 20, wall);
        var vis    = FovSystem.Compute(state, origin, 6);

        // Cells to the side of the wall (same row) must still be visible.
        Assert.Contains(new Position(11, 8), vis);  // just left of wall
        Assert.Contains(new Position(13, 8), vis);  // just right of wall (if in radius)
        // The wall itself is visible.
        Assert.Contains(wall, vis);
    }

    [Fact]
    public void Radius_zero_only_origin_visible()
    {
        var origin = new Position(10, 10);
        var vis    = FovSystem.Compute(OpenMap(20, 20), origin, 0);

        Assert.Single(vis);
        Assert.Contains(origin, vis);
    }

    [Fact]
    public void Same_seed_map_produces_same_visible_set_deterministically()
    {
        // Pure function: calling Compute twice with identical inputs must return
        // identical results (important for replay / save-load correctness).
        var state  = MapWithWalls(20, 20, new Position(10, 8), new Position(11, 9));
        var origin = new Position(10, 10);

        var vis1 = FovSystem.Compute(state, origin, 6);
        var vis2 = FovSystem.Compute(state, origin, 6);

        Assert.Equal(vis1, vis2);
    }
}
