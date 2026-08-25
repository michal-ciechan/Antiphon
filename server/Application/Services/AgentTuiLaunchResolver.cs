using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Agents.Tui;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Antiphon.Server.Application.Services;

public sealed record ResolvedAgentTuiLaunch(
    AgentLaunchSpec Spec,
    Guid? ProfileId,
    Guid? ProfileRevisionId,
    string? EffectiveModelId,
    AgentTuiLaunchActivityMode ActivityMode,
    LaunchModelArgument ModelArgument = LaunchModelArgument.None);

/// <summary>
/// Selects the managed-profile resolver when it is available, retaining the configured-file
/// registry as the migration and rollback path when no managed installation default exists.
/// </summary>
internal static class AgentLaunchResolution
{
    public static async Task<ResolvedAgentTuiLaunch> ResolveForAgentAsync(
        Agent agent,
        AgentRegistry agentRegistry,
        AgentTuiLaunchResolver? launchResolver,
        AgentLaunchOptions options,
        CancellationToken cancellationToken,
        ApiKeyEnvResolver? apiKeyEnvResolver = null)
    {
        // The agent's own launch env, attached HERE (CARD-0106 S2) rather than at each of the five
        // call sites this funnel serves — a caller that forgot would launch the agent without the
        // environment somebody configured for it, silently.
        options = options with
        {
            AgentEnv = options.AgentEnv ?? AgentLaunchEnv.ParseForAgent(agent)
        };
        options = await AttachProjectContextAsync(
            options, agent.BoardId, apiKeyEnvResolver, cancellationToken);

        if (launchResolver is null)
        {
            if (agent.TuiProfileId is not null)
            {
                throw new ConflictException(
                    "The selected runner profile cannot be resolved by this installation.",
                    "profile_resolution_unavailable");
            }

            return await ResolveLegacyAsync(
                agentRegistry, options, agent, apiKeyEnvResolver, cancellationToken);
        }

        try
        {
            return await launchResolver.ResolveForAgentAsync(agent, options, cancellationToken);
        }
        catch (ConflictException exception)
            when (agent.TuiProfileId is null && exception.Code == "profile_not_found")
        {
            return await ResolveLegacyAsync(
                agentRegistry, options, agent, apiKeyEnvResolver, cancellationToken);
        }
    }

    public static async Task<ResolvedAgentTuiLaunch> ResolveDefaultAsync(
        AgentRegistry agentRegistry,
        AgentTuiLaunchResolver? launchResolver,
        AgentLaunchOptions options,
        CancellationToken cancellationToken,
        ApiKeyEnvResolver? apiKeyEnvResolver = null)
    {
        options = await AttachProjectContextAsync(
            options, boardId: null, apiKeyEnvResolver, cancellationToken);

        if (launchResolver is null)
        {
            return await ResolveLegacyAsync(
                agentRegistry, options, agent: null, apiKeyEnvResolver, cancellationToken);
        }

        try
        {
            return await launchResolver.ResolveDefaultAsync(options, cancellationToken);
        }
        catch (ConflictException exception) when (exception.Code == "profile_not_found")
        {
            return await ResolveLegacyAsync(
                agentRegistry, options, agent: null, apiKeyEnvResolver, cancellationToken);
        }
    }

    /// <summary>
    /// One project-identity decision per launch, used for both API-key scope and project default
    /// env (CARD-0106). Callers that bypass the funnel still have their own
    /// <c>options.ApiKeyProjectId ?? …</c> fallbacks; when they go through here those fallbacks
    /// read the hoisted value and cannot disagree with the defaults fetch.
    /// </summary>
    private static async Task<AgentLaunchOptions> AttachProjectContextAsync(
        AgentLaunchOptions options,
        Guid? boardId,
        ApiKeyEnvResolver? apiKeyEnvResolver,
        CancellationToken cancellationToken)
    {
        if (apiKeyEnvResolver is null)
            return options;

        var projectId = options.ApiKeyProjectId
            ?? await apiKeyEnvResolver.ResolveProjectIdAsync(boardId, cancellationToken);
        return options with
        {
            ApiKeyProjectId = projectId,
            ProjectDefaultEnv = await apiKeyEnvResolver.GetProjectDefaultEnvAsync(
                projectId, cancellationToken),
        };
    }

