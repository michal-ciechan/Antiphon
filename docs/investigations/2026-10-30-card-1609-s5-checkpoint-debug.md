# CARD-1609 S5 checkpoint Debug

Commission: task `a2935adb`, Linux, 2026-10-30. Assigned base
`0080d76f8021ebdf6f73650f3c1b2b3d9816dc2d`. This record preserves the failed
run; it does not grant retirement, deployment or deletion authority.

## Historical run

Source `7ad5d070fcf52b02acf1ce961037e56d85eea645`, run
`20261030-124644-de27`, retained under
`/work/worktrees/task-b2fc0aa5/.antiphon/checkpoints/`.
Debian 12, 24 cores, initial load 14.63/13.87/24.61, available RAM 100354 MiB.
Run wall 2790.860 s; charged execution 2595.326 s; admission 195.445 s; exit 5.
The receipt has three green, two red, four timed-out rows.

| Row | Historical executed/pass/fail/skip | Execution s | Row admission s | Cap s |
|---|---|---:|---:|---:|
| CP-3 | unknown (timeout) | 601.424 | 75 | 600 |
| CP-4 | unknown (timeout) | 122.806 | 15 | 120 |
| CP-5 | 30/30/0/0 | 41.849 | 30 | 120 |
| CP-6 | 132/131/1/0 | 123.186 | 0 | 360 |
| CP-7 | 52/52/0/0 | 32.105 | 15 | 240 |
| CP-8 | unknown (timeout) | 120.791 | 15 | 120 |
| CP-9 | unknown (timeout) | 361.479 | 0 | 360 |
| CP-15 | 16/16/0/0 | 173.873 | 0 | 360 |
| CP-16 | 23/22/1/0 | 85.577 | 0 | 360 |

One shared build: compiler wall 749.79 s, driver-accounted build 883.045 s,
execution 901.882 s, wall 973.068 s including 45 s slot wait and custody overhead.
There was no build inside the timed-out rows. Their TRX and phase records are
absent: per-test distributions, startup/setup/body/teardown attribution are
unknown, not zero. A censored row duration is not a measured p95. Original caps
were provisional estimates, not demonstrated 2x p95.

## Assertion failures

CP-6: `C1105_Git_audit_hidden_locations(origin-gone-counts)` expected
`PASS origin-gone-counts` but received empty stdout. The inner Node assertion at
`scripts/fixtures/c1105-git-audit.cjs:802` pinned the end of the diagnostic before
the legitimate recovery suffix. Production refused `RecycleUnpublishedWork`
with the correct 2 commits / 2 tips / 1 gone-tracking / 1 unadvertised-local.
This is a fixture expectation defect, not unsafe product acceptance. The prior
task reproduced it at its base and repaired it in
`c80f5de16ef550ac621eb6e3574323cba760a607`; that fix is already in this task's base.

CP-16: `C849_Deploy_prepares_and_verifies_before_acceptance` expected exit 0 at
`tests/Antiphon.Tests/Scripts/RemoteScriptContractTests.cs:7148`, received 2.
GNU timeout executes Docker by name, bypassing a shell `docker()` function, and
the unshimmed child reached the worker daemon, which refused the synthetic image
with status 125. The fixture repair in the same c80f commit installs an executable
fake Docker on the child-only PATH; it retains real GNU deadlines. No production
retirement defect caused this assertion. This task will qualify the complete row.

Timeout logs also disclose J1's wrong refusal diagnostic (fixed in prior
`0acf5373c08783d2bbbbda2a7d7dcc66846ffb4b`), CP-9's same Docker escape (fixed c80f),
and CP-8's host-proof fixture failing with `host-ls-remote status=2` (investigating).
CP-3's floor remains 25; only 22 methods exist. J7/J8/J10 and N1-N3 stay out of
this Debug scope. Missing methods cannot be hidden by lowering the floor.

## Investigation and bounded continuation

This commissioned Debug record satisfies the before-repeat requirement for the
historical >30-minute group. Only CP-3/4/6/8/9/16 will be rerun as one checkpoint
aggregate. No green row, full assembly or Windows/native-Docker qualification is
selected. The forthcoming measurements and results will be appended here.

Production deletes remain disabled: the first rollout remains preview-only.

## Fixture repair and setup lifetime

CP-8's fake Git printed a successful advertisement for every command but never
created a real origin graph or bundle. The current host writer requires that
bundle before publishing its ref proof. Repaired the existing redaction method
to route its clean approved fixture origin to an isolated real bare repository;
real init/fetch/bundle operations now run, with synthetic credential noise still
injected into ls-remote stdout/stderr. All artifact/log/argv sentinel assertions
remain, and the test additionally inspects the real bundle's exact head.

`C1008ComposeSeed` uses TUnit's PerTestSession shared-data container. It renders
the two current and two previous Compose models once in owned scratch, retains
only immutable strings, then removes that scratch. Each host fixture receives
fresh JSON nodes and inert file copies with its own paths; mutable fault,
status, census, volume contents, journals and proofs are never shared. The
existing fixture-custody method now checks path rebinding, model isolation and
file isolation. This removes four Node/Compose launches per fault vector after
the first vector. Compose config is read-only; no daemon/image/DB is started.

