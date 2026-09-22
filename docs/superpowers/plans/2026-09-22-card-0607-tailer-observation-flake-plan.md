# CARD-0607: deterministic unreadable-transcript test setup

Date: 2026-09-22. Status: ready for Code; verification design is included.
Base: `55b68e717de4df4f5801079a2daac569757fb307`.
Evidence: [investigation](../../investigations/2026-09-22-card-0607-tailer-observation-file-lock-flake.md).

Make the existing test wait for its tailer's initial read to finish before taking
the exclusive handle. This is a test setup fix; no production behavior changes.

## Ground truth

Paths and line numbers below refer to the base commit. Test paths are relative to `tests/`.

| Card assumption / question | What the code actually does | Consequence |
|---|---|---|
| A bound transcript is ready to lock. | `Antiphon.SessionRunner.Tests/TranscriptTailerObservationTests.cs:96` waits only for `BoundTranscriptPath`; `src/Antiphon.SessionRunner/TranscriptTailer.cs:657` publishes that path before the first read at lines 393-400. | The test's line 25 can lose the exclusive-open race. |
| Snapshot entries can signal that the read handle has closed. | The `await using` read scope ends before `ProcessPending` at lines 401-407; `Snapshot()` reads the entries under `_gate`. `SilentBody(true, false)` produces exactly three entries. With no append, the tail loop skips opening once its offset reaches file length. Fork discovery skips the current file. | Waiting for all three entries gives the required ordering, rather than a timing guess. |
| The production sharing flags may need repair. | Both production reads allow `FileShare.ReadWrite \| FileShare.Delete`; I/O failure in the observation becomes `Unavailable`. The investigation reproduced the exception at the test's own exclusive open. | Keep `TranscriptTailer` and its sharing flags unchanged. |
| `Antiphon.SessionRunner.Tests/RunnerRestartPreflightSafetyTests.cs:223` may have the same race. | `Bound_inputs_deny_writes_until_the_child_exits("pre-held-write-handle")` locks after `RestartFixture` finishes synchronous copies/reads, before `f.Run` binds inputs or starts any child. | No live reader/writer at acquisition; leave unchanged. |
| `Antiphon.Tests/Application/InterimVerificationReadinessTests.cs:102` may have the same race. | `C544_ReadFailure` awaits previous reads, then `StateFixture.Reset/Write` synchronously closes its writes; the next reader is constructed and invoked inside the lock scope. | No live reader/writer at acquisition; leave unchanged. |
| `Antiphon.Tests/Application/OutputDistillationDeliveryTests.cs:178` may have the same race. | `Unusable_file_uses_api_recovery("unreadable")` awaits `SettleAsync` and report storage before locking. The fixture starts the distiller only in the subsequent `RunDistillerAsync`, and the flush worker in `DeliverAsync`; its service provider is not a running host. | No live reader/writer at acquisition; leave unchanged. |
| `Antiphon.Tests/AgentTui/RunnerProcessProbeTests.cs:828` may have the same race. | `Required_file_rejects_a_file_without_read_access` awaits its unique scratch-file write, takes the lock, then creates and calls the probe. | No live reader/writer at acquisition; leave unchanged. |

## Decisions

- **D-1: Wait on parsed entries in the affected test.** After `BoundWorld.CreateAsync`
  returns, poll `world.Tailer.Snapshot().Entries.Count` until it reaches the three
  entries written by `SilentBody(true, false)`. Use the fixture's existing 8-second
  bound and 50-ms polling cadence; assert the count is exactly three before opening
  the file. A readiness timeout must fail with a setup-specific assertion.
- **D-2: Keep this wait local to `Unreadable_bound_file_is_unknown`.** The other
  observation cases do not need an exclusive lock. Reject changing `CreateAsync`
  globally, which would alter their setup unnecessarily. Name/comment the expected
  count as belonging to this three-entry fixture, not to transcripts in general.
- **D-3: Prefer the causal signal over retries.** A short bounded exclusive-open
  retry was an allowed alternative, but is unnecessary when this fixture has a
  known completion signal. Reject fixed sleeps, catch-and-ignore I/O errors,
  whole-test retries, timeout widening, and production sharing/retry changes.
- **D-4: Preserve the test's fault and verdict.** Retain the real `FileShare.None`
  handle for the entire observation and both existing assertions:
  `IsSuccessful.ShouldBeFalse()` and `Status.ShouldBe(Unavailable)`. Keep the tailer
  running and retain existing disposal and `ClaudeConfigDirEnv` serialization.
- **D-5: The four audited tests need no edits.** The four ground-truth rows above
  are their one-line dispositions. Reconfirm the ordering if the Code base changes;
  only an actual overlapping reader/writer warrants expanding this fix.

