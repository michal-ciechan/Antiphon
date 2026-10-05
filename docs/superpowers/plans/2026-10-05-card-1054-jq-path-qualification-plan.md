# CARD-1054: qualify the jq actually selected by the child PATH

Date: 2026-10-05. Plan task: `65991c1e-04a2-460e-a940-8043ff3834f9`.
Inspected source: `a60096be985bb43b3f9a611fd96dc7d10b2ddeda`.

## Outcome and stage boundary

Make the image's `jq-version` qualification refuse a user-home jq ahead of
`/usr/local/bin` on the actual child PATH, even when both binaries work and have
identical bytes. Accept the canonical regular file and an alias that resolves
to it, and retain both the path found and its resolved target. Preserve the
existing exact-version, exit-status, stderr and uid checks.

The host half is already implemented by CARD-1025. This plan closes the remaining
image-probe gap and hands its evidence contract to CARD-1040 S2. It neither
duplicates host provisioning nor claims current image activation.

This dispatch does not fold TestDesign into Plan. The test names, controls and
checkpoint table below are concrete proposals for that separate stage to freeze.
No product-policy answer is needed; the decisions below implement the card's
requested behavior. Next: **test-design**, not Code. This Plan task commits and
pushes only its assigned branch and lands nothing.

## Ground truth

Read live CARD-1054, CARD-1040, CARD-1025 and CARD-1058 on 2026-10-05. References
below are relative to the inspected source; historical card observations are not
fresh measurements of the running container.

| Card assumption | What the code or current card actually says | Plan consequence |
|---|---|---|
| A working home copy can be mistaken for image jq. | `docker/session-runner-grok/verify-codex-image.sh:80` runs **absolute** `/usr/local/bin/jq --version`. It refuses when that file is absent, but never discovers `command -v jq`. With a healthy canonical file present it can pass while the child would select a home copy. | Fix this specific mismatch; do not claim the current probe accepts the historical absent-canonical case. Test canonical-present and canonical-absent shadow cases. |
| The probe records which jq was found. | The same row currently emits only `C660_ROW jq-version ok jq-1.7.1 as uid 1654`. No found or resolved path appears. | Extend the row detail with both observations, including path refusals. |
| Version equality establishes custody. | `docker/session-runner-grok/Dockerfile:105` already pins jq 1.7.1 and its SHA-256, verifies before root-owned 0755 install. The CARD-1040 S1 report records a same-digest, app-owned home copy and absent canonical file on 2026-10-04. | Keep image pinning unchanged. PATH identity is an additional requirement, not a substitute for image identity/digest/owner/mode evidence. |
| CARD-1025 S1 is still in flight and needs this implementation. | Its live card is Done: S1 landed at `dc7d1794b`, S2 at `fc3b3e1e8`; its terminal reason reports a read-only live host qualification. `scripts/server2-host-jq.sh:23` clears command hashing, records lookup/resolution, rejects a home target and canonical-leaf symlink before invoking jq. | Reuse the landed host contract. The card's reference to Code `f92f723a` is historical. Do not reimplement or rerun its installer battery. |
| A canonical lookup string is sufficient. | The host helper requires resolved target equal to the canonical destination and a non-symlink regular canonical leaf. `scripts/deploy-server2.ps1:181` separately admits only canonical resolved success proofs. Existing host tests cover identical-byte home shadows, accepted aliases, canonical-leaf symlinks, forged proofs and refusal receipts. | Match that path meaning on the image side; the canonical spelling alone cannot admit a home target. |
| An existing test already covers image PATH shadowing. | `JqRunnerImageContractTests` has two source contracts and a twelve-result version matrix. Its extracted row fixture overrides `env`, without real PATH/alias admission. `RemoteScriptContractTests.LinuxShell` returns stdout and does not assert child exit status. | Add native private filesystem cases and assert process status, row status, paths and execution effects. Adapt the existing version fixture so a new path guard cannot mask its version assertions. |
| Adding path detail requires changing the image wrapper. | `scripts/verify-card0660-codex-image.ps1:162` retains `row-jq-version.txt` and grades exit zero plus exactly one matching `C660_ROW ... ok <detail>` line. It accepts additional detail. | Preserve that interface; no wrapper implementation change is planned. |
| CARD-1040 S2 can run on a provisional home jq. | Its frozen plan and `docs/testing-and-build.md` require canonical child resolution, immutable outer image/activation evidence, and fifteen exact consumer results with zero skips. Its S1 report leaves that gate pending. Both documents currently require the old exact jq row text. | Update the documentary row contract and preserve S2's activation gate and exact fifteen-method manifest. Do not rerun those methods here. |
| Path equality closes all custody risks. | CARD-1058 remains Backlog for canonical hardlinks and a writer's check-to-use swap. The host contract explicitly trusts `/usr/local/bin` integrity. | Preserve that limitation; this card does not add descriptor-based execution, link-count rules or a new host trust policy. |

Owners read: [testing/build](../../testing-and-build.md),
[Docker qualification and rollout](../../docker-stack.md),
[orchestration](../../orchestration-loop.md), [HTTP](../../ops-http.md) and
[project conventions](../../project-context.md). Existing dependency artifacts:
[CARD-1025 plan](2026-10-04-card-1025-host-jq-prerequisite-plan.md),
[CARD-1040 plan and frozen verification](2026-10-04-card-1040-jq-prerequisites-and-unit-timing-plan.md),
[CARD-1040 S1 evidence](../../investigations/2026-10-04-card-1040-s1-jq-admission.md).

## Decisions

- **D-1 — Observe the actual non-login PATH before running jq.** Clear the shell
  command cache, find jq without rewriting PATH, require an absolute executable
  file, resolve it with `readlink -f`, and compare with the canonical destination.
  Reject missing/unresolvable discovery and an unrelated home copy before jq
  execution. Rejected: relying on version/hash alone, prepending the canonical
  directory to conceal shadowing, a login-shell probe, or invoking the absolute
  canonical file without checking what the child would select.
- **D-2 — Permit aliases only to the regular canonical leaf.** Require resolved
  target `/usr/local/bin/jq`, an executable regular file there, and no symlink
  at that leaf. An earlier PATH alias to that file is valid. Invoke the admitted
  resolved path for the existing version check. Rejected: lookup-string equality
  as an alternative admission rule, accepting a canonical symlink into home, or
  rejecting all aliases. Keep the `/usr/local/bin` integrity boundary and
  CARD-1058's separate policy decision explicit.
