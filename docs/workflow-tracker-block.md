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
| `project` / `project_key` | Linear/Jira | Project key. |
| `jql` | Jira | Extra JQL filter. |

Any other scalar key under `tracker:` is retained in `IssueTrackerConfig.Options` (that is how
`sync_out_create` and `export_since` are read).

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

## Ownership / out of scope (v1)

- Sync owns managed labels `status:*` (always) and `priority:*` (export-origin only). A human
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
