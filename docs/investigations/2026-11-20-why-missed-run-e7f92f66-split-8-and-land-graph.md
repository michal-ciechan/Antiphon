# Why missed: run e7f92f66 split-8 statement budgets and land-graph drift

One committed note for rc qualification run e7f92f66, chunk `antiphon-all-split-8`
(94 of 97 passed on the desktop). Two causes share this file because the same
Debug task owned both. Path:
`docs/investigations/2026-11-20-why-missed-run-e7f92f66-split-8-and-land-graph.md`.
Owner: [why-missed write-up](../orchestration-loop.md#why-missed-write-up-card-1781).

Debug task `ebb01ffc`. Base master `6cf3a2680e08321509cf9fbc4d587474381a6fd4`.
The live Windows qualification run `34d41b7e` was not touched. Linux reproduced
the statement counts; the desktop was not used.

## Cause A — statement budgets

### Introducing commit/card

introducing commit/card: `540761e846c487b1607d4802476f760c2ba43e4a` (CARD-1987)

Author antiphon-server2-runner, 2026-11-18 20:02:14 +0000,
`fix(session): CARD-1987 write activity in one statement`. The only file is
`server/Application/Services/AgentSessionRuntime.cs`. `RecordActivityAsync`
dropped the session `FirstOrDefaultAsync`, the streaming-attempt
`FirstOrDefaultAsync`, and the following `SaveChangesAsync`, and replaced them
with one `ExecuteSqlInterpolatedAsync` tagged `-- rt.activity.write`.

The three failing windows all call `AwaitBudgetBackgroundCompletionAsync`,
which waits until `LastSeenAt` is written by that method. Three statements
became one, so each window lost exactly two commands:

| Action | Pinned before | Linux and desktop actual |
|---|---:|---:|
| running-recovery-existing-brief | 72 | 70 |
| running-recovery-missing-brief | 67 | 65 |
| watchdog-first-recovery | 67 | 65 |

Three Linux runs of
`/*/*/DelegationDispatchRecoveryBoundaryTests/C1149_C1150_Statement_budgets*`
before the pin failed only those three rows, each time at 70/65/65. The other
eleven budget rows stayed green. Each of the three SQL dumps contains exactly
one `-- rt.activity.write`. The count does not depend on OS. The last pin
change before this task was `d90a3d4a539a615ae0855f8e3afeda9bb12dbe39`
(2026-11-14, CARD-1917 safety reads). Nothing between that commit and
`540761e84` retouched these three numbers.

Exact equality stays. A later drop can be a missing safety read (CARD-1574
owner/generation reads, CARD-1892 uncertain-attempt and composer-less hold
probes). The failure message already prints the roster, and those three
actions also write the full command list. This repair only moves the three
pins to 70/65/65, in `af6d5b33a9b6374fe323f1d484ed38b63e0b81f7`. The green
rerun of the same 14 arguments passed 14, failed 0, skipped 0.

### Approving reviews

approving review(s) with scope (Full/Interim) and the selection they actually ran:

- Review task `e56b2b08`, scope Full (Final, round 1, verdict CLEAN), Code
  task `cd7b1a6e`, reviewed range
  `997f9b8d9ffc4ba870d2fd56caf7c03781591624..e61b5f6dc5b7d4e89ae283a6e43821ec25a7d5a6`.
  Artifact `b68bb2054028c9c598313c122b6edb5c6b395976` (the review markdown is
  not an ancestor of this master). Selection that ran:
  - CP-3 red then green: `AgentSessionRuntimeActivityTests/C1987_activity_write_is_one_command` (the one-command activity witness itself)
  - CP-2: `RoutingBindingCacheTests` and `PhoneHomeSessionRoutingTests` (executed 9)
  - CP-4: `AgentSessionRuntimeActivityTests` and `RunAttemptStallDetectorTests` (executed 4)
  - CP-G: `TestClassificationPolicy*`, `TestClassificationGuard*`, `SlowTestRegistryMerge*`, `SlowTestTripwire*`, `NativeLinuxPolicyWitnessTests*` (executed 53)

  Closed rows in `docs/superpowers/plans/2026-11-18-card-1987-checkpoints.md`
  are those activity, routing, and stall filters. They do not name
  `DelegationDispatchRecoveryBoundaryTests`.

### Outside the selection

why the failing test was outside that selection (lane/category/path): Integration,
`tests/Antiphon.Tests/Application/DelegationDispatchRecoveryBoundaryTests.cs`,
method `C1149_C1150_Statement_budgets`. It is outside every row the review ran.

- The guard union does not include this class. CP-G was the five classification
  and policy classes (53 results).
- `tests/impacted-classes.json` kind `statement-budgets` globs only
  `server/**/WorkspaceReservationJournal.cs` and `server/**/*Dispatch*.cs`, and
  its method pin is `DelegationDispatchRecoveryBoundaryTests/C1149_C1150_Statement_budgets`.
  `AgentSessionRuntime.cs` matches neither glob, so the method pin is not selected
  when the activity write changes.
- `tests/derived-impact-index.json` lists classes for
  `server/Application/Services/AgentSessionRuntime.cs` and does not list
  `DelegationDispatchRecoveryBoundaryTests`. The same index does list that class
  for `AgentSessionService.cs`. A derived-impact selection on the file CARD-1987
  edited therefore skips this class.
- The class is about 382 results and about 3505s of summed cost, width 4, and
  cannot fit one 570s Merge run. A whole-class derived edge would not be a
  legal short witness. The selector has to stay on the budget method.

### Cheapest pre-land check

the cheapest pre-land check as a path-glob -> class-filter entry for the impacted-classes table of CARD-1782:

`server/**/AgentSessionRuntime.cs` -> `/*/*/DelegationDispatchRecoveryBoundaryTests/C1149_C1150_Statement_budgets*`

Add that glob to the existing `statement-budgets` kind (the method pin is already
there). Do not add the class to the derived-impact edge for `AgentSessionRuntime.cs`:
that would select all 382 rows. This note proposes the glob. It does not add it.

### Exemption

Related, not the same introducing commit:
`docs/investigations/2026-10-26-split-8-dispatch-budgets.md` recorded the
CARD-1574 increase on this same assertion. This cause is the CARD-1987
decrease. First note for the one-statement activity write: none.

## Cause B — land-graph digest

### Introducing commit/card

Two landings, in order. The recorded digest was already false on master before
the definition file the brief names.

1. Refresh computed on the pre-rebase branch, then transplanted.
   `25acf9623c48132b15550fbf7f27190dd3875593` (antiphon-server2-runner,
   2026-11-20 02:45:17 +0000, `chore: refresh land-graph records for the landing batch`)
   on `origin/feat/card-task-b81aa118` wrote digest
   `DA2C126C08AFE2EF68FC7E57C01085F40474AB21DE7B9DA5D1F906D13F879820`, count 80.
   That digest matches the definition fingerprint of `25acf9623` and of its
   parent `83473d09db0b7d21112350cc0d83d3a2eca70352`. It is not an ancestor of
   master.
   Master commit `6acc0326efa51e1fa0bc2e0d32ccfad4235bc202` (2026-11-20 03:28:32,
   same subject) replays that manifest and evaluation onto parent
   `c077b918357bacbfab91bd30e9b54ff145a39c7b`. The parent's definition
   fingerprint, and `6acc0326e` itself, hash to `2DA6404C` (count 80), not to
   `DA2C126C`. `git diff-tree 6acc^..6acc` is only the two record files. A
   transplanted manifest is not a refresh of the tree it landed on.
   Definition files that differ `25acf9623..c077b918` and are the rebase delta:
   - `b2d76659060085f9df3a9720441a2bb82159a645` (2026-11-20 03:16:09) changes `src/Antiphon.GitInProcess/Antiphon.GitInProcess.csproj` and `src/Antiphon.HostCleanup/Antiphon.HostCleanup.csproj`
   - `869fe194e9cbe1c98df7cd420cb3e5686103741e` (2026-11-20 03:16:09) changes `src/Antiphon.GitInProcess/Antiphon.GitInProcess.csproj` and `tools/Antiphon.Checkpoints/Antiphon.Checkpoints.csproj`

2. The definition file that changed after that master refresh.
   `b55b31fbb530611f5dbf385cf77c0a0d0568dc45` (Mike Ciechan, 2026-11-20 04:23:19 +0000,
   `perf(checkpoints): answer run-checkpoint.ps1's tracked-path guards from the index`)
   adds a `Compile Include` of `scripts/lib/CheckpointIndexPaths.cs` to
   `tests/Antiphon.Tests/Antiphon.Tests.csproj`. No land-graph refresh commit
   exists after it. The live fingerprint moves from `2DA6404C` to
   `EA552D856F649EEDF024C84E76676F00D5547BEF4B1BC77573ABF5BF6B43C7CC` (count 80)
   at that commit and stays there through master. `LandGraphPrecheck.cs` and
   `scripts/land-graph-land-refresh.ps1` are unchanged from `6acc0326e` to that
   master, so this is tree content, not an algorithm change.

### Why nobody refreshed it

[Land-graph refresh](../orchestration-loop.md#land-graph-refresh) (operator
2026-11-07, CARD-1753; the rule cites the 2026-11-05 investigation
`docs/investigations/2026-11-05-investigate-land-graph-gate-value.md`) tells
Merge and Review to warn and continue when the mismatch predates the batch.
It is not a blocker and it is not a silent refresh. The batch that itself
changes a definition record is supposed to run
`scripts/land-graph-land-refresh.ps1` and never ask.

That script refuses `outside-batch` when `-Base` fingerprints to something
other than the recorded digest and any definition file differs from Base to
HEAD. `-Base 58e005fbc60a193c4ca0a22648e0c41c8ee58627` (parent of `b55b31fbb`,
fingerprint `2DA6404C`) refused for that reason. A lander who points the
script at the master parent of their csproj edit cannot repair a digest that
was already transplanted. The inherited-mismatch rule then lets the land
continue. `LandVerificationPolicy` uses the digest as the docs-only build
skip, not as a hard refuse of a code candidate, so a skipped refresh still
lands.

Short merge verification (2026-11-18) runs `-Check` and does not itself own
the refresh of an inherited mismatch. After `6acc0326e` every later batch saw
a warning and no batch owned the repair. This Debug task is that owner.

The base the script accepts is one whose definition fingerprint equals the
recorded digest. `25acf9623c48132b15550fbf7f27190dd3875593` does. The refresh
commit on this branch is `15503f6ca8a5b2ffbd3cfb5f5d485c6ba4e02ca5`.

| | Digest | Count | Pins |
|---|---|---:|---|
| Recorded before | `DA2C126C08AFE2EF68FC7E57C01085F40474AB21DE7B9DA5D1F906D13F879820` | 80 | `ffcbf900e17b4e3f482872aa055c3428549ac0e9` |
| Recorded after | `EA552D856F649EEDF024C84E76676F00D5547BEF4B1BC77573ABF5BF6B43C7CC` | 80 | `ffcbf900e17b4e3f482872aa055c3428549ac0e9` |

Changed records (definition paths whose blob differs `25acf9623..HEAD`; the
commit itself rewrites only the manifest and evaluation digest lines):

- `src/Antiphon.GitInProcess/Antiphon.GitInProcess.csproj`
- `src/Antiphon.HostCleanup/Antiphon.HostCleanup.csproj`
- `tests/Antiphon.Tests/Antiphon.Tests.csproj`
- `tools/Antiphon.Checkpoints/Antiphon.Checkpoints.csproj`

### Cheapest pre-land check

Do not build these. Proposed owner:

- The landing batch that changes a definition file already owns the refresh.
  Merge should refuse that batch unless a `land-graph-land-refresh.ps1` commit
  is in it. A warning is the wrong outcome for the batch that moved the digest.
- A rebase that replays an existing refresh commit onto a parent whose
  definition fingerprint differs must re-run the script before land. Keeping
  the digest text is not a refresh.
- Once a mismatch is inherited, the next master-bound fix that sees `-Check`
  fail owns the refresh (this task). Warn-and-continue with no owner is how
  `DA2C126C` survived from 03:28 through the csproj edit at 04:23 and into
  run e7f92f66.

### Exemption

Distinct from `docs/investigations/2026-11-08-why-missed-land-graph-land-refresh.md`
(Windows read-only git objects in `LandGraphLandRefreshTests`). First note for
a transplanted digest plus a later csproj include: none.

## Guards proposed, not built

1. Add `server/**/AgentSessionRuntime.cs` to impacted-classes kind
   `statement-budgets`, keeping the existing method pin, so a change to
   `RecordActivityAsync` selects
   `/*/*/DelegationDispatchRecoveryBoundaryTests/C1149_C1150_Statement_budgets*`
   and not the rest of the class.
2. Refuse a merge whose diff touches a definition path
   (`*proj`, `Directory.Build.*`, `*.props`, `*.targets`, `*.sln`, and the
   other paths `LandGraphPrecheck` already treats as definitions) unless the
   same batch contains the refresh script's commit and `-Check` exits 0.
3. Refuse a land whose recorded digest does not equal the definition
   fingerprint of the commit's parent when the commit's own diff is only the
   land-graph record files. That is the transplant case at `6acc0326e`.
