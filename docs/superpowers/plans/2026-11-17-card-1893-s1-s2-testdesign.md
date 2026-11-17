# CARD-1893 S1 and S2 test design: runner hold lift and guarded broker upgrade

Design only. No production code, no test code, no setting change, no restart, no deployment.
The fix design stays the CLEAN activation plan
`docs/superpowers/plans/2026-11-16-card-1893-activation-plan.md` at
`origin/feat/card-task-a1d414b6` tip `5c1a0d5023b9aada4b62c1ff85d4be42e5b72bbe`
(not on master). Round-1 review `origin/feat/card-task-ee9cdb64` tip
`11fa40e13b766d8255634339f385cef0d3789361` was a blocker. Round-2 review
`origin/feat/card-task-c3619b22` tip `3b349da49848998f526f4a993f702e74339a53a3`
is CLEAN. This file binds the tests and the checkpoint rows. It does not rewrite
D-1 through D-11. Round-1 review `origin/feat/card-task-f29c3d90` tip
`a1c94747a707fe98ec5ed739363e7cefa7bec2fc` blocked on B-1, B-2 and B-3.
This amendment fixes those three. Sections that blocker did not touch stay
as they were at `c88fc182d965b65a8154545f6431d5e7c5289430`.

Files below were read at worktree `321f98ec990a575f77e442ba3b1554f0fde33521`.
`origin/master` at design time is `c3cc0bb9b7b1d0c55464d07cd8e60a63b4239028`.
`git diff --stat` of every cited path between those two SHAs is empty, so the
file:line proofs apply to both. `origin/rc/2026-11-24` is still
`870a93d21f2b18c85a8b0905ab689b28e3099813`. `git merge-base --is-ancestor
9cf3e491705247bc03814e6afed77a6534805dfd origin/rc/2026-11-24` exits 0.
`git grep` of `HistoryPath` and `HistoryEnabled` on that rc SHA in the two
compose files prints nothing. The compose files and `docker/stack/init-state.sh`
have an empty diff from `870a93d21` to this worktree. This amendment
re-read `origin/master` on 2026-11-17; it is still
`c3cc0bb9b7b1d0c55464d07cd8e60a63b4239028`.

Load at the decision not to measure: `/proc/loadavg` was `5.11 5.96 7.55`, then
later `37.83 18.23 11.80`. No Antiphon.Tests timing run was started. The split
below keeps every `EstimatedMinutes` at or under 15, so no row derives a
timeout over the 45-minute advisory (`PlanTableImporter.cs:19`, `:167`;
`3 * 15 = 45` does not warn; `3 * 16 = 48` does).

The activation plan's `C1893ActivationComposeTests` name is not used. Compose
pins extend `DockerStackContractTests`. The plan's whole-class
`RemoteScriptContractTests` row is not used.

## Verification design

### Inspection

- `docker-compose.server2-runner.yml` session-runner environment `:50-108` and
  build-slots service `:169-192`. No `HistoryPath`, `HistoryEnabled`,
  `PhysicalHostKey`, `BindingRevision`, `SourceEpoch`, or `AuthorityId`.
  build-slots environment keys are exactly the eight `ASPNETCORE_URLS` /
  `BuildSlots*` lines at `:177-184`. Maps to V-1, R-1, G-14, G-16, G-17.
- `docker-compose.server2-runner.temp.yml:1-14`. session-runner environment is
  only `PhoneHome__RunnerId` and `ANTIPHON_BUILD_SLOTS_URL`. Maps to V-1, G-13.
- `docker/stack/init-state.sh` mkdir list `:8-15` (ends at `/runner-state/claude`),
  `chmod 0700` only on `/codex-home` at `:46`, `own_tree` `:77-104` chowns and
  does not chmod. Maps to V-1, G-15.
- `scripts/c590-remote.sh` `compose_host` `:953-977` (no `--env-file`),
  `ensure_build_slots_broker` `:1012-1030` (`up -d --no-build build-slots` at
  `:1020`, no `--no-recreate`), `retire_superseded_server2_images` `:1076-1083`
  (grep `^antiphon-server2/(server|session-testing):`), census occupancy
  `:2768-2774` (failed GET becomes `unknown`), case table `:10703-10746`
  (no `upgrade-broker)` arm; default `RealCasePending` at `:10744`). Maps to
  V-2, R-2, G-3 through G-12 and G-18.
- `scripts/deploy-server2.ps1` ValidateSet `:15` and the `Phase=all` array
  `:1547`. No `upgrade-broker`. Maps to V-2, G-12.
- `scripts/c590-real.ps1` `$script:C590LiveCases` `:11-30` and the private-bundle
  condition `:793`. `scripts/verify-docker-stack.ps1` stub arm
  `deploy-temp-runner` at `:203`. No `upgrade-broker`. Maps to V-2.
- `HostLoadCaptureService.cs:63` and `:87` return before a spool open when
  history is disabled. `:99-108` resolves an unset `HistoryPath` to local
  application data, then the user profile. `HostStatsSettings.cs:29` defaults
  `HistoryEnabled` to true. `:49-50` rejects an empty path. `HostStatsRoutes.cs:84`
  answers 404 when history is disabled. `BuildSlotRoutes.cs:73-75` answers
  `Unknown` when the physical key or authority id is blank. `HostLoadHistorySettings.cs:18-21`
  rejects a blank physical key, an empty epoch, or a revision below 1.
  These are why the compose literals matter. The tests assert the compose and
  the scripts, not these C# methods.
- `DockerStackContractTests` (117 `[Test]` methods) and
  `DockerStackDocumentationTests` (19). Existing pins that must stay green are
  named under R-1. `RemoteScriptContractTests` is one class, 231 `[Test]`
  methods, `[Timeout(600_000)]` at `RemoteScriptContractTests.cs:18`. Its
  `Block` extractor is `:9468`. `ensure_build_slots_broker` hits in that class
  are stubs that replace the function (`:3036`, `:3973`, `:3985`, `:4079`,
  `:4184`, `:7514`, `:7630`). `LinuxShell` is
  `RemoteScriptContractSupport.cs:50`. `C1893CollectorDiagnosticsTests` already
  exists under `tests/Antiphon.SessionRunner.Tests` and is not this work.
- `docs/docker-stack.md` and `docs/ops-http.md` do not contain `/state/host-load`,
  `HistoryEnabled: "false"`, or `upgrade-broker`. `docs/ops-http.md:1008-1010`
  names the HostStats setting keys only. `docs/testing-and-build.md:1291-1298`
  already describes `HistoryPath` and is not the new hold sentence.
  `:1341` still says an operator moves the pin. Maps to V-1 and V-2.
