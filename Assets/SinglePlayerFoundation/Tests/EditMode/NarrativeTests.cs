using System.IO;
using NUnit.Framework;
using SPF.Contracts.Pooling;
using SPF.L2.Narrative;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;

namespace SPF.Tests.EditMode
{
    public class NarrativeTests
    {
        const string Script = @"
=== start
~ visits += 1
guard: guard.halt                 // a key (choices follow the line that asks them)
* choice.bribe [if gold >= 5] -> bribe
* choice.talk -> talk
* ""Leave"" -> END

=== talk
guard: ""Fine words.""
~ trust += 2
? trust >= 2 -> friendly
guard: guard.cold
-> END

=== friendly
# open_gate
guard: guard.warm
-> start

=== bribe
~ gold -= 5
guard: guard.bribed
-> END
";

        const string Csv = "key,en,zh\n" +
                           "guard.halt,Halt! Who goes there?,站住！什么人？\n" +
                           "choice.talk,Talk,交谈\n" +
                           "choice.bribe,\"Bribe (5 gold, maybe)\",贿赂\n" +
                           "guard.warm,\"He says \"\"go on\"\".\",\n" +
                           "coins,You have {0} coins,你有{0}枚金币\n";

        [Test]
        public void ChoicesConditionsVariablesAndEvents()
        {
            var graph = DialogueCompiler.Compile(Script);
            var run = new DialogueRunner(graph);
            run.Start("start");
            Assert.AreEqual(DialogueRunner.Mode.Choice, run.State);
            Assert.AreEqual("guard", run.Speaker);
            Assert.AreEqual("guard.halt", run.Text);
            Assert.AreEqual(2, run.ChoiceCount, "the bribe needs 5 gold");
            Assert.AreEqual("choice.talk", run.ChoiceText(0));
            Assert.AreEqual(1, run["visits"]);

            run.Choose(0);   // talk
            Assert.AreEqual(DialogueRunner.Mode.Line, run.State);
            Assert.AreEqual("\"Fine words.", run.Text, "literal text keeps its marker");
            run.Advance();
            Assert.AreEqual("guard.warm", run.Text, "trust 2 branched to friendly");
            Assert.AreEqual(1, run.EventCount);
            Assert.AreEqual("open_gate", run.EventName(0));
            run.ClearEvents();

            run["gold"] = 7;
            run.Advance();   // back to start
            Assert.AreEqual(3, run.ChoiceCount, "now the bribe shows");
            Assert.AreEqual(2, run["visits"]);
            run.Choose(0);
            Assert.AreEqual("guard.bribed", run.Text);
            Assert.AreEqual(2, run["gold"]);
            run.Advance();
            Assert.AreEqual(DialogueRunner.Mode.Ended, run.State);
        }

        [Test]
        public void SnapshotsRestoreTheLineAndItsChoices()
        {
            var graph = DialogueCompiler.Compile(Script);
            var a = new DialogueRunner(graph);
            a.Start("start");
            a["gold"] = 9;
            a.Choose(1);   // talk (index 1 of [talk, leave]... gold was set after the choices were built)
            using var ms = new MemoryStream();
            using (var w = new BinaryWriter(ms, System.Text.Encoding.UTF8, true)) a.Write(w);
            ms.Position = 0;
            var b = new DialogueRunner(graph);
            using (var r = new BinaryReader(ms)) b.Read(r);
            Assert.AreEqual(a.State, b.State);
            Assert.AreEqual(a.Text, b.Text);
            Assert.AreEqual(a.Serial, b.Serial);
            Assert.AreEqual(9, b["gold"]);
        }

        [Test]
        public void CompileErrorsNameTheLine()
        {
            var e = Assert.Throws<DialogueCompileException>(() => DialogueCompiler.Compile("=== a\n-> nowhere\n"));
            StringAssert.Contains("line 2", e.Message);
            Assert.Throws<DialogueCompileException>(() => DialogueCompiler.Compile("=== a\nno colon here\n"));
        }

        [Test]
        public void RunningAllocatesNothing()
        {
            var graph = DialogueCompiler.Compile(Script);
            var run = new DialogueRunner(graph);
            TestDelegate loop = () =>
            {
                for (int i = 0; i < 50; i++)
                {
                    run.Start("start");
                    run.Choose(0);
                    run.Advance();
                    run.ClearEvents();
                }
            };
            loop();
            Assert.That(loop, Is.Not.AllocatingGCMemory());
        }

        [Test]
        public void LocalizationParsesCsvAndFallsBack()
        {
            var table = LocalizationTable.FromCsv(Csv);
            CollectionAssert.AreEqual(new[] { "en", "zh" }, table.Languages);
            Assert.AreEqual("Halt! Who goes there?", table.Get("guard.halt"));
            Assert.AreEqual("Bribe (5 gold, maybe)", table.Get("choice.bribe"), "quoted comma");
            Assert.AreEqual("He says \"go on\".", table.Get("guard.warm"), "doubled quotes");
            Assert.IsTrue(table.SetLanguage("zh"));
            Assert.AreEqual("站住！什么人？", table.Get("guard.halt"));
            Assert.AreEqual("He says \"go on\".", table.Get("guard.warm"), "missing translation falls back to English");
            Assert.AreEqual("missing.key", table.Get("missing.key"));
            Assert.AreEqual("Leave", table.Get("\"Leave"), "literals pass through");
            var b = new TextBuilder();
            Assert.AreEqual("你有12枚金币", table.Format(b, "coins", 12).ToString());
        }
    }
}
