# CARD-0641 investigation: land outcome delivery delay, misleading LandAged, LocalTargetAdvanced→PushStarted gap

Date: 2026-09-23. Task 0de233ab (Investigate). Evidence comes from read-only SELECTs against `antiphon` (Postgres on 17280), `C:\src\Antiphon\server\logs\antiphon-20260923.log` (timestamps are +01:00; all times below are converted to Z), loose-ref mtimes under `C:\src\Antiphon\.git\refs\antiphon\land\`, the `master` reflog, and code at `13522df8`.

## Verdicts

| # | Question | Verdict |
|---|---|---|
| 1 | Why did outcome note `2cbed084` stay Queued? | **Confirmed.** WhenIdle did not cause this. The note was never enqueued: the reconciler will not enqueue an Outcome/Conflict note while the repository landing lease is held by *anyone*. Back-to-back lands in the same repo kept the lease held from 22:02:06Z to 22:12:04Z. It was enqueued at 22:12:08, typed at 22:12:10 and confirmed at 22:12:20. The delay was 10 min; the note was not lost. |
| 2 | ~4 min between LocalTargetAdvanced and PushStarted on 3c8db8ad | **Refuted as stated.** The measured LocalTargetAdvanced→PushStarted time is about **24 s** (22:05:50→22:06:14). The ~4 min is request start (22:02:09) → PushStarted, and a full `dotnet build` verification (89 s) plus repeated recheck passes account for most of it. |
| 3 | LandAged says `holder= ()` for a queued request | **Confirmed.** A request waiting in the in-process land channel has no persisted hold. `HoldingTaskId`/`HoldReasonCode` are written only when the worker itself fails to get the lease, and the single-reader worker never tries until earlier lands finish. The monitor knows nothing about queue position. |

## 1. Outcome note held behind other lands' lease

### Mechanism

- `AgentTaskLandNotificationService.ReconcileAsync`, `server/Application/Services/AgentTaskLandNotificationService.cs:99-117`: for `Kind is Outcome or Conflict`, when the note is not yet linked to a queue row, it calls `leases.TryAcquireAsync(repository)`. On `null` it does a bare `return`. It records no `LastErrorCode`, no state change and no backoff. The comment says the intent is to wait for *the producer* to release its lease. The probe cannot tell the producer's lease from a *later* land's lease.
- The lease is a per-repository OS file lock, `<common>/antiphon/landing.lock` opened `FileShare.None` (`server/Infrastructure/Git/RepositoryMutationLease.cs:15-16`). It also returns `null` while an unfinished repository child journal exists (`:19-23`). The landing worker is not the only holder: guarded worktree/verification removal takes it too (`GuardedWorktreeRemoval.cs:10`, `GuardedVerificationRemoval.cs:8`).
- The land worker is one reader that drains the channel back-to-back (`server/Infrastructure/Orchestration/AgentTaskLandHostedService.cs:23-31`). Each `RunRequestAsync` holds the lease for its whole run (`AgentTaskLandService.cs:258`, `await using`) and releases it only after `SettleLandedAsync` commits the Outcome note (`:428`). The next queued land reacquires it about 2–3 s later.
- Only the 5-second hosted scan (`AgentTaskLandNotificationHostedService.cs:92-121`) reconciles Outcome notes. No post-settle call exists: `ReconcileAsync` callers are the scan, DeliveryFailure recovery (`AgentTaskDispatcher.cs:3139-3153`, DeliveryFailure only), reply-service completion paths and legacy check notes. The scan has to sample the lease in the 2–3 s window between two lands, and it missed.
- WhenIdle is not the gate here. Once enqueued, `SessionMessageQueueService` delivered within 2 s because the caller was idle.

### Evidence (caller session 0b70a773)

| Time (Z) | Event | Source |
|---|---|---|
| 21:57:50 | land cf0a5737 (1a46751e) starts, takes lease | `AgentTaskLandRequests.StartedAt` |
| 22:02:06.58 | 1a46751e Outcome committed; note `2cbed084` created, State=Queued | notification row `CreatedAt` |
| 22:02:09.18 | land bedb9df3 (3c8db8ad) starts, lease retaken 2.6 s later | request `StartedAt` |
| 22:07:05.35 | 3c8db8ad Outcome committed; note `58578e19` created | notification row |
| 22:07:07.13 | receipt LandAged for `2cbed084`: `queue=; state=Queued; error=.` Its own Aged note `94ed69e6` (not lease-gated) is enqueued 22:07:14 and confirmed 22:07:24 | `AgentTaskEvents` type 32; notification row |
| 22:07:07.22 | land ec387695 (2a1af42b) starts, lease retaken 1.9 s later | request `StartedAt` |
| 22:12:04.32 | 2a1af42b Outcome committed; queue now empty | event type 24 |
| 22:12:08.75 | `2cbed084` finally enqueued (queue row 798620f3), EnqueueAttempts=1 | notification `EnqueuedAt` |
| 22:12:10.41 / 22:12:20.82 | typed / transcript-confirmed | queue row `SentAt`; note `ConfirmedAt` |
| 22:12:32 | `58578e19` enqueued (327 s late). With `cec25445` it stayed Pending until the caller went idle; SentAt 22:14:49 / 22:14:42 | queue rows badffffb, cbccbbe5 |

`EnqueueAttempts=1` and `LastErrorCode=null` on `2cbed084` show the early `return` at `:109`. Every other path either enqueues or records an error.

### Frequency (since 2026-09-10, non-legacy)

Outcome (Kind 3) notes: 147 enqueued, **13 enqueued more than 60 s after commit** (max 2,420 s). In 11 of the 13, at least one other land *started* inside the gap. That is the same mechanism. The other two (`eaf8eea8` 72 s on 09-17, `80139ea4` 266 s on 09-20) had no overlapping land start. They fit the other lease holders (worktree cleanup/removal, unfinished child journal), but that part was **not** verified.

### Consequences observed

- The caller gets a receipt **LandAged warning before the outcome itself** (Aged notes are not lease-gated). This is the card's "LandAged fires for completed lands" symptom.
- The aged text `queue=; state=Queued; error=.` gives no reason, because the lease-busy return records none.

## 2. The LocalTargetAdvanced→PushStarted "gap"

The phase has no timestamp column, so the timeline below comes from the loose recovery-ref mtimes (each `RecheckRemoteSourceAsync` fetches into a fresh `source-recheck/<guid>` ref, and each `ObserveAsync` into `remote-observed/<guid>`) and from the `master` reflog, alongside the operation row `1e26e08e`:

| Time (Z) | Step |
|---|---|
| 22:02:09.2 | request started (lease acquired) |
| 22:02:19 / 22:02:23 | source-observed ×2 (source resolution) |
| 22:02:28.7 | operation row created |
| 22:02:38 → 22:03:26 | 6× source-recheck, remote-observed, pins (inspection/pinning) |
| 22:03:22.7 → 22:03:43.3 | rebase (the base moved to 0d56c3dd when 1a46751e landed, so a rebase was needed) |
| 22:03:54.1 → 22:05:23.3 | **verification: `dotnet build --artifacts-path <temp>` ran in full for the doc-only change, 89 s** (verifier child 49480 drained 22:05:15, log line 66718). Verification ran because `RebasedSourceSha != OriginalSourceSha` (`AgentTaskLandingProtocol.cs:248-257`) |
| 22:05:28.3 | source-recheck (Verified block) |
| 22:05:39.9 | source-recheck (TargetAdvanceStarted block) |
| 22:05:48 | `master` reflog: `merge a856df75: Fast-forward`, then CheckTarget, then **LocalTargetAdvanced ≈ 22:05:50** |
| 22:05:54.1 | source-recheck (LocalTargetAdvanced block) |
| 22:06:05.8 | remote-observed (pre-push `ObserveAsync`) |
| 22:06:14.4 | **PushStarted** (`PushStartedAt`) |
| 22:06:32.9 / 22:06:33.3 | remote-observed after push; PublicationConfirmed |
| 22:06:33 → 22:07:03.6 | cleanup (3× cleanup-observed) |

The orchestrator's GETs of the task at 22:05:34, 22:06:07, 22:06:12 and 22:06:24 (log lines 66726–66758) fall inside this ~40 s window, which is when `LocalTargetAdvanced` and a856df75 on master were visible. "About 4 minutes at LocalTargetAdvanced" matches the request's age (from 22:02:09), not how long the phase lasted. "No git process running" was a single sample. The block between git children does in-process work (DB rechecks, and canonicalising 650 registration paths).

Why a segment still takes ~10 s: the LocalTargetAdvanced block (`AgentTaskLandingProtocol.cs:305-322`) runs `RecheckRemoteSourceAsync` (ls-remote plus fetch to GitHub), `RecheckSourceAsync` twice and `CheckTargetAsync` twice, plus `ObserveAsync` (ls-remote plus fetch). Each `RecheckSourceAsync` → `InspectAsync` → 2× `IdentityAsync` (`LandingGit.cs:168-199`). Each of those runs `git worktree list --porcelain` and canonicalises every registered path. Each `CheckTargetAsync` runs `worktree list` again (`TargetCheckoutAsync`, `:548-555`) plus `git status --untracked-files=all` in `C:\src\Antiphon`. Measured on this machine now (read-only commands, `GIT_OPTIONAL_LOCKS=0`):

| Command | Cost |
|---|---|
| `git worktree list --porcelain` in `C:\src\Antiphon` (**650 registrations**, 0 prunable) | 1.33 s |
| canonicalising all 650 registration paths (as `IdentityAsync` does) | 0.42 s |
| `git ls-remote --refs --exit-code <origin> refs/heads/master` | 1.06–1.17 s |
| `git ls-files --others --ignored --exclude-standard` on built worktrees | 0.76–7.1 s |
| `git status --untracked-files=all` in main checkout | 0.13–0.28 s |

That makes roughly 6 `worktree list` calls, 4 registration sweeps, 2 ls-remote plus 2 fetch round trips and 2 ignored-file listings per segment: about 10–15 s, which fits the observed 11.7 s (source-recheck → remote-observed) and 8.7 s (remote-observed → PushStarted). Every phase boundary repeats the same set. That set, together with the unconditional full build, is why a doc-only land takes ~5 min end to end.

## 3. `holder= ()` on a queued request

- `AgentTaskLandMonitorService.AddAged`, `server/Application/Services/AgentTaskLandMonitorService.cs:85-98`, formats `reason={HoldReasonCode}; holder={HoldingTaskId:N} ({HoldingTaskStatus})`. All three are null unless `HoldAsync` ran (`AgentTaskLandService.cs:958-975`), and `HoldAsync` runs only inside `RunRequestAsync` after that request is dequeued (`:258-281`).
- A request behind others in `AgentTaskLandQueue` (an in-memory `Channel`, `AgentTaskLandQueue.cs:14`) stays `State=Queued`, `Attempt=0`, `LastProgressAt=RequestedAt`. The monitor's clock starts at `RequestedAt` (`:30`), so a queue wait of `LandWarningSeconds` (300 s default, `DelegationSettings.cs:573`) fires a Warning with no holder.
- Evidence: event at 22:05:14.22Z on 2a1af42b: `Warning: Land Queued; requested 2026-09-23T22:00:12.6672030Z; no progress since 2026-09-23T22:00:12.6672030Z; attempt=0; reason=; holder= ().` At that moment 3c8db8ad (bedb9df3, started 22:02:09) held the lease and ec387695 was first in the channel. At request time (22:00:12) it was behind two lands: 1a46751e running and 3c8db8ad queued. It started at 22:07:07.
- The server already has the facts the message lacks: the running request (`AgentTaskLandRequests` with `State=Running` for the same `RepositoryPathSnapshot`) and the earlier pending ones. The monitor does not read them.

## Red tests the Plan should commission

1. **Outcome enqueues while a different land holds the lease** (`AgentTaskLandNotificationService`). Arrange an Outcome note for task A, with the repository lease held by a fake `IRepositoryMutationLease` on behalf of an unrelated land B. One `ReconcileAsync` should set `QueueMessageId` and `State=AwaitingReceipt`. Today: red, because it returns at `:109` with the note untouched.
2. **Lease-blocked Outcome leaves a reason** (same service). If the design keeps any lease wait, a blocked reconcile must persist a non-null reason (for example `LastErrorCode`), and the receipt LandAged detail must show it rather than `error=.`. Today: red; the note is unchanged.
3. **LandAged for a queued request names holder and position** (`AgentTaskLandMonitorService`). Arrange request R1 `Running` (task A) and R2 `Queued` (task B) on the same repository, with R2 older than `LandWarningSeconds`. `SweepAsync` should produce a Warning detail naming A as the holder and position 1. Today: red, `holder= ()`.
4. (Optional, perf guard) **Recheck cost per phase boundary**: with a counting fake `ILandingGit`, one pass from LocalTargetAdvanced to PushStarted invokes `worktree list` no more than N times. Include this only if Plan chooses to cut the repeated recheck work. Today it is about 6.

## Fix options (one line each; the design belongs to Plan)

- (1) Make the probe distinguish the producer's lease from another land's lease, or drop it once the producing request is terminal; alternatively, settlement reconciles the note right after releasing the lease.
- (1) Record lease-blocked as a reason code so both Attention and aged text explain the wait.
- (3) For a Queued request, derive the holder and position from the running and earlier pending requests for the same repository, and consider starting the no-progress clock at dequeue rather than at `RequestedAt`.
- (2) Not a bug as reported. Separate candidate cards: skip or narrow the `dotnet build` verification for changes that touch only docs, and memoise `worktree list` and registration canonicalisation within one phase pass (650 registrations also suggests worktree pruning is overdue).

## Remaining uncertainties

- The two delayed Outcome notes with no overlapping land start (09-17, 09-20) are attributed to other lease holders or an unfinished child journal only by elimination.
- The exact LocalTargetAdvanced instant is inferred (reflog ff at 22:05:48 + CheckTarget); it is not stored.
- The Aged (Kind 1) and TaskCompletion (Kind 6) outliers since 09-10 (max 3,394 s / 1,520 s) are not lease-gated and were not investigated.
- The theoretical reverse effect, where the scan's own probe briefly holds the lease and causes a land hold "owner unknown", has 0 occurrences since 09-16.

## Not done, noted

- No production code changed, nothing restarted, and no DB writes.
- Fix idea: the reconciler should wait only for the producing request's own lease episode, not for any lease holder.
