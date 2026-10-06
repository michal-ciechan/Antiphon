# CARD-0817 Code: closed checkpoints delivered; Unit qualification caller-owned

The caller's refinement arrived after the test runs had finished and their build
slots had been released. It directs stopping whole-Unit/baseline work, treating
CP-1..CP-6 as the closed list, and committing, pushing and reporting existing
results. No further build, test, baseline run or source/test repair was started.
Unit qualification remains incomplete and caller-owned; this report does not
claim a green Final Unit profile.

Original Code task / landing owner: `2a876d8a-7681-47be-94bb-c8cba4abfade`.
Branch: `feat/card-task-2a876d8a`.
Worktree: `/work/worktrees/task-2a876d8a`.
Original base: `71685b84772b82517c2db5dd5d18e085ca8f360a`.
Implementation plan: `docs/superpowers/plans/2026-10-05-card-0817-https-token-push-credential-plan.md`.
Supplemental Final manifest: `.antiphon/card-0817-final-profile.md`.
The final caller-facing progress marker identifies the pushed report commit; actual tested SHAs are recorded below.

## Outcome under the caller's refinement

S1-S3 are implemented and pushed, including the two explicitly authorized scratch bindings. CP-1..CP-6 passed 54/54 tests, and the full workspace integration class passed 33/33. The implementation and completed ordinary V/R evidence are handed to Review with the limitations below. The broader Antiphon.Tests Unit row timed out without a TRX and its console exposed a confirmed new mount-count fixture failure; neither that failure nor the incomplete Final qualification is represented as green.

The controlling refinement is `.antiphon/inbox/250ae3ea-0d32-4f5f-9c3f-24a0b4a4acdc.md`: "Stop the whole-Unit and baseline runs now and release the build slot; CP-1 to CP-6 are the closed list. Commit, push and report with the results you have; Unit qualification is caller-owned." All owned runs had already ended, every launcher had reported its slot released, the baseline worktree had been removed, and alternate outputs had been cleaned. These facts were checked again on receipt; no additional slot was acquired.

The brief at `.antiphon/inbox/2e99facf-854c-4bb1-b019-45dbba79165a.md` says: "If a slice needs a seam outside the plan table, STOP and report it exactly." It also limits this dispatch to "at most 3 repair rounds." The user's follow-up authorized only the two scratch bindings already delivered. The following additional repair has not been applied:

- **F-1, introduced:** `tests/Antiphon.Tests/Scripts/RetiredTempContainerHostTests.cs:160`, `C994_Production_mount_topology_is_proven`, expects the exact logical roster `[14,3]`; the required token-directory bind makes it `[15,3]`. The same method passed at the original base (CP-14). Add this test file to scope and update the exact expected topology for the new read-only bind, keeping the existing negative topology cases. This is a new fixture seam beyond the two authorized files.
- **F-2, additional failure in an already-red guard:** `tests/Antiphon.Tests/Scripts/RemoteScriptContractTests.cs:1685`, `Nested_lane_never_uses_sudo_or_python`, does not include `ensure_runner_github_token_dir` among recognized host helpers. The task console fails on that helper's `sudo install -d`. The new helper itself refuses a non-host lane before sudo. A repair should assert that guard and recognize only that helper body, preserving the nested-lane prohibition. At base, the same method already fails on the unrelated `c1008_owned_mounts` readlink line (CP-13); merely fixing the new exemption will not erase that inherited failure.

**Caller-owned follow-up:** disposition of F-1/F-2 and the incomplete Unit scope remains with the caller and Review. A future repair needs the additional F-1 fixture seam and an authorized repair budget. The inherited guard failure also needs separate triage or an explicitly recorded inherited-red disposition. The latest refinement supersedes this report's earlier request to continue Code here. No source/test repair was made after the third allowed round.

### Supplemental Final and baseline results

| Row | Actual result | Classification |
|---|---|---|
| CP-7, task HEAD | 294 executed, 273 passed, 21 failed, 0 skipped | 20 failures reproduced at base; websocket failure did not reproduce in focused base/HEAD checks. |
| CP-8, task HEAD | 33 executed, 33 passed, 0 failed/skipped | Full RunnerWorkspaceServiceTests; receipt validated. |
| CP-9, task HEAD | Exit 5 after the unchanged 45-minute row timeout; executed/passed/failed/skipped unavailable, no TRX | Incomplete whole Antiphon.Tests Unit lane. Console diagnostics are not a completed test-count receipt. |
| CP-10, base | 19 executed, 0 passed, 19 failed, 0 skipped | Same Windows launch-policy assertion/platform failures; inherited on this Linux lane. |
| CP-11, base | 1 executed, 1 passed | Websocket timeout method passed in isolation. |
| CP-12, base | 1 executed, 0 passed, 1 failed | Same Linux custody-backend assertion; inherited. |
| CP-13, base | 1 executed, 0 passed, 1 failed | Host-lane guard was already red on a different helper; F-2 identifies the additional omission. |
| CP-14, base | 1 executed, 1 passed | Confirms F-1's new exact-roster mismatch. |
| CP-15, base | 1 executed, 0 passed, 1 failed | Same registry missing-reason entries for DirectoryLinkFixtureWindowsTests and CodexCliObservationGapTests; inherited. |
| CP-16, base | 2 executed, 0 passed, 2 failed | Both C1050 Windows outcome methods require Windows and fail on Linux at base too. |
| CP-17, task HEAD | 1 executed, 1 passed | Focused websocket follow-up passed; the earlier CP-7 failure remains recorded, not erased. |

