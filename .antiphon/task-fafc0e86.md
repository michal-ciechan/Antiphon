# CARD-1619 policy coverage repair

Fixed and pushed the three missing rc policy rows and synthetic-policy fixture
setup. The affected witnesses pass 15/15; the other requested checks passed 17/17.
The real land-graph `-Check` approved. This is narrow Linux verification, not native
Windows or full rc qualification.

## Baseline red

At untouched base `c622368c2fd8a134c191c5cdf8d484f9059406a2`, the exact two-class
selection `/*/*/(NativeLinuxPolicyWitnessTests*)|(NativeLinuxPolicyWitnessMutationTests*)/*`
executed 15 cases: 13 passed, 2 failed, 0 skipped (26.744 seconds).
Both `Policy_rows_require_pinned_attribute` and `Markers_do_not_expand_policy_exclusions`
failed with `reverse-coverage` diagnostics for these already-marked classes:

- `Antiphon.Tests.Application.Card1619ExitConsumerTests`
- `Antiphon.Tests.Application.Card1619ExitPersistenceTests`
- `Antiphon.Tests.Application.Card1619StopAttributionTests`

All 12 mutation cases passed, including LF/CRLF legacy digest, marker origin and
lookalike checks. No source change or commit preceded this reproduction.

The base build succeeded (0 errors; existing warnings), 195.63 seconds excluding
slot admission. Every driver used `scripts/build-slot.ps1`, `TEMP=/tmp`,
`UseAppHost=false`, producer-owned `OutputPath=bin-fafc0e86/`. Test execution used
`TUNIT_MAX_PARALLEL_TESTS=1`. Raw logs and fresh TRX are retained in
`/work/worktrees/task-fafc0e86/.antiphon/fafc0e86-evidence/` (ignored).

The referenced Debug report was absent here; it was read in full from
`/work/worktrees/task-d3f7786a/.antiphon/task-d3f7786a.md`.

## Linux-only source evidence

Each class already imports `Antiphon.TestSupport` and uses the real pinned
`NativeLinuxOnlyAttribute`. The shared native fixture
`tests/Antiphon.Tests/TestHelpers/Card1619NativeExitFixture.cs` launches `/bin/sh`
through the real detached PTY resolver, reads `/proc/<pid>/stat` start ticks to
verify Linux child identity, and imports libc `kill` to observe actual signal exit.
The three classes call that fixture to exercise consumers, persistence and stop
attribution respectively. This inspection confirms their existing class exclusion
is intentional; native test bodies were not executed under this brief.

## Policy fix and fixture red

Commit `078494b3a235a88ee68834f06314d7183ec493f0` preserves the untouched-base red.
Commit `f516c6fc11ab8545b6dde4e5168dafd4cdb9afec` adds exactly three Windows-only
rc exclusion rows, owned by CARD-1619, leaving the classes required on Linux.
All existing pinned attributes and migration baselines remain unchanged.

Owner tooling (`Get-NightlyPolicyHash` from `scripts/lib/nightly-common.ps1` and
`scripts/lib/nightly-policy.ps1`) recomputed policyHash:

- Old: `1f68aa2002aee99fe38115ff620bdaa2830497009ed6b15b4a3c2b353dbdbee6`
- New: `7de962a40547f495be039cdde12a71e62d7ae4e21e0ada18b76982f87dae6387`

The pinned legacy digest stays
`8397984d90368a0ef1cbc5991610ae50328165b40ac2503559ae0562d72b554d`.

After a successful candidate rebuild (189.05 seconds; 0 errors), the five-class
selection executed 32 cases: 28 passed, 4 failed, 0 skipped (88.510 seconds).
The shared witness is now 3/3 green. DinD 9/9, roster 3/3 and all five census cases
passed. The four failures are mutation fixture setup: `Policy()` retained the new
shipped rows in synthetic assemblies which contain only explicit fixture types,
producing `unresolved-row` for all three new classes. Failed methods:
`Marker_pin_and_origin_are_enforced`, `Lookalike_attribute_identity_is_rejected`,
and both LF/CRLF `Shipped_legacy_digest_is_pinned_across_checkout_line_endings` cases.

The fixture helper now excludes newer prefixed witnessed rows as well as frozen
rows, matching the existing shipped-legacy digest helper and inspector. Explicit
fixture extras are still appended and all assertions remain intact. The committed
policy-only slice is the red for this helper repair; no run was in flight during edits.

## Final verification

Commit `54d3fb068a71b05a4cafa6e1673502a0282ea3e3` repairs only the synthetic
policy helper, after the committed policy-only red. Its build passed with 0
errors (151.88 seconds). All three builds retained 1400 existing warnings;
no warning suppression was added.

The final two-class witness run at that commit passed 15/15, with 0 failures and
0 skips (18.959 seconds). The prior passing DinD/roster/census selection is unchanged
by the fixture-only edit and was not repeated, following the narrow rerun rule.

| Selection | Source | Passed | Failed | Skipped |
|---|---|---:|---:|---:|
| Both witness classes, untouched base | c622368c2 | 13 | 2 | 0 |
| All five requested classes, policy-only | f516c6fc1 | 28 | 4 fixture failures | 0 |
| Both witness classes, fixture repair | 54d3fb068 | 15 | 0 | 0 |

