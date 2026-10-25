# Non-antiphon Windows rc failure extraction: a779085c

Extraction complete; this is historical evidence for Debug dispatch, not a repaired or passing qualification. No build, test, service, process-control, native-lock or qualification action was performed. Only this Markdown report was written.

## Provenance and measurement

- Run: `a779085c09ec4397a4b49941fd3cad07`; candidate `a97194ee7a4adf215454fd82c453ba46362596ee`; `origin/rc/2026-10-24`; automated Windows rc lane; outcome `TESTS`.
- Stored interval: `2026-10-24T07:20:40.7310568Z` to `2026-10-25T04:10:32.6135613Z`; 74991.883 s outer wall. `coverageComplete=false` and `testsPassed=false` remain the recorded verdict.
- Evidence root R: `C:\Antiphon\releases\candidates\rc-2026-10-24-a97194ee7a4a\logs\2026-10-24-0720-a779085c`. Every evidence filename and line below is relative to R. Values are embedded here so Linux agents do not need desktop access. TRX row citations identify the result element, including execution/test IDs; full method identity comes from its TestDefinitions join.
- All source file:line citations refer to candidate `a97194ee7a4adf215454fd82c453ba46362596ee`, not the task branch or current rc. Read with `git show a97194ee7a4adf215454fd82c453ba46362596ee:<path>`. Later tips may already contain repairs; this extraction makes no current-tip regression claim.
- Class wall = earliest result start to latest result end within that class/chunk, including gaps and possible overlap with other classes. Sum of test durations is reported separately; neither is exclusive CPU time. Chunk wall comes from TRX Times. Script logs have no per-case timestamps; their wall times are unavailable. No duration was inferred from file mtimes.
- Parameterized failures/skips are coalesced by full class+method+outcome; case labels and row citations retain each expanded row. No missing or skipped row is counted as a pass.

## Totals

| Suite | TRX chunks | Passed | Failed | NotExecuted | Limit |
| --- | --- | --- | --- | --- | --- |
| session-runner | 6 | 1673 | 48 | 5 | TRX counters reconciled |
| agents-pty | 9 | 646 | 2 | 17 | TRX counters reconciled |
| pty-host | 50 | 116 | 0 | 8 | TRX counters reconciled |
| markdown-pdf | 11 | 16 | 0 | 0 | TRX counters reconciled |
| observability | 1 | 11 | 1 | 0 | TRX counters reconciled |
| messaging | 1 | 250 | 0 | 0 | TRX counters reconciled |
| e2e | 2 | 4 | 0 | 0 | E2E group 1 has no TRX; only groups 2–3 counted |

Client: **1,836 passed / 5 failed / 0 pending**, 127 files (4 failed), 632.45 s recorded suite wall; `client-all-tests.log:2904`. Scripts: **582 passed / 9 failed** top-level assertions across 13 script logs; suite exit 1. Error-looking text inside expected negative script scenarios is not an additional failure.

## Ranked dispatch groups

| Rank / lane | Group and recorded failures | Evidence-based assessment / remaining work |
| --- | --- | --- |
| L1 Linux | RunnerCapabilitiesTests — 1 | Confirmed source mechanism: the reference closure includes src/Antiphon.Observability, absent from the daemon path list. Debug can reproduce the source assertion on Linux. |
| L2 Linux, Windows validation retained | test-apphost-graceful-stop.ps1 — 9 | Confirmed fixture mechanism: Install-C716Scripts copies restart/apphost-common but no lib/live-startup-timing.ps1; restart dot-sources it before scenario effects. Missing-file failure is portable; original Windows acceptance remains native. |
| L3 Linux | attentionVisuals.test.ts + homeTasksModel.test.ts — 2 | Recorded diffs isolate ProviderInputWait disagreement in the two every-kind/liveness lists. Portable JavaScript assertions; validate current-tip ownership before repair. |
| L4 Linux | CardEditModal.test.tsx — 2; SessionTerminal.mouse.test.tsx — 1 | Two 20 s timeouts and one alias payload mismatch. Platform-independent test surface; cause and reproducibility unconfirmed. No flake claim without an isolation rerun. |
| L5 Linux investigation, then native if needed | CompletedCardWorkspaceRemovalTests.C1032_RemovalComponentRecovery — 1 | Portable dotnet worker exits before state:Intent marker. Exit cause/output is not in the recorded assertion. Linux can inspect/reproduce worker startup; same result not yet proven. |
| Q Linux qualification / Windows selector investigation | UnixPtyArgvAdmissionTests — 15; AgentPinPublicationTests — 6; AgentPinWorkspaceStoreTests — 3 | 24 failures encounter Linux-specific guards/native primitives on Windows. Do not turn these into Linux product-fix claims. Inspect selection and run the intended Linux contracts separately. |
| W1 native Windows | CompletedCardWorkspaceRemovalTests — other 4; LegacyWorkspaceRemovalGateTests — 10 | 14 path/admission/.git-access failures. Several stop at phone_home_unsupported_target before the intended lease/fence assertion. Windows reproduction needed to distinguish path/fixture defects. |
| W2 native Windows | DaemonStartupDiagnosticsTests — 6 | Each child hits the fixture 45 s WaitForExit budget; both pwsh.exe and powershell.exe, .cmd children. Cause unconfirmed. |
| W3 native Windows | FakeClaudeContractTests — 2 | Clipped paste never SUBMITs; doubled-quote test sees mojibake separator. Native PTY and console-output evidence required. |
| W4 native Windows | LiveStartupIntegrationTests — 1 | Directory.Delete after base.DisposeAsync throws file-in-use on antiphon-20261025.log. Windows sharing mechanism recorded; logger owner/lifetime cause not proven by this extraction. |
| W5 native Windows | PtyHostAdoptionTests — 1; WorkspaceSourceVerificationWireTests — 1 | Running versus Exited adoption assertion; repository-identity digest mismatch with Windows case normalization. Keep native evidence. |
| Separate owner | E2E — 5 failure lines visible, incomplete group | Task e7421374 owns detailed triage. Group 1 watchdog/missing TRX prevents final test counts. Do not double-dispatch. |

## Mechanisms reconstructed from stored evidence

Confirmed fixture dependency mismatch: `scripts/test-apphost-graceful-stop.ps1:44` begins Install-C716Scripts; lines 48–49 copy only restart-apphost.ps1 and apphost-common.ps1 before inert fixture setup. `scripts/restart-apphost.ps1:87` unconditionally dot-sources lib/live-startup-timing.ps1. `script-test-apphost-graceful-stop.log:1` records `FAIL T-1 - gracefulAt=-1 stopAt=-1 exit=1; TEST SEAMS ACTIVE`; lines 2–9 identify the missing fixture helper. The same missing-helper message accompanies all nine failing cases. Intended graceful-stop ordering was never reached in those cases.

Confirmed daemon closure mismatch: `tests/Antiphon.SessionRunner.Tests/RunnerCapabilitiesTests.cs:88` enumerates the runner project closure and line 94 asserts each directory occurs in the daemon script. `src/Antiphon.SessionRunner/Antiphon.SessionRunner.csproj:40` references ../Antiphon.Observability/Antiphon.Observability.csproj, while `scripts/check-daemon-build.ps1:18` through line 23 lists the other six runner directories. `session-runner-all-group-2-tests.log:134` records the missing src/Antiphon.Observability assertion. This is a source-list mismatch, independent of ConPTY.

Confirmed early platform boundaries, not deeper Linux failures: `tests/Antiphon.SessionRunner.Tests/UnixPtyArgvAdmissionTests.cs:284` throws `Unix checkpoint selected on Windows`. `src/Antiphon.SessionRunner/AgentPinWorkspaceStore.cs:35` refuses non-Linux/non-x64 before cwd validation (line 38); pin publication also catches missing native entry points and maps them to pin_platform_unsupported at `src/Antiphon.SessionRunner/AgentPinWorkspacePublisher.cs:167`. `tests/Antiphon.SessionRunner.Tests/AgentPinWorkspaceStoreTests.cs:19` and line 54 explicitly demand native Linux. Publication serialization only records a 10 s timeout; attributing every publication failure to one guard remains an inference.

Recorded JavaScript list mismatch: `client-all-tests.log:2654` shows ProviderInputWait missing from ALL_KINDS versus ATTENTION_VISUALS; `client-all-tests.log:2875` shows ProviderInputWait present in LIVENESS_KINDS but absent from the expected ten-kind list. Assertions are at `client/src/features/attention/attentionVisuals.test.ts:351` and `client/src/features/home/tasks/homeTasksModel.test.ts:389`. Which list owns the intended acceptance is for Debug; no list change was designed here.

Confirmed observability failure location: `tests/Antiphon.Observability.Tests/LiveStartupIntegrationTests.cs:105` awaits base.DisposeAsync at line 107 then recursively deletes the fixture root at line 108. The TRX records IOException for a log file held by another process. This reconstructs the immediate Windows file-sharing mechanism; it does not identify the remaining stream owner.

## Per-suite chunk inventory

P/F/N = Passed/Failed/NotExecuted. Every row is an existing TRX unless explicitly marked missing. The table includes successful chunks so clean siblings are not lost.

### session-runner

| Evidence chunk | P/F/N | TRX wall seconds | Outer exit | Outer timedOut |
| --- | --- | --- | --- | --- |
| `session-runner-all-group-1.trx:3` | 1159/15/3 | 1078.093 | 2 | false |
| `session-runner-all-group-2.trx:3` | 299/28/1 | 519.144 | 2 | false |
| `session-runner-all-group-3.trx:3` | 143/1/0 | 127.580 | 1 | true |
| `session-runner-all-group-4.trx:3` | 68/4/1 | 1058.707 | 2 | false |
| `session-runner-all-group-5.trx:3` | 3/0/0 | 1.832 | 0 | false |
| `session-runner-all-group-6.trx:3` | 1/0/0 | 0.718 | 0 | false |

### agents-pty

| Evidence chunk | P/F/N | TRX wall seconds | Outer exit | Outer timedOut |
| --- | --- | --- | --- | --- |
| `agents-pty-all-group-1.trx:3` | 551/2/17 | 123.879 | 2 | false |
| `agents-pty-all-group-2.trx:3` | 88/0/0 | 50.653 | 0 | false |
| `agents-pty-all-group-3.trx:3` | 1/0/0 | 0.531 | 0 | false |
| `agents-pty-all-profile-13.trx:3` | 1/0/0 | 1.249 | 0 | false |
| `agents-pty-all-profile-14.trx:3` | 1/0/0 | 0.985 | 0 | false |
| `agents-pty-all-profile-49.trx:3` | 1/0/0 | 0.825 | 0 | false |
| `agents-pty-all-profile-50.trx:3` | 1/0/0 | 6.059 | 0 | false |
| `agents-pty-all-profile-51.trx:3` | 1/0/0 | 1.975 | 0 | false |
| `agents-pty-all-profile-52.trx:3` | 1/0/0 | 2.141 | 0 | false |

### pty-host

| Evidence chunk | P/F/N | TRX wall seconds | Outer exit | Outer timedOut |
| --- | --- | --- | --- | --- |
| `pty-host-all-group-1.trx:3` | 65/0/8 | 43.822 | 0 | false |
| `pty-host-all-group-2.trx:3` | 1/0/0 | 0.378 | 0 | false |
| `pty-host-all-profile-1.trx:3` | 1/0/0 | 1.508 | 0 | false |
| `pty-host-all-profile-10.trx:3` | 1/0/0 | 1.017 | 0 | false |
| `pty-host-all-profile-11.trx:3` | 1/0/0 | 0.894 | 0 | false |
| `pty-host-all-profile-12.trx:3` | 1/0/0 | 0.706 | 0 | false |
| `pty-host-all-profile-13.trx:3` | 1/0/0 | 0.373 | 0 | false |
| `pty-host-all-profile-14.trx:3` | 1/0/0 | 0.437 | 0 | false |
| `pty-host-all-profile-15.trx:3` | 1/0/0 | 0.342 | 0 | false |
| `pty-host-all-profile-16.trx:3` | 1/0/0 | 0.460 | 0 | false |
| `pty-host-all-profile-17.trx:3` | 1/0/0 | 0.431 | 0 | false |
| `pty-host-all-profile-18.trx:3` | 1/0/0 | 0.360 | 0 | false |
| `pty-host-all-profile-19.trx:3` | 1/0/0 | 0.866 | 0 | false |
| `pty-host-all-profile-2.trx:3` | 1/0/0 | 0.462 | 0 | false |
| `pty-host-all-profile-20.trx:3` | 1/0/0 | 0.595 | 0 | false |
| `pty-host-all-profile-21.trx:3` | 1/0/0 | 1.357 | 0 | false |
| `pty-host-all-profile-22.trx:3` | 1/0/0 | 0.447 | 0 | false |
| `pty-host-all-profile-23.trx:3` | 1/0/0 | 0.861 | 0 | false |
| `pty-host-all-profile-24.trx:3` | 1/0/0 | 0.673 | 0 | false |
| `pty-host-all-profile-25.trx:3` | 1/0/0 | 0.876 | 0 | false |
| `pty-host-all-profile-26.trx:3` | 1/0/0 | 0.617 | 0 | false |
| `pty-host-all-profile-27.trx:3` | 1/0/0 | 0.376 | 0 | false |
| `pty-host-all-profile-28.trx:3` | 1/0/0 | 0.844 | 0 | false |
| `pty-host-all-profile-29.trx:3` | 1/0/0 | 1.351 | 0 | false |
| `pty-host-all-profile-3.trx:3` | 1/0/0 | 0.346 | 0 | false |
| `pty-host-all-profile-30.trx:3` | 1/0/0 | 0.650 | 0 | false |
| `pty-host-all-profile-31.trx:3` | 1/0/0 | 1.499 | 0 | false |
| `pty-host-all-profile-32.trx:3` | 1/0/0 | 0.372 | 0 | false |
| `pty-host-all-profile-33.trx:3` | 1/0/0 | 0.352 | 0 | false |
| `pty-host-all-profile-34.trx:3` | 1/0/0 | 0.401 | 0 | false |
| `pty-host-all-profile-35.trx:3` | 1/0/0 | 0.405 | 0 | false |
| `pty-host-all-profile-36.trx:3` | 1/0/0 | 0.338 | 0 | false |
| `pty-host-all-profile-37.trx:3` | 1/0/0 | 0.375 | 0 | false |
| `pty-host-all-profile-38.trx:3` | 1/0/0 | 1.023 | 0 | false |
| `pty-host-all-profile-39.trx:3` | 1/0/0 | 0.780 | 0 | false |
| `pty-host-all-profile-4.trx:3` | 1/0/0 | 0.937 | 0 | false |
| `pty-host-all-profile-49.trx:3` | 1/0/0 | 0.903 | 0 | false |
| `pty-host-all-profile-5.trx:3` | 1/0/0 | 1.142 | 0 | false |
| `pty-host-all-profile-50.trx:3` | 1/0/0 | 0.884 | 0 | false |
| `pty-host-all-profile-51.trx:3` | 2/0/0 | 0.882 | 0 | false |
| `pty-host-all-profile-52.trx:3` | 1/0/0 | 0.581 | 0 | false |
| `pty-host-all-profile-53.trx:3` | 2/0/0 | 0.525 | 0 | false |
| `pty-host-all-profile-54.trx:3` | 1/0/0 | 1.483 | 0 | false |
| `pty-host-all-profile-55.trx:3` | 1/0/0 | 0.361 | 0 | false |
| `pty-host-all-profile-56.trx:3` | 1/0/0 | 0.382 | 0 | false |
| `pty-host-all-profile-57.trx:3` | 1/0/0 | 1.211 | 0 | false |
| `pty-host-all-profile-6.trx:3` | 1/0/0 | 0.439 | 0 | false |
| `pty-host-all-profile-7.trx:3` | 1/0/0 | 0.382 | 0 | false |
| `pty-host-all-profile-8.trx:3` | 1/0/0 | 0.752 | 0 | false |
| `pty-host-all-profile-9.trx:3` | 1/0/0 | 1.266 | 0 | false |

### markdown-pdf

| Evidence chunk | P/F/N | TRX wall seconds | Outer exit | Outer timedOut |
| --- | --- | --- | --- | --- |
| `markdown-pdf-all-profile-1.trx:3` | 1/0/0 | 0.411 | 0 | false |
| `markdown-pdf-all-profile-10.trx:3` | 1/0/0 | 0.505 | 0 | false |
| `markdown-pdf-all-profile-11.trx:3` | 1/0/0 | 0.328 | 0 | false |
| `markdown-pdf-all-profile-2.trx:3` | 6/0/0 | 0.994 | 0 | false |
| `markdown-pdf-all-profile-3.trx:3` | 1/0/0 | 0.493 | 0 | false |
| `markdown-pdf-all-profile-4.trx:3` | 1/0/0 | 0.495 | 0 | false |
| `markdown-pdf-all-profile-5.trx:3` | 1/0/0 | 1.387 | 0 | false |
| `markdown-pdf-all-profile-6.trx:3` | 1/0/0 | 1.315 | 0 | false |
| `markdown-pdf-all-profile-7.trx:3` | 1/0/0 | 0.567 | 0 | false |
| `markdown-pdf-all-profile-8.trx:3` | 1/0/0 | 0.638 | 0 | false |
| `markdown-pdf-all-profile-9.trx:3` | 1/0/0 | 0.372 | 0 | false |

### observability

| Evidence chunk | P/F/N | TRX wall seconds | Outer exit | Outer timedOut |
| --- | --- | --- | --- | --- |
| `observability-all.trx:3` | 11/1/0 | 30.822 | 2 | false |

### messaging

| Evidence chunk | P/F/N | TRX wall seconds | Outer exit | Outer timedOut |
| --- | --- | --- | --- | --- |
| `messaging-all.trx:3` | 250/0/0 | 99.694 | 0 | false |

### e2e

| Evidence chunk | P/F/N | TRX wall seconds | Outer exit | Outer timedOut |
| --- | --- | --- | --- | --- |
| `e2e-all-group-1-tests.log:1` | unavailable (missing TRX) | 7200 s watchdog (brief); no final TRX wall | 1 | true |
| `e2e-all-group-2.trx:3` | 3/0/0 | 0.633 | 0 | false |
| `e2e-all-group-3.trx:3` | 1/0/0 | 0.457 | 0 | false |

Runner group 3 is a preserved metadata conflict: summary.suites marks timedOut=true and exitCode=1, but `session-runner-all-group-3-tests.log:32` has a completed 144-test summary (143/1/0), duration 2m 07s 649ms, and its TRX Times spans 127.580 s. No cause for the outer timeout is established. Do not replace this with a two-hour test-body duration.

## Failing .NET methods and exact class reruns

Class names are fully qualified. Use the listed class filter for an isolated rerun; method-scoped reruns replace the last wildcard with the exact method name. These are instructions for subsequent Debug, not commands executed here. A future build/test driver must use scripts/build-slot.ps1 and an isolated output path under docs/testing-and-build.md.

### Antiphon.Agents.Pty.Tests.FakeClaudeContractTests

Project `tests/Antiphon.Agents.Pty.Tests`; chunk `agents-pty-all-group-1.trx:238`; class wall **87.767 s**, sum of test durations 67.106 s; 47 passed / 2 failed / 0 NotExecuted.

Exact class filter: `/*/Antiphon.Agents.Pty.Tests/FakeClaudeContractTests/*`.

| Full failing method | Rows | Recorded error/assertion (first line plus expected/actual) | Expanded cases | TRX row evidence | Candidate source frame | Dispatch classification |
| --- | --- | --- | --- | --- | --- | --- |
| `Antiphon.Agents.Pty.Tests.FakeClaudeContractTests.Random_clipping_with_one_turn_keeps_exactly_one_whole_chunk` | 1 | ShouldAssertException: submitted / should be / True / but was / False / Additional Info: / a clipped body must still SUBMIT — clipping must never eat the submitting CR. Raw: | single row | `agents-pty-all-group-1.trx:865` | `tests/Antiphon.Agents.Pty.Tests/FakeClaudeContractTests.cs:1032` | Native Windows PTY/console encoding; keep Windows |
| `Antiphon.Agents.Pty.Tests.FakeClaudeContractTests.A_doubled_quote_argument_splits_for_a_native_parser_and_not_for_dotnet` | 1 | ShouldAssertException: looseRaw / should contain (case insensitive comparison) / "--append-system-prompt␟a "quoted" word" / but was actually / "--echo-argsÔÉƒ--echo-argv-strictÔÉƒ--append-system-promptÔÉƒa "quoted" word" / Additional Info: / the .NET parser reports the intended argument INTACT on the shredding line — which is precisely how this went unnoticed for months. Line was: C:\Antiphon\releases\checkout\tests\Antiphon.Agents.Pty.Tests\bin\Debug\net9.0\fakeclaude\fakeclaude.exe "--echo-args" "--echo-argv-strict" "--append-system-prompt" "a ""quoted"" word" | single row | `agents-pty-all-group-1.trx:1062` | `tests/Antiphon.Agents.Pty.Tests/FakeClaudeContractTests.cs:1691` | Native Windows PTY/console encoding; keep Windows |

### Antiphon.Observability.Tests.LiveStartupIntegrationTests

Project `tests/Antiphon.Observability.Tests`; chunk `observability-all.trx:22`; class wall **29.486 s**, sum of test durations 29.486 s; 0 passed / 1 failed / 0 NotExecuted.

