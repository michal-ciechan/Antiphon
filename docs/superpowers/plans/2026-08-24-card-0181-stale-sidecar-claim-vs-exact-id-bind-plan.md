# CARD-0181 — A stale transcript claim must never beat the session the file is named for — plan

**Date:** 2026-08-24 · **Card:** CARD-0181 (`674388eb-8900-461e-8964-a35491c1affd`), a
GitHub-imported Critical bug report with live evidence ·
**Status:** plan (no implementation in this pass) ·
**Verified against:** `master` @ `be92955`. The incident was captured on `4909bbf` (2026-08-23);
64 commits have landed since, and **zero** of them touch the code this design changes —
`TranscriptTailer.cs`, `TranscriptClaimRegistry.cs`, `TranscriptSidecar.cs`,
`TranscriptCandidateProbe.cs`, `TranscriptBindingIncidentService.cs` all have an empty
`git log 4909bbf..HEAD`; `SessionRunnerRuntime.cs` changed only in the herdr arms (CARD-0160/0162/0164),
not in `RestoreTranscriptClaims` / `AdoptOrphanedHostsAsync` / the Claude re-tail path. Every
file:line below was re-read out of the code on `be92955`.

**Where the live state is — and is not.** The card's evidence is from the reporter's machine
(`C:\Users\mike.ciechan`, cwd `D:\src\project\predictionMarkets`). This machine's server answers
**404** for the victim session `eaf64b4c-…`, so nothing here can be poked; the mechanism below is
reconstructed from the card's artefacts plus the code, and the two places where the card's evidence
cannot distinguish alternatives are named as such, with the exact log line that settles each.

**Established facts (Investigate, this pass):**

- **C1 is absolute in `TryBind`, including for the exact-id step.** `TranscriptTailer.TryBind`
  (`src/Antiphon.SessionRunner/TranscriptTailer.cs:392-397`) calls `_claims.TryClaim(path, _sessionId)`
  first, for every `how` — `Exact` (`LocateAsync` step 2, `:323-329`) included — and on refusal logs
  **Debug** ("already claimed by another session; not adopting") and returns false. The registry
  (`TranscriptClaimRegistry.cs:32-37`) is one `GetOrAdd`: first claimant wins, forever, with no
  notion of *why* it holds the file. Nothing in the system ranks "the filename is this session's id"
  above "some session got there first".
- **The census that feeds the fault report deliberately skips the exact file, so an exact-id C1
  loss is invisible.** `EvaluateCandidates` `continue`s on the basename match (`:446`, "handled by
  the fast path") before counting cwd matches, so a session whose own `<id>.jsonl` is held by
  somebody else reports `0 cwd-matched, 0 refused` (`FormatCensus`, `:760`) through
  `MaybeReportNoCandidates` (`:711`) — the card's "no cwd-matching transcript candidates …
  2455 file(s) … 0 cwd-matched" while the correct file sat one directory away. The Warning is
  factually wrong about the shape of the fault.
- **Sidecar restore re-asserts every recorded path unconditionally, dead sessions included.**
  `SessionRunnerRuntime.RestoreTranscriptClaims` (`SessionRunnerRuntime.cs:520-531`) iterates
  `TranscriptSidecar.LoadAll` and `TryClaim`s any non-null `TranscriptPath` for the sidecar's
  `SessionId`, before adoption (`:375`, and pinned that way by
  `Claims_are_restored_from_sidecars_before_new_adoption_runs`). It checks nothing about the path
  — not the basename, not whether another sidecar's session is the file's namesake, not whether the
  claimant is alive. Sidecars outlive their session by **14 days** (`:326-350`, pruned only when the
  session is also absent from `_sessions`), and the doc comment (`:515-519`) says the outliving
  claim is *deliberate*: "a previous session's transcript must never become adoptable by a new
  one". That intent is right for the dead session's **own** file and wrong for a file it bound
  heuristically — the code cannot tell the two apart because the registry has one strength.
- **The sidecar's `how` is overwritten on every restart, so the original bind method is gone.**
  Step 1 of `LocateAsync` re-binds the known path with `how = Sidecar` (`:305-311`), which calls
  `onBound` → `RecordTranscriptBinding` (`:1170-1174`) → `SaveSidecar(current with { How = how })`.
  The poisoned sidecar reads `"how": "sidecar"` for exactly this reason; it says nothing about how
  `912eb415` first came to hold `eaf64b4c-….jsonl`. **The line that does say is the Warning
  `"<session-id>.jsonl never appeared (Claude forked the id); adopting {Path} ({How})"` (`:337-341`)
  for session `912eb415` in the reporter's `logs/session-runner.log` (or its rolled-aside
  predecessors) around 2026-08-21T08:58:45Z — first thing the implementer should pull.**