    /// <summary>
    /// The legacy (no managed profile) path. It is also where the legacy spec gets FINALIZED, which
    /// is why API key resolution happens here rather than inside the sync, no-DB
    /// <c>AgentRegistry.Resolve</c> it wraps (plan section 4).
    /// </summary>
    private static async Task<ResolvedAgentTuiLaunch> ResolveLegacyAsync(
        AgentRegistry agentRegistry,
        AgentLaunchOptions options,
        Agent? agent,
        ApiKeyEnvResolver? apiKeyEnvResolver,
        CancellationToken cancellationToken)
    {
        var spec = agentRegistry.Resolve(agentRegistry.Settings.DefaultDefinition, options);
        if (apiKeyEnvResolver is not null)
        {
            var projectId = options.ApiKeyProjectId
                ?? await apiKeyEnvResolver.ResolveProjectIdAsync(agent?.BoardId, cancellationToken);
            spec = await apiKeyEnvResolver.ResolveSpecAsync(
                spec,
                projectId,
                agent is null ? "the default launch" : $"agent '{agent.Name}'",
                cancellationToken);
        }

        return new ResolvedAgentTuiLaunch(
            spec,
            ProfileId: null,
            ProfileRevisionId: null,
            EffectiveModelId: null,
            ActivityMode: AgentTuiLaunchActivityMode.Unknown,
            ModelArgument: string.IsNullOrWhiteSpace(options.TierModelAlias)
                ? LaunchModelArgument.None
                : LaunchModelArgument.Tier);
    }
}

public sealed class AgentTuiLaunchResolver
{
    private static readonly string[] ClaudeNestingMarkers =
    [
        "CLAUDECODE",
        "CLAUDE_CODE_CHILD_SESSION",
        "CLAUDE_CODE_SESSION_ID",
        "CLAUDE_CODE_BRIDGE_SESSION_ID",
        "CLAUDE_CODE_ENTRYPOINT",
    ];

    private const int MaximumEnvironmentValueLength = 4000;
    private readonly AppDbContext _db;
    private readonly IAgentTuiSecretProtector _secretProtector;
    private readonly AgentTuiMetrics _metrics;
    private readonly AgentTuiRunnerCatalog _runnerCatalog;
    private readonly IEqualityComparer<string> _environmentNameComparer;
    // CARD-0106 S2. Optional for the same reason AgentTuiLaunchResolver itself is optional to its
    // callers: a harness that does not wire it still resolves launches, and a placeholder that
    // therefore goes unresolved is caught by the tripwire in AgentSessionService.BuildRuntimeLaunchSpec
    // rather than reaching a child process. Production always registers it.
    private readonly ApiKeyEnvResolver? _apiKeyEnvResolver;
    private readonly ILogger<AgentTuiLaunchResolver> _logger;

    public AgentTuiLaunchResolver(
        AppDbContext db,
        IAgentTuiSecretProtector secretProtector,
        AgentTuiMetrics metrics,
        AgentTuiRunnerCatalog runnerCatalog,
        ApiKeyEnvResolver? apiKeyEnvResolver = null,
        ILogger<AgentTuiLaunchResolver>? logger = null)
        : this(
            db,
            secretProtector,
            metrics,
            runnerCatalog,
            AgentEnvironmentVariableNames.ForCurrentPlatform(),
            apiKeyEnvResolver,
            logger)
    {
    }

    internal AgentTuiLaunchResolver(
        AppDbContext db,
        IAgentTuiSecretProtector secretProtector,
        AgentTuiMetrics metrics,
        AgentTuiRunnerCatalog runnerCatalog,
        IEqualityComparer<string> environmentNameComparer,
        ApiKeyEnvResolver? apiKeyEnvResolver = null,
        ILogger<AgentTuiLaunchResolver>? logger = null)
    {
        _db = db;
        _secretProtector = secretProtector;
        _metrics = metrics;
        _runnerCatalog = runnerCatalog;
        _environmentNameComparer = environmentNameComparer;
        _apiKeyEnvResolver = apiKeyEnvResolver;
        _logger = logger ?? NullLogger<AgentTuiLaunchResolver>.Instance;
    }

