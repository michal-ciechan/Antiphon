# CARD-1981 c487-probe owned temp root: Review round 1 (Final)

Review task: `0a7fe845-2b35-4004-8a02-c0c37ed8a627`.
Subject: Code task `092a4d67-a75e-471e-9b8a-2aabd44501e6`, branch `feat/card-task-092a4d67`,
tip `c434223edbf1b4e7231e19ccc1ecdbd4408a65a8`, range `1443b7ebc82277e9b21cc83d31a686bdb62208c2..c434223edbf1b4e7231e19ccc1ecdbd4408a65a8`.

Verdict: **one blocker (D-1)**. The start sweep is exact. The process-exit release deletes the
whole shared owned root.

## D-1 (blocker): the shared probe root is tracked and recursively deleted at assembly end

- **Where:** `tests/Antiphon.Tests/TestHelpers/TestClassificationPolicyTests.cs:551-557`
  (`ProbeOwnedRoot` calls `OwnedTempRoot.Track(root)`), with
  `tests/Antiphon.Tests/TestHelpers/OwnedTempRoot.cs:41-48,52-66` (`ReleaseAll` on `[After(Assembly)]`
  and `ProcessExit` runs `Directory.Delete(root, recursive: true)`).
- **Failure:** any process that ran a c487 probe deletes
  `<temp>/antiphon-owned-c4870000000000000000000000000000` with everything in it when it exits:
  fresh probe directories, names outside the pattern, and other processes' live probes. Observed in
  review row CP-6 (checkpoint run `20261119-213820-5db5`, `TMPDIR` set to a scratch `share/` folder).
  Before the run, the root held a fresh `c487-probe-0123456789abcdef0123456789abcdef/live.txt`, an
  uppercase `c487-probe-AAAA...` and `unrelated/o.txt`. The witness passed 1/1. After the run all three
  and the root itself were gone. The only `antiphon-owned-` deleter in tests/src/tools is
  `OwnedTempRoot`. The checkpoint temp sweep matches only `c723-<hex>`.
- **Why it blocks:** the brief's blocker list includes a delete outside the exact pattern and root. The
  claim says only direct children named `c487-probe-` plus 32 lowercase hex digits, older than 24 h,
  are deleted, so a live probe is safe. Two test processes that share a temp directory break that:
  - on Windows, every run, because `GetTempPath` ignores the build-slot `TMPDIR`;
  - on Linux, any run that shares `TMPDIR`.
  When one process ends its assembly, it removes the other's in-flight probe (Probe.dll build/run),
  which makes the other fail. The witness cannot catch this because it asserts only inside the body.
- **Fix:** do not track the stable root. Keep tracking each probe directory (already done in
  `WithTempDirectoryAsync`). Add a witness that fails while the root is tracked or would be released,
  for example by asserting that a fresh foreign sibling survives the release path.

## Checked and clean

- Sweep exactness (`SweepStaleProbeDirectories`):
  - It enumerates only the root's direct child directories. A file with the exact name is not
    enumerated.
  - The name check is ordinal: the exact prefix plus exactly 32 characters in `[0-9a-f]`.
  - It requires the root's parent to equal `GetTempPath()` and the root not to be the temp path.
  - `Path.Combine(GetTempPath(), "antiphon-owned-c487…")` cannot produce an ancestor. With
    `TMPDIR=/`, the root is `/antiphon-owned-c487…`.
- Symlink child (pwsh .NET 9 probe in the scratchpad, using the same API sequence): a `c487-probe-<hex>`
  symlink to an outside directory is enumerated. `StartRefGit.DeleteDirectoryCore` unlinks only the
  link through the reparse branch. The target and its contents survived. The link is removed rather
  than skipped (advisory only).
- Concurrency of the sweep itself: it deletes only children whose directory last write is older than
  24 h. A live probe updates its directory when it creates entries, and a probe run lasts minutes.
  Safe, apart from D-1.
- Probe assertions: the diff changes no existing assertion. The two pre-existing c487 tests and the
  probe callers at `:755`/`:761` are unchanged.
- Witness: it asserts that the stale child is removed and that a fresh child, a non-hex near-miss and a
  foreign-parent child survive. Marker bytes are compared CRLF-folded and paths with `GetFullPath`. A
  mutant that sweeps fresh or near-miss directories would turn its asserts red (by construction, not
  run; mutation waived). Red at `ebd3b1bf` is as reported by Code (not re-run). Windows is UNOBSERVED.
- `f7554aea`: the CP-4 Min floor was lowered from 43 to 39, matching the executed union of the named
  guard classes. This review re-ran it: 39/39. `c434223e` adds one class to the derived index;
  regenerate prints `unchanged`.

## Rows (checkpoint run 20261119-213820-5db5 at c434223e, one cold build)

| Row | Result |
|---|---|
| CP-1 TestClassificationPolicyTests | 43/43 passed |
| CP-2 OwnedTempLeakTests | 10/10 |
| CP-3 DerivedImpactIndexTests | 26/27: `regenerate_script_reports_changed_then_unchanged` red, because the `-NoBuild` driver dll was not built in this fresh mirror (precondition, not the subject). R-1 substance: a regenerate run afterwards printed `derived-impact-index unchanged`, and the tree stayed clean. |
| CP-4 guard union | 39/39 |
| CP-5 land-graph `-Check` | exit 0 |
| CP-6 (review) witness under a shared TMPDIR | 1/1 passed; D-1 observed |

`validate` refuses the run (`report_failed`) because CP-3 is red, so there is no clean receipt.
Evidence diff over the range: commits=5, violations=0. `merge-tree` against `origin/master` at
`645159add428de48063ac359573d668e521df968` has no conflicts.
