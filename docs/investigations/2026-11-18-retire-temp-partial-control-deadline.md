# Retire-temp attempt 2 ended PartialRefused on a 5 s control deadline (2026-11-18)

Read-only Debug task 6243c535. Source read: main checkout `C:\src\Antiphon` at
`e229fb4d3fb859ee16f82bc553b1d4a5cfdccce4` (= `origin/master` and the rc/2026-11-27 rollout SHA).
Run `c7276c73a1812b76`, operation `c100884f1e02a03b541118d329379ce16e480`. Nothing was run,
resumed, deleted or restarted; server2 was read with `jq`, `ls`, `docker volume ls`,
`docker system df -v`, `sudo -n du/find` and `df` only.

## A. What was removed, what remains, and why

Host journal `/home/mc/antiphon-server2/recycle/c100884f1e02a03b541118d329379ce16e480.json`:
`outcome=partial`, `phase=removing`, `diagnosis="RecycleGitAuditUnknown reason=proof-stale"`.

| Volume (removal order) | Journal outcome | Retirement receipt |
|---|---|---|
| `antiphon-runner-temp_runner-state` | removed | intent + effect `removed` |
| `antiphon-runner-temp_dind-data` | removed | intent + effect `removed` |
| `antiphon-runner-temp_runner-tmp` | removed | intent + effect `removed` |
| `antiphon-runner-temp_work` | **pending** (still present) | **no intent, no effect** |

Protected receipt `/home/mc/antiphon-server2/retirement/<op>/receipt.json`: `schema=2`,
`state=PartialRefused`, `consumed=true`, `owner=null`. `lifecycle.json`: schema 2, `phase=Apply`,
`active=false`, `complete=true`, `readyPublished=true`. `docker volume ls` shows only
`antiphon-runner-temp_work`. The local copy `retire-temp-runner/recycle.json` is a pre-final
snapshot (`outcome=refused`); the host journal is authoritative.

### Timeline (from the host `exchange-*.json` / `control-*.json` mtimes and the desktop `coordinator-exchanges.jsonl`)

Each temp target goes through `c1609_publication_gate <name>` (`scripts/c1008-retirement.sh:512`).
That gate first asks the desktop to renew the host proof (`C1609_PROOF_REQUEST renew`, a
113.7 MB git bundle plus a 6,826-line proof file, 70-132 s of network per renewal). It then
sends a `C1609_CONTROL_REQUEST <purpose>`.

| Gate | Renewal exchange (network ms) | Control persisted |
|---|---|---|
| initial (admission) | e37303fc (70,958) | - |
| apply | d841b8d6 (76,527) | 05:39:57 `apply` |
| runner-state | 79efbc00 (90,626) | 05:42:45 |
| dind-data | 715d8610 (75,581) | 05:45:43 |
| runner-tmp | 61a3ffc4 (132,290) | 05:49:19 |
| **work** | f2da0fe7 (95,832), host record 05:51:34 | **none: failed** |

Desktop receipt `remote-output.txt`: `transport-exit=2 failure-line=438
failure-script=c590-real.ps1 failure-reason=control-deadline`.

### The control deadline: what it is and where it is set

The deadline is **5000 ms, hard-coded in the desktop coordinator `scripts/c590-real.ps1`**, not in
`deploy-server2.ps1`, `retire-runner-volumes.ps1` or the host case:

- `c590-real.ps1:427-438`: for each `C1609_CONTROL_REQUEST`, a `$controlWatch` stopwatch runs.
  For every private-origin row it calls `Assert-C1609LiveOriginProof` with
  `NetworkBudgetMs = min(5000 - elapsed, 600000 - networkMs - elapsed)`. Afterwards
  `if($controlWatch.ElapsedMilliseconds -gt 5000){throw 'control-deadline'}`.
- `c1609-host-proof.ps1:389-426` (`Assert-C1609LiveOriginProof`), all inside that window:
  - reads the 518 KB proof file;
  - `Get-FileHash` of the 113.7 MB `.bundle`;
  - parses the expected advertisement (6,815 refs) with a per-line regex loop;
  - runs `git ls-remote <origin> refs/heads/* refs/tags/* refs/antiphon/salvage/*` with
    `-TimeoutMs` set to the remaining budget, then parses that advertisement too;
  - deletes its scratch directory.
