# CARD-0415 identity slice: implementation and verification

CARD-0415 is incomplete. This branch implements S1's specialist execution snapshot
and actionable failure handling; S2-S6 and their acceptance remain ahead. No
fallback chain has been activated and no provider has been qualified.

## Implemented

- Both specialist runner APIs stamp the selected kind and tier, exact ModelId,
  canonical availability alias, live effective model, session Id/StartedAt and
  profile revision. Created events carry the identity metadata.
- Queued availability reads and dispatch use the captured alias. Standing dispatch
  preserves its public requested-kind/live-kind guard and refuses specialist
  model, tier, generation or profile drift before enqueue, with the appended
  `SpecialistIdentityMismatch` task failure code and runner outcome.
- Check digests and unavailable incidents retain the bounded original failure
  reason (800 UTF-16 characters, line endings flattened and task markers scrubbed).
- The CLI-generated `20260908225314_AddSpecialistExecutionIdentity` migration adds
  nullable task metadata. Existing task/pin behavior remains on the legacy path.
- The plan now records DF-1's corrected p95 78.972031s (rank 71), 17 durations
  exceeding 30s and five reaching/exceeding 60s; DF-2's actual bypass source at
  server/appsettings.json:81; and DF-3's bound on AgentControlService changes.

The identity tests use a cloned database, actual specialist task creation and the
real standing dispatcher with a fake runner. Their positive evidence is task
placement/queue insertion without a new session. They do not establish native
UserPrompt delivery, no-tools execution or model qualification.

## Dependency and integration base

Base: `d9d1f90e`. CARD-0412 is implemented on this base; the current recovery service
was last changed by `e138c41b`. Its `GrantReadyAsync` remains the sole granter;
`RedeemAsync` consumes matching grants and `CountedTaskLockKey` is
`antiphon.capacity.counted-tasks`. S1 adds no admission transactions, granter,
provider clock or recovery action. S5b must bind to the landed implementation.

CARD-0412's Attention addition landed first at `35b90042`:
`CapacityRecoveryExhausted = 32`. This slice adds no Attention kind. S5a must append
its kind and preserve that ordinal, with combined server/client assertions.

CARD-0167 remains absent on this branch: the existing
`CodexRealCliStubProxyCanaryTests.B_agent_path_deferred_until_CARD_0167` still
unconditionally throws SkipTestException. It was inspected, not run or counted as
passing. Codex production launch/tool-policy, transport and real-model arms remain
PendingDependency. No CLI flags, tool policies, transport ceilings or AgentControl
launch injection were changed. Claude capability work and other independent
slices also remain unimplemented here.

## Evidence custody

Worktree: `C:\Antiphon\worktrees\card-task-1f1c67b6`.
Raw logs, fresh TRXs, mutation diffs and the scoped control runner are retained at
`C:\Antiphon\worktrees\card-task-1f1c67b6\.antiphon\acceptance\card-0415`.
They are ignored test artifacts, not deployment qualification certificates.

The historical bug reproduced before the producer fix: one executed assertion
failed with a ClaudeCode Check task pinned to the synthetic live Codex session.
The unchanged test then passed. The later 10-case identity matrix also passed.
A matrix build failed on a tuple expression unsupported by expression trees; that
test compilation error was fixed and is not counted as red-control evidence.

PC-1 was repeated against the final snapshot implementation (1 red / 1 green).
PC-2 tier and exact-alias mutations each produced 1 red / 1 green. The generation
and public live-kind mutations were independent edits batched in one run with
separate data rows (2 red / 2 green, one per guard). A first tier mutation altered
both task and event assignments and is excluded; the retained accepted run changed
only the task assignment. Added PC-60 discards the original failure reason and
produced 2 red / 2 green on the named failure-detail assertions. Every accepted
red is an assertion failure in the intended method; no build failure, skip or
zero-test result counts. Source restoration used fresh writes to avoid stale DLLs.

Final unmutated regression results and the per-item ledger follow below.

## Final unmutated run

Commit under test: `753af1c0` (runtime/test source at `2b9abf16`).
`c415-s1-regressions.trx`: **90 executed, 90 passed, 0 failed/error/aborted/skipped**.
The fresh TRX contains all eight intended classes:

| Class | Passed |
|---|---:|
| SpecialistExecutionIdentityTests | 15 |
| AgentTaskCheckInterpreterTests | 39 |
| SpecialistTaskRunnerDeadlineTests | 7 |
| AgentTaskStandingAgentDispatchTests | 10 |
| PinnedCodexProfileDispatchLaunchTests | 1 |
| PinnedAgentKindTests | 4 |
| SpecialistRoleContractTests | 2 |
| CheckInterpreterProvisionerTests | 12 |

