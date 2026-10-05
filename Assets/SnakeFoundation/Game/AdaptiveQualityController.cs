using SPF.Runtime.Session;
using SPF.Shell.Performance;
using UnityEngine;
#if SPF_URP
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
#endif

namespace SnakeFoundation.Game
{
    /// <summary>
    /// Watches frame time and trades fidelity for speed when the device can't keep up (thermal
    /// throttling on phones): AI decision rate, translucency budget, node LOD, disc polygon detail,
    /// render scale — in that order. Recovers slowly when there is headroom. The simulation tick rate is never touched.
    /// </summary>
    [DefaultExecutionOrder(900)]
    public sealed class AdaptiveQualityController : MonoBehaviour
    {
        public SessionHost Host { get; set; }
        public float TargetFrameMs = 1000f / 60f;
        public float DegradeAfterSeconds = 2f;
        public float RecoverAfterSeconds = 6f;
        public const int MaxLevel = 3;

        static readonly int[] s_AIInterval = { 1, 8, 12, 16 };
        static readonly int[] s_Translucent = { 30, 20, 10, 0 };
        static readonly int[] s_NodeStride = { 1, 1, 2, 2 };
        static readonly int[] s_DiscSegments = { 16, 16, 8, 8 };
        static readonly float[] s_RenderScale = { 1f, 0.9f, 0.8f, 0.7f };

        readonly FrameBudget m_Budget = new FrameBudget { MaxLevel = MaxLevel };

        public int Level { get; private set; }

#if SPF_URP
        float m_OriginalRenderScale = -1f;

        void OnDisable()
        {
            // The URP asset is shared (and persists in the Editor): restore what we changed.
            if (m_OriginalRenderScale > 0f && GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urp)
                urp.renderScale = m_OriginalRenderScale;
        }
#endif

        void Update()
        {
            var session = Host != null ? Host.Session : null;
            if (session == null) return;
            m_Budget.TargetMs = TargetFrameMs;
            m_Budget.DegradeAfterSeconds = DegradeAfterSeconds;
            m_Budget.RecoverAfterSeconds = RecoverAfterSeconds;
            if (m_Budget.Feed(Time.unscaledDeltaTime)) SetLevel(session, m_Budget.Level);
        }

        public void SetLevel(SimSession session, int level)
        {
            Level = Mathf.Clamp(level, 0, MaxLevel);
            if (m_Budget.Level != Level) m_Budget.Reset(Level);
            var quality = session.World.Resource(SnakeKeys.Quality);
            quality.Level = Level;
            quality.AIDecisionIntervalTicks = s_AIInterval[Level];
            quality.TranslucentBudget = s_Translucent[Level];
            quality.NodeStride = s_NodeStride[Level];
            quality.DiscSegments = s_DiscSegments[Level];
            quality.RenderScale = s_RenderScale[Level];
#if SPF_URP
            if (GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urp)
            {
                if (m_OriginalRenderScale < 0f) m_OriginalRenderScale = urp.renderScale;
                urp.renderScale = m_OriginalRenderScale * quality.RenderScale;
            }
#endif
        }
    }
}
