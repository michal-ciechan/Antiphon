using System.Diagnostics;
using System.Globalization;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using Antiphon.Nightly;

namespace Antiphon.Tests.Scripts.Nightly;

/// <summary>
/// W2-nightly S14: the fixture and helpers of the stopped-run seed cases moved in-process from
/// scripts/test-nightly-stopped-seed.ps1. One world per case: the synthetic stopped run (scripts/fixtures/nightly/stopped-seed
/// copied as synthetic-run, its JSON time stamps moved to four hours ago), the five synthetic suites of five chunks each, the
/// ingress and schedule calls, the tree snapshots, the sibling-timing layouts, the merge seeds and git repositories, and the
/// unreadable-timing custody (Unix modes, else a current-user deny ACL, else a counted not-applicable).
/// </summary>
internal sealed class SeedWorld : IDisposable
{
    /// <summary>Cases that build their own layouts and seeds: no common export of the fixture run.</summary>
    private static readonly string[] OwnSeeds =
    [
        "StoppedTimingRootAndRefusals", "ChunkUnfinishedTolerance", "LinkCacheRefusesReplacement", "SkipRowsCountAsCompleted",
        "SummaryPrefixTamper", "ScaleExportBound", "RealPackerRegroupKeepsFailedFirst", "RealPackerFailedMethodFollows",
        "RealShapeFailedMethodBand", "UnfinishedRunLaneResume",
    ];

    public static string Repo => NightlyPlanCases.Repo;

    /// <summary>scripts/fixtures/nightly/stopped-seed.</summary>
    public static string FixtureDirectory => Path.Combine(Repo, "scripts", "fixtures", "nightly", "stopped-seed");

    public static readonly string B40 = new('b', 40);

    private SeedWorld(string caseName)
    {
        Case = caseName;
        ResetStatics();
        Fixture = NightlyFixture.Create("ss-" + caseName.ToLowerInvariant());
        Run = Path.Combine(Root, "synthetic-run");
        CopyDirectory(FixtureDirectory, Run);
        // Refresh fixture timestamps as a single fixed clock; fixture membership stays synthetic.
        Now = DateTime.UtcNow;
        foreach (var file in Directory.GetFiles(Run, "*.json"))
        {
            var text = File.ReadAllText(file).Replace("2026-10-30T08:30:00Z", Now.AddHours(-4).ToString("o", CultureInfo.InvariantCulture));
            File.WriteAllText(file, text);
        }
        SeedPath = Path.Combine(Root, "seed.json");
        Suites = new[] { "antiphon", "session-runner", "pty-host", "agents-pty", "e2e" }.Select(id =>
        {
            var prefix = "Synthetic." + id.Replace("-", "_") + ".";
            string[] names = ["Passed", "Never", "Red", "Torn", "New"];
            var chunks = names.Select(name => (object)Rec(("id", "new-" + name), ("classes", new object[] { prefix + name }), ("nodes", Array.Empty<object>()))).ToArray();
            var nodes = names.Select(name => (object)Rec(("Uid", prefix + name), ("ClassName", prefix + name), ("Method", "Run"), ("Type", name), ("Categories", Array.Empty<object>()))).ToArray();
            return (object)Rec(("id", id), ("chunks", chunks), ("nodes", nodes), ("exclusions", Array.Empty<object>()));
        }).ToArray();
    }

    /// <summary>The world of one case (the fixture copy and the suites; the common export is <see cref="CommonExport"/>).</summary>
    public static SeedWorld Create(string caseName) => new(caseName);

    public string Case { get; }

    public NightlyFixture Fixture { get; }

    /// <summary>The C487 results directory of the case.</summary>
    public string Root => Fixture.Root;

    /// <summary>The synthetic stopped run (a copy of the fixture).</summary>
    public string Run { get; }

    /// <summary>The case's fixed clock ($now).</summary>
    public DateTime Now { get; }

    /// <summary>root/seed.json: the common export.</summary>
    public string SeedPath { get; }

