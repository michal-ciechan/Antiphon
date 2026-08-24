# CARD-0177 umbrella (+ CARD-0178 / CARD-0179 / CARD-0180) — Home send observability, context badge copy, Report-bug bundle — plan

**Date:** 2026-08-24 · **Cards:** CARD-0177 (`6df2a78d-d390-4ef3-ab49-8cec2c1fdfa0`, GitHub #3, umbrella +
evidence pack), CARD-0178 (`e057073a-4031-4d3c-a07d-92d14a7e1539`, badge), CARD-0179
(`473d3d58-466b-4a38-9ad1-303758bbb17b`, Report-bug), CARD-0180 (`05c57304-25ec-4038-9dea-f65c77c43f62`,
Home send / transcript unbound) · **Status:** plan (no implementation in this pass) ·
**Verified against:** `master` @ `be92955`. The report was taken on `be617ef` (2026-08-21, 172 commits
back); every claim below was re-checked against the current code, and every live fact was queried
this pass (2026-08-24 ~21:30–22:30 BST) against the running Aspire stack.

**Card topology, settled first (the brief's "IMPORTANT FIRST STEP"):** CARD-0178, CARD-0179 and
CARD-0180 are one-for-one the three numbered sections of CARD-0177 — same repro, same code
citations, same "Expected" bullets, same evidence links (each child says "Screenshots + zip on #3").
This is the GitHub reporter's own split: #3 is the umbrella with the attachments, #4–#6 are the
children, and the tracker import created all four. They are **not** accidental duplicates to close;
they are the work breakdown. Decision 1 below says how the four are used so nothing is designed or
built twice.

---

## Established facts (Investigate, this pass — LIVE-VERIFIED where marked)

### §A — the badge (CARD-0178)

- **Unchanged since the report.** `git log be617ef..HEAD -- client/src/features/agents/SessionContextBadge.tsx`
  is empty. `SessionContextBadge.tsx:47-51` maps every `fullness == null` to label `awaiting next
  turn`, tooltip and `aria-label` `Compacted — awaiting next turn`. The file's own doc comment
  (`:32-35`) says null is "the expected post-compaction state (and the pre-first-turn state)"; the copy
  speaks only the first.
- **The server folds FOUR distinct reasons into one `null`.** `SessionContextUsage.Compute`
  (`server/Application/Services/SessionContextUsage.cs`) returns `Fullness = null` when (a) there is
  no usage-bearing row at all (`usage is null`, `:74-78` — the pre-first-turn case); (b) a
  `CompactBoundary` or a `/clear` local command landed after the newest usage row (`IsInvalidator`,
  `:117-118` — the genuinely compacted case, the one the copy describes); (c) the provider contract is
  `Degraded` + `SelfReported` (`:88-95`, Grok's suppressed fullness, CARD-0153 S5); (d) a non-positive
  ceiling (`:97`, theoretical). `SessionContextUsageResult` (`:29-33`) also carries `TokensUsed`,
  which is non-null in case (b) and null in case (a) — the two are distinguishable server-side today
  and the distinction is dropped at the DTO: `AgentSessionSummaryDto.ContextFullness` is a bare
  `double?` (`server/Application/Dtos/BoardDtos.cs:93-96`), attached by `AttachAsync` /
  `AttachToBoardAsync` (`SessionContextUsage.cs:190-255`) and by `AgentService.cs:214`.
- **Consumers:** `client/src/features/home/AgentRail.tsx:76-78` (Home rail, ClaudeCode only),
  `client/src/features/board/SessionTabs.tsx`, `client/src/features/agents/AgentsPage.tsx`,
  `AgentCliModal.tsx`, `AgentFilesPage.tsx` (grep `SessionContextBadge`). The vitest file
  `SessionContextBadge.test.tsx:13-19` pins the current null copy (`awaiting next turn`, tone
  `awaiting`), so the fix is red-first by construction.
- **LIVE: both shapes exist right now.** `GET /api/agents` shows 13 live sessions; "Torquay Leander"
  (`2ee8234c`) is ClaudeCode with `contextFullness: null`, **175** transcript entries and a
  `ContextCompacted` incident at seq 172 (2026-08-19) — the copy is *correct* for it. A session with
  zero entries gets byte-identical copy. (Also seen: "school-revision" at `0` — a real 0%, not null,
  renders "0%"; not this card.)

### §B — Home "Send now" (CARD-0180)

- **The server path moved under the report's feet: CARD-0164 landed after `be617ef`.**
  `git log be617ef..HEAD -- server/Application/Services/SessionMessageQueueService.cs` includes
  `cf61893` (B2, unobservable-baseline transcript-first confirm) and `4bfec3b` (B4, Mode:Now grace
  before 409). The report's item 3.5 ("CARD-0055's gate degrades when the baseline transcript is
  empty") described the pre-0164 code; the current shape is:
  - `EnqueueAsync` Mode.Now (`SessionMessageQueueService.cs:163-262`): live-check → ready-check →
    herdr blocked-check → spill → `CaptureTranscriptBaselineAsync` (`:1298`; `Observable=false` when
    the session has zero `TranscriptEntries`, `:1296`) → per-session lock → `DeliverAsync` (`:1339`).
  - Unobservable baseline (`:1441`, `:1564`): the confirm loop polls for a `UserPrompt` row whose
    timestamp is inside the wall-clock floor (`UnobservableBaselineConfirmClockToleranceSeconds`,
    default 30, `SupervisionSettings.cs:237`), pulling via `CatchUpTranscriptAsync` between polls. At
    the deadline (`TranscriptConfirmTimeoutSeconds` = 30, `:182`): **screen sequence advanced ⇒
    `Delivered`**, announced only by a `LogWarning` "confirmed by degraded screen-only verdict …
    (bind-failed / pre-first-turn fallback)" (`:1617-1629`); no advance ⇒ `NoSubmitOutput` ⇒ B4 grace
    (`:225-247`) ⇒ `409 Conflict` with `HandleDeliveryFailureAsync`.
  - **So the report's "200 with no signal" is still true, with one change: the 200 now arrives after
    ~30 s instead of instantly.** On an unbound session whose terminal redraws (Claude echoing the
    typed text is enough — the composer render advances the sequence), Mode:Now returns
    `200 { messages: [], working: false }`. The degraded verdict reaches no incident, no DTO field and
    no UI. `SessionQueueDto` is `(SessionId, Messages, Working)` (`server/Application/Dtos/SessionQueueDtos.cs:44-47`);
    the client type mirrors it (`client/src/api/sessions.ts:83-87`).
- **The client has no success rendering at all.** `SmartComposer.dispatch`
  (`client/src/features/agents/SmartComposer.tsx:110-124`): on resolve, clear the textarea; on throw,
  a red notification. The Home dock (`client/src/features/home/HomePage.tsx:246-278`,
  `data-testid="home-dock"`) renders `SessionTranscriptPanel` with `withComposer composerCollapsed`
  (`:271`); that panel's empty state is "No transcript yet. Send the agent a prompt and the structured
  turn-by-turn flow appears here." (`SessionTranscriptPanel.tsx:351-353`) and its status badge is
  `Idle` whenever `working` is false (`:299-308`) — which an unbound session always is (AGENTS.md:
  "run with NO transcript … correctly reads idle").
- **Home surfaces transcript-bind incidents nowhere.** `AttentionService.cs` has no `TranscriptBind*`
  kind (grep empty; `AttentionKind` enum, `AttentionDtos.cs:13-45`, is BlockedQuestion /
  ParkedMessage / DeadSession / NeverStarted / UncorrelatedReport / …); the Home "Needs attention"
  link counts `/api/attention` items only (`HomePage.tsx:315-331`). Incidents are reachable only via
  `GET /api/agents/{id}/incidents` on the Agents page (`client/src/api/agents.ts:378-390`).
- **LIVE: the unbound-for-hours state is present in this deployment today.** `AgentIncidents`:
  Kind 15 `TranscriptBindFailed` — **1 331 rows over 19 sessions** since 2026-08-13; Kind 27
  `TranscriptBindStuck` — 39 rows, all on session `f04cd114` (the "Codex" always-on agent,
  `AdoptionRefused`, "STILL unbound after 12.6h of continuous refusal (152 report(s))" at 21:33 BST).
  On Home that agent shows a green terminal icon and `Idle`. (That one is the Codex tailer refusing
  stale rollouts on C3 — a different tailer from the Claude shape in the report, the same
  *invisibility*.)
- **The bind mechanism the report describes is real, reproducible by code reading, and unchanged:**
  `git log be617ef..HEAD -- src/Antiphon.SessionRunner/TranscriptTailer.cs` is empty.
  1. `LocateAsync` step 2 (`TranscriptTailer.cs:324-329`) finds `<sessionId>.jsonl` under **any**
     project dir and calls `TryBind(candidate, Exact)`. `TryBind` (`:392-398`) asks
     `TranscriptClaimRegistry.TryClaim`; on refusal it **`LogDebug`s and returns false** — no fault
     event, no Warning, and Debug is below the runner's minimum level, so nothing is written anywhere.
  2. The loop then falls into `EvaluateCandidates` (`:430`), which skips our own exact filename
     (`:445-446`) and every file claimed by another session (`:449-450`) *before* `cwdMatched++`. The
     census is therefore EMPTY and the session raises `TranscriptMissing: "no cwd-matching transcript
     candidates in {cwd}"` (`:733-735`) — **exactly the report's line, produced while the same-id
     jsonl sits on disk.** The message names the wrong thing (same genre as the MSB3552 / podman
     entries in AGENTS.md).
  3. What claims the owner's file first: the migration shim (`:503-513`) — `restartAdopt` && no sidecar
     path && exactly one cwd-matching, actively-written file that passed C2b/C3 and failed C4. Nothing
     excludes a file whose basename is **another session's id**. `RestoreTranscriptClaims`
     (`SessionRunnerRuntime.cs:520-531`) restores only sidecars that already carry a `TranscriptPath`;
     a session that had not bound before the restart (Claude creates the file lazily on the first
     submit) leaves its exact file unclaimed. `AdoptOrphanedHostsAsync` (`:388-425`) adopts in
     `Directory.EnumerateFiles` order and each tailer starts inside `AdoptAsync` (`:1153-1163`), so an
     orphan manifest (a pty-host whose server row is gone — the "no agent owns the session" line the
     report saw, `TranscriptBindingIncidentService.cs:196-203`) adopted before the real owner shims the
     owner's `<id>.jsonl` if it is the only active file in that cwd. Claims are held until dispose
     (`TranscriptClaimRegistry.cs:10-13`), so the owner's Exact bind is then refused **for the rest of
     the runner's life**, silently (step 1), while reporting `TranscriptMissing` (step 2).
  - **LIVE census (not currently struck here):** 453 transcript sidecars under
    `C:\logs\antiphon\session-runner\transcripts` — how = exact 121, deterministic 116, discovery 92,
    sidecar 30, migration-shim 5, unbound 89; **0 duplicate `TranscriptPath`s**; none of the 12
    heuristic-bind targets in `AgentIncidents` (Kind 16) is an `AgentSessions.Id`. The report came off
    a fresh stack with orphans — the precondition the shim needs — so the absence here is not
    evidence against the mechanism.
- **The report's item 3.4 (Claude "Not logged in")** is an environment fact on the reporter's
  machine, not a code path; it is in scope only as "an auth-failed turn must not read as success",
  which the binding + receipt work covers.

### §C — Report-bug (CARD-0179)

- **No prior art in the product.** `grep -rl ZipArchive server src` → nothing; `client/package.json`
  has no screenshot/zip library; there is no version/commit endpoint — the server exposes only
  `MapHealthChecks("/health")` (`server/Program.cs:575`), the runner `/health` and `/capabilities`
  (`src/Antiphon.SessionRunner/Program.cs:121,128`); `client/vite.config.ts` `define`s nothing and
  `Directory.Build.props` sets no `SourceRevisionId`/`InformationalVersion` (grep empty), so **the
  git SHA is not currently available at runtime anywhere** — the bundle needs a build-time stamp.
- **Nearest prior art is test-only:** `tests/Antiphon.E2E/Fixtures/TestDiagnostics.cs` writes a
  per-test server log slice, browser console + failed requests, the DOM at failure, and notes
  (AGENTS.md "Diagnosing a failing E2E test"). That is the right *shape*; it cannot be reused as code
  (it drives Playwright).
- **Where the state already is:** `GET /api/agents`, `/api/agents/{id}/incidents?take=`,
  `/api/sessions/{id}/buffer | /transcript | /messages`, `/api/attention`
  (`docs/antiphon-api.md:129-142`). Logs: server `logs/antiphon-*.log` (`Serilog:LogPath` = `logs`,
  `server/appsettings.json:154`); runner `%TEMP%\antiphon-logs\session-runner-*.log`; sidecars and
  per-session pty logs under `C:\logs\antiphon\session-runner` (`SessionLogPath`,
  `src/Antiphon.SessionRunner/appsettings.json:3`).

**Related:** CARD-0006 (the C1–C4 adoption rules — *nothing here relaxes them*), CARD-0101
(TranscriptBindStuck escalation), CARD-0055 / CARD-0024 / CARD-0164 (delivery verdicts — the
never-weaken rule applies: no change here may make a failed delivery easier to mark Sent), CARD-0082 /
CARD-0153 / CARD-0157 (context fullness), CARD-0035 (the attention feed's non-widening rule), CARD-0073
(the empty-census fault), CARD-0170 / CARD-0175 (these four cards are tracker imports; `#3`–`#6` are
their GitHub keys, not their identifiers).

---

## Verdict up front — the seven decisions

1. **Topology: 0177 tracks, 0178 / 0180 / 0179 build, this document designs — once.** The three
   children stay open and each carries its own implementation slices (below); CARD-0177 carries the
   evidence pack, links this plan, and closes last, when the three children are closed. Nothing is
   closed as a duplicate. Reasoning: (i) the children are the reporter's own decomposition and are
   mirrored on GitHub as #4–#6, so closing them here would close real upstream issues as "duplicate"
   while the work is still open; (ii) the three have different urgency, different code areas
   (client-only / runner+server+client / new feature) and different verification, so they want
   separate delegates and separate commits; (iii) one design doc referenced from all four is what
   prevents four investigations — the brief's actual worry. **Order: CARD-0180 → CARD-0178 →
   CARD-0179** (most user-impacting first; the badge is a one-slice fix that can also ride alongside
   0180; Report-bug is a standalone feature with no dependency on either, but its bundle becomes more
   useful once 0180 S4's binding state exists in the DTO). §1.
2. **CARD-0178: the server names why fullness is null; the client stops guessing.** New
   `ContextFullnessState { Known, NoUsageYet, Compacted, Cleared, Suppressed }` on
   `SessionContextUsageResult` and on `AgentSessionSummaryDto` (`contextFullnessState`, additive,
   nullable). `SessionContextBadge` takes `state` and renders: `NoUsageYet` → gray "no turns yet" /
   "No turns yet — context unknown"; `Compacted` → today's copy; `Cleared` → gray "cleared" /
   "Conversation cleared — awaiting next turn"; `Suppressed` → nothing rendered. Absent state (older
   server) → gray "unknown". The badge stays visible for the first two so an empty session is not
   mistaken for a session with no badge. §2.
3. **CARD-0180 S1 — the exact filename is claimed before any heuristic runs, and is never a
   heuristic candidate.** `AdoptOrphanedHostsAsync` gains a `PreclaimExactTranscripts` step after
   `RestoreTranscriptClaims`: for every manifest session id, claim `<id>.jsonl` wherever it exists
   under the projects root. `EvaluateCandidates` gains rule **C0**: a file whose basename parses as
   a `Guid` that is a session id this runner knows (live, adopting, or in a manifest) is skipped for
   discovery *and* for the shim — it can only ever be that session's exact file. §3.
4. **CARD-0180 S2 — an exact bind refused by a claim is a loud fault, and an exact/sidecar claim
   evicts a heuristic one.** `TranscriptClaimRegistry` records `How` per claim; `TryClaim(path, id,
   how)` lets `Exact`/`Sidecar` take a path held by `Discovery`/`Fork`/`MigrationShim`, invoking an
   eviction callback that returns the loser to `LocateAsync`. A refusal that remains (exact vs exact,
   which cannot happen without a duplicate id) raises `TranscriptFaultKinds.ExactFileClaimed` with
   the holder's id, instead of `LogDebug`. §3.
5. **CARD-0180 S3 — Mode:Now returns a delivery receipt, and a degraded verdict is an incident.**
   `SessionQueueDto` gains `LastDelivery: DeliveryReceiptDto?` (verdict, confirmedBy
   `transcript|screen|none`, degraded, reason, at) populated only on the Mode:Now response; the
   composer renders it inline (green "Delivered — confirmed by transcript", amber "Typed, unverified —
   this session has no transcript bound", never a toast that disappears). The screen-only fallback
   additionally records `AgentIncidentKind.DeliveryUnverified` (35, Warning; Critical when
   channel-bound), deduped per session per 10 min. **Mode:Now stays unpersisted** — no queue row
   (§3 says why the alternative loses). §4.
6. **CARD-0180 S4 — binding state is on the session DTO, and the Home dock says so.** Runner
   `RunnerSessionDto` gains `TranscriptBound: bool?` and `TranscriptBindHow: string?`; the server's
   live metadata carries them to `AgentSessionSummaryDto.transcriptBinding: 'bound' | 'unbound' |
   'unknown'`. `SessionTranscriptPanel`'s empty state becomes a warning banner when the session is
   live and `unbound`: "This session has no transcript bound. Messages are typed into the terminal
   but nothing can be read back — the agent will look idle with 0 turns regardless of what it does.
   See incidents." with a link. No change to `/api/attention` (CARD-0035). §5.
7. **CARD-0179 — one server endpoint builds the zip; the client contributes what only it has.**
   `POST /api/diagnostics/bundle` (body: route, selected agent/session ids, screenshot PNG, console
   ring, `includePaths`) returns `application/zip` built with `System.IO.Compression` (BCL). Server
   collects health, capabilities, version, sanitized agent/session/incident/queue/binding state,
   transcript *kinds*, log tails, the screen buffer; a `DiagnosticsRedactor` with a fixed rule list
   runs over every text member. Client: a header `Report bug` button + one on the agent dock,
   `html-to-image` for the screenshot, a 200-entry console/failed-fetch ring buffer. No GitHub
   integration, no tokens. §6.

---

## 1. Decision 1 — how the four cards are used

| Card | Role | Slices | Closes when |
|---|---|---|---|
| CARD-0177 | Umbrella: evidence pack, this plan, cross-links | none | 0178 + 0179 + 0180 closed; add a closing comment naming the three commits |
| CARD-0180 | Home send / transcript unbound | S1 C0 + pre-claim · S2 loud fault + eviction · S3 receipt + incident · S4 binding on DTO + dock banner | S1–S4 merged, §7 tests green |
| CARD-0178 | Badge copy | one slice (server enum + client) | merged, §7 tests green |
| CARD-0179 | Report-bug bundle | R1 server endpoint + redactor · R2 client button + capture · R3 build-time version stamp | R1–R3 merged |

Sequencing inside 0180 is S1 → S2 → S3 → S4 (runner first, because S4's DTO field needs S2's registry
`How`; S3 is independent of S1/S2 and may run in parallel on a second delegate if wanted). 0178 is
independent of everything and can be dispatched at any point. 0179 R3 (version stamp) is independent
and small; R1 should land after 0180 S4 so the bundle's session.json includes `transcriptBinding`
(otherwise the bundle would omit the one field that diagnoses the report's own item 3).

What is **not** done to the cards in this pass: no closes, no column moves, no description rewrites
(the descriptions are the imported GitHub text and `UpdateExisting` would re-assert them anyway —
CARD-0170 plan §A). The orchestrator links this plan from each card's comments.

## 2. Decision 2 — the badge (CARD-0178)

**Server.** `SessionContextUsageResult` gains `ContextFullnessState State`. `Compute` sets it at each
return: `:74-78` → `NoUsageYet`; the `Degraded`/`SelfReported` arm (`:88-95`) → `Suppressed`; the
invalidator arm (`:117-118`) → `Cleared` if the newest later invalidator is the `/clear` local command,
else `Compacted` (a CompactBoundary — `(auto)` and `(manual)` both count; the distinction is on the
incident row already); otherwise `Known`. `LoadFullnessAsync` returns `(double? Fullness, State)` per
session; `AttachAsync` / `AttachToBoardAsync` / `AgentService.cs:214` copy both. `AgentSessionSummaryDto`
gains `ContextFullnessState? ContextFullnessState = null` after `ContextFullness` — additive, so every
existing `with { ContextFullness = … }` site keeps compiling and the JSON gains one string.

**Client.** `client/src/api/agents.ts` `AgentSessionSummaryDto` gains
`contextFullnessState?: 'Known' | 'NoUsageYet' | 'Compacted' | 'Cleared' | 'Suppressed'`.
`SessionContextBadge` props become `{ fullness, state?, size }`; copy table:

| state | label | tooltip / aria | tone |
|---|---|---|---|
| `Known` (fullness non-null) | `42%` | `Context 42% full` | normal/warning/danger (unchanged) |
| `NoUsageYet` | `no turns yet` | `No turns yet — context unknown` | `awaiting` (gray) |
| `Compacted` | `awaiting next turn` | `Compacted — awaiting next turn` | `awaiting` |
| `Cleared` | `cleared` | `Conversation cleared — awaiting next turn` | `awaiting` |
| `Suppressed` | *(renders null)* | — | — |
| undefined + null fullness | `unknown` | `Context unknown` | `awaiting` |

`data-tone` stays as is; add `data-state` for tests. All five callers pass `state={s.contextFullnessState}`.
The report's "or hide the badge" option is rejected for `NoUsageYet`: the rail already hides the
activity badge when it has nothing to say (`AgentRail.tsx:96-126`), and a ClaudeCode row with *no*
badge would read as "not a Claude session" next to its siblings.

## 3. Decisions 3–4 — binding ownership (CARD-0180 S1, S2)

**S1 — C0 and pre-claim.** Two changes, both in the runner:

1. `TranscriptTailer` takes `Func<Guid, bool>? isKnownSession`. In `EvaluateCandidates`, immediately
   after the own-exact-file skip (`:445-446`): if `Guid.TryParseExact(basename, "D", out var g)` and
   `g != _sessionId` and `isKnownSession(g)` → `continue` (not a refusal, not a census member — it is
   somebody's exact file). This closes the shim hole *and* the discovery hole (a C4 match on a file
   named for another live session is possible when two sessions were sent the same ≥12-char text, e.g.
   two identical boot notes; the exact name outranks a text match, same ordering `LocateAsync` already
   uses). `SessionRunnerRuntime` supplies `id => _sessions.ContainsKey(id) || _adoptingIds.Contains(id)`,
   where `_adoptingIds` is the set of manifest session ids collected at the top of
   `AdoptOrphanedHostsAsync` before any tailer starts.
2. `AdoptOrphanedHostsAsync`: after `RestoreTranscriptClaims()`, `PreclaimExactTranscripts(manifestIds)`
   — one enumeration of `<projectsRoot>/*/<id>.jsonl` for the whole id set (not one walk per session),
   `TryClaim(path, id)` for each hit, logged at Information with the count. Claude's projects root is
   per-cwd but the exact filename is unique across it, so this needs no cwd knowledge. A pre-claimed
   path is what `LocateAsync` step 2 then binds as `Exact` — `TryClaim` is idempotent for the same
   owner (`TranscriptClaimRegistry.cs:36`).

The shim itself is **kept, narrowed by C0** — not deleted. It still has 5 live binds in this deployment
and the "removable one release after deploy" note has not been acted on; deleting it is a separate
call with its own census, not a side effect of a bug fix.

**S2 — the claim registry learns `How`, exact evicts heuristic, refusal is loud.**

- `TranscriptClaimRegistry._claims` becomes `ConcurrentDictionary<string, Claim(Guid Owner, string How)>`.
  `TryClaim(path, sessionId, how)`: same-owner → true; free → take; held by another with a heuristic
  `How` (`Discovery`, `Fork`, `MigrationShim`) and the caller's `how` is `Exact` or `Sidecar` → replace
  atomically (`TryUpdate` on the old value) and invoke `OnEvicted(path, loserId, winnerId)`; otherwise
  false. Restored claims from sidecars carry the sidecar's `How`. The existing 2-arg overload stays for
  callers/tests and means `how = Discovery`.
- `TranscriptTailer` registers for eviction: `Unbind(path)` clears `BoundTranscriptPath`, releases any
  other paths it holds for that conversation, publishes
  `RunnerTranscriptFaultEvent(Kind: TranscriptFaultKinds.ClaimEvicted, Detail: "…<path> is
  <winner>'s exact transcript", CandidatePath: path)`, and re-enters `LocateAsync` (the tail loop
  checks a volatile flag each poll and returns to locating). The loser then either finds its own file
  by C4 or reports the empty census honestly — which is the *correct* outcome for an orphan that was
  reading somebody else's conversation.
- `TryBind` on a refused **`Exact`** claim publishes `TranscriptFaultKinds.ExactFileClaimed` (new
  constant; `Detail` names the holder and its `How`) at Warning, rate-limited like the refusal reports,
  instead of `LogDebug`. Server side, `TranscriptBindingIncidentService.OnTranscriptFaultAsync` needs no
  change — the kind flows into `failureReason` and the message; the Stuck escalation applies as-is.
- The server-side `OnHeuristicBindAsync` unowned arm (`:196-203`) currently logs Error and records
  nothing; give it the same standalone `IAlertService` path the fault arm already has (`:68-83`),
  Warning, dedup key `supervisor:TranscriptBoundByDiscovery:unclaimed:<sessionId>` — a heuristic bind by
  a session nobody owns is precisely the orphan-captures-the-owner's-file shape and deserves a row.

**Why not "never claim in the shim, only in Exact"?** Because the 2026-08-09 incident is the other
direction: a claim taken by a heuristic is what keeps a *sibling* from double-reading. C0 + pre-claim
remove the specific wrong claim; eviction handles any residual race (a session launched fresh while
an orphan is mid-evaluation); the loud fault covers whatever is left. Every rule from CARD-0006 stays.

## 4. Decision 5 — the delivery receipt (CARD-0180 S3)

- `DeliveryReceiptDto(string Verdict, string ConfirmedBy, bool Degraded, string? Reason, DateTime At)`,
  `ConfirmedBy ∈ { "transcript", "screen", "none" }`. `SessionQueueDto` gains
  `DeliveryReceiptDto? LastDelivery = null`. `DeliverAsync` already returns `DeliveryOutcome`; add
  `ConfirmedBy` to it (set at the three return points `:1601` transcript, `:1629` screen, else none)
  and have the Mode:Now arm of `EnqueueAsync` return `(await GetQueueAsync(…)) with { LastDelivery = … }`
  on both the direct and the grace-confirmed (`:238`) success paths. Failure paths keep throwing 409 —
  the 409 body already names the verdict via `Describe`.
- **Incident.** In the screen-only fallback (`:1619-1629`): after the `LogWarning`, if the session's
  last `DeliveryUnverified` incident is older than 10 min, `RecordIncidentAsync(owner, sessionId,
  AgentIncidentKind.DeliveryUnverified = 35, channelBound ? Critical : Warning, "Send-now was typed but
  could not be confirmed: this session has no transcript bound (or has not written one yet). The
  terminal redrew, so the text probably landed — nothing can verify it.")`. Owner resolution reuses
  `ResolveOwningAgentIdAsync` (lift it from `TranscriptBindingIncidentService` to a shared helper —
  `AttentionService` has the same two-step lookup). This is *observation only*: no kill, no re-type,
  no change to the verdict — the never-weaken rule is untouched and so is its mirror (nothing here
  makes a delivery *harder* to mark Sent either).
- **Client.** `SmartComposer` keeps a `lastReceipt` state; `dispatch` sets it from the response when
  `target !== 'raw'` and renders a one-line `Text size="xs"` under the action row: green check
  "Delivered · confirmed by transcript" / amber "Typed · unverified — no transcript bound (see
  incidents)" / for WhenIdle "Queued". It clears on the next keystroke. Also invalidate the session
  transcript query on a confirmed receipt so the dock's "0 turns" updates without waiting for the
  SignalR push.
- **Why not persist Mode:Now as a queue row (the report's "consider persisting send-now attempts")?**
  `LateConfirmAttemptedMessagesAsync` (`:1071-1090`) and the parking/redelivery paths iterate rows by
  status; a Sent-on-arrival row is inert to them today but every future sweep would have to remember to
  exclude it, and `SessionQueuedMessage`'s contract ("send-now never stored") is pinned by tests. The
  durable half the operator actually needs is "did it land and how do I know" — that is the incident
  row (timeline, alertable) plus the receipt (immediate). If a history of send-now bodies is wanted
  later it is its own card.

## 5. Decision 6 — binding state on the DTO and the dock banner (CARD-0180 S4)

- Runner: `RunnerSessionDto` gains `bool? TranscriptBound = null, string? TranscriptBindHow = null`
  (contracts are additive; an older server ignores them). `RunnerSession.ToDto()` fills them from the
  tailer (`BoundTranscriptPath is not null`, and the sidecar's `How`); Grok/Codex tailers expose the
  same two via the common tailer interface.
- Server: `SessionRunnerEventPump`'s live metadata (`TryGetLiveMetadata`) carries both;
  `AgentSessionSummaryDto` gains `string? TranscriptBinding = null` — `"bound" | "unbound"` when the
  runner answered, `"unknown"` (null) otherwise — set where `AgentService` attaches live status.
- Client: `SessionTranscriptPanel` takes the summary (it already receives the session id; the Home
  page has the `agent.liveSession` in hand — pass `transcriptBinding` down; the Agents page fetches
  it). When `entries.length === 0 && transcriptBinding === 'unbound' && liveStatus === 'Running'`, the
  empty state is a Mantine `Alert color="orange"` with the copy in Decision 6 and a link to
  `/agents/<id>?tab=incidents`; the `Idle` badge is replaced by `Unbound` (orange) in that state so the
  header does not say "Idle / 0 turns" over a session that cannot be read. `AgentRail` adds a small
  orange dot on the terminal icon for the same condition (tooltip "Terminal live — no transcript
  bound").
- The attention feed is **not** widened: `/api/attention` is the phone-sized "a human must act" list
  (CARD-0035), and the action for an unbound session is an operator investigation, which the banner
  now points at. If TranscriptBindStuck (Critical) is wanted on the phone, that is a one-line addition
  to `AttentionService` under its own card, with its own non-widening argument.

## 6. Decision 7 — Report-bug bundle (CARD-0179)

**R1 — server.** `DiagnosticsEndpoints.cs`: `POST /api/diagnostics/bundle` with
`BugReportRequest(string? Route, Guid? AgentId, Guid? SessionId, string? ScreenshotPngBase64,
IReadOnlyList<ConsoleEntry>? Console, bool IncludePaths = false, string? Note = null)` →
`application/zip` named `antiphon-bug-<yyyyMMdd-HHmmss>.zip`. `DiagnosticsBundleService` writes, each
member best-effort with a `errors.txt` line instead of a failed request when a section throws (the
report's "works when server is partially unhealthy"):

| member | source | notes |
|---|---|---|
| `manifest.json` | request + server clock + `version.json` fields | route, ids, note |
| `version.json` | R3 stamp | server sha, runner sha (from `/capabilities` once R3 adds it), client sha (request header set by the client from its own stamp) |
| `health.json` | `GET /health`, runner `/health`, `/capabilities` | via the existing `ISessionRunnerClient` |
| `agent.json` | `AgentService` summary + `liveSession` (incl. `contextFullnessState`, `transcriptBinding`) | |
| `session.json` | session row, working, queue (`GetQueueAsync`), last 50 incidents | bodies of queued messages replaced by length + digest |
| `transcript-kinds.jsonl` | last 200 `TranscriptEntries` as `{seq, ts, kind, role, len}` | never text |
| `buffer.txt` | `GET /sessions/{id}/buffer` text | the screen the operator saw |
| `screenshot.png` | request | |
| `console.json` | request | |
| `server-log.txt`, `runner-log.txt` | tail 2 000 lines of the newest `logs/antiphon-*.log` and `%TEMP%\antiphon-logs\session-runner-*.log` | 2 MB cap each |
| `attention.json` | `/api/attention` | |

`DiagnosticsRedactor.Redact(string)` runs over every text member: `{{key:*}}` bodies, `sk-…`, `xoxb-`/`xoxp-`,
`ghp_`/`github_pat_`, `Bearer …`, Telegram bot tokens `\d{8,}:[A-Za-z0-9_-]{35}`, `Password=…;` in
connection strings, and — unless `IncludePaths` — `C:\Users\<name>` → `~` and each configured
project directory → `<project-N>`. Rules are a static list with a unit test per rule and a
"no-secret-shaped-string survives" property test over the fixtures. Redaction is by pattern, so the
doc must say plainly it is best-effort and the operator is asked to glance before uploading.

**R2 — client.** `ReportBugButton` in the app header (`AppShell` nav, right side) and in the Home dock
tab list; a modal with an optional note, an `Include local paths` switch (default off), and the
download. Screenshot: `html-to-image` (`toPng(document.body)`; same-origin, no CDN — the app is served
from its own origin, so the CSP concerns for artifacts do not apply here). Console: `consoleRing.ts`
patches `console.error|warn|log` and `window.onerror`/`unhandledrejection` at app start into a 200-entry
ring; `apiClient` pushes failed requests (`method url status ms`) into the same ring. The client sends
its own sha (from R3's `__ANTIPHON_SHA__` define) as an `X-Antiphon-Client-Sha` header.

**R3 — version stamp.** `Directory.Build.props`: `<SourceRevisionId>` from `git rev-parse HEAD` at
build time (MSBuild `Exec` with `ContinueOnError`, falling back to `unknown`) into
`InformationalVersion`; server exposes it on `/health`'s JSON (or a tiny `GET /api/version`) and the
runner on `/capabilities` (`RunnerCapabilitiesDto` gains `Version`). Client: `vite.config.ts`
`define: { __ANTIPHON_SHA__: JSON.stringify(execSync('git rev-parse HEAD')…) }` with the same
fallback. E2E builds already run `npm run build` before serving `client/dist`, so the stamp is exercised.

Explicitly not in R1–R3: opening GitHub, any token, uploading anywhere, full transcript bodies, the
per-session pty `.ansi.log` (too large and too raw; the buffer text covers the visible screen).

## 7. Out of scope, stated

- Deleting the migration shim (kept, narrowed — §3).
- Any change to the C1–C4 rules, `PromptSubmissionMatch`, or any delivery verdict's *conditions*
  (§4 adds observation only).
- Persisting Mode:Now bodies as queue rows (§4).
- Widening `/api/attention` (§5).
- The Codex tailer's C3 refusal of stale rollouts (`f04cd114`, 12.6 h unbound today) — a real, live,
  separate fault: the Codex agent's session is on a fresh child (`ChildStart 2026-08-22`) and every
  rollout in `~/.codex/sessions` predates it, so it can never bind until it writes a new one — which
  it will only do on a prompt. Recommend a card: "Codex always-on session never binds when no prompt
  has been sent since the relaunch". Not this design.
- The reporter's Claude "Not logged in" state (environment).
- Report-bug: automatic GitHub issue creation, uploading attachments via the tracker sync.

## 8. Verification / test design

Each slice is red-first; the test names below are the contract.

**CARD-0178**
- `SessionContextUsageTests`: `State_is_NoUsageYet_with_no_usage_rows`, `State_is_Compacted_after_a_boundary`,
  `State_is_Cleared_after_a_clear_command`, `State_is_Suppressed_for_a_degraded_self_reported_contract`,
  `State_is_Known_with_fullness`.
- `SessionContextBadge.test.tsx`: replace `null renders the awaiting next turn state` with one case per
  row of the §2 table (`data-state`, text, accessible name), plus `absent state with null fullness renders unknown`.
- `HomePage.test.tsx`: a rail row with `contextFullness: null, contextFullnessState: 'NoUsageYet'` shows
  `no turns yet` and never the word "Compacted".

**CARD-0180 S1/S2** (`tests/Antiphon.SessionRunner.Tests/TranscriptAdoptionSafetyTests.cs`, same
harness as `Restart_without_sidecar_uses_migration_shim_only_for_unique_candidate` and
`Claims_are_restored_from_sidecars_before_new_adoption_runs`):
- `Shim_never_takes_a_file_named_for_another_known_session` — two restart-adopted sessions A (no
  sidecar) and B; only `<B>.jsonl` exists and is active; A's verdict is empty census, B binds Exact.
- `Discovery_never_takes_a_file_named_for_another_known_session_even_on_a_C4_match`.
- `Exact_files_are_preclaimed_for_every_manifest_before_any_tailer_starts` — adoption order A then B,
  B's exact file is claimed by B before A's tailer evaluates.
- `An_exact_bind_evicts_a_heuristic_claim_and_the_loser_returns_to_locating` — A shims `<B>.jsonl`
  (registry seeded by hand to simulate the pre-fix race), B's Exact evicts, A publishes `ClaimEvicted`,
  B's `BoundTranscriptPath` is the file.
- `A_refused_exact_bind_publishes_ExactFileClaimed_not_a_debug_line` (registry seeded with an `Exact`
  claim by a third id).
- `TranscriptClaimRegistryTests` (new): `Exact_replaces_discovery`, `Sidecar_replaces_shim`,
  `Discovery_does_not_replace_exact`, `Same_owner_is_idempotent`, `Eviction_callback_fires_once`.
- `TranscriptBindingIncidentTests`: `An_unowned_heuristic_bind_raises_a_standalone_alert`.

**CARD-0180 S3** (`SessionMessageQueueDeliveryVerificationTests`, beside
`A_session_with_no_transcript_entries_keeps_the_legacy_screen_only_verdict` and the `Card0164_*` set):
- `Mode_Now_response_carries_a_transcript_confirmed_receipt`.
- `Mode_Now_screen_only_fallback_returns_a_degraded_receipt_and_records_DeliveryUnverified_once_per_window`
  (two sends inside 10 min → one incident row).
- `Mode_Now_degraded_receipt_is_Critical_when_channel_bound`.
- `Mode_Now_failure_still_throws_409_with_no_receipt` (existing `Send_now_throws_conflict_when_delivery_cannot_be_verified`
  extended with the assertion).
- Client `SmartComposer.test.tsx`: receipt line renders for each `confirmedBy`; clears on typing;
  never renders for `raw`.

**CARD-0180 S4**
- Runner: `RunnerSession.ToDto` reports `TranscriptBound=false/How=null` while locating,
  `true/"exact"` after bind, `true/"sidecar"` after re-adopt (`PtyHostAdoptionTests` fixture).
- Server: `AgentServiceIntegrationTests`: `Live_session_dto_carries_transcriptBinding_from_runner_metadata`.
- Client: `SessionTranscriptPanel.test.tsx`: `unbound live session shows the banner and an Unbound badge, not Idle / No transcript yet`;
  `HomePage.test.tsx`: the rail dot.
- E2E (one, `Antiphon.E2E`, through fakeclaude with the projects root pointed at an empty dir so the
  session stays unbound): send-now from the Home dock → amber receipt line + banner visible +
  `DeliveryUnverified` incident on the agent, within 45 s. Rebuild `client/dist` first (AGENTS.md).

**CARD-0179**
- `DiagnosticsRedactorTests`: one case per rule + `Home_path_is_kept_only_with_IncludePaths`.
- `DiagnosticsBundleServiceTests` (integration, `TestDbFixture`): the zip contains every member for a
  seeded agent/session; a throwing section yields an `errors.txt` line and a 200; no transcript text
  appears anywhere in the archive (assert over all members for a sentinel body).
- Endpoint test: 400 on a screenshot over 8 MB; content-type and filename.
- Client: `ReportBugButton.test.tsx` — modal, switch default off, posts the ring buffer; `consoleRing`
  unit test — 200-entry cap, failed fetch entries.
- R3: `HealthEndpointTests.Version_is_present_and_not_unknown_in_a_git_checkout`.

## 9. Build order

1. **CARD-0180 S1** (runner: C0 + pre-claim) — smallest, closes the capture. Commit + push.
2. **CARD-0180 S2** (registry `How`, eviction, loud fault, unowned-bind alert). Commit + push.
3. **CARD-0180 S3** (receipt DTO + incident + composer line). Commit + push. Independent of 1–2.
4. **CARD-0180 S4** (binding on runner DTO → server DTO → dock banner + rail dot + E2E). Commit + push.
5. **CARD-0178** (state enum + badge table + tests). Any time; one commit.
6. **CARD-0179 R3** (version stamp) → **R1** (endpoint + redactor) → **R2** (button + capture).
7. Close 0180, 0178, 0179 in that order; close 0177 with the three commit ranges in its reason.

Each step runs the targeted test classes above, then the touched assembly (`Antiphon.SessionRunner.Tests`
for 1–2 and 4; `Antiphon.Tests.Application` namespace chunk for 3–5; `scripts/test-client.ps1` for
every client change), building to `--property:OutputPath=bin-<name>/` while the daemons hold `bin/`,
and deploys with `scripts/restart-session-runner.ps1` (1, 2, 4) / `scripts/restart-apphost.ps1` (all).
