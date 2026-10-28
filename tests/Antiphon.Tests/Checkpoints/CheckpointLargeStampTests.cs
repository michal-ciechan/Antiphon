using System.Security.Cryptography;
using System.Text.Json;
using Antiphon.Checkpoints;
using Antiphon.Tests.Infrastructure;
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
        var retryRoot = Repo();
        var old = Path.Combine(retryRoot, "App/bin-red/large.dll");
        WriteLarge(old, Large);
        var retry = await Run(retryRoot, driver, ["retry"], cancellationToken);
        retry.Exit.ShouldBe(0);
        driver.Builds.ShouldBe(3);
        driver.Rows.ShouldBe(3);
        var existing = await Run(root, driver, ["a"], cancellationToken);
        existing.Exit.ShouldNotBe(0);
        driver.Builds.ShouldBe(3);
        Expected(retained).ShouldBe(expected);
        Expected(old).ShouldBe(expected);
        SafeWrite.WasCreated(old).ShouldBeFalse();
        FlipTail(retained);
        AssertTampered(BuildConfinement.VerifyLedger(driver.Ledgers[1]), "App/bin-a/large.dll");
        File.Exists(retained).ShouldBeTrue();
    }

    [Test]
    public void C1532_content_and_receipt_limits_are_distinct()
    {
        var path = Path.Combine(TempDir(), "shape.bin");
        BuildConfinement.Stamp(path).ShouldBe("absent|0|");
        File.WriteAllBytes(path, []);
        BuildConfinement.Stamp(path).ShouldBe("file|0|e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855");
        BuildConfinement.Stamp(Path.GetDirectoryName(path)!).ShouldBe("directory|0|");
        File.WriteAllText(path, "value");
        BuildConfinement.Stamp(path).ShouldBe(Expected(path));

        var f = new HostCleanupCheckpointFixture();
        var node = f.Io.Put(path, []);
        const long bound = 1L << 30;
        node.Stat = node.Stat with { Size = bound };
        var stream = new ZeroStream(bound);
        f.Io.StreamFactory = () => stream;
        using var reference = new ZeroStream(bound);
        var expected = Convert.ToHexString(SHA256.HashData(reference)).ToLowerInvariant();
        BuildConfinement.Stamp(path, p => ReceiptFile.Digest(f.Context, p)).ShouldBe("file|1073741824|" + expected);
        stream.Delivered.ShouldBe(bound);
        stream.LargestRequest.ShouldBeLessThanOrEqualTo(256 * 1024);
        f.Io.Count("open-read", path).ShouldBe(1);
        f.Io.Count("close-read", path).ShouldBe(1);

        f.Io.Events.Clear();
        node.Stat = node.Stat with { Size = bound + 1 };
        BuildConfinement.Stamp(path, p => ReceiptFile.Digest(f.Context, p)).ShouldBe("unreadable|selected file exceeds size limit");
        f.Io.Count("stream").ShouldBe(0);
        node.Stat = node.Stat with { Size = bound };
        var growth = new ZeroStream(bound + 1);
        f.Io.StreamFactory = () => growth;
        BuildConfinement.Stamp(path, p => ReceiptFile.Digest(f.Context, p)).ShouldBe("unreadable|selected file exceeds size limit");
        growth.Delivered.ShouldBe(bound + 1);
        growth.LargestRequest.ShouldBeLessThanOrEqualTo(256 * 1024);

        node.Stat = node.Stat with { Size = 64 * MiB };
        f.Io.StreamFactory = () => new ZeroStream(64 * MiB);
        var receipt = ReceiptFile.ReadBytes(f.Context, path);
        receipt.LongLength.ShouldBe(64 * MiB);
        receipt.All(b => b == 0).ShouldBeTrue();
        node.Stat = node.Stat with { Size = 64 * MiB + 1 };
        f.Io.Events.Clear();
        Should.Throw<IOException>(() => ReceiptFile.ReadBytes(f.Context, path)).Message.ShouldContain("selected file exceeds size limit");
        f.Io.Count("stream").ShouldBe(0);
        f.Io.Events.ShouldNotContain(e => e.Operation == "write" || e.Operation == "unlink" || e.Operation == "rmdir");
    }

    [Test]
    public void C1532_digest_preserves_verified_read_refusals()
    {
        foreach (var kind in new[] { "hardlink", "nonregular", "outside" })
        {
            var f = new HostCleanupCheckpointFixture();
            f.Io.HandleMetadata = m => kind switch
            {
                "hardlink" => m with { Links = 2 },
                "nonregular" => m with { Regular = false },
                _ => m with { Path = Path.Combine(f.Parent, "outside") },
            };
            var reason = kind switch { "hardlink" => "multiple links", "nonregular" => "not regular", _ => "outside root" };
            Should.Throw<IOException>(() => ReceiptFile.Digest(f.Context, f.Payload)).Message.ShouldContain(reason);
            f.Io.Count("stream").ShouldBe(0);
            f.Io.Count("open-read").ShouldBe(1);
            f.Io.Count("close-read").ShouldBe(1);
        }
        foreach (var mode in new[] { "progress", "stall", "cancel-before", "cancel-during", "eof", "grow", "shrink", "inspect" })
        {
            var f = new HostCleanupCheckpointFixture();
            var bytes = Enumerable.Range(0, 4 * 65536).Select(i => (byte)(i * 31)).ToArray();
            var node = f.Io.Put(f.Payload, bytes);
            using var caller = new CancellationTokenSource();
            if (mode == "cancel-before") caller.Cancel();
            var reads = 0;
            f.Io.StreamFactory = () => new PacedBytes(bytes, () =>
            {
                reads++;
                if (mode == "cancel-during" && reads == 2) caller.Cancel();
                if (reads == 5 && mode is "eof" or "grow" or "shrink" or "inspect")
                {
                    if (mode == "grow") node.Stat = node.Stat with { Size = bytes.Length + 1 };
                    if (mode == "shrink") node.Stat = node.Stat with { Size = bytes.Length - 1 };
                    if (mode == "inspect") f.Io.InspectFails = true;
                    f.Clock.Advance(CheckpointFileReader.StallDeadline);
                }
                else f.Clock.Advance(mode == "stall" && reads == 2 ? CheckpointFileReader.StallDeadline : TimeSpan.FromSeconds(1));
            });
            if (mode is "progress" or "eof")
            {
                var digest = ReceiptFile.Digest(f.Context, f.Payload, caller.Token);
                digest.Length.ShouldBe(bytes.Length);
                digest.Sha256.ShouldBe(Convert.ToHexString(SHA256.HashData(bytes)));
                reads.ShouldBe(5);
            }
            else
            {
                var error = Should.Throw<IOException>(() => ReceiptFile.Digest(f.Context, f.Payload, caller.Token));
                if (mode.StartsWith("cancel", StringComparison.Ordinal)) error.InnerException.ShouldBeAssignableTo<OperationCanceledException>();
                else error.Message.ShouldContain(mode == "inspect" ? "inspect failed" : "read-stalled");
            }
            f.Io.Count("open-read").ShouldBe(mode == "cancel-before" ? 0 : 1);
            f.Io.Count("close-read").ShouldBe(mode == "cancel-before" ? 0 : 1);
            f.Io.Count("inspect").ShouldBe(mode == "cancel-before" ? 0 : mode is "eof" or "grow" or "shrink" or "inspect" ? 2 : 1);
            f.Io.Events.ShouldNotContain(e => e.Operation == "write" || e.Operation == "unlink" || e.Operation == "rmdir");
            node.Bytes.ShouldBe(bytes);
        }
    }

    private sealed class PacedBytes(byte[] bytes, Action beforeRead) : MemoryStream(bytes, false)
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            beforeRead();
            return base.ReadAsync(buffer[..Math.Min(buffer.Length, 65536)], cancellationToken);
        }
    }

    private sealed class ZeroStream(long length) : Stream
    {
        public long Delivered { get; private set; }
        public int LargestRequest { get; private set; }
        public override int Read(byte[] buffer, int offset, int count)
        {
            LargestRequest = Math.Max(LargestRequest, count);
            var take = (int)Math.Min(count, length - Delivered);
            buffer.AsSpan(offset, take).Clear();
            Delivered += take;
            return take;
        }
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LargestRequest = Math.Max(LargestRequest, buffer.Length);
            var take = (int)Math.Min(buffer.Length, length - Delivered);
            buffer.Span[..take].Clear();
            Delivered += take;
            return ValueTask.FromResult(take);
        }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => length;
        public override long Position { get => Delivered; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
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
