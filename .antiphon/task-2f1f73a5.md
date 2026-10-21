# Review 2f1f73a5: CARD-1408 cutover monitor repair (Final, Full)

Subject: Code 6743dd81-2335-43fa-94f9-accbb0c2aee1, `feat/card-task-6743dd81` at `8d4f7bd7b6c5821bb5e8e0f76a2dccec00b1b7d6`. Combined delta `origin/master...8d4f7bd7`, which includes Code 38d43631's unlanded change: `scripts/postgres-cutover-monitor.ps1`, `scripts/test-postgres-cutover-monitor.ps1`, and the CARD-1408 plan.

Verdict: **found**. The three earlier findings (D1-D3 from Review 39af9e8d) are closed. This review found two new defects. The first is a failed-land class that real lands produce and the monitor cannot count. The second is a set of credential-masking bypasses that I reproduced.

## Evidence

- CP-1 ran through the checkpoint tool: run `20261021-182634-4ea9`, `.antiphon/checkpoints/20261021-182634-4ea9/report.json`. Result: `verdict: GREEN exit=0`, `slot=granted`, `sourceState=clean`, source `8d4f7bd7`. The row console prints `POSTGRES-CUTOVER-MONITOR TESTS passed=58 failed=0`. The CHECKPOINT line shows `executed=n/a` because this is a script row; the counts above come from the row console.
- `scripts/validate-checkpoint-receipt.ps1 -Evidence <report.json> -ExpectedSourceSha 8d4f7bd7...` printed `CHECKPOINT SOURCE VALID ... rows=1` and exited 0. The tree is clean.
- `git merge-tree --write-tree origin/master(47e6916c) 8d4f7bd7` produced `5631ffcf` with no conflicts.
- `scripts/check-evidence-diff.ps1` over `13048a2e..8d4f7bd7` found commits=4, violations=0.
- Throwaway mutations were made in a scratch copy, run through `scripts/build-slot.ps1` (slot granted), and the copy was removed afterwards:
  - mutA changed `since=` to `updatedSince=`. Result: passed=28 failed=21, including the named case `task list asks with since=, not updatedSince=`.
  - mutB made `Test-CutoverLandFailed` drop the NeedsResolution clause. Result: passed=54 failed=4, including the named case `three failed lands are counted from task detail`.
- Unlisted runs, all read-only probes:
  - a dot-sourced probe of `Get-CutoverVerdict` boundaries and `Hide-CutoverText` inputs;
  - one `Invoke-CutoverHttpGet` against a loopback listener (the run hung after printing and was stopped);
  - the two mutation runs above.

## Checked against the real server (all match)

- **`since` key.** `since` is in `AgentTaskScope.SupportedListQueryKeys` (`server/Application/Services/AgentTaskScope.cs:61-71`). An unknown key is a 400 (`AgentTaskEndpoints.cs:551-562`). The handler binds `DateTime? since` (`:61-83`).
- **What `since` filters on.** Settled rows are kept when `CompletedAt >= since`. Every row that is not Succeeded, Failed or Canceled is kept whatever its age (`AgentTaskService.cs:2716-2722`). The response envelope is `{scope, items, excluded}`.
- **Failure count.** The monitor counts `status == Failed` with `CompletedAt` in the window. That matches D-11's "entered Failed in that window", and Canceled is excluded. Baseline and Watch use the same window and the same definition.
- **Summary land fields.** The summary DTO carries `landRequestedAt`, `landStartedAt` and `landAttempt` (`AgentTaskDtos.cs:311-315`). `ClearPending` (`AgentTaskLandService.cs:1434`) does not reset `LandAttempt`, so a land that started git work stays a candidate after it ends.
- **Detail DTO.** It has `summary`, `landing` and `landRequest` (`AgentTaskDtos.cs:405,439-440`). `landing` is the op at `task.ActiveLandingId`, and `landRequest` is `CurrentLandRequestId` (`AgentTaskService.cs:2788-2793`).
- **Enum strings.** Enums are serialized as their names (`Program.cs:360-364`, `JsonStringEnumConverter(namingPolicy: null)`). `Refused` is a real value of LandPhase (11) and of LandPublicationOutcome (3). `NeedsResolution` is a real value of LandRequestState. All are PascalCase and match the monitor exactly. Property names are camelCase, and the PowerShell property lookup is case-insensitive.
- **Threshold boundaries.**

  | Input | Verdict |
  |---|---|
  | median 1000 vs baseline 200 | OK |
  | median 1000.01 vs baseline 200 | ROLLBACK |
  | median 3000 vs baseline 1000 (exactly 3x) | OK |
  | median 3000.01 vs baseline 1000 | ROLLBACK |
  | median 1500.01 vs baseline 500 | ROLLBACK |
  | failures +2 over baseline | WARN |
  | failures +3 over baseline | ROLLBACK |
  | no successful samples | INVALID |
  | missing baseline, NaN baseline, or schema below 2 | INVALID |

