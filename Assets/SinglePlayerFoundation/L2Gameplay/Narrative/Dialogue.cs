using System;
using System.Collections.Generic;
using System.IO;

namespace SPF.L2.Narrative
{
    /// <summary>
    /// A compiled dialogue script. The source is a small line-based language (one statement per line):
    /// <code>
    /// === start                       a node (jump target)
    /// alice: intro.hello              a line: speaker id and a text key (or "literal text" in quotes)
    /// * choice.help -> help           a choice (text key, target node); optional condition: * key [if trust >= 2] -> node
    /// ~ trust += 1                    set a variable (=, +=, -=); variables are ints, all start at 0
    /// ? trust >= 2 -> friendly        conditional jump (==, !=, &gt;, &gt;=, &lt;, &lt;=)
    /// # gift                          an event for the game (item given, music change...)
    /// -> end                          jump; "-> END" ends the dialogue
    /// // comment
    /// </code>
    /// Choices directly follow the line that asks them. Compiling resolves names to indices, so running a
    /// dialogue allocates nothing and its whole state is a program counter plus an int array (snapshots).
    /// </summary>
    public sealed class DialogueGraph
    {
        public enum Op : byte { Line, Choice, Set, Add, Goto, Branch, Event, End }
        public enum Compare : byte { Eq, Ne, Gt, Ge, Lt, Le }

        public struct Instruction
        {
            public Op Op;
            public int Speaker;      // Line
            public int Text;         // Line, Choice: string table index
            public int Target;       // Choice, Goto, Branch: instruction index (-1 = end)
            public int Var;          // Set, Add, Branch, Choice condition (-1 = none)
            public int Value;
            public Compare Cmp;
            public int Event;        // Event: string table index
        }

        public Instruction[] Code;
        public string[] Strings;     // speakers, text keys / literals, event names
        public string[] Variables;
        public Dictionary<string, int> Nodes;

        public int Variable(string name) => Array.IndexOf(Variables, name);
        public int Node(string name) => Nodes.TryGetValue(name, out int pc) ? pc : throw new KeyNotFoundException("No dialogue node '" + name + "'.");

        public static bool Test(int a, Compare cmp, int b) => cmp switch
        {
            Compare.Eq => a == b,
            Compare.Ne => a != b,
            Compare.Gt => a > b,
            Compare.Ge => a >= b,
            Compare.Lt => a < b,
            _ => a <= b,
        };
    }

    public sealed class DialogueCompileException : Exception
    {
        public DialogueCompileException(int line, string message) : base($"line {line}: {message}") { }
    }

