# CARD-1105 ResumeRecycle stopped at RecycleResumeMismatch

Diagnosis only. The resume was not run again. Read-only SSH inspected the host journal, `stack.env` tag lines, container and volume identity, the deploy checkout HEAD, and whether temp's Grok process is still up. No `docker` stop, rm, start, compose, volume change, or deploy phase was run for this read.

Evidence for the resume: `C:\src\Antiphon\.antiphon\rolling-server2\c727d186a0fda983` (`deploy-parent.manifest.json`: `sourceSha=8892b7b759d96b3b5d6fa20518caf2c79c6be81a`, `operationId=c10084fb9431d31da48edb781dd54e5ebebf7`, `resume=true`). `deploy-parent/c590-result.json` and the run-level copy are both `{"accepted":false,"diagnosis":"RecycleResumeMismatch","exit":2}`. Orchestrator log `rollout6.log` records the same diagnosis and `HostCaseFailed deploy-parent exit=2`. Wrapper `host-jq-redeploy-old.json` is `mode=check`, `outcome=existing`, `installed=false`, `observedAtUtc=2026-10-09T12:32:45.3950430Z`.

The journal this resume tried to continue is the one written by the earlier `HostComposeFailed` run `c727676499a853c6` (same operation id, `resume=false`, same SHA). That failure is `docs/investigations/2026-10-09-card-1105-redeploy-compose-failed.md` on `feat/card-task-ab34e8bd` (`21a5494430de7c578db00207f8ad159f453a5e0f`). Both the desktop checkout and this worktree are `8892b7b759d96b3b5d6fa20518caf2c79c6be81a` (`fix(CARD-1105): pass the git-audit receipt to jq through a file`). `scripts/c590-remote.sh` matches that commit.

## Which check fired

`c1008_recycle` hashes the rendered Compose model before it reads the resume body, then requires the journal's `composeDigest` to equal that hash.

`scripts/c590-remote.sh` 5817-5833:

- 5817: `model="$(c1008_compose_model)"`.
- 5824: `digest` is `sha256sum` of that model.
- 5829: the journal file must exist and not be a symlink.
- 5831-5833: `.schema==1`, `.sourceSha==$SHA`, `.operationId`, `.project`, `.context=="default"`, `.dryRun==false`, and `.composeDigest==$digest`, or `RecycleResumeMismatch`.

That is the check that fired. The two sides are:

| Side | Value |
|---|---|
| Journal `.composeDigest` | `44e86c5b57ec21cbc7f293504e2bfecdee6768d0b4f0d938c51857c32cc84a67` |
| Resume render | sha256 of `c1008_compose_model` after `stack.env` `SOURCE_SHA12` had become `8892b7b759d9`. The number was not stored: the refusal is before `c1008_save`, and `C1008_ACTIVE` is still 0 so `c1008_refuse` does not write the journal either. |

The input that changed is `SOURCE_SHA12`, not the Compose file and not the audit.

`compose_host` (941-957) sets `SOURCE_SHA12="$(deployed_sha12)"` and `SOURCE_REVISION="$SHA"`. `deployed_sha12` (929-938) reads `SOURCE_SHA12` from `stack.env`. `c1008_compose_model` (4036-4120) runs `compose_host config` and hashes the whole rendered model, including image tags `antiphon-server2/server:${SOURCE_SHA12}` and `antiphon-server2/session-testing:${SOURCE_SHA12}` (`docker-compose.server2-runner.yml` 18 and 42).

`case_deploy_parent` calls `c1008_recycle` first (6003-6004) and only then rewrites `stack.env` to `SOURCE_SHA12=${SHA:0:12}` (6013-6027), before `compose up` (6046). The failed run got past that rewrite: `command.log` shows the new volumes, the seed line `state-init owned uid=1654`, the checkout clone, and then `chown: changing ownership of '/runner-state/grok/worktrees.db-wal'`. So the journal digest was taken while `SOURCE_SHA12` was still the previous deployment's tag, and the resume hashed the model after the rewrite.

