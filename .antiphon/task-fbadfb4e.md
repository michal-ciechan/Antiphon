# CARD-1608 Review (Final, Interim scope): strict UTF-8 checkpoint git output

Subject: task 474a7a72, feat/card-task-474a7a72 tip dcad3ecd87dcd0a7572d65529de27418ecdea964
(red witness 7ae17ac6a3e753bd96e092d18433a744db2b695c), base rc/2026-10-27 91b45006b4d76f144c2651180039aece5a9d7938.

Verdict: two defects (D-1 is a Unit-lane regression; D-2 is a Windows-lane red by construction). The production change itself is correct and fails closed. No fail-open path found.

## Defects

### D-1 Invoke-CheckpointGit now needs a helper that selective-import harnesses do not load
- Where: scripts/run-checkpoint.ps1:142 (call to ConvertFrom-CheckpointGitUtf8) and the catch-all at :147-148.
  The importing harnesses are scripts/test-checkpoint-bounded-reader.ps1:57 (`Import-Function 'Invoke-CheckpointGit'`)
  and scripts/test-checkpoint-output-inventory.ps1:28 (a list of named functions without the helper).
- Failure: a harness that AST-imports only Invoke-CheckpointGit throws CommandNotFoundException on the decode.
  The catch-all maps that to `Reason=git-unavailable`, `Exit=128`.
  `CheckpointBoundedReaderTests.changed_readers_bound_fifo_and_exited_child_pipes(script-git, ordinary)` is RED at the
  head with "healthy Git mismatch". The same probe is green at base:
  `node scripts/test-checkpoint-bounded-readers.mjs <repo> <Antiphon.Checkpoints.dll> script-git ordinary`
  gives base exit 0 and head exit 1. It was red in both of my runs.
  Output-inventory still passes only because its fixture never reaches git. Its imported Invoke-CheckpointGit is equally broken.
  A standalone repro that dot-sources only Invoke-CheckpointGit, then runs `rev-parse HEAD`, gives
  `exit=128 reason=git-unavailable`.
- Why: this class is a Unit-lane test, so the Unit lane goes red and the change cannot land. The CheckpointBoundedReaderTests
  usage entry is at scripts/lib/checkpoint-usage.ps1:675.
- Fix: inline the strict decode inside Invoke-CheckpointGit, as checkpoint-source.ps1:30 does with a local
  `$utf8 = [System.Text.UTF8Encoding]::new($false, $true)`. Alternatively, add ConvertFrom-CheckpointGitUtf8 to both import lists.
  Then rerun CheckpointBoundedReaderTests and CheckpointFreshOutputSealTests.
  Optional: let only a DecoderFallbackException produce a Reason, so that a missing command fails loudly.

The author's regression filter (from `delegate.ps1 -Status 474a7a72`) left out CheckpointBoundedReaderTests. That class AST-imports the changed function, so the author never saw this red.

### D-2 C1608 encoding witness is Linux-only, but it is not gated or rostered
- Where: tests/Antiphon.Tests/Checkpoints/CheckpointGitEncodingScriptTests.cs:20 (`C1608_invalid_utf8_git_output_is_refused`)
  and scripts/test-checkpoint-git-encoding.ps1:104-117.
- Failure: on the Windows Unit lane, encoding mode writes a `#!/bin/sh` file named `git` and runs `& chmod +x`.
  On Windows, `chmod` is not a command, so the script throws under ErrorActionPreference Stop and exits 1. The test goes red by construction.
  Even with chmod present, Windows would not run that shim as `git`.
  The author's report says "The new class is portable Unit, so no roster row". That claim is false for this case.
- Why: Linux-only Unit tests must start with `NativeLinuxGate.Require(NativeLinuxReason)` and carry a WindowsUnitSkipRoster entry.
  C1210 pins the roster at 61 identities and 72 results. C1210 stays green on Linux only because this case was never registered.
- Fix: either gate the encoding case with NativeLinuxGate, add a roster entry and update the C1210 counts, or make the shim portable
  (a `git.cmd` that writes byte 0xFF on Windows). Then run the C1210 class.

Pending, not a defect: the real bug is Windows console code-page decoding. Its only witness is the Linux latin1 seam on ProcessStartInfo.
That keeps the real type, which is allowed under os-branch-witnesses, but no native Windows witness ran. It stays pending.

## Checked and found sound (brief items 1-5)
1. Callers (run-checkpoint.ps1 177/185/212/831/834/837/856/946/948/984) all test `.Reason` before reading stdout.
   - ASCII output is byte-identical. The witness proves `seed.txt\0`.
   - An empty output still gives `''`.
   - `UTF8Encoding.GetString` keeps a leading U+FEFF. That is an improvement: the old StreamReader removed a BOM, which would have renamed a path that starts with U+FEFF.
   - The runner uses only rev-parse, `ls-files -z [-s|--others|--ignored]` and `status --porcelain=v2 -z`. Nothing parses commit or author text, so a latin1 commit message or author name cannot trip the check.
   - Git for Windows always emits UTF-8 paths. Invalid bytes can arrive only from Linux byte filenames. Failing closed is correct there, because the old lax decode stamped such a path `absent|0|` on both sides and missed the change.
