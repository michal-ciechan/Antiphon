using Antiphon.Checkpoints;
using Shouldly;
using TUnit.Core;
using CheckpointProgram = Antiphon.Checkpoints.Program;

namespace Antiphon.Tests.Infrastructure;

[Category("Unit")]
public sealed class C1307CancellationTests
{
    [Test]
    public async Task Canceled_wait_returns_130()
    {
        var f = new HostCleanupToolFixture(sealAuthority: true);
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        var output = new StringWriter();
        int? exit = null;
        Exception? failure = null;
        try { exit = await Command(f, "wait", cancel.Token, output); }
        catch (Exception ex) { failure = ex; }
        exit.ShouldBe(130, "caller cancellation must return 130, not throw " + failure?.GetType().Name);
        output.ToString().ShouldContain("canceled: caller-requested");
        f.Io.Count("unlink").ShouldBe(0);
    }

    [Test]
    public async Task Entered_receipt_cancellation_returns_130()
    {
        foreach (var verb in new[] { "status", "report" })
        {
            var f = new HostCleanupToolFixture(sealAuthority: true);
            using var cancel = new CancellationTokenSource();
            var stream = new EnteredRead();
            var target = verb == "status" ? f.Receipt.StatePath : f.Receipt.MarkdownPath;
            f.Io.Before = (op, path) =>
            {
                // Status swallows a failed state read; report's final read throws
                // its wrapped IOException after terminal receipt validation.
                if (op == "stream" && path == target
                    && (verb == "status" || f.Io.Count("open-read", target) == 2))
                    f.Io.StreamFactory = () => stream;
            };
            var output = new StringWriter();
            var command = Task.Run(() => Command(f, verb, cancel.Token, output));
            try
            {
                await stream.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
                cancel.Cancel();
                (await command.WaitAsync(TimeSpan.FromSeconds(5))).ShouldBe(130, verb);
                stream.Token.IsCancellationRequested.ShouldBeTrue(verb);
                output.ToString().ShouldContain("canceled: caller-requested");
                f.Io.Count("unlink").ShouldBe(0);
            }
            finally
            {
                cancel.Cancel();
                stream.Release.TrySetResult();
                await command;
            }
        }

        // Cancellation after the command's final I/O still wins over its returned 0.
        var returned = new HostCleanupToolFixture(sealAuthority: true);
        using var late = new CancellationTokenSource();
        var writer = new CancelOnReportWriter(late);
        (await Command(returned, "report", late.Token, writer)).ShouldBe(130);
        writer.ReportWritten.ShouldBeTrue();
        writer.ToString().ShouldContain("canceled: caller-requested");
    }

    [Test]
    public async Task Partial_cleanup_prints_residue_before_canceled_exit()
    {
        var f = new HostCleanupToolFixture(sealAuthority: true);
        using var cancel = new CancellationTokenSource();
        var stream = new EnteredRead();
        var reportAccessAfterCancel = 0;
        f.Io.Before = (op, path) =>
        {
            if (cancel.IsCancellationRequested && path == f.Receipt.MarkdownPath)
                reportAccessAfterCancel++;
            if (op == "stream" && f.Context.IsStrictlyInside(f.Tool, path)
                && f.Io.Events.Count(e => e.Operation == "unlink" && f.Context.IsStrictlyInside(f.Tool, e.Path)) == 1)
                f.Io.StreamFactory = () => stream;
        };
        var output = new StringWriter();
        var command = Task.Run(() => Command(f, "wait", cancel.Token, output));
        try
        {
            await stream.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var removed = f.Io.Events.Where(e => e.Operation == "unlink"
                && f.Context.IsStrictlyInside(f.Tool, e.Path)).ShouldHaveSingleItem();
            f.Io.FileExists(removed.Path).ShouldBeFalse();
            cancel.Cancel();
            (await command.WaitAsync(TimeSpan.FromSeconds(5))).ShouldBe(130);
            var text = output.ToString();
            text.ShouldContain("TOOL CLEANUP Refused");
            text.ShouldContain("tool-removal-incomplete");
            text.ShouldContain("manual inspection");
            text.IndexOf("tool-removal-incomplete", StringComparison.Ordinal)
                .ShouldBeLessThan(text.IndexOf("canceled: caller-requested", StringComparison.Ordinal));
            text.ShouldNotContain("--- checkpoint report ---");
            reportAccessAfterCancel.ShouldBe(0, "Wait must honor cancellation before accessing the earlier green report");
            f.Io.FileExists(Path.Combine(f.Run, "tool-removal-started.json")).ShouldBeTrue();
            f.Io.Text(f.Payload).ShouldBe("keep");
        }
        finally
        {
            cancel.Cancel();
            stream.Release.TrySetResult();
            await command;
        }
    }

