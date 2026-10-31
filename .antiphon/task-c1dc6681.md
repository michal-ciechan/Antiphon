# CARD-1609 round 13 Final Review (apply gate) — task c1dc6681

Outcome: **2 defects found, so this is not clean.** The token gate does keep volume deletion off by default. Every token variant refused, and every gate mutant went red. But the gate is placed late in `c1008_recycle`. It runs after the containers are stopped and removed, after the journal is committed and after the Ready preview is consumed. Because of that, the ordinary `redeploy-old` step cannot finish without authorising deletion, and the documented second-rollout command cannot apply a preview that a gate-off apply has already consumed.

Reviewed source: `feat/card-task-1fa8a516` tip `33ab2cffb9cb04cc3aa2f735982dabedbd1d4488` (server2/Linux). Synthetic fixtures only. No deploy, retire or docker command was run against a real host.

## Defects

### D-1 (blocker): a gate-off host recycle stops and removes the old runner before it refuses, and leaves no recovery that keeps the volumes

- **Where:** `scripts/c590-remote.sh:9462`. `c1609_apply_gate` runs after `docker stop` at 9416, `docker rm` at 9428 and the journal saves through `phase=removing`. The caller is `scripts/deploy-server2.ps1:1383-1386`: `redeploy-old` always passes a non-dry-run recycle context to `deploy-parent` and mints a fresh `c1008<guid>`.
- **Failure:** standard gate 6 (`-Phase redeploy-old`) runs with no token. The operator cannot set one in advance, because the operation id is minted inside the call and `-DryRun` mints a different id. The run then:
  1. stops the drained `antiphon-runner` session-runner and removes it;
  2. removes state-init;
  3. saves the journal as `phase=removing outcome=refused ownedRemoved=true`;
  4. exits 2 with `apply-gate-off`.

  `deploy-parent` never recreates the runner, so server2 main stays down. The only scripted continuation is `-ResumeRecycle <op>`, which hits the same gate. Getting the runner back through the scripts therefore requires `enabled-<op>`, and that deletes the host `work`, `runner-tmp` and `dind-data` volumes. This contradicts "production deletes disabled" and makes the preview-only first rollout fail at step 6.
- **Witness (reviewer probe P1, scratch only):** host `deploy-parent` with `C1609_FIXTURE_APPLY_GATE=off` gave exit 2 `DIAGNOSIS=apply-gate-off`. The fake-docker trace shows `stop --time 90 -- 1111…`, `rm -- 1111…` and `rm -- 2222…` before the refusal, and the journal reads `"phase":"removing","outcome":"refused","ownedRemoved":true`.
- **Missing coverage:** CP-17's host case asserts only "no volume rm". Every other `C1008HostFixture` test turns the gate on by default (`RollingVolumeRecycleScriptTests.cs:3379-3380`), and the PowerShell wrapper tests stub the host case. So no test covers `redeploy-old` under the production default.
- **Fix:**
  1. Evaluate the gate for every non-dry-run apply and resume right after context validation in `c1008_recycle`. That is before `c1008_lock`, the journal commit, any container stop/rm and `c1609_require_ready_preview`.
  2. In `deploy-server2.ps1` `redeploy-old`, do not request a recycle unless the operator opted in. Run `deploy-parent` without the C1008 context, which is a plain upgrade that keeps the volumes. Alternatively, refuse before the host case with a named reason.
  3. Document the gate in the gate 6 row of `docs/docker-stack.md`.
  4. Add a witness asserting zero `stop`/`rm` and no journal for the gate-off host apply and resume.

### D-2 (major): a gate-off temp apply consumes the Ready preview, so the documented `-RetirementProof <operation-id>` apply can never use it

- **Where:** `scripts/c590-remote.sh:9456`. `c1609_require_ready_preview` runs in consume mode before the gate. At `scripts/c1008-retirement.sh:600` it writes `consumed=true`, `state="Applying"` and prints `C1609_RETIREMENT state=Applying`. The journal `$SERVER2_ROOT/recycle/<op>.json` is already saved.
- **Failure:** gate 9 itself says "a following apply without the matching token refuses `apply-gate-off`". After that refusal:
  - `-RetirementProof <op>` with `enabled-<op>` fails. The wrapper throws `TempRetirementProofUnknown` because the proof state is no longer `Ready` (`deploy-server2.ps1:1448`), and the host refuses `RecycleResumeMismatch` because the journal exists.
  - The only working path is `-ResumeRecycle <op>` with the token, and that path is not documented.
  - The refused run also prints `state=Applying`, which is misleading.
