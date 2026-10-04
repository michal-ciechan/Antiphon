# CARD-1031: observe the Codex version despite notices

Plan task: `96d373ab-90a5-4e0c-8971-9490fbeb2c37`, 2026-10-04.
Assigned branch: `feat/card-task-96d373ab`; immutable task base:
`6a88d8ceaedb5934bb3a466802a622a4b56e5b37`.
Current master inspected: `c9e9c4b505dcf2d9a8ec6c9aa00107b0a6eeb307`.
The probe, parser, refresh service, contracts and observation projection are
unchanged between those two commits. This task changes this plan only.

## Outcome and stage boundary

The defect is confirmed. `src/Antiphon.SessionRunner/CodexCliVersionProbe.cs:171`
sets `error = "stderr_output"` whenever the completed child's stderr contains any
bytes. Line 172 parses stdout only when error is null; line 173 therefore returns
an unknown version even if stdout contains a valid banner and the exit code is 0.
Lines 169-170 give truncation and nonzero exit precedence over this rule.

There are two narrower corrections to the brief. The repository has **no separate
Codex stderr diagnostic field, error-token enum, or free-text stderr sanitiser**.
It has the string `CodexCliVersionError`, a fixed-token projection allowlist, and
a bounded stderr capture that is discarded. Also, the actual desktop warning
text is not established: an npm update notice is a hypothesis, not a captured
receipt. Neither correction undermines the confirmed stderr veto.

This plan is written under the explicit D-3 diagnostic representation default.
The caller must accept that default or select its alternative before TestDesign
freezes verification. **Next: decide**, then separate **test-design**. The brief
did not fold TestDesign into Plan; the proposed checkpoint table below is not yet
Code's executable freeze. No production edit, build, test, installed CLI command,
auth read, live probe POST, restart, provider turn or rollout ran in this task.

## Ground truth

All line anchors below refer to the task base and the inspected master, whose
relevant files match. Historical CARD-0959 expectations are superseded only where
this plan explicitly names the changed notice behavior.

| Card assumption | What the code/evidence actually says | Consequence |
|---|---|---|
| Any stderr notice loses a working version. | Probe `:167-174`: `stderr_output` prevents `ParseBanner`; `Unknown` at `:292` emits null version plus completion time and token. | Remove stderr as a success veto; keep stderr diagnostic-only. |
| The real desktop output is an npm/Node update notice. | Card reports build `6a88d8ce`, null version and `stderr_output`. A fresh catalogue GET during this task again returned null version, `stderr_output`, checked-at `2026-10-04T03:49:18.6226942+00:00`. Neither surface includes stdout/stderr. | Confirms the error state, not its text or installed version. Do not label a synthetic npm notice as a desktop capture. |
| The launcher necessarily executes npm. | Probe `:241-251` recognizes the stock Windows npm shim and normalizes it through `CodexWindowsLaunchPolicy` to native Node plus `codex.js --version`. | npm installation layout is not proof that npm itself emitted stderr. Codex/Node/launcher output cannot be distinguished from the retained evidence. |
| The existing parser can find a banner among notices. | `src/Antiphon.SessionRunner.Contracts/CodexCliVersion.cs:25-35` accepts exactly one whole banner, at most one LF/CRLF terminator; `Parse` at `:37-52` validates SemVer. | Removing line 171 alone does not fix surrounding stdout notices. Scan lines without weakening SemVer validation. |
| A diagnostic field and enum can be updated. | `CodexCliVersionContracts.cs:11-16` has version/time/error/fingerprint, with fixed-token errors. `server/Application/Services/CodexCliObservation.cs:17-28` is the public token allowlist, not an enum or text redactor. | D-3 reuses the existing token field; do not invent an existing diagnostic-text property. |
| A returned version plus diagnostic will already be fresh. | `CodexCliObservation.cs:9-14` returns null stale whenever `DisplayError` is non-null. The latter also returns a supplied error before checking skew/fingerprint. | D-3 requires explicit advisory classification and structural-diagnostic precedence. |
| Truncation is just a storage bound. | Probe `:15`, `:169`, `:294-306`: 4096 retained bytes per stream, drains the rest, but either truncated stream currently causes unknown. | Keep drain and memory bounds. A complete retained version survives excess notices; a cut-off candidate is not a version. |
| Existing fixtures reproduce the failure shape. | `Fixtures/CodexVersionChild.ps1:61-65` writes `codex-cli 0.160.0` to stdout, then `E` or `C959-diagnostic-sentinel` to stderr, exit 0. Tests at `CodexCliVersionProbeTests.cs:134-160` currently require failure. | Existing fixtures prove the exact shape in source. Add recognizable synthetic notices and replace the contradictory assertions. No fixture was executed here. |
| Windows tests are sealed from installed Codex. | `CodexCliVersionTestFixture.cs:32-36` creates an actual empty directory because empty PATH text can fall back to host PATH. `CodexCliVersionWindowsTests.cs:60,88` passes `kit.EmptyPath`; process I/O is redirected to the owned PowerShell child at fixture `:115-143`. | Preserve that directory and owned npm/Node layouts. Never substitute empty text or the host PATH for the sealed resolution path. |
| The probe is used to admit work. | CARD-0959's active re-freeze at plan `:1835` removed all CLI admission, including old-version refusal. Refresh service `:14-21` only refreshes background observation. Four `C959_*_never_refuses` methods remain in `CodexCliObservationTests.cs:293-350`. | No CLI gate, launch-time reprobe, provider-auth change or compatibility policy belongs here. CARD-1023 remains the future consumer. |

