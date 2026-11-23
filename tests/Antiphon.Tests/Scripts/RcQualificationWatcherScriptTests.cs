using Antiphon.Tests.TestHelpers;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class RcQualificationWatcherScriptTests
{
    // The PowerShell helpers (watch-tasks, watch-master, trx-failures). watch-qual is the antiphon-scripts command since
    // 2026-11-23; its eleven rows are in-process in WatchQualTests.
    [Test]
    public Task Offline_watchers_and_trx_extraction() => ScriptHarness.RunHarnessCaseAsync(
        "test-orchestration-watchers.ps1", "RCQUAL", "Smoke", 28,
        "RCQUAL bounds the same message string and stdout",
        "RCQUAL task rearm emits new failed transition",
        "RCQUAL extractor reports Timeout as a failure",
        "RCQUAL extractor reports Aborted as a failure",
        "RCQUAL task rearm emits re-Blocked and suppresses unchanged repeat",
        "RCQUAL task rearm emits re-Failed and suppresses unchanged repeat",
        "RCQUAL master watcher reports transport failure",
        "RCQUAL helpers resolve PSScriptRoot outside param defaults for 5.1 -File");

    // CARD-1667's supervisor set (test-qualification-supervisor.ps1, C1667) is in process since unit 7 of
    // 2026-11-22-remaining-ps1-to-csharp.md: QualificationSupervisorTests and QualificationStopTests.
}
