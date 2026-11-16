# CARD-1955 Review, round 2 (diff-only, blockers only)

Reviewed source: Debug task `ee76e32c-29ca-4596-bfda-e01a8b61add0`, branch
`feat/card-task-ee76e32c`, tip `45dbedf1ea0ff663a309dc0576466c6cd043e54a`.
Range `f1251c5cc19b524ef81ddef30f96d9e3f52e209b..45dbedf1ea0ff663a309dc0576466c6cd043e54a`
(round 1 reviewed `f1251c5cc`, Grok, review on `feat/card-task-9e615360`).
Reviewer: Claude (independent of the Grok author and the Grok round 1 reviewer).
This document is a later commit on `feat/card-task-fd30d2b0`. Land the Debug owner
at `45dbedf1ea0ff663a309dc0576466c6cd043e54a`, not this review commit.
`reviewedSourceClean` is false: no checkpoint manifest exists, so these runs are
build-slot leases without checkpoint receipts.

Verdict: CLEAN. The round 1 blocker is closed and there is no new blocker. The
advisories below go to cards and do not hold the land.

## The repair

- `a13a18616` adds the witness `Failed_refused_persist_refuses_active_lifecycle_apply`
  and its Windows skip-roster row. Nothing else changes.
- `a0c9cc99f` changes only `scripts/c1008-retirement.sh`. It adds
  `c1609_require_finished_phase`, calls it first in `c1609_require_ready_preview`,
  and in `c1609_phase_begin` it resets `C1609_PHASE_PRIOR_NONCE`, refuses
  `active==true` before any archive or overwrite, and records the archived
  predecessor nonce.
- `45dbedf1e` is the repair note in `docs/investigations/`.

`scripts/c590-remote.sh` is unchanged (empty `git diff --stat`). No other script
line changed. No test assertion was deleted or loosened, because the test diff only
adds lines. The 5000 ms and 900 s bounds, custody, header, nonce, dirty-work guards,
fsync refusal and live-origin withdrawal are untouched.

## (A) Does it close the hole?

Yes, on every path.

- **Late refusal with a failed Refused persist.** `c1609_invalidate_ready_receipt`
  leaves `C1609_READY_INVALIDATION_FAILED=1`. In `write_result`, invalidation runs
  before `c1609_phase_end`, and `phase_end` returns 1 at its first statement when
  the flag is set. Nothing is written, so `lifecycle.json` stays `active=true`.
  After that:
  - Direct host `c1609_require_ready_preview`, in either mode, runs
    `c1609_require_finished_phase` first. The lifecycle is not finished, and
    `C1609_PHASE_PID` is not this shell, so it refuses.
  - A later `c1609_phase_begin` refuses on `.active==true` before it archives or
    persists anything.
  - `c1008_recycle` therefore refuses with `RecycleReceiptUnavailable`.

  The witness shows each of these, and it shows the receipt bytes, the lifecycle
  bytes and an empty `Removed` list all unchanged. It can go red: on the unfixed
  script, direct admission returns 0, and the combined assertion fails. The Debug
  recorded that red at `a13a18616`.
- **Legacy edge: lifecycle inactive while the receipt is still Ready.** When the
  preview was not accepted, this state is unreachable. Invalidation precedes
  `phase_end`, and a failed invalidation stops `phase_end` before its first write.
  If invalidation succeeded, the receipt is durably Refused.
  The one reachable variant is `accepted=true`: `phase_end` renamed `lifecycle.json`
  to inactive, its directory `sync` then failed, and the follow-up invalidation also
  failed. That Ready came from a preview whose fences all passed, so it is not a
  Ready that should have been refused. Apply still re-checks the 900 s age,
  fingerprints, the inventory digest and the publication gate. If the archive
  persist fails first, `lifecycle.json` stays active, and the receipt is refused.
- **Crash after the Ready rename and before `phase_end`.** SIGKILL leaves the
  lifecycle active, so the receipt is refused. This is the safe side of the fence,
  as round 1 prescribed.

