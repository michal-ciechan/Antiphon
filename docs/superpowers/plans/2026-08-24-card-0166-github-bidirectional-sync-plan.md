# CARD-0166 — bidirectional GitHub Issues ↔ Antiphon cards sync: activation, comments, labels, close-on-complete — plan

**Date:** 2026-08-24 · **Card:** CARD-0166 (`5d19a270-9c6e-457e-aad4-1e2252530ca9`) ·
**Status:** plan (no implementation in this pass) ·
**Verified against:** `feat/card-task-898789a6` @ `13d43f6`. Every file:line below was re-read out of
the code on that commit.

**Established facts, not re-derived here** (Investigate stage, task `65112c18`, findings on the
card 2026-08-24):
- The read-side sync (`IIssueTracker` / `GitHubIssuesTracker` / `ExternalTrackerSyncService` /
  `ExternalIssueRef`) is code-complete and **inert in this deployment**: `BoardService.CreateAsync`
  hardcodes `TrackerKind = Internal` (`BoardService.cs:129`), no API changes it, all 152 live
  boards are Internal, zero `ExternalIssueRefs` rows exist.
- Board tracker config is the YAML `tracker:` block in the active workflow definition, parsed by
  `IssueTrackerConfigParser.TryParse`, edited only through the Monaco `WorkflowEditor` via
  `PUT /api/boards/{id}/workflow` (`BoardEndpoints.cs:78`).
- No comment concept exists on cards. `CardRevision` is a supersede log (ContentEdit stores the
  OLD text), and `/api/cards/{id}/comments` is **already taken** by `CardReviewService
  .PostCommentAsync` — a session-stdin inject, not a stored comment (`CardEndpoints.cs:145`).
- GitHub REST live-verified: per-issue and **repo-wide** comment listing both support `since`
  (bad value → 422 naming the param); labels via full-replace `PATCH labels` or the `/labels`
  sub-resource; close/reopen via `PATCH state` + `state_reason` (completed / not_planned /
  duplicate / reopened).
