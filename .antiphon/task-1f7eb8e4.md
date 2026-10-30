# Review 1f7eb8e4: CARD-1609 plan re-review, round 2

Subject: `docs/superpowers/plans/2026-10-29-card-1609-content-derived-volume-retirement-plan.md`
on `feat/card-task-02c3c411`, tip `50ebdf063661b1234355be1409125079b8995a60`. This review was
read-only and pushed nothing except this report. Code was read at master `98fc3ceb6` and
rc/2026-10-27 `ade542225`. The four touched production scripts are byte-identical on master and rc
(verified).

## Verdict

**Not ready for Code. Next: plan.**

The round-1 fixes are mostly real:

- the narrow seed classifier;
- the list-all preview;
- the D-19 retry and partial journal;
- the C849 zero rule;
- CARD-1607 test disposition;
- the H7 real-path baseline red;
- the skip roster;
- an importable table.

Four defects remain:

- D-1: the retained-volume deploy gate is placed after an effect it must precede.
- D-2: the plan never names the fate of existing tests in the code paths it changes.
- D-3: the CARD-1615 landing order is missing.
- D-4: the fixture classifier does not bind the whole commit object.

## Checks run

- **Importer.** `scripts/build-slot.ps1 -Label c1609-rr2-import -- dotnet run --project
  tools/Antiphon.Checkpoints --property:OutputPath=bin-c1609rr2/ -- import --plan <plan> --out
  .antiphon/c1609-rr2.yaml` exited 0 and imported 14 rows. The only warning is the expected CP-11
  duplicate build. All `EstimatedMinutes` are whole numbers.
  - The C1210 row (CP-12) and the DockerStackDocumentationTests pins row (CP-13) are present.
  - Row Min totals reconcile:
    - CP-3: 25 = C1-C14 + J1-J10 + H7.
    - CP-4 and CP-11: 12 = H1-H6 + W1-W6.
    - Linux lane: 273 = 25+12+30+132+52+10+9+3.
    - Windows lane: 17 = 12+3+2.
    - Red group: 4 = 2+1+1.
    - Whole manifest: 294.
- **Selected existing methods.** All 25 exist exactly once at master:
  - the 10 in CP-8 (RollingVolumeRecycleScriptTests lines 344-2841);
  - the 7 in CP-9 (RetiredTempContainerHostTests);
  - C1594_Antiphon_evidence_gitdir and C1335_Mirror_owner_stamp_in_linked_admin;
  - C1105_Git_audit_hidden_locations;
  - the 3 C1210 methods in WindowsUnitPlatformAdmissionTests;
  - the 2 DockerStackDocumentationTests pins.
- **Counts carried from round 1.** R3=30, R4=132 and R5=40+12 were verified in round 1 at this
  master and are unchanged by this branch.
- **Skip roster.** `WindowsUnitSkipRoster.cs:186-197` holds the entries the plan names: the four
  historical-image methods, the two CARD-1607 methods, and C1105_Temp_cleanup.
- **Merge simulation.** `git merge-tree --write-tree origin/master HEAD` is clean.
- **Cleanup.** The build outputs `src/Antiphon.HostCleanup/bin-c1609rr2` and
  `tools/Antiphon.Checkpoints/bin-c1609rr2`, and the generated YAML, were deleted.

## Brief items

### (1) Fixture classifier (D-17, plan :453-481)

The classifier checks content, not just metadata. The facts check out against
`tests/Antiphon.Tests/TestHelpers/ReportBudgetWorkspace.cs:112-123`:

- `init -b main`;
- `seed.txt` = `benign fixture` and `.gitignore` = `.antiphon/\n`;
- `-c user.name=Fixture -c user.email=fixture@example.invalid commit -m seed`;
- the path is `<checkout>/.antiphon/test-output/card-1605/template`;
- per-case copies are deleted on dispose.

Adversarial repos, tested against the spec as written:

