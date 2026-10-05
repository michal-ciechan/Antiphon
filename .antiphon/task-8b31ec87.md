# CARD-0519 S11 repair evidence — Code 8b31ec87

All seven reported failures are repaired. CP-15..22 passed 244/244, zero failed or skipped. Ordinary S11 verification is complete; next: Review of this task, followed by caller adoption into original landing owner 6ff18828. No activation or Mutation qualification is claimed.

## Identity and source

- Original Code task / landing owner: 6ff18828-907c-4430-ad54-460b4cb3a27c.
- Review subject: 8b31ec87-8e5c-471b-8f0d-0a3e39bbfbe5.
- Branch: feat/card-task-8b31ec87.
- Worktree: /work/worktrees/task-8b31ec87. Assigned desktop counterpart C:\Antiphon\worktrees\card-task-8b31ec87 was not accessed.
- Task base: 742115aa57ba612b57bcb744bb9dbe149d4d37e1. Original owner's task base: 29c241a33d114569e9c01a04c55ef2479fcccdff.
- Plan: docs/superpowers/plans/2026-10-04-card-0519-unified-outbound-recovery-plan.md, S11 and follow-up repair selections.
- Predecessor evidence: .antiphon/task-6ff18828.md.
- Actual tested source / expected SHA: 1c502c515394153646d3afb0d3ac09d584ad11bb. The final report-only commit is identified in the final handoff.
- Source commits, each pushed fast-forward: f0eee3196ee1b255d9df157401757388e3d3e46c; 1c502c515394153646d3afb0d3ac09d584ad11bb.
- ANTIPHON_TASK_TOKEN was present. GET /api/runner-defaults and GET /api/session-runners were read. No Runner or Platform pin was supplied.
- Restart: none. Caller owns eventual server/runner activation after the complete card. ChannelOutbound:UnifiedRecoveryEnabled remains false by default.

## Decisions and changes

1. CP-15 / R-2: fixture only. ChannelBridgeTests initializes its outbound FakeTimeProvider at UTC now truncated to PostgreSQL microsecond precision. Exact NextAttemptAt equality, no-early-publication and exact recovered-byte assertions remain. The previously failing missing-file recovery case now reaches and passes its recovery assertions.
2. CP-18 / R-4 and D-11: production gap. ChannelReplyPreparation rejected corrupt/unsupported optional historical manifests before reading explicit attachments. The existing DeliverableBundleService.ListAttachableFiles contract authorizes no implied files for those manifests, while explicit markers remain independent. Preparation parses once, skips unusable optional manifest metadata and forwards only usable manifests to staging. It preserves explicit PDF reads, valid manifests, legacy extension fallback, read failures and all budgets. The file store's manifest validation is unchanged. ChannelFollowUpAttachmentTests retains all exact names/order/bytes and forbidden-file checks, adds exact reply count and requires null bundle-delivered stamps for corrupt/unknown variants. All four historical variants pass. The predecessor's two ordinary failures establish that the existing tests detect the repaired defect; no deliberate mutant was run.
3. CP-21 / R-3 and R-10: stale test expectations. CARD-0584's runtime contract requires every inline batch member's marker. ChannelBatchingTests captures independently known enqueue IDs through onCreated and includes their literal markers in exact expected prompts. Body, order, boundaries and cardinality remain exact; no production correlation behavior changed.

The changed production surface required rerunning every S11 row, not only CP-15/18/21. The explicit brief and plan prohibit whole Unit and limit this slice to CP-15..22; that specific Final scope takes precedence over the appended generic profile. This is complete S11 verification, not full-card qualification. No full assembly/namespace run or manual recipient acceptance was selected. Queue/broker/recipient and process-death proofs remain S12; Windows parity remains S13.

## Runs, cost and provenance

The only unlisted build bootstrapped the missing checkpoint launcher:

```sh
pwsh -NoProfile -File scripts/build-slot.ps1 -Label c519-s11-repair-checkpoint-tool -- dotnet build tools/Antiphon.Checkpoints --property:OutputPath=bin-c519-s11-repair-tool/ --nologo
```

It passed with one existing TaskOwnerGuard.cs CS8602 warning and zero errors; slot=granted waited=0s, held 5s. Thereafter the built DLL avoided launcher rebuilds. Every checkpoint build and test driver used its host slot; no unleased driver ran.

