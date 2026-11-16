# CARD-1912 and CARD-1913 verification reconciliation

Read on 2026-11-16. No build and no test was run. This note reconciles what the two cards asked for, what is on `origin/master`, and which verification rows are still missing. It does not design a fix.

Observed tips at the end of the read: `origin/master` `870a93d21f2b18c85a8b0905ab689b28e3099813` (batch 253), `origin/rc/2026-11-23` `9b98828eb34eb552ce62bcde5a64dd045a872a88`. That rc tip is an ancestor of that master tip. Live Windows qualification run `a21d8174` was not opened from here. Its source is that rc tip, as given by the caller.

Board Antiphon `8988ca03-7414-47ad-b0b6-51556c701703`. Both cards are `FullOnly` / `FullOnly`, platform Any, High / Soon / rank 5.

## One change, two cards

CARD-1913 has no task and revision count 0. Its description, reviews, and publication all live on CARD-1912. Dispatching a second clock implementation would duplicate work that is already on master.

CARD-1912 history is nine revisions, all moves. Current column is InProgress only because this Investigate task was dispatched (revision 9, 2026-11-16T23:40:27Z). Before that it sat in Review after round 2 (revision 8). CARD-1913 is still Backlog, `updatedAt` equal to `createdAt`.

## What each card promised

CARD-1912 acceptance, from the card body:

1. The host-load identity lookup must keep one SQL shape, so a new key set does not compile a new EF query.
2. A 60-second JIT trace (`Microsoft-Windows-DotNETRuntime:0x18:5`) while ingest runs shows no `dynamicClass` loads under `HostLoadIngestService.IngestAsync` after warm-up.
3. A unit check: two ingests with different keys produce one `QueryCompilationStarting` for the identity query.

CARD-1913 acceptance, from the card body:

1. Hoist instance `UtcNow()` (and any other instance call) out of EF lambdas so the dispatcher tick and the delivery-failure sweep stop compiling a new query per call. Keep database `now()` where the provider translation is the clock.
2. The same 60-second trace shows no `dynamicClass` loads under `AgentTaskDispatcher.DispatchCoreAsync` or `RecoverDeliveryFailureNotificationsAsync` after warm-up.
3. A unit check: two dispatcher ticks compile the optional-work expiry `ExecuteUpdate` once.

The 110-row roster below is the round-2 Review's ordinary selection. It is not text on either card. Standing minimal-testing policy assigns the whole Unit lane to the nightly backstop and the rc qualification, not to another per-task Final.

## What landed on master

Merge `fc30e791-9d7d-44a0-8229-361ceeb35980` (batch 227) fast-forwarded master to `f701b2ba967d503427d7064fe4fffa17b8c1738d`. That commit, and every row below, is contained in current `origin/master` and in `rc/2026-11-23`. The merge report says standing authority covered landing without Review approval. Nothing in that batch was pushed to an rc ref at the time. The later rc cut contains it.

| Master commit | Subject |
|---|---|
| `541eae6d18c48d1aab785e645d13299d6b12cb34` | Witnesses. Cherry-pick of `e27cd034`. |
| `3cdd5a4c62c417cb2cb481d0cad09dfdc085971c` | Unnest identity SQL and hoisted instance clocks. Cherry-pick of `f6746246`. |
| `85997c7fb9df9fd205d3ee07352a4584cdedaf6d` | Database-clock witnesses before the guard narrowing. Cherry-pick of `0a546811`. |
| `f658c8bd6375221fd1174a2cdd12d93a9fc20484` | Restore inline `DateTime.UtcNow` on the conversion lease and per-row `Guid.NewGuid()`. Cherry-pick of `471be925`. |
| `ab20613b3b2e4b14fd6fc14c67b93598c2504664` | Derived-impact index regenerated on the combined tree. |

The reviewed tip `1f44a31a694edab5a1f3ce160b9a0505ef3adf43` is not an ancestor of batch 227. The merge skipped it because `tests/derived-impact-index.json` conflicted, and replaced it with `ab20613b3`. Patch-ids of the seven picked commits matched.

On current master the mechanism is still the one that landed:

- `server/Infrastructure/HostLoad/HostLoadIngestService.cs:107-131` is one `unnest` shape for every key set.
- `server/Application/Services/AgentTaskDispatcher.cs:543` reads `expiredAt` before the bulk expiry update, and `:549` keeps `Guid.NewGuid()` inline.
- `AgentTaskDispatcher.cs:5292` reads `now` before delivery-failure recovery.
- `server/Application/Services/OutboundConversionTaskRunner.cs:59` still compares `LeaseUntil` to inline `DateTime.UtcNow`.