- **D-3 — Retain found and resolved paths in the existing row.** Keep exactly
  one `C660_ROW jq-version ok|fail ...` line and current status/exit conventions;
  append `lookupPath` and `path` details on successful and path-refused outcomes.
  Use Bash shell escaping so whitespace/control characters cannot create extra
  physical receipt lines; unavailable observations have an explicit sentinel.
  Do not run an unapproved jq to format its own refusal. Rejected: a second
  `C660_ROW`, a new JSON receipt subsystem, silently replacing the found path
  with the canonical spelling, or dumping PATH/environment variables. TestDesign
  freezes the exact detail grammar and failure reason names before Code.
- **D-4 — Extend the existing small probe and test class.** Add a fixed
  `JQ_DESTINATION=/usr/local/bin/jq` constant if needed for a test-private copy;
  no environment override or caller-selectable production destination. Keep
  the twelve version vectors and two source contracts. Rejected: importing the
  host installer into the image probe, rebuilding the image to exercise a path
  decision, or extracting a general qualification framework for this change.
- **D-5 — Keep ownership and completion separate.** CARD-1025 owns host
  preflight/receipts and its pending PCs; CARD-1040 S2 owns durable image
  qualification, activation provenance and its fifteen-method proof; CARD-1058
  owns stronger canonical-directory trust. CARD-1054 owns this image PATH
  regression and its path evidence. Rejected: another host-jq implementation,
  a host receipt used as image proof, or closing CARD-1040 on private fixture
  results. No deployment, installation, restart or runner setting change here.
- **D-6 — Bound ordinary verification and defer deliberate mutations.** Use
  the exact methods below, one ordinary run per changed selection and one PC
  per new behavior in a separately commissioned post-land Mutation stage.
  Rejected: whole Unit, entire namespace/assembly runs, full provider-image
  qualification, class-wide PCs, retries or timeout changes. This brief's narrow
  scope supersedes the generic whole-Unit recipe for this card.

## Placement and dependencies

Read `GET /api/runner-defaults` and `GET /api/session-runners` at approximately
08:24 UTC on 2026-10-05 through the configured task API. Defaults revision was 2;
the catalogue had eligible Linux and Windows descriptors and one unavailable,
draining descriptor. These observations select no immutable image and reserve
no capacity. Re-read both routes at dispatch; do not encode a fleet hostname.

Plan, TestDesign and documentation work need no platform pin. The native Bash
filesystem/alias tests require the **native Linux lane**: use `-Platform Linux`
only for that work and omit `-Runner` unless explicitly commissioning one host's
qualification. Omit `-Platform` otherwise; `-Platform Any` clears a prior pin.
Build-slot availability remains a prerequisite, not permission for an unleased
retry. The tests use private executable fixtures, so they do not depend on the
active image already having qualified jq.

Before Code, confirm its base contains the landed CARD-1025 helper, wrapper and
canonical-leaf repair. The inspected base does. If a different base does not,
have the caller commission from the correct source; do not cherry-pick duplicate
host patches or rebase this task's fast-forward-only branch. Coordinate shared
documentation edits with the CARD-1040 S2 owner. That owner must consume the
reviewed CARD-1054 probe at its exact source SHA before claiming S2 admission.
No required fifteen-method run starts until S2 has its own image/activation and
same-child PATH evidence. CARD-1025's terminal report is a dependency reference,
not a new live host receipt produced by this Plan.

## Slices and files

### S1 — Image PATH admission and executable regression cases (45–60 minutes)

Change `docker/session-runner-grok/verify-codex-image.sh` only in the jq constant,
row and any narrowly needed jq detail formatting. Implement D-1 through D-3;
retain uid 1654, exact pin, nonzero-exit and empty-stderr checks.

Extend `tests/Antiphon.Tests/Infrastructure/JqRunnerImageContractTests.cs` with
the four named methods below. Build real private directory layouts, regular
executable stand-ins and symlinks, and run the production probe from a copied
script with only fixed path/isolated-home and test-uid substitutions. Record
every replacement and require its expected occurrence count. Do not replace the
admission predicate, `command -v` or `readlink` with an expected answer. Healthy
canonical and home stand-ins have identical bytes and valid version behavior;
an execution log distinguishes whether either was called. This is behavioral
path proof, not image-digest/ownership proof.

Use the existing assembly-local `ParallelLimiter<ProcessSpawnLimit>`. Bound and
reap each owned child, drain stdout/stderr and assert its actual exit code; the
stdout-only `LinuxShell` helper is insufficient on its own. All fixture writes,
PATH adjustments and cleanup stay private to the child/task. No root access,
real home edits, global profile changes or shared binary deletion. Adapt the
existing version-matrix setup to present a valid private canonical path so each
of its twelve rows continues testing its intended version/exit/stderr defect.

Read but leave `Dockerfile`, `scripts/verify-card0660-codex-image.ps1`,
`scripts/server2-host-jq.sh`, `scripts/deploy-server2.ps1` and
`HostJqPrerequisiteScriptTests.cs` unchanged. Commit/push S1 before its checkpoint
run; report and commit any necessary fix before rerunning the affected row.

### S2 — Qualification owner docs and dependency handoff (30–40 minutes)

Update the jq qualification subsection in `docs/testing-and-build.md` with the
new row detail, actual-child PATH requirement, alias/leaf rules, rejection
evidence and CARD-1058 boundary. Amend only the matching jq-row admission text
in `docs/superpowers/plans/2026-10-04-card-1040-jq-prerequisites-and-unit-timing-plan.md`
so its old exact-line requirement cannot reject the new evidence. Preserve all
fifteen method names, checkpoints, timings and activation obligations there;
leave the historical S1 evidence document untouched.

Link this plan as CARD-1054's prerequisite to CARD-1040 S2 and link the landed
CARD-1025 implementation for outer-host preflight. Record the S1 source SHA and
unedited checkpoint lines in the Code report; generated TRX/JSON/logs stay
gitignored. Commit/push S2. Check links and `git diff --check`; no extra test or
build for this documentation slice. A required provider/image/runtime change
is outside these slices and returns to the caller with the concrete finding.

## TestDesign handoff

TestDesign must add `## Verification design`, read the affected existing bodies
and specify the new methods, then freeze fixture substitutions, receipt grammar, exact roster, importable
manifest, control seams and costs. This Plan proposes four non-parameterized
methods; internal vectors each count as one TUnit result, not extra executions.

