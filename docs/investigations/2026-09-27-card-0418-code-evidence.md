# CARD-0418 Code evidence (implementation in progress)

Branch: `feat/card-task-4d9c7019` (continued from `feat/card-task-ae40541f`). This index records source-only settlement, the optional renderer and the current channel delivery implementation. It is not final V/R acceptance or acceptance of the mav-ref destination. Do not land until the remaining isolated delivery and recovery evidence below is complete.

## Implemented source slice

- `DeliverableBundleService` copies original Markdown bytes or writes them to a zip, with a version 1 source manifest. It no longer renders at settlement. It includes all selected paths up to a 64 MiB uncompressed budget and records omitted inputs and an incomplete note beyond that budget.
- New bundles imply only manifest-listed source files. Historical bundles imply only Markdown and `*-sources.zip`; an explicit PDF marker remains supported.
- Server defaults no longer configure the legacy renderer keys; a supplied legacy key produces one startup warning.

## Optional renderer slice

The Markdig renderer and browser invocation now live in `tools/Antiphon.MarkdownPdf`, with a manifest-in/PDF-out command and no server project dependency. Server registration, direct Markdig package and universal PDF prompt wording were removed. `dotnet publish` succeeded into `.antiphon/test-output/card-0418/tool-published/` with no server or messaging DLL in that output. The tool's real-browser artifact and process-tree cleanup checks remain pending on a host with a browser.

## Channel binding and delivery slice

- A channel binding selects one configured profile and displays its project, pinned worker, trigger, prompt revision and metered invocation preview. No profile is the default; a clear/unbind removes the selection.
- Agent replies with a selected profile stage a byte-frozen `ChannelReply` and source input before a unique durable intent and correlation link commit. Main, trailing and machine-turn sends use the same facade; control notices bypass conversion. The inbound envelope supplies the native reply handle when present, rather than the catalog's newer handle.
- A periodic pump claims intents, starts a pinned ordinary Worker/Custom task, observes its normal settlement, validates a generic output manifest and publishes the sealed reply. Original sources remain attached. Fallback, revocation, channel hold and uncertain broker acceptance have distinct states. An uncertain send has an explicit acknowledged retry action; attention projects its delivery id, destination and conversion task.
- Source zips are expanded from already attached bytes using the source manifest. Worker output cannot set routing fields and must fit the attachment and serialized bus budgets. The sample PDF worker prompt is `docs/samples/channel-outbound-pdf-agent.md`.

## Executed evidence

