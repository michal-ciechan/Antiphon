# Why missed: Claude empty-config witness inherits the runner OAuth token

One committed note for CARD-1927. The real-CLI witness
`ClaudeAuthProbeTests.Fresh_config_dir_reports_logged_out_with_the_real_cli`
reports logged in when the test process inherited a nonempty `CLAUDE_CODE_OAUTH_TOKEN`.
CARD-1781.
Owner: [why-missed write-up](../orchestration-loop.md#why-missed-write-up-card-1781).

Debug task `93f5087b` at master `08cc3dcac230658c9bb7f580d32ad91906665fbd`.
No login, logout, or model request was made. No credential value was printed.
Live qualification `34d41b7e` was not read or changed.

## Failure

`tests/Antiphon.SessionRunner.Tests/ClaudeAuthProbeTests.cs`. Category `Integration`.
The fixture points `PhoneHomeSettings.ClaudeHome` at a new temp directory and passes
`environment: _ => null`, so the probe's token check is empty. `ProbeCliAsync` still
builds `ProcessStartInfo` from the parent environment, sets `CLAUDE_CONFIG_DIR` to that
temp directory, and removes only `ANTHROPIC_API_KEY` and `ANTHROPIC_AUTH_TOKEN`.
The OAuth variable stays, which is how the runner's setup-token store reaches
`claude auth status --json`.

Presence-only on this host: `CLAUDE_CODE_OAUTH_TOKEN` nonempty, `ANTHROPIC_API_KEY` unset,
`ANTHROPIC_AUTH_TOKEN` unset, `$HOME/.claude` absent, and the inherited `CLAUDE_CONFIG_DIR`
has no `.credentials.json`. The red result follows the inherited OAuth variable.

Red, before the test change, native output, `UseAppHost=false`, lease
`2bdf486b-0579-492f-a9d6-a3be7959fec3` (waited 30s): 1 executed, 1 failed, 0 skipped,
duration 1s 863ms. Assertion: `dto.LoggedIn` should be False but was True.
Start loadavg `31.41 26.73 15.27`.

The repair is test-only. The witness clears the child environment and copies an
allowlist (`PATH` and locale/runtime names when present), then sets `HOME`,
`USERPROFILE`, `XDG_CONFIG_HOME`, `XDG_DATA_HOME`, temp directories, `CLAUDE_CONFIG_DIR`,
and `DISABLE_AUTOUPDATER` on that same empty temp profile. Production
`ClaudeAuthProbe` is unchanged, so a provisioned runner still passes its OAuth token
to its own probe child.

Green, same filter, lease `95c53655-366f-45bb-8fa9-ec28864f2b88` (waited 15s):
1 executed, 1 passed, duration 2s 140ms. Start loadavg `18.81 25.14 19.09`.
Whole class, lease `db8994ef-6b14-4061-8875-f0565c01a988` (waited 0s): 8 executed,
8 passed, duration 2s 470ms. Start loadavg `12.39 22.74 18.54`.

## Introducing commit/card

introducing commit/card: `15bc5c7ac4f550e59e91f0379a42d23db7a9aa77` (CARD-0628)

That commit adds `ClaudeAuthProbe` and this witness. Its message records CP-3 pending
and "Not yet built or tested." `git log` shows no later edit to
`ClaudeAuthProbeTests.cs`. Plan V-1 is `claude auth status --json` against an empty
`CLAUDE_CONFIG_DIR` through the real binary
(`docs/superpowers/plans/2026-09-23-card-0628-claude-on-server2-runner-plan.md`).

## Approving reviews

approving review(s) with scope (Full/Interim) and the selection they actually ran:

- No Review stage-outcome row was read for CARD-0628 in this task. The plan's CP-3
  selection is `/*/*/(PhoneHomeCommandDispatcherTests*)|(ClaudeAuthProbeTests*)|(PhoneHomeConnectionServiceTests*)/*`,
  with V-1 required to execute rather than skip.
- `fa14995e53ce767344a5e0ec516422ae5737bf72` and `95ac6e13681c3bf77ec053b721d1bbb103b51003`
  both record "CP-3 22/22 passed (V-1 real CLI executed)". That passing V-1 is the
  desktop-shaped run in the plan ("executed (not skipped) on this desktop").
- `d02e14538e677f0a2e62c5550805de8a332ac326` cites review `be0f8640` for exact Claude
  admission and boot-wedge relaunch.

## Outside the selection

why the failing test was outside that selection (lane/category/path): Integration
category, real CLI, path `tests/Antiphon.SessionRunner.Tests/ClaudeAuthProbeTests.cs`.

CARD-1892's explicit class list omitted `ClaudeAuthProbeTests`. The witness is
environment-sensitive: a host whose process has no OAuth variable passes the same
fixture, and a server2 runner that has exported the variable fails it. The derived
index already maps `src/Antiphon.SessionRunner/ClaudeAuthProbe.cs` to
`ClaudeAuthProbeTests`, so a probe edit selects the class. A class run on a
token-free host still passes. The Linux sweep `15406ecc` found it through the
broadened PhoneHome census. `provider.claude.server-runner` in
`tests/test-selection-policy.json` leaves the file unclassified.

## Cheapest pre-land check

the cheapest pre-land check as a path-glob -> class-filter entry for the impacted-classes table of CARD-1782:

`src/Antiphon.SessionRunner/ClaudeAuthProbe.cs` -> `/*/*/ClaudeAuthProbeTests/Fresh_config_dir_reports_logged_out_with_the_real_cli*`

Run that method on a host where `CLAUDE_CODE_OAUTH_TOKEN` is nonempty. Assert the
child environment names and `loggedIn=false`. Do not print the variable's value.

## Exemption

same cause as an already written note: none.
