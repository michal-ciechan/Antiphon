# rc/2026-10-18 Linux re-gate, second run

Status: **roster red, six failures**. Five are inherited confinement-ledger reds. One is the already diagnosed `/proc/<pid>/stat` race, on a different argument from the first re-gate. This is not an rc-profile complete-green receipt and not a publication. The roster was not run a third time.

Candidate: `42cbbd0417a2611f3a690e6671b13c9ba51fbf34` (`rc/2026-10-18`, candidate rc-2026-10-18-42cbbd0417a2).
Previous rc tip used for the inherited check: `61117dd1663d2f47ce4708c18b609b8936491986` (`rc/2026-10-15`).
First gate at `a1d24b6fc92d45ae5274b418bf54c6b8819a8697`: 4528 executed, 4527 passed, 1 failed (checkpoint namespace census). That census case passed here.
First re-gate at this same SHA: one failure, `CheckpointOwnedOutputTests.disk_ledger_cannot_authorize_user_deletion(outside)`. That argument passed here.

Host: Debian 12 container `a6457bc89508`, pwsh 7.5.4, .NET SDK 10.0.402, test host .NET 9.0.21, TUnit 1.44.0 / Microsoft Testing Platform 2.2.2, node v22.23.3. Build-slot broker `ANTIPHON_BUILD_SLOTS_URL` (`http://build-slots:8080/build-slots`). Production session-runner sessions were not used; the suite cleared the runner base URL to `http://127.0.0.1:1`.

## What ran

The reproduction in `docs/investigations/2026-10-15-card-1287-linux-unit.md`, the same method as `docs/investigations/2026-10-18-rc-linux-lane.md`. One isolated build, then one no-build execution of the frozen filter. Filter SHA-256 `9ba95d1db683438f6c382422c9ca4a596d2280930f100da9973b657596020ad7`. Roster SHA-256 `716018884f1a68c923958dde2c65fc7150ecb78d123972fd2d671bfadbe28168` (373 names). `UseAppHost=false`. Output path `bin-rc1019-ede44ef3/`. Headed, distiller, broker, and task-token variables were unset. `TUNIT_MAX_PARALLEL_TESTS` was unset. `MSBUILDDISABLENODEREUSE=1` and `DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER=1`.

Worktree: `/tmp/rc-regate-ede44ef3` (detached). Checkout HEAD during the run was the candidate. No product or test file was edited. `git status --porcelain` was empty before and after.

| Piece | Slot | Wait | Hold | Result |
|---|---|---:|---:|---|
| `dotnet build tests/Antiphon.Tests` | `9952ae91-85bd-45bb-9287-7911cc525711` | 0s | 188s | 1085 warnings, 0 errors. Start `2026-10-19T06:06:18Z`, end `2026-10-19T06:09:27Z` |
| Frozen roster | `c390fb29-165c-4299-ab7b-fddcbd63f8ad` | 0s | 2170s | exit 2. Outer `2026-10-19T06:09:29Z` to `2026-10-19T06:45:41Z` |

Assembly informational version `1.0.0+42cbbd0417a2611f3a690e6671b13c9ba51fbf34`. Assembly SHA-256 `eb6a1c5574804fe224cf94e896b64d2ce8dbe67830c5d98b460b2bf81f5eb787`.

TRX `run.trx`. Times start `2026-10-19T06:09:33.5383875Z`, finish `2026-10-19T06:45:39.7487583Z`. SHA-256 `1679f3f74ff32128739760367bf9647600515fb652eeab3d9a8906a19e443dd6`. Path: `/tmp/rc-linux-ede44ef3/roster/run.trx`. Class identities equal all 373 roster names, missing 0, extra 0.

| Suite | Executed | Passed | Failed | Skipped |
|---|---:|---:|---:|---:|
| antiphon, frozen Linux Unit roster (373 classes) | 4528 | 4522 | 6 | 0 |
| session-runner | not run | | | |
| pty-host | not run | | | |
| agents-pty | not run | | | |
| messaging | not run | | | |
| client | not run | | | |
| scripts | not run | | | |
| e2e | not run | | | |
| markdown-pdf | not run | | | |

`namespace_census_matches_compiled_checkpoint_cases` passed (3.705s). The eight suites other than this roster were not started.

## The named flake did not recur

`disk_ledger_cannot_authorize_user_deletion(outside)` passed in 113ms. The `forged`, `truncated`, and `missing` arguments also passed. The roster was not repeated.

The same `/proc/<pid>/stat` race did hit a different argument in this roster: `CheckpointOwnedOutputTests.changed_inventory_is_refused(directory)` (0.016s). Message: `DirectoryNotFoundException: Could not find a part of the path '/proc/19015/stat'`. Stack reaches `CheckpointNativeIo.StartToken` at `CheckpointNativeIo.cs:37`, the frame diagnosed in `docs/investigations/2026-10-19-rc-proc-stat-flake.md`. The test source blob `44f37ca17094baf8759c72ae0aa8ad37964fd155` is identical at this candidate and at `61117dd1663d2f47ce4708c18b609b8936491986`.

Isolated rerun of that method, at both tips, passed. The treenode filter `/*/*/CheckpointOwnedOutputTests/changed_inventory_is_refused(directory)*` also selected `file`, `changed`, and `link`. Each run executed 4, passed 4, failed 0, skipped 0, including `directory`.

