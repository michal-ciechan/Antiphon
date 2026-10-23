using System.Security.Cryptography;
using System.Text.Json;
using Antiphon.Checkpoints;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Checkpoints;

[Category("Unit")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class CheckpointLargeStampTests : CheckpointTestBase
{
    private const long MiB = 1024 * 1024;
    private const long Large = 70 * MiB;
    private static string[] BuildArgs(string id = "next") => ["build", "App/App.csproj", "--property:OutputPath=bin-" + id + "/"];

    [Test]
    public void C1532_regular_stamp_crosses_receipt_limit()
    {
        var path = Path.Combine(TempDir(), "payload.dll");
        foreach (var size in new[] { 64 * MiB, 64 * MiB + 1, Large })
        {
            WriteLarge(path, size);
            BuildConfinement.Stamp(path).ShouldBe(Expected(path), "full regular stamp must accept " + size + " bytes");
        }
    }

    [Test]
    public void C1532_large_ignored_paths_remain_covered()
    {
        var root = Repo();
        foreach (var relative in new[] { "App/bin-prior/large.dll", "App/obj/large.dll" }) WriteLarge(Path.Combine(root, relative), Large);
        var prior = Path.Combine(root, "App/bin-prior/large.dll");
        var obj = Path.Combine(root, "App/obj/large.dll");
        var expected = Expected(prior);
        var ledger = Capture(root);
        ledger.Files["App/bin-prior/large.dll"].ShouldBe(expected);
        ledger.Files.ShouldNotContainKey("App/obj/large.dll");
        var admission = BuildConfinement.AdmitLedger(ledger, strict: true);
        admission.Settlement.Kind.ShouldBeNull(admission.Settlement.Detail);
        var baseline = Prepare(root, ledger);
        baseline.Untracked["App/obj/large.dll"].ShouldBe(expected);
        BuildConfinement.Settle(baseline).Kind.ShouldBeNull();
        BuildConfinement.VerifyLedger(ledger).Kind.ShouldBeNull();
        Expected(prior).ShouldBe(expected);
        Expected(obj).ShouldBe(expected);
        SafeWrite.WasCreated(prior).ShouldBeFalse();
        SafeWrite.WasCreated(obj).ShouldBeFalse();
    }

    [Test]
    public void C1532_tail_edit_with_restored_mtime_is_tampered()
    {
        var root = Repo();
        foreach (var relative in new[] { "App/bin-prior/large.dll", "App/obj/large.dll" }) WriteLarge(Path.Combine(root, relative), Large);
        var ledger = Capture(root);
        ledger.Files["App/bin-prior/large.dll"].ShouldBe(Expected(Path.Combine(root, "App/bin-prior/large.dll")));
        var baseline = Prepare(root, ledger);
        foreach (var relative in new[] { "App/bin-prior/large.dll", "App/obj/large.dll" })
        {
            var path = Path.Combine(root, relative);
            var before = BuildConfinement.Stamp(path);
            before.ShouldStartWith("file|73400320|");
            FlipTail(path);
            BuildConfinement.Stamp(path).ShouldNotBe(before);
        }
        AssertTampered(BuildConfinement.AdmitLedger(ledger, strict: true).Settlement, "App/bin-prior/large.dll");
        AssertTampered(BuildConfinement.VerifyLedger(ledger), "App/bin-prior/large.dll");
        AssertTampered(BuildConfinement.Settle(baseline), "App/obj/large.dll");
        foreach (var relative in new[] { "App/bin-prior/large.dll", "App/obj/large.dll" })
        {
            using var stream = File.OpenRead(Path.Combine(root, relative));
            stream.Position = 69 * MiB;
            stream.ReadByte().ShouldBe(1, "confinement must leave the changed byte in place");
        }
    }

    [Test]
    [Timeout(120_000)]
    public async Task C1532_second_build_and_retry_admit_large_prior_output(CancellationToken cancellationToken)
    {
        var root = Repo();
        var driver = new LargeDriver();
        var first = await Run(root, driver, ["a", "b"], cancellationToken);
        first.Exit.ShouldBe(0, string.Join("\n", first.Report.Builds.Select(b => b.SlotReason)));
        driver.Builds.ShouldBe(2);
        driver.Rows.ShouldBe(2);
        driver.Captures.ShouldBe(2);
        var retained = Path.Combine(root, "App/bin-a/large.dll");
        var expected = Expected(retained);
        driver.Ledgers.Last().Files["App/bin-a/large.dll"].ShouldBe(expected);
        // A fresh run/output id must traverse retained outputs; an existing id cannot acquire custody.
        var retry = await Run(root, driver, ["retry"], cancellationToken);
        retry.Exit.ShouldBe(0);
        driver.Builds.ShouldBe(3);
        driver.Rows.ShouldBe(3);
        var existing = await Run(root, driver, ["a"], cancellationToken);
        existing.Exit.ShouldNotBe(0);
        driver.Builds.ShouldBe(3);
        Expected(retained).ShouldBe(expected);
        SafeWrite.WasCreated(retained).ShouldBeFalse();
        FlipTail(retained);
        AssertTampered(BuildConfinement.VerifyLedger(driver.Ledgers[1]), "App/bin-a/large.dll");
        File.Exists(retained).ShouldBeTrue();
    }

    private string Repo()
    {
        var root = Path.Combine(TempDir(), "repo");
        Directory.CreateDirectory(Path.Combine(root, "App"));
        File.WriteAllText(Path.Combine(root, ".gitignore"), "bin-*/\nobj/\n.antiphon/\n");
        File.WriteAllText(Path.Combine(root, "App/App.csproj"), "<Project />");
        CheckpointFixtureProcess.Git(root, "init", "-b", "fixture");
        CheckpointFixtureProcess.Git(root, "add", "-A");
        CheckpointFixtureProcess.Git(root, "-c", "user.name=Fixture", "-c", "user.email=fixture@example.invalid", "-c", "commit.gpgsign=false", "commit", "-qm", "fixture", "--no-verify");
        return root;
    }

    private static void WriteLarge(string path, long size)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var stream = File.Create(path);
        stream.SetLength(size); // sparse bytes still all contribute to both independent digests
        stream.WriteByte(0x53);
        stream.Position = size - 1;
        stream.WriteByte(0xA7);
    }
    private static string Expected(string path)
    {
        using var stream = File.OpenRead(path);
        return "file|" + stream.Length + "|" + Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }
    private static void FlipTail(string path)
    {
        var modified = File.GetLastWriteTimeUtc(path);
        using (var stream = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.Read))
        {
            stream.Position = 69 * MiB;
            var original = stream.ReadByte();
            stream.Position--;
            stream.WriteByte((byte)(original ^ 1));
        }
        File.SetLastWriteTimeUtc(path, modified);
        File.GetLastWriteTimeUtc(path).ShouldBe(modified);
        new FileInfo(path).Length.ShouldBe(Large);
    }
    private static void AssertTampered(BuildConfinement.Settlement settled, string path)
    {
        settled.Kind.ShouldBe("tampered", settled.Detail);
        settled.Paths.ShouldBe(new[] { path });
        settled.Detail.ShouldContain("TAMPERED " + path);
    }
    private static BuildConfinement.Ledger Capture(string root)
    {
        var capture = BuildConfinement.CaptureLedger(root, BuildArgs());
        capture.Refusal.ShouldBeNull(capture.Detail);
        return capture.Value.ShouldNotBeNull();
    }
    private static BuildConfinement.Baseline Prepare(string root, BuildConfinement.Ledger ledger)
    {
        var prepared = BuildConfinement.Prepare(root, BuildArgs(), null, CancellationToken.None, ledger);
        prepared.Refusal.ShouldBeNull(prepared.Detail);
        return prepared.Baseline.ShouldNotBeNull();
    }
    private static async Task<(int Exit, ReportModel Report)> Run(string root, LargeDriver driver, string[] ids, CancellationToken ct)
    {
        var source = CheckpointFixtures.CleanSource();
        var manifest = new CheckpointManifest
        {
            Build = new BuildDefaults { AdmissionHash = "strict" },
            Builds = ids.Select(id => new BuildSpec { Id = "bin-" + id, Project = "App/App.csproj", OutputPath = "bin-" + id + "/" }).ToList(),
            Checkpoints = ids.Select(id => CheckpointFixtures.Row("CP-" + id, "bin-" + id, serial: true)).ToList(),
        };
        var run = CheckpointApp.CreateRun(manifest, new RunRequest { Commit = source.Commit!, Branch = "fixture", KeepOutputs = true, Slots = "off" }, root,
            new CheckpointApp.Runtime { SourceCapture = _ => source });
        var exit = await CheckpointApp.ExecuteAsync(run, ct, new CheckpointApp.Runtime
        {
            Driver = driver, Slots = new LocalSlots(), SourceCapture = _ => source, EnvironmentLookup = _ => null,
        });
        return (exit, JsonSerializer.Deserialize<ReportModel>(File.ReadAllText(Path.Combine(run, "report.json")), ReportWriter.Json)!);
    }
    private sealed class LocalSlots : IBuildSlotClient
    {
        public Task<SlotSession> ProbeAsync(CancellationToken ct) => Task.FromResult(new SlotSession("granted", 1));
        public Task<SlotLease> AcquireAsync(SlotSession session, string label, CancellationToken ct) => Task.FromResult(new SlotLease { State = "granted", MaxCpuCount = 1 });
    }
    private sealed class LargeDriver : IConfinedDriver
    {
        public int Builds, Rows, Captures;
        public List<BuildConfinement.Ledger> Ledgers { get; } = [];
        public BuildConfinement.LedgerCapture CaptureLedger(DriverRequest request)
        {
            Captures++;
            var capture = BuildConfinement.CaptureLedger(request.WorkingDirectory, request.Arguments);
            if (capture.Value is not null) Ledgers.Add(capture.Value);
            return capture;
        }
        public Task<DriverResult> RunAsync(DriverRequest request, CancellationToken ct)
        {
            if (CheckpointFixtures.IsBuild(request))
            {
                Builds++;
                var prepared = BuildConfinement.Prepare(request.WorkingDirectory, request.Arguments, null, ct, request.Ledger);
                if (prepared.Refusal is not null) return Task.FromResult(new DriverResult(2, prepared.Detail, ""));
                var output = request.Arguments.Single(a => a.StartsWith("--property:OutputPath=", StringComparison.Ordinal)).Split('=', 2)[1];
                WriteLarge(Path.Combine(request.WorkingDirectory, "App", output, "large.dll"), Large);
                var settled = BuildConfinement.Settle(prepared.Baseline!);
                return Task.FromResult(new DriverResult(settled.Kind is null ? 0 : 2, settled.Detail, ""));
            }
            Rows++;
            var filter = request.Arguments[request.Arguments.ToList().IndexOf("--treenode-filter") + 1];
            var name = filter.Split('/')[3] + ".runs";
            CheckpointFixtures.WriteResults(CheckpointFixtures.TrxFile(request), (name, "Passed"));
            return Task.FromResult(new DriverResult(0, "", ""));
        }
        public void Kill(bool entireProcessTree) { }
    }
}
