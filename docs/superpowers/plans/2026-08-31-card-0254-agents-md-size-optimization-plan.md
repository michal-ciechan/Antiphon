# CARD-0254 — Reduce `AGENTS.md` to a mechanically guarded 24 KiB core

**Date:** 2026-08-31  
**Status:** Plan only. No `AGENTS.md` rewrite or mechanism work is included in this pass.

## Decision

Use a **layered instruction set**, not one shorter all-purpose file and not a separate monolithic
"full AGENTS" copy.

`AGENTS.md` is loaded by every project-facing agent and is the file Codex truncates at its 32 KiB
project-document budget. It must therefore be a small universal entry point: non-negotiable rules,
normal front doors, and a precise routing index. Role-specific orchestration rules stay in the
conditional `server/Bundles/orchestrator.md` and `docs/orchestration-loop.md`; they must not be
duplicated into a universal file. Detailed operational rationale belongs in the living document
that owns the behaviour, not in a second catch-all reference file and never in a dated plan as its
only home.

This keeps the reason a rule exists available before a relevant change, without making all agents
pay to load every past incident. It also preserves CARD-0247's context discipline: an orchestrator
receives its conditional bundle, while a delegate is routed to only the owner documents its brief
actually touches.

## Measured baseline

Measurement was taken from the current checked-out `AGENTS.md` on 2026-08-31 using raw UTF-8 byte
count, not character count. The file is **111,395 bytes**, 110,832 characters, 14,948 words, and
432 lines. It must lose **at least 80,675 bytes (72.4%)** merely to meet the user-set 30,720-byte
ceiling.

| Current section | Lines | UTF-8 bytes | Share |
|---|---:|---:|---:|
| Header and required reading | 18 | 1,105 | 1.0% |
| Living reference docs | 42 | 2,463 | 2.2% |
| Working cards from a shell | 33 | 2,581 | 2.3% |
| Running locally | 125 | 6,230 | 5.6% |
| Dev port map | 17 | 931 | 0.8% |
| Browser automation / ClaudeBot | 37 | 3,287 | 3.0% |
| Always-on backend | 19 | 5,531 | 5.0% |
| Gotchas | 112 | 86,937 | 78.0% |
| Pointer-versus-symlink rationale | 29 | 2,330 | 2.1% |
| **Total** | **432** | **111,395** | **100.0%** |

The main problem is unequivocal: Gotchas alone is 86,937 bytes. Light copy-editing cannot meet the
ceiling while retaining its incident narratives.

### Delivery budget

The implementation target is **at most 24,576 bytes (24 KiB)**, not merely 30,720 bytes. That
leaves 6,144 bytes of growth headroom below the hard ceiling and is small enough that Codex's
default 32 KiB budget has room for project-document framing. A proposed budget is:

| New `AGENTS.md` component | Budget |
|---|---:|
| Purpose, mandatory reading, and architecture pointer | 2,400 B |
| Card/delegate and local-stack front doors | 3,200 B |
| Always-loaded safety rules | 3,300 B |
| Area-to-owner-document routing index | 4,000 B |
| Short, trigger-based high-risk index | 7,000 B |
| Browser/secret boundary and `CLAUDE.md` pointer note | 1,500 B |
| Headings, link text, and growth reserve within the target | 3,176 B |
| **Total target** | **24,576 B** |

The high-risk index is not a compressed duplicate of all 75 Gotchas. It contains only situations
where an agent must change its immediate command or avoid irreversible harm before it can consult
the routed owner document: AppHost restart, dev Compose, secrets/browser filling, production
ports, destructive volumes, test/build front doors, and session delivery boundaries.

## Gotchas classification

Legend:

- **A — already enforced; shrink:** code, a wrapper, validation, or regression tests already
  protect the invariant. Keep at most a terse trigger and owner link in `AGENTS.md`; move the
  incident narrative to the owning living document.
- **E — enforce mechanically:** the current rule depends materially on a reader. File the named
  follow-up before removing its temporary short note. These mechanisms are deliberately separate
  cards, not opportunistic code bundled into the documentation rewrite.
- **D — document only:** no safe, proportionate mechanism can determine the human intent or
  external state. Keep the rule in the owner runbook; retain it in the core only when it is an
  immediate, high-consequence operator action.

There are **56 A**, **8 E**, and **11 D** entries. The table covers every current Gotchas bullet;
names are abbreviated only to keep the audit readable.

