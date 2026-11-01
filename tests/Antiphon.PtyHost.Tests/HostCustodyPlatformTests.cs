using System.Diagnostics;
using Antiphon.Agents.Pty;
using Antiphon.PtyHost.Protocol;
using Antiphon.SessionRunner.Contracts;
using Shouldly;
using TUnit.Core;

namespace Antiphon.PtyHost.Tests;

/// <summary>
/// CARD-1614: a host must refuse an opposite-OS custody binding before native start,
/// even when the binding and the host option name the same backend.
/// </summary>
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class HostCustodyPlatformTests
{
    [Test]
    public async Task C1614_Foreign_platform_binding_is_refused_before_native_start()
    {
        var opposite = OperatingSystem.IsWindows()
            ? VerificationCustodyBackends.LinuxCgroup
            : VerificationCustodyBackends.WindowsJob;
        await using var host = HostHarness.Start(o => o with
        {
            PtyBackend = OperatingSystem.IsWindows() ? "modern" : "inbox",
            CustodyStoreRoot = Path.Combine(Path.GetDirectoryName(o.ManifestDir)!, "custody"),
            CustodyBackend = opposite,
        });
        var (store, binding, marker) = Reserve(host, opposite);
        Guid? container = null;
        try
        {
            var reply = await host.Session.LaunchAsync(Launch(host, store, binding, marker), CancellationToken.None);
            container = store.ReadRecord<CustodyRoot>(binding, "tracking.json")?.ContainerId;
            reply.ShouldBeOfType<ErrorMessage>();
            File.Exists(marker).ShouldBeFalse();
            File.Exists(store.PathFor(binding.ExecutionId, "native-start-intent.json")).ShouldBeFalse();
            File.Exists(store.PathFor(binding.ExecutionId, "producer-receipt.json")).ShouldBeFalse();
            (await host.Session.GetCustodyAsync(binding, CancellationToken.None)).Receipt.ShouldBeNull();
        }
        finally
        {
            TryKillContainer(container);
        }
    }

    private static (VerificationCustodyStore Store, VerificationExecutionBinding Binding, string Marker) Reserve(
        HostHarness host, string backend)
    {
        var root = Path.GetFullPath(host.TempDir);
        var store = new VerificationCustodyStore(host.Options.CustodyStoreRoot!);
        var snapshot = Path.Combine(root, "snapshot");
        Directory.CreateDirectory(snapshot);
        Directory.CreateDirectory(Path.Combine(root, ".git", "worktrees", "snapshot"));
        var now = DateTime.UtcNow;
        var binding = new VerificationExecutionBinding(Guid.NewGuid(),
            new(Guid.NewGuid(), Guid.NewGuid(), new string('a', 40)),
            new(host.SessionId, new DateTime(now.Ticks - now.Ticks % 10, DateTimeKind.Utc)),
            new(root, Path.Combine(root, ".git"), snapshot,
                Path.Combine(root, ".git", "worktrees", "snapshot"), "feat/custody-platform", Guid.NewGuid()),
            backend, RunnerStoreId: store.StoreId);
        store.Reserve(binding);
        store.WriteRecord(binding, "runner-start-intent.json", new CustodyStamp(1, DateTime.UtcNow));
        return (store, binding, Path.Combine(root, "payload-ran"));
    }

    private static LaunchMessage Launch(HostHarness host, VerificationCustodyStore store,
        VerificationExecutionBinding binding, string marker)
    {
        if (OperatingSystem.IsWindows())
            return host.CmdLaunch("echo ran> payload-ran") with
            {
                Cwd = binding.Creation.WorktreePath,
                VerificationBinding = binding,
                RunnerStoreId = store.StoreId,
            };

        var script = Path.Combine(host.TempDir, "marker.sh");
        File.WriteAllText(script, "#!/bin/sh\necho ran > \"$1\"\n");
        return new LaunchMessage("/bin/sh", [script, marker], new Dictionary<string, string>(),
            binding.Creation.WorktreePath, 80, 24, MemoryLimitMb: 0, TranscriptEnabled: false,
            host.AnsiLogPath, AcceptedStartedAt: SessionGeneration.Normalize(DateTime.UtcNow))
        {
            VerificationBinding = binding,
            RunnerStoreId = store.StoreId,
        };
    }

    private static void TryKillContainer(Guid? container)
    {
        if (container is null || !OperatingSystem.IsLinux())
            return;
        try
        {
            using var kill = Process.Start(new ProcessStartInfo(LinuxCgroupContainment.SudoExe)
            {
                ArgumentList = { "-n", LinuxCgroupContainment.KillHelper, container.Value.ToString("D") },
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            });
            kill?.WaitForExit(45_000);
        }
        catch
        {
            // The case assertions are the verdict. Teardown cannot invent a receipt.
        }
    }
}
