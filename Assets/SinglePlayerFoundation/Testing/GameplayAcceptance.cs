using System;
using SPF.Contracts;
using SPF.Runtime.Session;
using SPF.Runtime.World;

namespace SPF.Testing
{
    /// <summary>Test-only adapter: one real factory, fixed inputs and explicit optional capabilities.
    /// No per-entity interfaces, game enum additions, reflection or alternate simulation loop.</summary>
    public sealed class GameplayAcceptanceCase : IDisposable
    {
        public readonly string Name;
        public readonly SimSession Session;
        public readonly TableKey IdentityTable;
        public readonly Action BeginPlay;
        public readonly Action<int> Input;
        public readonly Action CancelInput;
        public readonly Func<bool> InputIsClear;
        public readonly Action<int, int> Present;
        public readonly string PresentationScope;
        public readonly string UnsupportedViews;
        public readonly Func<byte[]> ReadState;
        public readonly Action<byte[]> RestoreState;
        public readonly string SaveScope;
        public readonly Func<int> SpawnStableIdentity;
        public readonly Func<int, int> ReadStableIdentity;
        readonly Action m_ReleaseAssets;
        bool m_Disposed;

        public GameplayAcceptanceCase(string name, SimSession session, TableKey identityTable,
            Action beginPlay, Action<int> input, Action cancelInput, Func<bool> inputIsClear,
            Action<int, int> present, string presentationScope, string unsupportedViews,
            Action releaseAssets = null, Func<int> spawnStableIdentity = null, Func<int, int> readStableIdentity = null,
            Func<byte[]> readState = null, Action<byte[]> restoreState = null, string unsupportedSave = null)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Session = session ?? throw new ArgumentNullException(nameof(session));
            IdentityTable = identityTable ?? throw new ArgumentNullException(nameof(identityTable));
            BeginPlay = beginPlay ?? throw new ArgumentNullException(nameof(beginPlay));
            Input = input ?? throw new ArgumentNullException(nameof(input));
            CancelInput = cancelInput ?? throw new ArgumentNullException(nameof(cancelInput));
            InputIsClear = inputIsClear ?? throw new ArgumentNullException(nameof(inputIsClear));
            Present = present;
            if (string.IsNullOrWhiteSpace(presentationScope) || string.IsNullOrWhiteSpace(unsupportedViews))
                throw new ArgumentException("Supported scope or an explicit unsupported reason is required.");
            PresentationScope = presentationScope; UnsupportedViews = unsupportedViews;
            m_ReleaseAssets = releaseAssets;
            SpawnStableIdentity = spawnStableIdentity; ReadStableIdentity = readStableIdentity;
            if (unsupportedSave != null && (string.IsNullOrWhiteSpace(unsupportedSave) || readState == null))
                throw new ArgumentException("Unsupported save needs a reason and an independent state reader.");
            ReadState = readState ?? session.CaptureSnapshot;
            RestoreState = unsupportedSave == null ? restoreState ?? session.RestoreSnapshot : null;
            SaveScope = unsupportedSave ?? "Same-layout legacy SimSession snapshot/replay; not portable schema/envelope migration.";
        }

