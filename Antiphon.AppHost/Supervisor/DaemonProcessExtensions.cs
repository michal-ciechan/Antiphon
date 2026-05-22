using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Lifecycle;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Antiphon.AppHost.Supervisor;

public static class DaemonProcessExtensions
{
    /// <summary>
    /// Register the daemon supervisor infrastructure once per AppHost.
    /// Call before AddDaemonProcess.
    /// </summary>
    public static IDistributedApplicationBuilder AddDaemonSupervisor(
        this IDistributedApplicationBuilder builder)
    {
        builder.Services.AddSingleton<DaemonProcessService>();
        builder.Services.AddSingleton<IHostedService>(sp => sp.GetRequiredService<DaemonProcessService>());
        builder.Services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IDistributedApplicationLifecycleHook, DaemonProcessLifecycleHook>());
        builder.Services.AddHostedService<ControlApiService>();
        return builder;
    }

    /// <summary>
    /// Add a daemon process resource with Start / Stop / Restart commands.
    /// </summary>
    public static IResourceBuilder<DaemonProcessResource> AddDaemonProcess(
        this IDistributedApplicationBuilder builder,
        string name,
        DaemonProcessConfig config)
    {
        // Log dir = <repoRoot>/logs/
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        var logDir   = Path.Combine(repoRoot, "logs");
        Directory.CreateDirectory(logDir);

        var resource = new DaemonProcessResource(name, config, logDir);
        var rb = builder.AddResource(resource)
            .WithInitialState(new CustomResourceSnapshot
            {
                ResourceType = "Daemon",
                State = KnownResourceStates.Starting,
                Properties = [new("Port", config.Port.ToString())]
            });

        // Dashboard commands
        rb.WithCommand(
            "stop",
            "Stop",
            async ctx =>
            {
                var svc = ctx.ServiceProvider.GetRequiredService<DaemonProcessService>();
                await svc.StopAsync(name, ctx.CancellationToken);
                return CommandResults.Success();
            },
            new CommandOptions { IconName = "Stop", IsHighlighted = false });

        rb.WithCommand(
            "start",
            "Start",
            async ctx =>
            {
                var svc = ctx.ServiceProvider.GetRequiredService<DaemonProcessService>();
                await svc.StartAsync(name, ctx.CancellationToken);
                return CommandResults.Success();
            },
            new CommandOptions { IconName = "Play", IsHighlighted = false });

        rb.WithCommand(
            "restart",
            "Restart",
            async ctx =>
            {
                var svc = ctx.ServiceProvider.GetRequiredService<DaemonProcessService>();
                await svc.RestartAsync(name, ctx.CancellationToken);
                return CommandResults.Success();
            },
            new CommandOptions { IconName = "ArrowClockwise", IsHighlighted = true });

        return rb;
    }
}
