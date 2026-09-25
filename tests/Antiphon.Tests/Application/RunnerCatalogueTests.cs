using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0710 V-9. GET /api/session-runners lists the desktop and every configured runner,
/// including one that is offline, and never borrows another runner's status.
/// </summary>
[Category("Integration")]
public sealed class RunnerCatalogueTests
{
    [Test]
    public async Task Catalogue_includes_desktop_and_offline_runners()
    {
        var secretA = "secret-a-" + Guid.NewGuid().ToString("N");
        await using var host = await PhoneHomeTestHost.StartAsync(configured: Settings(secretA));
        host.Local.Capabilities = new RunnerCapabilitiesDto(
            "InboxConhost", "inbox", "test", false,
            Features: [RunnerPlatformWire.Feature], Platform: "windows");

        using var response = await host.Http.GetAsync("/api/session-runners");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var ids = doc.RootElement.EnumerateArray().Select(row => row.GetProperty("runnerId").GetString()).ToArray();
        ids.ShouldContain("desktop");
        ids.ShouldContain("runner-a");
        ids.ShouldContain("runner-b");
        var offline = doc.RootElement.EnumerateArray().Single(row => row.GetProperty("runnerId").GetString() == "runner-b");
        offline.GetProperty("available").GetBoolean().ShouldBeFalse();
        offline.GetProperty("dispatchEligible").GetBoolean().ShouldBeFalse();
        var desktop = doc.RootElement.EnumerateArray().Single(row => row.GetProperty("runnerId").GetString() == "desktop");
        desktop.GetProperty("platform").GetString().ShouldBe("windows");
        desktop.GetProperty("capacityKind").GetString().ShouldBe("delegatedTasks");
    }

    [Test]
    public async Task Unknown_id_has_no_borrowed_status()
    {
        await using var host = await PhoneHomeTestHost.StartAsync();
        await using var peer = await host.ConnectPeerAsync();
        host.Directory.MarkRecovered(await host.WaitLiveAsync());

        using var missing = await host.Http.GetAsync($"/api/session-runners/{host.AllowedRunnerId}/../other/status");
        using var status = await host.Http.GetAsync("/api/session-runners/other-runner/status");
        status.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var own = await host.Http.GetFromJsonAsync<PhoneHomeRunnerStatusDto>(
            $"/api/session-runners/{host.AllowedRunnerId}/status", PhoneHomeFraming.Json);
        own.ShouldNotBeNull();
        own.RunnerStoreId.ShouldBe(host.StoreId);
        missing.StatusCode.ShouldNotBe(HttpStatusCode.OK);
        peer.Epoch.ShouldBeGreaterThan(0);
    }

