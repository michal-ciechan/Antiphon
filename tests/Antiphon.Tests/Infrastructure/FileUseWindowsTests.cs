using Antiphon.HostCleanup.Diagnostics;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Infrastructure;

[Category("Unit")]
public sealed class FileUseWindowsTests
{
    [Test]
    public void C1539_rm_contract()
    {
        var io = new Source();
        var result = Observe(io);
        result.Owners.ShouldContain(o => o.Pid == 41, "Restart Manager must retain the observed holder");
        result.Owners.Single().Pid.ShouldBe(41);
        result.Owners.Single().MatchKind.ShouldBe("RestartManager");
        result.Owners.Single().NativeStart.ShouldBe(Source.StartTime.ToString(System.Globalization.CultureInfo.InvariantCulture));
        io.Trace.ShouldBe(["start", "register:held.dll", "list:0", "list:1", "end"]);
        // A resource disappearing during growth must not skip session cleanup.
        io = new() { Lists = new([new(234, 1, []), new(234, 2, []), new(0, 2, [new(41, Source.StartTime, "dotnet")])]) };
        Observe(io).Owners.Single().Pid.ShouldBe(41); io.Trace.Last().ShouldBe("end");
        io = new() { Lists = new(Enumerable.Repeat(new WindowsFileUseList(234, 1, []), 5)) };
        Observe(io).Coverage.ShouldBe(FileUseCoverage.Partial); io.Trace.Count(x => x.StartsWith("list:")).ShouldBe(4); io.Trace.Last().ShouldBe("end");
        foreach (var error in new[] { 5, 353 })
        {
            io = new() { StartError = error };
            result = Observe(io); result.Coverage.ShouldNotBe(FileUseCoverage.Complete); result.Status.ShouldNotBe(FileUseStatus.NoOwnersObserved);
            io.Trace.ShouldBe(["start"]);
        }
        io = new() { RegisterError = 5 };
        Observe(io).Reasons.ShouldContain(FileUseReason.AccessDenied); io.Trace.Last().ShouldBe("end");
        io = new() { ThrowList = true };
        Observe(io).Coverage.ShouldNotBe(FileUseCoverage.Complete); io.Trace.Last().ShouldBe("end");
        io = new() { Kind = "directory", Descendants = ["held.dll", "nested.dll"] };
        Observe(io).Owners.Single().Pid.ShouldBe(41); io.Registered.ShouldBe(["held.dll", "nested.dll"]); io.Registered.ShouldNotContain("tree");
        io = new() { Kind = "directory", Descendants = Enumerable.Range(0, 130).Select(n => $"{n}.dll").ToArray() };
        result = Observe(io); io.Registered.Length.ShouldBe(128); result.Reasons.ShouldContain(FileUseReason.ResourceLimit);
        io = new() { Kind = "absent" }; Observe(io).Status.ShouldBe(FileUseStatus.PathGone); io.Trace.ShouldBeEmpty();
        io = new() { Lists = new([new(234, 4097, [])]) };
        result = Observe(io); result.Coverage.ShouldNotBe(FileUseCoverage.Complete); io.Trace.Count(x => x.StartsWith("list:")).ShouldBe(1);
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        io = new(); Should.Throw<OperationCanceledException>(() => new WindowsFileUseProvider(io).Observe(Request(), canceled.Token)); io.Trace.ShouldBeEmpty();
    }

