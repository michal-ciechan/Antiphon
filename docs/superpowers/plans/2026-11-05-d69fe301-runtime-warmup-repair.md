# Shared EF runtime warm-up repair (Code d69fe301)

Base: e6da2453013e7312c52c6ac6c117fc86b6c790d3. Test infrastructure only.
Debug provenance: origin/feat/card-task-cdf5415a, docs/investigations/2026-11-05-debug-s4a-77-timeouts.md;
external retained RED 0/77 and cache-off GREEN 77/77: /tmp/cdf5415a-evidence.

New assertion RED: 09c7fa1a125a58ce00c7538b5270000f2b3ea1f8 (unchanged base fixture),
checkpoint 20261105-214943-1020, eight executed/eight assertion failures: one missing real EF
initialization event and seven missing method limiters. Source clean; build provenance verified.

V-1: The normal cached path initializes the actual EF provider/runtime model once in the existing
selected-store assembly hook, before timed bodies. TestDbRuntimeWarmupTests observes EF diagnostics
from module load and requires a pre-body model identical to that used by ordinary consumers.
V-2: The original CP-6 full classes pass with caches enabled and original 35/45-second limits,
including every original 77-case identity. Record setup, clone queue/execution and test durations.
V-3: The ten real-runner cases have an assembly-local process limiter. Their scratch Git helper
measures spawn plus exit/output draining with one cancellation deadline; failures stay failures.
The base's nine additional secret-preservation transport cases use the same strict lane. The
already-limited nested-repository secret case retains its limiter. The method roster guard now
has eight arguments; the seven original variants have retained RED evidence and the added variant
remains a pending negative control for Mutation.
R-1: Worker children, deferred probes and DB-free selections remain cold (full
TestDbFixtureLazyInitializationTests). Preserve isolated clone databases and cache optimizations.
R-2: Classification and slow-registry guards and mechanical instruction contract pass.
R-3: Whole Unit lane, including lifecycle/cache/image branch tests, executes in Final.

PC-1 pending SourceLanding Mutation: remove the assembly hook's runtime warm-up call; run only
TestDbRuntimeWarmupTests.Real_provider_and_model_are_ready_before_the_timed_body, require its pre-body
EF observation assertion red, restore and green. PC-2 pending: remove the scratch-Git deadline; run only Scratch_git_deadline_includes_inherited_output_drains. PC-3 pending: remove one transport method limiter; run its exact Real_transport_methods_use_the_assembly_local_strict_lane case. Mutation owns binding and missing-control discovery.
Code does not execute deliberate mutants. An unbound candidate may be written in docs/superpowers/mutations/.

Run 20261105-220005-1e7c at 710dd6a9b8528740bb4c755795a9edfbe3b4b5ec was stopped using
the checkpoint tool after its successful build and before any result flushed. The full-class audit
found the nine newer real-transport cases lacked a limiter. No row from that canceled run is credited;
the fresh output below prevents reuse of its incomplete proof. Preserve its build/admission costs.

Run 20261105-221029-3b25 at 0521d284e024a8406ca6d3188b013dc1f47626c0 was stopped after
an introduced witness error: TUnit keeps TestContext.Current populated during assembly hooks.
The actual image-hit runtime warm-up was measured at 4.313 seconds; its exactly-once event passed,
but the test's ambient-null predicate was false. The corrected witness requires the concrete
AssemblyHookContext.Current for this test assembly; it retains the positive boundary assertion,
exactly-one real EF event and model identity. No assertion or timeout is loosened. No stopped row
is credited. CP-6 now goes first so the exact requested pack flushes before the longer guard/Unit
rows. Current base roster: workspace 24 + retention 71 + settings 14 + phone-home 29 = 138 cases.
The first complete cached CP-6 execution, 20261105-222542-e953 at
259c2cde2b9a6cda9b982f61751d770b84933052, was 136/138: 76/77 original identities passed,
C0824_ReleaseRoundTripRealRunner(direct) still hit 35 seconds, and the adjacent phone-home
overflow log witness saw zero terminal log records. Runtime warm-up: 10.272 seconds outside
bodies. Clone queue p95/max: 24.865/26.934 seconds; clone execution p95/max: 5.548/9.006 seconds.
The run was stopped before other rows flushed so the measured admission defect could be repaired.
The exact phone-home method passed once on the native e6da2453 base build; inheritance is therefore
not established. Preserve that result without claiming the first whole CP-6 row green.

The two rig classes now share a four-wide TUnit lane (the measured default clone capacity).
Admission happens before timed bodies, not in a new semaphore wait inside them. Real transport
methods retain their stricter process lane. Isolation, assertions, caches and 35/45-second limits
stay unchanged. PC-4 pending Mutation: remove either class's WorkspaceRigConcurrencyLimit attribute;
Scripted_rig_classes_share_bounded_admission must fail. Both class variants remain pending.

