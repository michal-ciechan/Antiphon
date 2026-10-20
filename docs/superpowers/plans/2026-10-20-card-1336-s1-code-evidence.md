# CARD-1336 S1 Code evidence

S1 is implemented and its commissioned ordinary verification passes. The inert
RC wrapper explicitly passes `-Ref rc/2026-10-18`; own/missing/foreign schedule
provenance, job forwarding and SSH exit status are preserved. S2 is not part of
this dispatch. Master-only. Restart: none. No release gate credit is claimed.

Original Code task / landing owner: `c6cc11da-0032-48b5-b38b-7fc879c4c1fa`.
Branch: `feat/card-task-c6cc11da`.
Worktree: `/work/worktrees/task-c6cc11da`.
Assigned base: `adc753e748c315f72555467afcc27277ef881722` (CARD-1339 S3-S5).
Test-only red commit: `262d8a11ea4a17e77e7ff7aafeedd4bb99d99d02`.
Fixed production/test commit: `b56091fcfd77528b5d17729b82041a7a774f2c2d`.
Both commits were pushed before their checkpoint runs. The final report commit
changes only this Markdown evidence; its full SHA is in the caller report.

## Scope and changes

The brief explicitly commissions the first independent slice and only its red
and narrow green checkpoints. It does not commission S2, a whole Unit lane,
full affected classes, a full assembly or any actual release profile. The
verification profile's generic Final scope is narrowed by this explicit brief
and the plan's closed list. This report claims completion of S1 only.

- Imported the committed Plan/TestDesign from
  `80daf82f9b91e7f9f4f0790427a59f01796ea60d` on the card's TestDesign branch
  `feat/card-task-6386e779`; it was absent from the assigned base and master.
  Plan: `docs/superpowers/plans/2026-10-19-card-1336-release-gate-followups-plan.md`.
  Added the TUnit trailing wildcard to the CP-1 and CP-3 method filters without
  changing their selection/counts, and recorded this dispatch's S1 boundary.
- Changed `scripts/windmill/antiphon-release-candidates.json` to pass the literal
  ref. Its description and `scripts/windmill/README.md` explain the explicit pin
  and rollover review. The definition remains inert/unregistered, the checked-in
  schedule remains disabled, and scheduled qualification is not claimed.
- Added `tests/Antiphon.Tests/Scripts/ReleaseGateFollowupTests.cs` and
  `tests/fixtures/card1336-release-gate-followups.ps1`. The Integration test uses
  `ScriptHarness` and `ParallelLimiter<ProcessSpawnLimit>` with unchanged
  300-second invocation and 10-second cleanup budgets.
- Added the unbound Mutation candidate at
  `docs/superpowers/mutations/2026-10-20-card-1336-release-gate-followups-candidate.md`.

The fixture evaluates the actual decoded production Bash bytes. A readonly
`ssh` function captures NUL-delimited arguments before the body is sourced;
child PATH contains only an empty owned directory. No real SSH command can be
resolved through it. Bash is awaited and cleaned up, with its own 30-second
operation deadline; there is no host-wide speed assertion. Launcher errexit is
cleared before sourcing, preserving reachability of the production `exit $?`
guard. Frozen independent Git blob IDs check the master wrapper and schedule.

No publish/promote/release-cut entrypoint was executed, directly or indirectly.
No real tag was created, pushed, moved or deleted. No live Windmill operation,
registration, release, deployment, rc mutation or external report delivery ran.

## Checkpoints and ordinary invariants

Both rows used the checkpoint tool, their exact committed expected SHA,
`--row-timeout 40m`, a fresh isolated build and the plan's exact filter. Every
foreground run was awaited through terminal exit. No source changed in flight.

| Row | Run | Actual TUnit executed/pass/fail/skip | Result | Build seconds | Host test wall seconds | Receipt wall seconds | Build / row slot and wait |
|---|---|---|---|---:|---:|---:|---|
| CP-1 | 20261020-061606-4c42 | 1 / 0 / 1 / 0 | Expected behavioral red, exit 1 | 348.204 | 12.856 | 380.616 | granted / granted; 0s / 0s |
| CP-2 | 20261020-062426-032a | 2 / 2 / 0 / 0 | Green, exit 0 | 235.357 | 12.177 | 262.613 | granted / granted; 0s / 0s |

CP-1 failed exactly at `C1336 W own-0`: the captured production command had
`Ref count=0`, while its schedule slot, job id and exit status were available.
This was not a setup failure, timeout or zero-count selection. Its historical
red stays red; no deliberate mutant was run.

CP-2's fresh TRX contains exactly
`Antiphon.Tests.Scripts.ReleaseGateFollowupTests.C1336_WrapperRef` and
`Antiphon.Tests.Scripts.ReleaseGateRegistrationTests.C599_ScheduleProvenance`,
both Passed. The harnesses validate 9 + 16 = 25 named internal rows, not 25 TUnit
executions. W covers own/missing/foreign schedule context at status 0 and 23,
schedule/schema properties, frozen master blobs and pin/status documentation.
The existing collateral registrar fixture is offline.

| ID | Actual outcome |
|---|---|
| V-1 | Passed at CP-2 after the decisive CP-1 missing-Ref red. |
| R-1 | Passed at CP-2: all nine W rows and the 16-row S collateral method. |
| V-2 | Not run: S2 admission identity is outside this dispatch. |
| V-3 | Not run: S2 local child/selection/state boundaries are outside this dispatch. |
| V-4 | Not run: S2 isolated candidate state and credit are outside this dispatch. |
| R-2 | Not run: S2 identity/admission regression selection is deferred. |
| R-3 | Not run: S2 identity propagation regression selection is deferred. |
| R-4 | Not run: S2 isolation/credit regression selection is deferred. |

