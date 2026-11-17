# Review: server2 dispatch incident runbook (round 1, Final)

Review task 2838f508 (Claude Opus), 2026-11-17. The subject is `docs/incident-runbook-server2-dispatch.md` at
`74296b2c0245244ae3f45a9987727b88d72cabe7` on `origin/feat/card-task-1cfb5216`, written by Grok in task 1cfb5216.
It is one docs-only commit on master `300c8a99d0d267a39791410b7540189ab702f251`. Read-only: no product file, setting,
host or ledger was changed.

**Verdict: one blocker (D-1). Everything else is advisory.**

## Gates

- `git diff --name-status 300c8a99d..74296b2c0`: one file, `A docs/incident-runbook-server2-dispatch.md`.
- `scripts/check-evidence-diff.ps1 -Repository . -BaseRef 300c8a99d… -HeadRef 74296b2c0…`: exit 0, commits=1,
  entries=0, violations=0.
- `git merge-tree --write-tree origin/master 74296b2c0…`: exit 0, tree `a1ee79ad1a84be39bd9a6fa2f2a487cc102bda4b`
  (`origin/master` = `300c8a99d`).
- Tests: none run. The subject changes no code or tests, so the guard batch does not apply. The one docs scan the
  brief names, `RefreshGithubTokenScriptTests` (`tests/Antiphon.Tests/Scripts/RefreshGithubTokenScriptTests.cs:85-91`),
  flags lines matching `\bdocker exec\b.*\b(?:ba)?sh -s\b.*<<`. The runbook has no `<<` anywhere (grep count 0), so
  it cannot match. `CodexRootExecContractTests.Provider_exec_into_the_runner_names_uid_1654` also scans `docs/**/*.md`,
  but the runbook's one `docker exec` runs `python3`, not a provider CLI.
- Sources were read at the SHAs the runbook names. Each is the current tip of its branch:
  `7e105faca` (e5d7e9da), `1cb47ef53` (87ebe4f0) and `125222cb4` (2bef90c6).

## Blocker

### D-1. The ledger count command cannot run: the server2 runner image has no `python3`

- **Where:** `docs/incident-runbook-server2-dispatch.md:124-127`. Section 2 "Count the open quarantines" is
  the first command for incident B in the triage table (`:198`), and every R-step reads it (`:177`).
- **Failure:** `docker exec antiphon-runner-session-runner-1 python3 -c ...` fails with "executable file not
  found". I checked this inside the live server2 runner container (`PhoneHome__RunnerId=server2`, the container
  that holds `/work/workspace-slots/ledger.json`): `python3: command not found`. No `python*` exists under
  `/usr/bin` or `/usr/local/bin`. The image is `docker/session-runner-grok/Dockerfile` target `session-testing`
  (`docker-compose.server2-runner.yml:38-39`). None of its apt installs (`:98`, `:200`) adds Python. It does
  carry `jq` (`:118-126`, CARD-0927) and node (`:227`, `:275`).
- **Why it blocks:** for incident B, the operator's confirm step, the R-0..R-4 advance and rollback reads, and the
  "`open` greater than 4" test in the triage table all depend on this one command. Run as written, it fails
  harmlessly but leaves no read path. An operator might then improvise, for example by pasting the ledger, which
  the runbook forbids.
- **Fix:** use `jq`, which is in the image, and keep the same field list. For example:
  `ssh -o BatchMode=yes -o ConnectTimeout=30 mc@server2 "docker exec antiphon-runner-session-runner-1 jq -r '[.quarantinedDirectories[]|select(.isOpen)] | \"open \(length)\", (.[] | \"\(.dirId) \(.slotId) \(.class) \(.reason) \(.branch) \(.bytes)\")' /work/workspace-slots/ledger.json"`.
  A `node -e` one-liner also works. Re-check quoting for the shell it is shown under: the block is fenced `bash`,
  but the desktop operator shell is pwsh. Keep it free of heredocs. I ran the jq form read-only in the container,
  and it printed the count and those six fields. This finding is empirical. No source mentions the read tool:
  the incident read the ledger from inside the container and does not record the command it used.

