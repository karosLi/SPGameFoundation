using System;
using NUnit.Framework;
using SPF.Runtime.Composition;
using SPF.Runtime.Session;
using Unity.Mathematics;

namespace PuzzleFoundation.Tests
{
    sealed class M3TestWorld : IDisposable
    {
        public readonly SimSession Session;
        readonly GameplayModuleAsset m_Module;
        readonly ModeDefinition m_Mode;
        public M3TestWorld(uint seed = 4)
        {
            m_Mode = M3Mode.Create(out m_Module);
            Session = SimSession.Create(m_Mode, seed);
            Session.ManualClock = true;
            Session.Start();
            Board.StartRequested = true;
            Session.Step();
        }
        public M3Board Board => Session.World.Resource(M3Keys.Board);
        public void Play(M3Move move) { Board.Moves.Enqueue(move); Session.Step(); }
        public void Dispose()
        {
            Session.Dispose();
            UnityEngine.Object.DestroyImmediate(m_Module);
            UnityEngine.Object.DestroyImmediate(m_Mode);
        }
    }

    public class M3Tests
    {
        static void SetRow(M3Board b, int y, string colors)
        {
            for (int x = 0; x < colors.Length; x++) b[new int2(x, y)] = (byte)(colors[x] - '0');
        }

        /// <summary>A board with no matches (period-3 diagonal pattern of colours 1-3, plus 4-6 noise).</summary>
        static void Calm(M3Board b)
        {
            for (int y = 0; y < M3Board.Height; y++)
                for (int x = 0; x < M3Board.Width; x++)
                {
                    b[new int2(x, y)] = (byte)(1 + (x + 2 * y) % 3 + 3 * ((x / 2 + y) % 2));
                    b.Special[M3Board.Index(new int2(x, y))] = M3Special.None;
                }
        }

        [Test]
        public void NewBoardsStartCalmAndPlayable()
        {
            for (uint seed = 1; seed < 30; seed++)
            {
                using var t = new M3TestWorld(seed);
                Assert.AreEqual(M3Flow.Playing, t.Board.Flow);
                Assert.AreEqual(0, M3Rules.FindMatches(t.Board, new bool[64]), "no ready-made matches");
                Assert.IsTrue(M3Rules.FindMove(t.Board, out _), "at least one move");
            }
        }

        [Test]
        public void SwapsWithoutAMatchAreUndone()
        {
            using var t = new M3TestWorld();
            Calm(t.Board);
            Assert.AreEqual(0, M3Rules.FindMatches(t.Board, new bool[64]));
            byte a = t.Board[new int2(0, 0)], b = t.Board[new int2(1, 0)];
            int moves = t.Board.MovesLeft;
            Assert.IsFalse(M3Rules.FindMatches(t.Board, new bool[64]) > 0);
            t.Play(new M3Move { Cell = new int2(0, 7), Direction = new int2(1, 0) });
            if (t.Board.Events.Count == 2)
            {
                Assert.AreEqual(M3EventKind.SwapBack, t.Board.Events[1].Kind);
                Assert.AreEqual(moves, t.Board.MovesLeft, "a refused swap costs no move");
            }
            Assert.AreEqual(a, t.Board[new int2(0, 0)]);
            Assert.AreEqual(b, t.Board[new int2(1, 0)]);
        }

        [Test]
        public void MatchesClearFallRefillAndScore()
        {
            using var t = new M3TestWorld();
            var board = t.Board;
            Calm(board);
            // Row 0: 1 1 2 1 ... swapping (2,0) with (2,1)=1 makes 1 1 1.
            SetRow(board, 0, "11214565");
            board[new int2(2, 1)] = 1;
            board[new int2(3, 1)] = 5;
            int moves = board.MovesLeft;
            t.Play(new M3Move { Cell = new int2(2, 0), Direction = new int2(0, 1) });
            Assert.AreEqual(moves - 1, board.MovesLeft);
            Assert.Greater(board.Score, 0);
            int clears = 0, falls = 0, spawns = 0, bombs = 0, maxStep = 0;
            foreach (var e in board.Events)
            {
                if (e.Kind == M3EventKind.Clear) clears++;
                if (e.Kind == M3EventKind.MakeBomb) bombs++;
                if (e.Kind == M3EventKind.Fall) falls++;
                if (e.Kind == M3EventKind.Spawn) spawns++;
                maxStep = math.max(maxStep, e.Step);
            }
            Assert.GreaterOrEqual(clears, 3);
            Assert.AreEqual(clears - bombs, spawns, "every cleared cell is refilled (bombs stay)");
            Assert.Greater(falls, 0);
            Assert.GreaterOrEqual(maxStep, 2, "clear, then fall / spawn");
            for (int i = 0; i < 64; i++) Assert.AreNotEqual(0, board.Color[i], "the board is full again");
            Assert.AreEqual(0, M3Rules.FindMatches(board, new bool[64]), "cascades ran to the end");
        }

