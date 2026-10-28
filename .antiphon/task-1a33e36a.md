# Final Review 1a33e36a (round 3): CARD-1594 read-only pinned-helper preview (subject 009e67f7)

Reviewed tip 7a5be16f869665c4e8c8c3ce3fea54012ee7b73f (origin feat/card-task-009e67f7 matches). Base rc/2026-10-27 f84228f285dd3e4ad7b5dc8260cbc9f819256f50. Runner: server2 mirror, Linux, uid 1654.

**Outcome: two defects, neither of them fail-open.** Every flag the brief claims is passed. The volumes cannot be written. No host Git runs. All six flag mutations go red. But:

- **D-1:** `--network none` makes the preview refuse on every real volume, where apply passes.
- **D-2:** the timeout and abort paths never kill the container by name.

Both fail closed. D-1 needs a contract decision before Code can fix it.

## Runs (fresh, build-slot gated: one build, then direct dll runs with exact filters)

| Run | Selection | Result |
|---|---|---|
| Build | `dotnet build tests/Antiphon.Tests --property:OutputPath=bin-r1a33/` | 0 errors, 4m12s |
| Named | C1008_Recycle_dry_run_never_mutates, C1338_Retire_temp_dry_run_lists_exactly_the_temp_volumes, C1210_Roster_closure_matches_source_gates, C1098_card_1087_wrapper_census_row_is_split_under_the_same_cap, C1008_Dry_run_preview_binds_a_dry_run_cleanup_receipt | total 5, failed 0, skipped 0 (1m22s; the preview test alone took 72.4 s) |
| Pair | `/*/*/(RollingVolumeRecycleScriptTests*)\|(CheckpointImportTests*)/*` | total 57, failed 0, skipped 0 (26m37s at load ~20) |
| Mutation reds | scratch copy of the repo with the same build output; `C1008_Dry_run_preview_binds_a_dry_run_cleanup_receipt` only | see below: 6/6 red |
| Network probe | `unshare --user --map-root-user --net git ls-remote --heads https://github.com/git/git.git` | exit 128, `Could not resolve host`. Same command with network: exit 0 |
| check-evidence-diff | f84228f2..7a5be16f | commits=6 entries=0 violations=0 |

Mutation reds. Each mutation was applied to a scratch copy, and the worktree was untouched. Each run had total 1 and failed 1.

- M1: drop `,readonly` from the work mount. Red: the stub refuses, then exit 2.
- M2: drop `,readonly` from the runner-tmp mount. Red.
- M3: drop `--network none`. Red.
- M4: drop `--cap-drop ALL`. Red.
- M5: drop `--rm`. Red.
- M6: drop the `timeout` wrapper. Red on `the helper has a hard timeout`.

M1-M5 go red through the stub's refusal, which surfaces as `RecycleGitAuditUnknown audit check=unclassified` at the first Exit assertion. They do not go red through AssertReadonlyPreview's own flag assertions. M6 got past the stub, which shows the scratch copy itself was sound.

Not run:
- The whole Unit lane: the brief says "No whole Unit", so ordinaryScopeCompleted is Interim.
- PCs: all stay pending.
- Real Docker: the brief forbids it. The sig-proxy, PID 1 and AutoRemove behaviour in D-2 comes from source reasoning.

All bin-r1a33 directories (33), the scratch copy and the fixture roots are deleted.

## Preview helper vs apply helper (scripts/c590-remote.sh:7644 vs :7599)

