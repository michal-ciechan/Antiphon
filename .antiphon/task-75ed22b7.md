# CARD-1614 plan review (task 75ed22b7)

Subject: `docs/superpowers/plans/2026-10-29-card-1614-linux-custody-repairs-plan.md` on
`origin/feat/card-task-9123be00` tip `d02342badebf24382ac2169e91c750cbe32332c1` (rc-based: `origin/rc/2026-10-27`
tip `8edda9996` is an ancestor). Product code read at that SHA. Reviewer: Claude Opus 5.5 on server2-temp (Linux,
kernel 4.15.0-213, cgroup v1). The review was read-only. It ran no product tests: only the importer and read-only
cgroup and fleet reads.

**Verdict: NOT SOUND YET. Return to Plan.** The three root causes are real, and the minimal fixes for P-1 and P-2 are
right. The P-3 design leaves no owner and no bound for live descendants. It has no release path once the host is
lost. It finalizes on a fail-open "missing tree == empty" read. The helpers carry a further dead population check the
plan says to preserve. Two of the native lanes have no host on this fleet. Three new tests have no red-first row.

## Defects

### D-1: P-3 leaves live descendants with no named releaser and no release path after host loss or launch failure
- **Where:** Plan §P-3 bullet 5 (linger), D-2, §Risks bullet 5. Code: `HostSession.cs:174` (tracked launch-failure
  `StartLingerExpiry`, not named by the plan), `:402`/`:445`; `HostCustodyJournal.cs:83-91` (`_failure` with native
  intent returns Unknown **before** `SealAndObserveCustodyAsync`, even on an explicit seal);
  `SessionRunnerRuntime.cs:2050-2056` and `:4472` (with the host lost, the runner returns `ReadUnavailable`. A runner
  seal only writes `runner-seal.json` and nothing terminates). In-tree callers: nothing on rc `8edda9996` or master
  `0dbbbe490` constructs `RunnerLaunchRequest.VerificationBinding`, calls `POST /sessions/{id}/executions/{eid}/seal`,
  or sends PhoneHome `ReadCustody{Seal=true}` (grep over `src/` outside SessionRunner and PtyHost: no hits).
- **Failure:** (a) Payload `setsid long-running-daemon & exit 0`. After the fix the linger timer is withheld, nothing
  in the tree ever issues a release, and the host and its descendants live without bound. (b) The PtyHost crashes
  (OOM or a bug) mid-linger. The descendants stay in the **host-global** custody cgroup (see D-4) with no observer.
  An explicit seal returns Unknown and no code path can kill them, so they leak forever. Today the auto-seal limits
  this window to about 1 s; after the fix it is unbounded. (c) A fast-exiting root is reaped before
  `HostCustodyJournal.RecordTracking`'s `Process.GetProcessById` (`:49`). The launch catch then records `_failure` with
  native intent, and an explicit release can never reach `Terminate`. After 24 h, `:174` makes the host exit and the
  container leaks.
- **Why:** Session-runtime invariant: a process release must leave the process killed, pooled warm or owned by a
  standing agent. The brief asks what bounds the linger and who releases it. The plan answers "explicit release
  remains available", but no in-tree caller performs one, and it is unreachable after host loss or launch failure.
- **Fix (plan text plus witnesses):**
  1. Name the release owner, which is the verification consumer, and state that tracked custody stays unadvertised
     until that consumer exists. Otherwise add the releaser to scope.
  2. Add a runner-side release after host loss. With `runner-seal` present and the host gone, invoke the kill helper
     for the recorded `host-identity.json` ContainerId and observe zero. The state stays Unknown and never becomes a
     reconstructed receipt, but the processes are killed. Extend `Lost_host_leaves_unknown_not_receipt` to cover it.
  3. Let an explicit release terminate a known container even when `_failure` is set with native intent, and give
     `HostSession.cs:174` an explicit disposition.
  4. Surface long-Draining tracked custody as detection (census or attention), never as an automatic kill.

### D-2: Natural-zero finalization consumes a fail-open read
- **Where:** `IPtyCustodyContainment.cs:126-130`. `ReadTreePids` returns `[]` when the tree directory is absent; per
  its comment, "gone after a successful kill is an empty tree". Plan §P-3 bullet 3 (natural zero) now consumes that
  read **without** any kill.
- **Failure:** The enter helper refuses before placement (`CustodyRootMissing`/`CustodyTreeUnavailable` exit 3), or
  the hierarchy/version detection disagrees, so the tree directory is absent. The sudo root exits, an ordinary read
  sees zero, and the host persists an `Exited`, zero-process receipt for a payload that never ran in custody.
