using SPF.Runtime.Session;
using Unity.Mathematics;
using UnityEngine;

namespace SnakeFoundation.Presentation
{
    /// <summary>
    /// Orthographic follow camera: tracks the interpolated player head (or the attract-mode focus) and
    /// zooms out as the snake grows. Runs after the session synced and before the world renderer.
    /// </summary>
    [DefaultExecutionOrder(400)]
    [RequireComponent(typeof(Camera))]
    public sealed class SnakeCameraRig : MonoBehaviour
    {
        [SerializeField] SessionHost m_Host;
        [SerializeField] float m_BaseSize = 20f;
        [SerializeField] float m_SizePerRadius = 7f;
        [SerializeField] float m_MaxSize = 80f;
        [SerializeField] float m_Follow = 12f;
        [SerializeField] float m_Zoom = 2f;

        Camera m_Camera;
        float2 m_Position;
        float m_Size;
        bool m_Initialised;

        public SessionHost Host
        {
            get => m_Host;
            set => m_Host = value;
        }

        public Camera Camera => m_Camera != null ? m_Camera : (m_Camera = GetComponent<Camera>());

        /// <summary>World rectangle visible this frame (min.xy, max.zw).</summary>
        public float4 ViewRect { get; private set; }

        void Awake()
        {
            m_Camera = GetComponent<Camera>();
            m_Camera.orthographic = true;
            m_Camera.nearClipPlane = 1f;
            m_Camera.farClipPlane = 100f;
            m_Size = m_BaseSize;
        }

        void LateUpdate()
        {
            var session = m_Host != null ? m_Host.Session : null;
            if (session == null) return;
            var world = session.World;
            var game = world.Resource(SnakeKeys.Game);
            float alpha = session.InterpolationAlpha;

            float2 target = game.Focus;
            float targetSize = m_BaseSize * 1.6f;
            if (world.Registry.TryResolve(game.Player, out _, out int row))
            {
                target = math.lerp(world.Column(SnakeKeys.PrevHead)[row], world.Column(SnakeKeys.Head)[row], alpha);
                float radius = world.Column(SnakeKeys.Radius)[row];
                bool boosting = world.Column(SnakeKeys.Info)[row].Has(SnakeFlags.Boosting);
                targetSize = m_BaseSize + radius * m_SizePerRadius + (boosting ? 3f : 0f);
            }
            targetSize = math.min(targetSize, m_MaxSize);

            float dt = Time.unscaledDeltaTime;
            if (!m_Initialised || math.distancesq(target, m_Position) > 400f * 400f)
            {
                m_Position = target;
                m_Size = targetSize;
                m_Initialised = true;
            }
            else
            {
                m_Position = math.lerp(m_Position, target, 1f - math.exp(-m_Follow * dt));
                m_Size = math.lerp(m_Size, targetSize, 1f - math.exp(-m_Zoom * dt));
            }

            var cam = Camera;
            cam.orthographicSize = m_Size;
            transform.position = new Vector3(m_Position.x, m_Position.y, -50f);
            float halfH = m_Size, halfW = m_Size * cam.aspect;
            ViewRect = new float4(m_Position.x - halfW, m_Position.y - halfH, m_Position.x + halfW, m_Position.y + halfH);
        }

        /// <summary>Snap to the target next frame (restart, portal).</summary>
        public void Snap() => m_Initialised = false;
    }
}
