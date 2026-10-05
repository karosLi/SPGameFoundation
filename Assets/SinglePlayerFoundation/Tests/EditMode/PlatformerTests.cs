using NUnit.Framework;
using SPF.L1.Spatial;
using SPF.L2.Movement;
using Unity.Mathematics;

namespace SPF.Tests.EditMode
{
    public class PlatformerTests
    {
        const byte Solid = 1, OneWay = 2;
        static readonly float2 Half = new float2(0.4f, 0.5f);
        const float Dt = 1f / 60f;

        /// <summary>A 40 x 20 room: floor at row 0, walls at the sides, a one-way ledge and a pillar.</summary>
        static TileMap Room()
        {
            var map = new TileMap(new int2(40, 20), 1f);
            for (int x = 0; x < 40; x++) { map[new int2(x, 0)] = Solid; map[new int2(x, 19)] = Solid; }
            for (int y = 0; y < 20; y++) { map[new int2(0, y)] = Solid; map[new int2(39, y)] = Solid; }
            for (int x = 10; x < 15; x++) map[new int2(x, 4)] = OneWay;
            for (int y = 1; y < 8; y++) map[new int2(25, y)] = Solid;
            return map;
        }

        [Test]
        public void BoxesLandSlideAndNeverTunnel()
        {
            using var map = Room();
            var view = map.AsView();
            // Falling very fast still stops on the floor.
            var p = view.MoveBox(new float2(5f, 10f), Half, new float2(0f, -50f), OneWay, out var c);
            Assert.AreEqual(1.5f, p.y, 1e-4f, "flush on the floor (top of row 0 is y = 1)");
            Assert.IsTrue((c & BoxContacts.Ground) != 0);
            // Sideways into the pillar from afar.
            p = view.MoveBox(new float2(20f, 1.5f), Half, new float2(30f, 0f), OneWay, out c);
            Assert.AreEqual(25f - 0.4f, p.x, 1e-4f);
            Assert.IsTrue((c & BoxContacts.Right) != 0);
            // Diagonal: slides along the floor.
            p = view.MoveBox(new float2(5f, 1.5f), Half, new float2(2f, -1f), OneWay, out c);
            Assert.AreEqual(new float2(7f, 1.5f), p);
            Assert.IsTrue(view.BoxGrounded(p, Half, OneWay));
            Assert.IsTrue(view.BoxTouches(new float2(12f, 4.6f), Half, OneWay));
        }

        [Test]
        public void OneWayPlatformsBlockOnlyFromAbove()
        {
            using var map = Room();
            var view = map.AsView();
            // Jump up through the ledge (row 4: y 4..5).
            var p = view.MoveBox(new float2(12f, 2f), Half, new float2(0f, 4f), OneWay, out var c);
            Assert.AreEqual(6f, p.y, 1e-4f, "passed through from below");
            Assert.AreEqual(BoxContacts.None, c);
            // Fall back onto it.
            p = view.MoveBox(p, Half, new float2(0f, -3f), OneWay, out c);
            Assert.AreEqual(5.5f, p.y, 1e-4f, "stands on top");
            Assert.IsTrue((c & BoxContacts.Ground) != 0);
            // Walking sideways through the ledge's height is not blocked.
            p = view.MoveBox(new float2(8f, 4.5f), Half, new float2(4f, 0f), OneWay, out c);
            Assert.AreEqual(12f, p.x, 1e-4f);
        }

        static float2 Run(TileMap map, ref PlatformerState s, float2 p, PlatformerInput input, int steps, in PlatformerTuning t)
        {
            var view = map.AsView();
            for (int i = 0; i < steps; i++)
            {
                p = PlatformerMotor.Step(t, ref s, input, Dt, view, p, Half, OneWay);
                input.JumpPressed = false;
            }
            return p;
        }

