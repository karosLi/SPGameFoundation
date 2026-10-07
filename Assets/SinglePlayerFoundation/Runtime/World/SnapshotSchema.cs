using System;
using System.Collections.Generic;
using System.IO;
using SPF.Contracts;

namespace SPF.Runtime.World
{
    /// <summary>Explicit semantics, not an inference from struct size or a reflected field list.
    /// Bump Version whenever the meaning/layout of this entry changes, even at the same size.</summary>
    public sealed class SnapshotMemberSchema
    {
        public AccessKey Key { get; }
        public string Meaning { get; }
        public int Version { get; }
        public SnapshotMemberSchema(AccessKey key, string meaning, int version = 1)
        {
            Key = key ?? throw new ArgumentNullException(nameof(key));
            if (string.IsNullOrWhiteSpace(meaning) || meaning.Length > 128 || string.IsNullOrWhiteSpace(key.Name) || key.Name.Length > 128)
                throw new ArgumentException("A bounded stable key and semantic ID are required.");
            if (version < 1) throw new ArgumentOutOfRangeException(nameof(version));
            Meaning = meaning; Version = version;
        }
        internal void Write(BinaryWriter w) { w.Write(Key.Name); w.Write(Meaning); w.Write(Version); }
    }

    /// <summary>Policies describe existing raw hooks. Snapshot includes authority; StaticDefinition is
    /// covered by content identity; Discard is output-only; SnapshotWithDerivedCaches rebuilds caches.</summary>
    public enum SnapshotResourcePolicy { Snapshot, StaticDefinition, Discard, SnapshotWithDerivedCaches }

    public sealed class SnapshotResourceSchema
    {
        public SnapshotMemberSchema Member { get; }
        public SnapshotResourcePolicy Policy { get; }
        public bool LevelScoped { get; }
        public SnapshotResourceSchema(AccessKey key, string meaning, SnapshotResourcePolicy policy,
            bool levelScoped = false, int version = 1)
        {
            Member = new SnapshotMemberSchema(key, meaning, version);
            if (policy < SnapshotResourcePolicy.Snapshot || policy > SnapshotResourcePolicy.SnapshotWithDerivedCaches)
                throw new ArgumentOutOfRangeException(nameof(policy));
            Policy = policy; LevelScoped = levelScoped;
        }
    }

    public sealed class SnapshotTableSchema
    {
        readonly SnapshotMemberSchema[] m_Columns;
        public SnapshotMemberSchema Member { get; }
        public bool LevelScoped { get; }
        public bool Pooled { get; }
        internal SnapshotMemberSchema[] Columns => m_Columns;
        public SnapshotTableSchema(TableKey key, string meaning, bool levelScoped, bool pooled,
            params SnapshotMemberSchema[] columns)
        {
            Member = new SnapshotMemberSchema(key, meaning);
            if (columns == null || columns.Length > 128) throw new ArgumentException("Invalid column schema.");
            m_Columns = (SnapshotMemberSchema[])columns.Clone();
            var seen = new HashSet<int>();
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var column in m_Columns)
                if (column == null || !seen.Add(column.Key.Id) || !names.Add(column.Key.Name)) throw new ArgumentException("Missing/duplicate column schema.");
            LevelScoped = levelScoped; Pooled = pooled;
        }
    }

    public sealed partial class SimWorld
    {
        /// <summary>Cold opt-in check of every ordered table/column/resource, including unsaved resources.
        /// Adds no bytes to raw snapshots and never serializes process-local AccessKey IDs.</summary>
        public void WriteSnapshotSchema(BinaryWriter writer, SnapshotTableSchema[] tables, SnapshotResourceSchema[] resources)
        {
            if (writer == null || tables == null || resources == null) throw new ArgumentNullException();
            if (tables.Length != m_Tables.Count || resources.Length != m_ResourceList.Count)
                throw new InvalidDataException("Snapshot schema does not cover the whole world.");
            writer.Write("spf.raw-world"); writer.Write(1); writer.Write("registry.generational-freelist"); writer.Write(1);
            writer.Write(tables.Length);
            var tableNames = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < tables.Length; i++)
            {
                var schema = tables[i] ?? throw new ArgumentException("Null table schema.");
                var table = m_Tables[i];
                if (!tableNames.Add(schema.Member.Key.Name) || !ReferenceEquals(table.Key, schema.Member.Key) || table.IsPooled != schema.Pooled ||
                    m_LevelTables.Contains(table) != schema.LevelScoped)
                    throw new InvalidDataException("Snapshot table identity/order/scope differs.");
                schema.Member.Write(writer); writer.Write(schema.LevelScoped); writer.Write(schema.Pooled);
                table.WriteSnapshotColumnSchema(writer, schema.Columns);
            }
            writer.Write(resources.Length);
            var resourceNames = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < resources.Length; i++)
            {
                var schema = resources[i] ?? throw new ArgumentException("Null resource schema.");
                var resource = m_ResourceList[i];
                bool level = resource is IResettableResource resettable && m_LevelResources.Contains(resettable);
                if (!resourceNames.Add(schema.Member.Key.Name) || !ReferenceEquals(schema.Member.Key, m_ResourceKeys[i]) || level != schema.LevelScoped ||
                    ((schema.Policy == SnapshotResourcePolicy.Snapshot || schema.Policy == SnapshotResourcePolicy.SnapshotWithDerivedCaches) && !(resource is ISnapshotResource)))
                    throw new InvalidDataException("Snapshot resource identity/order/scope/hook differs.");
                schema.Member.Write(writer); writer.Write((int)schema.Policy); writer.Write(level);
                writer.Write(resource is ISnapshotResource);
            }
        }

        /// <summary>Actual capacities, including the implicit queue. Separate from schema meaning.</summary>
        public void WriteSnapshotCapacities(BinaryWriter writer)
        {
            writer.Write(Registry.Capacity); writer.Write(m_DestroyQueue.Queue.Capacity);
            writer.Write(m_Tables.Count);
            foreach (var table in m_Tables) { writer.Write(table.Capacity); writer.Write(table.Changes != null); }
        }
    }

    public sealed unsafe partial class SimTable
    {
        internal void WriteSnapshotColumnSchema(BinaryWriter writer, SnapshotMemberSchema[] columns)
        {
            int offset = IsPooled ? 1 : 0;
            if (columns.Length + offset != m_ColumnList.Count) throw new InvalidDataException("Uncovered snapshot column.");
            writer.Write("handles.index-generation"); writer.Write(1); writer.Write(columns.Length);
            if (IsPooled) { writer.Write("pooled.dead-byte"); writer.Write(1); }
            for (int i = 0; i < columns.Length; i++)
            {
                var schema = columns[i];
                if (!m_Columns.TryGetValue(schema.Key.Id, out var actual) || !ReferenceEquals(actual, m_ColumnList[i + offset]))
                    throw new InvalidDataException("Snapshot column identity/order differs, including equal-size columns.");
                schema.Write(writer);
                // Size supplements explicit meaning/version. It is never the semantic identity.
                writer.Write(actual.ElementSize);
            }
        }
    }
}
