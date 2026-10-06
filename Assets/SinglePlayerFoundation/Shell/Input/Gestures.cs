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
            public bool Dragging, MultiTouch;
        }

        readonly List<Pointer> m_Pointers = new List<Pointer>();
        readonly List<float2> m_Taps = new List<float2>();
        readonly List<(float2 from, float2 to)> m_Swipes = new List<(float2, float2)>();
        readonly List<(float2 from, float2 to)> m_Releases = new List<(float2, float2)>();
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
        /// <summary>Single-pointer drags that ended this frame (aim-and-release controls: slingshots, pull shots).</summary>
        public IReadOnlyList<(float2 from, float2 to)> Releases => m_Releases;

        /// <summary>The first pointer still down: where it started and where it is now.</summary>
        public bool TryGetPrimary(out float2 start, out float2 position)
        {
            if (m_Pointers.Count == 0) { start = position = default; return false; }
            start = m_Pointers[0].Start;
            position = m_Pointers[0].Position;
            return true;
        }
        public int Active => m_Pointers.Count;

        /// <summary>Call once per frame before feeding this frame's events: clears the per-frame outputs.</summary>
        public void BeginFrame()
        {
            Drag = float2.zero;
            Pinch = 1f;
            m_Taps.Clear();
            m_Swipes.Clear();
            m_Releases.Clear();
        }

        public void Down(int id, float2 position, float time)
        {
            if (Find(id) >= 0) return;
            bool multiTouch = m_Pointers.Count > 0;
            if (multiTouch)
            {
                // Releasing the last finger of a pinch must not become a tap, swipe, or shot.
                for (int i = 0; i < m_Pointers.Count; i++)
                {
                    var p = m_Pointers[i];
                    p.MultiTouch = true;
                    m_Pointers[i] = p;
                }
                Drag = float2.zero;
            }
            m_Pointers.Add(new Pointer { Id = id, Start = position, Last = position, Position = position, StartTime = time, MultiTouch = multiTouch });
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
            bool alone = m_Pointers.Count == 1 && !p.MultiTouch;
            float duration = time - p.StartTime;
            if (alone && p.Dragging) m_Releases.Add((p.Start, position));
            if (alone && !p.Dragging && duration <= TapMaxSeconds) m_Taps.Add(p.Start);
            else if (alone && duration <= SwipeMaxSeconds && math.distance(p.Start, position) >= SwipeMinDistance) m_Swipes.Add((p.Start, position));
            m_Pointers.RemoveAt(i);
            m_LastSpread = Spread();
        }

        /// <summary>Forget a canceled pointer without producing a tap, swipe, or release.</summary>
        public void Cancel(int id)
        {
            int i = Find(id);
            if (i < 0) return;
            m_Pointers.RemoveAt(i);
            m_LastSpread = Spread();
        }

        /// <summary>Discard all input when the owning control is disabled or the app is interrupted.</summary>
        public void Reset()
        {
            m_Pointers.Clear();
            m_LastSpread = 0f;
            PinchCentre = float2.zero;
            BeginFrame();
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
        bool m_HadTouches;
        bool m_Paused, m_FocusLost;

        void Update()
        {
            Tracker.BeginFrame();
            if (!isActiveAndEnabled || m_Paused || m_FocusLost) return;
#if ENABLE_LEGACY_INPUT_MANAGER
            float time = Time.unscaledTime;
            if (UnityEngine.Input.touchCount > 0)
            {
                // Unity can emulate mouse input from touches. Do not retain an old mouse drag
                // while real touches take over, or complete it later as a phantom action.
                if (m_MouseDown) { Tracker.Cancel(-1); m_MouseDown = false; }
                m_HadTouches = true;
                for (int i = 0; i < UnityEngine.Input.touchCount; i++)
                    ProcessTouch(UnityEngine.Input.GetTouch(i), time);
                return;
            }
            if (m_HadTouches) { Tracker.Reset(); m_HadTouches = false; }
            var mouse = (float2)(Vector2)UnityEngine.Input.mousePosition;
            if (UnityEngine.Input.GetMouseButtonDown(0) && (Blocked == null || !Blocked(mouse))) { Tracker.Down(-1, mouse, time); m_MouseDown = true; }
            else if (m_MouseDown && UnityEngine.Input.GetMouseButton(0)) Tracker.Move(-1, mouse);
            else if (m_MouseDown)
            {
                if (UnityEngine.Input.GetMouseButtonUp(0)) Tracker.Up(-1, mouse, time);
                else Tracker.Cancel(-1); // A missed up (for example focus loss) is not a release.
                m_MouseDown = false;
            }
#endif
        }

        void ProcessTouch(Touch touch, float time)
        {
            var p = (float2)touch.position;
            switch (touch.phase)
            {
                case TouchPhase.Began: if (Blocked == null || !Blocked(p)) Tracker.Down(touch.fingerId, p, time); break;
                case TouchPhase.Moved: case TouchPhase.Stationary: Tracker.Move(touch.fingerId, p); break;
                case TouchPhase.Ended: Tracker.Up(touch.fingerId, p, time); break;
                case TouchPhase.Canceled: Tracker.Cancel(touch.fingerId); break;
            }
        }

        void ResetInput()
        {
            Tracker.Reset();
            m_MouseDown = false;
            m_HadTouches = false;
        }

        void OnDisable() => ResetInput();

        void OnApplicationPause(bool paused)
        {
            m_Paused = paused;
            if (paused) ResetInput();
        }

        void OnApplicationFocus(bool focused)
        {
            m_FocusLost = !focused;
            if (!focused) ResetInput();
        }
    }
}
