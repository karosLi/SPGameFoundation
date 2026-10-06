using System;
using SPF.L1.Skeleton;
using SPF.Presentation.Sprites;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace SPF.Presentation.Animation
{
    public enum PoseShadowQuality { None, Blob, BakedPose }

    /// <summary>Load-time input. Bones are root-relative, unit-scale, with the declared facing already applied.</summary>
    public sealed class PoseShadowSample
    {
        public int Kind;
        public float Facing;
        public BoneWorld[] Bones;
        public BoneAttachment[] Attachments;
        public PixelCanvas[] Parts; // One source mask per attachment, not an atlas crop.
    }
    public struct PoseShadowFrame
    {
        public int Index;
        public float2 Center, Size;
    }

    /// <summary>
    /// Small fixed-light, flat-ground silhouette library, NOT dynamic shadow mapping. Uses authored alpha
    /// masks, not a capsule approximation. 32 x 96x64 tiles in at most 512x512 RGBA32 (1 MiB GPU + readable
    /// CPU texture). Construction allocates; selection and rendering do not. No gameplay dependencies.
    /// </summary>
    public sealed class PoseSilhouetteShadow : IDisposable
    {
        public const int MaxSamples=32, TileWidth=96, TileHeight=64, MaxAtlasBytes=1024*1024;
        public const float MaxPoseError=.12f;
        public static readonly float2 Projection=new float2(.42f,-.24f);
        readonly PoseShadowSample[] m_Samples;
        readonly PoseShadowFrame[] m_Frames;
        public SpriteSheet Sheet { get; }
        public int SampleCount=>m_Samples.Length;
        public int TextureBytes=>Sheet.Size.x*Sheet.Size.y*4;
        public PoseShadowFrame Frame(int i)=>m_Frames[i];
        public static float2 Project(float2 p)=>new float2(p.x+p.y*Projection.x,p.y*Projection.y);
        public static float2 Unproject(float2 p)=>new float2(p.x-p.y/Projection.y*Projection.x,p.y/Projection.y);

        public PoseSilhouetteShadow(PoseShadowSample[] samples)
        {
            if(samples==null||samples.Length<1||samples.Length>MaxSamples)throw new ArgumentOutOfRangeException(nameof(samples));
            m_Samples=new PoseShadowSample[samples.Length];m_Frames=new PoseShadowFrame[samples.Length];
            var atlas=new SpriteAtlasBuilder();
            for(int i=0;i<samples.Length;i++)
            {
                var s=samples[i];
                if(s==null||s.Bones==null||s.Attachments==null||s.Parts==null||s.Attachments.Length!=s.Parts.Length||s.Attachments.Length==0||s.Attachments.Length>32||s.Bones.Length>32)
                    throw new ArgumentException("A sample requires 1..32 matching parts and at most 32 bones.",nameof(samples));
                // Freeze selection geometry: caller edits after baking cannot silently invalidate the envelope.
                var owned=new PoseShadowSample {Kind=s.Kind,Facing=s.Facing<0?-1:1,Bones=(BoneWorld[])s.Bones.Clone(),Attachments=(BoneAttachment[])s.Attachments.Clone()};
                for(int a=0;a<s.Attachments.Length;a++)
                    if(s.Attachments[a].Bone<0||s.Attachments[a].Bone>=s.Bones.Length||s.Parts[a]==null)
                        throw new ArgumentException("Invalid bone index or missing source mask.",nameof(samples));
                m_Samples[i]=owned;
                m_Frames[i]=BakeFrame(s,out var canvas);m_Frames[i].Index=atlas.Add(canvas);
            }
            Sheet=atlas.Build(512,FilterMode.Bilinear,2,true);
            if(TextureBytes>MaxAtlasBytes){Sheet.Dispose();throw new InvalidOperationException("Pose shadow atlas exceeded its explicit memory budget.");}
        }

        static void ValidateSample(PoseShadowSample sample)
        {
            if(sample==null||sample.Bones==null||sample.Attachments==null||sample.Parts==null||sample.Attachments.Length<1||sample.Attachments.Length>32||sample.Bones.Length<1||sample.Bones.Length>32||sample.Parts.Length!=sample.Attachments.Length||!math.isfinite(sample.Facing))
                throw new ArgumentException("A shadow sample needs finite facing, 1..32 bones and matching 1..32 parts.");
            for(int a=0;a<sample.Attachments.Length;a++)
            {
                var part=sample.Attachments[a];
                if(part.Bone<0||part.Bone>=sample.Bones.Length||!math.all(math.isfinite(part.Size))||math.any(part.Size<=0)||!math.all(math.isfinite(part.Offset))||!math.isfinite(part.Rotation)||sample.Parts[a]==null)
                    throw new ArgumentException("Shadow parts require valid bones, finite positive sizes, transforms, and readable masks.");
                var bone=sample.Bones[part.Bone];
                if(!math.all(math.isfinite(bone.Position))||!math.isfinite(bone.Rotation))throw new ArgumentException("Shadow bones must be finite.");
            }
        }

        static float2 Corner(in BoneWorld bone,in BoneAttachment part,float facing,int corner)
        {
            float2 local=part.Size*.5f*new float2((corner&1)==0?-1:1,(corner&2)==0?-1:1);
            float a=part.Rotation;math.sincos(a,out float s,out float c);
            return bone.Transform(part.Offset+new float2(local.x*c-local.y*s,local.x*s+local.y*c),facing);
        }
        public static PoseShadowFrame BakeFrame(PoseShadowSample sample,out PixelCanvas canvas)
        {
            ValidateSample(sample);
            float2 lo=new float2(float.MaxValue),hi=new float2(float.MinValue);
            for(int a=0;a<sample.Attachments.Length;a++)for(int k=0;k<4;k++)
            {float2 p=Project(Corner(sample.Bones[sample.Attachments[a].Bone],sample.Attachments[a],sample.Facing,k));lo=math.min(lo,p);hi=math.max(hi,p);}
            // Three texel gutter inside the content rectangle, in addition to atlas extrusion.
            float2 raw=math.max(hi-lo,.02f);float2 pad=raw/new float2(TileWidth-8,TileHeight-8)*4;lo-=pad;hi+=pad;
            float2 size=hi-lo;
            canvas=new PixelCanvas(TileWidth,TileHeight);
            var coverage=new byte[TileWidth*TileHeight];
            for(int y=0;y<TileHeight;y++)for(int x=0;x<TileWidth;x++)
            {
                float alpha=0;
                for(int sy=0;sy<2;sy++)for(int sx=0;sx<2;sx++)
                {
                    float2 point=Unproject(lo+size*new float2((x+(sx+.5f)*.5f)/TileWidth,(y+(sy+.5f)*.5f)/TileHeight));float mask=0;
                    for(int a=0;a<sample.Attachments.Length;a++)
                    {
                        var part=sample.Attachments[a];var bone=sample.Bones[part.Bone];
                        float2 center=bone.Transform(part.Offset,sample.Facing);
                        float rotation=bone.Rotation+part.Rotation*(sample.Facing<0?-1:1);
                        math.sincos(rotation,out float s,out float c);float2 d=point-center;
                        float2 uv=new float2(d.x*c+d.y*s,(-d.x*s+d.y*c)*(sample.Facing<0?-1:1))/part.Size+.5f;
                        if(math.any(uv<0)||math.any(uv>=1))continue;
                        var src=sample.Parts[a];mask=math.max(mask,src.Get((int)(uv.x*src.Width),(int)(uv.y*src.Height)).a);
                    }
                    alpha+=mask*.25f;
                }
                coverage[y*TileWidth+x]=(byte)math.clamp(alpha,0,255);
            }
            // Small fixed kernel at bake time: a soft edge with no per-frame blur/render target.
            for(int y=1;y<TileHeight-1;y++)for(int x=1;x<TileWidth-1;x++)
            {
                int sum=0;for(int dy=-1;dy<=1;dy++)for(int dx=-1;dx<=1;dx++)sum+=coverage[(y+dy)*TileWidth+x+dx];
                canvas.Pixels[y*TileWidth+x]=new Color32(255,255,255,(byte)(sum/9));
            }
            return new PoseShadowFrame {Center=(lo+hi)*.5f,Size=size};
        }

        /// <summary>
        /// A dynamic IK request ALWAYS uses a blob. Otherwise, accept only a sampled pose whose every
        /// attachment corner is within .12 model units of the current pose, including head/foot rotation.
        /// Facing, rig kind, and topology must match. This bounds approximation and avoids promising
        /// arbitrary IK correctness. The caller must fall back when false is returned.
        /// </summary>
        public bool TrySelect(PoseShadowQuality quality,int kind,NativeArray<BoneWorld> world,int at,float2 root,float facing,float scale,bool dynamicIk,out PoseShadowFrame frame)
        {
            frame=default;if(quality!=PoseShadowQuality.BakedPose||dynamicIk||scale<=0||!math.isfinite(scale)||!math.all(math.isfinite(root))||!math.isfinite(facing))return false;
            int best=-1;float bestError=MaxPoseError*MaxPoseError;
            for(int i=0;i<m_Samples.Length;i++)
            {
                var sample=m_Samples[i];if(sample.Kind!=kind||(sample.Facing<0)!=(facing<0)||at<0||at+sample.Bones.Length>world.Length)continue;
                float error=0;
                for(int a=0;a<sample.Attachments.Length&&error<=bestError;a++)
                {
                    var part=sample.Attachments[a];var current=world[at+part.Bone];
                    if(!math.all(math.isfinite(current.Position))||!math.isfinite(current.Rotation)){error=float.PositiveInfinity;break;}
                    current.Position=(current.Position-root)/scale;
                    for(int k=0;k<4;k++)error=math.max(error,math.lengthsq(Corner(current,part,facing,k)-Corner(sample.Bones[part.Bone],part,facing,k)));
                }
                if(error<=bestError){best=i;bestError=error;}
            }
            if(best<0)return false;frame=m_Frames[best];return true;
        }
        public void Dispose()=>Sheet?.Dispose();
    }
}