- **Two theft vectors exist in the code as written, and BOTH pass every rule C1–C4.** Either
  produces the card's artefacts; the log line above picks between them.
  1. **The migration shim** (`:504-513`): `_restartAdopt && _knownTranscriptPath is null &&
     shimEligible.Count == 1` binds the unique cwd-matching file written in the last 20 s on
     C1+C2+C2b+C3 alone — no C4. C3 (`EpochOk`, `:554-559`) rejects only files *older* than the
     child; the victim's file (first record 2026-08-21T08:58:45) is 16 h *newer* than the thief's
     child (2026-08-20T16:25:35) and passes. C2b passes because both incarnations were launched
     `--name PredictionMarkets-Orchestrator`. The comment calls it "removable one release after
     deploy" (deployed 2026-08-09, CARD-0006); it is not restricted to pre-sidecar sessions — **any**
     restart-adopted session that had not bound before the restart qualifies, which is precisely
     the shape of an orphaned always-on incarnation that never got past `Not logged in`. It is the
     "same cwd, written recently" heuristic CARD-0006 deleted, re-admitted for one launch path.
  2. **Discovery on templated text.** C4 excludes local-command records (`TranscriptCandidateProbe.cs:239-242`,
     so `/remote-control` and `/rename` cannot match), but a channel-bound always-on agent's launch
     note is a fixed string — `ChannelPreamble.BootstrapBody` / `RestartResumeBody`
     (`server/Application/Services/ChannelPreamble.cs:85-93`), both ≥ 12 chars, both plain user
     prompts. Incarnation A's in-memory `SessionInputLog` (if A was never restarted) holds the same
     text incarnation B's file records; C2, C2b and C3 pass as above; `Discovery` binds. "Text this
     session actually sent" is not identifying when every incarnation of the agent sends it.
- **After either theft the victim can never recover.** Its exact-id step fires every 250 ms
  (`LocatePollInterval`), loses C1 at Debug each time, and step 3 skips the file. The only release
  paths are the thief's tailer being disposed (`:804`, `ReleaseAll`) — which happens on session
  exit or runner shutdown, and on the next restart the thief's sidecar re-asserts the claim before
  the victim is adopted. Fifty hours is not an accident of timing; it is the steady state.
- **Why the thief was still there to steal.** `AgentControlService.StartAsync`
  (`server/Application/Services/AgentControlService.cs:96`) is idempotent on
  `HasLiveSessionAsync` and kills nothing; when supervision starts a replacement, the previous
  incarnation's pty-host is only ever killed through an explicit stop. `SessionReconciliationService`
  auto-kills only `Stopped` rows (`SessionReconciliationService.cs:261`), re-adopts `Failed`, and
  alerts on "no row". A runner-`Running` session whose DB row is `Running` but no longer any
  agent's `PersistentSessionId` matches **no arm**. The card cannot say which state `912eb415`'s row
  is in; that is the second thing to pull from the reporter's DB. It is CARD-0056's leak in the
  supervision direction and is scoped out below with the evidence it needs, not fixed here.
- **The thief's bind was itself a CARD-0006 false positive.** Session `912eb415` ingested 9 of the
  orchestrator's entries as its own (card §4) and, had it been channel-bound, would have relayed
  them. This card is not "the safety rules were too strict"; it is "a heuristic bind was allowed to
  outrank a positive one, and then to keep the spoils".

