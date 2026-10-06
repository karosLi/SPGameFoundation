using SPF.Presentation;
using SPF.Presentation.Sprites;
using SPF.Runtime.Session;
using Unity.Mathematics;
using UnityEngine;

namespace SurvivorFoundation.Presentation
{
    /// <summary>Read-only sword view on the existing two-tier sprite backend. Trails and aggregated
    /// damage numbers use bounded pools; dropping visual work can never suppress an authoritative hit.</summary>
    [DefaultExecutionOrder(550)]
    public sealed class SvSwordPresentation : MonoBehaviour
    {
        const int TrailSamples = 8, MaxSwords = 64;
        SessionHost m_Host;
        SvRenderer m_View;
        SvArt m_BoundArt;
        SpriteBatch m_Blades, m_Trails, m_NumberBatch;
        readonly SpriteEffects m_Numbers = new SpriteEffects(64);
        readonly float2[] m_Trail = new float2[MaxSwords * TrailSamples];
        readonly int[] m_TrailCount = new int[MaxSwords];
        int m_Tick = -1, m_Emitted;
        public int SwordsDrawn { get; private set; }
        public int TrailSegmentsDrawn { get; private set; }
        public int ActiveNumbers => m_Numbers.Active;
        public int AcceptedNumbers { get; private set; }
        public int DroppedNumbers { get; private set; }
        public int NumberBudget => m_View == null ? 0 : m_View.QualityLevel >= 3 ? 4 : m_View.QualityLevel >= 2 ? 8 : 16;
        public long BytesUploaded => (m_Blades?.BytesUploaded ?? 0) + (m_Trails?.BytesUploaded ?? 0) + (m_NumberBatch?.BytesUploaded ?? 0);
        public void Initialize(SessionHost host, SvRenderer view)
        {
            m_Host = host; m_View = view; view.Feedback += OnFeedback;
        }
        void OnFeedback(SvFeedback e)
        {
            if (e.Kind != SvFeedbackKind.Hit || e.Value <= 0f) return;
            if (m_Emitted >= NumberBudget) { if (DroppedNumbers < int.MaxValue) DroppedNumbers++; return; }
            m_Emitted++; if (AcceptedNumbers < int.MaxValue) AcceptedNumbers++;
            m_Numbers.Spawn(new SpriteEffects.Effect
            {
                NumberMode = true, Number = (int)math.round(math.min(999999, e.Value)), Prefix = '-',
                Position = e.Position + new float2(0, .8f), Velocity = new float2((m_Emitted % 3 - 1) * .12f, .85f),
                Size = new float2(.40f), Life = .62f, Fade = true,
                Color = new float4(1f, .89f, .42f, 1f), Depth = -.4f,
            });
        }
        void Release()
        {
            m_Blades?.Dispose(); m_Trails?.Dispose(); m_NumberBatch?.Dispose();
            m_Blades = m_Trails = m_NumberBatch = null; m_BoundArt = null;
        }
        void OnDestroy() { if (m_View != null) m_View.Feedback -= OnFeedback; Release(); }
        void Bind(SvArt art)
        {
            Release(); m_BoundArt = art;
            m_Blades = new SpriteBatch(m_View.Tier, art.Sheet.Texture, BlendKind.Translucent, MaxSwords * 2, queueOffset: 90);
            m_Trails = new SpriteBatch(m_View.Tier, art.Sheet.Texture, BlendKind.Translucent, MaxSwords * TrailSamples * 2, queueOffset: 75);
            m_NumberBatch = new SpriteBatch(m_View.Tier, art.Sheet.Texture, BlendKind.Translucent, 64 * 9, queueOffset: 175);
            m_Blades.Warmup(m_Blades.Capacity); m_Trails.Warmup(m_Trails.Capacity); m_NumberBatch.Warmup(m_NumberBatch.Capacity);
        }
        void LateUpdate() => RenderFrame();

