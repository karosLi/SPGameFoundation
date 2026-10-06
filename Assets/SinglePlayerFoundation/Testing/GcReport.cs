using System.IO;
using UnityEngine;

namespace SPF.Testing
{
    /// <summary>Appends managed-allocation measurements (not collection counts unless separately supplied) to Artifacts/perf-gc.txt (published by CI).</summary>
    public static class GcReport
    {
        public static void Write(string scenario, int frames, int framesAllocating, long bytes, int collections = -1)
        {
            string dir = Path.Combine(Application.dataPath, "..", "Artifacts");
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "perf-gc.txt"),
                $"{scenario}: {framesAllocating} of {frames} frames allocated, {bytes} bytes"
                + (collections >= 0 ? $", process-wide generation-0 collections={collections}" : "") + "\n");
        }
    }
}
