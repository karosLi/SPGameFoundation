using SPF.Contracts;
using Unity.Collections;
using Unity.Mathematics;

namespace SnakeFoundation
{
    public enum GameFlow
    {
        /// <summary>No player snake: AI plays, menu is shown.</summary>
        Attract = 0,
        Playing = 1,
        /// <summary>Player died; waiting for restart.</summary>
        GameOver = 2,
    }

    public struct LeaderboardEntry
    {
        public int SnakeId;
        public float Mass;
        public bool IsPlayer;
    }

    /// <summary>
    /// Main-thread game flow and UI-facing state. Written by ApplyCommands-phase systems and by the
    /// game shell (requests, player command); read by UI and presentation after the tick synced.
    /// </summary>
    public sealed class SnakeGameState : IResettableResource
    {
        public const int LeaderboardSize = 5;

        public GameFlow Flow { get; internal set; }
        public int ActiveRegion { get; internal set; }
        public EntityHandle Player { get; internal set; }
        public int PlayerSkin { get; set; }
        public string PlayerName { get; set; } = "You";

        /// <summary>Latest input; copied into the player's control at the start of each tick.</summary>
        public PlayerCommand Command;

        // Requests from the shell, consumed by systems at the next tick.
        public bool StartRequested { get; private set; }
        public void RequestStart() => StartRequested = true;
        internal void ConsumeStart() => StartRequested = false;

        /// <summary>Camera / window focus when there is no player (attract mode).</summary>
        public float2 Focus { get; internal set; }

        // Stats refreshed a few times per second for the UI.
        public float PlayerMass { get; internal set; }
        public float PlayerLength { get; internal set; }
        public float PlayerRadius { get; internal set; }
        public int PlayerKills { get; internal set; }
        public int PlayerRank { get; internal set; }
        public float2 PlayerHead { get; internal set; }
        public float BestMass { get; internal set; }
        public float SurvivalSeconds { get; internal set; }
        public int AliveSnakes { get; internal set; }
        public DeathCause LastDeathCause { get; internal set; }
        public int LastKillerId { get; internal set; }
        public int RegionSwitches { get; internal set; }
        public int NextSnakeId { get; internal set; } = 1;

        public readonly LeaderboardEntry[] Leaderboard = new LeaderboardEntry[LeaderboardSize];
        public int LeaderboardCount { get; internal set; }

        /// <summary>Incremented whenever any UI-facing value above changes.</summary>
        public int Version { get; internal set; }

        public void OnReset()
        {
            Flow = GameFlow.Attract;
            ActiveRegion = 0;
            Player = EntityHandle.Null;
            Command = default;
            StartRequested = false;
            Focus = float2.zero;
            PlayerMass = PlayerLength = PlayerRadius = 0f;
            PlayerKills = PlayerRank = 0;
            BestMass = SurvivalSeconds = 0f;
            AliveSnakes = 0;
            RegionSwitches = 0;
            NextSnakeId = 1;
            LeaderboardCount = 0;
            Version++;
        }
    }

    /// <summary>Small blittable mailbox that jobs use to signal the main thread (portal entry, player death).</summary>
    public sealed class Signals : System.IDisposable, IResettableResource
    {
        public const int PortalRequest = 0;      // portal index + 1, 0 = none
        public const int Count = 1;

        NativeArray<int> m_Values;

        public Signals() => m_Values = new NativeArray<int>(Count, Allocator.Persistent);

        public NativeArray<int> Values => m_Values;

        public int Take(int slot)
        {
            int v = m_Values[slot];
            m_Values[slot] = 0;
            return v;
        }

        public void OnReset()
        {
            for (int i = 0; i < Count; i++) m_Values[i] = 0;
        }

        public void Dispose()
        {
            if (m_Values.IsCreated) m_Values.Dispose();
        }
    }
}
