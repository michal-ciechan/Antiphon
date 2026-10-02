# CARD-0883: recover a land adoption interrupted between ref movement and checkout reset

Date: 2026-10-01. Stage: Code on `feat/card-task-0d765df1` from `150fa807b32aa2b720b67bc62646b0ec3f6f0881`, atop master `4380891cca92111cd6271a0bf0f3662965a7440d`. The original Plan baseline was `fccef27eb03f2522f2a40574dc1c764335d4a490`; its line references are historical. Code-start source and checkpoint counts are refreshed below. No rebase, merge, reset, or master update is part of this task.

## Outcome and scope

An explicitly reviewed recovery can finish an interrupted source adoption using a **new land request**, while genuine staged, unstaged, untracked, or obstructing ignored work remains preserved. Persist adoption intent before moving the ref, and eliminate land-request saves between successful `update-ref` and completed checkout reset. Keep recovery of historical half-reset states because process death, cancellation, filesystem failure, and old binaries can still leave them.

Retain exact reviewed-SHA approval, owner/source identity, repository lease, source-workspace admission, remote fingerprint and lease checks, original owner status, independent publication/cleanup verdicts, and the existing `source_dirty` refusal for real edits. Normal schema-3 land remains branch-based; this does not start inspecting/resetting every ordinary owner's checkout.

Add attributable concurrency diagnostics and a deterministic monitor race test. The change does not disable monitoring, retry arbitrary EF writes, introduce automatic force-cleaning, or operate a live checkout during Plan. This stage changes only this Markdown file. Templates inspected: the CARD-0826 daily-host-cleanup and CARD-0849 shared-runner-caches plans; owners: project-context, orchestration-loop, agent-card-lifecycle, testing-and-build, ops-http, logs, and agent-credentials.

## Evidence and limits

### Verified source facts

| Observation | Evidence at the baseline | Consequence |
|---|---|---|
| The failure cut exists exactly as reported | `server/Application/Services/AgentTaskLandSourceResolver.cs:397` records `source-adopt-reset`; `:398` checkpoints it; `:400` runs CAS `update-ref --no-deref <ref> <S> <L>`; `:406` clears the old PID, `:408` rotates the GUID, `:409` saves, and `:410` starts `reset --hard <S>`. | A database conflict at **409** occurs after HEAD moves and before the index/worktree refresh. Earlier durable intent is already available for historical recovery. |
| Fresh requests cannot use the repair | Resolver `:283` inspects IdentityAndStatus; `:284` requires the **current request's** `SourceAdvanceChildOperation == source-adopt-reset` and full `RecoveryLocalBeforeSha`; `:311` otherwise refuses the inspection result. `server/Infrastructure/Git/LandingGit.cs:402` returns `source_dirty` on any nonempty status. | A replacement request with empty recovery-progress fields cannot repair the old request's half-state. |
| Existing repair is too weak as a safety proof | Resolver `:290` checks identity/HEAD, `:294` runs `diff --quiet L`, `:296` lists ordinary untracked paths, and `:301` resets. It has no separate `diff --cached L` assertion or ignored-obstruction check. | Add independent index equality. An index-only staged edit can be hidden by a worktree that still matches L. Apply stronger guards to same-request repair too. |
| Identity includes the sequencer | `LandingGit.cs:379` validates refs; `:385` rejects sequencer state **before** the IdentityOnly return; `:420` checks canonical common directory, unique registration, symbolic branch and HEAD/ref equality. | Reuse these checks; a dirty refusal alone must never authorize a reset. |
| The start callback also saves after a child has started | Resolver `:276` writes child identity and token; `:281` saves. `LandingGit.cs:79` starts the process and `:101` invokes the callback. | Moving only line 409 is insufficient if a request-row save still runs in the reset child's callback. |
| Existing safe checkpoint writer preserves monitoring changes | `AgentTaskLandRequestWriter.cs:143` opens a short transaction, `:145` locks the task, `:146` reloads task/request, `:149` checks staleness/source progress, and `:155` applies a source-only patch. `:228` defines the task lock. | Use this writer before and after Git, without holding a DB transaction across Git. Preserve monitor fields and reject changed authority/source progress. |
| Owned Git already has an independent durable child fence | `LandingGit.cs:75` classifies mutations; `:77` begins `RepositoryChildJournal` for both RunAsync and RunOwnedAsync; `:100` records the child; `:118` drains streams before clearing it. `RepositoryChildJournal.cs:18` writes intent before process start. `RepositoryMutationLease.cs:37` rejects unfinished children. | The adoption pair can use journaled RunAsync without a request-saving callback. This must remain real journaled Git, not an unowned process wrapper. |
| Concurrency is a GUID, not xmin | `server/Domain/Entities/AgentTaskLandRequest.cs:40`; `server/Infrastructure/Data/AppDbContext.cs:1717`. | Keep the GUID concurrency contract. CARD-0719 xmin activation does not explain this request-row mechanism. |
| Failure attribution is currently lost | `AgentTaskLandService.cs:1118` clears the tracker; `:1135` logs the exception with task/request/attempt; `:1169` records a diagnostic ID/code/type. `LandFailureDiagnostic.cs` formats SHA facts, without conflicting entity keys/tokens/writer. | Capture safe exception-entry facts **before** clearing tracking; use a fresh read for observed database facts. |

The incident IDs, `08:43:47Z`, old/new SHAs, 12.5-second duration, and empty old-tree diffs are **caller-supplied historical evidence**, not measurements repeated here. The desktop owner tree, original diagnostic, and live DB were not accessed. The source establishes a reachable race, not which transaction won incident `7277cc62097f408289529e5ffd16ad95`.

### Concurrent-writer census

| Candidate | Actual request-row behavior | Attribution conclusion |
|---|---|---|
| **AgentTaskLandMonitorService.SweepAsync** | `:17` visits every pending request; `:23` locks the task and reloads; `:27` sets LastEvaluatedAt; **`:44-46` rotate the token and commit on every pass**, even with no warning/event. Hosted monitor repeats at `LandSweepSeconds` (`AgentTaskLandMonitorHostedService.cs:21`; default 5 seconds in `DelegationSettings.cs:586`). | Concrete independently scheduled competing writer. Its task lock does not protect the resolver's naked saves at 281/409. This is the leading code-supported explanation; absence of a notification cannot eliminate it. |
| Held-land/retry path in AgentTaskLandService | `HoldAsync :1551-1586` reloads under the task lock, updates hold/notification ownership, rotates the token and commits. Yield does likewise at `:1454-1482`; target-race retry at `:690-701`. | Another possible competing request writer, subject to actual queue/admission scheduling. Distinguish its label from the monitor, rather than inferring it from nearby Held events. |
| AgentTaskLandRequestWriter / source resolver | Checkpoint writer `:156/:182` and direct StartedAsync/409 saves above. | Expected source-progress writes; also label them so a second worker is visible. |
| AgentTaskLandingProtocol | `TransitionAsync :774-788` reloads and updates request progress under the task lock. | Real request writer, normally later in this land's own sequence; overlap needs execution evidence. |
| Merge settlement, AgentTaskReplyService | `ResolveConflictedParentAsync :1988-1991` returns when the current request is a reviewed recovery. `:2004-2011` can supersede an ordinary NeedsResolution request; that branch currently does not rotate the request token. | Nearby Merge settlement is **not evidence that it wrote this recovery row**. Stamp/rotate the ordinary supersession mutation consistently, preserving the recovery guard. |
| Request-status notification / AttentionService | AgentTaskLandNotificationService updates `AgentTaskLandNotification`, not this request row; AttentionService `:1385` reads pending requests AsNoTracking. `MergeHelperOutcome.cs:19` reads the request and creates events/notes. | Do not confuse notification tokens with request tokens. These inspected paths do not directly explain a conflicting request GUID. |
| AgentTaskService, AgentTaskDispatcher, DataRetentionService, LandApproval, LandOperationFactory | Service `:2451`, dispatcher `:1307/:4286`, retention `:409` read/reference requests; approval/factory consume the request as authority/input. | A type reference is not a write. Preserve these readers; there is no demonstrated direct competing token update in these inspected paths. |

Read-only measurement commands used: `git status --short --branch`, `git rev-parse HEAD origin/master`, `git ls-remote origin refs/heads/master refs/heads/feat/card-task-373933b4`, `rg --files`, `rg -n`, `nl -ba`, `sed`, and a PowerShell source-attribute census. No build, test, provider launch, DB update, host change, or incident-tree Git mutation was performed. No historical exception is being presented as freshly reproduced.

### Read-only experiment for the live race

This is an observation protocol for the host that owns the checkout, **not a Plan-time execution or a request to trigger a land**:

1. While an independently authorized recovery is running, select its exact owner/request/attempt and UTC window through the documented task status/timeline routes. Use `scripts/logs.ps1 -Source desktop-server` on the desktop, narrowly selecting that diagnostic/request; do not dump credentials, environment, full EF entries or arbitrary exception payloads.
2. If an approved DB read connection is available, poll only this request's Id, TaskId, ConcurrencyToken, LastEvaluatedAt, LastProgressAt, State, SourceAdvanceChildOperation, RecoveryLocalBeforeSha, ExpectedSourceSha and (after this change) writer stamp. Use short read-only transactions, no row locks, triggers, sleeping SQL, token rotation, tracing reconfiguration, or statistics reset. Pair timestamps with the resolver's structured boundary records. Otherwise record that the public DTO cannot expose the token and that attribution remains incomplete.
3. With `GIT_OPTIONAL_LOCKS=0`, observe the exact registered tree: `symbolic-ref -q HEAD`, `rev-parse HEAD`, `diff --quiet <L> --`, `diff --cached --quiet <L> --`, `ls-files --others --exclude-standard -z`, `ls-files --others --ignored --exclude-standard -z`, `ls-files --unmerged -z`, and sequencer presence in the resolved gitdir. Preserve exit codes; do not print file contents. Never fetch, reset, refresh the index, or recreate a half-state in production.
4. A token transition committed by Monitor while HEAD has moved but reset has not finished demonstrates the window. Match row ID **and token**, not just elapsed time. Polling may miss it; LastEvaluatedAt movement alone is suggestive, not writer proof. The current incident may remain unattributable because the needed historical writer/token record was never captured.
5. Reproduce deterministically only in the isolated test fixture in V-2, with a barrier and a second DbContext running the actual monitor. A read-only live observer cannot force a database conflict; do not disguise a destructive reproduction as measurement.

## Design decisions

### D-1: fresh-request recovery uses durable adoption provenance and exact checkout predicates

Use S for the newly approved expected source and L for a previously checkpointed pre-adoption tip. Enter the repair only for explicit OwnerReviewedSource/AdoptReviewedSource after the existing Review/remote/owner checks, while holding the repository mutation lease and the existing source-workspace exclusion. Do not add this behavior to ordinary land or cleanup-only land.

