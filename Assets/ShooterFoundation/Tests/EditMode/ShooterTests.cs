using System;
using NUnit.Framework;
using SPF.Testing;
using SPF.Runtime.Composition;
using SPF.Runtime.Session;
using SPF.Runtime.World;
using Unity.Mathematics;

namespace ShooterFoundation.Tests
{
    sealed class ShooterTestWorld : IDisposable
    {
        public readonly ShooterConfig Config; public readonly SimSession Session;
        readonly GameplayModuleAsset m_Module;readonly ModeDefinition m_Mode;
        public ShooterState State => Session.World.Resource(ShooterKeys.State);
        public SimWorld World => Session.World;
        public ShooterTestWorld(Action<ShooterConfig> tweak=null,uint seed=17,bool start=true)
        {
            Config=ShooterConfig.CreateDefault();Config.Settings.SpawnWaves=false;tweak?.Invoke(Config);
            m_Mode=ShooterMode.Create(Config,out m_Module);Session=SimSession.Create(m_Mode,seed);Session.Start();
            if(start) { State.Send(ShooterCommandKind.Start);Step();State.Send(ShooterCommandKind.Choose,1);Step();World.ClearLevel();SilenceWeapons(); }
        }
        public void SilenceWeapons() { State.Run.ShotTimer=State.Run.WingTimer=100000f; }
        public void Step(int count=1) { for(int i=0;i<count;i++)Session.Step(); }
        public void Dispose() { Session.Dispose();UnityEngine.Object.DestroyImmediate(m_Module);UnityEngine.Object.DestroyImmediate(m_Mode);UnityEngine.Object.DestroyImmediate(Config); }
    }
    public class ShooterTests
    {
        [Test]
        public void ConfigurationRejectsNonFiniteRatesAndOversizedOwners()
        {
            var s=ShooterSettings.Default;s.ShotSpeed=float.NaN;Assert.Throws<ArgumentOutOfRangeException>(()=>s.Validate());
            s=ShooterSettings.Default;s.EnemyCapacity=65537;Assert.Throws<ArgumentOutOfRangeException>(()=>s.Validate());
            s=ShooterSettings.Default;s.GridCell=float.PositiveInfinity;Assert.Throws<ArgumentOutOfRangeException>(()=>s.Validate());
        }
        [Test]
        public void StartOffersThreeDistinctChoicesAndFreezesSimulation()
        {
            using var t=new ShooterTestWorld(start:false);t.State.Send(ShooterCommandKind.Start);t.Step();
            Assert.AreEqual(ShooterFlow.Upgrade,t.State.Run.Flow);Assert.AreNotEqual(t.State.Choice(0),t.State.Choice(1));Assert.AreNotEqual(t.State.Choice(0),t.State.Choice(2));Assert.AreNotEqual(t.State.Choice(1),t.State.Choice(2));
            float time=t.State.Run.Time;t.State.Run.Move=new float2(1,1);t.Step(60);Assert.AreEqual(time,t.State.Run.Time);
            t.State.Send(ShooterCommandKind.Choose,-1);t.Step();Assert.AreEqual(ShooterFlow.Upgrade,t.State.Run.Flow);
            t.State.Send(ShooterCommandKind.Choose,0);t.State.Send(ShooterCommandKind.Choose,0);t.Step();
            Assert.AreEqual(ShooterFlow.Playing,t.State.Run.Flow);Assert.AreEqual(1,t.State.Run.Beam);Assert.AreEqual(1,t.State.Run.Wave,"double click applies exactly one choice");
        }
        [Test]
        public void SweptProjectileSelectsFirstImpactInsteadOfRowOrder()
        {
            using var t=new ShooterTestWorld();
            int far=ShooterSpawner.Enemy(t.World,new float2(0,4),hp:100,speed:0),near=ShooterSpawner.Enemy(t.World,new float2(0,1),hp:100,speed:0);
            ShooterSpawner.Bullet(t.World,new float2(0,-4),new float2(0,300),damage:25);t.Step();
            Assert.AreEqual(100,t.World.Column(ShooterKeys.Enemies)[far].Hp);Assert.AreEqual(75,t.World.Column(ShooterKeys.Enemies)[near].Hp);
        }
        [TestCase(600f)]
        [TestCase(1500f)]
        public void MovingEnemySweepsThroughStationaryProjectile(float speed)
        {
            using var t=new ShooterTestWorld();ShooterSpawner.Enemy(t.World,new float2(2,9.6f),hp:10,speed:speed);
            ShooterSpawner.Bullet(t.World,new float2(2,0),float2.zero,damage:20);t.Step();Assert.AreEqual(1,t.State.Run.Kills,"swept grid bounds include fast moving enemies");
        }
        [Test]
        public void BackdropScrollRemainsInRangeAfterLongRuns()
        {
            foreach(float seconds in new[] {120f,600f,36000f})
            {
                for(int i=0;i<18;i++)
                {
                    float cloud=ShooterMath.Repeat(i*2.1f-seconds*0.7f+40f,22f)-11f;
                    float streak=ShooterMath.Repeat(i*1.37f-seconds*1.8f+80f,20f)-10f;
                    Assert.That(cloud,Is.GreaterThanOrEqualTo(-11f).And.LessThan(11f));
                    Assert.That(streak,Is.GreaterThanOrEqualTo(-10f).And.LessThan(10f));
                }
            }
        }
        [Test]
        public void SweepHandlesTangentsInitialOverlapAndZeroLength()
        {
            Assert.IsTrue(ShooterMath.Sweep(new float2(-2,1),new float2(2,1),float2.zero,1,out var tangent));Assert.AreEqual(0.5f,tangent,0.0001f);
            Assert.IsTrue(ShooterMath.Sweep(float2.zero,float2.zero,float2.zero,1,out var overlap));Assert.AreEqual(0f,overlap);
            Assert.IsFalse(ShooterMath.Sweep(new float2(2),new float2(2),float2.zero,1,out _));
            Assert.IsFalse(ShooterMath.Sweep(new float2(1.0000005f,0),new float2(1,0),float2.zero,1,out _),"legacy Shooter preserves its tiny-motion cutoff; new CombatSweep API has no cutoff");
        }
        [Test]
        public void RayUsesNearestTargetThenStableSpawnId()
        {
            using var t=new ShooterTestWorld();t.State.Run.Hero=t.State.Run.HeroPrevious=float2.zero;t.State.Run.Beam=1;
            int left=ShooterSpawner.Enemy(t.World,new float2(-1,3),hp:100,speed:0),right=ShooterSpawner.Enemy(t.World,new float2(1,3),hp:100,speed:0);
            t.Step();Assert.AreEqual(t.World.Column(ShooterKeys.Enemies)[left].Id,t.State.Run.BeamTargetId);Assert.AreEqual(100,t.World.Column(ShooterKeys.Enemies)[right].Hp);
            var info=t.World.Column(ShooterKeys.Enemies);var e=info[left];e.Hp=0;info[left]=e;t.Step();Assert.AreEqual(info[right].Id,t.State.Run.BeamTargetId,"dead target is excluded immediately");
        }
        [Test]
        public void PickupsHealClampAndAwardSalvage()
        {
            using var t=new ShooterTestWorld();t.State.Run.Hp=95;ShooterSpawner.Pickup(t.World,t.State.Run.Hero,true);ShooterSpawner.Pickup(t.World,t.State.Run.Hero,false);t.Step();
            Assert.AreEqual(100,t.State.Run.Hp);Assert.AreEqual(1,t.State.Run.Coins);t.Step();Assert.AreEqual(0,t.World.Table(ShooterKeys.Pickup).Count);
        }
        [Test]
        public void CapacityOverflowIsExplicitAndDeadRowsAreReused()
        {
            using var t=new ShooterTestWorld(c=>{c.Settings.BulletCapacity=8;c.Settings.EnemyCapacity=2;c.Settings.PickupCapacity=2;});
            for(int i=0;i<8;i++)Assert.GreaterOrEqual(ShooterSpawner.Bullet(t.World,new float2(0,0),float2.zero),0);
            Assert.AreEqual(-1,ShooterSpawner.Bullet(t.World,float2.zero,float2.zero));Assert.AreEqual(1,t.State.Run.DroppedSpawns);
            var dead=t.World.Table(ShooterKeys.Bullet).DeadFlags;for(int i=0;i<4;i++)dead[i]=1;t.Step();Assert.AreEqual(4,t.World.Table(ShooterKeys.Bullet).Count);
            Assert.GreaterOrEqual(ShooterSpawner.Bullet(t.World,float2.zero,float2.zero),0);
            for(int i=0;i<16;i++)Assert.IsTrue(t.State.Send(ShooterCommandKind.Choose,0));Assert.IsFalse(t.State.Send(ShooterCommandKind.Choose,0));Assert.AreEqual(1,t.State.DroppedCommands);
        }
        [Test]
        public void CancelAndBoundsRejectStaleOrNonFiniteInput()
        {
            using var t=new ShooterTestWorld();float2 original=t.State.Run.Hero;t.State.Run.Drag=new float2(100);t.State.Run.Move=new float2(1);t.State.CancelInput();t.Step();Assert.AreEqual(original,t.State.Run.Hero);
            t.State.Run.Drag=new float2(float.NaN);t.State.Run.Move=new float2(float.PositiveInfinity);t.Step();Assert.AreEqual(original,t.State.Run.Hero);
            t.State.Run.Drag=new float2(1000);t.Step();Assert.IsTrue(math.all(t.State.Run.Hero<=t.Config.Settings.ArenaHalf));
        }
        [Test]
        public void WinDeathRestartAndMenuTransitionsAreBounded()
        {
            using var t=new ShooterTestWorld(c=>{c.Settings.SpawnWaves=true;c.Settings.Waves=1;c.Settings.EnemiesPerWave=1;});
            t.State.Run.Spawned=1;t.Step();Assert.AreEqual(ShooterFlow.Won,t.State.Run.Flow);
            t.State.Send(ShooterCommandKind.Start);t.Step();Assert.AreEqual(ShooterFlow.Upgrade,t.State.Run.Flow);Assert.AreEqual(0,t.State.Run.Kills);Assert.AreEqual(100,t.State.Run.Hp);
            t.State.Send(ShooterCommandKind.Choose,0);t.Step();t.State.Run.Hp=0;t.Step();Assert.AreEqual(ShooterFlow.Dead,t.State.Run.Flow);
            t.State.Send(ShooterCommandKind.Menu);t.Step();Assert.AreEqual(ShooterFlow.Menu,t.State.Run.Flow);Assert.AreEqual(0,t.World.Table(ShooterKeys.Bullet).Count);
        }
        [Test]
        public void SameSeedAndInputsMatchAndSnapshotResumesExactly()
        {
            byte[] Run(out byte[] middle)
            {
                using var t=new ShooterTestWorld(c=>{c.Settings.SpawnWaves=true;c.Settings.HeroHp=10000;},start:false);t.State.Send(ShooterCommandKind.Start);middle=null;
                for(int i=0;i<360;i++) { Input(t,i);if(i==180)middle=t.Session.CaptureSnapshot(); }
                return t.Session.CaptureSnapshot();
            }
            var a=Run(out var middle);var b=Run(out _);CollectionAssert.AreEqual(a,b);
            using var restored=new ShooterTestWorld(c=>{c.Settings.SpawnWaves=true;c.Settings.HeroHp=10000;},start:false);restored.Session.RestoreSnapshot(middle);
            for(int i=181;i<360;i++)Input(restored,i);CollectionAssert.AreEqual(a,restored.Session.CaptureSnapshot());
        }
        static void Input(ShooterTestWorld t,int tick) { if(t.State.Run.Flow==ShooterFlow.Upgrade)t.State.Send(ShooterCommandKind.Choose,tick%3);t.State.Run.Move=new float2(math.sin(tick*0.037f),0);t.Step(); }
        [Test]
        public void SteadySimulationHasNoManagedAllocations()
        {
            using var t=new ShooterTestWorld();t.State.Run.Hp=100000;
            for(int i=0;i<80;i++)ShooterSpawner.Enemy(t.World,new float2((i%10)*0.8f-3.6f,2+(i/10)*0.7f),hp:100000,speed:0);
            t.State.Run.ShotTimer=0;t.State.Run.WingTimer=0;
            t.Step(150);
            Action measured = () => t.Step(180);
            using var probe = new ManagedAllocationProbe();
            var calibrationBefore = probe.Calibrate();
            var sample = probe.Measure(measured);
            var calibrationAfter = probe.Calibrate();
            TestContext.WriteLine($"Shooter simulation, 180 ticks after 150 warm-up ticks: {sample.Value} current-thread {sample.Metric}; independent process-wide gen0 collections={sample.Collections}; retained-array/empty calibration before={calibrationBefore.RetainedArrays.Value}/{calibrationBefore.Empty.Value}, after={calibrationAfter.RetainedArrays.Value}/{calibrationAfter.Empty.Value}.");
            Assert.AreEqual(0, sample.Value, $"Warmed simulation must allocate zero current-thread {sample.Metric}; excludes engine rendering and native allocations.");
        }
    }
}