The real desktop warning cannot be reconstructed from these discarded bytes.
If identifying its cause later becomes necessary, commission metadata-only
classification of an already scheduled probe (exit status, bounded byte counts,
recognized banner presence and an allowlisted notice category). Do not enable raw
stderr logging, inspect auth homes, or manually run installed Codex for this plan.
The fix does not depend on the warning's exact wording.

## Decisions

### D-1: first parseable stdout banner, preserving supported forms

Change `CodexCliVersion.ParseBanner` to examine bounded stdout lines in order.
The first whole line matching a recognized banner with a valid SemVer wins;
`codex-cli <semver>` is the primary form. Retain the already supported aliases
(`codex`, optional `version`, optional `v`, case-insensitive banner prefix) to
avoid an unrelated parsing regression. Keep strict SemVer, prerelease/build
metadata and ordering unchanged. Skip blank lines, unrelated notices and malformed
candidate lines. Use LF/CRLF; allow a final unterminated line only when capture
reached EOF without truncation. Two valid lines select the first, not the latest
version. Do not trim arbitrary banner content or match an embedded substring.

Reason: the card asks for notices around a stdout version. Reject parsing stderr,
merging the streams, accepting a bare number, or accepting the last banner: each
can turn an unrelated notice into version evidence. Reject deleting supported
alias forms as unnecessary scope.

### D-2: completed exit-zero process plus parseable evidence means success

After both streams and the process complete, nonzero exit remains unknown even
with a valid banner. Otherwise parse stdout independently of stderr presence or
truncation. Keep the 4096-byte per-stream cap, concurrent draining, closed stdin,
five-second deadline, two-second cleanup ownership, sanitized environment,
scratch home/cwd, launcher verification, cache and single-flight unchanged.

If stdout was truncated, consider only complete lines ending within the retained
prefix. Drop its last unterminated fragment before parsing; a prefix such as
`codex-cli 0.160.0` cut off before more version bytes must not become false success.
A complete first banner followed by arbitrarily long finite notices is success.
A banner appearing only after the retained prefix is unknown: do not raise the cap
or scan unbounded data. Timeout remains unknown even if a banner arrived earlier.

For normally completed children the only success blockers are nonzero exit and
no parseable retained version. Timeout/cancellation and pre-execution failures
(missing/unverified executable, busy probe, unresolved cleanup) retain their
existing contracts. The card's wording does not authorize spawning an unverified
launcher or turning process-ownership failures into success.

