# CARD-1206 Code report

Implemented fail-closed Grok argv validation; 59 ordinary tests passed with no
failures or skips. Null list/element rows first reproduced inherited NREs on
production identical to base eb5330d78596a8d3f313770393c8808c2e5b7d07.

Original Code/landing owner: aa9cb8eb-96eb-4e44-bfd5-116286ac8466.
Branch: feat/card-task-aa9cb8eb. Worktree: /work/worktrees/task-aa9cb8eb.
All slices pushed to the authorized ref, fast-forward only. Final published SHA
is in the completion/progress line; the final commit changes only this report.

## Change and contract

- src/Antiphon.SessionRunner/SessionRunnerRuntime.cs: seven lines reject null
  args/elements for Grok identity after size/backend validation and before rules
  scanning, registration, files or spawn. ArgumentException, parameter request,
  message: Args must not be null or contain null elements.
- tests/Antiphon.SessionRunner.Tests/GrokLaunchArgsValidationTests.cs: four
  handwritten positive-size rows through real StartAsync with rules payload;
  null list, null element, empty and normal args. Empty/normal keep the existing
  command_line_budget refusal using an insufficient request budget. Two earlier
  validation controls pin invalid dimensions and unknown backend. Require zero
  registrations, empty registry and no disk effects.
- docs/superpowers/plans/2026-10-12-card-1206-grok-launch-null-args.md: design,
  closed checkpoints and pending PCs.

Exact downstream failure: GrokRulesTransport.HasExplicitRules, called by
SessionRunnerRuntime.StartCoreAsync after UnixPtyArgvGuard.VerifyOrThrow.
Its foreach dereferences a null list; StartsWith dereferences a null element.
No positive-size Args validation message existed; use the runtime's ordinary
ArgumentException convention. Earlier errors remain exact. No timeout widened,
assertion loosened, service action or rollout. Restart: none performed;
production runner activation remains caller-owned.

## Ordinary outcomes (Final)

| ID | Actual outcome |
|---|---|
| V-1 | CP-1: null_args/null_element red, actual NullReferenceException instead of handwritten ArgumentException. Empty/normal controls green. CP-2: four rows green. |
| V-2 | Both precedence controls green in CP-1 and CP-2; regression controls, not separately claimed baseline defect proofs. |
| R-1 | Full UnixPtyArgvAdmissionTests unchanged, CP-4: 15/15 green. |
| R-2 | Full UnixPtyArgvTests unchanged, CP-3: 17/17 green. |
| R-3 | Full GrokRulesFileLaunchTests unchanged, CP-5: 21/21 green. |
| M-1 | Fresh TRX method/class rosters and counts inspected; clean receipts validated; diff check clean; fetched-master merge-tree exit 0; evidence guard passed; alternate outputs removed; remote matched HEAD. Publication checks repeated after report commit. |

Broad Unit lane explicitly excluded by this brief; not run. No full assembly or
live provider acceptance required. No ordinary IDs deferred. PC-1a (null list),
PC-1b (null element), PC-2 (precedence) all PENDING method-scoped SourceLanding
Mutation after Review and caller landing of the original owner. No deliberate
mutants or post-land Mutation run; no repeat after green.

## Source qualification and evidence

Bound owner/session recorded by the tool; task token present, never printed.
GET /api/runner-defaults and /api/session-runners read; no placement pin added.
Every driver took the slot gate. The only unlisted build was needed tool
bootstrap: scripts/build-slot.ps1 -Label c1206-checkpoint-tool-bootstrap --
dotnet build tools/Antiphon.Checkpoints --property:OutputPath=bin-c1206-tool/
--property:UseAppHost=false --nologo. Success, slot=granted waited=0s; one existing
CS8602 warning. All bin-c1206-* outputs, including bootstrap, removed.

Checkpoint invocation: dotnet run --project tools/Antiphon.Checkpoints --no-build
--property:OutputPath=bin-c1206-tool/ --property:UseAppHost=false -- run --plan
docs/superpowers/plans/2026-10-12-card-1206-grok-launch-null-args.md --rows <rows>
--expected-source-sha <committed HEAD> --max-wait 50s. Await wait --run <id>
--max-wait 50s until exit is not 75. Every owned executor awaited to final exit.

- CP-1 tested 815f8c23d661adbc7140e18f3cbf22215d3c5c02: 6 executed,
  4 passed, 2 intentional NRE failures, 0 skipped, exit 1; production unchanged.
- Initial import refused unescaped filter pipes before running. Fixed/pushed.
- Initial green attempt tested e06754f18d8c47c7ba1f63f47b74d16f3b24e1bd:
  CP-2 combined filter selected ZERO tests, invalid evidence, exit 3. CP-3
  passed 17/17. No test failure was hidden by the filter correction.