Task HEAD for CP-7/8/9/17 was `9da56ce047e2109ad19b958c1be9b5e9d1abc63d`. Baseline runs used exactly `71685b84772b82517c2db5dd5d18e085ca8f360a` in a detached scratch worktree. Source and test files remained frozen while each run was active.

The first admitted CP-10..CP-12 attempt (`20261006-000122-8be3`) expired after 30 minutes during build-slot admission, before any build or test. It is NOT baseline proof. The same selection was rerun with unchanged limits after CP-9 ended, yielding the CP-10..CP-12 results above. Its launcher had slot=granted waited=345s before the inner admission wait; the successful retry launcher had slot=granted waited=0s.

The CP-9 console shows five failed methods before cancellation: F-1, F-2, the classification guard and the two Windows placement methods. The latter three were reproduced at base. Platform skips also appear in the partial console. There is no complete CP-9 roster or count, so neither complete Unit coverage nor green full coverage of RemoteScriptContractTests, RollingVolumeRecycleScriptTests or RetiredTempContainerHostTests is claimed.

**F-3, unresolved observation:** `PhoneHomeConnectionServiceTests.Websocket_connect_that_hangs_ends_within_the_connect_timeout` expected OperationCanceledException but got WebSocketException in CP-7. It passed at base (CP-11) and at task HEAD (CP-17). Both the test file and PhoneHomeConnectionService.cs are unchanged over the task range. This is not established as an introduced defect or a proven inherited red; no retry was added to the test, and no further repetition followed green.

Deferred to caller-owned Final qualification: CP-9's whole Unit coverage, F-1/F-2 corrections and their ordinary verification, and disposition of F-3. CP-7's original red result remains recorded. All V-1..V-18 and R-1..R-3 completed their named plan selections; none of these deferred items is marked passed.

## Implementation and authorized scope

S1 adds the push-failure classifier, fixed safe remedies, startup publication of the effective repository policy, optional validated policy path, and the hosted-service ordering. Probe argv, HTTP 409, problem code and the primary-repository exemption remain covered by the workspace tests. Raw git stderr is not surfaced or logged by the new refusal path.

S2 adds the baked POSIX helper, narrows the Antiphon push rewrite, binds the shared token directory read-only, and adds directory custody and presence-only diagnostics to deploy/restart/Compose/recycle paths. The helper reads an inert or operator-provisioned file only when git requests an admitted HTTPS GitHub path; it emits only the password line and never stores credentials. The deploy ownership sweep prunes the credential directory. The image is text-guarded here; building and activating it belongs to the caller's rollout.

The user explicitly authorized the two additional fixture seams after the initial scope stop: one scratch `GITHUB_TOKEN_DIR_PATH` binding each in `RollingVolumeRecycleScriptTests.cs` and `scripts/fixtures/c1008-recycle-real-cases.mjs`. Each received exactly that one-line fixture edit. The real-container fixture harness was not run by this task.

S3 adds the operator-only refresh script and custody/rotation documentation. Refresh streams the vault value through SSH stdin into a unique sibling temporary file, sets owner/mode before atomic replacement, clears its token variable, and prints only validated owner/mode/presence metadata. Code never executed that script against a vault or host. Lost SSH confirmation is reported honestly rather than promising that a completed remote replacement was rolled back.

## Commits and repairs

All commits were pushed fast-forward to the assigned branch; no amend, rebase, reset or force push was used.

