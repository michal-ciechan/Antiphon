# CARD-0987 S1a: incompatible frozen acceptance and incomplete I/O footprint

Code task / original landing owner: `f9310100-49c5-4a6e-abb2-160cab0491e6`.
Worktree: `/work/worktrees/task-f9310100`.
Branch: `feat/card-task-f9310100`.
Inspected source / task base: `8ffe3633357e6383b7e408bd078465acf9f5fd79`.
Date: 2026-10-15. Product implementation has **not started**; ordinary verification is incomplete.

## Outcome

The next planned slice is S1a. It cannot satisfy both its frozen compatibility oracles and
the later, reviewed cleanup repair. Its listed I/O footprint also predates the guarded
creation/deletion/receipt graph now reached by those tests. This is a substantive acceptance
and design conflict, not an advisory evidence correction. No product files, tests, checkpoint
floors, assertions, timeouts, activation settings or running services were changed.

The caller was asked to choose a refreshed Plan/TestDesign first (recommended), or an expanded
Code scope and budget that includes the changed custody graph. This report supplies the concrete
differences for that choice. No answer was inferred from elapsed time.

## Source and prerequisite checks

- Read CARD-0987 and its complete revision history through `scripts/card.ps1`.
- The card description links the original CARD-0826 plan; the later Plan and TestDesign
  results name `docs/superpowers/plans/2026-10-12-card-0987-remaining-slices-plan.md` and
  `docs/superpowers/plans/2026-10-12-card-0987-test-design.md`.
- Those two files are absent from both the assigned base and the fetched `origin/master`.
  The latest Plan branch was fetched for read-only inspection:
  `feat/card-task-04839ba2` at `17169b9d47f120b2cb7dcd6172d2c2cf0cfcd632`.
  Both documents were inspected from that exact Git object. They were not merged or
  copied into this task branch. Their absence alone is not the behavioral blocker below.
- CARD-1199 is Done. Its recorded reviewed landing is
  `e0877982c5dc41990c4e66b80cb47341a1bd0a41`, last Review `dca33d6b`.
  `git merge-base --is-ancestor e0877982c5dc41990c4e66b80cb47341a1bd0a41 HEAD`
  returned exit 0. Thus the explicit prerequisite landing is present; this is not an
  outstanding CARD-1199 landing or a missing credential.
- Read `GET /api/runner-defaults` and `GET /api/session-runners`: defaults revision 2;
  the default Linux runner was available and eligible. No placement setting was changed.
- The scoped open-task listing contained no open CARD-1199 task. No dispatch, runner
  restart, deployment, cleanup or message to another agent was performed.

## F1: two frozen oracles require behavior removed by the reviewed repair

The TestDesign at `17169b9d` still binds these cases and controls:

| Frozen contract | Current source at the task base | Consequence |
|---|---|---|
| `next_start_uses_the_shared_veto`: dead sibling tool removed, unknown/unrelated tools retained. Used by CP-17, CP-1, G-204 and PC-204. | `CheckpointToolCopyCleanupTests.next_start_preserves_all_pre_existing_sibling_tools` asserts **all three remain**. `CheckpointApp.CreateRun` creates a fresh directory and explicitly does not reclaim older paths. | Porting the frozen positive-delete oracle would demand reinstating behavior the repair removed. Renaming a new test without changing its oracle does not resolve this. |
| `age_clean_cannot_bypass_live_custody`: removed list contains only the dead run. Used by G-206 / PC-206. | The current test asserts an empty deleted list, `output-ledger-missing`, and preservation of the dead run as well as live/reused/unknown runs. | Death and age do not confer recorded output custody. The frozen fixture and expected result must be rebound to the repaired contract. |

Exact origin: `ef0f0c5a2e34a9ed5d2e08f17845b2227ded2a5d`
(`fix(CARD-1199): retain sibling and legacy outputs without recorded custody`). Its diff
changes the first method's name and dead-tool assertion, and changes the second method's
deleted/refusal assertions. This is source evidence; no runtime pass or red is claimed.

Current locations:

- `tests/Antiphon.Tests/Checkpoints/CheckpointToolCopyCleanupTests.cs:59` and `:124`.
- `tools/Antiphon.Checkpoints/CheckpointApp.cs:456` (`CreateRun`).
- `tools/Antiphon.Checkpoints/Cleanup/OutputCleanup.cs` (`RemoveOlderRuns`).

## F2: S1a's proposed port does not cover the current production custody graph

The amended plan's D-10 requires a complete instance I/O port in S1a and virtual execution
through the real callers. Its S1a file list names five cleanup files plus CheckpointApp,
Program, WaitCommand, EvidenceFolder and RunStateStore. The now-reachable dependencies below
are outside that list. Passing an I/O argument only through the listed files would still
reach native filesystem/process I/O and process-global custody state.

