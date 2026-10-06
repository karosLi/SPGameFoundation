using System;
using System.Runtime.InteropServices;
using NUnit.Framework;
using SPF.L1.Skeleton;
using SPF.Presentation.Characters;
using Unity.Collections;
using Unity.Mathematics;

namespace SPF.Tests.EditMode
{
    /// <summary>CPU contract tests: no graphics device, shader execution, or Unity object APIs required.</summary>
    public class BatCharacterTests
    {
        const float PoseTolerance = 2e-5f;

        [Test]
        public void ShaderAbiHasExactStridesAndFieldOffsets()
        {
            Assert.AreEqual(64, BatInstance.Stride);
            Assert.AreEqual(BatInstance.Stride, Marshal.SizeOf(typeof(BatInstance)));
            Assert.AreEqual(0, Marshal.OffsetOf(typeof(BatInstance), nameof(BatInstance.Placement)).ToInt32());
            Assert.AreEqual(16, Marshal.OffsetOf(typeof(BatInstance), nameof(BatInstance.Frames)).ToInt32());
            Assert.AreEqual(32, Marshal.OffsetOf(typeof(BatInstance), nameof(BatInstance.Tint)).ToInt32());
            Assert.AreEqual(48, Marshal.OffsetOf(typeof(BatInstance), nameof(BatInstance.Ik)).ToInt32());
            Assert.AreEqual(32, BatRows.Stride);
            Assert.AreEqual(BatRows.Stride, Marshal.SizeOf(typeof(BatRows)));
            Assert.AreEqual(0, Marshal.OffsetOf(typeof(BatRows), nameof(BatRows.Row0)).ToInt32());
            Assert.AreEqual(16, Marshal.OffsetOf(typeof(BatRows), nameof(BatRows.Row1)).ToInt32());
        }

        [Test]
        public void AuthoredMeshContainsNormalizedTwoBoneAndExactHalfWeightVertices()
        {
            BatCharacterAsset.CreateMesh(out var vertices, out var indices);
            Assert.LessOrEqual(vertices.Length, BatLimits.MaxVertices);
            Assert.LessOrEqual(indices.Length, BatLimits.MaxIndices);
            Assert.AreEqual(0, indices.Length % 3);
            int blended = 0, halfWeighted = 0, rootWeighted = 0;
            foreach (var vertex in vertices)
            {
                Assert.AreEqual(1f, vertex.Skin.z + vertex.Skin.w, 1e-6f);
                Assert.GreaterOrEqual(vertex.Skin.z, 0f);
                Assert.GreaterOrEqual(vertex.Skin.w, 0f);
                if (vertex.Skin.z > 0 && vertex.Skin.w > 0)
                {
                    blended++;
                    Assert.AreEqual(1f, vertex.Skin.x);
                    Assert.AreEqual(2f, vertex.Skin.y);
                }
                if (vertex.Skin.z == .5f && vertex.Skin.w == .5f) halfWeighted++;
                if (vertex.Skin.x == 0 && vertex.Skin.z == 1) rootWeighted++;
            }
            Assert.Greater(blended, 2);
            Assert.AreEqual(2, halfWeighted, "Both sides of the elbow ring must really blend two bones.");
            Assert.Greater(rootWeighted, 0);
            foreach (int index in indices)
            {
                Assert.GreaterOrEqual(index, 0);
                Assert.Less(index, vertices.Length);
            }
        }

        [Test]
        public void BakedFramesMatchSourceSkeletalPoseWithInverseBindAndWeightedSkinning()
        {
            using var rig = BatCharacterAsset.CreateRig();
            BatCharacterAsset.CreateMesh(out var vertices, out var indices);
            using var baked = BatBaker.Bake(rig.View, vertices, indices, 8);
            Assert.AreEqual(16, baked.FrameCount);
            Assert.AreEqual(2, baked.ClipCount);
            Assert.AreEqual((long)baked.FrameCount * BatLimits.Bones * BatRows.Stride * 2, baked.CpuPaletteBytes);
            AssertBakedMatchesSource(rig.View, baked);
        }

        [Test]
        public void BakerReadsChangedSourceKeysDurationsAndNonLoopingMetadata()
        {
            using var rig = BatCharacterAsset.CreateRig();
            var view = rig.View;
            var second = rig.Clips[1];
            second.Loop = false;
            second.Duration = 1.75f;
            rig.Clips[1] = second;
            for (int bone = 1; bone < BatLimits.Bones; bone++)
            {
                int2 channel = rig.Channels[BatLimits.Bones + bone];
                for (int k = 0; k < channel.y; k++)
                {
                    int at = channel.x + k;
                    var key = rig.Keys[at];
                    key.Time = k == channel.y - 1 ? second.Duration : 0;
                    key.Rotation += .137f * (bone + k);
                    rig.Keys[at] = key;
                }
            }
            BatCharacterAsset.CreateMesh(out var vertices, out var indices);
            using var baked = BatBaker.Bake(view, vertices, indices, 5);
            Assert.IsFalse(baked.Clip(1).Loop);
            Assert.AreEqual(second.Duration, baked.Clip(1).Duration);
            AssertBakedMatchesSource(view, baked);
            AssertFrames(baked.Instance(1, -20, 0), 5, 6, 0);
            AssertFrames(baked.Instance(1, second.Duration * .5f, 0), 7, 8, 0);
            AssertFrames(baked.Instance(1, second.Duration, 0), 9, 9, 0);
            AssertFrames(baked.Instance(1, 20, 0), 9, 9, 0);
            Assert.Greater(math.distance(baked.Row(5, 1, BatPrecision.Float).Row0,
                baked.Row(9, 1, BatPrecision.Float).Row0), .1f, "The non-loop endpoint must contain the changed source pose.");
        }

