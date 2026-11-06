using System.Globalization;
using System.Text;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Antiphon.Server.Infrastructure.Agents.Tui;

public enum AgentTuiMetricOutcome
{
    Succeeded,
    Failed,
    Conflict,
    Invalid,
    Partial,
    TimedOut,
    NotObserved
}

public enum AgentTuiSecretMetricOperation
{
    Write,
    Clear
}

public enum AgentTuiRevisionMetricOperation
{
    ProfileUpdate,
    SecretWrite,
    SecretClear
}

public enum AgentTuiLaunchModelMode
{
    Default,
    Exact
}

public enum AgentTuiLaunchActivityMode
{
    Unknown,
    Structured,
    QuietTime
}

public sealed class AgentTuiMetrics
{
    private const int MaximumObservedRunIds = 1024;
    private readonly object _sync = new();
    private readonly HashSet<Guid> _observedDiscoveryRunIds = [];
    private readonly Queue<Guid> _observedDiscoveryRunOrder = [];
    private readonly HashSet<Guid> _observedValidationRunIds = [];
    private readonly Queue<Guid> _observedValidationRunOrder = [];
    private readonly Dictionary<SecretKey, long> _secretOperations = [];
    private readonly Dictionary<DiscoveryKey, long> _discoveryRuns = [];
    private readonly Dictionary<DiscoveryDurationKey, double> _discoveryDurations = [];
    private readonly Dictionary<ValidationStageKey, long> _validationStages = [];
    private readonly Dictionary<ValidationDurationKey, double> _validationDurations = [];
    private readonly Dictionary<LaunchKey, long> _launches = [];
    private readonly Dictionary<LaunchDurationKey, double> _launchDurations = [];
    private readonly Dictionary<ImportKey, long> _imports = [];
    private readonly Dictionary<AgentTuiRevisionMetricOperation, long> _revisionConflicts = [];

    public void RecordSecret(
        AgentTuiSecretMetricOperation operation,
        AgentTuiMetricOutcome outcome)
    {
        lock (_sync)
            Increment(_secretOperations, new SecretKey(operation, outcome));
    }

    public void RecordDiscovery(
        Guid? runId,
        AgentKind runnerType,
        AgentTuiValidationStatus status,
        bool catalogueRefreshed,
        TimeSpan duration)
    {
        var outcome = Map(status);
        lock (_sync)
        {
            if (runId is { } completedRunId
                && !ObserveRun(
                    completedRunId,
                    _observedDiscoveryRunIds,
                    _observedDiscoveryRunOrder))
            {
                return;
            }
            Increment(_discoveryRuns, new DiscoveryKey(runnerType, outcome, catalogueRefreshed));
            _discoveryDurations[new DiscoveryDurationKey(runnerType, outcome)] =
                NonNegativeSeconds(duration);
        }
    }

    public void RecordValidation(
        AgentKind runnerType,
        AgentTuiValidationRunDto run,
        TimeSpan duration)
    {
        var outcome = Map(run.Status);
        lock (_sync)
        {
            if (!ObserveRun(run.Id, _observedValidationRunIds, _observedValidationRunOrder))
                return;
            foreach (var stage in run.Stages)
            {
                Increment(
                    _validationStages,
                    new ValidationStageKey(
                        runnerType,
                        MapStage(stage.Name),
                        Map(stage.Status)));
            }
            _validationDurations[new ValidationDurationKey(runnerType, outcome)] =
                NonNegativeSeconds(duration);
        }
    }

    public void RecordLaunch(
        AgentKind runnerType,
        AgentTuiMetricOutcome outcome,
        AgentTuiLaunchModelMode modelMode,
        AgentTuiLaunchActivityMode activityMode,
        TimeSpan duration)
    {
        lock (_sync)
        {
            Increment(_launches, new LaunchKey(runnerType, outcome, modelMode, activityMode));
            _launchDurations[new LaunchDurationKey(runnerType, outcome)] =
                NonNegativeSeconds(duration);
        }
    }

    public void RecordImport(AgentTuiImportResultDto result)
    {
        var changeKind = result.ProfilesCreated > 0
            ? ImportChangeKind.ProfilesCreated
            : result.AgentsAssigned > 0
                ? ImportChangeKind.AgentsAssigned
                : ImportChangeKind.Unchanged;
        lock (_sync)
            Increment(_imports, new ImportKey(AgentTuiMetricOutcome.Succeeded, changeKind));
    }

