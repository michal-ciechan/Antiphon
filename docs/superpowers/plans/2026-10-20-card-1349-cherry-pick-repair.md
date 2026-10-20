# CARD-1349 cherry-pick landing repair

Code task: af056548-20c6-41cd-b704-062a5f12c1c2.
Original Code landing owner: f4fd378a-75cf-4e60-b57a-db7bdf0ee62e.
Assigned branch: feat/card-task-af056548.
Task base: 9e78a08880cb300dab6fc3bedbefc1a47eed3284.
Reviewed source: ff3ae85d1ab467f0ca557a3a925d9ac7ee86f4ba from
feat/card-task-c92632b2. Target: master only.

The repair brief explicitly requests one narrow checkpoint plus
Registry_matches_compiled_metadata, excludes whole Unit, and sets a 45-minute
task budget and a 40-minute row timeout. This is the closed verification list
for this mechanical integration repair. It does not replace historical
verification or claim another full integration-class sweep.

The merge base is d5dec9555bd726ad11bb30073c1733d6b3e7bb0e. All 31 commits
in that base..reviewed range were positive in git cherry against origin/master;
none were skipped. They were cherry-picked in order and pushed individually.
The sole conflict was tests/Antiphon.Tests/slow-tests-allowlist.txt at
8e8d8bd35ca241ead07bca3249f5525137aee596. Resolution preserves the entire
landed file as an exact prefix and appends all three CARD-1349 rows, each with
its original reason comment immediately above it. The landed CARD-1233
entries and reason comments are unchanged. There were no other conflicts.

At the completed cherry-pick tip eb8d08544, all 35 CARD-1349-touched server/test
files other than the allowlist have the same Git blob IDs as the reviewed
source. The 127 other server/test files differing from the reviewed source
all equal their task-base blobs: these are previously landed changes.

Review evidence 97e9cdf1-82a7-48f9-8cfe-1d92a05f42fc belongs to Review task
7f9ed421-c98a-41b9-ac4e-9bc6ac711270. Its report validates the prior CP-34
receipt (428/428) rather than running new tests. Its named repair obligations
are D1 (vanished-default warning), D2 (overflow retaining the configured
project default), and F2 (brief enqueue fault bound to its owning transaction).
CP-35 selects exactly their three source methods, including all four F2
arguments. CP-36 checks the merged allowlist against compiled metadata.

## Verification design

The invariant is preserved configured-default warning behavior and complete
brief delivery after the mechanical cherry-pick, plus allowlist/metadata
agreement. There is no unbounded class set or full-assembly selection.
No assertion, timeout, production source or test body is changed by this repair.
The 40-minute checkpoint deadline is the explicit brief's execution allowance,
not a change to any test or product deadline.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-35 | S1 | `tests/Antiphon.Tests -> bin-c1349-cherry/` | reviewed-repair-methods | `/*/*/*/(C508_VanishedDefaultStillCapturesWarning*)\|(C1349_InventoryOverflowKeepsProjectDefaultObservation*)\|(C672_prepared_runner_brief_reaches_its_session_while_a_land_holds_the_lease*)` | Review D1, D2, F2; limited V-14/R-14 and V-18/R-18 witnesses | all 3 methods, 6 executions, 0 failed/skipped | 6 | 12 |
| CP-36 | S1 | CP-35 | allowlist-metadata | `/*/*/TestClassificationGuardTests/Registry_matches_compiled_metadata*` | merged allowlist | 1 execution, 0 failed/skipped | 1 | 2 |

Run both rows in one checkpoint-tool run, serially, with committed HEAD passed
as --expected-source-sha and --row-timeout 40m. Inspect fresh TRX for every
method and argument and validate the receipt. On a receipt refusal, remove only
this task's proven owned output residue and retry once, as authorized by the brief.
The infrastructure-only checkpoint-tool bootstrap uses build-slot.ps1 and
bin-c1349-cherry-tool/; its estimated cost is one minute outside the two rows.
No other builds or tests are planned.

Manual checks: exact landed allowlist-prefix preservation; reviewed blob
identity and landed-delta attribution; full task-base..HEAD evidence-history
guard; fetched-origin/master merge-tree conflict check; final remote tip.

V-1/R-1 through V-21/R-21 retain their historical outcomes in the original
implementation record. This repair does not rerun or relabel those complete
selections. No required repair selection is deferred. PC-1 through PC-118
and every variant remain pending for method-scoped SourceLanding Mutation,
including missing-control discovery. The candidate remains
docs/superpowers/mutations/2026-10-19-card-1349-dispatch-sibling-walk-pending.md.

Live runner-defaults and session-runners were read; no host or OS pin is used.
No restart or deployment is part of this repair. Eventual activation target:
server. Next stage: Review.

Final receipts, full commit mapping, actual tested SHA, unedited CHECKPOINT
lines and costs are stored in .antiphon/task-af056548.md after execution.