- Checkpoint importer: first `### Checkpoints` table only; advisory at
  `PlanTableImporter.cs:167`; a reused build must share `After`
  (`:214-223`); a second distinct `bin-` path for the same project, After, and
  Repeat warns (`:240-256`). Output paths must not contain the substring `ref`.

### Delivery inventory

No new asynchronous producer, queue, or session input. The hold check and
`upgrade-broker` are synchronous script gates. The observable receipt is the
process result: stdout exactly `HOLD`, `RELEASED`, or `NOT-AFFECTED` (hold
script, exit 0), or empty stdout and a non-zero exit when either commit does
not resolve, or the remote-script diagnosis `BrokerUpgradeBusy` (exit 2)
versus a rewritten `BUILD_SLOTS_SHA12` line plus the recorded `docker tag` and
`compose up` argv (upgrade). A docker ack, a Sent flag, or a queue insert is
not the receipt. Parking does not apply. Tests that stop at an acknowledgement
are not accepted.

### Recipe, variants, case, oracle, reachability, classification

- Recipe: each new `[Test]` calls the production file (compose text, `git show`
  through `scripts/c1893-runner-hold.sh`, or the real `c590-remote.sh` /
  `deploy-server2.ps1` case). A copy of the grep pasted into the test and
  asserted against today's rc is green once written and is forbidden.
- Variants: hold fixtures are both lines, only the standing path, only the temp
  disable, misspelled or unquoted lines, an unrelated commit whose ancestor
  object is present, and an argument that does not resolve. Upgrade fixtures
  are occupied, waiters, failed GET, unparsable body, `unknown`, second read
  busy, success, and verify failure.
- Case: one `[Test]` per method name below. No `[Arguments]`. One execution
  counts as 1 toward `Min`.
- Oracle: the assertion named on that method. Setup failure, a missing file
  before the named assertion, a timeout, or zero tests is not the red.
- Reachability: at this base the production script or the compose line is
  absent, so the named assertion fails. After Code adds the line, the same
  assertion passes. A positive control then breaks that line and the same
  assertion fails again.
- Classification: `[Category("Unit")]` on the class. Not `Slow`, not
  `Integration`, not added to `tests/Antiphon.Tests/slow-tests-allowlist.txt`.
  Methods that start bash take `[ParallelLimiter<ProcessSpawnLimit>]`. No
  class-level `[Timeout(600_000)]`.

### Proves it works now

- V-1: standing runner has absolute `HistoryPath` `/state/host-load` and literal
  identity; temp sets `HistoryEnabled` `"false"` and no path; init creates
  `/runner-state/host-load` and `chmod 0700`; the hold script prints `HOLD` for
  `870a93d21f2b18c85a8b0905ab689b28e3099813` and `RELEASED` only when both exact
  lines are in that commit; `NOT-AFFECTED` only when `is-ancestor` exits 1;
  an unresolved argument exits non-zero with none of the three words; S1 leaves
  build-slots without HostStats keys; docs
  name the hold. | contract + script | CP-1, CP-2, then CP-3 | first CP-1 and
  CP-2 run red at the assertions below; green rerun 0 failed; CP-3 0 failed.
- V-2: `ensure_build_slots_broker` passes `--no-recreate` and still creates an
  absent broker; `upgrade-broker` is outside `Phase=all`; a busy, failed,
  unparsable, or `unknown` read changes nothing; the immediate second read can
  stop the recreate; success rewrites only `BUILD_SLOTS_SHA12` and tags
  `antiphon-server2/broker-rollback:<old>`; verify failure restores the old pin;
  broker identity literals equal the runner. | script + compose | CP-4, CP-5,
  CP-6, then CP-7 | first CP-4, CP-5, and CP-6 run red; green rerun 0 failed;
  CP-7 0 failed.

### Guards the regression

- R-1: existing compose and doc pins stay true. | CP-3 and CP-7 run
  `DockerStackContractTests` and `DockerStackDocumentationTests` whole.
  Decisive assertions already in those classes include
  `Server2_file_defines_runner_state_init_and_profiled_broker` (the top-level
  key list, `DockerStackContractTests.cs:269`),
  `Temp_runner_override_keeps_project_state_separate` (`:284`),
  `Server2_broker_is_unprivileged_and_pinned_separately` (`:365`), and
  `DockerStackDocumentationTests` pins of `-Phase check-census` and
  `-Phase check-host-jq` (`DockerStackDocumentationTests.cs:12`). Adding
  environment keys does not by itself break those. Deleting a pinned sentence
  does. S1/S2 doc edits are additive.
- R-2: `--no-recreate` still creates a broker when `compose ps -q` is empty. |
  `C1893_Ensure_broker_passes_no_recreate_and_creates_when_absent` asserts the
  flag and that the up argv was still issued. The flag half is false today
  (`c590-remote.sh:1020`). The create half is true today and is not a test by
  itself.

### S1 methods

Extend `tests/Antiphon.Tests/Infrastructure/DockerStackContractTests.cs`.
Do not add a compose class. Read files with the class's existing `Text` /
`DockerStackDocuments.Service` helpers.

`C1893_S1_Standing_runner_sets_absolute_history_path`. The session-runner
service contains the exact line `SessionRunner__HostStats__HistoryPath: /state/host-load`,
and that value contains no `${`. The same method asserts the build-slots
environment key set is exactly `ASPNETCORE_URLS`, `SessionRunner__BuildSlotsOnly`,
`SessionRunner__BuildSlots__MaxConcurrent`, `SessionRunner__BuildSlots__MaxCpuCount`,
`SessionRunner__BuildSlots__MinAvailableMemoryMb`, `SessionRunner__BuildSlots__HolderLiveness`,
`SessionRunner__BuildSlots__RenewGraceSeconds`, `SessionRunner__BuildSlots__RenewEverySeconds`,
and that the build-slots service contains no `SessionRunner__HostStats__`.
Red today because the HistoryPath line is absent (`docker-compose.server2-runner.yml:108`
is `GIT_CONFIG_GLOBAL`, the end of the session-runner environment). The key-set
half is true today (`:177-184`) and must not be split into its own test.
Why it matters: an unset path resolves to local application data
(`HostLoadCaptureService.cs:99-106`), which on this image is container-layer
home, not the `runner-state` volume. S2 Code changes this method's expected
key set to those eight plus the three identity keys below, and still forbids
`HistoryPath`, `HistoryEnabled`, `SourceEpoch`, and `${` on build-slots.
Until that S2 edit, the S1 commit's copy of this method is the witness that S1
did not change build-slots, so `ensure_build_slots_broker` cannot recreate it.

