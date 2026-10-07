using NUnit.Framework;
using SPF.L2.Skills;

namespace SPF.Tests.EditMode
{
    public class AbilityAdmissionTests
    {
        [TestCase(true, true, true, true, 1, AbilityRejection.None)]
        [TestCase(true, true, true, false, 1, AbilityRejection.NotRequested)]
        [TestCase(false, true, true, true, 1, AbilityRejection.NotPlaying)]
        [TestCase(true, false, true, true, 1, AbilityRejection.Dead)]
        [TestCase(true, true, false, true, 1, AbilityRejection.Busy)]
        [TestCase(true, true, true, true, 0, AbilityRejection.NoCharge)]
        public void PureAdmissionReportsReasonWithoutCommittingAnAction(bool playing, bool alive, bool free, bool requested, int charges, AbilityRejection reason)
        {
            var result = AbilityAdmission.Evaluate(playing, alive, free, requested, charges);
            Assert.AreEqual(reason, result.Reason); Assert.AreEqual(reason == AbilityRejection.None, result.Allowed);
        }
    }
}
