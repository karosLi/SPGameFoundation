using System;
using System.IO;
using NUnit.Framework;
using SPF.Contracts;
using SPF.Contracts.Weapons;
using SPF.L2.Weapons;
using SPF.Testing;
using Unity.Mathematics;

namespace SPF.Tests.EditMode
{
    public class WeaponRuntimeTests
    {
        static WeaponRuntime Create(int shots=4,int history=4,int cues=8)=>new WeaponRuntime(WeaponProfiles.CreateDefaults(60),60,shots,history,cues);
        static void Step(WeaponRuntime r,int count=1,bool held=false,bool pressed=false){for(int i=0;i<count;i++)r.Step(true,true,false,pressed&&i==0,held,new float2(1,0),float2.zero,0,1);}
        static void Equip(WeaponRuntime r,int id){Assert.IsTrue(r.RequestEquip(id));Step(r,r.Profile(id).EquipTicks);Assert.AreEqual(id,r.Equipment.EquippedId);}
        static byte[] Save(WeaponRuntime r){using var m=new MemoryStream();using(var w=new BinaryWriter(m,System.Text.Encoding.UTF8,true))r.WriteSnapshot(w);return m.ToArray();}
        static void Restore(WeaponRuntime r,byte[] bytes){using var m=new MemoryStream(bytes);using var reader=new BinaryReader(m);r.ReadSnapshot(reader);}