A new request may find an earlier interrupted request for **the same owner, canonical repository/common directory, registered worktree, source ref, recovery source identity, endpoint fingerprint and S**. Require its persisted `AdvanceStarted`, `source-adopt-reset`, full L, no RecoveryAdoptedAt, and matching request-owned `adopt/local-before` and `adopt/source` pins. Refused/completed interrupted requests remain witnesses; the new request need not have its own child-operation fields or a SupersedesRequestId. Query by owner and S, exclude the new request, and require one unambiguous L/identity among qualifying unfinished intents. Conflicting witnesses refuse `adopt_recovery_ambiguous`; no witness refuses `source_dirty` with bounded detail. Do not scan arbitrary ancestors/reflogs looking for a convenient matching tree. Retain witness request/pins until this recovery is reconciled; no new cleanup policy is included.

Before reset, **all** of these must be true, and every Git/I/O error means refusal rather than an empty result:

1. Fresh IdentityOnly inspection succeeds; HEAD, branch ref and registration all equal S, and repository/path/ref identities still match. Existing sequencer and lease/child-journal fences pass. A prior known live or unknown child prevents repair, including a PID saved on a predecessor request.
2. For a **fresh-request** repair, `merge-base --is-ancestor L S` exits 0. Full commit OIDs and the pins agree. Existing same-request explicitly reviewed rewrites may retain their pinned non-ancestor L behavior; the new independent index/worktree guards still apply. This preserves `C753_InterruptedLocalAdvanceResumesOnlyPinnedOldCheckout` at the baseline, whose fixture deliberately uses a rewritten history.
3. Index equals L: `diff --cached --quiet --no-ext-diff --no-textconv L --` exits 0. Worktree equals L independently: `diff --quiet --no-ext-diff --no-textconv --ignore-submodules=none L --` exits 0. Also prove no unmerged entries. Never substitute a worktree-only diff, `status` alone, or a diff against S.
4. Ordinary untracked **and ignored** listings are successful and empty for this automatic repair. This conservative first version refuses ignored output/report residue rather than guessing whether `reset --hard` can overwrite it. No `git clean`, stash, index refresh, checkout-index, or file deletion is a repair probe.
5. Refuse sparse/skip-worktree/assume-unchanged entries, submodules/gitlinks and custom clean/smudge filters for automatic repair, because normal Git diff can hide content there. The inspector must check the actual index flags and attribute/config inputs, not infer their absence from status. In addition to the two Git diffs, compare actual tracked-file bytes with L's expected checkout bytes under **built-in LF/CRLF conversion only**, using byte-safe reads/digests rather than UTF-8 command-output strings. This catches raw changes that a clean conversion could hide. Check symlink targets without following them; unsupported encodings/attributes, unreadable links and uncertain mode evidence refuse. Never invoke an arbitrary external filter. If exact equality cannot be established, retain `source_dirty` with `recovery_checkout_unproven` detail. Built-in CRLF is a required supported case; other transforms fail closed.
6. Immediately before reset, recheck current request/owner authority, reviewed evidence, remote source/fingerprint, identity, L/S pins and the index/worktree predicates. Maintain the existing source-workspace exclusion throughout; changed HEAD refuses `adopt_local_changed`. Changed tracked/untracked content refuses `source_dirty`. No DB transaction is held while doing Git. After reset require IdentityAndStatus clean at S before acknowledging adoption or starting publication.

Copy old L into the new request's existing RecoveryLocalBeforeSha and record the prior request ID in a new nullable `RecoveryWitnessRequestId`. Carry it through the source checkpoint baseline/patch and the attributable SourceAdopted event detail, then checkpoint the new intent before reset. Do not overload SupersedesRequestId: a completed failed request supplies recovery evidence without being a pending request superseded by admission. The witness field is added in the same additive migration as D-4, has no cascading deletion, and is null for legacy/unrelated requests. Never resurrect or edit the old terminal request. A successful repeat observes clean S and continues without resetting again. The public `source_dirty` code stays; bounded details distinguish missing proof from actual content differences without publishing paths or content.

Equality with an arbitrary ancestor cannot distinguish an interrupted reset from deliberate staged reversion. The unresolved intent and pins are therefore required provenance. A manual editor that bypasses workspace exclusion cannot be made atomic with Git by a database lock; the implementation must fail closed on any detected change and must not claim the repository lease is an OS filesystem lock.

Rejected: unconditional reset when HEAD == S; accepting any historical matching ancestor; copying old progress fields without revalidation; repairing only the same request; dropping the source-dirty check; automatically deleting ignored files. A generic forensic repair without durable adoption evidence is outside this card.

### D-2: persist before movement; no request-row write inside the adoption pair

Use the existing task-locked/reload/source-patch writer to persist L/S, pins, `AdvanceStarted`, and `source-adopt-reset` **before** the CAS. Fold the token save currently at 409 into that pre-mutation checkpoint. A conflict/refusal there leaves HEAD/index/worktree at L and launches no mutating source child.

For this adoption pair only, run CAS update-ref and reset via journaled `ILandingGit.RunAsync`; do not supply `StartedAsync`, and do not save the request between them. `LandingGit` already maintains a standing child journal independent of the request for these commands. Preserve CAS's exact expected-old L argument. Recheck the D-1 checkout proof at the reset boundary; the only permitted intermediate checkout delta is the known HEAD movement L -> S. After successful reset/inspection, clear intent and checkpoint through the writer, which reloads and preserves any monitor/hold changes. An authority/source-progress change is still a refusal, never a blanket reload-and-overwrite retry.

The old request PID columns remain readable for legacy resumes; no schema reinterpretation or invented dead-PID observation. New adoption uses the existing common-directory journal as child custody; unknown/live journal entries still fence acquisition after worker death. Other owned operations, including source push, retain their existing custody contract.

This removes **database-conflict-induced** half-reset at the reported cut. It does not make Git ref/index/worktree changes a transaction: crash, cancellation or reset failure can still interrupt the pair, hence D-1. Preserve the durable intent until checkout alignment is proved, including on terminal failure. Do not clear it in generic failure settlement.

Rejected: an EF transaction pretending to atomically include Git; holding a task row lock across Git; merely moving 409 while leaving the post-start request-saving callback; blind one-time token retry; disabling the monitor; catching the exception and unconditionally resetting in a finally block. Monitor progress and genuine source/authority conflicts must remain observable.

### D-3: retain an exact fault seam at the historical save boundary

Extend the instance-scoped `LandDeliveryBoundary` observation pattern into the resolver. Name the cut `source-adopt-ref-moved-before-reset`, located immediately before baseline line 409, after verified successful update-ref and before any reset. No environment-enabled production fault switch or static callback.

First write the red test against the current ordering: a SaveChangesInterceptor, armed only for the fixture request at this cut, throws **DbUpdateConcurrencyException with the request entry** on that save. Assert the cut fired once, first request terminal code is `landing_concurrency_conflict`, HEAD == S, both trees == L, and no reset/target push occurred. Submit a new request with a different ID, fresh scoped services/DbContext and valid Review evidence; assert it completes recovery and confirms target publication. Baseline fails on that final assertion (`source_dirty`).

When D-2 removes that production save, retain the boundary and transfer the historical-fault injection to the test boundary at the **same post-ref/pre-reset location**. The test still constructs the same typed concurrency exception with the request entry and exercises real failure settlement and fresh admission; it must assert injection occurred and the half-state was actually observed. Do not leave an interceptor waiting for a save that no longer exists. Keep a separate test injecting the real reordered pre-intent SaveChanges to prove no source mutation before durable intent, plus a post-reset save failure test. TestDesign must document these two injection mechanisms explicitly; a Git failure, generic exception, dead callback, or later protocol save is not the 409 regression.

Rejected: only seeding a dirty checkout; only throwing somewhere in the resolver; retrying the same request; swallowing the first exception; a test that passes because the fault never fired. A seeded legacy fixture supplements the exact-cut test, not replaces it.

### D-4: safe entity/token diagnostics and transaction-linked writer provenance

Add nullable `LastWriterOperation` (bounded code-owned label), `LastWriterToken` (GUID), and `LastWriterAt` (UTC) to AgentTaskLandRequest, through a CLI-generated additive migration. Existing rows remain null. Stamp them together with the new ConcurrencyToken in the **same row update**, using the injected TimeProvider and a small concrete `LandRequestWriteProvenance` helper. Name actual writers: admission/start/hold/yield/terminal/race-retry in LandService, source checkpoint/operation-attach in RequestWriter, resolver's remaining owned callbacks, protocol progress, monitor sweep, and ordinary Merge supersession in ReplyService. Audit each changed request assignment, not just existing Guid.NewGuid calls. Do not tag notification-row changes as request writes.

On DbUpdateConcurrencyException, capture entry metadata before ChangeTracker.Clear: entity type, allow-listed GUID primary key, original token and attempted token. Then use fresh no-tracking DB values for observed token and writer stamp; accept the stamp only when `LastWriterToken == observed ConcurrencyToken`. Otherwise writer is `unknown` (old binary/uninstrumented writer). An unavailable/deleted row is explicit, not an exception that hides the original failure. Bound the entry list and modified-property **names**; never dump entry values, connection strings, SQL parameters, exception messages, worktree files or endpoints. Synthetic exceptions with no entries remain supported.

Persist the safe summary into the existing terminal event/notification detail, and log structured fields under the existing FailureDiagnosticId. Include failing phase and request/owner/attempt. Writer labels and entity names come from code-owned allowlists. Use the same summary in direct FailAsync and hosted-drain handling. When there have been multiple intervening writes, call the label `observedDatabaseWriter`, not falsely the original winning transaction. Token-linked committed-write log records (row ID, from/to token, label, UTC, request/attempt) permit exact sequence reconstruction when available. Emit “committed” only after transaction commit; rolled-back saves cannot count as a writer receipt.

No evidence attributes the old incident definitively; the delivered diagnostic makes the **next** occurrence attributable. Preserve C498's redaction and idempotent terminal behavior. The model's concurrency strategy stays GUID-based, with no xmin conversion and no automatic retry policy.

Rejected: naming the monitor as a proven historical culprit; timing-only inference from Merge completion; logging the entire exception-entry graph; a volatile last-writer dictionary; labels persisted independently of the token; schema defaults that invent historical provenance.

### D-5: preserve approval and serialize shared source owners

**CARD-0835 landed before this Code start.** Its source-clean Review gate remains required and is seeded in the fixture. Do not relax LandApproval or bypass the gate. No rebase/merge is authorized.

Other collisions are explicit below. Prefer server2 for all authoring, Linux checkpoints and isolated PostgreSQL/Git fixtures. Scope Windows work to the native checkout row, not a second full Code task or full-suite run.

