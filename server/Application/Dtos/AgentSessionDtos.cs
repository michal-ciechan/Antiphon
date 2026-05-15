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
    IReadOnlyDictionary<string, string>? ExtraEnv = null);

public sealed record AgentSessionStartResult(
    Guid SessionId,
    Guid RunAttemptId,
    Guid WorktreeId,
    bool FirstDeltaReceived);

public sealed record AgentSessionBufferDto(
    Guid SessionId,
    string Buffer);

public sealed record SendSessionInputRequest(string Input);

public sealed record ResizeSessionRequest(int Cols, int Rows);
