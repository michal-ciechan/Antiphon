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
