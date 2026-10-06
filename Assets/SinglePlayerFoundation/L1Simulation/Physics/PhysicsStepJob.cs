using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using static SPF.L1.Physics.PhysicsMath;

namespace SPF.L1.Physics
{
    /// <summary>
    /// One physics step as a single Burst job (single-threaded on purpose: the result is bit-identical run to
    /// run, which replays and snapshots rely on). Order: integrate forces → broad phase (sort and sweep on x,
    /// kept sorted across steps so it is near linear) → narrow phase with warm-start matching → islands and
    /// sleeping → sequential-impulse solve on compact velocity arrays → integrate positions → events.
    /// </summary>
    // The first scheduled step must finish compilation before simulation starts. Otherwise an editor
    // can run an arbitrary number of managed steps (and switch arithmetic backends mid-replay).
    [BurstCompile(CompileSynchronously = true)]
    struct PhysicsStepJob : IJob
    {
        public float Dt;
        public PhysicsSettings Settings;
        public int HighWater;
        public int JointCount;

        public NativeArray<Body> Bodies;
        public NativeArray<Joint> Joints;
        public NativeArray<int> Order;            // body indices sorted by AABB min x (persistent)
        public NativeArray<float4> Aabb;
        public NativeArray<Manifold> Manifolds;
        public NativeArray<Manifold> Previous;
        public NativeArray<ulong> HashKeys;       // open addressing over Previous
        public NativeArray<int> HashSlots;
        public NativeArray<int> Parent;           // islands (union-find)
        public NativeArray<float> IslandSleep;
        public NativeArray<float3> Vel;           // compact solver state: (vx, vy, w)
        public NativeArray<float2> InvMass;       // (1/m, 1/I)
        public NativeArray<ContactEvent> Events;
        public NativeArray<int> Counts;           // [0] manifolds, [1] previous manifolds, [2] order length, [3] Burst witness
        public NativeArray<PhysicsStats> Stats;

