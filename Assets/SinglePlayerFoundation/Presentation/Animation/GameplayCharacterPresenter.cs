using System;
using System.Collections.Generic;
using SPF.Contracts;
using SPF.L1.Skeleton;
using SPF.Presentation.Sprites;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace SPF.Presentation.Animation
{
    /// <summary>Reusable opt-in CPU/Burst cutout presentation for live gameplay. Owns one rig, atlas,
    /// warmed sprite batch and fixed native state. No actor GameObjects, Animator components or simulation writes.</summary>
    public sealed class GameplayCharacterPresenter : IDisposable
    {
        readonly SkeletonAsset m_Rig;
        readonly NaturalCharacterArt m_Art;
        readonly SpriteBatch m_Batch;
        readonly Dictionary<EntityHandle,int> m_Slots;
        readonly HashSet<EntityHandle> m_Submitted;
        readonly EntityHandle[] m_Handles;
        readonly int[] m_Seen;
        NativeArray<GameplayCharacterInput> m_Inputs;
        NativeArray<GameplayCharacterMotion> m_Motion;
        NativeArray<int> m_InputSlots,m_PoseTicks;
        NativeArray<BoneAttachment> m_Attachments;
        NativeArray<BoneLocal> m_Local;
        NativeArray<BoneWorld> m_World;
        float m_Time,m_Dt;
        int m_Frame,m_Quality;
        bool m_Disposed;
        public int Capacity { get; }
        public int Count { get; private set; }
        public int PartsDrawn=>m_Batch.Count;
        public long BytesUploaded=>m_Batch.BytesUploaded;
        public int PosesEvaluated { get; private set; }
        public uint VisibleStates { get; private set; }
        public int ColorAtlasBytes=>m_Art.Sheet.Texture.width*m_Art.Sheet.Texture.height*4;
        public GameplayCharacterPresenter(RenderTier tier,int capacity,int queueOffset=-20)
        {
            if(capacity<1||capacity>512)throw new ArgumentOutOfRangeException(nameof(capacity));
            Capacity=capacity;m_Rig=NaturalCharacterRig.Create();m_Art=new NaturalCharacterArt();
            m_Batch=new SpriteBatch(tier,m_Art.Sheet.Texture,BlendKind.Translucent,capacity*NaturalCharacterArt.Parts,queueOffset);
            // Mixed articulated/fallback batches share ground depth. A tiny alpha discard prevents transparent
            // quad borders writing depth while preserving antialiased edges and cross-atlas occlusion.
            m_Batch.Material?.SetFloat(RenderAssets.Ids.ZWrite,1);
            m_Batch.Material?.SetFloat(Shader.PropertyToID("_Cutoff"),.02f);
            m_Batch.Warmup(m_Batch.Capacity);
            m_Slots=new Dictionary<EntityHandle,int>(capacity);m_Submitted=new HashSet<EntityHandle>(capacity);m_Handles=new EntityHandle[capacity];m_Seen=new int[capacity];
            m_Inputs=new NativeArray<GameplayCharacterInput>(capacity,Allocator.Persistent);
            m_Motion=new NativeArray<GameplayCharacterMotion>(capacity,Allocator.Persistent);
            m_InputSlots=new NativeArray<int>(capacity,Allocator.Persistent);m_PoseTicks=new NativeArray<int>(capacity,Allocator.Persistent);
            m_Local=new NativeArray<BoneLocal>(capacity*NaturalCharacterRig.Bones,Allocator.Persistent);
            m_World=new NativeArray<BoneWorld>(capacity*NaturalCharacterRig.Bones,Allocator.Persistent);
            m_Attachments=new NativeArray<BoneAttachment>(2*NaturalCharacterArt.Parts,Allocator.Persistent);
            for(int k=0;k<2;k++)for(int p=0;p<NaturalCharacterArt.Parts;p++)m_Attachments[k*NaturalCharacterArt.Parts+p]=m_Art.Attachments[k][p];
            for(int i=0;i<capacity;i++)m_PoseTicks[i]=-1;
        }
        public void Clear()
        {
            m_Slots.Clear();m_Submitted.Clear();Array.Clear(m_Handles,0,Capacity);Array.Clear(m_Seen,0,Capacity);
            for(int i=0;i<Capacity;i++){m_Motion[i]=default;m_PoseTicks[i]=-1;}
            Count=0;m_Batch.Clear();
        }
        public void Begin(float dt,int quality)
        {
            m_Dt=math.clamp(dt,0,.1f);m_Time+=m_Dt;m_Quality=math.clamp(quality,0,3);
            m_Frame++;m_Submitted.Clear();Count=0;VisibleStates=0;PosesEvaluated=0;m_Batch.Clear();
        }
        public bool Submit(in GameplayCharacterInput input)
        {
            if(Count>=Capacity||input.Handle.IsNull||!math.all(math.isfinite(input.Root))||!math.all(math.isfinite(input.Ground)))return false;
            if(!m_Submitted.Add(input.Handle))return false;
            m_Inputs[Count++]=input;return true;
        }
        static bool ComesBefore(in GameplayCharacterInput a,in GameplayCharacterInput b)=>a.Ground.y>b.Ground.y||
            (a.Ground.y==b.Ground.y&&(a.Handle.Index<b.Handle.Index||(a.Handle.Index==b.Handle.Index&&a.Handle.Generation<b.Handle.Generation)));
        void SortInputs()
        {
            // In-place heapsort: deterministic O(capacity log capacity), no delegates/scratch allocations.
            // Actor depth ties use stable handles. Duplicate suppression is the pre-sized set at Submit.
            for(int root=Count/2-1;root>=0;root--)Sift(root,Count);
            for(int end=Count-1;end>0;end--){Swap(0,end);Sift(0,end);}
        }
        void Swap(int a,int b){var value=m_Inputs[a];m_Inputs[a]=m_Inputs[b];m_Inputs[b]=value;}
        void Sift(int root,int count)
        {
            while(root*2+1<count)
            {
                int next=root*2+1;
                if(next+1<count&&ComesBefore(m_Inputs[next],m_Inputs[next+1]))next++;
                if(!ComesBefore(m_Inputs[root],m_Inputs[next]))break;
                Swap(root,next);root=next;
            }
        }
        public void Evaluate()
        {
            SortInputs();
            // Mark all surviving identities before reclaiming any slot. New actors cannot evict a selected
            // actor merely because the simulation's dense rows or selection heap changed order.
            for(int i=0;i<Count;i++)
            {
                if(m_Slots.TryGetValue(m_Inputs[i].Handle,out int slot)){m_InputSlots[i]=slot;m_Seen[slot]=m_Frame;}
                else m_InputSlots[i]=-1;
            }
            int free=0;
            for(int i=0;i<Count;i++)
            {
                int slot=m_InputSlots[i];var input=m_Inputs[i];
                if(slot<0)
                {
                    while(free<Capacity&&m_Seen[free]==m_Frame)free++;
                    slot=free++;if(!m_Handles[slot].IsNull)m_Slots.Remove(m_Handles[slot]);
                    m_Handles[slot]=input.Handle;m_Slots.Add(input.Handle,slot);m_Seen[slot]=m_Frame;m_InputSlots[i]=slot;
                    m_Motion[slot]=default;m_PoseTicks[slot]=-1;
                }
                var motion=m_Motion[slot];motion.Step(input,m_Dt);m_Motion[slot]=motion;
                VisibleStates|=1u<<(int)input.State;
                int hz=input.Kind==0?60:m_Quality==0?30:m_Quality==1?24:15;
                int tick=(int)(m_Time*hz);
                if(m_PoseTicks[slot]!=tick)PosesEvaluated++;
            }
            if(Count==0)return;
            var output=m_Batch.Reserve(Count*NaturalCharacterArt.Parts);
            new PoseJob {Rig=m_Rig.View,Inputs=m_Inputs,Slots=m_InputSlots,Motion=m_Motion,Attachments=m_Attachments,
                Local=m_Local,World=m_World,PoseTicks=m_PoseTicks,Sprites=output,Time=m_Time,Quality=m_Quality}.Schedule(Count,16).Complete();
        }
        public void Draw(Bounds bounds)=>m_Batch.Draw(bounds);
        public bool TryRead(EntityHandle handle,out GameplayCharacterMotion motion)
        {if(m_Slots.TryGetValue(handle,out int slot)){motion=m_Motion[slot];return true;}motion=default;return false;}
        public BoneWorld ReadBone(EntityHandle handle,int bone)
        {if(bone<0||bone>=NaturalCharacterRig.Bones)throw new ArgumentOutOfRangeException(nameof(bone));return m_Slots.TryGetValue(handle,out int slot)?m_World[slot*NaturalCharacterRig.Bones+bone]:default;}
        public void Dispose()
        {
            if(m_Disposed)return;m_Disposed=true;m_Batch.Dispose();m_Art.Dispose();m_Rig.Dispose();
            m_Inputs.Dispose();m_Motion.Dispose();m_InputSlots.Dispose();m_PoseTicks.Dispose();m_Attachments.Dispose();m_Local.Dispose();m_World.Dispose();
        }
        [BurstCompile]
        struct PoseJob:IJobParallelFor
        {
            [ReadOnly] public SkeletonView Rig;
            [ReadOnly] public NativeArray<GameplayCharacterInput> Inputs;
            [ReadOnly] public NativeArray<int> Slots;
            [ReadOnly] public NativeArray<GameplayCharacterMotion> Motion;
            [ReadOnly] public NativeArray<BoneAttachment> Attachments;
            [NativeDisableParallelForRestriction] public NativeArray<BoneLocal> Local;
            [NativeDisableParallelForRestriction] public NativeArray<BoneWorld> World;
            [NativeDisableParallelForRestriction] public NativeArray<int> PoseTicks;
            [NativeDisableParallelForRestriction] public NativeArray<PackedSprite> Sprites;
            public float Time;
            public int Quality;
            public void Execute(int i)
            {
                int slot=Slots[i],at=slot*NaturalCharacterRig.Bones;var input=Inputs[i];var motion=Motion[slot];
                int hz=input.Kind==0?60:Quality==0?30:Quality==1?24:15,tick=(int)(Time*hz);
                if(PoseTicks[slot]!=tick)
                {
                    GameplayCharacterMotion.Pose(Rig,Local,World,input,motion,at);PoseTicks[slot]=tick;
                }
                else { GameplayCharacterMotion.CorrectContacts(Rig,Local,input,motion,at); Skeletal.ToWorld(Rig,Local,input.Root,motion.Facing,motion.Scale,World,at,at); }
                float fall=NaturalMotion.Ease(motion.Death),angle=-motion.Facing*fall*1.48f;
                float c=math.cos(angle),s=math.sin(angle);
                float4 tint=input.Tint;tint.w*=1f-math.saturate((motion.Death-.8f)*5f);
                for(int k=0;k<NaturalCharacterArt.Parts;k++)
                {
                    var attachment=Attachments[math.clamp(input.Kind,0,1)*NaturalCharacterArt.Parts+k];var bone=World[at+attachment.Bone];
                    float2 center=bone.Transform(attachment.Offset*motion.Scale,motion.Facing);
                    float2 p=center-input.Root;center=input.Root+new float2(c*p.x-s*p.y,s*p.x+c*p.y);
                    Sprites[i*NaturalCharacterArt.Parts+k]=PackedSprite.Pack(center,attachment.Size*new float2(motion.Scale,motion.Scale*motion.Facing),
                        attachment.Uv,input.Depth,attachment.Tint*tint,bone.Rotation+attachment.Rotation*motion.Facing+angle,math.saturate(input.Flash)*.3f);
                }
            }
        }
    }
}
