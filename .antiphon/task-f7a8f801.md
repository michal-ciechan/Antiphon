# CARD-1486 S4 test repair

Three retained-report boundary tests repair the inadequate coverage found by Review
`60ef9a8b-ccf0-461c-9dbb-8381e4c69acd`. Product source is unchanged from the assigned base.
This report is committed before final qualification. Final CP-8/CP-11 outcomes, unedited
CHECKPOINT lines, actual tested SHA and validated receipts are supplied by the final stored
task report and its generated checkpoint report; this pre-run record does not claim green.

Original Code task and landing owner for this complete S4 chain:
`f7a8f801-d46c-4bbe-a46e-acda4a9e6ae4`, branch `feat/card-task-f7a8f801`,
worktree `/work/worktrees/task-f7a8f801`. Assigned base:
`eb538317f1e769870cf3cb2d9f839f6607132412`. The original S4 implementation from
`b9237e61-bcdc-49cb-9070-fdfb3ae2c97e` rides in this chain; its full S4 base is
`0737fddc57eca08b946b57dc76447dc6f946b25e`. Plan:
`docs/superpowers/plans/2026-10-22-card-1486-reply-followups-plan.md`, S4/D-2.

The requested Review file is absent from the Review branch. Its complete stored `result`
was read through GET `/api/agent-tasks/60ef9a8b`, confirming the fixture analysis in the brief.
GET `/api/runner-defaults` and `/api/session-runners` confirmed server2 as the available Linux
default (defaults revision 2). No runner/platform pin or fleet setting was changed.

The test commit is `0be73e280b707a78fb2a98a921dd07fea06ac062`. All three new methods in
`tests/Antiphon.Tests/Application/BlockedReplyReportRetentionTests.cs` use the real
`BlockedBySyncAsync` fixture, Reply wire, transcript settlement and persisted task state:

| Method suffix after `Closing_lines_only_rejects_` | Body | Required outcome |
|---|---|---|
| `own_marker_between_prose_lines` | prose, own finding marker, later prose | Succeeded with the whole replacement body |
| `first_line_marker_followed_by_prose` | own progress marker using the fixture's actual pushed SHA, then prose | Succeeded with the whole replacement body |
| `foreign_marker_without_prose` | another task's finding marker alone | Succeeded with that replacement body |

