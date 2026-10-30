using System.Diagnostics;
using System.Text.Json;
using Antiphon.Agents.Pty;
using Antiphon.PtyHost.Protocol;
using Antiphon.SessionRunner.Contracts;
using Shouldly;
using TUnit.Core;
using TUnit.Core.Exceptions;

namespace Antiphon.PtyHost.Tests;

/// <summary>
/// CARD-1614 round 2. Portable Linux host witnesses. A fake containment stands in for the
/// cgroup; nothing here calls sudo or the installed helpers.
/// </summary>
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class LinuxCustodyReleaseHostTests
{
    [Test]
    public async Task C1614_Released_launch_failure_exits_within_ttl()
    {
        if (!OperatingSystem.IsLinux())
            throw new SkipTestException("The inbox pty launch runs on Linux.");
        var ttl = TimeSpan.FromMilliseconds(400);
        var files = new TrackingRefusal();
        await using var host = HostHarness.Start(o => o with
        {
            PtyBackend = "inbox",
            CustodyStoreRoot = Path.Combine(Path.GetDirectoryName(o.ManifestDir)!, "custody"),
            CustodyBackend = VerificationCustodyBackends.LinuxCgroup,
            LingerTtl = ttl,
        }, files);
        var containment = new FakeContainment();
        host.Session.UseContainmentForTests(containment);
        var (store, binding) = Reserve(host, files);
        try
        {
            var reply = await host.Session.LaunchAsync(Spec(host, store, binding, "sleep 30"), CancellationToken.None);
            reply.ShouldBeOfType<ErrorMessage>();
            files.Failures.ShouldBeGreaterThan(0);
            File.Exists(store.PathFor(binding.ExecutionId, "native-start-intent.json")).ShouldBeTrue();
            File.Exists(store.PathFor(binding.ExecutionId, "tracking.json")).ShouldBeFalse();

            var ns = store.ReadRecord<LinuxNamespaceRecord>(binding, "linux-namespace.json");
            ns.ShouldNotBeNull();
            if (ns!.NamespaceId == Guid.Empty)
            {
                store.WriteRecord(binding, "linux-namespace.json",
                    ns with { NamespaceId = Guid.NewGuid() });
            }

            store.WriteRecord(binding, "runner-seal.json", new CustodyStamp(3, DateTime.UtcNow));
            containment.Pids.Clear();
            var status = await host.Session.GetCustodyAsync(binding, true, CancellationToken.None);
            status.State.ShouldBe(VerificationCustodyState.Unknown);
            status.Receipt.ShouldBeNull();
            File.Exists(store.PathFor(binding.ExecutionId, "linux-release-disposition.json")).ShouldBeTrue();
            File.Exists(store.PathFor(binding.ExecutionId, "producer-receipt.json")).ShouldBeFalse();

            var done = await Task.WhenAny(host.RunTask, Task.Delay(ttl + TimeSpan.FromSeconds(2)));
            done.ShouldBe(host.RunTask);
            await host.RunTask;
        }
        finally
        {
            try
            {
                if (files.Child is { HasExited: false })
                    files.Child.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException) { }
            files.Child?.Dispose();
        }
    }

    private static (VerificationCustodyStore Store, VerificationExecutionBinding Binding) Reserve(
        HostHarness host, IVerificationCustodyFiles files)
    {
        var store = new VerificationCustodyStore(host.Options.CustodyStoreRoot!, files: files);
        var snapshot = Path.Combine(host.TempDir, "snapshot");
        Directory.CreateDirectory(snapshot);
        var now = DateTime.UtcNow;
        var binding = new VerificationExecutionBinding(Guid.NewGuid(),
            new(Guid.NewGuid(), Guid.NewGuid(), new string('a', 40)),
            new(host.SessionId, new DateTime(now.Ticks - now.Ticks % 10, DateTimeKind.Utc)),
            new(host.TempDir, Path.Combine(host.TempDir, ".git"), snapshot,
                Path.Combine(host.TempDir, ".git", "worktrees", "snapshot"), "feat/c1614", Guid.NewGuid()),
            VerificationCustodyBackends.LinuxCgroup, store.StoreId);
        store.Reserve(binding);
        store.WriteRecord(binding, "runner-start-intent.json", new CustodyStamp(1, DateTime.UtcNow));
        return (store, binding);
    }

    private static LaunchMessage Spec(HostHarness host, VerificationCustodyStore store,
        VerificationExecutionBinding binding, params string[] lines)
    {
        var script = Path.Combine(host.TempDir, "payload-" + Guid.NewGuid().ToString("N") + ".sh");
        File.WriteAllText(script, "#!/bin/sh\nset -u\n" + string.Join('\n', lines) + "\n");
        return new LaunchMessage("/bin/sh", [script], new Dictionary<string, string>(),
            binding.Creation.WorktreePath, 80, 24, 0, false, host.AnsiLogPath,
            AcceptedStartedAt: SessionGeneration.Normalize(DateTime.UtcNow))
        {
            VerificationBinding = binding,
            RunnerStoreId = store.StoreId,
        };
    }

    private sealed class FakeContainment : IPtyCustodyContainment
    {
        public Guid ContainerId { get; } = Guid.NewGuid();
        public List<int> Pids { get; } = [];
        public int Terminations { get; private set; }
        public bool Missing { get; set; }
        public PtyTrackedLaunch Place(PtyTrackedLaunch launch) => launch;
        public PtyCustodyPopulation ReadActive()
        {
            if (Missing)
                throw new IOException("verification_custody_tree_unreadable");
            return new((uint)Pids.Count, Pids);
        }
        public PtyTerminationObservation Terminate()
        {
            Terminations++;
            Pids.Clear();
            return new(true, 0);
        }
    }

    private sealed class TrackingRefusal : IVerificationCustodyFiles
    {
        private readonly VerificationCustodyFiles _files = new();
        public int Failures { get; private set; }
        public Process? Child { get; private set; }
        public byte[]? Read(string path) => _files.Read(path);
        public void CommitImmutable(string path, byte[] bytes)
        {
            if (Path.GetFileName(path) == "tracking.json")
            {
                var root = JsonSerializer.Deserialize<CustodyRoot>(bytes,
                    new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
                Child = Process.GetProcessById(root.Pid);
                Failures++;
                throw new IOException("Injected tracking persistence refusal");
            }

            _files.CommitImmutable(path, bytes);
        }
    }
}
