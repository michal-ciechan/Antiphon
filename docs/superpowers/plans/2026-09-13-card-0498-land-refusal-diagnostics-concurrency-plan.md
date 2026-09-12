# CARD-0498: Preserve land source evidence and explain failed landings

Status: Plan complete; TestDesign is the next stage. Runtime implementation and verification remain pending.

Authoring baseline: `0b0f62e0`. Plan task: `5a7b92a5`. Investigation: `34665810`. Card: `CARD-0498` (`65a54e96-6a8f-4992-89fc-5c7631c446bd`, Antiphon board).

## Outcome and scope

A monitor pass during source inspection must not make a valid land fail or erase its observed source snapshot. If land execution does throw, the request must retain a specific, bounded failure code and diagnostic identity, including when no landing operation exists. HTTP task detail, `delegate.ps1 -Status`, and the durable outcome notification must expose that evidence.

The card and its revision history, and the full Investigate report, were read before choosing this design. The investigation establishes three `DbUpdateConcurrencyException` failures at the resolver's Observed snapshot save: task `2cca15a0` twice and task `3fcd2c22` once. Use this finding as the starting point; do not reopen the Review-gate hypothesis. `3fcd2c22` was TestDesign, despite the card describing it as Plan.

This card covers resolver/request persistence, exception diagnostics, conservative Git inspection diagnostics, and CLI presentation. It preserves explicit full-SHA caller approval without Review evidence, the null merge target default of `refs/heads/master`, the asynchronous land acknowledgment, publication evidence, repository leases, source-child journals, and conservative cleanup. It does not retry historical incidents, change approval policy, broaden Git publication behavior, or introduce a new notification transport.

Owners: [project conventions](../../project-context.md), [orchestration](../../orchestration-loop.md), [HTTP operations](../../ops-http.md), [testing/build](../../testing-and-build.md), [credential custody](../../agent-credentials.md). Read [session-runtime invariants](../../session-runtime-invariants.md) before modifying notification production. Queue delivery and transcript confirmation need no behavioral change.

## Ground truth

| Card premise or possible shortcut | Confirmed behavior at the authoring baseline | Design consequence |
|---|---|---|
| Plan/non-reviewed lands are rejected by a new Review gate. | `AgentTaskLandService.RequestAsync` accepts `ExplicitCaller` approval; Review validation runs only for supplied evidence. The three failures were EF concurrency exceptions. | Add Plan and TestDesign regression cases; add no role exemption or mandatory Review requirement. |
| Missing merge target explains the incidents. | Admission snapshots and resolver coordinates default to `refs/heads/master`. | Exercise null targets through admission and execution. |
| Every named source refusal loses its reason. | `SourceRefusalReason` reaches `LandRequestStatusDto.From`; `FormatSourceRefusal` and `LandNotificationPayload` carry named refusal evidence. | Preserve these paths. Fix the exception path and missing CLI fields. |
| The monitor only writes when a warning fires. | `AgentTaskLandMonitorService.SweepAsync` updates `LastEvaluatedAt` and rotates the request token every pass, under an `AgentTasks` row lock. | A genuine competing write must remain in the regression test, including below warning thresholds. |
| The resolver's transaction prevents the race. | `SaveRequestAsync` saves its tracked request after Git work without acquiring the monitor's lock or refreshing its original token. | Coordinate the short save transaction; preserve the already computed patch before reloading. |
| Saving Observed is the only exposed write. | Resolver child checkpoints, refusals and Resolved use the same helper. Operation creation attaches request/task outside that helper; service fallback source reasons also save directly. | Cover all these handoffs, not only the incident line. |
| Failure handling retains the thrown error. | `FailRequestAsync` clears tracking and reloads, but discards its exception argument and persists generic `landing_failed` prose before publication. | Put typed failure evidence in the terminal transaction, after reload. |
| `-Land` can print an eventual refusal immediately. | `-Land` returns a 202 queue acknowledgment; later outcomes arrive through notifications/status. | Keep acknowledgment semantics. Improve the durable outcome and `-Status`; add no poll/wait mode. |
| The Git result contains safe stderr ready to print. | `LandingGit.ExecuteAsync` drains and deliberately discards stderr; its `Diagnostic` is a generated `git_exit_N`, not stderr. Inspection further reduces command results to reason codes. | Carry safe command/exit/type metadata. Do not resurrect raw stderr or label `Diagnostic` as stderr. |
| A healthy current server proves this source is active. | Investigate recorded different loaded and checkout SHAs. | Code activation must be verified separately after publication; this plan makes no live-runtime claim. |

