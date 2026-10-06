using System;
using System.Collections.Generic;
using System.IO;
using SPF.Contracts;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace SPF.L1.Physics
{
    /// <summary>
    /// Deterministic 2D rigid-body world: circles and boxes with rotation, friction, restitution, revolute /
    /// rod / rope / spring joints, sleeping islands and speculative contacts (no tunnelling at mobile tick
    /// rates). Everything lives in fixed-capacity native arrays allocated once; the step is one Burst job
    /// (<see cref="Schedule"/>), so a game system can run it on a worker while the main thread renders.
    /// Bodies and joints are addressed by stable indices (slots are reused after removal).
    /// A world resource: add it with <c>layout.Resource</c>, declare it in the stepping system, and it snapshots
    /// (including warm-start state, so a restored world continues bit-identically).
    /// </summary>
    public sealed class PhysicsWorld2D : IDisposable, IJobData, ISnapshotResource, IResettableResource
    {
        public PhysicsSettings Settings = PhysicsSettings.Default;

        NativeArray<Body> m_Bodies;
        NativeArray<Joint> m_Joints;
        NativeArray<int> m_Order;
        NativeArray<float4> m_Aabb;
        NativeArray<Manifold> m_Manifolds, m_Previous;
        NativeArray<ulong> m_HashKeys;
        NativeArray<int> m_HashSlots;
        NativeArray<int> m_Parent;
        NativeArray<float> m_IslandSleep;
        NativeArray<float3> m_Vel;
        NativeArray<float2> m_InvMass;
        NativeArray<ContactEvent> m_Events;
        NativeArray<int> m_Counts;
        NativeArray<PhysicsStats> m_Stats;
        readonly Stack<int> m_FreeBodies = new Stack<int>();
        readonly Stack<int> m_FreeJoints = new Stack<int>();
        int m_HighWater, m_JointCount;
        JobHandle m_Step;

        public PhysicsWorld2D(int bodyCapacity = 1024, int jointCapacity = 256, int manifoldCapacity = 0, int eventCapacity = 256)
        {
            if (manifoldCapacity <= 0) manifoldCapacity = bodyCapacity * 4;
            m_Bodies = new NativeArray<Body>(bodyCapacity, Allocator.Persistent);
            m_Joints = new NativeArray<Joint>(jointCapacity, Allocator.Persistent);
            m_Order = new NativeArray<int>(bodyCapacity, Allocator.Persistent);
            m_Aabb = new NativeArray<float4>(bodyCapacity, Allocator.Persistent);
            m_Manifolds = new NativeArray<Manifold>(manifoldCapacity, Allocator.Persistent);
            m_Previous = new NativeArray<Manifold>(manifoldCapacity, Allocator.Persistent);
            int hash = 1;
            while (hash < manifoldCapacity * 2) hash <<= 1;
            m_HashKeys = new NativeArray<ulong>(hash, Allocator.Persistent);
            m_HashSlots = new NativeArray<int>(hash, Allocator.Persistent);
            m_Parent = new NativeArray<int>(bodyCapacity, Allocator.Persistent);
            m_IslandSleep = new NativeArray<float>(bodyCapacity, Allocator.Persistent);
            m_Vel = new NativeArray<float3>(bodyCapacity, Allocator.Persistent);
            m_InvMass = new NativeArray<float2>(bodyCapacity, Allocator.Persistent);
            m_Events = new NativeArray<ContactEvent>(eventCapacity, Allocator.Persistent);
            m_Counts = new NativeArray<int>(4, Allocator.Persistent);
            m_Stats = new NativeArray<PhysicsStats>(1, Allocator.Persistent);
        }

        public int BodyCapacity => m_Bodies.Length;
        /// <summary>Bodies live in slots [0, HighWater); dead slots have <see cref="Body.Alive"/> 0.</summary>
        public int HighWater => m_HighWater;
        public int BodyCount => m_HighWater - m_FreeBodies.Count;
        public int JointCount => m_JointCount - m_FreeJoints.Count;
        /// <summary>Read-only view for renderers and queries (complete the step first).</summary>
        public NativeArray<Body> Bodies => m_Bodies;
        public NativeArray<Joint> Joints => m_Joints;
        public int JointHighWater => m_JointCount;
        public PhysicsStats Stats => m_Stats[0];
        /// <summary>
        /// Whether the last completed step actually ran Burst-compiled code. Read after Complete.
        /// False before a step, after Clear/ReadSnapshot, and for managed execution; not snapshot state.
        /// </summary>
        public bool LastStepExecutedWithBurst => m_Counts[3] != 0;
        public int EventCount => m_Stats[0].Events;
        public NativeArray<ContactEvent> Events => m_Events;
        public int ManifoldCount => m_Counts[0];
        public NativeArray<Manifold> Manifolds => m_Manifolds;

        public Body this[int id]
        {
            get => m_Bodies[id];
            set => m_Bodies[id] = value;
        }

        // ---- Creation -------------------------------------------------------------------------------------

        public static Body BoxBody(float2 position, float2 half, float angle, float density, float friction = 0.6f, float restitution = 0f)
        {
            float mass = density * 4f * half.x * half.y;
            float inertia = mass * (half.x * half.x + half.y * half.y) / 3f;   // m (w² + h²) / 12 with w = 2 hx
            return new Body
            {
                Shape = ShapeKind.Box, Position = position, Angle = angle, Half = half,
                InvMass = mass > 0f ? 1f / mass : 0f, InvInertia = inertia > 0f ? 1f / inertia : 0f,
                Friction = friction, Restitution = restitution, GravityScale = 1f, Layer = 1u, Mask = ~0u,
            };
        }

        public static Body CircleBody(float2 position, float radius, float density, float friction = 0.6f, float restitution = 0f)
        {
            float mass = density * math.PI * radius * radius;
            float inertia = 0.5f * mass * radius * radius;
            return new Body
            {
                Shape = ShapeKind.Circle, Position = position, Half = new float2(radius, radius),
                InvMass = mass > 0f ? 1f / mass : 0f, InvInertia = inertia > 0f ? 1f / inertia : 0f,
                Friction = friction, Restitution = restitution, GravityScale = 1f, Layer = 1u, Mask = ~0u,
            };
        }

        /// <summary>Adds a body (density 0 in the factory = static); returns its id, or -1 when full.</summary>
        public int Add(Body body)
        {
            Complete();
            int id;
            if (m_FreeBodies.Count > 0) id = m_FreeBodies.Pop();
            else if (m_HighWater < m_Bodies.Length) id = m_HighWater++;
            else return -1;
            body.Alive = 1;
            body.Awake = 1;
            body.SleepTime = 0f;
            m_Bodies[id] = body;
            return id;
        }

        public int AddBox(float2 position, float2 half, float angle = 0f, float density = 1f, float friction = 0.6f, float restitution = 0f) =>
            Add(BoxBody(position, half, angle, density, friction, restitution));

        public int AddCircle(float2 position, float radius, float density = 1f, float friction = 0.6f, float restitution = 0f) =>
            Add(CircleBody(position, radius, density, friction, restitution));

        /// <summary>Removes a body and every joint attached to it; wakes what it touched.</summary>
        public void Remove(int id)
        {
            Complete();
            if ((uint)id >= (uint)m_HighWater || m_Bodies[id].Alive == 0) return;
            for (int c = 0; c < m_Counts[0]; c++)
            {
                var m = m_Manifolds[c];
                if (m.A == id) Wake(m.B);
                else if (m.B == id) Wake(m.A);
            }
            for (int j = 0; j < m_JointCount; j++)
            {
                var joint = m_Joints[j];
                if (joint.Alive != 0 && (joint.A == id || joint.B == id)) RemoveJoint(j);
            }
            m_Bodies[id] = default;
            m_FreeBodies.Push(id);
        }

        public void Wake(int id)
        {
            var b = m_Bodies[id];
            if (b.Alive == 0) return;
            b.Awake = 1;
            b.SleepTime = 0f;
            m_Bodies[id] = b;
        }

        public void ApplyImpulse(int id, float2 impulse, float2 worldPoint)
        {
            Complete();
            var b = m_Bodies[id];
            if (b.Alive == 0 || !b.IsDynamic) return;
            b.Velocity += b.InvMass * impulse;
            b.AngularVelocity += b.InvInertia * PhysicsMath.Cross(worldPoint - b.Position, impulse);
            b.Awake = 1;
            b.SleepTime = 0f;
            m_Bodies[id] = b;
        }

        public int AddJoint(JointKind kind, int a, int b, float2 worldAnchorA, float2 worldAnchorB, float frequency = 0f, float dampingRatio = 0.7f)
        {
            Complete();
            int id;
            if (m_FreeJoints.Count > 0) id = m_FreeJoints.Pop();
            else if (m_JointCount < m_Joints.Length) id = m_JointCount++;
            else return -1;
            var ba = m_Bodies[a];
            var bb = m_Bodies[b];
            m_Joints[id] = new Joint
            {
                Kind = kind, Alive = 1, A = a, B = b,
                LocalAnchorA = math.mul(math.transpose(PhysicsMath.Rotation(ba.Angle)), worldAnchorA - ba.Position),
                LocalAnchorB = math.mul(math.transpose(PhysicsMath.Rotation(bb.Angle)), worldAnchorB - bb.Position),
                Length = math.distance(worldAnchorA, worldAnchorB),
                Frequency = frequency, DampingRatio = dampingRatio,
            };
            Wake(a);
            Wake(b);
            return id;
        }

        /// <summary>A hinge at <paramref name="worldAnchor"/>.</summary>
        public int AddRevolute(int a, int b, float2 worldAnchor) => AddJoint(JointKind.Revolute, a, b, worldAnchor, worldAnchor);

        public void RemoveJoint(int id)
        {
            Complete();
            if ((uint)id >= (uint)m_JointCount || m_Joints[id].Alive == 0) return;
            var j = m_Joints[id];
            Wake(j.A);
            Wake(j.B);
            m_Joints[id] = default;
            m_FreeJoints.Push(id);
        }

        /// <summary>Removes everything (level change) without reallocating.</summary>
        public void Clear()
        {
            Complete();
            for (int i = 0; i < m_HighWater; i++) m_Bodies[i] = default;
            for (int j = 0; j < m_JointCount; j++) m_Joints[j] = default;
            m_HighWater = 0;
            m_JointCount = 0;
            m_FreeBodies.Clear();
            m_FreeJoints.Clear();
            for (int i = 0; i < m_Counts.Length; i++) m_Counts[i] = 0;
            m_Stats[0] = default;
        }

        public void OnReset() => Clear();

        // ---- Stepping -------------------------------------------------------------------------------------

        public JobHandle Schedule(float dt, JobHandle dependency = default)
        {
            Complete();
            m_Step = new PhysicsStepJob
            {
                Dt = dt, Settings = Settings, HighWater = m_HighWater, JointCount = m_JointCount,
                Bodies = m_Bodies, Joints = m_Joints, Order = m_Order, Aabb = m_Aabb,
                Manifolds = m_Manifolds, Previous = m_Previous, HashKeys = m_HashKeys, HashSlots = m_HashSlots,
                Parent = m_Parent, IslandSleep = m_IslandSleep, Vel = m_Vel, InvMass = m_InvMass,
                Events = m_Events, Counts = m_Counts, Stats = m_Stats,
            }.Schedule(dependency);
            return m_Step;
        }

        public void Step(float dt) => Schedule(dt).Complete();

        public void Complete() => m_Step.Complete();

        // ---- Queries (main thread, after Complete) --------------------------------------------------------

        /// <summary>Closest body hit by the segment <paramref name="from"/> → <paramref name="to"/>.</summary>
        public bool Raycast(float2 from, float2 to, out RayHit hit, uint mask = ~0u)
        {
            Complete();
            hit = new RayHit { Body = -1, Fraction = 1f };
            float2 d = to - from;
            for (int i = 0; i < m_HighWater; i++)
            {
                var b = m_Bodies[i];
                if (b.Alive == 0 || (b.Layer & mask) == 0) continue;
                if (PhysicsCollide.Raycast(b, from, d, hit.Fraction, out float t, out float2 n) && (hit.Body < 0 || t < hit.Fraction))
                    hit = new RayHit { Body = i, Fraction = t, Point = from + t * d, Normal = n };
            }
            return hit.Body >= 0;
        }

        /// <summary>The body containing <paramref name="point"/>, or -1.</summary>
        public int OverlapPoint(float2 point, uint mask = ~0u)
        {
            Complete();
            for (int i = 0; i < m_HighWater; i++)
            {
                var b = m_Bodies[i];
                if (b.Alive == 0 || (b.Layer & mask) == 0) continue;
                if (b.Shape == ShapeKind.Circle)
                {
                    if (math.distancesq(point, b.Position) <= b.Half.x * b.Half.x) return i;
                }
                else
                {
                    float2 local = math.mul(math.transpose(PhysicsMath.Rotation(b.Angle)), point - b.Position);
                    if (math.all(math.abs(local) <= b.Half)) return i;
                }
            }
            return -1;
        }

        // ---- Snapshots ------------------------------------------------------------------------------------

        public void WriteSnapshot(BinaryWriter w)
        {
            Complete();
            NativeIO.WriteValue(w, Settings);
            w.Write(m_HighWater);
            w.Write(m_JointCount);
            NativeIO.Write(w, m_Bodies, m_HighWater);
            NativeIO.Write(w, m_Joints, m_JointCount);
            w.Write(m_Counts[1]);
            w.Write(m_Counts[2]);
            NativeIO.Write(w, m_Previous, m_Counts[1]);
            NativeIO.Write(w, m_Order, m_Counts[2]);
        }

        public void ReadSnapshot(BinaryReader r)
        {
            Clear();
            Settings = NativeIO.ReadValue<PhysicsSettings>(r);
            m_HighWater = r.ReadInt32();
            m_JointCount = r.ReadInt32();
            if (m_HighWater < 0 || m_HighWater > m_Bodies.Length || m_JointCount < 0 || m_JointCount > m_Joints.Length)
                throw new InvalidDataException("Physics snapshot does not fit this world's capacity.");
            NativeIO.Read(r, m_Bodies);
            NativeIO.Read(r, m_Joints);
            m_Counts[1] = r.ReadInt32();
            m_Counts[2] = r.ReadInt32();
            NativeIO.Read(r, m_Previous);
            NativeIO.Read(r, m_Order);
            for (int i = m_HighWater - 1; i >= 0; i--) if (m_Bodies[i].Alive == 0) m_FreeBodies.Push(i);
            for (int j = m_JointCount - 1; j >= 0; j--) if (m_Joints[j].Alive == 0) m_FreeJoints.Push(j);
        }

        public void Dispose()
        {
            m_Step.Complete();
            if (!m_Bodies.IsCreated) return;
            m_Bodies.Dispose(); m_Joints.Dispose(); m_Order.Dispose(); m_Aabb.Dispose();
            m_Manifolds.Dispose(); m_Previous.Dispose(); m_HashKeys.Dispose(); m_HashSlots.Dispose();
            m_Parent.Dispose(); m_IslandSleep.Dispose(); m_Vel.Dispose(); m_InvMass.Dispose();
            m_Events.Dispose(); m_Counts.Dispose(); m_Stats.Dispose();
        }
    }
}
