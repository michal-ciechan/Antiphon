using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Enums;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-1744 W-4 and W-5. An unpinned Windows Worktree Code task falls through a Linux
/// desktop to desktop-win, and the fallback scan must skip a runner that does not allow
/// delegated tasks.
/// </summary>
[Category("Integration")]
public sealed class C1744G2PlacementTests
{
    [Test]
    public async Task Windows_task_selects_desktop_win_when_local_is_linux()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var kit = MatrixKit(schema.ConnectionString, desktopWinDelegated: true);
        await using var db = kit.Context();
        var created = await kit.Service(db).CreateAsync(WindowsWorktree(), kit.Caller, CancellationToken.None);
        var saved = await kit.ReadAsync(created.Id);
        saved.Task.RunnerId.ShouldBe("desktop-win");
        saved.Task.RunnerSelectionSource.ShouldBe(RunnerSelectionSource.Fallback);
        saved.Task.PlacementReason.ShouldBe("platform_match");
    }

    [Test]
    public async Task Fallback_scan_skips_runner_without_delegated_tasks()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var kit = MatrixKit(schema.ConnectionString, desktopWinDelegated: false);
        var before = await kit.TaskCountAsync();
        await using var db = kit.Context();
        var refused = await Should.ThrowAsync<ConflictException>(() =>
            kit.Service(db).CreateAsync(WindowsWorktree(), kit.Caller, CancellationToken.None));
        refused.Code.ShouldBe(RunnerPlatformProblems.Unavailable);
        (await kit.TaskCountAsync()).ShouldBe(before);
    }

    private static CreateAgentTaskRequest WindowsWorktree() => new(
        "c1744 windows worktree",
        Role: AgentTaskRole.Code,
        AgentKind: AgentKind.ClaudeCode,
        Workspace: WorkspaceMode.Worktree,
        RequiredPlatform: RequiredPlatform.Windows);

    private static DefaultRunnerKit MatrixKit(string connectionString, bool desktopWinDelegated)
    {
        var kit = DefaultRunnerKit.Create(connectionString, "server2");
        kit.PhoneHome.Enabled = true;
        kit.PhoneHome.Runners["desktop-win"] = Entry(kit, desktopWinDelegated);
        kit.PhoneHome.Runners["server2"] = Entry(kit, delegated: true);
        kit.RealDirectory = Matrix();
        return kit;
    }

    private static PhoneHomeRunnerEntry Entry(DefaultRunnerKit kit, bool delegated) => new()
    {
        Enabled = true,
        AllowDelegatedTasks = delegated,
        HostWorkspaceRoot = kit.RepoRoot,
        CallbackOrigin = "https://antiphon.test",
        SharedSecret = "x",
        ChildGrokHome = "/state/grok",
    };

    private static MatrixDirectory Matrix()
    {
        var directory = new MatrixDirectory();
        directory.Rows["desktop"] = Describe("desktop", "linux");
        directory.Rows["desktop-win"] = Describe("desktop-win", "windows");
        directory.Rows["server2"] = Describe("server2", "linux");
        return directory;
    }

    private static RunnerDescriptor Describe(string id, string platform) => new(
        id, id, platform, DateTimeOffset.UtcNow, true, true, false, 4,
        new RunnerCapabilitiesDto("InboxConhost", "inbox", "test", false,
            Features: [RunnerPlatformWire.Feature], Platform: platform));

    private sealed class MatrixDirectory : ISessionRunnerDirectory
    {
        public Dictionary<string, RunnerDescriptor> Rows { get; } = new(StringComparer.Ordinal);
        public ISessionRunnerClient Local { get; } = new StubClient();
        public IReadOnlyList<string> KnownRunnerIds => Rows.Keys.ToList();
        public Guid? GetLiveStoreId(string? runnerId) =>
            string.IsNullOrWhiteSpace(runnerId) ? null : Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

        public Task<RunnerDescriptor?> DescribeAsync(string? runnerId, CancellationToken ct)
        {
            var key = string.IsNullOrWhiteSpace(runnerId) || RunnerRequestIntent.IsDesktopAlias(runnerId)
                ? "desktop" : runnerId.Trim();
            return Task.FromResult(Rows.TryGetValue(key, out var row) ? row : null);
        }

        public ISessionRunnerClient Resolve(string? runnerId)
        {
            if (runnerId is null || !Rows.TryGetValue(runnerId, out var row) || !row.DispatchEligible)
                throw new ServiceUnavailableException("Phone-home runner is unavailable.", PhoneHomeProblemTypes.Unavailable);
            return Local;
        }

        public Task<SessionRunnerOwner?> GetOwnerAsync(Guid sessionId, CancellationToken ct) =>
            Task.FromResult<SessionRunnerOwner?>(null);
        public Task<SessionRunnerBinding> GetBindingAsync(Guid sessionId, CancellationToken ct) =>
            Task.FromResult<SessionRunnerBinding>(SessionRunnerBinding.Missing.Instance);
        public Task<RunnerInventory> GetInventoryAsync(string? runnerId, CancellationToken ct) =>
            throw new InvalidOperationException("placement must not list inventory");

        private sealed class StubClient : ISessionRunnerClient
        {
            public Task<SessionRunnerSessionDto> StartAsync(Guid sessionId, AgentLaunchSpec spec, CancellationToken ct) =>
                throw new NotSupportedException();
            public Task<IReadOnlyList<SessionRunnerSessionDto>> ListAsync(CancellationToken ct) =>
                throw new NotSupportedException();
            public Task<SessionRunnerSessionDto> GetAsync(Guid sessionId, CancellationToken ct) =>
                throw new NotSupportedException();
            public Task<SessionRunnerBufferDto> GetBufferAsync(Guid sessionId, CancellationToken ct) =>
                throw new NotSupportedException();
            public Task<SessionRunnerSnapshotDto> GetSnapshotAsync(Guid sessionId, CancellationToken ct) =>
                throw new NotSupportedException();
            public Task<SessionRunnerTranscriptDto> GetTranscriptAsync(Guid sessionId, CancellationToken ct) =>
                throw new NotSupportedException();
            public Task SendInputAsync(Guid sessionId, string input, CancellationToken ct) => Task.CompletedTask;
            public Task ClearLiveBufferAsync(Guid sessionId, CancellationToken ct) => Task.CompletedTask;
            public Task ResizeAsync(Guid sessionId, int cols, int rows, CancellationToken ct) => Task.CompletedTask;
            public Task<SessionRunnerSessionDto> KillAsync(Guid sessionId, CancellationToken ct) =>
                throw new NotSupportedException();
            public IAsyncEnumerable<SessionRunnerEvent> StreamEventsAsync(CancellationToken ct) => Empty();
            private static async IAsyncEnumerable<SessionRunnerEvent> Empty()
            {
                await Task.CompletedTask;
                yield break;
            }
        }
    }
}
