using System;
using System.Collections.Generic;
using Latios.Psyshock;
using Latios.Transforms;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using Physics = Latios.Psyshock.Physics;

namespace Latios2022Lab
{
    public enum LayerExecution { Immediate, Single, Parallel }

    public static class PsyshockProbe
    {
        // (world center, radius). Finite, non-negative fixtures only; not a solver benchmark.
        public static float4[] Fixture(int fixture)
        {
            switch (fixture)
            {
                case 0: return Array.Empty<float4>();
                case 1: return new[] { new float4(-3f, 1f, 0f, 0.5f) };
                case 2: return new[] {
                    new float4(-2f, 0f, 0f, 1f), new float4(0f, 0f, 0f, 1f), // tangent AABBs
                    new float4(0.5f, 0.25f, 0f, 1f), new float4(7f, 0f, 0f, 0.25f),
                    new float4(-15f, 0f, 0f, 10f), // outlier + cross-cell large body
                    new float4(0f, 0f, 5f, 0.5f) }; // depth-separated
                case 3: return new[] {
                    new float4(0f, 0f, 0f, 0f), new float4(0f, 0f, 0f, 0f),
                    new float4(0.5f, 0f, 0f, 0f),
                    new float4(math.asfloat(math.asint(0.5f) + 1), 0f, 0f, 0f) };
                default: throw new ArgumentOutOfRangeException(nameof(fixture));
            }
        }

        public static void RunPairs(int fixture, LayerExecution execution, int subdivisions)
        {
            var spheres = Fixture(fixture);
            using var world = LabWorld.Create("S1a query identities");
            var bodies = new NativeArray<ColliderBody>(spheres.Length, Allocator.TempJob);
            try
            {
                // Maximum possible unordered pairs. AddNoResize must fail rather than drop results.
                using var pairs = new NativeList<int2>(math.max(1, spheres.Length * (spheres.Length - 1) / 2), Allocator.TempJob);
                for (int i = 0; i < spheres.Length; i++)
                {
                    bodies[i] = new ColliderBody {
                        collider = new SphereCollider(float3.zero, spheres[i].w),
                        transform = new TransformQvvs(spheres[i].xyz, quaternion.identity),
                        entity = world.EntityManager.CreateEntity() // real, distinct entities, no alias-check bypass
                    };
                }
                var config = Physics.BuildCollisionLayer(bodies)
                    .WithWorldBounds(new float3(-4f), new float3(4f))
                    .WithSubdivisions(subdivisions, subdivisions, subdivisions);
                CollisionLayer layer = default;
                JobHandle pending = default;
                bool created = false;
                try
                {
                    if (execution == LayerExecution.Immediate)
                        config.RunImmediate(out layer, Allocator.TempJob);
                    else if (execution == LayerExecution.Single)
                        pending = config.ScheduleSingle(out layer, Allocator.TempJob);
                    else
                        pending = config.ScheduleParallel(out layer, Allocator.TempJob);
                    created = true;
                    var processor = new CollectPairs { Pairs = pairs.AsParallelWriter() };
                    if (execution == LayerExecution.Immediate)
                        Physics.FindPairs(layer, processor).RunImmediate();
                    else if (execution == LayerExecution.Single)
                        pending = Physics.FindPairs(layer, processor).ScheduleSingle(pending);
                    else
                        pending = Physics.FindPairs(layer, processor).ScheduleParallel(pending);
                    pending.Complete();
                    var actual = new HashSet<int>();
                    for (int i = 0; i < pairs.Length; i++)
                    {
                        var pair = pairs[i];
                        if (pair.x < 0 || pair.y >= spheres.Length || pair.x >= pair.y)
                            throw new InvalidOperationException("Invalid normalized source-index pair.");
                        if (!actual.Add(pair.x * spheres.Length + pair.y))
                            throw new InvalidOperationException("Duplicate candidate pair.");
                    }
                    var expected = new HashSet<int>();
                    for (int a = 0; a < spheres.Length; a++)
                        for (int b = a + 1; b < spheres.Length; b++)
                        {
                            // Independent axis tests, not Physics.AabbFrom/Physics.Overlaps.
                            float3 amin = spheres[a].xyz - spheres[a].w;
                            float3 amax = spheres[a].xyz + spheres[a].w;
                            float3 bmin = spheres[b].xyz - spheres[b].w;
                            float3 bmax = spheres[b].xyz + spheres[b].w;
                            if (math.all(amin <= bmax) && math.all(bmin <= amax))
                                expected.Add(a * spheres.Length + b);
                        }
                    if (!actual.SetEquals(expected))
                        throw new InvalidOperationException("FindPairs differs from inclusive brute-force AABB oracle.");
                }
                finally
                {
                    pending.Complete();
                    if (created) layer.Dispose();
                }
            }
            finally
            {
                bodies.Dispose();
            }
        }

