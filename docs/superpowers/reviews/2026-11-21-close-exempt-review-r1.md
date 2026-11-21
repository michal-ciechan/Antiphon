# Review r1: Close receipt-less preview exemption (Code task 864fbc83)

Review task `df47f045`, ordinary round 1, Final/Full scope. Subject
`origin/feat/card-task-864fbc83` tip `4a65563792a0fe76c5baa140e14eab51468c693b`
(Grok-authored), base `588747a803943477b465f3926858c6ad5d305629`. Reviewer: Claude
Opus (different company from the author). Read-only: nothing on server2 or the desktop
was touched; no product file was edited.

Verdict: **CLEAN**. No blockers, no regressions. Advisories below go to cards and do not
hold the land.

## Checkpoint run

One checkpoint-tool run of the plan
`docs/superpowers/plans/2026-11-20-close-receiptless-exempt-checkpoints.md`, run id
`20261121-005326-8ec1`, one build (`bin-close-exempt/`, reused by CP-2..CP-6), every row
`slot=granted`, load 2.4 at start. `validate --expected-source-sha 4a655637...` printed
`CHECKPOINT SOURCE VALID source=4a65563792a0fe76c5baa140e14eab51468c693b rows=8`,
`state=clean buildSource=verified`.

| CP | Covers | Result |
|---|---|---|
| CP-1 VolumeRetirementCloseExemptionTests | V-1..V-3 | 5/5 passed, 0 failed, 0 skipped |
| CP-2 VolumeRetirementWrapperTests | V-4 | 7/7 passed |
| CP-3 RemoteScriptContractTests (5 deploy-order methods) | V-5 | 5/5 passed |
| CP-4 ReleaseGatePlatformTests Linux-only pair | R-1 | 2/2 passed |
| CP-5 guard union (TestClassificationPolicy/Guard, SlowTestRegistryMerge, SlowTestTripwire, NativeLinuxPolicyWitness, MechanicalPcContract, TaskPlatformGuidance, C1729LayeringGuard, TestLaneCategoryGuard) | R-2 | 84/84 passed (min 78) |
| CP-6 DerivedImpactIndexTests | R-5 | 27/27 passed |
| CP-7 audit-land-verification-graph -Check | R-3 | exit 0 |
| CP-8 check-evidence-diff 588747a8..HEAD | R-4 | exit 0 |

Total 130 tests, 0 failed. `scripts/test-live-lane-run.ps1 -PrintInputDigest` prints
`659ea09e42513920ce1e46c4479ef092728db7702b687a65742768d67d46886e`, equal to the resealed
`PinnedInputDigest` (R-6).

## Red and mutants (scratch, bash only, no build)

The CP-1 shell fixture body was run directly against scratch copies of
`scripts/c1008-retirement.sh` (fixture uses `mktemp -d` SERVER2_ROOT, function stubs for
`docker` and `c1008_volume`; no real Docker or `/home/mc` path is reachable).

| source | exempt | ready | live | pending |
|---|---|---|---|---|
| tip | CLOSE_EXIT=0, closure 3 removed-observed + 1 unattributed | 2 | 2 (OwnerActive) | 2 |
| base 588747a8 | **2, CLOSURE=absent** (the stated red) | 2 | 2 | 2 |
| m1 readyPublished check removed | 0 | **0** | 2 | 2 |
| m2 closure/close-pending check removed | 0 | 2 | 2 | **0** |
| m3 live-owner compare disabled | 0 | 2 | **0** | 2 |
| m4 Close `continue`s on predicate refusal | 0 | **0** | **0** | **0** |

Every mutant turns at least one witness red; every case keeps `SIBLINGS_UNTOUCHED=1`.
The wrapper case (`Close_wrapper_surfaces_remote_refusal_token`) is red at base per the
Code report (remote `Daemon` surfaced as `JournalUnknown`); not re-run at base here.

## Attack results

1. **Exemption safety.** The shared predicate admits a receipt-less directory only when
   `lifecycle.json` passes `c1609_lifecycle_valid` (custody: owner uid, single link, no
   group/other write, not a symlink, canonical JSON, operationId equals the directory
   name), `complete==true` and every child joined, the owner is dead or its start ticks
   differ, `readyPublished==false`, and neither `closure.json` nor `close-pending.json`
   exists (file or link). `c1609_phase_preview_issued` persists `readyPublished=true`
   *before* `preview-<nonce>.json`, `preview.json` and `receipt.json` are renamed, and
   `c1609_phase_begin` carries `readyPublished` forward (true when a receipt exists), so
   `readyPublished==false` with no receipt means Ready was never published by that
   directory; Apply effects need a consumed Ready receipt. `control-*.json` /
   `exchange-*.json` are proof bookkeeping written before Ready, not volume effects.
   Receipt deleted after Ready: `readyPublished` stays true, predicate returns 2, Close
   refuses (fixture `ready`). Symlinked receipt: returns 1 and `c1609_read_canonical`
   refuses. Symlinked or missing lifecycle, non-directory entry, or a name outside
   `^c1008[0-9a-f]{32}$`: refused before or inside the predicate. A same-uid writer can
   forge the lifecycle, but the same writer could forge a receipt; that boundary is
   unchanged.
   **Clearable not relaxed.** `c1609_temp_retirement_clearable` keeps its lifecycle,
   completion and owner checks before the call; the predicate repeats them and then
   applies the identical three receipt-less conditions; return 1 falls through to the
   unchanged receipt path; any other value returns 1. A scratch probe of the extracted
   function at base and tip over ten receipt-less cases (exempt, live owner,
   readyPublished true, incomplete, close-pending, closure, symlinked receipt, missing
   lifecycle, symlinked lifecycle, bad name) gives identical exit and reason for all ten.
   A missing predicate (version skew) returns 127 and refuses.
