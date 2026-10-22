using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace Antiphon.Checkpoints;

// Builds have a clean tracked baseline. Settlement detects changes and never
// writes to the worktree or index. Output redirection reduces accidental writes;
// arbitrary MSBuild targets remain capable of changing source and are reported.
public static class BuildConfinement
{
    public const int WalkCap = 500;
    private static readonly HashSet<string> IsolationKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "OutputPath", "OutDir", "BaseOutputPath", "BaseIntermediateOutputPath",
        "MSBuildProjectExtensionsPath", "ArtifactsPath", "UseArtifactsOutput",
    };

    // Covered holds the ledger stamps taken before the build slot; those paths are checked at admission
    // and after release, so Settle does not re-stamp them or report them as new.
    public sealed record Baseline(
        string Head,
        string Repository,
        Dictionary<string, string> Entries,
        Dictionary<string, string> Untracked,
        Dictionary<string, string> Content,
        IReadOnlyDictionary<string, string>? Covered = null);

    // CARD-1241: tracked, untracked and ignored files outside the row's write roots, stamped before
    // the build slot is taken, re-checked by AdmitLedger once it is granted and verified by VerifyLedger
    // after it is released. Stats holds each entry's LedgerStat, read before its hash; RacyFrom is the
    // capture start minus RacyWindow, in LedgerStat.Now() units. Stat reads the stamps at capture and at admission;
    // Stamp reads an entry's content stamp at capture, admission and verification (CARD-1358 seam).
    public sealed record Ledger(
        string Repository,
        string Head,
        IReadOnlyList<string> Roots,
        IReadOnlySet<string> Tracked,
        Dictionary<string, string> Files,
        Dictionary<string, LedgerStat> Stats,
        long RacyFrom,
        Func<string, LedgerStat> Stat,
        Func<string, string> Stamp);

    public sealed record Admission(Settlement Settlement, int Hashed);

    // A second write inside one file-system timestamp tick can leave the stat stamp equal, so an entry
    // whose mtime or ctime is this close to the capture is hashed at admission whatever its stat says.
    public const long RacyWindow = 2_000_000_000;

    public sealed record LedgerCapture(Ledger? Value, string? Refusal, string Detail)
    {
        public static LedgerCapture None { get; } = new(null, null, "");
    }

    public sealed record Settlement(string? Kind, string Detail, IReadOnlyList<string> Paths);

    public readonly record struct OutputLayout(string Owned, string Artifacts);

    public sealed record Prepared(
        string? Refusal,
        string Detail,
        IReadOnlyList<string> Arguments,
        Baseline? Baseline);

    public static string? ReportedRefusal(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return null;
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.TrimEnd('\r').Trim();
            if (line.StartsWith("refused:", StringComparison.Ordinal))
                return line;
        }

        return null;
    }

    public static OutputLayout LayoutFor(string workingDirectory, string project, string relativeOutput)
    {
        var projectFull = Path.GetFullPath(Path.IsPathRooted(project) ? project : Path.Combine(workingDirectory, project));
        var projectDir = ProjectWriteRoots.DirectoryOf(projectFull);
        if (string.IsNullOrEmpty(projectDir))
            projectDir = string.IsNullOrWhiteSpace(workingDirectory) ? Environment.CurrentDirectory : workingDirectory;
        var owned = AbsoluteOutput(projectDir, relativeOutput);
        return new OutputLayout(owned, owned.TrimEnd('/') + "/artifacts");
    }

    public static LedgerCapture CaptureLedger(string workingDirectory, IReadOnlyList<string> arguments, Func<string, LedgerStat>? stat = null,
        Func<string, string>? stamp = null)
    {
        stat ??= LedgerStat.Of;
        stamp ??= Stamp;
        if (!TryFindOutput(arguments, out var outputValue) || !TryFindProject(arguments, out var project))
            return LedgerCapture.None;
        var repo = Git(workingDirectory, ["rev-parse", "--show-toplevel"]);
        if (repo.Error is not null || repo.Exit != 0 || string.IsNullOrWhiteSpace(repo.Stdout))
            return RefuseLedger(repo.Error ?? "git-toplevel");
        var repository = repo.Stdout.Trim();
        var head = Git(repository, ["rev-parse", "HEAD"]);
        if (head.Error is not null || head.Exit != 0 || string.IsNullOrWhiteSpace(head.Stdout))
            return RefuseLedger(head.Error ?? "git-head");
        var racyFrom = LedgerStat.Now() - RacyWindow;
        var listed = Git(repository, ["ls-files", "-z", "-s"]);
        if (listed.Error is not null || listed.Exit != 0)
            return RefuseLedger(listed.Error ?? "git-ls-files");
        var entries = ParseIndex(listed.Stdout);
        if (entries.Error is not null || entries.Value is null)
            return RefuseLedger(entries.Error ?? "index-record");
        var untracked = ListUntracked(repository);
        if (untracked.Error is not null || untracked.Value is null)
            return RefuseLedger(untracked.Error ?? "git-ls-files");

        var roots = WriteRoots(repository, workingDirectory, project, outputValue);
        var files = new Dictionary<string, string>(StringComparer.Ordinal);
        var stats = new Dictionary<string, LedgerStat>(StringComparer.Ordinal);
        foreach (var path in entries.Value.Keys)
        {
            if (UnderRoots(path, roots)) continue;
            stats[path] = stat(Combine(repository, path));
            var value = stamp(Combine(repository, path));
            if (IsUnreadable(value)) return RefuseLedger("tracked-unreadable\n" + UnreadableLine(path, ReasonOf(value)));
            files[path] = value;
        }

        foreach (var path in untracked.Value)
        {
            if (UnderRoots(path, roots)) continue;
            stats[path] = stat(Combine(repository, path));
            files[path] = stamp(Combine(repository, path));
        }
        var tracked = new HashSet<string>(entries.Value.Keys, StringComparer.Ordinal);
        return new LedgerCapture(new Ledger(repository, head.Stdout.Trim(), roots, tracked, files, stats, racyFrom, stat, stamp), null, "");
    }

    // Review 06ff09f8 D1: the scheduler calls this once the build slot is granted, before the build starts.
    // Default (stat) mode skips an entry's hash when its recorded stamp has a file identity and a change time
    // (stat_without_a_change_time_hashes_each_entry_at_admission), its mtime and ctime are older than RacyWindow
    // (entry_modified_just_before_the_ledger_is_hashed_when_the_slot_is_granted,
    // entry_with_a_change_time_inside_the_racy_window_is_hashed_at_admission) and the stamp is unchanged
    // (unchanged_ledger_entries_are_not_hashed_when_the_slot_is_granted). The portable stamp has neither identity nor
    // change time, so it is hashed (entry_modified_just_before_the_ledger_is_hashed_when_the_slot_is_granted).
    // A change that moves the stamp is refused before launch
    // (file_changed_during_the_slot_wait_and_restored_by_the_build_is_refused_before_launch,
    // rewrite_keeping_size_and_mtime_on_the_real_file_system_is_refused_before_launch).
    // Limit (Review 9f1c8a2e D1): in default mode a non-output file changed during the slot wait through a shared
    // memory mapping, or by any writer that does not update the ctime, is not detected here; if the build then writes
    // the old bytes back, VerifyLedger after release sees no difference. VerifyLedger reports a difference still
    // present after release. Strict mode hashes each entry whatever its stat, the in-slot cost the stat rule avoids
    // (shared_mapping_style_change_with_an_unchanged_stat_is_refused_at_admission_only_in_strict_mode,
    // strict_admission_hash_in_the_manifest_refuses_the_build_before_launch).
    // CARD-1358 D-3: an entry whose recorded stamp is unreadable is unprovable whatever its stat says, so it is
    // reported without a hash; the others compare as before.
    public static Admission AdmitLedger(Ledger ledger, bool strict = false)
    {
        var changed = new List<string>();
        var unreadable = new Dictionary<string, string>(StringComparer.Ordinal);
        var hashed = 0;
        foreach (var pair in ledger.Files)
        {
            if (IsUnreadable(pair.Value))
            {
                unreadable[pair.Key] = ReasonOf(pair.Value);
                continue;
            }
            var full = Combine(ledger.Repository, pair.Key);
            if (!strict && ledger.Stats.TryGetValue(pair.Key, out var recorded) && recorded.HasIdentityAndChangeTime
                && recorded.Modified < ledger.RacyFrom && recorded.Changed < ledger.RacyFrom
                && recorded.Matches(ledger.Stat(full)))
                continue;
            hashed++;
            Compare(pair.Key, pair.Value, ledger.Stamp(full), changed, unreadable);
        }
        return new Admission(Classify(ledger.Repository, ledger.Head, changed, unreadable, ledger.Tracked.Contains, _ => false), hashed);
    }

    public static Settlement VerifyLedger(Ledger ledger)
    {
        var changed = new List<string>();
        var unreadable = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in ledger.Files)
        {
            if (IsUnreadable(pair.Value))
                unreadable[pair.Key] = ReasonOf(pair.Value);
            else
                Compare(pair.Key, pair.Value, ledger.Stamp(Combine(ledger.Repository, pair.Key)), changed, unreadable);
        }
        return Classify(ledger.Repository, ledger.Head, changed, unreadable, ledger.Tracked.Contains, _ => false);
    }

    // CARD-1358 D-3: a current stamp the reader refused is unprovable, not a change; a readable stamp that differs is a change.
    private static void Compare(string path, string recorded, string current, ICollection<string> changed, Dictionary<string, string> unreadable)
    {
        if (IsUnreadable(current))
            unreadable[path] = ReasonOf(current);
        else if (!string.Equals(current, recorded, StringComparison.Ordinal))
            changed.Add(path);
    }

    private static LedgerCapture RefuseLedger(string detail) =>
        new(null, "tracked-snapshot", "refused: tracked-snapshot\n" + detail + "\n");

    // The owned layout plus bin-<id>/ and obj/ beside each statically visible project, as
    // repository-relative prefixes. A root outside the repository adds no prefix.
    private static List<string> WriteRoots(string repository, string workingDirectory, string project, string outputValue)
    {
        var directories = new List<string> { LayoutFor(workingDirectory, project, outputValue).Owned };
        if (ProjectWriteRoots.TryCollect(workingDirectory, project, outputValue, out var collected) is null)
            directories.AddRange(collected);
        var top = Path.GetFullPath(repository).Replace('\\', '/').TrimEnd('/') + "/";
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var prefixes = new List<string>();
        foreach (var directory in directories)
        {
            var slash = Path.GetFullPath(directory).Replace('\\', '/').TrimEnd('/') + "/";
            if (slash.Length > top.Length && slash.StartsWith(top, comparison))
                prefixes.Add(slash[top.Length..]);
        }

        return prefixes;
    }

    private static bool UnderRoots(string relative, IReadOnlyList<string> roots)
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        foreach (var root in roots)
            if (relative.StartsWith(root, comparison))
                return true;
        return false;
    }

    public static Prepared Prepare(
        string workingDirectory,
        IReadOnlyList<string> arguments,
        IReadOnlyDictionary<string, string>? environment,
        CancellationToken cancellationToken,
        Ledger? ledger = null)
    {
        if (!TryFindOutput(arguments, out var outputValue))
            return new Prepared(null, "", arguments, null);
        if (!TryFindProject(arguments, out var project))
            return Refuse("graph-unevaluated", "build command has no project path");

        var projectFull = Path.GetFullPath(Path.IsPathRooted(project) ? project : Path.Combine(workingDirectory, project));
        var projectDir = ProjectWriteRoots.DirectoryOf(projectFull);
        if (string.IsNullOrEmpty(projectDir))
            return Refuse("graph-unevaluated", "project directory missing");

        var repo = Git(workingDirectory, ["rev-parse", "--show-toplevel"]);
        if (repo.Error is not null || repo.Exit != 0 || string.IsNullOrWhiteSpace(repo.Stdout))
            return Refuse("tracked-snapshot", repo.Error ?? "git-toplevel");
        var repository = repo.Stdout.Trim();
        var status = ReadStatus(repository);
        if (status.Error is not null)
            return Refuse("tracked-snapshot", status.Error);
        if (status.Paths.Count > 0)
            return new Prepared("dirty-tracked", DirtyText(status.Paths), [], null);

        var captured = Capture(repository, ledger);
        if (captured.Error is not null || captured.Value is null)
            return Refuse("tracked-snapshot", captured.Error ?? "git-ls-files");
        var baseline = captured.Value;

        var layout = LayoutFor(workingDirectory, project, outputValue);
        var fresh = SafeWrite.WasCreated(layout.Owned) ? null : SafeWrite.CreateFreshDirectory(layout.Owned, SafeWrite.Under(layout.Owned));
        if (fresh is not null) return Refuse(fresh, layout.Owned);
        var rewritten = Rewrite(arguments, layout.Owned, layout.Artifacts);
        var forwarded = Forwarded(arguments);
        WalkResult walked;
        try
        {
            walked = Walk(projectFull, layout.Owned, layout.Artifacts, forwarded, workingDirectory, environment, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            var settled = Settle(baseline);
            if (settled.Kind is not null)
                return new Prepared(settled.Kind, settled.Detail, [], baseline);
            throw;
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or JsonException)
        {
            return PreferSettlement(baseline, Refuse("graph-unevaluated", ex.GetType().Name));
        }

        if (walked.Error is not null)
            return PreferSettlement(baseline, Refuse("graph-unevaluated", walked.Error));
        return new Prepared(null, "", rewritten, baseline);
    }

    // stamp reads each baseline entry's content stamp at settlement; Prepare's own callers keep the real reader (CARD-1358 seam).
    public static Settlement Settle(Baseline baseline, Func<string, string>? stamp = null)
    {
        stamp ??= Stamp;
        var status = ReadStatus(baseline.Repository);
        if (status.Error is not null)
            return new Settlement("tracked-snapshot", "refused: tracked-snapshot\n" + status.Error + "\nBuild-result bytes and index entries were left in place; no source recovery was attempted.\n", []);
        var paths = new HashSet<string>(status.Paths, StringComparer.Ordinal);
        var unreadable = new Dictionary<string, string>(StringComparer.Ordinal);
        var staged = new HashSet<string>(StringComparer.Ordinal);
        foreach (var pair in baseline.Content.Concat(baseline.Untracked))
        {
            if (IsUnreadable(pair.Value))
                unreadable[pair.Key] = ReasonOf(pair.Value);
            else
                Compare(pair.Key, pair.Value, stamp(Combine(baseline.Repository, pair.Key)), paths, unreadable);
        }
        if (!IndexMatches(baseline, out var error))
        {
            if (error is not null)
                return new Settlement("tracked-snapshot", "refused: tracked-snapshot\n" + error + "\n", []);
            var listed = Git(baseline.Repository, ["ls-files", "-z", "-s"]);
            if (listed.Error is not null) return new Settlement("tracked-snapshot", "refused: " + listed.Error, []);
            var current = ParseIndex(listed.Stdout);
            if (current.Value is null) return new Settlement("tracked-snapshot", "refused: index-record", []);
            foreach (var key in current.Value.Keys) if (!baseline.Entries.ContainsKey(key)) staged.Add(key);
            foreach (var key in baseline.Entries.Keys.Union(current.Value.Keys))
                if (!baseline.Entries.TryGetValue(key, out var old) || !current.Value.TryGetValue(key, out var now) || old != now)
                    paths.Add(key);
        }
        var ordinary = Git(baseline.Repository, ["ls-files", "-z", "--others", "--exclude-standard"]);
        if (ordinary.Error is not null) return new Settlement("tracked-snapshot", "refused: " + ordinary.Error, []);
        foreach (var path in ordinary.Stdout.Split('\0', StringSplitOptions.RemoveEmptyEntries))
            if (!path.StartsWith(".antiphon/", StringComparison.Ordinal) && !baseline.Untracked.ContainsKey(path)
                && baseline.Covered?.ContainsKey(path) != true) paths.Add(path);
        return Classify(baseline.Repository, baseline.Head, paths, unreadable, baseline.Entries.ContainsKey, staged.Contains);
    }

    // CARD-1358 D-3: any changed path makes the kind tampered, with the unreadable entries listed beside it before the
    // restore line; only unreadable entries make the kind unreadable, which names no restore because no content
    // difference was observed. Paths lists every refusing path either way.
    private static Settlement Classify(string repository, string head, IEnumerable<string> found,
        IReadOnlyDictionary<string, string> unreadable, Func<string, bool> tracked, Func<string, bool> staged)
    {
        var changed = found.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        var unprovable = unreadable.Keys.Where(path => !changed.Contains(path)).Order(StringComparer.Ordinal).ToList();
        if (changed.Count == 0 && unprovable.Count == 0)
            return new Settlement(null, "", []);
        var paths = changed.Concat(unprovable).Order(StringComparer.Ordinal).ToList();
        var text = new StringBuilder();
        if (changed.Count > 0)
        {
            text.Append("refused: tampered\n");
            foreach (var path in changed)
            {
                text.Append("TAMPERED ").Append(path).Append('\n');
                if (tracked(path))
                    text.Append("manual command: git -C ").Append(Quote(repository)).Append(" restore --source=").Append(head).Append(" --staged --worktree -- ").Append(Quote(path)).Append('\n');
                else if (staged(path))
                    text.Append("manual command: git -C ").Append(Quote(repository)).Append(" restore --staged -- ").Append(Quote(path)).Append('\n');
                else
                    text.Append("manual command: git -C ").Append(Quote(repository)).Append(" status --short -- ").Append(Quote(path)).Append("; inspect and recover user bytes from your backup\n");
            }
            foreach (var path in unprovable)
                text.Append(UnreadableLine(path, unreadable[path])).Append('\n');
            text.Append("manual restore required; Build-result bytes and index entries were left in place; no source recovery was attempted.\n");
            return new Settlement("tampered", text.ToString(), paths);
        }

        text.Append("refused: unreadable\n");
        foreach (var path in unprovable)
            text.Append(UnreadableLine(path, unreadable[path])).Append('\n');
        text.Append("no content difference was observed; nothing in the worktree or index was changed; no restore is needed; repeat the run.\n");
        return new Settlement("unreadable", text.ToString(), paths);
    }

    private static string UnreadableLine(string path, string reason) => "UNREADABLE " + path + ": " + reason;

    private static string Combine(string repository, string relative) => Path.Combine(repository, relative.Replace('/', Path.DirectorySeparatorChar));

    private readonly record struct ManifestResult(Dictionary<string, string>? Value, string? Error);

    private readonly record struct ListResult(List<string>? Value, string? Error);

    private static ListResult ListUntracked(string repository)
    {
        var ordinary = Git(repository, ["ls-files", "-z", "--others", "--exclude-standard"]);
        if (ordinary.Error is not null) return new ListResult(null, ordinary.Error);
        var listed = Git(repository, ["ls-files", "-z", "--others", "--ignored", "--exclude-standard"]);
        if (listed.Error is not null || listed.Exit != 0)
            return new ListResult(null, listed.Error ?? "git-ls-files");
        var paths = new List<string>();
        foreach (var relative in (ordinary.Stdout + listed.Stdout).Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            // Evidence is written by the checkpoint process while the build is observed.
            if (!relative.StartsWith(".antiphon/", StringComparison.Ordinal))
                paths.Add(relative);
        }
        return new ListResult(paths, null);
    }

    // Paths the ledger covers were stamped before the slot; the other listed paths are stamped here.
    private static ManifestResult CaptureUntracked(string repository, IReadOnlyDictionary<string, string>? covered)
    {
        var listed = ListUntracked(repository);
        if (listed.Error is not null || listed.Value is null) return new ManifestResult(null, listed.Error ?? "git-ls-files");
        var files = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var relative in listed.Value)
            if (covered?.ContainsKey(relative) != true)
                files[relative] = Stamp(Combine(repository, relative));
        return new ManifestResult(files, null);
    }

    // CARD-1358 D-4: a refused read stamps "unreadable|<reason>", the innermost message on one line, at most 200 characters.
    private const string UnreadablePrefix = "unreadable|";

    internal static bool IsUnreadable(string stamp) => stamp.StartsWith("unreadable", StringComparison.Ordinal);

    internal static string ReasonOf(string stamp) =>
        stamp.Length > UnreadablePrefix.Length && stamp.StartsWith(UnreadablePrefix, StringComparison.Ordinal)
            ? stamp[UnreadablePrefix.Length..]
            : "read refused";

    private static string Reason(Exception ex)
    {
        var inner = ex;
        while (inner.InnerException is not null)
            inner = inner.InnerException;
        var text = inner.Message.Replace('\r', ' ').Replace('\n', ' ').Trim();
        if (text.Length == 0)
            text = inner.GetType().Name;
        return text.Length > 200 ? text[..200] : text;
    }

    internal static string Stamp(string full) => Stamp(full, path => ReceiptFile.ReadBytes(path));

    internal static string Stamp(string full, Func<string, byte[]> readBytes)
    {
        try
        {
            var file = new FileInfo(full);
            if (file.LinkTarget is not null)
                return StampLink(file.LinkTarget);
            var directory = new DirectoryInfo(full);
            if (directory.LinkTarget is not null)
                return StampLink(directory.LinkTarget);
            if (directory.Exists)
                return "directory|0|";
            if (!file.Exists)
                return "absent|0|";
            var bytes = readBytes(full);
            return "file|" + bytes.Length + "|" + Hash(bytes);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return UnreadablePrefix + Reason(ex);
        }
    }

    private static string StampLink(string target)
    {
        var bytes = Encoding.UTF8.GetBytes(target);
        return "symlink|" + bytes.Length + "|" + Hash(bytes);
    }

    private static string Hash(byte[] bytes) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant();

    private static Prepared PreferSettlement(Baseline baseline, Prepared graphRefusal)
    {
        var settled = Settle(baseline);
        if (settled.Kind is not null)
            return new Prepared(settled.Kind, settled.Detail, [], baseline);
        if (settled.Kind == "tampered")
            return new Prepared("tampered", settled.Detail, [], baseline);
        return graphRefusal;
    }

    private static Prepared Refuse(string reason, string detail) =>
        new(reason, "refused: " + reason + "\n" + detail + "\n", [], null);

    private static string DirtyText(IReadOnlyList<string> paths) => "refused: dirty-tracked\n" + string.Join("\n", paths.Select(path => "DIRTY " + path)) + "\n";

    private static string Quote(string value) => "'" + value.Replace("'", "'\\''", StringComparison.Ordinal) + "'";

    private static bool TryFindProject(IReadOnlyList<string> arguments, out string project)
    {
        project = "";
        for (var i = 1; i < arguments.Count; i++)
        {
            var token = arguments[i];
            if (token.StartsWith('-') || token.StartsWith("/p:", StringComparison.OrdinalIgnoreCase)
                || token.StartsWith("/property:", StringComparison.OrdinalIgnoreCase))
                continue;
            project = token;
            return true;
        }

        return false;
    }

    private static bool TryFindOutput(IReadOnlyList<string> arguments, out string value)
    {
        value = "";
        foreach (var token in arguments)
        {
            if (TryReadProperty(token, out var key, out var property) && key.Equals("OutputPath", StringComparison.OrdinalIgnoreCase))
            {
                value = property;
                return true;
            }
        }

        return false;
    }

    private static List<string> Rewrite(IReadOnlyList<string> arguments, string owned, string artifacts)
    {
        var result = new List<string>(arguments.Count + 3);
        var artifactsSeen = false;
        var useSeen = false;
        var hostSeen = false;
        foreach (var token in arguments)
        {
            if (TryReadProperty(token, out var key, out _))
            {
                if (key.Equals("OutputPath", StringComparison.OrdinalIgnoreCase))
                {
                    result.Add("--property:OutputPath=" + owned);
                    continue;
                }

                if (key.Equals("ArtifactsPath", StringComparison.OrdinalIgnoreCase))
                    artifactsSeen = true;
                else if (key.Equals("UseArtifactsOutput", StringComparison.OrdinalIgnoreCase))
                    useSeen = true;
                else if (key.Equals("UseAppHost", StringComparison.OrdinalIgnoreCase))
                    hostSeen = true;
            }

            result.Add(token);
        }

        if (!artifactsSeen)
            result.Add("--property:ArtifactsPath=" + artifacts);
        if (!useSeen)
            result.Add("--property:UseArtifactsOutput=true");
        AddConfinedUseAppHost(result, hostSeen);
        return result;
    }

    // CARD-0671: off Windows the extensionless fakeclaude apphost collides with the fakeclaude directory.
    private static void AddConfinedUseAppHost(List<string> args, bool hostSeen)
    {
        if (!hostSeen && !OperatingSystem.IsWindows())
            args.Add("--property:UseAppHost=false");
    }

    private static List<string> Forwarded(IReadOnlyList<string> arguments)
    {
        var forwarded = new List<string>();
        foreach (var token in arguments)
        {
            if (!TryReadProperty(token, out var key, out var value) || IsolationKeys.Contains(key))
                continue;
            forwarded.Add("--property:" + key + "=" + value);
        }

        return forwarded;
    }

    private static string AbsoluteOutput(string projectDir, string outputValue)
    {
        var full = Path.IsPathRooted(outputValue)
            ? Path.GetFullPath(outputValue)
            : Path.GetFullPath(Path.Combine(projectDir, outputValue));
        var slash = full.Replace('\\', '/');
        return slash.EndsWith('/') ? slash : slash + "/";
    }

    private readonly record struct WalkResult(List<string> Projects, string? Error);

    private static WalkResult Walk(
        string projectFull,
        string owned,
        string artifacts,
        IReadOnlyList<string> forwarded,
        string workingDirectory,
        IReadOnlyDictionary<string, string>? environment,
        CancellationToken cancellationToken)
    {
        var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var seen = new HashSet<string>(comparer);
        var queue = new Queue<string>();
        var projects = new List<string>();
        queue.Enqueue(projectFull);
        while (queue.Count > 0)
        {
            var current = Path.GetFullPath(queue.Dequeue());
            if (!seen.Add(current))
                continue;
            if (projects.Count >= WalkCap)
                return new WalkResult([], "walk cap exceeded");
            projects.Add(current);
            var eval = Evaluate(current, owned, artifacts, forwarded, workingDirectory, environment, cancellationToken);
            if (eval.Error is not null)
                return new WalkResult([], eval.Error);
            foreach (var reference in eval.References)
            {
                if (!File.Exists(reference))
                    return new WalkResult([], "referenced project missing: " + reference);
                queue.Enqueue(reference);
            }
        }

        return new WalkResult(projects, null);
    }

    private readonly record struct EvalResult(List<string> References, string? Error);

    private static EvalResult Evaluate(
        string project,
        string owned,
        string artifacts,
        IReadOnlyList<string> forwarded,
        string workingDirectory,
        IReadOnlyDictionary<string, string>? environment,
        CancellationToken cancellationToken)
    {
        var args = new List<string>
        {
            "msbuild", project, "-getItem:ProjectReference", "-nologo", "-nodeReuse:false",
            "--property:OutputPath=" + owned,
            "--property:ArtifactsPath=" + artifacts,
            "--property:UseArtifactsOutput=true",
        };
        args.AddRange(forwarded);
        AddConfinedUseAppHost(args, forwarded.Any(token => token.StartsWith("--property:UseAppHost=", StringComparison.OrdinalIgnoreCase)));

        var psi = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = string.IsNullOrWhiteSpace(workingDirectory) ? Environment.CurrentDirectory : workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (var arg in args)
            psi.ArgumentList.Add(arg);
        if (environment is not null)
            foreach (var (name, value) in environment)
                psi.Environment[name] = value;
        psi.Environment["MSBUILDDISABLENODEREUSE"] = "1";
        psi.Environment["DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER"] = "1";
        new Antiphon.Build.BuildDriverEnvironment().Apply(psi);
        foreach (var name in new[] { "GIT_DIR", "GIT_WORK_TREE", "GIT_INDEX_FILE" })
            psi.Environment.Remove(name);

        CheckpointProcessRunner.Result result;
        try { result = CheckpointProcessRunner.Run(psi, 120_000, false, cancellationToken); }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        { return new EvalResult([], ex.GetType().Name); }
        if (result.TimedOut) return new EvalResult([], "msbuild evaluation timed out");
        if (result.Exit != 0) return new EvalResult([], "msbuild exit " + result.Exit);
        return ParseReferences(result.Stdout, result.Stderr);
    }

    private static EvalResult ParseReferences(string stdout, string stderr)
    {
        var start = stdout.IndexOf('{');
        if (start < 0)
            return new EvalResult([], "msbuild json missing");
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(stdout[start..]);
        }
        catch (JsonException)
        {
            return new EvalResult([], "msbuild json unusable");
        }

        using (doc)
        {
            if (!doc.RootElement.TryGetProperty("Items", out var items) || items.ValueKind != JsonValueKind.Object)
                return new EvalResult([], "msbuild items missing");
            if (!items.TryGetProperty("ProjectReference", out var references))
                return new EvalResult([], null);
            var paths = new List<string>();
            if (references.ValueKind == JsonValueKind.Object)
            {
                if (!TryFullPath(references, paths))
                    return new EvalResult([], "project reference FullPath missing");
            }
            else if (references.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in references.EnumerateArray())
                {
                    if (!TryFullPath(item, paths))
                        return new EvalResult([], "project reference FullPath missing");
                }
            }
            else
                return new EvalResult([], "project reference json unusable");

            _ = stderr;
            return new EvalResult(paths, null);
        }
    }

    private static bool TryFullPath(JsonElement item, List<string> paths)
    {
        if (!item.TryGetProperty("FullPath", out var full) || full.ValueKind != JsonValueKind.String)
            return false;
        var path = full.GetString();
        if (string.IsNullOrWhiteSpace(path))
            return false;
        paths.Add(path);
        return true;
    }

    private readonly record struct CaptureResult(Baseline? Value, string? Error);

    private static CaptureResult Capture(string repository, Ledger? ledger)
    {
        var covered = ledger is not null && SameRepository(ledger.Repository, repository) ? ledger.Files : null;
        var head = Git(repository, ["rev-parse", "HEAD"]);
        if (head.Error is not null || head.Exit != 0 || string.IsNullOrWhiteSpace(head.Stdout))
            return new CaptureResult(null, head.Error ?? "git-head");
        var listed = Git(repository, ["ls-files", "-z", "-s"]);
        if (listed.Error is not null || listed.Exit != 0)
            return new CaptureResult(null, listed.Error ?? "git-ls-files");
        var entries = ParseIndex(listed.Stdout);
        if (entries.Error is not null || entries.Value is null)
            return new CaptureResult(null, entries.Error ?? "index-record");
        var untracked = CaptureUntracked(repository, covered);
        if (untracked.Error is not null || untracked.Value is null)
            return new CaptureResult(null, untracked.Error ?? "git-ls-files");
        var content = entries.Value.Keys.Where(path => covered?.ContainsKey(path) != true)
            .ToDictionary(path => path, path => Stamp(Combine(repository, path)), StringComparer.Ordinal);
        var unreadable = content.Where(pair => IsUnreadable(pair.Value)).OrderBy(pair => pair.Key, StringComparer.Ordinal).ToList();
        if (unreadable.Count > 0)
            return new CaptureResult(null, "tracked-unreadable\n" + string.Join("\n", unreadable.Select(pair => UnreadableLine(pair.Key, ReasonOf(pair.Value)))));
        return new CaptureResult(new Baseline(head.Stdout.Trim(), repository, entries.Value, untracked.Value, content, covered), null);
    }

    private static bool SameRepository(string left, string right) =>
        string.Equals(Path.GetFullPath(left).Replace('\\', '/').TrimEnd('/'), Path.GetFullPath(right).Replace('\\', '/').TrimEnd('/'),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private readonly record struct IndexResult(Dictionary<string, string>? Value, string? Error);

    private static IndexResult ParseIndex(string text)
    {
        var entries = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var record in text.Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            var tab = record.IndexOf('\t');
            if (tab < 1 || tab >= record.Length - 1)
                return new IndexResult(null, "index-record");
            entries[record[(tab + 1)..]] = record[..tab];
        }

        return new IndexResult(entries, null);
    }

    private static bool IndexMatches(Baseline baseline, out string? error)
    {
        var listed = Git(baseline.Repository, ["ls-files", "-z", "-s"]);
        if (listed.Error is not null || listed.Exit != 0)
        {
            error = listed.Error ?? "git-ls-files";
            return false;
        }

        var parsed = ParseIndex(listed.Stdout);
        if (parsed.Error is not null || parsed.Value is null)
        {
            error = parsed.Error ?? "index-record";
            return false;
        }

        error = null;
        if (parsed.Value.Count != baseline.Entries.Count)
            return false;
        foreach (var pair in baseline.Entries)
        {
            if (!parsed.Value.TryGetValue(pair.Key, out var right) || !string.Equals(pair.Value, right, StringComparison.Ordinal))
                return false;
        }

        return true;
    }

    private readonly record struct StatusResult(List<string> Paths, string? Error);

    private static StatusResult ReadStatus(string repository)
    {
        var status = Git(repository, ["status", "--porcelain=v2", "-z", "--untracked-files=no"]);
        if (status.Error is not null || status.Exit != 0)
            return new StatusResult([], status.Error ?? "git-status");
        return ParseStatus(status.Stdout);
    }

    private static StatusResult ParseStatus(string text)
    {
        var parts = text.Split('\0', StringSplitOptions.RemoveEmptyEntries);
        var paths = new List<string>();
        for (var i = 0; i < parts.Length; i++)
        {
            var entry = parts[i];
            var count = entry[0] switch { '1' => 9, '2' => 10, 'u' => 11, _ => 0 };
            if (count == 0) return new StatusResult([], "status-record");
            var fields = entry.Split(' ', count);
            if (fields.Length != count || fields[^1].Length == 0) return new StatusResult([], "status-record");
            paths.Add(fields[^1]);
            if (entry[0] == '2')
            {
                if (++i >= parts.Length) return new StatusResult([], "status-rename");
                paths.Add(parts[i]);
            }
        }
        return new StatusResult(paths.Distinct(StringComparer.Ordinal).ToList(), null);
    }

    private readonly record struct GitResult(int Exit, string Stdout, string? Error);

    private static GitResult Git(string workingDirectory, IReadOnlyList<string> args)
    {
        var psi = new ProcessStartInfo("git")
        {
            WorkingDirectory = string.IsNullOrWhiteSpace(workingDirectory) ? Environment.CurrentDirectory : workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (var arg in args)
            psi.ArgumentList.Add(arg);
        foreach (var name in new[] { "GIT_DIR", "GIT_WORK_TREE", "GIT_INDEX_FILE" })
            psi.Environment.Remove(name);
        CheckpointProcessRunner.Result result;
        try { result = CheckpointProcessRunner.Run(psi, 30_000, true); }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        { return new GitResult(128, "", "git-unavailable"); }
        if (result.TimedOut) return new GitResult(128, "", "git-timeout");
        return result.Exit == 0 ? new GitResult(0, result.Stdout, null) : new GitResult(result.Exit, "", "git-failed");
    }

    private static bool TryReadProperty(string token, out string key, out string value)
    {
        key = "";
        value = "";
        string? body = null;
        if (token.StartsWith("--property:", StringComparison.OrdinalIgnoreCase))
            body = token["--property:".Length..];
        else if (token.StartsWith("-p:", StringComparison.OrdinalIgnoreCase))
            body = token["-p:".Length..];
        else if (token.StartsWith("/p:", StringComparison.OrdinalIgnoreCase))
            body = token["/p:".Length..];
        if (body is null)
            return false;
        var eq = body.IndexOf('=');
        if (eq <= 0)
            return false;
        key = body[..eq];
        value = body[(eq + 1)..];
        return true;
    }
}
