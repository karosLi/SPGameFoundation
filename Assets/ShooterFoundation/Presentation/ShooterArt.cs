using System;
using SPF.Presentation.Sprites;
using Unity.Mathematics;
using UnityEngine;

namespace ShooterFoundation.Presentation
{
    /// <summary>Original vector-like placeholder art, rasterized at 4x and alpha-aware downsampled. No external/IP assets.</summary>
    public sealed class ShooterArt : IDisposable
    {
        public SpriteSheet Sheet;
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
        }
        public void Dispose() { Sheet?.Dispose(); Sheet = null; }
    }
}
