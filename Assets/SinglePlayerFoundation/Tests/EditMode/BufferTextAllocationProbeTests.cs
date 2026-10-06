#if !SPF_DOTNET_HARNESS
using System;
using System.Text;
using NUnit.Framework;
using SPF.Shell.UI;
using SPF.Testing;
using UnityEngine;
using UnityEngine.UI;

namespace SPF.Tests.EditMode
{
    /// <summary>
    /// Explicit, real-Unity diagnostic for warmed font/mesh work. Reports allocations rather than
    /// imposing a new budget. Calibrated current-thread GC.Alloc samples do not cover deferred canvas
    /// work, other threads, or native allocations; use the full-game profiler capture for those.
    /// </summary>
    public class BufferTextAllocationProbeTests
    {
        const int FontSize = 32;
        const int WarmupMeshes = 1000;
        const int MeasuredMeshes = 1000;
        const int ExpectedVertices = (5 + 4 + 7 + 4) * 4; // Kills, four digits, Enemies, four digits

        sealed class FontRebuildCounter
        {
            public Font Target;
            public int Count;

            public void OnRebuilt(Font font)
            {
                if (font == Target) Count++;
            }
        }

        sealed class Sample
        {
            public readonly string Name;
            public readonly int[] Collections;
            public ManagedAllocationSample Allocations;
            public int FontRebuilds;

            public Sample(string name)
            {
                Name = name;
                Collections = new int[GC.MaxGeneration + 1];
            }
        }

        static void PopulateCounters(BufferText label, VertexHelper vertices, bool multiline, int value)
        {
            label.Begin().Append("Kills ").Append(value % 10000, 4)
                .Append(multiline ? '\n' : ' ').Append("Enemies ").Append((value * 37) % 10000, 4);
            label.Commit();
            label.PopulateMesh(vertices);
        }

        static void RunMeshes(BufferText label, VertexHelper vertices, bool multiline, int first, int count)
        {
            for (int i = 0; i < count; i++)
                PopulateCounters(label, vertices, multiline, first + i);
        }

        static void BeginCollections(Sample sample)
        {
            for (int generation = 0; generation < sample.Collections.Length; generation++)
                sample.Collections[generation] = GC.CollectionCount(generation);
        }

        static void EndCollections(Sample sample)
        {
            for (int generation = 0; generation < sample.Collections.Length; generation++)
                sample.Collections[generation] = GC.CollectionCount(generation) - sample.Collections[generation];
        }

        static void MeasureMeshes(ManagedAllocationProbe probe, Sample sample, BufferText label,
            VertexHelper vertices, FontRebuildCounter rebuilds, bool multiline, Action measured)
        {
            // Re-warm both numeric content and mesh capacity before every independent window.
            // A separator is the only content difference; both variants emit twenty glyph quads.
            RunMeshes(label, vertices, multiline, 0, WarmupMeshes);
            BeginCollections(sample);
            int rebuiltBefore = rebuilds.Count;
            sample.Allocations = probe.Measure(measured);
            sample.FontRebuilds = rebuilds.Count - rebuiltBefore;
            EndCollections(sample);
        }

        static void CheckGeometry(BufferText label, VertexHelper vertices, bool multiline)
        {
            Assert.AreEqual(ExpectedVertices, vertices.currentVertCount, "Both variants must retain all twenty glyph quads.");
            Assert.AreEqual(ExpectedVertices / 4 * 6, vertices.currentIndexCount, "Each glyph needs two triangles.");
            var first = new UIVertex();
            var second = new UIVertex();
            vertices.PopulateUIVertex(ref first, 0);
            vertices.PopulateUIVertex(ref second, (5 + 4) * 4); // first vertex of Enemies
            if (multiline)
                Assert.Less(second.position.y, first.position.y - FontSize * 0.5f, "Enemies must be on the lower line.");
            else
                Assert.Less(Mathf.Abs(second.position.y - first.position.y), FontSize * 0.25f, "Both words must remain on one line.");
            Assert.Greater(label.Length, 0);
        }

        static void RequestNewlines(Font font, int count)
        {
            for (int i = 0; i < count; i++)
                font.RequestCharactersInTexture("\n", FontSize, FontStyle.Normal);
        }

        static void MeasureNewlineRequests(ManagedAllocationProbe probe, Sample sample, Font font,
            FontRebuildCounter rebuilds, Action measured)
        {
            RequestNewlines(font, WarmupMeshes);
            BeginCollections(sample);
            int rebuiltBefore = rebuilds.Count;
            sample.Allocations = probe.Measure(measured);
            sample.FontRebuilds = rebuilds.Count - rebuiltBefore;
            EndCollections(sample);
        }

