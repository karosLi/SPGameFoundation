using System;
using System.Collections.Generic;
using System.IO;
using SPF.Contracts;
using SPF.L1.Skeleton;
using SPF.Runtime.World;
using Unity.Mathematics;

namespace BrawlerFoundation
{
    public enum BwFlow : byte { Menu, Fighting, WaveClear, Won, Lost }

    public enum BwCommandKind : byte { Start, Menu }

    public enum FighterState : byte { Idle, Walk, Attack, Hit, KO }

    public enum AttackKind : byte { None, Jab, Cross, Kick }

    public static class BwButton
    {
        public const int Punch = 0, Kick = 1;
    }

    public struct FighterInfo
    {
        public byte Team;             // 0 = player, 1 = enemies
        public FighterState State;
        public AttackKind Attack;
        public byte Variant;          // look
        public float Hp, MaxHp;
        public float Facing;          // +1 right, -1 left
        public float StateTime;
        public float VelocityX;       // knockback
        public float Cooldown;        // AI attack cooldown
        public float Flash;           // hit flash 0..1
        public ulong HitMask;         // fighters already hit by the current attack (by row, < 64)
        public int Combo;             // player: next punch in the jab-cross chain
        public bool QueuedPunch;      // pressed during an attack: chain when it ends
    }

    public enum BwFeedbackKind : byte { Swing, Hit, KO, Wave, Lose }

    public struct BwFeedback
    {
        public BwFeedbackKind Kind;
        public float2 Position;
        public float Value;
    }

    public static class BwKeys
    {
        public static readonly TableKey Fighter = new TableKey("Bw.Fighter");
        public static readonly ColumnKey<float2> Position = new ColumnKey<float2>(Fighter, "Position");
        public static readonly ColumnKey<float2> Prev = new ColumnKey<float2>(Fighter, "Prev");
        public static readonly ColumnKey<FighterInfo> Info = new ColumnKey<FighterInfo>(Fighter, "Info");
        public static readonly ColumnKey<Animator2D> Anim = new ColumnKey<Animator2D>(Fighter, "Anim");

        public static readonly ResourceKey<BwGameState> Game = new ResourceKey<BwGameState>("Bw.Game");
        public static readonly ResourceKey<BwRig> Rig = new ResourceKey<BwRig>("Bw.Rig");
        public static readonly ResourceKey<EventQueue<BwFeedback>> Feedback = new ResourceKey<EventQueue<BwFeedback>>("Bw.Feedback");
    }

    /// <summary>Arena rules and attack data (active window, bone, reach, damage, knockback).</summary>
    public static class BwRules
    {
        public const float ArenaHalf = 9f;
        public const float PlayerSpeed = 3.2f, EnemySpeed = 1.7f;
        public const float BodyHalfWidth = 0.32f;
        public const float HurtBottom = 0.1f, HurtTop = 1.85f;
        public const float ProbeRadius = 0.22f;
        public const int Waves = 3;

        public struct AttackDef
        {
            public float Duration, ActiveFrom, ActiveTo, Damage, Knockback;
            public int Bone;           // set from the rig
        }

        public static float HitStun => 0.3f;
        public static float KoTime => 1.6f;
    }

    /// <summary>
    /// The shared fighter skeleton and its clips (static data read by jobs: an <see cref="IJobData"/> resource).
    /// Bones face right; the torso points up. Clip keys rotate bones relative to the bind pose.
    /// </summary>
    public sealed class BwRig : IDisposable, IJobData, ISnapshotResource
    {
        public readonly SkeletonAsset Asset;
        public readonly int Idle, Walk, Jab, Cross, Kick, Hit, KO;
        public readonly int Pelvis, Torso, Head, UpperF, ForeF, UpperB, ForeB, ThighF, ShinF, ThighB, ShinB;
        readonly BwRules.AttackDef[] m_Attacks = new BwRules.AttackDef[4];