        [Test]
        public void ProfilesHaveStableContentKeysDistinctMechanicsAndRejectBadAuthoredWindows()
        {
            var p=WeaponProfiles.CreateDefaults(60);Assert.AreEqual(4,p.Length);Assert.AreEqual(WeaponProfiles.Blade,p[0].ContentId);Assert.IsFalse(p[0].Ranged);Assert.IsFalse(p[1].Ranged);Assert.IsTrue(p[2].Ranged);Assert.IsTrue(p[3].Ranged);Assert.Greater(p[1].Reach,p[0].Reach);Assert.Greater(p[3].ReleaseTick,p[2].ReleaseTick);
            p[0].Active.Until=p[0].DurationTicks+1;Assert.Throws<ArgumentException>(()=>new WeaponRuntime(p,60));
        }
        [Test]
        public void BowCanceledDuringDrawCannotReleaseAndRepeatedSwitchDuringEquipIsValid()
        {
            using var r=Create();Equip(r,WeaponProfiles.Bow);Step(r,5,held:true);Assert.AreEqual(WeaponStage.Windup,r.View().Stage);
            r.RequestEquip(WeaponProfiles.Staff);Step(r);Assert.IsFalse(r.Equipment.Timeline.Running);Assert.AreEqual(0,r.Releases);
            r.RequestEquip(WeaponProfiles.Sword);Step(r,r.Profile(WeaponProfiles.Sword).EquipTicks);Assert.AreEqual(WeaponProfiles.Sword,r.Current.ContentId);Assert.AreEqual(0,r.ActiveProjectiles);
            r.RequestEquip(WeaponProfiles.Blade);Step(r);r.RequestEquip(WeaponProfiles.Sword);Step(r);Assert.AreEqual(0,r.Equipment.EquipRemaining);Assert.AreEqual(WeaponProfiles.Sword,r.Current.ContentId);
        }
        [Test]
        public void SwitchDuringContactWaitsUntilRecoveryAndSuppressesBufferedAttack()
        {
            using var r=Create();Step(r,1,pressed:true);Step(r,r.Current.Active.From);Assert.IsTrue(r.MeleeActive);
            uint pulse=r.Equipment.Timeline.PulseId;r.RequestEquip(WeaponProfiles.Bow);Step(r,2,held:true);Assert.AreEqual(WeaponProfiles.Blade,r.Current.ContentId);Assert.IsTrue(r.Equipment.Timeline.Running);
            Step(r,r.Current.DurationTicks);Assert.AreEqual(WeaponProfiles.Bow,r.Current.ContentId);Assert.AreEqual(pulse,r.Equipment.Timeline.PulseId);
        }
        [Test]
        public void EachRangedActionReleasesOnceAtCanonicalMuzzleAndFullPoolNeverRetries()
        {
            using var r=Create(shots:1);Equip(r,WeaponProfiles.Staff);Step(r,1,pressed:true);Step(r,r.Current.ReleaseTick);
            Assert.AreEqual(1,r.Releases);Assert.AreEqual(1,r.ActiveProjectiles);Assert.AreEqual(r.Current.MuzzleOffset.x,r.Projectiles[0].Previous.x,.0001f);Assert.AreEqual(r.Current.MuzzleOffset.y,r.Projectiles[0].Height);
            Step(r,r.Current.DurationTicks);Step(r,1,pressed:true);Step(r,r.Current.ReleaseTick);Assert.AreEqual(2,r.Releases);Assert.AreEqual(1,r.RejectedProjectiles);
            r.StopProjectile(0);Step(r,10);Assert.AreEqual(0,r.ActiveProjectiles);Assert.AreEqual(2,r.Releases,"capacity rejection is terminal for this action");
        }
        [Test]
        public void PauseIsBitExactAndDeadOwnerClearsInputsPendingEquipmentHistoryAndProjectiles()
        {
            using var r=Create();Equip(r,WeaponProfiles.Bow);Step(r,10,held:true);r.RequestEquip(WeaponProfiles.Staff);var before=Save(r);
            for(int i=0;i<100;i++)r.Step(false,true,false,true,true,0,0,0,1);CollectionAssert.AreEqual(before,Save(r));
            r.Step(true,false,false,true,true,0,0,0,1);Assert.IsFalse(r.Busy);Assert.AreEqual(0,r.Equipment.PendingId);Assert.IsFalse(r.Equipment.BufferedAttack.Pending);Assert.AreEqual(0,r.ActiveProjectiles);Assert.AreEqual(0,r.CueCount);
            r.OnReset();Assert.AreEqual(WeaponProfiles.Blade,r.Current.ContentId);Assert.AreEqual(0,r.Tick);
        }
        [Test]
        public void SnapshotBeforeAndAfterReleaseResumesWithoutDuplicateShotAndRejectsContentChanges()
        {
            using var a=Create();Equip(a,WeaponProfiles.Bow);Step(a,1,pressed:true);Step(a,a.Current.ReleaseTick-1);var before=Save(a);
            using var b=Create();Restore(b,before);Step(a,3);Step(b,3);CollectionAssert.AreEqual(Save(a),Save(b));Assert.AreEqual(1,b.Releases);
            var released=Save(a);Restore(b,released);Assert.AreEqual(0,b.CueCount,"old effects are not replayed by restore");Step(a,20);Step(b,20);CollectionAssert.AreEqual(Save(a),Save(b));Assert.AreEqual(1,b.Releases);
            var profiles=WeaponProfiles.CreateDefaults(60);profiles[3].Damage++;using var changed=new WeaponRuntime(profiles,60,4,4,8);Assert.Throws<InvalidDataException>(()=>Restore(changed,released));
        }
        [Test]
        public void StableTargetGenerationAndBoundedHistoryProtectAgainstRepeatedDamage()
        {
            using var r=Create(history:1);Step(r,1,pressed:true);var target=new EntityHandle(7,1);Assert.IsTrue(r.RecordHit(0,target,0));Assert.IsFalse(r.RecordHit(0,target,0));
            Assert.AreEqual(SPF.L2.Combat.HitRecordResult.Full,r.CheckHit(0,new EntityHandle(7,2)));Assert.AreEqual(1,r.AcceptedHits);
            Step(r,r.Current.DurationTicks);Step(r,1,pressed:true);Assert.IsTrue(r.RecordHit(0,new EntityHandle(7,2),0));
        }
        [Test]
        public void SkillPoseClockRejectsDurationsWithoutAVisibleInterpolatedInterval()
        {
            var pose=new SPF.L2.Skills.ActionPoseClock();Assert.Throws<ArgumentOutOfRangeException>(()=>pose.Begin(101,1));Assert.Throws<ArgumentOutOfRangeException>(()=>pose.Begin(101,2));
            pose.Begin(101,3);pose.Advance(true,true);pose.Advance(true,true);Assert.IsTrue(pose.Running);Assert.Greater(pose.Phase(0),0);Assert.Less(pose.Phase(0),1);
        }
        [Test]
        public void OneTickProjectileLifetimeStillTravelsOneFullIntervalBeforeExpiry()
        {
            var profiles=WeaponProfiles.CreateDefaults(60);profiles[2].ProjectileLifeTicks=1;
            using var r=new WeaponRuntime(profiles,60,4,4,8);Equip(r,WeaponProfiles.Staff);Step(r,1,pressed:true);Step(r,r.Current.ReleaseTick);
            Assert.AreEqual(1,r.Projectiles[0].RemainingTicks);float muzzle=r.Projectiles[0].Position.x;r.ExpireProjectiles();Assert.AreEqual(1,r.ActiveProjectiles);
            Step(r);Assert.AreEqual(muzzle+r.Current.ProjectileSpeed/60f,r.Projectiles[0].Position.x,.0001f);Assert.AreEqual(0,r.Projectiles[0].RemainingTicks);
            r.ExpireProjectiles();Assert.AreEqual(0,r.ActiveProjectiles);
        }
        [TestCase(0f,false)]
        [TestCase(.5f,false)]
        [TestCase(1f,true)]
        public void InterpolatedBowReleaseNeverShowsHeldAndFlyingArrowTogether(float alpha,bool visible)
        {
            using var r=Create();Equip(r,WeaponProfiles.Bow);Step(r,1,pressed:true);Step(r,r.Current.ReleaseTick);
            Assert.AreEqual(1,r.Releases);Assert.AreEqual(new float2(r.Current.MuzzleOffset.x,0),r.Projectiles[0].Position);
            var view=r.View(alpha);bool nocked=view.Phase<view.ReleasePhase;
            Assert.AreEqual(visible,r.ProjectileVisible(0,alpha));Assert.IsFalse(nocked&&r.ProjectileVisible(0,alpha));
            Step(r);Assert.IsTrue(r.ProjectileVisible(0,0));Assert.AreEqual(r.Current.MuzzleOffset.x,r.Projectiles[0].Previous.x,.0001f);
        }
        [Test]
        public void SameTickRestoreAndResetInvalidateReadOnlyPresentationRevision()
        {
            using var r=Create();Step(r,1,pressed:true);var saved=Save(r);long tick=r.Tick;uint before=r.Revision;
            Restore(r,saved);Assert.AreEqual(tick,r.Tick);Assert.AreNotEqual(before,r.Revision);Assert.AreEqual(0,r.CueCount);CollectionAssert.AreEqual(saved,Save(r));
            before=r.Revision;r.OnReset();Assert.AreNotEqual(before,r.Revision);
        }
        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        public void RestoreRejectsInvalidProjectileGeometryOrDetachedHitScope(int corruption)
        {
            using var source=Create();Equip(source,WeaponProfiles.Staff);Step(source,1,pressed:true);Step(source,source.Current.ReleaseTick);
            var shot=source.Projectiles[0];var scope=source.Scopes[1];
            if(corruption==0)shot.Scale=float.NaN;
            if(corruption==1)shot.Direction=float2.zero;
            if(corruption==2)scope.Owner=new EntityHandle(99,2);
            if(corruption==3)shot.Active=false;
            source.Projectiles[0]=shot;source.Scopes[1]=scope;
            using var restored=Create();Assert.Throws<InvalidDataException>(()=>Restore(restored,Save(source)));
        }
        [Test]
        public void RestoreRejectsContactHistoryDetachedFromActionPulse()
        {
            using var source=Create();Step(source,1,pressed:true);var scope=source.Scopes[0];scope.Pulse++;source.Scopes[0]=scope;
            using var restored=Create();Assert.Throws<InvalidDataException>(()=>Restore(restored,Save(source)));
        }
        [Test]
        public void HeldChainsRetargetAtNewActionWithoutChangingAnInflightAim()
        {
            using var r=Create();Equip(r,WeaponProfiles.Staff);Step(r,1,held:true);
            for(int i=0;i<r.Current.DurationTicks-1;i++)r.Step(true,true,false,false,true,new float2(-1,0),0,0,1);
            Assert.AreEqual(new float2(1,0),r.Equipment.Aim);uint pulse=r.Equipment.Timeline.PulseId;
            r.Step(true,true,false,false,true,new float2(-1,0),0,0,1);Assert.AreEqual(pulse+1,r.Equipment.Timeline.PulseId);Assert.AreEqual(new float2(-1,0),r.Equipment.Aim);
        }
        [Test]
        public void RetainedCueRingIsBoundedSequencedAndCannotChangeCombat()
        {
            using var a=Create(cues:1);using var b=Create(cues:32);Step(a,500,held:true);Step(b,500,held:true);
            Assert.AreEqual(a.Equipment.Timeline.Tick,b.Equipment.Timeline.Tick);Assert.AreEqual(a.Equipment.Timeline.PulseId,b.Equipment.Timeline.PulseId);Assert.AreEqual(1,a.CueCount);Assert.Greater(a.RejectedCues,0);Assert.AreEqual(a.Equipment.CueSequence,a.Cues[0].Sequence);
        }
    }
}