### D-3: stated default — retain fixed advisory tokens in the existing field

**Recommended caller default:** reuse `CodexCliVersionError` as the existing
diagnostic token field. When the child succeeds and a version is parsed, return
that version, the completed timestamp and fingerprint, with `stderr_output` for
nonempty bounded stderr, or `output_truncated` if either captured stream exceeded
the cap. Truncation takes advisory precedence over ordinary stderr. No notice
means null diagnostic. Keep only these fixed tokens; never decode stderr into
a returned/logged string. They disclose neither user paths nor tokens. Clarify the
DTO documentation: this legacy field name contains both failure and advisory
diagnostics; a diagnostic is not a task-admission decision.

| Completed output | Version | Existing diagnostic field |
|---|---|---|
| exit 0, complete parseable stdout, no stderr/overflow | parsed version | null |
| exit 0, complete parseable stdout, stderr notice | parsed version | `stderr_output` (advisory) |
| exit 0, complete retained banner, either stream over cap | parsed version | `output_truncated` (advisory) |
| nonzero exit, including with banner/notices/overflow | null | `nonzero_exit` |
| exit 0, no parseable retained stdout, capture complete | null | `invalid_output` |
| exit 0, no parseable retained stdout, stdout truncated | null | `output_truncated` (unknown evidence) |
| timeout or unconfirmed cleanup | null | existing `timeout` / `cleanup_unconfirmed` |

In `CodexCliObservation`, only `stderr_output` and `output_truncated` accompanying
a valid version may be advisory. Unknown strings still become `probe_unavailable`;
other errors remain failures even if a peer supplies a version. Missing/invalid
version or missing time still gives null freshness. Preserve the fifteen-minute
boundary and one-minute future tolerance. For advisory samples check clock skew
and malformed non-null fingerprint before reporting the advisory; those structural
diagnostics still yield null freshness. A legacy null fingerprint remains allowed.
Never derive dispatch eligibility from any of this.

Reason: this is the smallest way to retain safe diagnostics in the **existing**
field while recovering both version and freshness. It adds no schema, configuration,
enum, raw-text sanitiser or new transport field. Old server readers can already
display the returned version but give null freshness for its advisory until their
projection is updated; that temporary state remains refusal-free.

**Caller choice required before TestDesign:** accept these advisory semantics for
the legacy field. The alternative is a new dedicated nullable diagnostic token
property with successful `CodexCliVersionError = null`; that needs a wider contract,
runtime, registration/heartbeat and catalogue/status change and renewed collision
checks with CARD-1022/1010. Silently discarding successful stderr diagnostics or
retaining sanitized free text is not the planned alternative. No evidence here
justifies a generic path/token redaction engine.

### D-4: retain observation-only behavior and use the nearest fixtures

No changes to `AgentTaskService`, `AgentTaskDispatcher`, provider auth, models,
placement, queues, backend selection or compatibility floors. Keep the four
never-refuses methods and their delivery assertions. CARD-1023 must treat
advisories as observation, never an incompatibility refusal. No installed-version
upgrade is a prerequisite for this fix. Add test vectors through the existing
owned child and its process-I/O seam; never invoke installed Codex or spend a turn.

### D-5: qualify native Windows separately and activate reporting runners first

Linux proof does not discharge Windows. The caller commissions a separate Debug
task with `-Platform Windows`, at the exact final pushed Code SHA C. Omit `-Runner`
unless deliberately pinning a host. Portable work omits both placement flags;
`-Platform Any` only clears an inherited constraint. Read defaults and inventory
again before commissioning; do not embed a fleet address or checkout in a command.

## Implementation slices

Commit and push each coherent slice before checkpoint execution. S1 and S2 form
the final tested source group; do not edit tracked source during a running group.