        [TestCase(0f, 0, 1, 0f)]
        [TestCase(.125f, 0, 1, .5f)]
        [TestCase(.875f, 3, 0, .5f)]
        [TestCase(-.125f, 3, 0, .5f)]
        [TestCase(-1.125f, 3, 0, .5f)]
        [TestCase(1f, 0, 1, 0f)]
        public void LoopFramesAndWeightedSeamStayInsideTheSelectedClip(float time, int a, int b, float blend)
        {
            using var baked = BatCharacterAsset.Bake(4);
            for (int clip = 0; clip < baked.ClipCount; clip++)
            {
                int first = baked.Clip(clip).FirstFrame;
                var instance = baked.Instance(clip, time, new float2(2, -3), 1.5f, -1, new float4(1), .25f);
                AssertFrames(instance, first + a, first + b, blend);
                for (int v = 0; v < baked.VertexCount; v++)
                {
                    var vertex = baked.Vertex(v);
                    float2 firstPoint = WeightedFrame(baked, first + a, vertex, BatPrecision.Float);
                    float2 nextPoint = WeightedFrame(baked, first + b, vertex, BatPrecision.Float);
                    float2 posed = math.lerp(firstPoint, nextPoint, blend);
                    var expected = new float3(new float2(2 - posed.x * 1.5f, -3 + posed.y * 1.5f), .25f);
                    Assert.Less(math.distance(expected, baked.Skin(v, instance, BatPrecision.Float)), PoseTolerance);
                }
            }
        }

        [Test]
        public void OneFrameLoopAndOneShotAlwaysUseTheirOwnSingleSourceSample()
        {
            using var rig = BatCharacterAsset.CreateRig();
            var clip = rig.Clips[1];
            clip.Loop = false;
            rig.Clips[1] = clip;
            BatCharacterAsset.CreateMesh(out var vertices, out var indices);
            using var baked = BatBaker.Bake(rig.View, vertices, indices, 1);
            AssertBakedMatchesSource(rig.View, baked);
            foreach (float time in new[] { -100f, -.1f, 0f, .5f, 1f, 100f })
            for (int c = 0; c < 2; c++)
            {
                var instance = baked.Instance(c, time, 0);
                AssertFrames(instance, c, c, 0);
                for (int v = 0; v < baked.VertexCount; v++)
                    Assert.AreEqual(baked.Skin(v, baked.Instance(c, 0, 0), BatPrecision.Float), baked.Skin(v, instance, BatPrecision.Float));
            }
        }

        [Test]
        public void ReportedHalfErrorMatchesMeasuredWeightedFramesAndInterpolation()
        {
            using var baked = BatCharacterAsset.Bake(12);
            AssertMeasuredHalfError(baked);
        }

        [TestCase(0f, true)]
        [TestCase(37f, false)]
        public void HalfPrecisionAcceptanceIsDecidedByTheMeasuredPixelBudget(float angle, bool expectedAccepted)
        {
            using var rig = StaticRig(angle);
            var vertices = new[]
            {
                Vertex(new float2(8, 8), new float4(1, 2, 1, 0)),
                Vertex(new float2(7, 8), new float4(1, 2, .5f, .5f)),
                Vertex(new float2(8, 7), new float4(1, 2, 0, 1))
            };
            using var baked = BatBaker.Bake(rig.View, vertices, new[] { 0, 1, 2 }, 1);
            AssertMeasuredHalfError(baked);
            Assert.AreEqual(expectedAccepted, baked.HalfAccepted);
            if (expectedAccepted) Assert.AreEqual(0f, baked.HalfMaxModelError);
            else Assert.Greater(baked.HalfMaxPixelError, BatLimits.HalfPixelBudget);
        }

