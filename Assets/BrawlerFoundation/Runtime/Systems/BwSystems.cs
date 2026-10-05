using SPF.Contracts;
using SPF.L1.Skeleton;
using SPF.Runtime.Scheduling;
using SPF.Runtime.World;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace BrawlerFoundation.Systems
{
    /// <summary>Flow (main thread): start, waves, clearing knocked-out enemies, win and loss.</summary>
    sealed class FlowSystem : SimSystemBase
    {
        public override SimPhase Phase => SimPhase.ApplyCommands;
        public override void Declare(AccessDeclaration access) { }

        public override JobHandle OnTick(in SimContext context, JobHandle dependency)
        {
            var world = context.World;
            var game = world.Resource(BwKeys.Game);
            var feedback = world.Resource(BwKeys.Feedback);
            while (game.Commands.Count > 0)
            {
                switch (game.Commands.Dequeue())
                {
                    case BwCommandKind.Start:
                        world.ClearLevel();
                        game.Wave = 0; game.Score = 0; game.Kos = 0;
                        Spawn(world, 0, new float2(-4f, 0f), 1f, 0);
                        NextWave(world, game, feedback);
                        break;
                    case BwCommandKind.Menu:
                        world.ClearLevel();
                        game.Flow = BwFlow.Menu;
                        game.Version++;
                        break;
                }
            }
            if (game.Flow == BwFlow.Menu || game.Flow == BwFlow.Won || game.Flow == BwFlow.Lost) return dependency;

            var table = world.Table(BwKeys.Fighter);
            var info = world.Column(BwKeys.Info);
            var handles = table.Handles;
            int alive = 0;
            bool playerDown = false;
            for (int i = table.Count - 1; i >= 0; i--)
            {
                var f = info[i];
                if (f.Team == 0) { playerDown = f.State == FighterState.KO && f.StateTime > BwRules.KoTime; continue; }
                if (f.State != FighterState.KO) alive++;
                else if (f.StateTime > BwRules.KoTime) world.DestroyEntity(handles[i]);   // fade out after lying down
            }
            if (playerDown)
            {
                game.Flow = BwFlow.Lost;
                game.Version++;
                feedback.TryAdd(new BwFeedback { Kind = BwFeedbackKind.Lose });
                return dependency;
            }
            if (game.Flow == BwFlow.Fighting && alive == 0)
            {
                game.Flow = game.Wave >= BwRules.Waves ? BwFlow.Won : BwFlow.WaveClear;
                game.FlowTimer = 1.5f;
                game.Version++;
            }
            else if (game.Flow == BwFlow.WaveClear)
            {
                game.FlowTimer -= context.Time.DeltaTime;
                if (game.FlowTimer <= 0f) NextWave(world, game, feedback);
            }
            return dependency;
        }

        static void NextWave(SimWorld world, BwGameState game, EventQueue<BwFeedback> feedback)
        {
            game.Wave++;
            int count = game.Wave + 1;
            for (int i = 0; i < count; i++)
            {
                float side = (i & 1) == 0 ? 1f : -1f;
                Spawn(world, 1, new float2(side * (BwRules.ArenaHalf - 0.5f - (i / 2) * 1.2f), 0f), -side, (byte)(1 + (i + game.Wave) % 3));
            }
            game.Flow = BwFlow.Fighting;
            game.Version++;
            feedback.TryAdd(new BwFeedback { Kind = BwFeedbackKind.Wave, Value = game.Wave });
        }

        public static void Spawn(SimWorld world, byte team, float2 position, float facing, byte variant)
        {
            var rig = world.Resource(BwKeys.Rig);
            var h = world.CreateEntity(BwKeys.Fighter, out int row);
            if (h.IsNull) return;
            float hp = team == 0 ? 100f : 30f + 6f * variant;
            world.Column(BwKeys.Position).Set(row, position);
            world.Column(BwKeys.Prev).Set(row, position);
            world.Column(BwKeys.Info).Set(row, new FighterInfo { Team = team, Hp = hp, MaxHp = hp, Facing = facing, Variant = variant, Cooldown = 0.6f + 0.3f * variant });
            world.Column(BwKeys.Anim).Set(row, Animator2D.Start(rig.Idle));
        }
    }

    /// <summary>Intent, state machine, animation and movement for every fighter (one Burst job).</summary>
    sealed class FighterSystem : SimSystemBase
    {
        public override SimPhase Phase => SimPhase.Move;
        public override void Declare(AccessDeclaration access) => access
            .Read(BwKeys.Fighter).Write(BwKeys.Position).Write(BwKeys.Prev).Write(BwKeys.Info).Write(BwKeys.Anim).Read(BwKeys.Rig);

        public override JobHandle OnTick(in SimContext context, JobHandle dependency)
        {
            var game = context.Resource(BwKeys.Game);
            if (game.Flow == BwFlow.Menu) return dependency;
            var input = game.Input;
            game.Input.Pressed = 0;
            var rig = context.Resource(BwKeys.Rig);
            return new FighterJob
            {
                Count = context.Count(BwKeys.Fighter),
                Position = context.Column(BwKeys.Position), Prev = context.Column(BwKeys.Prev),
                Info = context.Column(BwKeys.Info), Anim = context.Column(BwKeys.Anim),
                Clips = rig.View.Clips, Input = input, Dt = context.Time.DeltaTime,
                Random = SimRandom.Create(context.Seed, context.Time.Tick, 41),
                Idle = rig.Idle, Walk = rig.Walk, Jab = rig.Jab, Cross = rig.Cross, Kick = rig.Kick, Hit = rig.Hit, KO = rig.KO,
                Durations = new float3(rig.Attack(AttackKind.Jab).Duration, rig.Attack(AttackKind.Cross).Duration, rig.Attack(AttackKind.Kick).Duration),
            }.Schedule(dependency);
        }

        [BurstCompile]
        struct FighterJob : IJob
        {
            public int Count;
            public NativeArray<float2> Position, Prev;
            public NativeArray<FighterInfo> Info;
            public NativeArray<Animator2D> Anim;
            [ReadOnly] public NativeArray<ClipInfo> Clips;
            public InputFrame Input;
            public float Dt;
            public Random Random;
            public int Idle, Walk, Jab, Cross, Kick, Hit, KO;
            public float3 Durations;

            float Duration(AttackKind k) => k == AttackKind.Jab ? Durations.x : k == AttackKind.Cross ? Durations.y : Durations.z;
            int ClipOf(AttackKind k) => k == AttackKind.Jab ? Jab : k == AttackKind.Cross ? Cross : Kick;

            void StartAttack(ref FighterInfo f, ref Animator2D a, AttackKind kind)
            {
                f.State = FighterState.Attack;
                f.Attack = kind;
                f.StateTime = 0f;
                f.HitMask = 0;
                f.QueuedPunch = false;
                a.Play(ClipOf(kind), 0.05f, restart: true);
            }

            public void Execute()
            {
                int player = -1;
                for (int i = 0; i < Count; i++) if (Info[i].Team == 0) { player = i; break; }
                float2 playerPos = player >= 0 ? Position[player] : float2.zero;
                bool playerDown = player < 0 || Info[player].State == FighterState.KO;

                for (int i = 0; i < Count; i++)
                {
                    var f = Info[i];
                    var a = Anim[i];
                    var p = Position[i];
                    Prev[i] = p;
                    f.StateTime += Dt;
                    f.Flash = math.max(0f, f.Flash - Dt * 5f);
                    f.Cooldown -= Dt;
                    a.Advance(Dt);
                    float move = 0f;
                    bool isPlayer = f.Team == 0;
                    bool punch = isPlayer && Input.WasPressed(BwButton.Punch);
                    bool kick = isPlayer && Input.WasPressed(BwButton.Kick);

                    switch (f.State)
                    {
                        case FighterState.KO:
                            a.Play(KO, 0.08f);
                            break;
                        case FighterState.Hit:
                            if (f.StateTime >= 0.3f) { f.State = FighterState.Idle; f.StateTime = 0f; }
                            break;
                        case FighterState.Attack:
                            if (punch) f.QueuedPunch = true;
                            if (f.StateTime >= Duration(f.Attack))
                            {
                                if (f.QueuedPunch)
                                {
                                    f.Combo ^= 1;
                                    StartAttack(ref f, ref a, f.Combo == 0 ? AttackKind.Jab : AttackKind.Cross);
                                }
                                else { f.State = FighterState.Idle; f.StateTime = 0f; f.Attack = AttackKind.None; }
                            }
                            break;
                        default:
                            if (isPlayer)
                            {
                                move = math.clamp(Input.Move.x, -1f, 1f) * BwRules.PlayerSpeed;
                                if (math.abs(move) > 0.1f) f.Facing = math.sign(move);
                                if (kick) StartAttack(ref f, ref a, AttackKind.Kick);
                                else if (punch) { f.Combo = 0; StartAttack(ref f, ref a, AttackKind.Jab); }
                            }
                            else if (f.Variant == 0) { }   // training dummy (tests): stands still
                            else if (!playerDown)
                            {
                                float dx = playerPos.x - p.x;
                                f.Facing = dx >= 0f ? 1f : -1f;
                                float reach = f.Variant == 2 ? 1.25f : 1.0f;
                                if (math.abs(dx) > reach) move = f.Facing * BwRules.EnemySpeed * (0.85f + 0.1f * f.Variant);
                                else if (f.Cooldown <= 0f)
                                {
                                    StartAttack(ref f, ref a, f.Variant == 2 && Random.NextFloat() < 0.6f ? AttackKind.Kick : AttackKind.Jab);
                                    f.Cooldown = 1.1f + Random.NextFloat(0.9f);
                                }
                            }
                            if (f.State != FighterState.Attack)
                            {
                                f.State = math.abs(move) > 0.1f ? FighterState.Walk : FighterState.Idle;
                                a.Play(f.State == FighterState.Walk ? Walk : Idle, 0.12f);
                            }
                            break;
                    }

                    p.x += (move + f.VelocityX) * Dt;
                    f.VelocityX *= math.exp(-7f * Dt);
                    p.x = math.clamp(p.x, -BwRules.ArenaHalf, BwRules.ArenaHalf);
                    Position[i] = p;
                    Info[i] = f;
                    Anim[i] = a;
                }

                // Bodies don't overlap (standing fighters push apart symmetrically; deterministic order).
                for (int i = 0; i < Count; i++)
                {
                    if (Info[i].State == FighterState.KO) continue;
                    for (int j = i + 1; j < Count; j++)
                    {
                        if (Info[j].State == FighterState.KO) continue;
                        float2 pi = Position[i], pj = Position[j];
                        float dx = pj.x - pi.x;
                        float min = BwRules.BodyHalfWidth * 2f;
                        if (math.abs(dx) >= min) continue;
                        float push = (min - math.abs(dx)) * 0.5f * (dx >= 0f ? 1f : -1f);
                        pi.x = math.clamp(pi.x - push, -BwRules.ArenaHalf, BwRules.ArenaHalf);
                        pj.x = math.clamp(pj.x + push, -BwRules.ArenaHalf, BwRules.ArenaHalf);
                        Position[i] = pi;
                        Position[j] = pj;
                    }
                }
            }
        }
    }

    /// <summary>
    /// Hits (main thread, ≤ 64 fighters): an attack's probe is the tip of its striking bone, evaluated from the same
    /// skeleton and clip the renderer draws; it hits each opposing hurt box once per swing.
    /// </summary>
    sealed class CombatSystem : SimSystemBase
    {
        public override SimPhase Phase => SimPhase.Resolve;
        public override void Declare(AccessDeclaration access) { }

        NativeArray<BoneLocal> m_Pose;
        NativeArray<BoneWorld> m_World;

        public override void OnDestroy(SimWorld world)
        {
            if (m_Pose.IsCreated) m_Pose.Dispose();
            if (m_World.IsCreated) m_World.Dispose();
        }

        public override JobHandle OnTick(in SimContext context, JobHandle dependency)
        {
            var world = context.World;
            var game = world.Resource(BwKeys.Game);
            if (game.Flow == BwFlow.Menu) return dependency;
            var rig = world.Resource(BwKeys.Rig);
            int bones = rig.Asset.BoneCount;
            if (!m_Pose.IsCreated)
            {
                m_Pose = new NativeArray<BoneLocal>(bones * 2, Allocator.Persistent);
                m_World = new NativeArray<BoneWorld>(bones, Allocator.Persistent);
            }
            var feedback = world.Resource(BwKeys.Feedback);
            int count = world.Table(BwKeys.Fighter).Count;
            var info = world.Column(BwKeys.Info);
            var anim = world.Column(BwKeys.Anim);
            var position = world.Column(BwKeys.Position);

            for (int i = 0; i < count && i < 64; i++)
            {
                var attacker = info[i];
                if (attacker.State != FighterState.Attack) continue;
                var def = rig.Attack(attacker.Attack);
                if (attacker.StateTime < def.ActiveFrom || attacker.StateTime > def.ActiveTo) continue;
                float2 tip = BwProbe.Tip(rig, attacker, anim[i], position[i], m_Pose, m_World);
                for (int j = 0; j < count && j < 64; j++)
                {
                    var target = info[j];
                    if (target.Team == attacker.Team || target.State == FighterState.KO || (attacker.HitMask & (1UL << j)) != 0) continue;
                    float2 c = position[j];
                    float r = BwRules.ProbeRadius;
                    if (tip.x < c.x - BwRules.BodyHalfWidth - r || tip.x > c.x + BwRules.BodyHalfWidth + r) continue;
                    if (tip.y < c.y + BwRules.HurtBottom - r || tip.y > c.y + BwRules.HurtTop + r) continue;

                    attacker.HitMask |= 1UL << j;
                    target.Hp -= def.Damage;
                    target.Flash = 1f;
                    target.VelocityX = attacker.Facing * def.Knockback;
                    target.Facing = -attacker.Facing;
                    target.StateTime = 0f;
                    target.Attack = AttackKind.None;
                    var targetAnim = anim[j];
                    if (target.Hp <= 0f)
                    {
                        target.Hp = 0f;
                        target.State = FighterState.KO;
                        target.VelocityX *= 1.6f;
                        targetAnim.Play(rig.KO, 0.05f, restart: true);
                        feedback.TryAdd(new BwFeedback { Kind = BwFeedbackKind.KO, Position = c + new float2(0f, 1f) });
                        if (attacker.Team == 0) { game.Kos++; game.Score += 100; }
                    }
                    else
                    {
                        target.State = FighterState.Hit;
                        targetAnim.Play(rig.Hit, 0.04f, restart: true);
                    }
                    if (attacker.Team == 0) game.Score += (int)def.Damage;
                    feedback.TryAdd(new BwFeedback { Kind = BwFeedbackKind.Hit, Position = tip, Value = def.Damage });
                    info[j] = target;
                    anim[j] = targetAnim;
                    game.Version++;
                }
                info[i] = attacker;
            }
            return dependency;
        }
    }

    /// <summary>Where an attack strikes: the tip of its bone in the pose the renderer draws.</summary>
    public static class BwProbe
    {
        public static float2 Tip(BwRig rig, in FighterInfo f, in Animator2D anim, float2 position, NativeArray<BoneLocal> pose, NativeArray<BoneWorld> world)
        {
            var view = rig.View;
            var def = rig.Attack(f.Attack == AttackKind.None ? AttackKind.Jab : f.Attack);
            Skeletal.Evaluate(view, anim, pose, pose, 0, view.BoneCount);
            Skeletal.ToWorld(view, pose, position, f.Facing, 1f, world);
            return world[def.Bone].Transform(new float2(view.Bones[def.Bone].Length, 0f), f.Facing);
        }
    }
}
