# CARD-1281: remove measured pipeline waste, then qualify a release train

Date: 2026-10-15. Stage: Plan. Status: proposed under the decisions below; operator approval pending. No policy, settings, schedule, stage routing or production code changes are authorized by this document.

## Outcome and evidence boundary

First remove documentation/evidence repair loops, unowned handoffs and delayed closure. Then qualify the existing release machinery and pilot a daily pre-land aggregate for bounded changes. Test omission of Plan independently. The evidence supports these experiments; it does **not** support a 10x-50x throughput forecast.

Implementation will need code and policy changes. This dispatch does not fold in TestDesign. After decisions, commission TestDesign for each approved implementation slice before Code; the checkpoint inventory below is planning input, not an executable verification manifest.

Evidence read:

- CARD-1281, board-scoped card `4627e73a-2a52-45ce-b761-9ae3c39860ee`, revision 3; operator request dated 2026-10-15.
- Full Investigate report, task `90f8c69c-3d69-4e40-b8f3-a9f6639a4e84`: `C:\src\Antiphon\.antiphon\reports\90f8c69c-3d69-4e40-b8f3-a9f6639a4e84\81751b776ac5a0e4ba01218c82c80887ec2f55871b2de99d37b730e2f508911a.md`.
- Its three complete deliverables under `C:\src\Antiphon\.antiphon\deliverables\90f8c69c\`: `logs.md`, `orchestration-loop.md`, `release-gates.md`. Each SHA-256 matches the corresponding current repository owner document and the supplied source manifest. The report remains the authority for the historical measurements; this Plan did not rerun the historical census.
- Code inspected at `cc8798c1fa4f3c2faa834283aef080d8e5fd28f0`; owners [orchestration-loop](../../orchestration-loop.md), [release-gates](../../release-gates.md), [agent-card-lifecycle](../../agent-card-lifecycle.md), [testing-and-build](../../testing-and-build.md), [logs](../../logs.md), [ops-http](../../ops-http.md) and [project-context](../../project-context.md).
- Live `GET /api/runner-defaults` and `GET /api/session-runners` succeeded on 2026-10-15. Defaults revision 2; the inventory offers eligible Windows and Linux lanes and an unavailable entry. This is a placement observation, not release qualification. Re-read both routes and the project-scoped pipeline limits at commissioning; embed no host location or fixed runner choice in the workflow.

### Baseline to retain in every comparison

The investigation window is October 1 00:00 to October 15 08:02 UTC. The fresh cohort has 249 completed, published cards first dispatched within that window. It excludes unfinished cards. The broader 320-card cohort includes older work; 104 of those cards closed in bulk on October 4. Never treat those closing timestamps as execution or test time.

| Measurement | Observed value | Interpretation |
|---|---:|---|
| Fresh first dispatch to last publication, median / p90 | 1.53h / 13.93h | Primary delivery baseline, conditional on completion |
| Fresh first dispatch to Done, median / p90 | 1.76h / 27.38h | Includes bookkeeping and any acceptance delay |
| Fresh uncovered time before publication | 663.17 card-hours; median 2.94m, p90 3.210h | No recorded task/landing; causes not established |
| Fresh uncovered time after publication | 975.12 card-hours; median 1.32m, p90 1.900h | Primarily closing latency; removal does not ship sooner |
| Documentation/evidence-only Found outcomes | 100 / 259 Found; 100 / 748 reported Reviews | 38.6% of findings; 13.4% of all reported Reviews |
| Those 100 Reviews | 33.08 dispatch-to-settlement hours; $790.65 stored cost | Includes initial useful inspection; subsequent repair cost is additional and unquantified |
| Other findings | 152 behavioral/safety/test-tool; 4 design; 2 integration; 1 unresolved | Only at least 11/259 have explicit branch/base comparison; 152 is not an introduced-regression count |
| Plan, fresh cards that ran it | 81/249 cards; 12.9m median, 25.5m p90 | Conditional summed stage duration per card |
| Exclusive Plan running time, fresh cohort | 22.72h of 697.88 running card-hours | Erasing Plan alone removes at most about 3.3% of observed running time |
| Exclusive Plan plus Review running time | 231.78h | Erasing both gives roughly 1.50x running-time reduction before extra repair; not throughput |
| Confirmed land creation to publication | 3.42m median, 4.51m p90 over 451 operations | Ordinary landing is a smaller target |
| Recovered checkpoint phases | 45.61h across 1,108 rows / 86 cards | Nonrandom partial sample: 17.24h builds, 8.60h startup, 15.84h tests, 3.37h slot waits, 0.55h teardown |
| Interim / Mutation / new SourceLanding use | No Interim observed; zero Mutation and new SourceLanding tasks | Removing Mutation has zero measured saving; enabling Interim has no measured product saving yet |

Card-hours sum across concurrent cards. Stage-runtime sums, the wall-clock partition, checkpoint-row sums and the all-task Review-cost population are different denominators. Do not add them into one savings estimate. Missing timing observations are unknown, not zero.

## Ground truth: assumptions versus implementation

| Card or report assumption | What the current code does | Design consequence |
|---|---|---|
| A documentation-only finding is already advisory | `server/Bundles/stage-review.md` explicitly makes missing evidence, unlisted/ungated runs and adequacy defects; requires ordinary reruns | Change the bundle and its tests together after approval; a one-off brief will keep conflicting with standing instructions |
| Stage completion automatically dispatches the next stage | `PipelineHandoff.cs` parses the handoff; `PipelineDefinitions.cs` lists stages. The owner describes dispatch as caller workflow, not an automatic transition engine | Start with completion-driven caller discipline and reason accounting; do not assume pipeline-definition editing implements dispatch |
| Confirmed publication already closes a card | `CardWorkTransitionService.Decide` moves successful settled work to Review; it has no Done decision | Add a separately authorized publication/acceptance close predicate; preserve human-move precedence |
| Interim can mean first-pass Code plus new tests | `InterimVerificationPolicy` requires a same-owner Final/Full baseline, explicit committed selection, per-card permission, readiness and a final-review latch | Current Interim is repair-round reuse, not train admission or first-pass minimal verification |
| Interim needs a **Clean** baseline | Owner prose/report says Clean; `RequireBaselineAsync` actually accepts **Clean or Found** from a succeeded delegate Review with completed Final/Full profile and matching identities; tests `C544_BaselineScope` / `C544_BaselineCompletion` cover baseline shape | Preserve this distinction. Found can provide completed baseline coverage, never landing approval. Reconcile prose in a later approved owner-doc slice |
| RC cutting has no reusable pre-land source primitive | `release-candidate.ps1` already exposes `SourceRef`, fetches it and pins `FETCH_HEAD`. `release-cut.ps1` does not pass it and defaults to master | Reuse the cutter; add exact expected-source and train-manifest binding to the orchestrated path. A source-ref option alone is not a train |
| The release lane is operational | `docs/release-gates.md` remains UNQUALIFIED. Investigation found no local qualifying receipts and did not audit live Windmill registration | Qualification is a hard rollout gate; this Plan makes no new claim about deployment |
| One card's Review approves an aggregate containing its patch | `LandApproval.LoadUsableEvidenceAsync` checks subject, SHA, ref, repository, clean-source assertion and supersession; Interim cannot approve. Recovery requires Final/Full | Aggregate approval must name the aggregate Code owner at aggregate SHA; member evidence stays bound to member source |
| All landing always requires an evidence ID | Unlatched ordinary landing retains an explicit caller-SHA approval path; rejected evidence never falls back; latched owners require Final evidence | Do not use caller-SHA approval to bypass train or Interim obligations. Its existing narrow use can support genuinely standalone advisory docs |
| A reviewed aggregate can be rebased and keep its RC green | Ordinary `AgentTaskLandingProtocol` prepares/rebases and verifies a resulting SHA; `LandingVerifier` is a build/test driver | Train publication needs an exact-candidate mode: no rebase or test-credit transfer to another SHA |
| Full RC means every platform and manual check | Schema-v2 `tests/test-execution-policy.json` RC profile is the automated Windows roster plus E2E, with owned exclusions | Explicit Linux, live, headed and manual obligations remain separate; count their cost |
| RC green is nightly readiness | Release credit and master-scheduled credit are separate and share native exclusion, not state | Keep master readiness and RC release evidence separate; do not promise one global long run/day under current policy |
| SourceLanding can verify each member concurrently against one aggregate operation | `SourceLandingAdmission` requires confirmed publication, a distinct same-board companion, matching repository/project and at most one open Mutation per operation | Use one operation-bound aggregate companion with a member obligation map; preserve pending status and external evidence custody |
| A task awaiting a train frees a seat after a timeout | Parking ships disabled; Blocked input waits have no automatic release deadline | Persist train readiness as data, settle workers, and confirm process ownership/release; never park workers merely to wait for a cut |

The two report refinements above do not invalidate the card's premise that pipeline waste exists. They narrow the new work needed. Further investigation is needed to attribute missing timings and handoff causes, not to restart this Plan.

## Decisions

These are proposed defaults, not enacted policy. The operator can approve D-1 through D-5 first and defer the train and Plan experiment. Record approvals on the card decision revision with scope and expiry.

| ID | Proposed decision and reason | Rejected alternative / explicit operator choice |
|---|---|---|
| D-1 | Optimize confirmed-publication throughput, cost per published card and p90 delivery; report Done lag separately. Set empirical stage targets below | Reject a 10x-50x promise and completed-card-only throughput claims |
| D-2 | Classify advisory docs/evidence findings separately; no Review rerun solely for a nonfunctional follow-up. Missing trustworthy required verification remains an incomplete gate, not a fabricated Clean or an introduced regression | Approve this distinction versus either keeping every paperwork issue Found or accepting all missing evidence as harmless |
| D-3 | Completion-driven handoff within 5m when eligible, with durable reason for delay. Pilot existing caller/watchdog paths before building automation | Approve bounded automatic continuation only for already-authorized stages; no new spend authority, cap override or provider reroute |
| D-4 | Auto-close only after confirmed publication **and** the recorded acceptance/deployment obligations; preserve Done = shipped. Before publication show “ready for release” in existing card/run metadata, not Done | Operator must explicitly choose if publication alone is enough for a category. Reject Done on Code success and any new board column |
| D-5 | Qualify CARD-0545/0487, CARD-0599 and CARD-0544 mechanisms before deferring ordinary work; keep their readiness separation | Reject enabling Interim or claiming RC credit from checked-in definitions or fabricated receipts |
| D-6 | Daily train only for opted-in, bounded, nonurgent cards; pilot up to 10 independent dependency groups per cut at 16:00 Europe/London. Keep an urgent Full lane | Approve cadence, membership cap and up-to-one-day queueing tradeoff; daily aggregation can worsen the current 1.53h median |
| D-7 | Low-risk members eventually use Code plus new/changed acceptance tests, with no per-card ordinary Review. One aggregate Review audits the exact aggregate and trusted RC receipts without rerunning the suite | Approve a new versioned train verification contract. Reject relabeling existing Interim or current Final/Full evidence; reject removing all independent inspection at launch |
| D-8 | Security, authorization, secrets, migrations, destructive/data-integrity work, concurrency/process custody, public contracts, dependencies, verification/landing/routing policy and uncertain impact remain Full Plan/TestDesign/Code/Review | Approve exclusions and escalation authority. Size/“easy” labels alone never establish low risk; this pipeline implementation is itself Full |
| D-9 | One aggregate Code owner, one immutable candidate, one exact target publication and per-member publication links. Conflict or target drift creates a new candidate | Reject N independent lands claiming the same aggregate receipt, force-push, rebase-after-green and a long-held repository mutation lease |
| D-10 | Retain SourceLanding companions and pending PCs; one union battery per aggregate operation if separately commissioned | Mutation was unused/paused in the measured window. This Plan neither reinstates it nor counts its removal as savings; operator decides commissioning and budget |
| D-11 | Randomized light-TestDesign-versus-Plan pilot, with ordinary Review unchanged and outside train treatment | Approve eligibility, sample, stop rules and expansion evidence below. Reject simultaneous Plan and Review removal as an uninterpretable experiment |
| D-12 | Rollout gates require measured savings without worse escaped defects; pause admissions on identity/readiness failure. Failure diagnosis budget is 4 engineer/agent wall-hours per cut before escalation, not permission to land red | Approve the budget, daily targets and rollback owner. A spent diagnosis budget leaves the candidate failed/pending, never green |

## First changes: remove measured waste cheaply

### Advisory docs and evidence repair

Use a finding taxonomy with independent fields for (a) product/acceptance behavior, (b) verification completeness, and (c) advisory documentation. Record introduced/inherited/unresolved attribution separately. A newly required acceptance behavior that is missing is substantive even when it is not a regression against the old product. An actual lost assertion, failed test, mismatched source, missing required run or unprovable build cannot become Clean by changing the label.

First policy slice: Review gives actionable behavioral findings and records advisory corrections without `next: code` solely for them. Required evidence that already exists can be attached/corrected in the stored report under the same source identity, followed by deterministic receipt validation; it does not require another model Review. If required execution is absent, the owner runs that exact missing selection once. An infrastructure failure or incomplete receipt is reported as incomplete; it is not counted as a product-regression finding. Historical findings remain historical; do not rewrite the 100 outcomes to improve the metric.

For source-changing advisory corrections, freeze the reviewed Code branch. Land its valid reviewed tip first, then commission a separate Docs helper and land its exact committed tip using the existing unlatched caller approval path under the new operator-approved narrow policy. The helper has no open product acceptance obligation and does not trigger another Review. A correction required for product correctness stays in the original change and gets the appropriate Review.

The allowed follow-up set starts with narrative prose outside instructions, policy, test selections, schemas, executable examples, generated inputs and configuration. A comment-only source change qualifies only with a language-aware token-equivalence proof that excludes directives, annotations, generated-code inputs, suppressions and behavior-bearing documentation; unsupported languages or uncertainty go to ordinary Review. Do not implement “all Markdown/comments are safe” by extension or regex. A new comment/doc SHA does not inherit the old Review or `reviewedSourceClean=true`; keep its separate approval and delta receipt. An Interim-latched source cannot use this escape hatch.

Expected saving: the 100 affected Reviews cost 33.08h / $790.65 before repairs. Initial Reviews still inspect behavior, so reclassification alone saves **zero** of that initial execution. Let f be the measured fraction of those hours spent on avoidable follow-up Reviews: removing them saves `33.08 * f` hours and corresponding per-task cost, bounded by 33.08h / $790.65 in that population, plus measured avoided repair time. Do not forecast f before the replay. The target is to remove at least 80% of advisory-only follow-up rounds; its comparable-window saving is `0.8 * avoidable-repeat-hours`, not an asserted 26.46h. CARD-1219's 138.46m and CARD-1154's 165.12m first-Found-to-Clean tails are diagnostic case studies, not fully removable runtime.

### Handoffs and closure

Reuse completion `next=`/handoff/artifact, pipeline status and the expectation watchdog. The authorized caller consumes a completion, checks blockers, source overlap, quota, stage/host limits and placement, then dispatches or records one durable reason with next observation time. Missing/unmarked handoff returns to its owner, not a new cold investigation. Before adding a dispatcher, measure settlement-to-notification, notification-to-consumption, eligibility-to-create and create-to-dispatch separately. Uncovered time is not automatically notification failure.

Initial operational target: 95% of eligible handoffs requested within 5m; unexplained eligible waits over 15m are investigated. Dependency, human decision, capacity, source overlap, deliberate priority and missing-evidence holds have separate counters. A later opt-in handoff service must deduplicate on predecessor task/attempt/settlement revision, stage and workflow-run revision; restart or duplicate delivery must not create a second child. Use current task creation and its refusals, never direct process launch. Tracker-triggered writes retain explicit YAML activation and spend acceptance.

Expected saving: 663.17 fresh pre-publication uncovered card-hours is the ceiling, not all avoidable. If attribution shows actionable fraction a and the intervention removes half, the comparable saving is `331.585 * a` card-hours. The stage target is 50% reduction of **actionable** uncovered intervals and 25% reduction of matched p90 publication latency. Report a and zero/negative savings if observed; no median multiplier is inferred from tail totals.

Closure begins with an explicit orchestrator close action on the correlated publication receipt, not a bulk calendar cleanup. Automation follows only after a shadow predicate matches human decisions. It requires the correct implementation owner/member mapping, current approval, confirmed remote publication, completed explicit acceptance/deployment, recorded pending companion if required, no open implementation task and no newer human move/reopen/hold. A landed Plan, a successful Code task or an unrelated same-card land never qualifies. Use `CardService` concurrency and terminal-move settlement behavior; do not silently cancel queued work by closing too early. Cleanup residue is a separate obligation and need not hide confirmed publication.

Expected saving: up to 975.12 fresh post-publication uncovered card-hours is bookkeeping exposure, **zero direct publication-speed saving**. With eligible fraction b, an 80% reduction saves `780.096 * b` closing card-hours. Target eligible publication/acceptance-to-Done p95 <=5m; preserve the raw publication-to-Done series to show acceptance time rather than subtracting it out invisibly.

## Release train design

### Admission, verification and “done”

Each member is a pushed, settled Code/Worktree source with repository/project/board identity, card revision, risk decision, immutable source SHA, patch range, dependencies, acceptance-test selection and measured result. Low risk means a bounded local behavior with a clear oracle and no D-8 area. Start with small changes (at most three production files and 200 changed production lines); treat these as screening limits, not proof of safety. Unknown or shared/native impact escalates to Full. A change to this train's own gate cannot approve itself using the experimental policy.

For the eventual selected lane, Code runs its new or changed acceptance/regression tests, including the failing trigger for fixes, and proves they assert observable behavior. Necessary compilation is retained. When a change has no new tests because an existing exact test covers it, name and run that test with a written justification; absence of a credible oracle excludes the card. A narrowly required adjacent smoke is named at TestDesign, rather than silently dropping a known safety check. Broader automatic regression runs move to the aggregate only under D-7. Record deferred test identities and excluded platform/manual obligations explicitly.

Reuse CARD-0544 selection references, committed selection sections, readiness snapshots, owner latches and fail-closed recovery patterns. Keep its **v1 repair semantics intact**: a real Final/Full Clean or Found baseline can support selected repair rounds only when qualified; a later Final/Full Review remains necessary for a v1-latched owner. Introduce a separate versioned `TrainSelected` verification/admission contract for first-pass selected Code. Do not send `verificationRound=Interim` without a baseline, forge a baseline, or mark deferred checks Full. Initially exclude v1-latched owners from the train; they finish through their existing Final path.

Before publication the Code task is Succeeded, while the implementation card remains in its existing Review state with a durable “ready for release <id>” reference. No session remains open merely to await the cut. “Code complete” and “train admitted” are useful milestones but are not implementation Done. Done follows confirmed aggregate publication and the card's explicit acceptance conditions, with pending SourceLanding named. If the operator instead wants Done before land, D-4 must be changed explicitly and reporting must still preserve a separate shipped clock; that is not this default.

High-risk members retain independent Full Review and named ordinary tests at their exact source, plus integration into the tested aggregate. They can use the urgent ordinary lane when delay is unacceptable. Manual, headed, Linux-only and live-provider obligations do not disappear into the Windows RC profile. In the first pilot they exclude a member unless already fulfilled and their validity at the aggregate is explicitly established.

### Aggregate source and ownership

1. At the daily cutoff, select topologically ordered dependency groups from eligible cards. Use one train coordination card and one newly assigned aggregate Code/Worktree owner T at exact current target B. Keep each member owner S_i distinct. No worker edits another task branch.
2. In T's own branch, integrate the pinned member patches in deterministic dependency/rank order. Record original source SHA C_i, base/range, patch digest, resulting aggregate commit(s) I_i and dependencies. Do not reuse a loose moving branch name as identity. Conflicts leave that group and its dependants out; resolution belongs to a fresh repair task, with appropriate tests/Review and a new membership version. An intentional integration edit is source work, never an invisible merge convenience.
3. Freeze a manifest with train id/version, repository identity, target full ref and B, T/ref, all board/card/task IDs and revisions, C_i/ranges/digests/I_i, selection references and deferrals, risk/approval policy revisions, member Review IDs where required and acceptance/companion obligations. Hash the manifest separately; it references A, so do not attempt to include its own hash in commit A. Persist it durably with candidate authority. Raw generated evidence remains ignored.
4. Push T's aggregate tip A. The existing producer-owned candidate clone fetches that exact ref and verifies A against the manifest before journal/push. Extend `release-cut.ps1` to select this explicit mode and pass `SourceRef` plus expected A; do not change the default master-cut behavior. Use the existing immutable `release/rc-...` candidate and candidate journal. The candidate branch never receives repair commits.
5. Run the existing RC census/runner at A, with its policy/script/build hashes, required UID roster, all attempts and report-delivery evidence. Required skips, zero/missing cases, missing TRX, source drift, contaminated fixtures or incomplete reporting are non-green. Add separately named required lanes when applicable. Use the native coordination lock and build-slot gate; hold no repository mutation lease throughout tests.
6. One aggregate Review independently inspects the composition, boundary risks, test adequacy and manifest. Under the **new versioned** train contract it validates trusted complete-green execution receipts for A and references them, rather than rerunning the same RC suite. It issues its own clean evidence for **subject T, ref T, repository and A**, with the profile/manifest digest and clean-source/build provenance. Member C_i evidence is supporting history, never copied as approval of A. Untrusted or incomplete receipts stop approval. Until this contract ships, existing Final Review obligations remain in force and a shadow pilot saves no duplicated execution.

The aggregate manifest and approval are new binding work. Reusing the cutter and release harness is not sufficient by itself. The settled train approval must be durable and immutable in the same sense as existing Review evidence, and every resume must revalidate current approval, membership, source and policy identities. New migrations are additive and generated through the repository's EF procedure.

### One publication, many cards

Request landing through the existing land service, with T as the sole landing owner, `-ExpectedSourceSha A` and T's current aggregate Review evidence ID. Add a typed exact-candidate mode on the persisted request/operation, carrying manifest/candidate authority and expected target B. A client flag/DTO for this is future work, not a currently valid command to run. Train mode requires the typed approval; the explicit caller-only path cannot waive it.

Under the normal repository mutation lease, revalidate A/ref/repository, clean source, the aggregate Review, complete-green receipt and member manifest; observe the exact remote target. If it is B, publish **A unchanged** as a fast-forward. If it already contains the same A, recover confirmed containment idempotently. If it has moved without containing A, refuse/supersede the candidate and rebuild from the new target; never rebase A and carry its green forward. An emergency ordinary land can therefore invalidate a train. Prefer a scheduled quiet publication window, not a long Git lease or unbounded worker session. Preserve the current push-intent journal, ambiguous-push recovery and cleanup separation.

Record one operation O with `VerifiedSourceSha=A` and observed remote R. Add durable per-member publication links `(train version, member owner/card, C_i, I_i, O, A, R, confirmation time)`. These links establish how the member patch shipped; they are not fabricated individual land operations or a claim that its original commit is an ancestor after cherry-picking. Validate patch containment before freeze and publication, including any resolved differences. Never call N ordinary member lands after aggregate publication merely to manufacture receipts. Member branches/worktrees retain normal explicit retirement authority; the manifest grants no deletion permission.

Update each member independently from the same confirmed O using idempotent projection and current card concurrency. A crash after target push but before card updates must recover the remaining links/moves without a second push. A newer human reopen or pending acceptance prevents that card's close; it does not unpublish siblings. Bind the train coordination card to O, so current SourceLanding identity rules have a real source card.

After target confirmation, reuse the existing tag/release publisher for the same A and candidate authority, adding a requirement that the train target contains A. Green tests alone do not authorize a pre-land public release. A failed GitHub publication resumes its existing journal; it does not undo a successful master update. Activation uses the existing canonical runbooks and desired/observed build probes. Whether a member requires release publication or deployment before Done is recorded at admission, never inferred later.

### Aggregate failure, conflict and revert

On red or incomplete aggregate evidence, publish nothing and retain the immutable candidate, manifest and all attempts. First classify build/fixture/coverage failure versus an actual test assertion. Re-run only the failed selection under the same isolated conditions, and run it at pinned B to distinguish inherited failure. An inherited red is not permission to release: repair the baseline or make an explicit policy decision; do not add a retry, widen a timeout or exclude it to obtain green.

For reproducible assertions, use the manifest's dependency groups to bisect ordered prefixes or dependency-closed subsets in disposable diagnostic candidates. Run the precise failing test(s), including necessary build/fixture prerequisites. Prefix binary search is valid only for a monotone failure; for interactions, test complements and dependency closures, then reduce the interacting set. Report “inconclusive” if infrastructure or nondeterminism prevents attribution. Diagnostic subsets earn no release credit. Record every tested SHA and selection so later accounting includes this cost.

Quarantine the implicated group and all dependants, or commission a forward fix. Never mutate the failed RC ref. Construct a new aggregate A2 from current B and the retained groups, link its predecessor, and run the **full required RC profile again**; the earlier green subset is not release approval. This is one long run per successful candidate attempt, not a guarantee of one run per day. The 4h diagnosis cap prompts a decision to defer/split/fix; it never releases a red train. Do not hold all good cards indefinitely: after one failed daily slot, offer smaller dependency-closed trains or their normal Full route, with explicit commissioning costs.

Before land, removing a card is recomposition, not a master revert. After confirmed publication, a real regression is repaired or reverted through a new reviewed source and landing. Revert the implicated card patch plus dependent changes when safe, or the full aggregate when independent removal cannot be proven; never reset/force-push target history. Security/data migrations retain their explicit rollback/forward-repair plan and cannot be mechanically reversed. Keep old release tags, evidence and member history; mark supersession and actual running version separately.

### SourceLanding and readiness

Reuse one distinct same-board verification companion for train O, containing each member's pending PC IDs, risk-specific controls, plan and original Review coordinates. Commission one fresh `Mutation -Worktree -SourceLanding O` only when separately authorized; its exact source is A, even if master moves. Current admission serializes open work per O. All member obligations remain pending until individually disposed, including zero-PC discovery obligations when commissioned. Native platform controls may require sequential tasks on the same O after prior settlement/restoration; do not evade same-O exclusion with fake operations.

Snapshot execution remains local inherited children only, with no snapshot commits/pushes and restoration/evidence in the assigned external root. Findings create forward remediation at current target and a new operation for re-verification; a surviving mutation is not itself a production regression. Cleanup uses `CleanupVerification`, never generic deletion. This reuses SourceLanding; it does not move post-land Mutation into pre-land RC or silently restart the paused program.

Keep scheduled master readiness and the RC lane separate. Under current policy the daily train adds an RC run beside the nightly master run; RC success cannot refresh the master readiness receipt. A future proposal to combine their credit must separately prove scheduling, current-master identity, report delivery and watchdog semantics. It is excluded from this rollout's claimed saving.

### Expected train economics and latency

Measure per successful train `saved = sum(counterfactual per-card broad build/test/Review execution) - (aggregate build/test + aggregate inspection + bisect/repair + extra platform checks + qualification amortization)`. Count each producing build once; keep slot wait, startup, execution and human waiting separate. Estimate the counterfactual from matched cards after the cheap cleanup, not from all historic Code duration.

The 45.61h observed checkpoint sample is an exposure ceiling for those recovered rows, not a forecast of eliminated time. Build/startup total 25.84h there; repeated work is plausible but not yet attributed. First economic gate: at least 30% lower total verification agent/host minutes per published eligible card over ten successful trains, including failed candidates and diagnostic costs. Report agent cost and host time separately. Measure overall published cards/day under comparable staffing and WIP; an expansion target is 20% higher publication throughput, not a guarantee.

A once-daily cutoff adds 0-24h queueing; under uniformly distributed readiness the mean is 12h and p90 21.6h **before** RC execution. That assumption is a planning illustration, not observed arrival data. Daily release throughput and fast individual publication are different objectives. Report ready-to-publication p50/p90, oldest ready item and missed cuts; target 95% of eligible accepted members published within one successful daily cycle (cut wait + measured RC duration + 15m publication allowance). Urgent work retains Full review/verification and can land earlier.

## Experiment: can light TestDesign replace Plan?

Run this after advisory/handoff cleanup stabilizes, while ordinary per-card Code/Review and verification remain unchanged. Do not enroll train members in the same experiment. The treatment tests design omission, not a different model, less testing, different WIP or removal of Review.

Eligibility: diagnosed bounded change, explicit acceptance criteria, reversible local implementation, at most three production files/200 changed production lines, no unresolved design alternatives/dependencies and no D-8 risk. Stratify by easy versus bounded medium and component; log exclusions before assignment. Unknown risk goes to Plan. Use a reproducible randomized assignment recorded before dispatch, with intended treatment retained if the card later escalates.

- Control: current Plan (including folded TestDesign for easy cards) and ordinary downstream stages.
- Treatment: one light TestDesign artifact in the established plans directory: problem/acceptance, code ground truth, chosen minimal approach, touched files, exact new/changed tests, adjacent obligations, risks and rollback. Aim for one page and roughly ten minutes of authoring; that is a cost target, not a timeout that kills a Working session. TestDesign may return to Plan whenever a real design decision appears; count that time and the extra handoff in treatment.
- Initial cohort: at least 40 assigned cards per arm over at least two weeks, extending to four if enrollment is slow. Review independently classifies design-caused repair, inherited failure, evidence-only correction and escaped defect. Record denominators, assignment, complexity, model/tier, calendar occupancy and all failed/unfinished cards. A review rubric can hide assignment where feasible; the artifact format prevents claiming full blinding.
- Primary benefit: at least 15% lower median total stage-running minutes per eligible card and at least ten minutes lower median dispatch-to-publication, with matched p90 no worse by more than 10%. Bootstrap intervals and raw counts accompany point estimates; an inconclusive benefit result does not justify expansion. Plan's 12.9m conditional median and 22.72 exclusive hours are ceilings/context, not expected per-card savings; light TestDesign consumes time too.
- Safety: zero attributable severe security/data/service-outage escapes; treatment's design-caused repair-rate increase must be <=5 percentage points and its one-sided 95% upper confidence bound must also be <=5 points before broad adoption. Include seven- and fourteen-day post-publication observations and uncompleted repair work. Stop new assignment immediately on a severe attributable escape; return affected scope to Plan on repeated missed design invariants.
- Small pilots will rarely establish the safety margin. For example, zero escapes in 40 observations still has an approximate 7.5% upper rate bound by the rule of three. If safety evidence is inconclusive, retain a bounded pilot, extend enrollment under an explicit budget, or keep Plan; never describe zero observed escapes as proof of safety.

After the initial cohort, decide independently whether to keep, narrow, extend or abandon treatment. Dropping Plan fleet-wide is not the automatic outcome. Do not sum Plan and TestDesign medians from different conditional populations to forecast savings.

## Rollout, targets and rollback

| Stage | Work and entry gate | Exit target / observation | Rollback or stop |
|---|---|---|---|
| R0: attribution | Preserve investigation baseline; collect 7 days / at least 50 newly dispatched cards, including unfinished ones | >=95% of build/test invocations have task/SHA/run/phase identity; every handoff over 15m has a reason or explicit unknown; all deployment attempts have correlated start/end/version facts | No policy change; gaps remain unknown and train saving cannot be sized |
| R1: advisory/handoff cleanup | D-2/D-3 approved, bundles/tests updated together; no weaker executable verification | Over 50 comparable cards: >=80% fewer advisory-only follow-up rounds, 95% eligible handoffs <=5m, actionable uncovered time down 50%; aim p90 publication down 25%, report actual | Restore prior bundle revision or suspend automation on misclassification/duplicate dispatch; keep receipts and corrected metrics |
| R2: closure | D-4 approved; shadow at least 30 publication/acceptance events with zero false-close proposals | 100% predicate agreement with reviewed decisions, then eligible close p95 <=5m and no publication-only false Done | Disable auto-close; preserve normal transitions and manual close. Any false close stops the pilot and gets explicit repair, never hidden history edits |
| R3: qualification / Interim | Accepted existing nightly/watchdog/RC qualification plus D-5; do not infer operational readiness from files alone | Manual and real scheduled master green, independent watchdog/outage recovery, RC complete roster including E2E and two real slot observations, exact tag/manifest, real Interim admission/refusal and later Final discharge | FullOnly and Interim disabled on lost readiness; retain journals. Existing latches still require Final |
| R4: train shadow | New ownership/approval path reviewed; freeze candidates without target publication, while members use normal route | At least 5 shadow aggregates of 3-10 independent groups; replay wrong SHA, moved target, conflict, crash-after-push and failing aggregate; zero false approval/duplicate member close | Remain on ordinary route; shadow tests do not count as speed savings or scheduled-master credit |
| R5: limited daily train | D-6 through D-10 approved; qualified RC, all R4 safety checks; first 3 trains keep per-card Full Review | Then up to 10 groups/cut in selected lane; over 10 successful trains plus all failures: >=30% lower verification minutes/card, >=95% meet daily-cycle target, zero severe attributable escapes; report 20% throughput target against observed result | Disable new train admission, keep candidate state; finish selected members through Full ordinary route with fresh owner-specific evidence or a still-valid authorized train. Never waive obligations or force a target update |
| R6: Plan experiment | D-11 approved; R1 stable; separate non-train cohort | Sample, time benefit, repair noninferiority and follow-up windows above | Restore Plan for new assignments; count escalations and outstanding repairs in original treatment |

R6 can run beside train qualification on a separate cohort; it cannot share treatment cards with R5. Expansion requires an operator decision with the actual savings and failure data. Do not relax concurrency to manufacture a throughput result.

Measurement retains first-dispatch-to-publication, first-dispatch-to-Done, ready-to-cut, publication-to-acceptance, acceptance-to-close, active task intervals, classified blocked/handoff time, verification phases, repair rounds and stored cost. Include scheduled/canceled/unfinished cards with censored ages; use consistent nearest-rank percentiles for comparison to the investigation. For train cost, divide shared work across included cards and retain the undivided total, including excluded/failed membership attempts. No card inherits a zero test cost because it ran in a shared job.

## Implementation slices and TestDesign handoff

Qualified paths below are repository-relative. Bare service filenames resolve under `server/Application/Services/`; entities under `server/Domain/Entities/`, DTOs under `server/Application/Dtos/`, and settings under `server/Application/Settings/`. Bare test filenames resolve under `tests/Antiphon.Tests/Application/`, except release-gate script tests and the proposed `PipelineSpeedReportTests.cs` / `ReleaseTrainDiagnosisTests.cs`, which belong under `tests/Antiphon.Tests/Scripts/`; `ReleaseGateActivationTests.cs` is an application test. “New” denotes a proposed file, not existing behavior. Keep slices separate so cheap changes need not wait for the train. Each approved code/policy slice gets ordinary independent Review; nothing here exempts the pipeline's own safety changes.

| Slice | Files / boundaries | Named tests and required observations |
|---|---|---|
| S1: attribution | Extend `scripts/stage-value-report.ps1`; new `scripts/pipeline-speed-report.ps1`; use `tools/Antiphon.Checkpoints/Report/PhaseTimings.cs` and `CheckpointApp.cs` receipts; deployment owner scripts only if required facts are missing | New `tests/Antiphon.Tests/Scripts/PipelineSpeedReportTests.cs`: overlapping intervals, censored unfinished cards, unknown timing, shared-build deduplication and two population denominators. Deployment instrumentation requires its own scoped tests before changing a deploy driver |
| S2: finding/advisory policy | `server/Bundles/stage-review.md`, `stage-code.md`, `orchestrator.md`; `StageOutcome.cs`, `StageOutcomeService.cs`, DTOs if typed classifications are introduced; new `AdvisoryChangePolicy.cs`; owner-doc amendments | Extend `InstructionBundleTests.cs`, `StageOutcomeSummaryTests.cs`, `ReviewEvidenceParserTests.cs`; new `AdvisoryChangePolicyTests.cs`: narrative-only admit; directives, plan/test-selection changes, mixed code, invalid receipts and unknown equivalence refuse; old source evidence never approves a changed SHA |
| S3: handoff ownership | First brief/watchdog operation only. If R0 shows a dispatch gap, new `PipelineContinuationService.cs` and durable intent entity wired through current task create; `PipelineHandoff.cs`, `CardWorkflowRunFactory.cs`, expectation services | Extend `PipelineHandoffParseTests.cs`, `ExpectationPipelineTests.cs`; new `PipelineContinuationTests.cs`: duplicate/restart, newer revision, blocker/cap/refusal, no unauthorized spend, report-to-complete-UserPrompt delivery where input is changed |
| S4: confirmed close | `CardWorkTransitionService.cs`, `CardWorkTransitionSettings.cs`, `CardService.cs`; new `PublishedCardCompletion.cs`; durable acceptance/publication linkage and additive migration as needed | Extend `CardWorkTransitionServiceTests.cs`; new `PublishedCardCompletionTests.cs`: Plan-only land, false publication, active task, newer human move, missing deploy/companion, residue, duplicate/restart and partial train projection |
| S5: existing qualification | `docs/investigations/<date>-card-0487-nightly-qualification.md`, CARD-0544 pilot and CARD-0599 qualification records; existing register/nightly/release scripts and settings through their owners | Existing `ReleaseGateActivationTests.cs`, `InterimVerificationPolicyTests.cs`, `InterimVerificationLandGuardTests.cs`, release gate script tests; actual qualification receipts required. This is commissioned operating acceptance, not a new scheduler or forged fixture acceptance |
| S6: selected admission and train composition | New `TrainVerificationPolicy.cs`, `ReleaseTrainService.cs`, `ReleaseTrain.cs` / member entities and DTOs; `AgentTaskService.cs`, `PipelineDefinitions.cs` only where the approved workflow requires; new additive migration; preserve `InterimVerificationPolicy.cs` v1 | New `TrainVerificationPolicyTests.cs`, `ReleaseTrainCompositionTests.cs`: first-pass/v1 separation, risk exclusion, changed source, dependencies, patch mapping, conflicts, immutable manifests and selected-to-Full fallback. Existing Interim tests stay green |
| S7: candidate execution/approval | `scripts/release-candidate.ps1`, `release-cut.ps1`, `lib/release-gate.ps1`, `lib/release-authority.ps1`, `publish-release.ps1`; `ReviewEvidenceBindingService.cs`, `StageOutcome.cs`; new `TrainApproval.cs` | Extend `ReleaseGatePublicationTests.cs`, `ReleaseGatePublicationAuthorityTests.cs`, `ReleaseGateRunTests.cs`; new `TrainApprovalTests.cs`: exact A/manifest/producer/policy binding, stale/missing UID/TRX, mismatched source, no pre-land release, receipt reuse only for same A |
| S8: exact aggregate land | `LandApproval.cs`, `AgentTaskLandService.cs`, `AgentTaskLandingProtocol.cs`, `AgentTaskLanding.cs`, land request/DTO/CLI, `LandingVerifier.cs` or its adapter for validated train authority; publication-member projections | New `ReleaseTrainLandingTests.cs`; extend `AgentTaskLandApprovalRequestTests.cs` / recovery tests: wrong owner/evidence, caller-only refusal, no rebase, target race, lost push acknowledgement, restart at each intent, O/A/R preservation, partial member update and no double publication |
| S9: failure/recovery/SourceLanding | New `scripts/release-train-diagnose.ps1` as a manifest-bound wrapper over existing checkpoint execution; `SourceLandingAdmission.cs` only if projection needs it; member obligation map and owner-doc procedure | New `ReleaseTrainDiagnosisTests.cs`, `ReleaseTrainPublicationProjectionTests.cs`; extend `SourceLandingAdmissionTests.cs`: dependency/complement diagnosis, no diagnostic green credit, recut/full rerun, same-O exclusion, exact A after target moves, restored snapshot and retained residue |
| S10: Plan experiment | Approved stage bundle/pipeline revision or explicit pilot commissioning; new `docs/investigations/<date>-card-1281-plan-pilot.md`; use S1 census | Extend `PipelineDefinitionServiceTests.cs`, `InstructionBundleTests.cs` if code/bundles change; new `PlanPilotAssignmentTests.cs` if assignment is automated. Reproducible strata, persisted assignment, risk exclusion and escalation counted in treatment |

Do not commission S1-S10 as one Code task. S2/S4/S6-S9 touch evidence, state or publication authority and stay in the Full lane. TestDesign must read the relevant persistence/resilience and session owners before designing changed asynchronous paths and migrations. Preserve the code-reviewed-source freeze and commit/push rules for every implementation slice.

### Checkpoints

Code is later needed, so this section records the required checkpoint **inventory**. TestDesign must replace it with the standard importable CP/After/Build/Group/Filter/Covers/Expect/Min/EstimatedMinutes table, exact expanded execution floors and V/R/PC bindings for each commissioned slice. Do not run a build from this planning inventory or pretend the proposed new classes exist. No `## Verification design` is supplied in this separate-stage Plan.