### Reviews

Two rounds, both on CARD-1912, both titled as covering CARD-1913. No round 3.

| Round | Task | Subject SHA | Verdict | Scope | Source clean |
|---|---|---|---|---|---|
| 1 | `23794b24-5347-4a10-9e28-93e4571fd08f` | `25fe10cebde5e8e17530d579a7c4e0b57f080515` | Found. D1 lease clock became application time. D2 CP-5–CP-25 and the integrated candidate unverified. D3 index conflict. | Interim | false |
| 2 | `ec0488ff-0329-4402-a62d-980437ce8ceb` | `1f44a31a694edab5a1f3ce160b9a0505ef3adf43` | Found. D4 synthetic SQL witness does not call production. D2 CP-3, CP-4–CP-102, and CP-201–CP-208 have no fresh Review receipt. | Interim | false |

Round 1 next was code. Round 2 next was code. Neither approved a land. Code tasks were `56fefc87-86ff-49f8-8af2-ee7e16176b04` (tip `25fe10ce`) and `c995680c-0c7c-4ae3-99b9-56d5c954c0c1` (tip `1f44a31a`). Debug `7bd634da-cf30-4434-9e45-10bad502c708` tip `116e1c490e2ea4bdd3ed49a6455c7c925160e9db` is not on master and not on the rc.

### Later publication that touches the same witnesses

Merge batch 238 ends at `2972ffa395c77b0776fd429b2d0ca0a0d444baa7`. It contains CARD-1925:

| Master commit | Role |
|---|---|
| `bb7c43595a3322e855948467f8def05bb9b7cbe5` | `EfProductionClockQueryTests` |
| `011ac5318f3055e020c090df43c9072651c523bc` | Fixture identities |
| `02faa4518da4f848b7f4658d95a632824e04fe5e` | Cherry-pick of reviewed tip `eb7bb5473d5c789e6d930087a0d0c37b5fef4824` |

CARD-1925 Review `bf127f48-f145-446e-89a3-27f30bd5e956` is round 1, finding Clean, `ordinaryScopeCompleted: Full`, `reviewedSourceClean: false`. The false clean bit is the Windows-only skip `WindowsUnitPlatformAdmissionTests.C1210_Windows_linux_only_outcomes_are_exact` plus a receipt bound to compose SHA `3ab96a934f905d5319ff43d441caccb2c151bb1c` rather than `eb7bb547`. The review still said land. Its own checkpoints, on that compose, were CP-1 4/4, CP-2 14/14, CP-3 53/53, CP-4 34 passed and 1 skipped, CP-5 27/27, CP-6 11/11. Those CP numbers are the CARD-1925 plan, not the CARD-1912 roster.

The warm-cache defect in the original compile-count fixture landed after batch 238: red `e9578751aa499252f22af2038c0123728d017824`, fix `ca3ba4649138f85661072d26d184b94da01a4408` (`UseMemoryCache` per fixture in `EfCompiledQueryCacheTests`). Both are on current master and on `rc/2026-11-23`. The earlier task SHAs `95910d4c` and `c02546483` are not the published objects.

`6c28fa5cde32ebbc08a35899cf8316f7ba85f743` raises the C519 broker startup budget to the sibling 120-second budget. It is on master and on the rc. That is the failure mode of round-2 CP-208 (`RedpandaBuilder.StartAsync` before product code). It is not a recorded green run of `ChannelOutboundUnifiedTransportTests`.

## Card-level rows

| Row | Where | Platform | Evidence |
|---|---|---|---|
| Identity compiles once | `EfCompiledQueryCacheTests.Identity_lookup_compiles_once_for_distinct_key_sets` | LINUX-OK. About 1 minute inside the 14-test class (Review CP-1 execution 44s). | Passed inside Review round 2 CP-1 14/14, Merge 227 CP-1 14/14, and CARD-1925's 14/14 neighbour run. The per-fixture cache fix is on the rc. |
| Expiry compiles once | `Optional_work_expiry_update_compiles_once_across_ticks` | LINUX-OK, same class. | Same three green runs, plus the landed warm-cache repair. |
| Recovery compiles once | `Delivery_failure_recovery_compiles_once_across_sweeps` | LINUX-OK, same class. | Same. |
| Production SQL for four sites | `EfProductionClockQueryTests` four methods | LINUX-OK. Code row about 1 minute; Review repeated it in five processes (38–60s each). | On master via batch 238. Covers dispatcher bulk expiry, `ChannelOutboundDeliveryPump.ClaimAsync`, queue `CancelExpiredBriefsAsync`, and the conversion `now()` fence. |
| 60s JIT trace, ingest | Manual `dotnet-trace`, not a test | NEEDS-WINDOWS. About 2 minutes of collection. | Not run by Code, either Review, Merge 227, CARD-1925, or run `a21d8174`. A suite TRX cannot show `MethodJittingStarted`. |
| 60s JIT trace, dispatcher | Same trace, frames `DispatchCoreAsync` and `RecoverDeliveryFailureNotificationsAsync` | NEEDS-WINDOWS. Same collection. | Not run. |

