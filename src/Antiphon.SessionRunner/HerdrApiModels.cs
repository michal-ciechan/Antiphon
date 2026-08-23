using System.Text.Json.Serialization;

namespace Antiphon.SessionRunner;

/// <summary>Wire models for typed <see cref="HerdrClient"/> wrappers (CARD-0160 B2). Snake_case via
/// <see cref="JsonPropertyNameAttribute"/>; unknown fields are ignored (herdr forward-compat).</summary>
public static class HerdrSources
{
    /// <summary>Stamped on every <c>report_*</c> call so herdr furniture is attributable.</summary>
    public const string Antiphon = "antiphon";
}

/// <summary>Agent-manifest kind string for Claude Code (live probe P4).</summary>
public static class HerdrAgentKinds
{
    public const string Claude = "claude";
}

public sealed record HerdrWorkspaceInfo(
    [property: JsonPropertyName("workspace_id")] string WorkspaceId,
    [property: JsonPropertyName("label")] string Label,
    [property: JsonPropertyName("number")] int Number,
    [property: JsonPropertyName("active_tab_id")] string ActiveTabId,
    [property: JsonPropertyName("pane_count")] int PaneCount,
    [property: JsonPropertyName("tab_count")] int TabCount,
    [property: JsonPropertyName("focused")] bool Focused = false,
    [property: JsonPropertyName("agent_status")] string? AgentStatus = null,
    [property: JsonPropertyName("tokens")] IReadOnlyDictionary<string, string>? Tokens = null);

public sealed record HerdrTabInfo(
    [property: JsonPropertyName("tab_id")] string TabId,
    [property: JsonPropertyName("workspace_id")] string WorkspaceId,
    [property: JsonPropertyName("label")] string Label,
    [property: JsonPropertyName("number")] int Number,
    [property: JsonPropertyName("pane_count")] int PaneCount,
    [property: JsonPropertyName("focused")] bool Focused = false,
    [property: JsonPropertyName("agent_status")] string? AgentStatus = null);

public sealed record HerdrPaneInfo(
    [property: JsonPropertyName("pane_id")] string PaneId,
    [property: JsonPropertyName("tab_id")] string TabId,
    [property: JsonPropertyName("workspace_id")] string WorkspaceId,
    [property: JsonPropertyName("terminal_id")] string? TerminalId = null,
    [property: JsonPropertyName("cwd")] string? Cwd = null,
    [property: JsonPropertyName("revision")] long Revision = 0,
    [property: JsonPropertyName("focused")] bool Focused = false,
    [property: JsonPropertyName("agent_status")] string? AgentStatus = null,
    [property: JsonPropertyName("label")] string? Label = null,
    [property: JsonPropertyName("title")] string? Title = null,
    [property: JsonPropertyName("agent")] string? Agent = null,
    [property: JsonPropertyName("tokens")] IReadOnlyDictionary<string, string>? Tokens = null,
    [property: JsonPropertyName("agent_session")] HerdrAgentSessionInfo? AgentSession = null);

public sealed record HerdrAgentSessionInfo(
    [property: JsonPropertyName("source")] string Source,
    [property: JsonPropertyName("agent")] string Agent,
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("value")] string Value);

public sealed record HerdrAgentInfo(
    [property: JsonPropertyName("pane_id")] string PaneId,
    [property: JsonPropertyName("tab_id")] string TabId,
    [property: JsonPropertyName("workspace_id")] string WorkspaceId,
    [property: JsonPropertyName("terminal_id")] string? TerminalId = null,
    [property: JsonPropertyName("name")] string? Name = null,
    [property: JsonPropertyName("agent")] string? Agent = null,
    [property: JsonPropertyName("agent_status")] string? AgentStatus = null,
    [property: JsonPropertyName("cwd")] string? Cwd = null,
    [property: JsonPropertyName("revision")] long Revision = 0,
    [property: JsonPropertyName("focused")] bool Focused = false,
    [property: JsonPropertyName("interactive_ready")] bool InteractiveReady = false,
    [property: JsonPropertyName("launch_pending")] bool LaunchPending = false,
    [property: JsonPropertyName("agent_session")] HerdrAgentSessionInfo? AgentSession = null,
    [property: JsonPropertyName("tokens")] IReadOnlyDictionary<string, string>? Tokens = null);

