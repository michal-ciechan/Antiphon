# rc/2026-11-19 cut and its prepared, unlaunched Windows Full run (2026-11-13)

Task 1d7cc1fe. This task created one new rc branch, checked the cumulative seed against it, and prepared a private clone, a launch record and a preflight. It did **not** launch the run. The orchestrator first restarts the AppHost and the desktop runner onto the new rc, then dispatches the launch.

The task created no tag and published or promoted nothing. It moved no other rc ref and changed no Windows Update setting. The AppHost, the production runner, Docker, the containers and every session were only read. There was no live qualification run: 0b292fa4 ended at 05:20Z.

Created:

- branch `rc/2026-11-19`
- `C:\Antiphon\releases\checkout-1d7cc1fe\`
- `C:\Antiphon\releases\task-1d7cc1fe\`

## 1. Cut

| Item | Value |
|---|---|
| New ref | `rc/2026-11-19` = **`ab7af12c699039270af7e22a5d7fd794035bc325`** (`git ls-remote` confirmed after the push) |
| Name | Highest existing was `rc/2026-11-18` (`56e96eabf`), so this is the next day. `rc/2026-11-19` did not exist before. |
| Candidate id | `rc-2026-11-19-ab7af12c6990` |
| Ancestry | `ab7af12c6` is an ancestor of `origin/master`. It **is** the current master tip (`origin/master` = `ab7af12c699039270af7e22a5d7fd794035bc325` at 05:43Z). |
| Landed on master after the cut | nothing |
| Range from rc/2026-11-18 | `56e96eabf..ab7af12c6`: 100 commits (no merges), 82 paths. The changed-file list is identical to the one the seed was built from. |

### In the cut, not in rc/2026-11-18

- **g82 card-file git cancel hook.** `dc5d74fb9` is the red witness and `c00aad519` the fix: a canceled card-file git tree now ends with its Windows job. CARD-1859 followed with `8ddff78e9` and `4b320084e` (the TryKill comment names the false job-terminate path). CARD-1856 `d1c0c0899` states the kill-fallback attach-after-start limit.
- **CARD-0747 queue-row expiry.** `4952cd7ef` and `01dab30c4` expire aged Pending rows of terminal tasks. `4b79f6b03` and `2dac9d85c` recheck under the session lock. `eb38ce667` and `880172176` page the canceled ids. Then `dd5b14672` and the witnesses. CARD-1857 `ad02a3b73`, `2637fa770` and `5c8dda8e5` add the expiry witnesses.
- **Hang fixes, batch 187** (hang d268c50c):
  - `457741398`, then `124235c4d` and `02f15f0cf`: C24 kills the whole descendant scope, which reaches the msys `sleep` (g118).
  - `1484cc1f1` and `412a7bc4a`: sweep every pty-host root.
  - `ef475fb59` and `8f321c55e`: C502 tears down its pty-host in a finally (sr g4).
  - `3a984fae2`, `6626e8144`, and `4a409cb32` (why-missed).
  - CARD-1853: `33c852a09`, `a9779c52f`, `51bbd1869`.
- **C443 fixture.** `aa5e14b61` expects the CARD-1224 UnixPty ceiling.
- **restart-session-runner census and closures** (debug 4ca0643a): `dae874a0d`, `b108f2e97`, `e8a064b0a`.
- **Nightly EOL hardening:**
  - ae84478d g126: `6f3902f7b`, `bc52e8e3c`, `bc9f86c6b`, `cc9777eb4`.
  - `da330aac0`, `7f19c1c89` and `68912c3f7` skip unresolved EOL paths.
  - `e761976e7`.
- **Nightly performance.** `898120436`, `077f0b3a7` and `b83497b83` speed up the expected-skip and source-gap scans. `e3be96cfd` covers impacted-classes and `75878de23` the client filter test.
- **DerivedImpact standalone driver** (CARD-1861): `c43f6a502`, `815a52492`, `ea15a8886`, `78c3f2d9f`. CARD-1860 is `6c3aba509`, `c0b61ccfe` and `a003d176f`.
- **CARD-1851** (fail closed on PID reads, capture nightly helpers): `733f1f7bd`, `a610396e2`, `ed837a344` and docs.
- **CARD-1836.** S2 boundary map and stable chunk identities: `4dddeb576`, `675e43cec`, `0241881c1`, `933b71486`, `c873e75b8`. S3a input inventory: `907ea2ba3`, `1d240a77f`, `929b75dd4`, `142c55c2e`, `318b0139c`, `14a9ab632`, `1330c24e3`, `5d193c898`, `eed04e7ad`. Both are libraries; nothing activates them in the run.
- **CARD-1820 S1R1** (candidate-bound incomplete release evidence): `7a67b659f`, `138bdde30`, `6b1cc5879`, `87d155183`, `190e6a0b9`, `8875093b0`, `44896818e`.
- `9d98ed092` removes the unread `tests/linux-test-roster.json`.
- Derived-impact index regenerations for merge batches 187 to 195, and a land-graph refresh (`fcc4b428a`).
- **Plans and investigations:**
  - CARD-1836 plan and investigation (`47eb20eab`, `121f78505`)
  - CARD-1850 containment plan (`e521d68da`)
  - CARD-1820 plan and selector investigation (`e813ec4f4`, `9bd54b652`, `a3e018c41`)
  - CARD-1844 migration state (`2bb3d8899`)

### Not in the cut

None of these is on master yet:

- CARD-1862 replay speed-up
- CARD-1820 S1R2
- CARD-1836 S2 hardening, S5a and S8a
- containment S1 (CARD-1850; only the plan is in)
- CARD-1844 S1 and S2 (only the docs commit is in)

A grep of master history since 2026-11-05 for `1862`, `S1R2`, `card-1844` and `card-1850` finds only the two docs commits named above.

## 2. Seed check

| Check | Result |
|---|---|
| Seed file | `C:\Antiphon\releases\task-cf82bfb0\seed-cumulative.json`, 727,161 bytes |
| Whole-file SHA-256 | `64f66b7b215f2635b3cca3c82240c59d4e4c8cf90f29fa006b188d3985a3ce9d`, **equal** to the stated digest |
| Cut's reader | `Read-NightlyStoppedRunSeed`, from `checkout-1d7cc1fe\scripts\lib`: accepted 1,161 chunks and 1 failed-test row. `sourceCandidateId` is `rc-2026-11-18-56e96eabf698` and `sourceSha` is `56e96eabf698...`. |
| Cut's dispatch entry | `Initialize-NightlyDispatchHistory -ExpectedDigest <digest> -CandidateSha ab7af12c6... -AcceptCrossCandidateSeed -SeedSourceCandidateId rc-2026-11-18-56e96eabf698`, called with no owned directory, so it copied nothing. Result: **no fallback**, history loaded, digest returned unchanged. This is the path `nightly-tests` uses. |
| Negative controls, same entry | wrong digest: `history-digest-mismatch`. No `-AcceptCrossCandidateSeed`: `seed-acknowledgement-required`. Source `rc-2026-11-17-3db1a43b0224`: `seed-source-candidate-mismatch`. |
| Freshness | The reader refuses a seed whose `createdAtUtc` or `sourceObservedAtUtc` is more than 14 days old. Created 2026-11-13 05:36:58Z, so a launch before about 2026-11-27 is in date. |

The reader and its controls are in `C:\Antiphon\releases\task-1d7cc1fe\verify-seed.ps1`. Its output is `verify-seed-output.txt`.

### Recount for exactly rc/2026-11-19 at ab7af12c6

`scripts/impacted-classes.ps1 -Range 56e96eabf698..ab7af12c6 -Json` was re-run from the cut tree. Its output is **byte-identical** to the one the seed was built from (SHA-256 `4a6767be...aa05`), and `git diff --name-only` gives the same 82 paths. An independent recount from the raw 0b292fa4 export, using that dirty set (180 bare names, with `scripts` dirty and `client` clean), agrees with the seed's own row classification:

| Count | Seed rows | Independent recount |
|---|---:|---:|
| Seeded as passed | **729** | 729 |
| Newly invalidated (`source-changed`) | **429** | 429 |
| Failed-first | **1** (antiphon g82, 1 row) | 1 |
| Hung, no credit | **2** (antiphon g118, sr g4) | 2 |
| Other | 0 | 0 |

Per suite (seeded / failed-first / invalidated / hung):

| Suite | Seeded | Failed-first | Invalidated | Hung |
|---|---:|---:|---:|---:|
| antiphon | 472 | 1 | 411 | 1 |
| session-runner | 86 | 0 | 14 | 1 |
| e2e | 74 | 0 | 2 | 0 |
| agents-pty | 28 | 0 | 1 | 0 |
| pty-host | 53 | 0 | 0 | 0 |
| markdown-pdf | 12 | 0 | 0 | 0 |
| messaging, observability, resilience, git-in-process | 1 each | 0 | 0 | 0 |
| server-git | 0 | 0 | 1 | 0 |

The rerun total is **432** chunks plus `client`, `scripts`, and any chunk that the cut's own plan adds.

**Tree difference: none.** The seed was built against master `ab7af12c6`, and the cut is that same commit, so the tree is the same. The seed's chunk keys come from 0b292fa4's plan. The cut's run plans its own chunks, and the resolver maps classes to the new plan, so new witness classes in this range (CARD-0747, 1836, 1820, 1851, 1859) run unseeded. In `-SelectionMode Full` the seed only orders the run. Every chunk still executes.

## 3. Launch (NOT run)

### Prepared files

| Path | Content |
|---|---|
| `C:\Antiphon\releases\checkout-1d7cc1fe\` | Fresh clone of `https://github.com/michal-ciechan/Antiphon` with `--reference-if-able checkout-25e35cdf --dissociate`, so there are no alternates (the same method as `checkout-25e35cdf`). `core.longpaths=true`, detached at `ab7af12c6`, `git status --porcelain` empty. Both markers are present: `.antiphon-nightly-owned` (`nightly-clone`) and `.antiphon-release-owned` (`release-clone`), with the same origin. |
| `C:\Antiphon\releases\task-1d7cc1fe\launch-rc.ps1` | Copy of `scripts/qualification/launch-rc.ps1`. SHA-256 `DB7A6DDF...21F65664` is identical in the repository, the cut checkout and the task folder (unchanged since rc/2026-11-18). |
| `C:\Antiphon\releases\task-1d7cc1fe\launch-record.json` | SHA-256 `c130f18c...aa585c`. Run id `97150a0b03454892a10a086837fe8058`, state root `C:\Antiphon\releases\state-1d7cc1fe`, candidate `rc-2026-11-19-ab7af12c6990`, build-slot label `rc-qualification-1d7cc1fe`. Its flags are in the same order as 25e35cdf's record, and every flag is a parameter of the cut's `nightly-run.ps1`. |
| `C:\Antiphon\releases\task-1d7cc1fe\preflight.ps1` | Read-only GO/NO-GO check. Re-run it just before launch. Exit 0 means GO. |