Added opt-in setup, teardown and CLI phase spans at the shared fixture boundaries.
Deleted the legacy timeout-function body so the receipt/content probes also
retain real GNU timeout through the already-installed executable fake Docker.
No product assertion or test was removed or weakened.

## Six-method measurement probe

Source `a9ed39fa09e306cf538c5244ac70d9c168daeea7`; leased isolated build
246.04 s (zero errors), slot wait 45 s. Leased six-method probe: 5 pass, 1 fail,
0 skip, test wall 318.774 s, slot held 322 s, no admission wait. This extra
selection is justified to measure the slow matrices and qualify the shared setup
before the single six-row checkpoint aggregate. It is not whole-row acceptance.
TRX: `.antiphon/c1609-debug-probe/run.trx`; opt-in phase journals:
`.antiphon/c1609-debug-probe-phases/`. Every phase/test association is joined by
the native timing identity in TRX stdout; nested spans are unioned, not added.

| Method | Result | TRX s | Setup union s | Teardown union s | CLI lifecycle union s | Unattributed remainder s |
|---|---|---:|---:|---:|---:|---:|
| C994_Fixture_custody_is_bounded | pass | 0.908 | 0.717 | 0.005 | 0.825 | 0.052 |
| Current_tooling_and_missing_history_do_not_block | pass | 37.826 | 0.022 | 0.027 | 37.674 | 0.110 |
| C994_Container_service_and_state_are_restricted | pass | 43.031 | 0.164 | 0.223 | 42.286 | 0.359 |
| C1008_Host_proof_admits_only_an_authenticated_private_origin | fail | 152.957 | 0.003 | 0.011 | 152.623 | 0.320 |
| Retained_volumes_block_deploy_and_completion | pass | 81.901 | 0.007 | 0.003 | 81.686 | 0.206 |
| C1008_Host_proof_file_omits_credentials | pass | 1.107 | 0 | 0 | 0.039 | 1.068 |

These are single-method samples (p50=p95=max within each one-element sample),
not population p95 estimates. Setup and CLI columns overlap when Compose starts
inside setup. The first method includes all four real renderings; later fixtures
perform none. Per-command HTTP/IO/Git work *inside* Bash remains unattributed
within its measured CLI lifetime; no DB/AppHost/container startup is applicable.
The redaction method's uninstrumented direct PowerShell child stays in remainder.

The probe revealed another CP-8 fixture-ordering defect at
`RollingVolumeRecycleScriptTests.cs:2823`: after its negative vectors write
Refused and its positive public-origin control changes the inventory, the test
attempts apply without a fresh Ready preview. Product correctly refuses
`RecycleGitAuditUnknown reason=preview-required`, with zero removals. Repair:
restore the private origin/proof, execute the real Ready preview, then apply.
No receipt is manufactured, no assertion weakened, and no product gate changed.

The 120 s CP-8 cap cannot contain its 152.957 s method alone. CP-4's 81.901 s W6
matrix also leaves only 38 s for eleven other methods. CP-3's old 600 s and
CP-9's old 360 s are censored lower bounds; sharing setup removes repeated work,
but cannot prove an unknown full-row p95. The next aggregate uses bounded
**provisional collection** caps CP-3=20m, CP-4=6m, CP-8=10m, CP-9=20m under this
commissioned CARD-1609 Debug. These correct impossible collection bounds using
the retained censored rows and measured slow methods; they are explicitly not
measured 2x-p95 claims. The completed-row measurements will determine the final
rounded 2x recommendations. Collect at most five samples through ordinary later
S5/Review work; do not repeat green rows merely to fill the sample.

## Assigned-base attribution replay

Replayed only the two CP-8 methods in an isolated detached scratch checkout at
the exact assigned base `0080d76f8021ebdf6f73650f3c1b2b3d9816dc2d`:
**2 executed, 0 passed, 2 failed, 0 skipped**, test wall 122.134 s. The redaction
method fails at baseline `RollingVolumeRecycleScriptTests.cs:2889` with
`host-ls-remote status=2` (0.992 s); the private-origin matrix fails at baseline
`:2823` with `reason=preview-required` and zero removals (120.315 s).
Both additional CP-8 defects are therefore inherited, not caused by setup sharing.
Baseline build: 365.95 s, zero errors, 45 s admission wait; test slot wait 0 s,
held 125 s. Evidence: `.antiphon/c1609-debug-base/run.trx`, matching `.log` and
`c1609-debug-base-build.log`. No native Windows/real-Docker test ran.

The real land-graph `-Check` exited 0 after a 60 s slot wait and 49 s held:
digest `20DE49F6B177E5A238F20D4F8C7F33EAEE6B511A9163CDB902D9136EF06DECB6`,
count 66, approved. No definition file changed and `-Update` was not run.
The evidence-history guard passed the first two commits with zero violations.
OS-witness inventory lint passed the five changed test/fixture paths, including
the unchanged Linux selector; no production OS branch or restart requirement
changed. The first lint call supplied a nonexistent input declaration and exited
2; the corrected declaration run exited 0. Neither static lint grants native credit.

## Complete six-row aggregate and follow-up repairs

