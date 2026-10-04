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

### Proposed checkpoints (historical; superseded by TestDesign below)

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

### Proposed cost (historical; superseded by TestDesign below)

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

## Verification design

TestDesign freeze: task `3fa63bb3`, 2026-10-04. The commissioning brief explicitly
accepts D-3 under the operator's allow-by-default principle. This appendix
supersedes the historical decision/handoff and proposed verification above; it
leaves D-1 through D-5 and the fix design intact. Only the two historical table
headings were relabelled: the importer consumes the **first** exact
`### Checkpoints` heading, so there must be just one active heading.

Fetched current master inspected: `4246196ddc980e5a35dc1e3d613b785cc25a773c`.
`git diff 6a88d8ce origin/master --` over the probe, parser, sample DTO,
observation projection and all five named test/fixture files is empty. The
veto remains at probe lines 171-173; the capture cap remains 4096 at line 15;
the four sample fields remain at contract lines 11-16. No contract or line-number
drift needs reconciliation. The checkpoint importer/census also match this
branch. This finding does not claim that all of master is unchanged.

**Land this plan plus freeze doc-only first. Code must start a fresh branch from
then-current master containing the landed document. Never merge master into the
old Plan/TestDesign branch.** Recheck the literal footprint at that new base;
report conflicting source changes before implementing against a stale design.

### Inspection

Bodies read, rather than names inferred from search:

- `src/Antiphon.SessionRunner/CodexCliVersionProbe.cs`: `ProbeAsync`, `CompleteAsync`, `RunAsync`, `CaptureAsync`, resolution and cleanup; `CodexCliVersionRefreshService.ExecuteAsync`; `src/Antiphon.SessionRunner.Contracts/CodexCliVersion.cs` and `CodexCliVersionContracts.cs` | parse/completion/capture/sample boundaries -> V-1..4, R-2.
- `server/Application/Services/CodexCliObservation.cs`: both projection methods; `PhoneHomeConnectionService.SendHeartbeatAsync`/heartbeat loop; `PhoneHomeRunnerDirectory.ObserveCodexCli`/`LatestCapabilities`; runtime/adapter snapshot field mappings and heartbeat DTO | advisory versus failure, age, skew, fingerprint and receiver projection -> V-6, V-7, R-3.
- Entire `CodexCliVersionProbeTests.cs` (5 `[Test]` bodies), `CodexCliVersionWindowsTests.cs` (3), `CodexCliVersionTestFixture.cs`, `Fixtures/CodexVersionChild.ps1`, and `CodexNpmLayout.cs` | parser aliases, private HOME, owned processes, 4096/4097, cancellation/reaping, actual empty PATH directory and npm layouts -> V-1..5, R-2. Existing diagnostic and stderr expectations need replacement; never delete the other C959 oracles.
- All 5 `RunnerCodexCliEvidenceTests` bodies, including `ProbeIo`, `ControlledSendSocket`, `Heartbeat`, `Caps`, `Sample`, serialization helpers; `PhoneHomeTestHost.StartAsync`, registration/connect/wait helpers and `CapturingLoggerProvider` | real random-port HTTP/WebSocket path, held writer, failure before/after send, reconnect, public JSON and logs -> V-6, V-7, R-3. No new test file is needed.
- Four `CodexCliObservationTests.C959_*_never_refuses` bodies; their `NeverRefusesAsync`, `AssertWarmReuseAsync`, `AssertDeliveryAsync`, `Kit`, `DispatchKit`, `BriefBoundary`, fake adapter submission callback; `CodexCliRemoteDeliveryFixture.RunAsync` and its freeze/launch/recipient seams | real queue and complete recipient W plus durable E, local Retry after lost claim, remote eligible/busy -> R-1. The provider/terminal itself is scripted. Broader crash/degraded variants remain CARD-1029, as detailed below.
- `AgentTaskService.CreateAsync` and `RetryAsync` entry blocks; dispatcher claim/reuse/delivery blocks | read-only insertion sites for R-1 controls. No implementation edit in these services is authorized by this fix.
- `PlanTableImporter` in full, `AfterSelector`, `ManifestValidator`, `ManifestLoader`, `Program.Import`, testing/build checkpoint and mutation contracts; `scripts/lib/checkpoint-usage.ps1:114` | exact heading, escaped OR, nine required columns, output path, floors and lane selection -> active manifest. Census is literally **377**; no tests enter namespace `Antiphon.Tests.Checkpoints`, and no census/tool edit is required.

Missing setup is an implementation obligation, not an existing capability:
add fixed owned child modes for the vectors below, and assertion labels named in
this freeze. Existing `Mode`/`ChildMode`, clocks, `StopTreeAsync`, `ProbeIo.Mode`
and the socket wrapper suffice; no production seam or installed CLI is needed.
Keep the assembly-local `ParallelLimiter<ProcessSpawnLimit>`. Console capture
uses `[NotInParallel]` with both writers restored in `finally`. Each fixture owns
its process handles and cleanup. A setup/compile error or zero tests is never PC red.

### Delivery inventory

There is no new queue, durable outbox or session-input path in D-1..D-3. Changed
content travels over the existing observation paths. Observation samples are
**memory-only**, and are not promised durable/exactly-once delivery. Their joined
identity is runner ID + store/boot + connection epoch + launcher fingerprint +
completed-at; epoch/boot are enclosing transport identity, not new sample fields.
A frame request ID alone does not identify a completed probe.

