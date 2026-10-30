# CARD-1615 plan re-review round 2 (task 122f1efb)

Subject: `docs/superpowers/plans/2026-10-30-card-1615-deploy-land-deadlock-plan.md` at
`ddd9946b3b6c69d052e18a590f44a8059b936437` (`feat/card-task-28be9547`; the remote tip matches).
This was a read-only plan review on server2/Linux. Round 2 changes only the plan file
(`f9bf72bd5..ddd9946b3`, 1 file, +283/-54).

## Verdict

Not ready for Code. Round 1's D-1, D-2 and D-3 are closed in their main points:
- The plan now has three caller classes. The standing orchestrator is admitted, and token-only card sessions are refused with a stated migration.
- Rollout SHA X is named, and each phase checks the server version before it starts.
- The five passages it names are the right lines.

Five new defects remain. Two concern the brief's specific questions: caller identity can be forged, and the ban on restarts during a rollout exists only in documentation while other standing text still tells orchestrators to restart. The other three: the task controller cannot outlive its own ceiling, a standing-agent checkout can block rc lands, and the replacement text breaks existing pinned doc tests that are not in the checkpoint list. Option (d) itself still holds.

## Ground truth verified

- `AgentTaskLandService.cs:1015-1045` `FindWriterAsync` queries only `AgentTasks`. A standing or card session is not an AgentTask, so it can never be a source writer or hold a land. Confirmed.
- Environment shapes:
  - Task: `AgentTaskDispatcher.cs:7271-7288` `BuildEnv` sets `ANTIPHON_TASK_ID`, `ANTIPHON_AGENT_ID` and `ANTIPHON_TASK_TOKEN`.
  - Standing agent: `AgentSessionLaunchComposer.cs:43-50` sets the token and agent ID only.
  - Unassigned card: `CardService.cs:1791-1803` and `OrchestratorService.cs:976-987` set the token only.
- The server resolves a token task-first (`AgentTaskService.cs:186-209`). A task token always maps to its task, whatever the environment says.
- `GET /api/agent-tasks/{id}` (`AgentTaskEndpoints.cs:184-193`) is a **public** status read. It uses `ResolvePollingCallerAsync` (`:537-548`), which ignores a missing or stale token, and it never binds the token to `{id}`.
- `restart-apphost.ps1` checks two locks (`:102-103`, `:151-165`), `apphost.launch.lock` and `apphost.restart.lock`. Both last one restart and expire after 15 minutes. The remote `rollout.lock` is held only around a POST (`deploy-server2.ps1:234-275`). Nothing machine-checked lasts for a whole rollout.
- Deploy ceiling: 240 minutes of wall clock from `DispatchedAt` (`AgentTaskDispatcher.cs:3746-3765`), plus a 90-minute mid-turn limit for a running local tool (`TaskDeadlinePolicy.cs:26-31`). `deploy-server2.ps1:12` sets `-WaitIdleMinutes` to 480 by default (used at `:1246` and `:1295`); the docs use 240 for drain-temp.
- `AgentTaskLandingProtocol.cs:557-563` `ScanTarget` refuses `target_checked_out_elsewhere` when any non-main worktree has the target ref checked out.

## Defects

**D-1: the caller class and the TASK proof depend on environment variables a task can drop or change.**
- *Where:* plan D-7 (`:67`, "not a sandbox against deliberate environment tampering"); Caller admission table (`:80-82`), TASK row "authenticated own-task detail"; S2 (`:153-155`).
- *Failure:* a Shared Deploy task (which has task ID, agent ID and token) runs `deploy-server2.ps1` with `ANTIPHON_TASK_ID` removed from the child environment, for example via `$env:ANTIPHON_TASK_ID=$null` or `env -u`. Its remaining environment matches the STANDING AGENT shape (token plus a valid agent ID), so it is admitted with **no** task read. The task stays Shared, keeps holding lands, and the CARD-1615 cycle comes back.
  - The non-main-checkout rule means the script must also come from another linked tree, but that is the only remaining barrier.
  - The TASK path is not authenticated either. The GET is public, so `summary.id` matching is an echo of the request, and nothing binds the token to the ID.
- *Why:* the brief requires the refusal to depend on something a task cannot forge. Only the token qualifies: `AuthenticateAsync` resolves a task token to its task before any session lookup. The plan's word "authenticated" is factually wrong for this route.
- *Fix (or caller decision):*
  - Classify from the server's resolution of the token. Add a small read-only caller-identity read that returns `AuthenticateAsync`'s Caller: task ID, workspace, status and worktree path, or the standing session ID.
  - Treat the environment variables only as cross-checks. Any token that resolves to a task is TASK-class.
  - Add a V-3 vector that uses the `BuildEnv` shape with `ANTIPHON_TASK_ID` removed and a Shared owner, and expect `RolloutControllerWorkspaceRequired`.
  - Correct the "authenticated" wording.
  - Alternatively, the caller explicitly accepts admission keyed on the environment and records it. The plan's "No new API" (`:183`) makes this a choice between the two.