## (B) The same-shell alternative

The alternative admits only when all of the following hold:

1. `C1609_PHASE_PID == $BASHPID`.
2. The current `lifecycle.json` nonce equals this shell's `C1609_PHASE_NONCE`, so it
   is this shell's own phase.
3. `C1609_PHASE_PRIOR_NONCE` is set. Only `phase_begin` sets it, after it has proved
   that the previous `lifecycle.json` was `active==false` and was complete with its
   children joined. The variable is cleared at every `phase_begin` entry.
4. The archived `lifecycle-<prior>.json` is valid, inactive, complete and joined.
   Only two writers produce an archive. `phase_end` writes it as `active=false`,
   and `phase_begin` copies a lifecycle it has already proved inactive.

It therefore accepts any *finished* predecessor phase of the same operation: a
finished preview, or a finished, refused, non-consuming Apply. It never accepts an
active predecessor or a predecessor with the failure marker. A failed or crashed
preview leaves the lifecycle active, so `phase_begin` refuses before it sets
`PRIOR`. A predecessor Apply that consumed the receipt leaves it in
`Applying`/`PartialRefused`, which fails `state=="Ready"`. A subshell or a
separate process fails the `BASHPID` check. The only remaining bypass is a caller
that injects all three `C1609_PHASE_*` variables into the environment. That
caller already controls the host shell and is not a review threat.

## (C) Legitimate flows

- **(i) Preview in invocation 1, then Apply with `-RetirementProof` in invocation 2.**
  `phase_end` makes the preview lifecycle inactive. Apply's `phase_begin` archives it
  and sets `PRIOR`, and the same-shell path admits. `VolumeRetirementApplyGateTests`
  13/13 includes the matching-token Apply that deletes the four temp volumes.
  The native N3 fixture calls `c1609_require_ready_preview` directly after a real
  preview. It hits the finished-lifecycle arm, so it is not regressed.
- **(ii) A preview that crashed mid-audit.** The same operation now refuses with
  `reason=lifecycle-active recovery=new-operation` and `DIAGNOSIS=RecycleReceiptUnavailable`.
  The witness asserts both through `retire-temp-runner`, with exit 2. The desktop
  path then refuses as well:
  - `c590-real.ps1` writes the diagnosis.
  - `deploy-server2.ps1` gives every new `-DryRun` a fresh `c1008<guid>` operation.
  - The docker-stack gate-9 row says any other refusal "gets one fresh preview and
    apply, then a Debug task".

  That is the stated recovery, with no hang and no same-operation retry loop.
  The bash EXIT trap runs on SIGHUP (checked locally), so an ssh drop still reaches
  `write_result` and `phase_end`. Only SIGKILL-class deaths leave the lifecycle active.

## (D) Nothing weakened

As listed under **The repair**. The only changes outside the new witness and the
two functions are the new helper `c1609_require_finished_phase`, one Windows
skip-roster row and the docs note.

## Advisories (cards, not land holds)

- **A-1. Same-operation resume after a SIGKILL-class Apply crash.** This applies to
  the temp project after OOM, SIGKILL or a host reboot. Before this change, a dead
  owner let `phase_begin` resume the operation. Now `-ResumeRecycle <op>` refuses
  with `lifecycle-active`, because the crashed Apply's own lifecycle stays active
  even though its receipt is already consumed (`Applying`). The outcome fails closed,
  as round 1 prescribed, but it costs the documented crash-resume.
  - The printed `recovery=new-operation` is incomplete for this case.
    `c1609_temp_retirement_clearable` refuses `TempRetirementUnresolved` while the
    stranded `Applying` operation exists. It then needs an operator
    `retire-runner-volumes.ps1 -Close`. `c1609_close_host` accepts an active
    lifecycle with a dead owner, but this review did not run that path end to end.
  - The N3 SIGKILL crash/resume fixture bypasses `phase_begin`. No test covers a
    resume through the real entry after SIGKILL.
  - Suggested card: allow `phase_begin` over an active, dead-owner lifecycle only
    when `C1008_RESUME=1` and the receipt is already consumed. Add a real-entry
    SIGKILL resume witness, and document the Close recovery.