| # | Current Gotcha | Class | Rewrite disposition / concrete mechanism |
|---:|---|:---:|---|
| 1 | 17203 serves built bundle; wait for rebuild | A | `client/scripts/serve.mjs` and `client-mode.ps1` own it; one core trigger plus bootstrap link. |
| 2 | Decision question is revision/attention, not a column or alert | D | Product-model intent cannot be inferred; keep in card-lifecycle/orchestration docs. |
| 2a | Parked machine message with no open task is cancelled, not retried | A | The parked-message sweep and attention state enforce the outcome; move detail to session-runtime invariants. |
| 3 | `-Scope` areas, holds, warnings, and drift | A | `ScopeDriftPolicy`, area-map contract tests, and task events own it; link from delegate guidance. |
| 4 | Use `docker-compose.dev.yml` | D | Raw shell intent cannot safely be intercepted; keep one command in bootstrap. |
| 5 | Postgres health before server | A | AppHost/dev scripts already wait and compose starts the database; shrink to the front door. |
| 6 | `npm install` before local client tools | D | Missing modules already fail clearly; keep in bootstrap only. |
| 7 | Storybook v9 has no addon-essentials package | D | Version-specific package knowledge belongs with Storybook setup, not universal instructions. |
| 8 | Foreign stale DCP on 17202 | D | Only an operator can establish foreign ownership; retain the diagnostic in the runbook. |
| 9 | Starting an AppHost | D | The required detached launch is an operator action; retain only the canonical command. |
| 10 | Restarting an AppHost and lock meanings | A | `restart-apphost.ps1` lock/refusal semantics are the mechanism; keep its command and exit-code warning. |
| 11 | Podman-looking DCP timeout | A | Restart diagnostics already distinguish the collision shape; move evidence to AppHost troubleshooting. |
| 12 | AppHost must not hard-code messaging broker | A | `Antiphon.AppHost/Program.cs` conditionally forwards configuration; keep a compact security boundary. |
| 13 | Stale daemon supervisors | A | Daemon scripts manage current supervisors; retain recovery command in operations reference. |
| 14 | Windows paths in `appsettings.json` | E | Add a typed path-settings validator that rejects forward-slash values for Windows-only filesystem settings, with startup/config tests. |
| 15 | Postgres credentials and persistent volume | D | Deleting a Docker volume is intentional external destruction; keep a concise warning in bootstrap. |
| 16 | Postgres stuck in `Created` / HNS | A | Existing AppHost pre-test detects and warns; detailed probe belongs in operations troubleshooting. |
| 17 | TUnit command, headed/process limits, project sequencing | E | Add one canonical server-test front door that serializes the two incompatible projects and validates the process-spawn lane; then reference it. |
| 18 | Vitest wrapper and global timeout | A | `scripts/test-client.ps1` is the safe front door; shrink to its command. |
| 19 | E2E requires a fresh `client/dist` | A | `EnsureClientBundleIsCurrent` hard-fails stale output; move details to testing guide. |
| 20 | E2E diagnostics location/setup | D | Test authoring choices need explanation, not a runtime gate; keep in testing guide. |
| 21 | 17204 runner port and E2E isolated runner | A | AppHost, daemon scripts, settings, and fixture own it; retain a one-line port contract. |
| 22 | Transcript-format runner mismatch | A | Runner refuses incompatible launch; recovery command goes in operations reference. |
| 23 | Integration tests share Postgres; scope assertions | D | Assertion relevance is semantic; retain examples/rationale in testing guide. |
| 24 | Succeeded row does not own/kill a process | A | Reconciliation, ownership states, and zombie census are code-level policy; move detail to session invariants. |
| 25 | Test `Program` boot must not hit production runner | A | `ProductionRunnerGuard` and `RefusingSessionRunnerClient` make the common test assembly safe. |
| 26 | Surface database's actual error | A | `DescribeDbFailure`, inner `ConflictException`, and middleware preserve it; keep no narrative in core. |
| 27 | Attached herdr panes detach, never kill | A | Sidecar origin and kill policies enforce it; `docs/herdr-sessions.md` owns details. |
| 28 | Sessions survive runner restarts | A | Pty-host split and re-adoption enforce it; ADR/spec owns rationale. |
| 29 | Build/test while daemons use alternate `OutputPath` | E | Add `scripts/build-isolated.ps1` with a sanitized output tag and cleanup; stop teaching hand-quoted MSBuild paths. |
| 30 | Trailing-space `bin-*` breaks MSBuild | E | The same isolated-build front door must reject malformed tags and preflight trailing-whitespace build directories with an exact remediation. |
| 31 | Multi-line TUI input needs LF/paste/separate Enter | A | `SessionMessageQueueService.DeliverAsync` and contract tests centralize it. |
| 32 | Local slash commands and `/clear` fork | A | Transcript classifiers/tailer and canaries enforce the processing rule. |
| 33 | Manual compaction is an end; automatic is not | A | Transcript normalization, queue flush, and lockstep tests enforce it. |
| 34 | Timeout is OCE, not always shutdown | E | Add a focused analyzer/repository contract for unfiltered OCE catches in long-lived hosted pumps; allow only filtered cancellation or explicit retry handling. |
| 35 | Pre-dispatch failure reminder ramp | A | Dispatcher, tick, and attention state enforce it. |
| 36 | Interrupted `git worktree add` cleanup | A | Worktree timeout/rollback/healing are implemented and tested. |
| 37 | Restart can leave false Working | A | Backfill/restart boundaries and working-state tests enforce the two paths. |
| 38 | Exact transcript sidecar claim beats heuristic | A | Claim-strength policy and tests enforce it. |
| 39 | Transcript needs positive ownership evidence | A | Binding policy, incidents, and tests enforce it. |
| 40 | TUI chunk loss and fakeclaude model | A | Delivery ceilings plus real/fake contract tests own it. |
| 41 | Modern ConPTY binary ships flag-off | A | Project staging, provenance, and backend contract tests enforce it. |
| 42 | Delivery ceilings conditional on real backend | A | `PtyDeliveryProfile` and runner capabilities enforce it. |
| 43 | Pasted-body placeholder is delivery evidence | A | Composer evidence logic and tests enforce it. |
| 44 | fakeclaude clips typed, not pasted input | A | Fake contract tests enforce the model. |
| 45 | Modern ConPTY DA1 response | A | `ModernConPtyConnection` and DA1 tests enforce it. |
| 46 | Interrupted marker ends a turn | A | Transcript-kind implementations and canaries enforce it. |
| 47 | Claude trust dialog is not healthy idle | A | Startup trust handling and canaries own the behaviour. |
| 48 | Pull transcript before kill on absence | A | Delivery/reconciliation guards and tests enforce it. |
| 49 | Sent requires matching `UserPrompt` | A | `PromptSubmissionMatch` and queue verification enforce it. |
| 50 | Sent also requires complete body | A | `DeliveryVerdict.Truncated` and matching tests enforce it. |
| 51 | Failed launch kills/reconciles both directions | A | Kill-and-dispose, re-adoption, and reconciliation tests enforce it. |
| 52 | Mid-turn `QueuedUserPrompt` is not reply identity | A | Channel reply window logic and tests enforce it. |
| 53 | Channel reply route must be durable | A | Durable queue rows, settlement marker, TTL incident, and tests enforce it. |
| 54 | Closing terminal leaves Remote Control orphan | E | CARD-0144 is the existing cleanup-mechanism follow-up; until it ships, keep the one-line `/exit`/resume runbook note. |
| 55 | Stall is detection, never automatic kill | A | Stall policy and checkpoint/cancel front doors enforce the safe response. |
| 56 | Bound task moves its card, preserving manual moves | A | `CardWorkTransitionService` and tests enforce it. |
| 57 | Subscription quota 409 is launch refusal | A | Start/create gate and explicit override enforce it. |
| 58 | `/remote-control` only on supported catalog kind | A | `RemoteControlPolicy` and catalog contract enforce it. |
| 59 | Herdr slug versus visible title | A | Launch-time name policy and collision handling enforce it. |
| 60 | Herdr pid-loss relaunch reuses known pane | A | Sidecar/foreign-occupant policy enforces it. |
| 61 | Herdr backend opt-in/restart limits | A | Backend policy, capability, and sidecar rules enforce it. |
| 62 | Herdr delivery verification/blocked semantics | A | Per-session delivery profile and confirmation logic enforce it. |
| 63 | Herdr events trigger verification, not evidence | A | Event pump confirmation and disagreement policy enforce it. |
| 64 | Unobservable baseline confirmation floor | A | Delivery verification contract and tests enforce it. |
| 65 | CLI stub requires stub receipt | A | `RealCliStubEnv` helpers and canaries enforce the oracle. |
| 66 | Delete Codex rollout only through CLI | D | This is an external tool's data model; retain in Codex test/operator reference. |
| 67 | Codex tests need isolated `CODEX_HOME` | E | Centralize a temporary-home test fixture/launcher and fail fast when it resolves to the user home. |
| 68 | GitHub tracker sync activation/write cadence | A | Endpoint/service separation and tracker tests enforce it. |
| 69 | `#N` means `CARD-000N` only | A | Identifier validation and route tests enforce it. |
| 70 | Card identifier unique per board, scoped resolution | A | `CardIdentifierScope` and API/CLI tests enforce it. |
| 71 | Tracker notification is opt-in targeted send | A | `TrackerSyncNotifier` routing/gates and tests enforce it. |
| 72 | Frozen `TimeProvider` hangs queue tests | E | Supply an approved offset/auto-advance test-clock factory and add a repository contract rejecting ad-hoc frozen providers in queue-host tests. |
| 73 | Full `Antiphon.Tests` looks stalled late | D | Runtime diagnosis depends on current machine/load; keep watched-run guidance in testing guide. |
| 74 | Huge `FileListAbsolute.txt` makes build slow | A | `Directory.Build.targets` size guard already removes oversized ledgers. |

