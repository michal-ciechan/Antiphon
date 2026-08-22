# CARD-0139 — changing an agent's `Kind` after creation via the API: plan

**Date:** 2026-08-22 · **Card:** CARD-0139 (`5a2aacc3-97f7-46d2-96e0-17523ff57de8`) ·
**Status:** plan (no implementation in this pass) ·
**Verified against:** master `1faa2fb`; every line/behaviour claim below was read out of the code on
that commit, and the row census was read out of the live dev database (`antiphon-postgres`, port
17280) on 2026-08-22.

**Sibling:** CARD-0138's plan **has landed** —
`docs/superpowers/plans/2026-08-22-card-0138-agent-kind-profile-sync-plan.md`. This plan is written
*against* it, not around it, and §"Where this sits relative to CARD-0138" states exactly which of
its decisions this one inherits and the one place it deliberately departs.

---

## Verdict

The card's *need* is real. The card's *mechanism* — "add a `Kind` setter so an operator can correct
a wrong `Kind`" — is the wrong lever, and shipping it as stated would re-open the exact drift
CARD-0138 closes. Three facts, all read out of `1faa2fb`, decide the shape:

1. **`Agent.Kind` does not select the program a standing agent launches, and never has.**
   `AgentControlService.StartInteractiveSessionAsync` derives `isClaudeCode`/`isGrok`/`isCodex`
   (`server/Application/Services/AgentControlService.cs:162`, `:163`, `:170`) from
   `PeekProfileKindAsync` (`:161`, defined `:377`) — attached **profile**'s `Kind`, then the
   installation-default profile's, then the legacy registry's default definition. `agent.Kind` is
   never read. The resolver behind it agrees: `AgentTuiLaunchResolver.cs:375` builds the spec with
   `Kind: profile.Kind`, and the no-profile fallback
   (`AgentLaunchResolution.ResolveLegacyAsync`, `AgentTuiLaunchResolver.cs:106`) resolves
   `agentRegistry.Settings.DefaultDefinition` — again not `agent.Kind`. Every downstream consumer
   takes `spec.Kind`: the session row (`AgentControlService.cs:328`, `OrchestratorService.cs:127`,
   `:155`, `CardService.cs:644`), the protocol adapter (`AgentSessionService.cs:168`, `:360`,
   `:881`, off `session.AgentKind`), and the runner's transcript contract
   (`SessionRunnerHttpClient.cs:50`, `:67`).

   The existing launch-kind test harness proves it independently: `NamedCodexAgentLaunchTests`
   obtains Codex composition by making the **registry's default definition** Codex-kind
   (`tests/Antiphon.Tests/Application/NamedCodexAgentLaunchTests.cs:115-120`) and never touches
   `agent.Kind` — `SetAgentAsync` (`:130`) writes `ModelLevel`, `ModelId` and `SystemPromptAppend`
   and nothing else.

   **Consequence for the card's own test ask.** The card (and the dispatch brief) ask for "a test
   that PATCHing `Kind` actually changes what gets composed at the next launch". That test cannot
   be written truthfully, because it is not true. It is replaced by T4/T5 below, which pin the real
   contract in both directions: a `Kind` PATCH changes the column and **not** the composed
   arguments; a `TuiProfileId` PATCH changes both.