Run `20261030-144342-f5c1` at `a51c4a4bf3114c0588fb5f4c81b5c3f109a419c7`
joined with exit 1: **221 executed, 218 passed, 3 failed, 0 skipped, 0 timeouts**.
CP-4/6/16 are green; CP-3/8/9 have one assertion failure each. All original filters
and floors are unchanged. CP-3 also still lacks J7/J8/J10 (22 methods, floor 25).
Source at both boundaries is clean, fingerprint
`6f066b18d3d1b1ac89516c799f547279a9d6b08fbb1ae31e9b8db7c3eca539aa`,
buildSource=verified. Compiler 385.49 s; driver build 482.837 s; build execution
490.111 s / wall 509.770 s, no slot wait. The extra 97.347 s over compilation
includes confinement/provenance/output IO; finer attribution is unknown.
Run wall 3070.570 s (51m11s); charged 2815.322 s; admission-only 255.156 s.
This was the single requested aggregate. Its historical RED verdict is retained.

Additional defects found behind the former timeout rows:

* **Product refusal formatter:** H7 expects `deniedPreview.Exit == 2` but receives
  `UnhandledExit 1` and `printf: write error: Broken pipe` at generated remote
  line 9248. Three fixture injection blocks map that to production
  `scripts/c590-remote.sh:9131`. `sed` quits after its first matching diagnostic,
  closing the pipe while printf can still be writing. With pipefail this escapes
  before the typed refusal. It fails closed; it cannot issue Ready or authorize a
  delete. The same early-quitting consumer occurs at :8796 and :10102.
* **CP-8 transport pin:** apply succeeds, then `created.Length > 0` fails at
  `RollingVolumeRecycleScriptTests.cs:2833`. Temp now uses the read-only `run`
  helper; `create/start` is the preserved main adapter. The repaired assertion
  requires an actual apply `run` plus the unchanged proof bind/operation/time
  checks, and adds rm/init/cap-drop/UID/v2 assertions.
* **CP-9 transport and content fixture:**
  `RetiredTempContainerHostTests.cs:483` expects a `create` call after successful
  published apply. It also seeds `Root/work/repo` while the read-only helper reads
  the actual volume mount, and lacks Ready before its dirty/unpublished controls.
  Repair aliases that fixture work path to the actual target mount, establishes
  Ready while clean, then mutates real target bytes. Only post-preview apply
  audits count; exact image/UID/readonly and retained-content assertions remain.

Published witness/fixture commit `e52c3b8894dba8876301e8f0e4e21193a1803d9b`
adds deterministic 1 MiB failed-audit streams to the existing H7 method (no new
TUnit method or floor credit). Before the product fix, replay at assigned-base
production with only that H7 test-file overlay gives **2 executed, 0 passed,
2 failed, 0 skipped**: H7 fails the intended typed-refusal assertion with broken
pipe (8.664 s); unchanged baseline C994 image-audit method fails the old create
pin. Test wall 111.891 s, build 152.19 s / zero errors; all drivers leased.
The production scripts were byte-identical to assigned base. Test overlay was
restored after joining. Evidence: `.antiphon/c1609-h7-c994-base-red/run.trx` and
matching logs. These failures are verified against baseline production, not
attributed to sharing setup.

Minimal product fix replaces all three early-quitting diagnostic pipelines with
two fully draining sed stages that still return only the first matching line.
No authorizing predicate, classifier, journal rule or delete path is weakened.
The next run selects only H7 and the two repaired transport/content methods;
ordinary full-row requalification remains part of unfinished S5.


## Aggregate timing ledger

All seconds below come from terminal report.json/TRX, not live heartbeat estimates.
Row wall includes row admission; the producing build is separate and charged once.

| Row | Executed/pass/fail/skip | Status | Admission s | Execution s | Wall s | Collection cap min | >=2x one observed row? | 2x observed proposal min |
|---|---|---|---:|---:|---:|---:|---|---:|
| CP-3 | 22/21/1/0 | red | 30 | 615.823 | 645.883 | 20 | no | 21 |
| CP-4 | 12/12/0/0 | green | 0 | 138.372 | 138.470 | 6 | yes | 5 |
| CP-6 | 132/132/0/0 | green | 30 | 144.408 | 174.728 | 6 | yes | 5 |
| CP-8 | 10/9/1/0 | red | 45 | 481.703 | 526.962 | 10 | no | 17 |
| CP-9 | 22/21/1/0 | red | 45 | 782.462 | 827.514 | 20 | no | 27 |
| CP-16 | 23/23/0/0 | green | 105 | 138.639 | 243.782 | 6 | yes | 5 |

The final column is a **provisional single-row proxy**, not a population p95.
CP-3 is the present 22-method roster, not the required 25; H7 and the C994 fixture
are repaired afterward, so their new durations must also be accounted for.
Collect at most five comparable normal S5/Review samples; do not repeat green
selections just to manufacture a p95. Initial censored rows remain unknown.

| Row | Startup s | Tests wall s | Host teardown s | Host wall s | Case p50 s | Case p95 s | Case max s | Measured fixture-ctor union sum s | Fixture teardown union sum s |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| CP-3 | 3.811 | 610.786 | 0.594 | 615.192 | 17.403 | 59.139 | 130.877 | 0.969 | 0.362 |
| CP-4 | 4.476 | 132.463 | 0.728 | 137.668 | 3.423 | 76.755 | 76.755 | 0.082 | 0.011 |
| CP-6 | 3.672 | 137.420 | 1.087 | 142.179 | 0.715 | 2.609 | 3.036 | 0.000 | 0.000 |
| CP-8 | 3.668 | 475.169 | 2.242 | 481.080 | 14.323 | 205.032 | 205.032 | 0.863 | 0.141 |
| CP-9 | 3.054 | 778.337 | 0.529 | 781.919 | 15.656 | 96.404 | 112.015 | 1.765 | 3.187 |
| CP-16 | 4.592 | 132.788 | 0.548 | 137.929 | 3.073 | 21.186 | 79.872 | 1.311 | 0.026 |

