# CARD-0327 — Tracker sync must not clobber a human importance; operator-raised issues default High; other authors are marked for review

**Date:** 2026-09-02 (Plan pass, task 1074f4a2 — design only; no production code changed, no tests run)
**Card:** CARD-0327 "GitHub issue sync should default higher importance for operator-raised issues, mark others for human review"
**Supersedes:** nothing. Refines CARD-0166 decision 11 (import-origin field authority) and lands on the
CARD-0039 importance model. Reuses the provenance shape of CARD-0309 (`ModelAvailabilityHold.Source`) and
CARD-0305 (`RoutingPin.Provenance`); it deliberately does not invent a third one.

**Sources (verified this pass):** the card and both addenda; `server/Application/Services/
{ExternalTrackerSyncService,TrackerBidirectionalSyncService,CardRanking,CardRevisionLog,CardService,
BoardService,ModelAvailability,RoutingPinService,IssueTrackerConfigParser,TrackerSyncMarkers,TrackerCache,
TrackerTokenResolver,AttentionService,CardTaskFileRenderer,OrchestratorService}.cs`;
`server/Infrastructure/IssueTrackers/GitHubIssuesTracker.cs`; `server/Application/Interfaces/IIssueTracker.cs`;
`server/Domain/Entities/{Card,CardRevision,ExternalIssueRef,RoutingPin,ModelAvailabilityHold}.cs`;
`server/Application/Dtos/{BoardDtos,AttentionDtos,RoutingPinDtos}.cs`; `server/Migrations/20260901200000_AddRoutingPins.cs`;
`tests/Antiphon.Tests/Application/{ExternalTrackerSyncLandingColumnTests,TrackerBidirectionalSyncTests}.cs`;
`client/src/features/board/{CardRow,BoardCard,CardModal,CardEditModal}.tsx`, `client/src/api/boards.ts`;
`docs/workflow-tracker-block.md`, `docs/agent-card-lifecycle.md`, `docs/antiphon-api.md`,
`docs/superpowers/plans/2026-09-02-card-0039-importance-urgency-axes-plan.md`; the live board at 17202
(CARD-0281/0294/0323/0324 and their `/revisions`), the Antiphon board workflow YAML, `server/logs/antiphon-20260902.log`,
and `gh issue list --state all --json author` on 2026-09-02.

---

## Verdict up front

**The clobber is real, it is the sync's own write path, and it leaves no revision — confirmed in code and in
the live timeline.** `ExternalTrackerSyncService.UpdateExisting` (lines 335–340) recomputes
`CardRanking.FromTrackerScale(issue.Priority)` on **every** sync pass for every import-origin card and assigns
`card.Importance` with a plain property set. It calls `CardRevisionLog.AppendContentEdit` nowhere; the only
revision the sync ever writes is a `Move`. The same silent overwrite applies to title, description and labels.
A GitHub issue with no `priority:*` label parses as priority 0, which maps to `Normal`, so a hand-rated `High`
is written back to `Normal` on the next tick, every tick, forever. The OUT half never mirrors an import-origin
importance to GitHub (`SyncLabelsAsync` computes `desiredPriority` only for export-origin), so no round trip
could have preserved it.

**The fix has four parts, in priority order.**

1. **Provenance column, sync guard, revision on overwrite (the root cause).** `Card.ImportanceProvenance`
   (`Auto | Human`), set to `Human` by any content edit that sets importance. The sync writes importance only
   while provenance is `Auto`, and every content field the sync overwrites (title, body, labels, importance)
   is preceded by one `ContentEdit` revision authored `external-tracker`. This is the CARD-0309 shape:
   the automatic writer refreshes what it may and leaves the human value standing.
2. **Author-aware tracker importance.** `TrackedIssue` carries the author login; a board declares its
   operators in the tracker block (`tracker.operator_logins`); an operator-authored issue with no explicit
   priority label imports as `High`. This is one pure function applied whenever the sync is allowed to write,
   so it is idempotent across ticks and never fights a human rating.
