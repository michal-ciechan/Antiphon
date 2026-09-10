using System.Text.Json;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Antiphon.E2E.Fixtures;

internal sealed record LandDeliveryOptions(string Root, string Cut = "none")
{
    public string Gate => Path.Combine(Root, "caller-busy");
    public void Configure(Dictionary<string, string?> settings)
    {
        settings["Agents:DefaultDefinition"] = "c467-grok";
        settings["Agents:Definitions:c467-grok:Kind"] = "Grok";
        settings["Agents:Definitions:c467-grok:Exe"] = Path.Combine(AppContext.BaseDirectory, "fakegrok", "fakegrok.exe");
        settings["Agents:Definitions:c467-grok:Env:GROK_HOME"] = Path.Combine(Root, "native");
        settings["Agents:Definitions:c467-grok:Env:ANTIPHON_FAKE_BUSY_GATE"] = Gate;
        settings["Git:WorkspacePath"] = Path.Combine(Root, "repo");
        settings["Git:WorktreeBasePath"] = Path.Combine(Root, "trees");
        settings["GitHub:Enabled"] = "false";
        settings["ChannelBridge:Enabled"] = "false";
        settings["AntiphonMessaging:BootstrapServers"] = "127.0.0.1:1";
        settings["Delegation:CheckInterpreterEnabled"] = "false";
        settings["Delegation:DiagnoseEnabled"] = "false";
        settings["Delegation:OutputDistillerEnabled"] = "false";
        settings["Supervision:DeliveryVerification:Enabled"] = "true";
        settings["Supervision:DeliveryVerification:TranscriptConfirmEnabled"] = "true";
    }
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<LandDeliveryBoundary>(new FileBoundary(this));
        services.AddSingleton(p => new PtyDeliveryProfile(p.GetRequiredService<IServiceScopeFactory>(),
            p.GetRequiredService<Microsoft.Extensions.Logging.ILogger<PtyDeliveryProfile>>(),
            p.GetRequiredService<IOptions<DelegationSettings>>(), p.GetRequiredService<TimeProvider>(), backendOverride: "modern"));
        services.AddSingleton<Antiphon.Messaging.Client.IAntiphonMessagingProducer, RefusingCanaryMessaging>();
        services.AddSingleton<Antiphon.Messaging.Client.IAntiphonMessagingConsumer, RefusingCanaryMessaging>();
    }

    private sealed class FileBoundary(LandDeliveryOptions options) : LandDeliveryBoundary
    {
        public override bool DropWakeup(string boundary, Guid identity) => options.Cut == "lost-flush" && boundary == "completion"
            || options.Cut == "lost-request" && boundary == "land-request";
        public override async Task ReachedAsync(string boundary, Guid taskId, Guid identity, CancellationToken ct)
        {
            var blocked = boundary == "before-execution" && !File.Exists(Path.Combine(options.Root, "execute.release"))
                || options.Cut == "terminal" && boundary is "terminal-committed" or "before-enqueue"
                || options.Cut == "queue" && boundary == "queue-inserted"
                || options.Cut == "receipt" && boundary == "receipt-before-save";
            if (!blocked) return;
            await File.WriteAllTextAsync(Path.Combine(options.Root, boundary + ".barrier.json"), JsonSerializer.Serialize(new
            { boundary, taskId, identity, pid = Environment.ProcessId, start = System.Diagnostics.Process.GetCurrentProcess().StartTime.ToUniversalTime() }), ct);
            while (!File.Exists(Path.Combine(options.Root, boundary + ".release"))
                && !(boundary == "before-execution" && File.Exists(Path.Combine(options.Root, "execute.release"))))
                await Task.Delay(50, ct);
        }
    }
}
