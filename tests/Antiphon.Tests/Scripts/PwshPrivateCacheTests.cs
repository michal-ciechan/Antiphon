using System.Diagnostics;
using System.Globalization;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

[Category("Unit")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class PwshPrivateCacheTests
{
    private const string ProbeVariable = "ANTIPHON_PWSH_CACHE_PROBE";

    [Test]
    [Timeout(60_000)]
    public async Task Child_environment_carries_private_cache_under_temp_root()
    {
        var root = Environment.GetEnvironmentVariable("XDG_CACHE_HOME");
        root.ShouldNotBeNullOrWhiteSpace();
        var full = Path.GetFullPath(root);
        Under(Path.GetTempPath(), full).ShouldBeTrue();
        Directory.Exists(full).ShouldBeTrue();
        var marker = Path.Combine(full, "owner.pid");
        File.ReadAllText(marker).Trim().ShouldBe(Environment.ProcessId.ToString(CultureInfo.InvariantCulture));
        var seen = await ReadProcessAsync(InheritedPwsh(), "inherited");
        seen.ShouldBe(full);
    }

    [Test]
    [Timeout(90_000)]
    public async Task Absent_empty_and_foreign_values_each_get_a_private_child_cache()
    {
        var temp = Directory.CreateTempSubdirectory("c1790-input-").FullName;
        var foreign = Path.Combine(temp, "foreign");
        Directory.CreateDirectory(foreign);
        try
        {
            var absent = await ProbeAsync(temp, "absent", static environment => environment.Remove("XDG_CACHE_HOME"));
            var empty = await ProbeAsync(temp, "empty", environment => environment["XDG_CACHE_HOME"] = "");
            var supplied = await ProbeAsync(temp, "foreign", environment => environment["XDG_CACHE_HOME"] = foreign);
            foreach (var seen in new[] { absent, empty, supplied })
            {
                Under(temp, seen).ShouldBeTrue(seen);
                seen.ShouldNotBe(Path.GetFullPath(foreign));
                Directory.Exists(seen).ShouldBeFalse(seen);
            }
            Directory.Exists(foreign).ShouldBeTrue();
            absent.ShouldNotBe(empty);
            empty.ShouldNotBe(supplied);
            supplied.ShouldNotBe(absent);
        }
        finally
        {
            Directory.Delete(temp, recursive: true);
        }
    }

    [Test]
    [Timeout(90_000)]
    public async Task Two_concurrent_hosts_get_different_roots_and_cleanup_removes_them()
    {
        var temp = Directory.CreateTempSubdirectory("c1790-hosts-").FullName;
        try
        {
            var first = ProbeAsync(temp, "host-a", static environment => environment.Remove("XDG_CACHE_HOME"));
            var second = ProbeAsync(temp, "host-b", static environment => environment.Remove("XDG_CACHE_HOME"));
            var paths = await Task.WhenAll(first, second);
            paths[0].ShouldNotBe(paths[1]);
            foreach (var seen in paths)
            {
                Under(temp, seen).ShouldBeTrue(seen);
                Directory.Exists(seen).ShouldBeFalse(seen);
            }
        }
        finally
        {
            Directory.Delete(temp, recursive: true);
        }
    }

    private static ProcessStartInfo InheritedPwsh()
    {
        var start = new ProcessStartInfo("pwsh")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-NonInteractive");
        start.ArgumentList.Add("-Command");
        start.ArgumentList.Add("[Console]::Out.Write($env:XDG_CACHE_HOME)");
        return start;
    }

    private static async Task<string> ProbeAsync(string tempRoot, string name, Action<IDictionary<string, string?>> configure)
    {
        var output = Path.Combine(tempRoot, name + ".txt");
        var helper = Path.Combine(AppContext.BaseDirectory, "script-harness-host", "Antiphon.ScriptHarnessHost.dll");
        File.Exists(helper).ShouldBeTrue(helper);
        var start = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = tempRoot,
        };
        start.Environment["TMPDIR"] = tempRoot;
        start.Environment["TMP"] = tempRoot;
        start.Environment["TEMP"] = tempRoot;
        start.Environment[ProbeVariable] = output;
        configure(start.Environment);
        start.ArgumentList.Add(helper);
        var seen = await ReadProcessAsync(start, name);
        seen.ShouldBe((await File.ReadAllTextAsync(output)).Trim(), name);
        return seen;
    }

    private static async Task<string> ReadProcessAsync(ProcessStartInfo start, string name)
    {
        using var process = Process.Start(start) ?? throw new InvalidOperationException(name + " did not start.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await process.WaitForExitAsync(timeout.Token);
        var error = await stderr;
        var output = (await stdout).Trim();
        process.ExitCode.ShouldBe(0, name + " " + output + " " + error);
        if (output.Length > 0)
            return output;
        start.Environment.TryGetValue(ProbeVariable, out var report).ShouldBeTrue(name);
        return (await File.ReadAllTextAsync(report!)).Trim();
    }

    private static bool Under(string root, string path)
    {
        var relative = Path.GetRelativePath(Path.GetFullPath(root), Path.GetFullPath(path));
        return relative != ".."
            && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            && !Path.IsPathRooted(relative);
    }
}