| Aspect | Preview (`c1008_audit_readonly`) | Apply (`c1008_audit`) |
|---|---|---|
| Launch | `timeout --kill-after=10s ${cap}s docker run --name c1008-preview-audit-$$ --rm` | `docker create` (anonymous), `docker start -a`, `docker rm <id>` |
| Network | `--network none` | default bridge |
| Capabilities | `--cap-drop ALL` | default |
| User | `--user 1654:1654` | same |
| Entrypoint / program | `--entrypoint /bin/bash … -c "$(c1008_git_program)"` | same |
| Image | record `.image`, must match `^sha256:[0-9a-f]{64}$` and equal `docker image inspect` Id. Immutable. | record `.image`, or a tag lookup that resolves to an Id. Same check. |
| Mounts | `type=volume,source=<p>_work,target=/work,readonly`; tmpfs `/tmp` (1777, 2 GiB); `type=volume,source=<p>_runner-tmp,target=/runner-tmp,readonly` plus `C1008_TMP_MOUNT=/runner-tmp` | identical |
| Env | `C1008_AUDIT_TIMEOUT_SECONDS`, optional `C1008_SCRATCH_QUOTA_BYTES` | identical |
| Outer deadline | `timeout` on the **client** only | none; inner `timeout` only |
| stderr | discarded | discarded |
| Absent in both | `--security-opt no-new-privileges`, `--pids-limit`, `--memory`, `--init`, `--read-only` rootfs | |

There are no host bind mounts, no docker socket, no `--privileged` and no `--cap-add`. HOME is the image default in both. `GIT_CONFIG_SYSTEM`/`GLOBAL=/dev/null` and commit-graph off are set inside the shared program. Neither helper disables `core.hooksPath` or `core.fsmonitor`. They are harmless here: everything runs inside the cap-less, read-only, uid-1654 container, and that matches apply.

## Findings

### D-1 (preview ≠ apply: the preview refuses wherever apply passes). Owner: Code 009e67f7

- **Where:** scripts/c590-remote.sh:7644 (`--network none`). The audit program requires network in two places:
  - `consider_common` does `timeout … git --git-dir="$top" ls-remote --heads --tags origin … || fail $?`, about line 7156 of the file (program line 788).
  - `fetch_origin` does the blobless origin-proof fetch.
- **Failure:** on any real server2 volume with at least one repository whose origin is a network URL (production clones `https://github.com/…`), `ls-remote` exits 128 with `Could not resolve host`. The probe above shows this. The preview then prints `RecycleGitAuditUnknown audit check=ls-remote status=128` and refuses. Apply runs the same program on the default network and passes. So the bound preview can never pass in production, and the doc claim at docs/docker-stack.md:400 ("runs the same publication audit as apply") is false.
- **Why the tests miss it:** the fake-docker stub (scripts/fixtures/c1008-fake-docker.sh, `previewRun`) checks for `--network none` but runs the program without a network namespace. The fixture origin is also a local bare repository inside the fixture root. Nothing in the test can tell a network-less helper from a networked one.
- **Fix (needs a decision first):**
  - Option a (recommended): give the preview helper apply's network and keep the read-only mounts, `--cap-drop ALL`, uid 1654, `--rm` and the pinned Id. Repo hooks already have network in apply.
  - Option b: keep `--network none` and make the preview an explicitly offline audit, with a named class for the origin step (for example `proof=offline`). The doc must then stop claiming apply parity.

  Either way:
  - The stub must run the program under `unshare --net` when `--network none` is passed.
  - A fixture with an origin that is reachable only over the host network (for example a loopback `git daemon` or HTTP server) must go red under the wrong choice.

### D-2 (lifecycle: the timeout and abort paths do not kill or remove the container). Owner: Code 009e67f7

- **Where:** scripts/c590-remote.sh:7644-7652.
- **Failure:**
  - **Client-only timeout:** `timeout` signals only the docker **client**. With the default sig-proxy, the client forwards SIGTERM to the container's PID 1. That process is `/bin/bash` (no `--init`, no trap), and PID 1 of a PID namespace ignores default-action signals, so nothing happens.
  - **Stale container on timeout:** after 10 s, `timeout` SIGKILLs the client. The container keeps running. The inner `timeout` uses the same cap but started later, so a body that ignores SIGTERM is still alive when the code runs `docker inspect "$name"`. The code then runs `docker rm -- "$name"` **without `-f`**, which cannot remove a running container. The preview returns 2 (fail closed) but leaves a running helper behind.
  - **Script abort:** there is no trap that kills the helper by name when the script is aborted (ssh drop or SIGHUP, SIGINT, wrapper kill). In that case the helper runs on for up to the full cap (3 h by default) with `<temp>_work` and `runner-tmp` mounted. An apply re-run in that window refuses `RecycleVolumeInUse`. That is fail closed, but it is residue the brief requires to be absent on every exit path. The helper is unlabeled, so no census can attribute it.
  - **Name reuse:** the name is `c1008-preview-audit-$$`. If a stale or foreign container already has that name, `docker run` fails, and the code then `docker rm`s that pre-existing container. Its ownership is never inspected.
