using System;
using SPF.Contracts;
using SPF.L1.Spatial;
using SPF.Presentation;
using SPF.Runtime.Session;
using SPF.Runtime.World;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace SnakeFoundation.Presentation
{
    /// <summary>
    /// Draws the snake world every frame from the synced simulation state (read-only):
    /// snakes (nodes / strips, opaque / translucent / additive, depth-ordered, translucency budget),
    /// eyes, food (GPU-resident pool or CPU-culled pages), props, projectiles, portals, feedback effects
    /// and the background. Allocation-free per frame after the first frame.
    /// </summary>
    [DefaultExecutionOrder(500)]
    public sealed class SnakeWorldRenderer : MonoBehaviour
    {
        [SerializeField] SessionHost m_Host;
        [SerializeField] SnakeCameraRig m_Camera;
        [SerializeField, Tooltip("Force a render tier (testing)")] bool m_ForceTier;
        [SerializeField] RenderTier m_Tier = RenderTier.DataTexture;
        [SerializeField] int m_MaxNodes = 40000;

        static readonly float4[] s_FoodPalette =
        {
            new float4(1.00f, 0.42f, 0.42f, 1f), new float4(1.00f, 0.78f, 0.30f, 1f), new float4(0.55f, 0.90f, 0.40f, 1f),
            new float4(0.35f, 0.80f, 1.00f, 1f), new float4(0.75f, 0.55f, 1.00f, 1f), new float4(1.00f, 0.55f, 0.85f, 1f),
            new float4(0.40f, 1.00f, 0.85f, 1f), new float4(1.00f, 1.00f, 0.60f, 1f),
        };
        static readonly float4[] s_PropColors =
        {
            new float4(1.0f, 0.85f, 0.2f, 1f),  // speed
            new float4(0.3f, 0.9f, 1.0f, 1f),   // magnet
            new float4(0.6f, 1.0f, 0.5f, 1f),   // shield
            new float4(0.9f, 0.5f, 1.0f, 1f),
        };

        const float FoodDepth = 5f, PropDepth = 4.5f, ProjectileDepth = 4f, SnakeDepthTop = 3f, EffectDepth = -20f;

        RenderAssets m_Assets;
        ChainRenderer m_Chains;
        PointCloudRenderer m_FoodPool;
        CircleBatch m_Food;
        CircleBatch m_Opaque;          // eyes, props
        CircleBatch m_TranslucentHeads;
        CircleBatch m_OpaqueHeads;
        CircleBatch m_Additive;        // projectiles, portals, effects
        Material m_BackgroundMaterial;
        Mesh m_Quad;
        NativeArray<int2> m_Visible;   // (row, mass bits) sorted by mass
        Effect[] m_Effects = new Effect[128];
        int m_EffectCursor;
        SimSession m_BoundSession;

        public SessionHost Host { get => m_Host; set => m_Host = value; }
        public SnakeCameraRig CameraRig { get => m_Camera; set => m_Camera = value; }
        public RenderTier Tier => m_Assets?.Tier ?? RenderTier.DataTexture;
        public int LastVisibleSnakes { get; private set; }
        public int LastTranslucentSnakes { get; private set; }
        public int LastFoodDrawn { get; private set; }

        struct Effect
        {
            public float2 Position;
            public float Size;
            public float Age;
            public float Life;
            public float4 Color;
        }

        /// <summary>Call after the world was reset: GPU mirrors are rebuilt from scratch.</summary>
        public void ResetCaches() => m_Chains?.Invalidate();

        void OnDisable() => Release();
        void OnDestroy() => Release();

        void Release()
        {
            m_Chains?.Dispose();
            m_FoodPool?.Dispose();
            m_Food?.Dispose();
            m_Opaque?.Dispose();
            m_TranslucentHeads?.Dispose();
            m_OpaqueHeads?.Dispose();
            m_Additive?.Dispose();
            if (m_Visible.IsCreated) m_Visible.Dispose();
            if (m_BackgroundMaterial != null) Destroy(m_BackgroundMaterial);
            if (m_Quad != null) Destroy(m_Quad);
            m_Chains = null;
            m_FoodPool = null;
            m_Food = m_Opaque = m_TranslucentHeads = m_OpaqueHeads = m_Additive = null;
            m_BoundSession = null;
            m_Assets = null;
        }

        void Bind(SimSession session)
        {
            Release();
            var world = session.World;
            var tier = m_ForceTier ? m_Tier : RenderCapabilities.Detect();
            m_Assets = new RenderAssets(tier);
            int snakeCapacity = world.Table(SnakeKeys.Snake).Capacity;
            var bodies = world.Resource(SnakeKeys.Bodies);
            m_Chains = new ChainRenderer(m_Assets, snakeCapacity, m_MaxNodes, bodies.TotalPoints, snakeCapacity);
            int foodCapacity = world.Table(SnakeKeys.Food).Capacity;
            if (tier == RenderTier.GpuDriven)
                m_FoodPool = new PointCloudRenderer(m_Assets, BlendKind.Opaque, foodCapacity, foodCapacity);
            else
                m_Food = new CircleBatch(m_Assets, BlendKind.Opaque, 16384);
            m_Opaque = new CircleBatch(m_Assets, BlendKind.Opaque, snakeCapacity * 4 + world.Table(SnakeKeys.Prop).Capacity * 2);
            m_OpaqueHeads = new CircleBatch(m_Assets, BlendKind.Opaque, snakeCapacity);
            m_TranslucentHeads = new CircleBatch(m_Assets, BlendKind.Translucent, snakeCapacity);
            m_Additive = new CircleBatch(m_Assets, BlendKind.Additive, world.Table(SnakeKeys.Projectile).Capacity + 64 + m_Effects.Length * 2, shaded: false);
            m_Visible = new NativeArray<int2>(snakeCapacity, Allocator.Persistent);
            if (m_Assets.BackgroundShader != null)
                m_BackgroundMaterial = new Material(m_Assets.BackgroundShader) { hideFlags = HideFlags.HideAndDontSave, renderQueue = 1900 };
            m_Quad = CreateQuad();
            m_BoundSession = session;
        }

        void LateUpdate()
        {
            var session = m_Host != null ? m_Host.Session : null;
            if (session == null || m_Camera == null) return;
            if (session != m_BoundSession) Bind(session);

            var world = session.World;
            var game = world.Resource(SnakeKeys.Game);
            var config = world.Resource(SnakeKeys.Config);
            var quality = world.Resource(SnakeKeys.Quality);
            float alpha = session.InterpolationAlpha;
            float4 view = m_Camera.ViewRect;
            float margin = 6f;
            float4 cull = view + new float4(-margin, -margin, margin, margin);
            var bounds = new Bounds(new Vector3((view.x + view.z) * 0.5f, (view.y + view.w) * 0.5f, 0f), new Vector3(view.z - view.x + 20f, view.w - view.y + 20f, 200f));
            float pixelsPerUnit = Screen.height / math.max(view.w - view.y, 1f);
            float time = Time.time;

            DrawBackground(config.Regions[game.ActiveRegion], view);
            DrawSnakes(world, config, quality, game, alpha, cull, pixelsPerUnit);
            DrawFood(world, cull);
            DrawItems(world, config, game, cull, time);
            UpdateEffects(world);

            m_Chains.Draw(alpha, cull, bounds);
            m_OpaqueHeads.Draw(bounds);
            m_TranslucentHeads.Draw(bounds);
            m_Opaque.Draw(bounds);
            if (m_FoodPool != null) m_FoodPool.Draw(cull, bounds);
            else m_Food.Draw(bounds);
            m_Additive.Draw(bounds);
        }

        void DrawBackground(in RegionDef region, float4 view)
        {
            if (m_BackgroundMaterial == null) return;
            m_BackgroundMaterial.SetVector(RenderAssets.Ids.Region, new Vector4(region.Min.x, region.Min.y, region.Max.x, region.Max.y));
            float2 center = (view.xy + view.zw) * 0.5f;
            float2 size = view.zw - view.xy + 4f;
            var matrix = Matrix4x4.TRS(new Vector3(center.x, center.y, 10f), Quaternion.identity, new Vector3(size.x, size.y, 1f));
            Graphics.RenderMesh(new RenderParams(m_BackgroundMaterial), m_Quad, 0, matrix);
        }

        void DrawSnakes(SimWorld world, SnakeRuntimeConfig config, SnakeQuality quality, SnakeGameState game, float alpha, float4 cull, float pixelsPerUnit)
        {
            var table = world.Table(SnakeKeys.Snake);
            var infos = table.Column(SnakeKeys.Info);
            var bounds = table.Column(SnakeKeys.Bounds);
            var masses = table.Column(SnakeKeys.Mass);
            int region = game.ActiveRegion;

            int visible = 0;
            for (int row = 0; row < table.Count; row++)
            {
                var info = infos[row];
                var b = bounds[row];
                if (info.Region != region || b.x > cull.z || b.y > cull.w || b.z < cull.x || b.w < cull.y) continue;
                // Player always on top; otherwise heavier snakes above lighter ones.
                float key = info.Has(SnakeFlags.Player) ? float.MaxValue : masses[row];
                m_Visible[visible++] = new int2(row, math.asint(key));
            }
            SortByKey(m_Visible, visible);
            LastVisibleSnakes = visible;

            var heads = table.Column(SnakeKeys.Head);
            var prevHeads = table.Column(SnakeKeys.PrevHead);
            var headings = table.Column(SnakeKeys.Heading);
            var trails = table.Column(SnakeKeys.Trail);
            var radii = table.Column(SnakeKeys.Radius);
            var prevArcs = table.Column(SnakeKeys.PrevArc);
            var s = config.Settings;
            var skins = config.Skins;
            m_Chains.Begin(world.Resource(SnakeKeys.Bodies).Points);
            m_Opaque.Count = 0;
            m_OpaqueHeads.Count = 0;
            m_TranslucentHeads.Count = 0;

            // Translucency budget counted from the top (nearest) snake down.
            int translucentLeft = quality.TranslucentBudget;
            int translucentUsed = 0;
            for (int o = visible - 1; o >= 0; o--)
            {
                int row = m_Visible[o].x;
                if (skins[infos[row].Skin % skins.Length].Blend != SkinBlend.Opaque)
                {
                    if (translucentLeft > 0) { translucentLeft--; translucentUsed++; }
                    else m_Visible[o] = new int2(row, -1);   // mark: render with opaque fallback
                }
            }
            LastTranslucentSnakes = translucentUsed;

            for (int o = 0; o < visible; o++)
            {
                int row = m_Visible[o].x;
                bool fallback = m_Visible[o].y == -1;
                var info = infos[row];
                var skin = skins[info.Skin % skins.Length];
                var blend = fallback || skin.Blend == SkinBlend.Opaque ? BlendKind.Opaque
                    : skin.Blend == SkinBlend.Additive ? BlendKind.Additive : BlendKind.Translucent;
                float depth = SnakeDepthTop - (visible - 1 - o) * 0.1f;
                float radius = radii[row];
                float4 colorA = fallback ? ToFloat4(skin.OpaqueFallback) : ToFloat4(skin.Primary, skin.Alpha);
                float4 colorB = fallback ? ToFloat4(skin.OpaqueFallback) * new float4(0.85f, 0.85f, 0.85f, 1f) : ToFloat4(skin.Secondary, skin.Alpha);
                if (info.Protection > 0f)
                {
                    float blink = 0.6f + 0.4f * math.sin(Time.time * 18f);
                    colorA.xyz *= blink;
                    colorB.xyz *= blink;
                }
                float stride = math.max(quality.NodeStride, radius * pixelsPerUnit < 3f ? 2 : 1);
                var trail = trails[row];
                float2 head = math.lerp(prevHeads[row], heads[row], alpha);

                m_Chains.Add(new ChainDesc
                {
                    Key = row,
                    Identity = info.Id,
                    Trail = trail,
                    HeadPrev = prevHeads[row],
                    HeadCurr = heads[row],
                    ArcPrev = prevArcs[row],
                    ArcCurr = math.length(heads[row] - trail.Last),
                    Spacing = s.TrailSpacing,
                    Radius = radius,
                    NodeSpacing = s.NodeSpacing(radius),
                    NodeStride = stride,
                    Depth = depth,
                    ColorA = colorA,
                    ColorB = colorB,
                    Stripe = skin.Stripe,
                    Blend = blend,
                    Shape = skin.Shape == SkinShape.Strip ? ChainShape.Strip : ChainShape.Nodes,
                });

                // Strip chains get a round head cap (nodes chains draw their head as node 0).
                if (skin.Shape == SkinShape.Strip)
                {
                    var batch = blend == BlendKind.Opaque ? m_OpaqueHeads : m_TranslucentHeads;
                    batch.Add(head, radius * 1.12f, depth - 0.001f, colorA);
                }

                // Eyes: whites + pupils looking along the heading.
                float2 heading = headings[row];
                float2 side = new float2(-heading.y, heading.x);
                float eyeR = radius * 0.38f;
                for (int e = -1; e <= 1; e += 2)
                {
                    float2 eye = head + heading * radius * 0.35f + side * (radius * 0.45f * e);
                    m_Opaque.Add(eye, eyeR, depth - 0.02f, new float4(1f, 1f, 1f, 1f));
                    m_Opaque.Add(eye + heading * eyeR * 0.35f, eyeR * 0.55f, depth - 0.03f, new float4(0.08f, 0.08f, 0.12f, 1f));
                }
            }
        }

        void DrawFood(SimWorld world, float4 cull)
        {
            var table = world.Table(SnakeKeys.Food);
            var positions = table.Column(SnakeKeys.FoodPosition);
            var infos = table.Column(SnakeKeys.FoodInfo);

            if (m_FoodPool != null)
            {
                var changes = table.Changes;
                if (changes.All)
                {
                    for (int row = 0; row < table.Count && !m_FoodPool.StagingFull; row++)
                        m_FoodPool.StageRow(row, FoodInstance(positions[row], infos[row]));
                }
                else
                {
                    for (int i = 0; i < changes.Count && !m_FoodPool.StagingFull; i++)
                    {
                        int row = changes[i];
                        m_FoodPool.StageRow(row, row < table.Count ? FoodInstance(positions[row], infos[row]) : default);
                    }
                }
                changes.Clear();
                m_FoodPool.PoolCount = table.Count;
                LastFoodDrawn = table.Count;
                return;
            }

            // Data-texture tier: CPU cull through the item grid (only the cells under the view).
            var grid = world.Resource(SnakeKeys.ItemGrid).AsReader();
            var visitor = new FoodCollector { Batch = m_Food, Positions = positions, Infos = infos, Count = 0 };
            m_Food.Count = 0;
            grid.QueryCells(cull.xy, cull.zw, ref visitor);
            m_Food.Count = visitor.Count;
            LastFoodDrawn = visitor.Count;
            table.Changes?.Clear();
        }

        struct FoodCollector : IGridVisitor
        {
            public CircleBatch Batch;
            public NativeArray<float2> Positions;
            public NativeArray<FoodInfo> Infos;
            public int Count;

            public bool Visit(in GridEntry entry)
            {
                if (entry.Data != 0 || entry.Owner >= Infos.Length) return true;
                if (Count >= Batch.Capacity) return false;
                Batch.Instances.Set(Count++, FoodInstance(Positions[entry.Owner], Infos[entry.Owner]));
                return true;
            }
        }

        static InstanceData FoodInstance(float2 position, in FoodInfo info) =>
            new InstanceData(position, info.Radius, FoodDepth, s_FoodPalette[info.Color & 7]);

        void DrawItems(SimWorld world, SnakeRuntimeConfig config, SnakeGameState game, float4 cull, float time)
        {
            m_Additive.Count = 0;

            var props = world.Table(SnakeKeys.Prop);
            var propPositions = props.Column(SnakeKeys.PropPosition);
            var propInfos = props.Column(SnakeKeys.PropInfo);
            float pulse = 1f + 0.12f * math.sin(time * 5f);
            for (int i = 0; i < props.Count; i++)
            {
                float2 p = propPositions[i];
                if (!Inside(p, cull)) continue;
                var info = propInfos[i];
                var color = s_PropColors[(info.Kind - 1) & 3];
                m_Opaque.Add(p, info.Radius * pulse, PropDepth, color);
                m_Opaque.Add(p, info.Radius * 0.45f * pulse, PropDepth - 0.01f, new float4(1f, 1f, 1f, 1f));
            }

            var projectiles = world.Table(SnakeKeys.Projectile);
            var projectilePositions = projectiles.Column(SnakeKeys.ProjectilePosition);
            var states = projectiles.Column(SnakeKeys.ProjectileState);
            for (int i = 0; i < projectiles.Count; i++)
            {
                float2 p = projectilePositions[i];
                if (!Inside(p, cull)) continue;
                float r = states[i].Radius;
                m_Additive.Add(p, r * 2.2f, ProjectileDepth, new float4(1f, 0.5f, 0.2f, 0.35f));
                m_Additive.Add(p, r, ProjectileDepth - 0.01f, new float4(1f, 0.9f, 0.6f, 1f));
            }

            for (int i = 0; i < config.PortalCount; i++)
            {
                var portal = config.Portals[i];
                if (portal.FromRegion != game.ActiveRegion || !Inside(portal.Position, cull + new float4(-portal.Radius, -portal.Radius, portal.Radius, portal.Radius))) continue;
                float swirl = 0.5f + 0.5f * math.sin(time * 3f);
                m_Additive.Add(portal.Position, portal.Radius * (1.25f + 0.1f * swirl), ProjectileDepth, new float4(0.4f, 0.3f, 1f, 0.25f));
                m_Additive.Add(portal.Position, portal.Radius, ProjectileDepth - 0.01f, new float4(0.5f, 0.6f, 1f, 0.5f));
                m_Additive.Add(portal.Position, portal.Radius * (0.4f + 0.2f * swirl), ProjectileDepth - 0.02f, new float4(1f, 1f, 1f, 0.6f));
            }
        }

        void UpdateEffects(SimWorld world)
        {
            var feedback = world.Resource(SnakeKeys.Feedback);
            for (int i = 0; i < feedback.Count; i++)
            {
                var e = feedback[i];
                switch (e.Kind)
                {
                    case FeedbackKind.Death: Spawn(e.Position, e.Size * 4f, 0.8f, new float4(1f, 0.4f, 0.3f, 0.6f)); break;
                    case FeedbackKind.Kill: Spawn(e.Position, e.Size * 6f, 1.0f, new float4(1f, 0.9f, 0.3f, 0.7f)); break;
                    case FeedbackKind.Eat: Spawn(e.Position, e.Size * 3f, 0.25f, new float4(1f, 1f, 1f, 0.5f)); break;
                    case FeedbackKind.Pickup: Spawn(e.Position, 4f, 0.6f, new float4(0.4f, 1f, 0.7f, 0.7f)); break;
                    case FeedbackKind.Hit: Spawn(e.Position, 3f, 0.4f, new float4(1f, 0.6f, 0.2f, 0.8f)); break;
                    case FeedbackKind.Portal: Spawn(e.Position, 12f, 1.2f, new float4(0.5f, 0.6f, 1f, 0.8f)); break;
                }
            }
            feedback.Clear();

            float dt = Time.deltaTime;
            for (int i = 0; i < m_Effects.Length; i++)
            {
                ref var fx = ref m_Effects[i];
                if (fx.Life <= 0f) continue;
                fx.Age += dt;
                if (fx.Age >= fx.Life) { fx.Life = 0f; continue; }
                float t = fx.Age / fx.Life;
                float4 c = fx.Color;
                c.w *= 1f - t;
                m_Additive.Add(fx.Position, fx.Size * (0.3f + t), EffectDepth, c);
            }
        }

        void Spawn(float2 position, float size, float life, float4 color)
        {
            m_Effects[m_EffectCursor] = new Effect { Position = position, Size = size, Life = life, Color = color };
            m_EffectCursor = (m_EffectCursor + 1) % m_Effects.Length;
        }

        static bool Inside(float2 p, float4 rect) => p.x >= rect.x && p.y >= rect.y && p.x <= rect.z && p.y <= rect.w;

        static float4 ToFloat4(Color32 c, float alpha = 1f) => new float4(c.r / 255f, c.g / 255f, c.b / 255f, alpha);

        /// <summary>Insertion sort by the key in .y (float bits, all non-negative) — ≤ 150 items, allocation-free.</summary>
        static void SortByKey(NativeArray<int2> items, int count)
        {
            for (int i = 1; i < count; i++)
            {
                var item = items[i];
                float key = math.asfloat(item.y);
                int j = i - 1;
                while (j >= 0 && math.asfloat(items[j].y) > key)
                {
                    items[j + 1] = items[j];
                    j--;
                }
                items[j + 1] = item;
            }
        }

        static Mesh CreateQuad()
        {
            var mesh = new Mesh { name = "SPF Background Quad", hideFlags = HideFlags.HideAndDontSave };
            mesh.SetVertices(new[] { new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f), new Vector3(0.5f, 0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f) });
            mesh.SetIndices(new[] { 0, 2, 1, 0, 3, 2 }, MeshTopology.Triangles, 0);
            return mesh;
        }
    }
}
