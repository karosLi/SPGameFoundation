using System;
using System.Diagnostics;
using System.IO;
using NUnit.Framework;
using SPF.L1.Spatial;
using SPF.Contracts;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace ShooterFoundation.Tests
{
    public class ShooterCollisionBenchmark
    {
        [BurstCompile(CompileSynchronously=true)]
        struct ProbeJob : IJob
        {
            public GridReader Grid;
            [ReadOnly]public NativeArray<float2> EnemyPositions,EnemyPrevious,Starts,Ends;
            [ReadOnly]public NativeArray<ShooterEnemy> Enemies;
            public NativeArray<int> Totals;
            public void Execute()
            {
                int candidates=0,hits=0;
                for(int i=0;i<Starts.Length;i++)
                {
                    var visitor=new ShooterTickSystem.SweepVisitor { Start=Starts[i],End=Ends[i],Radius=0.1f,Positions=EnemyPositions,Previous=EnemyPrevious,Info=Enemies,BestId=int.MaxValue,Hit=new ShooterHit { Target=-1,Fraction=2f } };
                    Grid.Query((Starts[i]+Ends[i])*0.5f,math.distance(Starts[i],Ends[i])*0.5f+0.101f,ref visitor);
                    candidates+=visitor.Hit.Candidates;if(visitor.Hit.Target>=0)hits++;
                }
                Totals[0]=candidates;Totals[1]=hits;
            }
        }
        [Test,Category("Performance")]
        public void DenseSpatialSweepCandidateAndTimingReport()
        {
            const int enemies=1024,bullets=4096,samples=30;
            using var t=new ShooterTestWorld(c=>{c.Settings.ArenaHalf=new float2(32);c.Settings.EnemyCapacity=enemies;c.Settings.BulletCapacity=bullets;c.Settings.EnemyFireInterval=100000;});
            for(int i=0;i<enemies;i++)ShooterSpawner.Enemy(t.World,new float2(i%32*1.8f-28f,i/32*1.8f-28f),hp:100000,speed:0);
            t.State.Run.Hero=new float2(30,-30);t.Step();
            using var starts=new NativeArray<float2>(bullets,Allocator.TempJob);using var ends=new NativeArray<float2>(bullets,Allocator.TempJob);using var totals=new NativeArray<int>(2,Allocator.TempJob);
            var random=new Unity.Mathematics.Random(721);
            for(int i=0;i<bullets;i++) { var p=t.World.Column(ShooterKeys.EnemyPosition)[i%enemies];float offset=random.NextFloat(-0.65f,0.65f);starts.Set(i,p+new float2(offset,-0.7f));ends.Set(i,p+new float2(offset,0.7f)); }
            var job=new ProbeJob { Grid=t.World.Resource(ShooterKeys.Grid).AsReader(),EnemyPositions=t.World.Column(ShooterKeys.EnemyPosition),EnemyPrevious=t.World.Column(ShooterKeys.EnemyPrevious),Enemies=t.World.Column(ShooterKeys.Enemies),Starts=starts,Ends=ends,Totals=totals };
            for(int i=0;i<10;i++)job.Run();var times=new double[samples];
            for(int i=0;i<samples;i++) { long start=Stopwatch.GetTimestamp();job.Run();times[i]=(Stopwatch.GetTimestamp()-start)*1000d/Stopwatch.Frequency; }
            Array.Sort(times);long brute=(long)enemies*bullets;
            string report=$"Shooter swept-collision microbenchmark\nEnemies {enemies}; sweeps {bullets}; direct Cartesian pairs {brute}\nGrid narrow-phase candidates {totals[0]}; hits {totals[1]}; candidate ratio {(double)totals[0]/brute:P3}\nWarmup 10; samples {samples}; p50 {times[samples/2]:F3} ms; p95 {times[(int)(samples*0.95)]:F3} ms\nIncludes grid query and exact moving-circle narrow phase. Excludes grid build, rendering and upload. Desktop/harness results do not establish mobile performance, Burst behavior under .NET stubs, thermal stability or battery use.\n";
            TestContext.WriteLine(report);
#if SPF_DOTNET_HARNESS
            string dir=Path.Combine(Path.GetTempPath(),"spf-artifacts");
#else
            string dir=Path.Combine(Path.GetDirectoryName(UnityEngine.Application.dataPath)??".","Artifacts");
#endif
            Directory.CreateDirectory(dir);File.WriteAllText(Path.Combine(dir,"perf-shooter.txt"),report);
            Assert.Greater(totals[1],bullets/2);Assert.Less(totals[0],brute/50,"spatial broadphase must not degenerate into bullet × enemy scanning");
        }
    }
}
