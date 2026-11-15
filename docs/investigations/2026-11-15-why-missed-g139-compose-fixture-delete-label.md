# Group 139: compose fixture cleanup operation label omitted

Debug `22ee1e10`, Linux server2, starting from fetched current master
`a71c45ab4d5703d480f4eee4c4e30adab45bc4f9` (Merge 244, CARD-1953).
The Windows run `160200cb` reported
`TestTimingOperationLabelTests.C1620_EveryCallSiteOperationLabelIsKept`
failing because `compose-fixture-delete` was recorded as `omitted`.

## Cause and introducing change

Commit `704dc63ce8833864c7739360cac6e5fd26c460eb`, originally
`67835ba18d3cd97ac092bd04b00aa944193851c3` from Debug `68f81b18`, adds
`ComposeFixtureRoot.Dispose` and its `FixtureTeardown` span. The literal was
absent from `TestTimingSession.OperationLabels`. `BoundOperation` therefore
replaced it with `omitted`; the strict call-site witness correctly rejected it.

An independent scan of all 2,698 tracked C# files under `tests/` found 60 static
operation labels and exactly one unlisted label: `compose-fixture-delete`.
The CARD-1934 labels `c1934-ryuk-create-start`,
`c1934-redpanda-container-create`, and `c1934-kafka-api-server` were already
admitted on this master. The repair adds the one missing exact literal and
keeps both the strict equality witness and arbitrary-text omission unchanged.
This follows the g138 repair, master commit
`4eafa4921ad72d7994bcd2cca80826cfec434d93`.

## Why Merge 244 missed it

The CARD-1953 verification record
[`2026-11-15-c1906-compose-fixture-leak.md`](2026-11-15-c1906-compose-fixture-leak.md)
records the compiler-policy fixture, c346 Compose fixture, cleanup witness,
selector, process-spawn roster and eleven-class guard union. That union omits
`TestTimingOperationLabelTests`. Successful fixture teardown does not assert
that the recorder preserves its operation label, and recording is opt-in.

No persistent path glob in the `timing-labels` selector row connected the Compose
fixture files to the label witness. The row did already have a generic added
`BeginPhase(`/`BeginFixturePhase(` detector, which could select the introducing
diff when evaluated over the complete range. The missed execution is therefore
also an omitted impacted selection, not evidence that the added-call detector
could not identify this change. Passing the selector's own tests is insufficient
without executing the selected witness.

The repair extends that same single row with the Compose scripts and helpers,
the shared Testcontainers database fixture files, and the C519 transport fixture.
It retains the generic added-call detector. The cheapest pre-land row is:

```sh
pwsh -NoProfile -File scripts/build-slot.ps1 -Label g139-labels -- dotnet run --project tests/Antiphon.Tests --no-build -- --treenode-filter '/*/*/TestTimingOperationLabelTests/*'
```

Run it after a leased native-default-output build. Run the impacted selector
over the complete landing range and execute its selected rows beside behavior
tests. The existing frozen `WhyMissedTestsFilter` already contains the exact
`TestTimingOperationLabelTests*` entry; no expectation relaxation is required.

## Baseline evidence

The complete label class ran at clean baseline source: 2 total, 1 passed,
1 failed, 0 skipped; exit 2. The assertion names only
`operation labels written as omitted: compose-fixture-delete`. Framework time
was 5.858 seconds, leased wrapper wall 9.928 seconds, slot wait zero.
The native-default apphost build passed with 0 errors and 2,138 warnings in
225.51 seconds (226-second lease). An initial `--no-restore` build failed
because this fresh mirror had no assets; the restored build supplied the
baseline. A preliminary PowerShell wrapper stalled in native wildcard expansion
before launching any test child; its owned process was stopped and joined.
The corrected wrapper uses `ProcessStartInfo.ArgumentList` for literal filters.

Raw logs, TRX, row/source/load metadata and opt-in phase JSONL are ignored under
`/work/worktrees/ws-02/.antiphon/task-22ee1e10/`. Initial `/proc/loadavg` was
`0.89 0.61 0.53`; baseline test load ranged from `6.56 7.01 3.48` to
`5.85 6.84 3.47`. These are individual contended-host observations, not p95.
Uninstrumented phases remain unknown. Build/slot wall times remain separate
from framework and recorded phase times.

## Pins and verification scope

The production policy and protected-input readers recomputed:

- policyHash `c2e3939821a8fef911600c44be767b951c8212bcb62824cb746e48c5aaba6909`
- PinnedInputDigest `2f48120a65280582b586f9369e2eaf6b1bdc464a2362c8e49d434bd852479f60`
- LegacyExclusionDigest pin `6d6ff646207dba9e3c09316e5bc77651f6801fe945ccde83b9b58644367b252a`

The impacted-class table is outside these pinned input sets; none changes.
The native-policy witness in the required guard union validates the legacy seal.
The commissioned verification is the complete label and selector classes,
compiler-policy fixture, c346 fixture and CARD-1953 cleanup witness, C519 compile,
and the eleven-class guard union. The derived-index driver must be built before
two regenerations, the second reporting unchanged. Mutation checks are waived
by the supplied operator authority; no full-suite or Windows qualification
credit is claimed.

## Selector expectation extension

The first full selector run at `03fff6f44ef180eb0e0d628891a2d59235f68a00`
passed 49/50 and failed the strict historical-range kind list for
`a54a6d4f755fa139b41fd7dc8aef456d3d2c0975`. That commit edits
`scripts/fixtures/c346-workspace-slot-compose.mjs`; the new persistent glob correctly adds `timing-labels`
to its former `remote-scripts`, `test-lane-category` selection. Replaying the
selector with the exact baseline table confirms the original two-kind result.
This expectation change is introduced by the intended broader coverage, not an
inherited failure. Extend the exact expected kind list and require the label
witness alongside the existing remote-script witnesses. Strict equality remains.
The initial failed TRX and its 265.596-second leased wall time remain in
`selectors/`; the corrected full-class rerun uses `selectors-green/`.
