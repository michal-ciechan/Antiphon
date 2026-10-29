using System.Diagnostics;
using Antiphon.Tests.Application;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;
using Antiphon.TestSupport;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// CARD-1245. Runs <c>scripts/refresh-server2-github-token.ps1</c> under pwsh with a fake
/// <c>bw</c> and a fake <c>ssh</c> first on PATH, from an LF copy and from the CRLF copy a
/// Windows checkout produces. The fake ssh records the remote command and, like sh on server2,
/// fails on a carriage return. No vault, no server2, and the token is synthetic.
/// </summary>
[NativeLinuxOnly("CARD-1245: the fake bw/ssh are POSIX sh executables; Windows skips explicitly.")]
[Timeout(600_000)]
[Category("Unit")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class RefreshGithubTokenRemoteCommandTests
{
    private const string NativeLinuxReason = "CARD-1245: the fake bw/ssh are POSIX sh executables; Windows skips explicitly.";
    private const string Token = "ghp_C1245SyntheticTokenNeverReal0123456789";

    [Test]
    public async Task Crlf_checkout_sends_carriage_return_free_remote_commands()
    {
        NativeLinuxGate.Require(NativeLinuxReason);
        using var lf = new Fixture(crlf: false);
        using var crlf = new Fixture(crlf: true);
        crlf.ScriptText.ShouldContain("set -eu\r\n", Case.Sensitive, "the CRLF copy must carry CRLF here-strings");

        var lfRun = await lf.RunAsync();
        var crlfRun = await crlf.RunAsync();

        foreach (var (fixture, run) in new[] { (lf, lfRun), (crlf, crlfRun) })
        {
            run.Exit.ShouldBe(0, run.Output);
            run.Stdout.ReplaceLineEndings("\n").ShouldBe("1654:1654 400\npresent=true\n", run.Output);
            fixture.Calls.ShouldBe(2, run.Output);
            for (var call = 1; call <= 2; call++)
            {
                var remote = fixture.Remote(call);
                remote.ShouldNotContain("\r", Case.Sensitive, "c1245-remote-command-has-no-carriage-return call " + call);
                remote.ShouldStartWith("sudo -n sh -c 'set -eu\n");
                remote.ShouldNotContain(Token, Case.Sensitive, "no token in the SSH command line");
            }
            fixture.Remote(1).ShouldContain("set -eu\numask 077\nd=/home/mc/antiphon-server2/secrets/github-token\n");
            fixture.Stdin(1).ShouldBe(Token + "\n", "the token travels on the upload's stdin only");
            fixture.Stdin(2).ShouldBeEmpty("the verify step sends nothing");
            run.Output.ShouldNotContain(Token);
        }
        crlf.Remote(1).ShouldBe(lf.Remote(1), "upload command is identical from either checkout");
        crlf.Remote(2).ShouldBe(lf.Remote(2), "verify command is identical from either checkout");
    }

    [Test]
    public async Task Upload_failure_surfaces_remote_stderr_without_the_token()
    {
        NativeLinuxGate.Require(NativeLinuxReason);
        using var fixture = new Fixture(crlf: false);
        fixture.UploadStderr = "sudo: a password is required\nremote echoed " + Token + " back";
        var run = await fixture.RunAsync();

        run.Exit.ShouldBe(1, run.Output);
        fixture.Calls.ShouldBe(1, "no verify after a failed upload");
        run.Output.ShouldNotContain("present=true", Case.Sensitive, "no metadata line after a failure");
        run.Output.ShouldContain("not confirmed over SSH", Case.Sensitive, run.Output);
        run.Output.ShouldContain("sudo: a password is required", Case.Sensitive, "c1245-upload-stderr-surfaced");
        run.Output.ShouldContain("remote echoed [redacted] back", Case.Sensitive, "c1245-upload-stderr-redacted");
        run.Output.ShouldNotContain(Token, Case.Sensitive, "c1245-token-never-printed");
        fixture.Remote(1).ShouldNotContain(Token);
    }

    [Test]
    public async Task Verify_failure_surfaces_remote_stderr()
    {
        NativeLinuxGate.Require(NativeLinuxReason);
        using var fixture = new Fixture(crlf: false);
        fixture.VerifyStderr = "stat: cannot statx '/home/mc/antiphon-server2/secrets/github-token/token': Permission denied";
        var run = await fixture.RunAsync();

        run.Exit.ShouldBe(1, run.Output);
        fixture.Calls.ShouldBe(2, run.Output);
        run.Output.ShouldNotContain("present=true", Case.Sensitive, "no metadata line after a failure");
        run.Output.ShouldContain("owner/mode/presence verification failed", Case.Sensitive, run.Output);
        run.Output.ShouldContain("stat: cannot statx", Case.Sensitive, "c1245-verify-stderr-surfaced");
        run.Output.ShouldNotContain(Token);
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "c1245-" + Guid.NewGuid().ToString("N"));

        public Fixture(bool crlf)
        {
            Directory.CreateDirectory(Bin);
            Directory.CreateDirectory(CallDir);
            var source = File.ReadAllText(Path.Combine(DelegateScriptRunner.RepoRoot, "scripts", "refresh-server2-github-token.ps1"))
                .Replace("\r\n", "\n");
            ScriptText = crlf ? source.Replace("\n", "\r\n") : source;
            File.WriteAllText(Script, ScriptText);
            Executable("bw", "#!/bin/sh\nprintf '%s\\n' \"$C1245_TOKEN\"\n");
            // The last argument is the remote command. A carriage return fails the way
            // server2's sh does on `set -eu\r`.
            Executable("ssh", """
                #!/bin/sh
                d="$C1245_CALLS"
                n=$(( $(cat "$d/count" 2>/dev/null || echo 0) + 1 ))
                echo "$n" > "$d/count"
                for last; do :; done
                printf '%s' "$last" > "$d/remote.$n"
                cat > "$d/stdin.$n"
                cr=$(printf '\r')
                case "$last" in *"$cr"*) echo 'sh: 1: set: Illegal option -' >&2; exit 2;; esac
                if [ "$n" = 1 ] && [ -s "$d/upload.err" ]; then cat "$d/upload.err" >&2; exit 1; fi
                if [ "$n" = 2 ] && [ -s "$d/verify.err" ]; then cat "$d/verify.err" >&2; exit 1; fi
                if [ "$n" = 2 ]; then printf '1654:1654 400\npresent=true\n'; fi
                exit 0
                """ + "\n");
        }

        public string ScriptText { get; }
        public string UploadStderr { set => File.WriteAllText(Path.Combine(CallDir, "upload.err"), value + "\n"); }
        public string VerifyStderr { set => File.WriteAllText(Path.Combine(CallDir, "verify.err"), value + "\n"); }
        private string Bin => Path.Combine(_root, "bin");
        private string CallDir => Path.Combine(_root, "calls");
        private string Script => Path.Combine(_root, "refresh-server2-github-token.ps1");

        public int Calls
        {
            get
            {
                var count = Path.Combine(CallDir, "count");
                return File.Exists(count) ? int.Parse(File.ReadAllText(count).Trim()) : 0;
            }
        }

        public string Remote(int call) => File.ReadAllText(Path.Combine(CallDir, "remote." + call));
        public string Stdin(int call) => File.ReadAllText(Path.Combine(CallDir, "stdin." + call));

        public async Task<(int Exit, string Stdout, string Output)> RunAsync()
        {
            var psi = new ProcessStartInfo("pwsh")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = _root
            };
            foreach (var arg in new[] { "-NoProfile", "-NonInteractive", "-File", Script, "-Server", "mc@server2" })
                psi.ArgumentList.Add(arg);
            psi.Environment["PATH"] = Bin + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH");
            psi.Environment["HOME"] = _root;
            psi.Environment["BW_SESSION"] = "c1245-fake-session";
            psi.Environment["C1245_TOKEN"] = Token;
            psi.Environment["C1245_CALLS"] = CallDir;
            using var child = Process.Start(psi)!;
            var stdout = child.StandardOutput.ReadToEndAsync();
            var stderr = child.StandardError.ReadToEndAsync();
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            try { await child.WaitForExitAsync(deadline.Token); }
            catch { if (!child.HasExited) { child.Kill(true); await child.WaitForExitAsync(); } throw; }
            var (o, e) = (await stdout, await stderr);
            return (child.ExitCode, o, o + e);
        }

        private void Executable(string name, string body)
        {
            var path = Path.Combine(Bin, name);
            File.WriteAllText(path, body.Replace("\r\n", "\n"));
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        public void Dispose()
        {
            try { Directory.Delete(_root, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
}
