using System;
using NUnit.Framework;
using SPF.Contracts.Collections;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace RpgFoundation.Tests
{
    public class RpgProjectileOrderingTests
    {
        [BurstCompile(CompileSynchronously = true)]
        struct EmitJob : IJobParallelFor
        {
            public NativeArray<ProjectileRequest> Scratch;
            public NativeArray<byte> Pending;
            public int Count;
            public void Execute(int i)
            {
                Pending[i] = (byte)(i % 4 == 0 ? 0 : 1);
                Scratch[i] = new ProjectileRequest { OwnerId = (Count - i) * 7, Position = new float2(i, i * .1f), Damage = i + 1 };
            }
        }
        [BurstCompile(CompileSynchronously = true)]
        struct GatherProbeJob : IJob
        {
            public GatherProjectileRequestsJob Gather;
            public NativeArray<int> Backend;
            public void Execute()
            {
                bool burst = true;
#if !SPF_DOTNET_HARNESS
                Managed(ref burst);
#else
                burst = false;
#endif
                Backend[0] = burst ? 1 : 0; Gather.Execute();
            }
#if !SPF_DOTNET_HARNESS
            [BurstDiscard] static void Managed(ref bool burst) => burst = false;
#endif
        }
        [TestCase(1, 64)] [TestCase(7, 64)] [TestCase(32, 64)]
        [TestCase(1, 7)] [TestCase(7, 7)] [TestCase(32, 7)]
        public void ScheduledPermutationsHaveIdenticalAcceptedRequestsAndOverflow(int batch, int capacity)
        {
            const int actors = 64;
            using var scratch = new NativeArray<ProjectileRequest>(actors, Allocator.TempJob);
            using var pending = new NativeArray<byte>(actors, Allocator.TempJob);
            using var backend = new NativeArray<int>(1, Allocator.TempJob);
            using var owned = new ParallelQueue<ProjectileRequest>(capacity, Allocator.TempJob);
            var queue = owned; // owning using variable is readonly; Clear changes the shared counter only.
            var expected = new ProjectileRequest[48]; int n = 0;
            for (int i = 0; i < actors; i++) if (i % 4 != 0)
                expected[n++] = new ProjectileRequest { OwnerId = (actors - i) * 7, Position = new float2(i, i * .1f), Damage = i + 1 };
            Array.Sort(expected, new RpgProjectileRequestOrder());
            for (int repeat = 0; repeat < 12; repeat++)
            {
                queue.Clear();
                var emit = new EmitJob { Scratch = scratch, Pending = pending, Count = actors }.Schedule(actors, batch);
                new GatherProbeJob { Gather = new GatherProjectileRequestsJob { Scratch = scratch, Pending = pending, Queue = queue.AsWriter(), Actors = actors }, Backend = backend }.Schedule(emit).Complete();
                Assert.AreEqual(Math.Min(capacity, expected.Length), queue.Count); Assert.AreEqual(Math.Max(0, expected.Length - capacity), queue.Overflow);
                for (int i = 0; i < queue.Count; i++) Assert.AreEqual(expected[i], queue[i], "repeat=" + repeat + " entry=" + i);
#if !SPF_DOTNET_HARNESS
                Assert.AreEqual(1, backend[0], "canonical gather must run through scheduled native Burst");
#endif
            }
        }
        [Test] public void EqualOwnerTieOrderIncludesPayloadAndSignedZero()
        {
            var order = new RpgProjectileRequestOrder(); var a = new ProjectileRequest { OwnerId = 4 };
            var b = a; b.Damage = 1;
            Assert.Less(order.Compare(a, b), 0); Assert.Greater(order.Compare(b, a), 0);
            b = a; b.Position.x = math.asfloat(0x80000000u);
            Assert.AreNotEqual(0, order.Compare(a, b)); Assert.AreEqual(0, order.Compare(a, a));
        }
        [Test] public void RestoredLegacyPendingOrderSpawnsInStableIdentityOrder()
        {
            using var t = new RpgTestWorld(); t.ClearMonsters();
            var requests = t.World.Resource(RpgKeys.ProjectileRequests);
            requests.TryAdd(new ProjectileRequest { OwnerId = 90, Position = t.HeroPosition, Direction = new float2(1, 0), Speed = 1, Radius = .1f, Life = 4, Team = Team.Hero });
            requests.TryAdd(new ProjectileRequest { OwnerId = 2, Position = t.HeroPosition, Direction = new float2(1, 0), Speed = 1, Radius = .1f, Life = 4, Team = Team.Hero });
            var oldShape = t.Session.CaptureSnapshot(); t.Session.RestoreSnapshot(oldShape); t.Step();
            Assert.AreEqual(2, t.World.Table(RpgKeys.Projectile).Count);
            var projectiles = t.World.Column(RpgKeys.ProjectileInfo);
            Assert.AreEqual(2, projectiles[0].OwnerId); Assert.AreEqual(90, projectiles[1].OwnerId);
        }
    }
}
