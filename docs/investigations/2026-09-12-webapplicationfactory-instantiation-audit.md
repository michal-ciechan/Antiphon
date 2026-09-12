# WebApplicationFactory instantiation audit (Antiphon.Tests)

**Date**: 2026-09-12 · **Author**: Investigate task 7ed382dc (fresh operator line of inquiry, no
card yet) · **Status**: investigation complete — no card recommended (see §5).

## 0. Question

Operator has prior experience with a specific perf trap: many separate `WebApplicationFactory`
boots across a suite when a handful of shared instances (one per distinct service-registration
combination) plus a cheap per-test reset would do. Does Antiphon.Tests have this problem?

## 1. Every place a factory is created

`Grep` for `WebApplicationFactory` across `tests/` and for factory subclass definitions
(`: AntiphonWebAppFactory`, `[ClassDataSource<...WebAppFactory>]`) in `tests/Antiphon.Tests`.

Base class: `tests/Antiphon.Tests/TestHelpers/AntiphonWebAppFactory.cs:32` —
`AntiphonWebAppFactory : WebApplicationFactory<ServerProgram>`.

Subclasses (13, all `: AntiphonWebAppFactory` unless noted):

| Subclass | Defined at | Consumer class(es) | `Shared` mode |
|---|---|---|---|
| `AntiphonWebAppFactory` (base, used directly) | — | ~20 classes (`BoardCardOrderApiTests`, `AttentionServiceTests`, `CardCommentApiTests`, `HealthEndpointTests`, `MutationPipelineTests`, `AgentModelLevelBindTests`, `HangfireStartupSafetyTests`, `DiagnosisEndpointTests`, `DistillationEndpointTests`, `FileSystemBrowseApiTests`, `PolicyRefreshEndpointTests`, `ProductionRunnerIsolationTests`, `ResponseCompressionIntegrationTests`, `ScheduleEndpointsTests`, `StageOutcomeFindingEndpointTests`, `CardAliasApiTests`, `CardThreadEndpointTests`, `CardReorderApiTests`, `CardReorderIntegrationTests`, `CardIdentifierResolutionTests`, `BoardCardOrderIntegrationTests`, `BoardProjectArchiveApiTests`, `ChannelPreamblePresetEndpointTests`, `CardCorrectionApiTests`, `DiagnosticsBundleEndpointTests`, `AgentTaskPipelineStatusTests`, `AgentTaskOverdueDeadlineTests`) | `PerTestSession` (all of them) |
| `LandContractWebAppFactory` | `TestHelpers/LandContractWebAppFactory.cs:11` | `AgentTaskLandContractEndpointTests` | `PerClass` (1 consumer) |
| `CardFileSyncEndpointWebAppFactory` | `Application/CardFileSyncEndpointTests.cs:155` | `CardFileSyncEndpointTests`, `CardFilePolicyApiTests`, `CardPrivateNotesApiTests` | `PerClass` — **3 consumers, no override differences** |
| `DisabledCardFileSyncEndpointWebAppFactory` | `Application/CardFileSyncEndpointTests.cs:169` | `CardFileSyncEndpointTests` (one nested test class) | `PerClass` (1 consumer, genuinely different config: sync disabled) |
| `AuditArchiveWebAppFactory` | `Application/AuditArchiveEndpointTests.cs:109` | `AuditArchiveEndpointTests` | `PerClass` (1 consumer) |
| `CardTrackerPushWebAppFactory` | `Application/CardTrackerPushApiTests.cs:229` | `CardTrackerPushApiTests` | `PerClass` (1 consumer) |
| `ComplexityChainApiWebAppFactory` | `Application/ComplexityChainHttpTests.cs:142` | `ComplexityChainHttpTests`, `ComplexityChainRoleHttpTests` | `PerClass` — **2 consumers, no override differences** |
| `ModelAvailabilityApiWebAppFactory` | `Application/ModelAvailabilityHttpTests.cs:138` | `ModelAvailabilityHttpTests` | `PerClass` (1 consumer) |
| `StandingRecoveryWebAppFactory` | `Application/StandingSessionRecoveryHttpTests.cs:107` | `StandingSessionRecoveryHttpTests` | `PerClass` (1 consumer) |
| `SubscriptionUsageApiWebAppFactory` | `Application/SubscriptionUsageHttpTests.cs:303` | `SubscriptionUsageHttpTests` | `PerClass` (1 consumer) |
| `TrackerSyncEndpointWebAppFactory` | `Application/TrackerSyncEndpointTests.cs:370` | `TrackerSyncEndpointTests` | `PerClass` (1 consumer) |
| `ApiKeyApiWebAppFactory` | `ApiKeys/ApiKeyApiTests.cs:176` | `ApiKeyApiTests` | `PerTestSession` (1 consumer) |
| `AgentTuiApiWebAppFactory` | `AgentTui/AgentTuiApiTests.cs:733` | `AgentTuiApiTests` (own schema, `ImportProfilesOnStartup=true` — genuinely different) | `PerTestSession` (1 consumer) |
| `MockedFileSystemWebAppFactory` | `TestHelpers/MockedFileSystemWebAppFactory.cs:16` | `FileSystemBrowseMockedTests` | `PerTestSession` (1 consumer) |
| `ChannelConsumerIdentityWebAppFactory` | `Application/ChannelConsumerIdentityEndpointTests.cs:52` | one `[Test]` method, ad hoc `new` (not `ClassDataSource`) | n/a — single use |
| `LiveFactory` (local, `GrokRulesLiveMappedDispatchTests.cs:148`) | inline | that file's tests | ad hoc `new` per its own harness — genuinely unique wiring (`DirectSessionRunnerClient`) |