CP-3/CP-4 and the six S2 IDs above remain for the later S2 Code dispatch and its
ordinary Review; they are not passed or discharged by this scoped result.
No required S1 manual/live operating acceptance was omitted: S1 acceptance is
the offline wrapper/configuration contract. Native qualification is excluded.

## Provenance, cost and cleanup

Read current `GET /api/runner-defaults` and `GET /api/session-runners`; no runner
or OS was pinned and no fleet location was embedded in the change.

The only build outside the checkpoint rows was the required tool bootstrap:

```sh
pwsh -NoProfile -File scripts/build-slot.ps1 -Label c1336-tool-bootstrap -- dotnet build tools/Antiphon.Checkpoints --property:OutputPath=bin-c1336-tool/ --nologo
```

It passed, 4 existing compiler warnings and 0 errors, `slot=granted waited=0s`,
held about 7s. The checkpoint launchers then invoked that DLL directly, with no
implicit build. Each executor acquired its own build and test slots. No other
build/test driver, warm-up, repetition or flake retry ran.

Completed receipt wall cost is 643.229s (10.72 minutes), plus the approximately
7s bootstrap and launcher/cleanup overhead. This is measured checkpoint cost,
not the whole authoring stopwatch or a claim of savings. The manifest declares
four builds, but only one selected build ran per checkpoint; CP-3/CP-4 were unused.

Both report Markdown digests match their JSON receipts. Fresh TRX identities
and counters were inspected independently. Start/end commit and fingerprints
match with `dirtyFiles=0`; the producing builds have verified source provenance.
The green validator returned:

```text
CHECKPOINT SOURCE VALID source=b56091fcfd77528b5d17729b82041a7a774f2c2d rows=1
```

Raw ignored receipts, TRX, source/build provenance and logs remain under:

- `/work/worktrees/task-c6cc11da/.antiphon/checkpoints/20261020-061606-4c42/`
- `/work/worktrees/task-c6cc11da/.antiphon/checkpoints/20261020-062426-032a/`

The expected-red run retained its output (`still-present`, exit remained 1).
After its executor ended, its exact report-owned
`tests/Antiphon.Tests/bin-c1336-cp1` was removed before CP-2. CP-2 cleaned its own
output and both executors' tool shadow copies were removed. The two known
bootstrap output directories were removed after terminal completion. No
`bin-c1336-*` output remained. No receipt-refusal rerun was necessary.

`scripts/check-evidence-diff.ps1` passed over the full assigned base through the
fixed tip: 2 commits, 0 evidence entries, 0 violations. It is run again over the
full base..HEAD after committing this report. The final caller report records
that result and the published tip.

`git fetch origin master` resolved master to
`6ec71d427574f3a1b74bfbe2042e0ea9a819d191`.
`git merge-tree --write-tree origin/master HEAD` passed without conflicts at
the fixed source, producing tree `311be6be7ee84290f433f480f0b889b218b4baf4`.
The final report-only tip is checked again before settlement. No merge was made.

## Unedited checkpoint lines

```text
CHECKPOINT CP-1 commit=262d8a11ea4a17e77e7ff7aafeedd4bb99d99d02 build=ok filter=/*/*/ReleaseGateFollowupTests/C1336_WrapperRef* executed=1 passed=0 failed=1 skipped=0 trx=/work/worktrees/task-c6cc11da/.antiphon/checkpoints/20261020-061606-4c42/rows/CP-1/run.trx slot=granted waited=0s dirty=0 source=262d8a11ea4a17e77e7ff7aafeedd4bb99d99d02 sourceState=clean buildSource=verified
CHECKPOINT CP-2 commit=b56091fcfd77528b5d17729b82041a7a774f2c2d build=ok filter=/*/Antiphon.Tests.Scripts/(ReleaseGateFollowupTests*)|(ReleaseGateRegistrationTests*)/(C1336_WrapperRef*)|(C599_ScheduleProvenance*) executed=2 passed=2 failed=0 skipped=0 trx=/work/worktrees/task-c6cc11da/.antiphon/checkpoints/20261020-062426-032a/rows/CP-2/run.trx slot=granted waited=0s dirty=0 source=b56091fcfd77528b5d17729b82041a7a774f2c2d sourceState=clean buildSource=verified
```

## Handoff

Review S1 at the final branch tip, inspecting CP-1's historical witness and
executing the plan's CP-2 selection with the exact reviewed SHA. Bootstrap the
tool through the gated command above, then:

```sh
dotnet tools/Antiphon.Checkpoints/bin-c1336-tool/Antiphon.Checkpoints.dll run --plan docs/superpowers/plans/2026-10-19-card-1336-release-gate-followups-plan.md --rows CP-2 --expected-source-sha <reviewed-sha> --row-timeout 40m
```

All PC-1..PC-42 and every listed variant remain pending for independently bound,
method-scoped SourceLanding Mutation. The candidate records the implemented S1
anchors and deferred S2 bindings. Mutation owns deliberate red/restore/green and
missing-control discovery. Ordinary green is not PC-clean.

The caller retains CARD-1339's exact-ref landing/evidence dependency and must
commission S2 separately. Master landing does not update an rc-hosted wrapper or
core; use the plan's conditional backport table. After ordinary Review the caller
lands this original Code task and commissions the applicable SourceLanding
Mutation. Next: review (S1 only). Restart: none.