    public async Task<ResolvedAgentTuiLaunch> ResolveForAgentAsync(
        Agent agent,
        AgentLaunchOptions options,
        CancellationToken cancellationToken)
    {
        var startedAt = Stopwatch.GetTimestamp();
        AgentKind? runnerType = null;
        try
        {
            var resolved = await ResolveCoreAsync(agent, options, cancellationToken);
            runnerType = resolved.Spec.Kind;
            _metrics.RecordLaunch(
                resolved.Spec.Kind,
                AgentTuiMetricOutcome.Succeeded,
                string.IsNullOrWhiteSpace(resolved.EffectiveModelId)
                    ? AgentTuiLaunchModelMode.Default
                    : AgentTuiLaunchModelMode.Exact,
                resolved.ActivityMode,
                Stopwatch.GetElapsedTime(startedAt));
            return resolved;
        }
        catch (Exception)
        {
            if (runnerType is { } kind)
            {
                _metrics.RecordLaunch(
                    kind,
                    AgentTuiMetricOutcome.Failed,
                    string.IsNullOrWhiteSpace(agent.ModelId)
                        ? AgentTuiLaunchModelMode.Default
                        : AgentTuiLaunchModelMode.Exact,
                    ActivityModeFor(kind),
                    Stopwatch.GetElapsedTime(startedAt));
            }
            throw;
        }
    }

    public async Task<ResolvedAgentTuiLaunch> ResolveDefaultAsync(
        AgentLaunchOptions options,
        CancellationToken cancellationToken)
    {
        var agent = new Agent
        {
            Id = Guid.Empty,
            Name = "default",
            TuiProfileId = null,
            ModelId = null
        };
        return await ResolveForAgentAsync(agent, options, cancellationToken);
    }

    private async Task<ResolvedAgentTuiLaunch> ResolveCoreAsync(
        Agent agent,
        AgentLaunchOptions options,
        CancellationToken cancellationToken)
    {
        if (options.Cols <= 0)
            throw new ArgumentException("Cols must be positive.", nameof(options));
        if (options.Rows <= 0)
            throw new ArgumentException("Rows must be positive.", nameof(options));

        var profile = await LoadProfileAsync(agent.TuiProfileId, cancellationToken);
        if (!profile.IsEnabled)
        {
            throw new ConflictException(
                "The selected runner profile is disabled.",
                "profile_disabled");
        }

        var revision = profile.ActiveRevision
            ?? throw new ConflictException(
                "The selected runner profile has no active revision.",
                "profile_not_validated");

        var arguments = DeserializeArray(revision.ArgumentsJson);
        var nonSecretEnvironment = DeserializeDictionary(revision.NonSecretEnvironmentJson);
        var secretNames = DeserializeArray(revision.SecretEnvironmentNamesJson);
        var environment = new Dictionary<string, string>(
            nonSecretEnvironment,
            StringComparer.Ordinal);

        if (revision.AuthenticationMode == AgentTuiAuthenticationMode.ManagedEnvironment)
        {
            foreach (var declaredName in secretNames)
            {
                var matches = profile.Secrets
                    .Where(secret => _environmentNameComparer.Equals(secret.Name, declaredName))
                    .ToArray();
                if (matches.Length != 1)
                {
                    throw new ConflictException(
                        "One or more managed credentials are missing for the selected profile.",
                        "profile_not_validated");
                }

                try
                {
                    var plaintext = _secretProtector.Unprotect(
                        profile.Id,
                        matches[0].Name,
                        matches[0].Ciphertext);
                    if (string.IsNullOrEmpty(plaintext) || plaintext.Length > MaximumEnvironmentValueLength)
                    {
                        throw new ConflictException(
                            "A managed credential could not be read safely.",
                            "secret_protection_unavailable");
                    }
                    environment[matches[0].Name] = plaintext;
                }
                catch (CryptographicException exception)
                {
                    throw new ServiceUnavailableException(
                        "Managed-secret protection is unavailable for launch.",
                        "secret_protection_unavailable",
                        exception);
                }
            }
        }

        // Merge order (CARD-0106): profile non-secret env and managed secrets -> project default
        // -> the AGENT's own launch env -> launch-time override -> ExtraEnv. The agent's field
        // outranks the profile because it is the more specific thing somebody wrote about THIS
        // agent; a project default outranks the shared profile (a credential/endpoint fact about
        // this project's agents) and loses to the agent. Neither outranks ExtraEnv, which carries
        // Antiphon's own ANTIPHON_* orchestration identity. Null AgentEnv means "read it off the
        // agent" — an explicit (possibly empty) dictionary from a caller wins.
        if (options.ProjectDefaultEnv is not null)
        {
            foreach (var (key, value) in options.ProjectDefaultEnv)
                environment[key] = value;
        }

        foreach (var (key, value) in options.AgentEnv ?? AgentLaunchEnv.ParseForAgent(agent))
            environment[key] = value;

        if (options.LaunchEnvOverride is not null)
        {
            foreach (var (key, value) in options.LaunchEnvOverride)
                environment[key] = value;
        }

        if (options.ExtraEnv is not null)
        {
            foreach (var (key, value) in options.ExtraEnv)
                environment[key] = value;
        }

        var args = new List<string>(arguments);
        if (options.ExtraArgs is not null)
            args.AddRange(options.ExtraArgs);

        var (effectiveModelId, modelArgument) = ApplyModelArgument(profile, revision, agent, options, args);

        ApplyClaudeEnvironmentDefaults(profile.Kind, environment);
        ApplyGrokEnvironmentDefaults(profile.Kind, environment);

        var cwd = string.IsNullOrWhiteSpace(options.Cwd)
            ? string.IsNullOrWhiteSpace(revision.WorkingDirectory)
                ? Environment.CurrentDirectory
                : revision.WorkingDirectory
            : options.Cwd;
        if (!string.IsNullOrWhiteSpace(cwd))
            cwd = Path.GetFullPath(cwd);

        var exe = AgentExecutableResolver.Default.TryResolve(revision.Executable)
                   ?? revision.Executable;
        var definitionName = profile.SourceDefinitionName
                             ?? profile.DisplayName;

        // CARD-0106 S2 — resolution runs over the FULLY-MERGED environment, after the kind defaults
        // above, so a {{key:NAME}} works identically whichever layer contributed the value: the
        // agent's own launch env, the profile revision's non-secret env, or an appsettings
        // definition. That last one is what makes the AgentTuiSecret convergence a small card later.
        var subject = agent.Id == Guid.Empty
            ? $"the default launch on profile '{definitionName}'"
            : $"agent '{agent.Name}'";
        if (modelArgument == LaunchModelArgument.ProfileOwned)
        {
            _logger.LogInformation(
                "{Subject}: profile '{Profile}' rev {Rev} declares no model argument; tier {Level} ({Alias}) not passed",
                subject,
                profile.DisplayName,
                revision.RevisionNumber,
                agent.ModelLevel,
                options.TierModelAlias?.Trim());
        }
        if (_apiKeyEnvResolver is not null)
        {
            // The agent's board decides the project scope. No board (a pool delegate, the synthetic
            // default agent) resolves GLOBAL keys only — deriving a project from a working directory
            // was rejected as unreliable, and a mis-scoped secret is worse than a failed launch.
            var projectId = options.ApiKeyProjectId
                ?? await _apiKeyEnvResolver.ResolveProjectIdAsync(agent.BoardId, cancellationToken);
            foreach (var argument in args)
                ApiKeyPlaceholder.EnsureAbsent(argument, $"A launch argument for {subject}");
            environment = new Dictionary<string, string>(
                await _apiKeyEnvResolver.ResolveAsync(
                    environment,
                    projectId,
                    subject,
                    cancellationToken),
                StringComparer.Ordinal);
        }

        var spec = new AgentLaunchSpec(
            DefinitionName: definitionName,
            Kind: profile.Kind,
            Exe: exe,
            Args: args.AsReadOnly(),
            Env: environment,
            Cwd: cwd,
            Cols: options.Cols,
            Rows: options.Rows);

        return new ResolvedAgentTuiLaunch(
            spec,
            profile.Id,
            revision.Id,
            effectiveModelId,
            ActivityModeFor(profile.Kind),
            modelArgument);
    }