## Decisions

### D-1. Use the existing task-row lock for short source checkpoints

Choose coordinated writes over automatic replay. Introduce a small concrete `AgentTaskLandRequestWriter` in `server/Application/Services/`, using the existing `AppDbContext` directly. It centralizes task-first locking and guarded source-checkpoint application. It is not a repository abstraction or a process-wide mutex. Keep external I/O in existing Git seams and EF types out of Domain.

The resolver must compute a typed, explicit source patch outside the database transaction. Capture its persisted baseline before modifying tracked state: request ID/task ID, attempt, approval identity, source coordinates, source-resolution fields and active operation binding. Prefer explicit patch records at call sites to deriving arbitrary entity modifications. A patch can explicitly clear a field; null must not ambiguously mean both clear and leave alone.

For every checkpoint:

1. Begin a short transaction and acquire `SELECT 1 FROM "AgentTasks" WHERE "Id" = ... FOR UPDATE`, matching the monitor and settlement lock order. If a transaction is already owned by the caller, use it without nesting or prematurely committing it. No Git command, child wait, verifier, event-bus publication or repository-lease acquisition runs inside this transaction.
2. Reload both the task and request under the lock. Retain the typed patch and baseline outside the EF objects so reload cannot discard the pending observation. Do not call `ChangeTracker.Clear()` here or mark a whole entity Modified.
3. Revalidate current request ID, `IsPending`, absent terminal event, eligible task state, request/task attempt and requested-at mirrors, and unchanged approval and coordinates. Approval identity comprises schema, expected SHA, evidence ID, approval kind/time, verify filter and the four coordinate snapshots. A different request, attempt, canceled/terminal request or changed approval is stale work: return an explicit `StaleRequest` disposition and stop this invocation without Git continuation or settlement of the replacement.
4. Compare the freshly persisted resolver-owned source state and operation binding with the captured baseline. Monitor clocks, reconciliation fields and warning/error changes are compatible; a newer source observation, child checkpoint, resolution state or operation is not. If the same current attempt has incompatible source progress, stop with a bounded `source_resolution_state_changed` failure while retaining the newer evidence. Never apply the stale patch. This is an unexpected internal conflict, not permission to rerun resolution.
5. Apply only the resolver-owned patch, rotate its token, and advance `LastProgressAt` using the checkpoint time without moving it backward. Preserve monitor-owned `LastEvaluatedAt`, reconciliation, warning/error fields and hold information. Save and commit before performing the next dependent action. Refresh the local baseline from the committed values.

Resolver-owned fields are `SourceResolutionState`, the local/remote/candidate/resolved SHAs, source relationship, remote ref/fingerprint/observation ref/time, source filesystem identity, source refusal/diagnostic fields and source-advance child fields. Operation attachment and progress clocks are explicit coordinated transitions, not unrestricted members of a generic patch. Approval, attempts, request lifecycle and terminal identity are never copied from a stale resolver object.

Apply the helper to Observed, AdvanceStarted, child started/drained, Resolved, and both refusal helpers. A failed checkpoint must stop dependent source fast-forward or operation creation. A stale/conflicted checkpoint inside the owned-child start callback must fail that callback so the existing owned-process exception/await/drain path runs; it cannot return normally and allow the child to proceed with unacknowledged custody. Existing owned-child handling remains responsible for an already launched child and its journal; this change cannot bypass that contract.

