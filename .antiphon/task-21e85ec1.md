# Design note (before edits)

- CARD-0957: DrainAsync preserves retiredAt, and clear precedes a fallible hold POST. Fail TempRunnerRetiredDuringHold before seed when the post-hold read is retired; require old accepting/eligible/non-retired and a host temp-project absence census before any retirement clear. Absence makes a failed hold safe even if clear succeeded. Choose the smallest additive abandon path expressly described by the card: documented manual retireWhenIdle=true drain only after host absence and zero work, wait for retirement, then retire-temp. No atomic server change or new phase.
- CARD-0958: Seed's cold early return omits absence; online recovery is routed into seed. Require host absence on deploy-temp before state mutations, repeat it in the real deploy host case and cold seed reuse. Explicitly refuse deploy-temp -SavedDonor (TempSavedDonorRequiresMaintenance); full donors remain supported by ordinary Seed, but a present temp project refuses deploy-temp (TempContainersRemain). Preserve ordinary status_zero/prune_idle maintenance gates rather than broaden them. Same-SHA available recovery waits without rebuilding.
- CARD-0976: command substitution isolates WROTE and EXIT trap overwrites receipt. c849_image returns dedicated statuses; every caller emits named write_result in parent. Test real EXIT trap final JSON.
- CARD-0944: seed name/item/source and require_ready item lack locals (i is already local at this start ref). Localize and add a static guard scanning every c849 function's for variables, including a deliberate CARD-0933-shaped missing local.
- CARD-0951/0952: observe treats sudo test failure as false and lacks literal canonical mountpoint equality. Require equality and distinguish test rc=1 from errors in observe/prune; named shell-shim cases for symlink, all sudo refusal, and test-only refusal in both functions.
- CARD-0946: successful rolling runs retain owned roots; jq driver already deletes success at this ref. Add guarded owned-root finally cleanup and KeepTemp, preserve external C973_TEST_ROOT custody for jq driver's state census, add matching keep/confinement to jq driver.

Files: scope-fenced deploy/c590 scripts, rolling harness and c727/c973 fixtures, RemoteScriptContractTests, docker-stack.md.

Tests: red first new C957/C958 rolling groups; C976 final receipt, C944 static loop guard, C951 named path checks, C946 cleanup contracts. Preserve C849/C912/C973 and DockerStack*/DindRunner/RunnerDrain/StandingPipeline docs; full PhoneHomeRollingRunner* classes if applicable. No server or shared helpers touched, so no broad Unit lane per brief.

