using NUnit.Framework;
using SPF.Presentation;
using SPF.Presentation.Animation;
using SPF.Presentation.ArtDirection;
using SPF.Presentation.Sprites;
using UnityEngine;

namespace SPF.Tests.EditMode
{
    public class SanctuaryArtDirectionTests
    {
        [Test]
        public void HeroAndThreatsHaveDistinctValuesWithoutChangingRigOrAtlasBudget()
        {
            using var art=new NaturalCharacterArt(true);
            Assert.AreEqual(14,NaturalCharacterArt.Parts);
            Assert.AreEqual(2,art.Attachments.Length);
            Assert.AreEqual(14,art.Attachments[0].Length);
            Assert.That(art.Sheet.Texture.width*art.Sheet.Texture.height*4,Is.LessThanOrEqualTo(2*1024*1024));
            Assert.That(MeanValue(art.Canvases[0][1]),Is.GreaterThan(MeanValue(art.Canvases[1][1])+.10f),"Warm ivory hero must remain brighter than coral threats.");
            Assert.That(art.Attachments[0][9].Size.x,Is.GreaterThanOrEqualTo(1.05f),"Chest silhouette cannot regress to the thin prototype.");
            Assert.That(art.Attachments[1][9].Size.x,Is.GreaterThan(art.Attachments[0][9].Size.x),"Raider chest has a distinct broad silhouette.");
            for(int k=0;k<2;k++)for(int p=0;p<NaturalCharacterArt.Parts;p++)
                Assert.That(art.Attachments[k][p].Bone,Is.InRange(0,NaturalCharacterRig.Bones-1));
        }
        static float MeanValue(PixelCanvas canvas)
        {
            float sum=0,n=0;
            foreach(var p in canvas.Pixels)if(p.a>128){sum+=(p.r*.2126f+p.g*.7152f+p.b*.0722f)/255f;n++;}
            return sum/n;
        }
#if !SPF_DOTNET_HARNESS
        [TestCase(SanctuaryScene.Courtyard)]
        [TestCase(SanctuaryScene.Terrace)]
        [TestCase(SanctuaryScene.SkyRiver)]
        public void NativeSceneResourcesLoadInsideMobileMemoryBudget(SanctuaryScene scene)
        {
            using var art=new SanctuaryBackdrop(RenderTier.DataTexture,scene);
            Assert.IsTrue(art.Ready,"Production art must be imported through the real Unity Resources path.");
            Assert.That(art.TextureBytes,Is.InRange(1,8L*1024*1024),"Each environment must stay below an 8 MiB conservative RGBA bound.");
            var ground=Resources.Load<Texture2D>(scene==SanctuaryScene.SkyRiver?"SPF/ArtDirection/SkyRiver":"SPF/ArtDirection/SanctuaryGround");
            Assert.IsFalse(ground.isReadable,"Imported plates must not retain an extra readable CPU copy.");
            Assert.That(Mathf.Max(ground.width,ground.height),Is.LessThanOrEqualTo(1024));
        }
#endif
    }
}
