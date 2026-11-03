# CARD-1676 Review round 1 of 3 (full), task 689df4fd

Outcome: **clean.** No D-n. Tested SHA 851bae3a19fbfa9126da72543f959dccef9aebb4 (code commit ac317e67f47b876013e19bf5607a3dc290758723; 851bae3a1 adds only the Code report). Subject: Code task 4555c064-96b9-44d5-9b5b-b16bc488cc6e, branch feat/card-task-4555c064, base master 7b920d84a.

## Runs (server2 Linux; each build and run in its own build-slot lease; native output; -p:UseAppHost=false; TEMP=/tmp)

| Run | Tree | Filter | total | failed |
|---|---|---|---|---|
| Green, 13 CARD-1647 classes + C1676 | 851bae3a1 clean | `/*/*/(AgentBundleAttachmentTests*)\|(AgentTaskCardBindingTests*)\|(CheckpointManifestDocumentationTests*)\|(D2AdvisoryDocumentationTests*)\|(InstructionBundleTests*)\|(MechanicalPcContractTests*)\|(PostLandMutationContractTests*)\|(ReviewEvidenceParserTests*)\|(ReviewRoundsPolicyTests*)\|(ScopedVerificationInstructionTests*)\|(StableBuildOutputDocumentationTests*)\|(TaskPlatformGuidanceTests*)\|(VerificationRoundInstructionTests*)/*` | 294 | 0 |
| Mutant 1: colon-less alternative removed (`^[ \t]*round:[ \t]*`) | 851bae3a1 + mutant | `/*/*/(AgentTaskCardBindingTests*)\|(ReviewRoundsPolicyTests*)/(C1676*)\|(C1647*)` | 28 | **9** (6 real-shape cases, both colon-less warns, creation test) |
| Mutant 2: `round\s*:?\s*` | 851bae3a1 + mutant | same | 28 | **5** (3 C1676 next-line cases, C1647 next-line test, creation test) |
| Green after restore: 13 orchestration-loop.md doc-reader classes + ReviewRoundsPolicyTests + AgentTaskCardBindingTests (extra: the doc changed and the bin held mutant 2) | 851bae3a1 clean rebuild | 15 classes `/*` | 105 | 0 |

Mutants restored with `git checkout -- <literal path>`; tree clean. Whole Unit lane not run (the brief scoped the 13 classes; operator minimal-testing policy), so ordinary scope is Interim. No checkpoint plan exists for this card, so no checkpoint receipts.

- check-evidence-diff 7b920d84a..851bae3a1: commits=3 entries=1 violations=0.
- merge-tree onto origin/master 74178698e (newer than base): clean (tree efc2f69c8).
- rc/2026-11-01 (10a826b9d) lacks CARD-1647 (a0ab77618 not an ancestor; ReviewRoundGuard.cs and ReviewRoundsPolicyTests.cs absent). A cherry-pick of ac317e67f alone conflicts (modify/delete on both files, content in orchestration-loop.md and AgentTaskCardBindingTests.cs): CARD-1676 ports to rc only with CARD-1647.

## (1) Invariants: guard probes on the built Antiphon.Server.dll (pwsh 7.5 / .NET 9, DeclaredRound + Evaluate with 3 and 0 completed Reviews)

No input threw. `warn3` = warning with three Succeeded Reviews, `warn0` = with none.

| Input | declared | warn3 | warn0 |
|---|---|---|---|
| `Round ４ of 3` / `Round ٤ of 3` | null | yes (count) | no |
| `round 4 of 3`, `ROUND 4` | 4 | yes | yes |
| `Round: 99999` | null (5 digits, `\b` fails) | yes (count) | no |
| `Round: 9999` | 9999 | yes | yes |
| `around 5`, `Ground round 4`, `background round 4`, `preview round 4`, `Roundup 4` | null | count | no |
| `pre-review round 4` | 4 (`\breview` after `-`) | yes | yes |
| `REVIEW ROUND`, `REVIEW ROUND (full)` | null | count | no |
| `round\0 4` / `round 4\0` | null / 4 | | |
| empty | null | count | no |
| `the previous review was round 3 ... REVIEW ROUND 2 OF 3` | 2 | no | no |
| `x). Round 1 found D-1` (mid-line) | null | count | no |
| `Round 1 found D-1` at line start | 1 | no | no |
| `round: 0` | 0 (pre-existing: suppresses the count) | no | no |

