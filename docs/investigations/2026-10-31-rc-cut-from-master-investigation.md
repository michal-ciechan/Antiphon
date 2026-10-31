# Cutting a new rc from master: investigation

Date: 2026-10-31 UTC. Task: `6ffcba8c`, Investigate. Scope: repository files and Git history only; no builds, tests, database/HTTP reads, qualification actions, target-ref writes or deployments. This report is the sole deliverable.

**Outcome:** the branch/candidate/tag distinction and the sources of lost-fix risk are confirmed from stored source and patch history. A new branch at today's master would drop 84 unmatched code/test/build commits carried by the current rc, including three older Claude composer fixes outside top-ups 44–47. Recommend the requested new-master cut **after** the reconciliation below, with `next: decide` because top-up 48's membership and acceptance of the additional master capacity changes remain unresolved.

## Evidence identities and limits

An initial `git fetch origin` completed successfully at approximately 05:10 UTC. All comparisons below use these immutable fetched identities, rather than later-moving branch names:

```text
HEAD / origin/master
cbe3e6b42401db766f4cee38bc9b34f5b9d314af
origin/rc/2026-10-27
3fb3c113e8b2554ccd5789e00375dbfd69697981

git rev-list --left-right --count origin/master...origin/rc/2026-10-27
1116    1096

git cherry -v origin/master origin/rc/2026-10-27
1096 rows: 978 '-' patch-equivalent; 118 '+' unmatched

git cherry -v origin/rc/2026-10-27 origin/master
1116 rows: 978 '-' patch-equivalent; 138 '+' unmatched

git diff --stat origin/master origin/rc/2026-10-27
239 files changed, 24805 insertions(+), 17493 deletions(-)
```

The brief's “master ports done through top-up 43” agrees with the patch reconstruction; the 1,096 rc ancestry-only commits are not 1,096 missing fixes. Git cherry compares individual patches, not integrated behavior. I inspected each `+` commit's changed paths, matched duplicate subjects by code-only stable patch ID, compared final blobs, and accounted for every one of the 146 changed paths outside `docs/` and `.antiphon/` using the unmatched commit path sets. That is a static reconciliation, not execution qualification or proof that future conflict resolutions preserve behavior.

Historical reports are cited as `<commit>:<path>:<line>` below. Retrieve them with `git show <commit>:<path> | nl -ba`; they need not exist in the assigned master worktree. Ordinary file citations refer to the master identity above unless explicitly marked rc. Queue and running-host state cannot be established from repository history.

## 1. What “cut” means today, and who may do it

There are four distinct operations:

| Operation | Exact entrypoint / command shape | Result and authority |
|---|---|---|
| Create a **new release line from master** | In a separately commissioned integration task, pin the fetched master SHA, verify the new name is absent, then `git push origin <full-master-sha>:refs/heads/rc/YYYY-MM-DD`; read the remote back. | Creates a remote **branch**. There is no automatic “latest-master to new long-lived rc” operation in `release-candidate.ps1`. The caller's instruction authorizes preparation of this path once all fixes are in; this investigation does not execute it. Branches and tags are separate authorities. |
| Journal an existing rc tip | `pwsh -NoProfile -File scripts/release-candidate.ps1 -Ref rc/YYYY-MM-DD -ReleaseRoot C:\Antiphon\releases -CheckoutRoot C:\Antiphon\releases\checkout` | Produces `candidate.json`; creates **no long-lived branch and no tag**. Delegate/qualification owner. The clone must pass the existing producer-ownership checks; do not manufacture its marker. |
| Qualify without publication | `pwsh -NoProfile -File scripts/release-cut.ps1 -Ref rc/YYYY-MM-DD -SelectionMode Full -SkipPublish` | Journals and tests the pinned tip under the rc profile. Creates no tag/release with `-SkipPublish`. Native Windows qualification owner, serialized with other native runs. |
| Publish the qualified candidate | `pwsh -NoProfile -File scripts/publish-release.ps1 -CandidateId <id> -ReleaseRoot C:\Antiphon\releases -CheckoutRoot C:\Antiphon\releases\checkout` | Reserves/creates annotated **tag** `vYYYY.MM.DD.N-rc.K`, pushes that tag, creates/uploads/verifies/publishes a GitHub **pre-release**. **Human-only under the explicit standing rule in this task's brief.** |
| Qualify **and publish on green** | `pwsh -NoProfile -File scripts/release-cut.ps1 -Ref rc/YYYY-MM-DD` | Same lane, then invokes publisher. Consequently **human-only** without `-SkipPublish`; its name does not make it branch-only. |
| Promote the published pre-release | `pwsh -NoProfile -File scripts/promote-release.ps1 -Tag <vYYYY.MM.DD.N-rc.K> -CandidateId <id> -ReleaseRoot C:\Antiphon\releases -CheckoutRoot C:\Antiphon\releases\checkout` | Creates/pushes final annotated **tag** `vYYYY.MM.DD.N` at the **same peeled SHA**, records promotion; retains the pre-release. **Human-only.** The observed implementation does not create another GitHub final release or restart a host. |
| Activate an rc SHA | Canonical AppHost restart / staged server2 rolling phases, described below. | Changes running code. Orchestrator autonomy applies; publication itself does not activate anything. |

