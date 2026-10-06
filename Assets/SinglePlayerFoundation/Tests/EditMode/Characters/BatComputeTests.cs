using System;
using System.Runtime.InteropServices;
using NUnit.Framework;
using SPF.Presentation.Characters;
using Unity.Mathematics;

namespace SPF.Tests.EditMode
{
    public class BatComputeTests
    {
        [Test]
        public void ThreeMatrixPaletteAbiIsExactlyNinetySixBytes()
        {
            Assert.That(Marshal.SizeOf(typeof(BatPalette)),Is.EqualTo(BatPalette.Stride));
            Assert.That(BatPalette.Stride,Is.EqualTo(96));
            Assert.That(Marshal.OffsetOf(typeof(BatPalette),nameof(BatPalette.Root)).ToInt32(),Is.Zero);
            Assert.That(Marshal.OffsetOf(typeof(BatPalette),nameof(BatPalette.Upper)).ToInt32(),Is.EqualTo(32));
            Assert.That(Marshal.OffsetOf(typeof(BatPalette),nameof(BatPalette.Lower)).ToInt32(),Is.EqualTo(64));
        }

        [TestCase(BatPrecision.Float)][TestCase(BatPrecision.Half)]
        public void PerInstancePaletteMatchesIndependentPerVertexReference(BatPrecision precision)
        {
            using var asset=BatCharacterAsset.Bake();
            var targets=new[]{asset.IkShape.xy,new float2(.7f,1.8f),new float2(3,3),new float2(-3,-2)};
            for(int clip=0;clip<2;clip++)for(int facing=-1;facing<=1;facing+=2)for(int bend=-1;bend<=1;bend+=2)
            for(int mode=0;mode<=targets.Length;mode++)for(int sample=0;sample<3;sample++)
            {
                var instance=asset.Instance(clip,-.003f,new float2(2,-3),1.7f,facing,new float4(1),.125f,
                    mode==0?default:targets[mode-1],mode!=0,bend);
                if(sample>0)instance.Frames=new float4(17,asset.Clip(1).FirstFrame+29,sample*.37f,.125f);
                var palette=asset.SamplePalette(instance,precision);
                for(int v=0;v<asset.VertexCount;v++)
                    Assert.That(math.distance(asset.Skin(v,instance,precision),BatSkinning.SkinFromPalette(asset.Vertex(v),instance,palette)),Is.LessThan(1e-6f));
                Assert.That(math.distance(palette.Root.Row0,asset.Row((int)instance.Frames.x,0,precision).Row0),Is.LessThan(1e-6f));
            }
        }

        [Test]
        public void ComputeIsOptInAndForceCpuAlwaysWins()
        {
            var c=Supported();
            Assert.That(c.Select(false,false,120,256,out _),Is.EqualTo(BatBackend.GpuVertex));
            Assert.That(c.Select(false,false,120,256,true,out _),Is.EqualTo(BatBackend.GpuComputePalette));
            Assert.That(c.Select(true,false,120,256,true,out _),Is.EqualTo(BatBackend.CpuWeighted));
            c.Shader=false;
            Assert.That(c.Select(false,false,120,256,true,out _),Is.EqualTo(BatBackend.GpuComputePalette),"Compute rendering has an independent shader gate.");
        }

        [TestCase("compute")][TestCase("renderShader")][TestCase("kernel")][TestCase("vertexBuffers")]
        [TestCase("computeBuffers")][TestCase("groupSize")][TestCase("paletteBuffer")]
        public void MissingComputeGateFallsBackToVertexThenCpu(string missing)
        {
            var c=Supported();
            switch(missing)
            {
                case "compute":c.Compute=false;break;
                case "renderShader":c.ComputeRenderShader=false;break;
                case "kernel":c.ComputeKernel=false;break;
                case "vertexBuffers":c.VertexBuffers=1;break;
                case "computeBuffers":c.ComputeBuffers=1;break;
                case "groupSize":c.ComputeGroupSize=63;break;
                case "paletteBuffer":c.MaxBufferBytes=256*BatPalette.Stride-1;break;
                default:throw new ArgumentException(missing);
            }
            Assert.That(c.Select(false,false,120,256,true,out _),Is.EqualTo(BatBackend.GpuVertex));
            c.Shader=false;
            Assert.That(c.Select(false,false,120,256,true,out _),Is.EqualTo(BatBackend.CpuWeighted));
        }

        [TestCase("graphics")][TestCase("api")][TestCase("instancing")][TestCase("shaderLevel")]
        [TestCase("format")][TestCase("texture")][TestCase("instanceBuffer")][TestCase("noVertexBuffer")]
        public void MissingSharedGateForcesCpuEvenWhenComputeIsSupported(string missing)
        {
            var c=Supported();
            switch(missing)
            {
                case "graphics":c.Graphics=false;break;
                case "api":c.SupportedApi=false;break;
                case "instancing":c.Instancing=false;break;
                case "shaderLevel":c.ShaderLevel=44;break;
                case "format":c.FloatSample=false;break;
                case "texture":c.MaxTextureSize=119;break;
                case "instanceBuffer":c.MaxBufferBytes=256*BatInstance.Stride-1;break;
                case "noVertexBuffer":c.VertexBuffers=0;break;
                default:throw new ArgumentException(missing);
            }
            Assert.That(c.Select(false,false,120,256,true,out _),Is.EqualTo(BatBackend.CpuWeighted));
        }

        [Test]
        public void AcceptedHalfAndResourceBoundariesRemainExplicit()
        {
            var c=Supported();c.FloatSample=false;
            Assert.That(c.Select(false,true,120,256,true,out var precision),Is.EqualTo(BatBackend.GpuComputePalette));
            Assert.That(precision,Is.EqualTo(BatPrecision.Half));
            c.HalfSample=false;
            Assert.That(c.Select(false,true,120,256,true,out _),Is.EqualTo(BatBackend.CpuWeighted));
            Assert.Throws<ArgumentOutOfRangeException>(()=>c.Select(false,false,0,1,true,out _));
            Assert.Throws<ArgumentOutOfRangeException>(()=>c.Select(false,false,121,1,true,out _));
            Assert.Throws<ArgumentOutOfRangeException>(()=>c.Select(false,false,120,0,true,out _));
            Assert.Throws<ArgumentOutOfRangeException>(()=>c.Select(false,false,120,257,true,out _));
        }
        static BatCapabilities Supported()=>new BatCapabilities
        {
            Graphics=true,SupportedApi=true,Shader=true,Instancing=true,HalfSample=true,FloatSample=true,
            ShaderLevel=45,VertexBuffers=2,MaxTextureSize=120,MaxBufferBytes=256*BatPalette.Stride,
            Compute=true,ComputeRenderShader=true,ComputeKernel=true,ComputeBuffers=2,ComputeGroupSize=64
        };
    }
}
