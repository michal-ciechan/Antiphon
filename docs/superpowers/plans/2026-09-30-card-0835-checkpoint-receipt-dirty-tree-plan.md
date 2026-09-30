# CARD-0835: checkpoint receipts identify the source actually exercised

Date: 2026-09-30. Plan baseline: `d7456a2352d15391f37eca8781c16db499a3759d`.
Stage: Plan; next: TestDesign. This commit changes documentation only.

## Problem and ground truth

A passing test run against edited files must not certify the commit named by HEAD.
CARD-0835 records a real Linux `build=reused` mutation run with six uncommitted
server edits whose receipt named only `913300828ac13abef680298d8b64a0d89d66a656`.
The card was read through `scripts/card.ps1 get CARD-0835`; its history is empty.

| Card assumption / boundary | Ground truth at the plan baseline | Consequence |
|---|---|---|
| Script receipt identifies the tested commit | `scripts/run-checkpoint.ps1:180` builds/runs before `:280-288` reads HEAD and prints it. No initial source observation exists. | Dirty files and a HEAD change during the run can both be attributed to an unrelated clean SHA. |
| Tool has no dirty-source protection | `tools/Antiphon.Checkpoints/CheckpointApp.cs:310-315` saves only commit/branch in `request.json`; `:142` passes that commit to the scheduler; `:409` copies it into the report. `Program.cs:294-310` does the same for `row`. | Both detached execution and direct rows need the new contract. Fixing the script alone leaves the primary runner unsafe. |
| Nothing records dirtiness | `tools/Antiphon.Checkpoints/Evidence/GitSnapshot.cs:11-15` already writes human-readable porcelain status into `git.txt`, captured at executor entry (`CheckpointApp.cs:85`). | The missing feature is an authoritative, structured source identity shared by receipts and consumers, plus end observations. Preserve the useful existing diagnostics. |
| Receipt format is free to change | `Report/CheckpointLine.cs:31-42`, `scripts/test-run-checkpoint.ps1:336`, and `tests/Antiphon.Tests/Checkpoints/CheckpointLineTests.cs:10-13` pin the current format. | Update the producers and their validators together. Keep `commit=` a full Git object ID. |
| Every row in a merged report belongs to its heading SHA | `Report/ReportMerger.cs:12-29` selects latest rows by CP ID while using the latest run's commit, with no source equality check. | A merge must not relabel earlier dirty or different-source rows as clean. |
| `-ExpectedSourceSha` already checks checkpoint source | It is a landing argument in `scripts/delegate.ps1:298,817`. Neither checkpoint producer exposes it today. | Add explicit strict checkpoint verification; retain the distinct landing approval argument. |
| Land consumes checkpoint lines | `server/Application/Services/ReviewEvidence.cs:45-77` parses subject/SHA/scope; settlement saves those in `AgentTaskReplyService.cs:4362-4440`; `LandApproval.cs:56-91` trusts persisted Review facts. It does not parse TRX, checkpoint lines, or a runner's local files. | Review must validate receipts, and the durable evidence must preserve its clean-source assertion for land to enforce. A new stdout token alone is insufficient. |
| SHA alone is always Review approval | `tests/Antiphon.Tests/Application/AgentTaskLandApprovalRequestTests.cs:57-69` deliberately admits an explicit caller SHA without Review evidence. | Do not silently abolish explicit caller approval. Enforce cleanliness on evidence-backed approval; do not describe a bare caller SHA as a verified checkpoint receipt. |
| Build outputs should not make a tree dirty | `.gitignore` already excludes `bin/`, `obj/`, `bin-*/`, `.antiphon/`, and the documented generated output directories. | Use Git's tracked/untracked classification, not a source-extension whitelist or a blanket path exclusion that could hide tracked edits. |
| Existing validation scripts can simply be adjusted | Repository search found format assertions in `scripts/test-run-checkpoint.ps1` and C# tests, but no general checkpoint-receipt validation script. `ManifestValidator` validates plans, not execution evidence. | Add a small receipt validator; do not claim the manifest validator already verifies source. |

Owners read: `docs/project-context.md`, the checkpoint/build-slot/mutation sections
of `docs/testing-and-build.md`, the Plan/Review/land sections of
`docs/orchestration-loop.md`, and `docs/ops-http.md`. The stage bundles establish
the Plan/TestDesign boundary. The live runner defaults and catalogue were read:
both Linux and Windows lanes exist. Resolve their current availability at dispatch;
this plan pins operating-system qualifications, not a fleet location.

## Decisions

### D-1. One versioned source contract, with a count and a content fingerprint

Introduce a concrete source snapshot in
`tools/Antiphon.Checkpoints/Evidence/SourceSnapshot.cs` and its PowerShell equivalent
in `scripts/lib/checkpoint-source.ps1`. Git is the external I/O seam; inject its
reader into tool fixtures rather than injecting fabricated cleanliness into production.
The source evidence object is version 1 and contains:

- `start` and `end`: `commit`, `dirtyFiles`, `fingerprint`, `observedAtUtc`, and
  capture status (`known` or `unknown`, with a bounded error code).
