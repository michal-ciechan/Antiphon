using System.Diagnostics;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Checkpoints;

/// <summary>
/// Import-CheckpointCompiledSource (scripts/lib/checkpoint-file-reader.ps1): the script helpers' C# is compiled once
/// per source version into a per-user cache and only loaded afterwards; a corrupt cached assembly is rebuilt, and a
/// source that does not compile fails once with nothing cached.
/// </summary>
[Category("Integration")]
[ParallelLimiter<CheckpointToolSpawnLimit>]
public sealed class CheckpointTypeCacheTests
{
    [Test]
    [Timeout(120_000)]
    public void Compiled_helper_is_cached_reused_rebuilt_when_corrupt_and_compile_errors_surface_once(CancellationToken cancellationToken)
    {
        var reader = Path.Combine(CheckpointFixtures.RepoRoot, "scripts", "lib", "checkpoint-file-reader.ps1");
        var nonce = Guid.NewGuid().ToString("N")[..12];
        var name = "Witness" + nonce;
        var source = "namespace " + name + " { public static class Probe { public static string Hello() { return \"hi\"; } } }";
        string Load(string code, string typeName, string cacheName) => Pwsh(
            ". '" + reader.Replace("'", "''") + "'; " +
            "try { Import-CheckpointCompiledSource -Source '" + code + "' -TypeName '" + typeName + "' -Name '" + cacheName + "'; " +
            "'RESULT=' + [" + typeName + "]::Hello() } catch { 'ERRORID=' + $_.FullyQualifiedErrorId }");

        var directory = Pwsh(". '" + reader.Replace("'", "''") + "'; Get-CheckpointTypeCacheDirectory").Trim();
        Directory.Exists(directory).ShouldBeTrue("a per-user cache directory exists");
        if (!OperatingSystem.IsWindows())
            File.GetUnixFileMode(directory).ShouldBe(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute,
                "never a directory another account can write");
        try
        {
            Load(source, name + ".Probe", name).ShouldContain("RESULT=hi");
            var cached = Directory.GetFiles(directory, name + "-*.dll").ShouldHaveSingleItem("compiled once into the cache");
            var bytes = File.ReadAllBytes(cached);

            Load(source, name + ".Probe", name).ShouldContain("RESULT=hi");
            Directory.GetFiles(directory, name + "-*.dll").ShouldHaveSingleItem().ShouldBe(cached);
            File.ReadAllBytes(cached).ShouldBe(bytes, "a later process loads the cached assembly, it does not rebuild it");

            File.WriteAllBytes(cached, [1, 2, 3]);
            Load(source, name + ".Probe", name).ShouldContain("RESULT=hi", customMessage: "a corrupt cache still loads the type");
            Load(source, name + ".Probe", name).ShouldContain("RESULT=hi");
            new FileInfo(cached).Length.ShouldBeGreaterThan(3, "the corrupt assembly was rebuilt");

            var broken = "Broken" + nonce;
            Load("namespace " + broken + " { public static class Probe { oops } }", broken + ".Probe", broken)
                .ShouldContain("ERRORID=COMPILER_ERRORS", customMessage: "a compile error is raised as Add-Type raises it");
            Directory.GetFiles(directory, broken + "-*.dll").ShouldBeEmpty("nothing is cached for a source that does not compile");
        }
        finally
        {
            foreach (var file in Directory.GetFiles(directory, "*" + nonce + "-*.dll"))
                try { File.Delete(file); } catch (IOException) { }
        }

        string Pwsh(string command)
        {
            var start = new ProcessStartInfo("pwsh")
            {
                UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
                WorkingDirectory = CheckpointFixtures.RepoRoot,
            };
            foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-Command", command })
                start.ArgumentList.Add(argument);
            var output = CheckpointFixtureProcess.Run(start, 60_000, cancellationToken);
            output.Exit.ShouldBe(0, output.Stdout + output.Stderr);
            return output.Stdout;
        }
    }
}
