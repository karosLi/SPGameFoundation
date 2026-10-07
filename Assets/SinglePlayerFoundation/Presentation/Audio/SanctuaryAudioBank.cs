using System;
using UnityEngine;

namespace SPF.Presentation.Audio
{
    /// <summary>Shared, authored original bank. Resources clips are borrowed and never destroyed by
    /// this adapter. Missing clips stay missing (and counted by SoundPlayer), not silently replaced.</summary>
    public sealed class SanctuaryAudioBank
    {
        public readonly int Knife, Sword, BowDraw, BowRelease, Staff, LightImpact, HeavyImpact, Hurt, Heal, Equip, Confirm, Cancel, Exploration, Combat;
        public SanctuaryAudioBank(SoundPlayer player, Func<string, AudioClip> load = null)
        {
            load ??= Resources.Load<AudioClip>;
            int Add(string name, SoundBus bus = SoundBus.Sfx, int max = 2, float interval = .06f, int priority = 0)
                => player.Register("sanctuary/" + name, load("Audio/Sanctuary/" + name), bus, .8f, max, interval, priority);
            Knife = Add("knife_swing"); Sword = Add("sword_swing"); BowDraw = Add("bow_draw", max: 1);
            BowRelease = Add("bow_release"); Staff = Add("staff_cast");
            LightImpact = Add("impact_light", max: 3, interval: .045f, priority: 1);
            HeavyImpact = Add("impact_heavy", max: 2, interval: .09f, priority: 2);
            Hurt = Add("hurt", max: 1, interval: .15f, priority: 4); Heal = Add("heal", max: 1, interval: .12f, priority: 3);
            Equip = Add("equip", max: 1, interval: .10f, priority: 2);
            Confirm = Add("ui_confirm", SoundBus.Ui, 2, .04f, 5); Cancel = Add("ui_cancel", SoundBus.Ui, 2, .04f, 5);
            Exploration = Add("exploration", SoundBus.Music, 1, 0, 6); Combat = Add("combat", SoundBus.Music, 1, 0, 6);
        }
    }
}