Authorities and implementation:

- `docs/release-gates.md:22-36` defines the long-lived branch, pre-release/final tag distinction, immutable tags, product hosts on rc, and at most one feature release from master per UTC day **after** the current rc is fully fixed and hosts run that tip. The document explicitly says that admission gate is not implemented.
- `docs/release-gates.md:467-496` and `scripts/release-candidate.ps1:147-210` require an existing remote long-lived ref, resolve its full SHA, derive the candidate ID, and journal without pushing. `-SourceRef master` does not change that long-lived-ref behavior. The legacy stamp path alone has a create-only branch push (`scripts/release-candidate.ps1:219-308`); it is not the requested new `rc/YYYY-MM-DD` workflow.
- `scripts/release-cut.ps1:43` declares `-SkipPublish`; `:155-173` forwards pinned ref/SHA/candidate/state to nightly; `:184-193` returns before publisher when skipped. Without that switch it calls publisher after a successful run.
- `docs/release-gates.md:515-559`, `scripts/publish-release.ps1:294-407` and `scripts/promote-release.ps1:183-216` establish publication and promotion effects. Promotion revalidates authority, Full evidence and publication reservation even on retry. Do not replace those guarded front doors with manual tag commands.
- The **human-only classification** comes explicitly from the assignment brief, `.antiphon/inbox/b085c064-2070-4e1c-89cf-dcff73eb8138.md` (question 1: “tags, publish and promote are human-only”). The repository's standing rule is narrower wording: `docs/orchestration-loop.md:626` retains existing authorization requirements for publish/promote/tag. It does not itself spell out a universal human-only rule. This report preserves the brief's stronger boundary rather than attributing invented wording to AGENTS.md.
- `AGENTS.md:54,57`, `docs/orchestration-loop.md:17-40,622-626` grant orchestrators restart/rollout authority, retain host/ref and review obligations, and keep `-KillSessions` human-only. No additional permission is needed to commission the authorized prerequisite work; this read-only task performs none of it.

There is documentation drift: `docs/release-gates.md:23` still calls `rc/2026-10-18` current. The brief and fetched remote identify `rc/2026-10-27` as the line under examination. Similarly `release-gates.md:24-26,44-57` describes merge-commit integration, whereas the newer standing rule `orchestration-loop.md:624` and stored top-up reports describe reviewed cherry-picks. A whole-line merge is not interchangeable with these pending ports: top-up 44 already recorded 14 prospective master conflict paths (`ac6bd1b5b:.antiphon/task-09322a8b.md:44-59`).

## 2. Which rc patches master actually lacks

The 118 `+` entries decompose exactly as follows:

| Group | Unmatched commits | Changed code/test/build commits | Meaning |
|---|---:|---:|---|
| Older residuals before top-up 44 | 23 | 8 | Three genuinely missing Claude patches; five code-equivalent false positives; fifteen documentation/report-only commits. |
| Top-up 44 | 41 | 37 | CARD-1619 chain, C448 journal cleanup, release-note credential-scan witness repair, plus records. |
| Top-up 45 | 12 | 12 | Groups 124/126/128 repairs and policy coverage/witness repairs. |
| Top-up 46 | 9 | 5 | Groups 104/105 GitFixture and diagnostics; one new project definition and graph approval. |
| Top-up 47 | 33 | 27 | CARD-1514 manifest publication and timing regressions, Remote custody assertion, plus records. |
| **Total** | **118** | **89** | **84 real unmatched code/test/build patches after removing the five code-equivalent rows.** |

