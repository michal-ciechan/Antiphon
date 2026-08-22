# CARD-0144 — weekly cleanup of stale/disconnected Claude Code remote sessions: plan

**Date:** 2026-08-22 · **Card:** CARD-0144 (`1a2bdf0b-573b-4d6e-9d7c-de24c591d38c`) ·
**Status:** plan (no implementation in this pass) ·
**Verified against:** master `6085885`. Every endpoint, field, menu item and browser gotcha below
was measured live against the real account on 2026-08-22 between 14:00Z and 14:15Z; nothing here
is inferred from documentation.

**Sibling:** the root-cause card for *why* several "Antiphon-Orchestrator" sessions exist is out of
scope (card says so explicitly). This plan consumes the staleness signal, it does not explain it.

---

## Verdict up front

**The card's three open questions are all answered, and the answer to #3 removes the browser from
the design entirely.**

1. The per-row kebab menu exposes **Archive (`A`)** and **Delete (`D`)** — confirmed by opening it.
2. There is no timestamp in the sidebar DOM (confirmed: zero `<time>` elements, zero `title`
   attributes on any of the 18 rows) — but the API carries `created_at` **and** `last_event_at`.
3. **There is a full REST surface for code sessions, and the Claude Code CLI's own OAuth token
   authenticates against it.** `GET /v1/code/sessions`, `POST /v1/code/sessions/{id}/archive`,
   `POST /v1/code/sessions/{id}/unarchive` and `DELETE /v1/code/sessions/{id}` all exist and all
   accept `Authorization: Bearer <claudeAiOauth.accessToken>` read out of
   `~/.claude/.credentials.json`. Measured 200 on both `https://claude.ai` and
   `https://api.anthropic.com`.

So the weekly job is a **plain PowerShell script making HTTPS calls** — no CDP, no Edge, no hover
choreography, no contention with the other automation that shares the browser profile. The card's
safety constraint about the shared `C:\Users\lndco\edge-cdp` profile is satisfied by not touching
the browser at all.

The card is also **wrong on one point, and it is the load-bearing one**: it proposes
`connection_status`/the "Remote Control disconnected" banner as "the real stale and disconnected
signal to filter on". It is not a liveness oracle in either direction, and this was measured twice
in fifteen minutes:

- **`disconnected` does not mean dead.** `cse_0127JHMVHrqYtq7qW23b9osH` and
  `cse_011D79CHh3qcgGNB3mXGgdPz` both report `connection_status: "disconnected"` and both moved
  their `last_event_at` forward to *within five minutes of the sweep* while this plan was being
  written. A rule keyed on `disconnected` alone would have archived two sessions that were
  actively emitting events.
- **`connected` does not mean alive.** Archived May sessions still report `connected`, and two
  *sidebar* sessions (`cse_01VPMZ8VJbzp4D5EPA4v7Wxq` "school-revision",
  `cse_01U7SPMfVBcuuFmPq8sgQHPW` "AZ Care") report `connected` with `last_event_at` **8.8 days
  old** — while being the live bridge targets of running local always-on agents.

`last_event_at` is the only trustworthy clock. `connection_status` stays in the rule as an
*additional* narrowing condition (it only ever removes candidates, never adds), not as the
signal.

---

## 1. What the investigation found

### 1.1 The REST surface

Every row measured against `https://api.anthropic.com` (identical results against
`https://claude.ai`). **`anthropic-version: 2023-06-01` is mandatory** — omit it and every call
returns `400 {"error":{"message":"anthropic-version: header is required"}}`, which is what makes a
naive `curl` look like the endpoint does not exist.

