# CARD-0540: Collapse redundant sibling warnings by commit ancestry

Date: 2026-09-15. Stage: Plan, including the verification design requested in the brief.
Baseline: `8435fb16a15f92364aa2dbd63a799556d3a9dc1d`.
Next: Code. No product-policy question remains for the caller.

## Outcome

For each successful dispatch, emit one sibling warning for each distinct tip that
is not proven contained in another eligible sibling tip. Equal tips share one
representative. Ancestor chains collapse to their containing tip. Divergent tips
remain separate warnings, including forks with a shared ancestor.

Reduce the observations **before** CARD-0508 captures durable warning intents.
Continue using its final dispatch-event identity, immutable payload, outbox,
keyed queue, recovery worker and complete UserPrompt receipt. This is a change to
notification cardinality; it grants no publication or cleanup authority.

Authority: [the confirmed investigation](../../investigations/2026-09-15-card-0540-dispatch-time-sibling-branch-warning-fires-once-per-unlanded-sibling-not-once-per-dispatch.md)
and [CARD-0508 amendment B](2026-09-14-card-0508-worktree-base-selection-plan.md#plan-amendment-b-p-3-and-p-4-resolved).
The latter shipped S1/S2/S2b; its earlier S3 sibling-base selection proposal remains
deferred. CARD-0540 does not activate that proposal.

## Ground truth

Source observations below are at the baseline above. They are not runtime tests.

| Card assumption or tempting shortcut | What the code/evidence actually does | Plan consequence |
|---|---|---|
| The four messages were duplicate delivery. | Investigation reconstructed four distinct warnings, four queue rows, and four once-delivered UserPrompts. All earlier tips are ancestors of the latest; two are identical. | Fix the producer's cardinality, not queue retries. The incident's four warnings become one under this plan. |
| CARD-0508 already collapses siblings. | `AgentTaskDispatcher.EvaluateCardSiblingBaseAsync` (around line 2890) still inspects every eligible sibling independently against the dispatch base. | Add sibling-to-sibling reduction after ordinary eligibility/base filtering. |
| A helper named `IsAncestorOfBaseAsync` is strict ancestry. | `DelegationWorktreeService.cs:148` delegates that method to patch-aware `ContainsPatchesAsync`, implemented with `git cherry`. | Add an explicitly strict commit-ancestry helper. Do not reuse or change the compatibility helper. |
| Existing tip text is an identity. | `DescribeKeptBranchAsync` uses `rev-parse --short`, then reads count and subject through a mutable ref. | Resolve a full commit ID once; use it for sibling comparisons and the corresponding description. Never group by abbreviated text or `unknown`. |
| Latest task means all older work is included. | Candidate query has no ordering, no supersession metadata, and no sibling ancestry check. Creation/completion times cannot prove containment. | Git ancestry decides coverage. Task creation time only breaks ties among equal tips. |
| All connected sibling histories can share one warning. | Two divergent branches can both contain an earlier ancestor without containing each other. | Keep both maximal tips; do not reduce by connected component or just choose the newest task. |
| Reduction can run before checking active lands. | The guard holds if any eligible, missing-from-base sibling has `LandRequestedAt`, even a Blocked or non-Code task. | Check holds across the original candidate set first. Redundancy does not release a hold. |
| Distinct sibling keys deduplicate equal content. | `BuildDispatchWarningDrafts` keys each draft as `sibling:<task GUID>`. Capture and the DB unique index deduplicate `(DispatchEventId, WarningKey)`, not Git tips. | Feed only representatives to this unchanged key scheme; no new table/key namespace. |
| Warnings are captured after successful launch. | Dispatcher captures intents with the final agent-dispatch event in the claim transaction (around lines 3240-3270), before launch. Projection happens later. | A committed claim owes its reduced warnings even if launch fails or the process dies. A rollback owes none. |
| Recovery can regroup against current branches. | `DispatchBaseWarningIntentService` materializes saved IDs/body/route; the hosted worker scans pending intents independently of task status. | Freeze the selected warnings once. Recovery never revisits Git or cancels an earlier intent because a branch later changes. |
| The guard observation is protected by the repository lease. | It is taken before `DispatchOneAsync` acquires the lease. CARD-0508 detects different observed/actual base ref names, not every same-ref movement. | Preserve that boundary and limitation. Full tip IDs make this reduction internally coherent, not contemporaneous with launch. |
| Native dispatch-warning verification can simply be rerun. | `DispatchBaseNotificationTests.cs` exists, but the `DispatchBaseWarningDeliveryE2ETests.cs` named by CARD-0508's plan does not. Existing `LandDeliveryFixture` seeds a Succeeded landing task and its census assumes `[land ...]`. | S3 explicitly supplies actual queued-dispatch setup and dispatch-header receipt/census support. Do not report absent CARD-0508 tests as inherited green evidence. |
| Containment permits deleting old branches/worktrees. | The investigation did not audit dirtiness/ownership. `docs/orchestration-loop.md` requires receipts, current source identity, remote containment and guarded removal. | Preserve branch refs, worktree content, task history and existing cleanup paths. |

## Decisions

### D-1. One warning per surviving tip, not one per dispatch

Among otherwise warnable siblings, equal full tips form one class. A class is
covered when its tip is a **strict ancestor** of another class's tip. Keep the
maximal classes of the proven relation. Unknown relationships retain visibility.

This follows the requested suppression while preserving existing independent
warnings for real divergence. It avoids a new aggregate payload, warning key or
delivery protocol. Default-ref and stale-observation warnings remain independent:

`total intents = surviving sibling warnings + applicable mismatch/default warnings`.

Examples, with every listed tip missing from the dispatch base:

| Observed histories | Sibling warnings |
|---|---|
| A@x, B@x | 1, naming the equal-tip representative |
| A@x -> B@y -> C@z | 1, naming C@z |
| A@x -> B@y, with duplicate C@y | 1, naming the representative for y |
| A@x and B@y, neither an ancestor of the other | 2, naming A and B |
| A@x -> B@y and A@x -> C@z; y and z diverge | 2, naming B and C |
| Above fork later merged into D@w, containing both y and z | 1, naming D |
| Two unresolved tips, or two failed ancestry comparisons | 2, conservatively retained |

### D-2. Strict ancestry between siblings; patch equivalence only against the base

Continue CARD-0508's patch-aware test for whether work is already in the dispatch
base. For sibling collapse use full object equality or successful
`git merge-base --is-ancestor <ancestorSha> <descendantSha>` only. Rebased or
cherry-picked siblings with different, non-ancestor tips still warn separately.

Commit ancestry is the explicit evidence requested by this card. Patch equivalence
would broaden suppression to histories that the brief does not authorize treating
as superseded. Reverts do not undo historical containment: the descendant warning
represents that branch's observed history, not a claim that every ancestor's tree
content remains in its final tree.

### D-3. Deterministic representatives and order

Within an equal-tip class choose `CreatedAt` descending, then branch name ordinal
ascending, then full task GUID in `N` format ordinal ascending. Order final warnings
by that same tuple. Add `CreatedAt` to the existing candidate projection.

Creation time expresses the later task for an identity tie; Git author/committer
clocks and query enumeration order are unsuitable. For unequal tips, containment
wins regardless of task dates: an earlier-created repair task may carry later
commits. Do not require a timestamp ordering to accept proven ancestry.

Assign a suppressed class to the first ordered surviving class that provably
contains it. Proven paths through intermediate classes count: strict ancestry is
transitive over these immutable IDs. Include equal-tip aliases in that survivor's
covered count. Each suppressed task is counted once, including a fork ancestor.

### D-4. Freeze full tips and fail conservatively

Add `ResolveKeptBranchTipAsync(repo, branch, ct)` and
`IsCommitAncestorAsync(repo, ancestorSha, descendantSha, ct)` to
`DelegationWorktreeService`, using its existing Git I/O seam. Resolve
`refs/heads/<branch>^{commit}` using `rev-parse --verify --quiet`; accept only one
full hexadecimal object ID (40 or 64 characters), normalizing case. The strict
helper accepts those IDs, never mutable branch names or abbreviated SHAs.

Snapshot each candidate once. Use a known full tip for its base-containment check,
description and sibling comparisons; `DescribeKeptBranchAsync` already accepts a
Git ref, so passing the pinned SHA avoids changing its other callers. Retain a
separate full identity from its short display tip. For a present branch whose tip
cannot be resolved, retain the existing conservative warning/hold behavior with
unknown description; do not join unknowns into an equality class. A branch already
absent at the existing local-branch check remains silent as today.

Only exit 0 proves ancestry. Exit 1, other failures, invalid output or a non-cancel
I/O exception add no coverage edge. Record a bounded diagnostic in the existing
logger for operational failures; no extra parent prompt is needed. Cancellation
propagates rather than becoming an observation or warning capture.

Compare each ordered pair of distinct known tips at most once per guard evaluation;
skip equality pairs and cache results locally. A simple O(k squared) comparison is
appropriate for this card's small sibling set (four warnable tips in the incident).
Do not cap the output count, persist a cache, fetch remote refs or infer containment
when a probe is skipped. Measure probe count and elapsed guard time in a 20-tip
scratch case; report the time, with no invented production latency target.

### D-5. Preserve eligibility and holds before reduction

Keep the current card/workspace/status/repository/local-branch/base-containment
filters. Succeeded and Blocked tasks of every role remain candidates; Queued,
Dispatched, Working, Failed and Canceled tasks do not. Keep repair dispatch and
SourceLanding bypass behavior and path-present reuse behavior.

Build the ordinary hold/warning observations first. If **any** candidate not
contained in the base has a land request, return the existing hold with no warning
intents. Only a no-hold result goes to reduction. Do not change dispatch-base
selection, move the guard under the lease, or claim to resolve CARD-0508's remaining
observation/provisioning race. Comparisons and message text describe observed IDs.

### D-6. Reuse the durable claim-time warning path unchanged

`BuildDispatchWarningDrafts` receives reduced observations and retains
`SiblingKey(representative.TaskId)`. Capture one frozen draft per representative,
using the existing final dispatch-event ID and original reply route. Same-claim
replay uses the original identities; another successful claim owes a new warning
even if it names the same tip. This is not cross-dispatch suppression.

Retain the singular warning's branch, tip, commit count, subject and existing
action wording. When its covered count is nonzero, append:
`At the observed tips, this warning also covers N other kept sibling branches.`
Use the representative's full observed SHA in the detail, which still contains
the existing abbreviated-tip substring. Do not enumerate every covered branch in
the prompt: the count adds constant text instead of making one message grow with
all historical aliases. Divergent representatives each retain their complete own
warning. The pure reduction result retains the member mapping for construction
and tests; no new durable metadata field is required by this scope.

Do not change `DispatchBaseWarningIntentService`, the payload header/digest,
notification/queue schema, workers, receipt rules or retry/retention policy to
implement collapse. Persisted historical intents and queued messages are immutable
obligations; do not backfill, merge, cancel or rewrite them.

### D-7. Superseded is a notification observation, not task or cleanup state

No new task status, card revision, landing receipt, branch deletion, worktree
removal or automatic land is produced. An uncommitted file on an older worktree is
not covered by a commit-ancestry assertion. Existing guarded cleanup alone owns its
eventual disposition. The existing Land instruction remains subject to ordinary
landing admission, including repair ownership; warning selection confers no Land
authority on the representative.

### D-8. Rejected alternatives

| Alternative | Reason rejected |
|---|---|
| One combined notification for every dispatch | Broader presentation/identity change; separate divergent warnings already satisfy visibility and fit CARD-0508's existing keys. |
| Warn only about the newest task or use a review-round cutoff | Can silently lose divergent commits. Time and roles do not prove ancestry. |
| Group connected components of the ancestry graph | Collapses both sides of a fork even when neither contains the other. |
| Compare short hashes, branch names, commit subjects or file counts | Display similarities are not commit identity or containment. |
| Use `ContainsPatchesAsync` between siblings | Extends suppression beyond the requested strict ancestry semantics. Keep its landed-base purpose. |
| Rewrite existing intents during projection/recovery | Changes an already committed observation and can lose delivery custody after a crash. |
| Introduce an aggregate outbox, per-card debounce, or global tip deduplication | Duplicates the established durable path and wrongly combines separate dispatch obligations. |
| Suppress active-land holds or delete covered siblings | Notification reduction proves neither dispatch readiness nor cleanup safety. |

## Implementation slices

Commit and push each slice with its actual verification outcome. These paths are
repo-relative. Files marked new do not exist at the inspected baseline.

| Slice | Files | Tests and completion condition |
|---|---|---|
| S1: Full-tip observation and reduction | `server/Application/Services/DelegationWorktreeService.cs`; new `server/Application/Services/SiblingWarningReducer.cs` | New `tests/Antiphon.Tests/Application/SiblingWarningReducerTests.cs`; strict-probe tests in `WorktreeBaseSelectionTests.cs`. Equality, chains, forks, patch-equivalent divergence, failures and deterministic output are independently pinned. |
| S2: Integrate before durable capture | `server/Application/Services/AgentTaskDispatcher.cs`; `tests/Antiphon.Tests/Application/AgentTaskDispatchBaseGuardTests.cs`; `tests/Antiphon.Tests/Application/DispatchBaseNotificationTests.cs` | Drive actual `TickAsync` and assert reduced intent keys, counts, frozen bodies, independent diagnostics and held/refused zero-intent cases. Preserve existing C508 tests' two genuinely divergent siblings and counts. No migration or notification-service production edit. |
| S3: Native acceptance and recovery | New `tests/Antiphon.E2E/DispatchBaseWarningDeliveryE2ETests.cs`; `tests/Antiphon.E2E/Fixtures/LandDeliveryFixture.cs`; `tests/Antiphon.E2E/Fixtures/LandDeliveryOptions.cs` | Add queued-dispatch setup, identity-based dispatch receipts and native census, plus dispatch claim/projection crash barriers. Ten native cases below pass through the real dispatcher and queue. Re-run the named existing land fixture regressions. |
| S4: Explain the shipped policy and record verification | `docs/orchestration-loop.md` at the dispatch-base paragraph; `docs/session-runtime-invariants.md` Gotcha 82; this plan's execution evidence section when Code records results | Document one warning per surviving observed tip, divergence/hold behavior, claim-time immutability and separate cleanup authority. Record build SHA, executed test counts, failures and pending PCs. Do not edit generated `docs/cards/`. |

`SiblingWarningReducer` is a pure internal concrete class/helper, not a DI service
or new interface. Its input is immutable candidate metadata
`(TaskId, Branch, CreatedAt, FullTipSha?)` plus the set of **proven** ordered
ancestor/descendant SHA pairs. Its output contains representative/member groups.
It owns equality grouping, reachability, maxima, deterministic assignment and order;
the dispatcher maps those results back to frozen descriptions. Git I/O stays in
the existing worktree seam. Do not add database or filesystem dependencies to the
pure reducer. Read-only command failures can be injected through `ILandingGit`.

## Verification design

### Inspection

Bodies read for this design:

| Source/test/fixture | Boundary coverage or explicit limit |
|---|---|
| `AgentTaskDispatcher` guard, claim and draft builder; `DelegationWorktreeService` probe/description/Git wrapper | V-1..V-8, R-1..R-3. Existing ancestry-name trap and pre-lease observation boundary verified. |
| `AgentTaskDispatchBaseGuardTests` hold, single-warning, `C508_GuardRefMismatchWarnedOnce`, seeders and materialize/deliver helper | Actual Git + isolated PostgreSQL + real producer for V-1..V-8. Existing seed helper always branches from master, so explicit tip/start-ref seed support must be added for aliases/chains. Its session is a DB fixture, not native receipt evidence. |
| `DispatchBaseNotificationTests` claim capture, attempt identity, projection custody and surrounding fixture setup | R-3/R-4 and V-9. Existing capture/route/identity tests are reusable. Add the exact grouped rollback/projection and receipt-negative methods below. |
| `ScratchGitRepo`, `DelegationTestServices`, `WorktreeBaseSelectionTests` probe/containment neighborhood | New reducer tests use the pure seam; new probe/dispatcher cases use real Git and the established graph registration, per-test DB schemas and process limiter. |
| `LandDeliveryFixture` setup, child restart, receipt, census, disposal; `LandDeliveryOptions` configuration and FileBoundary | V-10..V-19. Missing setup is specified below; current fixture alone cannot dispatch or census dispatch headers. |
| `AgentTaskLandDeliveryE2ETests` idle/busy, terminal/queue crashes, receipt/verdict cuts | R-5 validates existing fixture behavior after extension. These are real native tests but their existing land producers alone do not prove sibling-warning cardinality. |
| `DispatchBaseWarningIntentService`, `DispatchBaseNotificationPayload`, `AgentTaskLandNotificationHostedService`, `AgentTaskLandNotificationService`, `PromptSubmissionMatch.IsConfirmedBy/IsCompleteIn` | Claim -> projection -> queue -> original destination/full prompt. V-9..V-19 and the inherited-custody PCs below. |

Do not claim fixtures that only insert intents, queue rows or transcript rows prove
native receipt. They prove producer/database rules or negative matching decisions.
The native cases must actually type and observe their warnings.

### Delivery inventory

| Producer/handoff | Durable identity and destination | Commit/recovery contract | Receipt/test |
|---|---|---|---|
| Reduced sibling observations -> claim | Final `Dispatched` event + `sibling:<representative GUID>` -> intent ID/NotificationId; route from locked claim | Same transaction as successful claim. Before commit, rollback leaves zero intents. After commit, launch success is irrelevant. | V-8, V-12; inspect committed intent IDs before killing child. |
| Intent -> Warning + DispatchBase notification | `intent.Id = Warning.Id = note.SourceEventId`; `intent.NotificationId = note.Id` | Pair and MaterializedAt commit together. Hosted boot/periodic intent scan recovers original IDs/text/route without Git. | V-9, V-12/V-13. Projection precommit crash leaves original pending intent. |
| Notification -> queue | `note.Id = queue.SourceLandNotificationId`; frozen Body/digest and parent session | Existing unique keyed insertion and retry recover both pre-insert failures and insert-before-ack death. | V-14..V-16. Original IDs, one queue row per note, no extra unkeyed warning. |
| Queue -> native caller -> persisted receipt | Original parent session; full immutable dispatch header/body after original attempt sequence/time floor | Real WhenIdle queue/flush and transcript catch-up. Already-idle caller needs no fresh human input. Busy caller waits. | V-10/V-11, V-17..V-19: matching complete UserPrompt, stored confirming sequence, native census. Sent/screen/header-only evidence is insufficient. |

The native fixture uses the owned isolated runner (assert port != 17204), its
owned database and modern ConPTY FakeGrok setup. Add an **opt-in dispatch setup**
to the existing fixture rather than silently replacing its land setup: seed a
card/project and a real Queued Worktree task routed to the native caller; configure
the delegate's selected definition to the owned FakeGrok executable as well.
Use the real dispatcher/hosted services, not direct intent insertion. Keep the
existing Succeeded landing task absent from that card's candidate set. Retain
native homes, runner identity and MVID evidence under the fixture's owned root.

Generalize receipts to task/notification IDs and check `note.Body`, not only queue
body; census both `[dispatch-base ...]` and the actual warning-detail text so an
unkeyed duplicate cannot escape. Count all expected representative identities,
plus zero unexpected sibling warnings, after two further completed notification
scans. Busy cases assert zero attempts and no warning UserPrompt before releasing
the owned busy gate, then verify complete receipts.

Extend FileBoundary to record `dispatch-warning-intent-scan` and recognize claim
and projection cuts already emitted by production. A claim-crash cut must gate
**both** the producer at `dispatch-warning-claim-committed` and materializers at
`dispatch-warning-before-materialize`; otherwise the worker can win before death.
Projection cuts gate `dispatch-warning-before-commit`. Scope barriers by the
commissioned task/intent IDs, atomically publish barrier files, then kill only the
owned child server and await it. Keep the parent-owned runner/database alive.
Restart without fault gates and observe normal worker recovery. No background run
may outlive its test/fixture. Keep the queue's real progressing TimeProvider.

### Proves it works now

Abbreviations: **SR** = new `SiblingWarningReducerTests`; **BS** = existing
`WorktreeBaseSelectionTests`; **DG** = existing `AgentTaskDispatchBaseGuardTests`;
**DN** = existing `DispatchBaseNotificationTests`; **DE** = new
`DispatchBaseWarningDeliveryE2ETests`. New test methods below are implementation
requirements, not claims that tests already exist.

| ID | Exact methods / arrangement | Decisive expected result |
|---|---|---|
| V-1 | SR/DG `C540_IdenticalTipsCollapse`; SR `C540_RepresentativeOrderIsStable` | Two task/branch aliases at one real tip produce exactly one representative, one sibling intent/event/note, one covered task. All input permutations, creation-time ties, branch and GUID ties choose the specified representative. |
| V-2 | SR/DG `C540_AncestorChainCollapses` | A -> B -> C, plus alias of B, produces only C's warning/key, covered count 3. Deliberately reverse task creation times. Reconstruct the investigation's four-task/two-equal-tip topology. |
| V-3 | SR/DG `C540_DivergentTipsRemainVisible`; SR/DG `C540_ForkKeepsBothTips` | Independent branches each retain their key/detail. In A -> B and A -> C, retain B and C; cover A once. Add a real merge D containing B/C and expect only D. |
| V-4 | BS/DG `C540_PatchEquivalentSiblingsStillWarn` | Real cherry-picked equal patches on different histories: both sibling keys survive strict non-ancestry. Cherry-picking into the dispatch base remains silent via existing C508 base tests. |
| V-5 | BS `C540_FullTipIdentityIsUsed`, `C540_AncestryProbeFailureRetainsWarnings`, `C540_ProbeCancellationPropagates`; SR `C540_UnknownTipsRemainVisible` | Full SHA equals independent rev-parse; comparison spy sees frozen full IDs even if refs move. Two full IDs sharing a short prefix stay distinct in pure tests. Null/malformed tips stay separate; exits 1/128 and non-cancel I/O exceptions prove no edge; cancellation throws. |
| V-6 | DG `C540_RedundantSiblingStillHolds` (equal-tip and ancestor cases) | Landing older/alias sibling still leaves new task Queued, no worktree and zero intents. After clearing hold, next successful tick reduces normally. Include a Blocked sibling and a non-Code dispatch role. |
| V-7 | DG `C540_ExcludedSiblingCannotCoverWarning` (wrong card, Shared workspace, each excluded status, foreign repository) | Eligible older branch still warns even when an excluded row names a containing tip. Foreign-repository case has a same-name local ref to make omission of repo filtering observable. Deleted and base-contained candidates keep existing behavior. |
| V-8 | DG `C540_ReducedWarningsKeepBaseDiagnostics`; DG `C540_ClaimRollbackHasNoCollapsedIntent` | Mixed graph gives two sibling intents plus independently applicable mismatch/default intents. Inject failure at claim-before-commit after save (DB transaction interceptor for after-save cut); final dispatch/session/intents all roll back. Held/claim-loser/precommit failure also owe zero. |
| V-9 | DN `C540_CollapsedProjectionIsAtomic`; DN `C540_WarningRequiresCompletePrompt`, `C540_WarningRequiresAttemptFloor` | Saved representative intent projects original event/note once. Failure after projection SaveChanges but before commit leaves neither pair member nor marker; clean retry makes exactly one pair. For seeded negative receipt tests, >200-char matching head with missing tail and a complete old prompt stay unconfirmed; only full original body after attempt floor confirms. Test sequence and timestamp fallback independently. |
| V-10 | DE `C540_CollapsedWarningsReachIdleCaller` | Real mixed graph: two aliases plus a containing tip, and one divergent tip -> exactly two full native UserPrompts without new caller input; correct keys, covered count and immutable bodies. |
| V-11 | DE `C540_CollapsedWarningsWaitForBusyCaller` | Same graph: two WhenIdle rows, zero attempts/receipts before release, then the same two complete native receipts, exactly once each. |
| V-12 | DE `C540_ClaimCrashRecoversCollapsedWarnings` | Kill child with committed reduced intents and zero projected pairs. While stopped, move sibling refs and change task status/route; restart. The original caller receives the original reduced identities/bodies, no new group or current-ref lookup. |
| V-13 | DE `C540_ProjectionCrashRecoversOriginalPairs` | Kill at projection-before-commit. Rollback leaves pending intents, zero partial pairs. Boot recovers original pairs and complete prompts. |
| V-14 | DE `C540_PreEnqueueCrashRecoversWarnings` | Kill at before-enqueue with committed pairs and no queue rows; restart delivers same identities. |
| V-15 | DE `C540_EnqueueFailureRetriesWarnings` | Inject two owned before-enqueue I/O failures. Same notes retry to complete receipt, with nonzero failure evidence and no new notification identities. |
| V-16 | DE `C540_QueueInsertCrashReusesRows` | Busy caller; kill after keyed insert but before note acknowledgement. Record original queue IDs; restart/release yields those same rows and one complete prompt per warning. |
| V-17 | DE `C540_PreTypingCrashRecoversWarnings` | Kill at queue-before-typing; restart recovers attempted pending work through existing queue recovery and delivers once. |
| V-18 | DE `C540_PostPromptCrashDoesNotRetype` | Kill at queue-before-verdict after real prompt evidence. Restart catches up receipt, without another native submission. |
| V-19 | DE `C540_ReceiptCrashDoesNotRetype` | Kill at receipt-before-save after real full prompt evidence. Restart records original receipt, one native submission. |

V-10..V-19 are ten native methods, individually filterable. Every cut uses the
mixed graph, so recovery cannot pass by delivering only one divergent branch.
V-5's 20-tip probe census bounds work to at most k*(k-1) distinct ancestry calls;
zero/one unique-tip cases launch no sibling ancestry subprocess. Do not assert
wall-clock performance as a functional test.

### Guards the regression

| ID | Existing tests / assertions |
|---|---|
| R-1 | All `AgentTaskDispatchBaseGuardTests`, especially hold/deleted/single-warning, `C508_RepairRecordsOwnerAndSkipsSiblings`, `C508_GuardRefMismatchWarnedOnce`, `C508_RebasedSiblingUsesActualDefault`, `C508_GuardPreservesFailedDefault`. Preserve counts for their genuinely divergent seed branches. |
| R-2 | `WorktreeBaseSelectionTests`, `DelegationWorktreeTests`: existing base precedence, patch containment and worktree behavior remain green. Snapshot all seeded sibling refs and sentinel dirty/untracked worktree files around DG tests; reduction changes none. |
| R-3 | DN `C508_ClaimCapturesWarningIntents`, `C508_IntentAttemptIdentity`, `C508_IntentUniqueKeys`: same final-event/key replays same IDs; another final event gets new IDs even with identical task/Attempt/text. |
| R-4 | All `DispatchBaseNotificationTests`: frozen route/body/digest, projection integrity, concurrent projection, lost acknowledgement, retry and initial-state rules remain intact. `ReplyTo=None` remains NotRequired; unavailable destination retains custody. |
| R-5 | `AgentTaskLandDeliveryE2ETests.C467_V22_AlreadyIdleGetsOutcomeWithoutNewInput`, `C467_V26_HardCrashAfterQueueInsertReusesRow`, `C467_V30_ReceiptSaveFailureNeverRetypes` (both rows): land-mode setup, header census and child recovery still work after fixture extension. |

### Guard inventory and positive controls

The following is the explicit guard/PC inventory for the new reducer and the
claim/receipt boundaries this change relies on. Existing CARD-0508's wider
migration, attention, retention and paging controls remain its own verification
work; this plan neither repeats all 116 nor claims they have run. Their production
implementations are unchanged here. The inherited custody guards exercised here
have concrete controls below rather than relying on unexecuted plan references.

Each row is one G-n -> PC-n mapping. Mutations are temporary compiling defects,
applied only to the corresponding owned SourceLanding snapshot after land. For
new methods, the implementation must keep the named seam testable as specified.

| Guard / PC | Deliberate defect | Exact method and expected red assertion |
|---|---|---|
| G-1 / PC-1: D-1 equal-tip collapse | In reducer equality projection, emit every alias as representative instead of the selected member; keep strict unequal-tip comparisons. | SR `C540_IdenticalTipsCollapse`: representative count 1, actual 2. |
| G-2 / PC-2: D-4 full identity | Return the first eight characters from the new full-tip resolver. | BS `C540_FullTipIdentityIsUsed`: result equals independently resolved full commit ID. |
| G-3 / PC-3: D-2 ancestry direction | Swap ancestor/descendant args in the strict helper's Git command. | DG `C540_AncestorChainCollapses`: survivor key must name C, not A. |
| G-4 / PC-4: D-2 strict proof | Replace strict helper's result with `ContainsPatchesAsync(repo, ancestorSha, descendantSha, ct)`. | BS `C540_PatchEquivalentSiblingsStillWarn`: strict containment false in both directions. |
| G-5 / PC-5: D-4 unknowns separate | Group all null-tip candidates into one ordinary equality class. | SR `C540_UnknownTipsRemainVisible`: two unknown-task warnings remain. |
| G-6 / PC-6: D-4 failed probes | Treat a nonzero strict-probe exit as true instead of false. | BS `C540_AncestryProbeFailureRetainsWarnings`: exit-128 comparison contributes no edge. |
| G-7 / PC-7: D-4 cancellation | Catch OperationCanceledException in the new probe and return false. | BS `C540_ProbeCancellationPropagates`: cancellation is thrown. |
| G-8 / PC-8: D-1 fork divergence | In reducer, keep only the first maximal tip of a connected ancestry component. | SR `C540_ForkKeepsBothTips`: B and C both survive, with A covered once. |
| G-9 / PC-9: D-3 representative identity | Order equal-tip members by CreatedAt ascending. | SR `C540_RepresentativeOrderIsStable`: newest equal-tip task is representative. |
| G-10 / PC-10: D-5 hold precedence | Disable the `LandRequestedAt` hold arm in the guard. | DG `C540_RedundantSiblingStillHolds`: task remains Queued with no worktree/intents. |
| G-11 / PC-11: D-5 card eligibility | Remove the candidate query's CardId equality. | DG `C540_ExcludedSiblingCannotCoverWarning`: wrong-card containing tip cannot hide eligible warning. |
| G-12 / PC-12: D-5 workspace eligibility | Remove the candidate query's Worktree predicate. | Same DG method: Shared-workspace containing tip cannot hide eligible warning. |
| G-13 / PC-13: D-5 status eligibility | Remove the Succeeded/Blocked predicate. | Same DG method: excluded-status containing tip cannot hide eligible warning. |
| G-14 / PC-14: D-5 repo eligibility | Replace the same-repository filter with unconditional true. | Same DG method: foreign metadata plus shadow local ref cannot hide eligible warning. |
| G-15 / PC-15: D-6 actual producer uses reduction | Return original warning observations to BuildDispatchWarningDrafts instead of reduced representatives. | DG `C540_IdenticalTipsCollapse`: committed sibling-intent count is 1. |
| G-16 / PC-16: D-6 per-claim identity | In CaptureAsync's local and persisted lookup, match TaskId/WarningKey instead of DispatchEventId/WarningKey. | DN `C508_IntentAttemptIdentity`: second claim creates distinct IDs; four rows, not two. |
| G-17 / PC-17: D-6 claim atomicity | Save and commit immediately after capture, before claim-before-commit boundary. | DG `C540_ClaimRollbackHasNoCollapsedIntent`: committed intent count remains zero after injected cut. The test catches the expected dispatch failure before assertions. |
| G-18 / PC-18: D-6 boot recovery | Skip the hosted worker's intent-materialization pass, retaining its notification scan. | DE `C540_ClaimCrashRecoversCollapsedWarnings`: after two completed notification scans, expected original note-ID set has count 2, not 0. Do not wait for the removed intent-scan hook. |
| G-19 / PC-19: D-6 frozen payload | In payload Materialize, set note.Body to `intent.Detail`, omitting the frozen header. | DN `C508_IntentProjectionCustody`: Body equals original intent.Body. |
| G-20 / PC-20: D-6 projection atomicity | Commit after adding/saving the Warning alone, before adding note/marker and before the projection fault boundary. | DN `C540_CollapsedProjectionIsAtomic`: after caught failure, zero Warning rows, zero notes and null marker. |
| G-21 / PC-21: D-6 queue identity | Omit `sourceLandNotificationId: note.Id` from the notification enqueue call. | DE `C540_QueueInsertCrashReusesRows`: exactly one row under each original SourceLandNotificationId; original queue IDs retained. |
| G-22 / PC-22: full receipt | Remove `IsCompleteIn(note.Body, p.Text)` from the reconciler's evidence predicate. | DN `C540_WarningRequiresCompletePrompt`: >200-char identifying head with missing tail leaves ConfirmedAt null. |
| G-23 / PC-23: receipt attempt floor | Remove sequence-floor filtering in ReconcileAsync, preserving body checks. | DN `C540_WarningRequiresAttemptFloor`: old complete matching prompt does not confirm the current attempt. |
| G-24 / PC-24: original destination | In MaterializeAsync, overwrite new note.ParentSessionId from the current task row instead of the intent. | DN `C508_IntentProjectionCustody`: original destination A survives later route edit to B. |
| G-25 / PC-25: WhenIdle delivery | Change notification enqueue mode from WhenIdle to Now. | DE `C540_CollapsedWarningsWaitForBusyCaller`: queued mode remains WhenIdle and attempts remain zero before release. |
| G-26 / PC-26: receipt timestamp fallback | Remove the timestamp-floor filtering when LastDeliveryBaselineSequence is null, retaining sequence filtering. | DN `C540_WarningRequiresAttemptFloor`: in the no-sequence row, a full prompt older than the attempt time minus configured tolerance does not confirm. |
| G-27 / PC-27: rollback keeps tick custody (review F1) | In the claim-rollback handler, reload only `claimed` after `ChangeTracker.Clear()` instead of every loaded AgentTask. | DG `C540_ClaimRollbackKeepsLaterQueuedTaskCustody` (both `afterSave` variants): the second failed task persists Failed with one Failed event, not Queued. |
| G-28 / PC-28: queue late-confirm before resubmission (review F2) | Primary: make `LateConfirmAttemptedMessagesAsync` return without confirming (both paths). Variant Sent: bypass it only in interrupted-Sent `RecoverDeliveryRunLockedAsync`. Variant Pending: bypass it only on the Pending delivery path reached after that recovery reverts the row. Each single-path variant may be rescued by the other path; Mutation records whether each alone reaches red (a green single variant is a control finding, not a pass). | DE `C540_PostPromptCrashDoesNotRetype`: after real eligibility the original row is Sent/LateConfirmed with its original attempt count, and each intent has exactly one complete UserPrompt/native input. |

The absence of new cleanup/publication writes is a structural scope constraint,
verified by diff review plus R-2's unchanged refs/files, not an invented runtime
guard to mutate. Do not introduce a deletion implementation just to test it.
Tie-break permutations, diagnostic independence, transitive coverage assignment
and probe-count economy have ordinary functional tests; their individual sorting
and formatting branches do not authorize data loss or discharge receipt custody.

Code implements tests and runs ordinary V/R. Ordinary Review checks implementation,
evidence and pending PCs before land. Mutation runs **all 28** controls after land,
one precise method filter per red/restore/green cycle. PC-11..PC-14 share one
parameterized method; run that exact method for each independent mutation, not the
whole class. Source/build/fixture errors or zero tests are not red evidence.

### Out of scope

- CARD-0508 S3 base selection/chaining, public BaseRef wiring and moving the guard
  under the lease. Same-ref movement after observation remains a stated limitation.
- Historical cleanup/backfill or removal of already committed warnings, and
  uncommitted-work equivalence. Commit ancestry authorizes only warning reduction.
- Reimplementation or exhaustive requalification of every unchanged CARD-0508
  migration/attention/retention guard. No new persisted type or schema is introduced.
- Hosted-model or browser visual canaries. This is server-side producer behavior;
  native FakeGrok through the real queue is the recipient proof. It does not prove
  downstream human chat presentation, which is outside this card.
- Exhaustive topology x crash x busy/idle Cartesian products. Pure/Git/producer
  cases establish topology, native mixed topology retains two independent tips,
  idle and busy each have a baseline, and every custody handoff has a crash/failure
  case. All post-capture rows share the same immutable delivery path.

### Execution and cost

All figures are estimates, not measured results. Test authoring, ordinary review
analysis and unexpected-failure triage are additional to the execution floor.

| Ordinary Code work | Estimate |
|---|---:|
| Isolated Tests/E2E builds, native prerequisites and current client bundle | 15 min |
| New pure/Git/dispatcher/component cases V-1..V-9 | 20 min |
| Existing focused R-1..R-4 | 20 min |
| Ten native cases V-10..V-19, 3 min each | 30 min |
| R-5 four expanded native land regression cases, 3 min each | 12 min |
| One full Antiphon.Tests pass, chunked by discovered disjoint namespaces/classes | 120 min |
| Document/diff verification and result inventory | 1 min |
| **Ordinary floor (15 setup + 203 V/R)** | **218 min** |

| Post-land Mutation work | Estimate |
|---|---:|
| Owned SourceLanding setup/build | 10 min |
| 23 component/Git PCs: each 0.5 apply/build + 0.75 red + 0.5 restore/build + 0.75 green | 57.5 min |
| PC-18/21/25: each 0.5 apply/build + 3 red + 0.5 restore/build + 3 green | 21 min |
| **Mutation floor** | **88.5 min** |

**Total verification floor: setup/build 25 + ordinary V/R 203 + all PC cycles
78.5 = 306.5 minutes (5 h 6 min 30 s), estimated.** Costs count repeated coverage rather
than silently assuming the full pass replaces earlier slice checks. The full-suite
estimate follows the conservative CARD-0508 planning allowance; it is not a
timeout or a claim about current suite speed.

Method-scoped native PCs versus rerunning the ten-method native class save an
estimated `3 PCs * 2 runs * (30 - 3) = 162 minutes`. Claim no batching/sharding
savings: these controls share reducer/dispatcher/delivery files and the sourced
Mutation task owns one recorded snapshot. No coverage is removed for those savings.

Ordinary commands after implementation (PowerShell; fresh results directory per
invocation, and verify every intended class/method and nonzero count in TRX):

```powershell
dotnet build tests/Antiphon.Tests --property:OutputPath=bin-c540/ --nologo
dotnet build tests/Antiphon.E2E --property:OutputPath=bin-c540/ --nologo
dotnet run --project tests/Antiphon.Tests --no-build --property:OutputPath=bin-c540/ -- --treenode-filter '/*/Antiphon.Tests.Application/(SiblingWarningReducerTests*)|(WorktreeBaseSelectionTests*)|(AgentTaskDispatchBaseGuardTests*)|(DispatchBaseNotificationTests*)|(DelegationWorktreeTests*)/*' --report-trx --report-trx-filename focused.trx --results-directory .antiphon/c540-focused-01
dotnet run --project tests/Antiphon.E2E --no-build --property:OutputPath=bin-c540/ -- --treenode-filter '/*/*/DispatchBaseWarningDeliveryE2ETests/C540_*' --report-trx --report-trx-filename native.trx --results-directory .antiphon/c540-native-01
```

Rebuild `client/dist` from `client/` with `npm run build` before native E2E because
the shared real Program fixture requires the current prebuilt bundle. For R-5 run
each named method with the exact-method filter. Run the full Tests suite **once**
as disjoint namespace chunks, using the current namespace inventory; further
splitting the large Application namespace into disjoint class selections is
allowed for foreground windows. After fixes, target affected methods/classes only.

Example PC command (both red and restored green use their own fresh result folder
and a newly built output, without `--no-build`):

```powershell
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-c540-pc/ -- --treenode-filter '/*/*/AgentTaskDispatchBaseGuardTests/C540_AncestorChainCollapses' --report-trx --report-trx-filename pc03-red.trx --results-directory .antiphon/c540-pc03-red
```

For SourceLanding execution, replace the example results paths with the assigned
**external evidence root**. Never commit/push from that snapshot. Await all owned
commands; freeze source during long runs; refresh restored file timestamps or
force a rebuild so green cannot reuse mutated binaries. Run Antiphon.Tests and
native/Pty assemblies sequentially. Use per-test DB schemas and the established
assembly-local `ParallelLimiter<ProcessSpawnLimit>` for subprocess tests. If a
failure appears inherited, rerun that exact method at the base commit before
making the claim; do not widen timeouts, retry counts or assertions.

Inventory producer-created alternate outputs before/after builds, validate every
resolved deletion path stays inside the owned workspace, and remove only those
outputs when finished. Report test counts/failures against the exact verified SHA.
See [testing/build operations](../../testing-and-build.md) for the canonical rules.

### Readiness and execution evidence

Design inspection complete; guards=26, mapped=26, missing=0, duplicate PC
mappings=0. All PCs name a compiling defect and a decisive exact-method assertion.
Missing native dispatch setup is assigned to S3 with explicit receipt and fault
boundaries. No test/build/PC was run during this Plan task. Document checks alone
do not establish product behavior. Code must implement/run the V/R inventory,
then hand off ordinary Review; all deliberate PCs remain post-land work.

### Code execution checkpoint (2026-09-16)

[Code verification and exact evidence inventory](../../investigations/2026-09-16-card-0540-code-verification.md)
records the implementation, every V/R ID and all pending PC variants. V-1..V-19
pass. R-1/R-3/R-4 pass; R-2 has five exact-method baseline failures. R-5 has two
passes and two exact-method baseline failures; the extra shared-fixture V23 case
passes. Ordinary verification is complete. The stage brief selects Unit plus named affected integrations and
native methods, superseding the full-assembly estimate above. No deliberate PC has
run, and no feature-branch land or deployment has occurred. The evidence artifact
records the inherited reds and approval-review-blocked output-directory removal.