Preserve `LastEvaluatedAt` semantics in the monitor. Removing token rotation while still changing fields defeats optimistic concurrency; omitting evaluation timestamps changes existing status/monitoring behavior. Reducing genuinely redundant writes may be a later optimization, but is not the correctness fix here. The deterministic test must pass even when the monitor really commits a new token.

Rejected: a lock held across Git inspection; an in-process semaphore that excludes neither a second server nor recovery; removing the concurrency token; unbounded retries; catching every EF error and replaying `ResolveAsync`; or reloading and copying all stale current values back onto the entity. A narrowly guarded retry could work, but the existing shared row-lock convention gives a simpler single-attempt checkpoint contract and covers threshold-writing monitors too. An unexpected remaining `DbUpdateConcurrencyException` becomes D-3 evidence, not success or a hidden retry.

### D-2. Close the adjacent request-write and stale-settlement gaps

`CreateOperationAsync` must prepare its Git-derived candidate outside the transaction, then acquire the same task lock, reload request/task/previous operation, and revalidate the captured approval, source state and active-operation identity before attaching it. Replace the previous operation, insert the new operation and update task/request bindings atomically. Preserve the existing ordering needed by the unique active-operation constraint, rotating changed tokens. If another operation is now bound, abandon the stale candidate; reuse it only through the established identity-validated resume path. Never attach an operation to a replaced or terminal request.

Remove the two direct source-reason saves in `RunRequestAsync`. A source refusal returned by the resolver or its operation-creation helper must carry a typed reason and optional safe diagnostic into guarded persistence. A late fallback reason must not overwrite existing source evidence or close a new request. `StaleRequest` is a distinct resolver result from a current request's actual refusal; the caller must not route it through `PersistRefusalAsync`.

Failure and refusal settlement must carry the originally executing request identity through to the transaction. Checking `requestId` only before taking the lock is insufficient. Capture an immutable execution identity when the attempt is admitted, before Git work, and retain it in the scoped service/explicit failure argument for the hosted catch; do not infer the failed attempt from the latest row during recovery. Direct failure callers without that execution context must capture the current identity before their failure transaction and revalidate it under the lock. Clear failed EF tracking before loading durable failure state, acquire the task lock, then reload and check the expected request and execution attempt again. Factor the terminal transaction as needed so publication classification, diagnostic persistence, terminal event, outbox row and `ClearPending` use that same locked state. Do not nest existing settlement transactions or reload away a just-assigned diagnostic.

Check affected recovery/admission call sites when adopting the helper. Existing admission, cancellation, hold, protocol `TransitionAsync`, and settlement locks remain; preserve their behavior. The recovery sweep's task restart-warning/reset write must also reload and recheck the expected pending request under the task lock before saving, because operation creation now deliberately relies on that common writer order. This is a focused writer audit, not a general protocol rewrite.

Lock order remains repository mutation lease (where already held), task row, then request/operation/notification writes. Monitor and terminal persistence never acquire the repository lease while holding a task lock. Audit and test this order to avoid introducing a resolver/monitor deadlock.

### D-3. Persist bounded failure evidence on the land request

Add the following nullable columns to `AgentTaskLandRequest`, map lengths in `AppDbContext`, and generate the migration and model snapshot with the EF CLI. Additive diagnostics do not change approval schema version 2 or its check constraints.

| Field | Bound / meaning |
|---|---|
| `TerminalFailureCode` | 100 ASCII characters; stable classifier for an unexpected terminal execution failure. |
| `FailureDiagnosticId` | Nullable GUID; identifies this handled failure across persisted request, event, notification and server log. |
| `FailureExceptionType` | 200 characters; simple exception type name, restricted to identifier characters; no message, stack or assembly-qualified generic arguments. |
| `SourceDiagnosticCommand` | 160 characters; fixed command template/operation label, not arbitrary argv or a repository path. |
| `SourceDiagnosticExitCode` | Nullable integer; only an actually returned process exit code. |
| `SourceDiagnosticCode` | 100 ASCII characters; generated safe code such as `git_exit_128`, `git_start_failed`, or `stderr_suppressed`. |
| `SourceDiagnosticExceptionType` | 200 characters; same type-name contract for a handled inspection I/O failure. |

