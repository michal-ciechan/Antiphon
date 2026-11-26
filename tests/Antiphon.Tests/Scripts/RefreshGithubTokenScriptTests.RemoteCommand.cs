using Antiphon.Nightly;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

// CARD-1245: the remote commands of the server2 GitHub token refresh. These were RefreshGithubTokenRemoteCommandTests, which started
// scripts/refresh-server2-github-token.ps1 under pwsh with POSIX sh fakes of bw and ssh, so they ran on Linux only (its rc Windows
// exclusion row and frozen witness key left with that class in unit 51 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md).
// They run the refresh-server2-github-token command in process on every platform. The fake ssh records the remote command and
// stdin and, like sh on server2, fails on a carriage return. No vault, no server2, and the token is synthetic.
public sealed partial class RefreshGithubTokenScriptTests
{
    private const string RemoteToken = "ghp_C1245SyntheticTokenNeverReal0123456789";

    [Test]
    public void Crlf_checkout_sends_carriage_return_free_remote_commands()
    {
        // A CRLF checkout made the script's here-strings CRLF; the command removes every carriage return all the same.
        var crlfUpload = GithubTokenRefresh.UploadPayload.Replace("\n", "\r\n");
        crlfUpload.ShouldContain("set -eu\r\n", Case.Sensitive, "the CRLF payload must carry CRLF");
        GithubTokenRefresh.RemoteCommand(crlfUpload).ShouldBe(GithubTokenRefresh.RemoteCommand(GithubTokenRefresh.UploadPayload));
        GithubTokenRefresh.RemoteCommand(GithubTokenRefresh.VerifyPayload.Replace("\n", "\r\n"))
            .ShouldBe(GithubTokenRefresh.RemoteCommand(GithubTokenRefresh.VerifyPayload));
        GithubTokenRefresh.RemoteCommand("echo 'quoted'").ShouldBe("sudo -n sh -c 'echo '\\''quoted'\\'''");

        using var world = RemoteWorld();
        var exit = RunRemote(world);

        exit.ShouldBe(0, world.Out);
        world.Out.ShouldBe("1654:1654 400\npresent=true\n");
        var ssh = world.Tools.Calls.Where(call => call.FileName == "/fake/ssh").ToList();
        ssh.Count.ShouldBe(2);
        for (var call = 0; call < 2; call++)
        {
            var remote = ssh[call].Arguments[^1];
            remote.ShouldNotContain("\r", Case.Sensitive, "c1245-remote-command-has-no-carriage-return call " + (call + 1));
            remote.ShouldStartWith("sudo -n sh -c 'set -eu\n");
            remote.ShouldNotContain(RemoteToken, Case.Sensitive, "no token in the SSH command line");
        }
        ssh[0].Arguments[^1].ShouldContain("set -eu\numask 077\nd=/home/mc/antiphon-server2/secrets/github-token\n");
        ssh[0].Input.ShouldBe(RemoteToken + Environment.NewLine, "the token travels on the upload's stdin only");
        ssh[1].Input.ShouldBeNull("the verify step sends nothing");
        (world.Out + world.Error).ShouldNotContain(RemoteToken);
    }

    [Test]
    public void Upload_failure_surfaces_remote_stderr_without_the_token()
    {
        using var world = RemoteWorld(uploadStderr: ["sudo: a password is required", "remote echoed " + RemoteToken + " back"]);
        var exit = RunRemote(world);

        exit.ShouldBe(1, world.Out);
        world.Tools.Calls.Count(call => call.FileName == "/fake/ssh").ShouldBe(1, "no verify after a failed upload");
        world.Out.ShouldNotContain("present=true", Case.Sensitive, "no metadata line after a failure");
        world.Out.ShouldContain("not confirmed over SSH", Case.Sensitive, world.Out);
        world.Out.ShouldContain("sudo: a password is required", Case.Sensitive, "c1245-upload-stderr-surfaced");
        world.Out.ShouldContain("WARNING: Remote stderr (exit 1): sudo: a password is required | remote echoed [redacted] back", Case.Sensitive,
            "c1245-upload-stderr-redacted");
        (world.Out + world.Error).ShouldNotContain(RemoteToken, Case.Sensitive, "c1245-token-never-printed");
        world.Tools.Calls.Last().Arguments[^1].ShouldNotContain(RemoteToken);
    }

