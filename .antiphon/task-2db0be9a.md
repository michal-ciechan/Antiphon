# Task 2db0be9a: cut rc/2026-11-07 from master

Outcome: rc/2026-11-07 cut at master 1038d07e66a54b630a1bc1d394923bc7049b73dd (plain branch push, no force).
Candidate id: rc-2026-11-07-1038d07e66a5. No tag, no publish, no promote, no -Land, no land-graph -Update.

## Master pin and cut proofs

- Pin, `git ls-remote origin refs/heads/master` at 05:19 UTC: 1038d07e66a54b630a1bc1d394923bc7049b73dd
- Re-check at 05:56 UTC before the push: master unchanged (1038d07e66a54b630a1bc1d394923bc7049b73dd), rc/2026-11-07 still absent
- `git push origin 1038d07e66a54b630a1bc1d394923bc7049b73dd:refs/heads/rc/2026-11-07` gave `* [new branch]`
- `git ls-remote origin refs/heads/rc/2026-11-07` after the push: 1038d07e66a54b630a1bc1d394923bc7049b73dd (equals the pin)

## Name validation

`scripts/lib/release-gate.ps1` `Test-ReleaseGateCandidateRef -Ref rc/2026-11-07 -Sha <pin>` returns
Ok=true, Kind=long-lived, CandidateId=rc-2026-11-07-1038d07e66a5, CutUtc=2026-11-07T00:00:00Z.
That function is the only check on the name: the pattern `^rc/\d{4}-\d{2}-\d{2}$` plus a real-date parse.
None of release-cut.ps1, release-candidate.ps1, rc-qualification-status.ps1 or docs/release-gates.md
compares the ref date with the clock. **No script refuses the name.** release-candidate.ps1 needs the
long-lived ref to exist on the remote already, and it now does.

## A-series

- A1: `git ls-remote origin refs/heads/rc/2026-11-07` returned nothing before the cut. PASS.
- A2: rc/2026-11-06 (5a95ae7a5), rc/2026-11-05 (147034055) and rc/2026-11-04 (f32aed42e) are all ancestors of the pin.
  `git cherry <pin> origin/<rc>` gives 0 '+' and 0 '-' for each. Nothing is lost. PASS.
- A3: digests. PASS.
  - policyHash: stored 2c445d46531ccff2a572e27b5e641e984af40c3b4691c67661dd9a4a360eba72, recomputed (Get-NightlyPolicyHash) 2c445d46531ccff2a572e27b5e641e984af40c3b4691c67661dd9a4a360eba72
  - PinnedInputDigest: stored 417936f79e8bf453a32f2f91b202ae6435777c3ba21e159d831ca9b5a052c939, recomputed (-PrintInputDigest) 417936f79e8bf453a32f2f91b202ae6435777c3ba21e159d831ca9b5a052c939
- A4: `audit-land-verification-graph.ps1 -Check -ToolPath <native Debug LandGraphAudit.dll>` exit 0, `approved`.
  digest=D5B302A94FD3B0C28012C6764A2027B8B8DDE1D04506B3A641DD671EFB6F8C03 count=69, phase=pass2.
  No mismatch, so no warning is needed. 5 not-built entries, all expected (card-0462 Reproduce, c1375/c487 probes, TimeoutAudit.Tests, NativeLinuxMigrationAudit). The canary was removed.
- A5: test-rc-platform.ps1. PASS.
  - -Case LinuxOnlyWindowsExclusionPolicy: 6 passed, 0 failed, 6 rows
  - -Case C1686_LinuxOnlyCensus: 62 passed, 0 failed, 62 rows
- A6: test-rc-coverage-admission.ps1 gave **7 passed, 0 failed, 7 rows (not 8/8)**. The witness at the pin declares
  `-ExpectedRows 7` and every one of its 7 cases passes, so A6 holds at the pin.
  The 8th case, `class-scope-rejected-by-assertion` (ExpectedRows 8), is CARD-1732 on
  origin/feat/card-task-a98c2d1e (e6d4b39e5, 68843c370, 7ed514f47) and is **not on master**. Those commits
  change only the witness script, docs/release-gates.md and a plan doc: witness hardening against a
  class-wide manual row, with no policy or product change. I did not treat it as a blocker. If you want
  it on the line, it can come in only through a future cut (no push to a live rc).
  - `git diff --stat 5a95ae7a5..pin -- scripts/lib` is empty: scripts/lib is unchanged since the failed run's candidate.

## Tree verification (server2/Linux)

The whole-solution build was `dotnet build Antiphon.sln -p:UseAppHost=false` under one lease:
**0 errors**, 1887 warnings, **no NETSDK1047**. MSBuild time was 3m22s; with the 31 s slot wait the wall time was 237 s.

Smoke runs. Each run was `--no-build -p:UseAppHost=false` under its own lease, with TRX counts per class:

| Run | Classes | Result | Duration |
|---|---|---|---|
| A | NativeLinuxPolicyWitnessTests 3; TestClassificationPolicyTests 39, TestClassificationGuardTests 1, SlowTestRegistryMergeTests 5, SlowTestTripwireTests 2; MechanicalPcContractTests 11 (the mechanical instruction contract), TaskPlatformGuidanceTests 5 | 66/66 | 2m13s |
| B | C0824DesktopLegacyReleasePolicyTests 123, C0824DesktopLegacyReleaseSettingsTests 32; C1677RetentionServerTests 71, C0824WorkspaceSlotServerTests 24; LandingRemovalPolicyControlTests 118 + 2 skipped | 368 passed, 2 skipped, 0 failed | 2m36s |
| C | GhostSeatReleaseTests 24, ChannelBridgeTests 41; AgentTaskLandSourceFreshnessTests 59, LandSourceIdentityTests 22, LandingSourceFreshnessTests 23; RcQualificationWatcherScriptTests 2 | 171/171 | 4m15s |
| D (SessionRunner.Tests) | BuildSlotBroker 17, Settings 2, Metrics 5, MaxHold 6, Endpoint 7, EndToEnd 3; C0824WorkspaceSlotCustodyTests 19 | 59/59 | 33.6 s for the whole run, inside the 60 s budget |
| E | ReleaseGatePublicationAuthorityTests C599A_ExecutionLedger* (run once) | 6/6 | 17m28s |

- The 2 skips in B are C665_LockedFileMidDeleteResumesOnLaterPass and C721_HeldHandleDuringCleanupStaysRegisteredOrRecorded.
  Both report "Sharing-mode locks are a Windows file-system behaviour" and skip on Linux by design.
- ExecutionLedger durations against the 300 s limit: Joins 232.9 s, ExecutionLedger 212.3 s, Markers 180.8 s,
  Chunks 173.8 s, Receipt 145.4 s, Counts 91.7 s. All are under 300 s. Headroom is 22% on Joins under server2 load.

All bin/obj outputs were deleted afterwards and no land-graph canary was left behind.

## Master since the previous cut 5a95ae7a5 (186 commits, no merges)

By the first area each commit touches: PRODUCT (server/src/client/tools) 59, tests-only 103, scripts-only 7, docs-only 17.

PRODUCT-affecting changes:
- CARD-1713 build-slot lease cap: 959349dba caps leases at an absolute 48 h grant age, and ee09ffd85 normalises a zero max hold to 48 h (runner SessionRunner:BuildSlots:LeaseMaxHoldHours, default 48). 240231e86 (CARD-0851): a pid-mode lease survives the TTL while its holder runs.
- CARD-1715 status_untrusted: 4630cf9f9 / a77bae94d explain and surface untrusted status at both post-reset land sites and in task detail.
- CARD-1719/1721 ghost seat release: d10e1c51a, 91a649074, cff07f97e.
- CARD-1723 channel availability: 6a8738b4f witnesses and 534e3e66a (retain workers, filter scan pages, escalate repeated scan failure).
- CARD-1716 wake worker: 95e3e5e9c, where the inbound wake waits at most 1 s for a session queue lock. Same pattern in CARD-1709 e5b680ff0 (completion recovery).
- CARD-1591 S1 channel withhold incidents: 8a1a60f68, 4c475317e, e265c59fb, plus two migrations.
- CARD-0824 S5b-1 ref-only desktop coordinates (feature OFF, no producer): 7bf9f88df, a779b65b2, 22a6472af, 77d7f7871, ea89b8436, plus two migrations.
- In-process libgit2 reads for land, park, worktree and workspace: fceeab72a, cc8845f03, 8e27e49d0, 9ed40636a, d6b664811, 6594ade3b, acc8b2f5a, 92264fa89, c0deda487, f205fdcec, d71a4d60f.
- c448 / long-path / C1714 removal fixes: e05d39d56..d9904b1dc, d95d3f2cd. C0824 S4a e9d783769 (holds a tree that git status could not fully read).
- CARD-1718 retention secret-floor protection: aa729ee2c, cfcfa33cd, 0963befb7, 16d664e4e. CARD-1710 surrogate-safe Detail cut: db19cbbfc. CARD-1720 checkpoint pump deadline: e5d4dc06f..98991a93c.
- Not in this cut, as expected: the S4a hardening stack and the CARD-1708 salvage refs (no CARD-1708 commits in the range).

## Deploy note: migrations

New since 5a95ae7a5 (rc/2026-11-06), 4 migrations:
20261105024334_ChannelInboundInputWithheld, 20261105052528_ChannelQueuedWithholdScanIndex,
20261105110702_Card0824RefOnlyDesktopCoordinates, 20261105121558_Card0824RefOnlyDesktopCleanup.
The desktop server (`/api/version`) currently runs 919d8d42f (rc/2026-11-03), so it would apply **5** migrations at startup:
the four above plus 20261103210620_Card0824BranchDeletion. A host on rc/2026-11-05 or rc/2026-11-06 applies 4; a host on rc/2026-11-04 applies 5.