    public static class DialogueCompiler
    {
        public static DialogueGraph Compile(string source)
        {
            var code = new List<DialogueGraph.Instruction>();
            var strings = new List<string>();
            var stringIndex = new Dictionary<string, int>();
            var variables = new List<string>();
            var nodes = new Dictionary<string, int>();
            var fixups = new List<(int instruction, string node, int line)>();

            int Str(string s)
            {
                if (!stringIndex.TryGetValue(s, out int i)) { i = strings.Count; strings.Add(s); stringIndex[s] = i; }
                return i;
            }
            int Var(string name)
            {
                int i = variables.IndexOf(name);
                if (i < 0) { i = variables.Count; variables.Add(name); }
                return i;
            }
            void Jump(int at, string node, int line)
            {
                if (node == "END") { var ins = code[at]; ins.Target = -1; code[at] = ins; }
                else fixups.Add((at, node, line));
            }
            (int var, DialogueGraph.Compare cmp, int value) Condition(string text, int line)
            {
                string[] ops = { ">=", "<=", "==", "!=", ">", "<" };
                foreach (var op in ops)
                {
                    int at = text.IndexOf(op, StringComparison.Ordinal);
                    if (at < 0) continue;
                    string name = text.Substring(0, at).Trim();
                    if (!int.TryParse(text.Substring(at + op.Length).Trim(), out int value)) throw new DialogueCompileException(line, "condition needs an integer: " + text);
                    var cmp = op switch
                    {
                        ">=" => DialogueGraph.Compare.Ge, "<=" => DialogueGraph.Compare.Le, "==" => DialogueGraph.Compare.Eq,
                        "!=" => DialogueGraph.Compare.Ne, ">" => DialogueGraph.Compare.Gt, _ => DialogueGraph.Compare.Lt,
                    };
                    return (Var(name), cmp, value);
                }
                throw new DialogueCompileException(line, "bad condition: " + text);
            }

            var lines = source.Replace("\r", "").Split('\n');
            for (int n = 0; n < lines.Length; n++)
            {
                int lineNo = n + 1;
                string raw = lines[n];
                int comment = raw.IndexOf("//", StringComparison.Ordinal);
                if (comment >= 0 && raw.IndexOf('"') < 0) raw = raw.Substring(0, comment);
                string line = raw.Trim();
                if (line.Length == 0) continue;

                if (line.StartsWith("===", StringComparison.Ordinal))
                {
                    string name = line.Substring(3).Trim();
                    if (nodes.ContainsKey(name)) throw new DialogueCompileException(lineNo, "duplicate node " + name);
                    nodes[name] = code.Count;
                }
                else if (line.StartsWith("->", StringComparison.Ordinal))
                {
                    code.Add(new DialogueGraph.Instruction { Op = DialogueGraph.Op.Goto, Var = -1 });
                    Jump(code.Count - 1, line.Substring(2).Trim(), lineNo);
                }
                else if (line[0] == '*')
                {
                    int arrow = line.LastIndexOf("->", StringComparison.Ordinal);
                    if (arrow < 0) throw new DialogueCompileException(lineNo, "choice needs '-> node'");
                    string head = line.Substring(1, arrow - 1).Trim();
                    var ins = new DialogueGraph.Instruction { Op = DialogueGraph.Op.Choice, Var = -1 };
                    int cond = head.IndexOf("[if", StringComparison.Ordinal);
                    if (cond >= 0)
                    {
                        int close = head.IndexOf(']', cond);
                        if (close < 0) throw new DialogueCompileException(lineNo, "unclosed condition");
                        var (v, cmp, value) = Condition(head.Substring(cond + 3, close - cond - 3), lineNo);
                        ins.Var = v; ins.Cmp = cmp; ins.Value = value;
                        head = head.Substring(0, cond).Trim();
                    }
                    ins.Text = Str(Unquote(head));
                    code.Add(ins);
                    Jump(code.Count - 1, line.Substring(arrow + 2).Trim(), lineNo);
                }
                else if (line[0] == '~')
                {
                    string body = line.Substring(1).Trim();
                    var op = body.Contains("+=") ? "+=" : body.Contains("-=") ? "-=" : "=";
                    int at = body.IndexOf(op, StringComparison.Ordinal);
                    if (at < 0 || !int.TryParse(body.Substring(at + op.Length).Trim(), out int value)) throw new DialogueCompileException(lineNo, "bad assignment: " + body);
                    code.Add(new DialogueGraph.Instruction
                    {
                        Op = op == "=" ? DialogueGraph.Op.Set : DialogueGraph.Op.Add,
                        Var = Var(body.Substring(0, at).Trim()),
                        Value = op == "-=" ? -value : value,
                    });
                }
                else if (line[0] == '?')
                {
                    int arrow = line.LastIndexOf("->", StringComparison.Ordinal);
                    if (arrow < 0) throw new DialogueCompileException(lineNo, "branch needs '-> node'");
                    var (v, cmp, value) = Condition(line.Substring(1, arrow - 1), lineNo);
                    code.Add(new DialogueGraph.Instruction { Op = DialogueGraph.Op.Branch, Var = v, Cmp = cmp, Value = value });
                    Jump(code.Count - 1, line.Substring(arrow + 2).Trim(), lineNo);
                }
                else if (line[0] == '#')
                {
                    code.Add(new DialogueGraph.Instruction { Op = DialogueGraph.Op.Event, Event = Str(line.Substring(1).Trim()), Var = -1 });
                }
                else
                {
                    int colon = line.IndexOf(':');
                    if (colon <= 0) throw new DialogueCompileException(lineNo, "expected 'speaker: text'");
                    code.Add(new DialogueGraph.Instruction
                    {
                        Op = DialogueGraph.Op.Line, Var = -1,
                        Speaker = Str(line.Substring(0, colon).Trim()),
                        Text = Str(Unquote(line.Substring(colon + 1).Trim())),
                    });
                }
            }
            code.Add(new DialogueGraph.Instruction { Op = DialogueGraph.Op.End, Var = -1 });
            foreach (var (at, node, line) in fixups)
            {
                if (!nodes.TryGetValue(node, out int pc)) throw new DialogueCompileException(line, "unknown node " + node);
                var ins = code[at];
                ins.Target = pc;
                code[at] = ins;
            }
            return new DialogueGraph { Code = code.ToArray(), Strings = strings.ToArray(), Variables = variables.ToArray(), Nodes = nodes };
        }

        /// <summary>"text" → text (marks a literal); a bare word stays a localisation key.</summary>
        static string Unquote(string s) => s.Length >= 2 && s[0] == '"' && s[s.Length - 1] == '"' ? "\"" + s.Substring(1, s.Length - 2) : s;

        /// <summary>True when a string table entry is literal text rather than a localisation key.</summary>
        public static bool IsLiteral(string s) => s.Length > 0 && s[0] == '"';
    }

    /// <summary>
    /// Runs a <see cref="DialogueGraph"/>: <see cref="Start"/> a node, show <see cref="Speaker"/> / <see cref="Text"/>,
    /// <see cref="Advance"/> past lines, <see cref="Choose"/> among <see cref="Choices"/>. Events raised by '#' lines
    /// collect in <see cref="Events"/> for the game to drain. No allocation while running; state snapshots exactly.
    /// </summary>
    public sealed class DialogueRunner
    {
        public enum Mode : byte { Idle, Line, Choice, Ended }

