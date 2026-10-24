using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Antiphon.Checkpoints;
using Antiphon.HostCleanup.Checkpoints;
using Shouldly;
using TUnit.Core;
using CheckpointProgram = Antiphon.Checkpoints.Program;

namespace Antiphon.Tests.Infrastructure;

[Category("Unit")]
public sealed class HostCleanupCheckpointAuthorityTests
{
    [Test]
    public void Virtual_Windows_paths_preserve_one_file_across_separator_spellings()
    {
        // Pure strings: exercise the virtual Windows filesystem without a native lock or process.
        var io = new VirtualCheckpointIo(new Antiphon.Tests.TestHelpers.ControlledTimeProvider(), CheckpointPlatform.Windows);
        const string written = @"C:\c987\owned\payload.txt";
        const string observed = "C:/c987/owned/payload.txt";
        io.Put(written, "keep");
        io.FileExists(observed).ShouldBeTrue("Windows accepts both directory separators");
        io.Text(observed).ShouldBe("keep");
        io.Unlink(observed);
        io.FileExists(written).ShouldBeFalse("an alias must address the same virtual file");
    }

    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private static string Label(string method, string variant) => "C987." + method + "." + variant;
    private static string AuthorityPath(HostCleanupToolFixture f) => Path.Combine(f.Run, "tool-copy-authority.json");

    [Test]
    [Arguments(0)] [Arguments(1)]
    public async Task Fresh_context_removes_only_sealed_tool(int exit)
    {
        var f = new HostCleanupToolFixture(exit);
        await f.StartAsync();
        var before = Evidence(f);
        var fresh = f.Files.FreshContext();
        fresh.Registry.WasCreated(f.Tool).ShouldBeFalse();
        (await f.Wait(fresh)).ShouldBe(exit, Label(nameof(Fresh_context_removes_only_sealed_tool), "cross-context") + ": " + f.LastWaitOutput);
        f.Io.DirectoryExists(f.Tool).ShouldBeFalse();
        foreach (var (path, hash) in before) Hash(f.Io.Nodes[path].Bytes).ShouldBe(hash);
        f.Io.DirectoryExists(f.Receipt.Output).ShouldBeTrue();
        fresh.Registry.WasCreated(f.Tool).ShouldBeFalse();
    }

    [Test]
    public void Tool_authority_does_not_seed_generic_registry()
    {
        var f = new HostCleanupToolFixture(sealAuthority: true);
        var fresh = f.Files.FreshContext();
        ToolCopyAuthority.Load(f.Run, f.Owner, out var refusal, fresh).ShouldNotBeNull(); refusal.ShouldBeNull();
        new CheckpointOutputLedger(fresh).Check(f.Tool)!.StartsWith("output-memory-authority-missing", StringComparison.Ordinal).ShouldBeTrue(
            Label(nameof(Tool_authority_does_not_seed_generic_registry), "authority-import"));
        fresh.Registry.WasCreated(f.Payload).ShouldBeFalse(); fresh.Registry.WasCreated(f.Tool).ShouldBeFalse();
        f.Cleanup(fresh).Remove(f.Run).Outcome.ShouldBe("Removed");
        fresh.Registry.WasCreated(f.Payload).ShouldBeFalse();
    }

