#if UNITY_EDITOR && !SPF_DOTNET_HARNESS
using Unity.Profiling;
using UnityEngine;

namespace ShooterFoundation.Tests.PlayMode
{
    // Same Update phase as FrameGovernor, without its platform/quality/input policy.
    [DefaultExecutionOrder(950)]
    public sealed class ShooterPlatformRawReader : MonoBehaviour
    {
        internal ProfilerRecorder Recorder;
        internal long LastBytes, Bytes;
        internal int Frames, AllocatingFrames, ReadFrame;
        void OnEnable() => Recorder = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame");
        void OnDisable() => Recorder.Dispose();
        void Update()
        {
            ReadFrame = Time.frameCount;
            LastBytes = Recorder.Valid ? Recorder.LastValue : -1;
            Bytes += LastBytes;
            if (LastBytes > 0) AllocatingFrames++;
            Frames++;
        }
        internal void ResetWindow() { LastBytes = 0; Bytes = 0; Frames = AllocatingFrames = 0; }
    }

}
#endif
