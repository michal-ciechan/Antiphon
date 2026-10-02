# CARD-0974 Code report

## Design note (written before edits)

Current plan: all Delegation queue entities (including unrelated bodies and spill payloads), valid refinement/reply event metadata, then every lifetime UserPrompt/AssistantText entity for all bound open/blocked sessions. Ownership and matching happen only afterward.

New plan: project valid event metadata; select only canonical owned task-input keys or null-key legacy-pointer candidates on bound sessions, projecting Id/session/Sequence/Body/key/spill path. Validate ownership and legacy location with the existing code. Return before transcript reads if there are no valid inputs. For each relevant session, select candidate UserPrompt rows using a permissive SQL LIKE subsequence of the normalized input body; the exact existing completeness matcher determines the first delivered prompt. Stream these candidates in sequence order, stopping once every input has its first match. Project Sequence/Kind/Text and coalesced Timestamp/CreatedAt. Read only prompt and assistant rows at or after the earliest matched prompt, with the same projection. Retrieve ordinary Delegation queue bodies only for relevant sessions to preserve later-input supersession.

The bound is transcript-confirmed first delivery, not LastDeliveryBaselineSequence or wall-clock timestamps: the durable queue stores only the latest attempt, and either alternative can discard the original complaint on retry or imported transcripts. LIKE is a necessary subsequence condition, never proof of completeness. Full text is retained for the exact matcher: truncating arbitrarily changes completeness and complaints late in long text. Returned text is bounded by candidate/session/sequence filters and streaming the initial search; no new schema, lifetime entity loads, status or age cutoff.

Preserve exact task/session/input identity, warning severity, all strings/evidence (240-character display excerpt), timestamp fallback, input ordering, sole-input ambiguity, legacy parsing, later complete Delegation-input supersession, stable condition key and GetAsync ordering/dedupe. No changes to kinds/wire contracts.

Tests: 2,000-row no-input cost test (zero returned transcript rows/queries), input cost test excluding unrelated pre-delivery history, copied old algorithm oracle with no input/read OK/unreadable/legacy/multiple inputs/large history/retry/long text/supersession/foreign input fixtures; existing Attention, eight read-failure tests, wire tests and parked sweep classes. Two scratch mutations remove sequence restriction and candidate restriction; both must fail costs. One final Unit lane.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1 tests | tests/Antiphon.Tests -> bin-c974-red/ | red-cost | /*/*/TaskInputAttentionCostTests*/* | red cost proof | 2 failures expected at base | 2 | 5 |
| CP-2 | S2 implementation | tests/Antiphon.Tests -> bin-c974-red/ | cost-oracle | /*/*/TaskInputAttentionCostTests*/* | behavior + cost | all listed, 0 failed | 19 | 8 |
| CP-3 | scratch mutation 1 | tests/Antiphon.Tests -> bin-c974-red/ | mutation-floor | /*/*/TaskInputAttentionCostTests*/Input_reads_exclude_unrelated_history* | sequence-floor mutation | input cost fails | 1 | 4 |
| CP-4 | scratch mutation 2 | tests/Antiphon.Tests -> bin-c974-red/ | mutation-candidates | /*/*/TaskInputAttentionCostTests*/Input_reads_exclude_unrelated_history* | candidate-filter mutation | input cost fails | 1 | 4 |
| CP-5 | all restored | tests/Antiphon.Tests -> bin-c974-red/ | attention | /*/*/(AttentionServiceTests*)\|(TaskInputReadFailureTests*)\|(AttentionKindWireTests*)\|(ParkedMessageSweepServiceTests*)\|(TaskInputAttentionCostTests*)/* | all affected classes on restored final commit | all listed, 0 failed | 100 | 8 |
| CP-6 | all restored | CP-5 | unit | /*/*/*/*[Category=Unit] | whole Unit lane required by brief | >=1 executed, 0 failed | 1 | 5 |

Scratch mutations are committed and pushed before their diagnostic runs to honor the brief. CP-5 rebuilds and runs all affected classes at the final restoration SHA, because earlier build-source stamps bind to the pre-mutation commit. CP-6 reuses that final build.

## Evidence

CP-1 first attempt (not accepted as cost-red proof): both tests failed because the probe read FieldCount during disposal, after Npgsql closed the reader. Probe fixed to capture columns in ReaderExecutedAsync; ReadCount stays in DataReaderDisposing. Production remained unchanged. CP-1 rerun required for genuine cost assertions.
CHECKPOINT CP-1 commit=f577e590b7312e1ec1f88d6e927dbbe01ea9d662 build=ok filter=/*/*/TaskInputAttentionCostTests*/* executed=2 passed=0 failed=2 skipped=0 trx=/work/worktrees/task-2e6d822d/.antiphon/c974-checkpoints/CP-1-20261002-170937-b4c9/run.trx slot=granted waited=75s dirty=0 source=f577e590b7312e1ec1f88d6e927dbbe01ea9d662 sourceState=clean buildSource=verified

All rows rebuild into the same producer-owned isolated bin-c974-red/ output; the name dates from the red baseline. This avoids rebuilding unchanged graph projects into multiple output trees. No mutation run uses -NoBuild. Restores use fresh file timestamps; final CP-5 rebinds the build-source stamp, and only CP-6 reuses it.

CP-1 rerun establishes cost red on unchanged production code: no-input ReadCount=2001 (bound 0); input ReadCount=2003 (bound <=8). Both fail Shouldly cost assertions, not infrastructure/probe errors. reruns=1.
CHECKPOINT CP-1 commit=2bfcdbc5227937f686ebe34c790ede69c1b011ac build=ok filter=/*/*/TaskInputAttentionCostTests*/* executed=2 passed=0 failed=2 skipped=0 trx=/work/worktrees/task-2e6d822d/.antiphon/c974-checkpoints/CP-1-20261002-172129-7a21/run.trx slot=granted waited=105s dirty=0 source=2bfcdbc5227937f686ebe34c790ede69c1b011ac sourceState=clean buildSource=verified

New integration class marked Slow and registered in tests/Antiphon.Tests/slow-tests-allowlist.txt as required by the testing owner: baseline isolated-store/2,000-row cases measured >=5 seconds (input 8.153 s; no-input includes initial store setup). This is the only additional test metadata file.

CP-2 green: 19/19 (2 costs + 17 separately reported fixture cases). All serialized AttentionItemDto lists match the frozen old algorithm byte-for-byte. SQL candidate translation succeeds for ANSI, whitespace-free text, Unicode/wildcard escapes, legacy pointer, retries, multi-input/shared-session and long-text cases. No-input builder reads zero transcript rows and executes zero transcript queries; input builder stays <=8 Read operations and <=4 projected columns despite 2,000 old rows.
CHECKPOINT CP-2 commit=f6f3b43442ac8a405dfd4387ff9922c0311b1d35 build=ok filter=/*/*/TaskInputAttentionCostTests*/* executed=19 passed=19 failed=0 skipped=0 trx=/work/worktrees/task-2e6d822d/.antiphon/c974-checkpoints/CP-2-20261002-172947-c9e9/run.trx slot=granted waited=106s dirty=0 source=f6f3b43442ac8a405dfd4387ff9922c0311b1d35 sourceState=clean buildSource=verified

CP-3 scratch mutation caught: removing t.Sequence >= floor still produces the unreadable item, but ReadOperations rises to 2004 (>8). This is the intended cost assertion failure. The second mutation is generated independently from the green implementation and restores this floor.
CHECKPOINT CP-3 commit=9e2b1850f33cbb1ec233487c50c64528d5df2b62 build=ok filter=/*/*/TaskInputAttentionCostTests*/Input_reads_exclude_unrelated_history* executed=1 passed=0 failed=1 skipped=0 trx=/work/worktrees/task-2e6d822d/.antiphon/c974-checkpoints/CP-3-20261002-173709-5aab/run.trx slot=granted waited=45s dirty=0 source=9e2b1850f33cbb1ec233487c50c64528d5df2b62 sourceState=clean buildSource=verified

