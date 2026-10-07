using System;
using System.IO;
using SurvivorFoundation.Systems;
using SPF.Contracts;
using SPF.Runtime.Session;
using SPF.Runtime.World;

namespace SurvivorFoundation
{
    /// <summary>Optional envelope for Classic/Guard weapon sessions. Flying-sword/crossed-blade composites
    /// deliberately require their own complete schema. No default save format or gameplay rule changes.</summary>
    public static class SvWeaponSave
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
            if(settings.FlyingSwords.Enabled||settings.CrossedBlades.Enabled||settings.Variant==SvVariant.FlyingSwordHorde)
                throw new NotSupportedException("This envelope adapter covers Classic/Guard weapons only.");
            if(settings.Variant!=SvVariant.Classic&&settings.Variant!=SvVariant.GuardBeacon)throw new InvalidDataException("Unknown Survivor variant.");
            if(session.Clock.TickRate!=weapons.TickRate)throw new InvalidDataException("Weapon tick rate differs from session.");
            byte[] schema=SaveCompatibilityDescriptor.Encode(w=>{
                w.Write("module.survivor.weapon-combat");w.Write(1);w.Write("raw-session.clock-world-pipeline");w.Write(1);
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
                    R(SvMobileSkills.Key,"SkillSlots",SnapshotResourcePolicy.Snapshot,true)
                });
                SaveCompatibilityDescriptor.WriteSystemSchema(w,session.Pipeline,
                    S(typeof(FlowSystem),"survivor.flow"),S(typeof(RewardSystem),"survivor.reward"),S(typeof(SpawnSystem),"survivor.spawn"),
                    S(typeof(MobileSkillInputSystem),"survivor.skill-input"),S(typeof(HeroSystem),"survivor.hero"),S(typeof(EnemySystem),"survivor.enemy"),
                    S(typeof(BulletSystem),"survivor.bullet"),S(typeof(EnemyGridSystem),"survivor.grid"),S(typeof(CollideSystem),"survivor.collide"),
                    S(typeof(AnnularSkillSystem),"survivor.annular"),S(typeof(MobileSkillPulseSystem),"survivor.skill-pulse"),
                    S(typeof(WeaponCombatSystem),"survivor.weapon"),S(typeof(ResolveSystem),"survivor.resolve"));
            });
            byte[] content=SaveCompatibilityDescriptor.Encode(w=>{
                WeaponSaveContent.WriteSession(w,session);w.Write("survivor.rules");w.Write(1);WriteSettings(w,settings);w.Write(runtime.MobileSkills);
                w.Write(SvWeapons.ActorScale);WeaponSaveContent.WriteRules(w,weapons,slots);w.Write(runtime.Enemies.Length);w.Write(runtime.EnemyKinds);
                for(int i=0;i<runtime.Enemies.Length;i++){
                    var e=runtime.Enemies[i];w.Write(e.Radius);w.Write(e.Speed);w.Write(e.Hp);w.Write(e.Damage);w.Write(e.Xp);w.Write(e.SpawnFrom);w.Write(e.Weight);w.Write(e.Shooter);w.Write(e.KeepDistance);
                    var p=e.Pattern;w.Write(p.Arms);w.Write(p.Spread);w.Write(p.PerArm);w.Write(p.FanAngle);w.Write(p.Speed);w.Write(p.SpeedStep);w.Write(p.Interval);w.Write(p.Spin);w.Write(p.Aimed);
                }
                var grid=world.Resource(SvKeys.EnemyGrid);w.Write(grid.Dimensions.x);w.Write(grid.Dimensions.y);w.Write(grid.CellSize);w.Write(grid.Capacity);w.Write(grid.LargeRadius);w.Write(grid.LargeCellScale);
                w.Write(world.Resource(SvKeys.Hits).Capacity);w.Write(world.Resource(SvKeys.Deaths).Capacity);w.Write(world.Resource(SvKeys.BulletSpawns).Capacity);
                w.Write(world.Resource(SvKeys.HeroDamage).Capacity);w.Write(world.Resource(SvKeys.Collected).Capacity);w.Write(world.Resource(SvKeys.Feedback).Capacity);
            });
            byte[] visual=SaveCompatibilityDescriptor.Encode(w=>{
                WeaponSaveContent.WriteVisuals(w,weapons,slots);w.Write(runtime.Enemies.Length);
                for(int i=0;i<runtime.Enemies.Length;i++){var c=runtime.Enemies[i].Color;w.Write(c.x);w.Write(c.y);w.Write(c.z);w.Write(c.w);}
                w.Write(runtime.EnemyNames.Length);foreach(var name in runtime.EnemyNames)w.Write(name??"");
            });
            return new SaveCompatibilityDescriptor("survivor.weapon-combat","spf.weapon-save-contract.v1",runtimeId,schema,content,visual,
                SaveCompatibilityDescriptor.Encode(w=>WeaponSaveContent.WriteRawCompatibility(w,weapons,slots)));
        }
        static void WriteSettings(BinaryWriter w,SvSettings s)
        {
            w.Write(s.ArenaHalf);w.Write(s.HeroSpeed);w.Write(s.HeroHp);w.Write(s.HeroRadius);w.Write(s.HurtInvulnerable);
            w.Write(s.SpawnRadius);w.Write(s.SpawnPerSecond);w.Write(s.SpawnGrowth);w.Write(s.EliteEvery);w.Write(s.MaxEnemies);
            w.Write(s.PickupRadius);w.Write(s.MagnetRadius);w.Write(s.GemSpeed);w.Write(s.XpBase);w.Write(s.XpPerLevel);
            w.Write(s.BoltCooldown);w.Write(s.BoltDamage);w.Write(s.BoltSpeed);w.Write(s.NovaCooldown);w.Write(s.NovaDamage);
            w.Write(s.SpiralInterval);w.Write(s.SpiralDamage);w.Write(s.OrbitRadius);w.Write(s.OrbitDps);w.Write(s.EnemyBulletDamage);
            w.Write((byte)s.Variant);WeaponSaveContent.Write(w,s.BeaconPosition);w.Write(s.BeaconHp);w.Write(s.BeaconRadius);w.Write(s.GuardAggroRadius);
            w.Write(s.GuardDurationTicks);w.Write(s.BeaconHurtCooldownTicks);
            var a=s.AnnularSkill;w.Write(a.Enabled);w.Write(a.RadiusA);w.Write(a.RadiusB);w.Write(a.HalfWidth);w.Write(a.DamagePerSecond);w.Write(a.TickInterval);
            var c=s.CrossedBlades;w.Write(c.Enabled);w.Write(c.TickInterval);w.Write(c.MaxTargets);w.Write(c.Reach);w.Write(c.HalfWidth);w.Write(c.DamagePerPulse);
            // Disabled opt-in values are still part of authoring identity; enabled composites are rejected above.
            var f=s.FlyingSwords;w.Write(f.Enabled);w.Write(f.Capacity);w.Write(f.BaseCount);w.Write(f.HistoryPerSword);w.Write(f.OrbitTicks);w.Write(f.OutboundTicks);w.Write(f.ReturnTicks);w.Write(f.WaveTicks);
            w.Write(f.OrbitRadius);w.Write(f.OrbitRadiansPerSecond);w.Write(f.Speed);w.Write(f.ReturnSpeed);w.Write(f.TurnRate);w.Write(f.TargetRange);w.Write(f.Radius);w.Write(f.Damage);
            w.Write(s.ReorderInterval);w.Write(s.GridCell);
        }
        static SnapshotMemberSchema C(AccessKey key,string meaning)=>new SnapshotMemberSchema(key,meaning);
        static SnapshotResourceSchema R(AccessKey key,string meaning,SnapshotResourcePolicy policy,bool level=false)=>new SnapshotResourceSchema(key,meaning,policy,level);
        static SnapshotSystemSchema S(Type type,string id)=>new SnapshotSystemSchema(type,id);
    }
}
