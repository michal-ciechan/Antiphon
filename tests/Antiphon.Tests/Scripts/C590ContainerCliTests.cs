using static Antiphon.Tests.TestHelpers.RemoteScriptContractSupport;
using System.Text.RegularExpressions;
using Antiphon.Tests.Application;
using Antiphon.Tests.Infrastructure;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// Unit 68 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md: scripts/c590-remote.sh's container build-slot steps run
/// the host-published CLI (antiphon-scripts) with dotnet exec, mounted read-only at /opt/antiphon-cli in every helper the script
/// starts and copied (root-owned, removed after) into a running runner, which cannot take a new mount. No step runs pwsh or mounts
/// scripts/build-slot.ps1 (it dot-sourced lib/antiphon-cli.ps1, never mounted, so the helper steps had failed since unit 34), and
/// the CLI is published on the host by the checkout's own scripts/antiphon, never built or restored inside a container (the C849
/// cases measure the NuGet caches those containers use). Each step keeps its label and its command after --. The shell rows run the
/// real functions under bash (WSL on Windows) against a fake checkout and a fake docker; nothing real is started.
/// </summary>
[Category("Unit")]
public sealed class C590ContainerCliTests
{
    private static readonly string Mount = "--mount \"type=bind,source=$C849_CLI_DIR,target=/opt/antiphon-cli,readonly\"";

    [Test]
    public void Build_slot_steps_run_the_mounted_cli_with_dotnet_exec()
    {
        var remote = Remote();
        remote.ShouldNotContain("build-slot.ps1", customMessage: "no step runs or mounts the launcher");
        foreach (Match pwsh in Regex.Matches(remote, @"pwsh[^\n]*"))
            pwsh.Value.ShouldNotContain("build-slot", customMessage: "no pwsh build-slot step");
        var steps = Regex.Matches(remote, "dotnet exec (\"\\$C849_CLI\"|/opt/antiphon-cli/Antiphon\\.Scripts\\.dll) build-slot --label ([a-z0-9-]+) -- ([^\\n']+)")
            .Select(m => m.Groups[2].Value + " -- " + m.Groups[3].Value.TrimEnd()).ToArray();
        steps.ShouldBe(new[]
        {
            "c849-smoke -- /bin/sh \"$root/driver.sh\" \"$root\" >/dev/null",
            "c849-refill -- /bin/sh \"$root/driver.sh\" \"$root\" >/dev/null",
            "c849-fixture-restore -- /bin/sh \"/fixture/race/$1\" \"/fixture/race/$2\" \"$3\"",
            "c913-warm-control -- /bin/bash /probe.sh net9-offline || code=$?; rm -f \"$p/.warm\"; exit \"$code\"",
        });
        Regex.Matches(remote, @"build-slot --label").Count.ShouldBe(4);
        // Every helper the script starts for a smoke or a leased probe mounts the build read-only, after the CLI resolved.
        foreach (var function in new[] { "c849_seed", "c849_fixture_apphost", "c849_fixture_refill", "c849_fixture_restore_container",
                     "c849_fixture_warm_probe" })
        {
            var body = Block(remote, function);
            Regex.Matches(body, Regex.Escape(Mount)).Count.ShouldBe(1, function);
            var resolve = body.IndexOf("    c849_cli ||", StringComparison.Ordinal);
            resolve.ShouldBeGreaterThanOrEqualTo(0, function + " resolves the CLI");
            resolve.ShouldBeLessThan(body.IndexOf(Mount, StringComparison.Ordinal), function);
        }
        // The masked-pack control mounts only its mask (CARD-1192's exact-mount fact) and enters no CLI: its smoke refuses at the pack
        // check before build-slot, as it did when no helper had a working launcher.
        var masked = Block(remote, "c849_fixture_masked_pack");
        masked.ShouldNotContain("antiphon-cli");
        masked.ShouldContain("(c849_smoke \"$helper\" fixture-masked unmounted)");
        // A running runner gets the copy: c849_smoke and the refill pass its path in C849_CLI and remove it after the exec.
        foreach (var (function, label) in new[] { ("c849_smoke", "c849-smoke"), ("c849_prune", "c849-refill") })
        {
            var body = Block(remote, function);
            body.ShouldContain("-e \"C849_CLI=$", customMessage: function);
            Order(body, "c849_cli_enter \"$", "dotnet exec \"$C849_CLI\" build-slot --label " + label).ShouldBeTrue(function);
            Order(body, "dotnet exec \"$C849_CLI\" build-slot --label " + label, "c849_cli_leave \"$").ShouldBeTrue(function);
        }
        // The publish is the checkout's own launcher on this host, under umask 022; no container builds or restores the CLI.
        var cli = Block(remote, "c849_cli");
        cli.ShouldContain("( umask 022; sh \"$CHECKOUT/scripts/antiphon\" ) >> \"$CASE_DIR/cli.log\" 2>&1");
        cli.ShouldNotContain("docker");
        foreach (Match exec in Regex.Matches(remote, @"dotnet exec [^\n]*"))
            exec.Value.ShouldNotContain("CliLauncher", customMessage: "no container publishes the CLI");
    }