| Commit | Change and actual outcome |
|---|---|
| `c25196bb72c5abdda2630b636ed8edae4c3f2d51` | S1; CP-2 passed 6/6, initial CP-1 selected zero tests. |
| `a293d255f7c0ac7fb689a62fd5ab355fdde8198b` | Repair 1: combined-class discovery wildcards; CP-1 passed 8/8. |
| `7c12d5cc43cbe8de337a2dee7e8b74d4404022ac` | Initial scope-stop report, superseded by the user's authorization and this report. |
| `f48c14211cb5013ab90c082aeccf4865f471909e` | S2; first build failed with missing ProcessSpawnLimit namespace imports. |
| `b88a742e69a635ac0494b19b1642c262991d9b52` | Repair 2: import the existing test-helper namespace; CP-3/4/5 passed 6/25/6. |
| `80f79859d8b5435c8956cae32405b851ec7bea76` | S3; a parse-only check found invalid multiline PowerShell boolean continuation. |
| `d01a9b5d2cc92a9e3f9c5118c6223147024f35c8` | Repair 3: place the boolean operators on the preceding lines; syntax checks and CP-6 passed. |
| `9da56ce047e2109ad19b958c1be9b5e9d1abc63d` | Declare the mandatory supplemental Final scope before running it. No implementation change. |
| `de2931b79740c7d117f801662de55f3070550bb9` | Record the completed checks, Final timeout and fixture findings before the caller's stop-and-report refinement. |

The three permitted repair rounds are used. No timeout was widened, assertion loosened or retry added. Deliberate mutants were not run.

## Ordinary checkpoint and invariant outcomes

The closed CP-1..CP-6 selection passed all 54 intended tests, with fresh TRX rosters inspected. CP-3..CP-6 account for 40 of these tests. The initial zero-discovery and compile failures are retained below, not counted as passes.

| CP | Actual final result | Test slot |
|---|---|---|
| CP-1 | 8 executed, 8 passed, 0 failed/skipped at a293d255f7c0ac7fb689a62fd5ab355fdde8198b | slot=granted waited=0s |
| CP-2 | 6 executed, 6 passed, 0 failed/skipped at c25196bb72c5abdda2630b636ed8edae4c3f2d51 | slot=granted waited=0s |
| CP-3 | 6 executed, 6 passed, 0 failed/skipped at b88a742e69a635ac0494b19b1642c262991d9b52 | slot=granted waited=0s |
| CP-4 | 25 executed, 25 passed, 0 failed/skipped at b88a742e69a635ac0494b19b1642c262991d9b52 | slot=granted waited=15s |
| CP-5 | 6 executed, 6 passed, 0 failed/skipped at b88a742e69a635ac0494b19b1642c262991d9b52 | slot=granted waited=30s |
| CP-6 | 3 executed, 3 passed, 0 failed/skipped at d01a9b5d2cc92a9e3f9c5118c6223147024f35c8 | slot=granted waited=30s |

Every producing build for CP-1..CP-6 had slot=granted waited=0s. The failed S2 build prevented its three test rows from running; their receipts correctly say slot=skipped and buildSource=unknown. The successful rows have dirty=0, sourceState=clean and buildSource=verified and passed the receipt validator with their actual tested SHA.

| ID | Actual result in the plan's ordinary selection |
|---|---|
| V-1 | PASS: five classifier methods, all named stderr markers, rotation remedy exclusivity and success override. |
| V-2 | PASS: normalized primary, ordered ordinal prefixes and newline format. |
| V-3 | PASS: parent creation, atomic replacement, no temporary residue; disabled/unset no-op and enabled startup publication. |
| V-4 | PASS: null/absolute policy path accepted and relative path refused. |
| V-5 | PASS: refusal identity/category/409/code, no raw stderr, exact dry-run argv, no refused worktree, disabled-probe mirror succeeds. |
| V-6 | PASS: exact password-only output, stripped whitespace, empty stderr and replacement visibility. |
| V-7 | PASS: other hosts and non-HTTPS protocol receive nothing. |
| V-8 | PASS: owner boundary, normalized case, exact entry boundary and malformed-path refusal. |
| V-9 | PASS: missing policy fails closed. |
| V-10 | PASS: missing or whitespace-only token yields nothing. |
| V-11 | PASS: store/erase/unknown verbs have no output or writes; token/policy contents and directory roster remain unchanged. |
| V-12 | PASS: real local git URL resolution keeps exact Antiphon SSH rewrite and other pushes HTTPS. |
| V-13 | PASS: entrypoint notes presence without staging, printing or exporting token content. |
| V-14 | PASS: required shared read-only directory bind, policy path and temp inheritance. |
| V-15 | PASS: deploy directory guard, ownership/mode, presence evidence, restart order, Compose variables and recycle roster. |
| V-16 | PASS: refresh stdin custody, atomic replacement, cleanup, no token output/argv/environment/file copy and ASCII source. |
| V-17 | PASS: relay refusal before SSH and metadata-only confirmation. |
| V-18 | PASS: vault reference, scopes, custody, expiry, reminder and rotation documentation. |
| R-1 | PASS: all five named workspace regressions; the supplemental full class also passed 33/33. |
| R-2 | PASS: all other 22 DindRunnerContractTests methods (25/25 full class). |
| R-3 | PASS: all five named deploy/restart/recycle/scrubber regressions. |

