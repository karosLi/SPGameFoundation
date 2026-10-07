#if !SPF_DOTNET_HARNESS
using System.Collections;
using NUnit.Framework;
using SPF.Contracts;
using SPF.Presentation;
using SPF.Testing;
using SurvivorFoundation.Game;
using UnityEngine;
using UnityEngine.TestTools;

namespace SurvivorFoundation.Tests.PlayMode
{
    public sealed class SvAudioGameplayTests
    {
        [UnityTest]
        public IEnumerator ActualWeaponSessionAudioConsumesFactsOnceAndSuppressesRestoreBacklog()
        {
            var config = SvConfig.CreateWeaponCombatExample(); config.Settings.SpawnPerSecond = config.Settings.SpawnGrowth = config.Settings.EliteEvery = 0;
            var game = SvGameBootstrap.CreateWeaponCombatExample(config, ui: false);
            try
            {
                yield return null; game.StartRun(); yield return UIDriver.WaitUntil(() => game.State.Flow == SvFlow.Playing, 5);
                game.Session.Sync(); game.Session.ManualClock = true; game.InputRouter.enabled = false; game.Audio.Pump();
                var weapon = game.Session.World.Resource(SvWeapons.Key); int before = game.Audio.Player.Played; weapon.RequestEquip(1004);
                for (int i = 0; i < 125; i++)
                {
                    game.State.Input = new InputFrame { Held = 1u << SvWeapons.AttackButton };
                    game.Session.Step(); game.Session.Sync(); game.Audio.Pump();
                }
                Assert.Greater(weapon.Releases, 0); Assert.Greater(game.Audio.Player.Played, before);
                Assert.Greater(game.Audio.Cursor.RangedReleasesPlayed, 0, "equip/draw alone cannot satisfy release evidence");
                Assert.AreEqual(game.Audio.Player.Find("sanctuary/bow_release"), game.Audio.Cursor.LastRangedReleaseSound);
                int accepted = game.Audio.Cursor.Accepted; game.Audio.Pump(); game.Audio.Pump(); Assert.AreEqual(accepted, game.Audio.Cursor.Accepted);
                byte[] saved = game.Session.CaptureSnapshot(); int played = game.Audio.Player.Played;
                game.Session.RestoreSnapshot(saved); game.Audio.Pump(); Assert.AreEqual(played, game.Audio.Player.Played);
                Assert.AreEqual(weapon.Equipment.CueSequence, game.Audio.Cursor.Sequence);
                game.Audio.enabled = false; game.Audio.enabled = true; game.Audio.Pump(); Assert.AreEqual(played, game.Audio.Player.Played);
                game.Session.Pause(); game.Audio.Pump(); Assert.IsTrue(game.Audio.Player.Paused); game.Session.Resume(); game.Audio.Pump(); Assert.IsFalse(game.Audio.Player.Paused);
                game.Audio.enabled = false; game.Audio.Bind(game.Renderer); yield return null;
                foreach (var source in game.Audio.Player.GetComponents<AudioSource>()) Assert.IsFalse(source.isPlaying, "disabled adapter rebind must remain silent");
                game.Audio.enabled = true; game.Audio.Pump(); game.Audio.Player.enabled = false; yield return null; game.Audio.Pump();
                foreach (var source in game.Audio.Player.GetComponents<AudioSource>()) Assert.IsFalse(source.isPlaying, "disabled player must remain silent while adapter runs");
                Debug.Log("SPF_SV_AUDIO_FACTS: real weapon release, independent cursor, same-tick restore, disable/enable and session pause contracts passed; no microphone or speaker-quality inference.");
            }
            finally { RenderObjects.Destroy(game.gameObject); RenderObjects.Destroy(config); }
        }
    }
}
#endif
