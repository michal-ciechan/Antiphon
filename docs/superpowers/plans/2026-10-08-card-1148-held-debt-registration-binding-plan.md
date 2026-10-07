# CARD-1148: bind Held settlement debt to a registration before superseding

Plan task: `45a8ab48-6130-437d-bf4a-5fdb1cb0d42c`. Requested artifact date: 2026-10-08;
inspection performed 2026-10-07 UTC. Board: Antiphon. Fetched `origin/master` and
fast-forwarded the assigned branch from `b5e78700ae9a76430c13d75cc03399055dd82e59` to
**`e2c5150101014b259b436adb951f163dc0b1cdcd`**. All ground-truth citations below refer
to that master commit, not an unlanded repair branch.

## Outcome and scope decision

The card's premise is correct. **Production changes are required to implement the
binding; tests or documentation alone cannot clear Held debt safely.** The brief's
explicit **no migration** constraint prevents the direct, typed persistence change
the card requests. The debt and retirement rows have no registration identity or
general extension payload. This is not another missing predicate in the sweep.

This is a completed **decision plan under D-1's no-migration default**, not an
authorization to implement the conditional slices below. **next: decide**. The
caller must choose between commissioning the additive schema/producer work described
here, or retaining F3d and leaving CARD-1148 and CARD-1136 item 3 open. If the schema
scope is accepted, follow with **TestDesign** to qualify the registration producer,
removal evidence, and expanded cleanup scope before Code. The verification section
below specifies the required witnesses and candidate checkpoint list; it is not a
claim that those unimplemented tests exist or that a Code-ready design is approved.

No runtime files, migrations, assertions, settings, or owner documents change in this
Plan task. The requested plan is the deliverable. No AppHost restart is needed for it.

Read live card text for CARD-1148, CARD-1136, CARD-1082, CARD-1149, CARD-1150,
CARD-1115, CARD-1105, CARD-1125, and CARD-1128. Read the CARD-1082 follow-up plan,
especially its **F3d** amendment, and the landed recovery service and tests. The older
D-3/F3b/F3c supersession recipes and their old roster counts are historical; F3d
supersedes them. The F3d evidence table also predates later documentation/DI tests.

## Ground truth

Paths are repository-relative. Line citations were checked against the master SHA above.

