using System;
using System.IO;
using SPF.Contracts;
using SPF.L1.Spatial;
using SPF.L2.Combat;
using SPF.L2.AI;
using SPF.L2.Skills;
using SPF.Runtime.World;
using Unity.Collections;
using Unity.Mathematics;

namespace BrawlerFoundation
{
    [Serializable]
    public struct BwBeltConfig
    {
        public int Fighters, TargetsPerAttack, Drops, Waves, FirstWaveEnemies;
        // Same policy outputs; opt-in implementation choice, not additional saved simulation state.
        public bool UseDecisionTree;
        public static BwBeltConfig Default => new BwBeltConfig { Fighters = 64, TargetsPerAttack = 64, Drops = 32, Waves = 3, FirstWaveEnemies = 4 };
        public void Validate()
        {
            new BwSharedCombatConfig { Fighters = Fighters, TargetsPerAttack = TargetsPerAttack }.Validate();
            if (Fighters < 2 || Drops < 1 || Drops > 256 || Waves < 1 || Waves > 32 || FirstWaveEnemies < 1 || FirstWaveEnemies > Fighters - Waves)
                throw new ArgumentOutOfRangeException(nameof(Fighters), "Belt waves, drops and fighters must fit fixed capacities.");
        }
    }

    public struct BwBeltMotion
    {
        public float2 GroundVelocity;
        public float Height, PreviousHeight, HeightVelocity;
        public int ComboGraceTicks;
        public TickInputBuffer BufferedAttack;
    }

    public enum BwBeltDropKind : byte { Coin, Heal }
    public struct BwBeltDrop
    {
        public float2 Ground;
        public BwBeltDropKind Kind;
        public float Value;
        public int RemainingTicks;
    }

    public static class BwBeltKeys
    {
        // Added only to the opt-in layout. Classic FighterInfo, resources and serialized bytes stay intact.
        public static readonly ColumnKey<float2> Ground = new ColumnKey<float2>(BwKeys.Fighter, "BeltGround.V1");
        public static readonly ColumnKey<float2> PreviousGround = new ColumnKey<float2>(BwKeys.Fighter, "BeltPreviousGround.V1");
        public static readonly ColumnKey<BwBeltMotion> Motion = new ColumnKey<BwBeltMotion>(BwKeys.Fighter, "BeltMotion.V1");
        public static readonly ResourceKey<BwBeltState> State = new ResourceKey<BwBeltState>("Bw.BeltScroller.V1");
    }

    public static class BwBeltRules
    {
        public const float DepthHalf = 2.4f, DepthProjection = .55f, BodyDepth = .28f;
        public const int JumpButton = 2, HealButton = 3;
        public static float2 Project(float2 ground, float height) => new float2(ground.x, ground.y * DepthProjection + height);
        public static float2 ClampGround(float2 ground) => math.clamp(ground, new float2(-BwRules.ArenaHalf, -DepthHalf), new float2(BwRules.ArenaHalf, DepthHalf));
        public static SkillSlots CreateSkills() => new SkillSlots(
            new SkillSlotDefinition(11, 0, SkillActivation.Hold, 12),
            new SkillSlotDefinition(12, 1, SkillActivation.Tap, 100, 2),
            new SkillSlotDefinition(13, 2, SkillActivation.Tap, 50),
            new SkillSlotDefinition(14, 3, SkillActivation.Tap, 480));

        public static bool CanUseSlot(SimWorld world, int slot)
        {
            if (world.Resource(BwKeys.Game).Flow != BwFlow.Fighting) return false;
            var info = world.Column(BwKeys.Info); var motion = world.Column(BwBeltKeys.Motion);
            for (int i = 0; i < world.Table(BwKeys.Fighter).Count; i++)
            {
                var f = info[i]; if (f.Team != 0) continue;
                bool free = f.Hp > 0 && (f.State == FighterState.Idle || f.State == FighterState.Walk);
                if (slot == 0) return f.Hp > 0 && f.State != FighterState.Hit && f.State != FighterState.KO;
                return free && (slot < 2 || motion[i].Height <= .001f) && (slot != HealButton || f.Hp < f.MaxHp);
            }
            return false;
        }
    }