**Related:** CARD-0006 (C1–C4, the never-weaken constraint this design is held to), CARD-0073 (the
empty-census fault this card's Warning came through), CARD-0101 (`TranscriptBindStuck`),
CARD-0056 (launch-leak reconciliation; the supervision-side twin is scoped out to a new card),
CARD-0064 (queued-delivery C4 evidence — untouched).

## Verdict up front — the five decisions

1. **A claim carries a strength, derived from the path and the claimant, never declared by the
   caller.** `Exact` iff the file's basename is the claimant's own session id; `Heuristic` otherwise.
   Exact outranks heuristic; nothing outranks exact; heuristic-vs-heuristic stays first-wins. Restore
   from sidecars derives the same way, so a sidecar naming `<X>.jsonl` for session `Y ≠ X` is restored
   as a heuristic claim and yields to `X` the moment `X`'s exact-id step runs. (§1)
2. **Displacement is an event, not a silent overwrite.** The displaced session's live tailer stops
   reading the file, drops it from its sidecar, returns to `LocateAsync`, and publishes a new fault
   kind `ClaimRevoked` naming the new owner; the server records a new incident kind
   `TranscriptClaimRevoked` (Warning; Critical when the displaced agent is channel-bound, because it
   had been relaying a stranger's turns). (§2)
3. **A file named for another session this runner knows is never a discovery candidate — "C0".**
   If the candidate's basename is a GUID other than ours and a sidecar for that GUID exists under
   `<SessionLogPath>/transcripts/`, the runner *knows* whose file it is by name and refuses it for
   everyone else, with the namesake in the refusal reason. This closes both theft vectors before
   the exact bind is even needed and is strictly narrowing. (§3)
4. **Delete the migration shim.** It is the deleted CARD-0006 heuristic under another name, its own
   comment scheduled its removal for one release after 2026-08-09, and it is the more likely of the
   two theft vectors. Restart-adopted sessions that never bound stay unbound until new input lands
   — the outcome the Codex path already accepts (`SessionRunnerRuntime.cs:1125-1129`). **Operator
   decision, recommended yes**; §4 gives the narrowed form if the answer is no.
5. **Say who holds it.** An exact-id C1 loss becomes a rate-limited Warning naming the holder and a
   census line `exact file held by <id>`; sidecar restore logs a Warning per heuristic claim on a
   file named for another known session. "0 cwd-matched" stops being false comfort. (§5)

**The never-weaken argument, stated once.** CARD-0006's rules exist so that no session binds a
conversation that is not its own. Decision 1 permits exactly one bind that today is refused: a
session binding the file whose basename is the session id Antiphon generated and passed to Claude
as `--session-id` — the identification `LocateAsync` already ranks first ("the filename IS the
positive identification", `:323`), above every heuristic. No heuristic bind becomes easier; the
claim it displaces is, by construction, a heuristic claim on a file positively identified as another
session's — the false-positive class the rules were written to prevent, seen from the thief's side.
Decisions 3 and 4 refuse more than today, never less. Decision 2 and 5 add signal only. C2, C2b,
C3, C4 and the fork-follow rules (`TryFindNewerFork`, `:585-640`) are untouched, and every existing
pin in `TranscriptAdoptionSafetyTests` stays green except the one that pins the shim (§4).

**Residual, named:** a file whose basename Claude *self-chose* (an id-forked transcript) has no
namesake and C0 cannot protect it; it stays under C1–C4 exactly as today, including the templated
launch-note weakness in C4 for two same-named incarnations that both forked. Making the launch note
session-unique (a bracketed `[session <id:8>]` suffix at delivery in `AgentSessionService.DeliverLaunchNoteAsync`)
would close that too; it is a one-line server change with a prompt-text blast radius and is left
as a follow-up card, not folded in here.

## 1. Decision 1 — claim strength, derived

**Registry.** `TranscriptClaimRegistry` (`src/Antiphon.SessionRunner/TranscriptClaimRegistry.cs`)
stores `(Guid Owner, ClaimStrength Strength)` per canonical path. `TryClaim(path, sessionId)`
computes `strength = IsNamesake(path, sessionId) ? Exact : Heuristic` where `IsNamesake` is
`Path.GetFileNameWithoutExtension(path)` parsed as a GUID (`D` format, case-insensitive) equal to
`sessionId`. Resolution:

| existing | incoming | result |
|---|---|---|
| none | any | claimed |
| same owner | any | claimed (idempotent, strength upgraded if now Exact) |
| Heuristic (other) | Exact | **displaced** — new owner set, previous owner returned |
| Exact (other) | Heuristic | refused |
| Heuristic (other) | Heuristic | refused (today's behaviour) |
| Exact (other) | Exact | unreachable — two sessions cannot both be the namesake; refused, logged at Warning as a defensive branch |

`TryClaim` returns a `ClaimResult { bool Claimed; Guid? Displaced; }` (a record, so the existing
`bool` call sites become `.Claimed` — five in the runner: `TranscriptTailer.cs:394`,
`CodexTranscriptTailer.cs:367`, `RestoreTranscriptClaims`, and the two test call sites). New
`OwnerOf(path) → (Guid, ClaimStrength)?` for the messages in §5. `IsClaimedByOther`, `Release`,
`ReleaseAll`, `Snapshot` unchanged in contract.

**Why derived, not declared.** A `how` parameter on `TryClaim` would let the sidecar re-tail
(`how = Sidecar`) or a future caller assert `Exact` on a path that is not the session's namesake —
which is exactly the poisoned sidecar. The basename is a fact Claude wrote for the process launched
with that `--session-id`; letting only that fact confer strength means no caller, and no file on
disk, can promote a claim.

**Why the namesake of a Grok / Codex file is not a problem.** Grok's `updates.jsonl` and Codex's
rollout files are never named `<sessionId>.jsonl`; their claims are all `Heuristic` and behave as
today. `GrokTranscriptTailer` takes no registry at all (`SessionRunnerRuntime.cs:944`).

**Restore.** `RestoreTranscriptClaims` calls the same `TryClaim`, so strength derives from the
sidecar's `(TranscriptPath, SessionId)`. Nothing else changes in restore except the Warning in §5.
Restore order (before adoption, `:375`) is kept and its pin stays.

**Resume.** A `--resume` relaunch reuses the session id (`AgentControlService.cs:154`,
"same id, `claude --resume`"), so the relaunch's exact claim is the same owner: idempotent, no
displacement. The waiver of C3 on resume is untouched.

## 2. Decision 2 — displacement is an event

**Runtime wiring.** `TryClaim` returning `Displaced = Y` is observed inside `TryBind` of the winning
tailer, which has no handle on `Y`'s tailer. Route it through the runtime: `TranscriptClaimRegistry`
gets an `event Action<string path, Guid previousOwner, Guid newOwner>? ClaimDisplaced` raised from
`TryClaim`; `SessionRunnerRuntime` subscribes once at construction and, on the event, looks up
`_sessions[previousOwner]` and calls `RunnerSession.OnTranscriptClaimRevoked(path, newOwner)` if the
session is present (a restored-from-sidecar claim for a session that was never adopted has no
`RunnerSession` — Warning log only, `"claim restored from the sidecar of session {Prev} on {Path}
was displaced by its namesake {New}; {Prev} is not a live session"`).

**Tailer side.** `ITranscriptTailer` (`ITranscriptTailer.cs`) gains
`void NotifyClaimRevoked(string path, Guid newOwner)`. In `TranscriptTailer`:

- If `BoundTranscriptPath` equals `path` (canonical compare): set a `volatile` revoked flag the
  `RunAsync` tail loop checks each poll (`:162-217`), log Warning
  `"Session {SessionId}: transcript {Path} was reclaimed by its namesake session {NewOwner}; it was
  never ours. Dropping it and resuming discovery."`, publish
  `RunnerTranscriptFaultEvent(sessionId, TranscriptFaultKinds.ClaimRevoked, detail, CandidatePath: path)`
  with the new owner id in `Detail`, invoke a new `onUnbound` callback (sibling of `onBound`) so
  `RunnerSession` writes the sidecar back with `TranscriptPath = null, How = null`, then re-enter
  `LocateAsync` with `_knownTranscriptPath` cleared (a field, no longer `readonly`) so step 1 cannot
  re-bind the same file. Sequence numbers keep counting up — the server dedups by line uuid, and
  entries already emitted are not retracted (see "out of scope").
- If the tailer is still in `LocateAsync` (not yet bound) — nothing to drop; the registry already
  refuses the path from here on.

`CodexTranscriptTailer` implements `NotifyClaimRevoked` as the same drop-and-rediscover; `GrokTranscriptTailer`
as a no-op (it holds no claims).

**Server side.** `TranscriptFaultKinds.ClaimRevoked = "ClaimRevoked"`
(`SessionRunnerContracts.cs`, beside `:501-510`). `TranscriptBindingIncidentService.OnTranscriptFaultAsync`
(`TranscriptBindingIncidentService.cs:47`) branches on the kind: `ClaimRevoked` records
`AgentIncidentKind.TranscriptClaimRevoked = 35` (next free value after `HerdrStatusDisagreement = 34`)
with message `"This session had been reading the transcript of session {new owner} as its own; that
file has been handed back. Nothing ingested from it belonged to this session."`, Warning, Critical
when channel-bound — and, unlike `TranscriptBindFailed`, it does **not** feed `MaybeEscalateStuckAsync`
(`:129`): a revocation is a one-off, not a continuing refusal; the refusal that follows it (the
displaced session now running unbound) reports through the existing path with its own clock.
`UnboundSeconds`/`Repeat` are 0/1 on the event.

**What about the sidecar of a session that is not live?** Its `TranscriptPath` stays as written —
the runtime has no `RunnerSession` to write through — but it is harmless: on the next restart it
restores as `Heuristic` and yields again. §5's restore Warning keeps it visible until the 14-day
prune (`:326-350`). Rewriting a sidecar for a session the runner does not own is deliberately not
done; the sidecar is that session's record.

## 3. Decision 3 — C0, "a file named for another known session"

In `EvaluateCandidates` (`TranscriptTailer.cs:440-460`), immediately after the existing
`continue` for our own basename (`:446`) and before C1:

```
// C0 — CARD-0181. A transcript whose basename is another session's id is that session's by
// name, and this runner knows the session exists because it wrote a sidecar for it. No amount
// of content evidence makes it ours: two incarnations of one always-on agent share cwd, --name
// and a templated launch note, so C2/C2b/C4 all pass — and C3 only rejects OLDER files.
if (TryReadNamesake(file) is { } namesake && namesake != _sessionId && _knownSessions.Exists(namesake))
{
    refusals.Add($"{file}: named for session {namesake:D}, which this runner launched");
    continue;
}
```

`_knownSessions` is a small injected `IKnownSessionProbe` (`bool Exists(Guid)`) whose production
implementation is `File.Exists(TranscriptSidecar.PathFor(sessionLogPath, id))` — a sidecar is
written **before** the tailer starts (`SessionRunnerRuntime.cs:828` herdr, `:978-988` pty), for every
Claude and Codex session, so existence of the sidecar is existence of the session as far as this
runner is concerned, for 14 days after it ends. Null probe (unit tests that construct a bare tailer)
disables C0, the same convention `claims: null` uses. The same check goes into `TryFindNewerFork`
(`:585`) beside its C1 line (`:600`): a sibling's exact-named file is not a fork of ours.

**Why "known", not "GUID-shaped".** Claude's self-chosen fork ids are GUIDs too; refusing every
GUID-named file that is not ours would refuse every id-forked transcript and break discovery
entirely. The sidecar is what turns "a GUID" into "a session we launched".

**Why this is not enough on its own.** C0 is a runner-local fact and the registry is per process;
it protects discovery, not the exact step. Decision 1 is what makes the exact step win when the
claim already exists (restored from a sidecar written before this change, or by any future path).
The two are belt and braces; each is independently strictly narrowing / strictly correct.

## 4. Decision 4 — the migration shim

Delete `:504-513` and the `shimEligible` bookkeeping in `EvaluateCandidates`; keep
`TranscriptBindMethods.MigrationShim` (`SessionRunnerContracts.cs:533`) as a read-only constant,
because stored sidecars and `TranscriptBoundByDiscovery` incident rows carry the string, with its
doc comment changed to "historical; never written since CARD-0181". `_restartAdopt` gates nothing
but the shim (`:72`, `:125`, `:512` are its only uses), so the constructor parameter and its
`:1161` call-site argument go with it.

**What a restart-adopted, never-bound session then does:** exact step every 250 ms (finds its own
file if Claude honoured `--session-id`, which it usually does), otherwise discovery under C0–C4 with
an empty input log, so C4 cannot pass until the next delivery — at which point it can. Reported via
the existing empty-census / refusal faults. This is the documented Codex behaviour already
(`SessionRunnerRuntime.cs:1125-1129`: "running unbound is a fault to report, never a reason to relax
the rules").

**If the operator says keep it:** it must at least (a) apply C0, and (b) require `shimEligible[0]`
to have been *created* after the child start (`File.GetCreationTimeUtc > _childStartUtc`), not just
written in the last 20 s — a file that predates the session is never its own. That narrowed form is
still a bind without C4 and the plan's recommendation stands.

**Blast radius:** `Restart_without_sidecar_uses_migration_shim_only_for_unique_candidate`
(`tests/Antiphon.SessionRunner.Tests/TranscriptAdoptionSafetyTests.cs:627`) — its second half
("exactly one active candidate: adopt") inverts to "stays unbound and reports"; its first half
(two candidates: refuse) still holds and is kept. Nothing else references the shim by name outside
`TranscriptBindingIncidentService`'s doc comment and `RunnerTranscriptBoundEvent`'s.

## 5. Decision 5 — observability

- **Exact-step C1 loss** (`TryBind`, `:394-399`): when `how == Exact` and the claim is refused,
  log **Warning** (rate-limited by reusing the `_refusalFaultRepeat` cadence — a new
  `lastExactLossReport` timestamp in `LocateAsync`) `"Session {SessionId}: its own transcript {Path}
  is held by session {Holder} ({Strength}); not adopting."` using `OwnerOf`. After Decision 1 this
  fires only in the exact-vs-exact defensive branch; it stays because the registry contract is the
  thing being trusted and a future regression should be loud.
- **Census** (`CandidateVerdict`, `:418`): add `string? ExactFileHeldBy` populated by the exact
  step (the tailer records the last exact-step refusal in a field the verdict copies).
  `FormatCensus` appends `", exact file held by {id}"` when set, and `IsEmptyCensus` (`:753`) treats
  a held exact file as **not** an empty census — it is a refusal with a reason, reported through
  `MaybeReportRefusal` with the holder named, so `TranscriptBindFailed`'s `failureReason` and detail
  say what is actually wrong.
- **Restore** (`RestoreTranscriptClaims`, `:520`): for each restored `Heuristic` claim whose path's
  basename is a GUID ≠ the sidecar's session, log Warning `"Sidecar for session {Prev} claims {Path},
  a file named for session {Namesake}{, which also has a sidecar here}. Restored as a heuristic
  claim; {Namesake}'s own bind will displace it."` Count them in the existing
  `"Restored {Count} transcript claim(s)"` line as `({Heuristic} heuristic, {Exact} exact, {Suspect}
  on another session's file)`.
- **`TranscriptSidecar.How` is not rewritten on a sidecar re-tail.** `RecordTranscriptBinding`
  keeps the *original* `How` when the new one is `Sidecar` and the path is unchanged
  (`SessionRunnerRuntime.cs:1170-1174`), so the next incident of this shape still says
  `migration-shim` / `discovery` on disk. Small, free, and it is the fact this investigation could
  not recover.

## 6. Out of scope, stated

- **The leaked previous incarnation** (`912eb415` still Running on the runner with no agent
  pointing at it). That is supervision-side (CARD-0056's shape, in the AlwaysOn direction) and needs
  its own card with this evidence pulled first from the reporter's DB: `AgentSessions` row status
  for `912eb415`, `Agents.PersistentSessionId` history, and whether `AgentSupervisorService` started
  `eaf64b4c` as a *replacement* (Failed row → restart) or the operator started it. Whichever it is,
  the design above means a leaked incarnation can no longer take the live one's transcript; it can
  still burn a pty-host and a login.
- **Retracting misattributed entries.** The thief's 9 ingested entries remain on `912eb415`'s server
  rows. A `ClaimRevoked` incident names the file and the new owner, which is enough for an operator
  to judge; automated deletion of transcript rows is not something this card should introduce.
- **Session-unique launch notes** (the C4 residual, above) — follow-up card.
- **The per-process registry limitation** (two runners sharing one `~/.claude`) — unsupported
  configuration, unchanged.
- **Herdr sessions** — same Claude tailer, same registry, covered by construction; no herdr-specific
  arm.

## 7. Verification / test design

**Registry (unit, new `TranscriptClaimRegistryTests`, `tests/Antiphon.SessionRunner.Tests/`)**

- `Exact_claim_displaces_a_heuristic_claim_and_reports_the_previous_owner`
- `Heuristic_claim_never_displaces_an_exact_claim`
- `Heuristic_vs_heuristic_stays_first_wins`
- `Same_owner_reclaim_is_idempotent_and_upgrades_to_exact_when_it_is_the_namesake`
- `Strength_is_derived_from_the_basename_not_asserted` (a `Sidecar`-style caller cannot obtain
  `Exact` on a file it is not named for)
- `Canonical_path_variants_share_one_claim` (existing behaviour, now pinned)

**Tailer / runtime (`TranscriptAdoptionSafetyTests`, additions)**

- `THE_CARD_0181_shape_exact_id_bind_displaces_a_stale_sidecar_claim_after_restart` — two sidecars
  on disk: thief `Y` with `TranscriptPath = <X>.jsonl, How = migration-shim`, victim `X` with
  `TranscriptPath = null`; `AdoptOrphanedHostsAsync` restores; a tailer for `X` binds `<X>.jsonl`
  `Exact` within one poll; `Snapshot()` for `X` contains the file's entries; the registry owner of
  the path is `X` with `Exact`; a `ClaimDisplaced` event named `Y → X`.
- `A_live_tailer_that_loses_its_claim_stops_reading_and_resumes_discovery` — two tailers on one
  registry with C0 disabled (null probe) so the theft can be staged: `Y` heuristically bound to
  `<X>.jsonl`; `X` starts; `Y.BoundTranscriptPath` becomes null, `Y` publishes
  `SessionTranscriptFault` kind `ClaimRevoked` with `X` in the detail, `Y`'s sidecar path is
  cleared, lines appended after the revocation appear in `X.Snapshot()` and not in `Y`'s.
- `C0_a_file_named_for_another_known_session_is_refused_even_on_a_content_match` — sidecar for `X`
  exists; `Y`'s input log contains a prompt present in `<X>.jsonl`; verdict refuses with the
  namesake in the reason; `X` absent from the sidecar directory → today's behaviour (adoptable on
  C4) to prove "known" is the gate, not "GUID-shaped".
- `Templated_launch_note_does_not_let_a_previous_incarnation_bind_the_next_ones_file` — both
  tailers `--name` equal, both input logs hold `ChannelPreamble.BootstrapBody`'s text (copied
  literally into the test; the runner project does not reference the server), `Y`'s child start
  earlier than `<X>.jsonl`'s first record; refused under C0. This is the vector-2 regression lock.
- `Clear_fork_named_for_a_sibling_session_is_not_followed` — `TryFindNewerFork` under C0.
- `Restart_adopt_without_a_bound_transcript_stays_unbound_until_new_input` — replaces the shim
  test's second half; after a delivery whose text lands in the file, discovery binds via C4.
- `Exact_step_loss_names_the_holder_in_the_refusal_report` — forces the defensive branch through a
  test-only registry seam (`TryClaim` pre-seeded via `Snapshot`-shaped fixture) and asserts the
  `AdoptionRefused` fault detail contains the holder id and `FormatCensus` contains
  `exact file held by`.
- `Restoring_a_sidecar_that_names_another_sessions_file_is_heuristic_and_logged` — log capture on
  `RestoreTranscriptClaims`.
- `Sidecar_retail_keeps_the_original_how`.

**Server (`tests/Antiphon.Tests/Application/TranscriptBindingIncidentTests.cs`, additions)**

- `ClaimRevoked_fault_records_TranscriptClaimRevoked_warning`
- `ClaimRevoked_fault_is_critical_when_the_displaced_agent_is_channel_bound`
- `ClaimRevoked_fault_never_escalates_to_TranscriptBindStuck`

**Existing pins that must stay green (run targeted, `--treenode-filter`):** all of
`TranscriptAdoptionSafetyTests` except the inverted shim half — in particular
`Preexisting_actively_written_transcript_in_same_cwd_is_never_adopted`,
`A_file_claimed_by_another_live_tailer_is_refused_even_when_it_qualifies`,
`Two_sessions_in_one_cwd_adopt_their_own_forks_not_each_others`,
`Resume_fork_with_copied_old_timestamps_is_adopted_on_content_match`,
`Claims_are_restored_from_sidecars_before_new_adoption_runs`, the CARD-0064 queued-evidence
group; `TranscriptTailerCompactionTests` (fork follow); `CodexTranscriptTailerTests`
(`Sidecar_path_is_retailed_directly_after_restart_with_no_discovery` and the C1 case);
`TranscriptBindingIncidentTests` (4). `Antiphon.SessionRunner.Tests` must be run **alone**
(process-spawning lane, AGENTS.md) and to an alternate output path while the daemons hold `bin/`.

**Live verification after deploy (S4, in order, on the reporter's machine — this machine has no
copy of the state):**

1. Pull `logs/session-runner.log` history for `912eb415` around 2026-08-21T08:58:45Z; record which
   vector it was in the card's comments (`adopting … (migration-shim)` vs `(discovery)`).
2. Deploy; restart the session-runner **without** touching the sidecars. Expected in the log, in
   order: `Restored 1 transcript claim(s) (1 heuristic … 1 on another session's file)` with the
   Warning naming `eaf64b4c`; `Adopted pty-host for session 912eb415`; `Tailing transcript
   …\eaf64b4c-….jsonl for session 912eb415` (sidecar step, heuristic); `Adopted pty-host for session
   eaf64b4c`; `Tailing transcript …\eaf64b4c-….jsonl for session eaf64b4c`; the Warning from
   `912eb415` that its file was reclaimed.
3. `GET /api/sessions/eaf64b4c-…/transcript` → entries > 0 within a minute (the tailer re-reads from
   offset 0; the server dedups by uuid). `GET /api/attention` → the `TranscriptBindStuck` item clears
   on the next incident sweep; a single `TranscriptClaimRevoked` incident on whichever agent owns
   `912eb415` (or the unowned standalone alert if none does).
4. Only then apply the card's operator mitigation for the leaked incarnation (stop `912eb415`),
   which is now hygiene rather than the fix.

## 8. Build order

- **S1 — registry strength + displacement + C0 (runner only).** `TranscriptClaimRegistry`,
  `TranscriptTailer` (TryBind, EvaluateCandidates, TryFindNewerFork, revoke handling),
  `CodexTranscriptTailer` (same two touches), `ITranscriptTailer`, `SessionRunnerRuntime`
  (subscribe, route, `onUnbound`, `IKnownSessionProbe`), `TranscriptFaultKinds.ClaimRevoked`.
  Registry tests + the first five tailer tests. Independently shippable and already fixes the live
  case end to end.
- **S2 — shim deletion** (operator decision; default yes). One test inverted, one added.
- **S3 — observability + server incident.** Exact-loss Warning, census line, restore Warning,
  `How` preservation, `AgentIncidentKind.TranscriptClaimRevoked = 35`,
  `TranscriptBindingIncidentService` branch, three server tests.
- **S4 — AGENTS.md gotcha + live verification** on the reporter's machine per §7, and a comment on
  CARD-0181 with the vector identified in step 1.
- **Follow-up cards to raise, not build here:** the leaked always-on incarnation (supervision-side
  CARD-0056 twin); session-unique launch notes.

Estimate: S1 ≈ 3–4 h verification floor + authoring, S2 < 1 h, S3 ≈ 2 h, S4 depends on access to
the reporter's machine.