- `state`: `clean`, `dirty`, `changed`, or `unknown`.
- `buildSource`: `verified`, `unknown`, `mismatch`, or `notApplicable` for a command row.

`dirtyFiles` is an integer when known and null when unknown. Zero is never an error
fallback. Both snapshots include full 40- or 64-character object IDs. `commit=` in
the human receipt is the **start** commit; the end commit stays in the evidence.
Append, after the existing slot/rerun suffix, these mandatory tokens:

```text
dirty=0 source=<sha> sourceState=clean buildSource=verified
dirty=6 source=<sha>+dirty:<sha256> sourceState=dirty buildSource=verified
dirty=unknown source=unknown sourceState=unknown buildSource=unknown
```

A changed run keeps the start count/token and uses `sourceState=changed`; the
end snapshot explains why. Counts describe source entries, not failing tests.
A rename is one changed entry carrying both paths. Existing `build=`, TRX counts,
failure identities, slot state and rerun count keep their meanings.

Use `git status --porcelain=v1 -z --untracked-files=all --ignore-submodules=none`
and check the native exit code. Parse rename records and filenames as NUL-delimited
data, never line-by-line. The fingerprint is SHA-256 over a versioned,
length-prefixed byte sequence containing HEAD, the porcelain records, binary
`git diff HEAD` and cached diff (both with `--no-ext-diff --no-textconv --no-color
--no-renames` and fixed path quoting), and each
nonignored untracked path plus its content digest, in ordinal path order. Include
both index and worktree state: a staged edit with the working file restored to HEAD
must still be dirty. Do not persist or print diff contents. Stream hashes rather
than loading arbitrary untracked files into one string.

Tracked edits always count, including force-added files under `bin-*`. Untracked
ignored output does not. Do not add another broad exclusion list. Respect repository
Git ignore semantics; this is not an inventory of intentionally ignored local
configuration. Hash symlink text without following its target. An unreadable file,
unmerged index, unsupported submodule observation, failed Git command, or source
that is observed changing while capture is being completed is `unknown`, never clean.
Compare two complete captures, including content hashes, with a bounded two-pass
budget; status/HEAD alone cannot detect two different edits to an already-dirty
file. Changed file metadata during hashing also invalidates that capture. Test the
two implementations against the same
temporary Git repositories and fixed serialization vectors.

Rejected: a file count alone (two different edits to one file look identical),
`git diff HEAD` alone (misses untracked files and index-only differences), and
shell text pipelines (lose filename boundaries and cross-platform byte fidelity).

### D-2. Observe every execution boundary; diagnostic dirty runs remain possible

For the script, capture before build admission, recheck after any slot wait, capture
after build, before the test driver, and after it exits. For the tool, capture at
`CreateRun`, executor admission, before/after each build, before/after each row
and known-flaky rerun, and before final report publication. Bind each row to the
source of its build. Stop admitting new work after observed drift; await already
owned drivers and preserve their actual results. Do not kill unrelated processes.
Baseline comparison gets its own snapshot in its own worktree; it cannot replace
the main run's identity.

Stable dirty source may run without a strict SHA option, including the post-land
Mutation driver's intended edits. Its existing test exit remains 0/1/3 as applicable,
but its receipt is diagnostic and never approval evidence. Source drift or an
unknown capture is a source-integrity failure, exit **2**, with a reason such as
`source_changed` or `source_unknown`; retain test counts and logs even if tests passed.
Keep owner-ended exit 7 and existing slot/timeout/crash semantics; source-integrity
exit 2 participates in the existing invalid-input precedence, not a new exit code.
No automatic rerun may turn drift into a green certification.

This detects differences at observed boundaries. An edit made and completely
restored between observations is not provably detectable. The existing prohibition
on editing source during a run remains mandatory. An immutable build snapshot or
continuous write monitor is outside this card; do not claim continuous attestation.

### D-3. Strict SHA verification and reused-build provenance

Add `-ExpectedSourceSha` to `scripts/run-checkpoint.ps1` and
`--expected-source-sha` to tool `run`, `start`, and `row`; persist it in `RunRequest`.
It requires a normalized full SHA, a known clean matching start, stable clean end,
and verified build binding. Reject a dirty/unknown/mismatching preflight before
acquiring a slot or starting dotnet, and recheck after waiting. A source mismatch is
exit 2 with an explicit reason, not a test failure. Do not strip `+dirty:` from input
and treat the remaining SHA as an approval.

After a successful stable build, atomically write a versioned
`checkpoint-build-source.json` under that project's requested `bin-<name>/` output.
Bind it to repository root, project, output path, effective MSBuild properties,
source fingerprint and source state. Remove/invalidate a previous stamp before a
replacement build begins. A failed build cannot leave an old valid stamp.
The tool also retains the binding on its build result and report.

Strict `-NoBuild`/`row --no-build` requires a matching stamp; absent, dirty, or
mismatching provenance refuses before tests. A diagnostic reuse may still run and
report `buildSource=unknown|mismatch`; it does not become certifying merely because
the worktree is clean now. This closes dirty-build -> restore-source -> reused-build
laundering. Reuse within one tool run obeys the same rule. Ordinary Code/Review uses
the strict option. Mutation intentionally omits it, while preserving dirty metadata.

