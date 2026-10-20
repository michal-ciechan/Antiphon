# CARD-1364: bound-land wait evidence through real proof paths

Date: 2026-10-20. Inspected base: `d772fdebd1962b0d4e832a7dcc6f8b0d30962db1`
(CARD-1353/1361/1371/1374 stack). Read CARD-1364 on the Antiphon board.
Verification design is folded into this plan; next is **Code**.
Code budget: **30-60 minutes, target 50**. Plan ran no tests, builds or deploy phases.

The card's test gap is confirmed by inspection. No production defect was found:
all five wait call sites still use the wait helper. Add assertions at their real
callers, including the later proofs that an earlier refusal otherwise masks.
Final changes are test-only; any discovered production defect needs a plain
finding and revised scope, not a silent repair.

## Ground truth

Anchors throughout this plan refer to the inspected base.

| Card assumption | What the code does | Required response |
|---|---|---|
| Bound-land refusal assertions can pass without waiting. | `RemoteScriptContractTests.cs:3481,3539` checks the resume land token by containment; `:1992-1996` checks only that the late-land vector removes no volumes. `RetiredTempContainerHostTests.cs:248-253` checks exit and prior removal count. | Keep those safety assertions; add exact wait evidence through the callers. |
| Wait-helper coverage proves every call site. | `RollingVolumeRecycleScriptTests.cs:504` exercises wrapper `redeploy-old` only. Its expiry arm at `:540` accepts any suffix after `waited=`. Host helper coverage at `:1668-1686` invokes `c1008_tasks_wait` directly. | A helper-only test cannot detect bypass at another caller. |
| All expiry counts can be fixed positive integers. | Wrapper `scripts/deploy-server2.ps1:893-918` uses a wall-clock deadline, permitting zero polls on expiry. Host `scripts/c590-remote.sh:5893-5907` uses `ceil(wait/poll)` and rejects zero configuration. | Use deterministic wrapper zero-budget refusals, separate clearing controls, and exact host count 1. Do not tighten a timing assumption. |
| Receipts contain the same final refusal text as output. | Wrapper expiry records `landWait={polls,outcome}` at `:909-913`. Host census writes `landWaitPolls` at `:5878-5879` before its caller appends `runner=… waited=…` at `:5904`. | Compare numeric poll fields, not host receipt refusal text with the later diagnostic suffix. |
| A census always waits. | Read-only `check-census` deliberately invokes `Assert-RecycleTaskCensus -Report` at `scripts/deploy-server2.ps1:1349`. | Exclude this reporting path from wait requirements. No change to CARD-1361/1371/1374 retry or reconciliation policy. |

## Decisions

| ID | Decision and reason | Rejected alternative |
|---|---|---|
| D-1 | Exercise real phase/proof orchestration with `C1008WrapperFixture` and `C1008HostFixture`; inject only external facts at the selected boundary. Keep production wait/census/proof functions intact in the green run. | Extracting only the helper, replacing the proof, or checking script text: none establishes that the caller waits. |
| D-2 | Wrapper refusal: `-LandWaitMinutes 0`, exact `waited=0polls`, receipt polls 0/outcome expired, no wait line. Also run a pending-then-empty control at each wrapper call site with the existing 60,000 ms test deadline: one wait line, receipt polls 1/outcome cleared, intended next host case reached. | Requiring positive polls after a 1/100 ms wall-clock expiry would be flaky; introducing another production clock seam is unnecessary. |
| D-3 | Host refusal: fixture bounds `C1008_LAND_WAIT_SECONDS=1`, `C1008_LAND_POLL_SECONDS=1`; require exact `waited=1polls`, one `C1008_LAND_WAIT … polls=0` line, two refusing land reads and receipt `landWaitPolls=1`. A fixture-local sleep recorder can avoid elapsed waits. | Merely checking `waited=` presence or a refusal prefix misses corrupt counts. A global helper mutation would test the already-covered helper. |
| D-4 | One caller-local red witness, then one narrow green filter. Mutate only a disposable host-script copy for CP-1; retain clean tracked production source and explicitly label the evidence as mutated. | Committing a deploy-script defect, broad existing class runs, or multiple mutation cycles exceeds this test-only brief and run budget. |
| D-5 | Native Linux offline lane, with pwsh, bash, jq and the existing Node fixture dependencies. Dispatch Code with `-Platform Linux`, omit `-Runner`. Both placement GETs were read on 2026-10-20: defaults revision 2, no kind overrides; the catalogue has an eligible Linux runner. Re-read at dispatch. | Pinning a fleet location or inferring eligibility from the saved default. No Windows qualification is claimed. |

No unresolved product decision and no new or changed session waiting for input.
Session parking/release policy is unaffected.

## Proof-path inventory