Introduce a bounded classifier/formatter (`LandFailureDiagnostic` in Application, and an EF-free value shape if Domain needs one). Map known exception categories by type, never by arbitrary message matching:

| Condition | `TerminalFailureCode` |
|---|---|
| `DbUpdateConcurrencyException` | `landing_concurrency_conflict` |
| Guarded source baseline conflict, D-1 | `source_resolution_state_changed` |
| Other `DbUpdateException` | `landing_persistence_failed` |
| `TimeoutException` | `landing_timeout` |
| `IOException` | `landing_io_error` |
| `UnauthorizedAccessException` | `landing_access_denied` |
| Other unexpected exception, including cancellation without host shutdown | `landing_unexpected_exception` |
| Exception after durable publication | `landing_interrupted_after_publication` |

The exception type supplies detail without claiming a false specific cause. The concurrency code does not assert which tracked entity conflicted: the failing operation can include request, task or operation writes. Host shutdown cancellation retains the existing hosted-service exit/recovery behavior and creates no new terminal failure. Do not inspect arbitrary `Data`, SQL, inner messages, exception `ToString()` or stack text for public diagnostic content.

Generate the diagnostic ID once when the failure is handled, before the persistence attempt. Log it with task ID, expected request ID, attempt, exception type, code and available durable operation ID/phase, so it is useful even if terminal persistence fails. The standard log entry should use these bounded fields; do not expand raw exception messages into the new public path. Direct `FailAsync`/`FailRequestAsync` callers must get equivalent evidence, not depend on a hosted-service-only formatter. If a persistence error is logged too, use the same diagnostic ID and a distinct persistence-error type.

Apply diagnostics inside the locked terminal transaction after all reloads. For an unpublished failure, the request, one `LandRefused` terminal event, its warning, pending-marker clearing and one Outcome outbox record commit together. Preserve any already durable observed snapshot and ordinary `SourceRefusalReason`; do not set a fake source refusal for an execution exception. Do not manufacture SHAs or an operation from in-memory state whose save failed. If the database cannot commit, the request stays pending with no asserted terminal outcome; existing recovery retries apply and the bounded correlated log records the failed persistence attempt.

For a published operation, retain the existing publication/cleanup result and `landing_interrupted_after_publication` semantics. Attach diagnostic evidence to the request and its existing outcome shape without turning it into `LandRefused`, clearing verified/remote evidence, downgrading completed cleanup, or authorizing deletion. An already committed terminal event wins: a repeated failure callback must not replace its diagnostic, emit another outcome, or mutate an old outbox body/digest. A fresh explicit request gets fresh nullable fields; historical opaque records remain unknown rather than being backfilled from guessed log matches.

Rejected: only improving the log line; putting failure solely on a landing operation that may not exist; replacing ordinary source codes with generic exception codes; changing the delegate task's `Succeeded` status; or persisting unbounded exception messages.

### D-4. Carry safe Git inspection metadata without restoring stderr

Extend `LandSourceInspection` with an optional typed diagnostic, retaining its existing two-argument construction and `Accepted`/`Reason` semantics. Command failures directly available in `InspectAsync` (`status`, ignored-file listing, identity probes) carry a fixed command template, actual exit code and generated `git_exit_N`. For `RequiredAsync` failures used by identity inspection, a small `IOException` subtype carrying only that safe metadata can preserve the command identity through the current `identity_io_error` catch. Existing callers catching `IOException` continue to work.

