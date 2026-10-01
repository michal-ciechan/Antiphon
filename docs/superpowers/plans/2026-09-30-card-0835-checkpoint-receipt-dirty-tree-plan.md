# CARD-0835: checkpoint receipts identify the source actually exercised

Date: 2026-09-30. Plan baseline: `d7456a2352d15391f37eca8781c16db499a3759d`.
Stage: TestDesign complete; next: Code. Documentation only; no build or test execution.
Static audit baseline: `1edfc2a72abdd02d5613f5ff6297488dca9bdf02` (includes the
plan landed at `45321eaf`). CARD-0835 Code recount at `8331a9cf`: the 12-row
roster is amended to 201 planned executions per OS, including CARD-0833's 21
slot-path regressions. The 26 positive-control IDs and 35 independently applied
defect variants are unchanged.

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
| S4: contracts and regression fixtures | Owner docs and bundles in D-6; update clean approval seed helpers (including `C544World`) explicitly, preserving separate legacy/unknown fixtures; source fixtures and slow-test registration. | Existing named regression classes remain green. New tests are mapped below to mutations that change real outcomes. Follow the seed-caller audit below; any additional required row is a committed manifest amendment with its reason, never an ad hoc full suite. |

## Verification design

This is the finalized Code contract. New test names below are implementation
requirements, not tests claimed to exist or have run. Static inspection confirms
the existing filters/counts and the fixture seams; execution must still confirm
the fresh TRX roster on both operating systems.

### Inspection

Bodies read: `RunCheckpointScriptTests`, the script harness's runner/shim/format
cases, `CheckpointLineTests`, `CheckpointAppTests`, `ReportWriterTests`,
`ReportMergerTests`, the row runner's build/TRX cases, `CheckpointTestSupport`,
`ReviewEvidenceParserTests`, `ReviewEvidenceSettlementTests`' settlement helper and
authorization cases, `AgentTaskLandApprovalRequestTests`,
`AgentTaskLandApprovalPersistenceTests`' migration pattern,
`AgentTaskReviewEvidenceTests`' manual override cases, `DelegateScriptLandApprovalTests`,
and `C544World`'s real settlement entry point.

TestDesign additionally read the following bodies and their production boundaries:

| Inspected owner / fixture | Finding and coverage |
|---|---|
| `LandApproval.LoadUsableEvidenceAsync`, `LoadRecoveryEvidenceAsync`, `RevalidateFinalVerificationAsync`; `AgentTaskLandingProtocol.RecheckApprovalAsync` and unpublished/cleanup branches | The existing final-verification recheck returns early for unlatched owners. V-20/V-21 must reach it through the land service/protocol, with persisted evidence; a direct helper assertion is insufficient. |
| `LandingProtocolHarness.RequestAsync`, `RunAsync`, `RunQueuedAsync`, `RestartServicesAsync`; `InterimVerificationLandGuardTests.C544_RecoveryRevalidates` and `C544_PublishedCleanupCompatibility` | Reuse isolated DB, controlled Git trace and after-commit `SaveFault`. Restart rebuilds DI/queue while retaining persisted requests/operations. Copy fixture setup into helpers, never invoke those test methods. |
| `AgentTaskLandApprovalRecoveryTests.C488_OriginalApprovalNeverAdoptsHead`, `C488_ChangedSourceNeedsNewApproval` | These two exact CP-12 methods use explicit caller SHA, not Review evidence. Keep them as original-approval regressions; V-21 supplies the missing source-clean resume proof. |
| `AgentTaskLandAdoptionTests.AddReviewAsync`, self-recovery/adoption and published-cleanup cases; `ReviewEvidenceSettlementTests.AdoptionFixture` and its two adoption cases | Recovery evidence binds the source task, which can differ from the landing owner. V-21 uses `LandingSafetyHarness` for real temporary-repository recovery/adoption; V-19 uses real reply settlement. |
| `StageOutcomeService.RecordFindingAsync`; `AgentTaskReviewEvidenceTests.C488_OverrideDoesNotCopyApproval` and `C488_ManualFindingFieldsRestricted` | Overrides append/supersede rather than edit. Existing alias tests call other tests; new V-22 must have its own setup and fresh DB reads. Old true must survive on the old row without being copied to the new row. |
| `AgentTaskLandApprovalPersistenceTests.C488_MigrationDoesNotInventApproval` | V-22 needs an owned database migrated to the immediate predecessor of the new migration, then legacy SQL inserts. A current-model `EnsureCreated` fixture cannot prove upgrade behavior. |
| `RunCheckpointScriptTests`, `Invoke-C585Runner`, `CheckpointTestSupport`, `CheckpointAppTests`, report/line/merge fixtures and `DelegateScriptLandApprovalTests` | Existing offline shims and drivers are reusable; current script CWD is fixed and tool temp directories need explicit source observations. V-1-V-16/V-23 add actual source/qualification outcomes. |

### Test setup and seed audit

Missing setup to implement: the script harness currently forces the repository's
root as CWD; new cases need an owned temporary Git repository with committed seed
files and `.gitignore`, external fixtures/results, and a per-case CWD. Git/pwsh
spawners use `[ParallelLimiter<ProcessSpawnLimit>]` and Integration classification.
Pure serialization/parser tests remain Unit. Tool orchestration tests use
`FakeDriver`, `FixedSlotClient`, controlled gates and an injected source reader;
the Git/parity class exercises real Git. No nested real dotnet builds are needed.
DB tests use per-test isolated schemas/databases, scoped assertions and
`TUNIT_MAX_PARALLEL_TESTS=1`. Do not boot a server against a production runner.

