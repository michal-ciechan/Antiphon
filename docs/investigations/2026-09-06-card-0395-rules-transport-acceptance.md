# CARD-0395 implementation and incomplete acceptance

Status: **incomplete; do not land or close CARD-0395**. The initial file transport and acknowledgement
barrier work through FakeGrok, including restored Windows delegate E2Es. The complete verification
contract has not been executed. Real-model compliance, native wire dispatch acceptance, and both
two-auto-compaction endurance arms were not attempted. No auth/quota failure is claimed, no native
compact identity or elapsed live measurement exists, and no live acceptance gate is passed.

Worktree: `C:\Antiphon\worktrees\card-task-dfc6f878`.
Branch: `feat/card-task-dfc6f878-implementation`, based on fetched `57db87f555173a13c9d1623bd28836479573e5a6`.
Initial slices: `a06242c0` typed payload/store, `402d1481` settings/effective budget,
`d6044934` durable barriers/refresh/FakeGrok E2Es, `8c0ec49b` dispatch fixtures/documentation.
No deployment, shared-stack restart, production runner calls, or shared database migration.
The migration was generated with EF CLI; isolated tests applied their normal test migrations.

## Implemented scope

- Full composed Grok text travels in optional typed launch contracts, with strict UTF-8 body
  validation, source-conflict checks, runner capability gating and a stable atomic file store.
  Shared CARD-0382 argv policy is unchanged. Actual argv budgeting includes the generated bootstrap.
- Receipt metadata flows through runner responses, PtyHost manifests and Herdr sidecars. Adoption
  verifies disk hash/count. Remote receipt validation does not stat the server filesystem.
- Session generation/hash/count/state and refresh queue identity/deadline/coverage/ack evidence
  are persisted. Ordinary flush and Now/send-now hold behind the barrier. Initial delegate briefs
  are constructed after acknowledgement; an advisory lock serializes recovery and initial enqueue.
- Matching owning UserPrompt, assistant acknowledgement and successful TurnEnd are required.
  Rules turns have explicit task settlement, deferred settlement, channel and boot-reply exclusions.
- Transactional compact trigger rows, replay deduplication, conservative one-follow-on loop limit,
  error incidents and periodic recovery are present. These tests seed normalized boundaries;
  they are not native compaction acceptance.
- Legacy resume/removal preflights retain history and require the explicit existing fresh action.
  Warm Grok pool reuse checks receipt revision and Ready state. No live migration was performed.

## Known unfinished implementation/verification work

1. T-10 metadata ordering is incomplete: the file exists before launch, but normal PtyHost manifest
   and Herdr sidecar receipt persistence still occurs after native child/pane launch. Implement and
   fault-test the required durable pre-spawn receipt ordering and torn-metadata recovery.
2. Recovery needs the complete owned-startup failure matrix. A server restart after status became
   Running but before initialization ended uses the periodic recovery path; that path does not yet
   terminalize/reap a failed owned initialization through the failed-launch owner. A bad receipt
   can abort a recovery pass and delay other sessions. Deadline start currently follows first eligible
   queue attempt, and must be pinned against the required first-ready startup eligibility.
3. Complete the actual Herdr adoption/live-target checks, v1 native resume and policy-refresh tests,
   explicit artifact expiry, complete remote-path/malformed receipt matrix, and all direct-call
   body-validation/configuration boundaries. Card resume/control preflights still use default rules
   settings at some early checks; the adapter/client/runner use injected settings.
4. Complete the channel/late-chunk and queue bypass interleavings, delivery-attempt exhaustion reason,
   interrupted ordinary delivery ordering, all crash points, startup recovery isolation, and initial
   task/claim/card ownership assertions. A full subsequent tool read is not normalized as an evidence
   object; loop handling conservatively requests one follow-on instead of claiming read coverage.
5. Implement `GrokRulesDispatchAcceptanceTests`, `GrokRulesCompactionAcceptanceTests`, genuine 1.0.13
   auto-compaction fixture/provenance and the full V/PC matrices. No plan-specified live harness was
   authored or executed in this slice. Missing coverage must not be replaced with seeded success.

## Executed test suites

Final successful class-scoped runs below have zero failures and zero skips. Counts are tests,
not a claim that the broader V/R/PC contracts are all covered. TUnit duration excludes compilation.

