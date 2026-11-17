# CARD-1893 S1/S2 test design review, round 2

Subject: `docs/superpowers/plans/2026-11-17-card-1893-s1-s2-testdesign.md` at
`origin/feat/card-task-aefcc5e5` tip `7d36f6efbf88118537d27cfd90a3fdfab2a9be14` (Grok).
Previous reviewed SHA: `c88fc182d965b65a8154545f6431d5e7c5289430`. Round-1 review:
`origin/feat/card-task-f29c3d90` (B-1, B-2, B-3). Master: `c3cc0bb9b7b1d0c55464d07cd8e60a63b4239028`.
Reviewer: Claude Opus (Review aa63b6d1). Scope: `git diff c88fc182d..7d36f6efb` (one file,
+245/-118) against B-1..B-3 and master code. Blockers and regressions only.

**Verdict: CLEAN.** B-1, B-2 and B-3 are closed. Two advisories below; they do not hold the land.

## B-1 (closed): the hold script now fails closed

The design drops the snippet's bare `else NOT-AFFECTED`. It resolves `X^{commit}` and the
ancestor with `git cat-file -e` before `is-ancestor`, and either failure exits non-zero with
empty stdout. `NOT-AFFECTED` requires `is-ancestor` exit exactly 1. Any other status is the
same error. The caller runs `git fetch origin`, and the script does not. The doc method now
pins that sentence. G-21/PC-21 and `C1893_S1_Hold_exits_nonzero_when_the_argument_does_not_resolve`
were added. The method is in the CP-1 filter, and the CP-1 and CP-3/CP-7 Mins went 10→11 and
147/149→148/150.

I wrote the design's script into scratch (not committed) and ran it read-only:

| Where | Argument | Result |
|---|---|---|
| this checkout | `870a93d21` (= `origin/rc/2026-11-24` tip) | `HOLD`, exit 0 |
| this checkout | master `c3cc0bb9b` | `HOLD`, exit 0 |
| this checkout | `0000…0` | stdout empty, exit 2 |
| this checkout | unknown 40-hex | stdout empty, exit 2 |
| fixture | child with both exact lines, worktree dirtied | `RELEASED` |
| fixture | path line only | `HOLD` |
| fixture | temp disable only | `HOLD` |
| fixture | unquoted `false`; `"False"`; trailing `/`; lines swapped | `HOLD` ×4 |
| fixture | orphan commit, ancestor object present | `is-ancestor`=1, `NOT-AFFECTED` |
| fixture | `0000…0`, ancestor present | stdout empty, exit 2 |

So `RELEASED` needs both the standing `HistoryPath` and the temp `HistoryEnabled: "false"`.
`HOLD` still prints for rc-24 and master. The new method goes red at base because the script
is absent. A PC-21 mutant (128 → `NOT-AFFECTED`) fails it on stdout and exit.

## B-2 (closed): the upgrade harness is the faithful injection

I checked each claim at master:

| Claim | Where it holds |
|---|---|
| `InsertSetupAtEntry` is `internal static` on `internal sealed class C1008HostFixture`, so it can be called from the new class | `RollingVolumeRecycleScriptTests.cs:3269`, `:3552` |
| The single column-0 trap is after the hard-coded assignments | trap `c590-remote.sh:10654`; assignments `:18-21`, `:904-907` |
| `C1008HostFixture.Run` pins `C590_SHA` to forty `a` | `:3501` |
| The C1008 fake's `exec` answers only `stat`/`sh`, otherwise `fail()` | `c1008-fake-docker.sh:552-563` |
| `C590_REEXEC=1` skips `ensure_checkout` and the `exec` | `:10696-10701` |
| The census occupancy read is `docker exec … curl` | `:2771-2772` |
| The case falls through to `RealCasePending $CASE` | `:10744-10745` |
| `write_result` writes `$CASE_DIR/c590-result.json` | `:109-117` |
| The host-load route exists | `BuildSlotRoutes.cs:71` |
| `HostAdmissionSnapshot` field names | `HostLoadContracts.cs:20-22` |

