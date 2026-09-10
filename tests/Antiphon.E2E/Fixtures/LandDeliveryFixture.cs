using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using Antiphon.Agents.Pty;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;

namespace Antiphon.E2E.Fixtures;

/// <summary>Real Program/runner/queue/native FakeGrok. The parent owns resources across child server death.</summary>
public sealed class LandDeliveryFixture : IAsyncDisposable
{
    public string Root { get; } = Path.Combine(AntiphonAppFixture.FindRepositoryRoot(), ".antiphon", "acceptance", "card-0467", Guid.NewGuid().ToString("N"));
    private readonly string _suffix = "";
    private readonly bool _shared;
    public LandDeliveryFixture() { }
    internal LandDeliveryFixture(LandDeliveryFixture owner)
    { Root = owner.Root; _app = owner._app; _suffix = "-second"; _shared = true; }
    public string Repository => Path.Combine(Root, "repo" + _suffix);
    public string Source => Path.Combine(Root, "trees", "source" + _suffix);
    public string Remote => Path.Combine(Root, "remote" + _suffix + ".git");
    private string CallerDirectory => Path.Combine(Root, "caller" + _suffix);
    public Guid TaskId { get; } = Guid.NewGuid();
    public Guid CallerId { get; private set; }
    public string SourceSha { get; private set; } = "";
    private string _token = "";
    private AntiphonAppFixture _app = null!;
    private HttpClient _http = null!;
    private Process? _child;
    private Task<string>? _childOut, _childError;
    private string _connection = "";
    private string _address = "";
    private bool _hostSuspended;

