using System.Collections.Generic;
using SPF.Contracts;
using SPF.Contracts.Collections;
using SPF.L1.Navigation;
using SPF.L1.Spatial;
using SPF.L2.Progression;
using SPF.Runtime.Scheduling;
using SPF.Runtime.World;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace DefenseFoundation.Systems
{
    /// <summary>
    /// Commands (main thread): start, build (rejected when it would cut the path, stand on an enemy, cost
    /// too much or the tile is taken), sell, upgrade, call the next wave early, menu.
    /// </summary>
    sealed class CommandSystem : SimSystemBase
    {
        public override SimPhase Phase => SimPhase.ApplyCommands;
        public override void Declare(AccessDeclaration access) { }

        AStarScratch m_Scratch;

        public override void OnDestroy(SimWorld world) => m_Scratch.Dispose();

        public override JobHandle OnTick(in SimContext context, JobHandle dependency)
        {
            var world = context.World;
            var game = world.Resource(TdKeys.Game);
            var rules = world.Resource(TdKeys.Rules);
            var map = world.Resource(TdKeys.Map);
            var feedback = world.Resource(TdKeys.Feedback);
            while (game.Commands.Count > 0)
            {
                var c = game.Commands.Dequeue();
                switch (c.Kind)
                {
                    case TdCommandKind.Start: StartGame(world, game, rules, map); break;
                    case TdCommandKind.Menu: world.ClearLevel(); game.Flow = TdFlow.Menu; game.Version++; break;
                    case TdCommandKind.NextWave:
                        if (game.Flow == TdFlow.Building) game.BuildTimer = 0f;
                        break;
                    case TdCommandKind.Build:
                        if (!CanBuild(world, game, rules, map, c.Cell, c.Tower, out _)) { game.LastRejected++; feedback.TryAdd(new TdFeedback { Kind = TdFeedbackKind.Rejected, Position = map.AsView().CenterOf(c.Cell) }); break; }
                        var def = rules.Towers[(int)c.Tower];
                        var handle = world.CreateEntity(TdKeys.Tower, out int row);
                        if (handle.IsNull) break;
                        world.Column(TdKeys.TowerInfo).Set(row, new TowerInfo { Kind = c.Tower, Level = 1, Cell = c.Cell, Invested = def.Cost });
                        map[c.Cell] = TdTile.Tower;   // bumps the map version: the flow field re-routes the enemies
                        game.Gold -= def.Cost;
                        feedback.TryAdd(new TdFeedback { Kind = TdFeedbackKind.Built, Position = map.AsView().CenterOf(c.Cell), Tower = c.Tower });
                        game.Version++;
                        break;
                    case TdCommandKind.Sell:
                    case TdCommandKind.Upgrade:
                    {
                        int t = FindTower(world, c.Cell);
                        if (t < 0) break;
                        var towers = world.Column(TdKeys.TowerInfo);
                        var info = towers[t];
                        if (c.Kind == TdCommandKind.Sell)
                        {
                            game.Gold += info.Invested * 7 / 10;
                            map[c.Cell] = TdTile.Ground;
                            world.DestroyEntity(world.Table(TdKeys.Tower).Handles[t]);
                            feedback.TryAdd(new TdFeedback { Kind = TdFeedbackKind.Sold, Position = map.AsView().CenterOf(c.Cell) });
                        }
                        else
                        {
                            int cost = rules.UpgradeCost(info.Kind, info.Level);
                            if (info.Level >= 3 || game.Gold < cost) { game.LastRejected++; break; }
                            game.Gold -= cost;
                            info.Level++;
                            info.Invested += cost;
                            towers[t] = info;
                        }
                        game.Version++;
                        break;
                    }
                }
            }
            return dependency;
        }

        static void StartGame(SimWorld world, TdGameState game, TdRules rules, TileMap map)
        {
            world.ClearLevel();
            map.Fill(TdTile.Ground);
            for (int r = 0; r < rules.Rocks.Length; r++)
            {
                string row = rules.Rocks[r];
                int y = map.Size.y - 1 - r;
                for (int x = 0; x < math.min(row.Length, map.Size.x); x++)
                    if (row[x] == '#') map[new int2(x, y)] = TdTile.Rock;
            }
            game.Gold = rules.StartGold;
            game.Lives = rules.StartLives;
            game.Wave = -1;
            game.Kills = 0;
            game.Time = 0f;
            game.BuildTimer = rules.BuildTime;
            game.Flow = TdFlow.Building;
            game.MapBuilds++;
            game.Version++;
        }

        static int FindTower(SimWorld world, int2 cell)
        {
            var towers = world.Column(TdKeys.TowerInfo);
            for (int i = 0; i < world.Table(TdKeys.Tower).Count; i++) if (math.all(towers[i].Cell == cell)) return i;
            return -1;
        }

        /// <summary>Build check, also used by the UI preview.</summary>
        public bool CanBuild(SimWorld world, TdGameState game, TdRules rules, TileMap map, int2 cell, TowerKind kind, out string reason)
        {
            reason = null;
            var view = map.AsView();
            if (game.Flow != TdFlow.Building && game.Flow != TdFlow.Wave) { reason = "not now"; return false; }
            if (!view.InBounds(cell) || map[cell] != TdTile.Ground || math.all(cell == rules.Spawn) || math.all(cell == rules.Base)) { reason = "blocked tile"; return false; }
            if (game.Gold < rules.Towers[(int)kind].Cost) { reason = "not enough gold"; return false; }
            // No enemy may stand on it (it would be walled in).
            var positions = world.Column(TdKeys.Position);
            for (int i = 0; i < world.Table(TdKeys.Enemy).Count; i++)
                if (math.all(view.CellOf(positions[i]) == cell)) { reason = "occupied"; return false; }
            // There must still be a way from the spawn to the base.
            if (!m_Scratch.IsCreated) m_Scratch = new AStarScratch(map.Size.x * map.Size.y, Allocator.Persistent);
            var blocked = new NativeArray<byte>(map.Size.x * map.Size.y, Allocator.Temp);
            blocked[view.Index(cell)] = 1;
            bool open = GridAStar.Reachable(view, rules.Spawn, rules.Base, ref m_Scratch, blocked);
            blocked.Dispose();
            if (!open) { reason = "would block the path"; return false; }
            return true;
        }
    }

    /// <summary>Waves (main thread): build phase countdown, timed spawn groups, wave end, win / loss, rewards and leaks.</summary>
    sealed class WaveSystem : SimSystemBase
    {
        public override SimPhase Phase => SimPhase.ApplyCommands;
        public override int Order => 10;
        public override void Declare(AccessDeclaration access) { }

        public override JobHandle OnTick(in SimContext context, JobHandle dependency)
        {
            var world = context.World;
            var game = world.Resource(TdKeys.Game);
            var rules = world.Resource(TdKeys.Rules);
            float dt = context.Time.DeltaTime;
            var rewards = world.Resource(TdKeys.Rewards);
            var leaks = world.Resource(TdKeys.Leaks);
            for (int i = 0; i < rewards.Count; i++) { game.Gold += rewards[i]; game.Kills++; game.Version++; }
            for (int i = 0; i < leaks.Count; i++) { game.Lives -= leaks[i]; game.Version++; }
            rewards.Clear();
            leaks.Clear();
            if (game.Flow == TdFlow.Wave || game.Flow == TdFlow.Building) game.Time += dt;
            if (game.Lives <= 0 && (game.Flow == TdFlow.Wave || game.Flow == TdFlow.Building)) { game.Lives = 0; game.Flow = TdFlow.Lost; game.Version++; }

            if (game.Flow == TdFlow.Building)
            {
                game.BuildTimer -= dt;
                if (game.BuildTimer <= 0f)
                {
                    game.Wave++;
                    game.WaveTime = -1e-4f;   // so a group starting at 0 spawns its first unit too
                    game.Flow = TdFlow.Wave;
                    game.Version++;
                }
            }
            else if (game.Flow == TdFlow.Wave)
            {
                var groups = rules.Waves[game.Wave];
                float from = game.WaveTime, to = from + dt;
                game.WaveTime = to;
                bool pending = false;
                var view = world.Resource(TdKeys.Map).AsView();
                for (int g = 0; g < groups.Length; g++)
                {
                    int due = WaveSchedule.Due(groups[g], from, to);
                    for (int k = 0; k < due; k++) Spawn(world, rules, groups[g].Kind, view.CenterOf(rules.Spawn), game.Wave);
                    pending |= WaveSchedule.Spawned(groups[g], to) < groups[g].Count;
                }
                if (!pending && world.Table(TdKeys.Enemy).Count == 0)
                {
                    if (game.Wave + 1 >= rules.Waves.Count) game.Flow = TdFlow.Won;
                    else { game.Flow = TdFlow.Building; game.BuildTimer = rules.BuildTime * 0.5f; game.Gold += 10 + game.Wave * 2; }
                    game.Version++;
                }
            }
            return dependency;
        }

        public static EntityHandle Spawn(SimWorld world, TdRules rules, int kind, float2 position, int wave)
        {
            var h = world.CreateEntity(TdKeys.Enemy, out int row);
            if (h.IsNull) return h;
            var def = rules.Enemies[kind - 1];
            float hp = def.Hp * rules.HpScale(math.max(wave, 0));
            world.Column(TdKeys.Position).Set(row, position);
            world.Column(TdKeys.PrevPosition).Set(row, position);
            world.Column(TdKeys.Info).Set(row, new EnemyInfo { Kind = (byte)kind, Hp = hp, MaxHp = hp, Speed = def.Speed, Reward = def.Reward });
            return h;
        }
    }

    /// <summary>Input: rebuilds the flow field towards the base whenever the map changes (Burst BFS).</summary>
    sealed class PathSystem : SimSystemBase, ISnapshotSystem
    {
        uint m_Version = uint.MaxValue;
        public long Builds { get; private set; }

        public override SimPhase Phase => SimPhase.Input;
        public override void Declare(AccessDeclaration access) => access.Read(TdKeys.Map).Write(TdKeys.Flow);

        public override JobHandle OnTick(in SimContext context, JobHandle dependency)
        {
            var map = context.Resource(TdKeys.Map);
            if (map.Version == m_Version) return dependency;
            m_Version = map.Version;
            Builds++;
            System.Span<int2> goals = stackalloc int2[1];
            goals[0] = context.World.Resource(TdKeys.Rules).Base;
            return context.Resource(TdKeys.Flow).ScheduleBuild(map.AsView(), goals, dependency);
        }

        public void WriteSnapshot(System.IO.BinaryWriter writer) => writer.Write(m_Version);
        public void ReadSnapshot(System.IO.BinaryReader reader, SimWorld world) => m_Version = reader.ReadUInt32();
    }

    /// <summary>Move (Burst, parallel): enemies follow the flow field (slowed by frost); those reaching the base leak.</summary>
    sealed class EnemyMoveSystem : SimSystemBase
    {
        public override SimPhase Phase => SimPhase.Move;
        public override void Declare(AccessDeclaration access) => access
            .Read(TdKeys.Enemy).Write(TdKeys.Position).Write(TdKeys.PrevPosition).Write(TdKeys.Info)
            .Read(TdKeys.Map).Read(TdKeys.Flow).Write(TdKeys.Leaks).Write(SimWorld.DestroyQueueKey);

        public override JobHandle OnTick(in SimContext context, JobHandle dependency)
        {
            var world = context.World;
            var rules = world.Resource(TdKeys.Rules);
            var map = context.Resource(TdKeys.Map).AsView();
            return new MoveJob
            {
                Handles = context.Handles(TdKeys.Enemy),
                Position = context.Column(TdKeys.Position),
                Prev = context.Column(TdKeys.PrevPosition),
                Info = context.Column(TdKeys.Info),
                Map = map,
                Flow = context.Resource(TdKeys.Flow).AsView(map),
                Base = map.CenterOf(rules.Base),
                Leaks = context.Resource(TdKeys.Leaks).AsWriter(),
                Destroy = context.Resource(SimWorld.DestroyQueueKey).AsWriter(),
                DeltaTime = context.Time.DeltaTime,
            }.Schedule(context.Count(TdKeys.Enemy), 64, dependency);
        }

        [BurstCompile(CompileSynchronously = true)]
        struct MoveJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<EntityHandle> Handles;
            public NativeArray<float2> Position, Prev;
            public NativeArray<EnemyInfo> Info;
            public TileMapView Map;
            public FlowFieldView Flow;
            public float2 Base;
            public ParallelQueue<int>.Writer Leaks;
            public ParallelQueue<EntityHandle>.Writer Destroy;
            public float DeltaTime;

            public void Execute(int i)
            {
                var e = Info[i];
                float2 p = Position[i];
                Prev[i] = p;
                if (e.Dead) return;
                float speed = e.Speed * (e.Slow > 0f ? 0.5f : 1f);
                e.Slow = math.max(e.Slow - DeltaTime, 0f);
                float2 dir = Flow.Direction(p);
                if (math.all(Map.CellOf(p) == Map.CellOf(Base))) dir = math.normalizesafe(Base - p);
                p = Map.MoveCircle(p, dir * speed * DeltaTime, 0.3f);
                Position[i] = p;
                if (math.distancesq(p, Base) < 0.15f)
                {
                    e.Dead = true;
                    Leaks.TryAdd(e.Kind == 2 ? 3 : 1);
                    Destroy.TryAdd(Handles[i]);
                }
                Info[i] = e;
            }
        }
    }

    sealed class GridSystem : SimSystemBase
    {
        public override SimPhase Phase => SimPhase.SpatialBuild;
        public override void Declare(AccessDeclaration access) => access.Read(TdKeys.Position).Read(TdKeys.Info).Write(TdKeys.Grid);

        public override JobHandle OnTick(in SimContext context, JobHandle dependency)
        {
            var grid = context.Resource(TdKeys.Grid);
            var fill = new FillJob
            {
                Position = context.Column(TdKeys.Position), Info = context.Column(TdKeys.Info),
                Staging = grid.Staging, StagingCount = grid.StagingCount, Count = context.Count(TdKeys.Enemy),
            }.Schedule(dependency);
            return grid.ScheduleBuild(fill);
        }

        [BurstCompile(CompileSynchronously = true)]
        struct FillJob : IJob
        {
            [ReadOnly] public NativeArray<float2> Position;
            [ReadOnly] public NativeArray<EnemyInfo> Info;
            public NativeArray<GridEntry> Staging;
            public NativeArray<int> StagingCount;
            public int Count;

            public void Execute()
            {
                int n = 0;
                for (int i = 0; i < Count && n < Staging.Length; i++)
                    if (!Info[i].Dead) Staging[n++] = new GridEntry { Position = Position[i], Radius = 0.3f, Owner = i };
                StagingCount[0] = n;
            }
        }
    }

    /// <summary>
    /// Collision (Burst, parallel): towers pick the enemy in range closest to the base (lowest flow
    /// distance) and fire; shots home on their target (resolved through the handle lookup) and hit it,
    /// cannon shells splash, frost slows.
    /// </summary>
    sealed class CombatSystem : SimSystemBase
    {
        public override SimPhase Phase => SimPhase.Collision;
        public override void Declare(AccessDeclaration access) => access
            .Write(TdKeys.TowerInfo).Read(TdKeys.Grid).Read(TdKeys.Map).Read(TdKeys.Flow).Read(TdKeys.Position).Read(TdKeys.Enemy)
            .Write(TdKeys.Shot).Write(TdKeys.ShotPosition).Write(TdKeys.ShotInfo)
            .Write(TdKeys.ShotSpawns).Write(TdKeys.Hits);

        public override JobHandle OnTick(in SimContext context, JobHandle dependency)
        {
            var world = context.World;
            var game = world.Resource(TdKeys.Game);
            if (game.Flow != TdFlow.Wave && game.Flow != TdFlow.Building) return dependency;
            var rules = world.Resource(TdKeys.Rules);
            var map = context.Resource(TdKeys.Map).AsView();
            var grid = context.Resource(TdKeys.Grid).AsReader();
            var towers = new TowerJob
            {
                Towers = context.Column(TdKeys.TowerInfo),
                Defs = rules.Towers,
                Grid = grid,
                Map = map,
                Flow = context.Resource(TdKeys.Flow).AsView(map),
                Handles = context.Handles(TdKeys.Enemy),
                Spawns = context.Resource(TdKeys.ShotSpawns).AsWriter(),
                DeltaTime = context.Time.DeltaTime,
            }.Schedule(context.Count(TdKeys.Tower), 16, dependency);
            var shots = new ShotJob
            {
                Position = context.Column(TdKeys.ShotPosition),
                Info = context.Column(TdKeys.ShotInfo),
                Dead = world.Table(TdKeys.Shot).DeadFlags,
                Lookup = world.Registry.AsLookup(),
                Enemies = context.Column(TdKeys.Position),
                Grid = grid,
                Hits = context.Resource(TdKeys.Hits).AsWriter(),
                DeltaTime = context.Time.DeltaTime,
            }.Schedule(context.Count(TdKeys.Shot), 64, dependency);
            return JobHandle.CombineDependencies(towers, shots);
        }

        struct Target : IGridVisitor
        {
            public float2 From;
            public float RangeSq;
            public FlowFieldView Flow;
            public TileMapView Map;
            public int Row, Best;
            public float2 Position;

            public bool Visit(in GridEntry e)
            {
                if (math.distancesq(e.Position, From) > RangeSq) return true;
                int d = Flow.DistanceAt(Map.CellOf(e.Position));
                if (d < Best || (d == Best && e.Owner < Row)) { Best = d; Row = e.Owner; Position = e.Position; }
                return true;
            }
        }

        [BurstCompile(CompileSynchronously = true)]
        struct TowerJob : IJobParallelFor
        {
            public NativeArray<TowerInfo> Towers;
            [ReadOnly] public NativeArray<TowerDef> Defs;
            public GridReader Grid;
            public TileMapView Map;
            public FlowFieldView Flow;
            [ReadOnly] public NativeArray<EntityHandle> Handles;
            public ParallelQueue<ShotInfo>.Writer Spawns;
            public float DeltaTime;

            public void Execute(int i)
            {
                var t = Towers[i];
                t.Cooldown = math.max(t.Cooldown - DeltaTime, 0f);
                var def = Defs[(int)t.Kind];
                float scale = 1f + 0.6f * (t.Level - 1);
                float range = def.Range * (1f + 0.1f * (t.Level - 1));
                float2 from = Map.CenterOf(t.Cell);
                if (t.Cooldown <= 0f)
                {
                    var target = new Target { From = from, RangeSq = range * range, Flow = Flow, Map = Map, Row = -1, Best = int.MaxValue };
                    Grid.Query(from, range, ref target);
                    if (target.Row >= 0)
                    {
                        t.Cooldown = 1f / def.Rate;
                        t.Aim = math.atan2(target.Position.y - from.y, target.Position.x - from.x);
                        Spawns.TryAdd(new ShotInfo { Target = Handles[target.Row], Origin = from, Aim = target.Position, Speed = def.ShotSpeed, Damage = def.Damage * scale, Splash = def.Splash, Slow = def.Slow, Kind = t.Kind });
                    }
                }
                Towers[i] = t;
            }
        }

        struct Splash : IGridVisitor
        {
            public float2 Centre;
            public float RadiusSq, Damage, Slow;
            public ParallelQueue<TdHit>.Writer Hits;

            public bool Visit(in GridEntry e)
            {
                if (math.distancesq(e.Position, Centre) <= RadiusSq) Hits.TryAdd(new TdHit { Target = e.Owner, Damage = Damage, Slow = Slow });
                return true;
            }
        }

        [BurstCompile(CompileSynchronously = true)]
        struct ShotJob : IJobParallelFor
        {
            public NativeArray<float2> Position;
            public NativeArray<ShotInfo> Info;
            public NativeArray<byte> Dead;
            [ReadOnly] public EntityLookup Lookup;
            [ReadOnly] public NativeArray<float2> Enemies;
            public GridReader Grid;
            public ParallelQueue<TdHit>.Writer Hits;
            public float DeltaTime;

            public void Execute(int i)
            {
                if (Dead[i] != 0) return;
                var s = Info[i];
                bool alive = Lookup.TryResolve(s.Target, out _, out int row);
                if (alive) s.Aim = Enemies[row];
                float2 p = Position[i];
                float2 to = s.Aim - p;
                float step = s.Speed * DeltaTime, dist = math.length(to);
                if (dist > step) { Position[i] = p + to / dist * step; Info[i] = s; return; }
                Position[i] = s.Aim;
                Dead[i] = 1;
                if (s.Splash > 0f)
                {
                    var splash = new Splash { Centre = s.Aim, RadiusSq = s.Splash * s.Splash, Damage = s.Damage, Slow = s.Slow, Hits = Hits };
                    Grid.Query(s.Aim, s.Splash, ref splash);
                }
                else if (alive) Hits.TryAdd(new TdHit { Target = row, Damage = s.Damage, Slow = s.Slow });
            }
        }
    }

    /// <summary>Resolve (main thread): queued shots spawn (sorted, deterministic), hits apply, deaths pay out.</summary>
    sealed class ResolveSystem : SimSystemBase
    {
        public override SimPhase Phase => SimPhase.Resolve;
        public override void Declare(AccessDeclaration access) { }

        struct OriginOrder : IComparer<ShotInfo>
        {
            public int Compare(ShotInfo a, ShotInfo b) =>
                a.Origin.x != b.Origin.x ? a.Origin.x.CompareTo(b.Origin.x) : a.Origin.y.CompareTo(b.Origin.y);
        }

        struct HitOrder : IComparer<TdHit>
        {
            public int Compare(TdHit a, TdHit b) => a.Target != b.Target ? a.Target.CompareTo(b.Target) : a.Damage != b.Damage ? a.Damage.CompareTo(b.Damage) : a.Slow.CompareTo(b.Slow);
        }

        public override JobHandle OnTick(in SimContext context, JobHandle dependency)
        {
            var world = context.World;
            var feedback = world.Resource(TdKeys.Feedback);
            var hits = world.Resource(TdKeys.Hits);
            var infos = world.Column(TdKeys.Info);
            var positions = world.Column(TdKeys.Position);
            var handles = world.Table(TdKeys.Enemy).Handles;
            var destroy = world.Resource(SimWorld.DestroyQueueKey);
            var rewards = world.Resource(TdKeys.Rewards);
            var array = hits.AsArray();
            array.Sort(new HitOrder());
            int count = world.Table(TdKeys.Enemy).Count;
            for (int h = 0; h < array.Length; h++)
            {
                var hit = array[h];
                if ((uint)hit.Target >= (uint)count) continue;
                var e = infos[hit.Target];
                if (e.Dead) continue;
                e.Hp -= hit.Damage;
                e.Slow = math.max(e.Slow, hit.Slow);
                if (e.Hp <= 0f)
                {
                    e.Dead = true;
                    rewards.TryAdd(e.Reward);
                    destroy.Request(handles[hit.Target]);
                    feedback.TryAdd(new TdFeedback { Kind = TdFeedbackKind.Death, Position = positions[hit.Target] });
                }
                infos[hit.Target] = e;
            }
            hits.Clear();

            // New shots: towers queue them in parallel (thread order); one shot per tower per tick, so the
            // tower position orders them deterministically.
            var spawns = world.Resource(TdKeys.ShotSpawns);
            int n = spawns.Count;
            if (n > 0)
            {
                var s = spawns.AsArray();
                s.Sort(new OriginOrder());
                int start = world.SpawnRange(TdKeys.Shot, n, out int added);
                var shotPos = world.Column(TdKeys.ShotPosition);
                var shotInfo = world.Column(TdKeys.ShotInfo);
                for (int i = 0; i < added; i++)
                {
                    shotPos[start + i] = s[i].Origin;
                    shotInfo[start + i] = s[i];
                    feedback.TryAdd(new TdFeedback { Kind = TdFeedbackKind.Shot, Position = s[i].Origin, Tower = s[i].Kind });
                }
            }
            spawns.Clear();
            return dependency;
        }
    }
}
