# Mutation sweep batches 23 and 24: CARD-1669, CARD-1671, CARD-1647, land-graph canary

Task 2deb2419 (Mutation, waived). This was an ordinary-worktree sweep on master
`7b920d84aae97139998f940ee2cce3f457bb6e70` (L). It ran on server2 (Linux) on 2026-11-03,
from 05:46 to 06:44 UTC, well inside the 180-minute budget.

**These results are NOT CARD-0478 evidence.** Merge-role lands have no land operation id, so
there is no SourceLanding. The orchestrator waived the SourceLanding verification card and ran
this as an ordinary-worktree sweep. No product code was changed and nothing was landed.
CARD-1653 S2 (49 PCs) is not in this sweep; a separate sweep follows.

Sources: Code reports `.antiphon/task-87cbe3ef.md` (CARD-1669 slice A) and `.antiphon/task-bd1133db.md`
(CARD-1669 group 3); Merge reports fcf7ce80 (batch 23, `91aa16368..c4e7aa06e`) and df8ec44a
(batch 24, `c4e7aa06e..7b920d84a`).

## Outcome

- 54 mutants were run: 41 killed and 13 survived.
- Of the 13 survivors:
  - 4 are equivalent: M47-2L, M47-3, M47-4 and LG-K on Linux.
  - 3 are masked pairs that are pinned when both halves are removed together: LG-I and LG-C, and LG-S.
  - 2 are guard-meaning survivors. The guard turns a Linux pass into a skip; it does not prevent a red. They are M69-C4 and M69-L2.
  - 4 are reachable coverage gaps: M71-5, M71-6, M71-8 and M47-8.
- Baselines at L were green: Antiphon.Tests filter A 157/157, filter G 10/10; SessionRunner.Tests 114 total, 0 failed, 54 skipped; S5 method 1 skipped; Agents.Pty.Tests 2/2.
- The restored, rebuilt trees at L were green with the same counts (a99, g99, s99, t99, p99).
- The four CARD-1671 review mutants were all killed. The review's probe mutant (drop `overran is null`) **still survives**; CARD-1674 covers it.

## Method

- **Env-gated mutants.** Every mutant was compiled in behind `<Class>.On("<id>")`, which reads `B23_MUTANT` (comma-separated). The helpers were untracked files, deleted afterwards:
  - `server/B23Mutant.cs` (B23MutServer)
  - `src/Antiphon.SessionRunner/B23Mutant.cs` (B23MutRunner)
  - `src/Antiphon.Agents.Pty/B23Mutant.cs` (B23MutPty)
  - `tools/Antiphon.LandGraphAudit/B23Mutant.cs` (B23MutLga). The tool runs as a child process and inherits the variable; the fixture strips only `MSBuild*`.
  - `tests/Antiphon.SessionRunner.Tests/B23Mutant.cs` (B23MutSrTests), for the slice A test-side guards.
- With the variable unset, the gated code is L. The regex mutants are extra `[GeneratedRegex]` methods selected by the gate. The bundle mutant strips the two lines at bundle load. Those are the only shapes that needed it.
- **Builds.** There was one gated build per test project, each under its own build-slot lease with native output, `-p:UseAppHost=false` and `TEMP=/tmp`:
  - SessionRunner.Tests: 30 s
  - Antiphon.Tests: 350 s
  - Agents.Pty.Tests: 29 s