| What the card or a possible shortcut assumes | What master actually does | Consequence |
|---|---|---|
| Held debt can be cleared by completing a retirement. | `server/Application/Services/SettlementSyncRecoveryService.cs:78` takes the Held arm and only calls `RescheduleHeldAsync`; `:236` updates the clock/revision, guarded by Held state. | F3d intentionally supplies no positive supersession path. |
| Task and attempt identify one registration. | `server/Domain/Entities/AgentTaskSyncDebt.cs:17` stores debt ID, task, attempt and settlement event; `:22` stores paths. `server/Infrastructure/Data/AppDbContext.cs:174` makes `(TaskId, Attempt)` unique, not a registration. | Retiring/recreating a checkout cannot be disambiguated by this key. |
| The retirement's GUID identifies the removed registration. | `server/Domain/Entities/TaskWorktreeRetirement.cs:8` is the release operation ID; `:16` is `ReleasedTaskRevision`. `server/Application/Services/TaskWorktreeRetirementService.cs:150` creates a new release ID and `:157` copies the task concurrency token. | Neither is registration identity; no reinterpretation of either field. |
| A schema-free typed binding field already exists. | All debt properties are listed in `server/Domain/Entities/AgentTaskSyncDebt.cs:15`; retirement properties in `server/Domain/Entities/TaskWorktreeRetirement.cs:6`. The debt mapping at `server/Infrastructure/Data/AppDbContext.cs:160` has no JSON payload. Retirement `HandoffDispositionJson` at `:2900` is an existing handoff array. | Adding persisted registration properties requires a migration. Overloading a reason, path, fingerprint, GUID or handoff payload is not this contract. |
| `AgentTask.WorktreeId` can serve every ordinary delegate. | `server/Domain/Entities/AgentTask.cs:179` has this nullable card-worktree link; `:194` explicitly explains that ordinary task checkouts carry their own coordinates because the card-scoped entity requires a card. Ordinary provisioning at `server/Application/Services/DelegationWorktreeService.cs:345` and `:389` assigns paths/branches. | Do not treat the card entity key as a universal native registration ID. |
| There is no creation identity anywhere. | `server/Infrastructure/Git/WorktreeManager.cs:97` mints `CreationId` in schema-2 metadata before `worktree add`; `:136` marks creation complete; `:159` exposes it through verification creation reads. | A useful producer primitive exists, but it is not yet debt/removal authority. |
| Reading that metadata by path now proves what an old debt owned. | `server/Infrastructure/Git/WorktreeManager.cs:834` selects the metadata file by path and `:842` overwrites it. `server/Application/Dtos/WorktreeInfo.cs:3` omits the creation ID. `DelegationWorktreeService.cs:345` adopts a listing by task identifier/path. | Never backfill a historic debt from the present path's metadata. A stale sidecar is not proof of the current native registration. |
| The settlement captures an identity that the sweep can consume. | `server/Application/Services/AgentTaskReplyService.cs:1151` inserts debt atomically with settlement; the initializer at `:1158` snapshots coordinates, refs and SHAs, but no registration ID. | Bind at the producer before recovery uses it; retain settlement atomicity. |
| Any retirement means complete removal. | `server/Application/Services/TaskWorktreeRetirementService.cs:336` receives component removal results; `:340` records timestamps; `:346` marks Complete only for `IsClean`. Revocation exists at `:186`. | Require identity-bound, committed Complete evidence, not release/admission or an intent. |
| There is one removal writer. | `server/Application/Services/CardWorktreeCleanupExecutor.cs:116` also creates retirement rows. Its publication route at `:204` delegates to landing cleanup. `server/Application/Services/AgentTaskLandingProtocol.cs:384` removes published worktrees and `:392` records component flags and cleanup completion separately. | Retirement-only binding does not solve “after land.” Inventory both receipt families; do not mint a synthetic caller release to cover landing. |
| Current removal guards already compare a registration nonce. | `server/Infrastructure/Git/GuardedWorktreeRemoval.cs:320` compares retirement/task/ref/SHA and paths; there is no registration nonce comparison. | A new column populated from an unchecked task pointer would still be unsafe. Producer and removal attestation both need work. |
| The hourly restamp bounds the attention set. | `server/Application/Services/AttentionService.cs:3214` loads every Held/Pending row without `Take`; `SettlementSyncDebtAttention.cs:64` always warns for Held. | Restamping bounds writes, not retained row count or attention read volume. CARD-1136 item 3 remains open. |
| F3d lost Pending recovery. | `SettlementSyncRecoveryService.cs:111` still applies `RegistrationGoneAsync` only on the Pending path; `:149` and `:271` retain the Pending retirement/absence behavior. | Leave Pending behavior and immutable settlement evidence alone in this card. |
| Existing regression pins can be removed to enable the feature. | `tests/Antiphon.Tests/Application/SettlementSyncRecoveryTests.cs:477` requires Complete-without-ID to remain Held; `:507` covers recreation; `:580` covers four alias/unreadable/moved shapes; `:928` scans the Held closure, and `:1029` rejects retirement reads and Superseded. | Preserve all semantic assertions. A future bound branch needs explicit additional coverage and a reviewed scope adjustment to the structural scanner; hiding calls from it is not acceptable. |
| No exact cost pin exists. | `SettlementSyncRecoveryTests.cs:785` asserts one statement for not-due Held; `:797` asserts five for a due legacy Held restamp. `:546` repeats the five-statement guard for recreation. | Preserve those counts for unbound legacy rows. Budget a bound-row evidence read separately. |
| The schema can be changed silently without a migration. | `tests/Antiphon.Tests/Application/SettlementSyncDebtSchemaTests.cs:28` verifies applied/pending migrations and `:31` asserts no pending model changes. | Keep this assertion. Do not suppress model validation to comply nominally with “no migration.” |

## Decisions

### D-1. Preserve the stated default; request a scope decision through this artifact

No migration means no new persisted binding and no re-enabled Held supersession in
this dispatch. Tests-only hardening is not presented as completion of CARD-1148.
The safe current behavior is already implemented and extensively pinned by F3d.
There is no reason to add redundant tests just to produce a Code slice.

Recommended decision: authorize a **separate additive schema and producer change**,
then TestDesign, then Code. Rejected: silently treating “no migration” as “no migration
in the Plan task only”; pretending the feature can land as a one-line sweep fix;
closing CARD-1136 item 3 after hourly restamping.

### D-2. Use an immutable registration generation, never reconstructed identity

The candidate contract is a server-issued nonempty GUID for one canonical desktop
native registration lifetime. Creation/recreation gets a new GUID, even with the
same task, attempt, path, branch and SHA. A move or in-place recovery of the same
registration retains it only with positive registration evidence. A runner mirror
has separate custody and cannot supply the canonical registration ID.

Build on the existing `CreationId` producer only after proving its ownership at the
native registration, including protection against stale metadata left after manual
remove/add. The proposed witness is a versioned nonce recorded in the registration's
admin directory under the repository mutation lease and bound to the creation
record. New managed creation writes it before exposing the checkout. Missing,
malformed, mismatched, incomplete or inaccessible evidence means **unbound**, never
“use the task ID” or “mint an ID for the directory we found.” TestDesign must qualify
create crash recovery, adoption, moves and stale sidecars before this producer ships.

Rejected: Git admin directory name, path hash, inode/file ID, current branch SHA,
task attempt, concurrency token, retirement operation ID, or a GUID invented when
the sweep notices absence. All either change for unrelated reasons, can be reused,
or were never captured on the original debt.

