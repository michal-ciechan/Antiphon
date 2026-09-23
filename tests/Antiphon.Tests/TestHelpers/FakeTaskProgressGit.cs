using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Domain;
using Antiphon.Server.Infrastructure.Git;

namespace Antiphon.Tests.TestHelpers;

/// <summary>In-memory commit graph for CARD-0499 Unit policy tests. Not git behaviour.</summary>
internal sealed class FakeTaskProgressGit : ITaskProgressGit
{
    public Dictionary<string, string> LocalRefs { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, string> RemoteRefs { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, HashSet<string>> Parents { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> SymbolicHeads { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// CARD-0613. Paths whose HEAD is DETACHED. A path here has no symbolic ref, which is a valid
    /// alternate candidate, distinct from a failed read.
    /// </summary>
    public HashSet<string> DetachedHeads { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// CARD-0613. What each checkout's HEAD resolves to, keyed by path. Falls back to
    /// <c>LocalRefs["HEAD"]</c>, so existing worlds keep their single-checkout behaviour.
    /// </summary>
    public Dictionary<string, string> HeadsByPath { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// CARD-0613. Explicit committer times per commit. A commit with NO entry here is
    /// UNAVAILABLE, never 'now' - an absent fake clock must not manufacture a positive.
    /// </summary>
    public Dictionary<string, DateTime> CommitTimes { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>CARD-0613. Commits whose metadata read fails outright (nonzero exit / malformed).</summary>
    public HashSet<string> CommitTimeFaults { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>CARD-0613. Per-path git common directory; falls back to <see cref="CommonDirectory"/>.</summary>
    public Dictionary<string, string> CommonDirectoriesByPath { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>CARD-0613. Runs once after the Nth HEAD observation, to move the checkout mid-evaluation.</summary>
    public Action<FakeTaskProgressGit, int>? OnHeadObserved { get; set; }
    public int HeadObservations { get; private set; }
    public List<string[]> Trace { get; } = [];
    public List<string> Fetches { get; } = [];
    public string? EndpointFingerprint { get; set; } = new string('a', 64);
    public bool OriginConfigured { get; set; } = true;
    public string CommonDirectory { get; set; } = @"C:\repo";
    public string CanonicalRepository { get; set; } = @"C:\repo";
    public Func<string, IReadOnlyList<string>, Exception?>? Inject { get; set; }
    public Func<string, IReadOnlyList<string>, LandingGitResult?>? BeforeCommand { get; set; }
    public int ObserveAttemptsUntilStable { get; set; }
    public int ObserveCalls { get; private set; }

    public void AddCommit(string sha, params string[] parents)
    {
        Parents[sha] = new HashSet<string>(parents, StringComparer.OrdinalIgnoreCase);
        foreach (var p in parents)
            Parents.TryAdd(p, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
    }

    public Task<LandingGitResult> RunAsync(string repository, IReadOnlyList<string> arguments, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        Trace.Add(arguments.ToArray());
        ThrowIfInjected(arguments[0], arguments);
        if (BeforeCommand?.Invoke(repository, arguments) is { } injected)
            return Task.FromResult(injected);
        return Task.FromResult(new LandingGitResult(0, "", ""));
    }

    public Task<string> CanonicalDirectoryAsync(string path, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(Path.GetFullPath(path));
    }

    public Task<string> CommonDirectoryAsync(string repository, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        ThrowIfInjected("rev-parse", ["rev-parse", "--git-common-dir"]);
        return Task.FromResult(
            CommonDirectoriesByPath.TryGetValue(repository, out var scoped) ? scoped : CommonDirectory);
    }

    public Task<ProgressCommitTime> CommitTimeAsync(string repository, string sha, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        Trace.Add(["show", "-s", "--format=%ct", sha, "--"]);
        ThrowIfInjected("show", ["show", "-s", "--format=%ct", sha]);
        if (!GitObjectId.IsFull(sha))
            return Task.FromResult(new ProgressCommitTime(false, null, "commit_time_invalid_object"));
        if (CommitTimeFaults.Contains(sha))
            return Task.FromResult(new ProgressCommitTime(false, null, "commit_time_unavailable"));
        if (!CommitTimes.TryGetValue(sha, out var when))
            return Task.FromResult(new ProgressCommitTime(false, null, "commit_time_unavailable"));
        return Task.FromResult(new ProgressCommitTime(true, DateTime.SpecifyKind(when, DateTimeKind.Utc), null));
    }

    public Task<IReadOnlyList<LandingRegistration>> RegistrationsAsync(string repository, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<LandingRegistration>>([]);

    public Task<ProgressRevParse> RevParseCommitAsync(string repository, string revision, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        Trace.Add(["rev-parse", "--verify", revision]);
        ThrowIfInjected("rev-parse", ["rev-parse", "--verify", revision]);
        var key = revision.Replace("^{commit}", "", StringComparison.Ordinal);
        if (key is "HEAD")
        {
            // Resolve FIRST, then let the hook move the checkout: the point is that this answer
            // was true when read and stale by the time it is used.
            HeadsByPath.TryGetValue(repository, out var scopedHead);
            HeadObservations++;
            OnHeadObserved?.Invoke(this, HeadObservations);
            if (scopedHead is not null)
                return Task.FromResult(new ProgressRevParse(true, scopedHead, null));
        }
        if (LocalRefs.TryGetValue(key, out var sha))
            return Task.FromResult(new ProgressRevParse(true, sha, null));
        if (key is "HEAD" && LocalRefs.TryGetValue("HEAD", out sha))
            return Task.FromResult(new ProgressRevParse(true, sha, null));
        if (GitObjectId.IsFull(key) && Parents.ContainsKey(key))
            return Task.FromResult(new ProgressRevParse(true, key, null));
        return Task.FromResult(new ProgressRevParse(false, null, "repair_source_identity_unavailable"));
    }

    public Task<ProgressSymbolicHead> SymbolicHeadAsync(string repository, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        Trace.Add(["symbolic-ref", "-q", "HEAD"]);
        ThrowIfInjected("symbolic-ref", ["symbolic-ref"]);
        if (DetachedHeads.Contains(repository))
            return Task.FromResult(new ProgressSymbolicHead(true, null, "detached_head"));
        if (SymbolicHeads.TryGetValue(repository, out var full) || SymbolicHeads.TryGetValue("HEAD", out full))
            return Task.FromResult(new ProgressSymbolicHead(true, full, null));
        return Task.FromResult(new ProgressSymbolicHead(true, LocalRefs.Keys.FirstOrDefault(k => k.StartsWith("refs/heads/")), null));
    }

    public Task<string?> EndpointFingerprintAsync(string repository, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(OriginConfigured ? EndpointFingerprint : null);
    }

    public Task<bool> HasOriginAsync(string repository, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(OriginConfigured);
    }

    public Task<ProgressRemoteObservation> ObserveExactRefAsync(
        string repository, string fullRef, string? expectedFingerprint, Guid taskId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        Trace.Add(["ls-remote", "--refs", "--exit-code", "endpoint", fullRef]);
        ThrowIfInjected("ls-remote", ["ls-remote", fullRef]);
        ObserveCalls++;
        if (!OriginConfigured)
            return Task.FromResult(new ProgressRemoteObservation(ProgressRemoteState.NotConfigured));
        if (expectedFingerprint is { Length: > 0 }
            && EndpointFingerprint is not null
            && !string.Equals(expectedFingerprint, EndpointFingerprint, StringComparison.OrdinalIgnoreCase))
            return Task.FromResult(new ProgressRemoteObservation(ProgressRemoteState.Unavailable, EndpointFingerprint: EndpointFingerprint, Reason: "source_remote_endpoint_changed"));
        if (BeforeCommand?.Invoke(repository, ["ls-remote", "--refs", "--exit-code", "endpoint", fullRef]) is { } injected)
        {
            if (injected.ExitCode == 2)
                return Task.FromResult(new ProgressRemoteObservation(ProgressRemoteState.Missing, EndpointFingerprint: EndpointFingerprint));
            if (!injected.Succeeded)
                return Task.FromResult(new ProgressRemoteObservation(ProgressRemoteState.Unavailable, EndpointFingerprint: EndpointFingerprint, Reason: "source_remote_unreadable"));
        }
        if (!RemoteRefs.TryGetValue(fullRef, out var sha))
            return Task.FromResult(new ProgressRemoteObservation(ProgressRemoteState.Missing, EndpointFingerprint: EndpointFingerprint));
        if (ObserveAttemptsUntilStable > 0 && ObserveCalls < ObserveAttemptsUntilStable)
            return Task.FromResult(new ProgressRemoteObservation(ProgressRemoteState.Unavailable, Reason: "changed_during_confirmation"));
        if (!LocalRefs.ContainsValue(sha))
        {
            var pin = $"{TaskProgressGit.ProgressRefPrefix}{taskId:N}/observe";
            Fetches.Add(pin);
            Trace.Add(["fetch", "--no-tags", "--no-write-fetch-head", "endpoint", $"{fullRef}:{pin}"]);
        }
        return Task.FromResult(new ProgressRemoteObservation(ProgressRemoteState.Present, sha, EndpointFingerprint));
    }

    /// <summary>
    /// CARD-0613 G-23. Ancestor/descendant pairs whose <c>merge-base --is-ancestor</c> cannot
    /// answer, keyed <c>"&lt;ancestor&gt;:&lt;descendant&gt;"</c>. An UNKNOWN read is distinct
    /// from <c>false</c>: it is the incomplete observation D-8 forbids turning into a negative.
    /// </summary>
    public HashSet<string> AncestryUnknown { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Task<bool?> IsAncestorAsync(string repository, string ancestorSha, string descendantSha, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        Trace.Add(["merge-base", "--is-ancestor", ancestorSha, descendantSha]);
        ThrowIfInjected("merge-base", ["merge-base", "--is-ancestor", ancestorSha, descendantSha]);
        if (AncestryUnknown.Contains($"{ancestorSha}:{descendantSha}"))
            return Task.FromResult<bool?>(null);
        if (string.Equals(ancestorSha, descendantSha, StringComparison.OrdinalIgnoreCase))
            return Task.FromResult<bool?>(true);
        return Task.FromResult<bool?>(Reachable(descendantSha, ancestorSha));
    }

    public Task<ProgressPinResult> PinBaselineAsync(string repository, Guid taskId, string name, string sha, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        Trace.Add(["update-ref", $"{TaskProgressGit.ProgressRefPrefix}{taskId:N}/baseline-{name}", sha]);
        return Task.FromResult(new ProgressPinResult(true, $"{TaskProgressGit.ProgressRefPrefix}{taskId:N}/baseline-{name}"));
    }

    public Task<IReadOnlyList<string>> ListProgressPinsAsync(string repository, Guid taskId, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<string>>(
            Trace.Where(a => a.Length > 1 && a[0] == "update-ref").Select(a => a[1]).Distinct().ToArray());

    public bool Log50Called => Trace.Any(a => a.Length > 1 && a[0] == "log" && a[1].StartsWith("-50", StringComparison.Ordinal));

    private bool Reachable(string from, string ancestor)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var stack = new Stack<string>();
        stack.Push(from);
        while (stack.Count > 0)
        {
            var cur = stack.Pop();
            if (!seen.Add(cur)) continue;
            if (string.Equals(cur, ancestor, StringComparison.OrdinalIgnoreCase)) return true;
            if (Parents.TryGetValue(cur, out var ps))
                foreach (var p in ps) stack.Push(p);
        }
        return false;
    }

    private void ThrowIfInjected(string command, IReadOnlyList<string> arguments)
    {
        if (Inject?.Invoke(command, arguments) is { } ex) throw ex;
    }
}

internal sealed class StubWorkspaceProgressProbe(WorkspaceProgressArm arm) : IWorkspaceProgressProbe
{
    public Task<WorkspaceProgressArm> ProbeProgressAsync(
        string? workingDirectory, DateTime since, bool sharedCheckout, CancellationToken ct)
        => Task.FromResult(arm);
}