Fifteen of the eighteen audited hoists still have no production-call witness. CARD-1946 (Backlog, Low, revision 0) lists them and says they are not a land blocker. They are not an open CARD-1912 or CARD-1913 row. `ClaimAsync` does not cover the four pump hunks.

Positive controls named by the round-2 Code task stay unrun. The operator waiver of 2026-11-12 covers mutation. This reconciliation does not ask for them.

## Round-2 roster, and what later runs already supplied

The closed list is `/work/review-evidence/task-ec0488ff/replay-manifest.md`. Fresh Review credit exists only for CP-1 (14/14) and CP-2 (107/107) at `1f44a31a`. CP-3 was interrupted twice with no count. CP-4 through CP-102 and CP-201 through CP-208 never started in that Review. `reviewedSourceClean` stayed false. That is why the survey still says this is not a Full ordinary certificate.

Merge 227 then ran, on the published tree, a CP-1 of 14/14, `C1514HostLoadHistoryTests` 22/22, the other C1514 host-load classes 27/27 (Aggregate, Api, Migration, Recommendation, Scenario, Script), and the guard union 107/107. Its impacted-class pass listed aggregate counts and these exceptions only:

- Inherited, also red alone at the batch base: `PtyDeliveryCeilingsTests` two methods (expected InboxConhost, got UnixPty) and `PhoneHomeOutageMentionTests.In_flight_mention_Enter_late_confirms_without_retyping`.
- Failed once under load, then passed alone: `DelegationDispatchRecoveryBoundaryTests` C1153, `SessionRuntimeActionRoutingTests` C1259, `DispatcherSweepLifetimeTests.Abandoned_sweep_runs_on_its_own_scope_and_that_scope_is_disposed_after_it_ends`.
- `ChannelOutboundUnifiedTransportTests`: 3 cases, then stopped. Cost table records 1277 serial seconds, 17 nodes, not a p95.
- `ChannelOutboundUnifiedCrashTests`: not run. Cost table records 837 serial seconds, 21 nodes, not a p95.
- Windows-only classes, skipped on Linux: the eight named in the next section.
- Session-runner `C1514HostLoad*` sampler classes, including native overhead: not run in that merge.
- Capped debt of 103 `AgentTaskDispatcher.cs` edges and 126 `SessionMessageQueueService.cs` edges: not all selected.

Those merge counts are a report, not a checkpoint receipt. They are still later evidence than round 2, and they sit on the tree the rc was cut from. They do not justify another Review of this card.

Chunk ids for run `a21d8174` are not a stable number. `scripts/lib/nightly-tests-impl.ps1` packs a full run with `sourceId = 'all'` into `all-group-N`, about 15 minutes a bin, namespace by namespace. The TRX is `<suite>-all-group-N.trx`. Membership for that run is `timing/groups-planned-<suite>.json`. An older qualification's `all-group-30` is not this run's number. Match the class name.

### Rows the Windows run can cover

Suite `antiphon`. Look up each class in `groups-planned-antiphon.json` and read `antiphon-all-group-N.trx`.

| Class | Why it is still open | Platform |
|---|---|---|
| `EfCompiledQueryCacheTests` | Linux rows already passed. Native credit only. | The rc contains the warm-cache fix. |
| `EfProductionClockQueryTests` | Linux rows already passed. Native credit only. | LINUX-OK already; Windows is extra. |
| `C1514HostLoadHistoryTests` | Merge 227 reported 22/22. Native credit only. | LINUX-OK already. |
| `C1514HostLoadApiTests`, `C1514HostLoadAggregateTests`, `C1514HostLoadRecommendationTests`, `C1514HostLoadMigrationTests`, `C1514HostLoadScriptTests` | Inside the merge's 27/27. | LINUX-OK already. |
| `ChannelOutboundUnifiedTransportTests` | Linux never finished the class. The C519 budget fix is on this rc, so this TRX is the first full chance. | LINUX-OK class, running here on Windows. Recorded cost 1277s. |
| `ChannelOutboundUnifiedCrashTests` | Not run on Linux. | Recorded cost 837s. |
| `AgentSessionServiceIntegrationTests` | Every case skips unless Windows. | NEEDS-WINDOWS |
| `DelegateLaunchArgvIntegrityTests` | Skips unless Windows (`CommandLineToArgvW`). | NEEDS-WINDOWS |
| `DelegationBriefCeilingPtyTests` | Skips unless Windows ConPTY. | NEEDS-WINDOWS |
| `GrokDelegateEndToEndTests` | Skips unless Windows. | NEEDS-WINDOWS |
| `GrokSignInRuntimeTests` | Skips unless Windows. | NEEDS-WINDOWS |
| `SessionMessageQueueGrokPtyIntegrationTests` | Skips unless Windows. | NEEDS-WINDOWS |
| `SessionMessageQueuePtyIntegrationTests` | Skips unless Windows. | NEEDS-WINDOWS |
| `SessionQueueReceiptPlumbingTests` | Skips unless Windows. | NEEDS-WINDOWS |

