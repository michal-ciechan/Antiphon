C680's normal submission path is repaired: three consecutive native Windows runs passed on Claude 2.1.312 with default Haiku, and all 93 shared-fixture regression cases passed.

Task: `29984c60`, branch `feat/card-task-29984c60`.
Supplied base: `6799d7bd13390083ed265b9be53a387697eb4e99`.
Repair and tested source: `00dc13544ffb1abfb6239fe7b86279cb7fa74298`.

The inherited [investigation](2026-10-30-c680-askuserquestion-timeout.md) retains its two original setup failures and two diagnostic controls. Those outcomes are unchanged. This task uses that established red witness; it did not spend another live run reproducing the known unsubmitted prompt.

The only executable-source changes are in `tests/Antiphon.Tests/Agents/ClaudeDialogWatchdogCanaryTests.cs` and `ClaudeDialogCanaryDiagnostics.cs`. The normal fixture now LF-normalizes and brackets even these single-line prompts, observes two complete settled drafts 500 ms apart, sends one separate Enter, and waits for a complete matching transcript `UserPrompt` above the captured sequence floor. A missing receipt fails with a submission-precondition message before any watchdog assertion. An incomplete draft fails without sending Enter. A later missing dialog/tool call has its own message. There is no automatic resubmission.

The diagnostic-only submission and post-timeout dismissal routes were removed. Optional diagnostics now only record observations. The fixture retains the investigation's narrow recognition of the exact 2.1.312 manual-mode footer when inspecting its own draft. Product composer recognition, dialog admission, the watchdog, prompts, models, policies and exclusions are unchanged. Every existing refusal, zero-byte, unchanged-revision, stable-blocked-screen, absent-tool-result, absent-nudge-receipt, absent-permission-marker and unstarted-attempt assertion remains. The two-minute dialog wait and six-minute overall deadline remain. Draft settling is bounded at ten seconds; the new receipt phase is bounded at thirty seconds. These phase bounds are provisional, not measured p95 claims.

Native verification used one isolated `bin-c680fix299/` build and four sequential test invocations through `scripts/build-slot.ps1`. This is the brief's narrow selection, not a Full Windows qualification. All five drivers acquired leases with zero queue wait. The build passed with 0 errors and 1,380 warnings (including analyzer warnings); command wall time was 509.43 seconds, MSBuild reported 503.50 seconds. No other build or test driver ran.

| Run | Passed / failed / skipped | Test-body duration | Driver wall time |
|---|---|---:|---:|
| C680 live 1 | 1 / 0 / 0 | 59.492 s | 106.239 s |
| C680 live 2 | 1 / 0 / 0 | 28.136 s | 53.084 s |
| C680 live 3 | 1 / 0 / 0 | 24.934 s | 43.867 s |
| Shared-fixture regression batch | 93 / 0 / 0 | 128.119 s runner duration | 133.172 s |

All three native invocations set only the normal live opt-in, `ANTIPHON_HEADED_TESTS=1`, with `ANTIPHON_C680_DEBUG_ROOT`, `ANTIPHON_C680_DEBUG_MODEL` and `ANTIPHON_C680_DEBUG_SUBMIT` cleared. They therefore exercised the default Haiku path without diagnostics. Each fresh TRX has both complete-UserPrompt submission receipts and these unchanged safety receipts:

```text
C680 native cli=2.1.312 backend=ModernConPty case=Bash inputRevision=2 backendDelta=0 refused=true
C680 native cli=2.1.312 backend=ModernConPty case=AskUserQuestion inputRevision=2 backendDelta=0 refused=true
```

The eight regression classes include every consumer class of `ExpectationDeliveryFixture`, plus the direct-runner cleanup class. The canary-specific diagnostics helper has no other consumers. Fresh TRX class attribution was checked, not inferred from exit zero.

| Class | Passed |
|---|---:|
| ClaudeDialogDeliveryTests | 2 |
| ExpectationDirectDeliveryTests | 14 |
| ExpectationReceiptTests | 6 |
| ExpectationResponseTests | 10 |
| ExpectationWatchdogScenarioTests | 4 |
| ExpectationHoldReleaseEndpointTests | 4 |
| ProjectCascadeForeignKeyTests | 47 |
| DirectSessionRunnerClientDisposalTests | 6 |

The built test assembly SHA-256 was `15AE13A1CD5A25BDEE53EBFF74DDE2F82F49A5DAD72D8881995ED5EB1EC66F1E`. Each run checked that hash and source SHA against the build receipt, and recorded clean tracked source before and after execution. The pinned CLI copy SHA-256 was `6099672EBE5DCB518B67D1D32DDE1E3360BC6BEE1DAE7F8D28A69C49ACC7C865`; `DISABLE_AUTOUPDATER=1` was process-local. CLI receipts independently identify 2.1.312 and ModernConPty.

