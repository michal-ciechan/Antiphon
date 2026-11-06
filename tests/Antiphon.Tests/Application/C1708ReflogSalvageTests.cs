using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Git;
using Antiphon.TestSupport;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-1708 (operator 2026-11-05, option b with salvage refs). The automatic desktop release (S4a) removes the tree's
/// HEAD reflog and the branch reflog. A commit only those reflogs reach (history rewritten locally, never pushed)
/// gets <c>refs/antiphon/salvage/&lt;taskId&gt;/&lt;sha&gt;</c> before anything moves; a salvage that cannot be
/// written refuses the removal; the off switch writes no ref; the tick prunes a salvage ref only once it is older
/// than <c>WorktreeLegacyRelease:SalvageRefRetentionDays</c>. Review 9222cb00 B-1..B-4: a reflog's old ids, the retention
/// counted from a finished release under the repository lease, the switch at each prune delete, and salvage-name
/// collisions.
/// </summary>
[Category("Integration")]
[ParallelLimiter<GitProcessSpawnLimit>]
[Timeout(180_000)]
public sealed class C1708ReflogSalvageTests
{
    private const string Prefix = "refs/antiphon/salvage/";

    [Test, Timeout(40_000)]
    public async Task C1737_HeadReflogOnlyCommitSurvivesRemoval(CancellationToken ct) =>
        await ExclusiveSourceSurvivesAsync("W-U1H", branchOnly: false, ct);

    [Test, Timeout(40_000)]
    public async Task C1737_BranchReflogOnlyCommitSurvivesRemoval(CancellationToken ct) =>
        await ExclusiveSourceSurvivesAsync("W-U1B", branchOnly: true, ct);

    [Test, Timeout(60_000)]
    public async Task C1737_MissingVerifiedSalvageRefKeepsTree(CancellationToken ct)
    {
        await using var s = await SalvageSourceAsync("W-U3", branchOnly: false, oldOnly: false, ct);
        var wrote = false;
        var cut = 0;
        var verified = false;
        s.World.Fixture.Git.AfterCommand = async (directory, args, result) =>
        {
            s.Observe(directory, args, result);
            if (s.Match(directory, args, s.World.Fixture.Repository, s.CreateArguments) && result.Succeeded)
            {
                (await s.RequiredAsync(s.World.Fixture.Repository, "show-ref", "--verify", "--hash", s.Salvage)).Trim().ShouldBe(s.Lost);
                wrote = true;
            }
            if (wrote && cut == 1 && s.Match(directory, args, s.World.Fixture.Repository, s.VerifyArguments))
            {
                result.ExitCode.ShouldBe(0, "the real final enumeration succeeded");
                LogLines(result.Output).ShouldNotContain(s.Salvage + " " + s.Lost);
                verified = true;
                Console.WriteLine("reached:W-U3:successful-verify-missing-ref");
            }
        };
        s.World.Fixture.Git.BeforeCommand = async (directory, args) =>
        {
            if (wrote && cut == 0 && s.Match(directory, args, s.World.Fixture.Repository, s.VerifyArguments))
            {
                await s.RequiredAsync(s.World.Fixture.Repository, "update-ref", "--no-deref", "-d", s.Salvage, s.Lost);
                (await s.RunAsync(s.World.Fixture.Repository, "show-ref", "--verify", "--hash", "--quiet", s.Salvage)).ExitCode.ShouldBe(1);
                cut++;
            }
            return null;
        };
        DesktopLegacyReleasePolicyService.TickResult tick;
        try { tick = await s.World.TickAsync().WaitAsync(ct); }
        finally { s.ClearHooks(); await s.CaptureAsync(); }

        Directory.Exists(s.Task.Path).ShouldBeTrue("preserved:W-U3:source-tree");
        await s.AssertCustodyAsync(s.HeadBytes, s.HeadLog);
        (await s.RunAsync(s.World.Fixture.Repository, "show-ref", "--verify", "--hash", "--quiet", s.Salvage)).ExitCode.ShouldBe(1);
        wrote.ShouldBeTrue();
        cut.ShouldBe(1);
        verified.ShouldBeTrue();
        await s.AssertRefusedAsync(tick);
    }

    [Test, Timeout(40_000)]
    [NativeLinuxOnly("Linux-only by construction: mode-000 raw-reflog denial requires an unprivileged Linux process; Windows needs separate permission evidence.")]
    public async Task C1737_UnreadableRawReflogKeepsTree(CancellationToken ct) =>
        await RawFaultKeepsTreeAsync("W-U5R", "unreadable", ct);

    [Test, Timeout(40_000)]
    [Arguments("short-fields"), Arguments("bad-old"), Arguments("bad-new")]
    public async Task C1737_MalformedRawReflogKeepsTree(string variant, CancellationToken ct) =>
        await RawFaultKeepsTreeAsync("W-U5P", variant, ct);

    [Test, Timeout(40_000)]
    public async Task C1737_MissingRawReflogKeepsTree(CancellationToken ct) =>
        await RawFaultKeepsTreeAsync("W-U5M", "missing", ct);

    [Test, Timeout(40_000)]
    [Arguments("empty"), Arguments("all-zero-ids")]
    public async Task C1737_EmptyRawReflogKeepsTree(string variant, CancellationToken ct) =>
        await RawFaultKeepsTreeAsync("W-U5E", variant, ct);

