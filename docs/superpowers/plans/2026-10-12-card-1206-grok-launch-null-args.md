# CARD-1206: reject malformed Grok argv before launch effects

Original Code/landing owner: `aa9cb8eb`; base
`eb5330d78596a8d3f313770393c8808c2e5b7d07`. Final slice, bound to the task
when ANTIPHON_TASK_TOKEN is present. Restart: none.

## Root cause and contract

Current HEAD and history contain CARD-0963, but no downstream repair.
After UnixPtyArgvGuard.VerifyOrThrow, SessionRunnerRuntime.StartCoreAsync
passes the caller's vector to GrokRulesTransport.HasExplicitRules when a
GrokRulesPayload is present. Its foreach dereferences a null list; its
StartsWith calls dereference a null element. Both produce an inherited NRE
with positive dimensions. Other later consumers also assume present strings.

Reject malformed Grok argv in StartCoreAsync after existing request and backend
validation, before EnsureGrokRulesArgvSafe and every launch effect. Use the
runtime's ordinary ArgumentException validation contract, parameter request,
message "Args must not be null or contain null elements." There is no existing
positive-dimension Args validation message to preserve: CARD-0963's guard
defers absence, and its invalid-dimension tests must retain their earlier error.
Grok identity is TranscriptFormat or Herdr.AgentKind, as in the rules policy.
Empty and normal vectors keep their behavior and bytes. Unknown backends retain
their current error, including with null args. Non-Grok behavior is outside this
slice. No timeout or existing assertion changes; no services or rollout.

Files: src/Antiphon.SessionRunner/SessionRunnerRuntime.cs;
tests/Antiphon.SessionRunner.Tests/GrokLaunchArgsValidationTests.cs.

## Slices

S1: commit/push this design. S2: commit/push tests with unchanged production;
CP-1 proves null rows red on base production. S3: commit/push repair; CP-2..CP-5
verify green. Evidence is ignored; store a concise Markdown report separately.

## Verification design

- V-1: GrokLaunchArgsValidationTests.Positive_dimensions_validate_grok_argv,
  four handwritten argument rows through real StartAsync with a Grok rules
  payload: null args and null element => exact ArgumentException above;
  empty/normal args => existing grok_rules_argv_unsafe: command_line_budget
  using a deliberately insufficient request budget. All rows require positive
  dimensions, zero registrations, empty registry and no disk effects.
  Case -> defect -> baseline red: null list -> HasExplicitRules foreach -> NRE;
  null element -> HasExplicitRules StartsWith -> NRE. Empty/normal are controls.
- V-2: GrokLaunchArgsValidationTests.Earlier_validation_keeps_its_precedence,
  invalid-dimensions and unknown-backend rows with null args => exact existing
  ArgumentException messages, zero registrations and no disk effects.
- R-1: full UnixPtyArgvAdmissionTests, all 15 CARD-0963/CARD-0863 cases unchanged.
- R-2: full UnixPtyArgvTests, all 17 native/guard CARD-0863 cases unchanged.
- R-3: full GrokRulesFileLaunchTests, all 21 existing rules launch cases unchanged.
- M-1: inspect fresh TRX roster/counts, validate clean source/build receipts;
  run evidence diff guard over base..HEAD; fetch origin/master and merge-tree;
  tracked-file census, clean worktree, remote SHA equality, output cleanup.

The brief explicitly excludes the broad Unit lane. Final covers every named
full affected class above and every V/R; no full assembly or live acceptance.
Invariant: malformed Grok argv is refused before registration, file writes or
spawn; valid argv and prior validation keep their behavior. No unbounded class
selection. No repeat required after green. The first CP-2 combined-class filter
selected zero tests (exit 3), so split it into literal class rows CP-2/CP-4/CP-5.
CP-3 already passed at the repair SHA; retain that receipt without repetition.

## Positive controls pending SourceLanding Mutation

| PC | Detecting filter | Case -> mutation -> expected red |
|---|---|---|
| PC-1a | `/*/*/GrokLaunchArgsValidationTests/Positive_dimensions_validate_grok_argv` | null args -> remove list refusal -> NRE instead of exact validation |
| PC-1b | `/*/*/GrokLaunchArgsValidationTests/Positive_dimensions_validate_grok_argv` | null element -> remove element refusal -> NRE instead of exact validation |
| PC-2 | `/*/*/GrokLaunchArgsValidationTests/Earlier_validation_keeps_its_precedence` | earlier errors -> move argv validation before size/backend checks -> wrong message |

Every PC/variant stays pending; Code does no deliberate mutation cycles.

### Cost

Red floor 6 minutes, ordinary green floor 4 + 6 + 2 + 4 = 16 minutes,
authoring/report 10-15 minutes. Each group uses isolated output and
UseAppHost=false. CP-2/CP-4/CP-5 share one runner build; CP-3 runs sequentially.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | S2 | `tests/Antiphon.SessionRunner.Tests -> bin-c1206-red/` | grok-baseline | `/*/*/GrokLaunchArgsValidationTests/*` | V-1, V-2 | 6 executed; null list/element intentional NRE red, four controls green | 6 | 6 | true |
| CP-2 | S3 | `tests/Antiphon.SessionRunner.Tests -> bin-c1206-runner/` | grok-validation | `/*/*/GrokLaunchArgsValidationTests/*` | V-1, V-2 | 6 executed, zero failed/skipped | 6 | 4 | true |
| CP-3 | S3 | `tests/Antiphon.Agents.Pty.Tests -> bin-c1206-native/` | grok-native | `/*/*/UnixPtyArgvTests/*` | R-2 | 17 executed, zero failed/skipped | 17 | 6 | true |
| CP-4 | S3 | `tests/Antiphon.SessionRunner.Tests -> bin-c1206-runner/` | argv-admission | `/*/*/UnixPtyArgvAdmissionTests/*` | R-1 | 15 executed, zero failed/skipped | 15 | 2 | true |
| CP-5 | S3 | `tests/Antiphon.SessionRunner.Tests -> bin-c1206-runner/` | grok-rules-launch | `/*/*/GrokRulesFileLaunchTests/*` | R-3 | 21 executed, zero failed/skipped | 21 | 4 | true |
