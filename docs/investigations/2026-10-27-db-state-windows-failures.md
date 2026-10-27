Static analysis is complete: these seven Windows failures across six classes support fixture isolation, fixture lifetime/sharing, and Git positive-control repairs. No task-before-parent insertion defect was found. The 23503 attribution remains probable, because the digest does not identify the missing task or its deleting operation. All six proposed repairs are already present in the fetched rc branch; native closure remains outstanding.

Task: `9463cd6b` (Debug, report only). No product or test source was changed. No build, test, native process lane, desktop access, or new witness was run by this task. Earlier witness results below are explicitly attributed to their stored report, not claimed as this task's execution.

**Source and evidence identity**

- Historical Windows run: `8f055f87`, candidate `105792f7d22f297746cdb338976f52e424b76fa7`, rc/2026-10-25.
- Early digest: `9d7bd87b67c786b28f50920c68cfd0611ab630a5`, `docs/investigations/2026-10-26-rc-8f055f87-windows-failure-digest.md`, fetched `origin/feat/card-task-d5c669aa`.
- Whole-run digest: `c7ad9e901c6be169fb5a0f43be8e210211e9081b`, `docs/investigations/2026-10-27-rc-8f055f87-final-run-digest.md`, fetched `origin/feat/card-task-7e719c57`.
- Prior fixture repair evidence: `920ff8fc83d3fa6db1c43142264b8561c5f150a7`, `docs/investigations/2026-10-26-task-7f7aa584-windows-fixtures.md`; its compiled fixture source was `90eb3a8809ade4d13a95e7e2f95ee73ffd19f4ff`.
- Assigned checkout started at `718bd29f4a1504c64c491d607d31403fcb87dae2`, branch `feat/card-task-9463cd6b`. Fetched rc/2026-10-27 was `d1d090f766451797a3bd9b30c9d2966f2f8c034c`. These are different histories, with merge base `a97194ee7a4adf215454fd82c453ba46362596ee`; no reset/rebase/branch switch was performed. All six subject test files, RunnerSeatReleaseFixture, LiveFixtureText, AntiphonWebAppFactory, AgentTaskDispatcher, AgentTaskPipelineStatusService, and RepositoryMutationLease are byte-identical between those two tips. Analysis therefore applies to the fetched rc source despite the checkout-base discrepancy.
- AgentTaskDispatcher, AgentTaskService, AgentTaskPipelineStatusService, AppDbContext, RepositoryMutationLease, and TerminalRunnerSeatReleaseService also have no source delta from the historical candidate to this checkout. The insert-order conclusions below concern the code that ran, not a later product repair.

Line references below use the assigned checkout unless explicitly marked historical. The original TRX remains red; a targeted repair or a Linux pass does not rewrite that result.

| Class / original failure | Classification | Minimal repair and disposition |
|---|---|---|
| AgentTaskStandingAgentDispatchTests / F028, group 42 | Probable shared-database interference during a fleet-wide tick; no evidence of child-before-parent insertion | Give the row-write witness its own cloned database and dispose its provider before dropping it. Already present. Missing historical task ID prevents a definitive deletion attribution. |
| CardFilePrivacySyncTests / F014, group 35 | Shared database identity collision between parameter cases | Qualify the external issue canary by the private card GUID. Already present; earlier Linux red/green confirms this mechanism. |
| AgentTaskPipelineEndpointTests / F033, group 51 | Probable shared HTTP-factory database/settings state | Dedicated factory instance and database. This is exactly sibling `81924e54`; its patch is already on rc under another SHA. No duplicate repair. Historical effective policy was not captured. |
| ChannelOutboundRecoveryTests / F025-F026, group 37 | Windows reciprocal file-sharing failure in the fixture observer | Read the live marker with ReadWrite/Delete sharing and wait for complete marker content. Already present. |
| TerminalRunnerSeatReleaseTests / F004, group 32 | Fixture operation not joined on an exceptional path; Windows exposes the held exclusive file handle | Put the boundary wait inside finally coverage and join release plus answer before disposing the fixture. Already present; underlying timeout, if any, still needs native diagnosis. |
| GitWorkspaceServiceIndexLockTests / F034, group 54 | Git positive-control preconditions/environment, sensitive to platform; NTFS granularity not established | Clean content with stale stat data, old index timestamp, explicit GIT_OPTIONAL_LOCKS=1 in direct control. Already present. |

