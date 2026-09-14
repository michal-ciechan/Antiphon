using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Infrastructure.Git;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Infrastructure;

[Category("Unit")]
public sealed class WorktreeDeleteAccessProbeTests
{
    [Test] public async Task C443_ProbeRequestsDeleteAccess() => (await OpenAsync()).Access.ShouldBe(0x10000u);
    [Test] public async Task C443_ProbeSharesReadWriteDelete() => (await OpenAsync()).Share.ShouldBe(7u);
    [Test] public async Task C443_ProbeNeverCreates() => (await OpenAsync()).Disposition.ShouldBe(3u);
    [Test] public async Task C443_ProbeOpensDirectories() => ((await OpenAsync()).Flags & 0x02000000u).ShouldBe(0x02000000u);
    [Test] public async Task C443_ProbeOpensReparsePointItself() => ((await OpenAsync()).Flags & 0x00200000u).ShouldBe(0x00200000u);
    [Test] public async Task C443_ProbeCannotDeleteOrAlter() => (await OpenAsync()).Flags.ShouldBe(0x02200000u);
    [Test] public async Task C443_ProbeHandlesNotInherited() => (await OpenAsync()).Inherit.ShouldBeFalse();

    [Test]
    public async Task C443_ProbeClosesEachHandle()
    {
        var io = new RecordingIO(); io.Children.Add(io.Child("file"));
        io.BeforeOpen = _ => io.Outstanding.ShouldBe(0);
        await RunAsync(io); io.Outstanding.ShouldBe(0); io.Disposals.ShouldBe(2);
    }
    [Test]
    public async Task C443_ProbeCapturesLastErrorImmediately()
    {
        var io = new RecordingIO { Error = 32 };
        var row = (await RunAsync(io)).Observations.Single();
        row.NativeErrorCode.ShouldBe(32); row.Operation.ShouldBe("DeleteAccessOpen");
        row.RelativePath.ShouldBe("."); row.IdentityVerified.ShouldBeTrue();
    }
    [Test]
    public async Task C443_ProbeRootFirst()
    {
        var io = new RecordingIO(); io.Children.Add(io.Child("file"));
        await RunAsync(io, [new("holder", 42, "file")]);
        io.Opens[0].Path.ShouldBe(io.Root);
    }
    [Test]
    [Arguments(63)] [Arguments(64)] [Arguments(65)]
    public async Task C443_ProbeCandidateCap(int count)
    {
        var io = new RecordingIO(); io.Children.AddRange(Enumerable.Range(0, count).Select(i => io.Child("file" + i)));
        var result = await RunAsync(io);
        io.Opens.Count.ShouldBe(Math.Min(count, 64) + 1);
        result.Candidates.ShouldBe(Math.Min(count, 64));
        if (count >= 64) result.Status.ShouldBe(WorktreeLockStatus.Partial);
    }
    [Test]
    public async Task C443_ProbeEnumerationCap()
    {
        var io = new RecordingIO(); io.Children.AddRange(Enumerable.Range(0, 200).Select(i => Path.Combine(io.Admin, "entry" + i)));
        var result = await RunAsync(io);
        io.NextEntries.ShouldBe(64); io.Opens.Count.ShouldBe(1); result.Status.ShouldBe(WorktreeLockStatus.Partial);
    }
    [Test]
    [Arguments(1999)] [Arguments(2000)] [Arguments(2001)]
    public async Task C443_ProbeTwoSecondDeadline(int milliseconds)
    {
        var clock = new FakeTimeProvider(); var io = new RecordingIO(); io.Children.Add(io.Child("file"));
        io.BeforeOpen = _ => { if (io.Opens.Count == 0) clock.Advance(TimeSpan.FromMilliseconds(milliseconds)); };
        await RunAsync(io, clock: clock);
        io.Opens.Count.ShouldBe(milliseconds < 2000 ? 2 : 1);
    }
    [Test]
    public async Task C443_ProbeUsesRemainingBudget()
    {
        using var source = new CancellationTokenSource(); var io = new RecordingIO(); io.Children.Add(io.Child("file"));
        io.BeforeOpen = _ => source.Cancel();
        await Should.ThrowAsync<OperationCanceledException>(() => RunAsync(io, ct: source.Token));
        io.Opens.Count.ShouldBe(1); io.Outstanding.ShouldBe(0);
    }
    [Test]
    public async Task C443_ProbeDoesNotReadContents()
    {
        var io = new RecordingIO(); io.Children.Add(io.Child("file"));
        await RunAsync(io);
        io.Trace.ShouldAllBe(t => t == "identity" || t == "attributes" || t == "entries" || t == "open" || t == "exists");
    }
    [Test]
    public async Task C443_ProbeDoesNotTraverseReparse()
    {
        var io = new RecordingIO(); var junction = io.Child("junction"); io.Children.Add(junction);
        io.AttributeOverrides[junction] = FileAttributes.ReparsePoint | FileAttributes.Directory;
        await RunAsync(io);
        io.Opens.ShouldNotContain(o => o.Path == junction); io.Enumerated.ShouldNotContain(junction);
    }
    [Test]
    public async Task C443_ProbeRootIdentityRequired()
    {
        var io = new RecordingIO { IdentityRead = _ => null };
        (await RunAsync(io)).HasSharingConflict.ShouldBeFalse(); io.Opens.ShouldBeEmpty();
    }
    [Test]
    public async Task C443_ProbeValidatesBeforeOpen()
    {
        var io = new RecordingIO(); io.Children.Add(io.Child("replaced"));
        io.IdentityRead = p => p == io.Root ? "root" : null;
        await RunAsync(io); io.Opens.Count.ShouldBe(1);
    }
    [Test]
    public async Task C443_ProbeValidatesAfterFailure()
    {
        var io = new RecordingIO { Error = 32 };
        io.IdentityRead = _ => io.Opens.Count == 0 ? "before" : "after";
        var result = await RunAsync(io);
        result.HasSharingConflict.ShouldBeFalse(); result.Observations.Single().IdentityVerified.ShouldBeFalse();
    }
    [Test]
    public async Task C443_ProbeValidatesFinalHandlePath()
    {
        var io = new RecordingIO { FinalPathOverride = Path.GetFullPath("outside") };
        var result = await RunAsync(io);
        result.Status.ShouldBe(WorktreeLockStatus.Partial); result.Observations.Single().IdentityVerified.ShouldBeFalse();
    }
    [Test]
    public async Task C443_ProbeSkipsGitAdmin()
    {
        var io = new RecordingIO(); io.Children.AddRange([io.Child(".git"), io.Admin, io.Common]);
        await RunAsync(io); io.Opens.Select(o => o.Path).ShouldBe([io.Root]);
    }
    [Test]
    public async Task C443_ProbeValidatesOwnerCandidates()
    {
        var io = new RecordingIO();
        await RunAsync(io, [new("owner", 12, "../outside"), new("owner", 12, "inside")]);
        io.Opens.Select(o => o.Path).ShouldBe([io.Root, io.Child("inside")]);
    }
    [Test]
    public async Task C443_PartialProbeKeepsPositive()
    {
        var io = new RecordingIO { Error = 32 }; io.Children.AddRange(Enumerable.Range(0, 70).Select(i => io.Child("f" + i)));
        var result = await RunAsync(io);
        result.Status.ShouldBe(WorktreeLockStatus.Partial); result.HasSharingConflict.ShouldBeTrue();
    }
    [Test]
    public async Task C443_NativeUnsupportedPlatform()
    {
        var io = new RecordingIO { IsSupported = false };
        var result = await RunAsync(io);
        result.Status.ShouldBe(WorktreeLockStatus.Unavailable); result.Reason.ShouldBe("UnsupportedPlatform"); io.Opens.ShouldBeEmpty();
    }
    [Test]
    public async Task C443_NativeLimitationsArePartial()
    {
        var io = new RecordingIO { DeniedEnumeration = true };
        var result = await RunAsync(io);
        result.Status.ShouldBe(WorktreeLockStatus.Partial); result.Reason.ShouldBe("ProbeAccessLimited");
    }
    [Test]
    public async Task C443_NativeOuterCancellation()
    {
        using var source = new CancellationTokenSource(); source.Cancel();
        var io = new RecordingIO();
        var error = await Should.ThrowAsync<OperationCanceledException>(() => RunAsync(io, ct: source.Token));
        error.CancellationToken.ShouldBe(source.Token); io.Opens.ShouldBeEmpty();
    }