| # | Repo | Verdict |
|---|---|---|
| A1 | Exact path, no remote, one root commit by `Fixture`, message `seed`, but `seed.txt` holds real work | Refused (tree bytes) |
| A2 | Exact seed commit, plus an untracked file under the fixture's own ignored `.antiphon/` | Refused ("no extra content"); the fixture's own `.gitignore` grants nothing |
| A3 | Exact seed at main, plus a second Fixture-authored commit on another ref or reflog-only | Refused (sole parentless commit; other refs and reflog sides rejected) |
| A4 | Exact seed, but `objects/info/alternates`, a gitfile/linked admin, or `core.worktree` pointing at a real repo | Refused: alternates and linked admins explicitly; core.worktree via D-5's "unrecognized configuration refuses" |
| A5 | Exact tree, author, committer and message, but the commit object carries extra headers (`gpgsig`, `encoding`, `mergetag` or a custom continuation header) holding a payload | **Admitted as written** (D-4 below) |
| A6 | Exact seed copied to `/tmp/x/.antiphon/test-output/card-1605/template` with no inventoried outer checkout | Refused (needs an independently inventoried outer checkout) |

### (2) Stale FETCH_HEAD (D-16)

The publication proof is not offline, by design. The offline helper only lists every FETCH_HEAD
entry and its OID. The host then proves each OID against a fresh origin advertisement and an
origin-only scratch graph (D-6).

- A stale entry that names a deleted, unlanded tip still refuses, with the full listing.
- A mixed multi-entry file is tested (C12).
- This is sound.

### (3) Gone-tracking (D-18)

- Refusal is content-based.
- Recovery is an origin archive branch, done by the operator; nothing is pushed or pruned for them.
- The CARD-1105 replay-3b operator question is carried.
- C14 has a positive control.
- This is sound.

### (4) Census retry

Verified against the code:

- The temp deletes are in `c1008_recycle`: `c590-remote.sh:9026` (`compose_temp down -v`) and the
  per-name `docker volume rm` loop.
- `c1008_status_proof` (`:6745`) runs before each name.
- The task census is `c1008_tasks_collect` (`:6442`, 3-attempt Drift-only loop at `:6701`), run in
  the shell against `C604_SERVER_ORIGIN`.
- The wrapper refuses `cause=Unstable` at `deploy-server2.ps1:868` and retries Drift at `:879`.

So D-19 can be implemented where the deletes happen:

- at most 4 attempts with 2/5/10 s backoff, inside a 180 s gate;
- a stop before the next intent, with PartialRefused;
- no reuse of CARD-1371's post-container Drift allowance;
- resume from the durable intents (D-8.7);
- J6 injects failures before the first delete, between the state and dind deletes, and on resume.

The resume is idempotent: an absent target with an intent is recorded as `removed-observed`, a
same-name new incarnation refuses, and nothing is deleted again by name alone. This is sound.

### (5) Retained-volume gates (D-20)

- The intent is right: refuse, never adopt.
- C849 Retired keeps `tempPrivateVolumes=0`.
- The three caches are rejected by exact name.
- Retire-temp refusal returns nonzero and leaves gate 9 incomplete.
- **But the gate is in the wrong layer** (D-1).

### (6) Offender preview (D-15)

- One pass, the same traversal as one audit.
- Typed JSONL plus `proof.md`.
- `complete=false` lists every unvisited scope.
- A redacted-path mapping is kept host-local.
- No added complexity in minutes: it is the same 13-60 minute class of pass, with host proof for
  every identifiable repo.
- Residue: an owned helper is joined, and an operation directory is written. Its durable
  Pending/Refused records interact with D-20 (see D-1(b)).

### (7) CARD-1607 tests and H7

- Both methods are retained by name and rewritten, with every vector listed. A v1 adapter vector
  is kept. They are in CP-8 (R6).
- The H7 base red is traced in code:
  - the helper's `ls-remote` returns status 3, so `c1008_apply_host_proof` runs (`c590-remote.sh:7762`);
  - the host lines advertise only B;
  - `cat-file` reports B missing, so `present=()`;
  - `fetch_origin` fails, so `fetched=()`;
  - the result is `refuse_unknown origin-present 0` (`:7889`).
