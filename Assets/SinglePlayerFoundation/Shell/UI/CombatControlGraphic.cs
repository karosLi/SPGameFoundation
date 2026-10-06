using UnityEngine;
using UnityEngine.UI;

namespace SPF.Shell.UI
{
    /// <summary>Small vector control art, built by UGUI only when dirty. No runtime textures/materials.</summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class CombatControlGraphic : MaskableGraphic
    {
        public int Glyph = -1;
        public float Fraction = 1f;
        public bool Disc;
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            var rect = GetPixelAdjustedRect();
            Vector2 center = rect.center;
            float r = Mathf.Min(rect.width, rect.height) * .47f;
            if (Disc) { Circle(vh, center, r); return; }
            if (Glyph < 0) { Ring(vh, center, r, r * .075f, Mathf.Clamp01(Fraction)); return; }
            float q = r * .65f;
            if (Glyph == 0) // fist: knuckles and wrist
            {
                Box(vh, center + new Vector2(-q * .5f, 0), new Vector2(q, q * .66f));
                Box(vh, center + new Vector2(-q * .24f, -q * .62f), new Vector2(q * .54f, q * .7f));
                for (int i = 0; i < 4; i++) Box(vh, center + new Vector2(-q * .48f + i * q * .25f, q * .42f), new Vector2(q * .19f, q * .26f));
            }
            else if (Glyph == 1) // bent kick
            {
                Line(vh, center + new Vector2(-q * .6f, q * .8f), center + new Vector2(-q * .25f, 0), q * .22f);
                Line(vh, center + new Vector2(-q * .25f, 0), center + new Vector2(q * .75f, q * .2f), q * .22f);
                Line(vh, center + new Vector2(q * .75f, q * .2f), center + new Vector2(q * .8f, q * .6f), q * .22f);
            }
            else if (Glyph == 2) // pulse
            {
                Ring(vh, center, q * .45f, q * .11f, 1);
                for (int i = 0; i < 8; i++)
                {
                    float a = i * Mathf.PI / 4;
                    var d = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                    Line(vh, center + d * q * .65f, center + d * q, q * .09f);
                }
            }
            else // directional blink
            {
                Line(vh, center + new Vector2(-q * .7f, -q * .6f), center + new Vector2(q * .6f, q * .6f), q * .17f);
                Line(vh, center + new Vector2(-q * .2f, q * .6f), center + new Vector2(q * .6f, q * .6f), q * .17f);
                Line(vh, center + new Vector2(q * .6f, -q * .2f), center + new Vector2(q * .6f, q * .6f), q * .17f);
            }
        }
        void Vertex(VertexHelper vh, Vector2 p)
        { var v = UIVertex.simpleVert; v.position = new Vector3(p.x, p.y, 0); v.color = color; vh.AddVert(v); }
        void Quad(VertexHelper vh, Vector2 a, Vector2 b, Vector2 c, Vector2 d)
        { int n = vh.currentVertCount; Vertex(vh, a); Vertex(vh, b); Vertex(vh, c); Vertex(vh, d); vh.AddTriangle(n, n + 1, n + 2); vh.AddTriangle(n, n + 2, n + 3); }
        void Box(VertexHelper vh, Vector2 p, Vector2 size) => Quad(vh, p, p + new Vector2(size.x, 0), p + size, p + new Vector2(0, size.y));
        void Line(VertexHelper vh, Vector2 a, Vector2 b, float width)
        { var d = (b - a).normalized; var n = new Vector2(-d.y, d.x) * width * .5f; Quad(vh, a - n, b - n, b + n, a + n); }
        void Circle(VertexHelper vh, Vector2 c, float radius)
        {
            for (int i = 0; i < 40; i++)
            {
                float a = i * Mathf.PI * 2 / 40, b = (i + 1) * Mathf.PI * 2 / 40;
                int n = vh.currentVertCount; Vertex(vh, c); Vertex(vh, c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius);
                Vertex(vh, c + new Vector2(Mathf.Cos(b), Mathf.Sin(b)) * radius); vh.AddTriangle(n, n + 1, n + 2);
            }
        }
        void Ring(VertexHelper vh, Vector2 c, float radius, float width, float fraction)
        {
            int count = Mathf.CeilToInt(48 * fraction);
            for (int i = 0; i < count; i++)
            {
                float a = Mathf.PI * .5f - i * Mathf.PI * 2 / 48;
                float b = Mathf.PI * .5f - Mathf.Min(i + 1, 48 * fraction) * Mathf.PI * 2 / 48;
                var d = new Vector2(Mathf.Cos(a), Mathf.Sin(a)); var e = new Vector2(Mathf.Cos(b), Mathf.Sin(b));
                Quad(vh, c + d * radius, c + e * radius, c + e * (radius - width), c + d * (radius - width));
            }
        }
    }
}
