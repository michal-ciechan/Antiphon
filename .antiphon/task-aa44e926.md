# CARD-1614 slice A — task aa44e926

Slice A is complete on Linux. Linux custody stays unadvertised. Windows seal, linger, and kill-on-close were not changed. Slice B was not started. Product greens are at `99a17f1050866393cf6c9c8dbc3b865c850ad039`. This file is the report commit after that SHA and does not change product bytes.

Base: `ade54222543381e2e1b31ad79d8ae33a4035f70b` (rc top-up 37). Branch `feat/card-task-aa44e926`. Card HTTP get was not run; the reviewed plan is the authority.

## Commits

| SHA | Subject |
|---|---|
| `dcb2d70e2fc613ffbdef617fa16a385235548806` | docs(card-1614): import the reviewed Linux custody repair plan |
| `be1d5df93a0bd68fca484ccd9d54cb332e05fcd5` | test(card-1614): witness missing owned pty-host layout |
| `aa13042c760bdf5bd01f1beb4edead31aa50676d` | test(card-1614): witness identity, namespace, and admission |
| `238601a0c7b4f07e47576d2b6958517051235d4c` | fix(card-1614): keep sudo inside the cgroup and split helper locals |
| `a26c1b21a9c55e86e24b2c961395f443f977d17d` | fix(card-1614): isolate custody trees and keep Linux unadvertised |
| `e7cb2c7500e1130cfc53e6b7ea4ac737e946808e` | fix(card-1614): stage the Linux payload and record cleanup ownership |
| `99a17f1050866393cf6c9c8dbc3b865c850ad039` | test(card-1614): accept a probed Linux ledger on Linux |

## Closed-list Linux result

Green rows are the valid receipts below. Execution time is the checkpoint `executed` figure (slot wait excluded). No row's execution exceeded its EstimatedMinutes by more than 25%. CP-10's wall did, entirely from slot wait. The only tests over 5s are `Seal_terminates_and_observes_zero` (300.6s), `Second_seal_is_idempotent` (300.7s), and `Tracked_launch_places_child_in_tree` (120.2s). They live on `LinuxCgroupCustodyTests`, which is Slow with a CARD-1614 allowlist reason. Classification rows CP-24/25/26 passed.

### A0 baseline — run `20261029-210658-23d1` at `dcb2d70e2` — RED exit 1 (expected)

One-time build 63.672s. Batch wall 12m12s, execution 717.092s, admission 15.014s.

| CP | executed | wall | result |
|---|---:|---:|---|
| CP-1 | 304.492s | 304.536s | 1 failed. testsWall 300.600s. State Draining |
| CP-2 | 304.829s | 319.881s | 1 failed. waited 15s. testsWall 300.708s |
| CP-3 | 6.758s | 7.227s | 1 failed. missing apphost |
| CP-4 | 7.110s | 7.211s | 1 failed. missing apphost |
| CP-5 | 6.509s | 6.675s | 1 failed. missing apphost |
| CP-6 | 5.611s | 5.756s | 1 failed. missing apphost |

CP-3..6 are closed by CP-18. CP-1 and CP-2 stay the baseline seal reds; slice A does not change that lifecycle.

### A1 witness — run `20261029-212054-0c2e` at `be1d5df93` — RED exit 1 (expected)

CP-33 executed 3.438s, wall 3.490s, 1 failed (missing owned layout). Build 83.547s. Closed by CP-18.

### A2 witnesses

Valid assertion reds, later closed by the A4 greens named in parentheses.

- Run `20261029-213244-d2d5` at `aa13042c7`. CP-7 is the only assertion: executed 123.694s, wall 183.743s, waited 60s, slotWait 225s, build 25.210s, testsWall 120.177s, 1 failed (tree pid was the outer sudo pid). The same run hard-walled at 30m01s (exit 5). CP-10/35/40/45 in that run are not assertions.
- Run `20261029-220448-e79e` exit 2, `pre-existing-build-source`. Not an assertion.
- Run `20261029-221216-ff00` at `aa13042c7`, RED exit 1. Wall 15m19s, execution 171.275s, admission 748.131s.
  - CP-10 executed 5.574s, wall 485.770s, waited 480s, slotWait 690s, build 35.506s, 1 failed (LaunchedMessage). Closed by CP-19 (26/26).
  - CP-35 executed 3.934s, wall 3.995s, build 42.341s, 1 failed (Detect returned linux-cgroup-v1). Closed by CP-21.
  - CP-40 executed 2.783s, wall 2.825s, 2 failed. Closed by CP-41 (2/2).
  - CP-45 executed 2.645s, wall 2.703s, build 30.623s, 1 failed (path lacked the namespace guid). Closed by CP-20 (25/25).