Suite `session-runner`, file `session-runner-all-group-N.trx`:

| Class | Why |
|---|---|
| `C1514HostLoadNativeTests` | `Windows_ProductionFactoryPreservesHostSamples` skips on Linux. |
| `C1514HostLoadNativeOverheadTests` | The Windows full-runner experiment skips on Linux. The Linux pair is `[NativeLinuxOnly]` and is a different case. |
| `C1514HostLoadSampleTests`, `C1514HostLoadSpoolTests`, `C1514HostLoadWireTests`, `C1514HostLoadAuthorityTests`, `C1514SamplerTickCpuTests` | Named by the merge as not run. They are ordinary session-runner classes, not a new card filter. |

The other round-2 classes (CP-8 through CP-101, and the repair splits CP-102 and CP-201–CP-207) are the Unit lane. Their names are the filters in the replay manifest: the clock-owner classes in CP-8, `TerminalRunnerSeatReleaseTests`, the `ChannelOutbound*` splits, the reply classes, and the impacted and owed classes through `WorktreeRetirementRaceTests`. Merge 227's report says those selected Linux classes passed except the inherited and load-flake names above. Run `a21d8174` is the native pass over that same suite. A red class there is an rc Debug under the run-group loop, not a third Review of CARD-1912.

### Rows run `a21d8174` will not contain

The rc profile in `tests/test-execution-policy.json` excludes these. A missing TRX is expected and is not a CARD-1912 gap.

- `GrokRulesCompactionAcceptanceTests`, `GrokRulesDispatchAcceptanceTests`, `GrokRulesHerdrAcceptanceTests`, `GrokRulesLiveMappedDispatchTests`, plus the other `GrokRules*` exclusions on that profile. Owner CARD-0599. CP-54 executed 0 because `GrokRulesLiveMappedDispatchTests` is `[Explicit]`.
- `DelegationSequencingE2ETests` and `OutputDistillationApplyCanaryTests` (suite `e2e`). Same owner. Headed canary, not this card.

`C1210_Windows_linux_only_outcomes_are_exact` is the one skip that kept CARD-1925 `reviewedSourceClean` false. It is a platform-admission guard, not a clock behaviour. The same Windows run's antiphon chunk that contains `WindowsUnitPlatformAdmissionTests` is where that single method would show as passed rather than skipped. It does not gate CARD-1912.

## Recommendation

Do not open Review round 3 of either card. Do not dispatch Code for CARD-1913.

Leave both cards open for one shared manual trace, and for nothing else on these cards:

- On a running `Antiphon.Server` whose build contains `3cdd5a4c62c417cb2cb481d0cad09dfdc085971c`, collect about 60 seconds with provider `Microsoft-Windows-DotNETRuntime:0x18:5`. Pass means no `dynamicClass` loads under `HostLoadIngestService.IngestAsync`, `AgentTaskDispatcher.DispatchCoreAsync`, or `RecoverDeliveryFailureNotificationsAsync` after warm-up. This is NEEDS-WINDOWS, about 2 minutes, and it is not inside run `a21d8174`. This investigation did not read the live server version, so the trace still has to be taken against a process that actually contains that commit.

Read, do not re-run, these classes in run `a21d8174` when their chunks finish: the eight ConPTY classes, `ChannelOutboundUnifiedTransportTests`, `ChannelOutboundUnifiedCrashTests`, and the session-runner `C1514HostLoadNative*` Windows arms. A failure there is an rc Debug. A pass supplies the native lane. It still does not replace the JIT trace.

After the trace is recorded, both cards can close with a note. CARD-1946 remains the follow-up for the fifteen unwitnessed hoists. The Unit-lane roster is not a reason to keep these cards in Review.

## Not done, noted

No new witness and no production edit. The fifteen-site gap already has CARD-1946.
