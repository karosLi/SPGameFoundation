using SPF.Runtime.Composition;
using UnityEngine;

namespace SPF.Runtime.Session
{
    /// <summary>
    /// Scene entry point. Ticks are scheduled in Update and completed in LateUpdate, so simulation jobs
    /// overlap the Update of other scripts. Presentation scripts should read snapshots in LateUpdate
    /// with a later execution order than this component.
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    [DisallowMultipleComponent]
    public sealed class SessionHost : MonoBehaviour
    {
        [SerializeField] ModeDefinition m_Mode;
        [SerializeField] uint m_Seed = 12345;
        [SerializeField] bool m_StartOnAwake = true;

        public SimSession Session { get; private set; }

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
            Session?.Dispose();
            m_Mode = mode;
            m_Seed = seed;
            Session = SimSession.Create(mode, seed);
            if (start)
                Session.Start();
            enabled = true;
            SessionCreated?.Invoke(Session);
            return Session;
        }

        void Update() => Session?.Update(Time.deltaTime);

        void LateUpdate() => Session?.Sync();

        void OnDestroy()
        {
            Session?.Dispose();
            Session = null;
        }
    }
}
