# CARD-2040, CARD-2041, CARD-2042 Final review, round 1

Verdict: **CLEAN**. No blocker and no regression that holds the land. Advisories below go to cards under D-2. Positive controls stay pending for SourceLanding Mutation.

Subject: `origin/feat/card-task-bc823630` at `b1c5aefce3b66a1507c061d2382cdfd04a89387a` (Codex author, Code task `bc823630-a611-48f0-9c23-996da67a5c2a`). Range `ce0eb1c5fbccdf8b2918643a319b88386435b761..b1c5aefce3b66a1507c061d2382cdfd04a89387a`. Reviewer task `9b8c302a`. Plan `docs/superpowers/plans/2026-11-21-close-advisories-checkpoints.md`.

`reviewedSourceClean` is false. These rows are leased build-slot runs in the worktree's native output. There is no checkpoint-tool receipt and no SHA-validated clean receipt with build provenance. `ordinaryScopeCompleted` is Full: the plan's closed final rows, the named classes, the guard batch, evidence-diff, merge-tree, the pin, the OS-branch inventory, and the two scratch mutants all executed at this SHA.

## Claims

- CARD-2040. `VolumeRetirementCloseExemptionTests.Deploy_clearable_checks_receiptless_previews` extracts production `c1008_volume`, `c1609_load_retirement`, and `c1609_temp_retirement_clearable` from `scripts/c590-remote.sh` and sources production `scripts/c1008-retirement.sh`. The fixture uses a private `SERVER2_ROOT` and a docker stub. Six receipt-less cases pass on the real function. An always-clearable scratch mutant (`return 0` at the start of `c1609_temp_retirement_clearable`) made five refusal rows fail and left the exempt row green. The script was restored; `git diff` on it is empty.
- CARD-2041. Both `docs/docker-stack.md` passages (the Close section and the resume section) say a receipt-less preview is exempt when `complete: true`, every child is joined, `readyPublished` is false, and neither `closure.json` nor `close-pending.json` exists, and that phase start also writes `complete: true` with an empty child list so liveness rests on the owner check. That matches `c1609_phase_begin` and `c1609_receiptless_preview_exempt`.
- CARD-2042. `Get-CloseRemoteReason` returns only the fifteen exact tokens the Close packet emits, or `TempVolumesRetained` plus one of the four constant volume names. `password x`, a token with a trailing secret, a volume outside those four names, an ANSI-decorated token, and `TempRetirementUnresolved` all come back null, so the wrapper surfaces `JournalUnknown`. A newline keeps only the last `TempRetirementCloseRefused` line. No Close-packet token is missing from the allow-list. Local `Fail-Close` words never go through this filter.
- CARD-2043 diagnosis. `Test-C1008SafeAuditDiagnosis` accepts `missing-executable:` basenames that contain `+`, and accepts a `stderr=redacted` or `missing-executable:` suffix on the worktree-status line. `Invoke-HostCase` still throws `HostCaseFailed $Case exit=$LASTEXITCODE` and only appends a diagnosis the validator accepts. `c590-real.ps1` still replaces an unsafe diagnosis with `RecycleGitAuditUnknown` and calls `Write-C590Result -Accepted $false -ExitCode 2`.

## Attacks

The exempt-only owner skip is already refused on Close. Deleting the `/proc/$pid` block inside `c1609_receiptless_preview_exempt` left all six `Deploy_clearable` rows green, including the live owner, because `c1609_temp_retirement_clearable` checks the owner before it calls the shared predicate. `Close_refuses_receiptless_preview_with_live_owner` failed because Close completed (`TempRetirementClosed`). The Close sibling path depends on that check, and the existing Close test goes red when it is removed. The script was restored.

The new diagnosis branch does not decide acceptance. A `+` in `helper++` and a well-formed `stderr=` suffix change only whether that text is appended to `HostCaseFailed`. Credential-shaped basenames, ANSI, newlines, slashes, and a leading dot in the basename stay rejected. The script diff of the touched PowerShell and shell files is ASCII.

`git merge-tree --write-tree --name-only 72565797a7702f4af64f43f51d854ea08071b1d1 HEAD` exits 0 with tree `7767331dbc69a6b5f107e7488da95940a43b4613` and no conflict paths. `ce0eb1c5` is an ancestor of that master SHA. Master has since changed `tests/derived-impact-index.json` by 450 insertions and 108 deletions. The merged tree differs from that master by the branch's one added class edge. `DerivedImpactIndexTests` at 27/27 proves the branch file against the branch tree. Land regenerates the index on the combined tree. That is a replay, and it is not a conflict.

## Evidence

Native default output, `UseAppHost=false`, `TEMP=/tmp`, `ANTIPHON_LINUX_COMPILER_POLICY=0`, `--timeout 570s`, one build-slot lease per command. A row started only while the 1-minute load was below 14. Historical red rows CP-1 and CP-10 were Code's red-against-old-code; they were not re-expected red on this SHA. The scratch mutants above are the red evidence. The plan's custom `bin-close-advisories-*` outputs were not used; the brief and CARD-1629 require the native output.