| Project / filter class | Tests | Duration | Local log |
|---|---:|---|---|
| SessionRunner.Tests / `GrokRules*Tests` | 54 | 1.586 s | `.antiphon/card0395-runner-rules.log` |
| Antiphon.Tests / `GrokRules*Tests` | 19 | 26.183 s | `.antiphon/card0395-server-rules-final.log` |
| Antiphon.Tests / `GrokDelegateEndToEndTests` | 4 | 117.644 s | `.antiphon/card0395-e2e-rerun.log` |
| Antiphon.Tests / `GrokDelegateDispatchTests` | 28 | 20.213 s | `.antiphon/card0395-dispatch-rerun.log` |
| Antiphon.Tests / `DelegateBundleLaunchTests` | 16 | 22.379 s | `.antiphon/card0395-bundles.log` |
| Agents.Pty.Tests / `FakeGrokContractTests` | 17 | 10.059 s | `.antiphon/card0395-fake-contract.log` |
| Antiphon.Tests / `SessionMessageQueueGrokPtyIntegrationTests` | 4 | 44.748 s | `.antiphon/card0395-queue-pty.log` |

Selected suites total: 142 passed, zero failures, zero skips. Mutation runs are reported separately.

The server built successfully (zero errors; existing AgentService nullable warning). Test builds
also emit pre-existing nullable/TUnit warnings. Earlier corrections: E2E workspace validation failed
2 restored fixtures until explicit disposable `-Dir` was supplied; six dispatch tests retained old
refusal/legacy-pool expectations and were updated to the new contracts. A compaction test initially
missed its contracts import (build failure, not a positive control). One no-build invocation ran stale
tests (1 failure, 2 skips); one malformed OR filter ran zero tests. Neither is acceptance evidence.

Rerun each filter separately, sequentially between assemblies:

```powershell
dotnet run --project tests/Antiphon.SessionRunner.Tests --property:OutputPath=bin-card0395/ -- --treenode-filter "/*/*/GrokRules*Tests/*"
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-card0395/ -- --treenode-filter "/*/*/GrokRules*Tests/*"
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-card0395/ -- --treenode-filter "/*/*/GrokDelegateEndToEndTests/*"
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-card0395/ -- --treenode-filter "/*/*/GrokDelegateDispatchTests/*"
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-card0395/ -- --treenode-filter "/*/*/DelegateBundleLaunchTests/*"
dotnet run --project tests/Antiphon.Agents.Pty.Tests --property:OutputPath=bin-card0395/ -- --treenode-filter "/*/*/FakeGrokContractTests/*"
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-card0395/ -- --treenode-filter "/*/*/SessionMessageQueueGrokPtyIntegrationTests/*"
```

## Every V gate

“Partial” means related executable checks passed, while the complete named contract is unrun.
V-5 tests require a captured native ACP fixture; the current normalized-row unit/integration checks
cannot satisfy those gates. Live gates are unrun, not skipped/passed.

