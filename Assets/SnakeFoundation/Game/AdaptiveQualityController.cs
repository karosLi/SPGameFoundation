using SPF.Runtime.Session;
using UnityEngine;
#if SPF_URP
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
#endif

namespace SnakeFoundation.Game
{
    /// <summary>
    /// Watches frame time and trades fidelity for speed when the device can't keep up (thermal
    /// throttling on phones): AI decision rate, translucency budget, node LOD, render scale — in that
    /// order. Recovers slowly when there is headroom. The simulation tick rate is never touched.
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
        static readonly float[] s_RenderScale = { 1f, 0.9f, 0.8f, 0.7f };

        float m_Average = 16f;
        float m_SlowTime;
        float m_FastTime;

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
            float ms = Time.unscaledDeltaTime * 1000f;
            m_Average += (ms - m_Average) * 0.05f;

            if (m_Average > TargetFrameMs * 1.15f) { m_SlowTime += Time.unscaledDeltaTime; m_FastTime = 0f; }
            else if (m_Average < TargetFrameMs * 0.8f) { m_FastTime += Time.unscaledDeltaTime; m_SlowTime = 0f; }
            else { m_SlowTime = 0f; m_FastTime = 0f; }

            if (m_SlowTime > DegradeAfterSeconds && Level < MaxLevel) { SetLevel(session, Level + 1); m_SlowTime = 0f; }
            else if (m_FastTime > RecoverAfterSeconds && Level > 0) { SetLevel(session, Level - 1); m_FastTime = 0f; }
        }

        public void SetLevel(SimSession session, int level)
        {
            Level = Mathf.Clamp(level, 0, MaxLevel);
            var quality = session.World.Resource(SnakeKeys.Quality);
            quality.Level = Level;
            quality.AIDecisionIntervalTicks = s_AIInterval[Level];
            quality.TranslucentBudget = s_Translucent[Level];
            quality.NodeStride = s_NodeStride[Level];
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
