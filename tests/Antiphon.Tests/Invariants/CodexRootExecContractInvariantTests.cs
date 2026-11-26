using System.Text.RegularExpressions;
using Antiphon.Tests.Infrastructure;
using Shouldly;
using TUnit.Core;
using static Antiphon.Tests.Infrastructure.CodexRootExecContractTests;

namespace Antiphon.Tests.Invariants;

[Category("Unit")]
[Category("RepositoryInvariant")]
[Timeout(600_000)]
public sealed class CodexRootExecContractInvariantTests
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
        Read("scripts/lib/Antiphon.Nightly/PhoneHomeGrokVerify.cs").ShouldContain(
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
}