    /// <summary>Authoritative bounded loot plus derived spatial/scratch storage. The grid is rebuilt
    /// from saved columns before use, never saved and never influenced by a presentation budget.</summary>
    public sealed class BwBeltState : IDisposable, IResettableResource, ISnapshotResource
    {
        const int Magic = 0x42454C54;
        public readonly BwBeltConfig Config;
        public readonly SpatialGrid Grid;
        public NativeArray<DecisionNode> DecisionProgram; // derived immutable policy; excluded from snapshots
        public NativeArray<float2> SeparatedGround;
        public NativeArray<BwBeltDrop> Drops;
        public int Coins, HealsCollected, RejectedDrops, RejectedSpawns;
        public int SeparationCandidates, HitCandidates, LastGridDropped;
        public float MaxGroundStep; // derived broadphase padding for simultaneous moving-target sweeps
        public int ActiveDrops { get { int n = 0; for (int i = 0; i < Drops.Length; i++) if (Drops[i].RemainingTicks > 0) n++; return n; } }
        public BwBeltState(BwBeltConfig config)
        {
            config.Validate(); Config = config;
            DecisionProgram = BwBeltDecisions.CreateProgram();
            Grid = new SpatialGrid(new int2(20, 8), 1f, config.Fighters) { Origin = new float2(-10, -4) };
            SeparatedGround = new NativeArray<float2>(config.Fighters, Allocator.Persistent);
            Drops = new NativeArray<BwBeltDrop>(config.Drops, Allocator.Persistent);
        }
        public bool TryDrop(float2 ground, BwBeltDropKind kind, float value)
        {
            for (int i = 0; i < Drops.Length; i++) if (Drops[i].RemainingTicks == 0)
            {
                Drops[i] = new BwBeltDrop { Ground = BwBeltRules.ClampGround(ground), Kind = kind, Value = value, RemainingTicks = 720 };
                return true;
            }
            if (RejectedDrops < int.MaxValue) RejectedDrops++;
            return false; // reject newest; never evict an earned pickup or allocate an overflow list.
        }
        public void Rebuild(SimWorld world)
        {
            int n = 0; var info = world.Column(BwKeys.Info); var ground = world.Column(BwBeltKeys.Ground); var previous = world.Column(BwBeltKeys.PreviousGround); MaxGroundStep = 0;
            for (int i = 0; i < world.Table(BwKeys.Fighter).Count; i++)
                if (info[i].State != FighterState.KO) { MaxGroundStep = math.max(MaxGroundStep, math.distance(previous[i], ground[i])); Grid.Staging.Set(n++, new GridEntry { Owner = i, Position = ground[i], Radius = BwRules.BodyHalfWidth }); }
            Grid.StagingCount.Set(0, n); Grid.ScheduleBuild(default).Complete(); LastGridDropped = Grid.DroppedLastBuild;
        }
        public void OnReset()
        {
            MaxGroundStep = 0; Grid.OnReset(); for (int i = 0; i < Drops.Length; i++) Drops[i] = default;
            Coins = HealsCollected = RejectedDrops = RejectedSpawns = SeparationCandidates = HitCandidates = LastGridDropped = 0;
        }
        public void WriteSnapshot(BinaryWriter w)
        {
            w.Write(Magic); w.Write(1); w.Write(Config.Fighters); w.Write(Config.TargetsPerAttack); w.Write(Config.Drops); w.Write(Config.Waves); w.Write(Config.FirstWaveEnemies);
            w.Write(Coins); w.Write(HealsCollected); w.Write(RejectedDrops); w.Write(RejectedSpawns);
            NativeIO.Write(w, Drops);
        }
        public void ReadSnapshot(BinaryReader r)
        {
            if (r.ReadInt32() != Magic || r.ReadInt32() != 1 || r.ReadInt32() != Config.Fighters || r.ReadInt32() != Config.TargetsPerAttack ||
                r.ReadInt32() != Config.Drops || r.ReadInt32() != Config.Waves || r.ReadInt32() != Config.FirstWaveEnemies)
                throw new InvalidDataException("Unsupported belt-scroller snapshot configuration.");
            Coins = r.ReadInt32(); HealsCollected = r.ReadInt32(); RejectedDrops = r.ReadInt32(); RejectedSpawns = r.ReadInt32();
            if (Coins < 0 || HealsCollected < 0 || RejectedDrops < 0 || RejectedSpawns < 0) throw new InvalidDataException("Invalid belt counters.");
            NativeIO.ReadAll(r, Drops);
            for (int i = 0; i < Drops.Length; i++)
            {
                var d = Drops[i];
                if (d.RemainingTicks < 0 || d.RemainingTicks > 720 || d.Kind > BwBeltDropKind.Heal || !math.all(math.isfinite(d.Ground)) || !math.isfinite(d.Value) || d.Value < 0)
                    throw new InvalidDataException("Invalid belt pickup.");
            }
            Grid.OnReset(); SeparationCandidates = HitCandidates = LastGridDropped = 0;
        }
        public void Dispose() { Grid.Dispose(); SeparatedGround.Dispose(); Drops.Dispose(); DecisionProgram.Dispose(); }
    }
}