        public void Execute()
        {
            // Measure this job's actual execution path, rather than trusting the global Burst setting.
            bool usedBurst = true;
            MarkManagedExecution(ref usedBurst);
            Counts[3] = usedBurst ? 1 : 0;
            float dt = Dt;
            float invDt = dt > 0f ? 1f / dt : 0f;
            var stats = new PhysicsStats();

            // 1. Forces → velocities (awake dynamic bodies only).
            for (int i = 0; i < HighWater; i++)
            {
                var b = Bodies[i];
                if (b.Alive == 0) { Vel[i] = default; InvMass[i] = default; continue; }
                stats.Bodies++;
                bool moving = b.IsDynamic && b.Awake != 0;
                if (moving)
                {
                    stats.AwakeBodies++;
                    b.Velocity += dt * (Settings.Gravity * b.GravityScale + b.InvMass * b.Force);
                    b.AngularVelocity += dt * b.InvInertia * b.Torque;
                    b.Velocity *= 1f / (1f + dt * b.LinearDamping);
                    b.AngularVelocity *= 1f / (1f + dt * b.AngularDamping);
                }
                b.Force = default;
                b.Torque = 0f;
                Bodies[i] = b;
                Vel[i] = new float3(b.Velocity, b.AngularVelocity);
                InvMass[i] = moving ? new float2(b.InvMass, b.FixedRotation != 0 ? 0f : b.InvInertia) : float2.zero;
                Aabb[i] = PhysicsCollide.Bounds(b);
            }

            // 2. Broad phase: keep Order sorted by min x (insertion sort: bodies barely move between steps).
            int orderLength = Counts[2];
            int write = 0;
            for (int k = 0; k < orderLength; k++)
            {
                int id = Order[k];
                if (id < HighWater && Bodies[id].Alive != 0) Order[write++] = id;
            }
            orderLength = write;
            // Append bodies not in the order yet (new ones): mark present ones first.
            for (int i = 0; i < HighWater; i++) Parent[i] = 0;
            for (int k = 0; k < orderLength; k++) Parent[Order[k]] = 1;
            for (int i = 0; i < HighWater; i++) if (Bodies[i].Alive != 0 && Parent[i] == 0) Order[orderLength++] = i;
            for (int k = 1; k < orderLength; k++)
            {
                int id = Order[k];
                float x = Aabb[id].x;
                int j = k - 1;
                while (j >= 0 && Aabb[Order[j]].x > x) { Order[j + 1] = Order[j]; j--; }
                Order[j + 1] = id;
            }
            Counts[2] = orderLength;

            // Warm-start lookup table over last step's manifolds.
            int prevCount = Counts[1];
            int mask = HashKeys.Length - 1;
            for (int s = 0; s < HashKeys.Length; s++) HashKeys[s] = ulong.MaxValue;
            for (int p = 0; p < prevCount; p++)
            {
                ulong key = Previous[p].Key;
                int s = (int)(Hash(key) & (uint)mask);
                while (HashKeys[s] != ulong.MaxValue) s = (s + 1) & mask;
                HashKeys[s] = key;
                HashSlots[s] = p;
            }

            // 3. Sweep and narrow phase.
            int manifolds = 0;
            for (int k = 0; k < orderLength; k++)
            {
                int ia = Order[k];
                float4 boxA = Aabb[ia];
                var a = Bodies[ia];
                bool aActive = a.IsDynamic && a.Awake != 0;
                for (int k2 = k + 1; k2 < orderLength; k2++)
                {
                    int ib = Order[k2];
                    float4 boxB = Aabb[ib];
                    if (boxB.x > boxA.z + Settings.MaxSpeculative) break;
                    var b = Bodies[ib];
                    bool bActive = b.IsDynamic && b.Awake != 0;
                    if (!aActive && !bActive) continue;   // static / sleeping pairs need nothing
                    if ((a.Layer & b.Mask) == 0 || (b.Layer & a.Mask) == 0) continue;
                    // Speculative margin from the closing speed this step.
                    float2 relV = b.Velocity - a.Velocity;
                    float margin = math.clamp(math.length(relV) * dt, Settings.AllowedPenetration * 2f, Settings.MaxSpeculative);
                    if (boxB.x > boxA.z + margin || boxB.z < boxA.x - margin || boxB.y > boxA.w + margin || boxB.w < boxA.y - margin) continue;
                    stats.Pairs++;
                    if (manifolds >= Manifolds.Length) continue;
                    // Pairs are always stored lowest index first, so warm starting survives sort-order swaps.
                    bool swap = ib < ia;
                    var m = new Manifold { A = swap ? ib : ia, B = swap ? ia : ib };
                    if (!(swap ? PhysicsCollide.Collide(b, a, margin, ref m) : PhysicsCollide.Collide(a, b, margin, ref m))) continue;
                    m.Friction = math.sqrt(a.Friction * b.Friction);
                    m.Restitution = math.max(a.Restitution, b.Restitution);
                    if (Settings.WarmStarting) WarmStartFrom(ref m);
                    Manifolds[manifolds++] = m;
                }
            }
            Counts[0] = manifolds;
            stats.Manifolds = manifolds;

            // 4. Islands: dynamic bodies linked by contacts and joints sleep and wake together.
            for (int i = 0; i < HighWater; i++) Parent[i] = i;
            for (int c = 0; c < manifolds; c++)
            {
                var m = Manifolds[c];
                if (Bodies[m.A].IsDynamic && Bodies[m.B].IsDynamic) Union(m.A, m.B);
            }
            for (int j = 0; j < JointCount; j++)
            {
                var joint = Joints[j];
                if (joint.Alive != 0 && Bodies[joint.A].IsDynamic && Bodies[joint.B].IsDynamic) Union(joint.A, joint.B);
            }
            for (int i = 0; i < HighWater; i++) IslandSleep[i] = float.MaxValue;
            for (int i = 0; i < HighWater; i++)
            {
                var b = Bodies[i];
                if (b.Alive == 0 || !b.IsDynamic) continue;
                int root = Find(i);
                float t = b.Awake != 0 ? b.SleepTime : float.MaxValue;
                IslandSleep[root] = math.min(IslandSleep[root], t);
            }
            for (int i = 0; i < HighWater; i++)
            {
                var b = Bodies[i];
                if (b.Alive == 0 || !b.IsDynamic || b.Awake != 0) continue;
                // A sleeping body in an island with someone awake wakes up (and joins the solve next step).
                if (IslandSleep[Find(i)] < Settings.TimeToSleep) { b.Awake = 1; b.SleepTime = 0f; Bodies[i] = b; }
            }

            // 5. Solve.
            PreStepContacts(manifolds, invDt);
            PreStepJoints(invDt);
            for (int it = 0; it < Settings.VelocityIterations; it++)
            {
                SolveJoints();
                SolveContacts(manifolds);
            }

            // 6. Integrate positions, sleep timers.
            float linSq = Settings.SleepLinear * Settings.SleepLinear, angSq = Settings.SleepAngular * Settings.SleepAngular;
            for (int i = 0; i < HighWater; i++)
            {
                var b = Bodies[i];
                if (b.Alive == 0) continue;
                if (b.IsDynamic && b.Awake == 0) continue;
                float3 v = Vel[i];
                if (b.IsDynamic)
                {
                    b.Velocity = v.xy;
                    b.AngularVelocity = b.FixedRotation != 0 ? 0f : v.z;
                }
                b.Position += dt * b.Velocity;
                b.Angle += dt * b.AngularVelocity;
                if (b.IsDynamic)
                {
                    if (math.lengthsq(b.Velocity) < linSq && b.AngularVelocity * b.AngularVelocity < angSq) b.SleepTime += dt;
                    else b.SleepTime = 0f;
                }
                Bodies[i] = b;
            }

            // Islands that have all been still long enough go to sleep.
            if (Settings.AllowSleep)
            {
                for (int i = 0; i < HighWater; i++) IslandSleep[i] = float.MaxValue;
                for (int i = 0; i < HighWater; i++)
                {
                    var b = Bodies[i];
                    if (b.Alive == 0 || !b.IsDynamic) continue;
                    int root = Find(i);
                    IslandSleep[root] = math.min(IslandSleep[root], b.Awake != 0 ? b.SleepTime : float.MaxValue);
                }
                for (int i = 0; i < HighWater; i++)
                {
                    var b = Bodies[i];
                    if (b.Alive == 0 || !b.IsDynamic || b.Awake == 0) continue;
                    if (IslandSleep[Find(i)] >= Settings.TimeToSleep)
                    {
                        b.Awake = 0;
                        b.Velocity = default;
                        b.AngularVelocity = 0f;
                        Bodies[i] = b;
                    }
                }
            }
            for (int i = 0; i < HighWater; i++)
                if (Parent[i] == i && Bodies[i].Alive != 0 && Bodies[i].IsDynamic) stats.Islands++;

            // 7. Events, and keep this step's manifolds for warm starting the next.
            int events = 0;
            for (int c = 0; c < manifolds; c++)
            {
                var m = Manifolds[c];
                float impulse = m.P0.Pn + (m.Count > 1 ? m.P1.Pn : 0f);
                if (impulse >= Settings.EventImpulse && events < Events.Length)
                {
                    float2 point = m.Count > 1 ? (m.P0.Position + m.P1.Position) * 0.5f : m.P0.Position;
                    Events[events++] = new ContactEvent { A = m.A, B = m.B, Impulse = impulse, Point = point, Normal = m.Normal };
                }
                Previous[c] = m;
            }
            Counts[1] = manifolds;
            stats.Events = events;
            Stats[0] = stats;
        }

#if !SPF_DOTNET_HARNESS
        [BurstDiscard]
#endif
        static void MarkManagedExecution(ref bool usedBurst) => usedBurst = false;

