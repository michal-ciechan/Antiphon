# CARD-0883: recover a land adoption interrupted between ref movement and checkout reset

Date: 2026-10-01. Stage: Plan; next TestDesign. Baseline: `origin/master` at **`fccef27eb03f2522f2a40574dc1c764335d4a490`**. `git rev-parse HEAD origin/master` and `git ls-remote origin refs/heads/master` agreed during inspection. Assigned branch: `feat/card-task-373933b4`, starting at that same commit; no rebase, merge, reset, or master update is part of this task. Line references and the existing-test census below are against this frozen baseline, not a claim about the currently loaded server.

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

**Code must wait for CARD-0835 to land.** It changes AgentTaskLandingProtocol, Review/land authority and the EF model snapshot/migration sequence also touched here. Reinspect its landed source-clean Review contract and seed valid clean evidence in this card's fixtures; do not relax LandApproval or bypass the new gate. TestDesign may proceed now against this baseline, marking the required seed/census refresh. No Plan-time rebase/merge is authorized.

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

### Fixtures, delivery boundary and deterministic time

Use LandingSafetyHarness/LandingGitFixture for real Git in an owned temporary local repository with a local bare origin, an isolated PostgreSQL schema, a controlled verifier and ReplyTo=None. LandHalfResetFixture supplies the reviewed owner/source, pinned L/S intent, reset barrier and optional interceptor. No real provider, network Git remote, application restart, production runner, or production database. Add the assembly-local `[ParallelLimiter<ProcessSpawnLimit>]` to every new class that starts Git; serialize these rows and set `TUNIT_MAX_PARALLEL_TESTS=1`. Do not co-schedule Antiphon.Agents.Pty.Tests.

The first request must traverse real request admission, resolver, failure settlement and persisted terminal event. The second request must traverse real admission with a different ID and newly constructed scoped services, not reuse tracked objects or manually set its recovery-progress fields. Snapshot index/worktree content before publication cleanup can remove the owned test tree. Assert both the intermediate repair and confirmed remote target containment; Directory.Exists becoming false is not a publication assertion. Preserve/inspect the old failed request and pins independently of the new request's outcome.

These tests exercise the existing durable request -> terminal AgentTaskEvent -> AgentTaskLandNotification payload pipeline; inspect exactly one terminal event/notification for the request, correlated by its ID and FailureDiagnosticId. ReplyTo=None yields the existing NotRequired delivery state. No queue/typing behavior changes, so no live session receipt is claimed. Diagnostic text must agree in the event and stored notification body. If Code changes delivery routing, this roster is insufficient and TestDesign must amend the producer/destination/receipt inventory first.

Use a FakeTimeProvider only for monitor age/provenance timestamps and short, explicitly advanced service operations. Barriers complete by explicit TaskCompletionSource release and a second DbContext commit; their deadlines are failure guards, never expected outcomes. Do not inject a frozen clock into SessionMessageQueueService or a whole hosted server. No sleeps, 12.5-second scheduling assumptions, or 5-second monitor margins. The monitor race calls SweepAsync once at the barrier; its clock can stay below the warning threshold to prove that a token write needs no notification.

### V-1: fresh-request recovery and preservation

New `AgentTaskLandHalfResetTests`. The following is a **proposed executable roster**, not an assertion that these tests exist or passed. Each named argument is a separate `[Arguments]` result; do not collapse the rows into an internal assertion loop and keep their execution counts unchanged.