2. **`Agent.Kind`'s only readers are warm-pool paths, and they are the one place a write is
   genuinely dangerous.** Whole-server census of the property:

   | Site | Direction | Guard |
   |---|---|---|
   | `AgentTaskDispatcher.ResolveAgentAsync:1653` (`existing.Kind = task.AgentKind`) | write | none today; CARD-0138 W4 adds `IsPoolDelegate` |
   | `AgentTaskDispatcher.ResolveAgentAsync:1675` (fresh delegate initializer) | write | pool by construction |
   | `TryReuseWarmAgentAsync:1802` (`pinned.Kind != claimed.AgentKind`) | read | unreachable for standing rows — `:1793` returns first |
   | warm-pool predicate `:1843` (`a.IsPoolDelegate && … a.Kind == claimed.AgentKind`) | read | pool-only in the predicate |
   | `RetireIdleWarmAgentsAsync:2140` (group warm rows by directory+kind) | read | pool-only |

   `PlaceOnStandingAgentAsync` deliberately refuses to use the row (`:1941`: *"nothing but the
   dispatcher writes `Agent.Kind`, so a user's Grok agent still reads ClaudeCode"*) and reads the
   **live session's** kind instead. So for a standing agent the column has zero readers, and for a
   pool delegate it is the only thing standing between a Grok task and a warm Claude process.

3. **It is on no DTO, so nobody can see it.** `AgentSummaryDto` (`AgentDtos.cs:14`) and
   `AgentDetailDto` (`:65`) carry no `Kind`; neither does `AgentTuiConfiguredSelectionDto` (`:129`,
   which carries `ProfileDisplayName` but not the profile's kind). The live mismatch this card came
   from was found by hand-written SQL, and could only have been found that way.

**So the defect CARD-0139 is really about is not a missing setter. It is that a column which cannot
be seen, cannot be corrected in place, and has exactly one code path where writing it matters — the
warm pool — was reachable only by deleting the agent.** The remedy is therefore mostly *read*, with
a deliberately narrow write.

---

## Live census (re-run for this plan, 2026-08-22)

```sql
SELECT a."Name", a."Kind", a."TuiProfileId" IS NULL AS no_profile,
       a."IsPoolDelegate", p."Kind" AS profile_kind
FROM   "Agents" a
LEFT   JOIN "AgentTuiProfiles" p ON p."Id" = a."TuiProfileId"
ORDER  BY a."IsPoolDelegate", a."Name";
```

26 rows. Reproduces CARD-0138's audit exactly, and adds the fact that plan did not need to establish:

- **2 mismatches, both standing, both `Kind = 1`:** `Codex` (profile kind 2) and `Grok 4.6`
  (profile kind 4).
- **13 standing agents, 13 with a profile. Zero standing agents have a null `TuiProfileId`.**
- **13 pool delegates; the only 2 null-profile rows in the database are pool delegates**
  (`task-4880c164`, `task-c8bdfe87`).

That last line is the one that sizes this card. Under CARD-0138's D1 the write arm designed below
applies only to a non-pool agent with no profile — **a shape that does not currently exist**, and
which the UI cannot even produce (`AgentTuiSelection.tsx:69` marks the profile `Select` `required`).
The write is future-proofing and an assertion; the *read* is the part with subjects today.

---

## Where this sits relative to CARD-0138

CARD-0138 landed a plan whose §"Deliberately not done" says, verbatim:

> **Not done: any change to `CreateAgentRequest`/`UpdateAgentRequest`, the client, or any DTO.**
> Adding a `Kind` to the wire would create the drift this card is trying to remove.

That is a direct collision with CARD-0139's stated ask, and it is resolved rather than ignored.

**Inherited without change** — this plan does not restate or re-derive them:

- **D1, the invariant.** *If `Agent.TuiProfileId` is not null, `Agent.Kind` equals that profile's
  `Kind`. If it is null, `Agent.Kind` is the row's own truth and nothing derives it.* Every rule
  below is a consequence of D1, not a second rule about the same relationship.
- **W1** (`ApplyTuiSelectionAsync` syncs `Kind` from the profile) is what makes a `TuiProfileId`
  PATCH the primary in-place correction lever — see D6.
- **D5**'s backfill migration fixes the two live mismatches. CARD-0139 adds no second backfill.

**Where this plan departs, and why it is not a second validation rule.** CARD-0138 rejected a wire
`Kind` on the grounds that it would be "the independently-settable field the card is worried
about". Under D1 it need not be settable at all where a profile is attached: the field designed
here is an **assert-or-set**, and on a profiled agent it is *purely an assertion* — it can agree
(no-op) or disagree (refused). It cannot move `Kind` away from the profile in any code path. That
is D1 enforced at one more boundary, not a competing rule.

**Ordering constraint, load-bearing:** CARD-0139 must land **after CARD-0138 S1 (W1)**. Before W1,
`ApplyTuiSelectionAsync` does not sync `Kind`, so a profiled agent's stored `Kind` is always
`ClaudeCode` regardless of profile — and the assertion below would reject every honest PATCH
against a Codex agent while accepting the lie. Landing this first would be worse than not landing
it.

---

## Design decisions

### D1 — the field

```csharp
// CARD-0139. Null = leave unchanged (the convention every optional field on this record follows).
// ASSERT-OR-SET, not a free setter: with a TuiProfileId attached — the agent's existing one, or one
// this same request supplies — Kind is DERIVED from that profile (CARD-0138 D1) and this value is
// only checked against it; a disagreement is refused rather than written. It is applied as a value
// only for an agent with no profile at all, and never for a pool delegate.
AgentKind? Kind = null,
```

on `UpdateAgentRequest` (`server/Application/Dtos/AgentDtos.cs:204`), appended after
`LaunchEnv` — a new trailing optional positional parameter, the same way `BundleKeys`, the
auto-compact trio and `LaunchEnv` were each added, so no existing caller changes.

Wire shape: `AgentKind` serializes as a **string** and integers are rejected
(`Program.cs:218`, `JsonStringEnumConverter(namingPolicy: null, allowIntegerValues: false)`), so
this is `"kind": "Codex"` and a caller sending `"kind": 2` gets a 400 from the binder before
`UpdateAsync` is reached. Worth knowing because every value in the card and in the database is
written as an integer.

**`CreateAgentRequest` does NOT get a `Kind`.** Out of scope and it should stay that way: after
CARD-0138 W1, create derives `Kind` from the chosen profile, and the client's profile `Select` is
`required` (`AgentTuiSelection.tsx:69`) so a created agent always has one. A create-time `Kind`
would be settable with nothing to assert it against.

### D2 — the rule, in application order

Applied in `AgentService.UpdateAsync` (`server/Application/Services/AgentService.cs:317`)
**after** the `request.TuiProfileId` block at `:371-378`, never before. The ordering is the whole
correctness of the rule: a single PATCH that moves an agent from the `claude` profile to the
`codex` profile *and* asserts `Kind = Codex` must be checked against the **new** profile. Checking
first would reject the one PATCH that is most obviously right.

```
1. request.Kind is null                      -> nothing happens. (convention)
2. agent.IsPoolDelegate                      -> 409 agent_kind_pool_delegate
3. agent.TuiProfileId is not null (post-step-371)
     3a. requested == profile.Kind           -> accepted, no write (assertion satisfied)
     3b. requested != profile.Kind           -> 409 agent_kind_profile_mismatch
4. agent.TuiProfileId is null                -> agent.Kind = requested
```

Step 3's profile kind is read from the database by id, not from `request.TuiProfileId` — after
step `:370` the agent's attached profile is authoritative whether this request changed it or not.

**Why 409 and not 422.** It matches the sibling checks in the very method this rule lives beside:
`ApplyTuiSelectionAsync` throws `ConflictException` with codes `profile_disabled` and
`profile_not_validated` (`AgentService.cs:973`, `:979`). `ConflictException(message, code)` maps to
409 with the code in the problem document (`server/Application/Exceptions/ConflictException.cs:12`);
`ValidationException` is 422 and is for per-field input errors (`ValidationException.cs:11`). A
`Kind` that disagrees with the attached profile is not a malformed field — it is a request against
the current state of another resource. Same genre, same exception, same file's convention.

Message text must name **both** kinds and the profile, and say what to do:

> `Agent 'Codex' runs the 'codex' runner profile (Codex); its Kind cannot be set to ClaudeCode.
> Change the agent's runner profile instead, or omit kind.`

### D3 — why a pool delegate is refused even when the requested kind agrees

Rule 2 fires before rule 3, so a `Kind` on a pool delegate is refused unconditionally rather than
allowed-when-harmless. Three reasons, in order of weight:

1. A pool delegate's `Kind` is what the warm-pool predicate (`AgentTaskDispatcher.cs:1843`) matches
   on, and the dispatcher owns the write (`:1653`). An operator write racing a claim is the exact
   failure CARD-0138's T5 exists to prevent, arriving from the API instead of the importer: a task
   claims a warm process that is not the program it asked for, dispatch looks successful, and the
   report never comes.
2. "Allowed when it agrees" is a race, not a safe subset — the row can be restamped between the
   read and the write.
3. Pool rows are ephemeral furniture (`task-<shortid>`, deleted when their task settles). There is
   no operator workflow that wants to edit one, so refusing costs nothing and the refusal message
   can say so plainly.

This is the single most important validation in this card, and it is the mirror image of
CARD-0138's most dangerous edit.

### D4 — the agent does NOT need to be Stopped or idle

Explicitly, and more strongly than "not enforced":

- For a **standing** agent there is *no reader of `Agent.Kind` anywhere on any launch, session or
  supervision path* (§Verdict 1 and 2). The write cannot affect a running session, a queued
  message, a live composer or the next launch, because nothing consults it. It is not that the
  effect is deferred to the next launch — there is no effect to defer.
- For a **pool delegate**, liveness genuinely matters, and that case is refused outright (D3).

So no status gate, no `Stopped` precondition, and no "restart required" hint in the response.
The one thing that *would* need a restart — the program the agent actually runs — is changed by a
`TuiProfileId` PATCH, and that already surfaces through
`AgentTuiLiveSessionSelectionDto.PendingRestart` (`AgentDtos.cs:135-138`), computed at
`AgentService.cs:~300` against the live session's revision. Nothing new is needed there.

### D5 — expose `Kind` read-only on the agent DTOs

`AgentDetailDto` and `AgentSummaryDto` each gain a trailing `AgentKind Kind = AgentKind.ClaudeCode`
(default-valued so no existing construction site changes), populated from the row, plus the
matching optional field on the client's `AgentSummaryDto`/`AgentDetailDto` in
`client/src/api/agents.ts`.

This is the half of the card with real subjects today, and it is the half that actually explains
why "delete and recreate" was the only remediation: an operator could not *see* the wrong value, so
they could not know a `TuiProfileId` re-PATCH would fix it. Read-only on the wire — the setter is
D1's assert-or-set and nothing else — so it creates no second source of truth.

### D6 — the repair path is a `TuiProfileId` PATCH, and it should be written down

After CARD-0138 W1, `PATCH /api/agents/{id}` with the agent's **existing** `tuiProfileId` re-runs
`ApplyTuiSelectionAsync` and re-syncs `Kind` from the profile. That is the in-place correction the
card asks for, it needs no new endpoint, and it survives any future drift from a writer nobody has
found yet. It is idempotent and touches no session.

It gets a test (T6) and a sentence in `Agent.Kind`'s XML doc rather than a `POST
/api/agents/{id}/resync-kind`. A dedicated endpoint would be a third way to write the column, which
is the thing this pair of cards is trying to reduce.

### D7 — no raw `Kind` selector in the UI

**Recommendation: do not add one.** The card leaves this open; the code answers it.

`AgentTuiSelection.tsx:43` already labels every profile option with its kind —
``label: `${profile.displayName} (${profile.kind})${profile.isDefault ? ' · default' : ''}` `` —
and the `Select` is `required` (`:69`). So the agent-edit UI *already* expresses kind
implicitly-via-profile-selection, exactly as the card speculates, and it already prevents the
null-profile state where a raw `Kind` control would be the only lever. A second control would be
the independently-settable field CARD-0138 removes, re-introduced on the one surface an operator
touches most.

What the UI gets instead, optional and cheap (S3): the settings modal renders a Mantine `Alert`
when `agent.kind` (D5) disagrees with the selected profile's `kind`, naming both and pointing at
the profile picker. It lives in `AgentSettingsModal.tsx` beside the existing `<AgentTuiSelection>`
at `:239`. After CARD-0138 this should never render — it is a **canary for the invariant**, which
is worth having precisely because CARD-0138 D4 declined the database constraint that would have
made drift impossible.

---

## Slices

Three, each independently landable and revertible. **All three land after CARD-0138 S1.**

### S1 — `Kind` on the read DTOs (D5)

`AgentDetailDto` + `AgentSummaryDto` + `client/src/api/agents.ts` types + the projections in
`AgentService`. Tests T1, T2. No behaviour changes; a column becomes visible.

### S2 — the assert-or-set field (D1, D2, D3, D4)

`UpdateAgentRequest.Kind`, the client's `UpdateAgentRequest` type, and the rule block in
`UpdateAsync` after `:378`. Tests T3–T7. **T5 must land in the same commit as the rule** — it is
the pool-delegate refusal.

### S3 — the mismatch canary in the settings modal (D7) — optional

`AgentSettingsModal.tsx`. Test T8 (vitest). Ship last, or not at all if the team would rather not
carry UI for a state the invariant forbids.

---

## Test coverage

| # | Test | Where | Pins |
|---|---|---|---|
| T1 | `GET /api/agents/{id}` on an agent with a Codex profile returns `kind: "Codex"`; the list endpoint agrees | `tests/Antiphon.Tests/Application/AgentServiceIntegrationTests.cs` | D5 |
| T2 | The client's agent types round-trip `kind` (fixture + render) | `client/src/features/agents/` (alongside `AgentReplyStyle.test.tsx`) | D5, client half |
| T3 | **A null `Kind` in the request leaves the stored value unchanged** — PATCH every other field on a Codex-profile agent with `kind` omitted; `Kind` still reads Codex | `AgentServiceIntegrationTests.cs` | D1's convention half, modelled on `UpdateAsync_with_null_board_keeps_the_agents_board` (`:136`) and the reply-style note at `AgentReplyStyleTests.cs:146` |
| T4 | **A `Kind` PATCH changes the column and NOT the launch** — on a no-profile, non-pool agent, PATCH `kind`, then start it and assert the composed argv is byte-identical to the same start without the PATCH | `AgentServiceIntegrationTests.cs` + a launch assertion modelled on `NamedCodexAgentLaunchTests` | §Verdict 1 — the contract the card assumed was the opposite way round |
| T5 | **A `Kind` on a pool delegate is refused 409 `agent_kind_pool_delegate`, even when it equals the row's current kind, and the row is unchanged** | `AgentServiceIntegrationTests.cs` or `AgentTaskPoolTests.cs` | D3 — the dangerous edit |
| T6 | A `Kind` disagreeing with the attached profile is refused 409 `agent_kind_profile_mismatch` and writes nothing; **and** a PATCH re-sending the agent's existing `tuiProfileId` re-syncs a hand-corrupted `Kind` back to the profile's | `AgentServiceIntegrationTests.cs` | D2 rule 3b, D6 |
| T7 | **One PATCH that changes `tuiProfileId` to the `codex` profile and asserts `kind: "Codex"` succeeds**; the same PATCH asserting `"ClaudeCode"` is refused | `AgentServiceIntegrationTests.cs` | D2's application order — red if the check runs before `ApplyTuiSelectionAsync` |
| T8 | The settings modal renders the mismatch alert when `agent.kind` disagrees with the selected profile's kind, and renders nothing when they agree | `client/src/features/agents/` | D7 / S3 |

T4 is the substitution for the card's "PATCHing Kind changes what gets composed" ask, and it
asserts the opposite outcome deliberately — see §Verdict 1. The complementary direction (a
`TuiProfileId` PATCH *does* change composition) is already CARD-0138's T1/T2 and is not duplicated
here.

Run:

```
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-card0139/ \
  --treenode-filter "/*/Antiphon.Tests.Application/*/*"
pwsh -File scripts/test-client.ps1
```

then delete the ~12 `bin-card0139/` directories
(`Get-ChildItem C:\src\Antiphon -Recurse -Depth 2 -Directory -Filter bin-card0139 | Remove-Item -Recurse -Force`).

---

## Risks, and what is deliberately not done

- **Landing before CARD-0138 S1 inverts the rule.** Without W1 every profiled agent's stored `Kind`
  is `ClaudeCode`, so the assertion refuses the truth and accepts the lie. This is the only hard
  ordering constraint in the card and it belongs in the commit message.
- **The pool-delegate refusal is the one edit that can hurt.** T5 exists for it, and it is checked
  before the profile rule so no ordering change can leak past it.
- **The write arm has zero subjects today** (§Live census: no standing agent has a null profile,
  and the UI cannot create one). This is stated rather than hidden: S2's present-day value is the
  *assertion* and its 409s, not the write. If the team would rather not carry a field with no
  current write subject, S1 + D6 alone satisfy the card's actual remediation need, and that is a
  legitimate way to close it — say so in the card rather than shipping S2 silently reduced.
- **Not done: `Kind` on `CreateAgentRequest`** (D1). Create derives it from the profile after
  CARD-0138 W1 and the UI always supplies one.
- **Not done: a `resync-kind` endpoint** (D6). A third writer of the column is the wrong direction
  for this pair of cards.
- **Not done: a second backfill.** CARD-0138 D5's migration fixes the two live rows; re-doing it
  here would be two migrations racing the same rows.
- **Not done: any change to how `Kind` is consumed.** Every launch decision stays on `spec.Kind`
  from the profile. Making `Agent.Kind` load-bearing at launch is a much larger change and is
  precisely what CARD-0138's own "deliberately not done" item (the dispatcher's spawn path ignoring
  a standing agent's TUI profile) already reserves for its own card.
