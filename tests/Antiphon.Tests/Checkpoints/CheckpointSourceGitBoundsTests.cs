using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Antiphon.Checkpoints;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;
using Antiphon.TestSupport;

namespace Antiphon.Tests.Checkpoints;

[NativeLinuxOnly("Linux inherited-pipe custody proof")]
[Category("Integration")]
[Category("Slow")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class CheckpointSourceGitBoundsTests : CheckpointTestBase
{
    [Test]
    [Timeout(120_000)]
    public async Task C1233_ToolSource(CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsLinux())
            Skip.Test("Linux inherited-pipe custody proof");

        var healthy = await CaptureAsync("healthy", cancellationToken);
        healthy.Watchdog.ShouldBeFalse();
        healthy.Observation!.CaptureStatus.ShouldBe("known");
        healthy.Observation.Commit.ShouldBe(new string('a', 40));
        healthy.Observation.DirtyFiles.ShouldBe(2);
        // SHA-256 of v1's eight-byte big-endian length frames: version, HEAD,
        // status, HEAD diff, cached diff, raw.bin path and SHA-256(raw.bin).
        healthy.Observation.Fingerprint.ShouldBe("a8772ccdfabb7a27dac404c71c7fb916415e53bf40f4cc64436440be3697773b",
            "raw diff bytes (including invalid UTF-8), NUL paths, CRLF and binary data must retain v1 identity");

        var blocked = await CaptureAsync("held-pipe", cancellationToken);
        blocked.Watchdog.ShouldBeFalse("default SourceSnapshot Git drain required the independent 35 s watchdog");
        blocked.Milliseconds.ShouldBeLessThan(32_000, "the real 30 s Git budget includes inherited pipe drains");
        blocked.Observation!.CaptureStatus.ShouldBe("unknown");
        blocked.Observation.ErrorCode.ShouldBe("git_timeout");
        blocked.ParentExited.ShouldBeTrue("the selected Git parent must exit before its descendant releases stderr");
        blocked.DescendantDead.ShouldBeTrue("the pipe holder survived the Git deadline and 2 s death observation");

        var nonzero = await CaptureAsync("nonzero", cancellationToken);
        nonzero.Watchdog.ShouldBeFalse();
        nonzero.Observation!.CaptureStatus.ShouldBe("unknown");
        nonzero.Observation.ErrorCode.ShouldBe("git_exit");
    }

    [Test]
    [Timeout(120_000)]
    public async Task C1233_ScriptSource(CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsLinux())
            Skip.Test("Linux inherited-pipe custody proof");

        foreach (var caller in new[] { "tool", "script" })
        {
            var healthy = await CaptureAsync("healthy", cancellationToken, caller);
            healthy.Watchdog.ShouldBeFalse();
            healthy.Observation!.CaptureStatus.ShouldBe("known");
            healthy.Observation.Commit.ShouldBe(new string('a', 40));
            healthy.Observation.DirtyFiles.ShouldBe(2);
            healthy.Observation.Fingerprint.ShouldBe("a8772ccdfabb7a27dac404c71c7fb916415e53bf40f4cc64436440be3697773b",
                $"{caller} must retain the independent v1 raw-byte vector, including invalid UTF-8 diff bytes");
        }

        var nonzero = await CaptureAsync("nonzero", cancellationToken, "script");
        nonzero.Watchdog.ShouldBeFalse();
        nonzero.Observation!.CaptureStatus.ShouldBe("unknown");
        nonzero.Observation.ErrorCode.ShouldBe("git_exit");

        var blocked = await CaptureAsync("held-pipe", cancellationToken, "script");
        blocked.Watchdog.ShouldBeFalse("Get-CheckpointSource Git drain required the independent 35 s watchdog");
        blocked.Milliseconds.ShouldBeLessThan(32_000, "the real 30 s Git budget includes inherited pipe drains");
        blocked.Observation!.CaptureStatus.ShouldBe("unknown");
        blocked.Observation.ErrorCode.ShouldBe("git_timeout");
        blocked.ParentExited.ShouldBeTrue("the selected Git parent must exit before its descendant releases stderr");
        blocked.DescendantDead.ShouldBeTrue("the pipe holder survived the Git deadline and 2 s death observation");
    }

    private sealed record CaptureResult(SourceObservation? Observation, long Milliseconds,
        bool Watchdog, bool ParentExited, bool DescendantDead);

    private async Task<CaptureResult> CaptureAsync(string mode, CancellationToken cancellationToken,
        string caller = "tool")
    {
        var root = TempDir();
        var shims = Directory.CreateDirectory(Path.Combine(root, "shims")).FullName;
        var nonce = Guid.NewGuid().ToString("N");
        File.WriteAllBytes(Path.Combine(root, "status"), Encoding.UTF8.GetBytes(" M café.txt\0?? raw.bin\0"));
        File.WriteAllBytes(Path.Combine(root, "head.diff"),
            [.. Encoding.UTF8.GetBytes("diff --git a/café.txt b/café.txt\r\n+"), 0xff, 0xfe, 0, 13, 10]);
        File.WriteAllBytes(Path.Combine(root, "cached.diff"),
            [.. Encoding.UTF8.GetBytes("GIT binary patch\r\n"), 0, 0x80, 0xff, 13, 10]);
        File.WriteAllBytes(Path.Combine(root, "raw.bin"), [0, 0xff, 0x80, 13, 10, 65]);
        CheckpointBoundedProbe.Fifo(Path.Combine(root, "gate"));
        CheckpointBoundedProbe.Executable(Path.Combine(root, "holder"), """
            #!/bin/sh
            set -eu
            IFS= read -r stat < /proc/$$/stat
            stat=${stat##*) }
            set -- $stat
            shift 19
            printf '%s:%s:%s\n' "$$" "$1" "$C1233_NONCE" > "$C1233_ROOT/holder.identity"
            exec /bin/cat "$C1233_ROOT/gate" >/dev/null
            """);
        CheckpointBoundedProbe.Executable(Path.Combine(shims, "git"), """
            #!/bin/sh
            set -eu
            while [ "$1" = -c ]; do shift 2; done
            case "$1" in
              config)
                printf 'filter.fixture.clean\nignored\000filter.fixture.required\ntrue\000diff.fixture.textconv\nignored\000'
                ;;
              rev-parse)
                if [ "$2" = --show-toplevel ]; then
                  printf '%s\n' "$C1233_ROOT"
                else
                  printf 'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\n'
                fi
                ;;
              status)
                if [ "$C1233_MODE" = held-pipe ]; then
                  IFS= read -r stat < /proc/$$/stat
                  stat=${stat##*) }
                  set -- $stat
                  shift 19
                  printf '%s:%s:%s\n' "$$" "$1" "$C1233_NONCE" > "$C1233_ROOT/parent.identity"
                  "$C1233_ROOT/holder" &
                  exit 0
                fi
                /bin/cat "$C1233_ROOT/status"
                ;;
              ls-files) printf 'H café.txt\000' ;;
              diff)
                if [ "$C1233_MODE" = nonzero ]; then printf 'fixture failure\n' >&2; exit 7; fi
                if [ "$2" = --cached ]; then
                  /bin/cat "$C1233_ROOT/cached.diff"
                else
                  /bin/cat "$C1233_ROOT/head.diff"
                fi
                ;;
              *) exit 91 ;;
            esac
            """);
        var wrapper = Path.Combine(root, "capture.ps1");
        File.WriteAllText(wrapper, """
            $ErrorActionPreference = 'Stop'
            if ($env:C1233_CALLER -eq 'script') {
                . $env:C1233_SOURCE_SCRIPT
            } else {
                $null = [Reflection.Assembly]::LoadFrom($env:C1233_HOST_ASSEMBLY)
                $null = [Reflection.Assembly]::LoadFrom($env:C1233_TOOL_ASSEMBLY)
                $reader = [Antiphon.Checkpoints.SourceSnapshot]::new()
            }
            $stat = [IO.File]::ReadAllText("/proc/$PID/stat")
            $startToken = $stat.Substring($stat.LastIndexOf(')') + 2).Split(' ')[19]
            [Console]::Out.WriteLine("C1233_READY $PID`:$startToken`:$env:C1233_NONCE")
            $clock = [Diagnostics.Stopwatch]::StartNew()
            $observation = if ($env:C1233_CALLER -eq 'script') {
                Get-CheckpointSource $env:C1233_ROOT
            } else {
                $reader.Capture($env:C1233_ROOT)
            }
            $clock.Stop()
            [Console]::Out.WriteLine("C1233_RESULT $env:C1233_NONCE " + (@{
                observation = $observation; milliseconds = $clock.ElapsedMilliseconds
            } | ConvertTo-Json -Depth 5 -Compress))
            """);
        var start = new ProcessStartInfo("pwsh")
        {
            WorkingDirectory = root, UseShellExecute = false,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        foreach (var arg in new[] { "-NoProfile", "-NonInteractive", "-File", wrapper })
            start.ArgumentList.Add(arg);
        start.Environment["PATH"] = shims + Path.PathSeparator + start.Environment["PATH"];
        start.Environment["C1233_ROOT"] = root;
        start.Environment["C1233_NONCE"] = nonce;
        start.Environment["C1233_MODE"] = mode;
        start.Environment["C1233_CALLER"] = caller;
        start.Environment["C1233_SOURCE_SCRIPT"] = Path.Combine(CheckpointFixtures.RepoRoot,
            "scripts", "lib", "checkpoint-source.ps1");
        start.Environment["C1233_HOST_ASSEMBLY"] = typeof(CheckpointProcessRunner).Assembly.Location;
        start.Environment["C1233_TOOL_ASSEMBLY"] = typeof(SourceSnapshot).Assembly.Location;
        using var process = Process.Start(start)!;
        RegisterCheckpointChild(process);
        var wrapperIdentity = Identity(process.Id, nonce);
        var error = process.StandardError.ReadToEndAsync(cancellationToken);
        Task? completion = null;
        try
        {
            var ready = await process.StandardOutput.ReadLineAsync(cancellationToken).AsTask()
                .WaitAsync(TimeSpan.FromSeconds(20), cancellationToken);
            ready.ShouldBe("C1233_READY " + wrapperIdentity, "capture readiness is bound to the child PID/start/nonce");
            var output = process.StandardOutput.ReadToEndAsync(cancellationToken);
            completion = Task.WhenAll(process.WaitForExitAsync(cancellationToken), output, error);
            var watchdog = false;
            try { await completion.WaitAsync(TimeSpan.FromSeconds(35), cancellationToken); }
            catch (TimeoutException) { watchdog = true; }

            var parentExited = false;
            var descendantDead = false;
            if (mode == "held-pipe")
            {
                var parent = ReadIdentity("parent");
                var holder = ReadIdentity("holder");
                parentExited = !Alive(parent);
                var death = Stopwatch.StartNew();
                while (Alive(holder) && death.ElapsedMilliseconds < 2_000)
                    await Task.Delay(20, cancellationToken);
                descendantDead = !Alive(holder);
            }
            if (watchdog)
                return new CaptureResult(null, 0, true, parentExited, descendantDead);

            process.ExitCode.ShouldBe(0, await error);
            var prefix = $"C1233_RESULT {nonce} ";
            var result = (await output).Trim();
            result.ShouldStartWith(prefix);
            using var json = JsonDocument.Parse(result[prefix.Length..]);
            var observation = json.RootElement.GetProperty("observation").Deserialize<SourceObservation>(
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            var milliseconds = json.RootElement.GetProperty("milliseconds").GetInt64();
            Console.WriteLine($"{caller} {mode}: {milliseconds} ms; {observation!.CaptureStatus}/{observation.ErrorCode}; parentExited={parentExited}; descendantDead={descendantDead}");
            return new CaptureResult(observation, milliseconds, false, parentExited, descendantDead);
        }
        finally
        {
            foreach (var name in new[] { "holder", "parent" })
            {
                var file = Path.Combine(root, name + ".identity");
                if (File.Exists(file))
                {
                    var identity = ReadIdentity(name);
                    if (Alive(identity))
                    {
                        using var child = Process.GetProcessById(int.Parse(identity.Split(':')[0]));
                        child.Kill();
                    }
                }
            }
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(2));
            if (completion is not null)
                await completion.WaitAsync(TimeSpan.FromSeconds(2));
        }

        string ReadIdentity(string name)
        {
            var identity = File.ReadAllText(Path.Combine(root, name + ".identity")).Trim();
            identity.Split(':').Length.ShouldBe(3);
            identity.ShouldEndWith(":" + nonce);
            return identity;
        }
    }

    private static string Identity(int pid, string nonce)
    {
        var stat = File.ReadAllText($"/proc/{pid}/stat");
        return $"{pid}:{stat[(stat.LastIndexOf(')') + 2)..].Split(' ')[19]}:{nonce}";
    }

    private static bool Alive(string identity)
    {
        var parts = identity.Split(':');
        try
        {
            var stat = File.ReadAllText($"/proc/{int.Parse(parts[0])}/stat");
            var fields = stat[(stat.LastIndexOf(')') + 2)..].Split(' ');
            return fields[19] == parts[1] && fields[0] != "Z";
        }
        catch (IOException) { return false; }
    }
}
