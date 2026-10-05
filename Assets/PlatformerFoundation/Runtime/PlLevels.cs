namespace PlatformerFoundation
{
    /// <summary>
    /// Levels as text, top row first. '#' ground, '=' one-way ledge, '^' spikes, 'o' coin, 'w' walker,
    /// '-' moving platform (sideways), '|' moving platform (up and down), 'S' start, 'G' goal flag,
    /// 't' torch (decoration and a light on night levels).
    /// </summary>
    public static class PlLevels
    {
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
