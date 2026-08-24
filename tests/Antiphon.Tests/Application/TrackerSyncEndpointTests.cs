using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0166 S7: POST /api/boards/{id}/tracker/sync and /api/tracker-sync/run —
/// Internal → 409, concurrent double-fire → 409 with only one sync executing.
/// </summary>
[NotInParallel]
[ClassDataSource<TrackerSyncEndpointWebAppFactory>(Shared = SharedType.PerClass)]
public class TrackerSyncEndpointTests
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly TrackerSyncEndpointWebAppFactory _factory;
    private Guid _projectId;

    public TrackerSyncEndpointTests(TrackerSyncEndpointWebAppFactory factory) => _factory = factory;

    [Before(Test)]
    public async Task ResetAsync()
    {
        await _factory.ResetAsync();
        _factory.Tracker.ResetGate();
    }

    [After(Test)]
    public async Task CleanupAsync()
    {
        _factory.Tracker.ReleaseGate();
        if (_projectId == Guid.Empty)
            return;

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var boardIds = await db.Boards.Where(b => b.ProjectId == _projectId).Select(b => b.Id).ToListAsync();
        var cardIds = await db.Cards.Where(c => boardIds.Contains(c.BoardId)).Select(c => c.Id).ToListAsync();
        await db.CardComments.Where(c => cardIds.Contains(c.CardId)).ExecuteDeleteAsync();
        await db.CardRevisions.Where(r => cardIds.Contains(r.CardId)).ExecuteDeleteAsync();
        await db.ExternalIssueRefs.Where(r => cardIds.Contains(r.CardId)).ExecuteDeleteAsync();
        await db.Cards.Where(c => boardIds.Contains(c.BoardId)).ExecuteDeleteAsync();
        await db.BoardWorkflowDefinitions.Where(d => boardIds.Contains(d.BoardId)).ExecuteDeleteAsync();
        await db.BoardColumns.Where(c => boardIds.Contains(c.BoardId)).ExecuteDeleteAsync();
        await db.Boards.Where(b => boardIds.Contains(b.Id)).ExecuteDeleteAsync();
        await db.Projects.Where(p => p.Id == _projectId).ExecuteDeleteAsync();
        _projectId = Guid.Empty;
    }

    [Test]
    public async Task Internal_board_sync_returns_409_tracker_inactive()
    {
        var board = await SeedBoardAsync(TrackerKind.Internal);
        using var client = _factory.CreateClient();

        var response = await client.PostAsync($"/api/boards/{board.Id}/tracker/sync", content: null);
        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        var body = await response.Content.ReadAsStringAsync();
        body.ShouldContain("tracker block missing or inactive");
        _factory.Tracker.FetchCandidatesCalls.ShouldBe(0);
    }

    [Test]
    public async Task Missing_board_returns_404()
    {
        using var client = _factory.CreateClient();
        var response = await client.PostAsync($"/api/boards/{Guid.NewGuid()}/tracker/sync", content: null);
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task Concurrent_requests_at_same_board_only_one_runs_sync_other_gets_409()
    {
        var board = await SeedBoardAsync(TrackerKind.GitHubIssues);
        _factory.Tracker.ArmGate();
        using var client = _factory.CreateClient();

        var first = client.PostAsync($"/api/boards/{board.Id}/tracker/sync", content: null);
        await _factory.Tracker.WaitUntilEnteredAsync(TimeSpan.FromSeconds(10));

        var second = await client.PostAsync($"/api/boards/{board.Id}/tracker/sync", content: null);
        second.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        var secondBody = await second.Content.ReadAsStringAsync();
        secondBody.ShouldContain("Sync already running");

        // While the first request holds the per-board lock, only one adapter entry exists.
        _factory.Tracker.SyncEntries.ShouldBe(1);

        _factory.Tracker.ReleaseGate();
        var firstResponse = await first;
        firstResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        var summary = (await firstResponse.Content.ReadFromJsonAsync<TrackerSyncRunResult>(Json))!;
        summary.Boards.Count.ShouldBe(1);
        summary.ConcurrentRunSkipped.ShouldBeFalse();

        // The 409 never acquired the board lock, so it never entered the adapter.
        _factory.Tracker.FetchCandidatesCalls.ShouldBeGreaterThanOrEqualTo(1);
    }

    [Test]
    public async Task Global_run_returns_200_summary_for_external_boards()
    {
        await SeedBoardAsync(TrackerKind.GitHubIssues);
        using var client = _factory.CreateClient();

        var response = await client.PostAsync("/api/tracker-sync/run", content: null);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var summary = (await response.Content.ReadFromJsonAsync<TrackerSyncRunResult>(Json))!;
        summary.Boards.ShouldContain(b => b.BoardId != Guid.Empty);
    }

    private async Task<Board> SeedBoardAsync(TrackerKind kind)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTime.UtcNow;
        var project = new Project
        {
            Id = Guid.NewGuid(),
            Name = $"S7 Project {Guid.NewGuid():N}",
            GitRepositoryUrl = "https://example.test/repo.git",
            LocalRepositoryPath = Path.Combine(Path.GetTempPath(), $"antiphon-s7-{Guid.NewGuid():N}"),
            BaseBranch = "main",
            CreatedAt = now,
            UpdatedAt = now
        };
        Directory.CreateDirectory(project.LocalRepositoryPath!);

        var board = new Board
        {
            Id = Guid.NewGuid(),
            ProjectId = project.Id,
            Name = $"S7 Board {Guid.NewGuid():N}",
            TrackerKind = kind,
            TrackerActivatedAt = kind == TrackerKind.Internal ? null : now,
            MaxConcurrentSessions = 1,
            CreatedAt = now,
            UpdatedAt = now,
            Project = project
        };
        project.Boards.Add(board);

        var column = new BoardColumn
        {
            Id = Guid.NewGuid(),
            BoardId = board.Id,
            StateKey = "backlog",
            Name = "Backlog",
            ColumnOrder = 0,
            CardStatus = CardStatus.Backlog,
            IsActive = false,
            IsTerminal = false,
            CreatedAt = now,
            UpdatedAt = now,
            Board = board
        };
        board.Columns.Add(column);

        if (kind != TrackerKind.Internal)
        {
            board.WorkflowDefinitions.Add(new BoardWorkflowDefinition
            {
                Id = Guid.NewGuid(),
                BoardId = board.Id,
                Version = 1,
                Name = "Tracked",
                Content = """
                    ---
                    tracker:
                      kind: github_issues
                      repository: acme/app
                      active_states: [open]
                    ---
                    Work on {{ issue.identifier }}.
                    """,
                IsActive = true,
                CreatedAt = now,
                UpdatedAt = now,
                Board = board
            });
        }

        db.Projects.Add(project);
        await db.SaveChangesAsync();
        _projectId = project.Id;
        return board;
    }
}

