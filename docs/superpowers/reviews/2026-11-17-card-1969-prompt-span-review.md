# CARD-1969 review, round 1

Final review of CARD-1969 slice 2, the process-local prompt-span cache. Blockers only. Reviewer is Grok. Author is Claude Opus, Code task `52dac4e7-d1b8-4a65-a68a-e6186a1686f4`, tip `5ef455952f64f223ec38e680e437940297da5d39` on `feat/card-task-52dac4e7`. Dispatch base `e07e3efe8510fc18c7fa043eb85c6488f1fd1de6`. Card `fe7135a0-39ab-4de5-8041-c8033c4e9c5a`.

Reviewed range `e07e3efe8510fc18c7fa043eb85c6488f1fd1de6..5ef455952f64f223ec38e680e437940297da5d39` (6 commits). Current `origin/master` is `1f303bed04c3995e78b3fd9f1d5f568374435fec`.

## Verdict

Clean. No blocker. The stored stamp is derived from the stored rows. A hit is one statement. A miss proves the prefix and reads the tail inside one repeatable-read transaction, and every failed proof, empty cache, ambient transaction, eviction, idle expiry, and per-entry ceiling falls back to the full read. No production writer updates a stored prompt row's text, timestamp, or kind in place. S2 `38d4b3baad1991f997cbf8fa662081e7364b33c5` is unsafe alone and the commit messages require it to land with S3 `94cf0216695b8650dcc8eba4d3f24442dbdc0c5f` in one push. Positive controls are not in the plan. Mutation execution stays waived (operator 2026-11-12).

## Stamp, tail, and who can be served a stale list

`PromptCache.Entry.Derive` sets count, first id, max sequence, and top id from the rows and ids just read. The live stamp (`prompt-span.stamp`) is one SQL statement: a prompt count plus one `LIMIT 1` lateral per kind and direction, and it selects no text. Equality with the cached stamp returns those rows. There is no transaction on that path.

A miss opens `IsolationLevel.RepeatableRead` with `await using`, re-reads the proof inside that transaction (`prompt-span.proof`: same first id, prefix count at or below the cached max, id at that max), and appends `prompt-span.tail` only when the tail is non-empty, its length equals the count delta, and its last id is the proof's top id. Any other shape, including a max that did not move forward, reads `prompt-span.read` in the same snapshot. `LoadAsync` still applies the time window, housekeeping, and allowlist in memory, and `UserPrompt` stays the delivery verdict.

Two concurrent `LoadAsync` calls can store in either order. Each entry's stamp comes from its own rows, and the lock replaces the map entry whole. A slower store can put an older snapshot back. The next caller compares the live stamp before returning it. An insert rebased above the stored max changes the count, the max, and the top id, so that older entry misses and is not served to the watchdog (`TurnPrompts`), the deferred-report sweep (`RawPrompts`), or settlement (`Notifications`). A whole-session delete drops the count to zero and takes the full read. A reload through `PersistTranscriptCoreAsync` only inserts above the stored max or skips a uuid that is already stored. The accepted hole remains an in-place `Text` or `Timestamp` edit, or a `UserPrompt`/`QueuedUserPrompt` swap, of a row whose id, count, and endpoints do not move. The doc and the code both state that hole, and the re-grep below found no such writer.

W-7 (`C1969s2_prompt_committed_after_the_rows_is_never_hidden`) arms an interceptor on the first prompt-text reader and inserts one `UserPrompt` at the stored max plus one. At S2 the receipt fails because the second load does not contain that prompt. At S3 the derived stamp is the pre-insert rows, the second load misses, and the tail returns the prompt. CP-3 at the tip is 23/23, which includes this method.

The per-entry ceiling is 4,000,000 characters (text plus 48 per row). A session of 412,289 rows at 14,400 characters is over that ceiling: `Store` drops any previous entry and does not keep the new one, so the next call is today's full read. The map also stops at 256 entries and 32,000,000 characters. One lock guards the map, the LRU list, the last-used times, and the character total. `Lookup`, `Store`, `Forget`, and idle expiry do not await and do not touch the database under that lock.

Npgsql is configured without a retrying execution strategy (`Program.cs` `UseNpgsql` sets the version and the migrations assembly only), so the explicit transaction is legal.

## Re-grep

Under `server/`, production `TranscriptEntries.Add` is only in `AgentSessionRuntime.cs`: the batch insert, the per-row fallback, and `ApplyPersistStub`'s re-insert after a failed insert was cleared from the tracker. Stored sequence is the source sequence when it is already above the session max, otherwise max plus one. `ApplyPersistStub` rewrites the in-memory row before that new insert. There is no `ExecuteUpdate` of a transcript row, and no raw `UPDATE`, partial `DELETE`, or in-place `Text`/`Kind`/`Timestamp` assignment of a committed prompt row. The direct delete is `DataRetentionService.cs:372`, `WHERE AgentSessionId` only. `ProjectCascade` deletes the session row; the transcript rows leave by the session foreign key, which also drops the stamp's count.

