using System.Diagnostics;
using System.Globalization;
using System.Text;
using Antiphon.Server.Application.Dtos;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// Pure classifier for <c>.git/index.lock</c>. I/O lives on <see cref="Interfaces.ILandingGit.InspectIndexLockAsync"/>.
/// No code path here deletes an index.lock. A timeout kill of a GitWorkspaceService child
/// can race a foreign lock in the pre-lock window, so reclaim was removed (CARD-0543 Review).
/// </summary>
public static class GitIndexLock
{
    public const string StaleCode = "git_index_lock_stale";
    public const string HeldCode = "git_index_lock_held";
    public const string PathErrorReason = "index_lock_path_error";
    public const int DefaultStaleAfterSeconds = 300;
    public const int MinimumStaleAfterSeconds = 30;
    public static readonly TimeSpan CensusSkew = TimeSpan.FromSeconds(2);

    public enum Kind { None, Held, Stale }

    public sealed record Observation(
        string Path,
        bool Present,
        DateTime? LastWriteUtc,
        long? Length,
        IReadOnlyList<(int Pid, DateTime? StartUtc)> CandidateHolders);

    public static TimeSpan StaleAfter(int? configuredSeconds)
    {
        var seconds = configuredSeconds ?? DefaultStaleAfterSeconds;
        return TimeSpan.FromSeconds(Math.Max(MinimumStaleAfterSeconds, seconds));
    }

    public static Observation Observe(
        string path,
        DateTime nowUtc,
        IReadOnlyList<(int Pid, DateTime? StartUtc)> census)
    {
        _ = nowUtc;
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists)
                return new(path, false, null, null, census);
            info.Refresh();
            return new(path, true, info.LastWriteTimeUtc, info.Length, census);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return new(path, false, null, null, census);
        }
    }

    public static Kind Classify(Observation observation, TimeSpan staleAfter, DateTime nowUtc)
    {
        if (!observation.Present || observation.LastWriteUtc is null)
            return observation.Present ? Kind.Held : Kind.None;
        var age = nowUtc - observation.LastWriteUtc.Value;
        if (age < TimeSpan.Zero) age = TimeSpan.Zero;
        if (age < staleAfter)
            return Kind.Held;
        return HasHolder(observation) ? Kind.Held : Kind.Stale;
    }

    public static (Kind Kind, string? Detail) Evaluate(
        LandingIndexLockObservation observation,
        TimeSpan staleAfter,
        DateTime nowUtc)
    {
        if (!string.IsNullOrEmpty(observation.Reason))
            return (Kind.Held, FormatProbeFailure(observation));
        var observed = new Observation(
            observation.Path,
            observation.Present,
            observation.LastWriteUtc,
            observation.Length,
            observation.CandidateHolders);
        var kind = Classify(observed, staleAfter, nowUtc);
        if (kind == Kind.None)
            return (Kind.None, null);
        return (kind, FormatDetail(kind, observed, staleAfter, nowUtc));
    }

    public static (string Code, string Detail)? Refusal(
        LandingIndexLockObservation observation,
        TimeSpan staleAfter,
        DateTime nowUtc)
    {
        var (kind, detail) = Evaluate(observation, staleAfter, nowUtc);
        return kind switch
        {
            Kind.Stale => (StaleCode, detail ?? ""),
            Kind.Held => (HeldCode, detail ?? ""),
            _ => null,
        };
    }

    public static string FormatDetail(Kind kind, Observation observation, TimeSpan staleAfter, DateTime nowUtc)
    {
        var age = observation.LastWriteUtc is { } write
            ? FormatAge(nowUtc - write)
            : "unknown";
        var bytes = observation.Length?.ToString(CultureInfo.InvariantCulture) ?? "unknown";
        var liveness = kind == Kind.Stale
            ? "No git process older than the lock is running; it is an orphan from an interrupted git write"
            : FormatHeldLiveness(observation, staleAfter);
        var path = observation.Path;
        return $"Git index lock present at {path} (age {age}, {bytes} bytes). {liveness}. "
            + $"Remove it and the land resumes on the next sweep: Remove-Item '{path}'. "
            + "Never remove a lock while a git process older than it is running.";
    }

    public static IReadOnlyList<(int Pid, DateTime? StartUtc)> CensusGitProcesses()
    {
        Process[] processes;
        try { processes = Process.GetProcessesByName("git"); }
        catch { return []; }
        var list = new List<(int Pid, DateTime? StartUtc)>(processes.Length);
        foreach (var process in processes)
        {
            try
            {
                DateTime? start = null;
                try { start = process.StartTime.ToUniversalTime(); }
                catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception
                    or UnauthorizedAccessException)
                {
                    start = null;
                }
                list.Add((process.Id, start));
            }
            finally
            {
                process.Dispose();
            }
        }
        return list;
    }

    public static bool PathsEqual(string? left, string? right)
    {
        if (left is null || right is null) return left == right;
        return string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }

    private static bool HasHolder(Observation observation)
    {
        if (observation.LastWriteUtc is null)
            return observation.CandidateHolders.Count > 0;
        var cutoff = observation.LastWriteUtc.Value + CensusSkew;
        foreach (var (_, start) in observation.CandidateHolders)
        {
            if (start is null || start.Value <= cutoff)
                return true;
        }
        return false;
    }

    private static string FormatHeldLiveness(Observation observation, TimeSpan staleAfter)
    {
        (int Pid, DateTime? StartUtc)? holder = null;
        var cutoff = (observation.LastWriteUtc ?? DateTime.MinValue) + CensusSkew;
        foreach (var candidate in observation.CandidateHolders)
        {
            if (candidate.StartUtc is null || candidate.StartUtc.Value <= cutoff)
            {
                holder = candidate;
                break;
            }
        }
        if (holder is { } live)
        {
            var started = live.StartUtc is { } utc
                ? utc.ToString("o", CultureInfo.InvariantCulture)
                : "unreadable";
            return $"A git process started before the lock is still running (PID {live.Pid}, started {started})";
        }
        return $"The lock is younger than {(int)staleAfter.TotalSeconds} s";
    }

    private static string FormatAge(TimeSpan age)
    {
        if (age < TimeSpan.Zero) age = TimeSpan.Zero;
        return $"{(int)age.TotalHours}h {age.Minutes}m {age.Seconds}s";
    }

    private static string FormatProbeFailure(LandingIndexLockObservation observation)
    {
        var builder = new StringBuilder("Git index lock probe failed (")
            .Append(observation.Reason)
            .Append(')');
        if (!string.IsNullOrEmpty(observation.Path))
            builder.Append(" at ").Append(observation.Path);
        builder.Append(". Treating as held. Never remove a lock while a git process older than it is running.");
        if (!string.IsNullOrEmpty(observation.Path))
            builder.Append(" Remove-Item '").Append(observation.Path).Append("'.");
        return builder.ToString();
    }
}