## Additional checks and limits

The brief's Final profile and stage instructions require whole Unit lanes and full affected integration classes, despite the narrower plan execution paragraph. Before running these extra checks, the committed supplemental manifest named the admission/custody/startup invariant, both Unit projects, the bounded 33-method workspace integration class, serial execution and an estimated 21-minute cost. No whole-assembly or Pty assembly run was requested.

Two gated syntax-only commands were outside the checkpoint table because text guards cannot prove PowerShell/shell/JavaScript parseability. The first found the S3 continuation syntax defect at 80f79859d8b5435c8956cae32405b851ec7bea76. A direct parser diagnostic localized it; after d01a9b5d2cc92a9e3f9c5118c6223147024f35c8, the PowerShell parser, `sh -n` on helper and entrypoint, `bash -n` on c590, and `node --check` on both fixture modules all passed. These checks parsed six files and never executed refresh or rollout behavior. Both gated commands had slot=granted waited=0s.

The first baseline-reproduction launcher acquired a slot after 90 seconds but exited 2 before building/testing because its results root was outside the detached baseline checkout. The corrected invocation uses that checkout's own results directory. This is a reported invocation correction, not a hidden test or code repair.

## Mutation, operator acceptance and activation

PC-1, PC-2, PC-3, PC-4, PC-5, PC-6, PC-7, PC-8, PC-9, PC-10, PC-11, PC-12, PC-13, PC-14 (both username-output and whitespace behavior), PC-15, PC-16, PC-17, PC-18, PC-19 and PC-20 are all PENDING for method-scoped SourceLanding Mutation, including missing-control discovery. Ordinary green does not discharge them.

Operator acceptance steps 1-5, image build, token provisioning, live private fetch/dry-run/negative probe, canary and staged rollout remain caller-owned after Review and landing. No real token was read, printed, copied or installed by Code; no vault operation or production deployment was performed. The normal authorized branch pushes are publication only, not live credential acceptance.

Platform reads completed before implementation: GET /api/runner-defaults (revision 2) and GET /api/session-runners. The task token was checked for presence only. No runner/platform setting was changed.

Restart required for activation: runner, owned by the caller/operator's staged rollout. Restart performed by Code: none. No server restart is required by these runner changes.

## Build-slot and source provenance

Every build and test used the host gate. The original checkpoint launchers, supplemental Final launcher, successful baseline retry, Unit baseline launcher and websocket launcher had slot=granted waited=0s; the two earlier baseline launcher waits were 90s (invalid results-root invocation) and 345s (subsequently timed-out inner admission).

| Run | Producing build | Actual slot / wait | Build result |
|---|---|---|---|
| 20261005-232424-bc98 | bin-c0817-runner | slot=granted waited=0s | ok |
| 20261005-232557-a559 | bin-c0817-runner | slot=granted waited=0s | ok |
| 20261005-233256-4494 | bin-c0817-image | slot=granted waited=0s | failed |
| 20261005-233525-cec2 | bin-c0817-image | slot=granted waited=0s | ok |
| 20261005-234324-11a2 | bin-c0817-docs | slot=granted waited=0s | ok |
| 20261005-234657-4ebe | bin-c0817-final-runner | slot=granted waited=0s | ok |
| 20261005-234657-4ebe | bin-c0817-final | slot=granted waited=0s | ok |
| 20261006-003621-6521 | bin-c0817-websocket | slot=granted waited=120s | ok |
| 20261006-000122-8be3 | bin-c0817-base-runner | slot=not-granted waited=0s | failed |
| 20261006-003448-38f4 | bin-c0817-base-runner | slot=granted waited=0s | ok |
| 20261006-003610-f93f | bin-c0817-base-unit | slot=granted waited=0s | ok |

Per-row waits are preserved in the unedited lines below. CP-13 waited 75s, CP-14 15s, and CP-17 60s; CP-17's producing build waited another 120s. A stored waited=0 on an unstarted build is not a granted slot or evidence of execution. The timed-out baseline attempt's wall-clock admission wait was 30 minutes.

The additional CP-10..CP-16 selections exist only to check failures observed in CP-7/CP-9 at the original base. CP-17 is the single exact-method follow-up justified by CP-11 passing. They are declared in ignored diagnostic manifests `.antiphon/c0817-runner-base-proof.md`, `.antiphon/c0817-unit-base-proof.md`, and `.antiphon/c0817-websocket-followup.md`; their exact filters are retained below. No baseline assembly/Unit lane was run.