### D-3. Persist nullable identity snapshots; do not backfill old debt

Conditional schema proposal: nullable `WorktreeRegistrationId` on `AgentTask`,
`AgentTaskSyncDebt`, `TaskWorktreeRetirement`, and `AgentTaskLanding`. The latter
covers publication cleanup, which has its own existing receipt. Generate the
migration with EF CLI, including the model snapshot. Existing rows remain null.
An index on retirement registration identity supports bounded evidence lookup;
retain the existing debt unique key and due index. No cascade or row deletion.

Provisioning persists the validated current identity with the task coordinates.
Settlement copies that known identity into the debt in its existing transaction;
the debt binding never follows subsequent changes to the task pointer. If identity
cannot be established, settlement still succeeds with an **unbound** debt: recovery
must not turn registration uncertainty into a new blocked conversation. Release
and landing preparation snapshot their validated identity before deletion; terminal
evidence may use that identity only after removal revalidates the same generation.

No historic debt binding is filled from task, path, a matching retirement or metadata
observed after settlement. Null and `Guid.Empty` cannot match each other as proof.
Legacy Held attention will remain: new binding stops eligible future accumulation
but does not by itself bound the existing total. Any operator disposition/backfill
is a separately designed feature, not an implicit part of this card.

Rejected: encoding IDs in `ReasonCode`, `EndpointFingerprint`, event `Detail`,
`ProgressBaselineJson`, `VerificationCreationJson`, or `HandoffDispositionJson`;
repurposing settlement/debt primary keys; a filesystem debt ledger. These add new
authority/retention/atomicity semantics to unrelated contracts and do not implement
the card's typed snapshots. A new ledger is a larger persistence design, not a
minimal escape from the no-migration constraint.

### D-4. Removal evidence is exact, durable, and independent of today's path

Retirement must have the same nonempty registration ID and task/attempt as the debt,
be active and Complete, and carry DirectoryRemovedAt, RegistrationRemovedAt and
RetirementCompletedAt at or before the observation time. Completion cannot precede
the debt's creation; timestamps must be internally ordered. No matching row,
ambiguous rows, revoked/inactive/Partial/Refused/Released state, incomplete or future
timestamps, or an evidence-read failure keeps Held. None authorizes a new removal.

The removal producer must attest the nonce **before** unregistering while owning the
repository lease. A crash after unregistering without a durable identity-bound
removal record cannot be repaired by treating absence as success. Use the existing
cleanup intent/journal with explicit identity propagation; if its contract cannot
preserve the witness, expand the additive schema design before Code. This crash
boundary is a TestDesign acceptance gate, not permission to infer success.

Publication cleanup needs equivalent registration-bound evidence: confirmed
publication alone is insufficient; require Complete cleanup, both directory and
registration removal, and its committed completion clock. The existing
`CleanupCompletedAt` is also written for refusals, so never test the timestamp alone.
Do not create fake `TaskWorktreeRetirement` rows for landed owners. Keep the two
receipt adapters separate and give the sweep a single typed removal-proof result.

### D-5. Keep recovery narrow and fail closed

Retain the existing due query, batch of 32, task lock, re-read, revision CAS, hourly
clock and rescue transaction. A null-bound Held debt follows the exact legacy path:
five statements when due; one for an empty or not-due selection. A bound Held row
adds at most one bounded, indexed removal-evidence query per hourly claim; the
query may union the two receipt families, not issue an unbounded scan or per-tick
second sweep. TestDesign must pin its actual SQL and six-statement target.

On exact proof, CAS Held to Superseded with the existing
`settlement_sync_superseded_by_retirement` reason, null NextAttemptAt and one revision
advance. Keep Attempts, SourceSha, ConfirmedSha, SourceReadyAt, task verdict, report,
settlement event, progress evidence, outcomes and caller delivery unchanged. Return
zero attempts. No filesystem, runner call, Git, fast-forward, Ready transition,
caller note or seat operation belongs in this reader. On doubt, restamp Held.

A debt bound to B survives Complete retirement of A at the same path and attempt.
A debt truly bound to A can clear after A's proven removal even if B now uses its
former path. The debt's immutable binding, not the current directory, decides this.
Tests must distinguish these two timelines instead of asserting that any recreation
always vetoes every historic debt.

### D-6. Preserve the existing safety assertions and explain intentional scope

All current F3d scenarios remain unbound and retain their semantic assertions,
including no Git, no attempt increment, retained warning, clock/CAS and exact SQL
counts. Do not mass-update their fixtures to carry a made-up ID. New bound fixtures
must obtain identity through the real producer before creating debt.

The current source scanner forbids *all* Held supersession. A future TestDesign
must split its explicitly inspected legacy and bound branches, retaining the
legacy ban and adding a closed bound call graph that permits only typed DB proof,
never filesystem/Git. Replacing it with a weaker “no Directory.Exists” grep or
moving forbidden calls into an unscanned helper is rejected. This intentional
contract change needs review before Code; this plan changes no assertion.

