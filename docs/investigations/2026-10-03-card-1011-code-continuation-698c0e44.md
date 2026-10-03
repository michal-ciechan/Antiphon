# CARD-1011 Code continuation after the exhaustion amendment

Status at this evidence commit: **X complete, CP-2-X green; Q pending, prompt held; Final incomplete.**

Original Code/landing owner: `698c0e44-127d-4a7a-9584-7031570573e5`.
Branch: `feat/card-task-698c0e44`.
Worktree: `/work/worktrees/task-698c0e44`.
Plan: [amended sequence and checkpoints](../superpowers/plans/2026-10-03-card-1011-windows-grok-routing-plan.md), amendment source `9ed84772b188b5a09650fe51d535f1c031ef321d`.
Historical implementation/Windows qualification subject A: `749e73f1c743737d551b677bf8a6d78ff109a3a1`.
X assertion/documentation commit: `1f4fa69d15f2511b7386b59b8fa3d10881999bde`.

This supersedes the repair request in the [previous Code report](2026-10-03-card-1011-code-evidence-698c0e44.md). The caller approved preservation of existing admission behavior. No production repair is requested by this card: AgentTaskService.cs, placement, routing candidates, aliases, queue and native qualification harness are unchanged. The two formerly red Linux matrix rows now assert the exact existing 409 refusal and absence of persisted tasks/events. This is the explicit plan amendment, not a timeout increase, assertion weakening or production fix. The caller owns filing the recommended durable-Blocked follow-up.

## Admission and footprint recheck

Verified both remote refs with git ls-remote, fetched feat/card-task-418b258e in this worktree, then git merge --ff-only advanced this branch from 53581918de63b44af522ed3d1569ba887d0eb786 to amendment 9ed84772b188b5a09650fe51d535f1c031ef321d. No reset/rebase/force push.

At 2026-10-03 19:28:06 UTC, GET /api/agent-tasks/pipeline showed Code in-flight owners 818582a5, this owner and CARD-1008 continuation 52bffc69. Freshly fetched pushed footprints: CARD-0959 3ccc171bdaedffaf48838dfa744c6129d82c06ef (56 paths), CARD-1008 f47844d216354b24e56cbecd72d1f1a9c1beeac9 (160 paths). Intersection with CARD-1011 remains exactly the accepted docs/agent-kinds.md and docs/ai-agent-tui-configuration.md for CARD-0959, none for CARD-1008. The current three-file X slice and future server/Bundles/orchestrator.md do not overlap either owner. delegate.ps1 -Status confirmed both Dispatched with working sessions; the first CARD-1008 read returned 502 and the read-only retry succeeded. Saved local observations: .antiphon/c1011-resume-pipeline.json, c1011-resume-footprints.json, c1011-resume-status-818582a5.txt and c1011-resume-status-52bffc69.txt.

GET /api/runner-defaults and /api/session-runners were re-read through configured API; defaults remain revision 2, with eligible Windows/Linux inventory and a stale third runner. No default, pin or runner configuration write occurred; no fleet location was embedded in new guidance.

The already isolated provider-doc commit remains c9081e8c30996f35783787b2d58f8d177fd81cc8. Sections touched only: **5. Grok (xAI Grok Build TUI)** in agent-kinds.md and **Local Grok Build TUI profile** in ai-agent-tui-configuration.md. No new hunks were added there in this continuation.

## Amended assertions and owner prose

X was separately committed and pushed before the test run. It changes three files:

- WindowsGrokRoutingPolicyTests: Linux exhausted arguments capture sorted task/event ID sets from a fresh context, call real CreateAsync with no explicit kind/level/runner, require ConflictException at remote-exhaustion-refusal, status 409, runner_platform_unavailable and the exact message with period. Fresh-context sorted sets remain equal at remote-refusal-no-task and remote-refusal-no-event. The common pin-immutability assertions execute after both branches. Windows exhausted arguments retain every existing durable Blocked/audit/pair/platform assertion; the eight healthy/fallback arguments retain their oracles.
- InstructionBundleTests: existing qualification/order methods additionally guard exact backend restoration, independent Final/Full Review heading and fresh role/card pin/default/runner/occupancy reads. Labels backend-restored, final-full-review and fresh-state; fresh-state order is checked after canonical restart and before pin write. Existing PC-17 ordering assertion retains its earlier position. Zero new methods/results.
- orchestration-loop.md: within **Windows Review and Debug routing**, correct the two unconditional Blocked claims to local Blocked when placement can use local, otherwise pre-insert platform refusal. Frozen policy/gate clauses remain.

No deliberate mutants ran. New refusal/control reachability proof remains post-land Mutation; baseline/CP-2 historical failures remain unedited. The three additional gate assertions compile here but are executed by final CP-1 after Q/P, not by this routing-only selection.

## Importer admission

Amended source census remains CP-1 90, CP-2 44, CP-3 4, CP-4 2; full classes expand CP-5 to 20 and CP-6 to 5: **165 ordinary results**. The paid Explicit real-trust method is separate. Guard/PC inventory now **63**; all remain pending.

Explicit bootstrap exception: pwsh -NoProfile -File scripts/build-slot.ps1 -Label c1011-amended-importer -- dotnet build tools/Antiphon.Checkpoints --property:OutputPath=bin-c1011-tool/ --property:UseAppHost=false --nologo. Success, 0 errors, one existing CS8602 warning; build elapsed 3.09s, lease held 4s, slot=granted waited=0s. Then the built DLL's import --plan accepted all six amended rows. Imported .antiphon/checkpoints.yaml was inspected for literal filters and minima 90/44/4/2/20/5, including both full-class Windows filters. No additional tool build was used for import.

