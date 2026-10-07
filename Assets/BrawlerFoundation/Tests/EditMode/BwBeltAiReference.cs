// Frozen production reference from commit 1c9731a. Do not share decision predicates with the new policy.
// Compiled only in the test assembly; never selected by shipped gameplay.
using SPF.Contracts;
using SPF.L1.Skeleton;
using SPF.L1.Spatial;
using SPF.L2.Combat;
using SPF.Runtime.Scheduling;
using SPF.Runtime.World;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace BrawlerFoundation.Tests
{
    sealed class LegacyBeltFighterSystem : SimSystemBase
    {
        public override SimPhase Phase => SimPhase.Move;
        public override void Declare(AccessDeclaration access) => access.Read(BwKeys.Fighter).Write(BwKeys.Info).Write(BwKeys.Anim)
            .Write(BwKeys.Position).Write(BwKeys.Prev).Write(BwBeltKeys.Ground).Write(BwBeltKeys.PreviousGround).Write(BwBeltKeys.Motion)
            .Write(BwMobileSkills.Key).Write(BwBeltKeys.State).Read(BwKeys.Rig);
        public override JobHandle OnTick(in SimContext context, JobHandle dependency)
        {
            dependency.Complete();
            var world = context.World; var game = world.Resource(BwKeys.Game);
            if (game.Flow != BwFlow.Fighting && game.Flow != BwFlow.WaveClear) { game.Input = default; if (world.HasResource(BwWeapons.Key)) world.Resource(BwWeapons.Key).CancelAll(); if (world.HasResource(BwWeapons.PoseKey)) world.Resource(BwWeapons.PoseKey).Cancel(); return dependency; }
            bool fighting = game.Flow == BwFlow.Fighting;
            bool weapons = world.HasResource(BwWeapons.Key);
            var belt = world.Resource(BwBeltKeys.State); var slots = world.Resource(BwMobileSkills.Key); var rig = world.Resource(BwKeys.Rig);
            slots.AdvanceTick(fighting);
            var input = game.Input; game.Input.Pressed = 0;
            var info = world.Column(BwKeys.Info); var anim = world.Column(BwKeys.Anim); var ground = world.Column(BwBeltKeys.Ground);
            var motion = world.Column(BwBeltKeys.Motion); var previous = world.Column(BwBeltKeys.PreviousGround);
            var position = world.Column(BwKeys.Position); var prevPosition = world.Column(BwKeys.Prev); var handles = world.Table(BwKeys.Fighter).Handles;
            int count = world.Table(BwKeys.Fighter).Count, player = -1;
            for (int i = 0; i < count; i++) if (info[i].Team == 0) { player = i; break; }
            float2 playerGround = player >= 0 ? ground[player] : float2.zero;
            bool playerAlive = player >= 0 && info[player].State != FighterState.KO;
            float dt = context.Time.DeltaTime;
            for (int i = 0; i < count; i++)
            {
                var f = info[i]; var a = anim[i]; var m = motion[i]; var p = ground[i];
                previous[i] = p; prevPosition[i] = position[i]; m.PreviousHeight = m.Height;
                f.StateTime += dt; f.Flash = math.max(0, f.Flash - dt * 5); f.Cooldown -= dt; a.Advance(dt);
                if (m.ComboGraceTicks > 0) m.ComboGraceTicks--;
                bool isPlayer = f.Team == 0;
                if (isPlayer && !weapons && fighting && input.WasPressed(BwButton.Punch) && f.State != FighterState.Hit && f.State != FighterState.KO)
                    m.BufferedAttack.Push(BwButton.Punch, context.Time.Tick, 24);
                if (f.State == FighterState.Hit && f.StateTime >= .32f) { f.State = FighterState.Idle; f.StateTime = 0; }
                if (f.State == FighterState.Attack && f.Attack != AttackKind.None && f.StateTime >= rig.Attack(f.Attack).Duration)
                { f.State = FighterState.Idle; f.StateTime = 0; f.Attack = AttackKind.None; }
                float2 move = float2.zero;
                bool free = f.State == FighterState.Idle || f.State == FighterState.Walk;
                if (free)
                {
                    if (isPlayer)
                    {
                        move = math.normalizesafe(input.Move) * math.min(1f, math.length(input.Move)) * BwRules.PlayerSpeed;
                        if (math.abs(move.x) > .05f) f.Facing = math.sign(move.x);
                        if (fighting)
                        {
                            bool punch = !weapons && (input.IsHeld(BwButton.Punch) || m.BufferedAttack.Pending);
                            if (input.WasPressed(BwButton.Kick) && slots.TryActivate(1, true))
                            { BeginAttack(ref f, ref a, rig, AttackKind.Kick); m.BufferedAttack.Clear(); }
                            else if (punch && slots.GetSnapshot(0).Charges > 0)
                            {
                                bool buffered = m.BufferedAttack.TryConsume(context.Time.Tick, true, out _);
                                if ((buffered || input.IsHeld(BwButton.Punch)) && slots.TryActivate(0, true))
                                {
                                    if (m.ComboGraceTicks <= 0) f.Combo = 0;
                                    var attack = f.Combo == 0 ? AttackKind.Jab : f.Combo == 1 ? AttackKind.Cross : AttackKind.Kick;
                                    BeginAttack(ref f, ref a, rig, attack); f.Combo = (f.Combo + 1) % 3; m.ComboGraceTicks = 60;
                                }
                            }
                            else if (input.WasPressed(BwBeltRules.JumpButton) && m.Height <= .001f && slots.TryActivate(2, true)) m.HeightVelocity = 6f;
                            else if (input.WasPressed(BwBeltRules.HealButton) && m.Height <= .001f && slots.TryActivate(3, f.Hp < f.MaxHp))
                            { f.Hp = math.min(f.MaxHp, f.Hp + 24f); game.Version++; }
                        }
                    }
                    else if (fighting && f.Variant != 0 && playerAlive)
                    {
                        // Stable, small lane offsets stop every pursuer choosing the same point. Local
                        // spatial separation below supplies crowd avoidance without all-pairs scans.
                        float2 delta = playerGround - p; f.Facing = delta.x >= 0 ? 1 : -1;
                        float reach = f.Variant == 2 ? 1.2f : .92f;
                        if (math.abs(delta.x) > reach || math.abs(delta.y) > .38f)
                        {
                            float2 target = playerGround + new float2(-f.Facing * (reach - .1f), (handles[i].Index % 3 - 1) * .18f);
                            move = math.normalizesafe(target - p) * BwRules.EnemySpeed * (.9f + f.Variant * .07f);
                        }
                        else if (f.Cooldown <= 0)
                        { BeginAttack(ref f, ref a, rig, f.Variant == 2 ? AttackKind.Kick : AttackKind.Jab); f.Cooldown = 1.15f + .17f * f.Variant; }
                    }
                    if (f.State != FighterState.Attack)
                    { f.State = math.lengthsq(move) > .01f ? FighterState.Walk : FighterState.Idle; a.Play(f.State == FighterState.Walk ? rig.Walk : rig.Idle, .1f); }
                }
                if (f.State == FighterState.KO) a.Play(rig.KO, .08f);
                if (f.State == FighterState.Hit || f.State == FighterState.KO) { m.BufferedAttack.Clear(); m.ComboGraceTicks = 0; }
                if (f.State == FighterState.Attack) move *= .22f;
                m.GroundVelocity = move + new float2(f.VelocityX, 0);
                p = BwBeltRules.ClampGround(p + m.GroundVelocity * dt); f.VelocityX *= math.exp(-7f * dt);
                if (m.Height > 0 || m.HeightVelocity > 0)
                { m.HeightVelocity -= 16f * dt; m.Height = math.max(0, m.Height + m.HeightVelocity * dt); if (m.Height <= 0) m.HeightVelocity = 0; }
                ground[i] = p; info[i] = f; motion[i] = m; anim[i] = a;
            }
            belt.Rebuild(world); var grid = belt.Grid.AsReader(); belt.SeparationCandidates = 0;
            for (int i = 0; i < count; i++)
            {
                var visitor = new SeparateVisitor { Row = i, Self = ground[i], Height = motion[i].Height, Motions = motion, Handles = handles };
                if (info[i].State != FighterState.KO) grid.Query(ground[i], BwRules.BodyHalfWidth, ref visitor);
                float2 push = visitor.Push; float length = math.length(push); if (length > .16f) push *= .16f / length;
                belt.SeparatedGround[i] = BwBeltRules.ClampGround(ground[i] + push); belt.SeparationCandidates += visitor.Candidates;
            }
            for (int i = 0; i < count; i++) { ground[i] = belt.SeparatedGround[i]; position[i] = BwBeltRules.Project(ground[i], motion[i].Height); }
            CollectDrops(world, game, belt, player, dt);
            BwWeapons.AdvancePose(world);
            if (weapons) BwWeapons.Advance(world, input);
            return dependency;
        }
        static void BeginAttack(ref FighterInfo f, ref Animator2D a, BwRig rig, AttackKind kind)
        { f.State = FighterState.Attack; f.Attack = kind; f.StateTime = 0; f.QueuedPunch = false; f.HitMask = 0; a.Play(rig.ClipFor(kind), .04f, restart: true); }
        struct SeparateVisitor : IGridVisitor
        {
            public int Row, Candidates; public float2 Self, Push; public float Height;
            public NativeArray<BwBeltMotion> Motions; public NativeArray<EntityHandle> Handles;
            public bool Visit(in GridEntry entry)
            {
                if (entry.Owner == Row) return true;
                Candidates++;
                if (math.abs(Height - Motions[entry.Owner].Height) < .65f)
                    Push += GroundCombatQueries.Separation(Self, entry.Position, BwRules.BodyHalfWidth * 2, Handles[Row].Index, Handles[entry.Owner].Index);
                return true;
            }
        }
        static void CollectDrops(SimWorld world, BwGameState game, BwBeltState belt, int player, float dt)
        {
            bool alive = player >= 0 && world.Column(BwKeys.Info)[player].State != FighterState.KO;
            float2 p = alive ? world.Column(BwBeltKeys.Ground)[player] : float2.zero;
            for (int i = 0; i < belt.Drops.Length; i++)
            {
                var drop = belt.Drops[i]; if (drop.RemainingTicks <= 0) continue;
                drop.RemainingTicks--;
                float distance = math.distance(p, drop.Ground);
                if (alive && distance < 2.1f) drop.Ground += math.normalizesafe(p - drop.Ground) * math.min(distance, 3.5f * dt);
                if (alive && distance <= .65f && world.Column(BwBeltKeys.Motion)[player].Height < .7f)
                {
                    if (drop.Kind == BwBeltDropKind.Coin) { belt.Coins += (int)drop.Value; game.Score += (int)drop.Value; }
                    else { var f = world.Column(BwKeys.Info)[player]; f.Hp = math.min(f.MaxHp, f.Hp + drop.Value); world.Column(BwKeys.Info).Set(player, f); belt.HealsCollected++; }
                    drop = default; game.Version++;
                }
                belt.Drops[i] = drop;
            }
        }
    }

    // Test-only composition wrapper: same Id/data/system order, one frozen executor substitution.
    sealed class LegacyAiModule : SPF.Runtime.Composition.IGameplayModule
    {
        readonly SPF.Runtime.Composition.IGameplayModule m_Inner;
        public LegacyAiModule(SPF.Runtime.Composition.IGameplayModule inner) => m_Inner = inner;
        public string Id => m_Inner.Id;
        public void DeclareData(SPF.Runtime.World.WorldLayout layout) => m_Inner.DeclareData(layout);
        public void RegisterSystems(SPF.Runtime.Composition.SystemRegistry registry)
        {
            var original = new SPF.Runtime.Composition.SystemRegistry(); m_Inner.RegisterSystems(original);
            foreach (var system in original.Systems)
                registry.Add(system.GetType().Name == "BeltFighterSystem" ? new LegacyBeltFighterSystem() : system);
        }
    }
}
