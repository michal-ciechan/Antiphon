# CARD-1619 policy coverage repair

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

## Pending

Rebuild and rerun the two witness classes, then run the real land-graph `-Check`.