| Named method (prefix `C883_`) | Explicit arguments / executions | Required outcome |
|---|---|---|
| FreshRequestRepairsPinnedAncestor | OwnerReviewedSource, AdoptReviewedSource / 2 | Fresh ID has no initial progress fields; it finds the prior witness, aligns index/worktree to S, records L/witness, and confirms publication. Source task's separate checkout survives adoption. |
| FreshRequestRequiresInterruptedWitness | none / 1 | Even if both trees equal an ancestor, no persisted unfinished intent means source_dirty; zero reset and unchanged bytes. |
| FreshRequestRejectsWitnessIdentity | owner, ref, worktree, sha, fingerprint, ambiguous / 6 | Wrong binding cannot donate authority. First five refuse without reset; conflicting valid L witnesses give adopt_recovery_ambiguous. |
| UnstagedEditSurvivesFreshAndSameRequest | fresh, same / 2 | Alter a tracked file after half-reset; source_dirty, exact edit bytes/index remain, target unchanged. |
| StagedEditSurvivesFreshAndSameRequest | fresh, same / 2 | Stage a new blob, then restore only the worktree to L; source_dirty and that staged blob remains. This specifically defeats the baseline worktree-only diff. |
| UntrackedAndIgnoredArePreserved | untracked, ignored / 2 | Obstruct an S path with owned sentinel bytes; no reset or cleanup, sentinel unchanged. |
| FreshRequestRequiresAncestor | none / 1 | Valid old intent with non-ancestor L cannot authorize a fresh-request repair; source_dirty/recovery_checkout_unproven, no mutation. Same-request compatibility is separately covered by existing C753. |
| IdentityChangesBeforeResetRefuse | head, branch, registration / 3 | Change the identity at the last barrier; no reset, changed identity preserved, adopt_local_changed or existing precise identity refusal. |
| AuthorityChangesBeforeResetRefuse | review, remote, owner / 3 | Supersede evidence, move remote tip or make owner actively Working; no reset/publication, existing authority refusal preserved. CARD-0835's source-clean=false/null case must be included as internal assertions in the review argument. |
| UncertainPriorChildRefuses | live, unknown / 2 | A prior child/journal cannot be bypassed by a new request; existing interrupted-child/lease hold remains, no reset or process kill. |
| CurrentPendingRequestCannotBeStolen | none / 1 | New admission while the first request remains pending returns existing conflict; old identity/progress stays authoritative. |
| CompletedResetIsIdempotent | none / 1 | Interrupt after alignment but before acknowledgement, then fresh admission after terminal failure; clean S is accepted without another reset; one publication and preserved old refusal. |

Proposed V-1 total: `2+1+6+2+2+2+1+3+3+2+1+1 = 26` results. The fixture verifies no mutable-source bytes disappear on every refusal, not merely the reason string.

### V-2: save ordering, exact injected cut and competing writes

New `AgentTaskLandAdoptionConcurrencyTests`, seven nonparameterized methods:

| Named method (prefix `C883_`) | Observable assertions |
|---|---|
| Save409FaultThenFreshRequestCompletes | D-3's exact-cut interception fires once. First terminal code landing_concurrency_conflict; HEAD=S/index=L/worktree=L; zero reset/target push. A **new** request completes alignment and publication. This is CP-1's intentional baseline red and CP-2's repaired green. |
| PreIntentConflictLeavesCheckoutUnmoved | Interceptor throws at the real reordered pre-mutation save. Assert failure code, HEAD/index/worktree all L, zero CAS/reset. After failure settles, a fresh request publishes. |
| MonitorBetweenMoveAndResetDoesNotPreventReset | Pause at the exact historical cut, run real Monitor.SweepAsync in a second context, prove its token committed, release. New ordering finishes reset and preserves LastEvaluatedAt/monitor fields through the final source checkpoint; no LandRefused. |
| HeldWriterBetweenMoveAndResetDoesNotLoseHoldFacts | Coordinate the actual land HoldAsync path through a second service/controlled lease refusal, not a raw token-only SQL update. Prove its committed token/hold episode, then resume; source checkpoint retains hold facts until the normal next state transition clears them. No stale overwrite or half-reset. |
| PostResetSaveConflictFreshRequestCompletes | Throw on the first acknowledgement save after verified reset. First request fails with both trees at S; after terminal settlement a fresh request completes with no reset replay. |
| RestartAfterMoveResumesPendingBeforeFreshAdmission | A controlled shutdown cut leaves intent and pending request. Dispose/rebuild scoped services after the child is joined; a new admission is refused while pending, then resume the original request safely. No real server process is stopped. |
| HistoricalMonitorSaveConflictThenFreshRequestCompletes | At the post-ref/pre-reset boundary run the real monitor and, in a **test-only legacy replay callback**, attempt the old naked SaveChanges with its stale tracked request. Require an actual EF concurrency exception, changed database token with monitor provenance, first terminal diagnostic, then a fresh-ID successful land. Do not add a production save back for this test. |

