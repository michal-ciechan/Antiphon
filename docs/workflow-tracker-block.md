# Workflow `tracker:` block (CARD-0166)

Board external-tracker config lives in the active workflow definition's YAML front matter.
Saving the workflow via `PUT /api/boards/{id}/workflow` (Monaco `WorkflowEditor`) derives
`Board.TrackerKind` from `tracker.kind` — there is no separate activation API.

Parsed by `IssueTrackerConfigParser.TryParse` / `TryResolveBoardTrackerKind`.

## Keys

| Key | Required | Notes |
|---|---|---|
| `kind` | yes (to activate) | `github` / `github_issues` / `githubissues` → `GitHubIssues`; also `linear`, `jira`, `internal`. Absent block or `kind: internal` → board stays/returns to `Internal` and sync stops. |
| `repository` | GitHub | `owner/repo`. Alternatives: `owner` + `repo`. |
| `token_key` | recommended | Name of a CARD-0106 `ApiKeys` row (project scope then global). Alias: `token_key_name`. |
| `api_key_env` | fallback | Env-var name holding a PAT. Alias: `token_env`. Used when `token_key` is absent. |
| `base_url` / `endpoint` | no | Default `https://api.github.com` for GitHub. |
| `active_states` | no | Default `["open"]` for GitHub. List or comma-separated scalar. |
| `sync_out_create` | no | `true` enables Antiphon→GitHub issue creates for cards after the export watermark. Default off. |
| `export_since` | no | ISO-8601 watermark for creates; defaults to `Board.TrackerActivatedAt`. |
| `notify_channel` | no | CARD-0171. Catalog channel that receives a plain-text change summary when a sync is triggered with `notify=true`. A channel GUID (recommended - titles are editable) or an exact, case-insensitive `Title` that is unique in the catalog. Unset = notify is a no-op for this board. |
| `import_column` | no | CARD-0170. Where newly imported (and GitHub-reopened) issues land. `backlog` (default): the board's `CardStatus.Backlog` column, matching manual creates; the tracker then moves a card only across the terminal boundary. `active`: first `IsActive && !IsTerminal` column, and the tracker owns the non-terminal column (the original E10 behaviour). Any other value is a validation error on workflow save. |
| `project` / `project_key` | Linear/Jira | Project key. |
| `jql` | Jira | Extra JQL filter. |
| `operator_logins` | no | CARD-0327. List, or a comma-separated scalar, of tracker logins treated as operators for this board. Compared to the issue author case-insensitively after trimming a leading `@`. Unset (every board before this card) means `AuthorIsOperator` stays null and the operator-default-importance and needs-human-review rules never fire — behaviour is exactly pre-CARD-0327. |

Any other scalar key under `tracker:` is retained in `IssueTrackerConfig.Options` (that is how
`sync_out_create`, `export_since` and `import_column` are read).

## Landing column

New open issues land where manual creates land: the board's `CardStatus.Backlog` column
(fallback: first non-active non-terminal, then first column). The tracker then moves a card only
across the terminal boundary — closed → Done, cursor-proven GitHub reopen → landing column. It
does **not** drag an unowned card back to In Progress on every tick.

`tracker.import_column: active` opts a board back into the original E10 behaviour: first
`IsActive && !IsTerminal` column, tracker state owns the non-terminal column, Linear blocked →
waiting column. That is legitimate when the tracker really is the team's queue; on the default
column shape it means every open issue is eligible for auto-dispatch.

## Example (GitHub)

```yaml
---
tracker:
  kind: github
  repository: michal-ciechan/Antiphon
  token_key: github-antiphon-sync
  active_states: [open]
  sync_out_create: false
  notify_channel: caee9d25-b751-4401-a295-3b7e242842aa
---

Work on {{ issue.identifier }}.
```

## Triggers

- **Read-only tick:** `OrchestratorService.PollTickAsync` calls `ExternalTrackerSyncService.SyncAsync`
  gated by `Orchestrator:TrackerSyncIntervalMinutes` (default 30; `0` = every tick). Writes are
  structurally absent from the tick.
- **Bidirectional (comments / labels / state / creates):** only from
  `POST /api/boards/{id}/tracker/sync`, `POST /api/tracker-sync/run`,
  `scripts/github-sync.ps1`, the board "Sync tracker now" button, or the Windmill
  `u/lndcobra/antiphon_github_sync` schedule (every 3 hours, `0 0 */3 * * *` Europe/London).
- **Change notification (CARD-0171):** opt-in **per trigger**, via `?notify=true` on either
  endpoint (`scripts/github-sync.ps1 -Notify` forwards it; the Windmill schedule passes it). The
  board button deliberately does not - a click must not ping a family chat. The target is
  **per board**: `tracker.notify_channel`. Both are required; the flag alone announces nothing,
  because the per-board config is the consent. Boards that changed are grouped by resolved
  channel, so one channel gets one message per run. A board that changed but could not be
  announced reports why in the response's `notifications[]` (`notify_channel_unset`,
  `channel_not_found`, `channel_ambiguous`, `channel_disabled`, `send_failed`) - never an
  exception, never a failed sync, because the writes have already committed. A per-board
  `error` is not a change and is never announced; `github-sync.ps1` exits 1 for it instead.

