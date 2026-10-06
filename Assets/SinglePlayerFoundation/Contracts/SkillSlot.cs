using System;

namespace SPF.Contracts
{
    public enum SkillActivation : byte { Tap, Hold, AimRelease }

    /// <summary>Immutable authored rules. IDs select presentation assets; they never select damage logic.</summary>
    public readonly struct SkillSlotDefinition
    {
        public readonly int Id, IconId, CooldownTicks, MaxCharges;
        public readonly SkillActivation Activation;
        public SkillSlotDefinition(int id, int iconId, SkillActivation activation, int cooldownTicks, int maxCharges = 1)
        {
            if (id <= 0 || iconId < 0 || cooldownTicks < 1 || maxCharges < 1 || maxCharges > 16 || (byte)activation > 2)
                throw new ArgumentOutOfRangeException(nameof(id), "Invalid skill definition.");
            Id = id; IconId = iconId; Activation = activation; CooldownTicks = cooldownTicks; MaxCharges = maxCharges;
        }
    }

    /// <summary>A read-only authoritative snapshot. UI has no elapsed-time/cooldown ownership.</summary>
    public readonly struct SkillSlotSnapshot
    {
        public readonly SkillSlotDefinition Definition;
        public readonly int Charges, RechargeTicks;
        public readonly bool Enabled;
        public bool Ready => Enabled && Charges > 0;
        public SkillSlotSnapshot(in SkillSlotDefinition definition, int charges, int rechargeTicks, bool enabled)
        { Definition = definition; Charges = charges; RechargeTicks = rechargeTicks; Enabled = enabled; }
    }

    public static class SkillInput
    {
        /// <summary>Local gesture cancellation must not erase another slot's latched press or movement.</summary>
        public static InputFrame CancelSlot(in InputFrame frame, int slot, int aimSlot = -1)
        {
            if ((uint)slot >= 32) throw new ArgumentOutOfRangeException(nameof(slot));
            var result = frame;
            uint mask = ~(1u << slot);
            result.Pressed &= mask; result.Held &= mask;
            if (slot == aimSlot) result.Aim = default;
            return result;
        }

        /// <summary>One aim-release skill per InputFrame. Keep its release direction across render frames
        /// with no simulation tick, just as Pressed is latched. A new press replaces the pending direction.</summary>
        public static InputFrame Latch(in InputFrame stored, in InputFrame next, int aimSlot)
        {
            if ((uint)aimSlot >= 32) throw new ArgumentOutOfRangeException(nameof(aimSlot));
            var result = InputFrame.Latch(stored, next);
            if ((next.Pressed & (1u << aimSlot)) == 0 && (stored.Pressed & (1u << aimSlot)) != 0) result.Aim = stored.Aim;
            return result;
        }
    }
}
