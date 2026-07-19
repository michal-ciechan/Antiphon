namespace Antiphon.AppHost.Supervisor;

/// <summary>Static description of a daemon process.</summary>
/// <param name="BuildProjectDir">
/// When set, the supervisor runs <c>dotnet build</c> on this directory before each (re)launch and
/// then runs <see cref="Executable"/> directly (which must be the built exe path), NOT
/// <c>dotnet run</c>. Required for the session-runner so its detached pty-hosts are not captured by
/// the <c>dotnet run</c> muxer's kill-on-close job. See the 2026-07-19 pty-host-split spec.
/// </param>
public sealed record DaemonProcessConfig(
    string Executable,
    string[] Args,
    string WorkingDirectory,
    int Port,
    string? HealthPath = "/health",
    string? BuildProjectDir = null);