| Slice | Files | Work and test boundary |
|---|---|---|
| S1: version extraction and advisory sample | `src/Antiphon.SessionRunner/CodexCliVersionProbe.cs`; `src/Antiphon.SessionRunner.Contracts/CodexCliVersion.cs`; `src/Antiphon.SessionRunner.Contracts/CodexCliVersionContracts.cs`; `tests/Antiphon.SessionRunner.Tests/CodexCliVersionProbeTests.cs`; `tests/Antiphon.SessionRunner.Tests/CodexCliVersionWindowsTests.cs`; `tests/Antiphon.SessionRunner.Tests/Fixtures/CodexVersionChild.ps1` | D-1/D-2/D-3 producer changes, contract comment, parser vectors, owned stderr-notice/overflow/failure fixtures and Windows npm-layout notice case. Preserve existing fixture custody and sealed PATH. No edit to `CodexCliVersionTestFixture.cs` is expected unless a narrow fixture input extension is needed. |
| S2: advisory display and living contract | `server/Application/Services/CodexCliObservation.cs`; `tests/Antiphon.Tests/Application/RunnerCodexCliEvidenceTests.cs`; `docs/ops-http.md`; this plan's later verification freeze | D-3 projection classification, freshness/serialization/privacy cases and operator documentation. Existing refresh/transport/runtime producers remain structurally unchanged. Run final portable rows and caller-commissioned Windows Debug at C; preserve four existing refusal-free methods without changing their oracles. |

Suggested explicit scope is the literal paths above, not the entire runner or
delegation area. `CodexCliVersionRefreshService.cs`, launch policy, capability
constructors, phone-home DTOs and directory are read-only dependencies. The older
CARD-0959 plan remains historical provenance; record superseded PC expectations
in this card's TestDesign ledger instead of editing a second owner's large plan.

## Verification requirements for separate TestDesign

TestDesign must add the final `## Verification design`, bind each production
branch/assertion to exact methods and controls, validate the importer, and freeze
counts/cost. The method names below are proposed implementation obligations,
not claims about tests already present or executed.

| Requirement | Files / exact method obligations | Decisive evidence |
|---|---|---|
| V-1: parse notices deterministically | Extend `CodexCliVersionProbeTests.C959_Parses_and_orders_versions` | LF/CRLF and EOF banner; notices before/after; blank lines; invalid candidate then valid; two different valid versions choose first; legacy forms, prerelease/build metadata retained; malformed SemVer, bare numbers and embedded/trailing junk stay rejected. Replace only the old duplicate-banner/blank-line rejection vectors. |
| V-2: stderr is diagnostic-only | New `CodexCliVersionProbeTests.C1031_Stderr_notice_preserves_version` | Owned fake writes a literal synthetic npm/Node-style notice plus synthetic Windows/Unix user paths and token canary to stderr, canonical version to stdout, exit 0. Assert exact version, advisory token, completed timestamp/fingerprint and exited child. Capture console output safely in a serial test; no canary/path appears in serialized sample, capabilities, registration or logs. Refresh through failure -> notice success -> quiet success; diagnostic clears, checked-at changes only on completed attempt. |
| V-3: bounded evidence | New `CodexCliVersionProbeTests.C1031_Truncated_notices_preserve_complete_version` | stderr >4096 plus valid stdout succeeds; complete banner then stdout flood succeeds; flood then banner outside cap remains unknown; cut-off final banner fragment is never accepted; boundary 4096/4097 and CRLF across the cap. Both pipes drain and children exit. |
| V-4: failures remain failures | New `CodexCliVersionProbeTests.C1031_Invalid_or_failed_output_stays_unknown`; retain `C959_Failure_bounds_and_cleanup` | nonzero with valid stdout/stderr (including overflow) remains `nonzero_exit`; stderr-only banner and malformed/no stdout become `invalid_output`; real timeout/unresolved descendants remain unknown. Update old stderr failure vectors to the new contract, preserving all cleanup/cancellation assertions. |
| V-5: native Windows npm resolution | New `CodexCliVersionWindowsTests.C1031_Npm_notice_preserves_version`; retain all three `C959_*` methods | Same synthetic notice succeeds through recognized npm shim and direct Node/native resolution. Use owned sibling Node and owned PATH-Node layouts; otherwise `kit.EmptyPath`, an existing empty directory, never host PATH or empty string. Assert actual recorded argv, no auth opens, ownership and exact sample. |
| V-6: projection semantics | New `RunnerCodexCliEvidenceTests.C1031_Advisory_diagnostics_preserve_freshness`; retain all five `C959_*` methods | Inject valid version + each advisory through local capabilities and remote registration/heartbeat. Catalogue and status retain version/token, fresh at 15m and stale after; reads do not renew time. Missing version/time, unknown diagnostic, true failure, skew and bad fingerprint stay unknown as specified. Private fingerprint/raw sentinel absent from public output. |
| R-1: observation never refuses | Existing `CodexCliObservationTests.C959_Probe_failure_never_refuses`, `C959_Stale_sample_never_refuses`, `C959_Unknown_version_never_refuses`, `C959_Old_version_with_floor_never_refuses` | Retain actual create/retry/dispatch/reuse/delivery and zero typed-probe calls. No CLI-derived task gate is introduced; existing auth/model/placement policies retain their owners. |
| R-2: probe isolation, cache and refresh | Existing `C959_Probe_is_version_only_and_auth_free`, `C959_Refresh_replaces_evidence`, `C959_Local_and_registration_share_snapshot` | Preserve sanitized env, stdin EOF, owned scratch, cache/single-flight, refresh completion time and snapshot sharing. Replace the diagnostic fixture's old unknown-version assertion with successful version plus fixed advisory; keep no-leak assertions. |