Fresh TRX counters, classes and methods were inspected for every completed intended row. Green source validators passed for original CP-1..CP-6 at their respective SHAs, CP-8 and CP-17 at task HEAD, and CP-11/CP-14 at base. Failed rows were inspected as failed diagnostic evidence, not represented as green certificates. All completed test runs have clean source snapshots and verified producing builds; the no-build timeout attempt has unknown build provenance as shown.

## Unedited CHECKPOINT lines

Run `20261005-232424-bc98`, actual source `c25196bb72c5abdda2630b636ed8edae4c3f2d51`:

```text
CHECKPOINT CP-1 commit=c25196bb72c5abdda2630b636ed8edae4c3f2d51 build=ok filter=/*/*/(PushProbeOutcomeTests)|(PushCredentialPolicyFileTests)/* executed=0 passed=0 failed=0 skipped=0 trx=/work/worktrees/task-2a876d8a/.antiphon/checkpoints/20261005-232424-bc98/rows/CP-1/run.trx slot=granted waited=0s dirty=0 source=c25196bb72c5abdda2630b636ed8edae4c3f2d51 sourceState=clean buildSource=verified
CHECKPOINT CP-2 commit=c25196bb72c5abdda2630b636ed8edae4c3f2d51 build=reused filter=/*/*/RunnerWorkspaceServiceTests/(Mirror_refuses_a_secondary_repository_the_push_credential_cannot_push_to*)|(Mirror_accepts_a_secondary_repository_when_push_dry_run_succeeds*)|(Mirror_refuses_a_repository_outside_the_allowed_clone_sources*)|(Mirror_of_a_second_repository_clones_beside_the_primary_and_removes_through_its_own_checkout*)|(Mirror_creates_worktree_on_branch_at_sha*)|(Publish_pushes_only_own_fast_forward_branch*) executed=6 passed=6 failed=0 skipped=0 trx=/work/worktrees/task-2a876d8a/.antiphon/checkpoints/20261005-232424-bc98/rows/CP-2/run.trx slot=granted waited=0s dirty=0 source=c25196bb72c5abdda2630b636ed8edae4c3f2d51 sourceState=clean buildSource=verified
```

Run `20261005-232557-a559`, actual source `a293d255f7c0ac7fb689a62fd5ab355fdde8198b`:

```text
CHECKPOINT CP-1 commit=a293d255f7c0ac7fb689a62fd5ab355fdde8198b build=ok filter=/*/*/(PushProbeOutcomeTests*)|(PushCredentialPolicyFileTests*)/* executed=8 passed=8 failed=0 skipped=0 trx=/work/worktrees/task-2a876d8a/.antiphon/checkpoints/20261005-232557-a559/rows/CP-1/run.trx slot=granted waited=0s dirty=0 source=a293d255f7c0ac7fb689a62fd5ab355fdde8198b sourceState=clean buildSource=verified
```

Run `20261005-233256-4494`, actual source `f48c14211cb5013ab90c082aeccf4865f471909e`:

```text
CHECKPOINT CP-3 commit=f48c14211cb5013ab90c082aeccf4865f471909e build=failed filter=/*/*/GithubCredentialHelperTests/* executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a slot=skipped waited=0s dirty=0 source=f48c14211cb5013ab90c082aeccf4865f471909e sourceState=clean buildSource=unknown
CHECKPOINT CP-4 commit=f48c14211cb5013ab90c082aeccf4865f471909e build=failed filter=/*/*/DindRunnerContractTests/* executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a slot=skipped waited=0s dirty=0 source=f48c14211cb5013ab90c082aeccf4865f471909e sourceState=clean buildSource=unknown
CHECKPOINT CP-5 commit=f48c14211cb5013ab90c082aeccf4865f471909e build=failed filter=/*/*/RemoteScriptContractTests/(Deploy_parent_creates_the_github_token_directory_without_reading_it*)|(Deploy_parent_creates_the_codex_home_directory_without_reading_it*)|(Deploy_parent_seeds_or_verifies_runner_checkout*)|(Persistent_restart_ensures_the_identity_file_before_stopping_an_older_runner*)|(C1008_Recycle_exact_default_volumes*)|(Scrub_covers_github_token_prefixes*) executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a slot=skipped waited=0s dirty=0 source=f48c14211cb5013ab90c082aeccf4865f471909e sourceState=clean buildSource=unknown
```

Run `20261005-233525-cec2`, actual source `b88a742e69a635ac0494b19b1642c262991d9b52`:

```text
CHECKPOINT CP-3 commit=b88a742e69a635ac0494b19b1642c262991d9b52 build=ok filter=/*/*/GithubCredentialHelperTests/* executed=6 passed=6 failed=0 skipped=0 trx=/work/worktrees/task-2a876d8a/.antiphon/checkpoints/20261005-233525-cec2/rows/CP-3/run.trx slot=granted waited=0s dirty=0 source=b88a742e69a635ac0494b19b1642c262991d9b52 sourceState=clean buildSource=verified
CHECKPOINT CP-4 commit=b88a742e69a635ac0494b19b1642c262991d9b52 build=reused filter=/*/*/DindRunnerContractTests/* executed=25 passed=25 failed=0 skipped=0 trx=/work/worktrees/task-2a876d8a/.antiphon/checkpoints/20261005-233525-cec2/rows/CP-4/run.trx slot=granted waited=15s dirty=0 source=b88a742e69a635ac0494b19b1642c262991d9b52 sourceState=clean buildSource=verified
CHECKPOINT CP-5 commit=b88a742e69a635ac0494b19b1642c262991d9b52 build=reused filter=/*/*/RemoteScriptContractTests/(Deploy_parent_creates_the_github_token_directory_without_reading_it*)|(Deploy_parent_creates_the_codex_home_directory_without_reading_it*)|(Deploy_parent_seeds_or_verifies_runner_checkout*)|(Persistent_restart_ensures_the_identity_file_before_stopping_an_older_runner*)|(C1008_Recycle_exact_default_volumes*)|(Scrub_covers_github_token_prefixes*) executed=6 passed=6 failed=0 skipped=0 trx=/work/worktrees/task-2a876d8a/.antiphon/checkpoints/20261005-233525-cec2/rows/CP-5/run.trx slot=granted waited=30s dirty=0 source=b88a742e69a635ac0494b19b1642c262991d9b52 sourceState=clean buildSource=verified
```

Run `20261005-234324-11a2`, actual source `d01a9b5d2cc92a9e3f9c5118c6223147024f35c8`:

```text
CHECKPOINT CP-6 commit=d01a9b5d2cc92a9e3f9c5118c6223147024f35c8 build=ok filter=/*/*/(RefreshGithubTokenScriptTests*)|(RunnerPushCredentialDocsTests*)/* executed=3 passed=3 failed=0 skipped=0 trx=/work/worktrees/task-2a876d8a/.antiphon/checkpoints/20261005-234324-11a2/rows/CP-6/run.trx slot=granted waited=30s dirty=0 source=d01a9b5d2cc92a9e3f9c5118c6223147024f35c8 sourceState=clean buildSource=verified
```

Run `20261005-234657-4ebe`, actual source `9da56ce047e2109ad19b958c1be9b5e9d1abc63d`:

```text
CHECKPOINT CP-7 commit=9da56ce047e2109ad19b958c1be9b5e9d1abc63d build=ok filter=/*/*/*/*[Category=Unit] executed=294 passed=273 failed=21 skipped=0 trx=/work/worktrees/task-2a876d8a/.antiphon/checkpoints/20261005-234657-4ebe/rows/CP-7/run.trx slot=granted waited=0s dirty=0 source=9da56ce047e2109ad19b958c1be9b5e9d1abc63d sourceState=clean buildSource=verified
CHECKPOINT CP-8 commit=9da56ce047e2109ad19b958c1be9b5e9d1abc63d build=reused filter=/*/*/RunnerWorkspaceServiceTests/* executed=33 passed=33 failed=0 skipped=0 trx=/work/worktrees/task-2a876d8a/.antiphon/checkpoints/20261005-234657-4ebe/rows/CP-8/run.trx slot=granted waited=0s dirty=0 source=9da56ce047e2109ad19b958c1be9b5e9d1abc63d sourceState=clean buildSource=verified
CHECKPOINT CP-9 commit=9da56ce047e2109ad19b958c1be9b5e9d1abc63d build=ok filter=/*/*/*/*[Category=Unit] executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a timeout=45m slot=granted waited=0s dirty=0 source=9da56ce047e2109ad19b958c1be9b5e9d1abc63d sourceState=clean buildSource=verified
```

Run `20261006-003621-6521`, actual source `9da56ce047e2109ad19b958c1be9b5e9d1abc63d`:

```text
CHECKPOINT CP-17 commit=9da56ce047e2109ad19b958c1be9b5e9d1abc63d build=ok filter=/*/*/PhoneHomeConnectionServiceTests/Websocket_connect_that_hangs_ends_within_the_connect_timeout* executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-2a876d8a/.antiphon/checkpoints/20261006-003621-6521/rows/CP-17/run.trx slot=granted waited=60s dirty=0 source=9da56ce047e2109ad19b958c1be9b5e9d1abc63d sourceState=clean buildSource=verified
```

Run `20261006-000122-8be3`, actual source `71685b84772b82517c2db5dd5d18e085ca8f360a`:

```text
CHECKPOINT CP-10 commit=71685b84772b82517c2db5dd5d18e085ca8f360a build=n/a filter=/*/*/CodexWindowsLaunchPolicyTests/(A_configured_budget_above_30000_does_not_raise_the_transport_ceiling*)|(A_customized_wrapper_is_not_rewritten_and_falls_under_the_batch_ceiling*)|(Explicit_cmd_launcher_is_capped_at_7000*)|(Explicit_node_codex_js_is_left_alone_but_still_budgeted*)|(Install_root_with_spaces_and_unicode_round_trips*)|(Missing_node_never_returns_the_batch_request*)|(Missing_sibling_and_PATH_node_is_codex_launcher_unavailable*)|(Native_hop_is_bounded_by_the_longest_installed_native_path*)|(Nonpositive_budget_is_refused*)|(Nul_in_any_argument_is_refused*)|(PATH_node_is_used_when_no_sibling*)|(Quote_expansion_is_counted_not_estimated*)|(Recognized_shim_without_a_vendored_native_package_is_codex_launcher_unsupported*)|(Serialized_hop1_at_budget_is_accepted_and_plus_one_is_refused*)|(Shim_without_codex_js_is_codex_launcher_unavailable*)|(Sibling_node_wins_over_PATH_node*)|(Standard_npm_shim_becomes_node_exe_plus_codex_js*)|(Surrogate_pairs_count_two_units*) executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a slot=skipped waited=0s dirty=unknown source=unknown sourceState=unknown buildSource=unknown
CHECKPOINT CP-11 commit=71685b84772b82517c2db5dd5d18e085ca8f360a build=n/a filter=/*/*/PhoneHomeConnectionServiceTests/Websocket_connect_that_hangs_ends_within_the_connect_timeout* executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a slot=skipped waited=0s dirty=unknown source=unknown sourceState=unknown buildSource=unknown
CHECKPOINT CP-12 commit=71685b84772b82517c2db5dd5d18e085ca8f360a build=n/a filter=/*/*/RunnerCustodyLedgerBackendTests/Linux_backend_accepted_only_when_probe_passed* executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a slot=skipped waited=0s dirty=unknown source=unknown sourceState=unknown buildSource=unknown
```

Run `20261006-003448-38f4`, actual source `71685b84772b82517c2db5dd5d18e085ca8f360a`:

```text
CHECKPOINT CP-10 commit=71685b84772b82517c2db5dd5d18e085ca8f360a build=ok filter=/*/*/CodexWindowsLaunchPolicyTests/(A_configured_budget_above_30000_does_not_raise_the_transport_ceiling*)|(A_customized_wrapper_is_not_rewritten_and_falls_under_the_batch_ceiling*)|(Explicit_cmd_launcher_is_capped_at_7000*)|(Explicit_node_codex_js_is_left_alone_but_still_budgeted*)|(Install_root_with_spaces_and_unicode_round_trips*)|(Missing_node_never_returns_the_batch_request*)|(Missing_sibling_and_PATH_node_is_codex_launcher_unavailable*)|(Native_hop_is_bounded_by_the_longest_installed_native_path*)|(Nonpositive_budget_is_refused*)|(Nul_in_any_argument_is_refused*)|(PATH_node_is_used_when_no_sibling*)|(Quote_expansion_is_counted_not_estimated*)|(Recognized_shim_without_a_vendored_native_package_is_codex_launcher_unsupported*)|(Serialized_hop1_at_budget_is_accepted_and_plus_one_is_refused*)|(Shim_without_codex_js_is_codex_launcher_unavailable*)|(Sibling_node_wins_over_PATH_node*)|(Standard_npm_shim_becomes_node_exe_plus_codex_js*)|(Surrogate_pairs_count_two_units*) executed=19 passed=0 failed=19 skipped=0 trx=/work/worktrees/task-2a876d8a-baseline/.antiphon/checkpoints/20261006-003448-38f4/rows/CP-10/run.trx slot=granted waited=0s dirty=0 source=71685b84772b82517c2db5dd5d18e085ca8f360a sourceState=clean buildSource=verified
CHECKPOINT CP-11 commit=71685b84772b82517c2db5dd5d18e085ca8f360a build=reused filter=/*/*/PhoneHomeConnectionServiceTests/Websocket_connect_that_hangs_ends_within_the_connect_timeout* executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-2a876d8a-baseline/.antiphon/checkpoints/20261006-003448-38f4/rows/CP-11/run.trx slot=granted waited=0s dirty=0 source=71685b84772b82517c2db5dd5d18e085ca8f360a sourceState=clean buildSource=verified
CHECKPOINT CP-12 commit=71685b84772b82517c2db5dd5d18e085ca8f360a build=reused filter=/*/*/RunnerCustodyLedgerBackendTests/Linux_backend_accepted_only_when_probe_passed* executed=1 passed=0 failed=1 skipped=0 trx=/work/worktrees/task-2a876d8a-baseline/.antiphon/checkpoints/20261006-003448-38f4/rows/CP-12/run.trx slot=granted waited=0s dirty=0 source=71685b84772b82517c2db5dd5d18e085ca8f360a sourceState=clean buildSource=verified
```

