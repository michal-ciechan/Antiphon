# CARD-0476 Mutation — 56 positive controls

Target: `afdabd320f17e6c9f0eacecc03456f57a8b34a65` on `feat/card-task-6630ad4e`
(worktree `C:\Antiphon\worktrees\card-task-1ebe2253`, detached at that commit).
Plan: `docs/superpowers/plans/2026-09-12-card-0476-lazy-db-preflight-cache-plan.md` §Positive controls.

## Method

Every PC was run as its own red-then-green cycle — **no batching**. For each:

1. apply the mutation to exactly one source file,
2. `dotnet build tests/<Proj>/<Proj>.csproj --property:OutputPath=bin-c476pc/` (forward slash),
3. run only the named method: `bin-c476pc/<Proj>.exe --treenode-filter "/*/*/<Class>/<Method>"`,
4. confirm which `[Arguments]` row failed and at which source line,
5. restore the file from a pristine byte-for-byte backup, `cmp` it against that backup
   (`RESTORED-IDENTICAL` in every cycle), refresh its timestamp, rebuild, re-run the same
   filter green,
6. record the built test-assembly **MVID** for the red build and the green build.

Deterministic builds mean the pristine green MVID is constant across all cycles:

* `Antiphon.SessionRunner.Tests` green MVID = `3e8ad184-0188-403f-b3b0-006fc0f47e69`
* `Antiphon.Tests` green MVID = see the Antiphon.Tests section below

Every red MVID below differs from the corresponding green MVID.

Working tree was verified clean (`git status --porcelain` empty) after the SessionRunner block
and at the end of the run. No mutation was left in place.

## Results — `Antiphon.SessionRunner.Tests` (PC-22…PC-54)

Green run of every method listed: 0 failed, MVID `3e8ad184`.

