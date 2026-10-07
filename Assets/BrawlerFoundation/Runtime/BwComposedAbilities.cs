using System;
using System.IO;
using SPF.Contracts;
using SPF.L2.Skills;
using SPF.Runtime.Scheduling;
using SPF.Runtime.World;
using Unity.Jobs;
using Unity.Mathematics;

namespace BrawlerFoundation
{
    /// <summary>Opt-in game content. The shared skill slots own charges; this definition owns only
    /// brawler tuning and the independent kick-hit-earned heal-credit rule. No new weapon family.</summary>
    [Serializable]
    public struct BwComposedAbilityConfig
    {
        public int ContentId, KickCooldownTicks, HealCooldownTicks, KickHitHealCredit, MaxHealCredit;
        public float KickDamage, HealAmount;
        public static BwComposedAbilityConfig DataVariant => new BwComposedAbilityConfig
        { ContentId = 2101, KickDamage = 22, HealAmount = 18, KickCooldownTicks = 84, HealCooldownTicks = 360 };
        public static BwComposedAbilityConfig Default
        {
            get { var c = DataVariant; c.ContentId = 2102; c.KickHitHealCredit = 6; c.MaxHealCredit = 18; return c; }
        }
        public void Validate()
        {
            if (ContentId <= 0 || !math.isfinite(KickDamage) || KickDamage <= 0 || KickDamage > 10000 ||
                !math.isfinite(HealAmount) || HealAmount <= 0 || HealAmount > 10000 ||
                KickCooldownTicks < 1 || KickCooldownTicks > 216000 || HealCooldownTicks < 1 || HealCooldownTicks > 216000 ||
                KickHitHealCredit < 0 || KickHitHealCredit > 10000 || MaxHealCredit < 0 || MaxHealCredit > 10000 ||
                (KickHitHealCredit == 0) != (MaxHealCredit == 0) || KickHitHealCredit > MaxHealCredit)
                throw new ArgumentOutOfRangeException(nameof(ContentId), "Invalid composed brawler ability content.");
        }
        public SkillSlots CreateSkills() => new SkillSlots(
            new SkillSlotDefinition(11, 0, SkillActivation.Hold, 12),
            new SkillSlotDefinition(12, 1, SkillActivation.Tap, KickCooldownTicks, 2),
            new SkillSlotDefinition(13, 2, SkillActivation.Tap, 50),
            new SkillSlotDefinition(14, 3, SkillActivation.Tap, HealCooldownTicks));
    }

    /// <summary>One bounded player rule owner. Direct combat settlement records earned credit after
    /// an accepted unique bone contact. An accepted heal spends its slot immediately, but consumes
    /// credit only on its later pose marker. Interruption keeps credit and never refunds charges.
    /// Explicit v1 fields and exact content checks are independent of legacy raw snapshot layouts.</summary>
    public sealed class BwComposedAbilityState : ISnapshotResource, IResettableResource
    {
        const int Magic = 0x42434131;
        public const int SchemaVersion = 1, HealDurationTicks = 18, HealReleaseTick = 9;
        public static readonly ResourceKey<BwComposedAbilityState> Key = new ResourceKey<BwComposedAbilityState>("Bw.ComposedAbilities.V1");
        public readonly BwComposedAbilityConfig Config;
        public EntityHandle Owner { get; private set; }
        public int HealCredit { get; private set; }
        public uint KickPulse { get; private set; }
        public uint HealPulse { get; private set; }
        public bool PendingHeal => HealPulse != 0;
        public int AcceptedKicks { get; private set; }
        public int AcceptedHeals { get; private set; }
        public int RejectedRequests { get; private set; }
        public int SettledKickHits { get; private set; }
        public int AppliedHeals { get; private set; }
        public int CanceledHeals { get; private set; }
        public AbilityRejection LastRejection { get; private set; }
        static int Increment(int value) => value == int.MaxValue ? value : value + 1;
        public BwComposedAbilityState(BwComposedAbilityConfig config) { config.Validate(); Config = config; }

        public static bool IsHealing(SimWorld world)
        { var pose = world.Resource(BwWeapons.PoseKey); return pose.Running && pose.ContentId == BwWeapons.HealPose; }

