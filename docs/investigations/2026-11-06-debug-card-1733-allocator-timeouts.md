# CARD-1733: allocator reservation-matrix timeouts

Both incident methods reproduce their unchanged 300-second execution timeouts at
master. The dominant cost is repeated PowerShell filesystem work in the **test
transport adapter**, followed by snapshot restoration and fresh evidence validation.
This is related to the CARD-1731 ledger fixture problem, with a different dominant
helper. It is not the shared EF startup/clone-contention defect behind the 77 CP-6
timeouts. No product or test source was changed by this Debug task.

## Source, execution and attribution

- Debug task: `8f43dbed`, branch `feat/card-task-8f43dbed`.
- Tested master/base: `1a22e0d2ca7b32e7f6ac346a658e1c7824252a46`.
- Host: server2 container `dfcc116be984`, Debian/Linux, 24 CPUs, .NET 9.0.22,
  TUnit 1.44.0, Microsoft Testing Platform 2.2.2. No desktop execution.
- Native run ID: `3c7d1a57-bf42-4387-94a0-2d2855a9cb8a`.
- Exactly one native build and one measured test invocation. The latter selected
  two allocator methods and all seven required guard classes in one execution
  lease. No test was retried, no assertion was loosened and no timeout was raised.
- Final result: **68 executed, 64 passed, 4 failed, 0 skipped; exit 2**.
  This is diagnostic red evidence, not a clean qualification or landing approval.

| Method in `ReleaseTagAllocatorTests` | Earlier exact Code base, seconds | This master, seconds | Result |
|---|---:|---:|---|
| `C1339_PromoteReservationAndTarget` | 300.3217349 | 300.3501077 | execution timeout |
| `C1339_PromoteFreshReservations` | 300.0655713 | 300.1609168 | execution timeout |

The earlier base was `991e805e939b7cd95985a46feac8e378811a6496`, independently
compiled and executed by Code task `0915902c`. Its complete report was read from
`/work/worktrees/task-0915902c/.antiphon/task-0915902c.md`; the report is ignored
evidence, absent from that branch's Git objects. That uninstrumented base selection
executed 2/2 failures in 610.047 seconds. The candidate
`dbd0631edf823fd21c66116401b7ca6c60b607bf` also timed out both methods and exhausted
its 15-minute affected-class row. Those outcomes remain historical facts; the
other five allocator methods are not credited by this two-method Debug run.

The current target method ran from 03:07:33.606 to 03:12:33.956 UTC on 2026-11-06;
Fresh ran from 03:12:33.960 to 03:17:34.120. The next method started immediately
after the first failed, which argues against a leaked serial limiter. Admission
and waiting for the assembly-local `ProcessSpawnLimit` did not consume the next
method's own 300-second budget.

Build and admission costs are separate:

```text
BUILD SLOT granted lease=3c992523-2198-4d7c-a886-1d4aba7da744 waited=0s maxcpucount=6
build: 1729 warnings, 0 errors; Time Elapsed 00:03:47.65
BUILD SLOT released lease=3c992523-2198-4d7c-a886-1d4aba7da744 held=228s

BUILD SLOT granted lease=a5d9803a-6db4-4799-b567-1abcf9fc2ec2 waited=150s maxcpucount=6
native summary: 11m35.316s; measured driver wall excluding admission: 697.711s
BUILD SLOT released lease=a5d9803a-6db4-4799-b567-1abcf9fc2ec2 held=698s
```

The build used the worktree's native default output and `UseAppHost=false`; the
NoBuild run used the same property. `TEMP=/tmp`; the worktree was outside `/tmp`.
The build had a finite 20-minute outer bound and 12-minute admission bound. The
test driver had a finite 14-minute execution bound, 12-minute admission bound and
30-minute outer bound. These wrapper bounds did not replace the 300-second child
execution or 10-second cleanup budgets. None fired except the two child caps.

## What the matrices do

`ReleaseTagAllocatorTests.cs` calls `ScriptHarness`, which starts an owned
PowerShell process for each method. Despite the class name, the incident methods
exercise promotion, not an unbounded allocation inventory scan.

`Invoke-C1339PromotionReservations` creates a successful control and an independent
negative fixture, then applies **27 journal edits** over ten named assertion groups.
Each edit restores a snapshot before and after, independently reads recipients,
and invokes the real promoter, pinned authority and ledger validators. The resumed
matrix uses the same edit inventory and is a future affected consumer.