    [Test]
    [Arguments("added")] [Arguments("changed")] [Arguments("forged-ledger")]
    [Arguments("missing")] [Arguments("malformed")] [Arguments("foreign")]
    [Arguments("escape")] [Arguments("linked")] [Arguments("hardlinked")] [Arguments("replaced")]
    public async Task Fresh_context_retains_unproven_tool(string scenario)
    {
        const string method = nameof(Fresh_context_retains_unproven_tool);
        var positive = new HostCleanupToolFixture(sealAuthority: true);
        ToolCopyAuthority.Load(positive.Run, positive.Owner, out var valid, positive.Files.FreshContext()).ShouldNotBeNull();
        valid.ShouldBeNull();
        var variants = scenario switch
        {
            "added" => new[] { "roster-count", "roster-membership", "late-addition", "seal-creation" },
            "changed" => new[] { "content", "stat-before", "stat-after", "seal-stat" },
            "malformed" => new[] { "digest", "unique-properties", "version", "duplicate-entry", "entry-count", "entry-count-large", "json" },
            "foreign" => new[] { "run-id", "run-path", "tool-path", "creator" },
            "escape" => new[] { "relative-path", "relative-rooted", "relative-backslash" },
            "replaced" => new[] { "directory-device", "directory-inode", "directory-created", "remove-target", "final-entry", "final-subdirectory", "final-root" },
            _ => new[] { scenario }
        };
        foreach (var variant in variants)
        {
            var f = new HostCleanupToolFixture(sealAuthority: true);
            var fresh = f.Files.FreshContext();
            var label = Label(method, variant);
            var record = JsonSerializer.Deserialize<ToolCopyAuthority.CopyRecord>(f.Io.Text(AuthorityPath(f)), Json)!;
            string expected = "tool-authority-entry-mismatch";
            if (variant.StartsWith("final-", StringComparison.Ordinal) || variant is "late-addition" or "remove-target")
            {
                FinalRemovalGuard(f, variant, label);
                continue;
            }
            if (variant.StartsWith("seal-", StringComparison.Ordinal))
            {
                SealGuard(variant, label);
                continue;
            }
            switch (variant)
            {
                case "roster-count": f.Io.Nodes.Remove(f.Payload); expected = "tool-authority-inventory-mismatch"; break;
                case "roster-membership": f.Io.Nodes.Remove(f.Payload); f.Io.Put(Path.Combine(f.Tool, "foreign.dll"), "keep"); break;
                case "content": f.Io.Nodes[f.Payload].Bytes = Encoding.UTF8.GetBytes("evil"); break;
                case "stat-before": f.Io.Nodes[f.Payload].Stat = f.Io.Nodes[f.Payload].Stat with { Changed = 42 }; break;
                case "stat-after":
                    f.Io.AfterRead = path => { if (path == f.Payload) f.Io.Nodes[path].Stat = f.Io.Nodes[path].Stat with { Inode = 9898 }; };
                    break;
                case "digest": f.Owner.ToolCopy = new(new string('0', 64)); expected = "tool-authority-digest-mismatch"; break;
                case "unique-properties":
                    Rehash(f, f.Io.Text(AuthorityPath(f)).Replace("\"version\":1", "\"version\":1,\"version\":1"));
                    expected = "tool-authority-unreadable"; break;
                case "json": Rehash(f, "{"); expected = "tool-authority-unreadable"; break;
                case "version": record.Version = 2; Rewrite(f, record); expected = "tool-authority-run-mismatch"; break;
                case "run-id": record.RunId = "foreign"; Rewrite(f, record); expected = "tool-authority-run-mismatch"; break;
                case "run-path": record.RunDirectory += "-foreign"; Rewrite(f, record); expected = "tool-authority-run-mismatch"; break;
                case "tool-path": record.ToolDirectory += "-foreign"; Rewrite(f, record); expected = "tool-authority-run-mismatch"; break;
                case "creator": record.Creator = record.Creator with { StartUtcTicks = 999 }; Rewrite(f, record); expected = "tool-authority-run-mismatch"; break;
                case "relative-path": record.Entries[0].Path = "../outside"; Rewrite(f, record); expected = "tool-authority-path-invalid"; break;
                case "relative-rooted": record.Entries[0].Path = "/outside"; Rewrite(f, record); expected = "tool-authority-path-invalid"; break;
                case "relative-backslash": record.Entries[0].Path = "..\\outside"; Rewrite(f, record); expected = "tool-authority-path-invalid"; break;
                case "duplicate-entry": record.Entries.Add(record.Entries[0]); Rewrite(f, record); expected = "tool-authority-path-invalid"; break;
                case "entry-count": record.Entries.Clear(); Rewrite(f, record); expected = "tool-authority-path-invalid"; break;
                case "entry-count-large":
                    record.Entries = Enumerable.Range(0, 10001).Select(i => new ToolCopyAuthority.CopyEntry { Path = "file" + i, Content = "0:", Stamp = record.Entries[0].Stamp }).ToList();
                    Rewrite(f, record); expected = "tool-authority-path-invalid"; break;
                case "directory-device": f.Io.Nodes[f.Tool].Stat = f.Io.Nodes[f.Tool].Stat with { Device = 2 }; expected = "tool-authority-root-mismatch"; break;
                case "directory-inode": f.Io.Nodes[f.Tool].Stat = f.Io.Nodes[f.Tool].Stat with { Inode = 900 }; expected = "tool-authority-root-mismatch"; break;
                case "directory-created":
                    record.Root = record.Root with { Inode = 0 }; Rewrite(f, record);
                    ToolCopyAuthority.Load(f.Run, f.Owner, out var fallbackRefusal, fresh).ShouldNotBeNull(label + ": unchanged portable identity");
                    fallbackRefusal.ShouldBeNull(label);
                    f.Io.Nodes[f.Tool].CreatedUtc = f.Io.Nodes[f.Tool].CreatedUtc.AddTicks(1);
                    expected = "tool-authority-root-mismatch"; break;
                case "missing": f.Io.Nodes.Remove(AuthorityPath(f)); expected = "tool-authority-unreadable"; break;
                case "forged-ledger":
                    f.Owner.ToolCopy = null; f.Io.Put(Path.Combine(f.Tool, ".checkpoint-output-ledger.json"), "{}");
                    RunOwnershipStore.Write(f.Run, f.Owner, f.Context);
                    new CheckpointOutputLedger(fresh).Check(f.Tool)!.StartsWith("output-memory-authority-missing", StringComparison.Ordinal).ShouldBeTrue(label);
                    f.Cleanup(fresh).Remove(f.Run).Reason.Contains("output-memory-authority-missing", StringComparison.Ordinal).ShouldBeTrue(label);
                    (await f.Wait(fresh)).ShouldBe(2, label);
                    f.Io.Text(f.Payload).ShouldBe("keep", label); continue;
                case "linked":
                    f.Io.Put(Path.Combine(f.Files.Parent, "outside"), "outside-sentinel");
                    f.Io.Nodes[f.Payload].Target = Path.Combine(f.Files.Parent, "outside");
                    f.Io.Nodes[f.Payload].Stat = f.Io.Nodes[f.Payload].Stat with { Kind = "symlink", Mode = 0xA180 };
                    expected = "tool-authority-inventory-unreadable"; break;
                case "hardlinked":
                    f.Io.Put(Path.Combine(f.Files.Parent, "outside"), "outside-sentinel");
                    f.Io.Nodes[f.Payload].Stat = f.Io.Nodes[f.Payload].Stat with { Links = 2 };
                    expected = "tool-authority-inventory-unreadable"; break;
                default: throw new InvalidOperationException(variant);
            }
            // Bind any digest-only change into the journal too; field mutations cannot hide behind the seal hash.
            RunOwnershipStore.Write(f.Run, f.Owner, f.Context);
            f.Receipt.ClearEvents();
            ToolCopyAuthority.Load(f.Run, f.Owner, out var refusal, fresh).ShouldBeNull(label);
            refusal.ShouldBe(expected, label);
            f.Io.Count(variant == "missing" ? "stat" : "open-read", AuthorityPath(f)).ShouldBeGreaterThan(0, label);
            f.Io.Count("unlink").ShouldBe(0, label);
            (await f.Wait(fresh)).ShouldBe(2, label);
            f.Io.DirectoryExists(f.Tool).ShouldBeTrue(label);
            if (scenario is "linked" or "hardlinked") f.Io.Text(Path.Combine(f.Files.Parent, "outside")).ShouldBe("outside-sentinel", label);
            if (variant == "tool-path") f.Io.Events.Any(e => e.Path.StartsWith(record.ToolDirectory, StringComparison.Ordinal)).ShouldBeFalse(label);
        }
    }