## Verified (no defect)

1. **Routes, fields, templates, settings, file:line.** All match master `300c8a99d`:
   - `DispatchHoldDetails.cs:103-104`, the hold text.
   - `PhoneHomeRunnerDirectory.cs:164`, the exception; `:709-710`, status `available`; `:723-731`, the
     disconnect reason order; `:831-839`, the `DescribeAsync` descriptor; `:984-994`, `ReadSnapshot`, which has
     no logger.
   - `SessionRunnerCatalogue.cs:90-91`, `:123-124` and `:137-150`; `PhoneHomeContracts.cs:21` (lease 90) and
     `:554` (`phone_home_request_timeout`).
   - `PhoneHomeRecoveryPump.cs`: `:73`, `:236-244`, `:264-266`, `:268-272` ("failed {n} times in a row"), `:290`
     and `:363-366`.
   - `SessionRunnerEndpoints.cs:311-314` (`epoch {Epoch} accepted:`) and `:422` (`ended:`).
   - `RunnerAlarmCoordinator.cs:134`. Its `{Reason}` is the status `DisconnectReason`, through
     `RunnerEligibilitySnapshot`.
   - `AgentTaskDispatcher.cs:1153-1155`, `PhoneHomeRunnerSettings.cs:78` (`InventoryRefreshSeconds` 30).
   - `RemoteWorkspacePreparer.cs:105-111`, `:345-359` and `:368-369`; `DelegationSettings.cs:734` (30) and
     `:752` (900).
   - `WorkspaceSlotContracts.cs:12`, `:64-65`, `:100` and `:107-120`.
   - `RunnerWorkspaceSlotPool.cs:95-96` (`>`), `:826` and `:942`; `PhoneHomeWorkspaceSlotSettings.cs:24` (4);
     `RunnerWorkspaceSlotLedger.cs:78` and `:109` (Web, so camelCase).
   - `WorkspaceSlotRoles.cs:6-8` and `:96-102`, and `RolesNone = "roles_none"` (`:71`);
     `PhoneHomeCommandDispatcher.cs:488`.
   - Docs: `docs/ops-http.md:138`, `:148`, `:149`, `:154`, `:408`, `:413` and `:427-430`;
     `docs/docker-stack.md:1045-1047`, `:1053-1056`, `:1062-1063` and `:1066-1068`;
     `docs/apphost-runbook.md:37-47`, `:76-85` and `:172-175` (the 15-minute stamp).
   - The live ledger's JSON keys are `quarantinedDirectories`, `isOpen`, `dirId`, `slotId`, `class`, `reason`,
     `branch` and `bytes`. `attention` is top-level, and `rehomes` is per slot (`slots[].rehomes`).
   - `Delegation:WorkspaceSlotRoles` takes a single string, and `none` is the hot-reloaded soft rollback.
2. **Recovery safety and order.**
   - The locks are checked before `restart-apphost.ps1`, from the main checkout (a worktree exits 3).
   - The restart leaves Postgres, runner 17204, and the server2 container untouched.
   - The Windows-qualification host-load flake caveat matches investigation option 4.
   - The don'ts are present: no server2 container restart, no hand removal or ledger edits, no raised
     `MaxQuarantinedDirectories`, and the token is not printed.
   - The only commands are the restart and a read-only `ssh ... docker exec` (once D-1 is fixed). Nothing deletes
     or overwrites.
3. **Today vs after CARD-1960 are separated.** `:159` says there is nothing to call today. `:161` says the route
   "is not in this checkout". `:175` says not to start re-enabling while 1960 is a plan. R-0..R-4 match plan
   `1cb47ef53` lines 276-280, row for row: Needs, Setting, Advance and Roll back. The lingering rule, the
   leases-out rule and the ws-01 `rehomes` check also match.