### Exact pending-port inventory

Ranges use Git's exclusive-lower/inclusive-upper `A..B` convention. “Not started” below means **no published master port is present in this fetched history**. It does not establish that no task is queued or locally working. No DB/API queue read was authorized, so claiming “queued” would be unsupported.

| Series | Exact rc source / branch | Status on fetched master | Loss if omitted from new cut |
|---|---|---|---|
| Earlier Claude composer compatibility (no CARD ID in commit subjects) | rc `de9a8fba48dcf3c5ff84114fa23d73122aa5ff97`, `c90f8047b299aa3c263ff1a1a33536bbe48f02fc`, `cdfd8366fa252d5c87997bef10352f2650f96d53`; originals `3aac0cdd4`, `98aa524aa`, `f06b5f40d` on `origin/fix/composer-wrapped-rc-hint` | **Not started in master history.** Additional to the supplied top-up list. | Wrapped `/rc`/`/effort` hint/status rows, fourth hint row, weekly-usage banner and pasted-text hint handling. |
| 44, CARD-1619 | 33 commits `f0caf7762607067bbb63d0075be26ea44dfb674c..c6010e7445d95ad9f1fc8d593e4daa48ff2915f8`; complete top-up source `origin/feat/card-task-09322a8b` | **Not started in master history; ordered behind CARD-1610 S2+S3.** | Linux exit-signal observation/replay, host/runner/wire/server persistence and consumers, stop intent/issuer/kernel identity, and nullable evidence migration. |
| 44, C448 | `c6010e7445d95ad9f1fc8d593e4daa48ff2915f8..fe6b7c498f1365d4d48c9bbcf69c8fa6da268343` (five commits; two code/test, three records) | **Not started in master history.** | Transient owned-journal delete retry and diagnosed permanent cleanup fence. |
| 44, release-gate scan witness | `fe06d0a88a0e51d514fef8bee4ee56626f184fa4`, `3352bc5f5190f6fdc426a4576bbbcff1946dcf3e` | **Not started in master history.** | Release-note token-identifier false-positive repair and its witness. The fix edits `scripts/test-release-gate.ps1`, not a new publication permission. |
| 45, policy / group 126 | `ac6bd1b5b9cf6119bffd0b820ee12af7f8208492..4cd67d2c29a03085b56c487640ff87eec24af60e` (six) | **Not started in master history.** | Legacy policy pin/LF+CRLF witnesses, DinD namespace residue guard, CARD-1619 native Linux server policy rows and isolated mutation fixtures. |
| 45, groups 124/128 | `4cd67d2c29a03085b56c487640ff87eec24af60e..47a1cd5ede46e406d9d2667dfbf4f233df4b8936` (six) | **Not started in master history.** | Windows PATH/attribute cleanup, snapshot ACL scenario selection, and junction pipe-holder execution deadline witnesses. |
| 46, groups 104/105 | `47a1cd5ede46e406d9d2667dfbf4f233df4b8936..b37178b6bde70a79c40e2172a60eb05bb94895bb`; `origin/feat/card-task-346390be` | **Not started in master history.** | Native GitFixture executable for invalid UTF-8, diagnostic details, test-project reference and graph approval. |
| 47, CARD-1514 | `b37178b6bde70a79c40e2172a60eb05bb94895bb..ff446d2cb063493aab45a28f8e13f2243a6f7f58` (31); original series selected from `origin/feat/card-task-79c0c349` | **Not started in master history.** | Incremental executed-manifest journal, persistent projection context, seeded summary joins, transient timing recovery and structural timing witnesses. |
| 47, Remote assertion | `49322b8e0a40a8c94724385b4103d84f15b8fb61`; source `e4239eebba41642916935d64b3d034bd7c37c3d3`, `origin/feat/card-task-7aadc94a`; integrated branch `origin/feat/card-task-8f0a71bb` | **Not started in master history.** | Namespace-counter-compatible Remote custody residue assertion. |
| Later top-up 48 | **No identified published range or series in the allowed evidence.** Current rc ends at the top-up 47 report. An all-ref commit-subject search for `top-up 48`, `top-ups 48`, `rc48`, `rc 48` found no matching commit. | **Unresolved; neither ported nor queued can be established.** | Cannot claim lossless rollover until the caller supplies the commissioned source set or its eventual published tip. Do not silently treat 48 as empty. |