Keep the script, helper, shim, gate files and result logs outside each temporary
source repository; only deliberate source edits go inside. Track a minimal project
path and `.gitignore` in the fixture. Invoke the production script by absolute path
with the fixture as CWD, and capture the root resolved by Git. Configure Git identity
and autocrlf per fixture; never change the host's global Git configuration. Each
internal case has a label and an independent repository or a proved reset to its
seed. Every expected refusal has a valid neighboring control and observes actual
exit/verdict, persisted facts and driver/slot calls. An inner PASS-line count is
never a TUnit execution count.

The source-snapshot unknown matrix includes unmerged index, symlink target-text
hashing without dereference, and unsupported submodule state. Use injected I/O
errors and gates for unreadable/racing files (root can read a chmod-restricted file).
For parity, pin literal canonical input bytes and a separately computed SHA-256
digest, not a digest generated by either implementation under test. Windows tests
use real Git/pwsh, Unicode/spaces and a linked worktree; Linux also uses newline
filenames. A platform that cannot create a symlink exercises that serialization
vector and reports the filesystem capability limitation explicitly.

Seed search covered `ReviewedSourceSha =`, `LandContractSeeds`, `C544World` and its
report/settlement callers. S4 must apply the following fixture-only adaptations;
do not add true to every historical `StageOutcome` or change parser defaults:

| Seed sites / callers | Required adaptation and roster disposition |
|---|---|
| `AgentTaskLandApprovalRequestTests.SeedReviewAsync`; `LandContractSeeds.SeedReviewAsync` used by `AgentTaskLandContractEndpointTests` | Explicit clean-source true on the valid Review seed, nullable override for false/legacy rows. Preserve SHA/ref/subject/repository/supersession refusal tests by changing only cleanliness. CP-7/9 exercise the gate; the endpoint transport is unchanged. |
| `C544World.ReviewReport`, `SettleReviewAsync`, `SettleExistingReviewAsync`; `ReviewEvidenceSettlementTests.SettleAsync` | Add a nullable source-clean argument and emit a field only when supplied. Clean fixture wrappers explicitly supply true; V-19 explicitly supplies false/null. Preserve raw missing-field grammar fixtures. CP-7/8 cover real settlement and adoption. |
| Other C544 consumers: `C544DeliveryRig`, `CardVerificationPolicyTests`, `InterimVerificationPolicyTests`, `VerificationRoundDispatchTests`, `VerificationRoundSettlementTests`, `VerificationRoundDeliveryTests`, `ReviewEvidenceDeliveryTests`, `InterimVerificationLandGitTests`, `InterimVerificationLandGuardTests` | Audit direct `ReviewReport` calls as well as wrappers. Successful evidence-backed land setup supplies true; baseline/scope-only and deliberate missing-field cases retain their intent. No new delivery or verification-round behavior is planned; their full classes are outside this closed roster. |
| Direct Review seeds in `AgentTaskLandAdoptionTests`, `StartRefRepairAdoptionTests`, `AgentTaskLandRequestTests`, `AgentTaskLandSourceFreshnessTests`, `InterimVerificationLandGuardTests`, `VerificationRoundDeliveryTests` | Mark valid approval seeds true so their existing, unrelated refusal reason remains reachable. V-20/V-21 reuse the admission/recovery scenarios in CP-7; no additional full-class sweep. |
| `AgentTaskReviewEvidenceTests` override/immutability seeds | Old approval explicitly true for the copy-prevention case; legacy null remains a separate case. CP-7/8 prove no inference and unchanged old coordinates. |
| `TwoOwnerLandingWorld`, `AgentTaskLandingStateTests`, `AgentTaskLandRecoveryTests`, `AgentTaskLandApprovalRecoveryTests` operation SHA assignments | These are landing-operation identities, not Review `StageOutcome` approval seeds. Do not invent a source-clean field or retrofit historical approval. CP-12 preserves the two caller-SHA controls. |
| `CheckpointLineTests`, `ReportWriterTests`, `ReportMergerTests`, `CheckpointAppTests`, `RowRunnerTests`, `RunSchedulerTests` | Use explicit known source/build bindings for normal fixtures and explicit unknown for legacy fixtures; plain non-Git temp directories must not silently stand for clean. CP-5 covers all six classes. |
| `CheckpointTaskOwnershipTests`, `CheckpointExecutorLogTests` | Their legacy `TempDir` roots are not Git repositories. The supplemental Final Unit lane exposed their stale success fixtures; inject explicit stable source observations at both create and execute boundaries. Keep production unknown-source refusal. |
| `TaskPlatformGuidanceTests` and bundle classification guards | The source-clean Review field must fit the existing 2,480-character stage-bundle margin. Preserve the pinned checkpoint, Review, and delivery phrases; rebaseline only the equivalent shortened Review invariant/audit lines, and mark the new slow classes in the registry. |

The seed-only sites outside the roster above are compatibility edits, not added
behavioral scope. Their common changed boundary is exercised by CP-7/8/9. If Code
needs to change their behavior or add new test methods, amend the manifest and cost
before running an additional selection; do not conceal the expansion in a wildcard.

### Delivery inventory

