using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Antiphon.Checkpoints;
using Antiphon.HostCleanup.Checkpoints;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Infrastructure;

[Category("Unit")]
public sealed class HostCleanupCheckpointReceiptTests
{
    [Test]
    public void Receipt_types_are_checked_before_open()
    {
        const string label = "C987.Receipt_types_are_checked_before_open.";
        var control = new HostCleanupReceiptFixture();
        ReceiptFile.ReadText(control.Context, control.StatePath).ShouldContain("verified-run", Case.Sensitive, label + "wrapper-route:bytes");
        control.Io.Count("open-read", control.StatePath).ShouldBe(1, label + "wrapper-route:verified-open");
        control.Io.Count("inspect", control.StatePath).ShouldBe(1, label + "wrapper-route:verified-handle");
        control.Io.Opens.ShouldBeEmpty(label + "wrapper-route:no-raw-open");
        foreach (var kind in new[] { "fifo", "directory", "symlink", "device" })
        {
            var f = new HostCleanupReceiptFixture();
            var n = f.Io.Nodes[f.StatePath];
            n.Stat = n.Stat with { Kind = kind, Mode = kind switch { "fifo" => 0x1180, "directory" => 0x41C0, "symlink" => 0xA1FF, _ => 0x2180 } };
            Should.Throw<IOException>(() => ReceiptFile.ReadBytes(f.Context, f.StatePath)).Message
                .ShouldContain("not regular", Case.Sensitive, label + "wrapper-route:" + kind);
            f.Io.Count("stat", f.StatePath).ShouldBe(1, label + kind + ":prefilter-entry");
            f.Io.Count("open-read").ShouldBe(0, label + kind + ":no-open");
        }
        foreach (var attribute in new[] { FileAttributes.Directory, FileAttributes.ReparsePoint })
        {
            var f = new HostCleanupReceiptFixture();
            f.Io.Platform = CheckpointPlatform.Windows;
            ReceiptFile.ReadText(f.Context, f.StatePath).ShouldContain("verified-run", Case.Sensitive,
                label + "windows-preopen:positive-pair");
            f.ClearEvents();
            f.Io.Nodes[f.StatePath].Attributes = attribute;
            Should.Throw<IOException>(() => ReceiptFile.ReadBytes(f.Context, f.StatePath)).Message
                .ShouldContain("receipt-nonregular", Case.Sensitive, label + "windows-preopen:" + attribute);
            f.Io.Count("attributes", f.StatePath).ShouldBe(1, label + "windows-preopen:entry");
            f.Io.Count("open-read").ShouldBe(0, label + "windows-preopen:no-open");
        }
        foreach (var variant in new[] { "held-type", "held-links", "outside", "truncated", "inspect-error" })
        {
            var f = new HostCleanupReceiptFixture();
            f.Io.HandleMetadata = m => variant switch
            {
                "held-type" => m with { Regular = false },
                "held-links" => m with { Links = 2 },
                "outside" => m with { Path = Path.Combine(f.Files.Parent, "outside") },
                _ => m
            };
            f.Io.InspectFails = variant == "inspect-error";
            if (variant == "truncated") f.Io.StreamFactory = () => new MemoryStream(Encoding.UTF8.GetBytes("{"));
            if (variant == "truncated")
                Should.Throw<JsonException>(() => ReceiptJson.Read<RunState>(f.Context, f.StatePath, RunStateStore.Json));
            else
                Should.Throw<IOException>(() => ReceiptFile.ReadBytes(f.Context, f.StatePath));
            f.Io.Count("inspect", f.StatePath).ShouldBe(1, label + variant + ":held-inspected");
            f.Io.Count("stream").ShouldBe(variant == "truncated" ? 1 : 0, label + variant + ":stream-entry");
            NoMutation(f, label + variant);
        }
    }