Case quantiles use nearest rank across that row's argument-expanded results.
They are not per-method population p95. A method observed once has sample
p50=p95=max equal to its recorded duration; its population p95 remains unknown.
Native timing identities in TRX stdout join all 221 cases to the phase journals.
Fixture-ctor columns measure only the instrumented C1008 seed/copy boundaries;
other Git/data preparation remains inside CLI or unattributed remainder. An
absent fixture span is unknown total setup, not proof of zero setup. Nested setup
and CLI spans overlap; the columns must not be added into row wall. CP-16 has
parallel string guards, so summed case durations also exceed its tests wall.
No AppHost, DB/migration, real Docker-daemon or provider startup was selected.
IO/Git/HTTP work inside a Bash child remains unknown within its measured CLI
lifecycle. Fresh helper namespaces and mutable fault state are the test subject;
sharing them across preview/apply would invalidate the observations. Only immutable
Compose renderings were shared. No overall speedup is claimed from incomparable,
censored selections.

### Every method in the four formerly timed-out rows

Each row is one TRX method result. Phase values are seconds; CLI is the union of
recorded child lifecycles, including waits/output drain. Remainder is unattributed,
not idle time. Zero instrumented spans do not establish zero total setup/teardown.

| CP | Method | Result | Observed s | Ctor setup s | Teardown s | CLI union s | Remainder s |
|---|---|---|---:|---:|---:|---:|---:|
| CP-3 | All_target_roles_are_classified | Passed | 17.403 | 0.790 | 0.042 | 17.247 | 0.087 |
| CP-3 | State_credentials_refuse_before_removal | Passed | 8.692 | 0.007 | 0.005 | 8.648 | 0.033 |
| CP-3 | Volume_incarnation_and_operation_are_bound | Passed | 22.574 | 0.010 | 0.012 | 22.510 | 0.042 |
| CP-3 | Read_failures_and_incomplete_inventory_refuse | Passed | 5.550 | 0.006 | 0.010 | 5.525 | 0.009 |
| CP-3 | Preview_receipt_is_required_and_consumed | Passed | 5.693 | 0.006 | 0.003 | 5.675 | 0.010 |
| CP-3 | No_content_or_credentials_are_executed_or_exported | Passed | 8.250 | 0.004 | 0.005 | 8.234 | 0.007 |
| CP-3 | Receipt_shape_custody_and_digest_are_strict | Passed | 16.600 | 0.003 | 0.006 | 16.367 | 0.224 |
| CP-3 | Audit_identity_and_namespace_are_stable | Passed | 9.514 | 0.006 | 0.017 | 9.476 | 0.015 |
| CP-3 | Evidence_names_and_filesystem_shapes_fail_closed | Passed | 17.973 | 0.009 | 0.010 | 17.933 | 0.021 |
| CP-3 | Changed_content_and_remote_proof_refuse | Passed | 130.877 | 0.021 | 0.038 | 130.772 | 0.046 |
| CP-3 | Gone_tracking_retains_original_commit_protection | Passed | 38.711 | 0.007 | 0.008 | 38.667 | 0.029 |
| CP-3 | Fetch_head_requires_proven_objects_not_fresh_names | Passed | 28.912 | 0.004 | 0.014 | 28.860 | 0.035 |
| CP-3 | Preview_lists_every_offender_without_effects | Passed | 11.791 | 0.007 | 0.015 | 11.757 | 0.012 |
| CP-3 | Every_ref_recovery_tip_and_tag_object_requires_publication | Passed | 46.626 | 0.008 | 0.036 | 46.283 | 0.298 |
| CP-3 | Only_exact_known_seed_fixtures_are_disposable | Passed | 53.795 | 0.007 | 0.011 | 53.739 | 0.038 |
| CP-3 | Freshness_and_policy_version_are_rechecked | Passed | 17.330 | 0.004 | 0.013 | 17.290 | 0.023 |
| CP-3 | Absent_work_does_not_skip_other_volumes | Passed | 12.855 | 0.017 | 0.010 | 12.714 | 0.114 |
| CP-3 | Private_origin_advanced_head_uses_real_host_proof | Failed | 30.447 | 0.005 | 0.032 | 28.231 | 2.179 |
| CP-3 | Current_tooling_and_missing_history_do_not_block | Passed | 59.139 | 0.025 | 0.034 | 59.036 | 0.044 |
| CP-3 | Worktree_content_and_admin_closure_are_complete | Passed | 19.670 | 0.009 | 0.011 | 19.629 | 0.022 |
| CP-3 | Every_delete_rechecks_full_live_guards | Passed | 38.525 | 0.006 | 0.027 | 38.472 | 0.020 |
| CP-3 | Legacy_receipts_are_history_only | Passed | 9.708 | 0.008 | 0.003 | 9.687 | 0.010 |
| CP-4 | Proof_coverage_binding_and_freshness_are_exact | Passed | 3.423 | unknown | unknown | unknown | 3.466 |
| CP-4 | Main_recycle_and_existing_deploy_contracts_are_preserved | Passed | 9.382 | 0.033 | 0.002 | 7.996 | 1.352 |
| CP-4 | Manual_parameters_require_operator_attestation | Passed | 6.575 | unknown | unknown | unknown | 6.575 |
| CP-4 | Unknown_remote_and_object_failures_refuse | Passed | 0.854 | unknown | unknown | unknown | 0.854 |
| CP-4 | Retire_temp_ignores_missing_stack_and_historical_compose | Passed | 4.347 | unknown | 0.001 | 4.338 | 0.009 |
| CP-4 | Retire_temp_uses_current_proof_protocol | Passed | 18.866 | 0.002 | 0.002 | 18.855 | 0.007 |
| CP-4 | Host_proves_complete_origin_ancestry | Passed | 1.865 | unknown | unknown | unknown | 1.865 |
| CP-4 | Retained_volumes_block_deploy_and_completion | Passed | 76.755 | 0.047 | 0.006 | 76.408 | 0.294 |
| CP-4 | Bridge_validates_and_preserves_receipt_fields | Passed | 2.037 | unknown | unknown | unknown | 2.038 |
| CP-4 | Private_and_public_origins_share_offline_gate | Passed | 5.587 | unknown | unknown | unknown | 5.588 |
| CP-4 | Child_deadline_and_output_redaction_are_bounded | Passed | 1.722 | unknown | unknown | unknown | 1.722 |
| CP-4 | Origins_and_git_configuration_cannot_redirect_credentials | Passed | 0.942 | unknown | unknown | unknown | 0.942 |
| CP-8 | C1008_Host_proof_admits_only_an_authenticated_private_origin | Failed | 205.032 | 0.805 | 0.019 | 204.349 | 0.670 |
| CP-8 | C1008_Same_sha_and_partial_retries_are_safe | Passed | 22.666 | 0.003 | 0.001 | 22.638 | 0.025 |
| CP-8 | C1008_Wrapper_option_manifest_is_strict | Passed | 14.323 | 0.001 | 0.035 | 13.036 | 1.251 |
| CP-8 | C1008_Present_or_unknown_temp_keeps_null_refusal | Passed | 58.871 | 0.007 | 0.032 | 58.795 | 0.037 |
| CP-8 | C1008_Preview_helper_removal_is_owned | Passed | 36.631 | 0.006 | 0.031 | 36.537 | 0.058 |
| CP-8 | C1008_Host_proof_file_omits_credentials | Passed | 2.656 | unknown | unknown | 0.294 | 2.362 |
| CP-8 | C1008_Option_manifest_is_strict | Passed | 0.129 | 0.005 | 0.003 | 0.112 | 0.009 |
| CP-8 | C1008_Busy_routed_and_land_in_flight_refuse | Passed | 127.953 | 0.032 | 0.017 | 127.837 | 0.067 |
| CP-8 | C1008_Documentation_and_transport_pins_match | Passed | 0.062 | 0.003 | 0.002 | 0.053 | 0.004 |
| CP-8 | C1008_Wrapper_retired_absent_null_is_accepted | Passed | 6.748 | 0.001 | 0.001 | 6.743 | 0.003 |
| CP-9 | C994_Host_preview_never_mutates | Passed | 13.648 | 0.875 | 0.565 | 12.602 | 0.445 |
| CP-9 | C994_Receipt_write_and_copy_fail_closed | Passed | 12.565 | 0.019 | 0.019 | 12.497 | 0.031 |
| CP-9 | C994_Production_mount_topology_is_proven | Passed | 42.955 | 0.051 | 0.045 | 42.709 | 0.151 |
| CP-9 | C994_Final_absence_is_observed | Passed | 27.150 | 0.028 | 0.615 | 26.483 | 0.024 |
| CP-9 | C994_Host_context_is_strict | Passed | 0.458 | 0.019 | 0.017 | 0.392 | 0.030 |
| CP-9 | C994_Host_task_and_land_census_is_complete | Passed | 50.861 | 0.098 | 0.041 | 50.646 | 0.076 |
| CP-9 | C994_Whole_candidate_set_precedes_removal | Passed | 6.173 | 0.012 | 0.010 | 6.138 | 0.014 |
| CP-9 | C994_Removal_failure_and_crash_retry_preserve_progress | Passed | 96.404 | 0.038 | 0.471 | 95.778 | 0.117 |
| CP-9 | C994_Host_main_admission_is_required | Passed | 7.362 | 0.108 | 0.004 | 7.168 | 0.082 |
| CP-9 | C994_Census_and_inspect_fail_closed | Passed | 15.656 | 0.079 | 0.183 | 15.118 | 0.276 |
| CP-9 | C994_Exited_owned_containers_are_removed_without_volumes | Passed | 47.289 | 0.025 | 0.017 | 47.204 | 0.044 |
| CP-9 | C994_Older_retirement_receipt_does_not_block_cleanup | Passed | 7.243 | 0.004 | 0.005 | 7.221 | 0.013 |
| CP-9 | C994_Audit_image_is_pinned_and_required | Failed | 112.015 | 0.019 | 0.033 | 111.827 | 0.136 |
| CP-9 | C994_Cleanup_receipts_redact_unapproved_fields | Passed | 7.266 | 0.015 | 0.180 | 6.744 | 0.328 |
| CP-9 | C994_Container_service_and_state_are_restricted | Passed | 73.272 | 0.208 | 0.545 | 72.113 | 0.407 |
| CP-9 | C994_Host_retirement_proof_is_typed | Passed | 16.024 | 0.003 | 0.003 | 16.010 | 0.008 |
| CP-9 | C994_Rollout_lock_covers_final_census | Passed | 7.234 | 0.010 | 0.008 | 7.207 | 0.010 |
| CP-9 | C994_Fixture_custody_is_bounded | Passed | 0.175 | 0.008 | 0.005 | 0.159 | 0.003 |
| CP-9 | C1105_Temp_cleanup_uses_previous_generation | Passed | 8.284 | 0.008 | 0.009 | 8.254 | 0.013 |
| CP-9 | C994_Each_removal_rechecks_live_proofs | Passed | 87.428 | 0.103 | 0.385 | 86.404 | 0.536 |
| CP-9 | C994_Absent_cleanup_is_idempotent | Passed | 43.000 | 0.021 | 0.019 | 42.900 | 0.076 |
| CP-9 | C1609_Cleanup_does_not_require_historical_image | Passed | 95.739 | 0.014 | 0.008 | 95.636 | 0.081 |