## Documentation ownership after the move

The implementation must move detail before deleting it. Copy a Gotcha's complete current text to
its owner first, preserving measurements, rejected alternatives, cards, and test names where they
explain a non-obvious safety property. Then replace it with either a core trigger or a direct owner
link. Do not make `docs/superpowers/plans/` the sole destination: those documents are historical
snapshots by convention.

| Concern | Living owner after rewrite | Items / current material |
|---|---|---|
| Instruction-file contract | New `docs/agent-instruction-file-contract.md` | Pointer-versus-symlink rationale and the supported import mechanism. |
| Local developer operations | Expand `docs/bootstrap.md` | Prerequisites, local/AppHost modes, ports, Docker/Postgres, autostart, restart/DCP/HNS/supervisor recovery, browser entry points. |
| Testing and build operations | New `docs/testing-and-build.md` | TUnit, Vitest, E2E bundle/diagnostics, shared DB, output paths, long test diagnosis, frozen clocks, and MSBuild ledger recovery. |
| Runtime/session invariants | New `docs/session-runtime-invariants.md` | Transcript ownership/working rules, message delivery, launch/reconciliation, channel reply durability, and provider-specific terminal behaviours. Link to existing ADRs/investigations rather than repeating their full evidence where those are already the source. |
| Pty backend | `docs/adr/0002-modern-conpty-backend.md` plus the new session-invariants document | Backend selection, paste/DA1/ceiling invariants and their code/test anchors. |
| Cards, tasks, scope, and tracker | `docs/orchestration-loop.md`, `docs/agent-card-lifecycle.md`, `docs/workflow-tracker-block.md` | Decision questions, scope lease, transitions, identifiers, quota and tracker notification rules. |
| Agent/provider configuration | `docs/agent-kinds.md`, `docs/ai-agent-tui-configuration.md`, `docs/herdr-sessions.md`, `docs/agent-credentials.md` | Runner kinds, remote control, Herdr behaviour, credentials, broker/config boundaries, Codex test isolation. |
| Browser and external-site work | New short `docs/external-site-operations.md`, pointing to the existing browser-harness and ClaudeBot per-site notes | Browser harness rules, trusted input, secret relay, and Outlook access. |