        [TestCase(1f, 1f, 0)]
        [TestCase(1f, -1f, 0)]
        [TestCase(-1f, 1f, 0)]
        [TestCase(-1f, -1f, 0)]
        [TestCase(1f, 1f, 1)]
        [TestCase(1f, -1f, 1)]
        [TestCase(-1f, 1f, 1)]
        [TestCase(-1f, -1f, 1)]
        [TestCase(1f, 1f, 2)]
        [TestCase(1f, -1f, 2)]
        [TestCase(-1f, 1f, 2)]
        [TestCase(-1f, -1f, 2)]
        public void StaticParentIkMatchesActualSkeletalSolverForBendFacingAndClampedTargets(float bend, float facing, int targetKind)
        {
            using var rig = BatCharacterAsset.CreateRig();
            BatCharacterAsset.CreateMesh(out var vertices, out var indices);
            using var baked = BatBaker.Bake(rig.View, vertices, indices, 4);
            using var pose = new NativeArray<BoneLocal>(BatLimits.Bones, Allocator.Temp);
            using var world = new NativeArray<BoneWorld>(BatLimits.Bones, Allocator.Temp);
            var inverses = BindInverses(rig.View);
            float2 target = targetKind == 0 ? new float2(.9f, 1.9f) : targetKind == 1 ? new float2(9, 12) : rig.Bones[1].Position;
            float2 position = new float2(2.5f, -.8f);
            const float scale = 1.7f;
            Skeletal.Sample(rig.View, 1, .31f, pose);
            Skeletal.TwoBoneIK(rig.View, pose, 1, 2, target, bend);
            Skeletal.ToWorld(rig.View, pose, position, facing, scale, world);
            var instance = baked.Instance(1, .31f, position, scale, facing, new float4(1), .37f, target, true, bend);
            var otherAnimation = baked.Instance(0, .81f, position, scale, facing, new float4(1), .37f, target, true, bend);
            for (int v = 0; v < vertices.Length; v++)
            {
                float2 expected = SourceWeighted(vertices[v], inverses, world, facing, scale);
                float3 actual = baked.Skin(v, instance, BatPrecision.Float);
                Assert.IsTrue(math.all(math.isfinite(actual)));
                Assert.Less(math.distance(new float3(expected, .37f), actual), PoseTolerance);
                Assert.Less(math.distance(actual, baked.Skin(v, instance, BatPrecision.Half)), PoseTolerance);
                Assert.Less(math.distance(actual, baked.Skin(v, otherAnimation, BatPrecision.Float)), PoseTolerance,
                    "The bounded IK contract is a full override of the animated arm.");
            }
        }

        [Test]
        public void BakedAssetOwnsItsInputsAndReturnsValueCopies()
        {
            BatClipSet baked = null;
            try
            {
                BatVertex savedVertex;
                BatRows savedRow;
                BatClip savedClip;
                float3 savedPosition;
                int savedIndex;
                BatInstance instance;
                using (var rig = BatCharacterAsset.CreateRig())
                {
                    BatCharacterAsset.CreateMesh(out var vertices, out var indices);
                    baked = BatBaker.Bake(rig.View, vertices, indices, 4);
                    savedVertex = baked.Vertex(vertices.Length - 1);
                    savedRow = baked.Row(0, 2, BatPrecision.Float);
                    savedClip = baked.Clip(0);
                    savedIndex = baked.Index(0);
                    instance = baked.Instance(0, .2f, 0);
                    savedPosition = baked.Skin(vertices.Length - 1, instance, BatPrecision.Float);
                    vertices[vertices.Length - 1] = default;
                    indices[0] = int.MaxValue;
                    rig.Keys[0] = default;
                    rig.Bones[1] = default;
                    rig.Channels[1] = default;
                    rig.Clips[0] = default;
                }
                var vertexCopy = baked.Vertex(baked.VertexCount - 1);
                vertexCopy.Position = new float2(99);
                var rowCopy = baked.Row(0, 2, BatPrecision.Float);
                rowCopy.Row0 = new float4(99);
                var clipCopy = baked.Clip(0);
                clipCopy.Duration = 99;
                Assert.AreEqual(savedVertex.Position, baked.Vertex(baked.VertexCount - 1).Position);
                Assert.AreEqual(savedVertex.Skin, baked.Vertex(baked.VertexCount - 1).Skin);
                Assert.AreEqual(savedRow.Row0, baked.Row(0, 2, BatPrecision.Float).Row0);
                Assert.AreEqual(savedRow.Row1, baked.Row(0, 2, BatPrecision.Float).Row1);
                Assert.AreEqual(savedClip.Duration, baked.Clip(0).Duration);
                Assert.AreEqual(savedClip.FrameCount, baked.Clip(0).FrameCount);
                Assert.AreEqual(savedIndex, baked.Index(0));
                Assert.AreEqual(savedPosition, baked.Skin(baked.VertexCount - 1, instance, BatPrecision.Float));
            }
            finally { baked?.Dispose(); }
        }

        [Test]
        public void PresentationSamplingLeavesAuthoritativeRigAndAnimatorStateUnchanged()
        {
            using var rig = BatCharacterAsset.CreateRig();
            var bonesBefore = rig.Bones.ToArray();
            var keysBefore = rig.Keys.ToArray();
            var channelsBefore = rig.Channels.ToArray();
            var clipsBefore = rig.Clips.ToArray();
            var animator = Animator2D.Start(0);
            animator.Advance(.31f);
            animator.Play(1, .5f);
            animator.Advance(.1f);
            var animatorBefore = animator;
            BatCharacterAsset.CreateMesh(out var vertices, out var indices);
            using var baked = BatBaker.Bake(rig.View, vertices, indices, 4);
            for (int sample = 0; sample < 6; sample++)
            {
                var instance = baked.Instance(animator.Clip, animator.Time + sample * .19f,
                    new float2(sample, -sample), 1.5f, sample % 2 == 0 ? 1 : -1,
                    new float4(1), .2f, new float2(.9f, 1.9f), sample % 2 == 0, -1);
                var instanceBefore = instance;
                for (int v = 0; v < baked.VertexCount; v++)
                {
                    baked.Skin(v, instance, BatPrecision.Float);
                    baked.Skin(v, instance, BatPrecision.Half);
                }
                baked.Row((int)instance.Frames.x, 2, BatPrecision.Float);
                Assert.AreEqual(instanceBefore, instance, "Presentation reads must not advance or rewrite their input snapshot.");
            }
            CollectionAssert.AreEqual(bonesBefore, rig.Bones.ToArray());
            CollectionAssert.AreEqual(keysBefore, rig.Keys.ToArray());
            CollectionAssert.AreEqual(channelsBefore, rig.Channels.ToArray());
            CollectionAssert.AreEqual(clipsBefore, rig.Clips.ToArray());
            Assert.AreEqual(animatorBefore, animator, "Rendering cannot advance authoritative animation time or crossfade state.");
        }

