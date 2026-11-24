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
[ParallelLimiter<CheckpointToolSpawnLimit>]
public sealed class CheckpointSourceGitBoundsTests : CheckpointTestBase
{
    [Test]
    [Timeout(120_000)]
    public async Task C1233_ToolSource(CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsLinux())
            Skip.Test("Linux inherited-pipe custody proof");

        // Fingerprint v2 reads the checkout with libgit2: the tool's capture starts no git child at all, so no
        // git pipe can be inherited or held. The fake git on PATH records any call.
        var healthy = await CaptureAsync("tool-real-repo", cancellationToken, "tool");
        healthy.Watchdog.ShouldBeFalse();
        healthy.Observation!.CaptureStatus.ShouldBe("known");
        healthy.Observation.DirtyFiles.ShouldBe(1, "one untracked file in the real repository");
        healthy.GitInvoked.ShouldBeFalse("SourceSnapshot.Capture must not start git");
    }

    private sealed record CaptureResult(SourceObservation? Observation, long Milliseconds,
        bool Watchdog, bool ParentExited, bool DescendantDead, string Root = "", bool GitInvoked = false);

    private const string HeadOid = "1111111111111111111111111111111111111111";
    private const string IndexOid = "2222222222222222222222222222222222222222";

    private async Task<CaptureResult> CaptureAsync(string mode, CancellationToken cancellationToken,
        string caller = "tool")
    {
        var root = TempDir();
        var shims = Directory.CreateDirectory(Path.Combine(root, "shims")).FullName;
        var nonce = Guid.NewGuid().ToString("N");
        File.WriteAllBytes(Path.Combine(root, "status"), Encoding.UTF8.GetBytes(
            "# branch.oid " + new string('a', 40) + "\0# branch.head main\0" +
            "1 .M N... 100644 100644 100644 " + HeadOid + " " + IndexOid + " café.txt\0? raw.bin\0"));
        File.WriteAllBytes(Path.Combine(root, "raw.bin"), [0, 0xff, 0x80, 13, 10, 65]);
        var target = root;
        if (mode == "tool-real-repo")
        {
            // A real checkout beside the fixture files (the FIFO and scripts stay out of it).
            target = Directory.CreateDirectory(Path.Combine(root, "repo")).FullName;
            CheckpointFixtureProcess.Git(target, "init", "-q");
            File.WriteAllText(Path.Combine(target, "tracked.txt"), "tracked\n");
            CheckpointFixtureProcess.Git(target, "add", "tracked.txt");
            CheckpointFixtureProcess.Git(target, "-c", "user.name=t", "-c", "user.email=t@t", "commit", "-q", "-m", "seed");
            File.WriteAllText(Path.Combine(target, "untracked.txt"), "new\n");
        }
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
            printf 'x' >> "$C1233_ROOT/invoked"
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
              ls-files)
                if [ "$C1233_MODE" = nonzero ]; then printf 'fixture failure\n' >&2; exit 7; fi
                printf 'H café.txt\000'
                ;;
              *) exit 91 ;;
            esac
            """);
        var wrapper = Path.Combine(root, "capture.ps1");
        File.WriteAllText(wrapper, """
            $ErrorActionPreference = 'Stop'
            $null = [Reflection.Assembly]::LoadFrom($env:C1233_HOST_ASSEMBLY)
            $null = [Reflection.Assembly]::LoadFrom($env:C1233_TOOL_ASSEMBLY)
            $reader = [Antiphon.Checkpoints.SourceSnapshot]::new()
            $stat = [IO.File]::ReadAllText("/proc/$PID/stat")
            $startToken = $stat.Substring($stat.LastIndexOf(')') + 2).Split(' ')[19]
            [Console]::Out.WriteLine("C1233_READY $PID`:$startToken`:$env:C1233_NONCE")
            $clock = [Diagnostics.Stopwatch]::StartNew()
            $observation = $reader.Capture($env:C1233_TARGET)
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
        start.Environment["C1233_TARGET"] = target;
        start.Environment["C1233_NONCE"] = nonce;
        start.Environment["C1233_MODE"] = mode;
        start.Environment["C1233_CALLER"] = caller;
        start.Environment["C1233_HOST_ASSEMBLY"] = typeof(CheckpointProcessRunner).Assembly.Location;
        start.Environment["C1233_TOOL_ASSEMBLY"] = typeof(SourceSnapshot).Assembly.Location;
        // pwsh does not read the tool's deps.json, so libgit2's native library is found on the loader path.
        var native = Path.Combine(Path.GetDirectoryName(typeof(SourceSnapshot).Assembly.Location)!, "runtimes",
            System.Runtime.InteropServices.RuntimeInformation.RuntimeIdentifier, "native");
        start.Environment["LD_LIBRARY_PATH"] = native + (start.Environment.TryGetValue("LD_LIBRARY_PATH", out var loader)
            && !string.IsNullOrEmpty(loader) ? ":" + loader : "");
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
            return new CaptureResult(observation, milliseconds, false, parentExited, descendantDead, root,
                File.Exists(Path.Combine(root, "invoked")));
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
