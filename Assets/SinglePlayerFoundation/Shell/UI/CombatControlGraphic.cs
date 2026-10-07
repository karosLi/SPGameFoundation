using SPF.Contracts;
using SPF.Contracts.Weapons;
using UnityEngine;
using UnityEngine.UI;

namespace SPF.Shell.UI
{
    /// <summary>Optional presentation-only glyph selection, read after the authoritative slot snapshot.
    /// The snapshot's IconId still selects custom assets, which take precedence over this fallback.</summary>
    public interface IMobileCombatHudGlyphSource
    {
        int ReadFallbackGlyph(int slot, in SkillSlotSnapshot snapshot);
    }

    /// <summary>Small vector control art, built by UGUI only when dirty. No runtime textures/materials.</summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class CombatControlGraphic : MaskableGraphic
    {
        public const int FistGlyph = 0, KickGlyph = 1, PulseGlyph = 2, BlinkGlyph = 3;
        public const int HealGlyph = 4, JumpGlyph = 5, SwitchGlyph = 6, BladeGlyph = 7, StaffGlyph = 8, BowGlyph = 9;

        /// <summary>Resolve authored action families without coupling icons to inventory content IDs.</summary>
        public static int WeaponGlyph(WeaponActionFamily family)
        {
            switch (family)
            {
                case WeaponActionFamily.Slash:
                case WeaponActionFamily.Thrust: return BladeGlyph;
                case WeaponActionFamily.Cast: return StaffGlyph;
                case WeaponActionFamily.Draw: return BowGlyph;
                default: return FistGlyph;
            }
        }

        public int Glyph = -1;
        public float Fraction = 1f;
        public bool Disc;
        public bool Enamel;
        public bool Compass;
        public float RingWidth = .075f;
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            var rect = GetPixelAdjustedRect();
            Vector2 center = rect.center;
            float r = Mathf.Min(rect.width, rect.height) * .47f;
            if (Disc) { if (Enamel) Medallion(vh, center, r); else Circle(vh, center, r); return; }
            if (Glyph < 0) { Ring(vh, center, r, r * RingWidth, Mathf.Clamp01(Fraction)); return; }
            float q = r * .65f;
            if (Glyph == FistGlyph) // fist: knuckles and wrist
            {
                Box(vh, center + new Vector2(-q * .5f, 0), new Vector2(q, q * .66f));
                Box(vh, center + new Vector2(-q * .24f, -q * .62f), new Vector2(q * .54f, q * .7f));
                for (int i = 0; i < 4; i++) Box(vh, center + new Vector2(-q * .48f + i * q * .25f, q * .42f), new Vector2(q * .19f, q * .26f));
            }
            else if (Glyph == KickGlyph) // bent kick
            {
                Line(vh, center + new Vector2(-q * .6f, q * .8f), center + new Vector2(-q * .25f, 0), q * .22f);
                Line(vh, center + new Vector2(-q * .25f, 0), center + new Vector2(q * .75f, q * .2f), q * .22f);
                Line(vh, center + new Vector2(q * .75f, q * .2f), center + new Vector2(q * .8f, q * .6f), q * .22f);
            }
            else if (Glyph == PulseGlyph) // pulse
            {
                Ring(vh, center, q * .45f, q * .11f, 1);
                for (int i = 0; i < 8; i++)
                {
                    float a = i * Mathf.PI / 4;
                    var d = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                    Line(vh, center + d * q * .65f, center + d * q, q * .09f);
                }
            }
            else if (Glyph == HealGlyph) // medical cross
            {
                Box(vh, center + new Vector2(-q * .24f, -q * .78f), new Vector2(q * .48f, q * 1.56f));
                Box(vh, center + new Vector2(-q * .78f, -q * .24f), new Vector2(q * .54f, q * .48f));
                Box(vh, center + new Vector2(q * .24f, -q * .24f), new Vector2(q * .54f, q * .48f));
            }
            else if (Glyph == JumpGlyph) // rise from a ground line
            {
                Line(vh, center + new Vector2(-q * .7f, -q * .75f), center + new Vector2(q * .7f, -q * .75f), q * .13f);
                Line(vh, center + new Vector2(0, -q * .4f), center + new Vector2(0, q * .78f), q * .18f);
                Line(vh, center + new Vector2(-q * .45f, q * .28f), center + new Vector2(0, q * .78f), q * .18f);
                Line(vh, center + new Vector2(q * .45f, q * .28f), center + new Vector2(0, q * .78f), q * .18f);
            }
            else if (Glyph == SwitchGlyph) // opposing arrows
            {
                for (int side = -1; side <= 1; side += 2)
                {
                    var tip = center + new Vector2(side * q * .78f, side * q * .34f);
                    Line(vh, center + new Vector2(-side * q * .78f, side * q * .34f), tip, q * .16f);
                    Line(vh, tip + new Vector2(-side * q * .37f, q * .3f), tip, q * .16f);
                    Line(vh, tip + new Vector2(-side * q * .37f, -q * .3f), tip, q * .16f);
                }
            }
            else if (Glyph == BladeGlyph) // blade, guard and grip
            {
                var left = center + new Vector2(q * .49f, q * .67f);
                var right = center + new Vector2(q * .67f, q * .49f);
                Quad(vh, center + new Vector2(-q * .43f, -q * .25f), center + new Vector2(-q * .25f, -q * .43f), right, left);
                int n = vh.currentVertCount; Vertex(vh, left); Vertex(vh, right); Vertex(vh, center + new Vector2(q * .8f, q * .8f));
                vh.AddTriangle(n, n + 1, n + 2);
                Line(vh, center + new Vector2(-q * .65f, -q * .13f), center + new Vector2(-q * .13f, -q * .65f), q * .16f);
                Line(vh, center + new Vector2(-q * .39f, -q * .39f), center + new Vector2(-q * .76f, -q * .76f), q * .18f);
            }
            else if (Glyph == StaffGlyph) // staff with a ringed orb
            {
                Line(vh, center + new Vector2(-q * .62f, -q * .82f), center + new Vector2(q * .26f, q * .28f), q * .17f);
                Ring(vh, center + new Vector2(q * .43f, q * .52f), q * .34f, q * .12f, 1);
                Circle(vh, center + new Vector2(q * .43f, q * .52f), q * .1f);
            }
            else if (Glyph == BowGlyph) // curved bow, taut string and arrow
            {
                for (int i = 0; i < 12; i++)
                {
                    float a = -Mathf.PI * .5f + i * Mathf.PI / 12;
                    float b = -Mathf.PI * .5f + (i + 1) * Mathf.PI / 12;
                    Line(vh, center + new Vector2((-.48f + Mathf.Cos(a) * .8f) * q, Mathf.Sin(a) * q * .86f),
                        center + new Vector2((-.48f + Mathf.Cos(b) * .8f) * q, Mathf.Sin(b) * q * .86f), q * .13f);
                }
                Line(vh, center + new Vector2(-q * .48f, -q * .86f), center + new Vector2(-q * .48f, q * .86f), q * .07f);
                Line(vh, center + new Vector2(-q * .72f, 0), center + new Vector2(q * .84f, 0), q * .12f);
                Line(vh, center + new Vector2(q * .52f, q * .26f), center + new Vector2(q * .84f, 0), q * .14f);
                Line(vh, center + new Vector2(q * .52f, -q * .26f), center + new Vector2(q * .84f, 0), q * .14f);
            }
            else // directional blink, also retained for unknown project icon IDs
            {
                Line(vh, center + new Vector2(-q * .7f, -q * .6f), center + new Vector2(q * .6f, q * .6f), q * .17f);
                Line(vh, center + new Vector2(-q * .2f, q * .6f), center + new Vector2(q * .6f, q * .6f), q * .17f);
                Line(vh, center + new Vector2(q * .6f, -q * .2f), center + new Vector2(q * .6f, q * .6f), q * .17f);
            }
        }
        void Vertex(VertexHelper vh, Vector2 p)
        { var v = UIVertex.simpleVert; v.position = new Vector3(p.x, p.y, 0); v.color = color; vh.AddVert(v); }
        void Vertex(VertexHelper vh, Vector2 p, Color tint)
        { var v = UIVertex.simpleVert; v.position = new Vector3(p.x, p.y, 0); v.color = tint; vh.AddVert(v); }
        void Quad(VertexHelper vh, Vector2 a, Vector2 b, Vector2 c, Vector2 d)
        { int n = vh.currentVertCount; Vertex(vh, a); Vertex(vh, b); Vertex(vh, c); Vertex(vh, d); vh.AddTriangle(n, n + 1, n + 2); vh.AddTriangle(n, n + 2, n + 3); }
        void Box(VertexHelper vh, Vector2 p, Vector2 size) => Quad(vh, p, p + new Vector2(size.x, 0), p + size, p + new Vector2(0, size.y));
        void Line(VertexHelper vh, Vector2 a, Vector2 b, float width)
        { var d = (b - a).normalized; var n = new Vector2(-d.y, d.x) * width * .5f; Quad(vh, a - n, b - n, b + n, a + n); }
        void Medallion(VertexHelper vh, Vector2 c, float radius)
        {
            // Soft enamel shading and restrained metal engraving are part of the same retained mesh.
            // The control remains one raycast target with the original rectangular touch bounds.
            for (int i = 0; i < 48; i++)
            {
                float a = i * Mathf.PI * 2 / 48, b = (i + 1) * Mathf.PI * 2 / 48;
                var d = new Vector2(Mathf.Cos(a), Mathf.Sin(a)); var e = new Vector2(Mathf.Cos(b), Mathf.Sin(b));
                int n = vh.currentVertCount;
                Vertex(vh, c, color);
                Vertex(vh, c + d * radius, Shade(.83f + d.y * .10f));
                Vertex(vh, c + e * radius, Shade(.83f + e.y * .10f));
                vh.AddTriangle(n, n + 1, n + 2);
                var bronze = SanctuaryUiTheme.Bronze; bronze.a = color.a * .75f;
                TintQuad(vh, c + d * radius * .91f, c + e * radius * .91f,
                    c + e * (radius * .91f - 1.0f), c + d * (radius * .91f - 1.0f), bronze);
            }
            for (int i = 0; i < 4; i++)
            {
                float a = i * Mathf.PI * .5f;
                var d = new Vector2(Mathf.Cos(a), Mathf.Sin(a)); var n = new Vector2(-d.y, d.x);
                var bronze = SanctuaryUiTheme.Bronze; bronze.a = color.a * .82f;
                float r = radius * (Compass ? .72f : .93f), halfWidth = Compass ? 1.1f : 1.6f;
                TintQuad(vh, c + d * r - n * halfWidth, c + d * (r + radius * .05f),
                    c + d * r + n * halfWidth, c + d * (r - radius * .035f), bronze);
            }
        }
        Color Shade(float value) => new Color(color.r * value, color.g * value, color.b * value, color.a);
        void TintQuad(VertexHelper vh, Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color tint)
        { int n = vh.currentVertCount; Vertex(vh, a, tint); Vertex(vh, b, tint); Vertex(vh, c, tint); Vertex(vh, d, tint); vh.AddTriangle(n, n + 1, n + 2); vh.AddTriangle(n, n + 2, n + 3); }
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
