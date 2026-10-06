using NUnit.Framework;
using SPF.L1.Skeleton;
using Unity.Mathematics;

namespace SPF.Tests.EditMode
{
    public class BonePaletteMathTests
    {
        [TestCase(1f)]
        [TestCase(-1f)]
        public void AffineBoneMappingMatchesExistingCutoutFrameAndInverse(float facing)
        {
            var bone = new BoneWorld { Position = new float2(3f, -2f), Rotation = 0.83f };
            var matrix = Affine2D.FromBone(bone, facing);
            var point = new float2(0.7f, -0.4f);
            Assert.Less(math.distance(matrix.TransformPoint(point), bone.Transform(point, facing)), 1e-6f);
            Assert.IsTrue(matrix.TryInverse(out var inverse));
            Assert.Less(math.distance(inverse.TransformPoint(matrix.TransformPoint(point)), point), 1e-5f);
            var identity = Affine2D.Compose(inverse, matrix);
            Assert.Less(math.distance(identity.TransformPoint(point), point), 1e-5f);
        }

        [Test]
        public void BindInversePaletteMapsBindSpaceToPoseSpace()
        {
            var bind = Affine2D.FromBone(new BoneWorld { Position = new float2(1f, 0.5f), Rotation = 0.5f }, 1f);
            var posed = Affine2D.FromBone(new BoneWorld { Position = new float2(-2f, 3f), Rotation = 1.3f }, -1f);
            var local = new float2(0.8f, -0.2f);
            Assert.IsTrue(BonePaletteMath.TrySkinningTransform(bind, posed, out var palette));
            Assert.Less(math.distance(palette.TransformPoint(bind.TransformPoint(local)), posed.TransformPoint(local)), 1e-5f);
            var singular = new Affine2D();
            Assert.IsFalse(BonePaletteMath.TrySkinningTransform(singular, posed, out palette));
            Assert.AreEqual(local, palette.TransformPoint(local));
            var invalid = Affine2D.Identity;
            invalid.Row0.z = float.NaN;
            Assert.IsFalse(invalid.TryInverse(out _));
        }

        [TestCase(0f, 0, 1, 0f)]
        [TestCase(0.125f, 0, 1, 0.5f)]
        [TestCase(0.875f, 3, 0, 0.5f)]
        [TestCase(1f, 0, 1, 0f)]
        [TestCase(-0.125f, 3, 0, 0.5f)]
        public void LoopSamplingExcludesDuplicateEndpoint(float time, int first, int second, float blend)
        {
            BonePaletteMath.SampleFrames(time, 1f, 4, true, out int a, out int b, out float t);
            Assert.AreEqual(first, a);
            Assert.AreEqual(second, b);
            Assert.AreEqual(blend, t, 1e-6f);
        }

        [Test]
        public void OneShotSamplingIncludesEndpointsAndHandlesInvalidMetadata()
        {
            BonePaletteMath.SampleFrames(0.5f, 1f, 3, false, out int a, out int b, out float t);
            Assert.AreEqual(1, a); Assert.AreEqual(2, b); Assert.AreEqual(0f, t);
            BonePaletteMath.SampleFrames(2f, 1f, 3, false, out a, out b, out t);
            Assert.AreEqual(2, a); Assert.AreEqual(2, b); Assert.AreEqual(0f, t);
            BonePaletteMath.SampleFrames(-1f, 1f, 3, false, out a, out b, out t);
            Assert.AreEqual(0, a); Assert.AreEqual(1, b); Assert.AreEqual(0f, t);
            BonePaletteMath.SampleFrames(1f, 0f, 0, true, out a, out b, out t);
            Assert.AreEqual(0, a); Assert.AreEqual(0, b); Assert.AreEqual(0f, t);
            BonePaletteMath.SampleFrames(float.NaN, 1f, 4, true, out a, out b, out t);
            Assert.AreEqual(0, a); Assert.AreEqual(0, b); Assert.AreEqual(0f, t);
            BonePaletteMath.SampleFrames(1f, 1f, 1, true, out a, out b, out t);
            Assert.AreEqual(0, a); Assert.AreEqual(0, b); Assert.AreEqual(0f, t);
        }

        [Test]
        public void MatrixInterpolationIsExplicitlyVisualAndCanShrinkRotations()
        {
            var identity = Affine2D.Identity;
            var halfTurn = Affine2D.FromBone(new BoneWorld { Rotation = math.PI }, 1f);
            var halfway = BonePaletteMath.Lerp(identity, halfTurn, 0.5f);
            Assert.Less(math.length(halfway.TransformPoint(new float2(1f, 0f))), 1e-6f,
                "matrix blending is not rigid skeletal interpolation and cannot define gameplay hitboxes");
            Assert.AreEqual(identity.Row0, BonePaletteMath.Lerp(identity, halfTurn, -1f).Row0);
            Assert.AreEqual(halfTurn.Row1, BonePaletteMath.Lerp(identity, halfTurn, 2f).Row1);
        }
    }
}