| Checkpoint | Slice | Lane | Selection / obligation to bind in TestDesign |
|---|---|---|---|
| CP-1 | S1 | Portable script + application | `PipelineSpeedReportTests`: trace replay, attribution/denominator/unknown controls; retain the investigation examples as expected aggregates |
| CP-2 | S2 | Portable application + script | `InstructionBundleTests`, `StageOutcomeSummaryTests`, `AdvisoryChangePolicyTests`, affected Review evidence parser tests; deny unsafe docs/comments and missing proof |
| CP-3 | S3 | Application integration, eligible OS | `PipelineContinuationTests`, affected `ExpectationPipelineTests` / handoff cases; task-create and recipient delivery/restart witness, not only a fake callback |
| CP-4 | S4 | Application/database integration | `PublishedCardCompletionTests` and affected `CardWorkTransitionServiceTests`; real transaction/concurrency and human-move/acceptance controls |
| CP-5 | S5 | Windows RC + scheduled master qualification | Existing qualification rosters from their owner plans, distinct credit, independent watchdog and real recipient receipts; keep manual requirements separate |
| CP-6 | S6 | Portable application/database/Git integration | `TrainVerificationPolicyTests`, `ReleaseTrainCompositionTests`, affected Interim guard tests; real refs and dependency/conflict fixtures |
| CP-7 | S7 | Portable script contracts, then Windows native RC | `TrainApprovalTests`, affected release publication/authority/run cases; frozen A, census and trusted build/report evidence. Shadow and real RC are separately recorded |
| CP-8 | S8 | Git/database integration; Windows where native locking is required | `ReleaseTrainLandingTests`, affected land approval/recovery cases; exact target update, fault cuts and immutable resumed authority |
| CP-9 | S9 | Portable diagnosis/Git + SourceLanding custody lane | `ReleaseTrainDiagnosisTests`, `ReleaseTrainPublicationProjectionTests`, affected `SourceLandingAdmissionTests`; inherited/interaction failures, forward repair/revert and exact operation custody |
| CP-10 | S10 | Portable assignment / pipeline contracts | `PlanPilotAssignmentTests` if added, affected pipeline/bundle tests; assignment/exclusion/escalation denominators and historical compatibility |

