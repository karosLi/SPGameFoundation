using SPF.Contracts;
using SPF.Presentation;
using SPF.Presentation.Audio;
using SPF.Runtime.Composition;
using SPF.Runtime.Session;
using SPF.Shell.CameraRig;
using SPF.Shell.Performance;
using ShooterFoundation.Presentation;
using Unity.Mathematics;
using UnityEngine;

namespace ShooterFoundation.Game
{
    [DisallowMultipleComponent]
    public sealed class ShooterGameBootstrap : MonoBehaviour
    {
        [SerializeField] ShooterConfig m_Config;
        [SerializeField] uint m_Seed = 17;
        bool m_OwnsConfig; RenderTier? m_Tier;
        ModeDefinition m_Mode; GameplayModuleAsset m_Module;
        SoundPlayer m_Sound; int m_Shot,m_Destroyed,m_Pickup,m_Hurt,m_Upgrade;
        readonly SPF.Shell.Input.KeyboardInputSource m_Keyboard = new SPF.Shell.Input.KeyboardInputSource();
        public SessionHost Host { get; private set; }
        public SimSession Session => Host != null ? Host.Session : null;
        public ShooterState State => Session?.World.Resource(ShooterKeys.State);
        public FrameGovernor Governor { get; private set; }
        public FollowCamera2D CameraRig { get; private set; }
        public ShooterRenderer Renderer { get; private set; }
        public ShooterHud Hud { get; private set; }
        public bool AutoPlay { get; set; }
        public static ShooterGameBootstrap Create(RenderTier? tier = null, ShooterConfig config = null, uint seed = 17)
        {
            var go = new GameObject("ShooterGame"); go.SetActive(false); var game=go.AddComponent<ShooterGameBootstrap>();
            game.m_Tier=tier; game.m_Config=config; game.m_Seed=seed; go.SetActive(true); return game;
        }
        void Awake()
        {
            if(m_Config==null) { m_Config=ShooterConfig.CreateDefault();m_OwnsConfig=true; }
            m_Mode=ShooterMode.Create(m_Config,out m_Module);
            var simulation=new GameObject("Simulation"); simulation.transform.SetParent(transform,false); Host=simulation.AddComponent<SessionHost>();Host.Initialize(m_Mode,m_Seed);
            Governor=gameObject.AddComponent<FrameGovernor>();Governor.SetFrameRates(60,30);Governor.ThrottleWhenIdle=true;
            var cameraObject=UnityEngine.Camera.main!=null?UnityEngine.Camera.main.gameObject:new GameObject("Main Camera",typeof(UnityEngine.Camera));
            cameraObject.tag="MainCamera";var cam=cameraObject.GetComponent<UnityEngine.Camera>();cam.clearFlags=CameraClearFlags.SolidColor;cam.backgroundColor=new Color(0.022f,0.048f,0.087f);
            CameraRig=cameraObject.GetComponent<FollowCamera2D>();if(CameraRig==null)CameraRig=cameraObject.AddComponent<FollowCamera2D>();
            CameraRig.Target=float2.zero;CameraRig.Size=9.6f;CameraRig.Zoom=100f;CameraRig.UpdateTarget=FitCamera;
            if(cameraObject.GetComponent<AudioListener>()==null)cameraObject.AddComponent<AudioListener>();
            var view=new GameObject("ShooterRenderer");view.transform.SetParent(transform,false);Renderer=view.AddComponent<ShooterRenderer>();Renderer.Host=Host;Renderer.Camera=CameraRig;Renderer.ForcedTier=m_Tier;
            Governor.LevelChanged+=Renderer.SetQuality;Renderer.SetQuality(Governor.Level);
            m_Sound=SoundPlayer.Create(transform,8);
            m_Shot=m_Sound.Register("shot",SfxDef.Create(SfxWave.Triangle,850f,500f,0.045f,0.10f),maxVoices:2,minInterval:0.08f);
            m_Destroyed=m_Sound.Register("burst",SfxDef.Create(SfxWave.Noise,260f,60f,0.16f,0.25f).WithNoise(1f,0.4f),maxVoices:2,minInterval:0.05f);
            m_Pickup=m_Sound.Register("pickup",SfxDef.Create(SfxWave.Triangle,1100f,1600f,0.065f,0.15f),maxVoices:2,minInterval:0.05f);
            m_Hurt=m_Sound.Register("hurt",SfxDef.Create(SfxWave.Saw,180f,65f,0.20f,0.35f),maxVoices:1,minInterval:0.2f);
            m_Upgrade=m_Sound.Register("upgrade",SfxDef.Create(SfxWave.Triangle,440f,1320f,0.25f,0.3f),maxVoices:1);
            Renderer.Feedback+=OnFeedback;
            Hud=gameObject.AddComponent<ShooterHud>();Hud.Build(this);
        }
        void FitCamera(FollowCamera2D camera) { var half=m_Config.Settings.ArenaHalf;camera.Target=float2.zero;camera.Size=math.max(half.y+0.6f,(half.x+0.4f)/math.max(0.2f,camera.Camera.aspect)); }
        void Update()
        {
            if(Session==null)return;
            var state=State;
            if(state.Run.Flow==ShooterFlow.Playing)Governor.KeepAwake();
            if(AutoPlay)
            {
                Session.Sync();
                if(state.Run.Flow==ShooterFlow.Upgrade) { Choose(0);return; }
                if(state.Run.Flow==ShooterFlow.Playing)
                {
                    var w=Session.World;var positions=w.Column(ShooterKeys.EnemyPosition);var enemies=w.Table(ShooterKeys.Enemy);float x=0f,best=float.MaxValue;
                    for(int i=0;i<enemies.Count;i++) if(enemies.DeadFlags[i]==0 && positions[i].y<best) { best=positions[i].y;x=positions[i].x; }
                    state.Run.Move=math.normalizesafe(new float2(x-state.Run.Hero.x,-5.6f-state.Run.Hero.y));return;
                }
            }
            var input=default(InputFrame);m_Keyboard.TryRead(ref input);state.Run.Move=input.Move;
        }
        void OnFeedback(ShooterFeedback e)
        {
            switch(e.Kind) { case ShooterFeedbackKind.Shot:m_Sound.Play(m_Shot);break;case ShooterFeedbackKind.Destroyed:m_Sound.Play(m_Destroyed);break;case ShooterFeedbackKind.Pickup:m_Sound.Play(m_Pickup);break;case ShooterFeedbackKind.Hurt:m_Sound.Play(m_Hurt);break;case ShooterFeedbackKind.Upgrade:m_Sound.Play(m_Upgrade);break; }
        }
        public void StartRun() { CancelControl();State?.Send(ShooterCommandKind.Start); }
        public void Choose(int slot) { CancelControl();State?.Send(ShooterCommandKind.Choose,slot); }
        public void BackToMenu() { CancelControl();State?.Send(ShooterCommandKind.Menu); }
        public void DragPixels(Vector2 delta)
        {
            if(State==null || State.Run.Flow!=ShooterFlow.Playing)return;
            float units=CameraRig.Camera.orthographicSize*2f/math.max(1,Screen.height);
            State.Run.Drag+=(float2)delta*units;
        }
        public void CancelControl() { State?.CancelInput();Hud?.DragPad?.CancelPointer(); }
        void OnApplicationFocus(bool focused) { if(!focused)CancelControl(); }
        void OnApplicationPause(bool paused) { if(paused)CancelControl(); }
        void OnDisable() => CancelControl();
        void OnDestroy()
        {
            if(Governor!=null && Renderer!=null)Governor.LevelChanged-=Renderer.SetQuality;
            if(m_Mode!=null)Destroy(m_Mode);if(m_Module!=null)Destroy(m_Module);if(m_OwnsConfig && m_Config!=null)Destroy(m_Config);
        }
    }
}
