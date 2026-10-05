using System.IO;
using UnityEngine;

namespace SPF.Testing
{
    /// <summary>Appends steady-state GC measurements to Artifacts/Screenshots/perf-gc.txt (published by CI).</summary>
    public static class GcReport
    {
        public static void Write(string scenario, int frames, int framesAllocating, long bytes)
        {
            string dir = Path.Combine(Application.dataPath, "..", "Artifacts", "Screenshots");
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "perf-gc.txt"),
                $"{scenario}: {framesAllocating} of {frames} frames allocated, {bytes} bytes\n");
        }
    }
}
