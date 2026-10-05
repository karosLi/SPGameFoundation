using Unity.Mathematics;
using UnityEngine;
using UnityEngine.UI;

namespace SPF.Shell.UI
{
    /// <summary>
    /// Pooled screen-space labels that follow a world position, rise and fade (damage numbers, pickups).
    /// Fixed pool, no allocation per spawn; when full, the oldest label is reused.
    /// </summary>
    public sealed class FloatingTextPool : MonoBehaviour
    {
        struct Item
        {
            public Text Label;
            public float2 World;
            public float Age;
            public Color Color;
        }

        public float Lifetime = 0.9f;
        public float RiseWorldUnits = 1.2f;
        public Camera Camera;

        Item[] m_Items;
        int m_Next;
        RectTransform m_Root;

        public int Active { get; private set; }

        public void Build(Transform canvas, int capacity = 48, int fontSize = 34)
        {
            m_Root = UIFactory.Panel(canvas, "FloatingText", Color.clear, Vector2.zero, Vector2.one, raycast: false);
            m_Items = new Item[capacity];
            for (int i = 0; i < capacity; i++)
            {
                var label = UIFactory.Label(m_Root, "Float" + i, "", fontSize, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
                var rect = label.rectTransform;
                rect.sizeDelta = new Vector2(240, 60);
                label.gameObject.SetActive(false);
                m_Items[i] = new Item { Label = label, Age = float.MaxValue };
            }
        }

        public void Spawn(float2 world, string text, Color color)
        {
            if (m_Items == null) return;
            ref var item = ref m_Items[m_Next];
            m_Next = (m_Next + 1) % m_Items.Length;
            item.World = world;
            item.Age = 0f;
            item.Color = color;
            item.Label.text = text;
            item.Label.color = color;
            item.Label.gameObject.SetActive(true);
        }

        void LateUpdate()
        {
            if (m_Items == null || Camera == null) return;
            float dt = Time.unscaledDeltaTime;
            int active = 0;
            var canvasRect = m_Root;
            for (int i = 0; i < m_Items.Length; i++)
            {
                ref var item = ref m_Items[i];
                if (item.Age >= Lifetime)
                {
                    if (item.Label.gameObject.activeSelf) item.Label.gameObject.SetActive(false);
                    continue;
                }
                item.Age += dt;
                active++;
                float t = math.saturate(item.Age / Lifetime);
                var screen = Camera.WorldToScreenPoint(new Vector3(item.World.x, item.World.y + RiseWorldUnits * t, 0f));
                var size = canvasRect.rect.size;
                item.Label.rectTransform.anchoredPosition = new Vector2(screen.x / Mathf.Max(Screen.width, 1) * size.x - size.x * 0.5f,
                    screen.y / Mathf.Max(Screen.height, 1) * size.y - size.y * 0.5f);
                var c = item.Color;
                c.a = 1f - t * t;
                item.Label.color = c;
            }
            Active = active;
        }
    }
}