3. **"Needs human review" is derived, not stored.** An import-origin card from a non-operator author that no
   human has rated, still in Backlog, is `needsHumanReview`. It is exposed on the card DTO, shown as a chip
   beside the GitHub key, listed on `GET /api/attention`, and written into `docs/cards/` front matter.
   Rating the card is the act that clears it.
4. **Feasibility research is an on-demand delegate**, not a tick side effect: a documented
   `delegate.ps1` brief that posts findings to the card's discussion thread (which the bidirectional sync
   already mirrors to the GitHub issue as a comment) and *suggests* an importance without setting it.

---

## Root cause, precisely

### The write path

```csharp
// ExternalTrackerSyncService.UpdateExisting — runs for every issue on every SyncAsync
var importAuthoritative = externalRef.Origin != ExternalIssueOrigin.AntiphonExport;
if (importAuthoritative)
{
    … card.Title = title; … card.Description = description; … card.LabelsJson = labelsJson; …
    var importedImportance = CardRanking.FromTrackerScale(issue.Priority);
    if (card.Importance != importedImportance)
    {
        card.Importance = importedImportance;   // plain set; no CardRevisionLog call anywhere in this branch
        changed = true;
    }
}
…
if (changed) { card.UpdatedAt = utcNow; card.ConcurrencyToken = Guid.NewGuid(); }
```

- It is **unconditional on every pass**, not first-creation only: the same branch runs for an existing ref.
- It **bypasses `CardRevisionLog` entirely**. The service's two `AppendMove` calls are the only revisions it
  writes; content changes are invisible in history.
- `GitHubIssuesTracker.ParsePriority` returns **0 when no `priority:*`/`pN` label exists**, and
  `CardRanking.FromTrackerScale(0)` is `Normal`. So the tracker's "value" for an unlabelled issue is always
  `Normal`, and any other importance on the card is a difference to be "corrected".
- The read-only sync runs from `OrchestratorService.PollTickAsync` behind `ShouldRunTrackerSync`
  (`Orchestrator:TrackerSyncIntervalMinutes`, default 30, not overridden in `server/appsettings*.json`), and
  **immediately on the first tick after a restart** because `_controlState.LastTrackerSyncAt` is null.

### The live timeline (CARD-0324, guid `1dd1b309…`)

| UTC | Event | Evidence |
|---|---|---|
| 15:27:47 | Card created by import from GitHub #28 | `createdAt` |
| 15:39:26 | `card.ps1 edit -Importance High` | revision 1, `ContentEdit`, superseded `importance: Normal`, reason "GitHub issue #28 raised by Mike Ciechan (repo owner) - default High per operator instruction" |
| 16:08:05 | Server restarted | `antiphon-20260902.log:24315` "Application started" at 17:08:05 +01:00 |
| 16:08:34.173044 | First orchestrator tick's tracker sync saved | `updatedAt` on **all four** import cards (0281, 0294, 0323, 0324) is this identical microsecond — one `utcNow` per `SyncAsync`, one `SaveChanges` |
| now | `importance: Normal`, `revisionCount: 1` | no second revision exists; the reversion is unrecorded |

CARD-0323 has the same shape (revision 1 at 15:39, `Normal` now). Even without the restart, the 30-minute
cadence would have reverted both by 16:09.

### What else the same path does

- Title/description/labels on import-origin cards are overwritten the same silent way. That authority is a
  CARD-0166 decision (the issue body is authoritative on an import-origin link, and a human edit is mirrored
  to GitHub as a comment saying so), so this plan **keeps** it for text and labels — but adds the missing
  revision so the overwrite is at least visible.
- Export-origin (`AntiphonExport`) cards are untouched by this branch; they are Antiphon-authoritative and
  already push `priority:*` out.

### The four live cards

