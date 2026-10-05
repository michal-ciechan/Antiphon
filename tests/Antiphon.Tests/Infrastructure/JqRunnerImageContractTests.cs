using System.Diagnostics;
using System.Text.RegularExpressions;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Infrastructure;

[Category("Unit")]
public sealed class JqRunnerImageContractTests
{
    private const string Version = "1.7.1";
    private const string Sha256 = "5942c9b0934e510ee61eb3e30273f1b3fe2590df93933a93d7c58b81d19c8ff5";

    [Test]
    public void Pinned_download_is_verified_before_root_owned_install_in_every_runner_target()
    {
        var image = Read("docker/session-runner-grok/Dockerfile");
        var stages = DockerStackDocuments.Stages(image);
        var runtime = stages.Single(stage => stage.Name == "runtime-base").Body;
        runtime.ShouldContain("ARG JQ_VERSION=" + Version + "\n");
        runtime.ShouldContain("ARG JQ_SHA256=" + Sha256 + "\n");
        var install = Regex.Match(runtime, @"(?ms)^RUN .*?jq-linux-amd64.*?(?=^\S|\z)").Value;
        install.ShouldContain("https://github.com/jqlang/jq/releases/download/jq-${JQ_VERSION}/jq-linux-amd64");
        install.ShouldContain("&& echo \"${JQ_SHA256}  /tmp/jq-download/jq\" | sha256sum -c -");
        install.ShouldContain("&& install -o root -g root -m 0755 /tmp/jq-download/jq /usr/local/bin/jq");
        install.IndexOf("sha256sum -c -", StringComparison.Ordinal).ShouldBeLessThan(
            install.IndexOf("install -o root", StringComparison.Ordinal), "a failed digest must prevent installation");
        install.ShouldContain("&& test \"$(/usr/local/bin/jq --version)\" = \"jq-${JQ_VERSION}\"");
        install.ShouldContain("&& rm -rf /tmp/jq-download");
        foreach (var stage in stages)
            Regex.IsMatch(stage.Body, @"(?s)apt-get install[^\n]*(?:\\\n[^\n]*)*\bjq\b").ShouldBeFalse("no apt jq in " + stage.Name);
        foreach (var target in new[] { "runtime", "receipt-probe", "session-testing" })
            DockerStackDocuments.Closure(stages, target).ShouldContain("/tmp/jq-download/jq /usr/local/bin/jq");
        Read("docs/docker-stack.md").ShouldContain("jq " + Version + " (static jq-linux-amd64, SHA-256 `" + Sha256
            + "`, verified before installing root-owned mode 755 at `/usr/local/bin/jq`)");
    }

    [Test]
    public void Qualification_grades_the_jq_row_for_both_targets()
    {
        var wrapper = Read("scripts/verify-card0660-codex-image.ps1");
        wrapper.ShouldContain("'jq-version' = 'unknown'");
        wrapper.ShouldContain("$rows['jq-version'] = Invoke-Probe 'jq-version' '1654:1654' @()");
        Read("docker/session-runner-grok/verify-codex-image.sh").ShouldContain("JQ_VERSION=" + Version + "\n");
    }

    [Test]
    [Arguments("jq-1.7.1", 0, "", "ok")]
    [Arguments("jq-1.7", 0, "", "fail")]
    [Arguments("jq-1.7.0", 0, "", "fail")]
    [Arguments("jq-1.7.10", 0, "", "fail")]
    [Arguments("jq-1.8.0", 0, "", "fail")]
    [Arguments("jq-1.7.1-beta", 0, "", "fail")]
    [Arguments("jq-1x7x1", 0, "", "fail")]
    [Arguments("jq-1.7.1 extra", 0, "", "fail")]
    [Arguments("jq-1.7.1\nextra", 0, "", "fail")]
    [Arguments("", 0, "", "fail")]
    [Arguments("jq-1.7.1", 1, "", "fail")]
    [Arguments("jq-1.7.1", 0, "warning", "fail")]
    [ParallelLimiter<ProcessSpawnLimit>]
    public async Task Version_row_accepts_only_exact_successful_pin_without_stderr(string output, int exitCode, string error, string outcome)
    {
        using var f = await JqFixture.CreateAsync(CancellationToken.None);
        f.WriteJq(f.Destination);
        f.VersionOutput = output;
        f.VersionExit = exitCode;
        f.VersionError = error;
        var run = await f.ProbeAsync([f.DestinationDirectory]);
        f.AssertInvocation(f.Destination, f.ProbeHome);
        run.Exit.ShouldBe(outcome == "ok" ? 0 : 1);
        run.Stderr.ShouldBeEmpty();
        run.Stdout.ShouldNotContain("reason=Jq");
        var detail = exitCode != 0 ? "exit=" + exitCode
            : output != "jq-" + Version ? $"stdout=[{output}] expected jq-{Version}"
            : error.Length != 0 ? "unexpected version stderr"
            : $"jq-{Version} as uid 1654 lookupPath={f.Destination} path={f.Destination}";
        run.Stdout.ShouldBe($"C660_ROW jq-version {outcome} {detail}\n");
    }

