# CARD-1545 S1c fix round 2

Outcome: D-1 through D-6 are fixed on `feat/card-task-ee22148a` at `d3a522eefeb826ec7f37be3b26832b00fa04401b`, pushed. The plan-imported checkpoint run is red only because CP-24's class filter executes 4 of the required 5 (TUnit omits the Explicit parity method). The isolation wrapper then ran that method green. No source change after the checkpoint. Did not `-Update` the land graph. Did not merge.

## Commits

Base `2c550140e8d1f96f50dfa2080461150a966e906b`. Tip `d3a522eefeb826ec7f37be3b26832b00fa04401b`.

| SHA | Subject |
|---|---|
| `3d8bc71e797fce25aa18ac3f5c958fb4eedae791` | refuse a wildcard LiveClaude compile link |
| `86d3c864549e9cc1c44f3c527d2a9dd321586079` | expect slash and marker failures to stay red after control |
| `5612984bfab68db0873208428bab2c472b29c152` | keep a proven control's slash and marker misses red |
| `bd576348291022118682fea2feb1ef7d0ee15b5a` | refuse bare, mismatched, and variable process launches |
| `f4a4f6ecf7d01a00edc661f15ec0ad7c779e109e` | do not count a helper that starts its own process |
| `503921873e6f1eec67d9b792945d345e607b0ca8` | refuse a hook whose source token is another file |
| `67f6ccafdb3b65c5c9820bbf67768240e2679de7` | classify emitted setup skips under a bounded wait |
| `8594a8a8afc86e056babde3880cc05b64fc9d577` | bind the tailer from the launch config directory |
| `9d6709523f46064bdfc4b253f7208d447f6f6b63` | count the fourth CP-23 executable method |
| `fb494675acdfa35ce4bcd6c689be4894da7d5605` | construct the nested runner session in the tailer witness |
| `eab4a6842804e9ee76b14058e6242c88bfacd062` | list LiveClaude sources as literal compile items |
| `378f454182f3d03354d665da7916b553c09253cf` | return null when a refusal is not a setup skip |
| `bee4e70b0d551e7484839512e221aeab7521eb1c` | bound the outcome classifier wait at twice the measured child |
| `d3a522eefeb826ec7f37be3b26832b00fa04401b` | make bare-launch and argument-mismatch synthetics visible |

Mutants were checked out and never committed.

## D-1 literal compile items

The 15 `tests/Shared/LiveClaude/*.cs` files are literal `Compile` items in `tests/Antiphon.Tests/Antiphon.Tests.csproj` and `tests/Antiphon.Agents.Pty.Tests/Antiphon.Agents.Pty.Tests.csproj`, linked as `TestSupport/LiveClaude/<File>.cs`. Guard `linked_shared_sources_are_literal_compile_items` was red, then green.

Coverage verb, no `-Update`: `dotnet exec tools/Antiphon.Checkpoints/bin-c1545-green/Antiphon.Checkpoints.dll coverage --plan docs/superpowers/plans/2026-10-04-card-1035-checkpoint-child-cwd-plan.md` exit 0, `result=clean`, obligations=1 matched=1 missing=0. One advisory `PC_EARLIER_OTHER_LABEL` on PC-1. It does not block clean.

Land graph `-Check` this round: exit 1, digest-only. Live digest `7746353F611F91BD8CB68B0C3D12432362D19726A0CE7CCB6505851DB3DCEE1F` count=68. Pre-check printed `manifest does not match the selected graph`, then the wrapper printed `audit refused: land graph pre-check refused`. Evaluation did not run. Nothing else was printed. Prior tip digest was `9D7BBFDE28CEF5693596472D737003825248CDA632285B8E17077AE0FD0CC528` count 68. Recorded records equal master `7BDABBA075B620C10A47B50CE1277A932DDD801E9314A932C4EAC03885441A99` count 68. Definition-file diff versus `origin/master` (three-dot) is only those two csproj files (35 insertions). Did not `-Update`. Did not widen the property allow-list.

## D-2 setup-status witness

`S1cOutcomeFeedsS1bClassifier` throws `LiveClaudeRefusal` and catches with `when (LiveClaudeOutcome.SetupStatus(e.Reason) is string setup)`, mapping `20 <Setup>` via `Skipped`. A second unfiltered `catch (LiveClaudeRefusal)` returns null for silent reasons. `RequireCanarySetupFilters` uses `AreEquivalent` on `LiveClaudeOutcome.SetupStatus(e.Reason) is string setup` for the three startup canary methods and the menu canary. It does not start from `PremiseAbsent()`.

