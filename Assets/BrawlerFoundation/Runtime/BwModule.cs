using BrawlerFoundation.Systems;
using SPF.Runtime.Composition;
using SPF.Runtime.World;
using Unity.Mathematics;
using UnityEngine;

namespace BrawlerFoundation
{
    /// <summary>The brawler as one module: a fighter table (skeletal animators in a column), the shared rig, flow, fighters, combat.</summary>
    public sealed class BwModule : GameplayModuleAsset
    {
        public static BwModule Create()
        {
            var module = CreateInstance<BwModule>();
            module.hideFlags = HideFlags.DontSave;
            return module;
        }

        public override void DeclareData(WorldLayout layout)
        {
            layout.Table(BwKeys.Fighter, 64).LevelScoped()
                .Column(BwKeys.Position).Column(BwKeys.Prev).Column(BwKeys.Info).Column(BwKeys.Anim);
            layout.Resource(BwKeys.Rig, new BwRig());
            layout.Resource(BwKeys.Game, new BwGameState());
            layout.Resource(BwKeys.Feedback, new EventQueue<BwFeedback>(128, saved: false));
        }

        public override void RegisterSystems(SystemRegistry registry) => registry
            .Add(new FlowSystem())
            .Add(new FighterSystem())
            .Add(new CombatSystem());
    }

    public static class BwMode
    {
        public static ModeDefinition Create(out GameplayModuleAsset module)
        {
            module = BwModule.Create();
            var settings = SessionSettings.Default;
            settings.TickRate = 60;
            settings.MaxTicksPerFrame = 4;
            return ModeDefinition.Create(new[] { module }, settings);
        }
    }

    /// <summary>Spawning for tests and tools.</summary>
    public static class BwSpawner
    {
        public static void Spawn(SimWorld world, byte team, float2 position, float facing, byte variant) =>
            FlowSystem.Spawn(world, team, position, facing, variant);
    }
}
