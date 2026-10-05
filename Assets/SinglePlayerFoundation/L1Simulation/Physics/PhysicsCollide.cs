using Unity.Mathematics;
using static SPF.L1.Physics.PhysicsMath;

namespace SPF.L1.Physics
{
    /// <summary>
    /// Narrow phase: circle–circle, box–circle and box–box (separating axis + reference-face clipping, two points
    /// with stable feature ids for warm starting). Contacts are produced up to <c>margin</c> apart (speculative
    /// contacts): the solver only lets the bodies close that gap, which stops fast bodies tunnelling through thin
    /// ones without sub-stepping.
    /// </summary>
    public static class PhysicsCollide
    {
        const byte NoEdge = 0, Edge1 = 1, Edge2 = 2, Edge3 = 3, Edge4 = 4;

        struct ClipVertex
        {
            public float2 V;
            public byte InEdge1, OutEdge1, InEdge2, OutEdge2;
            public uint Feature => (uint)InEdge1 | (uint)OutEdge1 << 8 | (uint)InEdge2 << 16 | (uint)OutEdge2 << 24;
        }

        static uint Flip(uint f) => (f >> 16 & 0xFF) | (f >> 24 & 0xFF) << 8 | (f & 0xFF) << 16 | (f >> 8 & 0xFF) << 24;

        /// <summary>Fills <paramref name="m"/> (normal from A to B); returns false when the shapes are farther apart than <paramref name="margin"/>.</summary>
        public static bool Collide(in Body a, in Body b, float margin, ref Manifold m)
        {
            m.Count = 0;
            if (a.Shape == ShapeKind.Circle && b.Shape == ShapeKind.Circle) return CircleCircle(a, b, margin, ref m);
            if (a.Shape == ShapeKind.Box && b.Shape == ShapeKind.Circle) return BoxCircle(a, b, margin, ref m);
            if (a.Shape == ShapeKind.Circle && b.Shape == ShapeKind.Box)
            {
                if (!BoxCircle(b, a, margin, ref m)) return false;
                m.Normal = -m.Normal;
                return true;
            }
            return BoxBox(a, b, margin, ref m);
        }

        static bool CircleCircle(in Body a, in Body b, float margin, ref Manifold m)
        {
            float2 d = b.Position - a.Position;
            float distSq = math.lengthsq(d);
            float r = a.Half.x + b.Half.x;
            if (distSq > (r + margin) * (r + margin)) return false;
            float dist = math.sqrt(distSq);
            float2 n = dist > 1e-6f ? d / dist : new float2(0f, 1f);
            float separation = dist - r;
            m.Normal = n;
            m.Count = 1;
            m.P0 = new ContactPoint { Position = a.Position + n * (a.Half.x + separation * 0.5f), Separation = separation, Feature = 0 };
            return true;
        }

        /// <summary>Box <paramref name="box"/> against circle <paramref name="circle"/>; normal from the box to the circle.</summary>
        static bool BoxCircle(in Body box, in Body circle, float margin, ref Manifold m)
        {
            var rot = Rotation(box.Angle);
            float2 local = math.mul(math.transpose(rot), circle.Position - box.Position);
            float2 h = box.Half;
            float radius = circle.Half.x;
            float2 clamped = math.clamp(local, -h, h);
            float2 normalLocal;
            float separation;
            uint feature;
            if (math.all(clamped == local))
            {
                // Centre inside the box: push out along the axis of least penetration.
                float2 depth = h - math.abs(local);
                if (depth.x < depth.y)
                {
                    normalLocal = new float2(local.x >= 0f ? 1f : -1f, 0f);
                    separation = -depth.x - radius;
                    clamped = new float2(normalLocal.x * h.x, local.y);
                    feature = 1;
                }
                else
                {
                    normalLocal = new float2(0f, local.y >= 0f ? 1f : -1f);
                    separation = -depth.y - radius;
                    clamped = new float2(local.x, normalLocal.y * h.y);
                    feature = 2;
                }
            }
            else
            {
                float2 d = local - clamped;
                float dist = math.length(d);
                separation = dist - radius;
                if (separation > margin) return false;
                normalLocal = d / math.max(dist, 1e-6f);
                feature = 3;
            }
            float2 n = math.mul(rot, normalLocal);
            float2 surface = box.Position + math.mul(rot, clamped);
            m.Normal = n;
            m.Count = 1;
            m.P0 = new ContactPoint { Position = surface + n * (separation * 0.5f), Separation = separation, Feature = feature };
            return true;
        }