    [Test]
    public async Task Capacity_matches_dispatch_accounting()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var secretA = "secret-a-" + Guid.NewGuid().ToString("N");
        await using var host = await PhoneHomeTestHost.StartAsync(
            connectionString: schema.ConnectionString, configured: Settings(secretA));
        host.Capacity = 3;
        await using var peer = await host.ConnectPeerAsync(runnerId: "runner-a", secret: secretA, platform: "linux",
            capabilities: new RunnerCapabilitiesDto("InboxConhost", "inbox", "test", false,
                Features: [RunnerPlatformWire.Feature], Platform: "linux"));
        host.Directory.MarkRecovered(await host.WaitLiveAsync(runnerId: "runner-a"));
        var now = DateTime.UtcNow;
        await using (var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString)))
        {
            db.AgentSessions.Add(new AgentSession
            {
                Id = Guid.NewGuid(),
                DefinitionName = "grok",
                AgentKind = AgentKind.Grok,
                Status = SessionStatus.Running,
                Cwd = "/work",
                Cols = 80,
                Rows = 24,
                CreatedAt = now,
                StartedAt = now,
                LastSeenAt = now,
                RunnerId = "runner-a",
            });
            db.AgentTasks.Add(TaskRow(now, "runner-a", AgentTaskStatus.Queued, AgentTaskRole.Code, remotePrep: true));
            db.AgentTasks.Add(TaskRow(now, null, AgentTaskStatus.Working, AgentTaskRole.Code, remotePrep: false));
            db.AgentTasks.Add(TaskRow(now, null, AgentTaskStatus.Working, AgentTaskRole.Check, remotePrep: false));
            await db.SaveChangesAsync();
        }

        using var response = await host.Http.GetAsync("/api/session-runners");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var remote = doc.RootElement.EnumerateArray().Single(row => row.GetProperty("runnerId").GetString() == "runner-a");
        remote.GetProperty("capacity").GetInt32().ShouldBe(3);
        remote.GetProperty("occupied").GetInt32().ShouldBe(2, "one live session plus one prepared queued task, once each");
        remote.GetProperty("capacityKind").GetString().ShouldBe("sessions");
        var desktop = doc.RootElement.EnumerateArray().Single(row => row.GetProperty("runnerId").GetString() == "desktop");
        desktop.GetProperty("occupied").GetInt32().ShouldBe(1, "the specialist check is outside the delegated-task cap");
        desktop.GetProperty("capacityKind").GetString().ShouldBe("delegatedTasks");
        var offline = doc.RootElement.EnumerateArray().Single(row => row.GetProperty("runnerId").GetString() == "runner-b");
        offline.GetProperty("occupied").ValueKind.ShouldBe(JsonValueKind.Null);
        peer.Epoch.ShouldBeGreaterThan(0);
    }

    [Test]
    public async Task Task_dtos_keep_historical_placement()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var kit = DefaultRunnerKit.Create(schema.ConnectionString, defaultRunnerId: null, allowedRunnerId: "server2");
        var directory = new FlippingDirectory("linux");
        kit.RealDirectory = directory;
        await using var db = kit.Context();
        var created = await kit.Service(db).CreateAsync(
            new CreateAgentTaskRequest(
                "c710 frozen observation",
                Role: AgentTaskRole.Code,
                AgentKind: AgentKind.Grok,
                Workspace: WorkspaceMode.Worktree,
                RunnerId: "server2",
                RequiredPlatform: RequiredPlatform.Linux),
            kit.Caller, CancellationToken.None);
        directory.Platform = "windows";
        await using var readDb = kit.Context();
        var detail = await kit.Service(readDb).GetAsync(created.Id, CancellationToken.None);
        var json = JsonSerializer.Serialize(detail.Summary, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        using var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("requiredPlatform").GetString().ShouldBe("Linux");
        doc.RootElement.GetProperty("runnerId").GetString().ShouldBe("server2");
        doc.RootElement.GetProperty("observedPlatform").GetString().ShouldBe("linux");
        detail.Summary.Status.ShouldBe(AgentTaskStatus.Queued);
    }

    private static AgentTask TaskRow(DateTime now, string? runnerId, AgentTaskStatus status, AgentTaskRole role, bool remotePrep)
    {
        var id = Guid.NewGuid();
        return new AgentTask
        {
            Id = id,
            RootTaskId = id,
            Title = "catalogue",
            Goal = "catalogue",
            Kind = AgentTaskKind.Worker,
            Role = role,
            AgentKind = AgentKind.Grok,
            ModelLevel = AgentModelLevel.Medium,
            Workspace = WorkspaceMode.Worktree,
            WorkingDirectory = "/work",
            RunnerId = runnerId,
            Status = status,
            RemoteWorktreePath = remotePrep ? "/work/worktrees/task-remote" : null,
            ReplyTo = AgentTaskReplyTo.None,
            CreatedAt = now,
            ConcurrencyToken = Guid.NewGuid(),
        };
    }

    private sealed class FlippingDirectory : ISessionRunnerDirectory
    {
        public FlippingDirectory(string platform) => Platform = platform;
        public string Platform { get; set; }
        public ISessionRunnerClient Local { get; } = new PhoneHomeTestHost.RecordingLocalClient();
        public IReadOnlyList<string> KnownRunnerIds => ["server2"];
        public Guid? GetLiveStoreId(string? runnerId) => Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        public Task<RunnerDescriptor?> DescribeAsync(string? runnerId, CancellationToken ct) =>
            Task.FromResult<RunnerDescriptor?>(new RunnerDescriptor(
                "server2", "server2", Platform, DateTimeOffset.UtcNow, true, true, false, 4,
                new RunnerCapabilitiesDto("InboxConhost", "inbox", "test", false,
                    Features: [RunnerPlatformWire.Feature], Platform: Platform)));
        public ISessionRunnerClient Resolve(string? runnerId) => Local;
        public Task<SessionRunnerOwner?> GetOwnerAsync(Guid sessionId, CancellationToken ct) =>
            Task.FromResult<SessionRunnerOwner?>(null);
        public Task<SessionRunnerBinding> GetBindingAsync(Guid sessionId, CancellationToken ct) =>
            Task.FromResult<SessionRunnerBinding>(SessionRunnerBinding.Missing.Instance);
        public Task<RunnerInventory> GetInventoryAsync(string? runnerId, CancellationToken ct) =>
            Task.FromResult<RunnerInventory>(new RunnerInventory.Unavailable("not asked"));
    }

    private static PhoneHomeRunnerSettings Settings(string secretA) => new()
    {
        Enabled = true,
        Runners = new Dictionary<string, PhoneHomeRunnerEntry>(StringComparer.OrdinalIgnoreCase)
        {
            ["runner-a"] = new()
            {
                Enabled = true,
                DisplayName = "Runner A",
                AllowDelegatedTasks = true,
                HostWorkspaceRoot = @"C:\work",
                RunnerWorkspace = "/work/a",
                RunnerRepository = "/work/repos/antiphon",
                CallbackOrigin = "https://antiphon.test",
                SharedSecret = secretA,
                MaxCapacity = 3,
            },
            ["runner-b"] = new()
            {
                Enabled = true,
                DisplayName = "Runner B",
                AllowDelegatedTasks = true,
                HostWorkspaceRoot = @"C:\work",
                RunnerWorkspace = "/work/b",
                RunnerRepository = "/work/repos/antiphon",
                CallbackOrigin = "https://antiphon.test",
                SharedSecret = "secret-b",
                MaxCapacity = 3,
            },
        },
    };
}
