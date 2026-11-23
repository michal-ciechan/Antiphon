# rc cut and seeded Windows Full launch after the supervisor port (2026-11-23)

Investigate task `b3e500f7`. This was a read-only investigation, and the repository change is this
note. Nothing was run, restarted, pushed to rc or tagged, and no secret was read.

**Base.** The worktree is at master `c04934e50bb446c4576e819bb9cc806929d37a68` (2026-11-23T18:28:50Z),
which was equal to `origin/master` when it was fetched. Commit `9cabf2f08` (2026-11-23) deleted
`scripts/qualification/*.ps1`, `scripts/lib/qualification-supervisor.ps1`,
`scripts/rc-qualification-status.ps1` and `scripts/status-snapshot.ps1`, and replaced them with
`antiphon-scripts` commands. The next candidate cut from master has no `scripts\qualification\`
directory, so the hand-built task root used by the 557cdb63 launch (Copy-Item `*.ps1`,
`start-supervisor.ps1 -LaunchScript launch-rc.ps1`) cannot be rebuilt from that checkout.

**Markers.** **V** means VERIFIED-IN-CODE at the cited `file:line` on `c04934e50`. **V-remote** means
verified with `git ls-remote` at about 18:40Z. **D** means it is stated in a checked-in doc or note
and was not re-derived from code. **U** means UNVERIFIED. Windows paths, `C:\Antiphon` state and
the desktop process table cannot be read from this Linux mirror.

## Verdict

The supervisor set is a drop-in replacement with the same task-root files, the same launch-record
shape and the same seed arguments. Three things changed:

1. Commands now run as `scripts\antiphon.cmd <command>` from the candidate checkout. Nothing is
   copied into the task root by hand: `qualification-start-supervisor` copies the CLI into
   `<task-root>\cli` itself.
2. The flags are kebab-case (`--task-root`, `--launch-record`, `--log-root`, `--state-root`,
   `--run-id`). `-CheckoutRoot` and `-LaunchScript` are gone from the starter and the stop.
3. Seed verification and seed export are no longer PowerShell functions. `export-nightly-stopped-run-seed`
   is a CLI command. No CLI command verifies a seed without starting a run.

The next line name is `rc/2026-12-04`, not `rc/2026-11-24`. `rc/2026-11-24` already exists at
`870a93d21`.

## (1) Cutting the new rc line from master

| # | Statement | Mark |
|---|---|---|
| 1.1 | **Name.** The rule is the next unused date after the newest remote `rc/*` line, not the UTC date of the cut. The remote has every line from `rc/2026-11-09` through `rc/2026-12-03` (`ecc9bfaa5`), including `rc/2026-11-24` (`870a93d21`), so the next name is **`rc/2026-12-04`**. Precedent: `docs/investigations/2026-11-20-recut-readiness.md` ("Line name: `rc/2026-11-30`, not `rc/<UTC date>`"), which quotes cut task `fc9a0547`. | V-remote; rule D |
| 1.2 | The ref shape is `rc/YYYY-MM-DD`. Any other shape is refused. | D `docs/release-gates.md:540-542` |
| 1.3 | `release-candidate.ps1` does **not** create the branch. A long-lived ref must already exist on the remote. A missing ref is refused with `rc-ref-missing`, and an omitted `-Ref` with `rc-ref-required`. The branch is therefore pushed by hand first. | V `scripts/release-candidate.ps1:5-8`; D `docs/release-gates.md:544-546` |
| 1.4 | Candidate id: `rc-2026-12-04-<sha12>`. | V `scripts/release-candidate.ps1:7-8` |
| 1.5 | `release-candidate.ps1` clones with `-c core.longpaths=true` into `-CheckoutRoot` and writes `.antiphon-release-owned`. It refuses `unmarked-clone` when an existing checkout has no marker, and it also refuses the canonical checkout and a linked worktree. | V `scripts/release-candidate.ps1:100-117,137-145` |
| 1.6 | **Guards before the push.** These come from the 11-24 cut and the 11-20 readiness note. (a) I-5: regenerate the derived impact index (`pwsh -NoProfile -File scripts/regenerate-derived-impact-index.ps1`). If it reports `changed`, land the index on master first and re-pin. (b) `pwsh -NoProfile -File scripts/build-slot.ps1 -Label rc-cut-graph -- scripts/audit-land-verification-graph.ps1 -Check` must return `approved`. (c) `pwsh -NoProfile -File scripts/test-live-lane-run.ps1 -PrintInputDigest` must equal the pin. (d) Run the guard union: `TestClassificationPolicyTests*`, `TestClassificationGuardTests*`, `DerivedImpactIndexTests*`, `C1729LayeringGuardTests*`, `WindowsUnitPlatformAdmissionTests*`, `NativeLinuxPolicyWitnessMutationTests`. (e) Run the Merge high-radius subset on the cut pin. Do not push the rc ref while any row is red. | (a) D `docs/orchestration-loop.md:744`, script exists V; (b) script exists V, call D `.claude/skills/antiphon-rc-qualification/SKILL.md:184`; (c) V `scripts/test-live-lane-run.ps1:3,14`; (d) D; (e) D `docs/orchestration-loop.md:817` |
| 1.7 | The high-radius selector is now **`scripts\antiphon.cmd impacted-classes --merge-subset --range <base>..<pin> --json`** (`scripts/antiphon` on sh). `scripts/impacted-classes.ps1`, which the 11-20 note names, no longer exists. Pass SHAs, never `HEAD^`. | V file absent; D `docs/orchestration-loop.md:823-827`; `scripts/antiphon.cmd:5` |
| 1.8 | The 11-24 note computed `policyHash` with `Get-NightlyPolicyHash`. That PowerShell function no longer exists anywhere under `scripts/`. No replacement command was identified. The run writes its own `policyHash` into `last-run.json` and summary. | V absent; replacement U |
| 1.9 | Producer authority: pushing the new ref is a plain new-ref push and never `--force`. The 11-20 readiness note records that the operator's 2026-11-20 sentence covers that push. The authority for this cut belongs to whoever commissions it. This task has none. | D |

**Cut commands** (desktop, from `C:\src\Antiphon` or any owned clone with origin push rights, after 1.6 is green):

```powershell
git fetch origin
$pin = (git rev-parse origin/master)                       # pin once; full 40-hex
git ls-remote origin refs/heads/rc/2026-12-04               # must print nothing
git push origin "${pin}:refs/heads/rc/2026-12-04"           # plain new ref, never --force
git ls-remote origin refs/heads/rc/2026-12-04               # must equal $pin
$task = '<8hex>'                                            # fresh owned checkout name
pwsh -NoProfile -File scripts/release-candidate.ps1 -Ref rc/2026-12-04 -CheckoutRoot "C:\Antiphon\releases\checkout-$task" -WhatIf
pwsh -NoProfile -File scripts/release-candidate.ps1 -Ref rc/2026-12-04 -CheckoutRoot "C:\Antiphon\releases\checkout-$task"
git -C "C:\Antiphon\releases\checkout-$task" config --local core.longpaths true
git -C "C:\Antiphon\releases\checkout-$task" checkout --detach $pin
```

The clone also needs the **`.antiphon-nightly-owned`** marker. `nightly-run` refuses `unmarked-clone`
when an existing clone has `.git` and no marker (V `scripts/lib/Antiphon.Nightly/NightlyRun.cs:312-326`,
`:1019`). It writes the marker itself only on its own first clone (`:439`), and the launch always uses
`-NoSync`. Earlier cuts copied the marker from the previous candidate, where it is byte-identical
JSON `{ origin, kind: "nightly-clone" }` (D `2026-11-16-rc-cut-2026-11-24.md`; shape V
`NightlyRun.cs:439`). Copy it from `C:\Antiphon\releases\checkout-<prev>\.antiphon-nightly-owned`.
Whether `release-candidate.ps1` leaves the clone detached at the pin is U, so the explicit
`checkout --detach $pin` above makes sure of it. `qualification-launch` refuses a HEAD other than
`expectedSha` (V `QualificationLaunch.cs:164-166`).

## (2) Launching the seeded Windows Full

### What each replacement does

| Old (task-root copy of the .ps1) | Now (candidate checkout CLI) | Mark |
|---|---|---|
| `preflight.ps1 -CheckoutRoot <c>` | `scripts\antiphon.cmd qualification-preflight --checkout-root <c>`. Exit 0, or 2 with `REFUSED: longpaths-not-enabled` and the fix command. It does not change the checkout | V `scripts/lib/Antiphon.Nightly/QualificationLaunch.cs:34,102-114` |
| `start-supervisor.ps1 -CheckoutRoot -TaskRoot -LaunchScript launch-rc.ps1 -LogRoot -StateRoot -RunId` | `scripts\antiphon.cmd qualification-start-supervisor --task-root <t> --launch-record <t>\launch-record.json --log-root <L> --state-root <S> --run-id <id> [--poll-seconds 30] [--progress-minutes 10] [--hang-minutes 20] [--identity-timeout-seconds 60] [--powershell-path <pwsh>]`. Exit 0 means the supervisor is alive or finished, 1 refused or failed, 2 usage | V `scripts/lib/Antiphon.Nightly/QualificationSupervisor.cs:435-457`, `scripts/lib/Antiphon.Scripts/QualificationStartSupervisorCommand.cs:1-9` |
| `supervise-rc.ps1` (detached child) | `qualification-supervise`, started by the starter from `<t>\cli`. Never run it by hand | V `QualificationSupervisor.cs:503-545,467` |
| `launch-rc.ps1` (supervisor's launch child) | `qualification-launch --config <t>\launch-record.json --powershell-path <pwsh>`, started by the supervisor with `ANTIPHON_QUALIFICATION_RUN_ID=<id>` | V `QualificationSupervisor.cs:597,657-660` |

### Starter refusals (exit 1)

All of these are V `QualificationSupervisor.cs:516-534`:

- the task root is missing
- the launch record is missing
- `<t>\supervisor-identity.json` already exists
- the run id is already named by `<S>\last-run.json`, `<S>\run.lock` or `<t>\own-last-run.json`
- the CLI copy into `<t>\cli` fails

### Launch refusals

All of these are V `QualificationLaunch.cs:140-172`:

- exit 1: the record cannot be read
- exit 2: `expectedSha` is not 40 lowercase hex
- exit 2: `-RunId` is missing, or differs from the supervisor's run id
- exit 3: `<S>\run.lock` has a live or unreadable owner. `-StateRoot` defaults to `C:\Antiphon\nightly`
- exit 2: HEAD is not `expectedSha`
- exit 2: `longpaths-not-enabled`

When none of these applies, the launch runs
`<pwsh> -NoProfile -File <c>\scripts\build-slot.ps1 -Label <buildSlotLabel|rc-qualification> -- <pwsh> -NoProfile -File <c>\scripts\nightly-run.ps1 <nightlyArguments>`
and exits with that command's exit code (V `:173-185`).

### Launch record (`launch-record.json`)

The shape has not changed. The launch reads only `checkoutRoot`, `expectedSha`, `nightlyArguments`
(an array of strings, or one scalar) and an optional `buildSlotLabel` (V `QualificationLaunch.cs:147-151,173`).
A UTF-8 BOM is accepted, and so are extra keys. Repeated keys are refused (V
`RcQualificationRecords.cs:182-189`). `nightly-run` refuses an rc run without a release root, with
`rc-release-root-required`, exit 3 (V `NightlyRun.cs:285`; D `SKILL.md:255-257`).
`-CoordinationRoot` may be omitted. Its default is `C:\Antiphon\verification`
(V `scripts/lib/Antiphon.Nightly/ReleaseGatePublication.cs:18,74-76`).

### Seed intake

The seed is handled by `PriorFailureHistory.InitializeDispatch`. Every rule below is V
`scripts/lib/Antiphon.Nightly/PriorFailureHistory.cs:752-770` unless it says otherwise.

- The seed file is recognised by its `seedKind` key. Without `-AcceptCrossCandidateSeed` it is
  refused with `seed-acknowledgement-required`.
- The file's SHA-256 must equal `-PriorFailuresDigest` (`history-digest-mismatch`). The envelope
  `contentHash` must match (`seed-hash-mismatch`). `format`, `seedKind`, a 40-hex `sourceSha` and a
  non-blank `sourceRunId` and `sourceCandidateId` are required (`seed-schema`; V
  `scripts/lib/Antiphon.Nightly/StoppedSeedFiles.cs:86-102`).
- **Source identity:** `sourceCandidateId` must equal `-SeedSourceCandidateId`, otherwise
  `seed-source-candidate-mismatch`. **`sourceRunId` (557cdb63) is required to be non-blank but is
  not compared with any argument.** The run id is bound only through the file digest.
- **Age window:** both `createdAtUtc` and `sourceObservedAtUtc` must be at most 14 days old and at
  most 5 minutes in the future (`seed-stale`). For this seed the 557cdb63 note gives created
  2026-11-23T12:23:35.8593144Z and observed 2026-11-23T12:15:23.3259292Z. The seed is therefore
  valid until **2026-12-07T12:15:23Z**. The timestamps are D (`docs/investigations/2026-11-23-rc-run-557cdb63-final-analysis.md`
  on `origin/feat/card-task-865f7412` `10587f33e`, lines 118-139); the arithmetic follows from the V rule.
- The file is copied to `<S>\history\<runId>\prior-failures.json` before tests start, and the copy
  is checked against the digest (V `:787-801`, `NightlyRun.cs:369-384`).

**Gotcha.** Every seed refusal is a *fallback*, not a run refusal. `InitializeDispatch` catches it,
returns `History=null` and a `Warning`, and `nightly-run` prints
`failed-first fallback: <reason>` and **continues unseeded** (V `PriorFailureHistory.cs:804-809`,
`NightlyRun.cs:375-378`). A stale or mismatched seed therefore produces a normal Full run in plain
order. Check the run's log for that line, and check `dispatch-schedule.json:historyDigest` (D, 557cdb63
note line 104), before you trust failed-first ordering.

**Seed pre-check.** The old `verify-seed.ps1` used `Read-NightlyStoppedRunSeed`, which no longer
exists (V: no match under `scripts/`). No CLI command validates a seed without a run (V: the
command list in `scripts/lib/Antiphon.Scripts/Program.cs:32-91`). `nightly-run --what-if` returns
only after it has taken `run.lock` and the native lock and written `last-run.json`
(V `NightlyRun.cs:328-364,502-508`), so it is not a side-effect-free check. The 557cdb63 note
validated the seed in process, by calling `PriorFailureHistory.InitializeDispatch` from the Release DLL
with an empty owned directory (D, note line 134). Use that method if a pre-check is wanted. The
exact pwsh snippet is U.

### Launch recipe

Desktop, from the candidate checkout, after the native lock is free. Fill `$checkout`, `$sha` and
`$candidateId` from section 1.

```powershell
$checkout    = 'C:\Antiphon\releases\checkout-<task>'
$sha         = '<pin, 40 hex>'
$candidateId = 'rc-2026-12-04-<sha12>'
$releaseRoot = 'C:\Antiphon\releases'
$runId       = [guid]::NewGuid().ToString('N').Substring(0, 8)
$taskRoot    = "$releaseRoot\task-$runId"
$stateRoot   = "$releaseRoot\state-$runId"
$logRoot     = "$releaseRoot\candidates\$candidateId\logs"
$null = New-Item -ItemType Directory -Path $taskRoot, $stateRoot, $logRoot -ErrorAction Stop
[ordered]@{
  checkoutRoot = $checkout; expectedSha = $sha; buildSlotLabel = "rc-qualification-$runId"
  nightlyArguments = @('-NoSync','-Profile','rc','-Trigger','rc','-SelectionMode','Full',
    '-Ref','rc/2026-12-04','-ExpectedSha',$sha,'-CandidateId',$candidateId,
    '-CheckoutRoot',$checkout,'-StateRoot',$stateRoot,'-LogRoot',$logRoot,'-ReleaseRoot',$releaseRoot,'-RunId',$runId,
    '-PriorFailuresPath','C:\Antiphon\releases\state-557cdb63\seed-557cdb63-865f7412.json',
    '-PriorFailuresDigest','eea76b5e11a1c9034c91ccbef92b20789d2944b000cb2dd733aa02f1a7ed5a16',
    '-AcceptCrossCandidateSeed','-SeedSourceCandidateId','rc-2026-12-03-ecc9bfaa5bfa')
} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath "$taskRoot\launch-record.json" -Encoding utf8
pwsh -NoProfile -File "$checkout\scripts\workspace-slots.ps1" cold-reset -AllRunners -Reason "rc qualification rc/2026-12-04 $($sha.Substring(0,12))"
& "$checkout\scripts\antiphon.cmd" qualification-preflight --checkout-root $checkout ; if ($LASTEXITCODE -ne 0) { throw 'preflight refused' }
$starter = "cmd.exe /c $checkout\scripts\antiphon.cmd qualification-start-supervisor --task-root $taskRoot --launch-record $taskRoot\launch-record.json --log-root $logRoot --state-root $stateRoot --run-id $runId"
Invoke-CimMethod -ClassName Win32_Process -MethodName Create -Arguments @{ CommandLine = $starter; CurrentDirectory = $checkout }
```

| Statement | Mark |
|---|---|
| The run id goes in both `-RunId` (record) and `--run-id` (starter). It must be fresh. | V `QualificationLaunch.cs:151-154`, `QualificationSupervisor.cs:520-524` |
| The cold reset runs before a whole rc run. It does not wait for leased slots. | D `SKILL.md:349-354` |
| The starter goes through `Win32_Process` Create with `cmd.exe /c <checkout>\scripts\antiphon.cmd qualification-start-supervisor ...` so it leaves the caller's Job Object. | D `SKILL.md:278-280`; V command header `QualificationStartSupervisorCommand.cs:7-8` |
| `--powershell-path` is omitted above. The starter then resolves `pwsh` on PATH and passes it down (V `QualificationStartSupervisorCommand.cs:24`, `QualificationSupervisor.cs:445,657`). Whether a `Win32_Process`-created `cmd.exe` sees pwsh 7 on PATH is U. The old launch passed `C:\Program Files\PowerShell\7\pwsh.exe` explicitly. `antiphon.cmd` warns that inner quotes do not survive cmd (V `scripts/antiphon.cmd:4`), so passing a path with a space through `cmd /c` is U. If it is needed, use a space-free path (U). After the start, confirm the pwsh in `supervisor-identity.json` / `launch-child.json`. | V / U as marked |
| `antiphon.cmd` builds `Antiphon.Scripts` into `<checkout>\bin-launcher\antiphon-scripts` with raw `dotnet msbuild` (no build slot) and exits 9 when the build fails. `bin-*/` is gitignored, and the `-NoSync` path does not run `git clean`, so the output does not dirty the checkout. Run preflight first: its build warms the launcher before the `Win32_Process` start. | V `scripts/antiphon.cmd:7-15`, `.gitignore:48`, `NightlyRun.cs:450` |

### Task-root contents after a start

These files are written by the commands, not copied by hand (V `QualificationSupervisor.cs:503-508,540,615-704`; D `SKILL.md:272-290`):

- `launch-record.json`, written by the operator
- `cli\`
- `supervisor-stdout.log` and `supervisor-stderr.log`
- `supervisor-identity.json`
- `start-supervisor.json`
- `supervisor.log`, with START, LAUNCHED, PROGRESS, HANG_SUSPECTED and EXIT lines
- `supervisor-heartbeat.json`
- `launch-child.json`
- `commission-console.log`
- `own-last-run.json`
- `commission-exit.txt`

## (3) Observing and stopping

| Need | Command (desktop, `scripts\antiphon.cmd`; `scripts/antiphon` on sh) | Mark |
|---|---|---|
| Supervisor verdict | `qualification-observe --task-root <t> --state-root <S> [--heartbeat-stale-minutes 5]`. Prints one JSON line. Exit 0 means `completed`, `running` or `supervisor-lost-run-alive`; 3 means `run-lost`; 2 means `not-started`, `launch-failed` or `unknown`. Read-only | V `scripts/lib/Antiphon.Scripts/QualificationObserveCommand.cs:1-6`, `QualificationSupervisor.cs:302` |
| Coherent status | `rc-qualification-status --candidate-id <id> --run-id <runId> --state-root <S> --coordination-root C:\Antiphon\verification [--release-root C:\Antiphon\releases]`. Exit 0 coherent, 2 incomplete or invalid. Replaces `scripts/rc-qualification-status.ps1` | V `scripts/lib/Antiphon.Nightly/RcQualificationStatus.cs:26` |
| Watcher | `watch-qual --log-dir <L>\<run-dir> --state-file <owned-scratch>\qual.seen.txt --heartbeat <t>\supervisor-heartbeat.json [--timing-root <L>\timing-<runId>]`, as a Monitor task with about 30 min re-arms | V `scripts/lib/Antiphon.Nightly/WatchQual.cs:37`; D `SKILL.md:52-80` |
| ETA | `qualification-eta --timing-root <timing-root>` | D `SKILL.md:76` |
| Status snapshot | `status-snapshot [--project-id <guid>] [--runner server2] [--json] [--rc-run-log-dir <dir>] [--rc-state-dir <dir>] [--rc-heartbeat-path <file>] [--repository <dir>]`. Replaces `scripts/status-snapshot.ps1` | V `scripts/lib/Antiphon.Nightly/StatusSnapshot.cs:55-56` |
| Stop (**operator only**; agents never aim it at a live run) | `qualification-stop --task-root <t> --coordination-root C:\Antiphon\verification --run-id <runId> --reason <text> [--evidence-root <dir>] [--confirm]`. Without `--confirm` it is a dry run and exits 2. It runs on Windows only, and exits 5 elsewhere. Exit codes: 0 stopped, 3 incomplete, 4 lock retained. `--checkout-root` is now an unknown-flag usage error | V `scripts/lib/Antiphon.Nightly/QualificationStop.cs:35`; D `SKILL.md:319-345`, commit `9cabf2f08` message |

**File locations:**

- Heartbeat: `<t>\supervisor-heartbeat.json`.
- Shared state: `<S>\last-run.json` and `<S>\run.lock`. Keep `<S>` per run (`state-<runId>`).
- The supervisor's copy of the state: `<t>\own-last-run.json`.
- Run logs, `summary.json`, the TRX files and `dispatch-schedule.json`: `<L>\<yyyy-MM-dd-HHmm>-<runId>\`.
- Timing: `<L>\timing-<runId>`.
- Native lock: `C:\Antiphon\verification\native-run.lock`.
- Seed copy: `<S>\history\<runId>\prior-failures.json`.

Sources: V `QualificationSupervisor.cs:615-616`, `NightlyRun.cs:370,512`; D 557cdb63 note lines 14-16.

The checkout does not have an "active chunk" file. The current chunk is read from the newest chunk
log or heartbeat in the run directory. The exact heartbeat field names are D in `SKILL.md:38-42`;
the field list is U.

## (4) Old steps that no longer work as written

| Old step | Replacement | Mark |
|---|---|---|
| `Copy-Item <checkout>\scripts\qualification\*.ps1 -Destination $taskRoot` | **Gone.** The directory does not exist on master. `qualification-start-supervisor` copies the CLI into `<t>\cli` | V dir absent; `QualificationSupervisor.cs:527-534` |
| `pwsh $taskRoot\preflight.ps1 -CheckoutRoot $c` | `scripts\antiphon.cmd qualification-preflight --checkout-root $c` | V |
| `pwsh $taskRoot\start-supervisor.ps1 -CheckoutRoot -TaskRoot -LaunchScript launch-rc.ps1 -LogRoot -StateRoot -RunId` | `cmd.exe /c <c>\scripts\antiphon.cmd qualification-start-supervisor --task-root --launch-record --log-root --state-root --run-id`. `-LaunchScript` is gone (the launch is compiled), and so is `-CheckoutRoot` (it is taken from the record) | V |
| `launch-rc.ps1` | `qualification-launch --config` (started by the supervisor) | V |
| `supervise-rc.ps1` | `qualification-supervise` (started by the starter) | V |
| `observe.ps1 -TaskRoot -StateRoot` | `qualification-observe --task-root --state-root` | V |
| `stop-supervisor.ps1` / `qualification-stop.ps1 -CheckoutRoot ...` | `qualification-stop --task-root --coordination-root --run-id --reason [--confirm]`. There is no checkout root | V |
| `scripts/rc-qualification-status.ps1` | `rc-qualification-status` | V |
| `scripts/status-snapshot.ps1` | `status-snapshot` | V |
| `verify-seed.ps1` (`Read-NightlyStoppedRunSeed`, `Initialize-NightlyDispatchHistory`) | **Gone.** Only an in-process `PriorFailureHistory.InitializeDispatch` call remains, or reading the fallback line after the launch | V absent; method U |
| `scripts/export-nightly-stopped-run-seed.ps1` | `scripts\antiphon.cmd export-nightly-stopped-run-seed ...` (and `merge-nightly-seeds`) | V file absent, `scripts/lib/Antiphon.Nightly/NightlySeedCommands.cs:47-49` |
| `scripts/impacted-classes.ps1 -MergeSubset -Range` | `scripts\antiphon.cmd impacted-classes --merge-subset --range --json` | V file absent; D |
| `Get-NightlyPolicyHash` (cut check) | **Gone.** No identified replacement | V absent; U |
| `scripts/nightly-tests.ps1` | Run as `antiphon-scripts nightly-tests` from `nightly-run` | V file absent, `scripts/nightly-run.ps1:15-25` |
| Still PowerShell and still valid | `release-candidate.ps1`, `nightly-run.ps1` (a 5.1 launcher of the CLI), `build-slot.ps1`, `audit-land-verification-graph.ps1`, `workspace-slots.ps1`, `regenerate-derived-impact-index.ps1`, `test-live-lane-run.ps1`, `restart-apphost.ps1`. Several of them, including `release-candidate.ps1`, are on the remaining port list, so re-check before use | V files exist; port list D `docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md:156,170,233,282,338` |

## (5) Prerequisites and gotchas for the desktop launch

1. **One native owner.** `C:\Antiphon\verification\native-run.lock` must be free, or be a proven
   stale lock that the operator has cleared. A live owner is never stolen on age (D
   `docs/release-gates.md:100-111`). Read it first with `rc-qualification-status` and
   `qualification-observe` for the 557cdb63 task. Note that 557cdb63 has completed (D: note line 12).
2. **`run.lock` in the state root.** The launch refuses with exit 3 while `<S>\run.lock` names a
   live owner or is unreadable. This check is made only once, when the launch starts. A run that starts
   during the build-slot wait (up to 45 min) makes `nightly-run` refuse instead (V
   `QualificationLaunch.cs:155-163`; D `SKILL.md:265-268`). A per-run `state-<runId>` avoids the
   shared `C:\Antiphon\nightly` root. The launch defaults to that root only when `-StateRoot` is
   missing from the record.
3. **Build slot.** The whole run holds one desktop build-slot lease through
   `build-slot.ps1 -- nightly-run.ps1` for its 15-17 h. The broker keeps a live holder past its TTL
   but reaps it at the 48 h absolute bound (D `docs/testing-and-build.md:1345`). An exit 4 slot
   timeout is reported, never re-run unleased. The broker is the desktop runner at
   `http://localhost:17204/build-slots`, so do not restart the desktop session runner during the run
   (consequence U).
