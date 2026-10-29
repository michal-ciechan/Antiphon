using System.Diagnostics;
using System.Text.Json;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Checkpoints;

[Category("Integration")]
[Category("Slow")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class CheckpointVerifierBoundsTests : CheckpointTestBase
{
    [Test]
    [Arguments("temp")]
    [Arguments("orphan")]
    [Timeout(120_000)]
    public async Task C1233_VerifierGit(string caller, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsLinux())
            Skip.Test("Linux inherited-pipe custody proof");

        var healthy = await InvokeAsync(caller, "healthy", cancellationToken);
        healthy.Watchdog.ShouldBeFalse();
        var admitted = caller == "temp"
            ? "Prebuilt test DLL missing:" : "Prebuilt lifecycle or checkpoint tool DLL is missing.";
        healthy.Error.ShouldStartWith(admitted, Case.Sensitive,
            "a valid SHA must reach the next production admission check without running a usage suite");
        foreach (var mode in new[] { "invalid", "nonzero" })
        {
            var refused = await InvokeAsync(caller, mode, cancellationToken);
            refused.Watchdog.ShouldBeFalse();
            refused.Error.ShouldBe("Cannot resolve source SHA.", mode);
        }

        var blocked = await InvokeAsync(caller, "held-pipe", cancellationToken);
        blocked.Watchdog.ShouldBeFalse($"{caller} SHA resolution required the independent 35 s watchdog");
        blocked.Milliseconds.ShouldBeLessThan(32_000, "the real 30 s Git budget includes both output drains");
        blocked.Error.ShouldBe("Cannot resolve source SHA.");
        blocked.ParentExited.ShouldBeTrue("the selected Git parent exited while its descendant retained both pipes");
        blocked.DescendantDead.ShouldBeTrue("the pipe holder survived the deadline and 2 s death observation");

        var hardened = await InvokeAsync(caller, "hardened", cancellationToken);
        hardened.Watchdog.ShouldBeFalse();
        hardened.Error.ShouldStartWith(admitted, Case.Sensitive,
            "read-only Git must suppress ambient config, hooks, filters and textconv before resolving HEAD");
    }

    private sealed record Observation(string Error, long Milliseconds, bool Watchdog,
        bool ParentExited, bool DescendantDead);

    private async Task<Observation> InvokeAsync(string caller, string mode, CancellationToken cancellationToken)
    {
        var root = TempDir();
        var scripts = Directory.CreateDirectory(Path.Combine(root, "scripts")).FullName;
        Directory.CreateDirectory(Path.Combine(scripts, "lib"));
        var shims = Directory.CreateDirectory(Path.Combine(root, "shims")).FullName;
        var nonce = Guid.NewGuid().ToString("N");
        foreach (var relative in new[] { $"verify-checkpoint-{caller}-usage.ps1",
                     "lib/checkpoint-file-reader.ps1", "lib/checkpoint-process-runner.ps1",
                     "lib/CheckpointProcessRunner.cs" })
            File.Copy(Path.Combine(CheckpointFixtures.RepoRoot, "scripts", relative), Path.Combine(scripts, relative));
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
            settings='|'
            while [ "$1" = -c ] || [ "$1" = -C ]; do
              if [ "$1" = -c ]; then settings="$settings$2|"; fi
              shift 2
            done
            if [ "$C1233_MODE" = hardened ]; then
              [ "${GIT_OPTIONAL_LOCKS-}" = 0 ] && [ "${GIT_NO_REPLACE_OBJECTS-}" = 1 ] &&
              [ "${GIT_CONFIG_NOSYSTEM-}" = 1 ] && [ "${GIT_CONFIG_GLOBAL-}" = /dev/null ] &&
              [ -z "${GIT_CONFIG_COUNT-}" ] && [ -z "${GIT_CONFIG_PARAMETERS-}" ] &&
              [ -z "${GIT_DIR-}" ] && [ -z "${GIT_WORK_TREE-}" ] && [ -z "${GIT_INDEX_FILE-}" ] ||
                exit 90
              for setting in core.fsmonitor=false core.hooksPath=/dev/null diff.external= core.attributesFile=/dev/null gc.auto=0 maintenance.auto=false; do
                case "$settings" in *"|$setting|"*) ;; *) exit 91 ;; esac
              done
            fi
            case "$1" in
              config)
                printf 'filter.fixture.clean\nignored\000filter.fixture.required\ntrue\000diff.fixture.textconv\nignored\000'
                ;;
              rev-parse)
                [ "$2" = HEAD ] || exit 92
                [ "$PWD" = "$C1233_ROOT" ] || exit 93
                case "$C1233_MODE" in
                  held-pipe)
                    IFS= read -r stat < /proc/$$/stat
                    stat=${stat##*) }
                    set -- $stat
                    shift 19
                    printf '%s:%s:%s\n' "$$" "$1" "$C1233_NONCE" > "$C1233_ROOT/parent.identity"
                    "$C1233_ROOT/holder" &
                    exit 0
                    ;;
                  invalid) printf 'invalid-sha\n'; exit 0 ;;
                  nonzero) printf 'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\n'; exit 7 ;;
                  hardened)
                    for setting in filter.fixture.clean= filter.fixture.required=false diff.fixture.textconv=; do
                      case "$settings" in *"|$setting|"*) ;; *) exit 94 ;; esac
                    done
                    ;;
                esac
                printf '  aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa  \n'
                ;;
              *) exit 95 ;;
            esac
            """);
        var wrapper = Path.Combine(root, "invoke.ps1");
        File.WriteAllText(wrapper, """
            $ErrorActionPreference = 'Stop'
            . "$PSScriptRoot/scripts/lib/checkpoint-process-runner.ps1"
            $stat = [IO.File]::ReadAllText("/proc/$PID/stat")
            $startToken = $stat.Substring($stat.LastIndexOf(')') + 2).Split(' ')[19]
            [Console]::Out.WriteLine("C1233_READY $PID`:$startToken`:$env:C1233_NONCE")
            $clock = [Diagnostics.Stopwatch]::StartNew()
            $failure = ''
            try {
                if ($env:C1233_CALLER -eq 'temp') {
                    & "$PSScriptRoot/scripts/verify-checkpoint-temp-usage.ps1" -Phase Namespace -Pass 1 -OutputPath 'bin-probe/'
                } else {
                    & "$PSScriptRoot/scripts/verify-checkpoint-orphan-usage.ps1" -OutputPath 'bin-probe/'
                }
            } catch { $failure = $_.Exception.Message }
            $clock.Stop()
            [Console]::Out.WriteLine("C1233_RESULT $env:C1233_NONCE " + (@{
                error = $failure; milliseconds = $clock.ElapsedMilliseconds
            } | ConvertTo-Json -Compress))
            """);
        var start = new ProcessStartInfo("pwsh")
        {
            WorkingDirectory = root, UseShellExecute = false,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-File", wrapper })
            start.ArgumentList.Add(argument);
        start.Environment["PATH"] = shims + Path.PathSeparator + start.Environment["PATH"];
        start.Environment["C1233_ROOT"] = root;
        start.Environment["C1233_NONCE"] = nonce;
        start.Environment["C1233_CALLER"] = caller;
        start.Environment["C1233_MODE"] = mode;
        if (mode == "hardened")
            foreach (var name in new[] { "GIT_DIR", "GIT_WORK_TREE", "GIT_INDEX_FILE",
                         "GIT_CONFIG_PARAMETERS", "GIT_CONFIG_COUNT" })
                start.Environment[name] = "fixture-must-be-removed";
        using var process = Process.Start(start)!;
        RegisterCheckpointChild(process);
        var wrapperIdentity = Identity(process.Id, nonce);
        var error = process.StandardError.ReadToEndAsync(cancellationToken);
        Task? completion = null;
        try
        {
            var ready = await process.StandardOutput.ReadLineAsync(cancellationToken).AsTask()
                .WaitAsync(TimeSpan.FromSeconds(20), cancellationToken);
            ready.ShouldBe("C1233_READY " + wrapperIdentity, "readiness binds PID, start identity and nonce");
            var output = process.StandardOutput.ReadToEndAsync(cancellationToken);
            completion = Task.WhenAll(process.WaitForExitAsync(cancellationToken), output, error);
            var watchdog = false;
            try { await completion.WaitAsync(TimeSpan.FromSeconds(35), cancellationToken); }
            catch (TimeoutException) { watchdog = true; }

            var parentExited = false;
            var descendantDead = false;
            if (mode == "held-pipe")
            {
                parentExited = !Alive(ReadIdentity("parent"));
                var holder = ReadIdentity("holder");
                var death = Stopwatch.StartNew();
                while (Alive(holder) && death.ElapsedMilliseconds < 2_000)
                    await Task.Delay(20, cancellationToken);
                descendantDead = !Alive(holder);
            }
            Console.WriteLine($"{caller}/{mode}: watchdog={watchdog}; parentExited={parentExited}; descendantDead={descendantDead}");
            if (watchdog)
                return new Observation("", 0, true, parentExited, descendantDead);
            process.ExitCode.ShouldBe(0, await error);
            var prefix = $"C1233_RESULT {nonce} ";
            var result = (await output).Trim();
            result.ShouldStartWith(prefix);
            using var json = JsonDocument.Parse(result[prefix.Length..]);
            var failure = json.RootElement.GetProperty("error").GetString()!;
            var milliseconds = json.RootElement.GetProperty("milliseconds").GetInt64();
            Console.WriteLine($"{caller}/{mode}: {milliseconds} ms; {failure}");
            return new Observation(failure, milliseconds, false, parentExited, descendantDead);
        }
        finally
        {
            foreach (var name in new[] { "holder", "parent" })
                if (File.Exists(Path.Combine(root, name + ".identity")))
                {
                    var identity = ReadIdentity(name);
                    if (Alive(identity))
                    {
                        using var child = Process.GetProcessById(int.Parse(identity.Split(':')[0]));
                        child.Kill();
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