`C1893_S1_Standing_runner_identity_literals`. The session-runner service
contains `SessionRunner__HostStats__PhysicalHostKey: server2`,
`SessionRunner__HostStats__BindingRevision: "1"`,
`SessionRunner__HostStats__AuthorityId: server2-build-slots`, and
`SessionRunner__HostStats__SourceEpoch:` whose value matches
`^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$`.
None of the four values contain `${`. S1 Code generates the GUID once and
pastes it; this design does not pick it. The landed compose literal is the
source of truth. Red today: none of those keys exist in `:50-108`. A blank
key, an empty epoch, or a revision below 1 is an invalid server binding
(`HostLoadHistorySettings.cs:18-21`). `compose_host` passes no `--env-file`
(`c590-remote.sh:974-976`), so an interpolated value would bind empty.

`C1893_S1_Temp_runner_disables_history`. The temp session-runner service
contains `SessionRunner__HostStats__HistoryEnabled: "false"` and does not
contain `HistoryPath`. Red today: `docker-compose.server2-runner.temp.yml:4-7`
sets only the runner id and the broker URL. The default is history on
(`HostStatsSettings.cs:29`), and `ExecuteAsync` returns before opening a spool
only when `HistoryEnabled` is false (`HostLoadCaptureService.cs:87`).

`C1893_S1_Init_state_creates_host_load_and_chmod_0700`. The mkdir list includes
`/runner-state/host-load`, and a `chmod 0700` applies to that path. The codex
`chmod 0700` at `init-state.sh:46` does not satisfy this. The init path is
`/runner-state/host-load` (the state-init mount). The runner path is
`/state/host-load`. Assert both strings in their own files. Red today: the
mkdir list ends at `/runner-state/claude` (`:12`) and `own_tree` only chowns
(`:77-104`).

Hold script. S1 adds `scripts/c1893-runner-hold.sh`. It is the activation
plan's §7.0 check, except the snippet's bare `else echo NOT-AFFECTED` is not
copied: that `else` treats every non-zero `git merge-base --is-ancestor`
status, including 128, as `NOT-AFFECTED`. The script does not run `git fetch`.
The caller runs `git fetch origin` in that checkout before the script.
`docs/docker-stack.md` says so. Usage: `scripts/c1893-runner-hold.sh <40-hex>`.
It uses `git cat-file`, `git merge-base`, and `git show "$X:<path>"` in the
git directory it is run from, not the working tree. The ancestor is
`9cf3e491705247bc03814e6afed77a6534805dfd`.

Resolution, before the ancestor test. `git cat-file -e "$X^{commit}"` and
`git cat-file -e "9cf3e491705247bc03814e6afed77a6534805dfd^{commit}"`. If
either exits non-zero, the script exits non-zero and writes none of `HOLD`,
`RELEASED`, or `NOT-AFFECTED`. Stdout is empty. Git's `fatal` stays on stderr.
It does not run `is-ancestor`. Measured: `git cat-file -e` of
`0000000000000000000000000000000000000000^{commit}` exits 128.

Ancestor test, only after both resolve. `git merge-base --is-ancestor
9cf3e491705247bc03814e6afed77a6534805dfd "$X"`. Exit 0 means the ancestor is
contained: `RELEASED` only when both greps match, otherwise `HOLD`, and the
script exits 0. Exit 1 exactly means `NOT-AFFECTED` and exit 0. Any other
status, including 128, is the same error as a failed `cat-file`: non-zero,
empty stdout, none of the three words. Measured: `is-ancestor` against the
missing all-zero name exits 128.

```
^ +SessionRunner__HostStats__HistoryPath: /state/host-load$
^ +SessionRunner__HostStats__HistoryEnabled: "false"$
```

A missing script is non-zero and does not print those words. The tests invoke
the file through `RemoteScriptContractSupport.LinuxShellResult`
(`RemoteScriptContractSupport.cs:53` returns stdout only; stderr is not the
receipt) with `[ParallelLimiter<ProcessSpawnLimit>]`. They do not reimplement
the grep.

Fixture rule. Do not `git fetch` a SHA without `--depth 1`. This worktree and
`/work/repos/antiphon` are blob:none promisor clones
(`remote.origin.promisor=true`, `partialclonefilter=blob:none`). A fetch of
`9cf3e4917` without `--depth 1` aborts with `possible repository corruption
on the remote side` after 19 to 47 s (round-1 review, measured). `--depth 1`
works. Measured this session at loadavg `11.52 22.15 21.96`: `git fetch
--no-tags --depth 1` of `9cf3e491705247bc03814e6afed77a6534805dfd` took 3.389 s
from this worktree and 4.458 s from `/work/repos/antiphon`. The review measured
8 s from a worktree and 3 s from the mirror. Budget each fixture method at
one 8 s fetch plus local commits.

The six fixture methods each do one `git init`, then one `git fetch --no-tags
--depth 1 <DelegateScriptRunner.RepoRoot>
9cf3e491705247bc03814e6afed77a6534805dfd`, then only synthetic commits. Fetch
from the repo root, not from GitHub. Never commit the fixture on the task
branch and never push it. A descendant commit changes only the two compose
files, then the worktree is dirtied so it disagrees with `git show`.
`is-ancestor` on a child of that shallow root exits 0 (measured). The
misspelled method fetches once and then builds its four trees as four
synthetic commits in that same repo. It does not fetch four times.

`C1893_S1_Hold_check_prints_HOLD_on_rc_2026_11_24`. No fixture fetch. Run the
script in this checkout with `870a93d21f2b18c85a8b0905ab689b28e3099813`.
Stdout `HOLD`, exit 0. Red today because `scripts/c1893-runner-hold.sh` does
not exist. Confirmed at design time: that SHA is an ancestor of this worktree,
it contains `9cf3e4917`, and neither grep matches. `git show` of its blobs
works on a blob:none clone.

`C1893_S1_Hold_releases_when_both_exact_lines_are_present`. One depth-1 fetch,
then one synthetic child with both exact lines, worktree then dirtied. Stdout
`RELEASED`, exit 0. Red today because the script does not exist.

`C1893_S1_Hold_stays_when_only_the_standing_path_is_present`. One depth-1
fetch, then one synthetic child with only the HistoryPath line. Stdout `HOLD`,
exit 0. Red today because the script does not exist.