Use fixed labels such as `git status --porcelain=v1`, `git ls-files --others --ignored`, `git rev-parse <identity>`, `git worktree list`, `git check-ref-format <ref>` and `filesystem canonicalization`. Dynamic refs, paths, endpoint strings, configuration values, stdout and arbitrary exception messages are omitted. Filesystem errors have no invented command exit code. Plain semantic refusals such as `source_dirty`, `wrong_repository` and `active_sequencer` need no synthetic Git diagnostic.

Carry the optional diagnostic from every resolver-consumed inspection, including the fresh inspection in operation creation, into the request's source-diagnostic columns with its actual refusal reason. Where resolver commit lookup already has a `LandingGitResult`, retain its safe command/exit metadata on `commit_lookup_failed`. A locally available typed destination exception may also pass through safely. Do not rewrite remote observation, push, verifier, cleanup or all Git result types to obtain more detail.

`LandingGit.ExecuteAsync` currently discards stderr intentionally. Keep that policy. D-4 does not expose raw or truncation-only stderr: truncation does not remove secrets. It also does not install a broad regular-expression scrubber and assume arbitrary hook output is safe. `SourceDiagnosticCode` is explicitly a generated diagnostic, never described as captured stderr; status can state that stderr was suppressed. A future richer stderr feature needs its own safe-content contract. This conservative limit still makes `status_error` distinguishable from a path-access exception and identifies the failing command and exit where those facts exist.

Bounds and identifier/template allowlists are enforced at production, not only by database max lengths. Test arbitrary exception messages, credential-shaped URLs, query values, line breaks, terminal escape characters and oversized diagnostics using synthetic markers. None may reach public DTO/event/notification/CLI output or the new correlated log fields.

### D-5. Expose existing and new evidence consistently

Append optional/defaulted fields to `LandRequestStatusDto` and its mapper; mirror them as optional fields in `client/src/api/agentTasks.ts`. Existing task GET remains the HTTP surface. Do not add a separate failure endpoint, new status column, or client UI feature.

`delegate.ps1 -Status` prints these conditional lines before the delegate report:

```text
Candidate source: <candidateSourceSha>
Source refusal: <sourceRefusalReason>
Land execution failure: <terminalFailureCode>; diagnostic <failureDiagnosticId>; exception <failureExceptionType>
Source inspection: <sourceDiagnosticCommand>; exit <sourceDiagnosticExitCode>; diagnostic <sourceDiagnosticCode>
Source inspection exception: <sourceDiagnosticExceptionType>
Landing reason: <landing.reason>
```

Print the operation's existing `reason` independently of source/exception fields, including on publication with residue. Missing optional fields from an older server are tolerated and do not print false failure lines. Keep approval, observed/resolved SHA, hold, reconciliation and receipt lines. The CLI must not infer publication from a reason, Succeeded delegate status, or all-null SHA fields.

For new terminal events, format the same bounded code, ID and type from the request plus already durable source SHA evidence. `LandNotificationPayload` snapshots those facts through the terminal event detail in the existing atomic outbox path. Normal source refusal detail gains safe command metadata when present. The notification header, source event linkage, publication/cleanup fields, content digest, delivery retry and transcript receipt semantics remain unchanged. No direct send or new polling loop is introduced. `ReplyTo=None` still creates its normal NotRequired receipt state and exposes diagnostics through task GET/status.

Example for a failure before any operation exists:

```text
land unconfirmed: landing_concurrency_conflict; diagnostic=<guid>; exception=DbUpdateConcurrencyException;
expected=<approved SHA>; local=null; remote=null; candidate=null
```

This is evidence that land execution failed before a durable source snapshot, not a claim that the local target never changed. Existing source refusals retain their existing code; a handled `status_error` is not relabeled as an unexpected exception.

Rejected: parsing historical event prose back into typed state; rewriting immutable existing outbox payloads; waiting for execution in `-Land`; or hiding the operation reason whenever a request reason is present.

## Implementation slices

Each slice includes its implementation tests. TestDesign adds the exact verification design and mutation controls to this same document before Code starts. Suggested new filenames below are deliberate seams, not a requirement to create duplicate frameworks.

