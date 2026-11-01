using System.Diagnostics;
using Antiphon.HostCleanup.Diagnostics;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;
using TUnit.Core.Exceptions;
using Antiphon.TestSupport;

namespace Antiphon.Tests.Infrastructure;

[Category("Integration")]
[Category("Slow")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class FileUseCaptureTests
{
    [NativeLinuxOnly("Requires Linux /proc or the Linux no-follow evidence writer; Windows capture has separate provider coverage.")]
    [Test]
    [Arguments("exclusive-file")]
    [Arguments("mapped-file")]
    public async Task C1539_linux_holder(string mode)
    {
        RequireLinuxIntegration();
        using var fixture = new Holder(mode);
        await fixture.Ready();
        Exception failure = new UnauthorizedAccessException("access denied");
        if (mode == "exclusive-file")
        {
            try { using var conflict = new FileStream(fixture.Path, FileMode.Open, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException error) { failure = error; }
            failure.ShouldBeOfType<IOException>(); // Actual sharing conflict, not an invented Linux unlink failure.
        }
        var snapshot = await new FileUseCapture().TryCaptureAsync(new(fixture.Path, failure, fixture.Evidence));
        var owner = snapshot.Owners.SingleOrDefault(o => o.Pid == fixture.Child.Id);
        owner.ShouldNotBeNull("the capture must name the still-running real child holder");
        owner.Name.ShouldNotBeNullOrWhiteSpace();
        owner.NativeStart.ShouldNotBeNullOrWhiteSpace();
        var stat = await File.ReadAllTextAsync($"/proc/{fixture.Child.Id}/stat");
        owner.NativeStart.ShouldBe(stat[(stat.LastIndexOf(')') + 2)..].Split(' ')[19]);
        owner.StartUtc.ShouldBe(fixture.Child.StartTime.ToUniversalTime());
        owner.ParentPid.ShouldBe(Environment.ProcessId);
        owner.CommandLine.ShouldContain("Antiphon.FileUseTestChild");
        owner.MatchKind.ShouldBe(mode == "mapped-file" ? "MappedFile" : "OpenFile");
        snapshot.Summary.ShouldContain("pid=" + fixture.Child.Id);
        failure.Data["fileUseCaptureSummary"].ShouldBe(snapshot.Summary);
        Console.WriteLine(snapshot.Summary);
        snapshot.EvidencePath.ShouldNotBeNull();
        var json = await File.ReadAllTextAsync(snapshot.EvidencePath);
        json.ShouldContain(snapshot.CaptureId);
        json.ShouldContain(fixture.Child.Id.ToString());
        fixture.Child.HasExited.ShouldBeFalse();
        if (mode == "mapped-file") { File.Delete(fixture.Path); File.Exists(fixture.Path).ShouldBeFalse(); }
        await fixture.Release();
        fixture.Child.HasExited.ShouldBeTrue();
        using var durable = System.Text.Json.JsonDocument.Parse(await File.ReadAllTextAsync(snapshot.EvidencePath));
        durable.RootElement.GetProperty("CaptureId").GetString().ShouldBe(snapshot.CaptureId);
        durable.RootElement.GetProperty("Owners").EnumerateArray().Any(row => row.GetProperty("Pid").GetInt32() == fixture.Child.Id).ShouldBeTrue();
    }

    [Test]
    public async Task C1539_trigger_and_original_error()
    {
        foreach (var error in new Exception[] { new IOException("share", unchecked((int)0x80070020)), new IOException("lock", unchecked((int)0x80070021)), new UnauthorizedAccessException(), new System.ComponentModel.Win32Exception(5), new System.Security.SecurityException(), new System.ComponentModel.Win32Exception(13), new System.ComponentModel.Win32Exception(16), new System.ComponentModel.Win32Exception(26) })
            FileUseCapture.IsEligible(error).ShouldBeTrue(error.GetType().Name);
        foreach (var error in new Exception[] { new IOException("full", unchecked((int)0x80070070)), new OperationCanceledException(), new IOException("cleanup_refused: ownership") })
            FileUseCapture.IsEligible(error).ShouldBeFalse();
        FileUseCapture.IsEligible(new IOException(), "permission denied").ShouldBeTrue();
        var sentinel = new UnauthorizedAccessException("original"); var provider = new Provider((_, _) => throw new Exception("diagnostic failure"));
        try { try { throw sentinel; } catch { await new FileUseCapture(provider).TryCaptureAsync(new("/missing", sentinel, "/missing")); throw; } }
        catch (UnauthorizedAccessException actual) { ReferenceEquals(actual, sentinel).ShouldBeTrue(); actual.StackTrace.ShouldContain(nameof(C1539_trigger_and_original_error)); actual.HResult.ShouldBe(sentinel.HResult); }
    }

    [NativeLinuxOnly("Requires Linux /proc or the Linux no-follow evidence writer; Windows capture has separate provider coverage.")]
    [Test]
    [Arguments("denied")]
    [Arguments("already-free")]
    [Arguments("missing")]
    [Arguments("zero")]
    [Arguments("exited")]
    public async Task C1539_absent_inputs(string kind)
    {
        RequireLinuxIntegration();
        using var fixture = new Holder("exclusive-file"); await fixture.Ready();
        var source = new ControlledSource(fixture.Child.Id) { Denied = kind == "denied", ExitOnEnrichment = kind == "exited" };
        if (kind is "already-free" or "zero") { await fixture.Release(); source = new ControlledSource(Environment.ProcessId); }
        var path = kind == "missing" ? fixture.Path + ".missing" : fixture.Path;
        var result = await new FileUseCapture(new LinuxFileUseProvider(source)).TryCaptureAsync(new(path, new UnauthorizedAccessException("failure"), fixture.Evidence));
        result.FailureText.ShouldContain("failure"); result.FailureUtc.ShouldBeLessThanOrEqualTo(result.ObservationUtc);
        switch (kind)
        {
            case "denied": result.Status.ShouldBe(FileUseStatus.Unavailable); result.Reasons.ShouldContain(FileUseReason.AccessDenied); break;
            case "missing": result.Status.ShouldBe(FileUseStatus.PathGone); result.Owners.ShouldBeEmpty(); break;
            case "exited": result.Status.ShouldBe(FileUseStatus.Partial); result.Reasons.ShouldContain(FileUseReason.ExitedDuringCapture); result.Owners.Single().State.ShouldBe(FileUseOwnerState.ExitedDuringCapture); result.Owners.Single().CommandLine.ShouldBeNull(); break;
            default: result.Status.ShouldBe(FileUseStatus.NoOwnersObserved); result.Coverage.ShouldBe(FileUseCoverage.Complete); result.Owners.ShouldBeEmpty(); break;
        }
    }

    [NativeLinuxOnly("Requires Linux /proc or the Linux no-follow evidence writer; Windows capture has separate provider coverage.")]
    [Test]
    public async Task C1539_budget_and_caps()
    {
        RequireLinuxIntegration();
        using var scratch = new Scratch();
        var time = new Microsoft.Extensions.Time.Testing.FakeTimeProvider();
        using var started = new ManualResetEventSlim(); using var release = new ManualResetEventSlim(); using var done = new ManualResetEventSlim();
        var provider = new Provider((_, _) => { started.Set(); release.Wait(); done.Set(); return Empty(); });
        var capture = new FileUseCapture(provider, timeProvider: time);
        var request = new FileUseRequest(scratch.Path, new UnauthorizedAccessException(), scratch.Evidence) { Budget = TimeSpan.FromSeconds(5) };
        FileUseSnapshot? timedOut = null;
        var pending = capture.TryCaptureAsync(request); started.Wait(TimeSpan.FromSeconds(2)).ShouldBeTrue();
        time.Advance(TimeSpan.FromSeconds(5));
        try
        {
            timedOut = await pending.WaitAsync(TimeSpan.FromSeconds(2)); timedOut.Status.ShouldBe(FileUseStatus.TimedOut);
            (await capture.TryCaptureAsync(request with { Operation = new() })).Status.ShouldBe(FileUseStatus.Busy);
            provider.Calls.ShouldBe(1);
        }
        finally { release.Set(); done.Wait(TimeSpan.FromSeconds(2)).ShouldBeTrue(); await capture.DrainAsync().WaitAsync(TimeSpan.FromSeconds(2)); }
        (await capture.TryCaptureAsync(request)).CaptureId.ShouldBe(timedOut!.CaptureId); provider.Calls.ShouldBe(1);
        var many = Enumerable.Range(1, 40).Select(i => Owner(i) with { Name = new string('n', 900), CommandLine = "dotnet\0" + new string('v', 6000), Ancestors = Enumerable.Range(1, 12).Select(n => new FileUseAncestor(n, "1", new string('a', 300), "opaque")).ToArray() }).ToArray();

        var bounded = await new FileUseCapture(new Provider((_, _) => new(FileUseStatus.Observed, FileUseCoverage.Complete, many, []))).TryCaptureAsync(new(scratch.Path, new UnauthorizedAccessException(), scratch.Evidence));
        bounded.Owners.Count.ShouldBeLessThanOrEqualTo(32); bounded.Omitted.ShouldBeGreaterThan(0);
        bounded.Owners.ShouldAllBe(o => o.Name.Length <= 256 && o.Ancestors.Count <= 8 && (o.CommandLine == null || o.CommandLine.Length <= 4096));
        System.Text.Encoding.UTF8.GetByteCount(bounded.Summary).ShouldBeLessThanOrEqualTo(8192);
        new FileInfo(bounded.EvidencePath!).Length.ShouldBeLessThanOrEqualTo(32768);
        await AssertIndependentCaps(scratch);
        foreach (var limit in new[] { "process", "fd", "maps", "total", "resource" })
        {
            var src = new LimitSource(limit);
            var result = await new FileUseCapture(new LinuxFileUseProvider(src)).TryCaptureAsync(new(scratch.Path, new UnauthorizedAccessException(), scratch.Evidence) { AdditionalPaths = limit == "resource" ? Enumerable.Repeat(scratch.Path, 130).ToArray() : [] });
            result.Coverage.ShouldNotBe(FileUseCoverage.Complete, limit); result.Omitted.ShouldBeGreaterThan(0, limit);
            if (limit == "total") { src.MapsReads.ShouldBeLessThanOrEqualTo(8); result.Reasons.ShouldContain(FileUseReason.InputLimit); result.Reasons.ShouldNotContain(FileUseReason.ProcessLimit); }
        }
    }

    // Exact effective bounds: keep these small enough that the JSON cap cannot hide a missing owner/ancestor cap.
    private static async Task AssertIndependentCaps(Scratch scratch)
    {
        async Task<FileUseSnapshot> Capture(IReadOnlyList<FileUseOwner> owners) => await new FileUseCapture(new Provider((_, _) => new(FileUseStatus.Observed, FileUseCoverage.Complete, owners, []))).TryCaptureAsync(new(scratch.Path, new UnauthorizedAccessException(), scratch.Evidence));
        var owners = await Capture(Enumerable.Range(1, 33).Select(Owner).ToArray()); owners.Owners.Count.ShouldBe(32); owners.Reasons.ShouldContain(FileUseReason.OwnerLimit);
        var ancestry = await Capture([Owner(42) with { Ancestors = Enumerable.Range(1, 9).Select(n => new FileUseAncestor(n, "1", "parent", null)).ToArray() }]);
        ancestry.Owners.Single().Ancestors.Count.ShouldBe(8); ancestry.Reasons.ShouldContain(FileUseReason.AncestorLimit);
        var names = await Capture([Owner(42) with { Name = new string('n', 257), Resource = "/" + new string('r', 1024) }]);
        names.Owners.Single().Name.Length.ShouldBeLessThanOrEqualTo(256); names.Owners.Single().Resource.Length.ShouldBeLessThanOrEqualTo(1024); names.Reasons.ShouldContain(FileUseReason.TextLimit);
        var modules = Enumerable.Range(1, 24).Select(n => "/modules/" + n + new string('a', 210) + ".dll").ToArray();
        var command = await Capture([Owner(42) with { CommandLine = "dotnet\0" + string.Join('\0', modules), ProvenModules = modules }]);
        command.Owners.Single().CommandLine!.Length.ShouldBeLessThanOrEqualTo(4096); command.Owners.Single().CommandLine!.Length.ShouldBeGreaterThan(3500);
        using var holder = new Holder("exclusive-file"); await holder.Ready();
        var resources = await new FileUseCapture(new LinuxFileUseProvider(new ControlledSource(holder.Child.Id))).TryCaptureAsync(new(scratch.Path, new UnauthorizedAccessException(), scratch.Evidence) { AdditionalPaths = Enumerable.Repeat(scratch.Path, 127).Append(holder.Path).ToArray() });
        resources.Owners.ShouldBeEmpty(); resources.Reasons.ShouldContain(FileUseReason.ResourceLimit);
        await holder.Release();
        // Exercise the real bounded reader with an actual owned oversized input.
        var oversized = System.IO.Path.Combine(scratch.Root, "oversized"); File.WriteAllBytes(oversized, new byte[1024 * 1024 + 1]);
        Should.Throw<FileUseInputLimitException>(() => new LinuxFileUseSource().Read(oversized, 1024 * 1024));
        var operation = new FileUseOperation(); var provider = new Provider((_, _) => Empty()); var service = new FileUseCapture(provider);
        var first = await service.TryCaptureAsync(new(scratch.Path, new UnauthorizedAccessException(), scratch.Evidence) { Operation = operation });
        (await service.TryCaptureAsync(new(scratch.Path, new UnauthorizedAccessException(), scratch.Evidence) { Operation = operation })).CaptureId.ShouldBe(first.CaptureId);
        for (var n = 1; n < 8; n++) await service.TryCaptureAsync(new(scratch.Path + n, new UnauthorizedAccessException(), scratch.Evidence) { Operation = operation });
        var suppressed = await service.TryCaptureAsync(new(scratch.Path + "ninth", new UnauthorizedAccessException(), scratch.Evidence) { Operation = operation });
        suppressed.Reasons.ShouldContain(FileUseReason.Suppressed); provider.Calls.ShouldBe(8);
    }
    [NativeLinuxOnly("Requires Linux /proc or the Linux no-follow evidence writer; Windows capture has separate provider coverage.")]
    [Test]
    public async Task C1539_redaction_before_sinks()
    {
        RequireLinuxIntegration();
        using var scratch = new Scratch();
        var secret = "synthetic-sensitive-marker";
        var values = new[] { "{{key:fixture}}", "sk-abcdefghijk", "xoxb-fixture-123", "ghp_1234567890123456789012345", "github_pat_1234567890123456789012345", "Bearer fixture-bearer", "12345678:12345678901234567890123456789012345", "Password=fixture-password", "clientApiKey=fixture-api", "--token fixture-switch", "postgres://user:fixture-url@db/path?fixture-query#fixture-fragment", "-EncodedCommand fixture-payload", "opaque-positional-value", secret };
        var raw = "opaque-argv-zero\0" + string.Join('\0', values);
        var owner = Owner(42) with { Name = "fixture " + secret, CommandLine = raw, Ancestors = [new(1, "1", secret, raw)] };
        var snapshot = await new FileUseCapture(new Provider((_, _) => new(FileUseStatus.Observed, FileUseCoverage.Complete, [owner], []) { Backend = secret })).TryCaptureAsync(new(scratch.Path, new UnauthorizedAccessException(string.Join(' ', values)), scratch.Evidence) { SecretValues = [secret] });
        var json = await File.ReadAllTextAsync(snapshot.EvidencePath!);
        foreach (var marker in new[] { secret, "fixture-bearer", "fixture-password", "fixture-api", "fixture-switch", "fixture-url", "fixture-query", "fixture-fragment", "fixture-payload", "opaque-positional-value", "opaque-argv-zero", "abcdefghijk", "xoxb-fixture-123", "ghp_1234567890123456789012345" })
        { snapshot.Summary.ShouldNotContain(marker); json.ShouldNotContain(marker); }
        snapshot.Owners[0].CommandLine.ShouldStartWith("dotnet");
        var truncated = owner with { CommandLine = new string('x', 65537) };
        var result = await new FileUseCapture(new Provider((_, _) => new(FileUseStatus.Observed, FileUseCoverage.Complete, [truncated], []))).TryCaptureAsync(new(scratch.Path, new UnauthorizedAccessException(), scratch.Evidence));
        result.Owners[0].CommandLine.ShouldBeNull(); result.Reasons.ShouldContain(FileUseReason.RedactionUnavailable);
    }

    [NativeLinuxOnly("Requires Linux /proc or the Linux no-follow evidence writer; Windows capture has separate provider coverage.")]
    [Test]
    public async Task C1539_persistence_fail_safe()
    {
        RequireLinuxIntegration();
        using var scratch = new Scratch();
        var provider = new Provider((_, _) => Empty());
        var request = new FileUseRequest(scratch.Path, new UnauthorizedAccessException("original"), scratch.Evidence);
        var a = await new FileUseCapture(provider).TryCaptureAsync(request); var b = await new FileUseCapture(provider).TryCaptureAsync(request with { Operation = new() });
        a.EvidencePath.ShouldNotBe(b.EvidencePath); File.Exists(a.EvidencePath).ShouldBeTrue();
        using (var doc = System.Text.Json.JsonDocument.Parse(await File.ReadAllTextAsync(a.EvidencePath!))) doc.RootElement.GetProperty("CaptureId").GetString().ShouldBe(a.CaptureId);
        var broken = await new FileUseCapture(provider, new BrokenWriter()).TryCaptureAsync(request with { Operation = new() });
        broken.Reasons.ShouldContain(FileUseReason.EvidenceUnavailable); broken.FailureText.ShouldBe("original");
        var link = System.IO.Path.Combine(scratch.Root, "link"); Directory.CreateSymbolicLink(link, scratch.Evidence);
        var linked = await new FileUseCapture(provider).TryCaptureAsync(request with { EvidenceRoot = link, Operation = new() });
        linked.EvidencePath.ShouldBeNull(); linked.Reasons.ShouldContain(FileUseReason.EvidenceUnavailable);
        var inside = await new FileUseCapture(provider).TryCaptureAsync(request with { Path = scratch.Root, EvidenceRoot = scratch.Evidence, Operation = new() });
        inside.EvidencePath.ShouldBeNull();
        var time = new Microsoft.Extensions.Time.Testing.FakeTimeProvider(); using var gate = new ManualResetEventSlim(); using var started = new ManualResetEventSlim();
        var sink = new BlockedWriter(gate, started); var capture = new FileUseCapture(provider, sink, time);
        var pending = capture.TryCaptureAsync(request with { Operation = new() }); started.Wait(TimeSpan.FromSeconds(2)).ShouldBeTrue(); time.Advance(TimeSpan.FromSeconds(5));
        try { (await pending.WaitAsync(TimeSpan.FromSeconds(2))).Reasons.ShouldContain(FileUseReason.EvidenceUnavailable); }
        finally { gate.Set(); await capture.DrainAsync().WaitAsync(TimeSpan.FromSeconds(2)); }
        sink.Published.ShouldBeFalse();
    }

    [NativeLinuxOnly("Requires Linux /proc or the Linux no-follow evidence writer; Windows capture has separate provider coverage.")]
    [Test]
    public async Task C1539_generation_and_ownership()
    {
        RequireLinuxIntegration();
        using var fixture = new Holder("exclusive-file"); await fixture.Ready();
        var src = new ControlledSource(fixture.Child.Id); var request = new FileUseRequest(fixture.Path, new UnauthorizedAccessException(), fixture.Evidence);
        var first = await new FileUseCapture(new LinuxFileUseProvider(src)).TryCaptureAsync(request);
        var child = first.Owners.Single(o => o.Pid == fixture.Child.Id); child.AntiphonOwned.ShouldBeNull();
        var owned = await new FileUseCapture(new LinuxFileUseProvider(new ControlledSource(fixture.Child.Id))).TryCaptureAsync(request with { Operation = new(), OwnedGenerations = [new(child.Pid, child.NativeStart)] });
        owned.Owners.Single().AntiphonOwned.ShouldBe(true);
        var inherited = await new FileUseCapture(new LinuxFileUseProvider(new ControlledSource(fixture.Child.Id))).TryCaptureAsync(request with { Operation = new(), OwnedGenerations = [new(Environment.ProcessId, child.Ancestors.Single(a => a.Pid == Environment.ProcessId).NativeStart)] });
        inherited.Owners.Single().AntiphonOwned.ShouldBe(true);
        var foreign = await new FileUseCapture(new LinuxFileUseProvider(new ControlledSource(fixture.Child.Id))).TryCaptureAsync(request with { Operation = new(), ForeignGenerations = [new(child.Pid, child.NativeStart)] });
        foreign.Owners.Single().AntiphonOwned.ShouldBe(false);
        var reused = await new FileUseCapture(new LinuxFileUseProvider(new ControlledSource(fixture.Child.Id) { ChangeIdentity = true })).TryCaptureAsync(request with { Operation = new() });
        reused.Reasons.ShouldContain(FileUseReason.IdentityChanged); reused.Owners.Single().State.ShouldBe(FileUseOwnerState.IdentityChanged); reused.Owners.Single().CommandLine.ShouldBeNull(); reused.Owners.Single().NativeStart.ShouldBe(child.NativeStart);
    }

    [Test]
    public async Task C1539_success_is_inert()
    {
        var provider = new Provider((_, _) => throw new Exception("must not scan"));
        var result = await new FileUseCapture(provider, new BrokenWriter()).TryCaptureAsync(new("/unused", new IOException("unrelated"), "/unused"));
        result.Status.ShouldBe(FileUseStatus.NotTriggered); provider.Calls.ShouldBe(0); result.EvidencePath.ShouldBeNull();
    }

    private static void RequireLinuxIntegration()
    {
        // These rows require /proc identities, mmap/unlink semantics, or the Linux-only
        // no-follow evidence writer. Windows provider contracts live in FileUseWindowsTests;
        // the shipped Windows evidence writer explicitly remains unavailable (S2 debt).
        if (!OperatingSystem.IsLinux())
            throw new SkipTestException("Requires Linux /proc or the Linux no-follow evidence writer; Windows capture has separate provider coverage.");
    }

    private static FileUseObservation Empty() => new(FileUseStatus.NoOwnersObserved, FileUseCoverage.Complete, [], []);
    private static FileUseOwner Owner(int pid) => new(pid, "dotnet", "generation", null, "dotnet\0opaque", 1, [], null, "unknown", "OpenFile", "/held") { ProvenExecutable = "/usr/bin/dotnet" };
    private sealed class Provider(Func<FileUseRequest, CancellationToken, FileUseObservation> action) : IFileUseProvider
    { public int Calls; public FileUseObservation Observe(FileUseRequest request, CancellationToken token) { Interlocked.Increment(ref Calls); return action(request, token); } }
    private sealed class BrokenWriter : IFileUseEvidenceWriter
    { public string? Write(FileUseSnapshot snapshot, string root, CancellationToken token) => throw new UnauthorizedAccessException("sink failure"); }
    private sealed class BlockedWriter(ManualResetEventSlim gate, ManualResetEventSlim started) : IFileUseEvidenceWriter
    { public bool Published; public string? Write(FileUseSnapshot snapshot, string root, CancellationToken token) { started.Set(); gate.Wait(); token.ThrowIfCancellationRequested(); Published = true; return "/unexpected"; } }
    private sealed class ControlledSource(int pid) : LinuxFileUseSource
    {
        public bool Denied, ExitOnEnrichment, ChangeIdentity; private int statReads;
        public override IEnumerable<int> Processes() => Denied ? throw new UnauthorizedAccessException() : [pid];
        public override string Read(string path, int maximumBytes)
        {
            if (path == $"/proc/{pid}/stat" && ++statReads > 1)
            {
                if (ExitOnEnrichment) throw new FileNotFoundException();
                if (ChangeIdentity) { var raw = base.Read(path, maximumBytes); var split = raw.LastIndexOf(')'); var parts = raw[(split + 2)..].Split(' '); parts[19] = "99999999999"; return raw[..(split + 2)] + string.Join(' ', parts); }
            }
            return base.Read(path, maximumBytes);
        }
    }
    private sealed class LimitSource(string limit) : LinuxFileUseSource
    {
        public int MapsReads;
        public override IEnumerable<int> Processes() => limit is "process" or "total" ? Enumerable.Repeat(Environment.ProcessId, 4100) : [Environment.ProcessId];
        public override IEnumerable<string> Fds(int pid) => limit == "fd" ? Enumerable.Repeat("/proc/self/fd/999999", 4100) : [];
        public override string Read(string path, int maximumBytes)
        {
            if (path.EndsWith("/maps", StringComparison.Ordinal) && limit is "maps" or "total") { MapsReads++; throw new FileUseInputLimitException(); }
            return base.Read(path, maximumBytes);
        }
    }
    private sealed class Scratch : IDisposable
    {
        public string Root { get; } = Directory.CreateTempSubdirectory("c1539-scratch-").FullName;
        public string Path => System.IO.Path.Combine(Root, "resource");
        public string Evidence => System.IO.Path.Combine(Root, "evidence");
        public Scratch() => File.WriteAllText(Path, "fixture");
        public void Dispose() => Directory.Delete(Root, true);
    }
    private sealed class Holder : IDisposable
    {
        private readonly string root = Directory.CreateTempSubdirectory("c1539-holder-").FullName;
        private readonly string nonce = Guid.NewGuid().ToString("N");

        public string Path { get; }
        public string Evidence => System.IO.Path.Combine(root, "evidence");
        public Process Child { get; }
        public Holder(string mode)
        {
            Path = System.IO.Path.Combine(root, "held file.bin"); File.WriteAllBytes(Path, new byte[4096]);
            var start = new ProcessStartInfo("dotnet") { RedirectStandardInput = true, RedirectStandardOutput = true,
                RedirectStandardError = true, UseShellExecute = false };
            foreach (var arg in new[] { System.IO.Path.Combine(AppContext.BaseDirectory, "file-use-child", "Antiphon.FileUseTestChild.dll"), mode, Path, nonce }) start.ArgumentList.Add(arg);
            Child = Process.Start(start) ?? throw new IOException("fixture launch failed");
        }
        public async Task Ready() => (await Child.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5))).ShouldBe("READY " + nonce);
        public async Task Release()
        {
            await Child.StandardInput.WriteLineAsync("RELEASE " + nonce); await Child.StandardInput.FlushAsync();
            await Child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5)); Child.ExitCode.ShouldBe(0);
        }
        public void Dispose()
        {
            try
            {
                if (!Child.HasExited) { Child.StandardInput.WriteLine("RELEASE " + nonce); Child.StandardInput.Flush(); }
                if (!Child.WaitForExit(5000)) { Child.Kill(true); Child.WaitForExit(); throw new IOException("fixture rescue required"); }
            }
            finally { Child.Dispose(); Directory.Delete(root, true); }
        }
    }
}
