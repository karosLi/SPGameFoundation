using System;
using System.IO;
using NUnit.Framework;
using SPF.Contracts;
using SPF.L2.Weapons;
using SPF.Presentation;
using SPF.Runtime.Composition;
using SPF.Runtime.Session;
using SPF.Testing;
using Unity.Mathematics;
using UnityEngine;

namespace BrawlerFoundation.Tests
{
    public class BwWeaponSaveTests
    {
        const string Runtime = "test.brawler.same-runtime.v1";
        [Test] public void ActualWeaponBeltEnvelopeContinuesAndInvalidatesSameTickViews()
        {
            using var a=new BwTestWorld(belt:BwBeltConfig.Default,weapons:true);
            using var b=new BwTestWorld(start:false,belt:BwBeltConfig.Default,weapons:true);
            var weapon=a.World.Resource(BwWeapons.Key);weapon.RequestEquip(WeaponProfiles.Bow);a.Step(20);
            a.Press(BwButton.Punch);a.Step(weapon.Current.ReleaseTick+2);
            byte[] raw=a.Session.CaptureSnapshot();byte[] bytes=BwWeaponSave.Capture(a.Session,Runtime);
            var cursor=new MonotonicInterpolation();uint revision=a.Session.TimelineRevision;uint weaponRevision=weapon.Revision;
            uint tick=a.Session.Clock.NextTickIndex;cursor.Resolve(tick,revision,.8f);
            using(var stream=new MemoryStream(bytes))BwWeaponSave.Restore(stream,a.Session,Runtime);
            Assert.AreEqual(tick,a.Session.Clock.NextTickIndex);Assert.AreNotEqual(revision,a.Session.TimelineRevision);Assert.AreNotEqual(weaponRevision,weapon.Revision);
            Assert.AreEqual(0,cursor.Resolve(tick,a.Session.TimelineRevision,a.Session.InterpolationAlpha));CollectionAssert.AreEqual(raw,a.Session.CaptureSnapshot());
            using(var stream=new MemoryStream(bytes))BwWeaponSave.Restore(stream,b.Session,Runtime);
            for(int i=0;i<80;i++){a.Game.Input=b.Game.Input=new InputFrame{Move=new float2(.1f,0),Held=1};a.Step();b.Step();}
            CollectionAssert.AreEqual(a.Session.CaptureSnapshot(),b.Session.CaptureSnapshot());
        }
        [Test] public void BeltConfigAndLiveAuthoredSkeletonChangesRejectWithoutRestart()
        {
            using var a=new BwTestWorld(belt:BwBeltConfig.Default,weapons:true);byte[] bytes=BwWeaponSave.Capture(a.Session,Runtime);
            var changed=BwBeltConfig.Default;changed.Waves++;
            using var b=new BwTestWorld(belt:changed,weapons:true);Reject(bytes,b);
            var asset=a.World.Resource(BwKeys.Rig).Asset;var bone=asset.Bones[0];bone.Position.x+=.125f;asset.Bones[0]=bone;Reject(bytes,a);
            byte[] changedRig=BwWeaponSave.Capture(a.Session,Runtime);a.World.Resource(BwBeltKeys.State).Grid.Origin+=new float2(.5f,0);Reject(changedRig,a);
        }
        [Test] public void ExistingRawVisualGateRemainsDistinctFromAuthoritativeContent()
        {
            var p=WeaponProfiles.CreateDefaults(60);var q=(WeaponProfile[])p.Clone();q[0].VisualId+=100;
            var left=Create(p,out var lm,out var lmode);var right=Create(q,out var rm,out var rmode);
            try{
                var a=BwWeaponSave.Describe(left,Runtime);var b=BwWeaponSave.Describe(right,Runtime);
                Assert.AreEqual(a.ContentFingerprint,b.ContentFingerprint);Assert.AreNotEqual(a.VisualFingerprint,b.VisualFingerprint);Assert.AreNotEqual(a.RawCompatibilityFingerprint,b.RawCompatibilityFingerprint);
                byte[] raw=left.CaptureSnapshot();byte[] bytes=BwWeaponSave.Capture(left,Runtime);uint revision=right.TimelineRevision;
                using(var stream=new MemoryStream(bytes))Assert.Throws<InvalidDataException>(()=>BwWeaponSave.Restore(stream,right,Runtime));Assert.AreEqual(revision,right.TimelineRevision);
                p[0].Damage+=100;Assert.AreEqual(a.ContentFingerprint,BwWeaponSave.Describe(left,Runtime).ContentFingerprint,"WeaponRuntime already cloned authoring profiles");
                using(var stream=new MemoryStream(raw))Assert.Throws<InvalidDataException>(()=>right.ReadSnapshot(new BinaryReader(stream)));
            }finally{left.Dispose();right.Dispose();UnityEngine.Object.DestroyImmediate(lmode);UnityEngine.Object.DestroyImmediate(rmode);UnityEngine.Object.DestroyImmediate(lm);UnityEngine.Object.DestroyImmediate(rm);}
        }
        [Test] public void OptInAdapterRefusesClassicBrawler()
        {using var classic=new BwTestWorld();Assert.Throws<ArgumentException>(()=>BwWeaponSave.Describe(classic.Session,Runtime));}
        static SimSession Create(WeaponProfile[] profiles,out BwModule module,out ModeDefinition mode)
        {module=BwModule.CreateWeaponBelt(BwBeltConfig.Default,profiles);var settings=SessionSettings.Default;settings.TickRate=60;settings.MaxTicksPerFrame=4;mode=ModeDefinition.Create(new[]{module},settings);return SimSession.Create(mode,1);}
        static void Reject(byte[] bytes,BwTestWorld target)
        {byte[] raw=target.Session.CaptureSnapshot();uint revision=target.Session.TimelineRevision;target.Session.RequestTicks(3);int pending=target.Session.PendingTicks;using(var stream=new MemoryStream(bytes))Assert.Throws<InvalidDataException>(()=>BwWeaponSave.Restore(stream,target.Session,Runtime));Assert.AreEqual(revision,target.Session.TimelineRevision);Assert.AreEqual(pending,target.Session.PendingTicks);CollectionAssert.AreEqual(raw,target.Session.CaptureSnapshot());}
    }
}