`C1339_PromoteReservationAndTarget` intends to create **11 promotion fixtures** and
perform 18 promotion attempts, including recovery, journal faults and push races.
The measured run completed eight fixture constructions before its deadline. It
printed G-54 through G-60 as passed before timing out; this partial assertion output
does not pass the method or its remaining recovery/race assertions.

These are small local repositories. During Fresh, the two sampled release trees
had 22 files / 33,917 and 33,475 bytes; their checkouts, including Git metadata,
had 33 / 31 files and about 38 KB. The fixture commits the fixed
`scripts/fixtures/c599-policy-v2.json`, not the live policy or the monorepo. It writes
synthetic TRX evidence and uses a real loopback card-readback listener, shared among
fixtures within that script process. There is no AppHost, database container,
migration, EF model initialization or real nightly execution in these bodies.

## Measured phases

Read-only PowerShell debugger actions were installed before each target script
started. They recorded monotonic entry/return boundaries without changing source,
return values, recipients or assertions. The table contains **completed spans**;
unfinished spans at cancellation are excluded. Nested rows must not be added.

| Phase | Target calls / seconds | Fresh calls / seconds |
|---|---:|---:|
| `Assert-C1339ContainedPath` | 390 / **225.790** | 316 / **193.059** |
| Promotion fixture construction, including nested transport | 8 / 86.254 | 2 / 20.544 |
| Real policy checkout construction, nested in fixture | 8 / 1.825 | 2 / 0.215 |
| Authority/evidence generation, nested in fixture | 8 / 14.790 | 2 / 2.664 |
| Save snapshot | not used | 1 / 0.135 |
| Restore snapshot | not used | 25 / **49.242** |
| Fresh authority validation, including ledger | 8 / 22.162 | 14 / 41.268 |
| Pinned-authority validation, nested above | 8 / 0.991 | 15 / 2.419 |
| Ledger validation, nested above | 8 / 21.099 | 14 / 39.025 |
| Evidence-path validation, nested in ledger | 104 / 19.284 | 184 / 36.683 |
| `Invoke-C1339GitProcess`, including containment | 229 / 144.934 | 152 / 96.977 |
| Same Git-process envelopes minus their containment spans | 229 / **6.326** | 152 / **2.212** |
| Direct authority Git calls | 96 / 1.535 | 63 / 0.294 |

Containment alone accounts for about **75% of Target's capped wall** and **64% of
Fresh's**. Fresh's containment, restores and completed authority checks account
for about 283.57 seconds, with about 16.59 seconds remaining in its TRX envelope.
That remainder includes startup, mutations, wrapper work, unfinished validation,
cleanup and observation overhead; it is not all CPU scheduling delay.

The adapter `scripts/fixtures/c1339-release-git.ps1` checks checkout and bare paths
on every transport call, then checks the working directory again before launching
Git. Every check walks ancestors using `Test-Path`, `Get-Item` and `Split-Path`.
The largest completed check was 1.782 seconds. This repeats hundreds of times for
tiny, task-owned repositories. Git process start/run/wait/drain plus surrounding
wrapper remainder is only 6.326 / 2.212 seconds after subtracting the directly
nested containment checks. Those are parent-side envelopes, not Git internal CPU
or network timings. The recipient is a local bare repository, not GitHub transport.

The completed promotion-reservation validator samples totalled 0.0626 seconds
across 21 calls in Target, with a 0.0102-second maximum. Fresh had four completed
valid-control samples totalling 0.0252 seconds, maximum 0.0186 seconds; its negative
early returns lack exit probes and are not assigned zero time. The relevant
production validator scans one or two rows in these fixtures. No large-N allocator
benchmark was run, and this evidence cannot certify allocator scalability, but a
quadratic allocation scan does not explain these two failures.

One-minute host load sampled each second ranged from 25.74 to 44.54 during Target
and 18.94 to 36.88 during Fresh, on 24 CPUs. Across the whole invocation it ranged
from 16.12 to 44.54. This was a shared, loaded host, not a quiet-host comparison.
Debugger overhead was not calibrated. These measurements identify repeated work;
they are neither uninstrumented benchmarks nor a whole-test p95.

Other limitations: return-boundary spans exclude their final return expressions.
`Get-C1339RecipientState` starts and ends on its return-expression line, so its
apparent near-zero envelope was **discarded**; its child Git/path spans remain
usable. Some early-return/throw spans are incomplete. Individual HTTP, file-hash,
kernel IO, startup and teardown durations remain unknown within the enclosing
measurements. DB/AppHost phases are inapplicable, not measured zero. Timeout
`completion root=True/False` flags are task states, not independent process-exit
receipts; both owned harness groups subsequently completed their normal cleanup.