Exact class filter: `/*/Antiphon.Observability.Tests/LiveStartupIntegrationTests/*`.

| Full failing method | Rows | Recorded error/assertion (first line plus expected/actual) | Expanded cases | TRX row evidence | Candidate source frame | Dispatch classification |
| --- | --- | --- | --- | --- | --- | --- |
| `Antiphon.Observability.Tests.LiveStartupIntegrationTests.V9_Real_server_reports_startup_phases_after_listeners_are_ready` | 1 | IOException: The process cannot access the file 'antiphon-20261025.log' because it is being used by another process. | single row | `observability-all.trx:22` | `tests/Antiphon.Observability.Tests/LiveStartupIntegrationTests.cs:108` | Windows file-sharing deletion symptom; Linux can inspect lifetime but may hide it |

### Antiphon.SessionRunner.Tests.CompletedCardWorkspaceRemovalTests

Project `tests/Antiphon.SessionRunner.Tests`; chunk `session-runner-all-group-1.trx:130`; class wall **994.815 s**, sum of test durations 546.779 s; 89 passed / 5 failed / 3 NotExecuted.

Exact class filter: `/*/Antiphon.SessionRunner.Tests/CompletedCardWorkspaceRemovalTests/*`.

| Full failing method | Rows | Recorded error/assertion (first line plus expected/actual) | Expanded cases | TRX row evidence | Candidate source frame | Dispatch classification |
| --- | --- | --- | --- | --- | --- | --- |
| `Antiphon.SessionRunner.Tests.CompletedCardWorkspaceRemovalTests.C1032_RemovalComponentRecovery` | 1 | ShouldAssertException: child.HasExited / should be / False / but was / True / Additional Info: / worker must pause at state:Intent | single row | `session-runner-all-group-1.trx:1060` | `tests/Antiphon.SessionRunner.Tests/CompletedCardWorkspaceS5Tests.cs:468` | Portable worker, unexplained early exit; Linux Debug candidate |
| `Antiphon.SessionRunner.Tests.CompletedCardWorkspaceRemovalTests.C1032_TransportDispatchReplay` | 1 | ShouldAssertException: legacy.ErrorCode / should be / "workspace_in_use" / but was / "phone_home_unsupported_target" | single row | `session-runner-all-group-1.trx:1098` | `tests/Antiphon.SessionRunner.Tests/CompletedCardWorkspaceTransportTests.cs:174` | Windows/path or .git access sensitive; same Linux symptom unproved |
| `Antiphon.SessionRunner.Tests.CompletedCardWorkspaceRemovalTests.C1032_InspectionHoldShapes` | 2 | UnauthorizedAccessException: Access to the path 'C:\Users\lndco\AppData\Local\Temp\c1032-fence-<fixture>\work\worktrees\task-deadbeef\.git' is denied. ; ShouldAssertException: Directory.GetFiles(bin).Length / should be / 2 / but was / 1 / Additional Info: / requires case-sensitive scratch filesystem | `C1032_InspectionHoldShapes(other-repository, stale_mirror_binding)`; `C1032_InspectionHoldShapes(case-twin, case_ambiguous_content)` | `session-runner-all-group-1.trx:1145`; `session-runner-all-group-1.trx:1159` | `tests/Antiphon.SessionRunner.Tests/CompletedCardWorkspaceInspectionRepairTests.cs:139` | Windows/path or .git access sensitive; same Linux symptom unproved |
| `Antiphon.SessionRunner.Tests.CompletedCardWorkspaceRemovalTests.C1017_RunnerRepositoryIdentity` | 1 | UnauthorizedAccessException: Access to the path 'C:\Users\lndco\AppData\Local\Temp\c1032-fence-<fixture>\work\worktrees\task-deadbeef\.git' is denied. | single row | `session-runner-all-group-1.trx:1196` | `tests/Antiphon.SessionRunner.Tests/CompletedCardWorkspaceS5Tests.cs:96` | Windows/path or .git access sensitive; same Linux symptom unproved |

### Antiphon.SessionRunner.Tests.LegacyWorkspaceRemovalGateTests

Project `tests/Antiphon.SessionRunner.Tests`; chunk `session-runner-all-group-1.trx:1260`; class wall **59.664 s**, sum of test durations 59.657 s; 1 passed / 10 failed / 0 NotExecuted.

Exact class filter: `/*/Antiphon.SessionRunner.Tests/LegacyWorkspaceRemovalGateTests/*`.

| Full failing method | Rows | Recorded error/assertion (first line plus expected/actual) | Expanded cases | TRX row evidence | Candidate source frame | Dispatch classification |
| --- | --- | --- | --- | --- | --- | --- |
| `Antiphon.SessionRunner.Tests.LegacyWorkspaceRemovalGateTests.C1032_VerificationRemovalHeldLease` | 2 | ShouldAssertException: refusal.Code / should be / "workspace_in_use" / but was / "phone_home_unsupported_target" | `C1032_VerificationRemovalHeldLease(True)`; `C1032_VerificationRemovalHeldLease(False)` | `session-runner-all-group-1.trx:1260`; `session-runner-all-group-1.trx:1469` | `tests/Antiphon.SessionRunner.Tests/LegacyWorkspaceRemovalGateTests.cs:164` | Windows/path admission sensitive; same Linux symptom unproved |
| `Antiphon.SessionRunner.Tests.LegacyWorkspaceRemovalGateTests.C1032_LegacyRemovalCreationOrders` | 1 | PhoneHomeAdmissionException: Only a mirror under the worktree root may be removed. | single row | `session-runner-all-group-1.trx:1299` | `tests/Antiphon.SessionRunner.Tests/LegacyWorkspaceRemovalGateTests.cs:57` | Windows/path admission sensitive; same Linux symptom unproved |
| `Antiphon.SessionRunner.Tests.LegacyWorkspaceRemovalGateTests.C1032_LegacyRemovalJournalFence` | 1 | ShouldAssertException: refusal.Code / should be / "workspace_retiring_or_unknown" / but was / "phone_home_unsupported_target" | single row | `session-runner-all-group-1.trx:1310` | `tests/Antiphon.SessionRunner.Tests/LegacyWorkspaceRemovalGateTests.cs:201` | Windows/path admission sensitive; same Linux symptom unproved |
| `Antiphon.SessionRunner.Tests.LegacyWorkspaceRemovalGateTests.C1032_LegacyPruneHeldLease` | 1 | ShouldAssertException: refusal.Code / should be / "workspace_in_use" / but was / "phone_home_unsupported_target" | single row | `session-runner-all-group-1.trx:1348` | `tests/Antiphon.SessionRunner.Tests/LegacyWorkspaceRemovalGateTests.cs:36` | Windows/path admission sensitive; same Linux symptom unproved |
| `Antiphon.SessionRunner.Tests.LegacyWorkspaceRemovalGateTests.C1032_LegacyRemoveReleasedLease` | 1 | PhoneHomeAdmissionException: Only a mirror under the worktree root may be removed. | single row | `session-runner-all-group-1.trx:1387` | `tests/Antiphon.SessionRunner.Tests/LegacyWorkspaceRemovalGateTests.cs:47` | Windows/path admission sensitive; same Linux symptom unproved |
| `Antiphon.SessionRunner.Tests.LegacyWorkspaceRemovalGateTests.C1032_LegacyRemoveHeldLease` | 1 | ShouldAssertException: refusal.Code / should be / "workspace_in_use" / but was / "phone_home_unsupported_target" | single row | `session-runner-all-group-1.trx:1398` | `tests/Antiphon.SessionRunner.Tests/LegacyWorkspaceRemovalGateTests.cs:21` | Windows/path admission sensitive; same Linux symptom unproved |
| `Antiphon.SessionRunner.Tests.LegacyWorkspaceRemovalGateTests.C1032_LegacyUnconfiguredRemoval` | 1 | PhoneHomeAdmissionException: Only a mirror under the worktree root may be removed. | single row | `session-runner-all-group-1.trx:1437` | `tests/Antiphon.SessionRunner.Tests/LegacyWorkspaceRemovalGateTests.cs:94` | Windows/path admission sensitive; same Linux symptom unproved |
| `Antiphon.SessionRunner.Tests.LegacyWorkspaceRemovalGateTests.C1032_LegacyRemovalReleaseOnFailure` | 1 | ShouldAssertException: Task `service.RemoveAsync(new(f.Checkout, true), cancellation.Token)` / should throw / Antiphon.SessionRunner.Tests.FenceEffectException / but threw / Antiphon.SessionRunner.PhoneHomeAdmissionException | single row | `session-runner-all-group-1.trx:1449` | `tests/Antiphon.SessionRunner.Tests/LegacyWorkspaceRemovalGateTests.cs:116` | Windows/path admission sensitive; same Linux symptom unproved |
| `Antiphon.SessionRunner.Tests.LegacyWorkspaceRemovalGateTests.C1032_LegacyRemovalRetainedOwner` | 1 | ShouldAssertException: refusal.Code / should be / "workspace_in_use" / but was / "phone_home_unsupported_target" | single row | `session-runner-all-group-1.trx:1508` | `tests/Antiphon.SessionRunner.Tests/LegacyWorkspaceRemovalGateTests.cs:132` | Windows/path admission sensitive; same Linux symptom unproved |

### Antiphon.SessionRunner.Tests.AgentPinPublicationTests

Project `tests/Antiphon.SessionRunner.Tests`; chunk `session-runner-all-group-2.trx:76`; class wall **10.706 s**, sum of test durations 10.656 s; 0 passed / 6 failed / 0 NotExecuted.

Exact class filter: `/*/Antiphon.SessionRunner.Tests/AgentPinPublicationTests/*`.

| Full failing method | Rows | Recorded error/assertion (first line plus expected/actual) | Expanded cases | TRX row evidence | Candidate source frame | Dispatch classification |
| --- | --- | --- | --- | --- | --- | --- |
| `Antiphon.SessionRunner.Tests.AgentPinPublicationTests.V13_PublicationCustody` | 1 | ShouldAssertException: result["Reason"]!.GetValue<string>() / should be / "pin_custody_missing" / but was / "pin_platform_unsupported" | single row | `session-runner-all-group-2.trx:76` | `tests/Antiphon.SessionRunner.Tests/AgentPinPublicationTests.cs:309` | Windows selection of Linux contracts; Linux qualification, no Linux defect established |
| `Antiphon.SessionRunner.Tests.AgentPinPublicationTests.V34_PublicationAdmission` | 1 | ShouldAssertException: result["Reason"]!.GetValue<string>() / should be / "pin_cwd_invalid" / but was / "pin_platform_unsupported" | single row | `session-runner-all-group-2.trx:110` | `tests/Antiphon.SessionRunner.Tests/AgentPinPublicationTests.cs:309` | Windows selection of Linux contracts; Linux qualification, no Linux defect established |
| `Antiphon.SessionRunner.Tests.AgentPinPublicationTests.V14_PublicationReplay` | 1 | ShouldAssertException: result["Status"]!.GetValue<int>() / should be / 2 / but was / 1 | single row | `session-runner-all-group-2.trx:150` | `tests/Antiphon.SessionRunner.Tests/AgentPinPublicationTests.cs:79` | Windows selection of Linux contracts; Linux qualification, no Linux defect established |
| `Antiphon.SessionRunner.Tests.AgentPinPublicationTests.V15_PublicationCleanup` | 1 | ShouldAssertException: result["Reason"]!.GetValue<string>() / should be / "pin_custody_missing" / but was / "pin_platform_unsupported" | single row | `session-runner-all-group-2.trx:175` | `tests/Antiphon.SessionRunner.Tests/AgentPinPublicationTests.cs:309` | Windows selection of Linux contracts; Linux qualification, no Linux defect established |
| `Antiphon.SessionRunner.Tests.AgentPinPublicationTests.V14_PublicationSerialization` | 1 | TimeoutException: The operation has timed out. | single row | `session-runner-all-group-2.trx:281` | `tests/Antiphon.SessionRunner.Tests/AgentPinPublicationTests.cs:268` | Windows selection of Linux contracts; Linux qualification, no Linux defect established |
| `Antiphon.SessionRunner.Tests.AgentPinPublicationTests.V14_PublicationFences` | 1 | ShouldAssertException: result["Status"]!.GetValue<int>() / should be / 0 / but was / 1 / Additional Info: / {"Status":1,"Reason":"pin_platform_unsupported","Receipt":null} | single row | `session-runner-all-group-2.trx:291` | `tests/Antiphon.SessionRunner.Tests/AgentPinPublicationTests.cs:315` | Windows selection of Linux contracts; Linux qualification, no Linux defect established |

### Antiphon.SessionRunner.Tests.DaemonStartupDiagnosticsTests

Project `tests/Antiphon.SessionRunner.Tests`; chunk `session-runner-all-group-2.trx:403`; class wall **281.939 s**, sum of test durations 281.916 s; 2 passed / 6 failed / 0 NotExecuted.

Exact class filter: `/*/Antiphon.SessionRunner.Tests/DaemonStartupDiagnosticsTests/*`.

| Full failing method | Rows | Recorded error/assertion (first line plus expected/actual) | Expanded cases | TRX row evidence | Candidate source frame | Dispatch classification |
| --- | --- | --- | --- | --- | --- | --- |
| `Antiphon.SessionRunner.Tests.DaemonStartupDiagnosticsTests.Build_failure_retries_without_launching_failed_output` | 4 | TimeoutException: The operation has timed out. | `Build_failure_retries_without_launching_failed_output(pwsh·exe, False, 1)`; `Build_failure_retries_without_launching_failed_output(powershell·exe, False, 1)`; `Build_failure_retries_without_launching_failed_output(pwsh·exe, True, 1)`; `Build_failure_retries_without_launching_failed_output(powershell·exe, True, 1)` | `session-runner-all-group-2.trx:404`; `session-runner-all-group-2.trx:417`; `session-runner-all-group-2.trx:430`; `session-runner-all-group-2.trx:443` | `tests/Antiphon.SessionRunner.Tests/Fixtures/RunnerRestart/RestartFixture.cs:86` | Native Windows shells and .cmd fixture; keep Windows |
| `Antiphon.SessionRunner.Tests.DaemonStartupDiagnosticsTests.Every_relaunch_builds_and_gets_a_fresh_attempt` | 2 | TimeoutException: The operation has timed out. | `Every_relaunch_builds_and_gets_a_fresh_attempt(pwsh·exe)`; `Every_relaunch_builds_and_gets_a_fresh_attempt(powershell·exe)` | `session-runner-all-group-2.trx:456`; `session-runner-all-group-2.trx:469` | `tests/Antiphon.SessionRunner.Tests/Fixtures/RunnerRestart/RestartFixture.cs:86` | Native Windows shells and .cmd fixture; keep Windows |

### Antiphon.SessionRunner.Tests.RunnerCapabilitiesTests

Project `tests/Antiphon.SessionRunner.Tests`; chunk `session-runner-all-group-2.trx:369`; class wall **0.046 s**, sum of test durations 0.044 s; 6 passed / 1 failed / 0 NotExecuted.

Exact class filter: `/*/Antiphon.SessionRunner.Tests/RunnerCapabilitiesTests/*`.

| Full failing method | Rows | Recorded error/assertion (first line plus expected/actual) | Expanded cases | TRX row evidence | Candidate source frame | Dispatch classification |
| --- | --- | --- | --- | --- | --- | --- |
| `Antiphon.SessionRunner.Tests.RunnerCapabilitiesTests.Daemon_build_script_covers_the_runner_project_reference_closure` | 1 | ShouldAssertException: script / should contain (case insensitive comparison) / "src/Antiphon.Observability" / but was actually / "<# / .SYNOPSIS / Warn when an adopted daemon was built before a source change in its project closu..." | single row | `session-runner-all-group-2.trx:374` | `tests/Antiphon.SessionRunner.Tests/RunnerCapabilitiesTests.cs:94` | Linux reproducible: source/reference closure comparison |

### Antiphon.SessionRunner.Tests.UnixPtyArgvAdmissionTests

Project `tests/Antiphon.SessionRunner.Tests`; chunk `session-runner-all-group-2.trx:551`; class wall **0.193 s**, sum of test durations 0.017 s; 0 passed / 15 failed / 0 NotExecuted.

Exact class filter: `/*/Antiphon.SessionRunner.Tests/UnixPtyArgvAdmissionTests/*`.

| Full failing method | Rows | Recorded error/assertion (first line plus expected/actual) | Expanded cases | TRX row evidence | Candidate source frame | Dispatch classification |
| --- | --- | --- | --- | --- | --- | --- |
| `Antiphon.SessionRunner.Tests.UnixPtyArgvAdmissionTests.Null_exe_keeps_existing_validation_message_on_launch_routes` | 2 | InvalidOperationException: Unix checkpoint selected on Windows | `Null_exe_keeps_existing_validation_message_on_launch_routes(platform_constrained)`; `Null_exe_keeps_existing_validation_message_on_launch_routes(ordinary)` | `session-runner-all-group-2.trx:551`; `session-runner-all-group-2.trx:653` | `tests/Antiphon.SessionRunner.Tests/UnixPtyArgvAdmissionTests.cs:284` | Windows selection of Linux contracts; Linux qualification, no Linux defect established |
| `Antiphon.SessionRunner.Tests.UnixPtyArgvAdmissionTests.Nul_request_is_refused_before_effects` | 4 | InvalidOperationException: Unix checkpoint selected on Windows | `Nul_request_is_refused_before_effects(plain)`; `Nul_request_is_refused_before_effects(grok)`; `Nul_request_is_refused_before_effects(grok_payload)`; `Nul_request_is_refused_before_effects(tracked)` | `session-runner-all-group-2.trx:561`; `session-runner-all-group-2.trx:573`; `session-runner-all-group-2.trx:583`; `session-runner-all-group-2.trx:593` | `tests/Antiphon.SessionRunner.Tests/UnixPtyArgvAdmissionTests.cs:284` | Windows selection of Linux contracts; Linux qualification, no Linux defect established |
| `Antiphon.SessionRunner.Tests.UnixPtyArgvAdmissionTests.Nul_refusal_is_named_on_both_launch_routes` | 2 | InvalidOperationException: Unix checkpoint selected on Windows | `Nul_refusal_is_named_on_both_launch_routes(ordinary)`; `Nul_refusal_is_named_on_both_launch_routes(platform_constrained)` | `session-runner-all-group-2.trx:603`; `session-runner-all-group-2.trx:683` | `tests/Antiphon.SessionRunner.Tests/UnixPtyArgvAdmissionTests.cs:284` | Windows selection of Linux contracts; Linux qualification, no Linux defect established |
| `Antiphon.SessionRunner.Tests.UnixPtyArgvAdmissionTests.Platform_and_backend_boundaries_keep_existing_verdicts` | 4 | InvalidOperationException: Unix checkpoint selected on Windows | `Platform_and_backend_boundaries_keep_existing_verdicts(platform_invalid)`; `Platform_and_backend_boundaries_keep_existing_verdicts(platform_mismatch)`; `Platform_and_backend_boundaries_keep_existing_verdicts(unknown_backend)`; `Platform_and_backend_boundaries_keep_existing_verdicts(herdr_nul)` | `session-runner-all-group-2.trx:613`; `session-runner-all-group-2.trx:643`; `session-runner-all-group-2.trx:663`; `session-runner-all-group-2.trx:673` | `tests/Antiphon.SessionRunner.Tests/UnixPtyArgvAdmissionTests.cs:284` | Windows selection of Linux contracts; Linux qualification, no Linux defect established |
| `Antiphon.SessionRunner.Tests.UnixPtyArgvAdmissionTests.Phone_home_nul_refusal_is_named` | 2 | InvalidOperationException: Unix checkpoint selected on Windows | `Phone_home_nul_refusal_is_named(launch)`; `Phone_home_nul_refusal_is_named(platform_constrained)` | `session-runner-all-group-2.trx:623`; `session-runner-all-group-2.trx:633` | `tests/Antiphon.SessionRunner.Tests/UnixPtyArgvAdmissionTests.cs:284` | Windows selection of Linux contracts; Linux qualification, no Linux defect established |
| `Antiphon.SessionRunner.Tests.UnixPtyArgvAdmissionTests.Malformed_inputs_reach_existing_runtime_validation` | 1 | InvalidOperationException: Unix checkpoint selected on Windows | single row | `session-runner-all-group-2.trx:693` | `tests/Antiphon.SessionRunner.Tests/UnixPtyArgvAdmissionTests.cs:284` | Windows selection of Linux contracts; Linux qualification, no Linux defect established |

### Antiphon.SessionRunner.Tests.PtyHostAdoptionTests

Project `tests/Antiphon.SessionRunner.Tests`; chunk `session-runner-all-group-3.trx:111`; class wall **27.959 s**, sum of test durations 27.946 s; 5 passed / 1 failed / 0 NotExecuted.

Exact class filter: `/*/Antiphon.SessionRunner.Tests/PtyHostAdoptionTests/*`.

| Full failing method | Rows | Recorded error/assertion (first line plus expected/actual) | Expanded cases | TRX row evidence | Candidate source frame | Dispatch classification |
| --- | --- | --- | --- | --- | --- | --- |
| `Antiphon.SessionRunner.Tests.PtyHostAdoptionTests.Exit_while_runner_down_is_collected_on_adoption_with_the_real_exit_code` | 1 | ShouldAssertException: dto.Status / should be / "Exited" / but was / "Running" | single row | `session-runner-all-group-3.trx:120` | `tests/Antiphon.SessionRunner.Tests/PtyHostAdoptionTests.cs:176` | Native Windows PTY/adoption; Linux analogue cannot prove this result |

