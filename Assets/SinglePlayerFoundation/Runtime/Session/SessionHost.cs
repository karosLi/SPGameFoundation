using SPF.Runtime.Composition;
using SPF.Runtime.World;
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
        // Unpublished does not mean safely disposed. Keep one owner while child teardown or
        // job completion prevents disposal; no replacement is admitted until this slot drains.
        SimSession m_RetiredSession;
        bool m_RetirementInProgress;
        bool m_Initializing;

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
            if (m_Initializing)
                throw new System.InvalidOperationException("Session initialization is still in progress.");
            m_Initializing = true;
            try
            {
                RetireSession();
                m_Mode = mode;
                m_Seed = seed;
                var created = SimSession.Create(mode, seed);
                Session = created;
                try
                {
                    if (start) created.Start();
                    enabled = true;
                    EnsureLauncher();
                    SessionCreated?.Invoke(created);
                    if (!ReferenceEquals(Session, created) || created.State == SessionState.Disposed)
                        throw new System.InvalidOperationException("Session was removed or disposed during binding.");
                    return created;
                }
                catch (System.Exception failure)
                {
                    // Host owns the new Session, not arbitrary partial assets in subscribers.
                    // Unpublish first, retain an unsafe retirement for retry, and keep the binding
                    // failure primary if safe cleanup also reports errors.
                    CleanupErrors.Try(RetireSession, ref failure);
                    throw;
                }
            }
            finally { m_Initializing = false; }
        }

        void RetireSession()
        {
            // A cleanup hook may call Initialize/OnDestroy. Never create a replacement while
            // the outer owner is still running hooks, even if State is already Disposed.
            if (m_RetirementInProgress)
                throw new System.InvalidOperationException("Session cleanup is still in progress.");
            if (m_Launcher != null) m_Launcher.enabled = false;
            if (Session != null)
            {
                m_RetiredSession = Session;
                Session = null;
            }
            var retired = m_RetiredSession;
            if (retired == null) return;
            m_RetirementInProgress = true;
            try
            {
                retired.Dispose();
                if (retired.State != SessionState.Disposed)
                    throw new System.InvalidOperationException("Session cleanup is still in progress.");
            }
            finally
            {
                // Safe cleanup may throw diagnostics after releasing everything. Unsafe or
                // child-in-progress cleanup instead remains owned for Initialize/OnDestroy retry.
                if (retired.State == SessionState.Disposed) m_RetiredSession = null;
                m_RetirementInProgress = false;
            }
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
            RetireSession();
        }
    }
}
