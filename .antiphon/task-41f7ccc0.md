# Review 41f7ccc0: land graph native fixes (cache key, allowance, no stale hit)

Subject task 0a72f317-6101-4f45-860f-0d98ff0189a3, branch feat/card-task-0a72f317, tip
4d23a99fd9874d298b76430f92ac3daff28eacb8. The subject range is f7c77683f..4d23a99fd. Commits reviewed:
61cc78955 (budget), 5caf9b4bc and f71ecd067 (cache), their witnesses 2f21da1f6 and 79f3a09f5, and
the report commits. 043aed60e is superseded and its digest was not judged.
Host: server2, Linux. Scope: **Interim**. The whole Unit lane was not run.

## Verdict

No defects. No stale or foreign cache hit, no relaxed fail-closed bound, no weakened assertion and
no stub witness.

## (1) Cache: 5caf9b4bc and f71ecd067

- **Location.** The cache is not in the land-graph audit. It is in
  `scripts/lib/rc-project-selection.ps1`, the CARD-1545 advisory project proposal. That file's own
  header says "never execution or qualification authority". Its only automated consumer is
  `release-selection.ps1:214`, which hard-codes `effectiveMode='Full'`/`coverageScope='Full'` and
  stores the result as `projectProposal`. The C# evaluator and the land digest never load it.
- **Key.** The key is the 40-hex blob id from `ls-tree -r -z` (`:50`). Only modes 100644 and 100755
  produce one. Gitlinks and symlinks get no key and become `missing-input`, so they fall back to
  Full. A 64-hex (SHA-256) repository would also fall back to Full.
- **Read matches key.** The read is `cat-file blob <sha>:<path>`, the same object the key names.
- **What is keyed and what is not.** The parse settings are fixed in code. The parsed XmlDocument
  is only read (SelectNodes/GetAttribute), never mutated. Path-dependent work (import and reference
  resolution, inputs, hasTests) still uses the per-site `$input` and `$project.path` (`:107-133`).
  So a blob shared by two projects, or by the baseline and candidate trees, yields identical
  results. The fixture already does this: Server.csproj and Runner.csproj have identical bytes.
- **Lifetime.** The cache lives for one `Get-RcProjectSelection` call (`$documents=@{}` at `:169`).
  The parameter default `@{}` is fresh on every call (verified in pwsh). It is never on disk, so it
  cannot outlive a code change or an SDK pin.
- **Size and threads.** It holds at most the distinct definition blobs of two trees. It is used
  single-threaded only.
- **Invalid XML.** The cached exception covers only `XmlReader.Create`/`Load` for that blob, and it
  is rethrown at every site (`:105`). The outer catch (`:134`) still adds
  `graph:<site>:<same message>`. Any reason forces Full.
  - A git read failure is not cached (`:94` sits outside the inner try).
  - Counter-examples: an invalid blob cannot become a document, because a document is stored only
    after `Load` succeeds. A valid blob can be cached as an exception only if a non-content fault
    (for example OOM) hits that one call. The only effect is a more conservative Full.
