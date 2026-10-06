using NUnit.Framework;
using SPF.Contracts;
using SurvivorFoundation.Presentation;
using Unity.Mathematics;

namespace SurvivorFoundation.Tests
{
    public class SvVisualMotionTests
    {
        [Test]
        public void SwapRemovalDoesNotChangeSurvivingMonsterPose()
        {
            var rows = new[] { new EntityHandle(5,1), new EntityHandle(9,2), new EntityHandle(31,7) };
            for(int sample=0;sample<120;sample++)
            {
                float time=sample/60f;
                float2 before=new float2(SvVisualMotion.Frame(time,rows[2],true,true),SvVisualMotion.Wobble(time,rows[2],true));
                // A neighbouring monster dies: the last row moves into the vacated dense index.
                rows[1]=rows[2];
                float2 after=new float2(SvVisualMotion.Frame(time,rows[1],true,true),SvVisualMotion.Wobble(time,rows[1],true));
                Assert.AreEqual(before,after,"a surviving handle keeps its phase through table compaction");
            }
        }
        [Test]
        public void StationarySmoothPoseIsQuietAndRecycledIdentityGetsFreshPhase()
        {
            var handle=new EntityHandle(31,7);
            for(int i=0;i<20;i++) {Assert.AreEqual(0,SvVisualMotion.Frame(i,handle,false,true));Assert.AreEqual(0f,SvVisualMotion.Wobble(i,handle,false));}
            Assert.AreNotEqual(SvVisualMotion.Phase(handle),SvVisualMotion.Phase(new EntityHandle(31,8)));
            Assert.AreNotEqual(SvVisualMotion.Phase(handle),SvVisualMotion.Phase(new EntityHandle(32,7)));
        }
    }
}