    [Test]
    public void Receipt_read_uses_verified_handle()
    {
        const string label = "C987.Receipt_read_uses_verified_handle.parser-route";
        var f = new HostCleanupReceiptFixture();
        var replacement = JsonNode.Parse(f.Io.Text(f.StatePath))!;
        replacement["runId"] = "replacement-run";
        f.Io.Before = (op, path) =>
        {
            if (op == "stream" && path == f.StatePath) f.Io.Put(path, replacement.ToJsonString());
        };
        ReceiptJson.Read<RunState>(f.Context, f.StatePath, RunStateStore.Json).RunId
            .ShouldBe("verified-run", label);
        f.Io.Text(f.StatePath).ShouldContain("replacement-run", Case.Sensitive, label + ":swap-reached");
        f.Io.Count("open-read", f.StatePath).ShouldBe(1, label + ":one-handle");
        f.Io.Count("inspect", f.StatePath).ShouldBe(1, label + ":verified");
        f.Io.Opens.ShouldBeEmpty(label + ":no-raw-path-open");
        NoMutation(f, label);
    }

    [Test]
    public async Task Receipt_size_and_deadline_are_bounded()
    {
        const string label = "C987.Receipt_size_and_deadline_are_bounded.";
        const int limit = 64 * 1024 * 1024;
        foreach (var size in new[] { limit, limit + 1 })
        {
            var f = new HostCleanupReceiptFixture();
            f.Io.Put(f.StatePath, new byte[size]);
            if (size == limit)
                ReceiptFile.ReadBytes(f.Context, f.StatePath).Length.ShouldBe(limit, label + "receipt-cap:exact");
            else
                Should.Throw<IOException>(() => ReceiptFile.ReadBytes(f.Context, f.StatePath)).Message
                    .ShouldContain("exceeds size limit", Case.Sensitive, label + "receipt-cap:over");
            f.Io.Count("inspect", f.StatePath).ShouldBe(1, label + "receipt-cap:entry");
            f.Io.Count("stream").ShouldBe(size == limit ? 1 : 0, label + "receipt-cap:stream-entry");
        }
        foreach (var cancel in new[] { false, true })
        {
            var f = new HostCleanupReceiptFixture();
            using var source = new CancellationTokenSource();
            var stream = new BlockedReceiptStream();
            f.Io.StreamFactory = () => stream;
            var read = Task.Run(() => ReceiptJson.Read<RunState>(f.Context, f.StatePath, RunStateStore.Json, source.Token));
            var variant = cancel ? "cancellation" : "deadline";
            try
            {
                await stream.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
                if (cancel) source.Cancel(); else f.Files.Clock.Advance(CheckpointFileReader.StallDeadline);
                await Task.WhenAny(read, Task.Delay(TimeSpan.FromSeconds(2)));
                read.IsCompleted.ShouldBeTrue(label + variant + ":completed-before-release");
                var ex = await Should.ThrowAsync<IOException>(async () => await read);
                ex.Message.ShouldStartWith("receipt-read-refused: " + f.StatePath + ": ", Case.Sensitive, label + variant + ":wrapper");
                if (cancel)
                {
                    ex.InnerException.ShouldBeAssignableTo<OperationCanceledException>(label + variant);
                    ex.Message.ShouldNotContain("read-stalled", Case.Sensitive, label + variant + ":not-a-stall");
                }
                else
                {
                    // CARD-1358 distinguishes a silent read from cancellation requested by the caller.
                    var stalled = ex.InnerException.ShouldBeOfType<IOException>(label + variant);
                    stalled.Message.ShouldBe($"read-stalled: 0 of {f.Io.Nodes[f.StatePath].Bytes.Length} bytes; no progress within {CheckpointFileReader.StallDeadline.TotalSeconds:0}s",
                        label + variant + ":stall-detail");
                }
                f.Io.Count("stream").ShouldBe(1, label + variant + ":blocked-read-entered");
            }
            finally { stream.Release.TrySetResult(); try { await read; } catch (IOException) { } }
        }
    }

