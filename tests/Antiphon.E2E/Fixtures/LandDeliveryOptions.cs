using System.Diagnostics;
using System.Text.Json;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Infrastructure.Git;

namespace Antiphon.E2E.Fixtures;

internal sealed record LandDeliveryOptions(string Root, string Cut = "none")
{
    public string Gate => Path.Combine(Root, "caller-busy");
    public void Configure(Dictionary<string, string?> settings)
    {
        settings["Agents:DefaultDefinition"] = "c467-grok";
        // This definition always runs the staged native FakeGrok, which has no credential store.
        settings["Agents:GrokCredentialProbeEnabled"] = "false";
        settings["Agents:Definitions:c467-grok:Kind"] = "Grok";
        settings["Agents:Definitions:c467-grok:Exe"] = Path.Combine(Path.GetDirectoryName(typeof(LandDeliveryOptions).Assembly.Location)!, "fakegrok", "fakegrok.exe");
        settings["Agents:Definitions:c467-grok:Env:GROK_HOME"] = Path.Combine(Root, "native");
        settings["Agents:Definitions:c467-grok:Env:ANTIPHON_FAKE_BUSY_GATE"] = Gate;
        settings["Agents:Definitions:c467-grok:NonSecretEnvironmentNames:0"] = "GROK_HOME";
        settings["Agents:Definitions:c467-grok:NonSecretEnvironmentNames:1"] = "ANTIPHON_FAKE_BUSY_GATE";
        settings["Git:WorkspacePath"] = Path.Combine(Root, "repo");
        settings["Git:WorktreeBasePath"] = Path.Combine(Root, "trees");
        settings["GitHub:Enabled"] = "false";
        // Delivery/dispatch use hosted services, not Hangfire's machine-wide maintenance jobs.
        settings["Hangfire:ServerEnabled"] = "false";
        settings["ChannelBridge:Enabled"] = "false";
        settings["AntiphonMessaging:BootstrapServers"] = "127.0.0.1:1";
        settings["Delegation:CheckInterpreterEnabled"] = "false";
        settings["Delegation:DiagnoseEnabled"] = "false";
        settings["Delegation:OutputDistillerEnabled"] = "false";
        settings["Delegation:MaxTasksPerRoot"] = "1"; // Preserve the real conflict cap branch; never launch a model helper.
        settings["Supervision:DeliveryVerification:Enabled"] = "true";
        settings["Supervision:DeliveryVerification:TranscriptConfirmEnabled"] = "true";
    }
    public void ConfigureServices(IServiceCollection services)
    {
        // Allowed roots are also scanned as repositories. The evidence parent is not a Git
        // root: Git would walk upward from it into the checkout running this fixture.
        services.PostConfigure<DelegationSettings>(settings => settings.AllowedRoots =
            [Path.Combine(Root, "repo"), Path.Combine(Root, "trees", "source")]);
        services.AddSingleton<LandDeliveryBoundary>(p => new FileBoundary(this, p.GetRequiredService<IServiceScopeFactory>()));
        var lease = services.SingleOrDefault(d => d.ServiceType == typeof(IRepositoryMutationLease));
        if (lease is not null) services.Remove(lease);
        services.AddSingleton<IRepositoryMutationLease>(p => new MoveDefaultOnLease(
            new RepositoryMutationLease(p.GetRequiredService<ILandingGit>()),
            Root,
            p.GetRequiredService<IServiceScopeFactory>()));
        var clock = new LandClock(Root);
        services.AddScoped(p => ActivatorUtilities.CreateInstance<AgentTaskLandService>(p, clock));
        services.AddScoped(p => ActivatorUtilities.CreateInstance<AgentTaskLandingProtocol>(p, clock));
        services.AddScoped(p => ActivatorUtilities.CreateInstance<AgentTaskLandMonitorService>(p, clock));
        services.AddScoped(p => ActivatorUtilities.CreateInstance<AgentTaskLandNotificationService>(p, clock));
        services.AddScoped(p => ActivatorUtilities.CreateInstance<AttentionService>(p, clock));
        services.AddSingleton<ILandingGit>(new EvidenceGit(Root));
        services.AddTransient<IStartupFilter>(_ => new AcceptanceObserver(Root));
        services.AddSingleton(p => new PtyDeliveryProfile(p.GetRequiredService<IServiceScopeFactory>(),
            p.GetRequiredService<Microsoft.Extensions.Logging.ILogger<PtyDeliveryProfile>>(),
            p.GetRequiredService<IOptions<DelegationSettings>>(), p.GetRequiredService<TimeProvider>(), backendOverride: "modern"));
        services.AddSingleton<Antiphon.Messaging.Client.IAntiphonMessagingProducer, RefusingCanaryMessaging>();
        services.AddSingleton<Antiphon.Messaging.Client.IAntiphonMessagingConsumer, RefusingCanaryMessaging>();
    }

