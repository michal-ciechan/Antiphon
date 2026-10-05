# CARD-1067: declare the c1008/c994 host-lane sudo helpers

Date: 2026-10-05. Plan task: `404ff4eb-16a7-4fdb-a390-83d836729a67` (also the
sanctioned server2-temp runner canary). Inspected source:
`d985af05b5ace2f13ec3f0d886c15992e9c13c3f` (branch base). Card: Antiphon
`896c2b76-adea-4894-a79d-56fedb2f2d04`. Predecessor evidence: CARD-1066 task
d73d91aa, base `0de12dac930ad52a235604c566ee643e71b21daa`.

## Outcome and stage boundary

`RemoteScriptContractTests.Nested_lane_never_uses_sudo_or_python` is red at the
branch base because CARD-0994 (commits `b53f5ae42`, `db387e8ce`, 2026-10-04)
added `sudo -n` invocations to three blocks of `scripts/c590-remote.sh` that
the test's host-lane declaration list does not name, and never touched the
test. The helpers are **not** a nested-lane sudo: every path to them starts at
a `require_lane host` entry point. The test is stale on its declaration list,
and two of the helpers are also missing the lane declaration every sibling
sudo helper carries. Repair both sides: the helpers declare the host lane first,
the test names the three blocks, constrains their sudo to read-only inspection,
and gains a mutant-driven sensitivity regression so an undeclared sudo cannot
pass again.

Two Code slices, 30-60 minutes. Verification design is folded into this plan
(brief instruction); the Checkpoints table is the closed list. No whole-Unit
run, no live deploy, rollout, recycle, retire, Reset, Prune or Seed, no runner
or platform pin. This Plan changes only this document.

Lane facts read for this plan: `GET /api/runner-defaults` has
`globalRunnerId=server2` (operator default, revision 2). `GET /api/session-runners`
lists desktop (windows, capacity 2 delegatedTasks, occupied 2), server2 (linux,
capacity 10 sessions, occupied 4) and server2-temp (linux, capacity 10 sessions,
occupied 1). The checkpoints are Linux rows; omit `-Runner` and `-Platform`.

## Ground truth

