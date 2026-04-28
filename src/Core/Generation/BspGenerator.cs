namespace MonoRogue.Core.Generation;

using System.Collections.Immutable;
using MonoRogue.Core;
using MonoRogue.Core.Model;

/// <summary>
/// Binary Space Partitioning dungeon generator.
///
/// Algorithm:
///   1. Recursively split the map area into a tree of rectangular leaves.
///   2. Carve a room inside each leaf.
///   3. Connect sibling leaves with L-shaped corridors (post-order traversal).
///
/// Fully deterministic given the same seed and dimensions.
/// </summary>
public static class BspGenerator
{
    private const int MinLeafSize = 7;  // minimum side length before we stop splitting
    private const int MinRoomSize = 4;  // minimum room side length (inside the leaf)

    // ── Public entry point ───────────────────────────────────────────────────

    public static DungeonMap Generate(
        int          width,
        int          height,
        SeededRandom rng,
        int          floorLevel = 1)
    {
        // Tiles buffer: true = floor, false = wall
        var tiles = new bool[width * height]; // all walls initially

        // BSP tree
        var root = new BspNode(1, 1, width - 2, height - 2);
        SplitNode(root, rng, 0);
        CarveRooms(root, rng, tiles, width);

        // Collect rooms (leaves that have a room carved)
        var rooms = ImmutableArray.CreateBuilder<RoomRect>();
        CollectRooms(root, rooms);
        ConnectNodes(root, tiles, width, rng);

        var roomList = rooms.ToImmutable();

        if (roomList.IsEmpty)
        {
            // Fallback: single open room
            return FallbackMap(width, height, floorLevel);
        }

        var playerSpawn  = roomList[0].Center;
        var exitPosition = roomList[roomList.Length - 1].Center;

        return new DungeonMap(
            Width:         width,
            Height:        height,
            Tiles:         [.. tiles],
            Rooms:         roomList,
            PlayerSpawn:   playerSpawn,
            ExitPosition:  exitPosition,
            FloorLevel:    floorLevel
        );
    }

    // ── BSP node (mutable construction-time type, never exposed) ────────────

    private sealed class BspNode
    {
        public int X, Y, W, H;
        public BspNode?  Left, Right;
        public RoomRect? Room;

        public BspNode(int x, int y, int w, int h) { X = x; Y = y; W = w; H = h; }

        public RoomRect Bounds => new(X, Y, W, H);
    }

    // ── Splitting ─────────────────────────────────────────────────────────────

    private static void SplitNode(BspNode node, SeededRandom rng, int depth)
    {
        if (depth > 5) return; // cap recursion depth
        if (node.W < MinLeafSize * 2 && node.H < MinLeafSize * 2) return;

        bool splitH;
        if (node.W >= MinLeafSize * 2 && node.H >= MinLeafSize * 2)
            splitH = rng.NextBool(); // either direction
        else
            splitH = node.W < MinLeafSize * 2; // forced vertical split when too narrow

        if (splitH)
        {
            // Horizontal split: divide height
            var splitY = rng.Next(MinLeafSize, node.H - MinLeafSize + 1);
            node.Left  = new BspNode(node.X, node.Y,          node.W, splitY);
            node.Right = new BspNode(node.X, node.Y + splitY, node.W, node.H - splitY);
        }
        else
        {
            // Vertical split: divide width
            var splitX = rng.Next(MinLeafSize, node.W - MinLeafSize + 1);
            node.Left  = new BspNode(node.X,          node.Y, splitX,          node.H);
            node.Right = new BspNode(node.X + splitX, node.Y, node.W - splitX, node.H);
        }

        SplitNode(node.Left,  rng, depth + 1);
        SplitNode(node.Right, rng, depth + 1);
    }

    // ── Room carving ──────────────────────────────────────────────────────────

