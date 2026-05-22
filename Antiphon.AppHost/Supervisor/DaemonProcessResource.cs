using Aspire.Hosting.ApplicationModel;

namespace Antiphon.AppHost.Supervisor;

/// <summary>
/// A long-lived daemon resource managed outside the AppHost process group.
/// The backing pwsh.exe supervisor survives AppHost restart/exit; services
/// survive as children of the supervisor.
/// </summary>
public sealed class DaemonProcessResource(
    string name,
    DaemonProcessConfig config,
    string logDir)
    : Resource(name)
{
    private readonly string _logDir = logDir;
    private readonly string _name   = name;

    public DaemonProcessConfig Config { get; } = config;

    // Derived paths under <repoRoot>/logs/
    public string LogFile           => Path.Combine(_logDir, $"{_name}.log");
    public string StateFile         => Path.Combine(_logDir, $"{_name}.state");
    public string SupervisorPidFile => Path.Combine(_logDir, $"{_name}.supervisor.pid");
    public string ServicePidFile    => Path.Combine(_logDir, $"{_name}.service.pid");
}