- That matches the plan's stated red reason.
- `Get-C1008RetireProofExports` (`c590-real.ps1:342`) and `Write-C1008HostOriginProof`
  (`c1008-host-proof.ps1:112`) exist.

### (8) The 14 rows

Covered under Checks run. The rows import, and the counts and roster rows are present.
Their adequacy is not settled (D-2).

### (9) Ordering

- **rc/top-up 38:** the plan says to port to rc after top-up 38 and not splice into it, with
  per-commit simulation. It touches no definition files and claims no land-graph approval
  regeneration. This is sound.
- **CARD-1615:** the plan never mentions it (D-3).

## Defects

### D-1: The D-20 deploy gate runs after the wrapper's retirement clear (plan :536-545, S3 :663, S4 :664)

**Where.**
- D-20 puts the four-name census in `case_deploy_temp_runner` "before seed/build/Compose or
  retirement-clear effects".
- But the retirement clear is a wrapper effect. `deploy-server2.ps1:1196` POSTs
  `/drain/clear` on the retired `server2-temp`, then re-drains it with `retireWhenIdle=false`.
- Only after that does it run `runner-cache-seed` and `deploy-temp-runner` (`:1214-1215`).
- The wrapper's `Assert-TempProjectAbsent` (`:1139-1168`) checks containers only.
- S4 lists no deploy-temp change in `deploy-server2.ps1`.

**Failure.** A v2 retire-temp refuses, or ends PartialRefused, with volumes retained. The next
rollout's deploy-temp then:

1. clears `retiredAt`;
2. reaches the host case, which refuses `TempVolumesRetained`.

The pending operation can no longer resume:

- `c1008_status_proof` (`c590-remote.sh:6745`) requires `retiredAt` equal to the bound instant,
  plus `retireWhenIdle==true`.
- It now refuses `TempRunnerNotRetired`/`TempRunnerRetirementChanged`.
- D-3/J3 bind the retirement epoch.

So the recovery that D-20 names (Preview/Resume) is unreachable without a new drain/retire cycle,
and the retained work is stranded in a false refusal.

**(b) Which records block is undefined.** D-20 also blocks on any "pending/refused" record, but
never says which records count. Scenario:

1. A DryRun preview is Refused and lists its offenders. The preview made no effects.
2. The operator recovers the offenders, and a later operation completes; the volumes are now absent.
3. The stale Refused preview still blocks every deploy-temp.
4. There is no supersede or close verb to clear it.

**Fix.**
- Add a read-only wrapper preflight in deploy-temp, before `/drain/clear` and before any host case.
  It censuses the four names and the operation journal.
- Keep the host recheck under FD 8.
- State that only apply-phase states with durable intents, or a present volume, block. Preview-only
  records with no effects are inert, or are superseded by a later completed operation for the same
  incarnations.
- Extend W6 to assert no `/drain/clear` POST and no host case on refusal.

### D-2: Existing tests in the changed paths have no disposition and no row (Implementation slices :653-704, Checkpoints)

S3 changes the C994 temp preflight, the `c1008_recycle` temp branch and `case_deploy_temp_runner`.
S4 changes the wrapper retire-temp output. These existing tests are neither selected nor
dispositioned:

- **`RetiredTempContainerScriptTests` (15 methods; the plan never names the class).**
  - `C994_Preview_reports_pending_volume_proof` asserts
    `volumeProof=pending-confirmed-absence auditPending=true`, which D-8 replaces.
  - Also `C994_Retire_recovers_exited_and_absent_temp`, `C994_Resume_never_runs_container_cleanup`,
    `C994_Cleanup_receipt_is_required_before_recycle`, `C994_Verifier_manifest_is_strict` and the
    bridge/strict-admission methods.
