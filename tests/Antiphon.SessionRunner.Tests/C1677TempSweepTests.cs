using Antiphon.SessionRunner;
using Antiphon.SessionRunner.Contracts;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.SessionRunner.Tests;

/// <summary>
/// CARD-1677 S2 CP-7 (portable). The /tmp sweep against an injected temp root under a scratch directory, with
/// a fake clock, a scripted owner-uid seam and a scripted holder scan: only a registered, old, owned, unheld,
/// undenied top-level entry is removed (Execute) or reported would_remove (DryRun); every other name keeps
/// its bytes. Each refusal case violates one condition, with every other fact eligible (S-6, M-14).
/// </summary>
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
[Timeout(10_000)]
public sealed class C1677TempSweepTests
{
    internal const int Uid = 1654;
    internal static readonly DateTimeOffset Now = new(2026, 11, 3, 12, 0, 0, TimeSpan.Zero);

    internal sealed class Scratch : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "c1677-tmp-" + Guid.NewGuid().ToString("N"));
        public string Tmp => Root + "/tmp";
        public FakeHolderScan Holders { get; } = new();
        /// <summary>The scripted owner uid, always called with '/' separators: on Windows the engine walks
        /// <see cref="FileSystemInfo.FullName"/>, which normalises every separator to a backslash (run 766e3df5 group 1).</summary>
        public Func<string, int?> Owner { get; set; } = _ => Uid;
        public HashSet<string> Present { get; } = new(StringComparer.Ordinal);
        public List<string> Probed { get; } = [];
        public FakeTimeProvider Clock { get; } = new(Now);

        public Scratch() => Directory.CreateDirectory(Tmp);

        public RunnerTempSweep Sweep(TempSweepLimits? limits = null, IReadOnlyList<TempFamily>? families = null,
            IWorkspaceHolderScan? holders = null) =>
            new(Tmp, families ?? RunnerTempSweep.RegisteredFamilies, holders ?? Holders, path => Owner(path.Replace('\\', '/')), Uid, Clock,
                limits ?? new TempSweepLimits(), path =>
                {
                    Probed.Add(path);
                    return Present.Contains(path);
                }, retiringRoot: "/work/worktrees-retiring");

        /// <summary>An entry with <paramref name="files"/> files written <paramref name="age"/> ago (directories too).</summary>
        public string Entry(string name, TimeSpan age, int files = 3, int size = 64)
        {
            var path = Tmp + "/" + name;
            Directory.CreateDirectory(path);
            for (var i = 0; i < files; i++)
                File.WriteAllBytes(path + "/f" + i + ".bin", new byte[size]);
            Age(path, age);
            return path;
        }

        /// <summary>Sets every file and directory under (and including) the path to <paramref name="age"/> ago.</summary>
        public void Age(string path, TimeSpan age)
        {
            var at = Now.UtcDateTime - age;
            if (Directory.Exists(path))
            {
                foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                    File.SetLastWriteTimeUtc(file, at);
                foreach (var directory in Directory.EnumerateDirectories(path, "*", SearchOption.AllDirectories)
                             .OrderByDescending(d => d.Length))
                    Directory.SetLastWriteTimeUtc(directory, at);
                Directory.SetLastWriteTimeUtc(path, at);
            }
            else
                File.SetLastWriteTimeUtc(path, at);
        }

        public void Dispose()
        {
            try { Directory.Delete(Root, recursive: true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    internal static PhoneHomeTempSweepRequest Request(bool execute = true, int maxEntries = 200, long maxBytes = 21_474_836_480,
        Dictionary<string, bool>? families = null) =>
        new(execute ? TempSweepModes.Execute : TempSweepModes.DryRun, Guid.NewGuid(), 48, maxEntries, maxBytes,
            families ?? new Dictionary<string, bool> { [RunnerTempSweep.ClaudeScratch] = false });

    internal static string Hex32() => Guid.NewGuid().ToString("N");

    internal static TempSweepCandidate Single(PhoneHomeTempSweepResponse response, string name) =>
        response.Candidates.Where(c => c.Name == name).ShouldHaveSingleItem();

    internal static void KeptIntact(PhoneHomeTempSweepResponse response, string name, string reason, string path, int files = 3)
    {
        var candidate = Single(response, name);
        candidate.Verdict.ShouldBe(TempSweepCodes.Kept);
        candidate.Reason.ShouldBe(reason, candidate.Detail);
        Directory.Exists(path).ShouldBeTrue();
        Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories).Count().ShouldBe(files);
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task Old_registered_entry_is_swept(bool execute, CancellationToken ct)
    {
        // V-40.
        using var s = new Scratch();
        var name = "c487-probe-" + Hex32();
        var path = s.Entry(name, TimeSpan.FromHours(49));
        var response = await s.Sweep().SweepAsync(Request(execute), ct);
        var candidate = Single(response, name);
        candidate.Family.ShouldBe("c487-probe");
        candidate.Bytes.ShouldBe(3 * 64);
        if (execute)
        {
            candidate.Verdict.ShouldBe(TempSweepCodes.Removed, candidate.Detail);
            Directory.Exists(path).ShouldBeFalse();
            response.Removed.ShouldBe(1);
            response.RemovedBytes.ShouldBe(3 * 64);
        }
        else
        {
            candidate.Verdict.ShouldBe(TempSweepCodes.WouldRemove);
            Directory.EnumerateFiles(path).Count().ShouldBe(3);
        }
        s.Holders.Calls.ShouldBe(1);
    }

    [Test]
    public async Task Unregistered_name_is_reported_and_kept(CancellationToken ct)
    {
        // V-41 (G-85). An old name of no registered family is never a candidate; its bytes are reported.
        using var s = new Scratch();
        var name = "g104-" + Hex32();
        var path = s.Entry(name, TimeSpan.FromHours(200), files: 2, size: 100);
        var control = s.Entry("c1514-" + Hex32(), TimeSpan.FromHours(49));
        var response = await s.Sweep().SweepAsync(Request(), ct);
        response.Candidates.ShouldNotContain(c => c.Name == name);
        var row = response.Unregistered.ShouldHaveSingleItem();
        row.Prefix.ShouldBe("g104-<hex>");
        row.Count.ShouldBe(1);
        row.Bytes.ShouldBe(200);
        row.BytesComplete.ShouldBeTrue();
        Directory.EnumerateFiles(path).Count().ShouldBe(2);
        Directory.Exists(control).ShouldBeFalse("the registered control beside it is swept");
    }

    [Test]
    [Arguments("new-top-file")]
    [Arguments("new-nested-file")]
    public async Task Newest_file_write_decides_age(string shape, CancellationToken ct)
    {
        // V-42 (G-86, G-87). The directory mtime is 49 h old; one file is 1 h old.
        using var s = new Scratch();
        var name = "c502-exit-cap-" + Hex32();
        var path = s.Tmp + "/" + name;
        Directory.CreateDirectory(path + "/a/b");
        File.WriteAllText(path + "/top.txt", "x");
        File.WriteAllText(path + "/a/b/c.txt", "x");
        s.Age(path, TimeSpan.FromHours(49));
        File.SetLastWriteTimeUtc(shape == "new-top-file" ? path + "/top.txt" : path + "/a/b/c.txt", Now.UtcDateTime.AddHours(-1));
        Directory.GetLastWriteTimeUtc(path).ShouldBe(Now.UtcDateTime.AddHours(-49));
        var response = await s.Sweep().SweepAsync(Request(), ct);
        KeptIntact(response, name, TempSweepCodes.TooYoung, path, files: 2);
        Single(response, name).NewestWriteUtc.ShouldBe(Now.UtcDateTime.AddHours(-1));
    }

    [Test]
    [Arguments("holder")]
    [Arguments("scan-unknown")]
    public async Task Holder_keeps_the_entry(string shape, CancellationToken ct)
    {
        // V-43 (G-88, G-89).
        using var s = new Scratch();
        var name = "antiphon-bridge-queue-" + Hex32();
        var path = s.Entry(name, TimeSpan.FromHours(72));
        s.Holders.Next = shape == "holder"
            ? _ => new HolderScan(true, [new WorkspaceSlotHolder(4242, "/usr/bin/sleep", "cwd")])
            : _ => new HolderScan(false, []);
        var response = await s.Sweep().SweepAsync(Request(), ct);
        KeptIntact(response, name, shape == "holder" ? TempSweepCodes.LiveProcess : TempSweepCodes.HolderScanUnknown, path);
        if (shape == "holder")
            Single(response, name).Holders.ShouldNotBeNull().ShouldHaveSingleItem().Pid.ShouldBe(4242);
    }

    [Test]
    [Arguments("antiphon-pty-hosts")]
    [Arguments("antiphon-pty-<hex>")]
    [Arguments("CoreFxPipe_x")]
    [Arguments("dotnet-diagnostic-1-2-socket")]
    [Arguments(".X11-unix")]
    [Arguments("systemd-private-x")]
    [Arguments("node-compile-cache")]
    [Arguments(".codex")]
    public async Task Denied_names_are_kept_under_a_broad_family(string name, CancellationToken ct)
    {
        // V-44 (G-90..G-97). No shipped family matches a denied name (M-14), so a broad family is injected.
        using var s = new Scratch();
        name = name.Replace("<hex>", Hex32(), StringComparison.Ordinal);
        var path = s.Entry(name, TimeSpan.FromHours(500));
        var control = "control-" + Hex32();
        var controlPath = s.Entry(control, TimeSpan.FromHours(500));
        var response = await s.Sweep(families: [new TempFamily("broad", "^.*$", null, null, "x")]).SweepAsync(Request(), ct);
        KeptIntact(response, name, TempSweepCodes.Denied, path);
        Single(response, control).Verdict.ShouldBe(TempSweepCodes.Removed, "the broad family itself is live");
        Directory.Exists(controlPath).ShouldBeFalse();
    }

    [Test]
    public async Task Foreign_owned_descendant_keeps_the_entry(CancellationToken ct)
    {
        // V-45 (G-98).
        using var s = new Scratch();
        var name = "antiphon-land-verify-" + Hex32();
        var path = s.Entry(name, TimeSpan.FromHours(49));
        Directory.CreateDirectory(path + "/deep");
        File.WriteAllText(path + "/deep/theirs.txt", "x");
        s.Age(path, TimeSpan.FromHours(49));
        s.Owner = p => p.EndsWith("/deep/theirs.txt", StringComparison.Ordinal) ? Uid + 1 : Uid;
        var response = await s.Sweep().SweepAsync(Request(), ct);
        KeptIntact(response, name, TempSweepCodes.ForeignOwner, path, files: 4);
    }

    [Test]
    [Arguments("entry")]
    [Arguments("descendant")]
    public async Task Undeterminable_owner_keeps_the_entry(string where, CancellationToken ct)
    {
        // Fail closed: an owner the platform cannot read (null, as off Linux) never counts as the runner's.
        using var s = new Scratch();
        var name = "antiphon-land-verify-" + Hex32();
        var path = s.Entry(name, TimeSpan.FromHours(49));
        Directory.CreateDirectory(path + "/deep");
        File.WriteAllText(path + "/deep/unknown.txt", "x");
        s.Age(path, TimeSpan.FromHours(49));
        s.Owner = where == "entry"
            ? p => p == path.Replace('\\', '/') ? null : Uid
            : p => p.EndsWith("/deep/unknown.txt", StringComparison.Ordinal) ? null : Uid;
        var response = await s.Sweep().SweepAsync(Request(), ct);
        KeptIntact(response, name, TempSweepCodes.OwnerUnknown, path, files: 4);
    }

    [Test]
    [Arguments("entries")]
    [Arguments("bytes")]
    public async Task Per_tick_bounds(string bound, CancellationToken ct)
    {
        // V-46 (G-101, G-102). Oldest first; the rest are kept as tick_bound.
        using var s = new Scratch();
        var count = bound == "entries" ? 5 : 3;
        var paths = Enumerable.Range(0, count)
            .Select(i => s.Entry("c1514-" + Hex32(), TimeSpan.FromHours(100 - i)))
            .ToList();
        var limits = bound == "entries" ? new TempSweepLimits(MaxEntries: 2) : new TempSweepLimits(MaxBytes: 3 * 64 * 3 / 2);
        var response = await s.Sweep(limits).SweepAsync(Request(), ct);
        var expected = bound == "entries" ? 2 : 1;
        response.Removed.ShouldBe(expected);
        for (var i = 0; i < count; i++)
            Directory.Exists(paths[i]).ShouldBe(i >= expected, paths[i]);
        response.Candidates.Count(c => c.Reason == TempSweepCodes.TickBound).ShouldBe(count - expected);
    }

    [Test]
    public async Task Inventory_limit_keeps_the_entry(CancellationToken ct)
    {
        // V-46b (G-103).
        using var s = new Scratch();
        var name = "c487-probe-" + Hex32();
        var path = s.Entry(name, TimeSpan.FromHours(49), files: 11);
        var response = await s.Sweep(new TempSweepLimits(InventoryEntries: 10)).SweepAsync(Request(), ct);
        KeptIntact(response, name, TempSweepCodes.InventoryLimit, path, files: 11);
    }

    [Test]
    [Arguments("flag-off")]
    [Arguments("mirror-present")]
    [Arguments("retiring-present")]
    [Arguments("eligible")]
    public async Task Claude_scratch_needs_flag_and_no_mirror(string shape, CancellationToken ct)
    {
        // V-47 (G-104..G-106). Depth 2 under claude-1654; the container itself always stays.
        using var s = new Scratch();
        var name = "claude-1654/-work-worktrees-task-a1677001";
        var path = s.Entry(name, TimeSpan.FromHours(49));
        s.Age(s.Tmp + "/claude-1654", TimeSpan.FromHours(49));
        if (shape == "mirror-present")
            s.Present.Add("/work/worktrees/task-a1677001");
        if (shape == "retiring-present")
            s.Present.Add("/work/worktrees-retiring/task-a1677001.*");
        var flags = new Dictionary<string, bool> { [RunnerTempSweep.ClaudeScratch] = shape != "flag-off" };
        var response = await s.Sweep().SweepAsync(Request(families: flags), ct);
        switch (shape)
        {
            case "flag-off":
                KeptIntact(response, name, TempSweepCodes.FamilyDisabled, path);
                break;
            case "mirror-present":
            case "retiring-present":
                KeptIntact(response, name, TempSweepCodes.MirrorPresent, path);
                break;
            default:
                Single(response, name).Verdict.ShouldBe(TempSweepCodes.Removed, Single(response, name).Detail);
                Directory.Exists(path).ShouldBeFalse();
                s.Probed.ShouldBe(["/work/worktrees/task-a1677001", "/work/worktrees-retiring/task-a1677001.*"]);
                break;
        }
        Directory.Exists(s.Tmp + "/claude-1654").ShouldBeTrue();
        response.Candidates.ShouldNotContain(c => c.Name == "claude-1654");
    }

    [Test]
    public async Task Only_top_level_entries_are_considered(CancellationToken ct)
    {
        // V-47b (G-107).
        using var s = new Scratch();
        var nested = "x-unregistered/c487-probe-" + Hex32();
        var path = s.Entry(nested, TimeSpan.FromHours(49));
        s.Age(s.Tmp + "/x-unregistered", TimeSpan.FromHours(49));
        var response = await s.Sweep().SweepAsync(Request(), ct);
        response.Candidates.ShouldBeEmpty();
        response.Unregistered.ShouldHaveSingleItem().Prefix.ShouldBe("x-unregistered");
        Directory.EnumerateFiles(path).Count().ShouldBe(3);
    }
}
