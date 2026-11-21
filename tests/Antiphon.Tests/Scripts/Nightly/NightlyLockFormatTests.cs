using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Antiphon.Nightly;
using Antiphon.Tests.Application;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts.Nightly;

/// <summary>
/// W2-nightly S18: the lock records the compiled locks write (NightlyRunLock.cs, ReleaseGateNativeLock.cs) are read
/// unchanged by the readers that stay PowerShell: Read-NightlyLockRecord (nightly-common.ps1; live-lane-run.ps1 reads the
/// native lock through it), Read-RcQualificationRecord and Get-RcQualificationStatus (lib/rc-qualification-status.ps1,
/// behind scripts/rc-qualification-status.ps1 and the qualification scripts). Each test writes the records here with the
/// production process table and clock, keeps the owner's handle open as a run does, and reads them in a pwsh child (and
/// Windows PowerShell 5.1 on Windows). The bytes are also the ones "[ordered]@{...} | ConvertTo-Json -Compress" gives for
/// the same values. These tests launch PowerShell because the readers are production PowerShell.
/// </summary>
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class NightlyLockFormatTests
{
    private static readonly string[] RunFields = ["pid", "processStartedAt", "startedAt", "runId", "parentPid", "parentStartedAt", "bootId", "hostId"];
    private static readonly string[] NativeFields = ["pid", "processStartedAt", "runId", "lane", "continuationId", "acquiredAt"];

    [Test]
    [Timeout(180_000)]
    public async Task Core_written_locks_read_back_through_the_PowerShell_readers(CancellationToken cancellationToken)
    {
        using var fixture = NightlyFixture.Create("nightly-lock-format");
        var processes = new NightlyProcessTable();
        var runId = Guid.NewGuid().ToString("N");
        var state = fixture.PathOf("state");
        var reclaimState = fixture.PathOf("state-reclaim");
        var coord = fixture.PathOf("coord");
        using var runLock = new NightlyRunLock(TimeProvider.System, processes, null, null);
        using var reclaimLock = new NightlyRunLock(TimeProvider.System, processes, null, null);
        var native = new ReleaseGateNativeLock(TimeProvider.System, processes);

        var fresh = runLock.Enter(state, runId);
        fresh.Reason.ShouldBe("acquired");
        runLock.HoldsHandle.ShouldBeTrue();
        var shared = native.Enter(coord, runId, "rc", "continuation-1");
        shared.Reason.ShouldBe("acquired");

        // A dead legacy owner (no boot or host id, its parent gone too) is reclaimed in place; the new owner keeps the
        // shared handle.
        Directory.CreateDirectory(reclaimState);
        const int deadOwner = 2147483000, deadParent = 2147483001;
        processes.IsAlive(deadOwner).ShouldBeFalse();
        processes.IsAlive(deadParent).ShouldBeFalse();
        File.WriteAllText(NightlyRunLock.LockPath(reclaimState), NightlyLockFixture.PlantJson(deadOwner, "dead-run", parentPid: deadParent));
        var reclaimRunId = Guid.NewGuid().ToString("N");
        var reclaimed = reclaimLock.Enter(reclaimState, reclaimRunId);
        reclaimed.Reason.ShouldBe("reclaimed");
        reclaimLock.HoldsHandle.ShouldBeTrue();

        var paths = new Dictionary<string, string>
        {
            ["run"] = NightlyRunLock.LockPath(state),
            ["native"] = ReleaseGateNativeLock.LockPath(coord),
            ["reclaimed"] = NightlyRunLock.LockPath(reclaimState),
        };
        var expected = new Dictionary<string, NightlyLockRecord>
        {
            ["run"] = fresh.Record!,
            ["native"] = shared.Record!,
            ["reclaimed"] = reclaimed.Record!,
        };
        // The record written equals the record read back here.
        foreach (var (name, path) in paths)
            NightlyLockRecord.Read(path)!.RunId.ShouldBe(expected[name].RunId, name);

        var values = fixture.PathOf("values.txt");
        File.WriteAllLines(values, Values("run", fresh.Record!, RunFields).Concat(Values("native", shared.Record!, NativeFields))
            .Concat(Values("reclaimed", reclaimed.Record!, RunFields)));
        var input = fixture.PathOf("input.txt");
        File.WriteAllLines(input, [
            "run\t" + paths["run"], "native\t" + paths["native"], "reclaimed\t" + paths["reclaimed"],
            "reclaimRecord\t" + Path.Combine(reclaimState, NightlyRunLock.ReclaimRecordName),
            "stateRoot\t" + state, "coordinationRoot\t" + coord, "runId\t" + runId,
        ]);
        var script = fixture.PathOf("read-locks.ps1");
        File.WriteAllText(script, ReadScript, new UTF8Encoding(false));

        foreach (var host in Hosts())
        {
            var output = fixture.PathOf("observed-" + Path.GetFileNameWithoutExtension(host) + ".json");
            await RunAsync(host, script, [DelegateScriptRunner.RepoRoot, input, values, output], cancellationToken);
            using var observed = JsonDocument.Parse(File.ReadAllText(output));
            var root = observed.RootElement;
            var label = host + " " + root.GetProperty("psVersion").GetString();
            var modern = !root.GetProperty("psVersion").GetString()!.StartsWith("5.", StringComparison.Ordinal);

            foreach (var (name, record) in expected)
            {
                var read = root.GetProperty("reads").GetProperty(name);
                var fields = name == "native" ? NativeFields : RunFields;
                var nightly = read.GetProperty("nightly");
                foreach (var field in fields)
                    Text(nightly.GetProperty(field)).ShouldBe(record.Value(field), label + " Read-NightlyLockRecord " + name + "." + field);
                read.GetProperty("rcStatus").GetString().ShouldBe("ok", label + " Read-RcQualificationRecord " + name);
                var rc = read.GetProperty("rc");
                foreach (var field in fields)
                    Text(rc.GetProperty(field)).ShouldBe(record.Value(field), label + " Read-RcQualificationRecord " + name + "." + field);
                read.GetProperty("rcPidIsInteger").GetBoolean().ShouldBeTrue(label + " the pid is a JSON integer " + name);
                // The record's pid and process start are this process: the owner observation matches them to the tick.
                read.GetProperty("owner").GetString().ShouldBe("alive/pid-start-match", label + " Get-RcQualificationOwnerObservation " + name);
                if (modern)
                    read.GetProperty("rebuilt").GetString().ShouldBe(Convert.ToBase64String(NightlyLockFixture.ReadShared(paths[name])),
                        label + " ConvertTo-Json -Compress of the same values gives the same bytes: " + name);
            }

            var locks = root.GetProperty("status");
            locks.GetProperty("run").GetString().ShouldBe("this-run/alive", label + " Get-RcQualificationStatus run lock");
            locks.GetProperty("native").GetString().ShouldBe("this-run/alive", label + " Get-RcQualificationStatus native lock");
            locks.GetProperty("nativeLane").GetString().ShouldBe("rc", label + " Get-RcQualificationStatus native lane");
            locks.GetProperty("nativeContinuation").GetString().ShouldBe("continuation-1", label + " Get-RcQualificationStatus native continuation");

            var reclaim = root.GetProperty("reclaim");
            reclaim.GetProperty("reason").GetString().ShouldBe("dead-owner-legacy", label);
            reclaim.GetProperty("newRunId").GetString().ShouldBe(reclaimRunId, label);
            reclaim.GetProperty("previousRunId").GetString().ShouldBe("dead-run", label);
            reclaim.GetProperty("previousPid").GetString().ShouldBe(deadOwner.ToString(), label);
            reclaim.GetProperty("previousPidIsInteger").GetBoolean().ShouldBeTrue(label);
        }

        runLock.Exit(state, runId);
        reclaimLock.Exit(reclaimState, reclaimRunId);
        native.Exit(coord, runId);
        foreach (var path in paths.Values) File.Exists(path).ShouldBeFalse("exit removes " + path);
        File.Exists(Path.Combine(reclaimState, NightlyRunLock.ReclaimRecordName)).ShouldBeTrue("exit keeps lock-reclaim.json");
    }

    /// <summary>
    /// CARD-1396: the owner's handle mode (FileAccess.Write, FileShare.Read) still lets the record be read, here and from a
    /// second process through Read-NightlyLockRecord, and still refuses a FileShare.None open (the reclaim compare).
    /// </summary>
    [Test]
    [Timeout(120_000)]
    public async Task C1396_WriteHeldLockReadable(CancellationToken cancellationToken)
    {
        using var fixture = NightlyFixture.Create("nightly-lock-held");
        var lockPath = fixture.PathOf("run.lock");
        File.WriteAllText(lockPath, """{"pid":1,"runId":"held-run","parentPid":0}""");
        using var holder = new FileStream(lockPath, FileMode.Open, FileAccess.Write, FileShare.Read);

        var seen = NightlyLockRecord.Read(lockPath)?.RunId ?? "";
        seen.ShouldBe("held-run", "C1396 Write-held lock reads in-process " + seen);
        var noneRefused = false;
        try
        {
            using var exclusive = new FileStream(lockPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException)
        {
            noneRefused = true;
        }
        noneRefused.ShouldBeTrue("C1396 Write-held lock still refuses FileShare.None refused=" + noneRefused);

        var script = fixture.PathOf("read-child.ps1");
        File.WriteAllText(script, """
            param([string]$Repo, [string]$LockPath, [string]$ResultPath)
            $ErrorActionPreference = 'Stop'
            . (Join-Path $Repo 'scripts/lib/nightly-common.ps1')
            $record = Read-NightlyLockRecord -LockPath $LockPath
            $seen = ''
            if ($null -ne $record) { $seen = [string]$record.runId }
            [IO.File]::WriteAllText($ResultPath, $seen)
            """, new UTF8Encoding(false));
        var result = fixture.PathOf("read.txt");
        await RunAsync(Hosts()[0], script, [DelegateScriptRunner.RepoRoot, lockPath, result], cancellationToken);
        var childSeen = File.ReadAllText(result);
        childSeen.ShouldBe("held-run", "C1396 Write-held lock reads from a second process seen=" + childSeen);
    }

    private static IEnumerable<string> Values(string prefix, NightlyLockRecord record, string[] fields) =>
        fields.Select(field => prefix + "." + field + "\t" + (record.Value(field) is { } value ? Convert.ToBase64String(Encoding.UTF8.GetBytes(value)) : "-"));

    private static string? Text(JsonElement value) => value.ValueKind == JsonValueKind.Null ? null : value.ToString();

    // pwsh, and Windows PowerShell 5.1 on Windows (rc-qualification-status.ps1 and nightly-common.ps1 still run there).
    private static string[] Hosts()
    {
        var hosts = new List<string> { OperatingSystem.IsWindows() ? ScriptHarnessProcessFixture.ResolveInstalledPowerShell() : "pwsh" };
        var legacy = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");
        if (OperatingSystem.IsWindows() && File.Exists(legacy)) hosts.Add(legacy);
        return [.. hosts];
    }

    private static async Task RunAsync(string executable, string script, string[] arguments, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        // The readers need no compiled core: do not build or load it in the child.
        start.Environment["ANTIPHON_NIGHTLY_CORE"] = "off";
        var flags = OperatingSystem.IsWindows() ? new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", script } : ["-NoProfile", "-NonInteractive", "-File", script];
        foreach (var argument in flags.Concat(arguments)) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        try
        {
            var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            var text = await stdout + await stderr;
            process.ExitCode.ShouldBe(0, executable + ": " + text);
        }
        finally
        {
            if (!process.HasExited)
            {
                try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            }
        }
    }

    // Reads each lock with the PowerShell readers and rebuilds its bytes with ConvertTo-Json -Compress from the values
    // the core wrote. Parseable by Windows PowerShell 5.1.
    private const string ReadScript = """
        param([string]$Repo, [string]$InputPath, [string]$ValuesPath, [string]$OutPath)
        $ErrorActionPreference = 'Stop'
        . (Join-Path $Repo 'scripts/lib/nightly-common.ps1')
        . (Join-Path $Repo 'scripts/lib/rc-qualification-status.ps1')
        function Read-FormatMap([string]$Path, [bool]$Decode) {
            $map = @{}
            foreach ($line in [IO.File]::ReadAllLines($Path)) {
                $parts = $line -split "`t", 2
                $value = $parts[1]
                if ($Decode) {
                    if ($value -eq '-') { $value = $null } else { $value = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($value)) }
                }
                $map[$parts[0]] = $value
            }
            return $map
        }
        function ConvertTo-FormatText($Value) {
            if ($null -eq $Value) { return $null }
            if ($Value -is [datetime]) { return $Value.ToUniversalTime().ToString('o') }
            return [string]$Value
        }
        function Get-FormatFields($Record, [string[]]$Names) {
            $map = [ordered]@{}
            foreach ($name in $Names) {
                $prop = $null
                if ($null -ne $Record) { $prop = $Record.PSObject.Properties[$name] }
                if ($null -eq $prop) { $map[$name] = '<absent>' } else { $map[$name] = ConvertTo-FormatText $prop.Value }
            }
            return $map
        }
        $in = Read-FormatMap $InputPath $false
        $v = Read-FormatMap $ValuesPath $true
        $runFields = @('pid', 'processStartedAt', 'startedAt', 'runId', 'parentPid', 'parentStartedAt', 'bootId', 'hostId')
        $nativeFields = @('pid', 'processStartedAt', 'runId', 'lane', 'continuationId', 'acquiredAt')
        $reads = [ordered]@{}
        foreach ($name in @('run', 'native', 'reclaimed')) {
            $fields = $runFields
            if ($name -eq 'native') { $fields = $nativeFields }
            $path = $in[$name]
            $rc = Read-RcQualificationRecord -Path ([IO.Path]::GetFullPath($path))
            $owner = Get-RcQualificationOwnerObservation $rc.record
            if ($name -eq 'native') {
                $rebuilt = [ordered]@{ pid = [int]$v['native.pid']; processStartedAt = $v['native.processStartedAt']; runId = $v['native.runId']
                    lane = $v['native.lane']; continuationId = $v['native.continuationId']; acquiredAt = $v['native.acquiredAt'] }
            } else {
                $p = $name + '.'
                $rebuilt = [ordered]@{ pid = [int]$v[$p + 'pid']; processStartedAt = $v[$p + 'processStartedAt']; startedAt = $v[$p + 'startedAt']
                    runId = $v[$p + 'runId']; parentPid = [int]$v[$p + 'parentPid']; parentStartedAt = $v[$p + 'parentStartedAt']
                    bootId = $v[$p + 'bootId']; hostId = $v[$p + 'hostId'] }
            }
            $reads[$name] = [ordered]@{
                nightly = Get-FormatFields (Read-NightlyLockRecord -LockPath $path) $fields
                rcStatus = [string]$rc.status
                rc = Get-FormatFields $rc.record $fields
                rcPidIsInteger = ($rc.record.pid -is [int] -or $rc.record.pid -is [long])
                owner = ([string]$owner.status + '/' + [string]$owner.reason)
                rebuilt = [Convert]::ToBase64String([Text.Encoding]::ASCII.GetBytes(($rebuilt | ConvertTo-Json -Compress)))
            }
        }
        $status = Get-RcQualificationStatus -CandidateId 'format-candidate' -RunId $in['runId'] -StateRoot $in['stateRoot'] -CoordinationRoot $in['coordinationRoot']
        $reclaim = [IO.File]::ReadAllText($in['reclaimRecord']) | ConvertFrom-Json
        $out = [ordered]@{
            psVersion = $PSVersionTable.PSVersion.ToString()
            reads = $reads
            status = [ordered]@{
                run = ([string]$status.locks.run.relationship + '/' + [string]$status.locks.run.ownerObservation.status)
                native = ([string]$status.locks.native.relationship + '/' + [string]$status.locks.native.ownerObservation.status)
                nativeLane = [string]$status.locks.native.record.lane
                nativeContinuation = [string]$status.locks.native.record.continuationId
            }
            reclaim = [ordered]@{
                reason = [string]$reclaim.reason
                newRunId = [string]$reclaim.newRunId
                previousRunId = [string]$reclaim.previousRunId
                previousPid = [string]$reclaim.previousPid
                previousPidIsInteger = ($reclaim.previousPid -is [int] -or $reclaim.previousPid -is [long])
            }
        }
        [IO.File]::WriteAllText($OutPath, ($out | ConvertTo-Json -Depth 8))
        """;
}