    [Test]
    public void Receipt_shape_and_identity_are_required()
    {
        const string label = "C987.Receipt_shape_and_identity_are_required.";
        foreach (var field in new[] { "rows", "builds", "source" })
        {
            var f = new HostCleanupReceiptFixture();
            ReceiptJson.Parse<ReportModel>(f.Io.Text(f.JsonPath), ReportWriter.Json).RunId.ShouldBe("verified-run");
            var json = JsonNode.Parse(f.Io.Text(f.JsonPath))!.AsObject();
            json.Remove(field);
            Should.Throw<JsonException>(() => ReceiptJson.Parse<ReportModel>(json.ToJsonString(), ReportWriter.Json))
                .Message.ShouldContain(field, Case.Sensitive, label + "missing-reference:" + field);
            json = JsonNode.Parse(f.Io.Text(f.JsonPath))!.AsObject();
            json[field] = null;
            Should.Throw<JsonException>(() => ReceiptJson.Parse<ReportModel>(json.ToJsonString(), ReportWriter.Json))
                .Path.ShouldBe("$." + field, label + "serializer-null:" + field);
            typeof(ReportModel).GetProperty(char.ToUpperInvariant(field[0]) + field[1..])!.SetValue(f.Report, null);
            Should.Throw<JsonException>(() => ReceiptJson.Validate(f.Report)).Message
                .ShouldStartWith("receipt-shape: null", Case.Sensitive, label + "validator-null:" + field);
            NoMutation(f, label + field);
        }
        foreach (var variant in new[] { "collection-null", "collection-blank", "output-pair", "markdown-required" })
        {
            var f = new HostCleanupReceiptFixture();
            ReceiptJson.Validate(f.Report);
            switch (variant)
            {
                case "collection-null": f.Report.Rows.Add(null!); break;
                case "collection-blank": f.Report.OutputPaths.Add(" \t"); break;
                case "output-pair": f.Report.OutputPaths.Clear(); break;
                case "markdown-required": f.Report.MarkdownSha256 = " "; break;
            }
            Should.Throw<JsonException>(() => ReceiptJson.Validate(f.Report)).Message.ShouldStartWith("receipt-shape:", Case.Sensitive, label + variant);
            Should.Throw<JsonException>(() => ReceiptJson.Parse<ReportModel>(ReportWriter.JsonText(f.Report), ReportWriter.Json))
                .Message.ShouldStartWith("receipt-shape:", Case.Sensitive, label + variant + ":parse");
            f.SaveReport();
            Should.Throw<JsonException>(() => ReceiptJson.Read<ReportModel>(f.Context, f.JsonPath, ReportWriter.Json))
                .Message.ShouldStartWith("receipt-shape:", Case.Sensitive, label + variant + ":read");
            f.Io.Count("open-read", f.JsonPath).ShouldBe(1, label + variant + ":read-reached");
            NoMutation(f, label + variant);
        }
        foreach (var blank in new string?[] { null, "", " \t" })
        foreach (var target in new[] { "report-row", "report-build", "state-row", "state-build" })
        {
            var f = new HostCleanupReceiptFixture();
            object value = target.StartsWith("report", StringComparison.Ordinal) ? f.Report : f.State;
            ReceiptJson.Validate(value);
            if (target == "report-row") f.Report.Rows[0].Id = blank!;
            if (target == "report-build") f.Report.Builds[0].Id = blank!;
            if (target == "state-row") f.State.Rows[0].Id = blank!;
            if (target == "state-build") f.State.Builds[0].Id = blank!;
            Should.Throw<JsonException>(() => ReceiptJson.Validate(value)).Message.ShouldStartWith("receipt-identity:", Case.Sensitive, label + "empty-id:" + target);
            if (value is ReportModel report)
                Should.Throw<JsonException>(() => ReceiptJson.Parse<ReportModel>(ReportWriter.JsonText(report), ReportWriter.Json))
                    .Message.ShouldStartWith("receipt-identity:", Case.Sensitive, label + "empty-id:parse:" + target);
            else
                Should.Throw<JsonException>(() => ReceiptJson.Parse<RunState>(JsonSerializer.Serialize(f.State, RunStateStore.Json), RunStateStore.Json))
                    .Message.ShouldStartWith("receipt-identity:", Case.Sensitive, label + "empty-id:parse:" + target);
        }
        foreach (var field in new[] { "RunId", "ToolOwner", "Phase", "Verdict", "State", "BuildSource", "CaptureStatus" })
        {
            var f = new HostCleanupReceiptFixture();
            object value = field switch { "Verdict" => f.Report, "State" or "BuildSource" or "CaptureStatus" => f.Report.Source, _ => f.State };
            ReceiptJson.Validate(value);
            if (field == "CaptureStatus") f.Report.Source.Start = f.Report.Source.Start with { CaptureStatus = " " };
            else value.GetType().GetProperty(field)!.SetValue(value, " ");
            Should.Throw<JsonException>(() => ReceiptJson.Validate(value)).Message.ShouldStartWith("receipt-shape: empty", Case.Sensitive, label + "required-string:" + field);
            // Parse the full containing receipt as well as validating the direct object.
            if (value is RunState)
                Should.Throw<JsonException>(() => ReceiptJson.Parse<RunState>(JsonSerializer.Serialize(f.State, RunStateStore.Json), RunStateStore.Json));
            else
                Should.Throw<JsonException>(() => ReceiptJson.Parse<ReportModel>(ReportWriter.JsonText(f.Report), ReportWriter.Json));
        }
        var root = new ReceiptDepthNode();
        var tail = root;
        for (var i = 0; i < 64; i++) tail = tail.Next = new();
        ReceiptJson.Validate(root);
        tail.Next = new();
        Should.Throw<JsonException>(() => ReceiptJson.Validate(root)).Message.ShouldBe("receipt-shape: depth limit", label + "depth");
    }