'around' and 'background round 4' are excluded by the anchors (line start or `\breview[ \t]+`), not by a `\bround`.

Backtracking: no match timeout (GeneratedRegex with none). `round` + N spaces + a non-digit is quadratic: the two `[ \t]*` loops around the optional `:` are not made atomic. Measured for 3 calls: 20k 70 ms, 40k 272 ms, 80k 1,068 ms, 100k 2,553 ms. Every other 100k shape is linear (2-35 ms), including `round:` + 100k tabs. The goal is refused above 20,000 characters (AgentTaskService.cs:290) in CreateCoreAsync before the guard (line 1315), so the reachable worst case is about 25 ms per create. Not catastrophic, no throw: advisory A-1.

## Real brief heads (goals read from GET /api/agent-tasks/{id})

- 4468a62d line 1: `round: 2 of 3; stringency: less than round 1 (only the diff since the previous reviewed SHA; ...)`; line 4 also has `REVIEW ROUND 2 OF 3`, `Round 1 found D-1`, `round-1 reproducers`. Guard reads **2**.
- 36a5ec03 line 1: `round: 2 of 3; stringency: less than round 1 (...; blockers and diff-introduced regressions; ...)`; line 4 has `REVIEW ROUND 2 OF 3`, `Round 1 (on ad9bf0c...`. Guard reads **2**.
- bacbedc4 line 1: `round: 3 of 3; stringency: BLOCKERS ONLY (...; after this round a human decides: there is no round 4)`; line 4 has `REVIEW ROUND 3 OF 3`, `Round 2 found D-1`. Guard reads **3**, not the `round 4` later on line 1.
- This brief (689df4fd) `round: 1 of 3; stringency: full` reads 1. Code brief 4555c064 (CLEANUP and BUILD-SLOT lines first, no line-1 round) reads 2 from its quoted `'REVIEW ROUND 2 OF 3 (...)'`. That is a Code task, so the guard never runs on it.

On every real shape the declared first-line round wins. Mid-line mentions such as `Round 1 found D-1` do not match, because they are not at a line start and not after `REVIEW `.

## (2) False warnings

A declared round of 1-3 never warns, whatever the count (C1676 real-shape cases assert Evaluate null with three completed Reviews; probes agree). An undeclared brief falls back to the unchanged count rule (C1647 tests green; the C1676 prose controls assert the count warning).

## (3) Doc

The orchestration-loop.md review-rounds paragraph matches the regex: first match, any case, `of 3` optional, line start or after `REVIEW `, ASCII digits on the same line. Its plan-review caveat matches the code, which is unchanged. The policy and bundle-cap tests (ReviewRoundsPolicyTests, InstructionBundleTests) are green, and the stage-review bundle is untouched.

## Advisories

- A-1: The quadratic case above. It is bounded by the 20k goal cap. A linear form, if wanted: `round[ \t]*(?::[ \t]*)?([0-9]{1,4})\b`.
- A-2: The first match wins. A brief with no round declaration whose first match is a quote at a line start (`Round 1 found D-1: ...` or `Previous Review round 1 ...`) reads the quote. That suppresses the count warning, but the guard only warns. The line-1 `round: N of 3;` convention avoids it. `round: 0` declares 0 (pre-existing).
- A-3: No checkpoint plan or receipts for this card, so `reviewedSourceClean: false` here means receipt-less, not dirty: every run was on the clean tree at 851bae3a1 or on a stated mutant of it.

Cleanup: the 72 bin/obj directories my builds created (all born after the worktree was cut at 06:28 UTC) are deleted by literal path, along with the TRX directory in my scratchpad.