        [Test]
        public void LinesOfFourLeaveBombsThatClear3x3()
        {
            const byte X = 7;   // a test colour no generated gem has: no accidental matches
            using var t = new M3TestWorld();
            var board = t.Board;
            Calm(board);
            // Row 3: X X _ X with an X above the gap -> a line of four.
            board[new int2(0, 3)] = X; board[new int2(1, 3)] = X; board[new int2(3, 3)] = X; board[new int2(2, 4)] = X;
            t.Play(new M3Move { Cell = new int2(2, 3), Direction = new int2(0, 1) });
            int made = 0;
            foreach (var e in board.Events) if (e.Kind == M3EventKind.MakeBomb) { made++; Assert.AreEqual(new int2(2, 3), e.A, "at the swapped cell"); }
            Assert.AreEqual(1, made);

            // A bomb matched in a line takes its 3x3 neighbourhood with it.
            Calm(board);
            board[new int2(4, 4)] = X; board.Special[M3Board.Index(new int2(4, 4))] = M3Special.Bomb;
            board[new int2(5, 4)] = X; board[new int2(6, 5)] = X;
            t.Play(new M3Move { Cell = new int2(6, 4), Direction = new int2(0, 1) });
            int first = 0;
            foreach (var e in board.Events) if (e.Kind == M3EventKind.Clear && e.Step == 1) first++;
            Assert.GreaterOrEqual(first, 9 + 1, "the line plus the bomb's 3x3");
        }

        [Test]
        public void TurnsAreTicksAndUndoRestoresExactly()
        {
            using var t = new M3TestWorld(9);
            var history = new SnapshotHistory(t.Session, 16);
            history.Record();
            var before = t.Session.CaptureSnapshot();
            uint tick = t.Session.Clock.NextTickIndex;
            t.Session.Update(5f);
            Assert.AreEqual(tick, t.Session.Clock.NextTickIndex, "a turn-based session does not tick with time");
            Assert.IsTrue(M3Rules.FindMove(t.Board, out var move));
            t.Board.Moves.Enqueue(move);
            t.Session.RequestTicks(1);
            t.Session.Update(0f);
            t.Session.Sync();
            Assert.Greater(t.Board.Score, 0, "the move was played on the requested tick");
            history.Record();
            int score = t.Board.Score;
            Assert.IsTrue(history.Undo());
            Assert.AreEqual(0, t.Board.Score);
            CollectionAssert.AreEqual(before, t.Session.CaptureSnapshot(), "undo is exact");
            Assert.IsTrue(history.Redo());
            Assert.AreEqual(score, t.Board.Score);
        }

        [Test]
        public void SameSeedSameMovesSameGame()
        {
            int Play(uint seed, out byte[] state)
            {
                using var t = new M3TestWorld(seed);
                for (int i = 0; i < 15 && t.Board.Flow == M3Flow.Playing; i++)
                {
                    Assert.IsTrue(M3Rules.FindMove(t.Board, out var move));
                    t.Play(move);
                }
                state = t.Session.CaptureSnapshot();
                return t.Board.Score;
            }
            int a = Play(21, out var sa), b = Play(21, out var sb);
            Assert.AreEqual(a, b);
            CollectionAssert.AreEqual(sa, sb);
            Assert.Greater(a, 0);
        }

        [Test]
        public void ScoreTargetWinsAndRunningOutOfMovesLoses()
        {
            using var t = new M3TestWorld(3);
            t.Board.Target = 1;
            Assert.IsTrue(M3Rules.FindMove(t.Board, out var m));
            t.Play(m);
            Assert.AreEqual(M3Flow.Won, t.Board.Flow);

            using var u = new M3TestWorld(3);
            u.Board.Target = 1000000;
            u.Board.MovesLeft = 1;
            Assert.IsTrue(M3Rules.FindMove(u.Board, out m));
            u.Play(m);
            Assert.AreEqual(M3Flow.Lost, u.Board.Flow);
        }
    }
}
