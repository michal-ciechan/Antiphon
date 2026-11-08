# CARD-1790 private XDG_CACHE_HOME

Each test host and the session runner installs one process-private cache directory under `Path.GetTempPath()` and exports it as `XDG_CACHE_HOME` before `Main`. pwsh children inherit that variable. The process that created the directory deletes it on exit. A second copy of the installer in the same process reuses the directory and does not delete it. An inherited value is replaced unless this process owns it: absent, empty, and a foreign directory each get a new directory.

`tests/Antiphon.ScriptHarnessHost/Program.cs` releases that directory before the linux-owner supervisor signals its own process group. The existing Windows and Linux branches are unchanged.

The pwsh 7.5.4 exit-134 abort is not forced. Nothing else in startup, limits, or spawn arguments changes. `tests/test-execution-policy.json` is unchanged.

### Checkpoints

| CP-1 | `PwshPrivateCacheTests*` | Child cache is under the temp root and owned by this process; absent, empty, and foreign inputs each get a distinct directory that is gone after the host exits; two concurrent hosts differ. |
| CP-2 | `TestClassificationPolicy*` `TestClassificationGuard*` `SlowTestRegistryMerge*` `SlowTestTripwire*` `NativeLinuxPolicyWitnessTests*` `MechanicalPcContractTests*` `TaskPlatformGuidanceTests*` `C1729LayeringGuardTests*` | One batched guard run. Policy, mechanical contract, platform guidance, and server layering stay green. |