| PC | Guard | Mutation | Named row | Red at | Verdict |
|----|-------|----------|-----------|--------|---------|
| PC-22 | G-22 key includes shell path | drop `shell.NormalizedPath` from `BuildKey` | `Each_key_dimension_invalidates_independently(shell-path)` | `CacheTests.cs:102` `Invocations.ShouldBe(2)` | RED as specified (1/13 failed), red mvid `967a6858` |
| PC-23 | G-23 exe SHA-256 | drop `shell.ExeSha256` | `(shell-exe-sha)` | `:102` | RED as specified (1/13), `6ceb99ee` |
| PC-24 | G-24 shell version | drop `shell.Version` | `(shell-version)` | `:102` | RED as specified (1/13), `1333c9ac` |
| PC-25 | G-25 engine fingerprint | drop `shell.EngineSha256` | `(shell-engine-sha)` | `:102` | RED as specified (1/13), `36f04d35` |
| PC-26 | G-26 entry bytes | hash `EntryPath` **string** instead of its bytes | `(entry)` | `:102` | RED as specified (5/13 — see note A), `0e801db3` |
| PC-27 | G-27 helper bytes | drop `snapshot.Helper` | `(helper)` | `:102` | RED as specified (1/13), `705c2e23` |
| PC-28 | G-28 platform bytes | drop `snapshot.Platform` | `(platform)` | `:102` | RED as specified (1/13), `c1e66a62` |
| PC-29 | G-29 wrapper bytes | drop `snapshot.Wrapper` | `(wrapper)` | `:102` | RED as specified (1/13), `7b883d66` |
| PC-30 | G-30 validator identity | replace `validatorIdentity` with a constant | `(validator)` | `:102` | RED as specified (1/13), `95432c6f` |
| PC-31 | G-31 no newline/BOM normalization | strip BOM + CRLF→LF before hashing inputs | `(entry-crlf-to-lf)` | `:102` | RED as specified (2/13 — `entry-bom` also, same mutation), `8bcaa82e` |
| PC-32 | G-32 key only on exit 0 | `_keys.Add(key)` before the exit-code check | `Failed_thrown_…grant_no_approval(exit-1)` | `CacheTests.cs:145` `Should.ThrowAsync<PreflightRefusalException>` | RED, **different assertion** than planned — see note B, `316cc183` |
| PC-33 | G-33 exception/timeout/cancel grant nothing | `catch { _keys.Add(key); throw; }` | `(throw)` | `CacheTests.cs:148` `Should.ThrowAsync<InvalidOperationException>` | RED, **different assertion** + see note C, `e69c3af0` |
| PC-34 | G-34 bound inputs still valid at publication | delete the post-validation snapshot compare | `Changed_input_between_validation_and_publication_is_refused` | `CacheTests.cs:223` typed refusal | RED as specified, `519a4444` |
| PC-35 | G-35 every accepted Run launches | return the previous `RestartResult` on a hit | `Same_content_in_two_roots…(pwsh.exe)` | `SafetyTests.cs:29` `expired.Outcome.ShouldBe("wait-expired")` | RED, **one assertion earlier** than planned (`:32`), `18fd2f4a` |
| PC-36 | G-36 inputs hashed at every Run | memoize the per-path input hash | `Safe_same_length_edit_with_restored_timestamp_revalidates(pwsh.exe)` | `SafetyTests.cs:265` `ValidatorChildStarts.ShouldBe(2)` | RED as specified, `92319728` |
| PC-37 | G-37 write/delete-denying handles | open bound inputs `FileShare.ReadWrite \| Delete` | `Bound_inputs_deny_writes_until_the_child_exits(pwsh.exe)` | `SafetyTests.cs:233` first `Should.Throw<IOException>` | RED as specified, `1c179961` |
| PC-38 | G-38 unheldable handle fails first | swallow the handle-open `IOException` | `(pre-held-write-handle)` | `SafetyTests.cs:224` typed refusal | RED, **one assertion earlier** than planned (`:226`), `657106c1` |
| PC-39 | G-39 pre-launch identity recheck | delete the recheck | `Shell_identity_drift_before_launch_refuses_execution` | `CacheTests.cs:325` typed refusal | RED, **one assertion earlier** than planned (`:332`), `972b9a99` |
| PC-40 | G-40 never substitute the other shell | fall back to `powershell.exe` | `Resolver_picks…never_substitutes(other-shell-only)` | `CacheTests.cs:290` `Should.Throw<ShellResolutionException>` | RED as specified, `be5c67ef` |
| PC-41 | G-41 parser errors rejected | `[ref]$null` for parse errors, drop the count checks | `Malformed_syntax…(pwsh.exe, parser-error)` | `SafetyTests.cs:167` `Should.ThrowAsync` | RED as specified (also the `powershell.exe` row), `318da7f9` |
| PC-42 | G-42 missing core rejected | drop the throw, guard the core loop with `if($core)` | `(pwsh.exe, missing-core)` | `SafetyTests.cs:167` | RED as specified (+ powershell row), `0a4607aa` |
| PC-43 | G-43 non-inert helper import rejected | delete the inert-import loop | `(pwsh.exe, non-inert-helper)` | `SafetyTests.cs:167` | RED as specified (+ powershell row), `92d26beb` |
| PC-44 | G-44 entry command allowlist | add `Remove-Item` to the entry allowlist | `Warm_cache_rejects_a_tampered_entry…(pwsh.exe, forbidden-command)` | `SafetyTests.cs:122` `Should.ThrowAsync` | RED as specified (+ powershell row), `dc36cec9` |
| PC-45 | G-45 unchecked member calls | delete the `InvokeMemberExpressionAst` loop | `(pwsh.exe, member-call)` | `SafetyTests.cs:122` | RED as specified (+ powershell row), `4e704f0d` |
| PC-46 | G-46 unchecked dynamic commands | delete the `-not $name` branch | `(pwsh.exe, dynamic-command)` | `SafetyTests.cs:122` | RED as specified (+ powershell row), `79debe17` |
| PC-47 | G-47 core platform allowlist | delete the core command loop | `(pwsh.exe, core-bypass)` | `SafetyTests.cs:167` | RED as specified (+ powershell row), `83b59786` |
| PC-48 | G-48 wrapper contract before any import | delete the wrapper byte comparison | `Wrapper_tampering…(wrapper-absolute-import)` | `SafetyTests.cs:207` `ex.Message.ShouldContain("wrapper contract")` | RED, **different assertion** — see note D, `08f80d78` |
| PC-49 | G-49 fixture runs its own snapshot | wrapper dot-sources the repo helper by absolute path | `Fixture_executes_its_copied_helper_not_the_repository` | `SafetyTests.cs:280` `File.Exists(marker).ShouldBeTrue()` | RED as specified, `70b8fc6b` |
| PC-50 | G-50 gate released before execution | hold the async gate across the execute callback | `Concurrent_same_key_callers_share_one_validation_and_run_separately` | `CacheTests.cs:194` 5 s nested bound | RED as specified (22.4 s, deadlock), `d163712d` |
| PC-51 | G-51 concurrent misses validate once | remove the gate | same method | `CacheTests.cs:201` `Invocations.ShouldBe(1)` | RED as specified, `95d12f59` |
| PC-52 | G-52 Script/decoder never cached | memoize `Script` on identical driver text | `Script_and_decoder_launch_a_child_every_call` | `SafetyTests.cs:297` `ScriptChildStarts.ShouldBe(5)` | RED as specified, `18071152` |
| PC-53 | G-53 owned-child timeout/kill | plain `WaitForExitAsync()` | `Hung_child_is_killed_at_the_fixture_deadline` | `SafetyTests.cs:309` `Should.ThrowAsync<TimeoutException>` | RED as specified (12.7 s — the 10 s sleep ran to completion), `1a53de43` |
| PC-54 | G-54 every accepted Run writes current config | write `config.json` only on a miss | `Same_content_in_two_roots…(pwsh.exe)` | `SafetyTests.cs:28` (the `b.Run(...)` call throws `final.Length`) | RED, **one line earlier** than planned (`:29`), `53ead652` |