The controlled held-writer case must demonstrate reachability through the existing service boundary. If actual admission prevents its interleaving, TestDesign replaces that scenario with an assertion that admission excludes it and records why; it must not invent a production race or change admission to make the test pass. Recount the manifest on any changed expansion. The monitor race is mandatory regardless.

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
| SubmoduleAndFilterRefuse | gitlink, custom-filter / 2 | Reject unsupported proof; filter command is never invoked. |
| ProbeErrorsRefuse | cached-diff, worktree-diff, untracked, ignored, ancestry / 5 | Nonzero/error exit is not accepted as equality/absence; diagnostic remains sanitized. |
| IdentityResampleRefusesChange | none / 1 | Initial valid identity cannot mask later changed HEAD/registration. |
| BinaryEolAndModeComparison | binary, eol, mode / 3 | Detect one-byte binary changes; accept exact built-in LF/CRLF checkout bytes for L and reject a raw-byte deviation even if Git normalizes it; distinguish a tracked executable-mode change on Linux. Include symlink-target preservation assertions in the mode case. |

Proposed V-3 total: `1+1+1+2+4+4+2+5+1+3 = 24` results. Linux supplies executable-mode and symlink coverage. Windows-specific mode/path behavior belongs in V-5. Attribute/filter refusal must precede any attempted byte conversion; never execute a sentinel external filter and then claim refusal protected it.

### V-4: writer provenance and diagnostics

New `LandRequestWriteDiagnosticTests`:

| Named method (prefix `C883_`) | Explicit arguments / executions | Required outcome |
|---|---|---|
| ConflictNamesActualEntityAndTokens | request, other-entity / 2 | Real conflicting entry's entity/key/original/attempted/database tokens are reported; task/request context does not mislabel another entity as the request. |
| WriterStampComesFromCommittedToken | monitor, hold, source, protocol, merge, admission / 6 | Drive the real writer path with fixture rows; persisted stamp token equals committed token and operation label/time is correct. Merge ordinary supersession rotates/stamps; reviewed-recovery guard preserves its request. |
| MissingOrStaleWriterIsUnknown | legacy, token-mismatch, deleted-row, read-unavailable / 4 | No inferred actor, no secondary diagnostic exception masking landing_concurrency_conflict. Missing entry list is also asserted in the legacy case. |
| SecretPayloadIsAbsent | none / 1 | Synthetic hostile exception/entry values absent from log, terminal event and notification; allow-listed GUIDs/codes still present. |
| TerminalSummaryIsIdempotent | none / 1 | Repeated failure callback creates one terminal outcome/notification and retains the original diagnostic summary. |
| MigrationPreservesLegacyRows | none / 1 | Owned DB migrated to immediate predecessor; insert a real legacy request, migrate forward, assert unchanged GUID/progress/approval and null provenance/witness. Round-trip new fields and verify concurrency remains a GUID. EnsureCreated alone is insufficient. |
| RolledBackWriterIsNotCommitted | none / 1 | Abort transaction after SaveChanges: fresh observer retains prior stamp/token, and no committed-write log receipt is emitted. |

Proposed V-4 total: `2+6+4+1+1+1+1 = 16` results. No test merely compares a provenance helper to itself. Actual changed rows and the service's emitted summary are the verdicts.

### V-5: narrow Windows native row

New `AgentTaskLandHalfResetWindowsTests` contains four single-result methods: `C883_LinkedWorktreeWithSpacesRecovers` (fresh-ID cut/repair under native Windows Git, exact registered path); `C883_StagedBlobSurvivesNativeIndex` (index-only edit survives); `C883_BuiltinCrLfRecoveryPreservesRealEdits` (ordinary core.autocrlf checkout at L repairs successfully; a raw-byte deviation is source_dirty and preserved even if normalized Git diff is empty); `C883_CaseAliasCannotChangeRegisteredIdentity` (mismatched registration/case alias cannot redirect reset to another tree). Use the same owned Git/schema fixture and no ConPTY/provider. Four executions, no skips on Windows. CP-5 alone requires Windows; do not select that class on Linux. A wrong-host invocation refuses the fixture instead of silently producing passing empty tests.

