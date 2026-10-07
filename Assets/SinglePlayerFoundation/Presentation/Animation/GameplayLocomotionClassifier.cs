using Unity.Mathematics;

namespace SPF.Presentation.Animation
{
    /// <summary>Reusable speed hysteresis for skeletal or sprite backends. The caller chooses units:
    /// speed and thresholds must all be world units/s or all model units/s. No pose, clock or simulation writes.</summary>
    public struct GameplayLocomotionClassifier
    {
        public GameplayLocomotionState State;
        public bool RunLatched;
        public GameplayLocomotionState Step(float speed,float runEnter,float runExit,bool grounded=true,bool enabled=true,
            float idleEnter=.08f,float idleExit=.045f)
        {
            speed=math.isfinite(speed)?math.max(0,speed):0;
            runEnter=math.max(.001f,runEnter);runExit=math.clamp(runExit,0,runEnter);
            bool moving=enabled&&speed>(State==GameplayLocomotionState.Idle?idleEnter:idleExit);
            RunLatched=moving&&(RunLatched?speed>runExit:speed>runEnter);
            State=!enabled?GameplayLocomotionState.Idle:!grounded?GameplayLocomotionState.Air:!moving?GameplayLocomotionState.Idle:
                RunLatched?GameplayLocomotionState.Run:GameplayLocomotionState.Walk;
            return State;
        }
    }
}