| Behavior / coverage | Proposed method in `JqRunnerImageContractTests` | Required observations |
|---|---|---|
| B-1 / V-1 / R-1: reject a home PATH shadow | `C1054_Jq_row_rejects_home_shadow` | Identical working home binary precedes canonical directory, with canonical present and absent: exit 1, exactly one fail row, no ok row and no jq execution. Missing discovery also refuses; no skip. |
| B-2 / V-2 / R-2: accept the canonical file and an alias to it | `C1054_Jq_row_accepts_canonical_file_and_alias` | Direct file and earlier PATH symlink to it each exit 0, emit one ok row, and execute the admitted target. Preserve distinct found/resolved observations for alias. |
| B-3 / V-3 / R-3: reject the canonical leaf pointing into home | `C1054_Jq_row_rejects_canonical_leaf_symlink` | PATH discovers the canonical spelling but resolution is a healthy home target: exit 1, fail row, no invocation and no success. Keep the home target executable to prevent a broken fixture masking admission. |
| B-4 / V-4 / R-4: truthful path evidence | `C1054_Jq_row_records_found_and_resolved_paths` | Canonical, alias, home refusal and unavailable discovery retain the actual two observations or sentinel. Spaces/control characters stay escaped on a single physical row; no unapproved jq executes to format refusal. |
| V-5 / R-5: preserve existing pin/probe contract | Existing `Version_row_accepts_only_exact_successful_pin_without_stderr`, `Pinned_download_is_verified_before_root_owned_install_in_every_runner_target`, `Qualification_grades_the_jq_row_for_both_targets` | Twelve version/exit/stderr results plus two source-contract results; the new path gate is healthy in the version matrix, not the cause of their failures. |

One proposed PC per new behavior, all **pending**, each confined to its exact
method. TestDesign must ensure the mutant compiles and reaches the named
assertion; no credit for another guard's refusal, setup errors or zero tests.

| PC | Behavior and deliberate defect | Exact filter | Required red, then restoration |
|---|---|---|---|
| PC-1 | B-1: bypass resolved-target equality while retaining a healthy canonical leaf. | `/*/*/JqRunnerImageContractTests/C1054_Jq_row_rejects_home_shadow` | Canonical-present home-shadow vector fails its exit-1/no-ok admission assertion. Restore and pass the same method. |
| PC-2 | B-2: require lookup spelling equal to the canonical path, forbidding an otherwise approved alias. | `/*/*/JqRunnerImageContractTests/C1054_Jq_row_accepts_canonical_file_and_alias` | Alias vector fails its success assertion while direct canonical control still passes. Restore and pass the same method. |
| PC-3 | B-3: change the compound path admission to also accept lookup-equals-canonical regardless of resolved target/leaf. | `/*/*/JqRunnerImageContractTests/C1054_Jq_row_rejects_canonical_leaf_symlink` | Executable home target behind canonical symlink produces the forbidden success, failing exit-1 assertion. Restore and pass the same method. |
| PC-4 | B-4: write resolved target into the emitted lookupPath field. | `/*/*/JqRunnerImageContractTests/C1054_Jq_row_records_found_and_resolved_paths` | Accepted alias has wrong found-path evidence although qualification succeeds; exact lookup assertion fails. Restore and pass the same method. |

These controls do not duplicate CARD-1025's helper/wrapper PCs or CARD-0927's
version/checksum controls. They are designed here and executed only after
ordinary Code, separate Review and confirmed land under the commissioned
Mutation/restoration contract. Run method-scoped baseline/red/restored-green;
never a whole-class PC. A snapshot's evidence/restoration records remain in its
assigned external root and it never commits/pushes mutations.

Ordinary execution uses the checkpoint tool, one `run --plan <this-plan>` for
the committed S1 group with exact `--expected-source-sha`, then `wait` until its
exit is not 75. Do not settle with an owned run outstanding. Bootstrap the tool
through `scripts/build-slot.ps1` into a separate `bin-c1054-tool/`; rows take
their own slots. Keep source frozen during the run. Preserve exact receipt
lines and verify actual method rosters/counts, clean source and verified build
provenance. Slot timeout is not run. Remove only task-owned alternate outputs
after all children exit, including bootstrap outputs. Code/Review run
`scripts/check-evidence-diff.ps1` across their full task range.

If a regression fails, compare that exact method at the committed base before
calling it inherited. Do not run the base assembly. No timeout/skip/assertion
weakening, known-flaky addition or repeat after green is authorized here.

### Cost

S1 authoring/review is 34–47 minutes, tool setup 2–4 and proposed ordinary rows
9 minutes: 45–60 total. S2 is 30–40 minutes, documentation only. These are
estimates, not measured timings. Mutation is a separate budget: four exact
baselines at 3 minutes plus four red/restored-green cycles at 6 minutes gives a
36-minute execution floor, before setup/reporting. TestDesign must refine that
estimate rather than absorb deliberate PCs or activation waits into Code.

### Proposed checkpoints (superseded by the frozen manifest below)

Proposed closed list for S1, **pending TestDesign freeze**. Every group names
the native Linux lane. No CARD-1040 consumer rows, host installer battery, image
build or whole-Unit selection is added. S2 has no build/test row.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1054-jq/` | linux-jq-path | `/*/*/JqRunnerImageContractTests/(C1054_Jq_row_rejects_home_shadow*)\|(C1054_Jq_row_accepts_canonical_file_and_alias*)\|(C1054_Jq_row_rejects_canonical_leaf_symlink*)\|(C1054_Jq_row_records_found_and_resolved_paths*)` | V-1, V-2, V-3, V-4, R-1, R-2, R-3, R-4 | exactly the four proposed methods, 4 passed, 0 failed/skipped, no extra methods | 4 | 6 | true |
| CP-2 | S1 | CP-1 | linux-jq-pin-regression | `/*/*/JqRunnerImageContractTests/(Version_row_accepts_only_exact_successful_pin_without_stderr*)\|(Pinned_download_is_verified_before_root_owned_install_in_every_runner_target*)\|(Qualification_grades_the_jq_row_for_both_targets*)` | V-5, R-5 | exactly 12 argument-expanded version results and 2 source-contract results, 14 passed, 0 failed/skipped, no extra methods | 14 | 3 | true |

## Completion and caller obligations

The implementation is ready for ordinary Review when S1's exact 18 results are
green at its committed source, path evidence demonstrates both rejection and
accepted alias identity, and S2 updates both live qualification instructions.
Report PCs as designed/pending until separately executed. This is not evidence
that any running image has been rebuilt or activated.

The caller then gives CARD-1040 S2 the reviewed probe SHA and updated contract.
S2 obtains its own immutable image/activation evidence and same-child
qualification; only then does its unchanged fifteen-method manifest run. Keep
CARD-1025's host qualification and CARD-1058's trust limitation visible in that
handoff. No deployment is authorized by this Plan dispatch.

--- next stage ---
next: test-design
handoff: Freeze CARD-1054's four image PATH behaviors, one exact-method PC each and the 18-result native-Linux manifest; reuse landed CARD-1025 host admission, update CARD-1040 S2's row contract, and retain its image-activation gate. No whole Unit, host provisioning or rollout.
artifact: docs/superpowers/plans/2026-10-05-card-1054-jq-path-qualification-plan.md

## Verification design

TestDesign task: `a1db6174-4243-46e5-875f-4589a0f9fb02`, 2026-10-05.
Source read: `0ffa99b28fd675d0e8d04d3cd35a684f1f197028`. D-1 through D-6 and
S1/S2 remain the fix design. This section freezes their verification. The Plan
handoff and proposed table above are historical; the sole exact `### Checkpoints`
heading below is the importer entry point. `PlanTableImporter.ExtractSection`
selects the first such heading, not the last.

