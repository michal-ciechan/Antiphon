# Final Windows rc qualification digest — run 8f055f87

Investigation completed UTC: 2026-10-27T05:00:09.846710+00:00. Task `7e719c57`; assigned branch `feat/card-task-7e719c57`; report base `718bd29f4a1504c64c491d607d31403fcb87dae2`.

**The whole run ended red: 137 failed TRX results plus 3 diagnostic/console-only failures, 140 recorded failures in 65 classes. Both timed-out chunks retain partial diagnostic results; 179 required cases have no observed terminal result. All ten required suites were attempted.**

## Run identity and evidence rules

Candidate `origin/rc/2026-10-25` / `105792f7d22f297746cdb338976f52e424b76fa7`; run `8f055f87452d4c76998604df0b522db5`; Windows; requested/effective selection Full (fallback reason baseline-absent). Started `2026-10-26T09:46:22.8061354Z`, completed `2026-10-27T04:43:23.1108664Z`, duration `68220.305` seconds (18.9501 hours). Stored verdict: `outcome=TESTS`, `succeeded=false`, `coverageComplete=false`, `testsPassed=false`, `nativeActiveMax=1`.

All raw evidence below is relative to `C:\Antiphon\releases\candidates\rc-2026-10-25-105792f7d22f\logs\2026-10-26-0946-8f055f87`. The run directory, release clone and processes were read-only/not operated on. Only this Markdown report is committed; derived JSON/scripts remain ignored in the task worktree. No build, test, release action, restart or process probe was run. Standing authority covered the evidence work; no further approval was needed.

`summary.json` is the run manifest: its 333 `suites` rows, four build rows, preflight, selection and reasons were reconciled. There is no separately named manifest file at the run root. The immutable attempt record is `../../attempts/8f055f87452d4c76998604df0b522db5/selection-attempt.json:1`, selection digest `5e80650843bfbf8c7ad1a660e0a9bf4b107899ae620f5a8ba27045bffd94a0d3`. SHA-256 of summary.json raw bytes: `bf4aeeaeb45333df4a58af84bb730e9eae17ae1a601e85aa12cf7c5001e5b668`. Earlier snapshot `origin/feat/card-task-d5c669aa:docs/investigations/2026-10-26-rc-8f055f87-windows-failure-digest.md` reported only 74 failures through chunk 64; it is superseded in extent, not erased.

Counts are expanded result instances, not unique method names. TRX `NotExecuted` is shown as skipped/unexecuted, never pass credit. A skipped required case still makes coverage incomplete. Diagnostic-only terminal results are recovered separately and do not turn an absent TRX into completed qualification. XML line endings are parser-normalized; each failure preserves the first 12 decoded error lines and first six stored `at` stack frames, without character truncation. F138–F140 come from diagnostic error states and are corroborated by console headers. Framework/duplicate frames remain. Fewer frames means the source supplies fewer. All source paths/lines in stored stacks refer to candidate 105792f7, not the report branch.

## Per-suite totals for the whole run

| Suite | Chunks attempted | Chunks with recorded test failures | TRX passed / failed / skipped | Diagnostic-only passed / failed / skipped | Observed passed / failed / skipped | Nonzero-exit rows | Missing TRX rows |
|---|---:|---:|---|---|---|---:|---|
| antiphon | 179 | 31 | 17618 / 115 / 497 | 1486 / 3 / 11 | 19104 / 118 / 508 | 33 | all-group-28, all-group-65 |
| session-runner | 6 | 3 | 1688 / 12 / 5 | 0 / 0 / 0 | 1688 / 12 / 5 | 3 | none |
| pty-host | 50 | 0 | 116 / 0 / 8 | 0 / 0 / 0 | 116 / 0 / 8 | 0 | none |
| agents-pty | 9 | 1 | 647 / 1 / 17 | 0 / 0 / 0 | 647 / 1 / 17 | 1 | none |
| messaging | 1 | 0 | 250 / 0 / 0 | 0 / 0 / 0 | 250 / 0 / 0 | 0 | none |
| client | 1 | 0 | n/a (JSON/log evidence) | 0 / 0 / 0 | 1841 / 0 / 0 | 0 | all |
| scripts | 1 | 0 | n/a (JSON/log evidence) | 0 / 0 / 0 | 591 / 0 / 0 | 0 | all |
| e2e | 74 | 8 | 161 / 9 / 0 | 0 / 0 / 0 | 161 / 9 / 0 | 8 | none |
| markdown-pdf | 11 | 0 | 16 / 0 / 0 | 0 / 0 / 0 | 16 / 0 / 0 | 0 | none |
| observability | 1 | 0 | 12 / 0 / 0 | 0 / 0 / 0 | 12 / 0 / 0 | 0 | none |

329 TRX files contain 21,172 results: 20,508 passed, 137 failed, 527 NotExecuted. The two missing-TRX diagnostic logs add 1,486 passed / 3 failed / 11 skipped, without duplicate UIDs. Observed native totals are therefore 21,994 passed / 140 failed / 538 skipped (22,672 results). Client adds 1,841 passed; the 13 script harness logs add 591 passed. Combined observed test/assertion totals are 24,426 passed / 140 failed / 538 skipped; script assertions and test cases are different units, so suite totals are the preferred comparison.

Client `client-all.json` records numTotalTests=1841, numPassedTests=1841, numFailedTests=0, numPendingTests=0, success=true; console records 127 test files passed. Client JSON’s 370 suite nodes are nested Vitest suites, not 370 files. Client and scripts intentionally have no TRX. Their missing TRX is format information, not an extra native coverage gap. All four build rows and all preflight entries have exitCode=0/timedOut=false where supplied.

### Script harness totals (console evidence)

| Log | Passed | Failed | Evidence line |
|---|---:|---:|---:|
| `script-test-apphost-graceful-stop.log` | 11 | 0 | 13 |
| `script-test-apphost-lock-age.log` | 40 | 0 | 44 |
| `script-test-apphost-main-worktree-guard.log` | 24 | 0 | 26 |
| `script-test-apphost-probe-class.log` | 23 | 0 | 25 |
| `script-test-cleanup-claude-sessions.log` | 45 | 0 | 344 |
| `script-test-cleanup-codex-test-residue.log` | 23 | 0 | 79 |
| `script-test-client-mode.log` | 6 | 0 | 8 |
| `script-test-deploy-am-service.log` | 170 | 0 | 422 |
| `script-test-deploy-nightly-watchdog.log` | 25 | 0 | 27 |
| `script-test-hooks.log` | 43 | 0 | 66 |
| `script-test-nightly-report.log` | 122 | 0 | 163 |
| `script-test-reap-zombie-agents.log` | 40 | 0 | 204 |
| `script-test-stage-value-report.log` | 19 | 0 | 21 |

Expected-error text inside passing script negative controls is not counted as failed tests. `scripts-all-tests.log:1` contains only the invocation stamp; individual harness logs own their assertions.

## Every exceptional manifest row

Exactly 45 rows have nonzero exit; 4 rows have no TRX (2 native gaps and 2 console/JSON formats). Their union is 47 rows below. There are exactly 2 timedOut=true rows, both Antiphon. No additional actual watchdog event was found in the suite logs; references to watchdog test names/opt-in skips are not watchdog events. Test-level timeout failures are retained individually in the failure catalog, separately from chunk watchdogs. Row numbers below are one-based positions in `summary.json.suites`; JSON indices are row minus one.

| Row | Suite/chunk | Exit | timedOut | TRX | Failed / skipped in TRX | Evidence reasons |
|---:|---|---:|---|---|---|---|
| 19 | antiphon/all-group-19 | 2 | false | present | 1 / 0 | failed-row; process-exit-2 |
| 26 | antiphon/all-group-26 | 2 | false | present | 1 / 1 | failed-row; unexecuted-outcome (1 rows; identities in skipped appendix); process-exit-2 |
| 28 | antiphon/all-group-28 | 1 | true | absent | 0 / 0 | missing-trx |
| 29 | antiphon/all-group-29 | 2 | false | present | 1 / 0 | failed-row; process-exit-2 |
| 32 | antiphon/all-group-32 | 2 | false | present | 1 / 0 | failed-row; process-exit-2 |
| 34 | antiphon/all-group-34 | 2 | false | present | 2 / 0 | failed-row; process-exit-2 |
| 35 | antiphon/all-group-35 | 2 | false | present | 8 / 0 | failed-row; process-exit-2 |
| 36 | antiphon/all-group-36 | 2 | false | present | 10 / 0 | failed-row; process-exit-2 |
| 37 | antiphon/all-group-37 | 2 | false | present | 2 / 6 | failed-row; unexecuted-outcome (6 rows; identities in skipped appendix); process-exit-2 |
| 38 | antiphon/all-group-38 | 2 | false | present | 1 / 0 | failed-row; process-exit-2 |
| 42 | antiphon/all-group-42 | 2 | false | present | 1 / 0 | failed-row; process-exit-2 |
| 49 | antiphon/all-group-49 | 2 | false | present | 4 / 1 | failed-row; unexecuted-outcome (1 rows; identities in skipped appendix); process-exit-2 |
| 51 | antiphon/all-group-51 | 2 | false | present | 1 / 0 | failed-row; process-exit-2 |
| 54 | antiphon/all-group-54 | 2 | false | present | 3 / 0 | failed-row; process-exit-2 |
| 58 | antiphon/all-group-58 | 2 | false | present | 1 / 0 | failed-row; process-exit-2 |
| 61 | antiphon/all-group-61 | 2 | false | present | 12 / 83 | failed-row; unexecuted-outcome (83 rows; identities in skipped appendix); process-exit-2 |
| 62 | antiphon/all-group-62 | 2 | false | present | 9 / 17 | failed-row; unexecuted-outcome (17 rows; identities in skipped appendix); process-exit-2 |
| 63 | antiphon/all-group-63 | 2 | false | present | 16 / 5 | failed-row; unexecuted-outcome (5 rows; identities in skipped appendix); process-exit-2 |
| 65 | antiphon/all-group-65 | 1 | true | absent | 0 / 0 | missing-trx |
| 66 | antiphon/all-group-66 | 2 | false | present | 3 / 33 | failed-row; unexecuted-outcome (33 rows; identities in skipped appendix); process-exit-2 |
| 71 | antiphon/all-group-71 | 2 | false | present | 4 / 4 | failed-row; unexecuted-outcome (4 rows; identities in skipped appendix); process-exit-2 |
| 74 | antiphon/all-group-74 | 2 | false | present | 1 / 42 | failed-row; unexecuted-outcome (42 rows; identities in skipped appendix); process-exit-2 |
| 75 | antiphon/all-group-75 | 2 | false | present | 1 / 40 | failed-row; unexecuted-outcome (40 rows; identities in skipped appendix); process-exit-2 |
| 76 | antiphon/all-group-76 | 2 | false | present | 7 / 15 | failed-row; unexecuted-outcome (15 rows; identities in skipped appendix); process-exit-2 |
| 77 | antiphon/all-group-77 | 2 | false | present | 5 / 5 | failed-row; unexecuted-outcome (5 rows; identities in skipped appendix); process-exit-2 |
| 78 | antiphon/all-group-78 | 2 | false | present | 1 / 2 | failed-row; unexecuted-outcome (2 rows; identities in skipped appendix); process-exit-2 |
| 95 | antiphon/all-split-14 | 2 | false | present | 6 / 0 | failed-row; process-exit-2 |
| 96 | antiphon/all-split-15 | 2 | false | present | 5 / 0 | failed-row; process-exit-2 |
| 97 | antiphon/all-split-16 | 2 | false | present | 1 / 0 | failed-row; process-exit-2 |
| 99 | antiphon/all-split-18 | 8 | false | present | 0 / 132 | unexecuted-outcome (132 rows; identities in skipped appendix); process-exit-8 |
| 102 | antiphon/all-split-21 | 2 | false | present | 1 / 17 | failed-row; unexecuted-outcome (17 rows; identities in skipped appendix); process-exit-2 |
| 103 | antiphon/all-split-22 | 2 | false | present | 3 / 0 | failed-row; process-exit-2 |
| 105 | antiphon/all-split-24 | 2 | false | present | 3 / 3 | failed-row; unexecuted-outcome (3 rows; identities in skipped appendix); process-exit-2 |
| 180 | session-runner/all-group-1 | 2 | false | present | 4 / 3 | failed-row; unexecuted-outcome (3 rows; identities in skipped appendix); process-exit-2 |
| 181 | session-runner/all-group-2 | 2 | false | present | 6 / 1 | failed-row; unexecuted-outcome (1 rows; identities in skipped appendix); process-exit-2 |
| 183 | session-runner/all-group-4 | 2 | false | present | 2 / 1 | failed-row; unexecuted-outcome (1 rows; identities in skipped appendix); process-exit-2 |
| 236 | agents-pty/all-group-1 | 2 | false | present | 1 / 17 | failed-row; unexecuted-outcome (17 rows; identities in skipped appendix); process-exit-2 |
| 246 | client/all | 0 | false | absent | 0 / 0 | non-TRX format; exit 0 |
| 247 | scripts/all | 0 | false | absent | 0 / 0 | non-TRX format; exit 0 |
| 252 | e2e/all-group-5 | 2 | false | present | 1 / 0 | failed-row; process-exit-2 |
| 256 | e2e/all-group-9 | 2 | false | present | 1 / 0 | failed-row; process-exit-2 |
| 260 | e2e/all-split-2 | 2 | false | present | 1 / 0 | failed-row; process-exit-2 |
| 292 | e2e/all-split-34 | 2 | false | present | 1 / 0 | failed-row; process-exit-2 |
| 297 | e2e/all-split-39 | 2 | false | present | 1 / 0 | failed-row; process-exit-2 |
| 302 | e2e/all-split-44 | 2 | false | present | 1 / 0 | failed-row; process-exit-2 |
| 303 | e2e/all-split-45 | 2 | false | present | 1 / 0 | failed-row; process-exit-2 |
| 319 | e2e/all-split-61 | 2 | false | present | 2 / 0 | failed-row; process-exit-2 |

The 37 bad rows cited for db2c2088 belong to a different run. This run has 45 nonzero-exit rows. `antiphon/all-split-18` exits 8 with zero passed/failed and 132 skipped; its console explicitly says “Zero tests ran.” It is not an assertion failure or process crash.

## Missing results and incomplete coverage: reconstructed mechanism

All ten required suites have summary rows. No whole suite was omitted. Antiphon group 28 and group 65 never emitted their expected TRX. Summary reasons contain `missing-trx` twice and `timeout antiphon` twice. Despite timedOut=true, their row result is FAIL because the missing-TRX verdict overwrites the initial TIMEOUT label (`scripts/lib/nightly-tests-impl.ps1:1169`, `:1235`). Final outcome TESTS also does not deny timeouts: the outcome precedence puts TESTS before TIMEOUT (`scripts/lib/nightly-tests-impl.ps1:839`–`:844`).

**Confirmed accounting mechanism:** `scripts/lib/nightly-coverage.ps1:1195`–`:1201` returns immediately on absent TRX, before reading execution diagnostics. Thus `scripts/lib/nightly-tests-impl.ps1:1213`–`:1214` receives no terminalNodes for either timed-out chunk, and its suite-union check at `:1270`–`:1274` reports all 1,679 required UIDs from those chunks as missing. The 1,679 value is the count of whole UIDs split at comma followed by Antiphon.; splitting on every comma would incorrectly split parameter signatures.

Independent reconstruction from each `TestNodeUpdateMessage` UID and terminal state recovers 1,500 of those exact UIDs, leaving 179 with no observed terminal state. This recovery is evidence interpretation only; it does not repair or override qualification. The logs keep running cases close to the one-hour cap, so neither timeout proves that its final named test itself hung. Policy `tests/test-execution-policy.json:865` sets Antiphon to 3,600,000 ms; the driver passes that cap at `scripts/lib/nightly-tests-impl.ps1:1134`. The Windows owner sets TimedOut at its execution deadline and terminates its private job (`scripts/lib/nightly-owned-process.cs:178`–`:199`); output-drain timeout is also possible in that type. No separate custody observation was supplied to distinguish every internal termination phase, but both diagnostic timelines span almost exactly an hour.

| Chunk | Diagnostic terminal states recovered | Started without terminal state at capture | Last evidence |
|---|---|---|---|
| antiphon/all-group-28 | 540 passed, 0 failed, 0 skipped | ChannelBridgeTests.A_mid_turn_launch_note_does_not_steal_the_channel_reply | `antiphon-all-group-28-execution/log_261026132457749.diag:4692`–`:4693`, timestamp 2026-10-26T14:24:55.4975623+00:00; diagnostic starts 13:24:57 |
| antiphon/all-group-65 | 946 passed, 3 failed, 11 skipped | WorktreeManagerGitIntegrationTests.WorktreeManager_raw_remove_preserves_unregistered_leftover_directory | `antiphon-all-group-65-execution/log_261026190647795.diag:10307`–`:10308`, timestamp 2026-10-26T20:06:45.0826691+00:00; diagnostic starts 19:06:47 |

The group-28 console has only its invocation/runtime/diagnostic header, while its diagnostic log contains 540 passes. Group-65 console failures at lines 59, 83 and 125 are F138–F140 below, with precise timing/error states also in the diagnostic log. Therefore console-only scanning would also undercount completed work.

The Antiphon census reconciles exactly: 19,909 required expanded cases = 18,230 TRX results + 1,500 recovered diagnostic terminal results + 179 cases without terminal evidence. Group 28 accounts for 164 of the final 179 gaps (one started, 163 without a start record); group 65 accounts for 15 (one started, 14 without a start record). This does not add coverage credit for any excluded case.

### Required cases without any terminal evidence (179)

| Class | Cases without terminal evidence | State |
|---|---:|---|
| `Antiphon.Tests.Application.ApiErrorRecoveryServiceTests` | 40 | no terminal state found |
| `Antiphon.Tests.Application.ChannelBridgeTests` | 14 | one started case plus remaining unobserved |
| `Antiphon.Tests.Application.ChannelInboundRecoveryTests` | 41 | no terminal state found |
| `Antiphon.Tests.Application.CodexStartupDeliveryTests` | 62 | no terminal state found |
| `Antiphon.Tests.Application.LandEvidenceWarningDeliveryTests` | 7 | no terminal state found |
| `Antiphon.Tests.Infrastructure.WorktreeManagerGitIntegrationTests` | 15 | one started case plus remaining unobserved |

Full expanded identities, including all parameter variants, are retained in the missing-results appendix. These are not counted as failed assertions. Determining each unstarted case’s intended chunk from a separate launch manifest is unnecessary to prove absence; group membership follows its class and the two truncated native launch timelines, with no new execution.

### Other coverage reasons

The 71 stored reason entries break down as follows; repetitions are retained rather than deduplicated:

| Reason family | Entries |
|---|---:|
| unexecuted-outcome | 27 |
| failed-row | 19 |
| process-exit-2 | 19 |
| missing-trx | 2 |
| timeout antiphon | 2 |
| process-exit-8 | 1 |
| missing-expanded-row union | 1 |

TRX contributes 527 NotExecuted results (Antiphon 497, SessionRunner 5, PtyHost 8, Agents.Pty 17), with another 11 in group-65 diagnostics. Most identify native Linux semantics or explicit opt-in behavior in their log messages. The required census still includes them; `scripts/lib/nightly-coverage.ps1:1022`–`:1024` deliberately marks such outcomes incomplete. Every skipped identity, display variant and message is retained below. Exclusions are different: per-suite excludedUidCount is recorded once per suite, not summed over chunks.

| Suite | Required census | Excluded census |
|---|---:|---:|
| antiphon | 19909 | 99 |
| session-runner | 1705 | 37 |
| pty-host | 124 | 23 |
| agents-pty | 665 | 105 |
| messaging | 250 | 0 |
| e2e | 170 | 7 |
| markdown-pdf | 16 | 4 |
| observability | 12 | 0 |

## Pushed repair inventory and evidence limits

Refs were refreshed with git fetch origin during this investigation. A pushed repair means its diff targets the observed mechanism; it does not mean reviewed, landed, activated or native-Windows green. No failure result from the original candidate is retroactively changed. Full branch tips below pin the inspected evidence. For 7f7aa584 and 0d5de8f1, their committed reports were read; for a043b0a4 the committed `.antiphon/task-a043b0a4.md` report was read. Other dispositions are based on branch commit messages and changed source, not unstated task-completion receipts.

| Branch suffix (`feat/card-task-`) | Inspected full tip | Scope / limitation |
|---|---|---|
| a043b0a4 | `ffe9525531e2c6087a5395b43527c01bf460a8b5` | Shared tool/PowerShell Windows read-mode fix; portable red/green; historical native stalls unclosed. |
| 09289b6d | `d1e4be9ccb3b547b6c8a738301f050d3fdbeac55` | Claude observations, conditional receipt forwarding, POSIX remote Grok paths, settlement workers and unsupported transport paths; sign-in LF hypothesis explicitly dropped. |
| 7f7aa584 | `920ff8fc83d3fa6db1c43142264b8561c5f150a7` | Eight fixture classes repaired; BaselineComparer/PlanCoverageBinding no-repro; exact native reruns remain. |
| 9c66bc68 | `64c6ecc6d0ebfea4fc8255a02e887c39ab6cd5eb` | HostCleanupCheckpointExtraction dependency count expectation updated. |
| cb746f9d | `73ef01b156b5c7ea1c19d07dc80ddc924fc20cdf` | Source-gate closure roster completed; synthetic platform fixture inventory isolated. |
| d42f9a7c | `81924e54e7e7a638acd19c951412295752a16186` | Pipeline endpoint uses a dedicated database. |
| e3a90a36 | `a4167bde7d24fe9cb9773b38bb4cff95c6712576` | Platform-native index-lock stall hooks; separate from mtime positive-control fix. |
| 0d5de8f1 | `26e0c0b663da64a9fa5be998e516e25577f703b6` | Read-only race model no longer denies peer writer; additional post-candidate local-loop-variable defect fixed. Windows probe-path failures not closed. |
| a58b9b8c | `95267792ebb16f054d8646cf84059dafea2ec5d5` | Crash-worker path normalization; does not repair all CompletedCardWorkspaceRemoval failures. |
| 50faf42f | `25a98a02853bffabcadc0a399ea01deab479b549` | Daemon build mock reaches subprocess seam. |
| 3a4f6e81 | `d6ee905c4fc26421b18a1791e8466a7eb7f59fd8` | Repository identity expectation uses Git path separators. |
| 2fdd8dc8 | `de068dbb26f3685a4bcfe2e0af9f07093e3981d3` | Portable submit witness only; no doubled-quote/encoding repair in diff. |
| c320441d | `cd793a1f0fae61ae522af005db610f1fee845561` | Terminal shortcut menu lifetime fix. |
| 1e248236 | `daca35af03133c40625e4767de964afeaed080d9` | Phone pipeline width and future queue hold. |
| 380013d3 | `03d62fedb18a36c573b8b508ad2fe21f26985f48` | Board diff navigation and terminal input ordering. |
| 7cdaefee | `1166ae1a4e51561036ae98ccf036da4b0b02f4f0` | SDK provenance policy and evaluated graph checks; not a blanket LandNonCodeSkip closure. |

Post-candidate repairs already present in the report base include the Card0418 fixture rename (052479a2...), Windows recovery custody (de78f02f... / 694ab107...), and the WorktreeManager Git separator expectation (568294688...). These are separately labeled in each result; their existence is not proof of native closure. The 7cdaefee SDK branch addresses graph evaluation across SDK versions; no trailing-dot fixture or missing-NUL repair was found in its diff.

## Classes with no complete matching fix yet

This table includes classes with no fix, partial coverage, diagnostic-only work, or no-repro. Classes whose every recorded result has an identified addressing change are omitted here but remain in the complete catalog. The quoted Windows text is taken from this run; F IDs link the full first-12-line error and top-six frames. Native rerun obligations remain even for ADDRESSED results.

| Class | Open failure IDs | Windows error text / diagnostic facts | Disposition / evidence needed |
|---|---|---|---|
| `Antiphon.Agents.Pty.Tests.FakeClaudeContractTests` | [F128](#f128) | ShouldAssertException: looseRaw /     should contain (case insensitive comparison) / "--append-system-prompt␟a "quoted" word" /     but was actually / "--echo-argsÔÉƒ--echo-argv-strictÔÉƒ--append-system-promptÔÉƒa "quoted" word" /  / Additional Info: /     the .NET parser reports the intended argument INTACT on the shredding line — which is precisely how this went unnoticed for months. Line was: C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Agents.Pty.Tests\bin\Debug\net9.0\fakeclaude\fakeclaude.exe "--echo-args" "--echo-argv-strict" "--append-system-prompt" "a ""quoted"" word" | WITNESS-ONLY: 2fdd8dc8 adds a portable Windows submit witness; its diff does not change doubled-quote argument decoding. The observed mojibake failure has no demonstrated repair. |
| `Antiphon.E2E.AgentTaskLandDeliveryE2ETests` | [F131](#f131), [F132](#f132) | ShouldAssertException: terminal.Type /     should be / AgentTaskEventType.LandedWithResidue /     but was / AgentTaskEventType.Landed; ShouldAssertException: note.ConfirmedAt /     should be null but was / 2026-10-27T03:38:38.5361780Z | UNRESOLVED: No matching pushed repair established; native mechanism remains open. |
| `Antiphon.E2E.DispatchBaseWarningDeliveryE2ETests` | [F134](#f134), [F135](#f135) | TimeoutException: one committed mismatch dispatch intent | UNRESOLVED: No matching pushed repair established; native mechanism remains open. |
| `Antiphon.E2E.WorkflowDeleteTests` | [F136](#f136), [F137](#f137) | TimeoutException: Timeout 10000ms exceeded. / =========================== logs =========================== / waiting for navigation to "**/" until "Load" /   navigated to "http://127.0.0.1:62453/workflows" / ============================================================; TimeoutException: Timeout 10000ms exceeded. / Call log: /   - waiting for Locator("[role='article']").First to be visible | UNRESOLVED: No matching pushed repair established; native mechanism remains open. |
| `Antiphon.SessionRunner.Tests.CompletedCardWorkspaceRemovalTests` | [F116](#f116), [F117](#f117), [F118](#f118) | UnauthorizedAccessException: Access to the path 'C:\Users\lndco\AppData\Local\Temp\c1032-fence-0ba9292d9f5d46969df1f10e2abda8bb\work\worktrees\task-deadbeef\.git' is denied.; ShouldAssertException: Directory.GetFiles(bin).Length /     should be / 2 /     but was / 1 /  / Additional Info: /     requires case-sensitive scratch filesystem; UnauthorizedAccessException: Access to the path 'C:\Users\lndco\AppData\Local\Temp\c1032-fence-54daa6e29ef34cc4b41bd93e7bbffc48\work\worktrees\task-deadbeef\.git' is denied. | UNRESOLVED: No matching pushed repair established; native mechanism remains open. |
| `Antiphon.SessionRunner.Tests.GrokRulesHttpAcceptanceTests` | [F126](#f126) | ShouldAssertException: started.StatusCode /     should be / HttpStatusCode.Created /     but was / HttpStatusCode.InternalServerError | UNRESOLVED: No matching pushed repair established; native mechanism remains open. |
| `Antiphon.Tests.Application.DispatcherSiblingInspectionSchedulingTests` | [F001](#f001) | ShouldAssertException: f.Queue.Outstanding /     should be / 0 /     but was / 1 | UNRESOLVED: No matching pushed repair established; native mechanism remains open. |
| `Antiphon.Tests.Application.GrokSignInRuntimeTests` | [F037](#f037) | TaskCanceledException: A task was canceled. | UNRESOLVED: No matching pushed repair established; native mechanism remains open. |
| `Antiphon.Tests.Application.LandNonCodeSkipTests` | [F097](#f097), [F098](#f098), [F099](#f099), [F100](#f100), [F101](#f101), [F102](#f102), [F103](#f103), [F104](#f104), [F105](#f105), [F106](#f106), [F107](#f107), [F108](#f108) | ShouldAssertException: h.Verifier.Calls /     should be / 0 /     but was / 1 /  / Additional Info: /     publication=Landed; reason=; command=dotnet build; dotnet run --project tests/Antiphon.Tests; policy=unrecognized-build-graph; ShouldAssertException: h.Verifier.Calls /     should be / 0 /     but was / 1 /  / Additional Info: /     a docs-only rebased candidate must not invoke the verifier or its build slot; publication=Landed; reason=; command=dotnet build; dotnet run --project tests/Antiphon.Tests; policy=unrecognized-build-graph; ShouldAssertException: digest /     should be / "8D4CCE85EBDE82978FD648F0F5DDCEA221A3F492C4A28D67DCD1A7A549FCF34B" /     but was / "124B52D47DD3232B60F4AEAF083B5E50ACC68DEDDE374AB2ECD6D43E543DC055" /     difference / Difference     \|  \|    \|         \|    \|    \|    \|    \|    \|    \|         \|    \|    \|    \|    \|    \|    \|    \|    \|    \|         /                \| \\|/  \\|/       \\|/  \\|/  \\|/  \\|/  \\|/  \\|/  \\|/       \\|/  \\|/  \\|/  \\|/  \\|/  \\|/  \\|/  \\|/  \\|/  \\|/        / Index          \| 0    1    2    3    4    5    6    7    8    9    10   11   12   13   14   15   16   17   18   19   20   ...   / Expected Value \| 8    D    4    C    C    E    8    5    E    B    D    E    8    2    9    7    8    F    D    6    4    ...   / Actual Value   \| 1    2    4    B    5    2    D    4    7    D    D    3    2    3    2    B    6    0    F    4    A    ...   / Expected Code  \| 56   68   52   67   67   69   56   53   69   66   68   69   56   50   57   55   56   70   68   54   52   ...  ; ShouldAssertException: ran.Code /     should be / 1 /     but was / 0 /  / Additional Info: /     digest=F346980368EC03930A036CE4833CD73232897A7D14A9ED4716C5BF6A7AF9CB7E count=1; error: Invalid path 'server/fixtures/notes.md.'; fatal: git update-index: --cacheinfo cannot add server/fixtures/notes.md.; InvalidOperationException: table row N-12 failed: process.ExitCode /     should be / 0 /     but was / 128 /  / Additional Info: /     error: Invalid path 'server/fixtures/notes.md.' / fatal: git update-index: --cacheinfo cannot add server/fixtures/notes.md. | PARTIAL: 7cdaefee SDK provenance/evaluation repair is relevant to audit fallback/digest errors. Existing git.exe fixture resolution also applies. Exact native cases remain unverified. UNRESOLVED: No matching pushed repair established; native mechanism remains open. |
| `Antiphon.Tests.Application.RemoteControlRecoveryPtyIntegrationTests` | [F009](#f009), [F010](#f010) | AggregateException: test-owned disposal unresolved elapsed=00:00:00.0971358 (cleanup-unresolved elapsed=00:00:00.0915945 (capture session=a2b376e7-545e-4738-8a82-3af873f494b9 manifest=C:\Users\lndco\AppData\Local\Temp\antiphon-c514-pty-a2b376e7545e47388a823af873f494b9\pty-hosts\manifests\a2b376e7545e47388a823af873f494b9.json)); ShouldAssertException: await lane.WaitForPersistedPromptsAsync([work], TimeSpan.FromSeconds(20)) /     should be / True /     but was / False /  / Additional Info: /     the child must record its prompt in its own JSONL | PARTIAL: 09289b6d changes the shared RemoteControlPtyLane; existing 335631a4b/58801a729 change input/state setup. Cleanup/persisted-prompt failures are not independently closed. |
| `Antiphon.Tests.Application.SessionMessageQueueGrokPtyIntegrationTests` | [F027](#f027) | ConflictException: Message delivery could not be verified — the terminal did not accept it (the submitting Enter produced no output). See the agent's incidents. | UNRESOLVED: Existing 6befcc97c fixture repair is post-candidate, but no listed branch proves the submitting-Enter-no-output failure closed. |
| `Antiphon.Tests.Application.SessionRuntimeActionTests` | [F003](#f003) | TimeoutException: The operation has timed out. | UNRESOLVED: No matching pushed repair established; native mechanism remains open. |
| `Antiphon.Tests.Checkpoints.BaselineComparerTests` | [F055](#f055), [F056](#f056) | ShouldAssertException: kinds.Single().Kind /     should be / "INHERITED" /     but was / "NEW" /     difference / Difference     \|  \|    \|    \|    \|    \|    \|    \|    \|    \|    /                \| \\|/  \\|/  \\|/  \\|/  \\|/  \\|/  \\|/  \\|/  \\|/   / Index          \| 0    1    2    3    4    5    6    7    8     / Expected Value \| I    N    H    E    R    I    T    E    D     / Actual Value   \| N    E    W                                   / Expected Code  \| 73   78   72   69   82   73   84   69   68   ; ShouldAssertException: kinds.Single(item => item.Name == "N.Kept.bad").Kind /     should be / "INHERITED" /     but was / "NEW" /     difference / Difference     \|  \|    \|    \|    \|    \|    \|    \|    \|    \|    /                \| \\|/  \\|/  \\|/  \\|/  \\|/  \\|/  \\|/  \\|/  \\|/   / Index          \| 0    1    2    3    4    5    6    7    8     / Expected Value \| I    N    H    E    R    I    T    E    D     / Actual Value   \| N    E    W                                   / Expected Code  \| 73   78   72   69   82   73   84   69   68    | NO-REPRO: 7f7aa584 report records unchanged-base Linux passes; no direct repair. Collect native parse/coverage diagnostics; do not infer Windows green. |
| `Antiphon.Tests.Checkpoints.C1307CliCancellationTests` | [F072](#f072), [F073](#f073) | TimeoutException: The operation has timed out. | NO-REPRO: a043b0a4 reran the class green on Linux; no proven link from these timeouts to its read-mode repair. |
| `Antiphon.Tests.Checkpoints.CheckpointRepeatBuildIsolationTests` | [F074](#f074) | DOTNET build EXIT CODE: 0; refused: output-inventory-failed; executed=0; dirty=2; ShouldAssertException: process.ExitCode /     should be / 0 /     but was / 2 /  / Additional Info: /     sequence-repeat-2: BUILD SLOT skipped by -NoSlot /  /   Determining projects to restore... /   Restored C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Checkpoints.FilterHost\Antiphon.Checkpoints.FilterHost.csproj (in 777 ms). /   Antiphon.Checkpoints.FilterHost -> C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Checkpoints.FilterHost\bin-c885-script-c723-01a17998026275038318ceebd208253d-sequence-2\Antiphon.Checkpoints.FilterHost.dll | PARTIAL: a043b0a4 corrects the standalone reader used by inventory, but output-inventory-failed masks the underlying error. Existing 3edae481d isolates repeat outputs; native closure unproven. |
| `Antiphon.Tests.Checkpoints.PlanCoverageBindingTests` | [F065](#f065), [F071](#f071) | ShouldAssertException: CheckpointNativeDiagnostics.Coverage(exit, root, output) /     should be / 0 /     but was / 2 /  / Additional Info: /     c1055-0891; ShouldAssertException: CheckpointNativeDiagnostics.Coverage(firstExit, CheckpointFixtures.RepoRoot, first) /     should be / 0 /     but was / 2 /  / Additional Info: /     c1046-real-plans | NO-REPRO: 7f7aa584 report records unchanged-base Linux passes; no direct repair. Collect native parse/coverage diagnostics; do not infer Windows green. |
| `Antiphon.Tests.Infrastructure.AmServiceDeployReadinessTests` | [F075](#f075) | fatal: path 'scripts/deploy-am-service.ps1' exists on disk, but not in '10fe51b0f668a272d621a935c27e53313b8de1c8'; ShouldAssertException: process.ExitCode /     should be / 0 /     but was / 128 /  / Additional Info: /     fatal: path 'scripts/deploy-am-service.ps1' exists on disk, but not in '10fe51b0f668a272d621a935c27e53313b8de1c8' | UNRESOLVED: No matching pushed repair established; native mechanism remains open. |
| `Antiphon.Tests.Infrastructure.LandingGitTests` | [F138](#f138) | ShouldAssertException: watch.Elapsed /     should be less than / 00:00:15 /     but was / 00:00:30.4189208 | UNRESOLVED: No matching pushed repair established; native mechanism remains open. |
| `Antiphon.Tests.Infrastructure.RepositoryChildRecoveryTests` | [F139](#f139) | ShouldAssertException: raw /     should be / "C:\Users\lndco\AppData\Local\Temp\antiphon-c452-alias-treesk0guhytc.www\real parent\admin" /     but was / "C:/Users/lndco/AppData/Local/Temp/antiphon-c452-alias-treesk0guhytc.www/real parent/admin" /     difference / Difference     \|       \|                             \|                             \|                                       \|         /                \|      \\|/                           \\|/                           \\|/                                     \\|/        / Index          \| ...  2    3    4    5    6    7    8    9    10   11   12   13   14   15   16   17   18   19   20   21   22   ...   / Expected Value \| ...  \    U    s    e    r    s    \    l    n    d    c    o    \    A    p    p    D    a    t    a    \    ...   / Actual Value   \| ...  /    U    s    e    r    s    /    l    n    d    c    o    /    A    p    p    D    a    t    a    /    ...   / Expected Code  \| ...  92   85   115  101  114  115  92   108  110  100  99   111  92   65   112  112  68   97   116  97   92   ...   | UNRESOLVED: No matching pushed repair established; native mechanism remains open. |
| `Antiphon.Tests.Infrastructure.WorktreeRemovalAuthorityTests` | [F077](#f077) | ShouldAssertException: File.Exists(path) /     should be / True /     but was / False /  / Additional Info: /     build cleanup must preserve opaque wildcard-matched bytes | UNRESOLVED: No matching pushed repair established; native mechanism remains open. |
| `Antiphon.Tests.Scripts.C1008PlatformContractTests` | [F084](#f084), [F085](#f085) | c1050-child-source: outer row build stamp required; ShouldAssertException: File.Exists(stampPath) /     should be / True /     but was / False /  / Additional Info: /     c1050-child-source: outer row build stamp required | UNRESOLVED: No matching pushed repair established; native mechanism remains open. |
| `Antiphon.Tests.Scripts.CardRelationshipScriptTests` | [F086](#f086) | ShouldAssertException: run.Stderr /     should contain (case insensitive comparison) / "Relationship route unavailable" /     but was actually / "\u001b[31;1mInvoke-Antiphon: \u001b[0mC:\Antiphon\releases\checkout-997186e6\scripts\card.ps1:795\u001b[0m / \u001b[31;1m..." | UNRESOLVED: No matching pushed repair established; native mechanism remains open. |
| `Antiphon.Tests.Scripts.ChangeAwareReleaseTests` | [F091](#f091) | REFUSED: run-context ownership (origin/wrong-origin).; ShouldAssertException: result.ExitCode /     should be / 0 /     but was / 1 /  / Additional Info: /     [release-candidate] journalled rc/2026-10-19 at db4b4dfb87125f41055e6c2b2dcc7086ae50b50c; not pushed / REFUSED: run-context ownership (origin/wrong-origin). / {"nativeRunId":"","sha":"","ref":"","trigger":"","profile":"","candidateId":"","creditKind":"","localDueDate":"","policyHash":"","coverageComplete":false,"testsPassed":false,"reportDelivered":false,"exitCode":3,"summaryPath":""} / [release-cut] rc run did not complete green; publishing nothing (exit 3). / C1313 hop-summary-count=0 | UNRESOLVED: No matching pushed repair established; native mechanism remains open. |
| `Antiphon.Tests.Scripts.EvidenceDiffGuardTests` | [F082](#f082) | ShouldAssertException: JsonSerializer.Deserialize<string>(encoded) /     should be / ".antiphon/a b	 / é.json" /     but was / ".antiphon/a b	 / �.json" /     difference / Difference     \|                                                                             \|                             /                \|                                                                            \\|/                            / Index          \| 0    1    2    3    4    5    6    7    8    9    10   11   12   13   14   15   16   17   18   19   20    / Expected Value \| .    a    n    t    i    p    h    o    n    /    a    \s   b    \t   \n   é    .    j    s    o    n     | UNRESOLVED: No matching pushed repair established; native mechanism remains open. |
| `Antiphon.Tests.Scripts.RcQualificationStatusTests` | [F092](#f092) | FAIL C1500 input-bytes-and-times-preserved; ShouldAssertException: result.ExitCode /     should be / 0 /     but was / 1 /  / Additional Info: /     PASS C1500 real-entrypoint / FAIL C1500 input-bytes-and-times-preserved -  / PASS C1500 no-action-commands /  / C487: 2 passed, 1 failed, 3 rows | UNRESOLVED: No matching pushed repair established; native mechanism remains open. |
| `Antiphon.Tests.Scripts.ReleaseGateFollowupTests` | [F096](#f096) | Omitted downstream admission failed: Phase=ownership, Refusal=wrong-origin; ShouldAssertException: result.ExitCode /     should be / 0 /     but was / 1 /  / Additional Info: /     C1336 fixture runtime=7.6.9; edition=Core; OS=Windows_NT; separator=\ / PASS C1336 N omitted / \u001b[31;1mException: \u001b[0mC:\Antiphon\releases\checkout-997186e6\tests\fixtures\card1336-release-gate-followups.ps1:214\u001b[0m / \u001b[31;1m\u001b[0m\u001b[36;1mLine \|\u001b[0m / \u001b[31;1m\u001b[0m\u001b[36;1m\u001b[36;1m 214 \| \u001b[0m . de -ne 0) { \u001b[36;1mthrow ('Omitted downstream admission failed: ' + ($run.Re\u001b[0m .\u001b[0m | UNRESOLVED: No matching pushed repair established; native mechanism remains open. |
| `Antiphon.Tests.Scripts.ReleaseGatePlatformTests` | [F093](#f093) | launch-estimate-ceiling id=all-split-1 seconds=31200 ceiling=1800; ShouldAssertException: result.ExitCode /     should be / 0 /     but was / 1 /  / Additional Info: /     [2026-10-26T22:00:09.2474096Z] wrote C:\Users\lndco\AppData\Local\Temp\c1323-nightly-b28b5092fa4d406e9b8bfcf20c271120\execution\linux-direct\summary.json / PASS C1323 linux-launches-only-portable-cases / PASS C1323 linux-summary-is-diagnostic / PASS C1323 linux-census-retains-exclusion-attribution / PASS C1323 optin-and-legacy-method-semantics | UNRESOLVED: No matching pushed repair established; native mechanism remains open. |
| `Antiphon.Tests.Scripts.ReleaseGateRunTests` | [F087](#f087), [F088](#f088) | REFUSED: run-context ownership (origin/wrong-origin).; ShouldAssertException: result.ExitCode /     should be / 0 /     but was / 1 /  / Additional Info: /     REFUSED: unknown profile turbo. / {"nativeRunId":"","sha":"","ref":"","trigger":"","profile":"","candidateId":"","creditKind":"","localDueDate":"","policyHash":"","coverageComplete":false,"testsPassed":false,"reportDelivered":false,"exitCode":3,"summaryPath":""} / PASS C599 ParameterHops unknown profile refuses at the core / REFUSED: rc profile needs an rc/YYYY-MM-DD ref or a legacy release/rc-<stamp> (ref-shape). / {"nativeRunId":"","sha":"","ref":"","trigger":"","profile":"","candidateId":"","creditKind":"","localDueDate":"","policyHash":"","coverageComplete":false,"testsPassed":false,"reportDelivered":false,"exitCode":3,"summaryPath":""}; REFUSED: run-context ownership (origin/wrong-origin).; ShouldAssertException: result.ExitCode /     should be / 0 /     but was / 1 /  / Additional Info: /     PASS C599 MasterStateIsolation run starts without child summaries / REFUSED: run-context ownership (origin/wrong-origin). / {"nativeRunId":"","sha":"","ref":"","trigger":"","profile":"","candidateId":"","creditKind":"","localDueDate":"","policyHash":"","coverageComplete":false,"testsPassed":false,"reportDelivered":false,"exitCode":3,"summaryPath":""} / FAIL C599 MasterStateIsolation outer run reaches self-reexec without refusal - phase=ownership refusal=wrong-origin exit=3 / FAIL C599 MasterStateIsolation exactly one fresh child summary names this run - summaries=0 | UNRESOLVED: No matching pushed repair established; native mechanism remains open. |
| `Antiphon.Tests.Scripts.RemoteScriptContractTests` | [F109](#f109), [F110](#f110), [F111](#f111), [F113](#f113), [F114](#f114), [F115](#f115) | "1"; "0"; Additional Info:; "FixtureNpmNetworkFailed"; "FixtureNpmOfflineSetupFailed"; Additional Info:; Additional Info:; primary fixture: /bin/bash: C:UserslndcoAppDataLocalTempc1361-dle5lwht.g5gprobe.sh: No such file or directory; Additional Info:; primary fixture: /bin/bash: C:UserslndcoAppDataLocalTempc1361-gxcid1rz.ro1probe.sh: No such file or directory | NO-REPRO: 0d5de8f1 report records Linux baseline/targeted passes; the permission-model repair is not proof of these distinct Windows signatures. UNRESOLVED: No matching pushed repair established; native mechanism remains open. |
| `Antiphon.Tests.Scripts.RollingVolumeRecycleScriptTests` | [F078](#f078), [F079](#f079), [F080](#f080), [F081](#f081) | ShouldAssertException: run.Exit /     should be / 0 /     but was / 127 /  / Additional Info: /     /bin/bash: C:UserslndcoAppDataLocalTempc1361-3kt1xehc.22bprobe.sh: No such file or directory; RECYCLE_LAND_WAIT runner=server2 id=11111111-1111-1111-1111-111111111111 polls=0; ShouldAssertException: waitLines /     should be / 1 /     but was / 0 /  / Additional Info: /     clears: one wait line; RECYCLE_LAND_WAIT runner=server2 id=11111111-1111-1111-1111-111111111111 polls=0 / \u001b[33;1mWARNING: Resulting JSON is truncated as serialization has exceeded the set depth of 2.\u001b[0m / Rolling deploy complete: C:\Antiphon\releases\checkout-997186e6\.antiphon\rolling-server2\c727ed0ce7203a6e; ShouldAssertException: failures /     should be empty but had / 1 /     item and was / ["host: run.Exit /     should be / 0 /     but was / 127 /  / Additional Info: /     collected/open/-1/True fixture: /bin/bash: C:UserslndcoAppDataLocalTempc1361-wzdxusfw.gznprobe.sh: No such file or directory; ShouldAssertException: failures /     should be empty but had / 1 /     item and was / ["host: run.Exit /     should be / 0 /     but was / 127 /  / Additional Info: /     fixture: /bin/bash: C:UserslndcoAppDataLocalTempc1361-1nhweisp.qabprobe.sh: No such file or directory | UNRESOLVED: No matching pushed repair established; native mechanism remains open. |
| `Antiphon.Tests.Scripts.RunCheckpointRepeatScriptTests` | [F089](#f089), [F090](#f090) | reason=source_dirty; dirty=2; sourceState=unknown; executed=0; ShouldAssertException: reused.ExitCode /     should be / 0 /     but was / 2 /  / Additional Info: /     CHECKPOINT CP-1 commit=105792f7d22f297746cdb338976f52e424b76fa7 build=n/a filter=/*/*/SampleTests*/* executed=0 passed=0 failed=0 skipped=0 trx=n/a slot=skipped waited=0s dirty=2 source=105792f7d22f297746cdb338976f52e424b76fa7+dirty:c21323dc003df40740ec666ec6bb69b92d164aa4812095dfa8dd14e17e3a53dc sourceState=unknown buildSource=unknown repeat=5 repetitions=0/5 hostInvocations=1 reason=source_dirty / CHECKPOINT CP-1 EXIT CODE: 2 / ; CHECKPOINT SOURCE INVALID reason=source_ineligible; ShouldAssertException: valid.ExitCode /     should be / 0 /     but was / 2 /  / Additional Info: /     CHECKPOINT SOURCE INVALID reason=source_ineligible /  | UNRESOLVED: No matching pushed repair established; native mechanism remains open. |
| `Antiphon.Tests.Scripts.ScriptHarnessWindowsOwnershipTests` | [F083](#f083) | InvalidOperationException: Root exited before handle probe. | UNRESOLVED: No matching pushed repair established; native mechanism remains open. |
| `Antiphon.Tests.Scripts.WindowsUnitPlatformAdmissionTests` | [F095](#f095) | c1210-child-source-stamp: clean source; source.DirtyFiles expected 0, was 2; ShouldAssertException: source.DirtyFiles /     should be / 0 /     but was / 2 /  / Additional Info: /     c1210-child-source-stamp: clean source | UNRESOLVED: No matching pushed repair established; native mechanism remains open. |

## Every recorded failure, grouped by probable root cause

Catalog cardinality: 140 expanded instances; 137 from all 329 TRX, 3 recovered from group-65 diagnostic Error states. Argument variants retain their display names and separate identities. Probable groups do not replace the narrower certainty statement in each disposition.

### A. Verified checkpoint content reads stall before the first byte (17 results)

Confirmed boundary: 17 `receipt-read-refused` errors wrap `read-stalled: 0 of N bytes; no progress within 3s`. Stored stacks enter ReceiptFile.Digest / CheckpointWrite.RecordContent. Candidate `src/Antiphon.HostCleanup/Checkpoints/CheckpointFileReader.cs:71` starts its stall window and `:102` waits on ReadAsync; SafeWrite.cs:88 records created content. Branch a043b0a4 corrects synchronous Windows handle mode in both readers (281542f75b74848819ac2340402ff9e2167ec5bd), with portable red/green proof. Historical Windows worker starvation versus filesystem latency remains unmeasured; the original run is not closure evidence.

<a id="f044"></a>

#### F044 — `Antiphon.Tests.Checkpoints.CheckpointRecoveryWindowsTests.detached_staged_copy_survives_starter_exit`

- Display: `detached_staged_copy_survives_starter_exit`
- Suite/chunk: `antiphon/all-group-61`; duration `00:00:09.1919825`; manifest row 61.
- Evidence: `antiphon-all-group-61.trx:1267`; test identity `bc974a26-0c8b-e114-28cc-e9f0e8ff9620`; execution ID `4cefcea0-cf3d-4793-9ded-8a2eb18144f9`.
- Disposition: **ADDRESSED** — a043b0a4 / 281542f75b74848819ac2340402ff9e2167ec5bd: overlapped Windows handle mode; portable proof, native closure pending.

Error, first 12 lines (all lines if shorter):

```text
IOException: receipt-read-refused: C:\Users\lndco\AppData\Local\Temp\c723-01a1798bc8ac7120ade26b2524aaf5bc\staged\Antiphon.Tests.dll: read-stalled: 0 of 68985344 bytes; no progress within 3s
```


Top six stored stack frames (all if fewer):

```text
   at Antiphon.Checkpoints.ReceiptFile.Digest(CheckpointCleanupContext context, String path, CancellationToken cancellationToken) in C:\Antiphon\releases\checkout-997186e6\src\Antiphon.HostCleanup\Checkpoints\ReceiptFile.cs:line 39
   at Antiphon.Checkpoints.CheckpointWrite.Describe(String path) in C:\Antiphon\releases\checkout-997186e6\src\Antiphon.HostCleanup\Checkpoints\SafeWrite.cs:line 92
   at Antiphon.Checkpoints.CheckpointWrite.RecordContent(String path) in C:\Antiphon\releases\checkout-997186e6\src\Antiphon.HostCleanup\Checkpoints\SafeWrite.cs:line 88
   at Antiphon.Checkpoints.CheckpointWrite.CopyOverwrite(String source, String destination, WriteOwnership ownership) in C:\Antiphon\releases\checkout-997186e6\src\Antiphon.HostCleanup\Checkpoints\SafeWrite.cs:line 273
   at Antiphon.Checkpoints.ShadowCopy.CopyFiles(CheckpointCleanupContext context, String source, String destination, WriteOwnership ownership) in C:\Antiphon\releases\checkout-997186e6\tools\Antiphon.Checkpoints\Execution\ShadowCopy.cs:line 31
   at Antiphon.Checkpoints.ShadowCopy.CopyToolOutput(String toolDirectory, String destination, CheckpointCleanupContext context) in C:\Antiphon\releases\checkout-997186e6\tools\Antiphon.Checkpoints\Execution\ShadowCopy.cs:line 17
```


<a id="f050"></a>

#### F050 — `Antiphon.Tests.Checkpoints.CheckpointBuildBindingPathTests.Producer_stamp_is_accepted_by_reconstructed_audit_path`

- Display: `Producer_stamp_is_accepted_by_reconstructed_audit_path`
- Suite/chunk: `antiphon/all-group-62`; duration `00:00:03.1015974`; manifest row 62.
- Evidence: `antiphon-all-group-62.trx:98`; test identity `3a56c4c0-b8d9-27f6-70fd-46a6790c0ab4`; execution ID `61a59bfe-f1de-40d2-bd1d-de99adc791e6`.
- Disposition: **ADDRESSED** — a043b0a4 / 281542f75b74848819ac2340402ff9e2167ec5bd: overlapped Windows handle mode; portable proof, native closure pending.

Error, first 12 lines (all lines if shorter):

```text
IOException: receipt-read-refused: C:\Users\lndco\AppData\Local\Temp\c1210-binding-3lsjrofx.h52\tests\bin-c1210-producer\.checkpoint-output-owner.json: read-stalled: 0 of 32 bytes; no progress within 3s
```


Top six stored stack frames (all if fewer):

```text
   at Antiphon.Checkpoints.ReceiptFile.Digest(CheckpointCleanupContext context, String path, CancellationToken cancellationToken) in C:\Antiphon\releases\checkout-997186e6\src\Antiphon.HostCleanup\Checkpoints\ReceiptFile.cs:line 39
   at Antiphon.Checkpoints.CheckpointWrite.Describe(String path) in C:\Antiphon\releases\checkout-997186e6\src\Antiphon.HostCleanup\Checkpoints\SafeWrite.cs:line 92
   at Antiphon.Checkpoints.CheckpointWrite.RecordContent(String path) in C:\Antiphon\releases\checkout-997186e6\src\Antiphon.HostCleanup\Checkpoints\SafeWrite.cs:line 88
   at Antiphon.Checkpoints.CheckpointWrite.Text(String path, String content, WriteOwnership ownership) in C:\Antiphon\releases\checkout-997186e6\src\Antiphon.HostCleanup\Checkpoints\SafeWrite.cs:line 191
   at Antiphon.Checkpoints.CheckpointWrite.CreateFreshDirectory(String path, WriteOwnership ownership) in C:\Antiphon\releases\checkout-997186e6\src\Antiphon.HostCleanup\Checkpoints\SafeWrite.cs:line 323
   at Antiphon.Checkpoints.SafeWrite.CreateFreshDirectory(String path, WriteOwnership ownership) in C:\Antiphon\releases\checkout-997186e6\src\Antiphon.HostCleanup\Checkpoints\SafeWrite.cs:line 51
```


<a id="f051"></a>

#### F051 — `Antiphon.Tests.Checkpoints.CheckpointBuildBindingPathTests.Equivalent_directory_spellings_preserve_binding`

- Display: `Equivalent_directory_spellings_preserve_binding`
- Suite/chunk: `antiphon/all-group-62`; duration `00:00:03.6661335`; manifest row 62.
- Evidence: `antiphon-all-group-62.trx:123`; test identity `6ab78541-e6fa-3e4f-0405-f4755232398b`; execution ID `db2fa8d5-216c-40d3-9b33-16a1adb6f7e3`.
- Disposition: **ADDRESSED** — a043b0a4 / 281542f75b74848819ac2340402ff9e2167ec5bd: overlapped Windows handle mode; portable proof, native closure pending.

Error, first 12 lines (all lines if shorter):

```text
IOException: receipt-read-refused: C:\Users\lndco\AppData\Local\Temp\c1210-binding-a2xrkjme.znj\tests\bin-c1210-producer\.checkpoint-output-owner.json: read-stalled: 0 of 32 bytes; no progress within 3s
```


Top six stored stack frames (all if fewer):

```text
   at Antiphon.Checkpoints.ReceiptFile.Digest(CheckpointCleanupContext context, String path, CancellationToken cancellationToken) in C:\Antiphon\releases\checkout-997186e6\src\Antiphon.HostCleanup\Checkpoints\ReceiptFile.cs:line 39
   at Antiphon.Checkpoints.CheckpointWrite.Describe(String path) in C:\Antiphon\releases\checkout-997186e6\src\Antiphon.HostCleanup\Checkpoints\SafeWrite.cs:line 92
   at Antiphon.Checkpoints.CheckpointWrite.RecordContent(String path) in C:\Antiphon\releases\checkout-997186e6\src\Antiphon.HostCleanup\Checkpoints\SafeWrite.cs:line 88
   at Antiphon.Checkpoints.CheckpointWrite.Text(String path, String content, WriteOwnership ownership) in C:\Antiphon\releases\checkout-997186e6\src\Antiphon.HostCleanup\Checkpoints\SafeWrite.cs:line 191
   at Antiphon.Checkpoints.CheckpointWrite.CreateFreshDirectory(String path, WriteOwnership ownership) in C:\Antiphon\releases\checkout-997186e6\src\Antiphon.HostCleanup\Checkpoints\SafeWrite.cs:line 323
   at Antiphon.Checkpoints.SafeWrite.CreateFreshDirectory(String path, WriteOwnership ownership) in C:\Antiphon\releases\checkout-997186e6\src\Antiphon.HostCleanup\Checkpoints\SafeWrite.cs:line 51
```


<a id="f052"></a>

#### F052 — `Antiphon.Tests.Checkpoints.CheckpointAppTests.create_run_uses_the_manifest_results_root`

- Display: `create_run_uses_the_manifest_results_root`
- Suite/chunk: `antiphon/all-group-62`; duration `00:00:03.6677449`; manifest row 62.
- Evidence: `antiphon-all-group-62.trx:148`; test identity `942c6bb8-db0f-029e-742c-b6997c93c326`; execution ID `2741a6bc-ccdc-4f7c-840e-f28c9f493407`.
- Disposition: **ADDRESSED** — a043b0a4 / 281542f75b74848819ac2340402ff9e2167ec5bd: overlapped Windows handle mode; portable proof, native closure pending.

Error, first 12 lines (all lines if shorter):

```text
IOException: receipt-read-refused: C:\Users\lndco\AppData\Local\Temp\c723-01a17990f5af7fa6a0f67d24bae52ad8\.antiphon\custom-root\20261026-185505-0faf\.checkpoint-output-owner.json: read-stalled: 0 of 32 bytes; no progress within 3s
```


Top six stored stack frames (all if fewer):

```text
   at Antiphon.Checkpoints.ReceiptFile.Digest(CheckpointCleanupContext context, String path, CancellationToken cancellationToken) in C:\Antiphon\releases\checkout-997186e6\src\Antiphon.HostCleanup\Checkpoints\ReceiptFile.cs:line 39
   at Antiphon.Checkpoints.CheckpointWrite.Describe(String path) in C:\Antiphon\releases\checkout-997186e6\src\Antiphon.HostCleanup\Checkpoints\SafeWrite.cs:line 92
   at Antiphon.Checkpoints.CheckpointWrite.RecordContent(String path) in C:\Antiphon\releases\checkout-997186e6\src\Antiphon.HostCleanup\Checkpoints\SafeWrite.cs:line 88
   at Antiphon.Checkpoints.CheckpointWrite.Text(String path, String content, WriteOwnership ownership) in C:\Antiphon\releases\checkout-997186e6\src\Antiphon.HostCleanup\Checkpoints\SafeWrite.cs:line 191
   at Antiphon.Checkpoints.CheckpointWrite.CreateFreshDirectory(String path, WriteOwnership ownership) in C:\Antiphon\releases\checkout-997186e6\src\Antiphon.HostCleanup\Checkpoints\SafeWrite.cs:line 323
   at Antiphon.Checkpoints.CheckpointApp.CreateRun(CheckpointManifest manifest, RunRequest request, String repo, Runtime runtime) in C:\Antiphon\releases\checkout-997186e6\tools\Antiphon.Checkpoints\CheckpointApp.cs:line 568
```


<a id="f053"></a>

#### F053 — `Antiphon.Tests.Checkpoints.CheckpointBuildBindingPathTests.Different_directory_or_provenance_is_refused`

- Display: `Different_directory_or_provenance_is_refused`
- Suite/chunk: `antiphon/all-group-62`; duration `00:00:03.5963944`; manifest row 62.
- Evidence: `antiphon-all-group-62.trx:175`; test identity `29ee6426-99b0-d88a-96f0-75800aba1825`; execution ID `ee820c75-8346-4cad-af38-45ae14796dfd`.
- Disposition: **ADDRESSED** — a043b0a4 / 281542f75b74848819ac2340402ff9e2167ec5bd: overlapped Windows handle mode; portable proof, native closure pending.

Error, first 12 lines (all lines if shorter):

```text
IOException: receipt-read-refused: C:\Users\lndco\AppData\Local\Temp\c1210-binding-ed3eerha.mdm\tests\bin-c1210-producer\.checkpoint-output-owner.json: read-stalled: 0 of 32 bytes; no progress within 3s
```


Top six stored stack frames (all if fewer):

```text
   at Antiphon.Checkpoints.ReceiptFile.Digest(CheckpointCleanupContext context, String path, CancellationToken cancellationToken) in C:\Antiphon\releases\checkout-997186e6\src\Antiphon.HostCleanup\Checkpoints\ReceiptFile.cs:line 39
   at Antiphon.Checkpoints.CheckpointWrite.Describe(String path) in C:\Antiphon\releases\checkout-997186e6\src\Antiphon.HostCleanup\Checkpoints\SafeWrite.cs:line 92
   at Antiphon.Checkpoints.CheckpointWrite.RecordContent(String path) in C:\Antiphon\releases\checkout-997186e6\src\Antiphon.HostCleanup\Checkpoints\SafeWrite.cs:line 88
   at Antiphon.Checkpoints.CheckpointWrite.Text(String path, String content, WriteOwnership ownership) in C:\Antiphon\releases\checkout-997186e6\src\Antiphon.HostCleanup\Checkpoints\SafeWrite.cs:line 191
   at Antiphon.Checkpoints.CheckpointWrite.CreateFreshDirectory(String path, WriteOwnership ownership) in C:\Antiphon\releases\checkout-997186e6\src\Antiphon.HostCleanup\Checkpoints\SafeWrite.cs:line 323
   at Antiphon.Checkpoints.SafeWrite.CreateFreshDirectory(String path, WriteOwnership ownership) in C:\Antiphon\releases\checkout-997186e6\src\Antiphon.HostCleanup\Checkpoints\SafeWrite.cs:line 51
```


<a id="f054"></a>

#### F054 — `Antiphon.Tests.Checkpoints.CheckpointAdmissionContractTests.C1531_receipts_keep_wait_execution_and_counts`

- Display: `C1531_receipts_keep_wait_execution_and_counts`
- Suite/chunk: `antiphon/all-group-62`; duration `00:00:03.7098879`; manifest row 62.
- Evidence: `antiphon-all-group-62.trx:239`; test identity `eca1db75-9840-61a3-aca5-0d436037b9c8`; execution ID `72a2efca-322e-423e-9316-30b873fcf1e6`.
- Disposition: **ADDRESSED** — a043b0a4 / 281542f75b74848819ac2340402ff9e2167ec5bd: overlapped Windows handle mode; portable proof, native closure pending.

Error, first 12 lines (all lines if shorter):

```text
IOException: receipt-read-refused: C:\Users\lndco\AppData\Local\Temp\c723-01a17990f5967484b6bfe9c14172c69d\.antiphon\checkpoints\20261026-185505-7f08\.checkpoint-output-owner.json: read-stalled: 0 of 32 bytes; no progress within 3s
```


Top six stored stack frames (all if fewer):

```text
   at Antiphon.Checkpoints.ReceiptFile.Digest(CheckpointCleanupContext context, String path, CancellationToken cancellationToken) in C:\Antiphon\releases\checkout-997186e6\src\Antiphon.HostCleanup\Checkpoints\ReceiptFile.cs:line 39
   at Antiphon.Checkpoints.CheckpointWrite.Describe(String path) in C:\Antiphon\releases\checkout-997186e6\src\Antiphon.HostCleanup\Checkpoints\SafeWrite.cs:line 92
   at Antiphon.Checkpoints.CheckpointWrite.RecordContent(String path) in C:\Antiphon\releases\checkout-997186e6\src\Antiphon.HostCleanup\Checkpoints\SafeWrite.cs:line 88
   at Antiphon.Checkpoints.CheckpointWrite.Text(String path, String content, WriteOwnership ownership) in C:\Antiphon\releases\checkout-997186e6\src\Antiphon.HostCleanup\Checkpoints\SafeWrite.cs:line 191
   at Antiphon.Checkpoints.CheckpointWrite.CreateFreshDirectory(String path, WriteOwnership ownership) in C:\Antiphon\releases\checkout-997186e6\src\Antiphon.HostCleanup\Checkpoints\SafeWrite.cs:line 323
   at Antiphon.Checkpoints.CheckpointApp.CreateRun(CheckpointManifest manifest, RunRequest request, String repo, Runtime runtime) in C:\Antiphon\releases\checkout-997186e6\tools\Antiphon.Checkpoints\CheckpointApp.cs:line 568
```


<a id="f059"></a>

#### F059 — `Antiphon.Tests.Checkpoints.C1307CliCancellationTests.Cancel_before_request_read_is_not_crash`

- Display: `Cancel_before_request_read_is_not_crash`
- Suite/chunk: `antiphon/all-group-63`; duration `00:00:04.4720216`; manifest row 63.
- Evidence: `antiphon-all-group-63.trx:89`; test identity `2d9dddc3-a520-8f8e-7da2-9ffc5600a033`; execution ID `72ad71b7-5413-46a1-b68d-c2e932a37721`.
- Disposition: **ADDRESSED** — a043b0a4 / 281542f75b74848819ac2340402ff9e2167ec5bd: overlapped Windows handle mode; portable proof, native closure pending.

Error, first 12 lines (all lines if shorter):

```text
IOException: receipt-read-refused: C:\Users\lndco\AppData\Local\Temp\c723-01a17997e73270c3918f020eb706032f\.antiphon\checkpoints\20261026-190240-2482\.checkpoint-output-owner.json: read-stalled: 0 of 32 bytes; no progress within 3s
```


Top six stored stack frames (all if fewer):

```text
   at Antiphon.Checkpoints.ReceiptFile.Digest(CheckpointCleanupContext context, String path, CancellationToken cancellationToken) in C:\Antiphon\releases\checkout-997186e6\src\Antiphon.HostCleanup\Checkpoints\ReceiptFile.cs:line 39
   at Antiphon.Checkpoints.CheckpointWrite.Describe(String path) in C:\Antiphon\releases\checkout-997186e6\src\Antiphon.HostCleanup\Checkpoints\SafeWrite.cs:line 92
   at Antiphon.Checkpoints.CheckpointWrite.RecordContent(String path) in C:\Antiphon\releases\checkout-997186e6\src\Antiphon.HostCleanup\Checkpoints\SafeWrite.cs:line 88
   at Antiphon.Checkpoints.CheckpointWrite.Text(String path, String content, WriteOwnership ownership) in C:\Antiphon\releases\checkout-997186e6\src\Antiphon.HostCleanup\Checkpoints\SafeWrite.cs:line 191
   at Antiphon.Checkpoints.CheckpointWrite.CreateFreshDirectory(String path, WriteOwnership ownership) in C:\Antiphon\releases\checkout-997186e6\src\Antiphon.HostCleanup\Checkpoints\SafeWrite.cs:line 323
   at Antiphon.Checkpoints.CheckpointApp.CreateRun(CheckpointManifest manifest, RunRequest request, String repo, Runtime runtime) in C:\Antiphon\releases\checkout-997186e6\tools\Antiphon.Checkpoints\CheckpointApp.cs:line 568
```


<a id="f060"></a>

#### F060 — `Antiphon.Tests.Checkpoints.C1307CliCancellationTests.Executor_cancel_persists_130`

- Display: `Executor_cancel_persists_130`
- Suite/chunk: `antiphon/all-group-63`; duration `00:00:04.4719908`; manifest row 63.
- Evidence: `antiphon-all-group-63.trx:117`; test identity `b973cbcc-95bc-5e9f-6fa9-7da7d72d70fa`; execution ID `2b13a7f5-e0ea-4a5f-abac-d49ad0b26a57`.
- Disposition: **ADDRESSED** — a043b0a4 / 281542f75b74848819ac2340402ff9e2167ec5bd: overlapped Windows handle mode; portable proof, native closure pending.

Error, first 12 lines (all lines if shorter):

```text
IOException: receipt-read-refused: C:\Users\lndco\AppData\Local\Temp\c723-01a17997e733761e989cecf1df8a6b12\.antiphon\checkpoints\20261026-190240-c874\.checkpoint-output-owner.json: read-stalled: 0 of 32 bytes; no progress within 3s
```


Top six stored stack frames (all if fewer):

```text
   at Antiphon.Checkpoints.ReceiptFile.Digest(CheckpointCleanupContext context, String path, CancellationToken cancellationToken) in C:\Antiphon\releases\checkout-997186e6\src\Antiphon.HostCleanup\Checkpoints\ReceiptFile.cs:line 39
   at Antiphon.Checkpoints.CheckpointWrite.Describe(String path) in C:\Antiphon\releases\checkout-997186e6\src\Antiphon.HostCleanup\Checkpoints\SafeWrite.cs:line 92
   at Antiphon.Checkpoints.CheckpointWrite.RecordContent(String path) in C:\Antiphon\releases\checkout-997186e6\src\Antiphon.HostCleanup\Checkpoints\SafeWrite.cs:line 88
   at Antiphon.Checkpoints.CheckpointWrite.Text(String path, String content, WriteOwnership ownership) in C:\Antiphon\releases\checkout-997186e6\src\Antiphon.HostCleanup\Checkpoints\SafeWrite.cs:line 191
   at Antiphon.Checkpoints.CheckpointWrite.CreateFreshDirectory(String path, WriteOwnership ownership) in C:\Antiphon\releases\checkout-997186e6\src\Antiphon.HostCleanup\Checkpoints\SafeWrite.cs:line 323
   at Antiphon.Checkpoints.CheckpointApp.CreateRun(CheckpointManifest manifest, RunRequest request, String repo, Runtime runtime) in C:\Antiphon\releases\checkout-997186e6\tools\Antiphon.Checkpoints\CheckpointApp.cs:line 568
```


<a id="f061"></a>

#### F061 — `Antiphon.Tests.Checkpoints.EvidenceFolderTests.rerun_text_is_reset_for_each_row`

- Display: `rerun_text_is_reset_for_each_row`
- Suite/chunk: `antiphon/all-group-63`; duration `00:00:03.8670928`; manifest row 63.
- Evidence: `antiphon-all-group-63.trx:152`; test identity `616b372c-4157-51fd-0858-8c3dfbf55b21`; execution ID `9da3cb03-11fe-445f-842e-1bc8bcab92c6`.
- Disposition: **ADDRESSED** — a043b0a4 / 281542f75b74848819ac2340402ff9e2167ec5bd: overlapped Windows handle mode; portable proof, native closure pending.

Error, first 12 lines (all lines if shorter):

```text
IOException: receipt-read-refused: C:\Users\lndco\AppData\Local\Temp\c723-01a17997e990766f8c59def8fd460e13\rows\CP-2\failures.md: read-stalled: 0 of 467 bytes; no progress within 3s
```


Top six stored stack frames (all if fewer):

```text
   at Antiphon.Checkpoints.ReceiptFile.Digest(CheckpointCleanupContext context, String path, CancellationToken cancellationToken) in C:\Antiphon\releases\checkout-997186e6\src\Antiphon.HostCleanup\Checkpoints\ReceiptFile.cs:line 39
   at Antiphon.Checkpoints.CheckpointWrite.Describe(String path) in C:\Antiphon\releases\checkout-997186e6\src\Antiphon.HostCleanup\Checkpoints\SafeWrite.cs:line 92
   at Antiphon.Checkpoints.CheckpointWrite.RecordContent(String path) in C:\Antiphon\releases\checkout-997186e6\src\Antiphon.HostCleanup\Checkpoints\SafeWrite.cs:line 88
   at Antiphon.Checkpoints.CheckpointWrite.Text(String path, String content, WriteOwnership ownership) in C:\Antiphon\releases\checkout-997186e6\src\Antiphon.HostCleanup\Checkpoints\SafeWrite.cs:line 191
   at Antiphon.Checkpoints.EvidenceFolder.Write(String runDirectory, ReportModel model, Boolean removeToolCopy, String imageDirectory, CheckpointCleanupContext context) in C:\Antiphon\releases\checkout-997186e6\tools\Antiphon.Checkpoints\Evidence\EvidenceFolder.cs:line 47
   at Antiphon.Tests.Checkpoints.EvidenceFolderTests.rerun_text_is_reset_for_each_row() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Checkpoints\EvidenceFolderTests.cs:line 80
```


<a id="f062"></a>

#### F062 — `Antiphon.Tests.Checkpoints.CheckpointToolSourceTests.tiny_source_is_really_copied_to_the_launched_image`

- Display: `tiny_source_is_really_copied_to_the_launched_image`
- Suite/chunk: `antiphon/all-group-63`; duration `00:00:04.4727286`; manifest row 63.
- Evidence: `antiphon-all-group-63.trx:177`; test identity `adf5afbe-b582-7c2a-2127-d70fa8f7f549`; execution ID `05205f89-bd46-46f4-b901-f374555072e9`.
- Disposition: **ADDRESSED** — a043b0a4 / 281542f75b74848819ac2340402ff9e2167ec5bd: overlapped Windows handle mode; portable proof, native closure pending.

Error, first 12 lines (all lines if shorter):

```text
IOException: receipt-read-refused: C:\Users\lndco\AppData\Local\Temp\c723-01a17997e7317f27985ae74a286d50f8\.antiphon\checkpoints\20261026-190240-6f2e\.checkpoint-output-owner.json: read-stalled: 0 of 32 bytes; no progress within 3s
```


Top six stored stack frames (all if fewer):

```text
   at Antiphon.Checkpoints.ReceiptFile.Digest(CheckpointCleanupContext context, String path, CancellationToken cancellationToken) in C:\Antiphon\releases\checkout-997186e6\src\Antiphon.HostCleanup\Checkpoints\ReceiptFile.cs:line 39
   at Antiphon.Checkpoints.CheckpointWrite.Describe(String path) in C:\Antiphon\releases\checkout-997186e6\src\Antiphon.HostCleanup\Checkpoints\SafeWrite.cs:line 92
   at Antiphon.Checkpoints.CheckpointWrite.RecordContent(String path) in C:\Antiphon\releases\checkout-997186e6\src\Antiphon.HostCleanup\Checkpoints\SafeWrite.cs:line 88
   at Antiphon.Checkpoints.CheckpointWrite.Text(String path, String content, WriteOwnership ownership) in C:\Antiphon\releases\checkout-997186e6\src\Antiphon.HostCleanup\Checkpoints\SafeWrite.cs:line 191
   at Antiphon.Checkpoints.CheckpointWrite.CreateFreshDirectory(String path, WriteOwnership ownership) in C:\Antiphon\releases\checkout-997186e6\src\Antiphon.HostCleanup\Checkpoints\SafeWrite.cs:line 323
   at Antiphon.Checkpoints.CheckpointApp.CreateRun(CheckpointManifest manifest, RunRequest request, String repo, Runtime runtime) in C:\Antiphon\releases\checkout-997186e6\tools\Antiphon.Checkpoints\CheckpointApp.cs:line 568
```


<a id="f063"></a>

#### F063 — `Antiphon.Tests.Checkpoints.C1307CliCancellationTests.No_caller_cancel_preserves_existing_results`

- Display: `No_caller_cancel_preserves_existing_results`
- Suite/chunk: `antiphon/all-group-63`; duration `00:00:04.4720261`; manifest row 63.
- Evidence: `antiphon-all-group-63.trx:216`; test identity `380861a3-d3d4-e68b-3f27-0ded83820a7e`; execution ID `27139c88-63ed-4293-a7dc-199bd1db268f`.
- Disposition: **ADDRESSED** — a043b0a4 / 281542f75b74848819ac2340402ff9e2167ec5bd: overlapped Windows handle mode; portable proof, native closure pending.

Error, first 12 lines (all lines if shorter):

```text
IOException: receipt-read-refused: C:\Users\lndco\AppData\Local\Temp\c723-01a17997e7327d0eb5f72641ea0046ec\.antiphon\checkpoints\20261026-190240-c26e\.checkpoint-output-owner.json: read-stalled: 0 of 32 bytes; no progress within 3s
```


Top six stored stack frames (all if fewer):

```text
   at Antiphon.Checkpoints.ReceiptFile.Digest(CheckpointCleanupContext context, String path, CancellationToken cancellationToken) in C:\Antiphon\releases\checkout-997186e6\src\Antiphon.HostCleanup\Checkpoints\ReceiptFile.cs:line 39
   at Antiphon.Checkpoints.CheckpointWrite.Describe(String path) in C:\Antiphon\releases\checkout-997186e6\src\Antiphon.HostCleanup\Checkpoints\SafeWrite.cs:line 92
   at Antiphon.Checkpoints.CheckpointWrite.RecordContent(String path) in C:\Antiphon\releases\checkout-997186e6\src\Antiphon.HostCleanup\Checkpoints\SafeWrite.cs:line 88
   at Antiphon.Checkpoints.CheckpointWrite.Text(String path, String content, WriteOwnership ownership) in C:\Antiphon\releases\checkout-997186e6\src\Antiphon.HostCleanup\Checkpoints\SafeWrite.cs:line 191
   at Antiphon.Checkpoints.CheckpointWrite.CreateFreshDirectory(String path, WriteOwnership ownership) in C:\Antiphon\releases\checkout-997186e6\src\Antiphon.HostCleanup\Checkpoints\SafeWrite.cs:line 323
   at Antiphon.Checkpoints.CheckpointApp.CreateRun(CheckpointManifest manifest, RunRequest request, String repo, Runtime runtime) in C:\Antiphon\releases\checkout-997186e6\tools\Antiphon.Checkpoints\CheckpointApp.cs:line 568
```


<a id="f064"></a>

#### F064 — `Antiphon.Tests.Checkpoints.EvidenceFolderTests.red_run_writes_failures_with_message_stack_stdout_and_commands`

- Display: `red_run_writes_failures_with_message_stack_stdout_and_commands`
- Suite/chunk: `antiphon/all-group-63`; duration `00:00:04.3975427`; manifest row 63.
- Evidence: `antiphon-all-group-63.trx:250`; test identity `8a16550e-014f-b30e-ac35-7bc8e38077fe`; execution ID `37673d7d-71f7-49eb-b92e-598e3fdd58f2`.
- Disposition: **ADDRESSED** — a043b0a4 / 281542f75b74848819ac2340402ff9e2167ec5bd: overlapped Windows handle mode; portable proof, native closure pending.

Error, first 12 lines (all lines if shorter):

```text
IOException: receipt-read-refused: C:\Users\lndco\AppData\Local\Temp\c723-01a17997e77a7145b5c483a7e0bb3ab6\rows\CP-2\failures.md: read-stalled: 0 of 467 bytes; no progress within 3s
```


Top six stored stack frames (all if fewer):

```text
   at Antiphon.Checkpoints.ReceiptFile.Digest(CheckpointCleanupContext context, String path, CancellationToken cancellationToken) in C:\Antiphon\releases\checkout-997186e6\src\Antiphon.HostCleanup\Checkpoints\ReceiptFile.cs:line 39
   at Antiphon.Checkpoints.CheckpointWrite.Describe(String path) in C:\Antiphon\releases\checkout-997186e6\src\Antiphon.HostCleanup\Checkpoints\SafeWrite.cs:line 92
   at Antiphon.Checkpoints.CheckpointWrite.RecordContent(String path) in C:\Antiphon\releases\checkout-997186e6\src\Antiphon.HostCleanup\Checkpoints\SafeWrite.cs:line 88
   at Antiphon.Checkpoints.CheckpointWrite.Text(String path, String content, WriteOwnership ownership) in C:\Antiphon\releases\checkout-997186e6\src\Antiphon.HostCleanup\Checkpoints\SafeWrite.cs:line 191
   at Antiphon.Checkpoints.EvidenceFolder.Write(String runDirectory, ReportModel model, Boolean removeToolCopy, String imageDirectory, CheckpointCleanupContext context) in C:\Antiphon\releases\checkout-997186e6\tools\Antiphon.Checkpoints\Evidence\EvidenceFolder.cs:line 47
   at Antiphon.Tests.Checkpoints.EvidenceFolderTests.red_run_writes_failures_with_message_stack_stdout_and_commands() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Checkpoints\EvidenceFolderTests.cs:line 52
```


<a id="f066"></a>

#### F066 — `Antiphon.Tests.Checkpoints.RunStateStoreTests.readers_never_see_a_partial_file`

- Display: `readers_never_see_a_partial_file`
- Suite/chunk: `antiphon/all-group-63`; duration `00:00:03.6845339`; manifest row 63.
- Evidence: `antiphon-all-group-63.trx:477`; test identity `4b9150fa-4e63-b8c3-cacc-ac08e62bf504`; execution ID `f4cc53ed-a44d-476f-96ce-31c06ffbd914`.
- Disposition: **ADDRESSED** — a043b0a4 / 281542f75b74848819ac2340402ff9e2167ec5bd: overlapped Windows handle mode; portable proof, native closure pending.

Error, first 12 lines (all lines if shorter):

```text
IOException: receipt-read-refused: C:\Users\lndco\AppData\Local\Temp\c723-01a17997f9227ccda6911cfc82033b74\state.json: read-stalled: 0 of 808 bytes; no progress within 3s
```


Top six stored stack frames (all if fewer):

```text
   at Antiphon.Checkpoints.ReceiptFile.Digest(CheckpointCleanupContext context, String path, CancellationToken cancellationToken) in C:\Antiphon\releases\checkout-997186e6\src\Antiphon.HostCleanup\Checkpoints\ReceiptFile.cs:line 39
   at Antiphon.Checkpoints.CheckpointWrite.Describe(String path) in C:\Antiphon\releases\checkout-997186e6\src\Antiphon.HostCleanup\Checkpoints\SafeWrite.cs:line 92
   at Antiphon.Checkpoints.CheckpointWrite.RecordContent(String path) in C:\Antiphon\releases\checkout-997186e6\src\Antiphon.HostCleanup\Checkpoints\SafeWrite.cs:line 88
   at Antiphon.Checkpoints.CheckpointWrite.Text(String path, String content, WriteOwnership ownership) in C:\Antiphon\releases\checkout-997186e6\src\Antiphon.HostCleanup\Checkpoints\SafeWrite.cs:line 191
   at Antiphon.Checkpoints.RunStateStore.Write(String path, RunState state) in C:\Antiphon\releases\checkout-997186e6\tools\Antiphon.Checkpoints\State\RunStateStore.cs:line 35
   at Antiphon.Tests.Checkpoints.RunStateStoreTests.readers_never_see_a_partial_file() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Checkpoints\RunStateStoreTests.cs:line 44
```


<a id="f067"></a>

#### F067 — `Antiphon.Tests.Checkpoints.RerunPolicyTests.row_green_only_when_rerun_passed_and_nothing_else_failed`

- Display: `row_green_only_when_rerun_passed_and_nothing_else_failed`
- Suite/chunk: `antiphon/all-group-63`; duration `00:00:03.7537854`; manifest row 63.
- Evidence: `antiphon-all-group-63.trx:493`; test identity `0d64ced1-73e1-2d85-ca6d-6f62dd2b741c`; execution ID `964054b1-5d6a-43a9-95af-60dc40457b52`.
- Disposition: **ADDRESSED** — a043b0a4 / 281542f75b74848819ac2340402ff9e2167ec5bd: overlapped Windows handle mode; portable proof, native closure pending.

Error, first 12 lines (all lines if shorter):

```text
IOException: receipt-read-refused: C:\Users\lndco\AppData\Local\Temp\c723-01a17997f8de7bd7ab1bae87fd0bbf1e\tests\Antiphon.Tests\bin-ex\.checkpoint-output-owner.json: read-stalled: 0 of 32 bytes; no progress within 3s
```


Top six stored stack frames (all if fewer):

```text
   at Antiphon.Checkpoints.ReceiptFile.Digest(CheckpointCleanupContext context, String path, CancellationToken cancellationToken) in C:\Antiphon\releases\checkout-997186e6\src\Antiphon.HostCleanup\Checkpoints\ReceiptFile.cs:line 39
   at Antiphon.Checkpoints.CheckpointWrite.Describe(String path) in C:\Antiphon\releases\checkout-997186e6\src\Antiphon.HostCleanup\Checkpoints\SafeWrite.cs:line 92
   at Antiphon.Checkpoints.CheckpointWrite.RecordContent(String path) in C:\Antiphon\releases\checkout-997186e6\src\Antiphon.HostCleanup\Checkpoints\SafeWrite.cs:line 88
   at Antiphon.Checkpoints.CheckpointWrite.Text(String path, String content, WriteOwnership ownership) in C:\Antiphon\releases\checkout-997186e6\src\Antiphon.HostCleanup\Checkpoints\SafeWrite.cs:line 191
   at Antiphon.Checkpoints.CheckpointWrite.CreateFreshDirectory(String path, WriteOwnership ownership) in C:\Antiphon\releases\checkout-997186e6\src\Antiphon.HostCleanup\Checkpoints\SafeWrite.cs:line 323
   at Antiphon.Checkpoints.SafeWrite.CreateFreshDirectory(String path, WriteOwnership ownership) in C:\Antiphon\releases\checkout-997186e6\src\Antiphon.HostCleanup\Checkpoints\SafeWrite.cs:line 51
```


<a id="f068"></a>

#### F068 — `Antiphon.Tests.Checkpoints.RerunPolicyTests.unlisted_failure_never_reruns`

- Display: `unlisted_failure_never_reruns`
- Suite/chunk: `antiphon/all-group-63`; duration `00:00:03.7409293`; manifest row 63.
- Evidence: `antiphon-all-group-63.trx:528`; test identity `953b7ab0-50e9-67d1-359e-3b1985f3a9e5`; execution ID `fe2421bd-db21-4d4e-98f8-6db2ae5fc9ec`.
- Disposition: **ADDRESSED** — a043b0a4 / 281542f75b74848819ac2340402ff9e2167ec5bd: overlapped Windows handle mode; portable proof, native closure pending.

Error, first 12 lines (all lines if shorter):

```text
IOException: receipt-read-refused: C:\Users\lndco\AppData\Local\Temp\c723-01a17997f8ea7c16a3bb78d9ad05695f\tests\Antiphon.Tests\bin-ex\.checkpoint-output-owner.json: read-stalled: 0 of 32 bytes; no progress within 3s
```


Top six stored stack frames (all if fewer):

```text
   at Antiphon.Checkpoints.ReceiptFile.Digest(CheckpointCleanupContext context, String path, CancellationToken cancellationToken) in C:\Antiphon\releases\checkout-997186e6\src\Antiphon.HostCleanup\Checkpoints\ReceiptFile.cs:line 39
   at Antiphon.Checkpoints.CheckpointWrite.Describe(String path) in C:\Antiphon\releases\checkout-997186e6\src\Antiphon.HostCleanup\Checkpoints\SafeWrite.cs:line 92
   at Antiphon.Checkpoints.CheckpointWrite.RecordContent(String path) in C:\Antiphon\releases\checkout-997186e6\src\Antiphon.HostCleanup\Checkpoints\SafeWrite.cs:line 88
   at Antiphon.Checkpoints.CheckpointWrite.Text(String path, String content, WriteOwnership ownership) in C:\Antiphon\releases\checkout-997186e6\src\Antiphon.HostCleanup\Checkpoints\SafeWrite.cs:line 191
   at Antiphon.Checkpoints.CheckpointWrite.CreateFreshDirectory(String path, WriteOwnership ownership) in C:\Antiphon\releases\checkout-997186e6\src\Antiphon.HostCleanup\Checkpoints\SafeWrite.cs:line 323
   at Antiphon.Checkpoints.SafeWrite.CreateFreshDirectory(String path, WriteOwnership ownership) in C:\Antiphon\releases\checkout-997186e6\src\Antiphon.HostCleanup\Checkpoints\SafeWrite.cs:line 51
```


<a id="f069"></a>

#### F069 — `Antiphon.Tests.Checkpoints.RerunPolicyTests.rerun_lines_and_reruns_suffix`

- Display: `rerun_lines_and_reruns_suffix`
- Suite/chunk: `antiphon/all-group-63`; duration `00:00:03.7544439`; manifest row 63.
- Evidence: `antiphon-all-group-63.trx:563`; test identity `725427e1-dfc6-908a-f3ff-bacc22195699`; execution ID `107cca2a-2772-471a-8621-9172cba9b541`.
- Disposition: **ADDRESSED** — a043b0a4 / 281542f75b74848819ac2340402ff9e2167ec5bd: overlapped Windows handle mode; portable proof, native closure pending.

Error, first 12 lines (all lines if shorter):

```text
IOException: receipt-read-refused: C:\Users\lndco\AppData\Local\Temp\c723-01a17997f8de7d2092423646bdae9d91\tests\Antiphon.Tests\bin-ex\.checkpoint-output-owner.json: read-stalled: 0 of 32 bytes; no progress within 3s
```


Top six stored stack frames (all if fewer):

```text
   at Antiphon.Checkpoints.ReceiptFile.Digest(CheckpointCleanupContext context, String path, CancellationToken cancellationToken) in C:\Antiphon\releases\checkout-997186e6\src\Antiphon.HostCleanup\Checkpoints\ReceiptFile.cs:line 39
   at Antiphon.Checkpoints.CheckpointWrite.Describe(String path) in C:\Antiphon\releases\checkout-997186e6\src\Antiphon.HostCleanup\Checkpoints\SafeWrite.cs:line 92
   at Antiphon.Checkpoints.CheckpointWrite.RecordContent(String path) in C:\Antiphon\releases\checkout-997186e6\src\Antiphon.HostCleanup\Checkpoints\SafeWrite.cs:line 88
   at Antiphon.Checkpoints.CheckpointWrite.Text(String path, String content, WriteOwnership ownership) in C:\Antiphon\releases\checkout-997186e6\src\Antiphon.HostCleanup\Checkpoints\SafeWrite.cs:line 191
   at Antiphon.Checkpoints.CheckpointWrite.CreateFreshDirectory(String path, WriteOwnership ownership) in C:\Antiphon\releases\checkout-997186e6\src\Antiphon.HostCleanup\Checkpoints\SafeWrite.cs:line 323
   at Antiphon.Checkpoints.SafeWrite.CreateFreshDirectory(String path, WriteOwnership ownership) in C:\Antiphon\releases\checkout-997186e6\src\Antiphon.HostCleanup\Checkpoints\SafeWrite.cs:line 51
```


<a id="f070"></a>

#### F070 — `Antiphon.Tests.Checkpoints.RerunPolicyTests.reruns_only_known_flaky_names_once`

- Display: `reruns_only_known_flaky_names_once`
- Suite/chunk: `antiphon/all-group-63`; duration `00:00:03.7204669`; manifest row 63.
- Evidence: `antiphon-all-group-63.trx:598`; test identity `f08af7c5-d35e-5f96-fbfd-844ad9777985`; execution ID `6cad0b96-734c-42dc-a266-24e101633595`.
- Disposition: **ADDRESSED** — a043b0a4 / 281542f75b74848819ac2340402ff9e2167ec5bd: overlapped Windows handle mode; portable proof, native closure pending.

Error, first 12 lines (all lines if shorter):

```text
IOException: receipt-read-refused: C:\Users\lndco\AppData\Local\Temp\c723-01a17997f8ff7db897661c7db331cf5d\tests\Antiphon.Tests\bin-ex\.checkpoint-output-owner.json: read-stalled: 0 of 32 bytes; no progress within 3s
```


Top six stored stack frames (all if fewer):

```text
   at Antiphon.Checkpoints.ReceiptFile.Digest(CheckpointCleanupContext context, String path, CancellationToken cancellationToken) in C:\Antiphon\releases\checkout-997186e6\src\Antiphon.HostCleanup\Checkpoints\ReceiptFile.cs:line 39
   at Antiphon.Checkpoints.CheckpointWrite.Describe(String path) in C:\Antiphon\releases\checkout-997186e6\src\Antiphon.HostCleanup\Checkpoints\SafeWrite.cs:line 92
   at Antiphon.Checkpoints.CheckpointWrite.RecordContent(String path) in C:\Antiphon\releases\checkout-997186e6\src\Antiphon.HostCleanup\Checkpoints\SafeWrite.cs:line 88
   at Antiphon.Checkpoints.CheckpointWrite.Text(String path, String content, WriteOwnership ownership) in C:\Antiphon\releases\checkout-997186e6\src\Antiphon.HostCleanup\Checkpoints\SafeWrite.cs:line 191
   at Antiphon.Checkpoints.CheckpointWrite.CreateFreshDirectory(String path, WriteOwnership ownership) in C:\Antiphon\releases\checkout-997186e6\src\Antiphon.HostCleanup\Checkpoints\SafeWrite.cs:line 323
   at Antiphon.Checkpoints.SafeWrite.CreateFreshDirectory(String path, WriteOwnership ownership) in C:\Antiphon\releases\checkout-997186e6\src\Antiphon.HostCleanup\Checkpoints\SafeWrite.cs:line 51
```


### B. Windows fixture bytes, file custody, path spelling and case semantics (32 results)

Several independent mechanisms share a Windows boundary: fixed database identity collision (23505), reciprocal reader/writer sharing, unjoined fixture release, Git newline normalization, missing owned-tool ledger, Unix executable assumption, duplicate parameterized skip accounting, path-separator and case sensitivity assumptions. Group membership is a triage aid, not a claim that every member has one cause. The per-result dispositions distinguish pushed repairs from unclosed errors.

<a id="f004"></a>

#### F004 — `Antiphon.Tests.Application.TerminalRunnerSeatReleaseTests.Answer_racing_release_preserves_one_owner`

- Display: `Answer_racing_release_preserves_one_owner`
- Suite/chunk: `antiphon/all-group-32`; duration `00:00:30.9840065`; manifest row 32.
- Evidence: `antiphon-all-group-32.trx:3676`; test identity `5a19551f-01df-a5c0-6aa7-816c9722b3df`; execution ID `a391982f-b0e7-46ff-bc0f-15de6d4ba95e`.
- Disposition: **ADDRESSED** — 7f7aa584: specific fixture repair documented in its pushed Windows-fixtures report; native closure pending.

Error, first 12 lines (all lines if shorter):

```text
IOException: The process cannot access the file 'landing.lock' because it is being used by another process.
```


Top six stored stack frames (all if fewer):

```text
   at System.IO.FileSystem.RemoveDirectoryRecursive(String fullPath, WIN32_FIND_DATA& findData, Boolean topLevel)
   at System.IO.FileSystem.RemoveDirectory(String fullPath, Boolean recursive)
   at Antiphon.Tests.TestHelpers.ReadOnlyDirectory.Delete(String path) in C:\Antiphon\releases\checkout-997186e6\tests\Shared\ReadOnlyDirectory.cs:line 15
   at Antiphon.Tests.Application.RunnerSeatReleaseFixture.DisposeAsync() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Application\RunnerSeatReleaseFixture.cs:line 651
   at Antiphon.Tests.Application.TerminalRunnerSeatReleaseTests.Answer_racing_release_preserves_one_owner() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Application\TerminalRunnerSeatReleaseTests.cs:line 664
```


<a id="f014"></a>

#### F014 — `Antiphon.Tests.Application.CardFilePrivacySyncTests.Private_card_contributes_no_filename_index_count_or_metadata`

- Display: `Private_card_contributes_no_filename_index_count_or_metadata(False)`
- Suite/chunk: `antiphon/all-group-35`; duration `00:00:00.9096688`; manifest row 35.
- Evidence: `antiphon-all-group-35.trx:2254`; test identity `7378f997-1c0b-660c-b1ac-1df016f564d7`; execution ID `dc679527-0a8c-46b6-9873-34801ac76ccf`.
- Disposition: **ADDRESSED** — 7f7aa584: specific fixture repair documented in its pushed Windows-fixtures report; native closure pending.

Error, first 12 lines (all lines if shorter):

```text
DbUpdateException: An error occurred while saving the entity changes. See the inner exception for details.
```


Top six stored stack frames (all if fewer):

```text
   at Microsoft.EntityFrameworkCore.Update.ReaderModificationCommandBatch.ExecuteAsync(IRelationalConnection connection, CancellationToken cancellationToken)
   at Microsoft.EntityFrameworkCore.Update.Internal.BatchExecutor.ExecuteAsync(IEnumerable`1 commandBatches, IRelationalConnection connection, CancellationToken cancellationToken)
   at Microsoft.EntityFrameworkCore.Update.Internal.BatchExecutor.ExecuteAsync(IEnumerable`1 commandBatches, IRelationalConnection connection, CancellationToken cancellationToken)
   at Microsoft.EntityFrameworkCore.Update.Internal.BatchExecutor.ExecuteAsync(IEnumerable`1 commandBatches, IRelationalConnection connection, CancellationToken cancellationToken)
   at Microsoft.EntityFrameworkCore.Storage.RelationalDatabase.SaveChangesAsync(IList`1 entries, CancellationToken cancellationToken)
   at Microsoft.EntityFrameworkCore.ChangeTracking.Internal.StateManager.SaveChangesAsync(IList`1 entriesToSave, CancellationToken cancellationToken)
```


Supplemental inner database error, separate from the 12-line excerpt (`antiphon-all-group-35-tests.log`):

```text
281:   --->  Npgsql.PostgresException: 23505: duplicate key value violates unique constraint "IX_ExternalIssueRefs_TrackerKind_ExternalId"
```


<a id="f025"></a>

#### F025 — `Antiphon.Tests.Application.ChannelOutboundRecoveryTests.Process_death_preserves_ownership_at_each_boundary`

- Display: `Process_death_preserves_ownership_at_each_boundary(conversion-dispatched)`
- Suite/chunk: `antiphon/all-group-37`; duration `00:00:18.9176942`; manifest row 37.
- Evidence: `antiphon-all-group-37.trx:8581`; test identity `9b6c028c-dbbd-cad9-08de-7ac7954b2b2d`; execution ID `027bf41d-3fa3-4851-a82d-acb00ed64b4e`.
- Disposition: **ADDRESSED** — 7f7aa584: specific fixture repair documented in its pushed Windows-fixtures report; native closure pending.

Error, first 12 lines (all lines if shorter):

```text
IOException: The process cannot access the file 'C:\Users\lndco\AppData\Local\Temp\antiphon-outbound-admission-453e02b901d044948913af72afb79178\barrier' because it is being used by another process.
```


Top six stored stack frames (all if fewer):

```text
   at Microsoft.Win32.SafeHandles.SafeFileHandle.CreateFile(String fullPath, FileMode mode, FileAccess access, FileShare share, FileOptions options)
   at Microsoft.Win32.SafeHandles.SafeFileHandle.Open(String fullPath, FileMode mode, FileAccess access, FileShare share, FileOptions options, Int64 preallocationSize, Nullable`1 unixCreateMode)
   at System.IO.Strategies.OSFileStreamStrategy..ctor(String path, FileMode mode, FileAccess access, FileShare share, FileOptions options, Int64 preallocationSize, Nullable`1 unixCreateMode)
   at System.IO.File.InternalReadAllTextAsync(String path, Encoding encoding, CancellationToken cancellationToken)
   at Antiphon.Tests.Application.ChannelOutboundRecoveryTests.Process_death_preserves_ownership_at_each_boundary(String cut) in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Application\ChannelOutboundRecoveryTests.cs:line 684
   at Antiphon.Tests.Application.ChannelOutboundRecoveryTests.Process_death_preserves_ownership_at_each_boundary(String cut) in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Application\ChannelOutboundRecoveryTests.cs:line 1011
```


<a id="f026"></a>

#### F026 — `Antiphon.Tests.Application.ChannelOutboundRecoveryTests.Running_converter_reconciles_after_dispatcher_death`

- Display: `Running_converter_reconciles_after_dispatcher_death`
- Suite/chunk: `antiphon/all-group-37`; duration `00:00:17.7743489`; manifest row 37.
- Evidence: `antiphon-all-group-37.trx:8610`; test identity `68afbaa4-8818-af3f-9ab8-3cb5b1c790d1`; execution ID `46c10ad2-e2da-40c6-9212-af1902e6c5a5`.
- Disposition: **ADDRESSED** — 7f7aa584: specific fixture repair documented in its pushed Windows-fixtures report; native closure pending.

Error, first 12 lines (all lines if shorter):

```text
IOException: The process cannot access the file 'C:\Users\lndco\AppData\Local\Temp\antiphon-outbound-admission-1adab64a74394502b7fbe61834295dd6\barrier' because it is being used by another process.
```


Top six stored stack frames (all if fewer):

```text
   at Microsoft.Win32.SafeHandles.SafeFileHandle.CreateFile(String fullPath, FileMode mode, FileAccess access, FileShare share, FileOptions options)
   at Microsoft.Win32.SafeHandles.SafeFileHandle.Open(String fullPath, FileMode mode, FileAccess access, FileShare share, FileOptions options, Int64 preallocationSize, Nullable`1 unixCreateMode)
   at System.IO.Strategies.OSFileStreamStrategy..ctor(String path, FileMode mode, FileAccess access, FileShare share, FileOptions options, Int64 preallocationSize, Nullable`1 unixCreateMode)
   at System.IO.File.InternalReadAllTextAsync(String path, Encoding encoding, CancellationToken cancellationToken)
   at Antiphon.Tests.Application.ChannelOutboundRecoveryTests.Process_death_preserves_ownership_at_each_boundary(String cut) in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Application\ChannelOutboundRecoveryTests.cs:line 684
   at Antiphon.Tests.Application.ChannelOutboundRecoveryTests.Process_death_preserves_ownership_at_each_boundary(String cut) in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Application\ChannelOutboundRecoveryTests.cs:line 1011
```


<a id="f028"></a>

#### F028 — `Antiphon.Tests.Application.AgentTaskStandingAgentDispatchTests.and_it_writes_nothing_at_all_on_the_agent_row`

- Display: `and_it_writes_nothing_at_all_on_the_agent_row`
- Suite/chunk: `antiphon/all-group-42`; duration `00:00:08.2883642`; manifest row 42.
- Evidence: `antiphon-all-group-42.trx:416`; test identity `7b55b892-458d-9413-bf93-3ae72dfd4dc7`; execution ID `68c6ddcd-0449-45a0-ba72-84dd6b86d083`.
- Disposition: **ADDRESSED** — 7f7aa584: specific fixture repair documented in its pushed Windows-fixtures report; native closure pending.

Error, first 12 lines (all lines if shorter):

```text
DbUpdateException: An error occurred while saving the entity changes. See the inner exception for details.
```


Top six stored stack frames (all if fewer):

```text
   at Microsoft.EntityFrameworkCore.Update.ReaderModificationCommandBatch.ExecuteAsync(IRelationalConnection connection, CancellationToken cancellationToken)
   at Microsoft.EntityFrameworkCore.Update.Internal.BatchExecutor.ExecuteAsync(IEnumerable`1 commandBatches, IRelationalConnection connection, CancellationToken cancellationToken)
   at Microsoft.EntityFrameworkCore.Update.Internal.BatchExecutor.ExecuteAsync(IEnumerable`1 commandBatches, IRelationalConnection connection, CancellationToken cancellationToken)
   at Microsoft.EntityFrameworkCore.Update.Internal.BatchExecutor.ExecuteAsync(IEnumerable`1 commandBatches, IRelationalConnection connection, CancellationToken cancellationToken)
   at Microsoft.EntityFrameworkCore.Storage.RelationalDatabase.SaveChangesAsync(IList`1 entries, CancellationToken cancellationToken)
   at Microsoft.EntityFrameworkCore.ChangeTracking.Internal.StateManager.SaveChangesAsync(IList`1 entriesToSave, CancellationToken cancellationToken)
```


Supplemental inner database error, separate from the 12-line excerpt (`antiphon-all-group-42-tests.log`):

```text
23:   --->  Npgsql.PostgresException: 23503: insert or update on table "AgentTaskEvents" violates foreign key constraint "FK_AgentTaskEvents_AgentTasks_AgentTaskId"
```


<a id="f033"></a>

#### F033 — `Antiphon.Tests.Application.AgentTaskPipelineEndpointTests.pipeline_route_is_literal_and_returns_the_advisory_contract`

- Display: `pipeline_route_is_literal_and_returns_the_advisory_contract`
- Suite/chunk: `antiphon/all-group-51`; duration `00:00:02.0547946`; manifest row 51.
- Evidence: `antiphon-all-group-51.trx:951`; test identity `eeb15078-e2f1-c8c9-7f58-95f24dfc944d`; execution ID `c22f0383-56ed-4618-ace3-eaa41299b801`.
- Disposition: **ADDRESSED** — d42f9a7c / 81924e54e7e7a638acd19c951412295752a16186 isolates endpoint factory database from shared settings.

Error, first 12 lines (all lines if shorter):

```text
ShouldAssertException: dto.Stages
    should contain an element satisfying the condition
((s.Role == 1) && (s.RecommendedInFlight == 1))
    but does not
```


Top six stored stack frames (all if fewer):

```text
   at Antiphon.Tests.Application.AgentTaskPipelineEndpointTests.pipeline_route_is_literal_and_returns_the_advisory_contract() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Application\AgentTaskPipelineStatusTests.cs:line 993
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
```


<a id="f034"></a>

#### F034 — `Antiphon.Tests.Application.GitWorkspaceServiceIndexLockTests.Status_never_takes_the_optional_index_lock`

- Display: `Status_never_takes_the_optional_index_lock`
- Suite/chunk: `antiphon/all-group-54`; duration `00:00:03.1334863`; manifest row 54.
- Evidence: `antiphon-all-group-54.trx:247`; test identity `b1492f98-1db0-33d6-41d6-2364a68b438b`; execution ID `91035feb-61be-4408-8ba2-547ba069e2b6`.
- Disposition: **ADDRESSED** — 7f7aa584 makes index refresh control deterministic; e3a90a36 / a4167bde7d24fe9cb9773b38bb4cff95c6712576 makes stall hooks platform-native. Native positive control pending.

Error, first 12 lines (all lines if shorter):

```text
ShouldAssertException: File.GetLastWriteTimeUtc(index)
    should not be
2026-10-26T18:33:10.1478699Z
    but was
```


Top six stored stack frames (all if fewer):

```text
   at Antiphon.Tests.Application.GitWorkspaceServiceIndexLockTests.Status_never_takes_the_optional_index_lock() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Application\GitWorkspaceServiceIndexLockTests.cs:line 32
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at System.Runtime.CompilerServices.TaskAwaiter.ThrowForNonSuccess(Task task)
   at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at System.Runtime.CompilerServices.TaskAwaiter.ThrowForNonSuccess(Task task)
```


<a id="f038"></a>

#### F038 — `Antiphon.Tests.Checkpoints.CheckpointConfinementLedgerTests.ignored_tree_outside_write_roots_is_stamped_before_the_slot_not_inside_it`

- Display: `ignored_tree_outside_write_roots_is_stamped_before_the_slot_not_inside_it`
- Suite/chunk: `antiphon/all-group-61`; duration `00:00:09.5650190`; manifest row 61.
- Evidence: `antiphon-all-group-61.trx:623`; test identity `c5e2bf44-a43b-61a1-a09a-7d73174250f7`; execution ID `50eefe42-786a-4572-9b47-51e39b5f9b8f`.
- Disposition: **ADDRESSED** — 7f7aa584: specific fixture repair documented in its pushed Windows-fixtures report; native closure pending.

Error, first 12 lines (all lines if shorter):

```text
ShouldAssertException: prepared.Refusal
    should be null but was
"dirty-tracked"

Additional Info:
    refused: dirty-tracked
DIRTY App/App.csproj
```


Top six stored stack frames (all if fewer):

```text
   at Antiphon.Tests.Checkpoints.CheckpointConfinementLedgerTests.Prepare(String root, Ledger ledger, CancellationToken cancellationToken) in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Checkpoints\CheckpointConfinementLedgerTests.cs:line 601
   at Antiphon.Tests.Checkpoints.CheckpointConfinementLedgerTests.ignored_tree_outside_write_roots_is_stamped_before_the_slot_not_inside_it(CancellationToken cancellationToken) in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Checkpoints\CheckpointConfinementLedgerTests.cs:line 32
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at System.Runtime.CompilerServices.TaskAwaiter.ThrowForNonSuccess(Task task)
   at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
```


<a id="f039"></a>

#### F039 — `Antiphon.Tests.Checkpoints.CheckpointConfinementLedgerTests.in_slot_baseline_entry_unreadable_at_settlement_is_refused_unreadable`

- Display: `in_slot_baseline_entry_unreadable_at_settlement_is_refused_unreadable`
- Suite/chunk: `antiphon/all-group-61`; duration `00:00:06.3129856`; manifest row 61.
- Evidence: `antiphon-all-group-61.trx:709`; test identity `ddfbefb3-868b-fb59-d3b2-c073aa18608c`; execution ID `5e930e9f-61cb-4be8-8909-a63e6aef0da6`.
- Disposition: **ADDRESSED** — 7f7aa584: specific fixture repair documented in its pushed Windows-fixtures report; native closure pending.

Error, first 12 lines (all lines if shorter):

```text
ShouldAssertException: prepared.Refusal
    should be null but was
"dirty-tracked"

Additional Info:
    refused: dirty-tracked
DIRTY App/App.csproj
```


Top six stored stack frames (all if fewer):

```text
   at Antiphon.Tests.Checkpoints.CheckpointConfinementLedgerTests.Prepare(String root, Ledger ledger, CancellationToken cancellationToken) in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Checkpoints\CheckpointConfinementLedgerTests.cs:line 601
   at Antiphon.Tests.Checkpoints.CheckpointConfinementLedgerTests.in_slot_baseline_entry_unreadable_at_settlement_is_refused_unreadable(CancellationToken cancellationToken) in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Checkpoints\CheckpointConfinementLedgerTests.cs:line 427
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at System.Runtime.CompilerServices.TaskAwaiter.ThrowForNonSuccess(Task task)
   at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
```


<a id="f040"></a>

#### F040 — `Antiphon.Tests.Checkpoints.CheckpointConfinementLedgerTests.changed_file_under_an_output_root_is_still_refused_inside_the_slot`

- Display: `changed_file_under_an_output_root_is_still_refused_inside_the_slot`
- Suite/chunk: `antiphon/all-group-61`; duration `00:00:06.0743989`; manifest row 61.
- Evidence: `antiphon-all-group-61.trx:777`; test identity `8d031dbc-6461-300e-d331-d3241b303256`; execution ID `041d5c8c-ec0b-4857-8b30-7b2421899988`.
- Disposition: **ADDRESSED** — 7f7aa584: specific fixture repair documented in its pushed Windows-fixtures report; native closure pending.

Error, first 12 lines (all lines if shorter):

```text
ShouldAssertException: prepared.Refusal
    should be null but was
"dirty-tracked"

Additional Info:
    refused: dirty-tracked
DIRTY App/App.csproj
```


Top six stored stack frames (all if fewer):

```text
   at Antiphon.Tests.Checkpoints.CheckpointConfinementLedgerTests.Prepare(String root, Ledger ledger, CancellationToken cancellationToken) in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Checkpoints\CheckpointConfinementLedgerTests.cs:line 601
   at Antiphon.Tests.Checkpoints.CheckpointConfinementLedgerTests.changed_file_under_an_output_root_is_still_refused_inside_the_slot(CancellationToken cancellationToken) in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Checkpoints\CheckpointConfinementLedgerTests.cs:line 73
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at System.Runtime.CompilerServices.TaskAwaiter.ThrowForNonSuccess(Task task)
   at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
```


<a id="f041"></a>

#### F041 — `Antiphon.Tests.Checkpoints.CheckpointRecoveryWindowsTests.locked_tool_delete_is_reported_then_retried`

- Display: `locked_tool_delete_is_reported_then_retried`
- Suite/chunk: `antiphon/all-group-61`; duration `00:00:00.1520952`; manifest row 61.
- Evidence: `antiphon-all-group-61.trx:1161`; test identity `fe9f54ba-7559-8015-b2f7-496a7b30a2e9`; execution ID `21def76f-8121-4b0c-b159-5216ad961ab2`.
- Disposition: **ADDRESSED** — Existing pushed de78f02f7ede6fa490eb35f04e72dfc199edf3d9 / 694ab10791b1b5c865c0c59bb31c5d157f02cc27 add owned-tool custody and cleanup witnesses; native refusal rerun pending.

Error, first 12 lines (all lines if shorter):

```text
ShouldAssertException: Remove(run).Outcome
    should be
"Failed"
    but was
"Refused"
    difference
Difference     |  |    |    |    |    |    |    |   
               | \|/  \|/  \|/  \|/  \|/  \|/  \|/  
Index          | 0    1    2    3    4    5    6    
Expected Value | F    a    i    l    e    d         
Actual Value   | R    e    f    u    s    e    d    
Expected Code  | 70   97   105  108  101  100
```


Top six stored stack frames (all if fewer):

```text
   at Antiphon.Tests.Checkpoints.CheckpointRecoveryWindowsTests.locked_tool_delete_is_reported_then_retried() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Checkpoints\CheckpointRecoveryWindowsTests.cs:line 252
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at System.Runtime.CompilerServices.TaskAwaiter.ThrowForNonSuccess(Task task)
   at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at System.Runtime.CompilerServices.TaskAwaiter.ThrowForNonSuccess(Task task)
```


<a id="f042"></a>

#### F042 — `Antiphon.Tests.Checkpoints.CheckpointRecoveryWindowsTests.starter_death_before_launch_reclaims_partial_copy`

- Display: `starter_death_before_launch_reclaims_partial_copy`
- Suite/chunk: `antiphon/all-group-61`; duration `00:00:00.2392466`; manifest row 61.
- Evidence: `antiphon-all-group-61.trx:1197`; test identity `93f2142d-269d-7979-f91d-ea12949eb20b`; execution ID `8977bef6-6feb-445e-a824-c1be6e3724be`.
- Disposition: **ADDRESSED** — Existing pushed de78f02f7ede6fa490eb35f04e72dfc199edf3d9 / 694ab10791b1b5c865c0c59bb31c5d157f02cc27 add owned-tool custody and cleanup witnesses; native refusal rerun pending.

Error, first 12 lines (all lines if shorter):

```text
ShouldAssertException: Remove(run).Outcome
    should be
"Removed"
    but was
"Refused"
    difference
Difference     |            |    |    |             
               |           \|/  \|/  \|/            
Index          | 0    1    2    3    4    5    6    
Expected Value | R    e    m    o    v    e    d    
Actual Value   | R    e    f    u    s    e    d    
Expected Code  | 82   101  109  111  118  101  100
```


Top six stored stack frames (all if fewer):

```text
   at Antiphon.Tests.Checkpoints.CheckpointRecoveryWindowsTests.starter_death_before_launch_reclaims_partial_copy() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Checkpoints\CheckpointRecoveryWindowsTests.cs:line 113
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at Antiphon.Tests.Checkpoints.CheckpointRecoveryWindowsTests.starter_death_before_launch_reclaims_partial_copy() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Checkpoints\CheckpointRecoveryWindowsTests.cs:line 115
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at System.Runtime.CompilerServices.TaskAwaiter.ThrowForNonSuccess(Task task)
   at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
```


<a id="f043"></a>

#### F043 — `Antiphon.Tests.Checkpoints.CheckpointRecoveryWindowsTests.done_live_executor_retains_image`

- Display: `done_live_executor_retains_image`
- Suite/chunk: `antiphon/all-group-61`; duration `00:00:00.2765299`; manifest row 61.
- Evidence: `antiphon-all-group-61.trx:1236`; test identity `fec2ffd0-36e7-4220-0b23-1adc772c9c19`; execution ID `eefb7ff8-e3a5-4403-8841-a52de106fc6e`.
- Disposition: **ADDRESSED** — Existing pushed de78f02f7ede6fa490eb35f04e72dfc199edf3d9 / 694ab10791b1b5c865c0c59bb31c5d157f02cc27 add owned-tool custody and cleanup witnesses; native refusal rerun pending.

Error, first 12 lines (all lines if shorter):

```text
ShouldAssertException: await WaitAsync(run, TimeSpan.FromSeconds(1))
    should be
0
    but was
2
```


Top six stored stack frames (all if fewer):

```text
   at Antiphon.Tests.Checkpoints.CheckpointRecoveryWindowsTests.done_live_executor_retains_image() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Checkpoints\CheckpointRecoveryWindowsTests.cs:line 95
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at Antiphon.Tests.Checkpoints.CheckpointRecoveryWindowsTests.done_live_executor_retains_image() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Checkpoints\CheckpointRecoveryWindowsTests.cs:line 98
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at System.Runtime.CompilerServices.TaskAwaiter.ThrowForNonSuccess(Task task)
   at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
```


<a id="f045"></a>

#### F045 — `Antiphon.Tests.Checkpoints.CheckpointRecoveryWindowsTests.native_identity_keeps_live_then_removes_dead`

- Display: `native_identity_keeps_live_then_removes_dead`
- Suite/chunk: `antiphon/all-group-61`; duration `00:00:00.1739246`; manifest row 61.
- Evidence: `antiphon-all-group-61.trx:1325`; test identity `93561419-39e1-dca6-a914-2753f66d5228`; execution ID `2a33cb4c-46c1-46b0-953b-be0cc81d2206`.
- Disposition: **ADDRESSED** — Existing pushed de78f02f7ede6fa490eb35f04e72dfc199edf3d9 / 694ab10791b1b5c865c0c59bb31c5d157f02cc27 add owned-tool custody and cleanup witnesses; native refusal rerun pending.

Error, first 12 lines (all lines if shorter):

```text
ShouldAssertException: Remove(run, cleanup).Outcome
    should be
"Removed"
    but was
"Refused"
    difference
Difference     |            |    |    |             
               |           \|/  \|/  \|/            
Index          | 0    1    2    3    4    5    6    
Expected Value | R    e    m    o    v    e    d    
Actual Value   | R    e    f    u    s    e    d    
Expected Code  | 82   101  109  111  118  101  100
```


Top six stored stack frames (all if fewer):

```text
   at Antiphon.Tests.Checkpoints.CheckpointRecoveryWindowsTests.native_identity_keeps_live_then_removes_dead() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Checkpoints\CheckpointRecoveryWindowsTests.cs:line 45
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at Antiphon.Tests.Checkpoints.CheckpointRecoveryWindowsTests.native_identity_keeps_live_then_removes_dead() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Checkpoints\CheckpointRecoveryWindowsTests.cs:line 47
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at System.Runtime.CompilerServices.TaskAwaiter.ThrowForNonSuccess(Task task)
   at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
```


<a id="f046"></a>

#### F046 — `Antiphon.Tests.Checkpoints.CheckpointRecoveryWindowsTests.launch_unknown_survives_until_late_ack`

- Display: `launch_unknown_survives_until_late_ack`
- Suite/chunk: `antiphon/all-group-61`; duration `00:00:00.1843888`; manifest row 61.
- Evidence: `antiphon-all-group-61.trx:1364`; test identity `129a0972-f5c8-93bd-52cb-2209865490bb`; execution ID `8aaee1d9-26cd-4299-b4ed-0ae5b34ee77d`.
- Disposition: **ADDRESSED** — Existing pushed de78f02f7ede6fa490eb35f04e72dfc199edf3d9 / 694ab10791b1b5c865c0c59bb31c5d157f02cc27 add owned-tool custody and cleanup witnesses; native refusal rerun pending.

Error, first 12 lines (all lines if shorter):

```text
ShouldAssertException: Remove(run).Outcome
    should be
"Removed"
    but was
"Refused"
    difference
Difference     |            |    |    |             
               |           \|/  \|/  \|/            
Index          | 0    1    2    3    4    5    6    
Expected Value | R    e    m    o    v    e    d    
Actual Value   | R    e    f    u    s    e    d    
Expected Code  | 82   101  109  111  118  101  100
```


Top six stored stack frames (all if fewer):

```text
   at Antiphon.Tests.Checkpoints.CheckpointRecoveryWindowsTests.launch_unknown_survives_until_late_ack() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Checkpoints\CheckpointRecoveryWindowsTests.cs:line 237
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at Antiphon.Tests.Checkpoints.CheckpointRecoveryWindowsTests.launch_unknown_survives_until_late_ack() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Checkpoints\CheckpointRecoveryWindowsTests.cs:line 239
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at System.Runtime.CompilerServices.TaskAwaiter.ThrowForNonSuccess(Task task)
   at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
```


<a id="f047"></a>

#### F047 — `Antiphon.Tests.Checkpoints.OutputCleanupTests.preflight_git_failure_exits_2_without_writing`

- Display: `preflight_git_failure_exits_2_without_writing`
- Suite/chunk: `antiphon/all-group-61`; duration `00:00:00.3082688`; manifest row 61.
- Evidence: `antiphon-all-group-61.trx:4188`; test identity `a6206b3b-6643-e9a3-99d3-cfc7c7c59055`; execution ID `e77e0d09-f26b-4482-bbe1-847879c28a4c`.
- Disposition: **ADDRESSED** — 7f7aa584: specific fixture repair documented in its pushed Windows-fixtures report; native closure pending.

Error, first 12 lines (all lines if shorter):

```text
ShouldAssertException: File.Exists("/bin/false")
    should be
True
    but was
False
```


Top six stored stack frames (all if fewer):

```text
   at Antiphon.Tests.Checkpoints.OutputCleanupTests.preflight_git_failure_exits_2_without_writing() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Checkpoints\OutputCleanupTests.cs:line 1196
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at System.Runtime.CompilerServices.TaskAwaiter.ThrowForNonSuccess(Task task)
   at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at System.Runtime.CompilerServices.TaskAwaiter.ThrowForNonSuccess(Task task)
```


<a id="f048"></a>

#### F048 — `Antiphon.Tests.Checkpoints.OutputCleanupTests.safe_write_refuses_tracked_linked_unrecorded_and_git_failure`

- Display: `safe_write_refuses_tracked_linked_unrecorded_and_git_failure`
- Suite/chunk: `antiphon/all-group-61`; duration `00:00:01.9765477`; manifest row 61.
- Evidence: `antiphon-all-group-61.trx:4238`; test identity `1cf2bf0c-21ac-16d2-72b0-32868218ceac`; execution ID `76715f03-6d7a-411c-83b5-82b256aa025c`.
- Disposition: **ADDRESSED** — 7f7aa584: specific fixture repair documented in its pushed Windows-fixtures report; native closure pending.

Error, first 12 lines (all lines if shorter):

```text
ShouldAssertException: File.Exists("/bin/false")
    should be
True
    but was
False
```


Top six stored stack frames (all if fewer):

```text
   at Antiphon.Tests.Checkpoints.OutputCleanupTests.safe_write_refuses_tracked_linked_unrecorded_and_git_failure() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Checkpoints\OutputCleanupTests.cs:line 1158
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at System.Runtime.CompilerServices.TaskAwaiter.ThrowForNonSuccess(Task task)
   at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at System.Runtime.CompilerServices.TaskAwaiter.ThrowForNonSuccess(Task task)
```


<a id="f049"></a>

#### F049 — `Antiphon.Tests.Checkpoints.OutputCleanupTests.unknown_lifecycle_report_after_seal_is_refused_and_kept`

- Display: `unknown_lifecycle_report_after_seal_is_refused_and_kept`
- Suite/chunk: `antiphon/all-group-61`; duration `00:00:17.8016905`; manifest row 61.
- Evidence: `antiphon-all-group-61.trx:4587`; test identity `05c6bae7-d0ac-b032-1111-8701867e72a6`; execution ID `fb1e097b-42d1-4608-a48b-9bac9e38051d`.
- Disposition: **ADDRESSED** — 7f7aa584: specific fixture repair documented in its pushed Windows-fixtures report; native closure pending.

Error, first 12 lines (all lines if shorter):

```text
ShouldAssertException: result.Refusals
    should be
["output-ledger-mismatch; left in place: C:\Users\lndco\AppData\Local\Temp\c723-01a17990462170069dc146b8920594a7\bin-c723a\checkpoint-lifecycle-host\TestResults\unknown.txt"]
    but was (case sensitive comparison)
["output-ledger-mismatch; left in place: C:\Users\lndco\AppData\Local\Temp\c723-01a17990462170069dc146b8920594a7\bin-c723a\checkpoint-lifecycle-host/TestResults/unknown.txt"]
    difference
[*"output-ledger-mismatch; left in place: C:\Users\lndco\AppData\Local\Temp\c723-01a17990462170069dc146b8920594a7\bin-c723a\checkpoint-lifecycle-host/TestResults/unknown.txt"*]
```


Top six stored stack frames (all if fewer):

```text
   at Antiphon.Tests.Checkpoints.OutputCleanupTests.unknown_lifecycle_report_after_seal_is_refused_and_kept() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Checkpoints\OutputCleanupTests.cs:line 227
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at System.Runtime.CompilerServices.TaskAwaiter.ThrowForNonSuccess(Task task)
   at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at System.Runtime.CompilerServices.TaskAwaiter.ThrowForNonSuccess(Task task)
```


<a id="f057"></a>

#### F057 — `Antiphon.Tests.Checkpoints.CheckpointTempUsageTests.namespace_census_uses_the_native_execution_roster`

- Display: `namespace_census_uses_the_native_execution_roster`
- Suite/chunk: `antiphon/all-group-62`; duration `00:00:06.5975701`; manifest row 62.
- Evidence: `antiphon-all-group-62.trx:2734`; test identity `ea437a5e-654c-96f3-988a-597d4a010232`; execution ID `66ec182d-e9eb-470c-a6e6-48341934a232`.
- Disposition: **ADDRESSED** — 7f7aa584: specific fixture repair documented in its pushed Windows-fixtures report; native closure pending.

Error, first 12 lines (all lines if shorter):

```text
ShouldAssertException: evidence.GetProperty("executed").GetInt32()
    should be
670
    but was
646
```


Top six stored stack frames (all if fewer):

```text
   at Antiphon.Tests.Checkpoints.CheckpointTempUsageTests.namespace_census_uses_the_native_execution_roster() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Checkpoints\CheckpointTempUsageTests.cs:line 115
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at System.Runtime.CompilerServices.TaskAwaiter.ThrowForNonSuccess(Task task)
   at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at System.Runtime.CompilerServices.TaskAwaiter.ThrowForNonSuccess(Task task)
```


<a id="f058"></a>

#### F058 — `Antiphon.Tests.Checkpoints.CheckpointTempUsageTests.full_suite_checkpoint_floor_uses_the_current_census`

- Display: `full_suite_checkpoint_floor_uses_the_current_census`
- Suite/chunk: `antiphon/all-group-62`; duration `00:00:06.3043807`; manifest row 62.
- Evidence: `antiphon-all-group-62.trx:2779`; test identity `b928c723-6da3-9bcf-1099-b6f844b3153e`; execution ID `0bf6cb88-398c-4d09-8af5-7ebfcb44d62c`.
- Disposition: **ADDRESSED** — 7f7aa584: specific fixture repair documented in its pushed Windows-fixtures report; native closure pending.

Error, first 12 lines (all lines if shorter):

```text
ShouldAssertException: result.RootElement.GetProperty("native").GetProperty("executed").GetInt32()
    should be
1670
    but was
1646
```


Top six stored stack frames (all if fewer):

```text
   at Antiphon.Tests.Checkpoints.CheckpointTempUsageTests.full_suite_checkpoint_floor_uses_the_current_census() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Checkpoints\CheckpointTempUsageTests.cs:line 153
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at System.Runtime.CompilerServices.TaskAwaiter.ThrowForNonSuccess(Task task)
   at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at System.Runtime.CompilerServices.TaskAwaiter.ThrowForNonSuccess(Task task)
```


<a id="f076"></a>

#### F076 — `Antiphon.Tests.Infrastructure.HostCleanupCheckpointExtractionTests.Moved_types_have_one_library_owner`

- Display: `Moved_types_have_one_library_owner`
- Suite/chunk: `antiphon/all-group-66`; duration `00:00:00.1790500`; manifest row 66.
- Evidence: `antiphon-all-group-66.trx:469`; test identity `7c55229b-0377-5bec-5859-e8ff45ef5191`; execution ID `8ca55f5e-dc49-4186-91c1-68bbdc2c3432`.
- Disposition: **ADDRESSED** — 9c66bc68 / 64c6ecc6d0ebfea4fc8255a02e887c39ab6cd5eb accounts for lifecycle host-cleanup dependencies.

Error, first 12 lines (all lines if shorter):

```text
ShouldAssertException: Includes(hostProject, "Compile").Count()
    should be
2
    but was
4
```


Top six stored stack frames (all if fewer):

```text
   at Antiphon.Tests.Infrastructure.HostCleanupCheckpointExtractionTests.Moved_types_have_one_library_owner() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Infrastructure\HostCleanupCheckpointExtractionTests.cs:line 157
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at System.Runtime.CompilerServices.TaskAwaiter.ThrowForNonSuccess(Task task)
   at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at System.Runtime.CompilerServices.TaskAwaiter.ThrowForNonSuccess(Task task)
```


<a id="f077"></a>

#### F077 — `Antiphon.Tests.Infrastructure.WorktreeRemovalAuthorityTests.C448_V35_BuildJunkScriptPreservesOpaqueWildcardMatches`

- Display: `C448_V35_BuildJunkScriptPreservesOpaqueWildcardMatches`
- Suite/chunk: `antiphon/all-group-66`; duration `00:00:02.3783773`; manifest row 66.
- Evidence: `antiphon-all-group-66.trx:2066`; test identity `1f0cbd47-7510-dbb4-a4b6-09993fea4f3e`; execution ID `a1cfe1a1-d25b-4eef-b5d0-c51126d9641d`.
- Disposition: **UNRESOLVED** — No matching pushed repair established; native mechanism remains open.

Error, first 12 lines (all lines if shorter):

```text
ShouldAssertException: File.Exists(path)
    should be
True
    but was
False

Additional Info:
    build cleanup must preserve opaque wildcard-matched bytes
```


Top six stored stack frames (all if fewer):

```text
   at Antiphon.Tests.Infrastructure.WorktreeRemovalAuthorityTests.C448_V35_BuildJunkScriptPreservesOpaqueWildcardMatches() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Infrastructure\WorktreeRemovalAuthorityTests.cs:line 38
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at Antiphon.Tests.Infrastructure.WorktreeRemovalAuthorityTests.C448_V35_BuildJunkScriptPreservesOpaqueWildcardMatches() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Infrastructure\WorktreeRemovalAuthorityTests.cs:line 41
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at System.Runtime.CompilerServices.TaskAwaiter.ThrowForNonSuccess(Task task)
   at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
```


<a id="f082"></a>

#### F082 — `Antiphon.Tests.Scripts.EvidenceDiffGuardTests.Handles_literal_paths_and_case_variants`

- Display: `Handles_literal_paths_and_case_variants`
- Suite/chunk: `antiphon/all-group-74`; duration `00:00:04.0264793`; manifest row 74.
- Evidence: `antiphon-all-group-74.trx:224`; test identity `218cb12e-d5aa-3c64-3af5-47e77953f659`; execution ID `1561c4d7-0b4f-4749-813d-98e312fc1fc3`.
- Disposition: **UNRESOLVED** — No matching pushed repair established; native mechanism remains open.

Error, first 12 lines (all lines if shorter):

```text
ShouldAssertException: JsonSerializer.Deserialize<string>(encoded)
    should be
".antiphon/a b	
é.json"
    but was
".antiphon/a b	
�.json"
    difference
Difference     |                                                                             |                            
               |                                                                            \|/                           
Index          | 0    1    2    3    4    5    6    7    8    9    10   11   12   13   14   15   16   17   18   19   20   
Expected Value | .    a    n    t    i    p    h    o    n    /    a    \s   b    \t   \n   é    .    j    s    o    n
```


Top six stored stack frames (all if fewer):

```text
   at Antiphon.Tests.Scripts.EvidenceDiffGuardTests.Handles_literal_paths_and_case_variants() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Scripts\EvidenceDiffGuardTests.cs:line 223
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at Antiphon.Tests.Scripts.EvidenceDiffGuardTests.Handles_literal_paths_and_case_variants() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Scripts\EvidenceDiffGuardTests.cs:line 207
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at System.Runtime.CompilerServices.TaskAwaiter.ThrowForNonSuccess(Task task)
   at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
```


<a id="f094"></a>

#### F094 — `Antiphon.Tests.Scripts.WindowsUnitPlatformAdmissionTests.C1210_Roster_closure_matches_source_gates`

- Display: `C1210_Roster_closure_matches_source_gates`
- Suite/chunk: `antiphon/all-group-77`; duration `00:00:13.2692334`; manifest row 77.
- Evidence: `antiphon-all-group-77.trx:610`; test identity `d238892d-8a3d-c52b-38b6-416c26b1ab04`; execution ID `b4c5b4ad-db0d-4298-a56a-4a078d85d1b2`.
- Disposition: **ADDRESSED** — cb746f9d / 73ef01b156b5c7ea1c19d07dc80ddc924fc20cdf completes Linux source-gate roster inventory, including NightlyProbeSupervisor.

Error, first 12 lines (all lines if shorter):

```text
ShouldAssertException: entry
    should not be null but was

Additional Info:
    c1210-unrostered-gate: Antiphon.Tests.Scripts.NightlyProbeSupervisorTests.C1375_DeadlineKillsAndJoinsTree
```


Top six stored stack frames (all if fewer):

```text
   at Antiphon.Tests.Scripts.WindowsUnitPlatformAdmissionTests.AssertClosure(SyntaxNode[] roots, Entry[] expected, Boolean requireLegacyHelper) in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Scripts\WindowsUnitPlatformAdmissionTests.cs:line 114
   at Antiphon.Tests.Scripts.WindowsUnitPlatformAdmissionTests.C1210_Roster_closure_matches_source_gates() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Scripts\WindowsUnitPlatformAdmissionTests.cs:line 69
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at System.Runtime.CompilerServices.TaskAwaiter.ThrowForNonSuccess(Task task)
   at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
```


<a id="f116"></a>

#### F116 — `Antiphon.SessionRunner.Tests.CompletedCardWorkspaceRemovalTests.C1032_InspectionHoldShapes`

- Display: `C1032_InspectionHoldShapes(other-repository, stale_mirror_binding)`
- Suite/chunk: `session-runner/all-group-1`; duration `00:00:01.5002793`; manifest row 180.
- Evidence: `session-runner-all-group-1.trx:1056`; test identity `f2b7dcef-3bb4-c483-516b-ddc4accff99f`; execution ID `d0e5597c-a287-423b-af44-29c050bfb695`.
- Disposition: **UNRESOLVED** — No matching pushed repair established; native mechanism remains open.

Error, first 12 lines (all lines if shorter):

```text
UnauthorizedAccessException: Access to the path 'C:\Users\lndco\AppData\Local\Temp\c1032-fence-0ba9292d9f5d46969df1f10e2abda8bb\work\worktrees\task-deadbeef\.git' is denied.
```


Top six stored stack frames (all if fewer):

```text
   at Microsoft.Win32.SafeHandles.SafeFileHandle.CreateFile(String fullPath, FileMode mode, FileAccess access, FileShare share, FileOptions options)
   at Microsoft.Win32.SafeHandles.SafeFileHandle.Open(String fullPath, FileMode mode, FileAccess access, FileShare share, FileOptions options, Int64 preallocationSize, Nullable`1 unixCreateMode)
   at System.IO.File.WriteToFile(String path, FileMode mode, ReadOnlySpan`1 contents, Encoding encoding)
   at System.IO.File.WriteAllText(String path, String contents)
   at Antiphon.SessionRunner.Tests.CompletedCardWorkspaceRemovalTests.C1032_InspectionHoldShapes(String shape, String expectedReason) in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.SessionRunner.Tests\CompletedCardWorkspaceInspectionRepairTests.cs:line 139
   at Antiphon.SessionRunner.Tests.CompletedCardWorkspaceRemovalTests.C1032_InspectionHoldShapes(String shape, String expectedReason) in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.SessionRunner.Tests\CompletedCardWorkspaceInspectionRepairTests.cs:line 164
```


<a id="f117"></a>

#### F117 — `Antiphon.SessionRunner.Tests.CompletedCardWorkspaceRemovalTests.C1032_InspectionHoldShapes`

- Display: `C1032_InspectionHoldShapes(case-twin, case_ambiguous_content)`
- Suite/chunk: `session-runner/all-group-1`; duration `00:00:01.2184576`; manifest row 180.
- Evidence: `session-runner-all-group-1.trx:1075`; test identity `362bd8aa-09e2-b38d-bf0e-205d75e47f76`; execution ID `df4b39d6-d5b2-497d-a5e9-129fe1c1f99f`.
- Disposition: **UNRESOLVED** — No matching pushed repair established; native mechanism remains open.

Error, first 12 lines (all lines if shorter):

```text
ShouldAssertException: Directory.GetFiles(bin).Length
    should be
2
    but was
1

Additional Info:
    requires case-sensitive scratch filesystem
```


Top six stored stack frames (all if fewer):

```text
   at Antiphon.SessionRunner.Tests.CompletedCardWorkspaceRemovalTests.C1032_InspectionHoldShapes(String shape, String expectedReason) in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.SessionRunner.Tests\CompletedCardWorkspaceInspectionRepairTests.cs:line 144
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at Antiphon.SessionRunner.Tests.CompletedCardWorkspaceRemovalTests.C1032_InspectionHoldShapes(String shape, String expectedReason) in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.SessionRunner.Tests\CompletedCardWorkspaceInspectionRepairTests.cs:line 164
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
```


<a id="f118"></a>

#### F118 — `Antiphon.SessionRunner.Tests.CompletedCardWorkspaceRemovalTests.C1017_RunnerRepositoryIdentity`

- Display: `C1017_RunnerRepositoryIdentity`
- Suite/chunk: `session-runner/all-group-1`; duration `00:00:05.3977167`; manifest row 180.
- Evidence: `session-runner-all-group-1.trx:1106`; test identity `5fbb0aa0-c54e-d455-4985-70d4f5dc77e2`; execution ID `84b60754-e8e4-4d9a-be89-c01fd536df9d`.
- Disposition: **UNRESOLVED** — No matching pushed repair established; native mechanism remains open.

Error, first 12 lines (all lines if shorter):

```text
UnauthorizedAccessException: Access to the path 'C:\Users\lndco\AppData\Local\Temp\c1032-fence-54daa6e29ef34cc4b41bd93e7bbffc48\work\worktrees\task-deadbeef\.git' is denied.
```


Top six stored stack frames (all if fewer):

```text
   at Microsoft.Win32.SafeHandles.SafeFileHandle.CreateFile(String fullPath, FileMode mode, FileAccess access, FileShare share, FileOptions options)
   at Microsoft.Win32.SafeHandles.SafeFileHandle.Open(String fullPath, FileMode mode, FileAccess access, FileShare share, FileOptions options, Int64 preallocationSize, Nullable`1 unixCreateMode)
   at System.IO.File.WriteAllBytes(String path, ReadOnlySpan`1 bytes)
   at System.IO.File.WriteAllBytes(String path, Byte[] bytes)
   at Antiphon.SessionRunner.Tests.CompletedCardWorkspaceRemovalTests.C1017_RunnerRepositoryIdentity() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.SessionRunner.Tests\CompletedCardWorkspaceS5Tests.cs:line 96
   at Antiphon.SessionRunner.Tests.CompletedCardWorkspaceRemovalTests.C1017_RunnerRepositoryIdentity() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.SessionRunner.Tests\CompletedCardWorkspaceS5Tests.cs:line 100
```


<a id="f119"></a>

#### F119 — `Antiphon.SessionRunner.Tests.CompletedCardWorkspaceRemovalTests.C1032_RemovalComponentRecovery`

- Display: `C1032_RemovalComponentRecovery`
- Suite/chunk: `session-runner/all-group-1`; duration `00:00:04.0060395`; manifest row 180.
- Evidence: `session-runner-all-group-1.trx:1139`; test identity `880f19fb-ab23-2868-0a9c-baa408ca94fc`; execution ID `7b3b6a63-8019-4028-96ab-010cab001950`.
- Disposition: **ADDRESSED** — a58b9b8c / 95267792ebb16f054d8646cf84059dafea2ec5d5 normalizes crash-worker custody paths; other two Windows filesystem signatures in this class remain open.

Error, first 12 lines (all lines if shorter):

```text
ShouldAssertException: child.HasExited
    should be
False
    but was
True

Additional Info:
    worker must pause at state:Intent
```


Top six stored stack frames (all if fewer):

```text
   at Antiphon.SessionRunner.Tests.CompletedCardWorkspaceRemovalTests.C1032_RemovalComponentRecovery() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.SessionRunner.Tests\CompletedCardWorkspaceS5Tests.cs:line 468
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at Antiphon.SessionRunner.Tests.CompletedCardWorkspaceRemovalTests.C1032_RemovalComponentRecovery() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.SessionRunner.Tests\CompletedCardWorkspaceS5Tests.cs:line 480
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at Antiphon.SessionRunner.Tests.CompletedCardWorkspaceRemovalTests.C1032_RemovalComponentRecovery() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.SessionRunner.Tests\CompletedCardWorkspaceS5Tests.cs:line 514
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
```


<a id="f127"></a>

#### F127 — `Antiphon.SessionRunner.Tests.WorkspaceSourceVerificationWireTests.C1065_RepositoryIdentityReadIsBoundToSessionCheckout`

- Display: `C1065_RepositoryIdentityReadIsBoundToSessionCheckout`
- Suite/chunk: `session-runner/all-group-4`; duration `00:00:01.5132058`; manifest row 183.
- Evidence: `session-runner-all-group-4.trx:113`; test identity `cc9dde60-060e-c782-d524-3e4a743f1a7d`; execution ID `369e968d-573f-4785-a9cd-cd7cc2c3379c`.
- Disposition: **ADDRESSED** — 3a4f6e81 / d6ee905c4fc26421b18a1791e8466a7eb7f59fd8 normalizes Git separators in repository-identity expectation.

Error, first 12 lines (all lines if shorter):

```text
ShouldAssertException: identity.RepositoryIdentity
    should be
"CE2AB49553D479856398E3EADCA639339AD1D5BD88C1B34FB20283053D6613ED"
    but was
"880FF8A0A1662930A512391EAA59CC39BE83F386EB24FCB91B8D8A90BD559521"
    difference
Difference     |  |    |    |    |    |    |    |    |    |    |    |    |    |         |    |    |    |    |    |    |        
               | \|/  \|/  \|/  \|/  \|/  \|/  \|/  \|/  \|/  \|/  \|/  \|/  \|/       \|/  \|/  \|/  \|/  \|/  \|/  \|/       
Index          | 0    1    2    3    4    5    6    7    8    9    10   11   12   13   14   15   16   17   18   19   20   ...  
Expected Value | C    E    2    A    B    4    9    5    5    3    D    4    7    9    8    5    6    3    9    8    E    ...  
Actual Value   | 8    8    0    F    F    8    A    0    A    1    6    6    2    9    3    0    A    5    1    2    3    ...  
Expected Code  | 67   69   50   65   66   52   57   53   53   51   68   52   55   57   56   53   54   51   57   56   69   ...
```


Top six stored stack frames (all if fewer):

```text
   at Antiphon.SessionRunner.Tests.WorkspaceSourceVerificationWireTests.C1065_RepositoryIdentityReadIsBoundToSessionCheckout() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.SessionRunner.Tests\WorkspaceSourceVerificationWireTests.cs:line 33
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at Antiphon.SessionRunner.Tests.WorkspaceSourceVerificationWireTests.C1065_RepositoryIdentityReadIsBoundToSessionCheckout() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.SessionRunner.Tests\WorkspaceSourceVerificationWireTests.cs:line 110
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at Antiphon.SessionRunner.Tests.WorkspaceSourceVerificationWireTests.C1065_RepositoryIdentityReadIsBoundToSessionCheckout() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.SessionRunner.Tests\WorkspaceSourceVerificationWireTests.cs:line 110
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
```


<a id="f128"></a>

#### F128 — `Antiphon.Agents.Pty.Tests.FakeClaudeContractTests.A_doubled_quote_argument_splits_for_a_native_parser_and_not_for_dotnet`

- Display: `A_doubled_quote_argument_splits_for_a_native_parser_and_not_for_dotnet`
- Suite/chunk: `agents-pty/all-group-1`; duration `00:00:00.2791963`; manifest row 236.
- Evidence: `agents-pty-all-group-1.trx:887`; test identity `668e4f15-a85a-f6cf-3d05-426634996745`; execution ID `75ac2bb1-824e-4a22-87eb-d28855b0f0ef`.
- Disposition: **WITNESS-ONLY** — 2fdd8dc8 adds a portable Windows submit witness; its diff does not change doubled-quote argument decoding. The observed mojibake failure has no demonstrated repair.

Error, first 12 lines (all lines if shorter):

```text
ShouldAssertException: looseRaw
    should contain (case insensitive comparison)
"--append-system-prompt␟a "quoted" word"
    but was actually
"--echo-argsÔÉƒ--echo-argv-strictÔÉƒ--append-system-promptÔÉƒa "quoted" word"

Additional Info:
    the .NET parser reports the intended argument INTACT on the shredding line — which is precisely how this went unnoticed for months. Line was: C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Agents.Pty.Tests\bin\Debug\net9.0\fakeclaude\fakeclaude.exe "--echo-args" "--echo-argv-strict" "--append-system-prompt" "a ""quoted"" word"
```


Top six stored stack frames (all if fewer):

```text
   at Antiphon.Agents.Pty.Tests.FakeClaudeContractTests.A_doubled_quote_argument_splits_for_a_native_parser_and_not_for_dotnet() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Agents.Pty.Tests\FakeClaudeContractTests.cs:line 1691
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
```


<a id="f139"></a>

#### F139 — `Antiphon.Tests.Infrastructure.RepositoryChildRecoveryTests.C452_R2_AliasRecoveryUsesCanonicalCommonDirectory`

- Display: `C452_R2_AliasRecoveryUsesCanonicalCommonDirectory`
- Suite/chunk: `antiphon/all-group-65`; duration `00:00:01.0312372`; manifest row 65.
- Evidence: `antiphon-all-group-65-execution/log_261026190647795.diag:7973`; test identity `Antiphon.Tests.Infrastructure.RepositoryChildRecoveryTests.1.1.C452_R2_AliasRecoveryUsesCanonicalCommonDirectory.1.1.0`; execution ID `not recorded in TRX`.
- Disposition: **UNRESOLVED** — No matching pushed repair established; native mechanism remains open.

Error, first 12 lines (all lines if shorter):

```text
ShouldAssertException: raw
    should be
"C:\Users\lndco\AppData\Local\Temp\antiphon-c452-alias-treesk0guhytc.www\real parent\admin"
    but was
"C:/Users/lndco/AppData/Local/Temp/antiphon-c452-alias-treesk0guhytc.www/real parent/admin"
    difference
Difference     |       |                             |                             |                                       |        
               |      \|/                           \|/                           \|/                                     \|/       
Index          | ...  2    3    4    5    6    7    8    9    10   11   12   13   14   15   16   17   18   19   20   21   22   ...  
Expected Value | ...  \    U    s    e    r    s    \    l    n    d    c    o    \    A    p    p    D    a    t    a    \    ...  
Actual Value   | ...  /    U    s    e    r    s    /    l    n    d    c    o    /    A    p    p    D    a    t    a    /    ...  
Expected Code  | ...  92   85   115  101  114  115  92   108  110  100  99   111  92   65   112  112  68   97   116  97   92   ...
```


Top six stored stack frames (all if fewer):

```text
   at Antiphon.Tests.Infrastructure.RepositoryChildRecoveryTests.C452_R2_AliasRecoveryUsesCanonicalCommonDirectory() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Infrastructure\RepositoryChildRecoveryTests.cs:line 268
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
```


<a id="f140"></a>

#### F140 — `Antiphon.Tests.Infrastructure.WorktreeManagerGitIntegrationTests.C1502_CompleteCheckoutStillMarkedIncompleteIsHealed`

- Display: `C1502_CompleteCheckoutStillMarkedIncompleteIsHealed`
- Suite/chunk: `antiphon/all-group-65`; duration `00:00:03.6450578`; manifest row 65.
- Evidence: `antiphon-all-group-65-execution/log_261026190647795.diag:10108`; test identity `Antiphon.Tests.Infrastructure.WorktreeManagerGitIntegrationTests.1.1.C1502_CompleteCheckoutStillMarkedIncompleteIsHealed.1.1.0`; execution ID `not recorded in TRX`.
- Disposition: **ADDRESSED** — Existing pushed 56829468850f38beb64160af991b38925827a0a2 compares Windows registrations with Git separators. Native rerun pending.

Error, first 12 lines (all lines if shorter):

```text
ShouldAssertException: creation.GitDirectory
    should be
"C:\Users\lndco\AppData\Local\Temp\antiphon-worktree-tests-c5e3fa405bf442e78440a7e1c6dca56e\repo\.git\worktrees\card-task-deadbeef"
    but was
"C:/Users/lndco/AppData/Local/Temp/antiphon-worktree-tests-c5e3fa405bf442e78440a7e1c6dca56e/repo/.git/worktrees/card-task-deadbeef"
    difference
Difference     |       |                             |                             |                                       |        
               |      \|/                           \|/                           \|/                                     \|/       
Index          | ...  2    3    4    5    6    7    8    9    10   11   12   13   14   15   16   17   18   19   20   21   22   ...  
Expected Value | ...  \    U    s    e    r    s    \    l    n    d    c    o    \    A    p    p    D    a    t    a    \    ...  
Actual Value   | ...  /    U    s    e    r    s    /    l    n    d    c    o    /    A    p    p    D    a    t    a    /    ...  
Expected Code  | ...  92   85   115  101  114  115  92   108  110  100  99   111  92   65   112  112  68   97   116  97   92   ...
```


Top six stored stack frames (all if fewer):

```text
   at Antiphon.Tests.Infrastructure.WorktreeManagerGitIntegrationTests.C1502_CompleteCheckoutStillMarkedIncompleteIsHealed() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Infrastructure\WorktreeManagerTests.cs:line 311
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
```


### C. Scripted provider readiness, guarded input, remote rules and settlement plumbing (28 results)

Stored evidence shows pending queue rows, missing native UserPrompt receipts, missed fault cuts, unresolved cleanup, Grok remote rules-path refusal, missing settlement and HTTP 500. The 09289b6d fixture changes bind readable Claude dialog generations, preserve conditional input, compose POSIX remote paths and run settlement workers. They address named mechanisms, but do not establish that all native terminal failures share a cause. Grok sign-in, Grok submit-no-output and SessionRunner HTTP 500 retain explicit native uncertainty.

<a id="f005"></a>

#### F005 — `Antiphon.Tests.Application.HerdrAlwaysOnChannelParityTests.AlwaysOn_channel_bound_survives_child_death_and_replies`

- Display: `AlwaysOn_channel_bound_survives_child_death_and_replies(PtyHost)`
- Suite/chunk: `antiphon/all-group-34`; duration `00:00:00.9490657`; manifest row 34.
- Evidence: `antiphon-all-group-34.trx:3504`; test identity `f147b6c4-4ff9-88ce-e03c-5a0339a3ecf5`; execution ID `dc910347-49ab-4ece-8cb7-7c582c3a587a`.
- Disposition: **ADDRESSED** — 09289b6d fixture readiness/input/remote path/settlement changes apply; inspect exact mechanism below and rerun natively before closure.

Error, first 12 lines (all lines if shorter):

```text
ShouldAssertException: queued.Status
    should be
QueuedMessageStatus.Sent
    but was
QueuedMessageStatus.Pending
```


Top six stored stack frames (all if fewer):

```text
   at Antiphon.Tests.Application.HerdrAlwaysOnChannelParityTests.AlwaysOn_channel_bound_survives_child_death_and_replies(SessionBackend backend) in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Application\HerdrAlwaysOnChannelParityTests.cs:line 218
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at Antiphon.Tests.Application.HerdrAlwaysOnChannelParityTests.AlwaysOn_channel_bound_survives_child_death_and_replies(SessionBackend backend) in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Application\HerdrAlwaysOnChannelParityTests.cs:line 228
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at Antiphon.Tests.Application.HerdrAlwaysOnChannelParityTests.AlwaysOn_channel_bound_survives_child_death_and_replies(SessionBackend backend) in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Application\HerdrAlwaysOnChannelParityTests.cs:line 257
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
```


<a id="f006"></a>

#### F006 — `Antiphon.Tests.Application.HerdrAlwaysOnChannelParityTests.AlwaysOn_channel_bound_survives_child_death_and_replies`

- Display: `AlwaysOn_channel_bound_survives_child_death_and_replies(Herdr)`
- Suite/chunk: `antiphon/all-group-34`; duration `00:00:20.4183448`; manifest row 34.
- Evidence: `antiphon-all-group-34.trx:3589`; test identity `00b1ad14-634f-e526-8961-e6551898bc22`; execution ID `9305bb30-6913-4c21-8bcf-5bede3ebeba5`.
- Disposition: **ADDRESSED** — 09289b6d fixture readiness/input/remote path/settlement changes apply; inspect exact mechanism below and rerun natively before closure.

Error, first 12 lines (all lines if shorter):

```text
TimeoutException: Agent fdadde4a-e1df-410b-a705-c974d5695ab4 never reached a Running persistent session. agent=Failed persistent=b12b8878-4559-4c34-b6b5-50a2b8f395d1 live= sessions=[b12b8878-4559-4c34-b6b5-50a2b8f395d1:Failed:Automated Claude input withheld: observation-unknown:None::NonQualifying]
```


Top six stored stack frames (all if fewer):

```text
   at Antiphon.Tests.Application.HerdrAlwaysOnChannelParityTests.WaitForPersistentSessionAsync(Harness harness, Guid agentId) in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Application\HerdrAlwaysOnChannelParityTests.cs:line 712
   at Antiphon.Tests.Application.HerdrAlwaysOnChannelParityTests.WaitForPersistentSessionAsync(Harness harness, Guid agentId) in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Application\HerdrAlwaysOnChannelParityTests.cs:line 712
   at Antiphon.Tests.Application.HerdrAlwaysOnChannelParityTests.AlwaysOn_channel_bound_survives_child_death_and_replies(SessionBackend backend) in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Application\HerdrAlwaysOnChannelParityTests.cs:line 107
   at Antiphon.Tests.Application.HerdrAlwaysOnChannelParityTests.AlwaysOn_channel_bound_survives_child_death_and_replies(SessionBackend backend) in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Application\HerdrAlwaysOnChannelParityTests.cs:line 257
   at Antiphon.Tests.Application.HerdrAlwaysOnChannelParityTests.AlwaysOn_channel_bound_survives_child_death_and_replies(SessionBackend backend) in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Application\HerdrAlwaysOnChannelParityTests.cs:line 264
```


<a id="f007"></a>

#### F007 — `Antiphon.Tests.Application.SessionMessageQueuePtyIntegrationTests.Queued_message_submits_through_the_real_runtime_runner_pty_path`

- Display: `Queued_message_submits_through_the_real_runtime_runner_pty_path`
- Suite/chunk: `antiphon/all-group-35`; duration `00:00:12.1241571`; manifest row 35.
- Evidence: `antiphon-all-group-35.trx:467`; test identity `bbc66679-1fe9-e056-865c-45e3c47bf3ca`; execution ID `dfe007d0-0f15-497e-a160-3287299fcb9a`.
- Disposition: **ADDRESSED** — 09289b6d fixture readiness/input/remote path/settlement changes apply; inspect exact mechanism below and rerun natively before closure.

Error, first 12 lines (all lines if shorter):

```text
ShouldAssertException: submitted
    should be
True
    but was
False

Additional Info:
    a queued message must submit through the real runtime -> runner -> PTY path
```


Top six stored stack frames (all if fewer):

```text
   at Antiphon.Tests.Application.SessionMessageQueuePtyIntegrationTests.Queued_message_submits_through_the_real_runtime_runner_pty_path() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Application\SessionMessageQueuePtyIntegrationTests.cs:line 134
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at Antiphon.Tests.Application.SessionMessageQueuePtyIntegrationTests.Queued_message_submits_through_the_real_runtime_runner_pty_path() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Application\SessionMessageQueuePtyIntegrationTests.cs:line 153
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at Antiphon.Tests.Application.SessionMessageQueuePtyIntegrationTests.Queued_message_submits_through_the_real_runtime_runner_pty_path() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Application\SessionMessageQueuePtyIntegrationTests.cs:line 153
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
```


<a id="f008"></a>

#### F008 — `Antiphon.Tests.Application.SessionMessageQueuePtyIntegrationTests.A_swallowed_enter_is_re_pressed_and_the_body_lands_in_the_transcript_exactly_once`

- Display: `A_swallowed_enter_is_re_pressed_and_the_body_lands_in_the_transcript_exactly_once`
- Suite/chunk: `antiphon/all-group-35`; duration `00:00:01.6926857`; manifest row 35.
- Evidence: `antiphon-all-group-35.trx:514`; test identity `4a0d390e-b964-3d52-222b-507de7801825`; execution ID `02212ad3-d7d8-4ca7-8d42-c516162b9f7c`.
- Disposition: **ADDRESSED** — 09289b6d fixture readiness/input/remote path/settlement changes apply; inspect exact mechanism below and rerun natively before closure.

Error, first 12 lines (all lines if shorter):

```text
ShouldAssertException: snapshot.RawOutput ?? string.Empty
    should contain (case insensitive comparison)
"SWALLOWED-ENTER:"
    but was actually
"\u001b[2J\u001b[m\u001b[H\u001b]0;C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\bin\Debug\net9.0\fakeclaud..."

Additional Info:
    the fake must have eaten the first Enter; without that this asserts nothing
```


Top six stored stack frames (all if fewer):

```text
   at Antiphon.Tests.Application.SessionMessageQueuePtyIntegrationTests.A_swallowed_enter_is_re_pressed_and_the_body_lands_in_the_transcript_exactly_once() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Application\SessionMessageQueuePtyIntegrationTests.cs:line 697
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at Antiphon.Tests.Application.SessionMessageQueuePtyIntegrationTests.A_swallowed_enter_is_re_pressed_and_the_body_lands_in_the_transcript_exactly_once() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Application\SessionMessageQueuePtyIntegrationTests.cs:line 725
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at Antiphon.Tests.Application.SessionMessageQueuePtyIntegrationTests.A_swallowed_enter_is_re_pressed_and_the_body_lands_in_the_transcript_exactly_once() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Application\SessionMessageQueuePtyIntegrationTests.cs:line 725
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
```


<a id="f009"></a>

#### F009 — `Antiphon.Tests.Application.RemoteControlRecoveryPtyIntegrationTests.C514_Idle_modal_clearance_delivers_three_complete_prompts`

- Display: `C514_Idle_modal_clearance_delivers_three_complete_prompts`
- Suite/chunk: `antiphon/all-group-35`; duration `00:00:21.7792411`; manifest row 35.
- Evidence: `antiphon-all-group-35.trx:581`; test identity `048bd99f-d81f-c692-9f1c-e6018088835d`; execution ID `c100dd1b-2a4f-4c74-8973-8574190c4ad3`.
- Disposition: **PARTIAL** — 09289b6d changes the shared RemoteControlPtyLane; existing 335631a4b/58801a729 change input/state setup. Cleanup/persisted-prompt failures are not independently closed.

Error, first 12 lines (all lines if shorter):

```text
AggregateException: test-owned disposal unresolved elapsed=00:00:00.0971358 (cleanup-unresolved elapsed=00:00:00.0915945 (capture session=a2b376e7-545e-4738-8a82-3af873f494b9 manifest=C:\Users\lndco\AppData\Local\Temp\antiphon-c514-pty-a2b376e7545e47388a823af873f494b9\pty-hosts\manifests\a2b376e7545e47388a823af873f494b9.json))
```


Top six stored stack frames (all if fewer):

```text
   at Antiphon.Tests.TestHelpers.TestOwnedPtyHost.DisposeCoreAsync(Boolean kill, Action supplement, Func`2 stop, Func`1 disposeRuntime) in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\TestHelpers\TestOwnedPtyHost.cs:line 157
   at Antiphon.Tests.TestHelpers.RemoteControlPtyLane.DisposeAsync() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\TestHelpers\RemoteControlPtyLane.cs:line 342
   at Antiphon.Tests.Application.RemoteControlRecoveryPtyIntegrationTests.C514_Idle_modal_clearance_delivers_three_complete_prompts() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Application\RemoteControlRecoveryPtyIntegrationTests.cs:line 117
```


<a id="f010"></a>

#### F010 — `Antiphon.Tests.Application.RemoteControlRecoveryPtyIntegrationTests.C514_Health_request_reaches_busy_and_idle_recipients`

- Display: `C514_Health_request_reaches_busy_and_idle_recipients`
- Suite/chunk: `antiphon/all-group-35`; duration `00:00:22.0446075`; manifest row 35.
- Evidence: `antiphon-all-group-35.trx:702`; test identity `6b3d49c9-962f-d6fa-7b02-198963b9fea7`; execution ID `d5e7b0f7-275a-4f65-ab82-ad64f94802d9`.
- Disposition: **PARTIAL** — 09289b6d changes the shared RemoteControlPtyLane; existing 335631a4b/58801a729 change input/state setup. Cleanup/persisted-prompt failures are not independently closed.

Error, first 12 lines (all lines if shorter):

```text
ShouldAssertException: await lane.WaitForPersistedPromptsAsync([work], TimeSpan.FromSeconds(20))
    should be
True
    but was
False

Additional Info:
    the child must record its prompt in its own JSONL
```


Top six stored stack frames (all if fewer):

```text
   at Antiphon.Tests.Application.RemoteControlRecoveryPtyIntegrationTests.C514_Health_request_reaches_busy_and_idle_recipients() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Application\RemoteControlRecoveryPtyIntegrationTests.cs:line 39
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at Antiphon.Tests.Application.RemoteControlRecoveryPtyIntegrationTests.C514_Health_request_reaches_busy_and_idle_recipients() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Application\RemoteControlRecoveryPtyIntegrationTests.cs:line 74
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at System.Runtime.CompilerServices.TaskAwaiter.ThrowForNonSuccess(Task task)
   at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
```


<a id="f011"></a>

#### F011 — `Antiphon.Tests.Application.SessionMessageQueuePtyIntegrationTests.Large_multiline_channel_body_submits_as_one_intact_turn`

- Display: `Large_multiline_channel_body_submits_as_one_intact_turn`
- Suite/chunk: `antiphon/all-group-35`; duration `00:00:01.7565200`; manifest row 35.
- Evidence: `antiphon-all-group-35.trx:741`; test identity `f95d0f73-7d91-0ecf-d3d4-9e4defeb2e0b`; execution ID `e3dd0f3b-c6b0-49c8-aa67-abea0ebc9d8e`.
- Disposition: **ADDRESSED** — 09289b6d fixture readiness/input/remote path/settlement changes apply; inspect exact mechanism below and rerun natively before closure.

Error, first 12 lines (all lines if shorter):

```text
ShouldAssertException: SessionQueueTranscriptPump.FileUserPrompts(transcriptPath)
    should be
["[Telegram "Family" - Mike 01:55] HEAD-MARKER add these to my calendar:

booking line 1 xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx
booking line 2 xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx
booking line 3 xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx
booking line 4 xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx
booking line 5 xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx
booking line 6 xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx
booking line 7 xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx
booking line 8 xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx
```


Top six stored stack frames (all if fewer):

```text
   at Antiphon.Tests.Application.SessionMessageQueuePtyIntegrationTests.Large_multiline_channel_body_submits_as_one_intact_turn() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Application\SessionMessageQueuePtyIntegrationTests.cs:line 512
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at Antiphon.Tests.Application.SessionMessageQueuePtyIntegrationTests.Large_multiline_channel_body_submits_as_one_intact_turn() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Application\SessionMessageQueuePtyIntegrationTests.cs:line 545
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at Antiphon.Tests.Application.SessionMessageQueuePtyIntegrationTests.Large_multiline_channel_body_submits_as_one_intact_turn() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Application\SessionMessageQueuePtyIntegrationTests.cs:line 545
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
```


<a id="f012"></a>

#### F012 — `Antiphon.Tests.Application.SessionMessageQueuePtyIntegrationTests.Batched_multiline_body_passes_composer_delivery_verification_and_submits_once`

- Display: `Batched_multiline_body_passes_composer_delivery_verification_and_submits_once`
- Suite/chunk: `antiphon/all-group-35`; duration `00:00:12.1204747`; manifest row 35.
- Evidence: `antiphon-all-group-35.trx:1107`; test identity `0437cf85-f5fe-1a34-d9f8-3fb672317fff`; execution ID `c20bc120-0c6a-43a2-9485-ce7b3b29b91b`.
- Disposition: **ADDRESSED** — 09289b6d fixture readiness/input/remote path/settlement changes apply; inspect exact mechanism below and rerun natively before closure.

Error, first 12 lines (all lines if shorter):

```text
ShouldAssertException: submitted
    should be
True
    but was
False

Additional Info:
    the batched body must pass verification and submit as one turn
```


Top six stored stack frames (all if fewer):

```text
   at Antiphon.Tests.Application.SessionMessageQueuePtyIntegrationTests.Batched_multiline_body_passes_composer_delivery_verification_and_submits_once() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Application\SessionMessageQueuePtyIntegrationTests.cs:line 389
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at Antiphon.Tests.Application.SessionMessageQueuePtyIntegrationTests.Batched_multiline_body_passes_composer_delivery_verification_and_submits_once() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Application\SessionMessageQueuePtyIntegrationTests.cs:line 413
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at Antiphon.Tests.Application.SessionMessageQueuePtyIntegrationTests.Batched_multiline_body_passes_composer_delivery_verification_and_submits_once() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Application\SessionMessageQueuePtyIntegrationTests.cs:line 413
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
```


<a id="f013"></a>

#### F013 — `Antiphon.Tests.Application.SessionMessageQueuePtyIntegrationTests.A_delivery_whose_every_enter_is_swallowed_reverts_and_never_reaches_the_transcript`

- Display: `A_delivery_whose_every_enter_is_swallowed_reverts_and_never_reaches_the_transcript`
- Suite/chunk: `antiphon/all-group-35`; duration `00:00:02.0298740`; manifest row 35.
- Evidence: `antiphon-all-group-35.trx:1166`; test identity `55d9752e-49ce-f261-b0b7-fe7b248dfa5c`; execution ID `eabf82f1-4fd5-49ed-bd50-fc2201babb3a`.
- Disposition: **ADDRESSED** — 09289b6d fixture readiness/input/remote path/settlement changes apply; inspect exact mechanism below and rerun natively before closure.

Error, first 12 lines (all lines if shorter):

```text
ShouldAssertException: raw.Split("SWALLOWED-ENTER:").Length
    should be
4
    but was
1

Additional Info:
    the confirm loop must press Enter SubmitAttempts times and no more. Screen:
\u001b[2J\u001b[m\u001b[H\u001b]0;C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\bin\Debug\net9.0\fakeclaude\fakeclaude.exe\u0007\u001b[?25hFake Claude ready   
SWALLOWENTER:count=9
\u001b]0;✳\u0007
```


Top six stored stack frames (all if fewer):

```text
   at Antiphon.Tests.Application.SessionMessageQueuePtyIntegrationTests.A_delivery_whose_every_enter_is_swallowed_reverts_and_never_reaches_the_transcript() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Application\SessionMessageQueuePtyIntegrationTests.cs:line 776
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at Antiphon.Tests.Application.SessionMessageQueuePtyIntegrationTests.A_delivery_whose_every_enter_is_swallowed_reverts_and_never_reaches_the_transcript() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Application\SessionMessageQueuePtyIntegrationTests.cs:line 799
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at Antiphon.Tests.Application.SessionMessageQueuePtyIntegrationTests.A_delivery_whose_every_enter_is_swallowed_reverts_and_never_reaches_the_transcript() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Application\SessionMessageQueuePtyIntegrationTests.cs:line 799
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
```


<a id="f015"></a>

#### F015 — `Antiphon.Tests.Application.SessionQueueReceiptPlumbingTests.C475_AlreadyIdleWhenIdleHasRecipientReceipt`

- Display: `C475_AlreadyIdleWhenIdleHasRecipientReceipt`
- Suite/chunk: `antiphon/all-group-36`; duration `00:00:02.6479156`; manifest row 36.
- Evidence: `antiphon-all-group-36.trx:717`; test identity `e4819977-9111-078a-e5cb-0dab6e31687a`; execution ID `1599f563-fcd4-41fd-a6cd-d4747c6a19b6`.
- Disposition: **ADDRESSED** — 09289b6d fixture readiness/input/remote path/settlement changes apply; inspect exact mechanism below and rerun natively before closure.

Error, first 12 lines (all lines if shorter):

```text
ShouldAssertException: dto.Messages
    should be empty but had
1
    item and was
[QueuedMessageDto { Id = 4ca998cf-f78b-41aa-9ed3-ae3ecb0e0dff, Sequence = 1, Body = already idle, Status = Pending, CreatedAt = 26/10/2026 16:45:34, DeliveryAttempts = 0, Origin = Ui, Parked = False, NoteHeader = , ModalBlocked = True }]
```


Top six stored stack frames (all if fewer):

```text
   at Antiphon.Tests.Application.SessionQueueReceiptPlumbingTests.C475_AlreadyIdleWhenIdleHasRecipientReceipt() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Application\SessionQueueReceiptPlumbingTests.cs:line 172
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at Antiphon.Tests.Application.SessionQueueReceiptPlumbingTests.C475_AlreadyIdleWhenIdleHasRecipientReceipt() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Application\SessionQueueReceiptPlumbingTests.cs:line 180
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at System.Runtime.CompilerServices.TaskAwaiter.ThrowForNonSuccess(Task task)
   at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
```


<a id="f016"></a>

#### F016 — `Antiphon.Tests.Application.SessionQueueReceiptPlumbingTests.C1022_Incomplete_or_stale_receipts_do_not_confirm`

- Display: `C1022_Incomplete_or_stale_receipts_do_not_confirm`
- Suite/chunk: `antiphon/all-group-36`; duration `00:00:17.4420661`; manifest row 36.
- Evidence: `antiphon-all-group-36.trx:800`; test identity `31422562-6c41-498f-9573-af0362a6ae07`; execution ID `8673253c-23a6-4d0a-a9d9-9ad23d17c76a`.
- Disposition: **ADDRESSED** — 09289b6d fixture readiness/input/remote path/settlement changes apply; inspect exact mechanism below and rerun natively before closure.

Error, first 12 lines (all lines if shorter):

```text
ShouldAssertException: Task `attempt`
    should throw
System.InvalidOperationException
    but did not
```


Top six stored stack frames (all if fewer):

```text
   at System.Threading.Tasks.ContinuationResultTaskFromResultTask`2.InnerInvoke()
   at System.Threading.ExecutionContext.RunFromThreadPoolDispatchLoop(Thread threadPoolThread, ExecutionContext executionContext, ContextCallback callback, Object state)
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at System.Threading.ExecutionContext.RunFromThreadPoolDispatchLoop(Thread threadPoolThread, ExecutionContext executionContext, ContextCallback callback, Object state)
   at System.Threading.Tasks.Task.ExecuteWithThreadLocal(Task& currentTaskSlot, Thread threadPoolThread)
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
```


<a id="f017"></a>

#### F017 — `Antiphon.Tests.Application.SessionQueueReceiptPlumbingTests.C475_QueueCommitAndTransportRecovery`

- Display: `C475_QueueCommitAndTransportRecovery(receipt-before-verdict)`
- Suite/chunk: `antiphon/all-group-36`; duration `00:00:01.9211807`; manifest row 36.
- Evidence: `antiphon-all-group-36.trx:3105`; test identity `9c790327-f229-bf8f-dc0a-be0597591d56`; execution ID `df20942e-eb02-4b94-b49f-c8ed0035ceb1`.
- Disposition: **ADDRESSED** — 09289b6d fixture readiness/input/remote path/settlement changes apply; inspect exact mechanism below and rerun natively before closure.

Error, first 12 lines (all lines if shorter):

```text
ShouldAssertException: Task `world.Queue.EnqueueAsync(world.SessionId, text, MessageSendMode.WhenIdle, CancellationToken.None)`
    should throw
System.InvalidOperationException
    but did not
```


Top six stored stack frames (all if fewer):

```text
   at System.Threading.Tasks.ContinuationResultTaskFromResultTask`2.InnerInvoke()
   at System.Threading.ExecutionContext.RunFromThreadPoolDispatchLoop(Thread threadPoolThread, ExecutionContext executionContext, ContextCallback callback, Object state)
   at System.Threading.ExecutionContext.RunFromThreadPoolDispatchLoop(Thread threadPoolThread, ExecutionContext executionContext, ContextCallback callback, Object state)
   at System.Threading.Tasks.Task.ExecuteWithThreadLocal(Task& currentTaskSlot, Thread threadPoolThread)
   at Antiphon.Tests.Application.SessionQueueReceiptPlumbingTests.C475_QueueCommitAndTransportRecovery(String cut) in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Application\SessionQueueReceiptPlumbingTests.cs:line 114
   at Antiphon.Tests.Application.SessionQueueReceiptPlumbingTests.C475_QueueCommitAndTransportRecovery(String cut) in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Application\SessionQueueReceiptPlumbingTests.cs:line 156
```


<a id="f018"></a>

#### F018 — `Antiphon.Tests.Application.SessionQueueReceiptPlumbingTests.C475_QueueCommitAndTransportRecovery`

- Display: `C475_QueueCommitAndTransportRecovery(body-before-enter)`
- Suite/chunk: `antiphon/all-group-36`; duration `00:00:01.6995352`; manifest row 36.
- Evidence: `antiphon-all-group-36.trx:3303`; test identity `cc06b78c-a4ae-6625-56fe-b7eb5a488bd6`; execution ID `3e84544e-11c2-498a-af39-0a009a3a2cef`.
- Disposition: **ADDRESSED** — 09289b6d fixture readiness/input/remote path/settlement changes apply; inspect exact mechanism below and rerun natively before closure.

Error, first 12 lines (all lines if shorter):

```text
ShouldAssertException: Task `world.Queue.EnqueueAsync(world.SessionId, text, MessageSendMode.WhenIdle, CancellationToken.None)`
    should throw
System.InvalidOperationException
    but did not
```


Top six stored stack frames (all if fewer):

```text
   at System.Threading.Tasks.ContinuationResultTaskFromResultTask`2.InnerInvoke()
   at System.Threading.ExecutionContext.RunFromThreadPoolDispatchLoop(Thread threadPoolThread, ExecutionContext executionContext, ContextCallback callback, Object state)
   at System.Threading.ExecutionContext.RunFromThreadPoolDispatchLoop(Thread threadPoolThread, ExecutionContext executionContext, ContextCallback callback, Object state)
   at System.Threading.Tasks.Task.ExecuteWithThreadLocal(Task& currentTaskSlot, Thread threadPoolThread)
   at Antiphon.Tests.Application.SessionQueueReceiptPlumbingTests.C475_QueueCommitAndTransportRecovery(String cut) in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Application\SessionQueueReceiptPlumbingTests.cs:line 114
   at Antiphon.Tests.Application.SessionQueueReceiptPlumbingTests.C475_QueueCommitAndTransportRecovery(String cut) in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Application\SessionQueueReceiptPlumbingTests.cs:line 156
```


<a id="f019"></a>

#### F019 — `Antiphon.Tests.Application.SessionQueueReceiptPlumbingTests.C475_QueueCommitAndTransportRecovery`

- Display: `C475_QueueCommitAndTransportRecovery(attempt-before-write)`
- Suite/chunk: `antiphon/all-group-36`; duration `00:00:17.2786900`; manifest row 36.
- Evidence: `antiphon-all-group-36.trx:3477`; test identity `5b5ff3a5-c43a-ba5d-23d5-9c27c41f5d90`; execution ID `c677a3a5-6d59-4e77-b6a2-077a63035470`.
- Disposition: **ADDRESSED** — 09289b6d fixture readiness/input/remote path/settlement changes apply; inspect exact mechanism below and rerun natively before closure.

Error, first 12 lines (all lines if shorter):

```text
ShouldAssertException: Task `attempt`
    should throw
System.InvalidOperationException
    but did not
```


Top six stored stack frames (all if fewer):

```text
   at System.Threading.Tasks.ContinuationResultTaskFromResultTask`2.InnerInvoke()
   at System.Threading.ExecutionContext.RunFromThreadPoolDispatchLoop(Thread threadPoolThread, ExecutionContext executionContext, ContextCallback callback, Object state)
   at System.Threading.ExecutionContext.RunFromThreadPoolDispatchLoop(Thread threadPoolThread, ExecutionContext executionContext, ContextCallback callback, Object state)
   at System.Threading.Tasks.Task.ExecuteWithThreadLocal(Task& currentTaskSlot, Thread threadPoolThread)
   at Antiphon.Tests.Application.SessionQueueReceiptPlumbingTests.C475_QueueCommitAndTransportRecovery(String cut) in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Application\SessionQueueReceiptPlumbingTests.cs:line 92
   at Antiphon.Tests.Application.SessionQueueReceiptPlumbingTests.C475_QueueCommitAndTransportRecovery(String cut) in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Application\SessionQueueReceiptPlumbingTests.cs:line 156
```


<a id="f020"></a>

#### F020 — `Antiphon.Tests.Application.SessionQueueReceiptPlumbingTests.C475_QueueCommitAndTransportRecovery`

- Display: `C475_QueueCommitAndTransportRecovery(pending-before-flush)`
- Suite/chunk: `antiphon/all-group-36`; duration `00:00:17.3130708`; manifest row 36.
- Evidence: `antiphon-all-group-36.trx:3609`; test identity `67ec57fa-31ad-7141-03e9-1d239914dd90`; execution ID `2aee0055-b59c-44b1-b075-d9e1b30f5098`.
- Disposition: **ADDRESSED** — 09289b6d fixture readiness/input/remote path/settlement changes apply; inspect exact mechanism below and rerun natively before closure.

Error, first 12 lines (all lines if shorter):

```text
ShouldAssertException: received
    should be
True
    but was
False

Additional Info:
    recipient-receipt: the whole native UserPrompt must reach the destination
```


Top six stored stack frames (all if fewer):

```text
   at Antiphon.Tests.Application.SessionQueueReceiptPlumbingTests.PtyWorld.WaitForReceiptAsync(String text) in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Application\SessionQueueReceiptPlumbingTests.cs:line 725
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
   at Antiphon.Tests.Application.SessionQueueReceiptPlumbingTests.C475_QueueCommitAndTransportRecovery(String cut) in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Application\SessionQueueReceiptPlumbingTests.cs:line 150
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at Antiphon.Tests.Application.SessionQueueReceiptPlumbingTests.C475_QueueCommitAndTransportRecovery(String cut) in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Application\SessionQueueReceiptPlumbingTests.cs:line 156
```


<a id="f021"></a>

#### F021 — `Antiphon.Tests.Application.SessionQueueReceiptPlumbingTests.C475_QueueCommitAndTransportRecovery`

- Display: `C475_QueueCommitAndTransportRecovery(insert-fails)`
- Suite/chunk: `antiphon/all-group-36`; duration `00:00:17.0877034`; manifest row 36.
- Evidence: `antiphon-all-group-36.trx:3665`; test identity `b8867967-97ef-29f1-e7d3-8bbb5e931ecf`; execution ID `7defd97a-484f-42d4-8a05-60094b13fa19`.
- Disposition: **ADDRESSED** — 09289b6d fixture readiness/input/remote path/settlement changes apply; inspect exact mechanism below and rerun natively before closure.

Error, first 12 lines (all lines if shorter):

```text
ShouldAssertException: received
    should be
True
    but was
False

Additional Info:
    recipient-receipt: the whole native UserPrompt must reach the destination
```


Top six stored stack frames (all if fewer):

```text
   at Antiphon.Tests.Application.SessionQueueReceiptPlumbingTests.PtyWorld.WaitForReceiptAsync(String text) in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Application\SessionQueueReceiptPlumbingTests.cs:line 725
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
   at Antiphon.Tests.Application.SessionQueueReceiptPlumbingTests.C475_QueueCommitAndTransportRecovery(String cut) in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Application\SessionQueueReceiptPlumbingTests.cs:line 150
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at Antiphon.Tests.Application.SessionQueueReceiptPlumbingTests.C475_QueueCommitAndTransportRecovery(String cut) in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Application\SessionQueueReceiptPlumbingTests.cs:line 156
```


<a id="f022"></a>

#### F022 — `Antiphon.Tests.Application.SessionQueueReceiptPlumbingTests.C475_MultilineWritesKeepPasteMarkers`

- Display: `C475_MultilineWritesKeepPasteMarkers`
- Suite/chunk: `antiphon/all-group-36`; duration `00:00:02.3094303`; manifest row 36.
- Evidence: `antiphon-all-group-36.trx:3730`; test identity `fc2f5628-b9c2-00a2-8fd7-067a0c380925`; execution ID `4d450bfa-c7a5-45d4-a43b-addc6aa2fc2a`.
- Disposition: **ADDRESSED** — 09289b6d fixture readiness/input/remote path/settlement changes apply; inspect exact mechanism below and rerun natively before closure.

Error, first 12 lines (all lines if shorter):

```text
ShouldAssertException: world.Forward.Payloads
    should be
["\u001b[200~line one é
line two 世界\u001b[201~", "
"]
    but was (case sensitive comparison)
[] (System.Collections.Generic.List`1[System.String])
    difference
[*, *]

Additional Info:
    normalized LF, both paste markers, then a separate Enter write
```


Top six stored stack frames (all if fewer):

```text
   at Antiphon.Tests.Application.SessionQueueReceiptPlumbingTests.C475_MultilineWritesKeepPasteMarkers() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Application\SessionQueueReceiptPlumbingTests.cs:line 563
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at Antiphon.Tests.Application.SessionQueueReceiptPlumbingTests.C475_MultilineWritesKeepPasteMarkers() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Application\SessionQueueReceiptPlumbingTests.cs:line 570
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
```


<a id="f023"></a>

#### F023 — `Antiphon.Tests.Application.SessionQueueReceiptPlumbingTests.C475_QueueCommitAndTransportRecovery`

- Display: `C475_QueueCommitAndTransportRecovery(recipient-before-ingestion)`
- Suite/chunk: `antiphon/all-group-36`; duration `00:00:02.6079918`; manifest row 36.
- Evidence: `antiphon-all-group-36.trx:3789`; test identity `b41b6401-cdaa-27f7-1722-36103d408ae1`; execution ID `81592668-f3c5-4e2e-bd11-0a2938cea09c`.
- Disposition: **ADDRESSED** — 09289b6d fixture readiness/input/remote path/settlement changes apply; inspect exact mechanism below and rerun natively before closure.

Error, first 12 lines (all lines if shorter):

```text
ShouldAssertException: Task `world.Queue.EnqueueAsync(world.SessionId, text, MessageSendMode.WhenIdle, CancellationToken.None)`
    should throw
System.InvalidOperationException
    but did not
```


Top six stored stack frames (all if fewer):

```text
   at System.Threading.Tasks.ContinuationResultTaskFromResultTask`2.InnerInvoke()
   at System.Threading.ExecutionContext.RunFromThreadPoolDispatchLoop(Thread threadPoolThread, ExecutionContext executionContext, ContextCallback callback, Object state)
   at System.Threading.ExecutionContext.RunFromThreadPoolDispatchLoop(Thread threadPoolThread, ExecutionContext executionContext, ContextCallback callback, Object state)
   at System.Threading.Tasks.Task.ExecuteWithThreadLocal(Task& currentTaskSlot, Thread threadPoolThread)
   at Antiphon.Tests.Application.SessionQueueReceiptPlumbingTests.C475_QueueCommitAndTransportRecovery(String cut) in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Application\SessionQueueReceiptPlumbingTests.cs:line 114
   at Antiphon.Tests.Application.SessionQueueReceiptPlumbingTests.C475_QueueCommitAndTransportRecovery(String cut) in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Application\SessionQueueReceiptPlumbingTests.cs:line 156
```


<a id="f024"></a>

#### F024 — `Antiphon.Tests.Application.CodexCliObservationGapTests.C1029_Per_kind_receipts`

- Display: `C1029_Per_kind_receipts`
- Suite/chunk: `antiphon/all-group-36`; duration `00:03:37.5612551`; manifest row 36.
- Evidence: `antiphon-all-group-36.trx:3843`; test identity `3116f61b-4011-9be3-f49b-8e2130cfd871`; execution ID `8883eb0c-c493-42b0-a574-1a0fdd25d27e`.
- Disposition: **ADDRESSED** — 09289b6d fixture readiness/input/remote path/settlement changes apply; inspect exact mechanism below and rerun natively before closure.

Error, first 12 lines (all lines if shorter):

```text
AggregateException: C1029 per-kind receipt cases failed (grok/remote=True/busy=False) (grok/remote=True/busy=True) (grok-fault/remote=True/busy=False/after=False) (grok-fault/remote=True/busy=False/after=True) (grok-fault/remote=True/busy=True/after=False) (grok-fault/remote=True/busy=True/after=True)
```


Top six stored stack frames (all if fewer):

```text
   at Antiphon.Tests.Application.CodexCliObservationGapTests.C1029_Per_kind_receipts() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Application\CodexCliObservationGapTests.cs:line 127
```


Supplemental remote-path refusal (`antiphon-all-group-36-tests.log`):

```text
362:     C1029 GROK launch status=Failed failure=grok_rules_receipt_invalid: path rules=
365:     C1029 GROK launch status=Failed failure=grok_rules_receipt_invalid: path rules=
```


<a id="f027"></a>

#### F027 — `Antiphon.Tests.Application.SessionMessageQueueGrokPtyIntegrationTests.Queued_message_submits_through_the_real_runtime_runner_pty_path`

- Display: `Queued_message_submits_through_the_real_runtime_runner_pty_path`
- Suite/chunk: `antiphon/all-group-38`; duration `00:00:53.9168666`; manifest row 38.
- Evidence: `antiphon-all-group-38.trx:1979`; test identity `d9c7d474-813d-cf53-cfb5-e9aceda9519b`; execution ID `e2c2405f-ee0e-4c6a-a5d0-7d74739ab8f8`.
- Disposition: **UNRESOLVED** — Existing 6befcc97c fixture repair is post-candidate, but no listed branch proves the submitting-Enter-no-output failure closed.

Error, first 12 lines (all lines if shorter):

```text
ConflictException: Message delivery could not be verified — the terminal did not accept it (the submitting Enter produced no output). See the agent's incidents.
```


Top six stored stack frames (all if fewer):

```text
   at Antiphon.Server.Application.Services.SessionMessageQueueService.EnqueueCoreAsync(Guid sessionId, String body, MessageSendMode mode, QueuedMessageOrigin origin, String conversationKey, Nullable`1 sourceTaskId, String contentDigest, String noteHeader, Nullable`1 sourceScheduleId, Action`1 onCreated, Boolean deliverIfIdle, Nullable`1 holdUntil, Nullable`1 executionDeadlineAt, Nullable`1 executionTaskId, String capacityRecoveryActionKey, Nullable`1 capacityWaitId, Nullable`1 sourceLandNotificationId, Func`3 afterLandQueueInsert, Nullable`1 mentionOccurrenceId, Nullable`1 sourceChannelInboundId, IReadOnlyList`1 channelMemberInboundIds, Nullable`1 reviewThreadId, String runtimeActionKey, CancellationToken ct) in C:\Antiphon\releases\checkout-997186e6\server\Application\Services\SessionMessageQueueService.cs:line 661
   at Antiphon.Tests.Application.SessionMessageQueueGrokPtyIntegrationTests.Queued_message_submits_through_the_real_runtime_runner_pty_path() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Application\SessionMessageQueueGrokPtyIntegrationTests.cs:line 212
   at Antiphon.Tests.Application.SessionMessageQueueGrokPtyIntegrationTests.Queued_message_submits_through_the_real_runtime_runner_pty_path() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Application\SessionMessageQueueGrokPtyIntegrationTests.cs:line 226
   at Antiphon.Tests.Application.SessionMessageQueueGrokPtyIntegrationTests.Queued_message_submits_through_the_real_runtime_runner_pty_path() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Application\SessionMessageQueueGrokPtyIntegrationTests.cs:line 226
```


<a id="f029"></a>

#### F029 — `Antiphon.Tests.Application.GrokDelegateEndToEndTests.an_unmarked_grok_reply_inside_one_tailer_poll_of_the_nudge_settles_unmarked_after_nudge`

- Display: `an_unmarked_grok_reply_inside_one_tailer_poll_of_the_nudge_settles_unmarked_after_nudge`
- Suite/chunk: `antiphon/all-group-49`; duration `00:01:15.2939770`; manifest row 49.
- Evidence: `antiphon-all-group-49.trx:711`; test identity `38ddcde5-6e61-7368-3788-6f9b1767d926`; execution ID `8387b38a-dcf3-4d1f-a64e-7c4297e9b6af`.
- Disposition: **ADDRESSED** — 09289b6d fixture readiness/input/remote path/settlement changes apply; inspect exact mechanism below and rerun natively before closure.

Error, first 12 lines (all lines if shorter):

```text
ShouldAssertException: task cd7e56d1 id=cd7e56d1-fb79-4451-8a7a-fade7a27a1fe status=Dispatched nudgedSeq= boundarySeq=7 nudge.CreatedAt= boundary.CreatedAt=2026-10-26T18:20:34.1666270Z nudge.SentAt=. Screen:
\u001b[1t\u001b[c\u001b[?1004h\u001b[?9001hFake Grok ready
\u001b]0;grok\u0007\u001b[23;1H\u001b[2K\u001b[25;1H  ╭──────────────────────────────────────────────────────────────────────────────────────────────────────────────────╮\u001b[26;1H  │ >                                                                                                                │\u001b[27;1H  ╰────────────────────────────────────────────────────────────────────────────────────── grok-4.7 · always-approve ─╯\u001b[29;1H  Shift+Tab:mode  │  Ctrl+x:shortcuts\u001b[30;1H[antiphon-grok-rules:a75949b5cb2546479944f319bf6fe86f]Read the ENTIRE standing rules file "C:\Users\lndco\AppData\Local\Temp\antiphon-e2e-runner-a1271e35a2b444cdb74b09c3128585cc\instructions\grok\d6a741a74ec84da58832d9f3003d3107\rules.md"; use continuation reads beyond the 1,000-line tool default. Do no task work yet. Version=1 generation=11d3717d9673449799a9f431622176cf sha256=f5ed22d22d62b4a0ad24b56479ddfed705f127eac33dfb2c9207a6f722e81a4c byteCount=9959. On a complete matching read, reply with this standalone line: ANTIPHON_RULES_ACK id=a75949b5cb2546479944f319bf6fe86f generation=11d3717d9673449799a9f431622176cf sha256=f5ed22d22d62b4a0ad24b56479ddfed705f127eac33dfb2c9207a6f722e81a4cOtherwise reply: ANTIPHON_RULES_FAILED id=a75949b5cb2546479944f319bf6fe86f generation=11d3717d9673449799a9f431622176cf reason=unreadable (or incomplete or revision_mismatch).\u001b]0;Responding - grok\u0007
SUBMITTED:[antiphon-grok-rules:a75949b5cb2546479944f319bf6fe86f]Read the ENTIRE standing rules file "C:\Users\lndco\AppData\Local\Temp\antiphon-e2e-runner-a1271e35a2b444cdb74b09c3128585cc\instructions\grok\d6a741a74ec84da58832d9f3003d3107\rules.md"; use continuation reads beyond the 1,000-line tool default. Do no task work yet. Version=1 generation=11d3717d9673449799a9f431622176cf sha256=f5ed22d22d62b4a0ad24b56479ddfed705f127eac33dfb2c9207a6f722e81a4c byteCount=9959. On a complete matching read, reply with this standalone line: ANTIPHON_RULES_ACK id=a75949b5cb2546479944f319bf6fe86f generation=11d3717d9673449799a9f431622176cf sha256=f5ed22d22d62b4a0ad24b56479ddfed705f127eac33dfb2c9207a6f722e81a4cOtherwise reply: ANTIPHON_RULES_FAILED id=a75949b5cb2546479944f319bf6fe86f generation=11d3717d9673449799a9f431622176cf reason=unreadable (or incomplete or revision_mismatch).
ANTIPHON_RULES_ACK id=a75949b5cb2546479944f319bf6fe86f generation=11d3717d9673449799a9f431622176cf sha256=f5ed22d22d62b4a0ad24b56479ddfed705f127eac33dfb2c9207a6f722e81a4c
Worked for 1.7s
\u001b]0;grok\u0007[antiphon-task:cd7e56d1] role=Code tier=Frontier workspace=Shared verification=Final CARD-0329 unmarked nudge [antiphon-task:cd7e56d1] YOUR BRIEF IS NOT IN THIS MESSAGE. It is 3,605 characters — too long to type into a terminal without the transport dropping part of it, so it was written out instead. Read it in full before you do anything else: 'C:\Users\lndco\AppData\Local\Temp\antiphon-grok-e2ecnur1pkc.as3\.antiphon\task-cd7e56d1-brief.md' Everything you need is there. Do not start from this summary. --- how to report back --- The full reporting contract is in the brief above — read it there. Your final message is the entire report the caller receives. Close with the `--- next stage ---` block above the token. [antiphon-task:cd7e56d1]\u001b]0;Responding - grok\u0007
SUBMITTED:[antiphon-task:cd7e56d1] role=Code tier=Frontier workspace=Shared verification=Final CARD-0329 unmarked nudge [antiphon-task:cd7e56d1] YOUR BRIEF IS NOT IN THIS MESSAGE. It is 3,605 characters — too long to type into a terminal without the transport dropping part of it, so it was written out instead. Read it in full before you do anything else: 'C:\Users\lndco\AppData\Local\Temp\antiphon-grok-e2ecnur1pkc.as3\.antiphon\task-cd7e56d1-brief.md' Everything you need is there. Do not start from this summary. --- how to report back --- The full reporting contract is in the brief above — read it there. Your final message is the entire report the caller receives. Close with the `--- next stage ---` block above the token. [antiphon-task:cd7e56d1]
FAKE response to: [antiphon-task:cd7e56d1] role=Code tier=Frontier workspace=S
Worked for 1.7s
\u001b]0;grok\u0007
```


Top six stored stack frames (all if fewer):

```text
   at Antiphon.Tests.Application.GrokDelegateEndToEndTests.WaitUntilAsync(Func`1 condition, TimeSpan timeout, Func`1 failure) in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Application\GrokDelegateEndToEndTests.cs:line 1063
   at Antiphon.Tests.Application.GrokDelegateEndToEndTests.an_unmarked_grok_reply_inside_one_tailer_poll_of_the_nudge_settles_unmarked_after_nudge() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Application\GrokDelegateEndToEndTests.cs:line 285
   at Antiphon.Tests.Application.GrokDelegateEndToEndTests.an_unmarked_grok_reply_inside_one_tailer_poll_of_the_nudge_settles_unmarked_after_nudge() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Application\GrokDelegateEndToEndTests.cs:line 326
   at Antiphon.Tests.Application.GrokDelegateEndToEndTests.an_unmarked_grok_reply_inside_one_tailer_poll_of_the_nudge_settles_unmarked_after_nudge() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Application\GrokDelegateEndToEndTests.cs:line 334
   at Antiphon.Tests.Application.GrokDelegateEndToEndTests.an_unmarked_grok_reply_inside_one_tailer_poll_of_the_nudge_settles_unmarked_after_nudge() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Application\GrokDelegateEndToEndTests.cs:line 334
```


<a id="f030"></a>

#### F030 — `Antiphon.Tests.Application.PhoneHomeCompletedCardTransportTests.C1032_TransportClientUnsupported`

- Display: `C1032_TransportClientUnsupported`
- Suite/chunk: `antiphon/all-group-49`; duration `00:00:31.0872162`; manifest row 49.
- Evidence: `antiphon-all-group-49.trx:2057`; test identity `31a9bcfd-bf8e-5473-14d7-a2be43eb1aa3`; execution ID `d1515894-fb8b-4a14-8af0-803efb3c984d`.
- Disposition: **ADDRESSED** — 09289b6d fixture readiness/input/remote path/settlement changes apply; inspect exact mechanism below and rerun natively before closure.

Error, first 12 lines (all lines if shorter):

```text
TimeoutException: Phone-home live connection was not accepted.
```


Top six stored stack frames (all if fewer):

```text
   at Antiphon.Tests.TestHelpers.PhoneHomeTestHost.WaitLiveAsync(Nullable`1 timeout, String runnerId) in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\TestHelpers\PhoneHomeTestHost.cs:line 200
   at Antiphon.Tests.Application.PhoneHomeCompletedCardTransportTests.World.Connect() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Application\PhoneHomeCompletedCardTransportTests.cs:line 181
   at Antiphon.Tests.Application.PhoneHomeCompletedCardTransportTests.World.Start(Boolean configured) in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Application\PhoneHomeCompletedCardTransportTests.cs:line 150
   at Antiphon.Tests.Application.PhoneHomeCompletedCardTransportTests.World.Start(Boolean configured) in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Application\PhoneHomeCompletedCardTransportTests.cs:line 151
   at Antiphon.Tests.Application.PhoneHomeCompletedCardTransportTests.C1032_TransportClientUnsupported() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Application\PhoneHomeCompletedCardTransportTests.cs:line 25
```


<a id="f031"></a>

#### F031 — `Antiphon.Tests.Application.GrokDelegateEndToEndTests.a_mid_turn_interrupt_refinement_reaches_a_working_fakegrok_delegate_as_a_complete_UserPrompt`

- Display: `a_mid_turn_interrupt_refinement_reaches_a_working_fakegrok_delegate_as_a_complete_UserPrompt`
- Suite/chunk: `antiphon/all-group-49`; duration `00:01:38.2701307`; manifest row 49.
- Evidence: `antiphon-all-group-49.trx:2072`; test identity `ca416394-f5b2-cd25-7fa1-9b3aa43b9490`; execution ID `9461cae3-44e8-4c1d-b847-bdb60fddf8c8`.
- Disposition: **ADDRESSED** — 09289b6d fixture readiness/input/remote path/settlement changes apply; inspect exact mechanism below and rerun natively before closure.

Error, first 12 lines (all lines if shorter):

```text
ShouldAssertException: status=Dispatched reason= sawOpen=True ends=4:end_turn,6:cancelled
\u001b[1t\u001b[c\u001b[?1004h\u001b[?9001hFake Grok ready
\u001b]0;grok\u0007\u001b[23;1H\u001b[2K\u001b[25;1H  ╭──────────────────────────────────────────────────────────────────────────────────────────────────────────────────╮\u001b[26;1H  │ >                                                                                                                │\u001b[27;1H  ╰────────────────────────────────────────────────────────────────────────────────────── grok-4.7 · always-approve ─╯\u001b[29;1H  Shift+Tab:mode  │  Ctrl+x:shortcuts\u001b[30;1H[antiphon-grok-rules:2752078149834605a40ee2ba99ed1f20]Read the ENTIRE standing rules file "C:\Users\lndco\AppData\Local\Temp\antiphon-e2e-runner-9b5fbb676c2b4d00b7cff96b5f7e80fb\instructions\grok\9721dd41f5404086991f047ba92a654e\rules.md"; use continuation reads beyond the 1,000-line tool default. Do no task work yet. Version=1 generation=e8a149ea91b440dbbd1efab247eeeb26 sha256=f5ed22d22d62b4a0ad24b56479ddfed705f127eac33dfb2c9207a6f722e81a4c byteCount=9959. On a complete matching read, reply with this standalone line: ANTIPHON_RULES_ACK id=2752078149834605a40ee2ba99ed1f20 generation=e8a149ea91b440dbbd1efab247eeeb26 sha256=f5ed22d22d62b4a0ad24b56479ddfed705f127eac33dfb2c9207a6f722e81a4cOtherwise reply: ANTIPHON_RULES_FAILED id=2752078149834605a40ee2ba99ed1f20 generation=e8a149ea91b440dbbd1efab247eeeb26 reason=unreadable (or incomplete or revision_mismatch).\u001b]0;Responding - grok\u0007
SUBMITTED:[antiphon-grok-rules:2752078149834605a40ee2ba99ed1f20]Read the ENTIRE standing rules file "C:\Users\lndco\AppData\Local\Temp\antiphon-e2e-runner-9b5fbb676c2b4d00b7cff96b5f7e80fb\instructions\grok\9721dd41f5404086991f047ba92a654e\rules.md"; use continuation reads beyond the 1,000-line tool default. Do no task work yet. Version=1 generation=e8a149ea91b440dbbd1efab247eeeb26 sha256=f5ed22d22d62b4a0ad24b56479ddfed705f127eac33dfb2c9207a6f722e81a4c byteCount=9959. On a complete matching read, reply with this standalone line: ANTIPHON_RULES_ACK id=2752078149834605a40ee2ba99ed1f20 generation=e8a149ea91b440dbbd1efab247eeeb26 sha256=f5ed22d22d62b4a0ad24b56479ddfed705f127eac33dfb2c9207a6f722e81a4cOtherwise reply: ANTIPHON_RULES_FAILED id=2752078149834605a40ee2ba99ed1f20 generation=e8a149ea91b440dbbd1efab247eeeb26 reason=unreadable (or incomplete or revision_mismatch).
ANTIPHON_RULES_ACK id=2752078149834605a40ee2ba99ed1f20 generation=e8a149ea91b440dbbd1efab247eeeb26 sha256=f5ed22d22d62b4a0ad24b56479ddfed705f127eac33dfb2c9207a6f722e81a4c
Worked for 1.7s
\u001b]0;grok\u0007[antiphon-task:83ac8980] role=Code tier=Frontier workspace=Shared verification=Final CARD-0491 interrupt [antiphon-task:83ac8980] YOUR BRIEF IS NOT IN THIS MESSAGE. It is 3,321 characters — too long to type into a terminal without the transport dropping part of it, so it was written out instead. Read it in full before you do anything else: 'C:\Users\lndco\AppData\Local\Temp\antiphon-grok-e2ehyvaomdx.20q\.antiphon\task-83ac8980-brief.md' Everything you need is there. Do not start from this summary. --- how to report back --- The full reporting contract is in the brief above — read it there. Your final message is the entire report the caller receives. Close with the `--- next stage ---` block above the token. [antiphon-task:83ac8980]\u001b]0;Responding - grok\u0007
SUBMITTED:[antiphon-task:83ac8980] role=Code tier=Frontier workspace=Shared verification=Final CARD-0491 interrupt [antiphon-task:83ac8980] YOUR BRIEF IS NOT IN THIS MESSAGE. It is 3,321 characters — too long to type into a terminal without the transport dropping part of it, so it was written out instead. Read it in full before you do anything else: 'C:\Users\lndco\AppData\Local\Temp\antiphon-grok-e2ehyvaomdx.20q\.antiphon\task-83ac8980-brief.md' Everything you need is there. Do not start from this summary. --- how to report back --- The full reporting contract is in the brief above — read it there. Your final message is the entire report the caller receives. Close with the `--- next stage ---` block above the token. [antiphon-task:83ac8980]
REPORT-HOLD
\u001b[28;1H\u001b[2K\u001b[30;1H\u001b[2K\u001b[23;1H\u001b[2K\u001b[25;1H  ╭──────────────────────────────────────────────────────────────────────────────────────────────────────────────────╮\u001b[26;1H  │ >                                                                                                                │\u001b[27;1H  ╰────────────────────────────────────────────────────────────────────────────────────── grok-4.7 · always-approve ─╯\u001b[29;1H  Shift+Tab:mode  │  Ctrl+x:shortcuts\u001b[30;1H\u001b]0;grok\u0007\u001b[28;1H\u001b[2K\u001b[30;1H\u001b[2K\u001b[23;1H\u001b[2K\u001b[25;1H  ╭──────────────────────────────────────────────────────────────────────────────────────────────────────────────────╮\u001b[26;1H  │ >                                                                                                                │\u001b[27;1H  ╰────────────────────────────────────────────────────────────────────────────────────── grok-4.7 · always-approve ─╯\u001b[29;1H  Shift+Tab:mode  │  Ctrl+x:shortcuts\u001b[30;1H
```


Top six stored stack frames (all if fewer):

```text
   at Antiphon.Tests.Application.GrokDelegateEndToEndTests.WaitUntilAsync(Func`1 condition, TimeSpan timeout, Func`1 failure) in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Application\GrokDelegateEndToEndTests.cs:line 1063
   at Antiphon.Tests.Application.GrokDelegateEndToEndTests.a_mid_turn_interrupt_refinement_reaches_a_working_fakegrok_delegate_as_a_complete_UserPrompt() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Application\GrokDelegateEndToEndTests.cs:line 788
   at Antiphon.Tests.Application.GrokDelegateEndToEndTests.a_mid_turn_interrupt_refinement_reaches_a_working_fakegrok_delegate_as_a_complete_UserPrompt() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Application\GrokDelegateEndToEndTests.cs:line 850
   at Antiphon.Tests.Application.GrokDelegateEndToEndTests.a_mid_turn_interrupt_refinement_reaches_a_working_fakegrok_delegate_as_a_complete_UserPrompt() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Application\GrokDelegateEndToEndTests.cs:line 860
   at Antiphon.Tests.Application.GrokDelegateEndToEndTests.a_mid_turn_interrupt_refinement_reaches_a_working_fakegrok_delegate_as_a_complete_UserPrompt() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Application\GrokDelegateEndToEndTests.cs:line 860
```


<a id="f032"></a>

#### F032 — `Antiphon.Tests.Application.GrokDelegateEndToEndTests.a_Kind_Grok_worker_dispatched_from_the_delegate_script_reads_rules_settles_and_prices_on_the_grok_ladder`

- Display: `a_Kind_Grok_worker_dispatched_from_the_delegate_script_reads_rules_settles_and_prices_on_the_grok_ladder`
- Suite/chunk: `antiphon/all-group-49`; duration `00:01:07.9647981`; manifest row 49.
- Evidence: `antiphon-all-group-49.trx:4155`; test identity `6748b93c-8b96-4a76-82a1-fe4edfe43932`; execution ID `ee6a9b27-d0d2-4908-9705-10f2020be9c9`.
- Disposition: **ADDRESSED** — 09289b6d fixture readiness/input/remote path/settlement changes apply; inspect exact mechanism below and rerun natively before closure.

Error, first 12 lines (all lines if shorter):

```text
ShouldAssertException: \u001b[1t\u001b[c\u001b[?1004h\u001b[?9001hFake Grok ready
\u001b]0;grok\u0007\u001b[23;1H\u001b[2K\u001b[25;1H  ╭──────────────────────────────────────────────────────────────────────────────────────────────────────────────────╮\u001b[26;1H  │ >                                                                                                                │\u001b[27;1H  ╰────────────────────────────────────────────────────────────────────────────────────── grok-4.7 · always-approve ─╯\u001b[29;1H  Shift+Tab:mode  │  Ctrl+x:shortcuts\u001b[30;1H[antiphon-grok-rules:de54c6a55eb541889d9748ebfb2ccbb7]Read the ENTIRE standing rules file "C:\Users\lndco\AppData\Local\Temp\antiphon-e2e-runner-d809430ad4df4227b016e5b295b87b81\instructions\grok\557512f7e3df489187b10dc04d497924\rules.md"; use continuation reads beyond the 1,000-line tool default. Do no task work yet. Version=1 generation=90f8bc8baefd4a68a4b08b561fbcb256 sha256=f5ed22d22d62b4a0ad24b56479ddfed705f127eac33dfb2c9207a6f722e81a4c byteCount=9959. On a complete matching read, reply with this standalone line: ANTIPHON_RULES_ACK id=de54c6a55eb541889d9748ebfb2ccbb7 generation=90f8bc8baefd4a68a4b08b561fbcb256 sha256=f5ed22d22d62b4a0ad24b56479ddfed705f127eac33dfb2c9207a6f722e81a4cOtherwise reply: ANTIPHON_RULES_FAILED id=de54c6a55eb541889d9748ebfb2ccbb7 generation=90f8bc8baefd4a68a4b08b561fbcb256 reason=unreadable (or incomplete or revision_mismatch).\u001b]0;Responding - grok\u0007
SUBMITTED:[antiphon-grok-rules:de54c6a55eb541889d9748ebfb2ccbb7]Read the ENTIRE standing rules file "C:\Users\lndco\AppData\Local\Temp\antiphon-e2e-runner-d809430ad4df4227b016e5b295b87b81\instructions\grok\557512f7e3df489187b10dc04d497924\rules.md"; use continuation reads beyond the 1,000-line tool default. Do no task work yet. Version=1 generation=90f8bc8baefd4a68a4b08b561fbcb256 sha256=f5ed22d22d62b4a0ad24b56479ddfed705f127eac33dfb2c9207a6f722e81a4c byteCount=9959. On a complete matching read, reply with this standalone line: ANTIPHON_RULES_ACK id=de54c6a55eb541889d9748ebfb2ccbb7 generation=90f8bc8baefd4a68a4b08b561fbcb256 sha256=f5ed22d22d62b4a0ad24b56479ddfed705f127eac33dfb2c9207a6f722e81a4cOtherwise reply: ANTIPHON_RULES_FAILED id=de54c6a55eb541889d9748ebfb2ccbb7 generation=90f8bc8baefd4a68a4b08b561fbcb256 reason=unreadable (or incomplete or revision_mismatch).
ANTIPHON_RULES_ACK id=de54c6a55eb541889d9748ebfb2ccbb7 generation=90f8bc8baefd4a68a4b08b561fbcb256 sha256=f5ed22d22d62b4a0ad24b56479ddfed705f127eac33dfb2c9207a6f722e81a4c
Worked for 1.7s
\u001b]0;grok\u0007[antiphon-task:f3a4ea32] role=Code tier=Frontier workspace=Shared verification=Final CARD-0084 S6 [antiphon-task:f3a4ea32] YOUR BRIEF IS NOT IN THIS MESSAGE. It is 3,605 characters — too long to type into a terminal without the transport dropping part of it, so it was written out instead. Read it in full before you do anything else: 'C:\Users\lndco\AppData\Local\Temp\antiphon-grok-e2e3y1xbbcn.2rm\.antiphon\task-f3a4ea32-brief.md' Everything you need is there. Do not start from this summary. --- how to report back --- The full reporting contract is in the brief above — read it there. Your final message is the entire report the caller receives. Close with the `--- next stage ---` block above the token. [antiphon-task:f3a4ea32]\u001b]0;Responding - grok\u0007
SUBMITTED:[antiphon-task:f3a4ea32] role=Code tier=Frontier workspace=Shared verification=Final CARD-0084 S6 [antiphon-task:f3a4ea32] YOUR BRIEF IS NOT IN THIS MESSAGE. It is 3,605 characters — too long to type into a terminal without the transport dropping part of it, so it was written out instead. Read it in full before you do anything else: 'C:\Users\lndco\AppData\Local\Temp\antiphon-grok-e2e3y1xbbcn.2rm\.antiphon\task-f3a4ea32-brief.md' Everything you need is there. Do not start from this summary. --- how to report back --- The full reporting contract is in the brief above — read it there. Your final message is the entire report the caller receives. Close with the `--- next stage ---` block above the token. [antiphon-task:f3a4ea32]
FAKE response to: [antiphon-task:f3a4ea32] role=Code tier=Frontier workspace=S
Worked for 1.7s
\u001b]0;grok\u0007
```


Top six stored stack frames (all if fewer):

```text
   at Antiphon.Tests.Application.GrokDelegateEndToEndTests.WaitUntilAsync(Func`1 condition, TimeSpan timeout, Func`1 failure) in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Application\GrokDelegateEndToEndTests.cs:line 1063
   at Antiphon.Tests.Application.GrokDelegateEndToEndTests.a_Kind_Grok_worker_dispatched_from_the_delegate_script_reads_rules_settles_and_prices_on_the_grok_ladder() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Application\GrokDelegateEndToEndTests.cs:line 188
   at Antiphon.Tests.Application.GrokDelegateEndToEndTests.a_Kind_Grok_worker_dispatched_from_the_delegate_script_reads_rules_settles_and_prices_on_the_grok_ladder() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Application\GrokDelegateEndToEndTests.cs:line 217
   at Antiphon.Tests.Application.GrokDelegateEndToEndTests.a_Kind_Grok_worker_dispatched_from_the_delegate_script_reads_rules_settles_and_prices_on_the_grok_ladder() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Application\GrokDelegateEndToEndTests.cs:line 217
   at Antiphon.Tests.Application.GrokDelegateEndToEndTests.a_Kind_Grok_worker_dispatched_from_the_delegate_script_reads_rules_settles_and_prices_on_the_grok_ladder() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Application\GrokDelegateEndToEndTests.cs:line 222
   at Antiphon.Tests.Application.GrokDelegateEndToEndTests.a_Kind_Grok_worker_dispatched_from_the_delegate_script_reads_rules_settles_and_prices_on_the_grok_ladder() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Application\GrokDelegateEndToEndTests.cs:line 222
```


<a id="f035"></a>

#### F035 — `Antiphon.Tests.Application.DelegationBriefCeilingPtyTests.An_oversized_brief_reaches_the_child_whole_through_the_pointer_path`

- Display: `An_oversized_brief_reaches_the_child_whole_through_the_pointer_path`
- Suite/chunk: `antiphon/all-group-54`; duration `00:00:17.4462409`; manifest row 54.
- Evidence: `antiphon-all-group-54.trx:452`; test identity `273dcfe3-190f-fd49-f38b-07bc9a35d1b5`; execution ID `37a52ac1-f85b-4da9-b939-aaee8868d6c3`.
- Disposition: **ADDRESSED** — 09289b6d fixture readiness/input/remote path/settlement changes apply; inspect exact mechanism below and rerun natively before closure.

Error, first 12 lines (all lines if shorter):

```text
ShouldAssertException: arrived
    should be
True
    but was
False

Additional Info:
    the pointer must reach the child whole through a CLIPPING receiver — it is the one thing standing between a delegate and a headless brief. Raw:
\u001b[2J\u001b[m\u001b[H\u001b]0;C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\bin\Debug\net9.0\fakeclaude\fakeclaude.exe\u0007\u001b[?25hFake Claude ready\u001b[51X
CLIP:mode=deterministic chunkBytes=1024 keep=last seed=1 splitPct=25
\u001b]0;✳\u0007
```


Top six stored stack frames (all if fewer):

```text
   at Antiphon.Tests.Application.DelegationBriefCeilingPtyTests.An_oversized_brief_reaches_the_child_whole_through_the_pointer_path() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Application\DelegationBriefCeilingPtyTests.cs:line 108
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at Antiphon.Tests.Application.DelegationBriefCeilingPtyTests.An_oversized_brief_reaches_the_child_whole_through_the_pointer_path() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Application\DelegationBriefCeilingPtyTests.cs:line 117
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at System.Runtime.CompilerServices.TaskAwaiter.ThrowForNonSuccess(Task task)
   at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
```


<a id="f036"></a>

#### F036 — `Antiphon.Tests.Application.DelegationBriefCeilingPtyTests.The_same_brief_typed_inline_silently_loses_a_whole_chunk`

- Display: `The_same_brief_typed_inline_silently_loses_a_whole_chunk`
- Suite/chunk: `antiphon/all-group-54`; duration `00:00:16.4120432`; manifest row 54.
- Evidence: `antiphon-all-group-54.trx:500`; test identity `91af6e0f-6bcd-0560-6c73-adb0ab4dac7e`; execution ID `39b9561f-3462-4b13-bd99-61c906420bdc`.
- Disposition: **ADDRESSED** — 09289b6d fixture readiness/input/remote path/settlement changes apply; inspect exact mechanism below and rerun natively before closure.

Error, first 12 lines (all lines if shorter):

```text
ShouldAssertException: submitted
    should be
True
    but was
False

Additional Info:
    something must submit. Raw:
\u001b[2J\u001b[m\u001b[H\u001b]0;C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\bin\Debug\net9.0\fakeclaude\fakeclaude.exe\u0007\u001b[?25hFake Claude ready\u001b[51X
CLIP:mode=deterministic chunkBytes=1024 keep=last seed=1 splitPct=25
\u001b]0;✳\u0007
```


Top six stored stack frames (all if fewer):

```text
   at Antiphon.Tests.Application.DelegationBriefCeilingPtyTests.The_same_brief_typed_inline_silently_loses_a_whole_chunk() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Application\DelegationBriefCeilingPtyTests.cs:line 149
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at Antiphon.Tests.Application.DelegationBriefCeilingPtyTests.The_same_brief_typed_inline_silently_loses_a_whole_chunk() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Application\DelegationBriefCeilingPtyTests.cs:line 162
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
```


<a id="f037"></a>

#### F037 — `Antiphon.Tests.Application.GrokSignInRuntimeTests.A_sign_in_mode_fakegrok_pool_task_never_types_the_brief`

- Display: `A_sign_in_mode_fakegrok_pool_task_never_types_the_brief`
- Suite/chunk: `antiphon/all-group-58`; duration `00:00:31.5917129`; manifest row 58.
- Evidence: `antiphon-all-group-58.trx:301`; test identity `7d5c9f60-7133-40d4-429f-4a36c1a4e710`; execution ID `947e36e2-4f1f-4c41-8a3f-21449f4426f3`.
- Disposition: **UNRESOLVED** — No matching pushed repair established; native mechanism remains open.

Error, first 12 lines (all lines if shorter):

```text
TaskCanceledException: A task was canceled.
```


Top six stored stack frames (all if fewer):

```text
   at Antiphon.Server.Application.Services.AgentSessionLaunchQueue.WaitForIdleAsync(TimeSpan timeout, CancellationToken ct) in C:\Antiphon\releases\checkout-997186e6\server\Application\Services\AgentSessionLaunchQueue.cs:line 216
   at Antiphon.Tests.Application.GrokSignInRuntimeTests.A_sign_in_mode_fakegrok_pool_task_never_types_the_brief() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Application\GrokSignInRuntimeTests.cs:line 109
   at Antiphon.Tests.Application.GrokSignInRuntimeTests.A_sign_in_mode_fakegrok_pool_task_never_types_the_brief() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Application\GrokSignInRuntimeTests.cs:line 161
   at Antiphon.Tests.Application.GrokSignInRuntimeTests.A_sign_in_mode_fakegrok_pool_task_never_types_the_brief() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Application\GrokSignInRuntimeTests.cs:line 161
```


<a id="f126"></a>

#### F126 — `Antiphon.SessionRunner.Tests.GrokRulesHttpAcceptanceTests.Real_http_capability_and_session_receipts_exclude_rules_body_with_herdr_disabled`

- Display: `Real_http_capability_and_session_receipts_exclude_rules_body_with_herdr_disabled`
- Suite/chunk: `session-runner/all-group-4`; duration `00:00:01.8588234`; manifest row 183.
- Evidence: `session-runner-all-group-4.trx:78`; test identity `018a93e0-f673-ed7d-8f51-63b279a7433d`; execution ID `b66a10fa-42ea-4505-b16c-71280e1f32cc`.
- Disposition: **UNRESOLVED** — No matching pushed repair established; native mechanism remains open.

Error, first 12 lines (all lines if shorter):

```text
ShouldAssertException: started.StatusCode
    should be
HttpStatusCode.Created
    but was
HttpStatusCode.InternalServerError
```


Top six stored stack frames (all if fewer):

```text
   at Antiphon.SessionRunner.Tests.GrokRulesHttpAcceptanceTests.Real_http_capability_and_session_receipts_exclude_rules_body_with_herdr_disabled(CancellationToken ct) in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.SessionRunner.Tests\GrokRulesHttpAcceptanceTests.cs:line 63
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at Antiphon.SessionRunner.Tests.GrokRulesHttpAcceptanceTests.Real_http_capability_and_session_receipts_exclude_rules_body_with_herdr_disabled(CancellationToken ct) in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.SessionRunner.Tests\GrokRulesHttpAcceptanceTests.cs:line 114
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at System.Runtime.CompilerServices.TaskAwaiter.ThrowForNonSuccess(Task task)
   at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
```


### D. Scheduling, cancellation, process lifetime and elapsed-time boundaries (6 results)

Outstanding inspection work remains 1; hosted overflow recovery/cancellation barriers time out; a Windows ownership probe loses its root; the slow-push budget spends 30.4189208 seconds against a less-than-15-second expectation. These are separate observed boundaries. No common scheduling root is established. Capture queue/action timestamps, child start/exit and cancellation/drain phases in exact native repros.

<a id="f001"></a>

#### F001 — `Antiphon.Tests.Application.DispatcherSiblingInspectionSchedulingTests.C1349_InspectionQueueRecovers`

- Display: `C1349_InspectionQueueRecovers(fairness)`
- Suite/chunk: `antiphon/all-group-19`; duration `00:00:16.7432972`; manifest row 19.
- Evidence: `antiphon-all-group-19.trx:1035`; test identity `4e1a114b-240b-fdde-913f-90f699c6a3c4`; execution ID `971c0351-1ecc-4290-90d6-459196fd55a9`.
- Disposition: **UNRESOLVED** — No matching pushed repair established; native mechanism remains open.

Error, first 12 lines (all lines if shorter):

```text
ShouldAssertException: f.Queue.Outstanding
    should be
0
    but was
1
```


Top six stored stack frames (all if fewer):

```text
   at Antiphon.Tests.Application.DispatcherSiblingInspectionSchedulingTests.C1349_InspectionQueueRecovers(String scenario) in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Application\DispatcherSiblingInspectionSchedulingTests.cs:line 377
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at Antiphon.Tests.Application.DispatcherSiblingInspectionSchedulingTests.C1349_InspectionQueueRecovers(String scenario) in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Application\DispatcherSiblingInspectionSchedulingTests.cs:line 401
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at System.Runtime.CompilerServices.TaskAwaiter.ThrowForNonSuccess(Task task)
   at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
```


<a id="f003"></a>

#### F003 — `Antiphon.Tests.Application.SessionRuntimeActionTests.C1259_saturation_keeps_ingestion_live_and_recovers_overflow`

- Display: `C1259_saturation_keeps_ingestion_live_and_recovers_overflow(poison-page)`
- Suite/chunk: `antiphon/all-group-29`; duration `00:00:11.9641459`; manifest row 29.
- Evidence: `antiphon-all-group-29.trx:4467`; test identity `bff40ae1-85e2-a475-741d-10a7bd39e970`; execution ID `0fa2dbb1-d263-4af6-bacc-b0405b320fb0`.
- Disposition: **UNRESOLVED** — No matching pushed repair established; native mechanism remains open.

Error, first 12 lines (all lines if shorter):

```text
TimeoutException: The operation has timed out.
```


Top six stored stack frames (all if fewer):

```text
   at Antiphon.Tests.Application.SessionRuntimeActionTests.VerifyHostedExecutionPageYieldAsync() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Application\SessionRuntimeActionTests.cs:line 580
   at Antiphon.Tests.Application.SessionRuntimeActionTests.VerifyHostedExecutionPageYieldAsync() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Application\SessionRuntimeActionTests.cs:line 603
   at Antiphon.Tests.Application.SessionRuntimeActionTests.VerifyHostedExecutionPageYieldAsync() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Application\SessionRuntimeActionTests.cs:line 603
   at Antiphon.Tests.Application.SessionRuntimeActionTests.C1259_saturation_keeps_ingestion_live_and_recovers_overflow(String variant) in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Application\SessionRuntimeActionTests.cs:line 248
```


<a id="f072"></a>

#### F072 — `Antiphon.Tests.Checkpoints.C1307CliCancellationTests.Main_interrupt_reaches_command_and_unsubscribes`

- Display: `Main_interrupt_reaches_command_and_unsubscribes`
- Suite/chunk: `antiphon/all-group-63`; duration `00:00:09.4743584`; manifest row 63.
- Evidence: `antiphon-all-group-63.trx:817`; test identity `690314bd-172d-94d6-f1a7-97eb56de1b3e`; execution ID `c4b8119c-7436-434a-800a-6931734bd20a`.
- Disposition: **NO-REPRO** — a043b0a4 reran the class green on Linux; no proven link from these timeouts to its read-mode repair.

Error, first 12 lines (all lines if shorter):

```text
TimeoutException: The operation has timed out.
```


Top six stored stack frames (all if fewer):

```text
   at Antiphon.Tests.Checkpoints.C1307CliCancellationTests.Main_interrupt_reaches_command_and_unsubscribes() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Checkpoints\C1307CliCancellationTests.cs:line 109
   at Antiphon.Tests.Checkpoints.C1307CliCancellationTests.Main_interrupt_reaches_command_and_unsubscribes() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Checkpoints\C1307CliCancellationTests.cs:line 128
```


<a id="f073"></a>

#### F073 — `Antiphon.Tests.Checkpoints.C1307CliCancellationTests.Direct_row_cancel_reaches_slot_and_driver`

- Display: `Direct_row_cancel_reaches_slot_and_driver`
- Suite/chunk: `antiphon/all-group-63`; duration `00:00:09.4728261`; manifest row 63.
- Evidence: `antiphon-all-group-63.trx:839`; test identity `601d96ab-4f96-494a-c714-b2af9f727fc5`; execution ID `5f8381f5-ec2a-49f0-90ef-be1fff66fcb5`.
- Disposition: **NO-REPRO** — a043b0a4 reran the class green on Linux; no proven link from these timeouts to its read-mode repair.

Error, first 12 lines (all lines if shorter):

```text
TimeoutException: The operation has timed out.
```


Top six stored stack frames (all if fewer):

```text
   at Antiphon.Tests.Checkpoints.C1307CliCancellationTests.Direct_row_cancel_reaches_slot_and_driver() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Checkpoints\C1307CliCancellationTests.cs:line 226
   at Antiphon.Tests.Checkpoints.C1307CliCancellationTests.Direct_row_cancel_reaches_slot_and_driver() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Checkpoints\C1307CliCancellationTests.cs:line 240
```


<a id="f083"></a>

#### F083 — `Antiphon.Tests.Scripts.ScriptHarnessWindowsOwnershipTests.Only_standard_handles_are_inherited`

- Display: `Only_standard_handles_are_inherited`
- Suite/chunk: `antiphon/all-group-75`; duration `00:00:00.9588811`; manifest row 75.
- Evidence: `antiphon-all-group-75.trx:1874`; test identity `1eb9e621-ba34-1e40-651a-ab810d7641ec`; execution ID `eaed7bc5-1694-4485-83f3-7d9ee12342bf`.
- Disposition: **UNRESOLVED** — No matching pushed repair established; native mechanism remains open.

Error, first 12 lines (all lines if shorter):

```text
InvalidOperationException: Root exited before handle probe.
```


Top six stored stack frames (all if fewer):

```text
   at Antiphon.Tests.Scripts.ScriptHarnessWindowsProcessFixture.WaitProbeAsync(Task root) in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Scripts\ScriptHarnessWindowsProcessFixture.cs:line 86
   at Antiphon.Tests.Scripts.ScriptHarnessWindowsOwnershipTests.<>c__DisplayClass8_0.<<Only_standard_handles_are_inherited>b__0>d.MoveNext() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Scripts\ScriptHarnessWindowsOwnershipTests.cs:line 220
   at Antiphon.Tests.Scripts.ScriptHarnessWindowsProcessFixture.<>c__DisplayClass24_1.<<WithOwnerAsync>b__0>d.MoveNext() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Scripts\ScriptHarnessWindowsProcessFixture.cs:line 127
   at Antiphon.Tests.Scripts.ScriptHarnessWindowsProcessFixture.WithOwnerAsync(String caseName, WindowsScriptHarnessHooks hooks, Func`2 body, Action`1 configure, Boolean drainStreams) in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Scripts\ScriptHarnessWindowsProcessFixture.cs:line 124
   at Antiphon.Tests.Scripts.ScriptHarnessWindowsProcessFixture.WithOwnerAsync(String caseName, WindowsScriptHarnessHooks hooks, Func`2 body, Action`1 configure, Boolean drainStreams) in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Scripts\ScriptHarnessWindowsProcessFixture.cs:line 137
   at Antiphon.Tests.Scripts.ScriptHarnessWindowsOwnershipTests.Only_standard_handles_are_inherited() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Scripts\ScriptHarnessWindowsOwnershipTests.cs:line 218
```


<a id="f138"></a>

#### F138 — `Antiphon.Tests.Infrastructure.LandingGitTests.C1076_RunOptionsBudgetKillsASlowPushAndClearsItsJournal`

- Display: `C1076_RunOptionsBudgetKillsASlowPushAndClearsItsJournal`
- Suite/chunk: `antiphon/all-group-65`; duration `00:00:32.0991607`; manifest row 65.
- Evidence: `antiphon-all-group-65-execution/log_261026190647795.diag:6534`; test identity `Antiphon.Tests.Infrastructure.LandingGitTests.1.1.C1076_RunOptionsBudgetKillsASlowPushAndClearsItsJournal(System.Threading.CancellationToken).1.1.0`; execution ID `not recorded in TRX`.
- Disposition: **UNRESOLVED** — No matching pushed repair established; native mechanism remains open.

Error, first 12 lines (all lines if shorter):

```text
ShouldAssertException: watch.Elapsed
    should be less than
00:00:15
    but was
00:00:30.4189208
```


Top six stored stack frames (all if fewer):

```text
   at Antiphon.Tests.Infrastructure.LandingGitTests.C1076_RunOptionsBudgetKillsASlowPushAndClearsItsJournal(CancellationToken ct) in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Infrastructure\LandingGitTests.cs:line 573
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
```


### E. Land graph audit, SDK provenance and Windows Git-object fixtures (13 results)

F002 explicitly names a disallowed current-snapshots.md Content glob. The pushed 052479a2cbee6380df718d66afbcb5d5470073ab fixture rename addresses it. LandNonCodeSkip additionally shows unrecognized-build-graph fallback, registry digest drift, missing-NUL acceptance, and Git refusing a trailing-dot path. Branch 7cdaefee changes SDK evaluation/provenance and graph checks; it is relevant to audit fallback, not proof that missing-NUL or trailing-dot failures are closed.

<a id="f002"></a>

#### F002 — `Antiphon.Tests.Application.LandGraphEvaluationTests.C1483_S5_CheckRequiresEvaluationRecord`

- Display: `C1483_S5_CheckRequiresEvaluationRecord`
- Suite/chunk: `antiphon/all-group-26`; duration `00:00:35.7066861`; manifest row 26.
- Evidence: `antiphon-all-group-26.trx:1476`; test identity `7b9aecfb-4830-1e1d-838c-ea05d46c1083`; execution ID `7dfd11b0-5f56-4d41-89ef-d6f52d7ddfc3`.
- Disposition: **ADDRESSED** — Existing pushed 052479a2cbee6380df718d66afbcb5d5470073ab renames Card0418 documents to .fixture; newer SDK-audit issues are separate.

Error, first 12 lines (all lines if shorter):

```text
ShouldAssertException: real.ExitCode
    should be
0
    but was
1

Additional Info:
    definition file item selects a markdown or text document the audit does not allow; -Update cannot approve it: tests/Antiphon.Tests/Antiphon.Tests.csproj Content Include=Fixtures\Card0418\** -> tests/Antiphon.Tests/Fixtures/Card0418/current-snapshots.md
audit refused: land graph pre-check refused
```


Top six stored stack frames (all if fewer):

```text
   at Antiphon.Tests.Application.LandGraphEvaluationTests.C1483_S5_CheckRequiresEvaluationRecord() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Application\LandGraphEvaluationScriptTests.cs:line 123
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at Antiphon.Tests.Application.LandGraphEvaluationTests.C1483_S5_CheckRequiresEvaluationRecord() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Application\LandGraphEvaluationScriptTests.cs:line 123
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
```


<a id="f097"></a>

#### F097 — `Antiphon.Tests.Application.LandNonCodeSkipTests.C1380_RegularDocumentationChangesSkip`

- Display: `C1380_RegularDocumentationChangesSkip(add)`
- Suite/chunk: `antiphon/all-split-14`; duration `00:00:29.3492993`; manifest row 95.
- Evidence: `antiphon-all-split-14.trx:74`; test identity `fedce3f4-f725-dec8-af7d-18d9bf0f903d`; execution ID `0fbe118f-1415-4a5b-a9ae-1a3a101faf1c`.
- Disposition: **PARTIAL** — 7cdaefee SDK provenance/evaluation repair is relevant to audit fallback/digest errors. Existing git.exe fixture resolution also applies. Exact native cases remain unverified.

Error, first 12 lines (all lines if shorter):

```text
ShouldAssertException: h.Verifier.Calls
    should be
0
    but was
1

Additional Info:
    publication=Landed; reason=; command=dotnet build; dotnet run --project tests/Antiphon.Tests; policy=unrecognized-build-graph
```


Top six stored stack frames (all if fewer):

```text
   at Antiphon.Tests.Application.LandNonCodeSkipTests.LandAndAssertAsync(LandingSafetyHarness h, Boolean build, String filter, String target) in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Application\LandNonCodeSkipTests.cs:line 2675
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at System.Runtime.CompilerServices.TaskAwaiter.ThrowForNonSuccess(Task task)
   at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
   at Antiphon.Tests.Application.LandNonCodeSkipTests.C1380_RegularDocumentationChangesSkip(String variant) in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Application\LandNonCodeSkipTests.cs:line 168
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
```


<a id="f098"></a>

#### F098 — `Antiphon.Tests.Application.LandNonCodeSkipTests.C1380_RegularDocumentationChangesSkip`

- Display: `C1380_RegularDocumentationChangesSkip(empty)`
- Suite/chunk: `antiphon/all-split-14`; duration `00:00:29.0345843`; manifest row 95.
- Evidence: `antiphon-all-split-14.trx:125`; test identity `a2c8fc26-2818-4d08-99eb-e1d2bc1d48cf`; execution ID `36cbc252-6d9e-424e-80ac-61a048faa5b1`.
- Disposition: **PARTIAL** — 7cdaefee SDK provenance/evaluation repair is relevant to audit fallback/digest errors. Existing git.exe fixture resolution also applies. Exact native cases remain unverified.

Error, first 12 lines (all lines if shorter):

```text
ShouldAssertException: h.Verifier.Calls
    should be
0
    but was
1

Additional Info:
    publication=Landed; reason=; command=dotnet build; dotnet run --project tests/Antiphon.Tests; policy=unrecognized-build-graph
```


Top six stored stack frames (all if fewer):

```text
   at Antiphon.Tests.Application.LandNonCodeSkipTests.LandAndAssertAsync(LandingSafetyHarness h, Boolean build, String filter, String target) in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Application\LandNonCodeSkipTests.cs:line 2675
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at System.Runtime.CompilerServices.TaskAwaiter.ThrowForNonSuccess(Task task)
   at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
   at Antiphon.Tests.Application.LandNonCodeSkipTests.C1380_RegularDocumentationChangesSkip(String variant) in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Application\LandNonCodeSkipTests.cs:line 168
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
```


<a id="f099"></a>

#### F099 — `Antiphon.Tests.Application.LandNonCodeSkipTests.C1380_RegularDocumentationChangesSkip`

- Display: `C1380_RegularDocumentationChangesSkip(README)`
- Suite/chunk: `antiphon/all-split-14`; duration `00:00:30.3008055`; manifest row 95.
- Evidence: `antiphon-all-split-14.trx:164`; test identity `22802ca7-b33d-b6b9-89b2-24d3e554424c`; execution ID `ca18cec2-0535-4602-b7f5-e51fd16292c5`.
- Disposition: **PARTIAL** — 7cdaefee SDK provenance/evaluation repair is relevant to audit fallback/digest errors. Existing git.exe fixture resolution also applies. Exact native cases remain unverified.

Error, first 12 lines (all lines if shorter):

```text
ShouldAssertException: h.Verifier.Calls
    should be
0
    but was
1

Additional Info:
    publication=Landed; reason=; command=dotnet build; dotnet run --project tests/Antiphon.Tests; policy=unrecognized-build-graph
```


Top six stored stack frames (all if fewer):

```text
   at Antiphon.Tests.Application.LandNonCodeSkipTests.LandAndAssertAsync(LandingSafetyHarness h, Boolean build, String filter, String target) in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Application\LandNonCodeSkipTests.cs:line 2675
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at System.Runtime.CompilerServices.TaskAwaiter.ThrowForNonSuccess(Task task)
   at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
   at Antiphon.Tests.Application.LandNonCodeSkipTests.C1380_RegularDocumentationChangesSkip(String variant) in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Application\LandNonCodeSkipTests.cs:line 168
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
```


<a id="f100"></a>

#### F100 — `Antiphon.Tests.Application.LandNonCodeSkipTests.C1380_RegularDocumentationChangesSkip`

- Display: `C1380_RegularDocumentationChangesSkip(rename)`
- Suite/chunk: `antiphon/all-split-14`; duration `00:00:32.5708875`; manifest row 95.
- Evidence: `antiphon-all-split-14.trx:197`; test identity `9835d039-b9df-a5ea-aa91-21495fb14f0b`; execution ID `18d3751e-49f1-461c-b180-7879d8301393`.
- Disposition: **PARTIAL** — 7cdaefee SDK provenance/evaluation repair is relevant to audit fallback/digest errors. Existing git.exe fixture resolution also applies. Exact native cases remain unverified.

Error, first 12 lines (all lines if shorter):

```text
ShouldAssertException: h.Verifier.Calls
    should be
0
    but was
1

Additional Info:
    publication=Landed; reason=; command=dotnet build; dotnet run --project tests/Antiphon.Tests; policy=unrecognized-build-graph
```


Top six stored stack frames (all if fewer):

```text
   at Antiphon.Tests.Application.LandNonCodeSkipTests.LandAndAssertAsync(LandingSafetyHarness h, Boolean build, String filter, String target) in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Application\LandNonCodeSkipTests.cs:line 2675
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at System.Runtime.CompilerServices.TaskAwaiter.ThrowForNonSuccess(Task task)
   at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
   at Antiphon.Tests.Application.LandNonCodeSkipTests.C1380_RegularDocumentationChangesSkip(String variant) in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Application\LandNonCodeSkipTests.cs:line 168
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
```


<a id="f101"></a>

#### F101 — `Antiphon.Tests.Application.LandNonCodeSkipTests.C1380_RegularDocumentationChangesSkip`

- Display: `C1380_RegularDocumentationChangesSkip(edit)`
- Suite/chunk: `antiphon/all-split-14`; duration `00:00:31.2468218`; manifest row 95.
- Evidence: `antiphon-all-split-14.trx:230`; test identity `462e7440-a740-3fa5-588d-b12828208c32`; execution ID `d33b1dc9-d724-448b-8658-43721a7f35e6`.
- Disposition: **PARTIAL** — 7cdaefee SDK provenance/evaluation repair is relevant to audit fallback/digest errors. Existing git.exe fixture resolution also applies. Exact native cases remain unverified.

Error, first 12 lines (all lines if shorter):

```text
ShouldAssertException: h.Verifier.Calls
    should be
0
    but was
1

Additional Info:
    publication=Landed; reason=; command=dotnet build; dotnet run --project tests/Antiphon.Tests; policy=unrecognized-build-graph
```


Top six stored stack frames (all if fewer):

```text
   at Antiphon.Tests.Application.LandNonCodeSkipTests.LandAndAssertAsync(LandingSafetyHarness h, Boolean build, String filter, String target) in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Application\LandNonCodeSkipTests.cs:line 2675
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at System.Runtime.CompilerServices.TaskAwaiter.ThrowForNonSuccess(Task task)
   at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
   at Antiphon.Tests.Application.LandNonCodeSkipTests.C1380_RegularDocumentationChangesSkip(String variant) in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Application\LandNonCodeSkipTests.cs:line 168
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
```


<a id="f102"></a>

#### F102 — `Antiphon.Tests.Application.LandNonCodeSkipTests.C1380_RegularDocumentationChangesSkip`

- Display: `C1380_RegularDocumentationChangesSkip(delete)`
- Suite/chunk: `antiphon/all-split-14`; duration `00:00:31.8672895`; manifest row 95.
- Evidence: `antiphon-all-split-14.trx:269`; test identity `5a1eacc6-bacf-2ada-3551-15d393e1919a`; execution ID `829f1bcc-4160-4de4-abc1-8a37b073e61b`.
- Disposition: **PARTIAL** — 7cdaefee SDK provenance/evaluation repair is relevant to audit fallback/digest errors. Existing git.exe fixture resolution also applies. Exact native cases remain unverified.

Error, first 12 lines (all lines if shorter):

```text
ShouldAssertException: h.Verifier.Calls
    should be
0
    but was
1

Additional Info:
    publication=Landed; reason=; command=dotnet build; dotnet run --project tests/Antiphon.Tests; policy=unrecognized-build-graph
```


Top six stored stack frames (all if fewer):

```text
   at Antiphon.Tests.Application.LandNonCodeSkipTests.LandAndAssertAsync(LandingSafetyHarness h, Boolean build, String filter, String target) in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Application\LandNonCodeSkipTests.cs:line 2675
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at System.Runtime.CompilerServices.TaskAwaiter.ThrowForNonSuccess(Task task)
   at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
   at Antiphon.Tests.Application.LandNonCodeSkipTests.C1380_RegularDocumentationChangesSkip(String variant) in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Application\LandNonCodeSkipTests.cs:line 168
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
```


<a id="f103"></a>

#### F103 — `Antiphon.Tests.Application.LandNonCodeSkipTests.C1380_DocsOnlyRebasedLandRecordsNonCodeSkip`

- Display: `C1380_DocsOnlyRebasedLandRecordsNonCodeSkip`
- Suite/chunk: `antiphon/all-split-15`; duration `00:00:36.9868086`; manifest row 96.
- Evidence: `antiphon-all-split-15.trx:55`; test identity `f3c7188e-d9dc-1f9f-ff14-e911afebdaa7`; execution ID `ca969cf9-7958-420f-bd30-42f3d5705571`.
- Disposition: **PARTIAL** — 7cdaefee SDK provenance/evaluation repair is relevant to audit fallback/digest errors. Existing git.exe fixture resolution also applies. Exact native cases remain unverified.

Error, first 12 lines (all lines if shorter):

```text
ShouldAssertException: h.Verifier.Calls
    should be
0
    but was
1

Additional Info:
    a docs-only rebased candidate must not invoke the verifier or its build slot; publication=Landed; reason=; command=dotnet build; dotnet run --project tests/Antiphon.Tests; policy=unrecognized-build-graph
```


Top six stored stack frames (all if fewer):

```text
   at Antiphon.Tests.Application.LandNonCodeSkipTests.C1380_DocsOnlyRebasedLandRecordsNonCodeSkip() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Application\LandNonCodeSkipTests.cs:line 69
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at Antiphon.Tests.Application.LandNonCodeSkipTests.C1380_DocsOnlyRebasedLandRecordsNonCodeSkip() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Application\LandNonCodeSkipTests.cs:line 80
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at System.Runtime.CompilerServices.TaskAwaiter.ThrowForNonSuccess(Task task)
   at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
```


<a id="f104"></a>

#### F104 — `Antiphon.Tests.Application.LandNonCodeSkipTests.C1385_RegistryMatchesNarrowGraph`

- Display: `C1385_RegistryMatchesNarrowGraph`
- Suite/chunk: `antiphon/all-split-15`; duration `00:00:00.1373602`; manifest row 96.
- Evidence: `antiphon-all-split-15.trx:116`; test identity `f07f5f55-524b-e608-a41c-5e3fcb889177`; execution ID `d63f535b-3dac-4a32-b9f6-becfcc97e297`.
- Disposition: **PARTIAL** — 7cdaefee SDK provenance/evaluation repair is relevant to audit fallback/digest errors. Existing git.exe fixture resolution also applies. Exact native cases remain unverified.

Error, first 12 lines (all lines if shorter):

```text
ShouldAssertException: digest
    should be
"8D4CCE85EBDE82978FD648F0F5DDCEA221A3F492C4A28D67DCD1A7A549FCF34B"
    but was
"124B52D47DD3232B60F4AEAF083B5E50ACC68DEDDE374AB2ECD6D43E543DC055"
    difference
Difference     |  |    |         |    |    |    |    |    |    |         |    |    |    |    |    |    |    |    |    |        
               | \|/  \|/       \|/  \|/  \|/  \|/  \|/  \|/  \|/       \|/  \|/  \|/  \|/  \|/  \|/  \|/  \|/  \|/  \|/       
Index          | 0    1    2    3    4    5    6    7    8    9    10   11   12   13   14   15   16   17   18   19   20   ...  
Expected Value | 8    D    4    C    C    E    8    5    E    B    D    E    8    2    9    7    8    F    D    6    4    ...  
Actual Value   | 1    2    4    B    5    2    D    4    7    D    D    3    2    3    2    B    6    0    F    4    A    ...  
Expected Code  | 56   68   52   67   67   69   56   53   69   66   68   69   56   50   57   55   56   70   68   54   52   ...
```


Top six stored stack frames (all if fewer):

```text
   at Antiphon.Tests.Application.LandNonCodeSkipTests.C1385_RegistryMatchesNarrowGraph() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Application\LandNonCodeSkipTests.cs:line 338
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at System.Runtime.CompilerServices.TaskAwaiter.ThrowForNonSuccess(Task task)
   at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at System.Runtime.CompilerServices.TaskAwaiter.ThrowForNonSuccess(Task task)
```


Supplemental actual digest from same message: `124B52D47DD3232B60F4AEAF083B5E50ACC68DEDDE374AB2ECD6D43E543DC055`.

<a id="f105"></a>

#### F105 — `Antiphon.Tests.Application.LandNonCodeSkipTests.C1385_UnrelatedScriptChangeStillSkips`

- Display: `C1385_UnrelatedScriptChangeStillSkips`
- Suite/chunk: `antiphon/all-split-15`; duration `00:00:32.1495648`; manifest row 96.
- Evidence: `antiphon-all-split-15.trx:175`; test identity `fc8aeb7f-9c9d-6673-8006-62a68f4441dd`; execution ID `53640589-5dd0-4575-a57b-173d3d0d8fc0`.
- Disposition: **PARTIAL** — 7cdaefee SDK provenance/evaluation repair is relevant to audit fallback/digest errors. Existing git.exe fixture resolution also applies. Exact native cases remain unverified.

Error, first 12 lines (all lines if shorter):

```text
ShouldAssertException: h.Verifier.Calls
    should be
0
    but was
1

Additional Info:
    publication=Landed; reason=; command=dotnet build; dotnet run --project tests/Antiphon.Tests; policy=unrecognized-build-graph
```


Top six stored stack frames (all if fewer):

```text
   at Antiphon.Tests.Application.LandNonCodeSkipTests.LandAndAssertAsync(LandingSafetyHarness h, Boolean build, String filter, String target) in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Application\LandNonCodeSkipTests.cs:line 2675
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at System.Runtime.CompilerServices.TaskAwaiter.ThrowForNonSuccess(Task task)
   at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
   at Antiphon.Tests.Application.LandNonCodeSkipTests.C1385_UnrelatedScriptChangeStillSkips() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Application\LandNonCodeSkipTests.cs:line 307
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
```


<a id="f106"></a>

#### F106 — `Antiphon.Tests.Application.LandNonCodeSkipTests.C1380_TargetContainedCodeIsExcludedFromTheActualRebasedDiff`

- Display: `C1380_TargetContainedCodeIsExcludedFromTheActualRebasedDiff`
- Suite/chunk: `antiphon/all-split-15`; duration `00:00:32.2511143`; manifest row 96.
- Evidence: `antiphon-all-split-15.trx:208`; test identity `049d440a-e751-8fea-ac6f-0caefaaa6920`; execution ID `7f3c8a0f-bd18-4ea0-a61b-2d9b78d1d464`.
- Disposition: **PARTIAL** — 7cdaefee SDK provenance/evaluation repair is relevant to audit fallback/digest errors. Existing git.exe fixture resolution also applies. Exact native cases remain unverified.

Error, first 12 lines (all lines if shorter):

```text
ShouldAssertException: h.Verifier.Calls
    should be
0
    but was
1

Additional Info:
    publication=Landed; reason=; command=dotnet build; dotnet run --project tests/Antiphon.Tests; policy=unrecognized-build-graph
```


Top six stored stack frames (all if fewer):

```text
   at Antiphon.Tests.Application.LandNonCodeSkipTests.LandAndAssertAsync(LandingSafetyHarness h, Boolean build, String filter, String target) in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Application\LandNonCodeSkipTests.cs:line 2675
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at System.Runtime.CompilerServices.TaskAwaiter.ThrowForNonSuccess(Task task)
   at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
   at Antiphon.Tests.Application.LandNonCodeSkipTests.C1380_TargetContainedCodeIsExcludedFromTheActualRebasedDiff() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Application\LandNonCodeSkipTests.cs:line 295
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
```


<a id="f107"></a>

#### F107 — `Antiphon.Tests.Application.LandNonCodeSkipTests.C1390_MissingNulFailsUnderNonInvariantCulture`

- Display: `C1390_MissingNulFailsUnderNonInvariantCulture`
- Suite/chunk: `antiphon/all-split-15`; duration `00:00:02.5877285`; manifest row 96.
- Evidence: `antiphon-all-split-15.trx:259`; test identity `905e8c2d-b28d-a7aa-14d3-c5845a935d88`; execution ID `3d54b8b1-3f85-4596-98e0-0126b80f827f`.
- Disposition: **UNRESOLVED** — No matching pushed repair established; native mechanism remains open.

Error, first 12 lines (all lines if shorter):

```text
ShouldAssertException: ran.Code
    should be
1
    but was
0

Additional Info:
    digest=F346980368EC03930A036CE4833CD73232897A7D14A9ED4716C5BF6A7AF9CB7E count=1
```


Top six stored stack frames (all if fewer):

```text
   at Antiphon.Tests.Application.LandNonCodeSkipTests.<>c__DisplayClass60_0.<<C1390_MissingNulFailsUnderNonInvariantCulture>b__0>d.MoveNext() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Application\LandNonCodeSkipTests.cs:line 2613
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at System.Runtime.CompilerServices.TaskAwaiter.ThrowForNonSuccess(Task task)
   at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
   at Antiphon.Tests.TestHelpers.AuditGitFixture.RunAsync(Func`1 body) in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\TestHelpers\AuditGitFixture.cs:line 13
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
```


<a id="f108"></a>

#### F108 — `Antiphon.Tests.Application.LandNonCodeSkipTests.C1409_NormaliserAndItemOperationTablesHaveWitnesses`

- Display: `C1409_NormaliserAndItemOperationTablesHaveWitnesses`
- Suite/chunk: `antiphon/all-split-16`; duration `00:00:09.6795443`; manifest row 97.
- Evidence: `antiphon-all-split-16.trx:43`; test identity `aed7db25-daa2-1f42-bc97-9e0ac42264e3`; execution ID `f2a8b58e-3a28-440f-8362-7b24bf5e40c5`.
- Disposition: **UNRESOLVED** — No matching pushed repair established; native mechanism remains open.

Error, first 12 lines (all lines if shorter):

```text
InvalidOperationException: table row N-12 failed: process.ExitCode
    should be
0
    but was
128

Additional Info:
    error: Invalid path 'server/fixtures/notes.md.'
fatal: git update-index: --cacheinfo cannot add server/fixtures/notes.md.
```


Top six stored stack frames (all if fewer):

```text
   at Antiphon.Tests.Application.LandNonCodeSkipTests.C1409_NormaliserAndItemOperationTablesHaveWitnesses() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Application\LandNonCodeSkipTests.cs:line 2147
```


### F. Shell transport, host probe paths and remote fixture controls (13 results)

The host-probe errors contain `/bin/bash: C:Users...probe.sh: No such file or directory` with exit 127: Windows backslashes have disappeared before the shell file lookup. Other members observe a missing Git historical file, zero wait lines, relationship-error formatting, or remote fixture fact mismatches. The 0d5de8f1 branch reproduces a read-only mount model that changes a peer writer’s permissions and repairs C1192 race behavior; it does not prove closure of the Windows path, lock-probe or npm diagnosis failures.

<a id="f075"></a>

#### F075 — `Antiphon.Tests.Infrastructure.AmServiceDeployReadinessTests.RetriesUntilHttpReadyAsync`

- Display: `RetriesUntilHttpReadyAsync`
- Suite/chunk: `antiphon/all-group-66`; duration `00:00:00.2046003`; manifest row 66.
- Evidence: `antiphon-all-group-66.trx:373`; test identity `e6f3c785-679a-5f9b-3353-749f0aa55489`; execution ID `951ad27a-24ed-4230-8f89-8571d1270d7e`.
- Disposition: **UNRESOLVED** — No matching pushed repair established; native mechanism remains open.

Error, first 12 lines (all lines if shorter):

```text
ShouldAssertException: process.ExitCode
    should be
0
    but was
128

Additional Info:
    fatal: path 'scripts/deploy-am-service.ps1' exists on disk, but not in '10fe51b0f668a272d621a935c27e53313b8de1c8'
```


Top six stored stack frames (all if fewer):

```text
   at Antiphon.Tests.Infrastructure.AmServiceDeployReadinessTests.LoadBaselineAsync() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Infrastructure\AmServiceDeployReadinessTests.cs:line 305
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at System.Runtime.CompilerServices.TaskAwaiter.ThrowForNonSuccess(Task task)
   at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
   at System.Runtime.CompilerServices.TaskAwaiter`1.GetResult()
   at Antiphon.Tests.Infrastructure.AmServiceDeployReadinessTests.RetriesUntilHttpReadyAsync() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Infrastructure\AmServiceDeployReadinessTests.cs:line 23
```


<a id="f078"></a>

#### F078 — `Antiphon.Tests.Scripts.RollingVolumeRecycleScriptTests.C1326_Census_refuses_a_land_timestamp_or_land_pending_appearance`

- Display: `C1326_Census_refuses_a_land_timestamp_or_land_pending_appearance`
- Suite/chunk: `antiphon/all-group-71`; duration `00:00:06.3501769`; manifest row 71.
- Evidence: `antiphon-all-group-71.trx:227`; test identity `e6486767-c72f-53e0-0b52-8a1ce7d7d706`; execution ID `5b54cd7e-b70a-48cd-a2fe-2d6ce94097aa`.
- Disposition: **UNRESOLVED** — No matching pushed repair established; native mechanism remains open.

Error, first 12 lines (all lines if shorter):

```text
ShouldAssertException: run.Exit
    should be
0
    but was
127

Additional Info:
    /bin/bash: C:UserslndcoAppDataLocalTempc1361-3kt1xehc.22bprobe.sh: No such file or directory
```


Top six stored stack frames (all if fewer):

```text
   at Antiphon.Tests.Scripts.C1361CensusProbes.AppearanceBoundary() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Scripts\C1361CensusProbes.cs:line 822
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at System.Runtime.CompilerServices.TaskAwaiter.ThrowForNonSuccess(Task task)
   at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
   at Antiphon.Tests.Scripts.RollingVolumeRecycleScriptTests.C1326_Census_refuses_a_land_timestamp_or_land_pending_appearance() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Scripts\RollingVolumeRecycleScriptTests.cs:line 1385
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
```


<a id="f079"></a>

#### F079 — `Antiphon.Tests.Scripts.RollingVolumeRecycleScriptTests.C1353_Wrapper_waits_for_a_bound_land_then_proceeds`

- Display: `C1353_Wrapper_waits_for_a_bound_land_then_proceeds`
- Suite/chunk: `antiphon/all-group-71`; duration `00:00:03.1418724`; manifest row 71.
- Evidence: `antiphon-all-group-71.trx:491`; test identity `8555d0f0-669e-ab13-5b10-9e163d32e490`; execution ID `9038a93b-e498-4f42-b350-f14a6dbbabb7`.
- Disposition: **UNRESOLVED** — No matching pushed repair established; native mechanism remains open.

Error, first 12 lines (all lines if shorter):

```text
ShouldAssertException: waitLines
    should be
1
    but was
0

Additional Info:
    clears: one wait line; RECYCLE_LAND_WAIT runner=server2 id=11111111-1111-1111-1111-111111111111 polls=0
\u001b[33;1mWARNING: Resulting JSON is truncated as serialization has exceeded the set depth of 2.\u001b[0m
Rolling deploy complete: C:\Antiphon\releases\checkout-997186e6\.antiphon\rolling-server2\c727ed0ce7203a6e
```


Top six stored stack frames (all if fewer):

```text
   at Antiphon.Tests.Scripts.RollingVolumeRecycleScriptTests.C1353_Wrapper_waits_for_a_bound_land_then_proceeds() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Scripts\RollingVolumeRecycleScriptTests.cs:line 533
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at System.Runtime.CompilerServices.TaskAwaiter.ThrowForNonSuccess(Task task)
   at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at System.Runtime.CompilerServices.TaskAwaiter.ThrowForNonSuccess(Task task)
```


<a id="f080"></a>

#### F080 — `Antiphon.Tests.Scripts.RollingVolumeRecycleScriptTests.C1371_Census_distinguishes_cross_read_count_drift_from_malformed_counts`

- Display: `C1371_Census_distinguishes_cross_read_count_drift_from_malformed_counts`
- Suite/chunk: `antiphon/all-group-71`; duration `00:00:41.3114434`; manifest row 71.
- Evidence: `antiphon-all-group-71.trx:560`; test identity `cae62199-a750-0903-b546-bc4191a4f324`; execution ID `d98e9998-1afa-42a9-9563-e3d36e22cb38`.
- Disposition: **UNRESOLVED** — No matching pushed repair established; native mechanism remains open.

Error, first 12 lines (all lines if shorter):

```text
ShouldAssertException: failures
    should be empty but had
1
    item and was
["host: run.Exit
    should be
0
    but was
127

Additional Info:
    collected/open/-1/True fixture: /bin/bash: C:UserslndcoAppDataLocalTempc1361-wzdxusfw.gznprobe.sh: No such file or directory
```


Top six stored stack frames (all if fewer):

```text
   at Antiphon.Tests.Scripts.C1361CensusProbes.Counts() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Scripts\C1361CensusProbes.cs:line 247
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at System.Runtime.CompilerServices.TaskAwaiter.ThrowForNonSuccess(Task task)
   at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
   at Antiphon.Tests.Scripts.RollingVolumeRecycleScriptTests.C1371_Census_distinguishes_cross_read_count_drift_from_malformed_counts() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Scripts\RollingVolumeRecycleScriptTests.cs:line 1723
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
```


<a id="f081"></a>

#### F081 — `Antiphon.Tests.Scripts.RollingVolumeRecycleScriptTests.C1371_Post_removal_count_drift_does_not_abort`

- Display: `C1371_Post_removal_count_drift_does_not_abort`
- Suite/chunk: `antiphon/all-group-71`; duration `00:00:04.0887978`; manifest row 71.
- Evidence: `antiphon-all-group-71.trx:655`; test identity `b9b8f8be-ec03-e2dd-9df2-de5d8d59891a`; execution ID `8a871b8c-bf77-4a00-9b99-9230d87df3d4`.
- Disposition: **UNRESOLVED** — No matching pushed repair established; native mechanism remains open.

Error, first 12 lines (all lines if shorter):

```text
ShouldAssertException: failures
    should be empty but had
1
    item and was
["host: run.Exit
    should be
0
    but was
127

Additional Info:
    fixture: /bin/bash: C:UserslndcoAppDataLocalTempc1361-1nhweisp.qabprobe.sh: No such file or directory
```


Top six stored stack frames (all if fewer):

```text
   at Antiphon.Tests.Scripts.C1361CensusProbes.PostRemoval() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Scripts\C1361CensusProbes.cs:line 314
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at System.Runtime.CompilerServices.TaskAwaiter.ThrowForNonSuccess(Task task)
   at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
   at Antiphon.Tests.Scripts.RollingVolumeRecycleScriptTests.C1371_Post_removal_count_drift_does_not_abort() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Scripts\RollingVolumeRecycleScriptTests.cs:line 1728
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
```


<a id="f086"></a>

#### F086 — `Antiphon.Tests.Scripts.CardRelationshipScriptTests.Block_fails_loudly_when_relationships_returns_404`

- Display: `Block_fails_loudly_when_relationships_returns_404`
- Suite/chunk: `antiphon/all-group-76`; duration `00:00:01.0109678`; manifest row 76.
- Evidence: `antiphon-all-group-76.trx:331`; test identity `321be160-203d-313b-aec2-bc2709f21fe4`; execution ID `6e7c884c-e29a-4981-acad-423fe4f733ba`.
- Disposition: **UNRESOLVED** — No matching pushed repair established; native mechanism remains open.

Error, first 12 lines (all lines if shorter):

```text
ShouldAssertException: run.Stderr
    should contain (case insensitive comparison)
"Relationship route unavailable"
    but was actually
"\u001b[31;1mInvoke-Antiphon: \u001b[0mC:\Antiphon\releases\checkout-997186e6\scripts\card.ps1:795\u001b[0m
\u001b[31;1m..."
```


Top six stored stack frames (all if fewer):

```text
   at Antiphon.Tests.Scripts.CardRelationshipScriptTests.Block_fails_loudly_when_relationships_returns_404() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Scripts\CardRelationshipScriptTests.cs:line 170
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
```


<a id="f109"></a>

#### F109 — `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1191_Lock_probe_is_created_before_uid_handoff`

- Display: `C1191_Lock_probe_is_created_before_uid_handoff`
- Suite/chunk: `antiphon/all-split-21`; duration `00:00:01.3847057`; manifest row 102.
- Evidence: `antiphon-all-split-21.trx:290`; test identity `e8294796-8fa3-8ea3-dda5-c6ee252df252`; execution ID `f2ebac11-51a4-4280-a181-ca972acba2ff`.
- Disposition: **NO-REPRO** — 0d5de8f1 report records Linux baseline/targeted passes; the permission-model repair is not proof of these distinct Windows signatures.

Error, first 12 lines (all lines if shorter):

```text
ShouldAssertException: Fact(output, "probe-before-handoff")
    should be
"1"
    but was
"0"
    difference
Difference     |  |   
               | \|/  
Index          | 0    
Expected Value | 1    
Actual Value   | 0    
Expected Code  | 49
```


Top six stored stack frames (all if fewer):

```text
   at Antiphon.Tests.Scripts.RemoteScriptContractTests.C1191_Lock_probe_is_created_before_uid_handoff() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Scripts\RemoteScriptContractTests.cs:line 256
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at System.Runtime.CompilerServices.TaskAwaiter.ThrowForNonSuccess(Task task)
   at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at System.Runtime.CompilerServices.TaskAwaiter.ThrowForNonSuccess(Task task)
```


<a id="f110"></a>

#### F110 — `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1192_Race_barriers_observe_volume_state`

- Display: `C1192_Race_barriers_observe_volume_state`
- Suite/chunk: `antiphon/all-split-22`; duration `00:00:17.3348575`; manifest row 103.
- Evidence: `antiphon-all-split-22.trx:29`; test identity `40786921-16c0-e590-fafd-142fdc4e4c8f`; execution ID `c53d38ab-445a-4109-a7d9-c3ace952b191`.
- Disposition: **NO-REPRO** — 0d5de8f1 report records Linux baseline/targeted passes; the permission-model repair is not proof of these distinct Windows signatures.

Error, first 12 lines (all lines if shorter):

```text
ShouldAssertException: Fact(output, label)
    should be
"1"
    but was
"0"
    difference
Difference     |  |   
               | \|/  
Index          | 0    
Expected Value | 1    
Actual Value   | 0    
Expected Code  | 49
```


Top six stored stack frames (all if fewer):

```text
   at Antiphon.Tests.Scripts.RemoteScriptContractTests.C1192_Race_barriers_observe_volume_state() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Scripts\RemoteScriptContractTests.cs:line 1076
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at System.Runtime.CompilerServices.TaskAwaiter.ThrowForNonSuccess(Task task)
   at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at System.Runtime.CompilerServices.TaskAwaiter.ThrowForNonSuccess(Task task)
```


<a id="f111"></a>

#### F111 — `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1192_Npm_seed_install_and_refill_use_work_volume`

- Display: `C1192_Npm_seed_install_and_refill_use_work_volume`
- Suite/chunk: `antiphon/all-split-22`; duration `00:00:01.8965656`; manifest row 103.
- Evidence: `antiphon-all-split-22.trx:243`; test identity `b31fb7da-d69f-6f4a-6607-55feea573152`; execution ID `91d48590-11c5-4632-b0f3-9b9e65372741`.
- Disposition: **NO-REPRO** — 0d5de8f1 report records Linux baseline/targeted passes; the permission-model repair is not proof of these distinct Windows signatures.

Error, first 12 lines (all lines if shorter):

```text
ShouldAssertException: Fact(output, "refill-volume-source")
    should be
"1"
    but was
"0"
    difference
Difference     |  |   
               | \|/  
Index          | 0    
Expected Value | 1    
Actual Value   | 0    
Expected Code  | 49
```


Top six stored stack frames (all if fewer):

```text
   at Antiphon.Tests.Scripts.RemoteScriptContractTests.C1192_Npm_seed_install_and_refill_use_work_volume() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Scripts\RemoteScriptContractTests.cs:line 1100
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at System.Runtime.CompilerServices.TaskAwaiter.ThrowForNonSuccess(Task task)
   at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at System.Runtime.CompilerServices.TaskAwaiter.ThrowForNonSuccess(Task task)
```


<a id="f112"></a>

#### F112 — `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1192_Race_programs_and_feed_share_the_work_volume`

- Display: `C1192_Race_programs_and_feed_share_the_work_volume`
- Suite/chunk: `antiphon/all-split-22`; duration `00:00:02.0775142`; manifest row 103.
- Evidence: `antiphon-all-split-22.trx:291`; test identity `b1f05921-c59b-cc8c-0383-aca828e45358`; execution ID `a88e2883-9928-4a5f-ac8b-24ceb54f242f`.
- Disposition: **ADDRESSED** — 0d5de8f1 / caaab44cfc4d900c3c391501200df4d26d46657e reproduces and repairs race reader permission mutation; Windows race closure pending.

Error, first 12 lines (all lines if shorter):

```text
ShouldAssertException: Fact(output, "race-boundary")
    should be
"1"
    but was
"0"
    difference
Difference     |  |   
               | \|/  
Index          | 0    
Expected Value | 1    
Actual Value   | 0    
Expected Code  | 49
```


Top six stored stack frames (all if fewer):

```text
   at Antiphon.Tests.Scripts.RemoteScriptContractTests.C1192_Race_programs_and_feed_share_the_work_volume() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Scripts\RemoteScriptContractTests.cs:line 1054
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at System.Runtime.CompilerServices.TaskAwaiter.ThrowForNonSuccess(Task task)
   at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at System.Runtime.CompilerServices.TaskAwaiter.ThrowForNonSuccess(Task task)
```


<a id="f113"></a>

#### F113 — `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1333_Npm_network_create_failure_reports_FixtureNpmNetworkFailed`

- Display: `C1333_Npm_network_create_failure_reports_FixtureNpmNetworkFailed`
- Suite/chunk: `antiphon/all-split-24`; duration `00:00:02.6705885`; manifest row 105.
- Evidence: `antiphon-all-split-24.trx:534`; test identity `cfa962d5-e0f8-1fd6-7005-f143df4f590a`; execution ID `623ce3a6-c5c0-42de-a045-2cbc69e4d3c0`.
- Disposition: **NO-REPRO** — 0d5de8f1 report records Linux baseline/targeted passes; the permission-model repair is not proof of these distinct Windows signatures.

Error, first 12 lines (all lines if shorter):

```text
ShouldAssertException: Fact(output, "npm-diagnosis")
    should be
"FixtureNpmNetworkFailed"
    but was
"FixtureNpmOfflineSetupFailed"
    difference
Difference     |                      |    |    |    |    |    |    |    |    |    |    |    |    |    |    |    |    |    |   
               |                     \|/  \|/  \|/  \|/  \|/  \|/  \|/  \|/  \|/  \|/  \|/  \|/  \|/  \|/  \|/  \|/  \|/  \|/  
Index          | ...  7    8    9    10   11   12   13   14   15   16   17   18   19   20   21   22   23   24   25   26   27   
Expected Value | ...  N    p    m    N    e    t    w    o    r    k    F    a    i    l    e    d                             
Actual Value   | ...  N    p    m    O    f    f    l    i    n    e    S    e    t    u    p    F    a    i    l    e    d    
Expected Code  | ...  78   112  109  78   101  116  119  111  114  107  70   97   105  108  101  100
```


Top six stored stack frames (all if fewer):

```text
   at Antiphon.Tests.Scripts.RemoteScriptContractTests.C1333_Npm_network_create_failure_reports_FixtureNpmNetworkFailed() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Scripts\RemoteScriptContractTests.cs:line 1126
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at System.Runtime.CompilerServices.TaskAwaiter.ThrowForNonSuccess(Task task)
   at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at System.Runtime.CompilerServices.TaskAwaiter.ThrowForNonSuccess(Task task)
```


<a id="f114"></a>

#### F114 — `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1361_Host_census_tolerates_open_status_changes_between_list_and_detail`

- Display: `C1361_Host_census_tolerates_open_status_changes_between_list_and_detail`
- Suite/chunk: `antiphon/all-split-24`; duration `00:00:00.3023446`; manifest row 105.
- Evidence: `antiphon-all-split-24.trx:591`; test identity `59747ef5-06b5-07ce-6024-6e4e08a8e5ba`; execution ID `febe768d-9e29-4f81-be78-d8a64cf78579`.
- Disposition: **UNRESOLVED** — No matching pushed repair established; native mechanism remains open.

Error, first 12 lines (all lines if shorter):

```text
ShouldAssertException: run.Exit
    should be
0
    but was
127

Additional Info:
    primary fixture: /bin/bash: C:UserslndcoAppDataLocalTempc1361-dle5lwht.g5gprobe.sh: No such file or directory
```


Top six stored stack frames (all if fewer):

```text
   at Antiphon.Tests.Scripts.C1361CensusProbes.Check(Boolean host, Vector vector) in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Scripts\C1361CensusProbes.cs:line 368
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at System.Runtime.CompilerServices.TaskAwaiter.ThrowForNonSuccess(Task task)
   at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
   at Antiphon.Tests.Scripts.C1361CensusProbes.Status(Boolean host, Boolean passes) in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Scripts\C1361CensusProbes.cs:line 70
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
```


<a id="f115"></a>

#### F115 — `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1361_Host_census_tolerates_open_status_changes_between_passes`

- Display: `C1361_Host_census_tolerates_open_status_changes_between_passes`
- Suite/chunk: `antiphon/all-split-24`; duration `00:00:00.2477252`; manifest row 105.
- Evidence: `antiphon-all-split-24.trx:630`; test identity `fb30b17e-f20e-5217-3a41-7e960ce164ee`; execution ID `017307c5-922d-473b-a1d7-c925da3ef46b`.
- Disposition: **UNRESOLVED** — No matching pushed repair established; native mechanism remains open.

Error, first 12 lines (all lines if shorter):

```text
ShouldAssertException: run.Exit
    should be
0
    but was
127

Additional Info:
    primary fixture: /bin/bash: C:UserslndcoAppDataLocalTempc1361-gxcid1rz.ro1probe.sh: No such file or directory
```


Top six stored stack frames (all if fewer):

```text
   at Antiphon.Tests.Scripts.C1361CensusProbes.Check(Boolean host, Vector vector) in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Scripts\C1361CensusProbes.cs:line 368
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at System.Runtime.CompilerServices.TaskAwaiter.ThrowForNonSuccess(Task task)
   at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
   at Antiphon.Tests.Scripts.C1361CensusProbes.Status(Boolean host, Boolean passes) in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Scripts\C1361CensusProbes.cs:line 70
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
```


### G. Release/checkpoint admission, source stamps and clean-source contracts (12 results)

Stored messages show missing outer build stamps, wrong-origin admission refusals, dirty=2/source_dirty or source_ineligible, preserved-byte/time mismatch, and a 31,200-second synthetic launch exceeding the 1,800-second estimate ceiling. F074 builds successfully but refuses output inventory. These are distinct admission boundaries; neither the two dirty paths nor a common underlying cause is established from the allowed artifacts. No release/checkpoint admission was bypassed or changed.

<a id="f074"></a>

#### F074 — `Antiphon.Tests.Checkpoints.CheckpointRepeatBuildIsolationTests.repeat_two_then_default_then_three_rebuilds_the_same_project`

- Display: `repeat_two_then_default_then_three_rebuilds_the_same_project`
- Suite/chunk: `antiphon/all-group-63`; duration `00:03:31.3858939`; manifest row 63.
- Evidence: `antiphon-all-group-63.trx:869`; test identity `cd62c2ac-4e2d-b550-2b34-9b4bccdcd221`; execution ID `57570e4a-b0a0-4dda-a62b-4f3ca4460696`.
- Disposition: **PARTIAL** — a043b0a4 corrects the standalone reader used by inventory, but output-inventory-failed masks the underlying error. Existing 3edae481d isolates repeat outputs; native closure unproven.

Error, first 12 lines (all lines if shorter):

```text
ShouldAssertException: process.ExitCode
    should be
0
    but was
2

Additional Info:
    sequence-repeat-2: BUILD SLOT skipped by -NoSlot

  Determining projects to restore...
  Restored C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Checkpoints.FilterHost\Antiphon.Checkpoints.FilterHost.csproj (in 777 ms).
  Antiphon.Checkpoints.FilterHost -> C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Checkpoints.FilterHost\bin-c885-script-c723-01a17998026275038318ceebd208253d-sequence-2\Antiphon.Checkpoints.FilterHost.dll
```


Top six stored stack frames (all if fewer):

```text
   at Antiphon.Tests.Checkpoints.CheckpointRepeatBuildIsolationTests.AssertRunAsync(String root, Int32 repeat, Int32 expectedExecuted, CancellationToken cancellationToken) in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Checkpoints\CheckpointRepeatBuildIsolationTests.cs:line 55
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at Antiphon.Tests.Checkpoints.CheckpointRepeatBuildIsolationTests.AssertRunAsync(String root, Int32 repeat, Int32 expectedExecuted, CancellationToken cancellationToken) in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Checkpoints\CheckpointRepeatBuildIsolationTests.cs:line 84
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at System.Runtime.CompilerServices.TaskAwaiter.ThrowForNonSuccess(Task task)
   at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
```


Supplemental masked inventory refusal: build EXIT CODE 0; `refused: output-inventory-failed`; CHECKPOINT executed=0, exit=2, dirty=2. Stored error text gives no underlying inventory exception.

<a id="f084"></a>

#### F084 — `Antiphon.Tests.Scripts.C1008PlatformContractTests.C1050_Windows_rolling_outcomes_are_exact`

- Display: `C1050_Windows_rolling_outcomes_are_exact`
- Suite/chunk: `antiphon/all-group-76`; duration `00:00:00.0676715`; manifest row 76.
- Evidence: `antiphon-all-group-76.trx:17`; test identity `8fe91a2e-ab30-21a6-de2a-457abb771c13`; execution ID `f4ff526a-9c84-4db6-b9a6-e48fd5338dba`.
- Disposition: **UNRESOLVED** — No matching pushed repair established; native mechanism remains open.

Error, first 12 lines (all lines if shorter):

```text
ShouldAssertException: File.Exists(stampPath)
    should be
True
    but was
False

Additional Info:
    c1050-child-source: outer row build stamp required
```


Top six stored stack frames (all if fewer):

```text
   at Antiphon.Tests.Scripts.C1008PlatformContractTests.RunWindowsAuditAsync(String filter, String[] hosts, String[] portable, CancellationToken cancellationToken) in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Scripts\C1008PlatformContractTests.cs:line 133
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at System.Runtime.CompilerServices.TaskAwaiter.ThrowForNonSuccess(Task task)
   at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
   at Antiphon.Tests.Scripts.C1008PlatformContractTests.C1050_Windows_rolling_outcomes_are_exact(CancellationToken cancellationToken) in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Scripts\C1008PlatformContractTests.cs:line 115
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
```


<a id="f085"></a>

#### F085 — `Antiphon.Tests.Scripts.C1008PlatformContractTests.C1050_Windows_remote_outcomes_are_exact`

- Display: `C1050_Windows_remote_outcomes_are_exact`
- Suite/chunk: `antiphon/all-group-76`; duration `00:00:00.0042433`; manifest row 76.
- Evidence: `antiphon-all-group-76.trx:126`; test identity `63c43b05-ddd0-9f7c-4832-9f8e6a50b51e`; execution ID `50e95d5a-c35e-4ef2-8755-a0359d2da457`.
- Disposition: **UNRESOLVED** — No matching pushed repair established; native mechanism remains open.

Error, first 12 lines (all lines if shorter):

```text
ShouldAssertException: File.Exists(stampPath)
    should be
True
    but was
False

Additional Info:
    c1050-child-source: outer row build stamp required
```


Top six stored stack frames (all if fewer):

```text
   at Antiphon.Tests.Scripts.C1008PlatformContractTests.RunWindowsAuditAsync(String filter, String[] hosts, String[] portable, CancellationToken cancellationToken) in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Scripts\C1008PlatformContractTests.cs:line 133
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at System.Runtime.CompilerServices.TaskAwaiter.ThrowForNonSuccess(Task task)
   at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
   at Antiphon.Tests.Scripts.C1008PlatformContractTests.C1050_Windows_remote_outcomes_are_exact(CancellationToken cancellationToken) in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Scripts\C1008PlatformContractTests.cs:line 106
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
```


<a id="f087"></a>

#### F087 — `Antiphon.Tests.Scripts.ReleaseGateRunTests.C599_ParameterHops`

- Display: `C599_ParameterHops`
- Suite/chunk: `antiphon/all-group-76`; duration `00:00:08.4352208`; manifest row 76.
- Evidence: `antiphon-all-group-76.trx:1247`; test identity `a737a0d4-7819-2a51-cca1-cd8bae4207e6`; execution ID `5858572d-d327-4147-bf20-19b2bc88b63f`.
- Disposition: **UNRESOLVED** — No matching pushed repair established; native mechanism remains open.

Error, first 12 lines (all lines if shorter):

```text
ShouldAssertException: result.ExitCode
    should be
0
    but was
1

Additional Info:
    REFUSED: unknown profile turbo.
{"nativeRunId":"","sha":"","ref":"","trigger":"","profile":"","candidateId":"","creditKind":"","localDueDate":"","policyHash":"","coverageComplete":false,"testsPassed":false,"reportDelivered":false,"exitCode":3,"summaryPath":""}
PASS C599 ParameterHops unknown profile refuses at the core
REFUSED: rc profile needs an rc/YYYY-MM-DD ref or a legacy release/rc-<stamp> (ref-shape).
{"nativeRunId":"","sha":"","ref":"","trigger":"","profile":"","candidateId":"","creditKind":"","localDueDate":"","policyHash":"","coverageComplete":false,"testsPassed":false,"reportDelivered":false,"exitCode":3,"summaryPath":""}
```


Top six stored stack frames (all if fewer):

```text
   at Antiphon.Tests.Scripts.ScriptHarness.Validate(ScriptHarnessResult result, String prefix, String caseName, Int32 expectedRows, IReadOnlyList`1 requiredRows) in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Scripts\ScriptHarness.cs:line 25
   at Antiphon.Tests.Scripts.ScriptHarness.<>c__DisplayClass1_0.<RunHarnessCaseAsync>b__0(ScriptHarnessResult result) in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Scripts\ScriptHarness.cs:line 18
   at Antiphon.Tests.Scripts.ScriptHarnessProcess.RunAsync(String harness, String prefix, String caseName, String script, ScriptHarnessOptions options, CancellationToken callerToken, Action`1 validate) in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Scripts\ScriptHarnessProcess.cs:line 84
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at Antiphon.Tests.Scripts.ScriptHarnessProcess.RunAsync(String harness, String prefix, String caseName, String script, ScriptHarnessOptions options, CancellationToken callerToken, Action`1 validate) in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Scripts\ScriptHarnessProcess.cs:line 145
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
```


Supplemental ownership refusal from the same stored message:

```text
REFUSED: run-context ownership (origin/wrong-origin).
FAIL C599 ParameterHops outer run reaches self-reexec without refusal - phase=ownership refusal=wrong-origin exit=3
  C599 ParameterHops outer run reaches self-reexec without refusal : phase=ownership refusal=wrong-origin exit=3
```


<a id="f088"></a>

#### F088 — `Antiphon.Tests.Scripts.ReleaseGateRunTests.C599_MasterStateIsolation`

- Display: `C599_MasterStateIsolation`
- Suite/chunk: `antiphon/all-group-76`; duration `00:00:07.4974138`; manifest row 76.
- Evidence: `antiphon-all-group-76.trx:1324`; test identity `b7d46da2-5a8a-dcf3-e618-7587711cd0d6`; execution ID `43596517-f5c3-4d2f-b700-4011bebc78c9`.
- Disposition: **UNRESOLVED** — No matching pushed repair established; native mechanism remains open.

Error, first 12 lines (all lines if shorter):

```text
ShouldAssertException: result.ExitCode
    should be
0
    but was
1

Additional Info:
    PASS C599 MasterStateIsolation run starts without child summaries
REFUSED: run-context ownership (origin/wrong-origin).
{"nativeRunId":"","sha":"","ref":"","trigger":"","profile":"","candidateId":"","creditKind":"","localDueDate":"","policyHash":"","coverageComplete":false,"testsPassed":false,"reportDelivered":false,"exitCode":3,"summaryPath":""}
FAIL C599 MasterStateIsolation outer run reaches self-reexec without refusal - phase=ownership refusal=wrong-origin exit=3
FAIL C599 MasterStateIsolation exactly one fresh child summary names this run - summaries=0
```


Top six stored stack frames (all if fewer):

```text
   at Antiphon.Tests.Scripts.ScriptHarness.Validate(ScriptHarnessResult result, String prefix, String caseName, Int32 expectedRows, IReadOnlyList`1 requiredRows) in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Scripts\ScriptHarness.cs:line 25
   at Antiphon.Tests.Scripts.ScriptHarness.<>c__DisplayClass1_0.<RunHarnessCaseAsync>b__0(ScriptHarnessResult result) in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Scripts\ScriptHarness.cs:line 18
   at Antiphon.Tests.Scripts.ScriptHarnessProcess.RunAsync(String harness, String prefix, String caseName, String script, ScriptHarnessOptions options, CancellationToken callerToken, Action`1 validate) in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Scripts\ScriptHarnessProcess.cs:line 84
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at Antiphon.Tests.Scripts.ScriptHarnessProcess.RunAsync(String harness, String prefix, String caseName, String script, ScriptHarnessOptions options, CancellationToken callerToken, Action`1 validate) in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Scripts\ScriptHarnessProcess.cs:line 145
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
```


Supplemental ownership refusal from the same stored message:

```text
REFUSED: run-context ownership (origin/wrong-origin).
FAIL C599 MasterStateIsolation outer run reaches self-reexec without refusal - phase=ownership refusal=wrong-origin exit=3
  C599 MasterStateIsolation outer run reaches self-reexec without refusal : phase=ownership refusal=wrong-origin exit=3
```


<a id="f089"></a>

#### F089 — `Antiphon.Tests.Scripts.RunCheckpointRepeatScriptTests.C885_RepeatNoBuildAndLeaseCustody`

- Display: `C885_RepeatNoBuildAndLeaseCustody`
- Suite/chunk: `antiphon/all-group-76`; duration `00:00:23.8492704`; manifest row 76.
- Evidence: `antiphon-all-group-76.trx:1519`; test identity `cea4341b-eea0-e199-514b-7b89c41f7d3d`; execution ID `d90180aa-2027-4f57-b02c-c65fd2b3e4cd`.
- Disposition: **UNRESOLVED** — No matching pushed repair established; native mechanism remains open.

Error, first 12 lines (all lines if shorter):

```text
ShouldAssertException: reused.ExitCode
    should be
0
    but was
2

Additional Info:
    CHECKPOINT CP-1 commit=105792f7d22f297746cdb338976f52e424b76fa7 build=n/a filter=/*/*/SampleTests*/* executed=0 passed=0 failed=0 skipped=0 trx=n/a slot=skipped waited=0s dirty=2 source=105792f7d22f297746cdb338976f52e424b76fa7+dirty:c21323dc003df40740ec666ec6bb69b92d164aa4812095dfa8dd14e17e3a53dc sourceState=unknown buildSource=unknown repeat=5 repetitions=0/5 hostInvocations=1 reason=source_dirty
CHECKPOINT CP-1 EXIT CODE: 2
```


Top six stored stack frames (all if fewer):

```text
   at Antiphon.Tests.Scripts.RunCheckpointRepeatScriptTests.C885_RepeatNoBuildAndLeaseCustody(CancellationToken cancellationToken) in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Scripts\RunCheckpointRepeatScriptTests.cs:line 104
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
```


<a id="f090"></a>

#### F090 — `Antiphon.Tests.Scripts.RunCheckpointRepeatScriptTests.C885_RepeatReceiptAndTimings`

- Display: `C885_RepeatReceiptAndTimings`
- Suite/chunk: `antiphon/all-group-76`; duration `00:00:20.6802282`; manifest row 76.
- Evidence: `antiphon-all-group-76.trx:1555`; test identity `a8bef143-26e6-8ca1-33d6-46a26789fd15`; execution ID `d7eaebe0-cbaf-4483-b0d0-54927dd756fd`.
- Disposition: **UNRESOLVED** — No matching pushed repair established; native mechanism remains open.

Error, first 12 lines (all lines if shorter):

```text
ShouldAssertException: valid.ExitCode
    should be
0
    but was
2

Additional Info:
    CHECKPOINT SOURCE INVALID reason=source_ineligible
```


Top six stored stack frames (all if fewer):

```text
   at Antiphon.Tests.Scripts.RunCheckpointRepeatScriptTests.C885_RepeatReceiptAndTimings(CancellationToken cancellationToken) in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Scripts\RunCheckpointRepeatScriptTests.cs:line 67
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
```


<a id="f091"></a>

#### F091 — `Antiphon.Tests.Scripts.ChangeAwareReleaseTests.C1313_ParameterHops`

- Display: `C1313_ParameterHops`
- Suite/chunk: `antiphon/all-group-77`; duration `00:01:14.8218880`; manifest row 77.
- Evidence: `antiphon-all-group-77.trx:137`; test identity `87148df9-46c3-f4d0-b6cf-64d04e9ba7fe`; execution ID `9c44cefb-7ed8-4612-82c2-a79385ee458f`.
- Disposition: **UNRESOLVED** — No matching pushed repair established; native mechanism remains open.

Error, first 12 lines (all lines if shorter):

```text
ShouldAssertException: result.ExitCode
    should be
0
    but was
1

Additional Info:
    [release-candidate] journalled rc/2026-10-19 at db4b4dfb87125f41055e6c2b2dcc7086ae50b50c; not pushed
REFUSED: run-context ownership (origin/wrong-origin).
{"nativeRunId":"","sha":"","ref":"","trigger":"","profile":"","candidateId":"","creditKind":"","localDueDate":"","policyHash":"","coverageComplete":false,"testsPassed":false,"reportDelivered":false,"exitCode":3,"summaryPath":""}
[release-cut] rc run did not complete green; publishing nothing (exit 3).
C1313 hop-summary-count=0
```


Top six stored stack frames (all if fewer):

```text
   at Antiphon.Tests.Scripts.ScriptHarness.Validate(ScriptHarnessResult result, String prefix, String caseName, Int32 expectedRows, IReadOnlyList`1 requiredRows) in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Scripts\ScriptHarness.cs:line 25
   at Antiphon.Tests.Scripts.ScriptHarness.<>c__DisplayClass1_0.<RunHarnessCaseAsync>b__0(ScriptHarnessResult result) in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Scripts\ScriptHarness.cs:line 18
   at Antiphon.Tests.Scripts.ScriptHarnessProcess.RunAsync(String harness, String prefix, String caseName, String script, ScriptHarnessOptions options, CancellationToken callerToken, Action`1 validate) in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Scripts\ScriptHarnessProcess.cs:line 84
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at Antiphon.Tests.Scripts.ScriptHarnessProcess.RunAsync(String harness, String prefix, String caseName, String script, ScriptHarnessOptions options, CancellationToken callerToken, Action`1 validate) in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Scripts\ScriptHarnessProcess.cs:line 145
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
```


Supplemental ownership refusal from the same stored message:

```text
REFUSED: run-context ownership (origin/wrong-origin).
FAIL C1313 hop-baseline-bound - {"candidateId":"rc-2026-10-19-db4b4dfb8712","ref":"rc/2026-10-19","sha":"db4b4dfb87125f41055e6c2b2dcc7086ae50b50c","scheduleSlot":"","windmillJobId":"","tested":false,"published":false,"tag":"","refusal":"wrong-origin","exitCode":3,"selection":{"format":"antiphon-release-selection-attempt-v1","repository":"fixture/repository","candidateId":"rc-2026-10-19-db4b4dfb8712","candidateRef":"rc/2026-10-19","candidateSha":"db4b4dfb87125f41055e6c2b2dcc7086ae50b50c","baselineSha":"8bda9193e529222432577f9e6ebdc091bcd6cc47","requestedMode":"Shadow","effectiveMode":"Full","coverageScope":"Full","profile":"rc","runId":"hop-run","attempt":1,"selectionDigest":"618aef61036d6e412e993743c70c4c6abb3e318d8c9e0db6f28bb74b1034b659","contract":{"policyHash":"67fb49932cf43763c6dd0bafd61ccac4d2945e72f8ceeed97c9d1910de35f38d","catalogHash":"622274b4d95a49bb26d277fcee23085ab61339cc1372ab83799f79835a100c24","selectorHash":"1ab8ca65412b3bed68220996d759043d6e9ccf4f5e16f9351095516488ec54b7","runtimeHash":"38a06f72ec3f87feda16951f5f1afc81a87beac399e71ba48484d26ee60d7d63","profile":"rc","platform":"windows"},"baselineDigest":"","qualificationDigest":"b1faf2dcdba419d4c8ff23874e290e9f732b443099dd66add13eaf27e2d102b1","trainDigest":"cc5f2e249ef546fb608d454805d97793268a873776694204008c31862a44277c","fallbackReasons":["baseline-absent"],"baselineReceiptPath":"C:\\Users\\lndco\\AppData\\Local\\Temp\\c1313-nightly-7fdbe56b23444ac2ba1a6b9832cf5cce\\space 6887e8ce061e49e193d4727f55380ede\\releases ?\\absent receipt.json","qualificationPath":"C:\\Users\\lndco\\AppData\\Local\\Temp\\c1313-nightly-7fdbe56b23444ac2ba1a6b9832cf5cce\\space 6887e8ce061e49e193d4727f55380ede\\releases ?\\qualification\\claim.json","timingRoot":"C:\\Users\\lndco\\AppData\\Local\\Temp\\c1313-nightly-7fdbe56b23444ac2ba1a6b9832cf5cce\\space 6887e8ce061e49e193d4727f55380ede\\releases ?\\timing root","releaseIntentPath":"C:\\Users\\lndco\\AppData\\Local\\Temp\\c1313-nightly-7fdbe56b23444ac2ba1a6b9832cf5cce\\space 6887e8ce061e49e193d4727f55380ede\\releases ?\\future intent.json","trainManifestPath":"C:\\Users\\lndco\\AppData\\Local\\Temp\\c1313-nightly-7fdbe56b23444ac2ba1a6b9832cf5cce\\space 6887e8ce061e49e193d4727f55380ede\\releases ?\\future train.json"}}
  C1313 hop-baseline-bound : {"candidateId":"rc-2026-10-19-db4b4dfb8712","ref":"rc/2026-10-19","sha":"db4b4dfb87125f41055e6c2b2dcc7086ae50b50c","scheduleSlot":"","windmillJobId":"","tested":false,"published":false,"tag":"","refusal":"wrong-origin","exitCode":3,"selection":{"format":"antiphon-release-selection-attempt-v1","repository":"fixture/repository","candidateId":"rc-2026-10-19-db4b4dfb8712","candidateRef":"rc/2026-10-19","candidateSha":"db4b4dfb87125f41055e6c2b2dcc7086ae50b50c","baselineSha":"8bda9193e529222432577f9e6ebdc091bcd6cc47","requestedMode":"Shadow","effectiveMode":"Full","coverageScope":"Full","profile":"rc","runId":"hop-run","attempt":1,"selectionDigest":"618aef61036d6e412e993743c70c4c6abb3e318d8c9e0db6f28bb74b1034b659","contract":{"policyHash":"67fb49932cf43763c6dd0bafd61ccac4d2945e72f8ceeed97c9d1910de35f38d","catalogHash":"622274b4d95a49bb26d277fcee23085ab61339cc1372ab83799f79835a100c24","selectorHash":"1ab8ca65412b3bed68220996d759043d6e9ccf4f5e16f9351095516488ec54b7","runtimeHash":"38a06f72ec3f87feda16951f5f1afc81a87beac399e71ba48484d26ee60d7d63","profile":"rc","platform":"windows"},"baselineDigest":"","qualificationDigest":"b1faf2dcdba419d4c8ff23874e290e9f732b443099dd66add13eaf27e2d102b1","trainDigest":"cc5f2e249ef546fb608d454805d97793268a873776694204008c31862a44277c","fallbackReasons":["baseline-absent"],"baselineReceiptPath":"C:\\Users\\lndco\\AppData\\Local\\Temp\\c1313-nightly-7fdbe56b23444ac2ba1a6b9832cf5cce\\space 6887e8ce061e49e193d4727f55380ede\\releases ?\\absent receipt.json","qualificationPath":"C:\\Users\\lndco\\AppData\\Local\\Temp\\c1313-nightly-7fdbe56b23444ac2ba1a6b9832cf5cce\\space 6887e8ce061e49e193d4727f55380ede\\releases ?\\qualification\\claim.json","timingRoot":"C:\\Users\\lndco\\AppData\\Local\\Temp\\c1313-nightly-7fdbe56b23444ac2ba1a6b9832cf5cce\\space 6887e8ce061e49e193d4727f55380ede\\releases ?\\timing root","releaseIntentPath":"C:\\Users\\lndco\\AppData\\Local\\Temp\\c1313-nightly-7fdbe56b23444ac2ba1a6b9832cf5cce\\space 6887e8ce061e49e193d4727f55380ede\\releases ?\\future intent.json","trainManifestPath":"C:\\Users\\lndco\\AppData\\Local\\Temp\\c1313-nightly-7fdbe56b23444ac2ba1a6b9832cf5cce\\space 6887e8ce061e49e193d4727f55380ede\\releases ?\\future train.json"}}
```


<a id="f092"></a>

#### F092 — `Antiphon.Tests.Scripts.RcQualificationStatusTests.C1500_StatusDoesNotMutate`

- Display: `C1500_StatusDoesNotMutate`
- Suite/chunk: `antiphon/all-group-77`; duration `00:00:01.9589382`; manifest row 77.
- Evidence: `antiphon-all-group-77.trx:322`; test identity `6c895cff-e3a7-bcb7-c577-5df8465719bf`; execution ID `30913d92-32e6-4865-b2ac-81c8da97b04a`.
- Disposition: **UNRESOLVED** — No matching pushed repair established; native mechanism remains open.

Error, first 12 lines (all lines if shorter):

```text
ShouldAssertException: result.ExitCode
    should be
0
    but was
1

Additional Info:
    PASS C1500 real-entrypoint
FAIL C1500 input-bytes-and-times-preserved - 
PASS C1500 no-action-commands

C487: 2 passed, 1 failed, 3 rows
```


Top six stored stack frames (all if fewer):

```text
   at Antiphon.Tests.Scripts.ScriptHarness.Validate(ScriptHarnessResult result, String prefix, String caseName, Int32 expectedRows, IReadOnlyList`1 requiredRows) in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Scripts\ScriptHarness.cs:line 25
   at Antiphon.Tests.Scripts.ScriptHarness.<>c__DisplayClass1_0.<RunHarnessCaseAsync>b__0(ScriptHarnessResult result) in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Scripts\ScriptHarness.cs:line 18
   at Antiphon.Tests.Scripts.ScriptHarnessProcess.RunAsync(String harness, String prefix, String caseName, String script, ScriptHarnessOptions options, CancellationToken callerToken, Action`1 validate) in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Scripts\ScriptHarnessProcess.cs:line 84
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at Antiphon.Tests.Scripts.ScriptHarnessProcess.RunAsync(String harness, String prefix, String caseName, String script, ScriptHarnessOptions options, CancellationToken callerToken, Action`1 validate) in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Scripts\ScriptHarnessProcess.cs:line 145
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
```


<a id="f093"></a>

#### F093 — `Antiphon.Tests.Scripts.ReleaseGatePlatformTests.C1323_PlatformExecution`

- Display: `C1323_PlatformExecution`
- Suite/chunk: `antiphon/all-group-77`; duration `00:00:07.8795412`; manifest row 77.
- Evidence: `antiphon-all-group-77.trx:489`; test identity `d7fe97b1-dec0-18ef-e35b-81bef1ffa7a2`; execution ID `8f345b67-b560-4086-a72b-20a3605bca0e`.
- Disposition: **UNRESOLVED** — No matching pushed repair established; native mechanism remains open.

Error, first 12 lines (all lines if shorter):

```text
ShouldAssertException: result.ExitCode
    should be
0
    but was
1

Additional Info:
    [2026-10-26T22:00:09.2474096Z] wrote C:\Users\lndco\AppData\Local\Temp\c1323-nightly-b28b5092fa4d406e9b8bfcf20c271120\execution\linux-direct\summary.json
PASS C1323 linux-launches-only-portable-cases
PASS C1323 linux-summary-is-diagnostic
PASS C1323 linux-census-retains-exclusion-attribution
PASS C1323 optin-and-legacy-method-semantics
```


Top six stored stack frames (all if fewer):

```text
   at Antiphon.Tests.Scripts.ScriptHarness.Validate(ScriptHarnessResult result, String prefix, String caseName, Int32 expectedRows, IReadOnlyList`1 requiredRows) in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Scripts\ScriptHarness.cs:line 25
   at Antiphon.Tests.Scripts.ScriptHarness.<>c__DisplayClass1_0.<RunHarnessCaseAsync>b__0(ScriptHarnessResult result) in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Scripts\ScriptHarness.cs:line 18
   at Antiphon.Tests.Scripts.ScriptHarnessProcess.RunAsync(String harness, String prefix, String caseName, String script, ScriptHarnessOptions options, CancellationToken callerToken, Action`1 validate) in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Scripts\ScriptHarnessProcess.cs:line 84
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at Antiphon.Tests.Scripts.ScriptHarnessProcess.RunAsync(String harness, String prefix, String caseName, String script, ScriptHarnessOptions options, CancellationToken callerToken, Action`1 validate) in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Scripts\ScriptHarnessProcess.cs:line 145
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
```


<a id="f095"></a>

#### F095 — `Antiphon.Tests.Scripts.WindowsUnitPlatformAdmissionTests.C1210_Windows_linux_only_outcomes_are_exact`

- Display: `C1210_Windows_linux_only_outcomes_are_exact`
- Suite/chunk: `antiphon/all-group-77`; duration `00:00:02.7343840`; manifest row 77.
- Evidence: `antiphon-all-group-77.trx:808`; test identity `3a78c20a-983a-8639-1f12-84ee7be0b7af`; execution ID `b1d8f180-62c2-4d21-9926-5c7d2bf133aa`.
- Disposition: **UNRESOLVED** — No matching pushed repair established; native mechanism remains open.

Error, first 12 lines (all lines if shorter):

```text
ShouldAssertException: source.DirtyFiles
    should be
0
    but was
2

Additional Info:
    c1210-child-source-stamp: clean source
```


Top six stored stack frames (all if fewer):

```text
   at Antiphon.Tests.Scripts.WindowsUnitPlatformAdmissionTests.C1210_Windows_linux_only_outcomes_are_exact(CancellationToken cancellationToken) in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Scripts\WindowsUnitPlatformAdmissionTests.cs:line 437
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at System.Runtime.CompilerServices.TaskAwaiter.ThrowForNonSuccess(Task task)
   at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at System.Runtime.CompilerServices.TaskAwaiter.ThrowForNonSuccess(Task task)
```


<a id="f096"></a>

#### F096 — `Antiphon.Tests.Scripts.ReleaseGateFollowupTests.C1336_NightlyCandidateIdentity`

- Display: `C1336_NightlyCandidateIdentity`
- Suite/chunk: `antiphon/all-group-78`; duration `00:00:11.7991478`; manifest row 78.
- Evidence: `antiphon-all-group-78.trx:59`; test identity `c0922c0f-ab08-4f32-80a3-78a1c9d87f4c`; execution ID `4e8c78d1-f385-4017-ac21-c78f708ee26d`.
- Disposition: **UNRESOLVED** — No matching pushed repair established; native mechanism remains open.

Error, first 12 lines (all lines if shorter):

```text
ShouldAssertException: result.ExitCode
    should be
0
    but was
1

Additional Info:
    C1336 fixture runtime=7.6.9; edition=Core; OS=Windows_NT; separator=\
PASS C1336 N omitted
\u001b[31;1mException: \u001b[0mC:\Antiphon\releases\checkout-997186e6\tests\fixtures\card1336-release-gate-followups.ps1:214\u001b[0m
\u001b[31;1m\u001b[0m\u001b[36;1mLine |\u001b[0m
\u001b[31;1m\u001b[0m\u001b[36;1m\u001b[36;1m 214 | \u001b[0m . de -ne 0) { \u001b[36;1mthrow ('Omitted downstream admission failed: ' + ($run.Re\u001b[0m .\u001b[0m
```


Top six stored stack frames (all if fewer):

```text
   at Antiphon.Tests.Scripts.ScriptHarness.Validate(ScriptHarnessResult result, String prefix, String caseName, Int32 expectedRows, IReadOnlyList`1 requiredRows) in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Scripts\ScriptHarness.cs:line 25
   at Antiphon.Tests.Scripts.ScriptHarness.<>c__DisplayClass1_0.<RunHarnessCaseAsync>b__0(ScriptHarnessResult result) in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Scripts\ScriptHarness.cs:line 18
   at Antiphon.Tests.Scripts.ScriptHarnessProcess.RunAsync(String harness, String prefix, String caseName, String script, ScriptHarnessOptions options, CancellationToken callerToken, Action`1 validate) in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Scripts\ScriptHarnessProcess.cs:line 84
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at Antiphon.Tests.Scripts.ScriptHarnessProcess.RunAsync(String harness, String prefix, String caseName, String script, ScriptHarnessOptions options, CancellationToken callerToken, Action`1 validate) in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Scripts\ScriptHarnessProcess.cs:line 145
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
```


Supplemental ownership refusal from the same stored message:

```text
\u001b[31;1m\u001b[0m\u001b[36;1m\u001b[36;1m\u001b[0m\u001b[36;1m\u001b[0m\u001b[36;1m\u001b[31;1m\u001b[31;1m\u001b[36;1m\u001b[31;1m\u001b[36;1m     | \u001b[31;1m{"ExitCode":3,"RunId":"omitted","Phase":"ownership","Refusal":"wrong-origin","Sha":"","coverageComplete":false,"testsPassed":false,"reportDelivered":false,"trigger":"rc","profile":"rc","ref":"rc/2026-10-18","CreditKind":"","CandidateId":"rc-2026-10-18-6437fbb91de2","StateRoot":"C:\\Users\\lndco\\AppData\\Local\\Temp\\c1336-nightly-1cf56dfb0b3e4e0da721281269ec0068\\candidate fixture\\omitted\\state","LogDir":null,"OwnsLock":false,"LocalDueDate":"2026-10-26","PolicyHash":"","SummaryPath":"","Selection":null}\u001b[0m
```


### H. Checkpoint baseline classification and historical coverage binding (4 results)

Baseline classification returns NEW instead of INHERITED; historical coverage returns exit 2. The 7f7aa584 report records base Linux no-repro (8 baseline cases and 2 historical methods pass). Reader failure is a plausible secondary cause, not demonstrated in these errors. Native baseline parse Error/Ok and coverage JSON are required to confirm the mechanism.

<a id="f055"></a>

#### F055 — `Antiphon.Tests.Checkpoints.BaselineComparerTests.red_at_base_census_is_inherited_when_the_census_skips_build_output_folders`

- Display: `red_at_base_census_is_inherited_when_the_census_skips_build_output_folders`
- Suite/chunk: `antiphon/all-group-62`; duration `00:00:04.0385550`; manifest row 62.
- Evidence: `antiphon-all-group-62.trx:362`; test identity `63683ab3-35fc-c4f7-333f-14c2835b3f91`; execution ID `11b15db0-d9a7-42ee-a9d7-0ae8e967e174`.
- Disposition: **NO-REPRO** — 7f7aa584 report records unchanged-base Linux passes; no direct repair. Collect native parse/coverage diagnostics; do not infer Windows green.

Error, first 12 lines (all lines if shorter):

```text
ShouldAssertException: kinds.Single().Kind
    should be
"INHERITED"
    but was
"NEW"
    difference
Difference     |  |    |    |    |    |    |    |    |    |   
               | \|/  \|/  \|/  \|/  \|/  \|/  \|/  \|/  \|/  
Index          | 0    1    2    3    4    5    6    7    8    
Expected Value | I    N    H    E    R    I    T    E    D    
Actual Value   | N    E    W                                  
Expected Code  | 73   78   72   69   82   73   84   69   68
```


Top six stored stack frames (all if fewer):

```text
   at Antiphon.Tests.Checkpoints.BaselineComparerTests.red_at_base_census_is_inherited_when_the_census_skips_build_output_folders() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Checkpoints\BaselineComparerTests.cs:line 88
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at System.Runtime.CompilerServices.TaskAwaiter.ThrowForNonSuccess(Task task)
   at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at System.Runtime.CompilerServices.TaskAwaiter.ThrowForNonSuccess(Task task)
```


<a id="f056"></a>

#### F056 — `Antiphon.Tests.Checkpoints.BaselineComparerTests.classifies_inherited_introduced_new`

- Display: `classifies_inherited_introduced_new`
- Suite/chunk: `antiphon/all-group-62`; duration `00:00:04.0726505`; manifest row 62.
- Evidence: `antiphon-all-group-62.trx:438`; test identity `2d87753a-4133-e50d-4434-82e091b55ee8`; execution ID `1c2e59c5-fe7b-4932-9de3-5e90808bbc7d`.
- Disposition: **NO-REPRO** — 7f7aa584 report records unchanged-base Linux passes; no direct repair. Collect native parse/coverage diagnostics; do not infer Windows green.

Error, first 12 lines (all lines if shorter):

```text
ShouldAssertException: kinds.Single(item => item.Name == "N.Kept.bad").Kind
    should be
"INHERITED"
    but was
"NEW"
    difference
Difference     |  |    |    |    |    |    |    |    |    |   
               | \|/  \|/  \|/  \|/  \|/  \|/  \|/  \|/  \|/  
Index          | 0    1    2    3    4    5    6    7    8    
Expected Value | I    N    H    E    R    I    T    E    D    
Actual Value   | N    E    W                                  
Expected Code  | 73   78   72   69   82   73   84   69   68
```


Top six stored stack frames (all if fewer):

```text
   at Antiphon.Tests.Checkpoints.BaselineComparerTests.classifies_inherited_introduced_new() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Checkpoints\BaselineComparerTests.cs:line 48
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at System.Runtime.CompilerServices.TaskAwaiter.ThrowForNonSuccess(Task task)
   at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at System.Runtime.CompilerServices.TaskAwaiter.ThrowForNonSuccess(Task task)
```


<a id="f065"></a>

#### F065 — `Antiphon.Tests.Checkpoints.PlanCoverageBindingTests.historical_row_timeout_checklist_binds_the_advisory_method`

- Display: `historical_row_timeout_checklist_binds_the_advisory_method`
- Suite/chunk: `antiphon/all-group-63`; duration `00:00:03.8723345`; manifest row 63.
- Evidence: `antiphon-all-group-63.trx:372`; test identity `76b88250-e2a4-7874-6109-1e8a23292808`; execution ID `f21fef97-b68e-49b2-baaa-9b95e5468d59`.
- Disposition: **NO-REPRO** — 7f7aa584 report records unchanged-base Linux passes; no direct repair. Collect native parse/coverage diagnostics; do not infer Windows green.

Error, first 12 lines (all lines if shorter):

```text
ShouldAssertException: CheckpointNativeDiagnostics.Coverage(exit, root, output)
    should be
0
    but was
2

Additional Info:
    c1055-0891
```


Top six stored stack frames (all if fewer):

```text
   at Antiphon.Tests.Checkpoints.PlanCoverageBindingTests.<historical_row_timeout_checklist_binds_the_advisory_method>g__Bind|5_0(String plan, String[] tests, String id, Int32 obligations, String label, <>c__DisplayClass5_0&) in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Checkpoints\PlanCoverageBindingTests.cs:line 258
   at Antiphon.Tests.Checkpoints.PlanCoverageBindingTests.historical_row_timeout_checklist_binds_the_advisory_method(CancellationToken cancellationToken) in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Checkpoints\PlanCoverageBindingTests.cs:line 235
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at System.Runtime.CompilerServices.TaskAwaiter.ThrowForNonSuccess(Task task)
   at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
```


<a id="f071"></a>

#### F071 — `Antiphon.Tests.Checkpoints.PlanCoverageBindingTests.historical_plans_bind_without_suppression`

- Display: `historical_plans_bind_without_suppression`
- Suite/chunk: `antiphon/all-group-63`; duration `00:00:06.2180368`; manifest row 63.
- Evidence: `antiphon-all-group-63.trx:633`; test identity `17f390bd-7f33-757b-d1ea-a0cfe7029492`; execution ID `d158c4e9-4702-4684-a0ae-3b4754a3dad3`.
- Disposition: **NO-REPRO** — 7f7aa584 report records unchanged-base Linux passes; no direct repair. Collect native parse/coverage diagnostics; do not infer Windows green.

Error, first 12 lines (all lines if shorter):

```text
ShouldAssertException: CheckpointNativeDiagnostics.Coverage(firstExit, CheckpointFixtures.RepoRoot, first)
    should be
0
    but was
2

Additional Info:
    c1046-real-plans
```


Top six stored stack frames (all if fewer):

```text
   at Antiphon.Tests.Checkpoints.PlanCoverageBindingTests.historical_plans_bind_without_suppression() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Checkpoints\PlanCoverageBindingTests.cs:line 177
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at System.Runtime.CompilerServices.TaskAwaiter.ThrowForNonSuccess(Task task)
   at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at System.Runtime.CompilerServices.TaskAwaiter.ThrowForNonSuccess(Task task)
```


### I. Daemon build mocks cross a subprocess boundary (6 results)

All six results time out near 45 seconds in DaemonStartupDiagnosticsTests. Branch 50faf42f reproduces loss of a parent-only build mock after the build-slot child boundary and moves the mock through the child seam. Native rerun is still required.

<a id="f120"></a>

#### F120 — `Antiphon.SessionRunner.Tests.DaemonStartupDiagnosticsTests.Build_failure_retries_without_launching_failed_output`

- Display: `Build_failure_retries_without_launching_failed_output(powershell·exe, False, 1)`
- Suite/chunk: `session-runner/all-group-2`; duration `00:00:45.2124902`; manifest row 181.
- Evidence: `session-runner-all-group-2.trx:229`; test identity `eabfb73b-b4c7-2704-387e-8a62515826c5`; execution ID `2973ce86-e339-4c19-9554-e3e009c74677`.
- Disposition: **ADDRESSED** — 50faf42f / 25a98a02853bffabcadc0a399ea01deab479b549 carries daemon build mocks through the build-slot child seam.

Error, first 12 lines (all lines if shorter):

```text
TimeoutException: The operation has timed out.
```


Top six stored stack frames (all if fewer):

```text
   at Antiphon.SessionRunner.Tests.RestartFixture.Process(String path, String shellPath, String[] args) in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.SessionRunner.Tests\Fixtures\RunnerRestart\RestartFixture.cs:line 86
   at Antiphon.SessionRunner.Tests.RestartFixture.Process(String path, String shellPath, String[] args) in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.SessionRunner.Tests\Fixtures\RunnerRestart\RestartFixture.cs:line 93
   at Antiphon.SessionRunner.Tests.RestartFixture.Script(String script, String shell, String[] args) in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.SessionRunner.Tests\Fixtures\RunnerRestart\RestartFixture.cs:line 65
   at Antiphon.SessionRunner.Tests.DaemonStartupDiagnosticsTests.Exercise(String shell, Boolean failFirst, Int32 runs, Boolean build) in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.SessionRunner.Tests\DaemonStartupDiagnosticsTests.cs:line 39
   at Antiphon.SessionRunner.Tests.DaemonStartupDiagnosticsTests.Build_failure_retries_without_launching_failed_output(String shell, Boolean failFirst, Int32 runs) in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.SessionRunner.Tests\DaemonStartupDiagnosticsTests.cs:line 12
```


<a id="f121"></a>

#### F121 — `Antiphon.SessionRunner.Tests.DaemonStartupDiagnosticsTests.Build_failure_retries_without_launching_failed_output`

- Display: `Build_failure_retries_without_launching_failed_output(pwsh·exe, False, 1)`
- Suite/chunk: `session-runner/all-group-2`; duration `00:00:45.2213335`; manifest row 181.
- Evidence: `session-runner-all-group-2.trx:242`; test identity `bd8dfaff-5a78-f776-eb1b-309be1aa90ec`; execution ID `28604456-7097-444e-b728-e561c0156860`.
- Disposition: **ADDRESSED** — 50faf42f / 25a98a02853bffabcadc0a399ea01deab479b549 carries daemon build mocks through the build-slot child seam.

Error, first 12 lines (all lines if shorter):

```text
TimeoutException: The operation has timed out.
```


Top six stored stack frames (all if fewer):

```text
   at Antiphon.SessionRunner.Tests.RestartFixture.Process(String path, String shellPath, String[] args) in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.SessionRunner.Tests\Fixtures\RunnerRestart\RestartFixture.cs:line 86
   at Antiphon.SessionRunner.Tests.RestartFixture.Process(String path, String shellPath, String[] args) in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.SessionRunner.Tests\Fixtures\RunnerRestart\RestartFixture.cs:line 93
   at Antiphon.SessionRunner.Tests.RestartFixture.Script(String script, String shell, String[] args) in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.SessionRunner.Tests\Fixtures\RunnerRestart\RestartFixture.cs:line 65
   at Antiphon.SessionRunner.Tests.DaemonStartupDiagnosticsTests.Exercise(String shell, Boolean failFirst, Int32 runs, Boolean build) in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.SessionRunner.Tests\DaemonStartupDiagnosticsTests.cs:line 39
   at Antiphon.SessionRunner.Tests.DaemonStartupDiagnosticsTests.Build_failure_retries_without_launching_failed_output(String shell, Boolean failFirst, Int32 runs) in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.SessionRunner.Tests\DaemonStartupDiagnosticsTests.cs:line 12
```


<a id="f122"></a>

#### F122 — `Antiphon.SessionRunner.Tests.DaemonStartupDiagnosticsTests.Build_failure_retries_without_launching_failed_output`

- Display: `Build_failure_retries_without_launching_failed_output(pwsh·exe, True, 1)`
- Suite/chunk: `session-runner/all-group-2`; duration `00:00:45.1952308`; manifest row 181.
- Evidence: `session-runner-all-group-2.trx:255`; test identity `f3189c25-c77b-5213-6f15-7286c58f8ec9`; execution ID `c9a1e40d-6b87-48e2-9db2-c4787fa646e4`.
- Disposition: **ADDRESSED** — 50faf42f / 25a98a02853bffabcadc0a399ea01deab479b549 carries daemon build mocks through the build-slot child seam.

Error, first 12 lines (all lines if shorter):

```text
TimeoutException: The operation has timed out.
```


Top six stored stack frames (all if fewer):

```text
   at Antiphon.SessionRunner.Tests.RestartFixture.Process(String path, String shellPath, String[] args) in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.SessionRunner.Tests\Fixtures\RunnerRestart\RestartFixture.cs:line 86
   at Antiphon.SessionRunner.Tests.RestartFixture.Process(String path, String shellPath, String[] args) in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.SessionRunner.Tests\Fixtures\RunnerRestart\RestartFixture.cs:line 93
   at Antiphon.SessionRunner.Tests.RestartFixture.Script(String script, String shell, String[] args) in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.SessionRunner.Tests\Fixtures\RunnerRestart\RestartFixture.cs:line 65
   at Antiphon.SessionRunner.Tests.DaemonStartupDiagnosticsTests.Exercise(String shell, Boolean failFirst, Int32 runs, Boolean build) in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.SessionRunner.Tests\DaemonStartupDiagnosticsTests.cs:line 39
   at Antiphon.SessionRunner.Tests.DaemonStartupDiagnosticsTests.Build_failure_retries_without_launching_failed_output(String shell, Boolean failFirst, Int32 runs) in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.SessionRunner.Tests\DaemonStartupDiagnosticsTests.cs:line 12
```


<a id="f123"></a>

#### F123 — `Antiphon.SessionRunner.Tests.DaemonStartupDiagnosticsTests.Build_failure_retries_without_launching_failed_output`

- Display: `Build_failure_retries_without_launching_failed_output(powershell·exe, True, 1)`
- Suite/chunk: `session-runner/all-group-2`; duration `00:00:45.1468149`; manifest row 181.
- Evidence: `session-runner-all-group-2.trx:268`; test identity `bd6dc4ad-ce33-7fdb-2074-dfa07d7fa07b`; execution ID `62e407c4-9bac-424b-865a-76f149036a36`.
- Disposition: **ADDRESSED** — 50faf42f / 25a98a02853bffabcadc0a399ea01deab479b549 carries daemon build mocks through the build-slot child seam.

Error, first 12 lines (all lines if shorter):

```text
TimeoutException: The operation has timed out.
```


Top six stored stack frames (all if fewer):

```text
   at Antiphon.SessionRunner.Tests.RestartFixture.Process(String path, String shellPath, String[] args) in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.SessionRunner.Tests\Fixtures\RunnerRestart\RestartFixture.cs:line 86
   at Antiphon.SessionRunner.Tests.RestartFixture.Process(String path, String shellPath, String[] args) in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.SessionRunner.Tests\Fixtures\RunnerRestart\RestartFixture.cs:line 93
   at Antiphon.SessionRunner.Tests.RestartFixture.Script(String script, String shell, String[] args) in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.SessionRunner.Tests\Fixtures\RunnerRestart\RestartFixture.cs:line 65
   at Antiphon.SessionRunner.Tests.DaemonStartupDiagnosticsTests.Exercise(String shell, Boolean failFirst, Int32 runs, Boolean build) in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.SessionRunner.Tests\DaemonStartupDiagnosticsTests.cs:line 39
   at Antiphon.SessionRunner.Tests.DaemonStartupDiagnosticsTests.Build_failure_retries_without_launching_failed_output(String shell, Boolean failFirst, Int32 runs) in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.SessionRunner.Tests\DaemonStartupDiagnosticsTests.cs:line 12
```


<a id="f124"></a>

#### F124 — `Antiphon.SessionRunner.Tests.DaemonStartupDiagnosticsTests.Every_relaunch_builds_and_gets_a_fresh_attempt`

- Display: `Every_relaunch_builds_and_gets_a_fresh_attempt(pwsh·exe)`
- Suite/chunk: `session-runner/all-group-2`; duration `00:00:45.2720970`; manifest row 181.
- Evidence: `session-runner-all-group-2.trx:281`; test identity `d5179d5c-7727-fcb1-91f9-344f8edb9394`; execution ID `186675ba-5eea-49ad-8e27-78cc002681d0`.
- Disposition: **ADDRESSED** — 50faf42f / 25a98a02853bffabcadc0a399ea01deab479b549 carries daemon build mocks through the build-slot child seam.

Error, first 12 lines (all lines if shorter):

```text
TimeoutException: The operation has timed out.
```


Top six stored stack frames (all if fewer):

```text
   at Antiphon.SessionRunner.Tests.RestartFixture.Process(String path, String shellPath, String[] args) in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.SessionRunner.Tests\Fixtures\RunnerRestart\RestartFixture.cs:line 86
   at Antiphon.SessionRunner.Tests.RestartFixture.Process(String path, String shellPath, String[] args) in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.SessionRunner.Tests\Fixtures\RunnerRestart\RestartFixture.cs:line 93
   at Antiphon.SessionRunner.Tests.RestartFixture.Script(String script, String shell, String[] args) in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.SessionRunner.Tests\Fixtures\RunnerRestart\RestartFixture.cs:line 65
   at Antiphon.SessionRunner.Tests.DaemonStartupDiagnosticsTests.Exercise(String shell, Boolean failFirst, Int32 runs, Boolean build) in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.SessionRunner.Tests\DaemonStartupDiagnosticsTests.cs:line 39
   at Antiphon.SessionRunner.Tests.DaemonStartupDiagnosticsTests.Every_relaunch_builds_and_gets_a_fresh_attempt(String shell) in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.SessionRunner.Tests\DaemonStartupDiagnosticsTests.cs:line 14
```


<a id="f125"></a>

#### F125 — `Antiphon.SessionRunner.Tests.DaemonStartupDiagnosticsTests.Every_relaunch_builds_and_gets_a_fresh_attempt`

- Display: `Every_relaunch_builds_and_gets_a_fresh_attempt(powershell·exe)`
- Suite/chunk: `session-runner/all-group-2`; duration `00:00:45.1612023`; manifest row 181.
- Evidence: `session-runner-all-group-2.trx:294`; test identity `3d1819c1-935c-d644-3352-6800c5ab227b`; execution ID `d4152dfb-7ca5-405a-a255-c731c7d4cf2a`.
- Disposition: **ADDRESSED** — 50faf42f / 25a98a02853bffabcadc0a399ea01deab479b549 carries daemon build mocks through the build-slot child seam.

Error, first 12 lines (all lines if shorter):

```text
TimeoutException: The operation has timed out.
```


Top six stored stack frames (all if fewer):

```text
   at Antiphon.SessionRunner.Tests.RestartFixture.Process(String path, String shellPath, String[] args) in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.SessionRunner.Tests\Fixtures\RunnerRestart\RestartFixture.cs:line 86
   at Antiphon.SessionRunner.Tests.RestartFixture.Process(String path, String shellPath, String[] args) in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.SessionRunner.Tests\Fixtures\RunnerRestart\RestartFixture.cs:line 93
   at Antiphon.SessionRunner.Tests.RestartFixture.Script(String script, String shell, String[] args) in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.SessionRunner.Tests\Fixtures\RunnerRestart\RestartFixture.cs:line 65
   at Antiphon.SessionRunner.Tests.DaemonStartupDiagnosticsTests.Exercise(String shell, Boolean failFirst, Int32 runs, Boolean build) in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.SessionRunner.Tests\DaemonStartupDiagnosticsTests.cs:line 39
   at Antiphon.SessionRunner.Tests.DaemonStartupDiagnosticsTests.Every_relaunch_builds_and_gets_a_fresh_attempt(String shell) in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.SessionRunner.Tests\DaemonStartupDiagnosticsTests.cs:line 14
```


### J. Browser layout, menus, navigation and stale selectors (5 results)

The menu disappears before the visible assertion; the phone glance extends to x=565 in a 391-wide allowance; Diff review is absent; workflow deletion navigates to /workflows while the test waits for **/; the workflow article never becomes visible. Pushed UI/selector changes cover F129/F130/F133. No listed change closes the two workflow failures.

<a id="f129"></a>

#### F129 — `Antiphon.E2E.AgentE2ETests.Agents_page_terminal_icon_context_menu_reaches_settings`

- Display: `Agents_page_terminal_icon_context_menu_reaches_settings`
- Suite/chunk: `e2e/all-group-5`; duration `00:00:07.4975007`; manifest row 252.
- Evidence: `e2e-all-group-5.trx:462`; test identity `4981a619-ab64-26dc-d5d9-03782e2f1d98`; execution ID `b5b2bc44-c8b2-462f-938b-f2f94b1eaca0`.
- Disposition: **ADDRESSED** — c320441d / cd793a1f0fae61ae522af005db610f1fee845561 prevents terminal shortcut from dismissing its own menu.

Error, first 12 lines (all lines if shorter):

```text
PlaywrightException: Locator expected to be visible
Error: element(s) not found 
Call log:
  - Expect "ToBeVisibleAsync" GetByRole(AriaRole.Menu, new() { Name = "Terminal menu E2E Agent contextmenu" }) with timeout 5000ms
  - waiting for GetByRole(AriaRole.Menu, new() { Name = "Terminal menu E2E Agent contextmenu" })
Aria snapshot:
- banner:
  - button "Antiphon":
    - paragraph: Antiphon
  - link "Home":
    - /url: /
  - link "Workflows":
```


Top six stored stack frames (all if fewer):

```text
   at Microsoft.Playwright.Core.AssertionsBase.ExpectImplAsync(String expression, FrameExpectOptions expectOptions, Object expected, String message, String title) in /_/src/Playwright/Core/AssertionsBase.cs:line 95
   at Microsoft.Playwright.Core.AssertionsBase.ExpectImplAsync(String expression, ExpectedTextValue[] expectedText, Object expected, String message, String title, FrameExpectOptions options) in /_/src/Playwright/Core/AssertionsBase.cs:line 69
   at Antiphon.E2E.AgentE2ETests.Agents_page_terminal_icon_context_menu_reaches_settings() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.E2E\AgentE2ETests.cs:line 198
   at Antiphon.E2E.AgentE2ETests.Agents_page_terminal_icon_context_menu_reaches_settings() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.E2E\AgentE2ETests.cs:line 209
```


<a id="f130"></a>

#### F130 — `Antiphon.E2E.PipelineGlanceE2ETests.C557_combined_glance_on_phone`

- Display: `C557_combined_glance_on_phone`
- Suite/chunk: `e2e/all-group-9`; duration `00:00:02.5505364`; manifest row 256.
- Evidence: `e2e-all-group-9.trx:586`; test identity `0b036cf6-054a-06ee-7e5d-c08561ab5f19`; execution ID `3ef4ede7-c3d9-4ddd-bfb1-3e412e62a8bd`.
- Disposition: **ADDRESSED** — 1e248236 / 58e097b12212ce325a2a864c19cdadc02b439c41 constrains pipeline glance to viewport; daca35af03133c40625e4767de964afeaed080d9 fixes queue hold timing.

Error, first 12 lines (all lines if shorter):

```text
ShouldAssertException: box.X + box.Width
    should be less than or equal to
391f
    but was
565f
```


Top six stored stack frames (all if fewer):

```text
   at Antiphon.E2E.PipelineGlanceE2ETests.C557_combined_glance_on_phone() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.E2E\PipelineGlanceE2ETests.cs:line 74
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at Antiphon.E2E.PipelineGlanceE2ETests.C557_combined_glance_on_phone() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.E2E\PipelineGlanceE2ETests.cs:line 96
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at System.Runtime.CompilerServices.TaskAwaiter.ThrowForNonSuccess(Task task)
   at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
```


<a id="f133"></a>

#### F133 — `Antiphon.E2E.BoardE2ETests.Board_user_can_review_real_worktree_diff_and_send_inline_comment`

- Display: `Board_user_can_review_real_worktree_diff_and_send_inline_comment`
- Suite/chunk: `e2e/all-split-39`; duration `00:00:25.2754414`; manifest row 297.
- Evidence: `e2e-all-split-39.trx:8`; test identity `ad044220-4050-0f53-fa66-77c5a4eba780`; execution ID `666fbf2e-047e-4092-a7e8-c4b36ad016ca`.
- Disposition: **ADDRESSED** — 380013d3 / 03d62fedb18a36c573b8b508ad2fe21f26985f48 updates diff navigation and terminal input ordering.

Error, first 12 lines (all lines if shorter):

```text
PlaywrightException: Locator expected to be visible
Error: element(s) not found 
Call log:
  - Expect "ToBeVisibleAsync" GetByRole(AriaRole.Dialog).GetByText("Diff review") with timeout 15000ms
  - waiting for GetByRole(AriaRole.Dialog).GetByText("Diff review")
Aria snapshot:
- banner:
  - button "Antiphon":
    - paragraph: Antiphon
  - link "Home":
    - /url: /
  - link "Workflows":
```


Top six stored stack frames (all if fewer):

```text
   at Microsoft.Playwright.Core.AssertionsBase.ExpectImplAsync(String expression, FrameExpectOptions expectOptions, Object expected, String message, String title) in /_/src/Playwright/Core/AssertionsBase.cs:line 95
   at Microsoft.Playwright.Core.AssertionsBase.ExpectImplAsync(String expression, ExpectedTextValue[] expectedText, Object expected, String message, String title, FrameExpectOptions options) in /_/src/Playwright/Core/AssertionsBase.cs:line 69
   at Antiphon.E2E.BoardE2ETests.Board_user_can_review_real_worktree_diff_and_send_inline_comment() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.E2E\BoardE2ETests.cs:line 502
   at Antiphon.E2E.BoardE2ETests.Board_user_can_review_real_worktree_diff_and_send_inline_comment() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.E2E\BoardE2ETests.cs:line 546
```


<a id="f136"></a>

#### F136 — `Antiphon.E2E.WorkflowDeleteTests.Delete_workflow_via_settings_button_in_detail_page_navigates_to_dashboard`

- Display: `Delete_workflow_via_settings_button_in_detail_page_navigates_to_dashboard`
- Suite/chunk: `e2e/all-split-61`; duration `00:00:15.9825505`; manifest row 319.
- Evidence: `e2e-all-split-61.trx:607`; test identity `8f39eb4f-a27a-0d9e-5756-f5daf34e7555`; execution ID `bb0d6d7d-1659-4fe7-8820-579bd65ff352`.
- Disposition: **UNRESOLVED** — No matching pushed repair established; native mechanism remains open.

Error, first 12 lines (all lines if shorter):

```text
TimeoutException: Timeout 10000ms exceeded.
=========================== logs ===========================
waiting for navigation to "**/" until "Load"
  navigated to "http://127.0.0.1:62453/workflows"
============================================================
```


Top six stored stack frames (all if fewer):

```text
   at Microsoft.Playwright.Core.Waiter.WaitForPromiseAsync[T](Task`1 task, Action dispose) in /_/src/Playwright/Core/Waiter.cs:line 221
   at Microsoft.Playwright.Core.Frame.WaitForNavigationInternalAsync(Waiter waiter, String urlString, Func`2 urlFunc, Regex urlRegex, Nullable`1 waitUntil) in /_/src/Playwright/Core/Frame.cs:line 325
   at Microsoft.Playwright.Core.Frame.RunAndWaitForNavigationAsync(Func`1 action, FrameRunAndWaitForNavigationOptions options) in /_/src/Playwright/Core/Frame.cs:line 289
   at Antiphon.E2E.WorkflowDeleteTests.Delete_workflow_via_settings_button_in_detail_page_navigates_to_dashboard() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.E2E\WorkflowDeleteTests.cs:line 294
   at Antiphon.E2E.WorkflowDeleteTests.Delete_workflow_via_settings_button_in_detail_page_navigates_to_dashboard() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.E2E\WorkflowDeleteTests.cs:line 313
```


<a id="f137"></a>

#### F137 — `Antiphon.E2E.WorkflowDeleteTests.Delete_workflow_button_shows_confirm_and_removes_card`

- Display: `Delete_workflow_button_shows_confirm_and_removes_card`
- Suite/chunk: `e2e/all-split-61`; duration `00:00:14.8136184`; manifest row 319.
- Evidence: `e2e-all-split-61.trx:1331`; test identity `6d64b5aa-9130-32f2-9429-d4d464071fb6`; execution ID `d645b9f9-89ae-4749-bf2f-95659914c5f8`.
- Disposition: **UNRESOLVED** — No matching pushed repair established; native mechanism remains open.

Error, first 12 lines (all lines if shorter):

```text
TimeoutException: Timeout 10000ms exceeded.
Call log:
  - waiting for Locator("[role='article']").First to be visible
```


Top six stored stack frames (all if fewer):

```text
   at Microsoft.Playwright.Transport.Connection.InnerSendMessageToServerAsync[T](ChannelOwner object, String method, Dictionary`2 dictionary, Boolean keepNulls, Nullable`1 timeout) in /_/src/Playwright/Transport/Connection.cs:line 230
   at Microsoft.Playwright.Transport.Connection.WrapApiCallAsync[T](Func`1 action, Boolean isInternal, String title) in /_/src/Playwright/Transport/Connection.cs:line 539
   at Antiphon.E2E.WorkflowDeleteTests.Delete_workflow_button_shows_confirm_and_removes_card() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.E2E\WorkflowDeleteTests.cs:line 345
   at Antiphon.E2E.WorkflowDeleteTests.Delete_workflow_button_shows_confirm_and_removes_card() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.E2E\WorkflowDeleteTests.cs:line 410
```


### K. E2E landing outcomes, receipt confirmation and mismatch dispatch intent (4 results)

The observed terminal event is Landed rather than LandedWithResidue; a delivery note is already ConfirmedAt; two mismatch dispatch intent waits expire. These can involve fixture setup, concurrent workers or product behavior; stored assertions alone do not choose between them. Exact native debug evidence is still needed.

<a id="f131"></a>

#### F131 — `Antiphon.E2E.AgentTaskLandDeliveryE2ETests.C467_V29_RealOutcomeProducerMatrix_Batch2`

- Display: `C467_V29_RealOutcomeProducerMatrix_Batch2(residue-cleanup)`
- Suite/chunk: `e2e/all-split-2`; duration `00:01:09.6139634`; manifest row 260.
- Evidence: `e2e-all-split-2.trx:8`; test identity `e8cc7f02-06fd-3b65-d762-4000c38cc83a`; execution ID `a3efb38d-9429-4217-9961-22b0a29038ac`.
- Disposition: **UNRESOLVED** — No matching pushed repair established; native mechanism remains open.

Error, first 12 lines (all lines if shorter):

```text
ShouldAssertException: terminal.Type
    should be
AgentTaskEventType.LandedWithResidue
    but was
AgentTaskEventType.Landed
```


Top six stored stack frames (all if fewer):

```text
   at Antiphon.E2E.AgentTaskLandDeliveryE2ETests.C467_V29_RealOutcomeProducerMatrix(String outcome) in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.E2E\AgentTaskLandDeliveryE2ETests.cs:line 211
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at Antiphon.E2E.AgentTaskLandDeliveryE2ETests.C467_V29_RealOutcomeProducerMatrix(String outcome) in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.E2E\AgentTaskLandDeliveryE2ETests.cs:line 222
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at Antiphon.E2E.AgentTaskLandDeliveryE2ETests.C467_V29_RealOutcomeProducerMatrix(String outcome) in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.E2E\AgentTaskLandDeliveryE2ETests.cs:line 233
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
```


<a id="f132"></a>

#### F132 — `Antiphon.E2E.AgentTaskLandDeliveryE2ETests.C488_ReviewDeliveryCrashMatrix_Batch5`

- Display: `C488_ReviewDeliveryCrashMatrix_Batch5(prompt-before-verdict)`
- Suite/chunk: `e2e/all-split-34`; duration `00:03:03.6632488`; manifest row 292.
- Evidence: `e2e-all-split-34.trx:8`; test identity `99d25b24-139c-f50b-2758-8afa919ffb2b`; execution ID `b1613533-2020-4fee-a067-a8f9d5abf3cf`.
- Disposition: **UNRESOLVED** — No matching pushed repair established; native mechanism remains open.

Error, first 12 lines (all lines if shorter):

```text
ShouldAssertException: note.ConfirmedAt
    should be null but was
2026-10-27T03:38:38.5361780Z
```


Top six stored stack frames (all if fewer):

```text
   at Antiphon.E2E.Fixtures.LandDeliveryFixture.AssertReviewHeldAsync(String cut) in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.E2E\Fixtures\LandDeliveryFixture.Review.cs:line 269
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at Antiphon.E2E.Fixtures.LandDeliveryFixture.AssertReviewHeldAsync(String cut) in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.E2E\Fixtures\LandDeliveryFixture.Review.cs:line 270
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at System.Runtime.CompilerServices.TaskAwaiter.ThrowForNonSuccess(Task task)
   at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
```


<a id="f134"></a>

#### F134 — `Antiphon.E2E.DispatchBaseWarningDeliveryE2ETests.C508_MismatchWarningReachesCaller`

- Display: `C508_MismatchWarningReachesCaller`
- Suite/chunk: `e2e/all-split-44`; duration `00:02:46.1427955`; manifest row 302.
- Evidence: `e2e-all-split-44.trx:8`; test identity `a0fd0510-bd71-8102-f188-7e3bafc9c279`; execution ID `14f24d53-ebcd-4a5c-bcae-48247c3313dd`.
- Disposition: **UNRESOLVED** — No matching pushed repair established; native mechanism remains open.

Error, first 12 lines (all lines if shorter):

```text
TimeoutException: one committed mismatch dispatch intent
```


Top six stored stack frames (all if fewer):

```text
   at Antiphon.TestSupport.ProgressAwareWait.UntilAsync(Func`1 predicate, String evidence, Int32 seconds, Func`1 progress, Int32 capSeconds, TimeProvider clock) in C:\Antiphon\releases\checkout-997186e6\tests\Shared\ProgressAwareWait.cs:line 36
   at Antiphon.E2E.Fixtures.LandDeliveryFixture.WaitForMismatchIntentAsync() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.E2E\Fixtures\LandDeliveryFixture.Dispatch.cs:line 130
   at Antiphon.E2E.DispatchBaseWarningDeliveryE2ETests.C508_MismatchWarningReachesCaller() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.E2E\DispatchBaseWarningDeliveryE2ETests.cs:line 30
   at Antiphon.E2E.DispatchBaseWarningDeliveryE2ETests.C508_MismatchWarningReachesCaller() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.E2E\DispatchBaseWarningDeliveryE2ETests.cs:line 31
```


<a id="f135"></a>

#### F135 — `Antiphon.E2E.DispatchBaseWarningDeliveryE2ETests.C508_MismatchWarningWaitsForCaller`

- Display: `C508_MismatchWarningWaitsForCaller`
- Suite/chunk: `e2e/all-split-45`; duration `00:02:46.8200512`; manifest row 303.
- Evidence: `e2e-all-split-45.trx:8`; test identity `a55a7154-9270-bab6-d423-f2bf8f13f43c`; execution ID `e6dc1519-b410-4265-b62c-472be1d31ece`.
- Disposition: **UNRESOLVED** — No matching pushed repair established; native mechanism remains open.

Error, first 12 lines (all lines if shorter):

```text
TimeoutException: one committed mismatch dispatch intent
```


Top six stored stack frames (all if fewer):

```text
   at Antiphon.TestSupport.ProgressAwareWait.UntilAsync(Func`1 predicate, String evidence, Int32 seconds, Func`1 progress, Int32 capSeconds, TimeProvider clock) in C:\Antiphon\releases\checkout-997186e6\tests\Shared\ProgressAwareWait.cs:line 36
   at Antiphon.E2E.Fixtures.LandDeliveryFixture.WaitForMismatchIntentAsync() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.E2E\Fixtures\LandDeliveryFixture.Dispatch.cs:line 130
   at Antiphon.E2E.DispatchBaseWarningDeliveryE2ETests.C508_MismatchWarningWaitsForCaller() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.E2E\DispatchBaseWarningDeliveryE2ETests.cs:line 39
   at Antiphon.E2E.DispatchBaseWarningDeliveryE2ETests.C508_MismatchWarningWaitsForCaller() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.E2E\DispatchBaseWarningDeliveryE2ETests.cs:line 52
```


## Remaining uncertainty and native handoff

Confirmed in this investigation: the final outcome and suite census; 140 recorded failure instances with original error/stack/duration evidence; both absent TRX; diagnostic recovery of 1,500 exact missing UIDs; 179 still without terminal evidence; and the early-return/union mechanism behind the larger summary missing count. The multiple assertion roots are not collapsed into a single confirmed bug. Pushed fixes and no-repro reports are evidence of their stated scope, not native acceptance.

A Windows-side debugger can start with the open-class table and the exact F-record stack. For any recorded method, the existing targeted invocation shape is `pwsh -NoProfile -File scripts/build-slot.ps1 -Label <owned-label> -- dotnet run --project tests/<matching-project> --no-build --property:OutputPath=bin-<owned-label>/ -- --treenode-filter "/*/*/<Class>/<ExactMethod>" --report-trx --results-directory <fresh-owned-directory>`. This is a handoff recipe, not an execution receipt; it presumes one current, isolated, slot-gated build and the correct native prerequisites. No run was started here. Each case’s missing evidence is described in its disposition; retain base/fixed source SHA, native error details and actual phase timing rather than interpreting Linux no-repro as closure.

Not done, noted: no fix was designed or implemented; native debugging/verification of the open signatures and pending pushed repairs remains for the commissioned Code stage.

## Appendix: every skipped/unexecuted result identity

These are terminal skipped/unexecuted results, not absent results. The full TRX message is preserved with each row where supplied. Diagnostic-only skips are labeled and have their stored Explanation. Repeated parameter cases are retained.

| Evidence | Fully qualified method / display variant | Duration | Stored reason |
|---|---|---|---|
| `antiphon-all-group-3.trx:417` | `Antiphon.Tests.Agents.ClaudeDialogWatchdogCanaryTests.C680_LiveNudgeRefusesPermissionAndQuestionDialogs` / C680_LiveNudgeRefusesPermissionAndQuestionDialogs | 00:00:00.0079998 | Skipped: Native watchdog qualification is explicitly opt-in. |
| `antiphon-all-group-26.trx:1410` | `Antiphon.Tests.Application.LandGraphEvaluationTests.C1483_S1_UnreadableLinkComponentDenies` / C1483_S1_UnreadableLinkComponentDenies | 00:00:00.0051103 | Skipped: CARD-1483: Unix mode 000 has no Windows equivalent |
| `antiphon-all-group-37.trx:1707` | `Antiphon.Tests.Application.PhoneHomeMirrorJoinTests.C1032_JoinStoreMismatch` / C1032_JoinStoreMismatch | 00:00:00.0082686 | Skipped: CP-21 phone-home join requires Linux FIFOs and Unix file modes. |
| `antiphon-all-group-37.trx:1714` | `Antiphon.Tests.Application.PhoneHomeMirrorJoinTests.C1032_JoinStaleBinding` / C1032_JoinStaleBinding | 00:00:00.0036426 | Skipped: CP-21 phone-home join requires Linux FIFOs and Unix file modes. |
| `antiphon-all-group-37.trx:1721` | `Antiphon.Tests.Application.PhoneHomeMirrorJoinTests.C1032_JoinForeignOwner` / C1032_JoinForeignOwner | 00:00:00.0032081 | Skipped: CP-21 phone-home join requires Linux FIFOs and Unix file modes. |
| `antiphon-all-group-37.trx:1728` | `Antiphon.Tests.Application.PhoneHomeMirrorJoinTests.C1032_JoinCrashRetry` / C1032_JoinCrashRetry | 00:00:00.0030741 | Skipped: CP-21 phone-home join requires Linux FIFOs and Unix file modes. |
| `antiphon-all-group-37.trx:1735` | `Antiphon.Tests.Application.PhoneHomeMirrorJoinTests.C1032_JoinBusyRecipient` / C1032_JoinBusyRecipient | 00:00:00.0036170 | Skipped: CP-21 phone-home join requires Linux FIFOs and Unix file modes. |
| `antiphon-all-group-37.trx:1742` | `Antiphon.Tests.Application.PhoneHomeMirrorJoinTests.C1032_JoinIdleRecipient` / C1032_JoinIdleRecipient | 00:00:00.0022220 | Skipped: CP-21 phone-home join requires Linux FIFOs and Unix file modes. |
| `antiphon-all-group-49.trx:1984` | `Antiphon.Tests.Application.PhoneHomeCompletedCardTransportTests.C1032_TransportQueueRecipientReplay` / C1032_TransportQueueRecipientReplay | 00:00:00.0029842 | Skipped: Real phone-home runner join qualified on Linux |
| `antiphon-all-group-57.trx:688` | `Antiphon.Tests.Application.UnixDelegateLaunchArgvTests.Composed_delegate_argv_reaches_native_child` / Composed_delegate_argv_reaches_native_child(Code) | 00:00:00.0084543 | Skipped: Unix PTY argv witness; Windows argv has its own native parser tests. |
| `antiphon-all-group-57.trx:695` | `Antiphon.Tests.Application.UnixDelegateLaunchArgvTests.Composed_delegate_argv_reaches_native_child` / Composed_delegate_argv_reaches_native_child(Investigate) | 00:00:00.0005337 | Skipped: Unix PTY argv witness; Windows argv has its own native parser tests. |
| `antiphon-all-group-61.trx:46` | `Antiphon.Tests.Checkpoints.CheckpointBoundedReaderTests.changed_readers_bound_fifo_and_exited_child_pipes` / changed_readers_bound_fifo_and_exited_child_pipes(tool-git, ordinary) | 00:00:00.0474737 | Skipped: Linux FIFO and process-group qualification |
| `antiphon-all-group-61.trx:53` | `Antiphon.Tests.Checkpoints.CheckpointBoundedReaderTests.C1381_Project_read_budget_excludes_startup_delay` / C1381_Project_read_budget_excludes_startup_delay | 00:00:00.0015851 | Skipped: Linux FIFO and process-group qualification |
| `antiphon-all-group-61.trx:60` | `Antiphon.Tests.Checkpoints.CheckpointBoundedReaderTests.changed_readers_bound_fifo_and_exited_child_pipes` / changed_readers_bound_fifo_and_exited_child_pipes(script-git, ordinary) | 00:00:00.0006921 | Skipped: Linux FIFO and process-group qualification |
| `antiphon-all-group-61.trx:73` | `Antiphon.Tests.Checkpoints.CheckpointBoundedReaderTests.changed_readers_bound_fifo_and_exited_child_pipes` / changed_readers_bound_fifo_and_exited_child_pipes(project, ordinary) | 00:00:00.0014663 | Skipped: Linux FIFO and process-group qualification |
| `antiphon-all-group-61.trx:80` | `Antiphon.Tests.Checkpoints.CheckpointBoundedReaderTests.changed_readers_bound_fifo_and_exited_child_pipes` / changed_readers_bound_fifo_and_exited_child_pipes(tool-git, held) | 00:00:00.0008223 | Skipped: Linux FIFO and process-group qualification |
| `antiphon-all-group-61.trx:87` | `Antiphon.Tests.Checkpoints.CheckpointBoundedReaderTests.changed_readers_bound_fifo_and_exited_child_pipes` / changed_readers_bound_fifo_and_exited_child_pipes(script-git, held) | 00:00:00.0008045 | Skipped: Linux FIFO and process-group qualification |
| `antiphon-all-group-61.trx:94` | `Antiphon.Tests.Checkpoints.CheckpointBoundedReaderTests.changed_readers_bound_fifo_and_exited_child_pipes` / changed_readers_bound_fifo_and_exited_child_pipes(tool-eval, ordinary) | 00:00:00.0010278 | Skipped: Linux FIFO and process-group qualification |
| `antiphon-all-group-61.trx:101` | `Antiphon.Tests.Checkpoints.CheckpointBoundedReaderTests.changed_readers_bound_fifo_and_exited_child_pipes` / changed_readers_bound_fifo_and_exited_child_pipes(tool-eval, held) | 00:00:00.0005138 | Skipped: Linux FIFO and process-group qualification |
| `antiphon-all-group-61.trx:108` | `Antiphon.Tests.Checkpoints.CheckpointBoundedReaderTests.changed_readers_bound_fifo_and_exited_child_pipes` / changed_readers_bound_fifo_and_exited_child_pipes(script-eval, ordinary) | 00:00:00.0007034 | Skipped: Linux FIFO and process-group qualification |
| `antiphon-all-group-61.trx:139` | `Antiphon.Tests.Checkpoints.CheckpointBoundedReaderTests.changed_readers_bound_fifo_and_exited_child_pipes` / changed_readers_bound_fifo_and_exited_child_pipes(script-eval, held) | 00:00:00.0007276 | Skipped: Linux FIFO and process-group qualification |
| `antiphon-all-group-61.trx:152` | `Antiphon.Tests.Checkpoints.CheckpointBoundedReaderTests.changed_readers_bound_fifo_and_exited_child_pipes` / changed_readers_bound_fifo_and_exited_child_pipes(project, held) | 00:00:00.0005179 | Skipped: Linux FIFO and process-group qualification |
| `antiphon-all-group-61.trx:746` | `Antiphon.Tests.Checkpoints.CheckpointConfinementLedgerTests.unchanged_ledger_entries_are_not_hashed_when_the_slot_is_granted` / unchanged_ledger_entries_are_not_hashed_when_the_slot_is_granted | 00:00:00.0016272 | Skipped: the statx stamp (inode and ctime) is read on Linux only |
| `antiphon-all-group-61.trx:820` | `Antiphon.Tests.Checkpoints.CheckpointFixtureRoutingTests.C1233_TokenCallers` / C1233_TokenCallers(F1) | 00:00:00.0029223 | Skipped: CARD-1233 S8 requires Linux process identities and FIFO probes. |
| `antiphon-all-group-61.trx:827` | `Antiphon.Tests.Checkpoints.CheckpointFixtureRoutingTests.C1233_TokenCallers` / C1233_TokenCallers(F2) | 00:00:00.0005710 | Skipped: CARD-1233 S8 requires Linux process identities and FIFO probes. |
| `antiphon-all-group-61.trx:834` | `Antiphon.Tests.Checkpoints.CheckpointFixtureRoutingTests.C1233_TokenCallers` / C1233_TokenCallers(F3) | 00:00:00.0030765 | Skipped: CARD-1233 S8 requires Linux process identities and FIFO probes. |
| `antiphon-all-group-61.trx:841` | `Antiphon.Tests.Checkpoints.CheckpointFixtureRoutingTests.C1233_TokenCallers` / C1233_TokenCallers(F9) | 00:00:00.0006147 | Skipped: CARD-1233 S8 requires Linux process identities and FIFO probes. |
| `antiphon-all-group-61.trx:848` | `Antiphon.Tests.Checkpoints.CheckpointInheritedFileTests.C1233_FileCallers` / C1233_FileCallers(R1) | 00:00:00.0031528 | Skipped: Linux FIFO admission proof |
| `antiphon-all-group-61.trx:855` | `Antiphon.Tests.Checkpoints.CheckpointInheritedFileTests.C1233_FileCallers` / C1233_FileCallers(R2) | 00:00:00.0004582 | Skipped: Linux FIFO admission proof |
| `antiphon-all-group-61.trx:862` | `Antiphon.Tests.Checkpoints.CheckpointInheritedFileTests.C1233_FileCallers` / C1233_FileCallers(R4) | 00:00:00.0004402 | Skipped: Linux FIFO admission proof |
| `antiphon-all-group-61.trx:869` | `Antiphon.Tests.Checkpoints.CheckpointInheritedFileTests.C1233_FileCallers` / C1233_FileCallers(R3) | 00:00:00.0003952 | Skipped: Linux FIFO admission proof |
| `antiphon-all-group-61.trx:930` | `Antiphon.Tests.Checkpoints.CheckpointOwnedOutputTests.changed_inventory_is_refused` / changed_inventory_is_refused(link) | 00:00:00.0009508 | Skipped: Linux symlink proof; Windows junction remains R2 |
| `antiphon-all-group-61.trx:963` | `Antiphon.Tests.Checkpoints.CheckpointReceiptSafetyTests.linux_fifo_receipts_refuse_within_four_seconds` / linux_fifo_receipts_refuse_within_four_seconds | 00:00:00.0009080 | Skipped: Linux FIFO qualification requires Linux |
| `antiphon-all-group-61.trx:1073` | `Antiphon.Tests.Checkpoints.CheckpointRecoveryLinuxTests.dead_test_host_live_nested_executor_retains_root` / dead_test_host_live_nested_executor_retains_root | 00:00:00.0023208 | Skipped: native Linux custody proof requires Linux |
| `antiphon-all-group-61.trx:1080` | `Antiphon.Tests.Checkpoints.CheckpointRecoveryLinuxTests.native_identity_keeps_live_then_removes_dead` / native_identity_keeps_live_then_removes_dead | 00:00:00.0016821 | Skipped: native Linux custody proof requires Linux |
| `antiphon-all-group-61.trx:1087` | `Antiphon.Tests.Checkpoints.CheckpointRecoveryLinuxTests.detached_staged_copy_survives_starter_exit` / detached_staged_copy_survives_starter_exit | 00:00:00.0022723 | Skipped: native Linux custody proof requires Linux |
| `antiphon-all-group-61.trx:1094` | `Antiphon.Tests.Checkpoints.CheckpointRecoveryLinuxTests.done_live_executor_retains_image` / done_live_executor_retains_image | 00:00:00.0022266 | Skipped: native Linux custody proof requires Linux |
| `antiphon-all-group-61.trx:1101` | `Antiphon.Tests.Checkpoints.CheckpointRecoveryLinuxTests.starter_death_before_launch_reclaims_partial_copy` / starter_death_before_launch_reclaims_partial_copy | 00:00:00.0013631 | Skipped: native Linux custody proof requires Linux |
| `antiphon-all-group-61.trx:1108` | `Antiphon.Tests.Checkpoints.CheckpointRecoveryLinuxTests.executor_death_after_ack_before_progress_recovers` / executor_death_after_ack_before_progress_recovers | 00:00:00.0016969 | Skipped: native Linux custody proof requires Linux |
| `antiphon-all-group-61.trx:1115` | `Antiphon.Tests.Checkpoints.CheckpointRecoveryLinuxTests.launch_unknown_survives_until_late_ack` / launch_unknown_survives_until_late_ack | 00:00:00.0016983 | Skipped: native Linux custody proof requires Linux |
| `antiphon-all-group-61.trx:1122` | `Antiphon.Tests.Checkpoints.CheckpointRecoveryLinuxTests.other_worktree_live_owner_survives_sweep` / other_worktree_live_owner_survives_sweep | 00:00:00.0017340 | Skipped: native Linux custody proof requires Linux |
| `antiphon-all-group-61.trx:1129` | `Antiphon.Tests.Checkpoints.CheckpointRecoveryLinuxTests.concurrent_sweepers_cannot_delete_new_registration` / concurrent_sweepers_cannot_delete_new_registration | 00:00:00.0015148 | Skipped: native Linux custody proof requires Linux |
| `antiphon-all-group-61.trx:1136` | `Antiphon.Tests.Checkpoints.CheckpointRecoveryLinuxTests.native_links_keep_targets_unchanged` / native_links_keep_targets_unchanged | 00:00:00.0006102 | Skipped: native Linux custody proof requires Linux |
| `antiphon-all-group-61.trx:2721` | `Antiphon.Tests.Checkpoints.CheckpointShapeConflictTests.shape_conflict_rule` / shape_conflict_rule(deleted-tracked) | 00:00:00.0023934 | Skipped: CARD-1199 shape-conflict proof runs on Linux; Windows junctions remain an R2 disclosure. |
| `antiphon-all-group-61.trx:2728` | `Antiphon.Tests.Checkpoints.CheckpointShapeConflictTests.shape_conflict_rule` / shape_conflict_rule(symlink-on-file) | 00:00:00.0003539 | Skipped: CARD-1199 shape-conflict proof runs on Linux; Windows junctions remain an R2 disclosure. |
| `antiphon-all-group-61.trx:2735` | `Antiphon.Tests.Checkpoints.CheckpointShapeConflictTests.shape_conflict_rule` / shape_conflict_rule(file-on-directory) | 00:00:00.0003028 | Skipped: CARD-1199 shape-conflict proof runs on Linux; Windows junctions remain an R2 disclosure. |
| `antiphon-all-group-61.trx:2742` | `Antiphon.Tests.Checkpoints.CheckpointShapeConflictTests.shape_conflict_rule` / shape_conflict_rule(directory-on-file) | 00:00:00.0002972 | Skipped: CARD-1199 shape-conflict proof runs on Linux; Windows junctions remain an R2 disclosure. |
| `antiphon-all-group-61.trx:2749` | `Antiphon.Tests.Checkpoints.CheckpointShapeConflictTests.shape_conflict_rule` / shape_conflict_rule(untracked-descendant) | 00:00:00.0003019 | Skipped: CARD-1199 shape-conflict proof runs on Linux; Windows junctions remain an R2 disclosure. |
| `antiphon-all-group-61.trx:2756` | `Antiphon.Tests.Checkpoints.CheckpointShapeConflictTests.shape_conflict_rule` / shape_conflict_rule(staged-untracked) | 00:00:00.0003961 | Skipped: CARD-1199 shape-conflict proof runs on Linux; Windows junctions remain an R2 disclosure. |
| `antiphon-all-group-61.trx:2763` | `Antiphon.Tests.Checkpoints.CheckpointShapeConflictTests.shape_conflict_rule` / shape_conflict_rule(modified-tracked) | 00:00:00.0003551 | Skipped: CARD-1199 shape-conflict proof runs on Linux; Windows junctions remain an R2 disclosure. |
| `antiphon-all-group-61.trx:2770` | `Antiphon.Tests.Checkpoints.CheckpointShapeConflictTests.shape_conflict_rule` / shape_conflict_rule(clean-build) | 00:00:00.0003366 | Skipped: CARD-1199 shape-conflict proof runs on Linux; Windows junctions remain an R2 disclosure. |
| `antiphon-all-group-61.trx:2777` | `Antiphon.Tests.Checkpoints.CheckpointShapeConflictTests.shape_conflict_rule` / shape_conflict_rule(ignored-descendant) | 00:00:00.0003202 | Skipped: CARD-1199 shape-conflict proof runs on Linux; Windows junctions remain an R2 disclosure. |
| `antiphon-all-group-61.trx:2784` | `Antiphon.Tests.Checkpoints.CheckpointShapeConflictTests.missing_receipt_is_not_negative_success` / missing_receipt_is_not_negative_success | 00:00:00.0006679 | Skipped: CARD-1199 shape-conflict proof runs on Linux; Windows junctions remain an R2 disclosure. |
| `antiphon-all-group-61.trx:2791` | `Antiphon.Tests.Checkpoints.CheckpointShapeConflictTests.shape_conflict_rule` / shape_conflict_rule(existing-output-directory) | 00:00:00.0003424 | Skipped: CARD-1199 shape-conflict proof runs on Linux; Windows junctions remain an R2 disclosure. |
| `antiphon-all-group-61.trx:2798` | `Antiphon.Tests.Checkpoints.CheckpointShapeConflictTests.shape_conflict_rule` / shape_conflict_rule(compiler-collision) | 00:00:00.0033492 | Skipped: CARD-1199 shape-conflict proof runs on Linux; Windows junctions remain an R2 disclosure. |
| `antiphon-all-group-61.trx:2805` | `Antiphon.Tests.Checkpoints.CheckpointShapeConflictTests.shape_conflict_rule` / shape_conflict_rule(existing-stamp) | 00:00:00.0005231 | Skipped: CARD-1199 shape-conflict proof runs on Linux; Windows junctions remain an R2 disclosure. |
| `antiphon-all-group-61.trx:2812` | `Antiphon.Tests.Checkpoints.CheckpointShapeConflictTests.shape_conflict_rule` / shape_conflict_rule(changed-untracked) | 00:00:00.0003838 | Skipped: CARD-1199 shape-conflict proof runs on Linux; Windows junctions remain an R2 disclosure. |
| `antiphon-all-group-61.trx:2819` | `Antiphon.Tests.Checkpoints.CheckpointShapeConflictTests.shape_conflict_rule` / shape_conflict_rule(deleted-untracked) | 00:00:00.0003411 | Skipped: CARD-1199 shape-conflict proof runs on Linux; Windows junctions remain an R2 disclosure. |
| `antiphon-all-group-61.trx:2826` | `Antiphon.Tests.Checkpoints.CheckpointShapeConflictTests.shape_conflict_rule` / shape_conflict_rule(regular-user-move) | 00:00:00.0003897 | Skipped: CARD-1199 shape-conflict proof runs on Linux; Windows junctions remain an R2 disclosure. |
| `antiphon-all-group-61.trx:2833` | `Antiphon.Tests.Checkpoints.CheckpointShapeConflictTests.shape_conflict_rule` / shape_conflict_rule(existing-output-symlink) | 00:00:00.0004296 | Skipped: CARD-1199 shape-conflict proof runs on Linux; Windows junctions remain an R2 disclosure. |
| `antiphon-all-group-61.trx:3944` | `Antiphon.Tests.Checkpoints.CheckpointStampSafetyTests.tracked_stamp_shapes_are_bounded_and_leave_results_in_place` / tracked_stamp_shapes_are_bounded_and_leave_results_in_place(tool, fifo) | 00:00:00.0018696 | Skipped: Linux source-shape qualification |
| `antiphon-all-group-61.trx:3951` | `Antiphon.Tests.Checkpoints.CheckpointStampSafetyTests.tracked_stamp_shapes_are_bounded_and_leave_results_in_place` / tracked_stamp_shapes_are_bounded_and_leave_results_in_place(script, directory) | 00:00:00.0001917 | Skipped: Linux source-shape qualification |
| `antiphon-all-group-61.trx:3958` | `Antiphon.Tests.Checkpoints.CheckpointStampSafetyTests.tracked_stamp_shapes_are_bounded_and_leave_results_in_place` / tracked_stamp_shapes_are_bounded_and_leave_results_in_place(tool, directory) | 00:00:00.0002764 | Skipped: Linux source-shape qualification |
| `antiphon-all-group-61.trx:3971` | `Antiphon.Tests.Checkpoints.OutputCleanupTests.independent_writer_after_build_exit_is_not_adopted_and_cleanup_keeps_it` / independent_writer_after_build_exit_is_not_adopted_and_cleanup_keeps_it | 00:00:00.0015125 | Skipped: seal-time custody needs statx |
| `antiphon-all-group-61.trx:3985` | `Antiphon.Tests.Checkpoints.OutputCleanupTests.file_modified_after_seal_is_refused_and_kept` / file_modified_after_seal_is_refused_and_kept | 00:00:00.0021863 | Skipped: this helper needs Linux statx |
| `antiphon-all-group-61.trx:3992` | `Antiphon.Tests.Checkpoints.OutputCleanupTests.symlink_in_a_build_output_is_refused_before_and_after_seal` / symlink_in_a_build_output_is_refused_before_and_after_seal | 00:00:00.0010812 | Skipped: this helper needs Linux statx |
| `antiphon-all-group-61.trx:3999` | `Antiphon.Tests.Checkpoints.OutputCleanupTests.tracked_file_in_an_adopted_build_output_is_refused_and_kept` / tracked_file_in_an_adopted_build_output_is_refused_and_kept | 00:00:00.0007515 | Skipped: this helper needs Linux statx |
| `antiphon-all-group-61.trx:4006` | `Antiphon.Tests.Checkpoints.OutputCleanupTests.adopted_build_output_over_the_entry_cap_is_unsafe_tree` / adopted_build_output_over_the_entry_cap_is_unsafe_tree | 00:00:00.0009936 | Skipped: this helper needs Linux statx |
| `antiphon-all-group-61.trx:4029` | `Antiphon.Tests.Checkpoints.OutputCleanupTests.seal_without_driver_observed_build_exit_adopts_nothing` / seal_without_driver_observed_build_exit_adopts_nothing | 00:00:00.0010546 | Skipped: seal-time custody needs statx |
| `antiphon-all-group-61.trx:4036` | `Antiphon.Tests.Checkpoints.OutputCleanupTests.hard_linked_build_file_is_not_adopted_and_outside_bytes_stay` / hard_linked_build_file_is_not_adopted_and_outside_bytes_stay | 00:00:00.0012137 | Skipped: this helper needs Linux statx |
| `antiphon-all-group-61.trx:4043` | `Antiphon.Tests.Checkpoints.OutputCleanupTests.file_added_after_seal_is_refused_and_kept` / file_added_after_seal_is_refused_and_kept | 00:00:00.0009057 | Skipped: this helper needs Linux statx |
| `antiphon-all-group-61.trx:4070` | `Antiphon.Tests.Checkpoints.CheckpointStampSafetyTests.tracked_stamp_shapes_are_bounded_and_leave_results_in_place` / tracked_stamp_shapes_are_bounded_and_leave_results_in_place(script, regular) | 00:00:00.0004921 | Skipped: Linux source-shape qualification |
| `antiphon-all-group-61.trx:4077` | `Antiphon.Tests.Checkpoints.CheckpointStampSafetyTests.tracked_stamp_shapes_are_bounded_and_leave_results_in_place` / tracked_stamp_shapes_are_bounded_and_leave_results_in_place(tool, regular) | 00:00:00.0005533 | Skipped: Linux source-shape qualification |
| `antiphon-all-group-61.trx:4084` | `Antiphon.Tests.Checkpoints.CheckpointStampSafetyTests.tracked_stamp_shapes_are_bounded_and_leave_results_in_place` / tracked_stamp_shapes_are_bounded_and_leave_results_in_place(script, dangling) | 00:00:00.0003713 | Skipped: Linux source-shape qualification |
| `antiphon-all-group-61.trx:4091` | `Antiphon.Tests.Checkpoints.CheckpointStampSafetyTests.tracked_stamp_shapes_are_bounded_and_leave_results_in_place` / tracked_stamp_shapes_are_bounded_and_leave_results_in_place(tool, dangling) | 00:00:00.0005554 | Skipped: Linux source-shape qualification |
| `antiphon-all-group-61.trx:4098` | `Antiphon.Tests.Checkpoints.CheckpointStampSafetyTests.tracked_stamp_shapes_are_bounded_and_leave_results_in_place` / tracked_stamp_shapes_are_bounded_and_leave_results_in_place(script, symlink) | 00:00:00.0004736 | Skipped: Linux source-shape qualification |
| `antiphon-all-group-61.trx:4105` | `Antiphon.Tests.Checkpoints.CheckpointStampSafetyTests.tracked_stamp_shapes_are_bounded_and_leave_results_in_place` / tracked_stamp_shapes_are_bounded_and_leave_results_in_place(tool, symlink) | 00:00:00.0004176 | Skipped: Linux source-shape qualification |
| `antiphon-all-group-61.trx:4124` | `Antiphon.Tests.Checkpoints.OutputCleanupTests.external_build_payload_is_adopted_at_seal_and_removed_on_green` / external_build_payload_is_adopted_at_seal_and_removed_on_green | 00:00:00.0009711 | Skipped: this helper needs Linux statx |
| `antiphon-all-group-61.trx:4131` | `Antiphon.Tests.Checkpoints.CheckpointStampSafetyTests.tracked_stamp_shapes_are_bounded_and_leave_results_in_place` / tracked_stamp_shapes_are_bounded_and_leave_results_in_place(script, fifo) | 00:00:00.0006844 | Skipped: Linux source-shape qualification |
| `antiphon-all-group-61.trx:4268` | `Antiphon.Tests.Checkpoints.OutputCleanupTests.hanging_git_is_refused_and_killed_within_the_bound` / hanging_git_is_refused_and_killed_within_the_bound | 00:00:00.0021516 | Skipped: hanging-git proof runs on Linux |
| `antiphon-all-group-61.trx:4323` | `Antiphon.Tests.Checkpoints.OutputCleanupTests.parent_symlink_does_not_delete_a_tracked_stamp` / parent_symlink_does_not_delete_a_tracked_stamp | 00:00:00.0013909 | Skipped: Windows junction variant is unverified |
| `antiphon-all-group-61.trx:4482` | `Antiphon.Tests.Checkpoints.CheckpointTrackedBaselineTests.staged_ignored_file_is_kept_and_indexed` / staged_ignored_file_is_kept_and_indexed | 00:00:00.0021252 | Skipped: CARD-1199 tracked-baseline proof runs on Linux; Windows is unverified. |
| `antiphon-all-group-61.trx:4489` | `Antiphon.Tests.Checkpoints.CheckpointTrackedBaselineTests.staged_untracked_file_is_kept_and_indexed` / staged_untracked_file_is_kept_and_indexed | 00:00:00.0011641 | Skipped: CARD-1199 tracked-baseline proof runs on Linux; Windows is unverified. |
| `antiphon-all-group-61.trx:4496` | `Antiphon.Tests.Checkpoints.CheckpointTrackedBaselineTests.tracked_snapshot_contains_committed_files` / tracked_snapshot_contains_committed_files | 00:00:00.0016259 | Skipped: CARD-1199 tracked-baseline proof runs on Linux; Windows is unverified. |
| `antiphon-all-group-61.trx:4503` | `Antiphon.Tests.Checkpoints.CheckpointTrackedBaselineTests.fresh_checkout_run_executes_a_test` / fresh_checkout_run_executes_a_test | 00:00:00.0014119 | Skipped: CARD-1199 tracked-baseline proof runs on Linux; Windows is unverified. |
| `antiphon-all-group-61.trx:4510` | `Antiphon.Tests.Checkpoints.CheckpointTrackedBaselineTests.clean_untracked_output_build_exits_zero` / clean_untracked_output_build_exits_zero | 00:00:00.0015984 | Skipped: CARD-1199 tracked-baseline proof runs on Linux; Windows is unverified. |
| `antiphon-all-group-61.trx:4517` | `Antiphon.Tests.Checkpoints.CheckpointTrackedBaselineTests.generated_target_file_tampering_is_reported` / generated_target_file_tampering_is_reported | 00:00:00.0010919 | Skipped: CARD-1199 tracked-baseline proof runs on Linux; Windows is unverified. |
| `antiphon-all-group-61.trx:4524` | `Antiphon.Tests.Checkpoints.CheckpointTrackedBaselineTests.transitive_reference_tampering_is_reported` / transitive_reference_tampering_is_reported | 00:00:00.0011992 | Skipped: CARD-1199 tracked-baseline proof runs on Linux; Windows is unverified. |
| `antiphon-all-group-61.trx:4531` | `Antiphon.Tests.Checkpoints.CheckpointTrackedBaselineTests.imported_props_reference_tampering_is_reported` / imported_props_reference_tampering_is_reported | 00:00:00.0009973 | Skipped: CARD-1199 tracked-baseline proof runs on Linux; Windows is unverified. |
| `antiphon-all-group-61.trx:4538` | `Antiphon.Tests.Checkpoints.CheckpointTrackedBaselineTests.intermediate_output_path_tampering_is_reported` / intermediate_output_path_tampering_is_reported | 00:00:00.0009636 | Skipped: CARD-1199 tracked-baseline proof runs on Linux; Windows is unverified. |
| `antiphon-all-group-61.trx:4545` | `Antiphon.Tests.Checkpoints.CheckpointTrackedBaselineTests.local_output_property_tampering_is_reported` / local_output_property_tampering_is_reported | 00:00:00.0010423 | Skipped: CARD-1199 tracked-baseline proof runs on Linux; Windows is unverified. |
| `antiphon-all-group-61.trx:4552` | `Antiphon.Tests.Checkpoints.CheckpointTrackedBaselineTests.index_added_file_is_kept_and_indexed` / index_added_file_is_kept_and_indexed | 00:00:00.0011668 | Skipped: CARD-1199 tracked-baseline proof runs on Linux; Windows is unverified. |
| `antiphon-all-group-61.trx:4559` | `Antiphon.Tests.Checkpoints.CheckpointTrackedBaselineTests.dirty_tracked_tree_is_refused_and_unmodified` / dirty_tracked_tree_is_refused_and_unmodified | 00:00:00.0014795 | Skipped: CARD-1199 tracked-baseline proof runs on Linux; Windows is unverified. |
| `antiphon-all-group-61.trx:4566` | `Antiphon.Tests.Checkpoints.CheckpointTrackedBaselineTests.passing_script_requires_writable_source_receipt` / passing_script_requires_writable_source_receipt | 00:00:00.0015243 | Skipped: CARD-1199 tracked-baseline proof runs on Linux; Windows is unverified. |
| `antiphon-all-group-62.trx:1227` | `Antiphon.Tests.Checkpoints.CheckpointOutputConfinementTests.unevaluable_import_is_refused_before_the_build` / unevaluable_import_is_refused_before_the_build | 00:00:00.0025442 | Skipped: CARD-1199 output confinement real-build proof runs on Linux; Windows is unverified. |
| `antiphon-all-group-62.trx:1234` | `Antiphon.Tests.Checkpoints.CheckpointOutputConfinementTests.transitive_import_build_preserves_the_tracked_dependency` / transitive_import_build_preserves_the_tracked_dependency | 00:00:00.0008416 | Skipped: CARD-1199 output confinement real-build proof runs on Linux; Windows is unverified. |
| `antiphon-all-group-62.trx:1241` | `Antiphon.Tests.Checkpoints.CheckpointOutputConfinementTests.directory_build_props_build_preserves_the_tracked_dependency` / directory_build_props_build_preserves_the_tracked_dependency | 00:00:00.0006265 | Skipped: CARD-1199 output confinement real-build proof runs on Linux; Windows is unverified. |
| `antiphon-all-group-62.trx:1248` | `Antiphon.Tests.Checkpoints.CheckpointOutputConfinementTests.imported_props_build_preserves_the_tracked_dependency` / imported_props_build_preserves_the_tracked_dependency | 00:00:00.0006362 | Skipped: CARD-1199 output confinement real-build proof runs on Linux; Windows is unverified. |
| `antiphon-all-group-62.trx:1273` | `Antiphon.Tests.Checkpoints.CheckpointOutputConfinementTests.conditional_import_build_preserves_the_tracked_dependency` / conditional_import_build_preserves_the_tracked_dependency | 00:00:00.0009008 | Skipped: CARD-1199 output confinement real-build proof runs on Linux; Windows is unverified. |
| `antiphon-all-group-62.trx:1370` | `Antiphon.Tests.Checkpoints.PlanCoverageCommandTests.outside_project_fifo_is_refused_without_opening` / outside_project_fifo_is_refused_without_opening | 00:00:00.0019275 | Skipped: coverage outside-project FIFO read sentinel requires Linux mkfifo |
| `antiphon-all-group-62.trx:1468` | `Antiphon.Tests.Checkpoints.CheckpointScriptReaderDeadlineTests.script_reader_re_arms_its_stall_window_and_the_stamp_reports_unreadable_apart_from_tampered` / script_reader_re_arms_its_stall_window_and_the_stamp_reports_unreadable_apart_from_tampered(tampered-beside-unreadable) | 00:00:00.0006285 | Skipped: Linux hard-link ledger qualification; the reader cases run on every platform |
| `antiphon-all-group-62.trx:1475` | `Antiphon.Tests.Checkpoints.CheckpointScriptReaderDeadlineTests.script_reader_re_arms_its_stall_window_and_the_stamp_reports_unreadable_apart_from_tampered` / script_reader_re_arms_its_stall_window_and_the_stamp_reports_unreadable_apart_from_tampered(unreadable) | 00:00:00.0002682 | Skipped: Linux hard-link ledger qualification; the reader cases run on every platform |
| `antiphon-all-group-62.trx:1482` | `Antiphon.Tests.Checkpoints.CheckpointSourceGitBoundsTests.C1233_ToolSource` / C1233_ToolSource | 00:00:00.0016486 | Skipped: Linux inherited-pipe custody proof |
| `antiphon-all-group-62.trx:1489` | `Antiphon.Tests.Checkpoints.CheckpointSourceGitBoundsTests.C1233_ScriptSource` / C1233_ScriptSource | 00:00:00.0015192 | Skipped: Linux inherited-pipe custody proof |
| `antiphon-all-group-62.trx:2993` | `Antiphon.Tests.Checkpoints.CheckpointVerifierBoundsTests.C1233_VerifierGit` / C1233_VerifierGit(temp) | 00:00:00.0016102 | Skipped: Linux inherited-pipe custody proof |
| `antiphon-all-group-62.trx:3000` | `Antiphon.Tests.Checkpoints.CheckpointVerifierBoundsTests.C1233_VerifierGit` / C1233_VerifierGit(orphan) | 00:00:00.0003171 | Skipped: Linux inherited-pipe custody proof |
| `antiphon-all-group-62.trx:3007` | `Antiphon.Tests.Checkpoints.PlanCoverageHandleTests.in_repo_fifo_project_is_refused_within_15_seconds` / in_repo_fifo_project_is_refused_within_15_seconds | 00:00:00.0019407 | Skipped: FIFO sentinel requires Linux mkfifo |
| `antiphon-all-group-62.trx:3056` | `Antiphon.Tests.Checkpoints.PlanCoverageHandleTests.hardlinked_selected_file_is_refused` / hardlinked_selected_file_is_refused(project) | 00:00:00.0013949 | Skipped: hardlink sentinel requires Linux link(2) |
| `antiphon-all-group-62.trx:3063` | `Antiphon.Tests.Checkpoints.PlanCoverageHandleTests.hardlinked_selected_file_is_refused` / hardlinked_selected_file_is_refused(source) | 00:00:00.0002162 | Skipped: hardlink sentinel requires Linux link(2) |
| `antiphon-all-group-62.trx:3070` | `Antiphon.Tests.Checkpoints.PlanCoverageHandleTests.hardlinked_selected_file_is_refused` / hardlinked_selected_file_is_refused(plan) | 00:00:00.0003233 | Skipped: hardlink sentinel requires Linux link(2) |
| `antiphon-all-group-62.trx:3089` | `Antiphon.Tests.Checkpoints.PlanCoverageHandleTests.hardlinked_selected_file_is_refused` / hardlinked_selected_file_is_refused(checklist) | 00:00:00.0004920 | Skipped: hardlink sentinel requires Linux link(2) |
| `antiphon-all-group-63.trx:8` | `Antiphon.Tests.Checkpoints.CheckpointFixtureProcessTests.fixture_git_blocked_opening_its_config_fails_within_the_limit` / fixture_git_blocked_opening_its_config_fails_within_the_limit | 00:00:00.0375089 | Skipped: FIFO config proof runs on Linux |
| `antiphon-all-group-63.trx:458` | `Antiphon.Tests.Checkpoints.CheckpointFixtureProcessTests.fixture_child_whose_descendant_holds_a_pipe_fails_within_the_limit_and_is_killed` / fixture_child_whose_descendant_holds_a_pipe_fails_within_the_limit_and_is_killed | 00:00:00.0019329 | Skipped: process-group custody proof runs on Linux |
| `antiphon-all-group-63.trx:941` | `Antiphon.Tests.Checkpoints.DetachedLauncherTests.executor_survives_its_starter` / executor_survives_its_starter | 00:00:00.0027768 | Skipped: DetachedLauncher setsid proof runs on Linux; Review runs the Windows half. |
| `antiphon-all-group-63.trx:955` | `Antiphon.Tests.Checkpoints.SafeDeleteProcessBoundaryTests.exited_git_parent_with_pipe_holding_descendant_meets_one_deadline_and_is_killed` / exited_git_parent_with_pipe_holding_descendant_meets_one_deadline_and_is_killed | 00:00:00.0009398 | Skipped: process-group custody proof runs on Linux |
| `antiphon-all-group-63.trx:962` | `Antiphon.Tests.Checkpoints.SafeDeleteProcessBoundaryTests.configured_fsmonitor_hook_is_not_run_by_the_preflight_reader` / configured_fsmonitor_hook_is_not_run_by_the_preflight_reader | 00:00:00.0006151 | Skipped: FIFO-held monitor proof runs on Linux |
| `antiphon-all-group-66.trx:1047` | `Antiphon.Tests.Infrastructure.GithubCredentialHelperTests.Helper_answers_nothing_for_another_host` / Helper_answers_nothing_for_another_host | 00:00:00.0027733 | Skipped: The POSIX credential helper runs on Linux. |
| `antiphon-all-group-66.trx:1054` | `Antiphon.Tests.Infrastructure.GithubCredentialHelperTests.Helper_answers_nothing_when_the_token_file_is_missing_or_empty` / Helper_answers_nothing_when_the_token_file_is_missing_or_empty | 00:00:00.0014736 | Skipped: The POSIX credential helper runs on Linux. |
| `antiphon-all-group-66.trx:1061` | `Antiphon.Tests.Infrastructure.GithubCredentialHelperTests.Helper_answers_nothing_for_a_path_outside_the_allow_list` / Helper_answers_nothing_for_a_path_outside_the_allow_list | 00:00:00.0018659 | Skipped: The POSIX credential helper runs on Linux. |
| `antiphon-all-group-66.trx:1068` | `Antiphon.Tests.Infrastructure.GithubCredentialHelperTests.Helper_ignores_store_and_erase_and_writes_nothing` / Helper_ignores_store_and_erase_and_writes_nothing | 00:00:00.0015388 | Skipped: The POSIX credential helper runs on Linux. |
| `antiphon-all-group-66.trx:1075` | `Antiphon.Tests.Infrastructure.GithubCredentialHelperTests.Helper_answers_an_admitted_github_path_with_the_mounted_token` / Helper_answers_an_admitted_github_path_with_the_mounted_token | 00:00:00.0015178 | Skipped: The POSIX credential helper runs on Linux. |
| `antiphon-all-group-66.trx:1082` | `Antiphon.Tests.Infrastructure.GithubCredentialHelperTests.Helper_answers_nothing_when_the_policy_file_is_missing` / Helper_answers_nothing_when_the_policy_file_is_missing | 00:00:00.0015499 | Skipped: The POSIX credential helper runs on Linux. |
| `antiphon-all-group-66.trx:1089` | `Antiphon.Tests.Infrastructure.GrokSha256PinTests.Runner_grok_sha_rejects_embedded_newline` / Runner_grok_sha_rejects_embedded_newline | 00:00:00.0027754 | Skipped: CARD-1210: Grok SHA pin contracts require native Linux (/bin/sh and POSIX executable semantics); Windows skips explicitly. |
| `antiphon-all-group-66.trx:1096` | `Antiphon.Tests.Infrastructure.GrokSha256PinTests.Runner_grok_sha_accepts_matching_file` / Runner_grok_sha_accepts_matching_file | 00:00:00.0011071 | Skipped: CARD-1210: Grok SHA pin contracts require native Linux (/bin/sh and POSIX executable semantics); Windows skips explicitly. |
| `antiphon-all-group-66.trx:1103` | `Antiphon.Tests.Infrastructure.GrokSha256PinTests.Runner_grok_sha_rejects_empty_download` / Runner_grok_sha_rejects_empty_download | 00:00:00.0011998 | Skipped: CARD-1210: Grok SHA pin contracts require native Linux (/bin/sh and POSIX executable semantics); Windows skips explicitly. |
| `antiphon-all-group-66.trx:1110` | `Antiphon.Tests.Infrastructure.GrokSha256PinTests.Runner_grok_sha_rejects_empty_pin` / Runner_grok_sha_rejects_empty_pin | 00:00:00.0014084 | Skipped: CARD-1210: Grok SHA pin contracts require native Linux (/bin/sh and POSIX executable semantics); Windows skips explicitly. |
| `antiphon-all-group-66.trx:1117` | `Antiphon.Tests.Infrastructure.GrokSha256PinTests.Runner_grok_sha_rejects_long_pin` / Runner_grok_sha_rejects_long_pin | 00:00:00.0019588 | Skipped: CARD-1210: Grok SHA pin contracts require native Linux (/bin/sh and POSIX executable semantics); Windows skips explicitly. |
| `antiphon-all-group-66.trx:1124` | `Antiphon.Tests.Infrastructure.GrokSha256PinTests.Runner_grok_sha_rejects_short_pin` / Runner_grok_sha_rejects_short_pin | 00:00:00.0011310 | Skipped: CARD-1210: Grok SHA pin contracts require native Linux (/bin/sh and POSIX executable semantics); Windows skips explicitly. |
| `antiphon-all-group-66.trx:1131` | `Antiphon.Tests.Infrastructure.GrokSha256PinTests.Runner_grok_sha_rejects_uppercase_pin` / Runner_grok_sha_rejects_uppercase_pin | 00:00:00.0010737 | Skipped: CARD-1210: Grok SHA pin contracts require native Linux (/bin/sh and POSIX executable semantics); Windows skips explicitly. |
| `antiphon-all-group-66.trx:1138` | `Antiphon.Tests.Infrastructure.GrokSha256PinTests.Runner_grok_sha_rejects_multiline_pin` / Runner_grok_sha_rejects_multiline_pin | 00:00:00.0011108 | Skipped: CARD-1210: Grok SHA pin contracts require native Linux (/bin/sh and POSIX executable semantics); Windows skips explicitly. |
| `antiphon-all-group-66.trx:1145` | `Antiphon.Tests.Infrastructure.GrokSha256PinTests.Runner_grok_sha_rejects_devnull_second_line` / Runner_grok_sha_rejects_devnull_second_line | 00:00:00.0010789 | Skipped: CARD-1210: Grok SHA pin contracts require native Linux (/bin/sh and POSIX executable semantics); Windows skips explicitly. |
| `antiphon-all-group-66.trx:1152` | `Antiphon.Tests.Infrastructure.GrokSha256PinTests.Runner_grok_sha_rejects_tampered_file` / Runner_grok_sha_rejects_tampered_file | 00:00:00.0012250 | Skipped: CARD-1210: Grok SHA pin contracts require native Linux (/bin/sh and POSIX executable semantics); Windows skips explicitly. |
| `antiphon-all-group-66.trx:1159` | `Antiphon.Tests.Infrastructure.GrokSha256PinTests.Runner_grok_sha_rejects_trailing_newline` / Runner_grok_sha_rejects_trailing_newline | 00:00:00.0011293 | Skipped: CARD-1210: Grok SHA pin contracts require native Linux (/bin/sh and POSIX executable semantics); Windows skips explicitly. |
| `antiphon-all-group-66.trx:1166` | `Antiphon.Tests.Infrastructure.JqRunnerImageContractTests.Version_row_accepts_only_exact_successful_pin_without_stderr` / Version_row_accepts_only_exact_successful_pin_without_stderr(jq-1·7·1, 0, , ok) | 00:00:00.0020166 | Skipped: CARD-1210: jq image probes require native Linux (POSIX modes, symlinks and executable identity); Windows skips explicitly. |
| `antiphon-all-group-66.trx:1173` | `Antiphon.Tests.Infrastructure.JqRunnerImageContractTests.Version_row_accepts_only_exact_successful_pin_without_stderr` / Version_row_accepts_only_exact_successful_pin_without_stderr(jq-1·7·0, 0, , fail) | 00:00:00.0006169 | Skipped: CARD-1210: jq image probes require native Linux (POSIX modes, symlinks and executable identity); Windows skips explicitly. |
| `antiphon-all-group-66.trx:1180` | `Antiphon.Tests.Infrastructure.JqRunnerImageContractTests.Version_row_accepts_only_exact_successful_pin_without_stderr` / Version_row_accepts_only_exact_successful_pin_without_stderr(jq-1·7·10, 0, , fail) | 00:00:00.0004998 | Skipped: CARD-1210: jq image probes require native Linux (POSIX modes, symlinks and executable identity); Windows skips explicitly. |
| `antiphon-all-group-66.trx:1187` | `Antiphon.Tests.Infrastructure.JqRunnerImageContractTests.Version_row_accepts_only_exact_successful_pin_without_stderr` / Version_row_accepts_only_exact_successful_pin_without_stderr(jq-1·8·0, 0, , fail) | 00:00:00.0005125 | Skipped: CARD-1210: jq image probes require native Linux (POSIX modes, symlinks and executable identity); Windows skips explicitly. |
| `antiphon-all-group-66.trx:1194` | `Antiphon.Tests.Infrastructure.JqRunnerImageContractTests.Version_row_accepts_only_exact_successful_pin_without_stderr` / Version_row_accepts_only_exact_successful_pin_without_stderr(jq-1·7·1-beta, 0, , fail) | 00:00:00.0004853 | Skipped: CARD-1210: jq image probes require native Linux (POSIX modes, symlinks and executable identity); Windows skips explicitly. |
| `antiphon-all-group-66.trx:1201` | `Antiphon.Tests.Infrastructure.JqRunnerImageContractTests.Version_row_accepts_only_exact_successful_pin_without_stderr` / Version_row_accepts_only_exact_successful_pin_without_stderr(jq-1x7x1, 0, , fail) | 00:00:00.0005362 | Skipped: CARD-1210: jq image probes require native Linux (POSIX modes, symlinks and executable identity); Windows skips explicitly. |
| `antiphon-all-group-66.trx:1208` | `Antiphon.Tests.Infrastructure.JqRunnerImageContractTests.Version_row_accepts_only_exact_successful_pin_without_stderr` / Version_row_accepts_only_exact_successful_pin_without_stderr(jq-1·7·1 extra, 0, , fail) | 00:00:00.0005476 | Skipped: CARD-1210: jq image probes require native Linux (POSIX modes, symlinks and executable identity); Windows skips explicitly. |
| `antiphon-all-group-66.trx:1215` | `Antiphon.Tests.Infrastructure.JqRunnerImageContractTests.Version_row_accepts_only_exact_successful_pin_without_stderr` / Version_row_accepts_only_exact_successful_pin_without_stderr(jq-1·7·1 / extra, 0, , fail) | 00:00:00.0005172 | Skipped: CARD-1210: jq image probes require native Linux (POSIX modes, symlinks and executable identity); Windows skips explicitly. |
| `antiphon-all-group-66.trx:1222` | `Antiphon.Tests.Infrastructure.JqRunnerImageContractTests.Version_row_accepts_only_exact_successful_pin_without_stderr` / Version_row_accepts_only_exact_successful_pin_without_stderr(, 0, , fail) | 00:00:00.0003866 | Skipped: CARD-1210: jq image probes require native Linux (POSIX modes, symlinks and executable identity); Windows skips explicitly. |
| `antiphon-all-group-66.trx:1229` | `Antiphon.Tests.Infrastructure.JqRunnerImageContractTests.Version_row_accepts_only_exact_successful_pin_without_stderr` / Version_row_accepts_only_exact_successful_pin_without_stderr(jq-1·7, 0, , fail) | 00:00:00.0005057 | Skipped: CARD-1210: jq image probes require native Linux (POSIX modes, symlinks and executable identity); Windows skips explicitly. |
| `antiphon-all-group-66.trx:1236` | `Antiphon.Tests.Infrastructure.JqRunnerImageContractTests.Version_row_accepts_only_exact_successful_pin_without_stderr` / Version_row_accepts_only_exact_successful_pin_without_stderr(jq-1·7·1, 1, , fail) | 00:00:00.0004700 | Skipped: CARD-1210: jq image probes require native Linux (POSIX modes, symlinks and executable identity); Windows skips explicitly. |
| `antiphon-all-group-66.trx:1243` | `Antiphon.Tests.Infrastructure.JqRunnerImageContractTests.C1054_Jq_row_rejects_home_shadow` / C1054_Jq_row_rejects_home_shadow | 00:00:00.0027630 | Skipped: CARD-1210: jq image probes require native Linux (POSIX modes, symlinks and executable identity); Windows skips explicitly. |
| `antiphon-all-group-66.trx:1250` | `Antiphon.Tests.Infrastructure.JqRunnerImageContractTests.C1054_Jq_row_accepts_canonical_file_and_alias` / C1054_Jq_row_accepts_canonical_file_and_alias | 00:00:00.0018362 | Skipped: CARD-1210: jq image probes require native Linux (POSIX modes, symlinks and executable identity); Windows skips explicitly. |
| `antiphon-all-group-66.trx:1257` | `Antiphon.Tests.Infrastructure.JqRunnerImageContractTests.C1054_Jq_row_rejects_canonical_leaf_symlink` / C1054_Jq_row_rejects_canonical_leaf_symlink | 00:00:00.0022990 | Skipped: CARD-1210: jq image probes require native Linux (POSIX modes, symlinks and executable identity); Windows skips explicitly. |
| `antiphon-all-group-66.trx:1264` | `Antiphon.Tests.Infrastructure.JqRunnerImageContractTests.C1054_Jq_row_records_found_and_resolved_paths` / C1054_Jq_row_records_found_and_resolved_paths | 00:00:00.0036917 | Skipped: CARD-1210: jq image probes require native Linux (POSIX modes, symlinks and executable identity); Windows skips explicitly. |
| `antiphon-all-group-66.trx:1271` | `Antiphon.Tests.Infrastructure.JqRunnerImageContractTests.Version_row_accepts_only_exact_successful_pin_without_stderr` / Version_row_accepts_only_exact_successful_pin_without_stderr(jq-1·7·1, 0, warning, fail) | 00:00:00.0005606 | Skipped: CARD-1210: jq image probes require native Linux (POSIX modes, symlinks and executable identity); Windows skips explicitly. |
| `antiphon-all-group-67.trx:56` | `Antiphon.Tests.Infrastructure.CodexRootExecContractTests.Entrypoint_heal_never_names_a_path_outside_tmp` / Entrypoint_heal_never_names_a_path_outside_tmp | 00:00:00.0816764 | Skipped: CARD-1210: Codex heal contracts require native Linux with unshare user+mount namespaces and a bind-mountable /state |
| `antiphon-all-group-67.trx:63` | `Antiphon.Tests.Infrastructure.CodexRootExecContractTests.Entrypoint_heal_chowns_the_tmp_directory_once` / Entrypoint_heal_chowns_the_tmp_directory_once | 00:00:00.0008913 | Skipped: CARD-1210: Codex heal contracts require native Linux with unshare user+mount namespaces and a bind-mountable /state |
| `antiphon-all-group-67.trx:417` | `Antiphon.Tests.Infrastructure.GitMinimumVersionGuardTests.Guard_refuses_git_below_the_landing_floor` / Guard_refuses_git_below_the_landing_floor | 00:00:00.0032414 | Skipped: CARD-1260: the Git floor guard runs under /bin/sh with GNU sort -V, as in the Debian images; Windows skips explicitly. |
| `antiphon-all-group-71.trx:301` | `Antiphon.Tests.Scripts.RollingVolumeRecycleScriptTests.C1008_Retired_absent_null_is_accepted` / C1008_Retired_absent_null_is_accepted | 00:00:00.0031346 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-group-71.trx:314` | `Antiphon.Tests.Scripts.RollingVolumeRecycleScriptTests.C1008_Refusal_receipts_do_not_leak_secrets` / C1008_Refusal_receipts_do_not_leak_secrets | 00:00:00.0019690 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-group-71.trx:321` | `Antiphon.Tests.Scripts.RollingVolumeRecycleScriptTests.C1008_Documentation_and_transport_pins_match` / C1008_Documentation_and_transport_pins_match | 00:00:00.0011782 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-group-71.trx:478` | `Antiphon.Tests.Scripts.RollingVolumeRecycleScriptTests.C1008_Option_manifest_is_strict` / C1008_Option_manifest_is_strict | 00:00:00.0013845 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-group-73.trx:281` | `Antiphon.Tests.Scripts.RetiredTempContainerScriptTests.C994_Host_bridge_transport_preserves_literal_context` / C994_Host_bridge_transport_preserves_literal_context | 00:00:00.0048301 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-group-73.trx:318` | `Antiphon.Tests.Scripts.RetiredTempContainerScriptTests.C994_Host_strict_volume_admission_remains_independent` / C994_Host_strict_volume_admission_remains_independent | 00:00:00.0047381 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-group-73.trx:361` | `Antiphon.Tests.Scripts.RetiredTempContainerScriptTests.C994_Host_resume_never_runs_container_cleanup` / C994_Host_resume_never_runs_container_cleanup | 00:00:00.0015790 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-group-74.trx:515` | `Antiphon.Tests.Scripts.RetiredTempContainerHostTests.C994_Removal_failure_and_crash_retry_preserve_progress` / C994_Removal_failure_and_crash_retry_preserve_progress | 00:00:00.0005931 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-group-74.trx:522` | `Antiphon.Tests.Scripts.RetiredTempContainerHostTests.C994_Receipt_write_and_copy_fail_closed` / C994_Receipt_write_and_copy_fail_closed | 00:00:00.0000910 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-group-74.trx:529` | `Antiphon.Tests.Scripts.RetiredTempContainerHostTests.C994_Absent_cleanup_is_idempotent` / C994_Absent_cleanup_is_idempotent | 00:00:00.0000639 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-group-74.trx:536` | `Antiphon.Tests.Scripts.RetiredTempContainerHostTests.C994_Exited_owned_containers_are_removed_without_volumes` / C994_Exited_owned_containers_are_removed_without_volumes | 00:00:00.0000565 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-group-74.trx:543` | `Antiphon.Tests.Scripts.RetiredTempContainerHostTests.C994_Host_context_is_strict` / C994_Host_context_is_strict | 00:00:00.0000584 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-group-74.trx:550` | `Antiphon.Tests.Scripts.RetiredTempContainerHostTests.C994_Host_preview_never_mutates` / C994_Host_preview_never_mutates | 00:00:00.0000841 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-group-74.trx:557` | `Antiphon.Tests.Scripts.RetiredTempContainerHostTests.C994_Image_receipt_lookup_is_bound` / C994_Image_receipt_lookup_is_bound | 00:00:00.0001093 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-group-74.trx:564` | `Antiphon.Tests.Scripts.RetiredTempContainerHostTests.C994_Audit_image_is_pinned_and_required` / C994_Audit_image_is_pinned_and_required | 00:00:00.0000886 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-group-74.trx:571` | `Antiphon.Tests.Scripts.RetiredTempContainerHostTests.C994_Cleanup_receipts_redact_unapproved_fields` / C994_Cleanup_receipts_redact_unapproved_fields | 00:00:00.0000879 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-group-74.trx:578` | `Antiphon.Tests.Scripts.RetiredTempContainerHostTests.C1105_Temp_cleanup_uses_previous_generation` / C1105_Temp_cleanup_uses_previous_generation | 00:00:00.0000541 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-group-74.trx:585` | `Antiphon.Tests.Scripts.RetiredTempContainerHostTests.C994_Final_absence_is_observed` / C994_Final_absence_is_observed | 00:00:00.0000542 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-group-74.trx:592` | `Antiphon.Tests.Scripts.RetiredTempContainerHostTests.C994_Each_removal_rechecks_live_proofs` / C994_Each_removal_rechecks_live_proofs | 00:00:00.0000424 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-group-74.trx:599` | `Antiphon.Tests.Scripts.RetiredTempContainerHostTests.C994_Fixture_custody_is_bounded` / C994_Fixture_custody_is_bounded | 00:00:00.0000837 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-group-74.trx:606` | `Antiphon.Tests.Scripts.RetiredTempContainerHostTests.C994_Whole_candidate_set_precedes_removal` / C994_Whole_candidate_set_precedes_removal | 00:00:00.0000912 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-group-74.trx:613` | `Antiphon.Tests.Scripts.RetiredTempContainerHostTests.C994_Rollout_lock_covers_final_census` / C994_Rollout_lock_covers_final_census | 00:00:00.0000860 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-group-74.trx:620` | `Antiphon.Tests.Scripts.RetiredTempContainerHostTests.C994_Host_main_admission_is_required` / C994_Host_main_admission_is_required | 00:00:00.0000500 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-group-74.trx:627` | `Antiphon.Tests.Scripts.RetiredTempContainerHostTests.C994_Host_task_and_land_census_is_complete` / C994_Host_task_and_land_census_is_complete | 00:00:00.0000656 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-group-74.trx:634` | `Antiphon.Tests.Scripts.RetiredTempContainerHostTests.C994_Census_and_inspect_fail_closed` / C994_Census_and_inspect_fail_closed | 00:00:00.0000973 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-group-74.trx:641` | `Antiphon.Tests.Scripts.RetiredTempContainerHostTests.C994_Host_retirement_proof_is_typed` / C994_Host_retirement_proof_is_typed | 00:00:00.0000618 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-group-74.trx:648` | `Antiphon.Tests.Scripts.RetiredTempContainerHostTests.C994_Production_mount_topology_is_proven` / C994_Production_mount_topology_is_proven | 00:00:00.0000416 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-group-74.trx:655` | `Antiphon.Tests.Scripts.RetiredTempContainerHostTests.C994_Container_service_and_state_are_restricted` / C994_Container_service_and_state_are_restricted | 00:00:00.0000649 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-group-74.trx:662` | `Antiphon.Tests.Scripts.ScriptHarnessLinuxOwnershipTests.Stop_ack_waits_for_group_exit` / Stop_ack_waits_for_group_exit | 00:00:00.0000812 | Skipped: Native Linux process-group ownership qualification requires Linux (procfs, setsid and POSIX signals). |
| `antiphon-all-group-74.trx:669` | `Antiphon.Tests.Scripts.ScriptHarnessLinuxOwnershipTests.Control_disconnect_kills_group` / Control_disconnect_kills_group | 00:00:00.0000862 | Skipped: Native Linux process-group ownership qualification requires Linux (procfs, setsid and POSIX signals). |
| `antiphon-all-group-74.trx:676` | `Antiphon.Tests.Scripts.ScriptHarnessLinuxOwnershipTests.Zombie_members_are_not_executing` / Zombie_members_are_not_executing | 00:00:00.0000923 | Skipped: Native Linux process-group ownership qualification requires Linux (procfs, setsid and POSIX signals). |
| `antiphon-all-group-74.trx:683` | `Antiphon.Tests.Scripts.ScriptHarnessLinuxOwnershipTests.Unreadable_group_membership_is_not_empty` / Unreadable_group_membership_is_not_empty | 00:00:00.0000583 | Skipped: Native Linux process-group ownership qualification requires Linux (procfs, setsid and POSIX signals). |
| `antiphon-all-group-74.trx:690` | `Antiphon.Tests.Scripts.ScriptHarnessLinuxOwnershipTests.Unresponsive_supervisor_stop_is_bounded` / Unresponsive_supervisor_stop_is_bounded | 00:00:00.0000446 | Skipped: Native Linux process-group ownership qualification requires Linux (procfs, setsid and POSIX signals). |
| `antiphon-all-group-74.trx:697` | `Antiphon.Tests.Scripts.ScriptHarnessLinuxOwnershipTests.Lost_identity_never_signals_saved_group` / Lost_identity_never_signals_saved_group | 00:00:00.0000434 | Skipped: Native Linux process-group ownership qualification requires Linux (procfs, setsid and POSIX signals). |
| `antiphon-all-group-74.trx:704` | `Antiphon.Tests.Scripts.ScriptHarnessLinuxOwnershipTests.Lost_supervisor_reports_cleanup_failure` / Lost_supervisor_reports_cleanup_failure | 00:00:00.0000511 | Skipped: Native Linux process-group ownership qualification requires Linux (procfs, setsid and POSIX signals). |
| `antiphon-all-group-74.trx:711` | `Antiphon.Tests.Scripts.ScriptHarnessLinuxOwnershipTests.Failsafe_kills_group_with_connected_idle_controller` / Failsafe_kills_group_with_connected_idle_controller | 00:00:00.0000442 | Skipped: Native Linux process-group ownership qualification requires Linux (procfs, setsid and POSIX signals). |
| `antiphon-all-group-74.trx:718` | `Antiphon.Tests.Scripts.ScriptHarnessLinuxOwnershipTests.Supervisor_closes_its_output_writers` / Supervisor_closes_its_output_writers | 00:00:00.0000425 | Skipped: Native Linux process-group ownership qualification requires Linux (procfs, setsid and POSIX signals). |
| `antiphon-all-group-74.trx:725` | `Antiphon.Tests.Scripts.ScriptHarnessLinuxOwnershipTests.Invalid_group_handshake_never_starts_harness` / Invalid_group_handshake_never_starts_harness | 00:00:00.0000661 | Skipped: Native Linux process-group ownership qualification requires Linux (procfs, setsid and POSIX signals). |
| `antiphon-all-group-74.trx:732` | `Antiphon.Tests.Scripts.ScriptHarnessLinuxOwnershipTests.Supervisor_pins_group_after_root_exit` / Supervisor_pins_group_after_root_exit | 00:00:00.0000574 | Skipped: Native Linux process-group ownership qualification requires Linux (procfs, setsid and POSIX signals). |
| `antiphon-all-group-74.trx:739` | `Antiphon.Tests.Scripts.ScriptHarnessLinuxOwnershipTests.Stop_requires_matching_nonce` / Stop_requires_matching_nonce | 00:00:00.0000436 | Skipped: Native Linux process-group ownership qualification requires Linux (procfs, setsid and POSIX signals). |
| `antiphon-all-group-74.trx:746` | `Antiphon.Tests.Scripts.ScriptHarnessLinuxOwnershipTests.Root_exit_receipt_requires_matching_identity` / Root_exit_receipt_requires_matching_identity | 00:00:00.0000415 | Skipped: Native Linux process-group ownership qualification requires Linux (procfs, setsid and POSIX signals). |
| `antiphon-all-group-74.trx:753` | `Antiphon.Tests.Scripts.ScriptHarnessLinuxOwnershipTests.Root_exit_receipt_requires_matching_nonce` / Root_exit_receipt_requires_matching_nonce | 00:00:00.0000592 | Skipped: Native Linux process-group ownership qualification requires Linux (procfs, setsid and POSIX signals). |
| `antiphon-all-group-74.trx:760` | `Antiphon.Tests.Scripts.ScriptHarnessLinuxOwnershipTests.Malformed_control_frames_are_refused` / Malformed_control_frames_are_refused | 00:00:00.0000751 | Skipped: Native Linux process-group ownership qualification requires Linux (procfs, setsid and POSIX signals). |
| `antiphon-all-group-74.trx:767` | `Antiphon.Tests.Scripts.ScriptHarnessLinuxOwnershipTests.Missing_start_authorization_never_launches_child` / Missing_start_authorization_never_launches_child | 00:00:00.0000571 | Skipped: Native Linux process-group ownership qualification requires Linux (procfs, setsid and POSIX signals). |
| `antiphon-all-group-74.trx:774` | `Antiphon.Tests.Scripts.ScriptHarnessLinuxOwnershipTests.Start_authorization_requires_matching_nonce` / Start_authorization_requires_matching_nonce | 00:00:00.0000437 | Skipped: Native Linux process-group ownership qualification requires Linux (procfs, setsid and POSIX signals). |
| `antiphon-all-group-74.trx:781` | `Antiphon.Tests.Scripts.ScriptHarnessLinuxOwnershipTests.Setsid_failure_refuses_child_launch` / Setsid_failure_refuses_child_launch | 00:00:00.0000496 | Skipped: Native Linux process-group ownership qualification requires Linux (procfs, setsid and POSIX signals). |
| `antiphon-all-group-74.trx:788` | `Antiphon.Tests.Scripts.ScriptHarnessLinuxOwnershipTests.Failsafe_includes_time_waiting_for_start` / Failsafe_includes_time_waiting_for_start | 00:00:00.0000493 | Skipped: Native Linux process-group ownership qualification requires Linux (procfs, setsid and POSIX signals). |
| `antiphon-all-group-74.trx:795` | `Antiphon.Tests.Scripts.ScriptHarnessLinuxOwnershipTests.Group_stop_leaves_unrelated_process_alive` / Group_stop_leaves_unrelated_process_alive | 00:00:00.0000491 | Skipped: Native Linux process-group ownership qualification requires Linux (procfs, setsid and POSIX signals). |
| `antiphon-all-group-74.trx:802` | `Antiphon.Tests.Scripts.ScriptHarnessLinuxOwnershipTests.Root_stdin_is_separate_from_control_pipe` / Root_stdin_is_separate_from_control_pipe | 00:00:00.0000484 | Skipped: Native Linux process-group ownership qualification requires Linux (procfs, setsid and POSIX signals). |
| `antiphon-all-group-75.trx:41` | `Antiphon.Tests.Scripts.BuildSlotScriptTests.C800_WrapperInterruptKillsChildAndReleasesLease` / C800_WrapperInterruptKillsChildAndReleasesLease | 00:00:00.0464176 | Skipped: Linux SIGINT behavior only |
| `antiphon-all-group-75.trx:1217` | `Antiphon.Tests.Scripts.HostJqPrerequisiteScriptTests.C1025_Transport_preserves_secret_custody` / C1025_Transport_preserves_secret_custody | 00:00:00.0034059 | Skipped: CARD-1210: C1025/C1058 host jq contracts require native Linux (bash, flock, /proc/self/fd, GNU coreutils); Windows skips explicitly. |
| `antiphon-all-group-75.trx:1224` | `Antiphon.Tests.Scripts.HostJqPrerequisiteScriptTests.C1025_Final_recycle_guard_remains` / C1025_Final_recycle_guard_remains | 00:00:00.0017719 | Skipped: CARD-1210: C1025/C1058 host jq contracts require native Linux (bash, flock, /proc/self/fd, GNU coreutils); Windows skips explicitly. |
| `antiphon-all-group-75.trx:1231` | `Antiphon.Tests.Scripts.HostJqPrerequisiteScriptTests.C1058_Provision_removes_only_owned_stage_link` / C1058_Provision_removes_only_owned_stage_link | 00:00:00.0029528 | Skipped: CARD-1210: C1025/C1058 host jq contracts require native Linux (bash, flock, /proc/self/fd, GNU coreutils); Windows skips explicitly. |
| `antiphon-all-group-75.trx:1238` | `Antiphon.Tests.Scripts.HostJqPrerequisiteScriptTests.C1058_Check_rejects_canonical_hardlink` / C1058_Check_rejects_canonical_hardlink | 00:00:00.0028255 | Skipped: CARD-1210: C1025/C1058 host jq contracts require native Linux (bash, flock, /proc/self/fd, GNU coreutils); Windows skips explicitly. |
| `antiphon-all-group-75.trx:1245` | `Antiphon.Tests.Scripts.HostJqPrerequisiteScriptTests.C1058_Observation_failures_are_invalid` / C1058_Observation_failures_are_invalid | 00:00:00.0021179 | Skipped: CARD-1210: C1025/C1058 host jq contracts require native Linux (bash, flock, /proc/self/fd, GNU coreutils); Windows skips explicitly. |
| `antiphon-all-group-75.trx:1252` | `Antiphon.Tests.Scripts.HostJqPrerequisiteScriptTests.C1058_Metadata_syntax_refuses` / C1058_Metadata_syntax_refuses | 00:00:00.0018323 | Skipped: CARD-1210: C1025/C1058 host jq contracts require native Linux (bash, flock, /proc/self/fd, GNU coreutils); Windows skips explicitly. |
| `antiphon-all-group-75.trx:1259` | `Antiphon.Tests.Scripts.HostJqPrerequisiteScriptTests.C1058_Proof_is_buffered_until_final_admission` / C1058_Proof_is_buffered_until_final_admission | 00:00:00.0012448 | Skipped: CARD-1210: C1025/C1058 host jq contracts require native Linux (bash, flock, /proc/self/fd, GNU coreutils); Windows skips explicitly. |
| `antiphon-all-group-75.trx:1266` | `Antiphon.Tests.Scripts.HostJqPrerequisiteScriptTests.C1058_Final_link_checks_are_independent` / C1058_Final_link_checks_are_independent | 00:00:00.0011581 | Skipped: CARD-1210: C1025/C1058 host jq contracts require native Linux (bash, flock, /proc/self/fd, GNU coreutils); Windows skips explicitly. |
| `antiphon-all-group-75.trx:1273` | `Antiphon.Tests.Scripts.HostJqPrerequisiteScriptTests.C1058_Identity_comparisons_include_device` / C1058_Identity_comparisons_include_device | 00:00:00.0018556 | Skipped: CARD-1210: C1025/C1058 host jq contracts require native Linux (bash, flock, /proc/self/fd, GNU coreutils); Windows skips explicitly. |
| `antiphon-all-group-75.trx:1280` | `Antiphon.Tests.Scripts.HostJqPrerequisiteScriptTests.C1058_Staged_unlink_failure_refuses` / C1058_Staged_unlink_failure_refuses | 00:00:00.0016845 | Skipped: CARD-1210: C1025/C1058 host jq contracts require native Linux (bash, flock, /proc/self/fd, GNU coreutils); Windows skips explicitly. |
| `antiphon-all-group-75.trx:1287` | `Antiphon.Tests.Scripts.HostJqPrerequisiteScriptTests.C1058_Pre_execution_link_checks_are_independent` / C1058_Pre_execution_link_checks_are_independent | 00:00:00.0011356 | Skipped: CARD-1210: C1025/C1058 host jq contracts require native Linux (bash, flock, /proc/self/fd, GNU coreutils); Windows skips explicitly. |
| `antiphon-all-group-75.trx:1294` | `Antiphon.Tests.Scripts.HostJqPrerequisiteScriptTests.C1058_Receipt_formatter_uses_open_descriptor` / C1058_Receipt_formatter_uses_open_descriptor | 00:00:00.0007112 | Skipped: CARD-1210: C1025/C1058 host jq contracts require native Linux (bash, flock, /proc/self/fd, GNU coreutils); Windows skips explicitly. |
| `antiphon-all-group-75.trx:1301` | `Antiphon.Tests.Scripts.HostJqPrerequisiteScriptTests.C1058_False_probe_uses_open_descriptor` / C1058_False_probe_uses_open_descriptor | 00:00:00.0003757 | Skipped: CARD-1210: C1025/C1058 host jq contracts require native Linux (bash, flock, /proc/self/fd, GNU coreutils); Windows skips explicitly. |
| `antiphon-all-group-75.trx:1308` | `Antiphon.Tests.Scripts.HostJqPrerequisiteScriptTests.C1058_True_probe_uses_open_descriptor` / C1058_True_probe_uses_open_descriptor | 00:00:00.0002892 | Skipped: CARD-1210: C1025/C1058 host jq contracts require native Linux (bash, flock, /proc/self/fd, GNU coreutils); Windows skips explicitly. |
| `antiphon-all-group-75.trx:1315` | `Antiphon.Tests.Scripts.HostJqPrerequisiteScriptTests.C1058_Version_probe_uses_open_descriptor` / C1058_Version_probe_uses_open_descriptor | 00:00:00.0004239 | Skipped: CARD-1210: C1025/C1058 host jq contracts require native Linux (bash, flock, /proc/self/fd, GNU coreutils); Windows skips explicitly. |
| `antiphon-all-group-75.trx:1322` | `Antiphon.Tests.Scripts.HostJqPrerequisiteScriptTests.C1058_Installed_pin_is_checked_before_execution` / C1058_Installed_pin_is_checked_before_execution | 00:00:00.0017914 | Skipped: CARD-1210: C1025/C1058 host jq contracts require native Linux (bash, flock, /proc/self/fd, GNU coreutils); Windows skips explicitly. |
| `antiphon-all-group-75.trx:1329` | `Antiphon.Tests.Scripts.HostJqPrerequisiteScriptTests.C1058_Digest_is_bound_to_open_descriptor` / C1058_Digest_is_bound_to_open_descriptor | 00:00:00.0013653 | Skipped: CARD-1210: C1025/C1058 host jq contracts require native Linux (bash, flock, /proc/self/fd, GNU coreutils); Windows skips explicitly. |
| `antiphon-all-group-75.trx:1336` | `Antiphon.Tests.Scripts.HostJqPrerequisiteScriptTests.C1058_Open_descriptor_must_match_canonical_leaf` / C1058_Open_descriptor_must_match_canonical_leaf | 00:00:00.0012918 | Skipped: CARD-1210: C1025/C1058 host jq contracts require native Linux (bash, flock, /proc/self/fd, GNU coreutils); Windows skips explicitly. |
| `antiphon-all-group-75.trx:1343` | `Antiphon.Tests.Scripts.HostJqPrerequisiteScriptTests.C1058_Changed_leaf_cannot_emit_success_proof` / C1058_Changed_leaf_cannot_emit_success_proof | 00:00:00.0022007 | Skipped: CARD-1210: C1025/C1058 host jq contracts require native Linux (bash, flock, /proc/self/fd, GNU coreutils); Windows skips explicitly. |
| `antiphon-all-group-75.trx:1350` | `Antiphon.Tests.Scripts.HostJqPrerequisiteScriptTests.C1025_Check_qualifies_deployment_shell` / C1025_Check_qualifies_deployment_shell | 00:00:00.0053502 | Skipped: CARD-1210: C1025/C1058 host jq contracts require native Linux (bash, flock, /proc/self/fd, GNU coreutils); Windows skips explicitly. |
| `antiphon-all-group-75.trx:1357` | `Antiphon.Tests.Scripts.HostJqPrerequisiteScriptTests.C1058_Final_permission_checks_are_independent` / C1058_Final_permission_checks_are_independent | 00:00:00.0016778 | Skipped: CARD-1210: C1025/C1058 host jq contracts require native Linux (bash, flock, /proc/self/fd, GNU coreutils); Windows skips explicitly. |
| `antiphon-all-group-75.trx:1364` | `Antiphon.Tests.Scripts.HostJqPrerequisiteScriptTests.C1025_Receipt_rejects_canonical_lookup_with_unapproved_target` / C1025_Receipt_rejects_canonical_lookup_with_unapproved_target | 00:00:00.0014083 | Skipped: CARD-1210: C1025/C1058 host jq contracts require native Linux (bash, flock, /proc/self/fd, GNU coreutils); Windows skips explicitly. |
| `antiphon-all-group-75.trx:1371` | `Antiphon.Tests.Scripts.HostJqPrerequisiteScriptTests.C1025_Preflight_is_fresh_for_each_entry` / C1025_Preflight_is_fresh_for_each_entry | 00:00:00.0024927 | Skipped: CARD-1210: C1025/C1058 host jq contracts require native Linux (bash, flock, /proc/self/fd, GNU coreutils); Windows skips explicitly. |
| `antiphon-all-group-75.trx:1378` | `Antiphon.Tests.Scripts.HostJqPrerequisiteScriptTests.C1025_Check_rejects_canonical_leaf_symlink` / C1025_Check_rejects_canonical_leaf_symlink | 00:00:00.0018224 | Skipped: CARD-1210: C1025/C1058 host jq contracts require native Linux (bash, flock, /proc/self/fd, GNU coreutils); Windows skips explicitly. |
| `antiphon-all-group-75.trx:1385` | `Antiphon.Tests.Scripts.HostJqPrerequisiteScriptTests.C1025_Explicit_tool_phases_never_deploy` / C1025_Explicit_tool_phases_never_deploy | 00:00:00.0014507 | Skipped: CARD-1210: C1025/C1058 host jq contracts require native Linux (bash, flock, /proc/self/fd, GNU coreutils); Windows skips explicitly. |
| `antiphon-all-group-75.trx:1392` | `Antiphon.Tests.Scripts.HostJqPrerequisiteScriptTests.C1025_Receipt_persistence_is_required` / C1025_Receipt_persistence_is_required | 00:00:00.0021017 | Skipped: CARD-1210: C1025/C1058 host jq contracts require native Linux (bash, flock, /proc/self/fd, GNU coreutils); Windows skips explicitly. |
| `antiphon-all-group-75.trx:1399` | `Antiphon.Tests.Scripts.HostJqPrerequisiteScriptTests.C1025_Receipt_requires_complete_current_proof` / C1025_Receipt_requires_complete_current_proof | 00:00:00.0052693 | Skipped: CARD-1210: C1025/C1058 host jq contracts require native Linux (bash, flock, /proc/self/fd, GNU coreutils); Windows skips explicitly. |
| `antiphon-all-group-75.trx:1406` | `Antiphon.Tests.Scripts.HostJqPrerequisiteScriptTests.C1025_Transport_is_bounded_and_reaped` / C1025_Transport_is_bounded_and_reaped | 00:00:00.0016740 | Skipped: CARD-1210: C1025/C1058 host jq contracts require native Linux (bash, flock, /proc/self/fd, GNU coreutils); Windows skips explicitly. |
| `antiphon-all-group-75.trx:1413` | `Antiphon.Tests.Scripts.HostJqPrerequisiteScriptTests.C1025_Provision_requires_canonical_source` / C1025_Provision_requires_canonical_source | 00:00:00.0015854 | Skipped: CARD-1210: C1025/C1058 host jq contracts require native Linux (bash, flock, /proc/self/fd, GNU coreutils); Windows skips explicitly. |
| `antiphon-all-group-75.trx:1420` | `Antiphon.Tests.Scripts.HostJqPrerequisiteScriptTests.C1025_Provision_requalifies_published_jq` / C1025_Provision_requalifies_published_jq | 00:00:00.0014587 | Skipped: CARD-1210: C1025/C1058 host jq contracts require native Linux (bash, flock, /proc/self/fd, GNU coreutils); Windows skips explicitly. |
| `antiphon-all-group-75.trx:1427` | `Antiphon.Tests.Scripts.HostJqPrerequisiteScriptTests.C1025_Preflight_precedes_every_phase` / C1025_Preflight_precedes_every_phase | 00:00:00.0021897 | Skipped: CARD-1210: C1025/C1058 host jq contracts require native Linux (bash, flock, /proc/self/fd, GNU coreutils); Windows skips explicitly. |
| `antiphon-all-group-75.trx:1440` | `Antiphon.Tests.Scripts.HostJqPrerequisiteScriptTests.C1025_Provision_publishes_complete_no_clobber` / C1025_Provision_publishes_complete_no_clobber | 00:00:00.0026999 | Skipped: CARD-1210: C1025/C1058 host jq contracts require native Linux (bash, flock, /proc/self/fd, GNU coreutils); Windows skips explicitly. |
| `antiphon-all-group-75.trx:1447` | `Antiphon.Tests.Scripts.HostJqPrerequisiteScriptTests.C1025_Provision_verifies_download_before_use` / C1025_Provision_verifies_download_before_use | 00:00:00.0020902 | Skipped: CARD-1210: C1025/C1058 host jq contracts require native Linux (bash, flock, /proc/self/fd, GNU coreutils); Windows skips explicitly. |
| `antiphon-all-group-75.trx:1496` | `Antiphon.Tests.Scripts.HostJqPrerequisiteScriptTests.C1025_Provision_serializes_and_rechecks` / C1025_Provision_serializes_and_rechecks | 00:00:00.0026543 | Skipped: CARD-1210: C1025/C1058 host jq contracts require native Linux (bash, flock, /proc/self/fd, GNU coreutils); Windows skips explicitly. |
| `antiphon-all-group-75.trx:1557` | `Antiphon.Tests.Scripts.HostJqPrerequisiteScriptTests.C1025_Provision_refuses_unsafe_destination` / C1025_Provision_refuses_unsafe_destination | 00:00:00.0019318 | Skipped: CARD-1210: C1025/C1058 host jq contracts require native Linux (bash, flock, /proc/self/fd, GNU coreutils); Windows skips explicitly. |
| `antiphon-all-group-75.trx:1594` | `Antiphon.Tests.Scripts.HostJqPrerequisiteScriptTests.C1025_Provision_admits_host_prerequisites` / C1025_Provision_admits_host_prerequisites | 00:00:00.0025925 | Skipped: CARD-1210: C1025/C1058 host jq contracts require native Linux (bash, flock, /proc/self/fd, GNU coreutils); Windows skips explicitly. |
| `antiphon-all-group-75.trx:1607` | `Antiphon.Tests.Scripts.HostJqPrerequisiteScriptTests.C1025_Provision_requires_missing_jq` / C1025_Provision_requires_missing_jq | 00:00:00.0027887 | Skipped: CARD-1210: C1025/C1058 host jq contracts require native Linux (bash, flock, /proc/self/fd, GNU coreutils); Windows skips explicitly. |
| `antiphon-all-group-75.trx:1614` | `Antiphon.Tests.Scripts.HostJqPrerequisiteScriptTests.C1025_Check_has_no_install_effects` / C1025_Check_has_no_install_effects | 00:00:00.0014261 | Skipped: CARD-1210: C1025/C1058 host jq contracts require native Linux (bash, flock, /proc/self/fd, GNU coreutils); Windows skips explicitly. |
| `antiphon-all-group-75.trx:1621` | `Antiphon.Tests.Scripts.HostJqPrerequisiteScriptTests.C1025_Provision_cleans_only_owned_staging` / C1025_Provision_cleans_only_owned_staging | 00:00:00.0015738 | Skipped: CARD-1210: C1025/C1058 host jq contracts require native Linux (bash, flock, /proc/self/fd, GNU coreutils); Windows skips explicitly. |
| `antiphon-all-group-76.trx:160` | `Antiphon.Tests.Scripts.C1364BoundLandWaitProofTests.C1364_Host_recycle_post_removal_reports_waited` / C1364_Host_recycle_post_removal_reports_waited | 00:00:00.0025054 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-group-76.trx:191` | `Antiphon.Tests.Scripts.C1364BoundLandWaitProofTests.C1364_Host_recycle_admission_reports_waited` / C1364_Host_recycle_admission_reports_waited | 00:00:00.0020074 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-group-76.trx:210` | `Antiphon.Tests.Scripts.C1364BoundLandWaitProofTests.C1364_Host_cleanup_reports_waited` / C1364_Host_cleanup_reports_waited | 00:00:00.0022070 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-group-76.trx:1435` | `Antiphon.Tests.Scripts.RollingProductionMountTests.C1105_Absolute_secret_target_keeps_the_base_rule` / C1105_Absolute_secret_target_keeps_the_base_rule | 00:00:00.0000883 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-group-76.trx:1442` | `Antiphon.Tests.Scripts.RollingProductionMountTests.C1105_Relative_secret_target_is_refused` / C1105_Relative_secret_target_is_refused | 00:00:00.0000597 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-group-76.trx:1449` | `Antiphon.Tests.Scripts.RollingProductionMountTests.C1105_V218_previous_model_accepts_the_host_shape` / C1105_V218_previous_model_accepts_the_host_shape | 00:00:00.0000778 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-group-76.trx:1456` | `Antiphon.Tests.Scripts.RollingProductionMountTests.C1105_V218_compose_model_accepts_the_host_shape` / C1105_V218_compose_model_accepts_the_host_shape | 00:00:00.0000758 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-group-76.trx:1463` | `Antiphon.Tests.Scripts.RollingProductionMountTests.C1105_Previous_generation_roster_is_derived_from_its_compose` / C1105_Previous_generation_roster_is_derived_from_its_compose | 00:00:00.0001221 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-group-76.trx:1470` | `Antiphon.Tests.Scripts.RollingProductionMountTests.C994_Tmpfs_configuration_survives_exit` / C994_Tmpfs_configuration_survives_exit | 00:00:00.0000633 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-group-76.trx:1477` | `Antiphon.Tests.Scripts.RollingProductionMountTests.C1105_Malformed_secret_target_is_refused` / C1105_Malformed_secret_target_is_refused | 00:00:00.0000382 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-group-76.trx:1484` | `Antiphon.Tests.Scripts.RollingProductionMountTests.C994_Secrets_match_read_only_files` / C994_Secrets_match_read_only_files | 00:00:00.0000375 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-group-76.trx:1491` | `Antiphon.Tests.Scripts.RollingProductionMountTests.C994_Named_volume_identity_and_topology_are_exact` / C994_Named_volume_identity_and_topology_are_exact | 00:00:00.0000794 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-group-76.trx:1498` | `Antiphon.Tests.Scripts.RollingProductionMountTests.C994_Compose_model_preserves_service_specific_mounts` / C994_Compose_model_preserves_service_specific_mounts | 00:00:00.0000706 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-group-76.trx:1505` | `Antiphon.Tests.Scripts.RollingProductionMountTests.C994_Bind_sources_are_canonical_and_typed` / C994_Bind_sources_are_canonical_and_typed | 00:00:00.0000659 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-group-76.trx:1512` | `Antiphon.Tests.Scripts.RollingProductionMountTests.C1105_Relative_secret_target_equal_to_its_name_is_accepted` / C1105_Relative_secret_target_equal_to_its_name_is_accepted | 00:00:00.0000390 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-group-77.trx:390` | `Antiphon.Tests.Scripts.RefreshGithubTokenRemoteCommandTests.Verify_failure_surfaces_remote_stderr` / Verify_failure_surfaces_remote_stderr | 00:00:00.0013988 | Skipped: CARD-1245: the fake bw/ssh are POSIX sh executables; Windows skips explicitly. |
| `antiphon-all-group-77.trx:397` | `Antiphon.Tests.Scripts.RefreshGithubTokenRemoteCommandTests.Upload_failure_surfaces_remote_stderr_without_the_token` / Upload_failure_surfaces_remote_stderr_without_the_token | 00:00:00.0006965 | Skipped: CARD-1245: the fake bw/ssh are POSIX sh executables; Windows skips explicitly. |
| `antiphon-all-group-77.trx:404` | `Antiphon.Tests.Scripts.RefreshGithubTokenRemoteCommandTests.Crlf_checkout_sends_carriage_return_free_remote_commands` / Crlf_checkout_sends_carriage_return_free_remote_commands | 00:00:00.0010297 | Skipped: CARD-1245: the fake bw/ssh are POSIX sh executables; Windows skips explicitly. |
| `antiphon-all-group-77.trx:674` | `Antiphon.Tests.Scripts.RetiredTempContainerDockerTests.C994_Real_retired_temp_lifecycle` / C994_Real_retired_temp_lifecycle | 00:00:00.0030449 | Skipped: C994/C1008 real Docker lifecycle qualification requires native Linux inside its owned daemon (POSIX paths and process groups). |
| `antiphon-all-group-77.trx:681` | `Antiphon.Tests.Scripts.RollingVolumeRecycleDockerTests.C1008_Real_docker_comparison` / C1008_Real_docker_comparison | 00:00:00.0041377 | Skipped: C994/C1008 real Docker lifecycle qualification requires native Linux inside its owned daemon (POSIX paths and process groups). |
| `antiphon-all-group-78.trx:116` | `Antiphon.Tests.Scripts.RollingVolumeRecycleCensusChurnTests.C1099_Fixture_census_skips_a_vanished_foreign_container` / C1099_Fixture_census_skips_a_vanished_foreign_container | 00:00:00.0049047 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-group-78.trx:171` | `Antiphon.Tests.Scripts.NightlyProbeSupervisorTests.C1375_DeadlineKillsAndJoinsTree` / C1375_DeadlineKillsAndJoinsTree | 00:00:00.0014364 | Skipped: CARD-1375 native expiry qualification requires Linux. |
| `antiphon-all-split-18.trx:8` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105_Git_audit_hidden_locations` / C1105_Git_audit_hidden_locations(dot-git-content) | 00:00:00.0596549 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-18.trx:17` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105_Git_audit_hidden_locations` / C1105_Git_audit_hidden_locations(head) | 00:00:00.0010110 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-18.trx:24` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105_Git_audit_hidden_locations` / C1105_Git_audit_hidden_locations(loose-symlink-dangling) | 00:00:00.0020586 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-18.trx:31` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105_Git_audit_hidden_locations` / C1105_Git_audit_hidden_locations(tmp-linked-untracked) | 00:00:00.0010719 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-18.trx:38` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105_Git_audit_hidden_locations` / C1105_Git_audit_hidden_locations(bare-dot-git) | 00:00:00.0006902 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-18.trx:45` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105_Git_audit_hidden_locations` / C1105_Git_audit_hidden_locations(linked-core-worktree) | 00:00:00.0007125 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-18.trx:52` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105_Git_audit_hidden_locations` / C1105_Git_audit_hidden_locations(tmp-linked-modified) | 00:00:00.0007356 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-18.trx:59` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105_Git_audit_hidden_locations` / C1105_Git_audit_hidden_locations(tmp-linked-clean) | 00:00:00.0006294 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-18.trx:66` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105_Git_audit_hidden_locations` / C1105_Git_audit_hidden_locations(untracked-symlink) | 00:00:00.0004890 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-18.trx:73` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105_Git_audit_hidden_locations` / C1105_Git_audit_hidden_locations(metadata-symlink-outside) | 00:00:00.0005756 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-18.trx:80` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105_Git_audit_hidden_locations` / C1105_Git_audit_hidden_locations(orphan-symlink-ignored) | 00:00:00.0008550 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-18.trx:87` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105_Git_audit_hidden_locations` / C1105_Git_audit_hidden_locations(ignored-symlink-dangling) | 00:00:00.0008460 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-18.trx:94` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105_Git_audit_hidden_locations` / C1105_Git_audit_hidden_locations(ignored-symlink-outside) | 00:00:00.0006235 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-18.trx:101` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105_Git_audit_hidden_locations` / C1105_Git_audit_hidden_locations(damaged-git-dir-ignored) | 00:00:00.0007091 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-18.trx:108` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105_Git_audit_hidden_locations` / C1105_Git_audit_hidden_locations(bare-reflog) | 00:00:00.0012404 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-18.trx:115` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105_Git_audit_hidden_locations` / C1105_Git_audit_hidden_locations(ignored-head-equality) | 00:00:00.0005333 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-18.trx:122` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105_Git_audit_hidden_locations` / C1105_Git_audit_hidden_locations(tag-published-off-branch) | 00:00:00.0004895 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-18.trx:129` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105_Git_audit_hidden_locations` / C1105_Git_audit_hidden_locations(blob-ref) | 00:00:00.0004505 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-18.trx:136` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105_Git_audit_hidden_locations` / C1105_Git_audit_hidden_locations(tree-ref-unpublished) | 00:00:00.0004349 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-18.trx:143` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105_Git_audit_hidden_locations` / C1105_Git_audit_hidden_locations(tree-ref-published) | 00:00:00.0004822 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-18.trx:150` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105_Git_audit_hidden_locations` / C1105_Git_audit_hidden_locations(tracked-symlink-outside) | 00:00:00.0005511 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-18.trx:157` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105_Git_audit_hidden_locations` / C1105_Git_audit_hidden_locations(tag-tree) | 00:00:00.0006807 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-18.trx:164` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105_Git_audit_hidden_locations` / C1105_Git_audit_hidden_locations(tag-lightweight) | 00:00:00.0006535 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-18.trx:171` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105_Git_audit_hidden_locations` / C1105_Git_audit_hidden_locations(tag-of-tag-published) | 00:00:00.0005343 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-18.trx:178` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105_Git_audit_hidden_locations` / C1105_Git_audit_hidden_locations(tag-of-tag) | 00:00:00.0004515 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-18.trx:185` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105_Git_audit_hidden_locations` / C1105_Git_audit_hidden_locations(tag-annotated-published) | 00:00:00.0004766 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-18.trx:192` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105_Git_audit_hidden_locations` / C1105_Git_audit_hidden_locations(tag-annotated) | 00:00:00.0004150 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-18.trx:199` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105_Git_audit_hidden_locations` / C1105_Git_audit_hidden_locations(tag-annotated-packed) | 00:00:00.0004442 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-18.trx:206` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105_Git_audit_hidden_locations` / C1105_Git_audit_hidden_locations(orphan-missing-ignored) | 00:00:00.0004269 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-18.trx:213` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105_Git_audit_hidden_locations` / C1105_Git_audit_hidden_locations(orphan-elsewhere-modified) | 00:00:00.0004319 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-18.trx:220` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105_Git_audit_hidden_locations` / C1105_Git_audit_hidden_locations(orphan-missing-clean) | 00:00:00.0006620 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-18.trx:227` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105_Git_audit_hidden_locations` / C1105_Git_audit_hidden_locations(tmp-linked-orphan-untracked) | 00:00:00.0009518 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-18.trx:234` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105_Git_audit_hidden_locations` / C1105_Git_audit_hidden_locations(tmp-linked-absent-clean) | 00:00:00.0005289 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-18.trx:241` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105_Git_audit_hidden_locations` / C1105_Git_audit_hidden_locations(tmp-unmounted) | 00:00:00.0004734 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-18.trx:248` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105_Git_audit_hidden_locations` / C1105_Git_audit_hidden_locations(origin-gone-counts) | 00:00:00.0098681 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-18.trx:255` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105_Git_audit_hidden_locations` / C1105_Git_audit_hidden_locations(origin-fetched-unpublished) | 00:00:00.0007385 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-18.trx:262` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105_Git_audit_hidden_locations` / C1105_Git_audit_hidden_locations(origin-stale-master-readonly) | 00:00:00.0008076 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-18.trx:269` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105_Git_audit_hidden_locations` / C1105_Git_audit_hidden_locations(origin-stale-master-fetch-timeout) | 00:00:00.0008726 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-18.trx:276` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105_Git_audit_hidden_locations` / C1105_Git_audit_hidden_locations(origin-stale-master-fetch-failed) | 00:00:00.0007498 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-18.trx:283` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105_Git_audit_hidden_locations` / C1105_Git_audit_hidden_locations(origin-stale-master) | 00:00:00.0008961 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-18.trx:290` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105_Git_audit_hidden_locations` / C1105_Git_audit_hidden_locations(tmp-space-path-untracked) | 00:00:00.0006522 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-18.trx:297` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105_Git_audit_hidden_locations` / C1105_Git_audit_hidden_locations(tmp-space-path) | 00:00:00.0005968 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-18.trx:304` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105_Git_audit_hidden_locations` / C1105_Git_audit_hidden_locations(tmp-link-clean) | 00:00:00.0005719 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-18.trx:311` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105_Git_audit_hidden_locations` / C1105_Git_audit_hidden_locations(linked-link-target-newline-orphan) | 00:00:00.0005501 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-18.trx:318` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105_Git_audit_hidden_locations` / C1105_Git_audit_hidden_locations(linked-recorded-nul-orphan) | 00:00:00.0010044 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-18.trx:325` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105_Git_audit_hidden_locations` / C1105_Git_audit_hidden_locations(tmp-recorded-cr) | 00:00:00.0008595 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-18.trx:332` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105_Git_audit_hidden_locations` / C1105_Git_audit_hidden_locations(tmp-recorded-nul) | 00:00:00.0004387 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-18.trx:339` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105_Git_audit_hidden_locations` / C1105_Git_audit_hidden_locations(tmp-link-target-cr) | 00:00:00.0007380 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-18.trx:346` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105_Git_audit_hidden_locations` / C1105_Git_audit_hidden_locations(tmp-link-target-newline-last) | 00:00:00.0009457 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-18.trx:353` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105_Git_audit_hidden_locations` / C1105_Git_audit_hidden_locations(tmp-link-target-newline) | 00:00:00.0005323 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-18.trx:360` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105_Git_audit_hidden_locations` / C1105_Git_audit_hidden_locations(tmp-symlink-climb) | 00:00:00.0005455 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-18.trx:367` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105_Git_audit_hidden_locations` / C1105_Git_audit_hidden_locations(tmp-symlink-absolute) | 00:00:00.0009441 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-18.trx:374` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105_Git_audit_hidden_locations` / C1105_Git_audit_hidden_locations(tmp-symlink-relative) | 00:00:00.0012589 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-18.trx:381` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105_Git_audit_hidden_locations` / C1105_Git_audit_hidden_locations(tmp-symlink-dotdot) | 00:00:00.0011591 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-18.trx:388` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105_Git_audit_hidden_locations` / C1105_Git_audit_hidden_locations(tmp-nested-registered) | 00:00:00.0012708 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-18.trx:395` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105_Git_audit_hidden_locations` / C1105_Git_audit_hidden_locations(tmp-linked-ignored) | 00:00:00.0012644 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-18.trx:402` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105_Git_audit_hidden_locations` / C1105_Git_audit_hidden_locations(tmp-dot-git-symlink) | 00:00:00.0011698 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-18.trx:409` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105_Git_audit_hidden_locations` / C1105_Git_audit_hidden_locations(tmp-nested-git-shaped) | 00:00:00.0010340 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-18.trx:416` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105_Git_audit_hidden_locations` / C1105_Git_audit_hidden_locations(tmp-nested-pointer-unregistered) | 00:00:00.0005602 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-18.trx:423` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105_Git_audit_hidden_locations` / C1105_Git_audit_hidden_locations(tmp-nested-dot-git-file) | 00:00:00.0009563 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-18.trx:430` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105_Git_audit_hidden_locations` / C1105_Git_audit_hidden_locations(tmp-nested-dot-git) | 00:00:00.0012188 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-18.trx:437` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105_Git_audit_hidden_locations` / C1105_Git_audit_hidden_locations(tmp-escape) | 00:00:00.0011503 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-18.trx:444` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105_Git_audit_hidden_locations` / C1105_Git_audit_hidden_locations(tmp-mounted-outside) | 00:00:00.0008161 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-18.trx:451` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105_Git_audit_hidden_locations` / C1105_Git_audit_hidden_locations(tmp-linked-absent-staged) | 00:00:00.0008709 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-18.trx:458` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105_Git_audit_hidden_locations` / C1105_Git_audit_hidden_locations(orphan-missing-untracked) | 00:00:00.0007897 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-18.trx:465` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105_Git_audit_hidden_locations` / C1105_Git_audit_hidden_locations(orphan-missing-modified) | 00:00:00.0007337 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-18.trx:472` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105_Git_audit_hidden_locations` / C1105_Git_audit_hidden_locations(editor-swap) | 00:00:00.0007148 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-18.trx:479` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105_Git_audit_hidden_locations` / C1105_Git_audit_hidden_locations(sequencer) | 00:00:00.0005757 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-18.trx:486` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105_Git_audit_hidden_locations` / C1105_Git_audit_hidden_locations(rebase-apply) | 00:00:00.0007905 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-18.trx:493` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105_Git_audit_hidden_locations` / C1105_Git_audit_hidden_locations(rebase-merge) | 00:00:00.0006966 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-18.trx:500` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105_Git_audit_hidden_locations` / C1105_Git_audit_hidden_locations(revert-head) | 00:00:00.0014130 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-18.trx:507` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105_Git_audit_hidden_locations` / C1105_Git_audit_hidden_locations(cherry-pick-head) | 00:00:00.0007542 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-18.trx:514` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105_Git_audit_hidden_locations` / C1105_Git_audit_hidden_locations(merge-head) | 00:00:00.0007179 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-18.trx:521` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105_Git_audit_hidden_locations` / C1105_Git_audit_hidden_locations(merge-autostash) | 00:00:00.0007078 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-18.trx:528` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105_Git_audit_hidden_locations` / C1105_Git_audit_hidden_locations(auto-merge-in-progress) | 00:00:00.0007539 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-18.trx:535` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105_Git_audit_hidden_locations` / C1105_Git_audit_hidden_locations(auto-merge-sequencer) | 00:00:00.0008994 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-18.trx:542` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105_Git_audit_hidden_locations` / C1105_Git_audit_hidden_locations(auto-merge-commit) | 00:00:00.0010905 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-18.trx:549` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105_Git_audit_hidden_locations` / C1105_Git_audit_hidden_locations(auto-merge) | 00:00:00.0008528 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-18.trx:556` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105_Git_audit_hidden_locations` / C1105_Git_audit_hidden_locations(bisect-head) | 00:00:00.0013457 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-18.trx:563` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105_Git_audit_hidden_locations` / C1105_Git_audit_hidden_locations(rebase-head) | 00:00:00.0007645 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-18.trx:570` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105_Git_audit_hidden_locations` / C1105_Git_audit_hidden_locations(fetch-head) | 00:00:00.0010361 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-18.trx:577` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105_Git_audit_hidden_locations` / C1105_Git_audit_hidden_locations(orig-head-missing) | 00:00:00.0007767 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-18.trx:584` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105_Git_audit_hidden_locations` / C1105_Git_audit_hidden_locations(orig-head) | 00:00:00.0011113 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-18.trx:591` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105_Git_audit_hidden_locations` / C1105_Git_audit_hidden_locations(linked-merge-head) | 00:00:00.0005117 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-18.trx:598` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105_Git_audit_hidden_locations` / C1105_Git_audit_hidden_locations(linked-fetch-head) | 00:00:00.0009298 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-18.trx:605` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105_Git_audit_hidden_locations` / C1105_Git_audit_hidden_locations(linked-orig-head) | 00:00:00.0007889 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-18.trx:612` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105_Git_audit_hidden_locations` / C1105_Git_audit_hidden_locations(linked-reset) | 00:00:00.0006726 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-18.trx:619` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105_Git_audit_hidden_locations` / C1105_Git_audit_hidden_locations(linked-reflog) | 00:00:00.0010910 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-18.trx:626` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105_Git_audit_hidden_locations` / C1105_Git_audit_hidden_locations(linked-ref) | 00:00:00.0008549 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-18.trx:633` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105_Git_audit_hidden_locations` / C1105_Git_audit_hidden_locations(linked-head) | 00:00:00.0009461 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-18.trx:640` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105_Git_audit_hidden_locations` / C1105_Git_audit_hidden_locations(index) | 00:00:00.0010927 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-18.trx:647` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105_Git_audit_hidden_locations` / C1105_Git_audit_hidden_locations(notes) | 00:00:00.0004584 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-18.trx:654` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105_Git_audit_hidden_locations` / C1105_Git_audit_hidden_locations(stash) | 00:00:00.0004058 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-18.trx:661` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105_Git_audit_hidden_locations` / C1105_Git_audit_hidden_locations(reflog) | 00:00:00.0009608 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-18.trx:668` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105_Git_audit_hidden_locations` / C1105_Git_audit_hidden_locations(packed-ref) | 00:00:00.0008791 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-18.trx:675` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105_Git_audit_hidden_locations` / C1105_Git_audit_hidden_locations(loose-ref) | 00:00:00.0007453 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-18.trx:682` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105_Git_audit_hidden_locations` / C1105_Git_audit_hidden_locations(bisect-state) | 00:00:00.0009763 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-18.trx:689` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105_Git_audit_hidden_locations` / C1105_Git_audit_hidden_locations(notes-merge) | 00:00:00.0006838 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-18.trx:696` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105_Git_audit_hidden_locations` / C1105_Git_audit_hidden_locations(ref-lock) | 00:00:00.0007288 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-18.trx:703` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105_Git_audit_hidden_locations` / C1105_Git_audit_hidden_locations(replace) | 00:00:00.0008027 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-18.trx:710` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105_Git_audit_hidden_locations` / C1105_Git_audit_hidden_locations(daemon-export) | 00:00:00.0007886 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-18.trx:717` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105_Git_audit_hidden_locations` / C1105_Git_audit_hidden_locations(common-content) | 00:00:00.0004727 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-18.trx:724` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105_Git_audit_hidden_locations` / C1105_Git_audit_hidden_locations(common-empty) | 00:00:00.0007562 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-18.trx:731` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105_Git_audit_hidden_locations` / C1105_Git_audit_hidden_locations(review-evidence-linked) | 00:00:00.0007361 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-18.trx:738` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105_Git_audit_hidden_locations` / C1105_Git_audit_hidden_locations(review-evidence) | 00:00:00.0007281 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-18.trx:745` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105_Git_audit_hidden_locations` / C1105_Git_audit_hidden_locations(antiphon-linked) | 00:00:00.0010500 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-18.trx:752` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105_Git_audit_hidden_locations` / C1105_Git_audit_hidden_locations(antiphon-children) | 00:00:00.0043978 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-18.trx:759` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105_Git_audit_hidden_locations` / C1105_Git_audit_hidden_locations(antiphon-state) | 00:00:00.0004666 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-18.trx:766` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105_Git_audit_hidden_locations` / C1105_Git_audit_hidden_locations(ignored-tracked-modified) | 00:00:00.0003869 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-18.trx:773` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105_Git_audit_hidden_locations` / C1105_Git_audit_hidden_locations(ignored-build-output) | 00:00:00.0003728 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-18.trx:780` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105_Git_audit_hidden_locations` / C1105_Git_audit_hidden_locations(linked-outside) | 00:00:00.0003765 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-18.trx:787` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105_Git_audit_hidden_locations` / C1105_Git_audit_hidden_locations(stale-linked-published) | 00:00:00.0003790 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-18.trx:794` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105_Git_audit_hidden_locations` / C1105_Git_audit_hidden_locations(pruned-path-staged) | 00:00:00.0003829 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-18.trx:801` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105_Git_audit_hidden_locations` / C1105_Git_audit_hidden_locations(stale-pointer-staged) | 00:00:00.0003774 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-18.trx:808` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105_Git_audit_hidden_locations` / C1105_Git_audit_hidden_locations(origin-fetch-allowance-spent) | 00:00:00.0003956 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-18.trx:815` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105_Git_audit_hidden_locations` / C1105_Git_audit_hidden_locations(origin-non-commit) | 00:00:00.0003701 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-18.trx:822` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105_Git_audit_hidden_locations` / C1105_Git_audit_hidden_locations(origin-none-present) | 00:00:00.0003679 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-18.trx:829` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105_Git_audit_hidden_locations` / C1105_Git_audit_hidden_locations(origin-tracking-deleted) | 00:00:00.0003690 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-18.trx:836` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105_Git_audit_hidden_locations` / C1105_Git_audit_hidden_locations(origin-ahead) | 00:00:00.0003632 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-18.trx:843` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105_Git_audit_hidden_locations` / C1105_Git_audit_hidden_locations(published-gc) | 00:00:00.0007669 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-18.trx:850` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105_Git_audit_hidden_locations` / C1105_Git_audit_hidden_locations(published-linked) | 00:00:00.0003930 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-18.trx:857` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105_Git_audit_hidden_locations` / C1105_Git_audit_hidden_locations(published-orig-head) | 00:00:00.0003795 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-18.trx:864` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105_Git_audit_hidden_locations` / C1105_Git_audit_hidden_locations(published-fetch-head) | 00:00:00.0003719 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-18.trx:871` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105_Git_audit_hidden_locations` / C1105_Git_audit_hidden_locations(bare-index) | 00:00:00.0005367 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-18.trx:878` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105_Git_audit_hidden_locations` / C1105_Git_audit_hidden_locations(reftable) | 00:00:00.0006467 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-18.trx:885` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105_Git_audit_hidden_locations` / C1105_Git_audit_hidden_locations(modules) | 00:00:00.0005490 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-18.trx:892` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105_Git_audit_hidden_locations` / C1105_Git_audit_hidden_locations(unknown-entry) | 00:00:00.0006466 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-18.trx:899` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105_Git_audit_hidden_locations` / C1105_Git_audit_hidden_locations(info-entry) | 00:00:00.0005971 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-18.trx:906` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105_Git_audit_hidden_locations` / C1105_Git_audit_hidden_locations(grafts) | 00:00:00.0005692 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-18.trx:913` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105_Git_audit_hidden_locations` / C1105_Git_audit_hidden_locations(replace-packed) | 00:00:00.0005899 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-18.trx:920` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105_Git_audit_hidden_locations` / C1105_Git_audit_hidden_locations(origin-none-present-fetched) | 00:00:00.0005408 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-18.trx:927` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105_Git_audit_hidden_locations` / C1105_Git_audit_hidden_locations(origin-fetch-allowance-shared) | 00:00:00.0007311 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-19.trx:8` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1335_Mirror_owner_stamp_in_linked_admin` / C1335_Mirror_owner_stamp_in_linked_admin(mirror-owner-symlink) | 00:00:00.0533555 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-19.trx:17` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1335_Mirror_owner_stamp_in_linked_admin` / C1335_Mirror_owner_stamp_in_linked_admin(mirror-owner-directory) | 00:00:00.0005650 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-19.trx:24` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1335_Mirror_owner_stamp_in_linked_admin` / C1335_Mirror_owner_stamp_in_linked_admin(mirror-owner-bound) | 00:00:00.0004089 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-19.trx:31` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1335_Mirror_owner_stamp_in_linked_admin` / C1335_Mirror_owner_stamp_in_linked_admin(mirror-owner-hardlink) | 00:00:00.0003809 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-19.trx:38` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1335_Mirror_owner_stamp_in_linked_admin` / C1335_Mirror_owner_stamp_in_linked_admin(mirror-owner-oversize) | 00:00:00.0003990 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-19.trx:45` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1335_Mirror_owner_stamp_in_linked_admin` / C1335_Mirror_owner_stamp_in_linked_admin(mirror-owner-other-uid) | 00:00:00.0003597 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-19.trx:62` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1335_Mirror_owner_stamp_in_linked_admin` / C1335_Mirror_owner_stamp_in_linked_admin(mirror-owner-common) | 00:00:00.0007912 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-19.trx:69` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1335_Mirror_owner_stamp_in_linked_admin` / C1335_Mirror_owner_stamp_in_linked_admin(mirror-owner-unknown) | 00:00:00.0008233 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-19.trx:76` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1335_Mirror_owner_stamp_in_linked_admin` / C1335_Mirror_owner_stamp_in_linked_admin(mirror-owner) | 00:00:00.0007258 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-19.trx:83` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1335_Mirror_owner_stamp_in_linked_admin` / C1335_Mirror_owner_stamp_in_linked_admin(unknown-entry) | 00:00:00.0003940 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-19.trx:90` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105_Git_audit_seed_content` / C1105_Git_audit_seed_content(seed-stash) | 00:00:00.0005175 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-19.trx:97` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105_Git_audit_seed_content` / C1105_Git_audit_seed_content(modified) | 00:00:00.0004144 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-19.trx:104` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105_Git_audit_seed_content` / C1105_Git_audit_seed_content(seed-empty-ignored) | 00:00:00.0005032 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-19.trx:111` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105_Git_audit_seed_content` / C1105_Git_audit_seed_content(seed-ignored) | 00:00:00.0003920 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-19.trx:118` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105_Git_audit_seed_content` / C1105_Git_audit_seed_content(seed-tracked) | 00:00:00.0003519 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-19.trx:125` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105_Git_audit_seed_content` / C1105_Git_audit_seed_content(long-lived) | 00:00:00.0009170 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-19.trx:132` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105_Git_audit_seed_content` / C1105_Git_audit_seed_content(empty-used) | 00:00:00.0007328 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-19.trx:139` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105_Git_audit_seed_content` / C1105_Git_audit_seed_content(seed-untracked) | 00:00:00.0006174 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-19.trx:146` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105_Git_audit_seed_content` / C1105_Git_audit_seed_content(seed) | 00:00:00.0006277 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-19.trx:153` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105_Git_audit_seed_content` / C1105_Git_audit_seed_content(fetch-only) | 00:00:00.0005653 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-19.trx:160` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105_Git_audit_seed_content` / C1105_Git_audit_seed_content(staged) | 00:00:00.0005860 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-19.trx:167` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1335_Mirror_owner_stamp_in_linked_admin` / C1335_Mirror_owner_stamp_in_linked_admin(antiphon-linked) | 00:00:00.0010778 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-19.trx:174` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1335_Mirror_owner_stamp_in_linked_admin` / C1335_Mirror_owner_stamp_in_linked_admin(review-evidence-linked) | 00:00:00.0005149 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-19.trx:181` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105_Verify_tmp_accepts_an_absent_pty_host` / C1105_Verify_tmp_accepts_an_absent_pty_host(absent, 0, ) | 00:00:00.0021715 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-19.trx:188` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105_Resume_post_removal_census_tracks_open_work` / C1105_Resume_post_removal_census_tracks_open_work(land-other, 0, ) | 00:00:00.0028444 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-19.trx:195` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105_Resume_post_removal_census_tracks_open_work` / C1105_Resume_post_removal_census_tracks_open_work(bad-tasks, 2, RecycleTaskCensusUnknown cause=Unstable) | 00:00:00.0012549 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-19.trx:202` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105_Resume_post_removal_census_tracks_open_work` / C1105_Resume_post_removal_census_tracks_open_work(missing-phase, 2, RecycleTaskCensusUnknown cause=Unstable) | 00:00:00.0004731 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-19.trx:209` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105_Resume_post_removal_census_tracks_open_work` / C1105_Resume_post_removal_census_tracks_open_work(land, 2, RecycleLandInFlight 44444444-4444-4444-4444-444444444444) | 00:00:00.0010176 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-19.trx:216` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105_Resume_post_removal_census_tracks_open_work` / C1105_Resume_post_removal_census_tracks_open_work(verified, 0, ) | 00:00:00.0007139 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-19.trx:223` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105_Resume_post_removal_census_tracks_open_work` / C1105_Resume_post_removal_census_tracks_open_work(rebound, 2, RecycleTaskCensusUnknown cause=Unstable) | 00:00:00.0005785 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-19.trx:230` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105_Resume_post_removal_census_tracks_open_work` / C1105_Resume_post_removal_census_tracks_open_work(preflight, 2, RecycleTaskCensusUnknown cause=Unstable) | 00:00:00.0008802 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-19.trx:237` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105_Resume_post_removal_census_tracks_open_work` / C1105_Resume_post_removal_census_tracks_open_work(new-server2, 2, RecycleBoundTasks 44444444-4444-4444-4444-444444444444 status=Blocked runner=server2) | 00:00:00.0010552 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-19.trx:244` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105_Resume_post_removal_census_tracks_open_work` / C1105_Resume_post_removal_census_tracks_open_work(new-desktop, 0, ) | 00:00:00.0013089 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-19.trx:251` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105_Resume_post_removal_census_tracks_open_work` / C1105_Resume_post_removal_census_tracks_open_work(new-temp, 0, ) | 00:00:00.0006425 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-19.trx:258` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105_Resume_post_removal_census_tracks_open_work` / C1105_Resume_post_removal_census_tracks_open_work(terminal, 0, ) | 00:00:00.0004726 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-19.trx:265` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105_Resume_post_removal_census_tracks_open_work` / C1105_Resume_post_removal_census_tracks_open_work(matching, 0, ) | 00:00:00.0004616 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-19.trx:272` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105_Verify_tmp_accepts_an_absent_pty_host` / C1105_Verify_tmp_accepts_an_absent_pty_host(ok, 0, ) | 00:00:00.0004073 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-19.trx:279` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105_Verify_tmp_accepts_an_absent_pty_host` / C1105_Verify_tmp_accepts_an_absent_pty_host(error, 2, RecycleTmpAssetsMissing) | 00:00:00.0004131 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-19.trx:286` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105_Verify_tmp_accepts_an_absent_pty_host` / C1105_Verify_tmp_accepts_an_absent_pty_host(mode, 2, RecycleTmpModeInvalid) | 00:00:00.0005184 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-19.trx:293` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105_Verify_tmp_accepts_an_absent_pty_host` / C1105_Verify_tmp_accepts_an_absent_pty_host(unreadable, 2, RecycleTmpAssetsMissing) | 00:00:00.0003999 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-19.trx:300` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105_Verify_tmp_accepts_an_absent_pty_host` / C1105_Verify_tmp_accepts_an_absent_pty_host(dangling, 2, RecycleTmpAssetsMissing) | 00:00:00.0003914 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-19.trx:307` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105_Verify_tmp_accepts_an_absent_pty_host` / C1105_Verify_tmp_accepts_an_absent_pty_host(symlink, 2, RecycleTmpAssetsMissing) | 00:00:00.0003824 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-19.trx:314` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105_Verify_tmp_accepts_an_absent_pty_host` / C1105_Verify_tmp_accepts_an_absent_pty_host(file, 2, RecycleTmpAssetsMissing) | 00:00:00.0007322 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-19.trx:321` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105_Verify_tmp_accepts_an_absent_pty_host` / C1105_Verify_tmp_accepts_an_absent_pty_host(other-uid, 2, RecycleTmpAssetsMissing) | 00:00:00.0012342 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-19.trx:328` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105_Verify_tmp_accepts_an_absent_pty_host` / C1105_Verify_tmp_accepts_an_absent_pty_host(empty, 2, RecycleTmpAssetsMissing) | 00:00:00.0007565 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-19.trx:335` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105_Git_audit_recovery_tips` / C1105_Git_audit_recovery_tips(bare-head) | 00:00:00.0008407 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-19.trx:342` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105_Resume_post_removal_census_tracks_open_work` / C1105_Resume_post_removal_census_tracks_open_work(unstable, 2, RecycleTaskCensusUnknown cause=Unstable) | 00:00:00.0009958 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-19.trx:349` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105_Git_audit_recovery_tips` / C1105_Git_audit_recovery_tips(linked-private) | 00:00:00.0010540 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-19.trx:356` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105_Git_audit_recovery_tips` / C1105_Git_audit_recovery_tips(worktree-ref) | 00:00:00.0004125 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-19.trx:742` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105_Git_audit_recovery_tips` / C1105_Git_audit_recovery_tips(secondary) | 00:00:00.0010361 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-19.trx:749` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105_Git_audit_recovery_tips` / C1105_Git_audit_recovery_tips(recovery) | 00:00:00.0008260 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-19.trx:756` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105_Git_audit_recovery_tips` / C1105_Git_audit_recovery_tips(reflog-symlink) | 00:00:00.0007764 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-19.trx:763` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105_Git_audit_recovery_tips` / C1105_Git_audit_recovery_tips(reflog-old) | 00:00:00.0007812 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-19.trx:770` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105_Git_audit_recovery_tips` / C1105_Git_audit_recovery_tips(reflog) | 00:00:00.0007489 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-19.trx:777` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105_Git_audit_recovery_tips` / C1105_Git_audit_recovery_tips(stash) | 00:00:00.0006898 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-19.trx:997` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105_Git_audit_recovery_tips` / C1105_Git_audit_recovery_tips(linked-head) | 00:00:00.0010176 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-20.trx:218` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105_Resume_digest_pins_previous_sha12` / C1105_Resume_digest_pins_previous_sha12(short-previous, 2) | 00:00:00.0060860 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-20.trx:745` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105_Resume_digest_pins_previous_sha12` / C1105_Resume_digest_pins_previous_sha12(missing-previous, 2) | 00:00:00.0013771 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-20.trx:752` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105_Resume_digest_pins_previous_sha12` / C1105_Resume_digest_pins_previous_sha12(edited-digest, 2) | 00:00:00.0009884 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-20.trx:759` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105_Resume_digest_pins_previous_sha12` / C1105_Resume_digest_pins_previous_sha12(matching, 0) | 00:00:00.0015990 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-20.trx:790` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1008_Recycle_audits_promisor_checkout` / C1008_Recycle_audits_promisor_checkout | 00:00:00.0165577 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-20.trx:803` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1008_Recycle_reaps_dirty_scratch` / C1008_Recycle_reaps_dirty_scratch | 00:00:00.0022433 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-20.trx:840` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1008_Recycle_refuses_uninspectable_git` / C1008_Recycle_refuses_uninspectable_git | 00:00:00.0060506 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-20.trx:847` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1008_Recycle_refuses_unpublished_and_dirty_work` / C1008_Recycle_refuses_unpublished_and_dirty_work | 00:00:00.0030926 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-20.trx:854` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1008_Recycle_audits_work_as_1654` / C1008_Recycle_audits_work_as_1654 | 00:00:00.0029102 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-20.trx:861` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1008_Recycle_refuses_references_and_unknown_census` / C1008_Recycle_refuses_references_and_unknown_census | 00:00:00.0131998 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-20.trx:868` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1008_Recycle_exact_default_volumes` / C1008_Recycle_exact_default_volumes | 00:00:00.0058203 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-20.trx:907` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105_Git_audit_index_content` / C1105_Git_audit_index_content(assume) | 00:00:00.0012009 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-20.trx:1284` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105_Git_audit_index_content` / C1105_Git_audit_index_content(skip) | 00:00:00.0005099 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-20.trx:1291` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105_Git_audit_index_content` / C1105_Git_audit_index_content(intent) | 00:00:00.0003406 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-20.trx:1298` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105_Git_audit_index_content` / C1105_Git_audit_index_content(sparse) | 00:00:00.0005454 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-20.trx:1305` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1008_Recycle_dry_run_never_mutates` / C1008_Recycle_dry_run_never_mutates | 00:00:00.0017146 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-20.trx:1312` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1008_Recycle_receipt_records_disk_and_partial_failure` / C1008_Recycle_receipt_records_disk_and_partial_failure | 00:00:00.0064736 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-20.trx:1319` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1008_Recycle_preserves_tmp_copyup` / C1008_Recycle_preserves_tmp_copyup | 00:00:00.0019080 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-20.trx:1326` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105_Jq_argv_limit` / C1105_Jq_argv_limit(store, PASS store) | 00:00:00.0006851 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-20.trx:1333` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105_Jq_argv_limit` / C1105_Jq_argv_limit(arg, EXIT=126) | 00:00:00.0003764 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-20.trx:1340` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105_Jq_argv_limit` / C1105_Jq_argv_limit(rawfile, EXIT=0) | 00:00:00.0003612 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-20.trx:1347` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105_Git_audit_object_completeness` / C1105_Git_audit_object_completeness(missing-ancestor-graph) | 00:00:00.0004872 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-20.trx:1354` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105_Git_audit_object_completeness` / C1105_Git_audit_object_completeness(missing-head-tree) | 00:00:00.0003609 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-20.trx:1361` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105_Git_audit_object_completeness` / C1105_Git_audit_object_completeness(missing-tree) | 00:00:00.0003437 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-20.trx:1368` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105_Git_audit_object_completeness` / C1105_Git_audit_object_completeness(missing-commit) | 00:00:00.0003506 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-20.trx:1375` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105_Git_audit_index_content` / C1105_Git_audit_index_content(racy) | 00:00:00.0003498 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-21.trx:41` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105_Resume_identity_refusals` / C1105_Resume_identity_refusals | 00:00:00.0567644 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-21.trx:48` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105_Resume_state_init_image_follows_the_journal` / C1105_Resume_state_init_image_follows_the_journal | 00:00:00.0011678 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-21.trx:55` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105_Resume_foreign_leftover_is_not_an_image_mismatch` / C1105_Resume_foreign_leftover_is_not_an_image_mismatch | 00:00:00.0012825 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-21.trx:62` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105_Main_state_init_without_session_runner_refuses` / C1105_Main_state_init_without_session_runner_refuses | 00:00:00.0011037 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-21.trx:69` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105_Compose_cmp_refuses_before_removal` / C1105_Compose_cmp_refuses_before_removal | 00:00:00.0013825 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-21.trx:104` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105_Generation_identity_refuses_mismatch` / C1105_Generation_identity_refuses_mismatch | 00:00:00.0032360 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-21.trx:111` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105_V218_dry_run_reaches_the_generation_line` / C1105_V218_dry_run_reaches_the_generation_line | 00:00:00.0021361 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-21.trx:118` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105_Relative_dry_run_refuses_before_removal` / C1105_Relative_dry_run_refuses_before_removal | 00:00:00.0015101 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-21.trx:125` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105_Dry_run_previews_a_generation_change_without_moving_the_checkout` / C1105_Dry_run_previews_a_generation_change_without_moving_the_checkout | 00:00:00.0017355 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-21.trx:132` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105_Redeploy_accepts_previous_generation_and_requires_new_bind` / C1105_Redeploy_accepts_previous_generation_and_requires_new_bind | 00:00:00.0027995 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-21.trx:139` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1008_Retire_temp_reclaims_below_cache_disk_gate` / C1008_Retire_temp_reclaims_below_cache_disk_gate | 00:00:00.0011443 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-21.trx:146` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1008_Retire_temp_rechecks_absence_and_retirement` / C1008_Retire_temp_rechecks_absence_and_retirement | 00:00:00.0025760 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-21.trx:153` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1008_Recycle_resume_requires_matching_receipt` / C1008_Recycle_resume_requires_matching_receipt | 00:00:00.0030759 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-21.trx:160` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105_Git_audit_resume_preserves_proof` / C1105_Git_audit_resume_preserves_proof | 00:00:00.0014992 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-21.trx:167` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105_Git_audit_volume_scale` / C1105_Git_audit_volume_scale | 00:00:00.0015946 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-21.trx:174` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105_Git_audit_overall_timeout` / C1105_Git_audit_overall_timeout | 00:00:00.0006785 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-21.trx:181` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1087_Host_census_filters_and_names_cause` / C1087_Host_census_filters_and_names_cause | 00:00:00.0044197 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-24.trx:55` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C849_Deploy_prepares_and_verifies_before_acceptance` / C849_Deploy_prepares_and_verifies_before_acceptance | 00:00:00.0331482 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-24.trx:108` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1374_Host_census_recovers_transient_reads` / C1374_Host_census_recovers_transient_reads | 00:00:00.0013734 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `antiphon-all-split-24.trx:527` | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1338_Retire_temp_dry_run_lists_exactly_the_temp_volumes` / C1338_Retire_temp_dry_run_lists_exactly_the_temp_volumes | 00:00:00.0024059 | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `session-runner-all-group-1.trx:499` | `Antiphon.SessionRunner.Tests.CompletedCardWorkspaceRemovalTests.C1032_InspectionR3Mode` / C1032_InspectionR3Mode(add-executable) | 00:00:00.0066009 | Skipped: Unix executable-bit witness requires Unix filesystem modes |
| `session-runner-all-group-1.trx:537` | `Antiphon.SessionRunner.Tests.CompletedCardWorkspaceRemovalTests.C1032_InspectionR3Mode` / C1032_InspectionR3Mode(remove-executable) | 00:00:00.0006685 | Skipped: Unix executable-bit witness requires Unix filesystem modes |
| `session-runner-all-group-1.trx:1167` | `Antiphon.SessionRunner.Tests.CompletedCardWorkspaceRemovalTests.C1032_RemovalLinuxIgnoredFifoRefuses` / C1032_RemovalLinuxIgnoredFifoRefuses | 00:00:00.0018066 | Skipped: Linux named-pipe inventory witness; native FIFO required |
| `session-runner-all-group-2.trx:372` | `Antiphon.SessionRunner.Tests.RunnerWorkspaceBindingTests.C1032_StoreIdentityRefused` / C1032_StoreIdentityRefused(unreadable) | 00:00:00.0012766 | Skipped: Mode-bit denial of the registration file is Linux-only. |
| `session-runner-all-group-4.trx:16` | `Antiphon.SessionRunner.Tests.HostStatsProbeLiveTests.Linux_probe_reads_live_proc` / Linux_probe_reads_live_proc | 00:00:00.0055880 | Skipped: Linux /proc probe; this lane is Windows, where Windows_probe_reads_live_system_times runs. |
| `pty-host-all-group-1.trx:9` | `Antiphon.PtyHost.Tests.LinuxCgroupCustodyTests.Tracked_launch_places_child_in_tree` / Tracked_launch_places_child_in_tree | 00:00:00.0313997 | Skipped: The linux-cgroup-v1 backend executes on Linux only. |
| `pty-host-all-group-1.trx:14` | `Antiphon.PtyHost.Tests.LinuxCgroupCustodyTests.Double_fork_and_setsid_stay_in_tree` / Double_fork_and_setsid_stay_in_tree | 00:00:00.0016776 | Skipped: The linux-cgroup-v1 backend executes on Linux only. |
| `pty-host-all-group-1.trx:20` | `Antiphon.PtyHost.Tests.LinuxCgroupCustodyTests.Root_exit_with_live_descendants_is_draining_until_seal` / Root_exit_with_live_descendants_is_draining_until_seal | 00:00:00.0020064 | Skipped: The linux-cgroup-v1 backend executes on Linux only. |
| `pty-host-all-group-1.trx:52` | `Antiphon.PtyHost.Tests.LinuxCgroupCustodyTests.Seal_terminates_and_observes_zero` / Seal_terminates_and_observes_zero | 00:00:00.0020404 | Skipped: The linux-cgroup-v1 backend executes on Linux only. |
| `pty-host-all-group-1.trx:87` | `Antiphon.PtyHost.Tests.LinuxCgroupCustodyTests.Child_cannot_sudo_or_move_cgroup` / Child_cannot_sudo_or_move_cgroup | 00:00:00.0014493 | Skipped: The linux-cgroup-v1 backend executes on Linux only. |
| `pty-host-all-group-1.trx:93` | `Antiphon.PtyHost.Tests.LinuxCgroupCustodyTests.Lost_host_leaves_unknown_not_receipt` / Lost_host_leaves_unknown_not_receipt | 00:00:00.0012801 | Skipped: The linux-cgroup-v1 backend executes on Linux only. |
| `pty-host-all-group-1.trx:99` | `Antiphon.PtyHost.Tests.LinuxCgroupCustodyTests.Generation_mismatch_is_refused` / Generation_mismatch_is_refused | 00:00:00.0010120 | Skipped: The linux-cgroup-v1 backend executes on Linux only. |
| `pty-host-all-group-1.trx:105` | `Antiphon.PtyHost.Tests.LinuxCgroupCustodyTests.Second_seal_is_idempotent` / Second_seal_is_idempotent | 00:00:00.0073706 | Skipped: The linux-cgroup-v1 backend executes on Linux only. |
| `agents-pty-all-group-1.trx:770` | `Antiphon.Agents.Pty.Tests.UnixPtyArgvTests.Nul_is_refused_before_native_spawn` / Nul_is_refused_before_native_spawn(exe) | 00:00:00.0045397 | Skipped: Unix PTY argv contract requires a Unix PTY and node |
| `agents-pty-all-group-1.trx:775` | `Antiphon.Agents.Pty.Tests.UnixPtyArgvTests.Original_nul_is_refused_before_containment_and_consumes_attempt` / Original_nul_is_refused_before_containment_and_consumes_attempt | 00:00:00.0011304 | Skipped: Unix PTY argv contract requires a Unix PTY and node |
| `agents-pty-all-group-1.trx:780` | `Antiphon.Agents.Pty.Tests.UnixPtyArgvTests.Containment_introduced_nul_is_refused_before_start_intent` / Containment_introduced_nul_is_refused_before_start_intent(arg) | 00:00:00.0010339 | Skipped: Unix PTY argv contract requires a Unix PTY and node |
| `agents-pty-all-group-1.trx:785` | `Antiphon.Agents.Pty.Tests.UnixPtyArgvTests.Containment_introduced_nul_is_refused_before_start_intent` / Containment_introduced_nul_is_refused_before_start_intent(exe) | 00:00:00.0002032 | Skipped: Unix PTY argv contract requires a Unix PTY and node |
| `agents-pty-all-group-1.trx:790` | `Antiphon.Agents.Pty.Tests.UnixPtyArgvTests.Tracked_argv_is_verbatim_after_containment` / Tracked_argv_is_verbatim_after_containment | 00:00:00.0010568 | Skipped: Unix PTY argv contract requires a Unix PTY and node |
| `agents-pty-all-group-1.trx:795` | `Antiphon.Agents.Pty.Tests.UnixPtyArgvTests.Nul_is_refused_before_native_spawn` / Nul_is_refused_before_native_spawn(last) | 00:00:00.0002418 | Skipped: Unix PTY argv contract requires a Unix PTY and node |
| `agents-pty-all-group-1.trx:800` | `Antiphon.Agents.Pty.Tests.UnixPtyArgvTests.Native_argv_is_verbatim` / Native_argv_is_verbatim(plain) | 00:00:00.0016195 | Skipped: Unix PTY argv contract requires a Unix PTY and node |
| `agents-pty-all-group-1.trx:805` | `Antiphon.Agents.Pty.Tests.UnixPtyArgvTests.Native_argv_is_verbatim` / Native_argv_is_verbatim(empty) | 00:00:00.0002103 | Skipped: Unix PTY argv contract requires a Unix PTY and node |
| `agents-pty-all-group-1.trx:810` | `Antiphon.Agents.Pty.Tests.UnixPtyArgvTests.Native_argv_is_verbatim` / Native_argv_is_verbatim(space_tab) | 00:00:00.0001635 | Skipped: Unix PTY argv contract requires a Unix PTY and node |
| `agents-pty-all-group-1.trx:815` | `Antiphon.Agents.Pty.Tests.UnixPtyArgvTests.Native_argv_is_verbatim` / Native_argv_is_verbatim(cr) | 00:00:00.0001665 | Skipped: Unix PTY argv contract requires a Unix PTY and node |
| `agents-pty-all-group-1.trx:820` | `Antiphon.Agents.Pty.Tests.UnixPtyArgvTests.Native_argv_is_verbatim` / Native_argv_is_verbatim(lf) | 00:00:00.0001509 | Skipped: Unix PTY argv contract requires a Unix PTY and node |
| `agents-pty-all-group-1.trx:825` | `Antiphon.Agents.Pty.Tests.UnixPtyArgvTests.Nul_is_refused_before_native_spawn` / Nul_is_refused_before_native_spawn(middle) | 00:00:00.0001704 | Skipped: Unix PTY argv contract requires a Unix PTY and node |
| `agents-pty-all-group-1.trx:830` | `Antiphon.Agents.Pty.Tests.UnixPtyArgvTests.Native_argv_is_verbatim` / Native_argv_is_verbatim(quotes_backslashes) | 00:00:00.0001525 | Skipped: Unix PTY argv contract requires a Unix PTY and node |
| `agents-pty-all-group-1.trx:835` | `Antiphon.Agents.Pty.Tests.UnixPtyArgvTests.Native_argv_is_verbatim` / Native_argv_is_verbatim(unicode) | 00:00:00.0002988 | Skipped: Unix PTY argv contract requires a Unix PTY and node |
| `agents-pty-all-group-1.trx:840` | `Antiphon.Agents.Pty.Tests.UnixPtyArgvTests.Native_argv_is_verbatim` / Native_argv_is_verbatim(literal_shell) | 00:00:00.0001739 | Skipped: Unix PTY argv contract requires a Unix PTY and node |
| `agents-pty-all-group-1.trx:845` | `Antiphon.Agents.Pty.Tests.UnixPtyArgvTests.Nul_is_refused_before_native_spawn` / Nul_is_refused_before_native_spawn(first) | 00:00:00.0001878 | Skipped: Unix PTY argv contract requires a Unix PTY and node |
| `agents-pty-all-group-1.trx:850` | `Antiphon.Agents.Pty.Tests.UnixPtyArgvTests.Native_argv_is_verbatim` / Native_argv_is_verbatim(crlf) | 00:00:00.0001435 | Skipped: Unix PTY argv contract requires a Unix PTY and node |
| `antiphon-all-group-65-execution/log_261026190647795.diag:1956` | `Antiphon.Tests.Infrastructure.FileUseCaptureTests.1.1.C1539_absent_inputs(System.String).2.1.0` | 00:00:00.0058167 | Requires Linux /proc or the Linux no-follow evidence writer; Windows capture has separate provider coverage. |
| `antiphon-all-group-65-execution/log_261026190647795.diag:1964` | `Antiphon.Tests.Infrastructure.FileUseCaptureTests.1.1.C1539_linux_holder(System.String).2.1.0` | 00:00:00.0031478 | Requires Linux /proc or the Linux no-follow evidence writer; Windows capture has separate provider coverage. |
| `antiphon-all-group-65-execution/log_261026190647795.diag:1972` | `Antiphon.Tests.Infrastructure.FileUseCaptureTests.1.1.C1539_linux_holder(System.String).1.1.0` | 00:00:00.0006371 | Requires Linux /proc or the Linux no-follow evidence writer; Windows capture has separate provider coverage. |
| `antiphon-all-group-65-execution/log_261026190647795.diag:1980` | `Antiphon.Tests.Infrastructure.FileUseCaptureTests.1.1.C1539_absent_inputs(System.String).3.1.0` | 00:00:00.0004733 | Requires Linux /proc or the Linux no-follow evidence writer; Windows capture has separate provider coverage. |
| `antiphon-all-group-65-execution/log_261026190647795.diag:1988` | `Antiphon.Tests.Infrastructure.FileUseCaptureTests.1.1.C1539_absent_inputs(System.String).4.1.0` | 00:00:00.0003105 | Requires Linux /proc or the Linux no-follow evidence writer; Windows capture has separate provider coverage. |
| `antiphon-all-group-65-execution/log_261026190647795.diag:1996` | `Antiphon.Tests.Infrastructure.FileUseCaptureTests.1.1.C1539_absent_inputs(System.String).5.1.0` | 00:00:00.0004755 | Requires Linux /proc or the Linux no-follow evidence writer; Windows capture has separate provider coverage. |
| `antiphon-all-group-65-execution/log_261026190647795.diag:2004` | `Antiphon.Tests.Infrastructure.FileUseCaptureTests.1.1.C1539_budget_and_caps.1.1.0` | 00:00:00.0054396 | Requires Linux /proc or the Linux no-follow evidence writer; Windows capture has separate provider coverage. |
| `antiphon-all-group-65-execution/log_261026190647795.diag:2012` | `Antiphon.Tests.Infrastructure.FileUseCaptureTests.1.1.C1539_redaction_before_sinks.1.1.0` | 00:00:00.0022205 | Requires Linux /proc or the Linux no-follow evidence writer; Windows capture has separate provider coverage. |
| `antiphon-all-group-65-execution/log_261026190647795.diag:2020` | `Antiphon.Tests.Infrastructure.FileUseCaptureTests.1.1.C1539_persistence_fail_safe.1.1.0` | 00:00:00.0031414 | Requires Linux /proc or the Linux no-follow evidence writer; Windows capture has separate provider coverage. |
| `antiphon-all-group-65-execution/log_261026190647795.diag:2028` | `Antiphon.Tests.Infrastructure.FileUseCaptureTests.1.1.C1539_generation_and_ownership.1.1.0` | 00:00:00.0031178 | Requires Linux /proc or the Linux no-follow evidence writer; Windows capture has separate provider coverage. |
| `antiphon-all-group-65-execution/log_261026190647795.diag:2044` | `Antiphon.Tests.Infrastructure.FileUseCaptureTests.1.1.C1539_absent_inputs(System.String).1.1.0` | 00:00:00.0005450 | Requires Linux /proc or the Linux no-follow evidence writer; Windows capture has separate provider coverage. |

## Appendix: all 179 required expanded UIDs without a terminal state

```text
Antiphon.Tests.Application.ApiErrorRecoveryServiceTests.1.1.A_benign_is_api_error_false_synthetic_is_never_adopted.1.1.0
Antiphon.Tests.Application.ApiErrorRecoveryServiceTests.1.1.A_later_user_prompt_resolves_superseded_and_does_not_fire.1.1.0
Antiphon.Tests.Application.ApiErrorRecoveryServiceTests.1.1.A_local_command_record_after_the_stub_does_not_supersede_the_resume.1.1.0
Antiphon.Tests.Application.ApiErrorRecoveryServiceTests.1.1.A_non_running_session_is_skipped.1.1.0
Antiphon.Tests.Application.ApiErrorRecoveryServiceTests.1.1.A_stub_adopts_once_and_a_resweep_does_not_double_insert.1.1.0
Antiphon.Tests.Application.ApiErrorRecoveryServiceTests.1.1.A_stub_older_than_the_window_is_not_adopted.1.1.0
Antiphon.Tests.Application.ApiErrorRecoveryServiceTests.1.1.A_taskless_session_gets_the_bare_resume_prompt.1.1.0
Antiphon.Tests.Application.ApiErrorRecoveryServiceTests.1.1.A_taskless_session_is_detected_by_the_sweep.1.1.0
Antiphon.Tests.Application.ApiErrorRecoveryServiceTests.1.1.AssistantText_plus_TurnEnd_from_one_jsonl_line_schedules_once.1.1.0
Antiphon.Tests.Application.ApiErrorRecoveryServiceTests.1.1.Auto_detected_writer_does_not_shorten_a_manual_DisabledUntil.1.1.0
Antiphon.Tests.Application.ApiErrorRecoveryServiceTests.1.1.Card0412_V02_delayed_repair_clears_at_original_deadline.1.1.0
Antiphon.Tests.Application.ApiErrorRecoveryServiceTests.1.1.Card0412_V02_evidence_source_improves_once.1.1.0
Antiphon.Tests.Application.ApiErrorRecoveryServiceTests.1.1.Card0412_V02_production_null_turnend_hour_only_adopts_same_deadline.1.1.0
Antiphon.Tests.Application.ApiErrorRecoveryServiceTests.1.1.Card0412_V03_exact_active_empty_evidence_enriches_once.1.1.0
Antiphon.Tests.Application.ApiErrorRecoveryServiceTests.1.1.Card0412_V03_expiry_with_later_prompt_does_not_reopen_hold.1.1.0
Antiphon.Tests.Application.ApiErrorRecoveryServiceTests.1.1.Card0412_V03_lineage_mismatch_keeps_history(System.String).1.1.0
Antiphon.Tests.Application.ApiErrorRecoveryServiceTests.1.1.Card0412_V03_lineage_mismatch_keeps_history(System.String).2.1.0
Antiphon.Tests.Application.ApiErrorRecoveryServiceTests.1.1.Card0412_V03_lineage_mismatch_keeps_history(System.String).3.1.0
Antiphon.Tests.Application.ApiErrorRecoveryServiceTests.1.1.Card0412_V03_lineage_mismatch_keeps_history(System.String).4.1.0
Antiphon.Tests.Application.ApiErrorRecoveryServiceTests.1.1.Card0412_V03_lineage_mismatch_keeps_history(System.String).5.1.0
Antiphon.Tests.Application.ApiErrorRecoveryServiceTests.1.1.Card0412_V03_manual_put_retains_deadline.1.1.0
Antiphon.Tests.Application.ApiErrorRecoveryServiceTests.1.1.Card0412_V03_missing_hold_does_not_create_or_reopen.1.1.0
Antiphon.Tests.Application.ApiErrorRecoveryServiceTests.1.1.Card0412_V03_operator_clear_without_later_prompt_does_not_recreate_hold.1.1.0
Antiphon.Tests.Application.ApiErrorRecoveryServiceTests.1.1.Card0412_V04_later_user_prompt_first_adoption_is_superseded_audit.1.1.0
Antiphon.Tests.Application.ApiErrorRecoveryServiceTests.1.1.Card0412_V04_later_user_prompt_repair_does_not_mutate_hold.1.1.0
Antiphon.Tests.Application.ApiErrorRecoveryServiceTests.1.1.Card0412_V04_local_command_does_not_suppress_relevant_wall.1.1.0
Antiphon.Tests.Application.ApiErrorRecoveryServiceTests.1.1.Claude_production_shape_session_limit_uses_AssistantText_not_the_6h_fallback.1.1.0
Antiphon.Tests.Application.ApiErrorRecoveryServiceTests.1.1.Codex_TurnEnd_text_without_AssistantText_still_parses_session_limit.1.1.0
Antiphon.Tests.Application.ApiErrorRecoveryServiceTests.1.1.Dead_time_incident_is_critical_when_channel_bound.1.1.0
Antiphon.Tests.Application.ApiErrorRecoveryServiceTests.1.1.Dead_time_incident_is_warning_unless_channel_bound.1.1.0
Antiphon.Tests.Application.ApiErrorRecoveryServiceTests.1.1.Fable_5_stub_writes_a_fallback_hold_and_does_not_enqueue.1.1.0
Antiphon.Tests.Application.ApiErrorRecoveryServiceTests.1.1.Fires_at_the_rung_and_not_before_through_enqueue_only.1.1.0
Antiphon.Tests.Application.ApiErrorRecoveryServiceTests.1.1.Grok_402_stub_writes_a_fallback_hold_for_grok_4_6_and_never_enqueues.1.1.0
Antiphon.Tests.Application.ApiErrorRecoveryServiceTests.1.1.NeedsHuman_never_schedules.1.1.0
Antiphon.Tests.Application.ApiErrorRecoveryServiceTests.1.1.Session_limit_stub_schedules_one_resume_at_reset_plus_padding.1.1.0
Antiphon.Tests.Application.ApiErrorRecoveryServiceTests.1.1.Sibling_text_is_resolved_before_initial_wall_classification.1.1.0
Antiphon.Tests.Application.ApiErrorRecoveryServiceTests.1.1.Superseded_walls_do_not_count_as_three_deaths.1.1.0
Antiphon.Tests.Application.ApiErrorRecoveryServiceTests.1.1.The_resume_prompt_carries_the_open_tasks_marker.1.1.0
Antiphon.Tests.Application.ApiErrorRecoveryServiceTests.1.1.The_wall_resume_prompt_carries_the_marker_too.1.1.0
Antiphon.Tests.Application.ApiErrorRecoveryServiceTests.1.1.Unknown_parks_at_three_attempts.1.1.0
Antiphon.Tests.Application.ChannelBridgeTests.1.1.A_marker_only_reply_sends_the_document_with_no_text.1.1.0
Antiphon.Tests.Application.ChannelBridgeTests.1.1.A_metadata_only_attachment_becomes_a_visible_could_not_import_note.1.1.0
Antiphon.Tests.Application.ChannelBridgeTests.1.1.A_mid_turn_launch_note_does_not_steal_the_channel_reply.1.1.0
Antiphon.Tests.Application.ChannelBridgeTests.1.1.A_missing_attachment_remains_captured_until_its_due_retry_recovers_the_file.1.1.0
Antiphon.Tests.Application.ChannelBridgeTests.1.1.A_proactive_send_to_a_disabled_channel_is_refused.1.1.0
Antiphon.Tests.Application.ChannelBridgeTests.1.1.An_attach_marker_sends_the_file_inline_and_strips_the_marker_line.1.1.0
Antiphon.Tests.Application.ChannelBridgeTests.1.1.An_attachment_only_message_routes_with_the_saved_file_path.1.1.0
Antiphon.Tests.Application.ChannelBridgeTests.1.1.An_oversized_attachment_is_skipped_with_a_note.1.1.0
Antiphon.Tests.Application.ChannelBridgeTests.1.1.ClassifyKind_tells_answers_from_questions(System.String, Antiphon.Messaging.ChannelReplyKind).4.1.0
Antiphon.Tests.Application.ChannelBridgeTests.1.1.ExtractAttachments_finds_only_whole_line_markers(System.String, System.Int32).1.1.0
Antiphon.Tests.Application.ChannelBridgeTests.1.1.ExtractAttachments_finds_only_whole_line_markers(System.String, System.Int32).2.1.0
Antiphon.Tests.Application.ChannelBridgeTests.1.1.ExtractAttachments_finds_only_whole_line_markers(System.String, System.Int32).3.1.0
Antiphon.Tests.Application.ChannelBridgeTests.1.1.ExtractAttachments_finds_only_whole_line_markers(System.String, System.Int32).4.1.0
Antiphon.Tests.Application.ChannelBridgeTests.1.1.Herdr_held_agent_inbound_is_parked_with_one_incident_and_no_start_notice_or_reroute.1.1.0
Antiphon.Tests.Application.ChannelInboundRecoveryTests.1.1.C593_AcceptanceAckAndQueueCommitCuts_AreIdempotent.1.1.0
Antiphon.Tests.Application.ChannelInboundRecoveryTests.1.1.C593_ChannelWake_IsAutomaticWithQuotaOverride.1.1.0
Antiphon.Tests.Application.ChannelInboundRecoveryTests.1.1.C593_ChannelWake_StartsNonAlwaysOnAutomaticallyThroughExhaustedQuota.1.1.0
Antiphon.Tests.Application.ChannelInboundRecoveryTests.1.1.C593_CrashAfterQueueOwnership_RecoveryFlushesCompleteRecipientPrompt.1.1.0
Antiphon.Tests.Application.ChannelInboundRecoveryTests.1.1.C593_DebounceCrash_RetainsEveryMemberInOrder.1.1.0
Antiphon.Tests.Application.ChannelInboundRecoveryTests.1.1.C593_HoldsAndConflicts_ParkWithoutRerouting.1.1.0
Antiphon.Tests.Application.ChannelInboundRecoveryTests.1.1.C593_QueuedLaunch_WaitsOnceAndRecognizesTerminalFailure.1.1.0
Antiphon.Tests.Application.ChannelInboundRecoveryTests.1.1.C593_Restart_DrainsToEligibleAndBusyRecipients.1.1.0
Antiphon.Tests.Application.ChannelInboundRecoveryTests.1.1.C593_TransferAndUncertainWrite_RequireCompleteRecipientReceipt.1.1.0
Antiphon.Tests.Application.ChannelInboundRecoveryTests.1.1.C593_WakeTimeout_PreservesEnvelopeAndCriticalIncident.1.1.0
Antiphon.Tests.Application.ChannelInboundRecoveryTests.1.1.C767_AgentConcurrency_IsBoundedAndAllGroupsAreAwaited.1.1.0
Antiphon.Tests.Application.ChannelInboundRecoveryTests.1.1.C767_AgentHeadPaging_WrapsPastHeldAgents.1.1.0
Antiphon.Tests.Application.ChannelInboundRecoveryTests.1.1.C767_CancelledPermitWait_DoesNotOverRelease.1.1.0
Antiphon.Tests.Application.ChannelInboundRecoveryTests.1.1.C767_CompetingBridge_MultiPageClaimMissResumesOldestCompletePrompts.1.1.0
Antiphon.Tests.Application.ChannelInboundRecoveryTests.1.1.C767_ConcurrentDrains_ClaimMissRecoversWithoutDuplicatePrompt.1.1.0
Antiphon.Tests.Application.ChannelInboundRecoveryTests.1.1.C767_FailedAgent_WakesOncePerPass.1.1.0
Antiphon.Tests.Application.ChannelInboundRecoveryTests.1.1.C767_FailedPage_ResumesOldestPendingBeforeLaterRows.1.1.0
Antiphon.Tests.Application.ChannelInboundRecoveryTests.1.1.C767_HealthyAgent_ReceivesBeforeFailedWakeDeadline(System.Int32).1.1.0
Antiphon.Tests.Application.ChannelInboundRecoveryTests.1.1.C767_HealthyAgent_ReceivesBeforeFailedWakeDeadline(System.Int32).2.1.0
Antiphon.Tests.Application.ChannelInboundRecoveryTests.1.1.C767_MalformedDelivery_IsAcknowledgedAndNextMessageArrives.1.1.0
Antiphon.Tests.Application.ChannelInboundRecoveryTests.1.1.C767_ParkedPage_ResumesOldestCompletePromptsBeforeLaterRows(System.String).1.1.0
Antiphon.Tests.Application.ChannelInboundRecoveryTests.1.1.C767_ParkedPage_ResumesOldestCompletePromptsBeforeLaterRows(System.String).2.1.0
Antiphon.Tests.Application.ChannelInboundRecoveryTests.1.1.C767_ParkedPage_ResumesOldestCompletePromptsBeforeLaterRows(System.String).3.1.0
Antiphon.Tests.Application.ChannelInboundRecoveryTests.1.1.C767_Stop_AwaitsCancelledGroupsAndRestartRecovers.1.1.0
Antiphon.Tests.Application.ChannelInboundRecoveryTests.1.1.C768_AgedHoldIgnoresOwnedAndUnknownRefusals(System.String).1.1.0
Antiphon.Tests.Application.ChannelInboundRecoveryTests.1.1.C768_AgedHoldIgnoresOwnedAndUnknownRefusals(System.String).2.1.0
Antiphon.Tests.Application.ChannelInboundRecoveryTests.1.1.C768_AgedHoldRaisesChannelIncidentAtThreshold(System.String, System.String).1.1.0
Antiphon.Tests.Application.ChannelInboundRecoveryTests.1.1.C768_AgedHoldRaisesChannelIncidentAtThreshold(System.String, System.String).2.1.0
Antiphon.Tests.Application.ChannelInboundRecoveryTests.1.1.C768_AgedHoldRaisesChannelIncidentAtThreshold(System.String, System.String).3.1.0
Antiphon.Tests.Application.ChannelInboundRecoveryTests.1.1.C768_BrokerReplayAfterPayloadPurgeKeepsNativeIdentity(System.String).1.1.0
Antiphon.Tests.Application.ChannelInboundRecoveryTests.1.1.C768_BrokerReplayAfterPayloadPurgeKeepsNativeIdentity(System.String).2.1.0
Antiphon.Tests.Application.ChannelInboundRecoveryTests.1.1.C768_HoldEpisodeDeduplicatesAcrossDrainsAndRestart(System.String).1.1.0
Antiphon.Tests.Application.ChannelInboundRecoveryTests.1.1.C768_HoldEpisodeDeduplicatesAcrossDrainsAndRestart(System.String).2.1.0
Antiphon.Tests.Application.ChannelInboundRecoveryTests.1.1.C768_HoldEpisodeDeduplicatesAcrossDrainsAndRestart(System.String).3.1.0
Antiphon.Tests.Application.ChannelInboundRecoveryTests.1.1.C768_HoldIncidentAndStampCommitAtomically(System.String, System.String).1.1.0
Antiphon.Tests.Application.ChannelInboundRecoveryTests.1.1.C768_HoldIncidentAndStampCommitAtomically(System.String, System.String).2.1.0
Antiphon.Tests.Application.ChannelInboundRecoveryTests.1.1.C768_HoldIncidentAndStampCommitAtomically(System.String, System.String).3.1.0
Antiphon.Tests.Application.ChannelInboundRecoveryTests.1.1.C768_HoldIncidentAndStampCommitAtomically(System.String, System.String).4.1.0
Antiphon.Tests.Application.ChannelInboundRecoveryTests.1.1.C768_HoldIncidentAndStampCommitAtomically(System.String, System.String).5.1.0
Antiphon.Tests.Application.ChannelInboundRecoveryTests.1.1.C768_HoldIncidentAndStampCommitAtomically(System.String, System.String).6.1.0
Antiphon.Tests.Application.ChannelInboundRecoveryTests.1.1.C768_HoldIncidentsUseExistingAttentionAndPaging.1.1.0
Antiphon.Tests.Application.CodexStartupDeliveryTests.1.1.A_marker_or_prefix_receipt_does_not_confirm_the_brief.1.1.0
Antiphon.Tests.Application.CodexStartupDeliveryTests.1.1.Accepted_prompt_before_verdict_commit_is_not_typed_again.1.1.0
Antiphon.Tests.Application.CodexStartupDeliveryTests.1.1.Committed_brief_is_recovered_after_service_recreation.1.1.0
Antiphon.Tests.Application.CodexStartupDeliveryTests.1.1.Crash_after_attempt_commit_recovers_an_untyped_brief.1.1.0
Antiphon.Tests.Application.CodexStartupDeliveryTests.1.1.Crash_at_running_handoff_preserves_the_queued_brief(System.String).1.1.0
Antiphon.Tests.Application.CodexStartupDeliveryTests.1.1.Crash_at_running_handoff_preserves_the_queued_brief(System.String).2.1.0
Antiphon.Tests.Application.CodexStartupDeliveryTests.1.1.Crash_cut_worker_entry.1.1.0
Antiphon.Tests.Application.CodexStartupDeliveryTests.1.1.Enqueue_during_boot_keeps_the_brief_pending_without_attempt(Antiphon.Server.Domain.Enums.SessionStatus).1.1.0
Antiphon.Tests.Application.CodexStartupDeliveryTests.1.1.Enqueue_during_boot_keeps_the_brief_pending_without_attempt(Antiphon.Server.Domain.Enums.SessionStatus).2.1.0
Antiphon.Tests.Application.CodexStartupDeliveryTests.1.1.Enqueue_during_boot_keeps_the_brief_pending_without_attempt(Antiphon.Server.Domain.Enums.SessionStatus).3.1.0
Antiphon.Tests.Application.CodexStartupDeliveryTests.1.1.Enqueue_during_boot_keeps_the_brief_pending_without_attempt(Antiphon.Server.Domain.Enums.SessionStatus).4.1.0
Antiphon.Tests.Application.CodexStartupDeliveryTests.1.1.Enqueue_during_boot_keeps_the_brief_pending_without_attempt(Antiphon.Server.Domain.Enums.SessionStatus).5.1.0
Antiphon.Tests.Application.CodexStartupDeliveryTests.1.1.Enqueue_during_boot_keeps_the_brief_pending_without_attempt(Antiphon.Server.Domain.Enums.SessionStatus).6.1.0
Antiphon.Tests.Application.CodexStartupDeliveryTests.1.1.Enqueue_failure_is_reported_to_the_original_caller(System.Boolean, System.String).1.1.0
Antiphon.Tests.Application.CodexStartupDeliveryTests.1.1.Enqueue_failure_is_reported_to_the_original_caller(System.Boolean, System.String).2.1.0
Antiphon.Tests.Application.CodexStartupDeliveryTests.1.1.Enqueue_failure_is_reported_to_the_original_caller(System.Boolean, System.String).3.1.0
Antiphon.Tests.Application.CodexStartupDeliveryTests.1.1.Enqueue_failure_is_reported_to_the_original_caller(System.Boolean, System.String).4.1.0
Antiphon.Tests.Application.CodexStartupDeliveryTests.1.1.Explicit_flush_during_boot_types_nothing(System.String).1.1.0
Antiphon.Tests.Application.CodexStartupDeliveryTests.1.1.Explicit_flush_during_boot_types_nothing(System.String).2.1.0
Antiphon.Tests.Application.CodexStartupDeliveryTests.1.1.Explicit_flush_during_boot_types_nothing(System.String).3.1.0
Antiphon.Tests.Application.CodexStartupDeliveryTests.1.1.Explicit_flush_during_boot_types_nothing(System.String).4.1.0
Antiphon.Tests.Application.CodexStartupDeliveryTests.1.1.Incident_startup_frames_never_receive_work.1.1.0
Antiphon.Tests.Application.CodexStartupDeliveryTests.1.1.Interrupted_typed_brief_recovers_with_enter_only.1.1.0
Antiphon.Tests.Application.CodexStartupDeliveryTests.1.1.Launch_ownership_spans_the_entire_startup_gate.1.1.0
Antiphon.Tests.Application.CodexStartupDeliveryTests.1.1.Mode_now_during_boot_is_refused_without_input(Antiphon.Server.Domain.Enums.SessionStatus, System.Boolean).1.1.0
Antiphon.Tests.Application.CodexStartupDeliveryTests.1.1.Mode_now_during_boot_is_refused_without_input(Antiphon.Server.Domain.Enums.SessionStatus, System.Boolean).2.1.0
Antiphon.Tests.Application.CodexStartupDeliveryTests.1.1.Mode_now_during_boot_is_refused_without_input(Antiphon.Server.Domain.Enums.SessionStatus, System.Boolean).3.1.0
Antiphon.Tests.Application.CodexStartupDeliveryTests.1.1.Mode_now_during_boot_is_refused_without_input(Antiphon.Server.Domain.Enums.SessionStatus, System.Boolean).4.1.0
Antiphon.Tests.Application.CodexStartupDeliveryTests.1.1.Mode_now_during_boot_is_refused_without_input(Antiphon.Server.Domain.Enums.SessionStatus, System.Boolean).5.1.0
Antiphon.Tests.Application.CodexStartupDeliveryTests.1.1.Mode_now_during_boot_is_refused_without_input(Antiphon.Server.Domain.Enums.SessionStatus, System.Boolean).6.1.0
Antiphon.Tests.Application.CodexStartupDeliveryTests.1.1.Mode_now_during_boot_is_refused_without_input(Antiphon.Server.Domain.Enums.SessionStatus, System.Boolean).7.1.0
Antiphon.Tests.Application.CodexStartupDeliveryTests.1.1.Old_receipt_does_not_confirm_this_attempt.1.1.0
Antiphon.Tests.Application.CodexStartupDeliveryTests.1.1.Producer_brief_reaches_an_already_eligible_recipient_whole.1.1.0
Antiphon.Tests.Application.CodexStartupDeliveryTests.1.1.Producer_brief_waits_for_busy_recipient_and_arrives_whole.1.1.0
Antiphon.Tests.Application.CodexStartupDeliveryTests.1.1.Readiness_failure_kills_before_disposal.1.1.0
Antiphon.Tests.Application.CodexStartupDeliveryTests.1.1.Readiness_failure_notice_survives_worker_crash(System.String).1.1.0
Antiphon.Tests.Application.CodexStartupDeliveryTests.1.1.Readiness_failure_notice_survives_worker_crash(System.String).2.1.0
Antiphon.Tests.Application.CodexStartupDeliveryTests.1.1.Readiness_failure_notice_survives_worker_crash(System.String).3.1.0
Antiphon.Tests.Application.CodexStartupDeliveryTests.1.1.Readiness_failure_targets_only_the_accepted_generation(System.String).1.1.0
Antiphon.Tests.Application.CodexStartupDeliveryTests.1.1.Readiness_failure_targets_only_the_accepted_generation(System.String).2.1.0
Antiphon.Tests.Application.CodexStartupDeliveryTests.1.1.Readiness_failure_targets_only_the_accepted_generation(System.String).3.1.0
Antiphon.Tests.Application.CodexStartupDeliveryTests.1.1.Readiness_timeout_does_not_consume_delivery_or_boot_wedge_attempts.1.1.0
Antiphon.Tests.Application.CodexStartupDeliveryTests.1.1.Restart_during_boot_reverifies_before_delivering.1.1.0
Antiphon.Tests.Application.CodexStartupDeliveryTests.1.1.Resumed_readiness_failure_cannot_kill_replacement(System.String).1.1.0
Antiphon.Tests.Application.CodexStartupDeliveryTests.1.1.Resumed_readiness_failure_cannot_kill_replacement(System.String).2.1.0
Antiphon.Tests.Application.CodexStartupDeliveryTests.1.1.Resumed_readiness_failure_cannot_kill_replacement(System.String).3.1.0
Antiphon.Tests.Application.CodexStartupDeliveryTests.1.1.Send_now_during_boot_beats_a_closed_grok_rules_barrier.1.1.0
Antiphon.Tests.Application.CodexStartupDeliveryTests.1.1.Send_now_during_boot_is_refused_without_input(Antiphon.Server.Domain.Enums.SessionStatus, System.Boolean).1.1.0
Antiphon.Tests.Application.CodexStartupDeliveryTests.1.1.Send_now_during_boot_is_refused_without_input(Antiphon.Server.Domain.Enums.SessionStatus, System.Boolean).2.1.0
Antiphon.Tests.Application.CodexStartupDeliveryTests.1.1.Send_now_during_boot_is_refused_without_input(Antiphon.Server.Domain.Enums.SessionStatus, System.Boolean).3.1.0
Antiphon.Tests.Application.CodexStartupDeliveryTests.1.1.Send_now_during_boot_is_refused_without_input(Antiphon.Server.Domain.Enums.SessionStatus, System.Boolean).4.1.0
Antiphon.Tests.Application.CodexStartupDeliveryTests.1.1.Send_now_during_boot_is_refused_without_input(Antiphon.Server.Domain.Enums.SessionStatus, System.Boolean).5.1.0
Antiphon.Tests.Application.CodexStartupDeliveryTests.1.1.Send_now_during_boot_is_refused_without_input(Antiphon.Server.Domain.Enums.SessionStatus, System.Boolean).6.1.0
Antiphon.Tests.Application.CodexStartupDeliveryTests.1.1.Send_now_during_boot_is_refused_without_input(Antiphon.Server.Domain.Enums.SessionStatus, System.Boolean).7.1.0
Antiphon.Tests.Application.CodexStartupDeliveryTests.1.1.Starting_session_is_not_published_running_before_positive_ready.1.1.0
Antiphon.Tests.Application.CodexStartupDeliveryTests.1.1.Turn_end_during_boot_types_nothing(Antiphon.Server.Domain.Enums.SessionStatus).1.1.0
Antiphon.Tests.Application.CodexStartupDeliveryTests.1.1.Turn_end_during_boot_types_nothing(Antiphon.Server.Domain.Enums.SessionStatus).2.1.0
Antiphon.Tests.Application.CodexStartupDeliveryTests.1.1.Turn_end_during_boot_types_nothing(Antiphon.Server.Domain.Enums.SessionStatus).3.1.0
Antiphon.Tests.Application.CodexStartupDeliveryTests.1.1.Turn_end_during_boot_types_nothing(Antiphon.Server.Domain.Enums.SessionStatus).4.1.0
Antiphon.Tests.Application.CodexStartupDeliveryTests.1.1.Turn_end_during_boot_types_nothing(Antiphon.Server.Domain.Enums.SessionStatus).5.1.0
Antiphon.Tests.Application.CodexStartupDeliveryTests.1.1.Turn_end_during_boot_types_nothing(Antiphon.Server.Domain.Enums.SessionStatus).6.1.0
Antiphon.Tests.Application.CodexStartupDeliveryTests.1.1.Wrong_session_receipt_does_not_confirm_this_attempt.1.1.0
Antiphon.Tests.Application.LandEvidenceWarningDeliveryTests.1.1.C788_LandEvidenceRefusalReceipt(System.Boolean).1.1.0
Antiphon.Tests.Application.LandEvidenceWarningDeliveryTests.1.1.C788_LandEvidenceRefusalReceipt(System.Boolean).2.1.0
Antiphon.Tests.Application.LandEvidenceWarningDeliveryTests.1.1.C788_LandEvidenceRefusalRecovery(System.String).1.1.0
Antiphon.Tests.Application.LandEvidenceWarningDeliveryTests.1.1.C788_LandEvidenceRefusalRecovery(System.String).2.1.0
Antiphon.Tests.Application.LandEvidenceWarningDeliveryTests.1.1.C788_LandEvidenceRefusalRecovery(System.String).3.1.0
Antiphon.Tests.Application.LandEvidenceWarningDeliveryTests.1.1.C788_LandEvidenceRefusalRecovery(System.String).4.1.0
Antiphon.Tests.Application.LandEvidenceWarningDeliveryTests.1.1.C788_LandEvidenceRefusalRecovery(System.String).5.1.0
Antiphon.Tests.Infrastructure.WorktreeManagerGitIntegrationTests.1.1.C1448_InterruptedCreationIsRecreatedFresh.1.1.0
Antiphon.Tests.Infrastructure.WorktreeManagerGitIntegrationTests.1.1.C1448_InterruptedCreationWithoutDirectoryIsRecreated.1.1.0
Antiphon.Tests.Infrastructure.WorktreeManagerGitIntegrationTests.1.1.C1448_ListMarksAnInterruptedCreationIncomplete.1.1.0
Antiphon.Tests.Infrastructure.WorktreeManagerGitIntegrationTests.1.1.ScanDelegateWorktrees_names_a_dangling_task_branch_and_ignores_card_spawn.1.1.0
Antiphon.Tests.Infrastructure.WorktreeManagerGitIntegrationTests.1.1.WorktreeJanitor_does_not_convert_raw_refusal_into_authority.1.1.0
Antiphon.Tests.Infrastructure.WorktreeManagerGitIntegrationTests.1.1.WorktreeJanitor_preserves_stale_unregistered_leftover_directory.1.1.0
Antiphon.Tests.Infrastructure.WorktreeManagerGitIntegrationTests.1.1.WorktreeJanitor_preserves_stale_worktrees_without_receipt.1.1.0
Antiphon.Tests.Infrastructure.WorktreeManagerGitIntegrationTests.1.1.WorktreeManager_create_throws_when_branch_or_path_exists.1.1.0
Antiphon.Tests.Infrastructure.WorktreeManagerGitIntegrationTests.1.1.WorktreeManager_raw_remove_cannot_use_local_ancestry_as_authority.1.1.0
Antiphon.Tests.Infrastructure.WorktreeManagerGitIntegrationTests.1.1.WorktreeManager_raw_remove_preserves_unmerged_directory_and_branch.1.1.0
Antiphon.Tests.Infrastructure.WorktreeManagerGitIntegrationTests.1.1.WorktreeManager_raw_remove_preserves_unregistered_leftover_directory.1.1.0
Antiphon.Tests.Infrastructure.WorktreeManagerGitIntegrationTests.1.1.WorktreeManager_raw_remove_refuses_without_authority.1.1.0
Antiphon.Tests.Infrastructure.WorktreeManagerGitIntegrationTests.1.1.WorktreeManager_remove_still_throws_when_the_worktree_is_locked.1.1.0
Antiphon.Tests.Infrastructure.WorktreeManagerGitIntegrationTests.1.1.WorktreeManager_touch_updates_last_touched_metadata.1.1.0
Antiphon.Tests.Infrastructure.WorktreeManagerGitIntegrationTests.1.1.WorktreeManager_try_remove_reports_directory_residue_when_a_file_is_held.1.1.0
```


## Appendix: summary missing-expanded-row union by class

The official reason names 1,679 unique expanded UIDs. This class table records the full count without mistaking recovered diagnostic results for official qualification; 1,500 of these UIDs were recovered above. The original unedited 204,548-character reason remains in summary.json.reasons[57].

| Class | Official missing UID count | Recovered diagnostic terminals | Still without terminal evidence |
|---|---:|---:|---:|
| `Antiphon.Tests.Application.AgentCreateRaceTests` | 4 | 4 | 0 |
| `Antiphon.Tests.Application.AgentTaskServiceIntegrationTests` | 120 | 120 | 0 |
| `Antiphon.Tests.Application.ApiErrorRecoveryServiceTests` | 40 | 0 | 40 |
| `Antiphon.Tests.Application.BlockedTaskParkDeliveryTests` | 6 | 6 | 0 |
| `Antiphon.Tests.Application.BlockedTaskParkReclaimTests` | 38 | 38 | 0 |
| `Antiphon.Tests.Application.ChannelBridgeTests` | 40 | 26 | 14 |
| `Antiphon.Tests.Application.ChannelInboundRecoveryTests` | 41 | 0 | 41 |
| `Antiphon.Tests.Application.CodexStartupDeliveryTests` | 62 | 0 | 62 |
| `Antiphon.Tests.Application.DelegateBranchDeliveryTests` | 7 | 7 | 0 |
| `Antiphon.Tests.Application.HerdrLabelFollowTests` | 33 | 33 | 0 |
| `Antiphon.Tests.Application.LandEvidenceWarningDeliveryTests` | 7 | 0 | 7 |
| `Antiphon.Tests.Application.OutputDistillationGateTests` | 103 | 103 | 0 |
| `Antiphon.Tests.Application.PostLandMutationCustodyTests` | 56 | 56 | 0 |
| `Antiphon.Tests.Application.RepairSourceDispatchTests` | 41 | 41 | 0 |
| `Antiphon.Tests.Application.RepairSourceSettlementTests` | 36 | 36 | 0 |
| `Antiphon.Tests.Application.SessionRuntimeActionRoutingTests` | 35 | 35 | 0 |
| `Antiphon.Tests.Application.TaskInputAttentionCostTests` | 35 | 35 | 0 |
| `Antiphon.Tests.Infrastructure.CompletedCardWorktreeRemovalTests` | 37 | 37 | 0 |
| `Antiphon.Tests.Infrastructure.DockerDeliveryBarrierTests` | 24 | 24 | 0 |
| `Antiphon.Tests.Infrastructure.DockerDeliveryDatabaseTests` | 16 | 16 | 0 |
| `Antiphon.Tests.Infrastructure.DockerDeliveryObservationTests` | 31 | 31 | 0 |
| `Antiphon.Tests.Infrastructure.DockerStackContractTests` | 115 | 115 | 0 |
| `Antiphon.Tests.Infrastructure.FileUseCaptureTests` | 13 | 13 | 0 |
| `Antiphon.Tests.Infrastructure.GitServiceTests` | 12 | 12 | 0 |
| `Antiphon.Tests.Infrastructure.GrokRunnerImageContractTests` | 26 | 26 | 0 |
| `Antiphon.Tests.Infrastructure.HostCleanupCheckpointAuthorityTests` | 17 | 17 | 0 |
| `Antiphon.Tests.Infrastructure.HostCleanupCheckpointCompatibilityTests` | 54 | 54 | 0 |
| `Antiphon.Tests.Infrastructure.HostCleanupCheckpointCustodyTests` | 13 | 13 | 0 |
| `Antiphon.Tests.Infrastructure.HostCleanupOwnershipTests` | 12 | 12 | 0 |
| `Antiphon.Tests.Infrastructure.LandRecoveryCheckoutTests` | 34 | 34 | 0 |
| `Antiphon.Tests.Infrastructure.LandRecoveryIgnoredNestedRepositoryTests` | 15 | 15 | 0 |
| `Antiphon.Tests.Infrastructure.LandingAdmissionControlTests` | 42 | 42 | 0 |
| `Antiphon.Tests.Infrastructure.LandingGitProfileTests` | 26 | 26 | 0 |
| `Antiphon.Tests.Infrastructure.LandingGitTests` | 58 | 58 | 0 |
| `Antiphon.Tests.Infrastructure.LandingIdentityControlTests` | 26 | 26 | 0 |
| `Antiphon.Tests.Infrastructure.LandingRemovalControlTests` | 34 | 34 | 0 |
| `Antiphon.Tests.Infrastructure.LandingRemovalPolicyControlTests` | 72 | 72 | 0 |
| `Antiphon.Tests.Infrastructure.LandingSourceBoundaryControlTests` | 30 | 30 | 0 |
| `Antiphon.Tests.Infrastructure.LandingSourceFreshnessTests` | 21 | 21 | 0 |
| `Antiphon.Tests.Infrastructure.RepositoryChildRecoveryTests` | 15 | 15 | 0 |
| `Antiphon.Tests.Infrastructure.SettledWorktreeRemovalTests` | 78 | 78 | 0 |
| `Antiphon.Tests.Infrastructure.WorktreeDeleteAccessProbeTests` | 38 | 38 | 0 |
| `Antiphon.Tests.Infrastructure.WorktreeGuardedCleanupTests` | 49 | 49 | 0 |
| `Antiphon.Tests.Infrastructure.WorktreeLockDiagnosticsTests` | 37 | 37 | 0 |
| `Antiphon.Tests.Infrastructure.WorktreeManagerGitIntegrationTests` | 30 | 15 | 15 |

## Validation of this digest

Read-only parsing visited every summary row and every root TRX; all TRX parsed successfully. It cross-checked 137 TRX Failed results against 137 catalog entries, recovered exactly three additional diagnostic Error states, and matched all 1,500 recovered terminal UIDs against the official missing set. No duplicate recovered terminal UID was counted. All ten suite identities match requiredSuites and selectedSuites. Script totals sum to 591; client JSON independently matches its console total. All suite console logs independently contain exactly 140 failed-test headers (118 Antiphon, 12 SessionRunner, 1 Agents.Pty, 9 E2E). Report validation checks catalog IDs F001–F140 exactly once and compares every first-12-line error, top-six frames, duration and source citation with the parsed originals; it also checks 47 exceptional manifest rows, all 179 absent UIDs and all 538 skip rows. The open-class table contains 33 classes with 64 results lacking a complete matching repair; the remaining 76 results have an addressing change, with native closure pending. Git whitespace/evidence checks are recorded in the completion report. No tests/builds were run for this documentation-only digest.

Default staged `git diff --check` exits 2 for 84 trailing-whitespace lines, all verified inside verbatim evidence fences (Shouldly alignment, literal tab/newline path canary, and original Playwright/error text). They are retained to preserve the requested error excerpts. There are zero whitespace findings outside evidence fences; a diagnostic check with blank-at-eol disabled reports no other whitespace problems. This is a documented evidence-format exception, not a passing default whitespace check.

--- next stage ---
next: code
handoff: Use the open-class table and F001–F140 Windows evidence to debug unclosed failures; reconcile the listed pushed repairs without claiming native closure, and address the two timed-out chunks plus 179 cases with no terminal evidence.
artifact: docs/investigations/2026-10-27-rc-8f055f87-final-run-digest.md