    [Test]
    public async Task Deadline_without_caller_cancel_stays_invalid()
    {
        var f = new HostCleanupToolFixture(sealAuthority: true);
        using var caller = new CancellationTokenSource();
        var stream = new EnteredRead();
        f.Io.Before = (op, path) =>
        {
            if (op == "stream" && path == f.Receipt.StatePath) f.Io.StreamFactory = () => stream;
        };
        var output = new StringWriter();
        var command = Task.Run(() => Command(f, "status", caller.Token, output));
        try
        {
            await stream.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            f.Files.Clock.Advance(CheckpointFileReader.StallDeadline);
            (await command.WaitAsync(TimeSpan.FromSeconds(5))).ShouldBe(2);
            caller.IsCancellationRequested.ShouldBeFalse();
            output.ToString().ShouldContain("state-receipt-unreadable");
            output.ToString().ShouldNotContain("canceled: caller-requested");
            f.Io.Count("unlink").ShouldBe(0);
        }
        finally
        {
            stream.Release.TrySetResult();
            await command;
        }

        var internalCancel = new HostCleanupToolFixture();
        internalCancel.Io.Before = (op, path) =>
        {
            if (op == "directory-exists" && path == internalCancel.Run)
                throw new OperationCanceledException("internal cancellation without caller request");
        };
        await Should.ThrowAsync<OperationCanceledException>(() =>
            Command(internalCancel, "status", CancellationToken.None, new StringWriter()));
    }

    [Test]
    public async Task Canceling_wait_leaves_executor_and_receipts_unchanged()
    {
        var f = new HostCleanupToolFixture(sealAuthority: true);
        f.Io.Processes[f.Dead.Pid] = new(f.Dead.StartUtcTicks, 'R', false);
        var before = f.Io.Nodes.ToDictionary(p => p.Key, p => p.Value.Bytes.ToArray());
        using var cancel = new CancellationTokenSource();
        var control = new CountingControl();
        var output = new StringWriter();
        var command = Task.Run(() => Command(f, "wait", cancel.Token, output, control));
        var timer = f.Files.Clock.WaitForTimerAsync(
            e => e.Action == "create" && e.DueTime == TimeSpan.FromMilliseconds(200), ct: cancel.Token);
        try
        {
            await timer.WaitAsync(TimeSpan.FromSeconds(5));
            cancel.Cancel();
            (await command.WaitAsync(TimeSpan.FromSeconds(5))).ShouldBe(130);
            control.Calls.ShouldBe(0);
            new ProcessIdentityProbe(f.Context).Observe(f.Dead).Verdict.ShouldBe(ProcessVerdict.AliveSame);
            f.Io.Nodes.Keys.Order().ShouldBe(before.Keys.Order());
            foreach (var (path, bytes) in before) f.Io.Nodes[path].Bytes.ShouldBe(bytes, path);
            f.Io.Count("unlink").ShouldBe(0);
        }
        finally
        {
            cancel.Cancel();
            try { await timer; } catch (OperationCanceledException) { }
            await command;
        }
    }

    private static Task<int> Command(HostCleanupToolFixture f, string verb, CancellationToken token,
        TextWriter output, IProcessControl? control = null) =>
        CheckpointProgram.RunAsync([verb, "--run", f.Run, "--repo-root", f.Files.Parent, "--results-root", f.Run],
            new CheckpointApp.Runtime
            {
                CleanupContext = f.Context, CancellationToken = token, RepoRoot = f.Files.Parent,
                EnvironmentLookup = _ => null, Output = output, ProcessControl = control,
            });

    private sealed class EnteredRead : MemoryStream
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public CancellationToken Token { get; private set; }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Token = cancellationToken;
            Entered.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
            return 0;
        }
    }

    private sealed class CancelOnReportWriter(CancellationTokenSource cancel) : StringWriter
    {
        public bool ReportWritten { get; private set; }
        public override void Write(string? value)
        {
            base.Write(value);
            if (value?.Contains("--- checkpoint report ---", StringComparison.Ordinal) == true)
            {
                ReportWritten = true;
                cancel.Cancel();
            }
        }
    }

    private sealed class CountingControl : IProcessControl
    {
        public int Calls { get; private set; }
        public bool StopAndWait(ProcessIdentity identity, TimeSpan timeout) { Calls++; return false; }
    }
}