    public async Task InitializeAsync(bool busy = false, string cut = "none")
    {
        OperatingSystem.IsWindows().ShouldBeTrue();
        ConPtyRedistributable.TryLocate(out _, out var why).ShouldBeTrue(why);
        File.Exists(Path.Combine(AppContext.BaseDirectory, "fakegrok", "fakegrok.exe")).ShouldBeTrue("native FakeGrok must be staged");
        Directory.CreateDirectory(Repository);
        Directory.CreateDirectory(CallerDirectory);
        await GitAsync(Repository, "init", "-b", "master");
        await GitAsync(Repository, "config", "core.longpaths", "true");
        await GitAsync(Root, "init", "--bare", Remote);
        await GitAsync(Remote, "config", "core.longpaths", "true");
        await File.WriteAllTextAsync(Path.Combine(Repository, ".gitignore"), ".antiphon/\nbin/\nobj/\n");
        await File.WriteAllTextAsync(Path.Combine(Repository, "Owned.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net9.0</TargetFramework></PropertyGroup></Project>");
        await File.WriteAllTextAsync(Path.Combine(Repository, "Seed.cs"), "public class Seed {}\n");
        await GitAsync(Repository, "add", ".");
        await GitAsync(Repository, "commit", "-m", "owned seed");
        await GitAsync(Repository, "remote", "add", "origin", Remote);
        await GitAsync(Repository, "push", "-u", "origin", "master");
        await GitAsync(Repository, "worktree", "add", "-b", "c467-source", Source);
        await File.WriteAllTextAsync(Path.Combine(Source, "Feature.cs"), "public class Feature {}\n");
        await GitAsync(Source, "add", ".");
        await GitAsync(Source, "commit", "-m", "owned feature");
        SourceSha = (await GitAsync(Source, "rev-parse", "HEAD")).Trim();
        if (!_shared)
        {
            _app = new AntiphonAppFixture { LandDelivery = new(Root, cut), UsePrebuiltFrontend = true, DiagnosticsDirectory = Path.Combine(Root, "server-logs") };
            await _app.InitializeAsync();
        }
        _app.EnsureSessionRunnerReachable();
        new Uri(_app.OwnedRunnerUrl).Port.ShouldNotBe(17204);
        _connection = _app.OwnedDatabase;
        _address = _app.BaseAddress;
        _http = new HttpClient { BaseAddress = new Uri(_address) };
        var settings = _app.Services.GetRequiredService<IOptions<DelegationSettings>>().Value;
        settings.ApiBaseUrl = _address;
        settings.AllowedRoots = [Root];
        var verification = _app.Services.GetRequiredService<IOptions<SupervisionSettings>>().Value.DeliveryVerification;
        verification.Enabled.ShouldBeTrue(); verification.TranscriptConfirmEnabled.ShouldBeTrue();
        using (var scope = _app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.AgentTuiProfiles.ExecuteUpdateAsync(s => s.SetProperty(p => p.IsDefault, false));
            var agent = new Agent { Id = Guid.NewGuid(), Name = "C467 owned caller", Slug = "c467-" + Guid.NewGuid().ToString("N"),
                Kind = AgentKind.Grok, WorkingDirectory = CallerDirectory, AlwaysOn = false, AutoCompactEnabled = false,
                CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
            db.Agents.Add(agent);
            await db.SaveChangesAsync();
            var started = await scope.ServiceProvider.GetRequiredService<AgentControlService>().StartAsync(agent.Id,
                new(Prompt: busy ? "[c467-busy] hold this owned turn" : "C467 caller ready", IgnoreSubscriptionQuota: true), CancellationToken.None);
            CallerId = Guid.Parse(started.PersistentSessionId!);
        }
        await UntilAsync(async () => {
            await using var db = CreateContext();
            if (!await db.AgentSessions.AnyAsync(s => s.Id == CallerId && s.InteractiveLaunchCompletedAt != null)) return false;
            return busy ? await db.TranscriptEntries.AnyAsync(t => t.AgentSessionId == CallerId && t.Kind == TranscriptKinds.UserPrompt)
                && File.Exists(Path.Combine(Root, "caller-busy.held"))
                : await db.TranscriptEntries.AnyAsync(t => t.AgentSessionId == CallerId && t.Kind == TranscriptKinds.TurnEnd)
                    && !await SessionMessageQueueService.IsWorkingAsync(db, CallerId, CancellationToken.None);
        }, "native caller ready", 120);
        await using (var db = CreateContext())
        {
            var (token, hash) = AgentTaskService.NewToken(); _token = token;
            var session = await db.AgentSessions.SingleAsync(s => s.Id == CallerId);
            session.DelegationTokenHash = hash;
            db.AgentTasks.Add(new AgentTask { Id = TaskId, RootTaskId = TaskId, Title = "C467 real Land delivery", Goal = "owned fixture",
                Role = AgentTaskRole.Code, Kind = AgentTaskKind.Worker, Workspace = WorkspaceMode.Worktree, Status = AgentTaskStatus.Succeeded,
                WorkingDirectory = Source, RepoPath = Repository, WorktreePath = Source, WorktreeBranch = "c467-source",
                ReplyTo = AgentTaskReplyTo.Session, ParentSessionId = CallerId, CreatedAt = DateTime.UtcNow, CompletedAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }
        await File.WriteAllTextAsync(Path.Combine(Root, "identities" + _suffix + ".json"), JsonSerializer.Serialize(new {
            taskId = TaskId, callerId = CallerId, sourceSha = SourceSha, serverMvid = typeof(Program).Assembly.ManifestModule.ModuleVersionId,
            runner = _app.OwnedRunnerUrl, runnerDirectory = _app.OwnedRunnerDirectory, server = _address,
            fakeHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "fakegrok", "fakegrok.dll")))) }));
    }

