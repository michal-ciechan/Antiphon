# CARD-1486 S5 token-order witness repair

The staged Reply integration witness now asserts literal task-specific
`[antiphon-finding:<id> found]` and `[antiphon-finding:<id> clean]` markers on the
persisted queued body and requires found before clean. Expected strings use the
task GUID directly, independently of FindingContract and FindingToken. All existing
assertions remain. Production code is unchanged.

Task/assigned S5-chain landing owner: `ddd3fc81-77ee-42f7-9094-774ff6a17e82`.
Original S5 Code owner: `75c32a51-a96a-4e1f-af75-359c9bf55d17`.
Branch: `feat/card-task-ddd3fc81`.
Worktree: `/work/worktrees/task-ddd3fc81`.
Full task base: `5425942116c201283ae5cc0fa47f1f727cd9838b`.
Plan: `docs/superpowers/plans/2026-10-22-card-1486-reply-followups-plan.md`.
Review: `.antiphon/task-1bada46b.md` at `4dd1cf239`.

## Verification scope and provenance

This report is committed with the repair before qualification. It does not predict
results. Final CHECKPOINT lines and actual tested SHA are in the completion report
and ignored `.antiphon/checkpoints/<run-id>/report.json` / row TRX files. The final
pushed tip is passed as --expected-source-sha and remains unchanged during the run.
Validate that receipt with scripts/validate-checkpoint-receipt.ps1, inspect both
fresh TRX rosters and run scripts/check-evidence-diff.ps1 over the full base..HEAD.

The specific repair brief requires CP-10 (2 wire methods) and the whole affected
BlockedReplyReportRetentionTests class. CP-12 below is that explicitly requested
additional class row; it shares CP-10's one build. Its invariant is persisted reply
wire, retained-report and nudge behavior across every method of the changed class.
There is no unbounded shared impact or full-assembly run. No separate V-n/R-n IDs
exist in this plan. CP-1..CP-9 are historical earlier-slice evidence, not rerun here.
CP-11 is explicitly not rerun: Review recorded 365/365 at the base SHA, and this
repair changes no production code. No Unit lane or Windows qualification is run.
These excluded selections are not claimed newly passed at this repair tip.

One gated checkpoint-tool bootstrap is an explained unlisted build: this fresh
checkout has no executable. It uses bin-ddd3fc81-tool/ and builds only the tool.
The proof run uses a 12-minute row/build cap and 15-minute total cap, serial rows,
one production/test build, and no repetition after green. Outputs are removed only
after their owning processes exit. No assertions or existing timeouts are relaxed.
GET /api/runner-defaults and /api/session-runners confirmed the available Linux
default. No desktop, live Windows qualification or release directory is touched.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-10 | S5-repair | `tests/Antiphon.Tests -> bin-ddd3fc81-proof/` | c1486-s5-wire | `/*/Antiphon.Tests.Application/BlockedReplyReportRetentionTests/(Blocked_reply_wire*)` | S5/D-5 literal wire and null-stage guard | 2 passed, 0 failed/skipped | 2 | 6 |
| CP-12 | S5-repair | CP-10 | c1486-s5-repair-class | `/*/Antiphon.Tests.Application/BlockedReplyReportRetentionTests/*` | complete changed integration class | all 19 executed pass, 0 failed/skipped | 19 | 2 |

## Pending mutation controls

The higher-priority Code-stage contract assigns deliberate mutants and red/restore/
green to SourceLanding Mutation. Accordingly this Code repair does not execute the
brief's requested three deliberate mutations and does not claim a red witness.
Review `1bada46b` historically observed reordered tokens green (the defect), dropped
stage red in the staged-wire method, and null-stage emission red in the stage-less
wire method. Those results are preserved, not relabeled as repair-tip evidence.

The plan has zero PC-n rows. All candidates/variants remain pending, including
token order, marker omission/substitution, caller/report ordering, dropped stage,
null-stage emission and non-Review stage discovery. Inventory:
`docs/superpowers/mutations/2026-10-23-card-1486-s5-candidates.md`.

## Landing checks and handoff

Before settlement fetch origin/master, run git merge-tree --write-tree against it,
and verify RC application without moving any shared ref. Preserve Review's order:
9404e8d92774d01f7686b07b95f6ac639d559270,
66a5fe702b43828500c02d95bd9f1f68ed907558,
43ae1dc44ec2fced171830106e16478e6bc3dc24,
c4aee39948ac6f41e67c1884d7d5f81c9f08d14f,
3bd2b76d1e793cd23d5ee940f1cbb1c11506710d,
0c14ce6657dcf1484403317da3c1235b7147098c,
5425942116c201283ae5cc0fa47f1f727cd9838b, then this single repair commit.
RC reference expected by the brief: 5a126f6fc5ae4e16d803b628716b8f0485a37174.
No landing, restart or deployment is performed. Restart: none for this test-only delta.
Next: light ordinary Review of the one-test delta and qualified ordinary evidence;
caller lands the assigned S5-chain owner and commissions SourceLanding Mutation.