Live read at this diagnosis, journal `/home/mc/antiphon-server2/recycle/c10084fb9431d31da48edb781dd54e5ebebf7.json`:

- mtime `2026-10-09 11:10:42.843207385 +0000`, size 198185. The resume's host-jq observation is `2026-10-09T12:32:45Z`, so the resume did not rewrite the file.
- `phase=recreating` `outcome=verificationPending` `ownedRemoved=true`
- `sourceSha=8892b7b759d96b3b5d6fa20518caf2c79c6be81a`
- `previousSha=4358939ecd85d6e7ff0941f970879499cb930e3d`
- `composeDigest=44e86c5b57ec21cbc7f293504e2bfecdee6768d0b4f0d938c51857c32cc84a67`
- `stack.env` now: `SOURCE_REVISION=8892b7b759d96b3b5d6fa20518caf2c79c6be81a`, `SOURCE_SHA12=8892b7b759d9`, `BUILD_SLOTS_SHA12=a8b4e9e5a071`

`previousSha`'s first 12 characters are `4358939ecd85`. The journal `generation.imageTag` from the failed run is `antiphon-server2/session-testing:4358939ecd85`. That is the tag `deployed_sha12` would have supplied on the first hash. `BUILD_SLOTS_SHA12` is still `a8b4e9e5a071` (the running `antiphon-runner-build-slots-1` image). `compose_host` forces `SOURCE_REVISION=$SHA` on both calls, so the build-arg does not differ. The image tags that differ are `antiphon-server2/server` and `antiphon-server2/session-testing`: `4358939ecd85` at the save, `8892b7b759d9` at the resume.

The resume case directory shows the same cut. `deploy-parent/compose/8892b7b759d9/` exists, so `c1008_compose_source` ran inside `c1008_compose_model`. The diagnosis is not `RecycleComposeMismatch`, so the uninterpolated file compare at 5820-5821 passed. Absent, which later steps would have written:

- `deploy-parent/census-deploy-parent-server2.json` (`c1008_tasks` inside `c1008_status_proof`, 5837, which is after 5833)
- `deploy-parent/generation.json` and `previous-model.json` (`c1008_bind_generation`, 5842)
- `deploy-parent/recycle.json` (`c1008_save`)

`deploy-parent/command.log` is only the `GithubTokenAbsent` warning from `ensure_runner_github_token_dir` (1195), which runs in `ensure_runner_boot_files` before `c1008_recycle`. There is no `build.log` and no compose log.

### Not the audit store

`8892b7b75` stores the git audit with `--rawfile` (`c1008_write_private` of `audit.txt`, then `jq --rawfile`, 5929-5934) and spills resume records with `--slurpfile`. The failed run was that same commit, and it did store `.audit`. The journal field is a string of 178141 bytes, no trailing newline, prefix `entry repo=`. It is byte-identical to `c727676499a853c6/deploy-parent/audit.txt`. There is no `.auditFailure`.

The resume compare of that field is 5925-5927, and only when resume is still inside the removal path. Phase `recreating` returns at 5858-5871, after status proof and `c1008_bind_generation`, and before 5927. This resume never got there. The audit shape did not participate.

### Not the recreated generation

The phase-`recreating` container and volume compare is 5858-5868, and again inside `c1008_status_proof` when `runnerSessions` is null (4386-4388). Both are after the digest compare. Live identity still matches the journal's `.recreated` set from the failed run:

| Object | Now | Journal |
|---|---|---|
| `392f011e1f2b` `antiphon-runner-session-runner-1` | Created, image `antiphon-server2/session-testing:8892b7b759d9` | `.recreated.owned` session-runner, status `created` |
| `23c97aac84b7` `antiphon-runner-state-init-1` | Exited (1), image `antiphon-server2/server:8892b7b759d9` | `.recreated.owned` state-init, status `exited` |
| `4cabb8afc676` `antiphon-runner-build-slots-1` | Up 9 days (healthy), `session-testing:a8b4e9e5a071` | not in the owned set |
| `antiphon-runner_work`, `_runner-tmp`, `_dind-data` | `CreatedAt` `2026-10-09T11:09:57Z` | `.recreated.volumes` same timestamps |
| `antiphon-runner_runner-state` | `CreatedAt` `2026-09-22T19:10:50Z` | preserved, not removed |

`GET http://127.0.0.1:17202/api/session-runners/server2/status` at this read: `buildVersion=4358939ecd85d6e7ff0941f970879499cb930e3d`, `available=false`, `dispatchEligible=false`, `draining=true`, `redirectTo=server2-temp`, `acceptingNewWork=false`, `retiredAt=null`, `sessions=0`, `queuedTasks=0`, `runnerSessions=null`. `server2-temp`: `buildVersion=d985af05b5ace2f13ec3f0d886c15992e9c13c3f`, available and accepting, `sessions=4`, `runnerSessions=4`, `queuedTasks=0`. Same shape as the 11:21Z read in the earlier investigation.

## Host state after the resume

Unchanged where the recycle is concerned.

- Journal phase, outcome, digest, source SHA, and previous SHA match the 11:10:42 file. Size matches the evidence `recycle.json` (198185).
- Main containers and the four volume `CreatedAt` values match the post-failure set. Temp runner `927bb1ad3d60` is still `Up 3 days (healthy)` on `session-testing:d985af05b5ac`.
- Wrapper census `snapshot` for `c727d186a0fda983` equals `c727676499a853c6`. Both refusals are null. Host-jq did not install jq.
- Host deploy checkout `/work/repos/antiphon` is `8892b7b759d96b3b5d6fa20518caf2c79c6be81a` with a clean porcelain. `ensure_checkout` (136-149) resets only when HEAD differs, so this resume did not fetch or reset it.
- `deploy-parent/codex-home-state.txt` is `kept`. `github-token-present.txt` is `false`. `claude-oauth-token-present.txt` is `true`. `ensure_runner_boot_files` still chmod/chowns those existing paths (1292-1332, 1155-1190). That can update ctime. It does not rewrite `stack.env`, and it does not create the token file when the file is already non-empty.

No stop, rm, volume remove, compose up, or journal save ran in the resume. `c1008_lock` is inside `c1008_bind_generation` (5800), which this refusal does not reach.

## What to do next

`docs/docker-stack.md` 314-322 says the recovery after a seed or up failure is the same SHA, the same phase, and `-ResumeRecycle` of the recorded operation id. That command is what just refused. Running it again with this script fails the same compare. A different SHA fails `.sourceSha==$sha` at 5831-5833. A new `redeploy-old` without `-ResumeRecycle` hits 5836 (`RecycleResumeMismatch` because the journal exists) and, before the remote script, `Assert-ZeroCounters` (`scripts/deploy-server2.ps1` 335-347) because `runnerSessions` is null (`docs/docker-stack.md` 239-243). `Assert-NoIncompleteRecycle` (305-327) throws `RecycleResumeRequired` only once `buildVersion` already equals the target SHA, which it does not.

The script that runs is the desktop file, not the git blob of `-Sha`. `scripts/c590-real.ps1` 472 scp's `scripts/c590-remote.sh` to the host, then exports `C590_SHA` (478, 501). `ensure_checkout` then builds images from that SHA. A fix in the desktop script can resume this journal. A fix inside `init-state.sh` cannot, because the image is built from `8892b7b75`, which still has the recursive chown.

### Ranked by safety

1. Leave the host as it is. Do not resume, do not start the Created runner, do not edit the journal, do not compose by hand. Temp keeps the four sessions. Main stays drained to temp. No operator host command. This is the hold until the script fix exists.