Rejected: concurrent Code on the same land/model files; hiding overlap behind different new test filenames; delaying this Plan for an unrelated Grok fixture; editing CARD-0835's checkpoint script/tool to work around its owner guard.

## Exact implementation footprint and slices

Braced lists enumerate exact files. Only the migration timestamp is deferred to the EF CLI. New helpers are concrete; external Git inspection belongs on the existing ILandingGit seam. No edits to generated docs/cards files.

| Slice | Exact footprint | Test-first order |
|---|---|---|
| S1: witness and checkout proof | `server/Application/Services/AgentTaskLandSourceResolver.cs`; **new** `server/Application/Services/LandRecoveryWitness.cs`; `server/Application/Interfaces/ILandingGit.cs`; **new** `server/Application/Dtos/LandRecoveryCheckoutInspection.cs`; `server/Infrastructure/Git/LandingGit.cs`; **new** `tests/Antiphon.Tests/Application/AgentTaskLandHalfResetTests.cs`; **new** `tests/Antiphon.Tests/Infrastructure/LandRecoveryCheckoutTests.cs`; **new** `tests/Antiphon.Tests/TestHelpers/LandHalfResetFixture.cs`; `tests/Antiphon.Tests/TestHelpers/LandingSafetyHarness.cs`; `tests/Antiphon.Tests/Application/AgentTaskLandAdoptionTests.cs` (interrupted-state pin/clean-Review seed setup only) | Write fresh-ID success, staged-only/unstaged dirt, witness and inspection refusals before implementation. The fixture uses real local Git and isolated schemas, not production paths. |
| S2: ordering and exact historical cut | Resolver above; `server/Application/Services/AgentTaskLandRequestWriter.cs`; consume the existing `LandDeliveryBoundary.cs` seam without modifying its signature; **new** `tests/Antiphon.Tests/Application/AgentTaskLandAdoptionConcurrencyTests.cs`; fixture/harness above | Arm baseline save-409 fault first, then implement fresh recovery and move the save/callbacks. Preserve same-request rewritten recovery. Prove actual monitor writes between barriers, then clean alignment and publication. |
| S3: writer attribution and additive schema | `server/Domain/Entities/AgentTaskLandRequest.cs` (writer stamp and RecoveryWitnessRequestId); **new** `server/Application/Services/LandRequestWriteProvenance.cs`; `server/Application/Services/{AgentTaskLandService,AgentTaskLandMonitorService,AgentTaskLandRequestWriter,AgentTaskLandSourceResolver,AgentTaskLandingProtocol,AgentTaskReplyService,LandFailureDiagnostic}.cs`; `server/Infrastructure/Data/AppDbContext.cs`; generated `server/Migrations/<timestamp>_AddLandRequestWriterProvenance.cs`, matching `.Designer.cs`, `server/Migrations/AppDbContextModelSnapshot.cs`; **new** `tests/Antiphon.Tests/Application/LandRequestWriteDiagnosticTests.cs` | Write two-context token/writer, unavailable-entry and redaction assertions, plus actual predecessor-migration upgrade, before stamping/formatting. S1 may introduce the witness property/patch shape with its tests; S3 closes the shared additive migration before any final checkpoint. No production notifier, AttentionService or retention redesign. |
| S4: native validation and owner guidance | **new** `tests/Antiphon.Tests/Application/AgentTaskLandHalfResetWindowsTests.cs`; fixture above; `docs/orchestration-loop.md` reviewed recovery/refusal paragraphs; `docs/logs.md` concurrency diagnostic lookup; this plan | Add native Windows index/path/EOL cases. Document bounded automatic repair, remaining source_dirty cases and observation recipe. Refresh census after CARD-0835, commit all test/implementation source before checkpoints. |

Scope: `server/Application/Services/AgentTaskLand*.cs,server/Application/Services/LandRecoveryWitness.cs,server/Application/Services/LandRequestWriteProvenance.cs,server/Application/Services/LandFailureDiagnostic.cs,server/Application/Services/AgentTaskReplyService.cs,server/Application/Interfaces/ILandingGit.cs,server/Application/Dtos/LandRecoveryCheckoutInspection.cs,server/Infrastructure/Git/LandingGit.cs,server/Domain/Entities/AgentTaskLandRequest.cs,server/Infrastructure/Data/AppDbContext.cs,server/Migrations/**,tests/Antiphon.Tests/Application/AgentTaskLandHalfReset*.cs,tests/Antiphon.Tests/Application/AgentTaskLandAdoptionConcurrencyTests.cs,tests/Antiphon.Tests/Application/AgentTaskLandAdoptionTests.cs,tests/Antiphon.Tests/Application/LandRequestWriteDiagnosticTests.cs,tests/Antiphon.Tests/Infrastructure/LandRecoveryCheckoutTests.cs,tests/Antiphon.Tests/TestHelpers/LandHalfResetFixture.cs,tests/Antiphon.Tests/TestHelpers/LandingSafetyHarness.cs,docs/orchestration-loop.md,docs/logs.md,docs/superpowers/plans/2026-10-01-card-0883-land-half-reset-recovery-plan.md`

The migration glob reserves collision detection, not permission to edit unrelated migrations. ILandingGit test doubles may need the new inspection member: use a default fail-closed interface implementation for doubles that never enter recovery; extend only LandHalfResetFixture's recording adapter for the new tests. Any additional file needed for a real caller must be named in a committed footprint amendment before editing.

| Other card | Exact overlap with this footprint | Code ordering |
|---|---|---|
| CARD-0835 | AgentTaskLandingProtocol.cs, AgentTaskReplyService.cs, AppDbContext.cs, AppDbContextModelSnapshot.cs/migration sequence; shared land/Review behavior and approval seed helpers. LandApproval is consumed unchanged here. | Mandatory prior land and source/fixture recount. No checkpoint-tool, delegate.ps1 or testing-and-build.md edit by this card. |
| CARD-0778 | None: no Grok readiness source, Agents test directory or Grok fixture changes. | No dependency; host slots still apply. |
| CARD-0788 | AgentTaskLandService.cs, AgentTaskLandSourceResolver.cs, AgentTaskReplyService.cs, docs/orchestration-loop.md. | Serialize if admitted; whichever Code task owns these paths first lands first. No assumption that its held state will persist. |
| CARD-0505 | AppDbContext.cs, model snapshot/migration sequence, docs/orchestration-loop.md. | Serialize schema/doc ownership; no functional dependency on new dispatch settings. |
| CARD-0822 | AppDbContext.cs, snapshot/migration sequence, docs/orchestration-loop.md. | Same serialization; no functional dependency on instruction generation. |
| CARD-0826 | AgentTaskLandService.cs (its maintenance entry/exit), AppDbContext.cs and snapshot/migrations. | Serialize the shared files; preserve its lock-free worktree inventory policy. No dependency on daily cleanup to repair a checkout. |

These are source-footprint comparisons against the supplied plans, not live occupancy measurements. Before Code dispatch the orchestrator must reread effective pipeline/runner/default/host occupancy and actual in-flight scopes per orchestration-loop; defer on collision. Do not broaden antiphon.areas.json.

## Verification design

### Inspection

**Frozen by TestDesign, 2026-10-01, source `4cdd8809b85ed479a8a2b3d43f80e3cd7577bf6e`, branch `feat/card-task-0db701ec`.** Live `card.ps1 get CARD-0883 -Board Antiphon` confirms the interrupted-adoption/fresh-request/real-edit/attribution scope. This is design and source census: **0 builds, 0 test executions, 0 live reproductions**. New names below are specifications, not discovered tests. This checkout already includes CARD-0835's `ReviewedSourceClean == true` gate and adoption seed. Preserve both. Final Review uses the supplied regression-only rule: existing-behavior regressions and newly reachable fail-open behavior fail; unrelated improvements are not acceptance work.

| Bodies inspected at the TestDesign SHA | Boundary and coverage |
|---|---|
| `AgentTaskLandSourceResolver.cs:225-452`, `AgentTaskLandRequestWriter.cs:1-255`, `LandApproval.cs:68-115` | Recovery authority, pins, post-ref save, source patch/reload: V-1/V-2, R-1/R-2. |
| `AgentTaskLandService.cs:110-155,368-550,1115-1187,1487-1587`; `AgentTaskLandMonitorService.cs:15-47`; `LandDeliveryBoundary.cs` | Admission, actual hold/monitor writers, pre-clear failure capture: V-2/V-4. |
| `LandingGit.cs:350-452`; R-1..R-5 test bodies | Identity/sequencer/cache behavior and current result expansion: V-3/V-5, R-1..R-5. |
| `LandingSafetyHarness` construction/admission/execution/restart/interceptors; `LandingGitFixture` command hooks/disposal | Real Git and fresh persistence observers. Existing `InjectedSaveFailure` is not a concurrency exception; add the typed request-specific interceptor in the new fixture. |
| `TestDbFixture` and lifecycle; `AgentTaskLandReceiptTests.C467_V12_CatchUpAndRecoverReceiptWithoutRetyping`; `BridgeQueueHarness.CreateAsync`/`OnSubmitted`; notifier boundary calls | Owned cloned DB, real queue with fake adapter, recovery at persistence boundaries: V-4. |

**Code-start serialization refresh:** CARD-0835 and CARD-0788 are landed at `4380891c`; CARD-0826 is not landed and supplies no migration predecessor. This branch adds its migration after `20261001052023_AddReviewSourceClean`. Keep later shared-file work serialized. The prior planned CARD-0826 → CARD-0788 sequence was a forecast, not the source history used for this Code task.

### Delivery inventory

| Producer → destination | Durable boundary / recovery | Observable receipt and test |
|---|---|---|
| Failure settlement → request, terminal event, outcome notification | Request ID + attempt + diagnostic ID + terminal event ID; one transaction. R-3 retains before-save/after-save/commit/after-commit recovery. | V-2 checks first refusal and fresh-ID publication; V-4 checks the same safe summary in event and notification. ReplyTo=None is NotRequired, not delivered. |
| Changed diagnostic payload → existing session route | Notification ID/digest → keyed queue ID → session/sequence. Recover enqueue failure, insert rollback, committed queue before wakeup, receipt-save failure. | `C883_DiagnosticReachesCaller`: actual failure producer/notifier/queue, eligible and busy recipients, all four cuts; complete matching UserPrompt in the correct session above baseline and one confirmed receipt. Sent/enqueue/event alone cannot pass. |

The fake bridge adapter supplies transcript evidence; it proves application routing/reconciliation, not provider/PTY transport. Seed the session in the same owned DB and owner's ReplyTo/ParentSessionId before admission. Never manufacture the diagnostic notification. Use `HarnessOptions.ConfigureDeliveryVerification` to set evidence/post-submit/confirmation/re-enter/grace budgets to at least 2 seconds (5 preferred): helper defaults include one-second values. Keep the queue on real/offset time; only monitor/provenance time is frozen. No live caller receives traffic.