| Card | Issue | Author | GitHub priority label | Importance now | Status |
|---|---|---|---|---|---|
| CARD-0281 | #25 | michal-ciechan | none | Normal | Backlog |
| CARD-0294 | #27 | michal-ciechan | none | Normal | Review |
| CARD-0323 | #29 | michal-ciechan | none | Normal (hand-set High at 15:39, reverted 16:08) | Backlog |
| CARD-0324 | #28 | michal-ciechan | none | Normal (hand-set High at 15:39, reverted 16:08) | Backlog |

All 29 issues on the repository, open and closed, are authored by `michal-ciechan`; the authenticated
`gh` login is the same account.

---

## Decisions

### 1. Provenance is a column on `Card`, two values, the RoutingPin shape

```csharp
public enum CardImportanceProvenance { Auto = 0, Human = 1 }
public class Card { … public CardImportanceProvenance ImportanceProvenance { get; set; } … }
```

`Auto` means "a default or an automatic writer produced this value" (today the only automatic writer is the
tracker sync). `Human` means "an explicit API content edit or create set it". The server has no principals
(`CardRevision.EditedBy` is self-reported), so a delegate running `card.ps1 edit -Importance` is `Human`
here exactly as an agent's `PUT /api/routing-pins` with `provenance: Human` is — the word names the *kind
of act*, not an authenticated person.

**Why on `Card` and not on `ExternalIssueRef`.** The rule being protected is "an automatic writer must not
overwrite a human rating", and that is a property of the card's importance regardless of which automatic
writer exists. Putting it on the link would make `CardService.UpdateContentAsync` reach into the tracker
link to flip it, and would leave a future automatic rater (the triage delegate, or anything CARD-0039
rejected under "decay") with nothing to check on a non-synced card. On `Card` the edit path is one line
and knows nothing about trackers.

**Why not derive it from the revision log.** A `ContentEdit` row snapshots *all* superseded fields, so a
title-only edit also records the importance; telling "a human changed importance" from "a human changed
the title" needs a walk over consecutive snapshots. `RoutingPinService` says it outright: provenance is a
column and not a comment *because* overwrite protection has to be cheap and unambiguous.

### 2. Auto never overwrites Human; the sync skips rather than 409s

`ModelAvailability.UpsertAutoDetectedAsync` refreshes evidence and leaves a Manual `DisabledUntil` alone.
`RoutingPinService.UpsertAsync` throws 409 `routing_pin_human` when Auto would replace Human. The sync is a
background tick with no caller to receive a 409, so it takes the ModelAvailability form: when
`card.ImportanceProvenance == Human`, the importance branch is skipped and the rest of the update proceeds.
No log line per tick (it would fire every 30 minutes for every rated card); the skip is visible as the
absence of a revision, and the card DTO says `importanceProvenance: Human`.

A human write converts in place: `UpdateContentAsync` with `Importance` set assigns the value and sets
`Human`. A new optional `ImportanceProvenance` on `UpdateCardContentRequest` lets a caller hand the field
back (`"importanceProvenance": "Auto"`) — the analogue of `DELETE /api/routing-pins/{id}` — so "let the
tracker own it again" is an explicit act, never an accident.

### 3. Every sync content overwrite writes one `ContentEdit` revision

`UpdateExisting` computes the four content diffs first; if any differ it calls
`CardRevisionLog.AppendContentEdit(card, reason, TrackerActor, utcNow)` **before** the first assignment,
then applies them. Reason text names the source and the fields:
`External tracker #28 changed: description, labels.` (importance is listed only when it actually changed).
`AppendContentEdit` is synchronous and database-free by design, and the same service already appends
`Move` rows through the navigation on a card loaded without `Revisions`, so no new includes are needed.

The bidirectional OUT pass already excludes `EditedBy == "external-tracker"` when it mirrors content edits
as GitHub comments (`PushContentEditCommentsAsync`), so these rows never echo back to the issue. The
`LastRevisionSynced` cursor only advances on human rows; unchanged.

