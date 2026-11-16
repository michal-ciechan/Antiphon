# CARD-1955 Final Review, round 1

Review task cd9b15c9. Subject Debug task 235c38c6-6743-47c6-a80c-893100824776, tip 9cb0c9ab10dfa875cf21457d5ed3d744ef892c56, base 48a4615dcb4d67033886a3e31629dc7351ed0852. Verdict: clean. No product file was edited by this review.

## Outcome

The host freshness fence is the single named constant `C1609_EFFECT_WINDOW_MS=11000` in `scripts/c1008-retirement.sh`. A missing or malformed constant refuses. Equal to the window is admitted. An observation aged 15,820 ms refuses at 11,000 ms and would be admitted if the constant were 20,000 ms. The desktop 5-second control and Git budget, the 10-second response read, and the 900-second proof lifetime are unchanged. Apply still re-snapshots and re-checks publication before each volume delete.

`reviewedSourceClean` is false because every test run was a leased `dotnet run`, not a checkpoint-tool receipt. There is no plan and no `### Checkpoints` table on this Debug change. The commissioned selection ran, so ordinary scope is Full. Positive controls and waived mutation variants stay owed. Native Windows confirmation stays with the orchestrator's post-land preview. Qualification a21d8174 was not touched.

## Safety

`C1609_EFFECT_WINDOW_MS` is read by the exchange-required refusal text (line 519), the diagnostic `limitMs` (line 582), and the comparison and message in `c1609_effect_publication_fresh` (lines 596–600). The file contains no remaining `5000`. The comparison refuses unless the constant matches `^[1-9][0-9]{0,5}$` (1..999999) and refuses when elapsed is negative or greater than the window.

Remaining `5000` literals:

- `scripts/c590-remote.sh` lines 168, 224, and 319 are `ANTIPHON_BIND_PORT` / `CHILD_BIND_PORT`, not the fence.
- `scripts/c590-real.ps1` lines 431 and 436 are the desktop control budget and `control-deadline`. The file is not in the diff.
- `scripts/c1609-host-proof.ps1` lines 368 and 375 are `NetworkBudgetMs` default 5000 and the `-gt 5000` throw. The file is not in the diff.

`read -r -t 10` remains at `scripts/c1008-retirement.sh:562`. Proof lifetime comparisons remain 900 seconds (lines 295, 350, 351, 786).

Throwaway copies `/tmp/c1008-retirement-review-11000.sh` and `/tmp/c1008-retirement-review-20000.sh` (`bash -n` of both product scripts exited 0):

- Unset, empty, `abc`, `0`, `-1`, `+11000`, `11000.0`, `011000`, trailing space, `1e5`, `1000000`, and `999999999` each returned 1.
- `jq --argjson` of a non-numeric limit refused.
- Elapsed 5170 at window 11000 returned 0. Elapsed 15820 at window 11000 returned 1. Elapsed 15820 at window 20000 returned 0.
- A frozen monotonic clock at 100000000 made elapsed exactly 11000 return 0 and elapsed 11001 return 1. An earlier live-clock "equal" sample overshot (11060 and 1000279) and is not a product refusal.
- Empty `C1008_HOST_PROOF` still returns 0 before the window check. That skip predates this change.
- The regex ceiling admits 999999 ms. Seven or more digits refuse. The production assignment is the literal 11000. This ceiling is an advisory, not a land blocker.

No test assertion was deleted. Diff of `scripts/`, `tests/`, and `docs/` against the base moves constants with the fence: sleep 6 to 12, `>= 6000` to `>= 12000`, `> 5000` to `> 11000`, and the N3 log label `limitMs=11000`. The N3 label is still a hardcoded printf, the same shape as the old 5000 label; the exit code is the real function result. `WindowsUnitSkipRoster.cs` adds the two new receipt methods as Windows skips.

`VolumeRetirementHostProofTests.Final_live_check_keeps_the_five_second_desktop_budget` passed (5000 admitted; 5001 and 11000 refused before git; `c590-real.ps1` control-deadline stays 5000).

`docs/docker-stack.md` lines 488–505 state the 11-second provisional host fence, the unchanged desktop 5-second budget, the 10-second read, the 900-second lifetime, the extra six seconds of origin-withdrawal exposure, and the revisit trigger (a measured exchange over 8 seconds, or after the first five native applies). Apply in `scripts/c590-remote.sh` is unchanged: per volume, `c1609_publication_gate` (9658), `c1609_recheck_target` (9659), `c1609_effect_publication_fresh` (9663), then `docker volume rm` (9665). The apply gate entry remains line 9640.

`VolumeRetirementDockerTests.Exact_deletion_and_crash_resume_preserve_other_volumes` was not re-run. The Debug already recorded it red at this base and at this tip with `reason=lifecycle-active`. CARD-1956 owns that repair. It is not a defect of this fence change.

## Verification

Linux, leased, serial, `TEMP=/tmp`, `--timeout 570s`, `--no-build` after one `Antiphon.Tests` build. Build lease `c960ab8f-0feb-4fdd-a928-7b2841915348`, waited 105s, held 774s, 0 errors, 2145 warnings. Host `nproc=24`. No run's test duration exceeded 10 minutes.

