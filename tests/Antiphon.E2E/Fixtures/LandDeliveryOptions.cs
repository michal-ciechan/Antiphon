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
        settings["Agents:Definitions:c467-grok:Kind"] = "Grok";
        settings["Agents:Definitions:c467-grok:Exe"] = Path.Combine(Path.GetDirectoryName(typeof(LandDeliveryOptions).Assembly.Location)!, "fakegrok", "fakegrok.exe");
        settings["Agents:Definitions:c467-grok:Env:GROK_HOME"] = Path.Combine(Root, "native");
        settings["Agents:Definitions:c467-grok:Env:ANTIPHON_FAKE_BUSY_GATE"] = Gate;
        settings["Agents:Definitions:c467-grok:NonSecretEnvironmentNames:0"] = "GROK_HOME";
        settings["Agents:Definitions:c467-grok:NonSecretEnvironmentNames:1"] = "ANTIPHON_FAKE_BUSY_GATE";
        settings["Git:WorkspacePath"] = Path.Combine(Root, "repo");
        settings["Git:WorktreeBasePath"] = Path.Combine(Root, "trees");
        settings["GitHub:Enabled"] = "false";
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
        services.AddSingleton<LandDeliveryBoundary>(new FileBoundary(this));
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

    private sealed class FileBoundary(LandDeliveryOptions options) : LandDeliveryBoundary
    {
        private int _enqueueCalls;
        public override bool DropWakeup(string boundary, Guid identity) => options.Cut == "lost-flush" && boundary == "completion"
            || options.Cut == "lost-request" && boundary == "land-request";
        public override async Task ReachedAsync(string boundary, Guid taskId, Guid identity, CancellationToken ct)
        {
            if (boundary is "completion-scan" or "notification-scan" or "queue-existing-key")
                await File.WriteAllTextAsync(Path.Combine(options.Root, $"{boundary}-{Guid.NewGuid():N}.observation.json"),
                    JsonSerializer.Serialize(new { boundary, taskId, identity, at = DateTime.UtcNow, pid = Environment.ProcessId }), ct);
            if (options.Cut == "enqueue-errors" && boundary == "before-enqueue" && Interlocked.Increment(ref _enqueueCalls) <= 2)
                throw new IOException("Owned notification insert failure");
            var blocked = boundary == "before-execution" && !File.Exists(Path.Combine(options.Root, "execute.release"))
                || options.Cut == "terminal" && boundary is "terminal-committed" or "before-enqueue"
                || options.Cut == "queue" && boundary == "queue-inserted"
                || options.Cut == "receipt" && boundary == "receipt-before-save"
                || options.Cut == "attempt" && boundary == "queue-before-typing"
                || options.Cut == "verdict" && boundary == "queue-before-verdict";
            if (!blocked || File.Exists(Path.Combine(options.Root, boundary + ".release"))) return;
            var barrierPath = Path.Combine(options.Root, boundary + ".barrier.json");
            await File.WriteAllTextAsync(barrierPath + ".tmp", JsonSerializer.Serialize(new
            { boundary, taskId, identity, nonce = Path.GetFileName(options.Root), pid = Environment.ProcessId, start = System.Diagnostics.Process.GetCurrentProcess().StartTime.ToUniversalTime() }), ct);
            File.Move(barrierPath + ".tmp", barrierPath, true);
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

    private sealed class EvidenceGit(string root) : LandingGit
    {
        private async Task RecordAsync(string repository, IReadOnlyList<string> arguments, LandingGitResult result)
        {
            var path = Path.Combine(root, $"protocol-git-{Guid.NewGuid():N}.json");
            await File.WriteAllTextAsync(path + ".tmp", JsonSerializer.Serialize(new {
                repository, arguments, result.ExitCode, at = DateTime.UtcNow, pid = Environment.ProcessId }));
            File.Move(path + ".tmp", path);
        }
        public override async Task<LandingGitResult> RunAsync(string repository, IReadOnlyList<string> arguments, CancellationToken ct)
        {
            var failCleanup = Path.Combine(root, "cleanup-io.fail");
            if (arguments.Count >= 2 && arguments[0] == "worktree" && arguments[1] == "remove" && File.Exists(failCleanup))
            {
                File.Move(failCleanup, Path.Combine(root, "cleanup-io.failed"));
                throw new IOException("Owned one-shot cleanup I/O failure before removal");
            }
            var result = await base.RunAsync(repository, arguments, ct); await RecordAsync(repository, arguments, result); return result;
        }
        public override async Task<LandingGitResult> RunOwnedAsync(string repository, IReadOnlyList<string> arguments,
            Func<int, long, CancellationToken, Task> started, CancellationToken ct)
        {
            var result = await base.RunOwnedAsync(repository, arguments, started, ct); await RecordAsync(repository, arguments, result); return result;
        }
    }

    private sealed class AcceptanceObserver(string root) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use(async (context, onward) =>
            {
                if (!context.Request.Path.Value!.EndsWith("/land", StringComparison.Ordinal) || context.Request.Method != "POST")
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