`C1893_S1_Hold_stays_when_only_the_temp_disable_is_present`. One depth-1
fetch, then one synthetic child with only `HistoryEnabled: "false"` on the
temp file. Stdout `HOLD`, exit 0. Red today because the script does not exist.

`C1893_S1_Hold_stays_when_the_lines_are_misspelled_or_unquoted`. One method,
four trees, still one `[Test]` and one depth-1 fetch: unquoted `false`,
`"False"`, a trailing slash on the path, and the two lines swapped across the
files. Each stdout is `HOLD`. Red today because the script does not exist.

`C1893_S1_Hold_prints_NOT_AFFECTED_when_the_ancestor_is_absent`. One depth-1
fetch so the ancestor object is present, then `git checkout --orphan` and one
empty commit. The argument is that orphan. Both `cat-file -e` calls exit 0.
`is-ancestor` exits 1 (measured). Stdout `NOT-AFFECTED`, exit 0. Red today
because the script does not exist. A `git init` repo whose only commit does
not contain that ancestor object is not this fixture: `is-ancestor` there
exits 128, and printing `NOT-AFFECTED` for that 128 is the fail-open defect.

`C1893_S1_Hold_exits_nonzero_when_the_argument_does_not_resolve`. One depth-1
fetch so the ancestor object resolves, then the argument
`0000000000000000000000000000000000000000`. Exit non-zero. Stdout is empty, so
it does not contain `HOLD`, `RELEASED`, or `NOT-AFFECTED`. Red today because
the script does not exist. Mapping that 128 to `NOT-AFFECTED` fails this
method. That mutant is PC-21.

`C1893_S1_Docs_name_the_hold_and_the_durable_path` in
`DockerStackDocumentationTests`. `docs/docker-stack.md` contains
`/state/host-load`, the token `HOLD`, and the phase names `deploy-temp`,
`redeploy-old`, and `all`. The same file says the caller runs `git fetch
origin` before `scripts/c1893-runner-hold.sh`, and that the script itself does
not fetch. `docs/ops-http.md` says the temp runner sets `HistoryEnabled` false
and sets no `HistoryPath`. Red today: neither file contains `/state/host-load`,
and `docs/docker-stack.md` does not name `scripts/c1893-runner-hold.sh`. Do not
delete the sentences the existing 19 doc tests pin.

### S2 methods

`C1893_S2_Broker_identity_literals_match_the_runner` in `DockerStackContractTests`.
The build-slots values of `PhysicalHostKey`, `BindingRevision`, and `AuthorityId`
equal the session-runner literals (`server2`, `"1"`, `server2-build-slots`),
contain no `${`, and the build-slots service contains no `HistoryPath`.
Red today: build-slots environment `:176-184` has none of those keys. Blank
host or id makes `GET /build-slots/host-load` return `Unknown`
(`BuildSlotRoutes.cs:73-75`). This method is not part of the S1 filter. S2 Code
also updates the key set inside `C1893_S1_Standing_runner_sets_absolute_history_path`
as specified above, in the S2 commit, before CP-7.

`C1893_S2_Docs_name_upgrade_broker` in `DockerStackDocumentationTests`.
`docs/docker-stack.md` names `upgrade-broker` and says it is not in `Phase=all`.
`docs/testing-and-build.md` contains `upgrade-broker` in the paragraph that is
today `:1341` ("needs an operator to move the pin"). Keep the CARD-1489 facts
around that sentence. Red today: the token `upgrade-broker` is absent from
both files. No current test contains that operator sentence.

New class `tests/Antiphon.Tests/Scripts/C1893BrokerUpgradeScriptTests.cs`.
`[Category("Unit")]`. Copy the private `Block` extractor
(`RemoteScriptContractTests.cs:9468`) into this class. Do not add methods to
`RemoteScriptContractTests`. The eight `C1893_Upgrade_*` methods and
`C1893_Rollback_tag_is_outside_retire_superseded_server2_images` run the
injected script below. They do not use `LinuxShell`. They take
`[ParallelLimiter<ProcessSpawnLimit>]`. Do not call `C1008HostFixture.Run`:
it sets `C590_SHA` to forty `a`s (`RollingVolumeRecycleScriptTests.cs:3501`)
and its docker fake answers `exec` only for `stat` and `sh`
(`scripts/fixtures/c1008-fake-docker.sh:552-563`), so `docker exec … curl`
never returns a body.

Harness, shared by those nine methods. Read `scripts/c590-remote.sh` and pass
it through `C1008HostFixture.InsertSetupAtEntry`
(`RollingVolumeRecycleScriptTests.cs:3552`). That inserts at the one column-0
`trap 'ec=$?;` (`c590-remote.sh:10654`), which is after the hard-coded
assignments at `:18-21` and `:904-907`. The injection therefore replaces
`SERVER2_ROOT`, `ROOT`, `EVIDENCE_ROOT`, `CASE_DIR`, `SERVER2_ENV`, and
`SERVER2_TEMP_ENV`. A block prepended to a copy cannot: `:18-21` and `:906`
run later and overwrite it. The injection stubs `ensure_checkout`,
`ensure_dirs`, and `detect_lane` (`LANE=host`), replaces `compose_host` with
`docker compose -p "$HOST_PROJECT" "$@"`, and defines `sleep` as a counter
that returns. Production calls `sleep`, not `command sleep`, so the function
applies. Leave `C1008_CONTEXT` unset. Do not stub `curl` or `jq`. Real `jq`
parses the body the docker fake prints.

Set the process environment before bash starts, because `:13-15` reads it
before the trap: `C590_CASE=upgrade-broker`, `C590_RUN=c1893fixture`,
`C590_SHA=abcdefabcdefabcdefabcdefabcdefabcdefabcd` (sha12 `abcdefabcdef`),
`C590_REEXEC=1`. `C590_REEXEC=1` skips `:10696-10701`, so `ensure_checkout`
(`:143-153`) does not fetch or `reset --hard` `/work/repos/antiphon`, and the
script does not `exec` that mirror's copy (which would drop the injection).
The docker fake is a shell script on `PATH` and the body of the `docker`
function, the same two bindings as `:3434-3465`. Seed
`$fixture/main.env`, which the injection assigns to `SERVER2_ENV`, as exactly
`BUILD_SLOTS_SHA12=oldoldoldold\nUNRELATED=keep\n`.

