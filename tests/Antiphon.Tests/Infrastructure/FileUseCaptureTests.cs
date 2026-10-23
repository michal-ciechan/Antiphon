using System.Diagnostics;
using Antiphon.HostCleanup.Diagnostics;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Infrastructure;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class FileUseCaptureTests
{
    [Test]
    [Arguments("exclusive-file")]
    [Arguments("mapped-file")]
    public async Task C1539_linux_holder(string mode)
    {
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
        owner.ParentPid.ShouldBe(Environment.ProcessId);
        owner.CommandLine.ShouldContain("Antiphon.FileUseTestChild");
        owner.MatchKind.ShouldBe(mode == "mapped-file" ? "MappedFile" : "OpenFile");
        snapshot.Summary.ShouldContain("pid=" + fixture.Child.Id);
        snapshot.EvidencePath.ShouldNotBeNull();
        var json = await File.ReadAllTextAsync(snapshot.EvidencePath);
        json.ShouldContain(snapshot.CaptureId);
        json.ShouldContain(fixture.Child.Id.ToString());
        fixture.Child.HasExited.ShouldBeFalse();
        await fixture.Release();
    }

    private sealed class Holder : IDisposable
    {
        private readonly string root = Directory.CreateTempSubdirectory("c1539-holder-").FullName;
        private readonly string nonce = Guid.NewGuid().ToString("N");
        private bool released;
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
            await Child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5)); Child.ExitCode.ShouldBe(0); released = true;
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