## Relationship and history

- **CARD-1731 ledger family: related repeated fixture/provider work.** Debug
  `b8770621` measured 160.276 seconds of snapshot restore and 106.776 seconds of
  nested evidence-path checking in its censored Markers sample. Here the additional
  C1339 real-transport adapter's containment walk dominates. Applying only the
  ledger snapshot fix cannot remove Target's 225.790 seconds of containment work;
  Target does not use snapshots at all.
- **The 77 CP-6 timeouts: different immediate cause.** Debug `cdf5415a` and the
  d69fe301 warm-up plan document shared EF/runtime cold startup plus four-wide DB
  clone contention. That repair is relevant to those server rigs, not these
  PowerShell promotion bodies. Moving EF warm-up or changing clone concurrency
  cannot repair this adapter loop.
- Matrix construction appeared in
  `545b15d1699bff1f79503a2da341a3ee1ea46b28` (2026-10-20). The adapter's ancestor
  walk dates to `1c9d0c6342f34a71e1520497d4c7e27ded9cc92d` (2026-10-19).
  `4f9eb9f3aa097ae8e78758d57ed69b591a019ff1` (2026-10-25) already split the
  combined fresh/resumed/target method because it exceeded five minutes. The
  current C# methods have not changed since that split.
- **Earliest proven individual failures:** exact Code base
  `991e805e939b7cd95985a46feac8e378811a6496`, confirmed again at this master.
  These are not introduced by the CARD-1731 snapshot candidate. An exact first-bad
  commit for the individual methods is **unknown**: this bounded investigation
  did not bisect or repeatedly benchmark history. The existing class-cost row
  records 407 serial test-seconds for seven completed Linux nodes from run
  `8f055f87`; retain it as historical cost, not a current p95 or proof of an
  intervening product regression.

## Smallest compliant Code repair

Keep the existing 300-second cap and every real-remote/ref, no-write, journal-byte,
authority, release identity, race and recovery assertion. Repair the test fixture
before changing production allocator code:

1. Replace the provider-heavy ancestor loop in **`Assert-C1339ContainedPath`**
   with native filesystem metadata and native parent-path operations. Preserve
   canonicalization, the ordinal root boundary, every ancestor check including
   ancestors above the fixture root, missing-path behavior and refusal of linked
   or reparse components. Check current metadata on every invocation: do not cache
   successful containment across mutations. Keep the Git command/transport allowlist
   and environment sanitization. Add discriminating contained/sibling-prefix,
   symlink/reparse and changed-after-first-check regressions; retain G-64/G-65.
2. Apply R3 through **one immutable seed/template per selected run/assembly** for
   the tiny policy Git repository, A/B/C object graph and valid synthetic evidence.
   Reuse the existing `GitTreeTemplate` approach where suitable. Clone private
   mutable checkout, bare refs, journals, fault files, GitHub stores and readback
   stores per case; rebind absolute paths and authority identities. Share the
   loopback service once per selected run with separate fixture routes/stores.
   Never share mutable refs or cache authority acceptance. Record seed creation,
   clone preparation and teardown separately, rather than hiding unchanged work
   outside a timer. This removes genuine repeated setup; a Git-seed-only change
   cannot remove the measured transport containment cost.
3. Reuse CARD-1731's already-authored snapshot optimization from
   `dbd0631edf823fd21c66116401b7ca6c60b607bf` through its ordinary integration and
   Review path. Extend the restored-state invariant to
   **`Invoke-C1339ReservationVariant`**: after proving first-variant validity,
   byte restoration, thrown-mutation cleanup and next-variant isolation, retain
   mandatory restoration in `finally` and remove the redundant leading restore.
   A restore failure must terminate the case. Avoid rewriting identical bytes and
   repeated immutable parent inventories; do not reuse prior acceptance decisions.
4. Mark `ReleaseTagAllocatorTests` **Slow + Integration**, retaining its existing
   `ProcessSpawnLimit`, with an adjacent CARD-1733 reason and exact class FQN in
   `slow-tests-allowlist.txt`. Slow is a cost classification, not an exclusion.

This is a fixture repair, not a request to optimize production evidence guards.
After those changes, measure the exact two incident methods once, then complete
the full affected allocator class (including resumed and authority consumers) and
required guard selection through the Code plan's closed checkpoint manifest.
Preserve the existing CARD-1731 incomplete Final obligations. Do not repeat a green
row or substitute this diagnostic run for missing ordinary acceptance.