4. **Canonical checkout pin and AppHost activation.** These are separate from the Full and come
   first. AppHost and server2 track the current rc tip, so a new line needs activation. Do these
   from `C:\src\Antiphon` in order: `git status --porcelain` empty, `git checkout --detach <pin>`,
   then `pwsh -NoProfile -File scripts/restart-apphost.ps1 -ExpectedServerSha <pin>`. Exit 0 is the
   only success. Exit 3 is a refusal, including `phone-home-config-present` while
   `C:\Antiphon\runner\phone-home.json` exists. Exit 5 is a SHA mismatch. Afterwards, check
   `GET /api/version`. A server2 runner refresh from the new rc goes through the staged rolling
   rollout. (D `AGENTS.md` front doors; `docs/investigations/2026-11-20-post-cut-activation-runbook.md:63-161`;
   `docs/release-gates.md:664-684`.) After the CARD-1744 window, the production restart is
   `scripts/restart-linux-server.ps1`. Whether that window has happened is U, so check the desktop
   maintenance marker. Never point the Full at `C:\src\Antiphon` or at `desktop-win` (D runbook
   line 203).
5. **Longpaths.** `-NoSync` does not set `core.longpaths`. Preflight, the launch and `nightly-run`
   all refuse without it. Fix: `git -C <c> config --local core.longpaths true` (V
   `QualificationLaunch.cs:88,167-171`).