**1. Standing dispatch: trace of the parent and event writes**

The failing constraint is `FK_AgentTaskEvents_AgentTasks_AgentTaskId` (group-42 log line 23), not an AgentTask parent-task constraint. The failed method is `and_it_writes_nothing_at_all_on_the_agent_row`. The digest exposes only six EF save frames, not the missing key, application caller frame, original dispatch exception, or a deleting transaction.

The historical method awaits SeedStandingAgentAsync, SeedProjectAsync and SeedQueuedTaskAsync before TickAsync. The task seed adds the AgentTask and awaits SaveChangesAsync before returning (historical lines 712-715; current `tests/Antiphon.Tests/Application/AgentTaskStandingAgentDispatchTests.cs:719`). There is no fire-and-forget parent save. The ordinary product creation path likewise tracks AgentTask and its Created event in one context before SaveChanges (`server/Application/Services/AgentTaskService.cs:1686`, `:1745`), with the relationship defined at `server/Infrastructure/Data/AppDbContext.cs:2464`.

Dispatch subsequently reads already-persisted queued tasks (`server/Application/Services/AgentTaskDispatcher.cs:525`). DispatchOneAsync begins a transaction, selects the existing task FOR UPDATE and rejects an absent/nonqueued/stale-token row (`:5530`, `:5542`). PlaceOnStandingAgentAsync changes that claimed task and adds its Dispatched event (`:8502`, `:8513`); the caller saves and commits both before delivering the queued message (`:5703`). The successful standing path thus cannot insert an event for a parent that it has not yet inserted: it requires the pre-existing parent and locks it through the save.

The actual fixture hazard is broader. The historical harness defaults to TestDbFixture.ConnectionString, and TickAsync scans all queued rows, not just this test's GUID/project. `[NotInParallel("AgentQueue")]` coordinates only that test group; it does not establish exclusive ownership of the database. Other shared-store fixtures remove their own task rows during cleanup (for example `tests/Antiphon.Tests/Application/AgentTaskCallerResolutionTests.cs:273`). Such a row can have been included in this tick's earlier snapshot. This is a concrete interference route, not identification of the historical deleting fixture.

The error-reporting path can amplify a stale snapshot: pre-claim work may throw (`AgentTaskDispatcher.cs:5477` onward); the catch invokes FailAndNotifyAsync (`:1120`), and FailAsync adds a Failed event for the supplied task ID and saves without a fresh locked existence check (`:7867`, `:7885`, `:7906`). A task deleted after snapshot and before this save can produce the observed FK refusal. A simple deletion followed by an otherwise healthy claim is insufficient: the claim's null guard already handles that case. A deterministic diagnostic must include the failure/hold write that uses the stale snapshot.

Decision: use the fixture-isolation repair for this failure, with moderate confidence in historical causality. Do not reorder task/event inserts, relax the FK, or add a blind retry. Production retention explicitly excludes live task roots and applies an age cutoff (`server/Application/Services/DataRetentionService.cs:418`, `:451`); the digest does not establish a production operation deleting this fresh queued task. It also does not prove all other deletion races impossible.

Minimal change: `AgentTaskStandingAgentDispatchTests.cs:90` creates one isolated store, passes it through dispatcher/seed/read helpers, and owns the provider with async disposal. Despite the helper's name, CreateIsolatedSchemaAsync creates a cloned database (`TestDbFixture.cs:65`). The existing repair covers the reported method; other shared-store methods in the class remain a separate broader isolation consideration.

Witness: run the exact row-write method on the isolated source. The prior 7f7aa584 report says both original and repaired isolated Linux selections passed; there was no reproduced 23503 red. If native 23503 survives, retain the failing task ID, database identity, tracked event type/ID, full application stack and the original dispatch error. A separate deterministic test can pause AfterQueuedSnapshotAsync, delete a different queued row on a second connection, and force that row's pre-claim failure before FailAsync; this distinguishes failure-recording hardening from the already-safe absent-row claim. Commission a product repair only with that evidence. Any such repair should reload/lock and conditionally update the parent and event in one transaction, rather than rely on a racy AnyAsync existence check.

