# Task bffd3e70: CARD-1545 S2a-2 environment attestation + declaration follow-up (Code, Final)

**Outcome:** S2a-2 is implemented red-first then green, shadow-only (no gate credit, state stays
DUE). CP-22 6/6, CP-31 7/7 and the CP-8 regression 7/7 passed through the checkpoint tool at the
green SHA, receipts valid, `buildSource=verified`. Review f5fcf533 follow-up (a) is folded in: the
real-tree fingerprint reasons drop from 200 to 2. No landing.

- Branch `feat/card-task-bffd3e70`, worktree `/work/worktrees/task-bffd3e70` (server2/Linux),
  base `ec25d5f1aaa90f38796b53cf7126fb36c015c932` (S2a-1 tip, Review f5fcf533 clean, not yet on
  master). Original Code task / landing owner: bffd3e70.
- Red `7d90e4a510b591217ea28814f5e29286ec4d4a6e`: tests, exporter, harness, fail-open stub,
  real-declaration witness.
- Green (tested SHA) `8a654ab70e87f2d45b92da4d34ef4fdfa06f0a28`: attestation library, fingerprint
  `environment` component, status `-Attestation`, declaration roots.
- Report commit: this file only (no source change after the tested SHA).
- Test design: `docs/superpowers/plans/2026-11-02-card-1545-S2a-S2b-test-design.md` (S2a-2 = CP-22,
  CP-31; V-8..V-13; R-3..R-6). Imported manifest (ignored): `.antiphon/s2a2-checkpoints/manifest.yaml`.

## What S2a-2 delivers

New: `scripts/lib/live-lane-attestation.ps1`, `scripts/test-live-lane-attestation.ps1`,
`tests/Antiphon.Tests/Scripts/LiveLaneAttestationTests.cs` (Integration,
`ParallelLimiter<ProcessSpawnLimit>`), `tests/Antiphon.Tests/Scripts/LiveLaneAttestationExport.cs`.
Changed: `scripts/lib/live-lane-fingerprint.ps1`, `scripts/live-lane-status.ps1`,
`tests/live-lane-inputs.json`, `scripts/test-live-lane-fingerprint.ps1` +
`LiveLaneFingerprintTests.cs` (one new case, `real-declaration`).
Unchanged against `10c8c2471` (`git diff --exit-code`): `rc-project-selection.ps1`,
`test-selection.ps1`, `tests/live-lanes.json`, `tests/Shared/LiveClaude/*`, every S1
`live-lane-*.ps1`, `run-live-lane.ps1`, `tests/test-execution-policy.json`, every csproj/props/targets.

- `Get-LiveLaneEnvironmentAttestation -Path -Lane`: strict `antiphon-live-lane-attestation-v1`
  (exact keys, typed bool/int, `schemaVersion` 1, laneId match) -> throws `attestation-invalid`;
  any credential-named key anywhere -> `credential-field-present` (value never read or echoed).
  Unknown reasons: `executable-drift|executable-mismatch|chain-unresolved|route-not-owned|
  observation-missing:<uid>`, `installed-target-unresolved`, `runtime-missing:<field>`,
  `settings-not-reusable`, `source-unreadable|source-unverified|source-missing:<kind>`,
  `source-unresolved`, `model-unknown`. Any reason -> whole document `unknown`, `digest=null`,
  `components=null` (no partial digest). Eligibility is recomputed from source states; the
  document's `reuseEligible` can only withdraw. Components `executable` (bytes, version, chain
  order/count, route, installed target; never path/PID/start/session), `runtime` (+ os/arch),
  `settings` (kind/state/projectionDigest, model, env tri-state), `account` (+ host enrollment).
- `Test-LiveLaneExternalFreshness` (D-6): default `AlwaysRerun` -> `policy-always-rerun`;
  `Bounded24h` needs epoch, age < 24 h (fixed; `-MaxAgeHours` throws `horizon-not-configurable`),
  skew 120 s, auth+control probes <= 60 s old bound to candidate nonce/host/account and the
  executed host/account, result `Ok`, no revocation by epoch or receipt id. Eligible result carries
  `d6-bounded-exception`, `externalProof=$false`. Nothing calls it with gate effect yet (S2b).
- `Get-LiveLaneFingerprint -Attestation`: adds `components.environment`; unknown attestation adds
  `environment-unknown`. Without `-Attestation` the S2a-1 document is unchanged (CP-31/CP-8 green).
- `live-lane-status.ps1 -Attestation`: validated before any Git read (exit 2 on schema fault);
  `-History`/`-ReusePolicy` still refuse `option-not-available:<name>` (S2b). State always DUE.
