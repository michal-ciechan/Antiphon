# L5 / Q Linux triage (task 2ee82266)

Status: investigation in progress; no passing qualification claimed.

Baseline: fetched origin/master and assigned branch both at
`e5a867361937752b959c1451824ab9716fac83c0` on Linux x64.
Historical Windows extraction: branch `feat/card-task-b5042266`, report
`docs/investigations/2026-10-25-rc-a779085c-non-antiphon-failures.md`.

## Authorized selection and budget

One isolated SessionRunner.Tests build under scripts/build-slot.ps1 to
`bin-debug-2ee82266/`, then compiled-output runs under the same gate:

- `/*/Antiphon.SessionRunner.Tests/CompletedCardWorkspaceRemovalTests/C1032_RemovalComponentRecovery`
- `/*/Antiphon.SessionRunner.Tests/UnixPtyArgvAdmissionTests/*`
- `/*/Antiphon.SessionRunner.Tests/AgentPinPublicationTests/*`
- `/*/Antiphon.SessionRunner.Tests/AgentPinWorkspaceStoreTests/*`

No Unit lane, full suite, native Windows execution or active qualification action.
Task budget 70 minutes. Build/slot/execution recorded separately; provisional hard
limits: build 12 minutes, slot admission 10 minutes, each selected run 5 minutes.
These are bounded diagnostic samples, not measured p95 or release coverage.
No database/container/web-host fixture is required; test-owned filesystem/Git/worker
setup is part of the measured test duration. Uninstrumented phases remain unknown.
Raw logs/TRX and output inventory live in ignored `.antiphon/evidence/task-2ee82266/`.

## Results

Pending. Platform-exclusion changes will be recommended unless an exact one-line
attribute fix and a trustworthy witness can be established within scope.