The new documents are deliberately topic-sized and referenced from the top-level routing index.
They replace prose in `AGENTS.md`; they do not duplicate it. Existing ADRs, investigations, and
specs remain evidence sources and are linked from their living owner where a later maintainer needs
the full incident record.

## Rewrite sequence

### S1 — Establish the byte and link contract

1. Add `scripts/check-agent-context.ps1`. It must use raw file bytes, report current size and
   section sizes, fail above the **24,576-byte delivery target**, and separately report the
   30,720-byte hard ceiling for an actionable error.
2. Add a small repository contract test beside the existing area-map/document contract tests. It
   asserts the same raw-byte limit and that every local owner document named by the core routing
   index exists. This prevents a later prose accretion or a dead move target from silently
   restoring truncation.
3. Add the check to the normal documentation/verification command used by this repository. Do not
   rely on a local Git hook: hooks are optional per clone and cannot enforce the shared invariant.

### S2 — Preserve and assign the detail

1. Create the four focused living documents in the ownership table where no current owner exists.
2. Relocate the full narratives, preserving exact operational commands only in their owner
   document. Make every moved entry findable by its CARD identifier and a stable short heading.
3. Replace overlapping narratives with one source. In particular, runtime invariants must not be
   restated in both the new session document and the ConPTY ADR; the ADR retains architecture and
   measurements, while the runtime document links it for an operational rule.