Each executable row will name its platform requirement in its lane text/commissioning record, without adding an unsupported column to the checkpoint importer. Omit `-Runner` unless pinning a particular host is necessary. Omit `-Platform` for portable work; scope `-Platform Windows` to actual Windows qualification. `-Platform Any` deliberately removes an inherited requirement. The RC Windows coverage remains distinct from Linux-only or manual coverage.

Build/test drivers take the host build-slot gate; the checkpoint tool is the ordinary front door, one run per committed slice group and awaited to terminal completion. Use isolated `bin-<slice>/` outputs, freeze source through each run, validate clean SHA/build receipts, keep generated evidence ignored, and remove owned alternate outputs after execution. Broad runs need the approved coverage rationale and cost. TestDesign must budget each slice's current Full requirements until the experimental selected policy is actually approved and activated; this plan is not permission to skip today's Unit/named ordinary scope.

## Input waits, ownership and operating limits

This design adds durable queue states, not sessions sleeping until tomorrow. Ready members settle and publish their branches; normal terminal release must leave a process killed, intentionally warm or owned by a standing agent, with any conditional release debt visible. A failed release is not assumed successful because the task settled.

If a delegate needs an operator decision, put the question and source identity on the card move/reopen revision and return its report. **What releases a waiting session, and after how long?** An authorized cancellation/stop, process exit, or a qualified conditional parking release can release its seat; a Reply resumes work and does not free the seat. With `BlockedTaskParking:Enabled=false`, there is **no automatic release deadline** (CARD-1083). The 5m/15m handoff targets, daily cutoff and 4h diagnostic budget are detection/decision thresholds, never kill timers. Do not enable parking as a hidden throughput change. Inspect existing Blocked seats and answer/cancel only under the caller's authority; never auto-stop Working sessions to meet a metric.