**W** means the exact wrapper assertions in D-2; **H** means the exact host
assertions in D-3, with the runner/task identity belonging to that row.
Each vector must establish its boundary from HTTP/case/Docker traces and journal
phase, then assert the wait evidence and no continuation past that refusal.
Inject the land only at the selected boundary; every earlier census must be clean.
The strings `server2`/`server2-temp` below are product runner identities under test,
not placement instructions.

| ID | Real path and file:line | Injection and required assertion |
|---|---|---|
| V-1 | `redeploy-old` → `Wait-RecycleTaskCensus`, `scripts/deploy-server2.ps1:1247,1270` | Bound `server2` land before the host `deploy-parent` case: **W**; that case is absent on refusal. Cover ordinary, resume and preview admission; the already-current fast path at `:1254` does not census. |
| V-2 | `drain-temp` → `Wait-TempContainerRetirement` → `Invoke-TempContainerCleanup` (`scripts/deploy-server2.ps1:1306,1070`), and fresh `retire-temp` → the same cleanup (`:1313`); wait at `:1019` | Bound `server2-temp` land after retired/disconnected status is valid: **W**; no `retire-temp-containers` case on refusal. Exercise both entry routes and cleanup preview. |
| V-3 | `retire-temp` volume admission → wait, `scripts/deploy-server2.ps1:1321-1330`, specifically `:1326` | Fresh path must complete the cleanup at `:1313` with an empty census before introducing a bound temp land. **W**; cleanup case exists but `retire-temp-runner` does not. Also cover resume (cleanup skipped) and preview with no cleanup candidates. |
| V-4 | `c1008_recycle` initial proof at `scripts/c590-remote.sh:7806` → `c1008_status_proof:6209` → wait `:6275` | **H** before mutation. Exercise main/temp fresh and preview admission; valid resumed journals, including main `recreating`/`verified` and temp `removing`, enter here too. |
| V-5 | Same host proof after lock at `scripts/c590-remote.sh:7819` → wait `:6275` | Introduce land after initial proof/lock, **H**; no subsequent destructive effect. Main/temp apply and resume use this boundary; preview returns at `:7816`. `recreating`/`verified` main resumes exit after these admission proofs (`:7828` onward). |
| V-6 | Post-container-removal audit → proof at `scripts/c590-remote.sh:7935` → wait `:6275` | Introduce land only after prior proofs (existing main `late-land` fault is usable). Establish `containersRemoved`, completed second audit, then **H**; no volume removal or temp `down -v`. Exercise main/temp and a continuing resume. |
| V-7 | Per-volume proof at `scripts/c590-remote.sh:7943` → wait `:6275` | Inject immediately before the selected iteration, **H**. Cover each main/temp target position, preserving already-recorded progress and preventing the next effect. Temp `down -v` occurs at `:7940` before this loop; assert no *further* effect, not that all volumes still exist. |
| V-8 | `retire-temp-containers` initial `c994_status_proof` at `scripts/c590-remote.sh:8410` → wait `:8327` | Bound temp land at entry, **H**, no container removal. Cover apply and preview. |
| V-9 | Before each temp container removal, `scripts/c590-remote.sh:8466` → wait `:8327` | Inject after initial proof, and after one removal with a second candidate left (existing `c994-late-land` fault). **H** and exact prior removal count; no next `docker rm`. |
| V-10 | Final temp proof, `scripts/c590-remote.sh:8486` → wait `:8327` | With one candidate, introduce land after its removal; also cover initially absent containers with injection after the initial proof. **H**; no completed cleanup receipt. Two-candidate late-land alone reaches V-9, not this boundary. |

## Slices

**S1 — wrapper assertions (10-15 minutes).** Add
`tests/Antiphon.Tests/Scripts/C1364BoundLandWaitProofTests.cs` with three
unparameterized methods: `C1364_Wrapper_redeploy_reports_waited` (V-1),
`C1364_Wrapper_cleanup_reports_waited` (V-2), and
`C1364_Wrapper_retire_volumes_reports_waited` (V-3).
Reuse exact-path `taskQueries` sequences from `scripts/fixtures/c727-fake-http.ps1`
to let earlier two-pass censuses complete. Keep any helper code in the new test
file. Commit/push; no separate run for this slice.

**S2 — host boundaries and witness (15-20 minutes).** In the same new class add
`C1364_Host_recycle_admission_reports_waited` (V-4/V-5),
`C1364_Host_recycle_post_removal_reports_waited` (V-6/V-7), and
`C1364_Host_cleanup_reports_waited` (V-8/V-9/V-10).
Use existing fixture `Run(extra: …)` transport/record hooks and
`scripts/fixtures/c1008-fake-docker.sh` late-land faults. Reuse the C1105 valid
resume setup in `RemoteScriptContractTests.cs:3485,3548-3572`, factoring only the
needed test helper if necessary. Do not manufacture an invalid journal that
refuses before the wait.

