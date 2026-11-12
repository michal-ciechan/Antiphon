# Run ae84478d g126: TestSelectionTests red on Windows (stale CRLF in the reused release checkout)

Run `ae84478dd83d4cde8f537ba8303794fb`, rc/2026-11-17 = `3db1a43b02246706dc28075fce7e7b5e84804ddb`,
Windows desktop, chunk `antiphon-all-group-126`. Read-only extract from
`C:\Antiphon\releases\candidates\rc-2026-11-17-3db1a43b0224\logs\2026-11-11-1907-ae84478d\`
(`antiphon-all-group-126.trx`, `-tests.log`, `.timing.json`, `-execution\log_261112075614191.diag`).

## Verdict

Environment, deterministic. Not a product regression, not a flake, and not the g131/g133
family (missing test projects in a roster, policy or mirror).

The run's checkout `C:\Antiphon\releases\checkout` is a long-lived working tree that was moved
to the rc SHA with `git checkout` (reflog: 2026-11-11 11:39Z to `6f8741d87`, 19:06Z to
`3db1a43b0`; before that `reset --hard FETCH_HEAD` up to 2026-10-25). Git rewrites only working
files whose blob changes. `scripts/select-test-rosters.ps1` has not changed since 2026-10-16, so
its working copy is still the 2026-10-18 16:42Z CRLF write (41 CR bytes). That write predates
`3afdc2798` (2026-10-29, `*.ps1 text eol=lf`). The index stat still matches, so `git status`
reports the tree clean and `launch-rc.ps1`'s HEAD check passes.

`git -C C:\Antiphon\releases\checkout ls-files --eol` shows **170** tracked files with
`attr/... eol=lf` but `w/crlf`, among them `scripts/select-test-rosters.ps1`
(`i/lf w/crlf attr/text eol=lf`). `scripts/lib/test-selection.ps1` changed in the rc, was
rewritten on 2026-11-11 11:39Z and is LF (0 CR).

The three earlier Windows runs that passed this method used fresh per-task clones
(6591e378: `C:\Antiphon\releases\checkout-4dd2a426\...`). This run, and the d88de419 launch it
replaced, reused the shared tree.

## Failure

One failed of 37 in the chunk (36 passed, 0 skipped, 8m 12s).

`Antiphon.Tests.Scripts.TestSelectionTests.C1313_SelectorLineEndings`, Failed, 2.996 s,
08:04:23Z to 08:04:26Z on 2026-11-12, the last test of the chunk.

```
ShouldAssertException: result.ExitCode
    should be
0
    but was
1

Additional Info:
    Exception: C:\Antiphon\releases\checkout\scripts\test-test-selection.ps1:340
Line |
 340 |  .        if($lf.Contains("`r")){throw "live selector is not LF: $path"}
     |                                  ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
     | live selector is not LF: scripts/select-test-rosters.ps1

  at Antiphon.Tests.Scripts.ScriptHarness.Validate(...) ScriptHarness.cs:25
  at Antiphon.Tests.Scripts.ScriptHarness.<>c__DisplayClass1_0.<RunHarnessCaseAsync>b__0(...) ScriptHarness.cs:18
  at Antiphon.Tests.Scripts.ScriptHarnessProcess.RunAsync(...) ScriptHarnessProcess.cs:132