| Path | Producer -> recipient | Persistence and recovery | Observable receipt / test |
|---|---|---|---|
| Local capabilities | owned child -> completed probe Snapshot -> runtime capabilities -> server catalogue | Process completion publishes one memory sample; failure replaces it; process restart starts unknown and startup refresh reacquires; reads do not probe or advance its clock | V-2 and V-7 compare exact version/token/time/fingerprint at producer and consumer; no queue exists to enqueue or crash-replay here |
| Registration | Snapshot -> `PhoneHomeRuntimeAdapter.Capabilities` -> real registration endpoint/directory -> catalogue/status | Accepted store/boot/epoch replaces memory observation; lost/rejected registration publishes no new observation; reconnect sends the current sample | V-7 observes HTTP catalogue/status after real registration and reconnect, with the original completed-at; R-3 retains generation-clear and rejected-registration evidence |
| Heartbeat | Snapshot -> `SendHeartbeatAsync` -> real `PhoneHomeConnectionWriter` serialization gate -> WebSocket receive loop -> directory | No durable enqueue; send failure before write leaves previous sample, next heartbeat or reconnect reacquires; loss after write may already have reached the receiver; old/missing timestamp and old epoch do not overwrite current evidence | V-7 exercises a busy writer and already eligible writer, fault before send, fault after send, and reconnect. A health round trip is only an ordering barrier; exact receiver catalogue/status fields establish receipt |
| Existing task delivery (unchanged) | service -> committed task/claim -> launch queue -> durable `SessionQueuedMessages` -> remote spill/write/input -> transcript ingestion | Task ID joins queue `ExecutionTaskId`; queue ID owns E/path; session ID + generation + original baseline join W. R-1 retains local lost-claim/watchdog/Retry recovery and eligible/busy remote delivery | Require the matching **complete UserPrompt** W after the original baseline on the bound session/generation, and byte-exact E from independently frozen producer inputs. Queue row, Sent/event/ack/launch idle cannot substitute |

V-7 must use the production heartbeat producer with `ProbeIo` notice modes, not
only `Caps(...)` injection. Extend the existing heartbeat test's producer block
for notice and truncated-notice success, including recovered quiet success.
Check recipient version, token and original completed-at at every handoff.
For failure before write, assert unchanged recipient then resend and assert
receipt; for failure after write, assert receiver already has the sample and
resend does not renew its time. Abort/reconnect from `PhoneHomeRuntimeAdapter`
and observe the new connection's recipient fields. Hold the real writer gate
before a heartbeat, advance the clock while held, release and check original
sample time. Preserve existing newer/older/failure and generation cases.

The session queue evidence in R-1 is through the real queues, HTTP/WebSocket,
spill writer and transcript pull; its terminal records actual submitted text
into a scripted transcript. It proves the application delivery contract, not
installed Codex/Node behavior or native PTY/provider acceptance. Local adapter
callbacks are the corresponding local substitute. `ProbeIo` and the Windows
fixture redirect only process I/O to owned PowerShell children, so argv and
resolution are real but the installed npm/Node program is not executed.
Legacy-reader replicas below prove wire shape/reader semantics, not an old
server deployment. None of these substitutes is a screen/ack-based delivery verdict.

No new durable delivery guard is introduced; all newly asserted observation
handoffs have named PCs below. New crash-at-every-session-handoff tests are
excluded because no session handoff changes here. CARD-1029 expressly owns
before/after save, enqueue, spill, actual input, late confirmation, recreated-DI,
null-sequence and degraded-screen variants; R-1 does **not** discharge those gaps.
A future Code change to a queue or delivery/recovery seam invalidates this freeze
and must return to TestDesign with real producer-to-recipient recovery cases.

### Proves it works now

All named new methods below are Code obligations. Internal vector loops are one
TUnit execution per method; no `[Arguments]` or dynamic expansion is authorized
without a manifest recount. Use independent literal expectations, not a call to
the production parser/projection to compute expected results.