| Gate | Status / available evidence |
|---|---|
| V-1a | Partial: bundle/dispatch tests and runner store/launch checks; full composition matrix remains. |
| V-1b | Partial: bundle/dispatch tests and runner store/launch checks; full composition matrix remains. |
| V-1c | Partial: bundle/dispatch tests and runner store/launch checks; full composition matrix remains. |
| V-1d | Partial: bundle/dispatch tests and runner store/launch checks; full composition matrix remains. |
| V-1e | Partial: bundle/dispatch tests and runner store/launch checks; full composition matrix remains. |
| V-1f | Partial: bundle/dispatch tests and runner store/launch checks; full composition matrix remains. |
| V-1g | Partial: bundle/dispatch tests and runner store/launch checks; full composition matrix remains. |
| V-1h | Partial: bundle/dispatch tests and runner store/launch checks; full composition matrix remains. |
| V-2a | Partial: atomic store, remote grammar and refusal checks; crash/adoption/HTTP matrix remains. |
| V-2b | Partial: atomic store, remote grammar and refusal checks; crash/adoption/HTTP matrix remains. |
| V-2c | Partial: atomic store, remote grammar and refusal checks; crash/adoption/HTTP matrix remains. |
| V-2d | Partial: atomic store, remote grammar and refusal checks; crash/adoption/HTTP matrix remains. |
| V-2e | Partial: atomic store, remote grammar and refusal checks; crash/adoption/HTTP matrix remains. |
| V-2f | Partial: atomic store, remote grammar and refusal checks; crash/adoption/HTTP matrix remains. |
| V-2g | Partial: atomic store, remote grammar and refusal checks; crash/adoption/HTTP matrix remains. |
| V-2h | Partial: atomic store, remote grammar and refusal checks; crash/adoption/HTTP matrix remains. |
| V-2i | Partial: atomic store, remote grammar and refusal checks; crash/adoption/HTTP matrix remains. |
| V-3a | Partial: raw runner refusal and named-agent raw argv checks; independent server matrix remains. |
| V-3b | Partial: raw runner refusal and named-agent raw argv checks; independent server matrix remains. |
| V-3c | Partial: raw runner refusal and named-agent raw argv checks; independent server matrix remains. |
| V-3d | Passed: all 26 unchanged GrokRulesArgvPolicyTests. |
| V-3e | Passed policy/store checks: literal values remain unchanged; no native expansion claim. |
| V-4a | Partial: acknowledgement matrix, deadline recreation, delegate E2E; full lifecycle matrix remains. |
| V-4b | Partial: acknowledgement matrix, deadline recreation, delegate E2E; full lifecycle matrix remains. |
| V-4c | Partial: acknowledgement matrix, deadline recreation, delegate E2E; full lifecycle matrix remains. |
| V-4d | Partial: acknowledgement matrix, deadline recreation, delegate E2E; full lifecycle matrix remains. |
| V-4e | Partial: acknowledgement matrix, deadline recreation, delegate E2E; full lifecycle matrix remains. |
| V-4f | Partial: acknowledgement matrix, deadline recreation, delegate E2E; full lifecycle matrix remains. |
| V-4g | Partial: acknowledgement matrix, deadline recreation, delegate E2E; full lifecycle matrix remains. |
| V-4h | Partial: acknowledgement matrix, deadline recreation, delegate E2E; full lifecycle matrix remains. |
| V-4i | Partial: acknowledgement matrix, deadline recreation, delegate E2E; full lifecycle matrix remains. |
| V-4j | Partial: acknowledgement matrix, deadline recreation, delegate E2E; full lifecycle matrix remains. |
| V-4k | Partial: acknowledgement matrix, deadline recreation, delegate E2E; full lifecycle matrix remains. |
| V-5a | Unrun native fixture case; normalized trigger/loop tests only. |
| V-5b | Unrun native fixture case; normalized trigger/loop tests only. |
| V-5c | Unrun native fixture case; normalized trigger/loop tests only. |
| V-5d | Unrun native fixture case; normalized trigger/loop tests only. |
| V-5e | Unrun native fixture case; normalized trigger/loop tests only. |
| V-5f | Unrun native fixture case; normalized trigger/loop tests only. |
| V-5g | Unrun native fixture case; normalized trigger/loop tests only. |
| V-5h | Unrun native fixture case; normalized trigger/loop tests only. |
| V-5i | Unrun native fixture case; normalized trigger/loop tests only. |
| V-5j | Unrun native fixture case; normalized trigger/loop tests only. |
| V-6a | Partial: dispatch kind/reuse and bundle regressions; full lifecycle/provider regression list remains. |
| V-6b | Partial: dispatch kind/reuse and bundle regressions; full lifecycle/provider regression list remains. |
| V-6c | Partial: dispatch kind/reuse and bundle regressions; full lifecycle/provider regression list remains. |
| V-6d | Partial: dispatch kind/reuse and bundle regressions; full lifecycle/provider regression list remains. |
| V-6e | Partial: dispatch kind/reuse and bundle regressions; full lifecycle/provider regression list remains. |
| V-7a | Partial: restored capstone passes; additional full brief sentinels/unsafe-profile E2E remain. |
| V-7b | Passed: exact named unmarked-after-nudge E2E; zero skips. |
| V-7c | Passed: exact named task-only boot-stall/retry E2E; zero skips. |
| V-8a | Unrun; acceptance harness not implemented. |
| V-8b | Unrun; acceptance harness not implemented. |
| V-8c | Unrun; acceptance harness not implemented. |
| V-8d | Unrun; acceptance harness not implemented. |
| V-9a | Unrun; zero real compactions, zero live elapsed/retention measurements. |
| V-9b | Unrun; zero real compactions, zero live elapsed/retention measurements. |

## Regression control ledger

No R group is certified complete: the full mapped V matrix has not run. The table records partial
evidence separately from outstanding native, failure-injection and live requirements.

| Control | Status |
|---|---|
| R-1 | Partial related deterministic coverage; full mapped cases unrun. |
| R-2 | Partial related deterministic coverage; full mapped cases unrun. |
| R-3 | Partial related deterministic coverage; full mapped cases unrun. |
| R-4 | Partial related deterministic coverage; full mapped cases unrun. |
| R-5 | Partial related deterministic coverage; full mapped cases unrun. |
| R-6 | Partial related deterministic coverage; full mapped cases unrun. |
| R-7 | Partial related deterministic coverage; full mapped cases unrun. |
| R-8 | Partial related deterministic coverage; full mapped cases unrun. |
| R-9 | Partial related deterministic coverage; full mapped cases unrun. |
| R-10 | Partial related deterministic coverage; full mapped cases unrun. |
| R-11 | Partial related deterministic coverage; full mapped cases unrun. |
| R-12 | Partial related deterministic coverage; full mapped cases unrun. |
| R-13 | Unrun live evidence requirements. |
| R-14 | Partial related deterministic coverage; full mapped cases unrun. |

