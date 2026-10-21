# CARD-1388: witnesses for the released-lease ticket refusals

Plan date: 2026-10-21. Target: **master only**. Stage: Plan. TestDesign is the next stage.
Source inspected at `dcb461ce03c9bd8ea7d0edebfc637af33917776b` (`origin/master`, and this branch tip).
No product code is changed by this Plan task. No test was run. The gap is the absence of witnesses, confirmed by search.

## Outcome

`ValidateTicketAsync` still refuses a prepared land when, after R is released for verification and then reacquired, the captured ticket, the land-worktree identity, the tracked tree, or the land-worktree HEAD no longer matches. Nothing under `tests/` names `verification_ticket_changed`, `land_worktree_identity_changed`, `verification_tracked_tree_changed`, or `land_worktree_changed`. Add four witnesses on the existing CARD-1379 lease-window harness. Each witness fails if its own guard is inverted. The committed change is tests only.

## Ground truth

Card text from `scripts/card.ps1 get CARD-1388 -Board Antiphon`. The review that filed it is right about the missing names. The guards themselves are present.

| Premise | What this master tip does | Consequence |
|---|---|---|
| No test names the four codes. | `git grep` of the four strings on `origin/master` hits only `server/Application/Services/AgentTaskLandingProtocol.cs`. `tests/` has no hit. | The gap is open. Do not close the card. |
| The codes guard the released-lease window. | `AgentTaskLandService` calls `PrepareAsync`, releases R, runs `VerifyPreparedAsync`, reacquires R, then `ResumeAsync`. Resume reloads the operation and request and calls `ValidateTicketAsync` before any push (`AgentTaskLandingProtocol.cs` 76-81, then 596-636). A `LandingRefusal` from Prepared sets `LastReason`, `Publication=Refused`, and `Phase=Refused`, and returns before `PushStarted`. | A witness pauses the real verifier, mutates one fact, releases the child, and asserts that refusal. |
| One code is one guard. | `verification_ticket_changed` is also the null operation/request check at line 78 and the phase/eligibility check at lines 599-602. `land_worktree_identity_changed` is also capture's `rev-parse --absolute-git-dir` (line 584), the common-directory check (lines 611-612), and the registration predicate (lines 620-621). `land_worktree_changed` is also the rebase HEAD check at line 271, which runs before the window. | This card binds the four window behaviours below. It does not add a witness per `Require` site. |
| A HEAD move during the pause proves `land_worktree_changed`. | The post-reacquire registration check requires `land[0].Head == ticket.Identity.CandidateSha` before the porcelain check and before `rev-parse --verify HEAD^{commit}`. A HEAD move made while R is released changes the live list and refuses as `land_worktree_identity_changed`. Line 636 never runs. | The HEAD witness moves HEAD after that list has been parsed, inside the validation call. |
| `origin/rc/2026-10-20` needs the same tests. | Tip `aeaf16647b5188468ebd1638708508b07a421b7a` has `AgentTaskLandingProtocol.cs` and none of `ValidateTicketAsync`, `CaptureTicketAsync`, or the four codes. | No backport. The tests would not compile on that line. |

`RepairSourceWorld` registers its `ControlledTaskProgressGit` as `ILandingGit` (`RepairSourceWorld.cs` `BuildServices`). `BeforeCommand` runs before `RunAsync`. `LandVerificationWindowHarness` pauses inside `VerifyPreparedAsync`: `Started` completes after the child process starts and before `Release()` writes the file the child is waiting on. That pause is after `CaptureTicketAsync` and before `ValidateTicketAsync`.

`GET /api/runner-defaults` revision 2 was read on 2026-10-21 (`defaultRunnerId` empty). `GET /api/session-runners` showed an eligible Windows runner, an eligible Linux runner, and one draining runner that is not dispatch-eligible. Omit `-Runner`. Omit `-Platform`. Any OS. The harness already uses `pwsh`, which this Linux runner has.

## Decisions

