# CARD-1836 S2 Review, round 1 (Final/Full)

Review task `58f85687`. Subject is Code task `ff777e65-b170-43bd-9285-4ec06ce7e28a`, branch
`feat/card-task-ff777e65`, tip `9cf86d295e1e611f74ae19fb67b4de6f8b2eee62`, base/master
`cc48c70050a2932eddf4abe18cea8659d478c33e`. Scope: blockers and regressions only.

## Verdict

CLEAN. No blockers and no regressions. The advisories below go to cards and do not hold the land.

## Plan conformance

The Code brief commissioned "the existing S2 boundary IDs" from the landed plan
`2026-11-09-card-1836-incremental-rc.md` (S2, D-2, V-5..V-8, CP-2). The branch implements that
text: it parses `layers.txt` without editing it, assigns in order (lane path, first-rule literal
production glob, committed namespace prefix table, then `unassigned`), and computes
`b-{boundary}-{first16(SHA-256)}` over suite, boundary and ordinal-sorted `class/method` keys. It
does not pre-empt S3 to S8: there is no digest, packer, ledger predicate, status, launch order,
baseline or forced-Full code. CARD-1820 files are untouched.

The unlanded activation plan (`origin/feat/card-task-0f623d08:docs/superpowers/plans/2026-11-12-card-1836-incremental-activation-plan.md`, 2c7f3548a) S2a asks for more (A-1, A-2). Those are follow-ups for the S2a/S4a dispatch, not defects against the commissioned slice.

## Inertness

- `git grep` at the tip: only `scripts/test-rc-boundary.ps1` dot-sources `scripts/lib/rc-boundary.ps1`. No
  nightly, qualification, release-selection or startup script loads it, and no `scripts/lib` glob loader exists.
- The range changes no `scripts/nightly*`, `scripts/lib/nightly*`, release or rc launch script, `layers.txt`,
  `tests/test-execution-policy.json` or `tests/impacted-classes.json`. `-SelectionMode Full` source is byte-identical.
- The Windows native startup probe and rehearsal before an rc pick remain owed to the orchestrator.

## Determinism and collision resistance

- I recomputed the pinned id independently: `printf '%s' '["antiphon","core",["ATests/A","ZTests/B"]]' | sha256sum`
  gives `793b6fdbcbb93ce8`, which equals the test pin `b-core-793b6fdbcbb93ce8`.