One mistyped expected SHA was refused before any run/build. Group 1 run 20261005-162340-d91a at f0eee3196ee1b255d9df157401757388e3d3e46c was stopped during its first build after inspection found the file store also rejects an unusable forwarded manifest. Stop and the foreground run were awaited to terminal exit 6; the process inventory showed no executor or children before editing. Zero tests ran and no CHECKPOINT row was emitted. Its build recorded slot=granted waited=0s. Group 2 moved source-manifest forwarding behind successful parsing/shape validation, completing the final authorized repair round.

Final run 20261005-162445-000c used the exact committed HEAD:

```sh
dotnet tools/Antiphon.Checkpoints/bin-c519-s11-repair-tool/Antiphon.Checkpoints.dll run --plan docs/superpowers/plans/2026-10-04-card-0519-unified-outbound-recovery-plan.md --rows CP-15,CP-16,CP-17,CP-18,CP-19,CP-20,CP-21,CP-22 --expected-source-sha 1c502c515394153646d3afb0d3ac09d584ad11bb --row-timeout 15m --total-timeout 30m --serial --max-wait 50s
dotnet tools/Antiphon.Checkpoints/bin-c519-s11-repair-tool/Antiphon.Checkpoints.dll wait --run 20261005-162445-000c --max-wait 50s
```

Wait was repeated through terminal GREEN exit 0. Wall time: 26m39s, within the task's 40-minute hard budget. Plan estimate remains 53 minutes; actual predecessor and current runs each took 26m39s. Row deadlines were not widened; total timeout was reduced to fit the brief. No unchanged proof repetition, flake retry, loaded run or baseline rerun was performed. No failure is labeled inherited. Both authorized repair rounds were used.

All eight selected isolated builds passed. The footer's builds=59 counts manifest entries: eight are ok and 51 unused. Every selected build and row recorded slot=granted waited=0s. Fresh TRX definitions and results were inspected for every intended class/method and all argument rows; every expected class has its exact nonzero count. Derived inspection is ignored at .antiphon/checkpoints/20261005-162445-000c/trx-inspection.json.

| CP | Full class roster | Passed | Failed | Skipped |
|---|---|---:|---:|---:|
| CP-15 | ChannelBridgeTests | 40 | 0 | 0 |
| CP-16 | ChannelReplyDurabilityTests | 25 | 0 | 0 |
| CP-17 | ChannelPromptCorrelationTests (24), ChannelPromptCorrelationUnitTests (8), ChannelMachineTurnMatchTests (10) | 42 | 0 | 0 |
| CP-18 | ChannelMachineTurnTextTests (19), ChannelFollowUpAttachmentTests (26) | 45 | 0 | 0 |
| CP-19 | ChannelOutboundDeliveryTests | 38 | 0 | 0 |
| CP-20 | ChannelOutboundPolicyTests (29), ChannelOutboundContractTests (2) | 31 | 0 | 0 |
| CP-21 | ChannelBatchingTests | 10 | 0 | 0 |
| CP-22 | ChannelOutboundDeadlineTests | 13 | 0 | 0 |

Start/end source snapshots both show dirtyFiles=0, SHA 1c502c515394153646d3afb0d3ac09d584ad11bb, fingerprint 8b61b3370a5c40bddffdbc0fe6a0160481b6bdbd9f19163202f886404f492d4a, sourceState=clean and buildSource=verified. All eight build receipts agree. Validation of report.json against the actual tested SHA and all eight rows returned exit 0:

```text
CHECKPOINT SOURCE VALID source=1c502c515394153646d3afb0d3ac09d584ad11bb rows=8
```

Generated receipts/TRX/logs remain ignored under /work/worktrees/task-8b31ec87/.antiphon/checkpoints/. Green cleanup removed owned project outputs, wait removed the dead executor's shadow copy, and the isolated launcher output was removed. Final inventory found zero bin-c519-* directories under server/src/tests/tools. No source changed during either active run. The full task base..final HEAD evidence-policy audit is run after this report commit; its exact result and pushed SHA are in the final handoff.

## Ordinary invariant outcomes