        // Before player admission, stop only this opt-in action if equipment changes. Use saved
        // pending/equipped action state, not the unsaved presentation Revision, so restore can resume.
        internal void BeforeTick(SimWorld world, in InputFrame input)
        {
            var info = world.Column(BwKeys.Info); int player = FindPlayer(world);
            var owner = player < 0 ? EntityHandle.Null : world.Table(BwKeys.Fighter).Handles[player];
            bool alive = player >= 0 && info[player].Hp > 0 && info[player].State != FighterState.KO;
            if (owner != Owner || !alive) { HealCredit = 0; Cancel(); Owner = alive ? owner : EntityHandle.Null; }
            var weapons = world.Resource(BwWeapons.Key); var pose = world.Resource(BwWeapons.PoseKey);
            bool equipmentChange = input.WasPressed(BwWeapons.SwitchButton) || weapons.Equipment.PendingId != 0 || weapons.Equipment.EquipRemaining > 0;
            bool interrupted = !alive || world.Resource(BwKeys.Game).Flow != BwFlow.Fighting || (player >= 0 && info[player].State == FighterState.Hit);
            if (equipmentChange || interrupted)
            {
                bool ownAction = KickPulse != 0 || PendingHeal || IsHealing(world);
                Cancel();
                if (ownAction) pose.Cancel();
                if (equipmentChange && player >= 0 && info[player].State == FighterState.Attack && info[player].Attack == AttackKind.Kick)
                { var f = info[player]; f.State = FighterState.Idle; f.Attack = AttackKind.None; f.StateTime = 0; info[player] = f; }
            }
            else if (KickPulse != 0 && (!pose.Running || pose.ContentId != BwWeapons.KickPose || pose.Timeline.PulseId != KickPulse)) KickPulse = 0;
        }
        internal bool Admit(SkillSlots slots, int slot, bool playing, bool alive, bool free, bool requested)
        {
            var result = AbilityAdmission.Evaluate(playing, alive, free, requested, slots.GetSnapshot(slot).Charges);
            if (!requested) return false;
            if (!result.Allowed || !slots.TryActivate(slot, true))
            { RejectedRequests = Increment(RejectedRequests); LastRejection = result.Allowed ? AbilityRejection.Busy : result.Reason; return false; }
            LastRejection = AbilityRejection.None;
            if (slot == BwButton.Kick) AcceptedKicks = Increment(AcceptedKicks); else AcceptedHeals = Increment(AcceptedHeals);
            return true;
        }
        internal void BindAcceptedPose(SimWorld world)
        {
            var pose = world.Resource(BwWeapons.PoseKey); uint activated = world.Resource(BwMobileSkills.Key).Activated;
            if ((activated & (1u << BwButton.Kick)) != 0 && pose.Running && pose.ContentId == BwWeapons.KickPose) KickPulse = pose.Timeline.PulseId;
            if ((activated & (1u << BwBeltRules.HealButton)) != 0 && pose.Running && pose.ContentId == BwWeapons.HealPose) { KickPulse = 0; HealPulse = pose.Timeline.PulseId; }
        }
        internal void RecordSettledKick(SimWorld world, EntityHandle source, float appliedDamage)
        {
            var pose = world.Resource(BwWeapons.PoseKey);
            if (source != Owner || appliedDamage <= 0 || KickPulse == 0 || !pose.Running || pose.ContentId != BwWeapons.KickPose || pose.Timeline.PulseId != KickPulse) return;
            SettledKickHits = Increment(SettledKickHits);
            HealCredit = math.min(Config.MaxHealCredit, HealCredit + Config.KickHitHealCredit);
        }
        internal void SettleHeal(SimWorld world)
        {
            if (!PendingHeal) return;
            int player = FindPlayer(world); var pose = world.Resource(BwWeapons.PoseKey); var weapons = world.Resource(BwWeapons.Key);
            if (player < 0 || world.Table(BwKeys.Fighter).Handles[player] != Owner || world.Resource(BwKeys.Game).Flow != BwFlow.Fighting ||
                !pose.Running || pose.ContentId != BwWeapons.HealPose || pose.Timeline.PulseId != HealPulse ||
                weapons.Equipment.PendingId != 0 || weapons.Equipment.EquipRemaining != 0)
            { Cancel(); return; }
            var info = world.Column(BwKeys.Info); var f = info[player];
            if (f.Hp <= 0 || f.State == FighterState.KO || f.State == FighterState.Hit)
            { Cancel(); if (f.Hp <= 0 || f.State == FighterState.KO) { HealCredit = 0; Owner = EntityHandle.Null; } return; }
            if (pose.Timeline.Tick < HealReleaseTick) return;
            f.Hp = math.min(f.MaxHp, f.Hp + Config.HealAmount + HealCredit); info[player] = f;
            HealCredit = 0; HealPulse = 0; AppliedHeals = Increment(AppliedHeals); world.Resource(BwKeys.Game).Version++;
        }
        void Cancel()
        { KickPulse = 0; if (PendingHeal) CanceledHeals = Increment(CanceledHeals); HealPulse = 0; }
        static int FindPlayer(SimWorld world)
        { var info = world.Column(BwKeys.Info); for (int i = 0; i < world.Table(BwKeys.Fighter).Count; i++) if (info[i].Team == 0) return i; return -1; }
        public void OnReset()
        {
            Owner = default; HealCredit = 0; KickPulse = HealPulse = 0;
            AcceptedKicks = AcceptedHeals = RejectedRequests = SettledKickHits = AppliedHeals = CanceledHeals = 0; LastRejection = AbilityRejection.None;
        }
        public void WriteSnapshot(BinaryWriter w)
        {
            w.Write(Magic); w.Write(SchemaVersion); w.Write(Config.ContentId); w.Write(Config.KickDamage); w.Write(Config.HealAmount);
            w.Write(Config.KickCooldownTicks); w.Write(Config.HealCooldownTicks); w.Write(Config.KickHitHealCredit); w.Write(Config.MaxHealCredit);
            NativeIO.Write(w, Owner); w.Write(HealCredit); w.Write(KickPulse); w.Write(HealPulse);
            w.Write(AcceptedKicks); w.Write(AcceptedHeals); w.Write(RejectedRequests); w.Write(SettledKickHits); w.Write(AppliedHeals); w.Write(CanceledHeals); w.Write((byte)LastRejection);
        }
        public void ReadSnapshot(BinaryReader r)
        {
            if (r.ReadInt32() != Magic || r.ReadInt32() != SchemaVersion || r.ReadInt32() != Config.ContentId || r.ReadSingle() != Config.KickDamage || r.ReadSingle() != Config.HealAmount ||
                r.ReadInt32() != Config.KickCooldownTicks || r.ReadInt32() != Config.HealCooldownTicks || r.ReadInt32() != Config.KickHitHealCredit || r.ReadInt32() != Config.MaxHealCredit)
                throw new InvalidDataException("Composed brawler ability content/schema differs from snapshot.");
            var owner = NativeIO.ReadHandle(r); int credit = r.ReadInt32(); uint kick = r.ReadUInt32(), heal = r.ReadUInt32();
            int acceptedKicks = r.ReadInt32(), acceptedHeals = r.ReadInt32(), rejected = r.ReadInt32(), hits = r.ReadInt32(), applied = r.ReadInt32(), canceled = r.ReadInt32();
            var reason = (AbilityRejection)r.ReadByte();
            if (credit < 0 || credit > Config.MaxHealCredit || (!owner.IsNull && (owner.Index < 0 || owner.Generation <= 0)) ||
                (owner.IsNull && (credit != 0 || kick != 0 || heal != 0)) || (kick != 0 && heal != 0) ||
                acceptedKicks < 0 || acceptedHeals < 0 || rejected < 0 || hits < 0 || applied < 0 || canceled < 0 || applied > acceptedHeals || canceled > acceptedHeals ||
                (byte)reason > (byte)AbilityRejection.NoCharge)
                throw new InvalidDataException("Invalid bounded brawler ability state.");
            Owner = owner; HealCredit = credit; KickPulse = kick; HealPulse = heal; AcceptedKicks = acceptedKicks; AcceptedHeals = acceptedHeals;
            RejectedRequests = rejected; SettledKickHits = hits; AppliedHeals = applied; CanceledHeals = canceled; LastRejection = reason;
        }
    }
}

namespace BrawlerFoundation.Systems
{
    /// <summary>After direct weapon/bone contact settlement, a same-tick hit can interrupt a heal
    /// before its marker. This is a main-thread single-owner rule; it does not install an effect queue.</summary>
    sealed class BeltComposedAbilitySystem : SimSystemBase
    {
        public override SimPhase Phase => SimPhase.Resolve;
        public override int Order => 10;
        public override void Declare(AccessDeclaration a) => a.Write(BwComposedAbilityState.Key).Read(BwWeapons.PoseKey).Read(BwWeapons.Key).Read(BwKeys.Fighter).Write(BwKeys.Info).Write(BwKeys.Game);
        public override JobHandle OnTick(in SimContext context, JobHandle dependency)
        { dependency.Complete(); context.World.Resource(BwComposedAbilityState.Key).SettleHeal(context.World); return dependency; }
    }
}