- V-1: deterministic stdout extraction | contracts unit behavior in `CodexCliVersionProbeTests.C959_Parses_and_orders_versions` | CP-1/CP-4 | LF/CRLF, final EOF, blank/unrelated/malformed lines before and after, two different valid versions select the first. Retain every existing alias, SemVer, prerelease/build and ordering vector. Embedded banner, bare number, leading/trailing whitespace, extra suffix, malformed/overflow core and invalid prerelease remain invalid. Assertions `C1031-lines`, `C1031-first`, `C1031-whole-line`; retained SemVer labels remain C959-owned.
- V-2: notice preserves safe completed evidence | real owned child in new `CodexCliVersionProbeTests.C1031_Stderr_notice_preserves_version` | CP-1/CP-4 | stdout `codex-cli 0.160.0\n`, stderr literal synthetic notice `npm notice synthetic update; C:\Users\C1031\private; /home/C1031/private; C1031-token-canary`, exit 0. Require version `0.160.0`, `stderr_output`, completed-at, 64 hex fingerprint, exited child, auth opens 0; no path/canary in sample, local/registration JSON or captured console. Drive failure -> notice -> quiet with explicit clock advancement: snapshot replaces failure, error clears on quiet, reads never renew time. Gate completion with existing `ChildMode`/owned child receipt if needed; advancing fake time while a child is held must not publish success early. Labels `C1031-notice-version`, `C1031-fixed-diagnostic`, `C1031-no-console-leak`, `C1031-completed-at`, `C1031-clear-advisory`.
- V-3: bounded complete stdout evidence | new `CodexCliVersionProbeTests.C1031_Truncated_notices_preserve_complete_version` | CP-1/CP-4 | exact ASCII-byte vectors: 4096 and 4097 stderr bytes with a version; banner first then stdout notices; banner only after byte 4096; final candidate prefix ending `codex-cli 0.160.0` at byte 4096 but actual suffix `-beta\n` beyond cap; LF at byte 4096; CRLF wholly retained versus CR at 4096/LF at 4097; 4096-byte EOF without newline; valid EOF version with only stderr truncated; both streams with finite 1 MiB floods after a complete stdout banner. At <=4096 no truncation advisory; at 4097 it is `output_truncated`. No retained complete banner gives null version. Complete stdout EOF is allowed whenever stdout itself is not truncated, regardless of stderr overflow. All children exit, both pipes drain; completion is asserted with the existing bounded helper, never an infinite wait. Labels `C1031-stdout-overflow`, `C1031-stderr-overflow`, `C1031-no-fragment`, `C1031-byte-cap`, `C1031-both-drained`, `C1031-truncation-priority`.
- V-4: failures remain unknown | new `CodexCliVersionProbeTests.C1031_Invalid_or_failed_output_stays_unknown` and retained `C959_Failure_bounds_and_cleanup` | CP-1/CP-4 | exit 1 with valid stdout and stderr, also either/both overflow -> null/`nonzero_exit`; stderr-only valid banner with empty/malformed stdout -> null/`invalid_output`; no stdout + stderr overflow -> null/`invalid_output`; stdout overflow without retained banner -> null/`output_truncated`. Timeout after a banner, cancellation, real held descendant and unconfirmed cleanup retain existing oracles. Labels `C1031-exit-precedence`, `C1031-stdout-only`, `C1031-no-version-token`; existing timeout/ownership labels below. The old stderr-flood fixture has no stdout: its new expected token is **invalid_output**, not output_truncated. Do not accidentally classify stderr overflow as evidence of truncated stdout.
- V-5: native Windows resolution plus advisory | new `CodexCliVersionWindowsTests.C1031_Npm_notice_preserves_version` plus all three C959 methods | CP-4 only | owned stock npm shim with sibling Node, then sibling absent and owned PATH Node; owned native exe; direct Node with absolute/relative codex.js. Notice and truncated-notice success retain version/advisory through each selection, exact normalized argv, exited child and auth opens 0. Use roots with spaces/non-ASCII. For no-fallback cases assert `Directory.Exists(kit.EmptyPath)` and no entries, and pass that directory. Never empty PATH text or host PATH. The `pwsh` fixture launcher is the only host executable; the selected Codex/Node candidates are owned copies. Label `C1031-windows-notice`; retain C959 unverified-node/js/native/shim zero-start oracles. Linux cannot skip or discharge this row.
- V-6: advisory-aware public projection | new `RunnerCodexCliEvidenceTests.C1031_Advisory_diagnostics_preserve_freshness` | CP-2 | for each of `stderr_output`, `output_truncated`, inject local capabilities and remote registration then heartbeat; observe both catalogue rows and remote HTTP status. Valid `0.160.0` at age 0 and exactly 15m -> stale false, 15m+1 tick -> true; +1m future allowed, +1m+1 tick -> null/`clock_skew`. Valid 64-hex fingerprint and legacy null accepted; empty, 63/65-char and nonhex 64-char fingerprints -> null/`launcher_mismatch`. Missing/malformed version or missing time -> null stale. Every non-advisory allowed token (including timeout/nonzero/cleanup) and any unknown/case-changed token -> null stale; unknown token displays `probe_unavailable`. Both structural defects together select `clock_skew` before `launcher_mismatch`; either overrides an advisory, while a true failure retains its failure token. Public JSON omits fingerprint and synthetic raw diagnostics; inspect host log messages, exception strings and structured properties too. Labels `C1031-advisory-fresh`, `C1031-failure-null`, `C1031-unknown-token`, `C1031-version-required`, `C1031-time-required`, `C1031-age-boundary`, `C1031-skew`, `C1031-fingerprint`, `C1031-legacy-fingerprint`, `C1031-public-private`.
- V-7: wire compatibility and actual observation receipt | extend `RunnerCodexCliEvidenceTests.C959_Heartbeat_updates_only_probe_evidence`, `C959_Catalogue_and_status_project_version` and `CodexCliVersionProbeTests.C959_Local_and_registration_share_snapshot`; compatibility vectors in the new V-6 method | CP-1/CP-2/CP-4 | local, registration and actual heartbeat preserve the same advisory sample; failure-before-send recovery, loss-after-send and reconnect use the Delivery inventory oracles. Add notice and truncated-notice to the existing local/registration parity loop. Deserialize actual new producer JSON into minimal nested legacy C959 DTOs with the unchanged four camelCase member names/types and optional defaults, plus a pre-C959 reader ignoring unknown fields. Freeze the old reader's error/freshness behavior from `6a88d8ce`: parsed version remains visible, advisory token remains visible, stale stays null. New reader + old runner with absent members (legacy JSON literal), old quiet sample, and old `null` version + stderr error all deserialize and retain their old meaning; no new required property, enum or field. A null/absent heartbeat sample retains previous evidence. Labels `C1031-wire-members`, `C1031-old-reader`, `C1031-old-runner`, `C1031-heartbeat-receipt`, `C1031-writer-original-time`, `C1031-before-send-recovery`, `C1031-after-send-recovery`, `C1031-reconnect-receipt`.

Frozen vocabulary: success diagnostics are exactly null, `stderr_output`,
`output_truncated`, in that precedence order (truncation wins). Failure tokens
remain `invalid_output`, `executable_missing`, `nonzero_exit`, `timeout`,
`cancelled`, `cleanup_unconfirmed`, `launcher_unverified`, `probe_busy`,
`probe_unavailable`; absent version plus the two existing diagnostic tokens
remains unknown. Projection may produce `clock_skew` and `launcher_mismatch`.
These last two are structural projection results, not new accepted arbitrary
wire errors. Unknown strings map to `probe_unavailable`. No raw stderr, new
free-text diagnostic property or token enum is introduced. Structural fields and
wire names are unchanged; semantics are additive. Runners-first activation may
temporarily show a valid version with null freshness on an old server, safely.

### Guards the regression

- R-1: observation never refuses | CP-3 runs exactly `CodexCliObservationTests.C959_Probe_failure_never_refuses`, `C959_Stale_sample_never_refuses`, `C959_Unknown_version_never_refuses`, `C959_Old_version_with_floor_never_refuses` | unchanged assertions prove local/remote create, local Retry/cold/warm and remote cold eligible/busy with complete UserPrompt W, independent E bytes, bound identity/generation/baseline and zero typed version requests. Keep all four methods and their oracles. They do not certify CARD-1023 known-broken decisions or CARD-1029 deferred combinations.
- R-2: probe isolation/cache/refresh/completion | CP-1/CP-4 retain all five C959 portable methods | exact version-only argv, cleared environment, scratch HOME/cwd, stdin EOF, no auth opens, 32-entry cache, single flight, completion time, startup and periodic refresh, timeout/cancel/descendant ownership remain. Change only superseded stderr/parser/flood expectations. Diagnostic fixture now asserts version `0.160.0` plus `stderr_output`, with no leak. C959 PC-11's duplicate-banner rejection is replaced by C1031 PC-1/2; PC-29's stderr veto by PC-4; PC-30/31's blanket overflow rejection by PC-7/8/9/10. The historical plan is not edited.
- R-3: observation transport remains bound | CP-2 retains all five C959 evidence methods | selected runner, current store/boot/epoch, no response fallback/retry, monotonic completed-at, no liveness renewal, public privacy and registration clearing remain. Add actual advisory receipts in V-7 without weakening inherited assertions. This narrow fix does not qualify all historical C959 controls.

