using System.Diagnostics;
using Antiphon.PtyHost.Protocol;

namespace Antiphon.PtyHost.Client;

/// <summary>
/// Launches detached pty-hosts from shadow-copied binaries. The chain is
/// runner → intermediary (<c>Antiphon.PtyHost[.exe] --spawn …</c>, exits immediately) → host,
/// so the host's recorded parent is dead and no tree-kill aimed at the runner can reach it.
/// </summary>
public sealed class PtyHostLauncher(ShadowCopyStore store, string hostSourceDir)
{
    public static string HostExeName => OperatingSystem.IsWindows()
        ? "Antiphon.PtyHost.exe"
        : "Antiphon.PtyHost";

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
        string? custodyStoreRoot = null,
        string? custodyBackend = null,
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
            // The intermediary's detached child inherits this CWD. In custody mode neither
            // host may keep the verification snapshot open through its current directory.
            WorkingDirectory = custodyStoreRoot is null ? "" : CurrentShadowDir,
        };
        foreach (var arg in BuildHostArgs(
                     sessionId, manifestDir, hostLogFile, pipeName, launchTimeout, lingerTtl,
                     ringCapChars, ptyBackend))
            psi.ArgumentList.Add(arg);
        if (custodyStoreRoot is not null)
        {
            psi.ArgumentList.Add("--custody-store");
            psi.ArgumentList.Add(Path.GetFullPath(custodyStoreRoot));
            // CARD-0604 D-17: the runner's own probe result travels with the store root. A host
            // never decides for itself which mechanism it can perform.
            if (custodyBackend is not null)
            {
                psi.ArgumentList.Add("--custody-backend");
                psi.ArgumentList.Add(custodyBackend);
            }
        }

        using var intermediary = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start pty-host spawn intermediary.");

        // Reads are not ct-gated: an already-cancelled token used to throw before stdout was
        // consumed, which is how the detached host pid was lost (CARD-0086). WaitAsync(ct) is
        // the cancel point; the drain below uses CancellationToken.None so we can still parse
        // the pid and kill it. The kill deliberately stays outside the seam.
        var reads = BeginReads(intermediary);

        try
        {
            return await AwaitPidAsync(intermediary, reads, ct);
        }
        catch
        {
            await TryKillSpawnedHostAsync(reads);
            throw;
        }
    }

    /// <summary>The intermediary's first stdout line, its whole stderr, and its exit.</summary>
    internal sealed record IntermediaryReads(Task<string?> PidLine, Task<string> Stderr, Task Exit);

    /// <summary>How long after the intermediary's exit to wait for a pid line it already wrote.</summary>
    private static readonly TimeSpan PidLineGrace = TimeSpan.FromSeconds(5);

    /// <summary>Best-effort budget for collecting stderr on the failure path.</summary>
    private static readonly TimeSpan StderrDrainGrace = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Starts reading the intermediary. Stderr is read eagerly so a chatty failure can never fill
    /// its pipe and block the exit we gate on, but it is only ever consulted on the failure path
    /// — see <see cref="AwaitPidAsync"/>. Both reads get a fault observer: the caller disposes the
    /// <see cref="Process"/> on the way out, and a read that faults after that must not surface as
    /// an unobserved task exception.
    /// </summary>
    internal static IntermediaryReads BeginReads(Process intermediary)
    {
        var pidLine = intermediary.StandardOutput.ReadLineAsync();
        var stderr = intermediary.StandardError.ReadToEndAsync();
        Observe(pidLine);
        Observe(stderr);
        return new IntermediaryReads(pidLine, stderr, intermediary.WaitForExitAsync());
    }

    private static void Observe(Task task) =>
        _ = task.ContinueWith(
            static t => _ = t.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

    /// <summary>
    /// CARD-0594: success is the intermediary's exit plus a parseable pid line, never stdout or
    /// stderr EOF. The detached host inherits the intermediary's stdio and keeps those pipes open
    /// for its whole life on POSIX, so a gate on EOF cannot return until the host dies — which is
    /// the launch deadlock this replaces. <see cref="Process.WaitForExitAsync"/> observes the exit
    /// independently of stream EOF here because the streams are read directly rather than through
    /// <c>BeginOutputReadLine</c>.
    /// </summary>
    internal static async Task<int> AwaitPidAsync(
        Process intermediary, IntermediaryReads reads, CancellationToken ct)
    {
        await reads.Exit.WaitAsync(ct);

        string? pidLine = null;
        try
        {
            // The healthy intermediary writes the pid before it exits, so the line is already
            // buffered; the grace only covers the write landing after the exit is observed.
            pidLine = await reads.PidLine.WaitAsync(PidLineGrace, ct);
        }
        catch (TimeoutException)
        {
            // No pid line. Reported as a launch failure below, with whatever stderr says.
        }

        if (intermediary.ExitCode == 0 && int.TryParse(pidLine?.Trim(), out var hostPid) && hostPid > 0)
            return hostPid;

        var stderr = await DrainStderrAsync(reads);
        throw new InvalidOperationException(
            $"pty-host spawn intermediary failed (exit {intermediary.ExitCode}): {stderr} {pidLine}".Trim());
    }

    private static async Task<string> DrainStderrAsync(IntermediaryReads reads)
    {
        try
        {
            return await reads.Stderr.WaitAsync(StderrDrainGrace, CancellationToken.None);
        }
        catch
        {
            // Best effort: a stderr pipe still held open by the spawned host must not turn a
            // launch failure into a hang.
            return "";
        }
    }

    /// <summary>
    /// CARD-0086: if the intermediary has started and this method is about to throw, drain the
    /// pid line and WaitForExit on <see cref="CancellationToken.None"/> (the caller's token may
    /// already be cancelled), parse the host pid if present, and kill it. A kill failure is
    /// swallowed so it never replaces the launch exception. Stderr is not drained here: it is the
    /// stream the spawned host can hold open indefinitely.
    /// </summary>
    private static async Task TryKillSpawnedHostAsync(IntermediaryReads reads)
    {
        try
        {
            try
            {
                await Task.WhenAll(reads.PidLine, reads.Exit)
                    .WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
            }
            catch
            {
                // Drain timed out or the cancelled read left a stream unreadable — still try
                // whatever pid we already have.
            }

            var stdout = reads.PidLine.IsCompletedSuccessfully ? reads.PidLine.Result : null;

            if (int.TryParse(stdout?.Trim(), out var hostPid) && hostPid > 0)
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
