# Release gates: Interim rounds, RC profile and published releases

Owner document for CARD-0599. It covers the credit split between the nightly master lane
and the release-candidate lane, the RC coverage profile including automated E2E, CalVer
tags and GitHub Releases, and the dormant Interim activation configuration.

> **Deployment status.** Annotated tags `v2026.10.15.1` (peels to
> `067b47e2dc903247f1a7c8aa539032cc4266e4ad`) and `v2026.10.15.2` (peels to
> `6ca859d006b0a3cac9a88dc6bd551c37e98b2217`) exist. Both are historical direct finals;
> do not invent `-rc.K` parents for them. The Windmill RC job is not registered, its
> schedule stays disabled, and the scheduled lane is not qualified. There is no
> qualification receipt and no Interim pilot. `InterimVerification.Enabled` ships
> `false`. CARD-0599's operating acceptance stays open until the S5 and S6 receipts
> exist. Do not treat a checked-in definition file as registration evidence.

Related owners: [testing and build](testing-and-build.md), [nightly watchdog](nightly-watchdog.md),
[orchestration loop](orchestration-loop.md), [Windmill definitions](../scripts/windmill/README.md),
[HTTP operations](ops-http.md), [AppHost runbook](apphost-runbook.md).

## The release line

The release line is a long-lived `rc/YYYY-MM-DD` branch. The current line is
`rc/2026-10-18` (it succeeded `rc/2026-10-15` on 2026-10-18). Bug and test fixes for code already on that line land on it.
Features and refactors, including CARD-1311, are master-only. Every rc-line land
is merged to master with a merge commit. That merge commit is an explicit
exception to rebase-only landing, and `-Land` does not create it.

Releases are cut from the rc line. `publish-release.ps1` writes
`vYYYY.MM.DD.N-rc.K` and a GitHub pre-release. `scripts/promote-release.ps1`
writes the final `vYYYY.MM.DD.N` at that same peeled SHA. Tags are immutable:
they are never deleted or moved. Legacy `release/rc-YYYYMMDDTHHMMSSZ` refs remain
valid until a later card removes them. New cuts do not allocate a stamp.

AppHost and server2 run the current rc tip, never master. A feature release from
master is at most once per UTC day, and only after the current rc line is fully
fixed and those hosts are running that tip. This card does not implement that gate.

Change-aware heavy TUI selection is CARD-1313 on master. The rc profile, the
credit split, the nine-suite roster, and the rule that RC green is not master
readiness stay as written below.

### Merging the rc line into master (Merge role)

The rc-to-master merge, and the merge of a reviewed fix branch into the rc line, is a
`Merge`-role Worktree task, not a `-Land`. What that task needs: dispatch it with `-StartRef` at
the **target's** current tip (the master tip for an rc-to-master merge; the rc tip when a fix
branch is merged into the rc line), have it `git merge` the source on its own assigned task
branch, and have it publish with `git push origin HEAD:refs/heads/<target>`. That push is a
fast-forward because the task branch began at the target tip and the merge commit descends from
it. The flow starts at the target tip, not at the Code branch's tip, so that the result
fast-forwards the target; a Merge task never starts from the source side. The 2026-10-19 cycle's worked
examples: `feat/card-task-5d75d6f6` started at master, merged `df6b422c71b0` and master
fast-forwarded to `d5dec9555bd726ad11bb30073c1733d6b3e7bb0e`; `feat/card-task-a7accc73` started
at the rc tip `df6b422c71b0`, merged the reviewed smoke-pin branch `feat/card-task-c8c9f3c5`, and
`rc/2026-10-18` fast-forwarded to `81cfb267a0af5c0ddb65e8b238af355067bee9cc`. The merge commit is
the explicit exception to rebase-only landing named above; an ordinary `-Land` rebases and
cannot produce it.

## Two lanes, two kinds of credit

Master remains the integration branch for features, and its scheduled readiness
contract is unchanged. The rc lane tests one pinned SHA of the long-lived
`rc/YYYY-MM-DD` line; a legacy stamp ref remains valid. The two lanes earn
**different credit** and write **different files**.

| | master lane | RC lane |
|---|---|---|
| Profile | `nightly` | `rc` |
| Trigger | `scheduled` | `rc` |
| Ref | `master` / `origin/master` | `rc/YYYY-MM-DD` (legacy `release/rc-YYYYMMDDTHHMMSSZ` still accepted) |
| Credit kind | `master-scheduled` | `rc-release` |
| Green written to | `C:\Antiphon\nightly\last-complete-green.json` | `C:\Antiphon\releases\candidates\<candidate-id>\complete-green.json` |
| Earns | master daily readiness (Interim admission) | permission to publish one release |

`Test-NightlyCompleteGreenPredicate` in `scripts/lib/nightly-run-impl.ps1` takes the credit
kind it is being asked about and defaults to `master-scheduled`, so every pre-existing caller
keeps its original meaning. The decision itself lives in `Get-ReleaseGateCreditVerdict`
(`scripts/lib/release-gate.ps1`). Both kinds require:

- `coverageComplete`, `testsPassed` and `reportDelivered` as **real booleans** - a JSON
  `"true"` string earns nothing;
- `exitCode` 0, a non-empty `runId` and a non-empty `policyHash`.

`rc-release` additionally requires a valid candidate ref, a full 40-hex `sha`, an
`expectedSha` equal to it, and a candidate id. Any other profile/trigger/ref combination
earns **nothing**; there is no default kind and unknown combinations fail closed.

Consequences that are deliberate, not incidental:

- **RC green never substitutes for master readiness.** A green SHA on the rc line,
  including an older stamp candidate, says nothing about master. An RC success cannot
  repair a failed, missing, stale or overdue scheduled master run.
- **RC failure does not revoke a healthy master green.** It blocks that release only.
- An RC run refuses to start against the master state root at all, and an RC run leaves
  `last-run.json`, `last-complete-green.json`, `last-monitor.json` and
  `interim-qualification-receipt.json` byte-identical.