**2. Privacy sync: the unique identity is global, not board-scoped**

The two `[Arguments(false)]` / `[Arguments(true)]` cases create different cards and projects but historically insert the same ExternalId `C408_PRIVATE_CARD_ID` with the same default TrackerKind (`CardFilePrivacySyncTests.cs:169`, `:184`). The unique index is `(TrackerKind, ExternalId)`, not CardId or BoardId (`AppDbContext.cs:1943`). CardFilePrivacyWorld defaults to the shared connection (`tests/Antiphon.Tests/TestHelpers/CardFilePrivacyWorld.cs:13`, `:21`). Its cleanup removes only its own board's rows (`:63`), so it cannot prevent simultaneous parameter cases from colliding before either disposes.

The 23505 happens at fixture SaveChanges (`CardFilePrivacySyncTests.cs:193`), before SyncAsync (`:195`). It is not evidence of product sync duplicating an external issue or exporting private metadata.

Minimal change: `:185` uses `C408_PRIVATE_CARD_ID_{privateId:N}`. Keep the recognizable private-data prefix: the existing file, index and committed-tree assertions still reject any occurrence of `C408_PRIVATE_CARD` (`:209`, `:214`, `:220`). No production index or privacy assertion should change.

Witness: both parameter cases together. The stored 7f7aa584 report records original Linux 1 passed / 1 failed with this 23505, repaired 2 passed / 0 failed. Those results refute an OS-only explanation; native confirmation is still owed.

**3. Pipeline advisory: overlap with d42f9a7c**

The missing assertion is Plan RecommendedInFlight=1 (`tests/Antiphon.Tests/Application/AgentTaskPipelineStatusTests.cs:993`). The historical class shared the session-wide AntiphonWebAppFactory with unrelated HTTP fixtures. That factory owns one cloned database per instance (`tests/Antiphon.Tests/TestHelpers/AntiphonWebAppFactory.cs:54`). ResetAsync clears IResettableCache instances only; it does not remove database rows or restore settings (`:171`). Serial execution therefore does not eliminate retained state.

The endpoint intentionally uses persisted effective concurrency policy when available, not unconditional static role defaults (`server/Application/Services/AgentTaskPipelineStatusService.cs:350`). Initialization keeps an existing global settings row (`server/Application/Services/DispatchConcurrencySettingsService.cs:47`); clearing caches does not replace its seed/overrides. A prior shared-host setting change can therefore change the returned recommendation while the endpoint behaves correctly.

Sibling commit `81924e54e7e7a638acd19c951412295752a16186` changes the class datasource, field and constructor to AgentTaskPipelineWebAppFactory, a dedicated subclass (`AgentTaskPipelineStatusTests.cs:947`, `:958`, `:1163`). This obtains a separate database and host state. The identical patch is present here as `8458a0a79bec896fa8df86a7e43cfd64e66b6d10` and on fetched rc as `a73a7848f8ed549eef1fb02456788089e7997c3f`. The original SHA need not be an ancestor after cherry-picking. No additional patch is proposed.

Decision: the existing fix addresses the shared-factory leak described in the brief. Static inspection cannot certify that it explains the historical Plan value: the digest contains neither the returned stage DTO nor the effective settings/revisions, and no specific preceding writer was identified here. The previous fixture report records one standalone Linux endpoint pass, which does not exercise prior contamination.

Witness: mutate Plan's effective limit in the ordinary shared factory, clear its caches, then execute the dedicated pipeline factory's default-contract assertion; assert Plan=1, Code=2, Review=2 and an empty fleet, and verify the two database identities differ. For native closure, run the exact method in the original group context and capture the DTO plus effective policy sources/revisions if it fails. Do not change the expected recommendation merely to accommodate leaked state.

**4. Outbound barriers: reciprocal sharing and complete content**

