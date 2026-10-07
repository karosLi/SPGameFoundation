using SPF.Contracts;
using SPF.L1.Skeleton;
using SPF.L1.Spatial;
using SPF.L2.Combat;
using SPF.L2.Weapons;
using SPF.Runtime.Scheduling;
using SPF.Runtime.World;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace BrawlerFoundation
{
    public static class BwWeapons
    {
        public static readonly ResourceKey<WeaponRuntime> Key=new ResourceKey<WeaponRuntime>("Bw.Weapons.V1");
        public const int SwitchButton=4;
        public const float ActorScale=.9f;
        public static void Advance(SimWorld world,in InputFrame input)
        {
            var runtime=world.Resource(Key);var game=world.Resource(BwKeys.Game);var info=world.Column(BwKeys.Info);
            int player=-1;for(int i=0;i<world.Table(BwKeys.Fighter).Count;i++)if(info[i].Team==0){player=i;break;}
            if(player<0){runtime.CancelAll();return;}
            var f=info[player];bool alive=f.Hp>0&&f.State!=FighterState.KO;
            if(game.Flow!=BwFlow.Fighting){runtime.CancelAll();return;}
            runtime.Owner=world.Table(BwKeys.Fighter).Handles[player];
            if(alive&&input.WasPressed(SwitchButton))runtime.Cycle();
            bool kick=f.State==FighterState.Attack&&f.Attack==AttackKind.Kick;
            uint previousPulse=runtime.Equipment.Timeline.PulseId;
            runtime.Step(true,alive,f.State==FighterState.Hit||kick,input.WasPressed(BwButton.Punch),input.IsHeld(BwButton.Punch),
                math.lengthsq(input.Aim)>.001f?input.Aim:new float2(f.Facing,0),world.Column(BwBeltKeys.Ground)[player],world.Column(BwBeltKeys.Motion)[player].Height,ActorScale);
            if(runtime.Equipment.Timeline.PulseId!=previousPulse)world.Resource(BwMobileSkills.Key).TryActivate(0,true);
            if(alive&&f.State!=FighterState.Hit&&!kick)
            {
                if(runtime.Busy){f.State=FighterState.Attack;f.Attack=AttackKind.None;f.StateTime=(float)runtime.Equipment.Timeline.Tick/runtime.TickRate;f.Facing=runtime.Equipment.Aim.x<0?-1:1;}
                else if(f.State==FighterState.Attack&&f.Attack==AttackKind.None){f.State=FighterState.Idle;f.StateTime=0;}
                info[player]=f;
            }
        }
    }
}
namespace BrawlerFoundation.Systems
{
    /// <summary>Thin host adapter: existing belt ground grid, stable handles, hit reactions and loot.
    /// Only the opt-in player's primary attack uses shared equipment; enemy AI and other skills stay in
    /// the existing systems. Projectile collision chooses earliest TOI, breaking ties by stable handle.</summary>
    sealed class BeltWeaponSystem:SimSystemBase
    {
        public override SimPhase Phase=>SimPhase.Resolve;
        public override int Order=>-10;
        public override void Declare(AccessDeclaration a)=>a.Write(BwWeapons.Key).Read(BwKeys.Rig).Read(BwKeys.Fighter).Write(BwKeys.Info).Write(BwKeys.Anim).Write(BwBeltKeys.Motion).Read(BwBeltKeys.Ground).Write(BwBeltKeys.State).Write(BwKeys.Feedback);
        public override JobHandle OnTick(in SimContext context,JobHandle dependency)
        {
            dependency.Complete();var world=context.World;var game=world.Resource(BwKeys.Game);if(game.Flow!=BwFlow.Fighting)return dependency;
            var weapons=world.Resource(BwWeapons.Key);var belt=world.Resource(BwBeltKeys.State);belt.Rebuild(world);
            int player=-1;var info=world.Column(BwKeys.Info);for(int i=0;i<world.Table(BwKeys.Fighter).Count;i++)if(info[i].Team==0){player=i;break;}
            if(player<0||info[player].Hp<=0)return dependency;
            var visitor=new ContactVisitor{World=world,Weapons=weapons,Info=info,Ground=world.Column(BwBeltKeys.Ground),Motion=world.Column(BwBeltKeys.Motion),Handles=world.Table(BwKeys.Fighter).Handles,Player=player};
            var p=weapons.Current;
            if(weapons.MeleeActive)
            {
                visitor.Scope=0;visitor.Projectile=false;visitor.Profile=p;visitor.Start=visitor.Ground[player]+weapons.Equipment.Aim*(p.GripOffset.x*BwWeapons.ActorScale);
                visitor.End=visitor.Ground[player]+weapons.Equipment.Aim*(p.MuzzleOffset.x*BwWeapons.ActorScale);visitor.Height=visitor.Motion[player].Height+p.MuzzleOffset.y*BwWeapons.ActorScale;
                visitor.Direction=weapons.Equipment.Aim;visitor.Radius=p.Radius*BwWeapons.ActorScale;
                CombatShapes.BeamBounds(visitor.Start,visitor.End,visitor.Radius+BwRules.BodyHalfWidth,out var min,out var max);belt.Grid.AsReader().QueryCells(min,max,ref visitor);
            }
            for(int i=0;i<weapons.Projectiles.Length;i++)
            {
                var shot=weapons.Projectiles[i];if(!shot.Active)continue;
                visitor.Projectile=true;visitor.Scope=i+1;visitor.Profile=weapons.Profile(shot.ContentId);visitor.Start=shot.Previous;visitor.End=shot.Position;visitor.Height=shot.Height;visitor.Direction=shot.Direction;visitor.Radius=visitor.Profile.Radius*shot.Scale;visitor.BestRow=-1;visitor.BestFraction=2;
                CombatShapes.BeamBounds(visitor.Start,visitor.End,visitor.Radius+BwRules.BodyHalfWidth,out var min,out var max);belt.Grid.AsReader().QueryCells(min,max,ref visitor);
                if(visitor.BestRow>=0){visitor.Hit(visitor.BestRow,math.lerp(visitor.Start,visitor.End,visitor.BestFraction));weapons.StopProjectile(i);}
            }
            weapons.ExpireProjectiles();return dependency;
        }
        struct ContactVisitor:IGridVisitor
        {
            public SimWorld World;public WeaponRuntime Weapons;public NativeArray<FighterInfo> Info;public NativeArray<float2> Ground;public NativeArray<BwBeltMotion> Motion;public NativeArray<EntityHandle> Handles;
            public WeaponProfile Profile;public float2 Start,End,Direction;public float Height,Radius,BestFraction;public int Player,Scope,BestRow;public bool Projectile;
            public bool Visit(in GridEntry e)
            {
                int row=e.Owner;var f=Info[row];if(f.Team==0||f.State==FighterState.KO||Height+Radius<Motion[row].Height+BwRules.HurtBottom||Height-Radius>Motion[row].Height+BwRules.HurtTop)return true;
                if(!CombatSweep.PointCircle(Start,End,Ground[row],Radius+BwRules.BodyHalfWidth,out float fraction)||Weapons.CheckHit(Scope,Handles[row])!=HitRecordResult.Added)return true;
                if(Projectile){if(fraction<BestFraction||(fraction==BestFraction&&(BestRow<0||Handles[row].Index<Handles[BestRow].Index))){BestFraction=fraction;BestRow=row;}}
                else Hit(row,Ground[row]);return true;
            }
            public void Hit(int row,float2 contact)
            {
                var f=Info[row];var m=Motion[row];var anim=World.Column(BwKeys.Anim);var a=anim[row];var game=World.Resource(BwKeys.Game);var belt=World.Resource(BwBeltKeys.State);var rig=World.Resource(BwKeys.Rig);
                if(!Weapons.RecordHit(Scope,Handles[row],contact,Height))return;
                f.Hp=math.max(0,f.Hp-Profile.Damage);f.Flash=1;f.VelocityX=Direction.x*Profile.Knockback;f.StateTime=0;f.Attack=AttackKind.None;if(math.abs(Direction.x)>.0001f)f.Facing=Direction.x<0?1:-1;m.BufferedAttack.Clear();m.ComboGraceTicks=0;
                if(f.Hp<=0){f.State=FighterState.KO;a.Play(rig.KO,.05f,restart:true);game.Kos++;game.Score+=100;belt.TryDrop(Ground[row],BwBeltDropKind.Coin,10);if((game.Kos&1)==0)belt.TryDrop(Ground[row],BwBeltDropKind.Heal,18);}
                else{f.State=FighterState.Hit;a.Play(rig.Hit,.04f,restart:true);}
                game.Score+=(int)Profile.Damage;game.Version++;Info[row]=f;Motion[row]=m;anim[row]=a;
                World.Resource(BwKeys.Feedback).TryAdd(new BwFeedback{Kind=f.Hp<=0?BwFeedbackKind.KO:BwFeedbackKind.Hit,Position=BwBeltRules.Project(contact,Height),Value=Profile.Damage});
            }
        }
    }
}