    [Test]
    [ParallelLimiter<ProcessSpawnLimit>]
    public void Cli_resolves_the_host_publish_once_and_refuses_an_unusable_build()
    {
        var output = LinuxShell("set -u\n" + """
            root="$(mktemp -d)"; trap 'rm -rf -- "$root"' EXIT
            CHECKOUT="$root/checkout"; CASE_DIR="$root/case"; RUN=u68run
            mkdir -p "$CHECKOUT/scripts" "$CASE_DIR"
            export FAKE_LOG="$root/calls" FAKE_CACHE="$CHECKOUT/bin-launcher/antiphon-scripts" FAKE_ID=0123456789abcdef01234567 FAKE_EXIT=''
            cat > "$CHECKOUT/scripts/antiphon" <<'SH'
            printf 'call umask=%s args=%s\n' "$(umask)" "$#" >> "$FAKE_LOG"
            [ -z "$FAKE_EXIT" ] || exit "$FAKE_EXIT"
            mkdir -p "$FAKE_CACHE/$FAKE_ID"; printf dll > "$FAKE_CACHE/$FAKE_ID/Antiphon.Scripts.dll"; printf '%s\n' "$FAKE_ID" > "$FAKE_CACHE/current"
            exit 2
            SH
            """ + "\n" + Block(Remote(), "c849_cli") + "\n" + """
            check() { local name="$1" code=0; C849_CLI_DIR=''; : > "$FAKE_LOG"; c849_cli || code=$?; printf '%s code=%s dir=%s calls=%s\n' "$name" "$code" "${C849_CLI_DIR#"$root"}" "$(wc -l < "$FAKE_LOG" | tr -d ' ')"; }
            check published
            grep -Fxq 'call umask=0022 args=0' "$FAKE_LOG" && echo publish-under-umask-022
            : > "$FAKE_LOG"; c849_cli; printf 'cached calls=%s\n' "$(wc -l < "$FAKE_LOG" | tr -d ' ')"
            FAKE_EXIT=9; rm -rf "$FAKE_CACHE"; check unbuildable; FAKE_EXIT=''
            check republished
            # From here the fake launcher exits 1 without publishing, so each check reads the cache as this script left it.
            FAKE_EXIT=1
            printf '../x\n' > "$FAKE_CACHE/current"; check bad-pointer
            printf '%s\n' "$FAKE_ID" > "$FAKE_CACHE/current"; rm "$FAKE_CACHE/$FAKE_ID/Antiphon.Scripts.dll"; check no-dll
            printf dll > "$FAKE_CACHE/$FAKE_ID/Antiphon.Scripts.dll"; chmod 600 "$FAKE_CACHE/$FAKE_ID/Antiphon.Scripts.dll"; check unreadable-file
            chmod 644 "$FAKE_CACHE/$FAKE_ID/Antiphon.Scripts.dll"; mkdir "$FAKE_CACHE/$FAKE_ID/private"; chmod 700 "$FAKE_CACHE/$FAKE_ID/private"; check unreadable-folder
            rmdir "$FAKE_CACHE/$FAKE_ID/private"
            mv "$FAKE_CACHE/$FAKE_ID" "$root/elsewhere"; ln -s "$root/elsewhere" "$FAKE_CACHE/$FAKE_ID"; check linked-folder
            rm "$FAKE_CACHE/$FAKE_ID"; mv "$root/elsewhere" "$FAKE_CACHE/$FAKE_ID"; check restored
            rm "$CHECKOUT/scripts/antiphon"; check no-launcher
            C849_CLI_DIR=/preset; : > "$FAKE_LOG"; c849_cli; printf 'preset dir=%s calls=%s\n' "$C849_CLI_DIR" "$(wc -l < "$FAKE_LOG" | tr -d ' ')"
            """);
        foreach (var line in new[]
        {
            "published code=0 dir=/checkout/bin-launcher/antiphon-scripts/0123456789abcdef01234567 calls=1", "publish-under-umask-022",
            "cached calls=0", "unbuildable code=1 dir= calls=1", "republished code=0 dir=/checkout/bin-launcher/antiphon-scripts/0123456789abcdef01234567 calls=1",
            "bad-pointer code=1 dir= calls=1", "no-dll code=1 dir= calls=1", "unreadable-file code=1 dir= calls=1", "unreadable-folder code=1 dir= calls=1",
            "linked-folder code=1 dir= calls=1", "restored code=0 dir=/checkout/bin-launcher/antiphon-scripts/0123456789abcdef01234567 calls=1",
            "no-launcher code=1 dir= calls=0", "preset dir=/preset calls=0",
        })
            output.ShouldContain(line);
    }