Why this reaches the real case at this base. `upgrade-broker` matches none of
`:10675-10692`, so `:10693` calls the stubbed `ensure_dirs` (no `chown` of
`/work`). `:10703` has no `upgrade-broker)` arm, so `:10744-10745` runs
`write_result false "RealCasePending upgrade-broker"`. `write_result`
(`:109-117`) writes `$CASE_DIR/c590-result.json`. `CASE_DIR` is the fixture's
`$EVIDENCE_ROOT/upgrade-broker`. Each method asserts that path is under the
fixture root. That assertion passes at base with this harness. If it fails,
the run did not reach `:10744` through the redirected script (a prepend, a
missing `C590_REEXEC=1`, or an `exec` of the mirror) and the failure is not
the named red.

The docker fake records every argv. `compose` with `ps` and `build-slots`
prints `brokerfixture`. `exec` whose argv contains `curl` and
`http://127.0.0.1:8080/build-slots/host-load` prints the verify body. That
match wins, because the host-load URL also contains `/build-slots`. `exec`
whose argv contains `curl` and `http://127.0.0.1:8080/build-slots` prints the
next queued occupancy body and the sleep counter. `tag` and `compose` `up`
are recorded and exit 0. `inspect` of health prints `healthy`. A `curl` on
`PATH` is never the occupancy read: the census shape is `docker exec` then
`curl` (`c590-remote.sh:2771-2772`), and D-6 uses that same shape. At base the
pending arm returns before any docker call, so a trace assertion that requires
`exec` plus `curl` is red, and the diagnosis in the fixture result file is
`RealCasePending upgrade-broker`, not `BrokerUpgradeBusy`.

`C1893_Upgrade_refuses_when_occupied_is_nonzero`. Queued body
`{"occupied":1,"waiters":[]}`, exit 0. Diagnosis `BrokerUpgradeBusy`, exit 2,
env bytes unchanged, trace has `exec` and `curl` and has no `up` and no `tag`.
Red today through the harness: the fixture result says
`RealCasePending upgrade-broker`, and the trace has no `exec`. Env unchanged
and no `up` are true today and are not the red.

`C1893_Upgrade_refuses_when_waiters_are_nonempty`. Body
`{"occupied":0,"waiters":[{"pid":1}]}`, exit 0. Same unchanged result, and the
trace has `exec` and `curl`. Red today the same way: diagnosis
`RealCasePending upgrade-broker`, no `exec`.

`C1893_Upgrade_treats_failed_get_as_busy`. The queued `docker exec` `curl`
exits non-zero and prints nothing. Must not treat the empty body as idle.
Same unchanged result, trace has `exec` and `curl`. Red today the same way.
The census helper maps a failed GET to `unknown` (`c590-remote.sh:2771-2772`);
this phase must not treat that as idle.

`C1893_Upgrade_treats_unparsable_body_as_busy`. Body `not-json`, exit 0. Real
`jq` fails. Same unchanged result, trace has `exec` and `curl`. Red today the
same way.

`C1893_Upgrade_treats_unknown_occupancy_as_busy`. Body
`{"occupied":"unknown","waiters":[]}`, exit 0. Same unchanged result, trace
has `exec` and `curl`. Red today the same way.

`C1893_Upgrade_rechecks_immediately_and_stops_if_the_second_read_is_busy`.
First body idle (`occupied` 0, `waiters` []), second body `occupied` 1. The
fake records `sleep=<count>` on each occupancy response. The second response
has `sleep=0`. Diagnosis `BrokerUpgradeBusy`, no `up`, no `tag`, env
unchanged, trace has two `exec` `curl` occupancy reads. Red today the same
way: diagnosis `RealCasePending upgrade-broker`, and the trace has no
occupancy read.

`C1893_Upgrade_success_moves_only_the_pin_line`. Both occupancy reads idle.
Trace contains `docker tag` of `antiphon-server2/broker-rollback:oldoldoldold`
and a compose up whose argv contains `--force-recreate`, `--no-build`, and
`build-slots`. The env file is exactly
`BUILD_SLOTS_SHA12=abcdefabcdef\nUNRELATED=keep\n` (no second pin line,
`UNRELATED` still present). The host-load `docker exec` `curl` prints
`{"id":"server2-build-slots","physicalHostKey":"server2","revision":1,"state":"known"}`
and exits 0 (`HostAdmissionSnapshot`, `HostLoadContracts.cs:20-22`). Red
today through the harness: diagnosis `RealCasePending upgrade-broker`, env
still the seed, no `tag`.

`C1893_Upgrade_restores_the_old_pin_when_verify_fails`. Both occupancy reads
idle, up runs, then the host-load `docker exec` `curl` exits non-zero and
prints nothing (the non-200 side; a `state` other than `known` is the same
restore branch and is not a second test). Env file returns to the seed bytes.
A second up uses the rollback tag. `UNRELATED=keep` remains. Red today through
the harness: diagnosis `RealCasePending upgrade-broker`, and the trace has no
`tag` and no second `up`. The final env bytes match the seed today, so that
half is not the red.

`C1893_Ensure_broker_passes_no_recreate_and_creates_when_absent`. Extract the
real `ensure_build_slots_broker` (it exists). Stub `compose_host` to record
argv and return an empty `ps -q`. The recorded up argv contains `--no-recreate`
and `build-slots`, and the up line was issued. Red today at `:1020`, which has
no `--no-recreate`. The issued-up half is true today; the flag half is the
decisive failure.

`C1893_Upgrade_broker_is_outside_phase_all`. `deploy-server2.ps1` ValidateSet
`:15` contains `upgrade-broker`, the `Phase=all` array at `:1547` does not,
and `c590-remote.sh` has an `upgrade-broker)` arm before the default `*)`.
Red today because `:15` omits the token. The absence from `:1547` is true
today and is not a standalone assertion.

`C1893_Live_roster_and_stub_name_upgrade_broker`. `$script:C590LiveCases`
(`c590-real.ps1:11`) contains `upgrade-broker`. The private-bundle condition
at `:793` contains it. `verify-docker-stack.ps1` has an `upgrade-broker` switch
arm whose body does not call `docker` or `ssh`. Red today because those strings
are absent. Do not execute live SSH. Do not add `upgrade-broker` to the host-jq
phase list in `scripts/fixtures/c727-fake-verify.ps1:30` or
`HostJqPrerequisiteScriptTests.cs:924`. Those lists are the five rollout phases
plus the jq phases, and this phase is not one of them. Do not edit
`scripts/test-deploy-server2.ps1`; its ValidateSet is the script's own `-Only`
switch, not the deploy phases.

