namespace Antiphon.Tests.TestHelpers;

internal static class RepositoryLeaseCrashWorker
{
    // The workers construct server types by reflection, so a new optional constructor parameter
    // (CARD-1776 gave LandingGit IOptions<GitSettings>) is not a compile error here.
    // Activator.CreateInstance does not bind optional parameters; fill each one from its default.
    private const string ConstructWithDefaults = """
        function New-ServerObject([Type]$type, [object[]]$leading) {
            $constructors = @($type.GetConstructors())
            if ($constructors.Count -ne 1) { throw ('fixture_constructor_count ' + $type.Name) }
            $parameters = $constructors[0].GetParameters()
            $values = [object[]]::new($parameters.Length)
            for ($i = 0; $i -lt $parameters.Length; $i++) {
                if ($i -lt $leading.Length) { $values[$i] = $leading[$i] }
                elseif ($parameters[$i].HasDefaultValue) { $values[$i] = $parameters[$i].DefaultValue }
                else { throw ('fixture_constructor_required ' + $type.Name + '.' + $parameters[$i].Name) }
            }
            return $constructors[0].Invoke($values)
        }
        """;

    internal const string CrashScript = $$"""
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
        {{ConstructWithDefaults}}
        $git = New-ServerObject ($server.GetType('Antiphon.Server.Infrastructure.Git.LandingGit', $true)) @()
        $operation = $git.RunAsync($args[1], [string[]]@('commit', '--allow-empty', '-m', 'owned child'), [Threading.CancellationToken]::None)
        $result = $operation.GetAwaiter().GetResult()
        if (-not $result.Succeeded) { throw ('fixture_' + $result.Diagnostic) }
        """;
}