    public void RecordImportFailure()
    {
        lock (_sync)
            Increment(_imports, new ImportKey(AgentTuiMetricOutcome.Failed, ImportChangeKind.None));
    }

    public void RecordRevisionConflict(AgentTuiRevisionMetricOperation operation)
    {
        lock (_sync)
            Increment(_revisionConflicts, operation);
    }

    public async Task<string> RenderAsync(
        AppDbContext db,
        AgentTuiKeyProtectionReadiness keyReadiness,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var profiles = await db.AgentTuiProfiles
            .AsNoTracking()
            .Include(profile => profile.ActiveRevision)
            .Select(profile => new ProfileSnapshot(
                profile.Id,
                profile.Kind,
                profile.IsEnabled,
                profile.ActiveRevisionId,
                profile.ActiveRevision == null
                    ? null
                    : profile.ActiveRevision.AuthenticationMode))
            .ToArrayAsync(cancellationToken);
        var profileIds = profiles.Select(profile => profile.Id).ToArray();
        var validationRuns = profileIds.Length == 0
            ? []
            : await db.AgentTuiValidationRuns
                .AsNoTracking()
                .Where(run => profileIds.Contains(run.ProfileId)
                              && run.Operation == "validation")
                .Select(run => new ValidationSnapshot(
                    run.ProfileId,
                    run.ProfileRevisionId,
                    run.Status,
                    run.CreatedAt))
                .ToArrayAsync(cancellationToken);
        var validationByRevision = validationRuns
            .GroupBy(run => new { run.ProfileId, run.ProfileRevisionId })
            .ToDictionary(
                group => (group.Key.ProfileId, group.Key.ProfileRevisionId),
                group => group.OrderByDescending(run => run.CreatedAt).First().Status);
        var profileSamples = profiles
            .GroupBy(profile => new ProfileKey(
                profile.Kind,
                profile.IsEnabled,
                profile.ActiveRevisionId is { } revisionId
                && validationByRevision.TryGetValue((profile.Id, revisionId), out var status)
                    ? status
                    : AgentTuiValidationStatus.NeverRun,
                profile.AuthenticationMode))
            .Select(group => new KeyValuePair<ProfileKey, long>(group.Key, group.LongCount()))
            .ToArray();

        var cacheRows = await db.AgentTuiModels
            .AsNoTracking()
            .Where(model => model.DiscoveredAt != null)
            .Select(model => new CacheSnapshot(
                model.Profile.Kind,
                model.Availability,
                model.DiscoveredAt!.Value))
            .ToArrayAsync(cancellationToken);
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var cacheSamples = cacheRows
            .GroupBy(row => new CacheKey(row.RunnerType, row.Availability))
            .Select(group => new KeyValuePair<CacheKey, double>(
                group.Key,
                group.Max(row => Math.Max(0, (now - row.DiscoveredAt).TotalSeconds))))
            .ToArray();

        MetricsSnapshot counters;
        lock (_sync)
        {
            counters = new MetricsSnapshot(
                _secretOperations.ToArray(),
                _discoveryRuns.ToArray(),
                _discoveryDurations.ToArray(),
                _validationStages.ToArray(),
                _validationDurations.ToArray(),
                _launches.ToArray(),
                _launchDurations.ToArray(),
                _imports.ToArray(),
                _revisionConflicts.ToArray());
        }

        var output = new StringBuilder(4096);
        Family(output, "antiphon_agent_tui_profiles", "gauge",
            "Current Agent TUI profile inventory grouped by bounded readiness labels.");
        if (profileSamples.Length == 0)
        {
            Sample(output, "antiphon_agent_tui_profiles",
                Labels(("runner_type", "unknown"), ("enabled", "false"),
                    ("validation_state", "never_run"), ("auth_mode", "unknown")), 0);
        }
        foreach (var sample in profileSamples.OrderBy(sample => sample.Key, ProfileKeyComparer.Instance))
        {
            Sample(output, "antiphon_agent_tui_profiles",
                Labels(
                    ("runner_type", Runner(sample.Key.RunnerType)),
                    ("enabled", sample.Key.Enabled ? "true" : "false"),
                    ("validation_state", Validation(sample.Key.ValidationState)),
                    ("auth_mode", Authentication(sample.Key.AuthenticationMode))),
                sample.Value);
        }

        Family(output, "antiphon_agent_tui_secret_protection_ready", "gauge",
            "Whether managed Agent TUI secret protection is ready.");
        Sample(output, "antiphon_agent_tui_secret_protection_ready",
            Labels(("protector_type", "data_protection")), keyReadiness.IsReady ? 1 : 0);

        Family(output, "antiphon_agent_tui_secret_operations_total", "counter",
            "Agent TUI secret mutation outcomes.");
        foreach (var sample in counters.SecretOperations.OrderBy(sample => sample.Key.ToString(), StringComparer.Ordinal))
        {
            Sample(output, "antiphon_agent_tui_secret_operations_total",
                Labels(("operation", SecretOperation(sample.Key.Operation)),
                    ("outcome", Outcome(sample.Key.Outcome))), sample.Value);
        }

        Family(output, "antiphon_agent_tui_discovery_runs_total", "counter",
            "Bounded Agent TUI model discovery outcomes.");
        foreach (var sample in counters.DiscoveryRuns.OrderBy(sample => sample.Key.ToString(), StringComparer.Ordinal))
        {
            Sample(output, "antiphon_agent_tui_discovery_runs_total",
                Labels(("runner_type", Runner(sample.Key.RunnerType)),
                    ("outcome", Outcome(sample.Key.Outcome)),
                    ("cache_result", sample.Key.CatalogueRefreshed ? "refreshed" : "retained")),
                sample.Value);
        }

        Family(output, "antiphon_agent_tui_discovery_duration_seconds", "gauge",
            "Last bounded Agent TUI model discovery duration in seconds.");
        foreach (var sample in counters.DiscoveryDurations.OrderBy(sample => sample.Key.ToString(), StringComparer.Ordinal))
        {
            Sample(output, "antiphon_agent_tui_discovery_duration_seconds",
                Labels(("runner_type", Runner(sample.Key.RunnerType)),
                    ("outcome", Outcome(sample.Key.Outcome))), sample.Value);
        }

        Family(output, "antiphon_agent_tui_model_cache_age_seconds", "gauge",
            "Age of cached discovered Agent TUI models in seconds.");
        if (cacheSamples.Length == 0)
        {
            Sample(output, "antiphon_agent_tui_model_cache_age_seconds",
                Labels(("runner_type", "unknown"), ("cache_result", "no_cache")), 0);
        }
        foreach (var sample in cacheSamples.OrderBy(sample => sample.Key.ToString(), StringComparer.Ordinal))
        {
            Sample(output, "antiphon_agent_tui_model_cache_age_seconds",
                Labels(("runner_type", Runner(sample.Key.RunnerType)),
                    ("cache_result", Availability(sample.Key.Availability))), sample.Value);
        }

        Family(output, "antiphon_agent_tui_validation_stages_total", "counter",
            "Agent TUI validation stage outcomes.");
        foreach (var sample in counters.ValidationStages.OrderBy(sample => sample.Key.ToString(), StringComparer.Ordinal))
        {
            Sample(output, "antiphon_agent_tui_validation_stages_total",
                Labels(("runner_type", Runner(sample.Key.RunnerType)),
                    ("stage", Stage(sample.Key.Stage)),
                    ("outcome", Outcome(sample.Key.Outcome))), sample.Value);
        }

        Family(output, "antiphon_agent_tui_validation_duration_seconds", "gauge",
            "Last bounded Agent TUI validation duration in seconds.");
        foreach (var sample in counters.ValidationDurations.OrderBy(sample => sample.Key.ToString(), StringComparer.Ordinal))
        {
            Sample(output, "antiphon_agent_tui_validation_duration_seconds",
                Labels(("runner_type", Runner(sample.Key.RunnerType)),
                    ("outcome", Outcome(sample.Key.Outcome))), sample.Value);
        }

        Family(output, "antiphon_agent_tui_launches_total", "counter",
            "Agent TUI launch resolution outcomes.");
        if (counters.Launches.Length == 0)
        {
            Sample(output, "antiphon_agent_tui_launches_total",
                Labels(("runner_type", "unknown"), ("outcome", "not_observed"),
                    ("model_mode", "default"), ("activity_mode", "unknown")), 0);
        }
        foreach (var sample in counters.Launches.OrderBy(sample => sample.Key.ToString(), StringComparer.Ordinal))
        {
            Sample(output, "antiphon_agent_tui_launches_total",
                Labels(("runner_type", Runner(sample.Key.RunnerType)),
                    ("outcome", Outcome(sample.Key.Outcome)),
                    ("model_mode", ModelMode(sample.Key.ModelMode)),
                    ("activity_mode", ActivityMode(sample.Key.ActivityMode))), sample.Value);
        }

        Family(output, "antiphon_agent_tui_launch_resolution_duration_seconds", "gauge",
            "Last Agent TUI launch resolution duration in seconds.");
        if (counters.LaunchDurations.Length == 0)
        {
            Sample(output, "antiphon_agent_tui_launch_resolution_duration_seconds",
                Labels(("runner_type", "unknown"), ("outcome", "not_observed")), 0);
        }
        foreach (var sample in counters.LaunchDurations.OrderBy(sample => sample.Key.ToString(), StringComparer.Ordinal))
        {
            Sample(output, "antiphon_agent_tui_launch_resolution_duration_seconds",
                Labels(("runner_type", Runner(sample.Key.RunnerType)),
                    ("outcome", Outcome(sample.Key.Outcome))), sample.Value);
        }

        Family(output, "antiphon_agent_tui_imports_total", "counter",
            "Agent TUI file-definition import outcomes.");
        foreach (var sample in counters.Imports.OrderBy(sample => sample.Key.ToString(), StringComparer.Ordinal))
        {
            Sample(output, "antiphon_agent_tui_imports_total",
                Labels(("outcome", Outcome(sample.Key.Outcome)),
                    ("change_kind", ImportChange(sample.Key.ChangeKind))), sample.Value);
        }

        Family(output, "antiphon_agent_tui_revision_conflicts_total", "counter",
            "Agent TUI optimistic revision conflicts.");
        foreach (var sample in counters.RevisionConflicts.OrderBy(sample => sample.Key))
        {
            Sample(output, "antiphon_agent_tui_revision_conflicts_total",
                Labels(("operation", RevisionOperation(sample.Key))), sample.Value);
        }
        return output.ToString();
    }

