using Unity.Mathematics;

namespace SPF.Contracts
{
    /// <summary>
    /// Game-agnostic input for one frame: a move vector and up to 32 buttons. <see cref="Held"/> is the
    /// current state; <see cref="Pressed"/> holds one-shot presses, which must stay latched until a
    /// simulation tick consumes them (frames without a tick would otherwise drop them): use
    /// <see cref="Latch"/> when storing a frame into the simulation's command resource.
    /// </summary>
    public struct InputFrame
    {
        public float2 Move;      // length 0..1
        public float2 Aim;       // optional direction (zero = none)
        public uint Held;
        public uint Pressed;

        public bool IsHeld(int button) => (Held & (1u << button)) != 0;
        public bool WasPressed(int button) => (Pressed & (1u << button)) != 0;

        /// <summary>New state from <paramref name="next"/>, keeping presses not yet consumed.</summary>
        public static InputFrame Latch(in InputFrame stored, in InputFrame next)
        {
            var frame = next;
            frame.Pressed |= stored.Pressed;
            return frame;
        }
    }
}