The ordinary roster remains **four new methods plus twelve existing argument
results and two existing source contracts: eighteen results**. PC-1..PC-4 remain
the four proposed behavior controls, each confined to its exact method. The
standing TestDesign rule also requires a distinct PC for each independently
bypassable guard. PC-5..PC-13 therefore split the additional observation,
escaping, discovery and execution guards within those same four methods. They
add no ordinary methods or Code runs. Four mutations alone would not audit all
the safety assertions in D-1..D-3. All thirteen are designed, **not executed**.

### Inspection

| Bodies read | Boundaries -> V/R IDs or exclusion |
|---|---|
| `JqRunnerImageContractTests.Pinned_download_is_verified_before_root_owned_install_in_every_runner_target`, `Qualification_grades_the_jq_row_for_both_targets`, all twelve attributes and the body of `Version_row_accepts_only_exact_successful_pin_without_stderr`, and `Read` | Two source checks plus twelve version results -> V-5/R-5. The current extracted-row fixture replaces `env` and checks only stdout. It must gain a real admitted file and actual exit/trace assertions; another path refusal must not pass a version case. |
| `verify-codex-image.sh` constants, `need_uid`, `result`, `jq-version`; other row bodies inspected for shared-helper impact | Actual PATH, resolved identity, canonical leaf, status and diagnostic output -> V-1..V-4/R-1..R-4. Other provider rows are excluded: leave their bodies and shared `need_uid`/`result` unchanged. |
| `HostJqPrerequisiteScriptTests.C1025_Check_qualifies_deployment_shell`, `C1025_Check_rejects_canonical_leaf_symlink`, `C1025_Receipt_rejects_canonical_lookup_with_unapproved_target`; `HostJqFixture` constructor, `Existing`, `HomeShadow`, `JqScript`, `Start`, `Run`, `Collect`, `ReplaceOnce`, `Locate`, `Executable`, `Dispose` | Nearest fixture for new private filesystem setup in the existing image test file: identical-byte shadow, alias and live symlink target; exact counted constant replacement; child-only PATH, bounded stream collection -> V-1..V-4. Do not reuse its jq/SSH/Docker/privilege fakes or require installed jq for the image fixture. No new shared helper file is necessary. |
| `RemoteScriptContractTests.PrepareLinuxShellScript` and `LinuxShell` | Existing native/WSL launcher returns stdout without checking exit and can skip on Windows; insufficient for the new image admission oracle. Leave it unchanged; use a local result carrying exit/stdout/stderr -> all executable cases. |
| `DockerStackDocuments.Read`, `FindRoot`, `Stages`, `Closure`; `ProcessSpawnLimit.Limit` | Repository source lookup, Docker stage closure and assembly-local limiter=1 -> V-5 and private fixture process ownership. No fixture writes under the source binary directories or outside its owned root. |
| Dockerfile jq arguments/RUN; wrapper `Invoke-Probe`, jq row assignment and retained `row-jq-version.txt` | Digest-before-root-owned-install and exit-zero/exactly-one-ok-row wrapper admission -> V-5. Wrapper already accepts added detail. No wrapper/Dockerfile change, Docker build or live provider proof. |
| `server2-host-jq.sh` `qualify`/`refuse_path`; `deploy-server2.ps1` successful proof shape/path admission | Landed CARD-1025 clears hashing, resolves actual lookup, refuses home targets and canonical-leaf links; wrapper requires resolved canonical proof. Dependency only; no new host test, installer or host PC execution. |
| CARD-1040 verification admission, Delivery inventory, Guard inventory, fifteen-method Checkpoints and Cost; testing owner's jq qualification section | Only row grammar/dependency wording changes here. Preserve immutable outer image/activation receipt, actual-child qualification and all fifteen consumers -> documentary check in S2; no CARD-1040 test runs. |
| `PlanTableImporter.ImportMarkdown`, `ExtractSection`, `SplitRow`, `RosterTokens`; testing owner's manifest, runner, build-slot and Mutation sections | One active table, escaped OR separators, CP-2 build reuse within S1, 4/14 floors and explicit serial rows -> CP-1/CP-2. Actual TRX equality is a separate check from the tool's minimum. |

Missing setup to implement in S1, with no new production test seam:

- Add a private fixture in `JqRunnerImageContractTests.cs`. Native Linux is the
  checkpoint prerequisite. Bash, `id`, `env` and `readlink` must be real executable
  host tools; absence fails setup. No installed jq, Docker, root access, external
  network, database or currently activated image is required. No missing-tool
  skip is accepted in these Linux rows.
- Allocate `/tmp/c1054-<guid>` with a simple ASCII name, private mode 0700,
  `destination/jq`, `home/app/.local/bin/jq`, `alias/jq`, `tools`, `probe-home` and
  a NUL-delimited invocation log. The parent creates all directories. Real
  executable fixtures use mode 0755; symlinks use the native filesystem. Remove
  only this root after all owned processes and redirected streams finish.
- Copy the **whole** probe. Freeze three substitutions, each counted once before
  replacement: `JQ_DESTINATION=/usr/local/bin/jq` to a shell-quoted private
  destination; `PROBE_HOME=/c660-home` to a shell-quoted private home; and the
  single `need_uid 1654` inside the identified `jq-version` arm to the measured
  test uid. Locate that arm uniquely and preserve its remaining bytes. Leave
  `JQ_VERSION`, the actual uid function, result function, admission expressions,
  `command -v`, `readlink` and formatter unchanged. A replacement-count mismatch
  fails setup. These substitutions cannot qualify real uid 1654 or image custody.
- Start absolute native Bash without login/profile processing. Supply only
  fixture environment values, `LC_ALL=C`, the test-owned HOME, trace and version
  vector variables. PATH consists exclusively of the chosen private candidate
  directories plus `tools`; `tools` has symlinks to real `id`, `env`, `readlink`
  and no jq. No inherited PATH tail, imported shell functions or `BASH_ENV` may
  supply an accidental jq. Invoke the copied script with `jq-version`.
