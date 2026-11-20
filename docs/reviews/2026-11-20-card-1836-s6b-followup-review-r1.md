# CARD-1836 S6b follow-up review, round 1 (Final, Full)

Subject: `feat/card-task-56e5508d` tip `05d355d37faa4dc52304ce2f757be3c89b9084db`, range
`fc45631910173cbba5f458498ac2479a85066b4f..05d355d37faa4dc52304ce2f757be3c89b9084db` (996155bf red witness,
983da5cb fix, 3ef37773 checkpoints and OS witness, 05d355d3 index). Code/landing owner 56e5508d.
Reviewer: Claude (author Grok). Linux server2; Windows UNOBSERVED.

Verdict: **CLEAN**. No blockers. Advisories below go to cards and do not hold the land.

## Rerun evidence

Checkpoint tool run `20261120-035659-3825`, plan `docs/superpowers/plans/2026-11-20-card-1836-s6b-followup-checkpoints.md`,
`validate --expected-source-sha 05d355d3...`: `CHECKPOINT SOURCE VALID rows=3`, verdict GREEN exit 0.

| Row | executed | passed | failed | skipped | source |
|---|---:|---:|---:|---:|---|
| CP-12 | 13 | 13 | 0 | 0 | clean, buildSource=verified |
| CP-20 | 81 | 81 | 0 | 0 | clean, buildSource=verified |
| CP-21 | 27 | 27 | 0 | 0 | clean, buildSource=verified |

CP-12 TRX names: the 8 RcRunEvidenceTests (incl. OneEvidenceRootPerRun, ChunkOutcomeNeedsResults,
TornRevisionStaysNotRun, LaunchStampIsCheaper), NightlyCoreTests CoreMatchesFallback, 4 LiveLaneRunnerTests.

- `check-evidence-diff.ps1 -BaseRef fc456319 -HeadRef 05d355d3`: commits=4 entries=0 violations=0.
- `git merge-tree --write-tree d36d9541 05d355d3`: one conflict, `tests/derived-impact-index.json` (generated; the land
  regenerates it). Master changed `tests/test-execution-policy.json` and added `RcTagBacking.cs`; no overlap with the subject's files.
- Pins: the subject touches none of the policy hash or `PinnedInputDigest` inputs. The merged tree carries master's
  `policyHash` 8c376b03... and `PinnedInputDigest` c0d2a723.... The claimed 0396d6a5/3162a7da are the base values. No repin is owed.

## Red/green and mutants (scratch `git archive` copies, harness only, one build-slot lease each)

| Probe | Case | Result |
|---|---|---|
| 996155bf (red commit) | OneEvidenceRootPerRun | FAIL: missing-join-record; changed-input-snapshot; discovery-revision; second-run-refused |
| M1 admission hook back to `-LogRoot` (root split) | OneEvidenceRootPerRun | FAIL: missing-join-record; changed-input-snapshot; second-run-refused; fault-one-warning |
| M2 missing results file -> Passed | ChunkOutcomeNeedsResults | FAIL: missing-results-not-run |
| M3 drop `Write-RcRunEvidenceWarning` (silent swallow) | OneEvidenceRootPerRun | FAIL: fault-one-warning |
| tip | all CP-12 | green (above) |

The end-to-end witness drives the real `Invoke-AntiphonNightlyRunCore` in `scripts/lib/nightly-run-impl.ps1`, which hops
to a real child `pwsh nightly-tests.ps1` with only StartProcess/Git seams. It reads the launch join record, source
inputs, chunk revision and discovery files from the run directory. It does not match strings.

## Judgements