- **Why:** Brief item 3: a missing or unreadable custody record must refuse or keep custody, never seal.
- **Fix:** Natural zero requires the tree directory to be present at the read (or a recorded placement
  acknowledgement). A tree missing before a recorded termination is Unknown. Add a native witness in which the tree is
  absent or placement is refused: no receipt.

### D-3: The helpers' population checks are dead (`[ -s cgroup.procs ]`), and P-2 says to preserve them
- **Where:** `antiphon-custody-kill.sh:61,67` (`drain`), `antiphon-custody-enter.sh:67,82` (`CustodyTreeNotEmpty`).
  Plan §P-2: "Preserve validation, freeze/kill/thaw, deadlines". The plan's own cleanup step 2 says `-s` is not
  population evidence.
- **Evidence:** On this host `stat -c %s /sys/fs/cgroup/pids/cgroup.procs` gives `0` while the file holds 47 PIDs.
- **Failure:** After the `id` fix, `drain` returns 0 immediately and the helper prints `C604_CUSTODY_KILL_OK`
  without waiting. The 30 s deadline never applies. A v1 fork that escapes the kill (for example when the freezer tree
  is missing) still reports success. Enter never refuses a populated tree. V-8's "successful termination" (helper exit
  0) cannot go red for a drain failure.
- **Fix:** Use content-based population checks, with a read failure meaning refuse or nonzero exit. Add a native
  witness that a live member defeats `KILL_OK` within the deadline, and a PC row that restores `-s`.

### D-4: The native-lane prerequisites are not executable on this fleet as written
- **Where:** §Verification "Execution protocol"; CP-11–17 and CP-15/17; §Performance "Candidate-image provisioning…
  unknown"; D-8.
- **Evidence:** `GET /api/session-runners` lists `desktop` (windows, advertises `verificationCustodyV1`), `server2`
  (linux, draining) and `server2-temp` (linux). Both Linux runners are server2: kernel 4.15, cgroup v1. **No cgroup v2
  host exists**, so CP-15/17 can never run and a Final review can never be Full. Rows after S2 need the candidate
  helpers at the hardcoded `/usr/local/bin` paths with the new sudoers. The standing containers ship the old helpers,
  and the plan rightly forbids installing into them. Nothing specifies where or how the checkpoint tool runs inside a
  candidate image (nested docker is present: group `docker-nested`). This container shares the **host cgroup
  namespace** (`/proc/self/ns/cgroup` = `4026531835`; `/sys/fs/cgroup/pids/{docker,antiphon-custody}`). The custody
  root and the fixed `PROBE_ID …c604` are therefore host-global, shared by server2, server2-temp and any candidate
  container. Concurrent probes mkdir/rmdir the same directory, and candidate tests write into the production custody
  root.
- **Fix:**
  1. v2: either name a provisioned v2 lane, or fail closed by making the probe/advertisement accept only qualified
     v1, and move CP-15/17 out of the required set with an owner. That is a caller or human decision if no lane can be
     provisioned.
  2. Candidate lane: specify the procedure. Build the image from the exact SHA, run the checkpoint tool inside it with
     the SDK, a build slot and cgroup access, and name the owner.
  3. Isolate candidate runs from the production custody root and probe ID, or state the sharing and serialize it.

### D-5: Three new tests have no red-first row (checkpoint manifest rule: a new test that cannot go red is a stub)
- `LinuxRunnerCustodyLifecycleTests.C1614_Installed_helpers_advertise_custody` is red today against the installed
  helpers and can run here in S1. Add it to the S1 group.
- `…C1614_Natural_completion_accepts_exact_receipt_and_allows_shutdown` is red after S2: the runner's
  `HasExited` auto-seal kills, so `TerminationSucceeded` is present. Add it to CP-13.
- `LinuxPtyHostLauncherTests.C1614_Owned_payload_has_native_apphost_and_matching_library` is red on the current
  staging. Add it to S1. "Accepted by CP-18 later" is green-only.

## Advisories (no Review rerun needed)
- A-1: Line drift. `SessionRunnerRuntime.cs:4480` is actually `:4477`. `HostSession.cs:404,424` is actually
  `:402` (linger), `:405` (observer start) and `:433` (observer read).
- A-2: A latent third auto-seal exists. The unused 3-argument `HostCustodyJournal.ObserveAsync` (`:72-74`) defaults
  `seal=true`; remove it in S3.
- A-3: Today `_seal is not null` routes every later read through `Terminate()` (`HostCustodyJournal.cs:97-101` →
  `PtyAgentRunner.cs:524`). Natural zero persists the producer seal, so key termination to the runner seal alone.
  Otherwise a failed receipt persist means the next ordinary read kills, and `TerminationSucceeded` appears.
