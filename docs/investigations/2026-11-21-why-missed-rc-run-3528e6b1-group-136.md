# Why Windows Full run 3528e6b1 missed the RC port corpus path

The seeded Windows Full run `3528e6b1` on `rc/2026-11-30` at
`bd34a7ff6da9db1a3fa3690f824890ef9b7cc2c0` reported two failed tests in
`antiphon-all-group-136.trx`. `NonBooleanProvenanceFailureInvalidates` recorded
22 mismatches and `CarriedChunkResultInvalidatesCarries` recorded 13. Both came
from loading `C:\opt\powershell\System.Management.Automation.dll` while
reconstructing PowerShell objects for golden cases. Each affected case failed
before the C# verdict could be compared with the golden result.

## How it escaped review

The S6c prerequisite harness reconstructed real `PSObject` instances from the
pre-port PowerShell golden and invoked the compiled code. Its test process
loaded the real assembly directly from `/opt/powershell`, the server2 image
layout. CP-1 covered all 19 corpus methods there and review `bb05708f` recorded
19 passed with Windows UNOBSERVED. The checkpoint plan had no native Windows
corpus row. The shadow chain carried that coverage forward, so later Linux
checks exercised the same working path. Wrapper harnesses execute inside
PowerShell, where the assembly is already loaded by the shell. The Windows Full
run exercised the separate .NET test loader on a different filesystem.
[S6c checkpoint plan](../superpowers/plans/2026-11-20-card-1836-s6c-csharp-port-checkpoints.md) ·
[Recorded review](https://github.com/michal-ciechan/Antiphon/commit/6ecb2fbb3e53184dc0f632ee4a200739c1884747)

The hard-coded assembly path was confined to `RcPortCorpus.cs`. The shadow,
dirty-order and differential tests all consume that helper. Their C# product
implementations and the PowerShell wrappers contain no install-path dependency.
`Antiphon.Nightly.csproj` references `PowerShellStandard.Library` 5.1.1 for
compilation, and the PowerShell host supplies the runtime. The original golden
generator is absent from the reviewed tree (review advisory A-3). The
checked-in 199-case golden has no assembly-loading code.

## Repair and Windows verification

Linux replay discovers the installed assembly through `PSHOME`, then the
`pwsh` executable on `PATH` including symlink targets, then the two standard
runner locations. The desktop's observed pwsh 7.6.10 runs on .NET 10, while the
test project targets .NET 9. The Windows test build supplies the PowerShell
7.5.4 runtime through a test-only NuGet reference, matching the version used
by the Linux runner. The resolver can also probe `PSHOME` and the `pwsh.exe`
directory. Failed discovery names the corpus and shows the DLL paths it tried.
[Desktop PowerShell inventory](2026-11-19-card-2005-dry-write-on-real-logs.md) ·
[PowerShell runtime versions](https://learn.microsoft.com/powershell/scripting/install/powershell-support-lifecycle?view=powershell-7.6) ·
[PowerShell 7.5.4 package](https://www.nuget.org/packages/System.Management.Automation/7.5.4)

After run `3528e6b1` ends, include the two failed methods in the single native
Windows pass already owed for CARD-2048 C475 and `CheckpointIndexPathsTests`.
Use a fresh build at the repair SHA and retain the Windows TRX with both
methods executed. For future changes to the shadow chain's corpus loader or
PowerShell materialization, add a Windows counterpart to CP-1 alongside the
Linux row and require its 19 passes with zero skips before clearing platform
verification. Record the source SHA, PowerShell version and passed method
roster. Until that row runs, record Windows as UNOBSERVED.

This was a test harness failure in Windows Full qualification. The incremental
shadow chain remains disabled for gate credit.