### Notes

**A (PC-26).** Hashing the entry *path* instead of its bytes made five rows red, not one:
`entry`, `entry-same-length-restored-timestamp`, `entry-crlf-to-lf`, `entry-bom` (all at
`:102`) and `root-only` at `:93`. All five are direct consequences of the same mutation, and
the `root-only` failure is extra evidence that the real key is genuinely root-independent.

**B (PC-32).** The mutation is caught, but the first failing assertion is the *second-stage*
`Should.ThrowAsync<PreflightRefusalException>` at `:145`, not `Invocations.ShouldBe(2)` at
`:166`. That is unavoidable: once an exit-1 validation caches its key, the second call becomes
a cache hit and stops throwing, so the refusal assertion necessarily fires before the
invocation count is ever read. The plan's named assertion is not reachable for this mutation.

**C (PC-33).** Same shape: red at `:148`. Two extra effects worth recording — the `timeout`
row also went red (`:152`), and the `canceled` row **hangs forever**: its `while
(Invocations == 1) await Task.Delay(10)` spin never advances because the second approval is
served from the poisoned cache and never reaches the validator. The run had to be killed at
180 s. That is a genuine detection (the test cannot pass), but it is a hang rather than an
assertion failure, so the row is reported here explicitly rather than as a clean red.

**D (PC-48).** Deleting the wrapper contract comparison did not merely let a bad wrapper
through — the `wrapper-absolute-import` case then **hung the execution child for the full 45 s
fixture deadline** and surfaced as `TimeoutException: The operation has timed out.` at `:207`.
`ValidatorChildStarts.ShouldBe(0)` at `:210` was never reached (a validator child had in fact
started). The companion row `wrapper-extra-line` went red at `:205`.

## Results — `Antiphon.Tests` (PC-1…PC-21, PC-55, PC-56)

Pristine green MVID for `Antiphon.Tests` = `240b9167-8242-4ea9-a887-a604e4b0c6ae`
(whole `TestDbFixtureLifecycleTests` class: 29/29 pass).

