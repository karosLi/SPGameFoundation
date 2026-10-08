// Frozen test oracle from f46c7651193186126bcaa9f3f6537002293b7e2c; do not update with candidate art.
// Only namespace relocation and type-import directives differ from the original source.
using SPF.Presentation.Animation;
using System;
using SPF.Presentation.Sprites;
using Unity.Mathematics;
using UnityEngine;

namespace SPF.Tests.EditMode.FrozenBladeArtF46c765
{
    /// <summary>Original sanctuary sentinels and coral raiders, in 3x supersampled cutout art. Authored at load time; no generated textures in the pose loop.</summary>
    public sealed class NaturalCharacterArt : IDisposable
    {
        public const int Parts = 14;
        public readonly PixelCanvas[][] Canvases = new PixelCanvas[2][];
        public readonly BoneAttachment[][] Attachments = new BoneAttachment[2][];
        public SpriteSheet Sheet { get; private set; }
        public int White, Disc, Halo, Blob;
        public readonly int[] WeaponFrames=new int[WeaponArt.Count];
        static readonly Color32 Ink=new Color32(27,43,46,255), Cream=new Color32(250,239,209,255), Gold=new Color32(196,150,78,255);
        public NaturalCharacterArt(bool includeWeapons=false)
        {
            var builder=new SpriteAtlasBuilder();
            var frames=new int[2][];
            for(int kind=0;kind<2;kind++)
            {
                Canvases[kind]=new PixelCanvas[8];frames[kind]=new int[8];
                for(int part=0;part<8;part++) { Canvases[kind][part]=Draw(kind,part);frames[kind][part]=builder.Add(Canvases[kind][part]); }
            }
            if(includeWeapons)for(int k=0;k<WeaponArt.Count;k++)WeaponFrames[k]=builder.Add(WeaponArt.Draw(k));
            var white=new PixelCanvas(4,4);white.Rect(0,0,4,4,new Color32(255,255,255,255));White=builder.Add(white);
            var disc=new PixelCanvas(48,48);disc.Ellipse(24,24,22,22,new Color32(255,255,255,255));Disc=builder.Add(SmoothSpriteArt.Downsample(disc,2));
            Halo=builder.Add(BlobShadow.CreateCanvas(64,64));Blob=builder.Add(BlobShadow.CreateCanvas());
            Sheet=builder.Build(1024,FilterMode.Bilinear,2,true);
            for(int k=0;k<2;k++) Attachments[k]=BuildAttachments(frames[k],k);
        }

