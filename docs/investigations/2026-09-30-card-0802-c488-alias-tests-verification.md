# CARD-0802 C488 alias verification

## Baseline protocol

- Source SHA before test edits: `037b24a352a960a0ed35a10d171ea92f2814f4be`.
- Environment: Linux x86_64 (`4.15.0-213-generic`), .NET SDK `10.0.401`, runner mirror `/work/worktrees/task-6dfe4f27`.
- The task-owned census driver is `.antiphon/c802-census.ps1`. Its initial plan-verbatim SHA256 was `8fd0ca720389f6959bd4a717b9db457b53196360dbab0830d1fe0bc8c7b3d7fe`. After the first CP-2 attempt exposed multiline diagnostic records, the driver gained a loss-checked line-folding adapter; its final SHA256 is `ea9e6240e537e74448d9d3f13a88f8324e58ecdd01e8dc37d40ca321815ab918`. CP-2 and CP-5 use these identical final bytes.
- CP-1 builds `bin-c802-before/` and selects exactly the three named methods in the plan, preserving its fresh TRX and producer report. CP-2 discovers the unfiltered assembly from that retained output. Case durations will be read from outer `UnitTestResult` records joined by test ID to `TestDefinitions/UnitTest/TestMethod`.
- Baseline card evidence of 187.870 s belongs only to `C488_DetachedFollowUpPublishesReviewedFix` in an earlier Linux full-suite run; it is not a measurement of either alias here.

## Checkpoint results

The first CP-1 attempt at `b57343396a14b26093c865e3cecb140a69fd4385` is retained at
`.antiphon/checkpoints/20260930-091428-142a/`: 3 executed, 0 passed, 3 failed,
0 skipped. Each failure was the fixture's nested verifier build. A separate,
explicitly diagnostic build of the same miniature solution reproduced SDK
`10.0.401` error `MSB3030`, missing `obj/Antiphon.Tests/debug/apphost`; the same
build passed with process environment `UseAppHost=false`. The diagnostic used
the host build slot. The baseline checkpoint will be rerun with that Linux
environment setting inherited by the nested verifier build. No test body was
changed for this recovery.

CP-1 rerun at `8c42e138dae1ab896d86018976c846932ee1be7c` is green:
3 executed, 3 passed, 0 failed, 0 skipped. Filter:
`/*/*/AgentTaskLandSourceFreshnessTests/(C488_DetachedFollowUpPublishesReviewedFix*)|(C488_BehindSelectsRemote*)|(C488_DetachedFollowUpRequiresFetch*)`.
Fresh TRX: `.antiphon/checkpoints/20260930-092454-d66b/rows/CP-1/run.trx`.
Complete producer report copied verbatim to `.antiphon/c802-before-producer.json`
(SHA256 `bd7fc85bbc66117ea8007be0799b75f6334635ee9587dda084370e3bd87198e4`).
Host: Debian 12 x86_64, SDK 10.0.401; `UseAppHost=false` inherited by the
miniature verifier. Build slot: unleased after 60 s broker wait. CP-1 wall:
325 s; row wall: 175.826 s. Outer TRX durations (all passed): main
`17.2107442` s, Behind alias `22.9444085` s, RequiresFetch alias
`24.8679250` s. Combined three `65.0230777` s; aliases `47.8123335` s.
Each identity invokes the main body, which calls the real landing verifier
once and the independent target verifier once: 6 real invocations total.

CP-5 remains pending.

The first CP-2 attempt at `f3386cc04707d2628a3d1ba87c9535827c2d2915`
is retained at `.antiphon/checkpoints/20260930-093158-f7a6/`, with its
phase directory archived as `.antiphon/c802-census/before-attempt1/`. It
failed before export because 13,118 raw lines contained the discovered-state
marker while the nightly one-line parser returned 13,092 nodes. Inspection
showed 28 records split across lines by embedded newline display arguments.
The task-local adapter folds each such record, checks that no state marker was
lost, then applies the same pinned strict parser. This changes census input
framing only; raw diagnostics remain retained. The rerun uses the existing
CP-1 producer output and performs no test execution.

CP-2 rerun at `9fe5b1fca35510a8f8418961c52917535e42f2dc` passed:
0 tests executed, discovery wall 86 s including 60 s unleased slot wait.
The full unfiltered TUnit 1.44.0 / MTP 2.2.2 census contains **13,118
expanded UIDs**. The affected class has **47 source methods / 59 expanded
UIDs**; the main and both aliases each occur once. Raw logs, folded log,
export and normalized identities are under `.antiphon/c802-census/before/`.
The two CARD-0590 roster JSONs are frozen at source commit
`ceff11d83eee33b1ebbd45dee64a1821cd753dde`, count 40 and source hash
`ad8333885cf14c876f3eb9e76621f3654edbecb16647114b9a61a172c1f2b91f`.
They exclude this process-spawning class from that historical admitted-Linux
subset. Current 47/59 is the full assembly discovery; neither roster was
changed or relabeled as Unit.