### Guard inventory

The inventory below covers every changed decision and every additional safety
assertion promoted by this freeze. Independently bypassable decisions have their
own PC, even when one method covers several. Untouched SemVer internals,
launcher environment/cache policy and general phone-home/queue algorithms retain
their existing C959 assertion and pending-PC ownership; their full qualification
is CARD-1029, not a hidden extension of this battery. R-1 admission invariants and
D-2 timeout/cleanup are explicitly promoted here because accepting advisory
success must not bypass them. There is no untested guard in the C1031 inventory;
all PCs are pending execution after land, not claimed qualified now.

- G-1: D-1: notice/blank/malformed lines cannot hide a later valid stdout banner | PC-1

- G-2: D-1: first valid banner wins | PC-2

- G-3: D-1: only a whole banner line is evidence | PC-3

- G-4: D-2/D-3: stderr presence does not veto parsed success | PC-4

- G-5: D-1: stderr never supplies a version | PC-5

- G-6: D-2: nonzero exit wins over banner/notices/truncation | PC-6

- G-7: D-2: complete retained stdout banner survives stdout overflow | PC-7

- G-8: D-2: stderr overflow cannot veto complete stdout evidence, including stdout EOF | PC-8

- G-9: D-2: truncated stdout terminal fragment is never a banner | PC-9

- G-10: D-2: 4096 bytes per stream is the retained-evidence limit | PC-10

- G-11: D-2: continue draining after retained storage is full | PC-11

- G-12: D-2: timeout is unknown even when a banner arrived | PC-12

- G-13: D-2: unconfirmed process/pipe cleanup is unknown | PC-13

- G-14: D-3: no stdout evidence is invalid unless stdout itself was truncated | PC-14

- G-15: D-3: truncation advisory precedes ordinary stderr | PC-15

- G-16: D-3: sample diagnostic is fixed-token only | PC-16

- G-17: D-3: discarded stderr is not logged to either console writer | PC-17

- G-18: D-2/D-3: sample time describes the completed attempt | PC-18

- G-19: D-3: a later quiet success clears an earlier advisory | PC-19

- G-20: D-3/V-7: local capabilities retain successful advisory | PC-20

- G-21: D-3/V-7: registration producer retains successful advisory | PC-21

- G-22: V-7: heartbeat recipient accepts newer advisory evidence | PC-22

- G-23: V-7: busy/retried transport cannot renew probe completion time | PC-23

- G-24: V-7: accepted registration/reconnect retains the current advisory sample | PC-24

- G-25: D-3: only the two valid-version advisories permit freshness | PC-25

- G-26: D-3: a true failure with a supplied valid version remains unknown | PC-26

- G-27: D-3: unknown diagnostic strings cannot escape the allowlist | PC-27

- G-28: D-3: malformed or absent version never becomes fresh | PC-28

- G-29: D-3: absent completed-at never becomes fresh | PC-29

- G-30: D-3: fifteen-minute age equality is fresh | PC-30

- G-31: D-3: advisory cannot bypass the one-minute clock-skew guard | PC-31

- G-32: D-3: advisory cannot bypass malformed non-null fingerprint validation | PC-32

- G-33: D-3: absent legacy fingerprint is compatible | PC-33

- G-34: D-3/V-6: public output must not disclose the private fingerprint | PC-34

- G-35: D-3/V-7: legacy camelCase diagnostic member stays wire-compatible | PC-35

- G-36: D-5: Windows normalized npm/direct-node notice succeeds | PC-36

- G-37: D-2/D-5: failed launcher verification cannot fabricate success | PC-37

- G-38: D-4/R-1: create cannot refuse because CLI observation is unknown | PC-38

- G-39: D-4/R-1: Retry cannot refuse because CLI observation is unknown | PC-39

- G-40: D-4/R-1: dispatch cannot add a CLI-derived Blocked gate | PC-40

- G-41: D-4/R-1: warm reuse still delivers the whole brief | PC-41

- G-42: D-4/R-1: create does not perform a typed version probe | PC-42

- G-43: D-4/R-1: Retry does not perform a typed version probe | PC-43

- G-44: D-4/R-1: cold dispatch does not perform a typed version probe | PC-44

- G-45: D-4/R-1: warm dispatch does not perform a typed version probe | PC-45

### Positive controls

Mutation runs each control on landed L, separately commissioned after ordinary
Review and confirmed land. Code runs only V/R. Review judges these pending
controls before land. Each row below is one compiling source defect, one exact
method and one decisive assertion; restore exact tracked/index bytes afterward.
For all phases use `--treenode-filter "/*/*/ClassName/ExactTestMethod"`, replacing
ClassName/ExactTestMethod with the two literal parts of that row's method. There
is no class wildcard and no method wildcard. Minimum is 1 execution per phase.
Use fresh isolated outputs and the host build-slot gate for baseline, red and
restored green. A missing assertion is a Code defect, not permission to claim PC
success from an exception elsewhere. Preserve per-PC build/test evidence and
restoration records externally under SourceLanding custody; never commit there.
The two Windows PCs require their own native SourceLanding lane at L.

- PC-1: break G-1 by restore the original whole-buffer `ParseBanner` body; expect `CodexCliVersionProbeTests.C959_Parses_and_orders_versions` red at **C1031-lines: literal expected version 0.160.0**. Restore and require the same method green.

