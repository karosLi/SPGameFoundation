using SPF.Contracts;
using SPF.L1.Physics;
using SPF.Runtime.Scheduling;
using SPF.Runtime.World;
using Unity.Jobs;
using Unity.Mathematics;

namespace SlingFoundation.Systems
{
    /// <summary>Flow (main thread): commands, launching birds, deciding when a shot is over.</summary>
    sealed class FlowSystem : SimSystemBase
    {
        public override SimPhase Phase => SimPhase.ApplyCommands;
        public override void Declare(AccessDeclaration access) { }

        public override JobHandle OnTick(in SimContext context, JobHandle dependency)
        {
            var world = context.World;
            var game = world.Resource(SlKeys.Game);
            var physics = world.Resource(SlKeys.Physics);
            var feedback = world.Resource(SlKeys.Feedback);
            float dt = context.Time.DeltaTime;
            while (game.Commands.Count > 0)
            {
                var command = game.Commands.Dequeue();
                switch (command.Kind)
                {
                    case SlCommandKind.Start:
                        game.Score = 0;
                        SlLevels.Build(physics, game, 0);
                        break;
                    case SlCommandKind.Retry:
                        SlLevels.Build(physics, game, game.Level);
                        break;
                    case SlCommandKind.NextLevel:
                        if (game.Flow != SlFlow.LevelClear) break;
                        if (game.Level + 1 >= SlLevels.Count) { game.Flow = SlFlow.Won; game.Version++; break; }
                        SlLevels.Build(physics, game, game.Level + 1);
                        break;
                    case SlCommandKind.Menu:
                        physics.Clear();
                        game.Flow = SlFlow.Menu;
                        game.Version++;
                        break;
                    case SlCommandKind.Launch:
                        if (game.Flow != SlFlow.Aiming || game.BirdsLeft <= 0) break;
                        var body = PhysicsWorld2D.CircleBody(SlRules.Sling, SlRules.BirdRadius, 4f, 0.6f, 0.3f);
                        body.Velocity = SlRules.LaunchVelocity(command.Pull);
                        body.AngularDamping = 0.5f;
                        int id = physics.Add(body);
                        if (id < 0) break;
                        game.Kind[id] = PieceKind.Bird;
                        game.Hp[id] = float.PositiveInfinity;
                        game.Bird = id;
                        game.BirdsLeft--;
                        game.FlightTime = game.StillTime = 0f;
                        game.Flow = SlFlow.Flying;
                        game.Version++;
                        feedback.TryAdd(new SlFeedback { Kind = SlFeedbackKind.Launch, Position = SlRules.Sling });
                        break;
                }
            }

            if (game.Flow == SlFlow.Flying) UpdateShot(game, physics, feedback, dt);
            return dependency;
        }

        static void UpdateShot(SlGameState game, PhysicsWorld2D physics, EventQueue<SlFeedback> feedback, float dt)
        {
            game.FlightTime += dt;
            bool birdGone = game.Bird < 0 || physics[game.Bird].Alive == 0;
            if (!birdGone)
            {
                var bird = physics[game.Bird];
                bool still = math.lengthsq(bird.Velocity) < 0.25f || bird.Awake == 0;
                game.StillTime = still ? game.StillTime + dt : 0f;
                if (bird.Position.y < -5f || math.abs(bird.Position.x) > 45f) birdGone = true;
            }
            // The shot ends when the bird has rested a moment, everything else settled, or after a time limit.
            bool settled = physics.Stats.AwakeBodies <= 1;
            if (game.TargetsLeft == 0)
            {
                game.ClearTimer += dt;
                if (game.ClearTimer < 1f) return;
            }
            else if (!(birdGone || game.StillTime > 1.2f && settled || game.FlightTime > 10f)) return;

            if (game.Bird >= 0 && physics[game.Bird].Alive != 0) physics.Remove(game.Bird);
            if (game.Bird >= 0) game.Kind[game.Bird] = PieceKind.None;
            game.Bird = -1;
            if (game.TargetsLeft == 0)
            {
                game.Score += game.BirdsLeft * 2000;
                game.Flow = SlFlow.LevelClear;
                feedback.TryAdd(new SlFeedback { Kind = SlFeedbackKind.Clear });
            }
            else if (game.BirdsLeft == 0)
            {
                game.Flow = SlFlow.Failed;
                feedback.TryAdd(new SlFeedback { Kind = SlFeedbackKind.Fail });
            }
            else game.Flow = SlFlow.Aiming;
            game.Version++;
        }
    }

    /// <summary>Steps the physics world as a Burst job (the main thread is free until the next barrier).</summary>
    sealed class PhysicsSystem : SimSystemBase
    {
        public override SimPhase Phase => SimPhase.Move;
        public override void Declare(AccessDeclaration access) => access.Write(SlKeys.Physics);

        public override JobHandle OnTick(in SimContext context, JobHandle dependency)
        {
            var game = context.World.Resource(SlKeys.Game);
            if (game.Flow == SlFlow.Menu) return dependency;
            return context.Resource(SlKeys.Physics).Schedule(context.Time.DeltaTime, dependency);
        }
    }

    /// <summary>Damage from the step's hard contacts: pieces lose toughness by impulse and break; targets falling off count.</summary>
    sealed class DamageSystem : SimSystemBase
    {
        public override SimPhase Phase => SimPhase.Resolve;
        public override void Declare(AccessDeclaration access) { }

        public override JobHandle OnTick(in SimContext context, JobHandle dependency)
        {
            var world = context.World;
            var game = world.Resource(SlKeys.Game);
            if (game.Flow == SlFlow.Menu) return dependency;
            var physics = world.Resource(SlKeys.Physics);
            var feedback = world.Resource(SlKeys.Feedback);
            physics.Complete();
            var events = physics.Events;
            for (int e = 0; e < physics.EventCount; e++)
            {
                var hit = events[e];
                float strength = hit.Impulse;
                if (game.Kind[hit.A] == PieceKind.Bird || game.Kind[hit.B] == PieceKind.Bird)
                    feedback.TryAdd(new SlFeedback { Kind = SlFeedbackKind.Hit, Position = hit.Point, Strength = strength });
                Damage(game, physics, feedback, hit.A, strength);
                Damage(game, physics, feedback, hit.B, strength);
            }
            // Targets knocked off the world are down too.
            for (int i = 0; i < physics.HighWater; i++)
                if (game.Kind[i] == PieceKind.Target && physics[i].Position.y < -3f) Break(game, physics, feedback, i);
            return dependency;
        }

        static void Damage(SlGameState game, PhysicsWorld2D physics, EventQueue<SlFeedback> feedback, int id, float impulse)
        {
            var kind = game.Kind[id];
            if (kind == PieceKind.None || kind == PieceKind.Ground || kind == PieceKind.Bird) return;
            game.Hp[id] -= impulse;
            if (game.Hp[id] <= 0f) Break(game, physics, feedback, id);
        }

        static void Break(SlGameState game, PhysicsWorld2D physics, EventQueue<SlFeedback> feedback, int id)
        {
            var kind = game.Kind[id];
            if (kind == PieceKind.None) return;
            feedback.TryAdd(new SlFeedback { Kind = kind == PieceKind.Target ? SlFeedbackKind.TargetDown : SlFeedbackKind.Break, Piece = kind, Position = physics[id].Position });
            game.Score += SlRules.Points(kind);
            if (kind == PieceKind.Target) game.TargetsLeft--;
            game.Kind[id] = PieceKind.None;
            physics.Remove(id);
            game.Version++;
        }
    }
}