**D-2: the restart ban during a rollout is documentation only, and standing text that orders restarts is not replaced.**
- *Where:* plan Activation and phase SHA contract, step 2 (`:104-111`, "No new global lock/service... operating contract"); S3 replacement table (`:204-210`).
- *Failure:* D-8 keeps rc lands publishing during the rollout, so the rc tip moves from X to Y. The following unreplaced text then tells the orchestrator to activate Y:
  - `docs/orchestration-loop.md:372-390`, the post-land activation check: "restart onto that tip with it as `-ExpectedServerSha`". It is required "on every terminal card move or close".
  - `docs/orchestration-loop.md:622`, standing rule 7: "restart only when the rc tip changed".
  - `.claude/skills/antiphon-orchestrator/SKILL.md:174-179`, step 6.
  - `docs/apphost-runbook.md:196-220`.
  - The autonomy grant in `AGENTS.md:53` and `docs/orchestration-loop.md:15-21`, and the "track the current rc tip" sentence in `AGENTS.md:56`.

  `restart-apphost.ps1 -ExpectedServerSha Y` with HEAD at Y passes, and no lock or marker refuses it. A drain-old phase already running (up to 480 minutes) then reads census and API data from Y. The next phase refuses `RolloutControllerServerShaMismatch`. The rollout stops until a recovery is commissioned, with old drained.

  The plan only links the ban from the apphost-runbook "normal restart examples" (`:216-218`). A rollout is the orchestrator's normal work, and every rc land during it triggers the conflicting rule.
- *Answer to brief Q2:*
  - On a version mismatch, a phase refuses `RolloutControllerServerShaMismatch` before reading credentials or doing anything. That part is sound (`:112-118`).
  - Nothing machine-checked stops a canonical restart in the middle of a rollout. It is a convention, and it is not enough while that conflicting text stands.
- *Fix:*
  - Name and qualify each passage above: post-land activation and rule 7 are deferred while a rollout is unfinished.
  - Check whether the generated orchestrator-instructions file (`ANTIPHON_ORCHESTRATOR_INSTRUCTIONS`, which outranks AGENTS.md) carries restart text, and name it if so.
  - Preferably add a cheap mechanical guard. Preparation writes an active-rollout marker (X plus owner) in the canonical `logs/`. `restart-apphost.ps1` refuses with a named reason unless `-ExpectedServerSha == X` or an explicit release is given. The controller removes the marker on joined completion. Add a witness for it.
  - Otherwise, record that the caller accepts a documentation-only ban.

**D-3: one TASK controller cannot last a full rollout, and the successor path is not specified.**
- *Where:* D-1 (`:61`, "retain the same owner across explicit phases"); S3 recipe (`:189`); D-9 (`:69`); `:231-236`.
- *Failure:* the Deploy task's 240-minute ceiling fails it during a normal rollout. drain-old's default wait alone is 480 minutes, and drain-temp's is 240. The 90-minute limit on a running tool can fail it sooner. D-9 then refuses the next phase for the Failed owner. That is correct, but it is the routine outcome (the ceiling failure that just happened), not an exception. A successor needs "a proven quiescent state" and "CARD-1610's supported ownership/recovery". CARD-1610 has not landed (task f5c62876 is in review). The plan does not say:
  - who proves that the Failed-but-live predecessor's command has exited, and how;
  - what the handoff record contains: X, next phase, `-ResumeRecycle` context, evidence paths;
  - whether a fresh Worktree successor is admitted while the predecessor still exists. No V-3 vector covers this.