| Slice | Production files / change | Tests and completion evidence |
|---|---|---|
| S1: Request checkpoints | New `server/Application/Services/AgentTaskLandRequestWriter.cs`; `AgentTaskLandSourceResolver.cs`; request result/call sites in `AgentTaskLandService.cs`. Implement typed patches, task lock, fresh guards and operation attachment. Align the recovery task write with this lock order. Keep monitor evaluation semantics. | New `Application/AgentTaskLandSourcePersistenceTests.cs`; extend `AgentTaskLandMonitoringTests.cs` and controlled harness only as required. Deterministic real-EF interleaving and stale request/progress/operation guards. |
| S2: Durable terminal diagnostics | `server/Domain/Entities/AgentTaskLandRequest.cs`, `server/Infrastructure/Data/AppDbContext.cs`, CLI-generated `server/Migrations/*` plus snapshot; new `server/Application/Services/LandFailureDiagnostic.cs`; `AgentTaskLandService.cs`, `server/Infrastructure/Orchestration/AgentTaskLandHostedService.cs`. Put diagnostic and terminal settlement in one guarded transaction. | New `Application/AgentTaskLandFailureDiagnosticTests.cs`; extend `AgentTaskLandPersistenceFailureTests.cs` and `AgentTaskLandApprovalPersistenceTests.cs`. Fresh-context persistence, atomic fault cuts, stale callback and publication-preservation cases. |
| S3: Safe inspection detail | `server/Application/Dtos/LandingDtos.cs`, `server/Infrastructure/Git/LandingGit.cs`, resolver diagnostic propagation; a small safe exception/value type if required. No broad Git interface rewrite. | Extend `Infrastructure/LandingGitTests.cs` and `Application/AgentTaskLandFailureDiagnosticTests.cs`. Test real inspection producer behavior via the existing overridable command seam, plus typed refusal propagation and suppression. |
| S4: HTTP/status/outbox | `server/Application/Dtos/LandRequestStatusDto.cs`, mapper use in `AgentTaskService.cs` if needed, `LandNotificationPayload.cs` only if needed beyond event-detail formatting, `scripts/delegate.ps1`, `client/src/api/agentTasks.ts`. | Extend `AgentTaskLandContractEndpointTests.cs`, `DelegateScriptLandStatusTests.cs`, `AgentTaskLandNotificationPersistenceTests.cs` / `AgentTaskLandNotificationRecoveryTests.cs`; genuine producer-to-GET-to-pwsh status test. |
| S5: Approval regressions and owner docs | Exercise Plan/TestDesign with explicit SHA, null Review evidence and null merge target; update `docs/ops-http.md` and the request/receipt section of `docs/orchestration-loop.md` with diagnostic fields, correlation and stderr limits. | Extend `AgentTaskLandApprovalRequestTests.cs`, `AgentTaskLandSourceFreshnessTests.cs` and endpoint/status cases; run the bounded affected regression set and ordinary Unit lane. |

S2 depends on S1's locked settlement contract. S3/S4 use the fields defined by S2. Land the implementation only after ordinary Code verification and separate Review; this Plan stage changes only this document.

## Verification requirements for TestDesign

TestDesign must add the canonical `## Verification design` section with exact V/R/PC identifiers, methods, commands, expanded argument cases and intended assertions. The following are mandatory design inputs, not claims that tests have been implemented or run.

### Deterministic race and write ownership

Use `LandingProtocolHarness`, controlled `ILandingGit`, `FakeTimeProvider`, and separate `AppDbContext` instances against one `TestDbFixture.CreateIsolatedSchemaAsync()` PostgreSQL schema. Do not use EF InMemory or just throw a synthetic concurrency exception as proof of the race.

Pause resolution after it loaded the request and before the Observed save, using the controlled inspection/observation hook and explicit completion barriers. In a second context execute the real `AgentTaskLandMonitorService.SweepAsync`, await its commit, and in a third context verify that the token and `LastEvaluatedAt` changed. Then release resolution. Bound barrier waits for fixture failures, release them in `finally`, and await every task; use no sleeps as ordering evidence.