### Antiphon.SessionRunner.Tests.AgentPinWorkspaceStoreTests

Project `tests/Antiphon.SessionRunner.Tests`; chunk `session-runner-all-group-4.trx:9`; class wall **0.513 s**, sum of test durations 0.661 s; 0 passed / 3 failed / 0 NotExecuted.

Exact class filter: `/*/Antiphon.SessionRunner.Tests/AgentPinWorkspaceStoreTests/*`.

| Full failing method | Rows | Recorded error/assertion (first line plus expected/actual) | Expanded cases | TRX row evidence | Candidate source frame | Dispatch classification |
| --- | --- | --- | --- | --- | --- | --- |
| `Antiphon.SessionRunner.Tests.AgentPinWorkspaceStoreTests.V32_InspectPosixPaths` | 1 | ShouldAssertException: OperatingSystem.IsLinux() / should be / True / but was / False / Additional Info: / CP-4a requires native Linux qualification | single row | `session-runner-all-group-4.trx:9` | `tests/Antiphon.SessionRunner.Tests/AgentPinWorkspaceStoreTests.cs:19` | Windows selection of Linux contracts; Linux qualification, no Linux defect established |
| `Antiphon.SessionRunner.Tests.AgentPinWorkspaceStoreTests.V13_InspectPathSafety` | 1 | ShouldAssertException: OperatingSystem.IsLinux() / should be / True / but was / False / Additional Info: / CP-4a requires native Linux qualification | single row | `session-runner-all-group-4.trx:34` | `tests/Antiphon.SessionRunner.Tests/AgentPinWorkspaceStoreTests.cs:54` | Windows selection of Linux contracts; Linux qualification, no Linux defect established |
| `Antiphon.SessionRunner.Tests.AgentPinWorkspaceStoreTests.V34_InspectProtocolAdmission` | 1 | ShouldAssertException: result.Reason / should be / "pin_cwd_invalid" / but was / "pin_platform_unsupported" | single row | `session-runner-all-group-4.trx:79` | `tests/Antiphon.SessionRunner.Tests/AgentPinWorkspaceStoreTests.cs:172` | Windows selection of Linux contracts; Linux qualification, no Linux defect established |

### Antiphon.SessionRunner.Tests.WorkspaceSourceVerificationWireTests

Project `tests/Antiphon.SessionRunner.Tests`; chunk `session-runner-all-group-4.trx:153`; class wall **317.033 s**, sum of test durations 311.653 s; 1 passed / 1 failed / 0 NotExecuted.

Exact class filter: `/*/Antiphon.SessionRunner.Tests/WorkspaceSourceVerificationWireTests/*`.

| Full failing method | Rows | Recorded error/assertion (first line plus expected/actual) | Expanded cases | TRX row evidence | Candidate source frame | Dispatch classification |
| --- | --- | --- | --- | --- | --- | --- |
| `Antiphon.SessionRunner.Tests.WorkspaceSourceVerificationWireTests.C1065_RepositoryIdentityReadIsBoundToSessionCheckout` | 1 | ShouldAssertException: identity.RepositoryIdentity / should be / "D58CCE31D57923C4C98574071590A745EDFB447B5D8A5F16FF55A343C0EA2F07" / but was / "6B377C6C30E9A6DDFB22AFFE5CC212E7A24CBD92939E3C3A9596AD71D713870C" | single row | `session-runner-all-group-4.trx:153` | `tests/Antiphon.SessionRunner.Tests/WorkspaceSourceVerificationWireTests.cs:33` | Windows path/digest sensitive; native confirmation needed |

## NotExecuted methods and recorded reasons

TRX ErrorInfo is empty for these rows; reasons below come from the adjacent suite-log skipped entries, not inference. They remain coverage gaps in this Windows run.

| Full method | Rows | Recorded reason | Expanded cases | Log evidence | Intended-class filter |
| --- | --- | --- | --- | --- | --- |
| `Antiphon.Agents.Pty.Tests.UnixPtyArgvTests.Containment_introduced_nul_is_refused_before_start_intent` | 2 | Unix PTY argv contract requires a Unix PTY and node | `Containment_introduced_nul_is_refused_before_start_intent(arg)`; `Containment_introduced_nul_is_refused_before_start_intent(exe)` | `agents-pty-all-group-1-tests.log:8`; `agents-pty-all-group-1-tests.log:10` | `/*/Antiphon.Agents.Pty.Tests/UnixPtyArgvTests/*` |
| `Antiphon.Agents.Pty.Tests.UnixPtyArgvTests.Native_argv_is_verbatim` | 9 | Unix PTY argv contract requires a Unix PTY and node | `Native_argv_is_verbatim(plain)`; `Native_argv_is_verbatim(empty)`; `Native_argv_is_verbatim(space_tab)`; `Native_argv_is_verbatim(cr)`; `Native_argv_is_verbatim(lf)`; `Native_argv_is_verbatim(quotes_backslashes)`; `Native_argv_is_verbatim(unicode)`; `Native_argv_is_verbatim(literal_shell)`; `Native_argv_is_verbatim(crlf)` | `agents-pty-all-group-1-tests.log:16`; `agents-pty-all-group-1-tests.log:18`; `agents-pty-all-group-1-tests.log:20`; `agents-pty-all-group-1-tests.log:22`; `agents-pty-all-group-1-tests.log:24`; `agents-pty-all-group-1-tests.log:28`; `agents-pty-all-group-1-tests.log:30`; `agents-pty-all-group-1-tests.log:32`; `agents-pty-all-group-1-tests.log:36` | `/*/Antiphon.Agents.Pty.Tests/UnixPtyArgvTests/*` |
| `Antiphon.Agents.Pty.Tests.UnixPtyArgvTests.Nul_is_refused_before_native_spawn` | 4 | Unix PTY argv contract requires a Unix PTY and node | `Nul_is_refused_before_native_spawn(exe)`; `Nul_is_refused_before_native_spawn(last)`; `Nul_is_refused_before_native_spawn(middle)`; `Nul_is_refused_before_native_spawn(first)` | `agents-pty-all-group-1-tests.log:4`; `agents-pty-all-group-1-tests.log:14`; `agents-pty-all-group-1-tests.log:26`; `agents-pty-all-group-1-tests.log:34` | `/*/Antiphon.Agents.Pty.Tests/UnixPtyArgvTests/*` |
| `Antiphon.Agents.Pty.Tests.UnixPtyArgvTests.Original_nul_is_refused_before_containment_and_consumes_attempt` | 1 | Unix PTY argv contract requires a Unix PTY and node | `Original_nul_is_refused_before_containment_and_consumes_attempt` | `agents-pty-all-group-1-tests.log:6` | `/*/Antiphon.Agents.Pty.Tests/UnixPtyArgvTests/*` |
| `Antiphon.Agents.Pty.Tests.UnixPtyArgvTests.Tracked_argv_is_verbatim_after_containment` | 1 | Unix PTY argv contract requires a Unix PTY and node | `Tracked_argv_is_verbatim_after_containment` | `agents-pty-all-group-1-tests.log:12` | `/*/Antiphon.Agents.Pty.Tests/UnixPtyArgvTests/*` |
| `Antiphon.PtyHost.Tests.LinuxCgroupCustodyTests.Child_cannot_sudo_or_move_cgroup` | 1 | The linux-cgroup-v1 backend executes on Linux only. | `Child_cannot_sudo_or_move_cgroup` | `pty-host-all-group-1-tests.log:12` | `/*/Antiphon.PtyHost.Tests/LinuxCgroupCustodyTests/*` |
| `Antiphon.PtyHost.Tests.LinuxCgroupCustodyTests.Double_fork_and_setsid_stay_in_tree` | 1 | The linux-cgroup-v1 backend executes on Linux only. | `Double_fork_and_setsid_stay_in_tree` | `pty-host-all-group-1-tests.log:6` | `/*/Antiphon.PtyHost.Tests/LinuxCgroupCustodyTests/*` |
| `Antiphon.PtyHost.Tests.LinuxCgroupCustodyTests.Generation_mismatch_is_refused` | 1 | The linux-cgroup-v1 backend executes on Linux only. | `Generation_mismatch_is_refused` | `pty-host-all-group-1-tests.log:16` | `/*/Antiphon.PtyHost.Tests/LinuxCgroupCustodyTests/*` |
| `Antiphon.PtyHost.Tests.LinuxCgroupCustodyTests.Lost_host_leaves_unknown_not_receipt` | 1 | The linux-cgroup-v1 backend executes on Linux only. | `Lost_host_leaves_unknown_not_receipt` | `pty-host-all-group-1-tests.log:14` | `/*/Antiphon.PtyHost.Tests/LinuxCgroupCustodyTests/*` |
| `Antiphon.PtyHost.Tests.LinuxCgroupCustodyTests.Root_exit_with_live_descendants_is_draining_until_seal` | 1 | The linux-cgroup-v1 backend executes on Linux only. | `Root_exit_with_live_descendants_is_draining_until_seal` | `pty-host-all-group-1-tests.log:8` | `/*/Antiphon.PtyHost.Tests/LinuxCgroupCustodyTests/*` |
| `Antiphon.PtyHost.Tests.LinuxCgroupCustodyTests.Seal_terminates_and_observes_zero` | 1 | The linux-cgroup-v1 backend executes on Linux only. | `Seal_terminates_and_observes_zero` | `pty-host-all-group-1-tests.log:10` | `/*/Antiphon.PtyHost.Tests/LinuxCgroupCustodyTests/*` |
| `Antiphon.PtyHost.Tests.LinuxCgroupCustodyTests.Second_seal_is_idempotent` | 1 | The linux-cgroup-v1 backend executes on Linux only. | `Second_seal_is_idempotent` | `pty-host-all-group-1-tests.log:18` | `/*/Antiphon.PtyHost.Tests/LinuxCgroupCustodyTests/*` |
| `Antiphon.PtyHost.Tests.LinuxCgroupCustodyTests.Tracked_launch_places_child_in_tree` | 1 | The linux-cgroup-v1 backend executes on Linux only. | `Tracked_launch_places_child_in_tree` | `pty-host-all-group-1-tests.log:4` | `/*/Antiphon.PtyHost.Tests/LinuxCgroupCustodyTests/*` |
| `Antiphon.SessionRunner.Tests.CompletedCardWorkspaceRemovalTests.C1032_InspectionR3Mode` | 2 | Unix executable-bit witness requires Unix filesystem modes | `C1032_InspectionR3Mode(add-executable)`; `C1032_InspectionR3Mode(remove-executable)` | `session-runner-all-group-1-tests.log:4`; `session-runner-all-group-1-tests.log:63` | `/*/Antiphon.SessionRunner.Tests/CompletedCardWorkspaceRemovalTests/*` |
| `Antiphon.SessionRunner.Tests.CompletedCardWorkspaceRemovalTests.C1032_RemovalLinuxIgnoredFifoRefuses` | 1 | Linux named-pipe inventory witness; native FIFO required | `C1032_RemovalLinuxIgnoredFifoRefuses` | `session-runner-all-group-1-tests.log:6` | `/*/Antiphon.SessionRunner.Tests/CompletedCardWorkspaceRemovalTests/*` |
| `Antiphon.SessionRunner.Tests.HostStatsProbeLiveTests.Linux_probe_reads_live_proc` | 1 | Linux /proc probe; this lane is Windows, where Windows_probe_reads_live_system_times runs. | `Linux_probe_reads_live_proc` | `session-runner-all-group-4-tests.log:44` | `/*/Antiphon.SessionRunner.Tests/HostStatsProbeLiveTests/*` |
| `Antiphon.SessionRunner.Tests.RunnerWorkspaceBindingTests.C1032_StoreIdentityRefused` | 1 | Mode-bit denial of the registration file is Linux-only. | `C1032_StoreIdentityRefused(unreadable)` | `session-runner-all-group-2-tests.log:201` | `/*/Antiphon.SessionRunner.Tests/RunnerWorkspaceBindingTests/*` |

Profile exclusions are distinct from NotExecuted: summary records 13 excluded runner UIDs, 105 Agents.Pty UIDs, 23 PtyHost UIDs, 7 E2E UIDs, 0 MarkdownPdf/observability/messaging UIDs (deduplicated per suite; not multiplied by chunks). These are not executed failures. The exact per-method exclusions/reasons follow.