| Card assumption | What the code/evidence actually does | Consequence |
|---|---|---|
| The declaration list omits **a** c1008 helper (one line, `RecycleContainerStateUnknown`). | The assertion at `RemoteScriptContractTests.cs:1739-1763` throws on the first undeclared line and stops. An exact replica of its `Executable`/regex/`Block` logic over HEAD finds **five** undeclared lines in **three** blocks: `c1008_owned_mounts` (`c590-remote.sh:4186`, `:4188`), `c994_lookup_image` (`:4933`), `case_retire_temp_containers` (`:4988`, `:5030`). Same five at base `0de12dac`. | Declare all three blocks. Fixing only the named line leaves the method red at the next one. |
| The helper may really run sudo from the nested lane. | `c1008_owned_mounts` is called from `c1008_record_recreated` (`:4341`; returns early unless `C1008_ACTIVE=1`, which is set only at `:4439`/`:4493` inside `c1008_recycle`, whose fourth line is `require_lane host` at `:4382`), `c1008_reconcile_owned` (`:4361`; called only from `c1008_recycle` `:4405`), `c1008_recycle` (`:4472`) and `case_retire_temp_containers` (`:5014`, `:5045`; its first line is `require_lane host` at `:4975`). `c994_lookup_image` is called only from `c1008_recycle` (`:4410`) and the retire case (`:5015`). `seed_runner_checkout` (`:1184`, `:1198`) reaches `c1008_record_recreated` only under the same `C1008_ACTIVE` gate. | Not a nested-lane violation. The test is stale on its declaration list; the two helpers are missing the lane declaration. Repair guard and declaration, not reachability. |
| The sibling c1008 helpers set the pattern. | `c1008_cache_preservation` (`:4151`), `c1008_references` (`:4290`), `c1008_disk` (`:4313`), `c1008_rollout_lock` (`:3729`) and `c1008_recycle` (`:4382`) all open with `require_lane host`; the test asserts that for each declared c1008 block (`:1737-1738`). `c1008_owned_mounts` (`:4181`) and `c994_lookup_image` (`:4927`) open with `local`. | Add `require_lane host` as the first executable line of both helpers so the existing per-block assertion applies to them unchanged. |
| The sudo is needed. | Docker volume mountpoints and bind sources live under `/var/lib/docker` (root-only); `readlink -e`/`stat` as `mc` cannot traverse them. The retire-root canonicalisation mirrors `c849_*` host-lane realpath checks. Every one of the five lines is `readlink -e --` or `stat -c %F --`: read-only inspection. | Keep the sudo. Pin the read-only shape in the test the way `case_deploy_temp_runner` is pinned to `test -d`/`df -Pk` (`:1774-1778`). |
| Adding a lane guard to the helpers could break their direct-call tests. | Every harness that calls the helpers directly sets the host lane: `RollingProductionMountTests.cs:34` (`LANE=host`, fixture sudo shim at `RollingVolumeRecycleScriptTests.cs:666`), `RetiredTempContainerScriptTests.cs:132,172`, `RetiredTempContainerHostTests.cs:76,104,316`. `C994_Host_context_is_strict` (`RetiredTempContainerHostTests.cs:296`) already proves the retire case refuses `detect_lane() { LANE=nested; }` with exit 2 and no mutation. | The guard is a no-op for every existing caller and harness. Run the four direct-call methods as adjacent regressions, not the classes. |
| CARD-0994 verified this test. | `git log -- tests/Antiphon.Tests/Scripts/RemoteScriptContractTests.cs` has no CARD-0994 commit; the declaration lines were last changed by CARD-1008 on 2026-10-03 (`e887b0719`, `ecc619197`), the day before the sudo lines arrived. The CARD-0994 plan names no `RemoteScriptContractTests` row. | The regression is a declaration drift that CARD-0994's checkpoint selection could not see. The new C1067 test makes the drift self-reporting. |
| `Block()` could be confused by inner braces. | `Block` (`:4746-4754`) ends at the first line that is exactly `}`; `})` (`:3061`) and `}).listen(...)` (`:3191`) do not close a block, which is why the fixture sudo lines at `:3068`, `:3076`, `:3197` are inside their declared blocks. The jq programs inside `c1008_owned_mounts` close with indented braces. | No change to `Block`. A Code author must not add a bare column-0 `}` inside the helper bodies. |
| The predecessor's red is the same defect. | CP-BASE at `0de12dac`: `executed=1 passed=0 failed=1 skipped=0 dirty=0 sourceState=clean buildSource=verified`, failing on the `:4186` line (`.antiphon/task-d73d91aa.md` on `feat/card-task-d73d91aa` at `3623e861`). The replica reproduces it statically at both SHAs. | Do not repeat the base red proof. The S1 red run below is the new test's own red, not a second baseline. |

Owners: [testing/build](../../testing-and-build.md) (Checkpoint manifest, Combined
class filters), [Docker rollout](../../docker-stack.md), [orchestration](../../orchestration-loop.md).
Related designs: [CARD-1008](2026-10-03-card-1008-rolling-volume-recycle-and-retire-temp-plan.md),
[CARD-0994](2026-10-04-card-0994-exited-temp-container-retirement-plan.md),
[CARD-1066](2026-10-05-card-1066-cache-fixture-nounset-plan.md).

## Decisions

- **D-1 — Stale declaration, repaired on both sides.** The helpers gain a lane
  declaration and the test names them. Rejected: a test-only allowlist
  extension (admits a sudo helper that never declares its lane, weakening the
  invariant the test states at `:1695-1699`); a call-graph reachability
  analysis inside the test (a second parser for one defect); dropping sudo
  from the helpers (`/var/lib/docker` is not traversable as `mc`; a behaviour
  change outside this card).
- **D-2 — Guard first, before `local`.** `require_lane host` is the first
  executable line of `c1008_owned_mounts` and `c994_lookup_image`, matching
  the siblings, and the test asserts `Commands(block)[1] == "require_lane host"`
  for both (the shape used for `ensure_runner_codex_home` at `:1766-1767`).
  Rejected: guarding only at call sites (the next caller forgets), or asserting
  mere presence (a guard after the first sudo would pass).
- **D-3 — Read-only inspection pin for the three new blocks.** Every sudo line
  in `c1008_owned_mounts`, `c994_lookup_image` and `case_retire_temp_containers`
  must contain `sudo -n readlink -e --` or `sudo -n stat -c %F --`, mirroring
  the `case_deploy_temp_runner` pin. Rejected: unconstrained declaration.