If needed, add an optional source-transform argument to `C1008HostFixture.Run`
in `tests/Antiphon.Tests/Scripts/RollingVolumeRecycleScriptTests.cs:2005-2053`;
its default is identity and its output stays in the fixture's disposable root.
Only the new post-removal method opts into the CP-1 transform. Keep all new
process-spawning tests under `ParallelLimiter<ProcessSpawnLimit>`.
Commit/push before both checkpoints. No production script or deployment changes.

## Verification design

Implement V-1–V-10 as labelled internal vectors of the six methods above.
Parse the complete refusal suffix, including task identity, runner, integer count
and `polls` unit; reject absent, malformed, duplicate or wrong `waited` values.
Use the matching census receipt and boundary trace, not an unrelated earlier
wait line. Each host refusal must also prove the expected one sleep and second
land read. Retain the existing tests and their assertions.

**R-1, the one red witness:** first vector of
`C1364_Host_recycle_post_removal_reports_waited` is V-6 main, using real late-land
input. Under CP-1's test-only `C1364_WAIT_MUTANT=post-removal-zero`, transform the
disposable source inside `c1008_status_proof` immediately after `:6275`: only when
the record phase is `containersRemoved`, rewrite the actual error's
`waited=1polls` to `waited=0polls`. Require exactly one caller anchor replacement;
record that the target was hit. Do not alter the helper at `:5892`, its counter,
receipt or wait-progress output, nor the expected value.

The real proof must reach refusal exit 2, with one wait line and receipt count 1,
then the new diagnostic equality must fail **expected 1 / actual 0**. Today's
late-land and resume tests accept the unchanged prefix/refusal/no-removal behavior,
and direct helper tests never traverse this transformed caller (inspection
finding, not a claimed baseline run). A direct `c1008_tasks` substitution at the
same call site also fails the new required-field/count assertion because it has
no `waited` suffix; no second red run is commissioned. An anchor/fixture/build
failure or zero executions is not this witness.

CP-2 clears the mutant environment and uses unmodified source for every V row.
Assert the same exact suffix on the public diagnosis; do not assert a fake
wrapper-generated sentinel in place of the production result. Report the six
TUnit results separately from internal vector counts. A real deploy phase is
never invoked outside the established offline fixtures.

Run `tools/Antiphon.Checkpoints` once for CP-1, then once for CP-2:
`run --plan <this-path> --rows CP-1` / `--rows CP-2`, each with
`--expected-source-sha <committed-HEAD>`. Use the available built tool; if absent,
report its setup build and gate it through `scripts/build-slot.ps1` into a fresh
alternate output. The checkpoint drivers acquire their own build slots; do not
hold an outer slot while they wait. Await every run, continuing the same run's
`wait --max-wait 50s` on exit 75. Exit 4 means not run/blocked, never unleased retry.
CP-1's failure is intentional evidence; preserve its actual failed CHECKPOINT
line, then run CP-2. Keep tracked source frozen during each run and generated
receipts/TRX ignored. Run `scripts/check-evidence-diff.ps1` over task base..HEAD,
clean only owned alternate outputs, and report exact SHAs and outcomes.
Ordinary Review qualifies CP-2; it does not repeat the historical mutant.

### Cost

Authoring 25-35 minutes; checkpoints 12 minutes; evidence/cleanup 3 minutes:
**40-50 minutes expected, 60-minute budget ceiling**. These six methods are the
closed ordinary scope; no whole-class legacy matrix, full suite or live rollout.

### Checkpoints

Both rows use the **native Linux offline lane**. Filters retain TUnit trailing
wildcards; verify the exact executed method roster in fresh TRX. CP-1 executes
one unparameterized method and must fail the named numeric assertion. CP-2
executes exactly the six named methods, with the mutant unset. Separate fresh
outputs keep the intentional red build distinct from the final green build.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Environment |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | S1-S2 | `tests/Antiphon.Tests -> bin-c1364-red/` | linux-wait-proof-red | `/*/Antiphon.Tests.Scripts/C1364BoundLandWaitProofTests*/C1364_Host_recycle_post_removal_reports_waited*` | R-1 | exactly 1 executed, 1 expected waited-count assertion failure, 0 skipped | 1 | 4 | `C1364_WAIT_MUTANT=post-removal-zero` |
| CP-2 | S1-S2 | `tests/Antiphon.Tests -> bin-c1364-green/` | linux-wait-proof-green | `/*/Antiphon.Tests.Scripts/C1364BoundLandWaitProofTests*/C1364_*` | V-1–V-10, R-1 restored | exactly 6 executed, 6 passed, 0 failed/skipped | 6 | 8 | `C1364_WAIT_MUTANT=` |
