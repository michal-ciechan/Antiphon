# 2026-11-18 gate 9 retire-temp refusal: `withdrawn-ref` (an origin branch moved during the proof window)

Debug task f3afc2b6. Read-only diagnosis: nothing was applied, deleted, restarted or touched.

- Master SHA read: `e229fb4d3fb859ee16f82bc553b1d4a5cfdccce4` (= `origin/master` and the main
  checkout `C:\src\Antiphon` HEAD at 2026-11-18 ~05:20Z; the same SHA as the rollout).
- Rollout: Q8 server2 rollout of rc/2026-11-27 (`-Sha e229fb4d…`), gate 9 `retire-temp`.

## Verdict

**Cause observed: `withdrawn-ref`.** It is not a network, credential, DNS, registry or lock failure.
Between one origin proof and the next, an unrelated live branch on GitHub moved forward:
`refs/heads/feat/card-task-632bba95`, the active CARD-1984 Code task (status Dispatched). The
retirement host proof treats *any* change to *any* previously advertised ref (6813 refs:
heads, tags and `refs/antiphon/salvage/*`) as a withdrawal and refuses. This includes an ordinary
fast-forward push to an unrelated task branch. The diagnosis then hides the reason token behind a
generic `host-ls-remote status=2` (preview) or `RemoteResultMissing` (apply) message, which makes
it look like an `ls-remote` failure.

**The failure is transient. A retry is safe.** Nothing was removed (see "Journal state"). A
retry passes only if no existing origin ref changes during each of two windows of about 8
minutes: preview and apply. A Dispatched task that pushes every few minutes makes the retry a
race. **The check is too strict for a fleet that keeps dispatching**, which is a code defect
(card text below).

## Evidence

Receipt roots under `C:\src\Antiphon\.antiphon\rolling-server2\`:

| Run | Dir | Operation | Result |
|---|---|---|---|
| Preview 04:52–05:00Z | `c7272cafbc5f17b3` | `c10088385c6bd7f0147d68be72479706fb1f7` | accepted, `state=Ready` |
| Apply 05:01–05:10Z | `c727c0694aee24ef` | `c10088385…` | `RemoteResultMissing exit=2`; `remote-output.txt`: `transport-exit=2 failure-line=423 failure-script=c1609-host-proof.ps1 failure-reason=withdrawn-ref` |
| Fresh preview 05:10–05:16Z | `c727c1b8247b5eab` | `c100871b94454a62f4cbfb51c5ad40c91bbf8` | `RecycleGitAuditUnknown audit check=host-ls-remote status=2 repo=repos/antiphon`; renew `host-proof.log`: `repo=repos/antiphon attempt=1 reason=withdrawn-ref` |

Origin advertisement digests recorded in each proof file (`c1008-host-proof 2` header):

| Proof (completed) | Run / kind | advertisement | count |
|---|---|---|---|
| 04:54Z | preview initial | `9bfd27b2…` | 6813 |
| 05:00Z | preview renew | `9bfd27b2…` | 6813 |
| 05:04Z | apply initial | `9bfd27b2…` | 6813 |
| 05:09:07Z | apply renew | `9bfd27b2…` | 6813 |
| 05:13:11Z | fresh preview initial | `00632bed…` | 6813 |
| 05:16:48Z | fresh preview renew | refused `withdrawn-ref` | – |

Diff between the 05:09:07 and 05:13:11 advertisements: exactly one line.
`refs/heads/feat/card-task-632bba95` `e0fd69f6…` -> `aed5c821…`. The branch history now on
origin (committer times):

```
7322c508 2026-11-18T05:16:46Z docs(CARD-1984): round-2 plan amendment ...
0a53ea20 2026-11-18T05:15:18Z fix(CARD-1984): create wide-lane executables out of process ...
aed5c821 2026-11-18T05:09:35Z test(CARD-1984): red witnesses ...
e0fd69f6 2026-11-18T00:59:23Z test(CARD-1984): regenerate the derived impact index
```

Each failure lines up with a push of that branch:

- **Preview 04:52–05:00 passed** because no ref moved in that window. The branch had last moved
  at 00:59Z.
- **Apply failed at about 05:10:00.** Its renew proof (05:09:07) recorded `e0fd69f6`. `aed5c821`
  was pushed at about 05:09:35. Before the remote could proceed, the coordinator's per-control
  live check (`Assert-C1609LiveOriginProof`, `scripts/c1609-host-proof.ps1:419`, reported line
  423) found `e0fd69f6…\trefs/heads/feat/card-task-632bba95` missing from the live `ls-remote` and
  threw `withdrawn-ref`. The transport (`scripts/c590-real.ps1` ~l.430–466) then killed the
  remote controller, so no `retire-temp-runner/c590-result.json` came back. The wrapper therefore
  wrote the generic `RemoteResultMissing exit=2` (`c590-real.ps1:902`).
- **Fresh preview failed at 05:16:48.** Its initial proof (05:13:11) recorded `aed5c821`, then
  `0a53ea20` (05:15:18) and `7322c508` (05:16:46) were pushed. The renew's pre-fetch comparison
  (`c1609-host-proof.ps1:299`, `foreach($line in $oldAdvertisement){if(-not $live.Contains($line)){throw 'withdrawn-ref'}}`)
  refused. The catch block (l.350–357) records the token only in `host-proof.log` and rethrows
  `Get-C1008HostLsRemoteDiagnosis -Status 2`, which gives the misleading
  `check=host-ls-remote status=2`. `status=2` is the script's own fixed code, not a git exit.

A fresh `ls-remote` at 05:21:49 against the 05:13 proof shows the same single moved ref again.
The rest of the fleet was quiet, so CARD-1984's task 632bba95 is the only mover.

### Ruled out (B)

- **Where `ls-remote` runs:** on the **desktop** control host, not server2. It is
  `Invoke-C1609HostGit` (`c1609-host-proof.ps1:69`), `git -c credential.interactive=false … ls-remote https://github.com/michal-ciechan/Antiphon.git refs/heads/* refs/tags/* refs/antiphon/salvage/*`,
  timeout `min(30000 ms, budget)` for proofs and `≤5000 ms` for the live control check. The
  scratch is a bare repo in `%TEMP%\c1609-host-<32hex>` / `c1609-live-<guid>`. Credentials come
  from the desktop's normal git credential helper. No server2 token or volume repo is involved.
  Origin is from the receipt's `trusted-origins.json` (`{"origins":["https://github.com/michal-ciechan/Antiphon.git"],"schema":1}`).
