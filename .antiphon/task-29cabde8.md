# CARD-1614 plan re-review, round 2 (task 29cabde8)

Subject: `docs/superpowers/plans/2026-10-29-card-1614-linux-custody-repairs-plan.md` on
`origin/feat/card-task-35d0c3f1` tip `eb628a92742268190b764e2251e8a225ca743ab6` (plan task
`35d0c3f1-f355-49b9-82bd-3b93b31002eb`). Product code was read at that SHA. Reviewer: Claude Opus 5.5 on server2
(Linux). The review was read-only. It ran no product tests: only the checkpoint importer and source reads.

**Verdict: NOT SOUND YET. Return to Plan.** The round-1 items D-2, D-3 and D-5 are closed, D-4 is mostly closed, and
every advisory is closed. D-1, the heart of the review, is not closed. The revised linger rule withholds host disposal
for **every** tracked execution until an accepted receipt arrives, and that has three consequences:

- It removes Windows' only existing release, on the one platform that advertises custody today.
- It leaves the plan's own new permanent-Unknown states with a host that can never exit.
- It leaves a live but unadoptable host with no releaser.

The per-container root also has an unowned provisioning and boot-failure gap.

## Defects

### D-1: The withheld-linger rule has no terminal disposition for Windows, for permanent Unknown, or for an orphaned live host
- **Where:** Plan §P-3 bullet 5 (line 72: "Withhold automatic linger disposal while tracked custody lacks an accepted
  final receipt"), the release-ownership paragraph (line 74: "Windows … its existing explicit cleanup owner remains
  responsible for release"), the failure disposition (line 78), lost-host recovery (line 76) and fail-closed natural
  zero (lines 84–90).
- **Code:**
  - `PtyHostOptions.cs:26` sets `LingerTtl` = 24 h. `HostSession.cs:400-402` says the linger "must never make this host
    immortal".
  - On Windows, the tracked job is kill-on-close (`ModernConPtyConnection.cs:741`, enforced at `:262`), and the job
    closes when the host is disposed (`docs/session-runtime-invariants.md:1186`).
  - Windows explicit seal does not terminate (plan line 71; `HostCustodyTests.Live_job_seals_without_killing…`).
    `VerificationCleanupService.cs:14` says cleanup "never stops or kills".
  - The runner sends Shutdown only after a receipt (`SessionRunnerRuntime.cs:4495-4516`, polling every 1 s). It refuses
    to remove an unfinalized tracked host (`:4365`, `:4382-4383`). It never kills an unadoptable tracked host
    (`:2045`).
  - Ordinary journal states with no receipt: `HostCustodyJournal.cs:86-87` (unsupported), `:88-91` (failure after
    native intent), `:104-105` (host output drain failed).
- **Failure:**
  - **(a) Windows (production today).** On desktop, the only runner advertising `verificationCustodyV1`, a Mutation
    session's root exits and leaves a hung child, for example a stuck `dotnet test`. Today the 24 h linger disposes the
    host, and KILL_ON_JOB_CLOSE kills the child. With the plan's change the linger is withheld. Cleanup's explicit seal
    on Windows does not terminate, and runner recovery is Linux-only. The host and child then live forever, and
    cleanup returns Draining/Unknown residue on every retry. No witness covers this: CP-30 is untracked linger only,
    and the linger witnesses are native Linux only.
  - **(b) Permanent Unknown.** In the plan's own `C1614_Failed_tracking_explicit_release_terminates_without_receipt`,
    the root is reaped before `RecordTracking`. Cleanup's seal kills the retained container and observes zero, but by
    design it returns Unknown with no receipt. Under line 72 the host is never disposed. The runner's shutdown loop
    also waits for a receipt, so the PtyHost and the runner's 1-second poll both live forever, and repeated cleanup is
    idempotent. Several other states never reach an accepted receipt and end the same way:
    - refused placement and missing tree (lines 84–90);
    - host output drain failure;
    - a Linux or Windows **unsupported-backend** launch failure (the V-13 inbox path).

    Line 78 ("no automatic linger expiry may abandon a *possibly started* tracked tree") contradicts line 72, so Code
    can implement either rule.
  - **(c) Orphaned live host.** Runner restart cannot adopt a live tracked host, for example a workspace conflict at
    `:2040` or a broken pipe. `:2045` deliberately does not kill it. Line 76's recovery requires the host PID to be
    gone, so it refuses. The linger is withheld, so the host and its descendants have no releaser. Today the 24 h
    linger bounds this. The 30-minute Draining warning (line 80) is never produced for an unreachable host.
- **Why:** The invariant is that a release must leave the process killed, pooled warm or owned. The brief asks for "the
  bound and its owner". Line 74 names an owner that cannot terminate on Windows, and in (b) and (c) no owner can end
  the host at all.
- **Fix:**
  1. Write an explicit host-disposition matrix. Scope the withheld linger to the Linux cgroup backend. Keep Windows'
     linger with kill-on-close as its documented release, or add a real Windows termination on runner seal. State
     which one, and correct line 74.
  2. Pre-native and unsupported failures keep today's linger.
  3. For a possibly started Linux tree, the host stays until either an accepted receipt arrives or an explicit release
     has observed zero from the retained container. After that the existing linger/shutdown resumes, and the
     execution stays Unknown with no receipt.
  4. Name the owner for an orphaned live host. Either write an operator procedure (verify the host PID/start identity,
     stop it, then rerun cleanup so recovery runs) or allow an identity-verified explicit release. Extend detection to
     an unresolved Unknown after a runner seal.
  5. Add red-first witnesses:
     - host exit after release in `C1614_Failed_tracking…` and `C1614_Missing_tree…`;
     - a Windows tracked-linger case in CP-22 or a new Windows row.

### D-2: Per-container custody root: volume provisioning is unowned and boot-failure semantics are unspecified
- **Where:** §Native scope, "Root isolation" (lines 104–106); slice S2b (line 148).
- **Code:**
  - `dind-entrypoint.sh:7` runs `set -euo pipefail`, and `:23-26` refuses with exit 3.
  - `docker-compose.server2-runner.yml`, `docker-compose.server2-runner.temp.yml` and `docker-compose.runner-grok.yml`
    declare no custody volume and are not in any slice.
  - `scripts/c590-remote.sh:9780` and `scripts/verify-card0604-dind-runner.ps1:312` count residue directories one
    level under the old global root.
- **Failure:**
  - The rollout's "upgrade old" phase recreates the container, and compose reuses its named volume. The upgraded
    container then reads the previous namespace GUID and acquires the old trees. That contradicts "reuse only for
    restart of the same container identity" and "replacement cannot silently acquire old trees". No code enforces
    exclusivity.
  - The plan does not say whether a failed namespace initializer (missing or foreign-owned volume, mode mismatch)
    refuses the entrypoint. If it does, an rc rollout of the image takes the server2 runner down even though Linux
    custody is unadvertised.
  - The verifiers would read namespace directories as execution residue.
- **Fix:** Either keep the namespace record in the container's own writable layer, which survives `docker restart` but
  not recreation, so no volume is needed, or record the creating container identity in the volume and mint a fresh
  namespace on mismatch. State that an initializer failure only makes custody unavailable, so the namespace file is
  absent and the helpers fail closed, and that it never blocks runner boot. Add the compose and verifier files to S2b,
  or state that they are unchanged.

### D-3: The owner doc still says cleanup never kills
- **Where:** Slice S4 (line 150) updates `session-runtime-invariants.md`, `testing-and-build.md`, the ADR and the
  `VerificationCleanupService` summary only.
- **Failure:** `docs/orchestration-loop.md:2608-2616` is the AGENTS.md owner for the CleanupVerification procedure. It
  states "cleanup never kills" and that a started attempt requires descendant-zero. The plan now makes
  CleanupVerification the release owner. On Linux it terminates (it already does through `PtyAgentRunner.cs:524`),
  and on Windows it does not. Orchestrators would follow the stale owner text.
- **Fix:** Add `docs/orchestration-loop.md` to S4. It should state the post-settlement obligation, the retry of
  residue, that Linux cleanup kills and Windows cleanup does not, and the lost-host Unknown outcome.

## Advisories (no Review rerun needed)
- **A-1:** Line 82 extends `LinuxCgroupCustodyTests.Lost_host_leaves_unknown_not_receipt` with "exact-container recovery
  semantics". `Antiphon.PtyHost.Tests.csproj:20-23` does not reference SessionRunner, so runner recovery cannot be
  exercised there. Limit the host extension to no-release survival; NP-37 already owns recovery.
- **A-2:** V-17 has no closed-list witness, only NP-34 and NP-37. Its refusal arms (mismatched ContainerId or
  namespace, a live host PID, no `runner-seal.json`) are portable through a kill-helper seam. Add them to
  `RunnerCustodyReleaseTests` with a red row so that Code's S3 recovery code is tested in the closed list.
- **A-3:** CP-32's "caller-held maintenance exclusion" has no mechanism. Define it concretely: no runner restart or
  rollout on either server2 runner during the row, because each runner probes once per process
  (`SessionRunnerRuntime.cs:196`).
- **A-4:** Retained empty execution trees (line 88) accumulate with no named remover. Name one, for example after
  cleanup's receipt import.
- **A-5:** `origin/rc/2026-10-27` is now `ade54222` (top-up 37), so the C478_V14 guard CP-37 needs is on rc. Base Code
  on it.
  - `git merge-tree` against rc is clean.
  - Against `origin/master` `0e7dd390` there are exactly the four unrelated conflicts the plan lists at line 308:
    - `.antiphon/task-af2b78c8.md`
    - the card-1584 investigation
    - `CapacityRecoveryService.cs`
    - `CapacityLockOrderTests.cs`

## Round-1 closure (verified)
- **D-1, partly closed:**
  - The owner exists: `VerificationCleanupService.cs:61` is the only `seal:true` caller in `server/`+`src/`, reached
    only from `POST /api/agent-tasks/{id}/cleanup-verification` or `delegate.ps1 -CleanupVerification`. It is admitted
    only for terminal Mutation/SourceLanding tasks, so it never auto-kills a Working session.
  - The early return on the first Unknown cannot strand a later execution. `VerificationExecutionService.cs:24`
    refuses a new reservation while an earlier one lacks a receipt.
  - Failure paths (a), lost host, and (b), root reaped before `RecordTracking`, are designed (lines 76 and 78).
  - (c), launch failure: see D-1(b) above.
  - Long Draining is reported only (line 80).
  - No automatic release bound exists by design (lines 74 and 96). The owner is the orchestrator.
  - The gaps are D-1(a–c) above.
- **D-2, closed:** lines 84–90 cover a missing, unreadable or malformed tree, which throws and becomes Unknown, plus a
  placement observation and a present-but-never-placed arm, PC-8 and red rows CP-34/NP-34. That is precise enough.
- **D-3, closed:** line 54 specifies content reads for all four `-s` sites and removes `|| true`. Lines 56–58 give a
  deterministic failed-drain witness (NP-35) and PC-9. V-8 can go red through PC-2 and the managed failed-drain arm.
- **D-4, mostly closed:**
  - Advertisement is enforced in code, not by convention. `DetectCustodyBackend` (`SessionRunnerRuntime.cs:78-81`)
    gains the default-false `SessionRunner:LinuxCustodyQualified` plus a v1-only probe (line 102).
  - No server2 configuration sets it, so server2 cannot advertise by accident. The live catalogue still shows only
    desktop advertising.
  - The namespace path and probe lock are concrete (lines 104–108). For the provisioning gap, see D-2.
- **D-5, closed:**
  - Installed-helper probe → CP-32.
  - Natural completion → NP-13.
  - Owned layout → CP-33.
  - Every other new test has a red row: CP-9/10/34/35/36 and NP-34/35/36/37.
- **Advisories A-1 to A-8, closed:** lines 64, 68, 84, 90, 46, 297–298, CP-37, 120–122 and 308.
- **Checkpoint table:**
  - The importer gives `imported 31 rows` with four intentional duplicate-build advisories (CP-22/23/28/29).
  - Estimates sum to 56 minutes. The NATIVE-PENDING list has 11 rows and 28 minutes.
  - Counts verified against the source (executions include `[Arguments]`):

    | Row | Count |
    |---|---|
    | CP-18 | 5+1 = 6 |
    | CP-19 | 1+5+20 = 26 |
    | CP-20 | 7+17+2 = 26 |
    | CP-21 | 6+5+1 = 12 |
    | CP-22 | 17+1 = 18 |
    | CP-23 | 30 |
    | CP-29 | 23+2 = 25 |
    | CP-37 | 3 pass + 14 skip |
    | NP-14 | 8+5 = 13 |

    CP-37's 3 passes are G213 and Legacy_peer×2. The 14 skips are 7 `RequireModern`, 5 C478 wrappers, Inbox and V14.
- **Debug timing review:** the obligation is stated (line 177).
- **Final-review limitation:** stated plainly (lines 3 and 116). Final cannot be Full while NP rows are owed. The caller
  owns candidate qualification, and an operator decides whether to fund v2 or remove it.

## Commands run
- The tool build and the importer ran through `scripts/build-slot.ps1` (leases `be8771c5…`, held 7 s, and
  `5a2bb097…`, held 3 s):
  `dotnet run --no-build --project tools/Antiphon.Checkpoints --property:OutputPath=bin-c1614rev2/ -- import --plan <plan> --out .antiphon/evidence/card-1614-rev2/manifest.yml`.
  The manifest is ignored.
- The two `bin-c1614rev2` directories were removed by literal path.
