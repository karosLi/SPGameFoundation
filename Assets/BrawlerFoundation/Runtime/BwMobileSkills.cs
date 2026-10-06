using SPF.Contracts;
using SPF.L2.Skills;
using SPF.Runtime.Scheduling;
using SPF.Runtime.World;
using Unity.Jobs;

namespace BrawlerFoundation
{
    public static class BwMobileSkills
    {
        public static readonly ResourceKey<SkillSlots> Key = new ResourceKey<SkillSlots>("Bw.MobileSkills.V1");
        // Brawler runs at 60 Hz. Basic repeat is gated by both recovery state and charges.
        public static SkillSlots Create() => new SkillSlots(
            new SkillSlotDefinition(1, 0, SkillActivation.Hold, 20),
            new SkillSlotDefinition(2, 1, SkillActivation.Tap, 90, 2));

        public static bool CanAct(SimWorld world)
        {
            if (world.Resource(BwKeys.Game).Flow != BwFlow.Fighting) return false;
            var info = world.Column(BwKeys.Info);
            for (int i = 0; i < world.Table(BwKeys.Fighter).Count; i++)
                if (info[i].Team == 0) return info[i].Hp > 0f && (info[i].State == FighterState.Idle || info[i].State == FighterState.Walk);
            return false;
        }
    }
}

namespace BrawlerFoundation.Systems
{
    /// <summary>Only the opt-in mobile example changes attack input: no queued attack consumes a charge.
    /// Hold repeats after recovery; simultaneous requests use kick-first priority, matching FighterJob.</summary>
    sealed class MobileSkillSystem : SimSystemBase
    {
        public override SimPhase Phase => SimPhase.Input;
        public override void Declare(AccessDeclaration access) => access.Read(BwKeys.Info).Write(BwMobileSkills.Key);
        public override JobHandle OnTick(in SimContext context, JobHandle dependency)
        {
            dependency.Complete();
            var game = context.World.Resource(BwKeys.Game);
            var slots = context.World.Resource(BwMobileSkills.Key);
            bool playing = game.Flow == BwFlow.Fighting;
            slots.AdvanceTick(playing);
            uint accepted = 0;
            bool eligible = BwMobileSkills.CanAct(context.World);
            if (game.Input.WasPressed(BwButton.Kick) && slots.TryActivate(1, eligible)) accepted = 1u << BwButton.Kick;
            else if ((game.Input.WasPressed(BwButton.Punch) || game.Input.IsHeld(BwButton.Punch)) && slots.TryActivate(0, eligible)) accepted = 1u << BwButton.Punch;
            game.Input.Pressed = accepted;
            if (!playing) game.Input = default;
            return dependency;
        }
    }
}
