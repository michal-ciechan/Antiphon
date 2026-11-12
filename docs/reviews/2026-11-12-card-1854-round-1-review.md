# CARD-1854 round 1 Review (bd1e6657)

Subject: feat/card-task-9617f2d7, Code landing owner 9617f2d7, base
75878de2367fa059a16c0184ef297fa1dc7d9681, reviewed tip
cf1a28cef8630cd99b8670ba21595d1a51a425f5 (4 commits). Round 1, Final/Full ordinary scope.

Verdict: CLEAN. No blockers, no regressions.

## Checkpoint rerun (run 20261112-153525-fa8c, server2, one build)

Review plan `.antiphon/review-1854-plan.md` (ignored), CP rows match the plan's
`### Checkpoints`; CP-3 split so the inherited LiveLane red is proved separately.

| Row | Filter | Result |
|---|---|---|
| CP-1 | NightlyScriptsTests/C1854_EolRepairHardening | 1/1 passed, testsWall 1.9 s (budget 20 s) |
| CP-2 | NightlyScriptsTests/* | 7/7 passed |
| CP-3a | (NightlyCoreTests*)\|(TestLaneCategoryGuardTests*) | 2/2 passed |
| CP-G | TestClassificationPolicy*\|TestClassificationGuard*\|SlowTestRegistryMerge*\|SlowTestTripwire*\|NativeLinuxPolicyWitnessTests* | 52/52 passed |

`validate --expected-source-sha cf1a28cef...`: CHECKPOINT SOURCE VALID rows=4,
sourceState=clean, buildSource=verified, slot=granted on every row, unlisted: none.
Build 309.6 s cold.

CP-3 LiveLaneRunnerTests: run on a local, unpushed scratch merge of origin/master
077f0b3a7 with the reviewed tip (merge tree clean): LiveLaneRunnerTests 4/4 plus
NightlyScriptsTests 7/7 = 11/11 passed (build-slot gated build + `--no-build` run).

## Red before, green after

Harness `scripts/test-nightly-run.ps1 -Case C1854_EolRepairHardening` from
`git archive` copies (no build):

- S1 393e6f5fe (test, no fix): FAIL "mixed repair completes despite missing decoded
  path" (checkout-index exit 1), FAIL "checkout receives only the existing unquoted
  path" (argv carried the CP437-mangled `unicode-` name), FAIL "mixed repair logs two skipped paths".
- Tip cf1a28cef: 11/11 PASS.

The assertions compare real Git byte state, checkout argv and log text; none is a
self-compare or constant.

## Contract checks

- No data loss: the change only removes candidates from the delete/checkout batch.
  Remove-Item is still guarded by Test-Path -LiteralPath -PathType Leaf and only runs on
  paths that git reported i/lf with an explicit eol attribute contradicted by the work
  tree. A path skipped as missing is never passed to Remove-Item or checkout-index. A
  mis-decoded name could only hit another file if it equalled that file's exact name; the
  repair runs after reset --hard and before clean -fdx, which already discard dirty and
  untracked state in the owned clone, so no new loss class exists.
- Quoted-path counting moved inside the stale predicate, so only stale quoted paths are
  counted (no inflation from clean quoted paths).
- ASCII-only: scripts/lib/nightly-run-impl.ps1 and scripts/test-nightly-run.ps1 contain
  no non-ASCII byte (fixture names are built from [char] codes). `[ref]`, `::new` and
  GetEncoding(437) are PS 5.1 compatible.
- Log consumers: the only readers of `nightly sync:` are the new C1854 assertions; no
  parser in scripts, server, client, tests or docs consumes the line.
- check-evidence-diff 75878de23..cf1a28cef: commits=4 entries=0 violations=0.
- merge-tree --write-tree origin/master (077f0b3a7) HEAD: clean (36c250030).

## CARD-1855 answer (LiveLaneRunnerTests.WrapperOnlyPaidManifest)

- Branch delta touches none of the six protected inputs (nightly-common, nightly-policy,
  nightly-coverage, nightly-core, test-execution-policy.json, c487-probe discovery.diag).
- `-PrintInputDigest`: branch tip b18c4eea... (pin ba3268c2, so red at the base);
  master+branch merge 46d26934... equals master's new pin.
- WrapperOnlyPaidManifest green at master 077f0b3a7 + branch (LiveLaneRunnerTests 4/4).
- CARD-1855 can be closed: the red was the stale pin fixed by 077f0b3a7; landing rebases
  onto it.

## Pending (not discharged here)

- R-1: native Windows PowerShell 5.1 run of C1854 and C1313 and `ls-files --eol` timing
  on the reused clone C:\Antiphon\releases\checkout. Linux seam-forced decoding is not
  native credit.
- PC-1..PC-3 stay pending for method-scoped SourceLanding Mutation.

## Advisories (do not hold the land)

- None new.