- *Fix:* choose one of these and witness it (successor admitted; predecessor's receipt-recorded outcome required):
  - Controllers scoped to one phase each, with every wait bounded below the ceiling. For example, pass an explicit `-WaitIdleMinutes` below 240, or have the standing class run the long drain phases.
  - Order CARD-1610 before CARD-1615 is activated.

**D-4: a standing or interactive controller checkout on the rc branch blocks every rc land.**
- *Where:* Caller admission STANDING row (`:81`), "clean pinned script source in a non-main linked controller checkout"; INTERACTIVE row (`:82`).
- *Failure:* the plan never requires a detached HEAD. A natural "pinned rc" checkout, `git worktree add <ctl> rc/2026-10-29`, makes `ScanTarget` (`AgentTaskLandingProtocol.cs:560-561`) refuse every rc land with `target_checked_out_elsewhere` for the whole rollout. This is a new rollout-time land block of the kind this card removes. Task controllers are safe because they sit on their own feat branch.
- *Fix:* for standing and interactive controllers, require a detached HEAD at X, or a private branch that no land targets. Refuse `RolloutControllerSourceMismatch` when the controller's symbolic ref is a land-target ref. Add a vector.

**D-5: the replacement text breaks existing pinned doc tests, and the closed checkpoint list does not run them.**
- *Where:* S3 replacement for `orchestration-loop.md:29-37` (`:206`); `### Checkpoints` (`:338-342`); "do not add tests that merely grep prose" (`:219-220`).
- *Failure:* existing tests pin phrases that the replacement removes or rewords:
  - `tests/Antiphon.Tests/Application/StandingPipelinePolicyDocumentationTests.cs:155-157` (`operational_autonomy_owner_pins_authority_checks_and_human_only_steps`) pins "queued lands have finished" and "never pull while a land runs" in the autonomy section.
  - `tests/Antiphon.Tests/Infrastructure/DockerStackDocumentationTests.cs:46-47` (`Lands_proceed_during_rollout_is_documented`) pins "need no land freeze" and "never pull while a land runs" (case-sensitive).
  - The new text says "Never pull during a Running land" and drops "queued lands have finished". Both classes go red. The D-2 fix (qualifying "MAY restart or upgrade the AppHost", `policySentence`) also changes text this class pins.

  About 35 test files read these docs; the plan lists none. Code must either land red or run unlisted rows, which Review then flags.
- *Fix:*
  - Add a doc-contract CP row with an exact filter for `StandingPipelinePolicyDocumentationTests` and `DockerStackDocumentationTests`, plus any class that pins the delegate `SKILL.md` or AGENTS text. `InstructionBundleTests` and `RunnerBranchContractDocumentationTests` read the skill, and `AgentContextContractTests` holds AGENTS.md to 24,576 bytes; it is 17,803 now.
  - Specify whether each pinned phrase is kept or each pin is updated, with counts.

## Advisories

- **A-1.** No PC covers the version recheck on each later `all` subphase or the precedence of TASK over agent ID. Both are optional, because `all` is forbidden by the operating recipe.
- **A-2.** No build-definition files are touched (no `*proj`/`.props`/`.targets`/`global.json`), so land-graph approval is not needed. The AGENTS.md and `.claude/skills` edits make the land a build land, not a docs-only skip. That is expected.
- **A-3.** CARD-1609 first (D-11) is right, and `scripts/deploy-server2.ps1` is in the declared scope. Before Code dispatch, confirm CARD-1609 has actually landed on rc and on master, and record its SHA. The rc/master split is correctly left to the caller (`:481-487`), and the inherited master merge-tree conflicts are disclosed.
- **A-4.** The delegate `SKILL.md` replacement (`:210`) is correct against `AgentTaskService.cs:2513-2517`. The project `DefaultWorkerWorkspace` is not read by `EffectiveRequestedWorkspace`.

## Brief checks

1. **Round-1 defects.**
   - D-1 is closed: there are three classes, the standing agent is admitted, and token-only sessions are refused with a migration. Non-task sessions never hold a land (confirmed). A task **can** pass as a standing agent (new D-1).
   - D-2 is closed for phase admission. Restarts are blocked by convention only (new D-2).
   - D-3 is closed for the five named passages; the lines and texts are verified. The pins break (D-5), and the conflicting restart passages are missing (D-2).
2. **SHA binding.** A mismatch is refused before credentials. A restart in the middle of a rollout is stopped only by documentation (D-2).
3. **Controller failure in the middle of a rollout.** The resume path depends on CARD-1610 and has no specified successor handoff (D-3).
4. **Counts.**
   - 9+5+6 = 20 Linux results and 9 Windows results; the method rosters list exactly 9 and 5.
   - `C1364BoundLandWaitProofTests` has 6 `[Test]` methods and no `[Arguments]`.
   - The seven red-first methods are all in the CP-1 roster.
   - Estimates are whole minutes: 6/4/2 on Linux and 9/6/3 on Windows. Witnesses use the real `RunAsync` and the extracted production census functions.
5. **Docs.** Covered by D-2 and D-5 above.
6. **Ordering and definition files.** Covered by A-2 and A-3 above.

## Checks re-run

- Tool build through the gate: lease `11f41ce2-822e-464a-8867-c02ee3f2d1e7`, waited 0 s, held 7 s, 5 warnings, 0 errors, output `bin-r122f/`.
- Import (`build-slot`, lease `55138500-dd0d-49f6-8178-505e18a7a05f`, held 2 s) printed `imported 3 rows`, exit 0. Floors are 9/5/6, `serial: true` on all rows, importer default timeouts 18/15/15. The output stays ignored at `.antiphon/r122f-scratch/import.yaml`.
- Both `bin-r122f` directories were deleted: `src/Antiphon.HostCleanup` and `tools/Antiphon.Checkpoints`.
- `check-evidence-diff -BaseRef f9bf72bd5 -HeadRef ddd9946b3`: 6 commits, 0 entries, 0 violations.
- No product build or test ran. The new classes do not exist yet (Plan stage). PCs: three are defined and all stay pending.