| Tip | Slot | Hold | Result |
|---|---|---:|---|
| `42cbbd04` | `9934d6d0-34cf-4cd3-87bf-6bb7247b1544` | 4s | exit 0 |
| `61117dd1` | `3b6e9707-d989-4705-8c0f-9a8d6a948213` | 7s | exit 0 |

Classification: **not inherited**. It was not red when run at `rc/2026-10-15`. It is the diagnosed registration race, observed once in this roster on `directory`, and it did not reproduce alone. The first re-gate's `outside` failure did not repeat.

## Five inherited ledger reds

All five are in `Antiphon.Tests.Checkpoints.CheckpointConfinementLedgerTests`. The test file blob `e4e39e582ba67b1c6fc80c9c122069ca55e34d1b` is identical at both tips. Each method was run alone after one isolated build of that tip. Each isolated run executed 1, passed 0, failed 1, skipped 0. The assertion text matched the roster.

| Method | Roster | Alone at `42cbbd04` | Alone at `61117dd1` |
|---|---|---|---|
| `rewrite_keeping_size_and_mtime_on_the_real_file_system_is_refused_before_launch` | failed 3.385s. `f0012.js` should be `module.exports = 98;` and was `module.exports = 12;` (`CheckpointConfinementLedgerTests.cs:265`) | failed 3.669s, same assertion. Slot `3b0c5479-788d-4709-b17a-bdba8058ba55`, held 8s | failed 3.896s, same assertion. Slot `5a394742-5c29-4226-ab98-c188fb996eab`, held 8s |
| `portable_stat_rewrite_keeping_size_and_mtime_during_the_slot_wait_is_refused_before_launch` | failed 3.853s. `f0011.js` should be `module.exports = 99;` and was `module.exports = 11;` (`:240`) | failed 3.610s, same assertion. Slot `93a2ec98-722e-49f4-bad2-f84e7f46b34d`, held 6s | failed 3.692s, same assertion. Slot `fc91da42-ccef-4e28-bd05-f8cac8071247`, held 6s |
| `file_changed_during_the_slot_wait_and_restored_by_the_build_is_refused_before_launch` | failed 1.216s. `LedgerCaptures` should be `[False]` and was `[]` (`:159`) | failed 1.567s, same assertion. Slot `94b00f1a-846a-4769-b790-b05ad8e51c0e`, held 8s | failed 1.490s, same assertion. Slot `600901ad-8ccc-47fd-a387-7394bcb1b418`, held 8s |
| `scheduler_stamps_non_output_files_before_the_slot_and_verifies_after_release` | failed 1.352s. `LedgerCaptures` should be `[False]` and was `[]` (`:131`) | failed 1.643s, same assertion. Slot `e1ebe944-9e0e-4050-a9a3-be402abd421f`, held 6s | failed 1.449s, same assertion. Slot `f736078a-c273-4b08-83d2-4bc8a7f27c81`, held 6s |
| `strict_admission_hash_in_the_manifest_refuses_the_build_before_launch` | failed 3.293s. `BuildLedgers` should be `[True]` and was `[]` (`:356`) | failed 3.689s, same assertion. Slot `b77201ae-89bc-4eff-a0a0-8d35db265e50`, held 8s | failed 3.648s, same assertion. Slot `2e2140b4-8497-4147-b1a4-755e80e394cb`, held 5s |

Classification: **inherited**. Each method is red at `61117dd1663d2f47ce4708c18b609b8936491986` on this host today, with the same assertion.

Baseline build: slot `6ebc4e20-0c26-4dfa-a212-7d64e4050fb8`, waited 0s, held 134s, 1069 warnings, 0 errors, `2026-10-19T06:48:29Z` to `2026-10-19T06:50:44Z`. Assembly `1.0.0+61117dd1663d2f47ce4708c18b609b8936491986`, SHA-256 `e3d8452a89733401343c5f966cc24e18d2ae86700e43388aa72bdee819a8fc53`. Worktree `/tmp/rc-baseline-ede44ef3` (detached). No file was edited.

These five are not a stable red of this candidate. The earlier roster at this same SHA passed all five (`/tmp/rc-linux-541c1ad7/roster/run.trx`, SHA-256 `0275471162ed94a65aaa31cba158453ea91c1997dc090b329f66cf9af074a880`): rewrite 8.378s, portable 8.549s, restored 5.385s, scheduler 4.880s, strict 12.531s. That roster's only failure was `disk_ledger_cannot_authorize_user_deletion(outside)`. This run's failures of the five were shorter (1.2–3.9s) and left the build rewrite in place (`12` / `11`) or recorded no capture.

## Limits

No tags, no publish, no product or test edit, no third roster. Generated TRX stays outside Git. Removed 28 `bin-rc1019-ede44ef3` directories and 28 `bin-rc1015-ede44ef3` directories. This pass does not discharge Full rc coverage or the Windows lane.

--- next stage ---
next: decide
handoff: The second Linux CARD-1287 roster at 42cbbd0417a2611f3a690e6671b13c9ba51fbf34 is 4522 passed, 6 failed, 0 skipped. disk_ledger_cannot_authorize_user_deletion(outside) did not recur. The /proc stat race recurred once, on changed_inventory_is_refused(directory), and that method passed alone at both this tip and rc/2026-10-15. Five CheckpointConfinementLedgerTests methods failed with the same assertions alone at both tips, so they are inherited; the earlier roster at this same SHA passed those five. The namespace census passed. The roster was not run again.
artifact: docs/investigations/2026-10-19-rc-linux-regate2.md