        /// <summary>Normal rendered frame; exposed for calibrated synchronous presentation probes.</summary>
        public void RenderFrame()
        {
            var session = m_Host != null ? m_Host.Session : null;
            var art = m_View != null ? m_View.Art : null;
            if (session == null || art == null || !session.World.HasResource(SvFlyingSwordState.Key)) return;
            if (art != m_BoundArt) Bind(art);
            var game = session.World.Resource(SvKeys.Game); var swords = session.World.Resource(SvFlyingSwordState.Key);
            if (game.Flow == SvFlow.Menu || game.RunTicks < m_Tick)
            {
                m_Numbers.Clear(); System.Array.Clear(m_TrailCount, 0, m_TrailCount.Length);
                m_Emitted = 0; AcceptedNumbers = DroppedNumbers = 0;
            }
            bool sample = game.RunTicks != m_Tick; m_Tick = game.RunTicks;
            SwordsDrawn = TrailSegmentsDrawn = 0; m_Blades.Clear(); m_Trails.Clear(); m_NumberBatch.Clear();
            var view = m_View.Camera != null ? m_View.Camera.ViewRect : new float4(game.Hero - 20, game.Hero + 20);
            int segments = m_View.QualityLevel >= 3 ? 2 : m_View.QualityLevel >= 2 ? 4 : 7;
            for (int i = 0; i < swords.ActiveCount && game.Flow != SvFlow.Menu; i++)
            {
                var b = swords.Blades[i]; if (!b.Active) continue;
                int offset = i * TrailSamples;
                if (sample)
                {
                    // Blink, snapshot seek and return timeout cannot draw a false attack beam.
                    if (m_TrailCount[i] > 0 && math.distancesq(m_Trail[offset], b.Position) > 9f) m_TrailCount[i] = 0;
                    int count = math.min(TrailSamples - 1, m_TrailCount[i]);
                    for (int j = count; j > 0; j--) m_Trail[offset + j] = m_Trail[offset + j - 1];
                    m_Trail[offset] = b.Position; m_TrailCount[i] = math.min(TrailSamples, count + 1);
                }
                float2 p = math.lerp(b.Previous, b.Position, session.InterpolationAlpha);
                if (p.x < view.x - 1 || p.x > view.z + 1 || p.y < view.y - 1 || p.y > view.w + 1) continue;
                bool gold = (i & 1) == 0;
                float4 color = gold ? new float4(1f, .72f, .17f, .7f) : new float4(.18f, .66f, 1f, .78f);
                float2 last = p;
                for (int j = 0; j < math.min(segments, m_TrailCount[i] - 1); j++)
                {
                    // Sample zero is the current authoritative point ahead of the interpolated head.
                    float2 tail = m_Trail[offset + j + 1]; float2 delta = last - tail; float length = math.length(delta);
                    if (length > .015f && length < 3f)
                    {
                        float fade = 1f - (j + 1f) / (segments + 1f); float4 tint = color; tint.w *= fade;
                        float angle = math.atan2(delta.y, delta.x);
                        m_Trails.Add((last + tail) * .5f, new float2(length + .06f, .12f * fade + .025f), art.Sheet[art.White].Uv, .3f, tint, angle);
                        tint.w *= .7f;
                        m_Trails.Add((last + tail) * .5f, new float2(length, .035f), art.Sheet[art.White].Uv, .29f, new float4(.9f, .95f, 1f, tint.w), angle);
                        TrailSegmentsDrawn++;
                    }
                    last = tail;
                }
                m_Blades.Add(p, new float2(.72f), art.Sheet[art.Glow].Uv, .19f, color);
                m_Blades.Add(p, new float2(.92f), art.Sheet[art.Blade].Uv, .18f,
                    gold ? new float4(1f, .94f, .68f, 1f) : new float4(.7f, .92f, 1f, 1f), math.atan2(b.Direction.y, b.Direction.x) - math.PI * .25f);
                SwordsDrawn++;
            }
            m_Numbers.UpdateAndDraw(game.Flow == SvFlow.Playing ? Time.deltaTime : 0f, m_NumberBatch, art.Sheet, art.Font);
            var bounds = new Bounds(Vector3.zero, new Vector3(1e5f, 1e5f, 100f));
            m_Trails.Draw(bounds); m_Blades.Draw(bounds); m_NumberBatch.Draw(bounds); m_Emitted = 0;
        }
    }
}
