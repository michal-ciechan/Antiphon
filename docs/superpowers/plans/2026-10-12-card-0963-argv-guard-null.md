# CARD-0963: preserve validation through the Unix argv guard

Task/landing owner: Code `39bf31d8`; base `4f252abf4a5f41057cf871d04bc93cf8da96640a`.
Final slice; bound execution when `ANTIPHON_TASK_TOKEN` is present. No service restart.

## Root cause and decision

`UnixPtyArgvGuard.VerifyOrThrow` calls `Contains` on a null executable or
argument element and reads `Count` on a null argument list before
`SessionRunnerRuntime.StartCoreAsync` can validate the launch. JSON binding
does not enforce the request's non-null annotations. The current tree/log
still contains these dereferences; CARD-0863 is landed.

Make the guard accept nullable inputs and inspect only present strings for
NUL. Do not normalize or quote the caller's vector. Null/empty/whitespace
executables reach the existing `ArgumentException("Exe must not be empty.")`.
A valid executable with null args retains the runtime's existing validation
precedence (its argv-policy check already has an empty-array fallback); it must
reach subsequent validation rather than acquire a new error.
Null elements are absent to this NUL-only guard. Unknown backends keep their
existing verdict. NUL rejection and its index remain unchanged.

Files: `src/Antiphon.Agents.Pty/UnixPtyArgvGuard.cs` and
`tests/Antiphon.SessionRunner.Tests/UnixPtyArgvAdmissionTests.cs`.

## Slices

S1: commit this design. S2: commit tests against unchanged production and run
CP-1 red. S3: commit the guard fix, run CP-2/CP-3 green, push each slice.

## Verification design

- V-1: `Malformed_inputs_reach_existing_runtime_validation` uses a handwritten
  outcome table: null/empty/whitespace exe and empty exe with null args => exact
  ArgumentException/Exe message; valid exe with null args, empty args, null
  elements, or spaces/quotes => exact ArgumentException/terminal-size message
  from deliberately invalid dimensions. Capture every outcome before asserting
  the table, so baseline evidence covers every null variant. All rows assert
  zero registrations and no disk effects. Case -> guarded defect -> baseline
  red: null exe/list/element -> corresponding dereference -> NRE instead of
  the handwritten validation outcome. Other rows are regression controls in
  this same failing method, not separately claimed baseline-red tests.
- V-2: `Null_exe_keeps_existing_validation_message_on_launch_routes`, ordinary
  and platform-constrained HTTP POST rows. Development exception middleware
  exposes the fixture's error for assertion; require 500 and the existing Exe
  validation text, excluding NRE. Null exe -> app dereference -> NRE response
  lacks the expected message on the pre-fix source. Random loopback only.
- R-1: full `UnixPtyArgvAdmissionTests`, including existing platform/backend,
  NUL side-effect and HTTP/phone-home checks, unchanged assertions.
- R-2: full `UnixPtyArgvTests`, including CARD-0863 native spaces/quotes,
  control characters, empty strings, Unicode, literal shell text and custody.
- Manual M-1: inspect fresh TRX/receipts, source qualification, diff, fetched
  master merge-tree, tracked-file count, clean worktree and remote SHA.

Scope follows the brief's explicit **no broad Unit lane** exception. No
full-assembly run or unresolved cross-cutting invariant is required. Only two
affected full integration classes; no database or live service qualification.
No repetition after green. CP-1 is intentionally red test-first evidence;
CP-2 reruns its full affected class after the repair at a new committed SHA.

## Positive controls pending SourceLanding Mutation

Every PC remains pending; Code performs no deliberate mutants.

| PC | Exact method | Case -> mutation -> expected red |
|---|---|---|
| PC-1a | Malformed_inputs_reach_existing_runtime_validation | null exe -> remove executable null check -> NRE outcome |
| PC-1b | Malformed_inputs_reach_existing_runtime_validation | null args -> remove list null check -> NRE outcome |
| PC-1c | Malformed_inputs_reach_existing_runtime_validation | null element -> remove element null check -> NRE outcome |
| PC-2a | Null_exe_keeps_existing_validation_message_on_launch_routes | ordinary route/null exe -> remove executable null check -> missing Exe message |
| PC-2b | Null_exe_keeps_existing_validation_message_on_launch_routes | constrained route/null exe -> remove executable null check -> missing Exe message |
| PC-3 | Nul_is_refused_before_native_spawn | NUL -> omit rejection -> missing named refusal/index |
| PC-4 | Native_argv_is_verbatim | spaces/quotes -> transform argv -> native byte-content mismatch |

### Cost

Ordinary green floor: 6 + 6 = 12 minutes including two isolated project
builds. Test-first red: 6 minutes. Authoring/review/report: 10-15 minutes.
No production timeout or assertion is changed.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | S2 | `tests/Antiphon.SessionRunner.Tests -> bin-c963-red/` | argv-baseline | `/*/*/UnixPtyArgvAdmissionTests/*` | V-1, V-2, R-1 | all 15 cases; intentional 3 failures from new methods, 12 existing green | 15 | 6 | true |
| CP-2 | S3 | `tests/Antiphon.SessionRunner.Tests -> bin-c963-runner/` | argv-validation | `/*/*/UnixPtyArgvAdmissionTests/*` | V-1, V-2, R-1 | all 15 cases, 0 failed/skipped | 15 | 6 | true |
| CP-3 | S3 | `tests/Antiphon.Agents.Pty.Tests -> bin-c963-native/` | argv-native | `/*/*/UnixPtyArgvTests/*` | R-2 | all 17 cases, 0 failed/skipped | 17 | 6 | true |
