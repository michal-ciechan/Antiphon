# Review 3a155cc8: group 124 harness and oracle repairs (round 1, diff-only)

Subject: task 9654dc57-4d40-46f1-baae-2aa2ca517bb6, branch feat/card-task-9654dc57, tip
f19058c7b09aaa8eb4c3756a5f5fb5392b486d25, base 679b696bd178e3c8bb14b32fcd04a1c26b148435.

Verdict: one defect (D-1). The CRLF and staging-exit repairs are sound.

## D-1: the capture-seam phase strip removes the whole child environment

- Where: `scripts/test-qualification-timing.ps1:240` (inside `New-QualificationCaptureSeam`), commit 6f42e1747.
- Failure: `if ([string]$name.StartsWith("ANTIPHON_TEST_PHASE_"))`. In PowerShell the cast binds after
  member access, so this is `[string]($name.StartsWith(...))`. That gives "True" or "False", and both
  strings are truthy. When `QTIMING_FORWARD` is not 1, the loop removes every variable: PATH, HOME,
  SYSTEMROOT/TEMP on Windows, and every non-phase key just copied from `$Environment`. Only
  `QTIMING_SEEN`, added afterwards, survives.
  Probe (real seam extracted from each revision, parent `ANTIPHON_TEST_PHASE_ROOT` set, shell wrapper dumps `env`):
  - tip f19058c7b: forward=1 env=79 (PATH, HOME, KEEP_ME and PHASE present); forward=0 env=2 (PATH=0, HOME=0, KEEP_ME=0, PHASE=0).
  - base 679b696bd: forward=0 env=79 with PHASE=1 (the original bug).
  - With the precedence fixed (`([string]$name).StartsWith("ANTIPHON_TEST_PHASE_", [StringComparison]::Ordinal)`): forward=0 env=78, PATH=1, HOME=1, KEEP_ME=1, PHASE=0, and seen=0.
- Why it matters: (1) It weakens the oracle. capture-identity is meant to compare the same child with and
  without the phase variables. The off side is now a child with an empty environment, so
  `off.Seen -eq 0` holds whatever the forwarding filter does. The `continue` skip of phase keys in
  the `$Environment` loop is dead code: a fixture that forwarded phase keys would not go red.
  (2) It is a regression risk for the Windows case this repair targets: Git `sh.exe` (MSYS) now starts
  with no PATH, SYSTEMROOT, TEMP or HOME. Linux `/bin/sh` tolerates that, so
  `Qualification_timing_completes_without_mkfifo_chmod_or_ln_on_path` passes here. The worker's
  mutant 3 also passed because it only checked the phase variable.
- Fix: parenthesise the cast and use ordinal comparison (case-insensitive on Windows if wanted).
  Add a red-first witness that the off-side child keeps a non-phase variable, such as a forwarded
  `$Environment` key or PATH, and still lacks `ANTIPHON_TEST_PHASE_*`. Keep the forward=1 case unchanged.

Invariant (a) otherwise holds:
- `SourceLines` only maps CRLF and lone CR to LF, applied to the needle. No non-newline difference can compare equal.
- The stub `$global:LASTEXITCODE = 0` is the contract of a real successful scp. The product check at `scripts/c590-real.ps1:799` is unchanged.
- The strip is gated on `QTIMING_FORWARD -ne "1"`. forward=1 keeps every variable, verified above.

## Runs (Linux runner, one build at the tip, all under build-slot leases)

- Tip f19058c7b, `VolumeRetirementApplyGateTests` + `RetireTempRunnerTimestampTests` + `PosixHarnessToolTests`: total 24, passed 24, failed 0, skipped 0. TRX identities include both new apply-gate tests, the newline guard, 3/3 timestamp cases plus reject-before-remote, and Qualification_timing + Capture_identity.
- Mutants, one incremental rebuild:
  - M1 (`SourceLines` made an identity function): `Apply_gate_crlf_copy...` failed.
  - M2 (both stub `LASTEXITCODE = 0` lines removed): 3/3 `Bridge_exports...` failed with `DIAGNOSIS=RecycleReceiptUnavailable`. `Bridge_still_rejects...` and `Apply_gate_is_checked...` passed. Total 6: 4 failed, 2 passed. Both files were restored with git checkout.
  - M3 (inherited-strip removed): equivalent to the base probe above; off side PHASE=1, seen file written.
- Red commit fbdfc26a5 is red by construction: CRLF needle against the LF script without normalisation.

## Gates

- `audit-land-verification-graph.ps1 -Check` (evaluator built through build-slot; no -Update): digest=D92450D9AABCBF9C783B506F73DF06C45635F415D03E65C5F1A97AF415CC36BE count=69, approved.
- `check-evidence-diff.ps1 -BaseRef 679b696bd -HeadRef f19058c7b`: commits=5 entries=1 violations=0, exit 0.
- `merge-tree --write-tree` exit 0, no conflicts:
  - origin/master (679b696bd): tree e726787d7.
  - origin/rc/2026-11-01 (679b696bd): tree e726787d7.
  - 18c1cf8556a5 (group-122 census repair): tree 5eff60bdc.
- Build output (bin-r3a155, bin-land-audit) deleted. Worktree clean.

## Advisories (card candidates, not defects)

1. CARD-1609 has no witness that a failing or null-exit staging `scp` (c590-real.ps1:798-801) or a failing prepare/seal ssh yields `RecycleReceiptUnavailable`. Only the receipt-copy `-r` failure is witnessed (RemoteScriptContractTests:3224, RetiredTempContainerScriptTests:402). The diff did not create this gap.
2. `Multiline_raw_source_needles_are_newline_normalised` is coarse. Any `name.Replace(` within 800 characters counts as normalised. It only detects direct `ShouldContain/Contains/IndexOf(name` arguments.
3. The inherited `ReleaseGatePlatformTests.LinuxOnlyWindowsExclusionPolicy` red (56 vs 47, Card1619*), reported by the worker, was not rerun here.

Scope: diff-only per the brief. The whole Unit lane was not run, so ordinaryScopeCompleted is Interim. PCs are pending.