Final passing coverage by class, with no remaining failed case:

- `NativeLinuxPolicyWitnessTests`: 3/3 at 54d3fb068.
- `NativeLinuxPolicyWitnessMutationTests`: 12/12 at 54d3fb068, including pinned
  origin, lookalike identity, unmarked exclusions and both LF/CRLF legacy variants.
- `VerifyDindRunnerScriptTests`: 9/9 at f516c6fc1.
- `RcRosterRepairTests`: 3/3 at f516c6fc1.
- `C1514LandingSplitBudgetTests`: 5/5 at f516c6fc1: C1404, C1577, C1584, C1410,
  G2865. These exercise Windows census/roster contracts from Linux, not native Windows.

Both baseline failures named 21 signatures (consumer 8, persistence 5, attribution
8), all `reverse-coverage`; no missing-marker or missing-row diagnostic was present.
The original two coverage failures were inherited; the four fixture failures were
introduced by adding the shipped rows and resolved by the helper repair.

All build/test/evaluator drivers ran serially through a granted host build slot,
with zero-second admission waits. Slot-held build durations were 196/190/153
seconds; test driver durations were 32/92/22 seconds. Fresh TRX preserves per-test
durations and exact membership; `summary.json` joins each run to its source SHA.
Uninstrumented IO/CLI/fixture phase breakdown is unknown, not zero. No timeout,
cost row, production behavior, pinned attribute or assertion was relaxed.

The real audit, at 54d3fb068, exited 0 in a 26-second granted slot:

```sh
TEMP=/tmp pwsh -NoProfile -File scripts/build-slot.ps1 -Label fafc0e86-land-graph -- scripts/audit-land-verification-graph.ps1 -Check -ToolPath /work/worktrees/task-fafc0e86/tools/Antiphon.LandGraphAudit/bin-fafc0e86/Antiphon.LandGraphAudit.dll
```

It completed both evaluations and its canary comparison, ending `approved`.
Recorded/current digest:
`7BDABBA075B620C10A47B50CE1277A932DDD801E9314A932C4EAC03885441A99`, count 68.
No `-Update` was run. Evaluator SHA-256:
`25abdd1c0b804169a2b10663f483b280163161119ee0df1ffe66d08354beef6b`.
Final test assembly SHA-256:
`a203909dc0815a82cab61dce0d43b8d8105bcbc74617c70d17dbcaa192984d5b`.

## Landing and cleanup

Fetched `origin/rc/2026-10-27` =
`ac6bd1b5b9cf6119bffd0b820ee12af7f8208492` and `origin/master` =
`cbe3e6b42401db766f4cee38bc9b34f5b9d314af`.
The policy slice's rc merge-tree check was clean, tree
`0fdc164a685ce762a7348c88c0f7df112640f831`; the final receipt rechecks the
published tip. Master is a separate port: 17 conflicts, versus 16 verified at the
untouched base; this policy edit adds the test-execution-policy conflict.
No merge, branch switch, rebase, force-push, tag, release publication or activation
was performed. The task contains the complete d3f7786a ancestry and should merge
to rc with that repair. No native test body, live qualification run d47848cc,
desktop checkout or C:\Antiphon path was touched.

All 35 producer-owned `bin-fafc0e86` outputs were removed after every driver
completed, using exact literal paths recorded in `owned-outputs.json`; cleanup
verified that each was absent. No deletion targeted another task's outputs.
Generated logs, TRX, JSON and checksums remain ignored under the evidence root.
The final report commit changes narrative only; all executed verification binds
to the source commits stated above. The caller receipt supplies its full SHA,
remote verification and final evidence-history guard result.

## Rerun

After rebuilding the removed output, run the exact combined selection:

```sh
TEMP=/tmp pwsh -NoProfile -File scripts/build-slot.ps1 -Label fafc0e86-build -- dotnet build tests/Antiphon.Tests --property:OutputPath=bin-fafc0e86/ --property:UseAppHost=false --nologo
TEMP=/tmp TUNIT_MAX_PARALLEL_TESTS=1 pwsh -NoProfile -File scripts/build-slot.ps1 -Label fafc0e86-verify -- dotnet run --project tests/Antiphon.Tests --no-build --property:OutputPath=bin-fafc0e86/ --property:UseAppHost=false -- --treenode-filter '/*/*/(NativeLinuxPolicyWitnessTests*)|(NativeLinuxPolicyWitnessMutationTests*)|(VerifyDindRunnerScriptTests*)|(RcRosterRepairTests*)|(C1514LandingSplitBudgetTests*)/*' --report-trx --report-trx-filename rerun.trx --results-directory .antiphon/fafc0e86-rerun
```

No approval was needed: the caller explicitly authorized investigation, repair,
commit and push. Native Windows qualification remains with the caller's separate run.

--- next stage ---
next: merge to rc together with d3f7786a
handoff: Merge the pushed task branch, including its d3f7786a ancestry, into rc; retain the separate native Windows qualification gate.
artifact: /work/worktrees/task-fafc0e86/.antiphon/task-fafc0e86.md