- **Real-repo output.** I ran `select-rc-project-batches.ps1` with the current library, the
  pre-5caf9b4bc library and the pre-f71ecd067 library. Ranges: f7c77683f..4d23a99fd, and
  a97194ee7 (rc/master merge base)..16477bad0. The JSON is content-identical across all three.
  Only `plannerHash` (the script's own hash) and the `digest` computed over it differ.
- **Approval digest.** `LandVerificationBuildGraph.Includes` selects only proj, props, targets,
  sln, rsp, global.json and NuGet files. No subject commit touches one.
- **Real audit.** I ran the real audit `-Check` on a scratch clone at 4d23a99fd. It exited 0
  (approved) with digest 20DE49F6…DECB6 and count 66. All 9,159 tracked SHA-256 values matched
  before and after, `git status` stayed empty, and no canary was left.
- **Audit without the cache.** I repeated it on the same clone with the pre-cache selector
  committed. Result: exit 0, the same digest and count, approved.

## (2) Canary-copy allowance: 61cc78955

- **Change.** `WindowsCopyMeasurementMultiplier` went from 5 to 30. The Windows copy allowance went
  from 12 s to 68 s (2253 ms × 30, rounded up). The Linux budget (5 s) and the Windows pass floor
  (120 s) are unchanged. The tool ceiling on Windows is 120×2 + 68 + 60 = 368 s.
- **Calibration.** It is honest. Samples were 33515, 25745 and 25642 ms; nearest-rank p95 for n=3
  is the maximum, 33.5 s. Twice that is 67.03 s, which is at most 68 s. I could not read the
  Windows evidence file from server2.
- **Platform gate.** The new value applies only when `OperatingSystem.IsWindows()` is true. It is a
  constant: there is no environment variable or argument for it. The tool's argument set
  (`Program.cs:35-41`) and the script's parameters (`audit-land-verification-graph.ps1:22-26`) are
  unchanged by the range.
- **`-EvaluationBudgetSeconds`.** This existing parameter sets only the script's wait. The tool's
  per-phase clocks and its own watchdog still bound the run.
- **Timeout path.** It is unchanged. `PhaseClock.Bound` returns 2 and calls onTimeout (canary
  Dispose plus DeleteActive). The watchdog calls DeleteActive and exits 2, and the ProcessExit hook
  is still in place. The LandGraphAuditCleanupTests (5/5) and the LandGraphEvaluationTests budget
  and refusal cases pass.

## (3) Weakening check

- **Assertions.** None was relaxed. The 61cc78955 test edits change exact pinned values to the
  recomputed formula: 8000 ms gives 240; 1000 ms gives 30; the host value is 68 on Windows and 5 on
  Linux. The source-pin string is updated to `= 30;`. The old product is red against the new
  values, and so is a missing Windows branch (it would give 16).
- **Deadlines.** ChangeAwareReleaseTests/C1313 and every other deadline are outside the diff. The
  only test edits are the two named files and `scripts/test-rc-project-selection.ps1`.
- **Probe matrix.** I ran `C1545_Closure` directly from scratch copies:

| Variant | Result |
|---|---|
| Current tree | 9/9 pass |
| Pre-5caf9b4bc | **red**: immutable-blob-read-once |
| Pre-f71ecd067 | **red** |
| Mutant: invalid XML cached as an empty document (Full fallback dropped) | **red** |
| Mutant: key = path (stale hit across graphs) | **red** in Closure; also red in FailClosed (`dynamic-full`) and in HistoricalEdges (`old-edge`, `cycle-full`). ConsumerSafety stays green. |
| Mutant: rethrow line `:105` deleted | green (see A-2) |

## (4) Approval consequences

- The covered file set (Includes) is unchanged; LandVerificationBuildGraph.cs is not in the diff.
- The script's parameter allowlist and the tool's argument set are unchanged.
- check-evidence-diff over f7c77683f..4d23a99fd: 9 commits, 5 entries, 0 violations, exit 0.

## (5) Port

### rc/2026-10-27 at 16477bad0

1. Cherry-pick, in order, with `-x`: 61cc78955, 2f21da1f6, 5caf9b4bc, 79f3a09f5, f71ecd067.
2. 2f21da1f6 and 79f3a09f5 conflict modify/delete only on `.antiphon/task-0a72f317.md`. Drop that
   file. Everything else applies clean. This was simulated in a scratch clone: EvaluationBudget.cs
   and both selection scripts end up byte-identical to 4d23a99fd.
3. Do not pick 043aed60e. Its evaluation.txt records SDK 10.0.300; rc records 10.0.403. The digest
   (20DE49F6) and the pins (749d653) are the same in both.
4. No pick touches a selected definition, so rc's digest is unchanged. Run the documented real
   audit once on the final rc tree:
   - `-Check` first.
   - `-Update` only if `-Check` refuses, in its own commit with the SHA-256 values in the message.

### master at e65343132

- 61cc78955 cannot apply. Master lacks the whole per-phase budget series: EvaluationBudget.cs,
  PhaseClock and WallClock are absent. The pick gives modify/delete on EvaluationBudget.cs and
  content conflicts in docs/orchestration-loop.md and LandGraphEvaluationScriptTests.cs.
- The base series (f7c77683f lineage; the author reported 16 merge-tree conflicts) must reach
  master first. 61cc78955 follows it.
- 2f21da1f6, 5caf9b4bc, 79f3a09f5 and f71ecd067 apply now, dropping only the report file.
- Regenerate the approval once on the final master tree after the series lands.

## (6) Desktop leftovers

These are not live-verified; the desktop is not reachable from server2.

- **Three canaries.** They sit at `C:\Antiphon\worktrees\antiphon-landgraph-canary-*`, siblings of
  the worktrees. A later run creates its own new GUID sibling. Evaluation reads only the audited
  tree, the SDK and Directory.Build files on the ancestor chain; a sibling directory is not an
  ancestor file. The canary copy enumerates only the audited repo. Land never runs the evaluator,
  and its digest comes from git ls-tree.
- **The 34 `bin-0a72f317` directories.** They sit only inside the card-task-0a72f317 worktree. The
  author's final `-Check` approved with them present.
- **Conclusion.** Neither set can affect a later audit. The cost is disk only, and cleanup is the
  operator's.

## Runs

Build: one slot build of Antiphon.Tests (Debug, UseAppHost=false, OutputPath `bin-41f7ccc0/`) at
4d23a99fd with a clean tree: 0 errors, 253 s. All runs below are dotnet exec with no build, each
through `scripts/build-slot.ps1`.

**Run 1** (one slot, 12 min, TRX `_17e5428a8f56_2026-10-29_15_57_09`): 152 total, 151 passed,
0 failed, 1 skipped.

| Class | Count |
|---|---|
| LandGraphEvaluationTests | 130 |
| LandGraphAuditCleanupTests | 5 |
| RcProjectSelectionTests | 4 |
| RcRosterRepairTests | 3 |
| WindowsUnitPlatformAdmissionTests | 5 (4 pass, 1 skip) |
| C1514LandingSplitBudgetTests | 5: C1404, C1577, C1584, C1410, G2865 |

The skip is C1210_Windows_linux_only_outcomes_are_exact, which is native-only.

**Run 2** (own slot, 12 min 11 s): LandNonCodeSkipTests 111/111.

**Extra run** (6 min): ChangeAwareReleaseTests C1313_ModeCredit (255 s) and C1313_ParameterHops
(118 s), 2/2. These are the direct integration consumers of the cache.

**Evaluator:** Release build in the scratch clone, then two real `-Check` runs as above.

**Not run:** the whole Unit lane, PCs and E2E.

## Native-only witnesses

- **Run only on Windows:**
  - In LandGraphEvaluationTests, the Windows side of `host.Copy == 68`. Linux asserts 5; the Windows
    formula is covered on Linux by `--windows true` (240 and 30).
  - C1210_Windows_linux_only_outcomes_are_exact.
  - The real 68 s copy against NTFS.
- **Run only on Linux:** C1483_S1_UnreadableLinkComponentDenies and the slow-dotnet
  EvaluationBudgetRefusesSlo… case. They passed here and are skipped natively.

## Advisories

- **A-1.** The Windows allowance is derived from the Linux constant (`EvaluationBudget.cs:29-31,
  53`). Re-measuring Linux would silently rescale Windows. For example, 1500 ms would give 45 s,
  below 2× the native p95. The failure would be a spurious refusal (fail-closed), not an approval.
  Suggest a separate native `WindowsMeasuredCopyP95Milliseconds`. Also, n=3 makes the p95 the
  maximum, and the margin is only 1.01× over 2× p95.
- **A-2.** The rethrow at `rc-project-selection.ps1:105` has no distinguishing witness. Without it,
  `SelectNodes` on the cached Exception still throws, and the same `graph:<path>:` reason appears
  with different text. The behaviour stays fail-closed. The witness pins only the prefix.
- **A-3.** The inner catch (`:99-103`) caches any exception from Create/Load for the rest of the
  call, not only XmlException. The effect is only a more conservative Full.
- **A-4.** The brief describes the cache as part of the land-graph audit. It is not: it sits in the
  advisory release proposal. Its only gate-relevant property is that the proposal output is
  unchanged.
- **Routing.** This pass is Interim scope. The stage bundle routes a clean Interim to a Final
  review. The brief asks for `merge` on clean, so the routing decision is the caller's.