/// <summary>
/// Replaces real issue trackers with a gateable fake so concurrent HTTP requests can be raced.
/// </summary>
public sealed class TrackerSyncEndpointWebAppFactory : AntiphonWebAppFactory
{
    public GatingBidirectionalTracker Tracker { get; } = new(TrackerKind.GitHubIssues);

    protected override void ApplyTestOverrides(IServiceCollection services)
    {
        var existing = services.Where(d => d.ServiceType == typeof(IIssueTracker)).ToList();
        foreach (var d in existing)
            services.Remove(d);

        services.AddSingleton(Tracker);
        services.AddScoped<IIssueTracker>(_ => Tracker);
    }
}

public sealed class GatingBidirectionalTracker(TrackerKind kind) : IBidirectionalIssueTracker
{
    private readonly object _gateLock = new();
    private TaskCompletionSource? _entered;
    private TaskCompletionSource? _release;
    private int _syncEntries;

    public TrackerKind Kind { get; } = kind;
    public int FetchCandidatesCalls { get; private set; }
    public int SyncEntries => Volatile.Read(ref _syncEntries);
    public ConcurrentBag<(string ExternalId, string Body)> PostCommentCalls { get; } = [];

    public void ArmGate()
    {
        lock (_gateLock)
        {
            _entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }

    public void ResetGate()
    {
        lock (_gateLock)
        {
            _entered = null;
            _release?.TrySetResult();
            _release = null;
        }

        Interlocked.Exchange(ref _syncEntries, 0);
        FetchCandidatesCalls = 0;
        PostCommentCalls.Clear();
    }

    public void ReleaseGate()
    {
        TaskCompletionSource? release;
        lock (_gateLock)
            release = _release;
        release?.TrySetResult();
    }

    public async Task WaitUntilEnteredAsync(TimeSpan timeout)
    {
        TaskCompletionSource? entered;
        lock (_gateLock)
            entered = _entered;
        if (entered is null)
            throw new InvalidOperationException("Gate was not armed.");
        using var cts = new CancellationTokenSource(timeout);
        await entered.Task.WaitAsync(cts.Token);
    }

    private async Task EnterGateAsync(CancellationToken ct)
    {
        Interlocked.Increment(ref _syncEntries);
        TaskCompletionSource? entered;
        TaskCompletionSource? release;
        lock (_gateLock)
        {
            entered = _entered;
            release = _release;
        }

        entered?.TrySetResult();
        if (release is not null)
            await release.Task.WaitAsync(ct);
    }

    public async Task<IReadOnlyList<TrackedIssue>> FetchCandidatesAsync(
        IssueTrackerConfig config, CancellationToken ct)
    {
        await EnterGateAsync(ct);
        FetchCandidatesCalls++;
        return [];
    }

    public Task<IReadOnlyList<TrackedIssue>> FetchByStatesAsync(
        IssueTrackerConfig config, IReadOnlyList<string> states, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<TrackedIssue>>([]);

    public Task<IReadOnlyList<TrackedIssue>> FetchByIdsAsync(
        IssueTrackerConfig config, IReadOnlyList<string> externalIds, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<TrackedIssue>>([]);

    public Task<IReadOnlyList<TrackedIssueComment>> FetchCommentsSinceAsync(
        IssueTrackerConfig config, DateTime? since, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<TrackedIssueComment>>([]);

    public Task<TrackedIssueComment> PostCommentAsync(
        IssueTrackerConfig config, string externalId, string body, CancellationToken ct)
    {
        PostCommentCalls.Add((externalId, body));
        return Task.FromResult(new TrackedIssueComment(
            "1", externalId, "bot", body, "https://example.test", DateTime.UtcNow, DateTime.UtcNow));
    }

    public Task AddLabelsAsync(
        IssueTrackerConfig config, string externalId, IReadOnlyList<string> labels, CancellationToken ct) =>
        Task.CompletedTask;

    public Task RemoveLabelAsync(
        IssueTrackerConfig config, string externalId, string label, CancellationToken ct) =>
        Task.CompletedTask;

    public Task ReplaceLabelsAsync(
        IssueTrackerConfig config, string externalId, IReadOnlyList<string> labels, CancellationToken ct) =>
        Task.CompletedTask;

    public Task SetStateAsync(
        IssueTrackerConfig config, string externalId, string state, string? stateReason, CancellationToken ct) =>
        Task.CompletedTask;

    public Task<TrackedIssue> CreateIssueAsync(
        IssueTrackerConfig config, string title, string body, IReadOnlyList<string> labels, CancellationToken ct) =>
        Task.FromResult(new TrackedIssue(
            "acme/app#1", "#1", title, body, "open", 0, labels, [],
            "https://github.test/acme/app/issues/1", "{}"));

    public Task UpdateIssueContentAsync(
        IssueTrackerConfig config, string externalId, string title, string body, CancellationToken ct) =>
        Task.CompletedTask;
}