    private static async Task<OpenCall> OpenAsync()
    { var io = new RecordingIO(); await RunAsync(io); return io.Opens.Single(); }
    internal static Task<WorktreeNativeSnapshot> RunAsync(RecordingIO io, IReadOnlyList<WorktreeLockOwner>? owners = null,
        TimeProvider? clock = null, CancellationToken ct = default) =>
        new WindowsWorktreeDeleteAccessProbe(io, clock ?? TimeProvider.System).ObserveAsync(new(io.Root, io.Common, io.Admin), owners ?? [], ct);
    internal sealed record OpenCall(string Path, uint Access, uint Share, uint Disposition, uint Flags, bool Inherit);
    internal sealed class RecordingIO : WorktreeNativeIO
    {
        public string Root { get; } = Path.GetFullPath("fixture-tree");
        public string Common => Path.GetFullPath("fixture-common");
        public string Admin => Path.GetFullPath("fixture-admin");
        public string Child(string path) => Path.Combine(Root, path);
        public bool IsSupported = true;
        public int? Error;
        public bool DeniedEnumeration;
        public int Outstanding, Disposals, NextEntries;
        public string? FinalPathOverride;
        public Func<string, string?>? IdentityRead;
        public Action<string>? BeforeOpen;
        public List<string> Children { get; } = [];
        public List<string> Trace { get; } = [];
        public List<string> Enumerated { get; } = [];
        public List<OpenCall> Opens { get; } = [];
        public Dictionary<string, FileAttributes> AttributeOverrides { get; } = [];
        public override bool Supported => IsSupported;
        public override bool Exists(string path) { Trace.Add("exists"); return true; }
        public override string? Identity(string path) { Trace.Add("identity"); return IdentityRead is null ? path : IdentityRead(path); }
        public override FileAttributes Attributes(string path) { Trace.Add("attributes"); return AttributeOverrides.GetValueOrDefault(path, path == Root ? FileAttributes.Directory : FileAttributes.Normal); }
        public override IEnumerator<string> Entries(string path)
        {
            Trace.Add("entries"); Enumerated.Add(path);
            if (DeniedEnumeration) throw new UnauthorizedAccessException();
            return Enumerate().GetEnumerator();
            IEnumerable<string> Enumerate() { foreach (var child in path == Root ? Children : []) { NextEntries++; yield return child; } }
        }
        public override WorktreeNativeHandle Open(string path, uint desiredAccess, uint shareMode, uint disposition, uint flags, bool inherit)
        {
            BeforeOpen?.Invoke(path); Trace.Add("open"); Opens.Add(new(path, desiredAccess, shareMode, disposition, flags, inherit));
            if (Error is not null) return new(false, Error, null, null, false, null);
            Outstanding++;
            return new(true, null, FinalPathOverride ?? path, path, false, new Releaser(() => { Outstanding--; Disposals++; }));
        }
    }
    internal sealed class Releaser(Action release) : IDisposable { public void Dispose() => release(); }
}