- **Rounds.** Every round was `dotnet run --no-build -p:UseAppHost=false -- --treenode-filter … --report-trx --timeout 40m` under its own lease.
- **Round grouping.** A round never held two mutants that share a witness test method. The same rule kept one Antiphon.Tests round from overlapping another; SessionRunner and Pty rounds ran beside Antiphon.Tests rounds.
- **Restore.** Every file was restored with `git checkout -- <exact path>`, and the helpers were deleted by literal path. Then each project was rebuilt at L (SR 82 s, Pty 63 s, Antiphon.Tests 188 s) and the restored rounds ran.
- **Filters**, each class with a trailing `*`. The TRX of every round lists the named witnesses.
  - SR: `/*/*/(RunnerRestartScriptCompatibilityTests*)|(RunnerRestartPreflightSafetyTests*)|(DaemonLogRotationTests*)|(CodexWindowsLaunchPolicyTests*)|(CodexCliVersionWindowsTests*)|(RunnerCustodyLedgerBackendTests*)|(HerdrGkpLaunchGuardTests*)|(HerdrGrokResumeGuardTests*)|(PtyHostLeakSweepTests*)|(PhoneHomeConnectionServiceTests*)/*` (114)
  - T: `/*/*/(CompletedCardWorkspaceRemovalTests*)/(C1032_RemovalLateFileContentRefuses*)` (1)
  - P: `/*/*/(GrokNativeSessionStoreTests*)/*` (2)
  - A: `/*/*/(AlarmSettingsValidatorTests*)|(RunnerAlarmCoordinatorTests*)|(RunnerAlarmHostedServiceTests*)|(RunnerStateAlarmExclusionTests*)|(ReviewRoundsPolicyTests*)|(AgentTaskCardBindingTests*)|(ReviewEvidenceParserTests*)|(InstructionBundleTests*)/*` (157)
  - G: `/*/*/(LandGraphEvaluationTests*)/(C1385_*)|(C1483_IgnoredFileInDefaultGlob*)` (10, including the batch 24 red witness C1483_IgnoredFileInDefaultGlobApproves that the Merge smoke did not run)

| Round | B23_MUTANT | Total | Failed |
|---|---|---|---|
| s00 | none | 114 | 0 (54 skipped) |
| s01 | M69-H1, M69-D, M69-C1..C4, M69-G1, M69-X | 114 | 26 |
| s02 | M69-H2, M69-W, M69-L1, M69-L2, M69-G2, M69-P1, M69-Ph | 114 | 37 |
| s03 | M69-R1, M69-G3, M69-P2 | 114 | 8 |
| s04 | M69-R2, M69-R3, M69-R4, M69-R5 | 114 | 7 |
| s99 | none, rebuilt at L | 114 | 0 (54 skipped) |
| t00 / t01 / t99 | none / M69-S5 / none | 1 | 0 (skip) / 1 / 0 (skip) |
| p00 / p01 / p02 / p99 | none / M69-P1 / M69-P2 / none | 2 | 0 / 1 / 2 / 0 |
| a00 | none | 157 | 0 |
| a01 | M71-1, M71-5, M47-1, M47-2 | 157 | 3 |
| a02 | M71-2, M71-3, M47-T, M47-2L, M47-B | 157 | 5 |
| a03 | M71-4, M71-6, M47-3, M47-P, M47-7 | 157 | 4 |
| a04 | M71-7, M47-4, M47-5 | 157 | 2 |
| a05 | M71-8, M47-3 + M47-4 (M47-34) | 157 | 3 |
| a06 | M47-6 | 157 | 5 |
| a07 | M47-8 | 157 | 0 |
| a99 | none, rebuilt at L | 157 | 0 |
| g00 | none | 10 | 0 |
| g01..g10 | LG-W, LG-Wi, LG-R, LG-L, LG-I, LG-C, LG-I+LG-C, LG-S, LG-K, LG-S+LG-C | 10 | 6, 6, 1, 2, 0, 0, 1, 0, 0, 3 |
| g99 | none, rebuilt at L | 10 | 0 |

## (1) CARD-1669 slice A: the restart-fixture shell mapping and the Windows-only skip guards

**The Windows-side meaning on Linux.** At s00, every guarded test or row is Skipped (NotExecuted) on Linux. Skipped counts at s00:

- all 26 CodexWindowsLaunchPolicy cases
- the 4 CodexCliVersionWindows methods
- the 4 DaemonLogRotation methods
- both CustodyLedger Windows methods
- every `powershell.exe` restart row (15)
- `Bound_inputs_deny_writes_until_the_child_exits(pwsh.exe)`
- `Hung_http_response_is_canceled_at_wait_deadline(pwsh.exe, False)`

`C1032_RemovalLateFileContentRefuses` is Skipped (t00) after its portable half ran. The Windows lane itself (that each guard is a no-op on Windows) cannot be shown from Linux. That is still the live Windows qualification (Code report PC-3).

