using System.Collections.Generic;
using Unity.Mathematics;
using Random = Unity.Mathematics.Random;

namespace PuzzleFoundation
{
    /// <summary>
    /// Match-3 rules, deterministic: swaps that make a line of three or more clear it; lines of four or more
    /// leave a bomb (clears 3x3 when matched, chaining); gems fall, new ones drop in from a seeded stream;
    /// cascades repeat with a growing combo; a stuck board reshuffles. Every move appends an event log in
    /// animation steps for the presentation.
    /// </summary>
    public static class M3Rules
    {
        const int W = M3Board.Width, H = M3Board.Height, N = W * H;

        static Random Rng(M3Board b, uint salt) => new Random(math.hash(new uint3(b.Seed, (uint)b.MovesMade, salt)) | 1u);

        public static void NewBoard(M3Board b, uint seed, int level)
        {
            b.Seed = seed;
            b.Level = level;
            b.MovesMade = 0;
            b.Score = 0;
            b.Combo = 0;
            b.MovesLeft = 20;
            b.Target = 1200 + 400 * level;
            var random = Rng(b, 0xB0A2Du);
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    var c = new int2(x, y);
                    byte color;
                    do color = (byte)random.NextInt(1, M3Board.Colors + 1);
                    while ((x >= 2 && b[new int2(x - 1, y)] == color && b[new int2(x - 2, y)] == color) ||
                           (y >= 2 && b[new int2(x, y - 1)] == color && b[new int2(x, y - 2)] == color));
                    b[c] = color;
                    b.Special[M3Board.Index(c)] = M3Special.None;
                }
            if (!FindMove(b, out _)) Shuffle(b, 0);
            b.Events.Clear();
            b.Flow = M3Flow.Playing;
            b.Version++;
            b.BoardSerial++;
        }

        /// <summary>Marks every cell in a horizontal or vertical run of 3+ equal colours; returns the cells marked.</summary>
        public static int FindMatches(M3Board b, bool[] mask, List<(int2 start, int2 dir, int length)> runs = null)
        {
            System.Array.Clear(mask, 0, mask.Length);
            runs?.Clear();
            int marked = 0;
            for (int pass = 0; pass < 2; pass++)
            {
                int2 dir = pass == 0 ? new int2(1, 0) : new int2(0, 1);
                int outer = pass == 0 ? H : W, inner = pass == 0 ? W : H;
                for (int o = 0; o < outer; o++)
                {
                    int i = 0;
                    while (i < inner)
                    {
                        int2 start = pass == 0 ? new int2(i, o) : new int2(o, i);
                        byte color = b[start];
                        int len = 1;
                        while (i + len < inner && color != 0 && b[start + dir * len] == color) len++;
                        if (color != 0 && len >= 3)
                        {
                            runs?.Add((start, dir, len));
                            for (int k = 0; k < len; k++)
                            {
                                int idx = M3Board.Index(start + dir * k);
                                if (!mask[idx]) { mask[idx] = true; marked++; }
                            }
                        }
                        i += len;
                    }
                }
            }
            return marked;
        }

        static void SwapCells(M3Board b, int2 a, int2 c)
        {
            int ia = M3Board.Index(a), ic = M3Board.Index(c);
            (b.Color[ia], b.Color[ic]) = (b.Color[ic], b.Color[ia]);
            (b.Special[ia], b.Special[ic]) = (b.Special[ic], b.Special[ia]);
        }

