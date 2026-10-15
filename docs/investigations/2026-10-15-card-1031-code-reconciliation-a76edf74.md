# CARD-1031 Code reconciliation

Task `a76edf74-df28-4171-b930-c88a30b3e119`, 2026-10-15.
Worktree: `/work/worktrees/task-a76edf74`.
Branch: `feat/card-task-a76edf74`.
Immutable task base: `8ffe3633357e6383b7e408bd078465acf9f5fd79`.
Plan: `docs/superpowers/plans/2026-10-04-card-1031-codex-version-stderr-plan.md`.

## Outcome

No unfinished implementation slice was found. The original Code fix, ordinary
Review, native Windows qualification and publication already completed. This
task records those previously separate results; it changes no production code,
tests, executable plan selections, or policy. Remaining positive controls belong
to separately commissioned SourceLanding Mutation, expressly excluded by this
Code brief. This note does not grant landing or claim a new Clean/Full Review.

## Retrieved completion evidence

Read the full card and its ten revisions with `scripts/card.ps1 get/history
CARD-1031 -Board Antiphon`. Read the active plan and Code evidence, and retrieved
the complete displayed results with `scripts/delegate.ps1 -Status` for the
original Code, Review and Windows Test tasks. These are prior task reports;
their raw TRX files were not available in this assigned worktree and were not
independently revalidated here.

| Event | Recorded result |
|---|---|
| Original Code and landing owner | `b491b9c4-206a-4acb-8247-145ac9190644`; final source `b2f2107fbcc04eaf5e5174836725c0a143daeb2d` |
| Ordinary Review | `37ad6b6c-85b1-484f-bfea-cee5e67c8f50`; Clean at that exact source; evidence `d1efce77-5c41-41e6-bdb0-d5d0e550be8c`; its report explicitly conditioned land on Windows CP-4 |
| Subsequent native Windows Test | Task prefix `66704481`; CP-4 at the same source, 12 passed, 0 failed, 0 skipped; receipt validation reported passed |
| Confirmed publication | Original owner's operation `82f29ac1-799b-444a-982e-5dc528c13695`; Landed, remote/verified SHA `1ac1b61dbffddcd90992eb028a8cd893934de48d`, confirmed 2026-10-04 10:55:13 UTC; cleanup Complete |
| Assigned base containment | `git merge-base --is-ancestor 1ac1b61dbffddcd90992eb028a8cd893934de48d HEAD` exited 0 before this note was created |

The Windows task's unedited checkpoint line is retained below. Its paths refer
to that historical Windows task, not this Linux checkout:

```text
CHECKPOINT CP-4 commit=b2f2107fbcc04eaf5e5174836725c0a143daeb2d build=ok filter=/*/*/(CodexCliVersionProbeTests*)|(CodexCliVersionWindowsTests*)/* executed=12 passed=12 failed=0 skipped=0 trx=C:\Antiphon\worktrees\card-task-66704481\.antiphon\checkpoints\20261004-114828-f14e\rows\CP-4\run.trx slot=granted waited=0s dirty=0 source=b2f2107fbcc04eaf5e5174836725c0a143daeb2d sourceState=clean buildSource=verified
```

The Windows report retained an owned checkpoint bootstrap directory after an
automatic approval rejection of deletion. That historical residue is not this
task's output or authority to remove anything from the Windows checkout.

## Verification ledger

All outcomes below are historical task-reported results, not fresh executions
at `8ffe3633357e6383b7e408bd078465acf9f5fd79` or at this note's commit.