### R-1..R-5: independently recounted existing regression census

The read-only PowerShell census examined every matching source file, matched each `[Test]` method, counted its adjacent `[Arguments]` (or one when absent), and separately searched for Skip, SkipUnless, MethodDataSource, ClassDataSource, Matrix, platform branches and partial class declarations. These five classes are nonpartial, with no method generators/OS skips in the selected source. Runtime executed counts remain unmeasured until a fresh TRX exists. No count below was copied from another plan.

| ID / class | Source methods | Expanded existing results Linux / Windows | Count derivation in source order |
|---|---:|---:|---|
| R-1 AgentTaskLandAdoptionTests | 9 | 15 / 15 | `2+3+1+1+1+1+1+1+4` |
| R-2 AgentTaskLandSourcePersistenceTests | 9 | 35 / 35 | `3+8+3+1+10+4+3+2+1` |
| R-3 AgentTaskLandFailureDiagnosticTests | 7 | 25 / 25 | `8+2+3+1+4+2+5` |
| R-4 AgentTaskLandMonitoringTests | 8 | 25 / 25 | `4+4+4+4+1+1+5+2` |
| R-5 LandingGitTests | 22 | 55 / 55 | `2+4+1+1+1+7+4+4+2+3+7+3+4+1+4+1+1+1+1+1+1+1` |

Total existing selection: `9+9+7+8+22 = 55` source methods; `15+35+25+25+55 = 155` expanded results per OS; static skip count 0. Existing same-request non-ancestor recovery stays green. Complete its synthetic interrupted-state fixture with the actual durable pin setup that production performs before update-ref, preserving its outcome/assertions and result count. That fixture-only edit is explicitly in S1's footprint.

Recount recipe (read-only PowerShell, with `$names` set to the five exact classes above): enumerate `$name.cs` and `$name.*.cs`; apply regex `\[Test\]([\s\S]*?)(?:public|internal)\s+(?:async\s+)?(?:Task(?:<[^>]+>)?|void|ValueTask)\s+(\w+)\s*\(`, then count `\[Arguments\(` in capture 1 with minimum one. Print each method and count, then inspect data generators/conditional skips manually. This recipe is specific to these source shapes, not a general C# parser. Re-run against the actual post-CARD-0835 source and every proposed test file before committing a final TestDesign census. New roster cardinalities above are specifications derived from their enumerated rows, **not source-measured existing tests**; Code must reconcile them with generated executions before relying on Min.

### Positive controls and named red assertions

These are mutation-stage controls over production behavior. The exact historical cut is additionally executed test-first by CP-1. Every PC requires a clean baseline, named assertion red, and restored green; build errors, fixture errors, zero discovery, cancellation, or harness timeout do not count. Use only the listed detecting methods (parameterized method suffix `*`), not a whole class. Controls sharing a file/method run separately; restore timestamps/rebuild to avoid a stale mutated DLL. All timing is barrier/FakeTimeProvider-controlled as above.