`nightlyArguments`:

```
-Profile rc -Trigger rc -SelectionMode Full -RunId 97150a0b03454892a10a086837fe8058 -Ref rc/2026-11-19
-ExpectedSha ab7af12c699039270af7e22a5d7fd794035bc325 -CandidateId rc-2026-11-19-ab7af12c6990
-CheckoutRoot C:\Antiphon\releases\checkout-1d7cc1fe -ReleaseRoot C:\Antiphon\releases -StateRoot C:\Antiphon\releases\state-1d7cc1fe
-LogRoot C:\Antiphon\releases\candidates\rc-2026-11-19-ab7af12c6990\logs
-CoordinationRoot C:\Antiphon\releases\candidates\rc-2026-11-19-ab7af12c6990\coordination
-Repository michal-ciechan/Antiphon -PriorFailuresPath C:\Antiphon\releases\task-cf82bfb0\seed-cumulative.json
-PriorFailuresDigest 64f66b7b215f2635b3cca3c82240c59d4e4c8cf90f29fa006b188d3985a3ce9d
-AcceptCrossCandidateSeed -SeedSourceCandidateId rc-2026-11-18-56e96eabf698
```

### Launch command (the CARD-1667 set; `Win32_Process.Create` keeps the starter out of the caller's Job Object and console)