- Declaration (follow-up a): minimum roots + `tests/Antiphon.SessionRunner.Tests/`,
  `tests/Antiphon.PtyHost.Tests/`; runtime roots + `docker/`. `file.ps1`/`outside.ps1` left unknown.

## Checkpoints (closed list: CP-22, CP-31; plus CP-8 regression per brief)

Run `20261102-113306-5f17`, `--after S2a-2 --serial --total-timeout 30m --hard-wall-timeout 35m`, GREEN exit 0:

```text
CHECKPOINT CP-22 commit=8a654ab70e87f2d45b92da4d34ef4fdfa06f0a28 build=ok filter=/*/Antiphon.Tests.Scripts/LiveLaneAttestationTests/* executed=6 passed=6 failed=0 skipped=0 trx=/work/worktrees/task-bffd3e70/.antiphon/checkpoints/20261102-113306-5f17/rows/CP-22/run.trx slot=granted waited=0s dirty=0 source=8a654ab70e87f2d45b92da4d34ef4fdfa06f0a28 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-22 waited=0s executed=31.204s wall=31.239s
PHASES CP-22 slotWait=30s build=387.8942558s startup=3.0444287s testsWall=26.7472765s teardown=0.639886s hostWall=30.431594s
CHECKPOINT CP-31 commit=8a654ab70e87f2d45b92da4d34ef4fdfa06f0a28 build=reused filter=/*/Antiphon.Tests.Scripts/LiveLaneFingerprintTests/* executed=7 passed=7 failed=0 skipped=0 trx=/work/worktrees/task-bffd3e70/.antiphon/checkpoints/20261102-113306-5f17/rows/CP-31/run.trx slot=granted waited=15s dirty=0 source=8a654ab70e87f2d45b92da4d34ef4fdfa06f0a28 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-31 waited=15s executed=162.179s wall=177.395s
PHASES CP-31 slotWait=15s build=0s startup=7.3191166s testsWall=151.9857835s teardown=1.0598332s hostWall=160.3647335s
unlisted: none (the tool ran no other build or test command)
verdict: GREEN exit=0
```

Run `20261102-114533-6b56`, `--after S2a-1 --serial --total-timeout 25m --hard-wall-timeout 30m`, GREEN exit 0:

```text
CHECKPOINT CP-8 commit=8a654ab70e87f2d45b92da4d34ef4fdfa06f0a28 build=ok filter=/*/Antiphon.Tests.Scripts/LiveLaneFingerprintTests/* executed=7 passed=7 failed=0 skipped=0 trx=/work/worktrees/task-bffd3e70/.antiphon/checkpoints/20261102-114533-6b56/rows/CP-8/run.trx slot=granted waited=165s dirty=0 source=8a654ab70e87f2d45b92da4d34ef4fdfa06f0a28 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-8 waited=165s executed=280.024s wall=445.143s
PHASES CP-8 slotWait=165s build=545.9341715s startup=3.1545926s testsWall=275.471498s teardown=0.7146485s hostWall=279.3408822s
unlisted: none (the tool ran no other build or test command)
verdict: GREEN exit=0
```

`validate --evidence <report.json> --expected-source-sha 8a654ab7…` -> `CHECKPOINT SOURCE VALID`
rows=2 (CP-22,CP-31) and rows=1 (CP-8). Fresh TRX: CP-22 holds the 6 intended methods, CP-31 and
CP-8 the 7 fingerprint methods, all `Passed`. StableCanonicalInputs 44 s (CP-31) / 79 s (CP-8,
host loaded) against its 120 s budget; attestation methods 2-7.4 s each against 60 s.

YAML pins (ignored manifest): `slotWaitMinutes 15`, `rowMinutes 16`, `maxRows 1`, CP-22 8 min,
CP-31 10 min, CP-8 10 min. Deviation from the design's 4-min pin: CP-31/CP-8 measured 2m42s-4m40s
in S2a-1 and here, so 4 min would cancel on a loaded host.

**Red record at `7d90e4a5`** (not through the tool, to reuse one build): one gated build
(`c1545-s2a2-red-build`, waited 60 s, 4m24s) then `dotnet run --no-build` of
`LiveLaneAttestationTests/*` and `LiveLaneFingerprintTests/StableCanonicalInputs` (lease
`c1545-s2a2-red-run`, waited 105 s): attestation 6/6 methods failed, 44 rows = 41 FAIL + 3 PASS;
the 3 PASS are exactly the design's stub-green rows (`equal`, `volatile-only`, `bounded-fresh`).
Every red was an assertion row (no harness crash). StableCanonicalInputs failed on
`real-declaration` only (`reasons=200 sibling=198`), the other 9 rows passed.

