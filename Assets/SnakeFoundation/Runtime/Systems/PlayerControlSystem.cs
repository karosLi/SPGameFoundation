using SPF.Contracts;
using SPF.Runtime.Scheduling;
using Unity.Jobs;
using Unity.Mathematics;

namespace SnakeFoundation.Systems
{
    /// <summary>Input phase: copies the latest player command into the player snake's control.</summary>
    sealed class PlayerControlSystem : SimSystemBase
    {
        public override SimPhase Phase => SimPhase.Input;
        public override void Declare(AccessDeclaration access) => access.Write(SnakeKeys.Control).Read(SnakeKeys.Heading);

        public override JobHandle OnTick(in SimContext context, JobHandle dependency)
        {
            var world = context.World;
            var game = world.Resource(SnakeKeys.Game);
            var command = game.Command;
            game.Command.Skill = false;   // one-shot: consumed by this tick
            if (!world.Registry.TryResolve(game.Player, out _, out int row))
                return dependency;

            dependency.Complete();
            var heading = world.Column(SnakeKeys.Heading)[row];
            var controls = world.Column(SnakeKeys.Control);
            controls[row] = new SnakeControl
            {
                TargetDirection = math.lengthsq(command.Direction) > 1e-6f ? math.normalize(command.Direction) : heading,
                Boost = command.Boost,
                UseSkill = command.Skill,
            };
            return dependency;
        }
    }
}
