using SPF.Contracts;
using SPF.L1.Skeleton;
using SPF.L1.Spatial;
using SPF.L2.Combat;
using SPF.Runtime.Scheduling;
using SPF.Runtime.World;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace BrawlerFoundation.Systems
{
    /// <summary>The belt variant shares the session, fighter table, skeleton, flow commands and HUD.
    /// Only the opt-in module selects these systems; the classic rules and save layout are untouched.</summary>
    sealed class BeltFlowSystem : SimSystemBase
    {
        public override SimPhase Phase => SimPhase.ApplyCommands;
        public override void Declare(AccessDeclaration access) { }
        public override JobHandle OnTick(in SimContext context, JobHandle dependency)
        {
            dependency.Complete();
            var world = context.World; var game = world.Resource(BwKeys.Game); var belt = world.Resource(BwBeltKeys.State);
            while (game.Commands.Count > 0)
            {
                switch (game.Commands.Dequeue())
                {
                    case BwCommandKind.Start:
                        world.ClearLevel(); game.Wave = game.Score = game.Kos = 0; game.Input = default; game.FlowTimer = 0;
                        Spawn(world, 0, new float2(-4, 0), 1, 0); NextWave(world, game, belt); break;
                    case BwCommandKind.Menu:
                        world.ClearLevel(); game.Input = default; game.Flow = BwFlow.Menu; game.Version++; break;
                }
            }
            if (game.Flow == BwFlow.Menu || game.Flow == BwFlow.Won || game.Flow == BwFlow.Lost) return dependency;
            var table = world.Table(BwKeys.Fighter); var info = world.Column(BwKeys.Info);
            int alive = 0; bool playerDown = true;
            for (int i = table.Count - 1; i >= 0; i--)
            {
                var f = info[i];
                if (f.Team == 0) { playerDown = f.State == FighterState.KO && f.StateTime >= .9f; continue; }
                if (f.State != FighterState.KO) alive++;
                else if (f.StateTime >= 1f) world.DestroyEntity(table.Handles[i]);
            }
            if (playerDown)
            {
                game.Flow = BwFlow.Lost; game.Input = default; game.Version++;
                world.Resource(BwKeys.Feedback).TryAdd(new BwFeedback { Kind = BwFeedbackKind.Lose });
            }
            else if (game.Flow == BwFlow.Fighting && alive == 0)
            { game.Flow = BwFlow.WaveClear; game.FlowTimer = 2.2f; game.Version++; }
            else if (game.Flow == BwFlow.WaveClear)
            {
                game.FlowTimer -= context.Time.DeltaTime;
                if (game.FlowTimer <= 0)
                {
                    if (game.Wave >= belt.Config.Waves) { game.Flow = BwFlow.Won; game.Input = default; game.Version++; }
                    else NextWave(world, game, belt);
                }
            }
            return dependency;
        }
        static void NextWave(SimWorld world, BwGameState game, BwBeltState belt)
        {
            game.Wave++;
            int count = belt.Config.FirstWaveEnemies + game.Wave - 1;
            for (int i = 0; i < count; i++)
            {
                float side = (i & 1) == 0 ? 1 : -1;
                Spawn(world, 1, new float2(side * (8.1f - (i / 6) * .9f), ((i / 2) % 3 - 1) * 1.5f), -side, (byte)(1 + i % 3));
            }
            game.Flow = BwFlow.Fighting; game.Version++;
            world.Resource(BwKeys.Feedback).TryAdd(new BwFeedback { Kind = BwFeedbackKind.Wave, Value = game.Wave });
        }
        public static void Spawn(SimWorld world, byte team, float2 ground, float facing, byte variant)
        {
            var h = world.CreateEntity(BwKeys.Fighter, out int row);
            if (h.IsNull) { var belt = world.Resource(BwBeltKeys.State); if (belt.RejectedSpawns < int.MaxValue) belt.RejectedSpawns++; return; }
            ground = BwBeltRules.ClampGround(ground);
            var rig = world.Resource(BwKeys.Rig); float hp = team == 0 ? 120 : 25 + 7 * variant;
            world.Column(BwBeltKeys.Ground).Set(row, ground); world.Column(BwBeltKeys.PreviousGround).Set(row, ground);
            world.Column(BwBeltKeys.Motion).Set(row, default);
            world.Column(BwKeys.Position).Set(row, BwBeltRules.Project(ground, 0)); world.Column(BwKeys.Prev).Set(row, BwBeltRules.Project(ground, 0));
            world.Column(BwKeys.Info).Set(row, new FighterInfo { Team = team, Hp = hp, MaxHp = hp, Facing = facing >= 0 ? 1 : -1, Variant = variant, Cooldown = .6f + .2f * variant });
            world.Column(BwKeys.Anim).Set(row, Animator2D.Start(rig.Idle));
        }
    }

    sealed class BeltFighterSystem : SimSystemBase
    {
        readonly bool m_ComposedAbilities;
        public BeltFighterSystem(bool composedAbilities = false) { m_ComposedAbilities = composedAbilities; }
        public override SimPhase Phase => SimPhase.Move;
        public override void Declare(AccessDeclaration access)
        {
            access.Read(BwKeys.Fighter).Write(BwKeys.Info).Write(BwKeys.Anim)
                .Write(BwKeys.Position).Write(BwKeys.Prev).Write(BwBeltKeys.Ground).Write(BwBeltKeys.PreviousGround).Write(BwBeltKeys.Motion)
                .Write(BwMobileSkills.Key).Write(BwBeltKeys.State).Read(BwKeys.Rig);
            if (m_ComposedAbilities) access.Write(BwComposedAbilityState.Key).Write(BwWeapons.PoseKey).Write(BwWeapons.Key).Write(BwKeys.Game);
        }
        public override JobHandle OnTick(in SimContext context, JobHandle dependency)
        {
            dependency.Complete();
            var world = context.World; var game = world.Resource(BwKeys.Game);
            var abilities = world.HasResource(BwComposedAbilityState.Key) ? world.Resource(BwComposedAbilityState.Key) : null;
            abilities?.BeforeTick(world, game.Input);
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
                bool acceptedKick = false, acceptedHeal = false;
                if (isPlayer && abilities != null)
                {
                    var equipment = world.Resource(BwWeapons.Key);
                    bool abilityFree = free && !equipment.Busy && equipment.Equipment.PendingId == 0 &&
                        !input.WasPressed(BwWeapons.SwitchButton) && !BwComposedAbilityState.IsHealing(world);
                    acceptedKick = abilities.Admit(slots, BwButton.Kick, fighting, f.Hp > 0 && f.State != FighterState.KO,
                        abilityFree, input.WasPressed(BwButton.Kick));
                    acceptedHeal = abilities.Admit(slots, BwBeltRules.HealButton, fighting, f.Hp > 0 && f.State != FighterState.KO,
                        abilityFree && !acceptedKick && m.Height <= .001f && f.Hp < f.MaxHp && !input.WasPressed(BwBeltRules.JumpButton), input.WasPressed(BwBeltRules.HealButton));
                    free &= !BwComposedAbilityState.IsHealing(world);
                }
                if (free)
                {
                    if (isPlayer)
                    {
                        move = math.normalizesafe(input.Move) * math.min(1f, math.length(input.Move)) * BwRules.PlayerSpeed;
                        if (math.abs(move.x) > .05f) f.Facing = math.sign(move.x);
                        if (fighting)
                        {
                            bool punch = !weapons && (input.IsHeld(BwButton.Punch) || m.BufferedAttack.Pending);
                            if (abilities != null ? acceptedKick : input.WasPressed(BwButton.Kick) && slots.TryActivate(1, true))
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
                            else if (abilities != null ? acceptedHeal : input.WasPressed(BwBeltRules.HealButton) && m.Height <= .001f && slots.TryActivate(3, f.Hp < f.MaxHp))
                            { if (abilities == null) { f.Hp = math.min(f.MaxHp, f.Hp + 24f); game.Version++; } }
                        }
                    }
                    else if (fighting && f.Variant != 0 && playerAlive)
                    {
                        // Stable, small lane offsets stop every pursuer choosing the same point. Local
                        // spatial separation below supplies crowd avoidance without all-pairs scans.
                        float2 delta = playerGround - p; f.Facing = delta.x >= 0 ? 1 : -1;
                        float reach = f.Variant == 2 ? 1.2f : .92f;
                        var decision = belt.Config.UseDecisionTree
                            ? BwBeltDecisions.Select(belt.DecisionProgram, BwBeltDecisions.Facts(true, delta, reach, f.Cooldown), out _)
                            : math.abs(delta.x) > reach || math.abs(delta.y) > .38f ? BwBeltIntent.Approach
                            : f.Cooldown <= 0 ? BwBeltIntent.Attack : BwBeltIntent.Hold;
                        if (decision == BwBeltIntent.Approach)
                        {
                            float2 target = playerGround + new float2(-f.Facing * (reach - .1f), (handles[i].Index % 3 - 1) * .18f);
                            move = math.normalizesafe(target - p) * BwRules.EnemySpeed * (.9f + f.Variant * .07f);
                        }
                        else if (decision == BwBeltIntent.Attack)
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
            abilities?.BindAcceptedPose(world);
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

    sealed class BeltCombatSystem : SimSystemBase
    {
        public override SimPhase Phase => SimPhase.Resolve;
        public override void Declare(AccessDeclaration access) { }
        NativeArray<BoneLocal> m_Pose; NativeArray<BoneWorld> m_Bones;
        public override void OnCreate(SimWorld world)
        {
            try
            {
                int n = world.Resource(BwKeys.Rig).Asset.BoneCount;
                m_Pose = new NativeArray<BoneLocal>(n * 2, Allocator.Persistent); m_Bones = new NativeArray<BoneWorld>(n, Allocator.Persistent);
            }
            catch (System.Exception failure)
            {
                // OnCreate still owns partial buffers; the pipeline rolls back only completed systems.
                CleanupErrors.Try(ReleaseBuffers, ref failure);
                throw;
            }
        }
        public override void OnDestroy(SimWorld world) => ReleaseBuffers();

        void ReleaseBuffers()
        {
            System.Exception failure = null;
            if (m_Pose.IsCreated) CleanupErrors.Try(() => m_Pose.Dispose(), ref failure);
            if (m_Bones.IsCreated) CleanupErrors.Try(() => m_Bones.Dispose(), ref failure);
            CleanupErrors.ThrowIfAny(failure);
        }
        public override JobHandle OnTick(in SimContext context, JobHandle dependency)
        {
            dependency.Complete(); var world = context.World; var game = world.Resource(BwKeys.Game);
            if (game.Flow != BwFlow.Fighting) return dependency;
            var belt = world.Resource(BwBeltKeys.State); belt.Rebuild(world); belt.HitCandidates = 0;
            var shared = world.Resource(BwKeys.SharedCombat); shared.Prune(world);
            var rig = world.Resource(BwKeys.Rig); var info = world.Column(BwKeys.Info); var anim = world.Column(BwKeys.Anim);
            var ground = world.Column(BwBeltKeys.Ground); var motion = world.Column(BwBeltKeys.Motion); var handles = world.Table(BwKeys.Fighter).Handles;
            for (int i = 0; i < world.Table(BwKeys.Fighter).Count; i++)
            {
                var f = info[i]; if (f.State != FighterState.Attack || f.Attack == AttackKind.None) continue;
                int scope = shared.Track(handles[i], f, context.Time.DeltaTime); var def = rig.Attack(f.Attack);
                if (f.Team == 0 && f.Attack == AttackKind.Kick && world.HasResource(BwComposedAbilityState.Key))
                    def.Damage = world.Resource(BwComposedAbilityState.Key).Config.KickDamage;
                if (scope < 0 || !shared.CrossedActive(scope, def.ActiveFrom, def.ActiveTo, context.Time.DeltaTime)) continue;
                float2 tip = BwProbe.Tip(rig, f, anim[i], new float2(ground[i].x, motion[i].Height), m_Pose, m_Bones);
                var visitor = new HitVisitor { World = world, Game = game, Belt = belt, Shared = shared, Rig = rig,
                    Info = info, Anim = anim, Ground = ground, Motion = motion, Handles = handles,
                    Attacker = f, Source = handles[i], Scope = scope, Tip = tip, Depth = ground[i].y, Definition = def, Tick = context.Time.Tick };
                // Broadphase covers every authored bone reach plus target body. Narrowphase separates
                // ground depth from the vertical height interval; no projected-Y collisions are possible.
                belt.Grid.AsReader().QueryCells(ground[i] - new float2(2.4f, 1f), ground[i] + new float2(2.4f, 1f), ref visitor);
                belt.HitCandidates += visitor.Candidates;
            }
            return dependency;
        }
        struct HitVisitor : IGridVisitor
        {
            public SimWorld World; public BwGameState Game; public BwBeltState Belt; public BwSharedCombatState Shared; public BwRig Rig;
            public NativeArray<FighterInfo> Info; public NativeArray<Animator2D> Anim; public NativeArray<float2> Ground;
            public NativeArray<BwBeltMotion> Motion; public NativeArray<EntityHandle> Handles;
            public long Tick; public FighterInfo Attacker; public EntityHandle Source; public int Scope, Candidates; public float2 Tip; public float Depth; public BwRules.AttackDef Definition;
            public bool Visit(in GridEntry entry)
            {
                int row = entry.Owner; var target = Info[row];
                if (target.Team == Attacker.Team || target.State == FighterState.KO) return true;
                Candidates++;
                var m = Motion[row];
                var hurt = new GroundHurtBox { Ground = Ground[row], HalfWidth = BwRules.BodyHalfWidth, HalfDepth = BwBeltRules.BodyDepth,
                    Bottom = m.Height + BwRules.HurtBottom, Top = m.Height + BwRules.HurtTop };
                if (!GroundCombatQueries.ProbeOverlaps(Tip.x, Depth, Tip.y, BwRules.ProbeRadius, Attacker.Attack == AttackKind.Kick ? .5f : .32f, hurt)) return true;
                if (Shared.Record(Scope, Handles[row]) != HitRecordResult.Added) return true;
                float previousHp = target.Hp;
                bool facts = World.HasResource(AppliedDamageJournal.Key), critical = false;
                float damage = Definition.Damage;
                if (facts && Attacker.Team == 0) damage = World.Resource(CriticalDamageState.Key).Apply(damage, previousHp, out critical);
                target.Hp = math.max(0, target.Hp - damage); target.Flash = 1; target.VelocityX = Attacker.Facing * Definition.Knockback;
                target.Facing = -Attacker.Facing; target.StateTime = 0; target.Attack = AttackKind.None;
                m.BufferedAttack.Clear(); m.ComboGraceTicks = 0;
                var a = Anim[row];
                if (target.Hp <= 0)
                {
                    target.State = FighterState.KO; a.Play(Rig.KO, .05f, restart: true); target.VelocityX *= 1.3f;
                    World.Resource(BwKeys.Feedback).TryAdd(new BwFeedback { Kind = BwFeedbackKind.KO, Position = BwBeltRules.Project(Ground[row], m.Height + 1) });
                    if (Attacker.Team == 0)
                    {
                        Game.Kos++; Game.Score += 100;
                        Belt.TryDrop(Ground[row], BwBeltDropKind.Coin, 10);
                        if ((Game.Kos & 1) == 0) Belt.TryDrop(Ground[row] + new float2(.2f, .15f), BwBeltDropKind.Heal, 18);
                    }
                }
                else { target.State = FighterState.Hit; a.Play(Rig.Hit, .04f, restart: true); }
                if (Attacker.Attack == AttackKind.Kick) m.HeightVelocity = 3.8f;
                if (Attacker.Team == 0) Game.Score += (int)Definition.Damage;
                World.Resource(BwKeys.Feedback).TryAdd(new BwFeedback { Kind = BwFeedbackKind.Hit, Position = BwBeltRules.Project(new float2(Tip.x, Depth), Tip.y), Value = Definition.Damage });
                if (facts) World.Resource(AppliedDamageJournal.Key).AsWriter().Publish(Handles[row], BwBeltRules.Project(new float2(Tip.x, Depth), Tip.y),
                    math.min(math.max(0, previousHp), previousHp - target.Hp), critical, Tick);
                Info[row] = target; Anim[row] = a; Motion[row] = m; Game.Version++;
                if (Attacker.Team == 0 && Attacker.Attack == AttackKind.Kick && World.HasResource(BwComposedAbilityState.Key))
                    World.Resource(BwComposedAbilityState.Key).RecordSettledKick(World, Source, previousHp - target.Hp);
                if(target.Team==0){if(World.HasResource(BwWeapons.Key))World.Resource(BwWeapons.Key).CancelAll();if(World.HasResource(BwWeapons.PoseKey))World.Resource(BwWeapons.PoseKey).Cancel();}
                return true;
            }
        }
    }
}