4. File the six new mechanism cards from the E rows (path validation; server-test front door;
   isolated build output/trailing-space preflight; OCE pump guard; Codex test home guard;
   time-provider guard) and link the already-open CARD-0144 for Remote Control cleanup. Do not
   make their delivery a hidden prerequisite for the documentation move: the temporary terse note
   remains until each mechanism lands.

### S3 — Rewrite the universal core

1. Replace the current "single source of truth" claim with: `AGENTS.md` is the universal index and
   mandatory safety core; each linked living document owns its detailed behaviour. Keep
   `CLAUDE.md` as the one-line import pointer and move its clone/symlink rationale to the new
   instruction-file contract.
2. Keep a compact "read before changing" matrix: backend/domain/client conventions →
   `docs/project-context.md`; cards/delegation → `docs/orchestration-loop.md`; channel work →
   `docs/telegram.md`; sessions/pty → session/runtime owner; Herdr → `docs/herdr-sessions.md`;
   external browser/credentials → external-site owner; local operations/tests → their new guides.
3. Retain only terse action rules that prevent an immediate bad action. State a rule once, without
   incident history, measured timelines, duplicated configuration, or alternate commands. Use
   command names and links in place of copied examples.
4. Keep the required local facts that a delegate routinely needs before it can follow a link:
   canonical AppHost restart command, `verify-dev-stack` command, production runner port 17204,
   built-client watcher fact, card CLI file-backed text/concurrency rule, and the secret/browser
   non-disclosure boundary.
5. Remove the 75-entry Gotchas heading as a dump. Replace it with a short trigger index grouped by
   local stack, tests/build, sessions, cards/tracker, and external tools. An item whose mechanism
   already makes an unsafe path impossible may be a routed link rather than a core bullet.

### S4 — Add mechanics independently of wording

Each E item has a bounded implementation and acceptance condition. These should be dispatched as
separate cards so the size rewrite remains reviewable.

| Candidate | Scope and acceptance condition |
|---|---|
| Windows path-settings validation | Typed settings validation rejects Windows-only configured filesystem paths using `/`; test both a rejected bad value and accepted canonical backslash value. |
| Server-test front door | One script runs the two incompatible TUnit projects sequentially, carries headed/process settings explicitly, and reports a non-zero project result without a pipeline-mask ambiguity. |
| Isolated build front door | `scripts/build-isolated.ps1` accepts a safe tag rather than an arbitrary quoted `OutputPath`, appends the forward slash itself, detects existing whitespace-suffixed `bin-*` paths, and offers precise cleanup. |
| OCE hosted-pump guard | A focused analyzer or repository contract finds unfiltered `catch (OperationCanceledException)` in long-lived pumps unless the catch checks the owning cancellation token or delegates to a retry policy. |
| Remote Control cleanup | CARD-0144 remains the owner: ensure a close/resume workflow can identify and clean stale remote sessions without terminating a live one. |
| Codex test home guard | A single fixture creates a temporary `CODEX_HOME` for every headed/stub-proxy test and fails before launch if the resolved home is the user's desktop home. |
| Test clock guard | An approved offset/auto-advance clock factory replaces frozen ad-hoc providers for queue-host tests; a narrow contract rejects new frozen providers in that population. |

### S5 — Validate the actual result

1. Run `pwsh -NoProfile -File scripts/check-agent-context.ps1`; it must report `<= 24,576` raw
   UTF-8 bytes. Any result above 30,720 is an immediate failure, not a warning or a reason to
   relax the project-document limit.
2. Run the new fast document contract and `git diff --check`.
3. Run `codex debug prompt-input` in the repository and verify that an anchor at the end of the
   new core is present, proving Codex did not truncate project instructions.
4. Verify `CLAUDE.md` still imports `AGENTS.md`, all routing-index paths resolve, and no detailed
   Gotcha is duplicated in two living owners.
5. Record final bytes and the before/after section table in the implementation report. Do not claim
   success from line count or word count; the enforced unit is bytes.

## Explicit non-goals

- Do not rewrite `AGENTS.md` in this Plan pass.
- Do not discard rationale merely to fit the cap. Move it to a living owner unless an A/E mechanism
  now makes the prose redundant.
- Do not create an orchestrator-only replacement for universal instructions. Conditional bundles
  already supply role-specific content.
- Do not add broad hooks, raw command interception, or a new MCP server merely to make a sentence
  disappear. The E mechanisms above have specific unsafe actions and measurable tests; all other
  remaining guidance stays documentation.
- Do not increase Codex's `project_doc_max_bytes`; the repository must fit the default, with
  margin.
