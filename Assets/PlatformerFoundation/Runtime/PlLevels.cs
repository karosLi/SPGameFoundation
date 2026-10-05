using SPF.L1.Spatial;
using UnityEngine;

namespace PlatformerFoundation
{
    /// <summary>
    /// Levels as text, top row first. '#' ground, '=' one-way ledge, '^' spikes, 'o' coin, 'w' walker,
    /// '-' moving platform (sideways), '|' moving platform (up and down), 'S' start, 'G' goal flag,
    /// 't' torch (decoration and a light on night levels).
    /// </summary>
    public static class PlLevels
    {
        /// <summary>
        /// The text symbols as a level-asset legend: layer 0 is collision, layer 1 hazards; the rest are markers.
        /// Designers paint <see cref="TileLevelAsset"/>s with it (SPF → Tile Level Editor) and assign them to
        /// <see cref="Overrides"/>; without overrides the built-in text levels below are used.
        /// </summary>
        public static readonly TileSymbol[] Legend =
        {
            new TileSymbol { Symbol = '#', Name = "Ground", Layer = 0, Value = PlTile.Solid, Color = new Color(0.47f, 0.32f, 0.2f) },
            new TileSymbol { Symbol = '=', Name = "Ledge (one-way)", Layer = 0, Value = PlTile.OneWay, Color = new Color(0.67f, 0.47f, 0.27f) },
            new TileSymbol { Symbol = '^', Name = "Spikes", Layer = 1, Value = PlTile.Spikes, Color = new Color(0.8f, 0.8f, 0.86f) },
            new TileSymbol { Symbol = 'S', Name = "Start", Marker = true, Color = new Color(0.3f, 0.5f, 1f) },
            new TileSymbol { Symbol = 'G', Name = "Goal", Marker = true, Color = new Color(0.3f, 0.9f, 0.45f) },
            new TileSymbol { Symbol = 'o', Name = "Coin", Marker = true, Color = new Color(1f, 0.82f, 0.25f) },
            new TileSymbol { Symbol = 'w', Name = "Walker", Marker = true, Color = new Color(0.55f, 0.3f, 0.25f) },
            new TileSymbol { Symbol = '-', Name = "Platform (sideways)", Marker = true, Color = new Color(0.6f, 0.6f, 0.7f) },
            new TileSymbol { Symbol = '|', Name = "Platform (up/down)", Marker = true, Color = new Color(0.5f, 0.5f, 0.65f) },
            new TileSymbol { Symbol = 't', Name = "Torch", Marker = true, Color = new Color(1f, 0.55f, 0.2f) },
        };

        /// <summary>Designer-made levels replacing the built-in ones (null = built-in).</summary>
        public static TileLevelAsset[] Overrides;
        static TileLevelAsset[] s_BuiltIn;

        public static int Count => Overrides != null && Overrides.Length > 0 ? Overrides.Length : All.Length;

        /// <summary>Level <paramref name="index"/> as an asset (built-in text levels are converted once).</summary>
        public static TileLevelAsset Asset(int index)
        {
            if (Overrides != null && Overrides.Length > 0) return Overrides[Mathf.Clamp(index, 0, Overrides.Length - 1)];
            s_BuiltIn ??= new TileLevelAsset[All.Length];
            index = Mathf.Clamp(index, 0, All.Length - 1);
            return s_BuiltIn[index] != null ? s_BuiltIn[index] : (s_BuiltIn[index] = TileLevelAsset.Create(Legend, 2, All[index]));
        }

        public static bool IsNight(int index) => Overrides == null || Overrides.Length == 0 ? Night[Mathf.Clamp(index, 0, Night.Length - 1)] : HasTorches(Asset(index));

        static bool HasTorches(TileLevelAsset level)
        {
            foreach (var m in level.Markers) if (m.Symbol == 't') return true;
            return false;
        }

        /// <summary>Night levels are drawn with lit sprites: dark ambient, torches and the hero's lantern.</summary>
        public static readonly bool[] Night = { false, false, true };

        public static readonly string[][] All =
        {
            new[]
            {
                "                                                                      ",
                "                                                                      ",
                "                                                          o o o       ",
                "                                                         =======    G ",
                "                         o o                                     #####",
                "                        =====          o   o   o                 #####",
                "              o o                     ===========       -        #####",
                "             =====                                               #####",
                "                                                                 #####",
                "      o                      w                  w                #####",
                " S   ===       ###      ###########       ##########     ^^^^    #####",
                "#########     #####    #############     ############   ###############",
                "#########^^^^^#####^^^^#############^^^^^############^^^###############",
            },
            new[]
            {
                "                                                                        ",
                "                                                           o o        G ",
                "                                                          =====    #####",
                "                          o o o                                    #####",
                "                         =======                |                  #####",
                "             o                                                     #####",
                "            ===            w                                       #####",
                "                    ###############       o o                      #####",
                "     o              #             #      =====                     #####",
                " S  ===     -       #  o  o  o    #                    w           #####",
                "######              #  #######    #####           ###########      #####",
                "######^^^^^^^^^^^^^^#  #     #    #####^^^^^^^^^^^#         #^^^^^^#####",
            },
            new[]
            {
                "                                                                              ",
                "                                                                         G    ",
                "                                                              o o o   #######",
                "                                         w                   =====    #######",
                "                    o o             ##########          |             #######",
                "                   =====    -                                         #######",
                "         o o                                     o o                  #######",
                "        =====                                   =====                 #######",
                " tS          t      t  w        w t                               t   #######",
                "#####       ##############    ######    ^^^                    ^^^    #######",
                "#####^^^^^^^##############^^^^######^^^^###^^^^^^^^^^^^^^^^^^^^###^^^^#######",
            },
        };
    }
}
