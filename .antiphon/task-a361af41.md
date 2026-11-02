# task-a361af41: g124 CRLF witness double CR and CwdInstructionsCannotLeak

Base ead93feb00fcaf8bf31018599437f17bee8e934c. Branch feat/card-task-a361af41.

| Commit | Content |
|---|---|
| 686625bc3075f43cbdc9f6107dc5ac6a4795c38a | red witnesses (test-only) |
| cdc43e68d3241ef4afc2164d5d760a53e1208465 | fixes |

## (1) VolumeRetirementApplyGateTests::Apply_gate_crlf_copy_of_the_expected_block_matches_the_lf_script

Cause: the brief's diagnosis was right. On a Windows CRLF checkout the raw literal already holds CRLF.
`expected.Replace("\n", "\r\n")` then produced CR CR LF, and `SourceLines` turned each CR into its
own LF, which doubled every line break. This passed on Linux because the literal there is LF.

Fix (test-only): new helper `CrlfCopy(text) = SourceLines(text).Replace("\n", "\r\n")`, which
normalises first and then converts. `SourceLines` is unchanged (CRLF, then lone CR, to LF).

New witness: `Crlf_copy_of_an_already_crlf_literal_matches_the_lf_copy`. It feeds the helper a CRLF
copy of the literal and checks five things: the result equals the LF-literal copy; there is no
`\r\r`; it normalises back to the LF block; `SourceLines("a\rb\r\nc\n") == "a\nb\nc\n"`; and a
different block (`= 1`) still does not match the script.

Scan of the other CRLF witnesses added in this round (87f4a868a..ead93feb0) found none with the
same construction:
- LiveLaneEvidenceTests:494 is guarded by `IndexOf('\r') < 0`.
- RemoteScriptContractTests:3638 reads an eol=lf .sh file, and `Block` normalises it.
- LiveClaudeEvidenceWiringTests `ToCrlf` already normalises first.

## (2) LiveLaneIsolationTests::CwdInstructionsCannotLeak (V-14; the same code serves V-8)

Cause: this was not the cwd and not the fixture base. Line 177 of `LiveClaudeProfileCases.Instructions`
is the declared-admission step. The case declared
`f.Declared[Path.Combine(f.Root, ".claude/settings.json")]` (and `.claude/skills/s/SKILL.md`). On
Windows, `Path.Combine` keeps the `/`. The profile walk enumerates `Root\.claude\settings.json` and
looks it up as `Path.GetFullPath(path)`, which gives backslashes, in an Ordinal dictionary. The
declared owned file therefore missed the lookup and refused `unowned-instructions` at
`LiveClaudeProfile.cs:123`. This exactly matches the Windows stack (:123, then ctor, then
Cases.cs:177, then Run :96).

It is green on Linux because `/` is already the canonical separator there. The bug predates
88ebc077: the root has always been inside the walk. The earlier host-.claude refusal probably hid it.

Fix (test side): the case declares `Path.GetFullPath(Path.Combine(f.Root, name))`. The guard is
unchanged except for its diagnostic: `LiveClaudeRefusal(reason, path?)` now prints
`20 unowned-instructions path=<file>` for the above-root refusal (`entry`) and the
undeclared-file refusal (`path`). `Reason` is unchanged, so every `Refuses(...)` filter is
unaffected.

Witnesses (LiveLaneIsolationTests):
- `DeclaredInstructionSpellingMatchesTheWalkedPath` runs `Run("V-14", variant: ".claude//settings.json")`.
  The doubled separator is the Linux analogue of Windows' `/` vs `\`. Before the fix it reproduced
  the same stack as Windows.
- `InstructionRefusalNamesTheOffendingPath` checks that an undeclared owned CLAUDE.md, and an
  AGENTS.md above the owned root, each refuse with the full path in the message.
- Kept unchanged: `InstructionFilesAboveTheOwnedRootRefuse`, `HostAncestorClaudeDirectoryStaysOutsideTheOwnedClosure`.

Note for the caller: `Antiphon.Agents.Pty.Tests` `LiveClaudeIsolationWiringTests.CwdInstructionsCannotLeak`
runs V-8, which goes through the same `Instructions` loop with the same `/` names. On Windows it
must have failed the same way unless it did not run in this group. The brief says it passed, so
the native rerun should confirm that it actually ran.

## Runs (server2/Linux, build-slot, UseAppHost=false)

| Run | Build | Result |
|---|---|---|
| Red, 686625bc, classes VolumeRetirementApplyGateTests + LiveLaneIsolationTests | bin-a361r | 23 total: 20 passed, 3 failed (the three new witnesses, failing as described) |
| Green, cdc43e68, same two classes | bin-a361g | 23/23 passed |
| Pty LiveClaudeIsolationWiringTests, cdc43e68 | bin-a361p | 6/6 passed |
| Pty CwdInstructionsCannotLeak with C1545_PROFILE_CONTROLS=V-8 | bin-a361p | passed; PC-40..44 and PC-47, plus PC-46 for all six variants including `.claude/settings.json`: assertion-red, then restored-green |
| Mutant build (lone-CR rule dropped from SourceLines + walk stopped at the owned root, i.e. above-root guard dropped) | bin-a361m | 6 targeted: 4 failed as expected (Crlf_copy... at the lone-CR assertion; InstructionRefusalNamesTheOffendingPath above-root; HostAncestor...; InstructionFilesAboveTheOwnedRootRefuse); the 2 controls passed. Sources restored to cdc43e68 bytes, which is the green run |

The other mutants were proven by the red commit:
- Pre-normalisation removed: 686625bc is exactly that state.
- The "cwd moved back" equivalent (non-canonical declaration) and the refusal printing no path:
  both are the 686625bc state, red.

## Gates

- `audit-land-verification-graph.ps1 -Check` result: approved, digest=D92450D9AABCBF9C783B506F73DF06C45635F415D03E65C5F1A97AF415CC36BE count=69. `-Update` was not run.
- `check-evidence-diff.ps1 -Repository . -BaseRef ead93feb0 -HeadRef HEAD`: commits=2, entries=0, violations=0.
- `git merge-tree --write-tree origin/master HEAD` is clean. The same against origin/rc/2026-11-01 is clean, and the two commits cherry-pick cleanly onto rc in sequence (`--merge-base` chain).
- All `bin-a361*` and `bin-land-audit` directories were deleted.

## The native Windows rerun must capture

- `VolumeRetirementApplyGateTests` (whole class): Apply_gate_crlf... and Crlf_copy_of_an_already_crlf... both green.
- `LiveLaneIsolationTests` (whole class), including CwdInstructionsCannotLeak, DeclaredInstructionSpellingMatchesTheWalkedPath and InstructionRefusalNamesTheOffendingPath.
- `Antiphon.Agents.Pty.Tests` `LiveClaudeIsolationWiringTests/CwdInstructionsCannotLeak`: confirm it executed, not just that it was absent from the failures.
- On any remaining refusal, the `path=` value in the message.

--- next stage ---
next: Review (diff-only, for (2): guard-adjacent oracle and refusal message), then Merge-role fast-forward; (1) is test-only
handoff: feat/card-task-a361af41 at cdc43e68d3241ef4afc2164d5d760a53e1208465 on base ead93feb0; no landing done
artifact: .antiphon/task-a361af41.md