### 4. Tracker importance is one pure function, applied at create and on update while `Auto`

```csharp
// CardRanking (next to FromTrackerScale)
public static CardImportance FromTrackedIssue(int trackerScale, bool? authorIsOperator) =>
    trackerScale != 0 ? FromTrackerScale(trackerScale)
    : authorIsOperator == true ? CardImportance.High
    : CardImportance.Normal;
```

- An **explicit priority label wins** over the author rule: an operator who labels their own issue
  `priority:low` gets `Low`. `ParsePriority` returns 0 for "no label" and for an unrecognised value; both
  mean "the tracker did not say", which is when the author default applies.
- The operator default is `High`, not `Critical`, per the card and CARD-0039 §3 (`Critical` is reserved and
  never auto-assigned).
- The result is written with provenance **`Auto`**: it is a policy default, not a rating. So the operator can
  still raise it from GitHub with a label, and a human rating on the card still outranks both.

**Why not "at create time only", as the card literally says.** If the author rule ran only at create,
`UpdateExisting` would compute `FromTrackerScale(0) = Normal` on the next tick and — the card being `Auto` —
revert `High` to `Normal`. The only ways out are to stop the sync updating importance after creation at all
(losing the one channel by which a GitHub `priority:*` label reaches an import-origin card) or to make the
tracker-derived value author-aware everywhere. The second is one function and is idempotent: a second tick
over an unchanged issue computes the same value, changes nothing, writes no revision and does not touch
`UpdatedAt`. The card's intent — a default that never overrides a human — holds under decision 2.

### 5. Operator identity is declared per board: `tracker.operator_logins`

```yaml
tracker:
  kind: github
  repository: michal-ciechan/Antiphon
  token_key: github-antiphon-sync
  operator_logins: [michal-ciechan]      # list, or a comma-separated scalar
```

Parsed by `IssueTrackerConfigParser` with the existing `ParseStringList` (so both forms work), carried on
`IssueTrackerConfig.OperatorLogins` (`IReadOnlyList<string>`, default empty), compared to the issue author
case-insensitively after trimming a leading `@`. It also feeds `TrackerCache.ConfigKey` through
`OptionsKey` only if stored as a scalar; since it is a list, add it to `ConfigKey` explicitly so two boards
differing only in operators do not share a cache entry (they cannot today, but the key should be honest).

**When unset (every board today):** `AuthorIsOperator` is null, the author rule never fires, nothing is
marked for review. Behaviour is exactly today's minus the clobber. The rollout slice sets it on the Antiphon
board.

**Rejected alternatives.** *Hard-coding `michal-ciechan`* — the card asked not to. *The token's owner via
`GET /user`* — implicit, GitHub-only, a network call per pass, and wrong the day the sync token belongs to
a machine user or a fine-grained PAT scoped to a different account. *The git `user.name`* — not a tracker
login. There is no existing operator-identity concept on `Project`, `Board`, `ApiKey` or the tracker config
to reuse; the honest answer is one declared line of YAML, which also works for Jira/Linear.

### 6. The author is captured on the issue and stored on the link

- `TrackedIssue` gains `string? Author = null` as a trailing optional parameter (no construction site
  breaks). `GitHubIssuesTracker.ParseIssue` reads `user.login` (the same field `TryParseComment` already
  reads for comments). Jira (`fields.reporter.name`) and Linear (`creator.name`) are one-liners but are
  **not** in this card's slices; they stay null and therefore never trigger either rule.
- `ExternalIssueRef` gains `Author` (`varchar(200)`, null) and `AuthorIsOperator` (`bool?`). Both are
  refreshed on every sync pass, so changing `operator_logins` propagates within one tick without a
  migration. `AuthorIsOperator` is stored rather than recomputed at read time so that `BoardService`,
  `AttentionService` and `CardTaskFileRenderer` never parse workflow YAML.