No session-input or notification transport changes. The new persisted datum travels
from bare Review report -> `AgentTaskReplyService` settlement transaction ->
`StageOutcome`/DTO -> land admission and restart revalidation, joined by outcome ID,
subject task ID and reviewed SHA. V-19 reads a new context after settlement and
idempotent replay; V-20/V-21 consume that evidence and inspect request/operation
state and Git mutations. This proves durable approval use, not delivery to a caller.
Existing completion delivery and its complete-UserPrompt requirement stay out of
scope; no event, queued request or receipt token is substituted for user delivery.

### Resume and override fixture contract

V-21's ordinary-resume matrix is **2 latch states x 4 interruption points x 3
source assertions = 24 labeled cases** inside one test. The interruption points
are accepted request without an operation, Prepared, Verified, and PushStarted
after its durable intent but before push. Admit with true first; for false/null
cases update only that evidence column through a fresh DB context, clear fault
injection, restart services, and resume the saved request (not a new approval).
Keep valid Final/Full SHA/ref/repository/subject coordinates. The true case at each
point must progress using the same approval; false/null must persist
`review_evidence_source_not_clean`, retain original approval identity, and perform
zero new pin/update-ref/reset/rebase/merge/push mutations. Compare trace offsets
after setup, target/remote refs and verifier calls; also check no publication.
For the no-operation cut, refusal may live on the request/event rather than an
operation, so do not manufacture an operation just to assert its reason.

Recovery/adoption adds **2 modes x 3 assertions x 2 timings = 12 cases**: self
`RecoverReviewedSource` and `AdoptFromTaskId`; false/null/true; refusal at request
admission and re-read after an admitted request before source mutation. For adoption,
the Review subject/ref are the source task's, not the owner receiving the source.
Use the real temporary Git setup from `AdoptionFixture`/`LandingSafetyHarness`;
factor it into a test helper. The controlled harness currently has no recovery
arguments: do not pretend `LandingProtocolHarness.RequestAsync` supports them.
Assert admission refusal queues nothing; post-admission refusal changes neither
owner nor remote source/target refs. True controls reach the intended recovery.

Finally, for each of ordinary/self/adoption publication, leave cleanup residue,
then change the evidence true -> false and true -> null in separate fixtures:
**6 cleanup cases**. Confirmed publication retains operation/receipt identity;
cleanup succeeds after removing only the owned sentinel, with no new verification,
preparation or push. An unconfirmed PushStarted operation belongs to the first
matrix, even if its intent is durable. These **42 internal cases** still count as
one V-21 execution. Use bounded after-commit gates, no sleep-based races.

V-22 uses independent subfixtures for migration and override. Migrate a dedicated
database to the predecessor of `AddReviewSourceClean`, insert a legacy clean Review
with SHA/ref/repository but no new column, apply the actual generated migration
twice, and read null in a fresh context. Inspect column nullability/default and
round-trip true/false/null; do not assert only the migration's source text. For
override, seed old true, then call `RecordFindingAsync` with omitted/false/true
assertions and valid explicit SHA as separate cases; expect null/false/true on the
new DTO and DB row, `SupersedesId` pointing to the old row, and old true unchanged.
Also omit SHA, use wrong stage/Found=true, and use an unauthorized subject: none
may mint usable approval or append an authorized source assertion. Supplying only
the new field must not evade the existing evidence-field restrictions. No test
method calls another test as setup; reuse helper methods only.

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
landing fixtures plus the real-Git recovery fixture above; mark it Integration and
Slow, apply `ParallelLimiter<ProcessSpawnLimit>`, and register
`Antiphon.Tests.Application.CheckpointSourceApprovalTests` in
`tests/Antiphon.Tests/slow-tests-allowlist.txt`. Factor helper reuse without calling
entire other test methods as test setup.

### Guards the regression

| ID | Existing selection | Purpose |
|---|---|---|
| R-1 | `RunCheckpointScriptTests` (24 executions) | Literal arguments, fresh TRX, counters, exit codes, properties, source-bearing line grammar and build-slot release. |
| R-2 | `CheckpointLineTests` (3), `ReportWriterTests` (6), `ReportMergerTests` (1), `CheckpointAppTests` (3), `RowRunnerTests` (13), `RunSchedulerTests` (14), `CheckpointSlotExecutorTests` (10), `CheckpointSlotContractTests` (11) | Report compatibility and scheduling/build/row behavior with explicit fixture source states; the added 21 CARD-0833 cases guard the same executor/slot path touched by S2. |
| R-3 | `ReviewEvidenceParserTests` (19 existing + 2 new = 21), `AgentTaskReviewEvidenceTests` (16), `ReviewEvidenceSettlementTests` (11) | Report authority, scope, authorization, immutable persisted approval and settlement. |
| R-4 | `AgentTaskLandApprovalRequestTests` (21), `AgentTaskLandApprovalPersistenceTests` (10); two exact recovery methods in CP-12 | Current admission, schema invariants and preservation of original approval on resume. New V-21 exercises the new gate through the real boundary. |
| R-5 | `DelegateScriptLandApprovalTests` (4 existing + 1 new = 5), `CheckpointManifestDocumentationTests` (7) | CLI contract and owner-doc pins. |

Static census at the TestDesign baseline found no other data-source/repeat/matrix
attributes in the selected existing methods. Parser expansion is 12 methods -> 19
executions (`C488_ReviewBlockGrammar`: 3; `C488_ReviewBlockGrammar_rejects_invalid_sha`:
6); request expansion is 19 -> 21 (`C498_NonReviewedRolesAdmitExplicitSha`: 3).
All remaining selected existing methods execute once. New matrices deliberately
use one method each, with no `[Arguments]`. CP-6 therefore has 14 methods/21
executions; internal alias calls do not add executions.