        public BwRig()
        {
            var b = new SkeletonAsset.Builder()
                .Bone("pelvis", null, new float2(0f, 0.95f), 0f, 0.2f)
                .Bone("torso", "pelvis", new float2(0f, 0.05f), 90f, 0.6f)
                .Bone("head", "torso", new float2(0.62f, 0f), 0f, 0.32f)
                .Bone("upperB", "torso", new float2(0.55f, 0f), 175f, 0.32f)
                .Bone("foreB", "upperB", new float2(0.32f, 0f), 15f, 0.3f)
                .Bone("thighB", "pelvis", new float2(-0.05f, 0f), -95f, 0.45f)
                .Bone("shinB", "thighB", new float2(0.45f, 0f), 5f, 0.45f)
                .Bone("thighF", "pelvis", new float2(0.05f, 0f), -85f, 0.45f)
                .Bone("shinF", "thighF", new float2(0.45f, 0f), -5f, 0.45f)
                .Bone("upperF", "torso", new float2(0.55f, 0f), 185f, 0.32f)
                .Bone("foreF", "upperF", new float2(0.32f, 0f), 20f, 0.3f);

            b.Clip("idle", 1f, true)
                .Key("pelvis", 0f, 0f).Key("pelvis", 0.5f, 0f, new float2(0f, -0.03f))
                .Key("torso", 0f, -4f).Key("torso", 0.5f, -6f)
                .Key("upperF", 0f, 40f).Key("upperF", 0.5f, 45f).Key("foreF", 0f, 80f).Key("foreF", 0.5f, 85f)
                .Key("upperB", 0f, 35f).Key("upperB", 0.5f, 40f).Key("foreB", 0f, 85f).Key("foreB", 0.5f, 90f)
                .Key("thighF", 0f, 18f).Key("shinF", 0f, -20f).Key("thighB", 0f, -15f).Key("shinB", 0f, -8f);
            b.Clip("walk", 0.6f, true)
                .Key("pelvis", 0f, 0f).Key("pelvis", 0.15f, 0f, new float2(0f, 0.04f)).Key("pelvis", 0.3f, 0f).Key("pelvis", 0.45f, 0f, new float2(0f, 0.04f))
                .Key("thighF", 0f, 30f).Key("thighF", 0.3f, -25f)
                .Key("shinF", 0f, -10f).Key("shinF", 0.15f, -45f).Key("shinF", 0.3f, -5f)
                .Key("thighB", 0f, -25f).Key("thighB", 0.3f, 30f)
                .Key("shinB", 0f, -5f).Key("shinB", 0.3f, -10f).Key("shinB", 0.45f, -45f)
                .Key("upperF", 0f, 30f).Key("upperF", 0.3f, 60f).Key("foreF", 0f, 80f)
                .Key("upperB", 0f, 60f).Key("upperB", 0.3f, 30f).Key("foreB", 0f, 80f)
                .Key("torso", 0f, -6f);
            b.Clip("jab", 0.3f, false)
                .Key("torso", 0f, -6f).Key("torso", 0.08f, -14f).Key("torso", 0.3f, -6f)
                .Key("upperF", 0f, 40f).Key("upperF", 0.08f, 95f).Key("upperF", 0.16f, 95f).Key("upperF", 0.3f, 40f)
                .Key("foreF", 0f, 80f).Key("foreF", 0.08f, -20f).Key("foreF", 0.16f, -20f).Key("foreF", 0.3f, 80f)
                .Key("upperB", 0f, 35f).Key("foreB", 0f, 90f)
                .Key("thighF", 0f, 22f).Key("shinF", 0f, -20f).Key("thighB", 0f, -18f);
            b.Clip("cross", 0.34f, false)
                .Key("torso", 0f, -6f).Key("torso", 0.1f, -22f).Key("torso", 0.34f, -6f)
                .Key("upperB", 0f, 35f).Key("upperB", 0.1f, 98f).Key("upperB", 0.18f, 98f).Key("upperB", 0.34f, 35f)
                .Key("foreB", 0f, 90f).Key("foreB", 0.1f, -15f).Key("foreB", 0.18f, -15f).Key("foreB", 0.34f, 90f)
                .Key("upperF", 0f, 45f).Key("foreF", 0f, 95f)
                .Key("thighF", 0f, 25f).Key("shinF", 0f, -25f).Key("thighB", 0f, -22f);
            b.Clip("kick", 0.46f, false)
                .Key("torso", 0f, -6f).Key("torso", 0.14f, 18f).Key("torso", 0.28f, 18f).Key("torso", 0.46f, -6f)
                .Key("thighF", 0f, 18f).Key("thighF", 0.08f, 70f).Key("thighF", 0.14f, 92f).Key("thighF", 0.28f, 92f).Key("thighF", 0.46f, 18f)
                .Key("shinF", 0f, -20f).Key("shinF", 0.08f, -80f).Key("shinF", 0.14f, 0f).Key("shinF", 0.28f, 0f).Key("shinF", 0.46f, -20f)
                .Key("upperF", 0f, 45f).Key("upperF", 0.14f, 70f).Key("foreF", 0f, 80f)
                .Key("upperB", 0f, 40f).Key("upperB", 0.14f, 10f).Key("foreB", 0f, 85f)
                .Key("thighB", 0f, -15f);
            b.Clip("hit", 0.3f, false)
                .Key("torso", 0f, -6f).Key("torso", 0.06f, 20f).Key("torso", 0.3f, -6f)
                .Key("head", 0f, 0f).Key("head", 0.06f, 20f).Key("head", 0.3f, 0f)
                .Key("upperF", 0f, 40f).Key("upperF", 0.06f, 70f).Key("upperF", 0.3f, 40f).Key("foreF", 0f, 80f)
                .Key("upperB", 0f, 35f).Key("upperB", 0.06f, 70f).Key("upperB", 0.3f, 35f).Key("foreB", 0f, 85f)
                .Key("thighF", 0f, 18f).Key("shinF", 0f, -20f).Key("thighB", 0f, -15f);
            b.Clip("ko", 0.7f, false)
                .Key("pelvis", 0f, 0f).Key("pelvis", 0.45f, 0f, new float2(-0.3f, -0.75f)).Key("pelvis", 0.7f, 0f, new float2(-0.35f, -0.78f))
                .Key("torso", 0f, -6f).Key("torso", 0.45f, 80f).Key("torso", 0.7f, 82f)
                .Key("head", 0f, 0f).Key("head", 0.45f, 15f)
                .Key("upperF", 0f, 40f).Key("upperF", 0.45f, -60f).Key("foreF", 0f, 80f).Key("foreF", 0.45f, 10f)
                .Key("upperB", 0f, 35f).Key("upperB", 0.45f, -40f).Key("foreB", 0f, 85f).Key("foreB", 0.45f, 5f)
                .Key("thighF", 0f, 18f).Key("thighF", 0.45f, 85f).Key("shinF", 0f, -20f).Key("shinF", 0.45f, -5f)
                .Key("thighB", 0f, -15f).Key("thighB", 0.45f, 80f).Key("shinB", 0f, -8f).Key("shinB", 0.45f, 0f);
            Asset = b.Build();

            Idle = Asset.Clip("idle"); Walk = Asset.Clip("walk"); Jab = Asset.Clip("jab"); Cross = Asset.Clip("cross");
            Kick = Asset.Clip("kick"); Hit = Asset.Clip("hit"); KO = Asset.Clip("ko");
            Pelvis = Asset.Bone("pelvis"); Torso = Asset.Bone("torso"); Head = Asset.Bone("head");
            UpperF = Asset.Bone("upperF"); ForeF = Asset.Bone("foreF"); UpperB = Asset.Bone("upperB"); ForeB = Asset.Bone("foreB");
            ThighF = Asset.Bone("thighF"); ShinF = Asset.Bone("shinF"); ThighB = Asset.Bone("thighB"); ShinB = Asset.Bone("shinB");

            m_Attacks[(int)AttackKind.Jab] = new BwRules.AttackDef { Duration = 0.3f, ActiveFrom = 0.06f, ActiveTo = 0.17f, Damage = 8f, Knockback = 2.5f, Bone = ForeF };
            m_Attacks[(int)AttackKind.Cross] = new BwRules.AttackDef { Duration = 0.34f, ActiveFrom = 0.08f, ActiveTo = 0.19f, Damage = 12f, Knockback = 4f, Bone = ForeB };
            m_Attacks[(int)AttackKind.Kick] = new BwRules.AttackDef { Duration = 0.46f, ActiveFrom = 0.12f, ActiveTo = 0.29f, Damage = 16f, Knockback = 6f, Bone = ShinF };
        }

