using System.Diagnostics;
using System.IO;
using NUnit.Framework;
using SPF.L1.Skeleton;
using SPF.Presentation.Animation;
using SPF.Presentation.Sprites;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace SPF.Tests.EditMode
{
    public class SkeletonTests
    {
        /// <summary>Root → arm (1 m, pointing right) → forearm (1 m); a "wave" clip and a "raise" clip.</summary>
        static SkeletonAsset Arm()
        {
            var b = new SkeletonAsset.Builder()
                .Bone("root", null, float2.zero, 0f, 0f)
                .Bone("arm", "root", new float2(0f, 1f), 0f, 1f)
                .Bone("fore", "arm", new float2(1f, 0f), 0f, 1f);
            b.Clip("wave", 1f, true).Key("fore", 0f, 0f).Key("fore", 0.5f, 90f);
            b.Clip("raise", 1f, false).Key("arm", 0f, 0f).Key("arm", 1f, 90f).Key("root", 0f, 0f, new float2(0f, 0.5f));
            return b.Build();
        }

        static (NativeArray<BoneLocal> pose, NativeArray<BoneLocal> scratch, NativeArray<BoneWorld> world) Buffers(int bones) =>
            (new NativeArray<BoneLocal>(bones, Allocator.Temp), new NativeArray<BoneLocal>(bones, Allocator.Temp), new NativeArray<BoneWorld>(bones, Allocator.Temp));

        [Test]
        public void SamplesInterpolateAndChainsCompose()
        {
            using var asset = Arm();
            var view = asset.View;
            var (pose, scratch, world) = Buffers(asset.BoneCount);
            int wave = asset.Clip("wave");
            var animator = Animator2D.Start(wave);
            animator.Advance(0.5f);
            Skeletal.Evaluate(view, animator, pose, scratch);
            Skeletal.ToWorld(view, pose, new float2(10f, 0f), 1f, 1f, world);
            var tip = world[asset.Bone("fore")].Transform(new float2(1f, 0f), 1f);
            Assert.AreEqual(11f, tip.x, 1e-4f, "forearm turned 90°: tip straight above the elbow");
            Assert.AreEqual(2f, tip.y, 1e-4f);

            // Facing left mirrors around the root.
            Skeletal.ToWorld(view, pose, new float2(10f, 0f), -1f, 1f, world);
            tip = world[asset.Bone("fore")].Transform(new float2(1f, 0f), -1f);
            Assert.AreEqual(9f, tip.x, 1e-4f);
            Assert.AreEqual(2f, tip.y, 1e-4f);

            // Looping: t = 1.25 is a quarter of the way back from 90° (the wrap eases towards the first key).
            animator = Animator2D.Start(wave);
            animator.Advance(0.25f);
            Skeletal.Evaluate(view, animator, pose, scratch);
            Assert.AreEqual(math.radians(45f), pose[asset.Bone("fore")].Rotation, 1e-3f, "eased halfway between keys");
        }

        [Test]
        public void CrossfadeBlendsAndOneShotsFinish()
        {
            using var asset = Arm();
            var view = asset.View;
            var (pose, scratch, _) = Buffers(asset.BoneCount);
            var animator = Animator2D.Start(asset.Clip("raise"));
            animator.Advance(1f);
            Assert.IsTrue(animator.Finished(view));
            Skeletal.Evaluate(view, animator, pose, scratch);
            Assert.AreEqual(math.radians(90f), pose[asset.Bone("arm")].Rotation, 1e-4f);
            Assert.AreEqual(0.5f, pose[0].Position.y, 1e-4f, "root offset key (bind at 0)");

            animator.Play(asset.Clip("wave"), 0.2f);
            animator.Advance(0.1f);
            Skeletal.Evaluate(view, animator, pose, scratch);
            Assert.AreEqual(math.radians(45f), pose[asset.Bone("arm")].Rotation, 1e-3f, "half way through the fade");
            animator.Advance(0.2f);
            Skeletal.Evaluate(view, animator, pose, scratch);
            Assert.AreEqual(0f, pose[asset.Bone("arm")].Rotation, 1e-4f, "fade done");
        }

        [Test]
        public void TwoBoneIKReachesTheTarget()
        {
            using var asset = Arm();
            var view = asset.View;
            var (pose, scratch, world) = Buffers(asset.BoneCount);
            Skeletal.Evaluate(view, Animator2D.Start(asset.Clip("wave")), pose, scratch);
            var target = new float2(1.2f, 2.1f);   // in reach (shoulder at (0, 1), arms 1 + 1)
            Skeletal.TwoBoneIK(view, pose, asset.Bone("arm"), asset.Bone("fore"), target);
            Skeletal.ToWorld(view, pose, float2.zero, 1f, 1f, world);
            var tip = world[asset.Bone("fore")].Transform(new float2(1f, 0f), 1f);
            Assert.AreEqual(target.x, tip.x, 1e-3f);
            Assert.AreEqual(target.y, tip.y, 1e-3f);
        }

        [Test]
        public void CrowdBenchmark()
        {
            // An 11-bone humanoid with 11 attachments, 1000 characters: pose + sprite jobs.
            var b = new SkeletonAsset.Builder()
                .Bone("pelvis", null, new float2(0f, 1f), 0f, 0.2f)
                .Bone("torso", "pelvis", new float2(0f, 0.1f), 90f, 0.6f)
                .Bone("head", "torso", new float2(0.6f, 0f), 0f, 0.3f);
            foreach (var side in new[] { "L", "R" })
            {
                b.Bone("upper" + side, "torso", new float2(0.55f, 0f), 180f, 0.35f).Bone("fore" + side, "upper" + side, new float2(0.35f, 0f), 0f, 0.35f);
                b.Bone("thigh" + side, "pelvis", new float2(0f, 0f), -90f, 0.45f).Bone("shin" + side, "thigh" + side, new float2(0.45f, 0f), 0f, 0.45f);
            }
            var walk = b.Clip("walk", 0.8f, true);
            foreach (var bone in new[] { "upperL", "upperR", "thighL", "thighR", "foreL", "shinR" })
                walk.Key(bone, 0f, 25f).Key(bone, 0.4f, -25f);
            using var asset = b.Build();
            const int N = 1000;
            int bones = asset.BoneCount;
            var animators = new NativeArray<Animator2D>(N, Allocator.TempJob);
            var roots = new NativeArray<float2>(N, Allocator.TempJob);
            var facing = new NativeArray<float>(N, Allocator.TempJob);
            var tint = new NativeArray<float4>(N, Allocator.TempJob);
            var flash = new NativeArray<float>(N, Allocator.TempJob);
            var depth = new NativeArray<float>(N, Allocator.TempJob);
            var scratch = new NativeArray<BoneLocal>(N * bones * 2, Allocator.TempJob);
            var world = new NativeArray<BoneWorld>(N * bones, Allocator.TempJob);
            var attachments = new NativeArray<BoneAttachment>(bones, Allocator.TempJob);
            var output = new NativeArray<PackedSprite>(N * bones, Allocator.TempJob);
            var skin = new NativeArray<int>(N, Allocator.TempJob);
            var palette = new NativeArray<float4>(0, Allocator.TempJob);   // no skins (containers must still exist)
            try
            {
                for (int i = 0; i < bones; i++) attachments[i] = new BoneAttachment { Bone = i, Size = new float2(0.4f, 0.15f), Offset = new float2(0.2f, 0f), Tint = 1f, Uv = new float4(0f, 0f, 0.1f, 0.1f) };
                for (int i = 0; i < N; i++)
                {
                    var a = Animator2D.Start(0);
                    a.Advance(i * 0.013f);
                    animators[i] = a;
                    roots[i] = new float2(i % 40, i / 40);
                    facing[i] = (i & 1) == 0 ? 1f : -1f;
                    tint[i] = 1f;
                    depth[i] = 1f;
                }
                JobHandle Schedule(float offset)
                {
                    var pose = new SkeletonPoseJob { View = asset.View, Animators = animators, Roots = roots, Facing = facing, Scale = 1f, TimeOffset = offset, Scratch = scratch, World = world }.Schedule(N, 32);
                    return new SkeletonSpriteJob { Bones = bones, World = world, Attachments = attachments, Facing = facing, Tint = tint, Flash = flash, Depth = depth, Skin = skin, Palette = palette, LayerStep = 0.001f, Out = output }.Schedule(N, 32, pose);
                }
                for (int i = 0; i < 10; i++) Schedule(i * 0.01f).Complete();   // warm-up (Burst compile in the editor)
                var watch = Stopwatch.StartNew();
                const int Frames = 60;
                for (int i = 0; i < Frames; i++) Schedule(i * 0.016f).Complete();
                double ms = watch.Elapsed.TotalMilliseconds / Frames;
                string report = $"=== Skeletal crowd: {N} characters x {bones} bones / sprites ===\npose + sprite jobs ms per frame {ms:F3} ({N * bones} packed sprites, {N * bones * PackedSprite.Stride / 1024} KiB)\n";
                TestContext.WriteLine(report);
                try
                {
                    string dir = Path.Combine(UnityEngine.Application.dataPath, "..", "Artifacts");
                    Directory.CreateDirectory(dir);
                    File.WriteAllText(Path.Combine(dir, "perf-skeleton.txt"), report);
                }
                catch (System.Exception e) when (e is IOException || e is System.UnauthorizedAccessException) { }   // report only (no project folder in the .NET harness)
                Assert.AreNotEqual(output[0].Center, output[bones].Center, "characters placed apart");
#if !SPF_DOTNET_HARNESS
                Assert.Less(ms, 4.0, "Burst budget for a thousand skeletal characters");
#endif
            }
            finally
            {
                animators.Dispose(); roots.Dispose(); facing.Dispose(); tint.Dispose(); flash.Dispose(); depth.Dispose();
                scratch.Dispose(); world.Dispose(); attachments.Dispose(); output.Dispose(); skin.Dispose(); palette.Dispose();
            }
        }
    }
}