| Suite | Full method | Excluded rows | Reason | Owner |
| --- | --- | --- | --- | --- |
| agents-pty | `Antiphon.Agents.Pty.Tests.Card0490NativeCustodyTests.Abrupt_Qemu_stop_joins_and_rejects_partial_evidence` | 1 | QEMU/native placeholder control | CARD-0490 |
| agents-pty | `Antiphon.Agents.Pty.Tests.Card0490NativeCustodyTests.Guest_shutdown_allows_original_job_zero_and_drain` | 1 | QEMU/native placeholder control | CARD-0490 |
| agents-pty | `Antiphon.Agents.Pty.Tests.Card0490NativeCustodyTests.Qemu_remains_accounted_after_wrapper_exit` | 1 | QEMU/native placeholder control | CARD-0490 |
| agents-pty | `Antiphon.Agents.Pty.Tests.Card0490NativeCustodyTests.Transfer_refuses_child_outside_original_job` | 1 | QEMU/native placeholder control | CARD-0490 |
| agents-pty | `Antiphon.Agents.Pty.Tests.ClaudeAppendSystemPromptCanaryTests.Append_marker_survives_interactive_launch_compact_and_resume` | 1 | headed or real-provider experiment owned by its own canary workflow | CARD-0599 |
| agents-pty | `Antiphon.Agents.Pty.Tests.ClaudeAppendSystemPromptCanaryTests.Multi_line_append_arg_survives_windows_arg_quoting` | 1 | headed or real-provider experiment owned by its own canary workflow | CARD-0599 |
| agents-pty | `Antiphon.Agents.Pty.Tests.ClaudeCompactionCanaryTests.Compact_writes_a_boundary_entry_and_renders_the_compacted_screen_line` | 1 | headed or real-provider experiment owned by its own canary workflow | CARD-0599 |
| agents-pty | `Antiphon.Agents.Pty.Tests.ClaudeCompactionCanaryTests.Manual_compact_with_args_writes_raw_prompt_boundary_and_continuation_with_no_turn_end` | 1 | headed or real-provider experiment owned by its own canary workflow | CARD-0599 |
| agents-pty | `Antiphon.Agents.Pty.Tests.ClaudeComposerCaptureProbeTests.Composer_capture_matrix` | 1 | headed or real-provider experiment owned by its own canary workflow | CARD-0599 |
| agents-pty | `Antiphon.Agents.Pty.Tests.ClaudeComposerCaptureProbeTests.Cut_point_resolution` | 1 | headed or real-provider experiment owned by its own canary workflow | CARD-0599 |
| agents-pty | `Antiphon.Agents.Pty.Tests.ClaudeComposerCaptureProbeTests.Single_chunk_boundary` | 1 | headed or real-provider experiment owned by its own canary workflow | CARD-0599 |
| agents-pty | `Antiphon.Agents.Pty.Tests.ClaudeComposerRenderCanaryTests.Composer_render_contract_for_delivery_style_writes` | 1 | headed or real-provider experiment owned by its own canary workflow | CARD-0599 |
| agents-pty | `Antiphon.Agents.Pty.Tests.ClaudeDangerousTests.Claude_dangerous_can_answer_claude_question_and_receive_acknowledgement` | 1 | headed or real-provider experiment owned by its own canary workflow | CARD-0599 |
| agents-pty | `Antiphon.Agents.Pty.Tests.ClaudeDangerousTests.Claude_dangerous_cruncheddetector_waits_through_slow_bash_task` | 1 | headed or real-provider experiment owned by its own canary workflow | CARD-0599 |
| agents-pty | `Antiphon.Agents.Pty.Tests.ClaudeDangerousTests.Claude_dangerous_detects_claude_asking_a_question` | 1 | headed or real-provider experiment owned by its own canary workflow | CARD-0599 |
| agents-pty | `Antiphon.Agents.Pty.Tests.ClaudeDangerousTests.Claude_dangerous_kill_midflight_no_orphans` | 1 | headed or real-provider experiment owned by its own canary workflow | CARD-0599 |
| agents-pty | `Antiphon.Agents.Pty.Tests.ClaudeDangerousTests.Claude_dangerous_print_returns_pong` | 1 | headed or real-provider experiment owned by its own canary workflow | CARD-0599 |
| agents-pty | `Antiphon.Agents.Pty.Tests.ClaudeDangerousTests.Claude_dangerous_sequential_prompts_share_context` | 1 | headed or real-provider experiment owned by its own canary workflow | CARD-0599 |
| agents-pty | `Antiphon.Agents.Pty.Tests.ClaudeDangerousTests.Claude_dangerous_single_prompt_returns_response` | 1 | headed or real-provider experiment owned by its own canary workflow | CARD-0599 |
| agents-pty | `Antiphon.Agents.Pty.Tests.ClaudeDangerousTests.Claude_dangerous_tool_use_executes_without_permission_prompt` | 1 | headed or real-provider experiment owned by its own canary workflow | CARD-0599 |
| agents-pty | `Antiphon.Agents.Pty.Tests.ClaudeDangerousTests.Claude_dangerous_tui_reaches_ready_within_30s` | 1 | headed or real-provider experiment owned by its own canary workflow | CARD-0599 |
| agents-pty | `Antiphon.Agents.Pty.Tests.ClaudeDangerousTests.Claude_dangerous_version_exits_zero_with_version_string` | 1 | headed or real-provider experiment owned by its own canary workflow | CARD-0599 |
| agents-pty | `Antiphon.Agents.Pty.Tests.ClaudeHeadedLongTests.Cl_headed_resume_preserves_context_across_runner_instances` | 1 | ClSession: Windows, ANTIPHON_HEADED_TESTS, real Claude TUI; long live canary. | CARD-1313 |
| agents-pty | `Antiphon.Agents.Pty.Tests.ClaudeHeadedTests.Cl_headed_exit_command_completes_in_5s` | 1 | headed or real-provider experiment owned by its own canary workflow | CARD-0599 |
| agents-pty | `Antiphon.Agents.Pty.Tests.ClaudeHeadedTests.Cl_headed_kill_midflight_no_orphans` | 1 | headed or real-provider experiment owned by its own canary workflow | CARD-0599 |
| agents-pty | `Antiphon.Agents.Pty.Tests.ClaudeHeadedTests.Cl_headed_sequential_prompts_share_context` | 1 | headed or real-provider experiment owned by its own canary workflow | CARD-0599 |
| agents-pty | `Antiphon.Agents.Pty.Tests.ClaudeHeadedTests.Cl_headed_single_prompt_returns_response` | 1 | headed or real-provider experiment owned by its own canary workflow | CARD-0599 |
| agents-pty | `Antiphon.Agents.Pty.Tests.ClaudeHeadedTests.Cl_headed_tool_use_returns_systeminfo` | 1 | headed or real-provider experiment owned by its own canary workflow | CARD-0599 |
| agents-pty | `Antiphon.Agents.Pty.Tests.ClaudeHeadedTests.Cl_print_mode_returns_response` | 1 | headed or real-provider experiment owned by its own canary workflow | CARD-0599 |
| agents-pty | `Antiphon.Agents.Pty.Tests.ClaudeHeadedTests.Cl_tui_reaches_ready_with_default_detector` | 1 | headed or real-provider experiment owned by its own canary workflow | CARD-0599 |
| agents-pty | `Antiphon.Agents.Pty.Tests.ClaudeHeadedTests.Cl_version_via_pty_exits_zero_with_version_string` | 1 | headed or real-provider experiment owned by its own canary workflow | CARD-0599 |
| agents-pty | `Antiphon.Agents.Pty.Tests.ClaudeHookAdditionalContextCanaryTests.PreToolUse_additionalContext_reaches_the_model_without_blocking` | 1 | headed or real-provider experiment owned by its own canary workflow | CARD-0599 |
| agents-pty | `Antiphon.Agents.Pty.Tests.ClaudeHookAdditionalContextCanaryTests.Subagent_PreToolUse_carries_agent_id` | 1 | headed or real-provider experiment owned by its own canary workflow | CARD-0599 |
| agents-pty | `Antiphon.Agents.Pty.Tests.ClaudeHookAdditionalContextCanaryTests.additionalContext_survival_on_resume_is_not_confounded_by_assistant_text` | 1 | headed or real-provider experiment owned by its own canary workflow | CARD-0599 |
| agents-pty | `Antiphon.Agents.Pty.Tests.ClaudeInputProbeCanaryTests.A_ready_claude_answers_the_input_probe_and_the_composer_is_left_empty` | 1 | headed or real-provider experiment owned by its own canary workflow | CARD-0599 |
| agents-pty | `Antiphon.Agents.Pty.Tests.ClaudeInputProbeCanaryTests.Ctrl_u_clears_a_single_typed_line_from_the_rendered_composer` | 1 | headed or real-provider experiment owned by its own canary workflow | CARD-0599 |
| agents-pty | `Antiphon.Agents.Pty.Tests.ClaudeInteractionTests.Approve_permission_prompt_via_numbered_choice_executes_tool` | 1 | headed or real-provider experiment owned by its own canary workflow | CARD-0599 |
| agents-pty | `Antiphon.Agents.Pty.Tests.ClaudeInteractionTests.Deny_permission_prompt_via_numbered_choice_skips_tool` | 1 | headed or real-provider experiment owned by its own canary workflow | CARD-0599 |
| agents-pty | `Antiphon.Agents.Pty.Tests.ClaudeInteractionTests.Screen_detects_permission_prompt_for_bash_tool` | 1 | headed or real-provider experiment owned by its own canary workflow | CARD-0599 |
| agents-pty | `Antiphon.Agents.Pty.Tests.ClaudeInterruptCanaryTests.Esc_during_a_pure_text_stream_unwinds_the_turn_without_a_marker` | 1 | headed or real-provider experiment owned by its own canary workflow | CARD-0599 |
| agents-pty | `Antiphon.Agents.Pty.Tests.ClaudeInterruptCanaryTests.Esc_mid_turn_stops_work_on_screen_and_writes_the_interrupt_marker_to_the_jsonl` | 1 | headed or real-provider experiment owned by its own canary workflow | CARD-0599 |
| agents-pty | `Antiphon.Agents.Pty.Tests.ClaudeInterruptCanaryTests.Safe_listed_bash_command_auto_approves_without_a_permission_dialog` | 1 | headed or real-provider experiment owned by its own canary workflow | CARD-0599 |
| agents-pty | `Antiphon.Agents.Pty.Tests.ClaudeLocalCommandCanaryTests.Clear_writes_a_command_record_and_no_turn_completion_after_it` | 1 | headed or real-provider experiment owned by its own canary workflow | CARD-0599 |
| agents-pty | `Antiphon.Agents.Pty.Tests.ClaudeOverlayCanaryTests.Esc_on_an_idle_empty_composer_is_a_noop_and_a_body_still_renders` | 1 | headed or real-provider experiment owned by its own canary workflow | CARD-0599 |
| agents-pty | `Antiphon.Agents.Pty.Tests.ClaudeOverlayCanaryTests.One_Esc_dismisses_the_model_overlay_and_restores_the_composer` | 1 | headed or real-provider experiment owned by its own canary workflow | CARD-0599 |
| agents-pty | `Antiphon.Agents.Pty.Tests.ClaudePasteLossCanaryTests.Marked_bodies_into_real_claude_are_diffed_against_its_transcript` | 1 | headed or real-provider experiment owned by its own canary workflow | CARD-0599 |
| agents-pty | `Antiphon.Agents.Pty.Tests.ClaudeRemoteControlAtStartupCanaryTests.Control_auto_connects_a_bridge_session_before_the_first_user` | 1 | headed or real-provider experiment owned by its own canary workflow | CARD-0599 |
| agents-pty | `Antiphon.Agents.Pty.Tests.ClaudeRemoteControlAtStartupCanaryTests.Remote_control_slash_command_still_arms_after_settings_off` | 1 | headed or real-provider experiment owned by its own canary workflow | CARD-0599 |
| agents-pty | `Antiphon.Agents.Pty.Tests.ClaudeRemoteControlAtStartupCanaryTests.Settings_file_prevents_auto_connect_and_keeps_startup_records` | 1 | headed or real-provider experiment owned by its own canary workflow | CARD-0599 |
| agents-pty | `Antiphon.Agents.Pty.Tests.ClaudeRemoteControlMenuCanaryTests.C514_Idle_menu_clearance_preserves_bridge_and_converts_prompt` | 1 | headed or real-provider experiment owned by its own canary workflow | CARD-0599 |
| agents-pty | `Antiphon.Agents.Pty.Tests.ClaudeRemoteControlMenuCanaryTests.One_Esc_dismisses_the_remote_control_management_menu` | 1 | headed or real-provider experiment owned by its own canary workflow | CARD-0599 |
| agents-pty | `Antiphon.Agents.Pty.Tests.ClaudeSignalCanaryTests.Real_claude_still_emits_the_turn_end_signal_we_depend_on` | 1 | headed or real-provider experiment owned by its own canary workflow | CARD-0599 |
| agents-pty | `Antiphon.Agents.Pty.Tests.ClaudeSubmitConfirmCanaryTests.A_collapsed_pastes_jsonl_record_carries_the_full_body_not_the_placeholder` | 1 | headed or real-provider experiment owned by its own canary workflow | CARD-0599 |
| agents-pty | `Antiphon.Agents.Pty.Tests.ClaudeSubmitConfirmCanaryTests.Enter_on_an_empty_composer_is_a_no_op` | 1 | headed or real-provider experiment owned by its own canary workflow | CARD-0599 |
| agents-pty | `Antiphon.Agents.Pty.Tests.ClaudeSubmitContractLiveTests.Submitting_via_two_writes_completes_a_turn` | 1 | headed or real-provider experiment owned by its own canary workflow | CARD-0599 |
| agents-pty | `Antiphon.Agents.Pty.Tests.ClaudeTrustPromptCanaryTests.A_trusted_directory_never_reads_as_blocked` | 1 | headed or real-provider experiment owned by its own canary workflow | CARD-0599 |
| agents-pty | `Antiphon.Agents.Pty.Tests.ClaudeTrustPromptCanaryTests.An_untrusted_directory_blocks_the_tui_and_is_detected_within_seconds` | 1 | headed or real-provider experiment owned by its own canary workflow | CARD-0599 |
| agents-pty | `Antiphon.Agents.Pty.Tests.ClaudeTrustPromptCanaryTests.The_2_1_258_dialog_is_the_highlighted_list_layout` | 1 | headed or real-provider experiment owned by its own canary workflow | CARD-0599 |
| agents-pty | `Antiphon.Agents.Pty.Tests.ClaudeTuiModeTests.Spawned_claude_uses_classic_renderer_not_alternate_screen` | 1 | headed or real-provider experiment owned by its own canary workflow | CARD-0599 |
| agents-pty | `Antiphon.Agents.Pty.Tests.ClaudeVerifiedDeliveryTests.Real_evidence_appears_then_submit_completes_a_turn` | 4 | ClSession real Claude TUI gate; fake-native method remains required. | CARD-1313 |
| agents-pty | `Antiphon.Agents.Pty.Tests.CodexCanaryTests.One_real_turn_writes_UserMessage_AgentMessage_token_count_and_task_complete` | 1 | headed or real-provider experiment owned by its own canary workflow | CARD-0599 |
| agents-pty | `Antiphon.Agents.Pty.Tests.CodexCanaryTests.The_TUI_does_not_print_its_session_id_and_a_typed_newline_does_not_submit` | 1 | headed or real-provider experiment owned by its own canary workflow | CARD-0599 |
| agents-pty | `Antiphon.Agents.Pty.Tests.CodexCanaryTests.The_rollout_is_lazy_and_Enter_on_an_empty_composer_submits_nothing` | 1 | headed or real-provider experiment owned by its own canary workflow | CARD-0599 |
| agents-pty | `Antiphon.Agents.Pty.Tests.CodexComposerCanaryTests.Composer_submit_contract_on_the_modern_backend` | 1 | headed or real-provider experiment owned by its own canary workflow | CARD-0599 |
| agents-pty | `Antiphon.Agents.Pty.Tests.CodexDoneDetectionCanaryTests.Turn_lifecycle_contract_on_the_modern_backend` | 1 | headed or real-provider experiment owned by its own canary workflow | CARD-0599 |
| agents-pty | `Antiphon.Agents.Pty.Tests.CodexMcpBootProbeTests.Baseline_repo_cwd_incident_shape` | 1 | headed or real-provider experiment owned by its own canary workflow | CARD-0599 |
| agents-pty | `Antiphon.Agents.Pty.Tests.CodexMcpBootProbeTests.Baseline_scratch_cwd` | 1 | headed or real-provider experiment owned by its own canary workflow | CARD-0599 |
| agents-pty | `Antiphon.Agents.Pty.Tests.CodexMcpBootProbeTests.Suppressed_repo_cwd` | 1 | headed or real-provider experiment owned by its own canary workflow | CARD-0599 |
| agents-pty | `Antiphon.Agents.Pty.Tests.CodexOverlayCanaryTests.Esc_on_an_idle_empty_composer_is_a_noop_and_a_body_still_renders` | 1 | headed or real-provider experiment owned by its own canary workflow | CARD-0599 |
| agents-pty | `Antiphon.Agents.Pty.Tests.FakeVsRealClipParityTests.Fake_and_real_claude_agree_on_the_chunk_boundary` | 1 | headed or real-provider experiment owned by its own canary workflow | CARD-0599 |
| agents-pty | `Antiphon.Agents.Pty.Tests.FakeVsRealClipParityTests.Fake_and_real_claude_agree_that_a_bracketed_paste_is_not_clipped` | 1 | headed or real-provider experiment owned by its own canary workflow | CARD-0599 |
| agents-pty | `Antiphon.Agents.Pty.Tests.GrokCanaryTests.Composer_submit_contract_on_the_modern_backend` | 1 | headed or real-provider experiment owned by its own canary workflow | CARD-0599 |
| agents-pty | `Antiphon.Agents.Pty.Tests.GrokCanaryTests.Esc_interrupt_shape_in_updates_jsonl` | 1 | headed or real-provider experiment owned by its own canary workflow | CARD-0599 |
| agents-pty | `Antiphon.Agents.Pty.Tests.GrokCanaryTests.Fresh_cwd_and_fresh_grok_home_launch_blocking` | 1 | headed or real-provider experiment owned by its own canary workflow | CARD-0599 |
| agents-pty | `Antiphon.Agents.Pty.Tests.GrokCanaryTests.Models_cache_context_window_is_still_500000` | 1 | headed or real-provider experiment owned by its own canary workflow | CARD-0599 |
| agents-pty | `Antiphon.Agents.Pty.Tests.GrokCanaryTests.Session_recap_shape_and_trigger` | 1 | headed or real-provider experiment owned by its own canary workflow | CARD-0599 |
| agents-pty | `Antiphon.Agents.Pty.Tests.GrokCanaryTests.Turn_end_signals_vs_turn_completed_and_updates_flush_latency` | 1 | headed or real-provider experiment owned by its own canary workflow | CARD-0599 |
| agents-pty | `Antiphon.Agents.Pty.Tests.GrokMidTurnInterruptCanaryTests.Ctrl_C_on_an_empty_composer_mid_tool_turn_cancels_the_turn_and_keeps_the_session_alive` | 1 | GkSession: Windows, ANTIPHON_HEADED_TESTS, real Grok TUI and model API. | CARD-1313 |
| agents-pty | `Antiphon.Agents.Pty.Tests.GrokMidTurnInterruptCanaryTests.Ctrl_C_with_a_draft_in_the_composer_keeps_the_session_alive` | 1 | GkSession: Windows, ANTIPHON_HEADED_TESTS, real Grok TUI and model API. | CARD-1313 |
| agents-pty | `Antiphon.Agents.Pty.Tests.GrokNativeSessionCanaryTests.Create_writes_the_session_directory_at_launch` | 1 | headed or real-provider experiment owned by its own canary workflow | CARD-0599 |
| agents-pty | `Antiphon.Agents.Pty.Tests.GrokNativeSessionCanaryTests.Resume_of_an_unknown_id_exits_nonzero_fast_and_creates_nothing` | 1 | headed or real-provider experiment owned by its own canary workflow | CARD-0599 |
| agents-pty | `Antiphon.Agents.Pty.Tests.GrokNativeSessionCanaryTests.Resume_of_that_directory_starts` | 1 | headed or real-provider experiment owned by its own canary workflow | CARD-0599 |
| agents-pty | `Antiphon.Agents.Pty.Tests.GrokQuestionPopupCanaryTests.Popup_present_after_ask_user_question_usage_does_not_match_answer_clears_esc_does_not` | 1 | headed or real-provider experiment owned by its own canary workflow | CARD-0599 |
| agents-pty | `Antiphon.Agents.Pty.Tests.GrokSignInCanaryTests.Detector_matches_the_live_sign_in_screen_within_10s` | 1 | headed or real-provider experiment owned by its own canary workflow | CARD-0599 |
| agents-pty | `Antiphon.Agents.Pty.Tests.GrokSubmitWhileWorkingCanaryTests.Queued_follow_up_during_a_tool_turn_is_silent_on_jsonl_until_drain` | 1 | headed or real-provider experiment owned by its own canary workflow | CARD-0599 |
| agents-pty | `Antiphon.Agents.Pty.Tests.GrokSubmitWhileWorkingCanaryTests.Submit_during_a_streaming_text_turn_writes_cancelled_then_the_new_user_chunk` | 1 | headed or real-provider experiment owned by its own canary workflow | CARD-0599 |
| agents-pty | `Antiphon.Agents.Pty.Tests.GrokSubmitWhileWorkingCanaryTests.Submit_during_a_tool_turn_writes_cancelled_then_the_new_user_chunk` | 1 | headed or real-provider experiment owned by its own canary workflow | CARD-0599 |
| agents-pty | `Antiphon.Agents.Pty.Tests.GrokUsageOverlayCanaryTests.Esc_on_an_idle_empty_composer_is_a_noop_and_a_body_still_renders` | 1 | headed or real-provider experiment owned by its own canary workflow | CARD-0599 |
| agents-pty | `Antiphon.Agents.Pty.Tests.GrokUsageOverlayCanaryTests.One_Esc_dismisses_the_usage_overlay_and_restores_the_composer` | 1 | headed or real-provider experiment owned by its own canary workflow | CARD-0599 |
| agents-pty | `Antiphon.Agents.Pty.Tests.OrchestratorWorkspaceLayoutCanaryTests.Claude_loads_parent_CLAUDE_md_from_a_nested_checkout` | 1 | Two live Claude TUI methods and two real executable oracles gated by ANTIPHON_PTY_PROVIDER_ORACLES=1; no API in Codex/Grok oracles. | CARD-1313 |
| agents-pty | `Antiphon.Agents.Pty.Tests.OrchestratorWorkspaceLayoutCanaryTests.Claude_sibling_import_is_dropped_without_the_forward_slash_approval` | 1 | Two live Claude TUI methods and two real executable oracles gated by ANTIPHON_PTY_PROVIDER_ORACLES=1; no API in Codex/Grok oracles. | CARD-1313 |
| agents-pty | `Antiphon.Agents.Pty.Tests.OrchestratorWorkspaceLayoutCanaryTests.Codex_prompt_input_is_bounded_at_the_nested_checkout_git_root` | 1 | Two live Claude TUI methods and two real executable oracles gated by ANTIPHON_PTY_PROVIDER_ORACLES=1; no API in Codex/Grok oracles. | CARD-1313 |
| agents-pty | `Antiphon.Agents.Pty.Tests.OrchestratorWorkspaceLayoutCanaryTests.Grok_inspect_is_bounded_at_the_nested_checkout_git_root` | 1 | Two live Claude TUI methods and two real executable oracles gated by ANTIPHON_PTY_PROVIDER_ORACLES=1; no API in Codex/Grok oracles. | CARD-1313 |
| agents-pty | `Antiphon.Agents.Pty.Tests.PtyInputLossExperiments.E1_size_sweep` | 1 | headed or real-provider experiment owned by its own canary workflow | CARD-0599 |
| agents-pty | `Antiphon.Agents.Pty.Tests.PtyInputLossExperiments.E2_slow_drain` | 1 | headed or real-provider experiment owned by its own canary workflow | CARD-0599 |
| agents-pty | `Antiphon.Agents.Pty.Tests.PtyInputLossExperiments.E3_repeated_deliveries_one_session` | 1 | headed or real-provider experiment owned by its own canary workflow | CARD-0599 |
| agents-pty | `Antiphon.Agents.Pty.Tests.PtyInputLossExperiments.E4_encoding_variants` | 1 | headed or real-provider experiment owned by its own canary workflow | CARD-0599 |
| agents-pty | `Antiphon.Agents.Pty.Tests.PtyInputLossExperiments.E5_concurrent_writer` | 1 | headed or real-provider experiment owned by its own canary workflow | CARD-0599 |
| agents-pty | `Antiphon.Agents.Pty.Tests.PtyInputLossExperiments.E6_prior_partial_line` | 1 | headed or real-provider experiment owned by its own canary workflow | CARD-0599 |
| agents-pty | `Antiphon.Agents.Pty.Tests.PtyPasteMarkerExperiments.Real_claude_by_conpty_backend` | 1 | Only these methods launch real Claude; other methods retain Windows/Node/native prerequisites. | CARD-1313 |
| agents-pty | `Antiphon.Agents.Pty.Tests.PtyPasteMarkerExperiments.Real_claude_controls_and_size_sweep` | 1 | Only these methods launch real Claude; other methods retain Windows/Node/native prerequisites. | CARD-1313 |
| agents-pty | `Antiphon.Agents.Pty.Tests.PtyPasteMarkerExperiments.Real_claude_delivery_envelope` | 1 | Only these methods launch real Claude; other methods retain Windows/Node/native prerequisites. | CARD-1313 |
| e2e | `Antiphon.E2E.ChannelOutboundIsolatedTests.FakeGrok_outbound_mode_reads_frozen_request_and_invokes_a_tool` | 1 | F-5 isolated canary skips unless ANTIPHON_HEADED_TESTS, ANTIPHON_BROKER_TESTS and ANTIPHON_CHANNEL_OUTBOUND_ISOLATED are all 1 | CARD-1305 |
| e2e | `Antiphon.E2E.ChannelOutboundIsolatedTests.Four_sources_convert_only_for_the_selected_conversation` | 1 | F-5 isolated canary skips unless ANTIPHON_HEADED_TESTS, ANTIPHON_BROKER_TESTS and ANTIPHON_CHANNEL_OUTBOUND_ISOLATED are all 1 | CARD-1305 |
| e2e | `Antiphon.E2E.ChannelOutboundIsolatedTests.Static_logger_reaches_owned_host_file_after_restart` | 1 | F-5 isolated canary skips unless ANTIPHON_HEADED_TESTS, ANTIPHON_BROKER_TESTS and ANTIPHON_CHANNEL_OUTBOUND_ISOLATED are all 1 | CARD-1305 |
| e2e | `Antiphon.E2E.DelegationPipelineE2ETests.A_multi_step_job_is_classified_as_a_sub_orchestrator` | 1 | real headed provider / distiller approval; owned by its canary workflow | CARD-0599 |
| e2e | `Antiphon.E2E.DelegationPipelineE2ETests.A_small_well_defined_job_is_classified_as_a_worker` | 1 | real headed provider / distiller approval; owned by its canary workflow | CARD-0599 |
| e2e | `Antiphon.E2E.DelegationSequencingE2ETests.An_orchestrator_runs_two_delegates_in_sequence_and_the_second_sees_the_firsts_work` | 1 | real headed provider / distiller approval; owned by its canary workflow | CARD-0599 |
| e2e | `Antiphon.E2E.OutputDistillationApplyCanaryTests.Real_apply_and_long_fallback_reach_parent_and_files_survive_cleanup` | 1 | real headed provider / distiller approval; owned by its canary workflow | CARD-0599 |
| markdown-pdf | `Antiphon.MarkdownPdf.Tests.MarkdownPdfCommandTests.Symlinked_staged_document_is_refused_before_browser_launch` | 1 | Non-Windows filesystem behaviour; does not earn Windows behaviour credit. | CARD-1313 |
| markdown-pdf | `Antiphon.MarkdownPdf.Tests.MarkdownPdfRealBrowserTests.Four_documents_produce_readable_combined_pdf` | 1 | Linux Docker browser image plus ANTIPHON_HEADED_TESTS=1. | CARD-1313 |
| markdown-pdf | `Antiphon.MarkdownPdf.Tests.MarkdownPdfRendererTests.Browser_nonzero_exit_cannot_reuse_a_stale_pdf` | 1 | Non-Windows shell browser fixture; does not earn Windows behaviour credit. | CARD-1313 |
| markdown-pdf | `Antiphon.MarkdownPdf.Tests.MarkdownPdfRendererTests.Real_browser_exit_zero_requires_a_fresh_nonempty_pdf` | 1 | Non-Windows shell browser fixture; does not earn Windows behaviour credit. | CARD-1313 |
| pty-host | `Antiphon.PtyHost.Tests.Card0490NativeExecutionTests.Canceled_run_waits_for_owned_join` | 1 | QEMU/native placeholder control | CARD-0490 |
| pty-host | `Antiphon.PtyHost.Tests.Card0490NativeExecutionTests.Evidence_reparse_is_rejected_before_write` | 1 | QEMU/native placeholder control | CARD-0490 |
| pty-host | `Antiphon.PtyHost.Tests.Card0490NativeExecutionTests.Input_reparse_is_rejected_before_write` | 1 | QEMU/native placeholder control | CARD-0490 |
| pty-host | `Antiphon.PtyHost.Tests.Card0490NativeExecutionTests.Launch_projection_is_direct_and_inherited` | 1 | QEMU/native placeholder control | CARD-0490 |
| pty-host | `Antiphon.PtyHost.Tests.Card0490NativeExecutionTests.Missing_final_frame_invalidates_complete_prefix` | 1 | QEMU/native placeholder control | CARD-0490 |
| pty-host | `Antiphon.PtyHost.Tests.Card0490NativeGuestGuardTests.Guest_build_cannot_reuse_seeded_application_output` | 1 | QEMU/native placeholder control | CARD-0490 |
| pty-host | `Antiphon.PtyHost.Tests.Card0490NativeGuestGuardTests.Guest_refuses_non_ext4_work_root` | 1 | QEMU/native placeholder control | CARD-0490 |
| pty-host | `Antiphon.PtyHost.Tests.Card0490NativeGuestGuardTests.Guest_refuses_prior_phase_workspace` | 1 | QEMU/native placeholder control | CARD-0490 |
| pty-host | `Antiphon.PtyHost.Tests.Card0490NativeGuestGuardTests.Guest_rejects_changed_input_digest` | 1 | QEMU/native placeholder control | CARD-0490 |
| pty-host | `Antiphon.PtyHost.Tests.Card0490NativeGuestGuardTests.Guest_rejects_unlisted_fixture_probe` | 1 | QEMU/native placeholder control | CARD-0490 |
| pty-host | `Antiphon.PtyHost.Tests.Card0490NativeGuestGuardTests.Guest_rejects_unlisted_method_project` | 1 | QEMU/native placeholder control | CARD-0490 |
| pty-host | `Antiphon.PtyHost.Tests.Card0490NativeGuestGuardTests.Guest_rejects_unqualified_toolchain` | 1 | QEMU/native placeholder control | CARD-0490 |
| pty-host | `Antiphon.PtyHost.Tests.Card0490NativeHandoffTests.Crossed_peer_or_run_cannot_transfer` | 1 | QEMU/native placeholder control | CARD-0490 |
| pty-host | `Antiphon.PtyHost.Tests.Card0490NativeHandoffTests.Partial_duplication_never_authorizes_root_exit` | 1 | QEMU/native placeholder control | CARD-0490 |
| pty-host | `Antiphon.PtyHost.Tests.Card0490NativeHandoffTests.Release_ack_requires_original_end_closure` | 1 | QEMU/native placeholder control | CARD-0490 |
| pty-host | `Antiphon.PtyHost.Tests.Card0490NativeHandoffTests.Root_exit_waits_for_transfer_and_guest_ready` | 1 | QEMU/native placeholder control | CARD-0490 |
| pty-host | `Antiphon.PtyHost.Tests.Card0490NativeHandoffTests.Sender_retains_originals_until_adoption` | 1 | QEMU/native placeholder control | CARD-0490 |
| pty-host | `Antiphon.PtyHost.Tests.LinuxPtyHostLauncherTests.Canceled_launch_leaves_no_owned_host` | 1 | Linux-only qualification; this profile is the automated Windows lane | CARD-0590 |
| pty-host | `Antiphon.PtyHost.Tests.LinuxPtyHostLauncherTests.Detach_creates_a_new_session` | 1 | Linux-only qualification; this profile is the automated Windows lane | CARD-0590 |
| pty-host | `Antiphon.PtyHost.Tests.LinuxPtyHostLauncherTests.Detach_failure_exits_without_pipe_readiness` | 1 | Linux-only qualification; this profile is the automated Windows lane | CARD-0590 |
| pty-host | `Antiphon.PtyHost.Tests.LinuxPtyHostLauncherTests.Intermediary_pipes_reach_eof_while_host_lives` | 1 | Linux-only qualification; this profile is the automated Windows lane | CARD-0590 |
| pty-host | `Antiphon.PtyHost.Tests.LinuxPtyHostLauncherTests.Shadow_host_exchanges_bytes_and_exits` | 1 | Linux-only qualification; this profile is the automated Windows lane | CARD-0590 |
| pty-host | `Antiphon.PtyHost.Tests.ShadowCopyStoreTests.Linux_copy_preserves_execute_mode` | 1 | Linux-only qualification; this profile is the automated Windows lane | CARD-0590 |
| session-runner | `Antiphon.SessionRunner.Tests.HerdrGrokNativeSessionLiveTests.Arm_A_unknown_resume_is_refused_before_allocation` | 1 | live desktop session | CARD-0599 |
| session-runner | `Antiphon.SessionRunner.Tests.HerdrGrokNativeSessionLiveTests.Arm_B_create_is_detected_as_grok` | 1 | live desktop session | CARD-0599 |
| session-runner | `Antiphon.SessionRunner.Tests.HerdrGrokNativeSessionLiveTests.Arm_C_resume_of_the_created_directory_is_detected` | 1 | live desktop session | CARD-0599 |
| session-runner | `Antiphon.SessionRunner.Tests.HerdrGrokNativeSessionLiveTests.Arm_D_detect_timeout_on_an_idle_shell_keeps_the_pane_for_relaunch` | 1 | live desktop session | CARD-0599 |
| session-runner | `Antiphon.SessionRunner.Tests.HerdrLabelFollowLiveTests.Owned_tab_and_workspace_getters_support_rename_follow_validation` | 1 | live desktop session | CARD-0599 |
| session-runner | `Antiphon.SessionRunner.Tests.HerdrNamedTabPlacementLiveTests.Live_herdr_creates_reuses_and_refuses_on_a_labelled_tab` | 1 | live desktop session | CARD-0599 |
| session-runner | `Antiphon.SessionRunner.Tests.HerdrPaneDisposalGuardedLiveTests.C461_G083_Foreign_after_runner_inspection` | 1 | HerdrLiveSession flag and installed stock Herdr; dummy child, no real provider. | CARD-1313 |
| session-runner | `Antiphon.SessionRunner.Tests.HerdrPaneDisposalGuardedLiveTests.C461_G084_External_tree_race_at_teardown` | 1 | HerdrLiveSession flag and installed stock Herdr; dummy child, no real provider. | CARD-1313 |
| session-runner | `Antiphon.SessionRunner.Tests.HerdrPaneDisposalGuardedLiveTests.C461_G085_Backend_rpc_fence` | 1 | HerdrLiveSession flag and installed stock Herdr; dummy child, no real provider. | CARD-1313 |
| session-runner | `Antiphon.SessionRunner.Tests.HerdrPaneDisposalGuardedLiveTests.Isolated_backend_closes_only_reviewed_incarnation` | 2 | HerdrLiveSession flag and installed stock Herdr; dummy child, no real provider. | CARD-1313 |
| session-runner | `Antiphon.SessionRunner.Tests.HerdrPaneDisposalGuardedLiveTests.Isolated_backend_recovers_lost_close_result` | 1 | HerdrLiveSession flag and installed stock Herdr; dummy child, no real provider. | CARD-1313 |
| session-runner | `Antiphon.SessionRunner.Tests.LinuxPhoneHomeRunnerTests.Restart_adopts_same_store_session_and_generation` | 1 | Linux-only qualification; this profile is the automated Windows lane | CARD-0590 |