    public AppDbContext CreateContext() => new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(_connection).Options);

    public async Task<Guid> RequestAsync(bool initial = true)
    {
        var start = new ProcessStartInfo("pwsh") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in new[] { "-NoProfile", "-File", Path.Combine(AntiphonAppFixture.FindRepositoryRoot(), "scripts", "delegate.ps1"), "-Land", TaskId.ToString() }) start.ArgumentList.Add(arg);
        start.Environment["ANTIPHON_API"] = _address; start.Environment["ANTIPHON_TASK_TOKEN"] = _token;
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(60));
        process.ExitCode.ShouldBe(0, await stderr);
        var output = await stdout;
        await File.WriteAllTextAsync(Path.Combine(Root, "script-acceptance.txt"), output);
        output.ShouldContain("Publication pending");
        await using var db = CreateContext();
        var request = await db.AgentTaskLandRequests.OrderByDescending(r => r.RequestedAt).FirstAsync(r => r.TaskId == TaskId);
        output.ShouldContain(request.Id.ToString());
        using var acceptance = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(Root, "http-202.json")));
        acceptance.RootElement.GetProperty("requestId").GetGuid().ShouldBe(request.Id);
        if (initial) (await db.AgentTaskLandings.CountAsync(o => o.TaskId == TaskId)).ShouldBe(0, "execution gate must hold until acceptance is observed");
        return request.Id;
    }
    public Task ReleaseExecutionAsync() => File.WriteAllTextAsync(Path.Combine(Root, "execute.release"), "release");
    public Task ReleaseBusyAsync() => File.WriteAllTextAsync(Path.Combine(Root, "caller-busy.release"), "release");
    public Task AdvanceLandClockAsync(int seconds) => File.WriteAllTextAsync(Path.Combine(Root, "land-clock-seconds.txt"), seconds.ToString());
    public Task ReleaseBoundaryAsync(string boundary) => File.WriteAllTextAsync(Path.Combine(Root, boundary + ".release"), "release");

    public async Task<string> StatusAsync()
    {
        var start = new ProcessStartInfo("pwsh") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in new[] { "-NoProfile", "-File", Path.Combine(AntiphonAppFixture.FindRepositoryRoot(), "scripts", "delegate.ps1"), "-Status", TaskId.ToString() }) start.ArgumentList.Add(arg);
        start.Environment["ANTIPHON_API"] = _address; start.Environment["ANTIPHON_TASK_TOKEN"] = _token;
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(60));
        process.ExitCode.ShouldBe(0, await error);
        var text = await output;
        await File.AppendAllTextAsync(Path.Combine(Root, "status-polls.txt"), text + "\n");
        return text;
    }

    public async Task<AgentTaskLandNotification> ReceiptAsync(LandNotificationKind kind = LandNotificationKind.Outcome, Guid? requestId = null)
    {
        AgentTaskLandNotification? result = null;
        await UntilAsync(async () => {
            await using var db = CreateContext();
            result = await db.AgentTaskLandNotifications.AsNoTracking().FirstOrDefaultAsync(n => n.TaskId == TaskId && n.Kind == kind && n.ConfirmedAt != null && (requestId == null || n.RequestId == requestId));
            return result is not null;
        }, "complete native Land receipt");
        await using var observer = CreateContext();
        var row = await observer.SessionQueuedMessages.SingleAsync(m => m.Id == result!.QueueMessageId);
        var prompt = await observer.TranscriptEntries.SingleAsync(p => p.AgentSessionId == CallerId && p.Sequence == result!.ConfirmingPromptSequence);
        prompt.Kind.ShouldBe(TranscriptKinds.UserPrompt);
        PromptSubmissionMatch.IsConfirmedBy(row.Body, prompt.Text!).ShouldBeTrue();
        PromptSubmissionMatch.IsCompleteIn(row.Body, prompt.Text!).ShouldBeTrue();
        if (row.LastDeliveryBaselineSequence is long floor) prompt.Sequence.ShouldBeGreaterThan(floor);
        else (prompt.Timestamp >= row.LastDeliveryStartedAt).ShouldBeTrue();
        await SnapshotAsync();
        return result!;
    }

    public async Task AssertRemoteAsync()
    {
        await GitAsync(Remote, "merge-base", "--is-ancestor", SourceSha, "refs/heads/master");
        await File.WriteAllTextAsync(Path.Combine(Root, "remote-containment.txt"), $"{SourceSha} refs/heads/master exit=0");
    }
    public async Task AssertOnePromptAsync(AgentTaskLandNotification note)
    {
        await Task.Delay(TimeSpan.FromSeconds(11));
        await using var db = CreateContext();
        var prompts = await db.TranscriptEntries.Where(p => p.AgentSessionId == CallerId && p.Kind == TranscriptKinds.UserPrompt && p.Text != null).ToListAsync();
        prompts.Count(p => p.Text!.Contains("[land " + note.Id.ToString("N"))).ShouldBe(1);
        var native = Directory.GetFiles(Path.Combine(Root, "native"), "updates.jsonl", SearchOption.AllDirectories)
            .SelectMany(File.ReadAllLines).Count(line => line.Contains("user_message_chunk") && line.Contains("[land " + note.Id.ToString("N")));
        native.ShouldBe(1);
    }

    public async Task UseChildAsync(string cut)
    {
        if (!_hostSuspended) { await _app.SuspendLandHostAsync(); _hostSuspended = true; }
        if (_child is not null) await KillChildAsync();
        var ready = Path.Combine(Root, "child-" + Guid.NewGuid().ToString("N") + ".json");
        // Use this assembly's .NET/ASP.NET runtime graph. PowerShell's runtime cannot load the real Kestrel host.
        var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in new[] { typeof(LandDeliveryFixture).Assembly.Location, "--treenode-filter", "/*/*/AgentTaskLandDeliveryE2ETests/C467_V22*" }) start.ArgumentList.Add(arg);
        start.Environment["ANTIPHON_C467_CONNECTION"] = _connection;
        start.Environment["ANTIPHON_C467_CHILD"] = JsonSerializer.Serialize(new[] { Root, _app.OwnedRunnerUrl, cut, ready });
        _child = Process.Start(start)!;
        _childOut = _child.StandardOutput.ReadToEndAsync(); _childError = _child.StandardError.ReadToEndAsync();
        await UntilAsync(() => Task.FromResult(File.Exists(ready) || _child.HasExited), "owned child startup", 120);
        _child.HasExited.ShouldBeFalse(_child.HasExited ? await _childError : "");
        using var identity = JsonDocument.Parse(await File.ReadAllTextAsync(ready));
        identity.RootElement.GetProperty("pid").GetInt32().ShouldBe(_child.Id);
        _address = identity.RootElement.GetProperty("address").GetString()!;
        _http.Dispose(); _http = new HttpClient { BaseAddress = new Uri(_address) };
    }

    public static async Task RunChildAsync(string root, string runner, string cut, string ready)
    {
        var connection = Environment.GetEnvironmentVariable("ANTIPHON_C467_CONNECTION") ?? throw new InvalidOperationException("Missing owned database");
        var factory = new AntiphonAppFixture.KestrelWebApplicationFactory(null, connection, false, root,
            Path.Combine(root, "child-logs"), runner, land: new(root, cut));
        try { factory.CreateClient(); } catch when (factory.KestrelHost is not null) { }
        var host = factory.KestrelHost ?? throw new InvalidOperationException("No real child host");
        var address = host.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        var settings = host.Services.GetRequiredService<IOptions<DelegationSettings>>().Value;
        settings.ApiBaseUrl = address; settings.AllowedRoots = [root];
        await File.WriteAllTextAsync(ready, JsonSerializer.Serialize(new { address, pid = Environment.ProcessId, mvid = typeof(Program).Assembly.ManifestModule.ModuleVersionId }));
        await Task.Delay(Timeout.Infinite);
    }

    public async Task KillChildAsync()
    {
        if (_child is null) return;
        if (!_child.HasExited) _child.Kill(entireProcessTree: false);
        await _child.WaitForExitAsync();
        await File.WriteAllTextAsync(Path.Combine(Root, "child-" + _child.Id + ".stdout.log"), await _childOut!);
        await File.WriteAllTextAsync(Path.Combine(Root, "child-" + _child.Id + ".stderr.log"), await _childError!);
        _child.Dispose(); _child = null;
    }

    public async Task SnapshotAsync()
    {
        await using var db = CreateContext();
        await File.WriteAllTextAsync(Path.Combine(Root, "delivery-evidence.json"), JsonSerializer.Serialize(new {
            requests = await db.AgentTaskLandRequests.AsNoTracking().Where(r => r.TaskId == TaskId).ToListAsync(),
            operations = await db.AgentTaskLandings.AsNoTracking().Where(o => o.TaskId == TaskId).ToListAsync(),
            events = await db.AgentTaskEvents.AsNoTracking().Where(e => e.AgentTaskId == TaskId).Select(e => new { e.Id, e.Type, e.LandRequestId, e.LandingOperationId, e.At }).ToListAsync(),
            notifications = await db.AgentTaskLandNotifications.AsNoTracking().Where(n => n.TaskId == TaskId).ToListAsync(),
            queue = await db.SessionQueuedMessages.AsNoTracking().Where(m => m.SourceTaskId == TaskId).ToListAsync(),
            prompts = await db.TranscriptEntries.AsNoTracking().Where(p => p.AgentSessionId == CallerId && p.Kind == TranscriptKinds.UserPrompt).ToListAsync() }));
    }
    public async Task ArrangeOutcomeAsync(string outcome)
    {
        if (outcome == "already-present") await GitAsync(Source, "push", "origin", "HEAD:refs/heads/master");
        if (outcome == "residue-cleanup")
        {
            Directory.CreateDirectory(Path.Combine(Source, ".antiphon"));
            await File.WriteAllTextAsync(Path.Combine(Source, ".antiphon", "valuable.txt"), "owned user residue");
        }
        if (outcome == "preoperation-refusal")
        {
            await using var db = CreateContext();
            await db.AgentTasks.Where(t => t.Id == TaskId).ExecuteUpdateAsync(s => s.SetProperty(t => t.WorktreeBranch, (string?)null));
        }
        if (outcome == "operation-refusal")
            await File.WriteAllTextAsync(Path.Combine(Remote, "hooks", "pre-receive"), "#!/bin/sh\nexit 1\n");
        if (outcome == "conflict")
        {
            await File.WriteAllTextAsync(Path.Combine(Source, "Seed.cs"), "public class Seed { public int Source; }\n");
            await GitAsync(Source, "add", "."); await GitAsync(Source, "commit", "-m", "source conflict");
            SourceSha = (await GitAsync(Source, "rev-parse", "HEAD")).Trim();
            await File.WriteAllTextAsync(Path.Combine(Repository, "Seed.cs"), "public class Seed { public int Target; }\n");
            await GitAsync(Repository, "add", "."); await GitAsync(Repository, "commit", "-m", "target conflict");
            await GitAsync(Repository, "push", "origin", "master");
        }
    }
    public static async Task UntilAsync(Func<Task<bool>> predicate, string evidence, int seconds = 60)
    {
        var deadline = DateTime.UtcNow.AddSeconds(seconds);
        while (DateTime.UtcNow < deadline) { if (await predicate()) return; await Task.Delay(100); }
        throw new TimeoutException(evidence);
    }
    private async Task<string> GitAsync(string cwd, params string[] args)
    {
        var start = new ProcessStartInfo("git") { WorkingDirectory = cwd, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in new[] { "-c", "user.name=C467", "-c", "user.email=c467@example.invalid" }.Concat(args)) start.ArgumentList.Add(arg);
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        await File.AppendAllTextAsync(Path.Combine(Root, "git-trace.txt"), $"{cwd}: {string.Join(' ', args)} exit={process.ExitCode}\n");
        process.ExitCode.ShouldBe(0, await error);
        return await output;
    }
    public async ValueTask DisposeAsync()
    {
        await KillChildAsync();
        if (_app is not null && !_shared) await _app.DisposeAsync();
        _http?.Dispose();
        // Evidence, owned repository and native input records are intentionally retained.
    }
}