The slow unchanged witnesses have small comparable samples: C1 37.826/59.139 s
(n=2, sample p50 37.826 / p95=max 59.139); W6 81.901/76.755 s
(n=2, p50 76.755 / p95=max 81.901). These remain provisional. Failed partial H7,
the private-origin matrix before its fresh-preview repair, and the C994 method
before its real-volume repair are different paths and are not pooled as a
normal-green p95. Existing 120-second planning caps are disproved by J5
(130.877 s) and the private-origin matrix (205.032 s); no test attribute was
loosened here. Further normal samples and final S5 witnesses own method budgets.

### Unedited checkpoint provenance

```text
CHECKPOINT CP-3 commit=a51c4a4bf3114c0588fb5f4c81b5c3f109a419c7 build=ok filter=/*/Antiphon.Tests.Scripts/(VolumeRetirementContentTests*)|(VolumeRetirementReceiptTests*)/* executed=22 passed=21 failed=1 skipped=0 trx=/work/worktrees/task-a2935adb/.antiphon/checkpoints/20261030-144342-f5c1/rows/CP-3/run.trx slot=granted waited=30s dirty=0 source=a51c4a4bf3114c0588fb5f4c81b5c3f109a419c7 sourceState=clean buildSource=verified
CHECKPOINT CP-4 commit=a51c4a4bf3114c0588fb5f4c81b5c3f109a419c7 build=reused filter=/*/Antiphon.Tests.Scripts/(VolumeRetirementHostProofTests*)|(VolumeRetirementWrapperTests*)/* executed=12 passed=12 failed=0 skipped=0 trx=/work/worktrees/task-a2935adb/.antiphon/checkpoints/20261030-144342-f5c1/rows/CP-4/run.trx slot=granted waited=0s dirty=0 source=a51c4a4bf3114c0588fb5f4c81b5c3f109a419c7 sourceState=clean buildSource=verified
CHECKPOINT CP-6 commit=a51c4a4bf3114c0588fb5f4c81b5c3f109a419c7 build=reused filter=/*/Antiphon.Tests.Scripts/RemoteScriptContractTests/C1105_Git_audit_hidden_locations executed=132 passed=132 failed=0 skipped=0 trx=/work/worktrees/task-a2935adb/.antiphon/checkpoints/20261030-144342-f5c1/rows/CP-6/run.trx slot=granted waited=30s dirty=0 source=a51c4a4bf3114c0588fb5f4c81b5c3f109a419c7 sourceState=clean buildSource=verified
CHECKPOINT CP-8 commit=a51c4a4bf3114c0588fb5f4c81b5c3f109a419c7 build=reused filter=/*/Antiphon.Tests.Scripts/RollingVolumeRecycleScriptTests/(C1008_Same_sha_and_partial_retries_are_safe*)|(C1008_Option_manifest_is_strict*)|(C1008_Wrapper_option_manifest_is_strict*)|(C1008_Present_or_unknown_temp_keeps_null_refusal*)|(C1008_Busy_routed_and_land_in_flight_refuse*)|(C1008_Preview_helper_removal_is_owned*)|(C1008_Wrapper_retired_absent_null_is_accepted*)|(C1008_Host_proof_admits_only_an_authenticated_private_origin*)|(C1008_Host_proof_file_omits_credentials*)|(C1008_Documentation_and_transport_pins_match*) executed=10 passed=9 failed=1 skipped=0 trx=/work/worktrees/task-a2935adb/.antiphon/checkpoints/20261030-144342-f5c1/rows/CP-8/run.trx slot=granted waited=45s dirty=0 source=a51c4a4bf3114c0588fb5f4c81b5c3f109a419c7 sourceState=clean buildSource=verified
CHECKPOINT CP-9 commit=a51c4a4bf3114c0588fb5f4c81b5c3f109a419c7 build=reused filter=/*/Antiphon.Tests.Scripts/RetiredTempContainerHostTests/* executed=22 passed=21 failed=1 skipped=0 trx=/work/worktrees/task-a2935adb/.antiphon/checkpoints/20261030-144342-f5c1/rows/CP-9/run.trx slot=granted waited=45s dirty=0 source=a51c4a4bf3114c0588fb5f4c81b5c3f109a419c7 sourceState=clean buildSource=verified
CHECKPOINT CP-16 commit=a51c4a4bf3114c0588fb5f4c81b5c3f109a419c7 build=reused filter=/*/Antiphon.Tests.Scripts/RemoteScriptContractTests/(C0995_Deploy_temp_checks_host_absence_before_checkout*)|(C849_Deploy_temp_names_saved_import_when_seed_is_missing*)|(C849_Deploy_prepares_and_verifies_before_acceptance*)|(C849_Deploy_temp_observes_inaccessible_docker_mountpoints_with_sudo*)|(C849_Docker_volume_paths_have_no_bare_host_reads*)|(C849_Seed_publishes_complete_payloads_before_its_marker*)|(Temp_deploy_uses_its_own_project_and_the_inspected_grok_store*)|(Nested_lane_never_uses_sudo_or_python*)|(C1067_Nested_lane_sudo_guard_is_sensitive*)|(Deploy_parent_creates_the_github_token_directory_without_reading_it*)|(C849_Saved_donor_archive_is_checked_and_imported_without_a_container*)|(C849_Deploy_ordering_contract_is_pinned*)|(C912_Cold_runner_verification_uses_mounts_and_writability_not_payloads*)|(C912_Cold_exception_never_weakens_maintenance_or_donor_gates*)|(C973_Cold_marker_readers_accept_pruned_seed_image_and_refuse_invalid_markers*) executed=23 passed=23 failed=0 skipped=0 trx=/work/worktrees/task-a2935adb/.antiphon/checkpoints/20261030-144342-f5c1/rows/CP-16/run.trx slot=granted waited=105s dirty=0 source=a51c4a4bf3114c0588fb5f4c81b5c3f109a419c7 sourceState=clean buildSource=verified
```