    [Test]
    [ParallelLimiter<ProcessSpawnLimit>]
    public async Task C1054_Jq_row_rejects_home_shadow()
    {
        using var f = await JqFixture.CreateAsync(CancellationToken.None);
        f.WriteJq(f.Destination);
        f.WriteJq(f.HomeJq);
        File.ReadAllBytes(f.HomeJq).ShouldBe(File.ReadAllBytes(f.Destination), "identical working copies");
        await f.AssertHealthyAsync(f.Destination);
        await f.AssertHealthyAsync(f.HomeJq);
        AssertRefused(f, await f.ProbeAsync([f.HomeBin, f.DestinationDirectory]),
            "JqPathUnapproved", f.HomeJq, f.HomeJq, "c1054-home-present-refused");

        File.Delete(f.Destination);
        AssertRefused(f, await f.ProbeAsync([f.HomeBin, f.DestinationDirectory]),
            "JqPathUnapproved", f.HomeJq, f.HomeJq, "c1054-home-absent-refused");

        f.WriteJq(f.Destination);
        AssertRefused(f, await f.ProbeAsync([]), "JqNotFound", "unavailable", "unavailable", "c1054-missing-refused");
        File.Delete(f.Destination);
        File.Delete(f.HomeJq);
        AssertRefused(f, await f.ProbeAsync([f.HomeBin, f.DestinationDirectory]),
            "JqNotFound", "unavailable", "unavailable", "c1054-missing-everywhere-refused");

        f.WriteJq(f.Destination);
        AssertRefused(f, await f.ProbeAsync(["."], cwd: f.DestinationDirectory),
            "JqLookupInvalid", "./jq", "unavailable", "c1054-relative-refused");
        File.Delete(f.Tools + "/readlink");
        AssertRefused(f, await f.ProbeAsync([f.DestinationDirectory]),
            "JqResolveFailed", f.Destination, "unavailable", "c1054-resolution-refused");
        f.LinkTool("readlink");

        f.WriteJq(f.HomeJq);
        AssertRefused(f, await f.ProbeAsync([f.HomeBin, f.DestinationDirectory], cached: true),
            "JqPathUnapproved", f.HomeJq, f.HomeJq, "c1054-cache-cleared-refused");
    }

