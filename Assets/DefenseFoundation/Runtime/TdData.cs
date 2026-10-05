using System.Collections.Generic;
using System.IO;
using SPF.Contracts;
using SPF.L1.Navigation;
using SPF.L1.Spatial;
using SPF.L2.Progression;
using SPF.Runtime.World;
using Unity.Mathematics;

namespace DefenseFoundation
{
    public enum TdFlow : byte { Menu, Building, Wave, Won, Lost }
    public enum TdCommandKind : byte { Start, Build, Sell, Upgrade, NextWave, Menu }
    public enum TowerKind : byte { Arrow, Cannon, Frost }

    public struct TdCommand
    {
        public TdCommandKind Kind;
        public int2 Cell;
        public TowerKind Tower;
    }

    public static class TdTile
    {
        public const byte Ground = 0, Rock = 1, Tower = 2;
    }

    public struct EnemyInfo
    {
        public byte Kind;
        public float Hp, MaxHp, Speed, Slow;   // Slow: seconds of half speed left
        public int Reward;
        public bool Dead;
    }

    public struct TowerInfo
    {
        public TowerKind Kind;
        public byte Level;          // 1..3
        public int2 Cell;
        public float Cooldown;
        public float Aim;           // radians (presentation)
        public int Invested;        // gold spent (sell refund)
    }

    public struct ShotInfo
    {
        public EntityHandle Target;
        public float2 Origin;        // the firing tower (spawn position, deterministic ordering)
        public float2 Aim;           // last known target position
        public float Speed, Damage, Splash, Slow;
        public TowerKind Kind;
    }

    public struct TdHit
    {
        public int Target;
        public float Damage, Slow;
    }

    public struct TowerDef
    {
        public float Range, Rate, Damage, Splash, Slow, ShotSpeed;
        public int Cost;
    }

    public struct EnemyDef
    {
        public float Hp, Speed;
        public int Reward;
    }

    public enum TdFeedbackKind : byte { Shot, Hit, Death, Leak, Built, Sold, Rejected }

    public struct TdFeedback
    {
        public TdFeedbackKind Kind;
        public float2 Position;
        public TowerKind Tower;
    }

    public static class TdKeys
    {
        public static readonly TableKey Enemy = new TableKey("Td.Enemy");
        public static readonly ColumnKey<float2> Position = new ColumnKey<float2>(Enemy, "Position");
        public static readonly ColumnKey<float2> PrevPosition = new ColumnKey<float2>(Enemy, "PrevPosition");
        public static readonly ColumnKey<EnemyInfo> Info = new ColumnKey<EnemyInfo>(Enemy, "Info");

        public static readonly TableKey Tower = new TableKey("Td.Tower");
        public static readonly ColumnKey<TowerInfo> TowerInfo = new ColumnKey<TowerInfo>(Tower, "Info");

        public static readonly TableKey Shot = new TableKey("Td.Shot");
        public static readonly ColumnKey<float2> ShotPosition = new ColumnKey<float2>(Shot, "Position");
        public static readonly ColumnKey<ShotInfo> ShotInfo = new ColumnKey<ShotInfo>(Shot, "Info");

        public static readonly ResourceKey<TdGameState> Game = new ResourceKey<TdGameState>("Td.Game");
        public static readonly ResourceKey<TdRules> Rules = new ResourceKey<TdRules>("Td.Rules");
        public static readonly ResourceKey<TileMap> Map = new ResourceKey<TileMap>("Td.Map");
        public static readonly ResourceKey<FlowField> Flow = new ResourceKey<FlowField>("Td.Flow");
        public static readonly ResourceKey<SpatialGrid> Grid = new ResourceKey<SpatialGrid>("Td.Grid");
        public static readonly ResourceKey<EventQueue<TdHit>> Hits = new ResourceKey<EventQueue<TdHit>>("Td.Hits");
        public static readonly ResourceKey<EventQueue<ShotInfo>> ShotSpawns = new ResourceKey<EventQueue<ShotInfo>>("Td.ShotSpawns");
        public static readonly ResourceKey<EventQueue<int>> Rewards = new ResourceKey<EventQueue<int>>("Td.Rewards");
        public static readonly ResourceKey<EventQueue<int>> Leaks = new ResourceKey<EventQueue<int>>("Td.Leaks");
        public static readonly ResourceKey<EventQueue<TdFeedback>> Feedback = new ResourceKey<EventQueue<TdFeedback>>("Td.Feedback");
    }

    /// <summary>Static rules: map layout, towers, enemies and waves (baked once; not saved in snapshots).</summary>
    public sealed class TdRules : System.IDisposable
    {
        public static readonly int2 MapSize = new int2(24, 14);
        public int2 Spawn = new int2(0, 7), Base = new int2(23, 7);
        public int StartGold = 60, StartLives = 20;
        public float BuildTime = 12f;
        public Unity.Collections.NativeArray<TowerDef> Towers;
        public Unity.Collections.NativeArray<EnemyDef> Enemies;
        public readonly List<WaveGroup[]> Waves = new List<WaveGroup[]>();
        public readonly string[] Rocks =
        {
            "........................",
            "....##..........##......",
            "....##..........##......",
            "..........##............",
            "..........##.......##...",
            "...................##...",
            "........................",
            "........................",
            "...##...................",
            "...##.........##........",
            "..............##....##..",
            ".......##...........##..",
            ".......##...............",
            "........................",
        };