I ran the harness at base in scratch. It used a copy with the design's injection at the trap,
with the roots, `CASE_DIR`, `SERVER2_ENV` and `SERVER2_TEMP_ENV` redirected, and with
`ensure_checkout`, `ensure_dirs`, `detect_lane`, `compose_host` and `sleep` stubbed. A
recording docker fake was both on `PATH` and the `docker` function. The environment was
`C590_CASE=upgrade-broker`, `C590_REEXEC=1`, the design's SHA, and no `C1008_CONTEXT`.
The fixture result file said `{"accepted":false,"diagnosis":"RealCasePending upgrade-broker","exit":2}`,
exit 2. The docker trace was empty and `main.env` was byte-identical to the seed. Every
upgrade witness's stated red holds: wrong diagnosis, no `exec`/`curl`, no `tag`. The result
file is under the fixture root, and `/work/repos/antiphon` was not touched.

## B-3 (closed): depth-1 fetch only

Each of the six fixture methods does one `git init` and one `git fetch --no-tags --depth 1
<RepoRoot> 9cf3e4917…`, then only synthetic commits. The misspelled method fetches once for
its four trees.

Measured from this blob:none worktree at load ~27:
- The fetch took 7.27 s and succeeded.
- `git checkout --detach` of the shallow root worked (blobs present).
- A synthetic child gives `is-ancestor` = 0, and an orphan gives 1.

Budget: 6 × 8 s = 48 s plus the two class samples ≈ 73 s, inside CP-3's 2-minute estimate.
The derived row timeout is 15 min. `EstimatedMinutes` did not change.

## Mechanical checks

- Checkpoint import (read-only, ignored output
  `.antiphon/aa63b6d1-import.yaml`, one build-slot lease at load 14.60, held 17 s): exit 0,
  **imported 7 rows, 0 import warnings**. Two builds (`bin-c1893s1`, `bin-c1893s2`). Row
  timeouts are 42/15/15/42/15/15/15. Mins are 11/1/148/12/1/1/150.
- Guards: **guards=21 mapped=21 missing=0, duplicate PC maps=0**. There are 21 PC lines,
  each distinct.
- `check-evidence-diff.ps1 -Repository . -BaseRef 321f98ec9 -HeadRef 7d36f6efb`: commits=2,
  violations=0. `c88fc182d..7d36f6efb`: commits=1, violations=0.
- `git merge-tree --write-tree origin/master 7d36f6efb`: clean (tree `1c18605098c4`).
- No code or test file changed, so no class or guard-batch rerun is owed.

## Advisories (do not hold the land)

- **A-1: the injection list omits `mkdir -p "$CASE_DIR"`.** `ensure_dirs` is stubbed, and
  `c590-remote.sh:10694` writes `$CASE_DIR/lane.txt`. In scratch, the literal list (no mkdir)
  exited 1 there with diagnosis `UnhandledExit 1`. The trap's `write_result` still creates
  the result file under the fixture root. So the design's sanity assertion passes, the red is
  for the wrong reason, and S2 green is unreachable until the mkdir is added. With
  `mkdir -p "$CASE_DIR"` (as `C1008HostFixture.Run` does at `:3446`), the run reaches
  `RealCasePending upgrade-broker`. Code should add the mkdir. Code should also make the
  base-reach check assert `lane.txt` exists, or assert the `RealCasePending` diagnosis on the
  red run, not only the result-file location.
- **A-2: lazy fetch in promisor checkouts.** In a blob:none promisor checkout,
  `git cat-file -e` of an absent SHA lazily fetches from origin before failing. Measured: 22 s,
  `not our ref`, then exit 2. So a pushed but unfetched tip resolves anyway, and that is still
  fail-closed and correct. Keep test arguments that must not resolve inside the non-promisor
  fixture repos (the design does), so no test pays that network wait.

reviewedSourceClean is false: the subject is a test-design document. No checkpoint row exists yet to produce a SHA-validated receipt; only the read-only import ran.

--- review evidence ---
subjectTaskId: aefcc5e5-f817-4258-ac07-0f9feb56f026
reviewedSourceSha: 7d36f6efbf88118537d27cfd90a3fdfab2a9be14
reviewedSourceClean: false
ordinaryScopeCompleted: Full
reviewRound: 2
reviewedRange: c88fc182d965b65a8154545f6431d5e7c5289430..7d36f6efbf88118537d27cfd90a3fdfab2a9be14