- **D-4 — Sensitivity regression as in-memory text mutants.** Extract the sudo
  half of `Nested_lane_never_uses_sudo_or_python` into
  `private static void CheckNestedLaneSudo(string text)` without changing any
  assertion or message; the existing test calls it on `Remote()`. A new
  argument-expanded `C1067_Nested_lane_sudo_guard_is_sensitive(string control)`
  builds a mutant from `Remote()` for each control, asserts
  `mutant.ShouldNotBe(original)`, asserts `Should.Throw<ShouldAssertException>`
  whose message contains the named assertion text, and asserts the unmodified
  text passes. This is the `C944`/`C957` pattern already in the class
  (`:839-847`, `:974-1005`). Rejected: a scratch file copy (nothing reads the
  path), a new test class (the helpers are private to this class).
- **D-5 — Scope.** No docs change (no document enumerates the sudo helpers;
  `docs/docker-stack.md` mentions sudo only for `ensure_runner_codex_home` and
  custody). No change to `c1008_refuse`, the `c1008_refuse() { exit 2; }`
  subshell override, CARD-0994 semantics, timeouts or any existing assertion.
  Linux checkpoint lane only; the test is pure text analysis and runs on
  Windows unchanged.
- **D-6 — Red proof is the S1 commit's CP-1.** Run CP-1 once at the S1 commit
  (tests only): expect `executed=5 passed=0 failed=5`. The four C1067 arms go
  red because the guard strings do not exist yet (`ShouldNotBe`) and because
  the unmodified text fails the checker; `Nested_lane_*` stays red on the
  inherited line. This is a listed, explained run, not a Mutation cycle.