## Unit selection frozen before CP-4

CP-2 found **U=3,567** Unit-category UIDs, with zero Unit OptIn/Explicit
exclusions. On this Linux host, the source-conditioned skip ledger is:

| Class and exact methods | Expanded skips | Source condition |
|---|---:|---|
| `TimeoutTests.windows_quick_row_finishes_beside_a_slow_row`, `windows_row_arguments_round_trip_intact`, `windows_chatty_row_drains_interleaved_stdout_and_stderr`, `windows_row_timeout_kills_the_start_b_grandchild` | 4 | Windows child-process behavior |
| `LandingRemovalPolicyControlTests.C665_LockedFileMidDeleteResumesOnLaterPass`, `C721_HeldHandleDuringCleanupStaysRegisteredOrRecorded` | 2 | Windows file sharing locks |
| `AgentRegistrySettingsTests.The_shipped_codex_definition_resolves_to_a_real_executable_on_this_machine` | 1 | Windows npm shim layout |
| `GrokRulesTransportCompatibilityTests.Unsafe_raw_rules_are_refused_server_side_before_runner_calls` (12 argument rows) | 12 | Windows-only argv guard |
| `AgentPinPathTests.V01_canonical_cwd_uses_windows_separators_and_drops_trailing_slash` | 1 | Windows drive-rooted path |
| `SessionDeliveryProfileTests.Phone_home_Grok_never_uses_local_modern_evidence`, `Phone_home_Claude_keeps_the_inbox_ceiling_for_its_own_kind` | 2 | Modern ConPTY evidence only on Windows |
| `PtyDeliveryCeilingsTests.A_runner_on_the_inbox_conhost_downgrades_a_modern_server`, `A_runner_on_the_modern_backend_confirms_the_raised_ceilings`, `A_silent_runner_leaves_this_processes_own_decision_standing` | 3 | Shipped conpty.dll unavailable on Linux |
| `ClaudeRemoteControlLaunchArgsTests.Off_settings_path_round_trips_through_LaunchArgvGuard` | 1 | Windows CommandLineToArgvW |
| `AgentExecutableResolverTests.Resolves_sibling_flavor_when_configured_one_is_gone` | 1 | Windows executable flavors |
| `DirectoryBrowseServiceTests.prefix_returns_matching_child_directories`, `partial_leaf_matches_substring_within_child_name`, `trailing_slash_lists_children_of_that_directory`, `existing_path_reports_exists_true`, `caches_within_ttl_and_refreshes_after` | 5 | Windows MockFileSystem drive letters |
| `DelegationReportFormatterTests.reported_repository_paths_normalize_relative_and_absolute_windows_forms` | 1 | Windows drive-rooted report paths |

Thus **S=33** documented skips and **E=U-S=3,534** expected executed
and passed results, with zero failures. CP-4 will join TRX result
multiplicities to the selected census; a mismatch will be investigated and
recorded rather than silently changing this frozen expectation.

## Pending controls

CARD-0802 focused cases exercise the ordinary PC-31/44 seams. Their
compiling mutation variants and all other historical C488/C494 PCs remain
pending post-land method-scoped SourceLanding Mutation; no nightly or
ordinary green run will discharge them.

## First after-group attempt

At `3e0bd6950a6c6cc1d6c2985acc3e7b2bd70b1bbd`, the committed
S2-S3 run `.antiphon/checkpoints/20260930-094832-2cee/` gave CP-3
**59 executed, 59 passed, 0 failed, 0 skipped**, filter
`/*/*/AgentTaskLandSourceFreshnessTests/*`. The main real-verifier case,
the distinct negative case and both focused cases passed. Its fresh TRX is
`rows/CP-3/run.trx`, with durations: main `18.0166181` s, Behind
`0.5805915` s, RequiresFetch `0.2431845` s, negative verifier
`8.5301682` s. The two aliases total `0.8237760` s, a **98.3%**
reduction from CP-1's `47.8123335` s; the three C488 cases total
`18.8403941` s, a **71.0%** reduction from `65.0230777` s. Their
successful real-verifier invocation count is structurally 2 after versus
6 before; the separate negative verifier invocation remains in both
affected-class runs. CP-3 row wall was 220 s, with 60 s unleased slot wait.

