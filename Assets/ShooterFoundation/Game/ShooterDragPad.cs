using UnityEngine;
using UnityEngine.EventSystems;

namespace ShooterFoundation.Game
{
    /// <summary>One captured pointer; UI buttons consume their own events. Cancel never synthesizes movement or release.</summary>
    public sealed class ShooterDragPad : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler, IInitializePotentialDragHandler, ICancelHandler
    {
        public ShooterGameBootstrap Game;
        public bool Captured => m_Pointer != int.MinValue;
        int m_Pointer=int.MinValue;
        public void OnInitializePotentialDrag(PointerEventData e) => e.useDragThreshold=false;
        public void OnPointerDown(PointerEventData e) { if(!Captured && Game!=null && Game.State.Run.Flow==ShooterFlow.Playing)m_Pointer=e.pointerId; }
        public void OnDrag(PointerEventData e) { if(e.pointerId==m_Pointer)Game?.DragPixels(e.delta); }
        public void OnCancel(BaseEventData e) => CancelPointer();
        public void OnPointerUp(PointerEventData e)
        {
            if(e.pointerId!=m_Pointer)return;
            bool canceled=false;
#if ENABLE_LEGACY_INPUT_MANAGER
            // StandaloneInputModule maps TouchPhase.Canceled to pointer-up. ICancelHandler alone
            // does not cover that path. Inspect only the captured finger; another touch cannot cancel it.
            for(int i=0;i<UnityEngine.Input.touchCount;i++)
            {
                var touch=UnityEngine.Input.GetTouch(i);
                if(touch.fingerId!=e.pointerId)continue;
                canceled=touch.phase==TouchPhase.Canceled;break;
            }
#endif
            ReleasePointer(e.pointerId,canceled);
        }
        /// <summary>Shared by the native touch adapter and deterministic interruption tests.</summary>
        public void ReleasePointer(int pointerId,bool canceled)
        {
            if(pointerId!=m_Pointer)return;
            if(canceled)CancelPointer();else m_Pointer=int.MinValue;
        }
        public void CancelPointer() { m_Pointer=int.MinValue;Game?.State?.CancelInput(); }
        void OnDisable() => CancelPointer();
        void OnApplicationFocus(bool focused) { if(!focused)CancelPointer(); }
        void OnApplicationPause(bool paused) { if(paused)CancelPointer(); }
    }
}
