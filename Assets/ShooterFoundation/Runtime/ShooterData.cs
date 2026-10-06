using System;
using System.IO;
using SPF.Contracts;
using SPF.L1.Spatial;
using SPF.Runtime.World;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace ShooterFoundation
{
    public enum ShooterFlow : byte { Menu, Playing, Upgrade, Won, Dead }
    public enum ShooterUpgrade : byte { Power, Cadence, Beam, Wingman, Repair }
    public enum ShooterCommandKind : byte { Start, Choose, Menu }
    public struct ShooterCommand { public ShooterCommandKind Kind; public int Choice; }
    public struct ShooterEnemy
    {
        public int Id;
        public byte Kind;
        public float Hp, MaxHp, Radius, Speed, FireTimer, Age, BaseX, Flash;
    }
    public struct ShooterBullet { public float2 Velocity; public float Radius, Damage, Life; public bool Hostile; }
    public struct ShooterPickup { public bool Heal; public float Life; }
    public struct ShooterHit { public int Target, Candidates; public float Fraction; }
    public enum ShooterFeedbackKind : byte { Shot, Hit, Destroyed, Pickup, Hurt, Upgrade }
    public struct ShooterFeedback { public ShooterFeedbackKind Kind; public float2 Position; public float Scale; }

    [Serializable]
    public struct ShooterSettings
    {
        public float2 ArenaHalf;
        public int EnemyCapacity, BulletCapacity, PickupCapacity, Waves, EnemiesPerWave;
        public float HeroHp, HeroSpeed, SpawnInterval, EnemyHp, EnemySpeed, EnemyFireInterval;
        public float ShotInterval, ShotSpeed, ShotDamage, BeamRange, BeamDps, GridCell;
        public bool SpawnWaves;
        public static ShooterSettings Default => new ShooterSettings
        {
            ArenaHalf = new float2(5f, 9f), EnemyCapacity = 512, BulletCapacity = 4096, PickupCapacity = 512,
            Waves = 5, EnemiesPerWave = 10, HeroHp = 100f, HeroSpeed = 8f, SpawnInterval = 0.52f,
            EnemyHp = 20f, EnemySpeed = 1.4f, EnemyFireInterval = 2.1f,
            ShotInterval = 0.20f, ShotSpeed = 22f, ShotDamage = 12f, BeamRange = 7.5f, BeamDps = 24f,
            GridCell = 1f, SpawnWaves = true,
        };
        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        public void Validate()
        {
            if (!math.all(math.isfinite(ArenaHalf)) || math.any(ArenaHalf < new float2(2f, 4f)) || math.any(ArenaHalf > 128f) ||
                EnemyCapacity < 1 || EnemyCapacity > 65536 || BulletCapacity < 1 || BulletCapacity > 131072 || PickupCapacity < 1 || PickupCapacity > 65536 ||
                !Finite(HeroHp) || !Finite(HeroSpeed) || !Finite(SpawnInterval) || !Finite(EnemyHp) || !Finite(EnemySpeed) ||
                !Finite(EnemyFireInterval) || !Finite(ShotInterval) || !Finite(ShotSpeed) || !Finite(ShotDamage) || !Finite(BeamRange) || !Finite(BeamDps) || !Finite(GridCell) ||
                Waves < 1 || EnemiesPerWave < 1 || HeroHp <= 0f || HeroSpeed < 0f || SpawnInterval <= 0f || EnemyHp <= 0f ||
                EnemySpeed < 0f || EnemyFireInterval <= 0f || ShotInterval <= 0f || ShotSpeed <= 0f || ShotDamage <= 0f ||
                BeamRange <= 0f || BeamDps <= 0f || GridCell < 0.25f || GridCell > 8f)
                throw new ArgumentOutOfRangeException(nameof(ShooterSettings), "Invalid shooter bounds, rates or capacities.");
        }
    }
    public sealed class ShooterRules { public readonly ShooterSettings Settings; public ShooterRules(ShooterSettings settings) { settings.Validate(); Settings = settings; } }

    /// <summary>Blittable, snapshot-complete run state. Presentation budget never enters this struct.</summary>
    public struct ShooterRun
    {
        public ShooterFlow Flow;
        public float2 Hero, HeroPrevious, Move, Drag, BeamEnd;
        public float Hp, Invulnerable, Time, WaveTime, SpawnTimer, ShotTimer, WingTimer;
        public int Wave, Spawned, Kills, Coins, NextEnemyId, Power, Cadence, Beam, Wingman;
        public int Choice0, Choice1, Choice2, BeamTargetId, Version, DroppedSpawns, CandidateTests;
    }
    public sealed class ShooterState : ISnapshotResource, IResettableResource
    {
        public ShooterRun Run;
        readonly ShooterCommand[] m_Commands = new ShooterCommand[16];
        int m_Count, m_Head;
        public int DroppedCommands { get; private set; }
        public bool Send(ShooterCommandKind kind, int choice = 0)
        {
            if (m_Count == m_Commands.Length) { DroppedCommands++; return false; }
            m_Commands[(m_Head + m_Count++) % m_Commands.Length] = new ShooterCommand { Kind = kind, Choice = choice }; return true;
        }
        public bool TryTake(out ShooterCommand command)
        {
            if (m_Count == 0) { command = default; return false; }
            command = m_Commands[m_Head]; m_Head = (m_Head + 1) % m_Commands.Length; m_Count--; return true;
        }
        public int Choice(int index) => index == 0 ? Run.Choice0 : index == 1 ? Run.Choice1 : index == 2 ? Run.Choice2 : -1;
        public void CancelInput() { Run.Move = Run.Drag = float2.zero; }
        public void OnReset() { Run = default; m_Count = m_Head = DroppedCommands = 0; }
        public void WriteSnapshot(BinaryWriter w)
        {
            NativeIO.WriteValue(w, Run); w.Write(DroppedCommands); w.Write(m_Count);
            for (int i = 0; i < m_Count; i++) NativeIO.WriteValue(w, m_Commands[(m_Head + i) % m_Commands.Length]);
        }
        public void ReadSnapshot(BinaryReader r)
        {
            Run = NativeIO.ReadValue<ShooterRun>(r); DroppedCommands = r.ReadInt32(); m_Count = r.ReadInt32(); m_Head = 0;
            if (m_Count < 0 || m_Count > m_Commands.Length) throw new InvalidDataException("Invalid shooter command count.");
            for (int i = 0; i < m_Count; i++) m_Commands[i] = NativeIO.ReadValue<ShooterCommand>(r);
        }
    }
    public sealed class ShooterScratch : IDisposable, IJobData, ISnapshotResource
    {
        public NativeArray<ShooterHit> Hits;
        public ShooterScratch(int bullets) { Hits = new NativeArray<ShooterHit>(bullets, Allocator.Persistent); }
        // Derived per-tick output: every live bullet slot is overwritten before it is ever read.
        // Explicit empty snapshot hooks acknowledge this contract to SimWorld.SnapshotGaps.
        public void WriteSnapshot(BinaryWriter writer) { }
        public void ReadSnapshot(BinaryReader reader) { }
        public void Dispose() { if (Hits.IsCreated) Hits.Dispose(); }
    }
    public static class ShooterKeys
    {
        public static readonly TableKey Enemy = new TableKey("Shooter.Enemy"), Bullet = new TableKey("Shooter.Bullet"), Pickup = new TableKey("Shooter.Pickup");
        public static readonly ColumnKey<float2> EnemyPosition = new ColumnKey<float2>(Enemy, "Position"), EnemyPrevious = new ColumnKey<float2>(Enemy, "Previous");
        public static readonly ColumnKey<ShooterEnemy> Enemies = new ColumnKey<ShooterEnemy>(Enemy, "Info");
        public static readonly ColumnKey<float2> BulletPosition = new ColumnKey<float2>(Bullet, "Position"), BulletPrevious = new ColumnKey<float2>(Bullet, "Previous");
        public static readonly ColumnKey<ShooterBullet> Bullets = new ColumnKey<ShooterBullet>(Bullet, "Info");
        public static readonly ColumnKey<float2> PickupPosition = new ColumnKey<float2>(Pickup, "Position");
        public static readonly ColumnKey<ShooterPickup> Pickups = new ColumnKey<ShooterPickup>(Pickup, "Info");
        public static readonly ResourceKey<ShooterState> State = new ResourceKey<ShooterState>("Shooter.State");
        public static readonly ResourceKey<ShooterRules> Rules = new ResourceKey<ShooterRules>("Shooter.Rules");
        public static readonly ResourceKey<SpatialGrid> Grid = new ResourceKey<SpatialGrid>("Shooter.Grid");
        public static readonly ResourceKey<ShooterScratch> Scratch = new ResourceKey<ShooterScratch>("Shooter.Scratch");
        public static readonly ResourceKey<EventQueue<ShooterFeedback>> Feedback = new ResourceKey<EventQueue<ShooterFeedback>>("Shooter.Feedback");
    }

    public static class ShooterMath
    {
        /// <summary>Positive repeat for continuously scrolling presentation, including negative inputs.</summary>
        public static float Repeat(float value, float length) => value - math.floor(value / length) * length;

        /// <summary>Legacy circle contact in [0,1], preserving the original tiny-motion cutoff for replay.
        /// New consumers should use CombatSweep.PointCircle/Circles.</summary>
        public static bool Sweep(float2 from, float2 to, float2 center, float radius, out float t)
            => SPF.L2.Combat.CombatSweep.PointCircleLegacy(from, to, center, radius, out t);

        public static float2 WingPosition(in ShooterRun run) => run.Hero + new float2(-0.95f, -0.25f);
    }
}