        static void IncidentEdge(ref ClipVertex c0, ref ClipVertex c1, float2 h, float2 pos, float2x2 rot, float2 normal)
        {
            float2 n = -math.mul(math.transpose(rot), normal);
            float2 nAbs = math.abs(n);
            if (nAbs.x > nAbs.y)
            {
                if (n.x > 0f)
                {
                    c0.V = new float2(h.x, -h.y); c0.InEdge2 = Edge3; c0.OutEdge2 = Edge4;
                    c1.V = new float2(h.x, h.y); c1.InEdge2 = Edge4; c1.OutEdge2 = Edge1;
                }
                else
                {
                    c0.V = new float2(-h.x, h.y); c0.InEdge2 = Edge1; c0.OutEdge2 = Edge2;
                    c1.V = new float2(-h.x, -h.y); c1.InEdge2 = Edge2; c1.OutEdge2 = Edge3;
                }
            }
            else
            {
                if (n.y > 0f)
                {
                    c0.V = new float2(h.x, h.y); c0.InEdge2 = Edge4; c0.OutEdge2 = Edge1;
                    c1.V = new float2(-h.x, h.y); c1.InEdge2 = Edge1; c1.OutEdge2 = Edge2;
                }
                else
                {
                    c0.V = new float2(-h.x, -h.y); c0.InEdge2 = Edge2; c0.OutEdge2 = Edge3;
                    c1.V = new float2(h.x, -h.y); c1.InEdge2 = Edge3; c1.OutEdge2 = Edge4;
                }
            }
            c0.V = pos + math.mul(rot, c0.V);
            c1.V = pos + math.mul(rot, c1.V);
        }

        static int ClipSegment(ref ClipVertex o0, ref ClipVertex o1, in ClipVertex i0, in ClipVertex i1, float2 normal, float offset, byte clipEdge)
        {
            int count = 0;
            float d0 = math.dot(normal, i0.V) - offset;
            float d1 = math.dot(normal, i1.V) - offset;
            if (d0 <= 0f) { o0 = i0; count++; }
            if (d1 <= 0f) { if (count == 0) o0 = i1; else o1 = i1; count++; }
            if (d0 * d1 < 0f)
            {
                float t = d0 / (d0 - d1);
                ClipVertex v;
                if (d0 > 0f) { v = i0; v.InEdge1 = clipEdge; v.InEdge2 = NoEdge; }
                else { v = i1; v.OutEdge1 = clipEdge; v.OutEdge2 = NoEdge; }
                v.V = i0.V + t * (i1.V - i0.V);
                if (count == 0) o0 = v; else o1 = v;
                count++;
            }
            return count;
        }

        static bool BoxBox(in Body bodyA, in Body bodyB, float margin, ref Manifold m)
        {
            float2 hA = bodyA.Half, hB = bodyB.Half;
            float2 posA = bodyA.Position, posB = bodyB.Position;
            var rotA = Rotation(bodyA.Angle);
            var rotB = Rotation(bodyB.Angle);
            var rotAT = math.transpose(rotA);
            var rotBT = math.transpose(rotB);
            float2 dp = posB - posA;
            float2 dA = math.mul(rotAT, dp);
            float2 dB = math.mul(rotBT, dp);
            var c = math.mul(rotAT, rotB);
            var absC = new float2x2(math.abs(c.c0), math.abs(c.c1));
            var absCT = math.transpose(absC);

            float2 faceA = math.abs(dA) - hA - math.mul(absC, hB);
            if (faceA.x > margin || faceA.y > margin) return false;
            float2 faceB = math.abs(dB) - math.mul(absCT, hA) - hB;
            if (faceB.x > margin || faceB.y > margin) return false;

            const float RelativeTol = 0.95f, AbsoluteTol = 0.01f;
            int axis = 0;
            float separation = faceA.x;
            float2 normal = dA.x > 0f ? rotA.c0 : -rotA.c0;
            if (faceA.y > RelativeTol * separation + AbsoluteTol * hA.y) { axis = 1; separation = faceA.y; normal = dA.y > 0f ? rotA.c1 : -rotA.c1; }
            if (faceB.x > RelativeTol * separation + AbsoluteTol * hB.x) { axis = 2; separation = faceB.x; normal = dB.x > 0f ? rotB.c0 : -rotB.c0; }
            if (faceB.y > RelativeTol * separation + AbsoluteTol * hB.y) { axis = 3; separation = faceB.y; normal = dB.y > 0f ? rotB.c1 : -rotB.c1; }

            float2 frontNormal, sideNormal;
            float front, negSide, posSide;
            byte negEdge, posEdge;
            ClipVertex i0 = default, i1 = default;
            switch (axis)
            {
                case 0:
                {
                    frontNormal = normal;
                    front = math.dot(posA, frontNormal) + hA.x;
                    sideNormal = rotA.c1;
                    float side = math.dot(posA, sideNormal);
                    negSide = -side + hA.y; posSide = side + hA.y;
                    negEdge = Edge3; posEdge = Edge1;
                    IncidentEdge(ref i0, ref i1, hB, posB, rotB, frontNormal);
                    break;
                }
                case 1:
                {
                    frontNormal = normal;
                    front = math.dot(posA, frontNormal) + hA.y;
                    sideNormal = rotA.c0;
                    float side = math.dot(posA, sideNormal);
                    negSide = -side + hA.x; posSide = side + hA.x;
                    negEdge = Edge2; posEdge = Edge4;
                    IncidentEdge(ref i0, ref i1, hB, posB, rotB, frontNormal);
                    break;
                }
                case 2:
                {
                    frontNormal = -normal;
                    front = math.dot(posB, frontNormal) + hB.x;
                    sideNormal = rotB.c1;
                    float side = math.dot(posB, sideNormal);
                    negSide = -side + hB.y; posSide = side + hB.y;
                    negEdge = Edge3; posEdge = Edge1;
                    IncidentEdge(ref i0, ref i1, hA, posA, rotA, frontNormal);
                    break;
                }
                default:
                {
                    frontNormal = -normal;
                    front = math.dot(posB, frontNormal) + hB.y;
                    sideNormal = rotB.c0;
                    float side = math.dot(posB, sideNormal);
                    negSide = -side + hB.x; posSide = side + hB.x;
                    negEdge = Edge2; posEdge = Edge4;
                    IncidentEdge(ref i0, ref i1, hA, posA, rotA, frontNormal);
                    break;
                }
            }

            ClipVertex c0 = default, c1 = default, d0 = default, d1 = default;
            if (ClipSegment(ref c0, ref c1, i0, i1, -sideNormal, negSide, negEdge) < 2) return false;
            if (ClipSegment(ref d0, ref d1, c0, c1, sideNormal, posSide, posEdge) < 2) return false;

            m.Normal = normal;
            m.Count = 0;
            AddClipped(ref m, d0, frontNormal, front, margin, axis >= 2);
            AddClipped(ref m, d1, frontNormal, front, margin, axis >= 2);
            return m.Count > 0;
        }

