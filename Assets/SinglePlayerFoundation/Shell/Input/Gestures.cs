using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

namespace SPF.Shell.Input
{
    /// <summary>
    /// Turns raw pointers (touches, mouse) into tap / drag / pinch gestures for strategy and puzzle games
    /// (pan and zoom a map, tap a tile, swipe a piece). Pure logic fed with pointer events, so it is fully
    /// testable; <see cref="GestureInput"/> feeds it from Unity's input each frame.
    /// </summary>
    public sealed class GestureTracker
    {
        struct Pointer
        {
            public int Id;
            public float2 Start, Last, Position;
            public float StartTime;
            public bool Dragging;
        }

        readonly List<Pointer> m_Pointers = new List<Pointer>();
        readonly List<float2> m_Taps = new List<float2>();
        readonly List<(float2 from, float2 to)> m_Swipes = new List<(float2, float2)>();
        float m_LastSpread;

        /// <summary>Pixels a pointer may move and still count as a tap.</summary>
        public float TapSlop = 18f;
        public float TapMaxSeconds = 0.35f;
        /// <summary>Minimum distance for a quick single-finger stroke to count as a swipe.</summary>
        public float SwipeMinDistance = 40f;
        public float SwipeMaxSeconds = 0.5f;

        /// <summary>Single-pointer drag this frame (screen pixels); zero while pinching.</summary>
        public float2 Drag { get; private set; }
        /// <summary>Pinch zoom factor this frame (&gt; 1 = fingers apart); 1 without a pinch.</summary>
        public float Pinch { get; private set; } = 1f;
        public float2 PinchCentre { get; private set; }
        public IReadOnlyList<float2> Taps => m_Taps;
        public IReadOnlyList<(float2 from, float2 to)> Swipes => m_Swipes;
        public int Active => m_Pointers.Count;

        /// <summary>Call once per frame before feeding this frame's events: clears the per-frame outputs.</summary>
        public void BeginFrame()
        {
            Drag = float2.zero;
            Pinch = 1f;
            m_Taps.Clear();
            m_Swipes.Clear();
        }

        public void Down(int id, float2 position, float time)
        {
            m_Pointers.Add(new Pointer { Id = id, Start = position, Last = position, Position = position, StartTime = time });
            m_LastSpread = Spread();
        }

        public void Move(int id, float2 position)
        {
            int i = Find(id);
            if (i < 0) return;
            var p = m_Pointers[i];
            p.Position = position;
            if (math.distance(p.Position, p.Start) > TapSlop) p.Dragging = true;
            m_Pointers[i] = p;
            if (m_Pointers.Count == 1)
            {
                if (p.Dragging) Drag += position - p.Last;
            }
            else if (m_Pointers.Count >= 2)
            {
                float spread = Spread();
                if (m_LastSpread > 1f && spread > 1f) Pinch *= spread / m_LastSpread;
                m_LastSpread = spread;
                PinchCentre = (m_Pointers[0].Position + m_Pointers[1].Position) * 0.5f;
            }
            p.Last = position;
            m_Pointers[i] = p;
        }

        public void Up(int id, float2 position, float time)
        {
            int i = Find(id);
            if (i < 0) return;
            Move(id, position);
            var p = m_Pointers[i];
            bool alone = m_Pointers.Count == 1;
            float duration = time - p.StartTime;
            if (alone && !p.Dragging && duration <= TapMaxSeconds) m_Taps.Add(p.Start);
            else if (alone && duration <= SwipeMaxSeconds && math.distance(p.Start, position) >= SwipeMinDistance) m_Swipes.Add((p.Start, position));
            m_Pointers.RemoveAt(i);
            m_LastSpread = Spread();
        }

        int Find(int id)
        {
            for (int i = 0; i < m_Pointers.Count; i++) if (m_Pointers[i].Id == id) return i;
            return -1;
        }

        float Spread() => m_Pointers.Count >= 2 ? math.distance(m_Pointers[0].Position, m_Pointers[1].Position) : 0f;

        /// <summary>Main direction of a swipe as a grid step (±1 on one axis), for swap puzzles.</summary>
        public static int2 SwipeDirection(float2 from, float2 to)
        {
            float2 d = to - from;
            return math.abs(d.x) >= math.abs(d.y) ? new int2((int)math.sign(d.x), 0) : new int2(0, (int)math.sign(d.y));
        }
    }

    /// <summary>Feeds a <see cref="GestureTracker"/> from touches (or the mouse, wheel = pinch) every frame.</summary>
    [DefaultExecutionOrder(-50)]
    public sealed class GestureInput : MonoBehaviour
    {
        public readonly GestureTracker Tracker = new GestureTracker();
        /// <summary>Set while the pointer is over UI that should not pan the map (optional filter).</summary>
        public System.Func<float2, bool> Blocked;
        bool m_MouseDown;

        void Update()
        {
            Tracker.BeginFrame();
#if ENABLE_LEGACY_INPUT_MANAGER
            float time = Time.unscaledTime;
            if (UnityEngine.Input.touchCount > 0)
            {
                for (int i = 0; i < UnityEngine.Input.touchCount; i++)
                {
                    var t = UnityEngine.Input.GetTouch(i);
                    var p = (float2)(Vector2)t.position;
                    switch (t.phase)
                    {
                        case TouchPhase.Began: if (Blocked == null || !Blocked(p)) Tracker.Down(t.fingerId, p, time); break;
                        case TouchPhase.Moved: case TouchPhase.Stationary: Tracker.Move(t.fingerId, p); break;
                        default: Tracker.Up(t.fingerId, p, time); break;
                    }
                }
                return;
            }
            var mouse = (float2)(Vector2)UnityEngine.Input.mousePosition;
            if (UnityEngine.Input.GetMouseButtonDown(0) && (Blocked == null || !Blocked(mouse))) { Tracker.Down(-1, mouse, time); m_MouseDown = true; }
            else if (m_MouseDown && UnityEngine.Input.GetMouseButton(0)) Tracker.Move(-1, mouse);
            else if (m_MouseDown && UnityEngine.Input.GetMouseButtonUp(0)) { Tracker.Up(-1, mouse, time); m_MouseDown = false; }
#endif
        }
    }
}
