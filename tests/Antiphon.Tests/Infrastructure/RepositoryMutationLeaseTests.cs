using System.Diagnostics;
using Antiphon.Server.Infrastructure.Git;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Infrastructure;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class RepositoryMutationLeaseTests
{
    [Test]
    public async Task C448_V13_WindowsJunctionAndOtherProcessShareTheLease()
    {
        if (!OperatingSystem.IsWindows()) throw new TUnit.Core.Exceptions.SkipTestException("Requires Windows junctions");
        await using var fixture = new LandingGitFixture();
        await fixture.InitializeAsync();
        var alias = Path.Combine(fixture.Root, "alias");
        await RunChildAsync("cmd.exe", ["/c", "mklink", "/J", alias, fixture.Source], expectedExit: 0);
        try
        {
            (File.GetAttributes(alias) & FileAttributes.ReparsePoint).ShouldBe(FileAttributes.ReparsePoint);
            var provider = new RepositoryMutationLease(fixture.Git);
            await using var lease = await provider.TryAcquireAsync(fixture.Repository, CancellationToken.None);
            lease.ShouldNotBeNull();
            (await provider.TryAcquireAsync(alias, CancellationToken.None)).ShouldBeNull();
            var worker = Path.Combine(fixture.Root, "lease-worker.ps1");
            await File.WriteAllTextAsync(worker, """
                try {
                    $stream = [IO.File]::Open($args[0], [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
                    $stream.Dispose()
                    exit 10
                } catch [IO.IOException] { exit 0 }
                """);
            await RunChildAsync("pwsh", ["-NoProfile", "-File", worker,
                Path.Combine(lease.CommonDirectory, "antiphon", "landing.lock")], expectedExit: 0);
            provider.Owns(lease, lease.CommonDirectory).ShouldBeTrue();
            await fixture.AssertRemoteSourceAsync();
        }
        finally { Directory.Delete(alias, recursive: false); }
    }

    private static async Task RunChildAsync(string executable, string[] arguments, int expectedExit)
    {
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var child = Process.Start(start)!;
        var output = child.StandardOutput.ReadToEndAsync();
        var error = child.StandardError.ReadToEndAsync();
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try { await child.WaitForExitAsync(budget.Token); }
        catch (OperationCanceledException)
        {
            if (!child.HasExited) child.Kill(entireProcessTree: true);
            await child.WaitForExitAsync();
            throw;
        }
        await Task.WhenAll(output, error);
        child.ExitCode.ShouldBe(expectedExit);
    }

    [Test]
    public async Task C448_V13_LeaseUsesCommonRepositoryIdentity()
    {
        await using var fixture = new LandingGitFixture();
        await fixture.InitializeAsync();
        var provider = new RepositoryMutationLease(fixture.Git);
        var otherProvider = new RepositoryMutationLease(fixture.Git);
        var first = await provider.TryAcquireAsync(fixture.Repository, CancellationToken.None);
        first.ShouldNotBeNull();
        await using (first)
        {
            provider.Owns(first, await fixture.Git.CommonDirectoryAsync(fixture.Source, CancellationToken.None)).ShouldBeTrue();
            otherProvider.Owns(first, first.CommonDirectory).ShouldBeFalse();
            (await otherProvider.TryAcquireAsync(fixture.Source, CancellationToken.None)).ShouldBeNull();
            await using var independent = await otherProvider.TryAcquireAsync(fixture.Remote, CancellationToken.None);
            independent.ShouldNotBeNull();
        }
        provider.Owns(first, first.CommonDirectory).ShouldBeFalse();
        File.Exists(Path.Combine(first.CommonDirectory, "antiphon", "landing.lock")).ShouldBeTrue();
        await using var reacquired = await otherProvider.TryAcquireAsync(fixture.Source, CancellationToken.None);
        reacquired.ShouldNotBeNull();
        await fixture.AssertRemoteSourceAsync();
    }
}