- The once-nightly London due-date / 08:00 readiness model and the watchdog timing model
  are unchanged.

## Serialising the two lanes

Native work is expensive and must never overlap. Both entrypoints acquire one shared
verification lock at `C:\Antiphon\verification\native-run.lock` (overridable with
`-CoordinationRoot`, which the tests use for a private root) in the same order, before any
clone, build or test. Owner identity is PID + process start + run id + continuation id, and
it is carried across the self-reexec hop so a hop proves inheritance rather than stealing.

A live owner is **never** stolen on age. On contention a scheduled RC writes a
`deferred-busy` attempt, returns non-green and waits for its next slot; there is no retry
queue. The master schedule keeps its existing missed-run and readiness consequences - an RC
is never killed to make master green. Assembly-local `ParallelLimiter<ProcessSpawnLimit>`
limiters are per test project and are **not** cross-process locks; this file is.

## Inspecting and stopping a qualification

The repository provides a read-only observer. Populate every variable from the
specific launch record; do not select the newest directory or infer the state root
from the candidate. Run on the **execution host**: PID observations on copied state
describe the wrong host. No automatic stop command is supplied; that remains a
separate supervisor-ownership follow-up (CARD-1500 scope decision D-1).

```powershell
pwsh -NoProfile -File scripts/rc-qualification-status.ps1 `
    -CandidateId $candidateId -RunId $runId `
    -StateRoot $stateRoot -CoordinationRoot $coordinationRoot `
    -ReleaseRoot $releaseRoot