        BoneAttachment[] BuildAttachments(int[] f,int kind)
        {
            var a=new BoneAttachment[Parts];
            // Explicit back-to-front order for translucent edge filtering. No per-actor renderer objects.
            a[0]=Part(NaturalCharacterRig.FarArm,f[3],new float2(.22f,0),new float2(.56f,.34f),0,.68f);
            a[1]=Part(NaturalCharacterRig.FarForearm,f[4],new float2(.22f,0),new float2(.56f,.35f),1,.68f);
            a[2]=Part(NaturalCharacterRig.FarThigh,f[5],new float2(.28f,0),new float2(.69f,.40f),2,.7f);
            a[3]=Part(NaturalCharacterRig.FarShin,f[6],new float2(.275f,0),new float2(.65f,.34f),3,.7f);
            a[4]=Part(NaturalCharacterRig.FarFoot,f[7],new float2(.10f,-.005f),new float2(.46f,.27f),4,.7f);
            a[5]=Part(NaturalCharacterRig.Pelvis,f[0],new float2(0,.03f),new float2(.78f,.50f),5,1);
            a[6]=Part(NaturalCharacterRig.NearThigh,f[5],new float2(.28f,0),new float2(.69f,.40f),6,1);
            a[7]=Part(NaturalCharacterRig.NearShin,f[6],new float2(.275f,0),new float2(.65f,.34f),7,1);
            a[8]=Part(NaturalCharacterRig.NearFoot,f[7],new float2(.10f,-.005f),new float2(.46f,.27f),8,1);
            a[9]=Part(NaturalCharacterRig.Torso,f[1],new float2(0,.31f),new float2(kind==0?1.06f:1.17f,.98f),9,1);
            a[10]=Part(NaturalCharacterRig.Head,f[2],new float2(.05f,.23f),new float2(.90f,.92f),10,1);
            a[11]=Part(NaturalCharacterRig.NearArm,f[3],new float2(.22f,0),new float2(.56f,.38f),11,1);
            a[12]=Part(NaturalCharacterRig.NearForearm,f[4],new float2(.22f,0),new float2(.56f,.36f),12,1);
            a[13]=Part(NaturalCharacterRig.Hand,f[0],new float2(.055f,0),new float2(.19f,.19f),13,1);
            return a;
        }
        BoneAttachment Part(int bone,int frame,float2 offset,float2 size,int layer,float shade)=>new BoneAttachment
        {Bone=bone,Uv=Sheet[frame].Uv,Offset=offset,Size=size,Layer=layer,Tint=new float4(shade,shade,shade,1)};
        public static int CanvasForAttachment(int attachment)
        { switch(attachment){case 0:case 11:return 3;case 1:case 12:return 4;case 2:case 6:return 5;case 3:case 7:return 6;case 4:case 8:return 7;case 5:case 13:return 0;case 9:return 1;default:return 2;} }
        public void Dispose()=>Sheet?.Dispose();