    private static void FinalRemovalGuard(HostCleanupToolFixture f, string variant, string label)
    {
        // Add the nested positive before sealing so the later directory recheck is independently reached.
        if (variant == "final-subdirectory")
        {
            SafeWrite.Require(f.Files.Write.CreateDirectory(Path.Combine(f.Tool, "nested"), f.Files.Write.Under(f.Tool)));
            SafeWrite.Require(f.Files.Write.Text(Path.Combine(f.Tool, "nested", "child"), "keep", f.Files.Write.Under(f.Tool)));
            f.Owner.ToolCopy = ToolCopyAuthority.Seal(f.Run, f.Owner, f.Context);
        }
        var authority = ToolCopyAuthority.Load(f.Run, f.Owner, out var refusal, f.Files.FreshContext());
        authority.ShouldNotBeNull(label); refusal.ShouldBeNull(label); f.Receipt.ClearEvents();
        if (variant == "remove-target")
        {
            authority.Remove(f.Tool + "-foreign").ShouldBe("tool-authority-root-mismatch", label);
            f.Io.Count("unlink").ShouldBe(0, label); return;
        }
        var target = variant == "final-subdirectory" ? Path.Combine(f.Tool, "nested") : variant == "final-entry" ? f.Payload : f.Tool;
        var observations = 0;
        f.Io.Before = (op, path) =>
        {
            if (variant == "late-addition" && op == "rmdir" && path == f.Tool)
                f.Io.Put(Path.Combine(f.Tool, "late"), "keep-late");
            else if (op == "stat" && path == target && ++observations == (variant == "final-root" ? 2 : variant == "final-entry" ? 4 : 3))
                f.Io.Nodes[target].Stat = f.Io.Nodes[target].Stat with { Inode = 98765 };
        };
        if (variant == "late-addition")
        {
            Should.Throw<IOException>(() => authority.Remove(f.Tool), label);
            f.Io.Text(Path.Combine(f.Tool, "late")).ShouldBe("keep-late", label);
            f.Io.RecursiveIntents.ShouldBe(0, label);
        }
        else
        {
            authority.Remove(f.Tool).ShouldBe(variant == "final-root" ? "tool-authority-root-mismatch" : "tool-authority-entry-mismatch", label);
            observations.ShouldBeGreaterThanOrEqualTo(variant == "final-root" ? 2 : variant == "final-entry" ? 4 : 3, label);
            f.Io.Count(variant == "final-entry" ? "unlink" : "rmdir", target).ShouldBe(0, label);
        }
    }

