using System.IO;
using SPF.L2.Skills;
using SPF.L2.Weapons;
using Unity.Mathematics;

namespace SPF.Runtime.Session
{
    /// <summary>Cold canonical field encoding shared by the two opted-in weapon save adapters.
    /// Consumes existing frozen definitions, never mutates profile/skill state or changes raw writers.</summary>
    public static class WeaponSaveContent
    {
        public static void WriteSession(BinaryWriter w, SimSession session)
        {
            w.Write("spf.fixed-tick-row-order"); w.Write(1);
            w.Write(session.Clock.TickRate); w.Write(session.Clock.MaxTicksPerFrame); w.Write(session.ManualClock);
            w.Write(session.World.Seed); session.World.WriteSnapshotCapacities(w);
        }
        public static void WriteRules(BinaryWriter w, WeaponRuntime weapons, SkillSlots slots)
        {
            w.Write("weapon-rules"); w.Write(2); w.Write(weapons.TickRate); w.Write(weapons.Count);
            w.Write(weapons.Projectiles.Length); w.Write(weapons.History.Length); w.Write(weapons.Scopes.Length);
            w.Write(weapons.HistoryPerAttack); w.Write(weapons.Cues.Length);
            for (int i = 0; i < weapons.Count; i++)
            {
                var p = weapons.ProfileAt(i); p.Validate();
                w.Write(p.ContentId); w.Write((int)p.Family); w.Write(p.DurationTicks); w.Write(p.EquipTicks);
                w.Write(p.Active.From); w.Write(p.Active.Until); w.Write(p.Cancel.From); w.Write(p.Cancel.Until);
                w.Write(p.ReleaseTick); w.Write(p.ProjectileLifeTicks); w.Write(p.Damage); w.Write(p.Knockback);
                w.Write(p.Reach); w.Write(p.Radius); w.Write(p.ProjectileSpeed);
                // Grip.x and muzzle determine actual melee/projectile geometry in the adapters.
                Write(w, p.GripOffset); Write(w, p.MuzzleOffset);
            }
            w.Write("skill-slots"); w.Write(1); w.Write(slots.Count);
            for (int i = 0; i < slots.Count; i++)
            {
                var d = slots.GetSnapshot(i).Definition;
                w.Write(d.Id); w.Write((byte)d.Activation); w.Write(d.CooldownTicks); w.Write(d.MaxCharges);
            }
        }
        public static void WriteVisuals(BinaryWriter w, WeaponRuntime weapons, SkillSlots slots)
        {
            w.Write(weapons.Count);
            for (int i = 0; i < weapons.Count; i++)
            {
                var p = weapons.ProfileAt(i); w.Write(p.ContentId); w.Write(p.VisualId); w.Write(p.Name);
                Write(w, p.GripOffset); Write(w, p.SecondaryGripOffset); Write(w, p.MuzzleOffset);
            }
            w.Write(slots.Count);
            for (int i = 0; i < slots.Count; i++) w.Write(slots.GetSnapshot(i).Definition.IconId);
        }
        public static void WriteRawCompatibility(BinaryWriter w, WeaponRuntime weapons, SkillSlots slots)
        {
            // Preserve existing v2/v1 reader gates. Visual identity alone does not bypass them.
            w.Write("weapon.raw-v2"); w.Write(weapons.SnapshotContentFingerprint);
            w.Write("slots.raw-v1"); w.Write(slots.Count);
            for (int i = 0; i < slots.Count; i++) w.Write(slots.GetSnapshot(i).Definition.IconId);
        }
        public static void Write(BinaryWriter w, float2 value) { w.Write(value.x); w.Write(value.y); }
    }
}
