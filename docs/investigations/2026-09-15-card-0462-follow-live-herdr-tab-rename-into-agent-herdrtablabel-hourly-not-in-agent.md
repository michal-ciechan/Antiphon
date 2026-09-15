# CARD-0462 investigation: Follow live Herdr tab rename into Agent herdrTabLabel (hourly, not in-agent)

Date: 2026-09-15. Task: a61aba72. Source inspected: b97abfd83819e2d1b705b4ed0527cfa8953b797f.

Checkpoint: source trace complete; isolated reproduction and final evidence report pending.

The named-launch branch runs before last-pane resolution (`src/Antiphon.SessionRunner/HerdrPaneChild.cs:307`). A missing current tab-label match creates a new tab (`HerdrPaneChild.cs:415`). The sidecar writes launch options as labels (`HerdrPaneChild.cs:1015`); baseline and GET refresh do not update them (`HerdrEventPumpService.cs:192`, `SessionRunnerRuntime.cs:1549`).

The historical agent `2ee02f40-7b6d-48b1-96fc-c4344c651910` returned HTTP 404 at 2026-09-15T20:34:02Z. Installed Herdr is 0.8.2; `herdr tab get w2:tZ` returned `server_not_running` at the default socket. No live reproduction is claimed. CARD-0462's full description was read from the board using `pwsh -NoProfile -File scripts/card.ps1 get CARD-0462` (card id `0b14040a-4382-44c0-9bd0-3a1156cd07b3`, revision count 1).

The evidence harness in `evidence/card-0462/` uses the existing fake named-pipe server and unchanged production runner code. Run from the repository root:

```powershell
dotnet run --project docs/investigations/evidence/card-0462/Reproduce.csproj --property:OutputPath=bin-c462/
```

## Not done, noted

The card requests server-mediated following of existing pins; no fix is designed or implemented in this investigation.
