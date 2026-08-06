namespace Antiphon.Server.Domain.Enums;

public enum AgentStatus
{
    Idle = 0,
    Ready = 1,
    /// <summary>
    /// The agent has been started and has not since stopped or crashed — a lifecycle latch
    /// ("has a live session"), NOT "mid-turn right now". The transcript-derived busy signal is
    /// the separate Working bool on the agent DTOs. Renamed from Working (int value unchanged;
    /// the old name kept reading as "busy" and inviting edits that broke the latch semantics).
    /// </summary>
    Running = 2,
    WaitingForHumanReview = 3,
    Stopped = 4,
    Disconnected = 5,
    Failed = 6
}