### Checkpoints
| Row | Build / exact filter or driver |
|---|---|
| CP-red-host | isolated bin-c957-red; /*/*/RemoteScriptContractTests*/(C944_*)|(C951_*)|(C976_*)|(C946_*) |
| CP-green-caches | isolated bin-c957-caches; /*/*/RemoteScriptContractTests*/(C849_*)|(C912_*)|(C973_*)|(C944_*)|(C951_*)|(C976_*)|(C946_*) |
| CP-contracts | isolated bin-c957-contracts; /*/*/(DockerStackContractTests*)|(DindRunnerContractTests*)|(RemoteScriptContractTests*)|(RunnerDrainScriptTests*)|(DockerStackDocumentationTests*)|(DockerStackSmokeCommandTests*)|(StandingPipelinePolicyDocumentationTests*)/* |
| CP-rolling | build-slot wrapper: test-deploy-server2.ps1 |
| CP-jq | build-slot wrapper: test-deploy-server2-jq.ps1 absent/present/missing-shell/failing-shell |
| CP-real | build-slot wrapper: real c590 host cases against base/branch with own nested Docker daemon, labelled busybox runner/status endpoint; root paths relocated, sudo backed by a root helper container; expensive build/boot boundaries shimmed only |
| CP-spots | three scratch mutations, method-filtered checkpoint runs, exact byte restoration and empty diff |

Real exercise targets cold-marker + existing temp refusal, absent cold reuse, missing helper image final receipt, and observe/prune privileged negative paths. Docker operations/inspect and volume metadata real; no live host or standing runner changed.

# Final Code report

All seven cards are implemented and pushed within the assigned script/test/documentation fence. The final checkpoint passed 302/302 tests with zero failures or skips. This branch is ready for Final Review, followed by plain land by the landing owner. PCs remain pending method-scoped SourceLanding Mutation.

## Result and decisions

- **0957:** An early read-only SSH census includes stopped temp containers before clearing retirement or posting the hold. Old must be dispatch eligible, accepting, non-draining and non-retired. A retiredAt stamp surviving the hold now refuses before Seed; a second retirement check guards final clear. The failed-clear/hold case is tested and refuses before any host Seed/deploy operation. No atomic server API change was needed.
- **0957 abandon:** Chose the card's smallest additive documented manual path rather than a new phase. With old healthy/accepting, temp host absence confirmed and no bound/queued work, explicitly convert the non-retiring hold with runner-drain `-RetireWhenIdle`, then wait for retirement. Offline unknown live inventory may leave a retired absent placeholder; retire-temp still requires all three fresh counters equal zero. Remaining containers require reviewed recovery. Documentation and commands are in `docs/docker-stack.md`.
- **0958:** Deploy-temp rejects existing temp/donor containers with `TempContainersRemain`, and census errors with `TempContainerCensusUnavailable`. It explicitly rejects `-SavedDonor` as `TempSavedDonorRequiresMaintenance`, an option permitted by the card. Ordinary Seed/saved-donor maintenance remains available under its existing strict consumer gates. The host repeats temp absence for deploy and cold marker reuse. An available same-SHA recovering temp waits for eligibility without reseeding or recreating it.
- **0976:** c849_image returns status 10 (missing helper) or 11 (donor-image lookup failure). All 13 direct image assignments turn failures into write_result in the parent shell, preserving the final EXIT-trap receipt diagnosis; the two nested inspect uses already have named parent-shell refusals. The real receipt test and Docker exercise verify `CacheHelperImageMissing`, formerly `UnhandledExit 2`.
- **0944:** Seed/require_ready loop variables are local; i was already local in the starting version. The static guard examines every c849 function, excludes quoted child-shell programs, and also caught fixture_model's variables. A deliberate CARD-0933-shaped missing local goes red.
- **0951/0952:** Observe requires the literal canonical Docker volume path plus privileged resolved-path equality. Observe and prune accept sudo test -L exit 1 only; permission/command failures refuse `CacheTargetInvalid`. Named tests cover symlink, all sudo refused, test-only refusal and their combined case for both readers.
- **0946:** Successful harness runs remove only their direct owned literal non-reparse GUID root. Failed/KeepTemp runs preserve evidence; externally supplied C973_TEST_ROOT remains the driver's responsibility. The jq driver has matching containment and KeepTemp behavior. Both actual cleanup and intentional retained-failure behavior are tested.

Changed files: `scripts/c590-remote.sh`, `scripts/deploy-server2.ps1`, `scripts/test-deploy-server2.ps1`, `scripts/test-deploy-server2-jq.ps1`, `scripts/fixtures/c727-fake-http.ps1`, `scripts/fixtures/c727-fake-verify.ps1`, `scripts/fixtures/c973-marker-reader.sh`, new retained `scripts/fixtures/c973-rolling-host-cases.mjs`, `tests/Antiphon.Tests/Scripts/RemoteScriptContractTests.cs`, `docs/docker-stack.md`, and this report. No server/src or shared test helpers changed. Ordinary full Unit lane was therefore omitted under the brief's explicit exception.

## Verification and red evidence

Every build/test driver held a build slot. TUnit ran through run-checkpoint.ps1, with one isolated build and literal prefix filter per row. All test-bearing slices were committed and pushed first. The final CP-contracts includes the full PhoneHomeRollingRunnerTests class to preserve the live rolling shapes required by the brief; it extends the initial CP-contracts roster for that explicit requirement. CP-green-caches also includes C957 scratch controls. The final two combined C951 parameters are covered by CP-contracts.

| Evidence | Result |
|---|---|
| CP-red-host, unchanged production at fc561e3d51c91cd3b573a9e3e7485e0c88d3bd06 | 9 executed, 4 passed, 5 expected failures, 0 skipped: C944 local guard, C946 cleanup guard, C976 final receipt, C951 observe/prune test-only sudo refusal |
| C957/C958 targeted harness red, production identical to c545ebfb | T21 surviving retirement, T22 present temp, T23 recovering temp and T24 SavedDonor each failed at their named assertions before implementation |
| CP-green-caches at 59971a3f378e9b2628fee139efcdb3e2c4d2e8bd | 52 executed, 52 passed, 0 failed, 0 skipped; clean/verified build source; slot granted after 225 seconds |
| CP-contracts at 7e20d25c702b7763b03d691d0dcd24f3ae0a0eec | 302 executed, 302 passed, 0 failed, 0 skipped; isolated build passed; clean/verified source; slot granted with no wait |
| CP-rolling | 24 groups, 66 invocations, 227 assertions, 0 failures; success root removed |
| CP-jq present | 31 driver assertions, 0 failures; actual jq available; harness 24/66/227; KeepTemp root retained with 66 state files |
| CP-jq absent, missing-shell, failing-shell | Each 31 driver assertions, 0 failures; harness 23/62/218; only named jq-only T20 omitted; success roots removed |
| CP-real combined | 19 expected base/branch outcomes, 0 failures; real nested Docker volumes/containers and root sudo helper; evidence `/tmp/c957-real-dxu6Ko` |
| CP-spots | Three independent loop/observe/receipt scratch-file mutations go red, exact bytes restored, green assertions rerun, tracked git diff empty |
| Static checks | bash -n, node --check, PowerShell parser for all three changed drivers, git diff --check passed |

The full harness grew from the prescribed jq baseline 20/59/206 to 24/66/227; without jq it grew from 19/55/197 to 23/62/218. T21-T24 are named host-race, host-absence, host-recovery and host-saved groups. Existing ordinary Seed/Reset/Prune/saved-donor tests remain in the roster. Same-SHA redeploy-old and retired-placeholder/cold-marker/no-container flow remain covered.

Final TRX roster: DockerStackContractTests 112; DindRunnerContractTests 23; RemoteScriptContractTests 84; RunnerDrainScriptTests 1; DockerStackDocumentationTests 10; DockerStackSmokeCommandTests 35; StandingPipelinePolicyDocumentationTests 10; PhoneHomeRollingRunnerTests 27. All passed. Remote includes all eight named path-fault parameters and all three scratch mutation parameters. TRX: `/work/worktrees/task-21e85ec1/.antiphon/c957-checkpoints/CP-contracts-20261002-180906-c73c/run.trx`. The test runtime's production-runner guard reports loopback port 1 and disables standing interpreters and Hangfire; the rolling integration world used its isolated test database/host. This report-only successor does not alter tested implementation bytes.

```text
CHECKPOINT CP-contracts commit=7e20d25c702b7763b03d691d0dcd24f3ae0a0eec build=ok filter=/*/*/(DockerStackContractTests*)|(DindRunnerContractTests*)|(RemoteScriptContractTests*)|(RunnerDrainScriptTests*)|(DockerStackDocumentationTests*)|(DockerStackSmokeCommandTests*)|(StandingPipelinePolicyDocumentationTests*)|(PhoneHomeRollingRunnerTests*)/* executed=302 passed=302 failed=0 skipped=0 trx=/work/worktrees/task-21e85ec1/.antiphon/c957-checkpoints/CP-contracts-20261002-180906-c73c/run.trx slot=granted waited=0s dirty=0 source=7e20d25c702b7763b03d691d0dcd24f3ae0a0eec sourceState=clean buildSource=verified
CHECKPOINT CP-contracts EXIT CODE: 0
```

Development failures were resolved: the initial red build used an unsupported assertion overload; subsequent 9-test red run is the valid evidence. The first green cache run was 48/52 and the next 51/52 because old fakes assumed temp was always present and the receipt mutation did not restore the entire old interface. The first rolling harness run similarly exposed the retire-temp fake shape; both were corrected. A jq-driver parser error in cleanup was fixed before all four green driver runs. The initial final-checkpoint attempt had an invalid OutputPath without the required trailing forward slash and executed zero tests; it was corrected before the isolated run. Initial real-driver attempts failed because Python was absent and then because a fixture SHA was malformed; neither is claimed as red evidence.

## Real host exercise and limits

The retained Node fixture guards that it is inside the review container and Docker daemon Name equals that container's hostname before creating anything. It uses unique project-labelled busybox main/temp containers, three real labelled named volumes with 1654:1654 ownership and 0700 mode, and a busybox HTTP status endpoint on a random loopback port. A root Debian helper binds the daemon's Docker root and the private evidence root to run actual privileged realpath/test/stat probes. Finally removes only exact container IDs and volume names it created; evidence is kept.

Shimmed: host lane selection and root paths, lock/evidence directory setup on private roots, sudo transport via the root helper, compose main lookup via real project labels, and expensive deployment checkout/build/boot boundaries. Docker ps/inspect/volume inspection and volume metadata are real. The exercise does not build a production runner or make a live rollout/canary claim.

Base source is afb8ccfb128c53300fb960cf0bbb4433184cb748; its c590 and deploy-server2 blobs are identical to c545ebfb (respectively 39315a83a07021ce55bb30d52aa9e3c882e28c2b and 92e03fd3894a45c767ddde023b31a104431fa65e). Source receipt records base script SHA256 `5ff3194ef35cbd0bbe9b7f0f4c13cbbba37365fde9f438258515c20579696a1e` and branch script SHA256 `ed0729a9ad003166b4c5e9390329cec7cf033dd9f3dd56587dcc7de870bb34da`; current production bytes are unchanged since the real run at 59971a3f.

Actual contrasts: base accepts cold reuse with an existing temp, test-only sudo refusal in both readers, and combined symlink/test-refusal in observe; branch refuses each by the intended diagnosis. Base missing helper ends `UnhandledExit 2`; branch preserves `CacheHelperImageMissing`. Existing symlink-only refusals remain intact; branch cold reuse with no temp succeeds. Base deploy-existing-temp already refused downstream `ParentStackMissing` in the controlled shim; only branch's early `CacheTempContainerExists` gate is claimed for that case.

Local evidence paths: `.antiphon/c957-red-rerun.log`, `.antiphon/c957-rolling-red-race.log`, `.antiphon/c958-rolling-red-absence.log`, `.antiphon/c958-red-extra.log`, `.antiphon/c957-caches-green.log`, `.antiphon/c957-rolling-final.log`, `.antiphon/c957-jq-{absent,present,missing-shell,failing-shell}-final.log`, `.antiphon/c957-real-combined.log`, `.antiphon/c957-contracts-final.log`. Real JSON/receipt evidence is `/tmp/c957-real-dxu6Ko`; intentional jq KeepTemp evidence is `.antiphon/c973-jq-ff75dce1b65148efbe92d23f6229b6f7`. These logs are local; the committed report and retained fixture provide the portable rerun contract.

## Rerun

Use jq 1.7.1 on PATH with verified SHA256 `5942c9b0934e510ee61eb3e30273f1b3fe2590df93933a93d7c58b81d19c8ff5`; this run used `.antiphon/c957-tools/jq`. From the assigned worktree, use an already committed clean source and a distinct `bin-*/` output for any rerun:

```sh
PATH="$PWD/.antiphon/c957-tools:$PATH" TUNIT_MAX_PARALLEL_TESTS=1 pwsh -NoProfile -File scripts/run-checkpoint.ps1 -Name CP-contracts -Project tests/Antiphon.Tests -OutputPath bin-c957-contracts/ -Filter '/*/*/(DockerStackContractTests*)|(DindRunnerContractTests*)|(RemoteScriptContractTests*)|(RunnerDrainScriptTests*)|(DockerStackDocumentationTests*)|(DockerStackSmokeCommandTests*)|(StandingPipelinePolicyDocumentationTests*)|(PhoneHomeRollingRunnerTests*)/*' -MinExecuted 230 -Expect 'DockerStackContractTests,DindRunnerContractTests,RemoteScriptContractTests,RunnerDrainScriptTests,DockerStackDocumentationTests,DockerStackSmokeCommandTests,StandingPipelinePolicyDocumentationTests,PhoneHomeRollingRunnerTests' -ResultsRoot .antiphon/c957-checkpoints
PATH="$PWD/.antiphon/c957-tools:$PATH" pwsh -NoProfile -File scripts/build-slot.ps1 -Label c957-rolling -- pwsh -NoProfile -File scripts/test-deploy-server2.ps1
PATH="$PWD/.antiphon/c957-tools:$PATH" pwsh -NoProfile -File scripts/build-slot.ps1 -Label c957-jq -- pwsh -NoProfile -File scripts/test-deploy-server2-jq.ps1 -Case present
PATH="$PWD/.antiphon/c957-tools:$PATH" pwsh -NoProfile -File scripts/build-slot.ps1 -Label c957-real -- node scripts/fixtures/c973-rolling-host-cases.mjs
```

Repeat the jq driver command with `-Case absent`, `-Case missing-shell`, and `-Case failing-shell`. Add `-KeepTemp` when intentionally preserving successful driver evidence. The full contract filter reruns the three scratch spot checks. Final Review should assess the manual abandon/early-refusal policy, then plain land; all PCs still require method-scoped SourceLanding Mutation.