## Acceptance of this Plan

The deliverable is this committed, pushed document with decisions, current-code discrepancies, quantified opportunity limits, implementation slices and TestDesign input. No build or test is required to validate this documentation-only planning change. Verify Markdown links and named existing paths, clean diff and assigned branch publication; report the full commit SHA and merge-tree compatibility with fetched master. The next stage is **decide** because the proposal is complete under D-1 through D-12. After decisions, the next implementation step is **test-design**, beginning with approved cheap slices and measurement; it is not direct Code or settings activation.

## Verification design

TestDesign, 2026-10-15, task `12ca0ba3`; inspected base `ec8552a7d7ee76ca00a4af0ed44e3e1cabfad2b4`. Commissioning brief records operator approval of D-1..D-12 and the label **ready for release**. This appendix does not rewrite the design above. Scope is S2/D-2, S3/D-3, S4/D-4, S5/D-5, in that order. No implementation, qualification or PC result is claimed here.

**Handoff: Plan for the three binding gaps below, then Code per independent slice.** Proposed test names are contracts to implement, not existing passing cases. An unbound payload remains **binding required after Code**; a mock callback, prose pin or syntactically valid checkpoint table cannot discharge it.

| Binding | Evidence / required Plan output | Affected acceptance |
|---|---|---|
| B-1: advisory evidence correction | `ReviewEvidenceRecoveryService.RebindAsync` recovers an already-Clean retained report with confirmed sync and Full scope; `StageOutcomeService.RecordFindingAsync` appends an override, not validated execution. Name the authoritative correction input, validator, append-only audit and approval consumer for attaching existing receipts. Do not manufacture Full/Clean with an override. | V-2/V-3; S2 receipt-correction sub-slice only |
| B-2: caller action and delay record | Completion delivery has durable identity; `ExpectationPipelineTests` tests queued-work age (10m), not settled-to-next-stage consumption. Name the existing caller operation that persists consumption/eligibility/reason/next-observation and the readback joining its child request. If no such operation exists, plan that small seam; do not substitute a new dispatcher for the approved caller pilot. | V-8/V-9/V-11 and five-minute operating acceptance |
| B-3: close authorization | `CardWorkflowRun` has no acceptance/publication metadata record; `Card` has labels, notes and revisions. Name the versioned obligation record, authorized writer, source/card revision binding, and atomic recheck before terminal move. `ApplyAutomatedMoveAsync` reloads the card after the sweep's decision: stale decision versus newer human move needs a barrier test, not only optimistic concurrency after reload. | V-12..V-17 |

