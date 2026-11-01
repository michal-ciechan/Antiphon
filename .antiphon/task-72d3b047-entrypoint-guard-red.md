# CARD-1614 slice A round 3 — entrypoint guard red

Unlisted. Reason: red-first proof that `C1614_Entrypoint_library_guard_continues_unadvertised` fails when step 3b is a plain `source`. The repo entrypoint was not edited. The overlay `/tmp/c1614-bare-source-entrypoint.sh` kept the step 3b and step 4 markers and replaced the guard with `source /usr/local/lib/antiphon-custody-common.sh`. It was not committed.

Command: `dotnet run --no-build --project tests/Antiphon.SessionRunner.Tests --property:OutputPath=bin-c1614-r3-guard/ --property:UseAppHost=false -- --treenode-filter "/*/*/CustodyNamespaceBootstrapTests/C1614_Entrypoint_library_guard_continues_unadvertised"`

Environment: `ANTIPHON_C1614_ENTRYPOINT_OVERLAY=/tmp/c1614-bare-source-entrypoint.sh`

Slot: granted, waited 0s, lease `4a79c196-bac5-4fac-a661-ab9f53e0ed66`.

Result: failed 1, succeeded 0, skipped 0. Driver exit 2. The missing-library case exited 1 before the sentinel:

```
continued.ExitCode should be 0 but was 1
Additional Info: missing entry_rc=1
/tmp/c1614-step3b-9306a085ea2745a79031806b3cbbc2ca.sh: line 6: /usr/local/lib/antiphon-custody-common.sh: No such file or directory
```

CustodyNamespaceBootstrapTests.cs:104. That is the runner-stopping path (entrypoint exit 1, no dockerd). The same harness on a syntax-broken library exits 2; those arms run in the test once the overlay is unset.
