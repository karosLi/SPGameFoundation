#if !SPF_DOTNET_HARNESS
using System.Collections;
using NUnit.Framework;
using SPF.Contracts;
using SPF.Presentation;
using SPF.Testing;
using BrawlerFoundation.Game;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.TestTools;

namespace BrawlerFoundation.Tests.PlayMode
{
    public sealed class BwAudioGameplayTests
    {
        [UnityTest]
        public IEnumerator ActualBeltWeaponAudioConsumesFactsOnceAndRebindDoesNotDuplicate()
        {
            var game = BwGameBootstrap.CreateWeaponBelt(ui: false);
            try
            {
                yield return null; game.State.Send(BwCommandKind.Start); yield return UIDriver.WaitUntil(() => game.State.Flow == BwFlow.Fighting, 5);
                game.Session.Sync(); game.Session.ManualClock = true; game.InputRouter.enabled = false; game.Audio.Pump();
                var world = game.Session.World; world.ClearLevel(); game.State.Flow = BwFlow.Fighting;
                BwSpawner.Spawn(world, 0, 0, 1, 0); BwSpawner.Spawn(world, 1, new float2(12, 2), -1, 0);
                var foes = world.Column(BwKeys.Info); var foe = foes[1]; foe.Hp = foe.MaxHp = 10000; foes[1] = foe;
                game.Audio.Pump();
                var weapon = game.Session.World.Resource(BwWeapons.Key); int before = game.Audio.Player.Played; weapon.RequestEquip(1004);
                for (int i = 0; i < 125; i++)
                {
                    game.State.Input = new InputFrame { Held = 1u << BwButton.Punch };
                    game.Session.Step(); game.Session.Sync(); game.Audio.Pump();
                }
                Assert.Greater(weapon.Releases, 0); Assert.Greater(game.Audio.Player.Played, before);
                Assert.Greater(game.Audio.Cursor.RangedReleasesPlayed, 0, "equip/draw alone cannot satisfy release evidence");
                Assert.AreEqual(game.Audio.Player.Find("sanctuary/bow_release"), game.Audio.Cursor.LastRangedReleaseSound);
                int accepted = game.Audio.Cursor.Accepted; game.Audio.Pump(); Assert.AreEqual(accepted, game.Audio.Cursor.Accepted);
                byte[] saved = game.Session.CaptureSnapshot(); int played = game.Audio.Player.Played;
                game.Session.RestoreSnapshot(saved); game.Audio.Pump(); Assert.AreEqual(played, game.Audio.Player.Played);
                game.Audio.Bind(game.Renderer); game.Audio.Bind(game.Renderer); game.Audio.Pump(); Assert.AreEqual(played, game.Audio.Player.Played);
                Assert.AreEqual(18, game.Audio.Player.GetComponents<AudioSource>().Length);
                game.Audio.enabled = false; game.Audio.Bind(game.Renderer); yield return null;
                foreach (var source in game.Audio.Player.GetComponents<AudioSource>()) Assert.IsFalse(source.isPlaying, "disabled adapter rebind must remain silent");
                game.Audio.enabled = true; game.Audio.Pump(); game.Audio.Player.enabled = false; yield return null; game.Audio.Pump();
                foreach (var source in game.Audio.Player.GetComponents<AudioSource>()) Assert.IsFalse(source.isPlaying, "disabled player must remain silent while adapter runs");
                Debug.Log("SPF_BW_AUDIO_FACTS: real weapon release, cursor deduplication, restore and repeated rebind passed; native DSP output is a separate probe.");
            }
            finally { RenderObjects.Destroy(game.gameObject); }
        }
    }
}
#endif