- **D-7 — Filters.** New test names carry the `C1067_` prefix so one wildcard
  operand selects them. CP-1 uses the two-operand `(A*)|(B*)` method form
  proven by CARD-1066 CP-1 (`executed=12`); single-method rows use a trailing
  method wildcard. Never a bare `|` between full paths, never more than two
  operands (CARD-1066's four-operand row selected zero tests).

## Slices

| Slice | Files | Work | Time |
|---|---|---|---|
| S1: test declaration, pins and sensitivity regression | `tests/Antiphon.Tests/Scripts/RemoteScriptContractTests.cs` | (a) Extract `CheckNestedLaneSudo(string text)` from the sudo half of `Nested_lane_never_uses_sudo_or_python` (lines `:1695-1778`), verbatim assertions. (b) Add `var ownedMounts = Block(text, "c1008_owned_mounts")`, `var lookupImage = Block(text, "c994_lookup_image")`, `var retire = Block(text, "case_retire_temp_containers")` to the declaration set and the `||` chain. (c) Assert `Commands(ownedMounts)[1].ShouldBe("require_lane host", "c1008_owned_mounts refuses off the host lane before any sudo")` and the same for `c994_lookup_image`; `retire.ShouldContain("require_lane host", "the retire case is host-lane only")`. (d) For every sudo line in those three blocks assert it contains `sudo -n readlink -e --` or `sudo -n stat -c %F --` with message `"host-lane helper only inspects: " + line`. (e) Add `C1067_Nested_lane_sudo_guard_is_sensitive` with `[Arguments("undeclared-sudo")]`, `("owned-mounts-guard")`, `("lookup-guard")`, `("retire-guard")`: mutants are, respectively, prefix the `docker exec -u 1654:1654` line of `c1008_verify_tmp` (`:4230`) with `sudo -n `; remove the `require_lane host` line from `c1008_owned_mounts`; remove it from `c994_lookup_image`; remove it from `case_retire_temp_containers`. Expected message fragments: `sudo outside a declared host-lane case or helper`, `c1008_owned_mounts refuses off the host lane`, `c994_lookup_image refuses off the host lane`, `the retire case is host-lane only`. Commit, push, run CP-1 (expect red 5/5, D-6). | 20 min |
| S2: helper lane declarations | `scripts/c590-remote.sh` | Insert `    require_lane host` as the first line of `c1008_owned_mounts` (before `local owned=...` at `:4182`) and of `c994_lookup_image` (before `local operation=...` at `:4928`). Nothing else changes. Commit, push, run CP-1..CP-5 green. | 10 min |

Code reports per CP-n with unedited `CHECKPOINT` lines, the S1 red line
included, in ignored `.antiphon/task-<id>.md`. Checkpoint-tool bootstrap is an
explained unlisted build under the slot gate. At most two repair rounds.

## Verification design

- V-1: `Nested_lane_never_uses_sudo_or_python` green after S2 through
  `CheckNestedLaneSudo`: three new blocks declared; guard-first in both helpers
  (D-2); retire case contains the guard; every sudo line in the three blocks is
  read-only inspection (D-3); python assertions untouched.
- V-2: `C1067_Nested_lane_sudo_guard_is_sensitive` × 4: each named mutant
  throws `ShouldAssertException` carrying the named fragment; the unmodified
  text passes. `undeclared-sudo` is the inherited defect's exact shape.
- R-1: `RollingProductionMountTests.C994_Bind_sources_are_canonical_and_typed`
  calls the real `c1008_owned_mounts` under `LANE=host` with the fixture sudo
  shim: exact case roster and outcomes unchanged (the guard is a no-op).
- R-2: `RetiredTempContainerHostTests.C994_Image_receipt_lookup_is_bound` calls
  the real `c994_lookup_image` for 13 variants under `LANE=host` and runs the
  retire case twice: unchanged.
- R-3: `RetiredTempContainerHostTests.C994_Host_context_is_strict`: the
  `detect_lane() { LANE=nested; }` variant still exits 2 with no mutation.
- R-4: `RetiredTempContainerScriptTests.C994_Resume_never_runs_container_cleanup`:
  the reconcile-shaped subshell call of `c1008_owned_mounts` with the
  `c1008_refuse` override under `LANE=host` is unchanged.
- R-5: `bash -n scripts/c590-remote.sh` exit 0.

### Mutation handoff

One production, text-level control per behaviour; each is restored before the
next. Filters are exact methods.

| PC | Mutation (production source) | Exact test | Expected red |
|---|---|---|---|
| PC-1 | Delete the `require_lane host` line from `c1008_owned_mounts`. | `/*/*/RemoteScriptContractTests/Nested_lane_never_uses_sudo_or_python*` | `c1008_owned_mounts refuses off the host lane before any sudo` |
| PC-2 | Delete the `require_lane host` line from `c994_lookup_image`. | same | `c994_lookup_image refuses off the host lane before any sudo` |
| PC-3 | Prefix the `docker exec -u 1654:1654` line of `c1008_verify_tmp` with `sudo -n `. | same | `sudo outside a declared host-lane case or helper` (the inherited shape) |
| PC-4 | Replace `sudo -n stat -c %F --` in `c1008_owned_mounts` with `sudo -n chmod 0755 --`. | same | `host-lane helper only inspects` |
| PC-5 | Delete the `require_lane host` line from `case_retire_temp_containers`. | `/*/*/RetiredTempContainerHostTests/C994_Host_context_is_strict*` and the method above | nested variant exits 0 (`c994-host-context c994-host-lane`); `the retire case is host-lane only` |

Under PC-1, PC-2 and PC-5 the matching `C1067_*` arm also goes red
(`ShouldNotBe`): report it, it is not a second control. All controls remain
pending SourceLanding Mutation after land; ordinary green does not discharge
them.

### Cost

Ordinary Code floor: 8 + 3 + 6 + 4 + 1 = 22 minutes of checkpoints plus the
S1 red run of CP-1 (8) and about 25 minutes of authoring: 30-60 minutes.
No repeat proof after green. All rows pass the committed full SHA with
`--expected-source-sha`; one tool run per committed slice group
(`--rows CP-1` at S1, then `--after all` at S2).

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | all | `tests/Antiphon.Tests -> bin-c1067/` | nested-lane-guard | `/*/*/RemoteScriptContractTests/(Nested_lane_never_uses_sudo_or_python*)\|(C1067*)` | V-1, V-2 | 5 executed (1 + 4 arguments), 0 failed/skipped after S2; at the S1 commit the same row is run once and expected `failed=5` (D-6) | 5 | 8 |
| CP-2 | all | CP-1 | mount-proof | `/*/*/RollingProductionMountTests/C994_Bind_sources_are_canonical_and_typed*` | R-1 | 1 executed, 0 failed/skipped | 1 | 3 |
| CP-3 | all | CP-1 | retire-host | `/*/*/RetiredTempContainerHostTests/(C994_Image_receipt_lookup_is_bound*)\|(C994_Host_context_is_strict*)` | R-2, R-3 | 2 executed, 0 failed/skipped | 2 | 6 |
| CP-4 | all | CP-1 | resume-reconcile | `/*/*/RetiredTempContainerScriptTests/C994_Resume_never_runs_container_cleanup*` | R-4 | 1 executed, 0 failed/skipped | 1 | 4 |
| CP-5 | all | n/a | bash-syntax | `bash -n scripts/c590-remote.sh` | R-5 | exit 0 | n/a | 1 |
