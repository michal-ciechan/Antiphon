# CARD-1372 Final Review — task f3ef6242

Final Review of Code task 5666c650-a89e-45ab-942b-dfd8b3d6c225 is clean at bc5bd25990859b4f353b445042711ab441ca6bbd. The SessionUnowned hint and DTO summary match CARD-0188, the ownership predicate is unchanged, and the client test, CP-3, and the full AttentionServiceTests class are green on that SHA.

## Wording

Production delta in fd00cd06..bc5bd259 is the client hint, the DTO XML summary, one Vitest equality, the plan's S3 checkpoint amendment, and the Code report. bc5bd259 changes only `.antiphon/task-5666c650.md`.

- Hint: `A live session has no agent pointer, card or open task. Inspect its evidence.`
- DTO summary: `A live session older than ten minutes has no agent pointer, card or open task. Warning.`
- Emitted headline at `AttentionService.Leaks.cs:151` remains `Live session has no agent pointer, card or open task.`
- Enum value 46, severity, the ten-minute cutoff, the current-pointer predicate, and remote confirmation are unchanged. The DTO edit is the XML summary.
- `docs/ops-http.md:557` and `docs/session-runtime-invariants.md:944` already describe a current agent pointer.
- The old sentence remains only in the plan's ground-truth table. Historical investigation documents and the CARD-0188 plan were left as history.

## Receipts

Both validator commands exited 0 for source bc5bd25990859b4f353b445042711ab441ca6bbd.

- CP-2 run 20261020-123243-fd9c: 1 passed, 30 unselected, exit 0, source clean, buildSource=notApplicable.
- CP-3/CP-5 run 20261020-122044-9cbc: CP-3 1 executed/passed and CP-5 167 executed/passed, 0 failed, 0 skipped, buildSource=verified, slot=granted. Output `bin-c1372-green` was deleted.

The first combined run 20261020-120854-ec56 passed CP-2, then the managed build (0 errors, 1107 warnings) was refused for two esbuild hard links. Owned `tests/Antiphon.Tests/bin-c1372-green` was removed and the shared link was split. SHA-256 stayed c638273fcf95573ca74af586677800b4dd874c55f8f945adb54316d6902ba14b. One retry passed. The mixed report is `cleanup_refused` and is not the binding receipt. The standalone CP-2 run is.

Whole Unit was not run. The author's CP-3/CP-5 receipt at 3a3322bb0328f7b7b4da72f744abde01a46eda04 validated, rows=2. `check-evidence-diff` over efc052e64d2f15c16af1640b7e2b8fc67f03bd92..bc5bd25990859b4f353b445042711ab441ca6bbd: 6 commits, 3 entries, 0 violations.

## Land

`git merge-tree --write-tree origin/master bc5bd259` against origin/master 0c5c3ec3d03c5d10ba927f856733c5c79b1775c3 exited 0, tree 2461eab9c2acb0d34cf0391d242538158cca32ff. The three product files match the tip.

The nine original CARD-0188 commits do not rebase onto that master: add/add on `docs/investigations/2026-10-19-card-0188-leaked-incarnation.md` at 610a3749b. Their content is already on master. merge-tree of efc052e6 equals origin/master tree ec762f59bef4bc14801063dceb966c477b3e2f78, and `git cherry` marks all nine equivalent. A detached rebase of only the six CARD-1372 commits onto origin/master exited 0 at 827cfa992a01dc1b04fbdddfb39fb51dcf075078. The assigned branch was not moved.

No behaviour defect and no new fail-open. Zero declared PCs remain for SourceLanding discovery. The withdrawn Unit failure stays with CARD-1381.

## Review evidence

subjectTaskId: 5666c650-a89e-45ab-942b-dfd8b3d6c225
reviewedSourceSha: bc5bd25990859b4f353b445042711ab441ca6bbd
reviewedSourceClean: true
ordinaryScopeCompleted: Full
next: land
