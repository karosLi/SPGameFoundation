using SPF.L1.Physics;
using Unity.Mathematics;

namespace SlingFoundation
{
    /// <summary>The three levels, built from physics bodies (posts, planks, blocks and targets on a ground slab).</summary>
    public static class SlLevels
    {
        public const int Count = 3;

        struct Builder
        {
            public PhysicsWorld2D World;
            public SlGameState Game;

            public int Piece(PieceKind kind, Body body)
            {
                int id = World.Add(body);
                if (id < 0) return id;
                Game.Kind[id] = kind;
                Game.Hp[id] = SlRules.Toughness(kind);
                if (kind == PieceKind.Target) Game.TargetsLeft++;
                return id;
            }

            static float Density(PieceKind kind) => kind == PieceKind.Stone ? 2.5f : kind == PieceKind.Glass ? 0.8f : 0.7f;

            public int Block(PieceKind kind, float2 centre, float2 half, float angle = 0f) =>
                Piece(kind, PhysicsWorld2D.BoxBody(centre, half, angle, Density(kind), kind == PieceKind.Glass ? 0.3f : 0.7f, 0.05f));

            /// <summary>Two posts and a lintel standing on <paramref name="baseY"/>; returns the lintel's top.</summary>
            public float Arch(PieceKind kind, float x, float baseY, float width, float height)
            {
                const float post = 0.2f, lintel = 0.18f;
                Block(kind, new float2(x - width * 0.5f + post, baseY + height * 0.5f), new float2(post, height * 0.5f));
                Block(kind, new float2(x + width * 0.5f - post, baseY + height * 0.5f), new float2(post, height * 0.5f));
                Block(kind, new float2(x, baseY + height + lintel), new float2(width * 0.5f + 0.15f, lintel));
                return baseY + height + lintel * 2f;
            }

            public void Target(float2 centre, float radius = 0.45f) =>
                Piece(PieceKind.Target, PhysicsWorld2D.CircleBody(centre, radius, 0.6f, 0.6f, 0.1f));
        }

        public static void Build(PhysicsWorld2D world, SlGameState game, int level)
        {
            world.Clear();
            System.Array.Clear(game.Kind, 0, game.Kind.Length);
            game.TargetsLeft = 0;
            var b = new Builder { World = world, Game = game };
            // Ground: a static slab and the sling's mound.
            b.Piece(PieceKind.Ground, PhysicsWorld2D.BoxBody(new float2(0f, -1f), new float2(60f, 1f), 0f, 0f, 0.8f));
            b.Piece(PieceKind.Ground, PhysicsWorld2D.BoxBody(new float2(SlRules.Sling.x, 0.5f), new float2(1f, 0.5f), 0f, 0f, 0.8f));

            switch (level)
            {
                case 0:
                {
                    float top = b.Arch(PieceKind.Wood, 8f, 0f, 2.6f, 2.2f);
                    b.Target(new float2(8f, 0.45f));
                    b.Target(new float2(8f, top + 0.45f));
                    break;
                }
                case 1:
                {
                    float top = b.Arch(PieceKind.Wood, 6f, 0f, 2.6f, 2f);
                    b.Block(PieceKind.Glass, new float2(6f, top + 0.6f), new float2(0.5f, 0.6f));
                    b.Target(new float2(6f, 0.45f));
                    float top2 = b.Arch(PieceKind.Stone, 11f, 0f, 3f, 1.6f);
                    top2 = b.Arch(PieceKind.Wood, 11f, top2, 2.4f, 1.6f);
                    b.Target(new float2(11f, top2 + 0.45f));
                    b.Target(new float2(11f, 0.45f));
                    break;
                }
                default:
                {
                    // A stone base with a wooden tower and glass panes, three targets.
                    for (int i = 0; i < 4; i++) b.Block(PieceKind.Stone, new float2(6f + i * 1.25f, 0.5f), new float2(0.6f, 0.5f));
                    float top = b.Arch(PieceKind.Wood, 7.9f, 1f, 3.6f, 1.8f);
                    b.Target(new float2(7.9f, 1.45f));
                    top = b.Arch(PieceKind.Wood, 7.9f, top, 2.8f, 1.6f);
                    b.Block(PieceKind.Glass, new float2(6.9f, top + 0.5f), new float2(0.15f, 0.5f));
                    b.Block(PieceKind.Glass, new float2(8.9f, top + 0.5f), new float2(0.15f, 0.5f));
                    b.Target(new float2(7.9f, top + 0.45f));
                    b.Target(new float2(13f, 0.45f));
                    b.Block(PieceKind.Wood, new float2(12.2f, 0.9f), new float2(0.15f, 0.9f));
                    break;
                }
            }
            game.BirdsLeft = SlRules.BirdsPerLevel;
            game.Bird = -1;
            game.Flow = SlFlow.Aiming;
            game.FlightTime = game.StillTime = game.ClearTimer = 0f;
            game.Level = level;
            game.LevelBuilds++;
            game.Version++;
        }
    }
}
