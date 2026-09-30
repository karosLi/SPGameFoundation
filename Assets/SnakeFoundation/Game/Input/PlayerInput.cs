using Unity.Mathematics;
using UnityEngine;

namespace SnakeFoundation.Game
{
    /// <summary>A source of player commands. Sources are polled in priority order; the first that answers wins.</summary>
    public interface IPlayerInputSource
    {
        bool TryRead(out PlayerCommand command);
    }

    /// <summary>Command set from code: UI automation tests, bots, tutorials.</summary>
    public sealed class ScriptedInput : IPlayerInputSource
    {
        public bool Active;
        public PlayerCommand Command;

        public bool TryRead(out PlayerCommand command)
        {
            command = Command;
            if (!Active) return false;
            Command.Skill = false;   // one-shot
            return true;
        }
    }

    /// <summary>Keyboard (WASD / arrows, Shift / Space boost, E skill) and mouse steering (legacy input manager).</summary>
    public sealed class KeyboardMouseInput : IPlayerInputSource
    {
        float2 m_LastMouse;
        float m_MouseIdle = 999f;

        public bool TryRead(out PlayerCommand command)
        {
            command = default;
#if ENABLE_LEGACY_INPUT_MANAGER
            float2 keys = new float2(
                (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow) ? 1f : 0f) - (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow) ? 1f : 0f),
                (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow) ? 1f : 0f) - (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow) ? 1f : 0f));
            bool boost = Input.GetKey(KeyCode.Space) || Input.GetKey(KeyCode.LeftShift);
            bool skill = Input.GetKeyDown(KeyCode.E);
            if (math.lengthsq(keys) > 0f)
            {
                command = new PlayerCommand { Direction = keys, Boost = boost, Skill = skill };
                return true;
            }

            if (!Input.mousePresent || Input.touchCount > 0)
                return false;
            float2 mouse = new float2(Input.mousePosition.x, Input.mousePosition.y);
            m_MouseIdle = math.distancesq(mouse, m_LastMouse) > 1f ? 0f : m_MouseIdle + Time.unscaledDeltaTime;
            m_LastMouse = mouse;
            if (m_MouseIdle > 5f && !Input.GetMouseButton(0))
                return false;
            // The camera keeps the player centred, so steer towards the cursor from the screen centre.
            float2 fromCenter = mouse - new float2(Screen.width, Screen.height) * 0.5f;
            if (math.lengthsq(fromCenter) < 64f)
                return false;
            command = new PlayerCommand
            {
                Direction = math.normalize(fromCenter),
                Boost = boost || Input.GetMouseButton(0),
                Skill = skill || Input.GetMouseButtonDown(1),
            };
            return true;
#else
            return false;
#endif
        }
    }
}