### Fixtures, delivery boundary and deterministic time

Use LandingSafetyHarness/LandingGitFixture for real Git in an owned temporary local repository with a local bare origin, an isolated PostgreSQL schema, a controlled verifier and ReplyTo=None. LandHalfResetFixture supplies the reviewed owner/source, pinned L/S intent, reset barrier and optional interceptor. No real provider, network Git remote, application restart, production runner, or production database. Add the assembly-local `[ParallelLimiter<ProcessSpawnLimit>]` to every new class that starts Git; serialize these rows and set `TUNIT_MAX_PARALLEL_TESTS=1`. Do not co-schedule Antiphon.Agents.Pty.Tests.

Here “isolated schema” means `TestDbFixture.CreateIsolatedSchemaAsync`'s current **owned cloned database**, not a SearchPath schema. Scope assertions to fixture identities; global sweeps run only on that clone. Migration coverage uses the shared testcontainer and an owned empty database at the actual immediate predecessor (`20261001052023_AddReviewSourceClean`), inserts predecessor-compatible legacy rows, then upgrades through the CLI-generated migration. Never downgrade shared/template stores or use EnsureCreated as upgrade evidence.

Missing setup to implement inside the named fixture/harness footprint: a fixture-specific interceptor (not the existing generic SaveFault), an instance ILandingGit override used by both harness DI and CreateLand, and a scoped service factory for same-context RunRequestAsync/FailRequestAsync. `LandingSafetyHarness.RunQueuedAsync` propagates a DbUpdateConcurrencyException; it does not settle it. The exact-cut driver therefore catches that specific observed exception and calls the **same scoped service's** FailRequestAsync, as the actual hosted drain does, then releases the queue claim. It must assert the caught exception and persisted terminal result. V-4 also drives the real hosted drain. Do not add a broad production catch merely to make the fixture pass.

Use a subclass of `LandingGitFixture.FixtureGit` in the new fixture. Its existing protected `StartProcess` override observes a durable `common/antiphon/children` intent **before** calling the base real Git start; wrapper BeforeCommand is too early to observe the journal. Do not use the static journal callback, race a short-lived PID, or launch a fake provider. New S1-red seam plumbing includes the resolver's caller in `AgentTaskLandService.cs`. **S3 footprint amendment:** add `server/Infrastructure/Orchestration/AgentTaskLandHostedService.cs` to the existing footprint/scope for bounded failure logging only: it currently logs the raw exception again after FailRequestAsync. V-4 records both service and hosted loggers, so fixing only LandFailureDiagnostic cannot satisfy D-4's safe diagnostic contract. No queue, runtime or BridgeQueueHarness source change is commissioned.

Freeze two real-Git shapes with synthetic OIDs, never live incident objects/checkouts:

1. **CARD-0801:** create L and reviewed descendant S in a separate linked tree, push to the local bare remote, seed clean Final/Full Review and run real admission to the post-ref/pre-reset cut. Assert HEAD/ref=S, `write-tree == L^{tree}`, both diffs against L exit 0 and no untracked/ignored paths. First request refuses concurrency; fresh admitted ID repairs/publishes, preserving old request/pins.
2. **CARD-0835:** L has 51 files modified in S; S also adds 13 tracked files absent in L. Move the checked-out ref L→S without reset: exactly **51 staged M + 13 staged D**, not the reversed shape; 64 records with blank worktree column, no untracked/ignored paths, index tree L and raw checkout bytes L. `8331a9cf`/`1d994aac` are historical labels, not fixture OIDs. First terminal payload: Unconfirmed/NotStarted and `local/remote/candidate=null`; recovery-specific L/S progress stays durable. Fresh ID succeeds without a fixture-issued reset.

Use 0801 in `Save409FaultThenFreshRequestCompletes`, 0835 in `HistoricalMonitorSaveConflictThenFreshRequestCompletes`; V-1's success arguments reuse these shapes. Stage blob E, then restore only worktree bytes to L for the index-only negative: worktree diff empty, cached diff nonempty, preserved E blob/mode. Within each staged-edit result also assert a separate ordinary staged edit (index/worktree both E). Obstruction sentinels occupy a path S tracks, so reset would overwrite them. Every refusal checks raw bytes/link targets, index entries/blob IDs, refs/registration/markers, and zero reset/clean/publication.

The first request must traverse real request admission, resolver, failure settlement and persisted terminal event. The second request must traverse real admission with a different ID and newly constructed scoped services, not reuse tracked objects or manually set its recovery-progress fields. Snapshot index/worktree content before publication cleanup can remove the owned test tree. Assert both the intermediate repair and confirmed remote target containment; Directory.Exists becoming false is not a publication assertion. Preserve/inspect the old failed request and pins independently of the new request's outcome.

Most tests use ReplyTo=None and inspect exactly one terminal event/notification correlated by request ID and FailureDiagnosticId. V-4's explicit session arguments additionally exercise the real queue through the fake bridge. Diagnostic text agrees in event, immutable notification and complete prompt. No live session receipt is claimed. Changed routing would require a manifest amendment.

Use a FakeTimeProvider only for monitor age/provenance timestamps and short, explicitly advanced service operations. Barriers complete by explicit TaskCompletionSource release and a second DbContext commit; their deadlines are failure guards, never expected outcomes. Do not inject a frozen clock into SessionMessageQueueService or a whole hosted server. No sleeps, 12.5-second scheduling assumptions, or 5-second monitor margins. The monitor race calls SweepAsync once at the barrier; its clock can stay below the warning threshold to prove that a token write needs no notification.

### V-1: fresh-request recovery and preservation

New `AgentTaskLandHalfResetTests`. This **frozen executable roster** is in test-first order within S1; write its assertions before repair implementation. Each listed argument is a separate `[Arguments]` result. An explicitly required secondary assertion/setup inside a result does not increase Min.

| Named method (prefix `C883_`) | Explicit arguments / executions | Required outcome |
|---|---|---|
| FreshRequestRepairsPinnedAncestor | OwnerReviewedSource, AdoptReviewedSource / 2 | Fresh ID has no initial progress fields; it finds the prior witness, aligns index/worktree to S, records L/witness, and confirms publication. Source task's separate checkout survives adoption. |
| FreshRequestRequiresInterruptedWitness | absent, not-started, wrong-operation, already-adopted / 4 | One witness eligibility predicate is false, all others valid. Even matching ancestor trees cannot authorize repair; source_dirty, zero reset, unchanged bytes. |
| FreshRequestRejectsWitnessIdentity | owner, ref, worktree, sha, fingerprint, common-directory, source-identity, local-pin, source-pin, ambiguous / 10 | Change only the named witness binding. No reset for first nine; conflicting otherwise-valid L witnesses give adopt_recovery_ambiguous. Include an equivalent duplicate witness as a success subassertion: ambiguity is conflicting provenance, not simply row count. |
| UnstagedEditSurvivesFreshAndSameRequest | fresh, same / 2 | Alter a tracked file after half-reset; source_dirty, exact edit bytes/index remain, target unchanged. |
| StagedEditSurvivesFreshAndSameRequest | fresh, same / 2 | Stage a new blob, then restore only the worktree to L; source_dirty and that staged blob remains. This specifically defeats the baseline worktree-only diff. |
| UntrackedAndIgnoredArePreserved | untracked, ignored / 2 | Obstruct an S path with owned sentinel bytes; no reset or cleanup, sentinel unchanged. |
| FreshRequestRequiresAncestor | none / 1 | Valid old intent with non-ancestor L cannot authorize a fresh-request repair; source_dirty/recovery_checkout_unproven, no mutation. Same-request compatibility is separately covered by existing C753. |
| IdentityChangesBeforeResetRefuse | head, branch, registration / 3 | Change the identity at the last barrier; no reset, changed identity preserved, adopt_local_changed or existing precise identity refusal. |
| AuthorityChangesBeforeResetRefuse | review-sha, review-clean-false, review-clean-null, review-superseded, remote, fingerprint, owner / 7 | At final boundary change only the named field/evidence/remote endpoint, or make owner Working. Evidence/remote changes retain their precise refusal; a Working owner makes the task-locked source writer return StaleRequest, with the request pending for the sweep rather than writing under revoked authority. Every argument proves zero reset/publication and preserves the changed fact. E≠S is another valid commit, not malformed input. Keep initial evidence valid so the last authority check is exercised. |
| UncertainPriorChildRefuses | live-request, unknown-request, live-journal, unknown-journal / 4 | Predecessor PID/start-time evidence and repository journal are independent fences. Fake child liveness; owned durable journal records; no real process kill, no reset. |
| ContentChangesBeforeResetRefuse | index, tracked, untracked, ignored, local-pin, source-pin / 6 | First inspection passes; one-shot final-boundary callback changes just this fact. Refuse source_dirty or pin-proof refusal before reset/publication; changed content/pin remains. |
| PostResetMismatchCannotPublish | dirty, wrong-head / 2 | Real reset succeeds, then command-return hook changes bytes or HEAD. No adopted acknowledgement, no target push, intent retained, changed state preserved. |
| CurrentPendingRequestCannotBeStolen | none / 1 | New admission while the first request remains pending returns existing conflict; old identity/progress stays authoritative. |
| CompletedResetIsIdempotent | none / 1 | Interrupt after alignment but before acknowledgement, then fresh admission after terminal failure; clean S is accepted without another reset; one publication and preserved old refusal. |

Frozen V-1: 14 methods, `2+4+10+2+2+2+1+3+7+4+6+2+1+1 = 47` results on either OS. Ordinary/cleanup-only land must never enter recovery inspection: assert that as a compatibility subcase in CompletedResetIsIdempotent, with ordinary dirty-worktree publication retaining guarded cleanup residue.

### V-2: save ordering, exact injected cut and competing writes

New `AgentTaskLandAdoptionConcurrencyTests`, seven nonparameterized methods:

