using System.Text.RegularExpressions;
using Antiphon.Nightly;
using Antiphon.Tests.Application;
using Antiphon.Tests.Infrastructure;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// CARD-0817: the server2 GitHub token refresh. Unit 51 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md ported
/// scripts/refresh-server2-github-token.ps1 to the refresh-server2-github-token command (Antiphon.Nightly.GithubTokenRefresh); these
/// cases run it in process against fake bw and ssh (FakeOperatorTools) and a test environment, where they read the script's text.
/// The token is synthetic; no vault, relay, server2 or real environment variable is read.
/// </summary>
[Category("Unit")]
public sealed partial class RefreshGithubTokenScriptTests
{
    private const string Token = "ghp_C0817SyntheticTokenNeverReal0123456789";
    private const string Session = "c0817-fake-session";

    [Test]
    public void Refresh_script_streams_the_token_over_stdin_and_never_prints_it()
    {
        using var world = World();
        world.Variables["BW_SESSION"] = Session;
        var exit = Run(world);

        exit.ShouldBe(GithubTokenRefresh.Refreshed, world.Out);
        world.Out.ShouldBe("1654:1654 400\npresent=true\n", "only the two validated metadata lines reach stdout");
        world.Error.ShouldBeEmpty();
        var calls = world.Tools.Calls;
        calls.Count.ShouldBe(3);
        calls[0].FileName.ShouldBe("/fake/bw");
        calls[0].Arguments.ShouldBe(["get", "password", GithubTokenRefresh.VaultItem, "--nointeraction"]);
        calls[0].Environment.ShouldNotBeNull().ShouldBe(new Dictionary<string, string> { ["BW_SESSION"] = Session }, "the relay session reaches bw alone");
        calls[0].Input.ShouldBeNull();
        foreach (var ssh in calls.Skip(1))
        {
            ssh.FileName.ShouldBe("/fake/ssh");
            ssh.Arguments.Take(5).ShouldBe(["-o", "BatchMode=yes", "-o", "ConnectTimeout=30", "mc@server2"]);
            ssh.Arguments.Count.ShouldBe(6);
            ssh.Environment.ShouldBeNull("ssh gets no extra variable: neither the session nor the token");
        }
        calls[1].Arguments[5].ShouldBe(GithubTokenRefresh.RemoteCommand(GithubTokenRefresh.UploadPayload));
        calls[1].Input.ShouldBe(Token + Environment.NewLine, "the token travels on the upload's stdin only");
        calls[2].Arguments[5].ShouldBe(GithubTokenRefresh.RemoteCommand(GithubTokenRefresh.VerifyPayload));
        calls[2].Input.ShouldBeNull("the verification sends nothing");
        foreach (var call in calls)
        {
            call.Arguments.ShouldAllBe(argument => !argument.Contains(Token), "no token in argv");
            (call.Environment ?? new Dictionary<string, string>()).Values.ShouldAllBe(value => !value.Contains(Token), "no token in the environment");
        }
        (world.Out + world.Error).ShouldNotContain(Token);

        var upload = calls[1].Arguments[5];
        upload.ShouldStartWith("sudo -n sh -c 'set -eu\n");
        upload.ShouldContain("mktemp \"$d/token.tmp.XXXXXXXX\"");
        upload.ShouldContain("cat > \"$tmp\" && test -s \"$tmp\" && chown 1654:1654 \"$tmp\" && chmod 0400 \"$tmp\" && mv -f \"$tmp\" \"$d/token\"");
        GithubTokenRefresh.UploadPayload.ShouldContain("trap 'rm -f -- \"$tmp\"' EXIT HUP INT TERM");
        upload.ShouldContain("trap '\\''rm -f -- \"$tmp\"'\\'' EXIT HUP INT TERM", Case.Sensitive, "the payload's quotes are escaped for sh -c");
        upload.ShouldNotContain("https://x-access-token:");
    }

