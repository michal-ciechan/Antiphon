using Aspire.Hosting.Lifecycle;

namespace Antiphon.AppHost.Supervisor;

/// <summary>
/// Runs before AppHost reports "started". For each registered DaemonProcessResource:
/// - if the port is already listening, adopt the running service (no restart)
/// - if not, start a detached supervisor via run-daemon.ps1
/// </summary>
internal sealed class DaemonProcessLifecycleHook(
    DaemonProcessService supervisor,
    DistributedApplicationModel appModel) : IDistributedApplicationLifecycleHook
{
    public async Task BeforeStartAsync(DistributedApplicationModel application, CancellationToken ct)
    {
        foreach (var resource in appModel.Resources.OfType<DaemonProcessResource>())
            await supervisor.InitialiseAsync(resource, ct);
    }
}