2. **PID reuse.** Same logic as before in both callers: `/proc/<pid>` absent is dead; a
   reused pid with different start ticks is dead; a `/proc/<pid>/stat` read failure
   (process exits between the test and the read) refuses (return 2 / return 1).
   `owner.pid` is validated as a positive integer, so `/proc/` or `/proc/self` cannot be
   reached.
3. **Witness.** Five tests, all able to go red (table above). Temp SERVER2_ROOT, fake
   `docker`, fake `ssh` on a temp PATH; no real Docker, no real server2 path.
4. **Wrapper.** Success mapping unchanged (exit 0 and non-empty stdout returns stdout);
   exit 0 with empty stdout and every non-zero exit without a parsable token stay
   `JournalUnknown`; timeout path and the 90 s caller value unchanged. Only the last
   `^TempRetirementCloseRefused:` stderr line is used, capped at 160 chars and required to
   match `^[A-Za-z][A-Za-z0-9._: -]{0,159}$`, so ESC/CR/LF/`=`/`/` cannot reach the
   console. The file is ASCII-only; it is PS7-required at runtime and the new code uses
   no PS7-only syntax.
5. **Other edits.** `c590-remote.sh` diff is the clearable refactor only (see 1).
   Windows rc exclusion is legitimate: the class runs bash, `/proc` start ticks and an
   extensionless fake `ssh`, and carries `[NativeLinuxOnly]`; CP-4 confirms the policy
   and census agree. Both `docker-stack.md` sentences are true of the code (see A-2 for
   wording). Pin reseal and the derived index are the last two commits, in that order.
6. **Land mechanics.** `git merge-tree --write-tree origin/master(c711b48d) HEAD` is
   clean. The four master commits since the base touch only
   `ChannelOutboundUnifiedTransportTests.cs` and `UnifiedOutboundTransport.cs`, none of
   the protected nightly inputs, the policy, the index or these scripts, so no replay or
   reseal is needed; CP-7 `-Check` passes at the tip.

## Advisories (cards, do not hold the land)

- A-1: `VolumeRetirementWrapperTests.Retained_volumes_block_deploy_and_completion` never
  builds a receipt-less directory, so the clearable call site of the shared predicate
  (`exempt != 1 -> return 1`) has no witness; a mutant there survives CP-1/CP-2. The
  scratch base/tip probe above is the evidence for this review; add those cases.
- A-2: docs say "a finished lifecycle"; the code means `complete==true` with every child
  joined, which `c1609_phase_begin` also writes at phase start, so liveness rests on the
  owner check. Wording only.
- A-3: the charset filter rejects `password=x` but not `password x`; only fixed remote
  reasons emit the prefix today, so this is defence in depth.

## Preconditions before the operator-side Close on the real journal

Run from a clean checkout of the landed commit; all read-only on server2.

1. `ssh mc@server2 'ls -la /home/mc/antiphon-server2/retirement/'` — every entry is a
   directory matching `^c1008[0-9a-f]{32}$`, no symlinks, owner `mc`, no group/other
   write.
2. Receipt-less census, expect the 20 never-Ready previews and nothing else:
   `ssh mc@server2 'cd /home/mc/antiphon-server2/retirement && for d in c1008*; do [ -e "$d/receipt.json" ] || [ -L "$d/receipt.json" ] || { printf "%s " "$d"; jq -c "{p:.phase,a:.active,c:.complete,r:.readyPublished,o:.owner,k:[.children[].joined]}" "$d/lifecycle.json"; ls "$d"; }; done'`
   Each must show `r:false`, `c:true`, all `k` true, and no `preview*.json`,
   `closure.json` or `close-pending.json`; any `control-*.json` must have
   `purpose:"ready"`, never `"apply"`
   (`jq -r .purpose "$d"/control-*.json`).
3. Owner check for each receipt-less directory: `/proc/<owner.pid>` absent, or
   `awk '{sub(/^.*\) /,""); print $20}' /proc/<pid>/stat` differs from `owner.startTicks`.
4. Receipted siblings: `jq -c '{state,consumed,owner,effects:(.effects|length),intents:(.intents|length)}'`
   for every directory with a receipt; only `c100884f1e02a03b541118d329379ce16e480` may be
   `PartialRefused`, and its `sha256sum receipt.json` must equal the
   `-ExpectedJournalDigest` passed to Close.
5. `ssh mc@server2 'docker volume ls -q | grep -c "^antiphon-runner-temp_"'` prints 0.
6. No Preview, Apply or rollout is running:
   `ssh mc@server2 'ps -eo pid,args | grep -E "c1008|c590-remote|retire" | grep -v grep'` is empty.