- Input order, path separators (`\` folded to `/`) and suite/boundary/case are covered by witnesses (mutants M9 to M13 below).
- The id is truncated to 64 bits. There is no short-id collision rejection (A-1).

## Mutants (pwsh-only scratch copies of `git archive 9cf86d295`, one build-slot lease, repo untouched)

The tip control passed 4/4 cases. Every mutant was killed by the matching witness:

| M | Mutation | Killed by |
|---|---|---|
| 1 | prefix map `Antiphon.Tests.Domain` -> `git` | TwoBoundariesNeverShare (namespace fallback missing) |
| 2 | prefix match without the `.` boundary | TwoBoundariesNeverShare (namespace prefix boundary lost) |
| 3 | lane path override disabled | TwoBoundariesNeverShare (lane precedence x3) |
| 4 | `alwaysDirty=$false` | UnassignedStaysAlone (unknown is not dirty) |
| 5 | unknown suite -> `core` | UnassignedStaysAlone (unknown suite guessed) |
| 6 | final fallback `unassigned` -> `core` | UnassignedStaysAlone, TwoBoundariesNeverShare |
| 7 | mixed boundary accepted | UnassignedStaysAlone (unassigned joined core), TwoBoundariesNeverShare |
| 8 | mixed suite accepted | TwoBoundariesNeverShare (mixed suite accepted) |
| 9 | keys hashed in pack order | StableIdIgnoresOrder |
| 10 | suite dropped from hash | StableIdIgnoresOrder (suite omitted) |
| 11 | membership dropped from hash | StableIdIgnoresOrder, MovedClassChangesId (source/destination survived move) |
| 12 | class-only membership | StableIdIgnoresOrder, MovedClassChangesId (method omitted) |
| 13 | keys case-folded | StableIdIgnoresOrder (identity case folded) |
| 14 | literal order beats rule order | TwoBoundariesNeverShare (rule order lost) |
| 15 | single `*` crosses directories | TwoBoundariesNeverShare |
| 16 | duplicate membership accepted | StableIdIgnoresOrder |

PC-V-5..PC-V-8 stay pending for the method-scoped SourceLanding Mutation. This sweep does not discharge them.

## Checkpoint rerun at the reviewed SHA

Run `20261113-013448-0c7b` (`start --plan docs/superpowers/plans/2026-11-13-card-1836-s2-verification.md`):

- CP-2: 4/4 passed, slot granted, `sourceState=clean buildSource=verified`, executed 18.7 s.
- CP-11: 162 executed, 161 passed, 1 failed, executed 756 s. The failure is
  `DerivedImpactIndexTests.regenerate_script_reports_changed_then_unchanged`: "The application to execute does
  not exist: scripts/regenerate-derived-impact-index/bin/Debug/net9.0/Antiphon.DerivedImpactIndex.dll". The test
  runs the driver with `-NoBuild`, and a fresh worktree has not built it. The range does not touch the
  driver, its script or the test. The author's repair history records the same precondition red.
- CP-12: 2/2 passed. CP-13: 2/2 passed.
- Targeted rerun: I built the driver at native default output (`scripts/build-slot.ps1 -- dotnet build scripts/regenerate-derived-impact-index/Antiphon.DerivedImpactIndex.csproj -p:UseAppHost=false`, slot granted, 3m39s). Run `20261113-020437-8391` (ignored `.antiphon/review-58f85687-plan.md`, that one method) then gave 1/1 passed, `sourceState=clean buildSource=verified`, and `validate` printed `CHECKPOINT SOURCE VALID source=9cf86d29... rows=1`. The red is an environment precondition, not a branch regression.
- Receipt: run `0c7b` is RED, so `validate` refuses it (`report_failed`), and `reviewedSourceClean` is `false`. Every row has verified provenance at the reviewed SHA. Getting `true` needs one full rerun of the four rows now that the driver exists.

Guard union named in the brief (TestClassificationPolicy, TestClassificationGuard, SlowTestRegistryMerge,
SlowTestTripwire, NativeLinuxPolicyWitness): all inside CP-11, and all passed.

`scripts/check-evidence-diff.ps1 -BaseRef cc48c7005 -HeadRef 9cf86d295`: commits=5, entries=0, violations=0.
`git merge-tree --write-tree origin/master 9cf86d295` (master `cc48c7005`): exit 0, tree `aecb0c961`.

## Advisories (to cards; do not hold the land)

- A-1: the chunk id keeps only 64 bits and nothing rejects a short-id collision. The activation plan D-2 requires
  persisting the full digest and refusing a collision (S2a `MembershipAndCollision`). The S4a packer, or an S2a
  amendment, should return the full digest and refuse a collision.
- A-2: activation S2a says that conflicting literal evidence maps to `unassigned`. This slice applies
  first-rule-wins (the landed D-2 reading) and pins it ("rule order lost"). Membership keys are `class/method`,
  not the activation plan's `(suite, class, method, caseKey)`. Reconcile both in S2a/S3b.
- A-3: the canonical encoding uses `ConvertTo-Json`. Windows PowerShell 5.1 (JavaScriptSerializer) escapes
  `< > & '` as `\u00xx` where pwsh 7 does not, so a key containing one of those would hash differently under
  5.1. The library declares `#requires -Version 5.1`. Either pin a hand-built encoding or require pwsh 7. This is
  part of the owed native Windows proof.
