using System;
using System.IO;
using System.Text;
using NUnit.Framework;
using SPF.Contracts;
using SPF.L1.Skeleton;
using SPF.Presentation.Animation;
using Unity.Collections;
using Unity.Mathematics;

namespace SPF.Tests.EditMode
{
    public class GroundedGaitTests
    {
        [TestCase(30)] [TestCase(60)] [TestCase(120)]
        public void SteadyGaitsHaveAlternatingSupportAndStableBodyAcrossRolesAndGroundDirections(int hz)
        {
            using(var rig=NaturalCharacterRig.Create())
            using(var local=new NativeArray<BoneLocal>(NaturalCharacterRig.Bones,Allocator.Temp))
            using(var world=new NativeArray<BoneWorld>(NaturalCharacterRig.Bones,Allocator.Temp))
            {
                foreach(int role in new[]{0,1,2})foreach(float speed in new[]{.04f,.15f,.5f,1.5f,2.5f,3.5f,5f,7.58f})for(int direction=0;direction<8;direction++)
                {
                    var input=new GameplayCharacterInput{Handle=new EntityHandle(1,1),Facing=direction>=3&&direction<=5?-1:1,Scale=1,Tint=new float4(1),MotionProfileId=role};
                    float a=direction*math.PI/4;input.Velocity=new float2(math.cos(a),math.sin(a))*speed;var m=default(GameplayCharacterMotion);
                    float low=10,high=-10,headLow=10,headHigh=-10;int bothAir=0,doubleSupport=0,farLands=0,nearLands=0,frames=0,airStreak=0,maxAir=0;
                    string context=" Hz="+hz+" role="+role+" speed="+speed+" direction="+direction;
                    for(int f=0;f<hz*5;f++)
                    {
                        input.Root=input.Ground+=input.Velocity/hz;var before=m;m.Step(input,1f/hz);GameplayCharacterMotion.Pose(rig.View,local,world,input,m,0);
                        if(f<hz)continue;
                        Assert.IsFalse(m.Airborne,"locomotion must not invent authoritative jump"+context);
                        CheckContact(before.FarFoot,m.FarFoot,world[NaturalCharacterRig.FarFoot].Position,context+" far frame="+f+" root="+input.Root+" pelvis="+local[NaturalCharacterRig.Pelvis].Position);
                        CheckContact(before.NearFoot,m.NearFoot,world[NaturalCharacterRig.NearFoot].Position,context+" near frame="+f+" root="+input.Root+" pelvis="+local[NaturalCharacterRig.Pelvis].Position);
                        if(m.Locomotion==GameplayLocomotionState.Walk)Assert.IsTrue(m.FarFoot.InStance||m.NearFoot.InStance,"walking lost both supports"+context);
                        if(!m.FarFoot.InStance&&!m.NearFoot.InStance){bothAir++;airStreak++;maxAir=math.max(maxAir,airStreak);}else airStreak=0;
                        if(m.FarFoot.InStance&&m.NearFoot.InStance)doubleSupport++;
                        if(!before.FarFoot.InStance&&m.FarFoot.InStance)farLands++;
                        if(!before.NearFoot.InStance&&m.NearFoot.InStance)nearLands++;
                        float height=world[NaturalCharacterRig.Pelvis].Position.y-input.Root.y,head=world[NaturalCharacterRig.Head].Position.y-input.Root.y;
                        low=math.min(low,height);high=math.max(high,height);headLow=math.min(headLow,head);headHigh=math.max(headHigh,head);frames++;
                    }
                    Assert.Less(high-low,.085f,"steady locomotion must not squat/bounce the body"+context);
                    Assert.Less(headHigh-headLow,.11f,"head must not bounce with swing-foot reach"+context);
                    Assert.Greater(farLands,1,"far foot must actually step"+context);Assert.Greater(nearLands,1,"near foot must actually step"+context);
                    if(m.Locomotion==GameplayLocomotionState.Walk)Assert.Greater(doubleSupport,0,"walking requires double support"+context);
                    else{Assert.LessOrEqual(bothAir/(float)frames,.23f,"run flight must be short and coordinated"+context);Assert.LessOrEqual(maxAir/(float)hz,.10f,"run must not hover"+context);}
                }
            }
        }
        static void CheckContact(FootPlantState before,FootPlantState foot,float2 fk,string context)
        {
            if(!foot.InStance)return;
            if(before.Initialized&&before.InStance)Assert.AreEqual(before.Plant,foot.Plant,"a planted foot cannot slide"+context);
            Assert.Less(math.distance(fk,foot.Position),.0002f,"claimed contact must match rendered FK foot="+foot.Position+" fk="+fk+" phase="+foot.Phase+context);
        }
        [TestCase(30)] [TestCase(60)] [TestCase(120)]
        public void WalkStartStopAndReversalNeverBecomeSynchronizedHops(int hz)
        {
            using(var rig=NaturalCharacterRig.Create())
            using(var local=new NativeArray<BoneLocal>(NaturalCharacterRig.Bones,Allocator.Temp))
            using(var world=new NativeArray<BoneWorld>(NaturalCharacterRig.Bones,Allocator.Temp))
            {
                for(int role=0;role<3;role++)for(int path=0;path<4;path++)
                {
                    var input=new GameplayCharacterInput{Handle=new EntityHandle(1,1),Facing=1,Scale=1,Tint=new float4(1),MotionProfileId=role};var m=default(GameplayCharacterMotion);
                    for(int f=0;f<hz*8;f++)
                    {
                        float t=f/(float)hz;int stage=(int)t;float speed=stage==0||stage==3||stage==7?0:stage==1?.15f:stage==2?1.8f:stage==4?.6f:2.1f;
                        float angle=path==0?0:path==1?math.PI*.5f:path==2?math.PI*.25f:t*2.5f;if(stage>=5)angle+=math.PI;
                        input.Velocity=new float2(math.cos(angle),math.sin(angle))*speed;input.Facing=input.Velocity.x<-.02f?-1:1;input.Root=input.Ground+=input.Velocity/hz;
                        var before=m;m.Step(input,1f/hz);GameplayCharacterMotion.Pose(rig.View,local,world,input,m,0);
                        string context=" Hz="+hz+" role="+role+" path="+path+" frame="+f;
                        Assert.IsTrue(m.FarFoot.InStance||m.NearFoot.InStance,"start/stop/reversal lost every ground support"+context);
                        CheckContact(before.FarFoot,m.FarFoot,world[NaturalCharacterRig.FarFoot].Position,context+" far frame="+f+" root="+input.Root+" pelvis="+local[NaturalCharacterRig.Pelvis].Position);
                        CheckContact(before.NearFoot,m.NearFoot,world[NaturalCharacterRig.NearFoot].Position,context+" near frame="+f+" root="+input.Root+" pelvis="+local[NaturalCharacterRig.Pelvis].Position);
                    }
                }
            }
        }
        [Test] public void ExportGroundedGaitTrace()
        {
            string dir=Environment.GetEnvironmentVariable("SPF_GAIT_TRACE");if(string.IsNullOrEmpty(dir))return;
            Directory.CreateDirectory(dir);
            using(var rig=NaturalCharacterRig.Create())
            using(var local=new NativeArray<BoneLocal>(NaturalCharacterRig.Bones,Allocator.Temp))
            using(var world=new NativeArray<BoneWorld>(NaturalCharacterRig.Bones,Allocator.Temp))
            {
                foreach(int hz in new[]{30,60,120})foreach(int role in new[]{0,1,2})foreach(float speed in new[]{.15f,.5f,1.5f,2.5f,3.5f,5f,7.58f})foreach(int axis in new[]{0,1,2})
                {
                    var input=new GameplayCharacterInput{Handle=new EntityHandle(1,1),Facing=1,Scale=1,Tint=new float4(1),MotionProfileId=role};
                    float a=axis*math.PI/4;input.Velocity=new float2(math.cos(a),math.sin(a))*speed;
                    var m=default(GameplayCharacterMotion);float min=10,max=-10,maxdy=0,oldy=0;int air=0,frames=0,farLand=0,nearLand=0;
                    var csv=new StringBuilder("time,rootX,groundY,velocityX,velocityY,locomotion,farPhase,nearPhase,farStance,nearStance,farX,farY,nearX,nearY,pelvisY,headY,authoritativeJump\n");
                    for(int f=0;f<hz*5;f++)
                    {
                        input.Ground+=input.Velocity/hz;input.Root=input.Ground;var before=m;m.Step(input,1f/hz);GameplayCharacterMotion.Pose(rig.View,local,world,input,m,0);
                        float py=world[NaturalCharacterRig.Pelvis].Position.y-input.Root.y,hy=world[NaturalCharacterRig.Head].Position.y-input.Root.y;
                        if(f>=hz){frames++;if(!m.FarFoot.InStance&&!m.NearFoot.InStance)air++;min=math.min(min,py);max=math.max(max,py);maxdy=math.max(maxdy,math.abs(py-oldy));if(!before.FarFoot.InStance&&m.FarFoot.InStance)farLand++;if(!before.NearFoot.InStance&&m.NearFoot.InStance)nearLand++;}oldy=py;
                        csv.AppendLine(string.Join(",",f/(float)hz,input.Root.x,input.Root.y,input.Velocity.x,input.Velocity.y,m.Locomotion,m.FarFoot.Phase,m.NearFoot.Phase,m.FarFoot.InStance?1:0,m.NearFoot.InStance?1:0,m.FarFoot.Position.x,m.FarFoot.Position.y,m.NearFoot.Position.x,m.NearFoot.Position.y,py,hy,input.Root.y-input.Ground.y));
                    }
                    File.WriteAllText(Path.Combine(dir,$"hz{hz}-role{role}-speed{speed}-axis{axis}.csv"),csv.ToString());
                    TestContext.WriteLine($"GAIT hz={hz} role={role} speed={speed} axis={axis} state={m.Locomotion} bothAir={air}/{frames} pelvis={min:F4}..{max:F4} range={max-min:F4} maxStep={maxdy:F4} lands={farLand}/{nearLand}");
                }
            }
        }
    }
}