    /// <summary>
    /// CARD-0182 D1: the revision's <c>ModelArgumentName</c> is the single authority. Blank means
    /// the program owns its model and nothing is appended, whatever the tier or exact ModelId
    /// (D3 refuses an exact ModelId before we get here). Otherwise exact wins, then a supplied
    /// tier alias, then nothing.
    /// </summary>
    private (string? EffectiveModelId, LaunchModelArgument Provenance) ApplyModelArgument(
        AgentTuiProfile profile,
        AgentTuiProfileRevision revision,
        Agent agent,
        AgentLaunchOptions options,
        List<string> args)
    {
        var exactModelId = string.IsNullOrWhiteSpace(agent.ModelId) ? null : agent.ModelId.Trim();
        var tierAlias = string.IsNullOrWhiteSpace(options.TierModelAlias)
            ? null
            : options.TierModelAlias.Trim();
        var argumentName = string.IsNullOrWhiteSpace(revision.ModelArgumentName)
            ? null
            : revision.ModelArgumentName.Trim();

        if (argumentName is null)
        {
            if (exactModelId is not null)
            {
                throw new ConflictException(
                    "The selected runner profile passes no model argument; clear the exact model or set the profile's model argument name.",
                    "model_argument_unsupported");
            }

            return (null, tierAlias is null
                ? LaunchModelArgument.None
                : LaunchModelArgument.ProfileOwned);
        }

        if (exactModelId is not null)
        {
            EnsureModelAllowed(profile, exactModelId);
            args.Add(argumentName);
            args.Add(exactModelId);
            return (exactModelId, LaunchModelArgument.Exact);
        }

        if (tierAlias is not null)
        {
            args.Add(argumentName);
            args.Add(tierAlias);
            return (null, LaunchModelArgument.Tier);
        }

        return (null, LaunchModelArgument.None);
    }

