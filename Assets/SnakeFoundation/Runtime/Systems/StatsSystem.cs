using SPF.Contracts;
using SPF.Runtime.Scheduling;
using Unity.Jobs;

namespace SnakeFoundation.Systems
{
    /// <summary>ApplyCommands: refreshes the UI-facing numbers of <see cref="SnakeGameState"/> a few times per second.</summary>
    sealed class StatsSystem : SimSystemBase
    {
        const int Interval = 6;

        public override SimPhase Phase => SimPhase.ApplyCommands;
        public override int Order => 40;
        public override void Declare(AccessDeclaration access) { }

        public override JobHandle OnTick(in SimContext context, JobHandle dependency)
        {
            var world = context.World;
            var game = world.Resource(SnakeKeys.Game);
            bool alive = world.Registry.TryResolve(game.Player, out _, out int playerRow);
            if (alive && game.Flow == GameFlow.Playing)
                game.SurvivalSeconds += context.Time.DeltaTime;

            if (context.Time.Tick % Interval != 0)
                return dependency;

            var table = world.Table(SnakeKeys.Snake);
            var infos = table.Column(SnakeKeys.Info);
            var masses = table.Column(SnakeKeys.Mass);
            int region = game.ActiveRegion;

            if (alive)
            {
                float mass = masses[playerRow];
                game.PlayerMass = mass;
                game.PlayerLength = table.Column(SnakeKeys.Length)[playerRow];
                game.PlayerRadius = table.Column(SnakeKeys.Radius)[playerRow];
                game.PlayerHead = table.Column(SnakeKeys.Head)[playerRow];
                game.PlayerKills = infos[playerRow].Kills;
                if (mass > game.BestMass) game.BestMass = mass;
            }

            // Top-N by insertion into a fixed array; rank of the player by counting heavier snakes.
            var board = game.Leaderboard;
            int boardCount = 0;
            int aliveSnakes = 0;
            int heavier = 0;
            float playerMass = alive ? masses[playerRow] : float.MaxValue;
            for (int row = 0; row < table.Count; row++)
            {
                var info = infos[row];
                if (info.Region != region || info.Has(SnakeFlags.Dead)) continue;
                aliveSnakes++;
                float mass = masses[row];
                if (mass > playerMass) heavier++;
                var entry = new LeaderboardEntry { SnakeId = info.Id, Mass = mass, IsPlayer = info.Has(SnakeFlags.Player) };
                int pos = boardCount;
                while (pos > 0 && board[pos - 1].Mass < mass) pos--;
                if (pos >= SnakeGameState.LeaderboardSize) continue;
                int last = boardCount < SnakeGameState.LeaderboardSize ? boardCount : SnakeGameState.LeaderboardSize - 1;
                for (int k = last; k > pos; k--) board[k] = board[k - 1];
                board[pos] = entry;
                if (boardCount < SnakeGameState.LeaderboardSize) boardCount++;
            }
            game.LeaderboardCount = boardCount;
            game.AliveSnakes = aliveSnakes;
            game.PlayerRank = alive ? heavier + 1 : 0;
            game.Version++;
            return dependency;
        }
    }
}
