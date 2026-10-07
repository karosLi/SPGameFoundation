using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using SPF.Runtime.Composition;

namespace SPF.Runtime.Session
{
    /// <summary>Opt-in bounded framing around unchanged same-runtime raw snapshots. No file I/O, automatic
    /// legacy detection, field migration, compression, authentication or cross-platform ABI promise.
    /// Prevalidation rejection leaves the live session untouched (not even Sync). After prevalidation,
    /// raw reader failures retain SimSession's existing restart-and-rethrow semantics.</summary>
    public static class SaveEnvelope
    {
        public const int DefaultMaxPayloadBytes = 16 * 1024 * 1024;
        public const int AbsoluteMaxPayloadBytes = 64 * 1024 * 1024;
        public const int HeaderBytes = 16 + SaveCompatibilityDescriptor.IdentityCount * 32 + 32;
        const int Magic = 0x45504653;
        const int FormatVersion = 1;
        const int SameRuntimeRawPayload = 1;

        public static byte[] Capture(SimSession session, SaveCompatibilityDescriptor descriptor, int maxPayloadBytes = DefaultMaxPayloadBytes)
        {
            if (session == null || descriptor == null) throw new ArgumentNullException();
            ValidateLimit(maxPayloadBytes);
            using var payload = new BoundedSaveBuffer(maxPayloadBytes);
            using (var writer = new BinaryWriter(payload, Encoding.UTF8, true)) session.WriteSnapshot(writer);
            return Wrap(payload.ToArray(), descriptor, maxPayloadBytes);
        }

        /// <summary>Writes one complete envelope. A failed stream write is not an atomic file replacement;
        /// callers must keep their original file until their own durable replacement protocol succeeds.</summary>
        public static void Write(Stream destination, SimSession session, SaveCompatibilityDescriptor descriptor, int maxPayloadBytes = DefaultMaxPayloadBytes)
        {
            if (destination == null || !destination.CanWrite) throw new ArgumentException("A writable stream is required.");
            byte[] bytes = Capture(session, descriptor, maxPayloadBytes);
            destination.Write(bytes, 0, bytes.Length);
        }