Run from any pwsh 7 session, after the AppHost restart and a GO from the preflight:

```powershell
pwsh -NoProfile -File C:\Antiphon\releases\task-1d7cc1fe\preflight.ps1   # must print GO, exit 0
$cmd = '"C:\Program Files\PowerShell\7\pwsh.exe" -NoProfile -File C:\Antiphon\releases\checkout-1d7cc1fe\scripts\qualification\start-supervisor.ps1 -TaskRoot C:\Antiphon\releases\task-1d7cc1fe -LaunchScript C:\Antiphon\releases\task-1d7cc1fe\launch-rc.ps1 -LogRoot C:\Antiphon\releases\candidates\rc-2026-11-19-ab7af12c6990\logs -StateRoot C:\Antiphon\releases\state-1d7cc1fe -RunId 97150a0b03454892a10a086837fe8058'
Invoke-CimMethod -ClassName Win32_Process -MethodName Create -Arguments @{ CommandLine = $cmd; CurrentDirectory = 'C:\Antiphon\releases\checkout-1d7cc1fe' }
```

To observe it afterwards, use `pwsh -NoProfile -File C:\Antiphon\releases\checkout-1d7cc1fe\scripts\qualification\observe.ps1 -TaskRoot C:\Antiphon\releases\task-1d7cc1fe -StateRoot C:\Antiphon\releases\state-1d7cc1fe`. The supervisor log is `C:\Antiphon\releases\task-1d7cc1fe\supervisor.log`, and the run directory will be `C:\Antiphon\releases\candidates\rc-2026-11-19-ab7af12c6990\logs\2026-11-1x-hhmm-97150a0b`.

