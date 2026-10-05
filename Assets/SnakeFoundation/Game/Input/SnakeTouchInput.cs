using SPF.Shell.UI;

namespace SnakeFoundation.Game
{
    /// <summary>Touch controls (shell joystick + boost / skill buttons) as a snake command source.</summary>
    public sealed class SnakeTouchInput : IPlayerInputSource
    {
        readonly VirtualJoystick m_Joystick;
        readonly HoldButton m_Boost;
        readonly TapButton m_Skill;

        public SnakeTouchInput(VirtualJoystick joystick, HoldButton boost, TapButton skill)
        {
            m_Joystick = joystick;
            m_Boost = boost;
            m_Skill = skill;
        }

        public bool TryRead(out PlayerCommand command)
        {
            bool boost = m_Boost != null && m_Boost.Held;
            bool skill = m_Skill != null && m_Skill.Consume();
            bool pressed = m_Joystick != null && m_Joystick.Pressed;
            command = new PlayerCommand { Direction = pressed ? m_Joystick.Direction : default, Boost = boost, Skill = skill };
            // Buttons alone still count (boosting straight ahead).
            return pressed || boost || skill;
        }
    }
}
