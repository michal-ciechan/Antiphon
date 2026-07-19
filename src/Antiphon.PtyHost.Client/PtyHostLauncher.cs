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
    public async Task<int> LaunchDetachedAsync(
        Guid sessionId,
        string manifestDir,
        string? hostLogFile = null,
        string? pipeName = null,
        TimeSpan? launchTimeout = null,
        TimeSpan? lingerTtl = null,
        int? ringCapChars = null,
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
                     sessionId, manifestDir, hostLogFile, pipeName, launchTimeout, lingerTtl, ringCapChars))
            psi.ArgumentList.Add(arg);

        using var intermediary = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start pty-host spawn intermediary.");

        var stdout = await intermediary.StandardOutput.ReadToEndAsync(ct);
        var stderr = await intermediary.StandardError.ReadToEndAsync(ct);
        await intermediary.WaitForExitAsync(ct);

        if (intermediary.ExitCode != 0 || !int.TryParse(stdout.Trim(), out var hostPid))
            throw new InvalidOperationException(
                $"pty-host spawn intermediary failed (exit {intermediary.ExitCode}): {stderr} {stdout}".Trim());

        return hostPid;
    }

    private static IEnumerable<string> BuildHostArgs(
        Guid sessionId,
        string manifestDir,
        string? hostLogFile,
        string? pipeName,
        TimeSpan? launchTimeout,
        TimeSpan? lingerTtl,
        int? ringCapChars)
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
    }
}