- **D-1 — Four methods on `LandVerificationLeaseWindowTests`.** Keep `[Category("Integration")]` and `[ParallelLimiter<ProcessSpawnLimit>]`. Do not add a class. Do not edit `C1379_StartRefAndWorktreeFinishBeforeVerificationRelease`. Do not add these codes to `C688_RealLandProfile`.
- **D-2 — One behaviour per code, on the C1379 harness and the real git fixture that harness already uses.** `RepairSourceWorld.CreateAsync(ordinaryCodeTask: true, landingVerifier: child.Verifier)`, `LandAgentTaskRequest` with a non-empty filter (the harness writes a passing trx only when a results directory is present), `RunAsync`, wait for `Started`, mutate, `Release()`, await the land. No start-ref dispatch and no second land. Record `origin/master` of the fixture remote before `RunAsync`.
- **D-3 — Bind each witness to the Require that observes that behaviour.**

  | Witness | Mutation, after `Started` and before `Release`, unless noted | Guard that must be the refusing line | Inversion that must make this witness fail |
  |---|---|---|---|
  | `C1388_TicketFieldChangeRefusesResume` | On a second `DbContext`, set that operation's `VerificationFilter` to `c1388-filter` and save. Leave phase `Prepared` and the request `Running`. | Lines 604-610, the captured-ticket equality. | `Require(!(current.Identity == ticket.Identity && …))` so a differing filter does not throw. |
  | `C1388_LandWorktreeIdentityChangeRefusesResume` | `rev-parse --absolute-git-dir` in `LandWorktreePath`, then overwrite `<git-dir>/locked` with `c1388-not-antiphon-land`. Do not delete the file, change HEAD, or replace it with a link. | Lines 622-625, the lock-reason read. `LandWorkspace.LockReason` is `antiphon-land`. | `Require(text.Trim() != LandWorkspace.LockReason, …)` so a wrong reason does not throw. |
  | `C1388_TrackedTreeChangeRefusesResume` | Append one line to tracked `README.md` in the land worktree. Do not stage, commit, or create an untracked file (`--untracked-files=no` would ignore it). | Line 633, porcelain length 0. The land path is checked before the source path. | `Require(status.Succeeded && status.Output.Length != 0, …)` so a dirty tracked tree does not throw. |
  | `C1388_LandWorktreeHeadChangeRefusesResume` | Arm `world.Git.BeforeCommand` only after `Started`. On the first `rev-parse` `--verify` `HEAD^{commit}` whose directory is the land worktree, commit a new file with `ScratchGitRepo.GitInAsync` (that path does not re-enter `BeforeCommand`), disarm, then return null so the real rev-parse runs. | Line 636. | `Require(head.Succeeded && head.Output.Trim() != ticket.Identity.CandidateSha, …)` so a moved HEAD does not throw. |

- **D-4 — Refusal assertions, same for every witness.** `LastReason` equals that witness's code. `Phase` is `Refused`. `Publication` is `Refused`. `PushStartedAt` is null. `RemoteConfirmedAt` is null. The fixture origin's master SHA is unchanged. A timeout, an exception from `RunAsync`, or a different `LastReason` is a failed witness, not a pass.
- **D-5 — Red is a temporary inversion, then one green run.** The guards already exist, so the new tests pass on an unmodified protocol. Red is not a committed failing test. After S1 is committed, invert one guard as in D-3, rebuild, run that one method, expect 1 executed and 1 failed on the `LastReason` assertion. Restore `AgentTaskLandingProtocol.cs` with `git checkout --` before the next inversion. Do not commit an inversion. Then one green run of the four methods on the clean tree. A red run that still passes, or that fails before the assertion, stops the slice. Do not loosen the assertion in that same change.
- **D-6 — Master only.** No rc backport, no rc activation, no server restart. Sibling `Require` sites that share a code, and `verification_sequencer_changed`, stay untested here.
- **D-7 — Portable lane.** Omit `-Runner` and `-Platform`, per the runner reads above.

Rejected: mutate HEAD while the child is paused (that refuses as identity, so inverting line 636 would leave the witness green); one parameterized test for all four codes; a new harness; editing the protocol in the committed slice; caching the ticket; running the Unit lane or `C688_RealLandProfile`.

## Slice

One slice. Tests only. Any OS.

| Slice | Files | Change | Witness |
|---|---|---|---|
| S1 | `tests/Antiphon.Tests/Application/LandVerificationLeaseWindowTests.cs` | D-1 through D-4. A private helper may share the world, pause, and refusal assertions. Each method names one code. | Four reds per D-5, then CP-1. |

Do not edit `server/Application/Services/AgentTaskLandingProtocol.cs` in any commit. Do not edit historical plans.

## Verification design

TestDesign remains a separate stage. The table below is the closed manifest to adopt. Do not re-investigate the gap.

The four red runs are unlisted on purpose. The checkpoint tool requires a clean tree, and D-5 forbids committing the inverted guard. Each red is one method filter, one `bin-c1388/` build, through `scripts/build-slot.ps1`. Report `executed=1 failed=1` and the assertion name. Restore the protocol file before the next red and before CP-1.

Green is one narrow run of the four new methods. It does not include `C1379_StartRefAndWorktreeFinishBeforeVerificationRelease` and it is not the Unit lane. These witnesses are Integration because they hold a real repository lease and a real git worktree. Delete every `bin-c1388` directory after the green run.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---|---:|---|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1388/` | ticket-refusals | `/*/*/LandVerificationLeaseWindowTests/C1388_*` | D-3, D-4 | 4 passed, 0 failed | 4 | 20 | true |

### Cost

CP-1 is 20 minutes. The four unlisted red rebuilds are about 8 minutes each (32) and sit outside the table because they are dirty on purpose. Authoring the four methods is about 40 minutes. Code `-ExpectAbout` is about 90 minutes.

## Shipment

Commit and push S1 to `feat/card-task-93425ce1` with `git push origin HEAD:refs/heads/feat/card-task-93425ce1`. Fetch `origin/master` and run `git merge-tree --write-tree origin/master HEAD`. Do not merge or rebase the task branch. Land master only, through the normal land path.