    private static void Family(StringBuilder output, string name, string type, string help)
    {
        output.Append("# HELP ").Append(name).Append(' ').Append(help).Append('\n');
        output.Append("# TYPE ").Append(name).Append(' ').Append(type).Append('\n');
    }

    private static void Sample(
        StringBuilder output,
        string name,
        string labels,
        double value) => output.Append(name).Append(labels).Append(' ')
        .Append(value.ToString("0.#################", CultureInfo.InvariantCulture))
        .Append('\n');

    private static string Labels(params (string Name, string Value)[] labels) =>
        "{" + string.Join(",", labels.Select(label =>
            $"{label.Name}=\"{Escape(label.Value)}\"")) + "}";

    private static string Escape(string value) => value
        .Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("\n", "\\n", StringComparison.Ordinal)
        .Replace("\"", "\\\"", StringComparison.Ordinal);

    private static void Increment<TKey>(Dictionary<TKey, long> values, TKey key)
        where TKey : notnull => values[key] = values.GetValueOrDefault(key) + 1;

    private static bool ObserveRun(
        Guid runId,
        HashSet<Guid> observed,
        Queue<Guid> order)
    {
        if (!observed.Add(runId))
            return false;
        order.Enqueue(runId);
        while (order.Count > MaximumObservedRunIds)
            observed.Remove(order.Dequeue());
        return true;
    }

