using NUnit.Framework;
using SPF.Contracts;
using SPF.L1.Spatial;
using SPF.L2.Combat;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace SPF.Tests.EditMode
{
    /// <summary>Exact native swept-contact parity; the brute oracle runs in the same float mode.</summary>
    public class SpatialSweepParityTests
    {
        struct SweepQuery
        {
            public float2 From, To, Center;
            public float Radius, ProxyRadius;
        }

        struct ContactVisitor : IGridVisitor
        {
            [ReadOnly] public NativeArray<float2> Previous, Current;
            [ReadOnly] public NativeArray<float> Radii;
            public NativeArray<int> Masks;
            public SweepQuery Query;
            public int Row, BestOwner;
            public float BestFraction;

            public bool Visit(in GridEntry entry)
            {
                int owner = entry.Owner;
                if (!CombatSweep.Circles(Query.From, Query.To, Query.Radius,
                    Previous[owner], Current[owner], Radii[owner], out float fraction)) return true;
                Masks[Row + owner] = Masks[Row + owner] + 1;
                if (BestOwner < 0 || fraction < BestFraction || fraction == BestFraction && owner < BestOwner)
                {
                    BestOwner = owner;
                    BestFraction = fraction;
                }
                return true;
            }
        }

        struct Probe
        {
            public GridReader Grid;
            public BoundedQuadtreeReference.Reader Tree;
            [ReadOnly] public NativeArray<float2> Previous, Current;
            [ReadOnly] public NativeArray<float> Radii;
            [ReadOnly] public NativeArray<SweepQuery> Queries;
            public NativeArray<int> Masks, BestOwners, BurstSentinel;
            public NativeArray<float> BestFractions;

            public void Execute()
            {
                int burst = 1;
                MarkManaged(ref burst);
                BurstSentinel[0] = burst;
                int targets = Radii.Length, queryCount = Queries.Length;
                for (int i = 0; i < Masks.Length; i++) Masks[i] = 0;
                for (int q = 0; q < queryCount; q++)
                {
                    SweepQuery query = Queries[q];
                    int best = -1;
                    float first = 2f;
                    // No proxy-circle or spatial filtering in the oracle.
                    for (int owner = 0; owner < targets; owner++)
                    {
                        if (!CombatSweep.Circles(query.From, query.To, query.Radius,
                            Previous[owner], Current[owner], Radii[owner], out float fraction)) continue;
                        Masks[q * targets + owner] = 1;
                        if (best < 0 || fraction < first || fraction == first && owner < best)
                        {
                            best = owner;
                            first = fraction;
                        }
                    }
                    BestOwners[q] = best;
                    BestFractions[q] = first;
                    for (int mode = 1; mode <= 3; mode++)
                    {
                        int result = mode * queryCount + q;
                        var visitor = new ContactVisitor
                        {
                            Previous = Previous, Current = Current, Radii = Radii,
                            Masks = Masks, Row = result * targets, Query = query,
                            BestOwner = -1, BestFraction = 2f,
                        };
                        if (mode == 1) Grid.Query(query.Center, query.ProxyRadius, ref visitor);
                        else if (mode == 2) Grid.QueryPruned(query.Center, query.ProxyRadius, ref visitor);
                        else Tree.Query(query.Center, query.ProxyRadius, ref visitor);
                        BestOwners[result] = visitor.BestOwner;
                        BestFractions[result] = visitor.BestFraction;
                    }
                }
            }
        }

        [BurstCompile(FloatMode = FloatMode.Strict, CompileSynchronously = true)]
        struct StrictProbeJob : IJob
        {
            public Probe Data;
            public void Execute() => Data.Execute();
        }

        [BurstCompile(FloatMode = FloatMode.Fast, CompileSynchronously = true)]
        struct FastProbeJob : IJob
        {
            public Probe Data;
            public void Execute() => Data.Execute();
        }

#if !SPF_DOTNET_HARNESS
        [BurstDiscard]
#endif
        static void MarkManaged(ref int burst) { burst = 0; }

        [TestCase(false)]
        [TestCase(true)]
        public void ScheduledSweepsMatchEveryTargetEarliestFractionAndReverseStagedTie(bool fast)
        {
            const int Targets = 10, Queries = 9;
            using var previous = new NativeArray<float2>(Targets, Allocator.TempJob);
            using var current = new NativeArray<float2>(Targets, Allocator.TempJob);
            using var radii = new NativeArray<float>(Targets, Allocator.TempJob);
            using var queries = new NativeArray<SweepQuery>(Queries, Allocator.TempJob);
            using var masks = new NativeArray<int>(4 * Queries * Targets, Allocator.TempJob);
            using var bestOwners = new NativeArray<int>(4 * Queries, Allocator.TempJob);
            using var bestFractions = new NativeArray<float>(4 * Queries, Allocator.TempJob);
            using var burst = new NativeArray<int>(1, Allocator.TempJob);
            using var grid = new SpatialGrid(new int2(128), 2, Targets) { Origin = new float2(-128) };
            using var tree = new BoundedQuadtreeReference(new float2(-128), new float2(256), Targets,
                bucketSize: 1, nodeCapacity: 128);

            // Two simultaneous moving targets cross a stationary projectile. Owner 1 is staged
            // before owner 0 below, forcing stable-ID tie resolution to override traversal order.
            SetTarget(previous, current, radii, 0, new float2(-96, -106), new float2(-96, -86));
            SetTarget(previous, current, radii, 1, previous[0], current[0]);
            queries.Set(0, Query(new float2(-96, -96), new float2(-96, -96)));
            // Opposing horizontal, vertical and diagonal relative motion.
            SetTarget(previous, current, radii, 2, new float2(-22, -96), new float2(-42, -96));
            queries.Set(1, Query(new float2(-42, -96), new float2(-22, -96)));
            SetTarget(previous, current, radii, 3, new float2(32, -86), new float2(32, -106));
            queries.Set(2, Query(new float2(32, -106), new float2(32, -86)));
            SetTarget(previous, current, radii, 4, new float2(104, -88), new float2(88, -104));
            queries.Set(3, Query(new float2(88, -104), new float2(104, -88)));
            // Both motions are zero; one initial overlap and one exact initial tangency.
            SetTarget(previous, current, radii, 5, new float2(-96, 0), new float2(-96, 0));
            queries.Set(4, Query(new float2(-96, 0), new float2(-96, 0)));
            SetTarget(previous, current, radii, 6, new float2(-31.5f, 0), new float2(-31.5f, 0));
            queries.Set(5, Query(new float2(-32, 0), new float2(-32, 0)));
            // Closed glancing tangency, a zero-length miss, and contact exactly at the endpoint.
            SetTarget(previous, current, radii, 7, new float2(32, .5f), new float2(32, .5f));
            queries.Set(6, Query(new float2(24, 0), new float2(40, 0)));
            SetTarget(previous, current, radii, 8, new float2(96, 4), new float2(96, 4));
            queries.Set(7, Query(new float2(96, 0), new float2(96, 0)));
            SetTarget(previous, current, radii, 9, new float2(-96, 96), new float2(-96, 96));
            queries.Set(8, Query(new float2(-100, 96), new float2(-96.5f, 96)));

            var gridStaging = grid.Staging;
            var treeStaging = tree.Staging;
            for (int slot = 0; slot < Targets; slot++)
            {
                int owner = Targets - 1 - slot;
                var entry = new GridEntry
                {
                    Position = (previous[owner] + current[owner]) * .5f,
                    Radius = radii[owner] + math.distance(previous[owner], current[owner]) * .5f,
                    Owner = owner,
                };
                gridStaging[slot] = entry;
                treeStaging[slot] = entry;
            }
            grid.StagingCount.Set(0, Targets);
            grid.ScheduleBuild(default).Complete();
            tree.Build(Targets);
            Assert.AreEqual(Targets, grid.EntryCount);
            Assert.AreEqual(Targets, tree.EntryCount);
            var probe = new Probe
            {
                Grid = grid.AsReader(), Tree = tree.AsReader(), Previous = previous,
                Current = current, Radii = radii, Queries = queries, Masks = masks,
                BestOwners = bestOwners, BestFractions = bestFractions, BurstSentinel = burst,
            };
            if (fast) new FastProbeJob { Data = probe }.Schedule().Complete();
            else new StrictProbeJob { Data = probe }.Schedule().Complete();
#if !SPF_DOTNET_HARNESS
            if (BurstCompiler.IsEnabled) Assert.AreEqual(1, burst[0], "scheduled sweep parity job did not run with enabled Burst");
#endif
            for (int q = 0; q < Queries; q++)
            for (int mode = 1; mode <= 3; mode++)
            {
                int result = mode * Queries + q;
                for (int owner = 0; owner < Targets; owner++)
                    Assert.AreEqual(masks[q * Targets + owner], masks[result * Targets + owner],
                        "mode=" + mode + "; query=" + q + "; target=" + owner + "; fast=" + fast);
                Assert.AreEqual(bestOwners[q], bestOwners[result], "earliest owner query=" + q + "; mode=" + mode);
                Assert.AreEqual(bestFractions[q], bestFractions[result], "earliest fraction query=" + q + "; mode=" + mode);
            }
            CollectionAssert.AreEqual(new[] { 0, 2, 3, 4, 5, 6, 7, -1, 9 },
                new[] { bestOwners[0], bestOwners[1], bestOwners[2], bestOwners[3], bestOwners[4],
                    bestOwners[5], bestOwners[6], bestOwners[7], bestOwners[8] });
            Assert.AreEqual(1, masks[0]);
            Assert.AreEqual(1, masks[1], "both tied targets must appear exactly once");
            Assert.AreEqual(.475f, bestFractions[0], .000001f);
            Assert.AreEqual(0f, bestFractions[4]);
            Assert.AreEqual(0f, bestFractions[5], "initial tangency is closed");
            Assert.AreEqual(.5f, bestFractions[6], "glancing tangency is closed");
            Assert.AreEqual(2f, bestFractions[7], "no-contact sentinel remains untouched");
            Assert.AreEqual(1f, bestFractions[8], "endpoint contact is closed");
        }

        static void SetTarget(NativeArray<float2> previous, NativeArray<float2> current,
            NativeArray<float> radii, int owner, float2 from, float2 to)
        {
            previous[owner] = from;
            current[owner] = to;
            radii[owner] = .25f;
        }

        static SweepQuery Query(float2 from, float2 to) => new SweepQuery
        {
            From = from, To = to, Center = (from + to) * .5f, Radius = .25f,
            ProxyRadius = math.distance(from, to) * .5f + .25f + .001f,
        };
    }
}
