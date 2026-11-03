using Antiphon.Tests.TestHelpers;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class RcQualificationWatcherScriptTests
{
    [Test]
    public Task Offline_watchers_and_trx_extraction() => ScriptHarness.RunHarnessCaseAsync(
        "test-orchestration-watchers.ps1", "RCQUAL", "Smoke", 39,
        "RCQUAL bounds the same message string and stdout",
        "RCQUAL rearm deduplicates completed TRX and retries partial",
        "RCQUAL task rearm emits new failed transition",
        "RCQUAL qualification reports Timeout as a failure",
        "RCQUAL qualification reports Aborted as a failure",
        "RCQUAL task rearm emits re-Blocked and suppresses unchanged repeat",
        "RCQUAL task rearm emits re-Failed and suppresses unchanged repeat",
        "RCQUAL master watcher reports transport failure",
        "RCQUAL helpers resolve PSScriptRoot outside param defaults for 5.1 -File");

    // CARD-1667: the supervisor outlives its starter console/session and observe classifies a
    // lost supervisor with a live nightly-run as supervisor-lost-run-alive, not a failure.
    [Test]
    public Task Supervisor_detaches_and_observe_classifies_lost_supervisor() => ScriptHarness.RunHarnessCaseAsync(
        "test-qualification-supervisor.ps1", "C1667", "Smoke", 18,
        "C1667 observe classifies a gone supervisor with live nightly-run as supervisor-lost-run-alive",
        "C1667 observe treats a stale heartbeat as supervisor lost while the run is alive",
        "C1667 observe treats last-run completedAt as the end signal without commission-exit",
        "C1667 observe reports run-lost when nightly-run is gone without completedAt",
        "C1667 observe probes the run owner before reading last-run.json",
        "C1667 supervisor log lines are on disk at return and the file is not held open",
        "C1667 supervisor runs in its own session or console, not the wrapper's",
        "C1667 supervisor survives teardown of the starting group and records the launch exit code");
}
