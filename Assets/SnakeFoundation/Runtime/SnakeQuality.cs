namespace SnakeFoundation
{
    /// <summary>
    /// Runtime quality knobs that the adaptive performance controller turns under load.
    /// Simulation reads them on the main thread when scheduling; presentation reads the render ones.
    /// The simulation tick rate is never changed (gameplay feel stays identical).
    /// </summary>
    public sealed class SnakeQuality
    {
        /// <summary>0 = best. Each level trades fidelity for time.</summary>
        public int Level { get; set; }

        /// <summary>Minimum ticks between AI decisions (max with the config value).</summary>
        public int AIDecisionIntervalTicks { get; set; } = 1;

        /// <summary>How many translucent snakes may render translucent at once; the rest use their opaque fallback.</summary>
        public int TranslucentBudget { get; set; } = 30;

        /// <summary>Body node LOD: draw every Nth node (radius scaled) when > 1.</summary>
        public int NodeStride { get; set; } = 1;

        /// <summary>Fraction of the screen resolution to render at.</summary>
        public float RenderScale { get; set; } = 1f;
    }
}