    private static void CarveRooms(BspNode node, SeededRandom rng, bool[] tiles, int mapW)
    {
        if (node.Left is null && node.Right is null)
        {
            // Leaf: carve a room
            var roomW = rng.Next(MinRoomSize, Math.Max(MinRoomSize + 1, node.W - 1));
            var roomH = rng.Next(MinRoomSize, Math.Max(MinRoomSize + 1, node.H - 1));
            var roomX = node.X + rng.Next(0, Math.Max(1, node.W - roomW));
            var roomY = node.Y + rng.Next(0, Math.Max(1, node.H - roomH));

            // Clamp to leaf bounds
            roomW = Math.Min(roomW, node.X + node.W - roomX - 1);
            roomH = Math.Min(roomH, node.Y + node.H - roomY - 1);

            if (roomW < MinRoomSize || roomH < MinRoomSize) return;

            node.Room = new RoomRect(roomX, roomY, roomW, roomH);

            for (int y = roomY; y < roomY + roomH; y++)
                for (int x = roomX; x < roomX + roomW; x++)
                    tiles[y * mapW + x] = true;

            return;
        }

        if (node.Left  is not null) CarveRooms(node.Left,  rng, tiles, mapW);
        if (node.Right is not null) CarveRooms(node.Right, rng, tiles, mapW);
    }

    // ── Corridor connection ────────────────────────────────────────────────────

    private static void ConnectNodes(BspNode node, bool[] tiles, int mapW, SeededRandom rng)
    {
        if (node.Left is null || node.Right is null) return;

        ConnectNodes(node.Left,  tiles, mapW, rng);
        ConnectNodes(node.Right, tiles, mapW, rng);

        // Find representative rooms in each subtree
        var leftRoom  = FindAnyRoom(node.Left);
        var rightRoom = FindAnyRoom(node.Right);
        if (leftRoom is null || rightRoom is null) return;

        var from = leftRoom.Value.Center;
        var to   = rightRoom.Value.Center;

        // L-shaped corridor: horizontal then vertical (or vice versa)
        if (rng.NextBool())
        {
            CarveLine(tiles, mapW, from.X, to.X, from.Y, horizontal: true);
            CarveLine(tiles, mapW, from.Y, to.Y, to.X,   horizontal: false);
        }
        else
        {
            CarveLine(tiles, mapW, from.Y, to.Y, from.X, horizontal: false);
            CarveLine(tiles, mapW, from.X, to.X, to.Y,   horizontal: true);
        }
    }

    private static void CarveLine(bool[] tiles, int mapW, int a, int b, int fixed_, bool horizontal)
    {
        int lo = Math.Min(a, b);
        int hi = Math.Max(a, b);
        for (int i = lo; i <= hi; i++)
        {
            int x = horizontal ? i      : fixed_;
            int y = horizontal ? fixed_ : i;
            if (x >= 0 && x < mapW && y * mapW + x < tiles.Length)
                tiles[y * mapW + x] = true;
        }
    }

    // ── Room collection ────────────────────────────────────────────────────────

    private static void CollectRooms(BspNode node, ImmutableArray<RoomRect>.Builder rooms)
    {
        if (node.Room.HasValue) rooms.Add(node.Room.Value);
        if (node.Left  is not null) CollectRooms(node.Left,  rooms);
        if (node.Right is not null) CollectRooms(node.Right, rooms);
    }

    private static RoomRect? FindAnyRoom(BspNode node)
    {
        if (node.Room.HasValue) return node.Room;
        var left  = node.Left  is not null ? FindAnyRoom(node.Left)  : null;
        var right = node.Right is not null ? FindAnyRoom(node.Right) : null;
        return left ?? right;
    }

    // ── Fallback ───────────────────────────────────────────────────────────────

    private static DungeonMap FallbackMap(int width, int height, int floorLevel)
    {
        var tiles = new bool[width * height];
        for (int y = 1; y < height - 1; y++)
            for (int x = 1; x < width - 1; x++)
                tiles[y * width + x] = true;

        var rooms = ImmutableArray.Create(new RoomRect(1, 1, width - 2, height - 2));
        return new DungeonMap(width, height, [.. tiles],
            rooms, new Position(width / 2, height / 2),
            new Position(width - 3, height - 3), floorLevel);
    }
}
