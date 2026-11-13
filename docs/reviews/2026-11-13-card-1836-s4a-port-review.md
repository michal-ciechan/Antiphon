# CARD-1836 S4a port review, round 1 (Final)

Review task `53820e0f`. Subject is the port Code owner
`063b81c6-63d9-408a-853c-681772c288aa`, branch `feat/card-task-063b81c6`,
tip `4a4af070afe7fc3e4e537347095efa71c7b073a8` (12 commits over
`2349a1938b8ff11829ca6becf3c4719383bf9e52`). The combined tree below used
master `fac5cc2de89cc82eb39e85cd2bf6c4df18d55ac6`. The land check is the
later `origin/master` `ec5de7348fe327ed1fb7c67ffb2939098f548dbd`. CARD-1871
round 2 is `39b59824100b95992ccbc3b7b47a9c56815911ed` on
`feat/card-task-5463c5eb`.

**Verdict: CLEAN.** No product defect. Positive controls and the native
Windows proof stay pending. `reviewedSourceClean` is false because these
runs are build-slot console results, not checkpoint-tool receipts.

## Combined tree

Scratch worktree `/tmp/c1836-s4a-combined-53820e0f`, detached tip
`3e3c90e4f889ff918f786340d22fecafd442ef81`. Built the way Review
`57edd89d` built the CARD-1871 cherry-pick: `origin/master` plus the 14
commits `git cherry` marks `+` through `39b598241` (0 conflicts; the S2
library, harness, and `RcBoundaryChunkIdTests` match `39b598241`), then
the 12 port commits. The only port conflict was `tests/derived-impact-index.json`.
That side was the port index. Regeneration then reported
`derived-impact-index changed` (31 Windows-only, 1 Linux-only; 28 hunks,
44 insertions, 21 deletions). Those stale lines are the master delta.
`DerivedImpactIndexTests` ran after regeneration.

Packer and S2 files at that tip match `4a4af070` and `39b598241`.
`BoundaryAware` is referenced only by the packer and its two test scripts.

## What was verified

- Pack harness, all 5 cases, exit 0. S2 harness, all 11 cases, exit 0.
  `OrdinalSortAndSingleDigest` printed `BOUNDARY TIMING count=800
  order=random seconds=0.122901` and `order=reversed seconds=0.090346`.
- Collision keys `10788987499786589518` and `17490688019260441962` share
  `b-core-360a4ef28b10a96c`. The packer keeps both launches, marks both
  unassigned, dirty, and collision, and assigns distinct 77-character ids
  `b-unassigned-` plus the 64-hex digest. A non-collision id stays 16 hex
  (`b-core-c1884586e261a25a`, length 23). The default path stays
  `all-group-1` with no `boundary` property.
- The packer does not hash. `Complete-NightlyBoundaryLaunchGroups` copies
  `chunkId`, `fullDigest`, `caseKeys`, and the collision flags from
  `Resolve-RcBoundaryChunks`. `ConvertTo-Json` remains only as the
  exact-method UID equality check and the case-manifest dictionary key.
- Mutants, one slot each, then the file restored (`git diff` empty):
  JSON digest again fails `CanonicalDigestIncludesCaseKeys` with
  `boundary-pack: ID/digest mismatch`. Dropping case keys fails that case
  with `boundary-chunk: duplicate membership across chunks`. Throwing on
  collision fails `ShortIdCollisionUsesS2Policy` with `collision threw`.
  Dropping the `git` boundary and forcing every mapped class to `core`
  each exit 1 on `TwoBoundariesAndPrefixCollisionsStaySeparate`.
