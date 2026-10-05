using System;
using System.Collections.Generic;
using System.Text;
using SPF.Contracts.Pooling;

namespace SPF.L2.Narrative
{
    /// <summary>
    /// String tables from CSV: a header <c>key,en,zh,...</c> then one row per key (quoted fields may hold commas,
    /// quotes as "" and newlines). An empty field means "not translated"; a quoted empty field ("") is a real empty
    /// string (e.g. the narrator's name). Lookups fall back to the first language, then to the key itself, so a missing
    /// translation shows something rather than nothing. Strings are made once at load; lookups allocate nothing.
    /// </summary>
    public sealed class LocalizationTable
    {
        readonly Dictionary<string, string[]> m_Rows = new Dictionary<string, string[]>();
        readonly List<string> m_Languages = new List<string>();
        int m_Current;

        public IReadOnlyList<string> Languages => m_Languages;
        public string Language => m_Languages.Count > 0 ? m_Languages[m_Current] : null;
        public int Count => m_Rows.Count;
        /// <summary>Bumped when the language changes (UI refreshes).</summary>
        public int Version { get; private set; }

        public static LocalizationTable FromCsv(string csv)
        {
            var table = new LocalizationTable();
            var rows = ParseCsv(csv);
            if (rows.Count == 0) return table;
            for (int c = 1; c < rows[0].Count; c++) table.m_Languages.Add((rows[0][c] ?? "").Trim());
            for (int r = 1; r < rows.Count; r++)
            {
                var row = rows[r];
                if (row.Count == 0 || string.IsNullOrWhiteSpace(row[0])) continue;   // blank line
                var values = new string[table.m_Languages.Count];
                for (int c = 0; c < values.Length; c++) values[c] = c + 1 < row.Count ? row[c + 1] : null;   // null = not translated
                table.m_Rows[row[0].Trim()] = values;
            }
            return table;
        }

        public bool SetLanguage(string language)
        {
            int i = m_Languages.IndexOf(language);
            if (i < 0) return false;
            if (i != m_Current) { m_Current = i; Version++; }
            return true;
        }

        public bool Has(string key) => m_Rows.ContainsKey(key);

        public string Get(string key)
        {
            if (key == null) return string.Empty;
            if (DialogueCompiler.IsLiteral(key)) return key.Substring(1);
            if (!m_Rows.TryGetValue(key, out var values)) return key;
            return values[m_Current] ?? values[0] ?? key;
        }

        /// <summary>Appends the text for <paramref name="key"/> with {0} replaced by <paramref name="arg"/> (no allocation).</summary>
        public TextBuilder Format(TextBuilder builder, string key, int arg)
        {
            string template = Get(key);
            int at = template.IndexOf("{0}", StringComparison.Ordinal);
            if (at < 0) return builder.Append(template);
            for (int i = 0; i < at; i++) builder.Append(template[i]);
            builder.Append(arg);
            for (int i = at + 3; i < template.Length; i++) builder.Append(template[i]);
            return builder;
        }

        /// <summary>
        /// Keys a dialogue uses (speakers, lines, choices) with no text in <paramref name="language"/> (no fallback):
        /// run it in a test so a missing translation fails the build instead of showing a key to players.
        /// </summary>
        public List<string> Missing(DialogueGraph graph, string language)
        {
            var missing = new List<string>();
            int column = m_Languages.IndexOf(language);
            foreach (var ins in graph.Code)
            {
                if (ins.Op == DialogueGraph.Op.Line) Check(graph.Strings[ins.Speaker]);
                if (ins.Op == DialogueGraph.Op.Line || ins.Op == DialogueGraph.Op.Choice) Check(graph.Strings[ins.Text]);
            }
            return missing;

            void Check(string key)
            {
                if (DialogueCompiler.IsLiteral(key) || missing.Contains(key)) return;
                if (column < 0 || !m_Rows.TryGetValue(key, out var values) || values[column] == null) missing.Add(key);
            }
        }

        static List<List<string>> ParseCsv(string csv)
        {
            var rows = new List<List<string>>();
            var row = new List<string>();
            var field = new StringBuilder();
            bool quoted = false, wasQuoted = false;
            string Take()
            {
                string value = field.Length == 0 && !wasQuoted ? null : field.ToString();
                field.Clear();
                wasQuoted = false;
                return value;
            }
            for (int i = 0; i < csv.Length; i++)
            {
                char c = csv[i];
                if (quoted)
                {
                    if (c == '"')
                    {
                        if (i + 1 < csv.Length && csv[i + 1] == '"') { field.Append('"'); i++; }
                        else quoted = false;
                    }
                    else field.Append(c);
                    continue;
                }
                switch (c)
                {
                    case '"': quoted = true; wasQuoted = true; break;
                    case ',': row.Add(Take()); break;
                    case '\r': break;
                    case '\n': row.Add(Take()); rows.Add(row); row = new List<string>(); break;
                    default: field.Append(c); break;
                }
            }
            if (field.Length > 0 || row.Count > 0) { row.Add(Take()); rows.Add(row); }
            return rows;
        }
    }
}
