using System;
using System.IO;
using BrawlerFoundation.Systems;
using SPF.Contracts;
using SPF.Runtime.Session;
using SPF.Runtime.World;

namespace BrawlerFoundation
{
    /// <summary>Optional envelope entry point for the actual weapon-belt composition. Default bootstrap,
    /// raw snapshots, save slots and fixtures are unchanged. Re-describes live baked content each call.</summary>
    public static class BwWeaponSave
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
            if (session.Clock.TickRate != weapons.TickRate) throw new InvalidDataException("Weapon tick rate differs from session.");
            byte[] schema = SaveCompatibilityDescriptor.Encode(w => {
                w.Write("module.brawler.weapon-belt"); w.Write(1); w.Write("raw-session.clock-world-pipeline"); w.Write(1);
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
                    R(BwKeys.Rig,"BwRig.frozen-skeleton",SnapshotResourcePolicy.StaticDefinition),
                    R(BwKeys.Game,"BwGameState.input-commands-flow",SnapshotResourcePolicy.Snapshot),
                    R(BwKeys.Feedback,"BwFeedback.unsaved",SnapshotResourcePolicy.Discard),
                    R(BwMobileSkills.Key,"SkillSlots",SnapshotResourcePolicy.Snapshot,true),
                    R(BwKeys.SharedCombat,"BwSharedCombatState",SnapshotResourcePolicy.Snapshot,true)
                });
                SaveCompatibilityDescriptor.WriteSystemSchema(w, session.Pipeline,
                    S(typeof(BeltFlowSystem),"belt.flow"), S(typeof(BeltFighterSystem),"belt.fighter"),
                    S(typeof(BeltWeaponSystem),"belt.weapon"), S(typeof(BeltCombatSystem),"belt.combat"));
            });
            byte[] content = SaveCompatibilityDescriptor.Encode(w => WriteContent(w, session));
            return new SaveCompatibilityDescriptor("brawler.weapon-belt", "spf.weapon-save-contract.v1", runtimeId, schema, content,
                SaveCompatibilityDescriptor.Encode(w => WriteVisuals(w, session)),
                SaveCompatibilityDescriptor.Encode(w => WriteRawCompatibility(w, session)));
        }
        // Cold canonical writers shared by distinct, complete game-local recipes. These helpers do
        // not describe/relax layout coverage; the original recipe remains strict about extra state.
        internal static void WriteContent(BinaryWriter w, SimSession session)
        {
            var world = session.World; var belt = world.Resource(BwBeltKeys.State); var c = belt.Config;
            var weapons = world.Resource(BwWeapons.Key); var slots = world.Resource(BwMobileSkills.Key); var rig = world.Resource(BwKeys.Rig);
                WeaponSaveContent.WriteSession(w,session); w.Write("brawler.rules"); w.Write(1);
                w.Write(c.Fighters); w.Write(c.TargetsPerAttack); w.Write(c.Drops); w.Write(c.Waves); w.Write(c.FirstWaveEnemies); w.Write(c.UseDecisionTree);
                w.Write(BwWeapons.ActorScale); w.Write(BwRules.ArenaHalf); w.Write(BwRules.PlayerSpeed); w.Write(BwRules.EnemySpeed);
                w.Write(BwRules.BodyHalfWidth); w.Write(BwRules.HurtBottom); w.Write(BwRules.HurtTop); w.Write(BwRules.ProbeRadius); w.Write(BwRules.HitStun); w.Write(BwRules.KoTime);
                w.Write(BwBeltRules.DepthHalf); w.Write(BwBeltRules.DepthProjection); w.Write(BwBeltRules.BodyDepth);
                WeaponSaveContent.WriteRules(w,weapons,slots);
                var shared = world.Resource(BwKeys.SharedCombat); w.Write(shared.Attacks.Length); w.Write(shared.Targets.Length); w.Write(shared.TargetsPerAttack);
                w.Write(world.Resource(BwKeys.Feedback).Capacity); w.Write(belt.Drops.Length); w.Write(belt.SeparatedGround.Length);
                var grid=belt.Grid; w.Write(grid.Dimensions.x);w.Write(grid.Dimensions.y);w.Write(grid.CellSize);w.Write(grid.Capacity);w.Write(grid.LargeRadius);w.Write(grid.LargeCellScale);WeaponSaveContent.Write(w,grid.Origin);
                w.Write(belt.DecisionProgram.Length);
                for(int i=0;i<belt.DecisionProgram.Length;i++){var n=belt.DecisionProgram[i];w.Write((byte)n.Kind);w.Write(n.Required);w.Write(n.Forbidden);w.Write(n.Pass);w.Write(n.Fail);w.Write(n.Action);}
                // Skeleton data is authoritative: enemy/kick contact probes sample these bones/clips.
                var a=rig.Asset; w.Write(a.Bones.Length);
                for(int i=0;i<a.Bones.Length;i++){var b=a.Bones[i];w.Write(b.Parent);WeaponSaveContent.Write(w,b.Position);w.Write(b.Rotation);w.Write(b.Length);}
                w.Write(a.Keys.Length);for(int i=0;i<a.Keys.Length;i++){var k=a.Keys[i];w.Write(k.Time);w.Write(k.Rotation);WeaponSaveContent.Write(w,k.Offset);}
                w.Write(a.Channels.Length);for(int i=0;i<a.Channels.Length;i++){w.Write(a.Channels[i].x);w.Write(a.Channels[i].y);}
                w.Write(a.Clips.Length);for(int i=0;i<a.Clips.Length;i++){w.Write(a.Clips[i].Duration);w.Write(a.Clips[i].Loop);}
                for(int i=0;i<4;i++){var d=rig.Attack((AttackKind)i);w.Write(d.Duration);w.Write(d.ActiveFrom);w.Write(d.ActiveTo);w.Write(d.Damage);w.Write(d.Knockback);w.Write(d.Bone);}
        }
        internal static void WriteVisuals(BinaryWriter w, SimSession session)
        {
            var world = session.World; var weapons = world.Resource(BwWeapons.Key); var slots = world.Resource(BwMobileSkills.Key);
                WeaponSaveContent.WriteVisuals(w, weapons, slots);
        }
        internal static void WriteRawCompatibility(BinaryWriter w, SimSession session)
        {
            var world = session.World;
            WeaponSaveContent.WriteRawCompatibility(w, world.Resource(BwWeapons.Key), world.Resource(BwMobileSkills.Key));
        }
        static SnapshotMemberSchema C(AccessKey key,string meaning)=>new SnapshotMemberSchema(key,meaning);
        static SnapshotResourceSchema R(AccessKey key,string meaning,SnapshotResourcePolicy policy,bool level=false)=>new SnapshotResourceSchema(key,meaning,policy,level);
        static SnapshotSystemSchema S(Type type,string id)=>new SnapshotSystemSchema(type,id);
    }
}