No client, real-CLI, authenticated-model, lower-level Pty or full-assembly run was
performed. There were no unexpected runtime failures in the retained green runs.
The earlier matrix compilation error and excluded two-assignment mutation remain
in the raw logs for audit. No production service was restarted or deployed.

Rerun each listed class sequentially, replacing `<Class>` with its table entry:

```powershell
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-c415/ -- --treenode-filter "/*/*/<Class>/*" --report-trx --report-trx-filename <unique-name>.trx
```

The final combined run used the documented parenthesized named-class filter;
actual TRX definitions, result names and counters were inspected. Source mutations
are all restored; the dispatcher's original UTF-8 BOM was restored as well.

## Positive-control receipts

Each TRX name below resolves under the absolute evidence directory above. The
corresponding `.log` and `.diff` files are retained there. Mutation methods are in
`SpecialistExecutionIdentityTests`; tests were unchanged during each cycle.

| Control | Final production boundary | Method | Red / restored green | TRX names |
|---|---|---|---|---|
| PC-1 | SpecialistTaskRunner.cs:325, kind assignment | Card0415_V01_legacy_Codex_producer_reaches_the_live_standing_session | 1 failed / 1 passed | c415-pc01-final-red.trx; c415-pc01-final-green.trx |
| PC-2 tier | SpecialistTaskRunner.cs:326 | Card0415_V01_non_low_exact_model | 1 failed / 1 passed | c415-pc02-tier-single-red.trx; c415-pc02-tier-green.trx |
| PC-2 alias | SpecialistTaskRunner.cs:327 | Card0415_V01_non_low_exact_model | 1 failed / 1 passed | c415-pc02-alias-red.trx; c415-pc02-alias-green.trx |
| PC-2 generation | AgentTaskDispatcher.cs:4296 | Card0415_V01_generation_and_public_pin_guards(started) | 1 failed / 1 passed | c415-pc02-generation-pc03-kind-red.trx; c415-pc02-generation-pc03-kind-green.trx |
| PC-3 | AgentTaskDispatcher.cs:4270 | Card0415_V01_generation_and_public_pin_guards(public-kind) | 1 failed / 1 passed | same combined TRXs, independent data row |
| PC-60 (added) | SpecialistTaskRunner.cs:293, discard original failure detail | Card0415_V01_generation_and_public_pin_guards, both rows | 2 failed / 2 passed | c415-pc60-reason-red.trx; c415-pc60-reason-green.trx |

The original PC-1 pre-fix baseline and first restored green are also retained as
`c415-pc01-baseline-red.trx` and `c415-pc01-green.trx` (1 executed each).
Assertions were, respectively: Dispatched versus Failed with the historical
kind mismatch; High versus Low; Terra versus Luna; Failed versus Dispatched for
the two removed guards; and original seat-specific reason versus generic text.

| Accepted mutation diff | SHA-256 |
|---|---|
| pc01-final.diff | CCF98509FE245C2C6C6FCBDA720D78808CCC32A73A74AED90AB0CE2CF161B694 |
| pc02-tier-single.diff | E66581AD85FC2EB103F40CF2218A027D637F9A63FBC1FA654287072DF9E26BA5 |
| pc02-alias.diff | 600402BCBEFD9F1BEA8F0ACFD3488C7747B5847378127349BE59CA495E604A59 |
| pc02-generation-pc03-kind.diff | F9CADC80067D56C1CE81AC47F6A1E31F9895650B53724F8EE45F0C36BD7E93A8 |
| pc60-reason.diff | D0DBB6476D605FA1C6C5E503A365F776F9F36D36CDF39EEB1214CED6EE3CE4D5 |

The batched dispatcher diff also records the temporary BOM normalization caused
by the restoration helper; it changes no guard behavior and is absent from the
final source. Each of its two guard removals has its own assertion failure.

## Full-plan ledger

“Not run” is not a passing or skipped-green verdict. S2-S6 have not been
implemented in this slice. The Codex dependency does not prevent the remaining
independent implementation; it specifically blocks its production launch and
acceptance arms. CARD-0412 is available, but S5b integration has not been written.