    [Test]
    public async Task C1539_cim_fallback()
    {
        var io = new Source { StartError = 120, Inventory = [41, 42], ModulePaths = ["held.dll"] };
        var result = Observe(io);
        result.Owners.ShouldContain(o => o.Pid == 41 && o.MatchKind == "LoadedModule");
        io = new() { StartError = 120, Inventory = [41], ModulePaths = [], Argv = "dotnet\0held.dll" };
        result = Observe(io);
        result.Owners.Single().MatchKind.ShouldBe("CommandLineCandidate");
        result.Coverage.ShouldBe(FileUseCoverage.Partial);
        io = new() { Denied = true };
        result = Observe(io);
        result.Owners.Single().Pid.ShouldBe(41); result.Owners.Single().CommandLine.ShouldBeNull();
        result.Owners.Single().State.ShouldBe(FileUseOwnerState.MetadataUnavailable); result.Reasons.ShouldContain(FileUseReason.AccessDenied);
        io = new() { Reused = true };
        result = Observe(io); result.Owners.Single().State.ShouldBe(FileUseOwnerState.IdentityChanged);
        result.Owners.Single().CommandLine.ShouldBeNull(); result.Owners.Single().NativeStart.ShouldBe(Source.StartTime.ToString(System.Globalization.CultureInfo.InvariantCulture));
        io = new() { Exit = true };
        result = Observe(io); result.Owners.Single().State.ShouldBe(FileUseOwnerState.ExitedDuringCapture);
        result.Owners.Single().CommandLine.ShouldBeNull();
        io = new() { Argv = "dotnet\0--token\0fixture-secret\0opaque-secret" };
        var request = Request() with { OwnedGenerations = [new(41, Source.StartTime.ToString(System.Globalization.CultureInfo.InvariantCulture))] };
        var capture = await new FileUseCapture(new WindowsFileUseProvider(io), new NoWriter()).TryCaptureAsync(request);
        capture.Owners.Single().AntiphonOwned.ShouldBe(true); capture.Summary.ShouldNotContain("fixture-secret"); capture.Summary.ShouldNotContain("opaque-secret");
        capture.Owners.Single().CommandLine.ShouldContain("dotnet");
        io = new() { StartError = 120, InventoryDenied = true };
        result = Observe(io); result.Status.ShouldNotBe(FileUseStatus.NoOwnersObserved); result.Coverage.ShouldNotBe(FileUseCoverage.Complete);
    }

    private static FileUseRequest Request() => new("held.dll", new UnauthorizedAccessException("access denied"), Path.GetTempPath());
    private static FileUseObservation Observe(Source source) => new WindowsFileUseProvider(source).Observe(Request(), CancellationToken.None);
    private sealed class NoWriter : IFileUseEvidenceWriter
    { public string? Write(FileUseSnapshot snapshot, string root, CancellationToken cancellationToken) => null; }
    private sealed class Source : WindowsFileUseSource
    {
        public static long StartTime => new DateTime(2026, 10, 23, 0, 0, 0, DateTimeKind.Utc).ToFileTimeUtc();
        public List<string> Trace { get; } = [];
        public Queue<WindowsFileUseList> Lists = new([new(234, 1, []), new(0, 1, [new(41, StartTime, "dotnet")])]);
        public int StartError, RegisterError; public bool ThrowList, Denied, Reused, Exit, InventoryDenied;
        public string Kind = "file", Argv = "dotnet\0--no-logo";
        public string[] Registered = [], Descendants = [], ModulePaths = [];
        public int[] Inventory = [];
        public override bool IsSupported => true;
        public override string PathKind(string path) => Kind;
        public override IEnumerable<string> Files(string directory) => Descendants;
        public override int Start(out uint session) { Trace.Add("start"); session = 9; return StartError; }
        public override int Register(uint session, string[] files) { Registered = files; Trace.Add("register:" + string.Join(',', files)); return RegisterError; }
        public override WindowsFileUseList List(uint session, uint capacity) { Trace.Add("list:" + capacity); if (ThrowList) throw new DllNotFoundException(); return Lists.Dequeue(); }
        public override void End(uint session) => Trace.Add("end");
        public override IEnumerable<int> Processes() => InventoryDenied ? throw new UnauthorizedAccessException() : Inventory;
        public override WindowsFileUseMetadata Metadata(int pid) => Denied ? throw new UnauthorizedAccessException() : Exit ? throw new ArgumentException() : new(pid, StartTime, "dotnet", "dotnet.exe", Argv, null);
        public override long Generation(int pid) => Reused ? StartTime + 1 : StartTime;
        public override IEnumerable<string> Modules(int pid) => ModulePaths;
    }
}