```

- Expected: harness exit code 0. Actual: 1, because the harness threw before any C487 row.
- `New-C1313SelectorCandidate` (`scripts/test-test-selection.ps1:338-340`) reads the live
  selector from the repo root and refuses a CR. That guard came from `000fb9667`
  (2026-10-27) and is correct: the method builds CRLF blobs from known LF source bytes.
- Stdout only has `ANTIPHON_TEST_PHASE_ID ...C1313_SelectorLineEndings.1.1.0`. There is no
  stderr, and nothing relevant in the diag beyond the TestNode updates.

## What ran at the same time

Nothing inside the chunk. Every class is serial under `ParallelLimiter<ProcessSpawnLimit>`, and the
TRX start/end times do not overlap:
DockerTestCommandTests 07:56:17 to 07:56:31, EvidenceDeletionGuardTests 07:56:31 to 08:02:08,
TestSelectionTests 08:02:08 to 08:04:26. The failure reads file bytes on disk, so load and
timing cannot change it.

## Passing siblings (bound the cause)

TestSelectionTests, 8 passed: `C1313_DeterministicReasons` (57.7 s), `C1313_InputRefusal`,
`C1313_CoreAndUnknownFallback`, `C1313_PinnedPolicy`, `C1313_PathIdentity(delete)`,
`C1313_PathIdentity(case-rename)`, `C1313_PathIdentity(nul-paths)`, `C1313_AuthorityChanges`.
None of them read the live selector's working bytes. They work on git blobs and fixture repos.

The chunk's other classes all passed: DockerTestCommandTests (22) and
EvidenceDeletionGuardTests (6).

## Deterministic vs timing

Deterministic. The test asserts on file content (`$lf.Contains("`r")`), and the file on disk
has 41 CR bytes. There is no poll, timeout or sharing violation. Every run against this
checkout will fail the same way until its working files are rewritten.

## History of this method on Windows

| Run | rc | Checkout | Chunk | Outcome |
|---|---|---|---|---|
| 4e0d1159 | rc/2026-11-11 | `checkout-0297fa9c` | g123 | Failed: a different cause (`nightly-core.ps1` missing from the CRLF fixture, CARD-1781, `c071fe652`) |
| a6410818 | rc/2026-11-12 | `checkout-c9cb914c` | g125 | Passed |
| dce78ee0 | rc/2026-11-14 | `checkout-42be26dd` | g126 | Passed |
| 6591e378 | rc/2026-11-15 | `checkout-4dd2a426` | g126 | Passed |
| ae84478d | rc/2026-11-17 | shared `releases\checkout` | g126 | Failed (this note) |

Between rc 3db1a43b0 and master 56e96eabf, `scripts/test-test-selection.ps1` changed only
in `C1313_ProjectAndCatalogUniverse` (`0686117e5`, the g131/g133 GitInProcess mirror).
`C1313_SelectorLineEndings`, `scripts/select-test-rosters.ps1`, `scripts/lib/test-selection.ps1`
and `.gitattributes` are identical. On master the method passes on any checkout whose working
files honour `eol=lf`. It is already green at master on a correct tree, and the code needs no fix.

## Fix

`scripts/qualification/launch-rc.ps1` now runs `git ls-files --eol` after the HEAD check. It
refuses (exit 2, before build-slot) when any tracked working file has line endings that its
`eol` attribute forbids: `eol=lf` with `w/crlf|w/mixed`, or `eol=crlf` with `w/lf|w/mixed`. The
refusal names the count, the first paths and the remedy:
`git -C <checkout> checkout-index --force --all`. Scratch repo check: this command rewrites a
stat-clean CRLF working copy of an `eol=lf` file to LF.

Witness: `scripts/test-qualification-supervisor.ps1`, row
`C1667 launch-rc refuses a checkout whose working files break their eol attribute before build-slot`
(`RcQualificationWatcherScriptTests.Supervisor_detaches_and_observe_classifies_lost_supervisor`,
38 to 39 rows). The fixture repo commits an LF `*.ps1` under `eol=lf`, rewrites it CRLF, expects
exit 2 and no fake build-slot output. It then restores LF and expects the fake build-slot
(exit 7).

**Operator action (not done here, the checkout is under the live run):** before the next launch
from `C:\Antiphon\releases\checkout`, after ae84478d ends, run
`git -C C:\Antiphon\releases\checkout checkout-index --force --all`, or launch from a fresh
clone. Until then launch-rc refuses it.

## Native proof

PROOF_PLACEHOLDER

## Why missed

- **Introducing change.** The test was right. The environment went wrong when the
  qualification launch moved from fresh per-task clones (`checkout-<task>`) to the shared
  `C:\Antiphon\releases\checkout`. That started with the d88de419 launch (2026-11-11 11:39Z
  checkout) and continued with ae84478d (19:06Z). `3afdc2798` assumed a fresh clone: its
  message says "a fresh clone that inherits core.autocrlf=true".
  `SelectorLineEndingAttributeTests` checks only the attribute, not the working bytes.
- **Approving reviews.** None covered the launch: the checkout was prepared by the
  commissioning Custom task (231427bc), not by a reviewed script. CARD-1667's launch template
  verified HEAD only.
- **Outside the selection.** No pre-launch check compared the working tree with its
  attributes, and `git status` reports such a tree clean. Linux checkouts never produce CRLF,
  so the Linux lanes could not see it.
- **Cheapest pre-launch check.** `git -C <checkout> ls-files --eol` filtered for `eol=lf` with
  `w/crlf` takes about a second on the full tree. It now runs inside `launch-rc.ps1`.
  Pre-land path glob: `scripts/qualification/launch-rc.ps1` ->
  `/*/*/RcQualificationWatcherScriptTests/*`.
- **Exemption.** No earlier note has this cause. The 2026-11-08 note
  (`docs/investigations/2026-11-08-why-missed-test-selection.md`) covers the same method with a
  different cause.
- **Latent sibling.** `scripts/nightly-run.ps1`'s own sync (`reset --hard FETCH_HEAD`, Windmill
  `C:\Antiphon\nightly\checkout`) has the same property. That checkout was not inspected here.
  Follow-up: a card to give nightly-run the same preflight.