At minimum, design method-scoped post-land controls that restore the stderr veto,
restore whole-buffer parsing, choose the last banner, parse stderr, suppress nonzero
exit, reject valid evidence on stderr overflow, accept a truncated terminal
fragment, leak a synthetic stderr canary, treat every diagnostic as failed freshness,
and let an advisory bypass skew/fingerprint validation. Name each decisive assertion.
Controls are not executed in Plan/Code; Mutation follows ordinary Review and land.
Retarget historical C959 PC-011/029/030/031 and the changed parser/diagnostic
expectations explicitly rather than carrying their superseded oracle forward.

### Checkpoints

Proposed closed Final scope **after D-3 decision and TestDesign freeze**. Each row
names one isolated build, one exact filter and its lane. All rows require zero
failed/skipped results and a clean source receipt for C. Counts are TUnit method
executions: existing 5 + proposed 3 portable probe, existing 5 + proposed 1 wire,
four existing refusal-free methods, and 8 portable + (3 existing + 1 new) Windows.
No dynamic/argument expansion is proposed; TestDesign must recount if that changes.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---:|---:|---|---|
| CP-1 | S1-S2 | `tests/Antiphon.SessionRunner.Tests -> bin-c1031-linux-probe/` | linux-probe | `/*/*/CodexCliVersionProbeTests/*` | V-1..4, R-2 | 8 methods, 0 failed/skipped | 8 | 8 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-2 | S1-S2 | `tests/Antiphon.Tests -> bin-c1031-linux-observation/` | linux-observation | `/*/*/RunnerCodexCliEvidenceTests/*` | V-6 | 6 methods, 0 failed/skipped | 6 | 8 | true | `C804_ORPHAN_SWEEP_ROOT=c1031-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-3 | S1-S2 | `tests/Antiphon.Tests -> bin-c1031-linux-no-refusal/` | linux-no-refusal | `/*/*/CodexCliObservationTests/C959_*_never_refuses` | R-1 | all 4 named methods, 0 failed/skipped | 4 | 12 | true | `C804_ORPHAN_SWEEP_ROOT=c1031-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-4 | S1-S2 | `tests/Antiphon.SessionRunner.Tests -> bin-c1031-windows-probe/` | windows-native-debug | `/*/*/(CodexCliVersionProbeTests)\|(CodexCliVersionWindowsTests)/*` | V-1..5, R-2 | 12 methods on native Windows at C, 0 failed/skipped | 12 | 14 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |

One source group, one checkpoint-tool run per commissioned lane. Bootstrap the
checkpoint tool under `scripts/build-slot.ps1` into `bin-c1031-tool/`; this is the
declared prerequisite build. Invoke its built DLL with `run --plan <this-plan>
--rows CP-1,CP-2,CP-3 --expected-source-sha <C> --serial`; Windows Debug selects
`--rows CP-4`. Continue `wait` while exit is 75; await every owned run. Slots are
mandatory, exit 4 is not run, and native Windows skips cannot discharge CP-4.
Use the checkpoint runner's TUnit `dotnet run` driver, never `dotnet test`.

Keep source frozen for all qualification; later repairs need a new C and affected
portable reruns plus final-C Windows proof. Preserve unedited CHECKPOINT lines,
counts and source/build provenance in the stored report. Validate receipts against
C, run `scripts/check-evidence-diff.ps1` over the full task range, and remove only
owned alternate output directories after children finish. Generated evidence
remains ignored. Confirm inherited reds by the failing method at the recorded
base, never by weakening assertions, retries or timeouts. Review verdicts are
regression-only; unrelated defects stay with their owners.

No whole Unit/PTY/E2E run is proposed: this narrow parser/observation repair has
explicit process, projection and no-refusal coverage. Additional drivers require
a named uncovered invariant and reason. It does not inherit all 192 CARD-0959 PCs.

### Cost

Ordinary proposed floor: 8 + 8 + 12 + 14 = **42 minutes**, 30 executions across
both lanes (including the eight repeated portable methods on Windows). Allow
6 minutes for two tool bootstraps/import/receipt handling: **48 minutes** before
authoring, queue/slot wait or repairs. Estimated authoring 30 minutes gives 78.
These are estimates, not measured results. TestDesign must price its final guard
inventory and method-scoped baseline/red/restore/green controls separately; no
mutation floor is frozen here. Ordinary independent Review remains mandatory.

## Placement and collision ledger

Read-only `GET /api/runner-defaults` returned revision 2, no kind overrides and a
Linux default. `GET /api/session-runners` showed a live eligible Windows desktop,
a live eligible Linux runner, and an unavailable drained temporary runner. These
are observations, not hard-coded placement. Pipeline reread at
`2026-10-04T03:56:05.5144402Z` showed CARD-1017 Code and the docs batch active;
CARD-0983 Code had settled and its separate Windows Debug was active. Re-read
effective routes, occupancy, host limits and file footprints before dispatch.

| Work / inspected reference | Boundary and sequencing |
|---|---|
| CARD-1017 Code `13c90416`, tip `f384513257069626b5cab06dbde043216f0af73d` | Actual diff owns completed-card cleanup, land/retirement services, filesystem/Git helpers, DB/migration and cleanup tests. No direct planned C1031 source edit overlap. Preserve those changes and its evidence-custody rules; no worktree cleanup repair here. |
| CARD-0983 Code `608ed47c`, tip `cb95a4c3c171df7a55bfd06266e17980712c68fb` | Succeeded by the live read; Windows Debug `80ff713f` active. Owns require-jq scripts, RemoteScript/RollingVolumeRecycle tests and testing/build docs. No direct edit overlap; share Windows/build capacity only. No deploy-script or jq repair here. |
| CARD-1022 TestDesign `1b2000a7`, freeze `3e3436b329da772134c24c8ce4925063bf7d898a` | Settled; proposed ModernConPty work owns runtime/capability constructors, `SessionRunnerContracts.cs`, Program and backend tests. D-3 deliberately avoids new transported fields and those files. Recheck constructor/test changes after its landing; do not change backend pins/defaults. |
| CARD-1021 freeze `bda1fe51071c3104d1b940294c6f46913e37288f` | Owns exhausted routing/placement persistence and its tests. Keep AgentTaskService/dispatcher read-only; do not confuse observation failure with durable routing Blocked behavior. |
| CARD-1030 freeze `740245f4d4f9c809d0e5f881d3a55054ebf0a92b` | Owns C1008 Windows host-fixture/RemoteScript repair and native WSL proof. No shared test helper edits needed; do not repair its inherited reds in this task. Coordinate the separate Windows slot. |
| CARD-1010 freeze `2395858ecc0a6ab2e8acc3b2deb2cb6298ed1307` | Owns phone-home replacement-admission DTO/directory, ops/API docs and later explicit recycle work. Only intended direct overlap is the living `docs/ops-http.md`; merge that paragraph carefully. A new diagnostic field alternative would expand the collision to its DTO/directory and require resequencing. |
| CARD-1027 docs batch `167e5110` | Live docs work can change routing/qualification instructions. Use the then-current living owner documents, preserving its amendments. |
| Checkpoint census | Current-master `scripts/lib/checkpoint-usage.ps1:114` is literally **377**. This card adds no checkpoint-namespace test and must leave the literal and coverage tooling unchanged. |