| PC | file:line (L) | Mutation | Result | Killing test (assertion) |
|---|---|---|---|---|
| M69-H1 mapping | RestartFixture.cs:68 | `pwsh.exe` passed unchanged | killed | every pwsh restart row, e.g. ScriptCompat `Hung_http…(pwsh.exe, True)`, `Resolved_shell…(pwsh.exe)`, PreflightCache `Script_and_decoder…` (`ShellResolutionException: no readable regular file for: pwsh.exe`) |
| M69-H2 powershell skip | RestartFixture.cs:66 | `powershell.exe` not skipped | killed | 13 powershell rows plus `Each_shell_misses_once_then_hits` red (`ShellResolutionException: no readable regular file for: powershell.exe`); the other 2 powershell rows still skip behind their own Linux guards (R4, R5) |
| M69-R1 | RunnerRestartPreflightSafetyTests.cs:69 | `Resolve(shell)` without `HostShell` | killed | `Resolved_shell_is_the_process_that_runs(pwsh.exe)` (ShellResolutionException); the powershell row also goes red instead of skipping |
| M69-R2 version guard | …PreflightSafetyTests.cs:95 | version line always compared | killed | `Resolved_shell…(pwsh.exe)` (`lines[1]` should be "" but was "7.5.4") |
| M69-R3 Malformed pre-skip | …PreflightSafetyTests.cs:152 | `HostShell` pre-skip removed | killed | the 4 `Malformed…(powershell.exe, *)` rows (ex.Message should contain "parser"/…, but was the CARD-1669 skip text: `Should.ThrowAsync<Exception>` swallowed the skip) |
| M69-R4 Bound_inputs | …PreflightSafetyTests.cs:234 | Linux skip removed | killed | `Bound_inputs_deny_writes…(pwsh.exe)` (`File.WriteAllText(f.CopiedEntry, "tamper")` should throw) |
| M69-R5 Hung_http 200 | RunnerRestartScriptCompatibilityTests.cs:39 | Linux skip removed | killed | `Hung_http…(pwsh.exe, False)` (outcome "healthy" expected, was "wait-failed": iphlpapi) |
| M69-D | DaemonLogRotationTests.cs:125 | skip removed | killed | all 4 methods (`Win32Exception: … start process 'pwsh.exe'`) |
| M69-W | CodexWindowsLaunchPolicyTests.cs:465 | `RequireWindowsHost` no-op | killed (19 of 26) | e.g. `Standard_npm_shim_becomes_node_exe_plus_codex_js` (Exe node.exe expected, was codex.cmd), `Install_root…` (PlatformNotSupportedException CommandLineToArgvW) |
| M69-C1 | CodexCliVersionWindowsTests.cs:17 | skip removed | killed | `C1031_Npm_notice_preserves_version` (codexCliVersion "0.160.0" expected) |
| M69-C2 | …:54 | skip removed | killed | `C959_Npm_probe_uses_launch_resolution` |
| M69-C3 | …:79 | skip removed | killed | `C959_Native_and_direct_node_probe` |
| M69-C4 | …:116 | skip removed | **SURVIVED** (Passed on Linux) | none |
| M69-L1 | RunnerCustodyLedgerBackendTests.cs:84 | skip removed | killed | `Windows_path_unchanged` (VerificationCustodyException verification_custody_unsupported_backend) |
| M69-L2 | …:105 | skip removed | **SURVIVED** (Passed on Linux) | none |
| M69-S5 | CompletedCardWorkspaceS5Tests.cs:614 | skip removed | killed | `C1032_RemovalLateFileContentRefuses` (`attempted` should be True) |

**M69-W, 7 of 26.** The 7 cases that pass with the guard removed are the vacuous passes the Code report named: `Apply` is the identity off Windows, product line 41. The guard's job there is Passed→Skipped, not preventing a red. They are:

- Explicit_codex_exe…
- An_already_normalized…
- The_incident_fixture…
- Relative_direct_node…
- A_claude_request…
- A_codex_transcript_request…
- A_path_based_custom_exe…

**M69-C4 and M69-L2** are guard-meaning survivors, not coverage gaps.

