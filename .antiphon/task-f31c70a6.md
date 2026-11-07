# Review CARD-1728 round 1 (task f31c70a6): Unit lane out of per-task Final

Subject: origin/feat/card-task-b1ceb641 tip ecfb032da692304543eab6b2f4d3db2547c6d7b9 (Grok Code b1ceb641-7452-4315-be8b-d9812d4818a4; red da5411a4ce71d19140c0ddabab7cc66c3aa03df1, fix ecfb032da). Base c7216a06f55fba7dc255702c99217248e1b4a20e. Master edb2c20156afa4da503b1b29fb83f1f953987bd5.
Reviewed on runner mirror /work/worktrees/task-f31c70a6 (branch feat/card-task-f31c70a6 at the same tip). Read-only on the subject; no product file changed.

## Verdict: one blocker (B-1), two advisories

### B-1 The Code bundle still states the old ordinary scope and two guards pin it
- Where: `server/Bundles/stage-code.md` line 3 `SCOPE: Unit plus the named affected integration classes.`; pins in `tests/Antiphon.Tests/Application/ScopedVerificationInstructionTests.cs` (C487_G127 line 19 `code.ShouldContain("Unit plus the named affected")`, C487_G129 line 36) and `tests/Antiphon.Tests/Application/VerificationRoundInstructionTests.cs` line 25 (`"Unit plus the named affected integration classes"`, touched by this diff and left in place).
- Failure: that line is the compressed form of the removed instruction. d429ae91f wrote it as `SCOPE: Ordinary verification is Unit plus the named affected integration classes`, and the long form `Execute the Unit lane. Execute named affected integration classes` is what this diff deletes from docs/testing-and-build.md line 286. After the fix the bundle says "Unit plus the named affected integration classes" at SCOPE and "closed rows, named classes, not the whole lane" at ROUND: two contradictory ordinary-scope statements in the same 2.4 KB. The witness `PerTaskFinalUnitLaneWitnessTests` scans stage-code.md but its `RequiringPhrases` do not include this phrase, so it passes with the requirement still present.
- Why it matters: a Code delegate reads SCOPE first and runs the Unit lane, hits the 45-minute shard cap and reports incomplete, which is the outcome CARD-1728 exists to stop. Two guard tests still pin the old scope sentence, so the class is not closed by one witness (standing rule 4).
- Fix (same Code owner): reword SCOPE inside the 2,480-char cap (TaskPlatformGuidanceTests line 18; InstructionBundleTests caps at 2,500; file is 2,408 now), for example `SCOPE: Closed rows plus the named affected integration classes; the Unit lane is nightly/rc. Unit omits delivery, landing, leases, persistence. ...` (+36 chars = 2,444). Re-pin C487_G127/G129 and VerificationRoundInstructionTests line 25 to the new sentence (keep G127's `skip the Unit lane` negative). Add `Unit plus the named affected integration classes` to the witness `RequiringPhrases` so the old line cannot return. Re-run the 14-class selection below.

### A-1 Review bundle carries the same compression
`server/Bundles/stage-review.md` line 2 `SCOPE: Re-run the claimed Unit and named integration checks in one checkpoint-tool run` is pinned verbatim in `tests/Antiphon.Tests/Application/TaskPlatformGuidanceTests.cs` lines 97 and 126. "claimed" keeps it self-limiting (a Code report that claims no lane leaves nothing to rerun), so advisory. Reword with B-1 (file is 2,563 of 2,580; `claimed rows and named class checks` is shorter) and re-pin both lines.

### A-2 Witness is literal-phrase only
Synonyms such as "Unit lane row" or "[Category=Unit] row" would bypass the scan. Acceptable for a narrow witness; extend `RequiringPhrases` when a new form is found rather than adding witness classes.

## Checks against the brief
1. Completeness: grep of AGENTS.md, TESTING.md, docs/, server/, scripts/, .claude/skills (excluding plans, cards, investigations) finds no remaining whole-lane requirement except B-1/A-1. Remaining `[Category=Unit]` mentions are nightly/rc or historical (test-performance-standards.md R5 note, host-cleanup.md, shard section, test-build-slot.ps1 fixtures).
2. Nothing weakened: Interim branch of `DelegationReportFormatter` (lines 343-348) and the Interim bullets in release-gates.md unchanged; standing rule 8 only gains the ownership sentence; rule 3 loses only its parenthetical example; AGENTS.md/CLAUDE.md untouched; no scripts/, nightly, shard or `tests/test-execution-policy.json` change; the CARD-1573 shard section is reworded, not resized.
3. Witness: real file scan over 12 task-facing paths; 2-path allowlist asserted outside the scan and existing. Base-text mutant (base versions of the 6 non-test files) red with 7 hits over 6 files. PC-1 mutant (restore `ordinary scope: the whole Unit lane` in the formatter) red 1/1. `VerificationRoundBriefTests` asserts the complete new profile sentence from the real formatter; `VerificationRoundInstructionTests` asserts the new ROUND clause from the real bundle.
4. stage-code.md: 2,408 bytes, 0 non-ASCII bytes, 0 CR; caps 2,480 and 2,500 hold (both guards green).
5. Migration: none in diff.

## Runs (all through scripts/build-slot.ps1, native output, UseAppHost=false, TEMP=/tmp)
| Run | Lease | Result |
|---|---|---|
| `dotnet build tests/Antiphon.Tests` | fd796a95 waited 0s held 176s | 0 errors, 1,804 warnings |
| Green: `(PerTaskFinalUnitLaneWitnessTests*)\|(VerificationRoundBriefTests*)\|(VerificationRoundInstructionTests*)\|(TaskPlatformGuidanceTests*)\|(InstructionBundleTests*)\|(RepairSourceDocumentationTests*)\|(TestClassificationPolicy*)\|(TestClassificationGuard*)\|(SlowTestRegistryMerge*)\|(SlowTestTripwire*)\|(NativeLinuxPolicyWitnessTests*)\|(MechanicalPcContract*)\|(MechanicalPcRehearsal*)\|(C1729LayeringGuardTests*)` | 6cb976a7 waited 0s held 208s | 174 passed, 0 failed, 0 skipped, 3m22s |
| M-A base-text mutant, witness only | 02ee7619 waited 15s held 8s | 1 failed (7 hits), restored |
| M-B PC-1 sentence mutant, witness only | eb4de427 waited 60s held 7s | 1 failed (1 hit), restored |

Green per class: C1729LayeringGuardTests 9, InstructionBundleTests 78, MechanicalPcContractTests 11, MechanicalPcRehearsalTests 10, NativeLinuxPolicyWitnessTests 3, PerTaskFinalUnitLaneWitnessTests 1, RepairSourceDocumentationTests 3, SlowTestRegistryMergeTests 5, SlowTestTripwireTests 2, TaskPlatformGuidanceTests 5, TestClassificationGuardTests 1, TestClassificationPolicyTests 39, VerificationRoundBriefTests 1, VerificationRoundInstructionTests 6.
Tree verified clean (`git status --short` empty) after each mutant restore; HEAD ecfb032da throughout.
Evidence-diff: `scripts/check-evidence-diff.ps1 -Repository . -BaseRef c7216a06f... -HeadRef ecfb032da...` commits=2 entries=0 violations=0. `git merge-tree --write-tree origin/master HEAD` clean, tree 2face2edcaa42ad8ff1b4f6bc03b784ece8f3770. `git diff --check` clean.
TRX/logs (ephemeral scratchpad): /tmp/claude-1654/-work-worktrees-task-f31c70a6/9e6be7bd-987f-428f-b2e5-8b274eac6f61/scratchpad/{trx,logs}.
PC-1 stays pending for method-scoped SourceLanding Mutation.
