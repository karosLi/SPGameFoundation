using System;
using SPF.Presentation.Sprites;
using SPF.Presentation.Combat;
using Unity.Mathematics;
using UnityEngine;

namespace ShooterFoundation.Presentation
{
    /// <summary>Original vector-like placeholder art, rasterized at 4x and alpha-aware downsampled. No external/IP assets.</summary>
    public sealed class ShooterArt : IDisposable
    {
        public SpriteSheet Sheet;
        public CombatVfxArt CombatFx;
        public int Plane, Wing, Drone, Heavy, Bolt, Orb, Coin, Repair, Ring, White, Shadow, Cloud;
        static readonly Color32 Ink = C(17, 30, 51), Ivory = C(242, 241, 223), Teal = C(44, 202, 191), Coral = C(243, 99, 95);
        static Color32 C(byte r, byte g, byte b, byte a = 255) => new Color32(r, g, b, a);
        public static ShooterArt Build()
        {
            var a = new ShooterArt(); var atlas = new SpriteAtlasBuilder();
            a.Plane = Add(atlas, 96, c => Aircraft(c, false)); a.Wing = Add(atlas, 96, c => Aircraft(c, true));
            a.Drone = Add(atlas, 80, c => Enemy(c, false)); a.Heavy = Add(atlas, 80, c => Enemy(c, true));
            a.Bolt = Add(atlas, 32, c => { Line(c, 16, 5, 16, 27, 9, C(59, 203, 241)); Line(c, 16, 6, 16, 26, 4, C(227, 253, 247)); });
            a.Orb = Add(atlas, 32, c => { Ellipse(c,16,16,13,13,Ink); Ellipse(c,16,16,10.5f,10.5f,Coral); Ellipse(c,13,20,4,4,C(255,226,182)); });
            a.Coin = Add(atlas, 48, c => { Ellipse(c,24,24,20,20,Ink); Ellipse(c,24,24,17,17,C(247,181,63)); PaintRing(c,24,24,12,14,C(255,230,151)); Line(c,24,17,24,31,4,C(160,99,45)); });
            a.Repair = Add(atlas, 48, c => { Ellipse(c,24,24,21,21,Ink); Ellipse(c,24,24,18,18,C(103,224,177)); Line(c,24,14,24,34,7,Ivory); Line(c,14,24,34,24,7,Ivory); });
            a.Ring = Add(atlas, 64, c => PaintRing(c,32,32,26,30,C(255,255,255)));
            a.White = atlas.Add(new[] { C(255,255,255), C(255,255,255), C(255,255,255), C(255,255,255) },2,2,"white");
            a.Shadow = atlas.Add(BlobShadow.CreateCanvas());
            a.Cloud = Add(atlas, 96, c => { Ellipse(c,34,43,28,18,C(127,178,205,70)); Ellipse(c,57,53,30,22,C(127,178,205,70)); Ellipse(c,73,40,16,16,C(127,178,205,70)); });
            a.CombatFx = CombatVfxArt.AddTo(atlas);
            a.Sheet = atlas.Build(1024, FilterMode.Bilinear, 2, true); return a;
        }
        static int Add(SpriteAtlasBuilder atlas, int size, Action<PixelCanvas> draw) { var c = new PixelCanvas(size*4,size*4); draw(c); return atlas.Add(SmoothSpriteArt.Downsample(c,4)); }
        static void Ellipse(PixelCanvas c,float x,float y,float rx,float ry,Color32 color) => c.Ellipse(x*4,y*4,rx*4,ry*4,color);
        static void Line(PixelCanvas c,float x,float y,float xx,float yy,float width,Color32 color) => c.Line(new float2(x,y)*4,new float2(xx,yy)*4,width*4,color);
        static void PaintRing(PixelCanvas c,float x,float y,float r0,float r1,Color32 color) => c.Ring(x*4,y*4,r0*4,r1*4,color);
        static void Aircraft(PixelCanvas c,bool wing)
        {
            Color32 body = wing ? Teal : Ivory, panel = wing ? Ivory : Teal;
            // Swept wings and tail, each shape has a deliberate broad dark outline.
            Line(c,15,42,48,57,16,Ink); Line(c,48,57,81,42,16,Ink);
            Line(c,15,42,48,57,10,body); Line(c,48,57,81,42,10,body);
            Line(c,29,17,48,25,12,Ink); Line(c,48,25,67,17,12,Ink);
            Line(c,29,17,48,25,7,panel); Line(c,48,25,67,17,7,panel);
            Ellipse(c,48,48,14,36,Ink); Ellipse(c,48,49,10.5f,32,body);
            Ellipse(c,48,59,7.8f,14,Ink); Ellipse(c,48,61,5.5f,10.5f,C(81,157,204));
            Line(c,46,62,46,66,2.5f,C(208,247,255));
            Line(c,26,45,26,52,4,panel); Line(c,70,45,70,52,4,panel);
            Line(c,45,19,45,27,3,Ink); Line(c,51,19,51,27,3,Ink);
            // Airframe panel shading, ceramic highlights, brass hardpoints and engine vents.
            Line(c,13,40,36,52,2.5f,C(121,151,160)); Line(c,59,52,82,40,2.5f,C(121,151,160));
            Line(c,17,47,34,55,1.7f,C(255,254,236)); Line(c,61,55,79,47,1.7f,C(255,254,236));
            Line(c,37,30,39,45,2,C(136,160,167)); Line(c,57,30,55,45,2,C(136,160,167));
            Line(c,42,38,42,48,1.5f,C(255,255,244));
            Line(c,24,36,24,46,5,Ink); Line(c,72,36,72,46,5,Ink);
            Line(c,24,43,24,48,2.5f,C(255,184,66)); Line(c,72,43,72,48,2.5f,C(255,184,66));
            Ellipse(c,16,43,2.3f,2.3f,C(253,103,91)); Ellipse(c,80,43,2.3f,2.3f,C(83,236,197));
            for(int i=0;i<3;i++) { Line(c,40,24+i*3,44,24+i*3,1.2f,Ink); Line(c,52,24+i*3,56,24+i*3,1.2f,Ink); }
            Ellipse(c,48,83,3,3,C(250,174,62));
        }
        static void Enemy(PixelCanvas c,bool heavy)
        {
            var body = heavy ? C(226,160,76) : Coral;
            Line(c,10,49,27,39,15,Ink); Line(c,53,39,70,49,15,Ink);
            Line(c,10,49,27,39,9,body); Line(c,53,39,70,49,9,body);
            Ellipse(c,40,41,heavy?24:18,25,Ink); Ellipse(c,40,42,heavy?20:14,21,body);
            Line(c,30,49,50,49,10,Ink); Line(c,32,49,48,49,4,C(253,220,136));
            Ellipse(c,40,26,8,9,Ink); Ellipse(c,40,26,5,6,C(89,120,154));
            Line(c,40,61,40,66,4,Ivory);
            // Warm raised armor against cool inset machinery; silhouettes remain readable on mobile.
            Line(c,heavy?23:28,38,heavy?22:28,51,3,C(255,191,121));
            Line(c,heavy?57:52,38,heavy?58:52,51,3,C(132,61,74));
            Line(c,32,57,47,57,2,C(255,213,159));
            Line(c,32,33,35,29,2.5f,C(112,58,77)); Line(c,48,33,45,29,2.5f,C(112,58,77));
            Ellipse(c,38,29,2,2,C(129,203,226));
            Line(c,10,51,23,43,2,C(255,195,136)); Line(c,58,43,70,51,2,C(255,195,136));
            if(heavy) { Line(c,24,19,24,32,7,Ink); Line(c,56,19,56,32,7,Ink); Line(c,24,22,24,28,3,C(124,170,182)); Line(c,56,22,56,28,3,C(124,170,182)); }
        }
        public void Dispose() { Sheet?.Dispose(); Sheet = null; }
    }
}