| PC | Concrete mutation | Named detecting test and required red assertion |
|---|---|---|
| PC-1 | Restore fresh-request gate requiring the new request's own source-adopt-reset field. | `AgentTaskLandAdoptionConcurrencyTests.C883_Save409FaultThenFreshRequestCompletes`: second request's **publication confirmed** assertion fails with source_dirty; injection and half-state assertions must already have passed. |
| PC-2 | Delete the cached-index equality guard. | `AgentTaskLandHalfResetTests.C883_StagedEditSurvivesFreshAndSameRequest`: expected source_dirty or preserved staged-blob assertion fails. |
| PC-3 | Delete worktree equality guard while keeping index comparison. | `AgentTaskLandHalfResetTests.C883_UnstagedEditSurvivesFreshAndSameRequest`: preserved edit / refusal assertion fails. |
| PC-4 | Treat nonempty untracked/ignored lists as empty (one guard mutation per argument). | `AgentTaskLandHalfResetTests.C883_UntrackedAndIgnoredArePreserved`: sentinel bytes or zero-reset assertion fails. |
| PC-5 | Accept a witness with mismatched identity, or synthesize L from arbitrary ancestry when none exists (separate mutations). | `C883_FreshRequestRejectsWitnessIdentity` / `C883_FreshRequestRequiresInterruptedWitness` in AgentTaskLandHalfResetTests: rejected decision and zero-reset assertions fail. |
| PC-6 | Remove fresh-request ancestor requirement. | `AgentTaskLandHalfResetTests.C883_FreshRequestRequiresAncestor`: no-reset assertion fails; existing same-request rewrite is the compatibility contrast. |
| PC-7 | Bypass unsafe index flags, sequencer, gitlink or custom-filter guard (separate mutations). | `LandRecoveryCheckoutTests.C883_UnsafeIndexModesRefuse`, `.C883_SequencerRefuses`, `.C883_SubmoduleAndFilterRefuse`: production inspection's **Accepted == false** assertion fails; no external filter executes. |
| PC-8 | Interpret inspection error exits as successful empty probes. | `LandRecoveryCheckoutTests.C883_ProbeErrorsRefuse`: rejected/unknown result assertion fails for the named argument. |
| PC-9 | Remove final identity or approval/remote recheck (separate mutations). | `AgentTaskLandHalfResetTests.C883_IdentityChangesBeforeResetRefuse` / `.C883_AuthorityChangesBeforeResetRefuse`: zero reset/publication assertion fails after the controlled change. |
| PC-10 | Put the naked request SaveChanges back after CAS and before reset. | `AgentTaskLandAdoptionConcurrencyTests.C883_MonitorBetweenMoveAndResetDoesNotPreventReset`: reset-completed/no-LandRefused assertion fails after the real monitor token write. |
| PC-11 | Move the pre-intent checkpoint after CAS. | `AgentTaskLandAdoptionConcurrencyTests.C883_PreIntentConflictLeavesCheckoutUnmoved`: HEAD==L / zero-CAS assertion fails. |
| PC-12 | Allow fresh recovery to bypass uncertain prior child or pending-request authority (separate mutations). | `AgentTaskLandHalfResetTests.C883_UncertainPriorChildRefuses` / `.C883_CurrentPendingRequestCannotBeStolen`: no-reset/existing-admission-conflict assertion fails. No real process is killed. |
| PC-13 | Omit the conflicting entry's safe key/token summary. | `LandRequestWriteDiagnosticTests.C883_ConflictNamesActualEntityAndTokens`: exact entity/key/original/database-token assertion fails. |
| PC-14 | Trust a writer label without checking LastWriterToken against the stored token. | `LandRequestWriteDiagnosticTests.C883_MissingOrStaleWriterIsUnknown` (token-mismatch): expected unknown fails. |
| PC-15 | Append hostile exception/entry text to the persisted summary. | `LandRequestWriteDiagnosticTests.C883_SecretPayloadIsAbsent`: forbidden marker absence assertion fails in terminal event/notification/log. |
| PC-16 | Emit a committed-write receipt immediately after SaveChanges, before transaction commit. | `LandRequestWriteDiagnosticTests.C883_RolledBackWriterIsNotCommitted`: committed receipt count must be zero after rollback and fails. |
| PC-17 | Give migrated legacy rows an invented writer/token stamp. | `LandRequestWriteDiagnosticTests.C883_MigrationPreservesLegacyRows`: null legacy-provenance assertion fails; exercise a real predecessor upgrade. |
| PC-18 | Remove the raw-byte comparison and trust normalized diff alone. | `LandRecoveryCheckoutTests.C883_BinaryEolAndModeComparison` (eol) and Windows `AgentTaskLandHalfResetWindowsTests.C883_BuiltinCrLfRecoveryPreservesRealEdits`: expected source_dirty/preserved raw bytes assertion fails on a normalized-but-different checkout. |

