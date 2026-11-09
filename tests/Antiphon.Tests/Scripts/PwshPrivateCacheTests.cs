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
        var markerText = File.ReadAllText(Path.Combine(full, "owner.pid")).Trim();
        var parts = markerText.Split('@');
        parts[0].ShouldBe(Environment.ProcessId.ToString(CultureInfo.InvariantCulture));
        parts.Length.ShouldBe(2, markerText);
        var recorded = long.Parse(parts[1], CultureInfo.InvariantCulture);
        var observed = Process.GetCurrentProcess().StartTime.ToUniversalTime().Ticks;
        (recorded - recorded % 10).ShouldBe(observed - observed % 10);
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

    [Test]
    [Timeout(90_000)]
    public async Task Install_removes_dead_owner_caches_and_keeps_live_unmarked_and_linked_ones()
    {
        var temp = Directory.CreateTempSubdirectory("c1793-sweep-").FullName;
        try
        {
            var dead = DeadPid(temp);
            var stale = Seed(temp, $"antiphon-pwsh-cache-{dead}-{Guid.NewGuid():N}", dead.ToString(CultureInfo.InvariantCulture));
            var staleSession = Seed(temp, $"antiphon-pwsh-cache-session-{Guid.NewGuid():N}-{Guid.NewGuid():N}", dead.ToString(CultureInfo.InvariantCulture));
            var live = Seed(temp, $"antiphon-pwsh-cache-{Environment.ProcessId}-{Guid.NewGuid():N}", Environment.ProcessId.ToString(CultureInfo.InvariantCulture));
            var unmarked = Seed(temp, $"antiphon-pwsh-cache-{dead}-{Guid.NewGuid():N}", null);
            var garbled = Seed(temp, $"antiphon-pwsh-cache-{dead}-{Guid.NewGuid():N}", dead.ToString(CultureInfo.InvariantCulture) + "x");
            var foreign = Seed(temp, $"other-pwsh-cache-{dead}-{Guid.NewGuid():N}", dead.ToString(CultureInfo.InvariantCulture));
            var target = Seed(temp, "c1793-link-target", dead.ToString(CultureInfo.InvariantCulture));
            var link = Path.Combine(temp, $"antiphon-pwsh-cache-{dead}-{Guid.NewGuid():N}");
            var linked = TryLink(link, target);

            var seen = await ProbeAsync(temp, "sweep", static environment => environment.Remove("XDG_CACHE_HOME"));

            Directory.Exists(stale).ShouldBeFalse(stale);
            Directory.Exists(staleSession).ShouldBeFalse(staleSession);
            foreach (var kept in new[] { live, unmarked, garbled, foreign, target })
                File.Exists(Path.Combine(kept, "powershell", "StartupProfileData-NonInteractive")).ShouldBeTrue(kept);
            if (linked)
                Directory.Exists(link).ShouldBeTrue(link);
            Directory.Exists(seen).ShouldBeFalse(seen);
        }
        finally
        {
            Directory.Delete(temp, recursive: true);
        }
    }

    [Test]
    [Timeout(90_000)]
    public async Task Checkpoint_tool_installs_a_private_cache_that_sweeps_dead_owners()
    {
        var temp = Directory.CreateTempSubdirectory("c1793-tool-").FullName;
        try
        {
            var dead = DeadPid(temp);
            var stale = Seed(temp, $"antiphon-pwsh-cache-{dead}-{Guid.NewGuid():N}", dead.ToString(CultureInfo.InvariantCulture));
            var tool = Path.Combine(AppContext.BaseDirectory, "Antiphon.Checkpoints.dll");
            File.Exists(tool).ShouldBeTrue(tool);
            var start = new ProcessStartInfo("dotnet")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = temp,
            };
            start.Environment["TMPDIR"] = temp;
            start.Environment["TMP"] = temp;
            start.Environment["TEMP"] = temp;
            start.Environment.Remove("XDG_CACHE_HOME");
            start.ArgumentList.Add(tool);
            start.ArgumentList.Add("--help");
            using var process = Process.Start(start) ?? throw new InvalidOperationException("checkpoint tool did not start.");
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await process.WaitForExitAsync(timeout.Token);
            _ = await stdout;
            var error = await stderr;
            process.ExitCode.ShouldBe(0, error);

            Directory.Exists(stale).ShouldBeFalse(stale);
            Directory.GetDirectories(temp, "antiphon-pwsh-cache-*").ShouldBeEmpty();
        }
        finally
        {
            Directory.Delete(temp, recursive: true);
        }
    }

    // CARD-1813 A-1: the initializer sweeps and creates under ANTIPHON_PWSH_CACHE_ROOT,
    // not under TMPDIR. A sweep mutant must be able to point the test host at a private root.
    [Test]
    [Timeout(90_000)]
    public async Task Cache_root_override_is_the_only_sweep_and_create_root()
    {
        var root = Directory.CreateTempSubdirectory("c1813-root-").FullName;
        var over = Path.Combine(root, "over");
        var elsewhere = Path.Combine(root, "else");
        Directory.CreateDirectory(over);
        Directory.CreateDirectory(elsewhere);
        try
        {
            var dead = DeadPid(root);
            var stale = Seed(over, $"antiphon-pwsh-cache-{dead}-{Guid.NewGuid():N}", dead.ToString(CultureInfo.InvariantCulture));
            var decoy = Seed(elsewhere, $"antiphon-pwsh-cache-{dead}-{Guid.NewGuid():N}", dead.ToString(CultureInfo.InvariantCulture));
            var seen = await ProbeAsync(root, "root", environment =>
            {
                environment.Remove("XDG_CACHE_HOME");
                environment["TMPDIR"] = elsewhere;
                environment["TMP"] = elsewhere;
                environment["TEMP"] = elsewhere;
                environment["ANTIPHON_PWSH_CACHE_ROOT"] = over;
            });

            Directory.Exists(stale).ShouldBeFalse(stale);
            File.Exists(Path.Combine(decoy, "powershell", "StartupProfileData-NonInteractive")).ShouldBeTrue(decoy);
            Under(over, seen).ShouldBeTrue(seen);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    // CARD-1813 A-2: an unmarked family directory older than the 10-minute grace is swept.
    // A fresh unmarked directory and an old garbled marker stay.
    [Test]
    [Timeout(90_000)]
    public async Task Old_unmarked_directories_are_swept_and_fresh_or_garbled_ones_stay()
    {
        var temp = Directory.CreateTempSubdirectory("c1813-age-").FullName;
        try
        {
            var dead = DeadPid(temp);
            var fresh = Seed(temp, $"antiphon-pwsh-cache-{dead}-{Guid.NewGuid():N}", null);
            var old = Seed(temp, $"antiphon-pwsh-cache-{dead}-{Guid.NewGuid():N}", null);
            var garbled = Seed(temp, $"antiphon-pwsh-cache-{dead}-{Guid.NewGuid():N}", dead.ToString(CultureInfo.InvariantCulture) + "x");
            var expired = DateTime.UtcNow.AddMinutes(-11);
            Directory.SetLastWriteTimeUtc(old, expired);
            Directory.SetLastWriteTimeUtc(garbled, expired);

            var seen = await ProbeAsync(temp, "age", static environment => environment.Remove("XDG_CACHE_HOME"));

            Directory.Exists(old).ShouldBeFalse(old);
            File.Exists(Path.Combine(fresh, "powershell", "StartupProfileData-NonInteractive")).ShouldBeTrue(fresh);
            File.Exists(Path.Combine(garbled, "powershell", "StartupProfileData-NonInteractive")).ShouldBeTrue(garbled);
            Directory.Exists(seen).ShouldBeFalse(seen);
        }
        finally
        {
            Directory.Delete(temp, recursive: true);
        }
    }

    // CARD-1813 A-3: pid reuse. The same live pid with a different start is not the owner.
    [Test]
    [Timeout(90_000)]
    public async Task A_live_pid_with_a_different_start_is_not_the_owner()
    {
        var temp = Directory.CreateTempSubdirectory("c1813-start-").FullName;
        try
        {
            var ticks = Process.GetCurrentProcess().StartTime.ToUniversalTime().Ticks;
            var normalized = ticks - ticks % 10;
            var pid = Environment.ProcessId.ToString(CultureInfo.InvariantCulture);
            var reused = Seed(temp, $"antiphon-pwsh-cache-start-{Guid.NewGuid():N}", pid + "@" + (normalized + 10_000_000).ToString(CultureInfo.InvariantCulture));
            var same = Seed(temp, $"antiphon-pwsh-cache-start-{Guid.NewGuid():N}", pid + "@" + normalized.ToString(CultureInfo.InvariantCulture));

            var seen = await ProbeAsync(temp, "start", static environment => environment.Remove("XDG_CACHE_HOME"));

            Directory.Exists(reused).ShouldBeFalse(reused);
            File.Exists(Path.Combine(same, "powershell", "StartupProfileData-NonInteractive")).ShouldBeTrue(same);
            Directory.Exists(seen).ShouldBeFalse(seen);
        }
        finally
        {
            Directory.Delete(temp, recursive: true);
        }
    }

    // CARD-1813 A-4: a stamp newer than 10 minutes skips the sweep.
    [Test]
    [Timeout(90_000)]
    public async Task A_fresh_sweep_stamp_keeps_a_dead_owner_cache()
    {
        var temp = Directory.CreateTempSubdirectory("c1813-stamp-").FullName;
        try
        {
            var dead = DeadPid(temp);
            var stale = Seed(temp, $"antiphon-pwsh-cache-{dead}-{Guid.NewGuid():N}", dead.ToString(CultureInfo.InvariantCulture));
            File.WriteAllText(Path.Combine(temp, "antiphon-pwsh-cache-sweep.stamp"), "swept");

            var seen = await ProbeAsync(temp, "stamp", static environment => environment.Remove("XDG_CACHE_HOME"));

            File.Exists(Path.Combine(stale, "powershell", "StartupProfileData-NonInteractive")).ShouldBeTrue(stale);
            Directory.Exists(seen).ShouldBeFalse(seen);
        }
        finally
        {
            Directory.Delete(temp, recursive: true);
        }
    }

    private static int DeadPid(string workingDirectory)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = workingDirectory,
        };
        start.ArgumentList.Add("--version");
        using var process = Process.Start(start) ?? throw new InvalidOperationException("dotnet --version did not start.");
        process.StandardOutput.ReadToEnd();
        process.StandardError.ReadToEnd();
        process.WaitForExit();
        return process.Id;
    }

    private static string Seed(string root, string name, string? owner)
    {
        var directory = Path.Combine(root, name);
        Directory.CreateDirectory(Path.Combine(directory, "powershell"));
        File.WriteAllText(Path.Combine(directory, "powershell", "StartupProfileData-NonInteractive"), "profile");
        if (owner is not null)
            File.WriteAllText(Path.Combine(directory, "owner.pid"), owner);
        return directory;
    }

    private static bool TryLink(string link, string target)
    {
        try
        {
            Directory.CreateSymbolicLink(link, target);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
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
