# CARD-1981 S2 task a7e556cc: salvage of a Failed-but-pushed Code task

Date: 2026-11-18. Investigate task 99ffd771 (read-only). Subject task
`a7e556cc-7117-4f56-9879-f6a6406296e5` (Code, Grok, server2), session
`e95a7afe-1e80-4ec0-99d9-8895b599831b`, branch `feat/card-task-a7e556cc`.

## Verdict

The work is complete and its brief arrived intact. The Failed outcome is an
attribution miss: the work itself did not fail. The task-marked final report
(`[antiphon-report:a7e556cc done]`, `next: review`) ended its turn at
22:03:29.404Z. 21 ms later the Grok CLI typed a stale background-task
completion notice into the session as a user prompt. That prompt carries no
task marker. Settlement at 22:04:51Z judged the session's latest turn, whose
prompt was unmarked, and recorded "prompt carries no task marker". The branch
can go to Review as-is. Re-running the Code task would repeat committed work.

## 1. Branch facts

`git fetch origin feat/card-task-a7e556cc master`. `ls-remote` tip:
`05802489ac0f43715a69c342b4429e07da5e0ecf`. Merge-base with `origin/master`
(`709c41f16caf400b6fa91a80866a06d2c05fc318` at fetch):
`6414a71967575b2a7dfbf283ee273f930b7a2100`, the task's recorded
`worktreeBaseSha`.

The history is linear. Every commit has one parent, so there is no merge
commit. `git cherry -v origin/master` marks all five `+` (none is on master):

| # | SHA | Time (UTC) | Subject | Kind |
|---|---|---|---|---|
| 1 | `48277ce330e1bd1fe06a818a0cb2d7674a4a4d66` | 20:52:09 | test(build): CARD-1981 S2 lease-tmpdir witnesses, red at base | red witnesses + plan |
| 2 | `ad6aea57aa8a48bce232b987e91607f4e4025a00` | 21:00:34 | test(build): CARD-1981 S2 lease driver witness compiles | red witness compile fix (one `using`) |
| 3 | `47de367a6ad057584870c80981f8a0c4c9442559` | 21:35:11 | fix(build): CARD-1981 S2 confine SDK temp to a per-lease TMPDIR | fix |
| 4 | `1e1a5a81e374492f821e50713db534828c69412b` | 21:40:47 | chore(tests): CARD-1981 refresh the derived-impact index | generated index |
| 5 | `05802489ac0f43715a69c342b4429e07da5e0ecf` | 22:01:30 | chore: refresh land-graph records for the landing batch | land-graph records |

`git diff --stat 6414a719..05802489`: 21 files, +752/-35. Added:
`docs/superpowers/plans/2026-11-18-card-1981-s2-checkpoints.md`,
`scripts/lib/LeaseTempDirectory.cs` and
`tests/Antiphon.Tests/Checkpoints/C1981LeaseTempDriverTests.cs`. Modified:
`docs/testing-and-build.md`, `scripts/build-slot.ps1`,
`scripts/fixtures/c589-command-shim.ps1`,
`scripts/lib/build-driver-environment.ps1`, `scripts/run-checkpoint.ps1`,
`scripts/test-build-slot.ps1`, `server/Antiphon.Server.csproj`,
`server/Application/Services/AgentTaskLandService.cs`, the two
`LandVerificationBuildGraph.*.txt` records,
`src/Antiphon.SessionRunner/RunnerTempSweep.cs`, the three
`C1677Temp*Tests.cs` files, `tests/Antiphon.Tests/Scripts/BuildSlotScriptTests.cs`,
`tests/derived-impact-index.json`,
`tools/Antiphon.Checkpoints/Antiphon.Checkpoints.csproj` and
`tools/Antiphon.Checkpoints/Execution/ProcessDriver.cs`.

Not touched, as the brief required: `scripts/remove-review-scratch.ps1`,
`OwnedTempLeakTests`, `scripts/impacted-classes*`, `tests/impacted-classes.json`
and `tests/Antiphon.Tests/TestHelpers`.