    private static double NonNegativeSeconds(TimeSpan duration) =>
        Math.Max(0, duration.TotalSeconds);

    private static AgentTuiMetricOutcome Map(AgentTuiValidationStatus status) => status switch
    {
        AgentTuiValidationStatus.Succeeded => AgentTuiMetricOutcome.Succeeded,
        AgentTuiValidationStatus.Partial => AgentTuiMetricOutcome.Partial,
        AgentTuiValidationStatus.TimedOut => AgentTuiMetricOutcome.TimedOut,
        AgentTuiValidationStatus.Failed => AgentTuiMetricOutcome.Failed,
        _ => AgentTuiMetricOutcome.NotObserved
    };

    private static AgentTuiMetricOutcome Map(AgentTuiValidationStageStatus status) => status switch
    {
        AgentTuiValidationStageStatus.Passed => AgentTuiMetricOutcome.Succeeded,
        AgentTuiValidationStageStatus.Degraded => AgentTuiMetricOutcome.Partial,
        AgentTuiValidationStageStatus.Failed => AgentTuiMetricOutcome.Failed,
        _ => AgentTuiMetricOutcome.NotObserved
    };

    private static ValidationStage MapStage(string stage) => stage switch
    {
        "executable" => ValidationStage.Executable,
        "arguments" => ValidationStage.Arguments,
        "workingDirectory" => ValidationStage.WorkingDirectory,
        "authentication" => ValidationStage.Authentication,
        "versionCapabilities" => ValidationStage.VersionCapabilities,
        "discovery" => ValidationStage.Discovery,
        "startup" => ValidationStage.Startup,
        "cleanStop" => ValidationStage.CleanStop,
        "suitability" => ValidationStage.Suitability,
        _ => ValidationStage.Unknown
    };