- The migration backfills `Author` for existing GitHub refs from the jsonb payload the sync already keeps:
  `UPDATE "ExternalIssueRefs" SET "Author" = "RawPayloadJson"->'user'->>'login' WHERE "TrackerKind" = 2 AND "Author" IS NULL;`
  `AuthorIsOperator` is left null; the first tick after deploy judges it.

### 7. "Needs human review" is derived at read time, and rating clears it

```
needsHumanReview =
    ExternalIssueRef.Origin == ExternalImport
 && ExternalIssueRef.AuthorIsOperator == false          // judged, and not an operator
 && Card.ImportanceProvenance == Auto                   // no human has rated it
 && Card.Status == Backlog && Card.ArchivedAt == null   // still waiting
```

Every input is a durable column, so this is a projection with nothing stored — the
`BuildCardNeedsDecisionAsync` precedent. It is cleared by any of: a human rating the card (the review *is*
the rating; `Normal` counts, because an explicit `Normal` is `Human`), moving it out of Backlog, or
archiving it. No new column, no new state machine, no ack.

**Surfaces (all four ship together so the marker is "visibly distinguishable" wherever a card is read):**

| Surface | Change |
|---|---|
| `CardDto` | `importanceProvenance` (`"Auto" \| "Human"`) |
| `ExternalIssueDto` | `author`, `authorIsOperator` (`true \| false \| null`), `needsHumanReview` |
| Board rows (`CardRow`, `BoardCard`, `CardModal`) | a `review` chip next to the existing GitHub key tag when `needsHumanReview`; `CardModal` also prints `raised by <author>` and `rated by human`/`auto` under importance |
| `GET /api/attention` | new `AttentionKind.ImportedIssueNeedsReview = 27` (appended, never renumbered), `Warning`, one row per card, `Actions: [OpenCard]`, `Title` = `CARD-nnnn — title`, `Headline` = `Raised on GitHub by <author> (not an operator) — nobody has rated it.`, `Evidence` = the issue key/URL, the first line of the body, and the ready-to-paste triage command from decision 8, `SinceUtc` = `card.CreatedAt` |
| `docs/cards/` front matter (`CardTaskFileRenderer`) | `importance_provenance:`, `external_author:`, `needs_human_review:` lines; index bit `` `review` `` |
| `card.ps1 get` | `High/Normal rank 7 (human-rated)` or `(auto)`; ` [needs human review: raised by x]` after the external key |

**Rejected.** *A card label* — import-origin labels are rewritten from GitHub every tick, so the label
survives only if it also lives on GitHub as a sync-managed `triage:*` prefix; that is a bidirectional
label-ownership extension (3-hourly OUT, IN strip rules, clear-on-review) for the same visibility the
derived flag gives for free. Left open in case the family should see the marker on GitHub itself. *A new
column* — AGENTS.md: a decision belongs on the attention feed, never a new column.

### 8. Feasibility research is an on-demand delegate with a documented brief

No automation. The hook is `scripts/delegate.ps1` with a brief the tracker doc owns:

```
pwsh -File scripts/delegate.ps1 -Card CARD-0325 -Role Debug -Goal @'
Triage GitHub issue #30 (raised by <author>, not an operator). Answer, in under 300 words:
(1) is it real — reproduce or cite the code path; (2) is it feasible — name the owner file(s)
and rough size (S/M/L); (3) suggested importance (Low|Normal|High) and why. Post the answer
with POST /api/cards/CARD-0325/discussion (author "triage-delegate"). Do NOT edit the card's
importance — the rating is the human's act and is what clears the review marker.
'@
```

- The delegate's finding lands as a `CardComment` (`Origin = Antiphon`), which the next bidirectional run
  mirrors onto the GitHub issue as a comment — so the external author sees the triage without anyone
  copying text.
