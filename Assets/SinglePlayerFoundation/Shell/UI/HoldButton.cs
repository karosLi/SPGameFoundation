using UnityEngine;
using UnityEngine.EventSystems;

namespace SPF.Shell.UI
{
    public sealed class HoldButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
    {
        public bool Held { get; private set; }
        bool m_Pressed;
        int m_PointerId;
        bool m_Paused, m_FocusLost;

        public void OnPointerDown(PointerEventData eventData)
        {
            if (!isActiveAndEnabled || m_Paused || m_FocusLost || Held) return;
            m_PointerId = eventData.pointerId;
            Held = true;
            m_Pressed = true;
        }

        /// <summary>True once per press (even a tap shorter than a frame): jump buttons need the press and the hold.</summary>
        public bool ConsumePress()
        {
            bool pressed = m_Pressed;
            m_Pressed = false;
            return pressed;
        }
        public void OnPointerUp(PointerEventData eventData)
        {
            if (Held && eventData.pointerId == m_PointerId) Held = false;
        }

        public void OnPointerExit(PointerEventData eventData) => OnPointerUp(eventData);

        void ResetInput()
        {
            Held = false;
            m_Pressed = false;
        }

        void OnDisable() => ResetInput();

        void OnApplicationPause(bool paused)
        {
            m_Paused = paused;
            if (paused) ResetInput();
        }

        void OnApplicationFocus(bool focused)
        {
            m_FocusLost = !focused;
            if (!focused) ResetInput();
        }
    }
}
