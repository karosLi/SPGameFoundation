#if SPF_DOTNET_HARNESS
using System;
using System.Security.Cryptography;
using NUnit.Framework;
using SPF.Runtime.Session;
namespace BrawlerFoundation.Tests
{
    // Frozen against independent 56a13de D assemblies BEFORE the canonical writer extraction.
    // Raw native ABI is not claimed by this .NET-only control. Native same-runtime replay is separate.
    public class BwWeaponSaveWriterCompatibilityTests
    {
        [Test]
        public void ExtractedWriterRetainsFrozenDDescriptorAndEnvelopeBytes()
        {
            var mode = BwMode.CreateWeaponBelt(BwBeltConfig.Default, out var module);
            try
            {
                using var session = SimSession.Create(mode, 7); session.Start();
                session.World.Resource(BwKeys.Game).Send(BwCommandKind.Start); session.Step();
                for (int i = 0; i < 12; i++) session.Step();
                var descriptor = BwWeaponSave.Describe(session, "stage-e-byte-control");
                Assert.AreEqual("281f4b8404ec7ee17b98cae9165228863e61a518aa27616151b012a18123c6d9", descriptor.ContractFingerprint);
                Assert.AreEqual("9eedf58e5269947fd0fd0e65b9cb31ef618dd5d34e44640be41fd62d70af3f25", descriptor.SchemaFingerprint);
                Assert.AreEqual("f1bdc2869748056261a14553537dc8c1a9f08cc0d3359ed8b067db66ecb01460", descriptor.ContentFingerprint);
                Assert.AreEqual("61d857f34c456d75e610a5c634b30c2713e8fa62da78c2e7aacc18a3feef9040", descriptor.VisualFingerprint);
                Assert.AreEqual("1ffcd2fba4e2102b0008b8df84c93c779f97a2226cc515bd2f314724990f317b", descriptor.RawCompatibilityFingerprint);
                using var sha = SHA256.Create();
                var bytes = BwWeaponSave.Capture(session, "stage-e-byte-control");
                Assert.AreEqual("65e420610391875f34ed6fdeeb5d0df4204ad669aa804e816f5744dece759579", BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant());
            }
            finally { UnityEngine.Object.DestroyImmediate(mode); UnityEngine.Object.DestroyImmediate(module);  }
        }
    }
}
#endif
