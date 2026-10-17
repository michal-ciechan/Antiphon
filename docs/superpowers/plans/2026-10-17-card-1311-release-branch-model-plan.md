# CARD-1311 release branch model

Plan for the operator's long-lived release line. This card's slices land on **master only**.
Card revision 1 (2026-10-16) labels the work "new feature, refactor or release tooling;
lands on master only, not on the rc/2026-10-15 branch". Do not retarget `-Land` at
`rc/2026-10-15`, do not merge this branch onto that line, and do not restart AppHost
or any runner.

Task branch `feat/card-task-4ce63b7a` is fast-forward-only from `3adbe0d5efe214075b1325cd55081e3524cd148f`.
Do not rebase, amend, or merge `master` into it. Landing rebases onto master later.

Platform: no `-Runner` and no `-Platform`. Checkpoints are the Linux `Antiphon.Tests`
lane. `GET /api/runner-defaults` at plan time was revision 2 with `globalRunnerId`
`server2`. That runner was stale and draining; `server2-temp` was the dispatch-eligible
Linux host. Those facts are not a pin.

No slice adds or changes a session that waits for input.

## Decisions

Defaults below are what Code executes if the operator accepts them. Rejected
alternatives sit on the same row. Open confirmations are in [Operator questions](#operator-questions).

| Id | Default | Why | Rejected |
|---|---|---|---|
| D-1 | Keep accepting legacy `release/rc-YYYYMMDDTHHMMSSZ` refs, journals, and final-tag publication. New cuts do not allocate one. An omitted `-Ref` refuses `rc-ref-required`. | `C599_CreditIdentity`, `C599_CandidateIdentity`, and `C599_CutRecovery` pass an explicit stamp ref. Removing the shape turns that suite red and strands an in-flight stamp journal. | Accept only `rc/YYYY-MM-DD`. Generate a stamp when `-Ref` is omitted. |
| D-2 | A long-lived ref is `rc/YYYY-MM-DD` (optional `refs/heads/` prefix). Its candidate id is `rc-YYYY-MM-DD-` plus the first 12 hex digits of the tested SHA. The candidate root stays `candidates/<candidateId>` (`scripts/lib/release-gate.ps1:151-154`). | One id per tested tip keeps `Get-ReleaseGateTagProposal`'s "different SHA, same candidate id" refusal (`:414`, `:430-431`, `:449-450`) intact for stamp ids, and stops a moved tip from overwriting the previous journal and authority. | One candidate id per branch. A second SHA under that id allocates the next tag. |
| D-3 | `publish-release.ps1` on a long-lived id writes only `vYYYY.MM.DD.N-rc.K` and a GitHub pre-release. `scripts/promote-release.ps1` writes the final `vYYYY.MM.DD.N` at that same peeled SHA. Neither deletes or moves a tag. Stamp ids still publish a final tag directly, as today. | The two live tags were applied by hand with no `-rc.K` parent. Promotion has to be an explicit act, and the legacy publisher tests must keep creating final tags. | One script that publishes the final tag whenever the run is green. Delete the pre-release tag on promotion. |
| D-4 | While the UTC day matches, the next new SHA on that day uses the same `N` and the next `K`. After a final tag exists for that `N`, or the UTC day changes, the next pre-release starts the next free `N` at `K=1`. The same SHA reuses its reservation. | The operator's names are `vYYYY.MM.DD.N-rc.K` promoted to `vYYYY.MM.DD.N`. A new `N` per attempt would make the final name unstable. | A new `N` for every pre-release. `K` as a global counter with no `N`. |
| D-5 | `remote-candidate-moved` stays a hard refusal to tag or promote when the live ref tip differs from the tested SHA (`scripts/lib/release-gate.ps1:593`). It does not forbid the branch to move after a successful tag. | "Never tag a moved ref" is a property of one run. The line is supposed to move when the next fix lands. | Freeze `rc/<date>` at its first SHA. Retest a moved ref under the old candidate id. |
| D-6 | Authority and script digests stay per tested SHA. `Get-ReleaseAuthorityScriptHashes` (`scripts/lib/release-authority.ps1:249-257`) hashes the six script files in `$script:ReleaseAuthorityScripts` (`:49-56`). The discovery digest (`scripts/lib/nightly-coverage.ps1:651-660`) stays a digest of required and excluded UIDs. Neither gains the branch tip. | The tip is already the authority `sha` field. Folding it into the script hash would reject a later fix that did not change those files, and would churn qualification on every rc commit. | Hash `git rev-parse` of the rc ref into `scriptHashes` or the census digest. |
| D-7 | Docs describe the operator model in S1, including one sentence that S2-S4 have not landed yet. S4 deletes that sentence. Historical CARD-0599 plans and investigations are not edited. | A landed S1 tree must not pretend the scripts already accept `rc/<date>`. Old plans are the record of the stamp design, not the live contract. | Rewrite every historical plan. Leave `docs/release-gates.md` on the stamp model until the scripts change. |
| D-8 | AppHost and server2 run the current rc tip, never master. This card only documents that. It does not change `deploy-server2.ps1`, `restart-apphost.ps1`, or the Windmill cron. | The card label forbids landing this tooling on `rc/2026-10-15`, so the hosts that cut releases will not run these script edits until a later, separate merge onto the rc line. A host-SHA guard in the deploy scripts is a different card. | Make this card's land target the rc branch. Add a deploy refusal in these slices. |
| D-9 | `-Land` stays rebase plus fast-forward onto `MergeTargetRef ?? "master"`. No merge-commit publisher and no feature-size guard in this card. | See the ground-truth rows. Both are real follow-ups, and neither is a release-gate script change. | Teach `AgentTaskLandService` to merge rc into master inside these slices. Invent a line-count "feature" gate. |
| D-10 | Verification is the five checkpoint rows below. No full `C599` matrix, no E2E, no nightly profile run. | The operator asked for a minimal witness. The new methods call the production scripts. `C599_CreditIdentity` is the one legacy regression pin. | Run `ReleaseGatePublicationAuthorityTests` or the rc profile as the ordinary gate. |

## Operator questions

1. Keep legacy stamp acceptance (D-1)? Recommendation: yes.
2. Separate `scripts/promote-release.ps1` (D-3)? Recommendation: yes.
3. Leave land-to-rc merge commits and the feature-size guard unbuilt (D-9)? Recommendation: yes. File follow-up cards only if you want them; this plan does not file them.
4. Leave host activation as documentation (D-8)? Recommendation: yes.

## Ground truth

| The card or the brief assumes | What the tree does | Where |
|---|---|---|
| Candidate refs are only `release/rc-YYYYMMDDTHHMMSSZ`, immutable, with no repair commit. | The pattern is exactly that (`ReleaseGateCandidateRefPattern`, `:19`). `Test-ReleaseGateCandidateRef` (`:39-64`) rejects anything else, including `rc/2026-10-15`. `New-ReleaseGateCandidateRef` (`:66-70`) allocates a new stamp. An omitted `-Ref` uses it (`scripts/release-candidate.ps1:79`). | `scripts/lib/release-gate.ps1:6`, `:19`, `:39-70`; `scripts/release-candidate.ps1:4-13`, `:79-86`; `scripts/release-cut.ps1:6-16`; `scripts/lib/nightly-run-impl.ps1:174-183`, `:368-376` |
| Fixes land on the rc line, then a merge commit lands on master. | Land rebases and fast-forwards `task.MergeTargetRef ?? "master"` (`AgentTaskLandService.cs:655`, `:819`). `MergeTargetRef` is stored at task create from the request or the parent (`AgentTaskService.cs:1465-1470`). `scripts/delegate.ps1` has no parameter that sets it. | Those lines. `docs/orchestration-loop.md:414` still says fetch, rebase, verify, fast-forward, push. |
| `AddLandingTargetAndStageTiming` is a landing-target column. | The migration adds landing timestamps and `TargetCheckoutPath` only. | `server/Migrations/20260908144442_AddLandingTargetAndStageTiming.cs:14-48` |
| A task can already name a non-master target. | A stored `MergeTargetRef` of `release` is a base-selection input in `retained_empty_card_preview_names_explicit_destination`. It is not a release-line land and not a merge commit. | `tests/Antiphon.Tests/Application/AgentTaskDispatchBaseGuardTests.cs:875-893` |
| Tags are `vYYYY.MM.DD.N-rc.K`, promoted to `vYYYY.MM.DD.N`, and never moved. | The tag pattern is final-only (`:20`, `Test-ReleaseGateTag` `:365-373`). Tags are never deleted (`scripts/publish-release.ps1:28`; harness pin `scripts/test-release-gate.ps1:1551-1552`). No `-rc.K` tag exists on origin. Annotated `v2026.10.15.1` peels to `067b47e2dc903247f1a7c8aa539032cc4266e4ad`. Annotated `v2026.10.15.2` peels to `6ca859d006b0a3cac9a88dc6bd551c37e98b2217`. | `scripts/lib/release-gate.ps1:20`, `:365-381`; `git ls-remote --tags origin 'v2026.10.15*'` |
| Hosts run the rc tip, never master. Publish does not restart them. | `publish-release.ps1:40-41` and `release-status.ps1:6-13` already refuse restart and refuse ancestor-tag identity. `docs/docker-stack.md:5-7` and `:38` still deploy the canonical checkout HEAD and restart AppHost onto that HEAD. `docs/orchestration-loop.md:323-338` compares `GET /api/version` to canonical `HEAD` or `origin/master`. The orchestrator skill restarts onto the landed SHA. | `.claude/skills/antiphon-orchestrator/SKILL.md:172-175` |
| Full rc testing is change-aware (CARD-1313). | This tree has no `TestSetTiming` / change-aware selector. CARD-1313 is master-only and is not on this branch. The rc profile here is still the eight-suite profile in `docs/release-gates.md:72-92`. | This checkout at `3adbe0d5` |
| The release-gate doc matches the live tags. | `docs/release-gates.md:7-12` still says unqualified, no published tag, and no Windmill RC job. The two tags above exist. The Windmill definition is still marked not registered. | `docs/release-gates.md:7-12`; `scripts/windmill/README.md:20-27`; `scripts/windmill/antiphon-release-candidates.json:4` |
| `origin/rc/2026-10-15` is the branch this task was cut from. | Local base `3adbe0d5` was that tip at dispatch. At plan time `origin/rc/2026-10-15` was `5e5d5cc728949420647d15a932766e70755a6f01`. | `git ls-remote` |

## Inventory

Live statements Code changes, by slice. Line numbers are at `3adbe0d5`.

### S1, docs that state the stamp model or host-follows-master

| File:line | What it states |
|---|---|
| `docs/release-gates.md:7-12` | Unqualified; no published tag; do not describe the model as operational. |
| `docs/release-gates.md:20-22` | A candidate is an immutable cut of master. |
| `docs/release-gates.md:28` | RC ref is `release/rc-YYYYMMDDTHHMMSSZ`. |
| `docs/release-gates.md:42-44` | `rc-release` requires that candidate ref shape. |
| `docs/release-gates.md:48-50` | An older immutable candidate says nothing about the integration stream. |
| `docs/release-gates.md:134-137` | Cut from `origin/master`; publish a CalVer tag on complete-green. |
| `docs/release-gates.md:140-160` | Exact stamp ref; no repair commit; no moving a tested branch; fix on master, then cut a new candidate. |
| `docs/release-gates.md:162-181` | Publish only `vYYYY.MM.DD.N`; tags never deleted. The never-delete rule stays. |
| `docs/release-gates.md:207-211` | A release does not restart AppHost; the dev stack may lead the latest tag. |
| `docs/release-gates.md:340` | Failed RC: fix through master, recut. |
| `AGENTS.md:26` | Table row names "Release candidates" and points at `docs/release-gates.md`. The pointer stays; the row text changes. |
| `AGENTS.md:55` | Restart activation compares `GET /api/version` to source-root HEAD, with no rc-tip rule. |
| `docs/orchestration-loop.md:323-338` | Post-land activation compares the running SHA to canonical `HEAD` or `origin/master`. |
| `docs/orchestration-loop.md:414` | Land is rebase and fast-forward. Stays true (D-9). S1 adds that the rc-to-master merge commit is not this operation. |
| `.claude/skills/antiphon-orchestrator/SKILL.md:172-175` | After land, advance the canonical checkout to the landed SHA and restart AppHost. |
| `docs/docker-stack.md:5-7`, `:38` | Rolling rollout SHA is the canonical desktop checkout HEAD; restart AppHost onto the reviewed server change. |
| `scripts/windmill/README.md:20-27` | The RC job cuts `release/rc-<UTC stamp>` from `origin/master` and publishes a CalVer tag on complete-green. |
| `scripts/windmill/antiphon-release-candidates.json:4` | Same stamp-cut description, including "NOT REGISTERED". |

`docs/nightly-watchdog.md` has no stamp-candidate sentence. `server/Bundles/orchestrator.md` does not describe release refs. Do not edit either for this card.

### S2, scripts and tests that admit only the stamp ref

| File:line | What it states |
|---|---|
| `scripts/lib/release-gate.ps1:6`, `:19`, `:39-70`, `:72-98` | D-3 comment, pattern, parser, factory, and credit kind. |
| `scripts/release-candidate.ps1:4-13`, `:79-86`, `:164` | Create-only stamp branch; conflict if the ref already points at another SHA. |
| `scripts/release-cut.ps1:6-16` | Cut `origin/master`, then a new candidate after a master land. |
| `scripts/lib/nightly-run-impl.ps1:179`, `:369` | Refusal text and the moved-candidate comment. The refusal behavior in `:368-376` stays (D-5). |
| `scripts/publish-release.ps1:4-14` | Immutable candidate; a legacy candidate with no authority must be recut. Keep the authority requirement. |
| `scripts/test-release-gate.ps1:35` | `$CandidateRef` stamp fixture. Stays. |
| `scripts/test-release-gate.ps1:925-926` | Dashed and impossible stamps earn nothing. Stays. |
| `scripts/test-release-gate.ps1:1270-1272` | `release/current`, `master`, `release/rc-2026`, and `refs/tags/v1` refuse. Stays. |
| `tests/Antiphon.Tests/Scripts/ReleaseGateRunTests.cs:23-25` | Stamp ref earns `rc-release`; a stamp ref on the nightly trigger earns nothing. Stays, and CP-2b re-runs the method. |
| `tests/Antiphon.E2E/ReleaseGateReportDeliveryTests.cs:143` | Fixture gitRef is the stamp. Leave it. It is still a valid legacy ref under D-1. |

### S3, final-tag-only publication

| File:line | What it states |
|---|---|
| `scripts/lib/release-gate.ps1:8`, `:20`, `:365-381`, `:392-444` | CalVer final tags; reservation is one SHA per candidate id; the next tag is `vYYYY.MM.DD.N`. |
| `scripts/lib/release-gate.ps1:707-714` | `gh release create` has no `--prerelease`. |
| `scripts/publish-release.ps1:16-24` | Reserves and pushes `vYYYY.MM.DD.N` only. |
| `docs/release-gates.md:167-174` | Same eight-step final-tag sequence. S1 already rewrites the doc; S3 makes the scripts match. |

### S4, authority bound to one frozen candidate

| File:line | What it states |
|---|---|
| `scripts/lib/release-gate.ps1:593-594` | Tip SHA other than the tested SHA is `remote-candidate-moved`; an unresolved tip refuses. Keep both. |
| `scripts/lib/release-authority.ps1:5-18`, `:49-56`, `:249-257`, `:299-319`, `:530-532` | Authority is the policy blob at one pinned SHA, plus `candidateRef` and `scriptHashes` of the six files. |
| `scripts/lib/nightly-coverage.ps1:651-660` | Census digest of UIDs. Do not add a tip. |
| `scripts/test-release-gate.ps1:1346-1356`, `:2453`, `:2861`, `:2865` | Moved candidate refuses; authority fixtures use a stamp ref. Leave the stamp fixtures. |
| `tests/Antiphon.Tests/Scripts/ReleaseGatePublicationTests.cs:55-56` | `C599_PublicationGate` names the moved and unresolved refusals. Do not drop those rows. |

### Historical, do not edit

These describe the CARD-0599 design as it was written. They are not the live contract.

- `docs/superpowers/plans/2026-09-22-card-0599-release-gate-activation-plan.md:87`, `:113-115`, `:124`, `:260-262`
- `docs/superpowers/plans/2026-09-23-card-0599-release-recovery-and-cards-plan.md` (candidate-ref uniqueness and `publish-release.ps1` authority)
- `docs/superpowers/plans/2026-10-15-card-1281-pipeline-speed-plan.md:126`, `:213`
- `docs/investigations/2026-09-22-card-0599-release-gate-lighter-per-change-tests-plus-rc-releases.md:204`

`docs/investigations/2026-10-17-card-1310-diagnosis.md:12` and the 2026-10-16 rc plans already say fixes land on `rc/2026-10-15`. Leave them. They are the new practice, not the old model.

## Slices

Ship S1, then S2, then S3, then S4. Do not dispatch them in parallel: S2-S4 share `scripts/lib/release-gate.ps1` and `scripts/test-release-gate.ps1`, and S4 deletes the S1 lag sentence in `docs/release-gates.md`. Each slice's green tip is landable on master without the later slices. The failing-test commit inside a slice is not a land tip.

PowerShell stays ASCII-only. New scripts use the existing `-SeamsPath` / `ANTIPHON_RELEASE_GATE_TEST_MODE` admission. Do not hard-code `rc/2026-10-15` in a script. Tests may use that date as a fixture.

Red-first, every slice: commit the new test, run that slice's filter, confirm it fails on the assertion named below, then commit the production edit and rerun the same filter green. Report the red run as that CP with a reason, then the green rerun. Do not land the red commit alone.

### S1 docs

Files: `docs/release-gates.md`, `AGENTS.md`, `docs/orchestration-loop.md`, `.claude/skills/antiphon-orchestrator/SKILL.md`, `docs/docker-stack.md`, `scripts/windmill/README.md`, `scripts/windmill/antiphon-release-candidates.json`, and new `tests/Antiphon.Tests/Application/ReleaseBranchModelDocumentationTests.cs`.

Rewrite `docs/release-gates.md` so the live contract is:

- The release line is a long-lived `rc/YYYY-MM-DD` branch. The current line is `rc/2026-10-15`. Bug and test fixes for code already on that line land on it. Features and refactors, including this card, are master-only.
- Every rc-line land is merged to master with a merge commit. That is an explicit exception to rebase-only. `-Land` does not do it (D-9). Say so in one sentence.
- Releases are cut from the rc line. `publish-release.ps1` writes `vYYYY.MM.DD.N-rc.K`. `scripts/promote-release.ps1` writes `vYYYY.MM.DD.N` at the same SHA. Tags are immutable. `v2026.10.15.1` and `v2026.10.15.2` are historical direct finals at the peeled SHAs above; do not invent `-rc.K` parents for them.
- A feature release from master is at most once per UTC day, and only after the current rc line is fully fixed and AppHost and server2 are running that tip. Those hosts run the rc tip, never master. This card does not implement that gate.
- The rc profile, credit split, eight-suite roster, and "RC green is not master readiness" rules stay. Change-aware heavy TUI selection is CARD-1313 on master, not this tree.
- Legacy stamp refs remain valid until a later card removes them (D-1).
- One lag sentence: S2-S4 have not changed the scripts yet. S4 deletes it (D-7).
- Replace the "no published tag" banner with the two real tags, and keep the Windmill job marked not registered. Do not call the scheduled lane qualified.

Cross-links, one short paragraph each, pointing at `docs/release-gates.md`:

- `AGENTS.md:26` row text names the long-lived rc line, `-rc.K` tags, and the final CalVer tag. `AGENTS.md` local-stack activation (`:55`) says the Antiphon product hosts track the rc tip; a master-only land is not a restart.
- `docs/orchestration-loop.md` post-land check (`:323-338`) says the same, and `:414` notes the merge-commit exception is manual.
- Orchestrator skill `:172-175`: restart AppHost only when the landed commit is the rc tip the hosts run. A master-only land does not restart AppHost or server2.
- `docs/docker-stack.md:5-7`: the SHA deployed to AppHost and server2 is the current `rc/YYYY-MM-DD` tip. Do not rewrite the phase table.
- Windmill README `:20-27` and the JSON description: the lane tests the named rc ref; it does not cut a stamp from `origin/master`. Keep "NOT REGISTERED" and the disabled schedule.

Test `ReleaseBranchModelDocumentationTests` (Unit, no process spawn):

- `C1311_ReleaseGatesNameTheLongLivedRcBranch` reads `docs/release-gates.md` and requires the literals `rc/YYYY-MM-DD`, `vYYYY.MM.DD.N-rc.K`, `merge commit`, and `rc tip`, and requires the absence of `The candidate ref is exactly`.
- `C1311_ReleaseBranchPointers` requires `rc/YYYY-MM-DD` in `AGENTS.md`, `docs/orchestration-loop.md`, `docs/docker-stack.md`, `.claude/skills/antiphon-orchestrator/SKILL.md`, and `scripts/windmill/README.md`.

Red witness: both methods fail on `3adbe0d5` because those literals are absent and the exact-ref sentence is present.

### S2 long-lived ref

Files: `scripts/lib/release-gate.ps1`, `scripts/release-candidate.ps1`, `scripts/release-cut.ps1`, `scripts/lib/nightly-run-impl.ps1` (message text only), `scripts/test-release-gate.ps1`, new `tests/Antiphon.Tests/Scripts/ReleaseBranchModelGateTests.cs`.

`Test-ReleaseGateCandidateRef` accepts `rc/YYYY-MM-DD` and `refs/heads/rc/YYYY-MM-DD`. Month and day must be a real date. Candidate id is D-2. Stamp refs still parse as they do now.

`Get-ReleaseGateCreditKind` returns `rc-release` for profile `rc`, trigger `rc`, and either shape. Malformed rows already in `Test-C599_CreditIdentity` stay empty.

`release-candidate.ps1`: omitted `-Ref` returns `rc-ref-required` and pushes nothing. A long-lived ref must already exist on the remote; the script journals that tip and does not create the branch. A missing ref is `rc-ref-missing`. Stamp `-Ref` keeps today's create-only push and `remote-candidate-conflict` behavior.

`release-cut.ps1` requires `-Ref rc/YYYY-MM-DD`. It no longer describes a master land plus a new stamp. It still runs `nightly-run.ps1 -Profile rc -Trigger rc -ExpectedSha <sha>` and still does not write master credit files or restart anything.

`nightly-run-impl.ps1:179` says the rc profile needs `rc/YYYY-MM-DD` or a legacy stamp. The moved-SHA exit at `:368-376` stays.

New harness function `Test-C1311_LongLivedRcRef`, invoked as `-Case C1311_LongLivedRcRef`. The C# method uses prefix `C1311` and `expectedRows` 10. PASS names:

1. `C1311 LongLivedRcRef rc/2026-10-15 is accepted`
2. `C1311 LongLivedRcRef refs/heads/rc/2026-10-15 normalizes to rc/2026-10-15`
3. `C1311 LongLivedRcRef candidate id is rc-2026-10-15-` plus the 12-hex fixture prefix
4. `C1311 LongLivedRcRef rc/2026-13-01 refuses ref-date`
5. `C1311 LongLivedRcRef rc/20261015 refuses ref-shape`
6. `C1311 LongLivedRcRef legacy stamp release/rc-20260923T083000Z is accepted`
7. `C1311 LongLivedRcRef credit rc/rc/rc/2026-10-15 earns rc-release`
8. `C1311 LongLivedRcRef credit rc/rc/release/rc-20260923T083000Z earns rc-release`
9. `C1311 LongLivedRcRef omitted Ref refuses rc-ref-required`
10. `C1311 LongLivedRcRef an existing rc/2026-10-15 tip is journalled and not pushed`

Red witness: rows 1, 3, 7, and 10 fail while the pattern is stamp-only. Do not change the 34-row `C599_CreditIdentity` inventory. CP-3 re-runs that method after the script edit.

`ReleaseBranchModelGateTests` is Integration and carries `[ParallelLimiter<ProcessSpawnLimit>]`, same as `ReleaseGateRunTests`.

### S3 pre-release tag and promotion

Files: `scripts/lib/release-gate.ps1`, `scripts/publish-release.ps1`, new `scripts/promote-release.ps1`, `scripts/test-release-gate.ps1`, `ReleaseBranchModelGateTests.cs`.

Extend tag parsing with an optional `-rc.K` (`K` a positive integer). `v2026.10.15.2` still parses as a final tag. `v2026.10.15.2-rc.0` and `v2026.10.15.2-rc` refuse `tag-shape`.

For a long-lived candidate id, `Get-ReleaseGateTagProposal` proposes `vYYYY.MM.DD.N-rc.K` under D-4. Stamp candidate ids still propose a final tag. Same candidate id and same SHA reuse the row. A different SHA under a stamp id still throws.

`publish-release.ps1` on a long-lived id pushes that pre-release tag and passes `--prerelease` to `gh release create`. Stamp publication does not pass `--prerelease`. The rest of the eight-step journal, including no `--force` and no tag delete, stays.

`scripts/promote-release.ps1` takes the `-rc.K` tag, peels it, and creates `vYYYY.MM.DD.N` at that SHA. It refuses when the final tag exists at another SHA, when the pre-release tag is missing, and when the peeled SHA is not a full 40-hex SHA. The happy path used in the S3 test has the live ref tip equal to that SHA. It does not delete the pre-release tag or its GitHub pre-release. It does not restart anything.

Harness `Test-C1311_PreReleaseTag`, prefix `C1311`, `expectedRows` 8:

1. `C1311 PreReleaseTag v2026.10.15.2-rc.1 is accepted`
2. `C1311 PreReleaseTag v2026.10.15.2 is accepted`
3. `C1311 PreReleaseTag v2026.10.15.2-rc.0 refuses tag-shape`
4. `C1311 PreReleaseTag a long-lived proposal is vYYYY.MM.DD.N-rc.K`
5. `C1311 PreReleaseTag promote creates the final tag at the peeled sha`
6. `C1311 PreReleaseTag promote leaves the pre-release tag in place`
7. `C1311 PreReleaseTag promote refuses a final tag at another sha`
8. `C1311 PreReleaseTag no tag delete and no force push`

Red witness: rows 1, 4, and 5 fail before the parser and `promote-release.ps1` exist.

### S4 moving tip, authority, and tag immutability

Files: `scripts/promote-release.ps1`, `scripts/publish-release.ps1` only if the promote path is the one missing the check, `scripts/test-release-gate.ps1`, `ReleaseBranchModelGateTests.cs`, and deletion of the S1 lag sentence in `docs/release-gates.md`.

Do not weaken `Test-ReleaseGatePublicationGate`. Promote refuses `remote-candidate-moved` when the live `rc/YYYY-MM-DD` tip differs from the pre-release tag's peeled SHA, before any tag or GitHub write. A second pin of the same ref at a new SHA writes a new candidate root (D-2) and does not modify the previous root's `authority.json`. `scriptHashes` remain the six file hashes (D-6).

Harness `Test-C1311_MovingTipAuthority`, prefix `C1311`, `expectedRows` 6:

1. `C1311 MovingTipAuthority a second sha gets its own candidate root`
2. `C1311 MovingTipAuthority the first authority.json still names the first sha`
3. `C1311 MovingTipAuthority publish of the first sha refuses remote-candidate-moved`
4. `C1311 MovingTipAuthority the first tag still peels to the first sha`
5. `C1311 MovingTipAuthority promote of a moved tip refuses remote-candidate-moved`
6. `C1311 MovingTipAuthority scriptHashes omit the branch tip`

Red witness: row 5 fails on the S3 tip, because S3's promote happy path does not consult a moved tip. Rows 1-2 fail if the implementation stored one root per ref instead of per D-2 id.

After this slice, delete the S1 lag sentence. `C1311_ReleaseGatesNameTheLongLivedRcBranch` must still pass, and it must now also require the absence of that sentence. Add the absence assertion in this slice's test commit so it is red until the sentence is gone.

## Not built

- Landing a fix onto `rc/<date>` and merging it to master with a merge commit. `-Land` stays rebase and fast-forward (D-9).
- A guard that refuses a feature-sized change on the rc branch. "Feature-sized" has no mechanical definition here.
- A probe that AppHost or server2 is on the rc tip before a feature release, and any edit to `deploy-server2.ps1` or `restart-apphost.ps1`.
- Porting CARD-1313 change-aware timing, or editing the eight-suite rc roster.
- Registering or enabling the Windmill RC schedule, retuning its cron, or cutting a feature release from master.
- Rewriting historical CARD-0599 plans, moving `v2026.10.15.1` or `v2026.10.15.2`, deleting tags, or restarting hosts.
- Hard-coding the current line date into a script.

## Verification design

The brief folds test design into this plan and asks for a minimal witness. Ordinary scope is the five new methods plus `C599_CreditIdentity`, the stamp-credit pin D-1 must not break. No namespace run and no rc profile run.

- V-1: the owner doc and the five pointers name `rc/YYYY-MM-DD`, the pre-release tag, the merge-commit exception, and rc-tip hosts, and the exclusive stamp sentence is gone.
- V-2: `rc/YYYY-MM-DD` earns `rc-release`, parses to the D-2 candidate id, journals an existing tip, and does not push; a stamp ref still earns `rc-release`.
- V-3: `-rc.K` parses, a long-lived proposal uses it, and promote creates the final tag beside it.
- V-4: a moved tip gets a new candidate root, leaves the old authority and tag in place, and cannot publish or promote.
- R-1: `C599_CreditIdentity` still reports 34 passing harness rows after S2.
- R-2: stamp publication still does not pass `--prerelease`, and no new path deletes a tag or force-pushes one.
- R-3: `scriptHashes` stay the six script files. The census digest is untouched.

The checkpoint importer reads the first `### Checkpoints` section only (`tools/Antiphon.Checkpoints/Manifest/PlanTableImporter.cs:311-328`). This is that section. Slice headings above name the same CP ids and are not a second table.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1311-s1/` | c1311-docs | `/*/*/ReleaseBranchModelDocumentationTests/*` | V-1 | all listed, 0 failed | 2 | 6 |
| CP-2 | S2 | `tests/Antiphon.Tests -> bin-c1311-s2/` | c1311-ref | `/*/*/ReleaseBranchModelGateTests/C1311_LongLivedRcRef` | V-2 | all listed, 0 failed | 1 | 10 |
| CP-3 | S2 | CP-2 | c1311-stamp-credit | `/*/*/ReleaseGateRunTests/C599_CreditIdentity` | R-1 | all listed, 0 failed | 1 | 3 |
| CP-4 | S3 | `tests/Antiphon.Tests -> bin-c1311-s3/` | c1311-tag | `/*/*/ReleaseBranchModelGateTests/C1311_PreReleaseTag` | V-3, R-2 | all listed, 0 failed | 1 | 10 |
| CP-5 | S4 | `tests/Antiphon.Tests -> bin-c1311-s4/` | c1311-tip | `/*/*/ReleaseBranchModelGateTests/C1311_MovingTipAuthority` | V-4, R-3 | all listed, 0 failed | 1 | 12 |

CP-1 closes on the S1 green tip. Its red witness is the same filter at the test-only commit. CP-2, CP-4, and CP-5 are the same pattern. CP-3 is not a red-first row; it is the D-1 regression pin and runs once, green, on the S2 tip. `Min` is TUnit methods, not the harness PASS rows inside them. CP-2's harness count is 10, CP-4's is 8, CP-5's is 6, and CP-3's existing method reports 34 harness rows.

A failure in some other `C599_*` method is a slice defect. Re-run that method and report it as unlisted, with the reason. Do not add it to this table and do not run the authority matrix to look for it.

### Cost

41 minutes of checkpoint time, plus authoring. No unchanged green repetition. One isolated build per `After` group. The checkpoint-tool bootstrap build is the usual infrastructure prerequisite through `scripts/build-slot.ps1`, with its own `bin-c1311-tool/` output, and is not a table row.

## Mutation handoff

No production C# behavior changes. Positive controls are not named. Post-land Mutation has nothing to bind until a later card edits `AgentTaskLandService` or the deploy scripts.
