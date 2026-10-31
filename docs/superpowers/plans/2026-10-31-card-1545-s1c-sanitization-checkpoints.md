# CARD-1545 S1c-sanitization checkpoints

Closed list for the sanitizer slice only. CP-20 and CP-26 are the portable caller
witnesses. CP-29 scans their completed artifacts and does not read its own row.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-20 | S1c-sanitization | tests/Antiphon.Agents.Pty.Tests -> bin-live-pty-evidence/ | portable-live-pty-evidence | `/*/Antiphon.Agents.Pty.Tests/LiveClaudeEvidenceWiringTests/*` | V-5, V-6 | EvidenceIsSanitized,CanarySinksCannotBypassSanitizer | 2 | 6 | true |
| CP-26 | S1c-sanitization | tests/Antiphon.Tests -> bin-live-evidence/ | portable-live-c680-evidence | `/*/Antiphon.Tests.Scripts/LiveLaneEvidenceTests/*` | V-7, V-8 | C680EvidenceIsSanitized,ArtifactGateRequiresPresence | 2 | 10 | true |
| CP-29 | S1c-sanitization | n/a | portable-live-completed-artifacts | `pwsh -NoProfile -File scripts/test-live-lane-evidence.ps1 -Case CompletedRows` | V-9 | exit 0; markers per exact test id in TRX and in receipt and public summary; sentinel absent in every completed row file | n/a | 1 | true |
