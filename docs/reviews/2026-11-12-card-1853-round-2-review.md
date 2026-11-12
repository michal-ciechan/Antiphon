# CARD-1853 round 2 Review (task 1aa83aa9)

Subject: `origin/feat/card-task-9e1ebc66` tip `8c4c459ffd9bcbdc655d640968648f041fb65200`
(Code task 9e1ebc66, Grok author; original Code owner 202bdbb3). Round 2 is diff-only from the
round-1 reviewed SHA `1227766a70a5b4df90a51307b97c53aaebec0eae`: one commit, one file
(`tests/Antiphon.Tests/TestHelpers/DescendantLeakScopeTests.cs`, +4/-2). Round 1 report:
`docs/reviews/2026-11-12-card-1853-round-1-review.md` on `feat/card-task-00d82b2d`.

Verdict: **CLEAN**. Round-1 blocker D-1 is fixed. No new blockers or regressions.

## D-1 check

- `C24AdoptsInsideItsTry` now applies `.ReplaceLineEndings("\n")` to the `File.ReadAllText`
  result before any search. All five searches run on the normalised `source`/`body`: the two
  anchor `IndexOf` calls (single-line literals), `IndexOf("\n        try\n")`,
  `IndexOf("leaks.AdoptOrKill(process)")` and `ShouldNotContain("leaks.Adopt(process)")`. No
  search runs on unnormalised text. The only multi-line literal, `"\n        try\n"`, is LF and
  matches the normalised text.
- Nothing else changed: the remaining changed lines are the XML doc comment.

## Runs at 8c4c459ff (server2 Linux, one build-slot lease per command, native output, UseAppHost=false)

| Run | Result |
|---|---|
| Build `tests/Antiphon.Tests` | exit 0, 175 s lease |
| `(DescendantLeakScopeTests*)\|(RepositoryMutationLeaseTests*)` | 37/37 green |
| Guard batch `TestClassificationPolicy*\|TestClassificationGuard*\|SlowTestRegistryMerge*\|SlowTestTripwire*\|NativeLinuxPolicyWitnessTests*` | 52/52 green |
| CRLF probe: tip `RepositoryMutationLeaseTests.cs` converted to CRLF (810/810 lines), witness alone | 1/1 green (round 1: red at :124) |
| Mutant M1 LF: base C24 (bare `leaks.Adopt(process)` before `try`, no AdoptOrKill) | 1/1 red at :127 `should be greater than` |
| Mutant M1 CRLF | 1/1 red at :127 |
| Mutant M2 LF: `AdoptOrKill` moved before `try` | 1/1 red at :127 |
| Mutant M2 CRLF | 1/1 red at :127 |
| Mutant M3 CRLF: tip plus a bare `leaks.Adopt(process)` kept before `try` | 1/1 red at :128 `should not contain` |
| `check-evidence-diff.ps1` 1227766a7..8c4c459ff / 75878de23..8c4c459ff | commits=1 / 3, entries=0, violations=0 |
| `git merge-tree --write-tree origin/master(077f0b3a7) 8c4c459ff` | clean, tree f9c832ae2 |

The CRLF mutants show that each of the three assertions still goes red on a CRLF checkout. It is
not vacuously green: `tryAt` passes on CRLF, and both the order and absence checks fire. Each
probe or mutant replaced only the worktree source file that the witness reads at runtime. The
file was restored byte-for-byte after each run (cmp checked, `git status` clean).

## Pending

- Native Windows proof of a real `AssignProcessToJobObject` failure (manual, carried from round 1).
- Every PC stays pending for SourceLanding Mutation.
- Round-1 advisories A-1..A-3 stand as filed in round 1. None is new.