### D-4. Persist source facts, including failure and report-merge paths

Bump tool execution reports to schema 2. Carry the same `source` evidence object in
`request.json` (start/admission portion), state, `report.json`, each report row and
build binding. Add an explicit source summary to `report.md` and `git.txt`; retain
the existing human Git diagnostics. The script writes `source.json` with that same
object alongside its TRX/logs and includes the mandatory tokens in every terminal
receipt, including failed builds and missing TRX. Unknown/crashed observations
remain explicitly unknown. The validator never accepts an incomplete start-only
record. An executor crash cannot synthesize an end snapshot or an eligible row.

`ReportMerger` may merge certification evidence only when every retained row belongs
to the same commit and compatible source fingerprint/state/build binding. Reject
mixed or unknown source with an actionable error; do not take the latest heading's
identity for older rows. Keep diagnostics readable without merging incompatible
runs into one purported certificate. Legacy schema-1 reports remain readable as
unknown, but cannot be upgraded to clean without a new run. The same rule applies
to missing fields in handcrafted fixtures.

### D-5. An executable validator, not a regex match for `commit=`

Add `scripts/validate-checkpoint-receipt.ps1` accepting an evidence file and
`-ExpectedSourceSha`; share parsing/qualification logic with the script helper.
It accepts the script's `source.json` plus its terminal receipt, or the tool's
schema-2 report and selected CP IDs. It checks structured fields, corresponding
receipt tokens, successful row verdicts, and source/build binding equality.
Certification requires all selected rows to be clean, stable and for the expected
SHA. Missing, duplicate, malformed, conflicting, legacy, dirty, changed or unknown
facts fail closed with exit 2. A failed test still fails qualification despite clean
source. A source validator does not replace the plan's roster/minimum checks.

Expose the equivalent validation in the tool's report code, with a direct validation
command that performs no build or detached launch. Keep the script usable when the
tool launcher is broken. Update all pinned format assertions and `git.txt` checks;
tests must assert parsed values and qualification outcomes, not merely token presence.

### D-6. Carry Review's source assertion through the actual land boundary

Add `reviewedSourceClean: true|false` to the bare Review evidence block and nullable
`ReviewedSourceClean` to `ReviewEvidence.Result`, `StageOutcome`, its DTO, and
`ReviewEvidenceDto`. Missing/malformed/duplicate declarations are unknown (null).
SHA/block grammar and `ordinaryScopeCompleted` keep their existing meanings;
syntactically usable historical evidence may be displayed but is not clean-source
approval. Review may declare true only after its complete required receipt selection
passes D-5 for `reviewedSourceSha`. Dirty, changed and unknown all prevent true.

Persist the assertion in the same transaction as the existing evidence coordinates
in `AgentTaskReplyService`. Add a nullable column with a CLI-generated EF migration;
historical rows remain null, without backfilling true. Update the manual-finding
request and `scripts/delegate.ps1` with an explicitly supplied clean-source assertion;
do not copy it from a superseded row or infer it from `Found=false`. Preserve the
existing restriction to authorized successful Review approval. Display the value
in the script's Review-evidence status output.

`LandApproval.LoadUsableEvidenceAsync` requires `ReviewedSourceClean == true` and
returns `review_evidence_source_not_clean` otherwise. Exercise ordinary admission,
recovery/adoption, and pending/resumed operations. Today some resume paths recheck
only final-verification-latched owners; make every **evidence-backed, not yet
published** operation re-read this assertion before further repository mutation.
Do not retroactively change a confirmed publication or require new approval to do
receipt-backed cleanup. Keep the server's existing current-worktree cleanliness,
ref, repository, SHA, scope and supersession checks.

Explicit caller approval with no evidence ID stays a separate authorized mode.
Passing dirty evidence must not fall back to that mode, including after a 409.
This continues the existing trust in Review's report; the server cannot read a
runner-local evidence path and does not invent a signed artifact service here.

Update `server/Bundles/stage-code.md`, `stage-review.md`, `delegate-basics.md`,
`docs/testing-and-build.md`, and the Review/land contract in
`docs/orchestration-loop.md`. Also document the additive finding/status API fields
in `docs/antiphon-api.md` and `docs/ops-http.md`. Old Review approvals lacking the
field need fresh Review before an evidence-backed land; they are not silently blessed.

### D-7. Cross-platform and operational boundaries

Use `ProcessStartInfo.ArgumentList`/literal argument lists and repository-root Git
working directories. PowerShell scripts stay ASCII-only and keep their established
PowerShell version requirements. Repository paths containing spaces and non-ASCII
characters must work on both OSes; Linux adds legal newline filenames. Git worktrees
have a `.git` file, not necessarily a directory. Windows case comparison is for
filesystem roots only; do not lowercase Git path bytes used in the hash. Exercise
LF/CRLF and Git's normal autocrlf behavior. Fingerprints need equality between the
two implementations over the **same** source bytes, not between different OS checkouts
with different untracked bytes.

No changes to the slot broker, scheduler concurrency policy, production runner,
deployment scripts, or the defects tracked by CARD-0823/0818/0828. No fake traffic
to the live broker in tests. Own and await every gated driver in a `finally` before
deleting its fixture repository.