        /// <summary>Plays a move. Returns false (and logs a swap back) when it makes no match.</summary>
        public static bool Apply(M3Board b, M3Move move)
        {
            b.Events.Clear();
            b.LogSerial++;
            int2 a = move.Cell, c = move.Cell + move.Direction;
            if (b.Flow != M3Flow.Playing || !M3Board.InBounds(a) || !M3Board.InBounds(c) || math.csum(math.abs(move.Direction)) != 1) return false;
            SwapCells(b, a, c);
            b.Events.Add(new M3Event { Kind = M3EventKind.Swap, A = a, B = c, Step = 0 });
            var mask = new bool[N];
            var runs = new List<(int2, int2, int)>();
            if (FindMatches(b, mask, runs) == 0)
            {
                SwapCells(b, a, c);
                b.Events.Add(new M3Event { Kind = M3EventKind.SwapBack, A = c, B = a, Step = 1 });
                return false;
            }
            b.MovesMade++;
            b.MovesLeft--;
            b.Combo = 0;
            int step = 1;
            bool first = true;
            while (FindMatches(b, mask, runs) > 0)
            {
                // Lines of four or more leave a bomb: at the swapped cell when it is part of the line, else mid-line.
                var bombs = new List<(int2 cell, byte color)>();
                foreach (var (start, dir, len) in runs)
                {
                    if (len < 4) continue;
                    int2 at = start + dir * (len / 2);
                    for (int k = 0; k < len && first; k++)
                    {
                        int2 cell = start + dir * k;
                        if (math.all(cell == a) || math.all(cell == c)) at = cell;
                    }
                    bombs.Add((at, b[start]));
                }
                // Bombs caught in the clear explode (3x3), and may chain.
                bool grew = true;
                while (grew)
                {
                    grew = false;
                    for (int i = 0; i < N; i++)
                    {
                        if (!mask[i] || b.Special[i] != M3Special.Bomb) continue;
                        b.Special[i] = M3Special.None;
                        int2 centre = new int2(i % W, i / W);
                        for (int dy = -1; dy <= 1; dy++)
                            for (int dx = -1; dx <= 1; dx++)
                            {
                                int2 n = centre + new int2(dx, dy);
                                if (M3Board.InBounds(n) && !mask[M3Board.Index(n)]) { mask[M3Board.Index(n)] = true; grew = true; }
                            }
                    }
                }
                int cleared = 0;
                for (int i = 0; i < N; i++)
                {
                    if (!mask[i]) continue;
                    b.Events.Add(new M3Event { Kind = M3EventKind.Clear, A = new int2(i % W, i / W), Color = b.Color[i], Step = step });
                    b.Color[i] = 0;
                    b.Special[i] = M3Special.None;
                    cleared++;
                }
                b.Score += cleared * 10 * (b.Combo + 1);
                foreach (var (cell, color) in bombs)
                {
                    b[cell] = color;
                    b.Special[M3Board.Index(cell)] = M3Special.Bomb;
                    b.Events.Add(new M3Event { Kind = M3EventKind.MakeBomb, A = cell, Color = color, Special = M3Special.Bomb, Step = step });
                }
                step++;
                Collapse(b, step);
                step++;
                b.Combo++;
                first = false;
            }
            if (!FindMove(b, out _)) { Shuffle(b, step); }
            if (b.Score >= b.Target) b.Flow = M3Flow.Won;
            else if (b.MovesLeft <= 0) b.Flow = M3Flow.Lost;
            b.Version++;
            return true;
        }

        /// <summary>Gravity per column, then new gems from the seeded stream drop in from above.</summary>
        static void Collapse(M3Board b, int step)
        {
            var random = Rng(b, (uint)(0x5EED0 + step));
            for (int x = 0; x < W; x++)
            {
                int write = 0;
                for (int y = 0; y < H; y++)
                {
                    var from = new int2(x, y);
                    if (b[from] == 0) continue;
                    if (write != y)
                    {
                        var to = new int2(x, write);
                        b[to] = b[from];
                        b.Special[M3Board.Index(to)] = b.Special[M3Board.Index(from)];
                        b[from] = 0;
                        b.Special[M3Board.Index(from)] = M3Special.None;
                        b.Events.Add(new M3Event { Kind = M3EventKind.Fall, A = from, B = to, Color = b[to], Special = b.Special[M3Board.Index(to)], Step = step });
                    }
                    write++;
                }
                for (int y = write, k = 0; y < H; y++, k++)
                {
                    var cell = new int2(x, y);
                    byte color = (byte)random.NextInt(1, M3Board.Colors + 1);
                    b[cell] = color;
                    b.Events.Add(new M3Event { Kind = M3EventKind.Spawn, A = new int2(x, H + k), B = cell, Color = color, Step = step });
                }
            }
        }

        /// <summary>First swap (in board order) that makes a match: hints, the stuck-board check.</summary>
        public static bool FindMove(M3Board b, out M3Move move)
        {
            var mask = new bool[N];
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                    for (int d = 0; d < 2; d++)
                    {
                        var a = new int2(x, y);
                        var dir = d == 0 ? new int2(1, 0) : new int2(0, 1);
                        var c = a + dir;
                        if (!M3Board.InBounds(c)) continue;
                        SwapCells(b, a, c);
                        bool match = FindMatches(b, mask) > 0;
                        SwapCells(b, a, c);
                        if (match) { move = new M3Move { Cell = a, Direction = dir }; return true; }
                    }
            move = default;
            return false;
        }

        /// <summary>Deterministic reshuffle until the board has no ready-made match and at least one move.</summary>
        public static void Shuffle(M3Board b, int step)
        {
            var random = Rng(b, 0x5AFFu);
            var mask = new bool[N];
            for (int attempt = 0; attempt < 100; attempt++)
            {
                for (int i = N - 1; i > 0; i--)
                {
                    int j = random.NextInt(i + 1);
                    (b.Color[i], b.Color[j]) = (b.Color[j], b.Color[i]);
                    (b.Special[i], b.Special[j]) = (b.Special[j], b.Special[i]);
                }
                if (FindMatches(b, mask) == 0 && FindMove(b, out _)) break;
            }
            b.Events.Add(new M3Event { Kind = M3EventKind.Shuffle, Step = step });
        }
    }
}
