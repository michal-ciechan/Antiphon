using System.Diagnostics;
using System.Text.RegularExpressions;
using Antiphon.Tests.Scripts;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;
using Antiphon.TestSupport;

namespace Antiphon.Tests.Infrastructure;

// CARD-1174. The server2 runner container's user is root, so docker exec of codex without
// -u 1654 leaves root-owned names under the app-owned /state/codex/tmp. The entrypoint gives
// those entries to uid 1654, and operator commands name that uid.
[Timeout(600_000)]
[Category("Unit")]
public sealed class CodexRootExecContractTests
{
    internal const string NativeLinuxReason = "CARD-1210: Codex heal contracts require native Linux with unshare user+mount namespaces and a bind-mountable /state";

    private static readonly string[] ProviderCommands = ["codex", "claude", "grok"];

    private static readonly HashSet<string> ValueFlags = new(StringComparer.Ordinal)
    {
        "-u", "--user", "-e", "--env", "-w", "--workdir",
    };

    [Test]
    public void Entrypoint_heals_only_the_codex_tmp_directory()
    {
        var text = Read("docker/session-runner-grok/dind-entrypoint.sh");
        var fn = Function(text);
        fn.ShouldContain("codex_tmp=/state/codex/tmp\n");
        fn.ShouldContain("if [ -L \"$codex_tmp\" ]; then\n    return 1\n");
        fn.ShouldContain("if [ ! -d \"$codex_tmp\" ]; then\n    return 0\n");
        fn.ShouldContain("chown -R -h \"$APP_UID:$APP_GID\" -- \"$codex_tmp\" || true\n");
        var linkGuard = fn.IndexOf("[ -L \"$codex_tmp\" ]", StringComparison.Ordinal);
        var noFollow = fn.IndexOf("chown -R -h ", StringComparison.Ordinal);
        linkGuard.ShouldBeGreaterThanOrEqualTo(0);
        noFollow.ShouldBeGreaterThan(linkGuard);
        Regex.Matches(fn, @"\bchown\b").Count.ShouldBe(1, "one recursive no-follow chown, not a per-name loop");
        fn.Contains("find ", StringComparison.Ordinal).ShouldBeFalse();
        fn.Contains("\"$path\"", StringComparison.Ordinal).ShouldBeFalse();
        fn.Contains("-exec", StringComparison.Ordinal).ShouldBeFalse();
        fn.Contains("auth.json", StringComparison.Ordinal).ShouldBeFalse();
        fn.Contains("/state/grok", StringComparison.Ordinal).ShouldBeFalse();
        fn.Contains("/state/claude", StringComparison.Ordinal).ShouldBeFalse();
        fn.Contains("find /state", StringComparison.Ordinal).ShouldBeFalse();
        Regex.Matches(fn, "/state").Count.ShouldBe(1, "the heal names /state/codex/tmp and no other /state path");

        text.ShouldContain("heal_codex_tmp || refuse CodexTmpUnowned");
        var heal = text.IndexOf("heal_codex_tmp || refuse CodexTmpUnowned", StringComparison.Ordinal);
        var dockerd = text.IndexOf("dockerd --config-file", StringComparison.Ordinal);
        heal.ShouldBeGreaterThanOrEqualTo(0);
        dockerd.ShouldBeGreaterThan(heal);
    }

