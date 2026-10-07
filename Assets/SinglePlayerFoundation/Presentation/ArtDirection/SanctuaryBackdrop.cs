using System;
using SPF.Presentation.Sprites;
using Unity.Mathematics;
using UnityEngine;

namespace SPF.Presentation.ArtDirection
{
    public enum SanctuaryScene : byte { Courtyard, Terrace, SkyRiver }

    /// <summary>Original painted environment plates plus a small shared inlay atlas. No simulation writes,
    /// camera objects, runtime texture copies or per-frame allocations. Missing art retains the classic view.</summary>
    public sealed class SanctuaryBackdrop : IDisposable
    {
        public const int MaxGroundTiles = 64;
        readonly SanctuaryScene m_Scene;
        readonly SpriteBatch m_Ground, m_Vista, m_Details;
        readonly SpriteSheet m_Ornaments;
        readonly Texture2D m_GroundTexture, m_VistaTexture;
        readonly int m_Rune, m_Line, m_Medallion;
        static readonly float4 Full = new float4(0,0,1,1);
        public bool Ready => m_Ground != null && (m_Scene != SanctuaryScene.Terrace || m_Vista != null);
        public int SpritesDrawn => (m_Ground?.Count ?? 0)+(m_Vista?.Count ?? 0)+(m_Details?.Count ?? 0);
        public long BytesUploaded => (m_Ground?.BytesUploaded ?? 0)+(m_Vista?.BytesUploaded ?? 0)+(m_Details?.BytesUploaded ?? 0);
        // Conservative RGBA upper bound. Import compression can reduce this on device; no CPU-readable copy.
        public long TextureBytes => (m_GroundTexture==null?0L:(long)m_GroundTexture.width*m_GroundTexture.height*4)+
            (m_VistaTexture==null?0L:(long)m_VistaTexture.width*m_VistaTexture.height*4)+
            (m_Ornaments==null?0L:(long)m_Ornaments.Texture.width*m_Ornaments.Texture.height*4);

        public SanctuaryBackdrop(RenderTier tier, SanctuaryScene scene)
        {
            m_Scene=scene;
            m_GroundTexture=Resources.Load<Texture2D>(scene==SanctuaryScene.SkyRiver?"SPF/ArtDirection/SkyRiver":"SPF/ArtDirection/SanctuaryGround");
            if(scene==SanctuaryScene.Terrace)m_VistaTexture=Resources.Load<Texture2D>("SPF/ArtDirection/SanctuaryVista");
            if(m_GroundTexture==null || scene==SanctuaryScene.Terrace&&m_VistaTexture==null)return;
            m_Ground=new SpriteBatch(tier,m_GroundTexture,BlendKind.Opaque,MaxGroundTiles,-90);m_Ground.Warmup(MaxGroundTiles);
            if(m_VistaTexture!=null){m_Vista=new SpriteBatch(tier,m_VistaTexture,BlendKind.Opaque,1,-100);m_Vista.Warmup(1);}
            var atlas=new SpriteAtlasBuilder();
            var rune=new PixelCanvas(192,192);
            var bronze=new Color32(163,145,102,255);var patina=new Color32(83,126,119,255);
            rune.Ring(96,96,84,87,bronze);rune.Ring(96,96,76,77,patina);rune.Ring(96,96,49,51,bronze);
            for(int i=0;i<12;i++)
            {
                float a=i*math.PI/6f;var p=new float2(math.cos(a),math.sin(a));
                rune.Line(new float2(96)+p*55,new float2(96)+p*71,i%3==0?3:1.2f,bronze);
                if(i%3==0)rune.Ellipse(96+p.x*84,96+p.y*84,4,4,bronze);
            }
            rune.Line(new float2(96,64),new float2(118,96),2,bronze);rune.Line(new float2(118,96),new float2(96,128),2,bronze);
            rune.Line(new float2(96,128),new float2(74,96),2,bronze);rune.Line(new float2(74,96),new float2(96,64),2,bronze);
            m_Rune=atlas.Add(rune);
            var line=new PixelCanvas(4,4);line.Rect(0,0,4,4,new Color32(255,255,255,255));m_Line=atlas.Add(line);
            var medallion=new PixelCanvas(64,64);medallion.Ellipse(32,32,28,28,new Color32(26,52,52,255));
            medallion.Ring(32,32,23,25,bronze);medallion.Line(new float2(32,13),new float2(32,51),3,bronze);
            medallion.Line(new float2(17,32),new float2(47,32),3,bronze);m_Medallion=atlas.Add(medallion);
            m_Ornaments=atlas.Build(256,FilterMode.Bilinear,2,true);
            m_Details=new SpriteBatch(tier,m_Ornaments.Texture,BlendKind.Translucent,64,-85);m_Details.Warmup(64);
        }

