#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Text;
using SPF.Contracts;
using SPF.Runtime.Session;
using Unity.Profiling;
using UnityEngine;

namespace SPF.Runtime.Diagnostics
{
    /// <summary>
    /// Development overlay: tick timings per phase, entity counts, GC allocation per frame and draw calls.
    /// The text is rebuilt a few times per second, so the HUD itself allocates a little; it is compiled
    /// out of release builds. Toggle with F1 or a three-finger tap (legacy input manager).
    /// </summary>
    [DefaultExecutionOrder(1000)]
    public sealed class PerfHud : MonoBehaviour
    {
        [SerializeField] SessionHost m_Host;
        [SerializeField, Range(1f, 10f)] float m_RefreshRate = 4f;
        [SerializeField] bool m_Visible = true;
        [SerializeField] int m_FontSize = 22;

        readonly StringBuilder m_Text = new StringBuilder(2048);
        readonly GUIContent m_Content = new GUIContent(string.Empty);
        Vector2 m_ContentSize;
        bool m_ContentDirty = true;
        float m_NextRefresh;
        float m_FrameMsAverage;
        long m_GcPeakBytes;

        ProfilerRecorder m_GcAllocRecorder;
        ProfilerRecorder m_DrawCallsRecorder;
        ProfilerRecorder m_SetPassRecorder;
        GUIStyle m_Style;

        void OnEnable()
        {
            m_GcAllocRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame");
            m_DrawCallsRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count");
            m_SetPassRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Render, "SetPass Calls Count");
        }

        void OnDisable()
        {
            m_GcAllocRecorder.Dispose();
            m_DrawCallsRecorder.Dispose();
            m_SetPassRecorder.Dispose();
        }

        void Update()
        {
#if ENABLE_LEGACY_INPUT_MANAGER
            if (Input.GetKeyDown(KeyCode.F1) || Input.touchCount == 3 && Input.GetTouch(2).phase == TouchPhase.Began)
                m_Visible = !m_Visible;
#endif

            float frameMs = Time.unscaledDeltaTime * 1000f;
            m_FrameMsAverage += (frameMs - m_FrameMsAverage) * 0.1f;
            if (m_GcAllocRecorder.Valid && m_GcAllocRecorder.LastValue > m_GcPeakBytes)
                m_GcPeakBytes = m_GcAllocRecorder.LastValue;

            if (m_Visible && Time.unscaledTime >= m_NextRefresh)
            {
                m_NextRefresh = Time.unscaledTime + 1f / m_RefreshRate;
                Rebuild();
                m_GcPeakBytes = 0;
            }
        }

        void Rebuild()
        {
            var sb = m_Text.Clear();
            sb.Append("Frame ").Append(m_FrameMsAverage.ToString("F1")).Append(" ms");
            if (m_GcAllocRecorder.Valid)
                sb.Append("   GC peak/frame ").Append(m_GcPeakBytes).Append(" B");
            if (m_DrawCallsRecorder.Valid)
                sb.Append("\nDraw ").Append(m_DrawCallsRecorder.LastValue).Append("   SetPass ").Append(m_SetPassRecorder.LastValue);

            var session = m_Host != null ? m_Host.Session : null;
            if (session == null)
            {
                sb.Append("\n(no session)");
                Publish();
                return;
            }

            var stats = session.Pipeline.Stats;
            sb.Append("\nTick ").Append(session.Clock.TickRate).Append(" Hz  #").Append(stats.TickCount)
              .Append("  ticks/frame ").Append(session.TicksLastFrame)
              .Append("  dropped ").Append(session.Clock.DroppedTicks);
            sb.Append("\nSchedule ").Append(stats.ScheduleMs.ToString("F2"))
              .Append(" ms   SyncWait ").Append(stats.SyncWaitMs.ToString("F2"))
              .Append(" ms   Wall ").Append(stats.TickWallMs.ToString("F2")).Append(" ms");

            for (int p = 0; p < SimPhases.Count; p++)
            {
                var phase = (SimPhase)p;
                float ms = stats.PhaseScheduleMs(phase);
                if (ms <= 0f) continue;
                sb.Append("\n  ").Append(phase.ToString()).Append(' ').Append(ms.ToString("F3")).Append(" ms");
            }

            var world = session.World;
            sb.Append("\nEntities ").Append(world.Registry.AliveCount).Append('/').Append(world.Registry.Capacity);
            foreach (var table in world.Tables)
                sb.Append("\n  ").Append(table.Key.Name).Append(' ').Append(table.Count).Append('/').Append(table.Capacity);
            if (world.CreateFailures > 0)
                sb.Append("\n  create failures ").Append(world.CreateFailures);

            Publish();
        }

        void Publish()
        {
            m_Content.text = m_Text.ToString();
            m_ContentDirty = true;
        }

        void OnGUI()
        {
            if (!m_Visible) return;
            if (m_Style == null)
            {
                m_Style = new GUIStyle(GUI.skin.box)
                {
                    alignment = TextAnchor.UpperLeft,
                    fontSize = m_FontSize,
                    richText = false,
                };
                m_Style.normal.textColor = Color.white;
            }
            if (m_ContentDirty)
            {
                m_ContentSize = m_Style.CalcSize(m_Content);
                m_ContentDirty = false;
            }
            GUI.Box(new Rect(8f, 8f, m_ContentSize.x + 16f, m_ContentSize.y + 8f), m_Content, m_Style);
        }
    }
}
#endif