## Slices

Commit and push each slice on the assigned task branch; no rebase or merge of
master. All ordinary rows run after S1-S4 are committed, so there is one build group.

| Slice | Implementation and files | Tests / decisive result |
|---|---|---|
| S1: source contract and script | New `scripts/lib/checkpoint-source.ps1`; `scripts/run-checkpoint.ps1`; new `scripts/validate-checkpoint-receipt.ps1`; new `tools/Antiphon.Checkpoints/Evidence/SourceSnapshot.cs` and source evidence types; extend the offline harness with temporary Git repos and gated shim phases. | New `CheckpointSourceStateTests` and `RunCheckpointSourceScriptTests`; existing `RunCheckpointScriptTests` format assertions updated. Dirty diagnostic results differ from clean certification; strict admission invokes no driver when refused. |
| S2: tool propagation | `RunRequest.cs`, `State/RunState.cs`, `CheckpointApp.cs`, `Program.cs`, `Execution/{BuildStep,RowRunner,RunScheduler}.cs`, `Report/{CheckpointLine,ReportModel,ReportWriter,ReportMerger}.cs`, `Evidence/GitSnapshot.cs`; new source reader/validator as needed beside these owners. Preserve baseline/rerun identities. | New `CheckpointSourceExecutionTests`; update existing report/row/scheduler fixtures with explicit source observations. A delayed executor, reused build or merged report cannot certify a different tree. |
| S3: durable Review/land gate | `server/Application/Services/{ReviewEvidence,AgentTaskReplyService,StageOutcomeService,LandApproval,AgentTaskLandingProtocol}.cs`; `Domain/Entities/StageOutcome.cs`; DTO mappings including `AgentTaskService`; `Infrastructure/Data/AppDbContext.cs` and generated migration/snapshot; `scripts/delegate.ps1`. | Two new parser methods; new `CheckpointSourceApprovalTests` using isolated DB and existing settlement/land fixtures; one delegate-script case. Persisted false/null refuses before mutation; true follows existing SHA/ref/scope checks. |
| S4: contracts and regression fixtures | Owner docs and bundles in D-6; update clean approval seed helpers (including `C544World`) explicitly, preserving separate legacy/unknown fixtures; source fixtures and slow-test registration. | Existing named regression classes remain green. New tests are mapped below to mutations that change real outcomes. TestDesign inventories all affected seed callers before Code; any additional required row is a committed manifest amendment with its reason, never an ad hoc full suite. |

## Verification design

This is the initial executable inventory for the separate TestDesign stage, not a
claim that tests already exist or have run. TestDesign must read the remaining
landing recovery fixtures, finish the guard/variant inventory, and confirm setup
and costs before handing off to Code. Keep the decisions unless an unverifiable
boundary requires returning to Plan.

### Inspection and test setup

Bodies read: `RunCheckpointScriptTests`, the script harness's runner/shim/format
cases, `CheckpointLineTests`, `CheckpointAppTests`, `ReportWriterTests`,
`ReportMergerTests`, the row runner's build/TRX cases, `CheckpointTestSupport`,
`ReviewEvidenceParserTests`, `ReviewEvidenceSettlementTests`' settlement helper and
authorization cases, `AgentTaskLandApprovalRequestTests`,
`AgentTaskLandApprovalPersistenceTests`' migration pattern,
`AgentTaskReviewEvidenceTests`' manual override cases, `DelegateScriptLandApprovalTests`,
and `C544World`'s real settlement entry point.

Missing setup to implement: the script harness currently forces the repository's
root as CWD; new cases need an owned temporary Git repository with committed seed
files and `.gitignore`, external fixtures/results, and a per-case CWD. Git/pwsh
spawners use `[ParallelLimiter<ProcessSpawnLimit>]` and Integration classification.
Pure serialization/parser tests remain Unit. Tool orchestration tests use
`FakeDriver`, `FixedSlotClient`, controlled gates and an injected source reader;
the Git/parity class exercises real Git. No nested real dotnet builds are needed.
DB tests use per-test isolated schemas/databases, scoped assertions and
`TUNIT_MAX_PARALLEL_TESTS=1`. Do not boot a server against a production runner.

No session-input or notification delivery path is added. Tests prove receipt
publication and durable approval admission, not delivery to a user. Existing
settlement/queue behavior is unchanged; its transcript rules remain in force.

### Proves it works now

All method names below are planned exact names. Each listed method is one TUnit
execution; matrix loops are internal assertions, not extra executions.

