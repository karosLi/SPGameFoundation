using System.Collections.Generic;
using System.IO;
using SPF.Contracts;
using Unity.Collections;
using Unity.Mathematics;

namespace PuzzleFoundation
{
    public enum M3Flow : byte { Menu, Playing, Won, Lost }
    public enum M3Special : byte { None = 0, Bomb = 1 }

    /// <summary>What happened during a move, in animation steps (presentation plays them in order).</summary>
    public enum M3EventKind : byte { Swap, SwapBack, Clear, Fall, Spawn, MakeBomb, Shuffle }

    public struct M3Event
    {
        public M3EventKind Kind;
        public int2 A, B;          // cells (Fall: from A to B; Spawn: B is the cell, A its start above the board)
        public byte Color;
        public M3Special Special;
        public int Step;           // 0 = the swap, then one step per cascade phase
    }

    public struct M3Move
    {
        public int2 Cell;
        public int2 Direction;     // one of the four unit steps
    }

    /// <summary>
    /// The board and the run (a resource: a match-3 board is a grid, not a table of entities): colours,
    /// specials, score, moves left, target. All rules live in <see cref="M3Rules"/>; snapshots make undo exact.
    /// </summary>
    public sealed class M3Board : ISnapshotResource, IResettableResource, System.IDisposable
    {
        public const int Width = 8, Height = 8, Colors = 6;
        public NativeArray<byte> Color = new NativeArray<byte>(Width * Height, Allocator.Persistent);
        public NativeArray<M3Special> Special = new NativeArray<M3Special>(Width * Height, Allocator.Persistent);
        public M3Flow Flow = M3Flow.Menu;
        public int Score, MovesLeft, Target, Level, MovesMade, Combo;
        public uint Seed = 1;
        public readonly Queue<M3Move> Moves = new Queue<M3Move>();
        public readonly List<M3Event> Events = new List<M3Event>();   // last move's log (presentation)
        public bool StartRequested;
        public int Version;
        /// <summary>Bumped whenever <see cref="Events"/> holds a new move's log (presentation animates it).</summary>
        public int LogSerial;
        /// <summary>Bumped when the board was replaced wholesale (new game, undo / load): presentation rebuilds.</summary>
        public int BoardSerial;

        public static int Index(int2 c) => c.y * Width + c.x;
        public static bool InBounds(int2 c) => math.all(c >= 0) && c.x < Width && c.y < Height;
        public byte this[int2 c] { get => Color[Index(c)]; set => Color[Index(c)] = value; }

        public void OnReset() { Flow = M3Flow.Menu; Moves.Clear(); Events.Clear(); Version++; }

        public void WriteSnapshot(BinaryWriter w)
        {
            NativeIO.Write(w, Color);
            NativeIO.Write(w, Special);
            w.Write((byte)Flow); w.Write(Score); w.Write(MovesLeft); w.Write(Target); w.Write(Level); w.Write(MovesMade); w.Write(Combo); w.Write(Seed);
        }

        public void ReadSnapshot(BinaryReader r)
        {
            NativeIO.ReadAll(r, Color);
            NativeIO.ReadAll(r, Special);
            Flow = (M3Flow)r.ReadByte(); Score = r.ReadInt32(); MovesLeft = r.ReadInt32(); Target = r.ReadInt32(); Level = r.ReadInt32(); MovesMade = r.ReadInt32(); Combo = r.ReadInt32(); Seed = r.ReadUInt32();
            Moves.Clear();
            Events.Clear();
            Version++;
            BoardSerial++;
        }

        public void Dispose()
        {
            if (Color.IsCreated) Color.Dispose();
            if (Special.IsCreated) Special.Dispose();
        }
    }
}