Measured classifier child 1219 ms. `ClassifierWaitMs = 2_500`. Healthy re-run after that rebuild passed. Class stays `[Category("Unit"), Timeout(30_000)]` plus `[ParallelLimiter<ProcessSpawnLimit>]`.

Mappings: premise-absent to StartupDefaultAbsent (`20 premise-absent`); bridge-unestablished and bridge-unobserved to BridgeNotEstablished (`20 setup-unclassified`); trust-uncleared to TrustNotCleared (`20 setup-unclassified`); tui-unready and settings-unobserved to ReadyTimeout (`20 setup-unclassified`). Silent (null, not a skip): provider-error, transcript-partial, turn-unconfirmed, bridge-changed-submit, trust-unavailable, menu-unobserved, menu-not-dismissed, recipient-blocked.

## D-3 plan behaviour kept

After a successful control, Slash failing to arm and the off session losing a settings marker stay red (`InvalidOperationException`, `C1545 startup/...`), not exit 20. Exit 20 remains only when the control itself lacks the bridge or markers. `RequireBridge` was not changed.

The s1c test design still labels V-18 and V-22 as setup cases (`slash-setup`, `20 settings-unobserved`). That wording contradicts the plan. The plan's behaviour is what the tests enforce. Those design sentences were not edited. D-11 stays provisional; it was not asked.

## D-4 tailer launch environment

`LaunchEnvironmentBindsItsOwnTranscript`, called from `DedicatedConfigOwnsTrustAndEvidence`, sets process `CLAUDE_CONFIG_DIR` to a decoy and the launch env to the launch config. The bound transcript is the launch jsonl. The test restores the process env and deletes its temp directory. It does not write under `/state/claude`. Attach and adopt tailers were not changed (advisory A-1).

## D-5 delegation synthetics

`d3a522eef` makes the X2 synthetic an expression statement (a `return` call is hidden from `DelegatedCalls`) and the X4 synthetic `RunStartupControlAsync(other)` (a `"claude"` argument is still a bare launch).

## D-6 bound wait and census

CP-23 method list includes `S1cOutcomeFeedsS1bClassifier`. Plan Min is 4. Census text updated in `9d6709523`. V-18/V-22 "20 settings-unobserved" sentences were not rewritten.

## Checkpoint rows

Plan import `20261031-193419-7e62`, commit `d3a522eefeb826ec7f37be3b26832b00fa04401b`, source clean, buildSource verified, serial, total-timeout 90m. Tool verdict RED exit 3. Five rows green, one roster-miss. Unlisted builds: none.

| Row | Result | Counts |
|---|---|---|
| CP-17 | green | executed=3 passed=3 failed=0 skipped=0 |
| CP-23 | green | executed=4 passed=4 failed=0 skipped=0 |
| CP-18 | green | executed=6 passed=6 failed=0 skipped=0 |
| CP-24 | roster-miss | executed=4 passed=4 failed=0 skipped=0, plan Min 5 |
| CP-3 | green | executed=5 passed=5 failed=0 skipped=0 |
| CP-19 | green | executed=4 passed=4 failed=0 skipped=0 |

CP-24-PARITY is not a plan row. TUnit omits `[Explicit]` under the class wildcard, so the plan filter cannot execute `StartupArgvMatchesProductionApplyOff`. The importer has no parity row.

`scripts/test-live-lane-isolation.ps1` (no `-Controls`) published CP-18 captures under `ANTIPHON_LIVE_PORTABLE_CONTEXT` and then ran the consumer:

- Producer `20261031-200337-dd71`: CP-18 executed=6 passed=6 failed=0, exit 0. Context `/tmp/c1545-portable-dFbKRM/context.json`.
- Consumer `20261031-200712-cfe8`: CP-24 executed=4 passed=4 failed=0 (scratch manifest Min lowered from 5 to 4), CP-24-PARITY executed=1 passed=1 failed=0, exit 0. Build reused for parity.

The five CP-24 methods passed across the class filter and the exact-method row. The plan file was not edited. The script's global replace also rewrote the unrun CP-3 `minExecuted` from 5 to 4 inside that scratch manifest only. That row was not executed. The plan-import CP-3 5/5 above is the evidence.

Direct slotted runs on the same commit, before the tool: PTY four classes 18/18 exit 0 (`LiveClaudeExecutableWiringTests` 3, isolation 6, startup 5, delivery 4). Isolation class filter 4/4. Bare Explicit without the portable context refuses `20 missing-portable-context`. That refusal is `LiveClaudeArgvCapture.Required`, and the wrapper above is the run that supplies the context.

