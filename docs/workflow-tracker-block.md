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
  `u/lndcobra/antiphon_github_sync` schedule.

## Ownership / out of scope (v1)

- Sync owns managed labels `status:*` (always) and `priority:*` (export-origin only). A human
  edit of those on GitHub is rewritten on the next sync.
- GitHub-only writes. Jira/Linear stay read-only.
- Comment edits/deletions after posting are out of scope (Antiphon→GH and GH→Antiphon).
- Assignees, milestones, GH Projects, webhooks, auto-injecting GH comments into agent sessions,
  TrackerConfig form UI, and archive→GH-close semantics are out of scope.