| Selection | Lease | Result |
|---|---|---|
| Receipt witnesses `One_repository_measured_control_fits_the_host_fence`, `Control_observation_older_than_the_host_fence_refuses` | d40a1ffe, waited 15s, held 86s | 2 passed |
| `Final_live_check_keeps_the_five_second_desktop_budget` | c1e75f69, waited 15s, held 14s | 1 passed |
| Receipt `Large_ready`, `Late_ready`, `Failed_refused` | 3587bea7, held 137s | 3 passed |
| `Final_origin_binding_refresh_is_inside_effect_window` | 7df72938, waited 15s, held 52s | 1 passed |
| `VolumeRetirementApplyGateTests` | 8ffcf796, waited 150s, held 322s | 13 passed |
| Receipt shards A–D (Preview through Ready_publication) | 24ae1e2a, edca030f, dbfb4bc4, a93e9e1d | 4, 4, 5, 2 passed (held 69s, 446s, 489s, 42s) |
| `VolumeRetirementHostProofTests` | f49db0b4, held 65s | 16 passed |
| Timing, impacted-classes, docker-stack docs, and the guard classes except `DerivedImpactIndexTests` | 062a3574, held 267s | 159 total, 158 passed, 1 skipped (`C1210_Windows_linux_only_outcomes_are_exact`) |
| `scripts/regenerate-derived-impact-index.ps1` twice | 45af653f held 35s; 45f2480c held 35s `-NoBuild` | both printed `derived-impact-index unchanged` |
| `DerivedImpactIndexTests` alone, after both regenerations | 1f0b3f51, waited 30s, held 166s | 27 passed, duration 2m 38s |

Index sha256 before and after both regenerations: `87ba8cd71c0358df3a274ce6e6e952e503e7463ba9e0a8c7af8cea7878e1647e`. The worktree stayed clean.

`git fetch origin master` on 2026-11-16 still resolved `48a4615dcb4d67033886a3e31629dc7351ed0852`. `git merge-tree --write-tree origin/master 9cb0c9ab10dfa875cf21457d5ed3d744ef892c56` exited 0 with tree `341cd4807a6d0d8996c7a78516c21a461b3d8fa0`, identical to the tip tree. Regenerating on this checkout is the scratch merge. `scripts/check-evidence-diff.ps1` over that range: commits=5, entries=0, violations=0.

Pins present and covered by the passing guard witnesses: policyHash `c2e3939821a8fef911600c44be767b951c8212bcb62824cb746e48c5aaba6909`, PinnedInputDigest `2f48120a65280582b586f9369e2eaf6b1bdc464a2362c8e49d434bd852479f60`, LegacyExclusionDigest `6d6ff646207dba9e3c09316e5bc77651f6801fe945ccde83b9b58644367b252a`.

Impacted-classes kinds for the range: `native-linux-gate-roster`, `remote-scripts`, `spawn-limit-rosters`, `test-lane-category`. 34 classes. The migrations kind did not match, so the downgrade selector count is 0. The commissioned selection is the brief's named tests, not the whole `scripts/*.sh` blast. `ProcessSpawnLimitTests` was not run; this change adds no limiter (R4 does not fire).

## Checklists

OS branch witnesses: the changed production behavior is the Linux host bash constant. No .NET OS-selected factory or property changed. The desktop PowerShell budget is unchanged and is pinned by `Final_live_check_keeps_the_five_second_desktop_budget`. There is no plan file, so `scripts/check-os-witnesses.ps1` was not invoked. Native Windows execution remains the orchestrator's real preview.

Performance and CARD-1809: the new and updated witnesses sleep a fixed 12 seconds and then assert that same render's elapsed time and exit. They do not poll a multi-record turn (R1). Assertions are numeric bounds and `limitMs` tokens, not full generated text with a hard-coded newline (R2). No service terminal-state enum changed; the delete loop still re-snapshots before the freshness check (R3). No new `ParallelLimiter` (R4). No passes-alone red (R5). No selection ran over 30 minutes. Slot waits are the held/waited figures above. Receipt shard B was 7m22s and shard C was 8m05s of test time.

Input waits: this change does not wait on a session. `read -r -t 10` is unchanged. Parking is not involved.

## Advisories (do not hold the land)

- The malformed-constant regex admits any integer from 1 through 999999. Production ships the literal 11000, and non-numeric, zero, negative, and 7-digit values refuse.
- The N3 fixture's `limitMs=11000` is a log label, not a read of the production constant.
- CARD-1956 owns the inherited `VolumeRetirementDockerTests` `lifecycle-active` red. It was not re-run at the base in this review.
- Empty host-proof inventories still skip the freshness check. That path predates the constant.

## Land

Land the Code owner 235c38c6-6743-47c6-a80c-893100824776 with `-ExpectedSourceSha 9cb0c9ab10dfa875cf21457d5ed3d744ef892c56`. Do not land this review commit as the product change.
