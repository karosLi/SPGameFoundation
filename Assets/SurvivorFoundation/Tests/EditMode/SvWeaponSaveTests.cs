using System;
using System.IO;
using System.Reflection;
using System.Collections.Generic;
using NUnit.Framework;
using SPF.Contracts;
using SPF.L2.Weapons;
using SPF.Runtime.Composition;
using SPF.Runtime.Session;
using Unity.Mathematics;

namespace SurvivorFoundation.Tests
{
    public class SvWeaponSaveTests
    {
        const string Runtime="test.survivor.same-runtime.v1";
        static void Configure(SvConfig c)
        {
            c.MobileSkills=c.WeaponCombat=true;c.WeaponProfiles=WeaponProfiles.CreateDefaults(30);
            c.Capacity.Enemies=32;c.Capacity.Bullets=64;c.Capacity.Gems=32;c.Capacity.Events=128;
            c.Settings.SpawnPerSecond=c.Settings.SpawnGrowth=c.Settings.EliteEvery=0;c.Settings.XpBase=100000;
            foreach(var e in c.Enemies){e.Speed=0;e.Hp=10000;e.Damage=0;e.Shooter=false;}
        }
        static SvTestWorld Create(bool start=true)=>new SvTestWorld(start:start,tweak:Configure);
        [Test] public void ActualWeaponHordeEnvelopeRestoresArrowAndContinuesExactDamage()
        {
            using var a=Create();using var b=Create(false);var weapons=a.World.Resource(SvWeapons.Key);
            weapons.RequestEquip(WeaponProfiles.Bow);a.Step(12);a.Spawn(1,new float2(5,0));a.Step(weapons.Current.ReleaseTick+2);
            byte[] raw=a.Session.CaptureSnapshot();byte[] bytes=SvWeaponSave.Capture(a.Session,Runtime);
            using(var s=new MemoryStream(bytes))SvWeaponSave.Restore(s,b.Session,Runtime);
            uint revision=b.Session.TimelineRevision;var restoredWeapons=b.World.Resource(SvWeapons.Key);uint weaponRevision=restoredWeapons.Revision;uint tick=b.Session.Clock.NextTickIndex;
            using(var s=new MemoryStream(bytes))SvWeaponSave.Restore(s,b.Session,Runtime);
            Assert.AreNotEqual(revision,b.Session.TimelineRevision);Assert.AreNotEqual(weaponRevision,restoredWeapons.Revision);Assert.AreEqual(tick,b.Session.Clock.NextTickIndex);CollectionAssert.AreEqual(raw,b.Session.CaptureSnapshot());
            // a is never restored: compare the resumed result to uninterrupted gameplay.
            CollectionAssert.AreEqual(raw,a.Session.CaptureSnapshot());a.Step(50);b.Step(50);CollectionAssert.AreEqual(a.Session.CaptureSnapshot(),b.Session.CaptureSnapshot());Assert.Greater(a.World.Resource(SvWeapons.Key).AcceptedHits,0);
        }
        [Test] public void AuthoritativeSettingsAndSameIdEnemyRulesRejectBeforeMutation()
        {
            using var a=Create();byte[] bytes=SvWeaponSave.Capture(a.Session,Runtime);a.Runtime.Settings.HeroHp+=1;Reject(bytes,a);
            a.Runtime.Settings.HeroHp-=1;var enemy=a.Runtime.Enemies[0];enemy.Damage+=1;a.Runtime.Enemies[0]=enemy;Reject(bytes,a);
        }
        [Test] public void EverySvSettingsLeafHasAnExplicitContentDependency()
        {
            using var a=Create();string baseline=SvWeaponSave.Describe(a.Session,Runtime).ContentFingerprint;
            // Reflection is a test-only omission alarm, never the production compatibility identity.
            foreach(var path in Leaves(typeof(SvSettings))){
                var before=a.Runtime.Settings;a.Runtime.Settings=(SvSettings)Change(before,path,0);
                try{
                    if((path[0].Name=="FlyingSwords"||path[0].Name=="CrossedBlades")&&path[1].Name=="Enabled")
                        Assert.Throws<NotSupportedException>(()=>SvWeaponSave.Describe(a.Session,Runtime));
                    else Assert.AreNotEqual(baseline,SvWeaponSave.Describe(a.Session,Runtime).ContentFingerprint,PathName(path));
                }finally{a.Runtime.Settings=before;}
            }
        }
        [Test] public void EveryAuthoritativeEnemyDefinitionLeafHasAnExplicitContentDependency()
        {
            using var a=Create();string baseline=SvWeaponSave.Describe(a.Session,Runtime).ContentFingerprint;
            foreach(var path in Leaves(typeof(EnemyDef))){
                if(path[0].Name=="Color")continue;
                var before=a.Runtime.Enemies[0];a.Runtime.Enemies[0]=(EnemyDef)Change(before,path,0);
                try{Assert.AreNotEqual(baseline,SvWeaponSave.Describe(a.Session,Runtime).ContentFingerprint,PathName(path));}
                finally{a.Runtime.Enemies[0]=before;}
            }
        }
        static IEnumerable<FieldInfo[]> Leaves(Type type)
        {
            foreach(var f in type.GetFields(BindingFlags.Public|BindingFlags.Instance)){
                if(f.FieldType.IsPrimitive||f.FieldType.IsEnum)yield return new[]{f};
                else{
                    Assert.IsTrue(f.FieldType.IsValueType,"Unhandled authored reference field: "+f.Name);
                    bool found=false;foreach(var child in Leaves(f.FieldType)){found=true;var path=new FieldInfo[child.Length+1];path[0]=f;Array.Copy(child,0,path,1,child.Length);yield return path;}
                    Assert.IsTrue(found,"Unhandled opaque authored field: "+f.Name);
                }
            }
        }
        static object Change(object owner,FieldInfo[] path,int index)
        {
            var f=path[index];object value=f.GetValue(owner);
            if(index+1<path.Length)value=Change(value,path,index+1);
            else if(f.FieldType==typeof(float))value=(float)value+.125f;
            else if(f.FieldType==typeof(int))value=(int)value+1;
            else if(f.FieldType==typeof(bool))value=!(bool)value;
            else if(f.FieldType.IsEnum)value=Enum.ToObject(f.FieldType,Convert.ToInt32(value)+1);
            else Assert.Fail("Unhandled authoring leaf: "+PathName(path));
            f.SetValue(owner,value);return owner;
        }
        static string PathName(FieldInfo[] path){string text="";foreach(var f in path)text+=(text.Length==0?"":".")+f.Name;return text;}
        [Test] public void PureEnemyColorAndNameChangesAreVisualOnlyAndCanRestore()
        {
            using var a=Create();byte[] bytes=SvWeaponSave.Capture(a.Session,Runtime);var before=SvWeaponSave.Describe(a.Session,Runtime);
            var e=a.Runtime.Enemies[0];e.Color=new float4(.1f,.2f,.3f,1);a.Runtime.Enemies[0]=e;a.Runtime.EnemyNames[0]="New cosmetic name";
            var after=SvWeaponSave.Describe(a.Session,Runtime);Assert.AreEqual(before.ContentFingerprint,after.ContentFingerprint);Assert.AreNotEqual(before.VisualFingerprint,after.VisualFingerprint);
            using var stream=new MemoryStream(bytes);SvWeaponSave.Restore(stream,a.Session,Runtime);
        }
        [Test] public void GuardWeaponFactoryAndKnownLegacyImportUseTheRealOptionalComposition()
        {
            var config=SvConfig.CreateWeaponCombatExample();var mode=SvMode.Create(config,out var module);
            try{
                using var a=SimSession.Create(mode,71);a.World.Resource(SvKeys.Game).Send(SvCommandKind.Start);a.Step();a.Step();
                var d=SvWeaponSave.Describe(a,Runtime);byte[] raw=a.CaptureSnapshot();byte[] bytes;
                using(var stream=new MemoryStream(raw,false))bytes=SaveEnvelope.ImportKnownLegacy(stream,new KnownLegacySaveDescriptor("known.guard-weapons.raw-v1",d),mode,71,t=>SvWeaponSave.Describe(t,Runtime));
                uint rev=a.TimelineRevision;CollectionAssert.AreEqual(raw,a.CaptureSnapshot());
                using var resumed=SimSession.Create(mode,71);using(var stream=new MemoryStream(bytes))SvWeaponSave.Restore(stream,resumed,Runtime);
                Assert.AreEqual(rev,a.TimelineRevision);CollectionAssert.AreEqual(raw,resumed.CaptureSnapshot());
                for(int i=0;i<40;i++){
                    var input=new InputFrame{Move=new float2(.1f,0),Held=1u<<SvWeapons.AttackButton};
                    a.World.Resource(SvKeys.Game).Input=resumed.World.Resource(SvKeys.Game).Input=input;a.Step();resumed.Step();
                }
                CollectionAssert.AreEqual(a.CaptureSnapshot(),resumed.CaptureSnapshot(),"known legacy import matches uninterrupted fixed-input continuation");
            }finally{UnityEngine.Object.DestroyImmediate(module);UnityEngine.Object.DestroyImmediate(mode);UnityEngine.Object.DestroyImmediate(config);}
        }
        [Test] public void UnsupportedMixedModeAndClassicCompositionAreNotLabeledCompatible()
        {
            using var a=Create();a.Runtime.Settings.FlyingSwords.Enabled=true;Assert.Throws<NotSupportedException>(()=>SvWeaponSave.Describe(a.Session,Runtime));
            using var b=new SvTestWorld();Assert.Throws<ArgumentException>(()=>SvWeaponSave.Describe(b.Session,Runtime));
        }
        static void Reject(byte[] bytes,SvTestWorld target)
        {byte[] raw=target.Session.CaptureSnapshot();uint revision=target.Session.TimelineRevision;target.Session.RequestTicks(3);int pending=target.Session.PendingTicks;using(var stream=new MemoryStream(bytes))Assert.Throws<InvalidDataException>(()=>SvWeaponSave.Restore(stream,target.Session,Runtime));Assert.AreEqual(revision,target.Session.TimelineRevision);Assert.AreEqual(pending,target.Session.PendingTicks);CollectionAssert.AreEqual(raw,target.Session.CaptureSnapshot());}
    }
}
