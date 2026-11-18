# 2026-11-18 retire-temp fresh preview: `host-ls-remote status=2` is a `withdrawn-ref` race (CARD-1986)

Task e2099147 (Investigate, desktop, read-only). Master read: `e229fb4d3fb859ee16f82bc553b1d4a5cfdccce4`
(`origin/master` at 06:48Z, the same SHA as the rc/2026-11-27 rollout). Scripts quoted from that SHA.

## Verdict

**Confirmed, transient but recurring. Not a network, credential, path or GitHub fault.** The fresh
preview at 06:34:59Z-06:46:30Z (run `c727bca9d4f051fe`, operation `c10085a6c40ad1f8e49b5b449403634b3f63f`)
failed because fleet task branches were fast-forward pushed to origin between the first host proof
(06:39:18Z) and the renewal proof (06:45:08Z-06:46:26Z). The renewal treats any changed line of the
previous advertisement as `withdrawn-ref`, does not retry it, and reports it as
`RecycleGitAuditUnknown audit check=host-ls-remote status=2`. This is the already-filed defect
**CARD-1986** (InProgress, High/Soon); this is its third observed occurrence today (05:10 apply,
05:16 preview, 06:46 preview).

## (A) What `host-ls-remote` is

- It runs on the **desktop host**, not on server2 and not in any container. `Invoke-C1609HostGit`
  (`scripts/c1609-host-proof.ps1:69-80`) runs the desktop's `git` with
  `-c protocol.allow=never -c protocol.https.allow=always -c http.followRedirects=false -c credential.interactive=false`,
  env `GIT_TERMINAL_PROMPT=0` (`:23-27`), inside a short scratch bare repo under `%TEMP%\c1609-host-<guid>`.
- Command: `git --git-dir <scratch> ls-remote https://github.com/michal-ciechan/Antiphon.git refs/heads/* refs/tags/* refs/antiphon/salvage/*`
  (`:282`, again at `:335`; live control at `:416`). The URL is the trusted origin from the operator registry
  (`trusted-origins.json` / `private-origins.txt` in the exchange inventory). `repos/antiphon` is only the
  repo label from the temp volume inventory; the host never reads that path.
- Credential source: the desktop user's normal git credential helper (non-interactive). Not printed.
- Timeouts: 30 s per ls-remote (`[Math]::Min(30000, ...)`, `:281`, `:330`), 300 s for fetch/bundle,
  600 s overall network budget; the apply's live control is 5 s (`:400-404`).
- **`status=2` is not a git exit code.** Every failure in an attempt, whatever its reason token
  (`git`, `snapshot`, `withdrawn-ref`, `deadline`, ...), is rethrown as
  `Get-C1008HostLsRemoteDiagnosis -Repo <repo> -Status 2` (`:358`, `:79`, `:423`;
  formatter `scripts/c1008-host-proof.ps1:53`). The real reason is written only to the local
  `host-proof.log` in the exchange directory.
- Retry rule (`:213-229`): only `snapshot` is retried (max 5 attempts in the budget). `withdrawn-ref`
  (`:299`, raised when any line of `PreviousProofFile`'s advertisement is missing from the new
  `ls-remote`, i.e. any ref deleted **or moved, even fast-forward**) is thrown immediately.

## Evidence (run `c727bca9d4f051fe`, `C:\src\Antiphon\.antiphon\rolling-server2\c727bca9d4f051fe`)

| Item | Value |
|---|---|
| `remote-output.txt` | `transport-exit=2`, `failure-reason=` empty (no stderr recorded) |
| exchange-6497e668 (initial) `host-proof.log` | `attempt=1..3 reason=snapshot`, then `refs=6838`; proof `created 1794983958` (06:39:18Z) `completed` 06:39:50Z |
| exchange-0822b338 (renewal) `host-proof.log` | `attempt=1 reason=snapshot`, `attempt=2 reason=withdrawn-ref` (log 06:46:26Z) |
| exchange-0822b338 `c590-result.json` | `{"accepted":false,"diagnosis":"RecycleGitAuditUnknown audit check=host-ls-remote status=2 repo=repos/antiphon","exit":2}` |
| inventory (both exchanges) | `c590-result accepted:true`; origin `https://github.com/michal-ciechan/Antiphon.git` (registry match) |

Reconstruction: attempt 1 of the renewal passed the withdrawn-ref check at ~06:45:08, then a push moved a
ref during its fetch (`snapshot`); attempt 2's `ls-remote` no longer contained the moved line, so
`withdrawn-ref` was thrown and not retried.

## (B) Reproduction (read-only)

