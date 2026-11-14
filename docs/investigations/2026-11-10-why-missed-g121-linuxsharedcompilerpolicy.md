# Why missed: g121 LinuxSharedCompilerPolicyTests pinned a Linux path separator

rc qualification run 34d41b7e, rc/2026-11-20 = b559bfba2e9a420495122d27171352fe9b7e1ba4,
chunk antiphon-all-group-121 (Windows native desktop):
`LinuxSharedCompilerPolicyTests.Only_compiling_commands_receive_toolset_properties`
failed in 0.036 s. Classification: TEST FIXTURE DEFECT (a portable test with a Linux-only
expectation). It is not a Windows product defect, not timing, and not an expected Windows skip.
Deterministic: reproduced natively on Windows at 8cec1e74e (1 failed, 7 passed of 8).

## Cause

The last assertion of the test expects the literal string
`-p:RoslynTasksAssembly=/cache/tools/Microsoft.Build.Tasks.CodeAnalysis.dll`.
`LinuxSharedCompilerPolicy.Arguments` (`scripts/lib/BuildDriverEnvironment.cs`) builds that
value with `Path.Combine(toolset, "Microsoft.Build.Tasks.CodeAnalysis.dll")`, which on
Windows joins with `\`:

```
should be  [..., "-p:RoslynTasksAssembly=/cache/tools/Microsoft.Build.Tasks.CodeAnalysis.dll", ...]
but was    [..., "-p:RoslynTasksAssembly=/cache/tools\Microsoft.Build.Tasks.CodeAnalysis.dll", ...]
```

The product is correct on every platform where it runs. `LinuxSharedCompilerPolicy.Enabled`
returns false for Windows, and `BuildDriverEnvironment.Apply` returns before
`LinuxCompilerToolset.Prepare`, the only production caller of `Arguments`. The policy class
is pure. It is compiled into the checkpoint tool and Windows PowerShell, and its tests are
bound to that assembly, so they are portable by design (the plan labelled CP-1
`policy-portable`). Marking the class `[NativeLinuxOnly]` would discard seven passing
Windows witnesses, including `Windows_and_optout_preserve_existing_policy`, which exists to
prove the Windows path. The repair expects the host join
(`Path.Combine("/cache/tools", ...)`), which on Linux is byte-identical to the old literal, so
the Linux contract is unchanged.

Passing siblings on Windows (same run, same build): `Windows_and_optout_preserve_existing_policy`,
`Sparse_affinity_is_intersected_with_grant`, `Cache_and_pipe_identity_are_stable_across_worktrees`
(asserts only element `[1]`, `RoslynTargetsPath`, which has no `Path.Combine`),
`Conflicting_toolset_is_refused`, `Untrusted_or_incomplete_cache_is_refused`,
`Compiler_limits_do_not_leak_to_driver_environment`, `Default_enablement_requires_qualified_policy`.

## Introducing commit/card

introducing commit/card: ef43415b220607b844d4ed6726092fdfa61ff501 (CARD-1885) wrote the
literal expectation. 55106df0303914c07b151aaf09e4e80f8e2f97f8 (CARD-1885) implemented
`Arguments` with `Path.Combine`. Landed on master through Merge batch 211 (task a0dbfa79,
master 6f5ce63cb209b856d757691c08426f6fad3dfab5).

## Approving reviews

approving review(s) with scope (Full/Interim) and the selection they actually ran:

- fe5b7764 (Grok, server2 Linux), scope Full, selection the plan's closed CP rows on Linux at
  2c801bfca: CP-1 `/*/*/LinuxSharedCompilerPolicyTests/*` (8 passed), CP-2
  `BuildSlotScriptTests|RunCheckpointScriptTests` (44 passed, 2 Windows skips), CP-3 checkpoint
  receipt classes (45 passed), CP-5..7 scripts. It recorded Windows CP-8/CP-9 as owed, not a
  hold on the land.
- Merge batch 211 a0dbfa79 (ClaudeCode, server2 Linux): final-tree checks on Linux only.

## Outside the selection

why the failing test was outside that selection (lane/category/path): the class was inside the
selection but only on Linux. Lane Unit, category `Unit`, path
`tests/Antiphon.Tests/Infrastructure/LinuxSharedCompilerPolicyTests.cs`. The plan's Windows
rows were CP-8 (`BuildSlotScriptTests` only) and CP-9 (the PowerShell policy script). Neither
selects the portable CP-1 class. Both were still owed when the card landed. On Linux,
`Path.Combine` uses `/`, so the literal expectation could not fail there. No Windows execution
of the class happened before the rc run.

## Cheapest pre-land check

the cheapest pre-land check as a path-glob -> class-filter entry for the impacted-classes table of CARD-1782:

`scripts/lib/BuildDriverEnvironment.cs`, `tests/Antiphon.Tests/Infrastructure/LinuxSharedCompilerPolicyTests.cs`
-> `/*/*/(LinuxSharedCompilerPolicyTests*)/*` with `"windows": "required"`

Added as kind `linux-shared-compiler-policy` in `tests/impacted-classes.json`. The native
Windows run of the class takes about 6 s after the build.

## Exemption

none