CP-32 was not run. It would probe the live installed helpers and the shared probe GUID `00000000-0000-0000-0000-00000000c604`. Neither server2 runner was stopped.

### A4 greens — run `20261029-230814-3b57` at `e7cb2c750` — RED exit 1

Source clean, buildSource verified. Wall 4m16s, execution 256.476s, admission 0.048s. 10 green, 1 red, 0 skipped. One-time builds: host 33.425s, pty 42.944s, runner 54.245s.

| CP | executed | wall | count |
|---|---:|---:|---|
| CP-18 | 7.029s | 7.074s | 6/6 |
| CP-19 | 3.960s | 4.026s | 26/26 |
| CP-20 | 4.642s | 4.698s | 25/25 |
| CP-21 | 4.587s | 4.665s | 11 passed, 1 failed, 0 skipped |
| CP-24 | 3.544s | 3.676s | 1/1 |
| CP-25 | 5.028s | 5.577s | 1/1 |
| CP-26 | 4.407s | 4.483s | 1/1 |
| CP-27 | 11.721s | 11.786s | exit 0 |
| CP-31 | 17.206s | 17.336s | exit 0 |
| CP-37 | 3.529s | 3.592s | 3 passed, 14 skipped, 0 failed |
| CP-41 | 4.906s | 5.034s | 2/2 |

CP-21 failure at that SHA: `Linux_backend_accepted_only_when_probe_passed` did not throw. The ledger's platform arm accepts `linux-cgroup-v1` on Linux when the probe answer is that backend. The test's second assertion was the Windows platform refusal. Windows still throws `verification_custody_unsupported_backend` and still writes `unsupported.json`. Linux now requires `PrepareStart` true, `runner-start-intent.json` revision 1, and no `unsupported.json`. The unprobed refusal is unchanged on both platforms. The method is not skipped.

Run `20261029-230612-1228` is not a product result. `--total-timeout 24` is 24 seconds (`ParseDuration` treats a bare number as seconds), so the hard wall fired at about 71s (exit 5). Do not cite it.

### CP-21 repair — run `20261029-231750-239f` at `99a17f105` — GREEN exit 0

executed=12 passed=12 failed=0 skipped=0. executed 6.183s, wall 6.392s, build 98.881s, testsWall 0.641s, waited 0s. The tool deleted `bin-c1614-linux-runner/` after the green receipt.

### CP-28 cold — run `20261029-232147-468f` at `99a17f105` — GREEN exit 0

`NUGET_PACKAGES=/tmp/c1614-cold-nuget` (empty at start; 366M after, including `microsoft.netcore.app.host.linux-x64`) and `TEMP=/tmp TMP=/tmp`. executed=1 passed=1. executed 3.737s, wall 3.777s, build 41.346s, testsWall 0.786s. The tool deleted `bin-c1614-cold-host/`. The cold package directory was scratch and was removed after the receipt.

## Not run here

- CP-22 and CP-50 are the Windows lane. This host is Linux. They are not green.
- CP-32, as above.
- Native rows. `$tool` is `dotnet exec` of `tools/Antiphon.Checkpoints/bin-c1614tool/Antiphon.Checkpoints.dll` (or a rebuilt checkpoint DLL). Do not wrap `row` in a second build slot.

Witness SHA for NP-12, NP-35, and NP-36 is `238601a0c7b4f07e47576d2b6958517051235d4c` (image with `Defaults:#1654 !pam_setcred` and the old `[ -s ]` population). NP-35's method `C1614_Populated_tree_refuses_entry_and_drain_success` was never added; A2 extended `Tracked_launch` instead. Leave the command pointed at the missing method. Do not reset to that SHA.

```
dotnet "$tool" row --name NP-12 --project tests/Antiphon.PtyHost.Tests --output-path bin-c1614-s2a-host/ --filter "/*/*/LinuxCgroupCustodyTests/Tracked_launch_places_child_in_tree" --min-executed 1
dotnet "$tool" row --name NP-35 --project tests/Antiphon.PtyHost.Tests --output-path bin-c1614-s2a-host/ --no-build --filter "/*/*/LinuxCgroupCustodyTests/C1614_Populated_tree_refuses_entry_and_drain_success" --min-executed 1
dotnet "$tool" row --name NP-36 --project tests/Antiphon.SessionRunner.Tests --output-path bin-c1614-s2a-runner/ --filter "/*/*/LinuxRunnerCustodyLifecycleTests/C1614_Container_namespaces_and_probes_do_not_collide" --min-executed 1
```