4. **Numbers match the sources:**
   - 928,997 events and 260.6 MB, about 98/s. The 2h38m span is omitted but consistent.
   - 3 List timeouts at 02:49:03, 02:50:04 and 02:51:05; lease expiry about 02:50:47, inferred and labelled so;
     the alarm at 02:53:47.
   - 15 samples at 03:25-03:28.
   - 5 open quarantines, whose per-row GiB add up to 7.90; the fifth at 01:05:51.
   - 19 refusals by 01:21 and 22 in total, the last at 01:24:14.
   - Roles set to `none` at about 01:26; the four tasks started at 01:27:42 to 01:39:26.
   - Holds and ERR ageing; release-kill tasks.
5. **UNOBSERVED kept:** the stuck await (`:88`, `:219`), runner-side logs, CPU and pty survival (`:229`), the ledger
   after 01:40Z (`:118`, `:227`), the live roles value (`:147`), and the chunk-list path (`:84`). The restart outcome
   is credited to the caller, with no fabricated timestamp (`:80`, `:220`).
6. **No secrets.** `mc@server2` and `antiphon-runner-session-runner-1` already appear in `docs/logs.md:82` and
   `docs/docker-stack.md:122`. The runbook has no token, key or IP.
7. **Proposed index lines (author report, not applied, which is correct).** Both are well-formed, and their link
   paths resolve. The `docs/ops-http.md` sentence fits under `### Runner workspace slots (CARD-0824 S1)` (`:406`).
   See A-1 for where the AGENTS.md line belongs.

## Advisories (to cards; they do not hold the land)

- **A-1.** The proposed `AGENTS.md` row goes in "Read before changing", but an operator incident procedure is not a
  change area. It belongs under "Essential front doors". Check the AGENTS.md size contract
  (`docs/agent-instruction-file-contract.md`) when adding it.
- **A-2.** `:204` says "Times are UTC on 2026-11-17", but the quarantines at 16:00 and 21:49 in `:228` (and 19:52 and
  22:02 in the ledger) were on 2026-11-16. Label those dates.
- **A-3.** `:78`: say how to check, before the restart, that the main checkout's HEAD equals the served
  `/api/version`, or pass `-ExpectedServerSha <served sha>`. Otherwise a moved checkout turns the recovery
  restart into an unplanned code activation.
- **A-4.** CARD-1744: after the production server moves to server2, the recovery is
  `scripts/restart-linux-server.ps1`. The soft rollback becomes a Docker server restart with
  `Delegation__WorkspaceSlotRoles=none` (`docs/docker-stack.md:1064-1065`). Add one line on this, as the 1960 plan
  does.
- **A-5.** `:48`: the alarm line's `{Reason}` is the status disconnect reason at the time of the alarm. The
  investigation does not quote it, so "which was the lease expiry" is an inference. Label it as one.
- **A-6.** `:55`: "kills the sessions and pty hosts" conflicts with "unverified for pty survival" in the next
  sentence. Say "treat as killed".
- **A-7.** `:90`: the description of the server pid (an `Antiphon.Server.exe` child of `dotnet run`) is uncited.
  `Antiphon.AppHost/Program.cs:67` uses `AddProject`.
- **A-8.** The brief's "45-minute window to 03:36:20Z" is not in any cited source, and the runbook rightly does not
  state it. If the caller has the restart log lines, add the epoch-accepted time as a sourced row.

## Live observation (read-only, not a defect)

At 06:47Z the server2 ledger (`/work/workspace-slots/ledger.json`, modified 02:28:25Z) has **6 open of 6**
quarantines. The sixth is `c3db3bb02698` on ws-02, `custody/lingering`, opened at 02:28:23Z, after the 01:40Z read.
The pool would still refuse leases if roles were turned back on. This is consistent with the runbook's "count
again before you act".
