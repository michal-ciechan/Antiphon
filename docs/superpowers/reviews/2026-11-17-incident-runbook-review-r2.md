# Review: server2 dispatch incident runbook (round 2, Final, diff only)

Review task 324876aa (Claude Opus), 2026-11-17. The subject is `docs/incident-runbook-server2-dispatch.md` at
`64d1bf5e68959ec798561df7bb18e75bc3024988` on `origin/feat/card-task-292c3299`, written by Grok in task 292c3299.
It adds one docs commit on top of the round-1 reviewed tip `74296b2c0245244ae3f45a9987727b88d72cabe7`. The round-1 review is
`docs/superpowers/reviews/2026-11-17-incident-runbook-review.md` on `origin/feat/card-task-2838f508`. This round reviews
`git diff 74296b2c0..64d1bf5e6` only, against master `300c8a99d0d267a39791410b7540189ab702f251`. Read-only: no product
file, setting, host or ledger was changed.

**Verdict: CLEAN. Round-1 blocker D-1 is fixed. No new blocker. The advisories below go to cards and do not hold the land.**

## Gates

- `git diff --name-status 300c8a99d..64d1bf5e6`: one file, `A docs/incident-runbook-server2-dispatch.md`.
  `git diff --check 74296b2c0..64d1bf5e6`: clean.
- `scripts/check-evidence-diff.ps1 -Repository . -BaseRef 300c8a99d… -HeadRef 64d1bf5e6…`: exit 0, commits=2,
  entries=0, violations=0.
- `git merge-tree --write-tree origin/master 64d1bf5e6…`: exit 0, tree `98ce130fdf75828ac64a99de0d77659e75c9c90d`
  (`origin/master` = `300c8a99d`, checked with `git ls-remote`).
- Tests: none run by this review. The subject changes no code or tests, so the guard batch does not apply. The docs scan
  in `RefreshGithubTokenScriptTests` (`tests/Antiphon.Tests/Scripts/RefreshGithubTokenScriptTests.cs:85-91`) matches
  `\bdocker exec\b.*\b(?:ba)?sh -s\b.*<<`. The runbook has no `<<` (grep count 0), and both new `docker exec` lines run
  `jq`, not `sh -s`, so the scan cannot match. The author ran that class at the tip: 3 succeeded, 0 failed, 0 skipped.

## D-1 (round 1): fixed

- `:124-136` now counts with `jq` inside `antiphon-runner-session-runner-1`, in a bash form for the server2 host and a
  pwsh form for the desktop. Both have the same jq program and the same six fields.
- The image claim is right. `docker/session-runner-grok/Dockerfile:118-126` installs a pinned static `jq` 1.7.1 into
  `runtime-base` (`:70`). `session-testing` (`:190`) builds from `receipt-probe` (`:181`), which builds from
  `runtime-base`. The only Python mention is `NO_PYTHON=1` in the git build stage (`:29`).
- The jq program cannot fail on an empty ledger. `QuarantinedDirectories` defaults to `[]`
  (`RunnerWorkspaceSlotLedger.cs:93`), so `.quarantinedDirectories[]` always iterates.
- The pwsh quoting is right. In a single-quoted pwsh string, `''` is one `'`. The remote shell gets the jq program as
  one single-quoted word. The orchestrator ran this form from the desktop against server2 at 07:10Z. It printed `open 6`
  and then six rows.
- Other commands in the runbook: `restart-apphost.ps1`, the HTTP GETs, the log grep, and `dotnet-stack`/`dotnet-dump`
  all run on the desktop. None runs `python3`, `awk`, `column` or `curl` in the container. There is no `docker exec`
  heredoc.

## Applied advisories: checked against the sources

- **A-2 (dates), correct.**
  - `:237` labels 16:00 and 21:49 as 2026-11-16 and 01:05 as 2026-11-17. This matches the slot investigation's "Three
    filesystem quarantines in about 9 hours, at 16:00, 21:49 and 01:05" (`docs/investigations/2026-11-17-slot-pool-degraded-incident.md:109`),
    which ends at 01:05 on 2026-11-17.
  - The age-out is right. `RunnerWorkspaceSlotPool.cs:822` drops rehomes older than 24 hours, and `:826` retires a slot
    when the count is `> MaxRehomesPerSlotPerDay` (3, `PhoneHomeWorkspaceSlotSettings.cs:25`). ws-01 re-homed at 16:00,
    22:02 and 01:05 (investigation `:117-119`). So the 16:00Z 2026-11-16 rehome ages out at 16:00Z on 2026-11-17,
    which is what plan `1cb47ef53` `:285-286` and G-10 say.
- **A-3 (`/api/version` before restart), correct.**
  - `scripts/restart-apphost.ps1:56-57`: `-ExpectedServerSha` "must equal source-root HEAD".
  - `:187-214`: exit 3 when HEAD is unreadable, the SHA is malformed, or the SHA is not HEAD; otherwise the expected SHA
    is HEAD.
  - `:426-437`: the exit-5 "server build unverified" block.
  - So the runbook's "does not preserve an older served build" is right.