    [NativeLinuxOnly("CARD-1210: Codex heal contracts require native Linux with unshare user+mount namespaces and a bind-mountable /state")]
    [Test]
    [ParallelLimiter<ProcessSpawnLimit>]
    public void Entrypoint_heal_chowns_the_tmp_directory_once()
    {
        NativeLinuxGate.Require(NativeLinuxReason);
        AssertPrerequisiteRefusal("unshare", "PREREQ unshare", 97, "c1210-prereq-unshare");
        const string once = "-R -h 1654:1654 -- /state/codex/tmp";
        var output = RunExtractedHeal(
            """
            mkdir -p "$root/fixture/codex/tmp/nested"
            printf 'auth-bytes\n' > "$root/fixture/codex/auth.json"
            printf 'config-bytes\n' > "$root/fixture/codex/config.toml"
            printf 'keep\n' > "$root/fixture/codex/tmp/keep.txt"
            printf 'nested\n' > "$root/fixture/codex/tmp/nested/inside"
            """,
            """
            if heal_codex_tmp; then echo heal-exit=0; else echo heal-exit=$?; fi
            echo '--- chown ---'
            cat "$CHOWN_LOG"
            echo '--- end ---'
            printf 'auth=%s\n' "$(cat /state/codex/auth.json)"
            printf 'config=%s\n' "$(cat /state/codex/config.toml)"
            rm -rf /state/codex/tmp
            ln -s /tmp /state/codex/tmp
            : > "$CHOWN_LOG"
            if heal_codex_tmp; then echo symlink-exit=0; else echo symlink-exit=$?; fi
            echo symlink-lines=$(wc -l < "$CHOWN_LOG" | tr -d ' ')
            rm -f /state/codex/tmp
            : > "$CHOWN_LOG"
            if heal_codex_tmp; then echo absent-exit=0; else echo absent-exit=$?; fi
            echo absent-lines=$(wc -l < "$CHOWN_LOG" | tr -d ' ')
            mkdir -p /state/codex/tmp
            : > "$CHOWN_LOG"
            export CHOWN_EXIT=1
            if heal_codex_tmp; then echo vanished-exit=0; else echo vanished-exit=$?; fi
            echo '--- vanished ---'
            cat "$CHOWN_LOG"
            echo '--- vanished-end ---'
            """);

        output.ShouldContain("heal-exit=0\n");
        Between(output, "--- chown ---\n", "\n--- end ---\n").ShouldBe(once);
        output.ShouldContain("auth=auth-bytes\n");
        output.ShouldContain("config=config-bytes\n");
        output.ShouldContain("symlink-exit=1\n");
        output.ShouldContain("symlink-lines=0\n");
        output.ShouldContain("absent-exit=0\n");
        output.ShouldContain("absent-lines=0\n");
        output.ShouldContain("vanished-exit=0\n");
        Between(output, "--- vanished ---\n", "\n--- vanished-end ---\n").ShouldBe(once);
    }

    [NativeLinuxOnly("CARD-1210: Codex heal contracts require native Linux with unshare user+mount namespaces and a bind-mountable /state")]
    [Test]
    [ParallelLimiter<ProcessSpawnLimit>]
    public void Entrypoint_heal_never_names_a_path_outside_tmp()
    {
        NativeLinuxGate.Require(NativeLinuxReason);
        AssertPrerequisiteRefusal("state", "PREREQ /state", 98, "c1210-prereq-state");
        const string once = "-R -h 1654:1654 -- /state/codex/tmp";
        var output = RunExtractedHeal(
            """
            mkdir -p "$root/fixture/codex/tmp" "$root/fixture/outside"
            printf 'secret\n' > "$root/fixture/outside/secret"
            printf 'auth-bytes\n' > "$root/fixture/codex/auth.json"
            ln -s /state/outside "$root/fixture/codex/tmp/mid"
            ln -s /state/codex/auth.json "$root/fixture/codex/tmp/leaf"
            printf 'keep\n' > "$root/fixture/codex/tmp/keep.txt"
            """,
            """
            if heal_codex_tmp; then echo heal-exit=0; else echo heal-exit=$?; fi
            echo '--- chown ---'
            cat "$CHOWN_LOG"
            echo '--- end ---'
            printf 'secret=%s\n' "$(cat /state/outside/secret)"
            printf 'auth=%s\n' "$(cat /state/codex/auth.json)"
            """);

        output.ShouldContain("heal-exit=0\n");
        var log = Between(output, "--- chown ---\n", "\n--- end ---\n");
        log.ShouldBe(once);
        log.Contains("/state/outside", StringComparison.Ordinal).ShouldBeFalse();
        log.Contains("auth.json", StringComparison.Ordinal).ShouldBeFalse();
        log.Contains("/state/codex/tmp/", StringComparison.Ordinal).ShouldBeFalse();
        output.ShouldContain("secret=secret\n");
        output.ShouldContain("auth=auth-bytes\n");
    }

