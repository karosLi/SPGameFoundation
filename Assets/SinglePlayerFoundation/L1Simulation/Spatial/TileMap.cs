using System;
using System.Runtime.CompilerServices;
using SPF.Contracts;
using Unity.Collections;
using Unity.Mathematics;

namespace SPF.L1.Spatial
{
    /// <summary>
    /// Static tile map (dungeons, arenas, mazes): one byte per tile, 0 = walkable floor, any other value
    /// is a game-defined solid kind. Written on the main thread when a level is built, read by jobs
    /// through <see cref="TileMapView"/> (collision, line of sight, flow fields).
    /// </summary>
    public sealed class TileMap : IDisposable, IResettableResource
    {
        NativeArray<byte> m_Tiles;

        public TileMap(int2 size, float tileSize, float2 origin = default)
        {
            if (math.any(size <= 0)) throw new ArgumentOutOfRangeException(nameof(size));
            Size = size;
            TileSize = tileSize;
            Origin = origin;
            m_Tiles = new NativeArray<byte>(size.x * size.y, Allocator.Persistent);
        }

        public int2 Size { get; }
        public float TileSize { get; }
        public float2 Origin { get; set; }
        public NativeArray<byte> Tiles => m_Tiles;

        /// <summary>Bumped by every edit, so derived data (render meshes, flow fields) knows to rebuild.</summary>
        public uint Version { get; private set; }

        public byte this[int2 cell]
        {
            get => m_Tiles[cell.y * Size.x + cell.x];
            set { m_Tiles[cell.y * Size.x + cell.x] = value; Version++; }
        }

        public void Fill(byte value)
        {
            for (int i = 0; i < m_Tiles.Length; i++) m_Tiles[i] = value;
            Version++;
        }

        public void MarkChanged() => Version++;

        public TileMapView AsView() => new TileMapView(m_Tiles, Size, TileSize, Origin);

        public void OnReset() => Fill(0);

        public void Dispose()
        {
            if (m_Tiles.IsCreated) m_Tiles.Dispose();
        }
    }

    /// <summary>Read-only job view of a <see cref="TileMap"/>. Outside the map counts as solid.</summary>
    public struct TileMapView
    {
        [ReadOnly] NativeArray<byte> m_Tiles;
        public readonly int2 Size;
        public readonly float TileSize;
        public readonly float2 Origin;
        readonly float m_InvTile;

        public TileMapView(NativeArray<byte> tiles, int2 size, float tileSize, float2 origin)
        {
            m_Tiles = tiles;
            Size = size;
            TileSize = tileSize;
            Origin = origin;
            m_InvTile = 1f / tileSize;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int2 CellOf(float2 position) => (int2)math.floor((position - Origin) * m_InvTile);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float2 CenterOf(int2 cell) => Origin + ((float2)cell + 0.5f) * TileSize;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool InBounds(int2 cell) => math.all(cell >= 0) && math.all(cell < Size);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int Index(int2 cell) => cell.y * Size.x + cell.x;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public byte Get(int2 cell) => InBounds(cell) ? m_Tiles[Index(cell)] : (byte)255;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsSolid(int2 cell) => !InBounds(cell) || m_Tiles[Index(cell)] != 0;

        public bool IsSolidAt(float2 position) => IsSolid(CellOf(position));

        /// <summary>
        /// Pushes a circle out of every solid tile it overlaps (closest-point test against each tile's box,
        /// two passes for corners). Radius should not exceed one tile.
        /// </summary>
        public float2 ResolveCircle(float2 position, float radius)
        {
            for (int pass = 0; pass < 2; pass++)
            {
                int2 c = CellOf(position);
                bool moved = false;
                for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int2 cell = c + new int2(dx, dy);
                    if (!IsSolid(cell)) continue;
                    float2 min = Origin + (float2)cell * TileSize, max = min + TileSize;
                    float2 closest = math.clamp(position, min, max);
                    float2 d = position - closest;
                    float distSq = math.lengthsq(d);
                    if (distSq >= radius * radius) continue;
                    if (distSq > 1e-10f)
                    {
                        float dist = math.sqrt(distSq);
                        position += d * ((radius - dist) / dist);
                    }
                    else
                    {
                        // Centre inside the tile: leave through the nearest face.
                        float2 toMin = position - min, toMax = max - position;
                        float best = math.cmin(new float4(toMin, toMax));
                        if (best == toMin.x) position.x = min.x - radius;
                        else if (best == toMax.x) position.x = max.x + radius;
                        else if (best == toMin.y) position.y = min.y - radius;
                        else position.y = max.y + radius;
                    }
                    moved = true;
                }
                if (!moved) break;
            }
            return position;
        }

        /// <summary>Moves a circle by <paramref name="delta"/> in sub-steps of at most half its radius, sliding along walls.</summary>
        public float2 MoveCircle(float2 position, float2 delta, float radius)
        {
            float length = math.length(delta);
            int steps = math.max(1, (int)math.ceil(length / math.max(radius * 0.5f, 1e-3f)));
            steps = math.min(steps, 256);   // bounds the cost of absurd deltas (teleports)
            float2 step = delta / steps;
            for (int i = 0; i < steps; i++)
                position = ResolveCircle(position + step, radius);
            return position;
        }

        /// <summary>True when no solid tile lies on the segment (grid traversal, Amanatides-Woo).</summary>
        public bool LineOfSight(float2 from, float2 to)
        {
            float2 p = (from - Origin) * m_InvTile, q = (to - Origin) * m_InvTile;
            int2 cell = (int2)math.floor(p), end = (int2)math.floor(q);
            float2 d = q - p;
            int2 step = (int2)math.sign(d);
            float2 inv = math.select(1f / d, float.MaxValue, d == 0f);
            float2 next = (float2)(cell + math.max(step, 0));
            float2 tMax = math.select((next - p) * inv, float.MaxValue, d == 0f);
            float2 tDelta = math.abs(math.select(inv, float.MaxValue, d == 0f));
            int guard = math.abs(end.x - cell.x) + math.abs(end.y - cell.y) + 2;
            for (int i = 0; i < guard; i++)
            {
                if (IsSolid(cell)) return false;
                if (math.all(cell == end)) return true;
                if (tMax.x < tMax.y) { tMax.x += tDelta.x; cell.x += step.x; }
                else { tMax.y += tDelta.y; cell.y += step.y; }
            }
            return true;
        }
    }
}
