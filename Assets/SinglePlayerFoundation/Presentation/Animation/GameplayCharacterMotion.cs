using SPF.Contracts;
using SPF.L1.Skeleton;
using Unity.Collections;
using Unity.Mathematics;

namespace SPF.Presentation.Animation
{
    public enum GameplayCharacterState : byte { Idle, Run, Attack, Hit, Death, Recovery }
    public enum GameplayCharacterAction : byte { Punch, Kick, Cast }

    /// <summary>Read-only view contract. Ground is the contact plane; Root includes any authoritative jump
    /// height. Handle must be stable across swap-back/reordering. Timing and aim are inputs, never damage authority.</summary>
    public struct GameplayCharacterInput
    {
        public EntityHandle Handle;
        public float2 Root, Ground, Velocity, AimTarget;
        public float Facing, Scale, Phase, Flash, Depth;
        public float4 Tint;
        public GameplayCharacterState State;
        public GameplayCharacterAction Action;
        public int Kind;
        public bool Aim, Teleported;
    }

    /// <summary>One fixed slot of purely visual state; no simulation reference or RNG.</summary>
    public struct GameplayCharacterMotion
    {
        public FootPlantState FarFoot, NearFoot;
        public SmoothedAimState Aim;
        public float2 PreviousRoot;
        public float Phase, Run, Attack, Hit, Death, Facing, Scale;
        public bool Initialized;

        public void Step(in GameplayCharacterInput input, float dt)
        {
            dt = math.clamp(dt, 0, .1f);
            float scale = math.clamp(input.Scale, .1f, 4f);
            float facing = input.Facing < 0 ? -1f : 1f;
            float2 ground = input.Ground / scale;
            bool reset = !Initialized || input.Teleported || math.distancesq(input.Root, PreviousRoot) > scale * scale * 2.25f || math.abs(Scale-scale) > .001f;
            if (reset)
            {
                this = default; Initialized = true; Facing = facing; Scale = scale;
                Phase = ((uint)input.Handle.Index * 37u % 97) / 97f;
                NaturalMotion.InitializeFoot(ref FarFoot, ground, ground + new float2(-.13f, .075f), 0);
                NaturalMotion.InitializeFoot(ref NearFoot, ground, ground + new float2(.13f, .075f), .5f);
            }
            float response = 1f - math.exp(-18f * dt);
            Run = math.lerp(Run, input.State == GameplayCharacterState.Run ? 1 : 0, response);
            float attack = input.State == GameplayCharacterState.Attack || input.State == GameplayCharacterState.Recovery ? Strike(input.Phase) : 0;
            Attack = math.lerp(Attack, attack, 1f - math.exp(-35f * dt));
            Hit = math.lerp(Hit, input.State == GameplayCharacterState.Hit ? 1 : 0, response);
            Death = math.lerp(Death, input.State == GameplayCharacterState.Death ? 1 : 0, 1f-math.exp(-7f*dt));
            float speed = math.length(input.Velocity) / scale;
            float cadence = math.clamp(speed / .65f, 1, 7);
            // Changing direction changes only the rendered facing, never a stored world-space plant.
            Facing = facing;
            if (Run > .03f && speed > .03f)
            {
                float step = dt * cadence;
                int steps = math.max(1, (int)math.ceil(step / .2f));
                float2 oldGround = FarFoot.PreviousRoot;
                for (int k=0;k<steps;k++)
                {
                    float2 root = math.lerp(oldGround,ground,(k+1f)/steps);
                    NaturalMotion.StepFoot(ref FarFoot,root,input.Velocity/(scale*cadence),-.13f,step/steps,ground.y+.075f);
                    NaturalMotion.StepFoot(ref NearFoot,root,input.Velocity/(scale*cadence),.13f,step/steps,ground.y+.075f);
                }
                Phase = math.frac(Phase + step / NaturalMotion.Period);
            }
            else
            {
                // Smoothly settle an interrupted swing into a neutral stance. Stationary stance remains exact.
                Settle(ref FarFoot, ground, -.13f, dt); Settle(ref NearFoot,ground,.13f,dt);
                Phase = math.frac(Phase + dt * .16f);
            }
            float2 target = input.Aim ? input.AimTarget : input.Root + new float2(facing * math.lerp(.34f,.83f,math.max(0,Attack)), math.lerp(1.5f,1.65f,math.max(0,Attack))) * scale;
            NaturalMotion.SmoothAim(ref Aim,target,dt,24);
            PreviousRoot=input.Root;
        }

        static void Settle(ref FootPlantState foot,float2 ground,float offset,float dt)
        {
            if (!foot.InStance || math.distancesq(foot.Position,ground+new float2(offset,.075f)) > .35f*.35f)
            {
                foot.Position=math.lerp(foot.Position,ground+new float2(offset,.075f),1f-math.exp(-16f*dt));
                foot.Plant=foot.Position;
                if(math.distancesq(foot.Position,ground+new float2(offset,.075f))<.00001f)
                    NaturalMotion.InitializeFoot(ref foot,ground,ground+new float2(offset,.075f));
            }
            foot.PreviousRoot=ground;
        }

        /// <summary>Normalized authoritative action timeline: wind-up, contact, then recovery.</summary>
        public static float Strike(float phase)
        {
            phase=math.saturate(phase);
            if(phase<.2f)return -.2f*NaturalMotion.Ease(phase/.2f);
            if(phase<.42f)return math.lerp(-.2f,1,NaturalMotion.Ease((phase-.2f)/.22f));
            return 1-NaturalMotion.Ease((phase-.42f)/.58f);
        }

        public static void Pose(in SkeletonView rig,NativeArray<BoneLocal> local,NativeArray<BoneWorld> world,
            in GameplayCharacterInput input,in GameplayCharacterMotion motion,int at)
        {
            float scale=motion.Scale;
            float2 far=motion.FarFoot.Position*scale,near=motion.NearFoot.Position*scale;
            // Feet leave the ground with the authoritative root height. Ground sorting/shadows stay below.
            float height=math.max(0,input.Root.y-input.Ground.y);
            far.y+=height;near.y+=height;
            float strike=math.max(0,motion.Attack);
            if(input.Action==GameplayCharacterAction.Kick)
                near=math.lerp(near,input.Root+new float2(motion.Facing*.86f,.98f)*scale,strike);
            NaturalCharacterRig.Pose(rig,local,world,input.Root,motion.Facing,scale,motion.Phase,far,near,
                true,motion.Aim.Target,motion.Attack,at);
            var torso=local[at+NaturalCharacterRig.Torso];torso.Rotation+=motion.Hit*.24f;local[at+NaturalCharacterRig.Torso]=torso;
            var head=local[at+NaturalCharacterRig.Head];head.Rotation-=motion.Hit*.15f;local[at+NaturalCharacterRig.Head]=head;
            // A relaxed rear arm versus readable guarded front hand; stance/run changes ease through Run.
            var arm=local[at+NaturalCharacterRig.FarArm];arm.Rotation+=.18f*(1-motion.Run);local[at+NaturalCharacterRig.FarArm]=arm;
            Skeletal.ToWorld(rig,local,input.Root,motion.Facing,scale,world,at,at);
        }
    }
}
