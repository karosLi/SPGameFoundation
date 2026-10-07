using System;
using NUnit.Framework;
using SPF.Contracts;
using SPF.L2.Weapons;
using SPF.Testing;
using Unity.Collections;
using Unity.Mathematics;

namespace SurvivorFoundation.Tests
{
    public class SvWeaponTests
    {
        static void Configure(SvConfig c)
        {
            c.WeaponCombat=c.MobileSkills=true;c.WeaponProfiles=WeaponProfiles.CreateDefaults(30);c.Capacity.Enemies=256;c.Capacity.Events=512;c.Settings.SpawnPerSecond=c.Settings.SpawnGrowth=c.Settings.EliteEvery=0;c.Settings.XpBase=100000;
            foreach(var e in c.Enemies){e.Speed=0;e.Hp=10000;e.Damage=0;e.Shooter=false;}
        }
        static SvTestWorld Create(bool start=true)=>new SvTestWorld(start:start,tweak:Configure);
        static WeaponRuntime W(SvTestWorld t)=>t.World.Resource(SvWeapons.Key);
        static void Equip(SvTestWorld t,int id){W(t).RequestEquip(id);t.Step(W(t).Profile(id).EquipTicks);}
        [TestCase(WeaponProfiles.Blade,18)]
        [TestCase(WeaponProfiles.Sword,27)]
        [TestCase(WeaponProfiles.Staff,24)]
        [TestCase(WeaponProfiles.Bow,36)]
        public void EveryEquippedProfileChangesActualHordeDamage(int id,float damage)
        {
            using var t=Create();Equip(t,id);t.Spawn(1,new float2(id>=WeaponProfiles.Staff?5:1,0));t.Step(id>=WeaponProfiles.Staff?W(t).Current.ReleaseTick+18:W(t).Current.Active.Until+2);
            Assert.AreEqual(10000-damage,t.World.Column(SvKeys.Info)[0].Hp,.001f);Assert.AreEqual(1,W(t).AcceptedHits);Assert.AreEqual(id>=WeaponProfiles.Staff?1:0,W(t).Releases);Assert.AreEqual(0,t.Bullets,"classic auto bolt is replaced only in weapon mode");
        }
        [Test]
        public void PortraitSkillsStillWorkAlongsideWeaponSwitchCommand()
        {
            using var t=Create();t.Spawn(1,new float2(4,0));t.Game.Input=new InputFrame{Pressed=(1u<<SvWeapons.SwitchButton)|3u,Aim=new float2(1,0)};t.Step();Assert.AreEqual(3,t.Game.Hero.x,.001f);Assert.AreEqual(1,t.World.Resource(SvMobileSkills.Key).GetSnapshot(0).Charges);Assert.AreEqual(1,t.World.Resource(SvMobileSkills.Key).GetSnapshot(1).Charges);
            t.Game.Input=default;t.Step(12);Assert.AreEqual(WeaponProfiles.Sword,W(t).Current.ContentId);Assert.AreEqual(4,t.World.Resource(SvMobileSkills.Key).Count);
        }
        [Test]
        public void TwelvePortraitSwitchCommandsUseSameAuthoritativeSkillGate()
        {
            using var t=Create();
            for(int i=0;i<12;i++)
            {
                t.Game.Input=new InputFrame{Pressed=1u<<SvWeapons.SwitchButton};t.Step();t.Game.Input=default;
                int expected=WeaponProfiles.Blade+(i+1)%4;t.Step(W(t).Profile(expected).EquipTicks);Assert.AreEqual(expected,W(t).Current.ContentId);
            }
            Assert.AreEqual(WeaponProfiles.Blade,W(t).Current.ContentId);Assert.AreEqual(0,W(t).Releases);Assert.AreEqual(0,W(t).ActiveProjectiles);
        }
        [Test]
        public void LevelUpPausesWeaponDrawAndDropsPausedSwitchBeforeResume()
        {
            using var t=Create();Equip(t,WeaponProfiles.Bow);t.Spawn(1,new float2(5,0));t.Step(6);int tick=W(t).Equipment.Timeline.Tick;long clock=W(t).Tick;
            t.Game.Flow=SvFlow.LevelUp;t.Game.Input=new InputFrame{Pressed=1u<<SvWeapons.SwitchButton};t.Step(40);Assert.AreEqual(tick,W(t).Equipment.Timeline.Tick);Assert.AreEqual(clock,W(t).Tick);Assert.AreEqual(0u,t.Game.Input.Pressed);
            t.Game.Flow=SvFlow.Playing;t.Step(20);Assert.AreEqual(WeaponProfiles.Bow,W(t).Current.ContentId);Assert.AreEqual(1,W(t).Releases);
        }
        [Test]
        public void RestoreOfInFlightArrowMatchesUninterruptedActualDamageAndResetClearsPool()
        {
            using var a=Create();Equip(a,WeaponProfiles.Bow);a.Spawn(1,new float2(10,0));a.Step(W(a).Current.ReleaseTick+3);Assert.AreEqual(1,W(a).ActiveProjectiles);var saved=a.Session.CaptureSnapshot();using var b=Create(false);b.Session.RestoreSnapshot(saved);a.Step(20);b.Step(20);CollectionAssert.AreEqual(a.Session.CaptureSnapshot(),b.Session.CaptureSnapshot());Assert.AreEqual(1,W(b).AcceptedHits);
            b.Game.Send(SvCommandKind.Start);b.Step();Assert.AreEqual(0,W(b).ActiveProjectiles);Assert.AreEqual(WeaponProfiles.Blade,W(b).Current.ContentId);
        }
        [Test]
        public void FullHordeDamageQueueRejectsWithoutRecordingOrDuplicatingRelease()
        {
            using var t=Create();Equip(t,WeaponProfiles.Staff);t.Spawn(1,new float2(1.1f,0));t.Step();t.Step(W(t).Current.ReleaseTick);
            var hits=t.World.Resource(SvKeys.Hits);for(int i=0;i<hits.Capacity;i++)Assert.IsTrue(hits.TryAdd(new SvHit{Target=0,Damage=0}));t.Step();
            Assert.AreEqual(0,W(t).AcceptedHits);Assert.AreEqual(1,W(t).Releases);Assert.AreEqual(0,W(t).ActiveProjectiles);Assert.AreEqual(10000,t.World.Column(SvKeys.Info)[0].Hp);Assert.Greater(W(t).RejectedHits,0);
        }
        [Test]
        public void DenseWarmedAdapterWorkloadKeepsBoundedPoolAndCalibratedAllocationThreshold()
        {
            using var t=Create();for(int i=0;i<128;i++){float angle=i*2.399963f;t.Spawn(1,new float2(math.cos(angle),math.sin(angle))*(3+math.sqrt(i)*.5f));}Equip(t,WeaponProfiles.Staff);t.Step(90);
            Action work=()=>t.Step(180);using var probe=new ManagedAllocationProbe();var pre=probe.Calibrate();var sample=probe.Measure(work);var post=probe.Calibrate();
            TestContext.WriteLine($"Weapon horde128: {sample.Value} {sample.Metric}; calibration {pre.RetainedArrays.Value}/{pre.Empty.Value}, {post.RetainedArrays.Value}/{post.Empty.Value}; current-thread only.");Assert.AreEqual(0,sample.Value);Assert.LessOrEqual(W(t).ActiveProjectiles,32);Assert.Greater(W(t).AcceptedHits,0);
        }
    }
}
