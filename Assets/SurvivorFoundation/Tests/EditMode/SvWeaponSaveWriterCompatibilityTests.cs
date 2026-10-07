#if SPF_DOTNET_HARNESS
using System;
using System.Security.Cryptography;
using NUnit.Framework;
using SPF.Runtime.Session;
namespace SurvivorFoundation.Tests
{
    // Frozen against independent 56a13de D assemblies BEFORE the canonical writer extraction.
    // Raw native ABI is not claimed by this .NET-only control. Native same-runtime replay is separate.
    public class SvWeaponSaveWriterCompatibilityTests
    {
        [Test]
        public void ExtractedWriterRetainsFrozenDDescriptorAndEnvelopeBytes()
        {
            var config = SvConfig.CreateWeaponCombatExample(); var mode = SvMode.Create(config, out var module);
            try
            {
                using var session = SimSession.Create(mode, 7); session.Start();
                session.World.Resource(SvKeys.Game).Send(SvCommandKind.Start); session.Step();
                for (int i = 0; i < 12; i++) session.Step();
                var descriptor = SvWeaponSave.Describe(session, "stage-e-byte-control");
                Assert.AreEqual("281f4b8404ec7ee17b98cae9165228863e61a518aa27616151b012a18123c6d9", descriptor.ContractFingerprint);
                Assert.AreEqual("d61c43abc0df7eeb14ee7b22585141b20c977c7ea7fd2f209d25c898ee1a7ba1", descriptor.SchemaFingerprint);
                Assert.AreEqual("bf8c8e30238757dc11b1ec0101592248b72fa428d1b914aa3a439971b8bbc105", descriptor.ContentFingerprint);
                Assert.AreEqual("a3cc9ca93c4acece0ce0c9ba02d87772cc626a2ac9504d0b06696b39bc658804", descriptor.VisualFingerprint);
                Assert.AreEqual("a90c507ca2b3324d9411c373c34a7cf38d362c1c3da7c0c44fefa89f35251216", descriptor.RawCompatibilityFingerprint);
                using var sha = SHA256.Create();
                var bytes = SvWeaponSave.Capture(session, "stage-e-byte-control");
                Assert.AreEqual("11663a75e3a0f4e5081c5fab175ffe1b053a7aa349ebc4ebdb9b69c629050b77", BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant());
            }
            finally { UnityEngine.Object.DestroyImmediate(mode); UnityEngine.Object.DestroyImmediate(module); UnityEngine.Object.DestroyImmediate(config); }
        }
    }
}
#endif