Evidence root: `/work/worktrees/task-a2935adb/.antiphon/checkpoints/20261030-144342-f5c1/`.
Phase journals: `/work/worktrees/task-a2935adb/.antiphon/c1609-debug-final-phases/`.
Raw report and TRX retain argument identities, failures, exact filters and host/load.

## Final repair verification and handoff

Final exact repair selection: **3 executed / 3 passed / 0 failed / 0 skipped**, exit 0, source `e6f7cb25a46c0eafa3d07607de7af8d5130bbb0d`, dirty=0, sourceState=clean, buildSource=verified. Test wall 461.344 s; compiler 402.56 s. Per-test observations:

- `C994_Audit_image_is_pinned_and_required`: 223.455 s, Passed.
- `Private_origin_advanced_head_uses_real_host_proof`: 46.920 s, Passed.
- `C1008_Host_proof_admits_only_an_authenticated_private_origin`: 189.572 s, Passed.

No aggregate outcome is relabeled: CP-3/8/9 remain historically RED at a51c4a4; the exact failing methods are now green at e6f7cb2. CP-3 still lacks three of its required 25 methods. Full-row qualification at the repaired tip belongs to S5.

```text
CHECKPOINT DEBUG-H7-TRANSPORT commit=e6f7cb25a46c0eafa3d07607de7af8d5130bbb0d build=ok filter=/*/Antiphon.Tests.Scripts/(VolumeRetirementContentTests*)|(RollingVolumeRecycleScriptTests*)|(RetiredTempContainerHostTests*)/(Private_origin_advanced_head_uses_real_host_proof*)|(C1008_Host_proof_admits_only_an_authenticated_private_origin*)|(C994_Audit_image_is_pinned_and_required*) executed=3 passed=3 failed=0 skipped=0 trx=/work/worktrees/task-a2935adb/.antiphon/c1609-debug-repair/DEBUG-H7-TRANSPORT-20261030-154427-e23c/run.trx slot=granted waited=0s dirty=0 source=e6f7cb25a46c0eafa3d07607de7af8d5130bbb0d sourceState=clean buildSource=verified
```

