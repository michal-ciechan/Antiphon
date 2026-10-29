# Review 4c3ba19a: land-graph per-pass clocks and hard-linked canary (subject bf9b0104)

I reviewed `9ffec2d9e5a3879d8c27536e9c0b2b21c79b0813` against base `8004259c70a068507decec1b1b9896d58c5b4047` (rc/2026-10-27) on server2 Linux.
Result: **3 defects (D-1..D-3), 7 advisories**. No fail-open path was found, and the audit did not corrupt any file in the experiment. The scope was Interim, as the brief commissioned: the whole Unit lane was not run.

## Evidence

- **Targeted run.** The tip was built in a scratch clone through `scripts/build-slot.ps1` (lease e9b4f85a, 0 errors, UseAppHost=false, OutputPath=bin-review/). The tests ran under one slot (lease b65ebc77) with `dotnet exec bin-review/Antiphon.Tests.dll --treenode-filter "/*/*/(LandGraphEvaluationTests*)|(LandGraphAuditCleanupTests*)|(RcRosterRepairTests*)|(C1514LandingSplitBudgetTests*)|(LandNonCodeSkipTests*)|(WindowsUnitPlatformAdmissionTests*)/*"`.
  - Result: **total 235, passed 234, failed 0, skipped 1**, in 23m48s.
  - Per class: LandGraphEvaluationTests 106, LandGraphAuditCleanupTests 5, LandNonCodeSkipTests 111, RcRosterRepairTests 3, and C1514LandingSplitBudgetTests 5 (C1404, C1577, C1584, C1410 and G2865 all passed).
  - WindowsUnitPlatformAdmissionTests: 4 passed, including C1210_Roster_closure_matches_source_gates. `C1210_Windows_linux_only_outcomes_are_exact` was NotExecuted because it is Windows-only.
  - C1385_RegistryMatchesNarrowGraph passed in 35.7 s.
- **Red at base.** I built the base `8004259c` tool from a `git archive` export (build-slot lease b28b3486) and swapped it into a copy of the tip test output, under the base tree (base script). Lease 567d8446: **2/2 new tests failed**.
  - `EvaluationBudgetDerivation...` failed with `unknown or incomplete argument '--print-budget'`.
  - `EvaluationBudgetRefusesSlowDotnet...` failed because `exceeded 1 s` was missing from the output. Note: at base the exit-code assertion still passes, because the base script errors on the unknown parameter and exits 1. Only the message assertion discriminates.
- **Hard-link experiment.** I ran the tool directly twice and the script `-Check` once on a scratch clone at the tip, and took a sha256 + inode/nlink/mtime/mode snapshot of all 12,830 files, including `.git`.
  - All runs approved, taking 29–41 s.
  - While the audit ran, the canary `Antiphon.sln` shared the live inode (nlink 2). The canary `docs/*.md` files were fresh inodes holding the 21-byte canary text. 9,137 files were linked, which equals the `git ls-files` count, so `.git` was never linked.
  - After each run, nlink was back to 1 and no canary remained.
  - Tool-only runs: **sha and stat identical**.
  - Script run: the content of every tracked file was identical. Only `.git/index` was rewritten, by the script's own `git diff --quiet HEAD` stat refresh (see A-2), and `git diff-index --cached HEAD` is clean.
- `check-evidence-diff.ps1 -Repository . -BaseRef 8004259c.. -HeadRef 9ffec2d9..`: commits=1, violations=0.
- **Landability.**
  - `merge-tree` against origin/rc/2026-10-27: clean.
  - A whole-branch merge into origin/master 6f97e999e conflicts because of the branch's rc-only ancestry.
  - The single commit cherry-picks cleanly onto master (`merge-tree --merge-base 8004259c`): tree 5ce922ca.

## Verified (no defect)

- **Fail-closed paths.**
  - A pass-1, copy or pass-2 timeout calls `Program.Fail` and then `Environment.Exit(2)` (SolutionAudit.cs:81-100).
  - A copy exception propagates outside the try to Main's catch and exits 2.
  - A pass-2 exception still refuses with exit 1, as before.
  - The script's outer wait returns Code=2, which refuses with exit 1. `Kill($true)` killed the whole tree: no leftover processes were found after the runs.
  - The approve decision never depends on elapsed time: a larger budget only delays the refusal.
  - `not-built` lines are informational only.