| Row | Result |
|---|---|
| `VolumeRetirementCloseExemptionTests` | 15 total, 15 passed, 0 failed, 22s, load1 13.00, lease `817ed22d-d753-451b-8c04-4d82de8e8312` held 27s |
| `VolumeRetirementWrapperTests` | 7 total, 7 passed, 7m 54s, load1 11.67, lease `abd4f75a-b85e-445a-81d4-85dd35ba5046` held 479s |
| `Antiphon.Tests` `ProcessSpawnLimitTests` | 57 total, 57 passed, 8s, load1 9.57, lease `052ad5d9-c891-4261-97a6-b533390f9298` held 12s |
| Nine-class guards (CP-4) | 84 total, 84 passed, 1m 49s, load1 8.86, lease `29f8b5d8-77df-452a-b4af-e5d8f587d453` held 114s |
| Five `RemoteScriptContractTests` methods | 5 total, 5 passed, 6s, load1 11.48, lease `804e63b8-2576-4472-9791-fe7299f17850` held 10s |
| Two `ReleaseGatePlatformTests` methods | 2 total, 2 passed, 59s, load1 10.50, lease `7e0faf93-64f9-4844-8eb7-a823647e7954` held 64s |
| `DerivedImpactIndexTests` | 27 total, 27 passed, 3m 11s, load1 8.60, lease `850e9d9b-8571-4741-9af7-f18451d04335` held 197s |
| AuditDiagnosis fixture (CP-11) | checks 12, passed 12, load1 11.18, lease `63d0b014-8901-4780-a4bf-539a720e53c8` held 8s |
| Pty `ProcessSpawnLimitTests` (CP-12) | 2 total, 2 passed, 558ms, load1 8.06, lease `99efdaec-8b5b-428f-b715-e30da9854d44` held 4s. Build 0 errors, lease `a83c2503-0df5-4997-b77c-b6bf86a4c533` held 19s |
| SessionRunner `ProcessSpawnLimitTests` (CP-13) | 2 total, 2 passed, 798ms, load1 13.93, lease `1481265d-fbfa-4801-b0be-1b56502fe93f` held 4s. Build 0 errors, 190 warnings, lease `9be6b578-d122-424c-88dc-c3e6788d4a9a` held 24s, load1 13.53 |
| Always-clearable mutant | 6 selected, 5 failed, 1 passed, load1 12.95, lease `ce9d727f-de30-4389-9f86-ca327a2e8923` held 12s. Restored |
| Exempt owner-skip mutant | 7 selected, 1 failed (`Close_refuses_receiptless_preview_with_live_owner`), 6 passed, load1 13.02, lease `f9810300-db4a-4c7c-bff6-745a5ef19463` held 17s. Restored |
| `check-evidence-diff.ps1` | commits 4, entries 0, violations 0, exit 0 |
| Land-graph `-Check` | exit 0, digest `819858AE650809E79A56F06FEC131EE85FA720D2A05DCF53293403A766F7B8DD`, count 80, phase pass2. `-Update` was not run |
| Live-lane pin | recomputed digest `659ea09e42513920ce1e46c4479ef092728db7702b687a65742768d67d46886e` matches the pin |
| `impacted-classes.ps1 -MergeSubset` | `migrations=False`. Kinds `spawn-limit-rosters,test-lane-category`. The two spawn rosters and the two census methods are in the rows above |
| `check-os-witnesses.ps1` | exit 0. Inventory covers every non-doc path. The only candidates are unchanged `$IsWindows` path normalization at `scripts/deploy-server2.ps1` lines 549 and 553, classified non-behavior. Restart impact is empty |

CP-2's floor of 79 is the Antiphon.Tests split: 15 + 7 + 57. CP-12 and CP-13 are the other two `ProcessSpawnLimitTests` assemblies. CP-4's floor of 78 is a minimum; this run executed 84.

## Advisories

These do not hold the land.

- The new `stderr=` branch of `Test-C1008SafeAuditDiagnosis` accepts `repo=` with the same `[A-Za-z0-9._/-]+` class as the older fallback. For `stderr=redacted` the basename capture is empty, so the credential denylist does not inspect the repo. `repo=team/password stderr=redacted` and a leading-slash repo are accepted as diagnosis text. The honest producer still redacts those paths before it forms the line. Acceptance stays false and the exit stays 2. File a card if the diagnosis surface should use the private-origin repo denylist.
- `Deploy_clearable_checks_receiptless_previews` stays green when only the shared predicate's owner check is removed, because clearable checks the owner first. `Close_refuses_receiptless_preview_with_live_owner` is the witness for that predicate check.
- The plan's CP-4 minimum is 78. The executed count is 84, which matches the Code claim. The table minimum is stale and is still a floor.

PCs in `docs/superpowers/mutations/2026-11-21-close-advisories-candidate.md` stay pending.