| Method | Path | Measured result |
|---|---|---|
| `GET` | `/v1/code/sessions?statuses=active&statuses=paused&limit=50` | `200`, `{"data":[…18…],"resume_token":"…"}`. This is byte-for-byte the call the sidebar makes. |
| `GET` | `/v1/code/sessions?limit=100` | `200`, 99 rows — includes `status: "archived"`, i.e. archived sessions still exist, they are just filtered out of the sidebar. |
| `GET` | `/v1/code/sessions/{id}` | `404 {"type":"not_found_error","resource_type":"session"}` on a well-formed unknown id. |
| `POST` | `/v1/code/sessions/{id}/archive` | Route **exists**: `PATCH` → `405 Method Not Allowed`, `Allow: POST`; `POST` with an unknown-but-valid id → `404 not_found_error`. |
| `POST` | `/v1/code/sessions/{id}/unarchive` | Route **exists**: `PATCH` → `405`, `Allow: POST`. **Archive is reversible.** |
| `DELETE` | `/v1/code/sessions/{id}` | Route **exists**: `PATCH` on the item route → `405`, `Allow: DELETE, GET, HEAD, PUT`; `DELETE` unknown id → `404 not_found_error`. |
| — | `/v1/code/sessions/{id}/delete` | `404 page not found` — no such route (this is the control that proves the two above are real: the id-validation error fires *after* routing, so a 400/404-not_found means the route matched and a bare `404 page not found` means it did not). |
| — | `/v1/code/sessions/{id}/pin` | `404 page not found` — pin lives elsewhere. |

No mutation was performed. Route existence was established entirely with a **valid-format,
nonexistent** session id (`cse_01AAAAAAAAAAAAAAAAAAAAAA`) and with wrong-method probes read off
the `Allow` header.

### 1.2 The item shape

`GET /v1/code/sessions` returns per item:

```
config{model, origin, sources[], outcomes[]}   connection_status   created_at
environment_id  environment_kind  external_metadata{…}  id  last_event_at
participants[]  relations[]  status  status_bucket  tags[]  title  unread
user_message_count  worker_status
```

Observed value domains across the 18 sidebar rows:

| Field | Values seen | Use |
|---|---|---|
| `id` | `cse_<26-char>` | **The sidebar href is `/code/session_<same 26 chars>`** — the two id spaces differ only by prefix. |
| `status` | `active`, `archived` | `active` = in the sidebar. |
| `connection_status` | `connected`, `disconnected` | Narrowing only — see the Verdict. |
| `status_bucket` | `working`, `blocked`, `review_ready`, `completed` | `working`/`blocked` = do not touch. |
| `worker_status` | `running`, `requires_action`, `idle`, `WORKER_STATUS_UNSPECIFIED` | `running`/`requires_action` = do not touch. |
| `created_at`, `last_event_at` | RFC3339 UTC | **`last_event_at` is the age clock.** |
| `tags` | `["remote-control-repl"]` or `[]` | Every current sidebar row carries `remote-control-repl`. |
| `config.origin` | `claude_code_cli` | All 18. |
| `unread` | bool | 3 of the 9 candidates are unread — see §2.3. |

### 1.3 The local ↔ remote id join (the safety belt)

`~/.claude/sessions/<pid>.json` — the file `rc-status.ps1` already reads — carries
`bridgeSessionId`, and it is exactly the sidebar id:

```
13108.json → { pid: 13108, name: "Antiphon-Orchestrator", status: "busy",
               bridgeSessionId: "session_01CUD72fVwh8EQTg8LPS4FL3" }
API         → cse_01CUD72fVwh8EQTg8LPS4FL3   connected  working  running  "Antiphon-Orchestrator"
```

7 of the 18 sidebar sessions are the bridge targets of a **live local pid** right now. This gives a
machine-local, API-independent exclusion set, and it is the check that catches the two
`connected`-but-8.8-days-idle always-on agents that no server-side field flags.

Liveness must be re-derived, not assumed: these JSON files outlive their process (`rc-status.ps1`
calls that state `STALE`), so the check is "pid exists **and** its process name looks like
`claude*`/`node*`" — the same pid-reuse guard `rc-status.ps1` already applies.