`git merge-tree --write-tree --name-only origin/master 05802489` at
`709c41f1` exits 1 with three conflicts:
`tests/Antiphon.SessionRunner.Tests/C1677TempFamilyRegistryTests.cs` (family
count: master 20, branch 13), and the two
`server/Application/Services/LandVerificationBuildGraph.{evaluation,manifest}.txt`
records. The record conflicts are new since the agent's own check against
`0cbe40ee1686903c7c1ffd27f8cdd8404afd5ca6`: master has since landed its own
land-graph refresh (`709c41f16c`). Those records are regenerated at land
(land-graph refresh).

## 2. Session transcript and the attribution failure

Source: `GET /api/sessions/e95a7afe-1e80-4ec0-99d9-8895b599831b/transcript?since=0`
(677 entries: 5 UserPrompt, 5 TurnEnd, 3 auto CompactBoundary). Task row:
`GET /api/agent-tasks/a7e556cc-7117-4f56-9879-f6a6406296e5`.

| Seq | Time (UTC) | Entry |
|---|---|---|
| 1 | 20:24:47.591 | UserPrompt `[antiphon-grok-rules:df1cac68…]` rules read (no task marker, by design) |
| 7 | 20:25:00.469 | TurnEnd (ANTIPHON_RULES_ACK) |
| 8 | 20:25:03.281 | UserPrompt `[antiphon-task:a7e556cc] role=Code …` pointer to `.antiphon/inbox/297731ed-656e-4853-a550-3535a7f185f4.md` |
| 247 | 20:49:29.822 | ToolCall run_terminal_command "Run the lease TMPDIR witness red" (Grok background job `call-a4595a3c-…-61`) |
| 254 | 20:51:11.214 | AssistantText: "stuck walking `/tmp` … I'll stop that run" |
| 645 | 22:03:29.241 | AssistantText: final report, `next: review`, `[antiphon-report:a7e556cc done]` |
| 646 | 22:03:29.404 | TurnEnd (the only TurnEnd between seq 8 and seq 645) |
| 647 | 22:03:29.425 | UserPrompt `<system-reminder> Background task "call-a4595a3c-…-61" completed (terminated by signal signal 15). Description: Run the lease TMPDIR witness red \| Duration: 101.7s` (no task marker) |
| 653 | 22:03:53.002 | TurnEnd (reply: the killed process "does not change the result already reported") |
| 654 | 22:03:58.219 | UserPrompt `[antiphon-grok-rules:efe9d9d2…]` rules re-read (no task marker) |
| 661 | 22:04:10.481 | TurnEnd (ANTIPHON_RULES_ACK) |
| — | 22:04:51 | Task settled Failed (`SUPERSEDED … SETTLED at 2026-11-18T22:04:51Z` in task events) |
| 662 | 22:04:53.102 | UserPrompt `[task-supervision:62273bbd-…]` Failed notice, failure-event `318c1cf4-a93f-4272-aeee-9098a0ecd431` |
| 676/677 | 22:06:15 | Agent's triage reply `[antiphon-report:a7e556cc failed]`, `next: none`; TurnEnd |

Mechanism (reconstructed from stored rows):

- The background job's duration is 101.7 s and it started at 20:49:29.8.
  That puts its kill at about 20:51:11, the moment of the agent's "stop that
  run" (seq 254). The CLI held the completion notice through the one long
  turn (seq 8 to 646) and delivered it 21 ms after that turn ended, as a
  plain user prompt.
- After seq 646 the session ended two turns whose prompts have no
  `[antiphon-task:a7e556cc]` marker: 653 (prompt 647) and 661 (prompt 654).
  Settlement at 22:04:51 followed both. The stored rows do not say which of
  the two it judged. Either one gives the recorded reason. The marked report
  turn 646 was the latest turn for only 21 ms.
- The failure text says "10 minutes after dispatch". That is the template's
  threshold. The elapsed time was about 100 minutes (dispatch 20:24:41Z,
  settlement 22:04:51Z).
