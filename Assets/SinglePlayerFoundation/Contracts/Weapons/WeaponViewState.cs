using System;
using Unity.Mathematics;

namespace SPF.Contracts.Weapons
{
    // Mechanics/pose families, never an inventory enum. New authored content reuses a family.
    public enum WeaponActionFamily : byte { None, Slash, Thrust, Cast, Draw }
    public enum WeaponStage : byte { Idle, Windup, Active, Recovery, Equipping }
    [Flags] public enum WeaponCueKind : byte { None = 0, Begin = 1, Release = 2, Impact = 4, Equip = 8, Cancel = 16 }

    /// <summary>Simulation-owned read-only view. Offsets are (forward distance along AimDirection,
    /// screen-up height), in model units before actor Scale. Render point = Root + aim * offset.x *
    /// Scale + (0,offset.y*Scale). Belt adapters project ground aim into screen coordinates (do not
    /// normalize again). Ground-plane collision uses the same forward distance; height is separate.
    /// Phase/markers describe the whole authored action. Never smooth Phase or use a view for damage.</summary>
    public struct WeaponViewState
    {
        public int ContentId, VisualId, PendingContentId;
        public WeaponActionFamily Family;
        public WeaponStage Stage;
        public float Phase, StagePhase, ContactPhase, ActiveEndPhase, ReleasePhase;
        public float2 AimDirection, GripOffset, SecondaryGripOffset, MuzzleOffset;
        public float Reach, Radius;
        public uint ActionPulse, CueSequence;
        public WeaponCueKind Cues;
        public bool Equipped => ContentId != 0;
    }

    /// <summary>Stable monotonically sequenced presentation notification. Position is in the host ground plane and Height is separate until the renderer adapter
    /// projects the copy passed to presentation. Multiple views can independently consume sequence numbers.</summary>
    public struct WeaponCue
    {
        public EntityHandle Owner;
        public uint Sequence, ActionPulse;
        public int ContentId, VisualId;
        public WeaponCueKind Kind;
        public float2 Position, Direction;
        public float Height;
    }
}