| CP | Exact executed roster and count | Literal `-Expect` value |
|---|---|---|
| CP-1 | `CheckpointSourceStateTests`: 7 source tests + 1 Git-fixture cleanup regression = 8 | `CheckpointSourceStateTests` |
| CP-2 | `RunCheckpointSourceScriptTests`: 5 new | `RunCheckpointSourceScriptTests` |
| CP-3 | `RunCheckpointScriptTests`: 24 | `RunCheckpointScriptTests` |
| CP-4 | `CheckpointSourceExecutionTests`: 6 new | `CheckpointSourceExecutionTests` |
| CP-5 | `CheckpointLineTests`: 3; `ReportWriterTests`: 6; `ReportMergerTests`: 1; `CheckpointAppTests`: 3; `RowRunnerTests`: 13; `RunSchedulerTests`: 14; `CheckpointSlotExecutorTests`: 10; `CheckpointSlotContractTests`: 11 = 61 | `CheckpointLineTests,ReportWriterTests,ReportMergerTests,CheckpointAppTests,RowRunnerTests,RunSchedulerTests,CheckpointSlotExecutorTests,CheckpointSlotContractTests` |
| CP-6 | `ReviewEvidenceParserTests`: 19 + 2 new = 21 | `ReviewEvidenceParserTests` |
| CP-7 | `CheckpointSourceApprovalTests`: 4 new | `CheckpointSourceApprovalTests` |
| CP-8 | `AgentTaskReviewEvidenceTests`: 16; `ReviewEvidenceSettlementTests`: 11 = 27 | `AgentTaskReviewEvidenceTests,ReviewEvidenceSettlementTests` |
| CP-9 | `AgentTaskLandApprovalRequestTests`: 21 | `AgentTaskLandApprovalRequestTests` |
| CP-10 | `AgentTaskLandApprovalPersistenceTests`: 10 | `AgentTaskLandApprovalPersistenceTests` |
| CP-11 | `DelegateScriptLandApprovalTests`: 4 + 1 new; `CheckpointManifestDocumentationTests`: 7 = 12 | `DelegateScriptLandApprovalTests,CheckpointManifestDocumentationTests` |
| CP-12 | `AgentTaskLandApprovalRecoveryTests.C488_OriginalApprovalNeverAdoptsHead` and `.C488_ChangedSourceNeedsNewApproval`: 1 each = 2 | `AgentTaskLandApprovalRecoveryTests.C488_OriginalApprovalNeverAdoptsHead,AgentTaskLandApprovalRecoveryTests.C488_ChangedSourceNeedsNewApproval` |

Total after the Windows teardown refinement and hidden-index controls: **175 existing + 26 new = 201 per OS**, 402 for the two qualifications. The added CP-1 methods cover assume-unchanged and skip-worktree hidden edits in both readers; each also proves the clean positive control before and after clearing the flag.
The base source recount agrees with every other planned class count: 24, 3, 6,
1, 3, 13, 14, 19 expanded parser cases, 16, 11, 21 expanded request cases,
10, 4 and 7 in roster order; CP-12 still selects its two exact methods.
Each row requires exactly its stated count, all passed, zero failed/skipped, and
no other executed classes/methods; `MinExecuted` alone enforces only the lower
bound. Inspect TRX class/method identities as well as counts, especially the six
CP-5 class-prefix operands and CP-12's two method-prefix operands. First execution
must confirm these static counts; a skip, extra selection or filter miss is not a
reason to silently lower a floor. Do not use `--list-tests` as execution evidence.

### Guard inventory

Variant IDs are part of the guard/control identity: e.g. G-7A maps only to PC-7A.
There are **35 guards, 35 mapped variants under 26 PC IDs, missing=0, duplicate
maps=0**. Rows group shared validation decisions; input combinations beneath a
single predicate remain labeled matrix assertions, not extra defect runs.
The CP-5 CARD-0833 regression expansion adds no V method, guard or PC variant:
the 35 guard-to-control mappings below remain the complete Mutation inventory.