| ID | Actual outcome in this task |
|---|---|
| V-1 | Earlier-slice schema proof; not rerun or claimed passed here. |
| V-2 | Earlier-slice capture/race proof; not rerun or claimed passed here. |
| V-3 | Earlier-slice materialization proof; not rerun or claimed passed here. |
| V-4 | Earlier-slice unified dispatcher/runtime proof; not rerun or claimed passed here. |
| V-5 | Earlier-slice discovery proof; not rerun or claimed passed here. |
| V-6 | Earlier-slice trailing ownership proof; not rerun or claimed passed here. |
| V-7 | Earlier-slice retry/fencing proof; not rerun or claimed passed here. |
| V-8 | Earlier-slice atomic failure proof; not rerun or claimed passed here. |
| V-9 | Earlier-slice metadata repair proof; not rerun or claimed passed here. |
| V-10 | Earlier-slice retention proof; not rerun or claimed passed here. |
| V-11 | Pending S12 pre-publication process-death proof. |
| V-12 | Pending S12 during/after-publication process-death proof. |
| V-13 | Pending S12 real queue/broker/recipient proof. |
| R-1 | Earlier-slice dispatcher/runtime regression; not rerun here. |
| R-2 | Passed CP-15/16: 65/65. |
| R-3 | Passed CP-17: 42/42. |
| R-4 | Passed CP-18: 45/45. |
| R-5 | Passed CP-19/20: 69/69. |
| R-6 | Passed CP-22: 13/13. |
| R-7 | Earlier-slice storage regression; not rerun here. |
| R-8 | Pending S12 recovery/broker-receipt regression. |
| R-9 | Pending S12 composed transport regression. |
| R-10 | Passed CP-21: 10/10. |
| R-11 | Earlier-slice retention regression; not rerun here. |
| R-12 | Pending S13 Windows native parity. |

No S11 ordinary ID or manual acceptance remains pending. There are no Interim deferred-to-Final IDs: this is the explicit restricted Final S11 scope. Other slices and every PC remain unqualified by this task.

## Unedited final CHECKPOINT lines

```text
CHECKPOINT CP-15 commit=1c502c515394153646d3afb0d3ac09d584ad11bb build=ok filter=/*/*/ChannelBridgeTests/* executed=40 passed=40 failed=0 skipped=0 trx=/work/worktrees/task-8b31ec87/.antiphon/checkpoints/20261005-162445-000c/rows/CP-15/run.trx slot=granted waited=0s dirty=0 source=1c502c515394153646d3afb0d3ac09d584ad11bb sourceState=clean buildSource=verified
CHECKPOINT CP-16 commit=1c502c515394153646d3afb0d3ac09d584ad11bb build=ok filter=/*/*/ChannelReplyDurabilityTests/* executed=25 passed=25 failed=0 skipped=0 trx=/work/worktrees/task-8b31ec87/.antiphon/checkpoints/20261005-162445-000c/rows/CP-16/run.trx slot=granted waited=0s dirty=0 source=1c502c515394153646d3afb0d3ac09d584ad11bb sourceState=clean buildSource=verified
CHECKPOINT CP-17 commit=1c502c515394153646d3afb0d3ac09d584ad11bb build=ok filter=/*/*/(ChannelPromptCorrelationTests*)|(ChannelPromptCorrelationUnitTests*)|(ChannelMachineTurnMatchTests*)/* executed=42 passed=42 failed=0 skipped=0 trx=/work/worktrees/task-8b31ec87/.antiphon/checkpoints/20261005-162445-000c/rows/CP-17/run.trx slot=granted waited=0s dirty=0 source=1c502c515394153646d3afb0d3ac09d584ad11bb sourceState=clean buildSource=verified
CHECKPOINT CP-18 commit=1c502c515394153646d3afb0d3ac09d584ad11bb build=ok filter=/*/*/(ChannelMachineTurnTextTests*)|(ChannelFollowUpAttachmentTests*)/* executed=45 passed=45 failed=0 skipped=0 trx=/work/worktrees/task-8b31ec87/.antiphon/checkpoints/20261005-162445-000c/rows/CP-18/run.trx slot=granted waited=0s dirty=0 source=1c502c515394153646d3afb0d3ac09d584ad11bb sourceState=clean buildSource=verified
CHECKPOINT CP-19 commit=1c502c515394153646d3afb0d3ac09d584ad11bb build=ok filter=/*/*/ChannelOutboundDeliveryTests/* executed=38 passed=38 failed=0 skipped=0 trx=/work/worktrees/task-8b31ec87/.antiphon/checkpoints/20261005-162445-000c/rows/CP-19/run.trx slot=granted waited=0s dirty=0 source=1c502c515394153646d3afb0d3ac09d584ad11bb sourceState=clean buildSource=verified
CHECKPOINT CP-20 commit=1c502c515394153646d3afb0d3ac09d584ad11bb build=ok filter=/*/*/(ChannelOutboundPolicyTests*)|(ChannelOutboundContractTests*)/* executed=31 passed=31 failed=0 skipped=0 trx=/work/worktrees/task-8b31ec87/.antiphon/checkpoints/20261005-162445-000c/rows/CP-20/run.trx slot=granted waited=0s dirty=0 source=1c502c515394153646d3afb0d3ac09d584ad11bb sourceState=clean buildSource=verified
CHECKPOINT CP-21 commit=1c502c515394153646d3afb0d3ac09d584ad11bb build=ok filter=/*/*/ChannelBatchingTests/* executed=10 passed=10 failed=0 skipped=0 trx=/work/worktrees/task-8b31ec87/.antiphon/checkpoints/20261005-162445-000c/rows/CP-21/run.trx slot=granted waited=0s dirty=0 source=1c502c515394153646d3afb0d3ac09d584ad11bb sourceState=clean buildSource=verified
CHECKPOINT CP-22 commit=1c502c515394153646d3afb0d3ac09d584ad11bb build=ok filter=/*/*/ChannelOutboundDeadlineTests/* executed=13 passed=13 failed=0 skipped=0 trx=/work/worktrees/task-8b31ec87/.antiphon/checkpoints/20261005-162445-000c/rows/CP-22/run.trx slot=granted waited=0s dirty=0 source=1c502c515394153646d3afb0d3ac09d584ad11bb sourceState=clean buildSource=verified
```

