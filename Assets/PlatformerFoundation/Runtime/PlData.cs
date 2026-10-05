using SPF.Contracts;
using SPF.L1.Spatial;
using SPF.L2.Movement;
using SPF.Runtime.World;
using Unity.Mathematics;

namespace PlatformerFoundation
{
    public enum PlFlow : byte { Menu, Playing, Dying, LevelComplete, GameOver, Won }

    public enum PlCommandKind : byte { Start, NextLevel, Retry, Menu }

    public static class PlTile
    {
        public const byte Empty = 0, Solid = 1, OneWay = 2;
        // Hazard layer
        public const byte Spikes = 1, Goal = 2;
    }

    public static class PlButton
    {
        public const int Jump = 0;
    }

    public struct WalkerInfo
    {
        public float Dir, Speed;
        public float VelocityY;
        public float2 Half;
        public bool Dead;
        public float DeadTimer;
    }

    public struct PlatformInfo
    {
        public float2 Origin, Travel, Half;
        public float Period, Phase;
    }

    public enum PlFeedbackKind : byte { Jump, Land, Coin, Stomp, Die, Goal }

    public struct PlFeedback
    {
        public PlFeedbackKind Kind;
        public float2 Position;
    }

    public static class PlKeys
    {
        public static readonly TableKey Walker = new TableKey("Pl.Walker");
        public static readonly ColumnKey<float2> WalkerPosition = new ColumnKey<float2>(Walker, "Position");
        public static readonly ColumnKey<float2> WalkerPrev = new ColumnKey<float2>(Walker, "Prev");
        public static readonly ColumnKey<WalkerInfo> WalkerInfo = new ColumnKey<WalkerInfo>(Walker, "Info");

        public static readonly TableKey Platform = new TableKey("Pl.Platform");
        public static readonly ColumnKey<float2> PlatformPosition = new ColumnKey<float2>(Platform, "Position");
        public static readonly ColumnKey<float2> PlatformPrev = new ColumnKey<float2>(Platform, "Prev");
        public static readonly ColumnKey<PlatformInfo> PlatformInfo = new ColumnKey<PlatformInfo>(Platform, "Info");

        public static readonly TableKey Coin = new TableKey("Pl.Coin");
        public static readonly ColumnKey<float2> CoinPosition = new ColumnKey<float2>(Coin, "Position");

        public static readonly ResourceKey<PlGameState> Game = new ResourceKey<PlGameState>("Pl.Game");
        public static readonly ResourceKey<TileMap> Map = new ResourceKey<TileMap>("Pl.Map");
        public static readonly ResourceKey<TileMap> Hazards = new ResourceKey<TileMap>("Pl.Hazards");
        public static readonly ResourceKey<EventQueue<PlFeedback>> Feedback = new ResourceKey<EventQueue<PlFeedback>>("Pl.Feedback");
    }

    /// <summary>Hero, flow and progress (main-thread resource; the hero is a single actor kept here).</summary>
    public sealed class PlGameState : ISnapshotResource, IResettableResource
    {
        public static readonly float2 HeroHalf = new float2(0.35f, 0.45f);

        public PlFlow Flow = PlFlow.Menu;
        public int Level, Lives, Coins, CoinsInLevel, Stomps;
        public float2 Hero, HeroPrev, Start;
        public float Facing = 1f;
        public PlatformerState Motor;
        public int Riding = -1;           // platform row stood on (carry next tick)
        public float FlowTimer, Time;
        public float2 GoalPosition;
        public InputFrame Input;
        public PlatformerTuning Tuning = PlatformerTuning.Default;
        public readonly System.Collections.Generic.Queue<PlCommandKind> Commands = new System.Collections.Generic.Queue<PlCommandKind>();
        public int LevelBuilds, Version;

        public void Send(PlCommandKind kind) => Commands.Enqueue(kind);

        public void OnReset()
        {
            Flow = PlFlow.Menu;
            Commands.Clear();
            Input = default;
            Version++;
        }

        public void WriteSnapshot(System.IO.BinaryWriter w)
        {
            w.Write((byte)Flow); w.Write(Level); w.Write(Lives); w.Write(Coins); w.Write(CoinsInLevel); w.Write(Stomps);
            NativeIO.WriteValue(w, Hero); NativeIO.WriteValue(w, HeroPrev); NativeIO.WriteValue(w, Start);
            w.Write(Facing);
            NativeIO.WriteValue(w, Motor);
            w.Write(Riding); w.Write(FlowTimer); w.Write(Time);
            NativeIO.WriteValue(w, GoalPosition);
            NativeIO.WriteValue(w, Input);
            NativeIO.WriteValue(w, Tuning);
            w.Write(Commands.Count);
            foreach (var c in Commands) w.Write((byte)c);
        }

        public void ReadSnapshot(System.IO.BinaryReader r)
        {
            Flow = (PlFlow)r.ReadByte(); Level = r.ReadInt32(); Lives = r.ReadInt32(); Coins = r.ReadInt32(); CoinsInLevel = r.ReadInt32(); Stomps = r.ReadInt32();
            Hero = NativeIO.ReadValue<float2>(r); HeroPrev = NativeIO.ReadValue<float2>(r); Start = NativeIO.ReadValue<float2>(r);
            Facing = r.ReadSingle();
            Motor = NativeIO.ReadValue<PlatformerState>(r);
            Riding = r.ReadInt32(); FlowTimer = r.ReadSingle(); Time = r.ReadSingle();
            GoalPosition = NativeIO.ReadValue<float2>(r);
            Input = NativeIO.ReadValue<InputFrame>(r);
            Tuning = NativeIO.ReadValue<PlatformerTuning>(r);
            Commands.Clear();
            int n = r.ReadInt32();
            if (n < 0 || n > 64) throw new System.IO.InvalidDataException("Invalid command queue in snapshot.");
            for (int i = 0; i < n; i++) Commands.Enqueue((PlCommandKind)r.ReadByte());
            LevelBuilds++;
            Version++;
        }
    }
}