Scratch Git's whole-operation deadline is now 1 second: the 372 retained command spans from
the first cached pack had p95 70.768 ms and maximum 325.291 ms; twice the observed maximum,
rounded up to a second, bounds start, exit and both drains. This is one heterogeneous loaded
process sample, not a repeated per-method p95. CARD-1404/1514 own collecting up to three comparable
future cold-process samples; no repeat of an unchanged green selection is required here.
## Performance budget

Fixture lifetime: the existing assembly hook runs once; retain store selection, workers and probes.
No per-test migrations or shared mutable scenario clock. Warm-up gets an independent fixture phase;
consumer DB isolation stays cloned. No timeout increases. Shared hosts remain per scenario because
these tests exercise independent runner identities/clocks; the larger R3 host-sharing work is CARD-1514.

Historical normal CP-6: 75.254 seconds execution, 15 seconds admission; original 77-case master RED:
92.676 seconds execution, 135 seconds admission. Warm bootstrap diagnostic GREEN: 92.081 seconds
execution, 360 seconds admission; cases max 29.980 seconds. These are provisional samples, not p95
per method. No 60-second whole-test floor is applied. Git's finite operation deadline is provisional
pending measured command spans (CARD-1514/1404), and must not be increased to hide red.

Native compile preflight is required by the brief before checkpoint cold builds. The native witness
RED build/run is an additional diagnostic run to establish the new assertion fails before its fix.
At most three checkpoint rows per invocation; never repeat a green row. Unit execution is provisional
and may be long (CARD-1720); record time and escalate an observed >30-minute selection to caller Debug.
Retain exact filters, fresh TRX and phase trace JSONL in ignored evidence. Slot admission is separate
from execution. The independent checkpoint importer build deadline is 15 minutes minimum; it is not
an increased per-test limit. Total execution allowance derives from selected estimates, not 90-minute
wall budget. No full-assembly selection is authorized or required.

Required manual acceptance, outside the three-row checkpoint invocation to respect the brief's row
cap: run `scripts/test-mechanical-pc-contract.ps1` through `scripts/build-slot.ps1`, retaining its
summary and exit. Also run `scripts/check-evidence-diff.ps1` over full base..HEAD and the non-mutating
`git fetch origin master` / `git merge-tree --write-tree origin/master HEAD` check. These are
explicitly listed additional commands, not extra builds or repetitions. Git command spans measure
the combined start/exit/drain operation; separate syscall/drain timings are unavailable, not zero.

## OS and restart audit

All changes are test-only and add no production OS-selected behavior or startup path. The Git closure
uses the existing real runner service, slot pool and retention engine; native Linux retention tests
remain native and Windows obligations are unchanged. Restart: none (server/runner unchanged).

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---:|---:|---|---|
| CP-6 | repair | tests/Antiphon.Tests -> bin-d69fe301-admitted/ | normal-cached-cp6 | /*/*/(C1677RetentionServerTests*)\|(C1677RetentionSettingsTests*)\|(PhoneHomeConnectionTests*)\|(C0824WorkspaceSlotServerTests*)/* | V-2, V-3 | all full classes, no failed/skipped | 138 | 3 | true | ANTIPHON_TEST_TRACE_FILE=.antiphon/d69fe301/admitted-cp6-trace.jsonl;ANTIPHON_TEST_PHASE_ROOT=.antiphon/d69fe301/phases;ANTIPHON_TEST_PHASE_RUN_ID=d69fe301-fix;ANTIPHON_TEST_PHASE_GROUP_ID=CP-6 |
| CP-1 | repair | CP-6 | fixture-and-policy | /*/*/(TestDbRuntimeWarmupTests*)\|(C0824TransportGitTests*)\|(TestDbFixtureLazyInitializationTests*)\|(TestClassificationPolicy*)\|(TestClassificationGuard*)\|(SlowTestRegistryMerge*)\|(SlowTestTripwire*)/* | V-1, R-1, R-2 | all selected; disclose intentional child skips | 20 | 6 | true | ANTIPHON_TEST_TRACE_FILE=.antiphon/d69fe301/admitted-cp1-trace.jsonl;ANTIPHON_TEST_PHASE_ROOT=.antiphon/d69fe301/boundary-phases;ANTIPHON_TEST_PHASE_RUN_ID=d69fe301-boundary;ANTIPHON_TEST_PHASE_GROUP_ID=CP-1 |
| CP-3 | repair | CP-6 | final-unit | /*/*/*/*[Category=Unit] | R-3 | whole Unit lane; disclose inherited red | 1000 | 15 | true | ANTIPHON_TEST_TRACE_FILE=.antiphon/d69fe301/admitted-unit-trace.jsonl;ANTIPHON_TEST_PHASE_ROOT=.antiphon/d69fe301/phases;ANTIPHON_TEST_PHASE_RUN_ID=d69fe301-fix;ANTIPHON_TEST_PHASE_GROUP_ID=CP-3 |
