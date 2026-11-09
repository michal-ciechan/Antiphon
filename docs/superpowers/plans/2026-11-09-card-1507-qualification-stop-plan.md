# CARD-1507 qualification stop

## Verification design

The closed proof is one PowerShell witness, `scripts/test-qualification-stop.ps1 -Case Smoke`.
It drives `Invoke-QualificationRecordedStop` with an injected observation and an injected tree
stop, and it drives `Invoke-QualificationWindowsTreeStop` on a Unix host so the Windows
`taskkill` branch returns before a process starts. Five named functions cover kill order, the
stale-lock move, the run-log line, the Unix tree-stop refusal, and the entry refusal. There is
no C# test class for this script. Later Mutation owns any positive control; this plan names none.

CP-1 is a command row because a treenode filter would select zero TUnit methods. The checkpoint
tool runs that command once, under its own build slot. Linux green does not discharge the
Windows `taskkill` tree stop or a Windows PowerShell 5.1 run. Those stay pending on an eligible
Windows runner, against a private sleeper tree, never against the live qualification.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| CP-1 | S1 | n/a | qualification-stop | `TEMP=/tmp pwsh -NoProfile -File scripts/test-qualification-stop.ps1 -Case Smoke` | recorded stop order, stale lock move, run log, unix refusal | exit 0 and C1507 failed=0 | n/a | 2 |