    /// <summary>The five synthetic suites (antiphon, session-runner, pty-host, agents-pty, e2e) of chunks new-Passed .. new-New.</summary>
    public object[] Suites { get; }

    /// <summary>Snapshot before and after the common export.</summary>
    public string Before { get; private set; } = "";

    public string After { get; private set; } = "";

    /// <summary>The common export of the fixture run and its read-only snapshots, for every case but those that build their own.</summary>
    public void CommonExport()
    {
        if (OwnSeeds.Contains(Case, StringComparer.Ordinal) || Case.StartsWith("CumulativeMerge", StringComparison.Ordinal)) return;
        Before = Snapshot();
        StoppedSeedFiles.Export(Run, SeedPath, "", "", "", "");
        After = Snapshot();
    }

    public string PathOf(params string[] parts) => Fixture.PathOf(parts);

    /// <summary>The process-wide seed statics, as a process of its own would start them.</summary>
    public static void ResetStatics()
    {
        HistoryLinkCache.Reset();
        HistoryNodeIdentity.ForceWindows = false;
        StoppedSeedFiles.ReadBarrier = null;
    }

    public void Dispose()
    {
        ResetStatics();
        Fixture.Dispose();
    }

    // ---- ingress and schedule -------------------------------------------------------------------------------

    /// <summary>Ingress: PriorFailureHistory.InitializeDispatch for candidate b*40.</summary>
    public object Ingress(string? path = null, bool accept = false, string source = "rc-synthetic-old") =>
        PriorFailureHistory.InitializeDispatch(path ?? SeedPath, "", "", B40, accept, source, DateTime.UtcNow);

    /// <summary>Schedule $InputRecord: the history resolved onto the suites, scheduled with its previous run for candidate b*40.</summary>
    public object Schedule(object input)
    {
        object[] members = [], unresolved = [];
        var history = PsValue.Prop(input, "History");
        if (PsValue.IsTrue(history))
        {
            var resolved = PriorFailureHistory.Resolve(history, Suites, "Full", null, null);
            members = Seed.Arr(resolved, "members");
            unresolved = Seed.Arr(resolved, "unresolved");
        }
        return Sched(Suites, members, unresolved, PsValue.Prop(input, "PreviousRun"), B40, Now);
    }

    /// <summary>New-NightlyDispatchSchedule with its defaults.</summary>
    public static object Sched(object[] prepared, object[]? members = null, object[]? unresolved = null, object? previous = null, string candidate = "",
        DateTime? now = null) =>
        NightlyDispatch.Schedule(prepared, members ?? [], "", unresolved ?? [], true, "", "", previous, candidate, now ?? DateTime.UtcNow, "");