        static void AddClipped(ref Manifold m, in ClipVertex v, float2 frontNormal, float front, float margin, bool flip)
        {
            float separation = math.dot(frontNormal, v.V) - front;
            if (separation > margin) return;
            var p = new ContactPoint
            {
                Separation = separation,
                Position = v.V - separation * 0.5f * frontNormal,   // midway between the faces
                Feature = flip ? Flip(v.Feature) : v.Feature,
            };
            if (m.Count == 0) m.P0 = p; else m.P1 = p;
            m.Count++;
        }

        /// <summary>World AABB (min, max) of a body.</summary>
        public static float4 Bounds(in Body b)
        {
            float2 e;
            if (b.Shape == ShapeKind.Circle) e = new float2(b.Half.x);
            else
            {
                math.sincos(b.Angle, out float s, out float c);
                float ac = math.abs(c), as_ = math.abs(s);
                e = new float2(ac * b.Half.x + as_ * b.Half.y, as_ * b.Half.x + ac * b.Half.y);
            }
            return new float4(b.Position - e, b.Position + e);
        }

        /// <summary>Ray (from p, direction d scaled to length) against one body; fraction in [0, maxFraction].</summary>
        public static bool Raycast(in Body b, float2 p, float2 d, float maxFraction, out float fraction, out float2 normal)
        {
            fraction = 0f;
            normal = default;
            if (b.Shape == ShapeKind.Circle)
            {
                float2 s = p - b.Position;
                float r = b.Half.x;
                float bq = math.dot(s, d);
                float cq = math.dot(s, s) - r * r;
                float aq = math.dot(d, d);
                float disc = bq * bq - aq * cq;
                if (aq < 1e-12f || disc < 0f) return false;
                float t = (-bq - math.sqrt(disc)) / aq;
                if (t < 0f || t > maxFraction) return false;
                fraction = t;
                normal = math.normalize(s + t * d);
                return true;
            }
            var rot = Rotation(b.Angle);
            var rotT = math.transpose(rot);
            float2 lp = math.mul(rotT, p - b.Position);
            float2 ld = math.mul(rotT, d);
            float tMin = 0f, tMax = maxFraction;
            float2 n = default;
            for (int axis = 0; axis < 2; axis++)
            {
                float o = axis == 0 ? lp.x : lp.y, dir = axis == 0 ? ld.x : ld.y, h = axis == 0 ? b.Half.x : b.Half.y;
                if (math.abs(dir) < 1e-9f) { if (o < -h || o > h) return false; continue; }
                float inv = 1f / dir;
                float t1 = (-h - o) * inv, t2 = (h - o) * inv;
                float sign = -1f;
                if (t1 > t2) { (t1, t2) = (t2, t1); sign = 1f; }
                if (t1 > tMin) { tMin = t1; n = axis == 0 ? new float2(sign, 0f) : new float2(0f, sign); }
                tMax = math.min(tMax, t2);
                if (tMin > tMax) return false;
            }
            if (math.all(n == 0f)) return false;   // started inside
            fraction = tMin;
            normal = math.mul(rot, n);
            return true;
        }
    }
}