        [Test]
        public void DisposeIsIdempotentAndAllDataAccessRejectsDisposedAssets()
        {
            var baked = BatCharacterAsset.Bake(1);
            var instance = baked.Instance(0, 0, 0);
            Assert.IsFalse(baked.IsDisposed);
            baked.Dispose();
            baked.Dispose();
            Assert.IsTrue(baked.IsDisposed);
            Assert.Throws<ObjectDisposedException>(() => baked.Vertex(0));
            Assert.Throws<ObjectDisposedException>(() => baked.Index(0));
            Assert.Throws<ObjectDisposedException>(() => baked.Clip(0));
            Assert.Throws<ObjectDisposedException>(() => baked.Row(0, 0, BatPrecision.Float));
            Assert.Throws<ObjectDisposedException>(() => baked.Instance(0, 0, 0));
            Assert.Throws<ObjectDisposedException>(() => baked.Skin(0, instance, BatPrecision.Float));
        }

        [TestCase("missingBones")]
        [TestCase("missingKeys")]
        [TestCase("missingChannels")]
        [TestCase("missingClips")]
        [TestCase("emptyClips")]
        [TestCase("boneCount")]
        [TestCase("channelCount")]
        [TestCase("parent")]
        [TestCase("rootPosition")]
        [TestCase("bindRotation")]
        [TestCase("shortUpper")]
        [TestCase("longLower")]
        [TestCase("nonFiniteBone")]
        [TestCase("lowerOffset")]
        [TestCase("durationZero")]
        [TestCase("durationNonFinite")]
        [TestCase("negativeChannel")]
        [TestCase("overflowChannel")]
        [TestCase("rootAnimation")]
        [TestCase("animatedOffset")]
        [TestCase("nonFiniteKey")]
        [TestCase("negativeTime")]
        [TestCase("pastDuration")]
        [TestCase("duplicateTime")]
        public void BakerRejectsUnsupportedOrMalformedRigs(string invalid)
        {
            using var rig = BatCharacterAsset.CreateRig();
            BatCharacterAsset.CreateMesh(out var vertices, out var indices);
            var view = rig.View;
            var root = rig.Bones[0];
            var upper = rig.Bones[1];
            var lower = rig.Bones[2];
            var clip = rig.Clips[0];
            int first = rig.Channels[1].x;
            var key = rig.Keys[first];
            switch (invalid)
            {
                case "missingBones": view.Bones = default; break;
                case "missingKeys": view.Keys = default; break;
                case "missingChannels": view.Channels = default; break;
                case "missingClips": view.Clips = default; break;
                case "emptyClips": view.Clips = view.Clips.GetSubArray(0, 0); break;
                case "boneCount": view.Bones = view.Bones.GetSubArray(0, 2); break;
                case "channelCount": view.Channels = view.Channels.GetSubArray(0, 2); break;
                case "parent": upper.Parent = -1; rig.Bones[1] = upper; break;
                case "rootPosition": root.Position.x = .1f; rig.Bones[0] = root; break;
                case "bindRotation": upper.Rotation = .1f; rig.Bones[1] = upper; break;
                case "shortUpper": upper.Length = .001f; rig.Bones[1] = upper; break;
                case "longLower": lower.Length = 2.01f; rig.Bones[2] = lower; break;
                case "nonFiniteBone": upper.Position.y = float.NaN; rig.Bones[1] = upper; break;
                case "lowerOffset": lower.Position.y = .1f; rig.Bones[2] = lower; break;
                case "durationZero": clip.Duration = 0; rig.Clips[0] = clip; break;
                case "durationNonFinite": clip.Duration = float.PositiveInfinity; rig.Clips[0] = clip; break;
                case "negativeChannel": rig.Channels[1] = new int2(-1, 1); break;
                case "overflowChannel": rig.Channels[1] = new int2(int.MaxValue, int.MaxValue); break;
                case "rootAnimation": rig.Channels[0] = new int2(first, 1); break;
                case "animatedOffset": key.Offset.x = .1f; rig.Keys[first] = key; break;
                case "nonFiniteKey": key.Rotation = float.NaN; rig.Keys[first] = key; break;
                case "negativeTime": key.Time = -.01f; rig.Keys[first] = key; break;
                case "pastDuration": key.Time = clip.Duration + 1; rig.Keys[first] = key; break;
                case "duplicateTime": var next = rig.Keys[first + 1]; next.Time = key.Time; rig.Keys[first + 1] = next; break;
                default: throw new ArgumentException(invalid);
            }
            Assert.Throws<ArgumentException>(() => { using var rejected = BatBaker.Bake(view, vertices, indices, 4); });
        }

