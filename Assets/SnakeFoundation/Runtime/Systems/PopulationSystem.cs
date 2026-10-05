using SPF.Contracts;
using SPF.L1.Body;
using SPF.L1.Geometry;
using SPF.Runtime.Scheduling;
using Unity.Jobs;
using Unity.Mathematics;

namespace SnakeFoundation.Systems
{
    /// <summary>
    /// ApplyCommands: keeps the active region populated with AI snakes and grows trail slabs ahead of
    /// body growth (slab moves are main-thread only).
    /// </summary>
    sealed class PopulationSystem : SimSystemBase
    {
        public override SimPhase Phase => SimPhase.ApplyCommands;
        public override int Order => 10;
        public override void Declare(AccessDeclaration access) { }

        public override JobHandle OnTick(in SimContext context, JobHandle dependency)
        {
            var world = context.World;
            var config = world.Resource(SnakeKeys.Config);
            var game = world.Resource(SnakeKeys.Game);
            var bodies = world.Resource(SnakeKeys.Bodies);
            var s = config.Settings;
            var table = world.Table(SnakeKeys.Snake);
            var infos = table.Column(SnakeKeys.Info);
            var trails = table.Column(SnakeKeys.Trail);
            var masses = table.Column(SnakeKeys.Mass);
            var heads = table.Column(SnakeKeys.Head);
            int region = game.ActiveRegion;

            int aiInRegion = 0;
            for (int row = 0; row < table.Count; row++)
            {
                var info = infos[row];
                if (info.Region != region) continue;
                if (info.Has(SnakeFlags.AI)) aiInRegion++;

                var trail = trails[row];
                float mass = masses[row];
                float radius = s.Growth.Radius(mass);
                if (!trail.IsAllocated) continue;
                if (s.NeedsRespace(trail.Spacing, radius))
                {
                    // Thicker (or thinner) body: rewrite the trail with a spacing that fits its radius.
                    float spacing = s.TrailSpacingFor(radius);
                    int needed = TrailMath.PointsForLength(s.Growth.Length(mass), spacing) + 8;
                    if (bodies.Respace(ref trail, heads[row], spacing, math.min(needed + needed / 2, s.MaxTrailPoints)))
                        trails[row] = trail;
                }
                else
                {
                    // Grow the slab before the body needs it (+50% headroom so moves are rare).
                    int needed = TrailMath.PointsForLength(s.Growth.Length(mass), trail.Spacing) + 8;
                    if (needed > trail.Capacity && trail.Capacity < s.MaxTrailPoints)
                    {
                        if (bodies.Resize(ref trail, math.min(needed + needed / 2, s.MaxTrailPoints)))
                            trails[row] = trail;
                    }
                }
            }

            int missing = math.min(s.AIPerRegion - aiInRegion, s.AISpawnsPerTick);
            if (missing <= 0)
                return dependency;

            var def = config.Regions[region];
            bool hasPlayer = world.Registry.TryResolve(game.Player, out _, out int playerRow);
            float2 avoid = hasPlayer ? heads[playerRow] : game.Focus;
            float minDistance = hasPlayer ? s.MinSpawnDistanceFromPlayer : 0f;
            var random = SimRandom.Create(context.Seed, context.Time.Tick, 0x5A11u);
            int skins = config.Skins.Length;

            for (int i = 0; i < missing; i++)
            {
                float2 position = default;
                bool found = false;
                bool nearFocus = random.NextFloat() < s.SpawnNearFocusRatio;
                for (int attempt = 0; attempt < 8 && !found; attempt++)
                {
                    position = nearFocus
                        ? game.Focus + GeoMath.FromAngle(random.NextFloat(0f, 2f * math.PI)) * random.NextFloat(s.SpawnRingMin, s.SpawnRingMax)
                        : random.NextFloat2(def.Min + 40f, def.Max - 40f);
                    found = math.all(position > def.Min + 40f) && math.all(position < def.Max - 40f)
                        && math.distancesq(position, avoid) >= minDistance * minDistance;
                }
                if (!found) continue;
                float mass = random.NextFloat(s.AIStartMassMin, s.AIStartMassMax);
                float2 heading = GeoMath.FromAngle(random.NextFloat(0f, 2f * math.PI));
                var handle = SnakeSpawner.SpawnSnake(world, config, game, region, position, heading, mass,
                    SnakeFlags.AI, random.NextInt(0, skins), ref random);
                if (handle.IsNull) break;
            }
            return dependency;
        }
    }
}
