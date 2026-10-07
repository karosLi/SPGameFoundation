using System;
using System.IO;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using SPF.Runtime.Scheduling;

namespace SPF.Runtime.Session
{
    /// <summary>Immutable cold-path identities for a same-runtime raw payload. IDs/versions are authored
    /// contracts, not promises inferred from reflection. Fingerprints detect compatibility, not origin.
    /// The runtime ID must name the application's build/backend/architecture/ABI compatibility domain.</summary>
    public sealed class SaveCompatibilityDescriptor
    {
        internal const int IdentityCount = 7;
        readonly byte[][] m_Identities;
        public string ModeId { get; }
        public string RuntimeId { get; }
        public string ContractFingerprint => Hex(m_Identities[1]);
        public string SchemaFingerprint => Hex(m_Identities[2]);
        public string ContentFingerprint => Hex(m_Identities[3]);
        public string VisualFingerprint => Hex(m_Identities[4]);
        public string RawCompatibilityFingerprint => Hex(m_Identities[5]);

        public SaveCompatibilityDescriptor(string modeId, string contractId, string runtimeId,
            byte[] schema, byte[] content, byte[] visual, byte[] rawCompatibility)
        {
            ValidateId(modeId); ValidateId(contractId); ValidateId(runtimeId);
            ModeId = modeId; RuntimeId = runtimeId;
            m_Identities = new[] { Hash(Encoding.UTF8.GetBytes(modeId)), Hash(Encoding.UTF8.GetBytes(contractId)),
                HashInput(schema), HashInput(content), HashInput(visual), HashInput(rawCompatibility), Hash(Encoding.UTF8.GetBytes(runtimeId)) };
        }
        internal void Write(BinaryWriter writer) { foreach (var id in m_Identities) writer.Write(id); }
        internal void Validate(BinaryReader reader)
        {
            for (int i = 0; i < IdentityCount; i++)
            {
                byte[] incoming = reader.ReadBytes(32);
                if (incoming.Length != 32) throw new EndOfStreamException();
                // A visual-only change is permitted unless the old raw reader also requires it (identity 5).
                if (i != 4 && !Equal(incoming, m_Identities[i])) throw new InvalidDataException("Save compatibility identity differs at index " + i + ".");
            }
        }
        internal void RequireCompatible(SaveCompatibilityDescriptor other)
        {
            if (other == null) throw new ArgumentNullException(nameof(other));
            for (int i = 0; i < IdentityCount; i++)
                if (i != 4 && !Equal(m_Identities[i], other.m_Identities[i])) throw new InvalidDataException("Legacy descriptor is not compatible with the target.");
        }
        public static byte[] Encode(Action<BinaryWriter> write)
        {
            if (write == null) throw new ArgumentNullException(nameof(write));
            using var buffer = new BoundedSaveBuffer(64 * 1024);
            using var writer = new BinaryWriter(buffer, Encoding.UTF8, true);
            write(writer); writer.Flush(); return buffer.ToArray();
        }
        public static void WriteSystemSchema(BinaryWriter writer, TickPipeline pipeline, params SnapshotSystemSchema[] schemas)
        {
            if (schemas == null || schemas.Length != pipeline.SystemCount) throw new InvalidDataException("Uncovered system schema.");
            writer.Write(schemas.Length);
            var ids = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < schemas.Length; i++)
            {
                var expected = schemas[i] ?? throw new ArgumentException("Null system schema.");
                var actual = pipeline.GetSystem(i);
                if (!ids.Add(expected.StableId) || actual.GetType() != expected.Type) throw new InvalidDataException("System identity/order differs.");
                writer.Write(expected.StableId); writer.Write(expected.Version); writer.Write(expected.Type.FullName);
                writer.Write((int)actual.Phase); writer.Write(actual.Order); writer.Write(actual is ISnapshotSystem);
            }
        }
        static void ValidateId(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length > 128) throw new ArgumentException("A bounded explicit compatibility ID is required.");
        }
        static byte[] HashInput(byte[] value)
        {
            if (value == null || value.Length > 64 * 1024) throw new ArgumentException("Identity input exceeds its bound.");
            return Hash(value);
        }
        internal static byte[] Hash(byte[] bytes) { using var hash = SHA256.Create(); return hash.ComputeHash(bytes); }
        internal static bool Equal(byte[] a, byte[] b)
        { if (a.Length != b.Length) return false; for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false; return true; }
        static string Hex(byte[] bytes) => BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
    }

    public sealed class SnapshotSystemSchema
    {
        public Type Type { get; }
        public string StableId { get; }
        public int Version { get; }
        public SnapshotSystemSchema(Type type, string stableId, int version = 1)
        {
            if (type == null || !typeof(ISimSystem).IsAssignableFrom(type) || string.IsNullOrWhiteSpace(stableId) || stableId.Length > 128 || version < 1)
                throw new ArgumentException("Invalid explicit system schema.");
            Type = type; StableId = stableId; Version = version;
        }
    }
}
