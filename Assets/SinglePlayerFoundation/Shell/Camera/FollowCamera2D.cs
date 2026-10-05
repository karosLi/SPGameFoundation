using Unity.Mathematics;
using UnityEngine;

namespace SPF.Shell.CameraRig
{
    /// <summary>
    /// Orthographic 2D follow camera: smoothly tracks <see cref="Target"/> (set it every frame, e.g. the
    /// interpolated player position) at <see cref="Size"/> half-height. Runs after the session synced and
    /// before world renderers (execution order 400), and publishes the visible world rectangle.
    /// </summary>
    [DefaultExecutionOrder(400)]
    [RequireComponent(typeof(Camera))]
    public sealed class FollowCamera2D : MonoBehaviour
    {
        public float Follow = 10f;
        public float Zoom = 3f;
        public float SnapDistance = 50f;

        /// <summary>Called each frame before moving to update <see cref="Target"/> / <see cref="Size"/>.</summary>
        public System.Action<FollowCamera2D> UpdateTarget;

        Camera m_Camera;
        float2 m_Position;
        float m_ShakeAmplitude, m_ShakeTime, m_ShakeDuration;
        float m_CurrentSize;
        bool m_Initialised;

        public float2 Target { get; set; }
        public float Size { get; set; } = 10f;

        /// <summary>Half extents of a box around the view centre the target can move in without the camera following (platformers).</summary>
        public float2 DeadZone { get; set; }

        /// <summary>World rectangle (min.xy, max.zw) the view stays inside (level edges); zero = unbounded.</summary>
        public float4 Bounds { get; set; }
        public Camera Camera => m_Camera != null ? m_Camera : (m_Camera = GetComponent<Camera>());

        /// <summary>World rectangle visible this frame (min.xy, max.zw).</summary>
        public float4 ViewRect { get; private set; }

        void Awake()
        {
            m_Camera = GetComponent<Camera>();
            m_Camera.orthographic = true;
            m_Camera.nearClipPlane = 1f;
            m_Camera.farClipPlane = 100f;
        }

        void LateUpdate()
        {
            UpdateTarget?.Invoke(this);
            float dt = Time.unscaledDeltaTime;
            var cam = Camera;
            if (!m_Initialised || math.distancesq(Target, m_Position) > SnapDistance * SnapDistance)
            {
                m_Position = Target;
                m_CurrentSize = Size;
                m_Initialised = true;
            }
            else
            {
                m_Position = math.lerp(m_Position, Goal(m_Position, Target, DeadZone), 1f - math.exp(-Follow * dt));
                m_CurrentSize = math.lerp(m_CurrentSize, Size, 1f - math.exp(-Zoom * dt));
            }
            m_Position = Clamp(m_Position, new float2(m_CurrentSize * cam.aspect, m_CurrentSize), Bounds);
            cam.orthographicSize = m_CurrentSize;
            float2 shake = float2.zero;
            if (m_ShakeTime < m_ShakeDuration)
            {
                m_ShakeTime += dt;
                float k = m_ShakeAmplitude * (1f - m_ShakeTime / m_ShakeDuration);
                float t = Time.unscaledTime * 55f;
                shake = new float2(math.sin(t), math.cos(t * 1.37f)) * k;
            }
            transform.position = new Vector3(m_Position.x + shake.x, m_Position.y + shake.y, -50f);
            float halfH = m_CurrentSize, halfW = m_CurrentSize * cam.aspect;
            ViewRect = new float4(m_Position.x - halfW, m_Position.y - halfH, m_Position.x + halfW, m_Position.y + halfH);
        }

        /// <summary>Where the view centre should go: unchanged while the target is inside the dead zone, otherwise just far enough.</summary>
        public static float2 Goal(float2 view, float2 target, float2 deadZone)
        {
            float2 offset = target - view;
            return view + offset - math.clamp(offset, -deadZone, deadZone);
        }

        /// <summary>Keeps a view of the given half extents inside <paramref name="bounds"/> (centred when the bounds are smaller).</summary>
        public static float2 Clamp(float2 view, float2 halfExtents, float4 bounds)
        {
            if (math.all(bounds == 0f)) return view;
            float2 min = bounds.xy + halfExtents, max = bounds.zw - halfExtents;
            return math.select(math.clamp(view, min, max), (bounds.xy + bounds.zw) * 0.5f, min > max);
        }

        /// <summary>Screen shake (impacts): decays linearly over <paramref name="duration"/>; the strongest request wins.</summary>
        public void Shake(float amplitude, float duration)
        {
            float remaining = m_ShakeDuration > 0f ? m_ShakeAmplitude * (1f - m_ShakeTime / m_ShakeDuration) : 0f;
            if (amplitude < remaining) return;
            m_ShakeAmplitude = amplitude;
            m_ShakeDuration = math.max(duration, 0.01f);
            m_ShakeTime = 0f;
        }

        /// <summary>Jump to the target next frame (level start, teleport).</summary>
        public void Snap() => m_Initialised = false;
    }
}