### D-7. Keep session behavior and attention honest

This design adds no session that waits for input, no timeout and no automatic stop.
CARD-0079 remains the only automatic stop of its specific Working-session shape.
Parking stays default-off. Nothing here releases an input-waiting session, and no
release deadline exists while parking is disabled (CARD-1083). Debt reconciliation
is bookkeeping for an already settled task, not a seat-release mechanism.

Do not add `Take`, TTL, warning suppression, or an operator-clear side effect to
attention. Close CARD-1136 item 3 only with an explicit acceptance statement about
remaining legacy rows; do not promise that new binding makes the whole Held set bounded.

### D-8. Placement and overlap are dispatch-time facts

Read `GET /api/runner-defaults` and `GET /api/session-runners` at 23:27 UTC on
2026-10-07. Defaults revision 2 selects the configured Linux preference with no
kind overrides; the catalogue had an available Windows lane and an available,
accepting Linux lane, with another Linux entry draining. This is an observation,
not a host pin. Refresh both routes before dispatch. All proposed ordinary checks
use the **Linux lane with real Git and isolated PostgreSQL**. Omit `-Runner` and
`-Platform`; use `-Platform Any` only to remove a previous OS pin. Native nonce
portability qualification, if required by TestDesign, gets a separately named
Windows lane rather than pinning all work to one machine.

| In-flight work named by the caller | Shared files / required order |
|---|---|
| CARD-1149/1150 S2, Code `7c5280e8` | Owns `AgentTaskDispatcher.cs` and `SessionReconciliationService.cs`. Do not edit either for a sweep-only fix. Any provisioning integration that requires dispatcher changes follows that landing and refreshes its citations. |
| CARD-1115 S1, Code `c57f1d8a` | Owns `AgentTaskDispatcher.cs` pin guards and `RepairSourceDispatchTests.cs`. A registration capture hook or use of that fixture follows this landing too. Prefer `DelegationWorktreeService` for the writer; never combine pin-guard work here. |
| CARD-1105 audit fix | Owns `scripts/c590-remote.sh` and `RemoteScriptContractTests`. No overlap in this plan; do not change either or commission a rollout from this worktree. |

Shared backend docs (`docs/orchestration-loop.md`, `docs/ops-http.md`,
`docs/session-runtime-invariants.md`) and test helpers are potential later overlaps:
re-read their settled tips before the docs/fixture slice. Read-only inspections
already performed here do not need to wait for a Code landing.

## Conditional implementation slices

These are **planning budgets, 30-60 minutes of authoring each**, excluding their
checkpoint runs. All require a recorded change to D-1 and a completed TestDesign;
none is authorized by the presence of this table. Commit/push each meaningful
slice before verification; maintain a fast-forward-only task branch.

| Slice | Budget | Files and deliverable | Tests | AppHost restart |
|---|---:|---|---|---|
| S1 schema | 45 min | `server/Domain/Entities/AgentTask.cs`, `AgentTaskSyncDebt.cs`, `TaskWorktreeRetirement.cs`, `AgentTaskLanding.cs`; `server/Infrastructure/Data/AppDbContext.cs`; CLI-generated migration/designer/model snapshot. Nullable fields, no backfill or cascade. | `SettlementSyncDebtSchemaTests.C1148_RegistrationBindingRoundTripsAndLegacyRowsStayNull` | Schema deployment required before new readers; server activation accompanies later slices. |
| S2 native identity | 60 min | `server/Infrastructure/Git/WorktreeManager.cs`; `server/Application/Interfaces/IWorktreeManager.cs`; `server/Application/Dtos/WorktreeInfo.cs`; proposed typed registration DTO. Generation nonce, positive reads, crash-safe creation; legacy unknown stays unknown. | proposed `WorktreeRegistrationIdentityTests.C1148_NewRegistrationDiffersAndRecoveryPreservesIdentity`, `C1148_UnknownOrReplacedRegistrationCannotBorrowMetadataIdentity` | Yes when activated. |
| S3 capture | 60 min | `server/Application/Services/DelegationWorktreeService.cs`, `AgentTaskReplyService.cs`; `tests/Antiphon.Tests/TestHelpers/RunnerSettlementWorld.cs`. Carry validated provisioning identity and atomically snapshot debt. `AgentTaskDispatcher.cs` only if required, sequenced after both conflicting tasks above. | `RunnerTaskSettlementTests.C1148_SettlementCopiesRegistrationIdentityAtomically`, `C1148_UnboundSettlementStaysSucceededAndDebtUnbound` | Yes. |
| S4 retirement producer | 60 min | `server/Application/Services/TaskWorktreeRetirementService.cs`, `server/Application/Services/CardWorktreeCleanupExecutor.cs`, `server/Application/Dtos/WorktreeRemovalRequest.cs`, `server/Infrastructure/Git/GuardedWorktreeRemoval.cs`, `server/Infrastructure/Data/WorktreeRemovalEvidence.cs`. Snapshot and revalidate generation; never attest a substituted registration. | `TaskWorktreeRetirementTests.C1148_CompleteRemovalCarriesTheCapturedRegistration`; `CardDoneWorktreeCleanupTests.C1148_DoneRetirementCarriesRegistration` | Yes. |
| S5 removal crash boundary | 60 min | `server/Infrastructure/Data/WorktreeCleanupJournal.cs`, `server/Infrastructure/Data/RetirementCommandJournal.cs`, their `server/Application/Interfaces/` contracts, and `server/Infrastructure/Git/GuardedWorktreeRemoval.cs`. Propagate the ID into recoverable removal evidence or retain unknown; TestDesign finalizes any extra schema before S1, not by improvising another migration during S5. | proposed `HeldDebtRegistrationRemovalTests.C1148_CrashCannotInventRemovalProof` | Yes. |
| S6 publication producer | 60 min | `server/Application/Services/LandOperationFactory.cs`, `AgentTaskLandingProtocol.cs`; cleanup journal/request identity as needed. Bind publication cleanup without manufacturing retirement approval. | proposed `HeldDebtRegistrationRemovalTests.C1148_PublicationAloneDoesNotProveRegistrationRemoval` | Yes. |
| S7 Held consumer | 60 min | `SettlementSyncRecoveryService.cs`; proposed `HeldSettlementSyncRemovalPolicy.cs`/typed DB proof reader; `SettlementSyncRecoveryTests.cs` and proposed `HeldDebtRegistrationBindingTests.cs`. Implement D-4/D-5 only after both producers. | V-8 through V-13 below plus unchanged F3d class. | Yes. |
| S8 docs and final regression | 30 min | Three owner docs above; `RunnerBranchContractDocumentationTests.cs`; this plan's accepted successor. Keep historical F3d evidence as history. | Existing `C1082_settlement_sync_debt_is_documented` gains explicit legacy/bound sentences; final regression selections. | No additional restart. |