| Named method (prefix `C883_`) | Observable assertions |
|---|---|
| Save409FaultThenFreshRequestCompletes | D-3's exact-cut interception fires once. First terminal code landing_concurrency_conflict; HEAD=S/index=L/worktree=L; zero reset/target push. A **new** request completes alignment and publication. This is CP-1's intentional baseline red and CP-2's repaired green. |
| PreIntentConflictLeavesCheckoutUnmoved | Interceptor throws at the real reordered pre-mutation save. Assert failure code, HEAD/index/worktree all L, zero CAS/reset. After failure settles, a fresh request publishes. |
| MonitorBetweenMoveAndResetDoesNotPreventReset | Pause at the exact historical cut, run real Monitor.SweepAsync in a second context, prove its token committed, release. New ordering finishes reset and preserves LastEvaluatedAt/monitor fields through the final source checkpoint; no LandRefused. |
| HeldWriterBetweenMoveAndResetDoesNotLoseHoldFacts | Pause A with the real repository lease after CAS. In another scope call public RunRequestAsync(owner, same request ID, stored filter): it reaches TryAcquireAsync→null→HoldOnBusyLeaseAsync→HoldAsync before any new admission, and commits Held/token/episode. No RequestAsync or queue dequeue is used for B. Release A; its post-reset source checkpoint retains those facts until a normal later state transition clears them. Assert B returned Held and only A reset. |
| PostResetSaveConflictFreshRequestCompletes | Throw on the first acknowledgement save after verified reset. First request fails with both trees at S; after terminal settlement a fresh request completes with no reset replay. |
| RestartAfterMoveResumesPendingBeforeFreshAdmission | A controlled shutdown cut leaves intent and pending request. Dispose/rebuild scoped services after the child is joined; a new admission is refused while pending, then resume the original request safely. No real server process is stopped. |
| HistoricalMonitorSaveConflictThenFreshRequestCompletes | At the post-ref/pre-reset boundary run the real monitor and, in a **test-only legacy replay callback**, attempt the old naked SaveChanges with its stale tracked request. Require an actual EF concurrency exception, changed database token with monitor provenance, first terminal diagnostic, then a fresh-ID successful land. Do not add a production save back for this test. |

The held-writer interleaving is source-reachable: RunRequestAsync has no active-queue rejection; the repository-lease refusal is before RunLeasedAsync's admission. RequestAsync's land_running guard remains intact. This is a competing drain/replay service-call fixture, not proof a second hosted worker caused either historical incident. Use the real shared lease provider and separate contexts; do not inject an impossible successful second lease.

