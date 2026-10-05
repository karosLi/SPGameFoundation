using System;
using System.IO;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace SPF.Contracts
{
    /// <summary>
    /// Resource whose state is part of a world snapshot (mid-level saves, replay checkpoints). Snapshots are
    /// taken between ticks, so jobs never run while these are called. Read must restore exactly what Write
    /// saw: the restored world has to continue bit-identically. Configuration that never changes during a
    /// session does not need it.
    /// </summary>
    public interface ISnapshotResource
    {
        void WriteSnapshot(BinaryWriter writer);

        /// <summary>Restores the state; throws <see cref="InvalidDataException"/> on data that does not fit.</summary>
        void ReadSnapshot(BinaryReader reader);
    }

    /// <summary>Raw native-array I/O for snapshots (element count + element size + bytes, no per-element work).</summary>
    public static unsafe class NativeIO
    {
        public static void Write<T>(BinaryWriter writer, NativeArray<T> array, int count) where T : unmanaged
        {
            if ((uint)count > (uint)array.Length) throw new ArgumentOutOfRangeException(nameof(count));
            writer.Write(count);
            writer.Write(sizeof(T));
            if (count > 0)
                writer.Write(new ReadOnlySpan<byte>(array.GetUnsafePtr(), count * sizeof(T)));
        }

        public static void Write<T>(BinaryWriter writer, NativeArray<T> array) where T : unmanaged => Write(writer, array, array.Length);

        /// <summary>Reads into the start of <paramref name="array"/>; returns the element count.</summary>
        public static int Read<T>(BinaryReader reader, NativeArray<T> array) where T : unmanaged
        {
            int count = reader.ReadInt32();
            int size = reader.ReadInt32();
            if (size != sizeof(T))
                throw new InvalidDataException($"Snapshot element size {size} does not match {typeof(T).Name} ({sizeof(T)}).");
            if (count < 0 || count > array.Length)
                throw new InvalidDataException($"Snapshot holds {count} {typeof(T).Name} but the array has room for {array.Length}.");
            ReadExactly(reader, new Span<byte>(array.GetUnsafePtr(), count * size));
            return count;
        }

        /// <summary>Reads a snapshot that must fill the whole array.</summary>
        public static void ReadAll<T>(BinaryReader reader, NativeArray<T> array) where T : unmanaged
        {
            if (Read(reader, array) != array.Length)
                throw new InvalidDataException($"Snapshot {typeof(T).Name} array length does not match.");
        }

        static void ReadExactly(BinaryReader reader, Span<byte> target)
        {
            while (target.Length > 0)
            {
                int read = reader.Read(target);
                if (read <= 0) throw new EndOfStreamException();
                target = target.Slice(read);
            }
        }

        /// <summary>Marker written after a section; a mismatch on read means the reader and writer disagree.</summary>
        public static void WriteMarker(BinaryWriter writer, string section)
        {
            writer.Write(0x534E4150);
            writer.Write(section);
        }

        public static void ReadMarker(BinaryReader reader, string section)
        {
            if (reader.ReadInt32() != 0x534E4150 || reader.ReadString() != section)
                throw new InvalidDataException($"Snapshot section '{section}' is misaligned (its reader and writer disagree).");
        }

        /// <summary>Raw bytes of one unmanaged value (input frames, small state structs).</summary>
        public static void WriteValue<T>(BinaryWriter writer, in T value) where T : unmanaged
        {
            T copy = value;
            writer.Write(sizeof(T));
            writer.Write(new ReadOnlySpan<byte>(&copy, sizeof(T)));
        }

        public static T ReadValue<T>(BinaryReader reader) where T : unmanaged
        {
            if (reader.ReadInt32() != sizeof(T))
                throw new InvalidDataException($"Snapshot {typeof(T).Name} has a different size.");
            T value = default;
            ReadExactly(reader, new Span<byte>(&value, sizeof(T)));
            return value;
        }

        public static void Write(BinaryWriter writer, EntityHandle handle)
        {
            writer.Write(handle.Index);
            writer.Write(handle.Generation);
        }

        public static EntityHandle ReadHandle(BinaryReader reader) => new EntityHandle(reader.ReadInt32(), reader.ReadInt32());
    }
}