    /// <summary>Invoke-MergeChunkOrder: one Run node per chunk's first class, resolved and scheduled with the seed as previous run.</summary>
    public static object MergeChunkOrder(object doc, object[] chunks, DateTime now)
    {
        var nodes = new List<object>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var chunk in chunks)
        {
            var cls = PsValue.Str(Seed.Arr(chunk, "classes").FirstOrDefault());
            var key = cls + "/Run";
            if (seen.Add(key)) nodes.Add(Rec(("Uid", key), ("ClassName", cls), ("Method", "Run"), ("Type", "T"), ("Categories", Array.Empty<object>())));
        }
        object[] suite = [Rec(("id", "antiphon"), ("chunks", chunks), ("nodes", nodes.ToArray()), ("exclusions", Array.Empty<object>()))];
        var resolved = PriorFailureHistory.Resolve(doc, suite, "Full", null, null);
        return Sched(suite, Seed.Arr(resolved, "members"), null, doc, B40, now);
    }

    // ---- snapshots ------------------------------------------------------------------------------------------

    /// <summary>Snapshot: the run directory and every item under it, path|mtime ticks|SHA-256 ('directory' for a directory), sorted.</summary>
    public string Snapshot()
    {
        var items = new List<string> { Item(new DirectoryInfo(Run)) };
        items.AddRange(new DirectoryInfo(Run).EnumerateFileSystemInfos("*", SearchOption.AllDirectories).Select(Item));
        items.Sort(StringComparer.Ordinal);
        return string.Join("\n", items);
    }

    /// <summary>Snapshot-Tree: each root as path|dir, then every item under it, sorted and joined.</summary>
    public static string SnapshotTree(params string[] roots)
    {
        var items = new List<string>();
        foreach (var tree in roots)
        {
            items.Add(new DirectoryInfo(tree).FullName + "|dir");
            items.AddRange(new DirectoryInfo(tree).EnumerateFileSystemInfos("*", SearchOption.AllDirectories).Select(Item));
        }
        items.Sort(StringComparer.Ordinal);
        return string.Join("\n", items);
    }

    private static string Item(FileSystemInfo info) =>
        info.FullName + "|" + info.LastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture) + "|" + (info is DirectoryInfo ? "directory" : FileHash(info.FullName));

    /// <summary>Get-FileHash: SHA-256, upper-case hex.</summary>
    public static string FileHash(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    // ---- sibling layouts ------------------------------------------------------------------------------------

    public sealed record Layout(string Run, string Timing, string RunId);

    /// <summary>
    /// New-SeedSiblingLayout: logs/runName holding the fixture files (owner runId rebound), its planned groups moved to
    /// logs/timing-runId (runId rebound) beside a one-field planned index.
    /// </summary>
    public static Layout SiblingLayout(string logs, string runId, string runName)
    {
        var runDir = Path.Combine(logs, runName);
        var timing = Path.Combine(logs, "timing-" + runId);
        Directory.CreateDirectory(runDir);
        Directory.CreateDirectory(timing);
        CopyFiles(FixtureDirectory, runDir);
        var ownerPath = Path.Combine(runDir, "owner.json");
        if (File.Exists(ownerPath))
            File.WriteAllText(ownerPath, File.ReadAllText(ownerPath).Replace("\"runId\": \"synthetic-stopped\"", "\"runId\": \"" + runId + "\""));
        foreach (var plan in Directory.GetFiles(runDir, "groups-planned*.json"))
        {
            var text = File.ReadAllText(plan).Replace("\"runId\": \"synthetic-stopped\"", "\"runId\": \"" + runId + "\"");
            File.WriteAllText(Path.Combine(timing, Path.GetFileName(plan)), text);
            File.Delete(plan);
        }
        File.WriteAllText(Path.Combine(timing, "groups-planned-index.json"), "{\"version\":1}");
        return new Layout(runDir, timing, runId);
    }

    /// <summary>The summary header (startedAt, completedAt, sha, runId, a decoy selection.timingChunks, timingRoot) as compact JSON without its closing brace.</summary>
    public static string SummaryHeader(string timingRoot, string runId, string sha) =>
        NightlyJson.Text(Rec(("startedAt", "2026-10-30T08:30:00Z"), ("completedAt", "2026-10-30T12:30:00Z"), ("sha", sha), ("runId", runId),
            ("selection", Rec(("timingChunks", "decoy"))), ("timingRoot", timingRoot)), 64)!.TrimEnd('}');

    /// <summary>Write-SeedSummary: the header, then a bulk timingChunks key whose value is not JSON.</summary>
    public static void WriteSeedSummary(string runDir, string timingRoot, string runId, string sha) =>
        File.WriteAllText(Path.Combine(runDir, "summary.json"), SummaryHeader(timingRoot, runId, sha) + ",\"timingChunks\": INVALID}");

    // ---- seeds ----------------------------------------------------------------------------------------------

    /// <summary>A seed envelope over the payload's compact JSON, with its content hash.</summary>
    public static PlainObject Envelope(string runId, string candidate, string sha, string created, object payload)
    {
        var envelope = Rec(("format", "antiphon-nightly-stopped-seed-v1"), ("seedKind", "operator-seed-from-stopped-run"), ("sourceRunId", runId),
            ("sourceCandidateId", candidate), ("sourceSha", sha), ("createdAtUtc", created), ("payload", NightlyJson.Text(payload, 64)), ("contentHash", ""));
        envelope["contentHash"] = StoppedSeedFiles.ContentHash(envelope);
        return envelope;
    }

    /// <summary>Writes a seed (or any record) as indented JSON, UTF-8 without a byte-order mark.</summary>
    public static void WriteJson(string path, object value)
    {
        if (!NightlyJson.WriteFile(path, value, 64)) throw new InvalidOperationException("seed json not writable: " + path);
    }

    /// <summary>ConvertFrom-Json of a file as plain values (dates kept as text).</summary>
    public static PlainObject ReadJson(string path) =>
        NightlyJson.ParseText(File.ReadAllText(path), 1024, false) as PlainObject ?? throw new InvalidOperationException("not a JSON object: " + path);

    /// <summary>Write-MergeSeed: a seed of these chunks, failed tests and evidence.</summary>
    public static void WriteMergeSeed(string path, string runId, string candidate, string sha, string observed, string created, object[] chunks,
        object[]? failed, object[]? evidence)
    {
        var payload = Rec(("sourceObservedAtUtc", observed), ("chunks", chunks), ("failedTests", failed ?? []), ("evidence", evidence ?? []));
        WriteJson(path, Envelope(runId, candidate, sha, created, payload));
    }

    public static PlainObject MergeChunk(string suite, string chunk, string[] classes, bool completed, bool started, string last) =>
        Rec(("suite", suite), ("chunk", chunk), ("classes", classes.Cast<object>().ToArray()), ("completed", completed), ("started", started), ("lastRunAtUtc", last));

    public static PlainObject MergeFail(string suite, string chunk, string cls, string method, string outcome) =>
        Rec(("suite", suite), ("chunk", chunk), ("className", cls), ("method", method), ("outcome", outcome));

    public sealed record MergeResult(int Code, string Text);

    /// <summary>
    /// Invoke-MergeNightlySeeds: the operator's argv (-Seed and -Proven repeat). In-process through the compiled merge (the
    /// command's own catch is exit 2 with the message); with cli, the antiphon-scripts merge-nightly-seeds command
    /// (NightlySeedCommands.Run with that argv, in-process): its exit code and what it wrote to stdout and stderr.
    /// </summary>
    public static Task<MergeResult> Merge(string[] seeds, string output, string report, string decision = "", string[]? proven = null, string repo = "",
        bool strict = false, bool cli = false)
    {
        var argv = new List<string>();
        foreach (var seed in seeds) { argv.Add("-Seed"); argv.Add(seed); }
        if (!string.IsNullOrWhiteSpace(decision)) { argv.Add("-Decision"); argv.Add(decision); }
        foreach (var row in proven ?? []) if (!string.IsNullOrWhiteSpace(row)) { argv.Add("-Proven"); argv.Add(row); }
        if (!string.IsNullOrWhiteSpace(repo)) { argv.Add("-Repo"); argv.Add(repo); }
        if (strict) argv.Add("-Strict");
        argv.Add("-OutputPath"); argv.Add(output);
        argv.Add("-ReportPath"); argv.Add(report);
        if (cli)
        {
            var stdout = new StringWriter();
            var stderr = new StringWriter();
            var code = NightlySeedCommands.Run([NightlySeedCommands.MergeSeeds, .. argv], stdout, stderr);
            return Task.FromResult(new MergeResult(code, stdout.ToString() + stderr));
        }
        try
        {
            var outcome = SeedMerge.Run(argv.ToArray());
            return Task.FromResult(new MergeResult(outcome.Code, string.Join("\n", outcome.Lines ?? [])));
        }
        catch (Exception ex)
        {
            return Task.FromResult(new MergeResult(2, ex.Message));
        }
    }

    /// <summary>Read-MergeReport.</summary>
    public static PlainObject? MergeReport(string path) => File.Exists(path) ? ReadJson(path) : null;

    /// <summary>Find-MergeIdentity: the first identity row of that exact method, or null.</summary>
    public static object? MergeIdentity(object? report, string method) =>
        report == null ? null : Seed.Arr(report, "identities").FirstOrDefault(i => PsValue.Str(PsValue.Prop(i, "method")) == method);

    /// <summary>Test-MergeFileBytes.</summary>
    public static bool SameBytes(string left, string right) => File.ReadAllBytes(left).AsSpan().SequenceEqual(File.ReadAllBytes(right));

    // ---- git ------------------------------------------------------------------------------------------------

    /// <summary>Invoke-C487FixtureGit: git in the repository, stdout trimmed; a nonzero exit throws.</summary>
    public static string Git(string repo, params string[] arguments)
    {
        var start = new ProcessStartInfo("git")
        {
            WorkingDirectory = Path.GetFullPath(repo),
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = new UTF8Encoding(false),
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("git did not start");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(30000))
        {
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            throw new InvalidOperationException("git-read-failed:" + arguments[0] + ":timeout");
        }
        process.WaitForExit();
        if (process.ExitCode != 0) throw new InvalidOperationException("git-read-failed:" + arguments[0] + ":" + process.ExitCode + " " + stderr.Result);
        return stdout.Result.Trim();
    }

    /// <summary>New-MergeGitRepo: git init -b master with a fixture identity.</summary>
    public static void NewGitRepo(string repo)
    {
        Directory.CreateDirectory(repo);
        Git(repo, "init", "-b", "master");
        Git(repo, "config", "user.email", "merge-seed@example.com");
        Git(repo, "config", "user.name", "merge-seed");
    }

    /// <summary>Add-MergeGitCommit: writes the file, commits it with the title (and body) and returns HEAD.</summary>
    public static string Commit(string repo, string name, string content, string title, string body)
    {
        File.WriteAllText(Path.Combine(repo, name), content, new UTF8Encoding(false));
        Git(repo, "add", "--", name);
        if (string.IsNullOrWhiteSpace(body)) Git(repo, "commit", "-m", title);
        else Git(repo, "commit", "-m", title, "-m", body);
        return Git(repo, "rev-parse", "HEAD");
    }

    // ---- helpers --------------------------------------------------------------------------------------------

    public static PlainObject Rec(params (string Name, object? Value)[] entries) => new(entries.Select(e => (e.Name, e.Value!)).ToArray());

    public static void CopyFiles(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var file in Directory.GetFiles(source)) File.Copy(file, Path.Combine(target, Path.GetFileName(file)));
    }

    private static void CopyDirectory(string source, string target)
    {
        CopyFiles(source, target);
        foreach (var directory in Directory.GetDirectories(source)) CopyDirectory(directory, Path.Combine(target, Path.GetFileName(directory)));
    }
}

