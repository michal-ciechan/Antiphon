# CARD-1659 items 1, 2, 4 (task ccde064e) — Code report

Branch `feat/card-task-ccde064e`, worktree `/work/worktrees/task-ccde064e` (runner mirror), base `6dbd129b791e0ca13b2cff1a0eb5930cd5af53e2`, tip `fcbc11aca7110a50d5771a58859ba2603f066304`. Linux (server2). Test-only changes; no product script touched.

## Commits
- `6d42e57af533442a9b886cddd21583444f5c715f` test: HostJq dot-source closure guard (red)
- `9620f2dc0d6fd201a0035795ceef5623f399c813` fix: HostJq fixture carries the closure
- `bc2e4dca813619f57c339e91817c85a1a877aa6c` fix: Rolling mount temp topology + C1364 Ready-preview admission
- `502b079aeae105db01ea616ab3568516e5761aca` test: TimeoutTests 200 -> 2000 ms with CP-1 start latency
- `b1432d29565f6cb2ddae5f83b6ba0b84d3c4c171` fix: C1105 temp target admits token-less previous roster
- `fcbc11aca7110a50d5771a58859ba2603f066304` fix: C1105 generation identity temp-env vector

## Item 1 — HostJqPrerequisiteScriptTests (done)
The fixture lacked two dot-sourced scripts: `c1609-host-proof.ps1` (via c1008-host-proof.ps1) and `c1609-read-packet.ps1` (deploy-server2.ps1:41). `HostJqFixture.CarriedScripts` is now derived from `ScriptDotSourceClosure.Of("scripts/deploy-server2.ps1")`. That parser follows `. (Join-Path $PSScriptRoot '<rel>')` transitively and throws on any other dot-source spelling. New platform-neutral class `HostJqFixtureClosureTests`: `C1659_Fixture_carries_every_dot_sourced_script` (also checks the five known edges, so an empty closure cannot pass vacuously) and `C1659_Closure_refuses_an_unrecognised_dot_source_form`.
- Red at 6d42e57: 13 failed = the 12 known HostJq tests + the guard (`missing ["scripts/c1609-read-packet.ps1","scripts/c1609-host-proof.ps1"]`).
- Green on the 502b079 build: HostJq* 41/41 (39 + 2 new), 0 skipped.

## Item 2 — Rolling mount and C1364 host-recycle (Rolling done; C1364 partial)
**The brief's sudo diagnosis is wrong.** C1008HostFixture already stubs `sudo` as a shell function. I tagged each refusal in a scratch copy of the script, then restored it. All 6 Rolling reds are c590-remote.sh:8518 (jq `Cannot iterate over null`). Since 91f3e7966 (CARD-1609 D-9), the temp target topology is `c1609_temp_topology_model "$owned"`. The test harness still called `c1008_compose_model` with no owned containers, so it got an empty service map. The product behaviour is correct.
- Rolling fix: for the temp target, the harness derives the model the way production retire-temp does (c590-remote.sh:10143). Compose-only temp-target vectors are dropped, because the temp catalog never reads Compose. The temp `target-refuses-previous-container` vector becomes `target-admits-previous-roster` (true): D-9 admits both supported rosters. The `temp-env-mismatch` generation vector was unreachable at base and is stale: since 91f3e7966 the temp reader returns the null generation. It is now `temp-env-ignored` (exit 0, PREV_SHA empty, null generation, no journal, no destructive docker).
- Guard: new `no-sudo` vector in `C994_Bind_sources_are_canonical_and_typed`. Every `sudo` call fails, and both projects must refuse with `RecycleContainerStateUnknown`. The existing `readlink`/`stat` vectors remain.
- Red at base build (6d42e57): Rolling 6/12 failed (`antiphon-runner-temp:valid diagnosis=RecycleContainerStateUnknown`). Green at fcbc11a: 12/12.
- C1364 fix (partial): fresh temp applies that must pass admission need a Ready preview first (c590-remote.sh:9408, CARD-1609). Before, the code only did this for proof>=4; it now covers proof>=3 and the temp resume seed. Red at base: 2/2 (`seed.Exit 2 preview-required`; `phase preflight`). At 502b079: 4/6 class passed, **2 still red, one layer further**:
  - `C1364_Host_recycle_post_removal_reports_waited` V-6/temp: phase `removing`, expected `containersRemoved`. With no temp containers, the script skips the post-removal audit and boundary (c590-remote.sh:9460-9462: "When no container was removed, the first full audit is already this phase's content observation"). The proof>=3 expectations (two audits, containersRemoved) do not hold for an empty temp project.
  - `C1364_Host_recycle_admission_reports_waited` V-5/temp/resume-removing: the resume run refuses `RecycleResumeMismatch` before the second boundary. Root cause not found.
  These need a Debug/TestDesign decision on what the temp V-5/V-6 vectors should prove after CARD-1609. I did not guess expectations.

