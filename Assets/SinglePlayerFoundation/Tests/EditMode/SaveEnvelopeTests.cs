using System;
using System.IO;
using System.Text;
using System.Reflection;
using System.Security.Cryptography;
using NUnit.Framework;
using SPF.Contracts;
using SPF.Runtime.Composition;
using SPF.Runtime.Scheduling;
using SPF.Runtime.Session;
using SPF.Runtime.World;
using UnityEngine;

namespace SPF.Tests.EditMode
{
    public class SaveEnvelopeTests
    {
        static readonly TableKey Table = new TableKey("Envelope.Items");
        static readonly ColumnKey<int> A = new ColumnKey<int>(Table, "A");
        static readonly ColumnKey<int> B = new ColumnKey<int>(Table, "B");
        public sealed class Module : GameplayModuleAsset
        {
            public bool Reverse, Systems, ReverseSystems;
            public int Capacity=4;
            public override string Id => "envelope.test";
            public override void DeclareData(WorldLayout l)
            { var t = l.Table(Table, Capacity); if (Reverse) t.Column(B).Column(A); else t.Column(A).Column(B); }
            public override void RegisterSystems(SystemRegistry r)
            {if(!Systems)return;if(ReverseSystems)r.Add(new OtherSystem()).Add(new TestSystem());else r.Add(new TestSystem()).Add(new OtherSystem());}
        }
        sealed class Owned : IDisposable
        {
            public readonly Module Module;
            public readonly ModeDefinition Mode;
            public readonly SimSession Session;
            public Owned(bool reverse = false, int rate = 30, int capacity=4, uint seed=7, bool systems=false, bool reverseSystems=false)
            {
                Module=ScriptableObject.CreateInstance<Module>();Module.Reverse=reverse;Module.Capacity=capacity;Module.Systems=systems;Module.ReverseSystems=reverseSystems;
                var settings=SessionSettings.Default;settings.TickRate=rate;
                Mode=ModeDefinition.Create(new[]{Module},settings);Session=SimSession.Create(Mode,seed);
            }
            public void Dispose(){Session.Dispose();UnityEngine.Object.DestroyImmediate(Mode);UnityEngine.Object.DestroyImmediate(Module);}
        }
        static SaveCompatibilityDescriptor Describe(SimSession session, bool reverse=false, int version=1, int rule=7, string runtime="test.same-runtime.v1", string contract="contract.v1", byte visual=0, string mode="test.mode", byte raw=1)
        {
            var columns=new[]{new SnapshotMemberSchema(A,"int.hp",version),new SnapshotMemberSchema(B,"int.xp")};
            if(reverse)Array.Reverse(columns);
            byte[] schema=SaveCompatibilityDescriptor.Encode(w=>session.World.WriteSnapshotSchema(w,
                new[]{new SnapshotTableSchema(Table,"items.v1",false,false,columns)},
                new[]{new SnapshotResourceSchema(SimWorld.DestroyQueueKey,"destroy.sorted",SnapshotResourcePolicy.Snapshot)}));
            byte[] content=SaveCompatibilityDescriptor.Encode(w=>{WeaponSaveContent.WriteSession(w,session);w.Write(rule);});
            return new SaveCompatibilityDescriptor(mode,contract,runtime,schema,content,new[]{visual},new[]{raw});
        }
        static byte[] Capture(SimSession s,SaveCompatibilityDescriptor d=null)=>SaveEnvelope.Capture(s,d??Describe(s));
        static void Restore(byte[] data,SimSession s,SaveCompatibilityDescriptor d=null,int max=SaveEnvelope.DefaultMaxPayloadBytes)
        {using var stream=new MemoryStream(data,false);SaveEnvelope.Restore(stream,s,d??Describe(s),max);}
        static void Populate(SimSession s)
        {s.World.CreateEntity(Table,out int row);var a=s.World.Column(A);var b=s.World.Column(B);a[row]=17;b[row]=42;s.Step();}
        static void RejectUnchanged(byte[] data,SimSession s,SaveCompatibilityDescriptor d=null,int max=SaveEnvelope.DefaultMaxPayloadBytes)
        {
            s.Start();s.Pause();s.RequestTicks(3);int pending=s.PendingTicks;byte[] before=s.CaptureSnapshot();uint revision=s.TimelineRevision;
            Assert.That(()=>Restore(data,s,d,max),Throws.TypeOf<InvalidDataException>().Or.TypeOf<EndOfStreamException>());
            Assert.AreEqual(SessionState.Paused,s.State);Assert.AreEqual(pending,s.PendingTicks);Assert.AreEqual(revision,s.TimelineRevision);
            CollectionAssert.AreEqual(before,s.CaptureSnapshot());
        }
        [Test] public void EqualSizedColumnReorderMustRejectBeforeChangingLiveState()
        {
            using var a=new Owned();using var b=new Owned(true);Populate(a.Session);Populate(b.Session);
            RejectUnchanged(Capture(a.Session),b.Session,Describe(b.Session,true));
            Assert.Throws<InvalidDataException>(()=>Describe(b.Session));
        }
        [Test] public void TrailingBytesMustRejectBeforeChangingLiveState()
        {using var a=new Owned();Populate(a.Session);byte[] bytes=Capture(a.Session);Array.Resize(ref bytes,bytes.Length+1);RejectUnchanged(bytes,a.Session);}
        [Test] public void RoundTripKeepsRawPayloadAndInvalidatesSameTickTimeline()
        {
            using var a=new Owned();Populate(a.Session);var s=a.Session;byte[] raw=s.CaptureSnapshot();byte[] bytes=Capture(s);
            using(var stream=new MemoryStream(bytes))CollectionAssert.AreEqual(raw,SaveEnvelope.ReadPayload(stream,Describe(s)));
            uint revision=s.TimelineRevision;uint tick=s.Clock.NextTickIndex;s.RequestTicks(2);Restore(bytes,s);
            Assert.AreEqual(tick,s.Clock.NextTickIndex);Assert.AreEqual(revision+1,s.TimelineRevision);Assert.AreEqual(0,s.PendingTicks);CollectionAssert.AreEqual(raw,s.CaptureSnapshot());
        }
        [TestCase(0)][TestCase(1)][TestCase(4)][TestCase(8)][TestCase(15)][TestCase(100)][TestCase(SaveEnvelope.HeaderBytes-1)]
        public void TruncatedHeaderRejectsBeforeMutation(int length)
        {using var a=new Owned();Populate(a.Session);byte[] b=Capture(a.Session);Array.Resize(ref b,length);RejectUnchanged(b,a.Session);}
        [Test] public void EveryPayloadTruncationRejectsBeforeMutation()
        {
            using var a=new Owned();Populate(a.Session);byte[] b=Capture(a.Session);
            for(int n=SaveEnvelope.HeaderBytes;n<b.Length;n++){byte[] cut=new byte[n];Array.Copy(b,cut,n);RejectUnchanged(cut,a.Session);}
        }
        [TestCase(0)][TestCase(4)][TestCase(8)][TestCase(SaveEnvelope.HeaderBytes-1)][TestCase(SaveEnvelope.HeaderBytes)]
        public void UnknownVersionsMagicAndIntegrityCorruptionReject(int offset)
        {using var a=new Owned();Populate(a.Session);byte[] b=Capture(a.Session);b[offset]^=0x40;RejectUnchanged(b,a.Session);}
        [TestCase(-1)][TestCase(0)][TestCase(int.MaxValue)][TestCase(SaveEnvelope.AbsoluteMaxPayloadBytes+1)]
        public void MaliciousPayloadLengthsRejectBeforeReadingPayload(int length)
        {
            using var a=new Owned();byte[] b=Capture(a.Session);Array.Copy(BitConverter.GetBytes(length),0,b,12,4);
            using var source=new ChunkStream(b,SaveEnvelope.HeaderBytes);
            Assert.Throws<InvalidDataException>(()=>SaveEnvelope.Restore(source,a.Session,Describe(a.Session)));
            Assert.AreEqual(SaveEnvelope.HeaderBytes,source.ReadCount);
        }
        [Test] public void ContentSchemaRuntimeAndContractMismatchesRejectBeforeMutation()
        {
            using var a=new Owned();Populate(a.Session);var s=a.Session;byte[] b=Capture(s);
            RejectUnchanged(b,s,Describe(s,version:2));RejectUnchanged(b,s,Describe(s,rule:8));
            RejectUnchanged(b,s,Describe(s,mode:"other.mode"));RejectUnchanged(b,s,Describe(s,raw:2));
            RejectUnchanged(b,s,Describe(s,runtime:"other.abi"));RejectUnchanged(b,s,Describe(s,contract:"contract.v2"));
            using var faster=new Owned(rate:60);RejectUnchanged(b,faster.Session);
            using var smaller=new Owned(capacity:2);RejectUnchanged(b,smaller.Session);
            using var seed=new Owned(seed:8);RejectUnchanged(b,seed.Session);
        }
        [Test] public void VisualIdentityIsSeparateAndDescriptorOwnsInput()
        {
            using var a=new Owned();byte[] b=Capture(a.Session);Restore(b,a.Session,Describe(a.Session,visual:1));
            byte[] input={1,2};var d=new SaveCompatibilityDescriptor("mode","contract","runtime",input,input,input,input);
            string before=d.SchemaFingerprint;input[0]=9;Assert.AreEqual(before,d.SchemaFingerprint);
            Assert.AreNotEqual(Describe(a.Session).VisualFingerprint,Describe(a.Session,visual:1).VisualFingerprint);
            Assert.AreEqual(Describe(a.Session).ContentFingerprint,Describe(a.Session,visual:1).ContentFingerprint);
        }
        [Test] public void ExactLimitsAndChunkedNonSeekableStreamsWork()
        {
            using var a=new Owned();Populate(a.Session);byte[] b=Capture(a.Session);int size=b.Length-SaveEnvelope.HeaderBytes;
            CollectionAssert.AreEqual(b,SaveEnvelope.Capture(a.Session,Describe(a.Session),size));
            Assert.Throws<InvalidDataException>(()=>SaveEnvelope.Capture(a.Session,Describe(a.Session),size-1));RejectUnchanged(b,a.Session,max:size-1);
            using var stream=new ChunkStream(b);SaveEnvelope.Restore(stream,a.Session,Describe(a.Session),size);Assert.AreEqual(b.Length,stream.ReadCount);
            Array.Resize(ref b,b.Length+1);using var extra=new ChunkStream(b);Assert.Throws<InvalidDataException>(()=>SaveEnvelope.Restore(extra,a.Session,Describe(a.Session),size));
        }
        [Test] public void HeaderVisualCorruptionStillFailsIntegrity()
        {using var a=new Owned();byte[] b=Capture(a.Session);b[16+4*32]^=1;RejectUnchanged(b,a.Session);}
        [Test] public void PayloadFailureUsesExistingRawRestartSemantics()
        {
            using var a=new Owned();Populate(a.Session);var s=a.Session;s.Start();s.Pause();s.RequestTicks(4);uint revision=s.TimelineRevision;
            byte[] b=Capture(s);b[SaveEnvelope.HeaderBytes+12]^=1;Rechecksum(b);
            Assert.Throws<InvalidDataException>(()=>Restore(b,s));Assert.AreEqual(0,s.World.Table(Table).Count);Assert.AreEqual(0,s.Clock.NextTickIndex);
            Assert.AreEqual(SessionState.Paused,s.State);Assert.AreEqual(0,s.PendingTicks);Assert.AreEqual(revision+1,s.TimelineRevision);
        }
        [TestCase(SessionState.Created,false,false)]
        [TestCase(SessionState.Running,false,false)]
        [TestCase(SessionState.Paused,true,false)]
        [TestCase(SessionState.Running,false,true)]
        [TestCase(SessionState.Running,true,true)]
        [TestCase(SessionState.Paused,true,true)]
        [TestCase(SessionState.Created,true,true)]
        public void ChecksummedPayloadSuffixRestartsAndPreservesExactPauseReasons(SessionState state,bool manual,bool hostSuspended)
        {
            using var a=new Owned();var s=a.Session;Populate(s);s.ManualClock=manual;
            if(state!=SessionState.Created)s.Start();if(state==SessionState.Paused)s.Pause();SetHost(s,hostSuspended);
            var effective=s.State;s.RequestTicks(5);uint revision=s.TimelineRevision;
            byte[] bytes=Capture(s);Array.Resize(ref bytes,bytes.Length+1);bytes[bytes.Length-1]=0x99;
            Array.Copy(BitConverter.GetBytes(bytes.Length-SaveEnvelope.HeaderBytes),0,bytes,12,4);Rechecksum(bytes);
            Assert.Throws<InvalidDataException>(()=>Restore(bytes,s));Assert.AreEqual(effective,s.State);
            Assert.AreEqual(0,s.World.Table(Table).Count);Assert.AreEqual(0,s.PendingTicks);Assert.AreEqual(revision+2,s.TimelineRevision);
            Assert.AreEqual(manual,s.ManualClock);SetHost(s,false);Assert.AreEqual(state,s.State);
        }
        [Test] public void PrevalidationPreservesHostSuspensionAndDoesNotCompleteOutstandingTick()
        {
            using var a=new Owned();var s=a.Session;s.ManualClock=true;s.Start();byte[] bytes=Capture(s);bytes[0]^=1;
            s.RequestTicks();s.Update(0);Assert.IsTrue(s.Pipeline.HasPendingTick);uint revision=s.TimelineRevision;
            Assert.Throws<InvalidDataException>(()=>Restore(bytes,s));Assert.IsTrue(s.Pipeline.HasPendingTick);Assert.AreEqual(revision,s.TimelineRevision);
            s.Sync();SetHost(s,true);s.RequestTicks(3);Assert.Throws<InvalidDataException>(()=>Restore(bytes,s));
            Assert.AreEqual(SessionState.Paused,s.State);Assert.AreEqual(3,s.PendingTicks);SetHost(s,false);Assert.AreEqual(SessionState.Running,s.State);
        }
        static void SetHost(SimSession s,bool suspended)=>typeof(SimSession).GetMethod("SetHostSuspended",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(s,new object[]{suspended});

        [Test] public void LegacyImportIsExplicitTemporaryValidatedAndLeavesOriginalAndLiveSessionAlone()
        {
            using var a=new Owned();Populate(a.Session);var s=a.Session;var descriptor=Describe(s);byte[] raw=s.CaptureSnapshot();uint revision=s.TimelineRevision;
            byte[] imported;using(var stream=new MemoryStream(raw,false))imported=SaveEnvelope.ImportKnownLegacy(stream,new KnownLegacySaveDescriptor("known-test-raw.v1",descriptor),a.Mode,7,t=>Describe(t));
            Assert.AreEqual(revision,s.TimelineRevision);CollectionAssert.AreEqual(raw,s.CaptureSnapshot());Restore(imported,s);CollectionAssert.AreEqual(raw,s.CaptureSnapshot());
            byte[] bad=(byte[])raw.Clone();bad[12]^=1;byte[] original=(byte[])bad.Clone();
            using(var stream=new MemoryStream(bad,false))Assert.Throws<InvalidDataException>(()=>SaveEnvelope.ImportKnownLegacy(stream,new KnownLegacySaveDescriptor("known-test-raw.v1",descriptor),a.Mode,7,t=>Describe(t)));
            CollectionAssert.AreEqual(original,bad);CollectionAssert.AreEqual(raw,s.CaptureSnapshot());
            using(var stream=new MemoryStream(raw,false))Assert.Throws<InvalidDataException>(()=>SaveEnvelope.ImportKnownLegacy(stream,new KnownLegacySaveDescriptor("wrong-known-rules",Describe(s,rule:9)),a.Mode,7,t=>Describe(t)));
            Assert.Throws<ArgumentException>(()=>new KnownLegacySaveDescriptor("",descriptor));
        }
        [Test] public void NoImplicitLegacyFallbackOrUnboundedDescriptorInput()
        {
            using var a=new Owned();RejectUnchanged(a.Session.CaptureSnapshot(),a.Session);
            Assert.Throws<ArgumentException>(()=>new SaveCompatibilityDescriptor("m","c","r",new byte[65537],new byte[0],new byte[0],new byte[0]));
            Assert.Throws<InvalidDataException>(()=>SaveCompatibilityDescriptor.Encode(w=>w.Write(new byte[65537])));
            Assert.Throws<ArgumentOutOfRangeException>(()=>SaveEnvelope.Capture(a.Session,Describe(a.Session),0));
        }
        [Test] public void SystemIdentityVersionAndExecutionOrderAreExplicit()
        {
            using var a=new Owned();
            Assert.Throws<InvalidDataException>(()=>SaveCompatibilityDescriptor.Encode(w=>SaveCompatibilityDescriptor.WriteSystemSchema(w,a.Session.Pipeline,new SnapshotSystemSchema(typeof(TestSystem),"test.system"))));
            Assert.Throws<ArgumentException>(()=>new SnapshotSystemSchema(typeof(int),"bad"));
            Assert.Throws<ArgumentOutOfRangeException>(()=>new SnapshotMemberSchema(A,"hp",0));
        }
        [Test] public void ReorderedAndReversionedSystemsHaveDifferentSchemaIdentities()
        {
            using var a=new Owned(systems:true);using var b=new Owned(systems:true,reverseSystems:true);
            var first=new SnapshotSystemSchema(typeof(TestSystem),"test.first");var second=new SnapshotSystemSchema(typeof(OtherSystem),"test.second");
            byte[] normal=SaveCompatibilityDescriptor.Encode(w=>SaveCompatibilityDescriptor.WriteSystemSchema(w,a.Session.Pipeline,first,second));
            byte[] reordered=SaveCompatibilityDescriptor.Encode(w=>SaveCompatibilityDescriptor.WriteSystemSchema(w,b.Session.Pipeline,second,first));
            byte[] versioned=SaveCompatibilityDescriptor.Encode(w=>SaveCompatibilityDescriptor.WriteSystemSchema(w,a.Session.Pipeline,new SnapshotSystemSchema(typeof(TestSystem),"test.first",2),second));
            CollectionAssert.AreNotEqual(normal,reordered);CollectionAssert.AreNotEqual(normal,versioned);
            Assert.Throws<InvalidDataException>(()=>SaveCompatibilityDescriptor.Encode(w=>SaveCompatibilityDescriptor.WriteSystemSchema(w,b.Session.Pipeline,first,second)));
            Assert.Throws<InvalidDataException>(()=>SaveCompatibilityDescriptor.Encode(w=>SaveCompatibilityDescriptor.WriteSystemSchema(w,a.Session.Pipeline,first,new SnapshotSystemSchema(typeof(OtherSystem),"test.first"))));
        }
        [Test] public void InvalidSchemaCoverageScopeAndAmbiguousStableNamesReject()
        {
            using var a=new Owned();
            Assert.Throws<InvalidDataException>(()=>SaveCompatibilityDescriptor.Encode(w=>a.Session.World.WriteSnapshotSchema(w,new SnapshotTableSchema[0],new SnapshotResourceSchema[0])));
            Assert.Throws<InvalidDataException>(()=>SaveCompatibilityDescriptor.Encode(w=>a.Session.World.WriteSnapshotSchema(w,
                new[]{new SnapshotTableSchema(Table,"items",true,false,new SnapshotMemberSchema(A,"hp"),new SnapshotMemberSchema(B,"xp"))},
                new[]{new SnapshotResourceSchema(SimWorld.DestroyQueueKey,"destroy",SnapshotResourcePolicy.Snapshot)})));
            var alias=new ColumnKey<int>(Table,"A");
            Assert.Throws<ArgumentException>(()=>new SnapshotTableSchema(Table,"items",false,false,new SnapshotMemberSchema(A,"hp"),new SnapshotMemberSchema(alias,"xp")));
        }
        sealed class OtherSystem:SimSystemBase
        {public override SimPhase Phase=>SimPhase.Input;public override void Declare(AccessDeclaration a){} public override Unity.Jobs.JobHandle OnTick(in SimContext c,Unity.Jobs.JobHandle d)=>d;}
        sealed class TestSystem:SimSystemBase
        {public override SimPhase Phase=>SimPhase.Input;public override void Declare(AccessDeclaration a){} public override Unity.Jobs.JobHandle OnTick(in SimContext c,Unity.Jobs.JobHandle d)=>d;}
        static void Rechecksum(byte[] bytes)
        {
            using var hash=SHA256.Create();hash.TransformBlock(bytes,0,SaveEnvelope.HeaderBytes-32,bytes,0);
            hash.TransformFinalBlock(bytes,SaveEnvelope.HeaderBytes,bytes.Length-SaveEnvelope.HeaderBytes);
            Array.Copy(hash.Hash,0,bytes,SaveEnvelope.HeaderBytes-32,32);
        }
        sealed class ChunkStream:Stream
        {
            readonly byte[] bytes;readonly int stop;int position;
            public int ReadCount=>position;
            public ChunkStream(byte[] bytes,int stop=int.MaxValue){this.bytes=bytes;this.stop=stop;}
            public override int Read(byte[] buffer,int offset,int count)
            {if(position>=stop)throw new InvalidOperationException("Parser read beyond allowed header.");int n=Math.Min(Math.Min(count,3),bytes.Length-position);Array.Copy(bytes,position,buffer,offset,n);position+=n;return n;}
            public override int ReadByte(){if(position>=bytes.Length)return -1;if(position>=stop)throw new InvalidOperationException();return bytes[position++];}
            public override bool CanRead=>true;public override bool CanSeek=>false;public override bool CanWrite=>false;
            public override long Length=>throw new NotSupportedException();public override long Position{get=>throw new NotSupportedException();set=>throw new NotSupportedException();}
            public override void Flush(){}public override long Seek(long offset,SeekOrigin origin)=>throw new NotSupportedException();public override void SetLength(long value)=>throw new NotSupportedException();public override void Write(byte[] b,int o,int c)=>throw new NotSupportedException();
        }
    }
}
