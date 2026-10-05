using System.Collections.Generic;
using NUnit.Framework;
using SPF.Contracts.Pooling;
using SPF.Shell.UI;
using UnityEngine;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;

namespace SPF.Tests.EditMode
{
    public class PoolingTests
    {
        sealed class Thing { public int Uses; public bool Live; }

        [Test]
        public void PrewarmedPoolNeverCallsTheFactoryInPlay()
        {
            var pool = new ObjectPool<Thing>(() => new Thing(), onGet: t => { t.Uses++; t.Live = true; }, onRelease: t => t.Live = false);
            pool.Prewarm(8);
            Assert.AreEqual(8, pool.Created);
            var held = new Thing[8];
            for (int round = 0; round < 50; round++)
            {
                for (int i = 0; i < 8; i++) held[i] = pool.Get();
                for (int i = 0; i < 8; i++) { Assert.IsTrue(held[i].Live); pool.Release(held[i]); }
            }
            Assert.AreEqual(8, pool.Created, "warm pool: no new objects");
            Assert.AreEqual(8, pool.PeakActive);
            Assert.AreEqual(0, pool.CountActive);
            Assert.That(() => { var t = pool.Get(); pool.Release(t); }, Is.Not.AllocatingGCMemory());

            var extra = pool.Get();
            pool.Release(extra);
            Assert.Throws<System.InvalidOperationException>(() => pool.Release(extra), "double release is caught");
        }

        [Test]
        public void ListPoolReusesLists()
        {
            List<int> first;
            using (ListPool<int>.Get(out first)) first.Add(1);
            using (ListPool<int>.Get(out var second))
            {
                Assert.AreSame(first, second);
                Assert.AreEqual(0, second.Count, "returned lists are cleared");
            }
            Assert.That(() => { using (ListPool<int>.Get(out var l)) { l.Add(3); l.Add(4); } }, Is.Not.AllocatingGCMemory());
        }

        [Test]
        public void TextBuilderFormatsWithoutAllocating()
        {
            var b = new TextBuilder(8);
            b.Append("t=").Append(7, 2).Append(' ').Append(-1234).Append(' ').Append(3.14159f, 2).Append(' ').Append(0.5f, 0).Append(' ').Append(-0.05f, 1);
            Assert.AreEqual("t=07 -1234 3.14 1 -0.1", b.ToString());

            string shown = b.ToString();
            Assert.AreSame(shown, b.ToStringIfChanged(shown), "same characters, same string");
            TestDelegate format = () =>
            {
                b.Clear().Append("Kills ").Append(123456).Append(" / ").Append(99.5f, 1);
                b.ContentEquals(shown);
            };
            format();   // JIT first: Mono materialises the delegate's string literals on its first call
            Assert.That(format, Is.Not.AllocatingGCMemory());
            Assert.AreEqual("42", NumberStrings.Get(42));
            Assert.AreSame(NumberStrings.Get(42), NumberStrings.Get(42));
        }

        [Test]
        public void CachedTextOnlyAssignsOnChange()
        {
            var go = new GameObject("label");
            try
            {
                var label = go.AddComponent<UnityEngine.UI.Text>();
                var text = new CachedText(label);
                for (int frame = 0; frame < 100; frame++)
                {
                    text.Begin().Append("Score ").Append(frame / 25);
                    text.Commit();
                }
                Assert.AreEqual("Score 3", label.text);
                Assert.AreEqual(4, text.Commits, "one string per visible change, not per frame");
                TestDelegate same = () => { text.Begin().Append("Score ").Append(3); text.Commit(); };
                same();
                Assert.That(same, Is.Not.AllocatingGCMemory());
            }
            finally { Object.DestroyImmediate(go); }
        }
    }
}

namespace SPF.Tests.EditMode
{
    public class BufferTextTests
    {
        [Test]
        public void BuildsGlyphQuadsWithoutStringsAndRevealsProgressively()
        {
            var go = new GameObject("buffer", typeof(RectTransform));
            try
            {
                ((RectTransform)go.transform).sizeDelta = new Vector2(2000, 200);
                var label = go.AddComponent<BufferText>();
                label.Font = UIFactory.Font != null ? UIFactory.Font : Font.CreateDynamicFontFromOSFont("Arial", 32);
                label.FontSize = 32;
                var vh = new UnityEngine.UI.VertexHelper();

                Assert.IsTrue(label.SetText("abc def"));
                Assert.IsFalse(label.SetText("abc def"), "same text: no rebuild");
                label.PopulateMesh(vh);
                Assert.AreEqual(6 * 4, vh.currentVertCount, "six glyph quads, spaces are not drawn");

                label.MaxVisible = 4;   // "abc "
                label.PopulateMesh(vh);
                Assert.AreEqual(3 * 4, vh.currentVertCount, "typewriter reveal");
                label.MaxVisible = int.MaxValue;

                // Counters every frame: no allocation once the buffer has grown.
                TestDelegate counters = () =>
                {
                    for (int i = 0; i < 100; i++) { label.Begin().Append("Kills ").Append(i * 37); label.Commit(); }
                };
                counters();
                Assert.That(counters, Is.Not.AllocatingGCMemory());

                // Wrap: a narrow box pushes the second word onto a lower line.
                ((RectTransform)go.transform).sizeDelta = new Vector2(80, 400);
                label.Wrap = true;
                label.SetText("abcd efgh");
                label.PopulateMesh(vh);
                var first = new UIVertex();
                var last = new UIVertex();
                vh.PopulateUIVertex(ref first, 0);
                vh.PopulateUIVertex(ref last, vh.currentVertCount - 1);
                Assert.Less(last.position.y, first.position.y - 10f, "second word wrapped below the first");
                Assert.AreEqual("abcd efgh", label.ToString());
            }
            finally { Object.DestroyImmediate(go); }
        }
    }
}