2. Timeout and start failures are unchanged. Stderr is still text and unchanged.
   - A non-zero git exit with valid stdout is unchanged. With invalid stdout it reports git-encoding; both are refusals (advisory A-2).
   - Memory use is the same order: MemoryStream plus one string, where the old path read into a string.
3. scripts/run-checkpoint.ps1 is `#requires -Version 7.0` and already uses `ArgumentList`, `$IsWindows` and `Convert.ToHexString`, so Windows PowerShell 5.1 does not apply. `::new()` works on 5.0+ anyway. The diff is ASCII-only.
4. The witness is red at base and green at the head. I ran the head witness script against a `git archive` export of the base scripts:
   - tamper mode: base `untracked-non-ascii missed kind=`, head PASS.
   - encoding mode: base `invalid UTF-8 accepted reason= exit=0 stdoutHex=EFBFBD`, head PASS.

   The legacy code-page seam lives only in the witness: a text patch of the function, dot-sourced in the test process. No production parameter or environment variable switches strictness off. The encoding mode uses an sh shim, so it is Linux-only (D-2).
5. checkpoint-source.ps1 Get-CheckpointSourceOnce already decodes untracked paths strictly (line 30/77), so both now fail closed in the same direction.

## Advisories (no Review rerun needed)
- A-1: tools/Antiphon.Checkpoints/BuildConfinement.cs:835. `Git()` still calls the text `CheckpointProcessRunner.Run`, which decodes with the console code page on Windows and with replacement on Linux. It feeds the same tracked-snapshot, ls-files `-z -s` and `--others --ignored` logic (lines 106/314/323/379/381). This is the same latent fail-open in the C# twin; it needs its own card.
- A-2: run-checkpoint.ps1:146 forces `Exit=128` on git-encoding even when git itself failed. Test-CheckpointStampRefusal then reports `git-encoding` instead of `not-a-repository` or `git-failed`. It still refuses, but the diagnostics are worse.
- A-4: Get-CheckpointTrackedSnapshot and Get-CheckpointUntrackedManifest collapse `Reason=git-encoding` into `git-ls-files`. The operator sees `refused: tracked-snapshot git-ls-files` with no hint that a non-UTF-8 filename caused it. The `--ignored` listing makes that refusal reachable from build output.
- A-5: run-checkpoint.ps1:625 still decodes the root with lax `Encoding.UTF8`. This is pre-existing and harmless: an invalid root path fails later.

## Runs (server2 Linux; one build in the build slot, then `dotnet exec` in the slot)
- Build: tests/Antiphon.Tests with OutputPath=bin-rv1608/ and UseAppHost=false, exit 0.
  This was the one unlisted extra build; the brief asked for exactly one.
- Run 2 (slot lease a5b3ac24, 24m52s):
  - Filter: CheckpointGitEncodingScriptTests, RunCheckpoint*, CheckpointSourceState*, CheckpointVerifierBoundsTests, ScriptHarnessProcess*, TestPhaseSummaryTests, RcRosterRepairTests, WindowsUnitPlatformAdmissionTests, C1514LandingSplitBudgetTests, CheckpointBoundedReaderTests, CheckpointFreshOutputSealTests.
  - Result: total 121, passed 118, failed 1 (D-1), skipped 2 (C1210_Windows_linux_only_outcomes_are_exact and C1514T_PS51AndRerun, both platform skips on Linux).
  - C1608 2/2 passed. Census cases C1404, C1577, C1584, C1410 and G2865 passed 5/5. RcRosterRepairTests passed 3/3. WindowsUnitPlatformAdmissionTests passed 4 (C1210 Linux cases green).
  - The happy path of run-checkpoint.ps1 in scratch repos is exercised end to end by RunCheckpointScriptTests (26), RunCheckpointSourceScriptTests (6) and RunCheckpointRepeatScriptTests (4), all green.
- Run 1 was killed by my own 590 s wrapper timeout after a 315 s slot wait. Its `script-eval held` red was not reproduced in run 2, so it is not counted.
- Probe lease 2ace6064: the base/head witness runs above.
- check-evidence-diff over 91b45006b..dcad3ecd8: commits=2, violations=0.
- merge-tree onto origin/rc/2026-10-27 is clean. A whole-branch merge onto origin/master conflicts add/add on rc-only files, but the two card commits cherry-pick cleanly onto master (chained merge-tree with `--merge-base`).
- All bin-rv1608 directories (33) were removed by literal path.

Scope: Interim. The brief commissioned a named narrow selection, not the whole Unit lane, and no checkpoint-tool plan run was made. PCs stay pending.
