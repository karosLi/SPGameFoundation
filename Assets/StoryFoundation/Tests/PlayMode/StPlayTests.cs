using System.Collections;
using System.IO;
using NUnit.Framework;
using SPF.L2.Narrative;
using SPF.Presentation;
using SPF.Testing;
using StoryFoundation.Game;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;

namespace StoryFoundation.Tests.PlayMode
{
    public class StPlayTests
    {
        [UnityTest]
        public IEnumerator TapThroughChooseSwitchLanguageUndoAndSave()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("No graphics device");
            string saves = Path.Combine(Application.temporaryCachePath, "story-test");
            if (Directory.Exists(saves)) Directory.Delete(saves, true);
#if UNITY_EDITOR && !SPF_DOTNET_HARNESS
            using var capture = StAllocationCapture.CreateIfRequested();
#endif
            var game = StGameBootstrap.Create(saves);
            try
            {
                yield return null;
                UIDriver.Click(game.StartButton.gameObject);
                yield return UIDriver.WaitUntil(() => game.State.InStory, 3f);
                var run = game.State.Runner;
                Assert.AreEqual(DialogueRunner.Mode.Line, run.State);

#if UNITY_EDITOR && !SPF_DOTNET_HARNESS
                capture?.Warm(game);
#endif
                // The first line warms up UGUI's shared mesh buffers (they grow with the longest text once).
                yield return null;
                Assert.IsTrue(game.Dialogue.Typing, "text is revealed over several frames");
                for (int f = 0; f < 600 && game.Dialogue.Typing; f++) yield return null;

                // Tap: the next line (Mira appears and asks). Its typewriter reveal must allocate nothing.
                UIDriver.Click(game.Dialogue.TapCatcher.gameObject);
                yield return UIDriver.WaitUntil(() => run.State == DialogueRunner.Mode.Choice, 2f);
                yield return null;
                yield return null;
                game.Governor.ResetGcStats();
                for (int f = 0; f < 30 && game.Dialogue.Typing; f++)
                {
#if UNITY_EDITOR && !SPF_DOTNET_HARNESS
                    capture?.Frame(game);
#endif
                    yield return null;
                }
                // Save the original result before any diagnostic drain frames or export work.
                bool gcCounterValid = game.Governor.GcCounterValid;
                int measuredFrames = game.Governor.FramesSinceReset;
                int allocatingFrames = game.Governor.GcFramesSinceReset;
                long allocatedBytes = game.Governor.GcBytesSinceReset;
#if UNITY_EDITOR && !SPF_DOTNET_HARNESS
                if (capture != null)
                {
                    capture.End(game);
                    for (int f = 0; f < 3; f++) yield return null;
                    capture.Export();
                }
#endif
                if (gcCounterValid)
                {
                    GcReport.Write("story typewriter (second line)", measuredFrames, allocatingFrames, allocatedBytes);
                    Assert.LessOrEqual(allocatingFrames, 1, "revealing text allocates nothing");
                }
                game.Dialogue.Finish();
                yield return UIDriver.WaitUntil(() => game.Dialogue.ChoicesShown == 3, 2f);
                Assert.AreEqual(3, game.Dialogue.ChoicesShown);
                yield return UIDriver.WaitSeconds(0.4f);   // portrait slides in

                game.ToggleLanguage();
                yield return UIDriver.WaitUntil(() => game.Dialogue.SpeakerText.ToString() != "Mira", 2f);
                Assert.AreEqual("米拉", game.Dialogue.SpeakerText.ToString(),
                    $"language {game.Strings.Language} v{game.Strings.Version}, box drew v{game.Dialogue.StringsVersionShown}, speaker key '{run.Speaker}' -> '{game.Strings.Get(run.Speaker)}', state {run.State}");
                StringAssert.Contains("灯笼", game.Dialogue.BodyText.ToString());

                // Choose "ask" (second button), save, play on, load: back at the saved line.
                UIDriver.Click(game.Dialogue.Choice(1).gameObject);
                yield return UIDriver.WaitUntil(() => run.State == DialogueRunner.Mode.Line, 2f);
                Assert.AreEqual("m.story", run.Text, "asked about the festival");   // trust rises after this line
                int created = game.Dialogue.ChoiceButtonsCreated;
                UIDriver.Click(game.SaveButton.gameObject);
                string savedText = run.Text;
                game.Dialogue.Finish();
                UIDriver.Click(game.Dialogue.TapCatcher.gameObject);
                yield return UIDriver.WaitUntil(() => run.State == DialogueRunner.Mode.Choice, 2f);
                game.Dialogue.Finish();
                yield return UIDriver.WaitUntil(() => game.Dialogue.ChoicesShown > 0, 2f);
                Assert.AreEqual(created, game.Dialogue.ChoiceButtonsCreated, "choice buttons come from the pool");

                UIDriver.Click(game.BackButton.gameObject);
                yield return null;
                Assert.AreEqual(savedText, run.Text, "BACK undid one line");
                UIDriver.Click(game.LoadButton.gameObject);
                yield return null;
                Assert.AreEqual(savedText, run.Text, "LOAD restored the saved line");
                Assert.AreEqual(0, run["trust"], "variables restored with it");
            }
            finally
            {
                if (Camera.main != null) Object.Destroy(Camera.main.gameObject);
                Object.Destroy(game.gameObject);
            }
        }
    }
}