Each body is followed by THIS task's `done` token. No handoff is appended, preserving the
foreign-marker-only shape. The shared assertion checks exact Result, Marked evidence, removal
of the old sync handoff, no complete-report nudge, exactly one report-settlement event, and
unchanged concurrency token/event count on a second observation. The final assertion refinement
counts the actual report settlement: the service also emits Completed for merge-back bookkeeping
(AgentTaskReplyService's settlement-event comment and MergeBackAsync document this). It retains
the exact-one assertion and the full event-count idempotence guard. No timeout was widened.

The brief explicitly required three method-scoped defect witnesses. Each method was executed
once against its assigned mutant at the test commit above, using `scripts/build-slot.ps1`
and a hard 300-second timeout for each build/test invocation. Every fresh TRX has exactly
1 executed, 0 passed, 1 failed, 0 skipped, exit 2. Every failure is the guarded status assertion:
expected Succeeded, observed Working with the retained report and sync handoff.

| Mutant | Exact selected method suffix | Evidence directory |
|---|---|---|
| any own marker line suffices | `own_marker_between_prose_lines` | `.antiphon/witness-f7a8f801/any-marker/` |
| only the first nonblank line is checked | `first_line_marker_followed_by_prose` | `.antiphon/witness-f7a8f801/first-line/` |
| skip every unrecognized nonblank line | `foreign_marker_without_prose` | `.antiphon/witness-f7a8f801/drop-every-line/` |

Exact filter shape: `/*/*/BlockedReplyReportRetentionTests/Closing_lines_only_rejects_<suffix>`.
Each source mutation was restored byte-for-byte in `finally`, with refreshed mtime and a clean
`git diff --exit-code` for the formatter. Mutation diffs, logs, TRX and assembly hashes remain
ignored evidence. The later event-selection refinement does not alter the status assertion that
failed in every witness; the mutants never reached the event assertions. CP-8/CP-11 provide
the restored-code green verification for the final test source.

Unlisted runs were limited to the explicitly requested witness builds/methods and the necessary
checkpoint-tool bootstrap. Bootstrap: granted slot, waited=195s, held=10s, 0 errors. Any-marker:
build waited=0s/held=160s; test waited=0s/held=94s. First-line: build waited=0s/held=177s;
test waited=0s/held=91s. Two unsuccessful build attempts executed no tests and are not red
witnesses: an unquoted PowerShell `-nodeReuse:false` argument split before compilation
(waited=61s); the first drop-every-line build reached its unchanged 300-second deadline after
waited=196s. Its helper was restored. The replacement build compiled only `server` (waited=61s,
held=165s, 0 errors), copied the successful mutant DLL into the unchanged test assembly's output,
and verified matching SHA-256 hashes before the single-method run. The successful earlier test
build and `.antiphon/witness-f7a8f801/drop-every-line/assembly-provenance.json` identify that
diagnostic assembly reuse. Final qualification uses fresh checkpoint outputs, not those binaries.
The failed attempt logs are retained as `build-argument-failure.log` and `build-timeout.log`.

Final closed selection: CP-8 and CP-11 from the plan, unchanged filters, expected counts 7 and
364. CP-8's old four cases plus these three cover S4; CP-11 contains the full
AgentTaskReplyIntegrationTests (341), BlockedReplyReportRetentionTests (18), and
ClosingReportNudgeLifecycleTests (5), including S1-S3 repairs. The plan's stale nominal counts
are not used to claim fewer tests. Cost estimates remain 6 + 15 minutes including builds;
there is no full-assembly run or unbounded class. No V-n/R-n IDs or separate UI acceptance
are defined for S4. Unit, whole-suite and Windows runs are excluded by the explicit minimal
brief and plan, not reported passed. CP-1..CP-7 are historical slice rows; S5/CP-9/CP-10 are
excluded. No required S4 ordinary row is deferred. Final source receipts must validate using
`scripts/validate-checkpoint-receipt.ps1` at committed HEAD, with nonzero fresh TRX rosters.

Manual application check at the test commit: `git merge-tree --write-tree origin/master HEAD`
was clean against master `3f884978c0691dd35b2c829d75be58cc156aa2ed`. In a disposable detached
worktree, these commits applied cleanly to RC `0714a9a89e4bff65aae7e0b4b94098694aeb931c`
using `git cherry-pick --no-commit`, and `git diff --cached --check` passed:

- S1: `27e994af1f6e8de1493408e7a0ec25404199ac0d`, `8b7edcdde449f7e85e39e27b0f166ebaf92a5b3e`.
- S2: `645c7b30b884e3b17352ca4a2dc5c363d09c6c27`, `bcd04d29e8aec21988b281c6b0dc90d21d10a9d3`, `9516bfd35a3a3e2a075ba71fa4e3c586d7104220`.
- S3: `12a2141e6047bc1b40b4f4d74815293f1ef9a7ec`, `0fc7a4c2b08e6df7354cff2f374c9d603ab5ac03`, `2316cd6faf3852ad54eb32b77d3a7eae75d910f3`.
- S4: `31e12f9cdd56ef47b1c597fe9a89209554f330cc`, `91585acd67474e1c92a7bd3606bc004700c16541`.
- Repair: `0be73e280b707a78fb2a98a921dd07fea06ac062`.

The scratch tree was removed. Final-tip application/merge checks and full task-range
`scripts/check-evidence-diff.ps1` are repeated before reporting. No RC ref is advanced and
no RC build/runtime qualification is claimed by this repair. Restart: none.

The plan has zero PC-n rows. SourceLanding Mutation and missing-control discovery remain
pending after ordinary Review and the caller's landing of this Code task. The three deliberate
witnesses above do not discharge that gate. Pending unbound candidates include any-marker,
first-line-only, omitted every-line rejection, foreign finding/progress IDs, parser bypass,
and retention/waiting-sweep bypass. Mutation owns binding their variants and further controls.
Next: Review after final ordinary CP-8/CP-11 verification; this task owns the whole S4 landing.