The same run's CP-4 selected 3,534 executed results and 33 skips, as
frozen, but had 3,528 passed and **6 failed**. All failures are argument
rows of `TestClassificationPolicyTests.C487_G065`, `C487_G066`,
`C487_G067` and `C487_G069`: their nested probe builds an apphost and
then searches for its executable. The process-wide `UseAppHost=false`
suppressed those probe apphosts. The full failed result and TRX are
retained in that run.

The committed CP-4-only rerun at `5bad55b1ec9ceeb431fdd43ab2882e5db900574a`
without that variable, run `.antiphon/checkpoints/20260930-100343-c621/`,
still failed: 3,534 executed, 3,527 passed, 7 failed, 33 skipped. The
six probe failures changed to `MSB3030` because this host lacked the
.NET 9 Linux apphost pack; one unrelated
`ResilienceBudgetTests.Slow_first_attempt_consumes_the_same_budget`
timing assertion also exceeded its 12 s ceiling under load. A leased
diagnostic `dotnet restore --runtime linux-x64` on the same miniature
solution fetched `Microsoft.NETCore.App.Host.linux-x64` 9.0.20 into the
task host's NuGet cache; a second leased diagnostic build without
`UseAppHost=false` then passed. These were prerequisites to diagnose
the checkpoint failure, not additional Antiphon test coverage. The next
S2-S3 attempt can run both rows without the variable, keeping a green
group report for CP-5's producer gate. The initial attribution to an
environment collision was incomplete: the missing host pack was the
underlying defect when apphost generation was enabled.

## Green S2-S3 producer and timing

The final S2-S3 run at `c7a81aeae736d7fe56b4312b50fea731a0321259`,
`.antiphon/checkpoints/20260930-101423-dfab/`, passed both rows. Its
complete `report.json` is copied verbatim to
`.antiphon/c802-after-producer.json` (SHA256
`847a1b71941a9b2215ecea726b6a3f9a7820ff777ccb600e6559e4dc8f055ae8`).
The host was Debian 12 x86_64 with SDK 10.0.401; .NET 9 host pack 9.0.20
was now cached. Both rows waited 60 s for an unavailable build-slot broker
and ran unleased at the checkpoint tool's restricted CPU budget. The group
wall was 848.7 s. CP-3 row wall was 191.1 s and CP-4 row wall was 307.7 s.

| CP | Exact filter | Executed | Passed | Failed | Skipped | Fresh TRX |
|---|---|---:|---:|---:|---:|---|
| CP-3 | `/*/*/AgentTaskLandSourceFreshnessTests/*` | 59 | 59 | 0 | 0 | `.antiphon/checkpoints/20260930-101423-dfab/rows/CP-3/run.trx` |
| CP-4 | `/*/*/*/*[Category=Unit]` | 3,534 | 3,534 | 0 | 33 | `.antiphon/checkpoints/20260930-101423-dfab/rows/CP-4/run.trx` |

The CP-4 TRX contains all 3,567 selected Unit identities, counting the
33 `NotExecuted` skip rows. Joining every TRX test ID through its
`TestDefinitions/UnitTest/TestMethod` to CP-2's selected discovery by
Class.Method multiplicity found **zero missing or extra results**.
The 33 skip identities and their multiplicities matched the frozen ledger.

| C488 method | CP-1 before, s | Final CP-3 after, s | Outcome | Real verifier invocations before → after |
|---|---:|---:|---|---:|
| `C488_DetachedFollowUpPublishesReviewedFix` | 17.2107442 | 15.7216391 | passed → passed | 2 → 2 |
| `C488_BehindSelectsRemote` | 22.9444085 | 0.8081520 | passed → passed | 2 → 0 |
| `C488_DetachedFollowUpRequiresFetch` | 24.8679250 | 0.3138557 | passed → passed | 2 → 0 |
| **Combined aliases** | **47.8123335** | **1.1220077** | | **4 → 0** |
| **Combined three** | **65.0230777** | **16.8436468** | | **6 → 2** |

The final aliases were **97.7%** faster combined and all three were
**74.1%** faster combined by outer TRX duration, exceeding the plan's
80%/40% measurement goals. The final CP-3 run had the installed .NET 9
host pack and no process-wide `UseAppHost=false`; the first green CP-3
run under CP-1's earlier override independently measured 98.3%/71.0%
reductions. These are case durations, distinct from row/group walls and
from CARD-0802's historical 187.870 s main-case sample. The retained
`C494_DetachedFixVerifierFailurePreventsPublication` separately passed
in 10.1536085 s in the final class run.
