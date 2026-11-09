using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

[Category("Unit")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class PwshPrivateCacheTests
{
    private const string ProbeVariable = "ANTIPHON_PWSH_CACHE_PROBE";
    private static readonly object SeamGate = new();

    [Test]
    [Timeout(60_000)]
    public async Task Child_environment_carries_private_cache_under_temp_root()
    {
        var root = Environment.GetEnvironmentVariable("XDG_CACHE_HOME");
        root.ShouldNotBeNullOrWhiteSpace();
        var full = Path.GetFullPath(root);
        var parent = Antiphon.Hosting.PwshPrivateCache.ConfiguredCacheParent();
        Under(parent, full).ShouldBeTrue(full);
        Directory.Exists(full).ShouldBeTrue();
        var markerText = File.ReadAllText(Path.Combine(full, "owner.pid")).Trim();
        var parts = markerText.Split('@');
        parts[0].ShouldBe(Environment.ProcessId.ToString(CultureInfo.InvariantCulture));
        parts.Length.ShouldBe(2, markerText);
        if (OperatingSystem.IsLinux())
        {
            Antiphon.Hosting.PwshPrivateCache.TryReadKernelStartTicks(Environment.ProcessId, out var kernel).ShouldBeTrue(markerText);
            parts[1].ShouldBe("k" + kernel.ToString(CultureInfo.InvariantCulture), markerText);
        }
        else
        {
            var recorded = long.Parse(parts[1], CultureInfo.InvariantCulture);
            var observed = Process.GetCurrentProcess().StartTime.ToUniversalTime().Ticks;
            Math.Abs(recorded - observed).ShouldBeLessThanOrEqualTo(Antiphon.Hosting.PwshPrivateCache.StartMatchToleranceTicks);
        }
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
            start.Environment.Remove("ANTIPHON_PWSH_CACHE_ROOT");
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
            var pid = Environment.ProcessId.ToString(CultureInfo.InvariantCulture);
            string reusedToken;
            string sameToken;
            string? legacyToken = null;
            if (OperatingSystem.IsLinux())
            {
                Antiphon.Hosting.PwshPrivateCache.TryReadKernelStartTicks(Environment.ProcessId, out var kernel).ShouldBeTrue();
                var kernelText = kernel.ToString(CultureInfo.InvariantCulture);
                reusedToken = pid + "@k" + (kernel + 1).ToString(CultureInfo.InvariantCulture);
                sameToken = pid + "@k" + kernelText;
                var wall = Process.GetCurrentProcess().StartTime.ToUniversalTime().Ticks + TimeSpan.FromSeconds(1).Ticks;
                legacyToken = pid + "@" + wall.ToString(CultureInfo.InvariantCulture);
            }
            else
            {
                var ticks = Process.GetCurrentProcess().StartTime.ToUniversalTime().Ticks;
                reusedToken = pid + "@" + (ticks + TimeSpan.FromSeconds(1).Ticks).ToString(CultureInfo.InvariantCulture);
                sameToken = pid + "@" + ticks.ToString(CultureInfo.InvariantCulture);
            }

            var reused = Seed(temp, $"antiphon-pwsh-cache-start-{Guid.NewGuid():N}", reusedToken);
            var same = Seed(temp, $"antiphon-pwsh-cache-start-{Guid.NewGuid():N}", sameToken);
            var legacy = legacyToken is null ? null : Seed(temp, $"antiphon-pwsh-cache-start-{Guid.NewGuid():N}", legacyToken);

            var seen = await ProbeAsync(temp, "start", static environment => environment.Remove("XDG_CACHE_HOME"));

            Directory.Exists(reused).ShouldBeFalse(reused);
            File.Exists(Path.Combine(same, "powershell", "StartupProfileData-NonInteractive")).ShouldBeTrue(same);
            if (legacy is not null)
                File.Exists(Path.Combine(legacy, "powershell", "StartupProfileData-NonInteractive")).ShouldBeTrue(legacy);
            Directory.Exists(seen).ShouldBeFalse(seen);
        }
        finally
        {
            Directory.Delete(temp, recursive: true);
        }
    }

    // CARD-1813 review D-1: the recorded start is /proc stat ticks. A wall-clock value that
    // differs must not make that live owner look replaced.
    [Test]
    [Timeout(60_000)]
    public void Kernel_ticks_stay_live_when_the_wall_clock_differs()
    {
        lock (SeamGate)
        {
            var root = Directory.CreateTempSubdirectory("c1813-clock-").FullName;
            try
            {
                const long kernel = 1000;
                var wall = Process.GetCurrentProcess().StartTime.ToUniversalTime();
                var wallTicks = wall.Ticks - wall.Ticks % 10;
                var pid = Environment.ProcessId;
                var pidText = pid.ToString(CultureInfo.InvariantCulture);
                Antiphon.Hosting.PwshPrivateCache.StartIdentitySource = id => id == pid
                    ? Antiphon.Hosting.PwshPrivateCache.StartIdentity.Kernel(kernel, wallTicks)
                    : Antiphon.Hosting.PwshPrivateCache.StartIdentity.Absent;

                var owned = Path.Combine(root, "antiphon-pwsh-cache-clock-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(owned);
                Antiphon.Hosting.PwshPrivateCache.AssignOwner(owned, pid, wall);
                File.ReadAllText(Path.Combine(owned, "owner.pid")).Trim()
                    .ShouldBe(pidText + "@k" + kernel.ToString(CultureInfo.InvariantCulture));

                var mismatch = Seed(root, "antiphon-pwsh-cache-clock-" + Guid.NewGuid().ToString("N"), pidText + "@k" + (kernel + 1).ToString(CultureInfo.InvariantCulture));
                var legacy = Seed(root, "antiphon-pwsh-cache-clock-" + Guid.NewGuid().ToString("N"), pidText + "@" + wallTicks.ToString(CultureInfo.InvariantCulture));
                var pidOnly = Seed(root, "antiphon-pwsh-cache-clock-" + Guid.NewGuid().ToString("N"), pidText);
                var dead = DeadPid(root);
                var deadDir = Seed(root, "antiphon-pwsh-cache-clock-" + Guid.NewGuid().ToString("N"), dead.ToString(CultureInfo.InvariantCulture) + "@k" + kernel.ToString(CultureInfo.InvariantCulture));

                Antiphon.Hosting.PwshPrivateCache.SweepStale(root);

                Directory.Exists(owned).ShouldBeTrue(owned);
                Directory.Exists(mismatch).ShouldBeFalse(mismatch);
                File.Exists(Path.Combine(legacy, "powershell", "StartupProfileData-NonInteractive")).ShouldBeTrue(legacy);
                File.Exists(Path.Combine(pidOnly, "powershell", "StartupProfileData-NonInteractive")).ShouldBeTrue(pidOnly);
                Directory.Exists(deadDir).ShouldBeFalse(deadDir);
            }
            finally
            {
                Antiphon.Hosting.PwshPrivateCache.StartIdentitySource = null;
                Directory.Delete(root, recursive: true);
            }
        }
    }

    // CARD-1813 review D-2: a stamp dated in the future is not current, so sweeping stays on.
    [Test]
    [Timeout(90_000)]
    public async Task A_future_sweep_stamp_does_not_keep_a_dead_owner_cache()
    {
        var temp = Directory.CreateTempSubdirectory("c1813-future-").FullName;
        try
        {
            var dead = DeadPid(temp);
            var stale = Seed(temp, $"antiphon-pwsh-cache-{dead}-{Guid.NewGuid():N}", dead.ToString(CultureInfo.InvariantCulture));
            var stamp = Path.Combine(temp, "antiphon-pwsh-cache-sweep.stamp");
            File.WriteAllText(stamp, "future");
            File.SetLastWriteTimeUtc(stamp, DateTime.UtcNow.AddYears(1));

            var seen = await ProbeAsync(temp, "future", static environment => environment.Remove("XDG_CACHE_HOME"));

            Directory.Exists(stale).ShouldBeFalse(stale);
            var rewritten = File.GetLastWriteTimeUtc(stamp);
            rewritten.ShouldBeLessThan(DateTime.UtcNow.AddMinutes(1));
            rewritten.ShouldBeGreaterThan(DateTime.UtcNow.AddMinutes(-2));
            Directory.Exists(seen).ShouldBeFalse(seen);
        }
        finally
        {
            Directory.Delete(temp, recursive: true);
        }
    }

    // CARD-1813 review A-1: a symlink override skips the sweep instead of cleaning the real temp root.
    [Test]
    [Timeout(90_000)]
    public async Task An_unusable_root_override_skips_the_sweep()
    {
        var work = Directory.CreateTempSubdirectory("c1813-link-").FullName;
        try
        {
            var fallback = Path.Combine(work, "fallback");
            var target = Path.Combine(work, "target");
            var link = Path.Combine(work, "link");
            Directory.CreateDirectory(fallback);
            Directory.CreateDirectory(target);
            Directory.CreateSymbolicLink(link, target);
            var dead = DeadPid(work);
            var owner = dead.ToString(CultureInfo.InvariantCulture);
            var inFallback = Seed(fallback, $"antiphon-pwsh-cache-{dead}-{Guid.NewGuid():N}", owner);
            var inTarget = Seed(target, $"antiphon-pwsh-cache-{dead}-{Guid.NewGuid():N}", owner);

            var seen = await ProbeAsync(work, "link", environment =>
            {
                environment.Remove("XDG_CACHE_HOME");
                environment["TMPDIR"] = fallback;
                environment["TMP"] = fallback;
                environment["TEMP"] = fallback;
                environment["ANTIPHON_PWSH_CACHE_ROOT"] = link;
            });

            File.Exists(Path.Combine(inFallback, "powershell", "StartupProfileData-NonInteractive")).ShouldBeTrue(inFallback);
            File.Exists(Path.Combine(inTarget, "powershell", "StartupProfileData-NonInteractive")).ShouldBeTrue(inTarget);
            Under(fallback, seen).ShouldBeTrue(seen);
            Under(target, seen).ShouldBeFalse(seen);
        }
        finally
        {
            Directory.Delete(work, recursive: true);
        }
    }

    // CARD-1813 review A-2: a start read that throws is not proof the owner is dead.
    [Test]
    [Timeout(60_000)]
    public void An_unknown_start_read_keeps_the_owner_cache()
    {
        lock (SeamGate)
        {
            var root = Directory.CreateTempSubdirectory("c1813-unknown-").FullName;
            try
            {
                var pid = Environment.ProcessId;
                var wall = Process.GetCurrentProcess().StartTime.ToUniversalTime().Ticks + TimeSpan.FromHours(1).Ticks;
                Antiphon.Hosting.PwshPrivateCache.StartIdentitySource = id => id == pid
                    ? Antiphon.Hosting.PwshPrivateCache.StartIdentity.Unknown
                    : Antiphon.Hosting.PwshPrivateCache.StartIdentity.Absent;
                var kept = Seed(root, "antiphon-pwsh-cache-unknown-" + Guid.NewGuid().ToString("N"), pid.ToString(CultureInfo.InvariantCulture) + "@" + wall.ToString(CultureInfo.InvariantCulture));
                var dead = DeadPid(root);
                var deadDir = Seed(root, $"antiphon-pwsh-cache-{dead}-{Guid.NewGuid():N}", dead.ToString(CultureInfo.InvariantCulture));

                Antiphon.Hosting.PwshPrivateCache.SweepStale(root);

                File.Exists(Path.Combine(kept, "powershell", "StartupProfileData-NonInteractive")).ShouldBeTrue(kept);
                Directory.Exists(deadDir).ShouldBeFalse(deadDir);
            }
            finally
            {
                Antiphon.Hosting.PwshPrivateCache.StartIdentitySource = null;
                Directory.Delete(root, recursive: true);
            }
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

    // CARD-1813 round 2 A-1: comm may contain spaces and ')'. IndexOf(')') stops at the
    // first closer and rejects this line; LastIndexOf keeps field 22.
    [Test]
    [Timeout(30_000)]
    public void A_parenthesis_in_comm_keeps_the_start_field()
    {
        var stat = "42 (a) b) (c) S 1 1 1 0 -1 0 0 0 0 0 0 0 0 0 20 0 1 0 888 999";
        Antiphon.Hosting.PwshPrivateCache.TryParseProcStartTicks(stat, out var ticks).ShouldBeTrue(stat);
        ticks.ShouldBe(888);
    }

    // CARD-1813 round 2 A-2: ENOENT on stat while the pid is alive (hidepid, or no /proc)
    // is unknown, so the sweep keeps the cache. Master's kill(pid, 0) did the same on EPERM.
    [Test]
    [Timeout(60_000)]
    public void A_missing_stat_for_a_live_process_keeps_the_cache()
    {
        if (!OperatingSystem.IsLinux())
            throw new TUnit.Core.Exceptions.SkipTestException("A missing /proc stat while the pid is alive is Linux-only.");
        lock (SeamGate)
        {
            var root = Directory.CreateTempSubdirectory("c1813-live-stat-").FullName;
            try
            {
                var pid = Environment.ProcessId;
                Antiphon.Hosting.PwshPrivateCache.ProcStatReader = _ => throw new FileNotFoundException("stat");
                var kept = Seed(root, "antiphon-pwsh-cache-hid-" + Guid.NewGuid().ToString("N"), pid.ToString(CultureInfo.InvariantCulture));
                Antiphon.Hosting.PwshPrivateCache.SweepStale(root);
                File.Exists(Path.Combine(kept, "powershell", "StartupProfileData-NonInteractive")).ShouldBeTrue(kept);
            }
            finally
            {
                ClearProcSeams();
                Directory.Delete(root, recursive: true);
            }
        }
    }

    // The presence hook must win even when the real pid is already dead. Ignoring the hook
    // and calling kill deletes this cache.
    [Test]
    [Timeout(60_000)]
    public void A_hidden_proc_stat_keeps_a_live_owner()
    {
        if (!OperatingSystem.IsLinux())
            throw new TUnit.Core.Exceptions.SkipTestException("hidepid stat absence is Linux-only.");
        lock (SeamGate)
        {
            var root = Directory.CreateTempSubdirectory("c1813-hide-stat-").FullName;
            try
            {
                var dead = DeadPid(root);
                Antiphon.Hosting.PwshPrivateCache.ProcStatReader = _ => throw new FileNotFoundException("stat");
                Antiphon.Hosting.PwshPrivateCache.LinuxProcessPresence = _ => true;
                var kept = Seed(root, "antiphon-pwsh-cache-eperm-" + Guid.NewGuid().ToString("N"), dead.ToString(CultureInfo.InvariantCulture));
                Antiphon.Hosting.PwshPrivateCache.SweepStale(root);
                File.Exists(Path.Combine(kept, "powershell", "StartupProfileData-NonInteractive")).ShouldBeTrue(kept);
            }
            finally
            {
                ClearProcSeams();
                Directory.Delete(root, recursive: true);
            }
        }
    }

    // kill(pid, 0) EPERM for another user's live pid, with the stat unreadable, keeps the cache.
    // ESRCH is the only errno that means gone. Success is covered by the live-process witness.
    [Test]
    [Timeout(60_000)]
    public void A_eperm_for_a_live_foreign_pid_keeps_the_cache()
    {
        if (!OperatingSystem.IsLinux())
            throw new TUnit.Core.Exceptions.SkipTestException("kill(pid, 0) EPERM is Linux-only.");
        lock (SeamGate)
        {
            var root = Directory.CreateTempSubdirectory("c1813-eperm-").FullName;
            try
            {
                const int foreign = 1;
                _ = Process.GetProcessById(foreign);
                RequireEperm(foreign);
                Antiphon.Hosting.PwshPrivateCache.ProcStatReader = _ => throw new FileNotFoundException("stat");
                var kept = Seed(root, "antiphon-pwsh-cache-foreign-" + Guid.NewGuid().ToString("N"), foreign.ToString(CultureInfo.InvariantCulture));
                Antiphon.Hosting.PwshPrivateCache.SweepStale(root);
                File.Exists(Path.Combine(kept, "powershell", "StartupProfileData-NonInteractive")).ShouldBeTrue(kept);
            }
            finally
            {
                ClearProcSeams();
                Directory.Delete(root, recursive: true);
            }
        }
    }

    // A missing stat for a pid that kill(pid, 0) reports gone is still dead and is swept.
    [Test]
    [Timeout(60_000)]
    public void A_missing_stat_for_a_dead_process_is_swept()
    {
        if (!OperatingSystem.IsLinux())
            throw new TUnit.Core.Exceptions.SkipTestException("A missing /proc stat is Linux-only.");
        lock (SeamGate)
        {
            var root = Directory.CreateTempSubdirectory("c1813-dead-stat-").FullName;
            try
            {
                var dead = DeadPid(root);
                Antiphon.Hosting.PwshPrivateCache.ProcStatReader = _ => throw new FileNotFoundException("stat");
                var stale = Seed(root, "antiphon-pwsh-cache-gone-" + Guid.NewGuid().ToString("N"), dead.ToString(CultureInfo.InvariantCulture));
                Antiphon.Hosting.PwshPrivateCache.SweepStale(root);
                Directory.Exists(stale).ShouldBeFalse(stale);
            }
            finally
            {
                ClearProcSeams();
                Directory.Delete(root, recursive: true);
            }
        }
    }

    // CARD-1813 round 2 A-3: a relative override is unusable. The sweep does not follow it,
    // and the cache is created under the process temp root.
    [Test]
    [Timeout(90_000)]
    public async Task A_relative_root_override_is_unusable()
    {
        var work = Directory.CreateTempSubdirectory("c1813-rel-").FullName;
        var rel = Path.Combine(work, "rel-cache");
        var fallback = Path.Combine(work, "fallback");
        Directory.CreateDirectory(rel);
        Directory.CreateDirectory(fallback);
        try
        {
            var dead = DeadPid(work);
            var owner = dead.ToString(CultureInfo.InvariantCulture);
            var inRelative = Seed(rel, $"antiphon-pwsh-cache-{dead}-{Guid.NewGuid():N}", owner);
            var inFallback = Seed(fallback, $"antiphon-pwsh-cache-{dead}-{Guid.NewGuid():N}", owner);
            var seen = await ProbeAsync(work, "rel", environment =>
            {
                environment.Remove("XDG_CACHE_HOME");
                environment["TMPDIR"] = fallback;
                environment["TMP"] = fallback;
                environment["TEMP"] = fallback;
                environment["ANTIPHON_PWSH_CACHE_ROOT"] = "rel-cache";
            });

            File.Exists(Path.Combine(inRelative, "powershell", "StartupProfileData-NonInteractive")).ShouldBeTrue(inRelative);
            File.Exists(Path.Combine(inFallback, "powershell", "StartupProfileData-NonInteractive")).ShouldBeTrue(inFallback);
            Under(fallback, seen).ShouldBeTrue(seen);
            Under(rel, seen).ShouldBeFalse(seen);
        }
        finally
        {
            Directory.Delete(work, recursive: true);
        }
    }

    // A missing absolute override is not created, is not swept, and is not the create root.
    [Test]
    [Timeout(90_000)]
    public async Task A_missing_root_override_is_not_created()
    {
        var work = Directory.CreateTempSubdirectory("c1813-miss-").FullName;
        var missing = Path.Combine(work, "missing-root");
        var fallback = Path.Combine(work, "fallback");
        Directory.CreateDirectory(fallback);
        try
        {
            var dead = DeadPid(work);
            var kept = Seed(fallback, $"antiphon-pwsh-cache-{dead}-{Guid.NewGuid():N}", dead.ToString(CultureInfo.InvariantCulture));
            var seen = await ProbeAsync(work, "missing", environment =>
            {
                environment.Remove("XDG_CACHE_HOME");
                environment["TMPDIR"] = fallback;
                environment["TMP"] = fallback;
                environment["TEMP"] = fallback;
                environment["ANTIPHON_PWSH_CACHE_ROOT"] = missing;
            });

            Directory.Exists(missing).ShouldBeFalse(missing);
            File.Exists(Path.Combine(kept, "powershell", "StartupProfileData-NonInteractive")).ShouldBeTrue(kept);
            Under(fallback, seen).ShouldBeTrue(seen);
        }
        finally
        {
            Directory.Delete(work, recursive: true);
        }
    }

    // CARD-1813 round 2 D-1: the three cache-parent witnesses share ConfiguredCacheParent.
    // A hard-coded temp parent in any of them fails this scan.
    [Test]
    [Timeout(30_000)]
    public void Cache_parent_lookups_use_the_shared_helper()
    {
        var root = RepositoryRoot();
        var files = new[]
        {
            Path.Combine(root, "tests", "Antiphon.Tests", "Scripts", "PwshPrivateCacheTests.cs"),
            Path.Combine(root, "tests", "Antiphon.Tests", "Scripts", "ScriptHarnessLinuxOwnershipTests.cs"),
            Path.Combine(root, "tests", "Antiphon.SessionRunner.Tests", "C1793SessionPwshCacheTests.cs"),
        };
        var hardCodedDirectories = "GetDirectories(" + "Path.GetTempPath()";
        var hardCodedFull = "GetFullPath(" + "Path.GetTempPath())";
        foreach (var file in files)
        {
            var text = File.ReadAllText(file);
            text.Contains("ConfiguredCacheParent()", StringComparison.Ordinal).ShouldBeTrue(file);
            text.Contains(hardCodedDirectories, StringComparison.Ordinal).ShouldBeFalse(file);
            text.Contains(hardCodedFull, StringComparison.Ordinal).ShouldBeFalse(file);
        }
    }

    private const int Eperm = 1;

    [DllImport("libc", EntryPoint = "kill", SetLastError = true)]
    private static extern int ProbeKill(int pid, int signal);

    private static void RequireEperm(int pid)
    {
        if (ProbeKill(pid, 0) == 0)
            throw new InvalidOperationException($"This host can signal pid {pid}, so kill did not return EPERM.");
        var errno = Marshal.GetLastPInvokeError();
        if (errno != Eperm)
            throw new InvalidOperationException($"kill({pid}, 0) errno {errno} is not EPERM.");
    }

    private static void ClearProcSeams()
    {
        Antiphon.Hosting.PwshPrivateCache.ProcStatReader = null;
        Antiphon.Hosting.PwshPrivateCache.LinuxProcessPresence = null;
    }

    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Antiphon.sln")))
                return dir.FullName;
            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("Antiphon.sln was not found above the test host.");
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
        start.Environment.Remove("ANTIPHON_PWSH_CACHE_ROOT");
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
