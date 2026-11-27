# Why Windows rc run 419ed669 timed out after C849 passed

Windows Full qualification `419ed669` at `5c0883bf203d122fdc7d4556e79087af2a683669`
recorded one passed result in `antiphon-all-profile-2353`:
`RemoteScriptContractTests.C849_Refused_launch_keeps_a_foreign_container_and_signals_release_proven_cids`.
The TRX records 42.321 s for the method, the host reports 46.105 s, and the chunk ran
107.576 s. `execution-stop-to-exit-seconds=0.147` measures the diagnostic file's last
write to root exit. The chunk's timing reason and hang-capture path bind the failure to
the 60 s post-completion grace in `NightlyCompletionGrace`. The Antiphon launch
watchdog at this pin gives an uncalibrated chunk 3,600 s. The 524.597 s prerequisite
build ran separately. This is a single sample, so it supplies no p95 for a grace change.
The method and its shell fixture were already present at the `1fe763a9` pin. That
run's method timing is unavailable here.

The job owner waits for both root exit and an empty job. The summary records four
`wsl` images, two `wslhost` images and three `conhost` images seen in that job.
The summary's list includes previously observed descendants. The native
`.hang.txt` is needed to identify the exact still-live members at termination. The
C849 method runs the Linux PowerShell availability probe and the C1192
`slot-round2` script through the Windows WSL helper. That helper started a redirected
`wsl.exe`, waited for the root, and killed a process tree only on root timeout. A
descendant that outlived the root stayed in the outer qualification job.

The helper now gives each Windows invocation a private job before the root resumes.
The fixture owner preserves the root exit and output, terminates any remaining
members of that job, and waits for an empty job and drained output. The diagnostic
records launch, execution, cleanup and drain times. Native qualification remains
due for both the C849 case and the private-job descendant witness.

## Introducing commit/card

`0a656b1f885a10f0369225dc8890e0708e89c198` introduced the C849 slot-round2
witness (CARD-0849). `01715d0f990e222ae91af24e032b57d6d108de19` extracted
the existing remote-shell support (CARD-1729) and retained the root-only wait.
The first occurrence of the host-specific WSL leak is unproven.

## Approving reviews

Review `cd8476f1` (Interim) executed 138 Linux cases under
`/*/*/RemoteScriptContractTests/(C849*)|(C1192*)|(C1189*)|(C1191*)|(C1193*)|(C976*)|(C973*)|(C1208*)`.
Review `69fea092` (Interim) ran the same selection with 137 cases and explicitly
recorded the C849 case passing. Those reviews exercised Bash and the synthetic
restore paths on Linux. No native approval receipt for WSL descendant cleanup was
found.

## Outside the selection

The witness is an Integration method in `RemoteScriptContractTests.cs`. Linux
Review included it. The native WSL job boundary only runs on Windows. The existing
`remote-scripts` method selection covers deploy ordering. It has no filter for the
C849 restore/signals witness when the shell helper or C1192 fixture changes.

## Cheapest pre-land check

Map `tests/Antiphon.Tests/TestHelpers/RemoteScriptContractSupport.cs` and
`scripts/fixtures/c1192-workspace-contract.sh` to
`/*/*/RemoteScriptContractTests/C849_Refused_launch_keeps_a_foreign_container_and_signals_release_proven_cids` on Windows.
Map `scripts/lib/Antiphon.Nightly/NativeProcessOwner.cs` to
`/*/*/NightlyNativeOwnershipTests/C1039_DescendantExit` on Windows to cover the existing
qualification owner and the fixture's descendant-reaping path.

## Exemption

None. The earlier session-runner completed-hang investigation describes the
same watchdog symptom for a different process owner. The earlier C1192
race-barrier investigation describes a failing assertion during the test.
