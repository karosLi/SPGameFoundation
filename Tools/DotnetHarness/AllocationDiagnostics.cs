// Linux .NET-only diagnostic: real production pose calls plus an independent integer sentinel.
// GC allocation ticks are sampled; their absence alone is not proof of zero allocations.
// All measurement records and EventListener storage are allocated before measured windows.
using System.Diagnostics;
using System.Diagnostics.Tracing;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using SPF.Testing;
using SPF.L1.Skeleton;
using SPF.Presentation.Animation;
using Unity.Collections;
using Unity.Mathematics;

int ownerThread = Native.gettid();
var mode = args.Length == 0 ? "pose" : args[0];
var concurrent = args.Length > 1 && args[1] != "none";
var blocking = args.Length > 1 && args[1] == "blocking";
using var trace = new GcEvents();
using var ready = new ManualResetEventSlim();
using var stop = new CancellationTokenSource();
var worker = new Thread(() => {
    var ring = new byte[16][];
    for (int i=0;i<ring.Length;i++) ring[i] = new byte[1024*1024];
    var roots = new object[262144];
    for (int i=0;i<roots.Length;i++) roots[i] = new byte[32];
    GC.Collect(2, GCCollectionMode.Forced, true, false);
    ready.Set(); int ri=0;
    while(!stop.IsCancellationRequested) {
        if(concurrent) { ring[ri++ % ring.Length] = new byte[1024*1024]; GC.Collect(2,GCCollectionMode.Forced,blocking,false); }
        Thread.Sleep(4);
    }
    GC.KeepAlive(ring); GC.KeepAlive(roots);
});
using var rig = NaturalCharacterRig.Create();
using var local = new NativeArray<BoneLocal>(14,Allocator.Temp);
using var world = new NativeArray<BoneWorld>(14,Allocator.Temp);
FootPlantState foot=default; SmoothedAimState aim=default;
void Run(){NaturalMotion.StepFoot(ref foot,0,0,0,.016f,0);NaturalMotion.SmoothAim(ref aim,new float2(.7f,1.7f),.016f);NaturalCharacterRig.Pose(rig.View,local,world,0,1,1,.2f,new float2(-.2f,.075f),new float2(.2f,.075f),true,aim.Target,0);}
for(int i=0;i<100;i++) Run();
Action operation = mode == "empty" ? Empty.Run : () => {for(int i=0;i<1000;i++) Run();};
var records = new Record[256];
worker.Start(); ready.Wait();
for(int i=0;i<records.Length;i++) {
    using var probe = new ManagedAllocationProbe();
    try {
        var before = probe.Calibrate();
        records[i].Start = DateTime.UtcNow;
        var sample = probe.Measure(operation);
        records[i].End = DateTime.UtcNow;
        var after = probe.Calibrate();
        records[i].Bytes=sample.Value; records[i].Collections=sample.Collections;
        records[i].PositiveBefore=before.RetainedArrays.Value; records[i].PositiveAfter=after.RetainedArrays.Value;
        records[i].EmptyBefore=before.Empty.Value; records[i].EmptyAfter=after.Empty.Value;
    }
    catch (InvalidOperationException error) when (error.Message.StartsWith("Allocation measurement unavailable:")) {
        // Preserve the failed calibration as unavailable. Never retry or convert it to zero.
        records[i].Unavailable=error.Message;
    }
}
stop.Cancel(); worker.Join(); Thread.Sleep(100);
trace.Dispose();
if (trace.Used == trace.Entries.Length) throw new InvalidOperationException("GC event capture buffer exhausted.");
Console.WriteLine($"runtime={RuntimeInformation.FrameworkDescription}; arch={RuntimeInformation.ProcessArchitecture}; mode={mode}; background={concurrent}; serverGC={System.Runtime.GCSettings.IsServerGC}; recordedEvents={trace.Used}; ownerOSThread={ownerThread}; latency={System.Runtime.GCSettings.LatencyMode}; blockingRequested={blocking}");
for(int i=0;i<records.Length;i++) {
    var r=records[i];
    if (r.Unavailable != null) { Console.WriteLine($"sample={i} unavailable={r.Unavailable}"); continue; }
    int events=0, allocs=0; string names="";
    for(int j=0;j<trace.Used;j++) {var e=trace.Entries[j]; if(e.Time>=r.Start && e.Time<=r.End) {events++; if(e.Name.Contains("Allocation") && e.Thread==ownerThread)allocs++; names+=e.Name+"(t="+e.Thread+",type="+e.Type+");";}}
    Console.WriteLine($"sample={i} bytes={r.Bytes} gen0={r.Collections} controls={r.PositiveBefore}/{r.EmptyBefore},{r.PositiveAfter}/{r.EmptyAfter} gcEvents={events} ownerAllocationTicks={allocs} events={names}");
}
Console.WriteLine($"nonzero={records.Count(r=>r.Unavailable==null && r.Bytes!=0)}/{records.Count(r=>r.Unavailable==null)}; unavailable={records.Count(r=>r.Unavailable!=null)}");
struct Record {public long Bytes,PositiveBefore,PositiveAfter,EmptyBefore,EmptyAfter; public int Collections; public DateTime Start,End; public string Unavailable;}
static class Empty {
    static uint Sink=1;
    [MethodImpl(MethodImplOptions.NoInlining)] public static void Run() {for(int i=0;i<1000000;i++) Sink=unchecked(Sink*1664525+1013904223);}
}
sealed class GcEvents:EventListener {
    public readonly (DateTime Time,string Name,long Thread,string Type)[] Entries=new (DateTime,string,long,string)[65536];
    public int Used;
    protected override void OnEventSourceCreated(EventSource source) { if(source.Name=="Microsoft-Windows-DotNETRuntime") EnableEvents(source, EventLevel.Verbose, (EventKeywords)1); }
    protected override void OnEventWritten(EventWrittenEventArgs data) {if(Entries==null || Used==Entries.Length)return; string type=""; if(data.PayloadNames != null) for(int i=0;i<data.PayloadNames.Count;i++) if(data.PayloadNames[i]=="TypeName" || data.PayloadNames[i]=="Type") type=data.Payload[i]?.ToString(); Entries[Used++]=(data.TimeStamp,data.EventName,data.OSThreadId,type);}
}

static class Native { [DllImport("libc")] public static extern int gettid(); }