| Item | Verdict / remaining work |
|---|---|
| V-1 | Partial: the 15 application identity cases and affected regressions pass; the full plan's effective-profile/correlation matrix is not exhaustively certified. |
| V-2 | Not run; independent arms unimplemented and Codex production arms PendingDependency CARD-0167. |
| V-3 | Not run; independent arms unimplemented and Codex production arms PendingDependency CARD-0167. |
| V-4 | Not run; owning S2-S6 slice unimplemented. |
| V-5 | Not run; independent arms unimplemented and Codex production arms PendingDependency CARD-0167. |
| V-6 | Not run; owning S2-S6 slice unimplemented. |
| V-7 | Not run; owning S2-S6 slice unimplemented. |
| V-8 | Not run; owning S2-S6 slice unimplemented. |
| V-9 | Not run; owning S2-S6 slice unimplemented. |
| V-10 | Not run; owning S2-S6 slice unimplemented. |
| V-11 | Not run; owning S2-S6 slice unimplemented. |
| V-12 | Not run; owning S2-S6 slice unimplemented. |
| V-13 | Not run; owning S2-S6 slice unimplemented. |
| V-14 | Not run; owning S2-S6 slice unimplemented. |
| V-15 | Not run; owning S2-S6 slice unimplemented. |
| V-16 | Not run; owning S2-S6 slice unimplemented. |
| V-17 | Not run; owning S2-S6 slice unimplemented. |
| V-18 | Not run; owning S2-S6 slice unimplemented. |
| V-19 | Not run; CARD-0412 is landed, specialist integration unimplemented. |
| V-20 | Not run; CARD-0412 is landed, specialist integration unimplemented. |
| V-21 | Not run; CARD-0412 is landed, specialist integration unimplemented. |
| V-22 | Not run; CARD-0412 is landed, specialist integration unimplemented. |
| V-23 | Not run; owning S2-S6 slice unimplemented. |
| V-24 | Not run; owning S2-S6 slice unimplemented. |
| V-25 | Not run; owning S2-S6 slice unimplemented. |
| V-26 | Not run; independent arms unimplemented and Codex production arms PendingDependency CARD-0167. |
| R-1 | Partial coverage: PC-1/PC-2/PC-3 pass their named red/green controls; full V-1 remains partial. |
| R-2 | Not run; corresponding full-plan verification remains open. |
| R-3 | Not run; corresponding full-plan verification remains open. |
| R-4 | Not run; corresponding full-plan verification remains open. |
| R-5 | Not run; corresponding full-plan verification remains open. |
| R-6 | Not run; corresponding full-plan verification remains open. |
| R-7 | Not run; corresponding full-plan verification remains open. |
| R-8 | Not run; corresponding full-plan verification remains open. |
| R-9 | Not run; corresponding full-plan verification remains open. |
| R-10 | Not run; corresponding full-plan verification remains open. |
| R-11 | Not run; corresponding full-plan verification remains open. |
| R-12 | Not run; corresponding full-plan verification remains open. |
| R-13 | Not run; corresponding full-plan verification remains open. |
| R-14 | Not run; corresponding full-plan verification remains open. |
| R-15 | Not run; corresponding full-plan verification remains open. |
| PC-1 | Pass: historical baseline and final-source red/green; receipts above. |
| PC-2 | Pass for named tier, exact-alias and generation subarms; receipts above. |
| PC-3 | Pass: public live-kind guard red/green; receipts above. |
| PC-4 | Not run; corresponding S2-S6 guard/control unimplemented. |
| PC-5 | Not run; corresponding S2-S6 guard/control unimplemented. |
| PC-6 | Not run; corresponding S2-S6 guard/control unimplemented. |
| PC-7 | Not run; corresponding S2-S6 guard/control unimplemented. |
| PC-8 | Not run; corresponding S2-S6 guard/control unimplemented. |
| PC-9 | Not run; corresponding S2-S6 guard/control unimplemented. |
| PC-10 | Not run; corresponding S2-S6 guard/control unimplemented. |
| PC-11 | Not run; corresponding S2-S6 guard/control unimplemented. |
| PC-12 | Not run; corresponding S2-S6 guard/control unimplemented. |
| PC-13 | Not run; corresponding S2-S6 guard/control unimplemented. |
| PC-14 | Not run; corresponding S2-S6 guard/control unimplemented. |
| PC-15 | Not run; corresponding S2-S6 guard/control unimplemented. |
| PC-16 | Not run; corresponding S2-S6 guard/control unimplemented. |
| PC-17 | Not run; corresponding S2-S6 guard/control unimplemented. |
| PC-18 | Not run; corresponding S2-S6 guard/control unimplemented. |
| PC-19 | Not run; corresponding S2-S6 guard/control unimplemented. |
| PC-20 | Not run; corresponding S2-S6 guard/control unimplemented. |
| PC-21 | Not run; corresponding S2-S6 guard/control unimplemented. |
| PC-22 | Not run; corresponding S2-S6 guard/control unimplemented. |
| PC-23 | Not run; corresponding S2-S6 guard/control unimplemented. |
| PC-24 | Not run; corresponding S2-S6 guard/control unimplemented. |
| PC-25 | Not run; corresponding S2-S6 guard/control unimplemented. |
| PC-26 | Not run; corresponding S2-S6 guard/control unimplemented. |
| PC-27 | Not run; corresponding S2-S6 guard/control unimplemented. |
| PC-28 | Not run; corresponding S2-S6 guard/control unimplemented. |
| PC-29 | Not run; corresponding S2-S6 guard/control unimplemented. |
| PC-30 | Not run; corresponding S2-S6 guard/control unimplemented. |
| PC-31 | Not run; corresponding S2-S6 guard/control unimplemented. |
| PC-32 | Not run; corresponding S2-S6 guard/control unimplemented. |
| PC-33 | Not run; corresponding S2-S6 guard/control unimplemented. |
| PC-34 | Not run; corresponding S2-S6 guard/control unimplemented. |
| PC-35 | Not run; corresponding S2-S6 guard/control unimplemented. |
| PC-36 | Not run; corresponding S2-S6 guard/control unimplemented. |
| PC-37 | Not run; corresponding S2-S6 guard/control unimplemented. |
| PC-38 | Not run; corresponding S2-S6 guard/control unimplemented. |
| PC-39 | Not run; corresponding S2-S6 guard/control unimplemented. |
| PC-40 | Not run; corresponding S2-S6 guard/control unimplemented. |
| PC-41 | Not run; corresponding S2-S6 guard/control unimplemented. |
| PC-42 | Not run; corresponding S2-S6 guard/control unimplemented. |
| PC-43 | Not run; corresponding S2-S6 guard/control unimplemented. |
| PC-44 | Not run; corresponding S2-S6 guard/control unimplemented. |
| PC-45 | Not run; corresponding S2-S6 guard/control unimplemented. |
| PC-46 | Not run; corresponding S2-S6 guard/control unimplemented. |
| PC-47 | Not run; corresponding S2-S6 guard/control unimplemented. |
| PC-48 | Not run; corresponding S2-S6 guard/control unimplemented. |
| PC-49 | Not run; corresponding S2-S6 guard/control unimplemented. |
| PC-50 | Not run; corresponding S2-S6 guard/control unimplemented. |
| PC-51 | Not run; corresponding S2-S6 guard/control unimplemented. |
| PC-52 | Not run; corresponding S2-S6 guard/control unimplemented. |
| PC-53 | Not run; corresponding S2-S6 guard/control unimplemented. |
| PC-54 | Not run; corresponding S2-S6 guard/control unimplemented. |
| PC-55 | Not run; corresponding S2-S6 guard/control unimplemented. |
| PC-56 | Not run; corresponding S2-S6 guard/control unimplemented. |
| PC-57 | Not run; corresponding S2-S6 guard/control unimplemented. |
| PC-58 | Not run; corresponding S2-S6 guard/control unimplemented. |
| PC-59 | Not run; corresponding S2-S6 guard/control unimplemented. |
| PC-60 | Pass: added original-failure-detail control, 2 red / 2 green. |

## Continuation

Continue Code with S2-S6 and complete V-1's remaining profile/correlation detail.
Keep CARD-0167's Codex acceptance prerequisite explicit, reuse CARD-0412's actual
sole-granter implementation, retain the default full 60-second request budget
until calibrated, and implement the N=3 transient policy and durable Attention.
No operator model choice or provider substitution is requested by this slice.
Do not land this as a completed CARD-0415 or activate a chain from these receipts.
Landing and deployment remain the caller's operation. This migration/runtime
slice requires a server restart after authorized landing; no runner restart is
needed for this slice alone.

## Cleanup residue

Automatic approval review rejected removal of the generated build outputs with
the reason `blocked by policy`. Both the checked, worktree-bounded batch and a
retry naming the exact `tests/Antiphon.Tests/bin-c415` directory were refused.
No deletion occurred. Nineteen generated `bin-c415` / `bin/c415` directories
remain under this worktree. All verification processes have finished; the TRX
receipts were copied to the separate evidence directory before cleanup was
attempted. Source is restored and committed. No broader deletion or alternate
deletion mechanism was attempted after the literal-path refusal.
