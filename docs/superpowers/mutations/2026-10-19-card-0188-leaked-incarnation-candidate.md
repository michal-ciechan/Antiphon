# CARD-0188 mutation candidate

Code task and landing owner: `57b0232c`. Master only; restart: none.
Plan: [detection plan](../plans/2026-10-19-card-0188-leaked-incarnation-detection-plan.md).
This is an **unbound candidate**, not a SourceLanding pack or an execution receipt.
All **PC-1 through PC-27 remain pending**, including PC-1 after Code's natural red.
Mutation must bind the exact landed source, check compilation, perform method-scoped
baseline/red/restored-green cycles, and discover missing controls. No deliberate
mutant has been compiled or executed in Code. The payloads below are proposed C#
edits, not claims of compilation at the future landed SHA.

Source anchors below are in `server/Application/Services/AttentionService.Leaks.cs`.
Assertion lines are in `tests/Antiphon.Tests/Application/AttentionServiceTests.C691.cs`
at the Code S2 revision; use the method and literal assertion if landing shifts lines.
The retained fake's Start/Input/Kill counters increment before refusal in
`AttentionServiceTests.cs`; snapshots use a separate context and all mapped scalars.

| Binding | Exact method segment | Minimum executions |
|---|---|---:|
| H | `a_historical_standing_incarnation_without_a_current_pointer_is_unowned` | 1 |
| U | `a_live_session_no_agent_names_is_unowned*` | 2 |
| O | `a_live_session_an_agent_names_is_not_unowned*` | 4 |
| L | `remote_leak_rows_require_confirmed_liveness*` | 3 |

Every filter is `/*/*/AttentionServiceTests/<segment>` with the segment above,
never a whole class. Keep `TUNIT_MAX_PARALLEL_TESTS=1`. All controls touch the same
production file: do not batch. Preserve the plan's 273-minute Mutation estimate;
actual costs and receipt refusals remain separate facts. Mutation owns all deliberate
defects, restoration and evidence in its assigned external root.

| PC | Source anchor and proposed defect | Intended assertion and variant | Status |
|---|---|---|---|
| PC-1 | Line 134: add `s.StandingAgentId == null &&` to the candidate predicate | H:48 initial `ShouldHaveSingleItem`, zero A rows | Pending |
| PC-2 | Replace the `owned` query (144-146) with raw non-null pointer strings; at 147 use `!owned.Contains(s.Id.ToString())` | O:452 final absence, `agent` uppercase compact GUID | Pending |
| PC-3 | Line 144: add `a.AlwaysOn &&` to the pointer query | H:70 absence at the other-agent-to-A phase (call at 107) | Pending |
| PC-4 | Line 134: remove only `s.CardId == null &&` | H:70 absence in the card-bound phase (call at 145) | Pending |
| PC-5 | Line 135: replace `< cutoff` with `<= cutoff` | U:398 absence at exactly ten minutes, local/remote, unstamped/stamped | Pending |
| PC-6 | Line 134: remove only `&& s.EndedAt == null` | O:452 final absence, `ended` Running row | Pending |
| PC-7 | Line 137: append `|| s.Status == SessionStatus.Stopped` inside the status disjunction | H:70 absence in Stopped/unended phase (call at 117) | Pending |
| PC-8 | Lines 139-140: remove only `t.Status == AgentTaskStatus.Queued ||` | O:431 absence, Queued phase of `task` | Pending |
| PC-9 | Lines 139-140: remove only `t.Status == AgentTaskStatus.Dispatched ||` | O:431 absence, Dispatched phase of `task` | Pending |
| PC-10 | Lines 139-140: remove only `t.Status == AgentTaskStatus.Working ||` | O:431 absence, Working phase of `task` | Pending |
| PC-11 | Lines 139-140: remove only `|| t.Status == AgentTaskStatus.Blocked` | O:431 absence, Blocked phase of `task` | Pending |
| PC-12 | Line 147: use the remote expression below, then admit `_runnerDirectory is null` with a short-circuit OR | L:478 null-directory A absence | Pending |
| PC-13 | Line 147: remote expression below, remove only `!_runnerDirectory.RemoteInventoryPending(s.RunnerId) &&` | L:471 stamped A absence, `pending` | Pending |
| PC-14 | Line 147: remote expression below, remove only `!remoteUnknown.Contains(s.Id) &&` | L:471 stamped A absence, `unknown` | Pending |
| PC-15 | Line 147: remote expression below, remove only `&& remoteLive.Contains(s.Id)` | L:471 stamped A absence, `gone` | Pending |
| PC-16 | After line 142: injection wrapper below with Kill payload | H:56 `KillCalls.ShouldBe(0)` | Pending |
| PC-17 | After line 142: wrapper with session-write payload | H:50 persisted session scalar equality | Pending |
| PC-18 | After line 142: wrapper with agent-write payload | H:51 persisted agent scalar equality | Pending |
| PC-19 | After line 142: wrapper with task-write payload | H:52 persisted task scalar equality | Pending |
| PC-20 | After line 142: wrapper with incident-insert payload | H:53 persisted incident row-set equality | Pending |
| PC-21 | After line 142: wrapper with Input payload | H:57 `InputCalls.ShouldBe(0)` | Pending |
| PC-22 | After line 142: wrapper with Start payload | H:58 `StartCalls.ShouldBe(0)` | Pending |
| PC-23 | Line 149: change the DTO's `s.Id` SessionId argument to `null` | H:48 initial A row missing | Pending |
| PC-24 | Line 149: change the following AgentId `null` to `s.StandingAgentId` | H:78 `row.AgentId.ShouldBeNull()` | Pending |
| PC-25 | Line 156: change actions to `[AttentionAction.OpenDrawer, AttentionAction.OpenAgent]` | H:80 exact actions equality | Pending |
| PC-26 | Line 71: replace `group.First() with` with `group.Last() with` | H:156 overlap Kind equals SessionStopStuck | Pending |
| PC-27 | Lines 73-74: replace the Evidence expression with `group.First().Evidence` | H:158 overlap evidence contains `Also SessionUnowned` | Pending |

