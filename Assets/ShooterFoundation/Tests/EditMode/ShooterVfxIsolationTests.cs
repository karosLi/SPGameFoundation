using NUnit.Framework;
using SPF.Presentation.Combat;
using Unity.Mathematics;

namespace ShooterFoundation.Tests
{
    public class ShooterVfxIsolationTests
    {
        [Test]
        public void VisualQualityOverflowAndVisualSeedsLeaveReplayBytesIdentical()
        {
            using var a = new ShooterTestWorld(); using var b = new ShooterTestWorld();
            var rich = new CombatVfxPool(96); var low = new CombatVfxPool(1);
            ShooterSpawner.Enemy(a.World,new float2(0,0),hp:1000,speed:0);
            ShooterSpawner.Enemy(b.World,new float2(0,0),hp:1000,speed:0);
            a.State.Run.Beam = b.State.Run.Beam = 1;
            for(int tick=0;tick<90;tick++)
            {
                a.Step();b.Step();rich.BeginFrame(1f/30,0);low.BeginFrame(1f/30,3);
                for(int k=0;k<80;k++)
                {
                    ulong key=((ulong)(uint)(tick+1)<<32)|(uint)(k+1);
                    rich.Emit(VfxProfile.Impact,new float2(k,0),key);low.Emit(VfxProfile.HeroHurt,new float2(k,0),key);
                }
            }
            Assert.Greater(low.Stats.Dropped,100);
            CollectionAssert.AreEqual(a.Session.CaptureSnapshot(),b.Session.CaptureSnapshot(),"damage, collision, RNG and replay are independent of FX admission");
        }
    }
}