        public static void RunQueries(bool expectBurst)
        {
            using var values = new NativeArray<float>(6, Allocator.TempJob);
            using var flags = new NativeArray<int>(6, Allocator.TempJob);
            new QueryJob { Values = values, Flags = flags }.Schedule().Complete();
            if (flags[0] != 1 || flags[1] != 0 || flags[2] != 1 || flags[3] != 1 || flags[4] != 0)
                throw new InvalidOperationException("Known sphere hit/miss result mismatch.");
            float[] expected = { 2f, -3f, 1f, 2f, -0.5f, -1f };
            for (int i = 0; i < expected.Length; i++)
                if (!math.isfinite(values[i]) || math.abs(values[i] - expected[i]) > 1e-5f)
                    throw new InvalidOperationException("Known sphere ray/distance geometry mismatch at " + i);
            if (flags[5] != (expectBurst ? 1 : 0))
                throw new InvalidOperationException("Query job execution differs from required Burst mode.");
        }

        [BurstCompile(CompileSynchronously = true)]
        private struct CollectPairs : IFindPairsProcessor
        {
            public NativeList<int2>.ParallelWriter Pairs;
            public void Execute(in FindPairsResult result) => Pairs.AddNoResize(
                new int2(math.min(result.sourceIndexA, result.sourceIndexB), math.max(result.sourceIndexA, result.sourceIndexB)));
        }

        [BurstCompile(CompileSynchronously = true)]
        private struct QueryJob : IJob
        {
            public NativeArray<float> Values;
            public NativeArray<int> Flags;
            public void Execute()
            {
                Collider sphere = new SphereCollider(float3.zero, 1f);
                var transform = new TransformQvvs(new float3(-2f, 1f, 0f), quaternion.identity);
                Flags[0] = Physics.Raycast(new float3(-5f, 1f, 0f), new float3(2f, 1f, 0f), sphere, transform, out var hit) ? 1 : 0;
                Values[0] = hit.distance; Values[1] = hit.position.x; Values[2] = hit.position.y;
                Flags[1] = Physics.Raycast(new float3(-5f, 3f, 0f), new float3(2f, 3f, 0f), sphere, transform, out _) ? 1 : 0;
                Flags[2] = Physics.DistanceBetween(new float3(1f, 1f, 0f), sphere, transform, 2f, out var outside) ? 1 : 0;
                Values[3] = outside.distance;
                Flags[3] = Physics.DistanceBetween(new float3(-1.5f, 1f, 0f), sphere, transform, 0f, out var inside) ? 1 : 0;
                Values[4] = inside.distance; Values[5] = inside.hitpoint.x;
                Flags[4] = Physics.DistanceBetween(new float3(1f, 1f, 0f), sphere, transform, 1.9f, out _) ? 1 : 0;
                int burst = 1; MarkManaged(ref burst); Flags[5] = burst;
            }
            [BurstDiscard] private static void MarkManaged(ref int value) => value = 0;
        }
    }
}