| ID | Class.method | Required assertions |
|---|---|---|
| V-1 | `CheckpointSourceStateTests.clean_and_ignored_outputs_match_head` | Known clean SHA/count; adding ignored bin/obj/bin-*/.antiphon output preserves it; force-tracked output edits are dirty. |
| V-2 | `CheckpointSourceStateTests.index_and_worktree_edits_are_dirty` | Unstaged, staged, deletion, rename, binary and staged-plus-restored-worktree cases all dirty; changing bytes without changing file count changes fingerprint. |
| V-3 | `CheckpointSourceStateTests.untracked_contents_and_paths_affect_identity` | New source counted; content-only edit changes hash; deletion restores clean; spaces/Unicode/CRLF and Linux newline paths remain unambiguous. |
| V-4 | `CheckpointSourceStateTests.failed_or_unstable_capture_is_unknown` | Failed Git, missing HEAD, unreadable/hash-race and unsupported submodule capture never produce zero/clean; deterministic injected failures, not permission assumptions under root. |
| V-5 | `CheckpointSourceStateTests.script_and_tool_snapshots_agree` | Both readers match independent fixed serialization vectors and real-repo clean/tracked/untracked cases; `.git` file worktree and nested CWD resolve the same root. |
| V-6 | `RunCheckpointSourceScriptTests.C835_DiagnosticReceipts` | Real script + fake dotnet yields clean/tracked/untracked identities; `source.json`, line and `git.txt` agree; dirty test failure remains exit 1, clean success exit 0. |
| V-7 | `RunCheckpointSourceScriptTests.C835_StrictAdmission` | Wrong SHA, dirty and unknown refused before any dotnet call; clean full SHA accepted, including 64-character validation vector; recheck a slot-wait edit. |
| V-8 | `RunCheckpointSourceScriptTests.C835_DriftAndReuse` | Gated build/run edits, same-count byte change, dirty-to-clean restoration and HEAD movement produce changed/exit 2; no-build clean stamp accepted; missing/dirty/mismatched stamp and failed-build stale stamp refused under strict mode. |
| V-9 | `RunCheckpointSourceScriptTests.C835_TerminalEvidence` | Build failure, no/malformed TRX and interruption retain observed/unknown facts; no fabricated end or success; all owned processes reaped by the fixture. |
| V-10 | `RunCheckpointSourceScriptTests.C835_ReceiptValidation` | Clean current receipt accepted; dirty/changed/unknown, legacy/missing/duplicate/conflicting fields, wrong SHA, build mismatch, failed tests and malformed counts refused. |
| V-11 | `CheckpointSourceExecutionTests.clean_and_dirty_runs_publish_bound_source` | In-process `CreateRun` -> executor -> report/git/line agrees, both TUnit and command rows; dirty diagnostic never qualifies. |
| V-12 | `CheckpointSourceExecutionTests.changed_admission_and_driver_boundaries_refuse` | Change after `CreateRun`, slot wait, build, row and known-flaky rerun detected; queued drivers do not start; already running drivers are awaited; baseline cannot overwrite source. |
| V-13 | `CheckpointSourceExecutionTests.strict_cli_and_reuse_require_clean_binding` | `run/start/row` propagate expected SHA; strict source and no-build stamp checks match the script; effective build property/source mismatch refuses. |
| V-14 | `CheckpointSourceExecutionTests.terminal_paths_do_not_invent_clean_evidence` | Build/TRX error, timeout, owner cancellation and executor exception preserve facts and existing exit precedence; missing end stays unknown. |
| V-15 | `CheckpointSourceExecutionTests.merge_cannot_launder_source_identity` | Same-source merge preserves source per row; different SHA, dirty fingerprints, missing/legacy facts or incompatible build binding refuses, including a clean latest heading with an older dirty row. |
| V-16 | `CheckpointSourceExecutionTests.validation_requires_complete_consistent_source` | Tool validator agrees with the script on the D-5 fixture matrix; full report row/source/token equality and failed-test verdict checked. |
| V-17 | `ReviewEvidenceParserTests.C835_SourceCleanGrammar` | Exactly one explicit true/false parsed; missing/duplicate/malformed/quoted fields stay null; existing SHA/scope/heading semantics retained. |
| V-18 | `ReviewEvidenceParserTests.C835_SourceStateCannotBeInferred` | Clean finding, Full scope and bare SHA never imply true. Dirty suffix is not a Git object ID. |
| V-19 | `CheckpointSourceApprovalTests.settlement_persists_source_assertion` | Real settlement with true/false/missing state, fresh DB read and idempotent settlement; no inference from clean outcome or scope. |
| V-20 | `CheckpointSourceApprovalTests.land_admission_requires_clean_review_source` | Real land request: false/null 409 with no queued request/mutation; true accepted only with matching SHA/ref/subject/scope; explicit caller mode still works; no fallback when evidence is refused. |
| V-21 | `CheckpointSourceApprovalTests.recovery_and_resume_recheck_source_assertion` | Recovery/adoption and unpublished resumed evidence-backed operations with latched AND unlatched owners reject false/null before first mutation; confirmed publication/cleanup retains its existing contract. |
| V-22 | `CheckpointSourceApprovalTests.migration_and_override_preserve_unknown` | Upgrade a legacy row to null; true/false/null round-trip; manual override never copies old true, explicit authorized true works; failed/wrong-stage override cannot mint approval. |
| V-23 | `DelegateScriptLandApprovalTests.C835_FindingSourceCleanIsExplicit` | Loopback stub sees cleanliness only when explicitly supplied; status distinguishes false/null from true; no fallback land POST. |

`CheckpointSourceApprovalTests` uses the existing C544 settlement and controlled
landing fixtures; mark it Slow and register its full name. TestDesign must factor
fixture reuse without calling entire other test methods as test setup.