    private static string Runner(AgentKind kind) => kind switch
    {
        AgentKind.ClaudeCode => "claude_code",
        AgentKind.Codex => "codex",
        AgentKind.OpenCode => "open_code",
        AgentKind.Grok => "grok",
        AgentKind.Raw => "raw",
        _ => "unknown"
    };

    private static string Authentication(AgentTuiAuthenticationMode? mode) => mode switch
    {
        AgentTuiAuthenticationMode.WrapperManaged => "wrapper_managed",
        AgentTuiAuthenticationMode.ManagedEnvironment => "managed_environment",
        _ => "unknown"
    };

    private static string Validation(AgentTuiValidationStatus status) => status switch
    {
        AgentTuiValidationStatus.NeverRun => "never_run",
        AgentTuiValidationStatus.Running => "running",
        AgentTuiValidationStatus.Succeeded => "succeeded",
        AgentTuiValidationStatus.Partial => "partial",
        AgentTuiValidationStatus.Failed => "failed",
        AgentTuiValidationStatus.TimedOut => "timed_out",
        _ => "unknown"
    };

    private static string Outcome(AgentTuiMetricOutcome outcome) => outcome switch
    {
        AgentTuiMetricOutcome.Succeeded => "succeeded",
        AgentTuiMetricOutcome.Failed => "failed",
        AgentTuiMetricOutcome.Conflict => "conflict",
        AgentTuiMetricOutcome.Invalid => "invalid",
        AgentTuiMetricOutcome.Partial => "partial",
        AgentTuiMetricOutcome.TimedOut => "timed_out",
        _ => "not_observed"
    };

    private static string SecretOperation(AgentTuiSecretMetricOperation operation) => operation switch
    {
        AgentTuiSecretMetricOperation.Write => "write",
        AgentTuiSecretMetricOperation.Clear => "clear",
        _ => "unknown"
    };

    private static string Availability(AgentTuiModelAvailability availability) => availability switch
    {
        AgentTuiModelAvailability.Verified => "verified",
        AgentTuiModelAvailability.Stale => "stale",
        AgentTuiModelAvailability.Unavailable => "unavailable",
        _ => "unverified"
    };

    private static string Stage(ValidationStage stage) => stage switch
    {
        ValidationStage.Executable => "executable",
        ValidationStage.Arguments => "arguments",
        ValidationStage.WorkingDirectory => "working_directory",
        ValidationStage.Authentication => "authentication",
        ValidationStage.VersionCapabilities => "version_capabilities",
        ValidationStage.Discovery => "discovery",
        ValidationStage.Startup => "startup",
        ValidationStage.CleanStop => "clean_stop",
        ValidationStage.Suitability => "suitability",
        _ => "unknown"
    };

    private static string ModelMode(AgentTuiLaunchModelMode mode) => mode switch
    {
        AgentTuiLaunchModelMode.Exact => "exact",
        _ => "default"
    };

    private static string ActivityMode(AgentTuiLaunchActivityMode mode) => mode switch
    {
        AgentTuiLaunchActivityMode.Structured => "structured",
        AgentTuiLaunchActivityMode.QuietTime => "quiet_time",
        _ => "unknown"
    };