- **A-2. Misleading reason for a missing lifecycle.** A missing `lifecycle.json`
  prints `reason=lifecycle-active`. It fails closed, but the reason is wrong.
- **A-3. Refusal arms without a witness.** The same-shell arms that refuse a PID
  mismatch, a nonce mismatch, a missing `PRIOR` or an inactive-prior failure have no
  witness. Mutation is waived and owed, and the PCs stay pending.
- **Carried from round 1.** The C994 RD-4 `TempRetirementJournalUnknown` inherited
  refusal and the slow jq/rolling roster setup.

## Verification (Linux, server2 mirror, build-slot leased, serial)

Runs at `45dbedf1e` used one leased `dotnet build tests/Antiphon.Tests -p:UseAppHost=false`
(0 errors, 5m46s). Each run below was its own lease with `--no-build`.

| Run | Result |
|---|---|
| `VolumeRetirementReceiptTests/Failed_refused_persist_refuses_active_lifecycle_apply` alone | 1/1 passed, 46s |
| `VolumeRetirementApplyGateTests/*` | 13/13 passed, 5m33s |
| Receipt shard: Lock_order, Ready_publication, Large_ready, Late_ready | 4/4 passed, 2m00s |
| Receipt shard: Preview_receipt, Receipt_shape, Proof_directory, Volume_incarnation, Freshness, Legacy, Manual_attestation_is_exact | 7/7 passed, 5m34s |
| Receipt shard: Manual_attestation_public_entry, Changed_content, Every_delete, Journal_*_replacement, Journal_*_close | 5/5 passed, 9m00s |
| `Journal_intents_and_partial_resume_are_exact` alone | 1/1 passed, 4m19s |

`VolumeRetirementReceiptTests` passed 18/18, with no failures, no skips and no
timeouts. Load average stayed between 1.2 and 9.5.

`bash -n` passed for `scripts/c1008-retirement.sh` and for `scripts/c590-remote.sh`.
No RemoteScriptContract test names `c1609_phase_begin`, `c1609_require_ready_preview`
or the new helper, and `c590-remote.sh` is unchanged, so none was rerun. The 37
round 1 pieces were not rerun, per the brief.

Evidence-diff over `f1251c5cc..45dbedf1e`: `commits=3 entries=0 violations=0`.
`git merge-tree --write-tree origin/master 45dbedf1e` against
`9b98828eb34eb552ce62bcde5a64dd045a872a88` wrote
`9a1f7d7b59e808d2b5f3f14b8d739cae3437157f` with no conflicts.

Guard union on a scratch detached worktree of that merge tree. Its commit
`f309258b9` is local and not pushed.

- The impact driver regenerated twice, built first, under a lease. Both runs printed
  `derived-impact-index unchanged` (`windowsOnly=31`, `linuxOnly=1`).
- Antiphon.Tests built with 0 errors.
- The ten-class shard passed 87, skipped 1 (`C1210_Windows_linux_only_outcomes_are_exact`,
  a Linux skip by design) and failed 0, out of 88. `DerivedImpactIndexTests` passed
  27/27. The union total is 114 passed, 1 skipped and 0 failed, out of 115.
- Pins are unchanged:
  - `policyHash` `c2e3939821a8fef911600c44be767b951c8212bcb62824cb746e48c5aaba6909` is
    in `tests/test-execution-policy.json`.
  - `LegacyExclusionDigest` `6d6ff646207dba9e3c09316e5bc77651f6801fe945ccde83b9b58644367b252a`
    is in `tests/Shared/NativeLinuxPolicyWitness.cs`.
  - `test-live-lane-run.ps1 -PrintInputDigest` printed
    `2f48120a65280582b586f9369e2eaf6b1bdc464a2362c8e49d434bd852479f60`, which equals
    the stored `PinnedInputDigest`.

Mutation is waived and owed, and every PC stays pending.
