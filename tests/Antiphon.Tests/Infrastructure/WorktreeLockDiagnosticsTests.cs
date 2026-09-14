using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Infrastructure.Git;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Infrastructure;

[Category("Unit")]
public sealed class WorktreeLockDiagnosticsTests
{
    [Test]
    public async Task C443_UnsupportedPlatform()
    {
        var io = new RecordingIO { IsSupported = false }; var result = await RunAsync(io);
        result.Status.ShouldBe(WorktreeLockStatus.Unavailable); result.Reason.ShouldBe("UnsupportedPlatform"); io.Starts.ShouldBeEmpty();
    }
    [Test]
    public async Task C443_RejectUntrustedToolPath()
    {
        foreach (var path in new[] { "", "relative.exe", Path.GetFullPath("missing.exe") })
        {
            var io = new RecordingIO { ToolExists = false };
            (await RunAsync(io, executable: path)).Status.ShouldBe(WorktreeLockStatus.Unavailable); io.Starts.ShouldBeEmpty();
        }
    }
    [Test]
    public async Task C443_InsufficientPrivileges()
    {
        var io = new RecordingIO { IsElevated = false };
        (await RunAsync(io)).Reason.ShouldBe("InsufficientPrivileges"); io.Starts.ShouldBeEmpty();
    }
    [Test]
    public async Task C443_ReadOnlyArgumentVector()
    {
        var io = new RecordingIO(); await RunAsync(io);
        io.Starts[0].ArgumentList.ShouldBe(["-nobanner", "-v", io.Root]);
        io.Starts[0].UseShellExecute.ShouldBeFalse();
    }
    [Test]
    public async Task C443_DiagnosticsNeverUseShell()
    { var io = new RecordingIO(); await RunAsync(io); io.Starts.ShouldAllBe(s => !s.UseShellExecute); }
    [Test]
    public async Task C443_DiagnosticLocationsOutsideTarget()
    { var io = new RecordingIO(); await RunAsync(io); io.Starts.ShouldAllBe(s => !WorktreeNativeIO.Within(s.WorkingDirectory, io.Root)); }
    [Test]
    public async Task C443_TargetQueryPrecedesControls()
    { var io = new RecordingIO(); await RunAsync(io); io.Trace.ShouldBe(["TargetQuery", "CreateControls", "ControlQuery", "DisposeControls"]); }
    [Test]
    public async Task C443_MissingFileControl()
    { var io = new RecordingIO { FileControl = false }; (await RunAsync(io)).Reason.ShouldBe("PositiveControlFailed"); }
    [Test]
    public async Task C443_MissingDirectoryControl()
    { var io = new RecordingIO { DirectoryControl = false }; (await RunAsync(io)).Reason.ShouldBe("PositiveControlFailed"); }
    [Test]
    public async Task C443_ControlPidMustMatch()
    { var io = new RecordingIO { ControlPid = 999 }; (await RunAsync(io)).Reason.ShouldBe("PositiveControlFailed"); }
    [Test]
    public async Task C443_ControlResourcesDisposed()
    {
        var io = new RecordingIO { FailControlQuery = true };
        await RunAsync(io); io.Disposals.ShouldBe(1);
        using var source = new CancellationTokenSource(); io = new RecordingIO();
        io.BeforeQuery = index => { if (index == 2) source.Cancel(); return Task.CompletedTask; };
        await Should.ThrowAsync<OperationCanceledException>(() => RunAsync(io, ct: source.Token)); io.Disposals.ShouldBe(1);
    }
    [Test]
    [Arguments(0)] [Arguments(1)] [Arguments(2)]
    public async Task C443_PropagateOuterCancellation(int boundary)
    {
        using var source = new CancellationTokenSource(); var io = new RecordingIO();
        if (boundary == 0) source.Cancel();
        io.BeforeQuery = index => { if (index == boundary) source.Cancel(); return Task.CompletedTask; };
        await Should.ThrowAsync<OperationCanceledException>(() => RunAsync(io, ct: source.Token));
        io.Starts.Count.ShouldBe(boundary);
    }
    [Test]
    public async Task C443_SharedDiagnosticBudget()
    {
        var clock = new FakeTimeProvider(); var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var io = new RecordingIO();
        io.BeforeQuery = index => { if (index == 1) clock.Advance(TimeSpan.FromSeconds(4)); else entered.TrySetResult(); return Task.CompletedTask; };
        io.BlockControl = true;
        var capture = RunAsync(io, clock: clock); await entered.Task;
        clock.Advance(TimeSpan.FromSeconds(1));
        (await capture).Status.ShouldBe(WorktreeLockStatus.TimedOut); io.Disposals.ShouldBe(1);
    }
    [Test]
    [Arguments(262143)] [Arguments(262144)] [Arguments(262145)]
    public void C443_CombinedOutputBound(int count)
    {
        var bytes = new WorktreeDiagnosticBytes(); var first = bytes.Take(131072); var second = bytes.Take(count - 131072);
        (first + second).ShouldBe(Math.Min(count, 262144)); bytes.Truncated.ShouldBe(count > 262144);
    }
    [Test]
    [Arguments(31)] [Arguments(32)] [Arguments(33)]
    public async Task C443_OwnerCountBound(int count)
    {
        var io = new RecordingIO(); io.TargetCsv = RecordingIO.Header + string.Concat(Enumerable.Range(1, count).Select(i => RecordingIO.Row("owner" + i, i, io.Root)));
        var result = await RunAsync(io); result.Owners.Count.ShouldBe(Math.Min(count, 32)); result.OmittedOwners.ShouldBe(Math.Max(0, count - 32));
    }
    [Test]
    public async Task C443_StructuredEvidenceBound()
    {
        var io = new RecordingIO(); io.TargetCsv = RecordingIO.Header + string.Concat(Enumerable.Range(1, 32).Select(i =>
            RecordingIO.Row(new string('\u00e9', 80), i, Path.Combine(io.Root, new string('\u00e9', 500)))));
        var result = await RunAsync(io);
        Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(result)).ShouldBeLessThanOrEqualTo(32768);
        result.OmittedOwners.ShouldBeGreaterThan(0);
    }
    [Test]
    public async Task C443_ValidateCsvSchema()
    {
        var io = new RecordingIO(); io.TargetCsv = RecordingIO.Header + RecordingIO.Row("quoted,\"name", 45, Path.Combine(io.Root, "file,\"name"));
        var owner = (await RunAsync(io)).Owners.Single(); owner.Name.ShouldBe("quoted,\"name"); owner.RelativePath.ShouldBe("file,\"name");
    }
    [Test]
    public async Task C443_ExactRootAndDescendants()
    {
        var io = new RecordingIO(); io.TargetCsv = RecordingIO.Header + RecordingIO.Row("root", 1, io.Root.ToUpperInvariant())
            + RecordingIO.Row("child", 2, Path.Combine(io.Root, "file")) + RecordingIO.Row("sibling", 3, io.Root + "-sibling");
        var result = await RunAsync(io); result.Owners.Count.ShouldBe(2);
        result.Owners[0].RelativePath.ShouldBe("."); result.Owners[1].RelativePath.ShouldBe("file");
    }
    [Test]
    public async Task C443_UnresolvedAliasIsPartial()
    {
        var io = new RecordingIO(); io.TargetCsv = RecordingIO.Header + RecordingIO.Row("owner", 1, @"\Device\HarddiskVolume1\tree");
        (await RunAsync(io)).Status.ShouldBe(WorktreeLockStatus.Partial);
    }
    [Test]
    public async Task C443_EmptyRequiresCompleteCoverage()
    {
        var clean = new RecordingIO(); (await RunAsync(clean)).Status.ShouldBe(WorktreeLockStatus.NoOwnersObserved);
        foreach (var io in new[] { new RecordingIO { FileControl = false }, new RecordingIO { Truncated = true },
                     new RecordingIO { TargetCsv = "unknown\n" }, new RecordingIO { IsElevated = false } })
            (await RunAsync(io)).Status.ShouldNotBe(WorktreeLockStatus.NoOwnersObserved);
    }
    [Test]
    public async Task C443_PartialRetainsOwners()
    {
        var io = new RecordingIO { FileControl = false }; io.TargetCsv = RecordingIO.Header + RecordingIO.Row("owner", 12, io.Root);
        var result = await RunAsync(io); result.Status.ShouldBe(WorktreeLockStatus.Partial); result.Owners.Single().ProcessId.ShouldBe(12);
    }
    [Test]
    public async Task C443_ReusedPidDoesNotEnrichOldOwner()
    {
        var io = new RecordingIO(); io.TargetCsv = RecordingIO.Header + RecordingIO.Row("owner", 12, io.Root);
        var reads = 0; io.StartTime = _ => DateTime.UtcNow.AddDays(-1).Ticks + ++reads;
        var result = await RunAsync(io); result.Owners.Single().ProcessStartTicks.ShouldBeNull(); result.Status.ShouldBe(WorktreeLockStatus.Partial);
    }
    [Test]
    public async Task C443_UnknownAncestryStaysUnknown()
    {
        var io = new RecordingIO(); io.TargetCsv = RecordingIO.Header + RecordingIO.Row("dotnet", 12, io.Root);
        var owner = (await RunAsync(io)).Owners.Single(); owner.Attribution.ShouldBe("unknown"); owner.ParentProcessId.ShouldBeNull();
    }
    [Test]
    public async Task C443_PathGone()
    { var io = new RecordingIO { RootExists = false }; (await RunAsync(io)).Status.ShouldBe(WorktreeLockStatus.PathGone); io.Starts.ShouldBeEmpty(); }
    [Test]
    public async Task C443_SanitizedStructuredEvidence()
    {
        var io = new RecordingIO(); io.TargetCsv = RecordingIO.Header + RecordingIO.Row("owner", 12, io.Root)
            + RecordingIO.Row("UNRELATED_SECRET_MARKER", 99, Path.GetFullPath("unrelated"));
        JsonSerializer.Serialize(await RunAsync(io)).ShouldNotContain("UNRELATED_SECRET_MARKER");
    }
    [Test]
    public async Task C443_UnknownCsvSchema()
    {
        foreach (var csv in new[] { "unexpected,header\n", RecordingIO.Header + "bad,pid,File,42,path\n", RecordingIO.Header + "\"unterminated" })
        { var io = new RecordingIO { TargetCsv = csv }; (await RunAsync(io)).Status.ShouldBe(WorktreeLockStatus.Failed); }
    }
    [Test]
    public async Task C443_ControlDirectoryOutsideWorktrees()
    {
        var io = new RecordingIO(); await RunAsync(io);
        WorktreeNativeIO.Within(io.ControlRoot, io.Root).ShouldBeFalse();
        WorktreeNativeIO.Within(io.ControlRoot, Path.GetFullPath("managed-trees")).ShouldBeFalse();
    }
    [Test]
    public async Task C443_EvidencePathsOutsideTarget()
    {
        var io = new RecordingIO { Scratch = Path.GetFullPath("diagnostic-tree") };
        (await RunAsync(io)).Reason.ShouldBe("ExternalControlRootRequired"); io.Starts.ShouldBeEmpty();
    }

    internal static Task<WorktreeLockSnapshot> RunAsync(RecordingIO io, string? executable = null, TimeProvider? clock = null, CancellationToken ct = default) =>
        new WindowsWorktreeLockDiagnostics(io, Options.Create(new WorktreeLockSettings { HandleExecutablePath = executable ?? Path.GetFullPath("handle.exe") }),
            Options.Create(new GitSettings { WorktreeBasePath = Path.GetFullPath("managed-trees") }), clock ?? TimeProvider.System).CaptureAsync(io.Root, ct);

    internal sealed class RecordingIO() : WorktreeDiagnosticIO(new WorktreeNativeIO())
    {
        internal const string Header = "Process,PID,Type,Handle,Name\n";
        public string Root { get; } = Path.GetFullPath("diagnostic-tree");
        public string Scratch = Path.GetFullPath("diagnostic-scratch");
        public string ControlRoot => Path.Combine(Scratch, "control");
        public bool IsSupported = true, IsElevated = true, ToolExists = true, RootExists = true,
            FileControl = true, DirectoryControl = true, FailControlQuery, BlockControl, Truncated;
        public int ControlPid = 123, Disposals;
        public string TargetCsv = Header;
        public List<ProcessStartInfo> Starts { get; } = [];
        public List<string> Trace { get; } = [];
        public Func<int, Task>? BeforeQuery;
        public Func<int, long?>? StartTime;
        private readonly long _start = DateTime.UtcNow.AddDays(-1).Ticks;
        public override bool Supported => IsSupported;
        public override bool Elevated => IsElevated;
        public override string ScratchRoot => Scratch;
        public override bool Exists(string path) => RootExists;
        public override bool TrustedExecutable(string path) => ToolExists;
        public override (string Version, string Identity) ToolIdentity(string path) => ("synthetic-fixture", "unqualified");
        public override long? ProcessStart(int pid) => StartTime?.Invoke(pid) ?? _start;
        public override WorktreeDiagnosticControls CreateControls(string outsideRoot)
        {
            Trace.Add("CreateControls");
            return new(ControlRoot, Path.Combine(ControlRoot, "held.bin"), 123, () => { Disposals++; Trace.Add("DisposeControls"); });
        }
        public override async Task<WorktreeDiagnosticOutput> RunAsync(ProcessStartInfo start, WorktreeDiagnosticBytes bytes, CancellationToken ct)
        {
            Starts.Add(start); Trace.Add(Starts.Count == 1 ? "TargetQuery" : "ControlQuery");
            if (BeforeQuery is not null) await BeforeQuery(Starts.Count);
            ct.ThrowIfCancellationRequested();
            if (Starts.Count == 1) return new(0, TargetCsv, Truncated);
            if (FailControlQuery) throw new IOException("control query failed");
            if (BlockControl) await Task.Delay(Timeout.Infinite, ct);
            return new(0, Header + (FileControl ? Row("test", ControlPid, Path.Combine(ControlRoot, "held.bin")) : "")
                + (DirectoryControl ? Row("test", ControlPid, ControlRoot) : ""), false);
        }
        public static string Row(string name, int pid, string path) => $"\"{name.Replace("\"", "\"\"")}\",{pid},File,123,\"{path.Replace("\"", "\"\"")}\"\n";
    }
}