/// <summary>The PowerShell readings the cases use, over plain values.</summary>
internal static class Seed
{
    /// <summary>[string]$o.name.</summary>
    public static string S(object? o, string name) => PsValue.Str(PsValue.Prop(o, name));

    /// <summary>@($o.name); nothing for a missing value.</summary>
    public static object[] Arr(object? o, string name) => PsValue.Prop(o, name) is { } value ? PsValue.AsArray(value) : [];

    /// <summary>[bool]$o.name.</summary>
    public static bool T(object? o, string name) => PsValue.IsTrue(PsValue.Prop(o, name));

    /// <summary>$o.name -eq $true / -eq $false: the value is that boolean.</summary>
    public static bool Is(object? o, string name, bool value) => PsValue.Prop(o, name) is bool b && b == value;

    /// <summary>[string] of a boolean.</summary>
    public static string B(bool value) => value ? "True" : "False";

    /// <summary>@($schedule.entries.chunk).</summary>
    public static string[] Ids(object schedule) => Arr(schedule, "entries").Select(e => S(e, "chunk")).ToArray();

    /// <summary>The suite/chunk key of each entry, in order.</summary>
    public static string[] Keys(object schedule) => Arr(schedule, "entries").Select(e => S(e, "suite") + "/" + S(e, "chunk")).ToArray();

