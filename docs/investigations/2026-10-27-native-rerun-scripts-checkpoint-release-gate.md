# Native Windows rerun: scripts, checkpoint and release gate

Task: fb80be16. Investigation in progress, 2026-10-27. No product or test source changes authorized or made.

## Provenance and limits

- Assigned branch: `feat/card-task-fb80be16`; initial source `718bd29f4a1504c64c491d607d31403fcb87dae2`.
- Fetched rc reference: `origin/rc/2026-10-27` = `d1d090f766451797a3bd9b30c9d2966f2f8c034c`.
- The assigned branch is not descended from that rc commit. Git-common forbids switching, rebasing or merging the target into the assigned branch. The complete tree comparison differs only in Markdown plus `src/Antiphon.Agents.Pty/ClaudeScreen.cs` and `tests/Antiphon.Agents.Pty.Tests/ClaudeDialogObservationTests.cs`. All requested test classes, checkpoint code, scripts and build configuration match rc exactly. Runs will bind the actual assigned source; they do not qualify the exact rc tree.
- Original qualification evidence (read-only): `C:\Antiphon\releases\candidates\rc-2026-10-25-105792f7d22f\logs\2026-10-26-0946-8f055f87`.
- Fresh generated evidence (gitignored): `C:\Antiphon\worktrees\card-task-fb80be16\.antiphon\native-fb80be16`.
- Initial free memory: 8,935 MiB; no matching Antiphon.Tests/testhost/vstest processes or dotnet test-project drivers. Native qualification lock absent. Installed latest SDK is 10.0.300, not 10.0.402.
- Caller already authorized native individual reruns and investigation; no approval pause is required. No live AppHost/runner restart or release operation is in scope.

## Execution

Pending: one isolated native build through `scripts/build-slot.ps1`, then 34 separate class-filter invocations of the compiled output through the same slot gate. Failures will retain exact errors and stacks; suspected flakes require three repeats. Passing/failed/skipped counts and source witnesses will replace this progress record.

## Not done, noted

Fix design and implementation are outside this Investigate stage; any confirmed mechanism will be handed to Plan.

--- next stage ---
next: investigate
handoff: Complete the individually filtered native reruns and reconstruct remaining Windows failures from stored evidence.
artifact: docs/investigations/2026-10-27-native-rerun-scripts-checkpoint-release-gate.md
