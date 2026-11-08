# CARD-1790 private XDG_CACHE_HOME

Each test host and the session runner installs one process-private cache directory under `Path.GetTempPath()` and exports it as `XDG_CACHE_HOME` before `Main`. pwsh children inherit that variable. The process that created the directory deletes it on exit. A second copy of the installer in the same process reuses the directory and does not delete it. An inherited value is replaced unless this process owns it: absent, empty, and a foreign directory each get a new directory.

`tests/Antiphon.ScriptHarnessHost/Program.cs` answers `ANTIPHON_PWSH_CACHE_PROBE` from `Main` and releases that directory before the linux-owner supervisor signals its own process group. Exiting from the module initializer deadlocks the runtime on Linux. The existing Windows and Linux branches are unchanged.

`Directory.Build.targets` adds the installer as a linked Compile item to every project whose directory is directly under `tests/`; all 23 such projects are test hosts. The condition reads only `MSBuildProjectDirectory` and `MSBuildThisFileDirectory` and calls only `System.IO.Path::GetDirectoryName`, so it defines no new MSBuild property and passes the land-graph pre-check and evaluator. The session runner adds the same item in its own project file.

The pwsh 7.5.4 exit-134 abort is not forced. Nothing else in startup, limits, or spawn arguments changes. `tests/test-execution-policy.json` is unchanged.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---|---|
| CP-1 | S1 | tests/Antiphon.Tests -> bin-c1790-a/ | pwsh-cache | /*/Antiphon.Tests.Scripts/PwshPrivateCacheTests/* | S1 | 3 executed, 0 failed | 3 | 4 |
| CP-2 | S1 | CP-1 | guards | /*/*/(TestClassificationPolicyTests*)\|(TestClassificationGuardTests*)\|(SlowTestRegistryMergeTests*)\|(SlowTestTripwireTests*)\|(NativeLinuxPolicyWitnessTests*)\|(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(C1729LayeringGuardTests*)/* | S1 | all listed (75 at f0e9ea9d31), 0 failed | 60 | 8 |
| CP-3 | S1 | CP-1 | land-graph | /*/Antiphon.Tests.Application/LandGraphEvaluationTests/* | S1 | all executed (147 at 7a9b3a864a), 0 failed | 140 | 15 |

CP-1: the child cache is under the temp root and owned by this process; absent, empty, and foreign inputs each get a distinct directory that is gone after the host exits; two concurrent hosts differ. CP-2: one batched guard run; policy, mechanical contract, platform guidance, and server layering stay green. CP-3: the land-graph evaluator suite stays green with the installer item in `Directory.Build.targets`.
