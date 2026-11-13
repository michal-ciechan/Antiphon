using System.ComponentModel;
using System.Diagnostics;

namespace Antiphon.Server.Infrastructure.Git;

internal enum OwnedGitLifecycle { Prepared, Started, Terminating, Joined, Unconfirmed, Disposed }
internal enum OwnedGitLaunchState { NoChild, Started, Ambiguous }
internal enum OwnedGitMembershipState { Unknown, Empty, Live }
internal enum OwnedGitReason { None, NoChild, LaunchUnconfirmed, Deadline, ObservationFailed }

internal sealed record OwnedGitRoot(int Pid, DateTime? StartUtc, long? LinuxStartTicks);
internal sealed record OwnedGitLaunch(OwnedGitLaunchState State, OwnedGitRoot? Root);

/// <summary>Unknown may include positively observed survivors. An incomplete scan is never Empty.</summary>
internal sealed record OwnedGitMembers
{
    internal OwnedGitMembers(OwnedGitMembershipState state, int survivors = 0)
    {
        if (survivors < 0 || (state == OwnedGitMembershipState.Empty && survivors != 0)
            || (state == OwnedGitMembershipState.Live && survivors == 0))
            throw new ArgumentOutOfRangeException(nameof(survivors));
        State = state;
        Survivors = survivors;
    }

    internal OwnedGitMembershipState State { get; }
    internal int Survivors { get; }
}

/// <summary>No command, environment, stderr or marker values belong in cleanup diagnostics.</summary>
internal sealed record OwnedGitCleanup(bool RootExited, OwnedGitMembers Members, int SignalsAttempted,
    TimeSpan Duration, TimeSpan SignalDuration, TimeSpan RootWaitDuration, TimeSpan ProbeDuration,
    OwnedGitReason Reason)
{
    internal bool Joined => RootExited && Members.State == OwnedGitMembershipState.Empty;
}

/// <summary>One monotonic allowance shared by signaling, root wait, census and polling.</summary>
internal sealed class OwnedGitDeadline(TimeProvider clock, TimeSpan allowance)
{
    private readonly long _started = clock.GetTimestamp();
    internal TimeSpan Elapsed => clock.GetElapsedTime(_started);
    internal TimeSpan Remaining => Elapsed >= allowance ? TimeSpan.Zero : allowance - Elapsed;
}

/// <summary>
/// External process/containment I/O only. Each async operation must honor the supplied independent
/// cleanup token and deadline; it must join its own work before returning. Start must classify a
/// failure as NoChild only with proof that creation did not happen. Dispose closes resources,
/// never signals members. A platform owner retains all resources after an ambiguous launch.
/// </summary>
internal interface IOwnedGitProcessOperations : IDisposable
{
    OwnedGitLaunch Start(ProcessStartInfo start);
    StreamWriter StandardInput { get; }
    StreamReader StandardOutput { get; }
    StreamReader StandardError { get; }
    bool? RootExited { get; }
    int ExitCode { get; }
    Task<bool> WaitForRootExitAsync(CancellationToken cancellationToken);
    ValueTask<OwnedGitMembers> ProbeMembersAsync(OwnedGitRoot? root, OwnedGitDeadline deadline,
        CancellationToken cancellationToken);
    ValueTask<int> TerminateMembersAsync(OwnedGitRoot? root, OwnedGitDeadline deadline,
        CancellationToken cancellationToken);
}

/// <summary>
/// Invocation owner shared by the Git callers. It does not own journal/admission decisions.
/// S1 deliberately requires an explicit platform implementation: no root-only empty verdict and
/// no uncontained fallback. S2/S3 supply the native membership implementations before integration.
/// </summary>
internal sealed class OwnedGitProcess : IDisposable, IAsyncDisposable
{
    internal const string MarkerVariable = "ANTIPHON_REPOSITORY_CHILD";
    private readonly object _gate = new();
    private readonly IOwnedGitProcessOperations _operations;
    private readonly TimeProvider _clock;
    private readonly string _marker = Guid.NewGuid().ToString("N");
    private OwnedGitLaunch? _launch;
    private Task<OwnedGitCleanup>? _termination;
    private bool _disposeRequested;
    private bool _closed;
    private OwnedGitLifecycle _state = OwnedGitLifecycle.Prepared;

    internal OwnedGitProcess(IOwnedGitProcessOperations operations, TimeProvider? clock = null)
    {
        _operations = operations ?? throw new ArgumentNullException(nameof(operations));
        _clock = clock ?? TimeProvider.System;
    }