## Field authority for importance (CARD-0327)

`Card.ImportanceProvenance` (`Auto | Human`) says who last set `importance`. `Auto` means a
default or an automatic writer produced the value — today that is only the tracker sync, applying
`CardRanking.FromTrackedIssue`: an explicit `priority:*` label always wins; absent one, an
operator-authored import-origin issue defaults `High`, everything else defaults `Normal`. `Human`
means an explicit content edit (`PATCH /api/cards/{id}/content` with `importance` set, or
`card.ps1 edit -Importance`) set it. The guard is one rule: the sync skips the importance branch
entirely whenever `ImportanceProvenance == Human`, so a hand-rated card is never reverted by the
next tick, and no 409 is raised — a background tick has no caller to receive one. Any other content
field the sync overwrites (title, description, labels) is preceded by one `ContentEdit` revision
authored `external-tracker` so the overwrite is visible in history, even while importance itself is
left alone. `ImportanceProvenance` can be handed back to `Auto` explicitly (an optional
`importanceProvenance` field on the content PATCH, or `card.ps1 edit -ImportanceProvenance Auto`) —
"let the tracker own it again" is an explicit act, never an accident.

An import-origin card with `AuthorIsOperator == false` (a non-operator raised it) that is still
`Auto`, still in `Backlog`, and not archived is `needsHumanReview` — derived at read time, nothing
stored. It clears the moment a human rates the card (an explicit `Normal` counts — the review *is*
the rating), moves it out of Backlog, or archives it. It shows as a `review` chip beside the GitHub
key on the board and in `card.ps1 get`, as `needs_human_review:` in `docs/cards/` front matter, and
as an `ImportedIssueNeedsReview` row on `GET /api/attention` (see
[antiphon-api.md](antiphon-api.md)).

**Triage brief (decision 8).** There is no automatic dispatch on a `needsHumanReview` card — the
card says "if needed", and a tick that spends tokens on every external issue is a spend policy
nobody asked for. The seam is a documented, on-demand `delegate.ps1` brief, run by the operator (or
an orchestrator reading the attention feed) via the ready-to-paste command in that row's evidence:

```
pwsh -File scripts/delegate.ps1 -Card CARD-nnnn -Role Debug -Goal @'
Triage GitHub issue #N (raised by <author>, not an operator). Answer, in under 300 words:
(1) is it real - reproduce or cite the code path; (2) is it feasible - name the owner file(s)
and rough size (S/M/L); (3) suggested importance (Low|Normal|High) and why. Post the answer
with POST /api/cards/CARD-nnnn/discussion (author "triage-delegate"). Do NOT edit the card's
importance - the rating is the human's act and is what clears the review marker.
'@
```

Use `Role Debug` for "is this real"; `Role Plan` for a feature-shaped issue. The finding lands as a
`CardComment`, which the next bidirectional sync mirrors onto the GitHub issue as a comment, so the
external author sees the triage without anyone copying text. The delegate never sets importance —
only a human rating (or an explicit `importanceProvenance: Human` edit) clears the review marker.

## Ownership / out of scope (v1)

- Sync owns managed labels `status:*` (always) and `priority:*` (export-origin, and import-origin
  once a human has rated the card — CARD-0327 decision 9: while an import-origin card's importance
  is still `Auto`, the issue keeps its own labels and the card follows them, as before; once a human
  rates it, the sync adds the matching `priority:*` label if missing so GitHub and the card agree).
  The priority label carries the importance *name*: `priority:critical`, `priority:high`,
  `priority:low`. `Normal` importance exports no priority label. A human
  edit of those on GitHub is rewritten on the next sync. **Known noise mode (CARD-0171):** a human
  who re-adds a managed label every cycle produces `labelsChanged=1` on every run, so a
  notify-enabled board would say "labels updated on 1 issue" every 3 hours forever. Mitigation is
  documented, not built: if it happens, drop label-only runs from the notification gate behind a
  `Tracker:NotifyOnLabelOnlyChanges=false` setting (one line in the gate).
- GitHub->card creates are **not** in the change signal: the read-only tick creates the card
  first, so a 3-hourly run's own counters cannot see it. That needs a watermark digest over
  persisted rows - tracked separately as CARD-0173.
- GitHub-only writes. Jira/Linear stay read-only.
- Comment edits/deletions after posting are out of scope (Antiphon→GH and GH→Antiphon).
- Assignees, milestones, GH Projects, webhooks, auto-injecting GH comments into agent sessions,
  TrackerConfig form UI, and archive→GH-close semantics are out of scope.


<!-- CARD-0254 preserved source begins -->

