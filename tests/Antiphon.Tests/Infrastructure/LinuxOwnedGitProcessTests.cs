using System.Diagnostics;
using Antiphon.Server.Infrastructure.Git;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Infrastructure;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class LinuxOwnedGitProcessTests
{
    [Test]
    [Timeout(10_000)]
    [NativeLinuxOnly("Linux managed process pipes; Windows transport belongs to S3.")]
    public async Task Disposal_closes_all_used_streams(CancellationToken ct)
    {
        if (!OperatingSystem.IsLinux()) { Skip.Test("Linux managed pipes"); return; }
        using var adapter = new LinuxOwnedGitProcess(new EmptyMembers());
        using var owner = new OwnedGitProcess(adapter);
        owner.Start(new ProcessStartInfo("/bin/cat")
        {
            UseShellExecute = false, RedirectStandardInput = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        }).State.ShouldBe(OwnedGitLaunchState.Started);
        var input = owner.StandardInput;
        var output = owner.StandardOutput;
        var error = owner.StandardError;
        try
        {
            await input.WriteLineAsync("roundtrip".AsMemory(), ct);
            await input.FlushAsync(ct);
            (await output.ReadLineAsync(ct)).ShouldBe("roundtrip");
            input.Close();
            (await owner.WaitForRootExitAsync(ct)).ShouldBeTrue();
            owner.Dispose();
            output.BaseStream.CanRead.ShouldBeFalse("Dispose must close used stdout");
            error.BaseStream.CanRead.ShouldBeFalse("Dispose must close used stderr");
        }
        finally { input.Dispose(); output.Dispose(); error.Dispose(); }
    }

    private sealed class EmptyMembers : ILinuxOwnedGitMembers
    {
        public OwnedGitRoot CaptureRoot(int pid, DateTime? startUtc) => new(pid, startUtc, null);
        public void SetMarker(string marker) { }
        public ValueTask<OwnedGitMembers> ProbeAsync(OwnedGitRoot? root, OwnedGitDeadline deadline,
            CancellationToken cancellationToken) => ValueTask.FromResult(new OwnedGitMembers(OwnedGitMembershipState.Empty));
        public ValueTask<int> SignalAsync(OwnedGitRoot? root, OwnedGitDeadline deadline,
            CancellationToken cancellationToken) => ValueTask.FromResult(0);
        public void Dispose() { }
    }
}