CP-4 scratch mutation caught: removing the permissive UserPrompt candidate filter still produces the unreadable item, but ReadOperations rises to 1004 (>8). Both mutations were made as forward commits and pushed before running; no branch rewrite, rebase or merge.
CHECKPOINT CP-4 commit=fa297066ca920b97cc8bf810b43e34814032a4ad build=ok filter=/*/*/TaskInputAttentionCostTests*/Input_reads_exclude_unrelated_history* executed=1 passed=0 failed=1 skipped=0 trx=/work/worktrees/task-2e6d822d/.antiphon/c974-checkpoints/CP-4-20261002-174145-8245/run.trx slot=granted waited=105s dirty=0 source=fa297066ca920b97cc8bf810b43e34814032a4ad sourceState=clean buildSource=verified

## Final implementation and review handoff

- server/Application/Services/AttentionService.TaskInputs.cs: owned/legacy input candidate projection, earliest complete receipt discovery, four-column transcript projections and sequence-bound history, relevant-session Delegation-body projection. Existing result construction and matching rules retained.
- tests/Antiphon.Tests/Application/TaskInputAttentionCostTests.cs: measured reader-operation/column bounds; frozen pre-change oracle; 17 independent fixture cases.
- tests/Antiphon.Tests/slow-tests-allowlist.txt: required registration for the new Slow integration class.
- .antiphon/task-2e6d822d.md: requested design/proof report, force-added because transport/report files are normally ignored.

Restoration is checked byte-for-byte against the green production file and by an empty scoped diff against f6f3b43442ac8a405dfd4387ff9922c0311b1d35. Restored file timestamps are fresh; final CP-5 performs a real build. CP-5 and CP-6 run after this report is committed, so their source certificates bind to the final pushed checkout; exact final counts, failures and receipt lines are in the caller completion message. No tests/builds outside the listed checkpoint rows; CP-1 reruns=1 for the probe fix. Checkpoint validation and duration-tripwire are read-only evidence checks.

Performance scope: no transcript read at all for sessions without a valid owned/legacy input; narrow queue projection excludes RemoteSpillBody and unrelated entity fields. Receipt discovery streams only SQL-filtered prompt candidates, then loads only the four needed transcript columns at/after the first complete receipt. Discovery uses a query per relevant session, as does its sequence range, and full matcher text is intentionally preserved; no fixed truncation or speculative wall-clock/retry floor. Any false-positive candidates are checked by the original matcher. All Delegation bodies for relevant sessions remain eligible to supersede episodes (including older/canceled ordinary briefs), preserving the original rule.

Review the final pushed branch with the CP-5/CP-6 filters above. Card is the spec; no frozen plan or client/schema/AttentionKind change. These are ordinary cost mutation spot-checks, not SourceLanding Mutation: any PCs remain pending for method-scoped SourceLanding Mutation. Next stage is Final Review; plain land follows clean Final Review under this task's landing-owner contract.