A PC listing multiple guards/tests is a set of separate subcontrols, each reported by mutation/argument; it is not permission to count one unrelated failure as all guards detected. TestDesign should split IDs if its execution tooling requires one mutation per ID. New tests without a reachable assertion red return to Code.

### Known flakes and exclusions

Track the supplied known-flake list explicitly: **CARD-0791, CARD-0794, CARD-0818, CARD-0820, CARD-0828, CARD-0848, CARD-0878, CARD-0879**. Also: `ScaledTimeProviderTests.Speed_10` (CARD-0757), `HttpResilienceRegistrationTests.Runner_list_and_git_connectivity_keep_their_short_deadlines` (CARD-0751), and `ResilienceBudgetTests.Slow_first_attempt_consumes_the_same_budget` (CARD-0820). Their present board disposition was not queried. CARD-0878's Linux native-session classification skip is visible in `HerdrAlwaysOnChannelParityTests.StandingRecovery.cs:141` and is outside this selection.

Do not add those unrelated classes to the land filters or claim they passed. No named existing class here has a static OS skip. A selected failure is investigated on its own evidence; it is not excused by a neighboring flake card. A justified rerun uses the exact failing method once and records source SHA, original failure, rerun count and outcome. No full Unit/namespace/provider/Herdr/checkpoint-tool suite is commissioned. A control-plane timeout/slot refusal is infrastructure evidence, never a passing test or a successful positive control.

### Checkpoints

This is the **closed list**. CP-1 is a deliberate single-test baseline red after the minimal S1-red fixture/seam/test commit and before any repair/order implementation; CP-2..CP-4 are the final Linux group after committed S1-S4; CP-5 is the Windows-only native group at the same final tested SHA. Commit/push before **every** row. Each row has exactly one isolated build and one filter. Do not insert extra builds, broaden filters, or reuse a dirty receipt; any necessary roster/footprint change is a committed manifest amendment with a stated reason. Table pipes are Markdown-escaped; the command argument contains ordinary `|`, never `\|`.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---:|---:|---|---|
| CP-1 | S1-red | `tests/Antiphon.Tests -> bin-c883-red/` | historical-save-red | `/*/*/AgentTaskLandAdoptionConcurrencyTests*/C883_Save409FaultThenFreshRequestCompletes*` | V-2 baseline | Linux 1 executed, 0 passed, exactly 1 assertion failure at fresh-request publication, 0 skipped, driver exit 1; Windows 0 selected, not commissioned | 1 | 8 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-2 | S1-S4 | `tests/Antiphon.Tests -> bin-c883-recovery/` | recovery-and-ordering | `/*/*/(AgentTaskLandHalfResetTests*)\|(AgentTaskLandAdoptionConcurrencyTests*)\|(AgentTaskLandAdoptionTests*)/*` | V-1,V-2,R-1 | Linux 48 executed = 26+7+15, 0 failed/skipped; Windows 0 selected, not commissioned | 48 | 15 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-3 | S1-S4 | `tests/Antiphon.Tests -> bin-c883-git/` | checkout-proof | `/*/*/(LandRecoveryCheckoutTests*)\|(LandingGitTests*)/*` | V-3,R-5 | Linux 79 executed = 24+55, 0 failed/skipped; Windows 0 selected, not commissioned | 79 | 10 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-4 | S1-S4 | `tests/Antiphon.Tests -> bin-c883-diagnostics/` | writer-and-persistence | `/*/*/(LandRequestWriteDiagnosticTests*)\|(AgentTaskLandSourcePersistenceTests*)\|(AgentTaskLandFailureDiagnosticTests*)\|(AgentTaskLandMonitoringTests*)/*` | V-4,R-2,R-3,R-4 | Linux 101 executed = 16+35+25+25, 0 failed/skipped; Windows 0 selected, not commissioned | 101 | 15 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-5 | S1-S4 | `tests/Antiphon.Tests -> bin-c883-windows/` | windows-checkout | `/*/*/AgentTaskLandHalfResetWindowsTests*/*` | V-5 | Windows 4 executed, 0 failed/skipped; Linux 0 selected, do not run | 4 | 8 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |

## Execution, activation and rollback

Test-first order: commit the exact-cut seam, minimal fixture and C883_Save409FaultThenFreshRequestCompletes while retaining baseline behavior; run CP-1 and require the named red. Then add preservation tests before implementing S1, ordering/race tests before S2, diagnostics/upgrade tests before S3, and native tests before S4. Commit/push each substantial slice; after all source is committed run CP-2, CP-3, CP-4 serially on server2. Run CP-5 on Windows only. This stage has run **none** of these rows; CP-1 is not being reported as an observed red.

The task-specific instruction selects **scripts/run-checkpoint.ps1 directly** because the checkpoint tool can refuse owner-unverified without the task token (CARD-0853). This is the authorized exception to the generic CARD-0723 tool front door. Do not bootstrap the tool or change ownership settings. The script self-leases the host build slot and performs isolated `dotnet build` followed by TUnit `dotnet run --project tests/Antiphon.Tests --no-build ... -- --treenode-filter ...`; never dotnet test, never a second lease wrapper around this script, never -NoSlot. Report exit 4 without running unleased; a receipt that says unleased does not satisfy this brief's host-slot requirement. Linux's UseAppHost=false default remains unchanged.

Future commands, from the exact committed source root (not executed in Plan):

```powershell
$env:TUNIT_MAX_PARALLEL_TESTS = '1'
# At S1-red only; require the specified assertion failure, not build/fixture failure.
pwsh -NoProfile -File scripts/run-checkpoint.ps1 -Name CP-1 -Project tests/Antiphon.Tests -OutputPath bin-c883-red/ -Filter '/*/*/AgentTaskLandAdoptionConcurrencyTests*/C883_Save409FaultThenFreshRequestCompletes*' -MinExecuted 1 -Expect AgentTaskLandAdoptionConcurrencyTests.C883_Save409FaultThenFreshRequestCompletes -ResultsRoot .antiphon/c883-checkpoints
# At final S1-S4, Linux, serially:
pwsh -NoProfile -File scripts/run-checkpoint.ps1 -Name CP-2 -Project tests/Antiphon.Tests -OutputPath bin-c883-recovery/ -Filter '/*/*/(AgentTaskLandHalfResetTests*)|(AgentTaskLandAdoptionConcurrencyTests*)|(AgentTaskLandAdoptionTests*)/*' -MinExecuted 48 -Expect AgentTaskLandHalfResetTests,AgentTaskLandAdoptionConcurrencyTests,AgentTaskLandAdoptionTests -ResultsRoot .antiphon/c883-checkpoints
pwsh -NoProfile -File scripts/run-checkpoint.ps1 -Name CP-3 -Project tests/Antiphon.Tests -OutputPath bin-c883-git/ -Filter '/*/*/(LandRecoveryCheckoutTests*)|(LandingGitTests*)/*' -MinExecuted 79 -Expect LandRecoveryCheckoutTests,LandingGitTests -ResultsRoot .antiphon/c883-checkpoints
pwsh -NoProfile -File scripts/run-checkpoint.ps1 -Name CP-4 -Project tests/Antiphon.Tests -OutputPath bin-c883-diagnostics/ -Filter '/*/*/(LandRequestWriteDiagnosticTests*)|(AgentTaskLandSourcePersistenceTests*)|(AgentTaskLandFailureDiagnosticTests*)|(AgentTaskLandMonitoringTests*)/*' -MinExecuted 101 -Expect LandRequestWriteDiagnosticTests,AgentTaskLandSourcePersistenceTests,AgentTaskLandFailureDiagnosticTests,AgentTaskLandMonitoringTests -ResultsRoot .antiphon/c883-checkpoints
# Same final SHA, Windows only:
pwsh -NoProfile -File scripts/run-checkpoint.ps1 -Name CP-5 -Project tests/Antiphon.Tests -OutputPath bin-c883-windows/ -Filter '/*/*/AgentTaskLandHalfResetWindowsTests*/*' -MinExecuted 4 -Expect AgentTaskLandHalfResetWindowsTests -ResultsRoot .antiphon/c883-checkpoints
```