### 1.4 The kebab menu (open question #1, answered)

Opened successfully on `session_01C5ZCwnjajAL6U18ioBHMoe`. Full item list:

```
0. Open in        1. Pin (P)        2. Mark as unread (U)   3. Rename (R)   4. Share
5. Copy link (C)  6. Move to group  7. Archive (A)          8. Delete (D)
```

The sequence that works (the previous investigation's `Input.dispatchMouseEvent` "timeouts" were
two separate problems, both diagnosed):

1. `switch_tab(<claude tab id>)` — **before every single call.** The CDP daemon is shared and
   another automation on this machine moved the active tab out from under three of my calls
   mid-investigation (the Cloudflare tab's URL changed twice while I worked). Wrap every helper in
   a retry-on-`TimeoutError`.
2. `cdp("Emulation.setFocusEmulationEnabled", enabled=True)` — the tab is a background tab, so
   `document.visibilityState === "hidden"` and Chrome throttles it. **Any `js()` that awaits a
   promise then hangs and surfaces as `RuntimeError: Runtime.evaluate timed out` from the harness
   IPC, not as a page error.** A `fetch` kicked off without focus emulation was still `pending`
   after 25 s; with it, it resolved in under 4 s. This is almost certainly the same wall the
   earlier investigation hit.
3. `a.scrollIntoView({block:'center'})` — the target row sat at `y=511.5` in a **482 px**
   viewport. `click_at_xy` below the fold silently does nothing.
4. `Input.dispatchMouseEvent type=mouseMoved` at the row centre, then again at the kebab centre
   (~0.4 s apart), then `click_at_xy(kebab)`. The kebab is `opacity`/`pointer-events` hover-gated.
5. `press_key("Escape")` closes it cleanly (verified: `[role="menuitem"]` count back to 0).

**This choreography is documented for completeness and is deliberately NOT used by the job.** It
is worth writing up as `C:\src\claudebot\sites\claude.ai.md` (there is no file for this host yet) —
proposed as S6.

### 1.5 Auth (open question #3, answered better than the card hoped)

`~/.claude/.credentials.json` holds `claudeAiOauth.{accessToken, refreshToken, expiresAt,
refreshTokenExpiresAt, subscriptionType, rateLimitTier}`. Sending
`Authorization: Bearer <accessToken>` + `anthropic-version: 2023-06-01` returned **200 with the
same session list** on both hosts. The access token's lifetime is short (measured 91 minutes
remaining at 14:12Z); the refresh token runs to 2026-09-05.

**The job must never refresh it.** Rotating the refresh token out-of-band risks invalidating the
CLI's own session on a machine that runs always-on agents. On an expired/absent token the job
reports and exits — see §4.

---

## 2. What gets swept

### 2.1 The rule

A session is a candidate iff **all** of:

| # | Condition | Why |
|---|---|---|
| C1 | `status == "active"` | Only what is actually in the sidebar; never re-process archived rows. |
| C2 | `last_event_at` older than `-OlderThanDays` (default **7**) | The only trustworthy recency clock (§1.2). |
| C3 | `connection_status == "disconnected"` | Narrowing only. Removes candidates, never adds. Configurable off via `-RequireDisconnected:$false`, default **on**. |
| C4 | `status_bucket` not in {`working`, `blocked`} | A session mid-turn or awaiting input is not stale. |
| C5 | `worker_status` not in {`running`, `requires_action`} | Same, from the worker's side. |
| C6 | `cse_<X>` where `session_<X>` is **not** the `bridgeSessionId` of a live local Claude pid | §1.3. Independent of every server-side field. |

C2 **and** C6 are the two that actually carry the safety; C3/C4/C5 are cheap extra narrowing.

### 2.2 The measured dry-run (2026-08-22 14:14Z)

Running exactly this rule over the live account, right now, gives **9 candidates / 9 skipped**:

```
=== CANDIDATES (9) ===
cse_01A1bGtpG1reU5iCwWMwkwyj  disconnected review_ready idle  age= 8.8d  Antiphon-Orchestrator
cse_01LtSACyrcManQGiQPbjRynd  disconnected review_ready idle  age= 9.8d  ClaudeBot
cse_01Ht9GkXxoRJdax9a3w5KTzx  disconnected review_ready idle  age=10.1d  Antiphon
cse_012tkQGM6m8ZJf86yTmguEsb  disconnected review_ready idle  age=10.7d  GBrain
cse_019hZwrMDNbF142zVeiAMsZ7  disconnected review_ready idle  age=11.2d  AZ Care
cse_01DWKb4PB87hjqG67iKxxYaP  disconnected review_ready idle  age=11.6d  Antiphon-Orchestrator
cse_01AYbKg5uH3Mk635MmvFekcq  disconnected review_ready idle  age=12.7d  Family
cse_01CwamtCHtqhpxnLkvYvuCTs  disconnected review_ready idle  age=12.9d  Torquay Leander
cse_01C5ZCwnjajAL6U18ioBHMoe  disconnected review_ready idle  age=12.9d  school-revision

=== SKIPPED (9) ===
cse_01CUD72fVwh8EQTg8LPS4FL3  connected    working  running  age=0.0d  <- connected, fresh, bucket=working, worker=running, LOCAL-LIVE
cse_0127JHMVHrqYtq7qW23b9osH  disconnected review_ready idle  age=0.0d  <- fresh          (was "6 days old" 20 min earlier)
cse_011D79CHh3qcgGNB3mXGgdPz  disconnected review_ready idle  age=0.0d  <- fresh          (was "8.8 days old" 20 min earlier)
cse_01RiGUi9P1PrXp6X9oR7WEhk  connected    review_ready idle  age=0.0d  <- connected, fresh, LOCAL-LIVE
cse_01JB6WchwnrtDH7CcKNUHLw9  connected    review_ready idle  age=0.9d  <- connected, fresh, LOCAL-LIVE
cse_01LKaY7fXgtw7u2E1o93YqRc  connected    review_ready idle  age=1.3d  <- connected, fresh, LOCAL-LIVE
cse_01EEk37nM6Zh8rfD4mNExAji  connected    review_ready idle  age=2.8d  <- connected, fresh, LOCAL-LIVE
cse_01VPMZ8VJbzp4D5EPA4v7Wxq  connected    review_ready idle  age=8.8d  <- connected, LOCAL-LIVE
cse_01U7SPMfVBcuuFmPq8sgQHPW  connected    review_ready idle  age=8.8d  <- connected, LOCAL-LIVE
```

The candidate set is exactly the card's target: the `session_01DWKb4PB87hjqG67iKxxYaP`
"Antiphon-Orchestrator" the card names by hand is row 6. The last two skips are the ones that make
C6 non-optional: both are **older than the cutoff** and both belong to running local agents.

This list is the artefact the user reviews before anything is promoted (§5).

### 2.3 Deliberately *not* in the rule

- **`unread`.** 3 of the 9 candidates are `unread: true`; archiving them loses that cue. Offered as
  `-SkipUnread` (default **off**) rather than baked in — an unread 13-day-old disconnected session
  is exactly what the card wants gone.
- **`tags` / `config.origin`.** Every current sidebar row is `claude_code_cli` +
  `remote-control-repl`, so filtering on them would be a no-op today and would silently exclude
  future non-CLI sessions from cleanup. Recorded in the report for visibility, not used as a gate.
- **Title matching** (e.g. "only Antiphon-Orchestrator"). The card's sibling root-cause card owns
  that question; a title-blind age rule is the safer default and does not pre-judge it.

---

## 3. Archive, not delete

**Default `-Action Archive`.** `POST /v1/code/sessions/{id}/archive`:

- achieves precisely what the card asks — the sidebar filter is `statuses=active&statuses=paused`,
  so an archived session leaves Recents;
- is **reversible** — `POST /v1/code/sessions/{id}/unarchive` exists (§1.1), so a mis-classified
  session is one call away from returning;
- matches what the account already contains: 81 of the 99 sessions are already `archived`, i.e.
  this is the state the UI itself puts old sessions into.

`-Action Delete` (→ `DELETE /v1/code/sessions/{id}`) is implemented and supported but is **not the
default and is not what the Windmill schedule will run**. Irreversible removal of account data on a
weekly unattended timer is a strictly worse trade than a reversible one that produces an identical
sidebar.

---

## 4. The script

`scripts/cleanup-claude-sessions.ps1` — same directory and same house style as
`cleanup-build-junk.ps1` (which the Windmill job already SSHes in to run), ASCII-only so it parses
under both pwsh 7 and Windows PowerShell 5.1, comment header stating that the recurring run is a
**Windmill schedule on server2, not a local Scheduled Task** — `scripts/install-cleanup-task.ps1`
is the superseded local-task installer and must not gain a sibling.

```
param(
  [int]    $OlderThanDays      = 7,
  [ValidateSet('Archive','Delete')]
  [string] $Action             = 'Archive',
  [switch] $Execute,                       # absent => report only. THE DEFAULT IS A DRY RUN.
  [int]    $MaxPerRun          = 10,
  [switch] $SkipUnread,
  [bool]   $RequireDisconnected = $true,
  [string] $ReportPath         = "logs/claude-session-cleanup"
)
```

Behaviour:

1. **Credentials.** Read `~/.claude/.credentials.json`. Missing ⇒ exit **2**, nothing done.
   `expiresAt` in the past (or within 60 s) ⇒ exit **2**, nothing done, message says "CLI
   credentials stale — run/refresh Claude Code on this machine". **Never refresh, never print, never
   log, never pass the token on a command line** — `Invoke-RestMethod -Headers @{...}` keeps it out
   of argv, unlike `curl -H`. The report JSON must be scrubbed of it by construction (build the
   report object from an allow-list of fields, not by echoing the response).
2. **List.** `GET https://api.anthropic.com/v1/code/sessions?statuses=active&statuses=paused&limit=100`.
   If `data.Count -ge limit`, warn loudly and exit **3** rather than sweep a truncated view.
3. **Local exclusions.** Enumerate `~/.claude/sessions/*.json`, keep `bridgeSessionId` where the
   pid is alive and its process name matches `claude*`/`node*`.
4. **Classify** per §2.1 and print the candidate/skip table with a per-row reason string.
5. **Report.** Always write `logs/claude-session-cleanup/<yyyy-MM-dd-HHmmss>.json`
   (`logs/` is already gitignored) with the classification, the rule parameters, and the counts.
6. **Act** — only under `-Execute`. Refuse (exit **4**) if candidates exceed `-MaxPerRun`: a rule
   change that suddenly matches 40 sessions must stop, not proceed. Per candidate, one call, and
   treat `404 not_found_error` as success (already gone). Any other non-2xx is counted, logged and
   does not abort the remaining candidates (same tolerance as `cleanup-build-junk.ps1`'s per-dir
   try/catch).
7. **Exit 0** on a clean run (including a dry run and including "0 candidates").

Exit-code map: `0` ok · `2` no/stale credentials · `3` truncated list · `4` cap exceeded ·
`5` one or more mutations failed.

### Rejected alternatives