## Positive controls

Full assertion output (including the large exact expected/actual byte arrays), green output and
machine-readable per-mutation elapsed measurements are retained in the adjacent evidence ZIP.
No intentionally broken source was committed. Each accepted mutation below ran one test red,
restored original source bytes, then ran the same test green (one pass, zero skips).

Archive SHA-256: `9d2c984aba687a5806da037f5db3ad8b470c480e33a72234b0806c0112ba9410`.

| Mutation | Red / revert / green | Assertion excerpt | Build+test elapsed red / green |
|---|---|---|---|
| PC-1-truncation | 1 failed / source restored / 1 passed | `ShouldAssertException: await File.ReadAllBytesAsync(receipt.Path)`; `should be` [exact expected bytes], `but was` [corrupted bytes]. Full original arrays in archive. | 19.922 s / 14.984 s |
| PC-1-normalization | 1 failed / source restored / 1 passed | `ShouldAssertException: await File.ReadAllBytesAsync(receipt.Path)`; `should be` [exact expected bytes], `but was` [corrupted bytes]. Full original arrays in archive. | 15.859 s / 14.328 s |
| PC-8-bootstrap-budget | 1 failed / source restored / 1 passed | `ShouldAssertException: Directory.Exists(root)` / `should be False` / `but was True`. | 17.750 s / 14.969 s |
| PC-9-source-conflict | 1 failed / source restored / 1 passed | `ShouldAssertException: Directory.Exists(root)` / `should be False` / `but was True`. | 14.844 s / 15.281 s |

PC-8 and PC-9 initially failed on a downstream exception-type assertion. Their tests were strengthened
to assert zero disk effects first and rerun; the archived final evidence is the direct disk-effects
assertion, with a nonexistent disposable host source preventing actual child creation.

| Group | Status |
|---|---|
| PC-1 | Executed: separate truncation and newline-normalization mutations. |
| PC-2 | Unrun; no failing assertion or green rerun claimed. |
| PC-3 | Unrun; no failing assertion or green rerun claimed. |
| PC-4 | Unrun; no failing assertion or green rerun claimed. |
| PC-5 | Unrun; no failing assertion or green rerun claimed. |
| PC-6 | Unrun; no failing assertion or green rerun claimed. |
| PC-7 | Unrun; no failing assertion or green rerun claimed. |
| PC-8 | Executed runner-boundary mutation; see evidence above. |
| PC-9 | Executed runner-boundary mutation; see evidence above. |
| PC-10 | Unrun; no failing assertion or green rerun claimed. |
| PC-11 | Unrun; no failing assertion or green rerun claimed. |
| PC-12 | Unrun; no failing assertion or green rerun claimed. |
| PC-13 | Unrun; no failing assertion or green rerun claimed. |
| PC-14 | Unrun; no failing assertion or green rerun claimed. |
| PC-15 | Unrun; no failing assertion or green rerun claimed. |
| PC-16 | Unrun; no failing assertion or green rerun claimed. |
| PC-17 | Unrun; no failing assertion or green rerun claimed. |
| PC-18 | Unrun; no failing assertion or green rerun claimed. |
| PC-19 | Unrun; no failing assertion or green rerun claimed. |
| PC-20 | Unrun; no failing assertion or green rerun claimed. |
| PC-21 | Unrun; no failing assertion or green rerun claimed. |
| PC-22 | Unrun; no failing assertion or green rerun claimed. |
| PC-23 | Unrun; no failing assertion or green rerun claimed. |
| PC-24 | Unrun; no failing assertion or green rerun claimed. |
| PC-25 | Unrun; no failing assertion or green rerun claimed. |
| PC-26 | Unrun; no failing assertion or green rerun claimed. |
| PC-27 | Unrun; no failing assertion or green rerun claimed. |
| PC-28 | Unrun; no failing assertion or green rerun claimed. |
| PC-29 | Unrun; no failing assertion or green rerun claimed. |
| PC-30 | Unrun; no failing assertion or green rerun claimed. |
| PC-31 | Unrun; no failing assertion or green rerun claimed. |

## Handoff

Continue Code on this branch, starting with the listed T-6/T-10 lifecycle gaps and missing named
tests. Complete every unrun V/R/PC case, then the mandatory isolated real-wire and real-model gates.
Keep the card open and do not hand this implementation to Land as a completed fix.
