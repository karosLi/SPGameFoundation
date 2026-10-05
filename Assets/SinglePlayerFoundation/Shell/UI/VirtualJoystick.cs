using Unity.Mathematics;
using UnityEngine;
using UnityEngine.EventSystems;

namespace SPF.Shell.UI
{
    /// <summary>
    /// Floating virtual joystick: press anywhere in its area, drag to steer. <see cref="Direction"/> is a
    /// unit vector (or zero inside the dead zone); <see cref="Magnitude"/> is 0..1 over <see cref="Radius"/>.
    /// </summary>
    public sealed class VirtualJoystick : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        public float DeadZone = 12f;
        public float Radius = 120f;

        Vector2 m_Origin;
        public bool Pressed { get; private set; }
        public float2 Direction { get; private set; }
        public float Magnitude { get; private set; }

        public void OnPointerDown(PointerEventData eventData)
        {
            Pressed = true;
            m_Origin = eventData.position;
            Direction = float2.zero;
            Magnitude = 0f;
        }

        public void OnDrag(PointerEventData eventData)
        {
            Vector2 delta = eventData.position - m_Origin;
            if (delta.sqrMagnitude > DeadZone * DeadZone)
            {
                Direction = math.normalize(new float2(delta.x, delta.y));
                Magnitude = math.saturate(delta.magnitude / math.max(Radius, 1f));
            }
        }

        public void OnPointerUp(PointerEventData eventData) => Pressed = false;
    }
}
