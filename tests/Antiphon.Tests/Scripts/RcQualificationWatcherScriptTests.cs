using Antiphon.Tests.Application;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class RcQualificationWatcherScriptTests
{
    [Test]
    public Task Offline_watchers_and_trx_extraction() => ScriptHarness.RunHarnessCaseAsync(
        "test-orchestration-watchers.ps1", "RCQUAL", "Smoke", 18,
        "RCQUAL bounds the same message string and stdout",
        "RCQUAL rearm deduplicates completed TRX and retries partial",
        "RCQUAL task rearm emits new failed transition",
        "RCQUAL master watcher reports transport failure");
}