- A-4: Windows natural completion currently relies on the removed `Status == Exited` escalation. Its witnesses are
  `HostCustodyTests.cs:137-149` and `:156-165` (`exit /b 0`, then ordinary reads), which are in CP-22. Name them, and
  state that natural zero is cross-platform. Desktop is the only runner advertising custody, so Windows is the path
  this change actually reaches.
- A-5: The image ships sudo `1.9.13p3`, but the plan cites the 1.9.14 manual. Also confirm `log_exit_status`,
  `intercept` and `log_subcmds` are off in the effective policy, since they are direct-exec preconditions.
- A-6: Residue (read-only check). All three listed GUIDs **plus the probe tree
  `00000000-0000-0000-0000-00000000c604`** exist in both v1 hierarchies. They are root-owned, each has exactly one
  `tree` subdirectory, and the parent and tree `cgroup.procs` are empty. The failing kill probe leaves the probe tree,
  which self-cleans after P-2. The procedure is safe: literal paths, nonrecursive `rmdir`, which the kernel refuses
  on a populated cgroup, and an ownership/provenance gate. Because the root is host-global, that provenance gate is
  the real protection.
- A-7: Ordering and ports.
  - `git merge-tree` against `origin/feat/card-task-2e444201` (rc top-up 37, `ade542225`, C478_V14 Linux skip at
    `HostCustodyTests.cs:46`) is clean. It is a different method from V-13's inbox guard and has no policy-file
    overlap, so landing order is free.
  - Against rc the merge is clean. Against master, rc-vs-master divergence conflicts in
    `tests/test-execution-policy.json` and `scripts/test-rc-platform.ps1`, which S4 edits. The master port needs a
    fresh hash and census.
  - No row runs `HostCustodyTests` on Linux. Add a Linux row so V-16's "OS exclusions exact" is witnessed for both
    the inbox guard and C478_V14.
- A-8: The blast-radius statement is honest. Sharpen it with D-1's finding that no in-tree producer or seal caller
  exists on rc or master, and that the server2 catalogue omits `verificationCustodyV1`. That was re-confirmed at
  about 19:55 UTC.

## Verified as sound
- **P-1:**
  - `PtyAgentRunner.cs:197-199` records Porta's PID.
  - `IPtyCustodyContainment.cs:93-98` wraps the payload in `sudo -n enter`.
  - `enter.sh:93,99` writes `$$` and `:163` execs setpriv.
  - The sudoers file sets `!use_pty`/`!pam_session` but leaves `pam_setcred` on.
  - `!pam_setcred` matches the sudoers direct-exec conditions. Rejecting PID guessing is right.
- **P-2:** Reproduced with bash 5.2.15: `set -u; f(){ local id="$1" dir="/x/$id"; }; f abc` gives
  `id: unbound variable`, exit 127. Only `kill.sh:72,91` and `enter.sh:57` have the shape. No other `set -u` hazard
  exists: `APP_UID`/`APP_GID` are defined, `${1:-}` is guarded, and every positional is `$#`-checked.
- **P-3:**
  - Both escalations are real: `HostSession.cs:332` (also driven by the internal observer at `:433`) and
    `SessionRunnerRuntime.cs:4477`, driven by `ObserveCustodyThenShutdownAsync`.
  - `PtyAgentRunner.cs:524` is the only `Terminate` caller.
  - No path kills a Working (root-alive) session, and after the fix no automatic kill remains.
  - The planned receipt order (fence → seal → read → drain → re-read → persist; nonzero → Draining) matches the code.
- **Checkpoint table:**
  - The importer reports `imported 31 rows` with 6 duplicate-build advisories, all intentional cross-lane roots.
  - Estimates are whole minutes, and their sum of 64 equals the per-lane table.
  - The Debug timing-review obligation is stated.
  - Counts verified against the source (executions include `[Arguments]`):

    | Row | Count |
    |---|---|
    | CP-14 | 8+2 = 10 |
    | CP-18 | 5+1 = 6 |
    | CP-19 | 1+5+20 = 26 |
    | CP-20 | 7+17 = 24 |
    | CP-21 | 6+5 = 11 |
    | CP-22 | 17+1 = 18 |
    | CP-23 | 30 |
    | CP-29 | 23+2 = 25 |
    | CP-24–26 | 1 each |

## Commands run
- Importer, through the build slot (lease `33cc0f5d…`):
  `dotnet run --no-build --project tools/Antiphon.Checkpoints --property:OutputPath=bin-c1614rev/ -- import --plan
  <plan> --out .antiphon/evidence/card-1614-review/manifest.json`. The first attempt with an out-of-root `--out` was
  refused with `outside-root`.
- The two `bin-c1614rev` directories were removed by literal path.