## Other runs (explained unlisted)

- Green probe before commit (lease `c1545-s2a2-green-probe`, waited 75 s) against the red build
  (only scripts/JSON changed): attestation 6/6 passed; fingerprint 6/7 (real-declaration red
  because real-head reads committed HEAD and the declaration was still uncommitted).
- Real-tree probe at `8a654ab7` (lease `c1545-s2a2-real-probe`, 18 s): `live-claude-core-windows`
  status `unknown`, **reasons 200 -> 2** (`undeclared-runtime:file.ps1`,
  `undeclared-runtime:outside.ps1`), inventory 5229 -> 5455, closure 38, digest
  `b347cbaf8f2374f576e40828ea3c494978ac97cf38f07c82fb493b9872eda596`. State stays DUE.
- Land graph `scripts/audit-land-verification-graph.ps1 -Check -ToolPath
  tools/Antiphon.LandGraphAudit/bin/Debug/net10.0/Antiphon.LandGraphAudit.dll` (gated, twice: the
  first `tail` dropped the digest line): exit 0, **approved**, `digest=D92450D9AABCBF9C783B506F73DF06C45635F415D03E65C5F1A97AF415CC36BE count=69` (expected; no `-Update`).
- `git merge-tree --write-tree` clean onto `origin/master` `3a49b601…` and `origin/rc/2026-11-01`
  `e114313d…` (same tree `7725f6d2…`). Master-only feature; S2a-1 (`ec25d5f1`) is not yet on master.
- Unit lane: **not run, deferred to final, not passed.** New PowerShell plus two new Integration
  test files; no production C#, csproj, props, policy or shared helper changed; the land-graph pin
  is unchanged. Same reasoning as S2a-1 (about 67 min serial on server2, outside the 120-min brief).

## V / R outcomes

- V-8..V-13 (44 named rows: 5, 4, 5, 10, 9, 11) and R-3..R-6: passed in CP-22.
- V-1..V-7 and R-1..R-3: passed in CP-31 and CP-8 (StableCanonicalInputs now 10 rows).
- Scoped-rc rules: shadow-first (status DUE, `gateCredit=false`, reader untouched) and D-6
  (`always-rerun-default`, `bounded-fresh`, `expired`, `horizon-fixed`) witnessed here; stale/foreign
  receipt and V-14..V-19, R-7, R-8 belong to S2b and are not claimed.

## PCs pending for Mutation

PC-42..PC-82 (G-42..G-82, CP-22 methods) are pending, plus PC-1..PC-41 still pending from S2a-1.
Binding notes: G-63 (credential under `environment`) binds to `Assert-LiveLaneCredentialFree`
(global credential-name scan, ahead of the closed environment name set); G-47 is reachable through
`chain-shape`'s `[W]` vs `[W,W]` pair; G-54 any-row through `any-row` (row 6).

## Deviations from the test design

1. `chain-shape` also compares `[W]` with `[W,W]` so PC-47 (set ignoring count) can go red; the
   design's `[]` vs `[W]` alone differs even as sets.
2. Integration witnesses are folded into V-8 rows: `same-version-new-bytes` also asserts the
   fingerprint `environment` component equals the attestation digest and moves the lane digest;
   `pre-post-drift` asserts the fingerprint becomes `unknown` with `environment-unknown`. A minimal
   owned Git fixture lives in the attestation harness.
3. Exit-2 rows (`credential-free` forged, `string-boolean`, `schema` x2) run through the status
   script; `horizon-fixed` is an in-process throw (status has no reuse-policy option until S2b).
4. Attestation document carries `format` beside `schemaVersion`; route vocabulary is `owned`;
   path-search leaf is `cl`, `cl.exe|bat|cmd|ps1`.
5. Freshness API takes `-Executed @{epoch;host;account;receiptId}` for the executed side.
6. Follow-up (a): the sibling test trees are minimum roots, `docker/` is a runtime root; witness
   `real-declaration` asserts no docker/sibling reasons and both fixture strings still unknown (no
   count pin, so unrelated tree growth does not break it).
7. Row pins as above (8-10 min instead of 4).

## Open questions

- Q-A: `file.ps1`/`outside.ps1` keep the real lane `unknown` by design; a later slice decides
  whether test-argument literals outside `scripts/` count as runtime inputs.
- Q-B: `real-head` still makes two real reads (about 11 s each); a shared document cache is a
  performance follow-up only.
