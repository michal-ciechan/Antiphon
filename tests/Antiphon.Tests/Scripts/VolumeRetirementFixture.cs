using System.Diagnostics;
using System.Text.Json.Nodes;
using Antiphon.Tests.Application;
using Shouldly;

namespace Antiphon.Tests.Scripts;

internal static class VolumeRetirementFixture
{
    internal const string Operation = "c100800000000000000000000000000000001";
    internal const string AppliedCleanup = "c99400000000000000000000000000000001";
    internal const string PreviewCleanup = "c99400000000000000000000000000000002";
    internal const string ReadyMarker = "C1609_RETIREMENT state=Ready";
    internal const string OriginUrl = "https://fixture.invalid/card-1609/private.git";

    // C1008HostFixture supplies an executable Docker shim on its child PATH.
    // Keep GNU timeout active for every fixture caller, including these probes.
    internal const string ShellProbe = "";

    internal static string Quote(string value) => "'" + value.Replace("'", "'\\''", StringComparison.Ordinal) + "'";

    internal static string WorkMount(C1008HostFixture fixture) =>
        Path.Combine(fixture.Root, "volumes", "antiphon-runner-temp_work", "_data");

    internal static string StateMount(C1008HostFixture fixture) =>
        Path.Combine(fixture.Root, "volumes", "antiphon-runner-temp_runner-state", "_data");

