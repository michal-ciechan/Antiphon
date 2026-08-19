using System.Diagnostics;
using Antiphon.PtyHost.Protocol;

namespace Antiphon.PtyHost.Client;

/// <summary>
/// Launches detached pty-hosts from shadow-copied binaries. The chain is
/// runner → intermediary (<c>Antiphon.PtyHost.exe --spawn …</c>, exits immediately) → host,
/// so the host's recorded parent is dead and no tree-kill aimed at the runner can reach it.
/// </summary>
public sealed class PtyHostLauncher(ShadowCopyStore store, string hostSourceDir)
{
    public const string HostExeName = "Antiphon.PtyHost.exe";

    private readonly object _gate = new();
    private string? _cachedShadowDir;

    /// <summary>The shadow dir used for new launches (hashed once, cached per launcher).</summary>
    public string CurrentShadowDir
    {
        get
        {
            lock (_gate)
                return _cachedShadowDir ??= store.EnsureCurrent(hostSourceDir);
        }
    }

    /// <summary>
    /// Spawns a detached host for <paramref name="sessionId"/> and returns its pid.
    /// The host is empty (WaitingForLaunch) - connect to the pipe and send Launch next.
    /// </summary>
    /// <param name="ptyBackend">
    /// Which pseudoconsole the host's session should spawn under (<c>inbox</c>/<c>modern</c>), or
    /// null to leave it to the environment the host inherits. CARD-0045: passed as an argument
    /// rather than set on the StartInfo, so the choice is visible in the host's own command line
    /// while diagnosing a live host.
    /// </param>
    public async Task<int> LaunchDetachedAsync(
        Guid sessionId,
        string manifestDir,
        string? hostLogFile = null,
        string? pipeName = null,
        TimeSpan? launchTimeout = null,
        TimeSpan? lingerTtl = null,
        int? ringCapChars = null,
        string? ptyBackend = null,
        CancellationToken ct = default)
    {
        var exe = Path.Combine(CurrentShadowDir, HostExeName);
        if (!File.Exists(exe))
            throw new FileNotFoundException($"pty-host exe missing from shadow copy: {exe}");

        var psi = new ProcessStartInfo
        {
            FileName = exe,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var arg in BuildHostArgs(
                     sessionId, manifestDir, hostLogFile, pipeName, launchTimeout, lingerTtl,
                     ringCapChars, ptyBackend))
            psi.ArgumentList.Add(arg);

        using var intermediary = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start pty-host spawn intermediary.");

        // Reads are not ct-gated: an already-cancelled token used to throw before stdout was
        // consumed, which is how the detached host pid was lost (CARD-0086). WaitAsync(ct) is
        // the cancel point; the drain below uses CancellationToken.None so we can still parse
        // the pid and kill it.
        var stdoutTask = intermediary.StandardOutput.ReadToEndAsync();
        var stderrTask = intermediary.StandardError.ReadToEndAsync();
        var exitTask = intermediary.WaitForExitAsync();
        var stdout = "";
        var stderr = "";

        try
        {
            await Task.WhenAll(stdoutTask, stderrTask, exitTask).WaitAsync(ct);
            stdout = stdoutTask.Result;
            stderr = stderrTask.Result;

            if (intermediary.ExitCode != 0 || !int.TryParse(stdout.Trim(), out var hostPid))
                throw new InvalidOperationException(
                    $"pty-host spawn intermediary failed (exit {intermediary.ExitCode}): {stderr} {stdout}".Trim());

            return hostPid;
        }
        catch
        {
            await TryKillSpawnedHostAsync(intermediary, stdoutTask, stderrTask, exitTask, stdout);
            throw;
        }
    }

    /// <summary>
    /// CARD-0086: if the intermediary has started and this method is about to throw, drain
    /// stdout/stderr and WaitForExit on <see cref="CancellationToken.None"/> (the caller's
    /// token may already be cancelled), parse the host pid if present, and kill it. A kill
    /// failure is swallowed so it never replaces the launch exception.
    /// </summary>
    private static async Task TryKillSpawnedHostAsync(
        Process intermediary,
        Task<string> stdoutTask,
        Task<string> stderrTask,
        Task exitTask,
        string stdoutAlready)
    {
        try
        {
            try
            {
                await Task.WhenAll(stdoutTask, stderrTask, exitTask)
                    .WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
            }
            catch
            {
                // Drain timed out or the cancelled read left a stream unreadable — still try
                // whatever pid we already have.
            }

            var stdout = stdoutAlready;
            if (string.IsNullOrEmpty(stdout) && stdoutTask.IsCompletedSuccessfully)
                stdout = stdoutTask.Result;

            if (int.TryParse(stdout.Trim(), out var hostPid) && hostPid > 0)
            {
                try
                {
                    using var host = Process.GetProcessById(hostPid);
                    if (!host.HasExited)
                        host.Kill(entireProcessTree: true);
                }
                catch
                {
                    // Already gone, pid reuse, or access denied.
                }
            }
        }
        catch
        {
            // Kill failure is swallowed so it never replaces the launch exception.
        }
    }

    private static IEnumerable<string> BuildHostArgs(
        Guid sessionId,
        string manifestDir,
        string? hostLogFile,
        string? pipeName,
        TimeSpan? launchTimeout,
        TimeSpan? lingerTtl,
        int? ringCapChars,
        string? ptyBackend)
    {
        yield return "--spawn";
        yield return "--session";
        yield return sessionId.ToString();
        yield return "--pipe";
        yield return pipeName ?? PtyHostProtocol.PipeNameFor(sessionId);
        yield return "--manifest-dir";
        yield return manifestDir;
        if (hostLogFile is not null)
        {
            yield return "--log";
            yield return hostLogFile;
        }

        if (launchTimeout is { } lt)
        {
            yield return "--launch-timeout-sec";
            yield return ((int)lt.TotalSeconds).ToString();
        }

        if (lingerTtl is { } ttl)
        {
            yield return "--linger-hours";
            yield return ttl.TotalHours.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        if (ringCapChars is { } cap)
        {
            yield return "--ring-cap-chars";
            yield return cap.ToString();
        }

        if (!string.IsNullOrWhiteSpace(ptyBackend))
        {
            yield return "--pty-backend";
            yield return ptyBackend;
        }
    }
}
