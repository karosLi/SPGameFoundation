using NUnit.Framework;
using SPF.L1.Spatial;
using SPF.L2.Combat;
using Unity.Mathematics;

namespace SPF.Tests.EditMode
{
    public class SpatialBackendContractTests
    {
        static readonly SpatialTestBackend[] Backends = {
            SpatialTestBackend.Grid, SpatialTestBackend.PrunedGrid,
            SpatialTestBackend.LayeredGrid, SpatialTestBackend.Quadtree };
        static GridEntry Entry(int owner, float x, float y, float radius = .25f, int data = 1) =>
            new GridEntry { Owner = owner, Position = new float2(x, y), Radius = radius, Data = data };
        static SpatialBackendFixture Create(SpatialTestBackend backend, int capacity) =>
            new SpatialBackendFixture(backend, new int2(4), 1, new float2(-2), capacity, 2);
        static GridEntry[] OrderedSource() => new[] {
            Entry(0, -.5f, -1.5f), Entry(1, 1.5f, -1.5f, 1),
            Entry(2, -1.5f, -.5f), Entry(3, .5f, -.5f) };
        static void Fill(SpatialBackendFixture backend, GridEntry[] source, int requested)
        {
            for (int i = 0; i < math.min(source.Length, backend.Capacity); i++) backend.Set(i, source[i]);
            backend.Build(requested);
        }

        // Fixture policy: Owner/Data filtering is a caller responsibility. Count all filtered overflow
        // or stop at its first witness. A stopped count is a lower bound, never total hidden matches.
        struct BoundedVisitor : IGridVisitor
        {
            public int[] Output;
            public int Written, Calls, Overflow, Exclude, RequiredData;
            public bool StopOnOverflow, Stopped;
            public bool Visit(in GridEntry entry)
            {
                Calls++;
                if (entry.Owner == Exclude || (entry.Data & RequiredData) != RequiredData) return true;
                if (Written < Output.Length) { Output[Written++] = entry.Owner; return true; }
                Overflow++;
                if (StopOnOverflow) { Stopped = true; return false; }
                return true;
            }
        }
        static BoundedVisitor Collector(int capacity, bool stop = false) => new BoundedVisitor {
            Output = new int[capacity], Exclude = -1, StopOnOverflow = stop };

        [TestCaseSource(nameof(Backends))]
        public void SharedWindowCapacityAndStrictCircleContract(SpatialTestBackend backend)
        {
            using var fixture = Create(backend, 5);
            float belowMax = math.asfloat(math.asuint(2f) - 1);
            var source = new[] { Entry(0, -2, -2), Entry(1, belowMax, 0), Entry(2, 2, 0),
                Entry(3, -2.01f, 0), Entry(4, 0, 0, 1) };
            Fill(fixture, source, 8);
            Assert.AreEqual(3, fixture.EntryCount); Assert.AreEqual(5, fixture.Dropped,
                "Three unstaged requests plus two out-of-window centers; radius does not rescue a rejected center.");
            var all = Collector(5); fixture.Query(float2.zero, 20, ref all);
            CollectionAssert.AreEquivalent(new[] { 0, 1, 4 }, Prefix(all));
            // Only owner 4: exact circle tangent is excluded, one representable inward point included.
            Fill(fixture, new[] { source[4] }, 1);
            var tangent = Collector(5); fixture.Query(new float2(2, 0), 1, ref tangent);
            Assert.AreEqual(0, tangent.Written);
            var inward = Collector(5); fixture.Query(new float2(belowMax, 0), 1, ref inward);
            CollectionAssert.AreEqual(new[] { 4 }, Prefix(inward));
            fixture.Build(-1); var empty = Collector(1); fixture.Query(float2.zero, 20, ref empty);
            Assert.AreEqual(0, fixture.EntryCount); Assert.AreEqual(0, fixture.Dropped); Assert.AreEqual(0, empty.Calls);
            using var zero = Create(backend, 0); zero.Build(3);
            Assert.AreEqual(0, zero.EntryCount); Assert.AreEqual(3, zero.Dropped);
        }