| Guard | Decision / assertion protected | Sole control |
|---|---|---|
| G-1A | D-1 ignored outputs do not change source identity | PC-1A |
| G-1B | D-1 tracked output-path edits cannot be excluded | PC-1B |
| G-2 | D-1 index bytes participate even when worktree bytes equal HEAD | PC-2 |
| G-3 | D-1 untracked bytes participate, not just paths/counts | PC-3 |
| G-4 | D-1 failed/unstable capture is unknown | PC-4 |
| G-5 | D-1/D-7 canonical byte framing agrees across implementations | PC-5 |
| G-6 | D-2/D-4 script receipt preserves observed dirty facts | PC-6 |
| G-7A | D-3 strict dirty preflight stops before leasing | PC-7A |
| G-7B | D-3 strict expected SHA must equal observed SHA | PC-7B |
| G-7C | D-2/D-3 post-slot source recheck precedes driver start | PC-7C |
| G-8 | D-2 script driver-boundary drift invalidates certification | PC-8 |
| G-9A | D-3 strict reuse requires a complete matching clean build binding | PC-9A |
| G-9B | D-3 a failed rebuild invalidates its predecessor stamp | PC-9B |
| G-10 | D-4 script interruption cannot invent an end observation | PC-10 |
| G-11A | D-5 script validator needs complete current-schema evidence | PC-11A |
| G-11B | D-5 script validator requires eligible source state | PC-11B |
| G-11C | D-5 script validator binds a successful selected receipt | PC-11C |
| G-12 | D-4 tool reports preserve row source, including command rows | PC-12 |
| G-13 | D-2 executor admission compares the persisted CreateRun source | PC-13 |
| G-14A | D-2 normal tool driver boundaries stop further work on drift | PC-14A |
| G-14B | D-2 known-flaky reruns cannot replace invalid source evidence | PC-14B |
| G-15A | D-3 every CLI entry forwards expected-source SHA | PC-15A |
| G-15B | D-3 tool reuse requires equal effective build/source binding | PC-15B |
| G-16 | D-4 tool abnormal termination cannot invent clean completion | PC-16 |
| G-17 | D-4 merge cannot relabel retained rows | PC-17 |
| G-18A | D-5 tool validation refuses legacy/incomplete evidence | PC-18A |
| G-18B | D-5 tool report heading, row and receipt must agree | PC-18B |
| G-19 | D-6 Review source declaration is unique and explicit | PC-19 |
| G-20 | D-6 missing declaration stays unknown | PC-20 |
| G-21 | D-6 settlement persists the parsed assertion | PC-21 |
| G-22 | D-6 evidence-backed admission requires true | PC-22 |
| G-23 | D-6 unpublished resume rechecks persisted approval regardless of latch | PC-23 |
| G-24 | D-6 migration never invents historical clean approval | PC-24 |
| G-25 | D-6 manual replacement binds only its own explicit assertion | PC-25 |
| G-26 | D-6 CLI sends only explicitly supplied source assertion | PC-26 |

### Positive controls

Execute after land in the SourceLanding Mutation stage. Code runs ordinary V/R;
Review judges the PC design before land. Each entry below is an independently
compiling production defect; a build failure, zero selection, fixture timeout or
unrelated assertion does not count as red. Method-scoped baseline/red/restored-green
uses an external copy of the unmodified checkpoint driver **and its new source
helper** beside `lib/build-slot.ps1`. Do not mutate the driver executing the phase.