    private static async Task ExclusiveSourceSurvivesAsync(string caseId, bool branchOnly, CancellationToken ct)
    {
        await using var s = await SalvageSourceAsync(caseId, branchOnly, oldOnly: false, ct);
        s.World.Fixture.Git.AfterCommand = (directory, args, result) =>
        {
            s.Observe(directory, args, result);
            return Task.CompletedTask;
        };
        try { await s.World.TickAsync().WaitAsync(ct); }
        finally { s.ClearHooks(); await s.CaptureAsync(); }

        // Removal is a prerequisite: a retained reflog must not mask an omitted source tuple.
        Directory.Exists(s.Task.Path).ShouldBeFalse("the source really was removed");
        Directory.Exists(WorktreeSetAside.SetAsidePath(s.Task.Path)).ShouldBeFalse("no set-aside custody remains");
        (await s.RegisteredAsync()).ShouldBeFalse();
        (await s.RunAsync(s.World.Fixture.Repository, "show-ref", "--verify", "--hash", "--quiet", s.Task.FullRef)).ExitCode.ShouldBe(1);
        File.Exists(s.HeadLog).ShouldBeFalse();
        File.Exists(s.BranchLog).ShouldBeFalse();
        var salvage = await s.RunAsync(s.World.Fixture.Repository, "show-ref", "--verify", "--hash", s.Salvage);
        salvage.Succeeded.ShouldBeTrue("preserved:" + caseId + ":salvage-ref-present");
        salvage.Output.Trim().ShouldBe(s.Lost);
        (await s.RunAsync(s.World.Fixture.Repository, "symbolic-ref", "-q", s.Salvage)).ExitCode.ShouldBe(1, "the salvage root is direct");
        LogLines(await s.RequiredAsync(s.World.Fixture.Repository, "rev-list", "--all")).ShouldContain(s.Lost);
        await s.World.AssertRetiredAsync(s.Task, s.Origin);
        (await s.RequiredAsync(s.World.Fixture.Remote, "show-ref")).ShouldBe(s.Origin);
        s.Observed.ShouldContain(x => x.Directory == s.World.Fixture.Repository && x.Arguments.SequenceEqual(s.CreateArguments) && x.Exit == 0);
        s.Observed.Count(x => x.Directory == s.World.Fixture.Repository && x.Arguments.SequenceEqual(s.VerifyArguments) && x.Exit == 0).ShouldBe(2);
        s.Observed.ShouldContain(x => x.Arguments.Length > 0 && x.Arguments[0] == "rev-list" && x.Arguments.Contains(s.Lost) && LogLines(x.Output).Contains(s.Lost));
        foreach (var (directory, args) in s.SourceCalls)
            s.Observed.ShouldContain(x => LandingGit.PathsEqual(x.Directory, directory) && x.Arguments.SequenceEqual(args) && x.Exit == 0);
    }

