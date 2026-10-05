using SlingFoundation.Systems;
using SPF.L1.Physics;
using SPF.Runtime.Composition;
using SPF.Runtime.World;
using UnityEngine;

namespace SlingFoundation
{
    /// <summary>The physics slingshot game as one module: a rigid-body world resource, flow, a physics step and damage.</summary>
    public sealed class SlModule : GameplayModuleAsset
    {
        public static SlModule Create()
        {
            var module = CreateInstance<SlModule>();
            module.hideFlags = HideFlags.DontSave;
            return module;
        }

        public override void DeclareData(WorldLayout layout)
        {
            var physics = new PhysicsWorld2D(SlRules.Capacity, 32);
            physics.Settings.EventImpulse = 1.5f;
            layout.Resource(SlKeys.Physics, physics);
            layout.Resource(SlKeys.Game, new SlGameState());
            layout.Resource(SlKeys.Feedback, new EventQueue<SlFeedback>(256, saved: false));
        }

        public override void RegisterSystems(SystemRegistry registry) => registry
            .Add(new FlowSystem())
            .Add(new PhysicsSystem())
            .Add(new DamageSystem());
    }

    public static class SlMode
    {
        /// <summary>60 ticks per second: the physics step and the shot feel want the finer step.</summary>
        public static ModeDefinition Create(out GameplayModuleAsset module)
        {
            module = SlModule.Create();
            var settings = SessionSettings.Default;
            settings.TickRate = 60;
            settings.MaxTicksPerFrame = 4;
            return ModeDefinition.Create(new[] { module }, settings);
        }
    }
}