public sealed record HerdrPaneProcessInfo(
    [property: JsonPropertyName("pane_id")] string PaneId,
    [property: JsonPropertyName("shell_pid")] int? ShellPid = null,
    [property: JsonPropertyName("foreground_processes")] IReadOnlyList<HerdrPaneProcess>? ForegroundProcesses = null,
    [property: JsonPropertyName("tty")] string? Tty = null,
    [property: JsonPropertyName("foreground_process_group_id")] int? ForegroundProcessGroupId = null);

public sealed record HerdrPaneProcess(
    [property: JsonPropertyName("pid")] int Pid,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("argv")] IReadOnlyList<string>? Argv = null,
    [property: JsonPropertyName("cwd")] string? Cwd = null,
    [property: JsonPropertyName("cmdline")] string? Cmdline = null,
    [property: JsonPropertyName("argv0")] string? Argv0 = null);

public sealed record HerdrPaneReadResult(
    [property: JsonPropertyName("text")] string Text,
    [property: JsonPropertyName("revision")] long Revision,
    [property: JsonPropertyName("truncated")] bool Truncated,
    [property: JsonPropertyName("pane_id")] string? PaneId = null,
    [property: JsonPropertyName("workspace_id")] string? WorkspaceId = null,
    [property: JsonPropertyName("tab_id")] string? TabId = null,
    [property: JsonPropertyName("source")] string? Source = null,
    [property: JsonPropertyName("format")] string? Format = null);

/// <summary><c>tab.create</c> result (probe P2): tab plus the initial <c>root_pane</c>.</summary>
public sealed record HerdrTabCreateResult(
    [property: JsonPropertyName("tab")] HerdrTabInfo Tab,
    [property: JsonPropertyName("root_pane")] HerdrPaneInfo RootPane)
{
    [JsonIgnore]
    public string InitialPaneId => RootPane.PaneId;

    [JsonIgnore]
    public string TabId => Tab.TabId;
}

/// <summary><c>workspace.create</c> envelope (probe P1); callers usually take <see cref="Workspace"/>.</summary>
public sealed record HerdrWorkspaceCreateResult(
    [property: JsonPropertyName("workspace")] HerdrWorkspaceInfo Workspace,
    [property: JsonPropertyName("tab")] HerdrTabInfo? Tab = null,
    [property: JsonPropertyName("root_pane")] HerdrPaneInfo? RootPane = null)
{
    [JsonIgnore]
    public string WorkspaceId => Workspace.WorkspaceId;
}

// --- request param records (optional fields omitted when null via JsonIgnore) ---

public sealed record HerdrWorkspaceCreateParams(
    [property: JsonPropertyName("cwd")] string? Cwd = null,
    [property: JsonPropertyName("label")] string? Label = null,
    [property: JsonPropertyName("env")] IReadOnlyDictionary<string, string>? Env = null,
    [property: JsonPropertyName("focus")] bool Focus = false);