- For the stale-hash vector only, a Bash prelude first installs a real
  `hash -p <private-canonical> jq`, asserts `command -v jq` sees that cached
  canonical path, then sources the same copied script with argument `jq-version`
  **in that shell**. PATH already has home before canonical and is not reassigned
  after seeding the hash. The production `hash -r` must expose the home shadow.
  This is shell state setup, not replacement of discovery with a fake answer.
- Healthy home/canonical executable files have identical script bytes. The
  stand-in logs its actual `$0`, argc, argv and HOME as NUL-delimited values,
  emits the configured stdout/stderr and exit, and uses Bash builtins only.
  Establish byte equality and successful direct `--version` execution before
  the rejection vector, then clear the log. Thus a zero execution assertion
  cannot pass because the supposedly healthy rejected fixture was broken.
- Return `(Exit, Stdout, Stderr)` from every child; use `ArgumentList`, concurrently
  drain both streams, close stdin, and bound process exit plus both EOFs by the
  existing `LinuxShell` 60-second ceiling. On timeout/failure kill the owned tree,
  await exit and drain within a separate 10-second cleanup ceiling; failed cleanup
  fails the test. Retain `ParallelLimiter<ProcessSpawnLimit>` on all five
  process-spawning methods, including the existing parameterized method. No
  Pty test host or second Antiphon.Tests host runs beside these serial rows.

Receipt grammar, frozen for Code and the CARD-1040 handoff:

```text
C660_ROW jq-version ok jq-1.7.1 as uid 1654 lookupPath=Q(found) path=Q(resolved)
C660_ROW jq-version fail reason=REASON lookupPath=Q(found-or-unavailable) path=Q(resolved-or-unavailable)
```

`Q(value)` means Bash builtin `printf '%q'` with `LC_ALL=C` for these fields;
`Q(...)` is specification notation, not literal output. Fixed unavailable value
is `unavailable` (no quotes in its encoded form). An observation is retained once
known, even if refused. The failure line above applies to path admission; the
existing version/exit/stderr failure explanations remain intact. Path cases
have exactly one nonempty physical stdout line, one matching row and no raw
CR/tab/other control bytes inside that line. Success exits 0; every path refusal
exits 1, never 0 or usage exit 2. Stderr is empty in these controlled path cases.
Diagnostics use Bash builtins and must not execute a refused jq.

| First failed boundary | REASON | Retained observations |
|---|---|---|
| `command -v jq` fails/returns empty | `JqNotFound` | Both `unavailable`; an off-PATH canonical file is not fallback authority. |
| Found value is not an absolute executable regular file | `JqLookupInvalid` | Actual found text, resolved `unavailable` because resolution was not attempted. |
| Real `readlink -f -- <found>` fails/returns empty | `JqResolveFailed` | Actual found text, resolved `unavailable`. |
| Resolved target differs from the fixed destination, or canonical leaf fails regular/executable/non-symlink admission | `JqPathUnapproved` | Actual found and resolved values. Canonical spelling cannot override an unapproved target. |

Freeze the new methods as non-parameterized `[Test]` methods. Each vector below
is an internal case and contributes no additional TUnit result:

| Method / IDs | Internal boundary combinations and decisive observations |
|---|---|
| `C1054_Jq_row_rejects_home_shadow` / V-1, R-1 | Run canonical-present home shadow **first**, then canonical-absent home shadow; identical healthy binaries, home first on PATH, exit 1 and `JqPathUnapproved`, exact two home paths, trace empty. Add missing discovery with canonical present but off PATH, then absent everywhere (`JqNotFound`); PATH entry `.` with cwd=canonical parent (`./jq`, `JqLookupInvalid`); canonical found with only the fixture's readlink link omitted (`JqResolveFailed`); and the same-shell cached-canonical/home-first vector (`JqPathUnapproved`). Each asserts actual exit, exact fail row/no ok row, correct fields and no jq invocation. |
| `C1054_Jq_row_accepts_canonical_file_and_alias` / V-2, R-2 | Direct canonical first; earlier absolute alias to it second. Both exit 0 with exact success prefix/fields and one invocation whose `$0` is the resolved canonical path, argc=1, argv=`--version`, HOME=private probe home. Include a nonexecutable earlier home candidate plus executable canonical fallback: Bash must discover canonical, since the nonexecutable home file is not a shadow. |
| `C1054_Jq_row_rejects_canonical_leaf_symlink` / V-3, R-3 | Canonical PATH spelling is a symlink to a healthy executable home target, both with direct lookup and an earlier alias to that leaf. Require exit 1, `JqPathUnapproved`, found spelling preserved, resolved home preserved and empty trace. Additional static layouts: dangling leaf, self-loop leaf and directory leaf refuse `JqNotFound`, with unavailable observations and empty trace. A nonexecutable canonical leaf with no later jq is discovered by Bash and refuses `JqLookupInvalid`, retaining found canonical spelling and unavailable resolution. These layouts never reach execution or block on a FIFO. |
| `C1054_Jq_row_records_found_and_resolved_paths` / V-4, R-4 | Direct, accepted alias, home shadow, canonical-link-to-home, unavailable lookup and unavailable resolution. Assert complete row equality using independent fixture expectations. Add accepted alias directory containing a newline **first**, then space, tab, CR, quote and backslash as separate vectors; then a plain alias to an unapproved home path with those characters, again newline first. Cover each field independently: encoded found differs from encoded target. Assert a single physical row before field equality so PC-6/PC-13 reach their named assertion; no unsafe `eval` of receipt text. Preserve exact sentinel and reason expectations. |

Use literal expected encodings for those fixed filename suffixes, composed with
the safe ASCII private root, rather than calling the production formatter to
generate expected output. A space is `\ `; a newline-bearing full path uses
Bash's `$'...\n...'` form, and likewise for tab/CR. C# should compare the exact
encoded values as strings. Do not split shell-escaped fields on spaces.

The existing version method keeps all twelve attributes unchanged and uses the
same private file fixture with healthy canonical admission; configure only the
stand-in's stdout, exit and stderr. Require one traced canonical `--version`
invocation in **every** vector, actual exit 0 for the one good row and 1 for the
eleven refusal rows, and the existing correct refusal explanation: `exit=1`,
`stdout=[...] expected jq-1.7.1`, or `unexpected version stderr`. No
`reason=Jq...` path refusal is allowed to satisfy these assertions. Its two
source-contract companions remain unchanged.

Code correction (2026-10-05, task `dfea030e`): native CP-1 at
`f25bea3cdf7d1f50719a1cbb299cdffb570ee6a9` disproved the original assumption
that Bash never reports a nonexecutable file. With no executable fallback,
`command -v` returns its path; the frozen first-failed-boundary grammar therefore
requires `JqLookupInvalid` and the actual lookup observation. The vector above
now asserts that exact receipt, exit 1 and zero invocations. Production admission,
receipt grammar, checkpoint roster and all thirteen control seams are unchanged.

