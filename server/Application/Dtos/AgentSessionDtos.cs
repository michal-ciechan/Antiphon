using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Dtos;

public sealed record StartAgentSessionRequest(
    Guid CardId,
    string DefinitionName,
    AgentKind AgentKind,
    string Prompt,
    int Cols = 120,
    int Rows = 30,
    IReadOnlyList<string>? ExtraArgs = null,
    IReadOnlyDictionary<string, string>? ExtraEnv = null,
    Guid? PreclaimedSessionId = null,
    Guid? BoardWorkflowDefinitionId = null,
    bool UseWorkflowPrompt = false,
    // When set, send '/rename <name>' then '/remote-control' once the agent is ready,
    // before the work prompt — so a freshly booted agent can be monitored remotely.
    string? RemoteControlName = null);

public sealed record AgentSessionStartResult(
    Guid SessionId,
    Guid RunAttemptId,
    Guid WorktreeId,
    bool FirstDeltaReceived);

public sealed record AgentSessionResumeResult(
    Guid SessionId,
    Guid CardId);

public enum AgentSessionResumeMode
{
    Resume,
    Continue,
    New
}

public sealed record ResumeAgentSessionRequest(
    AgentSessionResumeMode Mode = AgentSessionResumeMode.Resume);

public sealed record AgentSessionBufferDto(
    Guid SessionId,
    string Buffer,
    long LastSequence);

public sealed record SendSessionInputRequest(string Input);

public sealed record ResizeSessionRequest(int Cols, int Rows);

public sealed record ChannelMessageRequest(string Message);

public sealed record ChannelDelegateCardRequest(
    Guid CardId,
    Guid ConcurrencyToken,
    string Message,
    string? DefinitionName = null,
    int Cols = 120,
    int Rows = 30);
