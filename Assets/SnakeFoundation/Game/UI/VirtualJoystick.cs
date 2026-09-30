using Unity.Mathematics;
using UnityEngine;
using UnityEngine.EventSystems;

namespace SnakeFoundation.Game.UI
{
    /// <summary>
    /// Floating virtual joystick: press anywhere in its area, drag to steer. Also an input source,
    /// so it plugs straight into the <see cref="InputRouter"/>.
    /// </summary>
    public sealed class VirtualJoystick : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler, IPlayerInputSource
    {
        public float DeadZone = 12f;
        public HoldButton Boost;
        public TapButton Skill;

        Vector2 m_Origin;
        public bool Pressed { get; private set; }
        public float2 Direction { get; private set; }

        public void OnPointerDown(PointerEventData eventData)
        {
            Pressed = true;
            m_Origin = eventData.position;
            Direction = float2.zero;
        }

        public void OnDrag(PointerEventData eventData)
        {
            Vector2 delta = eventData.position - m_Origin;
            if (delta.sqrMagnitude > DeadZone * DeadZone)
                Direction = math.normalize(new float2(delta.x, delta.y));
        }

        public void OnPointerUp(PointerEventData eventData) => Pressed = false;

        public bool TryRead(out PlayerCommand command)
        {
            bool boost = Boost != null && Boost.Held;
            bool skill = Skill != null && Skill.Consume();
            command = new PlayerCommand { Direction = Pressed ? Direction : float2.zero, Boost = boost, Skill = skill };
            // Buttons alone still count (boosting straight ahead).
            return Pressed || boost || skill;
        }
    }
}