### Controlled-ops block (Unit, `TestDbFixtureLifecycleTests`)

| PC | Guard | Mutation | Named row | Red at | Verdict |
|----|-------|----------|-----------|--------|---------|
| PC-2 | G-2 gate creates the task once | `_task ??=` to `_task =` | `Eight_concurrent_first_callers_share_one_bootstrap` | `LifecycleTests.cs:144` `Create.ShouldBe(1)` | RED as specified, red mvid `4cf359a2` |
| PC-3 | G-3 no init after disposal | delete the disposed check in the gate | `Teardown_matrix(access-after)` | `:310` `Should.Throw<ObjectDisposedException>` | RED as specified (1/7), `93f8798f` |
| PC-4 | G-4 bootstrap off the captured context | `Task.Factory.StartNew(..., FromCurrentSynchronizationContext())` | `Synchronous_access_completes_under_a_single_threaded_synchronization_context` | `:229` 30 s `WaitAsync` | RED as specified (30.0 s), `2970ce5d` |
| PC-5 | G-5 readiness only after template protection | publish `ready`, run `ProtectTemplateAsync` fire-and-forget | `Readiness_waits_for_template_protection(before-ALLOW_CONNECTIONS)` | `:202` `sync.IsCompleted.ShouldBeFalse()` | RED as specified (2/4 — `after-IS_TEMPLATE` too), `af9be072` |
| PC-6 | G-6 bootstrap never calls the lazy accessors | `_ = CreateDbContextOptions();` inside the bootstrap | `Eight_concurrent_first_callers_share_one_bootstrap` | `:142` 30 s bound (self-wait) | RED as specified (30.4 s), `9149a06b` |
| **PC-7** | G-7 explicit connection strings bypass init | `_ = Lifecycle.ConnectionString;` at the top of `TestDbFixture.CreateDbContextOptions(string?)` | `Explicit_options_never_initialize` | — | **DOES NOT REPRODUCE** — see finding 1 |
| **PC-8** | G-8 a bootstrap fault never enters the drop path | move the readiness await inside the `try` after the clone name | `Clone_request_on_a_faulted_lifecycle_does_not_drop` | — | **DOES NOT REPRODUCE** — see finding 2 |
| PC-9 | G-9 pool clearing scoped to the shared store | add `_ops.ClearAllPools();` to the bootstrap | `Bootstrap_clears_only_the_shared_pool` | `:403` `ops.ClearAll.ShouldBe(0)` | RED as specified, `881fb151` |
| PC-10 | G-10 never-requested teardown is a no-op | teardown creates the bootstrap task before disposing | `Teardown_matrix(never-requested)` | `:265` `Create.ShouldBe(0)` | RED as specified (1/7), `122d51e5` |
| PC-11 | G-11 in-flight teardown awaits then disposes | `if (!init.IsCompleted) return;` in `DisposeCoreAsync` | `Teardown_matrix(starting)` | `:277` `teardown.IsCompleted.ShouldBeFalse()` | RED, **one assertion earlier** than planned (`:282` `DisposeOwned.ShouldBe(1)`), `11074c2b` |
| PC-12 | G-12 repeated teardown shares one disposal | delete the `_disposeTask` short-circuit | `Teardown_matrix(repeated)` | `:304` `DisposeOwned.ShouldBe(1)` | RED as specified (1/7), `d1603be2` |
| PC-13 | G-13 fault is terminal, no retry | `lock (_gate) _task = null;` in the fault path | `Bootstrap_fault_is_terminal_for_all_waiters(migrate)` | `:350` `Create.ShouldBe(created)` | RED as specified (all 5 rows), `569ee465` |
| PC-14 | G-14 partial container disposed once | delete the owned-container disposal from the catch | `(start)` | `:354` `DisposeOwned.ShouldBe(1)` | RED as specified (4/5 rows; `construct` correctly stays green), `76bd22b6` |
| PC-15 | G-15 cleanup attached, never substituted | `throw cleanup;` instead of `AggregateException(ex, cleanup)` | `(protect-and-cleanup-fails)` | `:343` C476-FAULT sentinel | RED as specified (1/5), `8668fda1` |
| PC-16 | G-16 clone error preserved | `throw dropEx;` instead of `AggregateException(ex, dropEx)` | `Clone_creation_failure_drops_once_and_preserves_the_clone_error(drop-fails)` | `:390` C476-CLONE sentinel | RED as specified (1/2), `815596d2` |
| **PC-21** | G-21 clone disposal reads ready state | `DropClonedDatabaseAsync` calls `EnsureReadyAsync()` | `Teardown_matrix(clone-dispose-after)` | — | **DOES NOT REPRODUCE** — see finding 3 |

