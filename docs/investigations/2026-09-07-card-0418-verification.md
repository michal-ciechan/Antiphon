# CARD-0418 verification index

Implementation worktree: `C:\Antiphon\worktrees\card-task-7a479eaf`
Plan: `docs/superpowers/plans/2026-09-07-card-0418-channel-outbound-agent-plan.md`
Code task: `7a479eaf` (landing owner)
Verified SHA: see each stage's report HEAD (includes this file)

Isolated output: Code stage `--property:OutputPath=bin-c0418/`, follow-up pass
`--property:OutputPath=bin-c418v/` (forward slash; both removed after their runs).

## Code stage (2026-09-13)

| ID | Filter / class | TRX | Executed | Pass | Fail | Skip |
|---|---|---|---|---|---|---|
| Unit lane | `/*/*/*/*[Category=Unit]` | `.antiphon/c0418-unit/c0418-unit.trx` | 2306 | 2304 | 1 inherited `ScopedVerificationInstructionTests.C487_G142` | 1 symlink privilege |
| V-1/V-2/V-3/V-5-defaults/V-7-gates/V-21 | `DeliverableBundleServiceTests`, `SourceBundleManifestTests`, `ChannelOutboundContractTests`, `ChannelContractsTests`, `ChannelPreamblePresetEndpointTests`, `ChannelFollowUpAttachmentTests`, `ChatChannelServiceTests`, `InstructionBundleTests` | `.antiphon/c0418-int/c0418-int.trx` | 104 | 104 | 0 | 0 |
| V-1 settlement via reply | `AgentTaskReplyIntegrationTests` two methods | `.antiphon/c0418-reply2`, `.antiphon/c0418-reply3` | 1+1 | 1+1 | 0 | 0 |
| V-6/V-16 subset | `ChannelReplyDurabilityTests` + `ChannelBridgeTests` + remaining `AgentTaskReplyIntegrationTests` | `.antiphon/c0418-int2/c0418-int2.trx` | 191 | 188 | 3 (2 fixed after; 1 inherited session-limit date) | 0 |
| V-5 defaults | `ChannelOutboundPolicyTests.Profile_binding_defaults_and_validation` | `.antiphon/c0418-policy/c0418-policy.trx` | 1 | 1 | 0 | 0 |
| V-19 | `MarkdownPdfRendererTests` + `MarkdownPdfCommandTests` | `.antiphon/c0418-mdpdf/c0418-mdpdf.trx` | 5 | 5 | 0 | 0 |
| Client | `scripts/test-client.ps1 ChannelsPage.test.tsx` | logs/client-tests.log | 2 | 2 | 0 | 0 |

## Follow-up pass (2026-09-14, task `965784c3`)

Authors the TestDesign classes the Code stage left unwritten and runs them.

| ID | Class / filter | Executed | Pass | Fail | Skip |
|---|---|---|---|---|---|
| V-12, V-13 (output) | `OutboundConversionManifestTests` | 33 | 32 | 0 | 1 reparse (needs `SeCreateSymbolicLinkPrivilege`) |
| V-6, V-7, V-8, V-14, V-16, V-17, V-23 | `ChannelOutboundDeliveryTests` | 24 | 24 | 0 | 0 |
| V-9, V-13 (input) | `ChannelOutboundStorageTests` | 11 | 11 | 0 | 0 |
| V-11 | `ChannelOutboundDeadlineTests` | 13 | 13 | 0 | 0 |
| V-5 (endpoints) | `ChannelOutboundEndpointTests` | 12 | 12 | 0 | 0 |
| V-10 | `OutboundConversionTaskTests` | 3 | 3 | 0 | 0 |
| V-21 | `ChannelOutboundMigrationTests` | 2 | 2 | 0 | 0 |
| V-15 / F-3 | `ChannelOutboundRecoveryTests` + `Antiphon.ChannelOutbound.Probe` | 6 | 6 | 0 | 0 |
| Combined | all CARD-0418 classes in one run | 138 | 137 | 0 | 1 |
| Unit lane | `/*/*/*/*[Category=Unit]`, `.antiphon/c418-unit/unit.trx` | 2307 | 2305 | 1 inherited `C487_G142` | 1 symlink privilege |
| V-20 | `MarkdownPdfRealBrowserTests`, `ANTIPHON_HEADED_TESTS=1` on real Edge/Chrome | 1 | 1 | 0 | 0 |
| V-22 | `GatewayTests`, `SlackChannelAdapterTests`, `TelegramChannelAdapterTests`, `GatewayMonitorValidationTests`, `ConsumerLagAssessmentTests`, `InboundUnconsumedMonitorTests`, `GatewayMonitorStatusTests`, `LibrarySufficiencyTests` (`Antiphon.Messaging.Tests`) | 120 | 120 | 0 | 0 |
| V-22 | `ChannelConsumerIdentityEndpointTests` + `ChannelOutboundContractTests` + `ChannelOutboundPolicyTests` | 4 | 4 | 0 | 0 |

V-20 artifacts: 4 pages, each document on its own page, `Zażółć gęślą jaźń 🙂`
intact inside a table cell, fenced code and task list preserved. Retained under
`ANTIPHON_MDPDF_EVIDENCE` when set (PDF plus per-page extracted text). Page
RASTERS are not produced — no rasterizer ships here, so visual inspection is a
human step on the retained PDF.

### Still not run