        [Test]
        public void JumpHeightCoyoteAndBuffer()
        {
            using var map = Room();
            var t = PlatformerTuning.Default;
            var s = new PlatformerState();
            var p = Run(map, ref s, new float2(5f, 1.5f), default, 5, t);
            Assert.IsTrue(s.Grounded);

            // Full jump reaches about the analytic apex; an early release jumps much lower.
            float apex = p.y;
            var view = map.AsView();
            var input = new PlatformerInput { JumpPressed = true, JumpHeld = true };
            for (int i = 0; i < 120; i++) { p = PlatformerMotor.Step(t, ref s, input, Dt, view, p, Half, OneWay); input.JumpPressed = false; apex = math.max(apex, p.y); }
            Assert.AreEqual(1.5f + t.JumpHeight, apex, 0.35f);
            Assert.IsTrue(s.Grounded, "landed again");
            float low = p.y;
            input = new PlatformerInput { JumpPressed = true, JumpHeld = true };
            for (int i = 0; i < 120; i++) { p = PlatformerMotor.Step(t, ref s, input, Dt, view, p, Half, OneWay); input = new PlatformerInput(); low = math.max(low, p.y); }
            Assert.Less(low - 1.5f, (apex - 1.5f) * 0.5f, "tapping jumps lower");

            // Coyote: walk off the one-way ledge, press jump a few frames later: still jumps.
            s = new PlatformerState();
            p = Run(map, ref s, new float2(14f, 5.5f), default, 3, t);
            Assert.IsTrue(s.Grounded);
            for (int i = 0; i < 120 && s.Grounded; i++) p = Run(map, ref s, p, new PlatformerInput { MoveX = 1f }, 1, t);   // off the edge (x = 15)
            Assert.IsFalse(s.Grounded);
            p = Run(map, ref s, p, new PlatformerInput { MoveX = 1f }, 3, t);   // a few frames late
            p = Run(map, ref s, p, new PlatformerInput { JumpPressed = true, JumpHeld = true }, 1, t);
            Assert.IsTrue(s.JumpedThisStep || s.Velocity.y > 0f, "coyote jump");

            // Buffer: press jump just before landing: jumps on landing.
            s = new PlatformerState();
            p = Run(map, ref s, new float2(30f, 1.7f), default, 1, t);
            p = Run(map, ref s, p, new PlatformerInput { JumpPressed = true, JumpHeld = true }, 1, t);
            bool jumped = false;
            for (int i = 0; i < 10 && !jumped; i++) { p = Run(map, ref s, p, new PlatformerInput { JumpHeld = true }, 1, t); jumped = s.Velocity.y > 5f; }
            Assert.IsTrue(jumped, "buffered jump fired on landing");
        }

        [Test]
        public void WallSlideAndWallJump()
        {
            using var map = Room();
            var t = PlatformerTuning.Default;
            var s = new PlatformerState();
            // In the air next to the pillar, pushing into it.
            var p = Run(map, ref s, new float2(24f, 6.5f), new PlatformerInput { MoveX = 1f }, 30, t);
            Assert.AreEqual(1, s.WallSide);
            Assert.GreaterOrEqual(s.Velocity.y, -t.WallSlideSpeed - 1e-3f, "slides slowly");
            p = Run(map, ref s, p, new PlatformerInput { MoveX = 1f, JumpPressed = true, JumpHeld = true }, 1, t);
            Assert.Less(s.Velocity.x, 0f, "kicked away from the wall");
            Assert.Greater(s.Velocity.y, 0f);
        }

        [Test]
        public void LandingOnMovingBoxes()
        {
            var p = new float2(3f, 2.4f);
            Assert.IsTrue(PlatformerMotor.LandOn(ref p, Half, 2.05f, new float2(3.2f, 1.75f), new float2(1f, 0.25f)));
            Assert.AreEqual(2.5f, p.y, 1e-5f);
            var q = new float2(3f, 1.5f);
            Assert.IsFalse(PlatformerMotor.LandOn(ref q, Half, 0.9f, new float2(3.2f, 1.75f), new float2(1f, 0.25f)), "from below: no");
        }
    }
}

namespace SPF.Tests.EditMode
{
    public class CameraRigTests
    {
        [Test]
        public void DeadZoneAndBounds()
        {
            var zone = new Unity.Mathematics.float2(2f, 1f);
            Assert.AreEqual(new Unity.Mathematics.float2(0f, 0f), SPF.Shell.CameraRig.FollowCamera2D.Goal(0f, new Unity.Mathematics.float2(1.5f, -0.5f), zone), "inside: stay");
            Assert.AreEqual(new Unity.Mathematics.float2(3f, 0f), SPF.Shell.CameraRig.FollowCamera2D.Goal(0f, new Unity.Mathematics.float2(5f, 0.5f), zone), "outside: just enough");
            var bounds = new Unity.Mathematics.float4(0f, 0f, 40f, 20f);
            Assert.AreEqual(new Unity.Mathematics.float2(8f, 5f), SPF.Shell.CameraRig.FollowCamera2D.Clamp(new Unity.Mathematics.float2(2f, -3f), new Unity.Mathematics.float2(8f, 5f), bounds));
            Assert.AreEqual(new Unity.Mathematics.float2(20f, 10f), SPF.Shell.CameraRig.FollowCamera2D.Clamp(new Unity.Mathematics.float2(2f, -3f), new Unity.Mathematics.float2(30f, 15f), bounds), "a small level is centred");
        }
    }
}