## CARD-0254 preserved operational detail

### Preserved Gotcha #69

- **GitHub Issues bidirectional sync is YAML-activated and write-triggered, never tick-written** (CARD-0166): activation is derived from the workflow `tracker:` block on save (`kind` / `repository` / `token_key` / `sync_out_create` / `export_since` — see `docs/workflow-tracker-block.md`); there is no board PATCH for TrackerKind. The sync owns managed labels `status:*` and (export-origin) `priority:*` — a human edit of those on GitHub is rewritten next sync. Reads stay on the orchestrator tick behind `Orchestrator:TrackerSyncIntervalMinutes` (default 30); **writes never run from the tick** (`OrchestratorService` must not reference `TrackerBidirectionalSyncService`). Bidirectional pushes only from `POST /api/boards/{id}/tracker/sync`, `POST /api/tracker-sync/run`, `scripts/github-sync.ps1`, the board "Sync tracker now" button, or Windmill `u/lndcobra/antiphon_github_sync`. v1 is GitHub-only; comment edits/deletions after posting, assignees/milestones, GH Projects, webhooks, auto-injecting GH comments into agent sessions, a TrackerConfig form UI, and archive→GH-close are explicitly out of scope.

### Preserved Gotcha #70

- **`#N` is `CARD-000N` and nothing else** (CARD-0175 / CARD-0170): every card's `Identifier` is `CARD-nnnn`; a GitHub/Jira/Linear key lives only on `ExternalIssueRef.ExternalKey`. `GET /api/cards/%235` is CARD-0005, never GitHub issue #5. The tracker never picks a non-terminal column unless the board sets `tracker.import_column: active` (default is Backlog, the same rule as a manual create). `WorktreeManager.ValidateCardId` is the last line of defence, not the first — `#3` stays rejected; do not widen it.

### Preserved Gotcha #71

- **`CARD-nnnn` is unique per BOARD, and every card route resolves it through the same scope walk `delegate.ps1 -Card` uses** (CARD-0218): explicit `?boardId=` (`card.ps1 -Board <name|guid>`, now on every verb), else the caller's own card's board and standing agent's board (from `X-Antiphon-Task-Token`), else the boards of every project whose `LocalRepositoryPath` contains `?cwd=` (`card.ps1` sends `git rev-parse --show-toplevel`), else everywhere — uniqueness demanded inside the scope that answers, never a silent first row. A collision that survives all of that is `409 card_identifier_ambiguous` listing every candidate (board, guid, status, title) in `detail` and in a `candidates` extension; `-Board` on a card the board does not hold is a 404 naming where it does live. Two boards hold cards today (Antiphon, Gym Stat — `CARD-0001…0021` collide) and every project setup adds a board that will collide from its first card, so never resolve an identifier with a bare global query; go through `CardIdentifierScope`.

### Preserved Gotcha #72

- **A tracker sync announces itself by TARGETED SEND, opt-in per trigger, addressed per board** (CARD-0171): the alert path is not it — `ChannelAlertRouter` selects sinks by severity alone, so making a chat an alert sink delivers every stalled-task/quota alert there too, in the `Antiphon alerts:` digest voice. `TrackerSyncNotifier` composes the summary server-side and sends it through `ChatChannelService.SendAsync`; there is deliberately **no** generic `POST /api/channels/{id}/send` (the CARD-0171 draft's endpoint was discarded — a text-to-any-channel megaphone with no audit row needs its own card). Two separate switches, BOTH required: `?notify=true` is **per trigger** (`scripts/github-sync.ps1 -Notify`; the Windmill schedule passes it; the board's "Sync tracker now" button does not, so a click never pings a family chat) and `tracker.notify_channel` is **per board** (a channel GUID, or a unique exact title) — the per-board config is the consent, the flag alone announces nothing. The gate is `TrackerSyncBoardResult.Changes.Count > 0`, which is why `ExternalReopens` had to be added: `ApplyExternalReopens` incremented **nothing** before this card, so a run that only moved a card back out of Done read as a no-op. `IssuesPulled` is never a change, and a per-board `Error` is never announced to the channel — `github-sync.ps1` now exits **1** on one instead (it exited 0, so a failed sync was a green Windmill job). A notification that cannot be sent is a reason on the response (`notify_channel_unset` / `channel_not_found` / `channel_ambiguous` / `channel_disabled` / `send_failed`) plus a Warning log, never an exception and never a failed sync — the writes have already committed. **Known noise mode, documented not built:** a human who re-adds a managed `status:*`/`priority:*` label every cycle makes `labelsChanged=1` fire on every run, so the channel hears "labels updated on 1 issue" every 3 hours; the mitigation is a `Tracker:NotifyOnLabelOnlyChanges=false` gate line if it ever bites. GitHub→card creates are out of the signal by construction (the read-only tick creates the card first) — that is CARD-0173.
<!-- CARD-0254 preserved source ends -->