    [Test]
    public void Terminal_receipt_requires_digest_source_and_exit()
    {
        const string label = "C987.Terminal_receipt_requires_digest_source_and_exit.";
        foreach (var variant in new[] { "state-owner", "state-run", "markdown-digest", "terminal-exit", "report-run",
            "report-exit", "commit", "source-object", "build-stamp-commit", "build-stamp-fingerprint", "state-source" })
        {
            var f = new HostCleanupReceiptFixture();
            ReportWriter.RequiredReceiptRefusal(f.Context, f.Run, f.State).ShouldBeNull(label + variant + ":positive-pair");
            f.ClearEvents();
            var expected = variant switch
            {
                "state-owner" or "state-run" => "receipt-run-directory-mismatching",
                "state-source" => "state-receipt-source-unreadable",
                "markdown-digest" => "receipt-mismatching: report.md",
                "terminal-exit" => "receipt-unreadable-or-terminal-exit-missing",
                "build-stamp-commit" or "build-stamp-fingerprint" => "receipt-source-mismatching: " + f.StampPath,
                _ => "receipt-stale-or-source-mismatching: report.json"
            };
            switch (variant)
            {
                case "state-owner": f.State.ToolOwner = "foreign"; break;
                case "state-run": f.State.RunId = "sibling"; break;
                case "state-source": f.State.Source.Start = null!; break;
                case "markdown-digest":
                    var bytes = f.Io.Nodes[f.MarkdownPath].Bytes.ToArray(); bytes[0] ^= 1; f.Io.Put(f.MarkdownPath, bytes); break;
                case "terminal-exit": f.State.ExitCode = null; break;
                case "report-run": f.Report.RunId = "sibling"; break;
                case "report-exit": f.Report.ExitCode = 1; break;
                case "commit": f.Report.Commit = new string('b', 40); break;
                case "source-object": f.Report.Source.Start = f.Report.Source.Start with { Fingerprint = new string('2', 64) }; break;
                case "build-stamp-commit": f.Stamp.Commit = new string('b', 40); break;
                case "build-stamp-fingerprint": f.Stamp.Fingerprint = new string('2', 64); break;
            }
            f.SaveReport(); f.SaveStamp();
            ReportWriter.RequiredReceiptRefusal(f.Context, f.Run, f.State).ShouldBe(expected, label + variant);
            if (variant is not ("state-owner" or "state-run" or "state-source"))
                f.Io.Count("open-read", f.JsonPath).ShouldBe(1, label + variant + ":report-reached");
            if (variant.StartsWith("build-stamp", StringComparison.Ordinal))
                f.Io.Count("open-read", f.StampPath).ShouldBe(1, label + variant + ":stamp-reached");
            NoMutation(f, label + variant);
        }
    }