        public void Draw(float4 view, Bounds bounds, float time=0)
        {
            if(!Ready)return;
            m_Ground.Clear();m_Vista?.Clear();m_Details.Clear();
            if(m_Scene==SanctuaryScene.Terrace)Terrace(view);
            else if(m_Scene==SanctuaryScene.SkyRiver)Sky(view,time);
            else Courtyard(view);
            m_Vista?.Draw(bounds);m_Ground.Draw(bounds);m_Details.Draw(bounds);
        }
        void Courtyard(float4 view)
        {
            // Large stone groups reduce visible tiling; mirroring joins corresponding edge samples exactly.
            const float tile=14;
            int2 lo=(int2)math.floor(view.xy/tile),hi=(int2)math.floor(view.zw/tile);
            for(int y=lo.y;y<=hi.y;y++)for(int x=lo.x;x<=hi.x;x++)
                m_Ground.Add((new float2(x,y)+.5f)*tile,new float2((x&1)==0?tile+.01f:-tile-.01f,(y&1)==0?tile+.01f:-tile-.01f),Full,9.1f,new float4(.78f,.86f,.85f,1));
            // A fixed, low-contrast floor compass provides world orientation without resembling a danger cue.
            m_Details.Add(float2.zero,new float2(11),m_Ornaments[m_Rune].Uv,8.9f,new float4(.65f,.72f,.67f,.32f));
            for(int i=-2;i<=2;i++)
            {
                m_Details.Add(new float2(i*5.5f,0),new float2(.025f,32),m_Ornaments[m_Line].Uv,8.92f,new float4(.54f,.62f,.53f,.20f));
                m_Details.Add(new float2(0,i*5.5f),new float2(32,.025f),m_Ornaments[m_Line].Uv,8.92f,new float4(.54f,.62f,.53f,.20f));
                if(i!=0)m_Details.Add(new float2(i*5.5f,0),new float2(.75f),m_Ornaments[m_Medallion].Uv,8.91f,new float4(.8f,.87f,.8f,.5f));
            }
        }
        void Terrace(float4 view)
        {
            float width=math.max(25,view.z-view.x+1),height=width*2f/3f;
            m_Vista.Add(new float2(0,2.2f+height*.28f),new float2(width,height),Full,10,new float4(.72f,.83f,.84f,1));
            float bottom=math.min(-5,view.y-1),top=1.9f;
            // Only one opaque foreground layer; project the stone plate over the entire walkable belt.
            m_Ground.Add(new float2(0,(bottom+top)*.5f),new float2(40,top-bottom),Full,8,new float4(.78f,.86f,.85f,1));
            m_Details.Add(new float2(0,top),new float2(40,.055f),m_Ornaments[m_Line].Uv,7.9f,new float4(.83f,.76f,.53f,.85f));
            m_Details.Add(new float2(0,-1.6f),new float2(40,.035f),m_Ornaments[m_Line].Uv,7.9f,new float4(.65f,.60f,.42f,.55f));
            for(int i=-3;i<=3;i++)
            {
                m_Details.Add(new float2(i*4f,.05f),new float2(3.2f,1.8f),m_Ornaments[m_Rune].Uv,7.8f,new float4(.7f,.77f,.68f,.22f));
                m_Details.Add(new float2(i*4f,top-.16f),new float2(.28f,.18f),m_Ornaments[m_Medallion].Uv,7.8f,new float4(1,1,1,.85f));
            }
        }
        void Sky(float4 view,float time)
        {
            float width=math.max(view.z-view.x,10),height=width*1.5f;
            float centre=(view.y+view.w)*.5f,offset=time*.42f;
            // Alternating vertical mirroring avoids an abrupt seam without an alpha crossfade/fullscreen overdraw.
            int first=(int)math.floor((view.y-centre+offset)/height);
            for(int i=first;i<=first+2;i++)
                m_Ground.Add(new float2((view.x+view.z)*.5f,centre+(i+.5f)*height-offset),new float2(width+.01f,(i&1)==0?height+.01f:-height-.01f),Full,10.1f,new float4(.68f,.79f,.80f,1));
        }
        public void Dispose()
        {
            m_Ground?.Dispose();m_Vista?.Dispose();m_Details?.Dispose();m_Ornaments?.Dispose();
            // Resources textures are shared Unity assets, never destroy them from an individual renderer.
        }
    }
}
