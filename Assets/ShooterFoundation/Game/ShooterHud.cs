using SPF.Shell.UI;
using UnityEngine;
using UnityEngine.UI;

namespace ShooterFoundation.Game
{
    public sealed class ShooterHud : MonoBehaviour
    {
        ShooterGameBootstrap m_Game;
        ShooterFlow m_Flow=(ShooterFlow)255;int m_Version=-1;
        RectTransform m_Root;Text[] m_ChoiceLabels;Text m_Result;
        public Canvas Canvas { get; private set; }
        public RectTransform MenuPanel { get; private set; }
        public RectTransform UpgradePanel { get; private set; }
        public RectTransform ResultPanel { get; private set; }
        public ShooterDragPad DragPad { get; private set; }
        public Button StartButton { get; private set; }
        public Button RestartButton { get; private set; }
        public Button MenuButton { get; private set; }
        public Button[] ChoiceButtons { get; private set; }
        public BufferText Stats { get; private set; }
        public Image HealthFill { get; private set; }
        public Image WaveFill { get; private set; }
        static readonly string[] ChoiceText = {
            "TWIN CANNON\n+45% bullet damage\nPrecision firepower",
            "QUICK CYCLE\n+18% fire rate\nKeep the sky clear",
            "SOLAR RAY\nTracks the nearest enemy\nContinuous damage",
            "WING PARTNER\nStronger support fire\nStay in formation",
            "FIELD REPAIR\nRestore 40 hull + damage\nKeep flying"
        };
        static readonly Color Ink=SanctuaryUiTheme.Ink,Teal=SanctuaryUiTheme.Spirit,Gold=SanctuaryUiTheme.Bronze;
        public void Build(ShooterGameBootstrap game)
        {
            m_Game=game;Canvas=UIFactory.CreateCanvas(transform,"ShooterUI");var scaler=Canvas.GetComponent<CanvasScaler>();scaler.referenceResolution=new Vector2(540,960);scaler.matchWidthOrHeight=1f;
            m_Root=UIFactory.Panel(Canvas.transform,"Portrait",Color.clear,new Vector2(0.5f,0.5f),new Vector2(0.5f,0.5f));m_Root.pivot=new Vector2(0.5f,0.5f);m_Root.sizeDelta=new Vector2(540,960);
            var pad=UIFactory.Panel(m_Root,"DragFlight",new Color(1,1,1,0.001f),new Vector2(0,0.075f),new Vector2(1,0.85f));DragPad=pad.gameObject.AddComponent<ShooterDragPad>();DragPad.Game=game;
            var top=UIFactory.Card(m_Root,"FlightTelemetry",Ink,new Vector2(0.035f,0.865f),new Vector2(0.965f,0.97f),false);
            Stats=BufferText.Create(top,"Stats",22,TextAnchor.UpperLeft,new Vector2(0.025f,0.37f),new Vector2(0.975f,0.94f));
            HealthFill=UIFactory.Bar(top,"Hull",new Color(0.02f,0.04f,0.08f),Teal,new Vector2(0.03f,0.16f),new Vector2(0.70f,0.28f));
            WaveFill=UIFactory.Bar(top,"Wave",new Color(0.02f,0.04f,0.08f),Gold,new Vector2(0.74f,0.16f),new Vector2(0.97f,0.28f));
            var footer=UIFactory.Card(m_Root,"Controls",Ink,new Vector2(0.035f,0.02f),new Vector2(0.965f,0.075f),false);
            UIFactory.Label(footer,"Hint","DRAG TO FLY  /  WASD     AUTO FIRE",16,TextAnchor.MiddleCenter,Vector2.zero,Vector2.one).color=SanctuaryUiTheme.Muted;
            MenuPanel=UIFactory.Panel(m_Root,"Menu",new Color(0.035f,0.095f,0.105f,0.92f),Vector2.zero,Vector2.one);
            UIFactory.Label(MenuPanel,"Tag","SPF  /  PORTRAIT ARCADE",18,TextAnchor.MiddleCenter,new Vector2(0,0.69f),new Vector2(1,0.77f)).color=Gold;
            UIFactory.Label(MenuPanel,"Title","SKYWARD\nPATROL",60,TextAnchor.MiddleCenter,new Vector2(0,0.48f),new Vector2(1,0.70f));
            UIFactory.Label(MenuPanel,"Brief","A wing partner. Five waves.\nBuild your flight, one upgrade at a time.",21,TextAnchor.MiddleCenter,new Vector2(0,0.35f),new Vector2(1,0.49f));
            StartButton=UIFactory.Button(MenuPanel,"Launch","LAUNCH",new Vector2(0,-205),new Vector2(360,72),Teal,new Vector2(0.5f,0.5f),28);StartButton.onClick.AddListener(game.StartRun);
            UpgradePanel=UIFactory.Panel(m_Root,"Upgrade",Ink,Vector2.zero,Vector2.one);
            UIFactory.Label(UpgradePanel,"UpgradeTitle","CHOOSE YOUR UPGRADE",29,TextAnchor.MiddleCenter,new Vector2(0,0.75f),new Vector2(1,0.84f));
            UIFactory.Label(UpgradePanel,"UpgradeHint","Flight paused. Pick one to continue.",18,TextAnchor.MiddleCenter,new Vector2(0,0.70f),new Vector2(1,0.76f));
            ChoiceButtons=new Button[3];m_ChoiceLabels=new Text[3];
            for(int i=0;i<3;i++) { int slot=i;var button=UIFactory.Button(UpgradePanel,"Upgrade"+i,"",new Vector2(0,116-i*155),new Vector2(450,128),i==0?Teal:new Color(0.16f,0.26f,0.38f),new Vector2(0.5f,0.5f),23);var rect=(RectTransform)button.transform;rect.anchorMin=new Vector2(0.055f,0.5f);rect.anchorMax=new Vector2(0.945f,0.5f);rect.sizeDelta=new Vector2(0,128);button.onClick.AddListener(()=>game.Choose(slot));ChoiceButtons[i]=button;m_ChoiceLabels[i]=button.GetComponentInChildren<Text>(); }
            ResultPanel=UIFactory.Panel(m_Root,"Result",Ink,Vector2.zero,Vector2.one);
            m_Result=UIFactory.Label(ResultPanel,"ResultTitle","",43,TextAnchor.MiddleCenter,new Vector2(0,0.50f),new Vector2(1,0.74f));
            RestartButton=UIFactory.Button(ResultPanel,"Restart","FLY AGAIN",new Vector2(0,-95),new Vector2(360,74),Teal,new Vector2(0.5f,0.5f),26);RestartButton.onClick.AddListener(game.StartRun);
            MenuButton=UIFactory.Button(ResultPanel,"Menu","MAIN MENU",new Vector2(0,-190),new Vector2(360,62),new Color(0.16f,0.26f,0.38f),new Vector2(0.5f,0.5f),22);MenuButton.onClick.AddListener(game.BackToMenu);
            Refresh();
        }
        void Update() => Refresh();
        void Refresh()
        {
            if(m_Game==null || m_Game.State==null)return;var state=m_Game.State;var r=state.Run;var settings=m_Game.Session.World.Resource(ShooterKeys.Rules).Settings;
            float width=Mathf.Min(540f,Screen.width*960f/Mathf.Max(1,Screen.height));m_Root.sizeDelta=new Vector2(width,960f);
            if(m_Flow!=r.Flow) { m_Flow=r.Flow;MenuPanel.gameObject.SetActive(r.Flow==ShooterFlow.Menu);UpgradePanel.gameObject.SetActive(r.Flow==ShooterFlow.Upgrade);ResultPanel.gameObject.SetActive(r.Flow==ShooterFlow.Won || r.Flow==ShooterFlow.Dead);DragPad.gameObject.SetActive(r.Flow==ShooterFlow.Playing); }
            Stats.Begin().Append("WAVE ").Append(r.Wave).Append('/').Append(settings.Waves).Append("    HULL ").Append((int)r.Hp).Append("\nSALVAGE ").Append(r.Coins).Append("    DOWN ").Append(r.Kills);Stats.Commit();
            UIFactory.SetFill(HealthFill,r.Hp/settings.HeroHp);UIFactory.SetFill(WaveFill,r.Wave/(float)settings.Waves);
            if(m_Version==r.Version)return;m_Version=r.Version;
            if(r.Flow==ShooterFlow.Upgrade)for(int i=0;i<3;i++)m_ChoiceLabels[i].text=ChoiceText[state.Choice(i)];
            if(r.Flow==ShooterFlow.Won)m_Result.text="PATROL COMPLETE\nSKY SECURED";
            if(r.Flow==ShooterFlow.Dead)m_Result.text="FLIGHT ENDED\nTRY A NEW BUILD";
        }
    }
}