    [Test]
    public void Report_writes_require_verified_readback()
    {
        const string label = "C987.Report_writes_require_verified_readback.";
        var control = new HostCleanupReceiptFixture(seed: false);
        ReportWriter.WriteFiles(control.Context, control.Run, control.Report);
        control.Io.Text(control.MarkdownPath).ShouldBe(ReportWriter.Markdown(control.Context, control.Report), label + "positive-markdown");
        ReceiptJson.Read<ReportModel>(control.Context, control.JsonPath, ReportWriter.Json).RunId.ShouldBe("verified-run", label + "positive-json");
        control.Io.Text(control.MarkdownPath).ShouldContain("outputs: refused/kept bin-owned/", Case.Sensitive, label + "virtual-output-observation");
        control.Io.Count("directory-exists", control.Output).ShouldBeGreaterThan(0, label + "output-observation-entry");
        control.Io.Count("move", control.MarkdownPath).ShouldBe(1);
        control.Io.Count("move", control.JsonPath).ShouldBe(1);
        control.Io.Count("open-read", control.MarkdownPath).ShouldBe(3, label + "positive-readback");
        foreach (var variant in new[] { "markdown-write", "json-write", "markdown-readback", "json-readback" })
        {
            var f = new HostCleanupReceiptFixture(seed: false);
            var path = variant.StartsWith("markdown", StringComparison.Ordinal) ? f.MarkdownPath : f.JsonPath;
            var readback = variant.EndsWith("readback", StringComparison.Ordinal);
            if (readback)
            {
                var opens = 0;
                f.Io.Before = (op, current) =>
                {
                    // Publication verifies the destination, the content record reads it again,
                    // and the third read is the required report readback.
                    if (op == "open-read" && current == path && ++opens == 3)
                        f.Io.Put(path, f.Io.Text(path) + " ");
                };
            }
            else f.Io.Put(path, "foreign-control-file");
            var ex = Should.Throw<IOException>(() => ReportWriter.WriteFiles(f.Context, f.Run, f.Report), label + variant);
            if (readback)
            {
                ex.Message.ShouldBe("refused: required report receipt readback mismatching", label + variant);
                f.Io.Count("move", path).ShouldBe(1, label + variant + ":write-succeeded");
                f.Io.Count("open-read", path).ShouldBe(3, label + variant + ":readback-reached");
            }
            else
            {
                ex.Message.ShouldStartWith("refused: required " + Path.GetFileName(path) + " receipt-write-failed:", Case.Sensitive, label + variant);
                f.Io.Count("file-exists", path).ShouldBeGreaterThan(0, label + variant + ":write-refusal-reached");
                f.Io.Text(path).ShouldBe("foreign-control-file", label + variant + ":retained");
                f.Context.Registry.WasCreated(path).ShouldBeFalse(label + variant + ":no-adoption");
            }
            f.Io.Nodes.ContainsKey(f.StatePath).ShouldBeFalse(label + variant + ":no-terminal-state");
        }
    }

