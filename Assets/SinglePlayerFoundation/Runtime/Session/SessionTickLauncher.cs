using UnityEngine;

namespace SPF.Runtime.Session
{
    /// <summary>Schedules the next tick after every other LateUpdate (see <see cref="SessionHost.OverlapRendering"/>).</summary>
    [DefaultExecutionOrder(32000)]
    [AddComponentMenu("")]
    public sealed class SessionTickLauncher : MonoBehaviour
    {
        internal SessionHost Host { get; set; }

        void LateUpdate()
        {
            if (Host != null && Host.isActiveAndEnabled) Host.LaunchTicks();
        }
    }
}