## Witnesses

Red rows were not re-run. The plan runs each red row once at its slice.

| Run | SHA | Result |
|---|---|---|
| CP-1 `20261117-115312-172a` | `bd36785bd50f2736b721c5103c8fb829e26f9255` | 13 executed, 5 passed, 8 failed. W-9 was 41 instead of 40 because `ReadCount` includes the terminating `Read()`. Wrong reason. |
| CP-1 `20261117-120031-1bcf` | `b729136ad98d7e28c174ea25b8da2ac4a0c2ea9a` | 13 executed, 9 passed, 4 failed. W-1 three cases (400, 400, 1600) and W-3 (401 not 1). Source clean, `buildSource=verified`. |
| CP-2 `20261117-120657-4920` | `38d4b3baad1991f997cbf8fa662081e7364b33c5` | 13 executed, 5 passed, 8 failed. W-3 (401 not 1), W-4 (deleted sequence still present), W-5 (old top text), W-7 (inserted prompt absent), W-9 four cases (0 not 40). |

`b729136ad98d7e28c174ea25b8da2ac4a0c2ea9a` subtracts that terminating read. The corrected CP-1 matches the plan. The assertions are outcome checks, not self-comparisons.

Fresh tip run `20261117-133903-9ddf` at `5ef455952f64f223ec38e680e437940297da5d39`, Debian 12, source `state=clean` `buildSource=verified` `dirty=0`. `validate` printed `CHECKPOINT SOURCE VALID` for these four rows. Wall 8m25s. One build (CP-3 `build=ok`, 244.272s); CP-4, CP-5, and CP-6 are `build=reused`. The report's `builds: 3` summary is the tool's count, not three compilations. `unlisted: none`.

| Row | Result |
|---|---|
| CP-3 `/*/*/TranscriptPromptSpanTests/*` | 23/23/0/0, tests 15.025s |
| CP-4 selector and housekeeping | 13/13/0/0 |
| CP-5 receipt methods | 10/10/0/0 |
| CP-6 task-input methods | 48/48/0/0 |

`git diff --stat` for the CP-5 and CP-6 test files across the range is empty. No assertion in those classes changed, and no `Forget` call was added.

## Land rule and scope

S2's message says the commit is deliberately incomplete, unsafe alone, and that S2 and S3 land together in one push and one Merge, with the branch tip never left at S2. S3 and the index commit repeat that, and they name `38d4b3baad` and `94cf021669`. Plan D-9 is the same rule. Merge cherry-picks in order and does not stop between those two commits.

The range does not touch `server/Migrations`, nightly pins, or a setting. Bounds in the invariants paragraph are "chosen, not measured" (256 entries, 4,000,000 characters per entry, 32,000,000 total, 120 s idle, not a setting). The code comments mark the same figures UNOBSERVED. No input-wait change. Parking stays disabled and this slice adds no release deadline.

The index commit is last. The driver was built first (lease `9904f9bc-7dfa-428d-9de0-97636a42cc3f`, load 10.10) and printed `derived-impact-index unchanged` (`windowsOnly count=31`, `linuxOnly count=1`). A second `-NoBuild` run (lease `162a8c8f-f6f7-4a09-b778-08ce521160bd`, load 7.16) printed the same line. The worktree stayed clean.

## Other checks

Guard batch at the tip, load 12.67, lease `0a7e6d11-3742-40f2-b901-1828cec00701`, `--timeout 570s`, native output, `UseAppHost=false`: total 115, failed 0, succeeded 114, skipped 1. The skip is `C1210_Windows_linux_only_outcomes_are_exact` (`CARD-1210: the child skip-roster audit requires Windows placement`). The filter covered the classification policy and guard, slow-test registry and tripwire, native Linux policy witness, derived-impact index, mechanical contract, `TaskPlatformGuidanceTests`, `C1729LayeringGuardTests`, `TestLaneCategoryGuardTests`, and `WindowsUnitPlatformAdmissionTests`.

`scripts/check-evidence-diff.ps1` for the range: 6 commits, 0 entries, 0 violations. `git diff --check` exit 0. `scripts/check-os-witnesses.ps1` exit 0 (syntax and inventory). It printed `CLASSIFY unrecognized tests/derived-impact-index.json` and still completed. `scripts/impacted-classes.ps1 -MergeSubset` has `migrations=false`, so the downgrade selector was not run (count 0). `git fetch origin master` then `git merge-tree --write-tree origin/master HEAD` exit 0, tree `6097abcbd42bc338183892df13f3d2942af40679`. The only slice path master also changed since the dispatch base is `tests/derived-impact-index.json`, and that merge is clean.

Checkpoint wall 8m25s and the guard batch 3m36s are inside the plan's row budgets. The closed run did not pass `--row-timeout` or `--total-timeout`.