## Preparatory proof receipt, unedited

The amendment explicitly commissions this CP-2-only exception through run-checkpoint.ps1 rather than the final full checkpoint-tool run. Exact command used the amended filter/minimum plus -Expect for all three class identities, isolated bin-c1011-x/, committed expected SHA and .antiphon/c1011-x results root. It is X evidence, not C-final evidence. Source was frozen throughout; no second build/test ran concurrently.

```text
CHECKPOINT CP-2-X commit=1f4fa69d15f2511b7386b59b8fa3d10881999bde build=ok filter=/*/*/(WindowsGrokRoutingPolicyTests*)|(RoutingPinCandidateCreateTests*)|(TaskPlatformPlacementTests*)/* executed=44 passed=44 failed=0 skipped=0 trx=/work/worktrees/task-698c0e44/.antiphon/c1011-x/CP-2-X-20261003-193117-7434/run.trx slot=granted waited=0s dirty=0 source=1f4fa69d15f2511b7386b59b8fa3d10881999bde sourceState=clean buildSource=verified
```

Terminal driver exit 0; inspected fresh TRX TestDefinitions and every matrix argument. Class roster: WindowsGrokRoutingPolicyTests 12/12, RoutingPinCandidateCreateTests 17/17, TaskPlatformPlacementTests 15/15; total 44 passed, no failed/skipped. Both (Review, Linux) and (Debug, Linux) exhaustion arguments passed. Source envelope start/end agree on clean X SHA, dirtyFiles=0 and fingerprint; buildSource=verified. Timings: build 108.671s, startup 41.265s, tests 7.324s, teardown 1.627s, host 50.216s; lease held 160s, slot=granted waited=0s. No repeat required after green.

Raw evidence root: /work/worktrees/task-698c0e44/.antiphon/c1011-x/CP-2-X-20261003-193117-7434 (run.trx, source.json, build.log, run.log, git.txt). Caller report copy: /work/worktrees/task-698c0e44/.antiphon/task-698c0e44.md. Raw evidence is not force-added to Git.

## Remaining invariant status and sequence

| ID | Current actual outcome |
|---|---|
| V-1 | Historical A CP-1 88/90; two composed-prompt methods red while held. X gate assertions compiled, not yet executed. C CP-1/4 pending. |
| V-2 | X preparatory CP-2 44/44 green; final C CP-2 pending. |
| V-3 | Windows actual host/Ready/exact UserPrompt rows pending; caller commissions Q-native at A, then final C CP-3. |
| V-4 | Windows queue recovery/full class pending; A subset commissioning does not replace final C CP-5. |
| V-5 | Windows Grok tailer/full class pending; A subset commissioning does not replace final C CP-6. |
| R-1 | Historical existing instruction/platform/stage/cap cases green, two held prompt guards red; final C pending. |
| R-2 | X full three-class 44/44 green, including amended refusal/persistence checks; final C pending. |
| R-3 | Historical A trust adapter 4/4 green; Windows native and real fresh-trust qualification pending. |
| R-4 | A and C Windows delivery classes pending. |

The whole Unit lane already ran once uninterrupted at A: 3947 executed, 3945 passed, 2 failed, 52 skipped (33 Windows-specific and 19 missing-jq prerequisites). Those original receipts remain historical. The amendment explicitly requires affected CP-1 after the prompt fix, not a second whole Unit or a full assembly run. No full assembly run occurred or is proposed; no production impact expanded to unbounded classes.

Caller is commissioning Windows WQ-1/2/3 and restoration at A. Await clean attributable receipts and committed/reconciled Q evidence; neither queued tasks nor a successful canary assertion closes those gates. A-to-current diff contains documentation and admission tests only, with no production runtime or native harness change. Preserve source stamps: A qualification is not a checkpoint pass at X or C. If a qualification needs a harness/runtime repair, the affected row must be rerun at that repaired subject.

Then author P as a separate prompt-only exact frozen 868-to-674 replacement (LF UTF-16 final 14116, cap 14310). Record complete C and run the amended active manifest through the checkpoint tool, await terminal status and validate every TRX: CP-1/2 Any minima 90/44; CP-3..6 Windows minima 4/2/20/5. CP-5 filter /*/*/SessionQueueReceiptPlumbingTests/* and CP-6 /*/*/SessionMessageQueueGrokPtyIntegrationTests/* run complete affected classes. Final Windows Debug verification must be commissioned at full C SHA; no Windows rows are executed in this mirror.

Prompt is still untouched, LF-only length 14310. Live Debug pin is unchanged. Current restart: **none**; eventual embedded-guidance activation: **server**, with separately owned runner backend restoration. No land/deploy. Independent Final/Full Review follows complete Q/P and green C ordinary scope; original Code owner lands only after that Review. Activation retains canonical restart/version -> fresh pins/defaults/runners/occupancy -> approved Debug write/readback -> bundle stamp/idle refresh -> WQ-4 complete receipt/report/release -> acceptance -> SourceLanding Mutation.

Every **PC-1 through PC-63 and every listed variant** is pending for post-land method-scoped SourceLanding Mutation. No Code PC execution, pass or waiver. Amended estimates: final CP setup/V/R 46m, preparatory commissions 35m, PC floor 546m, live qualification/setup 55m; estimates are not measured passes. Actual runs and waits are above.