- Desktop, 06:50:06Z, same URL/ref patterns, 30 s timeout: `ls-remote exit=0`, stderr empty, 6839 refs.
  Reachable, credential valid, no rate limit or 5xx.
- Diff of the 06:39:18Z proof advertisement (6838 lines) against that live listing: exactly 4 lines
  withdrawn, all **fast-forward** moves of fleet task branches (old tip is ancestor of new):

| Branch | Old | New | New tip committer time |
|---|---|---|---|
| feat/card-task-746d140c | 7fc39b0f0 | 5108b429c | 06:45:41Z (inside renewal) |
| feat/card-task-ee23f2a2 | 9c511f163 | f077179ad | 06:46:09Z (inside renewal) |
| feat/card-task-6a008315 | a95ebc443 | 1f5e2db82 | 06:48:21Z (tip now; an earlier push in-window is not excluded) |
| feat/card-task-62cec2de | 342e4a090 | 81ba831c0 | 06:49:10Z (same) |

  plus one new ref (`feat/card-task-e46b1767`), which the check tolerates.
- server2 container exec was not needed: no part of this check runs on server2 or reads
  `repos/antiphon` from a container. The temp volume inventory (which does read it) was accepted in both exchanges.

## (C) Transient or deterministic

Transient per run, structural overall. Earlier previews passed only when no existing ref moved
in the window: 05:32Z (`c727ceb329f59c3e`, initial + renew both `refs=6814`, no retries) passed;
05:16Z (`c727c1b8247b5eab`, renew `attempt=1 reason=withdrawn-ref`) and 06:46Z failed with the same
signature. The probability of failure grows with fleet push rate and with the initial-to-renewal gap
(here ~6 min; initial exchange `networkMs=194898`). Nothing about path, credential or the runner recreate changed.

## (D) Next commands

Orchestrator (allowed by the gate 9 rule "one fresh preview and apply"; the PartialRefused
`c100884f1e02a03b541118d329379ce16e480` is closed by a fresh operation, not a Resume):

1. Pick a quiet window: hold new dispatches/pushes to `origin` (server2 and desktop task branches) for
   about 20 minutes, or at least avoid the start of Code/Debug tasks that push frequently. Check
   `git ls-remote origin 'refs/heads/*'` twice 60 s apart shows no change before starting.
2. Fresh preview:
   `pwsh -NoProfile -File scripts/deploy-server2.ps1 -Rolling -Sha e229fb4d3fb859ee16f82bc553b1d4a5cfdccce4 -Phase retire-temp -DryRun`
3. On `C1609_RETIREMENT state=Ready operation=<op>`, within 900 s:
   `$env:ANTIPHON_VOLUME_RETIREMENT_APPLY = 'enabled-<op>'; pwsh -NoProfile -File scripts/deploy-server2.ps1 -Rolling -Sha e229fb4d3fb859ee16f82bc553b1d4a5cfdccce4 -Phase retire-temp -RetirementProof <op>`
4. If either refuses again with `host-ls-remote status=2`, read the newest
   `exchange-*\c1008-proof-*\host-proof.log`; `reason=withdrawn-ref` is this race again: retry in a
   quieter window, do not open a new Debug for it (CARD-1986 owns it).

Operator: nothing. No credential refresh is needed; the registry is present and accepted.

Code fix: already carded as **CARD-1986** (narrow `withdrawn-ref` in `scripts/c1609-host-proof.ps1:299`
and `:419` to deleted/non-fast-forward/tag/temp-volume-own refs; report `withdrawn-ref` with the ref name
instead of `status=2`). The apply's 5 s live control (`:416-419`) has the same semantics, so the apply
is exposed to the same race (and to CARD-1989's 5 s window). No new card needed.

## (E) server2 state (read-only, 06:52:19Z)

- `antiphon-runner-session-runner-1`: Up 3 hours (healthy), image `session-testing:e229fb4d3fb8`, started 03:55:44Z.
- `antiphon-runner-build-slots-1`: healthy.
- No container with compose project `antiphon-runner-temp` (absent).
- Volume `antiphon-runner-temp_work` still present (the only remaining temp volume).

## Remaining uncertainty

- Exact push times of the 6a008315 and 62cec2de tips inside 06:39-06:46 are not proven (their current
  tips are later); two branches with in-window commits are enough to explain the refusal.
- `remote-output.txt` carries no `failure-reason` in this run, so the only record of the token is the
  local `host-proof.log` (CARD-1986 slice 2).

Not done, noted: retry `withdrawn-ref` when every changed ref is a fast-forward (CARD-1986 slice 1).