1. **Recording cannot harm a run (no blocker).** Fault matrix on the real path (scratch case `FaultMatrix`, same
   fixture, five runs on one log root): ANTIPHON_RC_RUN_EVIDENCE=off, record, unwritable root (`/proc/1/...`, parent and
   child), IOException "No space left on device" thrown inside every core call, and a 549-char space+unicode log root.
   All five ended with the same result: Phase=tests ExitCode=1 TestsPassed=False reasons=[stale-timestamp]. Off wrote 0
   evidence files and record wrote 6. Each fault printed one `rc-run-evidence:` line per process. Reading the code: every new hook is
   `[void](wrapper)`, and the wrapper catches everything inside the body. The added code outside the body is pure PowerShell:
   `Get-NightlyRcChunkOutcome`, with its IO in try; `Register-NightlyRcDiscoveryOnce`, which uses a hashtable; and
   Join-Path/Test-Path. Two runs on one log root get distinct run directories (UTC minute + RunId), and the writer lock is
   per root. The parent always hops to the checkout's own `nightly-run.ps1`, because the hop compares `$PSCommandPath`
   (the impl file) and that always differs. So the parent impl and the child `nightly-tests.ps1` are the same version.
   `-RcEvidenceRoot` cannot reach an older child's parameter block. Windows path: `C:\Antiphon\releases\candidates\<id ~40>\logs\<24>\rc-run-evidence\launches\<suite>\<chunk>.json`
   is about 190 characters with real names. The theoretical worst case, with 80-char suite and chunk tokens, is about 294.
   .NET 9 file APIs add the `\\?\` prefix themselves, and any failure is swallowed. UNOBSERVED on Windows.
2. **Passed rule.** Exit non-zero is Failed. A drain timeout, a missing file, an empty file or an unparseable file is
   NotRun with a reason. `$chunkRecord` is used only for the revision. The run verdict still comes from the existing logic.
3. **Torn write.** The revision is written to `.partial`, then `File.Move` without overwrite. That is an atomic rename on
   ext4, and MoveFileEx on NTFS on the same volume. A torn or empty slot is skipped by `NextRevisionSlot` and keeps
   `pending=not-run`. A leftover from a killed run lives in that run's own directory, and a new run never reads it.
4. **A-6 cost.** Launch registration runs before `$phaseWatch` starts. The full read runs once per suite, in the build
   evidence loop, outside any chunk phase. Per launch it also runs whenever a stamp mismatches. The formula
   `extrapolatedStamp1289 = wideLaunchMs x 1289` matches the claim of about 33 s.
5. **OS witness/restart.** All touched paths are portable and none adds an OS branch. A run in flight has already hopped
   and loaded its scripts, so it picks up the change only at its next sync+hop. Restart target none is correct.

## Advisories (cards, non-blocking)

- **AD-1 Shadow Passed is weaker than the run's own TRX acceptance.** In the fixture, the revision recorded
  `outcome=Passed` while the run's summary rejected the same TRX with `stale-timestamp`. A stale TRX or one with zero
  executed tests at the results path records Passed. Before an Incremental cut trusts these revisions, bind Passed to the
  run's TRX acceptance (freshness, executed>0, failed=0).
- **AD-2 No green-run equivalence witness.** The e2e fixture never passes (stale probe TRX), so `fault-verdict`
  compares one failing run with another. The success-path closure hook is never driven end to end. A fresh-timestamp
  probe TRX would let the witness compare off and record on a green run.
- **AD-3 Off switch does not cover `Get-NightlyRcChunkOutcome`.** With recording off it still reads and parses every
  chunk TRX. That costs a little but writes nothing.
- **AD-4 A-6 cost line omits per-launch `AssertCurrent`.** That is git rev-parse, git status --untracked-files=no, and
  a policy-file hash on the real checkout. It measured about 20 ms a launch on server2 for 11,156 files, and Windows is
  unmeasured. Any change to a top-level dll, deps.json or runtimeconfig after the seal makes every later launch pay the
  full read before refusing.
- **AD-5 Stamp soundness.** An edit with the same length and a restored mtime goes undetected. The build seal stats
  mtime after reading the bytes (TOCTOU). Acceptable for shadow evidence.
- **AD-6 Warning dedupe is per process and per reason.** The parent writes to its console and the child to the tests
  log. One fault can therefore produce two lines in a run: one parent, one child.

PCs PC-V12-join, PC-V12-inputs, PC-V12-interrupt and PC-V12-once stay pending for SourceLanding Mutation.