These are implementation decisions within the brief's authorized approach; no
product decision or approval is outstanding.

## Implementation slices

**S1 — Synchronize the exclusive-lock setup.** Edit only
`tests/Antiphon.SessionRunner.Tests/TranscriptTailerObservationTests.cs`, method
`Unreadable_bound_file_is_unknown`, following D-1 through D-4. Add a short comment
explaining that binding precedes the initial read, whereas snapshot entries follow
handle disposal. Keep the other four audited files unchanged under D-5; their
dispositions are already recorded here. Commit before CP-1. The existing five
methods in `TranscriptTailerObservationTests` are the test roster; no new test,
shared helper, production hook, or stress harness is needed.

## Verification design

### Inspection

Read the complete observation test/`BoundWorld` fixture; the tailer's snapshot,
binding, tail loop and fork scan; all four flagged methods; `RestartFixture`
construction/`Run`; readiness `StateFixture`/`ReadFor`; delivery fixture
`CreateAsync`/`SettleAsync`/worker startup and its `BuildHarness`; and probe
creation/scratch setup. The ground-truth table records the acquisition ordering.

### Proves it works now

- **V-1:** On Windows, the existing `Unreadable_bound_file_is_unknown` reaches an
  asserted three-entry snapshot before successfully acquiring its exclusive handle,
  then returns unsuccessful/`Unavailable` while the handle is held. CP-1 runs the
  real tailer and filesystem, not a mock.
- **V-2:** The same class's four remaining methods still pass:
  `Tail_observation_partial_line_is_unknown`, `Unparsed_new_line_is_unknown`,
  `Claim_switch_during_read_invalidates_observation`, and
  `Unsupported_tailer_cannot_certify_silence`.

### Guards the regression

- **R-1:** Review the ordering `read disposal -> three snapshot entries -> exclusive
  open -> observation`, with no file appends between readiness and locking. The
  explicit count assertion prevents a deadline exit from silently proceeding into
  the old race; the existing outcome assertions prevent a missing/ineffective lock
  from turning the test green. CP-1 validates this executable path. A single green
  run alone does not measure loaded flake frequency; the disposal/entry ordering
  and the investigation's measured mechanism are the reason the race is removed.

### Delivery inventory

No asynchronous outcome-delivery path is added or changed.

### Guard inventory

No production safety guard is added or changed. Guard inventory for changed
production logic: 0; mapped PCs: 0; missing: 0; duplicate mappings: 0.

### Positive controls

No PCs are commissioned for this test-setup-only change.
The existing unavailable-observation assertions remain intact; this card does not
redesign or requalify their production guard. Removing the wait would restore an
intermittent setup race, not a deterministic assertion-level positive control.

### Out of scope

Production source changes, the separate `CLAUDE_CONFIG_DIR` hazard noted in the
investigation, new hooks/helpers, synthetic CPU saturation, broad suites, and
rerunning the four unchanged audited classes. This card's ordinary scope is the
affected five-test class instead of the default Unit lane: only one test's setup
changes, and the brief explicitly calls for a proportionate test-only fix. If the
audit changes on a later base, amend the slices and checkpoint roster before
expanding execution.

### Cost

Estimated ordinary Code floor: **4 minutes**, including one isolated project build
and the five-test class; Mutation PC floor: **0 minutes**; combined verification:
**4 minutes**. Allow approximately 10 additional minutes for editing, inspection,
commit/report and cleanup. One build and five executions replace an unnecessary
full-assembly/namespace run; no empirical runtime saving is claimed.

Run CP-1 once on Windows after the S1 commit. Use fresh TRX results, require all
five named methods executed with zero failures/skips, and report the checkpoint
commit and actual counts. Do not claim a stress result. If red, diagnose and rerun
that row; confirm unexpected failures at the base with the exact failing method.
Retain the task's evidence and remove only its inventoried alternate build outputs
within the assigned worktree after execution.

```powershell
pwsh -NoProfile -File scripts/run-checkpoint.ps1 -Name CP-1 -Project tests/Antiphon.SessionRunner.Tests -OutputPath bin-c607/ -Filter '/*/*/TranscriptTailerObservationTests/*' -Expect TranscriptTailerObservationTests -MinExecuted 5 -ResultsRoot .antiphon/c607-checkpoints
```

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min |
|---|---|---|---|---|---|---|---|
| CP-1 | S1 | `tests/Antiphon.SessionRunner.Tests -> bin-c607/` | tailer-observation | `/*/*/TranscriptTailerObservationTests/*` | V-1, V-2, R-1 | all 5 named methods executed, 0 failed, 0 skipped | 4 |