### Guards the regression

| ID | Existing selection | Purpose |
|---|---|---|
| R-1 | `RunCheckpointScriptTests` (24 executions) | Literal arguments, fresh TRX, counters, exit codes, properties, source-bearing line grammar and build-slot release. |
| R-2 | `CheckpointLineTests` (3), `ReportWriterTests` (6), `ReportMergerTests` (1), `CheckpointAppTests` (3), `RowRunnerTests` (13), `RunSchedulerTests` (14) | Report compatibility and scheduling/build/row behavior with explicit fixture source states. |
| R-3 | `ReviewEvidenceParserTests` (19 existing + 2 new = 21), `AgentTaskReviewEvidenceTests` (16), `ReviewEvidenceSettlementTests` (11) | Report authority, scope, authorization, immutable persisted approval and settlement. |
| R-4 | `AgentTaskLandApprovalRequestTests` (21), `AgentTaskLandApprovalPersistenceTests` (10); two exact recovery methods in CP-12 | Current admission, schema invariants and preservation of original approval on resume. New V-21 exercises the new gate through the real boundary. |
| R-5 | `DelegateScriptLandApprovalTests` (4 existing + 1 new = 5), `CheckpointManifestDocumentationTests` (7) | CLI contract and owner-doc pins. |

Counts above were derived from `[Test]` plus `[Arguments]` in baseline source, not
from test discovery or measured TRX. New matrices deliberately use one method each.
First execution must confirm every listed class/method and actual expanded counts.
Do not silently lower a floor when an unintended skip or filter miss occurs.

### Positive controls

Execute after land in the SourceLanding Mutation stage. Code runs ordinary V/R;
Review judges the PC design before land. Each entry below is an independently
compiling production defect; a build failure, zero selection, fixture timeout or
unrelated assertion does not count as red. Method-scoped baseline/red/restored-green
uses an external copy of the unmodified checkpoint driver **and its new source
helper** beside `lib/build-slot.ps1`. Do not mutate the driver executing the phase.

| PC / guard | Deliberate defect | Exact method(s) required to fail |
|---|---|---|
| PC-1 clean/output classification | Include ignored build outputs as dirty (variant A); suppress tracked edits under bin-* (variant B). | V-1 |
| PC-2 tracked/index capture | Drop cached/index evidence, returning clean for a staged edit with HEAD bytes in worktree. | V-2 |
| PC-3 untracked identity | Omit untracked content digests, leaving only path/count. | V-3 |
| PC-4 unknown capture | Convert Git/capture failure into known clean/count 0. | V-4 |
| PC-5 byte/path parity | Change the PowerShell canonical hash framing or decode NUL paths as lines. | V-5 |
| PC-6 script propagation | Emit dirty=0/sourceState=clean in the script while capture says dirty. | V-6 |
| PC-7 strict script admission | Bypass the preflight dirty check (A), expected-SHA check (B), or post-slot-wait check (C), separately. | V-7 |
| PC-8 script drift | Skip the post-driver comparison and certify a changed fingerprint. | V-8 |
| PC-9 script reuse | Accept a missing/dirty/mismatching build-source stamp under strict NoBuild; independently leave the old stamp valid after a failed rebuild. | V-8, each defect separately |
| PC-10 script terminal paths | Reuse start as end after interrupted execution and mark it clean. | V-9 |
| PC-11 script validator | Treat absent/legacy source as clean (A), ignore dirty state (B), or accept mismatched/failed selected receipt (C), separately. | V-10 |
| PC-12 tool propagation | BuildReport drops the row's source and constructs a clean record from request.Commit. | V-11 |
| PC-13 executor admission | Omit comparison with the CreateRun observation. | V-12, admission case |
| PC-14 tool driver/rerun drift | Bypass the pre/post driver source guard, then separately the known-flaky rerun guard. | V-12, corresponding boundary cases |
| PC-15 tool strict/reuse | Drop ExpectedSourceSha forwarding (A) or skip build-binding equality (B). | V-13 |
| PC-16 tool terminal paths | Synthesize known clean end after executor exception/owner cancellation. | V-14 |
| PC-17 merge source equality | Remove source compatibility admission and stamp latest run's source onto retained rows. | V-15 |
| PC-18 tool validator | Accept schema-1/missing fields (A) or ignore row/heading disagreement (B). | V-16 |
| PC-19 Review grammar | Accept the last of duplicate cleanliness declarations. | V-17 |
| PC-20 no inference | Default ReviewedSourceClean to true when the declaration is absent. | V-18 |
| PC-21 settlement persistence | Persist true regardless of parsed false/null. | V-19 |
| PC-22 land admission | Bypass `ReviewedSourceClean == true` in LoadUsableEvidenceAsync. | V-20 |
| PC-23 resume/recovery gate | Skip source-clean revalidation for an unlatched pending operation. | V-21 |
| PC-24 unknown migration | Give the new column a true backfill/default. | V-22, migrated legacy row |
| PC-25 manual override | Copy source cleanliness from the superseded approval. | V-22, override case |
| PC-26 CLI explicitness | Always send reviewedSourceClean=true from delegate.ps1. | V-23 |

