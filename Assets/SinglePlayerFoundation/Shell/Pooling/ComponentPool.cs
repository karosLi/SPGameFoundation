using System;
using SPF.Contracts.Pooling;
using UnityEngine;

namespace SPF.Shell.Pooling
{
    /// <summary>
    /// Pool of scene objects (labels, effect rigs, UI rows) built from a factory or a template. Released objects
    /// are deactivated and parked under one inactive root, so getting and releasing never instantiates or
    /// destroys during play. Prewarm while loading.
    /// </summary>
    public sealed class ComponentPool<T> : IDisposable where T : Component
    {
        readonly ObjectPool<T> m_Pool;
        readonly Transform m_Parked;
        readonly Transform m_Active;

        /// <param name="create">Makes one object (its parent is fixed up by the pool).</param>
        /// <param name="activeParent">Where objects live while in use (for UI: a canvas panel).</param>
        public ComponentPool(Func<Transform, T> create, Transform activeParent, int prewarm = 0, string name = null)
        {
            var parked = new GameObject((name ?? typeof(T).Name) + " Pool");
            parked.SetActive(false);
            if (activeParent != null) parked.transform.SetParent(activeParent, false);
            m_Parked = parked.transform;
            m_Active = activeParent;
            m_Pool = new ObjectPool<T>(
                () => create(m_Parked),
                onGet: c => { c.transform.SetParent(m_Active, false); c.gameObject.SetActive(true); },
                onRelease: c => { c.gameObject.SetActive(false); c.transform.SetParent(m_Parked, false); });
            if (prewarm > 0) m_Pool.Prewarm(prewarm);
        }

        /// <summary>Clones <paramref name="template"/> (which should be inactive or outside the active parent).</summary>
        public static ComponentPool<T> FromTemplate(T template, Transform activeParent, int prewarm = 0) =>
            new ComponentPool<T>(parent => UnityEngine.Object.Instantiate(template, parent, false), activeParent, prewarm, template.name);

        public int Created => m_Pool.Created;
        public int CountActive => m_Pool.CountActive;
        public int CountInactive => m_Pool.CountInactive;
        public int PeakActive => m_Pool.PeakActive;

        public T Get() => m_Pool.Get();
        public void Release(T item) => m_Pool.Release(item);

        public void Dispose()
        {
            if (m_Parked != null) UnityEngine.Object.Destroy(m_Parked.gameObject);
        }
    }
}