- Manifest-only d5e323df56ecacc6d228d9ee041a5b48f2adff31 splits literal class
  filters with one shared isolated runner build: CP-2/CP-4/CP-5, 42/42 green.
  Production/tests unchanged since CP-3 SHA; do not repeat its green selection.
- Receipt validate exit 0: source=e06754f18d8c47c7ba1f63f47b74d16f3b24e1bd,
  rows=CP-3; source=d5e323df56ecacc6d228d9ee041a5b48f2adff31,
  rows=CP-2,CP-4,CP-5. dirty=0, sourceState=clean, buildSource=verified.
- Build slot waits: baseline 0s; initial runner 90s/native 45s; corrected runner
  75s. Row slot/waited values preserved below. All granted, none unleased.

Ignored TRX/receipt/log roots under /work/worktrees/task-aa9cb8eb/.antiphon/checkpoints/:
20261012-230019-3620 (red), 20261012-230125-eeb4 (zero-test/native),
20261012-230707-3496 (runner green). Only this small authored Markdown is committed.

Fetched origin/master: 0973be4121b89249f092a6a5f4e9542797b86a2c.
merge-tree before report-only commit: exit 0, tree
342e196ce7bd3bb2cb784e4612615b03a7d32d0f. Tracked census 7446 before report,
7447 including it. Evidence guard on base..d5e323df56ecacc6d228d9ee041a5b48f2adff31:
5 commits, 0 violations. Full base..HEAD guard and publication checks repeated
after this report commit; final results in the completion message.

## Unedited CHECKPOINT lines

```
CHECKPOINT CP-1 commit=815f8c23d661adbc7140e18f3cbf22215d3c5c02 build=ok filter=/*/*/GrokLaunchArgsValidationTests/* executed=6 passed=4 failed=2 skipped=0 trx=/work/worktrees/task-aa9cb8eb/.antiphon/checkpoints/20261012-230019-3620/rows/CP-1/run.trx slot=granted waited=0s dirty=0 source=815f8c23d661adbc7140e18f3cbf22215d3c5c02 sourceState=clean buildSource=verified
CHECKPOINT CP-2 commit=e06754f18d8c47c7ba1f63f47b74d16f3b24e1bd build=ok filter=/*/*/(GrokLaunchArgsValidationTests)|(UnixPtyArgvAdmissionTests)|(GrokRulesFileLaunchTests)/* executed=0 passed=0 failed=0 skipped=0 trx=/work/worktrees/task-aa9cb8eb/.antiphon/checkpoints/20261012-230125-eeb4/rows/CP-2/run.trx slot=granted waited=45s dirty=0 source=e06754f18d8c47c7ba1f63f47b74d16f3b24e1bd sourceState=clean buildSource=verified
CHECKPOINT CP-3 commit=e06754f18d8c47c7ba1f63f47b74d16f3b24e1bd build=ok filter=/*/*/UnixPtyArgvTests/* executed=17 passed=17 failed=0 skipped=0 trx=/work/worktrees/task-aa9cb8eb/.antiphon/checkpoints/20261012-230125-eeb4/rows/CP-3/run.trx slot=granted waited=15s dirty=0 source=e06754f18d8c47c7ba1f63f47b74d16f3b24e1bd sourceState=clean buildSource=verified
CHECKPOINT CP-2 commit=d5e323df56ecacc6d228d9ee041a5b48f2adff31 build=ok filter=/*/*/GrokLaunchArgsValidationTests/* executed=6 passed=6 failed=0 skipped=0 trx=/work/worktrees/task-aa9cb8eb/.antiphon/checkpoints/20261012-230707-3496/rows/CP-2/run.trx slot=granted waited=30s dirty=0 source=d5e323df56ecacc6d228d9ee041a5b48f2adff31 sourceState=clean buildSource=verified
CHECKPOINT CP-4 commit=d5e323df56ecacc6d228d9ee041a5b48f2adff31 build=reused filter=/*/*/UnixPtyArgvAdmissionTests/* executed=15 passed=15 failed=0 skipped=0 trx=/work/worktrees/task-aa9cb8eb/.antiphon/checkpoints/20261012-230707-3496/rows/CP-4/run.trx slot=granted waited=15s dirty=0 source=d5e323df56ecacc6d228d9ee041a5b48f2adff31 sourceState=clean buildSource=verified
CHECKPOINT CP-5 commit=d5e323df56ecacc6d228d9ee041a5b48f2adff31 build=reused filter=/*/*/GrokRulesFileLaunchTests/* executed=21 passed=21 failed=0 skipped=0 trx=/work/worktrees/task-aa9cb8eb/.antiphon/checkpoints/20261012-230707-3496/rows/CP-5/run.trx slot=granted waited=30s dirty=0 source=d5e323df56ecacc6d228d9ee041a5b48f2adff31 sourceState=clean buildSource=verified
```
