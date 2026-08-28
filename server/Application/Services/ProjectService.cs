using System.Net.Http.Headers;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Services;

public class ProjectService
{
    private readonly AppDbContext _db;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly GithubSettings _githubSettings;
    private readonly ILogger<ProjectService> _logger;
    private readonly ProjectReadinessCache? _readinessCache;

    public ProjectService(
        AppDbContext db,
        IHttpClientFactory httpClientFactory,
        IOptions<GithubSettings> githubSettings,
        ILogger<ProjectService> logger,
        ProjectReadinessCache? readinessCache = null)
    {
        _db = db;
        _httpClientFactory = httpClientFactory;
        _githubSettings = githubSettings.Value;
        _logger = logger;
        _readinessCache = readinessCache;
    }

    public async Task<List<ProjectDto>> GetAllAsync(CancellationToken cancellationToken)
    {
        var projects = await _db.Projects
            .OrderBy(p => p.Name)
            .ToListAsync(cancellationToken);

        return projects.Select(ToDto).ToList();
    }

    public async Task<ProjectDto> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        var project = await _db.Projects
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken)
            ?? throw new NotFoundException(nameof(Project), id);

        return ToDto(project);
    }

    public async Task<ProjectDto> CreateAsync(
        CreateProjectRequest request, CancellationToken cancellationToken)
    {
        ValidateRequest(request.Name, request.GitRepositoryUrl, request.LocalRepositoryPath);

        var project = new Project
        {
            Id = Guid.NewGuid(),
            Name = request.Name,
            GitRepositoryUrl = request.GitRepositoryUrl,
            LocalRepositoryPath = string.IsNullOrWhiteSpace(request.LocalRepositoryPath)
                ? null
                : request.LocalRepositoryPath,
            BaseBranch = string.IsNullOrWhiteSpace(request.BaseBranch) ? "master" : request.BaseBranch,
            ConstitutionPath = request.ConstitutionPath ?? "AGENTS.md;CLAUDE.md;README.md",
            GitHubIntegrationEnabled = request.GitHubIntegrationEnabled,
            NotificationsEnabled = request.NotificationsEnabled,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            DefaultLaunchEnvJson = request.DefaultLaunchEnv is null
                ? "{}"
                : AgentLaunchEnv.Serialize(
                    AgentLaunchEnv.ValidateOverride(request.DefaultLaunchEnv, "defaultLaunchEnv")),
        };

        _db.Projects.Add(project);
        await _db.SaveChangesAsync(cancellationToken);
        _readinessCache?.Remove(project.Id);

        _logger.LogInformation("Created project {ProjectName} ({ProjectId})", project.Name, project.Id);

        return ToDto(project);
    }

    public async Task<ProjectDto> UpdateAsync(
        Guid id, UpdateProjectRequest request, CancellationToken cancellationToken)
    {
        var project = await _db.Projects
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken)
            ?? throw new NotFoundException(nameof(Project), id);

        ValidateRequest(request.Name, request.GitRepositoryUrl, request.LocalRepositoryPath);

        project.Name = request.Name;
        project.GitRepositoryUrl = request.GitRepositoryUrl;
        project.LocalRepositoryPath = string.IsNullOrWhiteSpace(request.LocalRepositoryPath)
            ? null
            : request.LocalRepositoryPath;
        project.BaseBranch = string.IsNullOrWhiteSpace(request.BaseBranch) ? "master" : request.BaseBranch;
        project.ConstitutionPath = request.ConstitutionPath ?? "AGENTS.md;CLAUDE.md;README.md";
        project.GitHubIntegrationEnabled = request.GitHubIntegrationEnabled;
        project.NotificationsEnabled = request.NotificationsEnabled;
        if (request.DefaultLaunchEnv is not null)
        {
            project.DefaultLaunchEnvJson = AgentLaunchEnv.Serialize(
                AgentLaunchEnv.ValidateOverride(request.DefaultLaunchEnv, "defaultLaunchEnv"));
        }

        project.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(cancellationToken);
        _readinessCache?.Remove(project.Id);

        _logger.LogInformation("Updated project {ProjectName} ({ProjectId})", project.Name, project.Id);

        return ToDto(project);
    }

    /// <summary>
    /// Reports what deleting this project would destroy or detach, without touching anything.
    /// </summary>
    public async Task<ProjectDeletionImpactDto> GetDeletionImpactAsync(
        Guid id, CancellationToken cancellationToken)
    {
        var project = await _db.Projects
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken)
            ?? throw new NotFoundException(nameof(Project), id);

        var counts = await ProjectCascade.MeasureAsync(_db, id, cancellationToken);
        return ToImpactDto(project, counts);
    }

    /// <summary>
    /// Deletes a project. A project that owns boards or cards refuses unless
    /// <paramref name="force"/> is set, and then takes the whole board subtree with it; agents are
    /// detached rather than deleted. Orchestrator workflows block the delete outright — see
    /// <see cref="BlockersFor"/>.
    /// </summary>
    public async Task DeleteAsync(Guid id, bool force, CancellationToken cancellationToken)
    {
        var project = await _db.Projects
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken)
            ?? throw new NotFoundException(nameof(Project), id);

        var counts = await ProjectCascade.MeasureAsync(_db, id, cancellationToken);
        var impact = ToImpactDto(project, counts);

        if (!impact.CanDelete)
            throw new ConflictException($"'{project.Name}' cannot be deleted: {string.Join(" ", impact.Blockers)}");

        if (impact.RequiresConfirmation && !force)
        {
            throw new ConflictException(
                $"'{project.Name}' still has {Describe(counts)}. "
                + "Deleting it removes all of that. Re-send with force=true to confirm.");
        }

        var boardIds = await _db.Boards
            .Where(b => b.ProjectId == id)
            .Select(b => b.Id)
            .ToListAsync(cancellationToken);

        // One transaction: the cascade is a dozen separate statements, and a project half-deleted
        // is worse than one not deleted at all.
        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        await ProjectCascade.DeleteBoardsAsync(_db, boardIds, cancellationToken);
        await _db.Projects.Where(p => p.Id == id).ExecuteDeleteAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        _readinessCache?.Remove(id);

        _logger.LogInformation(
            "Deleted project {ProjectName} ({ProjectId}) with {BoardCount} board(s) and {CardCount} card(s)",
            project.Name, project.Id, counts.BoardCount, counts.CardCount);
    }

    private static ProjectDeletionImpactDto ToImpactDto(Project project, ProjectImpactCounts counts) =>
        new(
            project.Id,
            project.Name,
            counts.BoardCount,
            counts.CardCount,
            counts.OpenCardCount,
            counts.RunningSessionCount,
            counts.AgentCount,
            counts.WorkflowCount,
            BlockersFor(counts));

    /// <summary>
    /// Orchestrator workflows anchor cost-ledger entries through a Restrict FK — financial records
    /// that must not disappear behind a confirmation checkbox. They have their own delete screen,
    /// which knows how to unwind them; until they are gone the project stays.
    /// </summary>
    private static IReadOnlyList<string> BlockersFor(ProjectImpactCounts counts)
    {
        var blockers = new List<string>();
        if (counts.WorkflowCount > 0)
        {
            blockers.Add(
                $"{Plural(counts.WorkflowCount, "workflow")} still reference it — delete those first.");
        }

        return blockers;
    }

    private static string Describe(ProjectImpactCounts counts)
    {
        var parts = new List<string>();
        if (counts.BoardCount > 0)
            parts.Add(Plural(counts.BoardCount, "board"));
        if (counts.CardCount > 0)
        {
            parts.Add(counts.OpenCardCount > 0
                ? $"{Plural(counts.CardCount, "card")} ({counts.OpenCardCount} still open)"
                : Plural(counts.CardCount, "card"));
        }
        if (counts.RunningSessionCount > 0)
            parts.Add($"{Plural(counts.RunningSessionCount, "running session")}");
        if (counts.AgentCount > 0)
            parts.Add($"{Plural(counts.AgentCount, "agent")} attached (they will be detached, not deleted)");

        return parts.Count == 0 ? "attached work" : string.Join(", ", parts);
    }

    private static string Plural(int count, string noun) =>
        count == 1 ? $"1 {noun}" : $"{count} {noun}s";

    /// <summary>
    /// Validates git repository connectivity by attempting an HTTP HEAD request to the URL.
    /// For git:// or SSH URLs, falls back to checking URL format validity.
    /// </summary>
    public async Task<TestGitConnectivityResult> TestGitConnectivityAsync(
        string gitRepositoryUrl, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(gitRepositoryUrl))
        {
            return new TestGitConnectivityResult(false, "Git repository URL is required.");
        }

        try
        {
            // For HTTPS URLs, try an HTTP HEAD request to validate reachability
            if (gitRepositoryUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
                gitRepositoryUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            {
                var client = _httpClientFactory.CreateClient();
                client.Timeout = TimeSpan.FromSeconds(10);

                var url = gitRepositoryUrl.TrimEnd('/');
                if (!url.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
                {
                    url += ".git";
                }
                url += "/info/refs?service=git-upload-pack";

                using var request = new HttpRequestMessage(HttpMethod.Get, url);

                // Add PAT auth directly on the request if the URL is on the configured GitHub host
                if (_githubSettings.Enabled && !string.IsNullOrEmpty(_githubSettings.PersonalAccessToken))
                {
                    var repoHost = new Uri(gitRepositoryUrl).Host;
                    var githubHost = new Uri(_githubSettings.BaseUrl).Host;
                    if (string.Equals(repoHost, githubHost, StringComparison.OrdinalIgnoreCase))
                    {
                        var credentials = Convert.ToBase64String(
                            System.Text.Encoding.ASCII.GetBytes($"x:{_githubSettings.PersonalAccessToken}"));
                        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
                        _logger.LogDebug("Added Basic auth for git connectivity test to {Host}", repoHost);
                    }
                }

                using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

                if (response.IsSuccessStatusCode)
                {
                    return new TestGitConnectivityResult(true, "Repository is reachable.");
                }

                return new TestGitConnectivityResult(false,
                    $"Repository returned HTTP {(int)response.StatusCode}. Verify the URL and access permissions.");
            }

            // For SSH or git:// URLs, validate format only
            if (gitRepositoryUrl.StartsWith("git@", StringComparison.OrdinalIgnoreCase) ||
                gitRepositoryUrl.StartsWith("git://", StringComparison.OrdinalIgnoreCase) ||
                gitRepositoryUrl.StartsWith("ssh://", StringComparison.OrdinalIgnoreCase))
            {
                return new TestGitConnectivityResult(true,
                    "SSH/git URL format is valid. Connectivity will be verified when the repository is cloned.");
            }

            return new TestGitConnectivityResult(false,
                "Unrecognized URL scheme. Use HTTPS, SSH, or git:// URLs.");
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Git connectivity test failed for {Url}", gitRepositoryUrl);
            return new TestGitConnectivityResult(false, $"Connection failed: {ex.Message}");
        }
        catch (TaskCanceledException)
        {
            return new TestGitConnectivityResult(false, "Connection timed out.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unexpected error testing git connectivity for {Url}", gitRepositoryUrl);
            return new TestGitConnectivityResult(false, $"Unexpected error: {ex.Message}");
        }
    }

    private static void ValidateRequest(string name, string gitRepositoryUrl, string? localRepositoryPath)
    {
        var errors = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(name))
        {
            errors["name"] = ["Name is required."];
        }

        var hasLocalPath = !string.IsNullOrWhiteSpace(localRepositoryPath);
        if (string.IsNullOrWhiteSpace(gitRepositoryUrl))
        {
            if (!hasLocalPath)
            {
                errors["gitRepositoryUrl"] = ["Git repository URL is required when no local path is set."];
            }
        }
        else if (!Uri.TryCreate(gitRepositoryUrl, UriKind.Absolute, out _) &&
                 !gitRepositoryUrl.StartsWith("git@", StringComparison.OrdinalIgnoreCase))
        {
            errors["gitRepositoryUrl"] = ["Git repository URL must be a valid URL."];
        }

        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }
    }

    private static ProjectDto ToDto(Project entity) =>
        new(
            entity.Id,
            entity.Name,
            entity.GitRepositoryUrl,
            entity.LocalRepositoryPath,
            entity.BaseBranch,
            entity.ConstitutionPath,
            entity.GitHubIntegrationEnabled,
            entity.NotificationsEnabled,
            entity.CreatedAt,
            entity.UpdatedAt,
            AgentLaunchEnv.Parse(entity.DefaultLaunchEnvJson));
}

public record TestGitConnectivityResult(bool Success, string Message);