- PC-2: break G-2 by iterate candidate lines in reverse order; expect `CodexCliVersionProbeTests.C959_Parses_and_orders_versions` red at **C1031-first: first version 0.159.1, not later 0.160.0**. Restore and require the same method green.

- PC-3: break G-3 by remove the regex start anchor, permitting a prefixed substring; expect `CodexCliVersionProbeTests.C959_Parses_and_orders_versions` red at **C1031-whole-line: prefixed banner result is null**. Restore and require the same method green.

- PC-4: break G-4 by restore `if (diagnostic.Bytes.Length != 0) return Unknown("stderr_output", resolved.Fingerprint);` before success; expect `CodexCliVersionProbeTests.C1031_Stderr_notice_preserves_version` red at **C1031-notice-version: version equals 0.160.0**. Restore and require the same method green.

- PC-5: break G-5 by fall back to `ParseBanner(Encoding.UTF8.GetString(diagnostic.Bytes))` when stdout parsing returns null; expect `CodexCliVersionProbeTests.C1031_Invalid_or_failed_output_stays_unknown` red at **C1031-stdout-only: stderr-only banner gives null version**. Restore and require the same method green.

- PC-6: break G-6 by disable the nonzero-exit early return while leaving stdout parsing enabled; expect `CodexCliVersionProbeTests.C1031_Invalid_or_failed_output_stays_unknown` red at **C1031-exit-precedence: exit 1 gives null version and nonzero_exit**. Restore and require the same method green.

- PC-7: break G-7 by return `Unknown("output_truncated", resolved.Fingerprint)` whenever stdout is truncated; expect `CodexCliVersionProbeTests.C1031_Truncated_notices_preserve_complete_version` red at **C1031-stdout-overflow: version equals 0.160.0**. Restore and require the same method green.

- PC-8: break G-8 by return unknown whenever stderr is truncated; expect `CodexCliVersionProbeTests.C1031_Truncated_notices_preserve_complete_version` red at **C1031-stderr-overflow: version equals 0.160.0 even for stdout EOF**. Restore and require the same method green.

- PC-9: break G-9 by pass the entire retained stdout prefix to ParseBanner instead of removing its last unterminated fragment; expect `CodexCliVersionProbeTests.C1031_Truncated_notices_preserve_complete_version` red at **C1031-no-fragment: cut-off candidate gives null version**. Restore and require the same method green.

- PC-10: break G-10 by change `OutputByteLimit` from 4096 to 8192; expect `CodexCliVersionProbeTests.C1031_Truncated_notices_preserve_complete_version` red at **C1031-byte-cap: 4097 stderr bytes give output_truncated; exact 4096 does not**. Restore and require the same method green.

- PC-11: break G-11 by break the CaptureAsync read loop once result.Length reaches OutputByteLimit; expect `CodexCliVersionProbeTests.C1031_Truncated_notices_preserve_complete_version` red at **C1031-both-drained: bounded completion boolean is true for both finite 1 MiB floods**. Restore and require the same method green.

- PC-12: break G-12 by replace the timeout Unknown return with `new RunnerCodexCliVersionDto("0.160.0", _clock.GetUtcNow(), "timeout", resolved.Fingerprint)`; expect `CodexCliVersionProbeTests.C959_Failure_bounds_and_cleanup` red at **C959-v03-timeout: timed-out version is null**. Restore and require the same method green.

- PC-13: break G-13 by return the same synthetic valid-version DTO only when error is cleanup_unconfirmed; expect `CodexCliVersionProbeTests.C959_Failure_bounds_and_cleanup` red at **C959-pc-035: unconfirmed-cleanup version is null**. Restore and require the same method green.

- PC-14: break G-14 by choose output_truncated on either stream overflow in the version-null branch; expect `CodexCliVersionProbeTests.C1031_Invalid_or_failed_output_stays_unknown` red at **C1031-no-version-token: no stdout plus overflowing stderr is invalid_output**. Restore and require the same method green.

- PC-15: break G-15 by select stderr_output before checking truncated flags in the successful branch; expect `CodexCliVersionProbeTests.C1031_Truncated_notices_preserve_complete_version` red at **C1031-truncation-priority: mixed notice/overflow diagnostic equals output_truncated**. Restore and require the same method green.

- PC-16: break G-16 by set successful diagnostic to `Encoding.UTF8.GetString(diagnostic.Bytes)`; expect `CodexCliVersionProbeTests.C1031_Stderr_notice_preserves_version` red at **C1031-fixed-diagnostic: exact stderr_output, with no raw path or canary in JSON**. Restore and require the same method green.

- PC-17: break G-17 by add `Console.Error.Write(Encoding.UTF8.GetString(diagnostic.Bytes));` after capture; expect `CodexCliVersionProbeTests.C1031_Stderr_notice_preserves_version` red at **C1031-no-console-leak: combined captured writers exclude C1031-token-canary**. Restore and require the same method green.

- PC-18: break G-18 by subtract one tick from the successful DTO completed-at value; expect `CodexCliVersionProbeTests.C1031_Stderr_notice_preserves_version` red at **C1031-completed-at: timestamp equals the completion clock exactly**. Restore and require the same method green.

- PC-19: break G-19 by in RefreshDefaultAsync preserve the previous snapshot error when the new sample error is null; expect `CodexCliVersionProbeTests.C1031_Stderr_notice_preserves_version` red at **C1031-clear-advisory: quiet success error is null**. Restore and require the same method green.

- PC-20: break G-20 by in DescribeCapabilities drop CodexCliVersionError only when it is stderr_output/output_truncated; expect `CodexCliVersionProbeTests.C959_Local_and_registration_share_snapshot` red at **C959-pc-052: local advisory equals the literal vector token**. Restore and require the same method green.

- PC-21: break G-21 by in PhoneHomeRuntimeAdapter.Capabilities drop the same advisory field; expect `CodexCliVersionProbeTests.C959_Local_and_registration_share_snapshot` red at **C959-pc-056: registration advisory equals the literal vector token**. Restore and require the same method green.