`C1893_Rollback_tag_is_outside_retire_superseded_server2_images`. Same harness
as the success method, including the fixture result file. On that path the
tag argument is `antiphon-server2/broker-rollback:oldoldoldold`, and
`retire_superseded_server2_images` still greps only
`^antiphon-server2/(server|session-testing):` (`c590-remote.sh:1082-1083`).
That tag does not match the grep. Red today through the harness: diagnosis
`RealCasePending upgrade-broker`, and the trace has no `tag`. The grep half
is true today and is not a standalone assertion.

### Guard inventory

- G-1: §7.0 releases only when both exact lines match (D-4 and D-5 together) | PC-1
- G-2: a descendant without those lines prints `HOLD`, not `RELEASED` | PC-2
- G-3: `ensure_build_slots_broker` passes `--no-recreate` (H-2, D-6) | PC-3
- G-4: `occupied != 0` is busy; nothing is tagged or recreated | PC-4
- G-5: a non-empty `waiters` list is busy | PC-5
- G-6: a failed GET is busy, including an empty body | PC-6
- G-7: an unparsable body is busy | PC-7
- G-8: `unknown` occupancy is busy | PC-8
- G-9: the immediate second read can stop a recreate that the first read allowed | PC-9
- G-10: success changes only the `BUILD_SLOTS_SHA12` line | PC-10
- G-11: verify failure restores the old pin and recreates from the rollback tag | PC-11
- G-12: `upgrade-broker` is not in the `Phase=all` array (`deploy-server2.ps1:1547`) | PC-12
- G-13: temp `HistoryEnabled` is the quoted string `false` and temp sets no path (D-4) | PC-13
- G-14: standing `HistoryPath` is the absolute literal `/state/host-load` (D-5) | PC-14
- G-15: init creates `/runner-state/host-load` and `chmod 0700` on it (D-5) | PC-15
- G-16: S1 does not put HostStats keys on build-slots (D-9) | PC-16
- G-17: broker key, revision, and authority id are the runner literals and are not blank or interpolated (D-3) | PC-17
- G-18: the rollback tag is `antiphon-server2/broker-rollback:`, which `retire_superseded_server2_images` does not select | PC-18
- G-19: `NOT-AFFECTED` only when both commits resolve and `is-ancestor` exits exactly 1; inverting that test would print `NOT-AFFECTED` for rc-24 and the phase would run | PC-19
- G-20: an unquoted or differently spelled disable stays `HOLD` | PC-20
- G-21: an unresolved argument or an unresolved ancestor object exits non-zero and prints none of `HOLD`, `RELEASED`, `NOT-AFFECTED`; mapping git exit 128 to `NOT-AFFECTED` would let an unfetched descendant pass the hold | PC-21

### Positive controls

Operator 2026-11-12 waived mutation execution. These controls are designed and
owed. TestDesign does not run them. Code does not run them. Mutation, after
land, runs each one method-scoped: break, confirm the named assertion red,
restore, confirm green. A build error, a timeout, or zero tests is not red.
Each filter is `/*/*/Class/ExactMethod`.

- PC-1: change the `&&` between the two hold greps to `||`. Expect
  `C1893_S1_Hold_stays_when_only_the_standing_path_is_present` and
  `C1893_S1_Hold_stays_when_only_the_temp_disable_is_present` red (stdout
  `RELEASED`).
- PC-2: swap the `HOLD` and `RELEASED` echoes. Expect
  `C1893_S1_Hold_check_prints_HOLD_on_rc_2026_11_24` red (stdout `RELEASED`).
- PC-3: delete `--no-recreate` from the `ensure_build_slots_broker` up line
  (restore the `:1020` text). Expect
  `C1893_Ensure_broker_passes_no_recreate_and_creates_when_absent` red at the
  flag assertion.
- PC-4: treat `occupied > 0` as idle. Expect
  `C1893_Upgrade_refuses_when_occupied_is_nonzero` red (trace contains `tag`
  or `up`).
- PC-5: skip the waiters check. Expect
  `C1893_Upgrade_refuses_when_waiters_are_nonempty` red the same way.
- PC-6: treat a failed or empty GET as idle. Expect
  `C1893_Upgrade_treats_failed_get_as_busy` red.
- PC-7: treat an unparsable body as idle. Expect
  `C1893_Upgrade_treats_unparsable_body_as_busy` red.
- PC-8: treat `unknown` as idle. Expect
  `C1893_Upgrade_treats_unknown_occupancy_as_busy` red.
- PC-9: skip the second read. Expect
  `C1893_Upgrade_rechecks_immediately_and_stops_if_the_second_read_is_busy` red
  (up runs after the first idle read).
- PC-10: rewrite the whole env file, or append a second `BUILD_SLOTS_SHA12`
  line. Expect `C1893_Upgrade_success_moves_only_the_pin_line` red (`UNRELATED`
  missing, or two pin lines).
- PC-11: on verify failure leave the new pin. Expect
  `C1893_Upgrade_restores_the_old_pin_when_verify_fails` red (pin stays
  `abcdefabcdef`).
- PC-12: add `upgrade-broker` to the array at `deploy-server2.ps1:1547`. Expect
  `C1893_Upgrade_broker_is_outside_phase_all` red.
- PC-13: drop temp `HistoryEnabled` or leave it unquoted. Expect
  `C1893_S1_Temp_runner_disables_history` red.
- PC-14: misspell `/state/host-load` or interpolate it. Expect
  `C1893_S1_Standing_runner_sets_absolute_history_path` red.
- PC-15: mkdir `/runner-state/host-load` and do not chmod it (leave `own_tree`
  as the only owner step). Expect
  `C1893_S1_Init_state_creates_host_load_and_chmod_0700` red.
- PC-16: add `SessionRunner__HostStats__HistoryPath` on the build-slots service.
  Expect `C1893_S1_Standing_runner_sets_absolute_history_path` red at the
  build-slots key-set assertion (after the S2 update, that set still forbids
  `HistoryPath`).
- PC-17: leave broker `AuthorityId` blank or set it through `${...}`. Expect
  `C1893_S2_Broker_identity_literals_match_the_runner` red.
- PC-18: tag `antiphon-server2/session-testing:oldoldoldold` instead of
  `antiphon-server2/broker-rollback:oldoldoldold`. Expect
  `C1893_Rollback_tag_is_outside_retire_superseded_server2_images` red. That
  tag matches the retire grep.
- PC-19: invert the ancestor `if`. Expect
  `C1893_S1_Hold_check_prints_HOLD_on_rc_2026_11_24` red (stdout `NOT-AFFECTED`).
  This is a different mutant and a different actual from PC-2.