    internal static async Task<string> RetainPreviewImage(C1008HostFixture fixture)
    {
        fixture.Docker["containers"]!.AsArray().Add(
            fixture.Container('7', "antiphon-runner-temp", "session-runner", running: false));
        var sha = new string('a', 40);
        fixture.Statuses["server2-temp"]!["buildVersion"] = sha;
        await File.WriteAllTextAsync(Path.Combine(fixture.Root, "temp.env"),
            "RUNNER_GROK_STORE_DIR=" + fixture.Root + "/grok\nSOURCE_REVISION=" + sha + "\n");
        var applied = await fixture.Run("retire-temp-containers");
        applied.Exit.ShouldBe(0, "c1609: seed cleanup; " + applied.Output);
        fixture.ReloadDocker();
        var preview = await fixture.Run("retire-temp-containers", "C994_OPERATION=" + PreviewCleanup, dryRun: true);
        preview.Exit.ShouldBe(0, "c1609: preview cleanup; " + preview.Output);
        fixture.ReloadDocker();
        var receipt = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(
            fixture.Root, "server", "temp-container-retirement", PreviewCleanup + ".json")))!;
        return receipt["image"]!.GetValue<string>();
    }

    internal static async Task<(int Exit, string Output)> Bash(string script)
    {
        var run = await C994ScriptProcess.Run("bash", "-c", script);
        return run;
    }

    internal static async Task<(string A, string B, string Bare, string Repo)> SeedAdvancedOrigin(C1008HostFixture fixture)
    {
        var bare = Path.Combine(fixture.Root, "origin.git");
        var repo = Path.Combine(WorkMount(fixture), "repos", "school-revision");
        var publisher = Path.Combine(fixture.Root, "publisher");
        var setup = await Bash($$"""
            set -euo pipefail
            git init -q --bare {{Quote(bare)}}
            git -C {{Quote(bare)}} symbolic-ref HEAD refs/heads/main
            git init -q -b main {{Quote(repo)}}
            git -C {{Quote(repo)}} config user.name Fixture
            git -C {{Quote(repo)}} config user.email fixture@example.invalid
            printf 'published-a\n' > {{Quote(repo)}}/sentinel
            git -C {{Quote(repo)}} add sentinel
            git -C {{Quote(repo)}} commit -qm A
            git -C {{Quote(repo)}} remote add origin {{Quote(OriginUrl)}}
            git -C {{Quote(repo)}} push -q {{Quote(bare)}} HEAD:refs/heads/main
            git clone -q -b main {{Quote(bare)}} {{Quote(publisher)}}
            git -C {{Quote(publisher)}} config user.name Fixture
            git -C {{Quote(publisher)}} config user.email fixture@example.invalid
            printf 'published-b\n' > {{Quote(publisher)}}/sentinel
            git -C {{Quote(publisher)}} add sentinel
            git -C {{Quote(publisher)}} commit -qm B
            git -C {{Quote(publisher)}} push -q origin HEAD:refs/heads/main
            a=$(git -C {{Quote(repo)}} rev-parse HEAD)
            b=$(git -C {{Quote(publisher)}} rev-parse HEAD)
            git -C {{Quote(publisher)}} merge-base --is-ancestor "$a" "$b"
            if git -C {{Quote(repo)}} cat-file -e "$b^{commit}" 2>/dev/null; then
              printf 'B is present in the volume\n'
              exit 1
            fi
            advertised=$(git -C {{Quote(bare)}} rev-parse refs/heads/main)
            [ "$advertised" = "$b" ]
            printf 'A=%s\nB=%s\n' "$a" "$b"
            """);
        setup.Exit.ShouldBe(0, "c1609: advanced-origin seed; " + setup.Output);
        var lines = setup.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var a = lines.Single(line => line.StartsWith("A=", StringComparison.Ordinal))[2..];
        var b = lines.Single(line => line.StartsWith("B=", StringComparison.Ordinal))[2..];
        return (a, b, bare, repo);
    }

    internal static async Task<(int Exit, string Output)> InvokeHostProof(
        string evidence, string inventoryDir, string bare, string remoteRoot, bool deny)
    {
        Directory.CreateDirectory(evidence);
        Directory.CreateDirectory(remoteRoot);
        var bin = Path.Combine(evidence, "bin");
        Directory.CreateDirectory(bin);
        var trace = Path.Combine(evidence, "host-git-trace");
        var scpLog = Path.Combine(evidence, "scp-log");
        await File.WriteAllTextAsync(Path.Combine(bin, "git"),
            "#!/bin/bash\nprintf '%s\\n' \"$*\" >> \"$H7_HOST_GIT_TRACE\"\n" +
            "if [ \"${H7_HOST_DENY:-0}\" = 1 ]; then\n" +
            "  printf '%s\\n' \"fatal: could not read Username for 'https://fixture.invalid': terminal prompts disabled\" >&2\n" +
            "  exit 128\nfi\nargs=()\nfor arg in \"$@\"; do\n" +
            "  if [ \"$arg\" = " + Quote(OriginUrl) + " ]; then args+=(\"$H7_BARE\"); else args+=(\"$arg\"); fi\ndone\n" +
            "export PATH=\"${PATH#*:}\"\n" +
            "exec git -c protocol.file.allow=always \"${args[@]}\"\n");
        await File.WriteAllTextAsync(Path.Combine(bin, "ssh"),
            "#!/bin/bash\nprintf '%s\\n' \"$*\" >> \"$H7_SSH_LOG\"\nexit 0\n");
        await File.WriteAllTextAsync(Path.Combine(bin, "scp"),
            "#!/bin/bash\nset -euo pipefail\nargs=()\nwhile [ $# -gt 0 ]; do\n" +
            "  case \"$1\" in -o) shift 2 ;; -r) shift ;; *) args+=(\"$1\"); shift ;; esac\ndone\n" +
            "src=\"${args[0]}\"; dst=\"${args[1]}\"\nprintf '%s -> %s\\n' \"$src\" \"$dst\" >> \"$H7_SCP_LOG\"\n" +
            "case \"$src\" in\n  mc@server2:/work/test-evidence/*/retire-temp-inventory)\n" +
            "    mkdir -p -- \"$dst\"\n    rm -rf -- \"$dst/retire-temp-inventory\"\n" +
            "    cp -a -- \"$H7_INVENTORY_DIR\" \"$dst/retire-temp-inventory\"\n    ;;\n" +
            "  *)\n    case \"$dst\" in\n      mc@server2:*)\n" +
            "        rel=\"${dst#mc@server2:}\"\n        dest=\"$H7_REMOTE_ROOT$rel\"\n" +
            "        mkdir -p -- \"$(dirname -- \"$dest\")\"\n        cp -a -- \"$src\" \"$dest\"\n        ;;\n" +
            "      *) exit 2 ;;\n    esac\n    ;;\nesac\n");
        var mode = await Bash("chmod 0755 " + Quote(Path.Combine(bin, "git")) + " " +
            Quote(Path.Combine(bin, "ssh")) + " " + Quote(Path.Combine(bin, "scp")));
        mode.Exit.ShouldBe(0, "c1609: host transport mode; " + mode.Output);
        var repoRoot = DelegateScriptRunner.RepoRoot;
        var psi = new ProcessStartInfo("pwsh")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = repoRoot
        };
        psi.ArgumentList.Add("-NoProfile");
        psi.ArgumentList.Add("-NonInteractive");
        psi.ArgumentList.Add("-Command");
        psi.ArgumentList.Add(
            "$ErrorActionPreference='Stop'; . " + Quote(Path.Combine(repoRoot, "scripts/c590-command.ps1")) +
            "; . " + Quote(Path.Combine(repoRoot, "scripts/c590-real.ps1")) +
            "; $exports = @(Get-C1008RetireProofExports -EvidenceRoot $env:H7_EVIDENCE -Run 'h7fixture' " +
            "-RecycleOperation '" + Operation + "' -InventoryRemote 'true' -ProjectId 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa1'); " +
            "$exports | ForEach-Object { Write-Output $_ }");
        psi.Environment["PATH"] = bin + ":" + Environment.GetEnvironmentVariable("PATH");
        psi.Environment["H7_EVIDENCE"] = evidence;
        psi.Environment["H7_INVENTORY_DIR"] = inventoryDir;
        psi.Environment["H7_REMOTE_ROOT"] = remoteRoot;
        psi.Environment["H7_SCP_LOG"] = scpLog;
        psi.Environment["H7_SSH_LOG"] = Path.Combine(evidence, "ssh-log");
        psi.Environment["H7_HOST_GIT_TRACE"] = trace;
        psi.Environment["H7_BARE"] = bare;
        psi.Environment["H7_HOST_DENY"] = deny ? "1" : "0";
        psi.Environment["GIT_TERMINAL_PROMPT"] = "0";
        using var proc = Process.Start(psi)!;
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var stdoutTask = proc.StandardOutput.ReadToEndAsync(deadline.Token);
        var stderrTask = proc.StandardError.ReadToEndAsync(deadline.Token);
        try
        {
            await proc.WaitForExitAsync(deadline.Token);
        }
        catch (OperationCanceledException)
        {
            if (!proc.HasExited) { proc.Kill(true); await proc.WaitForExitAsync(); }
            throw new TimeoutException("c1609: host proof writer exceeded 60s");
        }
        return (proc.ExitCode, await stdoutTask + await stderrTask);
    }

    internal static string Truncate(string value)
    {
        var flat = value.Replace('\r', ' ').Replace('\n', ' ');
        return flat.Length <= 500 ? flat : flat[..500];
    }

    internal static string TmpMount(C1008HostFixture fixture) =>
        Path.Combine(fixture.Root, "volumes", "antiphon-runner-temp_runner-tmp", "_data");

    internal static string DindMount(C1008HostFixture fixture) =>
        Path.Combine(fixture.Root, "volumes", "antiphon-runner-temp_dind-data", "_data");

    internal static async Task ReadyPreview(C1008HostFixture fixture, string extra = "")
    {
        // The fixture's child PATH carries executable fake Docker, including when
        // the real GNU timeout launches it directly.
        var probe = string.IsNullOrEmpty(extra) ? ShellProbe : extra + "\n" + ShellProbe;
        var preview = await fixture.Run("retire-temp-runner", probe, dryRun: true);
        preview.Exit.ShouldBe(0, "c1609: a Ready preview precedes apply; " + preview.Output);
        preview.Output.ShouldContain(ReadyMarker);
        fixture.Removed.ShouldBeEmpty("c1609: the Ready preview removes nothing");
    }

    internal static async Task<string> AuditText(C1008HostFixture fixture, string hostCase = "retire-temp-runner")
    {
        var path = Path.Combine(fixture.Root, "evidence", hostCase, "preview-audit.out");
        return File.Exists(path) ? await File.ReadAllTextAsync(path) : "";
    }

    internal static async Task<(int Exit, string Output)> Preview(C1008HostFixture fixture)
    {
        return await fixture.Run("retire-temp-runner", "\n" + ShellProbe, dryRun: true);
    }

    // One published outer checkout. Nested card-1605 seeds sit under a gitignored .antiphon.
    internal static async Task<(string Bare, string Outer)> SeedPublishedOuter(C1008HostFixture fixture, string name = "outer")
    {
        var bare = Path.Combine(fixture.Root, name + ".git");
        var outer = Path.Combine(WorkMount(fixture), "repos", name);
        var setup = await Bash($$"""
            set -euo pipefail
            git init -q --bare {{Quote(bare)}}
            git -C {{Quote(bare)}} symbolic-ref HEAD refs/heads/main
            git init -q -b main {{Quote(outer)}}
            git -C {{Quote(outer)}} config user.name Fixture
            git -C {{Quote(outer)}} config user.email fixture@example.invalid
            git -C {{Quote(outer)}} config commit.gpgsign false
            printf '.antiphon/\n' > {{Quote(outer)}}/.gitignore
            printf 'published\n' > {{Quote(outer)}}/readme.txt
            git -C {{Quote(outer)}} add .gitignore readme.txt
            git -C {{Quote(outer)}} -c commit.gpgsign=false commit -qm published
            git -C {{Quote(outer)}} remote add origin {{Quote(bare)}}
            git -C {{Quote(outer)}} push -q origin HEAD:refs/heads/main
            """);
        setup.Exit.ShouldBe(0, "c1609: published outer; " + setup.Output);
        return (bare, outer);
    }
}
