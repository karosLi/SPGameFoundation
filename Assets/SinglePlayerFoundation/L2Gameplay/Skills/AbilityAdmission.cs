namespace SPF.L2.Skills
{
    public enum AbilityRejection : byte { None, NotRequested, NotPlaying, Dead, Busy, NoCharge }

    /// <summary>Pure fixed-tick admission result shared by the two opt-in game rules. This does not
    /// spend charges, begin an action, resolve a hit, or emit a visual cue. The game commits only after
    /// Allowed, using its existing SkillSlots.TryActivate gate. A committed action may still whiff.</summary>
    public readonly struct AbilityAdmission
    {
        public readonly AbilityRejection Reason;
        public bool Allowed => Reason == AbilityRejection.None;
        AbilityAdmission(AbilityRejection reason) { Reason = reason; }
        public static AbilityAdmission Evaluate(bool playing, bool alive, bool free, bool requested, int charges) =>
            new AbilityAdmission(!requested ? AbilityRejection.NotRequested : !playing ? AbilityRejection.NotPlaying :
                !alive ? AbilityRejection.Dead : !free ? AbilityRejection.Busy : charges <= 0 ? AbilityRejection.NoCharge : AbilityRejection.None);
    }

}