        [Test]
        public void BakerRejectsMoreThanTwoClips()
        {
            using var rig = BatCharacterAsset.CreateRig();
            using var clips = new NativeArray<ClipInfo>(3, Allocator.Temp);
            using var channels = new NativeArray<int2>(3 * BatLimits.Bones, Allocator.Temp);
            var view = rig.View;
            view.Clips = clips;
            view.Channels = channels;
            BatCharacterAsset.CreateMesh(out var vertices, out var indices);
            Assert.Throws<ArgumentException>(() => { using var rejected = BatBaker.Bake(view, vertices, indices, 4); });
        }

        [TestCase("nullVertices")]
        [TestCase("fewVertices")]
        [TestCase("manyVertices")]
        [TestCase("nullIndices")]
        [TestCase("fewIndices")]
        [TestCase("manyIndices")]
        [TestCase("incompleteTriangle")]
        [TestCase("negativeIndex")]
        [TestCase("pastEndIndex")]
        [TestCase("nonFinitePosition")]
        [TestCase("unboundedPosition")]
        [TestCase("nonFiniteUv")]
        [TestCase("fractionalBone")]
        [TestCase("negativeBone")]
        [TestCase("pastEndBone")]
        [TestCase("nonFiniteWeight")]
        [TestCase("negativeWeight")]
        [TestCase("unnormalizedWeight")]
        [TestCase("negativeColor")]
        [TestCase("brightColor")]
        [TestCase("nonFiniteColor")]
        public void BakerRejectsMalformedMeshesBeforeAllocatingPalettes(string invalid)
        {
            using var rig = BatCharacterAsset.CreateRig();
            BatCharacterAsset.CreateMesh(out var vertices, out var indices);
            var vertex = vertices[0];
            switch (invalid)
            {
                case "nullVertices": vertices = null; break;
                case "fewVertices": vertices = new BatVertex[2]; break;
                case "manyVertices": vertices = new BatVertex[BatLimits.MaxVertices + 1]; break;
                case "nullIndices": indices = null; break;
                case "fewIndices": indices = new int[2]; break;
                case "manyIndices": indices = new int[BatLimits.MaxIndices + 3]; break;
                case "incompleteTriangle": indices = new int[4]; break;
                case "negativeIndex": indices[0] = -1; break;
                case "pastEndIndex": indices[0] = vertices.Length; break;
                case "nonFinitePosition": vertex.Position.x = float.NaN; break;
                case "unboundedPosition": vertex.Position.y = 8.01f; break;
                case "nonFiniteUv": vertex.Uv.y = float.PositiveInfinity; break;
                case "fractionalBone": vertex.Skin.x = .5f; break;
                case "negativeBone": vertex.Skin.y = -1; break;
                case "pastEndBone": vertex.Skin.y = BatLimits.Bones; break;
                case "nonFiniteWeight": vertex.Skin.z = float.NaN; break;
                case "negativeWeight": vertex.Skin.z = 1.1f; vertex.Skin.w = -.1f; break;
                case "unnormalizedWeight": vertex.Skin.z = .9f; break;
                case "negativeColor": vertex.Color.x = -.1f; break;
                case "brightColor": vertex.Color.y = 1.1f; break;
                case "nonFiniteColor": vertex.Color.z = float.NaN; break;
                default: throw new ArgumentException(invalid);
            }
            if (vertices != null && vertices.Length > 0) vertices[0] = vertex;
            Assert.Throws<ArgumentException>(() => { using var rejected = BatBaker.Bake(rig.View, vertices, indices, 4); });
        }

        [TestCase(-1)]
        [TestCase(0)]
        [TestCase(61)]
        public void BakerRejectsFrameCountsOutsideItsBound(int frames)
        {
            using var rig = BatCharacterAsset.CreateRig();
            BatCharacterAsset.CreateMesh(out var vertices, out var indices);
            Assert.Throws<ArgumentException>(() => { using var rejected = BatBaker.Bake(rig.View, vertices, indices, frames); });
        }