        public static TdRules CreateDefault()
        {
            var r = new TdRules();
            r.Towers = new Unity.Collections.NativeArray<TowerDef>(3, Unity.Collections.Allocator.Persistent);
            r.Towers[(int)TowerKind.Arrow] = new TowerDef { Range = 3.5f, Rate = 1.4f, Damage = 6f, ShotSpeed = 14f, Cost = 10 };
            r.Towers[(int)TowerKind.Cannon] = new TowerDef { Range = 3f, Rate = 0.5f, Damage = 16f, Splash = 1.3f, ShotSpeed = 8f, Cost = 25 };
            r.Towers[(int)TowerKind.Frost] = new TowerDef { Range = 2.6f, Rate = 1f, Damage = 2f, Slow = 1.6f, ShotSpeed = 10f, Cost = 20 };
            r.Enemies = new Unity.Collections.NativeArray<EnemyDef>(3, Unity.Collections.Allocator.Persistent);
            r.Enemies[0] = new EnemyDef { Hp = 22f, Speed = 1.6f, Reward = 2 };    // runner
            r.Enemies[1] = new EnemyDef { Hp = 90f, Speed = 0.8f, Reward = 6 };    // brute
            r.Enemies[2] = new EnemyDef { Hp = 9f, Speed = 2.1f, Reward = 1 };     // swarm
            for (int w = 0; w < 10; w++)
            {
                var groups = new List<WaveGroup> { new WaveGroup { Start = 0f, Kind = 1, Count = 6 + w * 2, Interval = 0.8f } };
                if (w >= 2) groups.Add(new WaveGroup { Start = 4f, Kind = 3, Count = 8 + w * 3, Interval = 0.3f });
                if (w >= 3) groups.Add(new WaveGroup { Start = 8f, Kind = 2, Count = 1 + w / 2, Interval = 2f });
                r.Waves.Add(groups.ToArray());
            }
            return r;
        }

        /// <summary>Enemy health grows 15% per wave.</summary>
        public float HpScale(int wave) => 1f + 0.15f * wave;
        public TowerDef Tower(TowerKind kind, int level)
        {
            var d = Towers[(int)kind];
            d.Damage *= 1f + 0.6f * (level - 1);
            d.Range *= 1f + 0.1f * (level - 1);
            return d;
        }
        public int UpgradeCost(TowerKind kind, int level) => (int)(Towers[(int)kind].Cost * 0.75f * level);

        public void Dispose()
        {
            if (Towers.IsCreated) Towers.Dispose();
            if (Enemies.IsCreated) Enemies.Dispose();
        }
    }

    public sealed class TdGameState : ISnapshotResource, IResettableResource
    {
        public TdFlow Flow = TdFlow.Menu;
        public int Gold, Lives, Wave = -1, Kills;
        public float WaveTime, BuildTimer, Time;
        public readonly Queue<TdCommand> Commands = new Queue<TdCommand>();
        public int MapBuilds, Version;
        public int LastRejected;   // build commands refused (blocked path, gold, occupied)

        public void Send(TdCommandKind kind, int2 cell = default, TowerKind tower = TowerKind.Arrow) =>
            Commands.Enqueue(new TdCommand { Kind = kind, Cell = cell, Tower = tower });

        public void OnReset() { Flow = TdFlow.Menu; Commands.Clear(); Version++; }

        public void WriteSnapshot(BinaryWriter w)
        {
            w.Write((byte)Flow); w.Write(Gold); w.Write(Lives); w.Write(Wave); w.Write(Kills);
            w.Write(WaveTime); w.Write(BuildTimer); w.Write(Time); w.Write(LastRejected);
            w.Write(Commands.Count);
            foreach (var c in Commands) NativeIO.WriteValue(w, c);
        }

        public void ReadSnapshot(BinaryReader r)
        {
            Flow = (TdFlow)r.ReadByte(); Gold = r.ReadInt32(); Lives = r.ReadInt32(); Wave = r.ReadInt32(); Kills = r.ReadInt32();
            WaveTime = r.ReadSingle(); BuildTimer = r.ReadSingle(); Time = r.ReadSingle(); LastRejected = r.ReadInt32();
            Commands.Clear();
            int n = r.ReadInt32();
            if (n < 0 || n > 256) throw new InvalidDataException("Invalid command queue in snapshot.");
            for (int i = 0; i < n; i++) Commands.Enqueue(NativeIO.ReadValue<TdCommand>(r));
            MapBuilds++;
            Version++;
        }
    }
}