- Combined `Antiphon.Tests` build: 0 errors, 2053 warnings, 4m 03s.
  One leased run, 8m 03s: 191 passed, 0 failed, 0 skipped.
  `RcBoundaryPackTests` 5, `RcBoundaryChunkIdTests` 11, `RcForcedFullTests` 3,
  `NightlyCoreTests` 1, `LiveLaneRunnerTests` 4, `TestClassificationPolicyTests` 42,
  `TestClassificationGuardTests` 1, `SlowTestRegistryMergeTests` 5,
  `SlowTestTripwireTests` 2, `NativeLinuxPolicyWitnessTests` 3,
  `DerivedImpactIndexTests` 27, `ReleaseImpactEnvelopeTests` 4,
  `MechanicalPcContractTests` 11, `TaskPlatformGuidanceTests` 5,
  `C1729LayeringGuardTests` 9, `TestLaneCategoryGuardTests` 1,
  `ProcessSpawnLimitTests` 57.
- Real-tree census on that assembly (`assemblySha256`
  `1a7caba2506e663e6cd50c0da6892336ef1bda80fc5e3c2c6f048cd71f623a41`):
  1677 classes, 23165 cases, 184 chunks, `selectedExactlyOnce` 23165,
  `optOutByteIdentical` true, `optOutBytes` 727779. Boundary pack 18.46s.
  The proof script's own filter walk was 467.56s. No mixed boundary and
  no duplicate id. The class count is this tree's universe; master is
  ahead of the author's 1672.
- At `4a4af070`, leased `Antiphon.Tests` build 0 errors in 6m 43s, then
  68 passed, 0 failed, 0 skipped in 2m 21s: packer 5, chunk ids 11,
  policy 41, guard 1, slow-merge 5, tripwire 2, native witness 3.
  `Antiphon.Agents.Pty.Tests` `ProcessSpawnLimitTests` 2/2.
  `Antiphon.SessionRunner.Tests` `ProcessSpawnLimitTests` 2/2.

## Land graph

Fetched `origin/master` is `ec5de7348fe327ed1fb7c67ffb2939098f548dbd`
(`fac5cc2de` is an ancestor). `git merge-tree --write-tree origin/master
4a4af070` exits 1 and records tree `f25115391603c04e23ce97f464340a49ffd8e797`.
Three conflicts:

- add/add `scripts/test-rc-boundary.ps1`. Master matches `39b598241`
  (`6eb5df230be57932590611e37618c1f24d492446`, 5s bound). The port tip still
  has the `2349a1938` harness (`6bdd95214c821b1743cf65941c72f6e02851b428`,
  1s bound). Keep master's file.
- add/add `docs/superpowers/plans/2026-11-13-card-1871-boundary-hardening.md`.
  Master matches `39b598241` (`9a120be98d4b0cbdfed6f6627f48edd7b97571eb`).
  The port tip still has the round-1 plan. Keep master's file.
- content `tests/derived-impact-index.json`. Master is `e2d052ee9cf72b3c7188987172292de1f8f2d1a5`
  (moved again after CARD-1820). The port index is
  `c450604c50f0de6b09969b7cd0ac3afeb693931c`. Regenerate; do not hand-merge.

`scripts/lib/rc-boundary.ps1` (`77402503d33843d5e178afa80f7c52baf6f9e86a`) and
`RcBoundaryChunkIdTests.cs` (`d5e94e58b9f5de57501f5813c9c6d86b27bfd10a`) match
on master, `39b598241`, and the port tip, so they are not conflicts. The
packer files auto-merge. No master commit after `f6c8a388b` touches those
boundary files, so the combined-tree library and harness bytes are the bytes
on this master. Evidence diff `2349a1938..4a4af070`: 12 commits, 0 violations.
`impacted-classes.ps1 -MergeSubset` reports `migrations=False` (downgrade
selector not selected). Kinds: nightly-live-lane, spawn-limit-rosters,
test-lane-category. No OS-gated production change. The four scripts checked
are ASCII. Restart: none.

## Still pending

PC-V8-boundary, PC-V8-prefix, PC-V8-case-membership, PC-V8-default-ID,
PC-V8-canonical-case-key, and PC-V8-S2-collision stay pending for
method-scoped SourceLanding Mutation. The operator waiver does not pass
them. Native Windows startup and packing proof stays owed. CARD-1873 owns
the packer's performance follow-up.
