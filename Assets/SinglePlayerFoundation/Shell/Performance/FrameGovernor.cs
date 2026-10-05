using System;
using Unity.Profiling;
using UnityEngine;
#if SPF_URP
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
#endif

namespace SPF.Shell.Performance
{
    /// <summary>
    /// Frame-time hysteresis: steps a quality level up when the smoothed frame time stays over budget
    /// (thermal throttling on phones), and back down only after a longer stretch of headroom, so it
    /// does not oscillate. Pure logic, no engine calls; <see cref="FrameGovernor"/> drives it.
    /// </summary>
    public sealed class FrameBudget
    {
        public float TargetMs = 1000f / 60f;
        public float DegradeRatio = 1.15f;
        public float RecoverRatio = 0.8f;
        public float DegradeAfterSeconds = 2f;
        public float RecoverAfterSeconds = 6f;
        /// <summary>Frames longer than this (loading hitches, app resume) are ignored.</summary>
        public float SpikeSeconds = 0.25f;
        public int MaxLevel = 3;

        float m_Slow, m_Fast;

        public float AverageMs { get; private set; } = 1000f / 60f;
        public int Level { get; private set; }

        public void Reset(int level = 0)
        {
            Level = Math.Max(0, Math.Min(MaxLevel, level));
            AverageMs = TargetMs;
            m_Slow = m_Fast = 0f;
        }

        /// <summary>Feeds one frame's duration; returns true when <see cref="Level"/> changed.</summary>
        public bool Feed(float dtSeconds)
        {
            if (dtSeconds <= 0f || dtSeconds > SpikeSeconds) return false;
            AverageMs += (dtSeconds * 1000f - AverageMs) * 0.05f;

            if (AverageMs > TargetMs * DegradeRatio) { m_Slow += dtSeconds; m_Fast = 0f; }
            else if (AverageMs < TargetMs * RecoverRatio) { m_Fast += dtSeconds; m_Slow = 0f; }
            else { m_Slow = 0f; m_Fast = 0f; }

            if (m_Slow > DegradeAfterSeconds && Level < MaxLevel)
            {
                Level++;
                m_Slow = 0f;
                AverageMs = TargetMs;   // give the cheaper level a fresh measurement
                return true;
            }
            if (m_Fast > RecoverAfterSeconds && Level > 0)
            {
                Level--;
                m_Fast = 0f;
                return true;
            }
            return false;
        }
    }

    /// <summary>
    /// Idle detection for battery: after <see cref="IdleAfterSeconds"/> with no activity the governor drops
    /// the frame rate (a match-3 board at rest, a tower-defense build phase). Pure logic.
    /// </summary>
    public sealed class IdleThrottle
    {
        public float IdleAfterSeconds = 1.5f;
        float m_Quiet;

        public bool Idle { get; private set; }

        public void Wake()
        {
            m_Quiet = 0f;
            Idle = false;
        }

        /// <summary>Returns true when <see cref="Idle"/> changed.</summary>
        public bool Feed(float dtSeconds, bool activity)
        {
            bool was = Idle;
            if (activity) m_Quiet = 0f;
            else m_Quiet += dtSeconds;
            Idle = m_Quiet >= IdleAfterSeconds;
            return Idle != was;
        }
    }

    /// <summary>
    /// Mobile frame governor shared by the validation games:
    ///  - adaptive quality: <see cref="FrameBudget"/> levels, each mapped to a render scale (URP) and
    ///    announced through <see cref="LevelChanged"/> so a game can shed its own presentation work;
    ///  - idle throttling: optional lower frame rate while nothing moves and nobody touches the screen
    ///    (the game calls <see cref="KeepAwake"/> each frame something animates);
    ///  - GC watch: allocation per frame from the profiler counter (Editor / development players), for
    ///    zero-allocation checks in PlayMode tests and the perf HUD.
    /// The simulation tick rate is never touched: quality only changes presentation.
    /// </summary>
    [DefaultExecutionOrder(950)]
    public sealed class FrameGovernor : MonoBehaviour
    {
        public int ActiveFrameRate = 60;
        public int IdleFrameRate = 30;
        public bool ThrottleWhenIdle;
        public bool AdaptiveQuality = true;
        public float[] RenderScales = { 1f, 0.9f, 0.8f, 0.7f };