- `Debug` is the right role for "is this real"; `Plan` for a feature-shaped issue. The attention row's
  Evidence prints the command with the card and author filled in, so the operator (or an orchestrator
  reading the feed) runs it in one paste.
- **Left open, deliberately:** an automatic pass (`tracker.auto_triage: true` dispatching the brief on
  create, or a CARD-0057 schedule sweeping `needsHumanReview` cards). The card says "if needed"; a tick
  that spends tokens on every external issue is a spend policy the operator has not asked for. The
  derived flag and the attention row are the seam it would plug into.

### 9. Once a human rates an import-origin card, GitHub shows it

`SyncLabelsAsync` currently computes `desiredPriority` only for export-origin links, and the import-origin
branch removes stale managed labels but never *adds* a priority label. Change: for import-origin links with
`ImportanceProvenance == Human`, `desiredPriority = TrackerSyncMarkers.PriorityLabel(card.Importance)`, and
add it via `AddLabelsAsync` when missing (mirroring the existing `status:*` add). While provenance is
`Auto` the import-origin issue keeps its own labels and the card follows them, as today. The doc line
"`priority:*` (export-origin only)" becomes "`priority:*` (export-origin, and import-origin once a human
has rated the card)". This keeps the card and the issue from disagreeing forever after a hand rating, at
the cost of ~10 lines; it is a separate slice so it can be dropped if the operator prefers GitHub blind.

### 10. Create and edit contracts

- `CreateCardRequest.Importance` becomes `CardImportance?` (null/omitted → `Normal`, provenance `Auto`;
  explicit → provenance `Human`). The wire default is unchanged for callers; only the provenance differs.
  `CardModal`'s create form sends its select value today (defaults `Normal`) — it should send `null` until
  the user touches the select, one line.
- `UpdateCardContentRequest.Importance` set → `Human`; `CardEditModal` already sends `null` when the select
  is unchanged (line 79), so a title-only edit does not flip provenance. New optional
  `ImportanceProvenance` (decision 2) to hand it back to `Auto`.
- `card.ps1 edit … -ImportanceProvenance Auto|Human`; `new -Importance` unchanged.
- `CardRevision` does not snapshot provenance: the only transition is `Auto → Human` on an edit that the
  revision already records (the superseded importance), and `Human → Auto` is an explicit request whose
  reason is on that revision.

---

## Data model after this card

```
Cards
  ImportanceProvenance   integer  not null  default 0 (Auto)
ExternalIssueRefs
  Author                 character varying(200)  null    -- tracker login of the issue author
  AuthorIsOperator       boolean                 null    -- null = not judged (no operator_logins on the board)
```

Migration `20260902HHMMSS_AddCardImportanceProvenanceAndIssueAuthor`, hand-written like
`AddRoutingPins` (daemons hold `bin/`), with the `[DbContext]`/`[Migration]` attributes in the file and the
snapshot updated to match; `Sql(...)` for the `Author` backfill from `RawPayloadJson` (decision 6).
**No provenance backfill:** every import-origin card's current importance was written by the sync (proven
above), so `Auto` is the truth there; on non-import cards nothing automatic writes importance, so `Auto` is
harmless and a hand re-rate flips it. `IssueTrackerConfig` gains `OperatorLogins` (optional trailing
parameter). `TrackedIssue` gains `Author` (optional trailing parameter).

---

## Slices

Sequential, Shared workspace (S1–S3 all touch `ExternalTrackerSyncService`/`BoardDtos`). Build to an
alternate output path (`--property:OutputPath=bin-<name>/`, forward slash) while the daemons run.

### S1 — The clobber: provenance, guard, revision (server)

- `CardImportanceProvenance` enum; `Card.ImportanceProvenance`; `AppDbContext` mapping; migration (column
  only — the ref columns come in S2, or fold both into one migration if S1 and S2 ship together).
- `ExternalTrackerSyncService.UpdateExisting`: compute the four diffs; skip importance when `Human`;
  `AppendContentEdit(card, reason, TrackerActor, utcNow)` before the first mutation when any diff remains.
