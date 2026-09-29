using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Antiphon.Checkpoints;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Checkpoints;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class CheckpointTempUsageTests : CheckpointTestBase
{
    [Test]
    public async Task inspection_is_read_only()
    {
        var sandbox = TempDir();
        var candidate = Path.Combine(sandbox, "c723-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(candidate);
        var sentinel = Path.Combine(candidate, "sentinel.bin");
        await File.WriteAllBytesAsync(sentinel, RandomNumberGenerator.GetBytes(4096));
        var before = SHA256.HashData(await File.ReadAllBytesAsync(sentinel));
        var script = Path.Combine(CheckpointFixtures.RepoRoot, "scripts", "inspect-checkpoint-temp.ps1");
        using var process = Process.Start(new ProcessStartInfo("pwsh")
        {
            ArgumentList = { "-NoProfile", "-File", script, "-TempRoot", sandbox },
            RedirectStandardOutput = true, RedirectStandardError = true,
            UseShellExecute = false,
        })!;
        RegisterCheckpointChild(process);
        var output = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));
        process.ExitCode.ShouldBe(0, error);
        using var report = JsonDocument.Parse(output);
        report.RootElement.GetProperty("roots").GetArrayLength().ShouldBe(1);
        report.RootElement.GetProperty("roots")[0].GetProperty("reason").GetString().ShouldBe("legacy-unmarked");
        SHA256.HashData(await File.ReadAllBytesAsync(sentinel)).ShouldBe(before);
        Directory.Exists(candidate).ShouldBeTrue();
    }

    [Test]
    public void incomplete_trx_never_accepts_residue()
    {
        var trx = Path.Combine(TempDir(), "incomplete.trx");
        CheckpointFixtures.WriteResults(trx, ("N.C.one", "Passed"));
        var parsed = TrxReport.Parse(trx);
        parsed.Ok.ShouldBeTrue();
        var selected = new[] { "N.C.one", "N.C.two" };
        selected.Except(parsed.ExecutedNames, StringComparer.Ordinal).ShouldBe(["N.C.two"]);
        CheckpointUsageRosters.Complete(selected, parsed.ExecutedNames).ShouldBeFalse();
    }

    [Test]
    public void allocated_bytes_include_written_payload()
    {
        var path = Path.Combine(TempDir(), "payload.bin");
        File.WriteAllBytes(path, RandomNumberGenerator.GetBytes(128 * 1024));
        var allocated = CheckpointUsageRosters.AllocatedBytes(path);
        allocated.ShouldBeGreaterThanOrEqualTo(128 * 1024);
        allocated.ShouldBeLessThan(192 * 1024);
    }

    [Test]
    public void repeated_pass_manifest_is_exact()
    {
        var first = new[] { "C.a#0", "C.a#1", "C.b" };
        CheckpointUsageRosters.SameRoster(first, ["C.b", "C.a#1", "C.a#0"]).ShouldBeTrue();
        CheckpointUsageRosters.SameRoster(first, ["C.a#0", "C.a#1"]).ShouldBeFalse();
        CheckpointUsageRosters.Complete(first, ["C.a#0", "C.a#0", "C.b"]).ShouldBeFalse();
    }

    [Test]
    public void assembly_and_allocation_invoke_bounded_sweep()
    {
        var root = TempDir();
        var candidate = Path.Combine(root, "c723-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(candidate);
        var owner = new ProcessIdentityProbe().Current();
        TestRootGuard.Write(candidate, new CheckpointRootMarker
        {
            RootId = Path.GetFileName(candidate)[5..], RootPath = candidate,
            AttemptId = Guid.NewGuid().ToString("N"), AssemblyInvocationId = "usage-unit",
            CreatedAt = DateTimeOffset.UtcNow.AddHours(-1),
            Owner = owner with { Pid = int.MaxValue, StartUtcTicks = 1 },
        });
        var sweep = new CheckpointTempRootSweep(root, options: new CheckpointSweepOptions
        {
            Grace = TimeSpan.Zero, Interval = TimeSpan.Zero, MaxRoots = 1,
        });
        sweep.Register(candidate);
        sweep.SweepOnce().CompletedRoots.ShouldBe(1);
        Directory.Exists(candidate).ShouldBeFalse();
        typeof(CheckpointTempSweepAssemblyHook).GetMethod(nameof(CheckpointTempSweepAssemblyHook.Sweep))
            .ShouldNotBeNull();
    }
}

internal static class CheckpointUsageRosters
{
    public static bool Complete(IEnumerable<string> selected, IEnumerable<string> terminal)
    {
        var expected = selected.ToArray();
        var actual = terminal.ToArray();
        return expected.Length == actual.Length && SameRoster(expected, actual);
    }

    public static bool SameRoster(IEnumerable<string> a, IEnumerable<string> b) =>
        a.Order(StringComparer.Ordinal).SequenceEqual(b.Order(StringComparer.Ordinal));

    public static long AllocatedBytes(string path)
    {
        if (OperatingSystem.IsWindows()) return new FileInfo(path).Length;
        using var process = Process.Start(new ProcessStartInfo("stat")
        {
            ArgumentList = { "-c", "%b", "--", path },
            RedirectStandardOutput = true, UseShellExecute = false,
        })!;
        var blocks = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0) throw new IOException("stat failed");
        return long.Parse(blocks.Trim(), System.Globalization.CultureInfo.InvariantCulture) * 512;
    }
}