    [Test]
    public void Provider_exec_into_the_runner_names_uid_1654()
    {
        var offenders = new List<string>();
        foreach (var relative in OperationalFiles())
        {
            var lineNo = 0;
            foreach (var line in File.ReadLines(Path.Combine(DockerStackDocuments.RepoRoot, relative)))
            {
                lineNo++;
                var trimmed = line.TrimStart();
                if (trimmed.StartsWith('#') || trimmed.StartsWith("//"))
                    continue;
                foreach (var segment in line.Split(["&&", ";"], StringSplitOptions.None))
                {
                    if (SegmentRunsProviderAsRoot(segment))
                        offenders.Add(relative + ":" + lineNo);
                }
            }
        }

        offenders.ShouldBeEmpty();

        var docs = Read("docs/docker-stack.md");
        docs.ShouldContain(
            "docker exec -u 1654:1654 -e HOME=/home/app -e CODEX_HOME=/state/codex \"$c\" codex --version");
        docs.ShouldContain(
            "Codex must never be exec'd as root in the server2 runner container: use uid 1654, because a root exec leaves root-owned names under `/state/codex/tmp` that the session user cannot remove (CARD-1174).");
        Read("scripts/verify-phone-home-grok.ps1").ShouldContain(
            "exec -T --user 1654:1654 session-runner-grok grok --version");
    }

    private static bool SegmentRunsProviderAsRoot(string segment)
    {
        var tokens = Tokenize(segment);
        var execAt = -1;
        for (var i = 0; i < tokens.Count; i++)
        {
            if (tokens[i] == "exec" && IsDockerExec(tokens, i))
            {
                execAt = i;
                break;
            }
        }

        if (execAt < 0)
            return false;

        var at = execAt + 1;
        var userOk = false;
        while (at < tokens.Count && tokens[at].StartsWith('-'))
        {
            var flag = tokens[at];
            string? attached = null;
            var eq = flag.IndexOf('=');
            if (eq >= 0)
            {
                attached = flag[(eq + 1)..];
                flag = flag[..eq];
            }

            at++;
            string? value = attached;
            if (attached == null && ValueFlags.Contains(flag) && at < tokens.Count)
            {
                value = tokens[at];
                at++;
            }

            if ((flag == "-u" || flag == "--user") && value != null
                && value.StartsWith("1654", StringComparison.Ordinal))
                userOk = true;
        }

        if (at >= tokens.Count)
            return false;
        var target = tokens[at].Trim('"', '\'', '`');
        at++;
        if (at >= tokens.Count || !LooksLikeExecTarget(target))
            return false;
        var command = tokens[at].Trim('"', '\'', '`');
        return ProviderCommands.Contains(command) && !userOk;
    }

    // "docker exec with claude" is a sentence. A real target is a variable ($c), a
    // placeholder (<container>), a hyphenated service, or an id.
    private static bool LooksLikeExecTarget(string token) =>
        token.IndexOfAny("$<-/:0123456789".ToCharArray()) >= 0;

    private static bool IsDockerExec(List<string> tokens, int execAt)
    {
        for (var j = execAt - 1; j >= 0; j--)
        {
            if (tokens[j] != "docker")
                continue;
            return j + 1 == execAt || (j + 1 < tokens.Count && tokens[j + 1] == "compose");
        }

        return false;
    }

    private static List<string> Tokenize(string segment)
    {
        var tokens = new List<string>();
        foreach (Match match in Regex.Matches(segment, @"\S+"))
        {
            var token = match.Value.Trim('"', '\'', '`', '(', ')', ',', ';');
            if (token.Length > 0)
                tokens.Add(token);
        }

        return tokens;
    }

    private static IEnumerable<string> OperationalFiles()
    {
        var root = DockerStackDocuments.RepoRoot;
        var skip = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "bin", "obj", "node_modules", ".git", "TestResults", "artifacts",
        };
        foreach (var top in new[] { "scripts", "docs", "docker", "tests" })
        {
            var dir = Path.Combine(root, top);
            if (!Directory.Exists(dir))
                continue;
            foreach (var file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
            {
                var ext = Path.GetExtension(file);
                if (ext is not (".ps1" or ".sh" or ".md" or ".yml" or ".yaml" or ".cs"))
                    continue;
                var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
                if (relative.Split('/').Any(skip.Contains))
                    continue;
                yield return relative;
            }
        }
    }

    // Fake chown records one argv line. No root and no docker: a user namespace
    // bind-mounts the scratch fixture at the entrypoint's hard-coded path.
    private static string RunExtractedHeal(string prelude, string body)
    {
        var result = RunHealScript(BuildHealScript(prelude, body));
        result.Exit.ShouldBe(0, "c1210-healthy-prerequisites: " + result.Output);
        return result.Output;
    }