- **Evaluation only.** The tool uses `Project.FromFile` with no restore and no target, and ArtifactsPath points at a never-created temp directory. It still evaluates twice, in the repo and then in a sibling canary, which is the pre-existing design.
- **What the canary writes and links.** The canary writes only fresh paths: docs `*.md` and the added `docs/c1483-canary/canary.md`, both written before any link exists at that path. `README.md` is skipped. Tracked symlinks are recreated. Disposal unlinks the tree.
- **Linux link mechanics.** `libc` `link()` resolves on Linux, as the experiment shows.
- **Doc claims.** The doc's numbers match the code: 45 s per pass on Linux, 120 s on Windows, copy 16 s, total 106/256.

## Defects

**D-1: The core fix (one clock per pass) has no witness.**
- **Where:** tools/Antiphon.LandGraphAudit/SolutionAudit.cs:25-51 and 81-100 (`Bound`, `BoundCopy`, onTimeout dispose, `DeleteActive`). The new tests are LandGraphEvaluationScriptTests.cs:297-347.
- **Failure:** The new tests cover only the `--print-budget` arithmetic and the script's outer wait, using a fake `dotnet` shim. All of these mutations stay green across all 235 tests, because real evaluations finish in 25–40 s:
  - reverting `Run` to a single clock around pass 1 + copy + pass 2 (the exact 28bbb58e bug);
  - removing the per-phase bounds (the only bound left is the 166 s watchdog);
  - letting a timed-out pass return instead of exiting 2.
- **Why:** "Each pass has its own clock" and "a timeout still exits 2" are the claimed fix for the native Windows failure and the fail-closed guarantee of a gate that admits lands.
- **Fix:** Add a witness-only phase-budget input to the tool, such as `--pass-budget-ms` / `--copy-budget-ms` accepted only with `--repo`/`--solution` and never set by the script, or use an internal seam. Then add fixture-repo tests that:
  - give pass 1, then the copy, then pass 2 in turn a budget that expires, and require exit 2, `evaluating ... exceeded`, no `approved`, and no canary directory left;
  - prove the clocks are per phase: each phase fits its budget but the sum exceeds one phase budget, and the run is approved.

**D-2: Abnormal exits leave a hard-linked canary beside the live worktree. The leak predates this commit; the shared data is new.**
- **Where:**
  - scripts/audit-land-verification-graph.ps1:56-59 and 68-73: the outer-wait kill does no canary cleanup.
  - Program.cs:42-46: the watchdog Timer calls `Environment.Exit(2)` without cleanup.
  - SolutionAudit.cs:93-100 with CanaryRepository.cs:75-82: on a copy timeout, `DeleteActive` deletes the root while the copier thread keeps creating links.
- **Failure (reproduced):** `audit-land-verification-graph.ps1 -Check -ToolPath <tip dll> -EvaluationBudgetSeconds 22` exited 1 (`evaluating Antiphon.sln exceeded 22 s` / `refused (exit 2)`), which is correct. It left `<repo parent>/antiphon-landgraph-canary-6d172f57...` with 9,137 files, and its `Antiphon.sln` shares inode 24427575 with the live clone (nlink 2).
  - The same kill path is reached whenever startup exceeds the script's 30 s grace (see A-3).
