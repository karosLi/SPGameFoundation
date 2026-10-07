using UnityEngine;
using UnityEngine.UI;

namespace SPF.Shell.UI
{
    /// <summary>Original chamfered enamel panel and thin bronze inlay, drawn in a single UGUI mesh.
    /// An Image subclass preserves the factory's existing Graphic/Image input contract. No Update,
    /// generated textures, materials, mesh arrays or geometry allocations are needed.</summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class SanctuaryPanelGraphic : Image
    {
        public float Corner = 12f;
        public float Border = 1.2f;
        public bool OutlineOnly;

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            var rect = GetPixelAdjustedRect();
            if (rect.width <= 0 || rect.height <= 0) return;
            float cut = Mathf.Min(Corner, Mathf.Min(rect.width, rect.height) * .20f);
            float edge = Mathf.Min(Border, Mathf.Min(rect.width, rect.height) * .08f);
            var bronze = SanctuaryUiTheme.Bronze;
            bronze.a *= OutlineOnly ? .64f : color.a * .78f;
            for (int i = 0; i < 8; i++)
            {
                var a = Point(rect, cut, i); var b = Point(rect, cut, (i + 1) % 8);
                if (!OutlineOnly)
                {
                    int n = vh.currentVertCount;
                    Vertex(vh, rect.center, Face(.55f)); Vertex(vh, a, Face((a.y - rect.yMin) / rect.height));
                    Vertex(vh, b, Face((b.y - rect.yMin) / rect.height));
                    vh.AddTriangle(n, n + 1, n + 2);
                }
                var inner = new Rect(rect.xMin + edge, rect.yMin + edge, rect.width - edge * 2, rect.height - edge * 2);
                Quad(vh, a, b, Point(inner, Mathf.Max(0, cut - edge * .5f), (i + 1) % 8), Point(inner, Mathf.Max(0, cut - edge * .5f), i), bronze);
            }
            // A small, quiet highlight at the top catches the eye without a drop-shadow overdraw pass.
            if (rect.width > cut * 2 + 10 && rect.height > 4)
            {
                float y = rect.yMax - edge * 3;
                var glint = SanctuaryUiTheme.Ivory; glint.a = OutlineOnly ? .13f : color.a * .13f;
                Quad(vh, new Vector2(rect.xMin + cut + 5, y), new Vector2(rect.xMax - cut - 5, y),
                    new Vector2(rect.xMax - cut - 5, y - .8f), new Vector2(rect.xMin + cut + 5, y - .8f), glint);
            }
        }

        Color Face(float height)
        {
            float light = .82f + height * .24f;
            return new Color(color.r * light, color.g * light, color.b * light, color.a);
        }

        static Vector2 Point(Rect r, float c, int i)
        {
            switch (i)
            {
                case 0: return new Vector2(r.xMin + c, r.yMin);
                case 1: return new Vector2(r.xMax - c, r.yMin);
                case 2: return new Vector2(r.xMax, r.yMin + c);
                case 3: return new Vector2(r.xMax, r.yMax - c);
                case 4: return new Vector2(r.xMax - c, r.yMax);
                case 5: return new Vector2(r.xMin + c, r.yMax);
                case 6: return new Vector2(r.xMin, r.yMax - c);
                default: return new Vector2(r.xMin, r.yMin + c);
            }
        }
        static void Vertex(VertexHelper vh, Vector2 p, Color tint)
        { var v = UIVertex.simpleVert; v.position = new Vector3(p.x, p.y, 0); v.color = tint; vh.AddVert(v); }
        static void Quad(VertexHelper vh, Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color tint)
        {
            int n = vh.currentVertCount; Vertex(vh, a, tint); Vertex(vh, b, tint); Vertex(vh, c, tint); Vertex(vh, d, tint);
            vh.AddTriangle(n, n + 1, n + 2); vh.AddTriangle(n, n + 2, n + 3);
        }
    }
}
