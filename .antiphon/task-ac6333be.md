# CARD-0506 S2 round 4 — Code ac6333be (repair of Review 24d4e81c F1)

Comments, Markdown and stored report text only. Claims were shrunk to pointers at named tests; the inherited limitations the reviewer reproduced are stated as not covered / known limitation. No logic, assertion, timeout or test-name change. No tests or builds were run: the code is identical to the tip where Review 24d4e81c ran CP-2 green (68/68).

Branch feat/card-task-ac6333be, runner mirror /work/worktrees/task-ac6333be. It continues fast-forward from 404a0d164 (Code 87938db0). The original landing owner is Code task 2459c6e7-a8fa-409d-a7af-c3743d754d67.

## Commits
- 4f33804df9074d752db9bc8b24f30886aa72e4f7: the comment, test-design and stored-report corrections.
- This report (no source change).

## Review inventory, item by item
| Review location | Done |
|---|---|
| Editor 93–95 `isOlder`; report 18 | Removed the "only between PUT reply and in-flight read / same history" prose. `isOlder` now has a one-line description. `settleSave` states the actual comparison: `isOlder(reply, state.base)`, whichever read or earlier Save reply `base` came from. It names the one-history assumption, says `late-put-newer-get` covers an increasing counter, and names the two restore diagnostics as a known limitation. Report "Save reply" bullet rewritten to match. |
| Editor 286–287 "arrival order" | Now reads "fixed order (not arrival order)": Save reply, conflict reload, then a query snapshot. |
| Editor 291–293 "stays on screen until…" | Deleted. Now: `superseded` (the cache state read after `mutateAsync` resolves) is not adopted (`S2F2 refresh-fails`/`refresh-pending`). It also states that S1's onSuccess calls invalidateQueries before `mutateAsync` resolves but does not await the GET. |
| Report 3, 17, 72, 76; test 1187–1188; b5f1d7ae 19 | "current whatever their revision" / "any later successful read is current" / "only the superseded read is ignored" removed. The test header prose was replaced by a pointer to the editor's limitation notes. The b5f1d7ae correction now points at 87938db0's rules, tests and limitations. The timestamp collision is stated with the diagnostic name in the editor, the 87938db0 Residuals and the test design. |
| Report 79 unmount | Replaced with what `unmount-pending` asserts. The residual "effects after unmount" now says a late continuation still invalidates the shared cache and shows the global notification. |
| Report 65 "names the test that proves it" | Removed. |
| Report 66 "Two comments" | Corrected to three pre-existing blocks: the section header, `S2F2 refresh-fails` and `S2F2 newer-after-save`. Verified with `git diff 29e70fefa..404a0d164`. |

## Other text removed or shrunk
- **Editor.**
  - `rebaseDraft` and `adoptServer` docs: dropped "every", "never re-sends" and "the same document again only refreshes base (occupancy)", which had no test. Each doc is now one sentence plus its test pointers.
    - *Correction (Review 7826b06f F1, Code task f67f2811):* the `adoptServer` doc still said edits always rebase and show the banner; an unchanged document does neither. Corrected in the editor and in task-87938db0.md.
  - `base` comment: dropped "treats as current".
  - Pending-gate comment: dropped "no edit can be made"; the gate list and its S2F3 tests remain.
  - Conflict reload comment: dropped "current"; it now points at `V12 conflict-*`.
  - Scope-key comment: dropped "only".
- **Test comments.**
  - `S2F2 refresh-fails`: dropped "query keeps the r4 entry".
  - `refresh-pending`: dropped "r4 data stays in the cache".
  - `after-save-lower`: dropped "that read is current".
  - `after-failed-refresh-equal-data`: "new update time" replaced by the asserted `dataUpdateCount + 1`.
- **87938db0 report.** "Rule now in force" and the sweep were rewritten as test-pointed statements. Removed: "cannot roll back", "every entry", "only that entry can predate", "always", "only ever holds", "all synced state". The project-switch claim now says no committed test switches scope on one query client. The old same-millisecond residual claimed "equal in content", which was wrong; it is replaced by the diagnostic-named limitation.
- **Test design.** One Out-of-scope bullet lists the three diagnostics and the effects after unmount as not covered / known limitations. The census is unchanged.

## Checked and left
- **Editor comments.** The comments at 61, 108–111, 125–128, 179, 185, 265, 416, 546, 548–549 and 715 are code-literal or test-pointed.
- **Test comments.** The test-file comments at 220, 396, 562 and 976 are fixture descriptions or claims asserted by the test that contains them.
- **Other reports.** task-2459c6e7.md contains no claims from this wording family. The historical b5f1d7ae F2/sweep bullets are retracted by their correction lines.

## Verification
- **Comment-only diff.** In `git diff 404a0d164..HEAD -- '*.ts' '*.tsx'`, both files are byte-identical to 404a0d164 once whole-line comments are stripped (`cmp` equal). Every changed line in the -U0 diff is a comment line.
- **Whitespace.** `git diff --check` is clean.
- **Evidence diff.** `scripts/check-evidence-diff.ps1 -BaseRef b5653d583 -HeadRef HEAD` at 4f33804df: commits=11, entries=6, violations=0.
- **Merge check.** `git merge-tree --write-tree origin/master HEAD` exits 0, tree 5879bb6fe9c398beb9ff35449b610c83d77bb937 (origin/master 50cbb680379beb97a1ff07fed64ee4a0bcd18b60).
- **Not run.** No CP-2 rerun, builds or unlisted test drivers, as the brief directs.

## Pending
- **PCs.** Every PC is pending for SourceLanding Mutation. Mutation was not run.
- **Later slices.** S3–S5, CP-3/CP-4, V-15–V-18 and the manual/E2E work belong to later stages.