- PC-20: loosen the HistoryEnabled grep so unquoted `false` matches. Expect
  `C1893_S1_Hold_stays_when_the_lines_are_misspelled_or_unquoted` red.
- PC-21: when `git cat-file -e` or `is-ancestor` exits 128, echo `NOT-AFFECTED`
  and exit 0. Expect
  `C1893_S1_Hold_exits_nonzero_when_the_argument_does_not_resolve` red
  (stdout `NOT-AFFECTED`, exit 0). This is a different mutant from PC-19.

guards=21, mapped=21, missing=0, duplicate PC maps=0.

### Out of scope

Server user-secrets, the AppHost restart, the live server2 rollout, evidence
E-0 through E-7, the D-11 cursor delete, H-3 gap handling, and the Q-8 S0
fallback (operator answer was no). No edit to `scripts/c1609-host-proof.ps1`
or `tests/Antiphon.Tests/Scripts/VolumeRetirementHostProofTests.cs`. No live
SSH. `RemoteScriptContractTests` is not edited and is not a checkpoint row:
the class timeout is 600000 ms, the CARD-1514 profile recorded driver wall
587.19 s with exit 124 and no TRX, and the existing broker hits stub the
function, so adding `--no-recreate` does not turn them red. Do not schedule
`/*/*/RemoteScriptContractTests/*`. `C1893CollectorDiagnosticsTests` is the
collector plan's class and is not rerun here.

### Existing tests affected

`DockerStackContractTests` and `DockerStackDocumentationTests` gain the methods
above and are the R-1 rows. `C1893BrokerUpgradeScriptTests` is new.
`RemoteScriptContractTests`, `HostJqPrerequisiteScriptTests`, and the c727
fake-verify phase list are not edited. No migration, public DTO, route,
operation label, or statement budget. `scripts/impacted-classes.ps1` on these
paths returned empty kinds in the activation plan; an empty kinds result is
not permission to hand-edit `tests/derived-impact-index.json`. Derived readers
named in that plan run at Merge, not in these rows.

CARD-1782 classes touched by the new tests: none beyond the two DockerStack
classes and the new script class. The new class inherits Unit and, on the bash
methods, `ProcessSpawnLimit`.

### File lists and collisions

S1 may edit only:

- `docker-compose.server2-runner.yml` session-runner environment. The build-slots
  service stays byte-identical in the S1 commit.
- `docker-compose.server2-runner.temp.yml`
- `docker/stack/init-state.sh`
- `docs/docker-stack.md`
- `docs/ops-http.md`
- `scripts/c1893-runner-hold.sh` (new)
- `tests/Antiphon.Tests/Infrastructure/DockerStackContractTests.cs`
- `tests/Antiphon.Tests/Infrastructure/DockerStackDocumentationTests.cs`

S2 may edit only:

- `docker-compose.server2-runner.yml` build-slots environment (the three identity
  keys; no `HistoryPath`, no `SourceEpoch`)
- the S1 key-set assertion inside `DockerStackContractTests`, plus
  `C1893_S2_Broker_identity_literals_match_the_runner`
- `scripts/c590-remote.sh`
- `scripts/deploy-server2.ps1` (ValidateSet and dispatch; not the `:1547` array)
- `scripts/c590-real.ps1` (`C590LiveCases` and the `:793` bundle condition)
- `scripts/verify-docker-stack.ps1` (stub arm only)
- `docs/docker-stack.md`
- `docs/testing-and-build.md` (the `:1341` sentence)
- `tests/Antiphon.Tests/Scripts/C1893BrokerUpgradeScriptTests.cs` (new)
- `C1893_S2_Docs_name_upgrade_broker` in `DockerStackDocumentationTests`

In-flight tips re-read against `origin/master` `c3cc0bb9` (three-dot):

| Owner | Tip | Files this work also touches |
|---|---|---|
| docs task `d336fcc3` | `12105bfbc2e0f472614e51a96adc4240b2384a32` | `docs/docker-stack.md` only |
| docs fix `83d30eec` | `3d1fb98c8b90abdcd2476f34e0bf07f1646cc43b` | `docs/docker-stack.md` only |
| CARD-1957 `c6c85b5b` | `1b59dd109dd37c525d902b55ed3fbed1ef647637` | `docs/docker-stack.md`, `scripts/c590-real.ps1`, `scripts/deploy-server2.ps1`. Also `scripts/c1609-host-proof.ps1` and `VolumeRetirementHostProofTests.cs`, which S1/S2 do not edit |

S1 Code waits until `d336fcc3` and CARD-1957 have landed, or until neither is
in flight. S2 Code waits until S1 has landed and CARD-1957 has landed. Land
order: `d336fcc3`, then CARD-1957, then S1, then S2. If CARD-1957 stalls, S1
may go first only when the land rebases `docs/docker-stack.md`. S2 still waits
on `c590-real.ps1` and `deploy-server2.ps1`. This TestDesign does not wait.

Derived index. The selector chain owns `tests/derived-impact-index.json`.
S1 and S2 do not regenerate or edit it while a selector-chain stage that owns
that file is in flight. The brief names no task id for that stage; Code does
not invent one. After that stage has landed on the base the slice is rebasing
onto, Code runs, once, as its own build-slot lease and not wrapped around
another build:

`pwsh -NoProfile -File scripts/build-slot.ps1 -Label c1893-derived-index -- pwsh -NoProfile -File scripts/regenerate-derived-impact-index.ps1`

`scripts/regenerate-derived-impact-index/Program.cs:102` prints exactly
`derived-impact-index changed` or `derived-impact-index unchanged`. The script
commits nothing. Exit 0 is either print. Exit 1 is a build failure: stop, do
not edit the json by hand. Commit the json only when stdout is exactly
`derived-impact-index changed`. When it prints `unchanged`, leave the file
unstaged. Do not hand-merge it.

### Checkpoints

Two runs, two After groups, one isolated `tests/Antiphon.Tests` build each.
Do not pass `--after all`: that schedules both builds. No `totalTimeoutMinutes`
line. Derived allowance is `max(30, 2*sum+10)`: S1 sum 18 gives 46 minutes;
S2 sum 20 gives 50 minutes. Those allowances are not expected durations.
`bin-c1893s1/` and `bin-c1893s2/` contain no `ref` substring.

The activation plan's CP-2 and CP-4 used 16 minutes (derived row timeout 48)
and its CP-5 used 30 minutes on the whole `RemoteScriptContractTests` class
(derived 90). This table splits that work. There is no whole-class remote-script
row. Every estimate is at most 15, so import should warn on none of these rows.