        [TestCaseSource(nameof(Backends))]
        public void CallerFiltersBoundedPrefixAndOverflowRemainExplicit(SpatialTestBackend backend)
        {
            using var fixture = Create(backend, 6);
            // Same circle and staging order: broadphase cannot interpret team/height/generation.
            var source = new[] { Entry(0, 0, 0, data: 1), Entry(1, 0, 0, data: 1),
                Entry(2, 0, 0, data: 2), Entry(3, 0, 0, data: 1),
                Entry(4, 0, 0, data: 1), Entry(5, 0, 0, data: 3) };
            Fill(fixture, source, 6);
            foreach (int capacity in new[] { 0, 1, 2, 4, 6 })
            foreach (bool stop in new[] { false, true })
            {
                var plain = Collector(capacity, stop); plain.RequiredData = 1; plain.Exclude = 1;
                var measured = Collector(capacity, stop); measured.RequiredData = 1; measured.Exclude = 1;
                var stats = new GridQueryStats();
                fixture.Query(float2.zero, 1, ref plain);
                fixture.QueryMeasured(float2.zero, 1, ref measured, ref stats);
                var eligible = new[] { 0, 3, 4, 5 };
                Assert.AreEqual(math.min(capacity, 4), plain.Written);
                for (int i = 0; i < plain.Written; i++) Assert.AreEqual(eligible[i], plain.Output[i]);
                Assert.AreEqual(stop && capacity < 4, plain.Stopped);
                Assert.AreEqual(stop && capacity < 4 ? 1 : math.max(0, 4 - capacity), plain.Overflow);
                int expectedCalls = stop && capacity < 4 ? new[] { 1, 4, 5, 6 }[capacity] : 6;
                Assert.AreEqual(expectedCalls, plain.Calls, "No callbacks after early-out, including filtered entries.");
                CollectionAssert.AreEqual(Prefix(plain), Prefix(measured));
                Assert.AreEqual(plain.Calls, measured.Calls); Assert.AreEqual(plain.Overflow, measured.Overflow);
                Assert.AreEqual(plain.Stopped, measured.Stopped); Assert.AreEqual(plain.Calls, stats.VisitorCalls);
                Assert.GreaterOrEqual(stats.EntriesExamined, stats.VisitorCalls);
            }
        }

        struct SeparationPrefix : IGridVisitor
        {
            public int Remaining;
            public float2 Sum;
            public bool Visit(in GridEntry entry)
            {
                Sum += GroundCombatQueries.Separation(float2.zero, entry.Position, 4, 99, entry.Owner);
                return --Remaining != 0;
            }
        }
        [Test]
        public void EqualSetsDoNotAuthorizeDifferentPrefixesOrOrderedSeparation()
        {
            var source = OrderedSource();
            var expected = new[] { new[] { 0, 1, 2, 3 }, new[] { 0, 1, 2, 3 },
                new[] { 0, 2, 3, 1 }, new[] { 0, 2, 1, 3 } };
            var reductions = new float2[4];
            foreach (var backend in Backends)
            using (var fixture = Create(backend, source.Length))
            {
                Fill(fixture, source, source.Length);
                var all = Collector(4); var measured = Collector(4); var stats = new GridQueryStats();
                fixture.Query(float2.zero, 10, ref all); fixture.QueryMeasured(float2.zero, 10, ref measured, ref stats);
                CollectionAssert.AreEqual(expected[(int)backend], Prefix(all));
                CollectionAssert.AreEqual(Prefix(all), Prefix(measured));
                CollectionAssert.AreEquivalent(new[] { 0, 1, 2, 3 }, Prefix(all));
                var prefix = new SeparationPrefix { Remaining = 2 };
                fixture.Query(float2.zero, 10, ref prefix); reductions[(int)backend] = prefix.Sum;
                float2 independent = float2.zero;
                for (int i = 0; i < 2; i++)
                {
                    var e = source[expected[(int)backend][i]];
                    independent += GroundCombatQueries.Separation(float2.zero, e.Position, 4, 99, e.Owner);
                }
                Assert.AreEqual(math.asuint(independent), math.asuint(prefix.Sum));
            }
            Assert.AreEqual(math.asuint(reductions[0]), math.asuint(reductions[1]), "Pruned grid preserves reduction order.");
            Assert.AreNotEqual(math.asuint(reductions[0]), math.asuint(reductions[2]));
            Assert.AreNotEqual(math.asuint(reductions[0]), math.asuint(reductions[3]),
                "Test-only tree is set-equivalent but not a drop-in capped separation backend.");
        }

        struct StableNearest : IGridVisitor
        {
            public int Target, Exclude, RequiredData;
            public float Square;
            public bool Visit(in GridEntry entry)
            {
                if (entry.Owner == Exclude || (entry.Data & RequiredData) != RequiredData) return true;
                float square = math.lengthsq(entry.Position);
                if (Target < 0 || square < Square || square == Square && entry.Owner < Target)
                { Target = entry.Owner; Square = square; }
                return true;
            }
        }
        [TestCaseSource(nameof(Backends))]
        public void FullFilteredNearestReductionUsesCallerStableIdentityTie(SpatialTestBackend backend)
        {
            using var fixture = Create(backend, 4);
            var source = new[] { Entry(9, -1, 0), Entry(3, 1, 0), Entry(1, 0, 0, data: 2), Entry(0, 0, 0) };
            for (int reverse = 0; reverse < 2; reverse++)
            {
                for (int i = 0; i < source.Length; i++) fixture.Set(i, source[reverse == 0 ? i : source.Length - 1 - i]);
                fixture.Build(source.Length);
                var nearest = new StableNearest { Target = -1, Exclude = 0, RequiredData = 1 };
                fixture.Query(float2.zero, 2, ref nearest);
                Assert.AreEqual(3, nearest.Target); Assert.AreEqual(1f, nearest.Square);
            }
        }
        static int[] Prefix(BoundedVisitor visitor)
        {
            var result = new int[visitor.Written];
            System.Array.Copy(visitor.Output, result, visitor.Written); return result;
        }
    }
}