For PC-2 the replacement query is:

```csharp
var owned = (await _db.Agents.AsNoTracking().Where(a => a.PersistentSessionId != null)
    .Select(a => a.PersistentSessionId!).ToListAsync(ct)).ToHashSet();
```

For PC-12-15 replace only `RemoteLeakConfirmed(s, remoteLive, remoteUnknown)`
at the **unowned** call site with this equivalent expression before deleting
the named term. Keep the shared helper and pool call unchanged:

```csharp
(s.RunnerId is null || (_runnerDirectory is not null
    && !_runnerDirectory.RemoteInventoryPending(s.RunnerId)
    && !remoteUnknown.Contains(s.Id) && remoteLive.Contains(s.Id)))
```

PC-12's defective expression is specifically:

```csharp
(s.RunnerId is null || _runnerDirectory is null
    || (!_runnerDirectory.RemoteInventoryPending(s.RunnerId)
        && !remoteUnknown.Contains(s.Id) && remoteLive.Contains(s.Id)))
```

For PC-16-22 insert one payload inside this wrapper after the empty-candidate
return. Only one control is present at a time. H has one stamped unpointed A
and a stamped current B; selecting only the terminal-task-associated candidate
makes the write target deterministic and leaves current B untouched.

```csharp
var stamped = candidates.FirstOrDefault(s => s.StandingAgentId != null
    && _db.AgentTasks.Any(t => t.AgentSessionId == s.Id));
if (stamped is not null)
{
    // Insert exactly one payload below.
}
```

PC-16:

```csharp
try { await _runnerClient.KillAsync(stamped.Id, ct); }
catch (NotSupportedException) { }
```

PC-17:

```csharp
await _db.AgentSessions.Where(s => s.Id == stamped.Id)
    .ExecuteUpdateAsync(p => p.SetProperty(s => s.FailureReason, "pc-session-write"), ct);
```

PC-18:

```csharp
await _db.Agents.Where(a => a.Id == stamped.StandingAgentId)
    .ExecuteUpdateAsync(p => p.SetProperty(a => a.PoolIdleSince, now), ct);
```

PC-19:

```csharp
await _db.AgentTasks.Where(t => t.AgentSessionId == stamped.Id)
    .ExecuteUpdateAsync(p => p.SetProperty(t => t.Title, "pc-task-write"), ct);
```

PC-20:

```csharp
_db.AgentIncidents.Add(new AgentIncident
{
    Id = Guid.NewGuid(), SessionId = stamped.Id, Kind = AgentIncidentKind.Recovered,
    Severity = AlertSeverity.Warning, Message = "pc-incident-write", CreatedAt = now,
});
await _db.SaveChangesAsync(ct);
```

PC-21:

```csharp
try { await _runnerClient.SendInputAsync(stamped.Id, "pc-input", ct); }
catch (NotSupportedException) { }
```

PC-22:

```csharp
try { await _runnerClient.StartAsync(stamped.Id, null!, ct); }
catch (NotSupportedException) { }
```

The fake refuses Start before reading its spec. A compile/fixture error or zero
tests never counts as red. The plan's missing-control audit remains Mutation's
responsibility; this candidate establishes no PC-clean claim.