- The agent's own triage (seq 676) blames the first rules-ACK turn (seq 7).
  That is wrong. Turn 7 ended 19 s after dispatch, about 100 minutes before
  settlement.
- The task row has `result: null` and `progressEvidence: null`. A
  `LateFailedParent` supervision notice `93a09a5a-…` is Pending, with
  reportLink `…/transcript?since=675`, the triage reply (not the seq-645
  report).

### Final report (seq 645), quoted

Conclusion (first paragraph):

> CARD-1981 S2 is on `feat/card-task-a7e556cc` at `05802489ac0f43715a69c342b4429e07da5e0ecf`. A per-lease Linux `TMPDIR` confines the SDK 10.0.404 empty-directory leak, the sweep family `dotnet-sdk-empty` is registered and disabled, and CP-1 through CP-5 are green. Landing still has one registry-test conflict against `origin/master`.

Next-stage block:

```
--- next stage ---
next: review
handoff: 05802489ac0f43715a69c342b4429e07da5e0ecf: CP-1..CP-5 green at 1e1a5a81e374492f821e50713db534828c69412b (20261118-214117-8a3d, slot granted, waited 0s). dotnet-sdk-empty stays disabled. evidence-diff clean. merge-tree conflicts only in C1677TempFamilyRegistryTests.cs (resolve to count 21, null producer ClaudeScratch or DotnetSdkEmpty). Land-graph digest FEFB3C52 count 80.
artifact: docs/superpowers/plans/2026-11-18-card-1981-s2-checkpoints.md

[antiphon-report:a7e556cc done]
```

The report also claims these. Where marked, I checked them:
`check-evidence-diff` base..tip exit 0, commits=5, violations=0 (not re-run).
Pins unchanged: `policyHash 62b6e12c…af981`, live-lane `PinnedInputDigest
8642f40f…2458f` (not re-run). The guard union was not run because
classification, selector and pin paths were untouched. Land-graph `-Check`
was refreshed to digest `FEFB3C52…`. CARD-1981 was updated to revision 6 with
a pointer to `docs/investigations/2026-11-18-tmp-six-char-producer.md` (not
checked). Expected effect: about 52 fewer six-character directories an hour,
confirmed with `find /tmp -maxdepth 1 -type d -regextype posix-extended -regex
'/tmp/[A-Za-z0-9]{6}' -mmin -60 | wc -l`.

## 3. Did the brief reach the agent intact?