- **Witness (reviewer probe P6, scratch only):** preview, then gate-off apply gave exit 2 with output `C1609_RETIREMENT state=Applying operation=c1008…01` then `DIAGNOSIS=apply-gate-off`. The receipt was `consumed=true state=Applying`. A fresh apply with the token then gave exit 2 `RecycleResumeMismatch` and removed 0 volumes. Resume with the token gave exit 0 and removed 4.
- **Fix:** the same early gate placement as D-1, so a refused apply consumes nothing and writes no journal. Add a test that runs preview → gate-off apply → token apply of the same operation and expects exit 0 with 4 removed. In gate 9, state that the Ready preview expires after 900 s, so the second rollout needs a fresh `-DryRun` and then an apply within 15 minutes.

## What holds

- **Gate default and token variants:** these all refused with exit 2, `apply-gate-off` and zero volume rm: unset, `enabled`, `1`, `true` and a wrong operation id (CP-17), plus the reviewer probes for a leading space, a trailing space, a trailing newline, `Enabled-`, an uppercase `C1008` and the literal glob `enabled-*` (P2, 7 cases).
- **Comparison and parsing:** the comparison is an exact bash `!=` against `enabled-${C1008_OPERATION}`, after a regex check of the operation id. It fails closed. Constant-time comparison is irrelevant because the token is a confirmation phrase anyone can derive from the operation id, not a secret.
- **Token never echoed:** probe P3 found no `enabled-c1008` string in apply stdout or in any fixture evidence file. The bridge does pass the token on the ssh command line, which is advisory only.
- **TOCTOU:** temp apply re-derives the volumes:
  - `c1609_require_ready_preview` compares live fingerprints to the receipt (`c1008-retirement.sh:576`);
  - `c1609_recheck_target` rechecks each target immediately before its rm;
  - `-RetirementProof` binds `C1008_EXPECTED_PROOF_DIGEST`.

  The host path compares each volume against its journal original before rm.
- **Protected names:** `C1008_TARGETS` is built from the constants `HOST_PROJECT` and `TEMP_PROJECT`. Temp receipts, attestations and close records require exactly the four `antiphon-runner-temp_*` names (`c1008-retirement.sh:186,950,1043`). No input can name `antiphon_pgdata` or `antiphon-runner_runner-state`. Probe P5 showed a valid host token removed only `work`, `runner-tmp` and `dind-data`. Host `runner-state` and the caches are verified as preserved only after the deletes; see A-3.
- **Other deletion paths reachable from scripts:**
  - These are all scoped to synthetic project prefixes and are pre-existing: `compose_child down -v` (`c604${RUN}`), `retire_c590_leftovers` (`c590<12hex>`), and the c849 fixture `down -v` calls (`c849${RUN}*`).
  - `runner-cache-reset` and `runner-cache-prune` wipe cache contents with `find … rm -rf`. That data can be regenerated, and those paths are outside the retirement gate.
  - `drain-temp` runs `docker rm` without `-v`.
  - `deploy-temp` deletes nothing; it refuses `TempVolumesRetained`.
  - No volume or prune deletes exist in `server/` or `src/`.

## Mutants (scratch worktree at the tip, one build, script-only mutants, each restored)

| Mutant | Test | Result |
|---|---|---|
| M1: gate default flipped on (`${…:-enabled-$op}`) | CP-17 `Resume_refuses…` | red (blocked.Exit 0≠2) |
| M2: token compare skipped | CP-17 `Manual_apply_refuses…` | red |
| M3: operation-id binding skipped (`enabled-c1008*`) | CP-17 `Scripted_apply_refuses…` | red |
| M6: host left ungated (gate moved into the temp branch) | CP-17 `Scripted_apply_refuses…` | red (host exit 0≠2) |
| M4: `require_ready_preview` returns 0 | J1 `Preview_receipt_is_required_and_consumed` | **survived**. The earlier receipt-file existence check also refuses `preview-required`. |
| M4b: `recheck_target` returns 0 | J3 `Volume_incarnation…` and J6 `Every_delete_rechecks…` | **survived**. Redundant with the `require_ready_preview` fingerprint compare. |
| M4c: `EXPECTED_PROOF_DIGEST` compare removed | J2 `Receipt_shape_custody_and_digest_are_strict` | red |
| M4d: both fingerprint compares removed | J3 `Volume_incarnation_and_operation_are_bound` | red |
| M5: host `runner-state` added to targets | probe P5 | red: deleted before the post-check (A-3) |

