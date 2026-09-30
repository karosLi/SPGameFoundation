using System.Collections.Generic;
using SPF.Runtime.Session;
using UnityEngine;

namespace SnakeFoundation.Game
{
    /// <summary>
    /// Polls input sources in priority order every frame and writes the result into the game state
    /// before the session schedules this frame's ticks (execution order before SessionHost).
    /// </summary>
    [DefaultExecutionOrder(-1100)]
    public sealed class InputRouter : MonoBehaviour
    {
        readonly List<IPlayerInputSource> m_Sources = new List<IPlayerInputSource>();

        public SessionHost Host { get; set; }
        public ScriptedInput Scripted { get; } = new ScriptedInput();
        public PlayerCommand LastCommand { get; private set; }

        void Awake()
        {
            m_Sources.Add(Scripted);
        }

        /// <summary>Adds a source after the scripted one (earlier = higher priority).</summary>
        public void AddSource(IPlayerInputSource source) => m_Sources.Add(source);

        void Update()
        {
            var session = Host != null ? Host.Session : null;
            if (session == null) return;
            var game = session.World.Resource(SnakeKeys.Game);
            var replay = session.World.Resource(SnakeKeys.Replay);
            if (replay.Mode == ReplayMode.Play)
                return;

            var command = default(PlayerCommand);
            for (int i = 0; i < m_Sources.Count; i++)
                if (m_Sources[i].TryRead(out command))
                    break;
            game.Command = command;
            LastCommand = command;
        }
    }
}
