using System.IO;
using SPF.Runtime.Persistence;
using UnityEngine;

namespace RpgFoundation.Game
{
    /// <summary>Player settings (sound), saved in their own slot so they survive new games.</summary>
    public sealed class RpgOptions : ISaveData
    {
        public const string Slot = "options";

        public int Version => 1;
        public float SfxVolume = 0.8f;
        public float MusicVolume = 0.6f;
        public bool Muted;

        public void Write(BinaryWriter w)
        {
            w.Write(SfxVolume);
            w.Write(MusicVolume);
            w.Write(Muted);
        }

        public bool Read(BinaryReader r, int version)
        {
            SfxVolume = Mathf.Clamp01(r.ReadSingle());
            MusicVolume = Mathf.Clamp01(r.ReadSingle());
            Muted = r.ReadBoolean();
            return true;
        }
    }
}