Boundary exclusions are deliberate. Bash cannot discover a directory, dangling
link or loop as executable jq in this private PATH; they exercise discovery
refusal, not artificial `readlink` output. A nonexecutable file instead reaches
lookup validation, as measured above. Readlink failure
is exercised independently by omitting that utility from the child PATH while
canonical jq remains discoverable. Relative lookup is independently reachable
with PATH=`.`. NUL and slash inside a filename are impossible Linux filenames;
PATH components containing colon cannot designate the intended single directory.
No such pseudo-case is claimed. Parent symlink policy, hardlink custody and
check-to-use replacement remain CARD-1058; adding a race to isolate a redundant
leaf test would cross D-2's explicit static-directory trust boundary.

### Delivery inventory

New/changed asynchronous delivery paths: **none**. The probe synchronously
returns a row and process exit to its waiting wrapper. The wrapper retains
`row-jq-version.txt`; its existing producer, persistence and recovery are not
changed. Test fixture tasks await owned processes and are not message delivery.
There is no new queue, recipient, durable session identity, enqueue handoff,
crash recovery, busy-recipient branch or already-eligible-recipient branch to
test. No session input or UserPrompt receipt is claimed. Consequently the real
queue/transcript requirement is inapplicable, not replaced by a queue ack.

For the local synchronous oracle, join the private fixture root/vector identity,
child exit, captured row and **actual executable invocation log**. A row alone
does not prove what ran. The copied fixed-path script and stand-in executables
prove real Bash lookup/resolution/admission and execution effects; they cannot
prove artifact digest, root ownership, real uid 1654, actual runner activation
or recipient delivery. Source contracts only check checked-in install/wrapper
structure. CARD-1040 S2 still supplies outer container ID/creation/start, immutable
image ID/digest, build/activation receipts, runner buildVersion and matching
actual-child path/version/hash/owner/mode before its own fifteen results.

### Proves it works now

- V-1: Reject actual home shadows, unavailable discovery/resolution and relative
  lookup, including a stale command cache | real native Bash/private filesystem |
  CP-1 `C1054_Jq_row_rejects_home_shadow` | fail exit/row/reason/paths and no jq
  execution in every listed vector.
- V-2: Accept canonical file and an earlier alias to that same file | native
  Bash/symlink/invocation trace | CP-1
  `C1054_Jq_row_accepts_canonical_file_and_alias` | success and exactly one
  invocation through resolved canonical path with the expected argv/HOME.
- V-3: Refuse a working home target behind the canonical spelling | native
  filesystem | CP-1 `C1054_Jq_row_rejects_canonical_leaf_symlink` | fail exit/row,
  correct observed home target and zero execution; unusable leaves also refuse.
- V-4: Keep truthful, unambiguous found/resolved evidence in success and refusal |
  captured real stdout | CP-1 `C1054_Jq_row_records_found_and_resolved_paths` |
  exact independently expected fields, sentinels, reasons and one physical row.
- V-5: Preserve pin/install/wrapper and all existing version semantics | source
  contracts plus executable version fixture | CP-2 three existing methods |
  exactly 14 results; twelve version vectors reach canonical invocation, only
  exact successful pin with empty stderr passes qualification.

### Guards the regression

- R-1: A healthy absolute canonical binary must not conceal home-first PATH |
  V-1 method | first vector `Exit.ShouldBe(1, "c1054-home-present-refused")`,
  exact fail row and `Trace.ShouldBeEmpty("c1054-refusal-no-execution")`.
  Additional vectors independently label `c1054-missing-refused`,
  `c1054-relative-refused`, `c1054-resolution-refused` and
  `c1054-cache-cleared-refused` on their exit-1 assertions.
- R-2: An approved alias must not be rejected or used as the invocation spelling |
  V-2 method | `Exit.ShouldBe(0, "c1054-alias-accepted")`, then
  `InvokedPath.ShouldBe(Destination, "c1054-invoke-resolved")` and exact argv/HOME.
- R-3: Canonical lookup alone must not authorize a home target |
  V-3 method | first vector
  `Exit.ShouldBe(1, "c1054-canonical-leaf-refused")`; healthy target confirmed
  before the probe, path evidence retained and trace empty.
- R-4: A receipt must not collapse alias/home identity or add physical lines |
  V-4 method | exact expected lookup/path assertions labelled
  `c1054-found-path` and `c1054-resolved-path`; both control-character families
  first assert `PhysicalLines.Length.ShouldBe(1, "c1054-row-single-line")`.
  Unknown fields must equal literal `unavailable`, never canonical fallback.
- R-5: The path gate must not mask a version regression | existing version method
  and two source contracts | each of twelve rows asserts traced canonical
  invocation before its outcome/explanation; the original pin/digest/install
  ordering and wrapper-row assertions remain effective.

### Guard inventory

| Guard | Plan reference and safety-critical invariant | Positive control |
|---|---|---|
| G-1 | D-1/D-2: a discovered home target cannot pass resolved-target equality even with a healthy canonical leaf. | PC-1 |
| G-2 | D-2: an earlier absolute alias resolving to the regular canonical leaf is accepted. | PC-2 |
| G-3 | D-2: canonical lookup spelling cannot override resolved-target/non-symlink-leaf admission. | PC-3 |
| G-4 | D-3: lookupPath always carries the actual found observation, including refusal/unknown states. | PC-4 |
| G-5 | D-3: path always carries the actual resolved observation, including refusal/unknown states. | PC-5 |
| G-6 | D-3: lookupPath is shell-escaped so found-path characters cannot inject physical receipt lines. | PC-6 |
| G-7 | D-1: missing discovery refuses; an off-PATH canonical executable is not an implicit fallback. | PC-7 |
| G-8 | D-1: relative lookup cannot qualify even when readlink would normalize it to canonical. | PC-8 |
| G-9 | D-1: unavailable resolution refuses; canonical cannot be invented as the resolved observation. | PC-9 |
| G-10 | D-1: command cache is cleared before observing the current PATH. | PC-10 |
| G-11 | D-2: the admitted resolved path is the executable actually invoked. | PC-11 |
| G-12 | D-1/D-3: no rejected jq executes, including for diagnostics. | PC-12 |
| G-13 | D-3: path is independently shell-escaped so resolved-target characters cannot inject physical receipt lines. | PC-13 |

Scope census: **guards=13, mapped=13, missing=0, duplicate PC maps=0**.
G-1 and G-3 retain the two different bypass shapes of the compound identity
gate. The canonical `-f`/`-x`/`! -L` terms are recorded in G-3, not omitted: with
real discovery, real resolution and a static directory they cannot independently
admit an invalid leaf while resolved equality remains true. Removing only
`! -L` is therefore an equivalent mutant in this scope and earns no PC credit.
PC-3 deliberately exercises the executable canonical-spelling shortcut proposed
by Plan, retaining the other checks on the alternative path. G-4/G-5 and
G-6/G-13 are split because field assignment and field escaping are independently
breakable. No async delivery/recovery guard exists.

