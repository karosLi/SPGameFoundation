using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;

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