    [Test]
    [ParallelLimiter<ProcessSpawnLimit>]
    public async Task C1054_Jq_row_accepts_canonical_file_and_alias()
    {
        using var f = await JqFixture.CreateAsync(CancellationToken.None);
        f.WriteJq(f.Destination);
        AssertAccepted(f, await f.ProbeAsync([f.DestinationDirectory]), f.Destination, "c1054-canonical-accepted");
        var alias = f.LinkJq("alias", f.Destination);
        AssertAccepted(f, await f.ProbeAsync([Path.GetDirectoryName(alias)!, f.DestinationDirectory]),
            alias, "c1054-alias-accepted");
        f.WriteJq(f.HomeJq);
        File.SetUnixFileMode(f.HomeJq, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        AssertAccepted(f, await f.ProbeAsync([f.HomeBin, f.DestinationDirectory]), f.Destination, "c1054-nonexecutable-fallback");
    }

    [Test]
    [ParallelLimiter<ProcessSpawnLimit>]
    public async Task C1054_Jq_row_rejects_canonical_leaf_symlink()
    {
        using var f = await JqFixture.CreateAsync(CancellationToken.None);
        f.WriteJq(f.HomeJq);
        await f.AssertHealthyAsync(f.HomeJq);
        File.CreateSymbolicLink(f.Destination, f.HomeJq);
        AssertRefused(f, await f.ProbeAsync([f.DestinationDirectory]),
            "JqPathUnapproved", f.Destination, f.HomeJq, "c1054-canonical-leaf-refused");
        var alias = f.LinkJq("alias", f.Destination);
        AssertRefused(f, await f.ProbeAsync([Path.GetDirectoryName(alias)!, f.DestinationDirectory]),
            "JqPathUnapproved", alias, f.HomeJq, "c1054-aliased-leaf-refused");

        File.Delete(f.Destination);
        File.CreateSymbolicLink(f.Destination, f.Root + "/missing");
        AssertRefused(f, await f.ProbeAsync([f.DestinationDirectory]),
            "JqNotFound", "unavailable", "unavailable", "c1054-dangling-leaf-refused");
        File.Delete(f.Destination);
        File.CreateSymbolicLink(f.Destination, f.Destination);
        AssertRefused(f, await f.ProbeAsync([f.DestinationDirectory]),
            "JqNotFound", "unavailable", "unavailable", "c1054-loop-leaf-refused");
        File.Delete(f.Destination);
        Directory.CreateDirectory(f.Destination);
        AssertRefused(f, await f.ProbeAsync([f.DestinationDirectory]),
            "JqNotFound", "unavailable", "unavailable", "c1054-directory-leaf-refused");
        Directory.Delete(f.Destination);
        f.WriteJq(f.Destination);
        File.SetUnixFileMode(f.Destination, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        AssertRefused(f, await f.ProbeAsync([f.DestinationDirectory]),
            "JqNotFound", "unavailable", "unavailable", "c1054-nonexecutable-leaf-refused");
    }

    [Test]
    [ParallelLimiter<ProcessSpawnLimit>]
    public async Task C1054_Jq_row_records_found_and_resolved_paths()
    {
        using var f = await JqFixture.CreateAsync(CancellationToken.None);
        f.WriteJq(f.Destination);
        f.WriteJq(f.HomeJq);
        await f.AssertHealthyAsync(f.HomeJq);
        AssertAccepted(f, await f.ProbeAsync([f.DestinationDirectory]), f.Destination, "c1054-record-canonical");
        var alias = f.LinkJq("alias", f.Destination);
        AssertAccepted(f, await f.ProbeAsync([Path.GetDirectoryName(alias)!]), alias, "c1054-record-alias");
        AssertRefused(f, await f.ProbeAsync([f.HomeBin, f.DestinationDirectory]),
            "JqPathUnapproved", f.HomeJq, f.HomeJq, "c1054-record-home");
        File.Delete(f.Destination);
        File.CreateSymbolicLink(f.Destination, f.HomeJq);
        AssertRefused(f, await f.ProbeAsync([f.DestinationDirectory]),
            "JqPathUnapproved", f.Destination, f.HomeJq, "c1054-record-leaf");
        File.Delete(f.Destination);
        f.WriteJq(f.Destination);
        AssertRefused(f, await f.ProbeAsync([]), "JqNotFound", "unavailable", "unavailable", "c1054-record-missing");
        File.Delete(f.Tools + "/readlink");
        AssertRefused(f, await f.ProbeAsync([f.DestinationDirectory]),
            "JqResolveFailed", f.Destination, "unavailable", "c1054-record-resolution");
        f.LinkTool("readlink");

        // Literal Bash encodings are independent of the production formatter.
        // Newline is first in each family so escaping controls hit the line assertion.
        (string Name, string Encoded, bool Ansi)[] names =
        [
            ("name\nline", "name\\nline", true),
            ("name space", "name\\ space", false),
            ("name\ttab", "name\\ttab", true),
            ("name\rreturn", "name\\rreturn", true),
            ("name'quote", "name\\'quote", false),
            ("name\\slash", "name\\\\slash", false)
        ];
        foreach (var (name, encoded, ansi) in names)
        {
            var found = f.LinkJq("aliases/" + name, f.Destination);
            var expected = f.Root + "/aliases/" + encoded + "/jq";
            if (ansi) expected = "$'" + expected + "'";
            AssertAccepted(f, await f.ProbeAsync([Path.GetDirectoryName(found)!]), expected, "c1054-record-escaped-alias");
        }
        foreach (var (name, encoded, ansi) in names)
        {
            var target = f.Root + "/home/" + name + "/jq";
            f.WriteJq(target);
            await f.AssertHealthyAsync(target);
            File.Delete(alias);
            File.CreateSymbolicLink(alias, target);
            var expected = f.Root + "/home/" + encoded + "/jq";
            if (ansi) expected = "$'" + expected + "'";
            AssertRefused(f, await f.ProbeAsync([Path.GetDirectoryName(alias)!]),
                "JqPathUnapproved", alias, expected, "c1054-record-escaped-target");
        }
    }

    private static void AssertAccepted(JqFixture f, ProbeRun run, string lookup, string assertion)
    {
        run.Exit.ShouldBe(0, assertion);
        AssertReceipt(run, $"C660_ROW jq-version ok jq-{Version} as uid 1654", lookup, f.Destination);
        f.AssertInvocation(f.Destination, f.ProbeHome);
    }

    private static void AssertRefused(JqFixture f, ProbeRun run, string reason, string lookup, string resolved, string assertion)
    {
        run.Exit.ShouldBe(1, assertion);
        AssertReceipt(run, "C660_ROW jq-version fail reason=" + reason, lookup, resolved);
        f.Trace.ShouldBeEmpty("c1054-refusal-no-execution");
    }

    private static void AssertReceipt(ProbeRun run, string prefix, string lookup, string resolved)
    {
        run.Stderr.ShouldBeEmpty();
        run.Stdout.ShouldEndWith("\n");
        var physicalLines = run.Stdout[..^1].Split('\n');
        physicalLines.Length.ShouldBe(1, "c1054-row-single-line");
        physicalLines[0].Any(char.IsControl).ShouldBeFalse("receipt contains no raw control bytes");
        physicalLines[0].ShouldStartWith(prefix + " lookupPath=" + lookup + " path=", "c1054-found-path");
        physicalLines[0].ShouldEndWith(" path=" + resolved, "c1054-resolved-path");
        run.Stdout.ShouldBe(prefix + " lookupPath=" + lookup + " path=" + resolved + "\n");
    }

    private sealed record ProbeRun(int Exit, string Stdout, string Stderr);

    private sealed class JqFixture : IDisposable
    {
        public string Root { get; } = "/tmp/c1054-" + Guid.NewGuid().ToString("N");
        public string DestinationDirectory => Root + "/destination";
        public string Destination => DestinationDirectory + "/jq";
        public string HomeBin => Root + "/home/app/.local/bin";
        public string HomeJq => HomeBin + "/jq";
        public string Tools => Root + "/tools";
        public string ProbeHome => Root + "/probe-home";
        public string Trace => File.ReadAllText(Root + "/trace");
        public string VersionOutput { get; set; } = "jq-" + Version;
        public string VersionError { get; set; } = "";
        public int VersionExit { get; set; }
        private readonly string _bash;

        private JqFixture()
        {
            OperatingSystem.IsLinux().ShouldBeTrue("CARD-1054 requires the native Linux lane");
            _bash = Locate("bash");
            Directory.CreateDirectory(Root, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        public static async Task<JqFixture> CreateAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var f = new JqFixture();
            try
            {
                foreach (var directory in new[] { f.DestinationDirectory, f.HomeBin, f.Tools, f.ProbeHome, f.Root + "/alias" })
                    Directory.CreateDirectory(directory);
                foreach (var tool in new[] { "id", "env", "readlink" }) f.LinkTool(tool);
                File.WriteAllText(f.Root + "/trace", "");
                var uid = await f.RunAsync(f._bash, ["--noprofile", "--norc", "-c", "id -u"], []);
                uid.Exit.ShouldBe(0);
                uid.Stderr.ShouldBeEmpty();
                Regex.IsMatch(uid.Stdout, "^[0-9]+\n$").ShouldBeTrue("real native uid");
                var source = Read("docker/session-runner-grok/verify-codex-image.sh");
                source = ReplaceOnce(source, "JQ_DESTINATION=/usr/local/bin/jq\n", "JQ_DESTINATION=" + Quote(f.Destination) + "\n");
                source = ReplaceOnce(source, "PROBE_HOME=/c660-home\n", "PROBE_HOME=" + Quote(f.ProbeHome) + "\n");
                var arms = Regex.Matches(source, @"(?ms)^  jq-version\)\n.*?^    ;;$");
                arms.Count.ShouldBe(1, "one jq-version arm");
                var arm = arms[0];
                source = source[..arm.Index] + ReplaceOnce(arm.Value, "    need_uid 1654\n", "    need_uid " + uid.Stdout.Trim() + "\n")
                    + source[(arm.Index + arm.Length)..];
                File.WriteAllText(f.Root + "/probe.sh", source);
                return f;
            }
            catch { f.Dispose(); throw; }
        }

        public void LinkTool(string tool) => File.CreateSymbolicLink(Tools + "/" + tool, Locate(tool));

        public void WriteJq(string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "#!" + _bash + "\n" + """
                printf '%s\0' "$0" "$#" "$@" "$HOME" >> "$JQ_TRACE"
                printf '%s\n' "$JQ_STDOUT"
                printf '%s' "$JQ_STDERR" >&2
                exit "$JQ_EXIT"
                """ + "\n");
            File.SetUnixFileMode(path, (UnixFileMode)Convert.ToInt32("755", 8));
        }

        public string LinkJq(string directory, string target)
        {
            var path = Root + "/" + directory + "/jq";
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.CreateSymbolicLink(path, target);
            return path;
        }

        public async Task AssertHealthyAsync(string path)
        {
            File.WriteAllText(Root + "/trace", "");
            var run = await RunAsync(path, ["--version"], []);
            run.Exit.ShouldBe(0, "healthy fixture exit");
            run.Stdout.ShouldBe("jq-" + Version + "\n", "healthy fixture version");
            run.Stderr.ShouldBeEmpty();
            AssertInvocation(path, Root + "/home");
            File.WriteAllText(Root + "/trace", "");
        }

        public void AssertInvocation(string path, string home)
        {
            var fields = Trace.Split('\0');
            fields.Length.ShouldBe(5, "one complete NUL-delimited invocation");
            fields[0].ShouldBe(path, "c1054-invoke-resolved");
            fields[1].ShouldBe("1");
            fields[2].ShouldBe("--version");
            fields[3].ShouldBe(home);
            fields[4].ShouldBeEmpty();
        }

        public Task<ProbeRun> ProbeAsync(string[] directories, bool cached = false, string? cwd = null)
        {
            File.WriteAllText(Root + "/trace", "");
            // Source in the SAME shell as the seeded hash; never reassign PATH after it.
            var args = cached
                ? new[] { "--noprofile", "--norc", "-c", "hash -p \"$1\" jq; [ \"$(command -v jq)\" = \"$1\" ] || exit 2; source \"$2\" jq-version", "c1054", Destination, Root + "/probe.sh" }
                : new[] { "--noprofile", "--norc", Root + "/probe.sh", "jq-version" };
            return RunAsync(_bash, args, directories, cwd);
        }

        private async Task<ProbeRun> RunAsync(string executable, string[] args, string[] directories, string? cwd = null)
        {
            var start = new ProcessStartInfo(executable)
            {
                UseShellExecute = false, WorkingDirectory = cwd ?? Root,
                RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true
            };
            foreach (var arg in args) start.ArgumentList.Add(arg);
            start.Environment.Clear();
            start.Environment["PATH"] = string.Join(':', directories.Append(Tools));
            start.Environment["LC_ALL"] = "C";
            start.Environment["HOME"] = Root + "/home";
            start.Environment["JQ_TRACE"] = Root + "/trace";
            start.Environment["JQ_STDOUT"] = VersionOutput;
            start.Environment["JQ_STDERR"] = VersionError;
            start.Environment["JQ_EXIT"] = VersionExit.ToString(System.Globalization.CultureInfo.InvariantCulture);
            using var process = Process.Start(start) ?? throw new InvalidOperationException("native child did not start");
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            var completed = Task.WhenAll(process.WaitForExitAsync(), stdout, stderr);
            try
            {
                process.StandardInput.Close();
                await completed.WaitAsync(TimeSpan.FromSeconds(60));
                return new(process.ExitCode, await stdout, await stderr);
            }
            catch
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
                await completed.WaitAsync(TimeSpan.FromSeconds(10));
                throw;
            }
        }

        private static string Locate(string tool)
        {
            var path = new[] { "/usr/bin/" + tool, "/bin/" + tool }.FirstOrDefault(File.Exists);
            path.ShouldNotBeNull("native fixture prerequisite: " + tool);
            (File.GetUnixFileMode(path!) & (UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute))
                .ShouldNotBe((UnixFileMode)0, "executable prerequisite: " + tool);
            return path!;
        }

        private static string ReplaceOnce(string source, string before, string after)
        {
            source.Split(before, StringSplitOptions.None).Length.ShouldBe(2, "exact fixture replacement inventory: " + before);
            return source.Replace(before, after, StringComparison.Ordinal);
        }

        private static string Quote(string value) => "'" + value.Replace("'", "'\\''", StringComparison.Ordinal) + "'";

        public void Dispose() => Directory.Delete(Root, recursive: true);
    }

    private static string Read(string path) => DockerStackDocuments.Read(path).Replace("\r\n", "\n");
}