    private static async Task RawFaultKeepsTreeAsync(string caseId, string variant, CancellationToken ct)
    {
        await using var s = await SalvageSourceAsync(caseId, branchOnly: false, oldOnly: true, ct);
        var listed = false;
        var cuts = 0;
        var installed = s.HeadBytes;
        var evidence = s.HeadLog;
        UnixFileMode? originalMode = null;
        s.World.Fixture.Git.AfterCommand = async (directory, args, result) =>
        {
            s.Observe(directory, args, result);
            if (s.Match(directory, args, s.Task.Path, s.HeadShowArguments))
            {
                result.Succeeded.ShouldBeTrue();
                LogLines(result.Output).ShouldBe([s.Pushed]);
                listed = true;
            }
            if (!listed || cuts != 0 || !s.Match(directory, args, s.Task.Path, s.HeadPathArguments)) return;
            result.Succeeded.ShouldBeTrue();
            result.Output.Trim().ShouldBe(s.HeadLog);
            using var phase = TestTiming.BeginPhase(TestPhaseKind.Filesystem, "raw-reflog-fault");
            if (variant == "unreadable")
            {
                OperatingSystem.IsLinux().ShouldBeTrue("fixture unavailable outside Linux");
                var uid = (await File.ReadAllLinesAsync("/proc/self/status", ct)).Single(l => l.StartsWith("Uid:", StringComparison.Ordinal))
                    .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                uint.Parse(uid[2], System.Globalization.CultureInfo.InvariantCulture).ShouldNotBe(0u, "fixture unavailable for root");
                originalMode = File.GetUnixFileMode(s.HeadLog);
                File.SetUnixFileMode(s.HeadLog, UnixFileMode.None);
                File.Exists(s.HeadLog).ShouldBeTrue();
                Exception? denial = null;
                try { await File.ReadAllTextAsync(s.HeadLog, ct); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { denial = ex; }
                denial.ShouldNotBeNull("the same process must really be unable to read mode-000");
                Console.WriteLine("denied:" + denial.GetType().Name + ":euid=" + uid[2]);
            }
            else if (variant == "missing")
            {
                evidence = Path.Combine(s.World.Fixture.Root, "c1737-displaced-head.log");
                File.Move(s.HeadLog, evidence);
                File.Exists(s.HeadLog).ShouldBeFalse();
                (await File.ReadAllBytesAsync(evidence, ct)).ShouldBe(s.HeadBytes);
            }
            else
            {
                var bad = variant switch
                {
                    "short-fields" => s.Lost + "\n",
                    "bad-old" => LogRecord("not-an-oid", s.Lost),
                    "bad-new" => LogRecord(s.Lost, "not-an-oid"),
                    "empty" => "",
                    "all-zero-ids" => LogRecord(s.Zero, s.Zero),
                    _ => throw new InvalidOperationException("unknown raw-log variant"),
                };
                if (caseId == "W-U5P")
                {
                    var fields = LogLines(bad).Single().Split(' ', 3);
                    (fields.Length < 3).ShouldBe(variant == "short-fields");
                    if (fields.Length == 3)
                    {
                        IsFixtureOid(fields[0], s.Pushed.Length).ShouldBe(variant != "bad-old");
                        IsFixtureOid(fields[1], s.Pushed.Length).ShouldBe(variant != "bad-new");
                    }
                    bad += LogRecord(s.Zero, s.Pushed);
                    RawIds(LogRecord(s.Zero, s.Pushed), s.Pushed.Length).ShouldBe([s.Zero, s.Pushed]);
                }
                if (variant == "all-zero-ids") RawIds(bad, s.Pushed.Length).ShouldBe([s.Zero, s.Zero]);
                installed = Encoding.UTF8.GetBytes(bad);
                await File.WriteAllBytesAsync(s.HeadLog, installed, ct);
            }
            cuts++;
            Console.WriteLine("reached:" + caseId + ":after-list-before-raw-read:" + variant);
        };
        DesktopLegacyReleasePolicyService.TickResult tick;
        try { tick = await s.World.TickAsync().WaitAsync(ct); }
        finally
        {
            s.ClearHooks();
            if (originalMode is { } mode && OperatingSystem.IsLinux() && File.Exists(s.HeadLog))
                File.SetUnixFileMode(s.HeadLog, mode);
            await s.CaptureAsync(evidence);
        }

        Directory.Exists(s.Task.Path).ShouldBeTrue("preserved:" + caseId + ":source-tree");
        await s.AssertCustodyAsync(installed, evidence);
        if (variant == "missing") File.Exists(s.HeadLog).ShouldBeFalse();
        cuts.ShouldBe(1);
        await s.AssertRefusedAsync(tick);
    }

    private static string[] LogLines(string text) => text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    private static string LogRecord(string oldId, string newId) =>
        oldId + " " + newId + " C1737 Fixture <fixture@example.invalid> 1700000000 +0000\tfixture-message\n";
    private static bool IsFixtureOid(string value, int width) => value.Length == width && value.All(char.IsAsciiHexDigit);
    private static string[] RawIds(string text, int width) => LogLines(text).SelectMany(line =>
    {
        var fields = line.Split(' ', 3);
        fields.Length.ShouldBe(3);
        IsFixtureOid(fields[0], width).ShouldBeTrue();
        IsFixtureOid(fields[1], width).ShouldBeTrue();
        return fields.Take(2);
    }).ToArray();

    private static async Task<SalvageSource> SalvageSourceAsync(string caseId, bool branchOnly, bool oldOnly, CancellationToken ct)
    {
        var w = await DesktopLegacyWorld.CreateAsync();
        try
        {
            var a = await w.AddTaskAsync(ageHours: 60);
            var reader = new LandingGitFixture.FixtureGit(Path.Combine(w.Fixture.Root, "home"), w.Fixture.TaskId);
            var s = new SalvageSource(w, a, reader, caseId, ct);
            s.Pushed = (await s.RequiredAsync(a.Path, "rev-parse", "--verify", "HEAD^{commit}")).Trim();
            s.Lost = await RewriteAsync(w, a);
            s.Lost.ShouldNotBe(s.Pushed);
            (await s.RequiredAsync(w.Fixture.Repository, "cat-file", "-t", s.Lost)).Trim().ShouldBe("commit");
            s.HeadLog = await s.ResolveLogAsync(a.Path, "HEAD");
            s.BranchLog = await s.ResolveLogAsync(w.Fixture.Repository, a.FullRef);
            s.HeadLog.ShouldNotBe(s.BranchLog);
            var selected = oldOnly ? LogRecord(s.Lost, s.Pushed) : LogRecord(s.Zero, s.Lost) + LogRecord(s.Lost, s.Pushed);
            var other = LogRecord(s.Zero, s.Pushed);
            await File.WriteAllTextAsync(s.HeadLog, branchOnly ? other : selected, new UTF8Encoding(false), ct);
            await File.WriteAllTextAsync(s.BranchLog, branchOnly ? selected : other, new UTF8Encoding(false), ct);
            s.HeadBytes = await File.ReadAllBytesAsync(s.HeadLog, ct);
            s.BranchBytes = await File.ReadAllBytesAsync(s.BranchLog, ct);
            var headIds = RawIds(Encoding.UTF8.GetString(s.HeadBytes), s.Pushed.Length);
            var branchIds = RawIds(Encoding.UTF8.GetString(s.BranchBytes), s.Pushed.Length);
            (branchOnly ? branchIds : headIds).ShouldContain(s.Lost);
            (branchOnly ? headIds : branchIds).ShouldNotContain(s.Lost);
            var headShown = LogLines(await s.RequiredAsync(a.Path, s.HeadShowArguments));
            var branchShown = LogLines(await s.RequiredAsync(w.Fixture.Repository, s.BranchShowArguments));
            if (oldOnly) { headShown.ShouldBe([s.Pushed]); branchShown.ShouldBe([s.Pushed]); }
            else
            {
                (branchOnly ? branchShown : headShown).ShouldContain(s.Lost);
                (branchOnly ? headShown : branchShown).ShouldBe([s.Pushed]);
            }
            (await s.RequiredAsync(a.Path, "rev-parse", "--verify", "HEAD^{commit}")).Trim().ShouldBe(s.Pushed);
            (await s.RequiredAsync(w.Fixture.Repository, "show-ref", "--verify", "--hash", a.FullRef)).Trim().ShouldBe(s.Pushed);
            LogLines(await s.RequiredAsync(w.Fixture.Repository, "rev-list", "--all")).ShouldNotContain(s.Lost);
            (await s.RequiredAsync(w.Fixture.Repository, "rev-list", "--all", "--ancestry-path", s.Lost + "..")).ShouldBeEmpty("no ref reaches a descendant of L");
            // Audit every other existing raw log, including the main checkout and fixture task.
            var common = (await s.RequiredAsync(w.Fixture.Repository, "rev-parse", "--path-format=absolute", "--git-common-dir")).Trim();
            foreach (var log in Directory.EnumerateFiles(common, "*", SearchOption.AllDirectories)
                         .Where(p => p.Contains(Path.DirectorySeparatorChar + "logs" + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                             && p != s.HeadLog && p != s.BranchLog))
                RawIds(await File.ReadAllTextAsync(log, ct), s.Pushed.Length).ShouldNotContain(s.Lost);
            s.Origin = await s.RequiredAsync(w.Fixture.Remote, "show-ref");
            s.Snapshot = await w.SnapshotAsync(a.Path);
            Console.WriteLine("reached:" + caseId + ":exclusive-" + (branchOnly ? "branch" : "head") + (oldOnly ? ":old-id-only" : ""));
            return s;
        }
        catch { await w.DisposeAsync(); throw; }
    }

    /// <summary>R2-local independent Git observations; never installs hooks on the reader.</summary>
    private sealed class SalvageSource(DesktopLegacyWorld world, DesktopLegacyWorld.Handle task,
        LandingGitFixture.FixtureGit reader, string caseId, CancellationToken ct) : IAsyncDisposable
    {
        public DesktopLegacyWorld World => world;
        public DesktopLegacyWorld.Handle Task => task;
        public string Pushed { get; set; } = "";
        public string Lost { get; set; } = "";
        public string HeadLog { get; set; } = "";
        public string BranchLog { get; set; } = "";
        public byte[] HeadBytes { get; set; } = [];
        public byte[] BranchBytes { get; set; } = [];
        public string Origin { get; set; } = "";
        public string Snapshot { get; set; } = "";
        public string Zero => new('0', Pushed.Length);
        public string Salvage => Prefix + task.Id.ToString("D") + "/" + Lost;
        public string[] HeadShowArguments => ["reflog", "show", "--no-abbrev", "--format=%H", "HEAD"];
        public string[] BranchShowArguments => ["reflog", "show", "--no-abbrev", "--format=%H", task.FullRef];
        public string[] HeadPathArguments => ["rev-parse", "--path-format=absolute", "--git-path", "logs/HEAD"];
        public string[] VerifyArguments => ["for-each-ref", "--format=%(refname) %(objectname) %(symref)", Prefix + task.Id.ToString("D") + "/"];
        public string[] CreateArguments => ["update-ref", "--no-deref", "--create-reflog", "-m", "antiphon salvage " + task.Id.ToString("D"), Salvage, Lost, Zero];
        public (string Directory, string[] Arguments)[] SourceCalls =>
        [
            (task.Path, HeadShowArguments), (task.Path, ["rev-parse", "--verify", "HEAD^{commit}"]),
            (world.Fixture.Repository, BranchShowArguments), (task.Path, HeadPathArguments),
            (world.Fixture.Repository, ["rev-parse", "--path-format=absolute", "--git-path", "logs/" + task.FullRef]),
        ];
        public List<(string Directory, string[] Arguments, int Exit, string Output)> Observed { get; } = [];
        public bool Match(string directory, IReadOnlyList<string> args, string expectedDirectory, string[] expected) =>
            LandingGit.PathsEqual(directory, expectedDirectory) && args.SequenceEqual(expected);
        public void Observe(string directory, IReadOnlyList<string> args, LandingGitResult result)
        {
            Observed.Add((directory, args.ToArray(), result.ExitCode, result.Output));
            Console.WriteLine("source-call:" + JsonSerializer.Serialize(new { directory, args, result.ExitCode, result.Output }));
        }
        public async Task<LandingGitResult> RunAsync(string directory, params string[] args)
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
            deadline.CancelAfter(TimeSpan.FromSeconds(10));
            return await reader.RunAsync(directory, args, deadline.Token);
        }
        public async Task<string> RequiredAsync(string directory, params string[] args)
        {
            var result = await RunAsync(directory, args);
            result.Succeeded.ShouldBeTrue("independent Git: " + string.Join(" ", args) + ": " + result.Diagnostic);
            return result.Output;
        }
        public async Task<string> ResolveLogAsync(string directory, string name)
        {
            var path = (await RequiredAsync(directory, "rev-parse", "--path-format=absolute", "--git-path", "logs/" + name)).Trim();
            Path.IsPathFullyQualified(path).ShouldBeTrue();
            path.ShouldNotContain('\n');
            File.Exists(path).ShouldBeTrue();
            var common = (await RequiredAsync(world.Fixture.Repository, "rev-parse", "--path-format=absolute", "--git-common-dir")).Trim();
            path.StartsWith(Path.TrimEndingDirectorySeparator(common) + Path.DirectorySeparatorChar, StringComparison.Ordinal).ShouldBeTrue("raw log is fixture-owned");
            return path;
        }
        public async Task<bool> RegisteredAsync() => LandingGit.ParseRegistrations(
            await RequiredAsync(world.Fixture.Repository, "worktree", "list", "--porcelain", "-z"))
            .Any(r => LandingGit.PathsEqual(r.Path, task.Path));
        public void ClearHooks() { world.Fixture.Git.BeforeCommand = null; world.Fixture.Git.AfterCommand = null; }
        public async Task AssertCustodyAsync(byte[] expected, string evidence)
        {
            (await world.SnapshotAsync(task.Path)).ShouldBe(Snapshot);
            (await RegisteredAsync()).ShouldBeTrue();
            (await RequiredAsync(world.Fixture.Repository, "show-ref", "--verify", "--hash", task.FullRef)).Trim().ShouldBe(Pushed);
            (await RequiredAsync(world.Fixture.Remote, "show-ref")).ShouldBe(Origin);
            (await File.ReadAllBytesAsync(evidence, ct)).ShouldBe(expected);
            (await File.ReadAllBytesAsync(BranchLog, ct)).ShouldBe(BranchBytes);
        }
        public async Task AssertRefusedAsync(DesktopLegacyReleasePolicyService.TickResult tick)
        {
            (await world.RowAsync(tick.RunId, task)).ReasonCode.ShouldBe("reflog_salvage_failed");
            (await world.ReleasesAsync(task)).Single().State.ShouldNotBe(WorktreeRetirementState.Complete);
        }
        public async Task CaptureAsync(string? evidence = null)
        {
            // Emit physical state before the preservation assertion can abort a mutant result.
            Console.WriteLine("post-tick:" + JsonSerializer.Serialize(new
            {
                caseId, sourceExists = Directory.Exists(task.Path),
                asideExists = Directory.Exists(WorktreeSetAside.SetAsidePath(task.Path)),
                registered = await RegisteredAsync(),
                sourceRef = await RunAsync(world.Fixture.Repository, "show-ref", "--verify", "--hash", task.FullRef),
                headLog = File.Exists(HeadLog), branchLog = File.Exists(BranchLog),
                evidenceExists = File.Exists(evidence ?? HeadLog),
                evidenceHash = File.Exists(evidence ?? HeadLog) ? Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(evidence ?? HeadLog, ct))) : null,
                refs = await RequiredAsync(world.Fixture.Repository, "for-each-ref", "--format=%(refname) %(objectname) %(symref)", Prefix),
                reachable = await RequiredAsync(world.Fixture.Repository, "rev-list", "--all"),
            }));
        }
        public ValueTask DisposeAsync() { ClearHooks(); return world.DisposeAsync(); }
    }

    /// <summary>(i) The rewritten commit is still reachable through its salvage ref once the tree is retired.</summary>
    [Test]
    public async Task Released_tree_keeps_its_reflog_only_commit_under_a_salvage_ref()
    {
        await using var w = await DesktopLegacyWorld.CreateAsync();
        var a = await w.AddTaskAsync(ageHours: 60);
        var lost = await RewriteAsync(w, a);
        var control = await w.AddTaskAsync(ageHours: 60);
        var origin = await w.OriginRefsAsync();

        await w.TickAsync();

        await w.AssertRetiredAsync(a, origin);
        await w.AssertRetiredAsync(control, origin);
        (await SalvageRefsAsync(w)).ShouldBe([Prefix + a.Id.ToString("D") + "/" + lost],
            "one salvage ref, for the rewritten commit only (the pushed control has none)");
        (await w.ReadAsync(w.Fixture.Repository, "rev-parse", "--verify", Prefix + a.Id.ToString("D") + "/" + lost))
            .Trim().ShouldBe(lost);
        (await w.ReadAsync(w.Fixture.Repository, "rev-list", "--all")).ShouldContain(lost,
            Case.Sensitive, "the commit is reachable from a ref, not only from a deleted reflog");
    }

    /// <summary>(ii) A salvage ref that cannot be written (a file where its directory must go) refuses the removal.</summary>
    [Test]
    public async Task Salvage_failure_refuses_the_removal()
    {
        await using var w = await DesktopLegacyWorld.CreateAsync();
        var a = await w.AddTaskAsync(ageHours: 60);
        var lost = await RewriteAsync(w, a);
        var origin = await w.OriginRefsAsync();
        await w.Fixture.RequiredAsync(w.Fixture.Repository, "update-ref", Prefix + a.Id.ToString("D"), lost);
        var before = await w.SnapshotAsync(a.Path);

        var tick = await w.TickAsync();

        (await w.SnapshotAsync(a.Path)).ShouldBe(before, "the tree is byte-intact");
        (await w.RegisteredAsync(a.Path)).ShouldBeTrue("the registration is intact");
        (await w.BranchExistsAsync(a)).ShouldBeTrue("the branch and its reflog are intact");
        (await w.OriginRefsAsync()).ShouldBe(origin);
        (await w.ReadAsync(a.Path, "reflog", "show", "--format=%H", "HEAD")).ShouldContain(lost, Case.Sensitive);
        var row = await w.RowAsync(tick.RunId, a);
        row.Outcome.ShouldBe(nameof(WorktreeResidueCandidateOutcome.Refused));
        row.ReasonCode.ShouldBe("reflog_salvage_failed");
        (await w.ReleasesAsync(a)).Single().State.ShouldNotBe(WorktreeRetirementState.Complete);
    }

    /// <summary>(iii) Disabled, dry run and preview write no salvage ref; with the switch on the same tree gets one.</summary>
    [Test]
    public async Task Off_switch_writes_no_salvage_ref()
    {
        await using var w = await DesktopLegacyWorld.CreateAsync();
        var a = await w.AddTaskAsync(ageHours: 60);
        var lost = await RewriteAsync(w, a);
        var origin = await w.OriginRefsAsync();
        var before = await w.SnapshotAsync(a.Path);

        w.Settings.Value.Enabled = false;
        await w.TickAsync();
        w.Settings.Value.Enabled = true;
        w.Settings.Value.Execute = false;
        await w.TickAsync();
        await w.TickAsync(preview: true);
        w.Settings.Value.Execute = true;
        await w.TickAsync(preview: true);

        (await SalvageRefsAsync(w)).ShouldBeEmpty("no salvage side effect while the switch is off or in a preview");
        await w.AssertIntactAsync(a, before);

        await w.TickAsync();
        await w.AssertRetiredAsync(a, origin);
        (await SalvageRefsAsync(w)).ShouldBe([Prefix + a.Id.ToString("D") + "/" + lost], "the switch on salvages (control)");
    }

    /// <summary>(iii) Execute turned off as the salvage ref write starts: no ref is written and nothing is removed.</summary>
    [Test]
    public async Task Late_switch_at_the_salvage_write_writes_no_ref_and_keeps_the_tree()
    {
        await using var w = await DesktopLegacyWorld.CreateAsync();
        var a = await w.AddTaskAsync(ageHours: 60);
        var lost = await RewriteAsync(w, a);
        var origin = await w.OriginRefsAsync();
        var before = await w.SnapshotAsync(a.Path);
        var fired = 0;
        w.Fixture.Git.BeforeCommand = (_, args) =>
        {
            if (args.Count > 0 && args[0] == "update-ref" && args.Any(x => x.StartsWith(Prefix, StringComparison.Ordinal)))
            {
                fired++;
                w.Settings.Value.Execute = false;
            }
            return Task.FromResult<Antiphon.Server.Application.Dtos.LandingGitResult?>(null);
        };

        var tick = await w.TickAsync();

        w.Fixture.Git.BeforeCommand = null;
        fired.ShouldBe(1, "the salvage write was reached once");
        (await SalvageRefsAsync(w)).ShouldBeEmpty();
        (await w.SnapshotAsync(a.Path)).ShouldBe(before, "the tree is byte-intact");
        (await w.RegisteredAsync(a.Path)).ShouldBeTrue();
        (await w.BranchExistsAsync(a)).ShouldBeTrue();
        (await w.RowAsync(tick.RunId, a)).ReasonCode.ShouldBe(TaskWorktreeRetirementService.LegacyReleaseExecuteOff);

        w.Settings.Value.Execute = true;
        await w.TickAsync();
        await w.AssertRetiredAsync(a, origin);
        (await SalvageRefsAsync(w)).ShouldBe([Prefix + a.Id.ToString("D") + "/" + lost]);
    }

    /// <summary>(iv) The tick keeps a salvage ref younger than the retention (29 of 30 days) and prunes an older one.</summary>
    [Test]
    public async Task Age_pruning_keeps_young_salvage_refs()
    {
        await using var w = await DesktopLegacyWorld.CreateAsync();
        var a = await w.AddTaskAsync(ageHours: 60);
        var lost = await RewriteAsync(w, a);
        await w.TickAsync();
        var salvage = Prefix + a.Id.ToString("D") + "/" + lost;
        (await SalvageRefsAsync(w)).ShouldBe([salvage]);

        w.Clock.Advance(TimeSpan.FromDays(29));
        await w.TickAsync();
        (await SalvageRefsAsync(w)).ShouldBe([salvage], "29 days is younger than the 30 day retention");

        w.Settings.Value.Execute = false;
        w.Clock.Advance(TimeSpan.FromDays(2));
        await w.TickAsync();
        (await SalvageRefsAsync(w)).ShouldBe([salvage], "a dry run prunes nothing");

        w.Settings.Value.Execute = true;
        await w.TickAsync();
        (await SalvageRefsAsync(w)).ShouldBeEmpty("31 days is past the retention");
    }

    /// <summary>
    /// Review 9222cb00 B-1. The commit entry is gone from both reflogs, so the rewritten commit survives only as the
    /// OLD id of the reset entry; it still gets its salvage ref once the tree is retired.
    /// </summary>
    [Test]
    public async Task Commit_kept_only_as_a_reflog_old_id_gets_a_salvage_ref()
    {
        await using var w = await DesktopLegacyWorld.CreateAsync();
        var a = await w.AddTaskAsync(ageHours: 60);
        var lost = await RewriteAsync(w, a);
        await w.Fixture.RequiredAsync(a.Path, "reflog", "delete", "HEAD@{1}");
        await w.Fixture.RequiredAsync(a.Path, "reflog", "delete", a.FullRef + "@{1}");
        (await w.ReadAsync(a.Path, "reflog", "show", "--no-abbrev", "--format=%H", "HEAD")).ShouldNotContain(lost, Case.Sensitive);
        (await w.ReadAsync(w.Fixture.Repository, "reflog", "show", "--no-abbrev", "--format=%H", a.FullRef))
            .ShouldNotContain(lost, Case.Sensitive);
        (await w.ReadAsync(w.Fixture.Repository, "rev-list", "--reflog", "--all")).ShouldContain(lost, Case.Sensitive,
            "the reset entry's old id still protects the commit");
        var origin = await w.OriginRefsAsync();

        await w.TickAsync();

        await w.AssertRetiredAsync(a, origin);
        (await SalvageRefsAsync(w)).ShouldBe([Prefix + a.Id.ToString("D") + "/" + lost]);
        (await w.ReadAsync(w.Fixture.Repository, "rev-list", "--all")).ShouldContain(lost, Case.Sensitive);
    }

    /// <summary>
    /// Review 9222cb00 B-2. The first attempt writes the salvage ref and then refuses; the retry 31 days later
    /// finishes. The retention counts from that finished release, so the same tick keeps the ref, and so does 29
    /// days later; 31 days after the completion it is pruned.
    /// </summary>
    [Test]
    public async Task Aged_retry_keeps_its_salvage_ref_for_a_retention_after_completion()
    {
        await using var w = await DesktopLegacyWorld.CreateAsync();
        var a = await w.AddTaskAsync(ageHours: 60);
        var lost = await RewriteAsync(w, a);
        var origin = await w.OriginRefsAsync();
        var salvage = Prefix + a.Id.ToString("D") + "/" + lost;
        var fired = FailVerifyAfterWrite(w, a);

        var first = await w.TickAsync();

        w.Fixture.Git.BeforeCommand = null;
        fired().ShouldBe(1, "the verification after the salvage write failed once");
        (await w.RowAsync(first.RunId, a)).ReasonCode.ShouldBe("reflog_salvage_failed");
        (await SalvageRefsAsync(w)).ShouldBe([salvage], "the refused attempt left its salvage ref");

        w.Clock.Advance(TimeSpan.FromDays(31));
        await w.TickAsync();
        await w.AssertRetiredAsync(a, origin);
        (await SalvageRefsAsync(w)).ShouldBe([salvage], "the retry just finished: its salvage ref is not old");

        w.Clock.Advance(TimeSpan.FromDays(29));
        await w.TickAsync();
        (await SalvageRefsAsync(w)).ShouldBe([salvage], "29 days after the completion");

        w.Clock.Advance(TimeSpan.FromDays(2));
        await w.TickAsync();
        (await SalvageRefsAsync(w)).ShouldBeEmpty("31 days after the completion (control)");
    }

    /// <summary>
    /// Review 9222cb00 B-2. A release that never finished (refused once, then retry exhausted) keeps its salvage ref
    /// past the retention, while a finished release's ref in the same repository and tick is pruned.
    /// </summary>
    [Test]
    public async Task Unfinished_release_keeps_its_salvage_ref_past_the_retention()
    {
        await using var w = await DesktopLegacyWorld.CreateAsync(s => s.MaxRefusedAttempts = 1);
        var a = await w.AddTaskAsync(ageHours: 60);
        var lostA = await RewriteAsync(w, a);
        var c = await w.AddTaskAsync(ageHours: 60);
        var lostC = await RewriteAsync(w, c);
        var origin = await w.OriginRefsAsync();
        var fired = FailVerifyAfterWrite(w, a);

        await w.TickAsync();

        w.Fixture.Git.BeforeCommand = null;
        fired().ShouldBe(1);
        await w.AssertRetiredAsync(c, origin);
        var salvageA = Prefix + a.Id.ToString("D") + "/" + lostA;
        var salvageC = Prefix + c.Id.ToString("D") + "/" + lostC;
        (await SalvageRefsAsync(w)).Order(StringComparer.Ordinal).ShouldBe(new[] { salvageA, salvageC }.Order(StringComparer.Ordinal));

        w.Clock.Advance(TimeSpan.FromDays(31));
        var tick = await w.TickAsync();

        (await w.RowAsync(tick.RunId, a)).ReasonCode.ShouldBe("retry_exhausted");
        (await w.ReleasesAsync(a)).Single().State.ShouldNotBe(WorktreeRetirementState.Complete);
        (await SalvageRefsAsync(w)).ShouldBe([salvageA], "the unfinished release keeps its ref; the finished one is pruned");
        (await w.BranchExistsAsync(a)).ShouldBeTrue();
    }

    /// <summary>Review 9222cb00 B-2. Pruning holds the repository lease the release holds; another holder keeps every ref.</summary>
    [Test]
    public async Task Prune_needs_the_repository_lease()
    {
        await using var w = await DesktopLegacyWorld.CreateAsync();
        var a = await w.AddTaskAsync(ageHours: 60);
        var lost = await RewriteAsync(w, a);
        await w.TickAsync();
        var salvage = Prefix + a.Id.ToString("D") + "/" + lost;
        (await SalvageRefsAsync(w)).ShouldBe([salvage]);
        w.Clock.Advance(TimeSpan.FromDays(31));

        await using (var lease = await w.Harness.LeaseAsync())
        {
            await w.TickAsync();
            (await SalvageRefsAsync(w)).ShouldBe([salvage], "the lease is held elsewhere: nothing is pruned");
        }

        await w.TickAsync();
        (await SalvageRefsAsync(w)).ShouldBeEmpty("with the lease free the aged ref is pruned (control)");
    }

    /// <summary>
    /// Review 9222cb00 B-3. Execute turned off as the first prune delete starts: that delete never runs, no later
    /// one is attempted and every salvage ref stays.
    /// </summary>
    [Test]
    public async Task Late_switch_at_the_prune_delete_keeps_every_salvage_ref()
    {
        await using var w = await DesktopLegacyWorld.CreateAsync();
        var a = await w.AddTaskAsync(ageHours: 60);
        var lostA = await RewriteAsync(w, a);
        var b = await w.AddTaskAsync(ageHours: 60);
        var lostB = await RewriteAsync(w, b);
        await w.TickAsync();
        var expected = new[] { Prefix + a.Id.ToString("D") + "/" + lostA, Prefix + b.Id.ToString("D") + "/" + lostB }
            .Order(StringComparer.Ordinal).ToArray();
        (await SalvageRefsAsync(w)).Order(StringComparer.Ordinal).ShouldBe(expected);
        w.Clock.Advance(TimeSpan.FromDays(31));
        var fired = 0;
        w.Fixture.Git.BeforeCommand = (_, args) =>
        {
            if (args.Count > 0 && args[0] == "update-ref" && args.Contains("-d")
                && args.Any(x => x.StartsWith(Prefix, StringComparison.Ordinal)))
            {
                fired++;
                w.Settings.Value.Execute = false;
            }
            return Task.FromResult<Antiphon.Server.Application.Dtos.LandingGitResult?>(null);
        };

        await w.TickAsync();

        w.Fixture.Git.BeforeCommand = null;
        fired.ShouldBe(1, "the first delete was reached and no later one was attempted");
        (await SalvageRefsAsync(w)).Order(StringComparer.Ordinal).ToArray().ShouldBe(expected, "the switch went off: nothing is pruned");

        w.Settings.Value.Execute = true;
        await w.TickAsync();
        (await SalvageRefsAsync(w)).ShouldBeEmpty("switch on (control)");
    }

    /// <summary>
    /// Review 9222cb00 B-4. A salvage name that is already a symbolic ref to another branch refuses the removal and
    /// never moves that branch.
    /// </summary>
    [Test]
    public async Task Symbolic_salvage_collision_refuses_and_never_moves_another_branch()
    {
        await using var w = await DesktopLegacyWorld.CreateAsync();
        var a = await w.AddTaskAsync(ageHours: 60);
        var lost = await RewriteAsync(w, a);
        var salvage = Prefix + a.Id.ToString("D") + "/" + lost;
        await w.Fixture.RequiredAsync(w.Fixture.Repository, "branch", "precious", "master");
        await w.Fixture.RequiredAsync(w.Fixture.Repository, "symbolic-ref", salvage, "refs/heads/precious");
        var heads = await w.ReadAsync(w.Fixture.Repository, "for-each-ref", "--format=%(refname) %(objectname)", "refs/heads/");
        var precious = await w.ReadAsync(w.Fixture.Repository, "reflog", "show", "--no-abbrev", "--format=%H %gs", "refs/heads/precious");
        var before = await w.SnapshotAsync(a.Path);

        var tick = await w.TickAsync();

        (await w.ReadAsync(w.Fixture.Repository, "for-each-ref", "--format=%(refname) %(objectname)", "refs/heads/"))
            .ShouldBe(heads, "no branch moved");
        (await w.ReadAsync(w.Fixture.Repository, "reflog", "show", "--no-abbrev", "--format=%H %gs", "refs/heads/precious"))
            .ShouldBe(precious);
        (await w.ReadAsync(w.Fixture.Repository, "symbolic-ref", salvage)).Trim().ShouldBe("refs/heads/precious");
        (await w.SnapshotAsync(a.Path)).ShouldBe(before, "the tree is byte-intact");
        (await w.BranchExistsAsync(a)).ShouldBeTrue();
        (await w.RowAsync(tick.RunId, a)).ReasonCode.ShouldBe("reflog_salvage_collision");
    }

    /// <summary>Review 9222cb00 B-4. A direct salvage name holding another object refuses and is left unchanged.</summary>
    [Test]
    public async Task Direct_salvage_collision_refuses_and_keeps_the_existing_ref()
    {
        await using var w = await DesktopLegacyWorld.CreateAsync();
        var a = await w.AddTaskAsync(ageHours: 60);
        var lost = await RewriteAsync(w, a);
        var salvage = Prefix + a.Id.ToString("D") + "/" + lost;
        var master = (await w.ReadAsync(w.Fixture.Repository, "rev-parse", "master")).Trim();
        await w.Fixture.RequiredAsync(w.Fixture.Repository, "update-ref", salvage, master);
        var before = await w.SnapshotAsync(a.Path);

        var tick = await w.TickAsync();

        (await w.ReadAsync(w.Fixture.Repository, "for-each-ref", "--format=%(refname) %(objectname) %(symref)", Prefix))
            .Trim().ShouldBe(salvage + " " + master, "the colliding ref is unchanged and still direct");
        (await w.SnapshotAsync(a.Path)).ShouldBe(before, "the tree is byte-intact");
        (await w.BranchExistsAsync(a)).ShouldBeTrue();
        (await w.RowAsync(tick.RunId, a)).ReasonCode.ShouldBe("reflog_salvage_collision");
    }

    /// <summary>
    /// Review 9222cb00 B-4. A symbolic ref under a finished, aged release's salvage scope (named after its target's
    /// object) is never pruned through: the branch it names stays.
    /// </summary>
    [Test]
    public async Task Prune_never_deletes_through_a_symbolic_salvage_ref()
    {
        await using var w = await DesktopLegacyWorld.CreateAsync();
        var a = await w.AddTaskAsync(ageHours: 60);
        await RewriteAsync(w, a);
        await w.TickAsync();
        await w.Fixture.RequiredAsync(w.Fixture.Repository, "branch", "precious", "master");
        var master = (await w.ReadAsync(w.Fixture.Repository, "rev-parse", "master")).Trim();
        var planted = Prefix + a.Id.ToString("D") + "/" + master;
        await w.Fixture.RequiredAsync(w.Fixture.Repository, "symbolic-ref", planted, "refs/heads/precious");
        w.Clock.Advance(TimeSpan.FromDays(31));

        await w.TickAsync();

        (await w.ReadAsync(w.Fixture.Repository, "rev-parse", "--verify", "refs/heads/precious")).Trim().ShouldBe(master);
        (await w.ReadAsync(w.Fixture.Repository, "symbolic-ref", planted)).Trim().ShouldBe("refs/heads/precious");
    }

    /// <summary>
    /// Review 39f7bfe1 B-4. A salvage name that is already a DANGLING symbolic ref (its target does not exist, so
    /// <c>for-each-ref</c> omits it and a create-only write would replace it) refuses the release: the symbolic ref
    /// stays byte-identical, the tree, registration and branch stay, and no ref is created or moved.
    /// </summary>
    [Test]
    public async Task Dangling_symbolic_salvage_collision_refuses_and_keeps_the_symbolic_ref()
    {
        await using var w = await DesktopLegacyWorld.CreateAsync();
        var a = await w.AddTaskAsync(ageHours: 60);
        var lost = await RewriteAsync(w, a);
        var salvage = Prefix + a.Id.ToString("D") + "/" + lost;
        await w.Fixture.RequiredAsync(w.Fixture.Repository, "symbolic-ref", salvage, "refs/heads/c1708-missing");
        var file = await RefFileAsync(w, salvage);
        var bytes = File.ReadAllBytes(file);
        var refs = await AllRefsAsync(w);
        var origin = await w.OriginRefsAsync();
        var before = await w.SnapshotAsync(a.Path);

        var tick = await w.TickAsync();

        (await w.RowAsync(tick.RunId, a)).ReasonCode.ShouldBe("reflog_salvage_collision");
        File.ReadAllBytes(file).ShouldBe(bytes, "the dangling symbolic ref is byte-identical");
        (await w.ReadAsync(w.Fixture.Repository, "symbolic-ref", salvage)).Trim().ShouldBe("refs/heads/c1708-missing");
        (await AllRefsAsync(w)).ShouldBe(refs, "no salvage or other ref was created, moved or deleted");
        refs.ShouldContain(l => l.StartsWith("refs/heads/master ", StringComparison.Ordinal));
        (await w.ReadAsync(w.Fixture.Repository, "rev-list", "--all")).ShouldNotContain(lost, Case.Sensitive);
        (await w.SnapshotAsync(a.Path)).ShouldBe(before, "the tree is byte-intact");
        (await w.RegisteredAsync(a.Path)).ShouldBeTrue("the registration is intact");
        (await w.BranchExistsAsync(a)).ShouldBeTrue();
        (await w.OriginRefsAsync()).ShouldBe(origin);
    }

    /// <summary>
    /// Review 39f7bfe1 B-4 (control). A dangling symbolic ref under a finished, aged release's salvage scope is never
    /// pruned: the scope's real salvage ref goes, the dangling one stays byte-identical.
    /// </summary>
    [Test]
    public async Task Prune_keeps_a_dangling_symbolic_salvage_ref()
    {
        await using var w = await DesktopLegacyWorld.CreateAsync();
        var a = await w.AddTaskAsync(ageHours: 60);
        var lost = await RewriteAsync(w, a);
        await w.TickAsync();
        var salvage = Prefix + a.Id.ToString("D") + "/" + lost;
        (await SalvageRefsAsync(w)).ShouldBe([salvage]);
        var master = (await w.ReadAsync(w.Fixture.Repository, "rev-parse", "master")).Trim();
        var planted = Prefix + a.Id.ToString("D") + "/" + master;
        await w.Fixture.RequiredAsync(w.Fixture.Repository, "symbolic-ref", planted, "refs/heads/c1708-missing");
        var file = await RefFileAsync(w, planted);
        var bytes = File.ReadAllBytes(file);
        w.Clock.Advance(TimeSpan.FromDays(31));

        await w.TickAsync();

        (await SalvageRefsAsync(w)).ShouldBeEmpty("the real salvage ref is past the retention");
        File.ReadAllBytes(file).ShouldBe(bytes, "the dangling symbolic ref is byte-identical");
        (await w.ReadAsync(w.Fixture.Repository, "symbolic-ref", planted)).Trim().ShouldBe("refs/heads/c1708-missing");
    }

    /// <summary>Fails the first verification read of <paramref name="a"/>'s salvage scope after a salvage ref write.</summary>
    private static Func<int> FailVerifyAfterWrite(DesktopLegacyWorld w, DesktopLegacyWorld.Handle a)
    {
        var scope = Prefix + a.Id.ToString("D") + "/";
        var wrote = false;
        var fired = 0;
        w.Fixture.Git.BeforeCommand = (_, args) =>
        {
            if (args.Count > 0 && args[0] == "update-ref" && args.Any(x => x.StartsWith(scope, StringComparison.Ordinal)))
                wrote = true;
            else if (wrote && fired == 0 && args.Count > 0 && args[0] == "for-each-ref" && args.Contains(scope))
            {
                fired++;
                return Task.FromResult<Antiphon.Server.Application.Dtos.LandingGitResult?>(new(1, "", "c1708_injected"));
            }
            return Task.FromResult<Antiphon.Server.Application.Dtos.LandingGitResult?>(null);
        };
        return () => fired;
    }

    /// <summary>A commit, then <c>reset --hard HEAD~1</c>: clean, at the pushed tip, the commit only in the reflogs.</summary>
    private static async Task<string> RewriteAsync(DesktopLegacyWorld w, DesktopLegacyWorld.Handle a)
    {
        await w.CommitAsync(a, a.Id8 + "-rewritten.txt");
        var lost = (await w.ReadAsync(a.Path, "rev-parse", "HEAD")).Trim();
        await w.Fixture.RequiredAsync(a.Path, "reset", "--hard", "HEAD~1");
        (await w.ReadAsync(w.Fixture.Repository, "rev-list", "--all")).ShouldNotContain(lost, Case.Sensitive);
        return lost;
    }

    /// <summary>The loose file of <paramref name="name"/> (a symbolic ref is always loose in the files backend).</summary>
    private static async Task<string> RefFileAsync(DesktopLegacyWorld w, string name)
    {
        var path = (await w.ReadAsync(w.Fixture.Repository, "rev-parse", "--path-format=absolute", "--git-path", name)).Trim();
        File.Exists(path).ShouldBeTrue(path);
        return path;
    }

    /// <summary>
    /// Every ref Git lists, with its object and symbolic target, except the release's own remote-observation pins
    /// under <c>refs/antiphon/retirement/</c> (written before the salvage step on every release, refused or not).
    /// </summary>
    private static async Task<string[]> AllRefsAsync(DesktopLegacyWorld w) =>
        (await w.ReadAsync(w.Fixture.Repository, "for-each-ref", "--format=%(refname) %(objectname) %(symref)"))
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(l => !l.StartsWith("refs/antiphon/retirement/", StringComparison.Ordinal)).ToArray();

    private static async Task<string[]> SalvageRefsAsync(DesktopLegacyWorld w) =>
        (await w.ReadAsync(w.Fixture.Repository, "for-each-ref", "--format=%(refname)", Prefix))
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
