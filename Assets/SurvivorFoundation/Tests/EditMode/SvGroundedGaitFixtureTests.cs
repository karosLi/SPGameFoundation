using NUnit.Framework;
using Unity.Mathematics;

namespace SurvivorFoundation.Tests
{
    public class SvGroundedGaitFixtureTests
    {
        [Test]
        public void LocomotionCaptureSpawnsMovingAgileAndHeavyActorsWithOneBasedContentIds()
        {
            using(var t=new SvTestWorld(tweak:c=>
            {
                c.Settings.SpawnPerSecond=c.Settings.SpawnGrowth=c.Settings.EliteEvery=0;
                foreach(var e in c.Enemies){e.Hp=10000;e.Damage=0;e.Shooter=false;}
            }))
            {
                t.World.ClearLevel();Assert.GreaterOrEqual(t.Runtime.EnemyKinds,3);
                for(int kind=0;kind<t.Runtime.Enemies.Length;kind++)
                {var enemy=t.Runtime.Enemies[kind];enemy.Speed=kind==2?1.3f:kind==0?2.1f:1.7f;t.Runtime.Enemies[kind]=enemy;}
                t.Spawn(1,new float2(3.5f,4));t.Spawn(2,new float2(-3.5f,3.5f));t.Spawn(3,new float2(0,-4));
                Assert.AreEqual(3,t.Enemies);var info=t.World.Column(SvKeys.Info);
                for(int row=0;row<3;row++){Assert.AreEqual(row+1,info[row].Kind);Assert.Greater(info[row].Speed,0);}
                Assert.Less(info[0].Radius,.65f,"first recorded enemy must use the agile renderer profile");
                Assert.GreaterOrEqual(info[2].Radius,.65f,"third recorded enemy must use the heavy renderer profile");
                var positions=t.World.Column(SvKeys.Position);float2 a=positions[0],b=positions[1],c=positions[2];
                t.Step(4);
                Assert.Greater(math.distance(a,positions[0]),.001f);Assert.Greater(math.distance(b,positions[1]),.001f);Assert.Greater(math.distance(c,positions[2]),.001f);
            }
        }
    }
}