    private static void SealGuard(string variant, string label)
    {
        var f = new HostCleanupToolFixture();
        var inventories = 0; var reads = 0;
        f.Io.Before = (op, path) =>
        {
            if (variant == "seal-creation" && op == "enumerate" && path == f.Tool && ++inventories == 2)
                f.Io.Put(Path.Combine(f.Tool, "unrecorded"), "outside-creation");
        };
        f.Io.AfterRead = path =>
        {
            if (variant == "seal-stat" && path == f.Payload && f.Io.Count("open-read", path) == 2 && reads++ == 0)
                f.Io.Nodes[path].Stat = f.Io.Nodes[path].Stat with { Inode = 4444 };
        };
        Should.Throw<IOException>(() => ToolCopyAuthority.Seal(f.Run, f.Owner, f.Context), label)
            .Message.Contains("tool-authority-creation-mismatch", StringComparison.Ordinal).ShouldBeTrue(label);
        f.Io.FileExists(AuthorityPath(f)).ShouldBeFalse(label);
        (variant == "seal-creation" ? inventories : f.Io.Count("open-read", f.Payload)).ShouldBe(2, label);
    }

    [Test]
    public async Task Invalid_terminal_receipt_prevents_cleanup()
    {
        const string method = nameof(Invalid_terminal_receipt_prevents_cleanup);
        foreach (var variant in new[] { "required-before-cleanup", "expected-sha", "dead-undone", "unknown-wait" })
        {
            var f = new HostCleanupToolFixture(sealAuthority: true);
            ReportWriter.RequiredReceiptRefusal(f.Context, f.Run, f.Receipt.State).ShouldBeNull();
            if (variant == "required-before-cleanup") f.Io.Put(f.Receipt.MarkdownPath, "corrupt");
            if (variant == "dead-undone") { f.Receipt.State.Phase = "running"; f.Io.Put(f.Receipt.StatePath, JsonSerializer.Serialize(f.Receipt.State, RunStateStore.Json)); }
            if (variant == "unknown-wait") { f.Owner.Launched = f.Dead with { Host = "foreign" }; RunOwnershipStore.Write(f.Run, f.Owner, f.Context); }
            f.Receipt.ClearEvents();
            var output = new StringWriter();
            var exit = await new WaitCommand(context: f.Files.FreshContext()).WaitAsync(f.Run, TimeSpan.Zero, TimeSpan.FromSeconds(1), output, CancellationToken.None,
                variant == "expected-sha" ? new string('b', 40) : null);
            exit.ShouldBe(variant == "dead-undone" ? 6 : variant == "unknown-wait" ? 75 : 2, Label(method, variant));
            if (variant == "dead-undone") output.ToString().ShouldContain("executor died without phase=done");
            else { f.Io.Count("open", Path.Combine(f.Run, ".cleanup.lock")).ShouldBe(0); f.Io.Count("unlink").ShouldBe(0); f.Io.DirectoryExists(f.Tool).ShouldBeTrue(); }
        }
        // CARD-1302 item 3: cancellation reaches an already-entered production receipt read.
        var cancelFixture = new HostCleanupToolFixture(sealAuthority: true);
        using var canceled = new CancellationTokenSource();
        var stream = new CancellableRead();
        cancelFixture.Io.StreamFactory = () => stream;
        var waiting = Task.Run(() => cancelFixture.Wait(ct: canceled.Token));
        await stream.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        try
        {
            canceled.Cancel();
            stream.Token.IsCancellationRequested.ShouldBeTrue("C987.Invalid_terminal_receipt_prevents_cleanup.caller-cancellation");
            await Should.ThrowAsync<OperationCanceledException>(() => waiting.WaitAsync(TimeSpan.FromSeconds(5)));
            stream.Token.IsCancellationRequested.ShouldBeTrue();
            cancelFixture.Io.Count("unlink").ShouldBe(0);
        }
        finally { stream.Release.TrySetResult(); try { await waiting; } catch (OperationCanceledException) { } }
    }

