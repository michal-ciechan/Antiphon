using System.Diagnostics;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;
using Antiphon.TestSupport;

namespace Antiphon.Tests.Checkpoints;

[NativeLinuxOnly("CARD-1233 S8 requires Linux process identities and FIFO probes.")]
[Category("Integration")]
[Category("Slow")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class CheckpointFixtureRoutingTests : CheckpointTestBase
{
    [Test]
    [Arguments("F1")]
    [Arguments("F2")]
    [Arguments("F3")]
    [Arguments("F9")]
    [Timeout(60_000)]
    public async Task C1233_TokenCallers(string site, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsLinux())
            Skip.Test("CARD-1233 S8 requires Linux process identities and FIFO probes.");

        CheckpointTestBase owner = site switch
        {
            "F1" or "F9" => new CheckpointOutputConfinementTests(),
            "F2" => new CheckpointShapeConflictTests(),
            "F3" => new CheckpointTrackedBaselineTests(),
            _ => throw new ArgumentOutOfRangeException(nameof(site)),
        };
        owner.OpenCheckpointScope();
        try
        {
            var nonce = Guid.NewGuid().ToString("N");
            var healthy = await InvokeAsync(owner, site,
                Shell($"printf 'stdout-{nonce}'; printf 'stderr-{nonce}' >&2; exit 23"), cancellationToken);
            healthy.Output.ShouldBe($"stdout-{nonce}stderr-{nonce}");
            if (site is "F2" or "F3")
                healthy.Exit.ShouldBe(23);

            await AssertCancellationAsync(owner, site, parentExits: false, cancellationToken);
            await AssertCancellationAsync(owner, site, parentExits: true, cancellationToken);
        }
        finally
        {
            await owner.CloseCheckpointScopeAsync();
        }
    }

    private async Task AssertCancellationAsync(CheckpointTestBase owner, string site,
        bool parentExits, CancellationToken cancellationToken)
    {
        var root = TempDir();
        var nonce = Guid.NewGuid().ToString("N");
        var parentFile = Path.Combine(root, "parent.ready");
        var childFile = Path.Combine(root, "child.ready");
        var exitFile = Path.Combine(root, "parent-exiting");
        var gate = Path.Combine(root, "gate");
        CheckpointBoundedProbe.Fifo(gate);
        var start = Shell("""
            token=$(awk '{print $22}' "/proc/$$/stat")
            printf '%s:%s:%s\n' "$$" "$token" "$C1233_NONCE" > "$C1233_PARENT.tmp"
            mv "$C1233_PARENT.tmp" "$C1233_PARENT"
            /bin/sh -c '
                token=$(awk "{print \$22}" "/proc/$$/stat")
                printf "%s:%s:%s\n" "$$" "$token" "$C1233_NONCE" > "$C1233_CHILD.tmp"
                mv "$C1233_CHILD.tmp" "$C1233_CHILD"
                exec /bin/cat "$C1233_GATE" >/dev/null
            ' &
            while [ ! -s "$C1233_CHILD" ]; do sleep 0.01; done
            if [ "$C1233_PARENT_EXITS" = 1 ]; then
                printf '%s' "$C1233_NONCE" > "$C1233_EXIT"
                exit 0
            fi
            wait
            """);
        start.Environment["C1233_NONCE"] = nonce;
        start.Environment["C1233_PARENT"] = parentFile;
        start.Environment["C1233_CHILD"] = childFile;
        start.Environment["C1233_GATE"] = gate;
        start.Environment["C1233_EXIT"] = exitFile;
        start.Environment["C1233_PARENT_EXITS"] = parentExits ? "1" : "0";
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var clock = Stopwatch.StartNew();
        limit.CancelAfter(2_000);
        var call = CaptureAsync(() => InvokeAsync(owner, site, start, limit.Token));
        var watchdog = false;
        try
        {
            Exception? error = null;
            try { error = await call.WaitAsync(TimeSpan.FromSeconds(8), cancellationToken); }
            catch (TimeoutException) { watchdog = true; }
            var elapsed = clock.ElapsedMilliseconds;
            var parent = ReadIdentity(parentFile, nonce);
            var child = ReadIdentity(childFile, nonce);
            if (parentExits)
                File.ReadAllText(exitFile).ShouldBe(nonce, "the direct parent must reach its exit before cancellation");
            var dead = await WaitDeadAsync([parent, child], cancellationToken);

            watchdog.ShouldBeFalse("the independent 8 s watchdog must not rescue the actual caller");
            error.ShouldBeAssignableTo<OperationCanceledException>();
            ((OperationCanceledException)error!).CancellationToken.ShouldBe(limit.Token);
            elapsed.ShouldBeLessThan(4_000);
            dead.ShouldBeTrue($"{site}: cancellation left an owned child alive (parentExits={parentExits})");
        }
        finally
        {
            // Rescue only exact recorded identities, after the survivor observation.
            KillRecorded(parentFile, nonce);
            KillRecorded(childFile, nonce);
            await call.WaitAsync(TimeSpan.FromSeconds(2), CancellationToken.None);
        }
    }

    private static async Task<(int? Exit, string Output)> InvokeAsync(CheckpointTestBase owner, string site,
        ProcessStartInfo start, CancellationToken cancellationToken)
    {
        switch (site)
        {
            case "F1":
                return (null, await ((CheckpointOutputConfinementTests)owner).RunScriptAsync(start, cancellationToken));
            case "F9":
                return (null, await ((CheckpointOutputConfinementTests)owner).RunConfinedScriptAsync(start, cancellationToken));
            case "F2":
                var shape = await ((CheckpointShapeConflictTests)owner).WaitAsync(start, cancellationToken);
                return (shape.Exit, shape.Output);
            case "F3":
                var tracked = await ((CheckpointTrackedBaselineTests)owner).WaitAsync(start, cancellationToken);
                return (tracked.Exit, tracked.Output);
            default:
                throw new ArgumentOutOfRangeException(nameof(site));
        }
    }

    private static async Task<Exception?> CaptureAsync(Func<Task<(int? Exit, string Output)>> action)
    {
        try { await action(); return null; }
        catch (Exception ex) { return ex; }
    }

    private static ProcessStartInfo Shell(string script)
    {
        var start = new ProcessStartInfo("/bin/sh")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        start.ArgumentList.Add("-c");
        start.ArgumentList.Add(script);
        return start;
    }

    private sealed record Identity(int Pid, string Start);

    private static Identity ReadIdentity(string path, string nonce)
    {
        File.Exists(path).ShouldBeTrue("the real child must publish readiness before cancellation");
        var parts = File.ReadAllText(path).Trim().Split(':');
        parts.Length.ShouldBe(3);
        parts[2].ShouldBe(nonce);
        return new Identity(int.Parse(parts[0]), parts[1]);
    }

    private static bool Alive(Identity identity)
    {
        try
        {
            var stat = File.ReadAllText($"/proc/{identity.Pid}/stat");
            var fields = stat[(stat.LastIndexOf(')') + 2)..].Split(' ');
            return fields[19] == identity.Start && fields[0] != "Z";
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException) { return false; }
    }

    private static async Task<bool> WaitDeadAsync(Identity[] identities, CancellationToken cancellationToken)
    {
        var clock = Stopwatch.StartNew();
        while (identities.Any(Alive) && clock.ElapsedMilliseconds < 2_000)
            await Task.Delay(25, cancellationToken);
        return !identities.Any(Alive);
    }

    private static void KillRecorded(string path, string nonce)
    {
        if (!File.Exists(path)) return;
        var identity = ReadIdentity(path, nonce);
        if (!Alive(identity)) return;
        try
        {
            using var process = Process.GetProcessById(identity.Pid);
            if (!Alive(identity)) return;
            process.Kill();
            process.WaitForExit(1_000);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException) { }
    }
}