        static uint Hash(ulong key)
        {
            key ^= key >> 33;
            key *= 0xff51afd7ed558ccdUL;
            key ^= key >> 33;
            return (uint)key;
        }

        int Find(int i)
        {
            while (Parent[i] != i)
            {
                Parent[i] = Parent[Parent[i]];
                i = Parent[i];
            }
            return i;
        }

        void Union(int a, int b)
        {
            a = Find(a); b = Find(b);
            if (a == b) return;
            if (a < b) Parent[b] = a; else Parent[a] = b;   // deterministic: lowest index is the root
        }

        void WarmStartFrom(ref Manifold m)
        {
            ulong key = m.Key;
            int mask = HashKeys.Length - 1;
            int s = (int)(Hash(key) & (uint)mask);
            while (HashKeys[s] != ulong.MaxValue)
            {
                if (HashKeys[s] == key)
                {
                    var old = Previous[HashSlots[s]];
                    if (old.A != m.A) return;   // pair swapped order: start cold
                    Match(ref m.P0, old);
                    if (m.Count > 1) Match(ref m.P1, old);
                    return;
                }
                s = (s + 1) & mask;
            }
        }

        static void Match(ref ContactPoint p, in Manifold old)
        {
            if (old.Count > 0 && old.P0.Feature == p.Feature) { p.Pn = old.P0.Pn; p.Pt = old.P0.Pt; }
            else if (old.Count > 1 && old.P1.Feature == p.Feature) { p.Pn = old.P1.Pn; p.Pt = old.P1.Pt; }
        }