### Findings

**Finding 1 — PC-7 is mapped to the wrong test; the guard itself holds.**
`Explicit_options_never_initialize` never calls `TestDbFixture.CreateDbContextOptions(string?)`.
It calls the *static* `TestDbFixtureLifecycle.BuildOptions` and asserts on its own private
`TestDbFixtureLifecycle` instance, which a mutation to the `TestDbFixture.Lifecycle` singleton
cannot touch. With the mutation applied the named test passed 1/1 (mvid `3ace8f78`).
The defect **is** caught elsewhere: with the same mutation still applied,
`TestDbFixtureLazyInitializationTests.A_db_free_exact_method_selection_constructs_and_starts_no_database`
went red — child assertion `TestDbFixture.Lifecycle.IsRequested.ShouldBeFalse()`
(`LazyInitializationTests.cs:171`), surfacing in the parent at `:26` `run.Exit.ShouldBe(0)`,
after the child had started a real container (44.7 s). So G-7 is guarded; the PC row names
the wrong method. Plan amendment only, no code change needed.

**Finding 2 — PC-8 is a real coverage hole.**
Moving the readiness await inside the `try` does put a faulted bootstrap on the drop path, but
`DropClonedDatabaseAsync` throws `ObjectDisposedException` on the null `_ready` *before*
`Interlocked.Increment(ref Drop)`, so the `Drop` counter — the only thing the test observes —
never moves, and the clone error still flattens to `C476-FAULT`. Evidence, all with the
mutation applied (mvid `865648d7`):

* named test `Clone_request_on_a_faulted_lifecycle_does_not_drop`: 1/1 **passed**
* whole `TestDbFixtureLifecycleTests` class: **29/29 passed**
* `TestDbFixtureLazyInitializationTests.A_post_start_fault_cleans_up_the_owned_container`
  (whose child asserts `Lifecycle.Drop.ShouldBe(0)` against a real faulted bootstrap): **passed**

G-8 is therefore asserted only through a counter that a second safety net (the `_ready` null
check) keeps at zero. A test that asserts the *structure* — that `_ops.DropAsync` is never
reached, or that the thrown exception is the bare fault rather than an `AggregateException`
carrying an `ObjectDisposedException` — would close it.

**Finding 3 — PC-21 cannot reproduce while G-3 holds.**
Making `DropClonedDatabaseAsync` call `EnsureReadyAsync()` cannot produce "a second create"
in `Teardown_matrix(clone-dispose-after)`: the lifecycle is already disposed, so
`EnsureReadyAsync` hits the G-3 disposed check and throws the very `ObjectDisposedException`
the row expects, leaving `Create` unchanged. Evidence with the mutation applied
(mvid `a032848e`): named method 7/7 **passed**, whole class **29/29 passed**.
The genuinely dangerous case for G-21 — dropping a clone against a *never-requested*
lifecycle, which would start a container — is unreachable from the test surface (you cannot
hold a clone without having initialised). The guard is real but PC-21 as written proves
nothing about it. Recommend rewording PC-21/G-21 to assert that `DropClonedDatabaseAsync`
observes the ready state directly (e.g. a faulted lifecycle must surface
`ObjectDisposedException`, not the bootstrap fault) rather than counting creates.
