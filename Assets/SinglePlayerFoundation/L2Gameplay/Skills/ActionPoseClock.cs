using System;
using System.IO;
using SPF.Contracts;
using SPF.L2.Combat;
using Unity.Mathematics;

namespace SPF.L2.Skills
{
    /// <summary>Optional authoritative timing for an instant skill's finite visual action. The caller
    /// supplies an authored pose content ID and duration (at least three ticks, retaining one visible
    /// interpolated interval between the rest endpoints). This never activates skills, deals damage,
    /// owns input or references presentation. Install only in modes that save this additional schema.</summary>
    public sealed class ActionPoseClock : IResettableResource, ISnapshotResource
    {
        const int Magic=0x41504331;
        public int ContentId { get; private set; }
        public int DurationTicks { get; private set; }
        public ActionTimeline Timeline;
        public uint Revision { get; private set; }
        public bool Running=>Timeline.Running;
        public void Advance(bool playing,bool alive)
        {
            if(!alive){Cancel();return;}
            if(!playing||!Timeline.Running)return;
            Timeline.Advance();if(Timeline.Tick>=DurationTicks)Timeline.Stop();
        }
        public void Begin(int contentId,int durationTicks)
        {
            if(contentId<=0||durationTicks<3||durationTicks>3600)throw new ArgumentOutOfRangeException(nameof(contentId));
            ContentId=contentId;DurationTicks=durationTicks;Timeline.Begin();
        }
        public float Phase(float alpha=1)=>Timeline.Running?math.saturate(math.lerp(math.max(0,Timeline.PreviousTick),Timeline.Tick,math.saturate(alpha))/math.max(1,DurationTicks)):1;
        public void Cancel()=>Timeline.Stop();
        public void OnReset(){ContentId=DurationTicks=0;Timeline=default;Revision=Revision==uint.MaxValue?1:Revision+1;}
        public void WriteSnapshot(BinaryWriter w)
        {
            w.Write(Magic);w.Write(1);w.Write(ContentId);w.Write(DurationTicks);w.Write(Timeline.PreviousTick);w.Write(Timeline.Tick);w.Write(Timeline.PulseId);w.Write(Timeline.Running);
        }
        public void ReadSnapshot(BinaryReader r)
        {
            Revision=Revision==uint.MaxValue?1:Revision+1;
            if(r.ReadInt32()!=Magic||r.ReadInt32()!=1)throw new InvalidDataException("Unsupported action pose clock.");
            ContentId=r.ReadInt32();DurationTicks=r.ReadInt32();Timeline=new ActionTimeline{PreviousTick=r.ReadInt32(),Tick=r.ReadInt32(),PulseId=r.ReadUInt32(),Running=r.ReadBoolean()};
            if(ContentId<0||DurationTicks<0||(DurationTicks>0&&DurationTicks<3)||DurationTicks>3600||Timeline.Tick<0||Timeline.PreviousTick< -1||Timeline.PreviousTick>Timeline.Tick||
                (Timeline.Running&&(ContentId==0||DurationTicks==0||Timeline.PulseId==0||Timeline.Tick>=DurationTicks)))throw new InvalidDataException("Invalid action pose clock state.");
        }
    }
}