- `CardService.UpdateContentAsync`: importance set → `Human`; honour `request.ImportanceProvenance`.
  `CreateAsync`: nullable importance per decision 10.
- DTOs: `CardDto.ImportanceProvenance`; `UpdateCardContentRequest.ImportanceProvenance`;
  `CreateCardRequest.Importance` nullable. `card.ps1 get`/`edit` per decision 10 (ASCII only).
- Tests — new `ExternalTrackerSyncImportanceProvenanceTests` on the `ExternalTrackerSyncLandingColumnTests`
  harness (`FakeIssueTracker`, `SeedTrackedBoardAsync`, `NewSut`): import lands `Normal/Auto`; a card set
  `High/Human` survives a sync whose issue has priority 0, with no new revision; a card at `Auto` follows a
  `priority:critical` label and gets one `ContentEdit` revision authored `external-tracker` whose superseded
  importance is `Normal`; a GitHub title change on an `Auto` card writes one revision holding the old
  title; an unchanged issue on a second pass writes nothing (`RevisionCount`, `UpdatedAt` equal);
  export-origin refs are untouched. `CardCorrectionIntegrationTests`: edit with importance → `Human`;
  title-only edit leaves provenance; `importanceProvenance: Auto` hands it back; create with and without
  importance. `TrackerBidirectionalSyncTests`: an `external-tracker` `ContentEdit` row is not echoed as a
  GitHub comment (the existing filter — add the assertion).
- Estimate: 2–3 h.

### S2 — Author, operator rule, review flag (server)

- `TrackedIssue.Author`; `GitHubIssuesTracker.ParseIssue` reads `user.login`.
- `IssueTrackerConfig.OperatorLogins`; parser (`operator_logins`, list or scalar); `TrackerCache.ConfigKey`.
- `ExternalIssueRef.Author`/`AuthorIsOperator` + mapping + migration (with the jsonb backfill).
- `CardRanking.FromTrackedIssue`; both call sites in `ExternalTrackerSyncService` (create and update)
  compute it and refresh `Author`/`AuthorIsOperator` on the ref each pass.
- `ExternalIssueDto` fields; `BoardService.ToDetailDto` derivation (decision 7); `CardTaskFileRenderer`
  front matter and index bit; `card.ps1 get` suffix.
- Tests: `IssueTrackerAdapterTests` (author parsed; missing `user` → null); parser tests for both YAML forms
  and for absence; sync tests: operator author + no label → `High/Auto`, second pass idempotent; operator +
  `priority:low` → `Low`; non-operator → `Normal`, `AuthorIsOperator=false`; `operator_logins` unset →
  `AuthorIsOperator=null` and no `High`; `CardTaskFileRendererTests` front matter; a `BoardService` test
  for each clause of `needsHumanReview` (rated → false; moved to In Progress → false; archived → false).
- Estimate: 2–3 h.

### S3 — Attention row, GitHub priority mirror, client chip

- `AttentionKind.ImportedIssueNeedsReview = 27`; `BuildImportedIssueNeedsReviewAsync` in `AttentionService`
  (query `ExternalIssueRefs` joined to `Cards` on the decision-7 predicate; `Include(r => r.Card)`), wired
  after `BuildCardNeedsDecisionAsync`; `AttentionSummaryDto` unchanged (it is a Warning, not a decision).
- `TrackerBidirectionalSyncService.SyncLabelsAsync` per decision 9.
- Client: `attention.ts` kind + `attentionVisuals.ts` label/colour; `boards.ts` DTO fields; the `review`
  chip in `CardRow`/`BoardCard`/`CardModal`; `CardModal` create form sends `null` importance until touched.
- Tests: `AttentionServiceTests` (row present for a non-operator unrated Backlog import; absent once rated;
  absent for an operator author); `TrackerBidirectionalSyncTests` (import-origin `Human/High` → `AddLabels
  priority:high`; `Auto` → no priority write); Vitest for `attentionVisuals`, `CardRow`, `CardModal`.
