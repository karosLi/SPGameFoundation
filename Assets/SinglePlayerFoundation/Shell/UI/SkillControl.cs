using SPF.Contracts;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.EventSystems;

namespace SPF.Shell.UI
{
    /// <summary>Owns a gesture, not a skill clock. Tap/hold activate on down; aimed skills activate on
    /// owner release. Drag outside CancelRadius to cancel an aimed cast. Other fingers cannot steal it.</summary>
    public sealed class SkillControl : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IDragHandler, ICancelHandler
    {
        public float AimRadius = 80f, CancelRadius = 240f;
        public bool Pressed { get; private set; }
        public bool AimingCanceled { get; private set; }
        public float2 Aim { get; private set; }
        public SkillSlotSnapshot Snapshot { get; private set; }
        bool m_Pending, m_Paused, m_FocusLost;
        float2 m_ReleaseAim;
        public System.Action Canceled;
        int m_Pointer;
        Vector2 m_Origin;

        public void SetSnapshot(in SkillSlotSnapshot snapshot)
        {
            if (Snapshot.Definition.Id != 0 && Snapshot.Definition.Id != snapshot.Definition.Id) CancelInput();
            Snapshot = snapshot;
        }

        public void OnPointerDown(PointerEventData e)
        {
            if (!isActiveAndEnabled || m_Paused || m_FocusLost || Pressed || !Snapshot.Ready) return;
            Pressed = true; m_Pointer = e.pointerId; m_Origin = e.position; Aim = float2.zero; AimingCanceled = false;
            if (Snapshot.Definition.Activation != SkillActivation.AimRelease) m_Pending = true;
        }

        public void OnDrag(PointerEventData e)
        {
            if (!Pressed || e.pointerId != m_Pointer || Snapshot.Definition.Activation != SkillActivation.AimRelease) return;
            var delta = e.position - m_Origin;
            AimingCanceled = delta.sqrMagnitude > CancelRadius * CancelRadius;
            Aim = delta.sqrMagnitude > AimRadius * AimRadius * .015625f ? math.normalize(new float2(delta.x, delta.y)) : float2.zero;
        }

        public void OnPointerUp(PointerEventData e)
        {
            if (!Pressed || e.pointerId != m_Pointer) return;
#if ENABLE_LEGACY_INPUT_MANAGER
            for (int i = 0; i < UnityEngine.Input.touchCount; i++)
            {
                var touch = UnityEngine.Input.GetTouch(i);
                if (touch.fingerId == m_Pointer && touch.phase == TouchPhase.Canceled) { CancelInput(); return; }
            }
#endif
            OnDrag(e);
            if (Snapshot.Definition.Activation == SkillActivation.AimRelease && AimingCanceled) { CancelInput(); return; }
            if (Snapshot.Definition.Activation == SkillActivation.AimRelease)
            { m_Pending = true; m_ReleaseAim = Aim; }
            Pressed = false; AimingCanceled = false; Aim = float2.zero;
        }

        public void Read(ref InputFrame frame, int slot)
        {
            if (Pressed && Snapshot.Definition.Activation == SkillActivation.Hold) frame.Held |= 1u << slot;
            if (!m_Pending) return;
            frame.Pressed |= 1u << slot;
            if (Snapshot.Definition.Activation == SkillActivation.AimRelease) frame.Aim = m_ReleaseAim;
            m_Pending = false; m_ReleaseAim = float2.zero;
        }

        public void CancelInput()
        { Pressed = false; AimingCanceled = false; Aim = m_ReleaseAim = float2.zero; m_Pending = false; Canceled?.Invoke(); }
        public void OnCancel(BaseEventData e) => CancelInput();
        void OnDisable() => CancelInput();
        void OnApplicationPause(bool paused) { m_Paused = paused; if (paused) CancelInput(); }
        void OnApplicationFocus(bool focused) { m_FocusLost = !focused; if (!focused) CancelInput(); }
    }
}