        public SkeletonView View => Asset.View;
        public BwRules.AttackDef Attack(AttackKind kind) => m_Attacks[(int)kind];
        public int ClipFor(AttackKind kind) => kind == AttackKind.Jab ? Jab : kind == AttackKind.Cross ? Cross : Kick;

        public void Dispose() => Asset.Dispose();

        // Immutable after construction: nothing to save, and a restored world builds the same rig.
        public void WriteSnapshot(BinaryWriter writer) { }
        public void ReadSnapshot(BinaryReader reader) { }
    }

    /// <summary>Flow, wave, score, player input (main-thread resource).</summary>
    public sealed class BwGameState : ISnapshotResource, IResettableResource
    {
        public BwFlow Flow = BwFlow.Menu;
        public int Wave, Score, Kos, Version;
        public float FlowTimer;
        public InputFrame Input;
        public readonly Queue<BwCommandKind> Commands = new Queue<BwCommandKind>();

        public void Send(BwCommandKind kind) => Commands.Enqueue(kind);

        public void OnReset()
        {
            Flow = BwFlow.Menu;
            Commands.Clear();
            Input = default;
            Version++;
        }

        public void WriteSnapshot(BinaryWriter w)
        {
            w.Write((byte)Flow); w.Write(Wave); w.Write(Score); w.Write(Kos); w.Write(FlowTimer);
            NativeIO.WriteValue(w, Input);
            w.Write(Commands.Count);
            foreach (var c in Commands) w.Write((byte)c);
        }

        public void ReadSnapshot(BinaryReader r)
        {
            Flow = (BwFlow)r.ReadByte(); Wave = r.ReadInt32(); Score = r.ReadInt32(); Kos = r.ReadInt32(); FlowTimer = r.ReadSingle();
            Input = NativeIO.ReadValue<InputFrame>(r);
            Commands.Clear();
            int n = r.ReadInt32();
            if (n < 0 || n > 64) throw new InvalidDataException("Invalid command queue in snapshot.");
            for (int i = 0; i < n; i++) Commands.Enqueue((BwCommandKind)r.ReadByte());
            Version++;
        }
    }
}