- PC-22: break G-22 by in ObserveCodexCli return before storing samples with either advisory token; expect `RunnerCodexCliEvidenceTests.C959_Heartbeat_updates_only_probe_evidence` red at **C1031-heartbeat-receipt: receiver has the new notice version/token/completed-at**. Restore and require the same method green.

- PC-23: break G-23 by in ObserveCodexCli store `sample with { CodexCliVersionCheckedAtUtc = _clock.GetUtcNow() }`; expect `RunnerCodexCliEvidenceTests.C959_Heartbeat_updates_only_probe_evidence` red at **C959-v09-producer-original-time: busy-writer receiver time remains original T**. Restore and require the same method green.

- PC-24: break G-24 by in PhoneHomeRunnerDirectory.Register set slot.CodexCli to null only for advisory capabilities; expect `RunnerCodexCliEvidenceTests.C1031_Advisory_diagnostics_preserve_freshness` red at **C1031-advisory-fresh: remote registration row retains valid version and stale false**. Restore and require the same method green.

- PC-25: break G-25 by restore the original DisplayStale rule that any DisplayError makes stale null; expect `RunnerCodexCliEvidenceTests.C1031_Advisory_diagnostics_preserve_freshness` red at **C1031-advisory-fresh: valid advisory at T has stale false**. Restore and require the same method green.

- PC-26: break G-26 by in DisplayStale return false for a version-bearing timeout sample; expect `RunnerCodexCliEvidenceTests.C1031_Advisory_diagnostics_preserve_freshness` red at **C1031-failure-null: timeout with version has null stale**. Restore and require the same method green.

- PC-27: break G-27 by return the original unknown error string instead of probe_unavailable; expect `RunnerCodexCliEvidenceTests.C1031_Advisory_diagnostics_preserve_freshness` red at **C1031-unknown-token: unknown diagnostic displays exactly probe_unavailable**. Restore and require the same method green.

- PC-28: break G-28 by in DisplayStale return false when sample version equals banana; expect `RunnerCodexCliEvidenceTests.C1031_Advisory_diagnostics_preserve_freshness` red at **C1031-version-required: malformed-version advisory has null stale**. Restore and require the same method green.

- PC-29: break G-29 by return false for nonnull samples whose checked-at is null; expect `RunnerCodexCliEvidenceTests.C1031_Advisory_diagnostics_preserve_freshness` red at **C1031-time-required: missing-time advisory has null stale**. Restore and require the same method green.

- PC-30: break G-30 by change the final age comparison from > to >=; expect `RunnerCodexCliEvidenceTests.C1031_Advisory_diagnostics_preserve_freshness` red at **C1031-age-boundary: exactly 15 minutes gives false**. Restore and require the same method green.

- PC-31: break G-31 by disable the future-skew branch in DisplayError; expect `RunnerCodexCliEvidenceTests.C1031_Advisory_diagnostics_preserve_freshness` red at **C1031-skew: +1 minute +1 tick displays clock_skew and null stale**. Restore and require the same method green.

- PC-32: break G-32 by disable the malformed-fingerprint branch in DisplayError; expect `RunnerCodexCliEvidenceTests.C1031_Advisory_diagnostics_preserve_freshness` red at **C1031-fingerprint: bad non-null fingerprint displays launcher_mismatch and null stale**. Restore and require the same method green.

- PC-33: break G-33 by treat null fingerprint as launcher_mismatch in DisplayError; expect `RunnerCodexCliEvidenceTests.C1031_Advisory_diagnostics_preserve_freshness` red at **C1031-legacy-fingerprint: null fingerprint with valid sample remains fresh**. Restore and require the same method green.

- PC-34: break G-34 by return sample.CodexCliLauncherFingerprint as the display diagnostic for an advisory; expect `RunnerCodexCliEvidenceTests.C1031_Advisory_diagnostics_preserve_freshness` red at **C1031-public-private: public JSON contains neither fingerprint property nor its known 64-hex value; run this check before token assertions**. Restore and require the same method green.

- PC-35: break G-35 by add `[property: System.Text.Json.Serialization.JsonPropertyName("codexCliVersionErrorRenamed")]` to the sample DTO error parameter; expect `RunnerCodexCliEvidenceTests.C1031_Advisory_diagnostics_preserve_freshness` red at **C1031-wire-members: actual sample JSON contains codexCliVersionError and the frozen legacy DTO receives stderr_output**. Restore and require the same method green.

- PC-36: break G-36 by on Windows only, return unknown for successful advisory samples with resolved.Args.Length == 2; expect `CodexCliVersionWindowsTests.C1031_Npm_notice_preserves_version` red at **C1031-windows-notice: normalized npm sample version equals 0.160.0**. Restore and require the same method green.

- PC-37: break G-37 by return `new RunnerCodexCliVersionDto("0.160.0")` from the `ProbeAsync` filtered exception catch that includes `CodexLaunchException`; leave process creation untouched. Missing Node throws that exception from launch normalization, not `ProbeRefusal`; expect `CodexCliVersionWindowsTests.C959_Unverified_launcher_is_unknown` red at **C959-pc-069: missing Node sample version is null, with zero child starts**. Restore and require the same method green.

- PC-38: break G-38 by at CreateAsync entry throw InvalidOperationException for request.AgentKind == AgentKind.Codex; expect `CodexCliObservationTests.C959_Probe_failure_never_refuses` red at **C959-pc-225 create/False/timeout: captured failure is null**. Restore and require the same method green.

- PC-39: break G-39 by after RetryAsync loads task, throw InvalidOperationException for task.AgentKind == AgentKind.Codex; expect `CodexCliObservationTests.C959_Probe_failure_never_refuses` red at **C959-pc-226 retry/timeout: retryFailure is null**. Restore and require the same method green.

- PC-40: break G-40 by before the reuse branch for Codex claimed tasks call BlockAsync with a synthetic CLI reason, commit the transaction, and return DispatchOneResult.NotClaimed; expect `CodexCliObservationTests.C959_Probe_failure_never_refuses` red at **C959-pc-227/timeout complete selected-session receipt: exactly one complete UserPrompt**. Restore and require the same method green.