        void PrepPoint(ref ContactPoint c, in Manifold m, float2 posA, float2 posB, float2 invA, float2 invB, float invDt)
        {
            float2 n = m.Normal;
            float2 t = new float2(n.y, -n.x);
            c.RA = c.Position - posA;
            c.RB = c.Position - posB;
            float rnA = Cross(c.RA, n), rnB = Cross(c.RB, n);
            float kNormal = invA.x + invB.x + invA.y * rnA * rnA + invB.y * rnB * rnB;
            c.MassNormal = kNormal > 0f ? 1f / kNormal : 0f;
            float rtA = Cross(c.RA, t), rtB = Cross(c.RB, t);
            float kTangent = invA.x + invB.x + invA.y * rtA * rtA + invB.y * rtB * rtB;
            c.MassTangent = kTangent > 0f ? 1f / kTangent : 0f;

            if (c.Separation > 0f)
            {
                // Speculative: allow closing exactly the gap this step.
                c.Bias = -c.Separation * invDt;
            }
            else
            {
                c.Bias = -Settings.BiasFactor * invDt * math.min(0f, c.Separation + Settings.AllowedPenetration);
                float3 va = Vel[m.A], vb = Vel[m.B];
                float2 dv = vb.xy + Cross(vb.z, c.RB) - va.xy - Cross(va.z, c.RA);
                float vn = math.dot(dv, n);
                if (vn < -Settings.RestitutionThreshold) c.Bias = math.max(c.Bias, -m.Restitution * vn);
            }
        }

        void PreStepContacts(int count, float invDt)
        {
            for (int ci = 0; ci < count; ci++)
            {
                var m = Manifolds[ci];
                float2 invA = InvMass[m.A], invB = InvMass[m.B];
                float2 posA = Bodies[m.A].Position, posB = Bodies[m.B].Position;
                PrepPoint(ref m.P0, m, posA, posB, invA, invB, invDt);
                if (m.Count > 1) PrepPoint(ref m.P1, m, posA, posB, invA, invB, invDt);
                // Warm start.
                float2 n = m.Normal, t = new float2(n.y, -n.x);
                float3 va = Vel[m.A], vb = Vel[m.B];
                for (int p = 0; p < m.Count; p++)
                {
                    var c = p == 0 ? m.P0 : m.P1;
                    float2 impulse = c.Pn * n + c.Pt * t;
                    va -= new float3(invA.x * impulse, invA.y * Cross(c.RA, impulse));
                    vb += new float3(invB.x * impulse, invB.y * Cross(c.RB, impulse));
                }
                Vel[m.A] = va;
                Vel[m.B] = vb;
                Manifolds[ci] = m;
            }
        }

