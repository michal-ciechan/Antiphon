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
