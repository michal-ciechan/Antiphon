# CARD-0449: probe diagnostics and floor-cancellation cleanup

Both requested defects are fixed. Final scoped regression: 86 passed, 0 failed,
0 skipped. Ready for Review. No live Claude launch, deployment or restart was performed.

## Changes

- `server/Infrastructure/Agents/SessionRunner/RunnerClaudeAdapter.cs`: restores the
  floor-delay `OperationCanceledException` guard returning false. ProbeFailed now
  logs the session-derived token, elapsed post-floor startup seconds, successful
  token-write count, failure detail and full rendered `Screen`.
- `server/Infrastructure/Agents/Pty/ClaudeAdapter.cs`: restores token, elapsed
  seconds, writes and failure detail on ProbeFailed. EffortFailed retains its
  existing reason-only diagnostics in both adapters; no effort screen dump was added.
- `src/Antiphon.Agents.Pty/ClaudeStartupReadiness.cs`: counts successful token
  writes immediately. Previously the coordinator copied the count only after a
  probe returned, so its shared deadline could cancel the probe and report zero
  despite actual writes. Cumulative limits remain enforced across interruptions.
- `server/Application/Services/AgentSessionService.cs`: skips the optional context
  fullness lookup when readiness has failed and the launch token is already
  canceled. This extra guard is necessary: the integration test remained red after
  restoring the adapter catch because the diagnostic query threw another OCE,
  again bypassing the caller's non-OCE cleanup catch. An uncanceled readiness
  failure still reports context fullness through the existing path.
- Three regression cases added to `RunnerClaudeAdapterEffortPromptTests`,
  `ClaudeAdapterEffortPromptTests` and `AgentSessionServiceIntegrationTests`.
  Diagnostic assertions inspect structured log fields and rendered text. The local
  test uses a real test-owned console peer, not Claude. Floor cancellation uses the
  actual runner adapter and session service over a fake runner client, asserting
  one kill with an uncanceled token, observed exit, Failed/SystemRequest state,
  and zero probe or boot input before test-finally cleanup.

V-21 and per-cohort dialog-copy concerns remain unchanged under CARD-0453.

## Commits and positive controls

Work began on requested `master` at `cccf64e7`. Tests were checkpointed in
`88581160` and `55a5a4b0`; production corrections are `6e722b4e` and `7ff6a0da`.
The initial test checkpoint needed a scratch-directory fixture compilation fix;
that was corrected before collecting baseline test evidence.

| Exact method | Baseline red | Fixed green |
|---|---|---|
| RunnerClaudeAdapterEffortPromptTests.Probe_failure_logs_token_elapsed_writes_and_screen | 1 failed: missing Token log field | 1 passed |
| ClaudeAdapterEffortPromptTests.Local_probe_failure_logs_token_elapsed_and_writes | 1 failed: missing Token log field | 1 passed |
| AgentSessionServiceIntegrationTests.Cancellation_during_claude_minimum_floor_kills_the_started_runner_session | 1 failed: TaskCanceledException instead of ordinary readiness failure | 1 passed |

Each red and green used only its exact method through `--treenode-filter`.
An additional floor run after the adapter-only correction still failed with OCE;
`7ff6a0da` fixes the optional diagnostic bypass it exposed. No temporary mutations
remain. The diagnostic green runs exercised `6e722b4e`; the final regression and
floor green exercised `7ff6a0da`.

## Final verification

| Class / scope | Executed | Passed | Failed |
|---|---:|---:|---:|
| RunnerClaudeAdapterEffortPromptTests | 10 | 10 | 0 |
| ClaudeAdapterEffortPromptTests | 4 | 4 | 0 |
| RunnerClaudeAdapterTrustPromptTests | 8 | 8 | 0 |
| AgentSessionLaunchFailureTests | 48 | 48 | 0 |
| ClaudeStartupReadinessTests (PTY project) | 15 | 15 | 0 |
| New floor-cancellation method (session integration class) | 1 | 1 | 0 |

The four-class adapter/launch run took 1m12s; the shared readiness run took 6s;
floor green took 23s including fixture startup. Both projects built successfully
and were run sequentially. Fresh TRX counters and executed class identities were
verified. `git diff --check` passed. No full suite or live-provider acceptance was run.

Logs and eight TRX files are preserved at
`C:\src\Antiphon\.antiphon\verification-5b1b77ec\`.
The intermediate floor failure is also retained as `floor-diagnostic-red.log`.
Automatic approval review rejected temporary-output removal as "blocked by policy"
without a more specific reason; the 15 `bin-c449-5b1b77ec` directories remain.

Rerun from `C:\src\Antiphon`, sequentially:

```powershell
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-c449-5b1b77ec/ -- --treenode-filter '/*/*/(RunnerClaudeAdapterEffortPromptTests*)|(ClaudeAdapterEffortPromptTests*)|(RunnerClaudeAdapterTrustPromptTests*)|(AgentSessionLaunchFailureTests*)/*' --report-trx --report-trx-filename adapter-regression.trx
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-c449-5b1b77ec/ -- --treenode-filter '/*/*/AgentSessionServiceIntegrationTests/Cancellation_during_claude_minimum_floor_kills_the_started_runner_session' --report-trx --report-trx-filename floor-green.trx
dotnet run --project tests/Antiphon.Agents.Pty.Tests --property:OutputPath=bin-c449-5b1b77ec/ -- --treenode-filter '/*/*/ClaudeStartupReadinessTests/*' --report-trx --report-trx-filename readiness-regression.trx
```

Review should include the optional context-diagnostic guard: restoring only the
adapter's floor catch was demonstrably insufficient to prevent the leak on this
HEAD. Review the probe write accounting together with the restored log fields.
