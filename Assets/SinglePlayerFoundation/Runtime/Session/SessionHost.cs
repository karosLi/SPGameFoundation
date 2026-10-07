using SPF.Runtime.Composition;
using UnityEngine;

namespace SPF.Runtime.Session
{
    /// <summary>
    /// Scene entry point. Presentation scripts read the world in LateUpdate with a later execution order
    /// than this component (which completes the in-flight tick at the start of LateUpdate).
    /// <para>
    /// With <see cref="OverlapRendering"/> (default) the next tick is scheduled at the very end of the
    /// frame, after presentation has read the world: its jobs run while Unity renders this frame and
    /// while the next frame's Update scripts run, and are completed at the start of the next LateUpdate.
    /// The worker time disappears from the main thread at the cost of one frame of display latency.
    /// Input written in Update still reaches the tick scheduled in the same frame.
    /// Without it, ticks are scheduled in Update and only overlap other scripts' Update.
    /// Main-thread code that touches native world data between those points must call
    /// <see cref="SimSession.Sync"/> first (the PlayMode test helpers do).
    /// </para>
    /// Disabling the host, losing focus or backgrounding the application completes pending work and
    /// suspends ticking. All interruption reasons must clear before ticking resumes; an explicit
    /// gameplay pause remains paused. The first resumed frame discards background wall time.
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    [DisallowMultipleComponent]
    public sealed class SessionHost : MonoBehaviour
    {
        [SerializeField] ModeDefinition m_Mode;
        [SerializeField] uint m_Seed = 12345;
        [SerializeField] bool m_StartOnAwake = true;
        [SerializeField] bool m_OverlapRendering = true;
        SessionTickLauncher m_Launcher;
        bool m_ApplicationPaused;
        bool m_FocusLost;

        public SimSession Session { get; private set; }

        /// <summary>Schedule ticks at the end of the frame so they run during rendering (see class remarks).</summary>
        public bool OverlapRendering
        {
            get => m_OverlapRendering;
            set { m_OverlapRendering = value; EnsureLauncher(); }
        }

        /// <summary>Raised after a session was created (from the serialized mode or <see cref="Initialize"/>).</summary>
        public event System.Action<SimSession> SessionCreated;

        void Awake()
        {
            // A host added from code has no mode yet; it is initialised explicitly.
            if (m_Mode != null && Session == null)
                Initialize(m_Mode, m_Seed, m_StartOnAwake);
        }

        void Start()
        {
            if (Session == null)
            {
                Debug.LogError("SessionHost has no ModeDefinition.", this);
                enabled = false;
            }
        }

        /// <summary>Creates the session from code (bootstraps, tests). Disposes a previous session.</summary>
        public SimSession Initialize(ModeDefinition mode, uint seed, bool start = true)
        {
            // Stop publishing/scheduling the old session before cleanup or creation can throw.
            var previous = Session;
            Session = null;
            if (m_Launcher != null) m_Launcher.enabled = false;
            previous?.Dispose();
            m_Mode = mode;
            m_Seed = seed;
            Session = SimSession.Create(mode, seed);
            if (start)
                Session.Start();
            enabled = true;
            EnsureLauncher();
            SessionCreated?.Invoke(Session);
            return Session;
        }

        void OnEnable() => EnsureLauncher();

        void OnDisable() => ApplySuspension();

        void OnApplicationPause(bool paused)
        {
            m_ApplicationPaused = paused;
            ApplySuspension();
        }

        void OnApplicationFocus(bool focused)
        {
            m_FocusLost = !focused;
            ApplySuspension();
        }

        void EnsureLauncher()
        {
            if (m_Launcher == null)
            {
                m_Launcher = GetComponent<SessionTickLauncher>();
                if (m_Launcher == null) m_Launcher = gameObject.AddComponent<SessionTickLauncher>();
                m_Launcher.hideFlags = HideFlags.HideInInspector;
                m_Launcher.Host = this;
            }
            ApplySuspension();
        }

        void ApplySuspension()
        {
            bool suspended = !isActiveAndEnabled || m_ApplicationPaused || m_FocusLost;
            Session?.SetHostSuspended(suspended);
            if (m_Launcher != null)
                m_Launcher.enabled = Session != null && m_OverlapRendering && !suspended;
        }

        void Update()
        {
            if (isActiveAndEnabled && !m_OverlapRendering)
                Session?.Update(Time.deltaTime);
        }

        void LateUpdate() => Session?.Sync();

        /// <summary>Called by <see cref="SessionTickLauncher"/> after all other LateUpdates.</summary>
        internal void LaunchTicks()
        {
            if (isActiveAndEnabled && m_OverlapRendering)
                Session?.Update(Time.deltaTime);
        }

        void OnDestroy()
        {
            if (m_Launcher != null)
            {
                m_Launcher.enabled = false;
                m_Launcher.Host = null;
            }
            var session = Session;
            Session = null;
            session?.Dispose();
        }
    }
}
