# CARD-0418 Code-stage verification index

Implementation worktree: `C:\Antiphon\worktrees\card-task-7a479eaf`
Plan: `docs/superpowers/plans/2026-09-07-card-0418-channel-outbound-agent-plan.md`
Code task: `7a479eaf` (landing owner)
Verified SHA: see Code report HEAD (includes this file)

Isolated output: `--property:OutputPath=bin-c0418/` (forward slash).

| ID | Filter / class | TRX | Executed | Pass | Fail | Skip |
|---|---|---|---|---|---|---|
| Unit lane | `/*/*/*/*[Category=Unit]` | `.antiphon/c0418-unit/c0418-unit.trx` | 2306 | 2304 | 1 inherited `ScopedVerificationInstructionTests.C487_G142` | 1 symlink privilege |
| V-1/V-2/V-3/V-5-defaults/V-7-gates/V-21 | `DeliverableBundleServiceTests`, `SourceBundleManifestTests`, `ChannelOutboundContractTests`, `ChannelContractsTests`, `ChannelPreamblePresetEndpointTests`, `ChannelFollowUpAttachmentTests`, `ChatChannelServiceTests`, `InstructionBundleTests` | `.antiphon/c0418-int/c0418-int.trx` | 104 | 104 | 0 | 0 |
| V-1 settlement via reply | `AgentTaskReplyIntegrationTests` two methods | `.antiphon/c0418-reply2/c0418-reply-a.trx`, `.antiphon/c0418-reply3/c0418-reply-b.trx` | 1+1 | 1+1 | 0 | 0 |
| V-6/V-16 subset | `ChannelReplyDurabilityTests` + `ChannelBridgeTests` + remaining `AgentTaskReplyIntegrationTests` | `.antiphon/c0418-int2/c0418-int2.trx` | 191 | 188 | 3 (2 fixed after; 1 inherited session-limit date) | 0 |
| V-5 defaults | `ChannelOutboundPolicyTests.Profile_binding_defaults_and_validation` | `.antiphon/c0418-policy/c0418-policy.trx` | 1 | 1 | 0 | 0 |
| V-19 | `MarkdownPdfRendererTests` + `MarkdownPdfCommandTests` | `.antiphon/c0418-mdpdf/c0418-mdpdf.trx` | 5 | 5 | 0 | 0 |
| Client | `scripts/test-client.ps1 ChannelsPage.test.tsx` | logs/client-tests.log | 2 | 2 | 0 | 0 |
| V-20 | `MarkdownPdfRealBrowserTests` | not run (`ANTIPHON_HEADED_TESTS` not set this pass) | 0 |  |  | pending |
| V-4, V-6..V-18, V-22..V-24 | named TestDesign classes not yet authored | — | 0 |  |  | pending |
| V-25 | mav-ref live PDF receipt | — | 0 |  |  | **pending** |

Inherited red (not caused by CARD-0418): `ScopedVerificationInstructionTests.C487_G142` expects `next: mutation` in stage-code; shipped bundle says `next: review`. Documented previously at CARD-0407 evidence. `ChannelReplyDurabilityTests.Claude_production_session_limit_stub_withholds_and_adopts_the_AssistantText_reset` expected `2026-09-05` hold expiry vs parsed `2026-09-14` (calendar/parser clock); not touched here.

PC-1..PC-30 remain pending for Mutation. Restart target: none.
