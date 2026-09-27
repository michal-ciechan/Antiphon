# CARD-0418 Code evidence (partial implementation)

Branch: `feat/card-task-ae40541f`. This index records the source-only settlement slice. It is not acceptance of channel opt-in or of the mav-ref destination. Do not land this slice alone: settlement no longer renders PDFs while the channel converter is not yet implemented.

## Implemented source slice

- `DeliverableBundleService` copies original Markdown bytes or writes them to a zip, with a version 1 source manifest. It no longer renders at settlement. It includes all selected paths up to a 64 MiB uncompressed budget and records omitted inputs and an incomplete note beyond that budget.
- New bundles imply only manifest-listed source files. Historical bundles imply only Markdown and `*-sources.zip`; an explicit PDF marker remains supported.
- Server defaults no longer configure the legacy renderer keys; a supplied legacy key produces one startup warning. The standalone optional renderer extraction is still pending.

## Executed evidence

| Checkpoint | Filter | Executed | Passed | Failed | Skipped | TRX | Observation |
|---|---|---:|---:|---:|---:|---|---|
| CP-S1-red | `/*/*/DeliverableBundleServiceTests/*` | 9 | 6 | 3 | 0 | `.antiphon/c0418-red/CP-S1-red-20260927-182513-342e/run.trx` | Baseline failed on source-only note, byte identity, and 41st source. |
| CP-S1-budget-red | `/*/*/DeliverableBundleServiceTests/Source_budget_records_omitted_inputs_without_claiming_completeness` | 1 | 0 | 1 | 0 | `.antiphon/c0418-budget-red/CP-S1-budget-red-20260927-184524-5ddc/run.trx` | With the budget guard set to `long.MaxValue`, expected count 1 was 2. The guard was restored. |
| CP-S1-final | `/*/*/DeliverableBundleServiceTests/*` | 10 | 10 | 0 | 0 | `.antiphon/c0418-final/CP-S1-final-20260927-184822-cfde/run.trx` | Source-only, byte identity, 41 files, exact 64 MiB plus one byte, branch-only read. |
| CP-S1-integration-final | `/*/*/(AgentTaskReplyIntegrationTests*)\|(ChannelFollowUpAttachmentTests*)/*` | 320 | 320 | 0 | 0 | `.antiphon/c0418-integrations-final/CP-S1-integration-final-20260927-184957-9a8a/run.trx` | Completion notes, implicit source attachment, explicit PDF and budget regressions. |

The plan predates the `### Checkpoints` manifest requirement and has no table. These runs used `scripts/run-checkpoint.ps1` with a build slot and `UseAppHost=false`; the final two rows used the same isolated `bin-c0418/` build. This is an execution-contract gap to resolve before final verification.

## Coverage status

| Plan IDs | Status |
|---|---|
| V-1 through V-3; R-1, R-2 | Partial: source slice has direct and integration evidence; full fixture matrices, cross-project cases and completeness stamping remain pending. |
| V-4 through V-24; R-3 through R-14 | Pending implementation and verification. |
| V-25 | Pending actual mav-ref migration, authorization and native Slack receipt. |
| PC-1 through PC-30 | Pending method-scoped SourceLanding Mutation; the budget red above is a local guard check, not a PC discharge. |

The whole Unit lane, optional renderer, channel policy/UI, durable preparation, ordinary worker, recovery cuts, isolated broker/gateway path and manual acceptance have not run. No live destination or shared stack was changed.
