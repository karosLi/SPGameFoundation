using System;
using SPF.Contracts;
using SPF.L1.Navigation;
using Unity.Mathematics;

namespace RpgFoundation
{
    /// <summary>
    /// Simple autopilot producing <see cref="InputFrame"/>s: hunts the nearest monster along its own flow
    /// field, swings when close, throws fireballs at visible targets, drinks potions when low, then walks to
    /// the stairs. Used by integration tests, benchmarks and the attract mode. Main thread.
    /// </summary>
    public sealed class RpgBot : IDisposable
    {
        FlowField m_Field;
        int2 m_Goal = new int2(-1);
        uint m_MapVersion = uint.MaxValue;

        public InputFrame Think(SPF.Runtime.World.SimWorld world)
        {
            var frame = default(InputFrame);
            var game = world.Resource(RpgKeys.Game);
            if (game.Flow != RpgFlow.Playing || !world.Registry.TryResolve(game.Hero, out _, out int heroRow))
                return frame;
            var map = world.Resource(RpgKeys.Map);
            var view = map.AsView();
            if (m_Field == null || math.any(m_Field.Size != map.Size)) { m_Field?.Dispose(); m_Field = new FlowField(map.Size); }

            var positions = world.Column(RpgKeys.Position);
            var infos = world.Column(RpgKeys.Info);
            float2 hero = positions[heroRow];
            var health = world.Column(RpgKeys.Health)[heroRow];
            if (health.Fraction < 0.35f && game.Profile.Potions > 0)
                frame.Pressed |= 1u << RpgButton.Potion;

            int count = world.Table(RpgKeys.Actor).Count;
            int target = -1, close = 0, near = 0;
            float best = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                var info = infos[i];
                if (info.Team != Team.Monsters || info.Has(ActorFlags.Dead)) continue;
                float d = math.distancesq(positions[i], hero);
                if (d < 2.2f * 2.2f) close++;
                if (d < 3.8f * 3.8f) near++;
                if (d < best) { best = d; target = i; }
            }
            // Area skills when crowded, a dash out when hurt and cornered (locked slots just do nothing).
            if (close >= 3) frame.Pressed |= 1u << RpgButton.Skill3;
            else if (near >= 4) frame.Pressed |= 1u << RpgButton.Skill4;
            if (health.Fraction < 0.3f && close >= 2) frame.Pressed |= 1u << RpgButton.Skill2;

            float2 goal = target >= 0 ? positions[target] : view.CenterOf(game.StairsCell);
            float dist = math.distance(goal, hero);
            bool sees = view.LineOfSight(hero, goal);
            if (target >= 0)
            {
                float reach = world.Column(RpgKeys.Stats)[heroRow][Stat.Range] + infos[heroRow].Radius + infos[target].Radius;
                if (dist <= reach + 0.3f) frame.Held |= 1u << RpgButton.Attack;
                if (sees && dist < 9f && dist > 2.5f) frame.Pressed |= 1u << RpgButton.Skill;
                if (dist <= reach * 0.8f) return frame;   // in reach: stand and swing
            }
            if (sees && dist < 4f)
            {
                frame.Move = math.normalizesafe(goal - hero);
                return frame;
            }
            int2 cell = view.CellOf(goal);
            if (math.any(cell != m_Goal) || map.Version != m_MapVersion)
            {
                m_Goal = cell;
                m_MapVersion = map.Version;
                Span<int2> goals = stackalloc int2[1];
                goals[0] = cell;
                m_Field.ScheduleBuild(view, goals, default).Complete();
            }
            frame.Move = m_Field.AsView(view).Direction(hero);
            return frame;
        }

        public void Dispose() => m_Field?.Dispose();
    }
}
