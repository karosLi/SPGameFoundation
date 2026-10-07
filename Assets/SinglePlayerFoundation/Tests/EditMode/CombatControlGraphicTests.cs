#if !SPF_DOTNET_HARNESS
using NUnit.Framework;
using SPF.Contracts;
using SPF.Shell.UI;
using UnityEngine;
using UnityEngine.UI;

namespace SPF.Tests.EditMode
{
    /// <summary>Engine-only coverage: stubs do not install required components or submit UGUI geometry.</summary>
    public class CombatControlGraphicTests
    {
        sealed class AllIconsSource : IMobileCombatHudSource
        {
            public int SlotCount => 4;
            public int TickRate => 30;
            public bool Playing => true;
            public string SlotLabel(int slot) => "SKILL";
            public SkillSlotSnapshot ReadSlot(int slot) => new SkillSlotSnapshot(
                new SkillSlotDefinition(slot + 1, slot, SkillActivation.Tap, 30), 1, 0, true);
        }

        static int SubmittedVertices(Graphic graphic)
        {
            // Unity 2022.3 returns the renderer-owned mesh; do not destroy the borrowed object.
            var mesh = graphic.GetComponent<CanvasRenderer>().GetMesh();
            return mesh != null ? mesh.vertexCount : 0;
        }

        [TestCase(-1, false)]
        [TestCase(-1, true)]
        [TestCase(0, false)]
        [TestCase(1, false)]
        [TestCase(2, false)]
        [TestCase(3, false)]
        [TestCase(CombatControlGraphic.HealGlyph, false)]
        [TestCase(CombatControlGraphic.JumpGlyph, false)]
        [TestCase(CombatControlGraphic.SwitchGlyph, false)]
        [TestCase(CombatControlGraphic.BladeGlyph, false)]
        [TestCase(CombatControlGraphic.StaffGlyph, false)]
        [TestCase(CombatControlGraphic.BowGlyph, false)]
        public void DirectAddInstallsRendererAndSubmitsEveryVectorShape(int glyph, bool disc)
        {
            var root = new GameObject("DirectGraphicCanvas");
            try
            {
                var canvas = UIFactory.CreateCanvas(root.transform);
                // No CanvasRenderer is supplied: direct/inspector AddComponent must install it.
                var go = new GameObject("DirectGraphic", typeof(RectTransform)); go.transform.SetParent(canvas.transform, false);
                var graphic = go.AddComponent<CombatControlGraphic>();
                Assert.IsNotNull(go.GetComponent<CanvasRenderer>());
                Assert.IsNotNull(go.GetComponent<RectTransform>());
                graphic.rectTransform.sizeDelta = new Vector2(160, 160);
                graphic.Glyph = glyph; graphic.Disc = disc; graphic.color = Color.white;
                graphic.SetAllDirty(); Canvas.ForceUpdateCanvases();
                Assert.Greater(SubmittedVertices(graphic), 0, "required components must result in real CanvasRenderer mesh submission");
            }
            finally { Object.DestroyImmediate(root); }
        }

        [TestCase(CombatControlGraphic.HealGlyph, CombatControlGraphic.BlinkGlyph)]
        [TestCase(CombatControlGraphic.JumpGlyph, CombatControlGraphic.PulseGlyph)]
        [TestCase(CombatControlGraphic.SwitchGlyph, CombatControlGraphic.KickGlyph)]
        [TestCase(CombatControlGraphic.BladeGlyph, CombatControlGraphic.FistGlyph)]
        [TestCase(CombatControlGraphic.StaffGlyph, CombatControlGraphic.FistGlyph)]
        [TestCase(CombatControlGraphic.BowGlyph, CombatControlGraphic.FistGlyph)]
        public void NewGlyphSubmitsDistinctGeometryFromPreviousSlotFallback(int glyph, int previous)
        {
            var root = new GameObject("DistinctGlyphCanvas");
            try
            {
                var canvas = UIFactory.CreateCanvas(root.transform);
                var go = new GameObject("Glyph", typeof(RectTransform)); go.transform.SetParent(canvas.transform, false);
                var graphic = go.AddComponent<CombatControlGraphic>(); graphic.rectTransform.sizeDelta = new Vector2(84, 84);
                graphic.Glyph = previous; graphic.SetAllDirty(); Canvas.ForceUpdateCanvases();
                var oldVertices = go.GetComponent<CanvasRenderer>().GetMesh().vertices;
                graphic.Glyph = glyph; graphic.SetVerticesDirty(); Canvas.ForceUpdateCanvases();
                var mesh = go.GetComponent<CanvasRenderer>().GetMesh();
                Assert.Greater(mesh.vertexCount, 0);
                CollectionAssert.AreNotEqual(oldVertices, mesh.vertices, "the new skill must not silently fall through to its old unrelated symbol");
                foreach (var vertex in mesh.vertices)
                    Assert.IsTrue(graphic.rectTransform.rect.Contains(new Vector2(vertex.x, vertex.y)), "fallback stays inside its icon rectangle");
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void HudFactoryCreatesRenderableJoystickAndAllFourSkillIcons()
        {
            var root = new GameObject("FactoryGraphicCanvas");
            try
            {
                var canvas = UIFactory.CreateCanvas(root.transform);
                var hud = root.AddComponent<MobileCombatHud>();
                hud.Build(canvas.transform, new AllIconsSource(), preferLandscape: true);
                hud.Refresh(); Canvas.ForceUpdateCanvases();
                var vectors = hud.SafeRoot.GetComponentsInChildren<CombatControlGraphic>(true);
                Assert.AreEqual(15, vectors.Length, "three joystick pieces and three vector graphics per skill");
                foreach (var graphic in hud.SafeRoot.GetComponentsInChildren<Graphic>(true))
                {
                    Assert.IsNotNull(graphic.GetComponent<CanvasRenderer>(), graphic.name + " must have a renderer before any raycaster access");
                    bool acceptsTouch = graphic.GetComponent<SkillControl>() != null || graphic.GetComponent<VirtualJoystick>() != null;
                    Assert.AreEqual(acceptsTouch, graphic.raycastTarget, graphic.name + " must not accidentally intercept a different control");
                    if (graphic.isActiveAndEnabled)
                        Assert.Greater(SubmittedVertices(graphic), 0, graphic.name + " must submit actual factory geometry");
                }
                var controls = hud.SafeRoot.Find("CombatControls");
                Assert.IsFalse(controls.Find("JoystickRing").GetComponent<CombatControlGraphic>().raycastTarget);
                Assert.IsTrue(hud.Joystick.GetComponent<Image>().raycastTarget);
                foreach (var button in hud.Buttons)
                {
                    Assert.IsTrue(button.GetComponent<CombatControlGraphic>().raycastTarget);
                    Assert.IsFalse(button.transform.Find("Icon").GetComponent<CombatControlGraphic>().raycastTarget);
                    Assert.IsFalse(button.transform.Find("Recharge").GetComponent<CombatControlGraphic>().raycastTarget);
                }
            }
            finally { Object.DestroyImmediate(root); }
        }
    }
}
#endif
