using NUnit.Framework;
using SPF.Presentation;

namespace SPF.Tests.EditMode
{
    public class MonotonicInterpolationTests
    {
        [Test]
        public void PauseResumeHoldsPresentedFractionUntilTheNextFixedTick()
        {
            var view=new MonotonicInterpolation();
            Assert.AreEqual(.3f,view.Resolve(41,0,.3f));
            Assert.AreEqual(1,view.Resolve(41,0,1));
            Assert.AreEqual(1,view.Resolve(41,0,.3f));
            Assert.AreEqual(1,view.Resolve(41,0,.8f));
            Assert.AreEqual(0,view.Resolve(42,0,0));
            Assert.AreEqual(.6f,view.Resolve(42,0,.6f));
            Assert.AreEqual(.6f,view.Resolve(42,0,.2f));
        }
        [Test]
        public void RestoreExplicitDiscontinuityAndViewReplacementStartNewIntervals()
        {
            var view=new MonotonicInterpolation();view.Resolve(11,7,1);
            Assert.AreEqual(.2f,view.Resolve(11,8,.2f),"a same-tick snapshot restore is a new timeline");
            Assert.AreEqual(.1f,view.Resolve(11,8,.1f,true),"level or resource replacement is explicit");
            view.Reset();Assert.AreEqual(0,view.Resolve(11,8,0));
            Assert.AreEqual(0,view.Resolve(uint.MaxValue,8,0));view.Resolve(uint.MaxValue,8,1);
            Assert.AreEqual(0,view.Resolve(0,8,0),"a wrapping tick still starts a new interval");
        }
        [Test]
        public void InvalidOrOvershootingRequestsCannotRewindOrEscapeTheInterval()
        {
            var view=new MonotonicInterpolation();Assert.AreEqual(0,view.Resolve(1,0,float.NaN));
            Assert.AreEqual(.4f,view.Resolve(1,0,.4f));Assert.AreEqual(.4f,view.Resolve(1,0,float.PositiveInfinity));
            Assert.AreEqual(1,view.Resolve(1,0,2));Assert.AreEqual(1,view.Resolve(1,0,-1));
        }
    }
}
