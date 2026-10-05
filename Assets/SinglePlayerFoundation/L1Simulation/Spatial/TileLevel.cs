using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

namespace SPF.L1.Spatial
{
    /// <summary>
    /// A symbol in a level's text form: a tile value on a layer, or a marker (spawn point, enemy, pickup)
    /// that the game turns into entities when it builds the level.
    /// </summary>
    [Serializable]
    public struct TileSymbol
    {
        public char Symbol;
        public string Name;
        public int Layer;        // tile layer (ignored for markers)
        public byte Value;       // tile value (ignored for markers)
        public bool Marker;
        public Color Color;      // editor swatch
    }

    [Serializable]
    public struct TileMarker
    {
        public char Symbol;
        public Vector2Int Cell;
    }

    /// <summary>
    /// Level data as an asset: tile layers (one byte per cell each) plus markers, painted in the editor
    /// (SPF → Tile Level Editor) or imported from text rows, and copied into <see cref="TileMap"/>s and entities
    /// when a level is built. Text import/export keeps levels diffable and lets code-defined levels become
    /// assets without hand conversion. Row 0 of the text is the top of the level.
    /// </summary>
    [CreateAssetMenu(menuName = "SPF/Tile Level", fileName = "Level")]
    public sealed class TileLevelAsset : ScriptableObject
    {
        public int Width = 32, Height = 16;
        public int LayerCount = 1;
        public TileSymbol[] Legend = Array.Empty<TileSymbol>();
        [SerializeField] byte[] m_Tiles = Array.Empty<byte>();     // layer-major, row-major, bottom row first
        public List<TileMarker> Markers = new List<TileMarker>();

        /// <summary>Bumped on every edit (renderers and tools rebuild).</summary>
        public int Revision { get; private set; }

        public void Resize(int width, int height, int layers)
        {
            width = Math.Max(1, width); height = Math.Max(1, height); layers = Math.Max(1, layers);
            var tiles = new byte[width * height * layers];
            for (int l = 0; l < Math.Min(layers, LayerCount); l++)
                for (int y = 0; y < Math.Min(height, Height); y++)
                    for (int x = 0; x < Math.Min(width, Width); x++)
                        if (Index(l, x, y) < m_Tiles.Length) tiles[(l * height + y) * width + x] = m_Tiles[Index(l, x, y)];
            Width = width; Height = height; LayerCount = layers;
            m_Tiles = tiles;
            Markers.RemoveAll(m => m.Cell.x >= width || m.Cell.y >= height);
            Revision++;
        }

        int Index(int layer, int x, int y) => (layer * Height + y) * Width + x;

        public bool InBounds(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height;

        public byte Get(int layer, int x, int y)
        {
            if (!InBounds(x, y) || layer < 0 || layer >= LayerCount) return 0;
            int i = Index(layer, x, y);
            return i < m_Tiles.Length ? m_Tiles[i] : (byte)0;
        }

        public void Set(int layer, int x, int y, byte value)
        {
            if (!InBounds(x, y) || layer < 0 || layer >= LayerCount) return;
            if (m_Tiles.Length != Width * Height * LayerCount) Resize(Width, Height, LayerCount);
            m_Tiles[Index(layer, x, y)] = value;
            Revision++;
        }

        public void SetMarker(char symbol, int x, int y)
        {
            RemoveMarkers(x, y);
            if (symbol != ' ' && InBounds(x, y)) Markers.Add(new TileMarker { Symbol = symbol, Cell = new Vector2Int(x, y) });
            Revision++;
        }

        public void RemoveMarkers(int x, int y)
        {
            Markers.RemoveAll(m => m.Cell.x == x && m.Cell.y == y);
            Revision++;
        }

        public int FindSymbol(char symbol)
        {
            for (int i = 0; i < Legend.Length; i++) if (Legend[i].Symbol == symbol) return i;
            return -1;
        }

        /// <summary>Paints with a legend symbol: sets its tile (clearing other layers' markers stay) or places its marker; ' ' erases.</summary>
        public void Paint(char symbol, int x, int y)
        {
            if (!InBounds(x, y)) return;
            if (symbol == ' ')
            {
                for (int l = 0; l < LayerCount; l++) Set(l, x, y, 0);
                RemoveMarkers(x, y);
                return;
            }
            int s = FindSymbol(symbol);
            if (s < 0) return;
            var entry = Legend[s];
            if (entry.Marker) SetMarker(symbol, x, y);
            else Set(entry.Layer, x, y, entry.Value);
        }

        /// <summary>Builds the level from text rows (top row first); unknown characters are left empty.</summary>
        public void FromRows(string[] rows)
        {
            int width = 0;
            foreach (var r in rows) width = Math.Max(width, r.Length);
            m_Tiles = Array.Empty<byte>();
            Width = 0; Height = 0;
            Resize(width, rows.Length, Math.Max(1, LayerCount));
            Markers.Clear();
            for (int r = 0; r < rows.Length; r++)
            {
                int y = rows.Length - 1 - r;
                for (int x = 0; x < rows[r].Length; x++)
                {
                    char c = rows[r][x];
                    if (c != ' ') Paint(c, x, y);
                }
            }
            Revision++;
        }

        /// <summary>The level as text rows (top row first): markers win over tiles, higher layers over lower.</summary>
        public string[] ToRows()
        {
            var rows = new string[Height];
            var line = new char[Width];
            for (int y = 0; y < Height; y++)
            {
                for (int x = 0; x < Width; x++)
                {
                    char c = ' ';
                    for (int l = LayerCount - 1; l >= 0; l--)
                    {
                        byte v = Get(l, x, y);
                        if (v == 0) continue;
                        foreach (var e in Legend) if (!e.Marker && e.Layer == l && e.Value == v) { c = e.Symbol; break; }
                        if (c != ' ') break;
                    }
                    foreach (var m in Markers) if (m.Cell.x == x && m.Cell.y == y) c = m.Symbol;
                    line[x] = c;
                }
                rows[Height - 1 - y] = new string(line).TrimEnd();
            }
            return rows;
        }

        /// <summary>Copies layer <paramref name="layer"/> into a tile map (sizes may differ: the overlap is copied, the rest cleared).</summary>
        public void CopyTo(int layer, TileMap map)
        {
            map.Fill(0);
            int w = Math.Min(Width, map.Size.x), h = Math.Min(Height, map.Size.y);
            var tiles = map.Tiles;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    tiles[y * map.Size.x + x] = Get(layer, x, y);
            map.MarkChanged();
        }

        /// <summary>A runtime instance (tests, code-defined levels).</summary>
        public static TileLevelAsset Create(TileSymbol[] legend, int layers, string[] rows)
        {
            var level = CreateInstance<TileLevelAsset>();
            level.hideFlags = HideFlags.DontSave;
            level.Legend = legend;
            level.LayerCount = layers;
            level.FromRows(rows);
            return level;
        }
    }
}
