using SPF.L1.Spatial;
using Unity.Mathematics;

namespace SPF.L2.Movement
{
    /// <summary>Feel parameters of a side-view character (units per second, seconds).</summary>
    [System.Serializable]
    public struct PlatformerTuning
    {
        public float RunSpeed, GroundAccel, AirAccel;
        public float Gravity, FallGravityScale, MaxFall;
        public float JumpVelocity;
        /// <summary>Upward velocity kept when the jump button is released early (variable jump height).</summary>
        public float JumpCut;
        /// <summary>Grace period to still jump after walking off a ledge.</summary>
        public float CoyoteTime;
        /// <summary>A jump pressed this long before landing still happens on landing.</summary>
        public float JumpBuffer;
        /// <summary>Maximum fall speed while pushing into a wall (0 = no wall slide / wall jump).</summary>
        public float WallSlideSpeed;
        public float2 WallJump;

        public static PlatformerTuning Default => new PlatformerTuning
        {
            RunSpeed = 7f, GroundAccel = 70f, AirAccel = 40f,
            Gravity = 45f, FallGravityScale = 1.6f, MaxFall = 22f,
            JumpVelocity = 15f, JumpCut = 0.45f, CoyoteTime = 0.1f, JumpBuffer = 0.12f,
            WallSlideSpeed = 3f, WallJump = new float2(8f, 13f),
        };

        /// <summary>Apex height of a full jump (no early release).</summary>
        public float JumpHeight => JumpVelocity * JumpVelocity / (2f * math.max(Gravity, 1e-3f));
    }

    /// <summary>Per-character motor state (blittable: lives in a table column).</summary>
    public struct PlatformerState
    {
        public float2 Velocity;
        public float Coyote, Buffer;
        public bool Grounded, JumpHeld;
        public sbyte WallSide;         // -1 / +1 while sliding on a wall
        public BoxContacts Contacts;   // last move
        public bool JumpedThisStep;
    }

    /// <summary>Input of one step.</summary>
    public struct PlatformerInput
    {
        public float MoveX;           // -1..1
        public bool JumpPressed, JumpHeld;
    }

    /// <summary>
    /// Deterministic side-view character controller on a tile map (Burst-friendly, no allocation): ground and
    /// air acceleration, gravity with faster falls, variable jump height, coyote time, jump buffering, wall
    /// slide / wall jump, one-way platforms and riding moving platforms (pass their displacement as
    /// <c>carry</c>). Collision is <see cref="TileMapView.MoveBox"/>, so it never tunnels.
    /// </summary>
    public static class PlatformerMotor
    {
        public static float2 Step(in PlatformerTuning t, ref PlatformerState s, in PlatformerInput input, float dt,
            in TileMapView map, float2 position, float2 half, byte oneWay, float2 carry = default)
        {
            s.JumpedThisStep = false;
            s.Buffer = input.JumpPressed ? t.JumpBuffer : math.max(s.Buffer - dt, 0f);
            s.Coyote = s.Grounded ? t.CoyoteTime : math.max(s.Coyote - dt, 0f);

            float target = math.clamp(input.MoveX, -1f, 1f) * t.RunSpeed;
            float accel = (s.Grounded ? t.GroundAccel : t.AirAccel) * dt;
            s.Velocity.x = s.Velocity.x + math.clamp(target - s.Velocity.x, -accel, accel);

            if (s.Buffer > 0f && s.Coyote > 0f)
            {
                s.Velocity.y = t.JumpVelocity;
                s.Buffer = s.Coyote = 0f;
                s.Grounded = false;
                s.JumpedThisStep = true;
            }
            else if (s.Buffer > 0f && !s.Grounded && s.WallSide != 0 && t.WallSlideSpeed > 0f)
            {
                s.Velocity = new float2(-s.WallSide * t.WallJump.x, t.WallJump.y);
                s.Buffer = 0f;
                s.WallSide = 0;
                s.JumpedThisStep = true;
            }
            // Releasing jump while rising cuts the jump short.
            if (s.JumpHeld && !input.JumpHeld && s.Velocity.y > 0f) s.Velocity.y *= t.JumpCut;
            s.JumpHeld = input.JumpHeld;

            float g = t.Gravity * (s.Velocity.y < 0f ? t.FallGravityScale : 1f);
            s.Velocity.y = math.max(s.Velocity.y - g * dt, -t.MaxFall);
            bool pushingWall = s.WallSide != 0 && math.sign(input.MoveX) == s.WallSide;
            if (pushingWall && t.WallSlideSpeed > 0f && s.Velocity.y < -t.WallSlideSpeed) s.Velocity.y = -t.WallSlideSpeed;

            position = map.MoveBox(position, half, s.Velocity * dt + carry, oneWay, out var contacts);
            s.Contacts = contacts;
            if ((contacts & BoxContacts.Ground) != 0 && s.Velocity.y < 0f) s.Velocity.y = 0f;
            if ((contacts & BoxContacts.Ceiling) != 0 && s.Velocity.y > 0f) s.Velocity.y = 0f;
            if ((contacts & (BoxContacts.Left | BoxContacts.Right)) != 0) s.Velocity.x = 0f;
            s.Grounded = s.Velocity.y <= 0f && ((contacts & BoxContacts.Ground) != 0 || map.BoxGrounded(position, half, oneWay));
            s.WallSide = s.Grounded ? (sbyte)0
                : (contacts & BoxContacts.Right) != 0 || (input.MoveX > 0f && Touches(map, position, half, 1, oneWay)) ? (sbyte)1
                : (contacts & BoxContacts.Left) != 0 || (input.MoveX < 0f && Touches(map, position, half, -1, oneWay)) ? (sbyte)-1 : (sbyte)0;
            return position;
        }

        static bool Touches(in TileMapView map, float2 position, float2 half, int side, byte oneWay)
        {
            map.MoveBox(position, half, new float2(side * 0.02f, 0f), oneWay, out var c);
            return (c & (side > 0 ? BoxContacts.Right : BoxContacts.Left)) != 0;
        }

        /// <summary>
        /// Lands a falling box on top of another box (moving platform, enemy head for stomps) when it crossed
        /// that box's top during this step. Returns true and snaps the box onto it.
        /// </summary>
        public static bool LandOn(ref float2 position, float2 half, float previousBottom, float2 boxCenter, float2 boxHalf)
        {
            float top = boxCenter.y + boxHalf.y;
            float bottom = position.y - half.y;
            if (previousBottom < top - 1e-3f || bottom > top) return false;
            if (math.abs(position.x - boxCenter.x) >= half.x + boxHalf.x) return false;
            position.y = top + half.y;
            return true;
        }
    }
}
