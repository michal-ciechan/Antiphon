# Review round 1: CARD-1667 qualification supervisor (task 6ffbb647)

Tested SHA: 1cdcbb408a51183fc0d1840c8a430cb8cd34e0ed (base 868704edd). Verdict: one defect, D-1.

## Runs (server2 runner mirror, Linux pwsh)

- `pwsh -NoProfile -File scripts/test-qualification-supervisor.ps1`: 18 passed, 0 failed, 18 rows, 32 s.
- `pwsh -NoProfile -File scripts/test-orchestration-watchers.ps1`: 39 passed, 0 failed, 39 rows.
- TUnit `/*/*/RcQualificationWatcherScriptTests*/*`: one build-slot lease for the build
  (`dotnet build tests/Antiphon.Tests -c Debug -p:UseAppHost=false`, 4m18s) and one for the
  `--no-build` run with the same properties: total 2, failed 0, succeeded 2, skipped 0.
- Mutant (owner probe moved after the last-run.json read, `scripts/lib/qualification-supervisor.ps1:171-173`):
  harness 17/18, red on "observe probes the run owner before reading last-run.json"
  (classification run-lost). Restored with `git checkout --`; tree clean.
- `check-evidence-diff.ps1 -Repository . -BaseRef 868704edd -HeadRef 1cdcbb408`: commits=3 violations=0, exit 0.
- `git merge-tree --write-tree origin/master(97e77f9f1, contains 2cad8a419) 1cdcbb408`: clean
  (tree abf71e0e9). Master has not touched the SKILL file, the TUnit class or the two libs since base.
- ASCII scan (`grep -P '[^\x00-\x7F]'`) of every new .ps1 and the SKILL: none. No `??`, `?.`,
  ternary or `&&`/`||` in the 5.1 scripts. launch-rc.ps1 is `#requires -Version 7.0` by design.

## D-1: observe credits a different run's last-run.json (refused launch reads as completed)

Where: `scripts/lib/qualification-supervisor.ps1:143-155` (Get-QualificationSupervisionClassification)
and `:169-176` (Get-QualificationSupervision); `scripts/qualification/observe.ps1` takes no run
identity. Neither the last-run.json runId nor the run.lock runId is compared with the launch.

Failure: the default StateRoot `C:\Antiphon\nightly` is shared by every nightly and rc run, so
last-run.json almost always carries the previous run's `completedAt`. nightly-run refuses on
run.lock or ownership (`nightly-run-impl.ps1:402-429`) before it writes the `Started` state, and
launch-rc refuses a SHA mismatch before nightly-run starts. Reproduced on this tip:
last-run.json `{runId:"prev-run", completedAt:<yesterday>, exitCode:0, testsPassed:true}`, no
run.lock, `commission-exit.txt` = 2 (launch-rc SHA refusal) ->
`observe.ps1` prints `classification:"completed", exitCode:0, testsPassed:true`, exit 0. The
same verdict appears with a live supervisor and a fresh heartbeat during the build-slot wait
(up to `-SlotWaitMinutes`, default 45) before nightly-run writes `Started`. It also appears on a
slot timeout (exit 4) and on a nightly refusal (exit 3). The inverse case: our launch is refused
because another live run holds run.lock. Observe then reports `supervisor-lost-run-alive`, exit 0,
"not a run failure", for a run that never started.

Why: a run that never started, or was refused, is reported as completed (and green-looking).
Equally, someone else's live run is reported as ours. That is the masking this card exists to
prevent, and an observer could close the qualification on another run's verdict.

Fix: bind the observation to the launch's runId. Take it from the launch record's
`nightlyArguments -RunId` (record it in supervisor-identity.json or launch-child.json) or from a
`-RunId` parameter on observe. Credit `completed`, `running` or `supervisor-lost-run-alive` only
when the last-run.json runId and the run.lock runId match it. Otherwise classify
not-started/other-run (exit 2), and report launch-failed when `commission-exit.txt` exists
without a matching state. Add harness rows: (a) a stale completedAt with another runId plus
commission-exit 2 is not completed; (b) a run.lock held by another runId is not this run's
running or supervisor-lost.

## Advisories

- A-1 run-lost (exit 3) only fires for a dead pid in a run.lock that was left behind. nightly-run's
  normal exception path (`nightly-run-impl.ps1:833-843`) releases run.lock without `completedAt`,
  so the result is `unknown` (exit 2), not "nightly-run gone without completedAt" = 3 as the SKILL
  says. It is nonzero either way. With the D-1 binding, a missing lock plus matching state without
  completedAt plus a gone launch child can be run-lost.
- A-2 SKILL audit note: event 4689 records the process that exited and its exit status. It does not
  record who terminated it. Naming the killer needs handle-access auditing (4656/4663
  PROCESS_TERMINATE) or Sysmon. Reword "To name the killer". The note is correctly operator-only
  and is not a recommendation to agents.
- A-3 launch-rc does not check run.lock itself. Nothing starts over a live run, because
  nightly-run's exclusive lock refuses before any state write or checkout work. A second launch
  does wait in build-slot behind the live run's whole-run lease, and can start a new run once that
  run ends. start-supervisor's refusal is per task directory and racy: the child writes the
  identity, so a rerun after the 60 s identity timeout can start a second supervisor in the same
  directory. Consider a run.lock-owner precheck and a claim file written by the starter.
- A-4 supervise-rc launches launch-rc.ps1 (`#requires 7.0`) with the starter's PowerShellPath. A
  starter run under powershell.exe makes the launch fail closed. Document "start with pwsh" or
  default -PowerShellPath to pwsh.
- A-5 The 1 s Unix identity tolerance is acceptable. A false match needs a stranger with the same
  pid that started within 1 s of the recorded start, which means the original died within a second
  of starting. Windows is exact: 0 ms, and the 'o' round-trip keeps the ticks.
- A-6 The Windows detach reads correctly: `-WindowStyle Hidden` without `-NoNewWindow`, with
  redirects. That matches the SKILL text and the 7f8e9fd1 launch-rc survival. The native witness is
  still owed: a pwsh and a powershell.exe 5.1 run of the harness once the live run releases the
  native lock. The Unix setsid end-to-end row passed here.
- Safety (1) holds: no new script stops, signals or restarts a process or touches a lock. The only
  delete is the atomic replace of the scripts' own record files. HANG_SUSPECTED writes a flag and a
  log line only.

ordinaryScopeCompleted: Interim (the brief's named rows only; the operator accepts Interim for master-only work).