Land/activation order: additive schema, producer chain, consumer, docs. Do not enable
a reader before the removal proof can survive the failure boundaries. Server changes
activate through the canonical AppHost restart procedure after landing, with
`GET /api/version` matching the expected landed SHA. This worktree never restarts
the shared stack. No runner deployment is part of canonical desktop registration
binding. Missing/old producers yield null bindings and retain Held.

Existing test files are under `tests/Antiphon.Tests/Application/` except
`WorktreeManagerTests.cs` under `Infrastructure/`. Proposed new files are
`tests/Antiphon.Tests/Infrastructure/WorktreeRegistrationIdentityTests.cs`,
`tests/Antiphon.Tests/Application/HeldDebtRegistrationRemovalTests.cs`, and
`tests/Antiphon.Tests/Application/HeldDebtRegistrationBindingTests.cs`.

## Plan validation

Re-fetched origin after writing the draft; master remained the inspected full SHA.
Checked the 18-row table's column counts, same-slice build reuse, trailing method
wildcards, serial flags and estimate arithmetic with a read-only PowerShell check.
No checkpoint importer build, product build, runtime test or mutation was run by
this Plan task. The conditional tests and removal regressions still require the
TestDesign qualification explicitly stated above.

## Verification design

This is the acceptance design for the **conditional** scope. TestDesign remains
necessary after the D-1 decision, specifically for registration custody and removal
journal crash recovery. It must freeze the final file list and checkpoint manifest
before handing off to Code. Plan-stage validation is source inspection, not test
execution; no passing runtime evidence is claimed.

### Witnesses and negative cases

New method names below are proposed, not present on inspected master. Use real Git,
the normal provisioning/removal producers and isolated Postgres. Seed Held by the
existing advanced-tip route, then bind only through the new producer; pure policy
tests may construct explicit immutable receipts, but cannot replace producer tests.
Each listed method is one result unless its count is explicitly shown.