## Pending SourceLanding Mutation inventory

Every control remains pending, including all argument/path/state variants in the plan. Code has executed zero deliberate red/restore/green cycles; ordinary passes are not Mutation evidence. Mutation also owns missing-control discovery.

- PC-1 through PC-100, including their named method arguments and main/tail/machine variants. PC-20's original missing-catalog-loss variant is superseded by S4's routing amendment; the revised catalog-less controls remain pending.
- PC-1059-1 roots; PC-1059-2 traversal; PC-1059-3 file/directory/allowed-root links; PC-1059-4 pre-read budget; PC-1059-5 Linux regular-file type; PC-1059-6 Linux growing-file budget.
- PC-S4-1 default off; PC-S4-2 catalog-less main; PC-S4-3 catalog-less machine; PC-S4-4 catalog-less trailing. Also PC-19 S4 main/machine activation, PC-15 S4 staged source, PC-22 S4 silence, PC-23 S4 origin.
- PC-S5-1 event closure; PC-S5-2 transactional closure; PC-S5-3 complete machine batch; PC-S5-4 original context time.
- PC-S6-1 silent main root; PC-S6-2 each independent variant (reset root cursor, remove Take(PageSize), remove MaximumPages); PC-S6-3 machine trailing policy; PC-S6-4 trailing API withholding; PC-S6-5 terminal ordering.
- PC-S7-1 null accepted lease; PC-44/45 entry/outcome owner/version/expiry/state and null-lease variants.
- PC-S8-1 SentAt origin; PC-S8-2 recording-only preparation repair.
- PC-S9-1 independent repair cursor/budget (reset cursor and remove maximum-pages bound).
- PC-S10-1a/b/c/d/e EOF, partial line, malformed JSON, file identity, exit grace; each Claude/Codex/Grok native argument.
- PC-S10-2a/b/c terminal generation, persisted payload, original-prompt membership.
- PC-S10-3a/b/c runtime/HTTP/phone-home completeness/generation propagation.
- PC-84 Published/Suppressed root and ineligible machine-source variants.
- S11 strengthens existing PC-8/19/21..24/47/49/50/74/77/78 and PC-88 witnesses; it introduces no duplicate PC ID. Every existing variant remains pending.


