# Final Review 25fcc811 (round 4): CARD-1594 preview owned helper on apply's network (subject 86bf9970)

Reviewed tip 960290824b7a850a0d23777884cd3b4e5e5946cf (code fix e17c7a6f1, origin feat/card-task-86bf9970 matches). Base rc/2026-10-27 f84228f285dd3e4ad7b5dc8260cbc9f819256f50. Runner: server2 mirror, Linux, uid 1654.

**Outcome: no defects, no regressions and no fail-open.** Review 1a33e36a's D-1 (network) and D-2 (helper lifetime) are both closed. Every claimed flag is passed, and 7 of 9 protection mutations go red. The two survivors are defence-in-depth checks with a structural backstop (A1).

## Runs (fresh; one build-slot build, then direct dll runs with exact filters)

| Run | Selection | Result |
|---|---|---|
| Build | `dotnet build tests/Antiphon.Tests --property:OutputPath=bin-r25fcc/` | 0 errors, 2m12s |
| Named | C1008_Dry_run_preview_binds_a_dry_run_cleanup_receipt, C1008_Recycle_dry_run_never_mutates, C1338_Retire_temp_dry_run_lists_exactly_the_temp_volumes, C1098_card_1087_wrapper_census_row_is_split_under_the_same_cap, C1008_Preview_helper_removal_is_owned (new) | total 5, failed 0, skipped 0, 2m03s |
| C1210 | `WindowsUnitPlatformAdmissionTests/C1210_Roster_closure_matches_source_gates` | total 1, failed 0 |
| Pair | `/*/*/(RollingVolumeRecycleScriptTests*)\|(CheckpointImportTests*)/*` | total 58 (32 + 26), failed 0, skipped 0, 26m31s at load 4-9 |
| check-evidence-diff | f84228f2..96029082 | commits=15 entries=2 violations=0 |

Mutations. Each was applied to a scratch copy (git archive plus the same build output), and the worktree was untouched. Each run was a single method.

| # | Mutation | Test | Result |
|---|---|---|---|
| M1 | add `--network none` | Dry_run_preview | **red**: `RecycleGitAuditUnknown audit check=ls-remote status=128`, a behavioural failure from the offline shim plus `unshare --net` |
| M2 | drop `--init` | Dry_run_preview | **red**: stub `preview requires --init` |
| M3 | drop `--security-opt no-new-privileges` | Dry_run_preview | **red**: stub refusal |
| M7 | drop `,readonly` on the preview work mount | Dry_run_preview | **red**: stub mount refusal (`preview-helper status=2`) |
| M4 | drop both `docker kill` calls (watchdog and release) | Preview_helper_removal_is_owned | **red**: `timeout kills the owned helper` |
| M5 | `rm -f` becomes `rm` | Preview_helper_removal_is_owned | **red**: `timeout force-removes the owned helper` |
| M6 | drop the INT/TERM/HUP trap | Preview_helper_removal_is_owned | **red**: `abort leaves no preview helper` |
| M8 | drop the label ownership compare in `c1008_preview_release` | Preview_helper_removal_is_owned | **green (survives)**, see A1 |
| M9 | drop the pre-run `docker inspect "$name"` refusal | Preview_helper_removal_is_owned | **green (survives)**, see A1 |

Round 3 already showed red for the runner-tmp readonly mount, `--cap-drop ALL` and `--rm`. Those stub checks are unchanged, so I did not rerun them.

The 9 scratch copies, all 33 bin-r25fcc directories and every fixture root are deleted. No c1008 process remains.

## Preview helper vs apply helper (scripts/c590-remote.sh:7783 vs :7597)

| Aspect | Preview | Apply | Difference |
|---|---|---|---|
| Launch | `timeout --kill-after=10s ${cap}s docker run` (backgrounded, waited) | `docker create`, then `start -a`, then `rm <id>` | lifecycle only |
| Network | no `--network` (default bridge) | no `--network` (default bridge) | **same**; D-1 closed |
| DNS / proxy / extra hosts | none | none | same |
| Env | `C1008_AUDIT_TIMEOUT_SECONDS`, optional `C1008_SCRATCH_QUOTA_BYTES`, `C1008_TMP_MOUNT` when runner-tmp exists | identical | same; no credential, token, SSH agent or helper in either |
| Git config | inside the shared program: `GIT_CONFIG_SYSTEM/GLOBAL=/dev/null`, `GIT_OPTIONAL_LOCKS=0`, `GIT_TERMINAL_PROMPT=0`; no safe.directory, since the files are owned by uid 1654 | identical program (`c1008_git_program`) | same |
| User / entrypoint | `--user 1654:1654 --entrypoint /bin/bash -c <program>` | same | same |
| Mounts | `<p>_work` to /work **readonly**, tmpfs /tmp 2 GiB, `<p>_runner-tmp` to /runner-tmp **readonly** | identical (line 7572 = line 7737) | same; no state or auth volume, no bind mount, no docker socket |
| Image | record `.image`, must be a `sha256:` Id equal to `docker image inspect` | the same, or a tag lookup when unpinned | preview is stricter |
| Hardening | `--rm --cap-drop ALL --security-opt no-new-privileges --init` | none | preview only. The preview gains nothing apply lacks, and the audit needs nothing it lost: uid 1654 has an empty effective cap set anyway, and the program uses no setuid binary |
| Ownership | `--name c1008-preview-<op>-<16hex>`, `--label io.antiphon.preview-audit=<op>:<token>`, `--cidfile $CASE_DIR/preview-<token>.cid` | anonymous id | preview only |

