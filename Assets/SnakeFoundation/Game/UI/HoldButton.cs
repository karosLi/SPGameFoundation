using UnityEngine;
using UnityEngine.EventSystems;

namespace SnakeFoundation.Game.UI
{
    public sealed class HoldButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
    {
        public bool Held { get; private set; }
        public void OnPointerDown(PointerEventData eventData) => Held = true;
        public void OnPointerUp(PointerEventData eventData) => Held = false;
        public void OnPointerExit(PointerEventData eventData) => Held = false;
    }
}