    [Test]
    [ParallelLimiter<ProcessSpawnLimit>]
    public void Cli_enters_a_helper_by_its_mount_and_a_runner_by_a_private_copy()
    {
        var remote = Remote();
        var output = LinuxShell("set -u\n" + """
            root="$(mktemp -d)"; trap 'rm -rf -- "$root"' EXIT
            C849_CLI_DIR=/host/cli; RUN=u68run; CASE_DIR="$root/case"; mkdir -p "$CASE_DIR"; log="$root/docker"
            docker() {
                printf 'docker %s\n' "$*" >> "$log"
                case "$1" in
                    inspect) [ "${@: -1}" != helper ] || printf '/opt/antiphon-cli\n'; return 0 ;;
                    cp) return "${FAKE_CP:-0}" ;;
                    exec) if [[ "$*" == *' /bin/sh -s'* ]]; then cat > /dev/null; printf 'C849_SMOKE uid=1654 restore=0 build=0 run=0 stdout=CARD0849_APPHOST_OK\n'; return "${FAKE_SMOKE:-0}"; fi; return 0 ;;
                esac
            }
            write_result() { printf 'result=%s:%s\n' "$1" "$2"; exit "$3"; }
            """ + "\n" + string.Join("\n", new[] { "c849_cli", "c849_cli_enter", "c849_cli_leave", "c849_smoke" }.Select(f => Block(remote, f))) + "\n" + """
            : > "$log"; dll="$(c849_cli_enter helper)"; printf 'helper dll=%s\n' "$dll"; c849_cli_leave helper "$dll"; sed 's/^/helper /' "$log"
            : > "$log"; dll="$(c849_cli_enter runner)"; printf 'runner dll=%s\n' "$dll"; c849_cli_leave runner "$dll"; sed 's/^/runner /' "$log"
            : > "$log"; code=0; FAKE_CP=1 c849_cli_enter runner >/dev/null || code=$?; printf 'copy-failed code=%s\n' "$code"
            : > "$log"; ( c849_smoke runner server2 ); printf 'smoke exit=%s\n' "$?"; sed 's/^/smoke /' "$log"; cat "$CASE_DIR/smoke-summary.txt"
            : > "$log"; ( FAKE_SMOKE=1 c849_smoke runner server2 ); printf 'failed-smoke exit=%s\n' "$?"; sed 's/^/failed-smoke /' "$log"
            : > "$log"; ( c849_smoke masked fixture-masked unmounted ); printf 'unmounted exit=%s\n' "$?"; sed 's/^/unmounted /' "$log"
            """);
        var lines = output.Replace("\r\n", "\n").Split('\n');
        lines.Where(l => l.StartsWith("helper ", StringComparison.Ordinal)).ShouldBe(new[]
        {
            "helper dll=/opt/antiphon-cli/Antiphon.Scripts.dll",
            "helper docker inspect -f {{range .Mounts}}{{if eq .Destination \"/opt/antiphon-cli\"}}{{.Destination}}{{end}}{{end}} helper",
        });
        lines.Where(l => l.StartsWith("runner ", StringComparison.Ordinal)).ShouldBe(new[]
        {
            "runner dll=/tmp/c849-cli-u68run/Antiphon.Scripts.dll",
            "runner docker inspect -f {{range .Mounts}}{{if eq .Destination \"/opt/antiphon-cli\"}}{{.Destination}}{{end}}{{end}} runner",
            "runner docker exec -u 0:0 runner rm -rf -- /tmp/c849-cli-u68run",
            "runner docker cp /host/cli/. runner:/tmp/c849-cli-u68run",
            "runner docker exec -u 0:0 runner chmod -R a+rX,go-w -- /tmp/c849-cli-u68run",
            "runner docker exec -u 0:0 runner rm -rf -- /tmp/c849-cli-u68run",
        });
        output.ShouldContain("copy-failed code=1");
        output.ShouldContain("smoke exit=0");
        output.ShouldContain("smoke docker exec -u 1654:1654 -e HOME=/home/app -e C849_CLI=/tmp/c849-cli-u68run/Antiphon.Scripts.dll -i runner /bin/sh -s");
        output.ShouldContain("C849_SMOKE runner=server2 uid=1654 restore=0 build=0 run=0 stdout=CARD0849_APPHOST_OK");
        // A failed smoke still removes the copy before it refuses.
        output.ShouldContain("result=false:CacheSmokeFailed");
        output.ShouldContain("failed-smoke exit=2");
        var failed = lines.Where(l => l.StartsWith("failed-smoke docker", StringComparison.Ordinal)).ToArray();
        failed.Last().ShouldBe("failed-smoke docker exec -u 0:0 runner rm -rf -- /tmp/c849-cli-u68run");
        // The masked control: no inspect, copy or removal; the smoke names the mount path it never reaches.
        lines.Where(l => l.StartsWith("unmounted docker", StringComparison.Ordinal)).ShouldBe(new[]
        {
            "unmounted docker exec -u 1654:1654 -e HOME=/home/app -e C849_CLI=/opt/antiphon-cli/Antiphon.Scripts.dll -i masked /bin/sh -s",
        });
        output.ShouldContain("unmounted exit=0");
    }

    private static bool Order(string text, string first, string second)
    {
        var a = text.IndexOf(first, StringComparison.Ordinal);
        var b = text.IndexOf(second, StringComparison.Ordinal);
        return a >= 0 && b > a;
    }

    private static string Block(string text, string function)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n');
        var start = Array.FindIndex(lines, line => line.StartsWith(function + "() {", StringComparison.Ordinal));
        start.ShouldBeGreaterThanOrEqualTo(0, "function " + function + " is missing");
        var end = Array.FindIndex(lines, start + 1, line => line == "}");
        end.ShouldBeGreaterThan(start, "function " + function + " never closes");
        return string.Join('\n', lines[start..(end + 1)]);
    }

    private static string Remote() =>
        File.ReadAllText(Path.Combine(DelegateScriptRunner.RepoRoot, "scripts", "c590-remote.sh")).Replace("\r\n", "\n");
}
