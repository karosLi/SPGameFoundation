using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SPF.Testing
{
    /// <summary>Drives UGUI like a finger would: pointer down / drag / up / click through the EventSystem.</summary>
    public static class UIDriver
    {
        static PointerEventData Pointer(GameObject target, Vector2 position) => new PointerEventData(EventSystem.current)
        {
            position = position,
            pressPosition = position,
            pointerPress = target,
            button = PointerEventData.InputButton.Left,
        };

        static Vector2 ScreenCenterOf(GameObject go)
        {
            var rect = go.transform as RectTransform;
            if (rect == null) return new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            var corners = new Vector3[4];
            rect.GetWorldCorners(corners);
            return (corners[0] + corners[2]) * 0.5f;
        }

        /// <summary>Actual overlay-canvas bounds, useful for safe-area and touch-target assertions.</summary>
        public static Rect ScreenRectOf(GameObject go)
        {
            var corners = new Vector3[4]; ((RectTransform)go.transform).GetWorldCorners(corners);
            return new Rect(corners[0].x, corners[0].y, corners[2].x - corners[0].x, corners[2].y - corners[0].y);
        }

        /// <summary>The object a real pointer at this control's center would hit first.</summary>
        public static GameObject FirstHitAtCenter(GameObject target)
        {
            var hits = new List<RaycastResult>();
            EventSystem.current.RaycastAll(Pointer(target, ScreenCenterOf(target)), hits);
            return hits.Count > 0 ? hits[0].gameObject : null;
        }

        /// <summary>Reads geometry actually submitted to a CanvasRenderer. Test-only allocation;
        /// the harness intentionally returns -1 because it cannot validate real UI mesh submission.</summary>
        public static int SubmittedVertexCount(Graphic graphic)
        {
#if SPF_DOTNET_HARNESS
            return -1;
#else
            var mesh = new Mesh();
            try { graphic.GetComponent<CanvasRenderer>().GetMesh(mesh); return mesh.vertexCount; }
            finally { Object.DestroyImmediate(mesh); }
#endif
        }

        public static void Click(GameObject target)
        {
            var p = Pointer(target, ScreenCenterOf(target));
            ExecuteEvents.Execute(target, p, ExecuteEvents.pointerDownHandler);
            ExecuteEvents.Execute(target, p, ExecuteEvents.pointerUpHandler);
            ExecuteEvents.Execute(target, p, ExecuteEvents.pointerClickHandler);
        }

        public static void Press(GameObject target) =>
            ExecuteEvents.Execute(target, Pointer(target, ScreenCenterOf(target)), ExecuteEvents.pointerDownHandler);

        public static void Release(GameObject target) =>
            ExecuteEvents.Execute(target, Pointer(target, ScreenCenterOf(target)), ExecuteEvents.pointerUpHandler);

        /// <summary>Touch-and-drag from the target's centre by <paramref name="delta"/> pixels over several frames, then hold.</summary>
        public static IEnumerator Drag(GameObject target, Vector2 delta, int frames = 6)
        {
            Vector2 start = ScreenCenterOf(target);
            var p = Pointer(target, start);
            ExecuteEvents.Execute(target, p, ExecuteEvents.pointerDownHandler);
            for (int i = 1; i <= frames; i++)
            {
                p.position = start + delta * i / frames;
                ExecuteEvents.Execute(target, p, ExecuteEvents.dragHandler);
                yield return null;
            }
        }

        public static IEnumerator WaitUntil(System.Func<bool> condition, float timeoutSeconds)
        {
            float end = Time.realtimeSinceStartup + timeoutSeconds;
            while (!condition() && Time.realtimeSinceStartup < end)
                yield return null;
        }

        public static IEnumerator WaitSeconds(float seconds)
        {
            float end = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < end)
                yield return null;
        }
    }
}