Run `20261006-003610-f93f`, actual source `71685b84772b82517c2db5dd5d18e085ca8f360a`:

```text
CHECKPOINT CP-13 commit=71685b84772b82517c2db5dd5d18e085ca8f360a build=ok filter=/*/*/RemoteScriptContractTests/Nested_lane_never_uses_sudo_or_python* executed=1 passed=0 failed=1 skipped=0 trx=/work/worktrees/task-2a876d8a-baseline/.antiphon/checkpoints/20261006-003610-f93f/rows/CP-13/run.trx slot=granted waited=75s dirty=0 source=71685b84772b82517c2db5dd5d18e085ca8f360a sourceState=clean buildSource=verified
CHECKPOINT CP-14 commit=71685b84772b82517c2db5dd5d18e085ca8f360a build=reused filter=/*/*/RetiredTempContainerHostTests/C994_Production_mount_topology_is_proven* executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-2a876d8a-baseline/.antiphon/checkpoints/20261006-003610-f93f/rows/CP-14/run.trx slot=granted waited=15s dirty=0 source=71685b84772b82517c2db5dd5d18e085ca8f360a sourceState=clean buildSource=verified
CHECKPOINT CP-15 commit=71685b84772b82517c2db5dd5d18e085ca8f360a build=reused filter=/*/*/TestClassificationGuardTests/Registry_matches_compiled_metadata* executed=1 passed=0 failed=1 skipped=0 trx=/work/worktrees/task-2a876d8a-baseline/.antiphon/checkpoints/20261006-003610-f93f/rows/CP-15/run.trx slot=granted waited=0s dirty=0 source=71685b84772b82517c2db5dd5d18e085ca8f360a sourceState=clean buildSource=verified
CHECKPOINT CP-16 commit=71685b84772b82517c2db5dd5d18e085ca8f360a build=reused filter=/*/*/C1008PlatformContractTests/(C1050_Windows_rolling_outcomes_are_exact*)|(C1050_Windows_remote_outcomes_are_exact*) executed=2 passed=0 failed=2 skipped=0 trx=/work/worktrees/task-2a876d8a-baseline/.antiphon/checkpoints/20261006-003610-f93f/rows/CP-16/run.trx slot=granted waited=0s dirty=0 source=71685b84772b82517c2db5dd5d18e085ca8f360a sourceState=clean buildSource=verified
```

## Evidence retention and cleanup

Primary receipts remain ignored under `/work/worktrees/task-2a876d8a/.antiphon/checkpoints/<run>/`; each run contains `report.md`, `report.json`, and its completed rows' fresh TRX. CP-9's partial console is `/work/worktrees/task-2a876d8a/.antiphon/checkpoints/20261005-234657-4ebe/rows/CP-9/console.log`.

Baseline run directories were copied unchanged to `/work/worktrees/task-2a876d8a/.antiphon/c0817-baseline-checkpoints/` before the scratch baseline worktree was removed. Their raw receipts retain the historical `/work/worktrees/task-2a876d8a-baseline/` paths; use the matching run/row suffix in the copied evidence. No generated TRX, JSON, log, checkpoint directory or runtime report store was committed.

All checkpoint commands have finished; no owned test run remains active. The checkpoint cleanup utility removed the retained `bin-c0817-final*`, `bin-c0817-base-runner` and `bin-c0817-base-unit` outputs. Green-run outputs had already been removed by the tool. A filesystem check across src, server, tests, tools and scripts found no remaining `bin-c0817-*` directories. The clean detached baseline worktree was removed after evidence retention.

The final publication checks are `git diff --check` and `pwsh -NoProfile -File scripts/check-evidence-diff.ps1 -BaseRef 71685b84772b82517c2db5dd5d18e085ca8f360a -HeadRef HEAD` over the full task history, including this report commit. Their actual result and the remote-confirmed final SHA are stated in the caller-facing final message.

--- next stage ---
next: review
handoff: Review S1-S3 and the green CP-1..CP-6 receipts at their recorded SHAs. The caller stopped further Unit/baseline work and owns Unit qualification; retain F-1/F-2 fixture findings, the CP-9 timeout and F-3 observation. Keep all 20 PCs pending for SourceLanding Mutation after Review and landing of the original Code task.
artifact: docs/superpowers/plans/2026-10-05-card-0817-https-token-push-credential-plan.md
