# CARD-0667 S1b Code evidence

S1b read-only qualification is implemented; CP-8 is green (4/4). Automatic release remains dormant. Original Code task and landing owner: `b71467ba-1d2d-47f4-87ab-856ba13e69f1` (`b71467ba`). Next: ordinary Review, then the caller lands this original Code task and commissions SourceLanding Mutation.

Branch: `feat/card-task-b71467ba`. Worktree: `/work/worktrees/task-b71467ba`. Desktop checkout is not reachable from this mirror. Task base: `86954affd07984a13c56ff57eabacdf067008bc6`. Plan: `docs/superpowers/plans/2026-10-01-card-0667-terminal-task-seat-release-plan.md`, S1b / CP-8. No rebase, reset, amend or force push.

## Implementation

- `src/Antiphon.SessionRunner.Contracts/TerminalSeatRelease.cs`: expected store/generation plus native delivery binding/floor, typed read-only qualification result and opaque token.
- `src/Antiphon.SessionRunner/TerminalSeatReleaseObservation.cs`: volatile proof bound to runtime epoch, session object, request and complete fresh evidence; first-read refusal, monotonic 120-second window, prompt-floor check, evidence-change reset and unavailable-read discard. A token permits reinspection only.
- `src/Antiphon.SessionRunner/SessionRunnerRuntime.cs`: unused internal observation/token-reinspection APIs under the launch gate, real tailer/generation lookup, current input/output evidence checks and test I/O binding seam. No HTTP/phone-home operation, signal route or automatic caller added. Attempted-input fencing remains S2b; this slice accounts for completed writes only. The delivery owner must supply the native binding/floor for the current generation; server ownership and delivery qualification remain later slices.
- `tests/Antiphon.SessionRunner.Tests/TerminalSeatReleaseTests.cs`: four CP-8 methods using real provider tailers, fake child writes, retained manifest/sidecar, independent runner/server clocks, exact boundary assertions and a production epoch-decision witness that cannot be masked by an empty token cache.
- Plan CP-8 filter only: added trailing method wildcards required by pinned TUnit source-generated OR discovery. This changes no intended method or count. The original exact row produced a fresh zero-test TRX; it is not behavioral evidence. No other checkpoint rows were edited.

## Verification and scope

The task's explicit S1b/CP-8-only instruction and plan prohibition on whole Unit/assembly runs were followed over the generic Final-profile boilerplate. No Windows row is part of S1b. No real provider, database, native PTY or production process was started; no live acceptance/deployment operation is required for this dormant slice.

| ID / case | Actual outcome |
|---|---|
| V-1 / `Restart_invalidates_volatile_observation_tokens` | Passed; otherwise-valid old-epoch proof refused by the production decision; recreated runtime rejects old token and requires a new 120-second window. |
| V-1 / `Old_turn_end_does_not_qualify_a_new_generation` | Passed; internal Claude/Grok/Codex cases reject old end and accept later real delivered prompt/end after the window. |
| V-1 / `Two_observations_require_the_full_safety_margin` | Passed at 0, 119.999, exactly 120 and 120.001 seconds; independent server clock cannot supply runner elapsed time. |
| V-1 / `Unavailable_observation_discards_qualification` | Passed; recovered read at old t+120 starts at zero elapsed and requires a new window. |
| V-1 remaining methods | Not rerun/claimed here; other owning slices and S4c final qualification. |
| V-2, V-3 | Deferred to server implementation slices and S4c; not passed by this task. |
| R-1, R-2, R-3, R-4 | Deferred to S4c final qualification; not passed by this task. |

No ordinary S1b case remains pending. All other CP IDs (CP-7, CP-9 through CP-18 and CP-1 through CP-6) were outside this dispatch. CP-1 through CP-6 remain the final S4c roster, including the separately commissioned Windows rows.

| Run | Tested committed source | CP-8 outcome |
|---|---|---|
| 20261005-095126-1a88 | `2b64bad6fcb508a1119f56fc1d36883e3f70ef8d` | Build clean, 0 executed; original plan filter discovery failure (exit 3), no red credit. |
| 20261005-095301-2f39 | `3cfbcfbd9cb43e992650a91d826679c31dd511f5` | 4 executed, 0 passed, 4 named assertion failures, 0 skipped (exit 1). Safe compiling seam returned Unknown instead of Waiting/OldPrompt/Qualified. All four exact methods inspected in fresh TRX. |
| 20261005-095655-1326 | `bc1276eacc7c28f4a7442f0c89baa8eee5f1914b` | 4 executed, 4 passed, 0 failed/skipped (exit 0); all four exact methods inspected in fresh TRX. |

