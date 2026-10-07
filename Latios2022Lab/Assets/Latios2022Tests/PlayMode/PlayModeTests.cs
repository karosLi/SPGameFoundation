using System;
using System.Collections;
using NUnit.Framework;
using Unity.Burst;
using UnityEngine.TestTools;

namespace Latios2022Lab
{
    public class PlayModeTests
    {
        [UnityTest]
        public IEnumerator RepeatedOwnedWorldsLeaveNoCollectionState()
        {
#if !ENABLE_UNITY_COLLECTIONS_CHECKS
            Assert.Fail("Native collection safety checks must be enabled.");
#endif
            bool expectedBurst = Environment.GetEnvironmentVariable("LATIOS_LAB_EXPECT_BURST") != "off";
            Assert.AreEqual(expectedBurst, BurstCompiler.IsEnabled, "Requested Burst control is unavailable.");
            if (expectedBurst) Assert.IsTrue(BurstCompiler.Options.EnableBurstSafetyChecks);
            for (int i = 0; i < 3; i++)
            {
                CollectionProbe.Run(8, CollectionExit.DisposeWorld);
                PsyshockProbe.RunPairs(2, LayerExecution.Parallel, 2);
                PsyshockProbe.RunQueries(expectedBurst);
                yield return null;
            }
            LogAssert.NoUnexpectedReceived();
        }
    }
}