Before the fix this ordering must produce the real `DbUpdateConcurrencyException` at source persistence. With the fix, inspect committed Observed evidence at a checkpoint before later resolution can mask it, then allow completion. Assert exact expected/local/remote/candidate SHAs, relationship, fingerprint and observation identity; unchanged approval; preserved monitor evaluation/reconciliation fields; no generic terminal refusal; and no extra Git inspection/replay attributable to the fix. Final resolution/publication and outbox counts must be checked from fresh contexts.

Run variants below threshold, at warning, and at error; retain exactly-once aged notifications and meaningful-progress clock behavior. Also stage the reverse ordering (resolver holds its short checkpoint lock, monitor waits) with a test-only EF command/save interceptor if the current Git hooks cannot reach it. The purpose is to establish both ordering and lack of a long transaction across Git.

Cover pending request replacement, terminal/canceled state, changed attempt, changed approval/coordinates, newer source observation and changed active operation between computation and save. A stale worker must stop without closing the replacement or overwriting new state. Exercise AdvanceStarted/child-start/child-clear/Resolved and operation-binding checkpoints so the fix cannot simply move the exception one save later. Preserve child journals and fail-before-dependent-mutation assertions.

### Failure producer to caller

Inject a real exception from the execution seam before an operation exists, after source evidence is committed, and after confirmed publication. Exercise the hosted drain's exception handoff at least once; a direct call to `FailRequestAsync` alone cannot prove that the actual catch passes the diagnostic correctly. Use `DbUpdateConcurrencyException` to test its classifier separately from the real monitor-race reproduction, plus a distinct I/O/timeout/generic type. Persist no synthetic secret markers carried in their messages.

Read the resulting request through a fresh context and the real task GET endpoint, invoke the actual `delegate.ps1 -Status` against that producer-generated HTTP state, and inspect the committed Outcome body. Assert the same code/diagnostic ID/type at all surfaces and in captured bounded log fields. A hand-authored JSON stub is useful for older DTO compatibility, but does not replace this chain. Use the existing guarded `LandContractWebAppFactory`/`AntiphonWebAppFactory`; drive the land service deliberately when hosted drain is disabled. Retain production-runner isolation.

Inject terminal-transaction failure before save, after save/before commit, at commit and after commit. Precommit cuts must leave zero terminal outcomes and no persisted terminal diagnostic/cleared pending marker; after-commit retry must preserve exactly one event/outbox/diagnostic. If storage remains unavailable, require correlated log evidence and a pending request, not an invented durable verdict. Include a replacement arriving before failure takes its task lock. Published cases retain the exact remote/verified receipt and appropriate cleanup state, never a refusal or new deletion authority.

For inspection refusal, use the real `LandingGit.InspectAsync` producer with controlled command failures for `status`, ignored-file listing, required identity commands and a filesystem exception. Prove code/template/exit/type propagation and distinguish actual exits from unknown values. Assert the raw synthetic stderr and hostile exception text never appear in request columns, JSON, events, notifications, CLI output or new log fields. Include zero/negative/large exit values and bounds where valid; never invent an exit for a launch failure. Ensure ordinary semantic reasons still work without diagnostics.

### Approval and presentation compatibility

Parameterize `AgentTaskRole.Plan` and `AgentTaskRole.TestDesign`, with `ReviewEvidenceId=null`, full explicit expected SHA and `MergeTargetRef=null`. Exercise admission and full land execution, including remote publication evidence. Verify `ApprovalKind=ExplicitCaller`, schema 2, retained expected SHA, default target `refs/heads/master`, and no `legacy_review_binding_required` refusal. Use controlled Git for the matrix plus a bounded real local bare-remote docs-only case for each role if required to establish the actual default-target integration; never land against a live repository in a test.