| Independent slice | Exclusive source area; fixtures/tests owned by this slice | Dependencies / land boundary |
|---|---|---|
| A / S2 | `server/Bundles/stage-review.md`, `stage-code.md`; proposed `server/Application/Services/AdvisoryChangePolicy.cs`; evidence classification/correction services and DTOs if B-1 requires them; new `tests/Antiphon.Tests/Application/AdvisoryChangePolicyTests.cs`. Existing `InstructionBundleTests`, `StageOutcomeSummaryTests`, `ReviewEvidenceParserTests` are read-only regression inputs. | Bundle/classification can land before receipt correction. Owner prose for evidence belongs to A. No edits to `orchestrator.md`, card transitions or release drivers. Comment-equivalence admission stays refused until its proof is implemented. |
| B / S3 | `server/Bundles/orchestrator.md`; caller/watchdog procedure in `docs/orchestration-loop.md`; new `tests/Antiphon.Tests/Application/PipelineHandoffPilotTests.cs` and slice-local harness. Existing completion queue, parser and watchdog are reused; B-2 must name any necessary production adapter. | Caller pilot only; no automatic continuation service. Read A's policy without editing its files. Do not edit `InstructionBundleTests` or shared delivery fixtures; add slice-local tests/extensions. |
| C / S4 | `CardWorkTransitionService.cs`, `CardWorkTransitionSettings.cs`, `CardService.cs`, proposed `PublishedCardCompletion.cs`, `docs/agent-card-lifecycle.md`; new `tests/Antiphon.Tests/Application/PublishedCardCompletionTests.cs` and slice-local fixture. | Ordinary single-owner publication only. B-3 first; shadow at least 30 events before enablement. Any shared EF model/Program registration changes from A and C require a separately sequenced integration slice; they are not parallel-safe. |
| D / S5 | Qualification records under `docs/investigations/` for CARD-0487, CARD-0599, CARD-0544; existing qualification scripts/settings through their owners. No runtime source edits in this qualification task. | Portable guard preflight may run independently. Live steps are sequential: 0545/0487 -> 0599 -> 0544. Repairs return to their owning Code task, not this slice. |

Parallel Code means disjoint declared paths, separate branches/build outputs and current cap checks, not simultaneous edits to common tests, owner documents, DI or migrations. No slice requires another slice's unlanded source. B-1..B-3 must be resolved before commissioning their dependent work; this is not a closed-list authorization to invent those seams in Code.

### Inspection

| Bodies read (paths relative to repository) | Boundary -> coverage or exclusion |
|---|---|
| Full CARD-1281 plan; `docs/project-context.md`; relevant checkpoint, coverage, mutation and delivery sections of `docs/testing-and-build.md`; caller/close sections of `docs/orchestration-loop.md`; lifecycle, session delivery/custody and resilience owners | Full ordinary verification retained; no write retries or five-minute kill timer |
| `InstructionBundleTests`: composer, C470 stage separation, C467 delivery inventory, C1083 source/render equality, C1213 read-only precedence; their `PinSentence`/normalization helpers; shipped `stage-review.md` | V-1/V-2; R-1. Text checks prove shipped instructions, not model obedience |
| `StageOutcomeSummaryTests` (all bodies/Row/context), `ReviewEvidenceParserTests` (all bodies/Block/Report); `StageOutcome`, `RecordFindingAsync`, `ReviewEvidenceRecoveryService` | V-2/V-3/V-7; B-1; R-1 |
| `PipelineHandoffParseTests` and Block helper; `ExpectationPipelineTests.C650_Queue_age_and_dispatch_progress_use_exact_boundaries`, quota/fence cases and Directive/Snap helpers; `CompletionNoteWorkQueueTests` | V-8/V-9/V-11; R-2. Queue age does not establish caller consumption |
| `CompletionWarningDeliveryTests` receipt/recovery/SettlePlan bodies; `C544DeliveryRig` including all persistence cuts, submit callback and restart; `VerificationRoundDeliveryTests.AssertReceivedOnceAsync`; `C544World` service-boundary setup | V-10; real queue with substitute terminal; B-2 starts after this proof |
| `CardWorkTransitionServiceTests` sweep cases and full nested harness; `CardWorkTransitionService`, settings, `CardService.ApplyAutomatedMoveAsync`, `Card`, `CardWorkflowRun`, `AgentTaskLandingState.HasPublication`; `LandingGitFixture.InitializeAsync`/observer helpers and `AgentTaskLandPublicationTests.C494_LostPushAckReconcilesRealTargetBeforeSourceChecks` | V-12..V-18; R-3. Card harness has real PostgreSQL/CardService, zero launch adapters; private bare remote provides independent publication witness. `LandingProtocolHarness` instead substitutes Git and cannot prove publication |
| `ReleaseGateActivationTests` including ActivationState; `InterimVerificationPolicyTests` permission/baseline cases and update/refusal helpers; `InterimVerificationLandGuardTests` bodies/identity helpers | V-19/V-21; R-4. Fabricated activation files are negative-test fixtures, never qualification receipts |
| `ReleaseGateRunTests`; `Test-C599_CreditVerdicts`, New-C599RunFx/Invoke-C599Run in `scripts/test-release-gate.ps1`; `ScriptHarness.RunHarnessCaseAsync`/Validate | V-20; R-4. Harness PASS rows are assertions, not TUnit result counts |
| `NightlyWatchdogCoreTests.C545_IntentBeforeNetwork`, `C545_ReceiptIdempotent`, `C545_ReaderHeldWithoutSession`; `C545World` ledger/transport/reader setup; `docs/nightly-watchdog.md`, `docs/release-gates.md`; CARD-0545 S6 and CARD-0599 activation Q-1..Q-4 | D delivery inventory; Q-1..Q-3 below. Existing RC activation plan explicitly leaves scheduler entrypoint/settings unbound; no `ReleaseGateSchedulerTests` or `ReleaseGateQueueTests` exists at this base |

### Delivery inventory

| Path / durable join | Producer -> destination; persistence boundary | Recovery and observable receipt / tests |
|---|---|---|
| B completion: task/attempt + completion source-event ID -> notification ID -> queue ID + destination session/generation + baseline sequence + wire digest | Real `AgentTaskReplyService` settlement -> completion obligation -> notification scanner -> real `SessionMessageQueueService` -> caller | V-10 uses `C544DeliveryRig`: already eligible and busy-then-TurnEnd; cuts `obligation-insert`, `settled-committed`, `note-insert`, `note-committed`, `wakeup-dropped`, `render-committed`, `spill-written`, `attempt-committed`, `prompt-accepted`. Prove each cut fired, recreate services, recover the same obligation/queue identity, then exactly one complete matching UserPrompt above its attempt floor. PC-21..PC-25. |
| B continuation: predecessor task/attempt/settlement event + next stage + workflow-run revision -> caller decision -> child task | Recipient consumes completion -> current eligibility/authority checks -> existing task creation; reason/next-observation persisted before acknowledging a held handoff | V-8/V-9/V-11 must recover after consumption, before child-create, and after committed create with lost response. One correlated child, or durable classified delay; never an unrecorded gap or duplicate child. **B-2 binding required**: current delivery receipt proves no child request or model decision. PC-13..PC-20, PC-26. |
| C publication projection: operation/implementation owner/card + source/ref/repository + obligation/card revision -> close revision | Confirmed landing plus recorded obligations -> close predicate -> `CardService` terminal write | V-13/V-15/V-17: crash before write, after durable write before acknowledgement, duplicate sweep, changed obligation/human revision. Fresh DB and CardService read show exactly one Done revision for the same operation; no second push or canceled queued implementation. B-3 binds the record. PC-27..PC-40. No session input added. `CardChanged` alone proves nothing. |
| D nightly/watchdog: due-day/namespace/outage epoch -> nid/attempt/body hash/recipient peer; master job/native run/policy/script identity | Independent watchdog SQLite intent before network -> Telegram transport -> authorized recipient reader; nightly report -> its existing recipient path | Q-1 must observe failure **and** recovery in the recipient view after delayed reader, already available reader, restart before send, acceptance with lost acknowledgement and reader outage. Existing C545 ledger tests are preflight only. Reuse CARD-0545's delivery/control inventory; never mark received from sent/message-id/read marker. |
| D RC: candidate SHA/ref/policy/native-run/job/slot -> report obligation -> board incident/revision; tag/release/assets | Real candidate runner -> report destination and publication readback | Q-2 requires complete board GET body/revision and independent remote tag/release/asset readback. Real queue busy/eligible and handoff crash coverage is owed by CARD-0599 activation Q-2; absent scheduler/queue bindings remain an upstream prerequisite, not a fake C1281 test. |

- Substitutes: `FakeAgentProtocolAdapter.OnSubmitted` records what was actually submitted; it cannot prove native provider ingestion or caller reasoning. No test inserts its expected UserPrompt as a success shortcut. `C545World` uses real SQLite but fake Windmill/Telegram/MTProto and time; it proves durable protocol logic, not live availability, credentials or delivery. Release script seams prove refusal logic, not an RC run. DB projection proves persisted state, not browser refresh.
- If B changes notification transport or C adds a new notice, extend this inventory and bind a producer-to-recipient test before Code closes. No new transport is authorized by the current cheap slices. Session acceptance always needs the matching **complete UserPrompt**, never a request, insert, event, Sent flag or ack.

### Proves it works now

Recipe keys: **A** = new `AdvisoryChangePolicyTests`, real composed bundles plus planned policy/production correction boundary (B-1); **B** = new `PipelineHandoffPilotTests`, C544 queue rig plus the caller operation bound by B-2; **C** = new `PublishedCardCompletionTests`, isolated migrated PostgreSQL, real CardService, private Git publication fixture and B-3 record; **D** = existing qualification fixtures named below. For new methods commit the failing test before implementation, observe an intended assertion failure against pre-fix behavior, then run its CP green. Missing symbols/build failure is not acceptance red. Existing D checks may already pass: record that honestly and use their post-land PC for sensitivity, not an invented regression.

Each row specifies recipe, variant/case, oracle, reachability and classification. Variant loops contribute one execution unless `[Arguments]` is explicitly prescribed. Exact methods below belong to `tests/Antiphon.Tests/Application/`, except `ReleaseGateRunTests` under `Scripts/`. A/B/C new classes are Integration; give process-spawning fixtures `ParallelLimiter<ProcessSpawnLimit>`, B the existing MessageQueue serialization, and C isolated schemas plus global-sweep serialization. Keep their fixtures slice-local so parallel authors do not edit shared helper files.