All restored scripts are byte-identical to the tip (`git checkout`). The CP rows below ran green at the tip in the clean worktree.

## Sweep

**Checkpoint run `20261031-151454-a64a`** at `33ab2cffb…`: `--rows CP-8,CP-9,CP-16,CP-17 --row-timeout 30m --total-timeout 95m`, verdict GREEN, exit 0, wall 57m18s, unlisted none, outputs deleted. Validate printed `CHECKPOINT SOURCE VALID source=33ab2cffb9cb04cc3aa2f735982dabedbd1d4488 rows=4`.

| Row | Executed | Passed | Failed | Skipped | Build | Source |
|---|---|---|---|---|---|---|
| CP-8 | 11 | 11 | 0 | 0 | ok | clean, buildSource=verified |
| CP-9 | 22 | 22 | 0 | 0 | reused | clean, buildSource=verified |
| CP-16 | 23 | 23 | 0 | 0 | reused | clean, buildSource=verified |
| CP-17 | 4 | 4 | 0 | 0 | reused | clean, buildSource=verified |

**Not run:**
- CP-3/4/5/6/7/10/15 (Linux) were not re-run.
- CP-11/12/13 are native Windows.
- CP-13's doc-pin file changed in this round.
- CP-10 N3 is deferred.

So `ordinaryScopeCompleted` is Interim.

**Other checks:**
- Land graph `scripts/audit-land-verification-graph.ps1 -Check` (no `-Update`): exit 0, `digest=20DE49F6B177E5A238F20D4F8C7F33EAEE6B511A9163CDB902D9136EF06DECB6 count=66`, `phase=pass2`, `approved`.
- Evidence diff: base `98fc3ceb6`..HEAD gave `commits=151 entries=44 violations=0`; `6d9bdc906`..HEAD gave `commits=10 entries=1 violations=0`.
- Merge onto `origin/master` `2051d3271`: clean, tree `6343bb9ab`.
- Merge onto `origin/rc/2026-10-27` `e0d49d221`: **not clean**. The whole-branch merge-tree has 213 conflicting files. A chained cherry-pick of the 151 branch commits conflicts at commit #2, `678655489` (`scripts/fixtures/c1008-fake-docker.sh`). rc carries no CARD-1609 and no `scripts/c1008-retirement.sh`, so an rc port needs its own port task with prerequisites.

## Advisories

- **A-1:** the worker's report says "the private authenticated origin is still the only admission". Commit `c4e76c8d` actually changes the 404, DNS and timeout cases from refusal to admission when a matching v2 proof exists. That matches plan D-6 and lines 932-937 (proof for every origin), but the report wording is wrong.
- **A-2:** gate 9 says the preview "reports … its operation id". In the code paths I read, the dry-run stdout prints `would delete:` and `state=Ready` but no operation id. The id sits only in the wrapper's evidence manifest. Print it, or fix the doc.
- **A-3:** host deletion has no pre-delete check that the targets do not overlap the preserved set (`runner-state` and the caches). Protection rests only on the constant target list; the preserved compare runs after the deletes. Consider an explicit pre-delete refusal.
- **A-4:** `c1609_preview_would_delete` treats a failed `c1008_volume` as absent (`|| facts=null`). This affects the display only, because apply refuses `RecycleVolumeCensusUnknown`.
- **A-5:** the token never expires for an in-flight operation. A later `-ResumeRecycle <op>` with the same export deletes. That is by design, but worth stating in the runbook.
- **A-6:** M4 and M4b survive because several guards check the same thing. No single row witnesses the distinct between-deletes fingerprint recheck.
