using System;
using SPF.Contracts;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using SPF.Presentation.Sprites;

namespace SPF.Presentation.Particles
{
    /// <summary>Bounded admission and CPU reference backend. GPU mode uses only admission metadata, spawn records and sockets.</summary>
    public sealed class ParticlePool : IDisposable
    {
        struct Reservation { public double Until; public float Area, DistanceSquared; public ParticlePriority Priority; public int Socket; public uint Token; }
        readonly Reservation[] m_Reservations;
        NativeArray<ParticleState> m_Cpu;
        NativeArray<ParticleSpawn> m_Spawns;
        NativeArray<ParticleSocket> m_Sockets;
        readonly bool m_Low;
        double m_Time;
        float m_Area;
        float4 m_View;
        int m_SpawnCount, m_Live;
        bool m_Disposed;
        public int Capacity { get; }
        public int SpawnCount => m_SpawnCount;
        public int ReservedCount => m_Live;
        public float ReservedCoverageFraction => m_Area / math.max(.001f, (m_View.z - m_View.x) * (m_View.w - m_View.y));
        public float DrawScale => math.sqrt(math.min(1, CoverageLimit / math.max(.00001f, ReservedCoverageFraction)));
        public float CoverageFraction => ReservedCoverageFraction * DrawScale * DrawScale;
        public float CoverageLimit => m_Low ? .07f : .12f;
        public int Dropped { get; private set; }
        public int Replaced { get; private set; }
        public float DeltaTime { get; private set; }
        public NativeArray<ParticleSpawn> Spawns => m_Spawns;
        public NativeArray<ParticleSocket> Sockets => m_Sockets;
        public NativeArray<ParticleState> CpuStates => m_Cpu;

