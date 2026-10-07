using System;
using SPF.Contracts.Weapons;
using SPF.L2.Combat;
using Unity.Mathematics;

namespace SPF.L2.Weapons
{
    /// <summary>Immutable session-baked content. IDs are stable authored content keys, not enum indices.
    /// Durations are fixed ticks at the mode's tick rate. Offsets follow WeaponViewState's convention.</summary>
    [Serializable]
    public struct WeaponProfile
    {
        public int ContentId, VisualId, DurationTicks, EquipTicks, ReleaseTick, ProjectileLifeTicks;
        public string Name;
        public WeaponActionFamily Family;
        // Active is the committed strike/follow-through interval. Melee damage is sampled once at ContactWindow.
        public ActionWindow Active, Cancel;
        public ActionWindow ContactWindow => new ActionWindow(Active.From, Active.From + 1);
        public float Damage, Knockback, Reach, Radius, ProjectileSpeed;
        public float2 GripOffset, SecondaryGripOffset, MuzzleOffset;
        public bool Ranged => Family == WeaponActionFamily.Cast || Family == WeaponActionFamily.Draw;
        public void Validate()
        {
            if (ContentId <= 0 || VisualId <= 0 || string.IsNullOrEmpty(Name) || Family < WeaponActionFamily.Slash || Family > WeaponActionFamily.Draw ||
                DurationTicks < 2 || DurationTicks > 3600 || EquipTicks < 1 || EquipTicks > 600 ||
                Active.From < 1 || Active.Until <= Active.From || Active.Until > DurationTicks ||
                Cancel.From < 0 || Cancel.Until < Cancel.From || Cancel.Until > Active.From ||
                !Finite(Damage) || Damage < 0 || !Finite(Knockback) || Knockback < 0 || !Finite(Reach) || Reach <= 0 ||
                !Finite(Radius) || Radius < 0 || !math.all(math.isfinite(GripOffset)) || !math.all(math.isfinite(SecondaryGripOffset)) || !math.all(math.isfinite(MuzzleOffset)) || MuzzleOffset.y < 0 ||
                (Ranged && (ReleaseTick < Active.From || ReleaseTick >= Active.Until || ProjectileLifeTicks < 1 || ProjectileLifeTicks > 3600 || !Finite(ProjectileSpeed) || ProjectileSpeed <= 0)))
                throw new ArgumentException("Invalid authored weapon profile.");
        }
        static bool Finite(float f) => !float.IsNaN(f) && !float.IsInfinity(f);
    }

    public static class WeaponProfiles
    {
        public const int Blade = 1001, Sword = 1002, Staff = 1003, Bow = 1004;
        public static WeaponProfile[] CreateDefaults(int tickRate)
        {
            if (tickRate < 10 || tickRate > 240) throw new ArgumentOutOfRangeException(nameof(tickRate));
            return new[] {
                Build(Blade,"BLADE",WeaponActionFamily.Slash,tickRate,.62f,.19f,.34f,18,1.65f,.30f,0),
                Build(Sword,"SWORD",WeaponActionFamily.Thrust,tickRate,.76f,.28f,.40f,27,2.12f,.18f,0),
                Build(Staff,"STAFF",WeaponActionFamily.Cast,tickRate,.90f,.36f,.40f,24,1.25f,.21f,12),
                Build(Bow,"BOW",WeaponActionFamily.Draw,tickRate,1.05f,.62f,.66f,36,.94f,.10f,21)
            };
        }
        static WeaponProfile Build(int id,string name,WeaponActionFamily family,int rate,float duration,float from,float until,float damage,float reach,float radius,float speed)
        {
            int active=(int)math.ceil(from*rate),end=math.max(active+1,(int)math.ceil(until*rate));
            bool bow=family==WeaponActionFamily.Draw,staff=family==WeaponActionFamily.Cast;
            return new WeaponProfile { ContentId=id,VisualId=id,Name=name,Family=family,DurationTicks=(int)math.ceil(duration*rate),
                EquipTicks=math.max(1,(int)math.ceil(.22f*rate)),Active=new ActionWindow(active,end),Cancel=new ActionWindow(0,active),ReleaseTick=active,
                Damage=damage,Knockback=staff?2:bow?3:4,Reach=reach,Radius=radius,ProjectileSpeed=speed,ProjectileLifeTicks=rate*2,
                GripOffset=new float2(bow?.65f:staff?.45f:.58f,bow?1.42f:staff?1.30f:1.25f),
                SecondaryGripOffset=new float2(bow?.10f:.23f,bow?1.43f:1.18f),
                MuzzleOffset=new float2(reach,bow?1.42f:staff?1.65f:1.25f) };
        }
    }
}
