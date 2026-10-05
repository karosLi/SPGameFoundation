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
        static IEnumerator Shot(StGameBootstrap game, string name)
        {
            var target = new RenderTexture(960, 540, 24, RenderTextureFormat.ARGB32);
            var read = new Texture2D(960, 540, TextureFormat.RGBA32, false);
            try
            {
                // UI is screen-space overlay: capture the whole screen at the end of the frame.
                yield return new WaitForEndOfFrame();
                var shot = ScreenCapture.CaptureScreenshotAsTexture();
                string dir = Path.Combine(Application.dataPath, "..", "Artifacts", "Screenshots");
                Directory.CreateDirectory(dir);
                File.WriteAllBytes(Path.Combine(dir, name), shot.EncodeToPNG());
                Object.Destroy(shot);
            }
            finally
            {
                target.Release();
                Object.Destroy(target);
                Object.Destroy(read);
            }
        }

        [UnityTest]
        public IEnumerator TapThroughChooseSwitchLanguageUndoAndSave()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("No graphics device");
            string saves = Path.Combine(Application.temporaryCachePath, "story-test");
            if (Directory.Exists(saves)) Directory.Delete(saves, true);
            var game = StGameBootstrap.Create(saves);
            try
            {
                yield return null;
                UIDriver.Click(game.StartButton.gameObject);
                yield return UIDriver.WaitUntil(() => game.State.InStory, 3f);
                var run = game.State.Runner;
                Assert.AreEqual(DialogueRunner.Mode.Line, run.State);

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
                for (int f = 0; f < 30 && game.Dialogue.Typing; f++) yield return null;
                if (game.Governor.GcCounterValid)
                {
                    GcReport.Write("story typewriter (second line)", game.Governor.FramesSinceReset, game.Governor.GcFramesSinceReset, game.Governor.GcBytesSinceReset);
                    Assert.LessOrEqual(game.Governor.GcFramesSinceReset, 1, "revealing text allocates nothing");
                }
                game.Dialogue.Finish();
                yield return UIDriver.WaitUntil(() => game.Dialogue.ChoicesShown == 3, 2f);
                Assert.AreEqual(3, game.Dialogue.ChoicesShown);
                yield return UIDriver.WaitSeconds(0.4f);   // portrait slides in
                yield return Shot(game, "story-en.png");

                game.ToggleLanguage();
                yield return UIDriver.WaitUntil(() => game.Dialogue.SpeakerText.ToString() != "Mira", 2f);
                Assert.AreEqual("米拉", game.Dialogue.SpeakerText.ToString(),
                    $"language {game.Strings.Language} v{game.Strings.Version}, box drew v{game.Dialogue.StringsVersionShown}, speaker key '{run.Speaker}' -> '{game.Strings.Get(run.Speaker)}', state {run.State}");
                StringAssert.Contains("灯笼", game.Dialogue.BodyText.ToString());
                yield return Shot(game, "story-zh.png");

                // Choose "ask" (second button), save, play on, load: back at the saved line.
                UIDriver.Click(game.Dialogue.Choice(1).gameObject);
                yield return UIDriver.WaitUntil(() => run.State == DialogueRunner.Mode.Line, 2f);
                Assert.AreEqual(1, run["trust"]);
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
                Assert.AreEqual(1, run["trust"]);
            }
            finally
            {
                if (Camera.main != null) Object.Destroy(Camera.main.gameObject);
                Object.Destroy(game.gameObject);
            }
        }
    }
}