        [Test]
        [Explicit("Allocation diagnostic: run this exact test in real Unity; leaves production behavior and GC budgets unchanged.")]
        public void WarmSinglelineAndMultilineMeshes_ReportAllocations()
        {
            var gameObject = new GameObject("BufferText allocation probe", typeof(RectTransform));
            var rebuilds = new FontRebuildCounter();
            Font.textureRebuilt += rebuilds.OnRebuilt;
            try
            {
                ((RectTransform)gameObject.transform).sizeDelta = new Vector2(2000f, 200f);
                var label = gameObject.AddComponent<BufferText>();
                label.Font = BufferText.SharedFont;
                label.FontSize = FontSize;
                label.Wrap = false;
                rebuilds.Target = label.Font;
                Assert.IsNotNull(label.Font, "A real Unity font is required.");

                using var vertices = new VertexHelper();
                var samples = new[]
                {
                    new Sample("singleline A"),
                    new Sample("multiline A"),
                    new Sample("multiline B"),
                    new Sample("singleline B"),
                    new Sample("newline font requests only"),
                };
                var warmup = new Sample("discarded instrumentation warm-up");
                Action singleline = () => RunMeshes(label, vertices, false, WarmupMeshes, MeasuredMeshes);
                Action multiline = () => RunMeshes(label, vertices, true, WarmupMeshes, MeasuredMeshes);
                Action newlineRequests = () => RequestNewlines(label.Font, MeasuredMeshes);
                using var probe = new ManagedAllocationProbe();
                probe.Calibrate();
                // Warm the measuring methods, counters, font, character cache, label buffers, and
                // VertexHelper before keeping any results. Newline is present in the warm-up too.
                MeasureMeshes(probe, warmup, label, vertices, rebuilds, false, singleline);
                CheckGeometry(label, vertices, false);
                MeasureMeshes(probe, warmup, label, vertices, rebuilds, true, multiline);
                CheckGeometry(label, vertices, true);
                MeasureNewlineRequests(probe, warmup, label.Font, rebuilds, newlineRequests);
                bool newlineBefore = label.Font.GetCharacterInfo('\n', out _, FontSize, FontStyle.Normal);

                var calibrationBefore = probe.Calibrate();
                // ABBA order reduces the chance that first-use/global font state masquerades as
                // a newline effect. No delegates, strings, logging, or assertions inside a window.
                MeasureMeshes(probe, samples[0], label, vertices, rebuilds, false, singleline);
                CheckGeometry(label, vertices, false);
                MeasureMeshes(probe, samples[1], label, vertices, rebuilds, true, multiline);
                CheckGeometry(label, vertices, true);
                MeasureMeshes(probe, samples[2], label, vertices, rebuilds, true, multiline);
                CheckGeometry(label, vertices, true);
                MeasureMeshes(probe, samples[3], label, vertices, rebuilds, false, singleline);
                CheckGeometry(label, vertices, false);
                MeasureNewlineRequests(probe, samples[4], label.Font, rebuilds, newlineRequests);
                var calibrationAfter = probe.Calibrate();
                bool newlineAfter = label.Font.GetCharacterInfo('\n', out _, FontSize, FontStyle.Normal);

                var report = new StringBuilder();
                report.Append("BufferText warmed allocation probe: ").Append(MeasuredMeshes).AppendLine(" calls per window");
                report.Append("Unity ").Append(Application.unityVersion).Append("; font ").Append(label.Font.name)
                    .Append("; dynamic ").Append(label.Font.dynamic).Append("; font size ").Append(FontSize).AppendLine();
                report.Append("Metric: ").Append(probe.Metric).Append("; retained-array/empty calibration before=")
                    .Append(calibrationBefore.RetainedArrays.Value).Append('/').Append(calibrationBefore.Empty.Value)
                    .Append(", after=").Append(calibrationAfter.RetainedArrays.Value).Append('/').Append(calibrationAfter.Empty.Value).AppendLine();
                report.Append("Newline GetCharacterInfo after warm-up: ").Append(newlineBefore)
                    .Append("; after isolated requests: ").Append(newlineAfter).AppendLine();
                for (int i = 0; i < samples.Length; i++)
                {
                    var sample = samples[i];
                    report.Append(sample.Name).Append(": ").Append(sample.Allocations.Value).Append(" current-thread ")
                        .Append(sample.Allocations.Metric).Append("; ").Append(sample.FontRebuilds)
                        .Append(" font texture rebuild callbacks; independent process-wide collections");
                    for (int generation = 0; generation < sample.Collections.Length; generation++)
                        report.Append(" gen").Append(generation).Append('=').Append(sample.Collections[generation]);
                    report.AppendLine();
                }
                report.AppendLine("Direct PopulateMesh probe only: deferred canvas work, other threads, and native allocations are not covered by current-thread allocation samples.");
                TestContext.WriteLine(report.ToString());
            }
            finally
            {
                Font.textureRebuilt -= rebuilds.OnRebuilt;
                UnityEngine.Object.DestroyImmediate(gameObject);
            }
        }
    }
}
#endif
