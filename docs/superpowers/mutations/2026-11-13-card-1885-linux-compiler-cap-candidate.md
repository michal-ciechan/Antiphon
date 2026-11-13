# CARD-1885 unbound Mutation candidate

Original Code/landing owner: `58c3a29f-14c8-499b-8c63-994820fb4264`.
Plan: ../plans/2026-11-13-card-1885-linux-compiler-cap.md.
SourceLanding binding belongs to the caller. All controls remain pending;
ordinary red/green, native correctness and performance samples discharge none.

| ID/variant | Guarded behavior | Deliberate mutant | Exact selection |
|---|---|---|---|
| PC-1/targets | Both global Roslyn overrides reach literal argv | Drop RoslynTargetsPath | `/*/*/LinuxSharedCompilerPolicyTests/Only_compiling_commands_receive_toolset_properties` |
| PC-1/tasks | Both global Roslyn overrides reach literal argv | Drop RoslynTasksAssembly | `/*/*/LinuxSharedCompilerPolicyTests/Only_compiling_commands_receive_toolset_properties` |
| PC-2 | Sparse allowed CPUs intersect the grant | Ignore the actual allowed set | `/*/*/LinuxSharedCompilerPolicyTests/Sparse_affinity_is_intersected_with_grant` |
| PC-3 | Worktrees share one SDK/policy generation | Include checkout path in identity | `/*/*/LinuxSharedCompilerPolicyTests/Cache_and_pipe_identity_are_stable_across_worktrees` |
| PC-4 | Limits belong only to compiler child | Export compiler GC/count into driver environment | `/*/*/LinuxSharedCompilerPolicyTests/Compiler_limits_do_not_leak_to_driver_environment` |
| PC-5/Windows | Windows keeps its original policy | Bypass Windows guard | `/*/*/LinuxSharedCompilerPolicyTests/Windows_and_optout_preserve_existing_policy` |
| PC-5/opt-out | Explicit rollback preserves stock behavior | Bypass exact opt-in/opt-out guard | `/*/*/LinuxSharedCompilerPolicyTests/Windows_and_optout_preserve_existing_policy` |
| PC-native/affinity | All server threads stay within the chosen set | Remove taskset only from the owned fixture launcher after preparation | `pwsh -NoProfile -File scripts/test-linux-shared-compiler.ps1 -Case Native`, named case `Test-C1885Native` |
| PC-native/idle | Fixture compiler exits by its idle allowance | Remove the idle argument only from that fixture launcher | Same exact Native case |

Native rows are script cases, not TUnit methods. Mutation must bind their command
scope explicitly rather than inventing a TUnit filter. No normal cache, SDK file,
running compiler, runner or service may be changed. The fixture identity is private
and its launcher is selected before the one build starts. Preserve exact fixture
restoration and owned-pipe shutdown evidence.

Mutation owns red/restore/fresh-green cycles and missing-control discovery, including
fail-open utility/cache/startability, request-log server identity, command grant
transport and checkpoint provenance. An earlier assertion, fixture/build failure,
zero test count, self-comparison or constant return is not the intended red proof.
