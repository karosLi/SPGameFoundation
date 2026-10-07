using System;
using System.IO;
using SPF.Contracts;
using SPF.Contracts.Weapons;
using SPF.L2.Combat;
using SPF.Runtime.World;
using Unity.Collections;
using Unity.Mathematics;

namespace SPF.L2.Weapons
{
    public struct WeaponEquipment
    {
        public int EquippedId, PendingId, EquipRemaining;
        public ActionTimeline Timeline;
        public TickInputBuffer BufferedAttack;
        public float2 Aim;
        public uint CueSequence;
        public WeaponCueKind Cues;
        public bool Released;
    }
    public struct WeaponProjectile
    {
        public bool Active;
        public int ContentId, RemainingTicks;
        public uint Pulse;
        public long SpawnTick;
        public float2 Position, Previous, Direction;
        public float Height, Scale, Damage;
    }

    /// <summary>Reusable single-owner equipment/action bank plus bounded projectile pool. Games keep
    /// their SoA targets/grids/resolvers and translate accepted contacts into existing damage commands.
    /// No Unity object, game assembly, render clock, row identity or dynamically growing hot container.</summary>
    public sealed class WeaponRuntime : IDisposable, IResettableResource, ISnapshotResource
    {
        const int Magic=0x57504E31;
        readonly WeaponProfile[] m_Profiles;
        readonly uint m_Fingerprint;
        public readonly int HistoryPerAttack;
        public readonly int TickRate;
        public WeaponEquipment Equipment;
        public EntityHandle Owner;
        public NativeArray<WeaponProjectile> Projectiles;
        public NativeArray<EntityHandle> History;
        public NativeArray<HitHistoryState> Scopes;
        public NativeArray<WeaponCue> Cues;
        // Unsaved diagnostics keep the existing snapshot schema/fingerprint and classic defaults.
        public readonly SPF.Contracts.Combat.CombatTraceBuffer CollisionDebug;
        public int CueCount, RejectedProjectiles, RejectedHits, RejectedCues, Releases, AcceptedHits;
        public long Tick;
        /// <summary>Unsaved presentation invalidation epoch; changes on reset or snapshot restore, even at the same tick.</summary>
        public uint Revision { get; private set; }
        public int Count => m_Profiles.Length;
        public int ActiveProjectiles { get { int n=0;for(int i=0;i<Projectiles.Length;i++)if(Projectiles[i].Active)n++;return n; } }
        public WeaponProfile Current => Profile(Equipment.EquippedId);
        public WeaponRuntime(WeaponProfile[] profiles,int tickRate,int projectiles=32,int targetsPerAttack=128,int cues=32)
        {
            if(profiles==null||profiles.Length<1||profiles.Length>32||tickRate<10||tickRate>240||projectiles<1||projectiles>256||targetsPerAttack<1||targetsPerAttack>1024||cues<1||cues>256)
                throw new ArgumentOutOfRangeException(nameof(profiles));
            m_Profiles=(WeaponProfile[])profiles.Clone();TickRate=tickRate;HistoryPerAttack=targetsPerAttack;
            uint hash=2166136261u;
            for(int i=0;i<Count;i++)
            {
                m_Profiles[i].Validate();for(int j=0;j<i;j++)if(m_Profiles[j].ContentId==m_Profiles[i].ContentId)throw new ArgumentException("Duplicate weapon content ID.");
                var p=m_Profiles[i];Hash(ref hash,(uint)p.ContentId);Hash(ref hash,(uint)p.VisualId);Hash(ref hash,(uint)p.Family);
                Hash(ref hash,(uint)p.DurationTicks);Hash(ref hash,(uint)p.EquipTicks);Hash(ref hash,(uint)p.Active.From);Hash(ref hash,(uint)p.Active.Until);Hash(ref hash,(uint)p.Cancel.From);Hash(ref hash,(uint)p.Cancel.Until);
                Hash(ref hash,(uint)p.ReleaseTick);Hash(ref hash,(uint)p.ProjectileLifeTicks);Hash(ref hash,math.asuint(p.Damage));Hash(ref hash,math.asuint(p.Knockback));Hash(ref hash,math.asuint(p.Reach));Hash(ref hash,math.asuint(p.Radius));Hash(ref hash,math.asuint(p.ProjectileSpeed));
                Hash(ref hash,math.asuint(p.GripOffset.x));Hash(ref hash,math.asuint(p.GripOffset.y));Hash(ref hash,math.asuint(p.SecondaryGripOffset.x));Hash(ref hash,math.asuint(p.SecondaryGripOffset.y));Hash(ref hash,math.asuint(p.MuzzleOffset.x));Hash(ref hash,math.asuint(p.MuzzleOffset.y));
            }
            m_Fingerprint=hash;
            Projectiles=new NativeArray<WeaponProjectile>(projectiles,Allocator.Persistent);
            History=new NativeArray<EntityHandle>((projectiles+1)*targetsPerAttack,Allocator.Persistent);
            Scopes=new NativeArray<HitHistoryState>(projectiles+1,Allocator.Persistent);
            Cues=new NativeArray<WeaponCue>(cues,Allocator.Persistent);CollisionDebug=new SPF.Contracts.Combat.CombatTraceBuffer();OnReset();
        }
        static void Hash(ref uint h,uint v){unchecked{h=(h^v)*16777619u;}}
        public WeaponProfile Profile(int contentId)
        {for(int i=0;i<Count;i++)if(m_Profiles[i].ContentId==contentId)return m_Profiles[i];throw new ArgumentException("Unknown weapon content ID.");}
        public bool HasProfile(int contentId){for(int i=0;i<Count;i++)if(m_Profiles[i].ContentId==contentId)return true;return false;}
        public bool RequestEquip(int contentId)
        {
            if(!HasProfile(contentId))return false;
            if(contentId==Equipment.EquippedId){Equipment.PendingId=Equipment.EquipRemaining=0;return true;}
            Equipment.PendingId=contentId;if(Equipment.EquipRemaining>0)Equipment.EquipRemaining=Profile(contentId).EquipTicks;Equipment.BufferedAttack.Clear();return true;
        }
        public void Cycle()
        {int id=Equipment.PendingId!=0?Equipment.PendingId:Equipment.EquippedId;for(int i=0;i<Count;i++)if(m_Profiles[i].ContentId==id){RequestEquip(m_Profiles[(i+1)%Count].ContentId);return;}}
        public bool Busy => Equipment.Timeline.Running||Equipment.EquipRemaining>0;
        public bool MeleeActive => Equipment.Timeline.Running&&!Current.Ranged&&Equipment.Timeline.Crossed(Current.ContactWindow);
        public void BufferAttack(){Equipment.BufferedAttack.Push(1,Tick,math.max(1,TickRate/3));}
        /// <summary>Exactly once per simulation tick. paused=false progression is an exact no-op; a dead
        /// or interrupted owner clears action, pending inputs and live projectiles. Fixed-tick release
        /// attempts once even when capacity is exhausted: there is no delayed/repeated overflow shot.</summary>
        public void Step(bool playing,bool alive,bool interrupted,bool attackPressed,bool attackHeld,float2 aim,float2 ground,float height,float scale)
        {
            if(!playing)return;
            if(!math.all(math.isfinite(ground))||!math.isfinite(height)||height<0||!math.isfinite(scale)||scale<=0||scale>4)throw new ArgumentOutOfRangeException(nameof(scale),"Weapon origin, height and scale must be finite and valid.");
            ExpireProjectiles();
            Tick++;Equipment.Cues=WeaponCueKind.None;
            if(!alive||interrupted){CancelAll();return;}
            if(math.all(math.isfinite(aim))&&math.lengthsq(aim)>.0001f&&!Equipment.Timeline.Running)Equipment.Aim=math.normalize(aim);
            if(attackPressed)BufferAttack();
            var p=Current;
            if(Equipment.PendingId!=0&&Equipment.EquipRemaining==0&&(!Equipment.Timeline.Running||p.Cancel.Contains(Equipment.Timeline.Tick)||Equipment.Timeline.Tick>=p.Active.Until))
            {
                if(Equipment.Timeline.Running)Emit(WeaponCueKind.Cancel,ground,Equipment.Aim,p);
                Equipment.Timeline.Stop();ReleaseScope(0);Equipment.EquipRemaining=Profile(Equipment.PendingId).EquipTicks;
            }
            if(Equipment.EquipRemaining>0)
            {
                if(--Equipment.EquipRemaining==0)
                {Equipment.EquippedId=Equipment.PendingId;Equipment.PendingId=0;Equipment.Released=false;Emit(WeaponCueKind.Equip,ground,Equipment.Aim,Current);}
            }
            else
            {
                if(Equipment.Timeline.Running)
                {Equipment.Timeline.Advance();if(Equipment.Timeline.Tick>=p.DurationTicks){Equipment.Timeline.Stop();ReleaseScope(0);}}
                if(!Equipment.Timeline.Running&&Equipment.PendingId==0&&(Equipment.BufferedAttack.TryConsume(Tick,true,out _)||attackHeld))
                {
                    if(math.all(math.isfinite(aim))&&math.lengthsq(aim)>.0001f)Equipment.Aim=math.normalize(aim);
                    Equipment.Timeline.Begin();Equipment.Released=false;var scope=Scopes[0];HitHistory.Begin(History,0,HistoryPerAttack,ref scope,Owner,Equipment.Timeline.PulseId);Scopes[0]=scope;
                    Emit(WeaponCueKind.Begin,ground,Equipment.Aim,p);
                }
                if(Equipment.Timeline.Running&&p.Ranged&&!Equipment.Released&&Equipment.Timeline.Crossed(new ActionWindow(p.ReleaseTick,p.ReleaseTick+1)))
                {
                    Equipment.Released=true;Releases++;
                    float2 muzzle=ground+Equipment.Aim*p.MuzzleOffset.x*scale;
                    TrySpawn(p,muzzle,height+p.MuzzleOffset.y*scale,scale);
                    Emit(WeaponCueKind.Release,muzzle,Equipment.Aim,p,0,height+p.MuzzleOffset.y*scale);
                }
            }
            for(int i=0;i<Projectiles.Length;i++)
            {
                var shot=Projectiles[i];if(!shot.Active)continue;
                // The release tick starts at the canonical muzzle. Travel begins next tick, matching
                // previous/current pose interpolation without drawing held and flying arrows together.
                if(shot.SpawnTick==Tick)continue;
                shot.Previous=shot.Position;shot.Position+=shot.Direction*(Profile(shot.ContentId).ProjectileSpeed/TickRate);shot.RemainingTicks--;
                Projectiles[i]=shot;
            }
        }
        /// <summary>Read-only birth visibility at interpolated simulation time. A ranged weapon keeps
        /// its nocked arrow until the release marker; its new projectile appears at that same marker.</summary>
        public bool ProjectileVisible(int slot,float interpolationAlpha)
        {
            var shot=Projectiles[slot];if(!shot.Active)return false;
            if(shot.Pulse!=Equipment.Timeline.PulseId||shot.ContentId!=Equipment.EquippedId||!Equipment.Timeline.Running)return true;
            var p=Profile(shot.ContentId);float sampled=math.lerp(Equipment.Timeline.PreviousTick,Equipment.Timeline.Tick,math.saturate(interpolationAlpha));
            return sampled>=p.ReleaseTick;
        }
        public void ExpireProjectiles(){for(int i=0;i<Projectiles.Length;i++)if(Projectiles[i].Active&&Projectiles[i].RemainingTicks<=0)StopProjectile(i);}
        void TrySpawn(in WeaponProfile p,float2 point,float height,float scale)
        {
            for(int i=0;i<Projectiles.Length;i++)if(!Projectiles[i].Active)
            {
                Projectiles[i]=new WeaponProjectile{Active=true,ContentId=p.ContentId,RemainingTicks=p.ProjectileLifeTicks,Pulse=Equipment.Timeline.PulseId,SpawnTick=Tick,Position=point,Previous=point,Direction=Equipment.Aim,Height=height,Scale=scale,Damage=p.Damage};
                var s=Scopes[i+1];HitHistory.Begin(History,(i+1)*HistoryPerAttack,HistoryPerAttack,ref s,Owner,Equipment.Timeline.PulseId);Scopes[i+1]=s;return;
            }
            if(RejectedProjectiles<int.MaxValue)RejectedProjectiles++;
        }
        public void StopProjectile(int index){Projectiles[index]=default;ReleaseScope(index+1);}
        public HitRecordResult CheckHit(int scope,EntityHandle target)
        {var r=HitHistory.Check(History,scope*HistoryPerAttack,HistoryPerAttack,Scopes[scope],target);if(r==HitRecordResult.Full&&RejectedHits<int.MaxValue)RejectedHits++;return r;}
        public bool RecordHit(int scope,EntityHandle target,float2 point,float height=0)
        {
            var s=Scopes[scope];var result=HitHistory.TryRecord(History,scope*HistoryPerAttack,HistoryPerAttack,ref s,target);Scopes[scope]=s;
            if(result!=HitRecordResult.Added)return false;
            AcceptedHits++;var p=scope==0?Current:Profile(Projectiles[scope-1].ContentId);Emit(WeaponCueKind.Impact,point,scope==0?Equipment.Aim:Projectiles[scope-1].Direction,p,s.Pulse,height);return true;
        }
        void ReleaseScope(int i){var s=Scopes[i];HitHistory.Release(History,i*HistoryPerAttack,HistoryPerAttack,ref s);Scopes[i]=s;}
        public void CancelAll()
        {
            CollisionDebug.Clear();
            Equipment.Timeline.Stop();Equipment.BufferedAttack.Clear();Equipment.PendingId=Equipment.EquipRemaining=0;Equipment.Released=false;Equipment.Cues=WeaponCueKind.None;
            for(int i=0;i<Projectiles.Length;i++)Projectiles[i]=default;for(int i=0;i<Scopes.Length;i++)ReleaseScope(i);CueCount=0;
        }
        void Emit(WeaponCueKind kind,float2 position,float2 direction,in WeaponProfile p,uint pulse=0,float height=0)
        {
            Equipment.CueSequence=Equipment.CueSequence==uint.MaxValue?1:Equipment.CueSequence+1;Equipment.Cues|=kind;
            if(CueCount>=Cues.Length){for(int i=1;i<Cues.Length;i++)Cues[i-1]=Cues[i];CueCount--;if(RejectedCues<int.MaxValue)RejectedCues++;}
            Cues[CueCount++]=new WeaponCue{Owner=Owner,Sequence=Equipment.CueSequence,ActionPulse=pulse==0?Equipment.Timeline.PulseId:pulse,ContentId=p.ContentId,VisualId=p.VisualId,Kind=kind,Position=position,Direction=direction,Height=height};
        }
        public WeaponViewState View(float interpolationAlpha=1)
        {
            var p=Current;float t=math.max(0,math.lerp(Equipment.Timeline.PreviousTick,Equipment.Timeline.Tick,math.saturate(interpolationAlpha)));bool run=Equipment.Timeline.Running;
            var stage=Equipment.EquipRemaining>0?WeaponStage.Equipping:!run?WeaponStage.Idle:t<p.Active.From?WeaponStage.Windup:t<p.Active.Until?WeaponStage.Active:WeaponStage.Recovery;
            float progress=stage==WeaponStage.Equipping?1f-(float)Equipment.EquipRemaining/Profile(Equipment.PendingId).EquipTicks:stage==WeaponStage.Windup?(float)t/p.Active.From:stage==WeaponStage.Active?(float)(t-p.Active.From)/(p.Active.Until-p.Active.From):stage==WeaponStage.Recovery?(float)(t-p.Active.Until)/(p.DurationTicks-p.Active.Until):0;
            return new WeaponViewState{ContentId=p.ContentId,VisualId=p.VisualId,PendingContentId=Equipment.PendingId,Family=p.Family,Stage=stage,Phase=run?(float)t/p.DurationTicks:0,StagePhase=math.saturate(progress),ContactPhase=(float)p.Active.From/p.DurationTicks,ActiveEndPhase=(float)p.Active.Until/p.DurationTicks,ReleasePhase=(float)p.ReleaseTick/p.DurationTicks,AimDirection=Equipment.Aim,GripOffset=p.GripOffset,SecondaryGripOffset=p.SecondaryGripOffset,MuzzleOffset=p.MuzzleOffset,Reach=p.Reach,Radius=p.Radius,ActionPulse=Equipment.Timeline.PulseId,CueSequence=Equipment.CueSequence,Cues=Equipment.Cues};
        }
        public void OnReset()
        {
            CollisionDebug.Clear();
            Revision=Revision==uint.MaxValue?1:Revision+1;
            Equipment=new WeaponEquipment{EquippedId=m_Profiles[0].ContentId,Aim=new float2(1,0)};Owner=EntityHandle.Null;Tick=0;
            for(int i=0;i<Projectiles.Length;i++)Projectiles[i]=default;for(int i=0;i<History.Length;i++)History[i]=default;for(int i=0;i<Scopes.Length;i++)Scopes[i]=default;
            CueCount=RejectedProjectiles=RejectedHits=RejectedCues=Releases=AcceptedHits=0;
        }
        public void WriteSnapshot(BinaryWriter w)
        {
            w.Write(Magic);w.Write(2);w.Write(m_Fingerprint);w.Write(TickRate);w.Write(Projectiles.Length);w.Write(HistoryPerAttack);w.Write(Cues.Length);
            var saved=Equipment;saved.Cues=WeaponCueKind.None;NativeIO.WriteValue(w,saved);NativeIO.Write(w,Owner);w.Write(Tick);w.Write(RejectedProjectiles);w.Write(RejectedHits);w.Write(Releases);w.Write(AcceptedHits);
            NativeIO.Write(w,Projectiles);NativeIO.Write(w,History);NativeIO.Write(w,Scopes);
        }
        public void ReadSnapshot(BinaryReader r)
        {
            CollisionDebug.Clear();
            Revision=Revision==uint.MaxValue?1:Revision+1;
            if(r.ReadInt32()!=Magic||r.ReadInt32()!=2||r.ReadUInt32()!=m_Fingerprint||r.ReadInt32()!=TickRate||r.ReadInt32()!=Projectiles.Length||r.ReadInt32()!=HistoryPerAttack||r.ReadInt32()!=Cues.Length)throw new InvalidDataException("Weapon content or capacity differs from snapshot.");
            Equipment=NativeIO.ReadValue<WeaponEquipment>(r);Owner=NativeIO.ReadHandle(r);Tick=r.ReadInt64();RejectedProjectiles=r.ReadInt32();RejectedHits=r.ReadInt32();RejectedCues=0;Releases=r.ReadInt32();AcceptedHits=r.ReadInt32();
            NativeIO.ReadAll(r,Projectiles);NativeIO.ReadAll(r,History);NativeIO.ReadAll(r,Scopes);CueCount=0;Equipment.Cues=WeaponCueKind.None;
            if(!HasProfile(Equipment.EquippedId)||(Equipment.PendingId!=0&&!HasProfile(Equipment.PendingId))||Equipment.EquipRemaining<0||(Equipment.EquipRemaining>0&&(Equipment.PendingId==0||Equipment.EquipRemaining>Profile(Equipment.PendingId).EquipTicks))||Tick<0||RejectedProjectiles<0||RejectedHits<0||RejectedCues<0||Releases<0||AcceptedHits<0||!math.all(math.isfinite(Equipment.Aim))||math.lengthsq(Equipment.Aim)<.9f||math.lengthsq(Equipment.Aim)>1.1f||Equipment.Timeline.Tick<0||Equipment.Timeline.PreviousTick< -1||Equipment.Timeline.PreviousTick>Equipment.Timeline.Tick||(Equipment.Timeline.Running&&(Equipment.Timeline.PulseId==0||Equipment.Timeline.Tick>=Current.DurationTicks)))throw new InvalidDataException("Invalid weapon equipment state.");
            var buffered=Equipment.BufferedAttack;
            if(buffered.Pending&&(buffered.Command!=1||buffered.PressedAt<0||buffered.PressedAt>Tick||buffered.ExpiresAt<=buffered.PressedAt||buffered.ExpiresAt-buffered.PressedAt>TickRate/3))throw new InvalidDataException("Invalid weapon input buffer.");
            for(int i=0;i<Scopes.Length;i++)
            {
                var s=Scopes[i];if(!HitHistory.IsValid(History,i*HistoryPerAttack,HistoryPerAttack,s)||(s.Pulse==0&&(s.Count!=0||!s.Owner.IsNull))||(s.Pulse!=0&&s.Owner!=Owner))throw new InvalidDataException("Invalid weapon hit scope.");
                for(int j=0;j<s.Count;j++){var h=History[i*HistoryPerAttack+j];if(h.Index<0||h.Generation<=0)throw new InvalidDataException("Invalid weapon target.");for(int k=0;k<j;k++)if(History[i*HistoryPerAttack+k]==h)throw new InvalidDataException("Duplicate weapon target.");}
                if(i==0){if(s.Pulse!=(Equipment.Timeline.Running?Equipment.Timeline.PulseId:0))throw new InvalidDataException("Weapon timeline and contact history differ.");continue;}var p=Projectiles[i-1];
                if(!p.Active&&s.Pulse!=0)throw new InvalidDataException("Inactive projectile retains hit history.");if(p.Active&&(!HasProfile(p.ContentId)||!Profile(p.ContentId).Ranged||p.SpawnTick<1||p.SpawnTick>Tick||p.RemainingTicks<0||p.RemainingTicks>Profile(p.ContentId).ProjectileLifeTicks||p.Pulse==0||s.Pulse!=p.Pulse||!math.all(math.isfinite(p.Position))||!math.all(math.isfinite(p.Previous))||!math.all(math.isfinite(p.Direction))||math.lengthsq(p.Direction)<.999f||math.lengthsq(p.Direction)>1.001f||!math.isfinite(p.Scale)||p.Scale<=0||p.Scale>4||!math.isfinite(p.Height)||p.Height<0||!math.isfinite(p.Damage)||p.Damage<0))throw new InvalidDataException("Invalid weapon projectile.");
            }
        }
        public void Dispose(){Projectiles.Dispose();History.Dispose();Scopes.Dispose();Cues.Dispose();CollisionDebug.Dispose();}
    }
}