| Variant | One compiling production defect | Exact V method / named red assertion |
|---|---|---|
| PC-1A | Include nontracked ignored files in the tool source inventory. | V-1: `ignored-output-identity` expects zero dirt and unchanged fingerprint after writing ignored outputs. |
| PC-1B | Filter tracked `bin-*` entries out of that inventory. | V-1: `tracked-output-dirty` expects a force-added output-path edit to count and change identity. |
| PC-2 | Omit cached diff bytes from the tool fingerprint. | V-2: `index-only-content-change` expects different fingerprints for two staged contents with identical porcelain records and worktree restored to HEAD. Status alone still says dirty, so asserting only dirty would not kill this mutant. |
| PC-3 | Omit untracked content digests from the tool fingerprint. | V-3: `untracked-same-path-new-bytes` expects changed hash with identical path/count. |
| PC-4 | Convert capture failure to known clean/count zero. | V-4: `capture-error-is-unknown` expects unknown/null and ineligibility for a deterministic Git failure. |
| PC-5 | Change the PowerShell canonical framing version byte. | V-5: `fixed-vector-parity` expects the independently pinned digest and tool/PowerShell equality on identical fixture bytes. Newline/NUL-path cases remain ordinary assertions; this is one framing defect, not an unspecified alternative mutation. |
| PC-6 | Hardcode script terminal dirty/source-state tokens to zero/clean. | V-6: `dirty-receipt-agrees-with-source-json` expects parsed nonzero dirty and dirty state matching the actual capture, while retaining the test exit/counts. |
| PC-7A | Omit the strict script dirty-preflight refusal. | V-7: `dirty-preflight-no-lease` expects exit 2 and zero slot-acquire calls as well as zero dotnet calls. A later guard refusing is still red on the lease assertion. |
| PC-7B | Omit strict script expected-SHA equality. | V-7: `wrong-sha-no-lease` expects exit 2/no lease/no dotnet for a clean tree and a different valid full SHA. |
| PC-7C | Omit the script post-slot source recheck. | V-7: `slot-edit-no-driver` expects drift refusal and zero build/run calls after a slot shim changes source before granting; assert the recorded post-wait observation, not merely a final exit. |
| PC-8 | Make the script driver-boundary comparison accept a changed fingerprint. | V-8: `driver-drift-is-changed` expects changed/exit 2 after gated same-count content drift and preserved passed TRX counts. |
| PC-9A | Bypass the script strict reused-build binding qualification. | V-8: `invalid-stamp-no-tests` expects exit 2/zero test calls for each missing, dirty and one-coordinate-mismatched stamp. One shared qualification bypass is one variant. |
| PC-9B | Leave the previous build-source stamp valid when a replacement build fails. | V-8: `failed-rebuild-invalidates-stamp` expects absent/invalid stamp and strict reuse refusal after a failed rebuild of the same clean source. Otherwise the old matching stamp would pass. |
| PC-10 | On a handled script interruption, substitute start for the unavailable end capture and mark it clean. | V-9: `interruption-has-no-observed-end` expects null/unknown end and validator refusal. Use a reachable catch/finally seam; killing the wrapper before it can write cannot test this defect. |
| PC-11A | Normalize missing/legacy script source evidence into a complete clean default. | V-10: `legacy-receipt-ineligible` expects validator exit 2 on an otherwise valid legacy receipt. |
| PC-11B | Bypass the script validator's clean/stable-source eligibility predicate. | V-10: `dirty-receipt-ineligible` expects exit 2 for internally consistent dirty source with a matching dirty build binding and passing tests. |
| PC-11C | Bypass qualification of the selected receipt's identity/verdict. | V-10: `selected-receipt-ineligible` expects exit 2 for an otherwise clean, matching-source receipt with a failed test; the same matrix separately changes CP identity, SHA, counts and tokens. |
| PC-12 | BuildReport constructs clean row evidence from request.Commit instead of propagating actual row source. | V-11: `dirty-row-preserved` expects actual dirty fingerprint/state in persisted JSON, Markdown, line and git evidence for TUnit and command rows. |
| PC-13 | At executor admission replace the saved CreateRun observation with the current capture instead of comparing them. | V-12: `queued-source-not-readmitted` expects original start identity retained, changed/exit 2 and no driver for A-at-create/B-at-execute. |
| PC-14A | Make the tool's normal driver-boundary source comparison accept drift. | V-12: `driver-drift-stops-next-row` expects changed state, no next driver and all already-owned drivers awaited; place drift after build/row gates and check each labeled boundary. |
| PC-14B | Let the known-flaky rerun replace its original source/verdict after drift. | V-12: `rerun-cannot-certify-drift` expects changed/exit 2 and retained original source even when the rerun TRX is green. |
| PC-15A | Drop expected-source SHA when binding CLI input to the run/row request. | V-13: `strict-cli-refuses-wrong-sha` expects refusal/no driver and persisted expected SHA for each run/start/row entry. Invoke the real argument parser in-process with fake launch/slot I/O. |
| PC-15B | Bypass the tool's effective build-binding equality check. | V-13: `property-mismatch-no-tests` expects strict reuse exit 2/no driver when only effective MSBuild properties differ. |
| PC-16 | Synthesize a known clean end after executor exception. | V-14: `executor-error-has-no-observed-end` expects unknown end and failed qualification while retaining existing crash/owner exit precedence. Owner cancellation and timeout remain separate ordinary matrix assertions. |
| PC-17 | Remove merge source compatibility checks and stamp the latest heading identity onto retained rows. | V-15: `old-dirty-row-not-relabeled` expects merge refusal for an older dirty CP retained beside a later clean CP. |
| PC-18A | Upgrade schema-1/missing tool evidence to a known clean default during validation. | V-16: `legacy-report-ineligible` expects exit 2 despite valid SHA/counts. |
| PC-18B | Ignore tool heading/row/receipt source equality during validation. | V-16: `row-heading-disagreement` expects exit 2 when exactly one row's otherwise valid source differs from the heading. |
| PC-19 | Accept the last duplicate source-clean declaration. | V-17: `duplicate-clean-is-unknown` expects null for false then true and true then false, with all other grammar valid. |
| PC-20 | Default absent ReviewedSourceClean to true. | V-18: `full-scope-is-not-source-clean` expects null for a valid bare SHA/Clean/Full report without the field. |
| PC-21 | Persist true instead of the parsed nullable assertion in settlement. | V-19: `settled-source-assertion-roundtrip` expects false/null in a new DB context and DTO after real report settlement and replay. |
| PC-22 | Remove the source-clean predicate from LoadUsableEvidenceAsync. | V-20: `unclean-evidence-no-request` expects conflict code `review_evidence_source_not_clean`, no new durable request, empty queue and no Git mutations for false/null. All other approval dimensions are valid. |
| PC-23 | Retain the old early return for unlatched owners in the unpublished approval recheck. | V-21: `unlatched-resume-refuses-unclean` expects persisted source-not-clean refusal and zero new mutations after true admission, false/null update and restart at each saved cut. Test false/unlatched first so the intended assertion is unambiguous. |
| PC-24 | Change the actual new migration's nullable column default/backfill to true. | V-22: `legacy-source-clean-remains-null` expects null on the upgraded legacy row and no true SQL default. Mutate the executed migration, not just the model snapshot. |
| PC-25 | Assign new override source cleanliness from the superseded row. | V-22: `override-does-not-inherit-true` expects new null/false and old true unchanged, with a valid explicit SHA to keep authorization checks satisfied. |
| PC-26 | Always add reviewedSourceClean=true to delegate.ps1 finding JSON. | V-23: `finding-json-preserves-explicitness` expects absent/null when omitted, false when explicit false, and true only when supplied; inspect actual loopback request bodies. |

Each V ID above resolves to one exact `Class.method` in the V table. The literal
method filter is `/*/*/<Class>/<method>` (no namespace or class sweep); every
baseline/red/restored-green phase has `-MinExecuted 1`, `-Expect <Class>.<method>`,
exactly one TUnit result and zero skips. For example PC-23 uses
`/*/*/CheckpointSourceApprovalTests/recovery_and_resume_recheck_source_assertion`.
Each assertion label above must appear in its failure message. A mutant that is
caught only by another guard or fixture error is not successful evidence for its
named assertion. Keep source capture/guard boundaries observable in structured
evidence or driver/slot traces so later refusals cannot hide an omitted early gate.

PC-1/7/9/11/14/15/18 expand as **2+3+2+3+2+2+2 = 16** runs; the other 19 IDs each
have one run: **35 total**. In particular PC-5 and PC-16 choose one concrete defect;
PC-9A is one qualification bypass tested against a matrix, and PC-9B separately
invalidates stale provenance. Never apply two variants in the same red phase.

