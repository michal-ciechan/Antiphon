# Review a549ddb0 — CARD-1621 round 1 (diff-only)

Subject: feat/card-task-38030aae tip ab0e1410f7cb58751ec5e8866e6b1e6a67c96ad3, range
3a49b60133327f4441f06698ef3369b1d277d049..ab0e1410f7cb58751ec5e8866e6b1e6a67c96ad3 (3 commits, 2 files).
Code owner: task 38030aae-1001-4138-9976-8dd50077995b. Worker report `.antiphon/task-38030aae.md` is not on the
subject branch, and this mirror does not have it either, so I could not check the worker's own mutant claims
against their report. I reran each mutant myself instead.

Verdict: 1 defect (D-1, a runner startup crash; it can never kill a process). Ownership proof, forgery
surface, save atomicity and witness validity all hold.

## Defects

**D-1: a Herdr protocol fault on the re-list call escapes adoption and crashes the runner at startup.**
- Where: `src/Antiphon.SessionRunner/SessionRunnerRuntime.cs:2401`. The catch in
  `RecordLegacyLinuxIdentityAsync` only handles `HerdrApiException or HerdrBackendUnavailableException`.
- Failure: a legacy launched sidecar reaches R1. The bar's `pane.process_info` succeeds, then the second call made
  by the new code returns a protocol-level fault: a null or malformed result, or a mismatched request id
  (`HerdrProtocolException`, HerdrClient.cs:424/429/536/554). The exception leaves `AdoptHerdrSessionsAsync`
  before `_sessions.TryAdd` (:2223), and `AdoptOrphanedHostsAsync` throws to Program.cs:208, so the runner fails to
  start. On the pending path (:2326) it escapes `SweepVanishedSessionsAsync`, which stops that tick's retries of
  the remaining pending sessions.
  - Reproduced with a scratch probe that I did not commit: `fake.TransformResult` returns `null` for the 2nd
    `pane.process_info` call. Output: `PROBE calls=2 thrown=Antiphon.SessionRunner.HerdrProtocolException
    msg=Herdr returned a null 'pane.process_info' result.` The probe failed 1/1.
- Why it matters: the item-1 contract is "any failed check changes nothing". The bar this code follows,
  `EvaluateHerdrBarAsync`, catches every exception. The new RPC adds an uncaught failure type to the startup
  path. Kills still fail closed, so this is not an unproven kill.
- Fix:
  - Also catch `HerdrProtocolException`, or every non-cancellation exception, and return the sidecar unchanged.
  - Add a witness: a protocol fault on the 2nd `process_info` call → session `Running`, the sidecar still has no
    `childLinuxIdentity`, and a P8 kill is refused.

## Verified sound

1. **No kill of an unproven process.**
   - The re-list is herdr's own `pane.process_info` RPC over the herdr socket (HerdrClient.cs:355), never a value
     read from the sidecar.
   - Two equal kernel identity reads (boot id + stat field 22) bracket that listing. A reused pid that belongs to
     an unrelated process cannot be listed in our pane, and a pid that dies and is reused between the reads
     changes the start ticks, so recording is refused.
   - The `IsAlive(LaunchedAtUtc)` check has a 2-minute tolerance and would be weak on its own, but nothing depends
     on it alone.
2. **Origin default.**
   - A null `Origin` is treated as launched. That is correct: `Origin` and `Attached` were added in the same commit
     (23de792a54, CARD-0224 S1), so every sidecar without the field predates attach.
   - Attach always writes `Origin=Attached` (HerdrPaneChild.cs:259).
   - Attached sessions detach and never pid-kill (HerdrPaneChild.cs:1124). The orphan and pending kills also check
     the origin.
3. **Forgery.**
   - Adoption trusts only the sidecar's `ChildPid`, `PaneId` and `Origin`. The identity values come from `/proc`.
   - Anyone who can write `SessionLogPath/herdr` could already write `childLinuxIdentity` directly, so the change
     does not widen the attack surface.
4. **Atomic save.**
   - `SaveAtomic` writes a temp file, fsyncs it, then renames it over the sidecar while holding the snapshot mutex.
   - Both call sites hold the pane lease (:2184, :2319), which serialises them with
     `TryKillPendingUnderPaneLeaseAsync`.
   - A crash before the rename leaves the old legacy sidecar in place, so kills stay refused and the next adoption
     retries. A leftover `*.json.<guid>.tmp` does not match the `*.json` glob.
   - If the save fails, the in-memory sidecar keeps the proof (the session is adopted from `upgraded`).
5. **Item-2 placement.** The refusal is guarded by `OperatingSystem.IsLinux()`, so these witnesses belong in the
   Linux-only `Card1619LinuxStopIntentTests` class. Accepted.

## Test runs (worktree build at ab0e1410f, Linux server2)

| Run | Result |
|---|---|
| Card1619LinuxStopIntentTests, run alone | 14/14 |
| HerdrAdoptionSweepTests | 22/22 |
| HerdrPaneChildKillTests | 6/6 |

**Mutants** (each one rebuilt, then run; the source was restored afterwards and `git status` is clean):

| Mutant | Result |
|---|---|
| M1: P8 refusal changed to kill anyway (HerdrPaneChild.cs:1213) | red 2/2, rerun red 2/2 (`KillAsync` should be False but was True) |
| M2: SessionRunnerRuntime.cs restored to a0da456b7, i.e. without the fix | Legacy witness red 1/1, at the identity assertion (`ChildLinuxIdentity` was null) |
| M3: pane re-list removed | survives 59/59 (Card1619 + AdoptionSweep + Attach) |
| M4: second identity read removed | survives 59/59 |
| M5: attached-origin guard removed | survives 59/59 (defence in depth only, see point 2) |

M3–M5 stay pending for Mutation:
- M3 needs a fake that drops the pid from `process_info` between the bar and recording.
- M4 needs a seam that changes the identity between the two reads.
- M5 needs an attached legacy sidecar witness that asserts the identity is not recorded.

**Other checks**

| Check | Result |
|---|---|
| Land graph `-Check` (evaluator built through build-slot, Release, `bin-land-audit` removed afterwards) | digest D92450D9AABCBF9C783B506F73DF06C45635F415D03E65C5F1A97AF415CC36BE, count 69, approved, exit 0 |
| `check-evidence-diff` over the range | commits=3, violations=0 |
| `merge-tree` onto origin/master f5283ad08 | clean |
| `merge-tree` onto origin/rc/2026-11-01 | clean (the change is master-only anyway) |

**Process deviation:** I ran all six mutant builds and runs inside one build-slot lease, as a single bash loop (held
384 s). The brief asks for one lease per build (CARD-1627). Every other build and run used its own lease.

**Scope:** the brief-named classes ran in full. The whole SessionRunner Unit lane did not, so
`ordinaryScopeCompleted` is Interim.

## Advisories
- A-1: the info log "Recorded Linux start identity..." (:2419) is also written after a failed save, right after the
  warning. Make it conditional on a persisted save.
- A-4 (known): a refused kill is logged as 'Herdr unreachable' and the runner raises no attention row.
