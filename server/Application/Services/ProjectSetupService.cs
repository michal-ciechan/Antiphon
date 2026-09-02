using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// CARD-0032 — project readiness projection, setup catalog, and the transactional setup write.
/// Readiness is computed per request from rows + disk + <see cref="DelegationSettings"/>; nothing
/// here is stored.
/// </summary>
public sealed class ProjectSetupService
{
    private readonly AppDbContext _db;
    private readonly DelegationWorkspaceResolver _resolver;
    private readonly GitWorkspaceService _git;
    private readonly ProjectReadinessCache _readinessCache;
    private readonly IServiceScopeFactory? _scopeFactory;
    private readonly DelegationSettings _delegation;
    private readonly ILogger<ProjectSetupService> _logger;
    private readonly ProjectService? _projectService;
    private readonly BoardService? _boardService;
    private readonly AgentService? _agentService;
    private readonly AgentControlService? _agentControlService;
    private readonly IDirectoryWriter? _directoryWriter;

    public ProjectSetupService(
        AppDbContext db,
        DelegationWorkspaceResolver resolver,
        IOptions<DelegationSettings> delegation,
        ILogger<ProjectSetupService> logger,
        ProjectService? projectService = null,
        BoardService? boardService = null,
        AgentService? agentService = null,
        AgentControlService? agentControlService = null,
        IDirectoryWriter? directoryWriter = null,
        GitWorkspaceService? git = null,
        ProjectReadinessCache? readinessCache = null,
        IServiceScopeFactory? scopeFactory = null)
    {
        _db = db;
        _resolver = resolver;
        _git = git ?? new GitWorkspaceService(Microsoft.Extensions.Logging.Abstractions.NullLogger<GitWorkspaceService>.Instance);
        _readinessCache = readinessCache ?? new ProjectReadinessCache(
            new Microsoft.Extensions.Caching.Memory.MemoryCache(new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions()),
            Options.Create(new ProjectsSettings()));
        _scopeFactory = scopeFactory;
        _delegation = delegation.Value;
        _logger = logger;
        _projectService = projectService;
        _boardService = boardService;
        _agentService = agentService;
        _agentControlService = agentControlService;
        _directoryWriter = directoryWriter;
    }

    public Task<ProjectReadinessDto> GetReadinessAsync(Guid projectId, CancellationToken ct) =>
        _readinessCache.GetOrCreateAsync(projectId, () => ComputeReadinessAsync(projectId, ct));