## Activation and acceptance after Review and land

The caller owns activation; nothing is restarted from this plan worktree. Record
Code C, Review evidence and confirmed landed L separately. Sync the canonical
checkout to the intended landed source before building/restarting anything.

1. **Desktop reporting runner first.** From the canonical Windows checkout, inspect
   `logs/apphost.restart.lock` and `logs/apphost.launch.lock`, then use
   `pwsh -NoProfile -File scripts/restart-session-runner.ps1`. Follow the existing
   restart owner for wait expiry; `-WaitOnly` observes without another restart.
   Do not use `-AllowWorktree`, `-KillSessions`, or a blind second restart. A hard
   supervisor refresh is only for a separately established need.
2. Confirm the actual runner build SHA and feature, then GET its memory capability
   snapshot and server catalogue. Require a parseable version and a completion
   time after restart, with only the fixed advisory if a notice persists. Do not
   invent the expected installed version or claim green from health alone. Let
   the ordinary five-minute refresh run twice and observe advancing attempt times;
   reads/heartbeats alone must not renew them. No manual installed CLI invocation,
   diagnostic POST, auth read or provider canary is part of acceptance.
3. **Server reader next**, because D-3 changes the freshness projection. Activate
   through the canonical AppHost restart procedure after reporting-runner proof;
   verify `/api/version` against intended source and verify actual version/error/
   stale fields. The older reader's interim null freshness is documented, not a
   CLI admission failure. Old runners without observations continue to show unknown.
4. **server2 reporting runner at the next staged rollout.** Follow
   `docs/docker-stack.md`'s existing named phases and stop gates. Do not initiate
   volume recycling, install a CLI or bypass an active deployment owner for this
   metadata fix. Observe build identity and registration/heartbeat evidence over
   two refresh periods after activation; record this pending lane if not yet rolled.

Rollback restores the prior reader/runner through the same sanctioned lanes and
may restore unknown/stale-null display; it must never add a compatibility gate.
Fixture qualification, publication and live activation are separate facts.

## Caller decision and handoff

Accept D-3's fixed advisory tokens in `CodexCliVersionError`, or commission the
wider dedicated-diagnostic-field alternative. The plan is complete under the
recommended default; no answer was needed to make the proposal reviewable.
After that choice, commission TestDesign to freeze the proposed methods, red
controls, counts and checkpoint importer, then Code, separate final-C native
Windows Debug, regression-only ordinary Review, land and activation. Post-land
Mutation remains separately commissioned under the repository's custody rules.

--- next stage ---
next: decide
handoff: Accept D-3: retain only fixed stderr_output/output_truncated advisories in the existing error field alongside a valid version, with advisory-aware freshness; otherwise choose a dedicated diagnostic field. Then commission separate TestDesign to freeze methods, controls and four checkpoints, including final-SHA Windows Debug.
artifact: docs/superpowers/plans/2026-10-04-card-1031-codex-version-stderr-plan.md
