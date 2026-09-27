# CARD-0418 Code evidence (partial implementation)

Branch: `feat/card-task-ae40541f`. This index records the source-only settlement and optional renderer slices. It is not acceptance of channel opt-in or of the mav-ref destination. Do not land these slices alone: settlement no longer renders PDFs while the channel converter is not yet implemented.

## Implemented source slice

- `DeliverableBundleService` copies original Markdown bytes or writes them to a zip, with a version 1 source manifest. It no longer renders at settlement. It includes all selected paths up to a 64 MiB uncompressed budget and records omitted inputs and an incomplete note beyond that budget.
- New bundles imply only manifest-listed source files. Historical bundles imply only Markdown and `*-sources.zip`; an explicit PDF marker remains supported.
- Server defaults no longer configure the legacy renderer keys; a supplied legacy key produces one startup warning.

## Optional renderer slice

The Markdig renderer and browser invocation now live in `tools/Antiphon.MarkdownPdf`, with a manifest-in/PDF-out command and no server project dependency. Server registration, direct Markdig package and universal PDF prompt wording were removed. `dotnet publish` succeeded into `.antiphon/test-output/card-0418/tool-published/` with no server or messaging DLL in that output. The tool's real-browser artifact and process-tree cleanup checks remain pending on a host with a browser.

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

The plan predates the `### Checkpoints` manifest requirement and has no table. These runs used `scripts/run-checkpoint.ps1` with a build slot and `UseAppHost=false`; the final two rows used the same isolated `bin-c0418/` build. This is an execution-contract gap to resolve before final verification.

## Coverage status

| Plan IDs | Status |
|---|---|
| V-1 through V-3; R-1, R-2 | Partial: source slice has direct and integration evidence; full fixture matrices, cross-project cases and completeness stamping remain pending. |
| V-19 through V-21; R-12 | Partial: tool CLI/renderer and instruction tests; real PDF open/page oracle and process-tree behavior pending. |
| V-4 through V-18, V-22 through V-24; R-3 through R-11, R-13 through R-14 | Pending implementation and verification. |
| V-25 | Pending actual mav-ref migration, authorization and native Slack receipt. |
| PC-1 through PC-30 | Pending method-scoped SourceLanding Mutation; the budget red above is a local guard check, not a PC discharge. |

The whole Unit lane, real-browser renderer, channel policy/UI, durable preparation, ordinary worker, recovery cuts, isolated broker/gateway path and manual acceptance have not run. No live destination or shared stack was changed.
