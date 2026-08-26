using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Dtos;

public enum ReadinessLevel
{
    Required = 0,
    Recommended = 1,
    Optional = 2,
}

public enum ReadinessStatus
{
    Ok = 0,
    Missing = 1,
    Warning = 2,
    NotApplicable = 3,
}

public sealed record ProjectReadinessDto(
    Guid ProjectId,
    bool CanDispatch,
    IReadOnlyList<ReadinessCheckDto> Checks);

public sealed record ReadinessCheckDto(
    string Key,
    ReadinessLevel Level,
    ReadinessStatus Status,
    string Summary,
    string? Detail,
    ReadinessFixDto? Fix);

public sealed record ReadinessFixDto(
    string Label,
    string? Route,
    string? Action);

public static class ReadinessKeys
{
    public const string Directory = "directory";
    public const string GitRepository = "git-repository";
    public const string Board = "board";
    public const string Agent = "agent";
    public const string AgentRunner = "agent-runner";
    public const string AgentDirectory = "agent-directory";
    public const string DelegationRoot = "delegation-root";
    public const string WorkflowTemplate = "workflow-template";
    public const string Orchestrator = "orchestrator";
    public const string Channel = "channel";
    public const string GitHub = "github";
}
