using UnityEngine;
using UnityEngine.EventSystems;

namespace SnakeFoundation.Game.UI
{
    public sealed class TapButton : MonoBehaviour, IPointerDownHandler
    {
        bool m_Pending;
        public void OnPointerDown(PointerEventData eventData) => m_Pending = true;

        public bool Consume()
        {
            bool pending = m_Pending;
            m_Pending = false;
            return pending;
        }
    }
}