Final receipt: `/work/worktrees/task-a2935adb/.antiphon/c1609-debug-repair/DEBUG-H7-TRANSPORT-20261030-154427-e23c/report.json`. The direct checkpoint-tool row is an additional selection justified solely by the three newly exposed failures; it is not a second aggregate or a repeat of green rows.

Updated provisional row caps (minutes): {"CP-3":22,"CP-4":5,"CP-8":17,"CP-9":30}. Updated cost estimates: {"CP-3":19,"CP-4":3,"CP-8":9,"CP-9":15}. For CP-3 and CP-9, replace only the old failed-method duration with the new longer green observation when estimating cost; do not reduce a row on a faster sample. This yields a bounded measurement-derived proposal, not an independently executed full-row p95. Keep up to five comparable samples from normal S5/Review work. The complete group remains expensive: split future qualification rather than replaying the 51-minute aggregate.

The existing v1 Rolling PreviewWitnessProbe models timeout; it cannot qualify real helper cancellation. Receipt/content probes retain GNU timeout. N1 must supply the real helper namespace/custody/deadline evidence.

Still owed: J7 supported-writer lock ordering; J8 durable intents, partial resume and Close; J10 manual attestation; N1-N3 real Docker kernel/namespace, replacement/reference and crash/deletion safeguards; all remaining S5 matrices, native Windows CP-11/12/13, independent Review and the first production preview. The fresh-Ready and actual-volume fixture findings particularly affect J8/J10 and N1/N3. Current-tool/historical-topology decoupling and v2 publication compatibility closure listed in the predecessor report remain owed. No missing method or native check is credited. No production audit/apply/delete, activation, rc change or tag occurred; rollout remains preview-only by policy.

