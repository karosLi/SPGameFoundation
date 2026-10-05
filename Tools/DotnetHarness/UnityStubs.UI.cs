// UGUI / EventSystems surface for the harness (compile checks only).
using System;
using System.Collections.Generic;

namespace UnityEngine
{
    public enum RenderMode { ScreenSpaceOverlay, ScreenSpaceCamera, WorldSpace }
    public sealed class Canvas : Behaviour
    {
        public RenderMode renderMode { get; set; }
        public int sortingOrder { get; set; }
        public Camera worldCamera { get; set; }
        public float scaleFactor { get; set; } = 1f;
    }
    public struct UIVertex
    {
        public Vector3 position; public Vector3 normal; public Vector4 tangent; public Color32 color; public Vector4 uv0, uv1;
        public static UIVertex simpleVert => new UIVertex { color = new Color32(255, 255, 255, 255) };
    }
    public enum HorizontalWrapMode { Wrap, Overflow }
    public enum VerticalWrapMode { Truncate, Overflow }
}

namespace UnityEngine.Events
{
    public class UnityEvent
    {
        readonly List<Action> m_Listeners = new List<Action>();
        public void AddListener(Action a) => m_Listeners.Add(a);
        public void RemoveAllListeners() => m_Listeners.Clear();
        public void Invoke() { foreach (var l in m_Listeners.ToArray()) l(); }
    }
}

namespace UnityEngine.EventSystems
{
    public class UIBehaviour : MonoBehaviour
    {
        protected virtual void OnEnable() { }
        protected virtual void OnDisable() { }
    }
    public struct RaycastResult { public GameObject gameObject { get; set; } }
    public class EventSystem : UIBehaviour
    {
        public static EventSystem current { get; set; }
        public bool IsPointerOverGameObject() => false;
        public void RaycastAll(PointerEventData eventData, System.Collections.Generic.List<RaycastResult> raycastResults) { }
    }
    public class BaseInputModule : UIBehaviour { }
    public class StandaloneInputModule : BaseInputModule { }
    public class BaseEventData { public BaseEventData(EventSystem es) { } }
    public class PointerEventData : BaseEventData
    {
        public PointerEventData(EventSystem es) : base(es) { }
        public Vector2 position { get; set; }
        public Vector2 pressPosition { get; set; }
        public Vector2 delta { get; set; }
        public int pointerId { get; set; }
        public GameObject pointerPress { get; set; }
        public enum InputButton { Left, Right, Middle }
        public InputButton button { get; set; }
    }
    public interface IEventSystemHandler { }
    public interface IPointerDownHandler : IEventSystemHandler { void OnPointerDown(PointerEventData e); }
    public interface IPointerUpHandler : IEventSystemHandler { void OnPointerUp(PointerEventData e); }
    public interface IPointerExitHandler : IEventSystemHandler { void OnPointerExit(PointerEventData e); }
    public interface IPointerClickHandler : IEventSystemHandler { void OnPointerClick(PointerEventData e); }
    public interface IDragHandler : IEventSystemHandler { void OnDrag(PointerEventData e); }
    public interface IBeginDragHandler : IEventSystemHandler { void OnBeginDrag(PointerEventData e); }
    public interface IEndDragHandler : IEventSystemHandler { void OnEndDrag(PointerEventData e); }
    public static class ExecuteEvents
    {
        public delegate void EventFunction<T>(T handler, BaseEventData data);
        public static EventFunction<IPointerDownHandler> pointerDownHandler => (h, d) => h.OnPointerDown((PointerEventData)d);
        public static EventFunction<IPointerUpHandler> pointerUpHandler => (h, d) => h.OnPointerUp((PointerEventData)d);
        public static EventFunction<IPointerClickHandler> pointerClickHandler => (h, d) => h.OnPointerClick((PointerEventData)d);
        public static EventFunction<IDragHandler> dragHandler => (h, d) => h.OnDrag((PointerEventData)d);
        public static bool Execute<T>(GameObject target, BaseEventData data, EventFunction<T> f) where T : IEventSystemHandler => false;
    }
}

namespace UnityEngine.UI
{
    public class VertexHelper
    {
        public readonly System.Collections.Generic.List<UIVertex> Verts = new System.Collections.Generic.List<UIVertex>();
        public int Triangles;
        public int currentVertCount => Verts.Count;
        public int currentIndexCount => Triangles * 3;
        public void Clear() { Verts.Clear(); Triangles = 0; }
        public void AddVert(UIVertex v) => Verts.Add(v);
        public void AddTriangle(int a, int b, int c) => Triangles++;
        public void PopulateUIVertex(ref UIVertex v, int i) => v = Verts[i];
    }
    public class Graphic : EventSystems.UIBehaviour
    {
        public Rect StubRect = new Rect(0, 0, 400, 200);
        public virtual Texture mainTexture => null;
        public Canvas canvas => null;
        public virtual void SetVerticesDirty() { }
        public virtual void SetAllDirty() { }
        public Rect GetPixelAdjustedRect() => rectTransform != null ? rectTransform.rect : StubRect;
        protected virtual void OnPopulateMesh(VertexHelper vh) { }
        public void StubPopulate(VertexHelper vh) => OnPopulateMesh(vh);
        public Color color { get; set; } = Color.white;
        public bool raycastTarget { get; set; } = true;
        public RectTransform rectTransform => transform as RectTransform;
    }
    public class MaskableGraphic : Graphic { }
    public class Image : MaskableGraphic { public Sprite sprite { get; set; } }
    public class RawImage : MaskableGraphic { public Texture texture { get; set; } public Rect uvRect { get; set; } }
    public class Sprite : Object { }
    public class Text : MaskableGraphic
    {
        public Font font { get; set; }
        public string text { get; set; } = "";
        public int fontSize { get; set; }
        public TextAnchor alignment { get; set; }
        public FontStyle fontStyle { get; set; }
        public HorizontalWrapMode horizontalOverflow { get; set; }
        public VerticalWrapMode verticalOverflow { get; set; }
    }
    public class Selectable : EventSystems.UIBehaviour { public Graphic targetGraphic { get; set; } public bool interactable { get; set; } = true; }
    public class Button : Selectable, EventSystems.IPointerClickHandler
    {
        public class ButtonClickedEvent : Events.UnityEvent { }
        public ButtonClickedEvent onClick { get; } = new ButtonClickedEvent();
        public void OnPointerClick(EventSystems.PointerEventData e) => onClick.Invoke();
    }
    public class CanvasScaler : EventSystems.UIBehaviour
    {
        public enum ScaleMode { ConstantPixelSize, ScaleWithScreenSize, ConstantPhysicalSize }
        public ScaleMode uiScaleMode { get; set; }
        public Vector2 referenceResolution { get; set; }
        public float matchWidthOrHeight { get; set; }
    }
    public class GraphicRaycaster : EventSystems.UIBehaviour { }
}
