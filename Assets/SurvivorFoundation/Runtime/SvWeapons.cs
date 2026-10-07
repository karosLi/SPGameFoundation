using SPF.Contracts;
using SPF.L1.Spatial;
using SPF.L2.Combat;
using SPF.L2.Skills;
using SPF.L2.Weapons;
using SPF.Runtime.Scheduling;
using SPF.Runtime.World;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace SurvivorFoundation
{
    public static class SvWeapons
    {
        public static readonly ResourceKey<WeaponRuntime> Key=new ResourceKey<WeaponRuntime>("Sv.Weapons.V1");
        public const int AttackButton=2,SwitchButton=3;
        public const float ActorScale=.66f;
        public static SkillSlots CreateSkills()=>new SkillSlots(
            new SkillSlotDefinition(11,2,SkillActivation.Tap,90,2),new SkillSlotDefinition(12,3,SkillActivation.AimRelease,120,2),
            new SkillSlotDefinition(21,0,SkillActivation.Hold,1),new SkillSlotDefinition(22,1,SkillActivation.Tap,7));
        public static void Advance(SimWorld world,in InputFrame input)
        {
            var game=world.Resource(SvKeys.Game);var weapons=world.Resource(Key);
            if(game.Flow!=SvFlow.Playing){if(game.Flow!=SvFlow.LevelUp)weapons.CancelAll();return;}
            if(input.WasPressed(SwitchButton)&&world.Resource(SvMobileSkills.Key).TryActivate(3,game.Hp>0))weapons.Cycle();
            weapons.Owner=new EntityHandle(-1,1);
            var p=weapons.Current;
            var near=new Nearest{Origin=game.Hero,Best=p.Ranged?13f*13f:(p.Reach*ActorScale+.7f)*(p.Reach*ActorScale+.7f)};
            world.Resource(SvKeys.EnemyGrid).AsReader().Query(game.Hero,p.Ranged?13:p.Reach*ActorScale+.7f,ref near);
            float2 aim=near.Found?near.Target-game.Hero:game.Facing;
            if(math.lengthsq(input.Aim)>.001f)aim=input.Aim;
            // Auto-strike within this weapon's reach preserves the portrait horde control scheme.
            // The dedicated attack control can also draw/fire when there is no nearby target.
            weapons.Step(true,game.Hp>0,false,input.WasPressed(AttackButton),near.Found||input.IsHeld(AttackButton),aim,game.Hero,0,ActorScale);
            if(weapons.Busy)game.Facing=weapons.Equipment.Aim;
            if(input.IsHeld(AttackButton)||input.WasPressed(AttackButton))world.Resource(SvMobileSkills.Key).TryActivate(2,game.Hp>0);
        }
        struct Nearest:IGridVisitor
        {
            public float2 Origin,Target;public float Best;public bool Found;
            public bool Visit(in GridEntry e){float d=math.distancesq(Origin,e.Position);if(d<Best||(d==Best&&(!Found||e.Position.x<Target.x||(e.Position.x==Target.x&&e.Position.y<Target.y)))){Best=d;Target=e.Position;Found=true;}return true;}
        }
    }
}
namespace SurvivorFoundation.Systems
{
    /// <summary>Shared equipment plugs into the existing grid and bounded damage queue. A full damage
    /// queue does not record the target; projectile contact consumes that projectile deterministically.
    /// No extra all-enemy update loop, GameObjects, or per-shot managed allocation.</summary>
    sealed class WeaponCombatSystem:SimSystemBase
    {
        public override SimPhase Phase=>SimPhase.Collision;
        public override int Order=>8;
        public override void Declare(AccessDeclaration a)=>a.Write(SvWeapons.Key).Read(SvKeys.Enemy).Read(SvKeys.Position).Read(SvKeys.PrevPosition).Read(SvKeys.Info).Read(SvKeys.EnemyGrid).Write(SvKeys.Hits);
        public override JobHandle OnTick(in SimContext context,JobHandle dependency)
        {
            dependency.Complete();var w=context.World;var game=w.Resource(SvKeys.Game);if(game.Flow!=SvFlow.Playing||game.Hp<=0)return dependency;
            var weapons=w.Resource(SvWeapons.Key);var grid=w.Resource(SvKeys.EnemyGrid).AsReader();
            var visitor=new Contacts{Weapons=weapons,Handles=w.Table(SvKeys.Enemy).Handles,Info=w.Column(SvKeys.Info),Positions=w.Column(SvKeys.Position),Previous=w.Column(SvKeys.PrevPosition),Hits=w.Resource(SvKeys.Hits),Might=SvRules.Might(game)};
            var p=weapons.Current;
            if(weapons.MeleeActive)
            {
                visitor.Scope=0;visitor.Start=game.Hero+weapons.Equipment.Aim*p.GripOffset.x*SvWeapons.ActorScale;visitor.End=game.Hero+weapons.Equipment.Aim*p.MuzzleOffset.x*SvWeapons.ActorScale;
                visitor.Radius=p.Radius*SvWeapons.ActorScale;visitor.Damage=p.Damage;visitor.Height=p.MuzzleOffset.y*SvWeapons.ActorScale;visitor.Projectile=false;
                CombatShapes.BeamBounds(visitor.Start,visitor.End,visitor.Radius+grid.MaxEntryRadius,out var min,out var max);grid.QueryCells(min,max,ref visitor);
            }
            for(int i=0;i<weapons.Projectiles.Length;i++)
            {
                var shot=weapons.Projectiles[i];if(!shot.Active)continue;
                visitor.Scope=i+1;visitor.Start=shot.Previous;visitor.End=shot.Position;visitor.Radius=weapons.Profile(shot.ContentId).Radius*shot.Scale;visitor.Damage=shot.Damage;visitor.Height=shot.Height;visitor.Projectile=true;visitor.BestRow=-1;visitor.BestFraction=2;
                // Enemy motion is bounded by authored speed each tick. Expand broadphase so a target
                // that crossed the segment and ended outside the beam still reaches swept narrowphase.
                var config=w.Resource(SvKeys.Config);float speed=0,radius=0;
                for(int k=0;k<config.Enemies.Length;k++){speed=math.max(speed,config.Enemies[k].Speed*1.15f);radius=math.max(radius,config.Enemies[k].Radius*1.4f);}
                float movePad=(speed+radius*96f)/weapons.TickRate;
                if(config.Settings.Variant==SvVariant.GuardBeacon)movePad+=2*(config.Settings.BeaconRadius+radius);
                CombatShapes.BeamBounds(visitor.Start,visitor.End,visitor.Radius+grid.MaxEntryRadius+movePad,out var min,out var max);grid.QueryCells(min,max,ref visitor);
                if(visitor.BestRow>=0){visitor.Hit(visitor.BestRow,math.lerp(visitor.Start,visitor.End,visitor.BestFraction));weapons.StopProjectile(i);}
            }
            weapons.ExpireProjectiles();return dependency;
        }
        struct Contacts:IGridVisitor
        {
            public WeaponRuntime Weapons;public NativeArray<EntityHandle> Handles;public NativeArray<EnemyInfo> Info;public NativeArray<float2> Positions,Previous;public EventQueue<SvHit> Hits;
            public float2 Start,End;public float Radius,Damage,Height,Might,BestFraction;public int Scope,BestRow;public bool Projectile;
            public bool Visit(in GridEntry e)
            {
                int row=e.Owner;if(Info[row].Has(EnemyFlags.Dead))return true;
                bool overlap=Projectile?CombatSweep.Circles(Start,End,Radius,Previous[row],Positions[row],Info[row].Radius,out float fraction):CombatSweep.PointCircle(Start,End,Positions[row],Radius+Info[row].Radius,out fraction);
                if(!overlap||Weapons.CheckHit(Scope,Handles[row])!=HitRecordResult.Added)return true;
                if(Projectile){if(fraction<BestFraction||(fraction==BestFraction&&(BestRow<0||Handles[row].Index<Handles[BestRow].Index))){BestFraction=fraction;BestRow=row;}}
                else Hit(row,Positions[row]);return true;
            }
            public void Hit(int row,float2 point)
            {if(Hits.TryAdd(new SvHit{Target=row,Damage=Damage*Might}))Weapons.RecordHit(Scope,Handles[row],point,Height);else if(Weapons.RejectedHits<int.MaxValue)Weapons.RejectedHits++;}
        }
    }
}