| Option | Why not |
|---|---|
| Drive the kebab menu through CDP Edge weekly | Needs Edge running and logged in, needs the hover/scroll/focus-emulation choreography of §1.4 to keep working across UI redesigns, and **shares one single-connection daemon with the machine's other automation** — which visibly stole the active tab three times during a 15-minute investigation. Every one of those failure modes is silent. |
| Extract the `sessionKey` cookie from the Edge profile | DPAPI-decrypting a live account credential out of a browser profile into a scheduled script, to reach an endpoint that already accepts a token we can read directly. Strictly more secret-handling for strictly less. |
| A new local Windows Scheduled Task | Explicitly against the standing decision recorded in the `reference_windmill_cleanup_schedule` memory and in `cleanup-build-junk.ps1`'s own header. |
| Do it inside the Antiphon server (a hosted service) | This is machine housekeeping against a third-party account, not Antiphon domain behaviour; it would put an account credential inside the server process and couple a weekly chore to server uptime. |

---

## 5. Scheduling, and the promotion ladder

Follow the existing Windmill precedent exactly: workspace `mc` on
`server2.tail62cf02.ts.net`, a **bash** script with worker tag `desktop` that SSHes
`lndco@host.docker.internal` with key `/tmp/windmill/worker_to_windows` — a copy of
`u/lndcobra/antiphon_build_junk_cleanup` with the command swapped. New script + schedule
`u/lndcobra/claude_session_cleanup`, **weekly, Monday, Europe/London, offset from the 09:00
build-junk job** (09:15) so two SSH sessions never land together.

The card's safety requirement is met by shipping the schedule in report-only form first and
promoting it in a separate, explicit step:

| Step | What runs | Gate to advance |
|---|---|---|
| **A** | `-Report` by hand (S1) | User reads the candidate list and confirms every row is genuinely stale. |
| **B** | `-Execute -Action Archive` **by hand, once** (S4) | User confirms the sidebar looks right and nothing live disappeared. |
| **C** | Windmill weekly, **report-only** (S5) | Two consecutive weekly reports whose candidate lists the user is happy with. |
| **D** | Windmill weekly, `-Execute -Action Archive` (S6) | — |

Nothing between A and D is skippable, and D is a one-line change to the Windmill script body.

---

## 6. Verification

No .NET code is involved, so the honest test surface is the classifier, driven from a captured
fixture rather than the network.

`-InputJson <path>` makes the script classify a saved `GET /v1/code/sessions` body instead of
calling out (and forces `-Execute` off). Commit
`scripts/fixtures/claude-sessions-2026-08-22.json` — the real 18-row capture reduced to the fields
the rule reads (`id, title, status, connection_status, status_bucket, worker_status, created_at,
last_event_at, unread, tags`) — plus `scripts/fixtures/claude-sessions-live-bridges.json` for the
C6 input, and `scripts/test-cleanup-claude-sessions.ps1` asserting:

| # | Test | Asserts |
|---|---|---|
| T1 | Fixture at a pinned `-Now` of 2026-08-22T14:14Z splits **9 / 9** | The whole rule, against measured reality. |
| T2 | `cse_01VPMZ8…` and `cse_01U7SPM…` are skipped | C6 catches `connected` + 8.8-day-old + locally live. |
| T3 | A row that is `disconnected` but `last_event_at` = now is skipped | The `cse_0127JHMV…` shape — the card's own signal, alone, is not enough. |
| T4 | A row with `status_bucket: working` is skipped even when old and disconnected | C4. |
| T5 | `status: "archived"` rows never appear as candidates | C1. |
| T6 | No `-Execute` ⇒ zero mutating calls | Injected HTTP shim records **0** requests. Dry run is the default. |
| T7 | `-Execute` with 11 candidates and `-MaxPerRun 10` ⇒ exit 4, zero mutating calls | The cap refuses, it does not truncate. |
| T8 | `-Execute -Action Archive` issues exactly `POST …/{id}/archive`, one per candidate | Never `DELETE` unless asked. |
| T9 | A `404 not_found_error` from one candidate does not abort the rest | Already-gone is success. |
| T10 | Expired `expiresAt` ⇒ exit 2 and zero HTTP calls | No 401 storms, no refresh attempt. |
| T11 | `data.Count -ge limit` ⇒ exit 3, zero mutating calls | Never sweep a truncated view. |
| T12 | The written report JSON contains no substring of the access token | Secret never lands on disk. |