- **Fix:**
  - Run with `--init`, or give the program a TERM trap.
  - On timeout, signal or EXIT, `docker kill "$name"` (or `docker rm -f` by the recorded ID) and confirm absence.
  - Use `--cidfile` or the launch's own ID with an owner label, and remove only that ID.
  - Add a stub witness: a hanging helper must be killed and leave no container, and an aborted wrapper must leave no container.

## Closure of Review 8486d90d defects

- D-1 (userns root with caps, remount escape, detached writer): **closed**. The container is uid 1654 with `--cap-drop ALL`, and Docker makes both mounts read-only, so remount fails. When PID 1 exits, the kernel kills every process in the namespace, so a detached writer cannot outlive the audit. The hooked fixture's remount and late writer both leave the bytes unchanged after the 4 s wait.
- D-2 (repo code on the host): **closed**. No host Git runs: the probe records any host `git` call and recorded none, and `c1008_audit_readonly` contains no Git invocation.
- D-3 (host Git): **closed**. The program runs on the image's Git, pinned by image Id.
- D-4 (per-file sudo digest): **closed**. The digest is removed.

Read-only classification (priority 2): a read-only mount cannot turn a refusal into a pass. Any EROFS-induced Git failure takes `fail`, which is RecycleGitAuditUnknown. The program sets `GIT_OPTIONAL_LOCKS=0` and apply is read-only too, so preview and apply see the same filesystem semantics, apart from D-1. The exit-code mapping 3/4/other to Unpublished/Dirty/Unknown is identical, and `AssertApplyMatchesPreview` checks parity for the dirty and unpublished cases.

## Advisories

- **A1:** both helpers lack `--security-opt no-new-privileges`, `--pids-limit` and `--memory`. Volume safety does not depend on them: `--cap-drop ALL` empties the bounding set, and the mounts are read-only. A fork or memory bomb from a repo hook could load the host in both preview and apply.
- **A2:** the stub runs the program as userns root with full capabilities, not as uid 1654 without capabilities. The hook's remount fails only because of mount locking. The stub does not model `--network none` (see D-1), `--rm` or the timeout. The assertion that no `io.antiphon.audit` container is left cannot go red: the stub never records a `run` container, and the real preview sets no such label.
- **A3:** clean-pass parity is not compared. Apply runs only in the dirty and unpublished cases, never on the clean or linked fixture.
- **A4 (performance standards):** the preview test takes 72 s and was added to CP-3b (EstimatedMinutes 8). The combined CP-3/3b row previously hit its 1200 s cap, and the estimate was not re-measured. The 57-test pair took 26m37s at load ~20, under the 30-minute Debug threshold.
- **A5:** the census plan and CheckpointImportTests counts (13 rest / 17 declared) and the WindowsUnitSkipRoster entry are consistent with the green pair run.
- **N/A to this change:** producer/recipient, transcript and UserPrompt evidence, and input-wait release, because this is shell and test only.

## Recommendation on the contract change (operator decides)

Accept the relaxation from "no preview helper" to exactly one helper: read-only mounts, uid 1654, capability-less, auto-removed, pinned by Id. It is strictly safer than every host-side variant reviewed so far, and the witnesses for it are real. But accept it together with D-1 option (a), the helper on apply's network. With `--network none` the preview can never pass on a real volume. Then Code fixes D-2.