    /// <summary>
    /// Fires once when <c>move-default-on-lease.json</c> is present: after the dispatcher's
    /// pre-lease base observation and before the locked claim, matching
    /// AgentTaskDispatchBaseGuardTests.C508_GuardRefMismatchWarnedOnce.
    /// </summary>
    private sealed class MoveDefaultOnLease(IRepositoryMutationLease inner, string root, IServiceScopeFactory scopes)
        : IRepositoryMutationLease
    {
        private int _fired;

        public async Task<RepositoryLease?> TryAcquireAsync(string repository, CancellationToken ct)
        {
            var path = Path.Combine(root, "move-default-on-lease.json");
            if (File.Exists(path) && Interlocked.Exchange(ref _fired, 1) == 0)
            {
                using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(path, ct));
                var projectId = doc.RootElement.GetProperty("projectId").GetGuid();
                var baseBranch = doc.RootElement.GetProperty("baseBranch").GetString()!;
                await using var scope = scopes.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                await db.Projects.Where(p => p.Id == projectId)
                    .ExecuteUpdateAsync(s => s.SetProperty(p => p.BaseBranch, baseBranch), ct);
                await File.WriteAllTextAsync(Path.Combine(root, "move-default-on-lease.fired.json"),
                    JsonSerializer.Serialize(new { projectId, baseBranch, at = DateTime.UtcNow, pid = Environment.ProcessId }), ct);
            }
            return await inner.TryAcquireAsync(repository, ct);
        }

        public bool Owns(RepositoryLease lease, string commonDirectory) =>
            inner.Owns(lease, commonDirectory);

