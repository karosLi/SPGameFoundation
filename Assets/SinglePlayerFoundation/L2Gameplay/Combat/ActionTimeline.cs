using System;

namespace SPF.L2.Combat
{
    /// <summary>Fixed simulation tick interval [From, Until). Empty/reversed windows never match.</summary>
    [Serializable]
    public struct ActionWindow
    {
        public int From, Until;
        public ActionWindow(int from, int until) { From = from; Until = until; }
        public bool Contains(int tick) => From >= 0 && Until > From && tick >= From && tick < Until;
    }

    /// <summary>Small value-state timeline; owns no effects, queues, animations or allocation. Begin is
    /// tick 0; Advance visits (previous, current], so a skipped active window is still detected. Call once
    /// per simulation step, never per render frame. Persist all fields to resume an in-flight action.</summary>
    public struct ActionTimeline
    {
        public int PreviousTick, Tick;
        public uint PulseId;
        public bool Running;

        public void Begin()
        {
            PulseId = PulseId == uint.MaxValue ? 1 : PulseId + 1;
            PreviousTick = -1;
            Tick = 0;
            Running = true;
        }

        /// <summary>Negative steps fail without mutation; zero steps visit no additional ticks.</summary>
        public bool Advance(int ticks = 1)
        {
            if (!Running || ticks < 0 || ticks > int.MaxValue - Tick) return false;
            PreviousTick = Tick;
            Tick += ticks;
            return true;
        }

        public bool Crossed(in ActionWindow window) => Running && window.From >= 0 && window.Until > window.From &&
            Tick >= window.From && PreviousTick < window.Until - 1 && Tick > PreviousTick;

        /// <summary>Cancellation is allowed at the CURRENT tick only, not in a window already skipped.</summary>
        public bool TryCancel(in ActionWindow window)
        {
            if (!Running || !window.Contains(Tick)) return false;
            Running = false;
            return true;
        }

        public void Stop() => Running = false;
    }

    /// <summary>Latest-input-wins, single command buffer. Tick is an absolute monotonic simulation tick;
    /// valid through [pressedAt, pressedAt+lifetime). Ineligible consumption preserves unexpired input.
    /// Reset on interruption/owner reuse. This is value-state and must be saved with its owner.</summary>
    public struct TickInputBuffer
    {
        public int Command;
        public long PressedAt, ExpiresAt;
        public bool Pending;

        public bool Push(int command, long tick, int lifetimeTicks)
        {
            if (tick < 0 || lifetimeTicks <= 0 || tick > long.MaxValue - lifetimeTicks) return false;
            Command = command;
            PressedAt = tick;
            ExpiresAt = tick + lifetimeTicks;
            Pending = true;
            return true;
        }

        public bool TryConsume(long tick, bool eligible, out int command)
        {
            command = default;
            if (!Pending || tick < PressedAt) return false;
            if (tick >= ExpiresAt) { Clear(); return false; }
            if (!eligible) return false;
            command = Command;
            Clear();
            return true;
        }

        public void Clear() => this = default;
    }
}
