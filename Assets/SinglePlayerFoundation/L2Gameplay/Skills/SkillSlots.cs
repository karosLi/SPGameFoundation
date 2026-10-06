using System;
using System.IO;
using SPF.Contracts;

namespace SPF.L2.Skills
{
    /// <summary>Bounded main-thread fixed-tick charge gate, installed only by opted-in gameplay modules.
    /// The consumer chooses eligibility and applies the actual skill. No frame timers or Unity UI.
    /// Recharge is sequential: spending another charge never restarts an already running recharge.</summary>
    public sealed class SkillSlots : ISnapshotResource, IResettableResource
    {
        const int Magic = 0x534B4C31;
        readonly SkillSlotDefinition[] m_Definitions;
        readonly int[] m_Charges, m_Recharge;
        public int Count => m_Definitions.Length;
        public uint Activated { get; private set; }

        public SkillSlots(params SkillSlotDefinition[] definitions)
        {
            if (definitions == null || definitions.Length < 1 || definitions.Length > 4)
                throw new ArgumentException("A skill bank contains 1–4 authored slots.");
            m_Definitions = (SkillSlotDefinition[])definitions.Clone();
            for (int i = 0; i < Count; i++)
            {
                var d = m_Definitions[i];
                if (d.Id <= 0 || d.CooldownTicks < 1 || d.MaxCharges < 1 || d.MaxCharges > 16 || (byte)d.Activation > 2)
                    throw new ArgumentException("Invalid skill definition.");
                for (int j = 0; j < i; j++) if (m_Definitions[j].Id == d.Id) throw new ArgumentException("Duplicate skill ID.");
            }
            m_Charges = new int[Count]; m_Recharge = new int[Count]; OnReset();
        }

        public SkillSlotSnapshot GetSnapshot(int slot, bool enabled = true) =>
            new SkillSlotSnapshot(m_Definitions[slot], m_Charges[slot], m_Recharge[slot], enabled);

        /// <summary>Call once at the beginning of each eligible playing tick, then attempt activation.</summary>
        public void AdvanceTick(bool playing)
        {
            Activated = 0;
            if (!playing) return;
            for (int i = 0; i < Count; i++)
            {
                if (m_Recharge[i] == 0) continue;
                if (--m_Recharge[i] != 0) continue;
                m_Charges[i]++;
                if (m_Charges[i] < m_Definitions[i].MaxCharges) m_Recharge[i] = m_Definitions[i].CooldownTicks;
            }
        }

        public bool TryActivate(int slot, bool enabled)
        {
            if ((uint)slot >= Count || !enabled || m_Charges[slot] == 0 || (Activated & (1u << slot)) != 0) return false;
            m_Charges[slot]--;
            if (m_Recharge[slot] == 0) m_Recharge[slot] = m_Definitions[slot].CooldownTicks;
            Activated |= 1u << slot;
            return true;
        }

        public void OnReset()
        {
            Activated = 0;
            for (int i = 0; i < Count; i++) { m_Charges[i] = m_Definitions[i].MaxCharges; m_Recharge[i] = 0; }
        }

        public void WriteSnapshot(BinaryWriter w)
        {
            w.Write(Magic); w.Write(1); w.Write(Count); w.Write(Activated);
            for (int i = 0; i < Count; i++)
            {
                var d = m_Definitions[i];
                w.Write(d.Id); w.Write(d.IconId); w.Write((byte)d.Activation); w.Write(d.CooldownTicks); w.Write(d.MaxCharges);
                w.Write(m_Charges[i]); w.Write(m_Recharge[i]);
            }
        }

        public void ReadSnapshot(BinaryReader r)
        {
            if (r.ReadInt32() != Magic || r.ReadInt32() != 1 || r.ReadInt32() != Count)
                throw new InvalidDataException("Unsupported skill bank layout.");
            Activated = r.ReadUInt32();
            if ((Activated >> Count) != 0) throw new InvalidDataException("Invalid activated skill mask.");
            for (int i = 0; i < Count; i++)
            {
                var d = m_Definitions[i];
                if (r.ReadInt32() != d.Id || r.ReadInt32() != d.IconId || r.ReadByte() != (byte)d.Activation ||
                    r.ReadInt32() != d.CooldownTicks || r.ReadInt32() != d.MaxCharges)
                    throw new InvalidDataException("Skill definitions differ from the captured session.");
                int charges = r.ReadInt32(), recharge = r.ReadInt32();
                if (charges < 0 || charges > d.MaxCharges || recharge < 0 || recharge > d.CooldownTicks ||
                    (charges == d.MaxCharges) != (recharge == 0)) throw new InvalidDataException("Invalid skill charge state.");
                m_Charges[i] = charges; m_Recharge[i] = recharge;
            }
        }
    }
}
