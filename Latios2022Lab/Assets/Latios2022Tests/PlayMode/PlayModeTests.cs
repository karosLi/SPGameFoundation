using System.Collections;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace Latios2022Lab
{
    public class PlayModeTests
    {
        [UnityTest]
        public IEnumerator RepeatedOwnedWorldsLeaveNoCollectionState()
        {
            bool expectedBurst = LabRunPolicy.VerifyRequestedMode();
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
