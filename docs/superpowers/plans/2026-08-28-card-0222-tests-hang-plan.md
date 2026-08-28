# CARD-0222 — plan: stop `Antiphon.Tests` hanging (2026-08-28)

Evidence: `docs/investigations/2026-08-28-card-0222-antiphon-tests-hang.md`. Two unrelated causes,
one already fixed on master, one fixed-and-verified but held back from the contested file.

## Done in this task (on master)

- **S0 — ledger guard.** `Directory.Build.targets` deletes an
  `obj/…/<Project>.csproj.FileListAbsolute.txt` over 2 MB before `IncrementalClean` reads it
  (`AntiphonCleanFileMaxBytes`, `0` disables). Verified: 3 MB synthetic ledger → warning + reset to
  1.4 KB, silent next build. The three bloated ledgers in `C:\src\Antiphon` were reset; full-graph
  build of `tests/Antiphon.Tests` went 21m31s → 1m28s. Worktrees self-heal on their next build.

## To land, in this order

- **S1 — the herdr harness clock** (`tests/Antiphon.Tests/Application/HerdrAlwaysOnChannelParityTests.cs`,
  the private `MutableTimeProvider` at the bottom of the file). Replace the frozen `_now` with an
  offset over `DateTimeOffset.UtcNow` — the exact class is in the investigation §2.5 and in this
  session's `card-0222-clock.patch`. Three lines plus a docstring; `Advance` keeps its meaning.
  **Owner: whoever next touches that file** (the brief's CARD-0224 rule; CARD-0224 S1 is already on
  master as `23de792` and no worktree is ahead of master on the file). Verification: run the class
  three times with `--treenode-filter "/*/Antiphon.Tests.Application/HerdrAlwaysOnChannelParityTests/*"`
  built to an alternate `OutputPath`; expect 5/5 in ~25 s each (measured 24 s / 23 s / 25 s tonight).
  Until it lands, run this class only under a `--timeout` or the watchdog — it hangs on roughly
  every run that wins the metadata race.
- **S2 — the same three lines in `AgentSupervisionTests.cs`** (line 588, byte-identical provider,
  same queue service, same `[NotInParallel]` end-of-run phase). Not caught hanging tonight (7/7 in
  18 s, one run); fix it because it is the same defect and it is the strongest CARD-0165 candidate.
  Verification: the class three times, plus the class docstring gaining the clock rule.
- **S3 — the six `FakeTimeProvider(DateTimeOffset.UtcNow)` harnesses** that also register
  `SessionMessageQueueService` (`AgentTaskCheckInterpreterTests`, `AgentTaskCheckScheduleTests`,
  `AgentTaskDeadSessionReconciliationTests`, `AgentTaskReplyIntegrationTests`,
  `OrchestratorTrackerCadenceTests`, `SessionHealthTests`). `FakeTimeProvider` freezes timers too,
  so a delivery through them would stop at the first `Task.Delay` rather than spin. Audit each for
  a test that drives a verified delivery with live metadata; where one exists, either give the
  provider `AutoAdvanceAmount` or scope the fake clock to the service that needs it and hand the
  queue `TimeProvider.System`. Where none exists, a one-line comment saying so is enough. Small,
  mechanical, can be one Codex-tier slice.
- **S4 — settle CARD-0165 with the recipe, not a guess.** One run of the `Antiphon.Tests.Application`
  namespace under the watchdog (`run-watched.ps1 -Dump`, or any wrapper that runs
  `dotnet-dump collect -p` on the still-alive `Antiphon.Tests.exe` before killing it), then
  `dotnet-dump analyze <dmp> -c dumpasync`. If the leaf is a `DelayPromise` under a
  `UtcNow()`-bounded loop, S1–S3 close CARD-0165 too; if it is anything else the chain names the
  real owner. Do this *after* S1/S2 so a known hang cannot mask an unknown one. Chunk the run per
  the CLAUDE.md 10-minute rule, or run it detached with the dump wrapper.

## Not planned

- Rewriting the six `deadline = UtcNow() + N` loops in `SessionMessageQueueService` to bound on a
  stopwatch. They are correct against a coherent clock; the defect is a test double whose clock and
  timers disagree.
- A TUnit-level `--timeout` in the project defaults. It would convert a silent hang into a loud
  failure with no location — worth having once a hang-dump extension is wired in, not on its own.