T6–T11 need an injectable HTTP surface: a `-HttpShim <scriptblock>` parameter defaulting to
`Invoke-RestMethod`. That is the only design concession the tests ask for.

---

## 7. Slices

| Slice | Contents | Done when |
|---|---|---|
| **S1** | `scripts/cleanup-claude-sessions.ps1` with **no mutation code path at all**: credentials, list, local-bridge exclusions, classify, table + JSON report. `-InputJson`, `-HttpShim`. | Running it by hand reproduces the §2.2 split. |
| **S2** | Fixtures + `scripts/test-cleanup-claude-sessions.ps1`, tests T1–T5, T10–T12. | Green on a machine with no credentials file (fixtures only). |
| **S3** | Mutation path: `-Execute`, `-Action Archive` / `Delete`, `-MaxPerRun`, per-candidate error tolerance, exit codes 4/5. Tests T6–T9. | Green; `-Execute` still never run for real. |
| **S4** | **Supervised first live run** — user reviews the S1 report, then runs `-Execute -Action Archive` once by hand. Record the before/after counts on the card. | User confirms the sidebar. Gate B→C. |
| **S5** | Windmill script + schedule `u/lndcobra/claude_session_cleanup`, weekly Mon 09:15 Europe/London, tag `desktop`, **report-only**. Update the `reference_windmill_cleanup_schedule` memory to name both jobs. | Two clean weekly reports. |
| **S6** | Flip the Windmill body to `-Execute -Action Archive`. Write `C:\src\claudebot\sites\claude.ai.md` with §1.1/§1.4/§1.5 (endpoints, the focus-emulation gotcha, the hover sequence, the shared-daemon `switch_tab`-every-call rule). | Card closed. |

S1–S3 are buildable back to back; **S4 is a human gate and must not be automated past.**

---

## 8. Open questions and risks

1. **Archive vs Delete is the user's call.** This plan defaults to Archive on reversibility
   grounds. If the intent is genuinely "gone, not hidden", S3 already supports
   `-Action Delete` and only the Windmill body changes — but note that the account currently holds
   81 archived sessions that nobody has minded, which suggests hidden is sufficient.
2. **Should `connected`-but-ancient sessions be swept?** Two exist today
   (`cse_01VPMZ8…`, `cse_01U7SPM…`, both 8.8 days). Both are excluded twice over (C3 and C6). If
   the always-on agents behind them are ever retired, C6 stops excluding them and only C3 remains —
   `-RequireDisconnected:$false` would then sweep them. Left off by default; revisit after a few
   weekly reports show what actually accumulates.
3. **Does archiving a session break a live local Claude bridged to it?** Unknown and untestable
   without mutating a live session. C6 makes it moot in practice; do not remove C6 to find out.
4. **Token lifetime vs a weekly timer.** The access token is short-lived and refreshed by the CLI.
   On a machine that runs always-on agents it is essentially always fresh, but a week-long quiet
   spell would produce exit 2. That is the correct outcome (report and stop), not something to
   engineer around by refreshing — see §1.5.
5. **`last_event_at` semantics.** Confirmed to move on real activity (two rows advanced during the
   investigation) and confirmed *not* to move merely because we read the session (the other 16 rows
   were byte-identical across two reads 20 minutes apart). It is not documented, though, and a
   change in what counts as an "event" would silently change the sweep's reach — T1's pinned
   fixture will not catch that. The weekly report is the detector: a sudden change in candidate
   count is the signal to re-measure.
6. **Sidebar `statuses=` values.** `active` and `paused` are what the UI asks for; `archived` is
   what it hides. Whether other values exist (e.g. a `deleted` tombstone) was not probed, and the
   `-ge limit` guard (§4, step 2) is what protects the sweep from an unknown status arriving in bulk.