Build-stamp fixtures vary repository root, project, output, effective properties,
SHA/fingerprint, state, schema, missing/truncated fields and start/end completeness
one at a time from a valid stamp; stamp-writing gates test failed build and drift.
Receipt fixtures similarly start valid and alter exactly one dimension, keeping
row IDs, full SHA, counts and verdict independent. For PC-11B the build remains
verified against its dirty source, so a build mismatch cannot mask the state guard.
For PC-18B each row is internally valid; only cross-record equality is wrong.
Both validators consume the same fixture bytes in V-10/V-16 and assert identical
qualification outcomes. True/clean neighboring controls prevent always-refuse
implementations from passing the matrices.

All PCs are statically specified as executable, with 23/23 new methods mapped to
an outcome-changing defect. Execution is pending SourceLanding Mutation. Code must
identify the concrete edited production expression for each variant in its handoff;
if implementation splits one planned shared guard into independently bypassable
guards, commit the additional variant and reconcile cost before commissioning it.

### Platform qualification and known hazards

Run the same closed table on Linux and Windows. CP-1 and CP-2 require real Git and
pwsh on each OS; simulated `C671_PLATFORM` alone is not Windows source-path evidence.
The remaining rows run on both without live provider, PTY, apphost or desktop UI.
Keep Linux's script default `UseAppHost=false`; no FakeClaude apphost-dependent test
is selected. DB rows need the established isolated PostgreSQL test environment.

CARD-0823 was read: the tool's broker-null crash can leak a lease before building.
Use `scripts/run-checkpoint.ps1` directly for these rows; it takes its own slot.
CARD-0845 moves a wrapped `.ps1` target into a child process, so an outer build-slot
wrapper would create a nested lease wait. The tool is tested in-process with fake
slots, not used as the outer launcher. CARD-0818's wedged executor-log concurrency test and CARD-0828's
owner-watch uncertainty pair are excluded: neither is needed for the source change.
The closed CP table has no Unit-lane or Checkpoints-namespace sweep. The Final Code
brief separately authorizes one supplemental Unit lane; it found S4 fixture,
bundle-size and Slow-category repairs. This adds no CP row or V/R execution
and does not change the 201-per-OS census. New terminal tests use
bounded gates with unconditional release/cancel/await cleanup instead of those
flaky fixture clocks. A timeout is reported with identities, never retried into a
silent green or called a successful PC.

### Out of scope

