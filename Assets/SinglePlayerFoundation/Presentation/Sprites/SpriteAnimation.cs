using Unity.Mathematics;

namespace SPF.Presentation.Sprites
{
    /// <summary>A run of consecutive atlas frames played at a fixed rate.</summary>
    public struct SpriteClip
    {
        public int First;
        public int Count;
        public float Fps;
        public bool Loop;

        public SpriteClip(int first, int count, float fps, bool loop)
        {
            First = first;
            Count = math.max(count, 1);
            Fps = fps;
            Loop = loop;
        }

        public float Duration => Count / math.max(Fps, 1e-3f);

        /// <summary>Atlas frame at <paramref name="time"/> seconds (clamps on the last frame when not looping).</summary>
        public int FrameAt(float time)
        {
            int f = (int)math.floor(math.max(time, 0f) * Fps);
            f = Loop ? f % Count : math.min(f, Count - 1);
            return First + f;
        }

        /// <summary>Atlas frame at a normalised progress 0..1 (actions driven by simulation timers).</summary>
        public int FrameAtProgress(float t) => First + math.min((int)(math.saturate(t) * Count), Count - 1);
    }

    /// <summary>
    /// Per-entity playback state, presentation side (frame-rate time, not simulation ticks). Switching to
    /// another clip restarts it; <see cref="Play"/> with the current clip keeps the time running.
    /// </summary>
    public struct SpriteAnimator
    {
        public int Clip;      // game-defined clip id
        public float Time;

        public void Play(int clip, bool restart = false)
        {
            if (clip == Clip && !restart) return;
            Clip = clip;
            Time = 0f;
        }

        public void Advance(float dt) => Time += dt;

        public bool Finished(in SpriteClip clip) => !clip.Loop && Time >= clip.Duration;
    }
}