| ID / behavior | Recipe; exact test | Variant/case -> decisive oracle | Reachability / classification |
|---|---|---|---|
| V-1 advisory-only disposition | A; `AdvisoryChangePolicyTests.C1281_AdvisoryDoesNotRerun` | Valid Final/Full source + narrative correction: no new Review/Code solely for advisory; retain independent behavior/evidence/advisory fields and attribution | Compose actual Review/Code text and invoke policy; new acceptance, red-first. A phrase pin alone cannot prove the caller action |
| V-2 substantive and incomplete stay gates | A; `AdvisoryChangePolicyTests.C1281_IncompleteNeverClean` | Missing acceptance behavior, failed assertion, absent required run, missing provenance, wrong receipt: substantive/incomplete retained; no usable Clean/Full grant | Feed each through production classification/approval consumer; new safety acceptance, B-1 required |
| V-3 existing evidence correction | A; `AdvisoryChangePolicyTests.C1281_ReceiptCorrection` | Same-source valid receipt attached twice: one audited correction, zero new Review tasks; absent execution requests only missing selection; wrong subject/SHA/ref/repository refuses and leaves original evidence intact | Real stored report/receipt validator; include restart; new acceptance, B-1 required |
| V-4 separate Docs follow-up | A; `AdvisoryChangePolicyTests.C1281_SeparateDocsIdentity` | Original reviewed branch frozen and published first; new Docs tip has separate caller approval/delta, never old reviewedSourceClean; Interim-latched helper refuses | Scratch Git refs and real land request boundary; new acceptance |
| V-5 safe narrative eligibility | A; `AdvisoryChangePolicyTests.C1281_NarrativeBoundary` | Ordinary narrative admits; instructions, policy, test selections, schemas, executable examples, generated inputs, configuration and mixed code refuse individually | Policy reads actual diff payloads, not extension-only fixtures; new acceptance |
| V-6 comment eligibility | A; `AdvisoryChangePolicyTests.C1281_CommentEquivalence` | Proved ordinary comment admits; token delta, directive, annotation, suppression, generated input, behavior-bearing doc and unsupported language refuse | Language-aware proof through policy; unsupported/proof absent stays refused; new acceptance |
| V-7 historical attribution | A; `AdvisoryChangePolicyTests.C1281_HistoryPreserved` | Existing Found rows/costs unchanged; introduced/inherited/unresolved independent of new taxonomy; missing acceptance remains substantive | Fresh DB read of seeded historical rows after policy operation; new regression acceptance |
| V-8 timely request or durable reason | B; `PipelineHandoffPilotTests.C1281_DelayAccounting` | Eligible request at 4:59 and 5:00 passes; 5:00.001 is late; unexplained >15m is investigated. Holds: dependency, decision, capacity, overlap, priority, evidence, quota, placement; each has reason, observed time, next observation | Real caller operation/readback after service restart; separate settlement->notification->receipt->consumption->eligible->create->dispatch clocks; new acceptance, B-2 required; operating p95 measured separately |
| V-9 authority and refusal | B; `PipelineHandoffPilotTests.C1281_AdmissionHolds` | Unapproved stage, open blocker, overlap, quota, stage cap, host cap, placement failure, tracker not activated/spend not accepted: no child; durable reason; allowed control creates through existing API | One invalid dimension with all others eligible; current cap/quota refuses preserved, no reroute/override; new acceptance, B-2 required |
| V-10 receipt and recovery | B; `PipelineHandoffPilotTests.C1281_CompletionRecovery` | `[Arguments]` for the nine cuts in delivery inventory; each loops busy/eligible and raw/distilled-spilled. Also clip body, move destination/generation and reuse pre-floor prompt: never confirmed. Restored positive gets one complete UserPrompt and stable IDs | Real settlement/scanner/queue and actual submitted callback; new composed-boundary regression; 9 TUnit executions, not 36 |
| V-11 once per current handoff | B; `PipelineHandoffPilotTests.C1281_CurrentHandoffOnce` | Duplicate/restart/lost create response: one child for the same key; stale workflow revision creates none; missing/unmarked/unknown handoff returns to original owner; no timer kills a Working/Blocked seat | Bound caller operation and real task rows, not parser-only; new acceptance, B-2 required |
| V-12 readiness is metadata | C; `PublishedCardCompletionTests.C1281_ReadyForRelease` | Settled Code pre-publication remains Review; metadata says ready for release with source identity; board column count/IDs unchanged, no session kept just to wait | Actual card/run read after restart; new acceptance, B-3 required |
| V-13 close only shipped owner | C; `PublishedCardCompletionTests.C1281_PublicationGate` | Real confirmed ordinary implementation land + valid obligations closes once; successful Code, Plan-only land, unrelated same-card land, push-start/ack without confirmation and stale approval do not | Produce positive via private bare remote/landing protocol; vary each negative separately; new safety acceptance |
| V-14 explicit obligations | C; `PublishedCardCompletionTests.C1281_Obligations` | Pending acceptance or deployment blocks independently; wrong build/version deployment blocks; required pending companion must be recorded with correct owner/operation. Cleanup residue alone does not block | Persist/reload B-3 record; zero-obligation category must be explicitly recorded, never inferred from absence; new acceptance |
| V-15 current task/human state | C; `PublishedCardCompletionTests.C1281_ConcurrentClose` | Queued/Dispatched/Working/Blocked implementation task, human move/reopen/hold, archived/owned/decision/terminal card each blocks; barrier inserts human move after proposal before CardService reload | Two DB contexts and deterministic barrier; no lost human revision or canceled implementation; new safety acceptance |
| V-16 shadow then enable | C; `PublishedCardCompletionTests.C1281_ShadowGate` | Disabled/shadow: proposal only, zero Done writes; <30 accepted events or any false-close proposal forbids enable; qualified enabled control closes; false close suspends admission | Actual settings/qualification consumer, not test-count flag; new acceptance; pilot sample is Q-4 |
| V-17 recover close once | C; `PublishedCardCompletionTests.C1281_CloseRecovery` | Pre-write crash, post-commit lost acknowledgement, restart, duplicate projection: one same-operation close; changed obligations/card revision re-evaluated | DB failure/barrier hooks assert reached; new acceptance, B-3 required |
| V-18 publication survives cleanup debt | C; `PublishedCardCompletionTests.C1281_CleanupDebt` | Confirmed publication + completed obligations + retained worktree residue may close; unresolved cleanup stays recorded, no second push/deletion | Real operation and card reads; new acceptance |
| V-19 readiness remains fail-closed | D; `ReleaseGateActivationTests.C599_Admission`, `ReleaseGateActivationTests.C599_QueuedReadiness` | Valid fixture ready; missing/wrong qualification, recipient/outage/watchdog data, disabled, stale/future/unreadable monitor refuse | Real reader over temp files; existing guard qualification, **not live acceptance** |
| V-20 separate lane credit | D; `ReleaseGateRunTests.C599_CreditVerdicts` | RC never earns master credit; master never release credit; false/string booleans, missing identity and wrong candidate SHA earn none | Real script predicate via ScriptHarness; existing regression qualification, 1 TUnit execution / 24 harness assertions |
| V-21 Interim qualification semantics | D; `InterimVerificationPolicyTests.C544_BaselineCompletion`, `InterimVerificationLandGuardTests.C544_FinalFullRequired` | Completed Found Final/Full may supply baseline; noncompleted baseline refuses. Unknown/None legacy scope cannot land; valid Final/Full can | Real create/land admission boundaries, controlled readiness/Git; existing qualification. Actual pilot additionally Q-3 |

Live acceptance (ordinary guard green never substitutes):

| ID | Exact existing recipe / required recipient evidence | Status and estimate |
|---|---|---|
| Q-1 / D-5 0545/0487 | CARD-0545 plan S6/F-1..F-6; manual full unattended master green then real scheduled master green, all seven suites; independent watchdog failure/recovery recipient observations joined by nid/body hash and job/native identities. Commit qualification Markdown before receipt references its SHA. | Operator qualification; no live sends in this TestDesign. Estimated 300-600 active/run minutes plus two overnight boundaries, inherited from owner plan |
| Q-2 / D-5 0599 | CARD-0599 activation plan Q-1..Q-4: deployed entrypoint/registration readbacks; real queue/process rehearsal; full Windows automated RC including E2E; both schedule slots, at least one actual scheduled green; peeled tag + downloaded manifest/summary digests + board GET receipt. | **Upstream binding required**: activation plan itself says next=plan for missing settings/entrypoint; do not invent Hangfire commands or switch scheduler here. Estimate 20+20+313+318=671 minutes plus slot waits |
| Q-3 / D-5 0544 | After Q-1/Q-2: one real qualified Interim repair admission, refusal when readiness is lost, retained owner latch, restored Final/Full covering all deferrals at exact source, confirmed land; record selection, policy, owner/card revision, costs and rollback to FullOnly/disabled. Found baseline is coverage, never approval. | Operator pilot, binding required for live identities; estimate 90 minutes plus its approved ordinary-scope duration (budget separately once source/selection known) |
| Q-4 / D-4 shadow | At least 30 correlated publication/acceptance events independently adjudicated; zero false-close proposals before enable. Eligible acceptance/publication-to-close p95 <=5m; retain raw publication-to-Done latency. | Operating acceptance; estimate 30 minutes review plus collection latency, no invented elapsed-time guarantee |
| Q-5 / D-3 pilot | 50 comparable completions including held/unfinished cases; >=95% eligible handoffs requested <=5m; every >15m unexplained wait investigated. Retain four latency components and original denominators. | Operating acceptance; estimate 30 minutes audit plus cohort collection; B-2 must supply records |

### Guards the regression

| ID | Regression selection and decisive assertion |
|---|---|
| R-1 | A's V-1..V-7 plus unchanged `InstructionBundleTests`, `StageOutcomeSummaryTests`, `ReviewEvidenceParserTests`: composed read-only/PC/source contracts survive; historical Found/Clean counts and supersession remain; Full never implies source-clean. Separate helper SHA cannot borrow approval. |
| R-2 | B's V-8..V-11 plus unchanged `PipelineHandoffParseTests`, `ExpectationPipelineTests`, `CompletionNoteWorkQueueTests`: unmarked remains unmarked; unrelated scope cannot count as progress; due/coalescing behavior preserved. V-10 is the recipient witness; these Unit classes alone are not. |
| R-3 | C's V-12..V-18 plus unchanged `CardWorkTransitionServiceTests`: dispatch -> InProgress, last successful settle -> Review, human precedence, disabled sweep, unrelated/owned/archived cards and idempotence remain. New Done predicate must not reuse the old exclusion of Queued tasks. |
| R-4 | V-19..V-21 plus `NightlyWatchdogCoreTests.C545_IntentBeforeNetwork`, `C545_ReceiptIdempotent`, `C545_ReaderHeldWithoutSession`: persisted intent, receipt idempotence and missing-reader hold remain. Existing comprehensive 0487/0545/0544/0599 control inventories remain pending under their original IDs; this preflight does not qualify or replace those programs. |

### Guard inventory

Only cheap-slice boundary guards are newly commissioned below; unchanged transitive guards inside the reused transport/landing/qualification engines retain their original plans' controls. Each listed independently bypassable guard has its own PC, including presently unbound ones. No safety-critical exemption is claimed.

