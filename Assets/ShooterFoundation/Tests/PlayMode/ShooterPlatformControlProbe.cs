#if UNITY_EDITOR && !SPF_DOTNET_HARNESS
using System;
using System.Runtime.CompilerServices;
using SPF.Shell.Performance;
using UnityEngine;

namespace ShooterFoundation.Tests.PlayMode
{
    [DefaultExecutionOrder(900)]
    public sealed class ShooterPlatformControlProbe : MonoBehaviour
    {
        internal static readonly string[] Names = {
            "Update.Empty", "Update.Retained", "LateUpdate.Empty", "LateUpdate.Retained",
            "Coroutine.Empty", "Coroutine.Retained"
        };
        internal readonly int[] SourceFrames = new int[6], Emissions = new int[6], RequestedFrames = new int[6], ActualPhases = new int[6];
        internal readonly double[] SourceTimes = new double[6];
        readonly byte[][] m_Retained = new byte[3][];
        internal FrameGovernor Governor;
        int m_Control = -1, m_Target = -1;
        internal static int Payload(int control) => (control & 1) == 0 ? 0 : 4096 << (control / 2);
        internal void Warm()
        {
            for (int i = 0; i < 6; i++) Emit(i, i / 2);
            Array.Clear(SourceFrames, 0, 6); Array.Clear(SourceTimes, 0, 6); Array.Clear(Emissions, 0, 6);
        }
        internal void Arm(int control, int target) { m_Control = control; m_Target = target; RequestedFrames[control] = target; }
        void Update()
        {
            // Matches Shooter Playing's pre-governor KeepAwake; no input or thermal override.
            if (Governor != null) Governor.KeepAwake();
            if (m_Control >= 0 && m_Control < 2) Pulse(0);
        }
        void LateUpdate() { if (m_Control >= 2 && m_Control < 4) Pulse(1); }
        internal void CoroutinePulse() { if (m_Control >= 4) Pulse(2); }
        void Pulse(int phase)
        {
            if (Time.frameCount != m_Target) return;
            Emit(m_Control, phase); m_Control = -1;
        }
        [MethodImpl(MethodImplOptions.NoInlining)]
        void Emit(int control, int phase)
        {
            int payload = Payload(control);
            if (payload > 0)
            {
                var retained = new byte[payload];
                retained[0] = 73; retained[payload - 1] = 19;
                m_Retained[control / 2] = retained;
            }
            SourceFrames[control] = Time.frameCount;
            ActualPhases[control] = phase;
            SourceTimes[control] = Time.realtimeSinceStartupAsDouble;
            Emissions[control]++;
        }
        internal bool Retained(int control)
        {
            int payload = Payload(control);
            if (payload == 0) return true;
            byte[] value = m_Retained[control / 2];
            return value != null && value.Length == payload && value[0] == 73 && value[payload - 1] == 19;
        }
        void OnDestroy() => GC.KeepAlive(m_Retained);
    }
}
#endif