- **C959_Unverified_launcher_is_unknown.** Its assertions are: codexCliVersion is null, an error is present, and no wrapper child started. They hold on Linux for a different reason: there is no npm/PATHEXT resolution at all. This is the same vacuous-pass shape as M69-W.
- **Windows_backend_still_requires_modern_conpty.** It asserts `verification_custody_unsupported_backend` for a WindowsJob binding. Linux refuses WindowsJob with the same code, so the assertion is true but for the wrong reason.
- Both guards are justified as "do not report a Windows premise as a Linux pass". No witness is needed.
- **Option, caller's choice:** L2 could instead stay un-skipped on Linux with a reason, because "Linux refuses WindowsJob" is a real Linux property. It would need its own name.

## (1) CARD-1669 group 3

| PC | file:line (L) | Mutation | Result | Killing test (assertion) |
|---|---|---|---|---|
| M69-G1 only backslash | HerdrGkpLaunchGuard.cs:48 | name split on `\` only (no `Path.GetFileName`) | killed | `Gkp_launch_is_classified_alike…` (`IsGkpLaunch(["-File", "/home/x/.local/bin/gkp.ps1"])` should be True) |
| M69-G2 no drive strip | :50 | drive-relative `C:` not stripped | killed | `Gkp_launch_is_classified_alike…` (`IsGkpLaunch(["-File", "C:gkp.ps1"])` should be True) |
| M69-G3 no `\` split | :49 | file name from `Path.GetFileName` only | killed | `Gkp_launch_is_classified_alike…` (`C:/Users/x\.local/bin\gkp.ps1`); also `Is_gkp_launch_keys_on_the_wrapper_file_name_only`, `Require_throws_the_gkp_code…`, `Missing_chat_proxy_url_warns…` |
| M69-P1 always Missing | GrokNativeSessionStore.cs:29 | `when` condition removed | killed | Pty `Probe_reports_unavailable_for_non_directory_storage`; SR `HerdrGrokResumeGuardTests.Unavailable_store_is_an_IO_failure…` |
| M69-P2 File.Exists inverted | :29 | `when (File.Exists(root))` | killed | Pty `Probe_distinguishes_missing_and_found_storage` and `Probe_reports_unavailable…`; SR `Guid_resume_with_no_directory_throws_the_named_code` and `Unavailable_store…` |
| M69-X leak-sweep regex | PtyHostLeakSweep.cs:156 | `[\\/]` reverted to `\\` | killed | `PtyHostLeakSweepTests.A_host_under_a_registered_root_is_identified_and_a_production_path_is_not` (IsOwnedHost should be True) |
| M69-Ph accepted released early | PhoneHomeConnectionServiceTests.cs:563 | accepted client discarded (`_ = await`) | killed | `Websocket_connect_that_hangs_ends_within_the_connect_timeout` (should throw OperationCanceledException, threw WebSocketException) |

## (2) CARD-1671: drain bound, DrainExpiresAt publication and the wake

| PC | file:line (L) | Mutation | Result | Killing test (assertion) |
|---|---|---|---|---|
| M71-1 (review) | AlarmSettingsValidator.cs:15 | `>` to `>=` | killed | `C1671_the_drain_bound_is_at_most_seven_days` (Validate(10080).Succeeded should be True) |
| M71-2 (review) | RunnerStateAlarmExclusion.cs:45 | `\|\| since > now` removed | killed | `C1671_a_drain_start_in_the_future_fails_safe_and_counts_as_overrun` (Overran(future, T0, 30) should not be null) |
| M71-3 (review) | RunnerAlarmCoordinator.cs:166 | DrainExpiresAt not published (empty map) | killed | `C1671_the_loop_wakes_when_a_drain_passes_its_bound…` (TimeoutException: the loop did not wake when the drain passed its bound) |
| M71-4 (review) | RunnerAlarmCoordinator.cs:258 | reachable-retired case never taken | killed | `C1671_a_retired_runner_that_reconnects_is_told_it_is_retired_not_ineligible` (Body should contain "reachable but retired") |
| M71-5 (review probe) | RunnerAlarmCoordinator.cs:77 | `overran is null` dropped from the publication guard | **SURVIVED** | none (a01: only M71-1/M47-1/M47-2 witnesses red) |
| M71-6 wake margin 0 s | RunnerAlarmHostedService.cs:77 | slack 1 s to 0 s | **SURVIVED** | none |
| M71-7 wake margin 2 s | RunnerAlarmHostedService.cs:77 | slack 1 s to 2 s | killed | `C1671_the_loop_wakes…` (TimeoutException: the loop did not wake…). This is a real assertion, not the FirstTimer load flake. |
| M71-8 min to max | RunnerAlarmHostedService.cs:75-80 | wake on the latest drain expiry instead of the earliest | **SURVIVED** | none |

**M71-5 is confirmed as a survivor, and it is reachable.**

- A draining runner whose drain overran but which is still dispatch-eligible keeps `excluded == "draining"` (:74). The mutant then still publishes its expiry, which is already in the past.
- So `NextDue` returns `expires + 1 s`, which is less than now, on every pass. The alarm loop stops sleeping for as long as that drain stays overran and eligible: a hot loop.
- Nothing pins it. The single-drain loop test uses an ineligible runner, so `overran` clears `excluded`.
- **Proposed witness (CARD-1674):** a RunnerAlarmCoordinatorTests case. A draining, eligible runner has `FakeExclusion.Overran` set and `Expires` in the past. After EvaluateRunnersAsync, `state.Current.DrainExpiresAt` must not contain the runner. A within-bound control runner must still be present.

**M71-6 is a reachable gap.** It is the timing of the wake, not the eventual raise.

- With 0 s slack, the loop wakes exactly at the bound. The drain is not yet overrun (the bound is inclusive), and the next due is the same instant, so the wait returns at once and the loop spins until the clock moves.
- The test's fake clock cannot see the spin. The raise still lands at +1 s.
- **Proposed witness:** in `C1671_the_loop_wakes…`, count coordinator passes (or the timers armed on AlarmClock) across the `Advance(30 min)` step. Expect exactly one pass and a single armed timer due at start+30 min+1 s. The mutant arms at start+30 min and re-runs without the clock advancing.

**M71-8 is a reachable gap.** The only witness has one drain.

- **Proposed witness:** two draining offline runners with expiries at +30 min and +60 min. After `Advance(30 min + 1 s)`, the first must be raised and the second not. The mutant sleeps until +60 min+1 s.

## (3) CARD-1647: review round guard, call-site catch, TagWith, stage-review lines

| PC | file:line (L) | Mutation | Result | Killing test (assertion) |
|---|---|---|---|---|
| M47-1 catch disabled | AgentTaskService.cs:1317 | catch filter false | killed | `AgentTaskCardBindingTests.C1647_a_round_guard_fault_still_creates_the_review` (InvalidOperationException: injected round guard query fault escapes the create) |
| M47-T TagWith removed | ReviewRoundGuard.cs:55 | no `TagWith(QueryTag)` | killed | `C1647_a_round_guard_fault_still_creates_the_review` (fault.Thrown should be > 0, was 0: the tag-keyed fault never fires) |
| M47-2 middle `[ \t]*` to `\s*` | ReviewRoundGuard.cs:25 | `round:\s*([0-9]{1,4})` | killed | `C1647_a_round_label_never_reads_the_next_lines_number` (DeclaredRound("round:\n7 of 3") should be null, was 7) |
| M47-2L leading `[ \t]*` to `\s*` | :25 | `^\s*round:` | **SURVIVED**, equivalent | none. Under Multiline, `^\s*` only adds leading blank lines to the match; `round:` must still begin its own line after whitespace, and the captured number is the same. |
| M47-3 TryParse to Parse | :34 | `int.Parse` | **SURVIVED**, equivalent (confirmed) | none. `[0-9]{1,4}` admits only ASCII 0-9999, which `int.Parse` never rejects. |
| M47-4 `[0-9]` to `\d` | :25 | `(\d{1,4})` | **SURVIVED**, equivalent | none. TryParse with NumberStyles.None and the invariant culture rejects non-ASCII digits, so declared stays null and the count rule applies, as with `[0-9]`. |
| M47-34 both reverted | :25 + :34 | `\d` with `int.Parse` (the pre-D-1 guard) | killed | `ReviewRoundsPolicyTests.C1647_a_non_ascii_round_digit_never_throws…` (both cases: FormatException '４'/'٤'); `C1647_a_non_ascii_round_digit_still_creates_the_review` (warning "the round check failed (FormatException)" instead of the count rule) |
| M47-5 count threshold 2 | :43 | `Count < 2` | killed | `C1647_three_completed_reviews_flag_an_undeclared_round` (two Reviews should be null) |
| M47-6 count threshold 4 | :43 | `Count < 4` | killed | `C1647_three_completed_reviews…`, `C1647_a_fourth_review_on_a_card_is_flagged_at_creation`, both non-ASCII cases and `…still_creates_the_review` (warning should not be null) |
| M47-7 card filter dropped | :56 | `t.CardId == card` removed | killed | `C1647_a_fourth_review_on_a_card_is_flagged_at_creation` (control card's warning should not contain review_round_limit) |
| M47-8 Succeeded filter dropped | :56 | `t.Status == Succeeded` removed | **SURVIVED** | none |
| M47-B stage-review lines removed | Bundles/stage-review.md:13-14 (stripped at load) | `reviewRound:`/`reviewedRange:` lines gone | killed | `C1647_review_bundle_evidence_block_carries_round_and_range` (block should contain reviewRound); `InstructionBundleTests.C1083_EmbeddedBundlesMatchSourceAndRenderedHeaders` |
| M47-P parser refuses the new lines | ReviewEvidence.cs:70 | `reviewRound`/`reviewedRange` keys make the block unusable | killed | `C1647_review_bundle_evidence_block…` and `ReviewEvidenceParserTests.C807_ShippedReviewExampleParses` (parsed.Usable should be True) |

**M47-8 is a reachable coverage gap.** Every seeded prior Review in the witnesses is Succeeded.

- **Proposed witness:** in `C1647_a_fourth_review_on_a_card_is_flagged_at_creation`, give the control card three Reviews in Failed, Cancelled and Working (or Queued) states. Its new Review must carry no `review_round_limit`.
- The mutant counts them and flags the control.

## (4) Land-graph canary tolerance (tools/Antiphon.LandGraphAudit)

| PC | file:line (L) | Mutation | Result | Killing test (assertion) |
|---|---|---|---|---|
| LG-W widened (all rows) | EvaluationSnapshot.cs:55 | every repository-pass row the canary lacks is dropped | killed | `C1385_CopiedFileMissingFromCanaryPassRefuses(tracked, untracked)`, `C1385_IgnoredItemBesideRealDifferenceRefuses`, `C1385_IgnoredLinkOrOutsideItemRefuses(file-link, directory-link, outside)` (ExitCode should be 1, was 0) |
| LG-Wi widened (all item rows) | :55 | every canary-missing item row is dropped | killed | the same 6 cases |
| LG-R tolerance removed | :55 | no row tolerated | killed | `C1483_IgnoredFileInDefaultGlobApproves` (text should not contain "refused:") |
| LG-L applied to links | CanaryRepository.cs:134 | reparse-point walk removed | killed | `C1385_IgnoredLinkOrOutsideItemRefuses(file-link)` and `(directory-link)` (ExitCode should be 1, was 0) |
| LG-I in-repo check removed | CanaryRepository.cs:124 | `StartsWith(_repo + sep)` removed | **SURVIVED** (masked) | none |
| LG-C canary-absence check removed | CanaryRepository.cs:127 | `File.Exists(Root/relative)` term removed | **SURVIVED** (masked) | none |
| LG-I + LG-C | :124 + :127 | both | killed | `C1385_IgnoredLinkOrOutsideItemRefuses(outside)` (ExitCode should be 1, was 0) |
| LG-S copy-set exclusion removed | CanaryRepository.cs:126 | `_copied.Contains(…)` term removed | **SURVIVED** (masked) | none |
| LG-S + LG-C | :126 + :127 | both | killed | `C1385_CopiedFileMissingFromCanaryPassRefuses(tracked, untracked)`, `C1385_IgnoredItemBesideRealDifferenceRefuses` (ExitCode should be 1) |
| LG-K Windows-case comparison | CanaryRepository.cs:56 | case-insensitive copy set on Linux too | **SURVIVED**, equivalent on Linux | none |

**Can one witness kill LG-I or LG-C alone? No.** The Code stage's masking claim holds on Linux, and it is structural:

- **LG-I alone is equivalent while LG-C stands.**
  - The canary root is a sibling of the repository (the same parent). So for any path outside the repository, `Path.GetRelativePath(_repo, full)` starts with `..`, and `Path.Combine(Root, relative)` normalises lexically to the same outside file.
  - That file exists (the guard already required `File.Exists(full)`), so the canary-absence term returns false.
  - No outside path can reach `true` without the in-repo check also being gone (g07).
- **LG-C alone is masked by LG-I and LG-S on Linux.** The canary holds only the copy set, so an in-repository file outside `_copied` is never present under Root.
  - LG-C would bite only if `_copied` misses a copied file's spelling. One such case is a file the canary pass creates itself. Another is a case or separator variant: on Windows, the `OrdinalIgnoreCase` set exists for that.
  - Neither can be built on Linux from the fixture surface.
- **LG-S alone is masked by LG-C.** A copied file is present under Root, so the canary-absence term refuses the tolerance first. The pair is pinned (g10).
- **Proposed witnesses, optional, caller's choice:** call `CanaryRepository.IsIgnoredRepositoryFile` directly through a test seam on a constructed canary. Each would kill one guard alone:
  - an outside path whose resolution through Root differs, for example a canary under a different parent (kills LG-I);
  - a copied file deleted from the canary before the call (kills LG-S);
  - a non-copied file planted in the canary (kills LG-C).
- **Alternative:** accept the layers as defense in depth. The pairs are pinned.

**LG-K.** On Linux the comparer change is unobservable without two spellings of one file, and the fixture creates none. The meaningful mutant (Ordinal on Windows) needs the Windows lane.

## Restoration and final state

- **Restore.** These files were restored with `git checkout -- <exact path>`:
  - src/Antiphon.SessionRunner/HerdrGkpLaunchGuard.cs
  - src/Antiphon.Agents.Pty/GrokNativeSessionStore.cs
  - tests/Antiphon.SessionRunner.Tests/{CodexCliVersionWindowsTests, CodexWindowsLaunchPolicyTests, CompletedCardWorkspaceS5Tests, DaemonLogRotationTests, PtyHostLeakSweep, RunnerCustodyLedgerBackendTests, RunnerRestartPreflightSafetyTests, RunnerRestartScriptCompatibilityTests, PhoneHomeConnectionServiceTests}.cs
  - tests/Antiphon.SessionRunner.Tests/Fixtures/RunnerRestart/RestartFixture.cs
  - server/Application/Services/{AgentTaskService, InstructionBundles, ReviewEvidence, ReviewRoundGuard, RunnerAlarmCoordinator, RunnerStateAlarmExclusion}.cs
  - server/Application/Settings/AlarmSettingsValidator.cs
  - server/Infrastructure/Orchestration/RunnerAlarmHostedService.cs
  - tools/Antiphon.LandGraphAudit/{CanaryRepository, EvaluationSnapshot}.cs

  The five untracked `B23Mutant.cs` helpers were deleted by literal path.
- **Stale-DLL check.** After the L rebuilds, a byte search found neither the UTF-16 `B23_MUTANT` literal nor `B23Mut` in any built copy of Antiphon.Server, Antiphon.SessionRunner, Antiphon.Agents.Pty, Antiphon.LandGraphAudit or Antiphon.SessionRunner.Tests. That covers 38 DLLs in bin/, obj/ and the staged helper folders. The pattern was checked against a positive probe.
- **Restored green at L:** a99 157/157, g99 10/10, s99 114 total with 0 failed and 54 skipped, t99 skipped, p99 2/2.
- **Final state.**
  - HEAD was `7b920d84aae97139998f940ee2cce3f457bb6e70` before this report commit.
  - Tracked source and index are clean, and no sweep process is still running.
  - All 78 native `bin/` and `obj/` directories this sweep built were deleted by literal path, as was the restart tests' `.antiphon/c420-evidence/` output.
  - The remaining untracked output is `.antiphon/inbox/`.
- **Leases.** There were 40 build-slot leases: 6 builds (3 gated, 3 restored) and 34 no-build rounds.
- **Logs and TRX** are in the task scratchpad (`logs/run-<round>.log`, `trx/<round>/`), which is not committed.
