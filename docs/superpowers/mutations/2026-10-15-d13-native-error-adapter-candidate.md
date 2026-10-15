# D-13 SourceLanding Mutation candidate (unbound)

Original Code / landing owner: `df7c3a1a-8e1f-47da-8e28-b04ad33d3c77`.
Plan: `docs/superpowers/plans/2026-10-15-d13-native-error-adapter-plan.md`.
Commission only after ordinary Review and confirmed publication of the original Code task.
This candidate has no SourceLanding operation or snapshot binding yet.

All controls are pending. Code performed ordinary red-first defect reproduction,
not deliberate mutation, and has not discharged any positive control.

| ID | Production mutation | Expected failing assertion | Method-scoped detecting filter |
|---|---|---|---|
| PC-1 | Remove Error recognition from Get-NightlyDiagnosticStateName | D13_ErrorTerminal rejects missing failed record / unknown diagnostic format | `/*/*/NightlyExecutionDiagnosticTests/D13_ErrorTerminal` |
| PC-2 | Drop the failure Message on ConvertFrom-NightlyDiagnosticRecord | D13 LF failure message / complete failed verdict | `/*/*/NightlyExecutionDiagnosticTests/D13_ErrorTerminal` |
| PC-3 | Remove the duplicate terminal UID refusal in ConvertFrom-NightlyDiagnosticLog | D13 duplicate Error refuses | `/*/*/NightlyExecutionDiagnosticTests/D13_ExecutionGuards` |

Mutation owns deliberate red/restore/green cycles, missing-control discovery and
snapshot restoration evidence. Do not commit or push from the SourceLanding snapshot.
Restart: none.