    internal OwnedGitLifecycle State { get { lock (_gate) return _state; } }
    internal OwnedGitRoot? Root { get { lock (_gate) return _launch?.Root; } }
    internal bool? RootExited
    {
        get
        {
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_disposeRequested, this);
                return _launch?.State switch
                {
                    OwnedGitLaunchState.NoChild => true,
                    OwnedGitLaunchState.Started => _operations.RootExited,
                    _ => null
                };
            }
        }
    }
    internal StreamWriter StandardInput { get { lock (_gate) { RequireStarted(); return _operations.StandardInput; } } }
    internal StreamReader StandardOutput { get { lock (_gate) { RequireStarted(); return _operations.StandardOutput; } } }
    internal StreamReader StandardError { get { lock (_gate) { RequireStarted(); return _operations.StandardError; } } }
    internal int ExitCode { get { lock (_gate) { RequireStarted(); return _operations.ExitCode; } } }

    internal OwnedGitLaunch Start(ProcessStartInfo start)
    {
        ArgumentNullException.ThrowIfNull(start);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposeRequested, this);
            if (_launch is not null) throw new InvalidOperationException("owned_git_already_started");
            // Reject shell/credential/verb launches before creation on every platform. The
            // native Windows implementation adds its own transport validation in S3.
            if (start.UseShellExecute || !string.IsNullOrEmpty(start.Verb)
                || !string.IsNullOrEmpty(start.UserName))
                _launch = new(OwnedGitLaunchState.NoChild, null);
            else
            {
                start.Environment[MarkerVariable] = _marker;
                try
                {
                    _launch = _operations.Start(start);
                    // A claimed successful launch without an identity cannot prove root exit.
                    if (_launch.State == OwnedGitLaunchState.Started && _launch.Root is null)
                        _launch = new(OwnedGitLaunchState.Ambiguous, null);
                    if (_launch.State == OwnedGitLaunchState.NoChild && _launch.Root is not null)
                        _launch = new(OwnedGitLaunchState.Ambiguous, _launch.Root);
                }
                catch (Exception)
                {
                    // Creation may already have happened, including inside CaptureRoot.
                    // Every exception consumes the launch; never permit an untracked retry.
                    _launch = new(OwnedGitLaunchState.Ambiguous, null);
                }
            }
            _state = _launch.State switch
            {
                OwnedGitLaunchState.Started => OwnedGitLifecycle.Started,
                OwnedGitLaunchState.NoChild => OwnedGitLifecycle.Joined,
                _ => OwnedGitLifecycle.Unconfirmed
            };
            return _launch;
        }
    }

    internal Task<bool> WaitForRootExitAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            RequireStarted();
            return _operations.WaitForRootExitAsync(cancellationToken);
        }
    }

    internal ValueTask<OwnedGitMembers> ProbeMembersAsync(OwnedGitDeadline deadline,
        CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposeRequested, this);
            if (_launch is null) throw new InvalidOperationException("owned_git_not_started");
            if (_launch.State == OwnedGitLaunchState.NoChild)
                return ValueTask.FromResult(new OwnedGitMembers(OwnedGitMembershipState.Empty));
            return _operations.ProbeMembersAsync(_launch.Root, deadline, cancellationToken);
        }
    }

    /// <summary>
    /// The first request fixes the deadline. All callers receive the same task. Request
    /// cancellation never cancels cleanup; the independent deadline is its only cancellation.
    /// Callers must await this task before returning a cancellation/failure verdict.
    /// </summary>
    internal Task<OwnedGitCleanup> TerminateAndJoinAsync(TimeSpan allowance,
        CancellationToken cancellationToken = default)
    {
        if (allowance <= TimeSpan.Zero || allowance > TimeSpan.FromSeconds(6))
            throw new ArgumentOutOfRangeException(nameof(allowance));
        TaskCompletionSource<OwnedGitCleanup> completion;
        lock (_gate)
        {
            if (_termination is not null) return _termination;
            ObjectDisposedException.ThrowIf(_disposeRequested, this);
            if (_launch is null) throw new InvalidOperationException("owned_git_not_started");
            completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _termination = completion.Task;
            _state = OwnedGitLifecycle.Terminating;
        }
        _ = CompleteTerminationAsync(completion, allowance);
        return completion.Task;
    }

    private async Task CompleteTerminationAsync(TaskCompletionSource<OwnedGitCleanup> completion, TimeSpan allowance)
    {
        try
        {
            var result = await CleanupAsync(allowance).ConfigureAwait(false);
            lock (_gate)
            {
                if (!_disposeRequested) _state = result.Joined ? OwnedGitLifecycle.Joined : OwnedGitLifecycle.Unconfirmed;
                else Close();
                // Publish completion while holding the same gate as Dispose. Otherwise a
                // disposer can observe a pending task after the last close check and leak it.
                completion.TrySetResult(result);
            }
        }
        catch (Exception ex)
        {
            lock (_gate)
            {
                if (!_disposeRequested) _state = OwnedGitLifecycle.Unconfirmed;
                else Close();
                completion.TrySetException(ex);
            }
        }
    }

    private async Task<OwnedGitCleanup> CleanupAsync(TimeSpan allowance)
    {
        var deadline = new OwnedGitDeadline(_clock, allowance);
        var members = new OwnedGitMembers(OwnedGitMembershipState.Unknown);
        var exited = false;
        var signals = 0;
        var signalDuration = TimeSpan.Zero;
        var rootDuration = TimeSpan.Zero;
        var probeDuration = TimeSpan.Zero;
        var reason = OwnedGitReason.Deadline;
        if (_launch!.State == OwnedGitLaunchState.NoChild)
            return new(true, new(OwnedGitMembershipState.Empty), 0, deadline.Elapsed,
                signalDuration, rootDuration, probeDuration, OwnedGitReason.NoChild);
        using var cleanupCancellation = new CancellationTokenSource(allowance, _clock);
        try
        {
            while (deadline.Remaining > TimeSpan.Zero)
            {
                var phase = _clock.GetTimestamp();
                try
                {
                    signals += await _operations.TerminateMembersAsync(_launch.Root, deadline,
                        cleanupCancellation.Token).ConfigureAwait(false);
                }
                finally { signalDuration += _clock.GetElapsedTime(phase); }
                cleanupCancellation.Token.ThrowIfCancellationRequested();
                if (deadline.Remaining == TimeSpan.Zero) break;

                phase = _clock.GetTimestamp();
                try
                {
                    // No root identity after ambiguous creation is not proof of root exit.
                    exited = _launch.Root is not null
                        && await _operations.WaitForRootExitAsync(cleanupCancellation.Token).ConfigureAwait(false);
                }
                finally { rootDuration += _clock.GetElapsedTime(phase); }
                cleanupCancellation.Token.ThrowIfCancellationRequested();
                if (deadline.Remaining == TimeSpan.Zero) break;

                phase = _clock.GetTimestamp();
                try
                {
                    members = await _operations.ProbeMembersAsync(_launch.Root, deadline,
                        cleanupCancellation.Token).ConfigureAwait(false);
                }
                finally { probeDuration += _clock.GetElapsedTime(phase); }
                cleanupCancellation.Token.ThrowIfCancellationRequested();
                if (deadline.Remaining == TimeSpan.Zero) break;
                if (exited && members.State == OwnedGitMembershipState.Empty)
                {
                    reason = OwnedGitReason.None;
                    return Result();
                }
                await Task.Delay(TimeSpan.FromMilliseconds(Math.Min(25, deadline.Remaining.TotalMilliseconds)),
                    _clock, cleanupCancellation.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cleanupCancellation.IsCancellationRequested) { }
        catch (Exception ex) when (IsObservationFailure(ex))
        {
            members = new(OwnedGitMembershipState.Unknown);
            reason = OwnedGitReason.ObservationFailed;
        }
        // A scan that completed after the deadline cannot authorize admission.
        if (reason == OwnedGitReason.Deadline) members = new(OwnedGitMembershipState.Unknown, members.Survivors);
        return Result();

        OwnedGitCleanup Result() => new(exited, members, signals, deadline.Elapsed,
            signalDuration, rootDuration, probeDuration, reason);
    }

    private static bool IsObservationFailure(Exception ex) => ex is IOException or UnauthorizedAccessException
        or Win32Exception or InvalidOperationException;

    private void RequireStarted()
    {
        ObjectDisposedException.ThrowIf(_disposeRequested, this);
        if (_launch?.State != OwnedGitLaunchState.Started)
            throw new InvalidOperationException("owned_git_root_unavailable");
    }

    /// <summary>Close handles/streams only. A crash/dispose must preserve children and durable fencing.</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposeRequested) return;
            _disposeRequested = true;
            _state = OwnedGitLifecycle.Disposed;
            if (_termination is null || _termination.IsCompleted) Close();
        }
    }

    public async ValueTask DisposeAsync()
    {
        Dispose();
        Task<OwnedGitCleanup>? termination;
        lock (_gate) termination = _termination;
        if (termination is not null) await termination.ConfigureAwait(false);
    }

    private void Close()
    {
        if (_closed) return;
        _closed = true;
        _operations.Dispose();
    }
}

/// <summary>
/// Membership I/O supplied by S2's bounded Linux census. Signal only freshly revalidated exact
/// marker/start identities; unreadable, truncated or incomplete scans return Unknown. Signal
/// also retains the constrained ancestry backstop. This port cannot be replaced by root exit.
/// </summary>
internal interface ILinuxOwnedGitMembers : IDisposable
{
    OwnedGitRoot CaptureRoot(int pid, DateTime? startUtc);
    ValueTask<OwnedGitMembers> ProbeAsync(OwnedGitRoot? root, OwnedGitDeadline deadline,
        CancellationToken cancellationToken);
    ValueTask<int> SignalAsync(OwnedGitRoot? root, OwnedGitDeadline deadline,
        CancellationToken cancellationToken);
    void SetMarker(string marker);
}