Network exposure compared with apply: none new. The work and runner-tmp volumes are read-only, and no credentials are passed. Repo-config code (for example `core.fsmonitor`) can already run with network in apply. It can now also do so in the preview, as uid 1654 with no capabilities and read-only volumes.

## D-2 exit-path trace (all removal is by the cidfile id after a label match)

- **Normal:** `wait` returns. `c1008_preview_finish` then calls `c1008_preview_release`, which kills the watchdog, then `inspect id`. The container is gone (`--rm`), so the release returns 0. If the container still exists, the release checks the label, runs `kill` if it is running, then `rm -f id`, then `inspect`; a container that is still present returns `preview-helper status=2`.
- **Audit failure:** the same path. The refusal class comes from the output: 3 Unpublished, 4 Dirty, else Unknown. That is the same mapping as apply.
- **Timeout:** the client `timeout` sends TERM. With the default sig-proxy, the client forwards it to `docker-init`, which passes it to bash; the program also traps TERM, which leads to `exit 143`. The watchdog (`sleep $cap`) does a `docker kill` on the owned id at the same budget, and the release then force-removes it. The test witnesses this with a TERM-ignoring hang stub (M4 and M5 red).
- **docker run failure:** no cidfile is written, so there is no id and nothing is removed. The run returns `preview-helper status=<rc> detail=<redacted stderr line>`, which is RecycleGitAuditUnknown.
- **SIGINT/TERM/HUP:** `c1008_preview_on_signal` ignores further signals, TERMs the client (bash execs `timeout` directly in that subshell; verified with bash 5.2.15), then releases and exits 143. The restored EXIT trap writes `UnhandledExit 143`. M6 red.
- **EXIT:** the temporary EXIT trap releases first, then writes the original receipt. The restored trap is byte-identical to line 9015.
- **ssh drop:** with no tty there is no HUP. The preview runs to completion or to the budget and cleans up normally. If the script is SIGKILLed, the watchdog's kill and the program's inner timeout both still bound the helper at the cap.
- **Name collision:** the pre-check refuses `preview-name`, and Docker itself refuses `Conflict`. Neither removes anything. Witnessed: the foreign container is kept, and the trace has no rm or kill.

## Read-only and fail-closed (priority 3)

The preview and apply run the same `c1008_git_program`, on the image's Git, with the same refusal classes. Both mounts are read-only in both helpers, so an EROFS index or lock write fails into `fail`, which is RecycleGitAuditUnknown, never a pass. A preview prints `auditPending=false` only after a zero-exit audit. An unpinned preview never calls `c1008_audit_readonly` and prints `auditPending=true` (C1338 and C1008_Recycle_dry_run_never_mutates are green). The preview receipt carries no apply authority.

## Findings

None. No way was found for the preview to write a volume, run repo code on the host, pass where apply refuses, leak credentials, or leave a running container on any signalled path.

## Advisories

- **A1 (test adequacy):** M8 (label compare) and M9 (name pre-check) survive.
  - The release takes its id only from this run's fresh cidfile, which only a create this run issued writes, and Docker refuses a name conflict itself. That makes both checks unwitnessed defence-in-depth, not a gap in safety.
  - Witness to add: a stub fault that writes a foreign labelled id into the cidfile, asserting that container is kept.
- **A2 (race):** `c1008_preview_finish` calls the release while the INT/TERM/HUP traps are still live (scripts/c590-remote.sh:7693), and the release clears the owned id and cidfile before its docker calls.
  - A signal landing inside that window re-enters `on_signal`, finds no id, and exits 143 without the `rm -f`.
  - The residue is bounded: on that path the client has already exited and `--rm` removes the container, and on timeout the program's TERM trap and inner timeout end it.
  - Fix: `trap '' INT TERM HUP` at the start of finish, as `on_signal` already does, or clear the state only after removal.
- **A3:** a TERM between the daemon's create and the client's cidfile write could leave a created-but-never-started container, which `--rm` does not remove. This is a narrow Docker CLI window and was not verified.
- **A4 (budget):** the preview shares apply's budget, 10800 s by default. That figure is justified by the 5225 s clean-volume measurement, but it is hours: a preview can hold the temp volumes for up to 3 h. Whether the preview should take a shorter cap is an operator choice.
- **A5:** neither helper sets `--pids-limit` or `--memory` (carried from A1 of the last round).
- **A6 (performance):** CP-3b EstimatedMinutes 8 is still not re-measured now that it covers 14 methods; the previous round's A4 is carried. The pair took 26m31s, under the 30-minute Debug threshold.
- **A7:** the `/dev/fd` symlink is fixture-only, inside the stub's private stage (scripts/fixtures/c1008-fake-docker.sh). Real images get `/dev/fd` from Docker.
- **N/A to this change:** producer/recipient, transcript and UserPrompt delivery, and input-wait release. This is shell and test code only.

## Recommendation on the contract change (operator decides)

Accept the relaxation from "no preview helper" to exactly one helper: read-only, uid 1654, capability-less, no-new-privileges, `--init`, auto-removed, pinned by image Id, on apply's network, run-owned by name, label and cidfile.

- The preview receives exactly apply's inputs plus extra hardening. Its only new capability compared with round 3 is network access, which apply already has and the audit requires.
- Without it, the preview can never pass on a real volume.
- A2 is a cheap follow-up and is not a blocker.
