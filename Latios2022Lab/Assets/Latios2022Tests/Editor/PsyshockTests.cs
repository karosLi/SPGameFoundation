using NUnit.Framework;

namespace Latios2022Lab
{
    public class PsyshockTests
    {
        [Test, Combinatorial]
        public void ArrayLayerCandidatesMatchIndependentAabbOracle(
            [Values(0, 1, 2, 3)] int fixture,
            [Values(LayerExecution.Immediate, LayerExecution.Single, LayerExecution.Parallel)] LayerExecution execution,
            [Values(1, 2, 4)] int subdivisions)
        {
            LabEnvironment.Verify();
            PsyshockProbe.RunPairs(fixture, execution, subdivisions);
        }

        [Test]
        public void ScheduledRayAndSignedDistanceMatchKnownGeometryAndBurstMode()
        {
            LabEnvironment.Verify();
            PsyshockProbe.RunQueries(LabEnvironment.ExpectBurst);
        }
    }
}