Same DLL, after the tool, lease `bd3598b9-b6b9-4069-a80d-52061a774d5a`: `PlanCoverageBindingTests` (7, including `linked_shared_sources_are_literal_compile_items`), `RcRosterRepairTests` (3: Mirror, Explicit, Costs), `C1514LandingSplitBudgetTests` (5: C1404, C1577, C1584, C1410, G2865). Total 15, failed 0, exit 0, duration 2m 51s.

## Mutants

Each file mutant below was proven red, then `git checkout` restored, then the same test passed. None were committed. One build-slot lease per build.

| Mutant | Red message | Restored |
|---|---|---|
| X2 | `C1545 V-1/bare-launch lambda must fail` | green on the project DLL after `d3a522eef` |
| X4 | `C1545 V-1/bare-launch lambda must fail` | green on the project DLL after `d3a522eef` |
| X6 | `C1545 V-1/bare-launch lambda must fail` | source restored; the first restored exec was exit 131 (a lone DLL copied to `/tmp`). Healthy behaviour is the later 18/18 and CP-17 3/3 |
| P3 | `C1545 V-8/source-token expected typed settings-unobserved refusal` | source restored; same invalid exit 131 on the lone DLL. Healthy behaviour is the later 18/18 and CP-18 6/6 |
| P6 | tailer bound the decoy jsonl | green |
| P8 | tailer bound the decoy jsonl | green |
| P7 | `C1545 V-13/projects-root reader uses the profile config directory` | green |
| S1 | `premise-absent did not emit a setup skip` | green |
| S2 | `premise-absent emitted 20 ReadyTimeout` | green |
| S4 | `C1545 profile-audit/Control_auto_connects_a_bridge_session_before_the_first_user-filter` | green (`--no-build`; canaries restored) |
| S5 | `provider-error became 20 ReadyTimeout` | green |
| S6 | `premise-absent emitted 20 ReadyTimeout` | green |
| D4 (A-3) | `Sequence contains no matching element` | green |
| D5 (A-3) | `C1545 outcome mismatched request was accepted` | green |

A-3: the review had witnessed only `rate_limit`. `overloaded` (D4) and the mismatched-request link (D5) now go red.

Not re-applied as file mutants this round: X1, X3, X5, P1, P2, P4, P5, S3, D1, D2, D3, D6. In-memory runners (`LiveClaudeExecutableMutants`, `LiveClaudeProfileMutants`, `LiveClaudeStartupMutants`, `LiveClaudeDeliveryMutants`) run only when `C1545_*_CONTROLS` equals the V id. That variable was unset, so those string-replace controls did not run. The in-test `if (false)` synthetic still runs inside green `ClSessionUsesAttestedExecutable` (X1-style hidden launch). X5's argument check is the same guard X4 deleted.

## Other checks

`scripts/check-evidence-diff.ps1`: from `2c550140e` exit 0, commits=14 entries=0 violations=0, head `d3a522eef`. From `cbe3e6b42` exit 0, commits=66 entries=7 violations=0.

`git fetch origin master` then `git merge-tree --write-tree origin/master HEAD`: tree `70d1aa5ef4d7f9e47233edc57171967db723f2c1`, then only an add/add on `docs/superpowers/plans/2026-10-31-card-1545-rc-scoped-lanes-live-claude-lane-plan.md` (ours `413074d8`, theirs `96132b74`). Exit 1. Did not merge.

## Inherited reds, not chased

`Policy_rows_require_pinned_attribute` legacy-digest-mismatch `8397984d90368a0ef1cbc5991610ae50328165b40ac2503559ae0562d72b554d` on Antiphon.Tests and on agents-pty (3 tests, 1 failed, 2 succeeded). session-runner and pty-host policy witnesses were not re-run; no Debug net9.0 DLLs were on disk. Also named and not chased: `Custody_residue_counts_subdirectories_only`; SessionRunner tailer-area classes that fail the same way on master (HerdrGkpLaunchGuard, HerdrGrokResumeGuard, CompactionContinuationStop, TranscriptAdoptionSafety ToDto). `tests/test-execution-policy.json` and `NativeLinuxPolicyWitness.cs` were not modified.

## Advisories left as advisories

A-1 attach/adopt tailers unchanged. A-2 TRX reader is still not in S1b. A-3 overloaded and mismatched-request are now red, as above. A-4 `5df70c872` carries master's `scripts/lib/live-lane-outcome.ps1` byte-identical. A-5 isolation-script temp leak was not changed.

## Scratch

Alternate `bin-c1545-*`, `bin-land-audit`, project `bin-live-*`, and `bin-f727-c17cd8d8-*` output directories were removed by literal path. Checkpoint evidence under `.antiphon/checkpoints/20261031-193419-7e62`, `20261031-200337-dd71`, and `20261031-200712-cfe8` was left in place. `/tmp/c1545-portable-dFbKRM` was left in place.
