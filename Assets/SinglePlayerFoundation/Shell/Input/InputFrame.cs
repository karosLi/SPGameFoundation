using System.Collections.Generic;
using SPF.Shell.UI;
using Unity.Mathematics;
using UnityEngine;

namespace SPF.Shell.Input
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

    /// <summary>A source of input frames, polled in priority order; the first that answers wins.</summary>
    public interface IInputSource
    {
        bool TryRead(ref InputFrame frame);
    }

    /// <summary>Input set from code: UI automation, bots, tutorials. Presses are one-shot.</summary>
    public sealed class ScriptedInputSource : IInputSource
    {
        public bool Active;
        public InputFrame Frame;

        public void Press(int button) => Frame.Pressed |= 1u << button;

        public bool TryRead(ref InputFrame frame)
        {
            if (!Active) return false;
            frame = Frame;
            Frame.Pressed = 0;
            return true;
        }
    }

    /// <summary>Touch controls: a <see cref="VirtualJoystick"/> plus buttons mapped to button indices.</summary>
    public sealed class TouchInputSource : IInputSource
    {
        readonly VirtualJoystick m_Joystick;
        readonly List<(HoldButton hold, TapButton tap, int index)> m_Buttons = new List<(HoldButton, TapButton, int)>();

        public TouchInputSource(VirtualJoystick joystick) => m_Joystick = joystick;

        public TouchInputSource Hold(HoldButton button, int index) { m_Buttons.Add((button, null, index)); return this; }
        public TouchInputSource Tap(TapButton button, int index) { m_Buttons.Add((null, button, index)); return this; }

        public bool TryRead(ref InputFrame frame)
        {
            bool any = false;
            var result = default(InputFrame);
            if (m_Joystick != null && m_Joystick.Pressed)
            {
                result.Move = m_Joystick.Direction * m_Joystick.Magnitude;
                any = true;
            }
            for (int i = 0; i < m_Buttons.Count; i++)
            {
                var (hold, tap, index) = m_Buttons[i];
                if (hold != null && hold.Held) { result.Held |= 1u << index; any = true; }
                if (tap != null && tap.Consume()) { result.Pressed |= 1u << index; any = true; }
            }
            if (any) frame = result;
            return any;
        }
    }

    /// <summary>Keyboard (legacy input manager): WASD / arrows move, configurable keys per button.</summary>
    public sealed class KeyboardInputSource : IInputSource
    {
        readonly List<(KeyCode key, int index)> m_Keys = new List<(KeyCode, int)>();

        public KeyboardInputSource Map(KeyCode key, int button) { m_Keys.Add((key, button)); return this; }

        public bool TryRead(ref InputFrame frame)
        {
#if ENABLE_LEGACY_INPUT_MANAGER
            var result = default(InputFrame);
            float2 move = new float2(
                (UnityEngine.Input.GetKey(KeyCode.D) || UnityEngine.Input.GetKey(KeyCode.RightArrow) ? 1f : 0f) - (UnityEngine.Input.GetKey(KeyCode.A) || UnityEngine.Input.GetKey(KeyCode.LeftArrow) ? 1f : 0f),
                (UnityEngine.Input.GetKey(KeyCode.W) || UnityEngine.Input.GetKey(KeyCode.UpArrow) ? 1f : 0f) - (UnityEngine.Input.GetKey(KeyCode.S) || UnityEngine.Input.GetKey(KeyCode.DownArrow) ? 1f : 0f));
            result.Move = math.lengthsq(move) > 0f ? math.normalize(move) : float2.zero;
            for (int i = 0; i < m_Keys.Count; i++)
            {
                if (UnityEngine.Input.GetKey(m_Keys[i].key)) result.Held |= 1u << m_Keys[i].index;
                if (UnityEngine.Input.GetKeyDown(m_Keys[i].key)) result.Pressed |= 1u << m_Keys[i].index;
            }
            if (math.lengthsq(result.Move) == 0f && result.Held == 0 && result.Pressed == 0)
                return false;
            frame = result;
            return true;
#else
            return false;
#endif
        }
    }

    /// <summary>
    /// Polls sources in priority order every frame (before the session schedules ticks) and hands the
    /// frame to <see cref="Sink"/>, which stores it into the simulation (typically with
    /// <see cref="InputFrame.Latch"/>). The scripted source always comes first.
    /// </summary>
    [DefaultExecutionOrder(-1100)]
    public sealed class InputRouter : MonoBehaviour
    {
        readonly List<IInputSource> m_Sources = new List<IInputSource>();

        public ScriptedInputSource Scripted { get; } = new ScriptedInputSource();
        public InputFrame Last { get; private set; }
        public System.Action<InputFrame> Sink;

        void Awake() => m_Sources.Add(Scripted);

        public void AddSource(IInputSource source) => m_Sources.Add(source);

        void Update()
        {
            var frame = default(InputFrame);
            for (int i = 0; i < m_Sources.Count; i++)
                if (m_Sources[i].TryRead(ref frame))
                    break;
            Last = frame;
            Sink?.Invoke(frame);
        }
    }
}
