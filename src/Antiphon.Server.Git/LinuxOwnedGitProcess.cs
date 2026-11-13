using System.ComponentModel;
using System.Diagnostics;

namespace Antiphon.Server.Infrastructure.Git;

/// <summary>
/// Linux managed-stream adapter, not yet selected by callers. The mandatory census provider
/// owns marker discovery/signaling; Process owns its native handles. No dispose-time kill.
/// The marker is applied to child start info before creation, never to the worker environment.
/// </summary>
internal sealed class LinuxOwnedGitProcess(ILinuxOwnedGitMembers members) : IOwnedGitProcessOperations
{
    private readonly Process _child = new();
    private StreamWriter? _input;
    private StreamReader? _output, _error;
    private bool _disposed;
    internal LinuxOwnedGitProcess() : this(new LinuxOwnedGitMembers()) { }
    public StreamWriter StandardInput => _input ??= _child.StandardInput;
    public StreamReader StandardOutput => _output ??= _child.StandardOutput;
    public StreamReader StandardError => _error ??= _child.StandardError;
    public bool? RootExited
    {
        get
        {
            try { return _child.HasExited; }
            catch (Exception ex) when (ex is InvalidOperationException or Win32Exception) { return null; }
        }
    }
    public int ExitCode => _child.ExitCode;

    public OwnedGitLaunch Start(ProcessStartInfo start)
    {
        members.SetMarker(start.Environment[OwnedGitProcess.MarkerVariable]
            ?? throw new InvalidOperationException("owned_git_marker_missing"));
        _child.StartInfo = start;
        if (!_child.Start()) return new(OwnedGitLaunchState.NoChild, null);
        // Process.Dispose does not close redirected streams once callers have used them.
        // Capture all redirected ends immediately, including post-start identity failures.
        if (start.RedirectStandardInput) _input = _child.StandardInput;
        if (start.RedirectStandardOutput) _output = _child.StandardOutput;
        if (start.RedirectStandardError) _error = _child.StandardError;
        DateTime? startUtc;
        try { startUtc = _child.StartTime.ToUniversalTime(); }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception) { startUtc = null; }
        return new(OwnedGitLaunchState.Started, members.CaptureRoot(_child.Id, startUtc));
    }

    public async Task<bool> WaitForRootExitAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _child.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            return _child.HasExited;
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception) { return false; }
    }

    public ValueTask<OwnedGitMembers> ProbeMembersAsync(OwnedGitRoot? root, OwnedGitDeadline deadline,
        CancellationToken cancellationToken) => members.ProbeAsync(root, deadline, cancellationToken);
    public ValueTask<int> TerminateMembersAsync(OwnedGitRoot? root, OwnedGitDeadline deadline,
        CancellationToken cancellationToken) => members.SignalAsync(root, deadline, cancellationToken);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _input?.Dispose(); }
        finally
        {
            try { _output?.Dispose(); }
            finally
            {
                try { _error?.Dispose(); }
                finally
                {
                    try { _child.Dispose(); }
                    finally { members.Dispose(); }
                }
            }
        }
    }
}