        public ParticlePool(bool lowQuality = false)
        {
            m_Low = lowQuality; Capacity = lowQuality ? ParticleLimits.LowCapacity : ParticleLimits.HighCapacity;
            m_Reservations = new Reservation[Capacity];
            m_Cpu = new NativeArray<ParticleState>(Capacity, Allocator.Persistent);
            m_Spawns = new NativeArray<ParticleSpawn>(ParticleLimits.SpawnsPerFrame, Allocator.Persistent);
            m_Sockets = new NativeArray<ParticleSocket>(ParticleLimits.Emitters, Allocator.Persistent);
            m_View = new float4(-8,-5,8,5);
        }
        public void BeginFrame(float dt, float4 view)
        {
            Check(); DeltaTime = ParticleLimits.Delta(dt); m_Time += DeltaTime; m_SpawnCount = 0; Dropped = 0; Replaced = 0;
            if (math.all(math.isfinite(view)) && view.z > view.x && view.w > view.y) m_View = view;
            for (int i = 0; i < Capacity; i++) if (m_Reservations[i].Until > 0 && m_Reservations[i].Until <= m_Time) Retire(i);
            // A zoom reduces every quad through DrawScale; admission never undercounts live GPU particles.
        }
        public void SetSocket(int index, in ParticleSocket socket)
        {
            Check(); if ((uint)index >= ParticleLimits.Emitters) throw new ArgumentOutOfRangeException(nameof(index));
            m_Sockets[index] = socket;
            for (int i = 0; i < Capacity; i++)
                if (m_Reservations[i].Until > 0 && m_Reservations[i].Socket == index + 1 &&
                    (socket.Identity.y == 0 || m_Reservations[i].Token != socket.Identity.x)) Retire(i);
        }
        public bool Spawn(in ParticleState state)
        {
            Check();
            if (!Valid(state)) { Dropped++; return false; }
            var priority = (ParticlePriority)state.Attachment.z;
            bool important = priority >= ParticlePriority.Release;
            int command = -1, slot = -1;
            int spawnLimit = important ? ParticleLimits.SpawnsPerFrame : ParticleLimits.SpawnsPerFrame - ParticleLimits.ReservedSpawns;
            if (m_SpawnCount >= spawnLimit)
            {
                if (!important) { Dropped++; return false; }
                for (int i = 0; i < m_SpawnCount; i++)
                    if (m_Spawns[i].State.Attachment.z < (uint)priority && (command < 0 || m_Spawns[i].State.Attachment.z < m_Spawns[command].State.Attachment.z)) command = i;
                if (command < 0) { Dropped++; return false; }
                slot = (int)m_Spawns[command].Target.x;
            }
            float maxScale = math.max(1, state.Visual.z);
            float area = state.Shape.x * state.Shape.y * maxScale * maxScale;
            float viewArea = math.max(.001f, (m_View.z-m_View.x)*(m_View.w-m_View.y));
            if (area > viewArea * .004f || area > viewArea * CoverageLimit) { Dropped++; return false; }
            float2 position = state.Attachment.x == 0 ? state.PositionAge.xy : ParticleMath.WorldPosition(state, m_Sockets[(int)state.Attachment.x - 1]);
            if (position.x < m_View.x - 1 || position.x > m_View.z + 1 || position.y < m_View.y - 1 || position.y > m_View.w + 1) { Dropped++; return false; }
            float limit = CoverageLimit * viewArea * (important ? 1f : .75f);
            float replacedArea = slot < 0 ? 0 : m_Reservations[slot].Area;
            while (m_Area - replacedArea + area > limit)
            {
                int victim = Worst(priority, false, slot);
                if (victim < 0) { Dropped++; return false; }
                // Killing a GPU particle also consumes a bounded command; no unbounded retirement uploads.
                if (!Kill(victim)) { Dropped++; return false; }
                Retire(victim); Replaced++;
            }
            if (slot < 0)
            {
                if (!important && m_Live >= Capacity - ParticleLimits.ReservedParticles) { Dropped++; return false; }
                for (int i = 0; i < Capacity; i++) if (m_Reservations[i].Until == 0) { slot = i; break; }
                if (slot < 0) slot = Worst(priority, false);
                if (slot < 0) { Dropped++; return false; }
                // A just-retired slot may have a staged kill/spawn. Replace that command, never race two scatter writes.
                for (int i = 0; i < m_SpawnCount; i++) if (m_Spawns[i].Target.x == (uint)slot) { command = i; break; }
            }
            if (command < 0 && m_SpawnCount >= ParticleLimits.SpawnsPerFrame) { Dropped++; return false; }
            if (m_Reservations[slot].Until > 0) { Retire(slot); Replaced++; }
            m_Reservations[slot] = new Reservation { Until = m_Time + state.PositionAge.w + .001, Area = area, DistanceSquared = math.lengthsq(position - (m_View.xy + m_View.zw) * .5f), Priority = priority, Socket = (int)state.Attachment.x, Token = state.Attachment.y };
            m_Area += area; m_Live++;
            if (command < 0) command = m_SpawnCount++;
            m_Spawns[command] = new ParticleSpawn { Target = new uint4((uint)slot,0,0,0), State = state };
            return true;
        }
        bool Valid(in ParticleState s) => s.Alive && s.PositionAge.z == 0 && s.PositionAge.w <= ParticleLimits.MaxLife && math.all(math.isfinite(s.PositionAge)) &&
            math.all(math.isfinite(s.VelocityDrag)) && s.VelocityDrag.z >= 0 && math.all(math.isfinite(s.Shape)) && s.Shape.x > 0 && s.Shape.y > 0 &&
            math.all(math.isfinite(s.Color)) && math.all(math.isfinite(s.Visual)) && s.Visual.y >= 0 && s.Visual.y < 4 && s.Visual.z >= 0 && s.Visual.z <= 2 &&
            s.Attachment.x <= ParticleLimits.Emitters && s.Attachment.z <= (uint)ParticlePriority.Hero &&
            (s.Attachment.x == 0 || (m_Sockets[(int)s.Attachment.x-1].Identity.y != 0 && m_Sockets[(int)s.Attachment.x-1].Identity.x == s.Attachment.y));
        int Worst(ParticlePriority priority, bool inclusive, int exclude = -1)
        {
            int result = -1;
            for (int i = 0; i < Capacity; i++)
            {
                var r = m_Reservations[i]; if (i == exclude || r.Until == 0 || (inclusive ? r.Priority > priority : r.Priority >= priority)) continue;
                if (result < 0 || r.Priority < m_Reservations[result].Priority || (r.Priority == m_Reservations[result].Priority && r.DistanceSquared > m_Reservations[result].DistanceSquared)) result = i;
            }
            return result;
        }
        void Retire(int slot) { if (m_Reservations[slot].Until == 0) return; m_Area = math.max(0,m_Area-m_Reservations[slot].Area); m_Live--; m_Reservations[slot] = default; }
        bool Kill(int slot)
        {
            for (int i = 0; i < m_SpawnCount; i++) if (m_Spawns[i].Target.x == (uint)slot) { m_Spawns[i] = new ParticleSpawn { Target = new uint4((uint)slot,0,0,0) }; return true; }
            if (m_SpawnCount >= ParticleLimits.SpawnsPerFrame) return false;
            m_Spawns[m_SpawnCount++] = new ParticleSpawn { Target = new uint4((uint)slot,0,0,0) };
            return true;
        }
        public void SimulateCpu()
        {
            Check();
            for (int i = 0; i < m_SpawnCount; i++) m_Cpu[(int)m_Spawns[i].Target.x] = m_Spawns[i].State;
            new SimulateJob { States = m_Cpu, Sockets = m_Sockets, Dt = DeltaTime }.Schedule(Capacity,64).Complete();
        }
        public void Clear()
        {
            Check(); Array.Clear(m_Reservations,0,Capacity); m_Live=0; m_SpawnCount=0; m_Area=0;
            for (int i=0;i<Capacity;i++) m_Cpu[i]=default;
            for (int i=0;i<ParticleLimits.Emitters;i++) m_Sockets[i]=default;
        }
        public void Dispose() { if(m_Disposed)return; m_Disposed=true; m_Cpu.Dispose(); m_Spawns.Dispose(); m_Sockets.Dispose(); }
        void Check() { if(m_Disposed)throw new ObjectDisposedException(nameof(ParticlePool)); }
        [BurstCompile]
        struct SimulateJob : IJobParallelFor
        {
            public NativeArray<ParticleState> States;
            [ReadOnly] public NativeArray<ParticleSocket> Sockets;
            public float Dt;
            public void Execute(int index) { var p=States[index]; var socket=p.Attachment.x==0?default:Sockets[(int)p.Attachment.x-1]; States[index]=ParticleMath.Step(p,socket,Dt); }
        }
    }
}