Ad hoc `new` sites outside `ClassDataSource` (checked each individually):
- `SmokeTests.cs:53` — `new WebApplicationFactory<ServerProgram>()` in `[Before(Class)]`, own
  dedicated `PostgreSqlContainer` (not the shared assembly template) — deliberately tests a
  from-scratch boot against a fresh, non-templated DB; class-scoped, not per-test.
- `ProgramStartupConcurrencyTests.cs:35-36` — two `AntiphonWebAppFactory` instances created
  **in the same test**, deliberately, to assert concurrent-startup behaviour — not overcreation.
- `HomeTaskServiceIntegrationTests.cs:289` — one `[Test]` does `new AntiphonWebAppFactory()`
  instead of taking the already-session-shared instance via `ClassDataSource`. One avoidable extra
  boot; every other test in that file uses the lighter `World.CreateAsync()` helper instead.
- `ChannelConsumerIdentityEndpointTests.cs:20` — one `[Test]` does `new
  ChannelConsumerIdentityWebAppFactory()` ad hoc; only one test in the file needs it, so this is a
  single boot, not a pattern.

E2E project (`tests/Antiphon.E2E`) has its own `KestrelWebApplicationFactory` /
`AntiphonAppFixture` — a real-Kestrel, real-port fixture for full E2E, a different tier with
different cost characteristics; out of scope for this Unit-lane question and not investigated
further here.

## 2. Registration combinations vs instances actually booted

Genuinely distinct service-registration combinations: **~14** (the base config plus each
subclass's real `ApplyTestOverrides`/`ConfigureWebHost` delta — disabled sync, mocked filesystem,
own-schema AgentTui import, refusing/live session-runner wiring, etc.).

Instances actually created at run time, given the `Shared` attributes found: **~17-18**, because
two subclasses are reused by multiple consumer classes under `SharedType.PerClass` instead of
`SharedType.PerTestSession`:

- `CardFileSyncEndpointWebAppFactory`: 3 consumer classes × `PerClass` = **3 boots** for one
  registration combination (should be 1).
- `ComplexityChainApiWebAppFactory`: 2 consumer classes × `PerClass` = **2 boots** for one
  registration combination (should be 1).
- Plus the one ad hoc `new AntiphonWebAppFactory()` in `HomeTaskServiceIntegrationTests` that
  duplicates a combination already shared session-wide.

That is **4 avoidable extra boots** total, all fixable by changing `Shared = SharedType.PerClass`
to `SharedType.PerTestSession` on the two factory subclasses' attributes (5 attribute sites: 3 for
`CardFileSyncEndpointWebAppFactory`, 2 for `ComplexityChainApiWebAppFactory`) and by having
`HomeTaskServiceIntegrationTests`'s one test take `AntiphonWebAppFactory` via `ClassDataSource`
like its siblings.

Everything else already matches the efficient pattern the operator described: one factory type per
distinct registration combination, shared for the whole test session
(`SharedType.PerTestSession`), reused across every consuming test class, with per-test isolation
via `AntiphonWebAppFactory.ResetAsync()` (clears every `IResettableCache`, called from
`[Before(Test)]`) instead of a fresh host boot. The base class's own doc comment
(`AntiphonWebAppFactory.cs:20-25`) states this design explicitly and names the reason: "booting a
factory per test is expensive."

## 3. Cost of one boot, and how many boots happen suite-wide

`AntiphonWebAppFactory.cs:46-49` documents the current cost model directly: CARD-0110 S2 made the
per-instance database setup a **migrate-once template clone** (~100-300 ms) instead of a fresh
55-migration replay; `Program.cs`'s own `MigrateAsync` on top of a cloned schema is then just a
version check.