For 44, `ac6bd1b5b:.antiphon/task-09322a8b.md:65-112` is the complete source-to-rc SHA mapping. Its migration is recorded at `:11-13` and exclusions at `:114-124`. For 47, `3fb3c113e:.antiphon/task-8f0a71bb.md:12-46,75-91` maps every selected source, confirms the graph, and records no new migration. The twelve top-up 45 commits and nine top-up 46 commits are directly reproducible with `git log --reverse --format='%H %s' <range>` from the table.

The older Claude omission is demonstrated by the final-tree diff, not just subjects: `git diff cbe3e6b42 3fb3c113e -- src/Antiphon.Agents.Pty/ClaudeScreen.cs tests/Antiphon.Agents.Pty.Tests/ClaudeDialogObservationTests.cs` shows all three additions missing from master. At rc, `ClaudeScreen.cs:127-175` contains the fourth row, weekly-limit/paste hints and `IsWrappedHintCommandRow`; master has three rows and lacks that helper.

### Five misleading `git cherry +` pairs

For each pair I ran `git show --format= <sha> -- . ':(exclude)docs/**' ':(exclude).antiphon/**' | git patch-id --stable`. Both members produced the same ID:

| rc SHA | master SHA | Subject / code-only stable patch ID |
|---|---|---|
| `125c720b9` | `583a36b0b` | CARD-1574 regrowth/malformed-byte witnesses; `3d69e8b0089baed5be9355a66624e199d343f6a2` |
| `3cb215cf3` | `909e99ad4` | CARD-1610 current Failed owner/unproved transcripts; `d8560f32e3bb86379f4e8f265da156120c19e91b` |
| `9c271b7e1` | `c2ebb2a47` | CARD-1610 zero-row pool retention/boundary IDs; `43b0dc31ef136de0d96928bda8dfaa6e98aadf1d` |
| `80b3a59c4` | `698bf1b73` | CARD-1610 mixed-cap/terminal zero-row witnesses; `54767f3553fcc6f48aeb1592b7880c86e775858a` |
| `b226c226b` | `cbe3e6b42` | C1379 committed replay observation; `6436afbf224cd2640dcca6e398548dedece762c9` |

Their full patches differ because plan/investigation/report hunks were omitted or restored during ports. Final-tree blobs are also identical across both refs for `ResumeReceiptReadinessTests.cs` (`b661836e14377e1314fcefd2d10b6ca3d7ef001e`), `TaskSupervisionVisibilityTests.cs` (`53a5df6b786efcce1aaf9e956165c21f05eed915`), `LandRestartReplayTests.cs` (`2ffcb41dffef38d5964d2ca0fa9a60961f6bf0ff`), `FailedTaskSessionOwnership.cs` (`b6bff114d214c75b3c37a0be9d59ed860659bd0e`) and `AgentTaskDispatcher.PoolSupervision.cs` (`76203dad3f5cfb0ff839b16f01f6cd92c4cc2fe5`). These five rows are not another missing-code queue.

## 3. Prerequisites, migration ordering, and what a completed port buys

| Requested item | Repository finding | Consequence |
|---|---|---|
| CARD-1610 S1 | Present on master, including the three code-equivalent rows above. | Do not re-port it merely because whole-patch IDs differ. |
| CARD-1610 S2+S3 | **Not on master or current rc.** Latest published repair source observed is `44d3a46c400a208215a10d29a02f74042d43ccee`, `origin/feat/card-task-18255e41` / `origin/feat/card-task-7743ab23`. Its report explicitly still requires Final Review and a joint master land. | Finish that review/land first. A task-ref tip is not landed code or approval. |
| CARD-1619 migration | rc has `20261030141651_AddSessionExitSignalEvidence`; current master newest is `20261028170715_AddAgentSessionCodexTokenCount`. CARD-1610 adds `20261030114738_AddTaskCeilingSupervision`, `20261030143219_AddTaskSupervisionQueuePurpose`, and `20261030214946_CloseTaskSupervisionReportWatches`. | As instructed in the brief and recorded by Review, the master CARD-1619 migration must be re-created after CARD-1610's migration chain; copying the old snapshot wholesale does not reconstruct that chain. This work belongs to the commissioned port, not this investigation. |
| CARD-1609 | **Not landed on master or current rc.** Published continuation history exists, including `origin/feat/card-task-cdf3cde4` at `ac7504a0a3f1de2d89004cdd110d9024b04acbc8`; its report says S5 incomplete and its delivered abort repair is not green. Later continuation branches exist, so this old report is not a current whole-card verdict. | “All fixes are in” cannot include CARD-1609 on the strength of a branch alone. Obtain its eventual final reviewed/landed source; it changes rollout/retirement tooling. |
| CARD-1618 slices | **Already ported.** Master includes S-A `61dc58c26`, S-B1 `4175a84b3`, S-B2 `db21304ee`, S-C1 `e85d31bce` / `bce716158`, S-C2 `96399c2d7`, S-F `d70fcbce5` / `522ec3e32`, and aggregate record `7ba9c8625`. | A new master cut retains these source changes. Native Windows, Full/R-2 and S-F-cost obligations are separate; ported does not mean qualified. |
| CARD-1616 | **Already ported.** Master `0562f6e29`, `b191de000`, `8ef492cf4`, `cff8b5bd0`, `e6e4c6871`; corresponding rc patches `09a103053`, `4647e8b65`, `f399faa98`, `e655711dc`, `f44e1c75e` are `git cherry -` equivalents. Subsequent admitted-build terminal-state repair is master `5ebf45c75`, rc `734d0a90b`. | Retained by a master cut; no duplicate port required. |