- 9 real open issues (#3–#11) on `michal-ciechan/Antiphon` for read-side testing; write-side live
  testing must use a throwaway issue created for the purpose.
- Windmill on server2 is live and already runs Windows-side Antiphon scripts on schedules
  (the `antiphon_build_junk_cleanup` pattern).
- `gh` CLI holds `repo` scope on this desktop — an operator convenience, not a credential the
  Antiphon server process can rely on.

**Related:** CARD-0067 (claim-before-produce idempotency — the shape the outbound comment stamp
copies), CARD-0106 (the encrypted `ApiKeys` store + `ApiKeyEnvResolver` this plan reuses for the
server-side token), CARD-0019 (the card record is append-only; the discussion thread must not
pollute it).

---

## Verdict up front — the sixteen decisions

Numbered as on the card. Decision 3 first, because everything else assumes it.

3. **TrackerKind activation: derived from the workflow YAML on save — no new board PATCH API.**
   `WorkflowDefinitionLoader.UpdateAsync` (and the file-reload path) parses the front matter's
   `tracker.kind` and sets `Board.TrackerKind` to match, stamping a new
   `Board.TrackerActivatedAt` on the Internal→external flip. Removing the block flips back to
   Internal (sync stops; refs stay, inert). The YAML is already the only config surface; making
   the DB column a derived index of it removes the second activation step that kept the read sync
   dead for its entire life. §1.
1. **Write interface: one new capability interface, `IBidirectionalIssueTracker : IIssueTracker`**
   (comment pull + comment/label/state/create writes), implemented by `GitHubIssuesTracker` only.
   The sync service capability-tests (`tracker is IBidirectionalIssueTracker`); Jira/Linear remain
   read-only and untouched. §2.
2. **Credentials: all GitHub calls go through the server's `HttpClient`; the trigger script talks
   only to Antiphon.** Token resolution: new optional `tracker.token_key` naming a stored
   CARD-0106 API key (project scope then global, via `IApiKeyProtector`), falling back to the
   existing `api_key_env` env-var path. `gh` shell-out is rejected for the server (untestable with
   `StubHttpMessageHandler`, absent under a Scheduled-Task identity) and unnecessary for the
   trigger (the script calls an Antiphon endpoint; Antiphon holds the GitHub token). §3.
4. **Comment representation: a new `CardComment` entity, not a `CardRevision` kind.** A revision
   row is a supersede snapshot with one per-card monotonic sequence guarding edits; a discussion
   thread is append-only foreign text. Endpoints live at `/api/cards/{id}/discussion` because
   `/comments` is taken by the session-inject path. §4.
5. **Loop prevention: origin column + hidden marker + cursors — structurally incapable of
   echoing.** Inbound comments get `Origin = External` and are never selected by the OUT query
   (structural). Outbound comments carry a hidden HTML marker `<!-- antiphon:comment=<guid> -->`;
   the IN pull recognizes the marker and stamps the originating row's `ExternalCommentId` instead
   of importing a new row — so the echo closes the link rather than creating content. Marker, not
   actor-tag, because the PAT may be the operator's own account and actor exclusion would then
   swallow their genuine comments. State echoes are suppressed by
   `ExternalIssueRef.LastKnownExternalState`. §5, pins §11.
6. **What goes OUT as a GH comment:** (a) `CardComment` rows authored on the Antiphon side;
   (b) a closing comment on completion (terminal reason + card link) before the state PATCH;
   (c) a reopen comment; (d) for **import-origin** cards only, a content-edit summary comment
   carrying the new text (we do not edit a GH-owned body). Column moves are NOT comments — they
   are the `status:*` label (decision 9). §6.
7. **Where GH comments land IN: `CardComment` rows** (`Origin = External`, author = GH login,
   `CreatedAt` = GitHub's `created_at`, `ExternalCommentId`, `ExternalUrl`), shown in a new
   discussion section of the card modal. Never injected into agent sessions (the
   `CardReviewService` inject stays a separate, explicit act). §4, §6.
8. **Label taxonomy: hybrid.** Free-form card labels pass through under the per-origin authority
   rule (decision 11); the sync additionally OWNS two managed prefixes it derives and rewrites —
   `status:*` (always) and `priority:*` (export-origin only). Managed prefixes are stripped from
   the IN import so card labels never accumulate `status:done`. §7.
9. **Status-label derivation: one `status:<kebab(CardStatus)>` label** —
   `status:backlog | status:in-progress | status:review | status:done | status:blocked |
   status:canceled` from the card's `CardStatus` (the enum, `CardStatus.cs:3`), applied via the
   `/labels` sub-resource (add new + delete stale managed only — never full-replace on
   import-origin issues, so concurrent GH label edits are not clobbered). Antiphon is
   definitionally authoritative for this prefix (GH has no columns); a divergent `status:*` label
   edited on GH is rewritten on the next sync, documented as owned. §7.
10. **Close-on-complete:** card terminal `Done` → close with `state_reason=completed`; terminal
    `Canceled` → close with `state_reason=not_planned`; both preceded by the closing comment.
    Card `Reopen` → GH reopen (`state:open`, `state_reason=reopened`) + comment. A GH-side reopen
    of a synced-closed issue reopens the card (a **new IN arm** — today `UpdateExisting` refuses
    to move terminal cards, which is kept for everything except this cursor-proven transition).
    §8.
11. **Conflict resolution: no timestamp last-write-wins anywhere. Field authority is static per
    ref origin** (`ExternalIssueRef.Origin`: `ExternalImport` | `AntiphonExport`): title, body,
    free-form labels and priority are GH-authoritative on imports (today's clobber, unchanged)
    and Antiphon-authoritative on exports (IN skips those fields; OUT PATCHes them). State is
    transition-based with the `LastKnownExternalState` cursor; when both sides transitioned in
    the same window, the origin side's state wins and the losing transition is preserved loudly
    in the card history (Move/Reopen revision reason naming the conflict) plus a Warning log —
    never silent. §8, §9.
12. **OUT creates GitHub issues for unlinked cards — gated.** Only on boards whose tracker block
    sets `sync_out_create: true`, only for non-archived, non-terminal cards created **after**
    `Board.TrackerActivatedAt` (override: `export_since: <ISO date>` to widen deliberately) — so
    activating a 150-card legacy board can never mass-spam a repo. Initial issue: card title,
    body = description + backlink footer with the card marker, labels = free-form + managed.
    The ref is created `Origin = AntiphonExport`. §10.
13. **Trigger surface: an Antiphon endpoint + a thin script + a Windmill schedule — all three.**
    `POST /api/tracker-sync/run` (all bidirectional boards) and
    `POST /api/boards/{id}/tracker/sync`; `scripts/github-sync.ps1` calls it; a Windmill
    end-of-day schedule (desktop-tagged, the cleanup-job pattern) calls the script; a small
    "Sync now" button on linked boards calls the per-board endpoint. The full bidirectional pass
    NEVER runs from the orchestrator tick. §12.
14. **Cursor storage: columns on `ExternalIssueRef` and `Board`, one migration.** Ref:
    `Origin (int)`, `LastKnownExternalState (text?)`, `LastRevisionSynced (int)`,
    `LastOutboundSyncedAt (timestamptz?)`. Board: `TrackerActivatedAt (timestamptz?)`,
    `TrackerCommentsPulledAt (timestamptz?)` (the repo-wide `since` cursor — one per board,
    because the comments pull is one repo-wide call per board). Per-comment cursors live on
    `CardComment` itself (`ExternalCommentId` unique-filtered, `SyncedAt`). §11 (schema), §5.
15. **v1 scope: GitHub-only writes — agree with the investigation.** The operator's request is
    GitHub-specific; Jira/Linear read adapters are untouched; the capability interface (decision
    1) is the extension seam, so adding a provider later is implementing an interface, not a
    redesign. Anything Jira/Linear-write in this card is scope creep and out. §13.
16. **Relationship to the orchestrator poll: reads gated to a cadence, writes never on the
    tick.** `OrchestratorService.PollTickAsync` keeps calling the read-only
    `ExternalTrackerSyncService.SyncAsync` (`OrchestratorService.cs:77`) but behind a new
    `OrchestratorSettings.TrackerSyncIntervalMinutes` (default 30; `0` = every tick) so
    activation doesn't silently put GitHub on a 30-second poll. The new
    `TrackerBidirectionalSyncService` (comments IN + all writes + creates) runs only from the
    decision-13 trigger, and internally runs pull-then-push in one pass. §12.

---

## 1. Decision 3 — activation: TrackerKind becomes a derived index of the workflow YAML

**Change site:** `WorkflowDefinitionLoader` — both `UpdateAsync` (`WorkflowDefinitionLoader.cs:76`)
and the file-reload path (`ReloadContentAsync`, reached from `GetAsync:48` and
`ReloadFromFileAsync:103`), so an operator editing `WORKFLOW.md` on disk gets the same behavior as
one saving in Monaco. After a successful parse+save of a new version:

- Parse the front matter's `tracker.kind` with the existing alias logic. Today that logic is
  private to `IssueTrackerConfigParser.ParseKind` (`IssueTrackerConfigParser.cs:100`); extract it
  to an internal `IssueTrackerConfigParser.TryParseKind(string?, out TrackerKind)` so the loader
  and the parser share one alias table.
- `tracker.kind` present and valid → set `Board.TrackerKind` to it; on an Internal→external flip,
  stamp `Board.TrackerActivatedAt = utcNow` (only if currently null — reactivation after a
  temporary removal must not move the export watermark and re-spam creates, §10).
- `tracker:` block absent, or `kind` absent → `Board.TrackerKind = Internal`. Deliberate: the
  YAML is the config; deleting the block IS deactivation. Refs and comments stay in place, inert.
- `tracker.kind` present but unparseable → **ValidationException on save** (fail loud in the
  editor), via the same `PublishReloadedAsync(ok:false, …)` path malformed YAML already takes.
  A silent fall-back to Internal is exactly the invisible-dead-sync failure being fixed.

**Parser follow-through:** `IssueTrackerConfigParser.TryParse` keeps its guards unchanged
(`TrackerKind == Internal` → false at `:16`; YAML/board kind mismatch → error at `:57`). After
this slice the mismatch arm is unreachable in practice (the loader keeps them in lockstep) but it
stays as the tripwire for a hand-edited DB.

**Deliberately NOT built:** a `PATCH /api/boards/{id}` tracker field. Two writable sources of
truth for the same fact is how the parser's mismatch error becomes a live state; and a dedicated
TrackerConfig form (the never-built plan.md item) stays out of scope — Monaco is the editor.

`AgentService.cs:783` and `BoardService.cs:129` keep hardcoding Internal at create: a new board
has no workflow yet, and the first workflow save with a `tracker:` block activates it.

## 2. Decision 1 — the write interface

```csharp
public interface IBidirectionalIssueTracker : IIssueTracker
{
    Task<IReadOnlyList<TrackedIssueComment>> FetchCommentsSinceAsync(
        IssueTrackerConfig config, DateTime? since, CancellationToken ct); // repo-wide pull
    Task<TrackedIssueComment> PostCommentAsync(
        IssueTrackerConfig config, string externalId, string body, CancellationToken ct);
    Task AddLabelsAsync(IssueTrackerConfig config, string externalId,
        IReadOnlyList<string> labels, CancellationToken ct);      // POST /labels sub-resource
    Task RemoveLabelAsync(IssueTrackerConfig config, string externalId,
        string label, CancellationToken ct);                      // DELETE /labels/{name}
    Task ReplaceLabelsAsync(IssueTrackerConfig config, string externalId,
        IReadOnlyList<string> labels, CancellationToken ct);      // PATCH labels (export-origin only)
    Task SetStateAsync(IssueTrackerConfig config, string externalId,
        string state, string? stateReason, CancellationToken ct); // PATCH state + state_reason
    Task<TrackedIssue> CreateIssueAsync(IssueTrackerConfig config, string title, string body,
        IReadOnlyList<string> labels, CancellationToken ct);      // POST /issues
}

public sealed record TrackedIssueComment(
    string ExternalCommentId,   // GitHub comment id, stringified
    string IssueExternalId,     // "owner/repo#N" — derived from issue_url on the repo-wide pull
    string Author,              // user.login
    string Body,
    string Url,                 // html_url
    DateTime CreatedAt,
    DateTime UpdatedAt);
```

Implemented by `GitHubIssuesTracker` (which already owns `SendAsync`, repo/number parsing and the
`ExternalId` format `owner/repo#N`, `GitHubIssuesTracker.cs:70-163`). The repo-wide pull maps
`GET repos/{o}/{r}/issues/comments?since=&per_page=100&sort=created&direction=asc` and derives
`IssueExternalId` from each comment's `issue_url`; comments on PRs are dropped by checking the
issue is one we track (join happens in the sync service against `ExternalIssueRefs` — an
unmatched issue number is simply skipped, which also covers PR comments without an extra call).
Pagination: follow `per_page=100` pages until short page (both here and, pre-existing gap left
as-is this card, the issue list).

DI: no new registration — the sync service receives `IEnumerable<IIssueTracker>` as today
(`ExternalTrackerSyncService.cs:26`) and capability-tests. A board whose kind's tracker is not
bidirectional logs Debug and is skipped by the bidirectional pass; the read pass is unaffected.

## 3. Decision 2 — credentials

`IssueTrackerConfig` grows `TokenKeyName` (from `tracker.token_key` / `tracker.token_key_name`),
and — because the tracker is a singleton-ish HttpClient wrapper with no DbContext — token
resolution moves out of the tracker for the resolved path: the config record grows a
`ResolvedToken` (never serialized, never logged) that the sync services populate before calling
the tracker, via a small `TrackerTokenResolver`:

1. `TokenKeyName` set → look up the CARD-0106 `ApiKeys` row (project scope of the board's
   project first, then global — the exact `ApiKeyEnvResolver` search order,
   `ApiKeyEnvResolver.cs:176-195`), decrypt with `IApiKeyProtector`. Missing/undecryptable →
   the board's sync is skipped with a Warning naming the key name and scopes searched (the
   `ApiKeyEnvResolver` message shape), never a crash of the whole pass.
2. Else `ApiKeyEnv` set → `Environment.GetEnvironmentVariable` (today's read path, byte-for-byte
   back-compat: `GitHubIssuesTracker.ResolveToken:165` becomes the fallback inside the resolver).
3. Else → unauthenticated (reads may still work on public repos; any write returns 401/403 and
   the board sync logs Warning and skips writes).

Operator setup for this deployment: mint a fine-grained PAT (Issues RW on
`michal-ciechan/Antiphon`), store it under Settings → API Keys as `github-antiphon-sync`
(global scope), and put `token_key: github-antiphon-sync` in the board's tracker block. No env
plumbing, survives restarts, encrypted at rest, and the Scheduled-Task server identity needs
nothing installed.

Writes never shell out to `gh`. The Windmill/ops script needs no GitHub credential at all — it
calls Antiphon (§12).

## 4. Decision 4 & 7 — the `CardComment` entity

```csharp
public class CardComment
{
    public Guid Id { get; set; }
    public Guid CardId { get; set; }
    public string Body { get; set; } = string.Empty;
    /// <summary>Self-reported author (agent name, "operator") or the GitHub login on imports.
    /// Free text; the server has no principals (same stance as CardRevision.EditedBy).</summary>
    public string? Author { get; set; }
    public CardCommentOrigin Origin { get; set; }          // Antiphon = 0, External = 1
    /// <summary>Tracker comment id ("owner/repo#comment-id" not needed — the GitHub numeric id,
    /// stringified; unique-filtered where non-null). Non-null on imports at insert; stamped onto
    /// an Antiphon-origin row when its outbound echo is recognized by marker (§5).</summary>
    public string? ExternalCommentId { get; set; }
    public string? ExternalUrl { get; set; }
    /// <summary>Imports: GitHub created_at. Antiphon rows: server now.</summary>
    public DateTime CreatedAt { get; set; }
    /// <summary>Outbound claim stamp — set BEFORE the POST, cleared if it throws (CARD-0067
    /// shape). Null on External rows.</summary>
    public DateTime? SyncedAt { get; set; }
    public Card Card { get; set; } = null!;
}
```

Indexes: `(CardId, CreatedAt)`; unique filtered on `ExternalCommentId` where non-null (the IN
dedupe is a constraint, not a convention).

Why not a `CardRevision` kind: `RevisionNumber` is one guarded per-card sequence allocated from
`Card.RevisionCount` under the concurrency token (`CardRevisionLog.cs:107-118`) — a comment
import batch would contend with every card mutation for it, `RevisionCount` doubles as the UI's
"edited" affordance and would start counting chatter, and a ContentEdit row semantically stores
*superseded* values (`CardRevision.cs:10-13`) which a comment supersedes nothing of. The card
RECORD stays what CARD-0019 made it; the discussion thread is a separate surface.

API: `GET /api/cards/{id}/discussion` (list, ascending CreatedAt) and
`POST /api/cards/{id}/discussion` (`{ body, author }`) → creates `Origin = Antiphon`, publishes
`BoardChanged`. Route named `discussion` because `/api/cards/{id}/comments` is the
`CardReviewService` session-inject (`CardEndpoints.cs:145`) and must not change meaning under
existing callers. Client: a discussion section in the card modal — list + composer, plus a badge
count on the card face; the session-inject "comment to agent" UI is untouched and visually
distinct.

## 5. Decision 5 — loop prevention, and why it provably terminates

Every synced artifact has exactly one of these shapes:

- **Inbound comment** → row with `Origin = External`. The OUT query is
  `Origin == Antiphon && ExternalCommentId == null && SyncedAt == null` — an External row is
  structurally unselectable. No timestamp reasoning involved.
- **Outbound comment** → body posted to GH with a trailing hidden marker
  `\n\n<!-- antiphon:comment=<CardComment.Id:N> -->` (GitHub renders HTML comments invisibly;
  the marker survives quoting poorly, which is fine — a quoted marker inside someone's reply
  fails the *trailing* position check and the reply imports normally). Claim-before-produce:
  `SyncedAt` is stamped and saved BEFORE the POST and cleared if the POST throws — so a crash
  between POST and save leaves a claimed row that the next pass's marker-match (below) resolves,
  rather than a double post (CARD-0067's stamp-before-produce, same reasoning).
- **The echo of an outbound comment** arrives on the next IN pull carrying the marker. The
  importer parses the trailing marker, finds the originating row by Guid, and stamps its
  `ExternalCommentId`/`ExternalUrl` instead of inserting — the echo becomes the link. A marker
  whose Guid matches nothing (deleted row, foreign Antiphon install) imports as a normal
  External row — fail-open to visible, never to silent drop of a human's text.
- **Content-edit echo:** the IN clobber (`UpdateExisting`) writes NO ContentEdit revision (it
  mutates title/description directly, `ExternalTrackerSyncService.cs:217-312`), and the
  OUT edit-comment cursor is `ExternalIssueRef.LastRevisionSynced` over revisions with
  `Kind == ContentEdit && EditedBy != "external-tracker"` — belt (no revision exists) and braces
  (actor exclusion if one ever does).
- **State echo:** suppressed by `LastKnownExternalState` (§8) — a push updates the cursor in the
  same SaveChanges, and both IN arms compare against the cursor before acting.

Why marker + origin instead of actor-tag alone: the PAT may be (and on this deployment, is) the
operator's own GitHub account, so "comments authored by the sync's login" would wrongly swallow
the operator's genuine GH comments. The marker discriminates by artifact, not author.

Termination argument, pinned in §14: a GH-born comment produces one External row and is dedupe-
blocked forever after (unique index); an Antiphon-born comment produces one GH comment (claimed
before post) whose echo produces zero rows (marker match) and whose non-null `ExternalCommentId`
blocks any re-push. Every cycle strictly consumes its trigger; there is no path that creates a
new syncable artifact from a synced one.

## 6. Decision 6 — the OUT comment set, precisely

Per bidirectional board, per linked card, in one pass, in this order (comments before state so a
closing comment lands while the issue is open):

1. **Discussion comments:** unclaimed `Origin = Antiphon` rows, ascending CreatedAt, posted with
   the marker. Author is prefixed in the body (`**{Author}** via Antiphon:`) since the PAT posts
   as one GH account.
2. **Content-edit comments (import-origin refs only):** for revisions past
   `LastRevisionSynced` — one comment per edit: reason, editor, and the CURRENT card
   title/description (the new text — so an Antiphon edit that IN-reconverges to the GH body is
   preserved in the thread, not lost; stated plainly in the comment: "the issue body remains
   authoritative"). Cursor advances in the same save. Export-origin refs skip this — their edits
   PATCH the issue title/body instead (§9), no comment noise.
3. **Lifecycle comments:** terminal transition → closing comment (terminal reason, board/card
   link); reopen → reopen comment. Detected by state-cursor divergence (§8), not by revision
   scanning, so a card that went terminal while sync was off still closes out on the next run.

Not comments, ever: column moves (label territory), archive (see §13 — archive does NOT touch
GH), agent session chatter.

## 7. Decisions 8 & 9 — labels

**Managed prefixes, sync-owned:** `status:*` on every linked issue; `priority:*` on
export-origin issues (import-origin priority stays IN-derived from the existing `priority:` /
`p1..p5` parse, `GitHubIssuesTracker.cs:112-143`, untouched).

- Derivation: `status:` + kebab-case of `CardStatus` → `status:backlog`, `status:in-progress`,
  `status:review`, `status:done`, `status:blocked`, `status:canceled`. Priority:
  `priority:<int>` when `Priority != 0`.
- Application: compare the issue's current labels (from the ref's freshly-pulled payload) —
  remove stale managed labels (`DELETE /labels/{name}`), add the derived one
  (`POST /labels`) — sub-resource ops only, so a concurrent human label edit on GH is never
  clobbered by a full replace. Skipped entirely when already convergent (zero API calls on the
  steady state).
- Authority: Antiphon always wins for managed prefixes (GH has no columns; the label is a
  read-model). A human editing `status:*` on GH sees it rewritten next sync — documented in the
  workflow-block docs as owned.

**Free-form labels** follow the origin-authority rule (§9): import-origin → GH wins (today's
IN clobber via `SerializeLabels`, unchanged — except managed prefixes are now stripped before
`LabelsJson` is written, so `status:done` never becomes a card label); export-origin → card wins
(OUT computes the full set: free-form + managed, via `ReplaceLabelsAsync`; IN skips `LabelsJson`
for these refs). Missing GH labels are auto-created by the add call (GitHub creates unknown
labels on POST /labels with default color) — no pre-provisioning step.

## 8. Decision 10 — state machine, both directions

New cursor `ExternalIssueRef.LastKnownExternalState` ("open"/"closed"; null backfill = treat
first sync as "whatever the pull says", no transition fired).

**OUT (push, second phase of a run):**
- Card is terminal (`Done`/`Canceled` status, terminal column) AND cursor != "closed" → post
  closing comment, `SetStateAsync("closed", Done → "completed", Canceled → "not_planned")`,
  cursor := "closed".
- Card is non-terminal AND cursor == "closed" AND the card's most recent state change was an
  Antiphon-side reopen (a `Reopen` revision newer than `LastOutboundSyncedAt`) → reopen comment,
  `SetStateAsync("open", "reopened")`, cursor := "open".

**IN (pull, first phase):**
- Issue closed, card non-terminal → today's `ReconcileStaleIssuesAsync`/`MarkInactive` move to
  the terminal column (unchanged), now also cursor := "closed".
- Issue open, card terminal, cursor == "closed" → **genuine external reopen**: new arm that
  calls `CardRevisionLog.AppendReopen` (actor `external-tracker`, reason naming the tracker
  state) and moves the card to the first active column — the one transition `UpdateExisting`'s
  terminal guard (`:280`) deliberately blocks today, unlocked only by cursor proof that the
  close was previously observed/made by the sync. Cursor := "open".
- Issue open, card terminal, cursor == "open"/null → the card went terminal locally and OUT will
  close the issue later this same run (pull-then-push order) — no IN action.

**Both-transitioned conflict** (e.g. issue reopened on GH while the card was completed locally,
both since last run): pull-then-push means IN acts first. Resolution follows origin authority
(decision 11): import-origin → GH's state is applied to the card (the reopen arm fires; the
card's local completion is superseded, with the Reopen revision's reason explicitly naming the
conflict: "External tracker reopened; superseded local completion at <ts>") and OUT does not
re-close (cursor is now "open" and the card is non-terminal). Export-origin → IN's reopen arm is
skipped for state too; OUT closes the issue. In both cases the losing transition is written into
the card history and logged Warning — visible, deterministic, and convergent in one run. Never
does the sync close or reopen anything without either a cursor transition or a terminal-status
fact behind it.

Archive is not close: archiving a linked card stops it syncing (queries filter `ArchivedAt ==
null`) but does not touch the GH issue — deletion semantics on someone's issue tracker are not
Antiphon's call.

## 9. Decision 11 — the conflict table, field by field

| Field | Import-origin ref | Export-origin ref | Mechanism |
|---|---|---|---|
| Title | GH wins, always | Antiphon wins, always | IN: `UpdateExisting` unchanged / skipped per origin. OUT: `PATCH title` when card changed since `LastOutboundSyncedAt` (export only) |
| Description/body | GH wins | Antiphon wins | same; import-origin card edits surface as a comment (§6.2) |
| Free-form labels | GH wins | Antiphon wins | §7 |
| Managed `status:*`/`priority:*` labels | Antiphon wins | Antiphon wins | §7, sub-resource ops |
| Priority | GH wins (label parse) | Antiphon wins (`priority:` label out) | existing parse / §7 |
| State open/closed | transition-based; GH wins a true both-changed conflict | transition-based; Antiphon wins it | §8 cursors; loser preserved in history + Warning |
| Comments | append-only both ways — no conflict is possible | same | §5 |

"Changed since `LastOutboundSyncedAt`" is `Card.UpdatedAt > LastOutboundSyncedAt` — a coarse
dirty check for export pushes only, safe because the authority rule (not the timestamp) decides
direction; the timestamp only decides whether to bother. No field is ever resolved by comparing
two wall clocks against each other.

## 10. Decision 12 — issue creation for unlinked cards

Gate: `tracker.sync_out_create: true` (default absent/false — creation is the highest-blast-
radius write and must be an explicit opt-in per board). Selection: cards on the board with no
`ExternalIssueRef`, `ArchivedAt == null`, non-terminal status, and
`CreatedAt >= (tracker.export_since ?? Board.TrackerActivatedAt)`. The watermark is what makes
activation on a 152-card deployment safe: flipping a legacy board creates issues only for cards
born after the flip unless the operator deliberately sets `export_since` earlier.

Create: `POST /issues` with title, body = card description + `\n\n---\n<!-- antiphon:card=
<CardId:N> -->\n_Mirrored from Antiphon card <identifier> (<board link>)_`, labels = free-form +
managed. Then insert the ref (`Origin = AntiphonExport`, cursor "open", `LastOutboundSyncedAt =
now`) in the same SaveChanges batch as the pushes. Claim shape: the ref insert happens
immediately after the POST returns; a crash between POST and save leaves an orphan GH issue whose
body marker names the card — the next run detects it (the pull matches no ref; the create phase
re-checks by searching pulled issues for the card marker before POSTing) and links instead of
duplicating. The card-marker pre-check runs against the same issues list the pull already
fetched — zero extra API calls.

`ExternalId` collision safety: the ref uses the same `owner/repo#N` normalized id the read sync
uses, so the created issue is immediately owned by the normal pull path on subsequent runs.

## 11. Decision 14 — schema, one migration (`20260824…_AddBidirectionalTrackerSync`)

- `Boards`: `TrackerActivatedAt timestamptz NULL`, `TrackerCommentsPulledAt timestamptz NULL`.
- `ExternalIssueRefs`: `Origin int NOT NULL DEFAULT 0` (ExternalImport — correct for every
  pre-existing row; there are zero in this deployment anyway), `LastKnownExternalState text
  NULL`, `LastRevisionSynced int NOT NULL DEFAULT 0`, `LastOutboundSyncedAt timestamptz NULL`.
  Backfill note: `LastRevisionSynced = 0` on a legacy linked card would comment-echo its entire
  edit history on first sync — the migration sets it to the card's current `RevisionCount` for
  existing rows (vacuously none here, but the migration must be correct on any deployment).
- `CardComments`: new table per §4, FK to Cards cascade, `(CardId, CreatedAt)` index, unique
  filtered `ExternalCommentId`.
- The repo-wide comments cursor is per-board (one repo per board config) — `since` is set from
  `TrackerCommentsPulledAt` minus a 60 s overlap (GitHub `since` is inclusive-ish on update
  time; the unique index makes overlap free), advanced to the pull's start time on success.

Hand-written migration in `server/Migrations/` following the existing `yyyyMMddHHmmss_Name`
pattern.

## 12. Decisions 13 & 16 — triggers and the tick

**New service `TrackerBidirectionalSyncService`** (scoped, like `ExternalTrackerSyncService`,
composing it rather than duplicating it): one `RunAsync(boardId?, utcNow, ct)` =
(1) read-side issue upsert (delegates to `ExternalTrackerSyncService.SyncAsync` scoped to the
board(s)), (2) comments IN, (3) pushes OUT (comments → content-edit comments → labels →
state), (4) creates. Returns a summary DTO (per board: issues pulled, comments in/out, labels,
state changes, creates, skips + reasons) that the endpoint returns and the script prints.
Per-board failures log Warning and continue (the existing per-board catch shape,
`ExternalTrackerSyncService.cs:78`).

**Endpoints:** `POST /api/boards/{id:guid}/tracker/sync` and `POST /api/tracker-sync/run`
(all boards with `TrackerKind != Internal`). 409 when the board is Internal (`tracker block
missing or inactive`), 200 with the summary otherwise. A concurrent-run guard (per-board
`SemaphoreSlim` registry or a simple in-memory "running" set) makes a double-fire a fast 409,
not a doubled push.

**Script:** `scripts/github-sync.ps1 [-BoardId <guid>] [-BaseUrl http://localhost:17202]` —
calls the endpoint, prints the summary, exits non-zero on HTTP failure. ASCII-only (the CLAUDE.md
daemon-script rule).

**Windmill:** one schedule on server2, desktop-tagged, daily 18:00 local, SSH-bridging to
`pwsh -File C:\src\Antiphon\scripts\github-sync.ps1` — byte-for-byte the
`antiphon_build_junk_cleanup` pattern. Registered at build time via the windmill skill; the
schedule is the "end of day" the operator asked for, the endpoint/button are the "on demand".

**UI:** a "Sync tracker now" button on boards with `TrackerKind != Internal` (the DTO already
carries TrackerKind read-only) calling the per-board endpoint and toasting the summary.

**The tick (decision 16):** `OrchestratorService.PollTickAsync` keeps its read-only
`SyncAsync` call but gated: new `OrchestratorSettings.TrackerSyncIntervalMinutes` (default 30,
0 = every tick), tracked with a last-run stamp in the service. Reads stay reasonably fresh for
dispatch (`LoadEligibleCandidatesAsync` treats external-board cards as dispatchable,
`OrchestratorService.cs:385`) without a 30-second GitHub poll; the write half is structurally
absent from the tick — `OrchestratorService` never references the bidirectional service.

## 13. Decision 15 — scope, and the explicit out-list

GitHub-only writes. Out of scope for this card: Jira/Linear writes (interface seam exists;
nothing implemented, nothing registered); editing GH-side comments after posting (an edited
Antiphon `CardComment` is not re-pushed — v1 comments are immutable once synced; stated in the
discussion UI); syncing GH comment edits/deletions IN after import (updated_at drift is
ignored; the `since` pull may return an edited comment — the unique index drops it);
issue assignees/milestones; GH Projects; webhooks (the occasional-pull model is the operator's
explicit request — a webhook receiver is a different card if cadence ever matters); auto-
injecting GH comments into agent sessions; a TrackerConfig form UI; archive→GH semantics beyond
"stops syncing".

## 14. Verification / test design

All server tests in `Antiphon.Tests` (TUnit, Shouldly, category `Integration` where DB-backed;
shared-Postgres rules — every assertion scoped to rows the test made; GitHub HTTP via the
existing `StubHttpMessageHandler` recording `Requests`, `IssueTrackerAdapterTests.cs` pattern —
no real network, no `gh`).

- **`WorkflowTrackerActivationTests`** (S1): saving a workflow with `tracker.kind: github` flips
  the board to GitHubIssues and stamps `TrackerActivatedAt`; re-saving does not move the stamp;
  removing the block flips back to Internal and a later re-add does not move the stamp;
  unparseable kind → ValidationException and the board unchanged; the file-reload path flips
  identically; `IssueTrackerConfigParser.TryParse` succeeds end-to-end on the flipped board.
- **`GitHubIssuesTrackerWriteTests`** (S2): request-shape pins per method — comment POST body
  and path; repo-wide comments GET carries `since` + `sort=created&direction=asc` and derives
  `IssueExternalId` from `issue_url`; label add/remove hit the sub-resource; state PATCH carries
  `state` + `state_reason`; create POST shape; Bearer header present when a token resolves;
  pagination follows a full page. **`TrackerTokenResolverTests`**: `token_key` resolves project-
  then-global from the ApiKeys store; missing key → skip-with-warning not throw; `api_key_env`
  fallback byte-compatible with today.
- **`CardCommentApiTests`** (S3): POST/GET discussion round-trip; External rows are read-only via
  the API; the session-inject `/comments` route is untouched (existing tests keep passing —
  that is the pin).
- **`TrackerBidirectionalSyncTests`** (S4–S6) — the core suite, `FakeIssueTracker` extended to
  implement `IBidirectionalIssueTracker` with recording lists:
  - **Comments IN:** a GH comment lands as one External `CardComment` with GH author/timestamp;
    a second pull with overlapping `since` inserts nothing (unique index); the board cursor
    advances; a comment on an untracked issue number is skipped.
  - **THE LOOP-PREVENTION PIN (round trip, both directions, explicitly per the card):**
    (a) GH comment in → a full subsequent `RunAsync` → **zero** outbound `PostCommentAsync`
    calls recorded and zero new rows; (b) Antiphon comment out → the fake echoes it back
    (marker intact) on the next pull → **zero** new `CardComment` rows, the original row's
    `ExternalCommentId` stamped, and a third run performs zero writes. Steady-state pin: a
    fully-converged board produces zero write calls on `RunAsync`.
  - **Claim shape:** `PostCommentAsync` throwing leaves `SyncedAt` null (re-pushed next run);
    a crash simulated between POST and save (fake succeeded, save aborted) is healed by the
    marker match, not double-posted.
  - **Content-edit comments:** an operator ContentEdit on an import-origin card → one comment
    carrying the new text, cursor advances; `external-tracker` revisions and export-origin refs
    produce none.
  - **Labels:** status label rewritten via add/remove only on import-origin (full replace never
    called); managed prefixes stripped from `LabelsJson` on IN; export-origin full set replaced;
    convergent state makes zero label calls.
  - **State:** terminal Done → closing comment then `closed/completed`; Canceled →
    `not_planned`; GH reopen of a cursor-closed issue reopens the card via `AppendReopen`
    (actor `external-tracker`); GH-closed echo after our own close is a no-op.
  - **THE CONFLICT PIN (per §8's rule):** import-origin, both-transitioned (card completed
    locally, issue reopened externally, cursor "closed") → the card is reopened, the issue is
    NOT re-closed, and the Reopen revision's reason names the superseded local completion;
    export-origin mirror-image → the issue is closed and no card reopen occurs. Neither case is
    silent: the history row exists and is asserted on.
  - **Creates:** gated off by default; on, only cards after the watermark; orphan-issue
    (marker-in-body, no ref) is linked not duplicated; terminal/archived cards never created.
- **`OrchestratorTrackerCadenceTests`** (S7): with `TrackerSyncIntervalMinutes = 30`, two ticks
  inside the window call `SyncAsync` once; `0` preserves every-tick; the bidirectional service
  is never invoked from a tick (no registration reachable from `OrchestratorService` — asserted
  by construction/DI).
- **Live smoke (S7, build stage):** against `michal-ciechan/Antiphon` with the real PAT —
  read-in of #3–#11 to a scratch board; then a **throwaway issue created by the smoke itself**
  for the write path (comment out, label, close `not_planned`, reopen check, re-close), never
  touching #3–#11; results recorded on the card. This is a manual probe, not a CI test.

## 15. Build order

Seven slices, each independently shippable; nothing before S4 changes any live behavior on this
deployment (all boards Internal until an operator saves a tracker block).

1. **S1 — activation (decision 3):** loader-derived TrackerKind, `TrackerActivatedAt`, the
   parser's `TryParseKind` extraction, the migration (§11 — all columns land here, once), and
   the tick cadence gate (`TrackerSyncIntervalMinutes`) so the moment activation becomes
   possible the 30-second poll already isn't. `WorkflowTrackerActivationTests` +
   `OrchestratorTrackerCadenceTests`.
2. **S2 — write interface + credentials (decisions 1, 2):** `IBidirectionalIssueTracker`,
   `TrackedIssueComment`, the GitHub implementation, `TrackerTokenResolver` + `token_key`
   parsing. `GitHubIssuesTrackerWriteTests`, `TrackerTokenResolverTests`.
3. **S3 — `CardComment` (decisions 4, 7):** entity + discussion endpoints + minimal modal UI
   section. `CardCommentApiTests`, client vitest for the panel.
4. **S4 — comments IN (decisions 5-in, 7):** `TrackerBidirectionalSyncService` skeleton
   (pull-then-push frame, per-board error isolation, run guard) + the comments pull, marker
   recognition, board cursor. IN-side `TrackerBidirectionalSyncTests` incl. loop pin (a).
5. **S5 — pushes OUT (decisions 6, 8, 9, 10, 11):** comment push with claim, content-edit
   comments, managed labels, per-origin authority in `UpdateExisting`, state machine + reopen
   arm + cursors. Loop pin (b), steady-state pin, conflict pins, label and state cases.
6. **S6 — creates (decision 12):** the gated create phase + orphan-link pre-check. Create cases.
7. **S7 — trigger surface + live smoke + docs (decisions 13, 16):** endpoints, run guard 409,
   `scripts/github-sync.ps1`, Windmill schedule registration, board "Sync now" button, the live
   smoke on a throwaway issue, workflow-block reference docs (`tracker:` keys incl. `token_key`,
   `sync_out_create`, `export_since`), CLAUDE.md gotcha entry (activation is YAML-derived; the
   sync owns `status:*`; writes never run from the tick), close the card with measured results.