        public readonly DialogueGraph Graph;
        public readonly int[] Vars;
        readonly int[] m_Choices = new int[8];
        readonly int[] m_Events = new int[16];
        int m_Pc = -1;

        public DialogueRunner(DialogueGraph graph)
        {
            Graph = graph;
            Vars = new int[graph.Variables.Length];
        }

        public Mode State { get; private set; }
        public int ChoiceCount { get; private set; }
        public int EventCount { get; private set; }
        /// <summary>Lines shown so far (UI uses it to restart the typewriter).</summary>
        public int Serial { get; private set; }

        public string Speaker => State == Mode.Line || State == Mode.Choice ? Graph.Strings[Graph.Code[m_Pc].Speaker] : null;
        public string Text => State == Mode.Line || State == Mode.Choice ? Graph.Strings[Graph.Code[m_Pc].Text] : null;
        public string ChoiceText(int i) => Graph.Strings[Graph.Code[m_Choices[i]].Text];
        public string EventName(int i) => Graph.Strings[m_Events[i]];
        public void ClearEvents() => EventCount = 0;

        public int this[string variable]
        {
            get => Vars[Graph.Variable(variable)];
            set => Vars[Graph.Variable(variable)] = value;
        }

        public void Start(string node)
        {
            m_Pc = Graph.Node(node);
            Run();
        }

        /// <summary>Continues after a line (ignored while a choice is pending).</summary>
        public void Advance()
        {
            if (State != Mode.Line) return;
            m_Pc++;
            Run();
        }

        public void Choose(int index)
        {
            if (State != Mode.Choice || (uint)index >= (uint)ChoiceCount) return;
            int target = Graph.Code[m_Choices[index]].Target;
            if (target < 0) { State = Mode.Ended; return; }
            m_Pc = target;
            Run();
        }

        void Run()
        {
            var code = Graph.Code;
            for (int guard = 0; guard < 100000; guard++)
            {
                var ins = code[m_Pc];
                switch (ins.Op)
                {
                    case DialogueGraph.Op.Line:
                        Serial++;
                        // A line followed by choices asks them.
                        ChoiceCount = 0;
                        for (int c = m_Pc + 1; c < code.Length && code[c].Op == DialogueGraph.Op.Choice; c++)
                            if (ChoiceCount < m_Choices.Length && (code[c].Var < 0 || DialogueGraph.Test(Vars[code[c].Var], code[c].Cmp, code[c].Value)))
                                m_Choices[ChoiceCount++] = c;
                        bool asks = m_Pc + 1 < code.Length && code[m_Pc + 1].Op == DialogueGraph.Op.Choice;
                        State = asks && ChoiceCount > 0 ? Mode.Choice : Mode.Line;
                        if (asks && ChoiceCount == 0)
                        {
                            // Every choice is locked: behave like a plain line that then falls past them.
                            int c = m_Pc + 1;
                            while (c < code.Length && code[c].Op == DialogueGraph.Op.Choice) c++;
                            m_Pc = c - 1;
                        }
                        return;
                    case DialogueGraph.Op.Choice:
                        m_Pc++;   // reached by falling through: skip
                        break;
                    case DialogueGraph.Op.Set: Vars[ins.Var] = ins.Value; m_Pc++; break;
                    case DialogueGraph.Op.Add: Vars[ins.Var] += ins.Value; m_Pc++; break;
                    case DialogueGraph.Op.Event:
                        if (EventCount < m_Events.Length) m_Events[EventCount++] = ins.Event;
                        m_Pc++;
                        break;
                    case DialogueGraph.Op.Goto:
                        if (ins.Target < 0) { State = Mode.Ended; return; }
                        m_Pc = ins.Target;
                        break;
                    case DialogueGraph.Op.Branch:
                        if (DialogueGraph.Test(Vars[ins.Var], ins.Cmp, ins.Value))
                        {
                            if (ins.Target < 0) { State = Mode.Ended; return; }
                            m_Pc = ins.Target;
                        }
                        else m_Pc++;
                        break;
                    default:
                        State = Mode.Ended;
                        return;
                }
            }
            throw new InvalidOperationException("Dialogue loops without showing a line.");
        }

        public void Write(BinaryWriter w)
        {
            w.Write(m_Pc); w.Write((byte)State); w.Write(Serial);
            w.Write(Vars.Length);
            foreach (int v in Vars) w.Write(v);
        }

        public void Read(BinaryReader r)
        {
            m_Pc = r.ReadInt32();
            var state = (Mode)r.ReadByte();
            Serial = r.ReadInt32();
            int n = r.ReadInt32();
            if (n != Vars.Length) throw new InvalidDataException("Dialogue variables changed since this save.");
            for (int i = 0; i < n; i++) Vars[i] = r.ReadInt32();
            EventCount = 0;
            if (state == Mode.Line || state == Mode.Choice)
            {
                // Re-enter the line to rebuild the visible choices (conditions read the restored variables).
                int serial = Serial - 1;
                Run();
                Serial = serial + 1;
            }
            else State = state;
        }
    }
}