```

`ReleaseRoot` is optional; when supplied it reads only the selected
`candidates/<candidateId>/candidate.json`. `-PassThru` returns the JSON-shaped object
to an in-process caller. The other four inputs are required and nonblank. The
observer reads only `last-run.json`, `run.lock`, `native-run.lock` and that optional
journal, with a 1 MiB limit per record. It rejects linked/reparse paths and ancestors.
Each `inputs` entry reports its literal path and `ok`, `missing`, `malformed`,
`unreadable`, `unsupported-path` or `changed` outcome (`not-requested` for an omitted
journal root). Changed file identity/length/write-time during a read is incomplete.
These are independent snapshots, not an atomic filesystem transaction; concurrent
same-account writers are outside a diagnostic reader's authority.

`state` contains recorded identity, phase and timestamps, nullable exits and actual
boolean coverage/test/report flags. An early blank `sha` can use `expectedSha`.
`lastActivityAt` is a recorded transition, **not a continuous heartbeat**; its age
does not prove a hang or supply an ETA. `ownerObservation` distinguishes `alive`
(matching PID and start), `absent` (definitive PID not found), `reused` (different
start), and `unknown` (invalid identity or inaccessible probe). `alive` describes
only the recorded owner, never the supervisor tree. An unrelated shared lock stays
visible as `other-run`; it is not this run's liveness. Missing candidate journals
do not hide direct diagnostic runs. `identityMismatches` names contradictions.
Exit **0** means a coherent observation, including a recorded failed run; **2**
means invalid inputs or an incomplete/contradictory observation. Neither exit,
`observationComplete`, nor an absent owner grants release credit or cleanup authority.

Manual emergency stop and stale shared-lock recovery:

1. **Identify and preserve.** Obtain candidate, full SHA, run ID, exact state,
   coordination and checkout paths, and supervisor PID/start/script from launch
   evidence. Run the observer on that host. Preserve exact state and lock bytes and
   a restricted local process inventory under the operator's evidence root. Keep
   command lines and credentials private. If launch identity is incomplete, stop
   this procedure and collect evidence; do not guess a supervisor from a lock PID.
2. **Prove the tree.** Use `Get-CimInstance Win32_Process` and creation times to trace
   the recorded nightly lock owner through its wrapper/build-slot ancestors to the
   launch-record supervisor. Check candidate/run/checkout arguments at the nightly
   hop, creation order and descendant membership. A script name, path substring,
   cached PID or heartbeat alone is insufficient. Reused PID, missing ancestry,
   inaccessible identity or an unrelated child aborts the stop. Never kill all
   `pwsh`, `dotnet`, `conhost`, runners, or processes under a shared root.
3. **Stop the verified supervisor tree.** With exclusive operational control of this
   launch, reobserve supervisor PID/start immediately before the explicit command:
   `taskkill /PID $verifiedSupervisorPid /T /F`. Abort if the generation changed.
   This manual command is not an atomic ownership API. Never substitute the native
   lock owner's PID for the supervisor. Bound the subsequent census to **30 seconds**,
   checking every previously observed identity and any remaining candidate/run
   descendants. Timeout, unknown identity, surviving child or incomplete census
   means incomplete stop: retain locks. Do not repeat a kill against a reused PID.
   The command's exit code alone proves nothing.
4. **Clear only a proven stale shared lock.** This step also applies after an
   independently established owner exit. Prove the owner generation and this run's
   children are gone; retain unknown/reused identities for investigation. Serialize
   recovery with all other operators and hold new native starts through existing
   launch owners for this short window. Without that exclusive hold, abort cleanup.
   Re-read the exact `native-run.lock` identity and bytes against the preserved copy.
   Foreign run, changed file, malformed record or live/unknown owner aborts cleanup.
   Move only that exact file with
   `Move-Item -LiteralPath $nativeLockPath -Destination $uniqueEvidencePath`, using a
   **nonexisting destination on the same volume**, without `-Force`, recursion or
   wildcards. Keep the moved file as the receipt; verify its bytes against the
   preserved copy and verify the original path is absent before releasing the hold.
   The operational hold is essential: unchecked read/delete is not compare-and-swap.
   Failure retains evidence and does not authorize retry against a different lock.
   `Enter-ReleaseGateNativeLock` intentionally does not auto-reclaim stale locks.
5. **Record before any separately intended restart.** Keep stop time, reason,
   identities, before/after observations and retained lock path. Do not rewrite
   `last-run.json`, green files, journals or reports to manufacture a terminal
   result. `run.lock` has distinct existing reclaim rules; never delete both lock
   files as a pair. A later qualification is a new attempt with a new identity.
   Do not use `release-cut.ps1` as a diagnostic restart: it publishes on green.

## The RC coverage profile

`tests/test-execution-policy.json` is schema **v4**. It retains the v3 project
dispositions and selection catalog, and keeps `defaultSuites` as the compatible
nightly default alongside `profiles`:

- `profiles.nightly.requiredSuites` - seven suites. E2E and `markdown-pdf` stay out, and
  `OptIn` / `Explicit` still excludes exactly as before. **The default nightly lane is
  unchanged.**
- `profiles.rc.requiredSuites` - those seven **plus `e2e` and `markdown-pdf`**: `antiphon`,
  `session-runner`, `pty-host`, `agents-pty`, `messaging`, `client`, `scripts`,
  `e2e`, and `markdown-pdf`.

The selected profile is authoritative for required suites. An unknown profile name, or `rc`
asked of a schema-v1 policy, is a hard refusal rather than a silent fallback.
The whole policy, profile dispositions and exclusions included, is covered by `policyHash`;
a stale hash refuses.

`rc` means the full **automated Windows profile**: all Unit, Integration and Slow cases
eligible for this host, the native suites, disposable-broker messaging, client
build/lint/Vitest, the script census and automated E2E. It is *not* every test on every
platform. Explicitly outside it, each with a reason and an owner recorded in the policy:
live provider and headed canaries, separately approved distiller tests, QEMU SourceLanding
controls (CARD-0490) and Linux-only lanes (CARD-0590).

Schema v4 allows an exclusion's optional scalar `platform` to be `windows` or
`linux`; omission keeps its all-platform meaning. Values, reasons and owners are
validated before filtering, and platform participates in the existing policy hash.
Frozen release authority always resolves exclusions for **Windows**, both when
created and when validated from its pinned policy blob, regardless of coordinator OS.

The executor resolves its host OS once and applies that host's exclusions before
native selection and census. Linux `rc` execution is a platform diagnostic:
`coverageComplete=false`, `coverageScope=PlatformDiagnostic`, and reason
`windows-rc-obligations-outstanding` persist in summary and run state even if every
selected case passes. It cannot advance candidate or master green files or earn
release/readiness credit. Unknown host platforms refuse before build/test commands.

All **70** LandDelivery cases remain required on Windows: 53 in
`AgentTaskLandDeliveryE2ETests`, 12 in `DispatchBaseWarningDeliveryE2ETests`, and 5 in
`WorktreeRetirementDeliveryE2ETests`. Their three CARD-1323 exclusions apply only to
Linux because their fixture requires Windows ConPTY/native FakeGrok. These counts
describe the required roster, not an execution receipt. Linux results do not discharge
any Windows obligation; there is no cross-host receipt merger or new profile.

The three Linux-native SessionRunner classes `UnixPtyArgvAdmissionTests` (15 cases,
CARD-0863), `AgentPinPublicationTests` (6) and `AgentPinWorkspaceStoreTests` (3,
both CARD-0262) have Windows-only RC exclusions. All 24 cases remain required on
Linux; their Linux checkpoint manifests and separate Windows native obligations
are unchanged. `test-rc-platform.ps1 -Case LinuxSessionRunnerAdmission` checks the
source roster, both platform censuses and launch plans, and frozen Windows authority.
A runtime skip alone leaves required coverage incomplete, so this admission belongs
in the profile policy. These exclusions are dispositions, not execution receipts.

CARD-1415 keeps two Windows-only, method-scoped `antiphon` rows for cases that skip by design
there, because a launched skip leaves the census incomplete:
`C1213_RemoteSourceLandingKeepsLegacyIdentity` (POSIX runner paths) and
`a_git_timeout_fails_one_task_not_the_tick` (the sh hook does not delay `git worktree add` under
Git for Windows). Their sibling `C1213_SourceLandingKeepsLegacyIdentity` stays required. Since
CARD-1425 the nine CARD-1281 producer-pack cases (five `AdvisoryChangePolicyTests` correction cases
and `C1281_B1Authorization`, `C1281_B1ReceiptImport`, `C1281_B1CorrectionTransaction`,
`C1281_WiredRoundtrip` in `PipelineSpeedIntegrationTests`) run from the deterministic in-test
`C1281ProducerPackFixture`, whose inventory digest is pinned, and are required on every platform.
`test-rc-platform.ps1 -Case C1415_SkipExclusions` checks the shipped rows and that the pack cases
are launched on both platforms.

Rows whose reason starts with `Linux-only by construction:` are Windows-only exclusions for
tests whose skip is a POSIX mechanism (FIFOs, statx, `/proc`, setsid, unshare, Unix modes, or
POSIX sh). Linux keeps those cases required. Skips the qualification inventory treats as
Windows obligations (junctions, case-sensitivity, CARD-1199 unverified Windows behavior, and
opt-in) stay required and have no exclusion row.
CARD-1199 S-A keeps all thirty-five checkpoint identities required on both platforms.
`test-rc-platform.ps1 -Case C1199_WindowsRequiredRoster` compares the compiled S/T/O
roster, minus the existing Windows confinement sibling, with that required set and
pins the remaining runtime skips. It is a census contract, not native Windows
execution evidence. Ten shape mutations, eight tracked-baseline cases, and five
confinement cases stay runtime-skipped on Windows until their own slices. R-2
junction and custody qualification stays open.
`test-rc-platform.ps1 -Case LinuxOnlyWindowsExclusionPolicy` checks the shipped
rows and that those obligation gaps still leave coverage incomplete. The compiled
marker witness is `NativeLinuxPolicyWitnessTests` in each of the four suites.
The retired `-Case LinuxOnlyWindowsExclusions` refuses and names that witness.

#### CARD-1613 marker migration and maintenance

The [CARD-1613 plan](superpowers/plans/2026-10-29-card-1613-pinned-linux-skip-reflection-plan.md)
replaces that source-only verdict with compiled marker witnesses.

At migration, compare frozen pre-S1 and annotated S2 source once, retaining the ignored
audit inputs and receipt. Whole-file/helper digests belong only to that migration audit.
Permanent Unit witnesses compare compiled pinned markers with frozen reasons for migrated
signatures; they never digest live inventory files. Unrelated portable test edits require
no baseline refresh.

- A new non-legacy exclusion needs a reviewed Windows policy row, the exact pinned marker
  with a nonempty reason on every selected node, and passing reverse-coverage/policy checks
  in all four suites. It has no historical Windows reason and needs no migration-baseline
  entry. New tests under a marked class follow the same rule.
- An intentional reason change for a migrated signature updates the effective marker,
  retained runtime gate and frozen reason/hash in one reviewed commit. Record the owning
  card, affected signatures and exact old/new reasons. Run the four witnesses, R-14 reason
  variants, and any affected C1210/C1050 native check with its deliberately updated oracle.
  Keep original migration receipts historical; never regenerate the baseline during tests.
- A rename/removal of a migrated signature needs a reviewed identity mapping/removal in
  the same change. Missing identities otherwise fail; do not silently prune the baseline.
- A legacy exclusion change supplies a record-level before/after diff, owning card and
  reason. Confirm that none of the 45 witnessed identities moves into legacy, then update
  the reviewed legacy records and literal canonical digest together with policyHash.
  Run R-13 refusal variants, four suite witnesses and policy/census checks. The initial
  88-record count permits separately reviewed maintenance; a new reason/owner does not
  make an exclusion legacy. Never derive the expected digest from live policy at runtime
  or approve a mismatch with a hash-only edit.

The default/full `verify-dev-stack.ps1` smoke belongs on the Windows Aspire desktop:
its `antiphon-postgres` check concerns that desktop stack, not an isolated runner
database. Non-Windows full invocations, including `-SkipBrowser`, refuse with
`desktop-platform-required` before AppHost imports, jobs, Docker/network operations
or HTTP. `-ProbeListeningPorts` remains a separate portable listener-only probe;
explicit `-SimpleMode` retains its compose-stack behavior. Neither probe earns RC
coverage or substitutes for the Windows obligations above.

### OptIn is not exclusion in a profile lane

CARD-1561 assigns the 27 named `CardWorktreeMirrorCleanupTests` methods to the
Linux phone-home lane through a Windows-only method exclusion. Linux keeps them
required; separate Windows handle qualification remains an owed native obligation.
Future methods in that class remain required unless explicitly classified.

CARD-1566 assigns `RunnerGrokAdapterReadyTestsPty.C1011_real_fresh_worktree_ready_and_one_turn`
to its separately commissioned headed WQ-3 live-provider workflow. Its sibling
methods remain required. TUnit diagnostic export exposes metadata categories but
does not expose this method's `[Explicit]` execution attribute; the RC disposition
therefore names the method and its manual owner rather than inferring spend
authorization from an absent category. Neither exclusion is a pass receipt.

In the `rc` profile, `OptIn` alone does **not** exclude a discovery node. Only a declared
`profiles.rc.exclusions` row - class, or class plus named methods - excludes, and every row
must carry a `reason` and an `owner` or the policy refuses to load. Raw discovery categories
are preserved on the node and reported; they no longer decide.

The consequence is the point: a new or unclassified case **stays required** and fails the
coverage census rather than silently inheriting manual status. `Get-NightlyDiscoveryCensus`
gives every UID exactly one disposition and emits required/excluded counts plus a digest that
becomes a manifest artifact. A required skip, a missing UID, a duplicate chunk membership, a
missing TRX, an unknown outcome, **zero executed required cases** or stale evidence all block
release. A zero-required-UID run is never green in a profile lane.

### Automated E2E execution

`e2e` is a runnable suite disposition for `Profile=rc`; the nightly lane continues to leave
it out. Native ownership is derived from the suite definition (project + assembly), so `e2e`
serialises and produces TRX, discovery and execution diagnostics like every other native
suite, rather than needing a second hard-coded id list.

Before any E2E chunk runs: npm ci / build / lint at the pinned SHA, the E2E project build,
`playwright.ps1 install chromium` from the generated script, and a client bundle present.
A missing browser, unavailable Docker, stale bundle, fixture failure or unavailable owned
runner is **red or incomplete, never a skip**.

E2E uses the existing `AntiphonAppFixture`, `PlaywrightFixture` and `IsolatedSessionRunner`:
the app's runner endpoint is its own loopback random port, never production 17204; the
database is disposable; scratch repos are owned. Headed and live environment variables stay
cleared, as do inherited task tokens, production API overrides and distiller approval paths.
Normal E2E must not reach a live broker or an authenticated provider to satisfy coverage.

Replaces the earlier claim that `-Suites e2e` runs E2E: it did not. `nightly-tests-impl.ps1`
skipped `mode=manual` unconditionally and `$NightlyNativeGroups` omitted E2E, so the
selection was accepted and then dropped. Use `-Profile rc`.

### The gate accepted for rc/2026-10-18 (CARD-1350)

`rc/2026-10-18` was accepted at `df6b422c71b0828b4bd7f855a86e2a51d1987e79` on a narrower standard
than `rc-release` complete-green, because no Windows rc run has yet produced that receipt. The
standard, recorded here so the next line is judged the same way: the frozen Linux CARD-1287
roster green on server2 (373 classes, 4,528 executed and passed, none failed or skipped;
[Linux final](investigations/2026-10-19-rc-linux-final.md) on `feat/card-task-aaee7f74`), plus
every Windows rc-profile suite that completes to a TRX: session-runner, pty-host, agents-pty,
messaging, client, scripts and markdown-pdf, with each red classified inherited from
`rc/2026-10-15` or flake and none new ([final Windows re-gate](investigations/2026-10-19-rc-regate.md)
on `feat/card-task-999f7534`; [new-red classification](investigations/2026-10-19-rc-windows-new-reds.md)
on `feat/card-task-90dcf4ee`). The Windows `antiphon` and `e2e` suites are **known-incomplete**
and were not required: `ProcessSpawnLimit.Limit == 1` serialises the 356 class-level limiter
classes of `Antiphon.Tests` through one lane, so the suite admits about 46 UIDs a minute and a
60-, 90- or 120-minute watchdog kills it long before its roughly 12,000-method census ends
([live reds](investigations/2026-10-19-rc-windows-live-reds.md) on `feat/card-task-93971d09`);
the serial `AgentTaskLandDeliveryE2ETests` class alone fills the e2e window
([hangs](investigations/2026-10-19-rc-windows-hangs.md) on `feat/card-task-5b8ad483`). The
watchdog values and their reasons are in [testing and build](testing-and-build.md#nightly).
Acceptance on this standard is the operator's CARD-1350 decision: it earns no `rc-release` credit
and authorises no `publish-release.ps1`; publication still needs the full automated Windows
profile.

## Startup-path rc picks

Apply the [OS witness contract](testing-and-build.md#os-branch-witnesses) to
changes affecting the AppHost/dev-aspire/restart/build-slot path, its libraries
or reviewed startup dependencies. Document/test-only changes may record not
applicable with their actual changed-path census. These steps supplement the
reviewed rc integration procedure; they do not grant merge/publication authority.

1. Before rc publication, record the current rc ref/tip, last healthy running SHA,
   reviewed source SHA and prerequisite chain. Prepare the reviewed **exact integration candidate**.
   On native Windows at that candidate, verify a clean checkout, expected SHA,
   native OS, PowerShell version and resolved real `dotnet`. Record the probe's
   sanitized output, UTC time, source SHA, OS/runtime, absent opt-out state, slot
   grant/release and child exit. Keep its receipt in Review evidence or an
   exact-candidate attachment before pick authorization; an older master receipt
   does not qualify a changed candidate and does not relabel the original Review.
2. In that checkout, run this fresh native PowerShell process. It removes only
   the process opt-out and refuses inherited test shims; USER/MACHINE stay intact.

   ```powershell
   pwsh -NoProfile -Command {
       $ErrorActionPreference = 'Stop'
       if (-not $IsWindows) { throw 'native Windows required' }
       foreach ($name in @('C589_SLOT_SHIM', 'C589_COMMAND_SHIM', 'ANTIPHON_APPHOST_TEST_SEAMS')) {
           if ([Environment]::GetEnvironmentVariable($name, 'Process')) { throw "test seam present: $name" }
       }
       [Environment]::SetEnvironmentVariable('ANTIPHON_BUILD_MEMORY_POLICY_OPTOUT', [NullString]::Value, 'Process')
       if ([Environment]::GetEnvironmentVariables('Process').Contains('ANTIPHON_BUILD_MEMORY_POLICY_OPTOUT')) { throw 'opt-out must be absent' }
       $dotnet = Get-Command dotnet -CommandType Application -ErrorAction Stop
       & ./scripts/build-slot.ps1 -Label rc-native-probe -- $dotnet.Source --version
       exit $LASTEXITCODE
   }
   ```

   Absence is a key-presence assertion, not empty-string equality. Do not dump
   the environment. The wrapper owns the slot: do not nest it inside a slot-owning
   checkpoint. Unavailable, timeout or unleased probes are unqualified; never
   retry outside the gate. This cheap native probe crosses the build-driver
   boundary; it does not prove restore, build or healthy startup.
3. After pushing rc, read back the remote tip and advance the clean canonical
   checkout through the existing rc procedure. Verify it equals the published
   SHA. Repeat the native probe there, then run the **restart rehearsal**: use
   the same fresh-process block above, replacing label `rc-native-probe` with
   `rc-restart-rehearsal` and arguments `--version` with `restore Antiphon.AppHost`.
   The underlying restore-only substep is:

   ```powershell
   pwsh -NoProfile -File scripts/build-slot.ps1 -Label rc-restart-rehearsal -- dotnet restore Antiphon.AppHost
   ```

   The fresh wrapper and its absent opt-out assertion remain required. This does
   not stop AppHost, run npm, create another launcher or waive the canonical
   checkout guard. `restart-apphost.ps1 -StopOnly -WhatIf` only previews stop;
   it cannot satisfy rehearsal. Bare `-WhatIf` is refused. Do not run
   `dev-aspire.ps1` just to rehearse its restore substep.
4. Activate only after rehearsal passes, from an independent owning shell under
   the [restart runbook](apphost-runbook.md#first-start-and-apphost-restart), both
   locks and the Job Object caveat. Require restart exit 0, health and
   `/api/version` equal to the intended rc SHA. Record publication, rehearsal
   and activation separately; a healthy old process is not activation.
5. If startup fails, preserve the error/logs, mark the pick unactivated and
   commission Debug. Use the [forward revert recipe](apphost-runbook.md#failed-startup-path-pick)
   only when source attribution supports it. An infrastructure timeout alone
   does not justify reverting unrelated source.

CARD-1525's temporary USER opt-out is incident cleanup owned by its repair task.
That owner removes it only after repaired rc source and required restarts are
verified. A probe with opt-out `1` cannot count as native policy coverage.

## Cutting, testing and publishing a candidate

| Script | Job |
|---|---|
| `scripts/release-candidate.ps1` | Journal an existing `rc/YYYY-MM-DD` tip, or keep today's create-only push for a legacy stamp ref. An omitted `-Ref` refuses. |
| `scripts/release-cut.ps1` | The lane entrypoint: test the named rc ref under the `rc` profile and publish only on RC complete-green. |
| `scripts/publish-release.ps1` | On a long-lived id, write `vYYYY.MM.DD.N-rc.K` and a GitHub pre-release at the tested SHA. A legacy stamp id still publishes a final tag directly. |
| `scripts/promote-release.ps1` | Write final `vYYYY.MM.DD.N` at the same peeled SHA. It does not delete the pre-release tag. |
| `scripts/release-status.ps1` | Is the running build a published release? Read-only. |
| `scripts/register-release-gates.ps1` | Idempotent Windmill registration front door. |

**Cut.** The release ref is `rc/YYYY-MM-DD`, with an optional `refs/heads/` prefix.
A legacy stamp `release/rc-YYYYMMDDTHHMMSSZ` remains valid. `release/current` and any
other shape refuse. The full SHA is pinned **once** and written to the candidate journal
before any remote update. There is no force push, and tags are never deleted or moved.
A long-lived ref must already exist on the remote; the cut journals that tip and does
not create the branch. An omitted ref refuses `rc-ref-required`. A stamp ref keeps
today's create-only push: an existing remote stamp at a different SHA is a hard refusal,
there is no repair commit on that stamp, and the same journal/ref/SHA is an idempotent
resume. A lost push response is resolved by re-reading the remote rather than pushing
again under a new identity. The producer clone must be owned: the canonical checkout, a
linked worktree and an unmarked clone are all refused.

**Test.** `nightly-run.ps1 -Profile rc -Trigger rc -Ref <candidate> -ExpectedSha <sha>` runs
against `C:\Antiphon\releases\checkout` and the candidate's private state root. `-Profile`,
`-ExpectedSha`, `-CandidateId`, `-ReleaseRoot` and `-CoordinationRoot` survive the
wrapper -> core -> self-reexec -> tests hops. Before tests and again before publication, the
checkout HEAD, candidate journal and remote ref are compared to the pinned SHA. A moved
ref fails that run (`remote-candidate-moved`) and is never retested under the old candidate
id. The long-lived line may move after a successful tag; the next SHA is a new candidate id.

The nightly bootstrap sets `core.longpaths=true` in its owned clone before reset and
cleanup (and during a new clone). Windows' `LongPathsEnabled` alone does not opt Git
into long fixture paths. Its `git clean -fdx` excludes the root
`.antiphon-nightly-owned` and `.antiphon-release-owned` files, preserving their bytes
for the self-reexec and later ownership checks. An unmarked existing clone still
refuses; do not mark an arbitrary clone to bypass that check. Recovery of a proven
release checkout must retain the failed run's evidence and verify its origin and
exact HEAD. The documented `-NoSync` hop can run a separately prepared, cleaned,
marked checkout at the pinned SHA; recheck the remote and candidate journal before
commissioning it. A durable bootstrap repair also needs to reach the rc line before
ordinary syncing runs use that line's script.

**Fix.** A bug or test fix for code already on the rc line lands on that line. A feature
or refactor stays master-only. The failed run's evidence is kept. The line's new tip is
tested from scratch under the rc profile. A partial run or a green under a different
policy is never reused.

**Publish.** Only on RC complete-green, and only after the gate re-validates the candidate
journal, profile/policy hash, every required suite result, report delivery and the remote
candidate SHA. Diagnostic, `-NoReport` and seam-driven runs are refused outright. For a
long-lived candidate id the reserved name is `vYYYY.MM.DD.N-rc.K`, dated by the
publisher's UTC clock sampled once per attempt. The branch-derived candidate id
and `cutUtc` remain unchanged. Fresh remote tag inventory and journal reservations
jointly occupy numbers; a remote final closes its `N`, including hand-published
and lightweight finals. While the UTC day
matches, the next new SHA on that day uses the same `N` and the next `K`. After a final
tag exists for that `N`, or the UTC day changes, the next pre-release starts the next free
`N` at `K=1`. The same candidate/SHA reuses its reservation across midnight and
refuses conflicts without renumbering. A legacy stamp id still uses its stamp date for a
final `vYYYY.MM.DD.N` and publishes it directly, without `--prerelease`.

The pre-release sequence, each transition journalled **before** the next begins:

1. reserve `vYYYY.MM.DD.N-rc.K` in the publication journal
2. create the local annotated tag at that SHA
3. push that tag alone, never with `--force`
4. verify the peeled remote target equals the tested SHA
5. `gh release create <tag> --verify-tag --draft --prerelease --notes-file <file>`
6. upload `release-manifest.json` and the sanitized verification summary
7. read tag and asset digests back
8. publish the draft, then persist the release id and URL

`scripts/promote-release.ps1` then writes `vYYYY.MM.DD.N` at that same peeled SHA. It
revalidates the pinned authority, Full ledger, script hashes and release-card
readback on every attempt, including an already-matching final. Its explicit
`Repository` and `AntiphonApiUrl` inputs follow the publisher's defaults.
The authority now binds seven scripts, including `promote-release.ps1`; existing
six-script authorities must be recut and requalified through the normal process.
Do not insert a new digest into old evidence. Promotion
refuses when the final tag already exists at another SHA, when the pre-release tag is
missing, or when the peel is not a full 40-hex SHA. It does not delete the pre-release
tag or its GitHub pre-release, and it does not restart anything.

Promotion requires one published reservation with the exact candidate id, full
SHA and pre-release tag, nonempty release id/URL, and no conflicting recorded
final. A missing, malformed, duplicate, mismatched or unpublished reservation
refuses before either tag creation or same-final success. Recording revalidates
the tuple, atomically writes the final name, and reads the row back. A post-push
recording failure returns `promotion-journal-write-failed` and reports the
verified remote tag without claiming completed promotion. A retry validates
authority and reservation again, then records the same final without another
tag write. Direct tags without a supported peel refuse `tag-peel`.

These changes are master-only. An rc checkout needs a separately commissioned,
reviewed backport before its release entrypoints can use them; master landing
does not update running rc tooling or qualify the release workflow.

A retry resumes the same journal. Same tag/SHA/manifest is success or recovery; any mismatch
is a hard refusal. A tag with no published release stays **pending**, not released, and so
does a draft missing its assets. Tags are never deleted or moved, and another release's
evidence is never overwritten. After an ambiguous timeout, remote state is re-read before
another write; a crash after publication but before local acknowledgement recovers the
existing release id rather than allocating another `N`.

The manifest carries only allowlisted fields - schema version, repository, tag, candidate
ref and full SHA, native run and Windmill job/slot ids, UTC start/end, policy/profile/script
hashes, build and bundle hashes, per-suite discovered/required/executed/passed/failed/
skipped/excluded counts, exclusion reasons, summary digest and build-reported capabilities.
Capabilities are read from the candidate's own isolated server identity probe, never from the
production server and never from a copied constant. Raw logs, credentials, hostnames and
transcripts never reach a public asset.

Release notes name the commits since the prior published tag, card references where present,
test counts, profile and exclusions, evidence identities and the previous release. The first
release says there is no earlier release.

## Release identity is not local activation

`/api/version.version` remains the built SHA and `capabilities` remain feature probes; no new
web route and no change to NuGet or SDK assembly versioning. `scripts/release-status.ps1`
looks the running SHA up by **exact** match against published release manifests and returns
one of:

- `released` plus the matched tag and URL,
- `unreleased integration build`,
- `unknown` when publication or GitHub data is unavailable.

An ancestor tag, or simply the newest release, is never reported as a newer running SHA's
identity. Creating a release never restarts AppHost or the runner and never checks a tag out
in the main worktree. AppHost and server2 run the current `rc/YYYY-MM-DD` rc tip, never
master. A master-only land is not a restart of those hosts, and this card does not add a
deploy-script guard that probes the tip before a feature release. The rc tip may sit ahead
of the latest tag after a later fix lands. If an operator later deploys a release, require
`/api/version` SHA equality to that tag's peeled SHA and the intended capabilities, using the
[canonical restart runbook](apphost-runbook.md). A server2 runner upgrade that carries the
rc tip follows the staged rolling rollout and keeps dispatching:
[continuous dispatch](docker-stack.md#continuous-dispatch-during-rollout).

## GitHub Actions is not a release prerequisite

Neither candidate admission nor publication depends on Actions being green; they depend on
this profile's fresh native evidence. CARD-0610 owns Actions repair, and the missing CI
signal is stated in initial release notes and qualification. That separation does **not**
waive an overlapping defect: the same client lint failure that reddens CI also reddens this
harness, and it blocks qualification and release until it is repaired on the branch
that owns that code. There is no
baseline-red waiver, retry loop or exclusion created solely to get past a failure. Linux
coverage stays owned by CARD-0590 / CARD-0605.

## Registration (operator-run, S5/S6)

`scripts/register-release-gates.ps1` is preview-only by default and writes nothing. It takes
an untracked `-Profile <json>` with `windmillBaseUrl`, `workspace` (must be `mc`),
`tokenFile`, `repositoryPath` and `projectId`; an inline token, password or secret in that
profile is refused. The token is read from the operator-placed file and is never printed,
logged or embedded in a script payload, and this script creates no token.

```powershell
pwsh -NoProfile -File scripts/register-release-gates.ps1 -Profile C:\Antiphon\nightly\release-gates-deploy.json
pwsh -NoProfile -File scripts/register-release-gates.ps1 -Profile C:\Antiphon\nightly\release-gates-deploy.json -Apply
pwsh -NoProfile -File scripts/register-release-gates.ps1 -Profile C:\Antiphon\nightly\release-gates-deploy.json -Apply -EnableSchedule readiness
```

Registration uses the installed Windmill HTTP API, never a database write. Scripts are
created and read back by hash and content; schedules are always **created disabled**, and
`-EnableSchedule <nightly|readiness|rc>` (which requires `-Apply`) turns on exactly one. An
idempotent reapply discovers the existing objects, preserves an already-enabled matching
schedule and refuses cron/timezone drift; disabling is always explicit and never an accident
of the disabled creation payload. Enable `nightly` after its manual green and `rc` after RC
qualification. Qualification records the before/after/readback of each command, including a
second apply proving no duplicates.

Trigger provenance is derived from the runner's own schedule context (`WM_SCHEDULE_PATH`
matching the expected schedule path); otherwise the wrapper passes `manual`. An API caller's
`trigger` string cannot manufacture scheduled credit, and a missing schedule context refuses
scheduled credit rather than defaulting to it. RC uses `trigger=rc` plus separate
invocation-source, job and slot fields; there is no master-credit alias.

Planned schedules: master `0 30 0 * * *`, readiness `0 */30 * * * *`, RC
`0 30 8,16 * * *` - all `Europe/London`. Two RC cuts a day are *attempts*, not a guarantee
of two successful releases.

Secret placement, interactive Telegram login and authorization for live notices remain the
operator's, as the [custody rule](nightly-watchdog.md#custody) requires. A Code or Review
delegate does not run these commands against the live workspace.

## Interim verification rounds

`server/appsettings.json` now ships the section explicitly:

```json
"InterimVerification": {
  "Enabled": false,
  "CanonicalRepositoryPath": null,
  "ProjectId": null,
  "StateRoot": null,
  "MonitorFreshMinutes": 60,
  "MaxFileBytes": 65536
}
```

`FullOnly` remains the card default. After qualification the deployed server gets the real
values, through the existing `antiphon-server` user-secrets store under the actual AppHost
service account, or the corresponding `InterimVerification__...` environment on a deployment
without user secrets:

```json
"InterimVerification": {
  "Enabled": true,
  "CanonicalRepositoryPath": "C:\\src\\Antiphon",
  "ProjectId": "<actual Antiphon project GUID>",
  "StateRoot": "C:\\Antiphon\\nightly",
  "MonitorFreshMinutes": 60,
  "MaxFileBytes": 65536
}
```

`Antiphon.AppHost/Program.cs` launches the server as Development but does **not** forward an
arbitrary AppHost configuration section, so putting these values only in AppHost appsettings
does not activate the server. Verify effective behaviour with a real pilot admission and
refusal plus recorded deployment identity. Resolve the project through the board/project API,
not a hard-coded GUID and not the board id; validate the configured canonical path against
the task's repository identity before opt-in; keep Windows backslashes. The configured
canonical path is the qualified repository, not the isolated nightly clone.

### What Interim actually saves

The shipped Interim profile reduces **repeated repair-round verification**. It does not
reduce the final ordinary verification required to land a change, and it is not a weaker
landing policy.

- First rounds are Final. A Clean Final baseline is established first.
- An Interim round selects cumulative changed cases since that baseline (earlier repair cases
  included), unresolved-finding tests and named adjacent smoke, and records every deferred ID.
  Deferred IDs are never marked passed.
- Whole-Unit and affected-class ordinary V/R stay in Final.
- Unbounded shared or native impact stays Final.
- Unnamed broad integration/native work and automated E2E run periodically on RCs; a named
  ordinary check is never silently reclassified as "covered by the RC".
- The land latch is unchanged: an exact-SHA Final / Full Review still precedes land.

No claim that activating Interim moves all affected Slow or native tests from Final Review to
the RC gate is valid. Product savings measured so far: **zero minutes, zero dollars**. A plan
and unactivated receipts prove no saving. S5 records comparable Interim and Final
command-correlated costs at one committed source; broader opt-in is a later evidence-based
decision.

### Required before `Enabled=true`

| Artifact | Required content |
|---|---|
| `docs/investigations/<date>-card-0487-nightly-qualification.md` | Accepted CARD-0545 S6 result; deployed code, script/policy hashes, manual full unattended green, later real scheduled full unattended green, all seven suite counts, job/native-run correlation, notification and outage/recovery receipts. |
| `C:\Antiphon\nightly\watchdog-deploy.json` + operator watchdog env/session | Qualifying independent host, correct instance, reachable snapshot; custody local, secrets never committed. |
| `C:\Antiphon\nightly\readiness-config.json` | Expected script/policy hashes, watchdog URL/instance, canonical repository and actual project. |
| `C:\Antiphon\nightly\interim-qualification-receipt.json` | Schema 1; canonical repository/project; committed qualification artifact path and full commit; policy/script hashes; manualRunId, scheduledRunId, scheduledJobId; non-empty recipientEvidenceIds and outageRecoveryEvidenceIds; watchdogInstanceId. |
| `last-complete-green.json` + `last-monitor.json` under the same root | Real scheduled master green for the valid London due day; fresh Healthy/ReadyForDeferral booleans; matching repository/project/hash/native-run/job/watchdog identities. Freshness stays 0-60 minutes; future timestamps rejected. |
| `docs/investigations/<date>-card-0544-verification-pilot.md` | Baseline/subject/selection/card revision, admission and refusal evidence, deferred cases actually run in Final, exact-SHA land, rollback check, measured costs. |
| `docs/investigations/<date>-card-0599-release-gate-qualification.md` | Registration readbacks, full automated RC roster and counts including E2E, two real schedule-slot observations, published tag/release/manifest, exact-SHA status lookup, all exclusions and limits. |

Commit the qualification document **before** writing its full commit SHA into the trusted
receipt. Do not bootstrap readiness by forging `Healthy=true`, accepting a send
acknowledgement as a receipt, or substituting a scheduled RC for the scheduled master
acceptance run.

## Failure handling and rollback

- **Failed RC:** retain evidence and the incident, publish nothing, fix on the rc line when the defect is in code already there (master-only when it is a feature or refactor), then retest that tip.
- **Failed publication after green:** resume the same journal. Never change tested source.
- **Lost master readiness:** Interim admission and queued launch fail closed; existing owner
  latches persist and require Final. RC green cannot restore eligibility.
- **Rollback:** set pilot policies `FullOnly`, `Enabled=false`, and disable RC scheduling and
  publication. Keep the master and readiness jobs and the watchdog running for evidence.
  Preserve tags, releases, candidate journals and qualification receipts - rollback is never
  destructive.
- **Changed script, profile or hash:** an old qualification does not silently apply. Requalify
  identity and readiness before further Interim admission. Old releases keep their original
  manifest.
- RC job failure is visible in Windmill and in a dedicated `release-gate` board incident. The
  independent watchdog continues to monitor **master only**; there is no independent RC
  scheduler-outage notification. An RC incident can never update or close a master nightly
  incident, and a reporting failure is a failed job with a local durable record, not green.

## Advisory project batches (CARD-1545 first slice)

`scripts/select-rc-project-batches.ps1 -BaselineSha <full sha> -CandidateSha <full sha>`
reads committed files and `tests/rc-project-categories.json`. Its JSON manifest
contains both ProjectReference graphs, direct references, transitive downstream
projects, linked Compile/content inputs, categories and proposed whole-test-project batches.
Use `-OutputPath <new file>` to retain an intent without overwriting an older one.
The release selection intent records the same data as `projectProposal` when a
baseline and a non-Full request are present. This adds no test execution.

The first slice keeps mixed projects whole. Server/git operations and shared
Claude/Codex/pty projects carry all their categories; no handler extraction or
per-method discovery is implied. Conditional references are conservatively unioned.
Literal imports are traversed without executing MSBuild. Unknown categories,
paths, missing TUnit batch dispositions, unresolved imports/references, cycles and shared build/scripts/policy inputs
fall back to Full. Client and script inputs also fall back to Full.

`mode=Scoped` describes a shadow proposal only: `effectiveMode=Full`,
`coverageScope=Full`, `gateCredit=false`, `executionEvidence=false`. Every release
still executes the existing full nine-suite profile. This manifest is not a
qualification receipt, unchanged-project proof, or permission to reuse old groups.
Admission of scoped gate credit and shadow/full comparison qualification remain
future CARD-1545 slices; the next full RC run is unchanged.

The advisory inventory separately lists harness-only TUnit executables with reasons.
A TUnit project absent from both the batch list and those dispositions forces Full
and is retained in the proposed full batch list. Literal and wildcard content,
resources and additional files participate in consumer ownership, including files
copied from another project. This inventory does not amend the qualification policy.