NP-11, NP-38, and NP-39 use the helper tree at `99a17f1050866393cf6c9c8dbc3b865c850ad039` (helpers are unchanged from `e7cb2c750`; the later commit is the ledger test). NP-38 still names the missing populated-tree method, so its `--min-executed 7` cannot pass until that method exists on a candidate image.

```
dotnet "$tool" row --name NP-11 --project tests/Antiphon.SessionRunner.Tests --output-path bin-c1614-prereq-runner/ --filter "/*/*/LinuxRunnerCustodyLifecycleTests/C1614_Installed_helpers_advertise_custody" --min-executed 1
dotnet "$tool" row --name NP-38 --project tests/Antiphon.PtyHost.Tests --output-path bin-c1614-a-v1-host/ --filter "/*/*/LinuxCgroupCustodyTests/(Tracked_launch_places_child_in_tree*)|(Double_fork_and_setsid_stay_in_tree*)|(Seal_terminates_and_observes_zero*)|(Child_cannot_sudo_or_move_cgroup*)|(Generation_mismatch_is_refused*)|(Second_seal_is_idempotent*)|(C1614_Populated_tree_refuses_entry_and_drain_success*)" --min-executed 7
dotnet "$tool" row --name NP-39 --project tests/Antiphon.SessionRunner.Tests --output-path bin-c1614-prereq-runner/ --no-build --filter "/*/*/LinuxRunnerCustodyLifecycleTests/C1614_Container_namespaces_and_probes_do_not_collide" --min-executed 1
```

Candidate image, when commissioned: `docker build --target session-testing --build-arg SOURCE_REVISION=<full-sha> -f docker/session-runner-grok/Dockerfile`. Do not push the image. B native rows (NP-13, 14, 15, 16, 17, 34, 37) are not this task.

`scripts/c590-remote.sh` and `scripts/verify-card0604-dind-runner.ps1` still require the runner to advertise `linux-cgroup-v1`. Shipped `SessionRunner:LinuxCustodyQualified` is false, so those qualification cases stay red until qualification is explicitly on. Residue counting itself was updated.

## Deviations

- Population witness: no separate `C1614_Populated_tree_refuses_entry_and_drain_success`. NP-35 stays a real red against `238601a0` while `[ -s ]` is the population decision. Later commits read proc content (`custody_procs_populated`: 0 populated, 1 readable-empty, 2 missing).
- `ReadTreePids` and `LinuxCgroupCustodyTests` `ReadTreeAsync` still use the global tree path. `ResolveProcsPath` includes the namespace guid. Changing the global reader would break production reads and the still-red native launch tests. Slice B owns the strict missing-tree read.
- A2 did not add csproj content links for the custody library. Tests walk parents from `AppContext.BaseDirectory`.
- Apphost intermediate output stays in `src/Antiphon.PtyHost/obj`. Relocating it cycled project references or duplicated assembly attributes. `OutDir` is `$(TargetDir)ptyhost-native/`, so the daemon bin is not the output. The staged apphost is user-executable and `libporta_pty.so` is copied beside it.
- Daemon fixtures replaced `sleep 120/300` with a release-file hold capped at 10s. Dispose writes the release file, waits 10s, runs the kill helper (`WaitForExit` 10s), waits another 10s, and throws `custody fixture residue survived the 10s watchdog` if residue remains. Production drain stays 30s. Host observation stays 5s.
- `tests/test-selection-policy.json` was not edited. The census compares project paths and no project was added.
- Linux pwsh has no `TEMP`. CP-27 and CP-31 were run with `TEMP=/tmp TMP=/tmp`. `scripts/lib/c487-harness.ps1` was not edited.
- Policy hash `4bf8e2d990a29851a0c116be9cbbbfd7350ec02a94c49636b213f682d1275305`. Exactly three CARD-1614 execution-policy rows.
- Historical investigation prose that says cleanup never kills was left. Owner docs (orchestration loop, testing guide, runtime invariants, ADR 0002, `VerificationCleanupService` summary) now say terminal-task cleanup can terminate Linux containment and a Windows seal does not.

## Kept outputs

Red stamps: `bin-c1614-baseline/`, `bin-c1614-red-layout/`, `bin-c1614-red/`, `bin-c1614-red-runner/`, `bin-c1614-a-red-namespace/`. Green stamps still on disk: `bin-c1614-linux-host/`, `bin-c1614-linux-pty/`. Checkpoint evidence directories were not deleted. The three leftover empty cgroup trees were not removed.

## Slice B

Not started. Do not treat this report as seal, linger, disposition, or orphan-owner work.