        void SolveContacts(int count)
        {
            for (int ci = 0; ci < count; ci++)
            {
                var m = Manifolds[ci];
                float2 invA = InvMass[m.A], invB = InvMass[m.B];
                if (invA.x == 0f && invB.x == 0f && invA.y == 0f && invB.y == 0f) continue;
                float3 va = Vel[m.A], vb = Vel[m.B];
                float2 n = m.Normal, t = new float2(n.y, -n.x);
                SolvePoint(ref m.P0, ref va, ref vb, invA, invB, n, t, m.Friction);
                if (m.Count > 1) SolvePoint(ref m.P1, ref va, ref vb, invA, invB, n, t, m.Friction);
                Vel[m.A] = va;
                Vel[m.B] = vb;
                Manifolds[ci] = m;
            }
        }

        static void SolvePoint(ref ContactPoint c, ref float3 va, ref float3 vb, float2 invA, float2 invB, float2 n, float2 t, float friction)
        {
            float2 dv = vb.xy + Cross(vb.z, c.RB) - va.xy - Cross(va.z, c.RA);
            float vn = math.dot(dv, n);
            float dPn = c.MassNormal * (-vn + c.Bias);
            float pn0 = c.Pn;
            c.Pn = math.max(pn0 + dPn, 0f);
            dPn = c.Pn - pn0;
            float2 pn = dPn * n;
            va -= new float3(invA.x * pn, invA.y * Cross(c.RA, pn));
            vb += new float3(invB.x * pn, invB.y * Cross(c.RB, pn));

            dv = vb.xy + Cross(vb.z, c.RB) - va.xy - Cross(va.z, c.RA);
            float vt = math.dot(dv, t);
            float dPt = c.MassTangent * -vt;
            float maxPt = friction * c.Pn;
            float pt0 = c.Pt;
            c.Pt = math.clamp(pt0 + dPt, -maxPt, maxPt);
            dPt = c.Pt - pt0;
            float2 pt = dPt * t;
            va -= new float3(invA.x * pt, invA.y * Cross(c.RA, pt));
            vb += new float3(invB.x * pt, invB.y * Cross(c.RB, pt));
        }

