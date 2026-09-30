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

        void Awake()
        {
            if (m_Mode == null)
            {
                Debug.LogError("SessionHost has no ModeDefinition.", this);
                enabled = false;
                return;
            }
            Session = SimSession.Create(m_Mode, m_Seed);
            if (m_StartOnAwake)
                Session.Start();
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
