using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SPF.Shell.UI
{
    /// <summary>Builds the UGUI hierarchy in code (no prefabs), so the game runs from a single component.</summary>
    public static class UIFactory
    {
        static Font s_Font;

        public static Font Font
        {
            get
            {
                if (s_Font != null) return s_Font;
                s_Font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                if (s_Font == null) s_Font = Resources.GetBuiltinResource<Font>("Arial.ttf");
                return s_Font;
            }
        }

        public static Canvas CreateCanvas(Transform parent, string name = "GameUI")
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            go.AddComponent<GraphicRaycaster>();

            if (Object.FindObjectOfType<EventSystem>() == null)
            {
                var es = new GameObject("EventSystem");
                es.transform.SetParent(parent, false);
                es.AddComponent<EventSystem>();
                es.AddComponent<StandaloneInputModule>();
            }
            return canvas;
        }

        public static RectTransform Panel(Transform parent, string name, Color color, Vector2 anchorMin, Vector2 anchorMax, bool raycast = true)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            if (color.a > 0f)
            {
                var image = go.AddComponent<Image>();
                image.color = color;
                image.raycastTarget = raycast;
            }
            return rect;
        }

        public static Text Label(Transform parent, string name, string text, int size, TextAnchor anchor, Vector2 anchorMin, Vector2 anchorMax)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = new Vector2(16, 8);
            rect.offsetMax = new Vector2(-16, -8);
            var label = go.AddComponent<Text>();
            label.font = Font;
            label.text = text;
            label.fontSize = size;
            label.alignment = anchor;
            label.color = Color.white;
            label.raycastTarget = false;
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            return label;
        }

        public static Button Button(Transform parent, string name, string text, Vector2 anchoredPosition, Vector2 size, Color color, Vector2 anchor, int fontSize = 40)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = size;
            var image = go.AddComponent<Image>();
            image.color = color;
            var button = go.AddComponent<Button>();
            button.targetGraphic = image;
            Label(go.transform, "Label", text, fontSize, TextAnchor.MiddleCenter, Vector2.zero, Vector2.one);
            return button;
        }

        /// <summary>Horizontal fill bar (background + fill image); returns the fill to drive with <see cref="SetFill"/>.</summary>
        public static Image Bar(Transform parent, string name, Color background, Color fill, Vector2 anchorMin, Vector2 anchorMax)
        {
            var back = Panel(parent, name, background, anchorMin, anchorMax, raycast: false);
            var fillRect = Panel(back, "Fill", fill, Vector2.zero, Vector2.one, raycast: false);
            return fillRect.GetComponent<Image>();
        }

        /// <summary>Sets a bar's fill fraction (0..1) by moving the fill's right anchor (no sprite needed).</summary>
        public static void SetFill(Image fill, float fraction)
        {
            var rect = fill.rectTransform;
            rect.anchorMax = new Vector2(Mathf.Clamp01(fraction), 1f);
        }

        public static void SetText(Button button, string text)
        {
            var label = button.GetComponentInChildren<Text>();
            if (label != null) label.text = text;
        }
    }
}
