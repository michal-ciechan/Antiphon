# Why missed: C1817/C1818 read-only git delete on Windows

One committed note for the second miss of rc chunk session-runner all-group-2. Run `d88de419` on `rc/2026-11-16` = `6f8741d872d63507827014f972ccf71196722d4a` still failed `C1817RunnerRepositoryMaintenanceTests` (5) and `C1818GcOwnerRobustnessTests` (4). The extract is `docs/investigations/2026-11-10-run-d88de419-sr group2 C1817/C1818 still red-windows-failure.md`. CARD-1781.

This is a different cause from `docs/investigations/2026-11-10-why-missed-c1817-c1818-windows.md`. That note reconstructed POSIX tools and a missing commit identity. Those four rows are green on this rerun. The rows that stayed red throw `UnauthorizedAccessException` from `Directory.Delete` of a read-only git object or `commit-graph`.

## Failure

`Delete` in both classes catches only `IOException`. Windows `RemoveDirectoryRecursive` throws `UnauthorizedAccessException` for a read-only file and names the entry (`commit-graph`, or a 38-hex loose object). The attribute-clear retry never runs. `a4ea62dd3bfaa20b20ab8609c9352825b96c9974` does not edit `Delete`. Every method that commits fails in `finally`. Every sibling that calls the same `Delete` with zero commits passes. The `commit-graph` sentence is unchanged from run 6591e378.

## Introducing commit/card

introducing commit/card: `d18bf2e00812a6854744722857176f1ff7eef274` (CARD-1817) and `58454a7a709431c8e974f17328dc75e68a41623f` (CARD-1818)

Those commits added `Delete` with `catch (IOException)` only. The Windows fixture fix `a4ea62dd3bfaa20b20ab8609c9352825b96c9974` (cherry of `6a7eb10efaf42dedd6a9f54d662a286f6370cbd8`) left both helpers in place. Its red witness `15edc83cf014432236112949be34adadeb729a1f` asserts `cmd.exe` and the commit identity. It does not delete a repository that contains a read-only object.

## Approving reviews

approving review(s) with scope (Full/Interim) and the selection they actually ran:

- 211c38d3, scope Full, Linux server2. Selection: SessionRunner.Tests `(C1818*)|(C1817*)|(C1441*)|(TestClassificationGuard*)|(NativeLinuxPolicyWitnessTests*)|(PhoneHomeCommandDispatcherTests*)` 78/78. The report recorded native Windows as read, not run. Quoted from the first note. Linux `Directory.Delete` does not throw on the DOS read-only bit, so this selection stays green with the defect present.
- 5b3c6c26, scope round-2 diff only, Linux server2. Selection: SessionRunner.Tests `(C1818*)|(C1817*)|(C1441*)|(TestClassificationGuard*)|(NativeLinuxPolicyWitnessTests*)|(TestLaneCategoryGuardTests*)` 34/34. Same gap. A-5 named `/bin/sh`, `stat -c`, and `/proc`, not `Delete`.
- No commit on this rc records a review that ran `/*/*/(C1817RunnerRepositoryMaintenanceTests*)|(C1818GcOwnerRobustnessTests*)/*` on Windows after `a4ea62dd3`. `git log --grep="gc fixture"` on this cut shows only `15edc83cf` and `a4ea62dd3`.

The final-result note `7a3c45c3f82e36c7b992d5b4b66316cd52127b47` then listed the seven `UnauthorizedAccessException` rows under "Already fixed on master" because `a4ea62dd3` is an ancestor of master. The rerun list marked `session-runner-all-group-2` expected green. That classification is how the unread delete rows were sent back to Windows as if the POSIX fix covered them.

## Outside the selection

why the failing test was outside that selection (lane/category/path): Windows Full qualification lane, Integration, `tests/Antiphon.SessionRunner.Tests/C1817RunnerRepositoryMaintenanceTests.cs` and `tests/Antiphon.SessionRunner.Tests/C1818GcOwnerRobustnessTests.cs`

The class names were inside the Linux selections. The failure is the Windows read-only attribute on `.git/objects` and on `commit-graph`. A Linux run, and the new `C1817_forced_windows_fixture_uses_no_posix_tool` seam (no repository), cannot see it. The first why-missed note says the Windows TRX was not on the mirror, so the Debug that wrote `a4ea62dd3` did not have these messages in front of it. The final-result note did, and still credited that commit.

## Cheapest pre-land check

the cheapest pre-land check as a path-glob -> class-filter entry for the impacted-classes table of CARD-1782:

`tests/Antiphon.SessionRunner.Tests/C1817*.cs`, `tests/Antiphon.SessionRunner.Tests/C1818*.cs` -> `/*/*/(C1817RunnerRepositoryMaintenanceTests*)|(C1818GcOwnerRobustnessTests*)/*`

Proposed row only (not inserted). `"windows": "required"`. The filter must be the whole classes. `C1817_forced_windows_fixture_uses_no_posix_tool` alone stays green while `Delete` is still wrong. Run it on native Windows. Linux is not a substitute for this cause.

```json
{
  "id": "runner-gc-readonly-delete",
  "globs": [
    "tests/Antiphon.SessionRunner.Tests/C1817*.cs",
    "tests/Antiphon.SessionRunner.Tests/C1818*.cs"
  ],
  "classes": [
    "C1817RunnerRepositoryMaintenanceTests",
    "C1818GcOwnerRobustnessTests"
  ],
  "windows": "required"
}
```

Project is `tests/Antiphon.SessionRunner.Tests`.

## Exemption

same cause as an already written note: none

The earlier note `docs/investigations/2026-11-10-why-missed-c1817-c1818-windows.md` is the POSIX and identity cause. Do not reuse it for these nine rows.