- PC-41: break G-41 by return early in DeliverReuseMessagesAsync for Codex tasks, without enqueueing; expect `CodexCliObservationTests.C959_Probe_failure_never_refuses` red at **C959-warm-real-receipt timeout: exactly one selected-session UserPrompt**. Restore and require the same method green.

- PC-42: break G-42 by in CreateAsync for Codex call `_runners!.Resolve(request.RunnerId).GetCodexCliVersionAsync(new("codex"), ct)` and await it; expect `CodexCliObservationTests.C959_Probe_failure_never_refuses` red at **C959-pc-237 create/timeout: selected.Requests is empty**. Restore and require the same method green.

- PC-43: break G-43 by after loading the Codex task in RetryAsync await `_runners!.Resolve(task.RunnerId).GetCodexCliVersionAsync(new("codex"), ct)`; expect `CodexCliObservationTests.C959_Probe_failure_never_refuses` red at **C959-pc-238 retry/timeout: Requests is empty**. Restore and require the same method green.

- PC-44: break G-44 by before reuse selection await `_runners!.Resolve(claimed.RunnerId).GetCodexCliVersionAsync(new("codex"), ct)` for Codex; expect `CodexCliObservationTests.C959_Probe_failure_never_refuses` red at **C959-pc-239 cold and C959-pc-253 final/timeout: Requests is empty**. Restore and require the same method green.

- PC-45: break G-45 by at DeliverReuseMessagesAsync entry await `_runners!.Resolve(task.RunnerId).GetCodexCliVersionAsync(new("codex"), ct)` for Codex; expect `CodexCliObservationTests.C959_Probe_failure_never_refuses` red at **C959-pc-240 warm pinned timeout: Requests is empty**. Restore and require the same method green.


PC-17 uses only the synthetic fixture's stderr and restores console capture; it
does not log a live CLI. PC-37 fabricates the return DTO without starting an
unverified executable, so it cannot reach host Codex. PC-42..45 run only the
isolated clients already installed by `DispatchKit`/`Kit`; no provider is invoked.
Do not batch defects in the same production file or share a process-spawning
assembly across concurrent test drivers. All 45 controls are executable with
the named existing seams plus the expressly required fixture/assertion additions.

### Out of scope

- CARD-1023: this is an observation-feed correction. A valid version plus advisory is observed data, not evidence of a known-working or known-broken (CLI, model, runner, pty/backend, host) cell. Unknown/failure remains allow-by-default. Do not add compatibility storage, cell writers, admission, floors or reprobes. Its live card was read; it remains a separate design owner.
- CARD-1029: retain its 192 pending active C959 controls and disclosed V-13/V-21..26 variants, including remote Retry/warm/recreated-DI, enqueue/late-confirmation, null sequence and degraded receipt work. Our four unchanged no-refusal methods and Windows CP-4 do not close CARD-1029 or its separate 29-execution Windows launch-policy row.
- Untouched parser ordering/SemVer internals, isolated environment/cache, generation fencing and queue recovery are rerun where present in selected methods, but this card does not repeat their entire historical mutation census. The new guarded branches, overflow/timeout safety, token/privacy, transport content and no-admission contracts are explicitly mapped here; no absent method is silently credited.
- Installed Codex/npm/Node, real auth homes, manual CLI invocation, diagnostic POSTs, live refresh forcing, provider turns, PTY/backend changes, deployment, restarts and fleet activation are excluded from Code's fixture proof. The earlier activation section remains caller-owned and runners-first. No raw desktop stderr capture is claimed.
- No full unit/PTY/E2E suite, checkpoint-namespace tests or census/tool changes. The selected classes plus four no-refusal methods cover the changed surface; broad reruns need a named new concern and a stated cost. No generated evidence is tracked.

### Checkpoints

Closed ordinary scope after S1-S2 are committed and pushed. C is the final Code
SHA; preserve it across both lanes. The class filter on CP-4 has the importer-
recognized trailing-star OR operands, with the Markdown pipe escaped. CP-3 is
the only method wildcard and selects exactly the four named no-refusal methods.
Native Windows **Debug is a separate task** at C, selected with `-Platform Windows`
and no runner pin unless justified by current inventory. CP-1..3 run on Linux;
CP-4 runs on native Windows, not WSL/Wine, and zero skips is required.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---:|---:|---|---|
| CP-1 | S1-S2 | `tests/Antiphon.SessionRunner.Tests -> bin-c1031-linux-probe/` | linux-probe | `/*/*/CodexCliVersionProbeTests/*` | V-1, V-2, V-3, V-4, V-7, R-2 | exactly 8 executions; 0 failed/skipped | 8 | 8 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-2 | S1-S2 | `tests/Antiphon.Tests -> bin-c1031-linux-observation/` | linux-observation | `/*/*/RunnerCodexCliEvidenceTests/*` | V-6, V-7, R-3 | exactly 6 executions; 0 failed/skipped | 6 | 8 | true | `C804_ORPHAN_SWEEP_ROOT=c1031-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-3 | S1-S2 | `tests/Antiphon.Tests -> bin-c1031-linux-no-refusal/` | linux-no-refusal | `/*/*/CodexCliObservationTests/C959_*_never_refuses` | R-1 | exactly the 4 named executions; 0 failed/skipped | 4 | 12 | true | `C804_ORPHAN_SWEEP_ROOT=c1031-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-4 | S1-S2 | `tests/Antiphon.SessionRunner.Tests -> bin-c1031-windows-probe/` | windows-native-debug | `/*/*/(CodexCliVersionProbeTests*)\|(CodexCliVersionWindowsTests*)/*` | V-1, V-2, V-3, V-4, V-5, V-7, R-2 | exactly 12 native Windows executions at C; 0 failed/skipped | 12 | 14 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |

Source census: portable probe 5 existing + 3 new = 8; wire/evidence 5 existing +
1 new = 6; no-refusal 4 existing = 4; Windows 3 existing + 1 new, plus portable
8 = 12. Total **30 executions, 22 unique methods, 5 new methods**; vectors and
loop iterations do not inflate Min. Tests live in `Antiphon.SessionRunner.Tests`
and `Antiphon.Tests.Application`; `Antiphon.Tests.Checkpoints` remains **377**.
The importer extracts class roster tokens, not exact method-count equality from
Expect prose; Code/Review must additionally compare the executed method roster
and exact counts to this census. Additional matching classes/methods after base
drift require a recount, not silent acceptance of a larger floor.

Per lane, the sole declared prerequisite build is checkpoint-tool bootstrap
under `scripts/build-slot.ps1`, with `--property:OutputPath=bin-c1031-tool/`.
After the final Code commit, invoke the built tool from the checkout root:

```powershell
dotnet tools/Antiphon.Checkpoints/bin-c1031-tool/Antiphon.Checkpoints.dll run --plan docs/superpowers/plans/2026-10-04-card-1031-codex-version-stderr-plan.md --rows CP-1,CP-2,CP-3 --expected-source-sha (git rev-parse HEAD) --serial
```

The separate native Windows task uses the same command with `--rows CP-4`, after
confirming checkout HEAD equals the final pushed C supplied in its dispatch.
Continue foreground `wait` until the run no longer returns 75; await every owned
process. No tests/builds were run by TestDesign. Each row has its own build,
not reused output. All drivers require host slots; exit 4 is not run, never an
unleased retry. Preserve unedited CHECKPOINT lines, counts and clean/source/build
provenance. Remove only owned alternate outputs after completion. Code and
ordinary Review run `scripts/check-evidence-diff.ps1` across their full candidate
range. Any repair creates a new C and requires affected portable qualification
and Windows proof at that final C before Review accepts the result.

### Cost

All times are **estimated minutes**, not measured test results. Ordinary Code
V/R floor is CP-1 8 + CP-2 8 + CP-3 12 + CP-4 14 = **42** (Linux 28, separate
Windows 14), including each isolated row build. Two tool bootstraps/import and
receipt handling add **6**: ordinary verification total **48**. Authoring is
estimated **45** separately, so Code plus the separate Windows task need **93**
minutes excluding slot waits or repairs. No unrelated suite is hidden in that
floor.

Mutation floor is method-scoped, with the exact filters derived above:

| PCs / exact method family | Count | Baseline build+green | Defect build+red | Restore build+green | Edit/restore accounting | Per PC | Subtotal |
|---|---:|---:|---:|---:|---:|---:|---:|
| PC-1..21: the literal `CodexCliVersionProbeTests` methods named above | 21 | 3 | 3 | 3 | 1 | 10 | 210 |
| PC-22..35: the literal `RunnerCodexCliEvidenceTests` methods named above | 14 | 3 | 4 | 4 | 1 | 12 | 168 |
| PC-36..37: two literal Windows methods named above, native Windows at L | 2 | 4 | 5 | 5 | 2 | 16 | 32 |
| PC-38..45: `/*/*/CodexCliObservationTests/C959_Probe_failure_never_refuses` | 8 | 6 | 7 | 7 | 2 | 22 | 176 |

PC cycle floor = **586**; two lane setups/discovery/evidence handling = **8**;
Mutation total = **594**. Each of the 45 controls receives its own baseline,
compiling red, exact restoration and rebuilt green (135 minimum method
executions), including repeated methods with different independent defects.
Combined verification estimate = ordinary setup 6 + V/R 42 + Mutation setup 8
+ PCs 586 = **642 minutes**; with Code authoring 45 = **687**. Separate ordinary
Review, operational activation, queue/slot waits and any repairs are not priced
as test execution and must be commissioned separately. Ordinary savings versus
the proposed 42-minute floor are **0**: all four rows, especially final-C native
Windows, remain required. There was no prior numeric mutation floor to subtract;
method-scoped controls bound the new cost instead of inventing savings.

### Freeze audit and handoff

Read-only importer validation executed using the existing
`tools/Antiphon.Checkpoints/bin-c1022-tool/Antiphon.Checkpoints.dll` under runner
worktree `task-e41a7005`; its reported version was
`1.0.0+a10bd1883e0803bcad3fe4513f3f1d1685c0b666`. Git comparison of the entire
checkpoint tool from that source to inspected current master was empty. This
was tool reuse for document parsing, not a qualified build or test receipt.
The CLI `import` produced 4 rows in ignored `.antiphon/c1031-import.yaml` with
exit 0. Loading that same existing assembly and directly calling
`PlanTableImporter.ImportFile` and `ManifestValidator.Validate` for both
`isWindows=false` and `true` produced these actual lines, with no compilation:

```text
IMPORT windows=False rows=4 builds=4 min=8,6,4,12 minutes=42 warnings=0 validation=ok
IMPORT windows=True rows=4 builds=4 min=8,6,4,12 minutes=42 warnings=0 validation=ok
```

Resolved CP-4 filter is
`/*/*/(CodexCliVersionProbeTests*)|(CodexCliVersionWindowsTests*)/*`; both class
roster tokens are present. All rows expand After to S1,S2, have unique build and
group names, serial=true and the declared environments. Source-only counting
found existing method counts 5/5/4/3 and no argument expansion in selected
methods. `git diff --check` passes. New methods are specified, not fabricated as
already discovered or executed by the importer.

Bodies read as listed; **guards=45, mapped=45, missing=0, duplicate PC maps=0**.
All PCs have a concrete compiling defect, literal method and decisive assertion;
execution awaits Code and post-land Mutation. No human choice or unverifiable
new seam remains. D-3 is accepted. No build, test, positive-control cycle or live
probe was performed in this docs-only stage.

--- next stage ---
next: code
handoff: Land this plan+freeze doc-only first; start Code fresh from current master containing it, never merge master into the old plan branch. Implement accepted D-3, the five named tests and exact four checkpoints; commission separate native Windows Debug at final pushed C. Keep CARD-1023/1029 ownership; ordinary Review precedes land and 45 post-land PCs.
artifact: docs/superpowers/plans/2026-10-04-card-1031-codex-version-stderr-plan.md
