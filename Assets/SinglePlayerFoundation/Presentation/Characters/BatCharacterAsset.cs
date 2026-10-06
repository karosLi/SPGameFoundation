using System.Collections.Generic;
using SPF.L1.Skeleton;
using Unity.Mathematics;

namespace SPF.Presentation.Characters
{
    /// <summary>Original smooth geometric character. Arm rings include real 50/50 upper/lower influences.</summary>
    public static class BatCharacterAsset
    {
        public static SkeletonAsset CreateRig()
        {
            var b=new SkeletonAsset.Builder().Bone("root",null,0,0,0).Bone("upper","root",new float2(.2f,1.4f),0,.6f).Bone("lower","upper",new float2(.6f,0),0,.5f);
            b.Clip("wave",1,true).Key("upper",0,-40).Key("upper",.25f,35).Key("upper",.5f,15).Key("upper",.75f,-25)
                .Key("lower",0,25).Key("lower",.25f,75).Key("lower",.5f,130).Key("lower",.75f,60);
            b.Clip("reach",1,true).Key("upper",0,-65).Key("upper",.5f,45).Key("lower",0,100).Key("lower",.5f,-65);
            return b.Build();
        }
        public static BatClipSet Bake(int framesPerClip=60)
        {
            using var rig=CreateRig(); CreateMesh(out var vertices,out var indices); return BatBaker.Bake(rig.View,vertices,indices,framesPerClip);
        }
        public static void CreateMesh(out BatVertex[] vertices,out int[] triangles)
        {
            var v=new List<BatVertex>();var t=new List<int>();
            Ellipse(v,t,new float2(0,.98f),new float2(.29f,.48f),16,new float4(.16f,.52f,.62f,1));
            Ellipse(v,t,new float2(0,1.76f),new float2(.25f,.28f),16,new float4(1,.79f,.49f,1));
            Ellipse(v,t,new float2(-.15f,.3f),new float2(.11f,.34f),12,new float4(.12f,.22f,.36f,1));
            Ellipse(v,t,new float2(.15f,.3f),new float2(.11f,.34f),12,new float4(.12f,.22f,.36f,1));
            Ellipse(v,t,new float2(.10f,1.81f),new float2(.035f,.05f),8,new float4(.08f,.12f,.2f,1));
            int start=v.Count;
            for(int ring=0;ring<=12;ring++)
            {
                float x=.2f+1.1f*ring/12;
                float lower=math.saturate((x-.64f)/.32f);
                // The center ring is exactly 50/50 at the elbow.
                if(ring==6){x=.8f;lower=.5f;}
                float width=ring==12?.065f:.095f;
                for(int side=0;side<2;side++)v.Add(new BatVertex { Position=new float2(x,1.4f+(side==0?-width:width)),Uv=new float2(.5f,.5f),Skin=new float4(1,2,1-lower,lower),Color=new float4(1,.79f,.49f,1) });
                if(ring>0){int a=start+(ring-1)*2;t.Add(a);t.Add(a+2);t.Add(a+1);t.Add(a+1);t.Add(a+2);t.Add(a+3);}
            }
            vertices=v.ToArray();triangles=t.ToArray();
        }
        static void Ellipse(List<BatVertex> v,List<int> t,float2 center,float2 size,int sides,float4 color)
        {
            int start=v.Count;
            v.Add(new BatVertex { Position=center,Uv=new float2(.5f),Skin=new float4(0,0,1,0),Color=color });
            for(int i=0;i<sides;i++)
            {
                float a=i*2*math.PI/sides;
                v.Add(new BatVertex { Position=center+new float2(math.cos(a),math.sin(a))*size,Uv=new float2(.5f),Skin=new float4(0,0,1,0),Color=color });
                t.Add(start);t.Add(start+1+i);t.Add(start+1+(i+1)%sides);
            }
        }
    }
}