Retain rejection of missing/abbreviated SHA, mismatching supplied Review evidence, pending-identity changes and Mutation snapshot lands. Preserve source-freshness refusal and exact-SHA recovery cases already pinned by CARD-0488. Extend status tests with existing source-only, operation-only, combined and all-null/legacy fields, ensuring the actual report remains after the status facts. Keep `DelegateScriptLandCompatibilityTests` and `DelegateScriptLandApprovalTests` green: version/SHA fences and 202 acknowledgment remain unchanged.

### Execution shape and positive controls

Ordinary verification uses one producer-owned isolated build, the Unit lane, and the named affected integration classes in the slice table. Most of the concurrency matrix uses controlled Git and real isolated PostgreSQL; reserve process-heavy Git/HTTP/pwsh cases for their boundary assertions. Give every new class its correct Unit/Integration category and every process-spawning class the assembly-local `ParallelLimiter<ProcessSpawnLimit>`. Do not co-run the Pty assembly. Scope DB assertions to the owned request/task/schema.

Base recipe for Code, with fresh result directories per invocation and TestDesign's final class filters:

```powershell
dotnet build tests/Antiphon.Tests --property:OutputPath=bin-c498/ --nologo
dotnet run --project tests/Antiphon.Tests --no-build --property:OutputPath=bin-c498/ -- --treenode-filter '/*/*/*/*[Category=Unit]' --report-trx --report-trx-filename unit.trx --results-directory .antiphon/c498-unit
dotnet run --project tests/Antiphon.Tests --no-build --property:OutputPath=bin-c498/ -- --treenode-filter '/*/*/(AgentTaskLandSourcePersistenceTests*)|(AgentTaskLandFailureDiagnosticTests*)|(AgentTaskLandMonitoringTests*)/*' --report-trx --report-trx-filename request.trx --results-directory .antiphon/c498-request
```

TestDesign must name additional filters for S3-S5 and existing approval/recovery/notification boundaries; the example request filter is not the full regression set. Include ordinary persistence and protocol controls actually affected by the writer audit. Inspect fresh TRX method names and nonzero counts for every class; an exit-zero or discovery listing is not evidence. Run the duration tripwire and explain new slow boundary tests. Type-check the changed client contract; no browser feature or E2E sweep is required solely for optional type additions.

Positive controls should include removing the task lock/reload protection (the real monitor race returns), applying stale source fields after reload (newer evidence is overwritten), dropping the failure diagnostic during terminal reload/DTO mapping, restoring generic exception prose, omitting each requested CLI field, and discarding inspection command/exit metadata. Design independent guard/visibility controls rather than one giant test whose first failure masks later omissions. Define intended assertion failures and exact method filters. PCs execute in post-land Mutation under the current companion/SourceLanding protocol, after ordinary Code and separate Review; the new plan does not require pre-land mutation runs.

## Acceptance and handoff

Done for the implementation means the deterministic monitor interleaving succeeds with committed source evidence; stale work cannot overwrite or settle newer work; unexpected exceptions produce bounded durable diagnostics through HTTP/status/outbox; useful safe inspection metadata survives; and explicit-SHA non-reviewed Plan/TestDesign lands with null targets still publish correctly. Review must also check additive migration compatibility, lock order, atomic settlement/idempotence and the raw-diagnostic boundary.

No product decision is pending. The selected defaults are short coordinated checkpoints, additive request diagnostics, command/exit/type metadata with stderr suppression, and the existing asynchronous land contract. If TestDesign finds a checkpoint cannot be verified under these guards, return `next: plan` with the exact gap.

This document is authored in the authorized Shared canonical checkout on `master`; commit and push this exact plan path so the next stage can branch from it. It is not a succeeded Worktree task and does not require a self-directed `/land/v2` request. For the later implementation, the orchestrator uses the original Code task's approved SHA and normal land receipt, then the canonical deployment runbook if activation is required. Confirm `/api/version` contains the intended implementation before relying on live behavior. Plan publication itself requires no server restart.
