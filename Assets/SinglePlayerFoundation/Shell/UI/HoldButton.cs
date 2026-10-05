using UnityEngine;
using UnityEngine.EventSystems;

namespace SPF.Shell.UI
{
    public sealed class HoldButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
    {
        public bool Held { get; private set; }
        bool m_Pressed;

        public void OnPointerDown(PointerEventData eventData) { Held = true; m_Pressed = true; }

        /// <summary>True once per press (even a tap shorter than a frame): jump buttons need the press and the hold.</summary>
        public bool ConsumePress()
        {
            bool pressed = m_Pressed;
            m_Pressed = false;
            return pressed;
        }
        public void OnPointerUp(PointerEventData eventData) => Held = false;
        public void OnPointerExit(PointerEventData eventData) => Held = false;
    }
}