If this run id is ever consumed by a failed attempt, `start-supervisor` refuses to reuse it. Generate a new id, then edit `-RunId` in both the record and `$cmd`.

### Preflight (05:48:17Z, read-only)

| Check | Result |
|---|---|
| Checkout HEAD = `ab7af12c6`, clean, both markers, no alternates | PASS |
| `launch-rc.ps1` byte-identical to the cut | PASS |
| No `supervisor-identity.json` in the task dir; run id named in none of `last-run.json`, `run.lock`, `own-last-run.json` | PASS |
| `state-1d7cc1fe\run.lock` absent; `C:\Antiphon\nightly\run.lock` absent | PASS |
| `native-run.lock` absent for the new candidate; the rc-2026-11-18 lock was released | PASS |
| Seed digest | PASS |
| No live `supervise-rc`, `launch-rc`, `nightly-run` or `nightly-tests`; the 0b292fa4 supervisor `EXIT 1` / `SUPERVISOR-END` came at 05:26:58Z | PASS |
| No leftover test hosts | PASS. The 11 live `Antiphon.PtyHost` processes all run from `C:\logs\antiphon\session-runner\pty-hosts\bin\...`: they are the production runner's session hosts, not test leaks. |
| Docker | PASS (server 29.5.3) |
| `antiphon-postgres` | PASS (Up 36 hours, healthy) |
| AppHost `/api/version` and `/health` | PASS: answers `56e96eabf...`, `land-v2`, `operator-shutdown-v1`; `/health` 200 |
| AppHost on the cut | **FAIL, as expected**. It still serves `56e96eabf`. The preflight stays NO-GO until the orchestrator's restart onto `rc/2026-11-19` makes `/api/version` report `ab7af12c6...`. |
| Memory | PASS: 11.5 GB physical free of 31.9 GB; 21.3 GB commit free |
| Disk C: | PASS: 263.6 GB free |

One leftover is not ours to remove: a stale `native-run.lock` under `candidates\rc-2026-11-16-6f8741d872d6\coordination` (2026-11-11 11:41Z, run d88de419, killed by the reboot). It is a different coordination root, so it does not block this launch.

### Pending reboot (read only)

- **No OS reboot is pending.** CBS `RebootPending` and WU `Auto Update\RebootRequired` are absent. There is no computer-rename and no `PostRebootReporting`.
- `PendingFileRenameOperations` holds 11 delete-on-reboot entries from the Edge, OneDrive, Dropbox and gaming-services updaters. None is an OS servicing entry.
- The last boot was 2026-11-11T17:36:56Z, after event 1074 at 17:36:01Z (svchost, "Operating System: Service pack (Planned)").
- WU active hours are 08 to 17. Windows Update settings were not changed.

## 4. ETA

Planned 51.4 h × 0.329 (0b292fa4's elapsed/planned; ae84478d had 0.325) gives about **16.9 h**. Builds and discovery add about 10 minutes. A launch at time T is expected to end around T + 17 h, if no hang or idle kill adds time. A Full run executes every chunk; the seed only moves g82, then g118 and sr g4 and the 429 invalidated chunks, to the front.

## Authority

The operator wrote on 2026-11-11: "cut a new rc and then restart app host and server2". The standing rule of 2026-10-31 and 2026-11-02 is to cut a new rc from master after fixes and rerun on prior failures. That covered creating `rc/2026-11-19`, so no approval was sought. Tags, publish, promote and the launch itself were left alone.
