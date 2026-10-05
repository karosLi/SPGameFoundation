using System;
using NUnit.Framework;
using SPF.Contracts;
using SPF.Runtime.Scheduling;
using SPF.Runtime.World;
using Unity.Jobs;

namespace SPF.Tests.EditMode
{
    /// <summary>Undeclared accesses during scheduling are reported; declared ones, barriers and outside-tick reads are not.</summary>
    public class AccessGuardTests
    {
        sealed class Reader : SimSystemBase
        {
            public bool DeclareValue;
            public override SimPhase Phase => SimPhase.Decide;
            public override void Declare(AccessDeclaration access)
            {
                access.Read(TestKeys.Weight);
                if (DeclareValue) access.Read(TestKeys.Value);
            }
            public override JobHandle OnTick(in SimContext context, JobHandle dependency)
            {
                var values = context.Column(TestKeys.Value);   // declared only when DeclareValue
                return dependency;
            }
        }

        sealed class Barrier : SimSystemBase
        {
            public override SimPhase Phase => SimPhase.ApplyCommands;
            public override void Declare(AccessDeclaration access) { }
            public override JobHandle OnTick(in SimContext context, JobHandle dependency)
            {
                context.Column(TestKeys.Value);
                context.World.Resource(SimWorld.DestroyQueueKey);
                return dependency;
            }
        }

        sealed class JobDataReader : SimSystemBase
        {
            public override SimPhase Phase => SimPhase.Decide;
            public override void Declare(AccessDeclaration access) => access.Read(TestKeys.Value);
            public override JobHandle OnTick(in SimContext context, JobHandle dependency)
            {
                context.World.Resource(SimWorld.DestroyQueueKey);   // job data, not declared
                context.World.Resource(TestKeys.Snapshot);          // plain resource: unchecked
                return dependency;
            }
        }

        [Test]
        public void UndeclaredColumnThrowsDeclaredDoesNot()
        {
            using var world = TestKeys.CreateWorld(4);
            using (var bad = new TickPipeline(world, new ISimSystem[] { new Reader() }))
            {
                var e = Assert.Throws<InvalidOperationException>(() => { bad.BeginTick(new TickTime(1, 1f / 30f, 0.0)); bad.EndTick(); });
                StringAssert.Contains("Test.Item.Value", e.Message);
                StringAssert.Contains("Reader", e.Message);
            }
            using var good = new TickPipeline(world, new ISimSystem[] { new Reader { DeclareValue = true }, new Barrier() });
            Assert.DoesNotThrow(() => { good.BeginTick(new TickTime(1, 1f / 30f, 0.0)); good.EndTick(); });
            Assert.DoesNotThrow(() => world.Column(TestKeys.Value), "outside a tick nothing is checked");
        }

        [Test]
        public void UndeclaredJobDataResourceThrows()
        {
            using var world = TestKeys.CreateWorld(4);
            using var pipeline = new TickPipeline(world, new ISimSystem[] { new JobDataReader() });
            var e = Assert.Throws<InvalidOperationException>(() => { pipeline.BeginTick(new TickTime(1, 1f / 30f, 0.0)); pipeline.EndTick(); });
            StringAssert.Contains("World.DestroyQueue", e.Message);
        }
    }
}