        [TestCase(-1)]
        [TestCase(0)]
        [TestCase(257)]
        public void CapacityAndBackendSelectionRejectOutOfRangeCounts(int capacity)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => BatLimits.Capacity(capacity));
            var capabilities = FullCapabilities();
            Assert.Throws<ArgumentOutOfRangeException>(() => capabilities.Select(false, true, 60, capacity, out _));
        }

        [TestCase("nonFinitePlacement")]
        [TestCase("zeroScale")]
        [TestCase("negativeScale")]
        [TestCase("largeScale")]
        [TestCase("zeroFacing")]
        [TestCase("fractionalFacing")]
        [TestCase("largePosition")]
        [TestCase("negativeFrame")]
        [TestCase("fractionalFrame")]
        [TestCase("pastEndFirstFrame")]
        [TestCase("pastEndSecondFrame")]
        [TestCase("negativeBlend")]
        [TestCase("largeBlend")]
        [TestCase("nonFiniteFrame")]
        [TestCase("largeDepth")]
        [TestCase("negativeTint")]
        [TestCase("brightTint")]
        [TestCase("nonFiniteTint")]
        [TestCase("largeTarget")]
        [TestCase("nonFiniteTarget")]
        [TestCase("fractionalIkEnable")]
        [TestCase("largeIkEnable")]
        [TestCase("zeroBend")]
        [TestCase("fractionalBend")]
        public void InvalidInstanceDataIsRejected(string invalid)
        {
            var instance = ValidInstance();
            switch (invalid)
            {
                case "nonFinitePlacement": instance.Placement.x = float.NaN; break;
                case "zeroScale": instance.Placement.z = 0; break;
                case "negativeScale": instance.Placement.z = -1; break;
                case "largeScale": instance.Placement.z = BatLimits.MaxScale + .1f; break;
                case "zeroFacing": instance.Placement.w = 0; break;
                case "fractionalFacing": instance.Placement.w = .5f; break;
                case "largePosition": instance.Placement.y = 100001; break;
                case "negativeFrame": instance.Frames.x = -1; break;
                case "fractionalFrame": instance.Frames.y = .5f; break;
                case "pastEndFirstFrame": instance.Frames.x = 2; break;
                case "pastEndSecondFrame": instance.Frames.y = 2; break;
                case "negativeBlend": instance.Frames.z = -.1f; break;
                case "largeBlend": instance.Frames.z = 1.1f; break;
                case "nonFiniteFrame": instance.Frames.w = float.PositiveInfinity; break;
                case "largeDepth": instance.Frames.w = -100001; break;
                case "negativeTint": instance.Tint.x = -.1f; break;
                case "brightTint": instance.Tint.w = 1.1f; break;
                case "nonFiniteTint": instance.Tint.y = float.NaN; break;
                case "largeTarget": instance.Ik.x = 1001; break;
                case "nonFiniteTarget": instance.Ik.y = float.NaN; break;
                case "fractionalIkEnable": instance.Ik.z = .5f; break;
                case "largeIkEnable": instance.Ik.z = 2; break;
                case "zeroBend": instance.Ik.w = 0; break;
                case "fractionalBend": instance.Ik.w = -.5f; break;
                default: throw new ArgumentException(invalid);
            }
            Assert.Throws<ArgumentException>(() => BatLimits.Instance(instance, 2));
        }

        [Test]
        public void InstanceFactoryAndSkinEntryPointEnforceTheContract()
        {
            using var baked = BatCharacterAsset.Bake(1);
            foreach (float invalidTime in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
                Assert.Throws<ArgumentException>(() => baked.Instance(0, invalidTime, 0));
            Assert.Throws<ArgumentException>(() => baked.Instance(0, 0, 0, 0));
            var invalidInstance = ValidInstance();
            invalidInstance.Frames.y = baked.FrameCount;
            Assert.Throws<ArgumentException>(() => baked.Skin(0, invalidInstance, BatPrecision.Float));
            Assert.Throws<ArgumentOutOfRangeException>(() => baked.Row(-1, 0, BatPrecision.Float));
            Assert.Throws<ArgumentOutOfRangeException>(() => baked.Row(baked.FrameCount, 0, BatPrecision.Float));
            Assert.Throws<ArgumentOutOfRangeException>(() => baked.Row(0, BatLimits.Bones, BatPrecision.Float));
            var boundary = new BatInstance
            {
                Placement = new float4(-100000, 100000, BatLimits.MaxScale, -1),
                Frames = new float4(0, 119, 1, -100000),
                Tint = new float4(0, 1, 0, 1),
                Ik = new float4(-1000, 1000, 1, -1)
            };
            Assert.DoesNotThrow(() => BatLimits.Instance(boundary, 120));
            Assert.DoesNotThrow(() => BatLimits.Capacity(1));
            Assert.DoesNotThrow(() => BatLimits.Capacity(BatLimits.MaxCapacity));
        }

        [TestCase("forceCpu")]
        [TestCase("headless")]
        [TestCase("unsupportedApi")]
        [TestCase("unsupportedShader")]
        [TestCase("noInstancing")]
        [TestCase("shaderLevel")]
        [TestCase("vertexBuffers")]
        [TestCase("textureWidth")]
        [TestCase("textureHeight")]
        [TestCase("bufferBytes")]
        public void EveryIndependentGpuCapabilityGateCanForceTheCpuBackend(string missing)
        {
            var capabilities = FullCapabilities();
            bool forceCpu = false;
            int frames = 60;
            switch (missing)
            {
                case "forceCpu": forceCpu = true; break;
                case "headless": capabilities.Graphics = false; break;
                case "unsupportedApi": capabilities.SupportedApi = false; break;
                case "unsupportedShader": capabilities.Shader = false; break;
                case "noInstancing": capabilities.Instancing = false; break;
                case "shaderLevel": capabilities.ShaderLevel = 44; break;
                case "vertexBuffers": capabilities.VertexBuffers = 0; break;
                case "textureWidth": capabilities.MaxTextureSize = 2 * BatLimits.Bones - 1; frames = 1; break;
                case "textureHeight": capabilities.MaxTextureSize = 59; break;
                case "bufferBytes": capabilities.MaxBufferBytes = 2 * BatInstance.Stride - 1; break;
                default: throw new ArgumentException(missing);
            }
            Assert.AreEqual(BatBackend.CpuWeighted, capabilities.Select(forceCpu, true, frames, 2, out _));
        }

        [Test]
        public void ComputeClassShaderLevelDoesNotImplyVertexBufferSupport()
        {
            var capabilities = FullCapabilities();
            capabilities.ShaderLevel = 50;
            capabilities.VertexBuffers = 0;
            Assert.AreEqual(BatBackend.CpuWeighted, capabilities.Select(false, true, 60, 2, out _));
            capabilities.VertexBuffers = 1;
            Assert.AreEqual(BatBackend.GpuVertex, capabilities.Select(false, true, 60, 2, out _));
        }

        [TestCase(true, true, true, BatBackend.GpuVertex, BatPrecision.Half)]
        [TestCase(true, true, false, BatBackend.GpuVertex, BatPrecision.Half)]
        [TestCase(true, false, true, BatBackend.GpuVertex, BatPrecision.Float)]
        [TestCase(true, false, false, BatBackend.CpuWeighted, BatPrecision.Float)]
        [TestCase(false, true, true, BatBackend.GpuVertex, BatPrecision.Float)]
        [TestCase(false, true, false, BatBackend.CpuWeighted, BatPrecision.Float)]
        [TestCase(false, false, true, BatBackend.GpuVertex, BatPrecision.Float)]
        [TestCase(false, false, false, BatBackend.CpuWeighted, BatPrecision.Float)]
        public void BackendSelectionRequiresTheExactAcceptedSampleFormat(bool halfAccepted, bool halfSample, bool floatSample, BatBackend backend, BatPrecision precision)
        {
            var capabilities = FullCapabilities();
            capabilities.HalfSample = halfSample;
            capabilities.FloatSample = floatSample;
            Assert.AreEqual(backend, capabilities.Select(false, halfAccepted, 60, 2, out var selected));
            Assert.AreEqual(precision, selected);
        }

        [Test]
        public void BackendSelectionAcceptsExactResourceBoundsAndRejectsInvalidFrameCounts()
        {
            var capabilities = FullCapabilities();
            capabilities.MaxTextureSize = BatLimits.MaxClips * BatLimits.MaxFramesPerClip;
            capabilities.MaxBufferBytes = (long)BatLimits.MaxCapacity * BatInstance.Stride;
            Assert.AreEqual(BatBackend.GpuVertex, capabilities.Select(false, false, capabilities.MaxTextureSize, BatLimits.MaxCapacity, out _));
            Assert.Throws<ArgumentOutOfRangeException>(() => capabilities.Select(false, false, 0, 1, out _));
            Assert.Throws<ArgumentOutOfRangeException>(() => capabilities.Select(false, false, 121, 1, out _));
        }

        static BatVertex Vertex(float2 position, float4 weights) => new BatVertex
        {
            Position = position, Uv = new float2(.5f), Skin = weights, Color = new float4(1)
        };

        static SkeletonAsset StaticRig(float angle)
        {
            var builder = new SkeletonAsset.Builder()
                .Bone("root", null, 0, 0, 0)
                .Bone("upper", "root", new float2(.2f, 1.4f), 0, .6f)
                .Bone("lower", "upper", new float2(.6f, 0), 0, .5f);
            builder.Clip("static", 1, true).Key("upper", 0, angle).Key("lower", 0, 0);
            return builder.Build();
        }

        static BatInstance ValidInstance() => new BatInstance
        {
            Placement = new float4(3, 4, 1, -1), Frames = new float4(0, 1, .5f, .3f),
            Tint = new float4(1), Ik = new float4(.2f, 1.4f, 0, 1)
        };

        static BatCapabilities FullCapabilities() => new BatCapabilities
        {
            Graphics = true, SupportedApi = true, Shader = true, Instancing = true,
            HalfSample = true, FloatSample = true, ShaderLevel = 45, VertexBuffers = 1,
            MaxTextureSize = 60, MaxBufferBytes = 2 * BatInstance.Stride
        };

        static void AssertFrames(BatInstance instance, int a, int b, float blend)
        {
            Assert.AreEqual(a, instance.Frames.x);
            Assert.AreEqual(b, instance.Frames.y);
            Assert.AreEqual(blend, instance.Frames.z, 1e-6f);
        }

        static Affine2D[] BindInverses(SkeletonView rig)
        {
            using var pose = new NativeArray<BoneLocal>(BatLimits.Bones, Allocator.Temp);
            using var world = new NativeArray<BoneWorld>(BatLimits.Bones, Allocator.Temp);
            var writablePose = pose;
            for (int b = 0; b < BatLimits.Bones; b++)
                writablePose[b] = new BoneLocal { Position = rig.Bones[b].Position, Rotation = rig.Bones[b].Rotation };
            Skeletal.ToWorld(rig, pose, 0, 1, 1, world);
            var inverses = new Affine2D[BatLimits.Bones];
            for (int b = 0; b < BatLimits.Bones; b++)
                Assert.IsTrue(Affine2D.FromBone(world[b], 1).TryInverse(out inverses[b]));
            return inverses;
        }

        static float2 SourceWeighted(BatVertex vertex, Affine2D[] inverses, NativeArray<BoneWorld> world, float facing, float scale)
        {
            int a = (int)vertex.Skin.x, b = (int)vertex.Skin.y;
            float2 localA = inverses[a].TransformPoint(vertex.Position);
            float2 localB = inverses[b].TransformPoint(vertex.Position);
            return vertex.Skin.z * world[a].Transform(localA * scale, facing) + vertex.Skin.w * world[b].Transform(localB * scale, facing);
        }

        static void AssertBakedMatchesSource(SkeletonView rig, BatClipSet baked)
        {
            var inverses = BindInverses(rig);
            using var pose = new NativeArray<BoneLocal>(BatLimits.Bones, Allocator.Temp);
            using var world = new NativeArray<BoneWorld>(BatLimits.Bones, Allocator.Temp);
            for (int c = 0; c < baked.ClipCount; c++)
            {
                var clip = baked.Clip(c);
                Assert.AreEqual(rig.Clips[c].Duration, clip.Duration);
                Assert.AreEqual(rig.Clips[c].Loop, clip.Loop);
                for (int f = 0; f < clip.FrameCount; f++)
                {
                    float time = clip.FrameCount == 1 ? 0 : clip.Duration * f / (clip.Loop ? clip.FrameCount : clip.FrameCount - 1);
                    Skeletal.Sample(rig, c, time, pose);
                    Skeletal.ToWorld(rig, pose, 0, 1, 1, world);
                    for (int b = 0; b < BatLimits.Bones; b++)
                    {
                        var row = baked.Row(clip.FirstFrame + f, b, BatPrecision.Float);
                        Assert.AreEqual(0f, row.Row0.w);
                        Assert.AreEqual(0f, row.Row1.w);
                        foreach (var point in new[] { float2.zero, new float2(1, 0), new float2(0, 1), new float2(.8f, 1.495f) })
                        {
                            float2 expected = world[b].Transform(inverses[b].TransformPoint(point), 1);
                            Assert.Less(math.distance(expected, row.Transform(point)), PoseTolerance);
                        }
                    }
                    foreach (float facing in new[] { -1f, 1f })
                    {
                        float2 position = new float2(2.5f, -1.25f);
                        const float scale = 1.7f;
                        Skeletal.ToWorld(rig, pose, position, facing, scale, world);
                        var instance = baked.Instance(c, time, position, scale, facing, new float4(1), .6f);
                        // Select the exact baked frame so this assertion measures the bake, not float time division.
                        instance.Frames = new float4(clip.FirstFrame + f, clip.FirstFrame + f, 0, .6f);
                        for (int v = 0; v < baked.VertexCount; v++)
                        {
                            float2 expected = SourceWeighted(baked.Vertex(v), inverses, world, facing, scale);
                            Assert.Less(math.distance(new float3(expected, .6f), baked.Skin(v, instance, BatPrecision.Float)), PoseTolerance);
                        }
                    }
                }
            }
        }

        static float2 WeightedFrame(BatClipSet baked, int frame, BatVertex vertex, BatPrecision precision) =>
            vertex.Skin.z * baked.Row(frame, (int)vertex.Skin.x, precision).Transform(vertex.Position) +
            vertex.Skin.w * baked.Row(frame, (int)vertex.Skin.y, precision).Transform(vertex.Position);

        static void AssertMeasuredHalfError(BatClipSet baked)
        {
            float measured = 0;
            for (int c = 0; c < baked.ClipCount; c++)
            {
                var clip = baked.Clip(c);
                for (int f = 0; f < clip.FrameCount; f++)
                for (int sub = 0; sub < 3; sub++)
                {
                    int next = clip.Loop ? (f + 1) % clip.FrameCount : math.min(f + 1, clip.FrameCount - 1);
                    var instance = baked.Instance(c, 0, 0, tint: new float4(1));
                    instance.Frames = new float4(clip.FirstFrame + f, clip.FirstFrame + next, sub * .5f, 0);
                    for (int v = 0; v < baked.VertexCount; v++)
                    {
                        float3 full = baked.Skin(v, instance, BatPrecision.Float);
                        float3 half = baked.Skin(v, instance, BatPrecision.Half);
                        Assert.IsTrue(math.all(math.isfinite(full)));
                        Assert.IsTrue(math.all(math.isfinite(half)));
                        measured = math.max(measured, math.distance(full, half));
                    }
                }
            }
            Assert.AreEqual(measured, baked.HalfMaxModelError, 1e-7f);
            Assert.AreEqual(measured * BatLimits.MaxPixelsPerUnit * BatLimits.MaxScale, baked.HalfMaxPixelError, 1e-4f);
            Assert.AreEqual(baked.HalfMaxPixelError <= BatLimits.HalfPixelBudget, baked.HalfAccepted);
        }
    }
}