**Exact-cut transition:** S1-red adds only the instance boundary, fixture and Save409 test. At `source-adopt-ref-moved-before-reset`, arm a request-ID-specific SaveChangesInterceptor for the next request update and throw DbUpdateConcurrencyException containing that entry. Assert one hit and actual HEAD=S/index=L/worktree=L before fresh admission; only the fresh publication assertion may fail CP-1. After S2 removes that save, move the throw into the same boundary callback (entry supplied by the fixture's scoped-context handle); assert one hit again. HistoricalMonitor instead retains the stale tracked request and explicitly performs the old naked save in a **test-only** boundary callback after a real sweep commits. It must catch a genuine EF exception, not synthesize the competing-token evidence.

Monitor interleaving: A commits intent, executes CAS and signals paused; B captures original token, runs SweepAsync with FakeTimeProvider at +2 seconds (below 300-second warning), commits, and fresh C proves token changed/LastEvaluatedAt advanced with zero aged notifications; only then release A. Capture monitor stamp before later writers replace it. Record request-save callbacks during CAS/reset (must be zero), durable journal intent/child ownership for both real Git commands, and no DB transaction on entry to Git. Barriers use RunContinuationsAsynchronously and release/join in finally. A 30-second wait guard diagnoses fixture deadlock; it is never the sole expected red. PreIntent also observes committed intent from a fresh context at CAS; PostReset and restart cuts must preserve intent until alignment is proved.

### V-3: inspection probes cannot turn unknown content into clean content

New `LandRecoveryCheckoutTests`, directly exercising the **production** inspection member with real Git for content and controlled Git results for I/O errors:

| Named method (prefix `C883_`) | Explicit arguments / executions | Required outcome |
|---|---|---|
| EqualIndexAndWorktreeAccepted | none / 1 | Independent equality receipts name L and the same identity; no mutating probe. |
| IndexOnlyDifferenceRefused | none / 1 | Working tree equals L but index differs; rejected. |
| WorktreeOnlyDifferenceRefused | none / 1 | Index equals L but working tree differs; rejected. |
| UntrackedAndIgnoredRefused | untracked, ignored / 2 | Nonempty successful listings reject and preserve bytes. |
| UnsafeIndexModesRefuse | unmerged, assume-unchanged, skip-worktree, sparse / 4 | Flags/config that can hide work reject even if ordinary diff appears empty. |
| SequencerRefuses | merge, rebase, cherry-pick, sequencer-directory / 4 | Identity gate rejects before repair; marker remains. |
| SubmoduleAndFilterRefuse | gitlink, custom-filter, working-tree-encoding, ident / 4 | Each otherwise equal fixture rejects unsupported proof before conversion; external filter invocation count is zero. Inspect actual attributes/config/index, not status. |
| ProbeErrorsRefuse | cached-diff, worktree-diff, untracked, ignored, ancestry, index-flags, attributes, blob-read, byte-io / 9 | Otherwise valid/equal checkout. Inject exit 128, malformed NUL response, or scoped IOException at the named I/O seam; reject with sanitized unknown-proof result. No real filesystem permission race; byte-io can remove the owned path after enumeration. For worktree-diff, raw bytes deliberately still equal L so another guard cannot mask removal of the exit check. |
| IdentityResampleRefusesChange | none / 1 | Initial valid identity cannot mask later changed HEAD/registration. |
| BinaryEolAndModeComparison | binary, eol, mode / 3 | Detect one-byte binary changes; accept exact built-in LF/CRLF checkout bytes for L and reject a raw-byte deviation even if Git normalizes it; distinguish a tracked executable-mode change on Linux. Include symlink-target preservation assertions in the mode case. |

Frozen V-3: 10 methods, `1+1+1+2+4+4+4+9+1+3 = 30` results on either OS. The mode result proves actual executable/symlink edits on Linux; on Windows it proves refusal of uncertain mode/link evidence without requiring symlink privilege. This is one result on each OS, not a skip. V-5 adds native Windows path/index semantics. Attribute/filter refusal precedes any conversion. Every inspection result also asserts zero mutating commands.

### V-4: writer provenance and diagnostics

New `LandRequestWriteDiagnosticTests`:

| Named method (prefix `C883_`) | Explicit arguments / executions | Required outcome |
|---|---|---|
| ConflictNamesActualEntityAndTokens | request-direct, request-hosted, other-entity / 3 | Real two-context conflict on AgentTaskLandRequest or AgentTask; retain different original/attempted/stored tokens and exact GUID key. Drive FailAsync or hosted drain, assert captured summary survives ChangeTracker.Clear and names the actual entity, phase, request, attempt and observedDatabaseWriter. A later intervening writer is reported as observed, never asserted to be the original winner. |
| WriterStampComesFromCommittedToken | monitor, hold, source, protocol, merge, admission / 6 | Drive the real writer path with fixture rows; persisted stamp token equals committed token and operation label/time is correct. Merge ordinary supersession rotates/stamps; reviewed-recovery guard preserves its request. |
| MissingOrStaleWriterIsUnknown | legacy, token-mismatch, deleted-row, read-unavailable / 4 | No inferred actor, no secondary diagnostic exception masking landing_concurrency_conflict. Missing entry list is also asserted in the legacy case. |
| SecretPayloadIsAbsent | none / 1 | Synthetic hostile exception/entry values absent from log, terminal event and notification; allow-listed GUIDs/codes still present. |
| TerminalSummaryIsIdempotent | none / 1 | Repeated failure callback creates one terminal outcome/notification and retains the original diagnostic summary. |
| MigrationPreservesLegacyRows | none / 1 | Owned DB migrated to immediate predecessor; insert a real legacy request, migrate forward, assert unchanged GUID/progress/approval and null provenance/witness. Round-trip new fields and verify concurrency remains a GUID. EnsureCreated alone is insufficient. |
| RolledBackWriterIsNotCommitted | none / 1 | Abort transaction after SaveChanges: fresh observer retains prior stamp/token, and no committed-write log receipt is emitted. |
| DiagnosticReachesCaller | eligible, busy, before-enqueue, queue-inserted, queue-committed-before-wakeup, receipt-before-save / 6 | Produce the real concurrency diagnostic, then actual notifier/queue through BridgeQueueHarness. Busy: no submit until explicit TurnEnd. At each named cut throw/drop once, reconstruct notifier scope and reconcile. Assert one immutable safe complete prompt, correct correlation/session/sequence, one queue row/receipt and no duplicate typing after receipt-save recovery. Use bounded drain/reconcile operations; no sleep-based ordering. |

Frozen V-4: 8 methods, `3+6+4+1+1+1+1+6 = 23` results on either OS. Writer arguments drive production paths; source covers checkpoint/operation-attach/remaining owned source callback and protocol/admission arguments inspect start/terminal/retry/yield stamps encountered by their controlled lifecycle. Enumerate code-owned labels with exact token-linked receipts; no helper self-comparison. SecretPayload inspects structured state **and captured Exception.ToString()**, since the current logger passes the exception object even when formatted text is safe. Use synthetic markers only. Bound entry list/property-name count and length explicitly; terminal/log outputs contain no property values.

### V-5: narrow Windows native row

New `AgentTaskLandHalfResetWindowsTests` contains four single-result methods: `C883_LinkedWorktreeWithSpacesRecovers` (fresh-ID cut/repair under native Windows Git, exact registered path); `C883_StagedBlobSurvivesNativeIndex` (index-only edit survives); `C883_BuiltinCrLfRecoveryPreservesRealEdits` (ordinary core.autocrlf checkout at L repairs successfully; a raw-byte deviation is source_dirty and preserved even if normalized Git diff is empty); `C883_CaseAliasCannotChangeRegisteredIdentity` (mismatched registration/case alias cannot redirect reset to another tree). Use the same owned Git/schema fixture and no ConPTY/provider. Four executions, no skips on Windows. CP-5 alone requires Windows; do not select that class on Linux. A wrong-host invocation refuses the fixture instead of silently producing passing empty tests.

### R-1..R-5: independently recounted existing regression census

TestDesign reran the read-only PowerShell census at `4cdd8809b85ed479a8a2b3d43f80e3cd7577bf6e`, examining every matching source file, matching each `[Test]` method and counting adjacent `[Arguments]` (minimum one), then checking data generators, platform branches and skip attributes. Each class has one nonpartial file, no generated method data and no OS skips. The word Matrix in a method name is not a data-source attribute. Runtime counts remain unmeasured until fresh TRX exists; the table below is independently source-counted, not copied from Plan.

| ID / class | Source methods | Expanded existing results Linux / Windows | Count derivation in source order |
|---|---:|---:|---|
| R-1 AgentTaskLandAdoptionTests | 10 | 21 / 21 | `2+3+1+1+1+1+1+1+4+6` |
| R-2 AgentTaskLandSourcePersistenceTests | 9 | 35 / 35 | `3+8+3+1+10+4+3+2+1` |
| R-3 AgentTaskLandFailureDiagnosticTests | 7 | 25 / 25 | `8+2+3+1+4+2+5` |
| R-4 AgentTaskLandMonitoringTests | 8 | 25 / 25 | `4+4+4+4+1+1+5+2` |
| R-5 LandingGitTests | 22 | 55 / 55 | `2+4+1+1+1+7+4+4+2+3+7+3+4+1+4+1+1+1+1+1+1+1` |

Code-start recount from `4380891c`: CARD-0788 added `C788_RecoveryMismatchDetailSurvivesRestart` with six arguments to R-1; the other four named class files are unchanged from TestDesign. Total existing selection is `10+9+7+8+22 = 56` source methods and `21+35+25+25+55 = 161` expanded results per OS; static skip count 0. Existing same-request non-ancestor recovery stays green. Complete its synthetic interrupted-state fixture with the actual durable pin setup that production performs before update-ref, preserving its outcome/assertions and result count. That fixture-only edit is explicitly in S1's footprint.

Recount recipe (read-only PowerShell, with `$names` set to the five exact classes above): enumerate `$name.cs` and `$name.*.cs`; apply regex `\[Test\]([\s\S]*?)(?:public|internal)\s+(?:async\s+)?(?:Task(?:<[^>]+>)?|void|ValueTask)\s+(\w+)\s*\(`, then count `\[Arguments\(` in capture 1 with minimum one. Print each method and count; inspect generators/conditional skips manually. This is source-shape-specific, not a general C# parser. Code repeats it after serialized landings and after creating the new tests, then reconciles against actual TRX. New cardinalities are specifications, not source-measured tests.

Decisive regressions: R-1 retains same-request non-ancestor adoption, owner status, local CAS/remote lease and independent cleanup verdicts; R-2 retains monitor fields while rejecting stale approval/source progress and preserves pending shutdown work; R-3 retains classification/redaction, terminal idempotence and publication after later failure; R-4 retains per-pass token rotation without an aged event and notification/attention clocks; R-5 retains identity/sequencer/ref/error and cache behavior. No test outcome is weakened to accommodate recovery.

### Guard inventory

Each row below defines one G-n and its distinct PC-n with the same number. Independently removable witness fields/probes are separate controls. Several arguments exercising one predicate share its PC; Mutation must inspect every named argument result. Guards=81, mapped=81, missing=0, duplicate PC mappings=0. This inventory covers the new recovery/diagnostic surface; unchanged publication/remote-lease/cleanup machinery remains R-1..R-5, with no unrelated mutation campaign.

### Positive controls

Abbreviations expand exactly: H = AgentTaskLandHalfResetTests; A = AgentTaskLandAdoptionConcurrencyTests; G = LandRecoveryCheckoutTests; D = LandRequestWriteDiagnosticTests. All abbreviated method names have the literal C883_ prefix. A fully qualified existing method retains its written prefix. Each PC filter is exactly /*/*/<expanded-class>*/<expanded-method>*; arguments in parentheses identify the TRX rows to inspect, not literal filter suffixes. Every PC uses its named method only, with separate isolated baseline/red/restored-green outputs.

Mutation must produce an intended assertion failure, never a compile/setup failure, zero discovery, dead callback or timeout. For redundant safety checks, use the explicitly specified branch-local accepted-proof mutation rather than deleting an early return that a later refusal could mask. Unmerged/gitlink/transform controls return accepted at the production rejection branch and are detected by the direct inspector's Accepted=false assertion; they never execute a filter. A test-owned command observer records and safely refuses any attempted external conversion. Wrong-witness fixtures change only that witness field, keeping live coordinates/approval valid; the ambiguity fixture uses distinct ancestor commits with the same L tree so both witnesses independently satisfy checkout equality.

| Guard / PC | Required guard | Concrete compiling mutation | Named detecting method (argument rows) | Intended red assertion |
|---|---|---|---|---|
| G-1 / PC-1 | Fresh recovery is available only through proven intent | Reinstate requirement for the fresh request's own source-adopt-reset field | A.Save409FaultThenFreshRequestCompletes | fresh request publication confirmed (all cut assertions already pass) |
| G-2 / PC-2 | Unfinished witness exists | When no candidate exists, fabricate L from the matching ancestor | H.FreshRequestRequiresInterruptedWitness(absent) | source_dirty and zero reset |
| G-3 / PC-3 | Witness AdvanceStarted | Ignore only witness SourceResolutionState | H.FreshRequestRequiresInterruptedWitness(not-started) | source_dirty and zero reset |
| G-4 / PC-4 | Witness operation | Ignore only source-adopt-reset match | H.FreshRequestRequiresInterruptedWitness(wrong-operation) | source_dirty and zero reset |
| G-5 / PC-5 | Witness not already adopted | Ignore only RecoveryAdoptedAt | H.FreshRequestRequiresInterruptedWitness(already-adopted) | source_dirty and zero reset |
| G-6 / PC-6 | Witness owner | Drop owner comparison in witness eligibility | H.FreshRequestRejectsWitnessIdentity(owner) | no accepted witness / zero reset |
| G-7 / PC-7 | Witness source ref | Drop witness ref comparison | H.FreshRequestRejectsWitnessIdentity(ref) | no accepted witness / zero reset |
| G-8 / PC-8 | Witness registered worktree | Drop witness worktree comparison | H.FreshRequestRejectsWitnessIdentity(worktree) | no accepted witness / zero reset |
| G-9 / PC-9 | Witness expected S | Drop witness ExpectedSourceSha comparison | H.FreshRequestRejectsWitnessIdentity(sha) | no accepted witness / zero reset |
| G-10 / PC-10 | Witness endpoint fingerprint | Drop witness fingerprint comparison | H.FreshRequestRejectsWitnessIdentity(fingerprint) | no accepted witness / zero reset |
| G-11 / PC-11 | Witness canonical repository | Drop witness common-directory comparison | H.FreshRequestRejectsWitnessIdentity(common-directory) | no accepted witness / zero reset |
| G-12 / PC-12 | Witness recovery source identity | Drop source-task identity comparison | H.FreshRequestRejectsWitnessIdentity(source-identity) | no accepted witness / zero reset |
| G-13 / PC-13 | Witness L pin | Ignore the request-owned local-before pin mismatch | H.FreshRequestRejectsWitnessIdentity(local-pin) | source_dirty and zero reset |
| G-14 / PC-14 | Witness S pin | Ignore the request-owned source pin mismatch | H.FreshRequestRejectsWitnessIdentity(source-pin) | source_dirty and zero reset |
| G-15 / PC-15 | Unambiguous witness | Select the first conflicting valid witness instead of refusing | H.FreshRequestRejectsWitnessIdentity(ambiguous) | adopt_recovery_ambiguous |
| G-16 / PC-16 | Fresh L is ancestor of S | Treat merge-base exit 1 as an ancestor | H.FreshRequestRequiresAncestor | zero reset; same-request C753 compatibility still green |
| G-17 / PC-17 | Independent index equality | Omit cached-index difference rejection | H.StagedEditSurvivesFreshAndSameRequest | source_dirty and retained staged E blob with worktree=L |
| G-18 / PC-18 | Worktree diff must succeed | Treat worktree-diff exit 128 as equality | G.ProbeErrorsRefuse(worktree-diff) | Accepted=false on equal raw bytes; no masking by raw comparison |
| G-19 / PC-19 | Raw checkout equality | Return success for raw-byte mismatch after successful normalized diff | G.BinaryEolAndModeComparison(eol) | rejected raw CRLF deviation despite normalized diff=0 |
| G-20 / PC-20 | Untracked absence | Ignore nonempty ordinary untracked output | G.UntrackedAndIgnoredRefused(untracked) | Accepted=false; integration sentinel remains |
| G-21 / PC-21 | Ignored absence | Ignore nonempty ignored output | G.UntrackedAndIgnoredRefused(ignored) | Accepted=false; integration obstruction remains |
| G-22 / PC-22 | Unmerged index | Return an accepted proof from the unmerged-entry rejection branch | G.UnsafeIndexModesRefuse(unmerged) | Accepted=false; no later diff guard can mask this mutation |
| G-23 / PC-23 | Assume-unchanged exclusion | Remove assume-unchanged flag refusal | G.UnsafeIndexModesRefuse(assume-unchanged) | Accepted=false on otherwise equal checkout |
| G-24 / PC-24 | Skip-worktree exclusion | Remove skip-worktree flag refusal | G.UnsafeIndexModesRefuse(skip-worktree) | Accepted=false on otherwise equal checkout |
| G-25 / PC-25 | Sparse exclusion | Ignore sparse config with all paths currently included | G.UnsafeIndexModesRefuse(sparse) | Accepted=false with no other unsafe index flag |
| G-26 / PC-26 | Merge sequencer exclusion | Remove MERGE_HEAD from the sequencer predicate | G.SequencerRefuses(merge) | active_sequencer and marker preserved |
| G-27 / PC-27 | Rebase sequencer exclusion | Remove rebase directory checks from the predicate | G.SequencerRefuses(rebase) | active_sequencer and marker preserved |
| G-28 / PC-28 | Cherry-pick sequencer exclusion | Remove CHERRY_PICK_HEAD from the predicate | G.SequencerRefuses(cherry-pick) | active_sequencer and marker preserved |
| G-29 / PC-29 | Sequencer-directory exclusion | Remove sequencer directory check | G.SequencerRefuses(sequencer-directory) | active_sequencer and marker preserved |
| G-30 / PC-30 | Gitlink exclusion | Return an accepted proof from the gitlink rejection branch | G.SubmoduleAndFilterRefuse(gitlink) | Accepted=false before content conversion |
| G-31 / PC-31 | Custom-filter exclusion | Return an accepted proof from the custom-filter rejection branch | G.SubmoduleAndFilterRefuse(custom-filter) | Accepted=false; filter calls remain zero on both baseline and mutant |
| G-32 / PC-32 | Encoding exclusion | Return an accepted proof from the encoding rejection branch | G.SubmoduleAndFilterRefuse(working-tree-encoding) | Accepted=false before conversion |
| G-33 / PC-33 | Ident exclusion | Return an accepted proof from the ident rejection branch | G.SubmoduleAndFilterRefuse(ident) | Accepted=false before conversion |
| G-34 / PC-34 | Cached diff error | Treat cached-diff exit 128 as empty | G.ProbeErrorsRefuse(cached-diff) | Accepted=false on otherwise equal checkout |
| G-35 / PC-35 | Untracked listing error | Treat untracked exit 128 as empty | G.ProbeErrorsRefuse(untracked) | Accepted=false |
| G-36 / PC-36 | Ignored listing error | Treat ignored exit 128 as empty | G.ProbeErrorsRefuse(ignored) | Accepted=false |
| G-37 / PC-37 | Ancestry error | Treat merge-base exit 128 as ancestor | G.ProbeErrorsRefuse(ancestry) | Accepted=false |
| G-38 / PC-38 | Index flag probe error | Treat malformed index flag response as no flags | G.ProbeErrorsRefuse(index-flags) | Accepted=false |
| G-39 / PC-39 | Attribute probe error | Treat failed attribute lookup as no attributes | G.ProbeErrorsRefuse(attributes) | Accepted=false |
| G-40 / PC-40 | Blob probe error | Treat failed blob read as proved equality | G.ProbeErrorsRefuse(blob-read) | Accepted=false |
| G-41 / PC-41 | Byte I/O error | Catch owned read IOException and return equality | G.ProbeErrorsRefuse(byte-io) | Accepted=false and bounded unknown-proof detail |
| G-42 / PC-42 | Final HEAD identity | Omit final live identity resample (HEAD argument) | H.IdentityChangesBeforeResetRefuse(head) | zero reset after changed HEAD |
| G-43 / PC-43 | Final branch identity | Ignore final symbolic-ref mismatch | H.IdentityChangesBeforeResetRefuse(branch) | zero reset; other branch preserved |
| G-44 / PC-44 | Final registration identity | Ignore final registration mismatch | H.IdentityChangesBeforeResetRefuse(registration) | zero reset; no redirected mutation |
| G-45 / PC-45 | Exact reviewed SHA | Skip final LoadRecoveryEvidenceAsync SHA validation only | H.AuthorityChangesBeforeResetRefuse(review-sha) | review_evidence_sha_mismatch before reset |
| G-46 / PC-46 | Verified clean Review | Accept ReviewedSourceClean!=true at final check | H.AuthorityChangesBeforeResetRefuse(review-clean-false, review-clean-null) | review_evidence_source_not_clean for both rows |
| G-47 / PC-47 | Review still current | Skip final superseded-evidence query | H.AuthorityChangesBeforeResetRefuse(review-superseded) | review_evidence_superseded before reset |
| G-48 / PC-48 | Reviewed remote tip | Accept changed source tip at final recheck | H.AuthorityChangesBeforeResetRefuse(remote) | zero reset/publication |
| G-49 / PC-49 | Remote endpoint identity | Accept changed fingerprint at final recheck | H.AuthorityChangesBeforeResetRefuse(fingerprint) | zero reset/publication |
| G-50 / PC-50 | Owner remains eligible | Skip final owner-status recheck | H.AuthorityChangesBeforeResetRefuse(owner) | zero reset/publication while Working |
| G-51 / PC-51 | Legacy child custody | Treat live/unknown predecessor PID observations as dead | H.UncertainPriorChildRefuses(live-request, unknown-request) | no reset, existing child hold/refusal; zero kill |
| G-52 / PC-52 | Journal child custody | Ignore live/unknown unfinished child journal when acquiring | H.UncertainPriorChildRefuses(live-journal, unknown-journal) | no reset, lease refused; zero kill |
| G-53 / PC-53 | Final content recheck | Reuse first content proof rather than resampling at reset | H.ContentChangesBeforeResetRefuse(index, tracked, untracked, ignored) | zero reset; injected bytes/index preserved |
| G-54 / PC-54 | Final pin recheck | Reuse initial L/S pins at reset | H.ContentChangesBeforeResetRefuse(local-pin, source-pin) | zero reset; changed pin remains |
| G-55 / PC-55 | Post-reset alignment | Acknowledge adoption without final clean-S inspection | H.PostResetMismatchCannotPublish | RecoveryAdoptedAt=null and zero target push |
| G-56 / PC-56 | Pending request authority | Allow RequestAsync to replace a running recovery request | H.CurrentPendingRequestCannotBeStolen | land_running/land_request_identity_conflict; same authoritative ID |
| G-57 / PC-57 | Clean-S idempotence | Always hard-reset even when S already aligned | H.CompletedResetIsIdempotent | second request reset count=0 |
| G-58 / PC-58 | CAS expected old L | Remove expected-old operand from adoption update-ref | A.PreIntentConflictLeavesCheckoutUnmoved | CAS argument vector contains exact L on its recovery-success subcase |
| G-59 / PC-59 | Standing child journal | Bypass journal Begin for adoption RunAsync mutations | A.MonitorBetweenMoveAndResetDoesNotPreventReset | durable child intent observed at each real CAS/reset start |
| G-60 / PC-60 | Durable intent before CAS | Move pre-intent checkpoint after CAS | A.PreIntentConflictLeavesCheckoutUnmoved | HEAD/index/worktree=L and zero CAS after injected pre-intent save conflict |
| G-61 / PC-61 | No naked write between CAS/reset | Restore the post-CAS naked request save | A.MonitorBetweenMoveAndResetDoesNotPreventReset | reset completed / no LandRefused after actual monitor token change |
| G-62 / PC-62 | No request-saving started callback | Use RunOwnedAsync with request-saving StartedAsync for adoption reset | A.MonitorBetweenMoveAndResetDoesNotPreventReset | request-save callback count inside adoption pair=0 |
| G-63 / PC-63 | Preserve concurrent hold fields | Apply stale full request over reloaded row at source checkpoint | A.HeldWriterBetweenMoveAndResetDoesNotLoseHoldFacts | committed hold episode/owner/detail retained at source acknowledgement |
| G-64 / PC-64 | Reject changed source authority | Make RequestWriter.IsStale ignore ExpectedSourceSha | AgentTaskLandSourcePersistenceTests.C498_StaleRequestGuards(approval-sha) | source progress remains None; no publication |
| G-65 / PC-65 | No DB transaction across Git | Extend checkpoint transaction into adoption commands | A.MonitorBetweenMoveAndResetDoesNotPreventReset | CurrentTransaction=null asserted at command entry, before waiting for B |
| G-66 / PC-66 | Interrupted intent retained | Clear adoption intent/pins in generic terminal failure | A.Save409FaultThenFreshRequestCompletes | first failed request retains AdvanceStarted/L/S/operation and pins |
| G-67 / PC-67 | Capture before tracker clear | Move safe entry capture after Clear and derive it from tracker | D.ConflictNamesActualEntityAndTokens(request-direct, request-hosted) | exact original/attempted token and key present despite cleared tracker |
| G-68 / PC-68 | Actual conflicting entity | Always label entry as request and use contextual request key | D.ConflictNamesActualEntityAndTokens(other-entity) | AgentTask key/type, not AgentTaskLandRequest |
| G-69 / PC-69 | Token-linked observed writer | Trust label when LastWriterToken differs from stored token | D.MissingOrStaleWriterIsUnknown(token-mismatch) | observedDatabaseWriter=unknown |
| G-70 / PC-70 | Unavailable provenance | Return a synthetic monitor-sweep writer label from unavailable-row/entry/read-error branches | D.MissingOrStaleWriterIsUnknown | unknown/unavailable writer assertion; original concurrency code still present |
| G-71 / PC-71 | No secret values | Append raw exception/entry values or attach hostile exception to safe log | D.SecretPayloadIsAbsent | marker absent from log state/exception/event/notification |
| G-72 / PC-72 | Bounded safe detail | Remove entry-count/property-name/detail bounds | D.SecretPayloadIsAbsent | bounded summary and only allow-listed identifiers after oversized synthetic input |
| G-73 / PC-73 | Commit-linked receipts | Emit committed receipt after SaveChanges before commit | D.RolledBackWriterIsNotCommitted | zero committed receipts and original stored stamp/token after rollback |
| G-74 / PC-74 | Legacy migration truth | Backfill invented writer stamp during migration | D.MigrationPreservesLegacyRows | legacy writer/time/token/witness all null; legacy approval/GUID unchanged |
| G-75 / PC-75 | Stamp same row/token | Set LastWriterToken to the old concurrency token | D.WriterStampComesFromCommittedToken | stamp token equals committed token in every argument |
| G-76 / PC-76 | Recovery Merge authority | Remove ReplyService's reviewed-recovery early return | D.WriterStampComesFromCommittedToken(merge) | reviewed recovery row remains authoritative; ordinary row rotates/stamps |
| G-77 / PC-77 | Terminal diagnostic idempotence | Overwrite original summary on repeated failure callback | D.TerminalSummaryIsIdempotent | same diagnostic/digest; one terminal and outcome |
| G-78 / PC-78 | Diagnostic reaches recipient | Omit safe summary when formatting terminal notification | D.DiagnosticReachesCaller(eligible, busy) | complete prompt contains expected safe summary and diagnostic ID |
| G-79 / PC-79 | Reconciliation does not duplicate | On replay discard stored queue identity and allocate a new key/ID instead of reusing the persisted notification key | D.DiagnosticReachesCaller(queue-committed-before-wakeup, receipt-before-save) | one queue ID/complete prompt and no duplicate submit |
| G-80 / PC-80 | Receipt requires matching complete prompt | Mark outcome confirmed at enqueue/Sent without prompt | D.DiagnosticReachesCaller(busy) | ConfirmedAt=null while busy; confirms only after matching UserPrompt |
| G-81 / PC-81 | Ordinary-land isolation | Route ordinary/cleanup-only land through half-reset proof | H.CompletedResetIsIdempotent | ordinary dirty-source publication allowed with cleanup residue; recovery-inspection count=0 |

Windows native controls use the corresponding invariant without multiplying the ordinary roster: PC-17 additionally runs AgentTaskLandHalfResetWindowsTests.C883_StagedBlobSurvivesNativeIndex; PC-19 additionally runs its C883_BuiltinCrLfRecoveryPreservesRealEdits; PC-44 additionally runs its C883_CaseAliasCannotChangeRegisteredIdentity. Each native method is an independent baseline/red/restore/green variant; no Linux result substitutes for it. The positive native success assertion detects rejecting all Windows paths in C883_LinkedWorktreeWithSpacesRecovers as a fourth native variant of PC-1.

### Out of scope

Track the supplied known-flake list explicitly: **CARD-0791, CARD-0794, CARD-0818, CARD-0820, CARD-0828, CARD-0848, CARD-0878, CARD-0879**. Also: `ScaledTimeProviderTests.Speed_10` (CARD-0757), `HttpResilienceRegistrationTests.Runner_list_and_git_connectivity_keep_their_short_deadlines` (CARD-0751), and `ResilienceBudgetTests.Slow_first_attempt_consumes_the_same_budget` (CARD-0820). Their present board disposition was not queried. CARD-0878's Linux native-session classification skip is visible in `HerdrAlwaysOnChannelParityTests.StandingRecovery.cs:141` and is outside this selection.

Do not add those unrelated classes to the land filters or claim they passed. No named existing class here has a static OS skip. A selected failure is investigated on its own evidence; it is not excused by a neighboring flake card. A justified rerun uses the exact failing method once and records source SHA, original failure, rerun count and outcome. No full Unit/namespace/provider/Herdr/checkpoint-tool suite is commissioned. A control-plane timeout/slot refusal is infrastructure evidence, never a passing test or a successful positive control.

### Cost

All figures are estimates, not observed runtimes. Closed Code floor = CP-1..CP-5 **8+18+12+18+8 = 64 minutes**: one deliberate pre-fix red plus 56 minutes final V/R. Code-start final unique results = **75+85+108+4 = 272**, of which 161 are source-counted existing results and 111 are specified new results. CP-1 repeats one result, giving 273 planned executions and one intentional pre-fix failure. All final results must pass without skips.

Mutation has 81 distinct PCs plus four Windows native variants = **85 method-scoped variants**. At an estimated 4 minutes per isolated build/test phase, baseline + red + restored green costs **85×3×4 = 1,020 minutes**, separate from mutation editing/discovery and host-slot waits. Split Mutation into bounded commissions; do not represent 18 unsplit Plan labels as 18 executed controls. Code+Mutation execution floor is 1,084 minutes. Five batched Code builds replace one build per 43 new test methods (38 avoided builds); no full-suite claim or speculative numeric wall-time saving. Authoring remains approximately 120–180 minutes, plus any fixture work beyond that estimate; this TestDesign's 60-minute target is separate.

### Checkpoints

The **closed list** follows. S1-red is deliberately minimal and precedes repair/order implementation; final rows follow committed S1-S4. Each row has exactly one isolated build and one literal filter. CP-1..CP-4 are **OS-unpinned**: run once on the available qualified host (prefer server2/Linux), not once per OS. Counts are stated for both. CP-5 alone requires Windows, for native path/index/CRLF behavior; wrong-host invocation fails explicitly, never produces a green empty selection. Serial applies between rows; TUnit parallelism is separately limited by Environment. Table pipes are escaped; actual filter arguments contain ordinary pipes.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---:|---:|---|---|
| CP-1 | S1-red | `tests/Antiphon.Tests -> bin-c883-red/` | historical-save-red | `/*/*/AgentTaskLandAdoptionConcurrencyTests*/C883_Save409FaultThenFreshRequestCompletes*` | V-2 baseline | Linux or Windows: 1 executed, 0 passed, exactly 1 fresh-publication assertion failed, 0 skipped; exit 1 intentional | 1 | 8 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-2 | S1-S4 | `tests/Antiphon.Tests -> bin-c883-recovery/` | recovery-and-ordering | `/*/*/(AgentTaskLandHalfResetTests*)\|(AgentTaskLandAdoptionConcurrencyTests*)\|(AgentTaskLandAdoptionTests*)/*` | V-1,V-2,R-1 | Linux 75 / Windows 75 = 47+7+21; all listed, 0 failed/skipped | 75 | 18 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-3 | S1-S4 | `tests/Antiphon.Tests -> bin-c883-git/` | checkout-proof | `/*/*/(LandRecoveryCheckoutTests*)\|(LandingGitTests*)/*` | V-3,R-5 | Linux 85 / Windows 85 = 30+55; all listed, 0 failed/skipped | 85 | 12 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-4 | S1-S4 | `tests/Antiphon.Tests -> bin-c883-diagnostics/` | writer-and-persistence | `/*/*/(LandRequestWriteDiagnosticTests*)\|(AgentTaskLandSourcePersistenceTests*)\|(AgentTaskLandFailureDiagnosticTests*)\|(AgentTaskLandMonitoringTests*)/*` | V-4,R-2,R-3,R-4 | Linux 108 / Windows 108 = 23+35+25+25; all listed, 0 failed/skipped | 108 | 18 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-5 | S1-S4 | `tests/Antiphon.Tests -> bin-c883-windows/` | windows-checkout | `/*/*/AgentTaskLandHalfResetWindowsTests*/*` | V-5 | Windows 4, all listed, 0 failed/skipped; Linux not commissioned | 4 | 8 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |

## Execution, activation and rollback

Test-first sequence is frozen:

1. **S1-red:** instance-scoped exact-cut observation only, typed request-entry interceptor, minimal 0801 fixture and Save409FaultThenFreshRequestCompletes. Preserve old ordering/behavior. Commit/push, run only CP-1. Require its one intended fresh-publication failure; setup/cut/half-tree assertions pass first.
2. **S1:** add V-1/V-3 in table order, then witness/checkout proof and existing C753 pin setup. Its current clean Review seed already exists. Write the additional content/provenance assertions before production changes.
3. **S2:** remaining V-2 in table order, then checkpoint ordering/callback removal. Transfer Save409's injection to the same boundary when the old save disappears. Do not leave a dormant interceptor. Real monitor stale-save replay stays test-only.
4. **S3:** V-4 in table order before provenance/failure formatting/migration work. Generate the additive migration against the landed `AddReviewSourceClean` predecessor; CARD-0826 is not a dependency.
5. **S4:** four V-5 native tests before native adjustments/docs. Commit/push each slice; run CP-2..CP-4 at the same final SHA and CP-5 on Windows at that SHA. No uncommitted source fixes hidden under an old receipt.

The current Code brief requires **`scripts/run-checkpoint.ps1` directly** because the checkpoint tool refuses owner-unverified execution without a task token (CARD-0853). The script self-leases. Commit before each row, pass the exact SHA and a literal filter with unescaped pipes, and preserve its receipt. No checkpoint runs occurred in TestDesign.

```powershell
$sourceSha = (git rev-parse HEAD).Trim()
pwsh -NoProfile -File scripts/run-checkpoint.ps1 -Name CP-1 -Project tests/Antiphon.Tests -OutputPath bin-c883-red/ -Filter '/*/*/AgentTaskLandAdoptionConcurrencyTests*/C883_Save409FaultThenFreshRequestCompletes*' -MinExecuted 1 -Expect AgentTaskLandAdoptionConcurrencyTests -ResultsRoot .antiphon/c883-red -ExpectedSourceSha $sourceSha
# At final committed S1-S4 SHA, run each final row once; CP-5 runs on Windows only.
pwsh -NoProfile -File scripts/run-checkpoint.ps1 -Name CP-2 -Project tests/Antiphon.Tests -OutputPath bin-c883-recovery/ -Filter '/*/*/(AgentTaskLandHalfResetTests*)|(AgentTaskLandAdoptionConcurrencyTests*)|(AgentTaskLandAdoptionTests*)/*' -MinExecuted 75 -Expect AgentTaskLandHalfResetTests,AgentTaskLandAdoptionConcurrencyTests,AgentTaskLandAdoptionTests -ResultsRoot .antiphon/c883-final -ExpectedSourceSha $sourceSha
pwsh -NoProfile -File scripts/run-checkpoint.ps1 -Name CP-3 -Project tests/Antiphon.Tests -OutputPath bin-c883-git/ -Filter '/*/*/(LandRecoveryCheckoutTests*)|(LandingGitTests*)/*' -MinExecuted 85 -Expect LandRecoveryCheckoutTests,LandingGitTests -ResultsRoot .antiphon/c883-final -ExpectedSourceSha $sourceSha
pwsh -NoProfile -File scripts/run-checkpoint.ps1 -Name CP-4 -Project tests/Antiphon.Tests -OutputPath bin-c883-diagnostics/ -Filter '/*/*/(LandRequestWriteDiagnosticTests*)|(AgentTaskLandSourcePersistenceTests*)|(AgentTaskLandFailureDiagnosticTests*)|(AgentTaskLandMonitoringTests*)/*' -MinExecuted 108 -Expect LandRequestWriteDiagnosticTests,AgentTaskLandSourcePersistenceTests,AgentTaskLandFailureDiagnosticTests,AgentTaskLandMonitoringTests -ResultsRoot .antiphon/c883-final -ExpectedSourceSha $sourceSha
pwsh -NoProfile -File scripts/run-checkpoint.ps1 -Name CP-5 -Project tests/Antiphon.Tests -OutputPath bin-c883-windows/ -Filter '/*/*/AgentTaskLandHalfResetWindowsTests*/*' -MinExecuted 4 -Expect AgentTaskLandHalfResetWindowsTests -ResultsRoot .antiphon/c883-final -ExpectedSourceSha $sourceSha
```

Each script row acquires and releases its own build slot and drives an isolated build plus TUnit filter. Never dotnet test, bypass the slot, or treat zero selected tests as a pass. Report slot refusal/timeout/unleased as incomplete host evidence.

At each run preserve unedited CHECKPOINT/FAILED/EXECUTED lines, source.json/report.json, exact committed SHA, OS, filter, build result, executed/passed/failed/skipped, reruns and fresh TRX roster. Strict source evidence must be clean/verified. Compare method/argument identities as well as Min; a count alone cannot prove the intended selection. Final Review has every final row; CP-1 is separately labeled pre-fix red, not accepted as clean evidence. Windows evidence pending remains pending. For illustration, CP-2's actual filter is `/*/*/(AgentTaskLandHalfResetTests*)|(AgentTaskLandAdoptionConcurrencyTests*)|(AgentTaskLandAdoptionTests*)/*` (no backslashes).

CLI schema generation is the other explicitly listed authoring operation: `dotnet tool restore`, then `pwsh -NoProfile -File scripts/build-slot.ps1 -Label c883-migration -- dotnet ef migrations add AddLandRequestWriterProvenance --project server`. Record the build-driving command with reason schema generation. Never hand-edit migration/Designer/snapshot. Mutation later uses the unchanged direct checkpoint script with one exact detecting method per phase under the owner's Mutation procedure, separate from these Code rows.

Activation is a later caller/operator action after Code, Review and mandatory source-owner land ordering. Additive nullable provenance columns deploy before binaries that reference them; old rows/binaries yield unknown provenance rather than an invented writer. The source recovery code needs no data backfill, automatic repair job or runner upgrade. From the main checkout, use the canonical AppHost restart path after updating to the landed source; read the Job Object/current-checkout caveat, and confirm `/api/version` full SHA and land-v2 directly. Do not restart from this worktree or infer activation from health. A worktree branch push is publication of this plan only.

After activation, use the read-only observation recipe for the next independently requested recovery. Confirm fresh request ID, old witness, aligned tree, terminal publication and diagnostic fields; do not manufacture a half-reset on a live owner. If the tree contains real work, retain source_dirty and route it for inspection. No manual reset is commissioned by this plan.

Rollback: roll forward with a reviewed revert of this feature's behavior on the shared FF-only workflow, retaining nullable columns and recovery pins/intent. Do not run a destructive Down migration or blanket reset worktrees. The previous binary safely ignores the new columns but has the old fresh-request limitation; explicitly hold affected recoveries for inspected disposition. Preserve the old diagnostic/request/event evidence and any pending operation. Publication already confirmed is not undone; cleanup remains separately authorized.

### Handoff status

TestDesign specified 43 new methods / 111 results. Code-start recount finds 56 existing methods / 161 source-counted results after CARD-0788; 81 guards map to 81 controls plus four native variants, with five closed checkpoint rows. Code preserves source-clean Review approval and reports actual fresh TRX counts against this roster. No activation occurred in this stage.

Next stage: **Code** after the source-owner collisions are clear. The exact-cut red is the first implementation checkpoint; the historical concurrent writer remains unproven until correlated live evidence exists.