6. **Markers.** The clone needs `.antiphon-release-owned`, written by `release-candidate.ps1`, and
   `.antiphon-nightly-owned`, which you copy in. Without the second, `nightly-run` refuses
   `unmarked-clone` (V, section 1).
7. **Remote and journal recheck.** `nightly-run` compares HEAD, the candidate journal and the
   remote ref with the pin before tests and before publication. If the ref has moved, the run fails
   with `remote-candidate-moved`. **Do not push to `rc/2026-12-04` while the run is in flight.**
   Repairs go to the next line (D `docs/release-gates.md:552-558`).
8. **No `release-cut.ps1`.** It publishes on green, so it is not the launcher (D `docs/release-gates.md:217`).
9. **Win32_Process environment.** Whether PATH includes `dotnet` and `pwsh` 7 under
   `Win32_Process` Create is U. If `antiphon.cmd` cannot build, it exits 9 and the starter never
   runs. Look for `start-supervisor.json` within about 60 s and `supervisor-identity.json`
   (`consoleProcessIds` must not contain the starter pid; D `SKILL.md:294-296`).
10. **Unseeded fallback.** See section 2: a seed refusal is a warning, not a stop. After `2026-12-07T12:15Z` this
    seed silently stops applying.

## Not done, noted

- Nothing was run on Windows. Every desktop path and lock state is D or U.
- Fix idea: add a read-only `antiphon-scripts verify-nightly-seed --path --digest --source-candidate-id`
  that prints `InitializeDispatch`'s `FallbackReason`, so the pre-check is restored without taking
  any lock.
