using System.IO;
using SPF.Runtime.Session;

namespace SPF.Runtime.Persistence
{
    /// <summary>
    /// A whole-session snapshot as a save slot (mid-level save, suspend on mobile): <c>store.Save(slot,
    /// new SessionSnapshotSave(session))</c> / <c>store.Load(...)</c>. Loading needs a session built from the
    /// same mode and seed; a snapshot that does not fit is rejected (Load returns false) and the session is
    /// restarted, never left half-restored.
    /// </summary>
    public sealed class SessionSnapshotSave : ISaveData
    {
        readonly SimSession m_Session;

        public SessionSnapshotSave(SimSession session) => m_Session = session;

        public int Version => 1;

        public void Write(BinaryWriter writer) => m_Session.WriteSnapshot(writer);

        public bool Read(BinaryReader reader, int version)
        {
            try
            {
                m_Session.ReadSnapshot(reader);
                return true;
            }
            catch (InvalidDataException) { return false; }
            catch (EndOfStreamException) { return false; }
        }
    }
}
