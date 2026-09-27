namespace Antiphon.Tests.TestHelpers;

internal static class RepositoryLeaseCrashWorker
{
    internal const string CrashScript = """
        $ErrorActionPreference = 'Stop'
        $assemblyDirectory = [IO.Path]::GetDirectoryName($args[0])
        $loadContext = [Runtime.Loader.AssemblyLoadContext]::new('C448 lease crash worker', $true)
        $resolver = [Runtime.Loader.AssemblyDependencyResolver]::new($args[0])
        $resolveDependency = [Func[Runtime.Loader.AssemblyLoadContext, Reflection.AssemblyName, Reflection.Assembly]] {
            param($context, $name)
            $path = $resolver.ResolveAssemblyToPath($name)
            if ($null -eq $path) {
                $candidate = [IO.Path]::Combine($assemblyDirectory, $name.Name + '.dll')
                if ([IO.File]::Exists($candidate)) { $path = $candidate }
            }
            if ($null -eq $path) { return $null }
            return $context.LoadFromAssemblyPath($path)
        }.GetNewClosure()
        $loadContext.add_Resolving($resolveDependency)
        $server = $loadContext.LoadFromAssemblyPath($args[0])
        $git = [Activator]::CreateInstance($server.GetType('Antiphon.Server.Infrastructure.Git.LandingGit', $true), [object[]]@($null))
        $operation = $git.RunAsync($args[1], [string[]]@('commit', '--allow-empty', '-m', 'owned child'), [Threading.CancellationToken]::None)
        $result = $operation.GetAwaiter().GetResult()
        if (-not $result.Succeeded) { throw ('fixture_' + $result.Diagnostic) }
        """;

    internal const string RecoveryScript = """
        $ErrorActionPreference = 'Stop'
        $record = Get-Content -LiteralPath $args[4] -Raw | ConvertFrom-Json
        if ($record.ProcessId -ne [int]$args[6] -or $record.StartTicks -le 0) { throw 'worker did not record the live child' }
        $child = [Diagnostics.Process]::GetProcessById([int]$record.ProcessId)
        if ($child.HasExited) { throw 'recorded child exited before recovery' }
        # Linux's Process.StartTime wall-clock conversion can shift across processes.
        # Seed and inspect it in this same reader, as in the D1 live-recovery case.
        $record.StartTicks = $child.StartTime.ToUniversalTime().Ticks
        [IO.File]::WriteAllText($args[4], ($record | ConvertTo-Json -Compress))
        [IO.File]::Copy($args[4], $args[7])
        $assemblyDirectory = [IO.Path]::GetDirectoryName($args[0])
        $loadContext = [Runtime.Loader.AssemblyLoadContext]::new('C448 lease recovery worker', $true)
        $resolver = [Runtime.Loader.AssemblyDependencyResolver]::new($args[0])
        $resolveDependency = [Func[Runtime.Loader.AssemblyLoadContext, Reflection.AssemblyName, Reflection.Assembly]] {
            param($context, $name)
            $path = $resolver.ResolveAssemblyToPath($name)
            if ($null -eq $path) {
                $candidate = [IO.Path]::Combine($assemblyDirectory, $name.Name + '.dll')
                if ([IO.File]::Exists($candidate)) { $path = $candidate }
            }
            if ($null -eq $path) { return $null }
            return $context.LoadFromAssemblyPath($path)
        }.GetNewClosure()
        $loadContext.add_Resolving($resolveDependency)
        $server = $loadContext.LoadFromAssemblyPath($args[0])
        $git = [Activator]::CreateInstance($server.GetType('Antiphon.Server.Infrastructure.Git.LandingGit', $true), [object[]]@($null))
        $leaseType = $server.GetType('Antiphon.Server.Infrastructure.Git.RepositoryMutationLease', $true)
        $leases = [Activator]::CreateInstance($leaseType, [object[]]@($git, $null))
        foreach ($repository in @($args[1], $args[2])) {
            $lease = $leases.TryAcquireAsync($repository, [Threading.CancellationToken]::None).GetAwaiter().GetResult()
            if ($null -ne $lease) { $lease.DisposeAsync().GetAwaiter().GetResult(); exit 10 }
        }
        $recoveryOutput = @(& $args[5] -Repository $args[1] -Execute -ConfirmDescendantsExited)
        $recoveryExit = $LASTEXITCODE
        if ($recoveryExit -ne 3 -or -not ($recoveryOutput -match 'retained \(alive\):')) { exit 11 }
        foreach ($repository in @($args[1], $args[2])) {
            $lease = $leases.TryAcquireAsync($repository, [Threading.CancellationToken]::None).GetAwaiter().GetResult()
            if ($null -ne $lease) { $lease.DisposeAsync().GetAwaiter().GetResult(); exit 12 }
        }
        $ownedProcess = [Diagnostics.Process]::GetCurrentProcess()
        [IO.File]::WriteAllText($args[3], (@{ Worker = $ownedProcess.Id; Child = $child.Id; StartTicks = $record.StartTicks; HeldSource = $true; HeldMain = $true; RecoveryExit = $recoveryExit } | ConvertTo-Json -Compress))
        """;
}
