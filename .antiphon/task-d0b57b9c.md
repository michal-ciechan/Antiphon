# Review d0b57b9c: CARD-1677 S2 round 2 (diff b15f66352..82d5f8f39)

**Verdict: Clean.** I reviewed tip 82d5f8f39ba6be62924fe008aa2ae1efc7606533 (subject branch feat/card-task-6f66c43e; 4f9999e70 adds only the Code report). I found no D-n defect.

## (1) D-1 completeness
- All 10 family patterns in `RegisteredFamilies` start with `^` and end with `\z`. No family pattern contains `$`. They use `RegexOptions.CultureInvariant` only, so matching is case-sensitive and `[0-9a-f]` is ASCII.
- The diff leaves no other pattern in the S2 delete decision. `HexRun` and `DigitRun` only normalize report prefixes. The server TempSweep tick sends a request and matches no names.
- `HasControl` (`char.IsControl`) rejects a name before any family regex sees it. This applies to top-level names and container children. It covers `\n`, `\r`, `\t`, `\0`, `\x7f` and U+0085. It does not cover U+2028 or U+2029 (`IsControl` is false for both).
- Probe (pwsh, same .NET regex engine, patterns extracted from the source): each sample with a prefix or suffix of U+2028, U+2029, U+0085, space, `.`, `\n`, `\r`, `\t`, NUL, `\x7f`, `/`, `/x`, U+FEFF or U+200B, plus uppercase and fullwidth-digit variants: 170 of 170 rejected, 0 matched, and all 10 bare samples matched.
- claude-scratch:
  - The container match is exact (`f.Container == name`). `claude-1654\n` falls to the top-level path and is reported unregistered.
  - A child is a single filename (it cannot contain `/`). With `\z`, `relative` must be exactly `claude-1654/-work-worktrees-task-<8hex>`.
  - So `name[^8..]` always reads the 8 hex characters. Extra path components and a trailing slash cannot match.

## (2) Seams are inert by default
- `RunnerTempSweep.AfterDeleteInventory`:
  - It is an `internal` settable `Action<string>?` and defaults to null. It is invoked with `?.`.
  - Its only setter is `C1677TempSweepNativeTests`. InternalsVisibleTo is limited to `Antiphon.SessionRunner.Tests` and `Antiphon.Tests`.
  - Nothing on the wire, in config or in the environment reaches it.
- `CreateDefault(retiringRoot, tempRoot)` is internal. The public `CreateDefault(retiring)` delegates with `"/tmp"` and composes exactly the same arguments as before.
- `PhoneHomeRuntimeAdapter.TempSweepIsLinux`:
  - It is an `internal init` property that defaults to `OperatingSystem.IsLinux()`. `Program.cs` never sets it.
  - The advertisement (`TempSweepFeatures(TempSweepIsLinux)`) and the answer both use it.
  - The dispatcher's tempSweepV1 gate is still in front.
  - Result: a Windows runner neither advertises nor answers the sweep. Linux behaviour is byte-for-byte unchanged.

## (3) Witnesses can go red (scratch detached worktree at 82d5f8f39, uncommitted, removed afterwards)
- Build A (SessionRunner.Tests, one lease per build and one per run), 2 of 17 red, each at its own witness:
  - M1, dispatcher `TempSweepV1` gate removed: `Sweep_is_refused_without_the_capability(False)` was red (Kind Result instead of Error).
  - M2, `DeleteOwnedAsync` replaced by `Directory.Delete(path, recursive: true)`: `Tree_changed_after_the_delete_inventory_is_refused` was red (verdict removed instead of kept).
- Build C:
  - c1514 pattern `\z` changed back to `$`, with the top-level `HasControl` gate also removed. 3 of 10 were red:
    - `Family_regexes_are_anchored_and_exact`
    - `Family_name_with_a_line_break_is_unregistered_and_kept` (a newline name became a candidate)
    - `Control_character_name_is_refused_before_any_family_match`
  - With only `\z` reverted, the registry test is the killer. The end-to-end newline sweep case is masked by the `HasControl` gate, which is intended defence in depth. That is why the sweep mutant also removes the gate.

## (4) Defaults
`git diff b15f66352..82d5f8f39 -- server/appsettings.json` is empty (0 bytes).

## (5) Bookkeeping and runs
- The round-2 diff touches 7 files: the plan amendment, the adapter, `RunnerTempSweep`, and 4 test files. It changes no policy row, census entry or pin.
- `check-os-witnesses.ps1 -BaseRef b15f66352… -HeadRef 82d5f8f39… -Plan <amendment>` exits 0 with "complete syntax/inventory".
  - The worker's declaration file is gitignored in their mirror, so I re-declared the round-2 diff myself in scratchpad `osw-r2.json`.
  - Witnesses: adapter on CP-14 (portable-forced), statx on CP-8 (native Linux). The Windows `IsRegular` branch is pending native: nothing ran on Windows.
- `check-evidence-diff.ps1 -Repository . -BaseRef b15f66352… -HeadRef 82d5f8f39…`: commits=4, violations=0, exit 0. Over the whole S2 range, f32aed42e..4f9999e70: commits=7, violations=0.
- Checkpoint run 20261103-193451-7cb9 at 82d5f8f39, all sourceState=clean and buildSource=verified. `validate --evidence report.json --expected-source-sha 82d5f8f39…` returned VALID rows=6.

  | CP | Executed | Passed | Failed | Skipped |
  |---|---:|---:|---:|---:|
  | CP-7 | 27 | 27 | 0 | 0 |
  | CP-14 | 10 | 10 | 0 | 0 |
  | CP-8 | 7 | 7 | 0 | 0 |
  | CP-13 | 3 | 3 | 0 | 0 |
  | CP-9 | 7 | 7 | 0 | 0 |
  | CP-5 | 69 | 69 | 0 | 0 |

- No `c1677-dispatch-*` or `c1677-tmp-*` residue is left in /tmp.
- `git merge-tree --write-tree origin/master(e56e4d1b6) 4f9999e70` is clean.

## Advisories
- **A-1, land order with CARD-1686.** The brief's expectation that this "should merge cleanly" holds for the round-2 diff but not for the whole branch.
  - S2's round-1 commit 5ecdb7bb9 re-pins `tests/test-execution-policy.json` (policyHash), `scripts/test-live-lane-run.ps1` (PinnedInputDigest) and `scripts/test-rc-platform.ps1` (inventory and documentClasses).
  - CARD-1686 c9cb7349 (ed67056c5) and ef4c37b4 (4cedfe0a2) each also re-pin those three files.
  - Each branch merges cleanly onto master alone. A simulation of S2 landed first and then either 1686 branch conflicts in all three files.
  - Whichever lands second needs a re-pin pass.
- **A-2.** `Normalize` escapes only `char.IsControl`. U+2028 and U+2029 appear raw in unregistered report prefixes. This is report text only; no delete path is involved.
- **A-3.** Outside S2 and this diff, S1 patterns still end in `$`:
  - `RunnerWorkspaceRetention.Residues` (`^leaf\.[0-9a-f]{8}$`, retiring-residue selection)
  - server `RunnerWorktreeRetentionService` `LeafPattern` (guarded by exact equality) and `OwnerPrefix`
  - `RunnerWorkspaceIgnoredContent`

  A consistency card is worth filing. None of these is in this round's scope.

PCs: all pending (method-scoped SourceLanding Mutation).
