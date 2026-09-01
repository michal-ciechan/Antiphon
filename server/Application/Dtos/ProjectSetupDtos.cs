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

public sealed record ProjectSetupCatalogDto(
    IReadOnlyList<ModelLevelDto> ModelLevels,
    IReadOnlyList<ReplyStyleDto> ReplyStyles,
    IReadOnlyList<InstructionBundleDto> Bundles,
    IReadOnlyList<AgentTuiProfileSummaryDto> Profiles,
    IReadOnlyList<AgentPresetDto> Presets,
    DelegationSummaryDto Delegation);

public sealed record ModelLevelDto(
    string Key,
    string Label,
    string Blurb,
    IReadOnlyDictionary<string, string> AliasesByKind);

public sealed record ReplyStyleDto(
    string Key,
    string Label,
    string Description);

public sealed record AgentTuiProfileSummaryDto(
    Guid Id,
    string DisplayName,
    AgentKind Kind,
    bool IsDefault,
    bool HasActiveRevision);

public sealed record AgentPresetDto(
    string Key,
    string Label,
    string Description,
    bool AlwaysOn,
    AgentModelLevel ModelLevel,
    AgentReplyStyle ReplyStyle,
    IReadOnlyList<string> BundleKeys,
    string? SystemPromptTemplate,
    string NamePattern,
    // CARD-0255. Trailing so existing `new(...)` sites compile. Create-time starting point only.
    bool RemoteControlEnabled = false,
    Guid? DefaultWorkflowTemplateId = null);

public sealed record DelegationSummaryDto(
    IReadOnlyList<string> AllowedRoots,
    bool AllowedRootsIsEmpty,
    int MaxConcurrentTasks,
    decimal MaxCostUsdPerRoot,
    int MaxDepth,
    AgentModelLevel DefaultLevel);

public sealed record ProjectSetupRequest(
    string Directory,
    bool CreateDirectory = false,
    string? Name = null,
    string? GitRepositoryUrl = null,
    string? BaseBranch = null,
    string? BoardName = null,
    int BoardMaxConcurrentSessions = 1,
    ProjectSetupAgentRequest? Agent = null,
    bool StartAgent = false);

public sealed record ProjectSetupAgentRequest(
    string? Preset = null,
    string? Name = null,
    Guid? TuiProfileId = null,
    string? ModelId = null,
    AgentModelLevel? ModelLevel = null,
    AgentReplyStyle? ReplyStyle = null,
    bool? AlwaysOn = null,
    bool? RemoteControlEnabled = null,
    IReadOnlyList<string>? BundleKeys = null,
    string? SystemPromptAppend = null);

public sealed record ProjectSetupResultDto(
    ProjectDto Project,
    BoardSummaryDto Board,
    AgentDetailDto? Agent,
    ProjectReadinessDto Readiness,
    IReadOnlyList<string> Notes);
