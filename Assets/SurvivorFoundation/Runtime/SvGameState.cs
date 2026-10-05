using System.Collections.Generic;
using System.IO;
using SPF.Contracts;
using Unity.Mathematics;

namespace SurvivorFoundation
{
    /// <summary>
    /// Run state (main-thread resource): flow, the hero (a single actor, kept here rather than in a table),
    /// experience, upgrades, weapon timers and spawn accumulators. Jobs receive copies of what they need.
    /// </summary>
    public sealed class SvGameState : ISnapshotResource, IResettableResource
    {
        public const int UpgradeCount = 8;
        public const int MaxLevel = 5;
        public const int ChoiceCount = 3;

        public SvFlow Flow = SvFlow.Menu;
        public float2 Hero, HeroPrev;
        public float2 Facing = new float2(1f, 0f);
        public float Hp, MaxHp, Invulnerable;
        public int Level = 1, Xp, Kills;
        public float Time;
        public InputFrame Input;
        public readonly int[] Upgrades = new int[UpgradeCount];
        public readonly int[] Choices = new int[ChoiceCount];
        public int ChoiceCountOffered;
        public float SpawnAccumulator, NextElite;
        public float BoltTimer, NovaTimer, SpiralTimer, SpiralAngle, OrbitAngle;
        public int PendingLevels;
        public readonly Queue<SvCommand> Commands = new Queue<SvCommand>();
        /// <summary>Bumped when the HUD should refresh (level-ups, choices, flow).</summary>
        public int Version;

        public int Level0(Upgrade u) => Upgrades[(int)u];

        public void Send(SvCommandKind kind, int argument = 0) => Commands.Enqueue(new SvCommand { Kind = kind, Argument = argument });

        public void OnReset()
        {
            Flow = SvFlow.Menu;
            Commands.Clear();
            Input = default;
            Version++;
        }

        public void WriteSnapshot(BinaryWriter w)
        {
            w.Write((byte)Flow);
            w.Write(Hero.x); w.Write(Hero.y); w.Write(HeroPrev.x); w.Write(HeroPrev.y); w.Write(Facing.x); w.Write(Facing.y);
            w.Write(Hp); w.Write(MaxHp); w.Write(Invulnerable);
            w.Write(Level); w.Write(Xp); w.Write(Kills); w.Write(Time);
            NativeIO.WriteValue(w, Input);
            foreach (int u in Upgrades) w.Write(u);
            foreach (int c in Choices) w.Write(c);
            w.Write(ChoiceCountOffered);
            w.Write(SpawnAccumulator); w.Write(NextElite);
            w.Write(BoltTimer); w.Write(NovaTimer); w.Write(SpiralTimer); w.Write(SpiralAngle); w.Write(OrbitAngle);
            w.Write(PendingLevels);
            w.Write(Commands.Count);
            foreach (var c in Commands) { w.Write((byte)c.Kind); w.Write(c.Argument); }
        }

        public void ReadSnapshot(BinaryReader r)
        {
            Flow = (SvFlow)r.ReadByte();
            Hero = new float2(r.ReadSingle(), r.ReadSingle()); HeroPrev = new float2(r.ReadSingle(), r.ReadSingle()); Facing = new float2(r.ReadSingle(), r.ReadSingle());
            Hp = r.ReadSingle(); MaxHp = r.ReadSingle(); Invulnerable = r.ReadSingle();
            Level = r.ReadInt32(); Xp = r.ReadInt32(); Kills = r.ReadInt32(); Time = r.ReadSingle();
            Input = NativeIO.ReadValue<InputFrame>(r);
            for (int i = 0; i < Upgrades.Length; i++) Upgrades[i] = r.ReadInt32();
            for (int i = 0; i < Choices.Length; i++) Choices[i] = r.ReadInt32();
            ChoiceCountOffered = r.ReadInt32();
            SpawnAccumulator = r.ReadSingle(); NextElite = r.ReadSingle();
            BoltTimer = r.ReadSingle(); NovaTimer = r.ReadSingle(); SpiralTimer = r.ReadSingle(); SpiralAngle = r.ReadSingle(); OrbitAngle = r.ReadSingle();
            PendingLevels = r.ReadInt32();
            Commands.Clear();
            int n = r.ReadInt32();
            if (n < 0 || n > 256) throw new InvalidDataException("Invalid command queue in snapshot.");
            for (int i = 0; i < n; i++) Commands.Enqueue(new SvCommand { Kind = (SvCommandKind)r.ReadByte(), Argument = r.ReadInt32() });
            Version++;
        }
    }

    /// <summary>Upgrade effects, shared by systems, HUD and tests.</summary>
    public static class SvRules
    {
        public static int XpToNext(in SvSettings s, int level) => (int)math.round(s.XpBase + s.XpPerLevel * (level - 1));
        public static float Might(SvGameState g) => 1f + 0.2f * g.Level0(Upgrade.Might);
        public static float HeroSpeed(in SvSettings s, SvGameState g) => s.HeroSpeed * (1f + 0.12f * g.Level0(Upgrade.Speed));
        public static float MaxHp(in SvSettings s, SvGameState g) => s.HeroHp * (1f + 0.25f * g.Level0(Upgrade.Vitality));
        public static float Magnet(in SvSettings s, SvGameState g) => s.MagnetRadius * (1f + 0.5f * g.Level0(Upgrade.Magnet));
        public static int BoltCount(SvGameState g) => g.Level0(Upgrade.Bolt);
        public static int NovaBullets(SvGameState g) => g.Level0(Upgrade.Nova) == 0 ? 0 : 12 + 6 * g.Level0(Upgrade.Nova);
        public static int SpiralArms(SvGameState g) => g.Level0(Upgrade.Spiral) == 0 ? 0 : 1 + g.Level0(Upgrade.Spiral);
        public static int OrbitBlades(SvGameState g) => g.Level0(Upgrade.Orbit) == 0 ? 0 : 1 + g.Level0(Upgrade.Orbit);

        public static readonly string[] Names = { "Magic Bolt", "Nova", "Spiral", "Orbit Blades", "Swiftness", "Vitality", "Magnet", "Might" };
    }
}
