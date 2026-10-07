using SPF.Presentation.Sprites;
using SPF.Contracts.Weapons;
using Unity.Mathematics;
using UnityEngine;

namespace SPF.Presentation.Animation
{
    /// <summary>Original outlined adventure weapons, authored in the shared character atlas. Grip and
    /// muzzle pixels are explicit: changing a silhouette cannot silently move its gameplay socket.</summary>
    public static class WeaponArt
    {
        public const int Count=4,ExtraParts=5;
        public static bool Supported(int visualId)=>visualId>=1001&&visualId<=1004;
        /// <summary>New content may reuse a visual. Unregistered art falls back coherently by action family,
        /// including its matching pivot/size, rather than clamping an unknown ID onto an unrelated UV.</summary>
        public static int Resolve(int visualId,WeaponActionFamily family)=>Supported(visualId)?visualId:1000+math.clamp((int)family,1,4);
        public static int Index(int visualId)=>Supported(visualId)?visualId-1001:0;
        public static float TipPixels(int visualId)=>visualId==1001?math.sqrt(201*201+20*20):visualId==1003?157:198;
        public static float AngleOffset(int visualId)=>visualId==1001?-math.atan2(20,201):0;
        public static float2 Size(int visualId,float length)=>visualId==1004?new float2(.68f,1.40f):
            new float2(256,96)*(length/TipPixels(visualId));
        public static float2 Centre(int visualId,float length)=>visualId==1004?new float2(-.116875f,0):
            new float2((128-(visualId==1003?62:46))*length/TipPixels(visualId),0);
        public static PixelCanvas Draw(int kind)
        {
            const int sample=3;int width=kind==3?128:256,height=kind==3?256:96;
            var c=new PixelCanvas(width*sample,height*sample);
            var ink=new Color32(20,31,46,255);var gold=new Color32(230,158,66,255);
            var pale=new Color32(253,238,197,255);var steel=new Color32(133,205,219,255);
            var shadow=new Color32(49,101,125,255);var teal=new Color32(55,161,159,255);
            void Line(float x,float y,float xx,float yy,float w,Color32 col)=>c.Line(new float2(x,y)*sample,new float2(xx,yy)*sample,w*sample,col);
            void Ellipse(float x,float y,float rx,float ry,Color32 col)=>c.Ellipse(x*sample,y*sample,rx*sample,ry*sample,col);
            void Poly(Color32 col,params float[] xy)
            {
                for(int y=0;y<height*sample;y++)for(int x=0;x<width*sample;x++)
                {
                    bool inside=false;float px=(x+.5f)/sample,py=(y+.5f)/sample;
                    for(int i=0,j=xy.Length-2;i<xy.Length;j=i,i+=2)
                    {float ax=xy[i],ay=xy[i+1],bx=xy[j],by=xy[j+1];if((ay>py)!=(by>py)&&px<(bx-ax)*(py-ay)/(by-ay)+ax)inside=!inside;}
                    if(inside)c.Set(x,y,col);
                }
            }
            if(kind<2)
            {
                Line(13,48,77,48,22,ink);Line(16,48,74,48,14,shadow);
                for(int x=23;x<61;x+=10)Line(x,43,x+4,53,3,gold);
                Ellipse(15,48,10,11,ink);Ellipse(15,48,6,7,gold);Ellipse(14,50,3,3,pale);
                if(kind==0)
                {
                    // A broad forward-curved saber, with an asymmetric cut edge and thick spine.
                    Poly(ink,66,39,165,35,214,44,247,68,227,66,193,59,71,63);
                    Poly(steel,72,43,164,40,211,48,235,62,196,54,72,58);
                    Poly(pale,79,56,191,53,235,62,226,65,193,59,76,62);
                    Line(79,46,170,43,3,shadow);Line(70,27,65,70,15,ink);Line(70,29,66,68,9,gold);
                    Ellipse(69,48,8,8,ink);Ellipse(69,48,4,4,teal);
                }
                else
                {
                    // Long straight double edge and faceted fuller make the thrust sword distinct.
                    Poly(ink,70,33,213,35,244,48,213,62,70,64);
                    Poly(steel,77,39,212,40,240,48,211,56,77,58);
                    Poly(pale,77,40,211,41,240,48,80,48);Poly(shadow,82,50,233,49,210,56,81,58);
                    Line(69,19,69,77,14,ink);Line(69,23,69,73,8,gold);Ellipse(69,48,11,10,ink);Ellipse(69,48,7,6,teal);
                }
            }
            else if(kind==2)
            {
                Line(12,48,229,48,18,ink);Line(15,48,220,48,10,shadow);Line(19,52,203,52,3,gold);
                for(int x=35;x<98;x+=10)Line(x,44,x+4,52,4,teal);
                Poly(ink,185,42,204,14,231,15,250,37,246,67,221,82,201,67);
                Poly(gold,192,43,207,20,229,21,244,40,240,64,220,75,206,63);
                Poly(ink,204,38,221,24,238,39,235,61,218,68,204,55);
                Poly(teal,209,39,222,30,234,41,231,57,219,63,209,54);
                Poly(new Color32(128,248,223,255),211,39,222,32,223,49,211,55);
                Ellipse(219,44,5,6,pale);Line(191,47,203,47,5,gold);
            }
            else
            {
                // Recurve outline; the string is live geometry, never painted into this frame.
                float lastX=35,lastY=17;
                for(int i=1;i<=24;i++)
                {
                    float t=i/24f,y=17+t*222,x=35+51*math.sin(math.PI*t)-17*math.sin(3*math.PI*t);
                    Line(lastX,lastY,x,y,15,ink);lastX=x;lastY=y;
                }
                lastX=35;lastY=17;
                for(int i=1;i<=24;i++)
                {
                    float t=i/24f,y=17+t*222,x=35+51*math.sin(math.PI*t)-17*math.sin(3*math.PI*t);
                    Line(lastX,lastY,x,y,8,gold);Line(lastX+1,lastY+1,x+1,y+1,3,pale);lastX=x;lastY=y;
                }
                Line(86,112,86,144,17,ink);Line(86,114,86,142,10,teal);
                for(int y=116;y<143;y+=7)Line(81,y,90,y+2,3,pale);
                Ellipse(36,17,7,7,ink);Ellipse(36,17,3,3,teal);Ellipse(36,239,7,7,ink);Ellipse(36,239,3,3,teal);
            }
            return SmoothSpriteArt.Downsample(c,sample);
        }
    }
}
