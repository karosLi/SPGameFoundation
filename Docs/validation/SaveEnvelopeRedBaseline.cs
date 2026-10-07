using System.IO;
using NUnit.Framework;
using SPF.Contracts;
using SPF.Runtime.Composition;
using SPF.Runtime.Session;
using SPF.Runtime.World;
namespace SPF.Tests.EditMode
{
    public class SaveEnvelopeTests
    {
        static readonly TableKey Table = new TableKey("Envelope.Items");
        static readonly ColumnKey<int> A = new ColumnKey<int>(Table, "A");
        static readonly ColumnKey<int> B = new ColumnKey<int>(Table, "B");
        sealed class Module : IGameplayModule
        {
            readonly bool reverse;
            public Module(bool reverse = false) { this.reverse = reverse; }
            public string Id => "envelope.test";
            public void DeclareData(WorldLayout l)
            { var t = l.Table(Table, 4); if (reverse) t.Column(B).Column(A); else t.Column(A).Column(B); }
            public void RegisterSystems(SystemRegistry r) { }
        }
        static SimSession Create(bool reverse = false) => new SimSession(new[] { new Module(reverse) }, SessionSettings.Default, 7);
        [Test] public void EqualSizedColumnReorderMustRejectBeforeChangingLiveState()
        {
            using var a = Create(); using var b = Create(true);
            a.World.CreateEntity(Table, out int row); var ca = a.World.Column(A); var cb = a.World.Column(B); ca[row] = 17; cb[row] = 42;
            Assert.Throws<InvalidDataException>(() => b.RestoreSnapshot(a.CaptureSnapshot()));
        }
        [Test] public void TrailingBytesMustRejectBeforeChangingLiveState()
        {
            using var a = Create(); byte[] raw = a.CaptureSnapshot(); System.Array.Resize(ref raw, raw.Length + 1);
            Assert.Throws<InvalidDataException>(() => a.RestoreSnapshot(raw));
        }
    }
}
