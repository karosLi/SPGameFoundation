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
        float m_CurrentSize;
        bool m_Initialised;

        public float2 Target { get; set; }
        public float Size { get; set; } = 10f;
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
            if (!m_Initialised || math.distancesq(Target, m_Position) > SnapDistance * SnapDistance)
            {
                m_Position = Target;
                m_CurrentSize = Size;
                m_Initialised = true;
            }
            else
            {
                m_Position = math.lerp(m_Position, Target, 1f - math.exp(-Follow * dt));
                m_CurrentSize = math.lerp(m_CurrentSize, Size, 1f - math.exp(-Zoom * dt));
            }
            var cam = Camera;
            cam.orthographicSize = m_CurrentSize;
            transform.position = new Vector3(m_Position.x, m_Position.y, -50f);
            float halfH = m_CurrentSize, halfW = m_CurrentSize * cam.aspect;
            ViewRect = new float4(m_Position.x - halfW, m_Position.y - halfH, m_Position.x + halfW, m_Position.y + halfH);
        }

        /// <summary>Jump to the target next frame (level start, teleport).</summary>
        public void Snap() => m_Initialised = false;
    }
}
