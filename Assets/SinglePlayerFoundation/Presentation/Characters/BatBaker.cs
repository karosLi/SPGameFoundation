using System;
using SPF.L1.Skeleton;
using Unity.Collections;
using Unity.Mathematics;

namespace SPF.Presentation.Characters
{
    /// <summary>Loading-time deterministic baker for the explicitly bounded static-root three-bone asset.</summary>
    public static class BatBaker
    {
        public static BatClipSet Bake(in SkeletonView rig, BatVertex[] vertices, int[] triangles, int framesPerClip = 60)
        {
            Validate(rig,vertices,triangles,framesPerClip);
            int frames = checked(rig.Clips.Length * framesPerClip);
            var clips = new BatClip[rig.Clips.Length];
            var floats = new BatRows[checked(frames * BatLimits.Bones)];
            var halves = new BatRows[floats.Length];
            using var pose = new NativeArray<BoneLocal>(BatLimits.Bones,Allocator.Temp);
            using var world = new NativeArray<BoneWorld>(BatLimits.Bones,Allocator.Temp);
            var bind = new Affine2D[BatLimits.Bones];
            var writablePose = pose;
            for (int b=0;b<BatLimits.Bones;b++) writablePose[b] = new BoneLocal { Position=rig.Bones[b].Position,Rotation=rig.Bones[b].Rotation };
            Skeletal.ToWorld(rig,pose,0,1,1,world);
            for (int b=0;b<BatLimits.Bones;b++) bind[b]=Affine2D.FromBone(world[b],1);
            float radius = 0;
            for (int c=0;c<rig.Clips.Length;c++)
            {
                var info=rig.Clips[c];
                clips[c]=new BatClip { FirstFrame=c*framesPerClip,FrameCount=framesPerClip,Duration=info.Duration,Loop=info.Loop };
                for (int f=0;f<framesPerClip;f++)
                {
                    float time=framesPerClip==1?0:info.Duration*f/(info.Loop?framesPerClip:framesPerClip-1);
                    Skeletal.Sample(rig,c,time,pose); Skeletal.ToWorld(rig,pose,0,1,1,world);
                    for (int b=0;b<BatLimits.Bones;b++)
                    {
                        var posed=Affine2D.FromBone(world[b],1);
                        if (!BonePaletteMath.TrySkinningTransform(bind[b],posed,out var p)) throw new ArgumentException("Invalid bind inverse.");
                        int at=(c*framesPerClip+f)*BatLimits.Bones+b;
                        if (!math.all(math.isfinite(p.Row0)) || !math.all(math.isfinite(p.Row1))) throw new ArgumentException("Non-finite baked matrix.");
                        floats[at]=BatRows.From(p); halves[at]=Quantize(floats[at]);
                    }
                }
            }
            float error=0;
            // Weighted skinning and matrix lerp are linear; endpoint maximum bounds every interpolation fraction.
            // Check midpoint too as a regression guard against changing the interpolation implementation.
            for (int c=0;c<clips.Length;c++)
            for (int f=0;f<framesPerClip;f++)
            for (int sub=0;sub<3;sub++)
            {
                int next=math.min(f+1,framesPerClip-1); if(clips[c].Loop)next=(f+1)%framesPerClip;
                int a=(clips[c].FirstFrame+f)*BatLimits.Bones,b=(clips[c].FirstFrame+next)*BatLimits.Bones;
                float t=sub*.5f;
                foreach(var v in vertices)
                {
                    float2 p=Skin(floats,a,b,t,v),q=Skin(halves,a,b,t,v);
                    error=math.max(error,math.distance(p,q)); radius=math.max(radius,math.max(math.length(p),math.length(q)));
                }
            }
            float4 shape=new float4(rig.Bones[1].Position,rig.Bones[1].Length,rig.Bones[2].Length);
            // IK matrices can move a vertex beyond the endpoint by its distance from its associated bind origin.
            foreach(var v in vertices)
            for(int j=0;j<2;j++)
            {
                int bone=(int)(j==0?v.Skin.x:v.Skin.y);
                float2 origin=bone==2?shape.xy+new float2(shape.z,0):bone==1?shape.xy:float2.zero;
                float posedOriginBound=bone==2?math.length(shape.xy)+shape.z:bone==1?math.length(shape.xy):0;
                radius=math.max(radius,posedOriginBound+math.distance(v.Position,origin));
            }
            return new BatClipSet(vertices,triangles,clips,floats,halves,shape,error,radius+.01f);
        }
        static float2 Skin(BatRows[] rows,int a,int b,float t,BatVertex v)
        {
            int i=(int)v.Skin.x,j=(int)v.Skin.y;
            return v.Skin.z*BatRows.Lerp(rows[a+i],rows[b+i],t).Transform(v.Position)+v.Skin.w*BatRows.Lerp(rows[a+j],rows[b+j],t).Transform(v.Position);
        }
        static BatRows Quantize(BatRows p) => new BatRows { Row0=Quantize(p.Row0),Row1=Quantize(p.Row1) };
        static float4 Quantize(float4 p)
        {
            float4 q=math.f16tof32(math.f32tof16(p));
            // Float palette remains usable when half coefficients overflow. Never select that half palette.
            return q;
        }
        static bool Finite(float2 x)=>math.all(math.isfinite(x));
        static bool Finite(float4 x)=>math.all(math.isfinite(x));
        static void Validate(in SkeletonView rig,BatVertex[] vertices,int[] triangles,int frames)
        {
            if(!rig.Bones.IsCreated||!rig.Clips.IsCreated||!rig.Keys.IsCreated||!rig.Channels.IsCreated||rig.BoneCount!=BatLimits.Bones||rig.Clips.Length<1||rig.Clips.Length>BatLimits.MaxClips||rig.Channels.Length!=rig.Clips.Length*BatLimits.Bones||frames<1||frames>BatLimits.MaxFramesPerClip)
                throw new ArgumentException("BAT requires exactly three bones, one/two clips and 1..60 frames per clip.");
            for(int b=0;b<BatLimits.Bones;b++)
            {
                var bone=rig.Bones[b];
                if(bone.Parent!=b-1||!Finite(bone.Position)||!math.isfinite(bone.Rotation)||bone.Rotation!=0||!math.isfinite(bone.Length)||bone.Length<0||bone.Length>2||math.any(math.abs(bone.Position)>4))
                    throw new ArgumentException("BAT IK requires root->upper->lower, zero bind rotations and bounded finite bind positions/lengths.");
            }
            if(math.any(rig.Bones[0].Position!=0)||rig.Bones[1].Length<=.001f||rig.Bones[2].Length<=.001f||math.any(rig.Bones[2].Position!=new float2(rig.Bones[1].Length,0)))
                throw new ArgumentException("BAT IK requires a static identity root and lower offset (upper length,0).");
            for(int c=0;c<rig.Clips.Length;c++)
            {
                var clip=rig.Clips[c]; if(!math.isfinite(clip.Duration)||clip.Duration<=0)throw new ArgumentException("Clip duration must be finite and positive.");
                for(int b=0;b<BatLimits.Bones;b++)
                {
                    int2 channel=rig.Channels[c*BatLimits.Bones+b];
                    if(channel.x<0||channel.y<0||(long)channel.x+channel.y>rig.Keys.Length)throw new ArgumentException("Invalid channel range.");
                    float prior=-1;
                    for(int k=0;k<channel.y;k++)
                    {
                        var key=rig.Keys[channel.x+k];
                        if(!math.isfinite(key.Time)||key.Time<0||key.Time>clip.Duration||key.Time<=prior||!math.isfinite(key.Rotation)||!Finite(key.Offset)||math.any(key.Offset!=0)||(b==0&&key.Rotation!=0))
                            throw new ArgumentException("Keys must be ordered, finite, inside duration; root motion and animated offsets are unsupported.");
                        prior=key.Time;
                    }
                }
            }
            if(vertices==null||vertices.Length<3||vertices.Length>BatLimits.MaxVertices||triangles==null||triangles.Length<3||triangles.Length>BatLimits.MaxIndices||triangles.Length%3!=0)throw new ArgumentException("Invalid bounded weighted mesh.");
            foreach(var v in vertices)
            {
                if(!Finite(v.Position)||math.any(math.abs(v.Position)>8)||!Finite(v.Uv)||!Finite(v.Skin)||!Finite(v.Color)||math.any(v.Color<0)||math.any(v.Color>1)||
                    v.Skin.x<0||v.Skin.x>=3||v.Skin.y<0||v.Skin.y>=3||v.Skin.x!=math.floor(v.Skin.x)||v.Skin.y!=math.floor(v.Skin.y)||v.Skin.z<0||v.Skin.w<0||math.abs(v.Skin.z+v.Skin.w-1)>1e-6f)
                    throw new ArgumentException("Mesh requires finite bounded positions and two normalized nonnegative influences with integer bone IDs.");
            }
            foreach(int i in triangles)if(i<0||i>=vertices.Length)throw new ArgumentException("Triangle index outside mesh.");
        }
    }
}