Unchanged safety guards are explicitly outside this card's mutation census:
uid=1654 and result exit conventions (CARD-0660), exact version/exit/empty-stderr
and download digest/root-owned-install (CARD-0927), wrapper row admission, and
host qualification/provision/receipt guards (CARD-1025). V-5 retains the relevant
ordinary regression checks; uid ownership/activation remains S2's runtime
evidence obligation. This design claims no new PCs for those owners. If Code
edits those guards, shared helpers or their policies, return to TestDesign
instead of treating the thirteen-control inventory as covering the extra edit.

### Positive controls

All mutations below modify the copied-from production probe in the sourced
verification checkout, never the test's expectations or native discovery tools.
They are valid Bash edits and leave C# compilation unchanged. Require a single
expected source match; after implementation, Review identifies each concrete
statement fulfilling that description. A missing seam returns to Plan/TestDesign;
Mutation must not improvise a different defect. The method names below are in
`JqRunnerImageContractTests`; every phase's filter is exactly
`/*/*/JqRunnerImageContractTests/<listed-method>` without a class wildcard.

| PC / guard | Compiling defect | Exact method | Required assertion red |
|---|---|---|---|
| PC-1 / G-1 | Replace only resolved-target equality in compound admission with `true`; retain canonical regular/executable/non-symlink checks. | `C1054_Jq_row_rejects_home_shadow` | First canonical-present vector: `c1054-home-present-refused`, actual exit 0 versus expected 1; home stand-in ran successfully. |
| PC-2 / G-2 | Add lookup-equals-destination as a required conjunct of otherwise unchanged path admission. | `C1054_Jq_row_accepts_canonical_file_and_alias` | Direct vector passes; alias `c1054-alias-accepted`, actual exit 1 versus expected 0. |
| PC-3 / G-3 | Admit `lookup == JQ_DESTINATION` as an OR alternative to the complete resolved-target/regular-leaf predicate. | `C1054_Jq_row_rejects_canonical_leaf_symlink` | First executable-home-target vector: `c1054-canonical-leaf-refused`, actual exit 0 versus expected 1. This is not merely deleting the redundant `! -L` term. |
| PC-4 / G-4 | Feed the resolved observation into the lookupPath formatting argument instead of the found observation. | `C1054_Jq_row_records_found_and_resolved_paths` | Accepted alias: `c1054-found-path`, canonical text versus expected alias; success status remains valid. |
| PC-5 / G-5 | Feed the found observation into the path formatting argument instead of the resolved observation. | `C1054_Jq_row_records_found_and_resolved_paths` | Accepted alias: `c1054-resolved-path`, alias text versus expected canonical. |
| PC-6 / G-6 | Change only the lookupPath conversion from `%q` to `%s`. | `C1054_Jq_row_records_found_and_resolved_paths` | Newline-containing accepted alias: `c1054-row-single-line`, more than one physical stdout line. Keep path escaping intact. |
| PC-7 / G-7 | Replace the not-found refusal with assignment of the canonical destination to lookup, then continue normal admission. | `C1054_Jq_row_rejects_home_shadow` | Canonical-present/off-PATH vector: `c1054-missing-refused`, exit 0 versus 1. Other healthy admission checks are reached. |
| PC-8 / G-8 | Remove only the absolute-path requirement from the discovered-file check. | `C1054_Jq_row_rejects_home_shadow` | PATH=`.` vector: `c1054-relative-refused`, exit 0 versus 1 after real resolution to canonical. |
| PC-9 / G-9 | On failed/empty readlink resolution, assign the canonical destination instead of refusing; suppress readlink's tool-error stderr as in ordinary probe. | `C1054_Jq_row_rejects_home_shadow` | Canonical-found/readlink-omitted vector: `c1054-resolution-refused`, exit 0 versus 1. No shell launch or fixture failure counts. |
| PC-10 / G-10 | Replace production `hash -r` with `:`. | `C1054_Jq_row_rejects_home_shadow` | Same-shell seeded-cache vector: `c1054-cache-cleared-refused`, exit 0 versus 1 because cached canonical obscures actual home-first PATH. |
| PC-11 / G-11 | Invoke the admitted lookup path instead of resolved path for `--version`. | `C1054_Jq_row_accepts_canonical_file_and_alias` | Alias vector succeeds qualification but `c1054-invoke-resolved` sees the alias in `$0` rather than canonical destination. |
| PC-12 / G-12 | Immediately before path-refusal emission, run a nonempty found path once with `--version`, redirecting its stdout/stderr to `/dev/null`; preserve refusal status and fields. | `C1054_Jq_row_rejects_home_shadow` | First healthy home shadow passes exit/row checks, then `c1054-refusal-no-execution` sees one forbidden invocation. |
| PC-13 / G-13 | Change only the path conversion from `%q` to `%s`. | `C1054_Jq_row_records_found_and_resolved_paths` | Plain alias to newline-containing unapproved home target: `c1054-row-single-line`, more than one physical stdout line. Keep lookupPath escaping intact. |

Code runs V/R; ordinary Review judges this pending design and the implementation;
Mutation runs baseline/break/red/restore/green **after confirmed land**. Each PC
gets its own exact-method baseline, red and restored-green driver invocation,
`-MinExecuted 1`, exact `-Expect JqRunnerImageContractTests.<method>`, and fresh
phase-specific `bin-c1054-pcN-<phase>/` plus external results directory. Each phase
must execute exactly one result, no skip. These thirteen controls all edit the
same probe file, so they run serially, never as a concurrent batch.

Use the unchanged `scripts/run-checkpoint.ps1` and `scripts/lib/build-slot.ps1`
copies in the SourceLanding external evidence root as prescribed by the testing
owner. The driver takes its own slot. Red requires a completed successful build,
driver exit 1, and the precise assertion above in fresh TRX. Build/fixture/tool
errors, wrong assertion, equivalent mutant, zero tests, skip or timeout earn no
credit. Restore exact source bytes and observe the same method green before the
next defect; refresh restored timestamps/build outputs. Keep all runs foreground
supervised and awaited. The sourced snapshot never commits or pushes; mutation
evidence/restoration belongs in its assigned external root. No mutation is part
of this TestDesign or Code run.

### Out of scope

- Whole Unit/class/namespace/assembly runs, changing test limits/retries/skip
  policy, provider qualification, Docker image builds, rollout/restart or
  installing jq in any standing environment. Private fixtures do not admit
  CARD-1040 S2 execution or close its activation obligation.