Retained ignored evidence is at `C:\Antiphon\worktrees\card-task-29984c60\.antiphon\c680-fix29984c60`: `build.json`, build/run logs, fresh `live1`/`live2`/`live3`/`siblings` TRX and metadata, timing shards, class and cleanup summaries, hashes, and exact local driver scripts. Uninstrumented phases remain unknown; driver time includes discovery/setup overhead and is not test-body time. The small three-run sample is not a stable p95. No selection approached thirty minutes. This task changes no fixture lifetime: the existing shared database lifecycle and per-test isolation remain in use.

All 19 captured native child identities have matching observed-exit records. All 34 task-created alternate output directories and the task's copied CLI were removed after the drivers completed, using a validated explicit literal-path list. No operator process was killed. No AppHost/runner restart, deployment or modification to the canonical checkout was performed. Diagnostic transcript capture was disabled; no transcripts or credentials were printed or committed. The local text-evidence scan found zero secret-pattern matches.

C680 remains a required opt-in obligation; no policy row or exclusion was changed. The operator decides whether the next Windows Full run supplies the opt-in. It requires native Windows with staged modern ConPTY files, Node and Claude on PATH, working Claude authentication and live provider quota/billing, and the normal database-test prerequisites. Reproduce this qualification with a task-owned copy of the cached 2.1.312 executable first on PATH and updates disabled. This task spent exactly three live runs out of the five-run cap: six submitted probe prompts, with no Sonnet or Opus turns. Each run incurs provider usage for two dialog scenarios. Exact currency cost is not exposed by these receipts and depends on account billing; it must not be reported as free or zero. Observed driver time was about 44-106 seconds per run, plus the one 8.5-minute build. The existing six-minute test deadline remains the failure bound.

For a new owned output and fresh results directory, after arranging the pinned CLI and authentication:

```powershell
pwsh -NoProfile -File scripts/build-slot.ps1 -Label c680-review-build -- dotnet build tests/Antiphon.Tests --property:OutputPath=bin-c680review/ --nologo --verbosity quiet
$env:ANTIPHON_HEADED_TESTS = '1'
$env:DISABLE_AUTOUPDATER = '1'
$env:ANTIPHON_C680_DEBUG_ROOT = $null
$env:ANTIPHON_C680_DEBUG_MODEL = $null
$env:ANTIPHON_C680_DEBUG_SUBMIT = $null
pwsh -NoProfile -File scripts/build-slot.ps1 -Label c680-review-native -- dotnet run --project tests/Antiphon.Tests --no-build --property:OutputPath=bin-c680review/ -- --treenode-filter '/*/*/ClaudeDialogWatchdogCanaryTests/C680_LiveNudgeRefusesPermissionAndQuestionDialogs' --report-trx --report-trx-filename run.trx --results-directory '<fresh absolute result directory>'
```

The regression filter, with headed opt-in cleared, was:

```text
/*/*/(ClaudeDialogDeliveryTests*)|(ExpectationDirectDeliveryTests*)|(ExpectationReceiptTests*)|(ExpectationResponseTests*)|(ExpectationWatchdogScenarioTests*)|(ExpectationHoldReleaseEndpointTests*)|(ProjectCascadeForeignKeyTests*)|(DirectSessionRunnerClientDisposalTests*)/*
```

`scripts/check-evidence-diff.ps1` reported zero violations over the repair range. `git fetch origin master` observed `98fc3ceb6440a17b68cafe3ecc21c76d190b18ca`. `git merge-tree --write-tree origin/master HEAD` is not clean: the same four conflicts occur against the supplied base, establishing that they are inherited:

- `.antiphon/task-af2b78c8.md`
- `docs/investigations/2026-10-25-card-1584-e2e-launch-bounds.md`
- `server/Application/Services/CapacityRecoveryService.cs`
- `tests/Antiphon.Tests/Application/CapacityLockOrderTests.cs`

No merge or conflict repair was attempted. The caller must account for the supplied branch's lineage when arranging landing; this report does not claim master merge readiness. The native authority in the brief covered all work, so no further approval was requested.

--- next stage ---
next: review
handoff: Review the test-only submission repair at 00dc13544ffb1abfb6239fe7b86279cb7fa74298 and the 3 native plus 93 regression passes. Preserve C680 as required opt-in. The operator owns the Windows Full opt-in decision; the supplied base has four inherited master merge conflicts.
artifact: C:\Antiphon\worktrees\card-task-29984c60\docs\investigations\2026-10-30-c680-submission-repair.md