Budgets. The collector plan's cold `Antiphon.Tests` build was 607.7 s at
loadavg 14.85. CARD-1901 measured `DockerStackContractTests` 117/117 in 11.56 s
and `DockerStackDocumentationTests` 19/19 in 13.55 s. CP-1 and CP-4 at 14
minutes cover one cold build of that size plus seconds of contract tests.
That is one sample, not 2× p95 (2× of 607.7 s is about 20 minutes, and 16
already warns). Code records loadavg, slot id, and phase seconds. If a build
row's wall time exceeds 14 minutes, do not raise the estimate to 16. Split
the filter or record a measured p95 and keep the estimate at or under 15.
Reused rows at 2 minutes stay above 2× of the 11.56 s and 13.55 s samples.
CP-3 also reruns the six hold fixtures. Each does one `git fetch --no-tags
--depth 1` and then local commits, budgeted at the review's 8 s high sample
(this session measured 3.389 s and 4.458 s). Six fetches are 48 s. With the
two class samples, CP-3 is about 73 s, inside the 2-minute row. A fetch
without `--depth 1` is not the recipe. If Code measures CP-3 over 2 minutes,
raise only that row, still at or under 15. No `EstimatedMinutes` changes, so
derived row timeouts stay 42 for CP-1 and CP-4 and 15 for the others.

Red-first is a diagnostic, not the green certificate. Commit the new tests
before the production edit. Import this table to ignored YAML, keep the rows
for that slice, and change only the build output to `bin-c1893s1-red/` or
`bin-c1893s2-red/`. Run CP-1 and CP-2 at the S1 test-only commit, and CP-4,
CP-5, and CP-6 at the S2 test-only commit. Preserve the nonzero exit and the
`FAILED` lines. The named assertion must be the failure. Then commit the
production edit and run the table's own outputs at that SHA. CP-3 runs only
at the S1 production commit (`--after S1`). CP-7 runs only at the S2
production commit (`--after S2`). Do not point a green row at the red output
directory.

`Min` counts `[Test]` methods. The 117 and 19 figures are the current classes,
and CARD-1901 executed every one of them with 0 skipped, so they are executed
results, not assertion counts. New methods add one each. CP-3 is
117 + 11 S1 contract methods + 19 + 1 S1 doc method = 148, at the S1 commit,
before the S2 methods exist. CP-7 is 117 + 11 + 1 broker-identity method + 19
+ 1 + 1 upgrade-doc method = 150, at the S2 commit. A `[Test]` that loops
internally is still one result. The misspelled hold method is one result
even though it builds four trees.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1893s1/` | s1-contract | `/*/*/DockerStackContractTests/(C1893_S1_Standing_runner_sets_absolute_history_path*)\|(C1893_S1_Standing_runner_identity_literals*)\|(C1893_S1_Temp_runner_disables_history*)\|(C1893_S1_Init_state_creates_host_load_and_chmod_0700*)\|(C1893_S1_Hold_check_prints_HOLD_on_rc_2026_11_24*)\|(C1893_S1_Hold_releases_when_both_exact_lines_are_present*)\|(C1893_S1_Hold_stays_when_only_the_standing_path_is_present*)\|(C1893_S1_Hold_stays_when_only_the_temp_disable_is_present*)\|(C1893_S1_Hold_stays_when_the_lines_are_misspelled_or_unquoted*)\|(C1893_S1_Hold_prints_NOT_AFFECTED_when_the_ancestor_is_absent*)\|(C1893_S1_Hold_exits_nonzero_when_the_argument_does_not_resolve*)` | V-1 | all listed, 0 failed | 11 | 14 |
| CP-2 | S1 | CP-1 | s1-docs | `/*/*/DockerStackDocumentationTests/C1893_S1_Docs_name_the_hold_and_the_durable_path` | V-1 | all listed, 0 failed | 1 | 2 |
| CP-3 | S1 | CP-1 | s1-green | `/*/*/(DockerStackContractTests*)\|(DockerStackDocumentationTests*)/*` | V-1, R-1 | all listed, 0 failed | 148 | 2 |
| CP-4 | S2 | `tests/Antiphon.Tests -> bin-c1893s2/` | s2-upgrade | `/*/*/C1893BrokerUpgradeScriptTests/*` | V-2, R-2 | all listed, 0 failed | 12 | 14 |
| CP-5 | S2 | CP-4 | s2-identity | `/*/*/DockerStackContractTests/C1893_S2_Broker_identity_literals_match_the_runner` | V-2 | all listed, 0 failed | 1 | 2 |
| CP-6 | S2 | CP-4 | s2-docs | `/*/*/DockerStackDocumentationTests/C1893_S2_Docs_name_upgrade_broker` | V-2 | all listed, 0 failed | 1 | 2 |
| CP-7 | S2 | CP-4 | s2-green | `/*/*/(DockerStackContractTests*)\|(DockerStackDocumentationTests*)/*` | V-2, R-1 | all listed, 0 failed | 150 | 2 |

### What Code reports per CP-n

For every row, the unedited line from the checkpoint tool:

`CHECKPOINT CP-n commit=<sha> build=<ok|reused|failed> filter=<filter> executed=N passed=N failed=N skipped=N trx=<path>`

plus slot and wait, `dirty`, `source`, `sourceState`, `buildSource`, and
`reruns` when the rerun count is greater than 0. For CP-1, CP-2, CP-4, CP-5,
and CP-6, also keep the first-run `FAILED` lines that name the assertion in
this design, then the green rerun at `reruns=1` with 0 failed. `Min` met,
loadavg, wall time against `EstimatedMinutes`, source SHA, and the receipt
path. The first run is red by design. A leased run does not support
`reviewedSourceClean`.

### Cost

Ordinary V/R floor is the sum of `EstimatedMinutes`: 14+2+2+14+2+2+2 = 38
minutes. That sum is estimated, from the one 607.7 s cold-build sample and the
CARD-1901 class timings, not a measured p95 of these rows. PC floor is
Mutation after land: 21 method-scoped break/red/restore/green cycles, one
leased build per mutation and per restore. Not run here. The waiver does not
mark them done. No `RemoteScriptContractTests` class run is saved against; the
row was removed because that class does not finish inside its own 600 s timeout.

--- next stage ---
next: review
handoff: Opus plan-review round 2, diff from c88fc182d965b65a8154545f6431d5e7c5289430. guards=21, mapped=21, missing=0, duplicate PC maps=0. Mutation waived and owed.
artifact: docs/superpowers/plans/2026-11-17-card-1893-s1-s2-testdesign.md
