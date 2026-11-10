# CARD-1458 Final Review round 1

Clean. No blocker. Land the Code owner.

Subject task: 9f95ca26-5aa8-4d99-b486-21e253674f5f (Code, Succeeded).
Reviewed tip: e9e2f312820c7e82137a1ee48138997511feb5a7 on feat/card-task-9f95ca26.
Range: ac6d00022e940ee3b90584c7d313567b4491c498..e9e2f312820c7e82137a1ee48138997511feb5a7.
Review task: 1de70bea-4e0e-42af-b62e-98564922c29a.

D-1 and D-3 match the plan. A pool string `{"password":"SENT","n":1}` is masked to `{"password":"<redacted>","n":1}` before it is serialized, and the baseline and result JSON from a Baseline run do not contain the sentinel. `(Password=S)` and `(Password=SENTOM1458) medianMs=5000` keep the closer and the tail. `Password=SENT)TAIL1458` is still `Password=<redacted>`.

## Verification

- CP-1 checkpoint run 20261110-132517-49a8. commit e9e2f312820c7e82137a1ee48138997511feb5a7, sourceState=clean, buildSource=notApplicable, slot=granted, dirty=0, exit 0. Console: passed=308 failed=0, EXIT CODE 0. CHECKPOINT-TIMING executed=119.848s wall=119.921s. Plan estimate 3 minutes. unlisted: none.
- Red witnesses re-run from commit 4a77721a1431b80c86ac692dc3544ea49c102372 in a temp copy, not the worktree. Nested: passed=3 failed=2 exit 1. Paren: passed=3 failed=4 exit 1. Same counts as the plan.
- Guard batch, one leased build (0 errors) and one leased `--no-build` run. Filter is the testing-and-build nine classes plus ProcessSpawnLimitTests, which the plan names in the guard batch. TRX `.antiphon/c1458-guards/c1458-guards.trx`: 135 passed, 0 failed, 0 skipped, duration 2m 19s. Counts: TestClassificationPolicyTests 41, TestClassificationGuardTests 1, SlowTestRegistryMergeTests 5, SlowTestTripwireTests 2, NativeLinuxPolicyWitnessTests 3, MechanicalPcContractTests 11, TaskPlatformGuidanceTests 5, C1729LayeringGuardTests 9, TestLaneCategoryGuardTests 1, ProcessSpawnLimitTests 57.
- check-evidence-diff: commits=2 entries=0 violations=0.
- merge-tree --write-tree origin/master (da6b708a2cab8ced435feb2f359d2e90618ae333) e9e2f312820c7e82137a1ee48138997511feb5a7: exit 0, tree 3b2880cb9bd5184a8879a6c6e2e139cc9cb1b951, no conflicts.
- impacted-classes on that range: no kinds, no classes, no filters. Downgrade selector count 0 (the migration row did not match).
- OS witness checker exit 0. Both scripts inventoried portable / test-only, zero candidates. No -Runner and no -Platform. Runner-defaults and session-runners both HTTP 200; this row stays unpinned.
- ASCII: the monitor, the harness, and the plan have zero bytes above 127.
- The diff adds no session input wait. Parking stays disabled, so there is no automatic release deadline (CARD-1083). Not applicable here.
- R1 no poll. R2 assertions are single-line function results or a regex over the JSON file, not a hard-coded newline product. R3 no terminal-state change. R4 limiter types were not touched; ProcessSpawnLimitTests still ran 57/57 because the plan batch names them. R5 no passes-alone red.
- Positive controls stay pending. This Review did not re-apply the two named mutants (raw string return; closer stop removed). SourceLanding owns them.

## Owner decisions (not blockers)

Confirmed still open, as D-2 and D-4 wrote them:

- Bare `u:p` is not a known form. `http://u:p@fake.invalid/x` is `http://<redacted>@fake.invalid/x`. A Plan with that server URL does not print `u:p`. `Password=ab` and `{"password":"ab"}` are masked.
- `pass=`, `pw=`, `pin=`, `otp=`, and `session=` stay unmasked.
- Scheme-less `u:x@h`, `Authorization: Bearer`, `Authorization: Basic`, and a base64 blob stay unmasked.
- A bare pair still runs to `;`, `&`, or end of line. `Password=SENT)TAIL1458` loses the tail. `Host=h;Password=SE NTCS1458;Database=kept` keeps `Database=kept`.

## Advisories (file cards; do not hold the land)

A 75-shape corpus of fake sentinels, including Baseline writes for the contracted note, a deep object, an array, a double-serialized document, a unicode key, and a parenthesised note.

1. A pool string whose value is already-escaped JSON still leaks. `{"outer":"{\"password\":\"SENTDBL1458\"}"}` reached the baseline and result JSON. One Hide-CutoverText pass does not see that pair. Real object nesting and arrays at wraps 1 through 12 are masked, including the e2e deep/array case.
2. A unicode-escaped key `\u0070assword` leaks into the written JSON. A unicode escape or an escaped quote inside a secret value is redacted.
3. Single-quoted `{'password':'...'}` leaks. It is not JSON.
4. `password%3D` inside a non-secret JSON string is not decoded, so the sentinel remains.
5. JSON keys `uid` and `user id` leak. Those spellings are on the value detector as `uid=` / `user id=`, not on the secret-name pattern.
6. `( Password=SENT)` with a space before the key still drops the closer. The new stop requires `(` immediately before the key.
7. `(Password=SE)NTPAR)` keeps the first `)` and leaks `NTPAR`. D-3 says that first `)` is the wrapper. The bare form `Password=abc)def;Database=kept` still masks through the paren.
8. Inside a pool JSON string, keys `passwordPolicy`, `design` (matches `sig`), and `author` (matches `auth`) are now redacted. A sentence that only contains the word password is kept. This comes from running the existing substring pattern through Hide-CutoverText.
9. A whole string that contains `Password=` or `scheme://...@` is still replaced with `<redacted>`, so sibling text in that string is dropped. That Test-CutoverSecretValue rule was already there.
10. Wrap 13 and 14 return the string raw because `Depth -gt 12` returns before the new mask, and `ConvertTo-Json -Depth 12` still writes that string. Wraps 1-12 do not leak.
11. A newline immediately after `=` is whitespace in the existing lead, so the next line is treated as the secret value (`Password=` plus a newline plus `SENTSPLIT` became `Password=` newline `<redacted>`). The lead regex is not in this diff.
12. The plan has no `## Performance budget` section. Measured CP-1 is 119.8s against an estimate of 3 minutes. No timeout was widened. D-2: this doc gap does not force another Review.

## Performance

No selection ran over 30 minutes. CP-1 119.8s. Guard execution 2m 19s after a 105s slot wait. Antiphon.Tests build 3m 51s, 0 errors, slot granted. Checkpoint-tool build 24.5s after a 31s slot wait.
