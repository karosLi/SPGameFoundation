namespace SPF.Presentation
{
    /// <summary>Per-view, nonpersistent ownership stamp. Numeric handles and ticks may coincide in
    /// different Sessions; restore and level changes invalidate transient caches without changing
    /// entity identity or authoritative snapshot data. The owner is borrowed, never disposed here.</summary>
    public struct ViewTimelineStamp
    {
        object m_Owner;
        uint m_Revision;
        int m_LevelVersion;
        public bool Matches(object owner, uint revision, int levelVersion) =>
            ReferenceEquals(m_Owner, owner) && m_Revision == revision && m_LevelVersion == levelVersion;
        public bool Update(object owner, uint revision, int levelVersion)
        {
            bool changed = !Matches(owner, revision, levelVersion);
            m_Owner = owner; m_Revision = revision; m_LevelVersion = levelVersion;
            return changed;
        }
        public void Reset() => this = default;
    }
}
