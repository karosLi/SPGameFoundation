using System;
using System.IO;

namespace SPF.Runtime.Persistence
{
    /// <summary>Game data that can be written to / read from a save file. <see cref="Version"/> lets old files migrate.</summary>
    public interface ISaveData
    {
        int Version { get; }
        void Write(BinaryWriter writer);
        /// <summary>Reads data written by <paramref name="version"/> (≤ current). Returns false to reject the file.</summary>
        bool Read(BinaryReader reader, int version);
    }

    /// <summary>
    /// Save slots on disk: header (magic, format, data version, payload length, FNV-1a checksum) + payload.
    /// Writes go to a temporary file that replaces the slot atomically, so a crash mid-save keeps the
    /// previous save. Corrupt or foreign files are rejected, not half-loaded. Main thread.
    /// </summary>
    public sealed class ProfileStore
    {
        const uint Magic = 0x53504653;   // "SPFS"
        const int Format = 1;

        public ProfileStore(string directory) => Directory = directory;

        public string Directory { get; }

        public string PathOf(string slot) => System.IO.Path.Combine(Directory, slot + ".sav");

        public bool Exists(string slot) => File.Exists(PathOf(slot));

        public void Delete(string slot)
        {
            if (Exists(slot)) File.Delete(PathOf(slot));
        }

        public void Save(string slot, ISaveData data)
        {
            byte[] payload;
            using (var buffer = new MemoryStream())
            {
                using (var writer = new BinaryWriter(buffer))
                    data.Write(writer);
                payload = buffer.ToArray();
            }
            System.IO.Directory.CreateDirectory(Directory);
            string path = PathOf(slot), temp = path + ".tmp";
            using (var file = new FileStream(temp, FileMode.Create, FileAccess.Write))
            using (var writer = new BinaryWriter(file))
            {
                writer.Write(Magic);
                writer.Write(Format);
                writer.Write(data.Version);
                writer.Write(payload.Length);
                writer.Write(Checksum(payload));
                writer.Write(payload);
            }
            if (File.Exists(path)) File.Delete(path);
            File.Move(temp, path);
        }

        /// <summary>Loads a slot into <paramref name="data"/>. False when missing, corrupt, from a newer version or rejected.</summary>
        public bool Load(string slot, ISaveData data)
        {
            string path = PathOf(slot);
            if (!File.Exists(path)) return false;
            try
            {
                using var file = new FileStream(path, FileMode.Open, FileAccess.Read);
                using var reader = new BinaryReader(file);
                if (reader.ReadUInt32() != Magic || reader.ReadInt32() != Format) return false;
                int version = reader.ReadInt32();
                int length = reader.ReadInt32();
                uint checksum = reader.ReadUInt32();
                if (version > data.Version || length < 0 || length > file.Length) return false;
                byte[] payload = reader.ReadBytes(length);
                if (payload.Length != length || Checksum(payload) != checksum) return false;
                using var payloadReader = new BinaryReader(new MemoryStream(payload));
                return data.Read(payloadReader, version);
            }
            catch (IOException) { return false; }
        }

        static uint Checksum(byte[] bytes)
        {
            uint hash = 2166136261;
            for (int i = 0; i < bytes.Length; i++) hash = (hash ^ bytes[i]) * 16777619;
            return hash;
        }
    }
}
