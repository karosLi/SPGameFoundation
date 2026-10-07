using System;
using System.IO;
using SurvivorFoundation.Systems;
using SPF.Contracts;
using SPF.L2.Combat;
using SPF.Runtime.Session;
using SPF.Runtime.World;

namespace SurvivorFoundation
{
    /// <summary>Complete versioned envelope for composed abilities plus optional deterministic critical
    /// rules and settled damage facts. Critical cadence is authority; the bounded journal is discarded.
    /// Prior composed/weapon descriptors remain strict and do not silently accept this new layout.</summary>
    public static class SvDamageNumbersSave
    {
        public static byte[] Capture(SimSession session, string runtimeId, int maxPayloadBytes = SaveEnvelope.DefaultMaxPayloadBytes)
            => SaveEnvelope.Capture(session, Describe(session, runtimeId), maxPayloadBytes);
        public static void Restore(Stream source, SimSession session, string runtimeId, int maxPayloadBytes = SaveEnvelope.DefaultMaxPayloadBytes)
            => SaveEnvelope.Restore(source, session, Describe(session, runtimeId), maxPayloadBytes);

        public static SaveCompatibilityDescriptor Describe(SimSession session, string runtimeId)
        {
            if(session==null)throw new ArgumentNullException(nameof(session));
            var world=session.World;var runtime=world.Resource(SvKeys.Config);var settings=runtime.Settings;
            var weapons=world.Resource(SvWeapons.Key);var slots=world.Resource(SvMobileSkills.Key);
            var pulse = world.Resource(SvComposedPulseState.Key); pulse.Definition.Validate();
            if (!pulse.History.IsCreated || pulse.History.Length != pulse.Definition.Targets)
                throw new InvalidDataException("Composed pulse history differs from its fixed authored capacity.");
            if(settings.FlyingSwords.Enabled||settings.CrossedBlades.Enabled||settings.Variant==SvVariant.FlyingSwordHorde)
                throw new NotSupportedException("This envelope adapter covers Classic/Guard weapons only.");
            if(settings.Variant!=SvVariant.Classic&&settings.Variant!=SvVariant.GuardBeacon)throw new InvalidDataException("Unknown Survivor variant.");
            if(session.Clock.TickRate!=weapons.TickRate)throw new InvalidDataException("Weapon tick rate differs from session.");
            byte[] schema=SaveCompatibilityDescriptor.Encode(w=>{
                w.Write("module.survivor.damage-numbers");w.Write(1);w.Write("raw-session.clock-world-pipeline");w.Write(1);
                w.Write(world.Resource(SvKeys.Game).UsesExtendedSnapshots);
                world.WriteSnapshotSchema(w,new[]{
                    new SnapshotTableSchema(SvKeys.Enemy,"enemy.handles-soa.v1",true,false,
                        C(SvKeys.Position,"position.float2.xy"),C(SvKeys.PrevPosition,"previous.float2.xy"),C(SvKeys.Info,"EnemyInfo")),
                    new SnapshotTableSchema(SvKeys.Bullet,"bullet.pooled-soa.v1",true,true,
                        C(SvKeys.BulletPosition,"position.float2.xy"),C(SvKeys.BulletInfo,"BulletInfo.row-history")),
                    new SnapshotTableSchema(SvKeys.Gem,"gem.pooled-soa.v1",true,true,
                        C(SvKeys.GemPosition,"position.float2.xy"),C(SvKeys.GemInfo,"GemInfo"))
                },new[]{
                    R(SimWorld.DestroyQueueKey,"destroy.index-generation.sorted",SnapshotResourcePolicy.Snapshot),
                    R(SvKeys.Config,"SvRuntime.frozen-enemy-definitions",SnapshotResourcePolicy.StaticDefinition),
                    R(SvKeys.Game,"SvGameState.input-commands-flow-extension",SnapshotResourcePolicy.Snapshot),
                    R(SvKeys.EnemyGrid,"SpatialGrid",SnapshotResourcePolicy.SnapshotWithDerivedCaches,true),
                    R(SvKeys.Hits,"EventQueue.SvHit",SnapshotResourcePolicy.Snapshot,true),
                    R(SvKeys.Deaths,"EventQueue.SvDeath",SnapshotResourcePolicy.Snapshot,true),
                    R(SvKeys.BulletSpawns,"EventQueue.BulletSpawn",SnapshotResourcePolicy.Snapshot,true),
                    R(SvKeys.HeroDamage,"EventQueue.hero-positive-beacon-negative",SnapshotResourcePolicy.Snapshot,true),
                    R(SvKeys.Collected,"EventQueue.xp-int",SnapshotResourcePolicy.Snapshot,true),
                    R(SvKeys.Feedback,"SvFeedback.unsaved",SnapshotResourcePolicy.Discard),
                    R(SvWeapons.PoseKey,"ActionPoseClock",SnapshotResourcePolicy.Snapshot,true),
                    new SnapshotResourceSchema(SvWeapons.Key,"WeaponRuntime",SnapshotResourcePolicy.Snapshot,true,2),
                    R(SvComposedPulseState.Key,"SvComposedPulseState.action-stable-history.v1",SnapshotResourcePolicy.Snapshot,true),
                    R(SvMobileSkills.Key,"SkillSlots",SnapshotResourcePolicy.Snapshot,true),
                    R(AppliedDamageJournal.Key,"AppliedDamageFact.unsaved-priority-lanes.v1",SnapshotResourcePolicy.Discard,true),
                    R(CriticalDamageState.Key,"CriticalDamageState.accepted-outgoing-cadence.v1",SnapshotResourcePolicy.Snapshot,true)
                });
                SaveCompatibilityDescriptor.WriteSystemSchema(w,session.Pipeline,
                    S(typeof(FlowSystem),"survivor.flow"),new SnapshotSystemSchema(typeof(RewardSystem),"survivor.composed-reward",2),S(typeof(SpawnSystem),"survivor.spawn"),
                    new SnapshotSystemSchema(typeof(MobileSkillInputSystem),"survivor.composed-skill-input",2),new SnapshotSystemSchema(typeof(HeroSystem),"survivor.composed-hero",2),S(typeof(EnemySystem),"survivor.enemy"),
                    S(typeof(BulletSystem),"survivor.bullet"),S(typeof(EnemyGridSystem),"survivor.grid"),S(typeof(CollideSystem),"survivor.collide"),
                    S(typeof(AnnularSkillSystem),"survivor.annular"),S(typeof(ComposedPulseSystem),"survivor.composed-pulse"),
                    S(typeof(WeaponCombatSystem),"survivor.weapon"),new SnapshotSystemSchema(typeof(ResolveSystem),"survivor.settled-critical-resolve",2));
            });
            byte[] content = SaveCompatibilityDescriptor.Encode(w => {
                SvWeaponSave.WriteContent(w, session); w.Write("survivor.damage-numbers.rules"); w.Write(1);
                world.Resource(CriticalDamageState.Key).Rule.Write(w); w.Write(world.Resource(AppliedDamageJournal.Key).Capacity);
                pulse.Definition.Write(w); w.Write(SvPulseDefinition.DurationTicks); w.Write(SvPulseDefinition.ReleaseTick);
            });
            byte[] visual = SaveCompatibilityDescriptor.Encode(w => { SvWeaponSave.WriteVisuals(w, session); w.Write("pulse-pose101.nova"); });
            return new SaveCompatibilityDescriptor("survivor.damage-numbers","spf.damage-numbers-save-contract.v1",runtimeId,schema,content,visual,
                SaveCompatibilityDescriptor.Encode(w => SvWeaponSave.WriteRawCompatibility(w, session)));
        }
        static SnapshotMemberSchema C(AccessKey key,string meaning)=>new SnapshotMemberSchema(key,meaning);
        static SnapshotResourceSchema R(AccessKey key,string meaning,SnapshotResourcePolicy policy,bool level=false)=>new SnapshotResourceSchema(key,meaning,policy,level);
        static SnapshotSystemSchema S(Type type,string id)=>new SnapshotSystemSchema(type,id);
    }
}