| V | Test (class-qualified) | Required observable witness |
|---|---|---|
| V-1 | `SettlementSyncDebtSchemaTests.C1148_RegistrationBindingRoundTripsAndLegacyRowsStayNull` | Persist/reload distinct IDs across all accepted model fields; migrated old rows remain null; model matches applied migration; existing unique constraints survive. |
| V-2 | `WorktreeRegistrationIdentityTests.C1148_NewRegistrationDiffersAndRecoveryPreservesIdentity` | Recreate same path/task/attempt with new registration yields B != A; restart/recovery of the same registration keeps A; no ID minted on a read. |
| V-3 | `WorktreeRegistrationIdentityTests.C1148_UnknownOrReplacedRegistrationCannotBorrowMetadataIdentity` | Stale sidecar, malformed/missing nonce, legacy metadata and replaced native registration yield unknown; do not silently adopt a path-derived ID. |
| V-4 | `RunnerTaskSettlementTests.C1148_SettlementCopiesRegistrationIdentityAtomically` | Debt records the captured A with settlement; rollback leaves neither; restart/reply replay does not rewrite it to current B or duplicate the debt. |
| V-5 | `RunnerTaskSettlementTests.C1148_UnboundSettlementStaysSucceededAndDebtUnbound` | Missing identity preserves normal succeeded Pending-debt settlement and caller delivery; no new block or invented ID. |
| V-6 | `TaskWorktreeRetirementTests.C1148_CompleteRemovalCarriesTheCapturedRegistration`; `CardDoneWorktreeCleanupTests.C1148_DoneRetirementCarriesRegistration` | Each real retirement writer carries the observed ID through Complete removal; replacing generation between release and removal cannot produce proof for the original ID. One result per named method. |
| V-7 | `HeldDebtRegistrationRemovalTests.C1148_CrashCannotInventRemovalProof`; `C1148_PublicationAloneDoesNotProveRegistrationRemoval` | Crash before/after removal/receipt save recovers only from retained identity-bound evidence; absent proof stays unknown. Confirmed publication with failed cleanup is insufficient; Complete bound cleanup is sufficient. One result per named method. |
| V-8 | `HeldDebtRegistrationBindingTests.C1148_ExactCompleteRemovalSupersedesOnlyBoundDebt` | Due A + exact Complete A becomes Superseded, null next, warning gone, zero Git/attempts/Ready/caller events; completion exactly at due is allowed. |
| V-9 | `HeldDebtRegistrationBindingTests.C1148_SamePathDifferentRegistrationStaysHeld` | Debt B survives Complete A, even with identical task/attempt/path/SHA and temporary B absence. |
| V-10 | `HeldDebtRegistrationBindingTests.C1148_IncompleteOrInvalidProofStaysHeld` | Missing/null/empty IDs, revoked/inactive/Partial/Released/Refused receipts, missing component clocks, future/retrograde clocks, foreign owner/attempt, duplicate/conflicting evidence each restamp Held. Use named internal cases and assertions; they remain one TUnit result. |
| V-11 | `HeldDebtRegistrationBindingTests.C1148_ProofReadFailureReschedulesWithoutSideEffects` | Inject actual DB proof-read failure; transaction rolls back and rescue CAS restamps; no successful proof fabricated, no Git, warning persists. |
| V-12 | `HeldDebtRegistrationBindingTests.C1148_ConcurrentRevisionCannotSupersedeANewerDebt` | Barrier between proof read and terminal CAS; another writer changes revision/binding/state; stale writer affects zero rows. Two sweep workers cannot write two terminal transitions. |
| V-13 | `HeldDebtRegistrationBindingTests.C1148_BoundProofReadHasHourlyCostAndLeavesSettlementImmutable` | One statement empty/not-due; legacy due remains five; bound due target six; no per-tick second query; task/event/report/progress/outcome/Attempts snapshots byte-for-byte unchanged. |
| R-1 | Existing `SettlementSyncRecoveryTests.C1136_*` methods | Keep all legacy Complete/no-ID, recreated, alias/moved/unreadable, revoked, absence, uncertainty, read-failure and hourly behavior. Existing class roster: 15 methods, 24 expanded results. |
| R-2 | Existing `SettlementSyncRecoveryTests.C1082_*` methods | Pending backoff, exact-source fast-forward, advanced source, episode change, retirement and immutable evidence remain unchanged. Ten expanded results, included in R-1's class run. |
| R-3 | Existing `SettlementSyncDebtSchemaTests.C1082_SchemaMigrationMatchesModel` | No hidden schema drift or weakened uniqueness. |
| R-4 | Existing `RunnerBranchContractDocumentationTests.C1082_settlement_sync_debt_is_documented` | Owner prose explicitly distinguishes null legacy binding from proven removal. |

### Method-scoped negative controls

Every new method must fail a specific outcome assertion under a quick compiling
mutation of the named production assignment/predicate, then pass after restoration.
Baseline/red/restored-green all use only that method with a trailing `*`. No whole
class/suite PCs, no zero-test or fixture/build errors counted as red. These are
designs, not executed controls. New-line locations are identified by symbols;
the accepted Code/TestDesign artifact must record their final line numbers.