    private static string ImportChange(ImportChangeKind changeKind) => changeKind switch
    {
        ImportChangeKind.ProfilesCreated => "profiles_created",
        ImportChangeKind.AgentsAssigned => "agents_assigned",
        ImportChangeKind.Unchanged => "unchanged",
        _ => "none"
    };

    private static string RevisionOperation(AgentTuiRevisionMetricOperation operation) => operation switch
    {
        AgentTuiRevisionMetricOperation.ProfileUpdate => "profile_update",
        AgentTuiRevisionMetricOperation.SecretWrite => "secret_write",
        AgentTuiRevisionMetricOperation.SecretClear => "secret_clear",
        _ => "unknown"
    };

    private enum ValidationStage
    {
        Unknown,
        Executable,
        Arguments,
        WorkingDirectory,
        Authentication,
        VersionCapabilities,
        Discovery,
        Startup,
        CleanStop,
        Suitability
    }

    private enum ImportChangeKind
    {
        None,
        Unchanged,
        ProfilesCreated,
        AgentsAssigned
    }

    private sealed class ProfileKeyComparer : IComparer<ProfileKey>
    {
        public static ProfileKeyComparer Instance { get; } = new();
        public int Compare(ProfileKey left, ProfileKey right) =>
            StringComparer.Ordinal.Compare(left.ToString(), right.ToString());
    }

    private readonly record struct ProfileSnapshot(
        Guid Id,
        AgentKind Kind,
        bool IsEnabled,
        Guid? ActiveRevisionId,
        AgentTuiAuthenticationMode? AuthenticationMode);
    private readonly record struct ValidationSnapshot(
        Guid ProfileId,
        Guid ProfileRevisionId,
        AgentTuiValidationStatus Status,
        DateTime CreatedAt);
    private readonly record struct CacheSnapshot(
        AgentKind RunnerType,
        AgentTuiModelAvailability Availability,
        DateTime DiscoveredAt);
    private readonly record struct ProfileKey(
        AgentKind RunnerType,
        bool Enabled,
        AgentTuiValidationStatus ValidationState,
        AgentTuiAuthenticationMode? AuthenticationMode);
    private readonly record struct CacheKey(AgentKind RunnerType, AgentTuiModelAvailability Availability);
    private readonly record struct SecretKey(
        AgentTuiSecretMetricOperation Operation,
        AgentTuiMetricOutcome Outcome);
    private readonly record struct DiscoveryKey(
        AgentKind RunnerType,
        AgentTuiMetricOutcome Outcome,
        bool CatalogueRefreshed);
    private readonly record struct DiscoveryDurationKey(
        AgentKind RunnerType,
        AgentTuiMetricOutcome Outcome);
    private readonly record struct ValidationStageKey(
        AgentKind RunnerType,
        ValidationStage Stage,
        AgentTuiMetricOutcome Outcome);
    private readonly record struct ValidationDurationKey(
        AgentKind RunnerType,
        AgentTuiMetricOutcome Outcome);
    private readonly record struct LaunchKey(
        AgentKind RunnerType,
        AgentTuiMetricOutcome Outcome,
        AgentTuiLaunchModelMode ModelMode,
        AgentTuiLaunchActivityMode ActivityMode);
    private readonly record struct LaunchDurationKey(
        AgentKind RunnerType,
        AgentTuiMetricOutcome Outcome);
    private readonly record struct ImportKey(
        AgentTuiMetricOutcome Outcome,
        ImportChangeKind ChangeKind);
    private sealed record MetricsSnapshot(
        KeyValuePair<SecretKey, long>[] SecretOperations,
        KeyValuePair<DiscoveryKey, long>[] DiscoveryRuns,
        KeyValuePair<DiscoveryDurationKey, double>[] DiscoveryDurations,
        KeyValuePair<ValidationStageKey, long>[] ValidationStages,
        KeyValuePair<ValidationDurationKey, double>[] ValidationDurations,
        KeyValuePair<LaunchKey, long>[] Launches,
        KeyValuePair<LaunchDurationKey, double>[] LaunchDurations,
        KeyValuePair<ImportKey, long>[] Imports,
        KeyValuePair<AgentTuiRevisionMetricOperation, long>[] RevisionConflicts);
}
