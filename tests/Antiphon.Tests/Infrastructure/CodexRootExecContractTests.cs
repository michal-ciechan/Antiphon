using System.Text.RegularExpressions;
using Antiphon.Tests.Scripts;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Infrastructure;

// CARD-1174. The server2 runner container's user is root, so docker exec of codex without
// -u 1654 leaves root-owned names under the app-owned /state/codex/tmp. The entrypoint gives
// those entries to uid 1654, and operator commands name that uid.
[Category("Unit")]
public sealed class CodexRootExecContractTests
{
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
        fn.ShouldContain("chown -h \"$APP_UID:$APP_GID\" \"$codex_tmp\"");
        fn.ShouldContain("find \"$codex_tmp\" -mindepth 1 -ignore_readdir_race");
        fn.ShouldContain("chown -h \"$uid:$gid\" \"$path\"");
        fn.Contains("chown -R ", StringComparison.Ordinal).ShouldBeFalse();
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

    [Test]
    [ParallelLimiter<ProcessSpawnLimit>]
    public void Entrypoint_heal_chowns_foreign_tmp_entries_and_leaves_the_home()
    {
        var fn = Function(Read("docker/session-runner-grok/dind-entrypoint.sh"));
        fn.ShouldContain("codex_tmp=/state/codex/tmp\n");
        var script = string.Join('\n',
            "set -eu",
            "root=\"$(mktemp -d)\"",
            "trap 'rm -rf \"$root\"' EXIT",
            "mkdir -p \"$root/bin\" \"$root/fixture/codex/tmp/arg0/codex-arg0DIR\"",
            "printf 'auth-bytes\\n' > \"$root/fixture/codex/auth.json\"",
            "printf 'config-bytes\\n' > \"$root/fixture/codex/config.toml\"",
            "printf 'foreign\\n' > \"$root/fixture/codex/tmp/arg0/codex-arg0FOREIGN\"",
            "printf 'ours\\n' > \"$root/fixture/codex/tmp/arg0/keep.txt\"",
            "printf 'nested\\n' > \"$root/fixture/codex/tmp/arg0/codex-arg0DIR/inside\"",
            "ln -s \"$root/fixture/codex/auth.json\" \"$root/fixture/codex/tmp/arg0/codex-arg0LINK\"",
            "cat > \"$root/bin/stat\" << 'SHIM'",
            "#!/bin/sh",
            "path=",
            "while [ $# -gt 0 ]; do",
            "  case \"$1\" in",
            "    -c) shift 2 ;;",
            "    *) path=$1; shift ;;",
            "  esac",
            "done",
            "case \"$path\" in",
            "  *codex-arg0FOREIGN*|*codex-arg0DIR*|*codex-arg0LINK*) printf '0\\n' ;;",
            "  *) printf '1654\\n' ;;",
            "esac",
            "SHIM",
            "cat > \"$root/bin/chown\" << 'SHIM'",
            "#!/bin/sh",
            "printf '%s\\n' \"$*\" >> \"$CHOWN_LOG\"",
            "exit 0",
            "SHIM",
            "chmod 0755 \"$root/bin/stat\" \"$root/bin/chown\"",
            "cat > \"$root/heal.sh\" << 'HEAL_FN'",
            fn.TrimEnd('\n'),
            "HEAL_FN",
            "unshare --user --map-root-user --mount bash -s \"$root\" << 'RUN'",
            "set -eu",
            "root=$1",
            "mkdir -p /state",
            "mount --bind \"$root/fixture\" /state",
            "export PATH=\"$root/bin:$PATH\"",
            "export CHOWN_LOG=\"$root/chown.log\"",
            "export APP_UID=1654 APP_GID=1654",
            ": > \"$CHOWN_LOG\"",
            ". \"$root/heal.sh\"",
            "heal_codex_tmp",
            "echo heal-exit=$?",
            "echo '--- chown ---'",
            "cat \"$CHOWN_LOG\"",
            "echo '--- end ---'",
            "printf 'auth=%s\\n' \"$(cat /state/codex/auth.json)\"",
            "printf 'config=%s\\n' \"$(cat /state/codex/config.toml)\"",
            "rm -rf /state/codex/tmp",
            "ln -s /tmp /state/codex/tmp",
            ": > \"$CHOWN_LOG\"",
            "if heal_codex_tmp; then echo symlink-exit=0; else echo symlink-exit=$?; fi",
            "echo symlink-lines=$(wc -l < \"$CHOWN_LOG\" | tr -d ' ')",
            "rm -f /state/codex/tmp",
            "if heal_codex_tmp; then echo absent-exit=0; else echo absent-exit=$?; fi",
            "RUN",
            "") + "\n";

        var output = RemoteScriptContractTests.LinuxShell(script).Replace("\r\n", "\n");
        output.ShouldContain("heal-exit=0\n");
        var log = Between(output, "--- chown ---\n", "\n--- end ---\n");
        log.ShouldContain("codex-arg0FOREIGN");
        log.ShouldContain("codex-arg0DIR");
        log.ShouldContain("codex-arg0LINK");
        log.ShouldContain("/inside");
        log.Contains("keep.txt", StringComparison.Ordinal).ShouldBeFalse();
        log.Contains("auth.json", StringComparison.Ordinal).ShouldBeFalse();
        log.Contains("config.toml", StringComparison.Ordinal).ShouldBeFalse();
        output.ShouldContain("auth=auth-bytes\n");
        output.ShouldContain("config=config-bytes\n");
        output.ShouldContain("symlink-exit=1\n");
        output.ShouldContain("symlink-lines=0\n");
        output.ShouldContain("absent-exit=0\n");
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
        at++;
        if (at >= tokens.Count)
            return false;
        var command = tokens[at].Trim('"', '\'', '`');
        return ProviderCommands.Contains(command) && !userOk;
    }

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