| PC | Detector | Production mutation and expected red |
|---|---|---|
| PC-1 | V-1 method | New registration-property EF mapping ignores the debt field: reloaded ID is null. Do not remove migration/model assertions. |
| PC-2 | V-2 method | `WorktreeManager.CreateAsync`, current mint site `:97`: reuse the prior registration ID on actual recreation; B != A fails. Separately rotate on same-registration recovery; stable-A assertion fails. |
| PC-3 | V-3 method | New nonce validation in WorktreeManager accepts a missing/mismatched stamp: unknown result becomes a stale positive ID. |
| PC-4 | V-4 method | Debt initializer at `AgentTaskReplyService.cs:1158`: assign a fresh GUID instead of captured task registration; stored ID differs from A. Atomic rollback arm must also observe no orphan debt. |
| PC-5 | V-5 method | Same initializer: coalesce unknown to `Guid.NewGuid()`; the persisted null assertion fails. |
| PC-6a | V-6 retirement method | `TaskWorktreeRetirementService` release initializer at `:148`: omit the captured ID; completed receipt ID is null. |
| PC-6b | V-6 CardDone method | `CardWorktreeCleanupExecutor` initializer at `:116`: omit the ID; its completed receipt is unbound. |
| PC-7a | V-7 crash method | New guarded-removal/journal recovery predicate treats absence without a bound receipt as complete proof; injected crash falsely clears debt. |
| PC-7b | V-7 publication method | New proof adapter accepts `Publication=Confirmed` without `Cleanup=Complete` and removal flags; refused-cleanup case supplies false proof. |
| PC-8 | V-8 method | New Held proof terminal branch in `ClaimAsync` always reschedules; exact Complete A remains Held. |
| PC-9 | V-9 method | New removal-policy equality gate compares task/attempt only; Complete A incorrectly supersedes debt B. |
| PC-10 | V-10 method | Independently remove each ID/state/clock/owner/ambiguity guard. The specifically named internal case must fail, not just some unrelated case in the method. Restore between mutations. |
| PC-11 | V-11 method | New proof-read catch treats failure as success rather than using the existing rescue behavior at `SettlementSyncRecoveryService.cs:87`; state changes or pacing assertion fails. |
| PC-12 | V-12 method | Remove the new terminal update's revision predicate (current `TerminalAsync` site `:215`); stale writer overwrites the newer row. |
| PC-13 | V-13 method | Add a second proof SELECT or increment Attempts on the bound Held path; exact cost or unchanged Attempts assertion fails. |

No new docs-only test is needed: extend the existing R-4 pin. Its negative control
removes one promised owner sentence, and the exact R-4 method must go red. Preserve
every existing assertion; adding coverage is not permission to weaken another pin.

### Regression selection and documentation

Re-run the full `SettlementSyncRecoveryTests` class once after S7; rerun
`RunnerTaskSettlementTests`, `SettlementSyncDebtSchemaTests`, and
`RunnerBranchContractDocumentationTests` at the final qualified SHA if their files
changed after their earlier checkpoint. TestDesign must also freeze bounded
affected selections in `WorktreeManagerTests`, `DelegationWorktreeTests` (including
its partial file), `TaskWorktreeRetirementTests`, `CardDoneWorktreeCleanupTests`,
and the landing cleanup/journal test classes once S5's exact journal design is set.
This missing removal-journal qualification is why this is **not next: code**.
Do not run whole Unit, a whole namespace, or the full assembly.

Only after activation should S8 replace the current F3d wording. Pin these literal
sentences in the existing C1082 documentation method and its sentence arrays:

| Owner | Proposed sentence |
|---|---|
| `docs/orchestration-loop.md` | A Held debt with no captured registration ID stays Held. A bound Held debt becomes Superseded only from committed Complete removal evidence for that same registration ID; the re-check reads no filesystem and runs no Git. |
| `docs/session-runtime-invariants.md` | Registration-bound debt supersession changes no session, task verdict, settlement evidence, or caller delivery. Legacy unbound Held debt is not backfilled from a path. |
| `docs/ops-http.md` | The hourly Held re-check clears attention only after proven removal of the debt's captured registration; unbound legacy debt continues to warn. |

Keep the current docs true until that implementation lands. Update CARD-1136's
closure claim separately from code publication; a receipt that clears a newly bound
debt does not prove the accumulated legacy attention set is bounded.

### Checkpoint execution contract

The following rows are the candidate manifest for the stated witnesses. They are
not to be run by this Plan task or used as a Code brief until the D-1 decision and
TestDesign have completed the additional native/removal regressions above. One
filter per behavior; literal After tokens; proposed methods all have one result.
Every row is **Linux / Git / isolated PostgreSQL**, including pure checks sharing
the build. `Serial=true` and `TUNIT_MAX_PARALLEL_TESTS=1` prevent Postgres overlap.
Build reuse stays within the same After token. Keep fresh per-row evidence ignored.

Bootstrap the checkpoint tool through `scripts/build-slot.ps1` into a producer-owned
`bin-c1148-driver/`, then invoke its built DLL with `run --plan <accepted-plan> --after S1
--expected-source-sha <committed-sha>` (and similarly for each literal token).
The tool owns row build slots. Wait until its exit is not 75; never end a task with
a run outstanding. Slot timeout 4 is not-run, never permission for an unleased run.
Record actual counts and the tested SHA, including unlisted diagnostics and failures.
Quick PCs are separately declared method-only controls, restored before ordinary
checkpoint qualification. Remove only this task's alternate outputs afterward.
Code/Review run `scripts/check-evidence-diff.ps1` over the full task range.