| ID | What it needs |
|---|---|
| V-23 (broker half), V-24 | `ChannelOutboundIsolatedTests` under `tests/Antiphon.E2E`: an explicitly configured `AntiphonAppFixture` on a private database with `IsolatedSessionRunner` on a random port, a disposable Redpanda (the `KafkaConsumerGroupObservationTests` pattern), `FakeSlackServer` shared by linked source, a deterministic fixture CLI registered as the converter through a supported delegatable kind, and `ANTIPHON_HEADED_TESTS=1` + `ANTIPHON_BROKER_TESTS=1` + `ANTIPHON_CHANNEL_OUTBOUND_ISOLATED=1`. Not authored in this pass. The serialization-budget half of V-23 IS covered in `ChannelOutboundDeliveryTests.Serialized_payload_budget_includes_all_fields`; what is missing is a real broker accepting the payload. |
| V-25 | The live mav-ref installation, an approved converter profile and an active mikeysbot-slack thread. Out of scope for a local pass. |
| PC-1..PC-30 | Mutation stage. Not executed here by instruction. |

## Reply-group re-run at `f4e784cc` (2026-09-14, task `6886d58b`)

The Code stage's 191-test reply group was last executed at `ea77a5b3`, before two
of its cases were fixed, and the follow-up pass could not re-run it inside its
foreground window. Re-run here in three class-sized chunks against `f4e784cc`
(build `--property:OutputPath=bin-c418f/`, removed after the runs).

| Class | TRX | Executed | Pass | Fail | Duration |
|---|---|---|---|---|---|
| `ChannelBridgeTests` | `.antiphon/c418f/c418f-bridge.trx` | 40 | 40 | 0 | 11m13s |
| `ChannelReplyDurabilityTests` | `.antiphon/c418f/c418f-durability.trx` | 24 | 23 | 1 inherited | 24s |
| `AgentTaskReplyIntegrationTests` | `.antiphon/c418f/c418f-reply.trx` | 127 | 127 | 0 | 1m01s |
| **Total** | | **191** | **190** | **1 inherited** | |

The one failure is the already-documented
`ChannelReplyDurabilityTests.Claude_production_session_limit_stub_withholds_and_adopts_the_AssistantText_reset`.
Attribution confirmed rather than assumed: `git diff --name-only $(git merge-base
master HEAD) HEAD` touches neither that test file nor any session-limit
production code, and the assertion hard-codes
`new DateTime(2026, 9, 5, 16, 22, 0, DateTimeKind.Utc)` against a stub the parser
resolves relative to the wall clock — a calendar time bomb that has been red
since 2026-09-06 and is red on `master` for the same reason. Not a CARD-0418
regression; it needs its own fix, not a loosened assertion here.

With this, every production change on the branch is covered by an executed
green run at the branch HEAD. The only production delta between the Code stage
`ea77a5b3` and `f4e784cc` is inside
`server/Application/Services/OutboundConversionManifest.cs`
(`UnpackSourceZip`), which has no production caller (finding 4), so no other
previously-green run is stale against the current source.

## Findings


1. **Nonacceptance is never retried and never terminal.** Every producer
   exception becomes `PublishUncertain`, and `PumpOnceAsync` selects only
   Pending/Converting/Ready — so a refused send is never retried and never
   reaches `Failed`, which makes `MaxPublishAttempts` unreachable and the
   configured cap inert. The conservative no-replay half is correct per C-6/C-7;
   what is missing is distinguishing a DEMONSTRABLE nonacceptance (safe to
   retry) from an ambiguous one. Pinned by
   `ChannelOutboundDeliveryTests.Nonacceptance_holds_uncertain_and_is_never_retried_automatically`.
2. **A crash during publication strands the row in `Publishing` for ever.**
   `PumpOnceAsync` does not select `Publishing` and
   `AttentionService.BuildOutboundDeliveryItemsAsync` projects only
   Held/Failed/PublishUncertain, so neither a later tick nor any operator view
   ever sees it again. C-6/C-7 require `PublishUncertain` plus attention. Pinned
   by `ChannelOutboundRecoveryTests.Process_death_during_publication_is_stranded_in_publishing`.
3. **The admission-race loser gets an exception, not the winner's intent.**
   `AdmitDeferredAsync` reads then writes with no handler for the unique-index
   violation it races, so the losing caller sees `DbUpdateException`. The
   database invariant holds (one intent, nothing sent); the caller contract does
   not. Pinned by `ChannelOutboundStorageTests.Concurrent_triggers_claim_one_intent_and_one_task`.
4. **`UnpackSourceZip` has no production caller.** `BuildStagedInputAsync`
   stages inline attachment bytes only, so a settlement source zip triggers
   conversion without its Markdown members ever being expanded for the worker —
   the zip-input half of D-6/V-4/V-6 is unwired.

### Repaired in this pass

`OutboundConversionManifestValidator.UnpackSourceZip` read the extracted file
back while its own `File.Create` handle was still open, so on Windows every
SUCCESSFUL extraction threw `IOException`; and its expanded-byte budget
accumulated `ZipArchiveEntry.Length`, the size the archive declares about
itself, rather than the bytes actually streamed out. Both fixed; the budget is
now enforced per read and a partial file is removed when it trips. No caller
changes (see finding 4).

## Inherited red (not caused by CARD-0418)

- `ScopedVerificationInstructionTests.C487_G142` expects `next: mutation` in
  stage-code; the shipped bundle says `next: review`. Documented at CARD-0407
  evidence and present before this card.
- `ChannelReplyDurabilityTests.Claude_production_session_limit_stub_withholds_and_adopts_the_AssistantText_reset`
  expected a `2026-09-05` hold expiry against a parsed `2026-09-14`
  (calendar/parser clock). Not touched here.

Restart target: none.