Both errors occur at historical ChannelOutboundRecoveryTests.cs:684, opening the fixture barrier. The conversion-dispatched child creates that file with write access and FileShare.Read, writes the complete marker, flushes, closes, then waits (`tests/Antiphon.ChannelOutbound.Probe/Program.cs:291`). Another path writes dispatch-ready with WriteAllTextAsync (`:113`). File.Exists can become true while those writers are still open.

A reader that shares only Read does not grant the already-open writer its Write access; the writer allowing reads supplies only one direction of Windows' sharing check. The observed IOException is consistent with precisely this interval. The production delivery queue has not failed at this assertion boundary.

Minimal change: route the three live-marker reads through `tests/Antiphon.Tests/TestHelpers/LiveFixtureText.cs:9`, using `FileShare.ReadWrite | FileShare.Delete`. The polling condition at `ChannelOutboundRecoveryTests.cs:686` requires the complete expected marker, rather than any nonempty prefix. Sharing alone does not make a partially written marker complete. No delete-on-close flag is involved at these cited writer sites.

Witness: `LiveFixtureTextTests.Barrier_reader_shares_an_existing_writer_and_preserves_partial_text` models the reciprocal share requirement for empty, partial and complete content (3 cases). The prior report records 3 red -> 3 green source probes and 3 passing TUnit cases, plus all 5 process-death cases and the separate running-converter case passing on Linux. The model does not constitute native handle evidence. Native reruns remain the two originally failed methods, including conversion-dispatched.

**5. Seat release: teardown can mask a boundary timeout**

The original error is from RunnerSeatReleaseFixture.DisposeAsync deleting its root, through ReadOnlyDirectory.Delete. The fixture started `release = f.ReleaseAsync(...)`, then awaited the ten-second entered barrier outside its try/finally. If that wait threw, fixture disposal proceeded without resuming or joining release. The later old finally also awaited release before answer, so a release exception could skip joining answer.

Release performs publication verification before reaching BeforeDispatch (`server/Application/Services/TerminalRunnerSeatReleaseService.cs:633`). Local publication verification obtains a repository lease (`server/Infrastructure/Git/LocalTaskParkPublisher.cs:53`); its landing.lock stream uses FileShare.None (`server/Infrastructure/Git/RepositoryMutationLease.cs:54`) and is released by disposing the operation-owned lease (`:151`). The fixture cannot dispose an in-flight operation merely by disposing its root service provider. On Windows, a still-open handle lacking delete sharing prevents recursive deletion.

Minimal change: `tests/Antiphon.Tests/Application/TerminalRunnerSeatReleaseTests.cs:625` initializes answer to CompletedTask, puts barrier wait and answer creation inside try, then resumes and awaits Task.WhenAll(release, answer) in finally (`:632`). Keep the ten-second budget. Keep the exclusive production lease; neither FileShare.Delete on landing.lock nor a deletion retry is the correction. Existing fixture diagnostics at `RunnerSeatReleaseFixture.cs:650` should retain a remaining handle refusal. Clearing read-only attributes cannot release an open handle.

Witness: force the barrier timeout while the release owns a synthetic lease; assert release and any answer are joined before root disposal. The earlier report records 1 red -> 1 green source-extracted control-flow witness and passing original/repaired Linux scenario. The original digest has no handle-owner trace or primary timeout, so this is a demonstrated exceptional-path flaw and a probable explanation of F004, not proof of its exact schedule. The native rerun may now reveal the original timeout instead of a cleanup IOException; that timeout would remain a failure requiring diagnosis.

**6. Git index: the direct positive control is the failed assertion**

The service no-write assertion passed; the direct Git status control left the same index timestamp (historical GitWorkspaceServiceIndexLockTests.cs:32). The original test dirtied tracked content, then dirtied it again. That does not establish a refreshable clean stat-cache entry. Its direct ScratchGitRepo child also inherited GIT_OPTIONAL_LOCKS unless explicitly overridden (`tests/Antiphon.Tests/TestHelpers/ScratchGitRepo.cs:45`). In contrast, the service deliberately sets it to 0 for read-only commands (`server/Application/Services/GitWorkspaceService.cs:1154`).