    /// <summary>The suite/chunk key of each prepared chunk, in canonical order.</summary>
    public static string[] Canonical(IEnumerable<object> suites) =>
        suites.SelectMany(s => Arr(s, "chunks").Select(c => S(s, "id") + "/" + S(c, "id"))).ToArray();

    /// <summary>$a[from..to] (a descending range when to &lt; from, negative indexes from the end; out of range left out).</summary>
    public static string[] Range(string[] items, int from, int to)
    {
        var picked = new List<string>();
        var step = from <= to ? 1 : -1;
        for (var i = from; ; i += step)
        {
            var at = i < 0 ? items.Length + i : i;
            if (at >= 0 && at < items.Length) picked.Add(items[at]);
            if (i == to) break;
        }
        return picked.ToArray();
    }

    /// <summary>-join ','.</summary>
    public static string J(IEnumerable<string> items) => string.Join(",", items);

    /// <summary>$value -like '*fragment*' (case-insensitive).</summary>
    public static bool Has(string? value, string fragment) => (value ?? "").Contains(fragment, StringComparison.OrdinalIgnoreCase);

    /// <summary>$value -like 'prefix*' (case-insensitive).</summary>
    public static bool Starts(string? value, string prefix) => (value ?? "").StartsWith(prefix, StringComparison.OrdinalIgnoreCase);