V IDs resolve to the exact class/method table, never an entire namespace. Controls
with A/B/C variants run separately. TestDesign must split any further independently
bypassable guards it identifies and keep a 1:1 guard/control map. Every proposed
new method has at least one outcome-changing control; none is a string self-compare.

### Platform qualification and known hazards

Run the same closed table on Linux and Windows. CP-1 and CP-2 require real Git and
pwsh on each OS; simulated `C671_PLATFORM` alone is not Windows source-path evidence.
The remaining rows run on both without live provider, PTY, apphost or desktop UI.
Keep Linux's script default `UseAppHost=false`; no FakeClaude apphost-dependent test
is selected. DB rows need the established isolated PostgreSQL test environment.

CARD-0823 was read: the tool's broker-null crash can leak a lease before building.
Use `scripts/run-checkpoint.ps1` for these rows, under `scripts/build-slot.ps1`, as
the task brief requires. The tool is tested in-process with fake slots, not used as
the outer launcher. CARD-0818's wedged executor-log concurrency test and CARD-0828's
owner-watch uncertainty pair are excluded: neither is needed for the source change.
No Unit-lane or Checkpoints-namespace sweep is authorized. New terminal tests use
bounded gates with unconditional release/cancel/await cleanup instead of those
flaky fixture clocks. A timeout is reported with identities, never retried into a
silent green or called a successful PC.

### Runnable procedure

S3 has one **declared supporting build outside the ordinary checkpoint table**:
EF must load the changed model to generate the migration before the final source
can be committed and tested. This is scaffolding, not verification. Restore the
repo-local dotnet tools per the owner, then build `server` once under
`scripts/build-slot.ps1` with `--property:OutputPath=bin-c835-schema/`. Run the
leased `dotnet ef migrations add AddReviewSourceClean --project server --no-build`
with process-scoped MSBuild `OutputPath=bin-c835-schema/` so metadata resolves that
same output; restore the previous environment value afterward. Keep the forward
slash and use no default live `bin/` output. Record the supporting build and its
exit explicitly. Its 5-minute Linux / 8-minute Windows estimate is included in
the authoring allowance below, not hidden in the CP execution counts. No other
supporting build is planned.

After S4 is committed and pushed, record HEAD and verify the tree is clean. Execute
CP-1, then each subsequent row with the same output and `-NoBuild`. Use a fresh
results root per round and `-ExpectedSourceSha` on every ordinary row:

```powershell
$sourceSha = (git rev-parse HEAD).Trim()
pwsh -NoProfile -File scripts/build-slot.ps1 -Label card-0835-CP-1 -- ./scripts/run-checkpoint.ps1 -Name CP-1 -Project tests/Antiphon.Tests -OutputPath bin-c835/ -Filter '/*/*/CheckpointSourceStateTests/*' -MinExecuted 5 -Expect CheckpointSourceStateTests -ExpectedSourceSha $sourceSha -ResultsRoot .antiphon/c835-linux-r1
```

Pass the `.ps1` command directly as shown, **not** a second `pwsh -File` process:
`build-slot.ps1:126-128` calls PowerShell scripts in its process and
`BuildSlotBroker.cs:80-87` returns the existing lease to that same PID/start identity.
This avoids nested processes holding one lease each while waiting for another.
The inner row releases after its child exits; the wrapper's final duplicate release
can report unknown lease. Do not solve that diagnostic with `-NoSlot` or broker
changes. If a deployment's lease mode cannot support this documented same-holder
invocation, report the row blocked rather than launching an unleased alternative.

For CP-2 onward substitute the exact row filter/min/Expect class names, add
`-NoBuild`, and keep `bin-c835/`. On Windows use a new root
`.antiphon/c835-windows-r1`. For DB rows set `TUNIT_MAX_PARALLEL_TESTS=1` only in the
driver's environment and restore it afterward. All rows run sequentially. The
script's native argument handling keeps escaped table pipes literal. Slot timeout
is exit 4 and a row not run. No rebuild between rows unless committed source changed;
then repeat the affected build group with fresh results, recording reruns.

Run the receipt validator over every row for the pinned SHA before declaring Review
eligible. Report its verdict alongside the unedited CP lines. The table's test
filters exercise the validator; reading/validating the resulting evidence does not
launch another test. No product build/test is required for this Plan commit itself.

### Cost

Estimates, not measured results: ordinary V/R floor **54 minutes Linux**, **83
minutes Windows**, excluding slot queueing and first-time dependency downloads.
One isolated test-project build includes the tool via its existing project reference.
CP-1 includes that build (12/18 minutes); CP-7/8/10 are the slow DB/settlement rows
(7/12, 6/10, 9/14 minutes). A separate Windows qualification is mandatory, so total
ordinary host time is 137 minutes. Authoring estimate: 300 minutes, including the
two implementations, migration, consumer gate and fixtures; first Linux Code
dispatch estimate 354 minutes plus observed slot wait. Split authoring by slices,
not by repeatedly rebuilding the same slice.