No definition file changed. Real land graph -Check passed with digest `20DE49F6B177E5A238F20D4F8C7F33EAEE6B511A9163CDB902D9136EF06DECB6`, 66 definitions; -Update was not run. Final code OS-witness inventory passed; no OS dispatch or startup change. Evidence guard over six code/report commits passed with zero violations; it is rerun over the report publication too.

## Published commits

- `efef66d318b79ec8a485a79b1deaa53123ee9db6` docs: record CARD-1609 S5 checkpoint failure diagnosis
- `a9ed39fa09e306cf538c5244ac70d9c168daeea7` test: repair host proof fixture and share immutable Compose setup
- `73592e19ffadaef68802181e41fe79ae6f0f4fb8` test: refresh Ready preview after private-origin refusal vectors
- `a51c4a4bf3114c0588fb5f4c81b5c3f109a419c7` docs: record assigned-base reproductions and Debug measurements
- `e52c3b8894dba8876301e8f0e4e21193a1803d9b` test: pin large audit refusals and actual temp helper transport
- `e6f7cb25a46c0eafa3d07607de7af8d5130bbb0d` fix: drain failed audit output before returning typed refusals

Final plan re-import succeeded (16 rows). Its CP-3 57-minute derived-default warning and CP-11 duplicate-build warning are advisory; the generated next manifest is explicitly pinned to the documented caps, never that default. After every owned process joined, all 72 recorded task-owned alternate output roots were removed by exact literal path. TRX/JSON/log receipts remain ignored and retained. No production volume or state directory was removed.

--- next stage ---
next: code
handoff: Finish S5 J7/J8/J10 and N1-N3 from this pushed tip; retain all count floors and requalify the affected rows.
artifact: /work/worktrees/task-a2935adb/docs/investigations/2026-10-30-card-1609-s5-checkpoint-debug.md

## Continuation cdf3cde4: Close and updated collection budget (2026-10-31)

The new public Close witness preserves the original J8 crash/resume matrix. At
`1b71ab92db8e6159dbb9b50bbe14c255319af05b`, CLOSE-RED executed 1/0/1/0:
the actual public command reported Closed while its container census failed.
Its method took 342.433 s; compiler 383.28 s. This is assertion red, not a
fixture/build failure. The Close script was unchanged from the assigned base.

At `eaffae9ce630db7d7e7cb46bf96fce485bcadf6f`, CLOSE-GREEN executed 3/2/1/0.
J8 passed in 474.235 s, including a 71.400 s public-command matrix, 18 refusal
vectors, actual FD-8 contention, failed-flush recovery, idempotency and older
operation/delegated closure. The literal-transport method passed in 2.038 s.
The portable authority method failed in 6.678 s because its nested PowerShell
`-Command` did not forward `$LASTEXITCODE`; the typed refusal was correct. The
fixture now forwards that exit, retaining the exact assertion. The historical
3/2 result remains red. All runs used granted host slots and clean verified builds.

The new Close driver has a provisional 120 s collection bound, separate from
existing 30 s crash/resume helper bounds. Its first complete measurement was
71.400 s; individual public-call and Docker CLI timings are retained in
`.antiphon/cdf-close-green-phases/close-calls-31048.json` (188 CLI calls). Normal
successful Close calls took 6.007/6.084 s. These small samples do not establish
population p95. Retain up to five normal S5/Review samples under CARD-1609 and
CARD-1404/1514; do not replay green rows solely to collect samples.

The strengthened J10 normal method measured 115.490 s versus the predecessor's
85.875 s. The J8 increment above is 131.802 s; together the measured increments
are 161.417 s. For the next complete 25-method CP-3 collection, increase its
provisional cap from 22 to **28 minutes**: 22 plus the rounded-up 2x incremental
cost (6 minutes). The predecessor's complete CP-3 run charged about 1180.845 s
outside its producing build, giving an incremental estimate near 1342 s before
unmeasured common lifecycle overhead. Full current-row p95 remains unknown.
Keep the original 25 floor/filter, a separate producing-build measurement, and
a 30-minute charged group cap. Other Debug row caps remain unchanged. This is
bounded collection justified by new measured work, not an inherited timeout
relabeled green or a claim of 2x population p95.

N1 alone passed 1/1/0/0 at `38867c76ee570e435b16a89c30929a61ca6039cd`:
setup 27.052 s, native body 56.909 s, method 83.994 s, test-host wall 86.204 s.
The native session recorded owned cleanup absent. CP-10 retains its 8-minute
cap; N1 does not justify raising it. Requalification remains separate from these
focused measurements. Public manual Preview/Apply and strict v2 publication/
transport acceptance remain incomplete; production deletes remain disabled.
