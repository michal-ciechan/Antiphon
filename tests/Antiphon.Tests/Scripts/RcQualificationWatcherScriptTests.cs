using Antiphon.Tests.TestHelpers;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class RcQualificationWatcherScriptTests
{
    [Test]
    public Task Offline_watchers_and_trx_extraction() => ScriptHarness.RunHarnessCaseAsync(
        "test-orchestration-watchers.ps1", "RCQUAL", "Smoke", 26,
        "RCQUAL bounds the same message string and stdout",
        "RCQUAL rearm deduplicates completed TRX and retries partial",
        "RCQUAL task rearm emits new failed transition",
        "RCQUAL qualification reports Timeout as a failure",
        "RCQUAL qualification reports Aborted as a failure",
        "RCQUAL task rearm emits re-Blocked and suppresses unchanged repeat",
        "RCQUAL task rearm emits re-Failed and suppresses unchanged repeat",
        "RCQUAL master watcher reports transport failure");
}
