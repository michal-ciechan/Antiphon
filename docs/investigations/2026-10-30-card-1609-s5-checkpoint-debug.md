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