Both whole-method samples are **censored at 300 seconds**. Multiplying them to
invent a 600-second timeout would be invalid. Keep 300 seconds. A repaired method
should complete in at most about 150 seconds if its current cap is to represent
approximately 2x a completed measured maximum. At n=1, derive a provisional budget
as `ceil(2 * completedSeconds / 10) * 10`, label it provisional and collect comparable
platform/load samples in the next 20 qualifications/20 days under CARD-1733, with
review after five samples. Current completed *diagnostic phase* maxima imply about
4 seconds for one containment call, 7 seconds for one restore, 11 seconds for one
authority check and 33 seconds for one promotion-fixture setup. These are rounded
2x observed maxima, not activated deadlines or post-repair performance promises.
If a repaired matrix still cannot meet 150 seconds, partition by assertion groups
into bounded methods while retaining every edit and both fresh/resumed paths;
do not simply widen its timeout. No post-fix savings were executed here.

## Required guards and separate inherited finding

| Class | Executed | Passed | Failed |
|---|---:|---:|---:|
| `MechanicalPcContractTests` | 11 | 11 | 0 |
| `TaskPlatformGuidanceTests` | 5 | 5 | 0 |
| `TestClassificationPolicyTests` | 39 | 39 | 0 |
| `TestClassificationGuardTests` | 1 | 1 | 0 |
| `SlowTestRegistryMergeTests` | 5 | 5 | 0 |
| `SlowTestTripwireTests` | 2 | 2 | 0 |
| `NativeLinuxPolicyWitnessTests` | 3 | 1 | 2 |
| Two incident allocator methods | 2 | 0 | 2 |

The native-policy failures are `Policy_rows_require_pinned_attribute` and
`Markers_do_not_expand_policy_exclusions`, each about 10.879 seconds. Both report:

```text
reverse-coverage suite=antiphon row=C0824TransportGitTests
concrete=Antiphon.Tests.Application.C0824TransportGitTests
declaring=Antiphon.Tests.Application.C0824TransportGitTests
signature=Scratch_git_deadline_includes_inherited_output_drains()
```

The method has `[NativeLinuxOnly]`, introduced in
`77b4e1d77aaa44bb56bbaaa8edda57afcc1c9896`, but has no matching live policy row.
It is inherited at this pristine Debug base, independent of allocator work. The
full board search for `Scratch_git_deadline` returned zero cards. Caller should
track a separate precise policy repair, preserving the other portable methods in
that class; do not suppress either witness or exclude the whole class. This Debug
did not edit policy or mutate the board. Such a policy repair must refresh both
coupled identities and satisfy the platform-owner requirements.

Allocator adapter/snapshot/template/Slow-registry changes alone need not touch
the protected policy inputs. The current identities were recomputed successfully:

```text
policyHash old = new = 0ac7b7c0dfcf6949ef1714fcfa02d4acd58e6258a8556209248338408f8f46a2
PinnedInputDigest old = new = f510daab843687c8a873d4d3167e01088be50987e796d693729db0418ed16ada
```

No identity was refreshed because no input changed. If Code changes
`tests/test-execution-policy.json` or another protected nightly input, recompute
`Get-NightlyPolicyHash` and `test-live-lane-run.ps1 -PrintInputDigest` together and
report the actual old/new values. No land-graph update, master/rc push or restart.

## Reproduction, evidence and cleanup

The native build was:

```sh
TEMP=/tmp pwsh -NoProfile -File scripts/build-slot.ps1 -Label c1733-native-build -SlotWaitMinutes 12 -- dotnet build tests/Antiphon.Tests -p:UseAppHost=false --nologo
```

The measured driver used the same NoBuild property, `--timeout 14m`, TRX output,
and the exact tree filter below. It ran inside the one execution lease through
the task-local Node launcher, which installed a child-only `pwsh` observation shim
and sampled owned descendant CPU/RSS and host load once per second. The gate's
non-dotnet-wrapper warning was expected: the launcher ran **one NoBuild test
driver**, no nested build or driver loop. The shim forwarded unrelated scripts
unchanged. Native phase metadata was run `c1733-20261106`, group
`allocator-and-guards`, attempt `1`, suite `antiphon`.

The exact filter enumerates the two incident methods and guard declarations;
extra names from embedded probe source match no additional test in these classes.
The actual 68-node roster and counts are given above. Class wildcards are retained.

