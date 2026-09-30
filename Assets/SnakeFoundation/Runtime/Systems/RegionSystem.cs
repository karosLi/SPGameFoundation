using SPF.Contracts;
using SPF.Runtime.Scheduling;
using Unity.Jobs;
using Unity.Mathematics;

namespace SnakeFoundation.Systems
{
    /// <summary>
    /// ApplyCommands: moves the player through a portal. The player's whole body is translated so
    /// its shape is preserved; the old region's snakes stay in the tables, frozen, until the player returns.
    /// </summary>
    sealed class RegionSystem : SimSystemBase
    {
        public override SimPhase Phase => SimPhase.ApplyCommands;
        public override int Order => 5;
        public override void Declare(AccessDeclaration access) { }

        public override JobHandle OnTick(in SimContext context, JobHandle dependency)
        {
            var world = context.World;
            int request = world.Resource(SnakeKeys.Signal).Take(Signals.PortalRequest);
            var game = world.Resource(SnakeKeys.Game);
            if (request <= 0 || !world.Registry.TryResolve(game.Player, out _, out int row))
                return dependency;

            var config = world.Resource(SnakeKeys.Config);
            if (request > config.PortalCount)
                return dependency;
            var portal = config.Portals[request - 1];
            var s = config.Settings;
            var bodies = world.Resource(SnakeKeys.Bodies);
            var points = bodies.Points;

            var heads = world.Column(SnakeKeys.Head);
            var prevHeads = world.Column(SnakeKeys.PrevHead);
            var trails = world.Column(SnakeKeys.Trail);
            var infos = world.Column(SnakeKeys.Info);
            var bounds = world.Column(SnakeKeys.Bounds);

            float2 delta = portal.Arrival - heads[row];
            var trail = trails[row];
            for (int i = 0; i < trail.Count; i++)
            {
                int slot = trail.Slot(trail.Pushed - 1 - (uint)i);
                points[slot] = points[slot] + delta;
            }
            trail.Last += delta;
            trail.Version++;
            trails[row] = trail;
            heads[row] += delta;
            prevHeads[row] += delta;
            bounds[row] += new float4(delta, delta);

            var info = infos[row];
            info.Region = (byte)portal.ToRegion;
            info.PortalCooldown = 3f;
            info.Protection = math.max(info.Protection, s.PortalProtection);
            infos[row] = info;

            game.ActiveRegion = portal.ToRegion;
            game.RegionSwitches++;
            game.Version++;
            world.Resource(SnakeKeys.Feedback).TryAdd(new FeedbackEvent
            {
                Kind = FeedbackKind.Portal,
                Position = heads[row],
                Size = 1f,
                SnakeId = info.Id,
                InvolvesPlayer = true,
            });
            return dependency;
        }
    }
}