- CARD-1025 helper/wrapper/installer implementation and controls, and the
  unchanged CARD-0927/CARD-0660 guards listed above. Their owner artifacts remain
  dependencies; this plan adds no duplicate host admission mechanism.
- Hardlinks, concurrent writers and parent-directory custody policies
  (CARD-1058). They require a different trust/design decision, not a synthetic
  readlink answer. Windows/WSL qualification is excluded by native Linux placement.
- No repeated passing selection or new timeout experiment. A failed existing
  method is compared at the recorded base using that exact selection before
  calling it inherited; a new missing method cannot be tested at a base where
  it does not exist. Report that as NEW, not inherited or zero-test green.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1054-jq/` | linux-jq-path | `/*/*/JqRunnerImageContractTests/(C1054_Jq_row_rejects_home_shadow*)\|(C1054_Jq_row_accepts_canonical_file_and_alias*)\|(C1054_Jq_row_rejects_canonical_leaf_symlink*)\|(C1054_Jq_row_records_found_and_resolved_paths*)` | V-1, V-2, V-3, V-4, R-1, R-2, R-3, R-4 | exactly the four literal C1054 methods listed above, 4 passed, 0 failed/skipped, no extra methods | 4 | 6 | true |
| CP-2 | S1 | CP-1 | linux-jq-pin-regression | `/*/*/JqRunnerImageContractTests/(Version_row_accepts_only_exact_successful_pin_without_stderr*)\|(Pinned_download_is_verified_before_root_owned_install_in_every_runner_target*)\|(Qualification_grades_the_jq_row_for_both_targets*)` | V-5, R-5 | exactly 12 argument-expanded version results and the 2 literal source-contract methods, 14 passed, 0 failed/skipped, no extra methods | 14 | 3 | true |

The union is the complete ordinary executable scope: seven unique methods,
eighteen TUnit executions, **one isolated build** reused only within committed S1.
Internal vector/assertion counts are not `Min`. Prefix stars are the pinned OR
discovery convention; compare fresh TRX method names/counts for exact equality,
including all twelve version argument sets. CP-1/CP-2 minima alone cannot prove
that equality or the receipt/trace assertions.

Bootstrap the checkpoint tool once through the host build-slot gate:

```powershell
pwsh -NoProfile -File scripts/build-slot.ps1 -Label c1054-tool -- dotnet build tools/Antiphon.Checkpoints --property:OutputPath=bin-c1054-tool/ --property:UseAppHost=false --nologo
$c1054Source = (git rev-parse HEAD).Trim()
dotnet tools/Antiphon.Checkpoints/bin-c1054-tool/Antiphon.Checkpoints.dll run --plan docs/superpowers/plans/2026-10-05-card-1054-jq-path-qualification-plan.md --after S1 --serial --expected-source-sha $c1054Source --max-wait 50s
```

The tool owns each row/build lease; do not wrap it in a second slot. If it returns
75, use the emitted run ID with `wait --max-wait 50s` until terminal. Source is
committed/pushed before running and frozen until every owned driver exits.
Derived row deadlines are 18 and 15 minutes; total derived deadline is 28
minutes. These are limits, not estimates or reasons to retry. Slot timeout=not
run. Keep unedited CHECKPOINT lines, actual rosters, selected SHA and validated
clean source/build provenance in the Code report; generated receipts stay ignored.
Clean exact task-owned alternate outputs, including tool outputs, after exit.

S2 has no build/test row. Check the two documentary row contracts against the
grammar above and confirm CARD-1040's three checkpoint rows, fifteen names,
18-minute floor and all activation/uid/digest/owner/mode obligations survive
unchanged. Record the reviewed S1 SHA for that owner. Run `git diff --check`,
resolve edited relative links, and Code/Review run
`scripts/check-evidence-diff.ps1` over their complete task range. This static
document review is not an additional TUnit group or activation receipt.

### Cost

- **Ordinary V/R floor (Code), estimated: 9 minutes** = CP-1 four-method OR,
  6 minutes (isolated build 3, fixture startup/execution/teardown 3), plus CP-2
  three-method OR, 3 minutes (reused build, fourteen results). Exact filters are
  frozen in the table. No whole Unit or CARD-1040 consumer run is included.
- **Setup outside the rows, estimated: 3 minutes** = tool bootstrap build 2
  plus static document/link/manifest/source-receipt preparation 1. Code-side
  verification total is **12 minutes**, excluding implementation authoring.
- **PC floor (Mutation), estimated: 130 minutes**. Each of PC-1..PC-13 uses its
  exact method filter above: baseline build/run 3 + defect application 0.5 +
  red build/run 3 + byte restoration 0.5 + restored-green build/run 3 = 10
  minutes. Totals: 39 baseline + 39 red + 39 green + 6.5 break + 6.5 restore.
  Phase builds are included, not charged twice. They cannot share mutation
  builds because they touch the same script and each phase needs its own receipt.
- **Total verification estimate: 142 minutes** = setup 3 + ordinary 9 + PCs
  130. These are estimates, not measured runs. Split commissioning by the
  exact-method controls if needed; Code never absorbs the 130-minute Mutation
  floor. Four primary controls account for 40 minutes; nine required guard
  splits account for 90. Runtime activation waits and host slot contention are
  external waits to report separately, not hidden inside authoring or PC time.
- CP-2 reuse saves **one estimated 3-minute isolated test-project build** versus
  rebuilding both ordinary rows. No additional wall saving is claimed against
  historical broad runs: this bounded scope has no fresh timing measurement.
  One ordinary pass is exactly eighteen results, with no duplicate host/image
  battery or post-green repeat.

Handoff audit: selected test/fixture/helper bodies read; all boundary vectors
above assigned or explicitly excluded; guards=13, mapped=13, missing=0,
duplicate PC maps=0; all thirteen PCs specify executable Bash defects and exact
method/assertion outcomes. Fixture work is concrete S1 test implementation, not
an unverifiable production seam. Numeric floors: ordinary=9, setup=3, PC=130,
total=142 minutes. No build, test or mutation was run by TestDesign; static
manifest/roster/link checks are the available design validation. Code may proceed
on native Linux; activation and all deliberate controls remain separate gates.

--- next stage ---
next: code
handoff: Implement CARD-1054 S1/S2 with the frozen receipt grammar and four private-filesystem methods; run the sole 18-result Linux checkpoint manifest. Keep all 13 exact-method PCs pending post-land, reuse CARD-1025 host admission, and preserve CARD-1040 S2 activation plus its fifteen-consumer gate.
artifact: docs/superpowers/plans/2026-10-05-card-1054-jq-path-qualification-plan.md