- Estimate: 2–3 h.

### S4 — Docs, config, rollout

- `docs/workflow-tracker-block.md`: `operator_logins` row in the keys table; a "Field authority for
  importance" paragraph (Auto/Human, the guard, the revision); the decision-8 triage brief; the
  `priority:*` ownership line per decision 9.
- `docs/agent-card-lifecycle.md` "Importance and urgency": one bullet — importance carries a provenance;
  the tracker sync writes it only while `Auto`; any explicit edit makes it `Human`; the review marker and
  what clears it.
- `docs/antiphon-api.md`: the new DTO fields, `importanceProvenance` on the content PATCH, the attention
  kind; `docs/ops-http.md` if it lists attention kinds.
- Rollout: `dev-backup.ps1`; add `operator_logins: [michal-ciechan]` to the Antiphon board's workflow front
  matter (the Monaco editor or `PUT /api/boards/8988ca03-…/workflow`); restart via
  `scripts/restart-apphost.ps1`; the first tick backfills `AuthorIsOperator` and — because all four import
  cards are `Auto` with no label — raises CARD-0281/0294/0323/0324 to `High/Auto` **with a revision each**.
  Then hand-rate the two the operator already rated (`card.ps1 edit CARD-0323 -Importance High -Reason …`,
  same for 0324) so they read `Human`; force a sync (`POST /api/boards/{id}/tracker/sync`) and confirm the
  value holds and no further revision appears. Rebuild the client bundle; browser-check the Backlog column
  chip through the browser-harness lane. Close with the verdict.
- Estimate: 1–2 h.

Total: roughly 8–11 h of agent time across four dispatches. S1 alone stops the clobber and is worth
shipping first if capacity is short.

---

## Test matrix

| Slice | Server (TUnit, `dotnet run --project tests/Antiphon.Tests`, chunk by namespace) | Client (Vitest, `scripts/test-client.ps1`) |
|---|---|---|
| S1 | `ExternalTrackerSyncImportanceProvenanceTests` (new); `CardCorrectionIntegrationTests`; `CardCorrectionApiTests`; `TrackerBidirectionalSyncTests`; `ContractSnapshotTests` if it pins `CardDto` | — |
| S2 | `IssueTrackerAdapterTests`; parser tests; the S1 sync file; `CardTaskFileRendererTests`; `BoardServiceIntegrationTests` | — |
| S3 | `AttentionServiceTests`; `TrackerBidirectionalSyncTests`; `TrackerSyncEndpointTests` | `attentionVisuals`, `CardRow`, `BoardCard`, `CardModal` |
| S4 | — | browser check on the built bundle |

Run the full `Antiphon.Tests` once after S2 (chunked), then only the touched namespaces.

---

## What this card does not do

- **Change CARD-0166 decision 11 for text.** The issue body stays authoritative on an import-origin link; a
  human description edit is still mirrored to GitHub as a comment and then overwritten locally. It now
  leaves a revision. Making text human-authoritative after an edit is a different card.
- **Jira/Linear authors.** The field exists; the adapters keep returning null.
- **Automatic triage.** Decision 8 names the seam; nothing dispatches on its own.
- **A GitHub-side "needs review" label.** Decision 7's rejected alternative; left open.
- **Provenance for urgency or due date.** Nothing automatic writes them; add the column the day something
  does, on the same shape.

## Left open, deliberately

- Whether `AttentionSummaryDto` should count review rows separately from decisions (it counts
  `CardNeedsDecision` today). Ship as a Warning and see whether the operator wants it in the badge.
- The CARD-0173 watermark digest (Backlog card f17f4d17 "notify Family when a GitHub issue becomes a card")
  should read `author` and `needsHumanReview` from the ref when it lands; the columns are there for it.