## Client Vitest failures

| File / test group | Full failing test name | File wall s | Failed test s | Recorded first error | Evidence | Lane |
| --- | --- | --- | --- | --- | --- | --- |
| `client/src/features/attention/attentionVisuals.test.ts` | `attentionVisuals draws CapacityRecoveryExhausted and includes it in every-kind coverage` | 0.138 | 0.088 | AssertionError: expected [ 'AgentOutlivedTask', …(59) ] to deeply equal [ 'AgentOutlivedTask', …(60) ] | `client-all-tests.log` | Linux candidate; not rerun |
| `client/src/features/board/CardEditModal.test.tsx` | `CardEditModal sends only the fields that changed, plus the token and editedBy` | 128.164 | 20.147 | Error: STACK_TRACE_ERROR | `client-all-tests.log` | Linux candidate; not rerun |
| `client/src/features/board/CardEditModal.test.tsx` | `CardEditModal sends a changed short alias and an emptied one as a clear` | 128.164 | 5.957 | AssertionError: expected "vi.fn()" to be called with arguments: [ ObjectContaining{…} ][90m; recorded alias was "C", expected "Check header" | `client-all-tests.log` | Linux candidate; not rerun |
| `client/src/features/board/SessionTerminal.mouse.test.tsx` | `CARD-0148 mouse policy reset and instances remain independent` | 112.126 | 22.400 | Error: STACK_TRACE_ERROR | `client-all-tests.log` | Linux candidate; not rerun |
| `client/src/features/home/tasks/homeTasksModel.test.ts` | `livenessFor pins the ten progress kinds and excludes BlockedQuestion and CardStalled` | 0.073 | 0.011 | AssertionError: expected [ 'BriefUndelivered', …(10) ] to deeply equal [ 'BriefUndelivered', …(9) ] | `client-all-tests.log` | Linux candidate; not rerun |

Exact file reruns (each wrapper invocation is its own selection; do not combine into a broad suite):

- `pwsh -NoProfile -File scripts/build-slot.ps1 -Label debug-client -- pwsh -NoProfile -File scripts/test-client.ps1 src/features/attention/attentionVisuals.test.ts`
- `pwsh -NoProfile -File scripts/build-slot.ps1 -Label debug-client -- pwsh -NoProfile -File scripts/test-client.ps1 src/features/board/CardEditModal.test.tsx`
- `pwsh -NoProfile -File scripts/build-slot.ps1 -Label debug-client -- pwsh -NoProfile -File scripts/test-client.ps1 src/features/board/SessionTerminal.mouse.test.tsx`
- `pwsh -NoProfile -File scripts/build-slot.ps1 -Label debug-client -- pwsh -NoProfile -File scripts/test-client.ps1 src/features/home/tasks/homeTasksModel.test.ts`

## Scripts assertion reconstruction

| Script group | Passed | Failed | Stored count evidence | Per-group wall |
| --- | --- | --- | --- | --- |
| `scripts/test-apphost-graceful-stop.ps1` | 2 | 9 | `script-test-apphost-graceful-stop.log:94` | not recorded |
| `scripts/test-apphost-lock-age.ps1` | 40 | 0 | `script-test-apphost-lock-age.log:44` | not recorded |
| `scripts/test-apphost-main-worktree-guard.ps1` | 24 | 0 | `script-test-apphost-main-worktree-guard.log:26` | not recorded |
| `scripts/test-apphost-probe-class.ps1` | 23 | 0 | `script-test-apphost-probe-class.log:25` | not recorded |
| `scripts/test-cleanup-claude-sessions.ps1` | 45 | 0 | `script-test-cleanup-claude-sessions.log:344` | not recorded |
| `scripts/test-cleanup-codex-test-residue.ps1` | 23 | 0 | `script-test-cleanup-codex-test-residue.log:79` | not recorded |
| `scripts/test-client-mode.ps1` | 6 | 0 | `script-test-client-mode.log:8` | not recorded |
| `scripts/test-deploy-am-service.ps1` | 170 | 0 | `script-test-deploy-am-service.log:422` | not recorded |
| `scripts/test-deploy-nightly-watchdog.ps1` | 25 | 0 | `script-test-deploy-nightly-watchdog.log:27` | not recorded |
| `scripts/test-hooks.ps1` | 43 | 0 | `script-test-hooks.log:66` | not recorded (hooks node inner duration 2.457 s) |
| `scripts/test-nightly-report.ps1` | 122 | 0 | `script-test-nightly-report.log:163` | not recorded |
| `scripts/test-reap-zombie-agents.ps1` | 40 | 0 | `script-test-reap-zombie-agents.log:204` | not recorded |
| `scripts/test-stage-value-report.ps1` | 19 | 0 | `script-test-stage-value-report.log:21` | not recorded |

scripts-all-tests.log contains only `C487_INVOCATION=a779085c09ec4397a4b49941fd3cad07` at line 1. The 13 child logs above supply the assertion totals; summary.suites[id=scripts] supplies FAIL/exit 1. Expected negative scenarios such as the deploy remote exit 9 and mock archive HTTP 500 remain passing harness cases.

| Full script function / case | Recorded first failure line | Evidence | Exact isolated rerun |
| --- | --- | --- | --- |
| `scripts/test-apphost-graceful-stop.ps1::Test-T1` | FAIL T-1 - gracefulAt=-1 stopAt=-1 exit=1; TEST SEAMS ACTIVE | `script-test-apphost-graceful-stop.log:1` | `pwsh -NoProfile -File scripts/test-apphost-graceful-stop.ps1 -Case T1` |
| `scripts/test-apphost-graceful-stop.ps1::Test-T2` | FAIL T-2 - subjectExit=1 baselineExit=1 stopTree=0; TEST SEAMS ACTIVE | `script-test-apphost-graceful-stop.log:10` | `pwsh -NoProfile -File scripts/test-apphost-graceful-stop.ps1 -Case T2` |
| `scripts/test-apphost-graceful-stop.ps1::Test-T3` | FAIL T-3 - waitExit=0 stopTree=0 exit=1; TEST SEAMS ACTIVE | `script-test-apphost-graceful-stop.log:19` | `pwsh -NoProfile -File scripts/test-apphost-graceful-stop.ps1 -Case T3` |
| `scripts/test-apphost-graceful-stop.ps1::Test-T4` | FAIL T-4 - TEST SEAMS ACTIVE | `script-test-apphost-graceful-stop.log:28` | `pwsh -NoProfile -File scripts/test-apphost-graceful-stop.ps1 -Case T4` |
| `scripts/test-apphost-graceful-stop.ps1::Test-T5` | FAIL T-5 - declared=True graceful=0 stopTree=0 exit=1; TEST SEAMS ACTIVE | `script-test-apphost-graceful-stop.log:46` | `pwsh -NoProfile -File scripts/test-apphost-graceful-stop.ps1 -Case T5` |
| `scripts/test-apphost-graceful-stop.ps1::Test-T6` | FAIL T-6 - exit=1 graceful=0 stopTree=0; TEST SEAMS ACTIVE | `script-test-apphost-graceful-stop.log:55` | `pwsh -NoProfile -File scripts/test-apphost-graceful-stop.ps1 -Case T6` |
| `scripts/test-apphost-graceful-stop.ps1::Test-T9` | FAIL T-9 - declared=True exit=1 graceful=0 launch=0 stop=0; TEST SEAMS ACTIVE | `script-test-apphost-graceful-stop.log:66` | `pwsh -NoProfile -File scripts/test-apphost-graceful-stop.ps1 -Case T9` |
| `scripts/test-apphost-graceful-stop.ps1::Test-T10` | FAIL T-10 - exit=1 gracefulAt=-1 stopAt=-1 launchAt=-1; TEST SEAMS ACTIVE | `script-test-apphost-graceful-stop.log:75` | `pwsh -NoProfile -File scripts/test-apphost-graceful-stop.ps1 -Case T10` |
| `scripts/test-apphost-graceful-stop.ps1::Test-T12` | FAIL T-12 - exit=1 graceful=0 launch=0; TEST SEAMS ACTIVE | `script-test-apphost-graceful-stop.log:84` | `pwsh -NoProfile -File scripts/test-apphost-graceful-stop.ps1 -Case T12` |

Case runner accepts T1 or T-1 normalization at `scripts/test-apphost-graceful-stop.ps1:646`–654. No skipped/NotExecuted verdict is recorded for the script assertions. Linux can reconstruct the missing fixture dependency; full Windows restart acceptance requires the native lane.

## E2E observation only — task e7421374 owns triage

Group 1: summary says timedOut=true, missing-trx, exit 1; brief identifies a 7,200 s watchdog. Five failure headers are visible below; these are lower-bound observed failures, not final suite counts. Groups 2 and 3 have passing TRXs (3 and 1 tests). The run summary also names four missing expanded rows in the required-UID union: three CreatedResourceTests methods and LandDeliveryFixtureProbeTests.ProtocolProgress_counts_committed_protocol_git_records_only. Presence of a passing later-group result does not recover group 1 coverage.

| Recorded failure header | First error | Log evidence | Recorded source frame |
| --- | --- | --- | --- |
| failed C488_ApprovalPollingCannotConfirm (1m 08s 952ms) | TUnit.Engine.Exceptions.TestFailedException: [Test Failure] InvalidOperationException: Sequence contains no matching element | `e2e-all-group-1-tests.log:4` | at Antiphon.E2E.AgentTaskLandDeliveryE2ETests.C467_V32_StatusPollingCannotDischargeUnreceivedOutcome(String state) in C:\Antiphon\releases\checkout\tests\Antiphon.E2E\AgentTaskLandDeliveryE2ETests.cs:260 |
| failed C488_ApprovalReceiptSaveFailureNeverRetypes (4m 08s 265ms) | TUnit.Engine.Exceptions.TestFailedException: [Test Failure] TimeoutException: queue-before-verdict (quiet 60s after 73.2s; progress 47) | `e2e-all-group-1-tests.log:7008` | at Antiphon.E2E.Fixtures.LandDeliveryFixture.UntilProtocolAsync(Func`1 predicate, String evidence, Int32 quietSeconds) in C:\Antiphon\releases\checkout\tests\Antiphon.E2E\Fixtures\LandDeliveryFixture.cs:368 |
| failed C467_V29_RealOutcomeProducerMatrix(landed) (2m 27s 809ms) | TUnit.Engine.Exceptions.TestFailedException: [Test Failure] TimeoutException: native caller ready | `e2e-all-group-1-tests.log:10544` | at Antiphon.E2E.Fixtures.LandDeliveryFixture.InitializeAsync(Boolean busy, String cut, Boolean dispatch, String completionForm) in C:\Antiphon\releases\checkout\tests\Antiphon.E2E\Fixtures\LandDeliveryFixture.cs:117 |
| failed C467_V29_RealOutcomeProducerMatrix(residue-cleanup) (1m 28s 708ms) | TUnit.Engine.Exceptions.TestFailedException: [Test Failure] ShouldAssertException: terminal.Type | `e2e-all-group-1-tests.log:13150` | at Antiphon.E2E.AgentTaskLandDeliveryE2ETests.C467_V29_RealOutcomeProducerMatrix(String outcome) in C:\Antiphon\releases\checkout\tests\Antiphon.E2E\AgentTaskLandDeliveryE2ETests.cs:199 |
| failed C467_V29_RealOutcomeProducerMatrix(operation-refusal) (1m 50s 385ms) | TUnit.Engine.Exceptions.TestFailedException: [Test Failure] TimeoutException: complete native Land receipt (quiet 60s after 72.8s; progress 46) | `e2e-all-group-1-tests.log:22141` | at Antiphon.E2E.Fixtures.LandDeliveryFixture.UntilProtocolAsync(Func`1 predicate, String evidence, Int32 quietSeconds) in C:\Antiphon\releases\checkout\tests\Antiphon.E2E\Fixtures\LandDeliveryFixture.cs:368 |

No E2E diagnosis, rerun, or native qualification process was started by this task.

## Original failure-chunk selections

These are the recorded launch filters, copied from execution diagnostics Command line arguments. They preserve original chunk scope; isolated class filters above are the intended Debug selections. Observability launched without a treenode filter (whole 12-test assembly).

- `session-runner-all-group-1`: `/*/Antiphon.SessionRunner.Tests/(CodexWindowsLaunchPolicyTests*)\|(CompletedCardWorkspaceRemovalTests*)\|(FakeHerdrServerListenerTests*)\|(GrokRulesArgvPolicyTests*)\|(GrokRulesFileLaunchTests*)\|(GrokTranscriptTailerTests*)\|(HerdrAdoptionSweepTests*)\|(HerdrAttachTests*)\|(HerdrLabelFollowSchedulingTests*)\|(HerdrLabelObservationTests*)\|(HerdrLabelSnapshotTests*)\|(HerdrLaunchShapeTests*)\|(HerdrNamedTabPlacementTests*)\|(HerdrPaneDisposalConcurrencyTests*)\|(HerdrPaneDisposalIdentityTests*)\|(HerdrPaneDisposalRedactionTests*)\|(HerdrPaneDisposalServiceTests*)\|(LegacyWorkspaceRemovalGateTests*)\|(PhoneHomeCommandDispatcherTests*)\|(PromptSubmissionMatchTests*)\|(RepositoryCloneSourceTests*)\|(RunnerAbsenceEvidenceRuntimeTests*)\|(RunnerAbsenceEvidenceTests*)\|(RunnerCustodyTests*)\|(RunnerRestartHealthTests*)\|(RunnerRestartPreflightCacheTests*)\|(RunnerRestartPreflightSafetyTests*)\|(RunnerWorkspaceServiceTests*)\|(TerminalSeatReleaseTests*)\|(TranscriptAdoptionSafetyTests*)/*` — `session-runner-all-group-1-execution/log_261025000149968.diag:10`
- `session-runner-all-group-2`: `/*/Antiphon.SessionRunner.Tests/(AgentPinPublicationTests*)\|(BuildSlotBrokerTests*)\|(ClaudeAuthProbeTests*)\|(CodexAuthProbeTests*)\|(CodexCliVersionProbeTests*)\|(CodexQuotaEvidenceTests*)\|(CodexTranscriptNormalizerTests*)\|(CodexTranscriptTailerTests*)\|(CpuSpinDetectorTests*)\|(DaemonStartupDiagnosticsTests*)\|(GrokRulesFileStoreTests*)\|(GrokRulesRunnerRefusalTests*)\|(HerdrClientTests*)\|(HerdrGkpLaunchGuardTests*)\|(HerdrGrokResumeGuardTests*)\|(HerdrNamedTabResolverTests*)\|(HerdrPaneDisposalStopRegressionTests*)\|(HerdrPaneSidecarTests*)\|(HerdrPlacementCheckRouteTests*)\|(HerdrRunnerSessionTests*)\|(HerdrTransportTests*)\|(HostStatsProbeParseTests*)\|(HostStatsStoreTests*)\|(PhoneHomeConnectionServiceTests*)\|(RunnerAbsenceEvidenceContractTests*)\|(RunnerCapabilitiesTests*)\|(RunnerRestartScriptCompatibilityTests*)\|(RunnerWorkspaceBindingTests*)\|(TranscriptWorkingStateTests*)\|(UnixPtyArgvAdmissionTests*)/*` — `session-runner-all-group-2-execution/log_261025002019050.diag:10`
- `session-runner-all-group-3`: `/*/Antiphon.SessionRunner.Tests/(BuildSlotEndpointTests*)\|(BuildSlotEndToEndTests*)\|(BuildSlotMetricsTests*)\|(C1022BackendCapabilitiesTests*)\|(CodexCliVersionWindowsTests*)\|(CompactionContinuationStopTests*)\|(CompletedCardWorkspaceContractTests*)\|(DaemonLogRotationTests*)\|(GrokLaunchArgsValidationTests*)\|(GrokRulesAdoptionTests*)\|(GrokRulesStoreFailureTests*)\|(HerdrEventPumpTests*)\|(HerdrPaneAllocatorTests*)\|(HerdrPaneChildKillTests*)\|(HerdrStatusPushTests*)\|(HostStatsEndpointTests*)\|(HostStatsSamplerTests*)\|(LinuxCustodyProbeTests*)\|(PtyHostAdoptionTests*)\|(PushProbeOutcomeTests*)\|(RemoteControlConditionalInputTests*)\|(RunnerCustodyLedgerBackendTests*)\|(RunnerMultilinePromptDeliveryTests*)\|(RunnerSessionGenerationTests*)\|(RunnerStartupDiagnosticsTests*)\|(SessionLivenessTests*)\|(TranscriptClaimRegistryTests*)\|(TranscriptIngestionLatencyProbeTests*)\|(TranscriptTailerCompactionTests*)\|(TranscriptTailerObservationTests*)/*` — `session-runner-all-group-3-execution/log_261025002919606.diag:10`
- `session-runner-all-group-4`: `/*/Antiphon.SessionRunner.Tests/(AgentPinWorkspaceStoreTests*)\|(BlockedParkWireTests*)\|(BuildSlotSettingsTests*)\|(C1022BackendLaunchTests*)\|(ClaudeDialogInputGuardTests*)\|(CodexCommandLengthHttpAcceptanceTests*)\|(CodexLaunchRefusalTests*)\|(CodexProviderAuthRoutingTests*)\|(FirstWriteRaceTests*)\|(GrokAuthProbeTests*)\|(GrokRulesHttpAcceptanceTests*)\|(HerdrClientSurfaceTests*)\|(HerdrPaneDisposalEligibilityTests*)\|(HerdrSnapshotBoundedReadTests*)\|(HostStatsProbeLiveTests*)\|(ProcessSpawnLimitTests*)\|(PtyBackendSeamTests*)\|(PtyHostDeploymentGuardTests*)\|(PushCredentialPolicyFileTests*)\|(RunnerAbsenceEvidencePhoneHomeTests*)\|(RunnerCapacityCountTests*)\|(RunnerCustodyCrashTests*)\|(RunnerPlatformContractTests*)\|(RunnerPlatformLaunchTests*)\|(RunnerStartupReadinessTests*)\|(SessionBufferBoundsTests*)\|(SessionCpuWatchdogTests*)\|(SessionRunnerEventHubTests*)\|(WorkspaceParkPublicationTests*)\|(WorkspaceSourceVerificationWireTests*)/*` — `session-runner-all-group-4-execution/log_261025005949070.diag:10`
- `agents-pty-all-group-1`: `/*/Antiphon.Agents.Pty.Tests/(AnsiStripperTests*)\|(ClaudeEffortDismissalReplayTests*)\|(ClaudeEffortPromptTests*)\|(ClaudeProjectTrustTests*)\|(ClaudeScreenTests*)\|(ClaudeStartupReadinessTests*)\|(ClaudeStartupTrustPromptTests*)\|(CodexReadyWaitTests*)\|(CodexStartupReadinessTests*)\|(CodexV0156StartupPromptTests*)\|(ComposerDeliveryEvidenceTests*)\|(ComposerInputProbeTests*)\|(ConPtyEnvironmentIsolationGuardTests*)\|(Da1StartupResponderTests*)\|(FakeClaudeContractTests*)\|(FakeGrokContractTests*)\|(LaunchArgvGuardTests*)\|(LinuxCgroupContainmentTests*)\|(ModernConPtyCommandLineTests*)\|(ProviderOracleGateTests*)\|(PtyAgentRunnerTests*)\|(PtyBackendPolicyTests*)\|(PtyCustodyTests*)\|(PtyInputChunkingTests*)\|(PtyInputEncodingTests*)\|(RingBufferTests*)\|(StdinClipModelTests*)\|(SubmitEvidenceTests*)\|(TerminalScreenTests*)\|(UnixPtyArgvTests*)/*` — `agents-pty-all-group-1-execution/log_261025014523900.diag:10`
- `observability-all`: `(none)` — `observability-all-execution/log_261025040946131.diag:10`
- `e2e-all-group-1`: `/*/Antiphon.E2E/(AgentE2ETests*)\|(AgentTaskLandDeliveryE2ETests*)\|(BoardE2ETests*)\|(CardCliE2ETests*)\|(ChannelE2ETests*)\|(ChromiumRuntimeSmokeTests*)\|(ContractSnapshotTests*)\|(CreatedResourceTests*)\|(DirectoryAutocompleteE2ETests*)\|(DispatchBaseWarningDeliveryE2ETests*)\|(DispatchConcurrencySettingsE2ETests*)\|(LandDeliveryFixtureProbeTests*)\|(OrchestratorE2ETests*)\|(OutputDistillationCanaryGuardTests*)\|(PipelineGlanceE2ETests*)\|(ProjectDeleteE2ETests*)\|(ReleaseGateIsolationTests*)\|(ReleaseGateReportDeliveryTests*)\|(ScheduleCliE2ETests*)\|(SmokeE2ETests*)\|(WorkflowDeleteTests*)\|(WorkflowOutputTests*)\|(WorktreeRetirementDeliveryE2ETests*)/*` — `e2e-all-group-1-execution/log_261025020612209.diag:10`

## Per-class timing ledger

Complete ledger for the 80 available non-antiphon TRXs, including successful classes and the two available E2E groups. Project is the namespace prefix of the fully qualified class. Citations point at the first class result; class wall and sum cover every result belonging to that class in that chunk. No class-wall claim is made for E2E group 1.

### session-runner class timings

| Chunk / first row | Full test class | P/F/N | Class wall s | Sum test s |
| --- | --- | --- | --- | --- |
| `session-runner-all-group-1.trx:8` | `Antiphon.SessionRunner.Tests.CodexWindowsLaunchPolicyTests` | 26/0/0 | 0.261 | 1.165 |
| `session-runner-all-group-1.trx:130` | `Antiphon.SessionRunner.Tests.CompletedCardWorkspaceRemovalTests` | 89/5/3 | 994.815 | 546.779 |
| `session-runner-all-group-1.trx:131` | `Antiphon.SessionRunner.Tests.FakeHerdrServerListenerTests` | 20/0/0 | 1.623 | 1.616 |
| `session-runner-all-group-1.trx:42` | `Antiphon.SessionRunner.Tests.GrokRulesArgvPolicyTests` | 26/0/0 | 0.011 | 0.014 |
| `session-runner-all-group-1.trx:182` | `Antiphon.SessionRunner.Tests.GrokRulesFileLaunchTests` | 21/0/0 | 7.936 | 0.568 |
| `session-runner-all-group-1.trx:141` | `Antiphon.SessionRunner.Tests.GrokTranscriptTailerTests` | 32/0/0 | 4.164 | 5.382 |
| `session-runner-all-group-1.trx:1215` | `Antiphon.SessionRunner.Tests.HerdrAdoptionSweepTests` | 22/0/0 | 14.055 | 14.047 |
| `session-runner-all-group-1.trx:1237` | `Antiphon.SessionRunner.Tests.HerdrAttachTests` | 23/0/0 | 3.677 | 3.672 |
| `session-runner-all-group-1.trx:207` | `Antiphon.SessionRunner.Tests.HerdrLabelFollowSchedulingTests` | 25/0/0 | 8.593 | 4.293 |
| `session-runner-all-group-1.trx:208` | `Antiphon.SessionRunner.Tests.HerdrLabelObservationTests` | 71/0/0 | 0.975 | 7.526 |
| `session-runner-all-group-1.trx:305` | `Antiphon.SessionRunner.Tests.HerdrLabelSnapshotTests` | 36/0/0 | 13.416 | 5.576 |
| `session-runner-all-group-1.trx:71` | `Antiphon.SessionRunner.Tests.HerdrLaunchShapeTests` | 37/0/0 | 6.190 | 6.168 |
| `session-runner-all-group-1.trx:70` | `Antiphon.SessionRunner.Tests.HerdrNamedTabPlacementTests` | 23/0/0 | 5.778 | 5.766 |
| `session-runner-all-group-1.trx:311` | `Antiphon.SessionRunner.Tests.HerdrPaneDisposalConcurrencyTests` | 27/0/0 | 1.861 | 5.803 |
| `session-runner-all-group-1.trx:337` | `Antiphon.SessionRunner.Tests.HerdrPaneDisposalIdentityTests` | 35/0/0 | 0.075 | 0.109 |
| `session-runner-all-group-1.trx:378` | `Antiphon.SessionRunner.Tests.HerdrPaneDisposalRedactionTests` | 103/0/0 | 5.689 | 70.416 |
| `session-runner-all-group-1.trx:512` | `Antiphon.SessionRunner.Tests.HerdrPaneDisposalServiceTests` | 92/0/0 | 19.737 | 8.934 |
| `session-runner-all-group-1.trx:1260` | `Antiphon.SessionRunner.Tests.LegacyWorkspaceRemovalGateTests` | 1/10/0 | 59.664 | 59.657 |
| `session-runner-all-group-1.trx:584` | `Antiphon.SessionRunner.Tests.PhoneHomeCommandDispatcherTests` | 43/0/0 | 0.231 | 0.417 |
| `session-runner-all-group-1.trx:628` | `Antiphon.SessionRunner.Tests.PromptSubmissionMatchTests` | 31/0/0 | 0.058 | 0.110 |
| `session-runner-all-group-1.trx:660` | `Antiphon.SessionRunner.Tests.RepositoryCloneSourceTests` | 21/0/0 | 0.004 | 0.007 |
| `session-runner-all-group-1.trx:1545` | `Antiphon.SessionRunner.Tests.RunnerAbsenceEvidenceRuntimeTests` | 22/0/0 | 3.150 | 3.138 |
| `session-runner-all-group-1.trx:683` | `Antiphon.SessionRunner.Tests.RunnerAbsenceEvidenceTests` | 81/0/0 | 9.148 | 18.292 |
| `session-runner-all-group-1.trx:798` | `Antiphon.SessionRunner.Tests.RunnerCustodyTests` | 23/0/0 | 59.085 | 59.077 |
| `session-runner-all-group-1.trx:836` | `Antiphon.SessionRunner.Tests.RunnerRestartHealthTests` | 55/0/0 | 150.582 | 138.307 |
| `session-runner-all-group-1.trx:1567` | `Antiphon.SessionRunner.Tests.RunnerRestartPreflightCacheTests` | 26/0/0 | 1.084 | 1.073 |
| `session-runner-all-group-1.trx:895` | `Antiphon.SessionRunner.Tests.RunnerRestartPreflightSafetyTests` | 31/0/0 | 90.546 | 85.391 |
| `session-runner-all-group-1.trx:928` | `Antiphon.SessionRunner.Tests.RunnerWorkspaceServiceTests` | 35/0/0 | 75.999 | 72.711 |
| `session-runner-all-group-1.trx:194` | `Antiphon.SessionRunner.Tests.TerminalSeatReleaseTests` | 38/0/0 | 81.159 | 81.119 |
| `session-runner-all-group-1.trx:857` | `Antiphon.SessionRunner.Tests.TranscriptAdoptionSafetyTests` | 44/0/0 | 447.503 | 69.761 |
| `session-runner-all-group-2.trx:76` | `Antiphon.SessionRunner.Tests.AgentPinPublicationTests` | 0/6/0 | 10.706 | 10.656 |
| `session-runner-all-group-2.trx:8` | `Antiphon.SessionRunner.Tests.BuildSlotBrokerTests` | 13/0/0 | 0.080 | 0.391 |
| `session-runner-all-group-2.trx:22` | `Antiphon.SessionRunner.Tests.ClaudeAuthProbeTests` | 8/0/0 | 506.298 | 2.574 |
| `session-runner-all-group-2.trx:21` | `Antiphon.SessionRunner.Tests.CodexAuthProbeTests` | 9/0/0 | 0.060 | 0.096 |
| `session-runner-all-group-2.trx:320` | `Antiphon.SessionRunner.Tests.CodexCliVersionProbeTests` | 8/0/0 | 505.021 | 151.727 |
| `session-runner-all-group-2.trx:35` | `Antiphon.SessionRunner.Tests.CodexQuotaEvidenceTests` | 8/0/0 | 0.044 | 0.207 |
| `session-runner-all-group-2.trx:39` | `Antiphon.SessionRunner.Tests.CodexTranscriptNormalizerTests` | 19/0/0 | 0.090 | 0.455 |
| `session-runner-all-group-2.trx:61` | `Antiphon.SessionRunner.Tests.CodexTranscriptTailerTests` | 17/0/0 | 5.212 | 17.832 |
| `session-runner-all-group-2.trx:64` | `Antiphon.SessionRunner.Tests.CpuSpinDetectorTests` | 7/0/0 | 0.023 | 0.017 |
| `session-runner-all-group-2.trx:403` | `Antiphon.SessionRunner.Tests.DaemonStartupDiagnosticsTests` | 2/6/0 | 281.939 | 281.916 |
| `session-runner-all-group-2.trx:144` | `Antiphon.SessionRunner.Tests.GrokRulesFileStoreTests` | 11/0/0 | 0.109 | 0.164 |
| `session-runner-all-group-2.trx:317` | `Antiphon.SessionRunner.Tests.GrokRulesRunnerRefusalTests` | 11/0/0 | 495.327 | 0.243 |
| `session-runner-all-group-2.trx:211` | `Antiphon.SessionRunner.Tests.HerdrClientTests` | 16/0/0 | 0.286 | 0.602 |
| `session-runner-all-group-2.trx:225` | `Antiphon.SessionRunner.Tests.HerdrGkpLaunchGuardTests` | 10/0/0 | 0.016 | 0.012 |
| `session-runner-all-group-2.trx:236` | `Antiphon.SessionRunner.Tests.HerdrGrokResumeGuardTests` | 7/0/0 | 0.037 | 0.054 |
| `session-runner-all-group-2.trx:243` | `Antiphon.SessionRunner.Tests.HerdrNamedTabResolverTests` | 7/0/0 | 0.009 | 0.011 |
| `session-runner-all-group-2.trx:318` | `Antiphon.SessionRunner.Tests.HerdrPaneDisposalStopRegressionTests` | 13/0/0 | 495.638 | 5.095 |
| `session-runner-all-group-2.trx:250` | `Antiphon.SessionRunner.Tests.HerdrPaneSidecarTests` | 7/0/0 | 0.137 | 0.242 |
| `session-runner-all-group-2.trx:256` | `Antiphon.SessionRunner.Tests.HerdrPlacementCheckRouteTests` | 5/0/0 | 0.181 | 0.247 |
| `session-runner-all-group-2.trx:221` | `Antiphon.SessionRunner.Tests.HerdrRunnerSessionTests` | 7/0/0 | 5.441 | 5.429 |
| `session-runner-all-group-2.trx:483` | `Antiphon.SessionRunner.Tests.HerdrTransportTests` | 19/0/0 | 3.959 | 2.867 |
| `session-runner-all-group-2.trx:322` | `Antiphon.SessionRunner.Tests.HostStatsProbeParseTests` | 11/0/0 | 0.262 | 0.257 |
| `session-runner-all-group-2.trx:333` | `Antiphon.SessionRunner.Tests.HostStatsStoreTests` | 9/0/0 | 0.494 | 0.491 |
| `session-runner-all-group-2.trx:342` | `Antiphon.SessionRunner.Tests.PhoneHomeConnectionServiceTests` | 17/0/0 | 4.649 | 4.642 |
| `session-runner-all-group-2.trx:359` | `Antiphon.SessionRunner.Tests.RunnerAbsenceEvidenceContractTests` | 10/0/0 | 4.194 | 4.190 |
| `session-runner-all-group-2.trx:369` | `Antiphon.SessionRunner.Tests.RunnerCapabilitiesTests` | 6/1/0 | 0.046 | 0.044 |
| `session-runner-all-group-2.trx:519` | `Antiphon.SessionRunner.Tests.RunnerRestartScriptCompatibilityTests` | 8/0/0 | 27.111 | 27.106 |
| `session-runner-all-group-2.trx:527` | `Antiphon.SessionRunner.Tests.RunnerWorkspaceBindingTests` | 19/0/1 | 36.471 | 36.464 |
| `session-runner-all-group-2.trx:500` | `Antiphon.SessionRunner.Tests.TranscriptWorkingStateTests` | 15/0/0 | 0.004 | 0.007 |
| `session-runner-all-group-2.trx:551` | `Antiphon.SessionRunner.Tests.UnixPtyArgvAdmissionTests` | 0/15/0 | 0.193 | 0.017 |
| `session-runner-all-group-3.trx:72` | `Antiphon.SessionRunner.Tests.BuildSlotEndToEndTests` | 2/0/0 | 18.414 | 18.413 |
| `session-runner-all-group-3.trx:24` | `Antiphon.SessionRunner.Tests.BuildSlotEndpointTests` | 6/0/0 | 19.650 | 3.807 |
| `session-runner-all-group-3.trx:8` | `Antiphon.SessionRunner.Tests.BuildSlotMetricsTests` | 5/0/0 | 0.636 | 0.718 |
| `session-runner-all-group-3.trx:193` | `Antiphon.SessionRunner.Tests.C1022BackendCapabilitiesTests` | 5/0/0 | 0.266 | 0.266 |
| `session-runner-all-group-3.trx:77` | `Antiphon.SessionRunner.Tests.CodexCliVersionWindowsTests` | 4/0/0 | 20.831 | 20.830 |
| `session-runner-all-group-3.trx:75` | `Antiphon.SessionRunner.Tests.CompactionContinuationStopTests` | 6/0/0 | 41.631 | 11.309 |
| `session-runner-all-group-3.trx:12` | `Antiphon.SessionRunner.Tests.CompletedCardWorkspaceContractTests` | 4/0/0 | 0.261 | 0.332 |
| `session-runner-all-group-3.trx:81` | `Antiphon.SessionRunner.Tests.DaemonLogRotationTests` | 4/0/0 | 4.886 | 4.884 |
| `session-runner-all-group-3.trx:85` | `Antiphon.SessionRunner.Tests.GrokLaunchArgsValidationTests` | 6/0/0 | 0.013 | 0.011 |
| `session-runner-all-group-3.trx:76` | `Antiphon.SessionRunner.Tests.GrokRulesAdoptionTests` | 4/0/0 | 36.108 | 4.121 |
| `session-runner-all-group-3.trx:91` | `Antiphon.SessionRunner.Tests.GrokRulesStoreFailureTests` | 6/0/0 | 0.276 | 0.273 |
| `session-runner-all-group-3.trx:15` | `Antiphon.SessionRunner.Tests.HerdrEventPumpTests` | 5/0/0 | 0.542 | 0.772 |
| `session-runner-all-group-3.trx:19` | `Antiphon.SessionRunner.Tests.HerdrPaneAllocatorTests` | 5/0/0 | 0.010 | 0.009 |
| `session-runner-all-group-3.trx:104` | `Antiphon.SessionRunner.Tests.HerdrPaneChildKillTests` | 6/0/0 | 4.146 | 2.106 |
| `session-runner-all-group-3.trx:68` | `Antiphon.SessionRunner.Tests.HerdrStatusPushTests` | 4/0/0 | 2.835 | 2.830 |
| `session-runner-all-group-3.trx:26` | `Antiphon.SessionRunner.Tests.HostStatsEndpointTests` | 4/0/0 | 0.366 | 0.563 |
| `session-runner-all-group-3.trx:28` | `Antiphon.SessionRunner.Tests.HostStatsSamplerTests` | 5/0/0 | 0.078 | 0.085 |
| `session-runner-all-group-3.trx:34` | `Antiphon.SessionRunner.Tests.LinuxCustodyProbeTests` | 6/0/0 | 0.005 | 0.004 |
| `session-runner-all-group-3.trx:111` | `Antiphon.SessionRunner.Tests.PtyHostAdoptionTests` | 5/1/0 | 27.959 | 27.946 |
| `session-runner-all-group-3.trx:42` | `Antiphon.SessionRunner.Tests.PushProbeOutcomeTests` | 5/0/0 | 0.006 | 0.016 |
| `session-runner-all-group-3.trx:149` | `Antiphon.SessionRunner.Tests.RemoteControlConditionalInputTests` | 4/0/0 | 10.301 | 10.300 |
| `session-runner-all-group-3.trx:50` | `Antiphon.SessionRunner.Tests.RunnerCustodyLedgerBackendTests` | 5/0/0 | 0.207 | 0.688 |
| `session-runner-all-group-3.trx:153` | `Antiphon.SessionRunner.Tests.RunnerMultilinePromptDeliveryTests` | 2/0/0 | 2.154 | 2.154 |
| `session-runner-all-group-3.trx:167` | `Antiphon.SessionRunner.Tests.RunnerSessionGenerationTests` | 5/0/0 | 13.794 | 13.793 |
| `session-runner-all-group-3.trx:62` | `Antiphon.SessionRunner.Tests.RunnerStartupDiagnosticsTests` | 5/0/0 | 0.314 | 0.990 |
| `session-runner-all-group-3.trx:179` | `Antiphon.SessionRunner.Tests.SessionLivenessTests` | 6/0/0 | 8.498 | 8.497 |
| `session-runner-all-group-3.trx:54` | `Antiphon.SessionRunner.Tests.TranscriptClaimRegistryTests` | 6/0/0 | 0.015 | 0.014 |
| `session-runner-all-group-3.trx:113` | `Antiphon.SessionRunner.Tests.TranscriptIngestionLatencyProbeTests` | 4/0/0 | 57.519 | 57.515 |
| `session-runner-all-group-3.trx:178` | `Antiphon.SessionRunner.Tests.TranscriptTailerCompactionTests` | 4/0/0 | 4.160 | 4.159 |
| `session-runner-all-group-3.trx:186` | `Antiphon.SessionRunner.Tests.TranscriptTailerObservationTests` | 5/0/0 | 0.991 | 0.990 |
| `session-runner-all-group-4.trx:9` | `Antiphon.SessionRunner.Tests.AgentPinWorkspaceStoreTests` | 0/3/0 | 0.513 | 0.661 |
| `session-runner-all-group-4.trx:130` | `Antiphon.SessionRunner.Tests.BlockedParkWireTests` | 3/0/0 | 694.425 | 539.473 |
| `session-runner-all-group-4.trx:8` | `Antiphon.SessionRunner.Tests.BuildSlotSettingsTests` | 2/0/0 | 0.105 | 0.187 |
| `session-runner-all-group-4.trx:211` | `Antiphon.SessionRunner.Tests.C1022BackendLaunchTests` | 3/0/0 | 6.044 | 6.042 |
| `session-runner-all-group-4.trx:110` | `Antiphon.SessionRunner.Tests.ClaudeDialogInputGuardTests` | 2/0/0 | 0.598 | 1.144 |
| `session-runner-all-group-4.trx:140` | `Antiphon.SessionRunner.Tests.CodexCommandLengthHttpAcceptanceTests` | 1/0/0 | 1.755 | 1.755 |
| `session-runner-all-group-4.trx:131` | `Antiphon.SessionRunner.Tests.CodexLaunchRefusalTests` | 3/0/0 | 0.046 | 0.044 |
| `session-runner-all-group-4.trx:115` | `Antiphon.SessionRunner.Tests.CodexProviderAuthRoutingTests` | 1/0/0 | 0.508 | 0.508 |
| `session-runner-all-group-4.trx:141` | `Antiphon.SessionRunner.Tests.FirstWriteRaceTests` | 1/0/0 | 1.557 | 1.557 |
| `session-runner-all-group-4.trx:60` | `Antiphon.SessionRunner.Tests.GrokAuthProbeTests` | 3/0/0 | 0.186 | 0.204 |
| `session-runner-all-group-4.trx:143` | `Antiphon.SessionRunner.Tests.GrokRulesHttpAcceptanceTests` | 1/0/0 | 7.344 | 7.344 |
| `session-runner-all-group-4.trx:61` | `Antiphon.SessionRunner.Tests.HerdrClientSurfaceTests` | 3/0/0 | 0.576 | 0.911 |
| `session-runner-all-group-4.trx:63` | `Antiphon.SessionRunner.Tests.HerdrPaneDisposalEligibilityTests` | 3/0/0 | 0.031 | 0.043 |
| `session-runner-all-group-4.trx:127` | `Antiphon.SessionRunner.Tests.HerdrSnapshotBoundedReadTests` | 1/0/0 | 0.863 | 0.863 |
| `session-runner-all-group-4.trx:66` | `Antiphon.SessionRunner.Tests.HostStatsProbeLiveTests` | 1/0/1 | 1.137 | 1.143 |
| `session-runner-all-group-4.trx:71` | `Antiphon.SessionRunner.Tests.ProcessSpawnLimitTests` | 2/0/0 | 0.265 | 0.264 |
| `session-runner-all-group-4.trx:274` | `Antiphon.SessionRunner.Tests.PtyBackendSeamTests` | 3/0/0 | 5.607 | 5.605 |
| `session-runner-all-group-4.trx:74` | `Antiphon.SessionRunner.Tests.PtyHostDeploymentGuardTests` | 3/0/0 | 0.314 | 0.416 |
| `session-runner-all-group-4.trx:75` | `Antiphon.SessionRunner.Tests.PushCredentialPolicyFileTests` | 3/0/0 | 0.056 | 0.054 |
| `session-runner-all-group-4.trx:122` | `Antiphon.SessionRunner.Tests.RunnerAbsenceEvidencePhoneHomeTests` | 3/0/0 | 0.338 | 0.904 |
| `session-runner-all-group-4.trx:113` | `Antiphon.SessionRunner.Tests.RunnerCapacityCountTests` | 3/0/0 | 2.266 | 2.554 |
| `session-runner-all-group-4.trx:134` | `Antiphon.SessionRunner.Tests.RunnerCustodyCrashTests` | 2/0/0 | 10.988 | 10.988 |
| `session-runner-all-group-4.trx:116` | `Antiphon.SessionRunner.Tests.RunnerPlatformContractTests` | 3/0/0 | 0.034 | 0.033 |
| `session-runner-all-group-4.trx:144` | `Antiphon.SessionRunner.Tests.RunnerPlatformLaunchTests` | 3/0/0 | 289.405 | 2.922 |
| `session-runner-all-group-4.trx:150` | `Antiphon.SessionRunner.Tests.RunnerStartupReadinessTests` | 2/0/0 | 25.571 | 22.975 |
| `session-runner-all-group-4.trx:208` | `Antiphon.SessionRunner.Tests.SessionBufferBoundsTests` | 2/0/0 | 315.102 | 7.426 |
| `session-runner-all-group-4.trx:147` | `Antiphon.SessionRunner.Tests.SessionCpuWatchdogTests` | 3/0/0 | 16.821 | 6.134 |
| `session-runner-all-group-4.trx:119` | `Antiphon.SessionRunner.Tests.SessionRunnerEventHubTests` | 3/0/0 | 0.353 | 0.404 |
| `session-runner-all-group-4.trx:136` | `Antiphon.SessionRunner.Tests.WorkspaceParkPublicationTests` | 4/0/0 | 133.246 | 133.245 |
| `session-runner-all-group-4.trx:153` | `Antiphon.SessionRunner.Tests.WorkspaceSourceVerificationWireTests` | 1/1/0 | 317.033 | 311.653 |
| `session-runner-all-group-5.trx:9` | `Antiphon.SessionRunner.Tests.PtyBackendEnvGuardTests` | 1/0/0 | 0.011 | 0.011 |
| `session-runner-all-group-5.trx:8` | `Antiphon.SessionRunner.Tests.PtyHostLeakSweepTests` | 1/0/0 | 0.007 | 0.007 |
| `session-runner-all-group-5.trx:10` | `Antiphon.SessionRunner.Tests.RunnerChildClaudeTokenInheritanceTests` | 1/0/0 | 1.272 | 1.272 |
| `session-runner-all-group-6.trx:8` | `Antiphon.TestSupport.TestClassificationGuardTests` | 1/0/0 | 0.128 | 0.128 |

### agents-pty class timings

| Chunk / first row | Full test class | P/F/N | Class wall s | Sum test s |
| --- | --- | --- | --- | --- |
| `agents-pty-all-group-1.trx:8` | `Antiphon.Agents.Pty.Tests.AnsiStripperTests` | 8/0/0 | 0.061 | 0.221 |
| `agents-pty-all-group-1.trx:16` | `Antiphon.Agents.Pty.Tests.ClaudeEffortDismissalReplayTests` | 39/0/0 | 11.990 | 28.287 |
| `agents-pty-all-group-1.trx:46` | `Antiphon.Agents.Pty.Tests.ClaudeEffortPromptTests` | 64/0/0 | 8.225 | 76.568 |
| `agents-pty-all-group-1.trx:210` | `Antiphon.Agents.Pty.Tests.ClaudeProjectTrustTests` | 7/0/0 | 0.120 | 0.286 |
| `agents-pty-all-group-1.trx:226` | `Antiphon.Agents.Pty.Tests.ClaudeScreenTests` | 9/0/0 | 0.003 | 0.003 |
| `agents-pty-all-group-1.trx:240` | `Antiphon.Agents.Pty.Tests.ClaudeStartupReadinessTests` | 17/0/0 | 7.416 | 37.575 |
| `agents-pty-all-group-1.trx:288` | `Antiphon.Agents.Pty.Tests.ClaudeStartupTrustPromptTests` | 16/0/0 | 4.903 | 6.891 |
| `agents-pty-all-group-1.trx:314` | `Antiphon.Agents.Pty.Tests.CodexReadyWaitTests` | 19/0/0 | 2.554 | 5.768 |
| `agents-pty-all-group-1.trx:350` | `Antiphon.Agents.Pty.Tests.CodexStartupReadinessTests` | 31/0/0 | 0.016 | 0.028 |
| `agents-pty-all-group-1.trx:381` | `Antiphon.Agents.Pty.Tests.CodexV0156StartupPromptTests` | 8/0/0 | 0.057 | 0.104 |
| `agents-pty-all-group-1.trx:387` | `Antiphon.Agents.Pty.Tests.ComposerDeliveryEvidenceTests` | 24/0/0 | 0.022 | 0.023 |
| `agents-pty-all-group-1.trx:413` | `Antiphon.Agents.Pty.Tests.ComposerInputProbeTests` | 15/0/0 | 1.049 | 3.957 |
| `agents-pty-all-group-1.trx:436` | `Antiphon.Agents.Pty.Tests.ConPtyEnvironmentIsolationGuardTests` | 6/0/0 | 1.297 | 1.313 |
| `agents-pty-all-group-1.trx:442` | `Antiphon.Agents.Pty.Tests.Da1StartupResponderTests` | 22/0/0 | 0.013 | 0.006 |
| `agents-pty-all-group-1.trx:238` | `Antiphon.Agents.Pty.Tests.FakeClaudeContractTests` | 47/2/0 | 87.767 | 67.106 |
| `agents-pty-all-group-1.trx:1095` | `Antiphon.Agents.Pty.Tests.FakeGrokContractTests` | 19/0/0 | 10.637 | 10.633 |
| `agents-pty-all-group-1.trx:465` | `Antiphon.Agents.Pty.Tests.LaunchArgvGuardTests` | 10/0/0 | 0.027 | 0.028 |
| `agents-pty-all-group-1.trx:474` | `Antiphon.Agents.Pty.Tests.LinuxCgroupContainmentTests` | 7/0/0 | 0.006 | 0.011 |
| `agents-pty-all-group-1.trx:495` | `Antiphon.Agents.Pty.Tests.ModernConPtyCommandLineTests` | 7/0/0 | 0.001 | 0.002 |
| `agents-pty-all-group-1.trx:502` | `Antiphon.Agents.Pty.Tests.ProviderOracleGateTests` | 7/0/0 | 0.002 | 0.005 |
| `agents-pty-all-group-1.trx:1114` | `Antiphon.Agents.Pty.Tests.PtyAgentRunnerTests` | 27/0/0 | 20.835 | 20.829 |
| `agents-pty-all-group-1.trx:509` | `Antiphon.Agents.Pty.Tests.PtyBackendPolicyTests` | 8/0/0 | 0.052 | 0.057 |
| `agents-pty-all-group-1.trx:590` | `Antiphon.Agents.Pty.Tests.PtyCustodyTests` | 30/0/0 | 21.680 | 20.584 |
| `agents-pty-all-group-1.trx:1141` | `Antiphon.Agents.Pty.Tests.PtyInputChunkingTests` | 6/0/0 | 3.451 | 3.450 |
| `agents-pty-all-group-1.trx:619` | `Antiphon.Agents.Pty.Tests.PtyInputEncodingTests` | 6/0/0 | 0.004 | 0.002 |
| `agents-pty-all-group-1.trx:625` | `Antiphon.Agents.Pty.Tests.RingBufferTests` | 8/0/0 | 1.013 | 1.015 |
| `agents-pty-all-group-1.trx:638` | `Antiphon.Agents.Pty.Tests.StdinClipModelTests` | 13/0/0 | 0.087 | 0.064 |
| `agents-pty-all-group-1.trx:651` | `Antiphon.Agents.Pty.Tests.SubmitEvidenceTests` | 9/0/0 | 0.010 | 0.007 |
| `agents-pty-all-group-1.trx:660` | `Antiphon.Agents.Pty.Tests.TerminalScreenTests` | 62/0/0 | 1.113 | 1.186 |
| `agents-pty-all-group-1.trx:769` | `Antiphon.Agents.Pty.Tests.UnixPtyArgvTests` | 0/0/17 | 0.033 | 0.027 |
| `agents-pty-all-group-2.trx:64` | `Antiphon.Agents.Pty.Tests.C1022TypedInputTests` | 3/0/0 | 21.189 | 2.225 |
| `agents-pty-all-group-2.trx:59` | `Antiphon.Agents.Pty.Tests.ClaudeDetectorsTests` | 5/0/0 | 16.254 | 16.251 |
| `agents-pty-all-group-2.trx:8` | `Antiphon.Agents.Pty.Tests.ClaudeDialogObservationTests` | 1/0/0 | 0.025 | 0.025 |
| `agents-pty-all-group-2.trx:9` | `Antiphon.Agents.Pty.Tests.ClaudeGoldenReplayTests` | 4/0/0 | 0.015 | 0.035 |
| `agents-pty-all-group-2.trx:78` | `Antiphon.Agents.Pty.Tests.ClaudeSubmitContractTests` | 2/0/0 | 3.704 | 3.704 |
| `agents-pty-all-group-2.trx:13` | `Antiphon.Agents.Pty.Tests.CodexMcpBootTests` | 1/0/0 | 0.004 | 0.004 |
| `agents-pty-all-group-2.trx:10` | `Antiphon.Agents.Pty.Tests.CodexReadyTrackerTests` | 4/0/0 | 0.048 | 0.074 |
| `agents-pty-all-group-2.trx:22` | `Antiphon.Agents.Pty.Tests.CodexV0160FixtureTests` | 3/0/0 | 0.022 | 0.054 |
| `agents-pty-all-group-2.trx:17` | `Antiphon.Agents.Pty.Tests.CodexV0160StartupTests` | 4/0/0 | 0.009 | 0.022 |
| `agents-pty-all-group-2.trx:24` | `Antiphon.Agents.Pty.Tests.CommandLineLengthTests` | 1/0/0 | 0.009 | 0.009 |
| `agents-pty-all-group-2.trx:23` | `Antiphon.Agents.Pty.Tests.CxSessionTestHomeTests` | 1/0/0 | 0.008 | 0.008 |
| `agents-pty-all-group-2.trx:49` | `Antiphon.Agents.Pty.Tests.EchoGatedLineSenderTests` | 5/0/0 | 0.130 | 0.414 |
| `agents-pty-all-group-2.trx:25` | `Antiphon.Agents.Pty.Tests.GkSessionFollowUpBehaviorTests` | 3/0/0 | 0.002 | 0.001 |
| `agents-pty-all-group-2.trx:35` | `Antiphon.Agents.Pty.Tests.GrokNativeSessionStoreTests` | 2/0/0 | 0.015 | 0.027 |
| `agents-pty-all-group-2.trx:80` | `Antiphon.Agents.Pty.Tests.ModernPtyDa1Tests` | 4/0/0 | 3.469 | 3.467 |
| `agents-pty-all-group-2.trx:29` | `Antiphon.Agents.Pty.Tests.ProcessSpawnLimitTests` | 2/0/0 | 0.003 | 0.003 |
| `agents-pty-all-group-2.trx:84` | `Antiphon.Agents.Pty.Tests.PtyBackendContractTests` | 4/0/0 | 1.554 | 1.552 |
| `agents-pty-all-group-2.trx:32` | `Antiphon.Agents.Pty.Tests.PtyBackendEnvGuardTests` | 1/0/0 | 0.002 | 0.002 |
| `agents-pty-all-group-2.trx:88` | `Antiphon.Agents.Pty.Tests.PtyBracketedPasteContractTests` | 2/0/0 | 1.790 | 1.789 |
| `agents-pty-all-group-2.trx:65` | `Antiphon.Agents.Pty.Tests.PtyKillProcessTreeTests` | 5/0/0 | 3.648 | 3.646 |
| `agents-pty-all-group-2.trx:90` | `Antiphon.Agents.Pty.Tests.PtyLargeWriteTests` | 4/0/0 | 1.949 | 1.947 |
| `agents-pty-all-group-2.trx:33` | `Antiphon.Agents.Pty.Tests.PtySessionAuditTests` | 4/0/0 | 0.036 | 0.056 |
| `agents-pty-all-group-2.trx:70` | `Antiphon.Agents.Pty.Tests.PtyStressTests` | 3/0/0 | 8.755 | 8.754 |
| `agents-pty-all-group-2.trx:45` | `Antiphon.Agents.Pty.Tests.PtyUtf8DecoderTests` | 3/0/0 | 0.031 | 0.076 |
| `agents-pty-all-group-2.trx:37` | `Antiphon.Agents.Pty.Tests.TempBatchTests` | 2/0/0 | 0.019 | 0.018 |
| `agents-pty-all-group-2.trx:51` | `Antiphon.Agents.Pty.Tests.VerifiedPromptSubmitterTests` | 5/0/0 | 0.709 | 1.369 |
| `agents-pty-all-group-2.trx:39` | `Antiphon.Agents.Pty.Tests.VisiblePtyOutputTests` | 5/0/0 | 0.004 | 0.003 |
| `agents-pty-all-group-2.trx:73` | `Antiphon.Agents.Pty.Tests.WaitForQuietAfterVisibleTests` | 3/0/0 | 6.554 | 6.553 |
| `agents-pty-all-group-2.trx:94` | `Antiphon.Agents.Pty.Tests.WindowsPtyArgvNativeTests` | 2/0/0 | 0.221 | 0.221 |
| `agents-pty-all-group-3.trx:8` | `Antiphon.TestSupport.TestClassificationGuardTests` | 1/0/0 | 0.078 | 0.078 |
| `agents-pty-all-profile-13.trx:8` | `Antiphon.Agents.Pty.Tests.ClaudeVerifiedDeliveryTests` | 1/0/0 | 0.920 | 0.920 |
| `agents-pty-all-profile-14.trx:8` | `Antiphon.Agents.Pty.Tests.ClaudeVerifiedDeliveryTests` | 1/0/0 | 0.641 | 0.641 |
| `agents-pty-all-profile-49.trx:8` | `Antiphon.Agents.Pty.Tests.PtyPasteMarkerExperiments` | 1/0/0 | 0.319 | 0.319 |
| `agents-pty-all-profile-50.trx:8` | `Antiphon.Agents.Pty.Tests.PtyPasteMarkerExperiments` | 1/0/0 | 5.599 | 5.599 |
| `agents-pty-all-profile-51.trx:8` | `Antiphon.Agents.Pty.Tests.PtyPasteMarkerExperiments` | 1/0/0 | 1.610 | 1.610 |
| `agents-pty-all-profile-52.trx:8` | `Antiphon.Agents.Pty.Tests.PtyPasteMarkerExperiments` | 1/0/0 | 1.816 | 1.816 |

### pty-host class timings

| Chunk / first row | Full test class | P/F/N | Class wall s | Sum test s |
| --- | --- | --- | --- | --- |
| `pty-host-all-group-1.trx:46` | `Antiphon.PtyHost.Tests.CustodyReceiptBackendTests` | 5/0/0 | 1.024 | 3.702 |
| `pty-host-all-group-1.trx:8` | `Antiphon.PtyHost.Tests.FramingTests` | 4/0/0 | 0.628 | 0.674 |
| `pty-host-all-group-1.trx:74` | `Antiphon.PtyHost.Tests.HostCustodyTests` | 17/0/0 | 14.767 | 12.108 |
| `pty-host-all-group-1.trx:98` | `Antiphon.PtyHost.Tests.HostSessionPipeTests` | 9/0/0 | 12.528 | 12.525 |
| `pty-host-all-group-1.trx:10` | `Antiphon.PtyHost.Tests.LinuxCgroupCustodyTests` | 0/0/8 | 2.472 | 0.443 |
| `pty-host-all-group-1.trx:80` | `Antiphon.PtyHost.Tests.ParentDeathSpikeTests` | 1/0/0 | 3.243 | 3.243 |
| `pty-host-all-group-1.trx:50` | `Antiphon.PtyHost.Tests.PtyBackendEnvGuardTests` | 1/0/0 | 0.004 | 0.004 |
| `pty-host-all-group-1.trx:81` | `Antiphon.PtyHost.Tests.PtyHostLauncherTests` | 7/0/0 | 40.350 | 17.710 |
| `pty-host-all-group-1.trx:49` | `Antiphon.PtyHost.Tests.PtyHostPipeNameTests` | 1/0/0 | 0.001 | 0.001 |
| `pty-host-all-group-1.trx:51` | `Antiphon.PtyHost.Tests.VerificationCustodyStoreTests` | 20/0/0 | 0.578 | 1.788 |
| `pty-host-all-group-2.trx:8` | `Antiphon.TestSupport.TestClassificationGuardTests` | 1/0/0 | 0.020 | 0.020 |
| `pty-host-all-profile-1.trx:8` | `Antiphon.PtyHost.Tests.Card0490NativeExecutionTests` | 1/0/0 | 0.008 | 0.008 |
| `pty-host-all-profile-10.trx:8` | `Antiphon.PtyHost.Tests.Card0490NativeExecutionTests` | 1/0/0 | 0.006 | 0.006 |
| `pty-host-all-profile-11.trx:8` | `Antiphon.PtyHost.Tests.Card0490NativeExecutionTests` | 1/0/0 | 0.005 | 0.005 |
| `pty-host-all-profile-12.trx:8` | `Antiphon.PtyHost.Tests.Card0490NativeExecutionTests` | 1/0/0 | 0.014 | 0.014 |
| `pty-host-all-profile-13.trx:8` | `Antiphon.PtyHost.Tests.Card0490NativeExecutionTests` | 1/0/0 | 0.006 | 0.006 |
| `pty-host-all-profile-14.trx:8` | `Antiphon.PtyHost.Tests.Card0490NativeExecutionTests` | 1/0/0 | 0.005 | 0.005 |
| `pty-host-all-profile-15.trx:8` | `Antiphon.PtyHost.Tests.Card0490NativeExecutionTests` | 1/0/0 | 0.004 | 0.004 |
| `pty-host-all-profile-16.trx:8` | `Antiphon.PtyHost.Tests.Card0490NativeExecutionTests` | 1/0/0 | 0.006 | 0.006 |
| `pty-host-all-profile-17.trx:8` | `Antiphon.PtyHost.Tests.Card0490NativeExecutionTests` | 1/0/0 | 0.005 | 0.005 |
| `pty-host-all-profile-18.trx:8` | `Antiphon.PtyHost.Tests.Card0490NativeExecutionTests` | 1/0/0 | 0.006 | 0.006 |
| `pty-host-all-profile-19.trx:8` | `Antiphon.PtyHost.Tests.Card0490NativeExecutionTests` | 1/0/0 | 0.041 | 0.041 |
| `pty-host-all-profile-2.trx:8` | `Antiphon.PtyHost.Tests.Card0490NativeExecutionTests` | 1/0/0 | 0.010 | 0.010 |
| `pty-host-all-profile-20.trx:8` | `Antiphon.PtyHost.Tests.Card0490NativeExecutionTests` | 1/0/0 | 0.011 | 0.011 |
| `pty-host-all-profile-21.trx:8` | `Antiphon.PtyHost.Tests.Card0490NativeExecutionTests` | 1/0/0 | 0.017 | 0.017 |
| `pty-host-all-profile-22.trx:8` | `Antiphon.PtyHost.Tests.Card0490NativeExecutionTests` | 1/0/0 | 0.005 | 0.005 |
| `pty-host-all-profile-23.trx:8` | `Antiphon.PtyHost.Tests.Card0490NativeExecutionTests` | 1/0/0 | 0.017 | 0.017 |
| `pty-host-all-profile-24.trx:8` | `Antiphon.PtyHost.Tests.Card0490NativeExecutionTests` | 1/0/0 | 0.015 | 0.015 |
| `pty-host-all-profile-25.trx:8` | `Antiphon.PtyHost.Tests.Card0490NativeExecutionTests` | 1/0/0 | 0.008 | 0.008 |
| `pty-host-all-profile-26.trx:8` | `Antiphon.PtyHost.Tests.Card0490NativeExecutionTests` | 1/0/0 | 0.012 | 0.012 |
| `pty-host-all-profile-27.trx:8` | `Antiphon.PtyHost.Tests.Card0490NativeExecutionTests` | 1/0/0 | 0.004 | 0.004 |
| `pty-host-all-profile-28.trx:8` | `Antiphon.PtyHost.Tests.Card0490NativeExecutionTests` | 1/0/0 | 0.008 | 0.008 |
| `pty-host-all-profile-29.trx:8` | `Antiphon.PtyHost.Tests.Card0490NativeExecutionTests` | 1/0/0 | 0.010 | 0.010 |
| `pty-host-all-profile-3.trx:8` | `Antiphon.PtyHost.Tests.Card0490NativeExecutionTests` | 1/0/0 | 0.003 | 0.003 |
| `pty-host-all-profile-30.trx:8` | `Antiphon.PtyHost.Tests.Card0490NativeExecutionTests` | 1/0/0 | 0.006 | 0.006 |
| `pty-host-all-profile-31.trx:8` | `Antiphon.PtyHost.Tests.Card0490NativeExecutionTests` | 1/0/0 | 0.008 | 0.008 |
| `pty-host-all-profile-32.trx:8` | `Antiphon.PtyHost.Tests.Card0490NativeExecutionTests` | 1/0/0 | 0.007 | 0.007 |
| `pty-host-all-profile-33.trx:8` | `Antiphon.PtyHost.Tests.Card0490NativeExecutionTests` | 1/0/0 | 0.004 | 0.004 |
| `pty-host-all-profile-34.trx:8` | `Antiphon.PtyHost.Tests.Card0490NativeExecutionTests` | 1/0/0 | 0.006 | 0.006 |
| `pty-host-all-profile-35.trx:8` | `Antiphon.PtyHost.Tests.Card0490NativeExecutionTests` | 1/0/0 | 0.012 | 0.012 |
| `pty-host-all-profile-36.trx:8` | `Antiphon.PtyHost.Tests.Card0490NativeExecutionTests` | 1/0/0 | 0.004 | 0.004 |
| `pty-host-all-profile-37.trx:8` | `Antiphon.PtyHost.Tests.Card0490NativeExecutionTests` | 1/0/0 | 0.004 | 0.004 |
| `pty-host-all-profile-38.trx:8` | `Antiphon.PtyHost.Tests.Card0490NativeExecutionTests` | 1/0/0 | 0.010 | 0.010 |
| `pty-host-all-profile-39.trx:8` | `Antiphon.PtyHost.Tests.Card0490NativeExecutionTests` | 1/0/0 | 0.007 | 0.007 |
| `pty-host-all-profile-4.trx:8` | `Antiphon.PtyHost.Tests.Card0490NativeExecutionTests` | 1/0/0 | 0.012 | 0.012 |
| `pty-host-all-profile-49.trx:8` | `Antiphon.PtyHost.Tests.ShadowCopyStoreTests` | 1/0/0 | 0.138 | 0.138 |
| `pty-host-all-profile-5.trx:8` | `Antiphon.PtyHost.Tests.Card0490NativeExecutionTests` | 1/0/0 | 0.011 | 0.011 |
| `pty-host-all-profile-50.trx:8` | `Antiphon.PtyHost.Tests.ShadowCopyStoreTests` | 1/0/0 | 0.085 | 0.085 |
| `pty-host-all-profile-51.trx:8` | `Antiphon.PtyHost.Tests.ShadowCopyStoreTests` | 2/0/0 | 0.173 | 0.294 |
| `pty-host-all-profile-52.trx:8` | `Antiphon.PtyHost.Tests.ShadowCopyStoreTests` | 1/0/0 | 0.095 | 0.095 |
| `pty-host-all-profile-53.trx:8` | `Antiphon.PtyHost.Tests.ShadowCopyStoreTests` | 2/0/0 | 0.085 | 0.169 |
| `pty-host-all-profile-54.trx:8` | `Antiphon.PtyHost.Tests.ShadowCopyStoreTests` | 1/0/0 | 0.159 | 0.159 |
| `pty-host-all-profile-55.trx:8` | `Antiphon.PtyHost.Tests.ShadowCopyStoreTests` | 1/0/0 | 0.047 | 0.047 |
| `pty-host-all-profile-56.trx:8` | `Antiphon.PtyHost.Tests.ShadowCopyStoreTests` | 1/0/0 | 0.037 | 0.037 |
| `pty-host-all-profile-57.trx:8` | `Antiphon.PtyHost.Tests.ShadowCopyStoreTests` | 1/0/0 | 0.199 | 0.199 |
| `pty-host-all-profile-6.trx:8` | `Antiphon.PtyHost.Tests.Card0490NativeExecutionTests` | 1/0/0 | 0.006 | 0.006 |
| `pty-host-all-profile-7.trx:8` | `Antiphon.PtyHost.Tests.Card0490NativeExecutionTests` | 1/0/0 | 0.005 | 0.005 |
| `pty-host-all-profile-8.trx:8` | `Antiphon.PtyHost.Tests.Card0490NativeExecutionTests` | 1/0/0 | 0.009 | 0.009 |
| `pty-host-all-profile-9.trx:8` | `Antiphon.PtyHost.Tests.Card0490NativeExecutionTests` | 1/0/0 | 0.021 | 0.021 |

### markdown-pdf class timings

| Chunk / first row | Full test class | P/F/N | Class wall s | Sum test s |
| --- | --- | --- | --- | --- |
| `markdown-pdf-all-profile-1.trx:8` | `Antiphon.MarkdownPdf.Tests.MarkdownPdfCommandTests` | 1/0/0 | 0.106 | 0.106 |
| `markdown-pdf-all-profile-10.trx:8` | `Antiphon.MarkdownPdf.Tests.MarkdownPdfRendererTests` | 1/0/0 | 0.086 | 0.086 |
| `markdown-pdf-all-profile-11.trx:8` | `Antiphon.MarkdownPdf.Tests.MarkdownPdfRendererTests` | 1/0/0 | 0.062 | 0.062 |
| `markdown-pdf-all-profile-2.trx:8` | `Antiphon.MarkdownPdf.Tests.MarkdownPdfCommandTests` | 6/0/0 | 0.719 | 0.699 |
| `markdown-pdf-all-profile-3.trx:8` | `Antiphon.MarkdownPdf.Tests.MarkdownPdfCommandTests` | 1/0/0 | 0.214 | 0.214 |
| `markdown-pdf-all-profile-4.trx:8` | `Antiphon.MarkdownPdf.Tests.MarkdownPdfRendererTests` | 1/0/0 | 0.012 | 0.012 |
| `markdown-pdf-all-profile-5.trx:8` | `Antiphon.MarkdownPdf.Tests.MarkdownPdfRendererTests` | 1/0/0 | 1.071 | 1.071 |
| `markdown-pdf-all-profile-6.trx:8` | `Antiphon.MarkdownPdf.Tests.MarkdownPdfRendererTests` | 1/0/0 | 1.017 | 1.017 |
| `markdown-pdf-all-profile-7.trx:8` | `Antiphon.MarkdownPdf.Tests.MarkdownPdfRendererTests` | 1/0/0 | 0.015 | 0.015 |
| `markdown-pdf-all-profile-8.trx:8` | `Antiphon.MarkdownPdf.Tests.MarkdownPdfRendererTests` | 1/0/0 | 0.316 | 0.316 |
| `markdown-pdf-all-profile-9.trx:8` | `Antiphon.MarkdownPdf.Tests.MarkdownPdfRendererTests` | 1/0/0 | 0.081 | 0.081 |

### observability class timings

| Chunk / first row | Full test class | P/F/N | Class wall s | Sum test s |
| --- | --- | --- | --- | --- |
| `observability-all.trx:140` | `Antiphon.Observability.Tests.ExternalBoundaryTests` | 2/0/0 | 0.123 | 0.122 |
| `observability-all.trx:22` | `Antiphon.Observability.Tests.LiveStartupIntegrationTests` | 0/1/0 | 29.486 | 29.486 |
| `observability-all.trx:8` | `Antiphon.Observability.Tests.LiveTimingTests` | 4/0/0 | 0.124 | 0.229 |
| `observability-all.trx:11` | `Antiphon.Observability.Tests.StartupEndpointTests` | 2/0/0 | 0.292 | 0.331 |
| `observability-all.trx:142` | `Antiphon.Observability.Tests.StartupScriptTests` | 1/0/0 | 0.889 | 0.889 |
| `observability-all.trx:18` | `Antiphon.Tests.TestHelpers.ProductionRunnerGuardTests` | 2/0/0 | 2.074 | 2.102 |

### messaging class timings

| Chunk / first row | Full test class | P/F/N | Class wall s | Sum test s |
| --- | --- | --- | --- | --- |
| `messaging-all.trx:8` | `Antiphon.Messaging.Tests.Client.AntiphonMessagingClientTests` | 7/0/0 | 0.343 | 1.024 |
| `messaging-all.trx:298` | `Antiphon.Messaging.Tests.Client.KafkaInboundCommitTests` | 2/0/0 | 14.505 | 14.504 |
| `messaging-all.trx:315` | `Antiphon.Messaging.Tests.Client.KafkaOutboundPayloadTests` | 4/0/0 | 21.089 | 21.086 |
| `messaging-all.trx:134` | `Antiphon.Messaging.Tests.Conformance.TelegramContractTests` | 8/0/0 | 3.860 | 27.540 |
| `messaging-all.trx:151` | `Antiphon.Messaging.Tests.Conformance.TelegramLiveChatConformanceTests` | 1/0/0 | 3.526 | 3.526 |
| `messaging-all.trx:15` | `Antiphon.Messaging.Tests.Contract.ContractSchemaTests` | 2/0/0 | 0.100 | 0.119 |
| `messaging-all.trx:16` | `Antiphon.Messaging.Tests.Contract.WireContractTests` | 10/0/0 | 0.025 | 0.030 |
| `messaging-all.trx:264` | `Antiphon.Messaging.Tests.FakeServerPortTests` | 1/0/0 | 6.035 | 6.035 |
| `messaging-all.trx:27` | `Antiphon.Messaging.Tests.Gateway.ConsumerLagAssessmentTests` | 18/0/0 | 0.014 | 0.010 |
| `messaging-all.trx:45` | `Antiphon.Messaging.Tests.Gateway.EchoGatewaySampleTests` | 5/0/0 | 0.012 | 0.017 |
| `messaging-all.trx:49` | `Antiphon.Messaging.Tests.Gateway.GatewayMonitorStatusTests` | 5/0/0 | 0.081 | 0.106 |
| `messaging-all.trx:54` | `Antiphon.Messaging.Tests.Gateway.GatewayMonitorValidationTests` | 15/0/0 | 0.789 | 2.561 |
| `messaging-all.trx:67` | `Antiphon.Messaging.Tests.Gateway.GatewayTests` | 7/0/0 | 2.616 | 2.706 |
| `messaging-all.trx:89` | `Antiphon.Messaging.Tests.Gateway.InboundUnconsumedMonitorTests` | 18/0/0 | 0.060 | 0.073 |
| `messaging-all.trx:334` | `Antiphon.Messaging.Tests.Gateway.KafkaConsumerGroupObservationTests` | 9/0/0 | 23.747 | 23.744 |
| `messaging-all.trx:107` | `Antiphon.Messaging.Tests.Gateway.LibrarySufficiencyTests` | 7/0/0 | 2.480 | 2.507 |
| `messaging-all.trx:130` | `Antiphon.Messaging.Tests.PublishWorkflowSanityTests` | 2/0/0 | 0.039 | 0.058 |
| `messaging-all.trx:131` | `Antiphon.Messaging.Tests.Service.EfInboxReceiptStoreTests` | 9/0/0 | 2.285 | 8.953 |
| `messaging-all.trx:139` | `Antiphon.Messaging.Tests.Service.InMemoryInboxReceiptStoreTests` | 2/0/0 | 0.006 | 0.005 |
| `messaging-all.trx:364` | `Antiphon.Messaging.Tests.Service.InboxConsumerServiceTests` | 3/0/0 | 9.239 | 9.238 |
| `messaging-all.trx:269` | `Antiphon.Messaging.Tests.Service.InboxConversationMigrationTests` | 1/0/0 | 14.130 | 14.130 |
| `messaging-all.trx:152` | `Antiphon.Messaging.Tests.SlackChannelAdapterTests` | 34/0/0 | 1.232 | 10.356 |
| `messaging-all.trx:175` | `Antiphon.Messaging.Tests.SlackMrkdwnRendererTests` | 19/0/0 | 0.010 | 0.011 |
| `messaging-all.trx:207` | `Antiphon.Messaging.Tests.SlackResilienceTests` | 14/0/0 | 1.794 | 7.873 |
| `messaging-all.trx:230` | `Antiphon.Messaging.Tests.TelegramChannelAdapterTests` | 17/0/0 | 0.461 | 5.166 |
| `messaging-all.trx:212` | `Antiphon.Messaging.Tests.TelegramMarkdownRendererTests` | 21/0/0 | 0.080 | 0.018 |
| `messaging-all.trx:254` | `Antiphon.Messaging.Tests.TelegramResilienceTests` | 8/0/0 | 1.790 | 5.799 |
| `messaging-all.trx:257` | `Antiphon.TestSupport.TestClassificationGuardTests` | 1/0/0 | 0.153 | 0.153 |

### e2e class timings

| Chunk / first row | Full test class | P/F/N | Class wall s | Sum test s |
| --- | --- | --- | --- | --- |
| `e2e-all-group-2.trx:8` | `Antiphon.E2E.Fixtures.IsolatedSessionRunnerTeardownTests` | 2/0/0 | 0.229 | 0.243 |
| `e2e-all-group-2.trx:9` | `Antiphon.E2E.Fixtures.PtyBackendEnvGuardTests` | 1/0/0 | 0.012 | 0.012 |
| `e2e-all-group-3.trx:8` | `Antiphon.TestSupport.TestClassificationGuardTests` | 1/0/0 | 0.051 | 0.051 |

## Evidence integrity

SHA-256 of primary input and failure-bearing artifacts; full TRX case identities and failure text have been extracted above. Generated payloads remain at R and are not added to Git.

| Input | Bytes | SHA-256 |
| --- | --- | --- |
| `summary.json` | 12212324 | `e5356c07e8f57d083fe8bb334db0a7b95ad1119d7e04e42fb6dc26d23e55eaf7` |
| `client-all.json` | 565301 | `ac20775225078c0f00f5e8a322c2d3d59785612ccc75ccb5879aeea849b22245` |
| `client-all-tests.log` | 237582 | `d7a4e261f1795718970f9b0ae9695ab6383d4e7eabdfcc6e6f68526616223753` |
| `scripts-all-tests.log` | 50 | `a82d6541799102974ba67ec89ca1e5f103a25878029f843e778bc63e608e138a` |
| `script-test-apphost-graceful-stop.log` | 23820 | `40296ac574314a945bd8164c836a3c5eb9395852348c1df05c10bb1c1a1b643d` |
| `session-runner-all-group-1.trx` | 1825751 | `be9384110c92c5ff065bcd504610f19d748c02afeb2ec4ff9f6bc931895fa3e9` |
| `session-runner-all-group-2.trx` | 508774 | `80f1c581094696040beeb0edf212d54db10e59ff98926030fef2b60a9c7623fc` |
| `session-runner-all-group-3.trx` | 216284 | `8f24a66bae7dde50e5156cd750adbaaceb183e359699cffc7b974234f669ef62` |
| `session-runner-all-group-4.trx` | 139586 | `b22cca042a752069b2c10b27a37c838d97f777c3c61dbafd011c56e243e73aed` |
| `agents-pty-all-group-1.trx` | 881126 | `ee6194fb84195ed8129624d3d80553fb0b2877505c43674513ef1e5845384506` |
| `observability-all.trx` | 32433 | `d75cf404fdd3347b883e8de844757de316b0a99115aad241a8bb7db072a76362` |
| `e2e-all-group-1-tests.log` | 1440182 | `fd4e65d57ebc288bfa044f12d8d17082953ed7aab477f6cda7a617ca81186204` |

The run timing directory is not a reliable combined non-antiphon class ledger: timing/groups-executed.json identifies suite antiphon, while timing/groups-planned.json identifies observability (last suite). Class measurements here join each TRX directly and do not reinterpret that mixed metadata.

## Remaining uncertainties and disposition

- No fresh reproduction was authorized. Confirmed means the immediate mechanism is reconstructed above from candidate source and stored results; unconfirmed groups need the named isolated Debug selection on the stated OS.
- Linux reproducibility of async client failures and the crash worker is unmeasured. Timeouts are not labeled flakes; no retry or budget change was made.
- Windows path admission, denied .git access, repository digest normalization, native paste/encoding, adoption timing and logger ownership still need native evidence. The extraction does not establish that these share one cause.
- Runner group 3 outer timedOut conflicts with its completed short test run; retain both facts until the outer supervisor/process evidence explains it.
- NotExecuted and profile exclusions explain unavailable coverage, not a passing result. E2E group 1 final counts and class wall are unavailable; task e7421374 remains its owner.
- This report gives no fix design or implementation. Confirm current-tip source before commissioning repairs against these historical candidate failures.

## Not done, noted

No fix proposed or implemented; Debug should establish the unresolved mechanisms and current-tip status before Plan.

--- next stage ---
next: investigate
handoff: Dispatch Debug helpers for Linux L1 daemon closure (1), L2 graceful-stop fixture (9), L3 ProviderInputWait lists (2), L4 async client (3), L5 crash worker (1); retain Windows W1-W5 native groups (25) and investigate Linux-only Windows selection (24). E2E stays with e7421374.
artifact: docs/investigations/2026-10-25-rc-a779085c-non-antiphon-failures.md
