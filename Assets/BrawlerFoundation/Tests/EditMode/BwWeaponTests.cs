using System;
using NUnit.Framework;
using SPF.Contracts;
using SPF.L2.Weapons;
using SPF.Testing;
using Unity.Collections;
using Unity.Mathematics;

namespace BrawlerFoundation.Tests
{
    public class BwWeaponTests
    {
        static BwTestWorld Create(float2 enemy,bool start=true,int history=64)
        {
            var config=BwBeltConfig.Default;config.TargetsPerAttack=history;var t=new BwTestWorld(start:start,belt:config,weapons:true);
            if(start){t.World.ClearLevel();BwSpawner.Spawn(t.World,0,0,1,0);BwSpawner.Spawn(t.World,1,enemy,-1,0);var f=t.Info(1);f.Hp=f.MaxHp=10000;t.World.Column(BwKeys.Info).Set(1,f);t.Game.Flow=BwFlow.Fighting;}
            return t;
        }
        static WeaponRuntime W(BwTestWorld t)=>t.World.Resource(BwWeapons.Key);
        static void Equip(BwTestWorld t,int id){W(t).RequestEquip(id);t.Step(W(t).Profile(id).EquipTicks);}
        [TestCase(WeaponProfiles.Blade,10000)]
        [TestCase(WeaponProfiles.Sword,9973)]
        public void ActualBeltWeaponProfilesChangeReachAndDamage(int weapon,float hp)
        {
            using var t=Create(new float2(2.25f,0));Equip(t,weapon);t.Press(BwButton.Punch);t.Step(W(t).Current.DurationTicks);Assert.AreEqual(hp,t.Info(1).Hp,.001f);
        }
        [TestCase(WeaponProfiles.Staff,24)]
        [TestCase(WeaponProfiles.Bow,36)]
        public void RangedWeaponReleasesOneTravelingShotThatDamagesDistantEnemy(int weapon,float damage)
        {
            using var t=Create(new float2(5,0));Equip(t,weapon);t.Press(BwButton.Punch);t.Step(W(t).Current.DurationTicks+30);Assert.AreEqual(1,W(t).Releases);Assert.AreEqual(10000-damage,t.Info(1).Hp,.001f);Assert.AreEqual(1,W(t).AcceptedHits);
        }
        [Test]
        public void DepthAndHeightPreventWeaponHitsOnAnotherLane()
        {
            using var t=Create(new float2(1.3f,1.3f));t.Press(BwButton.Punch);t.Step(35);Assert.AreEqual(10000,t.Info(1).Hp);
        }
        [Test]
        public void KeyboardSwitchCommandCancelsDrawAndDeathClearsPendingCommands()
        {
            using var t=Create(new float2(6,0));Equip(t,WeaponProfiles.Bow);t.Press(BwButton.Punch);t.Step(5);t.Press(BwWeapons.SwitchButton);t.Step(20);Assert.AreEqual(WeaponProfiles.Blade,W(t).Current.ContentId);Assert.AreEqual(0,W(t).Releases);
            t.Press(BwButton.Punch);var p=t.Info(0);p.Hp=0;p.State=FighterState.KO;t.World.Column(BwKeys.Info).Set(0,p);t.Step();Assert.IsFalse(W(t).Busy);Assert.AreEqual(0,W(t).ActiveProjectiles);Assert.IsFalse(W(t).Equipment.BufferedAttack.Pending);
            t.Game.Send(BwCommandKind.Start);t.Step();Assert.AreEqual(WeaponProfiles.Blade,W(t).Current.ContentId);Assert.AreEqual(0,W(t).Releases);
        }
        [Test]
        public void ActualAdapterSnapshotAtReleaseAndRowReorderDoNotRepeatHits()
        {
            using var a=Create(new float2(1.2f,0));a.Press(0);a.Step(W(a).Current.Active.From);Assert.AreEqual(9982,a.Info(1).Hp);
            var target=a.World.Table(BwKeys.Fighter).Handles[1];using(var order=new NativeArray<uint>(new uint[]{2,1},Allocator.Temp))a.World.SortRows(BwKeys.Fighter,order);
            var saved=a.Session.CaptureSnapshot();using var b=Create(0,start:false);b.Session.RestoreSnapshot(saved);a.Step(25);b.Step(25);CollectionAssert.AreEqual(a.Session.CaptureSnapshot(),b.Session.CaptureSnapshot());
            Assert.IsTrue(a.World.Registry.TryResolve(target,out _,out int row));Assert.AreEqual(9982,a.Info(row).Hp);
        }
        [Test]
        public void TwelveSwitchPressesCycleAllProfilesAndCancelUnreleasedBowDraws()
        {
            using var t=Create(new float2(8,0));
            for(int i=0;i<12;i++)
            {
                t.Game.Input=new InputFrame{Pressed=1u<<BwWeapons.SwitchButton};t.Step();t.Game.Input=default;
                int expected=WeaponProfiles.Blade+(i+1)%4;t.Step(W(t).Profile(expected).EquipTicks);Assert.AreEqual(expected,W(t).Current.ContentId);
                if(expected==WeaponProfiles.Bow){t.Press(BwButton.Punch);t.Step(3);}
            }
            Assert.AreEqual(0,W(t).Releases);Assert.AreEqual(WeaponProfiles.Blade,W(t).Current.ContentId);
            for(int i=0;i<12;i++){t.Game.Input=new InputFrame{Pressed=1u<<BwWeapons.SwitchButton};t.Step();}
            t.Game.Input=default;t.Step(20);Assert.AreEqual(WeaponProfiles.Blade,W(t).Current.ContentId);Assert.AreEqual(0,W(t).Equipment.PendingId);
        }
        [Test]
        public void TargetEnteringAfterContactCannotBeHitByVisualFollowThrough()
        {
            using var t=Create(new float2(5,0));t.Press(0);t.Step(W(t).Current.Active.From);
            t.World.Column(BwBeltKeys.Ground).Set(1,new float2(1.2f,0));t.World.Column(BwBeltKeys.PreviousGround).Set(1,new float2(1.2f,0));t.Step(8);
            Assert.AreEqual(10000,t.Info(1).Hp);Assert.AreEqual(0,W(t).AcceptedHits);
        }
        [Test]
        public void ActualContactQueryHonorsFixedStableHistoryCapacity()
        {
            using var t=Create(new float2(1.2f,-.1f),history:1);BwSpawner.Spawn(t.World,1,new float2(1.2f,.1f),-1,0);
            t.Press(0);t.Step(W(t).Current.Active.From);Assert.AreEqual(1,W(t).AcceptedHits);Assert.Greater(W(t).RejectedHits,0);
        }
        [Test]
        public void WarmWeaponBeltTicksRemainAllocationFreeWithCalibratedProbe()
        {
            using var t=Create(new float2(8,0));Equip(t,WeaponProfiles.Bow);t.Game.Input=new InputFrame{Held=1};t.Step(180);Action work=()=>t.Step(180);
            using var probe=new ManagedAllocationProbe();var pre=probe.Calibrate();var result=probe.Measure(work);var post=probe.Calibrate();
            TestContext.WriteLine($"Weapon belt: {result.Value} {result.Metric}; controls {pre.RetainedArrays.Value}/{pre.Empty.Value}, {post.RetainedArrays.Value}/{post.Empty.Value}; current-thread scope only.");Assert.AreEqual(0,result.Value);Assert.Greater(W(t).Releases,1);
        }
    }
}
