using System.Text.Json;
using Antiphon.PtyHost.Protocol;
using Antiphon.SessionRunner.Contracts;
using Shouldly;
using TUnit.Core;

namespace Antiphon.E2E.Fixtures;

public class IsolatedSessionRunnerTeardownTests
{
    [Test]
    public async Task Snapshot_then_kill_all_then_census_preserves_every_listed_host()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var order = new List<string>();
        var runner = new FakeRunner(
        [
            Session(first, hostPid: 701),
            Session(second, hostPid: null)
        ], order);
        IReadOnlyList<SessionHostSnapshot>? censused = null;

        await IsolatedSessionRunnerTeardown.SnapshotKillAllThenCensusAsync(
            runner,
            _ =>
            {
                order.Add("snapshot");
                return Task.CompletedTask;
            },
            hosts =>
            {
                order.Add("census");
                censused = hosts;
                return Task.CompletedTask;
            },
            _ => throw new InvalidOperationException("kill-all should succeed"),
            CancellationToken.None);

        order.ShouldBe(["list", "snapshot", "kill-all", "census"]);
        censused.ShouldBe([new SessionHostSnapshot(first, 701)]);
    }

    [Test]
    public async Task Crashed_run_sweep_skips_concurrent_runner_and_reaps_only_dead_run_hosts()
    {
        var root = Path.Combine(Path.GetTempPath(), $"antiphon-e2e-sweep-{Guid.NewGuid():N}");
        var concurrent = Path.Combine(root, "run-concurrent");
        var crashed = Path.Combine(root, "run-crashed");
        var now = DateTime.UtcNow;
        var processes = new FakeProcessInspector
        {
            [101] = new ProcessIdentity("Antiphon.SessionRunner", now),
            [201] = new ProcessIdentity("not-the-runner", now),
            [301] = new ProcessIdentity("Antiphon.PtyHost", now),
            [302] = new ProcessIdentity("Antiphon.PtyHost", now)
        };

        try
        {
            await WriteRunnerMarkerAsync(concurrent, 101, now);
            WriteManifest(concurrent, 301, now);
            await WriteRunnerMarkerAsync(crashed, 201, now);
            WriteManifest(crashed, 302, now);

            await IsolatedSessionRunner.SweepCrashedRunsAsync(root, processes);

            Directory.Exists(concurrent).ShouldBeTrue();
            Directory.Exists(crashed).ShouldBeFalse();
            processes.KilledPids.ShouldBe([302]);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    private static RunnerSessionDto Session(Guid sessionId, int? hostPid) =>
        new(sessionId, null, DateTime.UtcNow, "Running", null, "", 0, hostPid);

    private static async Task WriteRunnerMarkerAsync(string runDirectory, int pid, DateTime startedAt)
    {
        Directory.CreateDirectory(runDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(runDirectory, "runner.json"),
            JsonSerializer.Serialize(new { Pid = pid, ProcessStartTimeUtc = startedAt }));
    }

    private static void WriteManifest(string runDirectory, int hostPid, DateTime startedAt)
    {
        var manifestPath = Path.Combine(
            runDirectory,
            "logs",
            "pty-hosts",
            "manifests",
            $"{Guid.NewGuid():N}.json");
        new PtyHostManifest
        {
            SessionId = Guid.NewGuid(),
            PipeName = "test-pipe",
            HostPid = hostPid,
            HostStartTimeUtc = startedAt,
            Cols = 80,
            Rows = 24,
            CreatedAtUtc = startedAt
        }.SaveAtomic(manifestPath);
    }

    private sealed class FakeRunner(IReadOnlyList<RunnerSessionDto> sessions, List<string> order) : IIsolatedSessionRunnerClient
    {
        private readonly IReadOnlyList<RunnerSessionDto> _sessions = sessions;
        private readonly List<string> _order = order;

        public Task<IReadOnlyList<RunnerSessionDto>> ListSessionsAsync(CancellationToken cancellationToken)
        {
            _order.Add("list");
            return Task.FromResult(_sessions);
        }

        public Task KillAllSessionsAsync(CancellationToken cancellationToken)
        {
            _order.Add("kill-all");
            return Task.CompletedTask;
        }
    }

    private sealed class FakeProcessInspector : IProcessInspector
    {
        private readonly Dictionary<int, ProcessIdentity> _processes = [];

        public List<int> KilledPids { get; } = [];

        public ProcessIdentity this[int pid]
        {
            set => _processes[pid] = value;
        }

        public bool TryGet(int pid, out ProcessIdentity process) => _processes.TryGetValue(pid, out process);

        public void KillTree(int pid) => KilledPids.Add(pid);
    }
}
