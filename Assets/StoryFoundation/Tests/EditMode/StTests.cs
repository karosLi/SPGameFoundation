using System;
using NUnit.Framework;
using SPF.L2.Narrative;
using SPF.Runtime.Composition;
using SPF.Runtime.Session;

namespace StoryFoundation.Tests
{
    sealed class StTestWorld : IDisposable
    {
        public readonly SimSession Session;
        readonly GameplayModuleAsset m_Module;
        readonly ModeDefinition m_Mode;

        public StTestWorld()
        {
            m_Mode = StMode.Create(out m_Module);
            Session = SimSession.Create(m_Mode, 1);
            Session.ManualClock = true;
            Session.Start();
        }

        public StState State => Session.World.Resource(StKeys.State);
        public DialogueRunner Run => State.Runner;
        public void Send(StCommandKind kind, int choice = 0) { State.Send(kind, choice); Session.Step(); }

        /// <summary>Clicks through lines and picks choices by their text key until the story ends.</summary>
        public void Play(params string[] choices)
        {
            int next = 0;
            for (int guard = 0; guard < 200 && Run.State != DialogueRunner.Mode.Ended; guard++)
            {
                if (Run.State == DialogueRunner.Mode.Choice)
                {
                    if (next >= choices.Length) return;   // stop at the next question
                    int pick = -1;
                    for (int i = 0; i < Run.ChoiceCount; i++) if (Run.ChoiceText(i) == choices[next]) pick = i;
                    Assert.GreaterOrEqual(pick, 0, "choice offered: " + choices[next]);
                    next++;
                    Send(StCommandKind.Choose, pick);
                }
                else Send(StCommandKind.Advance);
            }
        }

        public void Dispose()
        {
            Session.Dispose();
            UnityEngine.Object.DestroyImmediate(m_Module);
            UnityEngine.Object.DestroyImmediate(m_Mode);
        }
    }

    public class StTests
    {
        [Test]
        public void EveryKeyIsTranslated()
        {
            var table = LocalizationTable.FromCsv(StContent.Strings);
            foreach (var language in table.Languages)
                CollectionAssert.IsEmpty(table.Missing(StModule.Graph, language), language);
        }

        [Test]
        public void KindnessReachesTheGoodEnding()
        {
            using var t = new StTestWorld();
            t.Send(StCommandKind.Start);
            Assert.IsFalse(t.State.MiraShown, "the narration comes first");
            t.Send(StCommandKind.Advance);
            Assert.IsTrue(t.State.MiraShown, "'# show' ran before her line");
            t.Play("c.ask", "c.help", "c.relight");   // asking first unlocks relighting (trust 2)
            Assert.AreEqual(DialogueRunner.Mode.Ended, t.Run.State);
            Assert.GreaterOrEqual(t.Run["trust"], 3);
            Assert.AreEqual(3, t.State.Lanterns);
            Assert.AreEqual(1, t.State.Background, "ended on the bridge");
            Assert.AreEqual(Expression.Smile, t.State.Face);
        }

        [Test]
        public void IndifferenceEndsAlone()
        {
            using var t = new StTestWorld();
            t.Send(StCommandKind.Start);
            t.Send(StCommandKind.Advance);   // past the narration
            Assert.AreEqual(DialogueRunner.Mode.Choice, t.Run.State);
            t.Play("c.help", "c.shrug");
            Assert.Less(t.Run["trust"], 3);
            Assert.AreEqual(Expression.Sad, t.State.Face);
        }

        [Test]
        public void RelightIsLockedWithoutTrust()
        {
            using var t = new StTestWorld();
            t.Send(StCommandKind.Start);
            t.Play("c.help");
            for (int i = 0; i < 10 && t.Run.State != DialogueRunner.Mode.Choice; i++) t.Send(StCommandKind.Advance);
            Assert.AreEqual(2, t.Run.ChoiceCount, "shield and shrug only: trust is 1");
        }

        [Test]
        public void SavesRestoreTheExactLineAndScene()
        {
            using var t = new StTestWorld();
            t.Send(StCommandKind.Start);
            t.Play("c.ask");
            var save = t.Session.CaptureSnapshot();
            string text = t.Run.Text;
            int trust = t.Run["trust"];
            t.Play("c.help", "c.relight");
            Assert.AreEqual(DialogueRunner.Mode.Ended, t.Run.State);

            t.Session.RestoreSnapshot(save);
            Assert.AreEqual(text, t.Run.Text);
            Assert.AreEqual(trust, t.Run["trust"]);
            Assert.AreEqual(DialogueRunner.Mode.Choice, t.Run.State, "the question is asked again");
        }
    }
}
