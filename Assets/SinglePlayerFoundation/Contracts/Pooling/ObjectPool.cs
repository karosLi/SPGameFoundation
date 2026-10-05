using System;
using System.Collections.Generic;

namespace SPF.Contracts.Pooling
{
    /// <summary>
    /// Free-list pool for managed objects (UI labels, effect controllers, scratch buffers). Prewarm it during
    /// loading so gameplay never calls the factory: <see cref="Created"/> stops growing after warm-up, which
    /// tests can assert. Not thread-safe (main thread only, like every managed object in SPF).
    /// </summary>
    public sealed class ObjectPool<T> where T : class
    {
        readonly Func<T> m_Create;
        readonly Action<T> m_OnGet;
        readonly Action<T> m_OnRelease;
        readonly Stack<T> m_Free;
        readonly int m_MaxFree;

        /// <param name="maxFree">Released objects beyond this many are dropped (left to the GC) instead of kept.</param>
        public ObjectPool(Func<T> create, Action<T> onGet = null, Action<T> onRelease = null, int capacity = 16, int maxFree = 1024)
        {
            m_Create = create ?? throw new ArgumentNullException(nameof(create));
            m_OnGet = onGet;
            m_OnRelease = onRelease;
            m_Free = new Stack<T>(capacity);
            m_MaxFree = maxFree;
        }

        /// <summary>Objects ever made by the factory.</summary>
        public int Created { get; private set; }
        public int CountInactive => m_Free.Count;
        public int CountActive { get; private set; }
        public int PeakActive { get; private set; }

        public void Prewarm(int count)
        {
            for (int i = m_Free.Count + CountActive; i < count && m_Free.Count < m_MaxFree; i++)
            {
                var item = m_Create();
                Created++;
                m_OnRelease?.Invoke(item);
                m_Free.Push(item);
            }
        }

        public T Get()
        {
            T item;
            if (m_Free.Count > 0) item = m_Free.Pop();
            else { item = m_Create(); Created++; }
            CountActive++;
            if (CountActive > PeakActive) PeakActive = CountActive;
            m_OnGet?.Invoke(item);
            return item;
        }

        public void Release(T item)
        {
            if (item == null) return;
#if UNITY_EDITOR || DEVELOPMENT_BUILD || SPF_DOTNET_HARNESS
            if (m_Free.Count > 0 && m_Free.Contains(item)) throw new InvalidOperationException("Object released to its pool twice.");
#endif
            CountActive--;
            m_OnRelease?.Invoke(item);
            if (m_Free.Count < m_MaxFree) m_Free.Push(item);
        }

        /// <summary>Drops every inactive object (e.g. on leaving a level), optionally running a destructor.</summary>
        public void Clear(Action<T> destroy = null)
        {
            if (destroy != null) foreach (var item in m_Free) destroy(item);
            m_Free.Clear();
        }
    }

    /// <summary>
    /// Shared scratch lists: <c>using (ListPool&lt;int&gt;.Get(out var list)) { ... }</c>. The handle is a struct, so
    /// borrowing and returning allocates nothing once the pool has a list of the needed capacity.
    /// </summary>
    public static class ListPool<T>
    {
        static readonly Stack<List<T>> s_Free = new Stack<List<T>>();

        public readonly struct Handle : IDisposable
        {
            readonly List<T> m_List;
            internal Handle(List<T> list) => m_List = list;
            public void Dispose() => Release(m_List);
        }

        public static Handle Get(out List<T> list)
        {
            list = s_Free.Count > 0 ? s_Free.Pop() : new List<T>(32);
            return new Handle(list);
        }

        public static void Release(List<T> list)
        {
            if (list == null) return;
            list.Clear();
            if (s_Free.Count < 64) s_Free.Push(list);
        }
    }
}