Each run used the checkpoint tool `run --plan <plan> --rows CP-8 --expected-source-sha <that committed HEAD> --max-wait 50s`, awaited to completion. The already-built tool was invoked with `dotnet tools/Antiphon.Checkpoints/bin-c667-tool/Antiphon.Checkpoints.dll`; it owned each selected build/test slot. Each selected build and row has `slot=granted`, `waited=0s`, `dirty=0`, `sourceState=clean`, `buildSource=verified`. The report's `builds: 18` is the imported manifest roster: only `bin-c667-s1b` ran; the other 17 entries have state `unused`.

One unlisted prerequisite build compiled the missing checkpoint tool, through `scripts/build-slot.ps1 -Label c667-s1b-checkpoint-tool -- dotnet build tools/Antiphon.Checkpoints --property:OutputPath=bin-c667-tool/ --property:UseAppHost=false -nodeReuse:false`. Slot granted, waited 0s, build succeeded with one existing TaskOwnerGuard nullable warning and no errors. No unlisted test driver ran. No unchanged-green repetition, loaded run, timeout widening, assertion loosening or deliberate mutant was used. The only repair was the CP-8 filter correction.

Green receipt: `/work/worktrees/task-b71467ba/.antiphon/checkpoints/20261005-095655-1326/report.json`. TRX: same root, `rows/CP-8/run.trx`. Red and zero-selection receipts/TRX remain in the correspondingly named ignored checkpoint roots.

`validate --evidence <green report.json> --expected-source-sha bc1276eacc7c28f4a7442f0c89baa8eee5f1914b --rows CP-8` returned exit 0:

```text
CHECKPOINT SOURCE VALID source=bc1276eacc7c28f4a7442f0c89baa8eee5f1914b rows=1
```

The full task-range evidence guard passed through the tested source (`commits=3 entries=0 violations=0`); it is run again after this Markdown-only evidence commit. This report-only commit is not another test execution. Generated TRX/JSON/logs remain ignored; only this individual Markdown report is committed. The checkpoint tool removed its selected alternate outputs; the separate `bin-c667-tool/` is removed before handoff.

Read-only routing prerequisites: authenticated GET `/api/runner-defaults` and `/api/session-runners` both succeeded; defaults revision 2, eligible Linux and Windows lanes observed. No host pin, platform change or operational setting change was made.

## Mutation and operational handoff

All S1b PCs remain pending for method-scoped post-land SourceLanding Mutation: PC-20 (epoch-only otherwise-valid proof plus runtime restart), PC-26 (Claude/Grok/Codex old-generation end variants), PC-29 (full margin boundary), PC-32 (unavailable gap). Code executed no deliberate mutants. Other plan PCs remain with their owning slices; no PC is discharged by ordinary green.

Restart: `none`; owner: caller/orchestrator for eventual activation after the later plan slices. No server/runner restart or deployment is requested for S1b.

## Unedited checkpoint lines

CHECKPOINT CP-8 commit=2b64bad6fcb508a1119f56fc1d36883e3f70ef8d build=ok filter=/*/*/TerminalSeatReleaseTests*/(Restart_invalidates_volatile_observation_tokens)|(Old_turn_end_does_not_qualify_a_new_generation)|(Two_observations_require_the_full_safety_margin)|(Unavailable_observation_discards_qualification) executed=0 passed=0 failed=0 skipped=0 trx=/work/worktrees/task-b71467ba/.antiphon/checkpoints/20261005-095126-1a88/rows/CP-8/run.trx slot=granted waited=0s dirty=0 source=2b64bad6fcb508a1119f56fc1d36883e3f70ef8d sourceState=clean buildSource=verified

CHECKPOINT CP-8 commit=3cfbcfbd9cb43e992650a91d826679c31dd511f5 build=ok filter=/*/*/TerminalSeatReleaseTests*/(Restart_invalidates_volatile_observation_tokens*)|(Old_turn_end_does_not_qualify_a_new_generation*)|(Two_observations_require_the_full_safety_margin*)|(Unavailable_observation_discards_qualification*) executed=4 passed=0 failed=4 skipped=0 trx=/work/worktrees/task-b71467ba/.antiphon/checkpoints/20261005-095301-2f39/rows/CP-8/run.trx slot=granted waited=0s dirty=0 source=3cfbcfbd9cb43e992650a91d826679c31dd511f5 sourceState=clean buildSource=verified

CHECKPOINT CP-8 commit=bc1276eacc7c28f4a7442f0c89baa8eee5f1914b build=ok filter=/*/*/TerminalSeatReleaseTests*/(Restart_invalidates_volatile_observation_tokens*)|(Old_turn_end_does_not_qualify_a_new_generation*)|(Two_observations_require_the_full_safety_margin*)|(Unavailable_observation_discards_qualification*) executed=4 passed=4 failed=0 skipped=0 trx=/work/worktrees/task-b71467ba/.antiphon/checkpoints/20261005-095655-1326/rows/CP-8/run.trx slot=granted waited=0s dirty=0 source=bc1276eacc7c28f4a7442f0c89baa8eee5f1914b sourceState=clean buildSource=verified

