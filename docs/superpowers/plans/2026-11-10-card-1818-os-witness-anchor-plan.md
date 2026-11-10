# CARD-1818 OS-witness anchor

CARD-1818 arrived with no checkpoint table. This file exists so
`scripts/check-os-witnesses.ps1` can resolve checkpoint ids. It is not a
checkpoint-tool manifest. Do not run `tools/Antiphon.Checkpoints` for CP-1.
Ordinary proof is the leased class filters and `scripts/test-qualification-stop.ps1`
already recorded on the Code task.

The only changed OS selection is the pre-existing platform gate in
`scripts/qualification/qualification-stop.ps1`. C# production changes are portable.
The native Windows stop (taskkill, Win32_Process.CommandLine, PowerShell 5.1)
stays pending.

## Verification design

### Checkpoints

| ID | Slice | Build | Filter | Command | Expected | Result | Slot | Minutes |
|---|---|---|---|---|---|---|---|---|
| CP-1 | os-witness | n/a | n/a | not a checkpoint-tool row; OS-witness id anchor only | checker exit 0 | recorded by Code outside the checkpoint tool | n/a | n/a |