Minimal change: `tests/Antiphon.Tests/Application/GitWorkspaceServiceIndexLockTests.cs:20` leaves content clean, makes the tracked file's stat time stale, sets the index time to 2001, verifies the service leaves it unchanged, then invokes direct Git with GIT_OPTIONAL_LOCKS=1 and requires a different timestamp (`:31`). This establishes a refresh opportunity and avoids relying on two writes landing in different clock ticks. Do not add a longer sleep or weaken the final assertion.

The historical timestamp alone does not prove coarse NTFS granularity, a read-only attribute, or an inherited optional-lock environment value. The stored repair report reproduces the same positive-control failure on Linux with inherited GIT_OPTIONAL_LOCKS=0 (1 failed), then passes the repaired method under that same environment (1 passed). That proves a real fixture weakness; it does not identify the exact Windows host configuration. Native closure should retain the Git version, explicit optional-lock setting, relevant core configuration, and before/after index timestamps/stat data if still red.

**Already-integrated repair identities**

| Repair | Assigned checkout commit | Fetched rc/2026-10-27 commit |
|---|---|---|
| Standing row witness isolation | `e8dc7893eedbb59ed6eabfd695fac491a1bdf017` | `cb44359efadd25b3c1c6146344cfad3a333783e7` |
| Unique privacy canary | `1dfd25b269e54467aa7bc18d4ad60b2d0007c5ed` | `08165415d55ae2c77215351eff9ab62227a4f55f` |
| Dedicated pipeline factory | `8458a0a79bec896fa8df86a7e43cfd64e66b6d10` | `a73a7848f8ed549eef1fb02456788089e7997c3f` |
| Live barrier sharing fix | `9dfbbd8b69ac1e7342ebdc8e2a4fa9b2faacd949` | `0a97b42d874f1b81eddc53f41cf8435757c5de95` |
| Join release on barrier failure | `d16e6d9a7d17b00891632616224c67495b60bab3` | `fde037c2dddb53c0fe53b8a134422119ab05a08f` |
| Deterministic index control | `5cd8fb9511ff0eb0e6457f0e309c00f6a33afaaa` | `0aa7bd4a76237e9479391b2092644f0155a44c27` |

**Exact remaining native selections**

Coordinate with the already-running desktop owners `32365cdf`, `fb80be16`, `595bc226`; this report does not launch duplicate runs. Each filter uses the TUnit prefix `/*/*/`. Use fresh, source-bound results and the host build-slot/checkpoint mechanism. These are proposed closure selections, not executed rows or green receipts from this task.

| Filter suffix | Required focus |
|---|---|
| `AgentTaskStandingAgentDispatchTests/and_it_writes_nothing_at_all_on_the_agent_row` | Isolated DB; capture missing key/original exception if 23503 survives. |
| `CardFilePrivacySyncTests/Private_card_contributes_no_filename_index_count_or_metadata` | Both parameter cases together. |
| `AgentTaskPipelineEndpointTests/pipeline_route_is_literal_and_returns_the_advisory_contract` | Original shared-host group context or deliberate prior contamination; retain effective policy on failure. |
| `ChannelOutboundRecoveryTests/Process_death_preserves_ownership_at_each_boundary` | Five cases, especially conversion-dispatched; complete marker reads. |
| `ChannelOutboundRecoveryTests/Running_converter_reconciles_after_dispatcher_death` | dispatch-ready marker, owned child cleanup. |
| `TerminalRunnerSeatReleaseTests/Answer_racing_release_preserves_one_owner` | Preserve any primary barrier failure after joined teardown. |
| `GitWorkspaceServiceIndexLockTests/Status_never_takes_the_optional_index_lock` | Positive control explicitly enables optional locks. |

No new production repair is justified by this static analysis alone. Keep required native verification incomplete until the existing owners return trustworthy receipts. The known fixture flaws and missing execution are separate from the unresolved historical identity/scheduling details. Do not duplicate the already-integrated patches or reinterpret their Linux evidence as Windows acceptance.

--- next stage ---
next: code
handoff: Use the existing native rerun owners to close these exact selections against the repaired rc source. Preserve any remaining failure; commission a targeted repair only from its new evidence. The 23503 needs the missing task ID and application stack if it survives isolation. No overlapping fixture patch is needed.
artifact: docs/investigations/2026-10-27-db-state-windows-failures.md