| Production path | Required dependency outside the S1a list | Why substituting a fixture result is insufficient |
|---|---|---|
| `ToolCopyCleanup.Remove` -> guarded lock/write and deletion | `Cleanup/SafeWrite.cs`, `Cleanup/SafeDelete.cs` | Creation authority is recorded in process memory; deletion rechecks containment, links, Git and inventory and removes entries individually. The virtual fixture must exercise these policies rather than return a canned removal verdict. |
| Tool copy removal / startup sealing | `Cleanup/OwnedOutputLedger.cs`, `Cleanup/ToolCopyAuthority.cs` | Generic disk ledgers do not confer deletion authority. A separate run-bound tool-copy capability binds digest, creator, directory identity and entries. Both branches must retain their distinct authority rules. |
| Creation and seal admission | `Cleanup/ExclusiveDirectory.cs`, `Cleanup/OutputCustody.cs`, `LedgerStat.cs` | Exclusive directory creation and native identity/stat facts protect the seal window. A path-only virtual File/Directory port does not model those facts. |
| Ownership/state/terminal report reads | `Evidence/ReceiptFile.cs`, `Report/ReportWriter.cs` and the receipt parser they use | Regular-file, single-link, bounded reads and required terminal receipt/source binding are exercised by the completed-wait cases. Bypassing them would prove a different path. |

Between the frozen Plan object and this task base, the cleanup directory alone gained the
following files: SafeWrite (473 lines), SafeDelete (474), OwnedOutputLedger (88),
ToolCopyAuthority (211), OutputCustody (75), ExclusiveDirectory (23). These are source sizes,
not estimates of execution coverage or a claim that every line must change.

The revised design needs to bind the low-level observation port and an instance-owned
creation registry while keeping the guarded policy in production. It must retain existing
facades/native defaults without a mutable global I/O override, and specify the dependency
direction when shared types become library-consumable. It must also account for the
source-derived existing-class regression row requested by this Code brief.

D-10 explicitly rejected an isolated seam-only slice: "a seam slice with no C987 row has
no checkpoint." Therefore extracting only ExecutorImageGuard and presenting that as S1a
would not complete this frozen slice. No such partial implementation was substituted.

## Concrete resolution needed

1. Bind the two changed compatibility methods, their guard/control rows, and CP-17/CP-1
   selectors to the current preservation rules. Preserve the 14 / 16 / 54 / 80 floors;
   add guards for later custody rules where the revised reachable graph requires them.
2. Re-split or widen the I/O/custody refactor using the current helper graph above; name
   the exact production files, real-policy virtual fixtures, red-first selection and
   existing affected-class selection. Keep each commissioned Code slice within its budget.
3. Carry the corrected Plan/TestDesign artifacts into the next Code task's start tree.
   Resume S1a before S1b, with cleanup still unwired and inactive.

This document does not itself amend the frozen plan, change a test selection, or authorize
new destructive behavior. The existing CARD-1199/1242/1254 safety semantics remain authoritative
production facts to preserve.

## Verification disposition

Builds: 0. Tests: 0 executed / 0 passed / 0 failed / 0 skipped. No new tests were authored,
so no red-first or mutation evidence is claimed. No test host, build slot or checkpoint
executor was started. `slot=n/a`, `waited=n/a`, tested SHA: none. There are no CHECKPOINT lines,
TRX files, build receipts or alternate-output directories from this task.

| Requirement | Actual outcome in this task |
|---|---|
| CP-17 / R-1a | NOT RUN: incompatible acceptance and incomplete I/O scope above. |
| CP-1, CP-2, CP-3, CP-4 | NOT RUN: S1b/S2/S3 remain unimplemented. |
| CP-5, CP-6, CP-7, CP-8, CP-9, CP-10 | NOT RUN: later server/runner slices. |
| CP-11, CP-12, CP-13, CP-14, CP-15, CP-16 | NOT RUN: later script/client/native slices; Windows rows not commissioned here. |
| V-1, V-2, V-3, V-4 | NOT RUN; historical results remain historical. |
| V-5, V-6, V-7, V-8, V-9 | NOT RUN. |
| V-10, V-11, V-12, V-13 | NOT RUN. |
| R-1, R-1a, R-1b, R-2, R-3, R-4, R-5 | NOT RUN. |
| Whole Unit lane | Not run, per the explicit minimal-verification direction in the brief. |
| Required existing-class row | Not run: no executable source/test/plan selection was changed. |
| Per-host dry-run / manual activation | NOT RUN; no activation is authorized by this finding report. |
| PC-1..PC-260 and all prescribed variants | PENDING for commissioned SourceLanding Mutation; no mutants run. |

No full-assembly run or unlisted build/test was used. The evidence history check and
non-mutating merge-tree result are recorded in the final task report after committing this
file. The task remains incomplete and does not qualify for Review or land as an implementation.
Restart required for this documentation-only change: **none**.