    [Test]
    public void Refresh_script_skips_without_a_relay_session_and_reports_mode_only()
    {
        // No BW_SESSION and no ~/.bw-session: the skipped warning, exit 2, and nothing is run.
        using (var world = World())
        {
            Run(world).ShouldBe(GithubTokenRefresh.Skipped);
            world.Out.ShouldBe("WARNING: " + GithubTokenRefresh.SkippedWarning + "\n");
            world.Out.ShouldContain("refresh skipped, previous file unchanged");
            world.Tools.Calls.ShouldBeEmpty();
        }

        // ~/.bw-session, trimmed, when BW_SESSION is absent.
        using (var world = World())
        {
            File.WriteAllText(world.PathOf(".bw-session"), "  " + Session + "\r\n");
            Run(world).ShouldBe(GithubTokenRefresh.Refreshed, world.Out);
            world.Tools.Calls[0].Environment!["BW_SESSION"].ShouldBe(Session);
        }

        // Without bw or without ssh on PATH: skipped before the vault is read.
        foreach (var missing in new[] { "bw", "ssh" })
        {
            using var world = World();
            world.Variables["BW_SESSION"] = Session;
            world.Tools.Found.Remove(missing);
            Run(world).ShouldBe(GithubTokenRefresh.Skipped, missing);
            world.Tools.Calls.ShouldBeEmpty(missing);
            world.Out.ShouldContain(GithubTokenRefresh.SkippedWarning);
        }

        // A failed read, an empty value or one holding whitespace: skipped, and ssh never starts.
        foreach (var (exitCode, output) in new[] { (1, new[] { Token }), (0, Array.Empty<string>()), (0, new[] { "  " }), (0, new[] { "ghp_a b" }), (0, new[] { "ghp_a", "ghp_b" }) })
        {
            using var world = World(bwExit: exitCode, bwOutput: output);
            world.Variables["BW_SESSION"] = Session;
            Run(world).ShouldBe(GithubTokenRefresh.Skipped, string.Join("|", output));
            world.Tools.Calls.Count.ShouldBe(1, "only bw ran");
        }

        // The verification reads metadata only and accepts exactly the two lines.
        GithubTokenRefresh.VerifyPayload.ShouldContain("stat -c '%u:%g %a' \"$d/token\"");
        GithubTokenRefresh.VerifyPayload.ShouldContain("printf 'present=true\\n'");
        GithubTokenRefresh.VerifyPayload.ShouldNotContain("cat ");
        GithubTokenRefresh.VerifyPayload.ShouldNotContain("sha256sum");
        foreach (var metadata in new[] { new[] { "1654:1654 600", "present=true" }, new[] { "1654:1654 400", "present=false" }, new[] { "1654:1654 400" },
                     new[] { "1654:1654 400", "present=true", "extra" } })
        {
            using var world = World(verifyOutput: metadata);
            world.Variables["BW_SESSION"] = Session;
            Run(world).ShouldBe(GithubTokenRefresh.NotConfirmed, string.Join("|", metadata));
            world.Out.ShouldBe("WARNING: " + GithubTokenRefresh.VerifyWarning + "\n", "no metadata line after a failed verification");
        }

        // The server defaults to mc@server2 and must be [user@]host (the script's ValidatePattern).
        GithubTokenRefreshOptions.Parse([], "home").Server.ShouldBe("mc@server2");
        GithubTokenRefreshOptions.Parse(["--server", "ops@server2-temp.local"], "home").Server.ShouldBe("ops@server2-temp.local");
        foreach (var bad in new[] { "", "-oProxyCommand=x", "mc@", "mc@server2;id", "@server2" })
            Should.Throw<NightlyUsageException>(() => GithubTokenRefreshOptions.Parse(["--server", bad], "home"), bad);
        Should.Throw<NightlyUsageException>(() => GithubTokenRefreshOptions.Parse(["-Server", "mc@server2"], "home"));
    }

    // CARD-1245. Without -i, docker exec gives the inner `sh -s` an empty stdin: the heredoc is
    // never read, nothing runs, and the verification "passes" with exit 0.
    [Test]
    public void Heredoc_fed_docker_exec_keeps_stdin_open()
    {
        var vectors = new[]
        {
            ("docker exec -u 1654:1654 -e GIT_TERMINAL_PROMPT=0 \"$c\" sh -s <<'VERIFY'", false),
            ("docker exec -i -u 1654:1654 -e GIT_TERMINAL_PROMPT=0 \"$c\" sh -s <<'VERIFY'", true),
            ("docker exec -u 1654:1654 -i \"$c\" sh -s <<'VERIFY'", true),
            ("docker exec --interactive \"$c\" sh -s <<'X'", true),
            ("ssh mc@server2 docker exec -it \"$c\" bash -s <<X", true),
        };
        foreach (var (line, expected) in vectors)
            KeepsStdin(line).ShouldBe(expected, "c1245-docker-exec-stdin-vector: " + line);

        var procedure = DockerStackDocuments.Read("docs/superpowers/plans/2026-10-05-card-0817-https-token-push-credential-plan.md")
            .Replace("\r\n", "\n");
        procedure.ShouldContain("sh -s <<'VERIFY'", Case.Sensitive, "c1245-verify-block-present");
        var root = Path.Combine(DelegateScriptRunner.RepoRoot, "docs");
        var offenders = Directory.EnumerateFiles(root, "*.md", SearchOption.AllDirectories)
            .SelectMany(path => File.ReadAllLines(path).Select(line => (Path.GetRelativePath(DelegateScriptRunner.RepoRoot, path), line)))
            .Where(entry => Regex.IsMatch(entry.line, @"\bdocker exec\b.*\b(?:ba)?sh -s\b.*<<") && !KeepsStdin(entry.line))
            .Select(entry => entry.Item1 + ": " + entry.line.Trim())
            .ToArray();
        offenders.ShouldBeEmpty("c1245-docker-exec-heredoc-needs-i");
    }

    private static bool KeepsStdin(string line)
    {
        var start = line.IndexOf("docker exec", StringComparison.Ordinal);
        var shell = Regex.Match(line, @"\b(?:ba)?sh -s\b");
        if (start < 0 || !shell.Success || shell.Index < start) return false;
        return line[(start + "docker exec".Length)..shell.Index]
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Any(token => token == "--interactive" || Regex.IsMatch(token, "^-[A-Za-z]*i[A-Za-z]*$"));
    }

    /// <summary>A world with bw and ssh on PATH: bw answers the token, ssh accepts the upload and answers the verification.</summary>
    internal static OperatorToolWorld World(int bwExit = 0, string[]? bwOutput = null, string[]? verifyOutput = null)
    {
        var world = new OperatorToolWorld("c0817");
        world.Tools.Found["bw"] = "/fake/bw";
        world.Tools.Found["ssh"] = "/fake/ssh";
        var ssh = 0;
        world.Tools.Behaviour = call =>
        {
            if (call.FileName == "/fake/bw") return OperatorToolResult.Of(bwExit, bwOutput ?? [Token], ["bw: diagnostic"]);
            ssh++;
            return ssh == 1 ? OperatorToolResult.Of(0) : OperatorToolResult.Of(0, verifyOutput ?? ["1654:1654 400", "present=true"]);
        };
        return world;
    }

    internal static int Run(OperatorToolWorld world, params string[] arguments) =>
        GithubTokenRefresh.Run(GithubTokenRefreshOptions.Parse(arguments, world.Root), world.Host(), new System.IO.Abstractions.FileSystem(), world.Tools);
}
