using SPF.Contracts;
using SPF.L1.Spatial;
using SPF.Runtime.World;
using Unity.Mathematics;

namespace SurvivorFoundation
{
    /// <summary>
    /// Simple kiting player (attract mode, smoke tests, benchmarks): flees the local crowd and enemy
    /// bullets, drifts towards nearby gems when it is safe, stays away from the arena edge, and picks
    /// upgrades by a fixed preference.
    /// </summary>
    public sealed class SvBot
    {
        static readonly Upgrade[] Preference = { Upgrade.Orbit, Upgrade.Spiral, Upgrade.Nova, Upgrade.Bolt, Upgrade.Might, Upgrade.Vitality, Upgrade.Magnet, Upgrade.Speed };

        struct Threats : IGridVisitor
        {
            public float2 Hero;
            public float2 Away;
            public int Count;

            public bool Visit(in GridEntry e)
            {
                float2 d = Hero - e.Position;
                float len2 = math.max(math.lengthsq(d), 0.05f);
                Away += d / len2;
                Count++;
                return true;
            }
        }

        public InputFrame Think(SimWorld world)
        {
            var game = world.Resource(SvKeys.Game);
            if (game.Flow == SvFlow.LevelUp)
            {
                int pick = 0, best = int.MaxValue;
                for (int i = 0; i < game.ChoiceCountOffered; i++)
                {
                    int rank = System.Array.IndexOf(Preference, (Upgrade)game.Choices[i]);
                    if (rank < best) { best = rank; pick = i; }
                }
                game.Send(SvCommandKind.Choose, pick);
                return default;
            }
            if (game.Flow != SvFlow.Playing) return default;
            var s = world.Resource(SvKeys.Config).Settings;
            float2 hero = game.Hero;
            var threats = new Threats { Hero = hero };
            world.Resource(SvKeys.EnemyGrid).AsReader().Query(hero, 5f, ref threats);
            float2 move = threats.Away;
            // Enemy bullets close by.
            var bullets = world.Column(SvKeys.BulletInfo);
            var bulletPos = world.Column(SvKeys.BulletPosition);
            int n = world.Table(SvKeys.Bullet).Count;
            for (int i = 0; i < n; i++)
            {
                if (bullets[i].Team != BulletTeam.Enemy) continue;
                float2 d = hero - bulletPos[i];
                float len2 = math.lengthsq(d);
                if (len2 < 9f) move += d / math.max(len2, 0.05f) * 1.5f;
            }
            if (math.lengthsq(move) < 0.05f)
            {
                // Safe: collect the nearest gem.
                var gems = world.Column(SvKeys.GemPosition);
                float best = 64f;
                float2 target = hero;
                for (int i = 0; i < world.Table(SvKeys.Gem).Count; i++)
                {
                    float d = math.distancesq(gems[i], hero);
                    if (d < best) { best = d; target = gems[i]; }
                }
                move = target - hero;
            }
            // Keep off the arena edge.
            float edge = s.ArenaHalf - 10f;
            if (math.abs(hero.x) > edge) move.x -= math.sign(hero.x) * 2f;
            if (math.abs(hero.y) > edge) move.y -= math.sign(hero.y) * 2f;
            return new InputFrame { Move = math.lengthsq(move) > 1e-4f ? math.normalize(move) : float2.zero };
        }
    }
}