        static PixelCanvas Draw(int kind,int part)
        {
            const int s=3;var c=new PixelCanvas(96*s,96*s);
            Color32 main=kind==0?new Color32(224,217,190,255):new Color32(190,102,89,255);
            Color32 light=kind==0?new Color32(253,246,220,255):new Color32(244,166,123,255);
            Color32 dark=kind==0?new Color32(44,80,83,255):new Color32(91,48,56,255);
            Color32 accent=kind==0?Gold:new Color32(151,64,65,255);
            void E(float x,float y,float rx,float ry,Color32 col)=>c.Ellipse(x*s,y*s,rx*s,ry*s,col);
            void L(float x,float y,float xx,float yy,float w,Color32 col)=>c.Line(new float2(x*s,y*s),new float2(xx*s,yy*s),w*s,col);
            void P(Color32 col,params float[] pts)
            {
                for(int y=0;y<96*s;y++)for(int x=0;x<96*s;x++)
                {
                    bool inside=false;float px=(x+.5f)/s,py=(y+.5f)/s;
                    for(int i=0,j=pts.Length-2;i<pts.Length;j=i,i+=2)
                    {float ax=pts[i],ay=pts[i+1],bx=pts[j],by=pts[j+1];if(((ay>py)!=(by>py))&&px<(bx-ax)*(py-ay)/(by-ay)+ax)inside=!inside;}
                    if(inside)c.Set(x,y,col);
                }
            }
            if(part==0)
            {
                E(47,49,36,29,Ink);E(47,51,32,26,dark);L(23,57,72,57,15,main);L(24,63,66,63,4,light);E(60,54,9,11,accent);E(60,55,4,6,Cream);
            }
            else if(part==1)
            {
                // A visible overlapping neck keeps the cutout head attached under body/head counter-rotation.
                L(49,76,51,88,14,Ink);L(49,78,51,88,9,main);
                if(kind==0)
                {
                    P(Ink,21,24,16,66,29,85,64,86,81,69,73,23,56,11,35,13);
                    P(main,24,28,23,63,32,78,63,79,74,66,68,27,55,17,37,18);
                    P(main,24,61,33,78,61,79,72,65,61,39,34,43);
                    P(light,27,62,33,76,59,78,61,73,34,71,30,58);
                    P(accent,30,68,35,70,65,32,60,27);L(30,28,65,28,5,accent);
                    E(52,58,10,11,Ink);E(52,59,7,8,Gold);P(new Color32(90,217,194,255),52,66,57,59,52,53,47,59);
                    // Overlapping ceramic breastplate, teal cloth skirt and fine hammered-bronze seams.
                    P(dark,23,43,31,46,39,24,34,16,24,22);P(dark,62,42,71,44,70,22,59,17,56,25);
                    L(33,77,58,79,1.6f,Cream);L(27,47,32,30,1.6f,Gold);L(66,46,64,30,1.6f,Gold);
                    E(35,64,2,2,Gold);E(67,66,2,2,Gold);
                    L(69,59,65,42,4,light);
                }
                else
                {
                    P(Cream,18,55,4,70,22,68,13,84,34,74,46,89,53,70,73,81,73,57);
                    E(47,47,35,35,Ink);E(46,48,31,32,dark);E(46,56,30,28,accent);
                    P(new Color32(239,153,107,255),23,62,36,80,61,75,55,59,32,49);
                    P(main,31,44,64,52,68,27,50,15,31,22);L(35,27,56,30,4,light);
                    L(42,68,50,55,3,Ink);L(24,59,37,53,3,Ink);L(60,73,66,59,3,Ink);
                }
            }
            else if(part==2)
            {
                if(kind==0)
                {
                    E(46,48,29,34,Ink);E(44,52,25,29,dark);E(42,58,24,24,main);
                    P(light,22,62,30,77,47,81,60,73,39,76,28,60);
                    P(Ink,31,55,75,57,77,42,61,35,33,40);P(new Color32(125,231,222,255),40,52,72,52,70,47,45,46);
                    E(27,46,10,13,Ink);E(27,47,7,9,Gold);E(27,48,3,5,Cream);
                    P(dark,41,33,62,33,66,23,49,17,33,28);L(48,29,61,29,3,light);
                    P(accent,27,80,42,87,51,81,43,72);
                }
                else
                {
                    P(Ink,19,52,8,86,19,90,37,66,64,72,77,92,85,87,77,50);
                    P(Cream,20,57,12,87,17,84,33,58,65,64,79,88,76,61);
                    E(47,45,30,29,Ink);E(48,46,27,25,main);
                    P(accent,23,59,41,71,64,63,77,53,64,49,48,54,30,48);
                    P(Ink,42,47,70,48,66,38,49,38);P(Gold,50,44,67,45,63,40,53,40);E(60,43,2,4,Ink);
                    P(light,40,32,70,39,75,27,58,16,35,24);P(Ink,44,30,69,32,66,25,49,24);
                    P(Cream,49,29,55,29,54,21);P(Cream,63,31,68,31,65,23);
                }
            }
            else if(part==7)
            {
                P(Ink,10,18,9,54,23,76,47,71,55,51,79,43,88,28,85,15);
                P(dark,16,25,15,51,25,68,42,64,50,43,75,37,81,27,78,22);
                P(main,18,49,26,68,40,63,48,43,32,40);L(23,61,39,58,4,light);L(18,23,80,23,6,accent);
            }
            else
            {
                // Rounded overlapping joints hide seams while preserving a clear limb taper.
                E(22,48,15,30,Ink);L(23,48,73,48,50,Ink);
                L(24,49,70,49,43,dark);L(25,55,66,55,31,main);L(27,64,63,64,6,light);
                if(part==3) {E(26,50,18,27,accent);E(23,57,12,16,kind==0?Cream:new Color32(238,158,112,255));}
                if(part==4||part==6) {P(kind==0?main:accent,49,28,71,28,80,44,74,64,55,64);L(55,59,72,59,3,Cream);L(53,30,73,32,2,Gold);L(59,38,65,49,2,kind==0?dark:light);}
                if(part==5&&kind==0){L(28,42,61,42,21,dark);L(31,52,58,52,3,Gold);}
                E(77,48,9,17,dark);L(78,43,78,55,3,light);
            }
            return SmoothSpriteArt.Downsample(c,s);
        }
    }
}
