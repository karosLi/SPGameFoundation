using System;
using NUnit.Framework;
using SPF.Contracts;
using SPF.Contracts.Weapons;
using SPF.L1.Skeleton;
using SPF.Presentation;
using SPF.Presentation.Animation;
using SPF.Presentation.Sprites;
using SPF.Testing;
using Unity.Mathematics;
using UnityEngine;

namespace SPF.Tests.EditMode
{
    public class GameplayCharacterStreamTests
    {
        static GameplayCharacterInput Actor(int id,float2 root,float depth,int weapon=0)
        {
            var input=new GameplayCharacterInput {Handle=new EntityHandle(id,1),Root=root,Ground=root,Depth=depth,
                Facing=id%2==0?1:-1,Scale=.7f,Tint=new float4(1),Kind=id==3?0:1};
            if(weapon!=0)input.Weapon=Weapon(weapon,input.Facing,(uint)id*31);
            return input;
        }
        static WeaponViewState Weapon(int visual,float facing,uint pulse)=>new WeaponViewState {ContentId=visual,VisualId=visual,
            Family=visual==1004?WeaponActionFamily.Draw:visual==1003?WeaponActionFamily.Cast:WeaponActionFamily.Thrust,Stage=WeaponStage.Idle,
            AimDirection=new float2(facing,0),GripOffset=new float2(.45f,1.3f),SecondaryGripOffset=new float2(.23f,1.18f),MuzzleOffset=new float2(1.25f,1.65f),
            ContactPhase=.4f,ReleasePhase=.4f,ActiveEndPhase=.55f,ActionPulse=pulse,CueSequence=pulse+1};
        static void CheckActor(GameplayCharacterPresenter presenter,in GameplayCharacterInput input,int first)
        {
            int count=input.Weapon.Equipped?19:14;
            for(int k=0;k<count;k++)Assert.AreEqual(input.Depth,presenter.ReadPart(first+k).Depth,"each packed part retains its actor depth");
            var far=presenter.ReadBone(input.Handle,NaturalCharacterRig.FarArm);
            Assert.That(math.distance(presenter.ReadPart(first).Center,far.Transform(new float2(.22f*input.Scale,0),input.Facing)),Is.LessThan(.00001f),"offset belongs to this actor's actual FK");
            if(!input.Weapon.Equipped){Assert.IsFalse(presenter.TryReadWeapon(input.Handle,out _));return;}
            Assert.IsTrue(presenter.TryReadWeapon(input.Handle,out var socket));Assert.AreEqual(input.Weapon.VisualId,socket.VisualId);
            Assert.AreEqual(input.Weapon.ActionPulse,socket.ActionPulse);Assert.AreEqual(input.Weapon.CueSequence,socket.CueSequence);
            float length=math.distance(socket.PrimaryGrip,socket.Tip)/input.Scale;
            float angle=socket.Rotation+WeaponArt.AngleOffset(socket.VisualId)*input.Facing;
            float2 centre=socket.PrimaryGrip+WeaponMotion.Rotate(WeaponArt.Centre(socket.VisualId,length)*new float2(input.Scale,input.Scale*input.Facing),angle);
            Assert.That(math.distance(presenter.ReadPart(first+11).Center,centre),Is.LessThan(.00001f),"weapon record belongs to its owner's socket");
            var hand=presenter.ReadBone(input.Handle,NaturalCharacterRig.Hand);
            Assert.That(math.distance(presenter.ReadPart(first+17).Center,hand.Transform(new float2(.055f*input.Scale,0),input.Facing)),Is.LessThan(.00001f),"hand remains after its weapon in the actor stream");
        }
        [Test] public void MixedReorderedAndRecycledActorsKeepCompactOutputAndSocketOwnership()
        {
            using(var p=new GameplayCharacterPresenter(RenderTier.DataTexture,5,includeWeapons:true))
            {
                var actors=new[]{Actor(9,new float2(4,2),.35f),Actor(3,new float2(-4,2),.45f,1002),Actor(6,new float2(0,-1),.2f),
                    Actor(12,new float2(8,0),.3f,1003),Actor(1,new float2(-8,0),.25f)};
                int[] sorted={1,0,4,3,2};var oldHandle=actors[3].Handle;
                for(int frame=0;frame<5;frame++)
                {
                    if(frame==2){actors[0].Weapon=Weapon(1004,actors[0].Facing,900);actors[1].Weapon=default;actors[3].Handle=new EntityHandle(12,2);}
                    p.Begin(1f/120,frame%4);
                    for(int i=0;i<actors.Length;i++)Assert.IsTrue(p.Submit(actors[(i*3+frame)%actors.Length]));
                    p.Evaluate();Assert.AreEqual(5*14+2*5,p.PartsDrawn);Assert.AreEqual(80*PackedSprite.Stride,p.PackedPayloadBytes);
                    int first=0;for(int i=0;i<sorted.Length;i++){var actor=actors[sorted[i]];CheckActor(p,actor,first);first+=actor.Weapon.Equipped?19:14;}
                    Assert.AreEqual(first,p.PartsDrawn);Assert.Throws<ArgumentOutOfRangeException>(()=>p.ReadPart(first));
                    if(frame>=2)Assert.IsFalse(p.TryReadWeapon(oldHandle,out _),"recycled generation cannot retain the former socket");
                }
                p.Begin(1f/120,0);p.Submit(actors[2]);p.Evaluate();Assert.AreEqual(14,p.PartsDrawn);
                Assert.IsFalse(p.TryReadWeapon(actors[0].Handle,out _),"unsubmitted actor cannot retain a current-frame socket");
            }
        }
        [Test] public void HordeUsesOnlyOneWeaponSuffixAndKeepsWarmedAllocationBudget()
        {
            const int actors=193;using(var p=new GameplayCharacterPresenter(RenderTier.DataTexture,actors,includeWeapons:true))
            {
                var inputs=new GameplayCharacterInput[actors];for(int i=0;i<actors;i++)inputs[i]=Actor(i+1,new float2(i%16,i/16),.4f+i*.0001f,i==0?1003:0);
                bool reverse=false;Action work=()=>{p.Begin(1f/120,3);for(int i=0;i<actors;i++)p.Submit(inputs[reverse?actors-1-i:i]);p.Evaluate();reverse=!reverse;};
                for(int i=0;i<8;i++)work();
                Assert.AreEqual(192*14+19,p.PartsDrawn);Assert.AreEqual(86624,p.PackedPayloadBytes);
                Assert.AreEqual(960,actors*19-p.PartsDrawn);Assert.AreEqual(30720,actors*19*PackedSprite.Stride-p.PackedPayloadBytes);
                int sockets=0;for(int i=0;i<actors;i++)if(p.TryReadWeapon(inputs[i].Handle,out var socket)){sockets++;Assert.AreEqual(inputs[0].Weapon.ActionPulse,socket.ActionPulse);}
                Assert.AreEqual(1,sockets);
                using(var probe=new ManagedAllocationProbe()){probe.Calibrate();var sample=probe.Measure(work);probe.Calibrate();Assert.AreEqual(0,sample.Value);}
                // Capacity/prewarm covers the worst case as equipment changes, without resizing the presenter.
                for(int i=0;i<actors;i++)inputs[i].Weapon=Weapon(1002,inputs[i].Facing,(uint)i+1000);
                work();Assert.AreEqual(actors*19,p.PartsDrawn);
                for(int i=0;i<actors;i++)inputs[i].Weapon=default;
                work();Assert.AreEqual(actors*14,p.PartsDrawn);
            }
        }
        [Test] public void BasePoseCounterDoesNotCountContinuousPerFrameComposition()
        {
            using(var p=new GameplayCharacterPresenter(RenderTier.DataTexture,1))
            {
                var input=Actor(8,0,.3f);input.Velocity=new float2(.35f,0);int cachedFrames=0;
                for(int frame=0;frame<120;frame++)
                {
                    input.Root=input.Ground+=input.Velocity/120;p.Begin(1f/120,3);p.Submit(input);p.Evaluate();
                    Assert.AreEqual(p.BasePoseRefreshes,p.PosesEvaluated);
                    if(p.BasePoseRefreshes!=0)continue;cachedFrames++;
                    p.TryRead(input.Handle,out var motion);
                    if(motion.FarFoot.InStance)Assert.That(math.distance(p.ReadBone(input.Handle,NaturalCharacterRig.FarFoot).Position,motion.FarFoot.Position*input.Scale),Is.LessThan(.0002f));
                }
                Assert.Greater(cachedFrames,90,"base refreshes are limited while continuous pose/contact work still runs");
            }
        }
#if !SPF_DOTNET_HARNESS
        [Test] public void DataTexturePaddingDoesNotMasqueradeAsPackedPayloadSavings()
        {
            if(SystemInfo.graphicsDeviceType==UnityEngine.Rendering.GraphicsDeviceType.Null)Assert.Ignore("Actual shader resources/upload accounting needs a graphics device.");
            const int count=193;using(var p=new GameplayCharacterPresenter(RenderTier.DataTexture,count,includeWeapons:true))
            {
                var bounds=new Bounds(Vector3.zero,new Vector3(100,100,10));
                p.Begin(.016f,0);for(int i=0;i<count;i++)p.Submit(Actor(i+1,new float2(i%16,i/16),.5f,1003));p.Evaluate();p.Draw(bounds);
                long padded=p.BytesUploaded;Assert.AreEqual(122880,padded);Assert.AreEqual(117344,p.PackedPayloadBytes);
                p.Begin(.016f,0);for(int i=0;i<count;i++)p.Submit(Actor(i+1,new float2(i%16,i/16),.5f,i==0?1003:0));p.Evaluate();p.Draw(bounds);
                Assert.AreEqual(86624,p.PackedPayloadBytes);Assert.AreEqual(padded,p.BytesUploaded,"the same warmed final prefix texture is uploaded");
            }
        }
#endif
    }
}
