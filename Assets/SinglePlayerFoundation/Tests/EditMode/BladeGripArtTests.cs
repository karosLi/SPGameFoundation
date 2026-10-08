using System;
using System.IO;
using NUnit.Framework;
using SPF.Contracts;
using SPF.Contracts.Weapons;
using SPF.Presentation;
using SPF.Presentation.Animation;
using SPF.Presentation.Sprites;
using SPF.Testing;
using Unity.Mathematics;
using UnityEngine;
using FrozenArt=SPF.Tests.EditMode.FrozenBladeArtF46c765;

namespace SPF.Tests.EditMode
{
    public class BladeGripArtTests
    {
        static GameplayCharacterInput Input(int kind=0,int visual=1001,WeaponActionFamily family=WeaponActionFamily.Slash,float facing=1)
        {
            return new GameplayCharacterInput {Handle=new EntityHandle(7,1),Kind=kind,Facing=facing,Scale=1,Tint=new float4(1),
                Weapon=new WeaponViewState {ContentId=1000+(int)family,VisualId=visual,Family=family,Stage=WeaponStage.Active,
                    ContactPhase=.31f,ReleasePhase=.31f,ActiveEndPhase=.45f,Phase=.31f,AimDirection=new float2(facing,0),
                    GripOffset=new float2(.58f,1.25f),SecondaryGripOffset=new float2(.23f,1.18f),MuzzleOffset=new float2(1.65f,1.25f)}};
        }
        [TestCase(0)] [TestCase(1)]
        public void BladeHandUsesDedicatedClosedGripInsteadOfPelvisTile(int kind)
        {
            using var art=new NaturalCharacterArt(true);
            using var presenter=new GameplayCharacterPresenter(RenderTier.DataTexture,1,includeWeapons:true);
            var input=Input(kind);presenter.Begin(1f/60,0);Assert.That(presenter.Submit(input),Is.True);presenter.Evaluate();
            var oldHand=PackedSprite.Pack(float2.zero,new float2(1),art.Attachments[kind][13].Uv,0,new float4(1));
            var actual=presenter.ReadPart(17);
            Assert.That(actual.B.xy,Is.Not.EqualTo(oldHand.B.xy),"The blade needs original closed fingers/thumb, not the pelvis tile shrunk to a hand.");
        }

