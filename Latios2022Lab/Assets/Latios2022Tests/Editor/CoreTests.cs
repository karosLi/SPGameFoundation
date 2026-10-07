using System;
using System.Collections;
using System.Text.RegularExpressions;
using Latios;
using NUnit.Framework;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace Latios2022Lab
{
    public class CoreTests
    {
        [Test, Combinatorial]
        public void TrackedWriterReaderAndDisposal(
            [Values(1, 8, 64)] int batchSize,
            [Values(CollectionExit.Remove, CollectionExit.DestroyEntity, CollectionExit.DisposeWorld)] CollectionExit exit)
        {
            LabEnvironment.Verify();
            CollectionProbe.Run(batchSize, exit);
        }

        [Test]
        public void TwoOwnedWorldsAndQvvsInstallationCanUpdateAndDispose()
        {
            LabEnvironment.Verify();
            using var first = LabWorld.Create("S1a first", true);
            using var second = LabWorld.Create("S1a second", true);
            Assert.AreNotEqual(first.SequenceNumber, second.SequenceNumber);
            first.Update();
            second.Update();
        }

        [Test]
        public void ExceptionAfterSchedulingStillOwnsTheDisposalDependency()
        {
            LabEnvironment.Verify();
            using var witness = new NativeArray<int>(2, Allocator.Persistent);
            using var world = LabWorld.Create("S1a expected exception");
            var owner = world.EntityManager.CreateEntity();
            CollectionProbe.Add(world, owner, witness);
            var writer = world.GetOrCreateSystemManaged<LabWriterSystem>();
            writer.Owner = owner; writer.BatchSize = 8; writer.ThrowAfterSchedule = true;
            world.simulationSystemGroup.AddSystemToUpdateList(writer);
            LogAssert.Expect(LogType.Exception, new Regex("InvalidOperationException: Lab scheduled failure"));
            world.simulationSystemGroup.Update();
            Assert.IsTrue(world.latiosWorldUnmanaged.RemoveCollectionComponentAndDispose<LabCollection>(owner));
            Assert.AreEqual(1, witness[0]);
            Assert.AreEqual(17, witness[1]);
        }

        [UnityTest]
        public IEnumerator ReenterPlayModeTwiceWithDomainReload()
        {
            LabEnvironment.Verify();
            Assert.IsFalse(EditorSettings.enterPlayModeOptionsEnabled &&
                (EditorSettings.enterPlayModeOptions & EnterPlayModeOptions.DisableDomainReload) != 0,
                "This test requires domain reload; restore that setting in this lab before running.");
            yield return new EnterPlayMode();
            CollectionProbe.Run(8, CollectionExit.DisposeWorld);
            yield return new ExitPlayMode();
            yield return new EnterPlayMode();
            CollectionProbe.Run(64, CollectionExit.DestroyEntity);
            yield return new ExitPlayMode();
        }
    }
}
