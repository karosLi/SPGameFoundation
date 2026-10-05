using System.Collections.Generic;
using SPF.L1.Navigation;
using SPF.L1.Spatial;
using Unity.Mathematics;

namespace RpgFoundation
{
    public struct Room
    {
        public int2 Min;
        public int2 Size;
        public int2 Center => Min + Size / 2;
        public int Area => Size.x * Size.y;
        public bool Contains(int2 cell) => math.all(cell >= Min) && math.all(cell < Min + Size);
    }

    /// <summary>
    /// Rooms-and-corridors floors, deterministic for (run seed, floor). Rooms never overlap; each room is
    /// joined to the previous one by an L-shaped two-tile corridor (plus an occasional extra link for
    /// loops), so every room is reachable. The start is the first room, the stairs go into the room
    /// farthest from it by walking distance (flow field).
    /// </summary>
    public static class DungeonGenerator
    {
        public const byte Floor = 0, Wall = 1;

        public static void Generate(TileMap map, uint runSeed, int floor, RpgConfig.DungeonSection d, List<Room> rooms, out int2 start, out int2 stairs, out int stairsRoom)
        {
            var random = new Random(math.hash(new uint2(runSeed, (uint)floor)) | 1u);
            map.Fill(Wall);
            rooms.Clear();
            int2 size = map.Size;
            int target = random.NextInt(d.RoomsMin, d.RoomsMax + 1);
            for (int attempt = 0; attempt < 400 && rooms.Count < target; attempt++)
            {
                int2 roomSize = new int2(random.NextInt(d.RoomSizeMin, d.RoomSizeMax + 1), random.NextInt(d.RoomSizeMin, d.RoomSizeMax + 1));
                int2 min = new int2(random.NextInt(2, size.x - roomSize.x - 2), random.NextInt(2, size.y - roomSize.y - 2));
                var room = new Room { Min = min, Size = roomSize };
                if (Overlaps(room, rooms)) continue;
                rooms.Add(room);
                Carve(map, room.Min, room.Size);
            }
            for (int i = 1; i < rooms.Count; i++)
            {
                Corridor(map, rooms[i - 1].Center, rooms[i].Center, random.NextBool());
                if (i >= 2 && random.NextFloat() < 0.25f)
                    Corridor(map, rooms[i - 2].Center, rooms[i].Center, random.NextBool());
            }
            map.MarkChanged();

            start = rooms[0].Center;
            stairs = start;
            stairsRoom = 0;
            // Farthest room by walking distance.
            using var field = new FlowField(map.Size);
            field.ScheduleBuild(map.AsView(), new[] { start }, default).Complete();
            int best = -1;
            for (int i = 1; i < rooms.Count; i++)
            {
                int dist = field.Distance[rooms[i].Center.y * size.x + rooms[i].Center.x];
                if (dist != FlowField.Unreachable && dist > best)
                {
                    best = dist;
                    stairs = rooms[i].Center;
                    stairsRoom = i;
                }
            }
        }

        static bool Overlaps(in Room room, List<Room> rooms)
        {
            for (int i = 0; i < rooms.Count; i++)
            {
                var r = rooms[i];
                // One tile of wall between rooms at least.
                if (math.all(room.Min - 2 < r.Min + r.Size) && math.all(r.Min - 2 < room.Min + room.Size))
                    return true;
            }
            return false;
        }

        static void Carve(TileMap map, int2 min, int2 size)
        {
            var tiles = map.Tiles;
            for (int y = min.y; y < min.y + size.y; y++)
            for (int x = min.x; x < min.x + size.x; x++)
                if (x > 0 && y > 0 && x < map.Size.x - 1 && y < map.Size.y - 1)
                    tiles[y * map.Size.x + x] = Floor;
        }

        static void Corridor(TileMap map, int2 a, int2 b, bool horizontalFirst)
        {
            int2 corner = horizontalFirst ? new int2(b.x, a.y) : new int2(a.x, b.y);
            Line(map, a, corner);
            Line(map, corner, b);
        }

        static void Line(TileMap map, int2 a, int2 b)
        {
            int2 min = math.min(a, b), max = math.max(a, b);
            // Two tiles wide so actors pass each other.
            Carve(map, min, max - min + 2);
        }
    }
}
