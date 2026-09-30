using SPF.Runtime.Session;
using UnityEngine;

namespace SPF.Samples.DriftSmoke
{
    /// <summary>Scene-view preview of the drift snapshot (editor gizmos only; real rendering arrives in M3).</summary>
    public sealed class DriftGizmoView : MonoBehaviour
    {
        [SerializeField] SessionHost m_Host;
        [SerializeField, Min(1)] int m_MaxDrawn = 2000;
        [SerializeField, Min(0.01f)] float m_Size = 0.5f;

        void OnDrawGizmos()
        {
            var session = m_Host != null ? m_Host.Session : null;
            if (session == null || !session.World.HasResource(DriftKeys.Snapshot))
                return;
            var snapshot = session.World.Resource(DriftKeys.Snapshot);
            var current = snapshot.Current;
            int count = Mathf.Min(current.Length, m_MaxDrawn);
            Gizmos.color = Color.cyan;
            var size = new Vector3(m_Size, m_Size, m_Size);
            for (int i = 0; i < count; i++)
                Gizmos.DrawCube(new Vector3(current[i].x, current[i].y, 0f), size);
        }
    }
}