    /// <summary>The refusal text of an action (the innermost message), or "" when it is accepted.</summary>
    public static string Refusal(Action action)
    {
        try { action(); }
        catch (Exception ex) { return Message(ex); }
        return "";
    }

    public static string Message(Exception ex)
    {
        while (ex is System.Reflection.TargetInvocationException && ex.InnerException != null) ex = ex.InnerException;
        return ex.Message;
    }

    /// <summary>$x.PSObject.Copy(): a shallow copy of a record.</summary>
    public static PlainObject Copy(object record)
    {
        var plain = record as PlainObject ?? throw new InvalidOperationException("not a plain record: " + record?.GetType().FullName);
        var copy = new PlainObject();
        foreach (var entry in plain.Entries) copy.Add(entry.Key, entry.Value);
        return copy;
    }

    /// <summary>Test-Permutation: the schedule holds each prepared chunk exactly once.</summary>
    public static bool Permutation(object schedule, IEnumerable<object> prepared)
    {
        var expected = Canonical(prepared).OrderBy(k => k, StringComparer.Ordinal).ToArray();
        var actual = Keys(schedule).OrderBy(k => k, StringComparer.Ordinal).ToArray();
        return expected.Length == actual.Length && actual.Distinct(StringComparer.Ordinal).Count() == actual.Length && J(expected) == J(actual);
    }
}

/// <summary>pwsh launches of the still-PowerShell entries: argv, both streams, a bounded wait, the tree killed on timeout.</summary>
internal static class Pwsh
{
    public sealed record Result(int Code, string Text);

    public static async Task<Result> Run(IEnumerable<string> arguments, TimeSpan timeout, string? workingDirectory = null)
    {
        var start = new ProcessStartInfo("pwsh")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        if (!string.IsNullOrEmpty(workingDirectory)) start.WorkingDirectory = workingDirectory;
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("pwsh did not start");
        using var cancel = new CancellationTokenSource(timeout);
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        try
        {
            await process.WaitForExitAsync(cancel.Token);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            return new Result(-1, "pwsh timed out after " + timeout + "\n" + await stdout + await stderr);
        }
        return new Result(process.ExitCode, await stdout + await stderr);
    }
}

/// <summary>
/// The unreadable-timing custody (Enter-SeedUnreadableTiming and its platform seam): Unix modes where they exist; elsewhere
/// a current-user deny ACL, or a counted not-applicable line. UnixModesSupported = false forces the Windows branch;
/// WindowsAcl replaces the ACL denial (its restore, or null when not applied).
/// </summary>
internal sealed class SeedCustody
{
    public const string NotApplicableLine = "SEED-CUSTODY not-applicable: Windows ACL custody is not checkable on this platform";