    /// <summary>
    /// Computes a bounded batch without sharing this scoped DbContext between parallel workers.
    /// One bad project deliberately becomes one bad row rather than failing the whole list.
    /// </summary>
    public async Task<IReadOnlyList<ProjectReadinessDto>> GetReadinessBatchAsync(
        IReadOnlyList<Guid> projectIds, CancellationToken ct)
    {
        var results = new ProjectReadinessDto[projectIds.Count];
        await Parallel.ForEachAsync(
            Enumerable.Range(0, projectIds.Count),
            new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = ct },
            async (index, token) =>
            {
                var projectId = projectIds[index];
                try
                {
                    results[index] = _scopeFactory is null
                        ? await GetReadinessAsync(projectId, token)
                        : await _readinessCache.GetOrCreateAsync(projectId, async () =>
                        {
                            await using var scope = _scopeFactory.CreateAsyncScope();
                            var service = scope.ServiceProvider.GetRequiredService<ProjectSetupService>();
                            return await service.ComputeReadinessAsync(projectId, token);
                        });
                }
                catch (Exception ex) when (ex is not OperationCanceledException || !token.IsCancellationRequested)
                {
                    results[index] = ErrorReadiness(projectId, ex);
                }
            });
        return results;
    }

    internal async Task<ProjectReadinessDto> ComputeReadinessAsync(Guid projectId, CancellationToken ct)
    {
        var project = await _db.Projects
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == projectId, ct)
            ?? throw new NotFoundException(nameof(Project), projectId);

        var boards = await _db.Boards
            .AsNoTracking()
            .Include(b => b.Columns)
            .Where(b => b.ProjectId == projectId)
            .ToListAsync(ct);
        var boardIds = boards.Select(b => b.Id).ToList();

        var standingAgents = await _db.Agents
            .AsNoTracking()
            .Include(a => a.TuiProfile)
            .Where(a => !a.IsPoolDelegate)
            .Where(a => a.BoardId != null && boardIds.Contains(a.BoardId.Value))
            .ToListAsync(ct);

        if (!string.IsNullOrWhiteSpace(project.LocalRepositoryPath))
        {
            var normalized = DelegationWorkspaceResolver.NormalizeSeparators(project.LocalRepositoryPath)
                .ToLowerInvariant();
            var pathMatched = await _db.Agents
                .AsNoTracking()
                .Include(a => a.TuiProfile)
                .Where(a => !a.IsPoolDelegate)
                .Where(a => a.WorkingDirectory.Replace("/", "\\").ToLower() == normalized)
                .ToListAsync(ct);
            foreach (var agent in pathMatched)
            {
                if (standingAgents.Any(existing => existing.Id == agent.Id))
                    continue;
                standingAgents.Add(agent);
            }
        }

        var primary = standingAgents
            .OrderByDescending(a => a.BoardId is { } id && boardIds.Contains(id))
            .ThenByDescending(a => a.AlwaysOn)
            .ThenBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

        var attachments = await AgentBundleAttachments.LoadAsync(
            _db, standingAgents.Select(a => a.Id).ToList(), _logger, ct);
        var defaultProfile = await _db.AgentTuiProfiles
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.IsDefault, ct);
        var anyTemplate = await _db.WorkflowTemplates.AnyAsync(ct);
        var channelBound = standingAgents.Count > 0
            && await _db.ChatChannels.AnyAsync(
                c => c.AgentId != null && standingAgents.Select(a => a.Id).Contains(c.AgentId.Value),
                ct);

        var checks = new List<ReadinessCheckDto>
        {
            DirectoryCheck(project),
            await GitRepositoryCheckAsync(project, ct),
            BoardCheck(boards),
            AgentCheck(standingAgents),
            AgentRunnerCheck(primary, defaultProfile),
            AgentDirectoryCheck(primary),
            DelegationRootCheck(project),
            WorkflowTemplateCheck(anyTemplate, primary),
            OrchestratorCheck(standingAgents, boardIds, attachments),
            ChannelCheck(channelBound),
            GitHubCheck(project),
        };

        var canDispatch = !checks.Any(c =>
            c.Level == ReadinessLevel.Required && c.Status == ReadinessStatus.Missing);
        return new ProjectReadinessDto(project.Id, canDispatch, checks);
    }

    public async Task<ProjectSetupCatalogDto> GetCatalogAsync(CancellationToken ct)
    {
        var profiles = await _db.AgentTuiProfiles
            .AsNoTracking()
            .Where(p => p.IsEnabled)
            .OrderBy(p => p.DisplayName)
            .Select(p => new AgentTuiProfileSummaryDto(
                p.Id,
                p.DisplayName,
                p.Kind,
                p.IsDefault,
                p.ActiveRevisionId != null))
            .ToListAsync(ct);

        var roots = _delegation.AllowedRoots.Where(r => !string.IsNullOrWhiteSpace(r)).ToList();
        return new ProjectSetupCatalogDto(
            ModelLevels: ModelLevelCatalog(),
            ReplyStyles: ReplyStyleCatalog(),
            Bundles: [.. InstructionBundles.Attachable.Select(b =>
                new InstructionBundleDto(b.Key, b.Version, b.Stamp, b.Summary, b.Text.Length))],
            Profiles: profiles,
            Presets: AgentPresets.All,
            Delegation: new DelegationSummaryDto(
                roots,
                roots.Count == 0,
                _delegation.MaxConcurrentTasks,
                _delegation.MaxCostUsdPerRoot,
                _delegation.MaxDepth,
                _delegation.DefaultLevel));
    }

    /// <summary>
    /// Creates the project chain in one database transaction. Starting an agent deliberately happens
    /// after commit: a session launch cannot be rolled back, and a refusal leaves useful setup rows.
    /// </summary>
    public async Task<ProjectSetupResultDto> SetupAsync(ProjectSetupRequest request, CancellationToken ct)
    {
        var projectService = _projectService ?? throw new InvalidOperationException("Project setup write services are not configured.");
        var boardService = _boardService ?? throw new InvalidOperationException("Project setup write services are not configured.");
        var agentService = _agentService ?? throw new InvalidOperationException("Project setup write services are not configured.");

        if (string.IsNullOrWhiteSpace(request.Directory))
            throw new ValidationException("directory", "Directory is required.");

        string directory;
        try
        {
            directory = Path.GetFullPath(request.Directory.Trim());
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new ValidationException("directory", "Directory is not a usable path.");
        }

        var notes = new List<string>();
        if (!Directory.Exists(directory) && request.CreateDirectory)
        {
            (_directoryWriter ?? throw new InvalidOperationException("Directory writer is not configured."))
                .CreateDirectory(directory);
            notes.Add($"Directory created: {directory}");
        }
        if (!Directory.Exists(directory))
            throw new ValidationException("directory", $"Directory does not exist: {directory}. Select create directory to create it.");

        var toplevel = await _resolver.GetRepoToplevelAsync(directory, ct);
        if (toplevel is not null && !AgentService.PathsMatch(toplevel, directory))
        {
            throw new ValidationException(
                "directory",
                $"Directory '{directory}' is inside the repository at '{toplevel}'. Use the repository root instead.");
        }

        if (await agentService.FindProjectForWorkingDirectoryAsync(directory, ct) is { } existing)
        {
            throw new ConflictException(
                $"Directory '{directory}' already belongs to project '{existing.Name}' ({existing.Id}).");
        }

        var name = string.IsNullOrWhiteSpace(request.Name)
            ? AgentService.DeriveProjectName(directory, "Project")
            : request.Name.Trim();
        var gitUrl = request.GitRepositoryUrl?.Trim();
        if (string.IsNullOrWhiteSpace(gitUrl) && toplevel is not null)
        {
            gitUrl = await _git.GetOriginUrlAsync(toplevel, ct);
            if (!string.IsNullOrWhiteSpace(gitUrl))
                notes.Add("Git remote read from the checkout.");
        }
        gitUrl ??= string.Empty;

        var boardName = string.IsNullOrWhiteSpace(request.BoardName) ? name : request.BoardName.Trim();
        ProjectDto project;
        BoardSummaryDto board;
        AgentDetailDto? agent = null;

        await using (var transaction = await _db.Database.BeginTransactionAsync(ct))
        {
            project = await projectService.CreateAsync(
                new CreateProjectRequest(
                    name,
                    gitUrl,
                    ConstitutionPath: null,
                    GitHubIntegrationEnabled: false,
                    NotificationsEnabled: false,
                    LocalRepositoryPath: directory,
                    BaseBranch: request.BaseBranch),
                ct);

            var createdBoard = await boardService.CreateAsync(
                new CreateBoardRequest(project.Id, boardName, MaxConcurrentSessions: request.BoardMaxConcurrentSessions),
                ct);
            board = new BoardSummaryDto(
                createdBoard.Id,
                createdBoard.ProjectId,
                createdBoard.ProjectName,
                createdBoard.Name,
                createdBoard.Description,
                createdBoard.TrackerKind,
                createdBoard.MaxConcurrentSessions,
                CardCount: 0,
                CreatedAt: createdBoard.CreatedAt,
                UpdatedAt: createdBoard.UpdatedAt);

            if (request.Agent is { } setupAgent)
            {
                var preset = AgentPresets.Find(setupAgent.Preset);
                if (!string.IsNullOrWhiteSpace(setupAgent.Preset) && preset is null)
                    throw new ValidationException("agent.preset", $"Unknown agent preset '{setupAgent.Preset}'.");

                agent = await agentService.CreateAsync(
                    new CreateAgentRequest(
                        Name: string.IsNullOrWhiteSpace(setupAgent.Name)
                            ? preset is null ? $"{project.Name} Agent" : AgentPresets.RenderName(preset.NamePattern, project.Name)
                            : setupAgent.Name.Trim(),
                        WorkingDirectory: directory,
                        CreateWorkingDirectory: false,
                        ModelLevel: setupAgent.ModelLevel,
                        TuiProfileId: setupAgent.TuiProfileId,
                        ModelId: setupAgent.ModelId,
                        ReplyStyle: setupAgent.ReplyStyle ?? AgentReplyStyle.Normal,
                        AlwaysOn: setupAgent.AlwaysOn,
                        RemoteControlEnabled: setupAgent.RemoteControlEnabled,
                        BoardId: board.Id,
                        BundleKeys: setupAgent.BundleKeys,
                        SystemPromptAppend: setupAgent.SystemPromptAppend,
                        Preset: setupAgent.Preset),
                    ct);
            }

            await transaction.CommitAsync(ct);
        }

        if (request.StartAgent && agent is not null)
        {
            try
            {
                agent = await (_agentControlService ?? throw new InvalidOperationException("Agent control service is not configured."))
                    .StartAsync(agent.Id, new StartAgentRequest(), ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                notes.Add($"Agent start was refused: {ex.Message}");
            }
        }

        var readiness = await GetReadinessAsync(project.Id, ct);
        return new ProjectSetupResultDto(project, board, agent, readiness, notes);
    }

    private static IReadOnlyList<ModelLevelDto> ModelLevelCatalog()
    {
        var kinds = new[] { AgentKind.ClaudeCode, AgentKind.Grok, AgentKind.Codex };
        return Enum.GetValues<AgentModelLevel>().Select(level =>
        {
            var aliases = kinds.ToDictionary(
                k => k.ToString(),
                k => ModelLevelAliases.For(k, level),
                StringComparer.Ordinal);
            var (label, blurb) = level switch
            {
                AgentModelLevel.Frontier => (
                    "Frontier",
                    "The most capable model — the very hardest tasks, highest level of reasoning."),
                AgentModelLevel.High => (
                    "High (default)",
                    "Frontier-grade daily driver — hard tasks, deep reasoning."),
                AgentModelLevel.Medium => (
                    "Medium",
                    "Balanced — strong everyday reasoning at moderate cost and token usage."),
                AgentModelLevel.Low => (
                    "Low",
                    "Fast and light — simple tasks and quick replies."),
                _ => (level.ToString(), string.Empty),
            };
            return new ModelLevelDto(level.ToString(), label, blurb, aliases);
        }).ToList();
    }

    private static IReadOnlyList<ReplyStyleDto> ReplyStyleCatalog() =>
    [
        new("Normal", "Normal", "No style instruction at all — the model writes the way it does by default."),
        new("Terse", "Terse", "Answer first, one line where one line will do. No preamble, no sign-off."),
        new("Caveman", "Caveman", "Short word. Drop small word. Paths, flags and code still written exactly."),
        new("Brief", "Brief", "Short bullets, minimum words, only what changes a decision. Paths, flags and code still exact."),
        new("Explanatory", "Explanatory", "Answer first, then the reasoning: alternatives, what it depends on, where it was read."),
    ];

    private static ReadinessCheckDto DirectoryCheck(Project project)
    {
        if (string.IsNullOrWhiteSpace(project.LocalRepositoryPath))
        {
            return Check(
                ReadinessKeys.Directory,
                ReadinessLevel.Required,
                ReadinessStatus.Missing,
                "No local directory is set on this project.",
                "A local path is what Start and card worktrees run in. Without one the project cannot dispatch.",
                new ReadinessFixDto("Edit project", "/settings?tab=projects", null));
        }

        if (!Directory.Exists(project.LocalRepositoryPath))
        {
            return Check(
                ReadinessKeys.Directory,
                ReadinessLevel.Required,
                ReadinessStatus.Missing,
                $"Directory does not exist: {project.LocalRepositoryPath}.",
                "Start refuses with 409 until this path exists on disk.",
                new ReadinessFixDto("Create the directory", "/settings?tab=projects", "create-directory"));
        }

        return Check(
            ReadinessKeys.Directory,
            ReadinessLevel.Required,
            ReadinessStatus.Ok,
            $"Directory exists: {project.LocalRepositoryPath}.",
            null,
            null);
    }

    private async Task<ReadinessCheckDto> GitRepositoryCheckAsync(Project project, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(project.LocalRepositoryPath)
            || !Directory.Exists(project.LocalRepositoryPath))
        {
            return Check(
                ReadinessKeys.GitRepository,
                ReadinessLevel.Recommended,
                ReadinessStatus.NotApplicable,
                "No directory to inspect for a git repository.",
                "Worktree tasks need a git toplevel. Set a local path first.",
                null);
        }

        var toplevel = await _git.GetRepoToplevelAsync(project.LocalRepositoryPath, ct);
        if (toplevel is null)
        {
            return Check(
                ReadinessKeys.GitRepository,
                ReadinessLevel.Recommended,
                ReadinessStatus.Warning,
                $"'{project.LocalRepositoryPath}' is not a git repository — worktree tasks will not be available.",
                "A plain directory is allowed. Card worktrees and -Worktree tasks need `git init` (or a clone) first.",
                null);
        }

        if (!AgentService.PathsMatch(toplevel, project.LocalRepositoryPath))
        {
            return Check(
                ReadinessKeys.GitRepository,
                ReadinessLevel.Recommended,
                ReadinessStatus.Warning,
                $"'{project.LocalRepositoryPath}' sits inside the repository at '{toplevel}', not at its root.",
                "Worktree tasks use the repository toplevel. Point the project at the root instead.",
                new ReadinessFixDto("Use the repository root", "/settings?tab=projects", null));
        }

        return Check(
            ReadinessKeys.GitRepository,
            ReadinessLevel.Recommended,
            ReadinessStatus.Ok,
            $"Git repository at {toplevel}.",
            null,
            null);
    }

    private static ReadinessCheckDto BoardCheck(IReadOnlyList<Board> boards)
    {
        var withActive = boards.FirstOrDefault(b =>
            b.Columns.Any(c => c.IsActive && !c.IsTerminal));
        if (withActive is null)
        {
            var reason = boards.Count == 0
                ? "This project has no board."
                : "This project has a board, but none of its columns is active and non-terminal.";
            return Check(
                ReadinessKeys.Board,
                ReadinessLevel.Required,
                ReadinessStatus.Missing,
                reason,
                "Moving a card into an active column is what starts a session. Default boards use In Progress.",
                new ReadinessFixDto("Create a board", "/boards", null));
        }

        return Check(
            ReadinessKeys.Board,
            ReadinessLevel.Required,
            ReadinessStatus.Ok,
            $"Board '{withActive.Name}' has an active column.",
            null,
            null);
    }

    private static ReadinessCheckDto AgentCheck(IReadOnlyList<Agent> agents)
    {
        if (agents.Count == 0)
        {
            return Check(
                ReadinessKeys.Agent,
                ReadinessLevel.Required,
                ReadinessStatus.Missing,
                "No standing agent is linked to this project.",
                "A card needs an agent on the board (or one whose working directory matches the project). Pool delegates do not count.",
                new ReadinessFixDto("Add an agent", "/agents", null));
        }

        return Check(
            ReadinessKeys.Agent,
            ReadinessLevel.Required,
            ReadinessStatus.Ok,
            agents.Count == 1
                ? $"Agent '{agents[0].Name}' is linked to this project."
                : $"{agents.Count} standing agents are linked to this project.",
            null,
            null);
    }

    private static ReadinessCheckDto AgentRunnerCheck(Agent? primary, AgentTuiProfile? defaultProfile)
    {
        if (primary is null)
        {
            return Check(
                ReadinessKeys.AgentRunner,
                ReadinessLevel.Required,
                ReadinessStatus.NotApplicable,
                "No agent to check a runner profile on.",
                "Add an agent first.",
                null);
        }

        var profile = primary.TuiProfile ?? defaultProfile;
        if (profile is null)
        {
            return Check(
                ReadinessKeys.AgentRunner,
                ReadinessLevel.Required,
                ReadinessStatus.Missing,
                $"Agent '{primary.Name}' has no runner profile, and no installation default exists.",
                "Start refuses until an enabled profile with an active revision is selected.",
                new ReadinessFixDto("Open AI Agent TUI settings", "/settings?tab=agent-tui", null));
        }

        if (!profile.IsEnabled)
        {
            return Check(
                ReadinessKeys.AgentRunner,
                ReadinessLevel.Required,
                ReadinessStatus.Missing,
                $"Runner profile '{profile.DisplayName}' is disabled.",
                "Start returns 409 profile_disabled until the profile is enabled.",
                new ReadinessFixDto("Open AI Agent TUI settings", "/settings?tab=agent-tui", null));
        }

        if (profile.ActiveRevisionId is null)
        {
            return Check(
                ReadinessKeys.AgentRunner,
                ReadinessLevel.Required,
                ReadinessStatus.Missing,
                $"Runner profile '{profile.DisplayName}' has no active revision.",
                "Start returns 409 profile_not_validated until a revision is active.",
                new ReadinessFixDto("Open AI Agent TUI settings", "/settings?tab=agent-tui", null));
        }

        return Check(
            ReadinessKeys.AgentRunner,
            ReadinessLevel.Required,
            ReadinessStatus.Ok,
            $"Runner profile '{profile.DisplayName}' is enabled with an active revision.",
            null,
            null);
    }

    private static ReadinessCheckDto AgentDirectoryCheck(Agent? primary)
    {
        if (primary is null)
        {
            return Check(
                ReadinessKeys.AgentDirectory,
                ReadinessLevel.Required,
                ReadinessStatus.NotApplicable,
                "No agent to check a working directory on.",
                "Add an agent first.",
                null);
        }

        if (!Directory.Exists(primary.WorkingDirectory))
        {
            return Check(
                ReadinessKeys.AgentDirectory,
                ReadinessLevel.Required,
                ReadinessStatus.Missing,
                $"Agent '{primary.Name}' working directory does not exist: {primary.WorkingDirectory}.",
                "Start refuses with 409 until this path exists.",
                new ReadinessFixDto(
                    "Create directory",
                    $"/agents?agent={primary.Id}",
                    "create-directory"));
        }

        return Check(
            ReadinessKeys.AgentDirectory,
            ReadinessLevel.Required,
            ReadinessStatus.Ok,
            $"Agent '{primary.Name}' working directory exists.",
            null,
            null);
    }

    private ReadinessCheckDto DelegationRootCheck(Project project)
    {
        // Recommended-shaped: gates the UI's "Delegate a task" button, never CanDispatch.
        if (string.IsNullOrWhiteSpace(project.LocalRepositoryPath))
        {
            return Check(
                ReadinessKeys.DelegationRoot,
                ReadinessLevel.Recommended,
                ReadinessStatus.NotApplicable,
                "No local directory to compare against allowed roots.",
                null,
                null);
        }

        string full;
        try
        {
            full = Path.GetFullPath(project.LocalRepositoryPath);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return Check(
                ReadinessKeys.DelegationRoot,
                ReadinessLevel.Recommended,
                ReadinessStatus.Warning,
                $"'{project.LocalRepositoryPath}' is not a usable directory path.",
                DelegationOutsideWording(project.LocalRepositoryPath, matchingRoot: null),
                null);
        }

        var roots = _delegation.AllowedRoots.Where(r => !string.IsNullOrWhiteSpace(r)).ToList();
        var matching = roots.FirstOrDefault(root => DelegationWorkspaceResolver.IsWithinRoot(full, root));
        if (matching is not null)
        {
            return Check(
                ReadinessKeys.DelegationRoot,
                ReadinessLevel.Recommended,
                ReadinessStatus.Ok,
                $"Tasks may be created here from the UI and from scripts: '{full}' is under the allowed root '{Path.GetFullPath(matching)}'.",
                null,
                null);
        }

        return Check(
            ReadinessKeys.DelegationRoot,
            ReadinessLevel.Recommended,
            ReadinessStatus.Warning,
            DelegationOutsideWording(full, matchingRoot: null),
            DelegationJsonHint(full),
            null);
    }

    internal static string DelegationOutsideWording(string directory, string? matchingRoot)
    {
        if (matchingRoot is not null)
        {
            return $"Tasks may be created here from the UI and from scripts: '{directory}' is under the allowed root '{matchingRoot}'.";
        }

        return
            $"A task created from this screen, or from `delegate.ps1` in a plain shell, must run under an allowed root, and '{directory}' is not under one. "
            + "This is a security boundary (`Delegation:AllowedRoots` in `server/appsettings.json`), so it is not changed from here. "
            + "You do not need it for: a card moved into In Progress with Spawn (the session starts in the agent's own directory); "
            + "a task delegated from inside that agent's session (a session inherits its own directory as its root); "
            + "or an always-on orchestrator working the board. "
            + "Add the root only if you want to dispatch into this directory from the UI or a plain shell.";
    }

    internal static string DelegationJsonHint(string directory) =>
        $"`AllowedRoots` empty means each caller's own tree only, which is the safe default. To permit this path, add: \"AllowedRoots\": [ \"{directory.Replace("\\", "\\\\", StringComparison.Ordinal)}\" ]";

    private static ReadinessCheckDto WorkflowTemplateCheck(bool anyTemplate, Agent? primary)
    {
        if (!anyTemplate)
        {
            return Check(
                ReadinessKeys.WorkflowTemplate,
                ReadinessLevel.Required,
                ReadinessStatus.Missing,
                "No workflow template exists.",
                "Spawning a card requires at least one workflow template.",
                new ReadinessFixDto("Create a workflow template", "/settings?tab=templates", null));
        }

        var detail = primary?.DefaultWorkflowTemplateId is { } id
            ? $"At least one workflow template exists; agent '{primary.Name}' defaults to {id}."
            : "At least one workflow template exists.";
        return Check(
            ReadinessKeys.WorkflowTemplate,
            ReadinessLevel.Required,
            ReadinessStatus.Ok,
            detail,
            null,
            null);
    }

    private static ReadinessCheckDto OrchestratorCheck(
        IReadOnlyList<Agent> agents,
        IReadOnlyList<Guid> boardIds,
        IReadOnlyDictionary<Guid, IReadOnlyList<string>> attachments)
    {
        var match = agents.FirstOrDefault(a =>
            a.AlwaysOn
            && a.BoardId is { } boardId
            && boardIds.Contains(boardId)
            && attachments.TryGetValue(a.Id, out var keys)
            && keys.Contains(InstructionBundles.Orchestrator, StringComparer.Ordinal)
            && keys.Contains(InstructionBundles.BoardApi, StringComparer.Ordinal));

        if (match is null)
        {
            return Check(
                ReadinessKeys.Orchestrator,
                ReadinessLevel.Recommended,
                ReadinessStatus.Missing,
                "No standing orchestrator is watching this board.",
                "A standing orchestrator is AlwaysOn with the orchestrator and board-api bundles attached. Cards can still dispatch without one — you spawn them yourself.",
                new ReadinessFixDto("Open agent settings", "/agents", null));
        }

        return Check(
            ReadinessKeys.Orchestrator,
            ReadinessLevel.Recommended,
            ReadinessStatus.Ok,
            $"Standing orchestrator '{match.Name}' is AlwaysOn with orchestrator and board-api attached.",
            null,
            null);
    }

    private static ReadinessCheckDto ChannelCheck(bool bound)
    {
        if (!bound)
        {
            return Check(
                ReadinessKeys.Channel,
                ReadinessLevel.Optional,
                ReadinessStatus.Missing,
                "No channel is bound to an agent of this project.",
                "Optional. Bind a Telegram or Slack channel if you want this project reachable from chat.",
                new ReadinessFixDto("Bind a channel", "/channels", null));
        }

        return Check(
            ReadinessKeys.Channel,
            ReadinessLevel.Optional,
            ReadinessStatus.Ok,
            "A channel is bound to an agent of this project.",
            null,
            null);
    }

    private static ReadinessCheckDto GitHubCheck(Project project)
    {
        if (string.IsNullOrWhiteSpace(project.GitRepositoryUrl))
        {
            return Check(
                ReadinessKeys.GitHub,
                ReadinessLevel.Optional,
                ReadinessStatus.Missing,
                "No git repository URL is set.",
                "Optional. A local-only project can dispatch without a remote.",
                new ReadinessFixDto("Edit project", "/settings?tab=projects", null));
        }

        var looksGitHub = project.GitRepositoryUrl.Contains("github", StringComparison.OrdinalIgnoreCase);
        if (looksGitHub && !project.GitHubIntegrationEnabled)
        {
            return Check(
                ReadinessKeys.GitHub,
                ReadinessLevel.Optional,
                ReadinessStatus.Warning,
                $"Git repository URL is set ({project.GitRepositoryUrl}) but GitHub integration is off.",
                "Turn on GitHub integration if you want PRs and issue sync for this project.",
                new ReadinessFixDto("Edit project", "/settings?tab=projects", null));
        }

        return Check(
            ReadinessKeys.GitHub,
            ReadinessLevel.Optional,
            ReadinessStatus.Ok,
            looksGitHub
                ? $"GitHub repository {project.GitRepositoryUrl} with integration enabled."
                : $"Git repository URL is set: {project.GitRepositoryUrl}.",
            null,
            null);
    }

    private static ReadinessCheckDto Check(
        string key,
        ReadinessLevel level,
        ReadinessStatus status,
        string summary,
        string? detail,
        ReadinessFixDto? fix) =>
        new(key, level, status, summary, detail, fix);

    private static ProjectReadinessDto ErrorReadiness(Guid projectId, Exception exception) =>
        new(
            projectId,
            CanDispatch: false,
            Checks:
            [
                Check(
                    ReadinessKeys.GitRepository,
                    ReadinessLevel.Recommended,
                    ReadinessStatus.Warning,
                    "Readiness could not be checked for this project.",
                    exception.Message,
                    null),
            ]);
}