- On the host side, `c1609_publication_gate` waits `read -t 10` for the response
  (`c1008-retirement.sh:562`). The coordinator threw, killed the ssh process, the read failed, and
  `c1008_refuse 'RecycleGitAuditUnknown reason=proof-stale'` ran with `C1609_APPLY_ACTIVE=1`.
  That gave PartialRefused and journal `partial` (`c590-remote.sh:6098-6110`, `:9659`).

Line 438 is the post-check, so `Assert-C1609LiveOriginProof` itself returned without throwing:
no ls-remote timeout and no withdrawn ref. The whole control step simply took longer than
5000 ms. **Which sub-step overran is UNOBSERVED**, because no per-control elapsed time is
recorded anywhere (`coordinator-exchanges.jsonl` logs proof exchanges only). Desktop
measurements taken now (06:00Z, same origin, same bundle):

- `git ls-remote`: 1.03-1.15 s (6,821 refs);
- `Get-FileHash` of the bundle: 0.90-1.43 s.

The two PowerShell advertisement parses were not measured: an attempt to time the full assertion
hung in a non-interactive run and was stopped. The margin under 5 s is therefore thin, and
contention on the desktop or a slow GitHub response is enough to miss it. Four of the five
controls in this apply (plus the preview's `ready`) passed with identical inputs, so this is
**transient timing, not a deterministic failure**.

**Removal never started on `work`.** The failure came before `c1609_target_intent` and
`docker volume rm`. Volume size did not cause the 20-minute duration or the failure. The
duration is five serial proof renewals (541.8 s of network) plus host census/audit between them.

### Size of the remaining volume

`sudo -n du -sh`: **2.5 G**. `find | wc -l`: **18,827** entries. `docker system df -v`: 2.534 GB,
0 links. It was created 2026-11-18T01:30:21Z by this rollout's `deploy-temp`; it is not an old
temp work tree.

## B. Documented recovery: Resume, not Close

- **Close is not available.** `docs/docker-stack.md` (gate 1/9 text, l.139-151, l.745-758) says
  Close "requires ... typed absence of all four volumes" and "records an audited journal
  transition after all four targets are absent; it neither removes a volume". `work` is present,
  so Close would refuse.
- **Resume is the documented recovery** (docs l.139-142: "Recovery is Resume of that operation";
  l.737-744). The receipt meets `c1609_require_resume_phase` (`c1008-retirement.sh:1024-1050`):
  `consumed=true`, `owner=null`, `state=PartialRefused` is an accepted state, lifecycle schema 2
  `phase=Apply`, `readyPublished=true`, and the lifecycle is inactive and complete (dead owner).
  Resume skips the fresh-preview requirement (`c590-remote.sh:9641-9645`), reuses the original
  operation and its incarnation guards, and only `work` remains to delete.

### Exact next command (orchestrator, main checkout `C:\src\Antiphon`, PowerShell)

```powershell
$env:ANTIPHON_VOLUME_RETIREMENT_APPLY = 'enabled-c100884f1e02a03b541118d329379ce16e480'
pwsh -NoProfile -File scripts/deploy-server2.ps1 -Rolling -Sha e229fb4d3fb859ee16f82bc553b1d4a5cfdccce4 -Phase retire-temp -ResumeRecycle c100884f1e02a03b541118d329379ce16e480
```

Preconditions and authority:

- **The same apply token is required.** `c1609_apply_gate` (`c590-remote.sh:9313-9321`): "Resume
  repeats the same id and needs the same token". Without it the run fails with `apply-gate-off`
  before any effect.
- **Operator credential.** `deploy-server2.ps1:61` reads `Get-RunnerOperatorToken` itself on the
  control host. No `-AuthorityFile` is needed on this path; that is for
  `retire-runner-volumes.ps1 -Close`/manual. No `-DryRun` and no new preview: `-DryRun` with
  `-ResumeRecycle` throws `RecycleContextInvalid` (l.36).
- **The same SHA and the same checkout**, with temp still retired/offline, the temp project
  absent, main accepting, and zero bound work on `server2-temp`. The wrapper rechecks all of these.
- **The live proof window still applies.** Resume runs the same coordinated flow: an initial
  proof exchange, then `apply` and `work` gates, each with a renewal of about 70-130 s and a 5 s
  control. The `withdrawn-ref` check runs at every control.
  - A move or delete of any existing origin ref between a renewal and its control fails the gate:
    a push to an existing branch, a force-push, or branch deletion by a land. Creating a new ref
    is harmless.
  - Hold lands, rc pushes and branch deletions on `michal-ciechan/Antiphon` for about 10 minutes
    while Resume runs.
  - Avoid heavy desktop CPU/disk load if possible, to keep the 5 s control margin.
- Network budget: Resume needs about three renewals, well inside the 600 s per-run coordinator
  budget (`c590-real.ps1:375,403`).
- Expected success: `work` gets intent, `removed` effect, receipt `Completed`, journal `completed`,
  and `docker volume ls` shows no `antiphon-runner-temp_*`.
- If Resume refuses again with `control-deadline`: the journal stays PartialRefused and `work`
  stays present. It is safe to repeat the same Resume. If it repeats, the defect below needs a Code
  fix first.
- If Resume prints `recovery=-Close`, do not Close while `work` exists: Close would refuse.

Under the gate-9 rule ("any other `RecycleGitAuditUnknown` gets one fresh preview and apply, then a
Debug task"), the fresh preview+apply has been spent and this note is the Debug. **Next is Resume,
not a third new operation.**

## C. What the partial state blocks now

- The standing `server2` runner is unaffected: its state and caches are preserved and their
  identities checked in the journal's `preserved`.
- The temp project stays absent.
- The next `deploy-temp` refuses `TempVolumesRetained antiphon-runner-temp_work`. Even if the
  volume were gone, the unresolved PartialRefused journal refuses (`c590-remote.sh:9946`,
  `TempRetirementUnresolved`).
- Nothing else is blocked until the next rollout. The rollout's gate 9 is incomplete, and per the
  docs `-Case Retired` must not be run as if gate 9 passed.

## D. Disk

The remaining volume holds **2.5 GB**. server2 root is **64 % used (427 G of 705 G, 250 G free)**
at 05:55Z, not 74 %. **Not urgent.** Before the apply, `diskBefore` was 63 %.

## E. Code defect (card text to file)

**Title:** Retire-temp control step has a fixed 5 s wall-clock window that includes hashing a
114 MB bundle and parsing 2x6.8k refs; a miss strands the apply as PartialRefused.

**Body:**
- Facts: run c7276c73a1812b76, operation c100884f1e02a03b541118d329379ce16e480 at e229fb4d3. The
  `work` gate control failed `control-deadline` (`scripts/c590-real.ps1:438`) after four of five
  controls passed with identical inputs.
- What the window covers: `Assert-C1609LiveOriginProof` (`scripts/c1609-host-proof.ps1:389`)
  re-hashes the proof bundle, re-parses the stored advertisement, runs ls-remote and parses its
  output, all inside the 5000 ms `$controlWatch`.
- Fix shape:
  1. Verify the bundle hash and parse the expected advertisement once, when the proof exchange
     returns, outside the control window. Keep only ls-remote plus the set compare inside it.
  2. Record per-control elapsed time and sub-step timings in `coordinator-exchanges.jsonl`, so a
     miss is attributable.
  3. Consider one bounded retry of the control on deadline only (never on `withdrawn-ref`), since
     the host's `read -t 10` leaves room.
  4. Consider whether every target needs a full 70-130 s proof renewal. Five serial renewals make a
     20-minute apply, widening the withdrawn-ref exposure and leaving only about 46 s of the
     600 s network budget. One more slow renewal would have hit `network-deadline` instead.
- Acceptance: a fixture with a 100+ MB bundle and 7k refs completes the control well under the
  window on a loaded host; a deadline miss records its sub-step timings.

## Risks

- Resume can fail again on the same transient or on a `withdrawn-ref` if a land or push moves a
  ref. Neither causes damage: PartialRefused stays, `work` stays, and Resume can be repeated.
- Until resolved, the next rollout's `deploy-temp` is blocked (C). Disk impact is 2.5 GB.
- Do not hand-delete `work` or edit the receipt. The docs fallback still ends with an audited
  Close, and a manual delete without an intent becomes `unattributed-absence`.

--- next stage ---
next: decide
handoff: Orchestrator runs the Resume above in a quiet push window (or files the card and defers to the next rollout); file the 5 s control-window card.
artifact: docs/investigations/2026-11-18-retire-temp-partial-control-deadline.md
