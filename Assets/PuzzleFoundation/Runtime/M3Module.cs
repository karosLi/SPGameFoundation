using SPF.Contracts;
using SPF.Runtime.Composition;
using SPF.Runtime.Scheduling;
using SPF.Runtime.World;
using Unity.Jobs;
using UnityEngine;

namespace PuzzleFoundation
{
    public static class M3Keys
    {
        public static readonly SPF.Contracts.ResourceKey<M3Board> Board = new SPF.Contracts.ResourceKey<M3Board>("M3.Board");
    }

    /// <summary>ApplyCommands: a requested new game, then at most one move per tick (each move is one turn).</summary>
    sealed class M3TurnSystem : SimSystemBase
    {
        public override SimPhase Phase => SimPhase.ApplyCommands;
        public override void Declare(AccessDeclaration access) { }

        public override JobHandle OnTick(in SimContext context, JobHandle dependency)
        {
            var board = context.World.Resource(M3Keys.Board);
            if (board.StartRequested)
            {
                board.StartRequested = false;
                M3Rules.NewBoard(board, context.Seed + (uint)board.Level * 7919u, board.Level);
            }
            else if (board.Moves.Count > 0)
                M3Rules.Apply(board, board.Moves.Dequeue());
            return dependency;
        }
    }

    public sealed class M3Module : GameplayModuleAsset
    {
        public static M3Module Create()
        {
            var m = CreateInstance<M3Module>();
            m.hideFlags = HideFlags.DontSave;
            return m;
        }

        public override void DeclareData(WorldLayout layout) => layout.Resource(M3Keys.Board, new M3Board());
        public override void RegisterSystems(SystemRegistry registry) => registry.Add(new M3TurnSystem());
    }

    public static class M3Mode
    {
        public static ModeDefinition Create(out GameplayModuleAsset module)
        {
            module = M3Module.Create();
            return ModeDefinition.Create(new[] { module }, SessionSettings.Default);
        }
    }
}