        public Task<string?> DescribeUnavailableAsync(string repository, CancellationToken ct) =>
            inner.DescribeUnavailableAsync(repository, ct);
    }

    private sealed class FileBoundary(LandDeliveryOptions options, IServiceScopeFactory scopes) : LandDeliveryBoundary
    {
        private int _enqueueCalls;
        public override bool DropWakeup(string boundary, Guid identity) => options.Cut == "lost-flush" && boundary == "completion"
            || options.Cut == "lost-request" && boundary == "land-request";
        public override async Task ReachedAsync(string boundary, Guid taskId, Guid identity, CancellationToken ct)
        {
            if (boundary is "completion-scan" or "notification-scan" or "queue-existing-key" or "dispatch-warning-intent-scan")
                await File.WriteAllTextAsync(Path.Combine(options.Root, $"{boundary}-{Guid.NewGuid():N}.observation.json"),
                    JsonSerializer.Serialize(new { boundary, taskId, identity, at = DateTime.UtcNow, pid = Environment.ProcessId }), ct);
            var dispatchFile = Path.Combine(options.Root, "dispatch-task.txt");
            if (File.Exists(dispatchFile))
            {
                var owned = Guid.Parse(await File.ReadAllTextAsync(dispatchFile, ct));
                if (boundary == "dispatch-warning-before-materialize")
                {
                    await using var scope = scopes.CreateAsyncScope();
                    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                    taskId = await db.AgentTaskDispatchWarningIntents.Where(i => i.Id == identity).Select(i => i.TaskId).SingleAsync(ct);
                }
                if (taskId != owned) return;
            }
            if (options.Cut == "enqueue-errors" && boundary == "before-enqueue" && Interlocked.Increment(ref _enqueueCalls) <= 2)
            {
                await File.WriteAllTextAsync(Path.Combine(options.Root, $"enqueue-failure-{Guid.NewGuid():N}.json"),
                    JsonSerializer.Serialize(new { taskId, identity }), ct);
                throw new IOException("Owned notification insert failure");
            }
            var blocked = boundary == "before-execution" && !File.Exists(Path.Combine(options.Root, "execute.release"))
                || options.Cut == "terminal" && boundary is "terminal-committed" or "before-enqueue"
                || options.Cut == "queue" && boundary == "queue-inserted"
                || options.Cut == "receipt" && boundary == "receipt-before-save"
                || options.Cut == "attempt" && boundary == "queue-before-typing"
                || options.Cut == "verdict" && boundary == "queue-before-verdict";
            blocked |= options.Cut == "dispatch-claim" && boundary is "dispatch-warning-claim-committed" or "dispatch-warning-before-materialize"
                || options.Cut == "dispatch-projection" && boundary == "dispatch-warning-before-commit"
                || options.Cut == "pre-enqueue" && boundary == "before-enqueue";
            if (!blocked || File.Exists(Path.Combine(options.Root, boundary + ".release"))) return;
            var barrierPath = Path.Combine(options.Root, boundary + ".barrier.json");
            var temporary = barrierPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(new
            { boundary, taskId, identity, nonce = Path.GetFileName(options.Root), pid = Environment.ProcessId, start = System.Diagnostics.Process.GetCurrentProcess().StartTime.ToUniversalTime() }), ct);
            File.Move(temporary, barrierPath, true);
            while (!File.Exists(Path.Combine(options.Root, boundary + ".release"))
                && !(boundary == "before-execution" && File.Exists(Path.Combine(options.Root, "execute.release"))))
                await Task.Delay(50, ct);
        }
    }

    private sealed class LandClock(string root) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow()
        {
            var path = Path.Combine(root, "land-clock-seconds.txt");
            return DateTimeOffset.UtcNow.AddSeconds(File.Exists(path) && double.TryParse(File.ReadAllText(path), out var seconds) ? seconds : 0);
        }
    }

    /// <summary>
    /// CARD-0550/CARD-0459: native land issues ~740 git process spawns; at 80–200 ms each that
    /// consumes the 60 s receipt window before FakeGrok is even typed. Cache stable identity
    /// reads and collapse Inspect's paired IdentityAsync. Fetch of observation pins does not
    /// invalidate worktree identity. Production <see cref="LandingGit"/> stays uncached.
    /// </summary>
    private sealed class EvidenceGit(string root) : LandingGit
    {
        private readonly object _gate = new();
        private readonly Dictionary<string, LandingGitResult> _stable = new(StringComparer.Ordinal);
        private readonly Dictionary<string, LandingGitResult> _volatile = new(StringComparer.Ordinal);
        private readonly Dictionary<string, LandingSourceObservation> _sourceObs = new(StringComparer.Ordinal);
        private readonly Dictionary<string, LandingRemoteObservation> _remoteObs = new(StringComparer.Ordinal);

        protected override void ConfigureProcess(ProcessStartInfo start)
        {
            start.Environment["GIT_FLUSH"] = "0";
            start.ArgumentList.Add("-c");
            start.ArgumentList.Add("gc.auto=0");
        }

        private async Task RecordAsync(string repository, IReadOnlyList<string> arguments, LandingGitResult result)
        {
            var path = Path.Combine(root, $"protocol-git-{Guid.NewGuid():N}.json");
            await File.WriteAllTextAsync(path + ".tmp", JsonSerializer.Serialize(new {
                repository, arguments, result.ExitCode, at = DateTime.UtcNow, pid = Environment.ProcessId }));
            File.Move(path + ".tmp", path);
        }

        private static string Key(string repository, IReadOnlyList<string> arguments)
            => Path.GetFullPath(repository) + "\0" + string.Join('\0', arguments);

        private static bool IsStableRead(IReadOnlyList<string> arguments)
            => arguments.Contains("--git-common-dir")
               || arguments.Contains("check-ref-format")
               || (arguments.Count >= 2 && arguments[0] == "remote" && arguments[1] == "get-url");

        private static bool InvalidatesVolatile(IReadOnlyList<string> arguments)
        {
            if (arguments.Count > 1 && arguments[0] == "worktree" && arguments[1] != "list")
                return true;
            foreach (var argument in arguments)
            {
                if (argument is "rebase" or "merge" or "push" or "update-ref" or "add" or "remove"
                    or "commit" or "checkout" or "checkout-index" or "restore" or "reset")
                    return true;
            }
            return false;
        }

        public override async Task<LandingGitResult> RunAsync(string repository, IReadOnlyList<string> arguments, CancellationToken ct)
        {
            var key = Key(repository, arguments);
            var stable = IsStableRead(arguments);
            lock (_gate)
            {
                if (stable && _stable.TryGetValue(key, out var cachedStable))
                    return cachedStable;
                if (!stable && !InvalidatesVolatile(arguments) && _volatile.TryGetValue(key, out var cached))
                    return cached;
            }

            var result = await base.RunAsync(repository, arguments, ct);
            await RecordAsync(repository, arguments, result);
            lock (_gate)
            {
                if (InvalidatesVolatile(arguments))
                {
                    _volatile.Clear();
                    if (arguments.Any(a => a is "push"))
                        _remoteObs.Clear();
                }
                else if (stable)
                    _stable[key] = result;
                else
                    _volatile[key] = result;
            }
            return result;
        }

        public override async Task<LandingGitResult> RunOwnedAsync(string repository, IReadOnlyList<string> arguments,
            Func<int, long, CancellationToken, Task> started, CancellationToken ct)
        {
            var result = await base.RunOwnedAsync(repository, arguments, started, ct);
            await RecordAsync(repository, arguments, result);
            lock (_gate)
            {
                _volatile.Clear();
                _remoteObs.Clear();
            }
            return result;
        }

        public override async Task<LandingSourceObservation> ObserveSourceAsync(string repository, string sourceFullRef,
            string observationPrefix, CancellationToken ct)
        {
            var key = Path.GetFullPath(repository) + "\0" + sourceFullRef;
            lock (_gate)
            {
                if (_sourceObs.TryGetValue(key, out var cached) && cached.Accepted)
                    return cached;
            }
            var result = await base.ObserveSourceAsync(repository, sourceFullRef, observationPrefix, ct);
            lock (_gate)
            {
                if (result.Accepted)
                    _sourceObs[key] = result;
            }
            return result;
        }

        public override async Task<LandingRemoteObservation> ObserveAsync(string repository, LandingDestination destination,
            string sourceSha, string observationRef, CancellationToken ct)
        {
            var key = Path.GetFullPath(repository) + "\0" + destination.FullRef + "\0" + sourceSha;
            lock (_gate)
            {
                if (_remoteObs.TryGetValue(key, out var cached) && cached.Reason is null)
                    return cached;
            }
            var result = await base.ObserveAsync(repository, destination, sourceSha, observationRef, ct);
            lock (_gate)
            {
                if (result.Reason is null)
                    _remoteObs[key] = result;
            }
            return result;
        }
    }

    private sealed class AcceptanceObserver(string root) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use(async (context, onward) =>
            {
                var path = context.Request.Path.Value ?? "";
                if (context.Request.Method != "POST"
                    || !(path.EndsWith("/land", StringComparison.Ordinal) || path.EndsWith("/land/v2", StringComparison.Ordinal)))
                { await onward(); return; }
                var original = context.Response.Body;
                await using var capture = new MemoryStream();
                context.Response.Body = capture;
                try
                {
                    await onward();
                    if (context.Response.StatusCode == 202)
                    {
                        using var encoded = new MemoryStream(capture.ToArray());
                        using Stream decoded = context.Response.Headers.ContentEncoding.ToString() switch {
                            "br" => new System.IO.Compression.BrotliStream(encoded, System.IO.Compression.CompressionMode.Decompress),
                            "gzip" => new System.IO.Compression.GZipStream(encoded, System.IO.Compression.CompressionMode.Decompress),
                            _ => encoded };
                        using var reader = new StreamReader(decoded);
                        await File.WriteAllTextAsync(Path.Combine(root, "http-202.json"), await reader.ReadToEndAsync());
                    }
                    capture.Position = 0; await capture.CopyToAsync(original);
                }
                finally { context.Response.Body = original; }
            });
            next(app);
        };
    }
}