Candidate ordinary floor is **118 minutes** for the rows below, plus the removal
regressions TestDesign must price, authoring and slot waits. These are estimates,
not measured runtimes. Do not turn missing evidence into a green result.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---:|---:|---|---|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1148-s1/` | binding-schema | `/*/*/SettlementSyncDebtSchemaTests/C1148_RegistrationBindingRoundTripsAndLegacyRowsStayNull*` | V-1 | exact 1, 0 failed/skipped | 1 | 7 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-2 | S1 | CP-1 | existing-schema | `/*/*/SettlementSyncDebtSchemaTests/C1082_SchemaMigrationMatchesModel*` | R-3 | exact 1, 0 failed/skipped | 1 | 3 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-3 | S2 | `tests/Antiphon.Tests -> bin-c1148-s2/` | generation-lifetime | `/*/*/WorktreeRegistrationIdentityTests/C1148_NewRegistrationDiffersAndRecoveryPreservesIdentity*` | V-2 | exact 1, 0 failed/skipped | 1 | 8 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-4 | S2 | CP-3 | unknown-generation | `/*/*/WorktreeRegistrationIdentityTests/C1148_UnknownOrReplacedRegistrationCannotBorrowMetadataIdentity*` | V-3 | exact 1, 0 failed/skipped | 1 | 5 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-5 | S3 | `tests/Antiphon.Tests -> bin-c1148-s3/` | atomic-debt-binding | `/*/*/RunnerTaskSettlementTests/C1148_SettlementCopiesRegistrationIdentityAtomically*` | V-4 | exact 1, 0 failed/skipped | 1 | 8 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-6 | S3 | CP-5 | unbound-settlement | `/*/*/RunnerTaskSettlementTests/C1148_UnboundSettlementStaysSucceededAndDebtUnbound*` | V-5 | exact 1, 0 failed/skipped | 1 | 4 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-7 | S4 | `tests/Antiphon.Tests -> bin-c1148-s4/` | retirement-binding | `/*/*/TaskWorktreeRetirementTests/C1148_CompleteRemovalCarriesTheCapturedRegistration*` | V-6 | exact 1, 0 failed/skipped | 1 | 8 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-8 | S4 | CP-7 | done-retirement-binding | `/*/*/CardDoneWorktreeCleanupTests/C1148_DoneRetirementCarriesRegistration*` | V-6 | exact 1, 0 failed/skipped | 1 | 5 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-9 | S5 | `tests/Antiphon.Tests -> bin-c1148-s5/` | crash-proof | `/*/*/HeldDebtRegistrationRemovalTests/C1148_CrashCannotInventRemovalProof*` | V-7 | exact 1, 0 failed/skipped | 1 | 10 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-10 | S6 | `tests/Antiphon.Tests -> bin-c1148-s6/` | publication-proof | `/*/*/HeldDebtRegistrationRemovalTests/C1148_PublicationAloneDoesNotProveRegistrationRemoval*` | V-7 | exact 1, 0 failed/skipped | 1 | 8 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-11 | S7 | `tests/Antiphon.Tests -> bin-c1148-s7/` | exact-removal | `/*/*/HeldDebtRegistrationBindingTests/C1148_ExactCompleteRemovalSupersedesOnlyBoundDebt*` | V-8 | exact 1, 0 failed/skipped | 1 | 8 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-12 | S7 | CP-11 | different-generation | `/*/*/HeldDebtRegistrationBindingTests/C1148_SamePathDifferentRegistrationStaysHeld*` | V-9 | exact 1, 0 failed/skipped | 1 | 4 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-13 | S7 | CP-11 | invalid-proof | `/*/*/HeldDebtRegistrationBindingTests/C1148_IncompleteOrInvalidProofStaysHeld*` | V-10 | exact 1, 0 failed/skipped | 1 | 5 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-14 | S7 | CP-11 | proof-read-failure | `/*/*/HeldDebtRegistrationBindingTests/C1148_ProofReadFailureReschedulesWithoutSideEffects*` | V-11 | exact 1, 0 failed/skipped | 1 | 4 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-15 | S7 | CP-11 | revision-fence | `/*/*/HeldDebtRegistrationBindingTests/C1148_ConcurrentRevisionCannotSupersedeANewerDebt*` | V-12 | exact 1, 0 failed/skipped | 1 | 5 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-16 | S7 | CP-11 | cost-and-immutability | `/*/*/HeldDebtRegistrationBindingTests/C1148_BoundProofReadHasHourlyCostAndLeavesSettlementImmutable*` | V-13 | exact 1, 0 failed/skipped | 1 | 5 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-17 | S7 | CP-11 | legacy-and-pending-regression | `/*/*/SettlementSyncRecoveryTests/*` | R-1, R-2 | exact 24, 0 failed/skipped | 24 | 14 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-18 | S8 | `tests/Antiphon.Tests -> bin-c1148-s8/` | owner-contract | `/*/*/RunnerBranchContractDocumentationTests/C1082_settlement_sync_debt_is_documented*` | R-4 | exact 1, 0 failed/skipped | 1 | 7 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