        public readonly FrameBudget Budget = new FrameBudget();
        public readonly IdleThrottle Idle = new IdleThrottle();

        /// <summary>Raised with the new level (0 = full quality).</summary>
        public event Action<int> LevelChanged;

        public int Level => Budget.Level;
        public float RenderScale => RenderScales.Length == 0 ? 1f : RenderScales[Math.Min(Level, RenderScales.Length - 1)];
        public bool IsIdle => Idle.Idle;
        public long GcBytesLastFrame { get; private set; }
        public long GcBytesSinceReset { get; private set; }
        public int GcFramesSinceReset { get; private set; }
        public int FramesSinceReset { get; private set; }
        public bool GcCounterValid => m_Gc.Valid;

        ProfilerRecorder m_Gc;
        int m_AwakeFrame = -1;
#if SPF_URP
        float m_OriginalRenderScale = -1f;
#endif

        /// <summary>Keeps the active frame rate for this frame (call while something animates).</summary>
        public void KeepAwake() => m_AwakeFrame = Time.frameCount;

        public void ResetGcStats()
        {
            GcBytesSinceReset = 0;
            GcFramesSinceReset = 0;
            FramesSinceReset = 0;
        }

        /// <summary>Sets the active and idle frame rates (the quality budget follows the active one).</summary>
        public void SetFrameRates(int active, int idle)
        {
            ActiveFrameRate = active;
            IdleFrameRate = idle;
            Budget.TargetMs = 1000f / Math.Max(1, active);
            Application.targetFrameRate = Idle.Idle && ThrottleWhenIdle ? idle : active;
        }

        public void SetLevel(int level)
        {
            Budget.Reset(level);
            Apply();
        }

        void OnEnable()
        {
            m_Gc = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame");
            Budget.MaxLevel = Math.Max(0, RenderScales.Length - 1);
            Budget.TargetMs = 1000f / Math.Max(1, ActiveFrameRate);
            Budget.Reset(Budget.Level);
            Idle.Wake();
            Application.targetFrameRate = ActiveFrameRate;
        }

        void OnDisable()
        {
            m_Gc.Dispose();
#if SPF_URP
            // The URP asset is shared (and persists in the Editor): restore what we changed.
            if (m_OriginalRenderScale > 0f && GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urp)
                urp.renderScale = m_OriginalRenderScale;
            m_OriginalRenderScale = -1f;
#endif
        }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            if (m_Gc.Valid)
            {
                GcBytesLastFrame = m_Gc.LastValue;
                GcBytesSinceReset += GcBytesLastFrame;
                if (GcBytesLastFrame > 0) GcFramesSinceReset++;
            }
            FramesSinceReset++;

            if (ThrottleWhenIdle)
            {
                bool activity = m_AwakeFrame >= Time.frameCount - 1 || AnyInput();
                if (Idle.Feed(dt, activity))
                    Application.targetFrameRate = Idle.Idle ? IdleFrameRate : ActiveFrameRate;
            }

            // Idle frames are slow on purpose; they say nothing about the device's headroom.
            if (AdaptiveQuality && !Idle.Idle && Budget.Feed(dt))
                Apply();
        }

        void Apply()
        {
#if SPF_URP
            if (GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urp)
            {
                if (m_OriginalRenderScale < 0f) m_OriginalRenderScale = urp.renderScale;
                urp.renderScale = m_OriginalRenderScale * RenderScale;
            }
#endif
            LevelChanged?.Invoke(Level);
        }

        static bool AnyInput()
        {
#if ENABLE_LEGACY_INPUT_MANAGER
            return UnityEngine.Input.touchCount > 0 || UnityEngine.Input.GetMouseButton(0) || UnityEngine.Input.anyKey;
#else
            return false;
#endif
        }
    }
}
