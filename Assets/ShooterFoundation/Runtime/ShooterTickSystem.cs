using SPF.Contracts;
using SPF.L1.Spatial;
using SPF.Runtime.Scheduling;
using SPF.Runtime.World;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace ShooterFoundation
{
    /// <summary>
    /// One bounded ApplyCommands transaction. Movement, grid building and per-projectile swept queries run
    /// in Burst jobs; all hits reduce in bullet-row order after completion. No unordered damage atomics,
    /// no per-entity objects and no projectile × enemy scan. Structure changes occur only after jobs finish.
    /// </summary>
    public sealed class ShooterTickSystem : SimSystemBase
    {
        public override SimPhase Phase => SimPhase.ApplyCommands;
        public override void Declare(AccessDeclaration a) => a
            .Write(ShooterKeys.Enemy).Write(ShooterKeys.EnemyPosition).Write(ShooterKeys.EnemyPrevious).Write(ShooterKeys.Enemies)
            .Write(ShooterKeys.Bullet).Write(ShooterKeys.BulletPosition).Write(ShooterKeys.BulletPrevious).Write(ShooterKeys.Bullets)
            .Write(ShooterKeys.Pickup).Write(ShooterKeys.PickupPosition).Write(ShooterKeys.Pickups)
            .Write(ShooterKeys.State).Read(ShooterKeys.Rules).Write(ShooterKeys.Grid).Write(ShooterKeys.Scratch).Write(ShooterKeys.Feedback);

        public override JobHandle OnTick(in SimContext c, JobHandle dependency)
        {
            dependency.Complete();
            var w = c.World; var state = w.Resource(ShooterKeys.State); var s = w.Resource(ShooterKeys.Rules).Settings;
            ref var r = ref state.Run;
            while (state.TryTake(out var command))
            {
                if (command.Kind == ShooterCommandKind.Menu) { w.ClearLevel(); r.Flow = ShooterFlow.Menu; state.CancelInput(); r.Version++; }
                else if (command.Kind == ShooterCommandKind.Start && (r.Flow == ShooterFlow.Menu || r.Flow == ShooterFlow.Won || r.Flow == ShooterFlow.Dead))
                {
                    int version = r.Version + 1; w.ClearLevel();
                    r = new ShooterRun { Flow = ShooterFlow.Upgrade, Hero = new float2(0f, -s.ArenaHalf.y + 3f), Hp = s.HeroHp, Wingman = 1, Version = version };
                    r.HeroPrevious = r.Hero; Offer(state, c.Seed);
                }
                else if (command.Kind == ShooterCommandKind.Choose && r.Flow == ShooterFlow.Upgrade && command.Choice >= 0 && command.Choice < 3)
                {
                    ApplyUpgrade(ref r, (ShooterUpgrade)state.Choice(command.Choice), s.HeroHp);
                    r.Wave++; r.Spawned = 0; r.WaveTime = 0f; r.SpawnTimer = 0.2f; r.Flow = ShooterFlow.Playing; r.Version++;
                    state.CancelInput(); Feedback(w, ShooterFeedbackKind.Upgrade, r.Hero);
                }
            }
            if (r.Flow != ShooterFlow.Playing) { state.CancelInput(); return default; }
            float dt = c.Time.DeltaTime;
            r.HeroPrevious = r.Hero;
            float2 move = math.lengthsq(r.Move) > 1f ? math.normalizesafe(r.Move) : r.Move;
            if (!math.all(math.isfinite(move))) move = float2.zero;
            float2 drag = math.all(math.isfinite(r.Drag)) ? r.Drag : float2.zero;
            r.Hero = math.clamp(r.Hero + move * s.HeroSpeed * dt + drag, -s.ArenaHalf + new float2(0.7f, 1.2f), s.ArenaHalf - new float2(0.7f, 2.4f));
            r.Drag = float2.zero; r.Time += dt; r.WaveTime += dt; r.Invulnerable = math.max(0f, r.Invulnerable - dt); r.BeamTargetId = 0;
            SpawnWave(w, state, s, c.Seed, dt);

            var enemies = w.Table(ShooterKeys.Enemy); var bullets = w.Table(ShooterKeys.Bullet);
            var enemyDead = enemies.DeadFlags; var bulletDead = bullets.DeadFlags;
            var ep = w.Column(ShooterKeys.EnemyPosition); var previous = w.Column(ShooterKeys.EnemyPrevious); var ei = w.Column(ShooterKeys.Enemies);
            var bp = w.Column(ShooterKeys.BulletPosition); var bprev = w.Column(ShooterKeys.BulletPrevious); var bi = w.Column(ShooterKeys.Bullets);
            var grid = w.Resource(ShooterKeys.Grid); var hits = w.Resource(ShooterKeys.Scratch).Hits;
            new EnemyMoveJob { Positions = ep, Previous = previous, Info = ei, Staging = grid.Staging, Delta = dt, Half = s.ArenaHalf, GridMin = grid.Origin, GridMax = grid.Origin + grid.Size }.Schedule(enemies.Count, 64).Complete();
            grid.StagingCount.Set(0, enemies.Count);
            var build = grid.ScheduleBuild(default);
            var moving = new BulletMoveJob { Positions = bp, Previous = bprev, Info = bi, Dead = bullets.DeadFlags, Delta = dt, Half = s.ArenaHalf }.Schedule(bullets.Count, 128);
            var collision = new BulletCollisionJob { Positions = bp, Previous = bprev, Info = bi, Dead = bullets.DeadFlags,
                EnemyPositions = ep, EnemyPrevious = previous, Enemies = ei, Grid = grid.AsReader(), Hits = hits,
                Hero = r.Hero, HeroPrevious = r.HeroPrevious }.Schedule(bullets.Count, 128, JobHandle.CombineDependencies(build, moving));
            collision.Complete();

            r.CandidateTests = 0;
            for (int i = 0; i < bullets.Count; i++)
            {
                var hit = hits[i]; r.CandidateTests += hit.Candidates;
                if (hit.Target == -1) continue;
                bulletDead[i] = 1;
                if (hit.Target == -2) Hurt(w, ref r, bi[i].Damage);
                else { var e = ei[hit.Target]; e.Hp -= bi[i].Damage; e.Flash = 0.12f; ei[hit.Target] = e; }
            }
            if (r.Beam > 0)
            {
                var nearest = new NearestVisitor { Positions = ep, Info = ei, Center = r.Hero, BestDistance = s.BeamRange * s.BeamRange, Row = -1, Id = int.MaxValue };
                grid.AsReader().Query(r.Hero, s.BeamRange + 0.001f, ref nearest);
                if (nearest.Row >= 0)
                {
                    int row = nearest.Row; var e = ei[row]; e.Hp -= s.BeamDps * (0.7f + r.Beam * 0.3f) * dt; e.Flash = 0.06f; ei[row] = e;
                    r.BeamTargetId = e.Id; r.BeamEnd = ep[row];
                }
            }
            int alive = 0;
            for (int i = 0; i < enemies.Count; i++)
            {
                var e = ei[i];
                if (e.Hp <= 0f)
                {
                    enemyDead[i] = 1; r.Kills++;
                    ShooterSpawner.Pickup(w, ep[i], e.Id % 6 == 0); Feedback(w, ShooterFeedbackKind.Destroyed, ep[i], e.Radius * 2f); continue;
                }
                if (ep[i].y < -s.ArenaHalf.y - 0.8f) { enemyDead[i] = 1; r.Hp = math.max(0f, r.Hp - 12f); Feedback(w, ShooterFeedbackKind.Hurt, r.Hero); continue; }
                alive++;
                if (ShooterMath.Sweep(previous[i] - r.HeroPrevious, ep[i] - r.Hero, float2.zero, e.Radius + 0.30f, out _)) Hurt(w, ref r, 18f);
                if (e.FireTimer <= 0f && ep[i].y > r.Hero.y + 1.4f && ep[i].y < s.ArenaHalf.y - 0.5f)
                {
                    var direction = math.normalizesafe(r.Hero - ep[i], new float2(0f, -1f));
                    ShooterSpawner.Bullet(w, ep[i], direction * (3.8f + r.Wave * 0.18f), true, 9f, 0.14f);
                    e.FireTimer = s.EnemyFireInterval * (e.Kind == 2 ? 0.7f : 1f); ei[i] = e;
                }
            }
            TickPickups(w, ref r, dt, s.HeroHp, s.ArenaHalf.y);
            if (r.Hp <= 0f) { r.Hp = 0f; r.Flow = ShooterFlow.Dead; r.Version++; state.CancelInput(); return default; }
            FireWeapons(w, ref r, s, dt);
            if (s.SpawnWaves && r.Spawned >= WaveCount(s, r.Wave) && alive == 0)
            {
                // An inter-wave reward sweep prevents dropped rewards being stranded behind the upgrade panel.
                var pickups = w.Table(ShooterKeys.Pickup); var pi = w.Column(ShooterKeys.Pickups); var pickupDead = pickups.DeadFlags;
                for (int i = 0; i < pickups.Count; i++) if (pickupDead[i] == 0) { Collect(ref r, pi[i], s.HeroHp); pickupDead[i] = 1; }
                r.Flow = r.Wave >= s.Waves ? ShooterFlow.Won : ShooterFlow.Upgrade; r.Version++; state.CancelInput();
                if (r.Flow == ShooterFlow.Upgrade) Offer(state, c.Seed);
            }
            return default;
        }
        public static int WaveCount(in ShooterSettings s, int wave) => s.EnemiesPerWave + (wave - 1) * 3;
        static void Offer(ShooterState state, uint seed)
        {
            ref var r = ref state.Run;
            if (r.Wave == 0) { r.Choice0 = (int)ShooterUpgrade.Beam; r.Choice1 = (int)ShooterUpgrade.Power; r.Choice2 = (int)ShooterUpgrade.Wingman; }
            else { var random = SimRandom.Create(seed, (uint)r.Wave, 99); int first = random.NextInt(5); r.Choice0 = first; r.Choice1 = (first + 1) % 5; r.Choice2 = (first + 3) % 5; }
        }
        public static void ApplyUpgrade(ref ShooterRun r, ShooterUpgrade upgrade, float maxHp)
        {
            switch (upgrade)
            {
                case ShooterUpgrade.Power: r.Power++; break;
                case ShooterUpgrade.Cadence: r.Cadence++; break;
                case ShooterUpgrade.Beam: r.Beam++; break;
                case ShooterUpgrade.Wingman: r.Wingman++; break;
                case ShooterUpgrade.Repair: r.Hp = math.min(maxHp, r.Hp + 40f); r.Power++; break;
            }
        }
        static void SpawnWave(SimWorld w, ShooterState state, in ShooterSettings s, uint seed, float dt)
        {
            ref var r = ref state.Run; if (!s.SpawnWaves) return;
            r.SpawnTimer -= dt;
            if (r.SpawnTimer > 0f || r.Spawned >= WaveCount(s, r.Wave)) return;
            var random = SimRandom.Create(seed, (uint)r.Wave, (uint)(r.Spawned + 1));
            float x = random.NextFloat(-s.ArenaHalf.x + 1f, s.ArenaHalf.x - 1f);
            byte kind = (byte)(r.Spawned % 7 == 6 ? 2 : r.Spawned % 2);
            if (ShooterSpawner.Enemy(w, new float2(x, s.ArenaHalf.y + 0.6f), kind) >= 0) r.Spawned++;
            r.SpawnTimer = s.SpawnInterval / (1f + 0.10f * (r.Wave - 1));
        }
        static void FireWeapons(SimWorld w, ref ShooterRun r, in ShooterSettings s, float dt)
        {
            r.ShotTimer -= dt; r.WingTimer -= dt;
            if (r.ShotTimer <= 0f)
            {
                float damage = s.ShotDamage * (1f + r.Power * 0.45f);
                ShooterSpawner.Bullet(w, r.Hero + new float2(-0.17f, 0.7f), new float2(0f, s.ShotSpeed), false, damage);
                ShooterSpawner.Bullet(w, r.Hero + new float2(0.17f, 0.7f), new float2(0f, s.ShotSpeed), false, damage);
                r.ShotTimer = s.ShotInterval / (1f + r.Cadence * 0.18f); Feedback(w, ShooterFeedbackKind.Shot, r.Hero);
            }
            if (r.WingTimer <= 0f && r.Wingman > 0)
            {
                float2 wing = ShooterMath.WingPosition(r);
                ShooterSpawner.Bullet(w, wing + new float2(0f, 0.35f), new float2(0f, s.ShotSpeed * 0.9f), false, s.ShotDamage * (0.55f + r.Wingman * 0.2f));
                r.WingTimer = 0.30f / (1f + r.Wingman * 0.12f);
            }
        }
        static void TickPickups(SimWorld w, ref ShooterRun r, float dt, float maxHp, float halfHeight)
        {
            var table = w.Table(ShooterKeys.Pickup); var dead = table.DeadFlags; var positions = w.Column(ShooterKeys.PickupPosition); var infos = w.Column(ShooterKeys.Pickups);
            for (int i = 0; i < table.Count; i++)
            {
                if (dead[i] != 0) continue;
                var p = positions[i]; var info = infos[i]; info.Life -= dt;
                var delta = r.Hero - p; float distance = math.length(delta);
                if (distance < 3.4f) p += math.normalizesafe(delta) * math.min(distance, dt * 8f); else p.y -= dt * 0.85f;
                if (math.distancesq(p, r.Hero) < 0.6f * 0.6f) { Collect(ref r, info, maxHp); dead[i] = 1; Feedback(w, ShooterFeedbackKind.Pickup, p); }
                else if (info.Life <= 0f || p.y < -halfHeight - 1f) dead[i] = 1;
                positions[i] = p; infos[i] = info;
            }
        }
        static void Collect(ref ShooterRun r, ShooterPickup pickup, float maxHp) { if (pickup.Heal) r.Hp = math.min(maxHp, r.Hp + 16f); else r.Coins++; }
        static void Hurt(SimWorld w, ref ShooterRun r, float damage)
        {
            if (r.Invulnerable > 0f) return; r.Hp = math.max(0f, r.Hp - damage); r.Invulnerable = 0.50f; Feedback(w, ShooterFeedbackKind.Hurt, r.Hero);
        }
        static void Feedback(SimWorld w, ShooterFeedbackKind kind, float2 position, float scale = 1f) => w.Resource(ShooterKeys.Feedback).TryAdd(new ShooterFeedback { Kind = kind, Position = position, Scale = scale });

        [BurstCompile(CompileSynchronously = true)]
        struct EnemyMoveJob : IJobParallelFor
        {
            public NativeArray<float2> Positions, Previous;
            public NativeArray<ShooterEnemy> Info;
            public NativeArray<GridEntry> Staging;
            public float Delta; public float2 Half, GridMin, GridMax;
            public void Execute(int i)
            {
                var e = Info[i]; var old = Positions[i]; var p = old;
                Previous[i] = old; e.Age += Delta; e.FireTimer -= Delta; e.Flash = math.max(0f, e.Flash - Delta);
                p.y -= e.Speed * Delta;
                if (e.Speed > 0f && e.Kind == 1) p.x = math.clamp(e.BaseX + math.sin(e.Age * 1.8f) * 0.9f, -Half.x + 0.6f, Half.x - 0.6f);
                Positions[i] = p; Info[i] = e;
                // Swept enemy bounds ensure a fast moving enemy cannot pass outside a projectile's broad phase.
                float2 midpoint = (old + p) * 0.5f;
                // SpatialGrid bins by CENTER, so retain a crossing sweep even when its midpoint
                // has left the window. Moving the center inward and enlarging the circle is conservative.
                float2 center = math.clamp(midpoint, GridMin + 0.001f, GridMax - 0.001f);
                Staging[i] = new GridEntry { Position = center, Radius = e.Radius + math.distance(old, p) * 0.5f + math.distance(midpoint, center), Owner = i };
            }
        }
        [BurstCompile(CompileSynchronously = true)]
        struct BulletMoveJob : IJobParallelFor
        {
            public NativeArray<float2> Positions, Previous;
            public NativeArray<ShooterBullet> Info; public NativeArray<byte> Dead;
            public float Delta; public float2 Half;
            public void Execute(int i)
            {
                var b = Info[i]; Previous[i] = Positions[i]; Positions[i] += b.Velocity * Delta; b.Life -= Delta; Info[i] = b;
                // Cull on the PREVIOUS point, after the sweep gets its final chance to hit at the boundary.
                if (b.Life <= 0f || math.any(math.abs(Previous[i]) > Half + 3f)) Dead[i] = 1;
            }
        }
        [BurstCompile(CompileSynchronously = true)]
        struct BulletCollisionJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<float2> Positions, Previous, EnemyPositions, EnemyPrevious;
            [ReadOnly] public NativeArray<ShooterBullet> Info;
            [ReadOnly] public NativeArray<ShooterEnemy> Enemies;
            [ReadOnly] public NativeArray<byte> Dead;
            public GridReader Grid; public NativeArray<ShooterHit> Hits;
            public float2 Hero, HeroPrevious;
            public void Execute(int i)
            {
                var result = new ShooterHit { Target = -1, Fraction = 2f };
                if (Dead[i] != 0) { Hits[i] = result; return; }
                var b = Info[i];
                if (b.Hostile)
                {
                    result.Candidates = 1;
                    if (ShooterMath.Sweep(Previous[i] - HeroPrevious, Positions[i] - Hero, float2.zero, b.Radius + 0.30f, out float t)) { result.Target = -2; result.Fraction = t; }
                }
                else
                {
                    var visitor = new SweepVisitor { Start = Previous[i], End = Positions[i], Radius = b.Radius, Positions = EnemyPositions, Previous = EnemyPrevious, Info = Enemies, Hit = result, BestId = int.MaxValue };
                    Grid.Query((Previous[i] + Positions[i]) * 0.5f, math.distance(Previous[i], Positions[i]) * 0.5f + b.Radius + 0.001f, ref visitor); result = visitor.Hit;
                }
                Hits[i] = result;
            }
        }
        public struct SweepVisitor : IGridVisitor
        {
            public float2 Start, End; public float Radius; public int BestId;
            [ReadOnly] public NativeArray<float2> Positions, Previous;
            [ReadOnly] public NativeArray<ShooterEnemy> Info;
            public ShooterHit Hit;
            public bool Visit(in GridEntry entry)
            {
                Hit.Candidates++; int row = entry.Owner; var e = Info[row];
                if (e.Hp <= 0f) return true;
                if (ShooterMath.Sweep(Start - Previous[row], End - Positions[row], float2.zero, Radius + e.Radius, out float t) &&
                    (t < Hit.Fraction || t == Hit.Fraction && e.Id < BestId)) { Hit.Target = row; Hit.Fraction = t; BestId = e.Id; }
                return true;
            }
        }
        public struct NearestVisitor : IGridVisitor
        {
            [ReadOnly] public NativeArray<float2> Positions;
            [ReadOnly] public NativeArray<ShooterEnemy> Info;
            public float2 Center; public float BestDistance; public int Row, Id;
            public bool Visit(in GridEntry entry)
            {
                var e = Info[entry.Owner]; if (e.Hp <= 0f) return true;
                float d = math.distancesq(Center, Positions[entry.Owner]);
                if (d < BestDistance || d == BestDistance && e.Id < Id) { Row = entry.Owner; Id = e.Id; BestDistance = d; }
                return true;
            }
        }
    }
}
