using Unity.Mathematics;
using UnityEngine;

namespace SPF.Presentation.Combat
{
    /// <summary>Bounded presentation-only reservations. Rectangles are world-space except the four
    /// viewport fields supplied by the HUD adapter. No camera, input or combat state is changed.</summary>
    public sealed class DamageNumberLayout
    {
        public const int ActorCapacity = 256;
        readonly float4[] m_Actors = new float4[ActorCapacity];
        float4 m_View, m_Safe, m_Header, m_Menu, m_Status;
        public float4 SafeViewport = new float4(0, 0, 1, 1);
        public float4 HeaderViewport, MenuViewport, StatusViewport;
        public int Actors { get; private set; }
        public int ReservationOverflows { get; private set; }
        public void Begin(float4 view)
        {
            m_View = view; Actors = ReservationOverflows = 0;
            m_Safe = Project(SafeViewport); m_Header = Project(HeaderViewport); m_Menu = Project(MenuViewport); m_Status = Project(StatusViewport);
        }
        /// <summary>Read the rendered orthographic view, including the existing impact shake.
        /// Follow targets/culling rectangles may deliberately omit that translation.</summary>
        public static float4 RenderedView(Camera camera, float4 fallback)
        {
            if (camera == null || !camera.orthographic) return fallback;
            var p = camera.transform.position; float2 half = new float2(camera.orthographicSize * camera.aspect, camera.orthographicSize);
            return new float4(new float2(p.x, p.y) - half, new float2(p.x, p.y) + half);
        }
        float4 Project(float4 normalized) => new float4(m_View.xy + normalized.xy * (m_View.zw - m_View.xy), m_View.xy + normalized.zw * (m_View.zw - m_View.xy));
        public void ReserveActor(float4 rectangle)
        {
            if (!math.all(math.isfinite(rectangle)) || rectangle.z <= rectangle.x || rectangle.w <= rectangle.y || !Overlaps(rectangle, m_View)) return;
            if (Actors < ActorCapacity) m_Actors[Actors++] = rectangle;
            else
            {
                // Conservatively coalesce overflow into the last slot instead of leaving later heads
                // unprotected. It can suppress extra labels; the exact count remains observable.
                var last = m_Actors[ActorCapacity - 1];
                m_Actors[ActorCapacity - 1] = new float4(math.min(last.xy, rectangle.xy), math.max(last.zw, rectangle.zw));
                ReservationOverflows++;
            }
        }
        public float ActorLift(float4 rectangle)
        {
            float lift = 0;
            for (int i = 0; i < Actors; i++)
                if (Overlaps(rectangle, m_Actors[i])) lift = math.max(lift, m_Actors[i].w - rectangle.y);
            return lift;
        }
        public bool Allows(float4 rectangle) => rectangle.x >= m_Safe.x && rectangle.y >= m_Safe.y && rectangle.z <= m_Safe.z && rectangle.w <= m_Safe.w &&
            !Overlaps(rectangle, m_Header) && !Overlaps(rectangle, m_Menu) && !Overlaps(rectangle, m_Status);
        public static bool Overlaps(float4 a, float4 b) => b.z > b.x && b.w > b.y && a.x < b.z && a.z > b.x && a.y < b.w && a.w > b.y;
        /// <summary>Only opted-in damage views hide healthy ordinary bars; elite and damaged bars remain.</summary>
        public static bool ShowHealthBar(float hp, float maximum, bool important = false) => important || hp < maximum;
    }
}
