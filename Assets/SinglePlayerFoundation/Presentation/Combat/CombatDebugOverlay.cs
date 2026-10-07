using System;
using SPF.Contracts.Combat;
using SPF.Presentation.Sprites;
using Unity.Mathematics;
using UnityEngine;

namespace SPF.Presentation.Combat
{
    /// <summary>Bounded diagnostic line sprites on both render tiers. Hosts submit authority-derived
    /// projected shapes; this class cannot access or mutate simulation. No per-shape GameObjects.</summary>
    public sealed class CombatDebugOverlay : IDisposable
    {
        public const int PrimitiveCapacity = 256, SegmentCapacity = 4096, CircleSegments = 16;
        readonly SpriteBatch m_Batch;
        readonly Texture2D m_White;
        readonly CombatDebugShape[] m_Shapes = new CombatDebugShape[PrimitiveCapacity];
        public int Count { get; private set; }
        public int Dropped { get; private set; }
        public int Segments => m_Batch.Count;
        public long UploadedBytes => m_Batch.BytesUploaded;
        public CombatDebugShape ShapeAt(int index) => m_Shapes[index];
        public CombatDebugOverlay(RenderTier tier)
        {
            m_White = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            m_White.SetPixel(0, 0, Color.white); m_White.Apply(false, false);
            m_Batch = new SpriteBatch(tier, m_White, BlendKind.Translucent, SegmentCapacity, 300);
            m_Batch.Warmup(SegmentCapacity);
        }
        public void Clear() { Count = Dropped = 0; m_Batch.Clear(); }
        public bool Add(in CombatDebugShape shape)
        {
            if (Count >= PrimitiveCapacity || !math.all(math.isfinite(shape.A)) || !math.all(math.isfinite(shape.B)) || !math.isfinite(shape.Radius) || shape.Radius < 0)
            { Dropped++; return false; }
            m_Shapes[Count++] = shape;
            float2 scale = math.all(shape.Scale > 0) ? shape.Scale : new float2(1);
            float4 color = ColorFor(shape.Role);
            if (shape.Kind == CombatShapeKind.Circle) Circle(shape.A, shape.Radius, scale, color);
            else if (shape.Kind == CombatShapeKind.Capsule)
            {
                float2 delta = (shape.B - shape.A) / scale;
                float2 normal = math.normalizesafe(new float2(-delta.y, delta.x), new float2(0, 1)) * shape.Radius * scale;
                Line(shape.A + normal, shape.B + normal, color); Line(shape.A - normal, shape.B - normal, color);
                Circle(shape.A, shape.Radius, scale, color); Circle(shape.B, shape.Radius, scale, color);
            }
            else if (shape.Kind == CombatShapeKind.Box)
            {
                float2 h = math.abs(shape.B), a = shape.A - h, b = shape.A + h;
                Line(a, new float2(b.x, a.y), color); Line(new float2(b.x, a.y), b, color);
                Line(b, new float2(a.x, b.y), color); Line(new float2(a.x, b.y), a, color);
            }
            else if (shape.Kind == CombatShapeKind.Line) Line(shape.A, shape.B, color);
            else
            {
                float r = math.max(.07f, shape.Radius);
                Line(shape.A - new float2(r, 0), shape.A + new float2(r, 0), color);
                Line(shape.A - new float2(0, r), shape.A + new float2(0, r), color);
            }
            return true;
        }
        void Circle(float2 center, float radius, float2 scale, float4 color)
        {
            float2 previous = center + new float2(radius, 0) * scale;
            for (int i = 1; i <= CircleSegments; i++)
            {
                float angle = i * (math.PI * 2 / CircleSegments);
                float2 next = center + new float2(math.cos(angle), math.sin(angle)) * radius * scale;
                Line(previous, next, color); previous = next;
            }
        }
        void Line(float2 a, float2 b, float4 color)
        {
            float2 delta = b - a; float length = math.length(delta);
            if (!math.isfinite(length) || !math.all(math.isfinite((a + b) * .5f))) { Dropped++; return; }
            if (length <= 0) return;
            if (!m_Batch.Add((a + b) * .5f, new float2(length, .035f), new float4(0, 0, 1, 1), -7f, color, math.atan2(delta.y, delta.x))) Dropped++;
        }
        public void Draw() => m_Batch.Draw(new Bounds(Vector3.zero, new Vector3(4096, 4096, 100)));
        public static float4 ColorFor(CombatShapeRole role)
        {
            switch (role)
            {
                case CombatShapeRole.Body: return new float4(0, 1, 1, 1);
                case CombatShapeRole.Hurt: return new float4(1, .1f, .8f, 1);
                case CombatShapeRole.Attack: return new float4(1, .65f, 0, 1);
                case CombatShapeRole.Accepted: return new float4(.15f, 1, .1f, 1);
                case CombatShapeRole.Rejected: return new float4(1, .1f, .1f, 1);
                default: return new float4(.5f, .75f, 1, .8f);
            }
        }
        public void Dispose()
        {
            m_Batch.Dispose();
            if (Application.isPlaying) UnityEngine.Object.Destroy(m_White); else UnityEngine.Object.DestroyImmediate(m_White);
        }
    }
}
