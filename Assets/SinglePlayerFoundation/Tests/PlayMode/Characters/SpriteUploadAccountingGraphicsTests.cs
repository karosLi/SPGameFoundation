#if !SPF_DOTNET_HARNESS
using NUnit.Framework;
using SPF.Contracts;
using SPF.Contracts.Weapons;
using SPF.Presentation;
using SPF.Presentation.Animation;
using SPF.Presentation.Sprites;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace SPF.Characters.Tests.PlayMode
{
    /// <summary>Executes upload-accounting regressions in the graphics-enabled CI suite. The equivalent
    /// EditMode checks correctly skip under -nographics; that skip is not validation of either backend.</summary>
    public class SpriteUploadAccountingGraphicsTests
    {
        [Test]
        public void GpuUploadIncludesArgumentsAndResetsWhenReusedOrCleared()
        {
            if (RenderCapabilities.Detect() != RenderTier.GpuDriven)
                Assert.Ignore("GPU-driven rendering is not supported on this graphics device.");
            using var batch = new SpriteBatch(RenderTier.GpuDriven, null, BlendKind.Opaque, 16);
            var instances = batch.Instances;
            for (int i = 0; i < batch.Capacity; i++)
                instances[i] = PackedSprite.Pack(new float2(i, 0), new float2(1),
                    new float4(0, 0, 1, 1), 0, new float4(1));
            var bounds = new Bounds(Vector3.zero, new Vector3(100, 100, 100));
            batch.Count = 3;
            batch.Draw(bounds, dirty: false);
            Assert.AreEqual(3L * PackedSprite.Stride + GraphicsBuffer.IndirectDrawIndexedArgs.size, batch.BytesUploaded);
            batch.Draw(bounds, dirty: false);
            Assert.AreEqual(0L, batch.BytesUploaded);
            batch.Count = 2;
            batch.Draw(bounds, dirty: false);
            Assert.AreEqual(2L * PackedSprite.Stride + GraphicsBuffer.IndirectDrawIndexedArgs.size, batch.BytesUploaded);
            batch.Clear();
            batch.Draw(bounds);
            Assert.AreEqual(0L, batch.BytesUploaded);
        }

        [Test]
        public void DataTexturePaddingRemainsDistinctFromUsefulMixedActorPayload()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                Assert.Ignore("Actual shader resources/upload accounting needs a graphics device.");
            const int count = 193;
            using var presenter = new GameplayCharacterPresenter(RenderTier.DataTexture, count, includeWeapons: true);
            var bounds = new Bounds(Vector3.zero, new Vector3(100, 100, 10));
            presenter.Begin(.016f, 0);
            for (int i = 0; i < count; i++) Assert.IsTrue(presenter.Submit(Actor(i + 1, true)));
            presenter.Evaluate(); presenter.Draw(bounds);
            long padded = presenter.BytesUploaded;
            Assert.AreEqual(122880, padded);
            Assert.AreEqual(117344, presenter.PackedPayloadBytes);
            presenter.Begin(.016f, 0);
            for (int i = 0; i < count; i++) Assert.IsTrue(presenter.Submit(Actor(i + 1, i == 0)));
            presenter.Evaluate(); presenter.Draw(bounds);
            Assert.AreEqual(86624, presenter.PackedPayloadBytes);
            Assert.AreEqual(padded, presenter.BytesUploaded, "the same warmed final prefix texture is uploaded");
        }

        static GameplayCharacterInput Actor(int id, bool equipped)
        {
            float facing = id % 2 == 0 ? 1 : -1;
            var root = new float2((id - 1) % 16, (id - 1) / 16);
            var input = new GameplayCharacterInput { Handle = new EntityHandle(id, 1), Root = root, Ground = root,
                Depth = .5f, Facing = facing, Scale = .7f, Tint = new float4(1), Kind = id == 3 ? 0 : 1 };
            if (equipped) input.Weapon = new WeaponViewState { ContentId = 1003, VisualId = 1003,
                Family = WeaponActionFamily.Cast, Stage = WeaponStage.Idle, AimDirection = new float2(facing, 0),
                GripOffset = new float2(.45f, 1.3f), SecondaryGripOffset = new float2(.23f, 1.18f), MuzzleOffset = new float2(1.25f, 1.65f),
                ContactPhase = .4f, ReleasePhase = .4f, ActiveEndPhase = .55f, ActionPulse = (uint)id * 31, CueSequence = (uint)id * 31 + 1 };
            return input;
        }
    }
}
#endif
