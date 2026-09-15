using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;

namespace Antiphon.Server.Infrastructure.Git;

public sealed class WindowsWorktreeDeleteAccessProbe(WorktreeNativeIO io, TimeProvider clock) : IWorktreeDeleteAccessProbe
{
    public Task<WorktreeNativeSnapshot> ObserveAsync(WorktreeProbeTarget target,
        IReadOnlyList<WorktreeLockOwner> owners, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!io.Supported) return Task.FromResult(new WorktreeNativeSnapshot(WorktreeLockStatus.Unavailable, "UnsupportedPlatform", []));
        var observations = new List<WorktreeNativeObservation>();
        var start = clock.GetTimestamp();
        var candidates = 0;
        var partial = false;
        var reason = "Observed";
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(target.Root));
        bool Expired() => clock.GetElapsedTime(start) >= TimeSpan.FromSeconds(2);
        void CheckBudget()
        {
            ct.ThrowIfCancellationRequested();
            if (Expired()) throw new TimeoutException("ProbeBudgetExpired");
        }
        string? Identity(string path)
        {
            CheckBudget();
            var identity = io.Identity(path, CheckBudget);
            CheckBudget();
            return identity;
        }
        bool Allowed(string path) => WorktreeNativeIO.Within(path, root)
            && !WorktreeNativeIO.Within(path, target.CommonDirectory) && !WorktreeNativeIO.Within(path, target.GitDirectory)
            && !Path.GetRelativePath(root, path).Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Any(p => p.Equals(".git", StringComparison.OrdinalIgnoreCase));
        WorktreeNativeSnapshot Result() => new(partial ? WorktreeLockStatus.Partial : WorktreeLockStatus.OwnersObserved,
            reason, observations, candidates);
        try
        {
            CheckBudget();
            if (!io.Exists(root)) return Task.FromResult(new WorktreeNativeSnapshot(WorktreeLockStatus.PathGone, "PathGone", []));
            var rootIdentity = Identity(root);
            if (rootIdentity is null || !Allowed(root))
                return Task.FromResult(new WorktreeNativeSnapshot(WorktreeLockStatus.Unavailable, "RootIdentityUnverifiable", []));
            void Probe(string path)
            {
                ct.ThrowIfCancellationRequested();
                if (Expired()) { partial = true; reason = "ProbeBudgetExpired"; return; }
                if (!Allowed(path) || Identity(root) != rootIdentity)
                { partial = true; reason = "PathIdentityUnverifiable"; return; }
                var before = Identity(path);
                if (before is null) { partial = true; reason = "PathIdentityUnverifiable"; return; }
                CheckBudget();
                using var handle = io.Open(path, 0x00010000, 7, 3, 0x02200000, false, CheckBudget);
                var error = handle.Error;
                var valid = handle.Succeeded ? WorktreeNativeIO.Same(handle.FinalPath, path) && !handle.ReparsePoint
                    : before == Identity(path) && rootIdentity == Identity(root);
                observations.Add(new("DeleteAccessOpen", Path.GetRelativePath(root, path), clock.GetUtcNow().UtcDateTime,
                    handle.Succeeded && valid, error, valid));
                if (!valid) { partial = true; reason = "PathIdentityUnverifiable"; }
                ct.ThrowIfCancellationRequested();
            }
            Probe(root);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { root };
            foreach (var owner in owners)
            {
                ct.ThrowIfCancellationRequested();
                if (Expired() || candidates >= 64) { partial = true; reason = "ProbeLimit"; break; }
                candidates++;
                if (Path.IsPathRooted(owner.RelativePath)) { partial = true; continue; }
                var path = Path.GetFullPath(Path.Combine(root, owner.RelativePath));
                if (seen.Add(path)) Probe(path);
            }
            var pending = new Stack<string>(); pending.Push(root);
            while (pending.Count > 0 && candidates < 64 && !Expired())
            {
                ct.ThrowIfCancellationRequested();
                var directory = pending.Pop();
                if (Identity(root) != rootIdentity || !Allowed(directory))
                { partial = true; reason = "PathIdentityUnverifiable"; continue; }
                CheckBudget();
                if ((io.Attributes(directory) & FileAttributes.ReparsePoint) != 0)
                { partial = true; reason = "PathIdentityUnverifiable"; continue; }
                CheckBudget();
                using var entries = io.Entries(directory);
                while (candidates < 64 && !Expired())
                {
                    CheckBudget();
                    if (!entries.MoveNext()) break;
                    candidates++;
                    var path = entries.Current;
                    if (!Allowed(path)) continue;
                    CheckBudget();
                    var attributes = io.Attributes(path);
                    if ((attributes & FileAttributes.ReparsePoint) != 0) { partial = true; reason = "ReparsePointExcluded"; continue; }
                    if (seen.Add(path)) Probe(path);
                    if ((attributes & FileAttributes.Directory) != 0) pending.Push(path);
                }
            }
            if (candidates >= 64 || Expired()) { partial = true; reason = "ProbeLimit"; }
        }
        catch (TimeoutException) { partial = true; reason = "ProbeBudgetExpired"; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        { partial = true; reason = "ProbeAccessLimited"; }
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(Result());
    }
}