| Guard | Plan reference / invariant | Control |
|---|---|---|
| G-1 | D-2 advisory alone does not demand a rerun | PC-1 |
| G-2 | D-2 missing behavior remains substantive | PC-2 |
| G-3 | D-2 absent/failed required verification never earns Clean/Full | PC-3 |
| G-4 | D-2 corrected evidence matches subject | PC-4 |
| G-5 | D-2 corrected evidence matches SHA | PC-5 |
| G-6 | D-2 corrected evidence matches ref | PC-6 |
| G-7 | D-2 corrected evidence matches repository | PC-7 |
| G-8 | D-2 protected instructions/executable documentation never use narrative exemption | PC-8 |
| G-9 | D-2 comments require safe language-aware equivalence | PC-9 |
| G-10 | D-2 give new Docs SHA separate approval | PC-10 |
| G-11 | D-2 Interim latch cannot use Docs escape | PC-11 |
| G-12 | D-2 historical rows and costs are not rewritten | PC-12 |
| G-13 | D-3 approved stage authority required | PC-13 |
| G-14 | D-3 dependency holds respected | PC-14 |
| G-15 | D-3 source overlap respected | PC-15 |
| G-16 | D-3 quota refusal respected, no silent reroute | PC-16 |
| G-17 | D-3 stage cap respected | PC-17 |
| G-18 | D-3 host cap respected | PC-18 |
| G-19 | D-3 tracker YAML activation required | PC-19 |
| G-20 | D-3 scheduled spend acceptance required | PC-20 |
| G-21 | D-3 completion obligation commits with settlement before enqueue | PC-21 |
| G-22 | D-3 lost enqueue/wakeup recovers same queue identity | PC-22 |
| G-23 | D-3 rendering/spill survives restart as the complete frozen body | PC-23 |
| G-24 | D-3 complete prompt required before receipt | PC-24 |
| G-25 | D-3 receipt is above attempt sequence floor | PC-25 |
| G-26 | D-3 current handoff key creates at most one child across restart | PC-26 |
| G-27 | D-4 confirmed publication required, not push ack/status | PC-27 |
| G-28 | D-4 implementation owner and card mapping required | PC-28 |
| G-29 | D-4 Plan-only land excluded | PC-29 |
| G-30 | D-4 current approval required | PC-30 |
| G-31 | D-4 explicit acceptance must be satisfied | PC-31 |
| G-32 | D-4 recorded deployment must match required build/version | PC-32 |
| G-33 | D-4 required pending companion recorded and correlated | PC-33 |
| G-34 | D-4 any open implementation task, including Queued, blocks close | PC-34 |
| G-35 | D-4 newer human move/reopen/hold wins | PC-35 |
| G-36 | D-4 revalidate predicate atomically at card write | PC-36 |
| G-37 | D-4 archived-card exclusion retained | PC-37 |
| G-38 | D-4 readiness uses metadata, no new column or premature Done | PC-38 |
| G-39 | D-4 shadow cannot close | PC-39 |
| G-40 | D-4 replay produces one operation-linked close revision | PC-40 |
| G-41 | D-5 RC cannot grant master readiness | PC-41 |
| G-42 | D-5 master cannot grant release credit | PC-42 |
| G-43 | D-5 incomplete execution cannot become green | PC-43 |
| G-44 | D-5 missing recipient evidence cannot qualify readiness | PC-44 |
| G-45 | D-5 stale monitor cannot admit Interim | PC-45 |
| G-46 | D-5 incomplete baseline cannot admit; Found Full remains usable coverage | PC-46 |
| G-47 | D-5 latched land requires Final/Full | PC-47 |
| G-48 | D-3 placement refusal independently blocks create | PC-48 |
| G-49 | D-3 delay reason/next observation durable before held acknowledgement | PC-49 |
| G-50 | D-3 stale or unmarked handoff cannot select/create a child | PC-50 |
| G-51 | D-3 timing targets never kill/release Working or Blocked sessions | PC-51 |
| G-52 | D-2 reviewed source branch stays frozen until publication | PC-52 |
| G-53 | D-3 receipt belongs to destination session | PC-53 |
| G-54 | D-3 receipt belongs to current destination generation | PC-54 |
| G-55 | D-3 human-decision hold respected | PC-55 |
| G-56 | D-3 missing-evidence hold respected | PC-56 |
| G-57 | D-3 unmarked/unknown handoff returns to owner, no guessed stage | PC-57 |
| G-58 | D-4 owned-card exclusion retained | PC-58 |
| G-59 | D-4 NeedsDecision exclusion retained | PC-59 |
| G-60 | D-4 terminal-card exclusion retained | PC-60 |
| G-61 | D-4 disabled cannot close | PC-61 |
| G-62 | D-4 fewer than 30 accepted shadow events cannot enable | PC-62 |
| G-63 | D-4 false-close proposal prevents enablement | PC-63 |
| G-64 | D-4 required deployment cannot be absent/pending | PC-64 |

For composite fixture matrices, Code must split further if implementation introduces independent bypasses (for example multiple token-proof validators); add distinct controls and costs before handoff, never reuse one PC mapping.

### Positive controls

Mutation runs **break/red/restore/green after land**; Code runs V/R; Review judges this design before land. Every PC is required when its slice's Mutation is commissioned. Bind proposed payloads to the landed production line and assert the branch/cut was reached. Build/fixture failure, skip, zero executions or unrelated assertion failure is not red. Restore fixed bytes and rerun the same exact method green. Never mutate a live deployment. New policy mutations below mean compiling edits to the actual predicate or shipped bundle, not changes to expected results.

Detecting filter for every row is exactly `/*/*/<Class>/<Method>` using the single class-qualified method shown; no class-wide mutation run. Assertions quoted below are required labels for new cases; existing-method rows name their existing assertion text. B-1/B-2/B-3 rows remain binding required until their production seam exists, even after ordinary Code green.

| PC | Compiling defect in the corresponding G guard | Exact detecting method; expected red oracle |
|---|---|---|
| PC-1 | Route advisory-only disposition to Review rerun | `AdvisoryChangePolicyTests.C1281_AdvisoryDoesNotRerun`; `advisory-rerun-count == 0` |
| PC-2 | Classify missing acceptance behavior as advisory | `AdvisoryChangePolicyTests.C1281_IncompleteNeverClean`; `missing-behavior-substantive` |
| PC-3 | Grant Clean/Full for absent required execution | `AdvisoryChangePolicyTests.C1281_IncompleteNeverClean`; `missing-run-no-approval` |
| PC-4 | Omit subject equality | `AdvisoryChangePolicyTests.C1281_ReceiptCorrection`; `foreign-subject-refused` |
| PC-5 | Omit SHA equality | `AdvisoryChangePolicyTests.C1281_ReceiptCorrection`; `foreign-sha-refused` |
| PC-6 | Omit ref equality | `AdvisoryChangePolicyTests.C1281_ReceiptCorrection`; `foreign-ref-refused` |
| PC-7 | Omit repository equality | `AdvisoryChangePolicyTests.C1281_ReceiptCorrection`; `foreign-repository-refused` |
| PC-8 | Admit policy Markdown by extension | `AdvisoryChangePolicyTests.C1281_NarrativeBoundary`; `policy-doc-refused` |
| PC-9 | Accept comments with no equivalence proof | `AdvisoryChangePolicyTests.C1281_CommentEquivalence`; `directive-refused` |
| PC-10 | Copy old reviewedSourceClean/approval onto Docs SHA | `AdvisoryChangePolicyTests.C1281_SeparateDocsIdentity`; `new-sha-separate-approval` |
| PC-11 | Ignore owner's Final latch in Docs eligibility | `AdvisoryChangePolicyTests.C1281_SeparateDocsIdentity`; `latched-helper-refused` |
| PC-12 | Update historical Found row to Clean | `AdvisoryChangePolicyTests.C1281_HistoryPreserved`; `historical-row-unchanged` |
| PC-13 | Skip stage authorization | `PipelineHandoffPilotTests.C1281_AdmissionHolds`; `unauthorized-zero-child` |
| PC-14 | Ignore held-card eligibility result | `PipelineHandoffPilotTests.C1281_AdmissionHolds`; `open-blocker-zero-child` |
| PC-15 | Ignore overlap result | `PipelineHandoffPilotTests.C1281_AdmissionHolds`; `overlap-zero-child` |
| PC-16 | Retry quota refusal through another provider | `PipelineHandoffPilotTests.C1281_AdmissionHolds`; `quota-zero-child` |
| PC-17 | Ignore stage cap | `PipelineHandoffPilotTests.C1281_AdmissionHolds`; `stage-cap-zero-child` |
| PC-18 | Ignore host cap | `PipelineHandoffPilotTests.C1281_AdmissionHolds`; `host-cap-zero-child` |
| PC-19 | Treat absent tracker activation as enabled | `PipelineHandoffPilotTests.C1281_AdmissionHolds`; `yaml-disabled-zero-child` |
| PC-20 | Default spend acceptance to true | `PipelineHandoffPilotTests.C1281_AdmissionHolds`; `spend-unaccepted-zero-child` |
| PC-21 | Save settlement without its completion obligation | `PipelineHandoffPilotTests.C1281_CompletionRecovery`; `obligation-insert-settlement-rolled-back` |
| PC-22 | Skip pending notification recovery after lost enqueue | `PipelineHandoffPilotTests.C1281_CompletionRecovery`; `note-insert-complete-prompt-count == 1` |
| PC-23 | Discard frozen delivery before recovery render | `PipelineHandoffPilotTests.C1281_CompletionRecovery`; `render-committed-wire-digest-unchanged` |
| PC-24 | Confirm from Sent before complete transcript match | `PipelineHandoffPilotTests.C1281_CompletionRecovery`; `clipped-prompt-unconfirmed` |
| PC-25 | Accept matching text below attempt sequence floor | `PipelineHandoffPilotTests.C1281_CompletionRecovery`; `pre-floor-unconfirmed` |
| PC-26 | Create another child after lost create acknowledgement | `PipelineHandoffPilotTests.C1281_CurrentHandoffOnce`; `same-key-child-count == 1` |
| PC-27 | Replace HasPublication with push-start/status test | `PublishedCardCompletionTests.C1281_PublicationGate`; `unconfirmed-zero-done` |
| PC-28 | Accept unrelated same-card owner's publication | `PublishedCardCompletionTests.C1281_PublicationGate`; `foreign-owner-zero-done` |
| PC-29 | Accept Plan owner as implementation owner | `PublishedCardCompletionTests.C1281_PublicationGate`; `plan-only-zero-done` |
| PC-30 | Skip current approval validation | `PublishedCardCompletionTests.C1281_PublicationGate`; `superseded-approval-zero-done` |
| PC-31 | Treat missing acceptance record as satisfied | `PublishedCardCompletionTests.C1281_Obligations`; `acceptance-missing-zero-done` |
| PC-32 | Accept deployment of the wrong build | `PublishedCardCompletionTests.C1281_Obligations`; `deploy-sha-mismatch-zero-done` |
| PC-33 | Ignore required companion correlation | `PublishedCardCompletionTests.C1281_Obligations`; `companion-missing-zero-done` |
| PC-34 | Reuse old OpenStatuses excluding Queued | `PublishedCardCompletionTests.C1281_ConcurrentClose`; `queued-implementation-not-canceled` |
| PC-35 | Ignore newer human move at proposal | `PublishedCardCompletionTests.C1281_ConcurrentClose`; `newer-human-zero-done` |
| PC-36 | Apply stale close decision after CardService reload | `PublishedCardCompletionTests.C1281_ConcurrentClose`; `human-between-proposal-and-write-wins` |
| PC-37 | Remove archived-card exclusion from close entrypoint | `PublishedCardCompletionTests.C1281_ConcurrentClose`; `archived-zero-done` |
| PC-38 | Store readiness by moving to Done | `PublishedCardCompletionTests.C1281_ReadyForRelease`; `ready-status-review` |
| PC-39 | Allow shadow mode to write Done | `PublishedCardCompletionTests.C1281_ShadowGate`; `shadow-zero-done` |
| PC-40 | Append a second close revision on replay | `PublishedCardCompletionTests.C1281_CloseRecovery`; `same-operation-close-count == 1` |
| PC-41 | Allow rc-release to satisfy master predicate | `ReleaseGateRunTests.C599_CreditVerdicts`; `rc green never earns master credit` |
| PC-42 | Allow master-scheduled to satisfy release predicate | `ReleaseGateRunTests.C599_CreditVerdicts`; `master green never earns release credit` |
| PC-43 | Remove coverageComplete condition | `ReleaseGateRunTests.C599_CreditVerdicts`; `coverageComplete false earns nothing` |
| PC-44 | Ignore recipientEvidenceIds emptiness | `ReleaseGateActivationTests.C599_Admission`; `receipt-no-recipients must fail closed` |
| PC-45 | Accept monitor one tick beyond freshness boundary | `ReleaseGateActivationTests.C599_QueuedReadiness`; `one tick past the boundary is stale` |
| PC-46 | Ignore baseline task completion | `InterimVerificationPolicyTests.C544_BaselineCompletion`; `succeeded-incomplete: zero admitted tasks` |
| PC-47 | Allow Final Unknown/None for latched land | `InterimVerificationLandGuardTests.C544_FinalFullRequired`; `final-unknown` refusal / zero request |
| PC-48 | Ignore placement failure | `PipelineHandoffPilotTests.C1281_AdmissionHolds`; `placement-zero-child` |
| PC-49 | Acknowledge held handoff before saving its delay record | `PipelineHandoffPilotTests.C1281_DelayAccounting`; `restart-retains-reason-and-next-observation` |
| PC-50 | Dispatch from stale workflow revision | `PipelineHandoffPilotTests.C1281_CurrentHandoffOnce`; `stale-revision-zero-child` |
| PC-51 | Stop waiting/working caller at 5m deadline | `PipelineHandoffPilotTests.C1281_CurrentHandoffOnce`; `deadline-zero-stop-calls` |
| PC-52 | Apply the Docs correction to the reviewed branch before publication | `AdvisoryChangePolicyTests.C1281_SeparateDocsIdentity`; `reviewed-tip-unchanged` |
| PC-53 | Match complete prompt from a foreign session | `PipelineHandoffPilotTests.C1281_CompletionRecovery`; `foreign-session-unconfirmed` |
| PC-54 | Match complete prompt from an old generation | `PipelineHandoffPilotTests.C1281_CompletionRecovery`; `old-generation-unconfirmed` |
| PC-55 | Ignore human-decision hold | `PipelineHandoffPilotTests.C1281_AdmissionHolds`; `decision-zero-child` |
| PC-56 | Ignore missing-evidence hold | `PipelineHandoffPilotTests.C1281_AdmissionHolds`; `evidence-zero-child` |
| PC-57 | Default an unmarked handoff to Code | `PipelineHandoffPilotTests.C1281_CurrentHandoffOnce`; `unmarked-zero-child` |
| PC-58 | Remove owned-card exclusion | `PublishedCardCompletionTests.C1281_ConcurrentClose`; `owned-zero-done` |
| PC-59 | Remove NeedsDecision exclusion | `PublishedCardCompletionTests.C1281_ConcurrentClose`; `decision-zero-done` |
| PC-60 | Remove terminal-card exclusion | `PublishedCardCompletionTests.C1281_ConcurrentClose`; `terminal-zero-new-move` |
| PC-61 | Ignore auto-close disabled switch | `PublishedCardCompletionTests.C1281_ShadowGate`; `disabled-zero-done` |
| PC-62 | Permit enablement with 29 accepted shadow events | `PublishedCardCompletionTests.C1281_ShadowGate`; `under-30-refused` |
| PC-63 | Permit enablement after a false-close proposal | `PublishedCardCompletionTests.C1281_ShadowGate`; `false-proposal-refused` |
| PC-64 | Treat absent deployment receipt as satisfied | `PublishedCardCompletionTests.C1281_Obligations`; `deployment-pending-zero-done` |