| ID | Actual reported outcome |
|---|---|
| CP-1 | 8 passed, 0 failed, 0 skipped; final Code and ordinary Review reported clean source and verified build provenance at `b2f2107fbcc04eaf5e5174836725c0a143daeb2d` |
| CP-2 | 6 passed, 0 failed, 0 skipped, same final source and reported provenance |
| CP-3 | 4 passed, 0 failed, 0 skipped, same final source and reported provenance |
| CP-4 | 12 passed, 0 failed, 0 skipped, native Windows at the same final source; line above |
| V-1 | Passed via CP-1 and native CP-4: first whole stdout banner and retained strict version parsing |
| V-2 | Passed via CP-1 and CP-4: stderr advisory, privacy and completion time |
| V-3 | Passed via CP-1 and CP-4: bounded complete evidence and draining |
| V-4 | Passed via CP-1 and CP-4: failed process/output remains unknown |
| V-5 | Passed subsequently via native CP-4: sealed owned Windows launch layouts |
| V-6 | Passed via CP-2: advisory-aware projection and freshness |
| V-7 | Passed via CP-1/CP-2/CP-4: observation receipts and compatibility |
| R-1 | Passed via CP-3: all four never-refuses methods |
| R-2 | Passed via CP-1 and CP-4: isolation, cache, refresh and completion |
| R-3 | Passed via CP-2: bound observation transport |

The original Code additionally reported all four adjacent methods in the full
CodexCliObservationTests class passing. Its Unit run at
`0523b2608f393cbdb114a67232d3fa77f6a3dac3` had 4,103 total, 4,051 executed,
4,036 passed, 15 failed and 52 skipped. The same 15 failures reproduced at its
immutable base; they were not repaired or reclassified as passes. See
`docs/investigations/2026-10-04-card-1031-code-b491b9c4.md` for the exact historical
lines, costs, rejected baseline attempt and corrected provenance. The original
notice regression failed before the fix at `C1031-notice-version` (1 execution,
0 passed, 1 failed). No new test or deliberate mutant was introduced here.

No build, checkpoint, Unit lane, integration run, repetition, installed CLI
command, live probe POST, activation or restart ran in this reconciliation.
Current-run counts: zero; build-slot `slot=not-run`, `waited=not-applicable`.
No new source was submitted for test qualification. Existing receipts are not
relabelled as current-source certificates. D-2 does not require a Review rerun
solely for this advisory evidence correction; any incomplete verification gate
remains incomplete. No new Final/Full gate is asserted by this task.

## Source and live observation checks

The probe, parser, sample contract, refresh service, projection, portable probe
tests, native Windows tests and owned fixture files match the reviewed original
source byte-for-byte. The current probe parses stdout independently of stderr,
discards truncated terminal fragments, retains nonzero-exit precedence and emits
only fixed advisory tokens. The projection accepts only the two version-bearing
advisories while retaining age, skew and fingerprint checks.

Two integration test files have subsequently changed:
`RunnerCodexCliEvidenceTests.cs` and `CodexCliObservationTests.cs`. The historical
receipts are not proof of those later revisions. No repair to them is part of
this task. Identifier search confirmed the three C1031 portable methods, the
Windows C1031 method, the C1031 projection method and all four never-refuses
methods remain present. The requested search for guards affected by changed
implementation files has an empty input set: this task changes only this
narrative evidence note, so there is no additional affected-class test row.

Read `GET /api/runner-defaults` and `GET /api/session-runners` without placement
overrides. Defaults revision 2; both Linux and Windows lanes were eligible.
At 2026-10-15 16:24:52 UTC the desktop catalogue showed version `0.160.0`,
diagnostic `stderr_output`, checked-at `2026-10-15T16:23:11.8203575+00:00` and
stale false. This is direct evidence that the formerly failing observation shape
is now served, not proof of the precise discarded warning text. The Linux
catalogue also retained version plus advisory, but stale true; that separate
freshness observation does not establish a regression in stderr parsing.

## Remaining ownership

PC-1 through PC-45 are all pending SourceLanding Mutation, including native
Windows PC-36 and PC-37 and every variant named by the plan. No prior Review
scratch control is credited as a SourceLanding PC. CARD-1029's separate pending
controls and deferred variants remain with CARD-1029.

Caller should reconcile the existing Review, Windows result and confirmed land
before commissioning Mutation against original operation
`82f29ac1-799b-444a-982e-5dc528c13695`. Do not land the original Code task again
on account of this stale Code commission. This evidence-only branch neither
replaces that owner nor authorizes mutation custody.

Restart: none. This task adds no executable behavior and the observed desktop
already exposes the corrected version/advisory shape.
