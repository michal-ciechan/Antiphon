# CARD-1996 helper closure checkpoints

Code owner: task `4a4a5995`. Base: `997f9b8d9ffc4ba870d2fd56caf7c03781591624` (origin/master at start).

When a batch touches `tests/Antiphon.Tests/TestHelpers/**` or a csproj that Compile-includes a file under `tests/Antiphon.Tests`, Merge builds `Antiphon.sln` once (or each linking mirror project once). `LinkedTestHelperClosureTests` reports a namespace-level TestHelpers type whose file name is not the type name. The one-by-one Compile lists stay: a TestHelpers glob would pull a different subset into each mirror.

## Verification design

V-1: At `3083ca555dd684265307c6a71db499afe7c45f50`, `differently_named_helper_file_is_reported` fails because the missing list does not contain `HiddenHelper` in `NotTheTypeName.cs`. The same-name census on the live tree passes in that run (1 succeeded, 1 failed).
V-2: After the census indexes namespace-level TestHelpers types, the synthetic cross-file fixture and the same-name fixture both report the unlinked file, and the live tree still has an empty missing list.
R-1: Docs state the Merge build rule, the brief sentence, and why the Compile lists stay one file at a time. No classification, selector, or pin input changes, so the guard union stays with nightly/rc under the SHORT profile.
R-2: Recompute the three nightly pins, `land-graph -Check`, evidence-diff over the task base..HEAD, and `git merge-tree --write-tree origin/master HEAD`. Build each mirror project that links Antiphon.Tests files, in native output, because this change is under `tests/Antiphon.Tests/TestHelpers`.

Manual M-1: checkpoint receipt at the committed SHA, one build, the exact class filter, nonzero counts.
M-2: rebuild the derived-index driver in native output, regenerate twice, and commit the index only if the first run prints changed.
M-3: no production host, migration, or restart. Mutation is waived (operator 2026-11-12). PC-1 stays pending: delete the top-level TestHelpers index so `differently_named_helper_file_is_reported` is empty again.

## Performance budget

CP-1 is three in-process file scans, no database and no service. The first-row EstimatedMinutes 15 is the cold-build allowance, not a test timeout. One red run at the witness commit, then one green run. No green repetition. Read `/proc/loadavg` before the driver and admit below 16. `ANTIPHON_LINUX_COMPILER_POLICY=0`. Remove `bin-c1996/` by that exact path after receipt inspection.

### Checkpoints

Run through the checkpoint tool with `--expected-source-sha` set to committed HEAD and `--serial`. Wait until the exit is not 75. The tool derives the row deadline from EstimatedMinutes and does not take a TUnit `--timeout` flag; direct `dotnet run` rows pass `--timeout 570s`. `ANTIPHON_LINUX_COMPILER_POLICY=0` is set on the tool process so its build inherits it.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---|---|---|
| CP-1 | code | tests/Antiphon.Tests -> bin-c1996/ | helper-closure | `/*/*/LinkedTestHelperClosureTests/*` | V-1,V-2 | 0 failed | 3 | 15 | true |

The Unit lane stays nightly/rc. Mirror project builds are direct build-slot builds in native output (M-1's sibling under R-2), not extra checkpoint rows: they have no test filter, and a command row must not take a second build slot.