Yes. The inbox file
`/work/worktrees/ws-10-d2/.antiphon/inbox/297731ed-656e-4853-a550-3535a7f185f4.md`
is still on this runner. It is 11,977 bytes and exactly 11,961 characters,
matching "It is 11,961 characters" in the seq-8 pointer prompt. It opens and
closes with `[antiphon-task:a7e556cc]`. The agent read it (seq 12: "The brief
is loaded"), and re-read it after each compaction (seq 165).

The work matches the brief:

- Per-lease `TMPDIR`: `/tmp/aslot-<8 hex>` is set by `scripts/build-slot.ps1`,
  `run-checkpoint.ps1` `Invoke-Dotnet`, the checkpoint `ProcessDriver`, and
  `AgentTaskLandService` (land verifier) through the shared
  `scripts/lib/LeaseTempDirectory.cs`. It is Linux-gated. Windows `%TEMP%` is
  unchanged and the report says so.
- Sweep family `dotnet-sdk-empty` in `RunnerTempSweep.RegisteredFamilies`:
  empty, mode 0700, uid 1654, older than one hour, six `[A-Za-z0-9]`
  characters, `DefaultEnabled` false. TempSweep stays off.
- Docs: `docs/testing-and-build.md` (+2 lines).
- Plan: `docs/superpowers/plans/2026-11-18-card-1981-s2-checkpoints.md`, as
  the brief named. Five rows CP-1..CP-5.
- Report: gives the expected effect and the read-only confirmation command.

## 4. Red witnesses, fix and evidence

Red witnesses: `48277ce3…` and `ad6aea57…`. `48277ce3` added
`C1981LeaseTempDriverTests.cs` without
`using Antiphon.Tests.TestHelpers;`. `ad6aea57` adds only that line, so the
Antiphon.Tests witnesses first compile at `ad6aea57` (still before the fix).
Fix: `47de367a…`. Generated: `1e1a5a81…` (index) and `05802489…` (records).

Red evidence is recorded only in the transcript and the seq-645 report (they
were unlisted pre-fix runs and the plan has no "Unlisted runs" section):

- seq 347 (21:07:35, after `ad6aea57`): `C1981_LeaseTempDirConfinesSdkScratch`:
  child `tmpdir=/tmp`, `sdk=/tmp/lYDGTt` left in parent `/tmp`.
  `C1981_dotnet_child_tmpdir_is_removed_with_the_lease`: `ShouldMatch
  ^/tmp/aslot-[0-9a-f]{8}$` but was `/tmp`
  (`C1981LeaseTempDriverTests.cs:60`).
- seq 424 (21:16:29): `C1981_dotnet_sdk_empty_accepts_only_the_exact_shape`:
  `ShouldContain Ab12Cd` failed, "the exact empty 0700 uid-1654 directory is
  not a candidate" (`C1677TempSweepNativeTests.cs:348`).

Green evidence: checkpoint receipt
`/work/worktrees/ws-10-d2/.antiphon/checkpoints/20261118-214117-8a3d/report.md`
(gitignored, on this runner's mirror only). Commit `1e1a5a81…`, state=clean,
buildSource=verified. CP-1 1/1, CP-2 2/2, CP-3 1/1, CP-4 1/1, CP-5 1/1,
skipped 0, `unlisted: none`, `verdict: GREEN exit=0`, wall 10m07s. The tip
`05802489` adds only the two land-graph record lines on top of the tested SHA.

Items the Review should judge. These are evidence gaps, not defects I found:

1. `C1981_lease_dispose_does_not_follow_a_replaced_symlink` (CP-2's second
   method) was added in the fix commit `47de367a`. It has no red-first run.
   At base it would fail only because `Antiphon.Build.LeaseTempDirectory` did
   not exist.
2. The fix commit rewrites two existing assertions. In
   `C1677TempSweepTests.Unregistered_random_and_process_names_aggregate_by_prefix`
   (CARD-1896 V-1), six-character names that were `<random>` unregistered
   rows now become `Kept`/`family_disabled` candidates of `dotnet-sdk-empty`.
   `C1677TempFamilyRegistryTests` count changes from 12 to 13.
3. The fix reaches `server/Application/Services/AgentTaskLandService.cs`
   (+16) and `server/Antiphon.Server.csproj` (a `Compile` include of
   `scripts/lib/LeaseTempDirectory.cs`), which goes beyond the build-slot
   script.
4. Only the transcript holds the red evidence, and only the runner mirror
   holds the green receipt. Both are lost when the mirror is retired or the
   7-day transcript retention passes.

## Conclusion

Review it as-is (Opus). No re-run is needed: the brief arrived intact, the
red-then-green order is in the commits and recorded in the transcript, and
CP-1..CP-5 are green at `1e1a5a81e374492f821e50713db534828c69412b` with a
clean receipt. The subject of the Review is
`05802489ac0f43715a69c342b4429e07da5e0ecf`, base
`6414a71967575b2a7dfbf283ee273f930b7a2100`. The landing rebase must resolve
three conflicts: the registry count, for which the agent proposes 21 with
the null-producer arm covering both `ClaudeScratch` and `DotnetSdkEmpty`,
and the two land-graph records, which a refresh regenerates.

## Not done, noted

- Fix idea (not designed): attribution should accept the most recent
  task-marked report turn when the later turns' prompts are harness-injected
  (CLI background notices or rules re-reads), not task prompts.
- No builds, tests or changes outside this note were made. I did not run
  `check-evidence-diff`, the pin prints or land-graph `-Check`.