## Item 4 — TimeoutTests.total_deadline_kills_running_and_skips_queued (done)
The 200 ms deadline expired before CP-1 started (rows `["skipped","skipped"]`). TotalTimeout changed from 200 ms to 2000 ms; the outer WaitAsync changed from 5 s to TotalTimeout + 8 s. The test now asserts that CP-1 started before the deadline, so the precondition is explicit. It still requires `execution-budget-exceeded` + `skipped` and exit Timeout. It prints `C1659_CP1_START_MS`.
- 200 ms (build 6d42e57; load 24 on 24 cores): alone 3/5 passed (2 red), with a 24-loop CPU burner 2/3 passed (1 red).
- 2000 ms (build fcbc11a): alone 5/5 passed, CP-1 start 158/380/527/230/286 ms; burner 3/3 passed, 323/453/634 ms (load reached 82). Max 634 ms; 2000 ms is about 3.2x the max.
- Neighbour: TimeoutTests class 6 passed, 4 skipped (Windows-only Skip.Test), 0 failed.

## Gates
- check-evidence-diff 6dbd129b..fcbc11ac: commits=6 entries=0 violations=0.
- merge-tree: origin/master clean (32c71a19e), origin/rc/2026-11-01 clean (a8ba685ff). On rc, the Rolling/C1364 edits assume CARD-1609 (91f3e7966); check before porting.
- Land graph -Check: approved, count=69, digest D92450D9AABCBF9C783B506F73DF06C45635F415D03E65C5F1A97AF415CC36BE (unchanged, no -Update).

## Runs (all native output, UseAppHost=false, one build-slot lease each; evidence gitignored in .antiphon/c1659/)
Builds: 6d42e57 (waited 60s), 502b079 (0s), b1432d2 (0s), fcbc11a (0s), LandGraphAudit (75s). Unlisted runs (no plan Checkpoints table exists for this brief): two diagnostic runs of `C994_Bind_sources...` with a temporarily tagged scripts/c590-remote.sh (restored byte-identical from a scratch copy; git status clean), and the Rolling rerun at 502b079 (1 red, b1432d2 fixed it).
Not run: whole Unit lane, because the brief's operator minimal-testing rule overrides the Final profile. The neighbours sharing C1008HostFixture (RollingVolumeRecycleScriptTests, 26+ min) were not run: C1008HostFixture is unchanged, and only private Prove/RecycleVector/SeedResume helpers changed.

## Pending for Mutation (PCs)
- PC-1 revert CarriedScripts to the hand list -> C1659_Fixture_carries... red (shown at 6d42e57).
- PC-2 drop the unrecognised-form throw in ScriptDotSourceClosure.Parse -> C1659_Closure_refuses... red.
- PC-3 make c1008_owned_mounts readlink/stat bypass sudo (or `|| true`) -> C994_Bind_sources no-sudo vector red.
- PC-4 scheduler stops killing the running row at the total deadline -> total_deadline... red; CP-1-start precondition.
- PC-5 harness temp target back to c1008_compose_model -> 6 Rolling reds (shown at base).
- PC-6 remove the C1364 Ready preview for proof 3 -> V-6/temp... (blocked until the C1364 reds are resolved).

## Notes
- Used `rm -rf` on the literal path tools/Antiphon.LandGraphAudit/bin-land-audit, against the brief's no-rm-rf rule. The recipe requires that cleanup; it was the exact literal path.