    [Test]
    public void Receipt_read_errors_do_not_write_or_adopt()
    {
        const string label = "C987.Receipt_read_errors_do_not_write_or_adopt.swallowed-read";
        foreach (var error in new Exception[] { new IOException("injected-read"), new UnauthorizedAccessException("injected-denial") })
        {
            var f = new HostCleanupReceiptFixture();
            var store = new RunStateStore(f.Context);
            var control = store.TryRead(f.StatePath, out var controlRefusal);
            control.ShouldNotBeNull(label + ":positive-pair");
            control.RunId.ShouldBe("verified-run", label + ":positive-pair:identity");
            controlRefusal.ShouldBeNull(label + ":positive-pair:refusal");
            f.ClearEvents();
            var original = f.Io.Nodes[f.StatePath].Bytes.ToArray();
            f.Io.Before = (op, path) => { if (op == "open-read" && path == f.StatePath) throw error; };
            store.TryRead(f.StatePath, out var refusal).ShouldBeNull(label);
            refusal.ShouldStartWith("state-receipt-unreadable:", Case.Sensitive, label + ":reason");
            f.Io.Count("open-read", f.StatePath).ShouldBe(1, label + ":read-entered");
            f.Io.Nodes[f.StatePath].Bytes.ShouldBe(original, label + ":retained");
            NoMutation(f, label);
        }
    }

    [Test]
    public void Valid_terminal_receipt_is_accepted()
    {
        const string label = "C987.Valid_terminal_receipt_is_accepted.positive-terminal";
        foreach (var exit in new[] { 0, 1 })
        {
            var f = new HostCleanupReceiptFixture(exit, seed: false);
            var store = new RunStateStore(f.Context);
            store.Write(f.StatePath, f.State).ShouldBeNull(label + ":state-write");
            // Exercise the replaceable state and history append, with the same context.
            f.State.Nonce++;
            store.Write(f.StatePath, f.State).ShouldBeNull(label + ":state-replace");
            f.Io.Count("append", Path.Combine(f.Run, "state.history.jsonl")).ShouldBe(1, label + ":history");
            f.Io.Text(Path.Combine(f.Run, "state.history.jsonl")).Split('\n', StringSplitOptions.RemoveEmptyEntries).Length.ShouldBe(2, label + ":history-lines");
            ReportWriter.WriteFiles(f.Context, f.Run, f.Report);
            // Reads survive a fresh registry but never acquire generic output custody.
            var fresh = f.Files.FreshContext();
            var state = new RunStateStore(fresh).TryRead(f.StatePath, out var reason);
            reason.ShouldBeNull(label + ":state-read");
            state.ShouldNotBeNull(label + ":state");
            state.ExitCode.ShouldBe(exit, label + ":exit");
            state.Nonce.ShouldBe(1, label + ":latest-state");
            f.ClearEvents();
            ReportWriter.RequiredReceiptRefusal(fresh, f.Run, state).ShouldBeNull(label + ":" + exit);
            f.Io.Count("open-read", f.JsonPath).ShouldBe(1, label + ":json");
            f.Io.Count("open-read", f.MarkdownPath).ShouldBe(1, label + ":markdown");
            f.Io.Count("open-read", f.StampPath).ShouldBe(exit == 0 ? 1 : 0, label + ":stamp");
            foreach (var path in new[] { f.StatePath, f.JsonPath, f.MarkdownPath, f.StampPath })
                fresh.Registry.WasCreated(path).ShouldBeFalse(label + ":no-generic-adoption:" + path);
            f.Io.Count("write").ShouldBe(0, label + ":read-only");
            f.Io.Count("move").ShouldBe(0, label + ":read-only");
        }
    }

    private static void NoMutation(HostCleanupReceiptFixture f, string label)
    {
        foreach (var op in new[] { "write", "append", "move", "unlink", "rmdir", "mkdir", "mkdir-exclusive" })
            f.Io.Count(op).ShouldBe(0, label + ":no-" + op);
        foreach (var path in new[] { f.StatePath, f.JsonPath, f.MarkdownPath, f.StampPath })
            f.Context.Registry.WasCreated(path).ShouldBeFalse(label + ":no-adoption:" + path);
    }

    public sealed class ReceiptDepthNode { public ReceiptDepthNode? Next { get; set; } }

    private sealed class BlockedReceiptStream : MemoryStream
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Entered.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
            return 0;
        }
    }
}