Stored evidence for CARD-1610: `44d3a46c4:.antiphon/task-18255e41.md:3-24` records the third repair, 417/417 narrowly verified tests, and the still-required Final Review; `d5c40513e:.antiphon/task-dea35e9f.md:52-56` explicitly records the migration ordering requirement. Those are historical source-bound results, not tests run by this investigation or a new clean verdict. CARD-1609's incomplete boundary is `ac7504a0a:.antiphon/task-cdf3cde4.md:1-3`.

CARD-1618 provenance: `.antiphon/task-0cbd3a9e.md:9-40` maps top-up 42's S-C2/S-F and failed-first work onto master, including the later aggregate update. The S-F addendum itself is `docs/superpowers/plans/2026-10-30-card-1199-windows-runnable-plan.md:8-19`; `docs/release-gates.md:266-285` retains native/Full/R-2/S-F-cost limits. These are not additional pending source ports.

Top-up 46 changes the build graph for a concrete reason: `0addf9c2a` adds `tests/Antiphon.Checkpoints.GitFixture/Antiphon.Checkpoints.GitFixture.csproj`, with `TreatAsLocalProperty="UseAppHost"` and `UseAppHost=true` at lines 1 and 7-8 because CreateProcess does not resolve `git.cmd`; it also references the fixture from the test project. `b37178b6b` changes both graph approval files:

```text
old: 7BDABBA075B620C10A47B50CE1277A932DDD801E9314A932C4EAC03885441A99 / 68
new: E7C6EDAFA83B544A42E186899267267FD73E6FDD2FB7DD2DF36C233F2C405806 / 69
```

The digest occupies line 1 of `server/Application/Services/LandVerificationBuildGraph.manifest.txt` and `.evaluation.txt`; manifest line 2 is the count. The later top-up 47 report `3fb3c113e:.antiphon/task-8f0a71bb.md:85-91` records the real approval and no further definition changes. A future master integration has additional changes, so this rc approval is provenance, not authorization to paste the digest into an unevaluated master graph.

**Counterfactual:** cutting at current master loses every genuinely pending row in section 2. After only the named 44–47 ports, the three Claude compatibility fixes would **still be missing**. After all those rows and the prerequisite lands are reconciled, those known rc fixes can be present on the new line; there is no intrinsic category of rc-only-code fix that a branch from master automatically inherits. Its content is exactly the selected master tree. Top-up 48 and future fixes require a fresh final comparison before a lossless-cut claim is possible.

An additional operational uncertainty is the live database migration ledger: the old rc nullable-column migration may or may not have run on product hosts. Its `Up` adds `ExitSignal` to three tables and `ExitStopIntentJson` to sessions (`3fb3c113e:server/Migrations/20261030141651_AddSessionExitSignalEvidence.cs:11-36`). This repository-only task cannot certify deployment compatibility with a later recreated migration ID; the port/release owner needs that actual ledger evidence.

## 4. Reverse inventory: what master would bring into the new line

The 138 unmatched master patches consist of 104 docs/report-only commits, one AGENTS.md/orchestration policy commit (`785d6a636`), the five code-equivalent pairs above, and 28 remaining product/test patches in two capacity series:

| Series | Source interval and representative subjects | rc finding / classification |
|---|---|---|
| CARD-1593 S3 round A | `925fa43e45086bfb1d8fbd45f2d4cd8e14b44a31..e65343132e877ce20bd26540d440eff3867d9798` (17 product/test commits): admitted revalidation consume, atomic supervisor reservation, bounded lost-commit reread and witnesses. | Absent as patches on rc. Master adds `CapacityConsumeReceipt.cs` and S3-specific tests; changes start/reservation behavior. This is additional master scope in a fresh cut. |
| CARD-1603 | `95ca3ae89ca2bc58039fd6f697c564dfff655de8..98fc3ceb6440a17b68cafe3ecc21c76d190b18ca` (11 product/test commits, plus plan/review records): detach obsolete queue selection, supersede replaced standing wait, release expired/finished grants, lock-order witnesses. | Absent as patches on rc. Master adds `CapacityRecoveryService.ObsoleteConsumers.cs` and its tests; depends on CARD-1593 S3-A. |

**The allowed evidence does not justify calling these “feature work not meant for rc.”** The S3 plan is based on rc and identifies leaks (`docs/superpowers/plans/2026-10-28-card-1593-s3-atomic-consume-plan.md:3,34-48`); CARD-1603's plan documents actual stale-consumer bugs and the master-first dependency (`docs/superpowers/plans/2026-10-29-card-1603-stop-capacity-grant-cycling-plan.md:17-39,45-59`). New files/changed behavior alone do not establish an exclusion policy. They are confirmed extra master scope whose admission must be explicit in the rollover decision. No separate unmatched feature/refactor series was found in this comparison. Do not invent one from the 1,116 ancestry count or from a `feat` prefix.

The existing policy remains features/refactors master-only **while continuing the existing rc line** (`docs/release-gates.md:24`; `docs/orchestration-loop.md:624`). A deliberately new line cut from master necessarily adopts its complete selected tree, including these capacity changes and anything that lands later. An exact final inventory, rather than today's list, is the admission boundary.

## 5. Hosts and Windows qualification on a line rollover

**The hosts do not discover “the newest rc branch” by its name.** AppHost builds the canonical checkout on disk. The restart script compares its source-root HEAD to `-ExpectedServerSha` (`scripts/restart-apphost.ps1:180-204`); its contract is canonical restart plus exact `/api/version` identity (`docs/apphost-runbook.md:29-39,163-165`). Creating a new remote branch leaves that checkout and the running host unchanged.

The server2 front door is explicitly SHA-addressed: `scripts/deploy-server2.ps1:5-9` requires `-Rolling -Sha <40hex>` and a named phase; status acceptance compares `buildVersion` to that SHA (`:998-1005,1185-1298`). Its host-case manifest still sets `c604Branch = 'master'` (`:1108-1115`), and `scripts/c590-remote.sh:17,136-148` uses that branch for bootstrap then fetches/detaches the requested SHA. That master literal is a clone/bootstrap detail, **not** policy authority to deploy master and not a selector for the active rc line. The runner repository can fetch master for task work while the product binary runs an rc SHA.

Rollover therefore requires explicitly selecting the new branch's pinned SHA for both product hosts, advancing the clean canonical checkout through the operational owner, and using the same staged rollout:

```powershell
# On the canonical desktop checkout at the intended rc SHA, after lock/land checks:
pwsh -NoProfile -File scripts/restart-apphost.ps1 -ExpectedServerSha <full-rc-sha>

# Separate named phases, each only after its runbook gate succeeds:
pwsh -NoProfile -File scripts/deploy-server2.ps1 -Rolling -Sha <full-rc-sha> -Phase deploy-temp
pwsh -NoProfile -File scripts/deploy-server2.ps1 -Rolling -Sha <full-rc-sha> -Phase drain-old -WaitIdleMinutes 240
pwsh -NoProfile -File scripts/deploy-server2.ps1 -Rolling -Sha <full-rc-sha> -Phase redeploy-old
pwsh -NoProfile -File scripts/deploy-server2.ps1 -Rolling -Sha <full-rc-sha> -Phase drain-temp -WaitIdleMinutes 240
pwsh -NoProfile -File scripts/deploy-server2.ps1 -Rolling -Sha <full-rc-sha> -Phase retire-temp
```