2. Code fix in `scripts/c590-remote.sh`, then the same resume command and the same SHA. No host change until that resume is deliberately run. The digest used at 5824, on resume only, must be rendered with the `SOURCE_SHA12` that was in effect when the journal was saved: the first 12 hex characters of `.previousSha` when that field is a 40-hex SHA (`4358939ecd85` for this journal). The override must not leak into the later `compose_host up`, which has to keep `SOURCE_SHA12=8892b7b759d9` so it uses the images already built (`antiphon-server2/session-testing:8892b7b759d9` and `antiphon-server2/server:8892b7b759d9`). Do not rewrite `stack.env` to the old tag.

   The fixture never sees this. `C1008HostFixture.Run` replaces `compose_host` with `docker compose -p "$HOST_PROJECT" "$@"` (`tests/Antiphon.Tests/Scripts/RollingVolumeRecycleScriptTests.cs` 992), so `C1008_Recycle_resume_requires_matching_receipt` (`RemoteScriptContractTests.cs` 1061) resumes after the env rewrite without re-pinning `SOURCE_SHA12`. Keep that test's edited-journal `composeDigest` drift refusing with no new deletion (1102-1124). Add a case whose `compose_host` honors `deployed_sha12` the way 941-951 does, writes `SOURCE_SHA12` to the target 12 between the failing up and the resume, and asserts resume does not return `RecycleResumeMismatch`. `Host_compose_uses_the_deployed_tag_not_this_runs_sha` (3002-3015) should stay: `compose_host up` still follows the env tag.

3. After that script fix, the chown race is still the blocker inside `compose up`. Temp container `927bb1ad3d60` has `grok` PID 14237. Its `/state/grok` mount is the bind `/var/lib/docker/volumes/antiphon-runner_runner-state/_data/grok` (`docker-compose.server2-runner.temp.yml` 14, set by `case_deploy_temp_runner` 6198-6207). `worktrees.db` is a regular file, 40960 bytes, uid 1654. `worktrees.db-wal` and `worktrees.db-shm` were absent at this read, so nothing holds them right now. They appear when that Grok process checkpoints SQLite. `docker/stack/init-state.sh` 72 is `chown -R` of `/runner-state` under `set -eu`. A name that vanishes mid-walk is exit 1. The seed chown in the failed run won that race; the `compose up` chown lost it. A resume that gets past the digest can lose it again.

   Quiescing temp's Grok writers before that `up` is an operator action. It interrupts the four temp sessions. `-KillSessions` is human-only. The orchestrator should not do it.

4. Durable image change, CARD-1168, for a later SHA, not for this resume. Stop recursing into the live Grok store: `chown -R` `/state`, `/work`, and the non-`grok` children of `/runner-state`, and `chown` `/runner-state` and `/runner-state/grok` as directories only. The directory is already uid 1654. This image is not what `8892b7b75` builds, so this journal cannot deploy it.

Not a recovery: editing `.composeDigest` by hand, `docker start` of `392f011e1f2b`, or a hand `compose up`. The documented drain clear runs only after a successful scripted `redeploy-old`. A hand start would leave `phase=recreating` and `server2` draining at the old `buildVersion`.

### Recommendation

Do not run `-ResumeRecycle` again on this script. Dispatch a Code change for the resume digest pin in (2), with the test above. After that script is what `c590-real.ps1` will scp, the operator quiesces temp Grok and then runs, from the canonical desktop checkout:

```
pwsh -NoProfile -File scripts/deploy-server2.ps1 -Rolling -Sha 8892b7b759d96b3b5d6fa20518caf2c79c6be81a -Phase redeploy-old -ResumeRecycle c10084fb9431d31da48edb781dd54e5ebebf7
```

Same operation id, same SHA, same phase. If that `up` returns `HostComposeFailed` again, stop. Do not open a second journal and do not remove the Created or Exited containers. CARD-1168 stays a follow-up image, after this journal reaches `verified`.