Inspect each driver's exit before starting the next command; preserve full CHECKPOINT/FAILED/EXECUTED output and fresh TRX. Report CP-n with commit, OS, build disposition, exact filter, executed/passed/failed/skipped and reruns. A floor alone is insufficient: compare the full class/method/argument roster with the table. CP-1's one expected assertion failure is intentional test-first evidence; every final row must be green. Missing Windows evidence stays pending, not a Linux skip or a passed cross-platform claim. Any CARD-0835 seed/census adjustment must preserve clean-source Review assertions.

The explicitly listed non-checkpoint authoring operation is CLI migration generation for S3: first `dotnet tool restore`, then `dotnet ef migrations add AddLandRequestWriterProvenance --project server`, using `scripts/build-slot.ps1 -Label c883-migration -- <command>` for the build-driving command. Record it with reason **schema generation**, not another verification row. No handwritten migration/Designer/snapshot changes. No extra solution build, client bundle, deployment image build, or checkpoint-tool bootstrap is needed. Mutation later uses the same direct checkpoint script for the exact named PC methods, outside the ordinary Code rows, and reports its baseline/red/green evidence separately.

Activation is a later caller/operator action after Code, Review and mandatory source-owner land ordering. Additive nullable provenance columns deploy before binaries that reference them; old rows/binaries yield unknown provenance rather than an invented writer. The source recovery code needs no data backfill, automatic repair job or runner upgrade. From the main checkout, use the canonical AppHost restart path after updating to the landed source; read the Job Object/current-checkout caveat, and confirm `/api/version` full SHA and land-v2 directly. Do not restart from this worktree or infer activation from health. A worktree branch push is publication of this plan only.

After activation, use the read-only observation recipe for the next independently requested recovery. Confirm fresh request ID, old witness, aligned tree, terminal publication and diagnostic fields; do not manufacture a half-reset on a live owner. If the tree contains real work, retain source_dirty and route it for inspection. No manual reset is commissioned by this plan.

Rollback: roll forward with a reviewed revert of this feature's behavior on the shared FF-only workflow, retaining nullable columns and recovery pins/intent. Do not run a destructive Down migration or blanket reset worktrees. The previous binary safely ignores the new columns but has the old fresh-request limitation; explicitly hold affected recoveries for inspected disposition. Preserve the old diagnostic/request/event evidence and any pending operation. Publication already confirmed is not undone; cleanup remains separately authorized.

### Cost

Plan performed read-only source inspection/census and Markdown commits/pushes only: 0 builds, 0 tests, 0 live mutations. Verification estimates are planning budgets, not measurements. CP-1..CP-5 sum to **8+15+10+15+8 = 56 minutes**: Linux 48 minutes including the intentional red, Windows 8 minutes. Final green scope alone is 48 minutes (Linux 40 + Windows 8). Expected final unique results are `48+79+101+4 = 232`; the intentional CP-1 repeat makes `233` planned executions across both phases, with one deliberate pre-fix assertion red and zero final failures/skips. No skipped-result count is used as executed coverage.

Allow approximately 120-180 minutes for implementation/fixture authoring plus the checkpoint budget, separate from schema-generation/slot waits and CARD-0835 serialization. Split the Code commission at committed slice boundaries if its task ceiling requires it; do not omit rows to fit a deadline. Mutation/Review cost is additional and depends on TestDesign's subcontrol split and actual build timings; PC labels are not build counts. The conservative ignored/sparse/filter refusals trade some automatic repairs for preservation of unproven work. The additive diagnostic schema and actual writer coverage are the main integration cost.

Next stage: **TestDesign**. Validate the exact-cut interceptor-to-boundary transition, freeze the proposed rosters against the source that will follow CARD-0835, resolve the controlled held-writer interleaving without weakening admission, and preserve the explicit historical-attribution limit. Only this plan is delivered; Code is not yet implemented or activated.