```text
/*/*/(ReleaseTagAllocatorTests*)|(MechanicalPcContractTests*)|(TaskPlatformGuidanceTests*)|(TestClassificationPolicyTests*)|(TestClassificationGuardTests*)|(SlowTestRegistryMergeTests*)|(SlowTestTripwireTests*)|(NativeLinuxPolicyWitnessTests*)/(C1339_PromoteFreshReservations)|(C1339_PromoteReservationAndTarget)|(C479_V05_MutationBundleFailsClosedAtLow)|(C479_V05b_CodePreparesUnboundCandidates)|(C479_V05c_ReviewJudgesAdequacyWithoutMutants)|(C479_V06_OwnerDocHoldsTheContract)|(C479_V06b_WorkedExamplesAreIllustrative)|(C479_V07_ActiveRecipesRouteBindingHighAndExecutionLow)|(C479_R06_LowNeverDiscoversAndTierRowsStayLow)|(Stage_bundles_leave_twenty_characters_below_the_size_cap)|(Stage_guidance_omits_platform_unless_needed_and_preserves_runner_routes)|(Orchestration_guidance_keeps_inheritance_and_task_scoped_unpinning)|(Review_guidance_keeps_scope_examples_landing_evidence_and_runner_routes)|(Review_guidance_preserves_every_instruction)|(Bundles_read_runtime_defaults_without_pinning_location)|(Platform_pins_are_explicit_and_bundles_defer_to_orchestrator_contract)|(Reroute_diagnostic_names_codex)|(Legacy_default_is_import_only_in_documented_configuration)|(C1383_Detached_reason_is_rejected)|(C1383_Row_without_adjacent_reason_is_rejected)|(C1383_Header_after_records_is_rejected)|(C1383_Valid_reason_pairs_are_accepted)|(C1383_Layout_opt_in_is_scoped)|(C1383_ReadRegistry_retains_legacy_layout_behavior)|(C487_G059)|(Missing_slow_class_is_named)|(C487_G060)|(C487_G061)|(C487_G062)|(C487_G063)|(C487_G064)|(C487_G065)|(C487_G066)|(C487_G067)|(C487_G068)|(C487_G069)|(C487_G070)|(C487_G071)|(C487_G072)|(C487_G073)|(C487_G074)|(C487_G075)|(C487_G076)|(Inherited)|(Plain)|(Rows)|(Data)|(SlowOne)|(SlowTwo)|(ManualOne)|(Bad)|(Registry_matches_compiled_metadata)|(C1383_Rebase_preserves_distinct_appended_pairs)|(C1383_Union_collapsed_lines_are_rejected)|(C1383_Source_only_attribute_does_not_rescue_first_rebase)|(allowlist_file_exists_and_has_spawn_lane_entries)|(unlisted_slow_test_is_a_hit_and_listed_fast_or_allowed_slow_is_not)|(Policy_rows_require_pinned_attribute)|(Pinned_attribute_contract_matches_native_platform)|(Markers_do_not_expand_policy_exclusions)
```

Task-local diagnostics live under the ignored
`/work/worktrees/task-8f43dbed/.antiphon/c1733-debug/`: build/run logs, exact
selection, probe TSVs, phase summary, load/process samples and launch/exit records.
Essential timings, outcomes and limitations are retained in this committed report.
Original artifact SHA-256 identities before required output cleanup:

```text
Antiphon.Tests.dll 556c3af4df70d101f2884557d9d8d29317df30bfba7185db05833cf7a5c75e55
allocator.trx     796ab5496ccf0ea078340e0f955f755118d16cbbc410601f064b8ba3cba61c51
phase-summary.json b62cf830a407e0119f26260ab70c41385680241b7ac7e8f1f272540c823f63d8
host.jsonl       45698415553e71e66b949afae492f47f88f6cc64fbf283e0774edf253779374b
```

Both foreground driver sessions were joined to terminal state. The harness removed
both allocator result/control roots; the final owned-process census found no
remaining test host or target child. Build `bin`/`obj` and native test-result outputs
are removed by explicit literal absolute paths after retaining the above evidence.
There were no source mutations to restore. The sole source deliverable is this
investigation; no product repair, clean Final gate or post-fix speedup is claimed.

--- next stage ---
next: code
handoff: Repair C1339 test-adapter containment with fresh native metadata checks, share immutable seed/readback setup once per selected run with private mutable clones, apply the CARD-1731 snapshot optimization and finally-based C1339 isolation, and classify the allocator class Slow. Keep 300s and every assertion; measure the incident methods and complete affected-class/guard obligations. Track the separate C0824 native-policy reverse-coverage finding with coupled hash/pin refresh if policy changes.
artifact: docs/investigations/2026-11-06-debug-card-1733-allocator-timeouts.md