    [Test]
    public void Marker_transition_requires_gate_identity_and_dead_owner()
    {
        const string method = nameof(Marker_transition_requires_gate_identity_and_dead_owner);
        foreach (var variant in new[] { "positive", "gate-writable", "gate-name", "current-state", "expected-state", "root-id", "attempt", "assembly", "created", "owner", "root-path", "fresh-death", "fresh-unknown", "fresh-reused", "first-bytes", "final-bytes", "foreign-file" })
        {
            var f = new HostCleanupToolFixture(); var root = Path.Combine(f.Files.Parent, "c723-marker");
            f.Io.CreateDirectory(root);
            var current = new CheckpointRootMarker { RootId = "marker", RootPath = root, AttemptId = "attempt", AssemblyInvocationId = "assembly", CreatedAt = f.Files.Clock.GetUtcNow(), Owner = f.Dead };
            TestRootGuard.Write(root, current, f.Context);
            var expected = JsonSerializer.Deserialize<CheckpointRootMarker>(JsonSerializer.Serialize(current))!; expected.State = "deleting";
            var marker = Path.Combine(root, TestRootGuard.MarkerName); var label = Label(method, variant);
            switch (variant)
            {
                case "current-state": current.State = "sealed"; f.Io.Put(marker, JsonSerializer.Serialize(current)); break;
                case "expected-state": expected.State = "active"; break;
                case "root-id": expected.RootId = "other"; break;
                case "attempt": expected.AttemptId = "other"; break;
                case "assembly": expected.AssemblyInvocationId = "other"; break;
                case "created": expected.CreatedAt = expected.CreatedAt.AddTicks(1); break;
                case "owner": expected.Owner = expected.Owner! with { Pid = 876 }; break;
                case "root-path": expected.RootPath += "-other"; break;
                case "fresh-death": f.Io.Processes[f.Dead.Pid] = new(f.Dead.StartUtcTicks, 'R', false); break;
                case "fresh-reused": f.Io.Processes[f.Dead.Pid] = new(f.Dead.StartUtcTicks + 1, 'R', false); break;
                case "fresh-unknown": f.Io.Before = (op, path) => { if (op == "process" && path == f.Dead.Pid.ToString()) throw new IOException("unknown"); }; break;
            }
            var gatePath = Path.Combine(root, TestRootGuard.LockName);
            using var gate = variant is "gate-writable" or "gate-name"
                ? f.Io.Open(variant == "gate-name" ? Path.Combine(root, "other.lock") : gatePath, FileMode.OpenOrCreate,
                    variant == "gate-writable" ? FileAccess.Read : FileAccess.ReadWrite, FileShare.None)
                : TestRootGuard.TryLock(f.Context, root)!;
            Should.Throw<IOException>(() => f.Io.Open(gatePath == f.Io.StreamPath(gate) ? gatePath : Path.Combine(root, "other.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None));
            var before = f.Io.Text(marker);
            if (variant == "foreign-file")
            {
                var foreign = Path.Combine(root, "foreign.json"); f.Io.Put(foreign, "foreign");
                f.Files.Write.Text(foreign, "changed", f.Files.Write.Under(root)).ShouldBe("unrecorded-existing", label);
                f.Io.Text(foreign).ShouldBe("foreign", label); continue;
            }
            var reads = 0;
            if (variant is "first-bytes" or "final-bytes")
            {
                f.Io.Before = (op, path) =>
                {
                    if (op == "open-read" && path == marker && ++reads == (variant == "first-bytes" ? 2 : 3))
                    { current.AttemptId = "replacement"; f.Io.Put(marker, JsonSerializer.Serialize(current)); }
                };
            }
            if (variant == "positive")
            {
                TestRootGuard.MarkDeleting(f.Context, root, expected, gate, new(f.Context));
                TestRootGuard.Read(root, f.Context)!.State.ShouldBe("deleting", label);
            }
            else
            {
                var error = Should.Throw<IOException>(() => TestRootGuard.MarkDeleting(f.Context, root, expected, gate, new(f.Context)), label);
                error.Message.Contains(variant is "first-bytes" or "final-bytes" ? "root-marker-changed" : "root-marker-transition-unproven", StringComparison.Ordinal).ShouldBeTrue(label);
                f.Io.Text(marker).ShouldBe(variant is "first-bytes" or "final-bytes" ? JsonSerializer.Serialize(current) : before, label);
            }
        }
    }

    [Test]
    public async Task Every_tool_caller_uses_the_injected_context()
    {
        const string method = nameof(Every_tool_caller_uses_the_injected_context);
        var f = new HostCleanupToolFixture();
        var run = CheckpointApp.CreateRun(HostCleanupToolFixture.Manifest(), new() { Branch = "virtual" }, f.Files.Parent, f.Runtime());
        f.Io.Count("mkdir-exclusive", run).ShouldBe(1, Label(method, "create"));
        await f.StartAsync();
        f.Io.Count("mkdir-exclusive", f.Tool).ShouldBe(1, Label(method, "start"));
        f.Io.Count("copy", Path.Combine(f.Tool, "Antiphon.Checkpoints.dll")).ShouldBe(1, Label(method, "start"));
        f.Io.FileExists(AuthorityPath(f)).ShouldBeTrue(Label(method, "start"));
        ExecutorOwnershipStore.Write(f.Run, f.Dead, f.Context);
        f.Io.Count("move", Path.Combine(f.Run, "executor-ownership.json")).ShouldBe(1, Label(method, "ack"));
        (await f.Wait(f.Files.FreshContext())).ShouldBe(0, Label(method, "wait") + ": " + f.LastWaitOutput);
        f.Io.Count("rmdir", f.Tool).ShouldBe(1, Label(method, "wait"));

        var runFixture = new HostCleanupToolFixture();
        var source = Path.Combine(runFixture.Files.Parent, "source");
        runFixture.Io.CreateDirectory(source); runFixture.Io.Put(Path.Combine(source, "Antiphon.Checkpoints.dll"), "image");
        var manifestPath = Path.Combine(runFixture.Files.Parent, "manifest.yaml");
        runFixture.Io.Put(manifestPath, ManifestLoader.ToYaml(HostCleanupToolFixture.Manifest()));
        var launches = 0; string? awaitedRun = null;
        var runtime = new CheckpointApp.Runtime
        {
            CleanupContext = runFixture.Context, RepoRoot = runFixture.Files.Parent, SourceBranch = "virtual", EnvironmentLookup = _ => null,
            SourceCapture = _ => runFixture.Receipt.State.Source.Start, ToolDirectory = source,
            LaunchWithOutcome = _ => { launches++; return new(LaunchKind.Started, runFixture.Dead.Pid, runFixture.Dead); },
            Wait = r => { awaitedRun = r; return Task.FromResult(0); }, Output = new StringWriter()
        };
        (await CheckpointProgram.RunAsync(["run", manifestPath, "--repo-root", runFixture.Files.Parent], runtime)).ShouldBe(0, Label(method, "run"));
        launches.ShouldBe(1); awaitedRun.ShouldNotBeNull();
        runFixture.Io.Count("open-read", manifestPath).ShouldBe(1);
        runFixture.Io.FileExists(Path.Combine(awaitedRun, "tool-copy-authority.json")).ShouldBeTrue();

        var stop = new HostCleanupToolFixture();
        stop.Owner.Launched = new ProcessIdentityProbe(stop.Context).Current(); RunOwnershipStore.Write(stop.Run, stop.Owner, stop.Context);
        stop.Receipt.State.Phase = "running"; stop.Io.Put(stop.Receipt.StatePath, JsonSerializer.Serialize(stop.Receipt.State, RunStateStore.Json));
        stop.Receipt.ClearEvents(); var control = new HostCleanupCheckpointCompatibilityTests.CountingControl();
        (await CheckpointProgram.RunAsync(["stop", "--run", stop.Run, "--repo-root", stop.Files.Parent], stop.Runtime(control))).ShouldBe(75, Label(method, "stop"));
        control.Calls.ShouldBe(1); stop.Io.Count("open-read", stop.Receipt.StatePath).ShouldBeGreaterThan(0);
        (await CheckpointProgram.RunAsync(["clean", "--run", stop.Run, "--repo-root", stop.Files.Parent], stop.Runtime())).ShouldBe(2, Label(method, "clean"));
        stop.Io.Count("open-read", stop.Receipt.JsonPath).ShouldBeGreaterThan(0);

        var evidence = new HostCleanupToolFixture();
        EvidenceFolder.Write(evidence.Run, new() { RunId = Path.GetFileName(evidence.Run) }, true, context: evidence.Context);
        evidence.Io.Count("rmdir", evidence.Tool).ShouldBe(1, Label(method, "evidence"));
        var output = new HostCleanupCheckpointFixture(); output.Seal();
        var removed = new CheckpointOutputCleanup(output.Context).CleanOwnedOutputs(output.Parent, ["bin-owned"], 0, false, false, createdPaths: [output.Root]);
        removed.Deleted.ShouldContain(output.Root, Label(method, "output"));
        output.Io.DirectoryExists(output.Root).ShouldBeFalse();
        await HostCleanupCheckpointCompatibilityTests.RetryDelayProof(Label(method, "delays"));
        // Current-image observation is injected as well, with zero unlink attempts.
        var image = new HostCleanupToolFixture(); image.Io.ImageDirectory = Path.Combine(image.Tool, "child");
        image.Cleanup().Remove(image.Run).Reason.ShouldBe("current-executor-image"); image.Io.Count("unlink").ShouldBe(0);
    }

    private static Dictionary<string, string> Evidence(HostCleanupToolFixture f) =>
        f.Io.Nodes.Where(p => Path.GetDirectoryName(p.Key) == f.Run && p.Value.Stat.Kind == "file")
            .ToDictionary(p => p.Key, p => Hash(p.Value.Bytes));
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private static void Rewrite(HostCleanupToolFixture f, ToolCopyAuthority.CopyRecord record) => Rehash(f, JsonSerializer.Serialize(record, Json));
    private static void Rehash(HostCleanupToolFixture f, string json)
    {
        f.Io.Put(AuthorityPath(f), json); f.Owner.ToolCopy = new(Hash(Encoding.UTF8.GetBytes(json)));
    }
    private sealed class CancellableRead : MemoryStream
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public CancellationToken Token { get; private set; }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Token = cancellationToken; Entered.TrySetResult(); await Release.Task.WaitAsync(cancellationToken); return 0;
        }
    }
}
