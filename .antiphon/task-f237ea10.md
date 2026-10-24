# Group 33b Debug+Fix

Fixed all six Linux-reproduced failures with fixture-only changes; the three changed classes pass 45/45. System-prompt launch and report-store failures did not reproduce at the assigned base. Native qualification remains the caller's rerun obligation.

## Source and changes

Base: `99bd60f861eab6ffdcb98d5a4a006719ff4d97bc`, clean assigned branch `feat/card-task-f237ea10`. Fetched master at start and finish: `dc1a2184526085b8f4305878a3804f9cec41daad`. No rebase, target merge, desktop access, native lock, release script, tag, Unit lane or Review. No sibling-owned classes changed.

Each class fix was committed and pushed separately:

- `02a439b6271e25a0cd30cadc76528a112dbd8e74`: `tests/Antiphon.Tests/Application/ExpectationDirectDeliveryTests.cs`. Rebind the fake's dialog observation after the deliberate session-generation change. Model an unreadable viewport only after a supported send, and restore readable composer evidence before the later send. Retains all refusal, hold, receipt, input-count and audit assertions.
- `1d1953cec2e37d80b3653c489f8d6b23dd234375`: `tests/Antiphon.Tests/Application/AgentTaskLandPreparationIdentityTests.cs`. Preserve the exact predecessor source-pin assertion during replacement verification. CARD-1432 intentionally collects superseded non-refused operation refs after terminal success; checking that old pin after collection was stale. Also assert the completed operation's source pin and that the predecessor check actually ran. All original identity, verification, cleanup, history and remote-source assertions remain.
- `3ef1855db73b721f451159e2f908d5ad421f53a6`: `tests/Antiphon.Tests/Application/LandRequestWriteDiagnosticTests.cs`. Seed the unrelated task under the current schema before rewinding. Insert the legacy request under the predecessor schema, migrate exactly the subject migration, and project only its available columns for every legacy-value assertion. Upgrade to current schema before current-model mutation/readback. The previous test failed inserting today's `AgentTasks.ReleasedSeatAnswer` property into the historical schema.

No production defect was demonstrated and no production code changed. The landing trace confirms retention behavior, rather than accidental loss of required live pins.

## Verification

All runs used the Linux runner mirror and `scripts/build-slot.ps1`, with compiled output `bin-f237ea10/` and `dotnet run --project tests/Antiphon.Tests --no-build`. Checkpoint inventory was bypassed as explicitly commissioned because of its 64 MiB DLL limit. Fresh TRX files confirm nonzero exact class counts and no unexpected classes.

| Class | Base passed/failed | After fix passed/failed |
|---|---:|---:|
| AgentSystemPromptLaunchTests | 28/0 | Unchanged; base evidence |
| AgentReportStoreTests | 28/0 | Unchanged; base evidence |
| ExpectationDirectDeliveryTests | 11/3 | 14/0 |
| AgentTaskLandPreparationIdentityTests | 14/2 | 16/0 |
| LandRequestWriteDiagnosticTests | 14/1 | 15/0 |

Baseline: 101 total, 95 passed, 6 failed, 0 skipped; 2m30.031s at the base. The exact landing-identity method was repeated unchanged at that same base with `ANTIPHON_C448_EVIDENCE`: 4 total, 2 passed, 2 failed, 0 skipped; 27.871s. It confirmed the source and verification-filter variants fail after terminal ref collection; target and target-checkout pass.

Final: 45 total, 45 passed, 0 failed/skipped; 2m18.291s at clean committed code tip `3ef1855db73b721f451159e2f908d5ad421f53a6`. Only the three changed classes were rerun. No assertions were weakened and no timeout/retry was widened or added.

Builds: baseline 8m38.89s; fixes 3m18.46s; both 0 errors, 1197 warnings. Slot wait was 0s for each run. Hard bounds: builds 600s each; baseline tests 600s; identity witness 180s; final tests 360s. None expired. These bounds were provisional collection limits, not measured p95 claims.

Baseline filter:

```text
/*/Antiphon.Tests.Application/(AgentSystemPromptLaunchTests*)|(ExpectationDirectDeliveryTests*)|(AgentTaskLandPreparationIdentityTests*)|(AgentReportStoreTests*)|(LandRequestWriteDiagnosticTests*)/*
```

Final filter:

```text
/*/Antiphon.Tests.Application/(ExpectationDirectDeliveryTests*)|(AgentTaskLandPreparationIdentityTests*)|(LandRequestWriteDiagnosticTests*)/*
```

Evidence root: `/work/worktrees/task-f237ea10/.antiphon/evidence-f237ea10/`. Retained baseline/final TRX, logs, per-case results, phase JSONL, identity Git/committed-state trace, DLL digests and exact source SHAs. Generated evidence remains ignored. Baseline DLL SHA-256: `4bc23462b83e4178a76101b0dec94ef43cd0ef47e813c9e2d84588fbbc2a3fea`; final: `10d30d717cabf4e80a028b9a5c675f00f4b89c98e6b055278dd7b9407f625b0c`. Phase capture records shared DB create/migrate at 4.846/34.388s baseline and 6.841/48.146s final; absent spans remain unknown/remainder. Recorder health reports no failures or dropped phases.

## Native rerun rows

Schedule these on the native qualification owner's lane after landing/building the resulting candidate, through the build-slot gate and `--no-build`; do not compete with the existing qualification supervisor or take its native lock from this task.

| Native filter | Expected cases |
|---|---:|
| `/*/Antiphon.Tests.Application/AgentSystemPromptLaunchTests/*` | 28 |
| `/*/Antiphon.Tests.Application/ExpectationDirectDeliveryTests/*` | 14 |
| `/*/Antiphon.Tests.Application/AgentTaskLandPreparationIdentityTests/*` | 16 |
| `/*/Antiphon.Tests.Application/AgentReportStoreTests/*` | 28 |
| `/*/Antiphon.Tests.Application/LandRequestWriteDiagnosticTests/*` | 15 |

The launch suite already contains the adapter-binding repair `99f73d3d00bdf0198002aa9269e7dd8bc2348ba4`, which landed between native candidate `a97194ee7a4a` and this base. Its 28 Linux cases, including forced CRLF and Windows drive-path input in the phone variants, pass. The two report-store failures (existing-ignore-rule and atomic publication) do not reproduce on Linux; their Windows cause remains unproven. No Windows-semantics cluster was established, so no speculative platform fix or new string witness was invented.

`git merge-tree --write-tree origin/master HEAD` at the code tip succeeded against fetched master, producing tree `2832c3a45b8852b6de88d64ef19cb36f338bc53e`. Evidence history check over base..code-tip: 3 commits, 0 violations. All 33 inventoried producer-owned `bin-f237ea10` directories were removed after completed runs; no children remain owned by this task.

--- next stage ---
next: land
handoff: Land the three fixture fixes under the operator's no-Review instruction; schedule the five native rows above through the qualification owner. Linux evidence does not qualify Windows behavior.
artifact: /work/worktrees/task-f237ea10/.antiphon/task-f237ea10.md