        void PreStepJoints(float invDt)
        {
            for (int j = 0; j < JointCount; j++)
            {
                var joint = Joints[j];
                joint.Active = 0;
                if (joint.Alive == 0) { Joints[j] = joint; continue; }
                var a = Bodies[joint.A];
                var b = Bodies[joint.B];
                float2 invA = InvMass[joint.A], invB = InvMass[joint.B];
                if (invA.x == 0f && invB.x == 0f) { Joints[j] = joint; continue; }
                joint.RA = math.mul(Rotation(a.Angle), joint.LocalAnchorA);
                joint.RB = math.mul(Rotation(b.Angle), joint.LocalAnchorB);
                float2 pa = a.Position + joint.RA, pb = b.Position + joint.RB;
                joint.Active = 1;

                if (joint.Kind == JointKind.Revolute)
                {
                    float2 ra = joint.RA, rb = joint.RB;
                    var k1 = new float2x2(invA.x + invB.x, 0f, 0f, invA.x + invB.x);
                    var k2 = new float2x2(invA.y * ra.y * ra.y, -invA.y * ra.x * ra.y, -invA.y * ra.x * ra.y, invA.y * ra.x * ra.x);
                    var k3 = new float2x2(invB.y * rb.y * rb.y, -invB.y * rb.x * rb.y, -invB.y * rb.x * rb.y, invB.y * rb.x * rb.x);
                    var k = k1 + k2 + k3;
                    float det = k.c0.x * k.c1.y - k.c1.x * k.c0.y;
                    joint.M = det != 0f ? new float2x2(k.c1.y, -k.c1.x, -k.c0.y, k.c0.x) * (1f / det) : default;
                    joint.Axis = -Settings.BiasFactor * invDt * (pb - pa);   // positional bias (velocity)
                    float2 p = joint.Impulse;
                    WarmJoint(joint.A, joint.B, invA, invB, ra, rb, p);
                }
                else
                {
                    float2 d = pb - pa;
                    float len = math.length(d);
                    float2 axis = len > 1e-6f ? d / len : new float2(1f, 0f);
                    joint.Axis = axis;
                    float c = len - joint.Length;
                    float crA = Cross(joint.RA, axis), crB = Cross(joint.RB, axis);
                    float kk = invA.x + invB.x + invA.y * crA * crA + invB.y * crB * crB;
                    joint.Gamma = 0f;
                    joint.Bias = Settings.BiasFactor * invDt * c;
                    if (joint.Kind == JointKind.Spring && joint.Frequency > 0f && kk > 0f)
                    {
                        float mass = 1f / kk;
                        float omega = 2f * math.PI * joint.Frequency;
                        float damp = 2f * mass * joint.DampingRatio * omega;
                        float stiff = mass * omega * omega;
                        float h = 1f / invDt;
                        joint.Gamma = h * (damp + h * stiff);
                        joint.Gamma = joint.Gamma > 0f ? 1f / joint.Gamma : 0f;
                        joint.Bias = c * h * stiff * joint.Gamma;
                        kk += joint.Gamma;
                    }
                    if (joint.Kind == JointKind.Rope && c < 0f) { joint.Impulse = default; joint.Active = 0; Joints[j] = joint; continue; }
                    joint.Mass = kk > 0f ? 1f / kk : 0f;
                    WarmJoint(joint.A, joint.B, invA, invB, joint.RA, joint.RB, joint.Impulse.x * axis);
                }
                Joints[j] = joint;
            }
        }

        void WarmJoint(int a, int b, float2 invA, float2 invB, float2 ra, float2 rb, float2 p)
        {
            Vel[a] -= new float3(invA.x * p, invA.y * Cross(ra, p));
            Vel[b] += new float3(invB.x * p, invB.y * Cross(rb, p));
        }

        void SolveJoints()
        {
            for (int j = 0; j < JointCount; j++)
            {
                var joint = Joints[j];
                if (joint.Active == 0) continue;
                float2 invA = InvMass[joint.A], invB = InvMass[joint.B];
                float3 va = Vel[joint.A], vb = Vel[joint.B];
                float2 dv = vb.xy + Cross(vb.z, joint.RB) - va.xy - Cross(va.z, joint.RA);
                float2 p;
                if (joint.Kind == JointKind.Revolute)
                {
                    p = math.mul(joint.M, joint.Axis - dv);
                    joint.Impulse += p;
                }
                else
                {
                    float cdot = math.dot(joint.Axis, dv);
                    float lambda = -joint.Mass * (cdot + joint.Bias + joint.Gamma * joint.Impulse.x);
                    float old = joint.Impulse.x;
                    float acc = old + lambda;
                    if (joint.Kind == JointKind.Rope) acc = math.min(acc, 0f);   // ropes only pull
                    joint.Impulse.x = acc;
                    p = (acc - old) * joint.Axis;
                }
                va -= new float3(invA.x * p, invA.y * Cross(joint.RA, p));
                vb += new float3(invB.x * p, invB.y * Cross(joint.RB, p));
                Vel[joint.A] = va;
                Vel[joint.B] = vb;
                Joints[j] = joint;
            }
        }
    }
}