This is a command inventory, not permission to skip interleaved checks: `docs/docker-stack.md:106-130,159-175` requires old-container identity preservation, temp smoke and sanctioned Plan canary before drain, zero-work gates, upgraded-old smoke/canary and `verify-card0849-caches.ps1 -Case Both -Sha <sha>`, then retirement proof. Keep dispatching to temp until old is ready, then back to old while temp drains (`docs/orchestration-loop.md:17-24`); preserve the caller's in-flight target. Run deployments from the desktop, not this Linux investigation (`scripts/deploy-server2.ps1:1`). Verify `/api/version`, runner `buildVersion`, eligibility and the changed behavior directly. A tag/release or a master land is not activation (`AGENTS.md:57`; `docs/release-gates.md:584-604`).

The Windows qualification identity is branch name **plus SHA**, not date alone:

```text
candidate ID: rc-YYYY-MM-DD-<first 12 lowercase hex digits of full SHA>
current fetched tip's would-be ID: rc-2026-10-27-3fb3c113e8b2
candidate directory: C:\Antiphon\releases\candidates\<candidate-id>
journal: candidate.json
RC green: complete-green.json
ordinary producer checkout: C:\Antiphon\releases\checkout
shared native lock: C:\Antiphon\verification\native-run.lock
```

Derivation: `scripts/lib/release-gate.ps1:143-161,258-276`; producer journal `scripts/release-candidate.ps1:163-196`; standard lane invocation/state forwarding `scripts/release-cut.ps1:155-173`. The nightly entry validates that a supplied ID equals the ref/full-SHA-derived ID and refuses the master state root (`scripts/lib/nightly-run-impl.ps1:209-243`). A newly named line needs its own ID/state even at an equal SHA. A moved ref invalidates the old attempt (`docs/release-gates.md:490-496`); never relabel old green.

The active Windows supervisor's actual launch record, private continuation script, state/log/coordination roots and PID are **not in this task's allowed live evidence**. The naming above is the repository contract, not a claim about that running process. Its owner must preserve the current launch and commission a fresh attempt with matching `-Ref`, full `-ExpectedSha`, `-CandidateId`, `-ReleaseRoot`, `-StateRoot`, `-CoordinationRoot` and run identity. `docs/release-gates.md:115-147` explicitly forbids guessing identity from the newest directory. Native work is serialized (`:99-111`); Linux results cannot grant RC credit (`:230-241`).

There is also a literal scheduled-wrapper pin: `scripts/windmill/antiphon-release-candidates.json:3-4,11` still names `rc/2026-10-18` and says to review it on rollover; its description says NOT REGISTERED and disabled. Creating a new rc branch does not retarget that wrapper, register a job or enable a schedule. No live scheduler state was read here.

## 6. Decision recommendation and exact ownership sequence

**Recommend (b), a new rc from the latest fully reconciled master, once the prerequisites are met.** This follows the caller's requested end state. It is not a claim that current master is ready to cut.

| Choice | Benefit | Measured/evidenced cost and risk |
|---|---|---|
| (a) Continue `rc/2026-10-27` for the next candidate | Retains the known 84 rc patches immediately and excludes the additional capacity series pending a deliberate port. Lower integration work before another current-line run. | Still needs remaining selected fixes and a fresh full native qualification of the new tip. It leaves master reconciliation/migration work outstanding and postpones the requested new-master line. |
| (b) New line after master ports | One chosen master tree contains the fixes and the newer admitted scope; simpler ancestry after the cut. Meets the requested end state. | Requires CARD-1610 review/land, CARD-1619 migration integration, 44–47 and the earlier Claude ports, reconciliation of 48/CARD-1609, and acceptance of master scope. A branch push itself is cheap; integration and fresh Windows qualification dominate. No measured end-to-end ETA exists. |

Known timing evidence illustrates the cost without inventing an estimate: the top-up 47 report records a 307.93s build and 2,312s of test-wrapper admission waits, plus 694s held (`3fb3c113e:.antiphon/task-8f0a71bb.md:99,135`). Those are one historical narrow pass, not a forecast for master reconciliation or a full Windows run. The existing-line option also cannot reuse green from another SHA/policy (`docs/release-gates.md:510-513`).

Recommended sequence, for the caller to commission under existing authority:

1. **Orchestrator:** retain the existing rc/qualification launch intact while the prerequisite work finishes; keep the requested work target on server2. Finish CARD-1610 S2+S3's Final Review of the repaired source and joint master landing. Resolve CARD-1609's still-open acceptance if it is part of “all fixes”; no task-ref existence substitutes for Review/land.
2. **Port delegates + ordinary Review/landing owners:** reconcile the exact pending inventory in section 2 onto master, with CARD-1619's migration ordered after the landed CARD-1610 chain. Include the older three Claude commits. Preserve top-up 46's new definition and require approval for the actual integrated graph. These are separate commissioned implementation tasks; this report supplies evidence, not a port implementation or design.
3. **Orchestrator/decider:** identify top-up 48's exact source set and eventual landing; decide explicitly on the additional CARD-1593/CARD-1603 scope and any newer master work. Re-fetch, pin final old-rc/master SHAs, rerun both `git cherry` directions and inspect unmatched code paths/patch IDs. Until every missing behavior is accounted for, the new cut does not satisfy “loses nothing.”
4. **Commissioned integration delegate:** after the old rc is fully fixed and product hosts run that tip, select today's permitted new date name (at most one master feature release per UTC day), confirm remote absence, and create the branch from the pinned latest master. Command shape: `git push origin <full-master-sha>:refs/heads/rc/YYYY-MM-DD`, followed by `git ls-remote origin refs/heads/rc/YYYY-MM-DD`. This task is restricted to its assigned report branch and does **not** execute that target-ref write. Existing tags/lines/evidence remain immutable history.
5. **Qualification owner on Windows:** preserve/finish or separately disposition the prior run through its owned supervisor procedure; do not overlap native lanes. Journal the new line and run `pwsh -NoProfile -File scripts/release-cut.ps1 -Ref rc/YYYY-MM-DD -SelectionMode Full -SkipPublish`, using the explicit owned roots and launch identity. If a specific supervisor is used instead, its forwarded fields must satisfy section 5. Require fresh complete Full Windows coverage, policy/script/source binding and report delivery; failures retain evidence and get Debug work under the standing rule. Startup-path changes retain their exact-candidate native probe/rehearsal obligation (`docs/release-gates.md:400-441`).
6. **Orchestrator / desktop rollout owner:** explicitly designate the new line as current, advance the canonical checkout, restart/rehearse and roll server2 through the documented named phases at its pinned tip, preserving smoke/canary/dispatch gates. Record exact live versions. Branch creation and qualification do not perform this transition automatically.
7. **Human publication owner:** after complete-green and valid pinned authority, invoke `publish-release.ps1` for the candidate; only this step creates `vYYYY.MM.DD.N-rc.K` and publishes the pre-release. A later human `promote-release.ps1 -Tag <reserved-pre-release> -CandidateId <id>` creates the final tag at the same SHA. Do not guess `N`/`K`, move a tag, or let an unskipped `release-cut.ps1` perform these human-only actions implicitly.

No request for further approval is needed to prepare/commission the work already covered by “make the latest rc cut from latest master once all fixes are in.” The narrower report scope and human-only tag/publication boundary still apply.

## Remaining uncertainties and completion boundary

- **Top-up 48 membership/status:** needs the caller's source branch/range or its later published report. The supplied known fact is not enough to name its commits. No invented port/queue status is reported.
- **Queue state and concurrent lands:** repository refs establish publication only. Reconcile against freshly fetched final refs at the decision point; this report's counts are tied to the recorded SHAs.
- **Final approvals:** the repaired CARD-1610 source report still asks for Final Review; CARD-1609 has later live continuation work. No current DB-backed task status was read.
- **Live deployment/schema/supervisor state:** needs actual host versions, migration ledger and the Windows launch record from their owners. None can be inferred from health, a task branch or a folder name.
- **Master scope:** the extra capacity series are established, but no evidence permits labeling them forbidden feature work. The decider must accept the exact final master tree before rollover.
- **Qualification:** no new test/build receipt was produced. Prior reports' narrow Linux successes do not earn Full Windows credit. No release candidate was cut, tagged, published, promoted or activated by this investigation.

## Not done, noted

Separately commissioned ports/rollover maintenance must reconcile missing source, migration history and stale literal rc references; no fix was designed or implemented here.

--- next stage ---
next: decide
handoff: Choose the new-master rollover after CARD-1610 and all pending ports, include the three older Claude fixes, identify top-up 48, and explicitly admit final master capacity scope; preserve human-only tags/publication and commission fresh Windows qualification plus explicit host rollout.
artifact: docs/investigations/2026-10-31-rc-cut-from-master-investigation.md