    /// <summary>$script:NightlySeedPlatformSeam.UnixModesSupported (null: the platform decides).</summary>
    public bool? UnixModesSupported { get; set; }

    /// <summary>$script:NightlySeedPlatformSeam.WindowsAcl.</summary>
    public Func<string, Action?>? WindowsAcl { get; set; }

    public int UnixModeCalls { get; set; }

    public bool WindowsBranchRan { get; set; }

    public int NotApplicable { get; set; }

    public string Note { get; set; } = "";

    public string Branch { get; set; } = "";

    /// <summary>Where Write-Host goes.</summary>
    public TextWriter Host { get; set; } = Console.Out;

    /// <summary>Test-SeedUnixModesSupported.</summary>
    public bool UnixModes => UnixModesSupported ?? Environment.GetEnvironmentVariable("OS") != "Windows_NT";

    /// <summary>Write-SeedCustodyNotApplicable.</summary>
    private void WriteNotApplicable()
    {
        Host.WriteLine(NotApplicableLine);
        Note = NotApplicableLine;
        NotApplicable++;
    }

    /// <summary>Use-SeedUnixFileMode: reads (mode null) or sets the file's Unix mode; refuses where Unix modes are not supported.</summary>
    public UnixFileMode? UseUnixFileMode(string path, UnixFileMode? mode)
    {
        if (!UnixModes) throw new InvalidOperationException("SEED-CUSTODY unix-mode-on-unsupported-platform");
        if (OperatingSystem.IsWindows()) throw new InvalidOperationException("SEED-CUSTODY unix-mode-on-unsupported-platform");
        UnixModeCalls++;
        if (mode == null) return File.GetUnixFileMode(path);
        File.SetUnixFileMode(path, mode.Value);
        return null;
    }

    /// <summary>Enter-SeedUnreadableTiming: makes the file unreadable and returns its restore, or null (not applicable).</summary>
    public Action? EnterUnreadableTiming(string path)
    {
        if (UnixModes)
        {
            Branch = "unix-mode";
            var mode = UseUnixFileMode(path, null)!.Value;
            UseUnixFileMode(path, UnixFileMode.None);
            return () => UseUnixFileMode(path, mode);
        }
        Branch = "windows-acl";
        WindowsBranchRan = true;
        if (WindowsAcl != null)
        {
            var applied = WindowsAcl(path);
            if (applied != null) return applied;
            WriteNotApplicable();
            return null;
        }
        var restore = OperatingSystem.IsWindows() ? DenyWindowsCurrentUserRead(path) : null;
        if (restore != null) return restore;
        WriteNotApplicable();
        return null;
    }

    /// <summary>Deny-SeedWindowsCurrentUserRead: a Read deny rule for the current user, proved by a failed read; its removal.</summary>
    [SupportedOSPlatform("windows")]
    private static Action? DenyWindowsCurrentUserRead(string path)
    {
        var sid = WindowsIdentity.GetCurrent().User;
        if (sid == null) return null;
        var file = new FileInfo(path);
        var acl = file.GetAccessControl(AccessControlSections.Access);
        var rule = new FileSystemAccessRule(sid, FileSystemRights.Read, AccessControlType.Deny);
        if (rule.IdentityReference.Value != sid.Value) throw new InvalidOperationException("SEED-CUSTODY acl-not-current-user");
        acl.AddAccessRule(rule);
        file.SetAccessControl(acl);
        var readable = true;
        try { File.ReadAllBytes(path); }
        catch (UnauthorizedAccessException) { readable = false; }
        catch (IOException) { readable = false; }
        if (readable)
        {
            acl.RemoveAccessRule(rule);
            file.SetAccessControl(acl);
            throw new InvalidOperationException("SEED-CUSTODY windows-acl-did-not-deny-current-user");
        }
        return () =>
        {
            acl.RemoveAccessRule(rule);
            file.SetAccessControl(acl);
        };
    }
}