- **`RemoteScriptContractTests` methods that extract and run the `case_deploy_temp_runner` block
  with stubs:**
  - `C0995_Deploy_temp_checks_host_absence_before_checkout`
  - `C849_Deploy_temp_names_saved_import_when_seed_is_missing`
  - `C849_Deploy_prepares_and_verifies_before_acceptance`
  - `C849_Deploy_temp_observes_inaccessible_docker_mountpoints_with_sudo`
  - `C849_Docker_volume_paths_have_no_bare_host_reads` (it scans that block for bare mountpoint
    reads; the D-20 census and the D-5 state/dind walker must stay `sudo -n`)
  - `C849_Seed_publishes_complete_payloads_before_its_marker` (asserts the exact wrapper case list
    `runner-cache-seed,deploy-temp-runner,verify-runner-caches`, which D-1's fix changes)
  - `Temp_deploy_uses_its_own_project_and_the_inspected_grok_store`
  - `C1067_Nested_lane_sudo_guard_is_sensitive`
  - `C912_*` block scans

  A new helper call inside an extracted block becomes "command not found" in these harnesses.
- **11 unselected `RetiredTempContainerHostTests` methods** that pin exactly what D-9 changes:
  - `C994_Production_mount_topology_is_proven`
  - `C994_Container_service_and_state_are_restricted`
  - `C994_Host_preview_never_mutates`
  - `C994_Older_retirement_receipt_does_not_block_cleanup`
  - `C994_Census_and_inspect_fail_closed`
  - `C994_Rollout_lock_covers_final_census`
  - `C994_Removal_failure_and_crash_retry_preserve_progress`
  - and others
- **`DockerStackDocumentationTests.Recycle_census_preflight_is_documented` and
  `Lands_proceed_during_rollout_is_documented`.** They pin the `cause=Unstable` census paragraph that
  D-19 changes for v2. CP-13 selects only 2 of the class's methods.

**Failure.** Code lands with these red, or rewrites them silently outside the closed checkpoint
list. Either way the C994 topology and deploy-temp ordering vectors lose their witnesses.

**Fix.**
- Name each affected method's fate (retain, rewrite or relocate).
- Add rows. Whole-class rows are acceptable where they are cheap:
  - RetiredTempContainerScriptTests;
  - RetiredTempContainerHostTests;
  - DockerStackDocumentationTests (string-only, seconds);
  - an exact deploy-temp method list in RemoteScriptContractTests.
- Recompute the Min/294 totals, and re-import.

### D-3: The CARD-1615 landing order is missing (Master/rc port and ordering :984-1030)

The CARD-1615 plan (`feat/card-task-28be9547` @ `ddd9946b3`, its D-11) orders the work
"**CARD-1609 first, CARD-1615 second**". It also changes the same areas:

- the bound-land census admission (a self-held land);
- `deploy-server2.ps1`;
- `docker-stack.md:473/513` (the manual-retire evidence path moves to `<controller-root>`).

CARD-1609 never mentions CARD-1615. Its D-19 keeps "the existing separately bounded bound-land
wait", and D-10 uses the canonical `.antiphon/rolling-server2/manual-retire-<UTC>` path.

**Fix.**
- State the reciprocal order: 1609 lands first, on master and then rc after top-up 38; 1615 Code
  starts from the landed 1609 SHA.
- Say that CARD-1609 Code must not take on 1615's self-held-land or controller-root changes.
- Say which plan owns the manual-retire evidence path wording, so S5's docs do not conflict.

### D-4: The seed classifier does not bind the whole commit object (D-17 :467)

**Where.** The classifier validates author, committer, message and tree, and lets "only commit
timestamps vary". It does not forbid extra commit headers.

**Failure.** A5 above: a seed-identical commit that carries a payload in a
`gpgsig`/`encoding`/custom continuation header is admitted and deleted.

**Fix.** Require the exact canonical commit byte shape: `tree <seed-tree>`, no parent,
author/committer `Fixture <fixture@example.invalid>` with only the epoch and timezone varying, a
blank line, then `seed\n`. Allow no other header. Add an extra-header vector to C13.

## Advisories

- **A-1.** Fixture leftovers outside the card-1605 template (crashed-test repos in a retired
  runner's `/tmp`) stay refused by design. The first read-only preview will measure how often;
  record that before enabling apply.

## Scope

- This was a plan design review. No Code exists, so there are no Unit-lane or integration rows to
  rerun. The executable checks were the importer run and the static roster and code reads, all done
  here.
- `ordinaryScopeCompleted` is None.
