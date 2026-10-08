#if !SPF_DOTNET_HARNESS
using System;
using UnityEngine;

namespace BrawlerFoundation.Tests.PlayMode
{
    // Test-owned hook: production BwRenderer prepares its actual stream at LateUpdate order 500.
    // Observe before SessionTickLauncher(32000) starts the next overlapped tick.
    // This hook only observes it. It does not invoke any simulation or pose update.
    [DefaultExecutionOrder(1000)]
    public sealed class BwBladeGripCloseupCaptureDriver : MonoBehaviour
    {
        public Action Observe;
        public int LateCallbacks { get; private set; }
        void LateUpdate() { LateCallbacks++; Observe?.Invoke(); }
        void OnDisable() { Observe = null; }
    }
}
#endif
