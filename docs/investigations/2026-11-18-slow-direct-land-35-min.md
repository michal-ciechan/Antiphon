# Slow direct land (~35 min) that ended verification_failed — investigation

Task 4ac60096 (Investigate, desktop, read-only). Date 2026-11-18.
Master read: `ae7bf7368231d819f555386350e1c30b185a589a` (origin/master at investigation time; also the land's target).
Subject: land request `3c4632e1-1995-4f2b-99f4-1786bf62dbac`, operation `98bf7f51-e342-43dc-a4cb-ee5595aca9f7`,
task `ae97c85e-4055-4384-bcbf-deb8b54ef60b`, candidate `18628faa77148d53ff6cd5ef7328d14d046f579d`.

## Verdict

**The land did not take 35 minutes. It took 7 min 45 s (464 s) and finished at 12:52:26Z.** About 436 s of that was a
server-side `dotnet build` of the whole solution. **That build failed because master `ae7bf7368` does not compile:**
`tests/Antiphon.Resilience.Tests` fails with CS0103 `TestDbFailureEvidence` (reproduced, see Q2). The task's own diff
has nothing to do with it. The remaining ~27 minutes were the gap between the outcome and the next status read
(13:19:23Z). In that time `noProgressSeconds` kept counting from the last *phase* transition (12:44:51Z) even though
the request was already `Completed`. That is why the status showed `Completed ... no progress for 2072s`.

**Consequence for the caller:** a Clean Review of `18628faa` (Review ff5ff6a5) will **not** make the next land of
ae97c85e pass. The land verifier builds every non-docs diff against master, and master's
`Antiphon.Resilience.Tests` does not compile. Until master is fixed, every code-touching `-Land` to master will be
refused in the same way after roughly 7 minutes.

## Q1 Timeline (desktop server log `C:\src\Antiphon\server\logs\antiphon-20261118.log`, UTC)

| Time (Z) | Log line | Event / phase |
|---|---|---|
| 12:43:31.8 | API `completedAt` | ae97c85e settled Succeeded (log 130427 at 12:43:53.9) |
| 12:44:41.593 | 130543 | `POST /api/agent-tasks/ae97c85e/land/v2` |
| 12:44:41.726 | API `landRequest.requestedAt` | request row written |
| 12:44:41.985 | API `landRequest.startedAt` | admitted at once. **No queue wait**: no other land ran 12:27:04 to 13:20 (`grep "Land git profile"`; the previous one, 27ed273a, ended at 12:27:04) |
| 12:44:41 to 12:44:51.66 | API `lastProgressAt` = 12:44:51.659 | admission, inspect, pin, land-worktree reset (2.26 s), rebase (0.5 s): git total 7.32 s (log 131753). **Last phase transition** (`Prepared`) |
| 12:44:53.674 | 130587 | `Landing verification decision ... reason diff-requires-build` |
| 12:44:54.635 | 130588 | verification window released R (L retained) |
| 12:44:58.713 | 130593 | `land_artifacts warm ...\land-verify\v1\dcaeb6b9...\426c6205...` |
| 12:44:58.764 | 130595 | `BUILD SLOT granted ... waited=0s maxcpucount=4`. **No slot wait** |
| 12:44:58 to 12:52:15 | 131706, 131708 | **child 79484 `dotnet build` ran 436 s** (`BUILD SLOT released ... held=436s`). Exactly one child joined, so no test child ran |
| 12:49:54.837 | API event `LandAged` | `Warning: Land Running; ... no progress since 12:44:51.659`: `LandWarningSeconds`=300 crossed **during the build** |
| 12:52:15.313 | 131709 | verification window reacquired |
| 12:52:19.706 | API events `Warning` + `LandRefused` | `land refused: verification_failed`; Outcome notification 66419afe created |
| 12:52:26.184 | 131753 | `Land git profile ... outcome=Complete/LandRefused wallSeconds=464.37 ... verify=- push=-` |
| 12:57:21.6 / 13:07:21.6 | API events `LandAged` | outcome receipt unconfirmed (Warning, then Error): notification 66419afe to orchestrator session b2f1a869 still `AwaitingReceipt` |
| 13:19:23.5 | 135891 | next `GET /api/agent-tasks/ae97c85e` (the orchestrator's poll). 13:19:23.5 − 12:44:51.66 = **2071.9 s**, which is the "no progress for 2072s" the caller saw |

The orchestrator's GETs at 12:49:57 and 12:50:56 (log 131352, 131507) fell inside the build. The slow phase is the
verification build. No queue wait, slot wait, git fetch or server2 round trip took measurable time.

## Q2 What `verification_failed` was

- Code: `AgentTaskLandingProtocol.cs:300-332` (ae7bf7368). In phase `Prepared`, `LandVerificationPolicy.DecideAsync`
  returns `diff-requires-build` for any master diff that is not documentation only (`LandVerificationPolicy.cs:24`).
  The verifier then runs `dotnet build --artifacts-path <warm gen> --configuration Debug -maxcpucount:<grant>`, plus
  `dotnet run --project tests/Antiphon.Tests ... --treenode-filter <filter>` only when a filter is set
  (`AgentTaskLandService.cs:1935-1967`). `Require(verification.Passed, "verification_failed")` (`:332`).
- It checks **no review evidence**: `landing.reviewEvidenceId = null` for this request, so this was not an
  evidence-backed admission, and `reviewedSourceClean` was never consulted. It also checks no checkpoint receipt and
  no land-graph audit. The refusal comes from the build alone.
- **Which step: the build.** Only one child joined (log 131706), so the test child never started.
  `AgentTaskLandService.cs:1944` returns `Failure("build", Tail(build))`. `LandingVerifier.cs:30` turns that into
  `"build failed"`. `Require` then keeps only the code `verification_failed`. The build tail **is not persisted
  anywhere** (not in the event, the landing row or the log). `AgentTaskLandService.cs:785` records a stage outcome
  saying "build failed or selected tests failed".
- **Which project:** the warm generation `...\426c6205b8e44b3c8bc397967a671e41` holds a fresh (≥12:44) output DLL for
  every project except `Antiphon.Resilience.Tests`. Its `obj\...\debug` stops at
  `CoreCompileInputs.cache` 12:48:04 and `FileListAbsolute.txt` 12:48:12, with no DLL. The generation `state` file is
  still `building`, so `Complete()` was never reached.
- **Reproduced:** `git archive 18628faa | tar -x` into the scratchpad, then
  `scripts/build-slot.ps1 -- dotnet build tests/Antiphon.Resilience.Tests/...csproj -c Debug` (lease af8a62aa,
  151 s): **2 errors**,
  `tests/Antiphon.Tests/TestHelpers/TestDbFixtureLifecycle.cs(800,30)` and `(812,30)`:
  `error CS0103: The name 'TestDbFailureEvidence' does not exist in the current context`
  `[...Antiphon.Resilience.Tests.csproj]`.
- **Mechanism:** commit `2c1b05349067731304b6a237e5f06511615675e5` (2026-11-18 09:54Z, "test(profile-413): write the
  inner exception chain of a transient test DB failure to the test output") added `TestDbFailureEvidence.In...` to
  `TestDbFixtureLifecycle.cs:800,812`. The type lives in a new file, `tests/Antiphon.Tests/TestHelpers/TestDbFailureEvidence.cs`.
  `Antiphon.Resilience.Tests.csproj` (CARD-1829 mirror) links `TestDbFixtureLifecycle.cs` file by file but does not
  link `TestDbFailureEvidence.cs`. `Antiphon.Tests` compiles; the Resilience mirror does not. This arrived on master
  with batch 270 (tip `ae7bf7368`, 11:57Z). The candidate's diff (`git diff ae7bf7368 18628faa`) touches none of
  `tests/Antiphon.Tests/TestHelpers`, `tests/Antiphon.Resilience.Tests` or `tests/Shared`.
- **Could it have been rejected up front?** Not by a review/evidence precondition, because none failed. A target-tree
  build health check (has this master SHA already failed the land build?) could have refused in seconds. No such
  cache exists: there were no other verifier builds 2026-11-15..18 (`grep "Landing verification decision"`), and
  the warm generation it reused was cut on 2026-10-26.
- Not investigated: why the batch 270 merge's final-tree compile did not catch this (memory rule: "one compile"; it
  may have compiled `Antiphon.Tests` only). UNOBSERVED.

## Q3 Why so slow / what "no progress" is

- No land queue: a single in-memory channel (CARD-0331), but empty. `startedAt − requestedAt` = 0.26 s.
- Verification itself: about 7 min of 7.7 min. This is a full-solution `dotnet build` at `-maxcpucount:4` into a warm
  artifacts generation that was 3 weeks stale (the last successful generation is from 2026-10-26), so close to a cold
  build of ~60 projects.
- `noProgressSeconds` = `now − LastProgressAt` (`LandRequestStatusDto.cs:50`). It is computed for every state,
  **including terminal `Completed`**. `LastProgressAt` moves only on a forward `LandPhase` transition
  (`AgentTaskLandingProtocol.cs:1098-1101`), at admission (`AgentTaskLandService.cs:645`) or on a race retry (`:860`).
  The verification build is one phase with no heartbeat, so any build longer than `Delegation:LandWarningSeconds`
  (300) raises a `LandAged` Warning, and one longer than `LandErrorSeconds` (900) raises an Error
  (`AgentTaskLandMonitorService.cs:34-48`). The only automatic action is the attention/notification row. Nothing is
  stopped or retried.
- `delegate.ps1:846` prints `Land: <state>; ... no progress for <n>s` and no phase. The refusal *outcome*
  notification (66419afe, Outcome) was created at 12:52:19.7, but it stayed `AwaitingReceipt` at the orchestrator
  session through 13:19. Why it was not consumed earlier (whether it was queued behind a busy turn) is UNOBSERVED.

## Q4 Recommendations (card texts, NOT filed)

**(0, urgent, a fix belongs to a separate card) Master does not compile `Antiphon.Resilience.Tests`.**
Title: `Master ae7bf7368: Antiphon.Resilience.Tests CS0103 TestDbFailureEvidence breaks every land build`
1. `TestDbFixtureLifecycle.cs:800,812` (from 2c1b05349) uses `TestDbFailureEvidence`.
2. `Antiphon.Resilience.Tests.csproj` links TestHelpers file by file and lacks `TestDbFailureEvidence.cs`.
3. Repro: `dotnet build tests/Antiphon.Resilience.Tests` at ae7bf7368 gives 2 × CS0103.
4. Effect: every non-docs `-Land` to master is refused `verification_failed` after ~7 min (ae97c85e, 2026-11-18).
5. Also check whether rc carries 2c1b05349, and why batch 270's final-tree compile missed the mirror project.

**(a) Persist the failing verifier step and tail; fail fast on a known-broken target.**
Title: `Land verifier: record build/test failure tail and refuse fast when the target tree is known not to build`
1. `LandingVerifier.cs:30` keeps only `"build failed"`, and `Require(..., "verification_failed")` drops even that.
2. Store `Step` + the last ~40 lines of `Tail(build)` on the landing row/event and print them in `-Status`.
3. Name the failing project(s) (first `error CSxxxx ... [*.csproj]` lines).
4. Cache a `(targetSha, graph fingerprint) → build failed in project X` verdict. A later land on the same target
   whose diff does not touch X refuses in seconds with `target_build_broken`.
5. Witness: a fake runner whose build fails gives a refusal detail that contains the error line.

**(b) Phase and heartbeat for a running land in `-Status`; stop counting after terminal.**
Title: `Land status: show current phase + phase start, heartbeat during verification, freeze noProgress at completion`
1. `LandRequestStatusDto.cs:50` counts `now − LastProgressAt` even when `State=Completed` (shown as 2072 s here).
2. Expose `phase`, `phaseStartedAt` and `verifierChildPid/step` (build|tests) on `landRequest`.
3. `delegate.ps1:846` prints `Land: Running; phase Prepared/verify-build since 12:44:58Z (held slot 312s)`.
4. Treat the verifier's child as live progress (heartbeat), so `LandAged` does not fire on a healthy 7-min build.
5. For a terminal request, print `completed at <t>; outcome <reason>` and no progress counter.

**(c) Document what a direct land needs; no review evidence is required for a plain `-Land`.**
Title: `Docs: a direct -Land is gated by the server build, not by review evidence; say so in the land section`
1. This request carried no `reviewEvidenceId`, so `reviewedSourceClean` was never checked.
   `docs/orchestration-loop.md:3049-3054`: "Evidence-backed land admission and every unpublished resume require true;
   missing or false returns `review_evidence_source_not_clean`". That applies only when evidence is supplied.
2. Without evidence, the gate is `LandVerificationPolicy`: a docs-only diff skips; anything else runs a full build, plus
   tests only with `-Verify <filter>` (`docs/orchestration-loop.md` ~2400-2422; `docs/testing-and-build.md:1412`).
3. So `-Land` of a plain-commit, single-subject Code branch *is* meant to work. It would have succeeded here if
   master compiled. A Clean Review does not bypass the build.
4. Evidence an author can carry to use the evidence path: a Review outcome with `reviewedSourceClean: true` (its
   required checkpoint selection validated by `scripts/validate-checkpoint-receipt.ps1` for `reviewedSourceSha`,
   `dirty=0`, verified build binding), passed as `-ReviewEvidenceId`. The verifier build still runs.
5. Add an orchestrator line: on `verification_failed`, read the refusal detail (after (a)) before commissioning a Review.

## Not done, noted

- No fix, no land, no setting, no restart. One read-only reproduction build ran in the scratchpad under a build slot
  (lease af8a62aa, 151 s).
- Fix idea (one line): link `TestDbFailureEvidence.cs` into `Antiphon.Resilience.Tests.csproj`, then rerun a desktop
  full-solution build.
- Remaining uncertainties: why the Outcome notification was not delivered or consumed before 13:19 (UNOBSERVED); why
  the batch 270 final-tree check missed the Resilience mirror (UNOBSERVED); rc exposure to 2c1b05349 not checked.
