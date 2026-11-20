# Review r1: gpt-6.2-astra selectable on demand (Code task 2c673f12)

Reviewer task `c9ba73a2` (Claude Opus; author Grok). Ordinary round 1, Final profile.
Subject `origin/feat/card-task-2c673f12` tip `3bc8ac66fb1aba3f27fa01afbb4d8378ac3b962f`,
base `8a89aa0e5715f86062bbcc7ee778810c2cf4cb3e`, master `a79ce4086ebbafd2d00d89b4107e07b9f5992463`.
Linux (server2) only; Windows unobserved. Production Postgres was not touched.

**Verdict: BLOCKERS (2).** The ladder, migration, validation and script parts are sound. The alias
is stored on the task only. Two dispatcher paths ignore it: warm-pool reuse and reroute.

## Blockers

### D-1 Warm-pool reuse ignores `RequestedModelAlias`; both directions run the wrong model

- **Where:** `server/Application/Services/AgentTaskDispatcher.cs` warm-pool predicate (~8927-8955:
  `a.ModelLevel == claimed.ModelLevel && a.Kind == claimed.AgentKind && ...`). The pool
  delegate row (~7843 `new Agent { ModelLevel = task.ModelLevel, Kind = task.AgentKind, ... }`)
  records no model alias. The same predicate is in `AgentTaskDispatcher.DesktopMemory.cs:59-66`.
  Shared tasks pool warm at settlement (`AgentTaskReplyService.cs:2893`, sweep `AgentTaskDispatcher.cs:9601`).
- **Failure:** (a) A Shared Codex/Frontier task created with `model: gpt-6.2-astra` launches
  `--model gpt-6.2-astra`. It settles and pools warm as Codex/Frontier. After the reservation
  lapses (`PoolReservedForCallerMinutes`), or at once for the same root, an ordinary Codex/Frontier
  Shared or ReadOnly task in that checkout reuses it. That Review runs on gpt-6.2-astra, and the
  operator rule says Frontier and Review must stay gpt-6-astra. (b) The reverse case: an Astra 6.2
  audit at High reuses a warm `gpt-6.2-sol` delegate. The hold check and the brief name
  gpt-6.2-astra, but the work runs on Sol. `--model` is a launch argument (comment ~7837), so
  reuse never re-selects the model.
- **Why:** The task model is no longer a function of (Kind, ModelLevel). Every pool match key
  still assumes it is.
- **Fix:** Use one of two options. (1) Record the launched alias on the pool row, for example a
  nullable `Agent.PoolModelAlias` stamped at launch. Match it Ordinal against
  `LaunchTierAlias(task, kind)`, with null equal only to null. (2) Make a task with a non-null
  `RequestedModelAlias` never reuse a warm delegate and never pool warm (kill on settle). Add a
  producer-to-recipient witness: an alias task settles warm, then a plain same-level Shared task
  is claimed and must spawn fresh. Add the reverse witness too. (2) is the smaller change.

### D-2 Reroute and kind/level re-walks keep the alias; a cross-kind move launches a Codex slug

- **Where:** `AgentTaskService.RerouteAsync` (~3330-3340) clears `Complexity`, `RoutingPinId`,
  `ExplicitAgentKind` and `ExplicitModelLevel`, but not `RequestedModelAlias`. The dispatch-time
  re-walk (`AgentTaskDispatcher.cs` ~1860 and ~2040) and the wall requeue (`AgentTaskService.cs`
  ~3631) set `AgentKind`/`ModelLevel` and leave the alias in place. `LaunchTierAlias` then returns
  the stored slug for ANY kind.
- **Failure:** An operator reroutes a held gpt-6.2-astra task to ClaudeCode/High (the documented
  way out of a hold). The tick checks `RequireAsync(ClaudeCode, "gpt-6.2-astra")`, finds no hold
  and launches `claude --model gpt-6.2-astra`, which fails at launch. A reroute Codex/High to
  Codex/Medium reports "rerouted to gpt-6.2-sol" (hold-checked on Sol), but the process still runs
  gpt-6.2-astra. A chain-governed alias task can also exist. `-Model` with no `-Kind`/`-Level`
  passes `MatrixDefaultApplies`, the server stamps `Complexity = DefaultComplexity`, and the
  model-plus-complexity 422 (~844) only sees the caller's own `Complexity`. If the walk picks
  Codex, the task carries the alias, and a wall cascade can move it to Grok or ClaudeCode.
- **Why:** The brief asks whether the alias is kept or deliberately dropped. Escalate and retry
  keep it consistently: `AfterRequeueAsync` hold-checks the alias that launches. Reroute and
  re-walk neither keep it coherently nor drop it.
- **Fix:** Clear `RequestedModelAlias` in `RerouteAsync`, in the dispatch re-walk and in the wall
  requeue, or refuse those moves when the alias is set. Either way the hold check and the event
  text must name the alias that will launch. Also make `-Model` without kind/level skip the
  matrix default, or refuse it, so an explicit alias is never chain-governed. Add witnesses:
  reroute of an alias task to ClaudeCode launches without `--model gpt-6.2-astra`, and a
  `-Model`-only create is not list-governed.

## Answers to the brief's questions