        static ulong PixelHash(PixelCanvas canvas)
        {
            ulong hash=14695981039346656037UL;
            unchecked {foreach(var pixel in canvas.Pixels) {hash=(hash^pixel.r)*1099511628211UL;hash=(hash^pixel.g)*1099511628211UL;hash=(hash^pixel.b)*1099511628211UL;hash=(hash^pixel.a)*1099511628211UL;}}
            return hash;
        }
        // Fixed .NET harness goldens remain an additional control on that runtime. Native Unity
        // executes the independent frozen f46c765 generator below on its own math/runtime surface.
#if SPF_DOTNET_HARNESS
        static readonly ulong[] OriginalHashes={
            0x3E9DE82B7F5F03B8,0x5A7E9039FF029C33,0x67F20B0CB7E4E47E,0xB409F6CAB09D12A1,
            0xB93E991DFC52FF16,0xCF00C0B4061AF8F1,0xB93E991DFC52FF16,0xDCCE1FC7E305CE78,
            0x454E7F383D94D273,0x1A9CA1609B74361E,0xCD4EA91FDB3597AB,0x3C074C4BD9523B17,
            0x28CBCADB4B261551,0x6127244C022419B4,0x28CBCADB4B261551,0xCA331734079453EE,
            0xA13503AD2C350C00,0x651CE18F97C37341,0xE17DAD9A6A855FD2,0x5E1941DF5A552C7A};
#endif
        static int FirstPixelMismatch(Color32[] actual,Color32[] expected)
        {
            if(actual.Length!=expected.Length)return math.min(actual.Length,expected.Length);
            for(int i=0;i<actual.Length;i++) {
                var a=actual[i];var b=expected[i];
                if(a.r!=b.r||a.g!=b.g||a.b!=b.b||a.a!=b.a)return i;
            }
            return -1;
        }
        static void AssertOriginalCanvas(PixelCanvas actual,FrozenArt.PixelCanvas expected,int index)
        {
            Assert.That(actual.Width,Is.EqualTo(expected.Width),"original canvas width "+index);
            Assert.That(actual.Height,Is.EqualTo(expected.Height),"original canvas height "+index);
            Assert.That(FirstPixelMismatch(actual.Pixels,expected.Pixels),Is.EqualTo(-1),"same-runtime frozen original RGBA bytes "+index);
#if SPF_DOTNET_HARNESS
            Assert.That(PixelHash(actual),Is.EqualTo(OriginalHashes[index]),"fixed .NET original RGBA hash "+index);
#endif
            // Positive controls use the exact byte comparator above. An isolated change in any
            // channel, including transparent RGB, must be detected; neither input is modified.
            var mutated=(Color32[])actual.Pixels.Clone();int at=mutated.Length/2;
            for(int channel=0;channel<4;channel++) {
                var value=actual.Pixels[at];
                if(channel==0)value.r^=1;else if(channel==1)value.g^=1;else if(channel==2)value.b^=1;else value.a^=1;
                mutated[at]=value;
                Assert.That(FirstPixelMismatch(mutated,expected.Pixels),Is.EqualTo(at),"RGBA mutation detector "+index+" channel "+channel);
            }
        }
        [TestCase(false)] [TestCase(true)]
        public void OriginalAtlasPixelsUvsAndTextureBudgetRemainExact(bool weapons)
        {
            using var art=new NaturalCharacterArt(weapons);using var frozen=new FrozenArt.NaturalCharacterArt(weapons);var original=frozen.Sheet;
            Assert.That(art.Sheet.Size,Is.EqualTo(new int2(1024,weapons?512:256)));
            Assert.That(art.Sheet.Count,Is.EqualTo(weapons?26:20));Assert.That(art.Sheet.Size,Is.EqualTo(original.Size));
            for(int frame=0;frame<original.Count;frame++)
            {
                Assert.That(art.Sheet.Origins[frame],Is.EqualTo(original.Origins[frame]),"old origin "+frame);
                Assert.That(art.Sheet[frame].Uv,Is.EqualTo(original[frame].Uv),"old UV "+frame);
                Assert.That(art.Sheet[frame].Pixels,Is.EqualTo(original[frame].Pixels),"old dimensions "+frame);
            }
            for(int role=0;role<2;role++)for(int part=0;part<8;part++)AssertOriginalCanvas(art.Canvases[role][part],frozen.Canvases[role][part],role*8+part);
            for(int weapon=0;weapon<WeaponArt.Count;weapon++)AssertOriginalCanvas(WeaponArt.Draw(weapon),FrozenArt.WeaponArt.Draw(weapon),16+weapon);
            if(weapons)
            {
                Assert.That(art.BladeGripFrames,Is.EqualTo(new[]{24,25}));
                Assert.That(art.Sheet.Origins[24],Is.EqualTo(new int2(954,362)));
                Assert.That(art.Sheet.Origins[25],Is.EqualTo(new int2(2,462)));
                for(int kind=0;kind<2;kind++)Assert.That(art.Sheet[art.BladeGripFrames[kind]].Pixels,Is.EqualTo(new int2(40,32)));
            }
            TestContext.WriteLine("Atlas RGBA32 bytes="+(art.Sheet.Size.x*art.Sheet.Size.y*4)+" old frames="+original.Count+" exact; all 20 canvases byte-identical to frozen f46c765 on this runtime; 80 single-channel mutations detected.");
        }
        [TestCase(0)] [TestCase(1)]
        public void ClosedGripHasPaddedThumbAndSeparateCurledFingersAtHilt(int kind)
        {
            var canvas=NaturalCharacterArt.DrawBladeGrip(kind);int opaque=0,edge=0;
            int2 min=new int2(canvas.Width,canvas.Height),max=new int2(-1);
            for(int y=0;y<canvas.Height;y++)for(int x=0;x<canvas.Width;x++)
            {
                int alpha=canvas.Get(x,y).a;if(alpha==255)opaque++;if(alpha>0&&alpha<255)edge++;
                if(alpha>0){min=math.min(min,new int2(x,y));max=math.max(max,new int2(x,y));}
                if(x<2||y<2||x>=canvas.Width-2||y>=canvas.Height-2)Assert.That(alpha,Is.Zero,"private transparent padding");
            }
            Assert.That(opaque,Is.InRange(300,650));Assert.That(edge,Is.GreaterThan(35));
            float2 visible=(float2)(max+1-min)/new float2(canvas.Width,canvas.Height)*NaturalCharacterArt.BladeGripSize;
            // Frozen old pelvis hand alpha bounds are 72x58 at a .19-square quad. A fitted cuff may
            // add horizontal length; palm thickness must remain close instead of hiding grip defects.
            Assert.That(visible.x,Is.LessThanOrEqualTo(.1425f*1.25f));
            Assert.That(visible.y,Is.LessThanOrEqualTo(.11479167f*1.05f));
            Assert.That(canvas.Get(20,16).a,Is.EqualTo(255),"closed palm covers the unchanged primary hilt pivot");
            Assert.That(canvas.Get(15,23).a,Is.EqualTo(255),"diagonal thumb above the handle");
            Assert.That(canvas.Get(13,8).a,Is.GreaterThan(200),"closed first fingertip below the handle");
            Assert.That(canvas.Get(25,10).a,Is.EqualTo(255),"closed outer fingertip below the handle");
            Assert.That(canvas.Get(15,9),Is.Not.EqualTo(canvas.Get(17,9)),"finger crease separates neighboring curls");
            Assert.That(PixelHash(canvas),Is.Not.EqualTo(PixelHash(NaturalCharacterArt.DrawBladeGrip(1-kind))),"both character palettes remain distinct");
            string output=Environment.GetEnvironmentVariable("SPF_BLADE_GRIP_ART_PREVIEW");
            if(!string.IsNullOrEmpty(output))
            {
                Directory.CreateDirectory(output);using var writer=new BinaryWriter(File.Create(Path.Combine(output,"blade-grip-"+kind+".rgba")));
                writer.Write(canvas.Width);writer.Write(canvas.Height);foreach(var p in canvas.Pixels){writer.Write(p.r);writer.Write(p.g);writer.Write(p.b);writer.Write(p.a);}
                var blade=WeaponArt.Draw(0);using var weaponWriter=new BinaryWriter(File.Create(Path.Combine(output,"blade.rgba")));
                weaponWriter.Write(blade.Width);weaponWriter.Write(blade.Height);foreach(var p in blade.Pixels){weaponWriter.Write(p.r);weaponWriter.Write(p.g);weaponWriter.Write(p.b);weaponWriter.Write(p.a);}
            }
            TestContext.WriteLine("Closed grip "+kind+" opaque="+opaque+" alpha-edge="+edge+" visible units="+visible+" hash="+PixelHash(canvas).ToString("X16"));
        }
        [TestCase(30)] [TestCase(60)] [TestCase(120)]
        public void ClosedGripKeepsPrimaryPivotAndFixedInstanceBudget(int hz)
        {
            using var art=new NaturalCharacterArt(true);
            using var presenter=new GameplayCharacterPresenter(RenderTier.DataTexture,1,includeWeapons:true);
            for(int kind=0;kind<2;kind++)for(int face=-1;face<=1;face+=2)for(int moving=0;moving<2;moving++)
            {
                presenter.Clear();var input=Input(kind,facing:face);input.Scale=.66f;
                input.Velocity=moving==0?float2.zero:new float2(.8f*face,.2f);
                for(int frame=0;frame<hz;frame++)
                {
                    input.Root=input.Ground+=input.Velocity/hz;input.Weapon.Phase=frame/(float)hz;
                    presenter.Begin(1f/hz,kind==0?0:3);Assert.That(presenter.Submit(input),Is.True);presenter.Evaluate();
                    Assert.That(presenter.TryReadWeapon(input.Handle,out var sample),Is.True);
                    var hand=presenter.ReadPart(17);var bone=presenter.ReadBone(input.Handle,NaturalCharacterRig.Hand);
                    Assert.That(math.distance(hand.Center,sample.PrimaryGrip),Is.LessThan(1e-6f),"hand sprite center is the canonical pivot");
                    Assert.That(math.distance(hand.Center,bone.Position),Is.LessThan(1e-6f));
                    Assert.That(presenter.PartsDrawn,Is.EqualTo(19));Assert.That(presenter.PackedPayloadBytes,Is.EqualTo(608));
                    Assert.That(presenter.ColorAtlasBytes,Is.EqualTo(2097152));
                    var packed=PackedSprite.Pack(float2.zero,new float2(1),art.Sheet[art.BladeGripFrames[kind]].Uv,0,new float4(1));
                    Assert.That(hand.B.xy,Is.EqualTo(packed.B.xy));
                }
            }
        }
        [Test]
        public void OnlyEquippedSlashWithResolvedBladeVisualSelectsClosedGrip()
        {
            using var art=new NaturalCharacterArt(true);
            using var presenter=new GameplayCharacterPresenter(RenderTier.DataTexture,1,includeWeapons:true);
            for(int kind=0;kind<2;kind++)for(int family=0;family<=4;family++)for(int visual=1001;visual<=1005;visual++)
            {
                presenter.Clear();var input=Input(kind,visual,(WeaponActionFamily)family);
                if(family==0)input.Weapon=default;
                presenter.Begin(1f/60,0);Assert.That(presenter.Submit(input),Is.True);presenter.Evaluate();
                bool closed=family==1&&(visual==1001||visual==1005);
                var uv=closed?art.Sheet[art.BladeGripFrames[kind]].Uv:art.Attachments[kind][13].Uv;
                var packed=PackedSprite.Pack(float2.zero,new float2(1),uv,0,new float4(1));
                Assert.That(presenter.ReadPart(family==0?13:17).B.xy,Is.EqualTo(packed.B.xy),"family="+family+" visual="+visual);
            }
        }
        [Test]
        public void WarmedClosedGripPresenterAllocatesZeroWithCalibratedProbe()
        {
            using var presenter=new GameplayCharacterPresenter(RenderTier.DataTexture,2,includeWeapons:true);
            var a=Input();var b=Input(1,facing:-1);b.Handle=new EntityHandle(8,1);
            Action work=()=>{for(int frame=0;frame<120;frame++)
            {a.Weapon.Phase=b.Weapon.Phase=frame/120f;presenter.Begin(1f/120,3);presenter.Submit(a);presenter.Submit(b);presenter.Evaluate();}};
            work();using var probe=new ManagedAllocationProbe();var before=probe.Calibrate();var sample=probe.Measure(work);var after=probe.Calibrate();
            TestContext.WriteLine("Two blade actors, 120 warmed presenter updates: "+sample.Value+" current-thread "+sample.Metric+"; controls before="+before.RetainedArrays.Value+"/"+before.Empty.Value+", after="+after.RetainedArrays.Value+"/"+after.Empty.Value+"; collections="+sample.Collections);
            Assert.That(sample.Value,Is.EqualTo(0));Assert.That(presenter.PartsDrawn,Is.EqualTo(38));Assert.That(presenter.PackedPayloadBytes,Is.EqualTo(1216));
        }
        [Test]
        public void OptionalAtlasPrefixPreservesDefaultPackingAndRejectsInvalidCounts()
        {
            var builder=new SpriteAtlasBuilder();for(int i=0;i<12;i++)builder.Add(new PixelCanvas(7+i%3,5+i%4));
            using var original=builder.Build(64,FilterMode.Bilinear,2,true);
            using var explicitAll=builder.Build(64,FilterMode.Bilinear,2,true,builder.Count);
            Assert.That(explicitAll.Size,Is.EqualTo(original.Size));
            for(int i=0;i<original.Count;i++){Assert.That(explicitAll.Origins[i],Is.EqualTo(original.Origins[i]));Assert.That(explicitAll[i].Uv,Is.EqualTo(original[i].Uv));}
            Assert.Throws<ArgumentOutOfRangeException>(()=>builder.Build(sortedPrefixCount:-2));
            Assert.Throws<ArgumentOutOfRangeException>(()=>builder.Build(sortedPrefixCount:builder.Count+1));
        }

#if !SPF_DOTNET_HARNESS
        [Test]
        public void NativeAtlasKeepsEveryOriginalTexelAndPrivateExtrusionGutter()
        {
            using var art=new NaturalCharacterArt(true);using var frozen=new FrozenArt.NaturalCharacterArt(true);var original=frozen.Sheet;
            var pixels=art.Sheet.Texture.GetPixels32();var before=original.Texture.GetPixels32();
            for(int frame=0;frame<original.Count;frame++)
            {
                var p=original.Origins[frame];var size=original[frame].Pixels;
                for(int y=-2;y<size.y+2;y++)for(int x=-2;x<size.x+2;x++)
                {int at=(p.y+y)*original.Size.x+p.x+x;Assert.That(pixels[at],Is.EqualTo(before[at]),"original frame/gutter "+frame);}
            }
            for(int kind=0;kind<2;kind++)
            {
                var canvas=NaturalCharacterArt.DrawBladeGrip(kind);var origin=art.Sheet.Origins[art.BladeGripFrames[kind]];
                for(int y=-2;y<canvas.Height+2;y++)for(int x=-2;x<canvas.Width+2;x++)
                    Assert.That(pixels[(origin.y+y)*art.Sheet.Size.x+origin.x+x],Is.EqualTo(canvas.Get(math.clamp(x,0,canvas.Width-1),math.clamp(y,0,canvas.Height-1))));
            }
        }
#endif
    }
}