- **A-4 (after CARD-1744), correct.**
  - `docs/docker-stack.md:2097`: once the server2 container is the control plane, `restart-linux-server.ps1` is the
    production restart and `restart-apphost.ps1` is the desktop rollback. Until the window, `restart-apphost.ps1`
    stays the production restart.
  - `:1064-1065`: the Docker server's soft rollback is a server restart with `Delegation__WorkspaceSlotRoles=none`.
- **A-5 (alarm `{Reason}`), correct.** `server/Application/Services/RunnerAlarmCoordinator.cs:134` is the
  `LogWarning("Runner {RunnerId} unavailable for {Minutes:0.0} min ({Reason})"`. Its third argument at `:135` is
  `snapshot.DisconnectReason`. Calling the lease expiry an inference is right.
- **A-6 (container restart), correct.** "Treat … as killed", with pty survival unverified, removes the contradiction.
- **A-7 (server pid), correct.** `Antiphon.AppHost/Program.cs:66-67` is `var server = builder.AddProject<Projects.Antiphon_Server>("server", …)`.

## Proposed index lines (author report 292c3299; not applied, which is correct)

- **`AGENTS.md`, under "Essential front doors":** `- [Server2 dispatch incidents](docs/incident-runbook-server2-dispatch.md): …`.
  - The section exists (`AGENTS.md:32`), and the placement follows round-1 A-1.
  - The link matches `AgentContextContractTests.every_local_owner_document_named_by_the_routing_index_exists`
    (`tests/Antiphon.Tests/Application/AreaMapTests.cs:443-459`). That test needs the runbook in the same tree, so the
    AGENTS.md line must land with or after the runbook, never before it.
  - Size: `AgentContextByteLimit = 24_576` (`AreaMapTests.cs:431`), checked as `<=` on raw UTF-8 bytes. AGENTS.md is
    19,653 bytes on master and at the tip. The proposed line is about 260 bytes, which leaves about 4,660 bytes.
- **`docs/ops-http.md`, under `### Runner workspace slots (CARD-0824 S1)` (`:406`):** links
  `incident-runbook-server2-dispatch.md`. Relative to `docs/`, that path resolves. Its sentence "`POST
  /sessions/workspace-slots/quarantine/{dirId}/release` is not on this checkout" matches the runbook `:168`.

## Advisories (to cards; they do not hold the land)

- **A-R2-1. Mark the pwsh form as tested.**
  - At `:132`, replace "The PowerShell form is UNTESTED on the desktop: verify it there by a first line `open <n>` and
    one line per open row." with "Tested 2026-11-17 07:10Z from the desktop in pwsh 7 against server2: the first line
    is `open <n>`, then one line per open row."
  - Keep "A jq error means the quotes were mangled."
  - Optionally add "not Windows PowerShell 5.1". Older native-argument passing can strip the embedded double quotes.
- **A-R2-2. A second recurring cause: custody `lingering`.** The new fact: at 07:10Z the ledger has **six** open
  quarantines. The round-1 review saw the same six at 06:47Z. The sixth is `c3db3bb02698`, ws-02, `custody/lingering`,
  branch `feat/card-task-c6c85b5b`, opened at 02:28:23Z. That is after roles were set to `none` at about 01:26Z. The
  task was already leased on ws-02 at 01:13Z.
  - Of the six, three are custody/lingering: a leftover process, such as a `sleep`, whose cwd is the slot directory.
    CARD-1961 fixes only the filesystem residue, so the plan does not stop these.
  - `none` does not stop a slot that is already leased from quarantining at release.
  - The runbook already says custody `lingering` is separate and never self-closes (`:180`, `:196`). It is not a
    blocker, but these edits would help:
    1. In "Why the quarantines opened", say custody `lingering` recurs at release of any leased slot, under any role
       setting. Say it is not covered by CARD-1961.
    2. In section 4, add a dated row for the 02:28:23Z sixth entry. Cite the 06:47Z round-1 read and the 07:10Z
       orchestrator read.
    3. In the R-0 paragraph (`:172`), say the "five entries passed the Pushed checks" covers only the 01:13Z five. Any
       later entry, including `c3db3bb02698`, needs its own pid-gone check and Pushed check before release.
    4. Optionally add a triage row: the count rose while roles were `none` → custody `lingering` from a lease that
       was already out.
  - File a card for the producer: delegates leave background `sleep`s with their cwd in the slot.
- **A-R2-3. A broken relative link on master (outside the round-2 diff; unchanged since round 1).**
  - The Notes link at `:242` is `investigations/2026-11-17-server2-phone-home-stale.md`. That note is only on
    `origin/feat/card-task-e5d7e9da`, not on master `300c8a99d`, so the link is dead after the land.
  - No test checks it. The line itself says the note is not on master.
  - Fix: land the e5d7e9da note first, or make the line plain text (branch and SHA) until it lands.

## Not run / pending

- No build or test. The change is docs only, and the brief names no PC or manual acceptance for it.
- I did not re-read the live ledger. The 07:10Z six-row result is the orchestrator's observation as given in the brief.
  It matches the 06:47Z round-1 read.