| Checkpoint | Filter | Executed | Passed | Failed | Skipped | TRX | Observation |
|---|---|---:|---:|---:|---:|---|---|
| CP-S1-red | `/*/*/DeliverableBundleServiceTests/*` | 9 | 6 | 3 | 0 | `.antiphon/c0418-red/CP-S1-red-20260927-182513-342e/run.trx` | Baseline failed on source-only note, byte identity, and 41st source. |
| CP-S1-budget-red | `/*/*/DeliverableBundleServiceTests/Source_budget_records_omitted_inputs_without_claiming_completeness` | 1 | 0 | 1 | 0 | `.antiphon/c0418-budget-red/CP-S1-budget-red-20260927-184524-5ddc/run.trx` | With the budget guard set to `long.MaxValue`, expected count 1 was 2. The guard was restored. |
| CP-S1-final | `/*/*/DeliverableBundleServiceTests/*` | 10 | 10 | 0 | 0 | `.antiphon/c0418-final/CP-S1-final-20260927-184822-cfde/run.trx` | Source-only, byte identity, 41 files, exact 64 MiB plus one byte, branch-only read. |
| CP-S1-integration-final | `/*/*/(AgentTaskReplyIntegrationTests*)\|(ChannelFollowUpAttachmentTests*)/*` | 320 | 320 | 0 | 0 | `.antiphon/c0418-integrations-final/CP-S1-integration-final-20260927-184957-9a8a/run.trx` | Completion notes, implicit source attachment, explicit PDF and budget regressions. |
| CP-S2-cli-red | `/*/*/MarkdownPdfCommandTests/*` | 1 | 0 | 1 | 0 | `.antiphon/c0418-pdf-red/CP-S2-cli-red-20260927-185453-8785/run.trx` | Stub CLI wrongly exited zero for a missing manifest. |
| CP-S2-browser-red | `/*/*/MarkdownPdfCommandTests/Valid_manifest_with_missing_browser_reports_failure_without_pdf` | 1 | 0 | 1 | 0 | `.antiphon/c0418-browser-red/CP-S2-browser-red-20260927-190200-f713/run.trx` | Temporary guard mutation wrongly returned zero for missing browser. Restored. |
| CP-S2-tool-final | `/*/*/(MarkdownPdfRendererTests*)\|(MarkdownPdfCommandTests*)/*` | 7 | 7 | 0 | 0 | `.antiphon/c0418-pdf-final/CP-S2-tool-final-20260927-190443-a402/run.trx` | CLI validation, missing browser, Markdig HTML, arguments and timeout seam. |
| CP-S2-server-regressions | `/*/*/(DeliverableBundleServiceTests*)\|(AgentTaskReplyIntegrationTests*)\|(ChannelFollowUpAttachmentTests*)/*` | 330 | 330 | 0 | 0 | `.antiphon/c0418-s2-regressions/CP-S2-server-regressions-20260927-191030-0dba/run.trx` | Source settlement and reply attachments after removing server renderer dependency. |
| CP-S2-instructions-red | `/*/*/(InstructionBundleTests*)\|(AgentWorkspaceProvisionerTests*)/*` | 83 | 80 | 3 | 0 | `.antiphon/c0418-instructions-red/CP-S2-instructions-red-20260927-185814-233c/run.trx` | Two intended PDF-wording failures plus the inherited Check interpreter floor failure. |
| CP-S2-instructions-green | same filter | 83 | 82 | 1 | 0 | `.antiphon/c0418-instructions-green/CP-S2-instructions-green-20260927-190127-5c88/run.trx` | New instruction assertions green; Check interpreter floor failure remained. |
| CP-S2-instruction-bundle | `/*/*/InstructionBundleTests/*` | 59 | 59 | 0 | 0 | `.antiphon/c0418-instruction-bundle/CP-S2-instruction-bundle-20260927-191417-e5fe/run.trx` | Full embedded instruction bundle class green. |
| CP-S2-check-floor-baseline | exact Check interpreter floor method on `a0e73c976a91f8ebf02def495c69fe37df55289f` | 1 | 0 | 1 | 0 | `.antiphon/c0418-check-floor-baseline.trx` | Same assertion fails on untouched base; inherited, not introduced by S2. |
| CP-S3-policy | `/*/*/ChannelOutboundPolicyTests/*` | 2 | 2 | 0 | 0 | `.antiphon/c0418-policy/CP-S3-policy-20260927-193124-7fca/run.trx` | Default null binding, profile validation, explicit clear and limits. |
| CP-S4-storage-final | `/*/*/ChannelOutboundStorageTests/*` | 5 | 5 | 0 | 0 | `.antiphon/c0418-storage/CP-S4-storage-final-20260927-201803-a06c/run.trx` | Frozen bytes, bounded input, manifest zip extraction, sealed output and forbidden routing field. |
| CP-S5-worker | `/*/*/ChannelOutboundDeliveryTests/*` | 1 | 1 | 0 | 0 | `.antiphon/c0418-delivery/CP-S5-worker-20260927-201431-42aa/run.trx` | Real ordinary task creation/link, two ordered intents, frozen handles and deferred stamps. Later revocation and retry assertions were added after this run and still need rerun. |
| S3/S5 client | `ChannelsPage.test.tsx attentionVisuals.test.ts` | 25 | 25 | 0 | 0 | `logs/client-tests.log` | Profile selector/clear and attention visuals. |

The plan predates the `### Checkpoints` manifest requirement and has no table. These runs used `scripts/run-checkpoint.ps1` with a build slot and `UseAppHost=false`; the final two rows used the same isolated `bin-c0418/` build. This is an execution-contract gap to resolve before final verification.

## Coverage status

| Plan IDs | Status |
|---|---|
| V-1 through V-3; R-1, R-2 | Partial: source slice has direct and integration evidence; full fixture matrices, cross-project cases and completeness stamping remain pending. |
| V-19 through V-21; R-12 | Partial: tool CLI/renderer and instruction tests; real PDF open/page oracle and process-tree behavior pending. |
| V-5, V-9, V-10, V-12, V-14, V-16, V-18; R-3, R-5, R-6, R-8, R-10 | Partial local policy/storage/worker/publication assertions only. Named endpoint, concurrency, full dispatch, deadlines, fault and recovery matrices remain pending. |
| V-4, V-6 through V-8, V-11, V-13, V-15, V-17, V-22 through V-24; R-4, R-7, R-9, R-11, R-13, R-14 | Pending full isolated and regression evidence. |
| V-25 | Pending actual mav-ref migration, authorization and native Slack receipt. |
| PC-1 through PC-30 | Pending method-scoped SourceLanding Mutation; the budget red above is a local guard check, not a PC discharge. |

The whole Unit lane, final named integrations, real-browser renderer, hard crash cuts, isolated broker/gateway path and manual acceptance have not run. No live destination or shared stack was changed. The V-25 mav-ref migration remains pending a deployment and authorized native Slack receipt.