PC budget: 26 controls expand to **35 defect runs** with the listed variants.
Budget 6 minutes per method-scoped baseline/red/restored-green cycle on Linux
(210 minutes), plus 20 minutes setup/discovery/reporting = **230 minutes**; Windows
9 minutes per cycle plus 25 setup = **340 minutes** if commissioned there. Migration
and settlement controls may exceed a simple parser cycle; the per-cycle average
reserves their cost. Numeric combined Linux authoring + ordinary V/R + Mutation
floor: **584 minutes**; mandatory Windows ordinary qualification adds 83 = **667**.
TestDesign must reconcile any extra guard variant with this arithmetic.

Savings are structural: one isolated build instead of one per class, no launcher
bootstrap, no unfiltered Unit/namespace run, and reuse of fake dotnet/TRX fixtures
instead of child builds in script tests. No measured speedup is claimed.

### Checkpoints

This is the closed ordinary Code/Review list. Every TUnit row resolves to one
isolated build plus one exact filter; CP-2 onward reuses CP-1 only after the same
`all` committed slice group. Pipe escaping is Markdown syntax, not an extra shell
backslash. All expected failures/skips are zero on both platforms.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | EstimatedMinutesWindows | Serial | Environment |
|---|---|---|---|---|---|---|---:|---:|---:|---|---|
| CP-1 | all | `tests/Antiphon.Tests -> bin-c835/` | source-git-parity | `/*/*/CheckpointSourceStateTests/*` | V-1-V-5 | all 5 executed, 0 failed/skipped, real Linux/Windows Git | 5 | 12 | 18 | true | n/a |
| CP-2 | all | CP-1 | script-source | `/*/*/RunCheckpointSourceScriptTests/*` | V-6-V-10 | all 5 executed, 0 failed/skipped | 5 | 4 | 6 | true | n/a |
| CP-3 | all | CP-1 | script-regression | `/*/*/RunCheckpointScriptTests/*` | R-1 | all 24 executed, 0 failed/skipped | 24 | 4 | 6 | true | n/a |
| CP-4 | all | CP-1 | tool-source | `/*/*/CheckpointSourceExecutionTests/*` | V-11-V-16 | all 6 executed, 0 failed/skipped | 6 | 2 | 3 | true | n/a |
| CP-5 | all | CP-1 | tool-regression | `/*/*/(CheckpointLineTests*)\|(ReportWriterTests*)\|(ReportMergerTests*)\|(CheckpointAppTests*)\|(RowRunnerTests*)\|(RunSchedulerTests*)/*` | R-2 | exactly 40 executed across the six named classes, 0 failed/skipped | 40 | 2 | 3 | true | n/a |
| CP-6 | all | CP-1 | review-grammar | `/*/*/ReviewEvidenceParserTests/*` | V-17,V-18,R-3 | all 21 expanded executions, 0 failed/skipped | 21 | 1 | 1 | true | n/a |
| CP-7 | all | CP-1 | source-approval | `/*/*/CheckpointSourceApprovalTests/*` | V-19-V-22 | all 4 executed with all internal admission/recovery/migration variants, 0 failed/skipped | 4 | 7 | 12 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-8 | all | CP-1 | review-settlement | `/*/*/(AgentTaskReviewEvidenceTests*)\|(ReviewEvidenceSettlementTests*)/*` | R-3 | exactly 27 executed (16+11), 0 failed/skipped | 27 | 6 | 10 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-9 | all | CP-1 | land-admission | `/*/*/AgentTaskLandApprovalRequestTests/*` | R-4 | all 21 expanded executions, 0 failed/skipped | 21 | 3 | 4 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-10 | all | CP-1 | approval-persistence | `/*/*/AgentTaskLandApprovalPersistenceTests/*` | R-4 | all 10 executed, 0 failed/skipped | 10 | 9 | 14 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-11 | all | CP-1 | cli-and-docs | `/*/*/(DelegateScriptLandApprovalTests*)\|(CheckpointManifestDocumentationTests*)/*` | V-23,R-5 | exactly 12 executed (5+7), 0 failed/skipped | 12 | 2 | 3 | true | n/a |
| CP-12 | all | CP-1 | original-approval-recovery | `/*/*/AgentTaskLandApprovalRecoveryTests/(C488_OriginalApprovalNeverAdoptsHead*)\|(C488_ChangedSourceNeedsNewApproval*)` | R-4 | exactly 2 executed, 0 failed/skipped | 2 | 2 | 3 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |

## Completion and rollout

TestDesign is required because this crosses script bytes, asynchronous tool
admission, report compatibility, persistence and landing recovery. It must finish
the guard inventory, especially resume/override paths and build-stamp validation,
and amend the closed table before Code if fixture inspection changes the roster.

Code completes only with the new tests, all ordinary rows for both OS qualifications,
per-row source-bearing receipts at the pushed SHA, and all PCs still explicitly
pending. Review independently checks the source evidence and durable gate, then
hands the original Code owner to land. Coordinate server migration and bundle
activation before relying on the new persisted approval field. Check actual loaded
server SHA after authorized activation; publication alone is not activation.
Historical receipts remain diagnostics. Any approval that lacks positive clean
source evidence requires fresh Review; never edit an old report into compliance.
