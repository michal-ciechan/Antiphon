namespace Antiphon.AppHost.Supervisor;

/// <summary>Static description of a daemon process.</summary>
public sealed record DaemonProcessConfig(
    string Executable,
    string[] Args,
    string WorkingDirectory,
    int Port,
    string? HealthPath = "/health");
