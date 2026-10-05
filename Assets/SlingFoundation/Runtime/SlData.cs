using System.Collections.Generic;
using System.IO;
using SPF.Contracts;
using SPF.L1.Physics;
using SPF.Runtime.World;
using Unity.Mathematics;

namespace SlingFoundation
{
    public enum SlFlow : byte { Menu, Aiming, Flying, LevelClear, Failed, Won }

    public enum SlCommandKind : byte { Start, Launch, NextLevel, Retry, Menu }

    public struct SlCommand
    {
        public SlCommandKind Kind;
        public float2 Pull;          // Launch: drag vector from the sling (the bird flies the other way)
    }

    public enum PieceKind : byte { None, Ground, Wood, Stone, Glass, Target, Bird }

    public enum SlFeedbackKind : byte { Launch, Hit, Break, TargetDown, Clear, Fail }

    public struct SlFeedback
    {
        public SlFeedbackKind Kind;
        public PieceKind Piece;
        public float2 Position;
        public float Strength;
    }

    public static class SlKeys
    {
        public static readonly ResourceKey<SlGameState> Game = new ResourceKey<SlGameState>("Sl.Game");
        public static readonly ResourceKey<PhysicsWorld2D> Physics = new ResourceKey<PhysicsWorld2D>("Sl.Physics");
        public static readonly ResourceKey<EventQueue<SlFeedback>> Feedback = new ResourceKey<EventQueue<SlFeedback>>("Sl.Feedback");
    }

    public static class SlRules
    {
        public const int Capacity = 512;
        public static readonly float2 Sling = new float2(-12f, 3f);
        public const float MaxPull = 3f;
        public const float LaunchPower = 9f;     // m/s per metre of pull
        public const float BirdRadius = 0.35f;
        public const int BirdsPerLevel = 3;

        public static float2 LaunchVelocity(float2 pull)
        {
            float len = math.length(pull);
            if (len > MaxPull) pull *= MaxPull / len;
            return -pull * LaunchPower;
        }

        /// <summary>Impulse a piece absorbs before it breaks.</summary>
        public static float Toughness(PieceKind kind) => kind switch
        {
            PieceKind.Wood => 9f,
            PieceKind.Stone => 22f,
            PieceKind.Glass => 3f,
            PieceKind.Target => 3.5f,
            _ => float.PositiveInfinity,
        };

        public static int Points(PieceKind kind) => kind switch
        {
            PieceKind.Wood => 100,
            PieceKind.Stone => 250,
            PieceKind.Glass => 50,
            PieceKind.Target => 1000,
            _ => 0,
        };
    }

    /// <summary>Flow, score and the per-body piece table (kind and remaining toughness, indexed by physics body id).</summary>
    public sealed class SlGameState : ISnapshotResource, IResettableResource
    {
        public SlFlow Flow = SlFlow.Menu;
        public int Level, Score, BirdsLeft, TargetsLeft, Version, LevelBuilds;
        public int Bird = -1;
        public float FlightTime, StillTime, ClearTimer;
        public readonly PieceKind[] Kind = new PieceKind[SlRules.Capacity];
        public readonly float[] Hp = new float[SlRules.Capacity];
        public readonly Queue<SlCommand> Commands = new Queue<SlCommand>();

        public void Send(SlCommandKind kind, float2 pull = default) => Commands.Enqueue(new SlCommand { Kind = kind, Pull = pull });

        public void OnReset()
        {
            Flow = SlFlow.Menu;
            Commands.Clear();
            Bird = -1;
            System.Array.Clear(Kind, 0, Kind.Length);
            Version++;
        }

        public void WriteSnapshot(BinaryWriter w)
        {
            w.Write((byte)Flow); w.Write(Level); w.Write(Score); w.Write(BirdsLeft); w.Write(TargetsLeft); w.Write(Bird);
            w.Write(FlightTime); w.Write(StillTime); w.Write(ClearTimer);
            for (int i = 0; i < Kind.Length; i++) { w.Write((byte)Kind[i]); w.Write(Hp[i]); }
            w.Write(Commands.Count);
            foreach (var c in Commands) { w.Write((byte)c.Kind); w.Write(c.Pull.x); w.Write(c.Pull.y); }
        }

        public void ReadSnapshot(BinaryReader r)
        {
            Flow = (SlFlow)r.ReadByte(); Level = r.ReadInt32(); Score = r.ReadInt32(); BirdsLeft = r.ReadInt32(); TargetsLeft = r.ReadInt32(); Bird = r.ReadInt32();
            FlightTime = r.ReadSingle(); StillTime = r.ReadSingle(); ClearTimer = r.ReadSingle();
            for (int i = 0; i < Kind.Length; i++) { Kind[i] = (PieceKind)r.ReadByte(); Hp[i] = r.ReadSingle(); }
            Commands.Clear();
            int n = r.ReadInt32();
            if (n < 0 || n > 64) throw new InvalidDataException("Invalid command queue in snapshot.");
            for (int i = 0; i < n; i++) Commands.Enqueue(new SlCommand { Kind = (SlCommandKind)r.ReadByte(), Pull = new float2(r.ReadSingle(), r.ReadSingle()) });
            LevelBuilds++;
            Version++;
        }
    }
}