Independent confirmation from `docs/superpowers/plans/2026-09-03-card-0110-remeasure-and-fast-lane.md`
(§2, measured 2026-09-03 on this worktree, post-S2 and post-CARD-0238):

| Class (WAF/isolated-schema driven) | Plan 2026-08-21 (pre-S2) | Today (post-S2) | Verdict |
|---|---|---|---|
| GitDiffSpikeTests | 124 s | ~5 s | collapsed ~25x |
| GitServiceTests | 116 s | ~2 s | collapsed |
| WorktreeManagerGitIntegrationTests | 111 s | ~15 s | collapsed |
| AttentionServiceTests | 86 s / 30 tests | ~21 s | collapsed |
| AgentTaskDeliveryWatchdogTests | max 12.2 s/test | ~24 s total | collapsed |

Same document, §4: the suite's actual current bottleneck is the **1-wide `ProcessSpawnLimit`
lane** (real process/ConPTY spawns), not factory boots —
`SessionMessageQueuePtyIntegrationTests` alone costs 3:52 wall, `RunnerProcessProbeTests` 2:17 —
dwarfing anything a WAF boot costs post-S2. The full suite is ~25.5 minutes (3,893 tests,
2026-09-03 measurement); at ~17-18 factory boots × ~100-300 ms each, total WAF-boot cost across the
whole run is on the order of **2-5 seconds**, i.e. well under 1% of wall time.

## 4. What "already efficient" looks like here (no code to write, per instructions)

The dominant, already-implemented pattern for the ~20 classes on the plain `AntiphonWebAppFactory`
combination:

```
[ClassDataSource<AntiphonWebAppFactory>(Shared = SharedType.PerTestSession)]
public class SomeEndpointTests(AntiphonWebAppFactory factory)
{
    [Before(Test)] public Task ResetAsync() => factory.ResetAsync();
    ...
}
```

One host boots once for the whole test session; every test resets `IResettableCache` instances
before running. This is exactly the "few factories, shared, cheap reset" shape the operator
described, and it is already the majority pattern.

The two identified gaps would be closed the same way — no new mechanism needed, just changing the
`Shared` value: `CardFileSyncEndpointWebAppFactory` and `ComplexityChainApiWebAppFactory` would
each move from `SharedType.PerClass` (repeated per consumer class) to `SharedType.PerTestSession`
(shared across their now-multiple consumer classes), since neither has any override that differs
between its consumer classes.

## 5. Verdict — no card recommended

The suspected trap (many boots where a few shared-per-combination instances would do) is **not**
present at meaningful scale. CARD-0110 S2 already eliminated the expensive part of a boot
(migration replay → template clone), and the suite's dominant Unit-lane cost today is process
spawning under the 1-wide `ProcessSpawnLimit` lane, not WebApplicationFactory instantiation. The
one real overcreation instance found — two factory subclasses shared `PerClass` instead of
`PerTestSession` across their (identical-config) consumer classes, plus one ad hoc `new` — costs an
estimated 1-2 extra seconds total in a 25.5-minute suite. That is real but not worth a dedicated
card at current priorities; it is a trivial five-attribute + one-constructor change if anyone
touches those files anyway, not something worth scheduling investigation/plan/code/mutation stages
for on its own.

Not done, noted: if this is ever picked up, the fix is exactly §4 — change `Shared =
SharedType.PerClass` to `SharedType.PerTestSession` on the 5 attribute sites and have
`HomeTaskServiceIntegrationTests`'s one test take the shared `AntiphonWebAppFactory` via
`ClassDataSource` instead of `new`-ing its own.

## Remaining uncertainty

- Did not verify whether TUnit's `SharedType.PerTestSession` for two *different* factory types
  (e.g. `AntiphonWebAppFactory` vs `CardFileSyncEndpointWebAppFactory`) could itself be collapsed
  into fewer combinations by parameterizing one factory rather than subclassing — this would be a
  design question for Plan, not Investigate, and the operator's brief asked only whether
  overcreation exists for the combinations as currently drawn.
- E2E's `KestrelWebApplicationFactory`/`AntiphonAppFixture` was not audited for the same pattern;
  out of scope (different lane, different cost profile, not what the operator described running
  into before).

---
next: none
handoff: WebApplicationFactory usage in Antiphon.Tests already matches the efficient pattern (few types, SharedType.PerTestSession, ResetAsync between tests); found two factory subclasses reused PerClass across identical-config consumer classes (~1-2s total waste in a 25.5min suite) but that is too small to warrant a card.
artifact: docs/investigations/2026-09-12-webapplicationfactory-instantiation-audit.md
