#if UNITY_EDITOR && !SPF_DOTNET_HARNESS
using System;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.Profiling;

namespace ShooterFoundation.Tests.PlayMode
{
    // Test-only pulses. Enabled only after the original budget endpoint. Update runs before the
    // governor (950); LateUpdate and the coroutine run after its Update. All three must map to
    // the same subsequent completed-frame read, not whichever read is closest to the allocation.
    [DefaultExecutionOrder(900)]
    public sealed class ShooterAllocationProbe : MonoBehaviour
    {
        internal static readonly string[] Markers = {
            "ShooterGC.Control.Update.Empty", "ShooterGC.Control.Update.Retained",
            "ShooterGC.Control.LateUpdate.Empty", "ShooterGC.Control.LateUpdate.Retained",
            "ShooterGC.Control.Coroutine.Empty", "ShooterGC.Control.Coroutine.Retained"
        };
        internal readonly int[] Frames = new int[6];
        internal readonly int[] Emissions = new int[6];
        readonly byte[][] m_Retained = new byte[3][];
        int m_Control = -1, m_Target = -1;

        internal static int Payload(int control) => (control & 1) == 0 ? 0 : 4096 << (control / 2);

        internal void Warm()
        {
            for (int i = 0; i < 6; i++) Emit(i);
            Array.Clear(Frames, 0, Frames.Length);
            Array.Clear(Emissions, 0, Emissions.Length);
        }

        internal void Arm(int control, int target)
        {
            m_Control = control; m_Target = target; enabled = true;
        }

        void Update() { if (m_Control >= 0 && m_Control < 2) TryPulse(); }
        void LateUpdate() { if (m_Control >= 2 && m_Control < 4) TryPulse(); }
        internal void CoroutinePulse() { if (m_Control >= 4) TryPulse(); }
        void TryPulse()
        {
            if (Time.frameCount != m_Target) return;
            Emit(m_Control);
            m_Control = -1;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        void Emit(int control)
        {
            Profiler.BeginSample(Markers[control]);
            try
            {
                int payload = Payload(control);
                if (payload > 0)
                {
                    var retained = new byte[payload];
                    retained[0] = 73; retained[payload - 1] = 19;
                    m_Retained[control / 2] = retained;
                }
            }
            finally { Profiler.EndSample(); }
            Frames[control] = Time.frameCount;
            Emissions[control]++;
        }

        void OnDestroy() => GC.KeepAlive(m_Retained);
    }
}
#endif