- **Reproduced read-only on the desktop:** the same `ls-remote` with the same patterns returned
  exit 0, 6813 lines, 1066 ms. DNS, HTTPS and the credential work. Every proof in all three runs
  also fetched successfully (`refs=6813`).
- **Registry:** `ssh mc@server2 ls -l /home/mc/antiphon-server2/retirement-origins.json` →
  `-rw------- 1 mc mc 128 Nov 3 19:54`. It is present and was used: `trusted-origins.json` was
  produced in every inventory.
- **Lock or clock:** no `clock`, `deadline`, `custody` or `existing` tokens anywhere. The failing
  token is `withdrawn-ref` in both runs.
- **The apply consuming the preview** is expected (Phase `Apply` admission). It is not the cause:
  the fresh preview (new operation `c100871b…`) failed the same way.

## Journal state (C)

Read-only on server2 (`/home/mc/antiphon-server2`):

- `recycle/c10088385c6bd7f0147d68be72479706fb1f7.json`: `outcome=refused`, `phase=removing`,
  `ownedRemoved=true` (owned list empty; temp had no containers), `removeReceipts=0`, all four
  volumes `pending`.
- `retirement/c10088385…/lifecycle.json`: `phase=Apply`, `active=false`, `complete=true`; all 4
  preview children `joined=true`.
- `retirement/c100871b…/lifecycle-ec29…json`: `phase=Preview`, `active=false`, `complete=true`,
  `readyPublished=false`; this preview never reached Ready.
- `docker volume ls` still lists `antiphon-runner-temp_dind-data`, `_runner-state`,
  `_runner-tmp` and `_work`.

**No partial effects:** no volume removal was started (no remove receipts or intents), so
operation `c10088385…` is a plain `refused` record, not Applying, Partial or PartialRefused.
**It needs neither Resume nor Close.** Its token is spent and must not be reused. The next
attempt is a new preview, which gets a new operation id. The fresh preview at 05:10 already
passed the census and journal checks with this record present, so the record does not block
`retire-temp`.

## What the orchestrator should run next

The runbook's one-retry allowance (fresh preview and apply, then Debug) has been used up, and
this note is the Debug outcome. Retrying is safe, but it needs a quiet origin:

1. Wait until task **632bba95 (CARD-1984 Code)** settles or pauses, and do not start another
   pushing task meanwhile. A Plan or Review that does not push is fine. To check the window, run
   `git ls-remote https://github.com/michal-ciechan/Antiphon.git refs/heads/*` twice, about 2
   minutes apart, and expect identical output.
2. Then, from `C:\src\Antiphon`, run the preview and the apply back to back:

```powershell
pwsh -NoProfile -File scripts/deploy-server2.ps1 -Rolling -Sha e229fb4d3fb859ee16f82bc553b1d4a5cfdccce4 -Phase retire-temp -DryRun
# take <op> from 'C1609_RETIREMENT state=Ready operation=<op>' and immediately:
$env:ANTIPHON_VOLUME_RETIREMENT_APPLY = 'enabled-<op>'
pwsh -NoProfile -File scripts/deploy-server2.ps1 -Rolling -Sha e229fb4d3fb859ee16f82bc553b1d4a5cfdccce4 -Phase retire-temp -RetirementProof <op>
```

If it refuses `withdrawn-ref` again, the cause is a push during the window. In the apply's
`remote-output.txt` it shows as `failure-reason=withdrawn-ref`; in the preview's
`exchange-*/c1008-proof-*/host-proof.log` it shows as `reason=withdrawn-ref`. In either case,
retry in a quieter window. The temp volumes only block the next `deploy-temp`
(`TempVolumesRetained`); they do not affect the standing runner.

## Code defect: card text to file

**Title:** Retire-temp host proof refuses `withdrawn-ref` on any unrelated branch push, which
blocks gate 9 while the fleet dispatches, and the refusal is reported as a `host-ls-remote`
failure.

**Body:**

Observed on 2026-11-18 at gate 9 of the rc/2026-11-27 server2 rollout (e229fb4d). The apply
`c10088385…` and the fresh preview `c100871b…` both refused because
`refs/heads/feat/card-task-632bba95` (a live CARD-1984 Code task) was fast-forwarded during the
proof window. Investigation: `docs/investigations/2026-11-18-retire-temp-git-audit-refusal.md`.

1. **Over-strict invariant.** `scripts/c1609-host-proof.ps1` l.295–299 (renew vs previous proof)
   and l.417–419 (`Assert-C1609LiveOriginProof`, per control request) require every
   previously advertised `<sha>\t<ref>` line, across all ~6800 heads, tags and salvage refs, to
   remain verbatim. A fast-forward push to any unrelated branch therefore refuses. Origin
   cannot stay still for the ~8 minutes per run that this needs while continuous dispatch is
   policy (docs/docker-stack.md "continuous dispatch during rollout"). The safety property that
   matters is that every object the temp audit relies on stays reachable from origin. Proposed
   direction, for Plan to decide:
   - Restrict the withdrawal check to the refs that cover the audit's required objects, or
     accept a moved ref when its old tip is an ancestor of the new tip (proven in the renew
     fetch, which already holds the graph).
   - At the live control check, either refuse only on a deletion or rewind of a covering ref,
     or turn any other move into an automatic bounded renew. Plan picks.
   - Keep refusing on a ref deletion or rewind (a force-push) of a covering ref.
2. **Misleading diagnosis.** Surface the reason in the operator-facing line. Today, the preview's
   `withdrawn-ref` reaches the operator as
   `RecycleGitAuditUnknown audit check=host-ls-remote status=2 repo=…` (the token is only in
   `host-proof.log`). The apply's reaches the operator as `RemoteResultMissing exit=2` (the token
   is only in `remote-output.txt`). Expected: a distinct, retryable diagnosis, for example
   `RecycleOriginMoved repo=repos/antiphon refs=<n> first=<refname>` (ref names are not
   secrets), plus a docker-stack.md gate-9 row that names it as a "retry in a quiet window"
   refusal. Related to CARD-1962 item 1 (failure-reason token list).

**Acceptance:**

- A witness where an unrelated branch fast-forwards between the initial proof and the renew,
  and between the renew and the live control check, and the retirement proceeds.
- A witness where a covering ref is deleted or rewound, and the run still refuses.
- A witness that the refusal line names the reason and the ref.

## Time and scope

About 35 minutes. Read-only: local receipt reads; `git ls-remote` and `fetch` of one branch
into the task worktree; `ssh mc@server2` with `ls`, `cat`, `jq` and `docker volume ls` only.
The read-only diagnosis in the standing authority covered this; nothing outside it was needed.