public sealed record HerdrWorkspaceReportMetadataParams(
    [property: JsonPropertyName("workspace_id")] string WorkspaceId,
    [property: JsonPropertyName("source")] string Source,
    [property: JsonPropertyName("tokens")] IReadOnlyDictionary<string, string?> Tokens,
    [property: JsonPropertyName("ttl_ms"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] long? TtlMs = null,
    [property: JsonPropertyName("seq"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] ulong? Seq = null);

public sealed record HerdrTabCreateParams(
    [property: JsonPropertyName("workspace_id")] string? WorkspaceId = null,
    [property: JsonPropertyName("cwd")] string? Cwd = null,
    [property: JsonPropertyName("env")] IReadOnlyDictionary<string, string>? Env = null,
    [property: JsonPropertyName("label")] string? Label = null,
    [property: JsonPropertyName("focus")] bool Focus = false);

public sealed record HerdrPaneSplitParams(
    [property: JsonPropertyName("direction")] string Direction,
    [property: JsonPropertyName("target_pane_id"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? TargetPaneId = null,
    [property: JsonPropertyName("ratio"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] double? Ratio = null,
    [property: JsonPropertyName("cwd"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Cwd = null,
    [property: JsonPropertyName("env"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyDictionary<string, string>? Env = null,
    [property: JsonPropertyName("workspace_id"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? WorkspaceId = null,
    [property: JsonPropertyName("focus")] bool Focus = false);

public sealed record HerdrPaneRenameParams(
    [property: JsonPropertyName("pane_id")] string PaneId,
    [property: JsonPropertyName("label"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Label = null);

public sealed record HerdrPaneReportMetadataParams(
    [property: JsonPropertyName("pane_id")] string PaneId,
    [property: JsonPropertyName("source")] string Source,
    [property: JsonPropertyName("tokens"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyDictionary<string, string?>? Tokens = null,
    [property: JsonPropertyName("title"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Title = null,
    [property: JsonPropertyName("ttl_ms"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] long? TtlMs = null,
    [property: JsonPropertyName("seq"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] ulong? Seq = null);

public sealed record HerdrPaneReportAgentSessionParams(
    [property: JsonPropertyName("pane_id")] string PaneId,
    [property: JsonPropertyName("source")] string Source,
    [property: JsonPropertyName("agent")] string Agent,
    [property: JsonPropertyName("agent_session_id"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? AgentSessionId = null,
    [property: JsonPropertyName("agent_session_path"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? AgentSessionPath = null,
    [property: JsonPropertyName("seq"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] ulong? Seq = null);

public sealed record HerdrAgentStartParams(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("pane_id")] string PaneId,
    [property: JsonPropertyName("args"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<string>? Args = null,
    [property: JsonPropertyName("timeout_ms"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] long? TimeoutMs = null);

public sealed record HerdrPaneTargetParams(
    [property: JsonPropertyName("pane_id")] string PaneId);

public sealed record HerdrTabTargetParams(
    [property: JsonPropertyName("tab_id")] string TabId);

public sealed record HerdrPaneListParams(
    [property: JsonPropertyName("workspace_id"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? WorkspaceId = null);

public sealed record HerdrPaneProcessInfoParams(
    [property: JsonPropertyName("pane_id"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? PaneId = null);

public sealed record HerdrPaneReadParams(
    [property: JsonPropertyName("pane_id")] string PaneId,
    [property: JsonPropertyName("source")] string Source,
    [property: JsonPropertyName("strip_ansi")] bool StripAnsi = true,
    [property: JsonPropertyName("lines"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? Lines = null,
    [property: JsonPropertyName("format")] string Format = "text");

public sealed record HerdrPaneSendTextParams(
    [property: JsonPropertyName("pane_id")] string PaneId,
    [property: JsonPropertyName("text")] string Text);

public sealed record HerdrPaneSendKeysParams(
    [property: JsonPropertyName("pane_id")] string PaneId,
    [property: JsonPropertyName("keys")] IReadOnlyList<string> Keys);

// --- response envelopes (deserialize then unwrap) ---

internal sealed record HerdrWorkspaceListEnvelope(
    [property: JsonPropertyName("workspaces")] IReadOnlyList<HerdrWorkspaceInfo> Workspaces);

internal sealed record HerdrWorkspaceCreateEnvelope(
    [property: JsonPropertyName("workspace")] HerdrWorkspaceInfo Workspace,
    [property: JsonPropertyName("tab")] HerdrTabInfo? Tab = null,
    [property: JsonPropertyName("root_pane")] HerdrPaneInfo? RootPane = null);

internal sealed record HerdrTabCreateEnvelope(
    [property: JsonPropertyName("tab")] HerdrTabInfo Tab,
    [property: JsonPropertyName("root_pane")] HerdrPaneInfo RootPane);

internal sealed record HerdrPaneEnvelope(
    [property: JsonPropertyName("pane")] HerdrPaneInfo Pane);

internal sealed record HerdrPaneListEnvelope(
    [property: JsonPropertyName("panes")] IReadOnlyList<HerdrPaneInfo> Panes);

internal sealed record HerdrPaneProcessInfoEnvelope(
    [property: JsonPropertyName("process_info")] HerdrPaneProcessInfo ProcessInfo);

internal sealed record HerdrPaneReadEnvelope(
    [property: JsonPropertyName("read")] HerdrPaneReadResult Read);

internal sealed record HerdrAgentStartedEnvelope(
    [property: JsonPropertyName("agent")] HerdrAgentInfo Agent,
    [property: JsonPropertyName("argv")] IReadOnlyList<string>? Argv = null);