    private async Task<AgentTuiProfile> LoadProfileAsync(
        Guid? profileId,
        CancellationToken cancellationToken)
    {
        IQueryable<AgentTuiProfile> query = _db.AgentTuiProfiles
            .AsNoTracking()
            .Include(profile => profile.ActiveRevision)
            .Include(profile => profile.Secrets)
            .Include(profile => profile.Models)
            .AsSplitQuery();

        AgentTuiProfile? profile;
        if (profileId is { } selectedId)
        {
            profile = await query.SingleOrDefaultAsync(
                candidate => candidate.Id == selectedId,
                cancellationToken);
            if (profile is null)
            {
                throw new NotFoundException(nameof(AgentTuiProfile), selectedId);
            }
        }
        else
        {
            profile = await query.SingleOrDefaultAsync(
                candidate => candidate.IsDefault,
                cancellationToken);
            if (profile is null)
            {
                throw new ConflictException(
                    "No installation default runner profile is configured.",
                    "profile_not_found");
            }
        }

        return profile;
    }

    private void EnsureModelAllowed(AgentTuiProfile profile, string modelId)
    {
        if (profile.Models.Any(model =>
                string.Equals(model.Identifier, modelId, StringComparison.Ordinal)))
        {
            return;
        }

        if (_runnerCatalog.Get(profile.Kind).CuratedModels.Any(model =>
                string.Equals(model.Identifier, modelId, StringComparison.Ordinal)))
        {
            return;
        }

        throw new ConflictException(
            "The selected model is not part of the profile catalogue.",
            "model_not_in_profile");
    }

    private static void ApplyClaudeEnvironmentDefaults(
        AgentKind kind,
        IDictionary<string, string> environment)
    {
        if (kind != AgentKind.ClaudeCode)
            return;
        if (!environment.ContainsKey("DISABLE_AUTOUPDATER"))
            environment["DISABLE_AUTOUPDATER"] = "1";
        if (!environment.ContainsKey("CLAUDE_CODE_DISABLE_ALTERNATE_SCREEN"))
            environment["CLAUDE_CODE_DISABLE_ALTERNATE_SCREEN"] = "1";
        foreach (var marker in ClaudeNestingMarkers)
        {
            if (!environment.ContainsKey(marker))
                environment[marker] = string.Empty;
        }
    }

    private static void ApplyGrokEnvironmentDefaults(
        AgentKind kind,
        IDictionary<string, string> environment)
    {
        if (kind != AgentKind.Grok)
            return;
        if (!environment.ContainsKey("GROK_TELEMETRY_ENABLED"))
            environment["GROK_TELEMETRY_ENABLED"] = "0";
        if (!environment.ContainsKey("GROK_FEEDBACK_ENABLED"))
            environment["GROK_FEEDBACK_ENABLED"] = "0";
    }

    // CARD-0080 S2 landed the Grok tailer; structured turn-completion is now the catalog fact,
    // not a Claude-only kind list. Undefined kinds stay Unknown (catalog.For throws).
    public static AgentTuiLaunchActivityMode ActivityModeFor(AgentKind kind)
    {
        if (!Enum.IsDefined(kind))
            return AgentTuiLaunchActivityMode.Unknown;
        return ProviderContractCatalog.For(kind).TurnCompletion.Signal
            == TurnCompletionSignal.StructuredTranscript
            ? AgentTuiLaunchActivityMode.Structured
            : AgentTuiLaunchActivityMode.QuietTime;
    }

    private static string[] DeserializeArray(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return [];
        return JsonSerializer.Deserialize<string[]>(json) ?? [];
    }

    private static Dictionary<string, string> DeserializeDictionary(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new Dictionary<string, string>(StringComparer.Ordinal);
        return JsonSerializer.Deserialize<Dictionary<string, string>>(json)
               ?? new Dictionary<string, string>(StringComparer.Ordinal);
    }
}
