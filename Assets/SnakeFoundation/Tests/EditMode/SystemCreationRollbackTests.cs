using System;
using System.Reflection;
using NUnit.Framework;
using SPF.Runtime.Scheduling;
using SPF.Runtime.World;
using Unity.Collections;

namespace SnakeFoundation.Tests
{
    public class SystemCreationRollbackTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public void ResolveInitializerReleasesEarlierBuffersWhenLaterTableIsMissing(bool includeProps)
        {
            // Exercise the real initializer's later world lookup after one/two real NativeArrays.
            // This is deterministic dependency failure, not native allocator/OOM injection.
            var type = typeof(SnakeGameModule).Assembly.GetType("SnakeFoundation.Systems.ResolveSystem", true);
            var system = (ISimSystem)Activator.CreateInstance(type, true);
            using var layout = new WorldLayout();
            layout.Table(SnakeKeys.Food, 4);
            if (includeProps) layout.Table(SnakeKeys.Prop, 4);
            using var world = new SimWorld(layout, 1);
            try
            {
                var error = Assert.Throws<ArgumentException>(() => new TickPipeline(world, new[] { system }));
                StringAssert.Contains(includeProps ? SnakeKeys.Snake.ToString() : SnakeKeys.Prop.ToString(), error.Message);
                foreach (var name in new[] { "m_FoodTaken", "m_PropTaken", "m_HeadOnDone" })
                    Assert.IsFalse(((NativeArray<byte>)type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
                        .GetValue(system)).IsCreated, name + " leaked after failed OnCreate");
                // Retry the same real system after self-cleanup, with its complete table dependency set.
                using var completeLayout = new WorldLayout();
                completeLayout.Table(SnakeKeys.Food, 4); completeLayout.Table(SnakeKeys.Prop, 4);
                completeLayout.Table(SnakeKeys.Snake, 4);
                using var completeWorld = new SimWorld(completeLayout, 1);
                using var retry = new TickPipeline(completeWorld, new[] { system });
                retry.Dispose();
                foreach (var name in new[] { "m_FoodTaken", "m_PropTaken", "m_HeadOnDone" })
                    Assert.IsFalse(((NativeArray<byte>)type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
                        .GetValue(system)).IsCreated, name + " leaked after successful retry");
            }
            finally
            {
                // Only this known implementation's explicit test fallback cleans the red baseline;
                // the composition pipeline must never invoke arbitrary failed initializers' OnDestroy.
                system.OnDestroy(world);
            }
        }
    }
}
