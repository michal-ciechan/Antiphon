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

### Checkpoints

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