- **A rollback already fired still wins.** At `:783-786`, a rollback that has fired keeps exit 2 even when the list or details cannot be read; V-6 has a case for this. A detail read that stops partway still counts the details it did read, which is a lower bound. Going over the cap is INVALID, never OK.
- **Exit codes.** The wrapper returns a single PSCustomObject whose `ExitCode` is an Int32, so there is no CARD-1407-style pollution. The child `pwsh` exits 2.
- **Read-only.** The only HTTP call is `Invoke-WebRequest -Method Get`, and the only file write is `WriteAllText` to the baseline or result path.

## Defects (landing owner: Code 6743dd81)

### D1. Lands that end in LandRefused without a Refused landing op are never counted

- **Where.** `scripts/postgres-cutover-monitor.ps1:173-182` (`Test-CutoverLandFailed`) and `:163-171` (candidate selection).
- **Failure.**
  - When a land throws an exception, for example an Npgsql timeout (the failure the cutover risks), `AgentTaskLandService.PersistFailureAsync` (`:1274-1310`) sets `landRequest.terminalFailureCode`. It completes the request with a `LandRefused` event through `CompleteTerminalLockedAsync`. That path never moves the landing op to `Refused`.
  - `CompleteRequest` (`:1762-1775`) sets `state = Completed` for every terminal.
  - So the detail shows `landing.phase` at some mid-flight phase, `publication: Unconfirmed` and `landRequest.state: Completed`, and the monitor counts 0.
  - `RefuseAsync` for `landing_protocol_unavailable` (`:468-471`) fires before `LandAttempt` is incremented, and `ClearPending` clears `landRequestedAt` and `landStartedAt`. The task is then not even a candidate.
  - `"land interrupted N times"` (`:1172-1178`) also ends in `LandRefused` without a Refused op.
- **Why it matters.** D-11 counts "land requests that ended failed in that window". Against the real server, the failed-land trigger cannot fire for exception-ended or pre-protocol refusals. The fake serves only `phase`/`publication: Refused` or `NeedsResolution`, so the harness cannot see this.
- **Fix.** Count a land as failed when the detail's `events` contain a `LandRefused` event with `at >= window start`, or when `landRequest.terminalFailureCode` is non-null. Keep the current Refused and NeedsResolution signals. Either widen candidate selection so it finds refusals made before the attempt counter moved, or state that remaining gap in D-6. Add V-7 cases where the fake detail has a non-Refused `landing.phase`, `landRequest {state: Completed, terminalFailureCode: ...}` and a `LandRefused` event, and assert that the land is counted.

### D2. Credential masking is bypassed by an unescaped `@` in the password, a percent-encoded key, and a fragment

- **Where.** `scripts/postgres-cutover-monitor.ps1:127-134` (`Hide-CutoverText`).
- **Failure.** All three were reproduced:
  - **(a) Unescaped `@`.** `http://admin:p@SENTX@127.0.0.1:.../api/agent-tasks` is printed as `http://<redacted>@SENTX@...`. The match ends at the first `@`, so the rest of the password stays visible.
    - .NET rejects this URI, and the request error `Cannot bind parameter 'Uri'. Cannot convert value "http://<redacted>@SENTX@..."` still contains the sentinel after masking.
    - The same text appears in every PLAN line and in the recorded JSON.
  - **(b) Percent-encoded key.** `?access%5Ftoken=SENTX` is printed unmasked, because `%` is not a key character in the regex. Meanwhile `[uri]` turns it into `?access_token=SENTX`, which is what gets sent.
  - **(c) Fragment.** `#access_token=SENTX` and `#token=SENTX` are printed unmasked, because `#` is not a boundary.
- **Why it matters.** D-5 and V-8 promise that userinfo and secret-looking query values are masked "in every printed line, error message and recorded JSON". The brief's verdict policy treats a credential leak as a found defect.
- **Fix.**
  - Match userinfo up to the last `@` before the first `/`, `?` or `#`, for example `\b([a-z][a-z0-9+.\-]*://)[^/?#\s"'<>]*@`.
  - Compare key names after percent-decoding, or allow `%[0-9A-Fa-f]{2}` in the key and decode before the test.
  - Add `#` to the boundary set.
  - Add V-8 sentinel cases for each form.

## Advisories (no rerun needed for these alone)

- **A1. `since` window semantics.** A land that fails inside the window for a task completed before the window is invisible. An unresolved `NeedsResolution` on a Blocked task is counted in every window whatever its age. Both effects apply equally to Baseline and Watch, so they cancel in the delta. State them in D-6.
- **A2. Detail GET and tokens.** `GET /api/agent-tasks/{id}` writes `LastPolledResult*` and `FullReadAt` when the caller sends a delegation token for the task's parent session (`AgentTaskService.cs:2731-2760`). The monitor sends no token header, so it stays read-only. Add one sentence to D-5 so nobody adds auth headers later.
- **A3. Out of contract.** Secrets in path segments, and keys outside the listed set (`pass`, `jwt`), are not masked. This is outside the stated contract and acceptable as it stands.
- **A4. Detail cap.** The cap of 50 is ample for a 30-minute window. A long `-WindowStartUtc` hits the cap and exits 4, never 0, and the cap cannot suppress a ROLLBACK that has already fired.
