using System.Diagnostics;
using System.Text.Json.Nodes;
using Antiphon.Tests.Application;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;
using Antiphon.TestSupport;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// CARD-1099. The real-docker census shim must not inspect a foreign container that
/// vanishes between <c>docker ps</c> and <c>docker inspect</c>.
/// </summary>
[NativeLinuxOnly("CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately.")]
[Category("Unit")]
public sealed class RollingVolumeRecycleCensusChurnTests
{
    [Test]
    [ParallelLimiter<ProcessSpawnLimit>]
    public async Task C1099_Fixture_census_skips_a_vanished_foreign_container()
    {
        C1008HostFixture.RequireNativeLinux();
        var label = "io.antiphon.c1008-real";
        var prefix = "c1008rd" + Guid.NewGuid().ToString("N")[..16];
        var owned = new string('a', 64);
        var foreign = new string('b', 64);
        var image = "sha256:" + new string('c', 64);
        var root = Directory.CreateTempSubdirectory("c1099-census-").FullName;
        try
        {
            var fixture = File.ReadAllText(Path.Combine(DelegateScriptRunner.RepoRoot,
                "scripts/fixtures/c1008-recycle-real-cases.mjs"));
            fixture.ShouldContain("command docker \"$@\" --filter '${renderDockerPsFilterValue(label, prefix)}'");
            var remote = File.ReadAllText(Path.Combine(DelegateScriptRunner.RepoRoot, "scripts/c590-remote.sh"));
            const string start = "c1008_container_census() {";
            var at = remote.IndexOf(start, StringComparison.Ordinal);
            at.ShouldBeGreaterThanOrEqualTo(0);
            var end = remote.IndexOf("\n}\n", at, StringComparison.Ordinal);
            end.ShouldBeGreaterThan(at);
            var census = remote[at..(end + 2)];
            census.ShouldContain("docker ps -aq --no-trunc");
            census.ShouldContain("docker inspect \"$id\"");

            var bin = Path.Combine(root, "bin");
            Directory.CreateDirectory(bin);
            var trace = Path.Combine(root, "trace");
            var inspect = "[" + new JsonObject
            {
                ["Id"] = owned,
                ["Image"] = image,
                ["State"] = new JsonObject { ["Running"] = false, ["Status"] = "exited" },
                ["Config"] = new JsonObject
                {
                    ["Labels"] = new JsonObject
                    {
                        ["com.docker.compose.project"] = "c1099-target",
                        ["com.docker.compose.service"] = "session-runner"
                    }
                },
                ["Mounts"] = new JsonArray(),
                ["HostConfig"] = new JsonObject { ["Tmpfs"] = null, ["Mounts"] = null }
            }.ToJsonString() + "]";
            File.WriteAllText(Path.Combine(bin, "docker"), """
                #!/bin/bash
                printf '%s\n' "$*" >> "${C1099_TRACE:?}"
                if [ "$1" = ps ]; then
                  if [[ "${4:-}" == --filter && "${5:-}" == "label=${C1099_LABEL:?}=${C1099_PREFIX:?}" ]]; then
                    printf '%s\n' "${C1099_OWNED:?}"
                    exit 0
                  fi
                  printf '%s\n' "${C1099_OWNED:?}" "${C1099_FOREIGN:?}"
                  exit 0
                fi
                if [ "$1" = inspect ]; then
                  id="${@: -1}"
                  if [ "$id" = "${C1099_FOREIGN:?}" ]; then exit 1; fi
                  if [ "$id" = "${C1099_OWNED:?}" ]; then printf '%s\n' "${C1099_INSPECT:?}"; exit 0; fi
                  exit 1
                fi
                exit 2
                """);
            File.SetUnixFileMode(Path.Combine(bin, "docker"), UnixFileMode.UserRead | UnixFileMode.UserExecute);

            var script = Path.Combine(root, "census.sh");
            File.WriteAllText(script, """
                #!/bin/bash
                set -euo pipefail
                export PATH="${C1099_BIN:?}:$PATH"
                filter="$(node "${C1099_FIXTURE:?}")"
                case "$filter" in
                  *[!A-Za-z0-9._=-]*|label=) printf 'unexpected filter: %s\n' "$filter" >&2; exit 2 ;;
                  label=*) ;;
                  *) printf 'unexpected filter: %s\n' "$filter" >&2; exit 2 ;;
                esac
                docker() {
                  if [ "$1" = ps ]; then command docker "$@" --filter "$filter"
                  else command docker "$@"; fi
                }
                # shellcheck disable=SC1091
                source "${C1099_CENSUS_FILE:?}"
                set +e
                result="$(c1008_container_census)"
                code=$?
                set -e
                printf '%s\n' "$code"
                printf '%s\n' "$result"
                """);
            File.WriteAllText(Path.Combine(root, "census.fn.sh"), census);
            var psi = new ProcessStartInfo("bash")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            psi.ArgumentList.Add(script);
            psi.Environment["C1099_BIN"] = bin;
            psi.Environment["C1099_TRACE"] = trace;
            psi.Environment["C1099_LABEL"] = label;
            psi.Environment["C1099_PREFIX"] = prefix;
            psi.Environment["C1099_OWNED"] = owned;
            psi.Environment["C1099_FOREIGN"] = foreign;
            psi.Environment["C1099_INSPECT"] = inspect;
            psi.Environment["C1099_FIXTURE"] = Path.Combine(DelegateScriptRunner.RepoRoot,
                "scripts/fixtures/c1008-recycle-real-cases.mjs");
            psi.Environment["C1099_CENSUS_FILE"] = Path.Combine(root, "census.fn.sh");
            psi.Environment["C1008_EMIT_CENSUS_PS"] = "1";
            psi.Environment["C1008_SHIM_LABEL"] = label;
            psi.Environment["C1008_SHIM_PREFIX"] = prefix;
            using var child = Process.Start(psi)!;
            var stdout = child.StandardOutput.ReadToEndAsync();
            var stderr = child.StandardError.ReadToEndAsync();
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            try { await child.WaitForExitAsync(deadline.Token); }
            catch
            {
                if (!child.HasExited) { child.Kill(true); await child.WaitForExitAsync(); }
                throw;
            }
            var output = await stdout;
            var error = await stderr;
            child.ExitCode.ShouldBe(0, output + error);
            var newline = output.IndexOf('\n');
            newline.ShouldBeGreaterThan(0, output + error);
            output[..newline].ShouldBe("0", output + error);
            var censusJson = JsonNode.Parse(output[(newline + 1)..])!.AsArray();
            censusJson.Count.ShouldBe(1, output + error);
            censusJson[0]!["Id"]!.GetValue<string>().ShouldBe(owned);
            output.ShouldNotContain(foreign);
            var calls = File.ReadAllLines(trace);
            calls.ShouldContain(x => x.Contains("ps -aq --no-trunc --filter label=" + label + "=" + prefix, StringComparison.Ordinal));
            calls.ShouldNotContain(x => x.Contains(foreign, StringComparison.Ordinal));
            calls.ShouldContain(x => x.StartsWith("inspect ", StringComparison.Ordinal) && x.Contains(owned, StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