1. **Migration: safe.** `20261120091515_AddRequestedModelAlias` sorts directly after
   `20261119045705_AddLandVerificationFailure`. Master `a79ce4086` adds no migration since the base.
   The snapshot and Designer agree, and `HasPendingModelChanges` is false (CP-4). A nullable add
   with no default is catalog-only in Postgres. `SET LOCAL lock_timeout = '5s'` and the throwing
   `Down` match `AddLandVerificationFailure` and `LandVerificationFailureMigrationTests`. The
   downgrade selector (CP-4: `RequestedModelAliasMigrationTests` + `AppDbContextModelTests`)
   ran 4/4. The migration is not idempotent SQL; it is guarded only by EF's history table, which
   is the repo norm. **Post-land activation:** the column exists only after an AppHost/server
   restart on a build containing it. Until then a create with `model` fails with an EF
   unknown-column error on the server.
2. **Default ladder: unchanged.** With the alias null, every path is unchanged:
   `LaunchTierAlias` falls through to `TierAliasFor`, `DispatchModelAlias.ForTaskAsync` falls
   through, and both create hold checks fall back to `ModelLevelAliases.For`.
   `ForCodex(Frontier)=gpt-6-astra` and `High=gpt-6.2-sol` are asserted in CP-2, and
   `RolePolicyReadServiceTests`/`ModelLevelAliasDisplayTests` stay green (CP-3). Escalate, retry
   and Reply continuation keep the alias. Children and Reviews do not inherit it: only create
   from `request.Model` writes it. Reroute and re-walk are D-2, and the pool is D-1.
3. **422/409: correct.** Validation throws before `SaveChanges`. The error echoes only the
   caller's trimmed text and the known list, with no secret. The stored and launched value is
   the canonical `ModelAlias.Normalize` result checked against `DelegatableAliases`, never the
   raw string. Injection: clean.
4. **Floor:** a hold on gpt-6.2-astra refuses create, retry and escalate. The 0.169.1 floor is
   data only, so a 0.160.0 runner gets a launch-time CLI/backend model error and no
   pre-dispatch refusal (known AC-1: the server2 image must reach 0.170.0 first).
5. **delegate.ps1:** `-Model` is in the Create set and is sent only when set, with a
   `-Complexity` + `-Model` refusal. The parameter is commented in the param block. The
   change is ASCII and works on PS 5.1. Covered by `Model_is_posted_only_when_set`.
6. **Witnesses and pins:** red-at-`e7520716` is recorded in the plan; I did not re-run red.
   Land-graph CP-7: inherited manifest mismatch, warn only; the branch changes no definition
   file. `PinnedInputDigest` at the branch is `c0d2a723...` (CP-9). In the merged tree
   (`git merge-tree --write-tree origin/master HEAD` = `42fe8372`, clean),
   `-PrintInputDigest` computes `99297dde12befbe23fd15dff6f60b3c99c0df82bca5a2817f2c6bef5e4fef6df`,
   which is master's stated pin.
7. **Client hold form:** one Codex option added and the Select widened to 180px. Correct; not
   browser-tested.

## Advisories (not land-blocking)

- A-1: Summary/chip DTOs do not expose `RequestedModelAlias`. Retry/escalate event text names
  the ladder alias (`Retried at gpt-6.2-sol`) while the task launches gpt-6.2-astra.
- A-2: No witness covers `model_alias_conflict` (standing agent) or the explicit
  `complexity`+`model` 422.
- A-3: The brief names CARD-1514 (slow test classes); this work is unrelated to that card.
- A-4: Both sides edit `tests/derived-impact-index.json`. The merge-tree is clean, but the
  merged index was not regenerated here. The land's rebase verification owns that check.

## Evidence

Checkpoint run `20261120-105458-2257` at `3bc8ac66f`, one build `bin-astra62/` (357 s),
`slot=granted`, `buildSource=verified`, `unlisted: none`:
CP-1 86/86, CP-2 26/26, CP-3 94/94, CP-4 4/4, CP-5 81/81, CP-7 exit 0 (inherited warn),
CP-8 exit 0, CP-9 exit 0. CP-6 26/27: `regenerate_script_reports_changed_then_unchanged` needs
the `Antiphon.DerivedImpactIndex` driver built first (plan R-3 prerequisite, not built in
that run). See the CP-6 re-run below.

CP-6 re-run (unlisted, reason: plan R-3 prerequisite). First an unlisted build of
`scripts/regenerate-derived-impact-index/Antiphon.DerivedImpactIndex.csproj`
(build-slot lease `7fc48faf-7b49-43f0-8dce-f2662da54738`, waited 0 s, held 10 s, 0 warnings).
Then `DerivedImpactIndexTests` from the same verified `bin-astra62/` output at `3bc8ac66f`
(lease `4b67b7e3-d6bf-4f07-b323-87dfec100018`, held 234 s): **27/27 passed**, 0 skipped. The
1-minute load was 17.9 when that row started, above the brief's 16 threshold; it was 11.7 one
poll earlier. Every ordinary row is now green at the reviewed SHA. `check-evidence-diff` is
CP-8, exit 0, and merge-tree against master is clean (`42fe8372`).
PCs: none in the plan. Mutation is waived (operator 2026-11-12).

--- next stage ---
next: code
handoff: Fix D-1 (warm-pool reuse matches only Kind+ModelLevel, so an alias task and a ladder task share warm Codex processes; stamp the launched alias on the pool row or never pool/reuse alias tasks) and D-2 (reroute, re-walk and wall requeue keep RequestedModelAlias across kind/level moves; -Model-only creates become chain-governed); add producer-to-recipient witnesses; branch feat/card-task-2c673f12 at 3bc8ac66f.
artifact: docs/superpowers/reviews/2026-11-20-astra62-selectable-review-r1.md