    [Test]
    public void Verify_failure_surfaces_remote_stderr()
    {
        using var world = RemoteWorld(verifyStderr: ["stat: cannot statx '/home/mc/antiphon-server2/secrets/github-token/token': Permission denied"]);
        var exit = RunRemote(world);

        exit.ShouldBe(1, world.Out);
        world.Tools.Calls.Count(call => call.FileName == "/fake/ssh").ShouldBe(2, world.Out);
        world.Out.ShouldNotContain("present=true", Case.Sensitive, "no metadata line after a failure");
        world.Out.ShouldContain("owner/mode/presence verification failed", Case.Sensitive, world.Out);
        world.Out.ShouldContain("stat: cannot statx", Case.Sensitive, "c1245-verify-stderr-surfaced");
        (world.Out + world.Error).ShouldNotContain(RemoteToken);
    }

    // The remote stderr is one line: control characters as spaces, empty lines dropped, at most 2000 characters.
    [Test]
    public void Remote_stderr_is_one_bounded_line()
    {
        GithubTokenRefresh.RemoteStderr(["  a\tb  ", "", "   ", "c\u001bd"], "").ShouldBe("a b | c d");
        GithubTokenRefresh.RemoteStderr(["x" + RemoteToken + "y"], RemoteToken).ShouldBe("x[redacted]y");
        GithubTokenRefresh.RemoteStderr([new string('e', 2500)], "").ShouldBe(new string('e', 2000) + "...");
        GithubTokenRefresh.RemoteStderr([], RemoteToken).ShouldBeEmpty();
    }

    // An exception never renders: before the upload it is the skipped warning (exit 2), after it the not-confirmed one (exit 1).
    [Test]
    public void An_exception_is_a_fixed_warning_without_its_text()
    {
        using (var world = RemoteWorld())
        {
            world.Tools.Behaviour = call => throw new InvalidOperationException("bw blew up with " + RemoteToken);
            RunRemote(world).ShouldBe(GithubTokenRefresh.Skipped);
            world.Out.ShouldBe("WARNING: " + GithubTokenRefresh.SkippedWarning + "\n");
        }
        using (var world = RemoteWorld())
        {
            var inner = world.Tools.Behaviour!;
            world.Tools.Behaviour = call => call.FileName == "/fake/ssh" ? throw new InvalidOperationException("ssh failed holding " + RemoteToken) : inner(call);
            RunRemote(world).ShouldBe(GithubTokenRefresh.NotConfirmed);
            world.Out.ShouldBe("WARNING: " + GithubTokenRefresh.FailedWarning + "\n");
            (world.Out + world.Error).ShouldNotContain(RemoteToken);
        }
    }

    private static OperatorToolWorld RemoteWorld(string[]? uploadStderr = null, string[]? verifyStderr = null)
    {
        var world = new OperatorToolWorld("c1245");
        world.Variables["BW_SESSION"] = "c1245-fake-session";
        world.Tools.Found["bw"] = "/fake/bw";
        world.Tools.Found["ssh"] = "/fake/ssh";
        var ssh = 0;
        world.Tools.Behaviour = call =>
        {
            if (call.FileName == "/fake/bw") return OperatorToolResult.Of(0, [RemoteToken]);
            ssh++;
            var remote = call.Arguments[^1];
            if (remote.Contains('\r')) return OperatorToolResult.Of(2, error: ["sh: 1: set: Illegal option -"]);
            if (ssh == 1 && uploadStderr != null) return OperatorToolResult.Of(1, error: uploadStderr);
            if (ssh == 2 && verifyStderr != null) return OperatorToolResult.Of(1, error: verifyStderr);
            return ssh == 2 ? OperatorToolResult.Of(0, ["1654:1654 400", "present=true"]) : OperatorToolResult.Of(0);
        };
        return world;
    }

    private static int RunRemote(OperatorToolWorld world) => Run(world, "--server", "mc@server2");
}
