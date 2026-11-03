# task-f123dae7: retire-temp preview `RecycleGitAuditUnknown`, read-only diagnosis

**Outcome.** The refusal comes from the temp-retirement **inventory exchange** (`c1609_prepare_exchange`), not from unpushed or dirty task worktrees. The publication audit never ran. The most likely cause is that the host trust registry `/home/mc/antiphon-server2/retirement-origins.json` is missing or does not list the origin. No script installs that file and no doc mentions it. This is strong inference, but I have not proven it: I could not reach server2 (`ssh mc@server2` fails with "Host key verification failed", and this runner's docker is the nested DinD). Running the five read-only `cat`/`ls` commands in Q2 settles it.

Nothing was changed: no code, no host, no volumes.

## Q1. What retire-temp-runner audits, and where "Unknown" comes from

How the call reaches the audit:
- `deploy-server2.ps1:1479`: the `-DryRun` preview runs `Invoke-HostCase retire-temp-runner` with `dryRun=$true`.
- `c590-real.ps1:861-863` runs that case through `Invoke-C1609RetirementCoordinator`, with `C1609_EXCHANGE=1` and `C1609_EXCHANGE_PROOF_ROOT=/home/mc/antiphon-c590`.
- On server2, `case_retire_temp_runner` (`c590-remote.sh:10251`) calls `c1008_recycle antiphon-runner-temp` (`:9188`).

The preview then runs these steps in order:
1. **Context, lock and receipt checks:** `c1008_lock`, `c1609_phase_begin`, `c1008_compose_model`, `c1008_status_proof`.
2. **Current tooling:** `c1609_resolve_current_audit` reads `audit-current.json`. On failure it refuses `RecycleGitAuditUnknown reason=current-tooling-*`, which carries a suffix.
3. **`c1609_prepare_exchange` (`c1008-retirement.sh:378-403`).** This is the only preview step that refuses a **bare** `RecycleGitAuditUnknown`:
   - It runs `c1008_audit_readonly inventory` and writes the output to `$SERVER2_ROOT/retirement/<op>/inventory-<phaseNonce>.out`. Nothing is printed to the console, which is why you saw only the `DIAGNOSIS=` line.
     - The helper is a `docker run --rm --network none --read-only --user 1654:1654` container.
     - `antiphon-runner-temp_work` is mounted read-only at `/work` and `_runner-tmp` read-only at `/tmp`, so a retired, absent container is not a problem.
     - Inventory mode runs `find` over `/work` for `.git`/`HEAD`. For each repository it checks:
       - common-dir and git-dir resolve and stay confined;
       - no `index.lock`, `MERGE_HEAD`, `CHERRY_PICK_HEAD`, `REVERT_HEAD` or `rebase-*`;
       - not shallow, no grafts;
       - promisor config;
       - bare yes/no.
     - Then, for each common dir, it lists the origin as `C1008_PRIVATE_ORIGIN`. In V2, `c1008_ls_remote_status` always returns 3, so every origin is "private" and gets proved on the desktop.
     - Any helper exit other than 0, 3 or 4 becomes a **bare** `RecycleGitAuditUnknown`.
   - It writes `inventory-<nonce>/private-origins.txt`.
   - **`c1609_export_trusted_origins` (`c1008-retirement.sh:214-230`).** If the request list is non-empty, it requires `$SERVER2_ROOT/retirement-origins.json` to be:
     - custody-clean: owned by `mc`, not group- or world-writable, one hard link, not a symlink;
     - canonical: its bytes are exactly the `jq -cS` output;
     - shaped `{"projects":{"<projectId>":{"origins":[...]}},"schema":1}`, with an entry for `C1008_PROJECT_ID`;
     - a list that contains **every** private origin as an exact string.

     If any of that fails, it returns 1, and that becomes a **bare** `RecycleGitAuditUnknown`.
   - `c1609_request_host_proof initial` (`:349`) sends a `C1609_PROOF_REQUEST` line, and the desktop coordinator answers it. The desktop does the ls-remote or fetch and builds the bundle (`c590-real.ps1:396-415`). If the exchange fails, that is also a **bare** `RecycleGitAuditUnknown`.
4. Only after step 3 does the real publication and content audit run, through `c1008_audit_readonly` into `preview-audit.out`:
   - unpublished commits give `RecycleUnpublishedWork audit check=... repo=...` (exit 3);
   - a dirty tree gives `RecycleWorktreeDirty ...` (exit 4);
   - anything else gives `RecycleGitAuditUnknown audit check=<name> status=<n> repo=<rel>`.

   These **always carry an `audit check=` suffix** (`c590-remote.sh:9290-9298`). The content blockers also have suffixes: `reason=hook` and `reason=evidence-name` (`c1008-content-audit.sh:458-465`).

Your output was the bare `DIAGNOSIS=RecycleGitAuditUnknown`, with no suffix, after 108 s (16:09:45 to 16:11:33, from the orchestrator transcript, seq 26096/26097). That rules out the unpublished, dirty and content paths and the tooling path. It leaves three possibilities, all inside `c1609_prepare_exchange`:
- **(a)** the inventory helper refused on a structural check;
- **(b)** the origins registry was missing or did not match;
- **(c)** the desktop proof exchange failed.

The only other bare site, the `jq` image-field write at `c590-remote.sh:9272`, can't realistically fail.

## Q2. Why Unknown now

**Most likely (b), the registry.**
- Every non-empty temp work volume produces at least one private origin: `repos/antiphon` → `https://github.com/michal-ciechan/Antiphon.git`. So the registry is always consulted.
- `retirement-origins.json` is referenced only in `c1008-retirement.sh:215` and in test fixtures (`RollingVolumeRecycleScriptTests.cs:2851,3257`, `c1609-wrapper-v2-cases.mjs`). There is no installer and no mention in `docs/`.
- The orchestrator transcript never mentions creating it.
- It arrived with CARD-1609 `85776c1c8` (2026-10-31), which is in the deployed `919d8d42f`. Since then, no retire-temp preview in the transcript has reached `C1609_RETIREMENT state=Ready`.

**(a) is unlikely.** I ran the exact inventory helper body read-only, as uid 1654, against this runner's own `/work`. That is the main `server2` work volume, same layout, 241 repository entries. It exits **0**:
- it lists `repos/antiphon` with the `.git` origin;
- it lists two nested `JournalCustodySurvivesInterleaving` failfirst fixture checkouts under `worktrees/task-*/.antiphon/failfirst/...`, with the origin `https://github.com/michal-ciechan/Antiphon` (no `.git`). If temp holds such fixtures, the registry needs that string too.
- It found no executable hooks and no stray `HEAD`/`FETCH_HEAD` files.
- I saw no uid, safe.directory or read-only-mount failure: the volumes are 1654-owned, and the helper uses `GIT_CONFIG_GLOBAL=/dev/null`.

**(c) is possible only if the registry passed.**

**Settle it read-only on server2, as `mc`:**
```
ls -l /home/mc/antiphon-server2/retirement-origins.json
op=$(ls -t /home/mc/antiphon-server2/retirement | head -1); echo $op
ls -la /home/mc/antiphon-server2/retirement/$op /home/mc/antiphon-server2/retirement/$op/inventory-*/
grep -v '^entry ' /home/mc/antiphon-server2/retirement/$op/inventory-*.out
docker system df -v | grep antiphon-runner-temp_
```
How to read the results:
- An `audit check=...` line in `inventory-*.out` means (a). The line names the check and the repository.
- `private-origins.txt` present but no `trusted-origins.json` means **(b)**.
- `trusted-origins.json` and `c590-result.json` both present means (c). Then read the desktop receipt `C:\src\Antiphon\.antiphon\rolling-server2\<runId>\...\remote-output.txt` (`transport-exit`/`failure-line`), `coordinator-exchanges.jsonl` and the `exchange-*` directories.

**Unpushed work:** I can't list it, for two reasons. I have no host access, and the publication audit, the step that finds unpushed commits, never ran because inventory refuses first. The fixed preview reports it per repository as `RecycleUnpublishedWork audit check=... repo=worktrees/task-<id>`.

## Q3. What the operator should do

**Leaving the volumes is safe.**
- The temp runner is retired, offline and has no container, so the four volumes are inert. Their size is unknown; get it with `docker system df -v` above.
- They **do** block the next rollout. At gate 1, `deploy-temp` calls `Assert-TempRetirementClearable` (`deploy-server2.ps1:1203-1215`), which throws `TempVolumesRetained <name>` while any `antiphon-runner-temp_*` volume exists. A retirement is eventually required.

**Sequence, all operator or human:**
1. **Confirm (b)** with the read-only commands above.
2. **Create the trust registry.** This is a security trust decision, so it is the operator's.
   - Get `<projectId>` from the `RECYCLE_PROJECT id=` line of `pwsh -NoProfile -File scripts/deploy-server2.ps1 -Rolling -Sha <sha> -Phase check-census`, which is read-only. It must equal `C1008_PROJECT_ID`.
   - Then, on server2 as `mc`:
     ```
     umask 077; jq -ncS --arg p <projectId> '{schema:1,projects:{($p):{origins:["https://github.com/michal-ciechan/Antiphon.git"]}}}' > /home/mc/antiphon-server2/retirement-origins.json
     ```
   - Add `"https://github.com/michal-ciechan/Antiphon"` if `inventory-*.out` lists that origin.
3. **Run a fresh preview, without `| tail`:**
   ```
   pwsh -NoProfile -File scripts/deploy-server2.ps1 -Rolling -Sha 919d8d42f14d1b11ae5dcba8dafd1e1b2a8d90cd -Phase retire-temp -DryRun
   ```
   - Ready prints the `C1008_PREVIEW remove=` lines and then `C1609_RETIREMENT state=Ready operation=<op>`.
   - The desktop needs GitHub access, because it proves each origin (600 s network budget).
   - If it prints `RecycleUnpublishedWork ... repo=worktrees/task-<id>`, push that task's branch through its owner and run the preview again.
4. **Apply within 900 s of Ready:**
   ```
   $env:ANTIPHON_VOLUME_RETIREMENT_APPLY='enabled-<op>'
   pwsh -NoProfile -File scripts/deploy-server2.ps1 -Rolling -Sha 919d8d42f14d1b11ae5dcba8dafd1e1b2a8d90cd -Phase retire-temp -RetirementProof <op>
   ```
   Then unset the variable (`docs/docker-stack.md:130`).

## Structural defects to card

- **D-1.** `retirement-origins.json` is a required host prerequisite for every non-empty V2 temp retirement, but nothing installs it and nothing documents it. As things stand, every retire-temp preview will refuse until someone writes it by hand.
- **D-2.** `c1609_prepare_exchange` gives three different causes (inventory refusal, registry mismatch, exchange failure) the same bare `RecycleGitAuditUnknown`. It needs a `reason=` suffix. The `inventory-*.out` detail is never surfaced on the console.

Neither is a one-token fix, so I made no code change.