Continuous attestation between observations, signing remote evidence, changing
explicit caller-SHA authority, live service deployment, and notification transport
are not part of this implementation. Neither CARD-0823 launcher repair nor
CARD-0818/0828 concurrency/owner-watch qualification belongs in these checkpoint
rows. Existing land identity, scope, supersession, cleanup-custody and current-tree
guards keep their semantics; their relevant controls are exercised by R-3/R-4 and
the valid neighboring cases in V-20/V-21, without commissioning unrelated PCs.

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
pwsh -NoProfile -File scripts/run-checkpoint.ps1 -Name CP-1 -Project tests/Antiphon.Tests -OutputPath bin-c835/ -Filter '/*/*/CheckpointSourceStateTests/*' -MinExecuted 8 -Expect CheckpointSourceStateTests -ExpectedSourceSha $sourceSha -ResultsRoot .antiphon/c835-linux-r1
```

Invoke the checkpoint driver directly as shown. It acquires and releases one lease
for its build and run. `BuildSlotBroker.cs:80-87` binds a lease to the holder PID and
start time; a child launched by `build-slot.ps1` cannot reuse its parent's lease.
Do not add an outer wrapper, `-NoSlot` or broker changes. At a budget of one, the
parent would hold the only slot while its self-leasing child waits.

For CP-2 onward substitute the exact row filter/min/Expect class names, add
`-NoBuild`, and keep `bin-c835/`. On Windows use a new root
`.antiphon/c835-windows-r1`. For DB rows set `TUNIT_MAX_PARALLEL_TESTS=1` only in the
driver's environment and restore it afterward. All rows run sequentially. The
Markdown `\|` in a table becomes a plain `|` in the literal CLI filter; remove that
Markdown escape and single-quote the entire filter. Do not pass a backslash to
TUnit or split the command at a pipe. For example CP-12's exact CLI argument is
`-Filter '/*/*/AgentTaskLandApprovalRecoveryTests/(C488_OriginalApprovalNeverAdoptsHead*)|(C488_ChangedSourceNeedsNewApproval*)'`.
Use the literal `-Expect` values in the census above as one comma-separated argument.
Slot timeout
is exit 4 and a row not run. No rebuild between rows unless committed source changed;
then repeat the affected build group with fresh results, recording reruns.

Run the receipt validator over every row for the pinned SHA before declaring Review
eligible. Report its verdict alongside the unedited CP lines. The table's test
filters exercise the validator; reading/validating the resulting evidence does not
launch another test. No product build/test is required for this TestDesign commit.

### Cost

Estimates, not measured results: ordinary V/R floor **60 minutes Linux**, **92
minutes Windows**, excluding slot queueing and first-time dependency downloads.
One isolated test-project build includes the tool via its existing project reference.
CP-1 includes that build (12/18 minutes); CP-7/8/10 are the slow DB/settlement rows
(12/20, 6/10, 9/14 minutes). CP-7's allowance increased from 7/12 to 12/20 after
auditing its 42 resume/recovery/cleanup cases and real migration fixture; counts
remain four TUnit executions. A separate Windows qualification is mandatory, so
total ordinary host time is 152 minutes. Authoring estimate: 300 minutes, including the
two implementations, migration, consumer gate and fixtures; first Linux Code
dispatch estimate 360 minutes plus observed slot wait. Split authoring by slices,
not by repeatedly rebuilding the same slice.

PC budget: 26 controls expand to **35 defect runs** with the listed variants.
Budget 6 minutes per method-scoped baseline/red/restored-green cycle on Linux
(210 minutes), plus 20 minutes setup/discovery/reporting = **230 minutes**; Windows
9 minutes per cycle plus 25 setup = **340 minutes** if commissioned there. Migration
and settlement controls may exceed a simple parser cycle; the per-cycle average
reserves their cost. Numeric combined Linux authoring + ordinary V/R + Mutation
floor: **590 minutes**; mandatory Windows ordinary qualification adds 92 = **682**.
Any additional guard variant requires an explicit cost amendment.

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
| CP-1 | all | `tests/Antiphon.Tests -> bin-c835/` | source-git-parity | `/*/*/CheckpointSourceStateTests/*` | V-1-V-5 + hidden-index controls + Git-fixture cleanup | all 8 executed, 0 failed/skipped, real Linux/Windows Git | 8 | 12 | 18 | true | n/a |
| CP-2 | all | CP-1 | script-source | `/*/*/RunCheckpointSourceScriptTests/*` | V-6-V-10 | all 5 executed, 0 failed/skipped | 5 | 4 | 6 | true | n/a |
| CP-3 | all | CP-1 | script-regression | `/*/*/RunCheckpointScriptTests/*` | R-1 | all 24 executed, 0 failed/skipped | 24 | 4 | 6 | true | n/a |
| CP-4 | all | CP-1 | tool-source | `/*/*/CheckpointSourceExecutionTests/*` | V-11-V-16 | all 6 executed, 0 failed/skipped | 6 | 2 | 3 | true | n/a |
| CP-5 | all | CP-1 | tool-regression | `/*/*/(CheckpointLineTests*)\|(ReportWriterTests*)\|(ReportMergerTests*)\|(CheckpointAppTests*)\|(RowRunnerTests*)\|(RunSchedulerTests*)\|(CheckpointSlotExecutorTests*)\|(CheckpointSlotContractTests*)/*` | R-2 | exactly 61 executed across the eight named classes, 0 failed/skipped | 61 | 3 | 4 | true | n/a |
| CP-6 | all | CP-1 | review-grammar | `/*/*/ReviewEvidenceParserTests/*` | V-17,V-18,R-3 | all 21 expanded executions, 0 failed/skipped | 21 | 1 | 1 | true | n/a |
| CP-7 | all | CP-1 | source-approval | `/*/*/CheckpointSourceApprovalTests/*` | V-19-V-22 | exactly 4 executed with all internal admission/recovery/migration variants, 0 failed/skipped | 4 | 12 | 20 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-8 | all | CP-1 | review-settlement | `/*/*/(AgentTaskReviewEvidenceTests*)\|(ReviewEvidenceSettlementTests*)/*` | R-3 | exactly 27 executed (16+11), 0 failed/skipped | 27 | 6 | 10 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-9 | all | CP-1 | land-admission | `/*/*/AgentTaskLandApprovalRequestTests/*` | R-4 | all 21 expanded executions, 0 failed/skipped | 21 | 3 | 4 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-10 | all | CP-1 | approval-persistence | `/*/*/AgentTaskLandApprovalPersistenceTests/*` | R-4 | all 10 executed, 0 failed/skipped | 10 | 9 | 14 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-11 | all | CP-1 | cli-and-docs | `/*/*/(DelegateScriptLandApprovalTests*)\|(CheckpointManifestDocumentationTests*)/*` | V-23,R-5 | exactly 12 executed (5+7), 0 failed/skipped | 12 | 2 | 3 | true | n/a |
| CP-12 | all | CP-1 | original-approval-recovery | `/*/*/AgentTaskLandApprovalRecoveryTests/(C488_OriginalApprovalNeverAdoptsHead*)\|(C488_ChangedSourceNeedsNewApproval*)` | R-4 | exactly 2 executed, 0 failed/skipped | 2 | 2 | 3 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |

## Completion and rollout

TestDesign is complete by static inspection: the twelve filters/counts are
reconciled, resume/override setup is specified, and 35 guard/control variants cover
all 23 proposed methods. No build, test, discovery or mutation was executed in this
stage. Code implements the named methods and the S1-S4 slices, using the closed
table and the documented PowerShell fallback for CARD-0823. New behavior outside
this contract requires an explicit manifest amendment, not an unlisted sweep.

Static validation parsed the Markdown cells (including escaped pipes), matched
class/method operands against source declarations plus the proposed V roster,
expanded `[Arguments]`, checked disjoint selections and every Min, summed both
time columns, and checked the bijection between guard and PC-variant IDs. Result:
12 rows, 201 planned executions per OS, 60/92 estimated minutes, 35 unique mappings and
23/23 proposed methods covered. `git diff --check` also passed. These are static
design checks, not measured TUnit results or evidence that the PCs have gone red.

Code completes only with the new tests, all ordinary rows for both OS qualifications,
per-row source-bearing receipts at the pushed SHA, and all PCs still explicitly
pending. Review independently checks the source evidence and durable gate, then
hands the original Code owner to land. Coordinate server migration and bundle
activation before relying on the new persisted approval field. Check actual loaded
server SHA after authorized activation; publication alone is not activation.
Historical receipts remain diagnostics. Any approval that lacks positive clean
source evidence requires fresh Review; never edit an old report into compliance.