    private static void AssertPrerequisiteRefusal(string probe, string reason, int exit, string label)
    {
        var result = RunHealScript(BuildHealScript("mkdir -p \"$root/fixture\"", "echo heal-body-entered", probe));
        result.Exit.ShouldBe(exit, label + ": " + result.Output);
        result.Output.ShouldContain(reason + "\n", Case.Sensitive, label);
        result.Output.ShouldContain("heal-calls=0\n", Case.Sensitive, label);
        result.Output.ShouldNotContain("heal-body-entered", Case.Sensitive, label);
        result.Output.ShouldNotContain("heal-reached", Case.Sensitive, label);
    }

    private static (int Exit, string Output) RunHealScript(string script)
    {
        var start = new ProcessStartInfo("bash")
        {
            UseShellExecute = false, RedirectStandardInput = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
        };
        start.ArgumentList.Add("-s");
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        process.StandardInput.Write(script);
        process.StandardInput.Close();
        if (!process.WaitForExit(60_000))
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit();
            throw new TimeoutException("the Linux heal harness did not finish in 60s");
        }
        var output = (stdout.GetAwaiter().GetResult() + stderr.GetAwaiter().GetResult()).Replace("\r\n", "\n");
        Console.WriteLine(output);
        return (process.ExitCode, output);
    }

    private static string BuildHealScript(string prelude, string body, string? probe = null)
    {
        var function = Function(Read("docker/session-runner-grok/dind-entrypoint.sh"));
        var script = string.Join('\n',
            "set -eu",
            "root=\"$(mktemp -d)\"",
            "export CHOWN_LOG=\"$root/chown.log\"",
            ": > \"$CHOWN_LOG\"",
            probe is null ? "trap 'rm -rf \"$root\"' EXIT"
                : "trap 'echo heal-calls=$(wc -l < \"$CHOWN_LOG\" | tr -d \" \"); rm -rf \"$root\"' EXIT",
            probe == "unshare" ? "unshare() { return 1; }" : probe == "state" ? "unshare() { return 0; }" : "",
            probe == "state" ? "state_target=\"$root/missing-state\"" : "state_target=/state",
            "if ! command -v unshare >/dev/null 2>&1 || ! unshare --user --map-root-user --mount true; then echo 'PREREQ unshare'; exit 97; fi",
            "if [ ! -d \"$state_target\" ]; then echo 'PREREQ /state'; exit 98; fi",
            "mkdir -p \"$root/bin\"",
            prelude.Trim('\n'),
            "cat > \"$root/bin/chown\" << 'SHIM'",
            "#!/bin/sh",
            "printf '%s\\n' \"$*\" >> \"$CHOWN_LOG\"",
            "exit \"${CHOWN_EXIT:-0}\"",
            "SHIM",
            "chmod 0755 \"$root/bin/chown\"",
            "cat > \"$root/heal.sh\" << 'HEAL_FN'",
            function.TrimEnd('\n'),
            "HEAL_FN",
            "unshare --user --map-root-user --mount bash -s \"$root\" \"$state_target\" << 'RUN'",
            "set -eu",
            "root=$1",
            "state_target=$2",
            "mount --bind \"$root/fixture\" /state",
            "export PATH=\"$root/bin:$PATH\"",
            "export CHOWN_LOG=\"$root/chown.log\"",
            "export APP_UID=1654 APP_GID=1654",
            ": > \"$CHOWN_LOG\"",
            ". \"$root/heal.sh\"",
            body.Trim('\n'),
            "RUN",
            probe is null ? "" : "echo heal-reached",
            "") + "\n";
        return script;
    }

    private static string Function(string text)
    {
        var start = text.IndexOf("\nheal_codex_tmp() {\n", StringComparison.Ordinal);
        start.ShouldBeGreaterThanOrEqualTo(0, "dind-entrypoint.sh defines heal_codex_tmp");
        var end = text.IndexOf("\n}\n", start, StringComparison.Ordinal);
        end.ShouldBeGreaterThan(start);
        return text[(start + 1)..(end + 2)];
    }

    private static string Between(string text, string start, string end)
    {
        var at = text.IndexOf(start, StringComparison.Ordinal);
        at.ShouldBeGreaterThanOrEqualTo(0, "missing " + start.Trim());
        at += start.Length;
        var to = text.IndexOf(end, at, StringComparison.Ordinal);
        to.ShouldBeGreaterThan(at);
        return text[at..to];
    }

    private static string Read(string relative) =>
        DockerStackDocuments.Read(relative).Replace("\r\n", "\n");
}
