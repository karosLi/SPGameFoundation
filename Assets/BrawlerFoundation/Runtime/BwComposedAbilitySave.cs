using System;
using System.IO;
using BrawlerFoundation.Systems;
using SPF.Contracts;
using SPF.Runtime.Session;
using SPF.Runtime.World;

namespace BrawlerFoundation
{
    /// <summary>Complete opt-in envelope for the composed kick/heal belt. The old weapon recipe stays
    /// strict; this distinct schema covers the additional authoritative credit and pending heal owner.</summary>
    public static class BwComposedAbilitySave
    {
        public static byte[] Capture(SimSession session, string runtimeId, int maxPayloadBytes = SaveEnvelope.DefaultMaxPayloadBytes)
            => SaveEnvelope.Capture(session, Describe(session, runtimeId), maxPayloadBytes);
        public static void Restore(Stream source, SimSession session, string runtimeId, int maxPayloadBytes = SaveEnvelope.DefaultMaxPayloadBytes)
            => SaveEnvelope.Restore(source, session, Describe(session, runtimeId), maxPayloadBytes);

        public static SaveCompatibilityDescriptor Describe(SimSession session, string runtimeId)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));
            var world = session.World;
            var belt = world.Resource(BwBeltKeys.State); var weapons = world.Resource(BwWeapons.Key);
            var slots = world.Resource(BwMobileSkills.Key); var rig = world.Resource(BwKeys.Rig);
            var c = belt.Config; c.Validate();
            var abilities = world.Resource(BwComposedAbilityState.Key); abilities.Config.Validate();
            if (session.Clock.TickRate != weapons.TickRate) throw new InvalidDataException("Weapon tick rate differs from session.");
            byte[] schema = SaveCompatibilityDescriptor.Encode(w => {
                w.Write("module.brawler.composed-ability-belt"); w.Write(1); w.Write("raw-session.clock-world-pipeline"); w.Write(1);
                world.WriteSnapshotSchema(w, new[] {
                    new SnapshotTableSchema(BwKeys.Fighter, "fighter.handles-soa.v1", true, false,
                        C(BwKeys.Position,"position.float2.xy"), C(BwKeys.Prev,"previous.float2.xy"), C(BwKeys.Info,"FighterInfo"),
                        C(BwKeys.Anim,"Animator2D"), C(BwBeltKeys.Ground,"ground.float2.x-depth"),
                        C(BwBeltKeys.PreviousGround,"previous-ground.float2.x-depth"), C(BwBeltKeys.Motion,"BwBeltMotion"))
                }, new[] {
                    R(SimWorld.DestroyQueueKey,"destroy.index-generation.sorted",SnapshotResourcePolicy.Snapshot),
                    R(BwBeltKeys.State,"BwBeltState",SnapshotResourcePolicy.SnapshotWithDerivedCaches,true),
                    R(BwWeapons.PoseKey,"ActionPoseClock",SnapshotResourcePolicy.Snapshot,true),
                    new SnapshotResourceSchema(BwWeapons.Key,"WeaponRuntime",SnapshotResourcePolicy.Snapshot,true,2),
                    R(BwComposedAbilityState.Key,"BwComposedAbilityState.earned-credit-pending-heal.v1",SnapshotResourcePolicy.Snapshot,true),
                    R(BwKeys.Rig,"BwRig.frozen-skeleton",SnapshotResourcePolicy.StaticDefinition),
                    R(BwKeys.Game,"BwGameState.input-commands-flow",SnapshotResourcePolicy.Snapshot),
                    R(BwKeys.Feedback,"BwFeedback.unsaved",SnapshotResourcePolicy.Discard),
                    R(BwMobileSkills.Key,"SkillSlots",SnapshotResourcePolicy.Snapshot,true),
                    R(BwKeys.SharedCombat,"BwSharedCombatState",SnapshotResourcePolicy.Snapshot,true)
                });
                SaveCompatibilityDescriptor.WriteSystemSchema(w, session.Pipeline,
                    S(typeof(BeltFlowSystem),"belt.flow"), new SnapshotSystemSchema(typeof(BeltFighterSystem),"belt.composed-fighter",2),
                    S(typeof(BeltWeaponSystem),"belt.weapon"), new SnapshotSystemSchema(typeof(BeltCombatSystem),"belt.composed-combat",2),
                    S(typeof(BeltComposedAbilitySystem),"belt.composed-heal-settlement"));
            });
            byte[] content = SaveCompatibilityDescriptor.Encode(w => {
                BwWeaponSave.WriteContent(w, session); w.Write("brawler.composed-abilities.rules"); w.Write(1);
                var a = abilities.Config; w.Write(a.ContentId); w.Write(a.KickDamage); w.Write(a.HealAmount);
                w.Write(a.KickCooldownTicks); w.Write(a.HealCooldownTicks); w.Write(a.KickHitHealCredit); w.Write(a.MaxHealCredit);
                w.Write(BwComposedAbilityState.HealDurationTicks); w.Write(BwComposedAbilityState.HealReleaseTick);
            });
            return new SaveCompatibilityDescriptor("brawler.composed-ability-belt", "spf.composed-ability-save-contract.v1", runtimeId, schema, content,
                SaveCompatibilityDescriptor.Encode(w => { BwWeaponSave.WriteVisuals(w, session); w.Write("kick-pose100.heal-pose105"); }),
                SaveCompatibilityDescriptor.Encode(w => BwWeaponSave.WriteRawCompatibility(w, session)));
        }
        static SnapshotMemberSchema C(AccessKey key,string meaning)=>new SnapshotMemberSchema(key,meaning);
        static SnapshotResourceSchema R(AccessKey key,string meaning,SnapshotResourcePolicy policy,bool level=false)=>new SnapshotResourceSchema(key,meaning,policy,level);
        static SnapshotSystemSchema S(Type type,string id)=>new SnapshotSystemSchema(type,id);
    }
}
