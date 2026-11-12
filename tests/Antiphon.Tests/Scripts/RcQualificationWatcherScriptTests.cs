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
    // lost supervisor with a live nightly-run as supervisor-lost-run-alive, not a failure, and
    // credits that only to the launch's own run id (Review b26811ff D-1), from the TaskRoot copy
    // once the shared last-run.json is superseded, else unknown/state-superseded (Review 9f29e134 D-1);
    // last-run.json readers allow FileShare.Delete for nightly-run's File.Replace (Review 230d1f5d A-1).
    [Test]
    public Task Supervisor_detaches_and_observe_classifies_lost_supervisor() => ScriptHarness.RunHarnessCaseAsync(
        "test-qualification-supervisor.ps1", "C1667", "Smoke", 39,
        "C1667 observe classifies a gone supervisor with live nightly-run as supervisor-lost-run-alive",
        "C1667 observe treats a stale heartbeat as supervisor lost while the run is alive",
        "C1667 observe treats last-run completedAt as the end signal without commission-exit",
        "C1667 observe reports run-lost when nightly-run is gone without completedAt",
        "C1667 observe probes the run owner before reading last-run.json",
        "C1667 observe does not credit a previous run completedAt to a refused launch (commission-exit 2)",
        "C1667 observe does not report supervisor-lost-run-alive for another run holding run.lock",
        "C1667 observe needs the run.lock run id as well as the last-run.json run id before crediting supervisor-lost-run-alive",
        "C1667 observe reports not-started while the launch waits for a build slot (no Started yet)",
        "C1667 observe reports a completed run superseded by a later run without a copy as unknown, not launch-failed",
        "C1667 observe reports a lost supervisor whose run was superseded as unknown, not not-started",
        "C1667 observe credits completed from the TaskRoot copy of our last-run.json after it is superseded",
        "C1667 supervise-rc copies last-run.json for its run id while polling and keeps it after the next run writes",
        "C1667 supervise-rc copies last-run.json for its run id at child exit",
        "C1667 observe credits neither completion nor supersession to a TaskRoot copy naming another run id",
        "C1667 start-supervisor refuses a run id the TaskRoot copy own-last-run.json already records",
        "C1667 last-run.json readers open with FileShare.Delete so the nightly-run File.Replace is never blocked",
        "C1667 launch-rc refuses a launch whose -RunId is missing or differs from the bound run id",
        "C1667 launch-rc refuses a checkout whose working files break their eol attribute before build-slot",
        "C1667 supervisor log lines are on disk at return and the file is not held open",
        "C1667 supervisor runs in its own session or console, not the wrapper's",
        "C1667 supervisor survives teardown of the starting group and records the launch exit code",
        "C1667 supervisor binds the launch run id in its identity, launch-child record and the launch environment");
}
