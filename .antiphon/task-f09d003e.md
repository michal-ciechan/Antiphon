# task-f09d003e: C772 dispatch test depended on host Grok sign-in (rc group 68)

Outcome: fixed test-only on feat/card-task-f09d003e at 9c438cd794a9d194d1a2e527467f96bd93edfd83 (base 028fcf8c8 = master = rc/2026-11-01). No product code changed; the production guard is untouched.

## Cause (confirmed)
`TaskPlatformDispatchTests.CreateDispatcher` registers a `grok` definition with no Env and leaves
`AgentRegistrySettings.GrokCredentialProbeEnabled` at its default (true). The C772 desktop leg dispatches a
runner-less Grok task, so `AgentTaskDispatcher.TryFailGrokCredentialProbeAsync` (CARD-0324) ran
`GrokCredentialStore.ResolveGrokHome(env)` -> launch env (none) -> process GROK_HOME -> `~/.grok`, i.e. the host
user's real store. server2's runner process has a signed-in GROK_HOME, so Linux was green; the Windows desktop
has no Grok login by design, so it failed `ProviderSignInRequired` (FailureCode AuthenticationRequired).

## Fix: seam used
The established seam (used by ~12 other dispatch fixtures: GrokDelegateDispatchTests, CodexDelegateDispatchTests,
PinnedAgentKindTests, PostLandMutationWorktreeTests, ...): `GrokCredentialProbeEnabled = false`.
`CreateDispatcher` gains `string? grokHome = null`: null -> probe off (all existing callers); non-null -> probe on,
with the grok definition Env `GROK_HOME=<grokHome>` and `GROK_AUTH_PATH=<grokHome>/auth.json` (launch env wins over
process env in ResolveGrokHome/ResolveAuthPath, so a host GROK_AUTH_PATH cannot leak in).
New witness `C772_Desktop_grok_probe_reads_only_its_own_store`: same desktop Grok dispatch, probe on:
empty temp store -> Failed, AuthenticationRequired, reason contains ProviderSignInRequired and the temp auth.json path,
no session, no launch spec; synthetic signed-in store (`{"c772":{"key":"synthetic"}}`) -> Dispatched, one spec.
No credential was read or printed (only an empty scratch dir was used as GROK_HOME).

## Evidence (server2/Linux, all via build-slot)
- RED at base 028fcf8c8 with GROK_HOME=<empty dir>: C772 1/1 failed, same ProviderSignInRequired text as Windows.
- GREEN at 9c438cd7, empty GROK_HOME: TaskPlatformDispatchTests 12 + GrokCredentialProbeDispatcherTests 3 +
  ProviderSignInRequiredCreateTests 2 + GrokSignInIncidentTests 1 = 18/18, 0 skipped (TRX class counts checked).
  Host GROK_HOME: TaskPlatformDispatchTests 12/12.
- Mutant 1 (fixture fake removed: `GrokCredentialProbeEnabled = true`): C772 RED (ProviderSignInRequired), witness green. Restored.
- Mutant 2 (production refusal removed: `if (!IsLaunchBlocking(finding))` -> always-true in
  AgentTaskDispatcher.TryFailGrokCredentialProbeAsync): witness RED (Status should be Failed) and
  GrokCredentialProbeDispatcherTests.Registry_Grok_with_an_Absent_store_fails_before_spawn RED. Restored.
- Restored tip rebuilt and re-run: 18/18 (empty store), 12/12 (host store).
- land graph -Check: approved, count=69, digest D92450D9AABCBF9C783B506F73DF06C45635F415D03E65C5F1A97AF415CC36BE (evaluator built via build-slot; no -Update).
- check-evidence-diff 028fcf8c8..HEAD: commits=1 violations=0.
- merge-tree --write-tree: clean onto origin/master and origin/rc/2026-11-01 (both 028fcf8c8).

## Scan for other host-credential-dependent tests
Static: 113 Antiphon.Tests classes that use a Grok kind and the dispatcher/AgentTaskService/AgentSessionService.
Dynamic: all 113 run at 9c438cd7 with GROK_HOME=<empty dir>, --maximum-parallel-tests 4:
2223 total, 2187 passed, 29 skipped (Windows-only ConPTY), 7 failed; NONE sign-in related:
- NamedCodexAgentLaunchTests x6: `Agent executable 'cmd.exe' was not found on PATH`: Windows-shaped fixture,
  Linux-red by construction, independent of GROK_HOME (still red alone with host env); not touched.
- ModelAvailabilityCreateTests.Create_Grok_succeeds_while_fable_is_held: `grok-4.7 is disabled ... (session-limit)`
  under the 113-class co-run only; class green alone (11/11): a co-run isolation flake, not credentials.
Codex/Claude: the dispatcher's Claude probe and AgentTaskService's Codex probe are runner-bound RPCs
(`ClaudeAuthProbeEnabledFor(runnerId)`, `CodexAuthProbeEnabled` via GetProviderAuthAsync) answered by the fake
peer; no desktop-path host-store read exists for them. The create-time Grok check
(AgentTaskService.RefuseUnauthenticatedGrokAsync) also reads the host store for runner-less Grok, but no scanned
class reached it unfaked. So C772 was the only test found depending on the host Grok login; any future
runner-less Grok dispatch/create fixture with the probe on and no GROK_HOME would fail on the Windows desktop.

## Native rerun
Not run on Windows from here (desktop unreachable; Windows Full run c8a7bcee is live). The next Windows run's
antiphon/all-group-68 re-tests it. Rerun locally: build Antiphon.Tests, then
`GROK_HOME=<empty dir> dotnet <bin>/Antiphon.Tests.dll --treenode-filter "/*/*/TaskPlatformDispatchTests/*"`.

--- next stage ---
next: fast-forward master and rc/2026-11-01 (test-only change; no product code; no landing performed here)
handoff: feat/card-task-f09d003e @ 9c438cd794a9d194d1a2e527467f96bd93edfd83 merges clean onto both; next Windows Full run re-tests group 68
artifact: .antiphon/task-f09d003e.md