        public void Start() { Session.Start(); BeginPlay(); }
        public void Tick(int inputIndex) { Input(inputIndex); Session.Step(); }
        public void Dispose()
        {
            if (m_Disposed) return;
            // Keep assets owned if pipeline disposal rejects pending work. A later retry remains possible.
            Session.Dispose(); m_ReleaseAssets?.Invoke(); m_Disposed = true;
        }
    }

    /// <summary>Assertions over the existing contracts, called by ordinary NUnit fixtures.
    /// Each check owns fresh factory results; destructive capacity probes never contaminate gameplay.</summary>
    public static class GameplayAcceptance
    {
        static void Require(bool value, string message)
        { if (!value) throw new InvalidOperationException("Gameplay acceptance: " + message); }

        public static void EqualBytes(byte[] a, byte[] b, string scope)
        {
            Require(a.Length == b.Length, scope + ": checkpoint lengths differ");
            for (int i = 0; i < a.Length; i++)
                if (a[i] != b[i]) throw new InvalidOperationException(scope + ": checkpoint bytes differ at " + i);
        }

        public static void Lifecycle(Func<GameplayAcceptanceCase> factory)
        {
            using var a = factory(); var s = a.Session;
            Require(s.State == SessionState.Created, "factory must return a Created session");
            s.Start(); s.Update(1f / s.Clock.TickRate); s.Sync();
            s.Pause(); uint tick = s.Clock.NextTickIndex;
            s.Update(10); Require(s.Clock.NextTickIndex == tick, "paused Update advanced");
            s.Resume(); s.Step(); Require(s.Clock.NextTickIndex > tick, "resume did not advance");
            s.RequestTicks(2); uint revision = s.TimelineRevision; s.Restart();
            Require(s.PendingTicks == 0 && s.Clock.NextTickIndex == 0, "restart retained clock/input requests");
            Require(s.TimelineRevision != revision, "restart did not invalidate view timeline");
            a.Dispose(); a.Dispose(); Require(s.State == SessionState.Disposed, "factory lease did not dispose");
        }

        public static void CapacityIdentityAndDeferredStructure(Func<GameplayAcceptanceCase> factory)
        {
            using var a = factory(); var s = a.Session; var w = s.World; var table = w.Table(a.IdentityTable);
            Require(!table.IsPooled && table.Count == 0, "identity probe needs an initially empty handle table");
            var handles = new EntityHandle[table.Capacity];
            for (int i = 0; i < handles.Length; i++)
            { handles[i] = w.CreateEntity(a.IdentityTable, out _); Require(!handles[i].IsNull, "capacity rejected early"); }
            int rejected = w.CreateFailures;
            Require(w.CreateEntity(a.IdentityTable, out _).IsNull, "full table accepted extra entity");
            Require(w.CreateFailures == rejected + 1, "full table did not count rejection");
            var stale = handles[handles.Length / 2];
            var queue = w.Resource(SimWorld.DestroyQueueKey); queue.Request(stale); queue.Request(stale);
            Require(w.Registry.IsAlive(stale), "destroy committed before tick boundary");
            s.Step(); Require(!w.Registry.IsAlive(stale), "destroy did not commit at next tick");
            Require(table.Count == handles.Length - 1, "duplicate destroy removed another entity");
            var replacement = w.CreateEntity(a.IdentityTable, out _);
            Require(replacement.Index == stale.Index && replacement.Generation != stale.Generation, "identity slot was not generation-safe on reuse");
            Require(!w.DestroyEntity(stale) && w.Registry.IsAlive(replacement), "stale handle destroyed replacement");
            int version = w.LevelVersion; w.ClearLevel();
            Require(table.Count == 0 && !w.Registry.IsAlive(replacement) && w.LevelVersion != version,
                "declared level table survived ClearLevel");
        }

        public static void PooledCapacityAndCompaction(Func<GameplayAcceptanceCase> factory)
        {
            using var a = factory(); var w = a.Session.World; bool found = false;
            foreach (var table in w.Tables)
            {
                if (!table.IsPooled) continue; found = true;
                Require(table.Count == 0, "pool probe requires empty initial tables");
                for (int i = 0; i < table.Capacity; i++) Require(w.Spawn(table.Key) == i, "pool rejected early");
                int rejected = w.CreateFailures;
                Require(w.Spawn(table.Key) == -1 && w.CreateFailures == rejected + 1, "pool overflow must reject/count");
                var dead = table.DeadFlags; dead[0] = 1;
                Require(table.Count == table.Capacity, "dead row compacted before boundary");
                a.Session.Step(); Require(table.Count == table.Capacity - 1, "pool did not compact next tick");
                Require(w.Spawn(table.Key) >= 0, "compacted pool did not reuse row capacity");
            }
            Require(found, "pooled storage unsupported: factory creates no pooled tables");
        }

        public static void PooledIdentityAndCompaction(Func<GameplayAcceptanceCase> factory)
        {
            using var a = factory(); var w = a.Session.World; var table = w.Table(a.IdentityTable);
            Require(table.IsPooled && table.Count == 0 && a.SpawnStableIdentity != null && a.ReadStableIdentity != null,
                "pooled identity check needs real spawn/identity readers and an empty pooled table");
            Require(a.SpawnStableIdentity() == 0 && a.SpawnStableIdentity() == 1 && a.SpawnStableIdentity() == 2,
                "pooled producer did not create expected rows");
            int old = a.ReadStableIdentity(0), next = a.ReadStableIdentity(1), last = a.ReadStableIdentity(2);
            Require(old != next && old != last && next != last, "pooled stable identities collide");
            var dead = table.DeadFlags; dead[0] = 1;
            Require(table.Count == 3, "pool destroyed before boundary"); a.Session.Step();
            Require(table.Count == 2 && a.ReadStableIdentity(0) == next && a.ReadStableIdentity(1) == last,
                "stable compaction changed surviving identities");
            Require(a.SpawnStableIdentity() == 2 && a.ReadStableIdentity(2) != old && a.ReadStableIdentity(2) != next && a.ReadStableIdentity(2) != last,
                "new pooled occupant reused stale identity");
            int version = w.LevelVersion; w.ClearLevel();
            Require(table.Count == 0 && w.LevelVersion != version, "pooled Level clear failed");
        }

        public static void QueuedArrivalOrder(Func<GameplayAcceptanceCase> factory)
        {
            using var a = factory(); using var b = factory();
            var ha = new EntityHandle[4]; var hb = new EntityHandle[4];
            for (int i = 0; i < 4; i++)
            { ha[i] = a.Session.World.CreateEntity(a.IdentityTable, out _); hb[i] = b.Session.World.CreateEntity(b.IdentityTable, out _); }
            Require(ha[0] == hb[0], "fixture must demonstrate equal handle values in independent worlds");
            var qa = a.Session.World.Resource(SimWorld.DestroyQueueKey);
            var qb = b.Session.World.Resource(SimWorld.DestroyQueueKey);
            qa.Request(ha[1]); qa.Request(ha[3]); qa.Request(ha[1]);
            qb.Request(hb[3]); qb.Request(hb[1]); qb.Request(hb[1]);
            a.Session.Step();
            Require(b.Session.World.Registry.IsAlive(hb[1]), "structural change crossed sessions");
            b.Session.Step();
            EqualBytes(a.ReadState(), b.ReadState(), "permuted duplicate destruction");
            for (int i = 0; i < 2; i++)
                Require(a.Session.World.CreateEntity(a.IdentityTable, out _) == b.Session.World.CreateEntity(b.IdentityTable, out _),
                    "permuted arrival order changed handle recycling");
        }

        public static void SaveReplayAndDoubleSession(Func<GameplayAcceptanceCase> factory)
        {
            using var a = factory(); using var b = factory();
            Require(a.RestoreState != null && b.RestoreState != null, "save unsupported: " + a.SaveScope);
            a.Start(); b.Start();
            Require(!ReferenceEquals(a.Session.World, b.Session.World), "factory shared a world");
            for (int i = 0; i < 24; i++) { a.Tick(i); b.Tick(i); }
            byte[] checkpoint = a.ReadState();
            EqualBytes(checkpoint, b.ReadState(), "same seed/input");
            for (int i = 24; i < 48; i++) a.Tick(i);
            EqualBytes(checkpoint, b.ReadState(), "second session isolation");
            byte[] expected = a.ReadState(); uint revision = a.Session.TimelineRevision;
            a.RestoreState(checkpoint);
            Require(a.Session.TimelineRevision != revision, "restore did not invalidate presentation timeline");
            for (int i = 24; i < 48; i++)
            { if ((i & 1) == 0) { a.Tick(i); b.Tick(i); } else { b.Tick(i); a.Tick(i); } }
            EqualBytes(expected, a.ReadState(), "restore replay");
            EqualBytes(expected, b.ReadState(), "alternating session schedule");
            a.Dispose(); b.Tick(48); Require(b.Session.State == SessionState.Running, "disposing A stopped B");
        }

        public static void InputInterruption(Func<GameplayAcceptanceCase> factory)
        {
            using var a = factory(); a.Start(); a.Input(0);
            Require(!a.InputIsClear(), "fixture input did not exercise held/latched input");
            a.Session.Pause(); a.CancelInput(); Require(a.InputIsClear(), "cancel retained held/latched input");
            a.Session.Resume(); a.Session.Step(); Require(a.InputIsClear(), "resume reintroduced old input");
            a.Input(0); a.Session.Restart(); Require(a.InputIsClear(), "restart retained old input");
        }

        public static void QualityIsolation(Func<GameplayAcceptanceCase> factory)
        {
            using var a = factory(); using var b = factory();
            Require(a.Present != null && b.Present != null, "quality unsupported: " + a.PresentationScope);
            a.Start(); b.Start();
            for (int i = 0; i < 48; i++)
            { a.Tick(i); b.Tick(i); a.Present(0, i); b.Present(3, i); }
            EqualBytes(a.ReadState(), b.ReadState(), a.PresentationScope);
        }
    }
}