- **Why:** Before this commit a leaked canary was an independent byte copy. Now it is a tree of hard links sharing data with the live worktree, sitting in the worktrees parent directory (`/work/worktrees/`, `C:\Antiphon\worktrees\`). Any later in-place write into that stray tree silently changes live worktree files: an editor save, a `File.WriteAllText`-style tool, a formatter, or a restore rewriting a lock file. On NTFS each leak also consumes link count (cap 1023); once a file reaches the cap, `CreateHardLinkW` fails with 1142, which A-1 turns into a permanent exit-2 refusal.
- **Fix:** Remove the canary on every exit path, or never leave a linked tree behind:
  - make the copier cancel cooperatively, then delete the canary before `Environment.Exit`, both on phase timeouts and in the watchdog;
  - have the tool announce its canary root (or use a PID-derived name) so the script deletes that confined path after `Kill`;
  - optionally, sweep stale `antiphon-landgraph-canary-*` siblings at startup.
  - Add a witness: a kill or timeout during pass 2 leaves no canary directory.

**D-3: The changed OS branches of `TryHardLink` have no witness (docs/testing-and-build.md#os-branch-witnesses).**
- **Where:** CanaryRepository.cs:56 and 91-106, covering the Linux `link()` branch with its EXDEV(18) copy fallback and the Windows `CreateHardLinkW` branch with its error 17 copy fallback.
- **Failure:**
  - No test asserts the observable result: non-doc files linked, docs written fresh rather than linked, originals byte-identical with nlink restored.
  - No test drives either copy fallback.
  - A mutation that links the docs and then writes the canary text, which writes through to live `docs/*.md`, would be caught only indirectly, by the end-of-script `git diff --quiet HEAD` on the real-repo tests, and only after it had already rewritten the developer checkout.
  - The Windows branch has no native witness.
- **Why:** This is the data-integrity invariant the brief asks to prove. The experiment above is review evidence, not regression protection.
- **Fix:**
  - Add a fixture test that runs the canary with a hook invoked mid-pass (or keeps the canary root) and asserts that linked files share the source identity (inode on Linux, file ID on Windows), that docs do not, and that originals are unchanged after dispose.
  - Add a seam test that forces the link to fail with EXDEV/17 and asserts a byte copy is made.
  - Schedule a native Windows witness.

## Advisories

- **A-1: Any link error except EXDEV/17 refuses with exit 2, where the old `File.Copy` succeeded** (CanaryRepository.cs:93-104). This is fail-closed, but it is an availability regression. Examples:
  - Linux EPERM (`fs.protected_hardlinks` on files owned by another user, or FUSE/9p/vboxsf/CIFS mounts without link support) or EMLINK;
  - Windows `ERROR_ACCESS_DENIED` (no FILE_WRITE_ATTRIBUTES), `ERROR_SHARING_VIOLATION`, 1142 (too many links), or 1/50 on FAT, exFAT or shares.

  Falling back to a copy on any link failure is data-safe.
- **A-2: The audit now changes metadata in the live tree.** `link()` bumps the ctime of every linked tracked file on Linux: `Antiphon.sln` ctime was 05:01:59 against an mtime of 04:09 after an audit. Git's stat cache (`core.trustctime`) is therefore invalidated after every audit, so the next status or diff re-hashes about 9.1k files and rewrites `.git/index`; this was observed. That contradicts Program.cs:11, "The tool writes nothing". Git for Windows uses creation time, so Windows is unaffected. Document it or accept it.
- **A-3: The two startup graces disagree.** The script grace is 30 s (ps1:133-135), but the tool's `StartupGraceSeconds` is 60 (EvaluationBudget.cs:31), giving a watchdog of 166 s on Linux and 316 s on Windows. The script's kill at 136/286 s therefore always fires first: the tool watchdog never fires under the script, and a startup over 30 s ends on the kill path, which leaks the canary (D-2). Suggest `--print-budget` also prints `ceiling=`, and the script waits on that.
- **A-4: The Windows budgets are guesses, and the docs do not say so.** The 120 s pass floor and the Linux-derived 16 s copy budget have no Windows sample. docs/orchestration-loop.md:2371-2378 neither marks them as provisional for Windows nor says how to raise them (edit the `EvaluationBudget` constants with fresh measurements). It also omits the script's total + 30 s wait and `-EvaluationBudgetSeconds`, which appear only in the script header.
- **A-5: `--print-budget` exits 0, the same value as `Program.Approved`.** Only the script consumes it, and only when it is argv[0], so the risk is low. A distinct exit code would be clearer.
- **A-6: The native Windows witnesses are pending.** These were not run here: the `CreateHardLinkW` branch, the 120 s floor in a real run, and C1385_RegistryMatchesNarrowGraph on the desktop (the original failure). `EvaluationBudgetRefusesSlowDotnet` skips on Windows, so the script's wait has no Windows witness either.
- **A-7: Timeouts are consistent.** `RunScriptAsync` and the C1385/EvaluationTests deadlines were raised from 240 s to 360 s, which is above the Windows 286 s script wait. The new tests are bounded (60 s / 180 s), and the 30 s fake sleep is killed at 1 s; the test took 10.7 s.

## Not done / scope

- **Interim scope.** The whole Unit lane was not run, as the brief said. PCs stay pending. There is no checkpoint-tool receipt: the brief ordered build-slot + `dotnet exec` runs, so `reviewedSourceClean=false`.
- **Scratch left in place.** The scratch clones are under `/tmp/claude-1654/-work-worktrees-task-4c3ba19a/e8f99d04-f7ab-4532-a223-102fb5153ced/scratchpad/`: `a/repo` (with its bin-review outputs), `base/`, and the deliberately leaked canary `a/antiphon-landgraph-canary-6d172f57257b4de2a70aaa705932eda5`. Removing them would need `rm -r`, which the cleanup rule forbids.
