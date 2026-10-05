using SPF.Contracts;
using SPF.Contracts.Collections;
using SPF.L1.Spatial;
using SPF.L2.Movement;
using SPF.Runtime.Scheduling;
using SPF.Runtime.World;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace PlatformerFoundation.Systems
{
    /// <summary>Flow (main thread): start, next level, retry, menu; builds levels from text.</summary>
    sealed class FlowSystem : SimSystemBase
    {
        public override SimPhase Phase => SimPhase.ApplyCommands;
        public override void Declare(AccessDeclaration access) { }

        public override JobHandle OnTick(in SimContext context, JobHandle dependency)
        {
            var world = context.World;
            var game = world.Resource(PlKeys.Game);
            float dt = context.Time.DeltaTime;
            while (game.Commands.Count > 0)
            {
                switch (game.Commands.Dequeue())
                {
                    case PlCommandKind.Start:
                        game.Lives = 3; game.Coins = 0; game.Stomps = 0;
                        Load(world, game, 0);
                        break;
                    case PlCommandKind.NextLevel:
                        if (game.Flow != PlFlow.LevelComplete) break;
                        if (game.Level + 1 >= PlLevels.All.Length) { game.Flow = PlFlow.Won; game.Version++; break; }
                        Load(world, game, game.Level + 1);
                        break;
                    case PlCommandKind.Retry:
                        game.Lives = 3;
                        Load(world, game, game.Level);
                        break;
                    case PlCommandKind.Menu:
                        world.ClearLevel();
                        game.Flow = PlFlow.Menu;
                        game.Version++;
                        break;
                }
            }
            if (game.Flow == PlFlow.Playing || game.Flow == PlFlow.Dying) game.Time += dt;
            if (game.Flow == PlFlow.Dying)
            {
                game.FlowTimer -= dt;
                if (game.FlowTimer <= 0f)
                {
                    if (game.Lives <= 0) { game.Flow = PlFlow.GameOver; game.Version++; }
                    else Respawn(game);
                }
            }
            return dependency;
        }

        static void Respawn(PlGameState game)
        {
            game.Hero = game.HeroPrev = game.Start;
            game.Motor = default;
            game.Riding = -1;
            game.Flow = PlFlow.Playing;
            game.Version++;
        }

        /// <summary>Builds a level: tiles, hazards, coins, walkers, platforms, start and goal.</summary>
        public static void Load(SimWorld world, PlGameState game, int level)
        {
            world.ClearLevel();
            var rows = PlLevels.All[level];
            var map = world.Resource(PlKeys.Map);
            var hazards = world.Resource(PlKeys.Hazards);
            map.Fill(PlTile.Empty);
            hazards.Fill(PlTile.Empty);
            int height = math.min(rows.Length, map.Size.y);
            game.CoinsInLevel = 0;
            for (int r = 0; r < height; r++)
            {
                string row = rows[r];
                int y = height - 1 - r;
                for (int x = 0; x < math.min(row.Length, map.Size.x); x++)
                {
                    var cell = new int2(x, y);
                    float2 centre = new float2(x + 0.5f, y + 0.5f);
                    switch (row[x])
                    {
                        case '#': map[cell] = PlTile.Solid; break;
                        case '=': map[cell] = PlTile.OneWay; break;
                        case '^': hazards[cell] = PlTile.Spikes; break;
                        case 'G': hazards[cell] = PlTile.Goal; hazards[cell + new int2(0, 1)] = PlTile.Goal; game.GoalPosition = centre; break;
                        case 'S': game.Start = new float2(x + 0.5f, y + PlGameState.HeroHalf.y); break;
                        case 'o':
                            world.Column(PlKeys.CoinPosition).Set(world.Spawn(PlKeys.Coin), centre);
                            game.CoinsInLevel++;
                            break;
                        case 'w':
                        {
                            var h = world.CreateEntity(PlKeys.Walker, out int row0);
                            if (h.IsNull) break;
                            var half = new float2(0.4f, 0.4f);
                            var p = new float2(x + 0.5f, y + half.y);
                            world.Column(PlKeys.WalkerPosition).Set(row0, p);
                            world.Column(PlKeys.WalkerPrev).Set(row0, p);
                            world.Column(PlKeys.WalkerInfo).Set(row0, new WalkerInfo { Dir = -1f, Speed = 1.6f, Half = half });
                            break;
                        }
                        case '-':
                        case '|':
                        {
                            var h = world.CreateEntity(PlKeys.Platform, out int row0);
                            if (h.IsNull) break;
                            var info = new PlatformInfo
                            {
                                Origin = centre, Half = new float2(1.5f, 0.25f), Period = 4f, Phase = x * 0.37f,
                                Travel = row[x] == '-' ? new float2(4f, 0f) : new float2(0f, 3f),
                            };
                            world.Column(PlKeys.PlatformInfo).Set(row0, info);
                            var p = PlatformSystem.PositionAt(info, 0f);
                            world.Column(PlKeys.PlatformPosition).Set(row0, p);
                            world.Column(PlKeys.PlatformPrev).Set(row0, p);
                            break;
                        }
                    }
                }
            }
            game.Level = level;
            game.Time = 0f;
            Respawn(game);
            game.LevelBuilds++;
        }
    }

    /// <summary>Move (main thread, few rows): platforms follow a smooth back-and-forth path.</summary>
    sealed class PlatformSystem : SimSystemBase
    {
        public override SimPhase Phase => SimPhase.Move;
        public override int Order => 0;
        public override void Declare(AccessDeclaration access) => access.Write(PlKeys.PlatformPosition).Write(PlKeys.PlatformPrev).Read(PlKeys.PlatformInfo);

        public static float2 PositionAt(in PlatformInfo p, float time) =>
            p.Origin + p.Travel * (0.5f - 0.5f * math.cos((time / p.Period + p.Phase) * math.PI * 2f));

        public override JobHandle OnTick(in SimContext context, JobHandle dependency)
        {
            var world = context.World;
            var game = world.Resource(PlKeys.Game);
            dependency.Complete();
            var pos = context.Column(PlKeys.PlatformPosition);
            var prev = context.Column(PlKeys.PlatformPrev);
            var infos = context.Column(PlKeys.PlatformInfo);
            for (int i = 0; i < context.Count(PlKeys.Platform); i++)
            {
                prev[i] = pos[i];
                pos[i] = PositionAt(infos[i], game.Time);
            }
            return dependency;
        }
    }

    /// <summary>Decide (Burst, parallel): walkers patrol, turning at walls and ledge edges; stomped ones fade out.</summary>
    sealed class WalkerSystem : SimSystemBase
    {
        public override SimPhase Phase => SimPhase.Decide;
        public override void Declare(AccessDeclaration access) => access
            .Read(PlKeys.Walker).Write(PlKeys.WalkerPosition).Write(PlKeys.WalkerPrev).Write(PlKeys.WalkerInfo)
            .Read(PlKeys.Map).Write(SimWorld.DestroyQueueKey);

        public override JobHandle OnTick(in SimContext context, JobHandle dependency)
        {
            var world = context.World;
            if (world.Resource(PlKeys.Game).Flow != PlFlow.Playing && world.Resource(PlKeys.Game).Flow != PlFlow.Dying) return dependency;
            return new WalkJob
            {
                Handles = context.Handles(PlKeys.Walker),
                Position = context.Column(PlKeys.WalkerPosition),
                Prev = context.Column(PlKeys.WalkerPrev),
                Info = context.Column(PlKeys.WalkerInfo),
                Map = context.Resource(PlKeys.Map).AsView(),
                Destroy = context.Resource(SimWorld.DestroyQueueKey).AsWriter(),
                DeltaTime = context.Time.DeltaTime,
            }.Schedule(context.Count(PlKeys.Walker), 32, dependency);
        }

        [BurstCompile(CompileSynchronously = true)]
        struct WalkJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<EntityHandle> Handles;
            public NativeArray<float2> Position, Prev;
            public NativeArray<WalkerInfo> Info;
            public TileMapView Map;
            public ParallelQueue<EntityHandle>.Writer Destroy;
            public float DeltaTime;

            public void Execute(int i)
            {
                var w = Info[i];
                float2 p = Position[i];
                Prev[i] = p;
                if (w.Dead)
                {
                    w.DeadTimer -= DeltaTime;
                    if (w.DeadTimer <= 0f) Destroy.TryAdd(Handles[i]);
                    Info[i] = w;
                    return;
                }
                w.VelocityY = math.max(w.VelocityY - 40f * DeltaTime, -20f);
                p = Map.MoveBox(p, w.Half, new float2(w.Dir * w.Speed * DeltaTime, w.VelocityY * DeltaTime), PlTile.OneWay, out var contacts);
                if ((contacts & BoxContacts.Ground) != 0) w.VelocityY = 0f;
                bool wall = (contacts & (BoxContacts.Left | BoxContacts.Right)) != 0;
                // Turn before walking off a ledge.
                bool grounded = Map.BoxGrounded(p, w.Half, PlTile.OneWay);
                bool ledge = grounded && !Map.BoxGrounded(p + new float2(w.Dir * w.Half.x * 2f, 0f), w.Half, PlTile.OneWay);
                if (wall || ledge) w.Dir = -w.Dir;
                Position[i] = p;
                Info[i] = w;
            }
        }
    }

    /// <summary>
    /// Collision (main thread: one hero): the platformer motor against the tile map, riding / landing on
    /// moving platforms, coins, stomping walkers (or dying on them), spikes, falling out, the goal.
    /// </summary>
    sealed class HeroSystem : SimSystemBase
    {
        public override SimPhase Phase => SimPhase.Collision;
        public override void Declare(AccessDeclaration access) { }

        public override JobHandle OnTick(in SimContext context, JobHandle dependency)
        {
            var world = context.World;
            var game = world.Resource(PlKeys.Game);
            var input = game.Input;
            game.Input.Pressed = 0;
            game.HeroPrev = game.Hero;
            if (game.Flow != PlFlow.Playing) return dependency;
            var feedback = world.Resource(PlKeys.Feedback);
            var map = world.Resource(PlKeys.Map).AsView();
            var hazards = world.Resource(PlKeys.Hazards).AsView();
            float dt = context.Time.DeltaTime;
            var half = PlGameState.HeroHalf;

            // Ride the platform stood on last tick.
            float2 carry = float2.zero;
            var platPos = world.Column(PlKeys.PlatformPosition);
            var platPrev = world.Column(PlKeys.PlatformPrev);
            var platInfo = world.Column(PlKeys.PlatformInfo);
            int platforms = world.Table(PlKeys.Platform).Count;
            if (game.Riding >= 0 && game.Riding < platforms) carry = platPos[game.Riding] - platPrev[game.Riding];

            bool wasGrounded = game.Motor.Grounded;
            var motorInput = new PlatformerInput
            {
                MoveX = input.Move.x,
                JumpPressed = input.WasPressed(PlButton.Jump),
                JumpHeld = input.IsHeld(PlButton.Jump),
            };
            float previousBottom = game.Hero.y - half.y + carry.y;
            bool riding = game.Riding >= 0;
            if (riding) game.Motor.Grounded = true;   // a platform is ground for coyote / jump purposes
            var p = PlatformerMotor.Step(game.Tuning, ref game.Motor, motorInput, dt, map, game.Hero, half, PlTile.OneWay, carry);
            if (math.abs(input.Move.x) > 0.1f) game.Facing = math.sign(input.Move.x);
            if (game.Motor.JumpedThisStep) feedback.TryAdd(new PlFeedback { Kind = PlFeedbackKind.Jump, Position = p });

            game.Riding = -1;
            if (game.Motor.Velocity.y <= 0f)
                for (int i = 0; i < platforms; i++)
                {
                    float2 pp = platPos[i];
                    if (!PlatformerMotor.LandOn(ref p, half, previousBottom, pp, platInfo[i].Half)) continue;
                    game.Riding = i;
                    game.Motor.Velocity.y = 0f;
                    game.Motor.Grounded = true;
                    break;
                }
            if (!wasGrounded && game.Motor.Grounded && !riding) feedback.TryAdd(new PlFeedback { Kind = PlFeedbackKind.Land, Position = p });
            game.Hero = p;

            // Coins (pooled rows).
            var coins = world.Column(PlKeys.CoinPosition);
            var dead = world.Table(PlKeys.Coin).DeadFlags;
            for (int i = 0; i < world.Table(PlKeys.Coin).Count; i++)
            {
                if (dead[i] != 0) continue;
                float2 d = math.abs(coins[i] - p);
                if (d.x < half.x + 0.3f && d.y < half.y + 0.3f)
                {
                    dead[i] = 1;
                    game.Coins++;
                    feedback.TryAdd(new PlFeedback { Kind = PlFeedbackKind.Coin, Position = coins[i] });
                    game.Version++;
                }
            }

            // Walkers: stomp from above, otherwise they hurt.
            var wPos = world.Column(PlKeys.WalkerPosition);
            var wInfo = world.Column(PlKeys.WalkerInfo);
            for (int i = 0; i < world.Table(PlKeys.Walker).Count; i++)
            {
                var w = wInfo[i];
                if (w.Dead) continue;
                float2 d = math.abs(wPos[i] - p);
                if (d.x >= half.x + w.Half.x || d.y >= half.y + w.Half.y) continue;
                if (game.Motor.Velocity.y < 0f && previousBottom >= wPos[i].y)
                {
                    w.Dead = true;
                    w.DeadTimer = 0.5f;
                    wInfo[i] = w;
                    game.Motor.Velocity.y = game.Tuning.JumpVelocity * 0.6f;
                    game.Stomps++;
                    feedback.TryAdd(new PlFeedback { Kind = PlFeedbackKind.Stomp, Position = wPos[i] });
                }
                else { Die(game, feedback); return dependency; }
            }

            if (hazards.BoxTouches(p, half, PlTile.Spikes) || p.y < -2f) { Die(game, feedback); return dependency; }
            if (hazards.BoxTouches(p, half, PlTile.Goal))
            {
                game.Flow = PlFlow.LevelComplete;
                feedback.TryAdd(new PlFeedback { Kind = PlFeedbackKind.Goal, Position = p });
                game.Version++;
            }
            return dependency;
        }

        static void Die(PlGameState game, EventQueue<PlFeedback> feedback)
        {
            game.Lives--;
            game.Flow = PlFlow.Dying;
            game.FlowTimer = 1f;
            game.Motor = default;
            feedback.TryAdd(new PlFeedback { Kind = PlFeedbackKind.Die, Position = game.Hero });
            game.Version++;
        }
    }
}