        public static void Restore(Stream source, SimSession session, SaveCompatibilityDescriptor expected, int maxPayloadBytes = DefaultMaxPayloadBytes)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));
            byte[] payload = ReadPayload(source, expected, maxPayloadBytes);
            session.ReadEnvelopePayload(payload);

        }

        /// <summary>Fully validates one EOF-terminated frame, including on non-seekable streams. Uses one payload allocation
        /// capped at the configured bound plus fixed header/hash buffers; never reads an untrusted string or collection count.</summary>
        public static byte[] ReadPayload(Stream source, SaveCompatibilityDescriptor expected, int maxPayloadBytes = DefaultMaxPayloadBytes)
        {
            ValidateLimit(maxPayloadBytes);
            if (source == null || !source.CanRead || expected == null) throw new ArgumentException("A readable stream and descriptor are required.");
            byte[] header = new byte[HeaderBytes]; ReadExactly(source, header);
            using var reader = new BinaryReader(new MemoryStream(header, false));
            if (reader.ReadInt32() != Magic || reader.ReadInt32() != FormatVersion || reader.ReadInt32() != SameRuntimeRawPayload)
                throw new InvalidDataException("Unknown save envelope or payload version.");
            int length = reader.ReadInt32();
            if (length < 1 || length > maxPayloadBytes) throw new InvalidDataException("Save payload exceeds its configured bound.");
            expected.Validate(reader);
            byte[] checksum = reader.ReadBytes(32);
            if (source.CanSeek && source.Length - source.Position != length) throw new InvalidDataException("Save frame length differs.");
            byte[] payload = new byte[length]; ReadExactly(source, payload);
            if (source.ReadByte() != -1) throw new InvalidDataException("Save envelope has trailing bytes.");
            if (!SaveCompatibilityDescriptor.Equal(checksum, Integrity(header, payload))) throw new InvalidDataException("Save envelope integrity check failed.");
            return payload;
        }

        /// <summary>Only wraps a known explicit legacy layout that is identical to the target raw schema.
        /// Creates and owns a fresh temporary session from the supplied mode. Validation and canonical
        /// recapture happen there. Original streams/files and live gameplay are not changed.
        /// This is import of a known same-runtime checkpoint, not a historical field converter.</summary>
        public static byte[] ImportKnownLegacy(Stream source, KnownLegacySaveDescriptor legacy, ModeDefinition mode, uint seed,
            Func<SimSession, SaveCompatibilityDescriptor> describeTemporary, int maxPayloadBytes = DefaultMaxPayloadBytes)
        {
            ValidateLimit(maxPayloadBytes);
            if (legacy == null || mode == null || describeTemporary == null || source == null || !source.CanRead) throw new ArgumentNullException();
            using var buffer = new BoundedSaveBuffer(maxPayloadBytes);
            var chunk = new byte[4096]; int n;
            while ((n = source.Read(chunk, 0, chunk.Length)) > 0) buffer.Write(chunk, 0, n);
            if (buffer.Length == 0) throw new InvalidDataException("Empty legacy checkpoint.");
            using var temporary = SimSession.Create(mode, seed);
            var target = describeTemporary(temporary) ?? throw new InvalidOperationException("Missing temporary-session descriptor.");
            legacy.Compatibility.RequireCompatible(target);
            buffer.Position = 0;
            using var reader = new BinaryReader(buffer, Encoding.UTF8, true);
            temporary.ReadSnapshot(reader);
            if (buffer.Position != buffer.Length) throw new InvalidDataException("Legacy checkpoint has trailing bytes.");
            return Capture(temporary, target, maxPayloadBytes);
        }

        static byte[] Wrap(byte[] payload, SaveCompatibilityDescriptor descriptor, int max)
        {
            if (payload.Length < 1 || payload.Length > max) throw new InvalidDataException("Save payload exceeds its bound.");
            var bytes = new byte[HeaderBytes + payload.Length];
            using var stream = new MemoryStream(bytes, true); using var writer = new BinaryWriter(stream, Encoding.UTF8, true);
            writer.Write(Magic); writer.Write(FormatVersion); writer.Write(SameRuntimeRawPayload); writer.Write(payload.Length); descriptor.Write(writer);
            byte[] header = new byte[HeaderBytes]; Buffer.BlockCopy(bytes, 0, header, 0, HeaderBytes);
            writer.Write(Integrity(header, payload)); writer.Write(payload); return bytes;
        }
        static byte[] Integrity(byte[] header, byte[] payload)
        {
            using var hash = SHA256.Create();
            hash.TransformBlock(header, 0, HeaderBytes - 32, header, 0);
            hash.TransformBlock(payload, 0, payload.Length, payload, 0);
            hash.TransformFinalBlock(Array.Empty<byte>(), 0, 0); return hash.Hash;
        }
        static void ReadExactly(Stream source, byte[] target)
        {
            int offset = 0;
            while (offset < target.Length)
            { int n = source.Read(target, offset, target.Length - offset); if (n <= 0) throw new EndOfStreamException(); offset += n; }
        }
        static void ValidateLimit(int max)
        { if (max < 1 || max > AbsoluteMaxPayloadBytes) throw new ArgumentOutOfRangeException(nameof(max)); }
    }

    public sealed partial class SimSession
    {
        // New envelope-only boundary. The composable public raw reader is deliberately unchanged.
        // Raw failures already restart inside ReadSnapshot. An internal payload suffix is discovered
        // after its successful restore: restart once more, preserving the actual private pause reason.
        // Thus this suffix case advances TimelineRevision twice (restore + restart), not once.
        internal void ReadEnvelopePayload(byte[] payload)
        {
            var priorState = m_State;
            using var reader = new BinaryReader(new MemoryStream(payload, false));
            ReadSnapshot(reader);
            if (reader.BaseStream.Position == reader.BaseStream.Length) return;
            Restart();
            m_State = priorState;
            throw new InvalidDataException("Raw snapshot has trailing bytes inside its envelope payload.");
        }
    }

    public sealed class KnownLegacySaveDescriptor
    {
        public string LegacyLayoutId { get; }
        public SaveCompatibilityDescriptor Compatibility { get; }
        public KnownLegacySaveDescriptor(string legacyLayoutId, SaveCompatibilityDescriptor compatibility)
        {
            if (string.IsNullOrWhiteSpace(legacyLayoutId) || legacyLayoutId.Length > 128) throw new ArgumentException("Explicit known legacy layout ID required.");
            LegacyLayoutId = legacyLayoutId; Compatibility = compatibility ?? throw new ArgumentNullException(nameof(compatibility));
        }
    }

    internal sealed class BoundedSaveBuffer : MemoryStream
    {
        readonly int m_Limit;
        public BoundedSaveBuffer(int limit) { m_Limit = limit; }
        void Reserve(long end)
        {
            if (end < 0 || end > m_Limit) throw new InvalidDataException("Save buffer exceeds its configured bound.");
            if (end > Capacity) Capacity = (int)Math.Min(m_Limit, Math.Max(end, Math.Max(256L, (long)Capacity * 2)));
        }
        public override void Write(byte[] buffer, int offset, int count) { Reserve(Position + count); base.Write(buffer, offset, count); }
        public override void Write(ReadOnlySpan<byte> buffer) { Reserve(Position + buffer.Length); base.Write(buffer); }
        public override void WriteByte(byte value) { Reserve(Position + 1); base.WriteByte(value); }
        public override void SetLength(long value) { Reserve(value); base.SetLength(value); }
    }
}