### Out of scope

- D-6..D-10 / S6..S9 are later slices only: depend on A/B cleanup measurement, C close authorization, qualified D lanes, then versioned train admission -> immutable composition/aggregate approval -> exact publication/member projection -> operation-bound SourceLanding. No train code, aggregate tests, new scheduler, new column, or partial-train projection is commissioned here.
- D-11 / S10 depends on stable R1 and S1 census; separate non-train cohort, ordinary Review unchanged. No assignment/pipeline experiment here.
- S1 attribution implementation is separate. This appendix preserves D-1/D-12 denominators and stop gates but does not build the reporting tool.
- Live deploy, credential placement, recipient authorization and schedule activation remain the owners' operating steps. Approval of D-5 commissions qualification design; it does not make this delegate the operator or authorize external messages.
- Unchanged internal landing, transcript, readiness and release guards retain their comprehensive original PC inventories. If a cheap slice edits those engines, expand this scope before execution; the small D preflight is not a replacement full qualification roster.

### Checkpoints

The importer reads the first `### Checkpoints` heading, which above is the historical planning inventory. To preserve the original plan unchanged, use the identical table in the [cheap-slice checkpoint manifest](2026-10-15-card-1281-cheap-slices-checkpoints.md), which has one heading. Do not run this parent document as `--plan`. B-1..B-3 unresolved means their rows are designed but not commissionable. D rows are portable fixture preflight only; Q rows are separate operating gates.

All new classes have the method/execution census above: A=7, B=12 (3 ordinary methods + 9 recovery arguments), C=7. Existing class floors below are conservative lower bounds, not claims about current expanded totals; Require every listed method and all its parameter rows, zero failures/skips, fresh TRX. Unit is retained per owner default with nonzero floor 1; Code must record its discovered/executed roster at the committed SHA. Unit already covers Unit-class R inputs; the integration rows do not rerun those classes.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S2 | `tests/Antiphon.Tests -> bin-c1281-a/` | advisory-unit | `/*/*/*/*[Category=Unit]` | R-1 | >=1 executed; full Unit roster, 0 failed/skipped | 1 | 7 |
| CP-2 | S2 | CP-1 | advisory-acceptance | `/*/*/(AdvisoryChangePolicyTests*)\|(StageOutcomeSummaryTests*)/*` | V-1,V-2,V-3,V-4,V-5,V-6,V-7,R-1 | A 7 + summary 5 methods, all variants, 0 failed/skipped | 12 | 6 |
| CP-3 | S3 | `tests/Antiphon.Tests -> bin-c1281-b/` | handoff-unit | `/*/*/*/*[Category=Unit]` | R-2 | >=1 executed; full Unit roster, 0 failed/skipped | 1 | 7 |
| CP-4 | S3 | CP-3 | handoff-acceptance | `/*/*/PipelineHandoffPilotTests/*` | V-8,V-9,V-10,V-11,R-2 | 12 executions, every cut reached, 0 failed/skipped | 12 | 12 |
| CP-5 | S4 | `tests/Antiphon.Tests -> bin-c1281-c/` | close-unit | `/*/*/*/*[Category=Unit]` | R-3 | >=1 executed; full Unit roster, 0 failed/skipped | 1 | 7 |
| CP-6 | S4 | CP-5 | close-acceptance | `/*/*/(PublishedCardCompletionTests*)\|(CardWorkTransitionServiceTests*)/*` | V-12,V-13,V-14,V-15,V-16,V-17,V-18,R-3 | C 7 + transition 13 methods, all variants, 0 failed/skipped | 20 | 8 |
| CP-7 | S5 | `tests/Antiphon.Tests -> bin-c1281-d/` | qualification-admission | `/*/*/ReleaseGateActivationTests/(C599_Admission*)\|(C599_QueuedReadiness*)` | V-19,R-4 | 2 methods, every negative and valid control, 0 failed/skipped | 2 | 6 |
| CP-8 | S5 | CP-7 | qualification-credit | `/*/*/ReleaseGateRunTests/C599_CreditVerdicts` | V-20,R-4 | 1 execution; 24 named harness assertions, 0 failed/skipped | 1 | 2 |
| CP-9 | S5 | CP-7 | qualification-baseline | `/*/*/InterimVerificationPolicyTests/C544_BaselineCompletion` | V-21,R-4 | 1 execution, Found Full control admits, 0 failed/skipped | 1 | 3 |
| CP-10 | S5 | CP-7 | qualification-final | `/*/*/InterimVerificationLandGuardTests/C544_FinalFullRequired` | V-21,R-4 | 1 execution, all scope variants, 0 failed/skipped | 1 | 3 |
| CP-11 | S5 | CP-7 | qualification-watchdog | `/*/*/NightlyWatchdogCoreTests/(C545_IntentBeforeNetwork*)\|(C545_ReceiptIdempotent*)\|(C545_ReaderHeldWithoutSession*)` | R-4 | 3 executions, all assertion rows, 0 failed/skipped | 3 | 2 |

Code front door after binding: `pwsh -NoProfile -File scripts/build-slot.ps1 -Label c1281 -- dotnet run --project tools/Antiphon.Checkpoints -- run --plan docs/superpowers/plans/2026-10-15-card-1281-cheap-slices-checkpoints.md --after S2 --expected-source-sha <committed-source-sha> --serial`; select S3/S4/S5 separately. Await completion (`wait` if exit 75), retain unedited CP lines/provenance, run the base..HEAD evidence guard, clean only owned alternate outputs. Commit red tests before fixes; red-first executions use the same row/selection and are counted as reruns. No mutation run in Code. No whole assembly/native suite added by this appendix.

### Cost

All figures are **estimated**, not measured. Ordinary V/R floor (Code) = **63 minutes**: A `7+6=13`, B `7+12=19`, C `7+8=15`, D `6+2+3+3+2=16`; filters are exactly CP-1..CP-11. Of this, four isolated builds/setup total 16 minutes and V/R execution totals 47. Review budgets its own same ordinary selection; receipt repair never silently replaces required initial Review.

PC floor (Mutation): PC-1..12, PC-27..47, PC-52, PC-58..64 = 41 cycles x 6 minutes = 246; PC-13..26, PC-48..51, PC-53..57 = 23 cycles x 9 minutes = 207; **453 minutes total**. Each six-minute cycle budgets 2 build + 1 exact-method red + 2 restored build + 1 exact-method green; each nine-minute B cycle budgets 2+2.5+2+2.5. No shared-build saving assumed; mutation methods are the exact filters above. Ordinary plus all new PCs = **516 minutes** = 16 setup/build + 47 V/R + 453 PC. An initial red-first pass of all changed A/B/C CP rows adds up to 47 minutes, so reserve **563 minutes** through Code+Mutation, plus independent Review (63) = **626 minutes**. Slot waits, authoring, inherited-red diagnosis and binding work are additional and recorded, never zero-filled.

Live/operating floor is separate: Q-1 lower estimate 300 + Q-2 671 + Q-3 90 + Q-4 30 + Q-5 30 = **1,121 minutes**, plus Q-3's source-specific ordinary run, calendar windows/cohort collection and any upstream activation repair. No complete live total is claimable until those are bound. TestDesign ran 0 builds, 0 tests, 0 PCs and changed documentation only.

Savings claim: **0 measured minutes / $0** from this design. Four ordinary builds replace a per-row build plan's eleven (7 builds avoided; at 4 minutes each, **28 estimated minutes**); baseline per-row rebuilding is a counterfactual, not historical evidence. Unit is intentionally repeated for independently landable A/B/C; D is qualification-only and changes no code. Do not charge all 33.08 historical Review hours as avoidable, or claim closure latency as publication savings.

Audit at this handoff: **guards=64, mapped=64, missing=0, duplicate PC maps=0**. All 64 PCs are required future runs; **executed=0, pending=64**, not passed. B-1/B-2/B-3 and RC activation entrypoint remain explicit binding obligations. `next: plan` resolves those seams; `next: code` is valid only after the affected recipes, exact payloads, source scopes and checkpoint selection are bound. No further D-1..D-12 operator decision is requested.
