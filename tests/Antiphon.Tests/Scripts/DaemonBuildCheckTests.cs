using System.Net.Http;
using System.Xml.Linq;
using Antiphon.Nightly;
using Antiphon.Tests.Scripts.Nightly;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// CARD-0604: the daemon build staleness check (Antiphon.Nightly.DaemonBuildCheck, the body of scripts/check-daemon-build.ps1
/// since unit 30 of the 2026-11-22 plan; the script is a thin launcher restart-apphost.ps1 and dev-aspire.ps1 run) in
/// process: the /capabilities reads on a recording transport, git on a FakeGit, the lines and the exit. The runner's path
/// list is checked against its project reference closure (formerly RunnerCapabilitiesTests, which read the script's text).
/// </summary>
[Category("Unit")]
public sealed class DaemonBuildCheckTests
{
    private const string Runner = "1111111111111111111111111111111111111111";
    private const string Gateway = "2222222222222222222222222222222222222222";

    private sealed record CheckRun(int Exit, string[] Lines, List<string> Requests);

    private static CheckRun Run(Func<string, NightlyReportHttpResponse> answer, FakeGit git, params string[] arguments)
    {
        using var output = new StringWriter();
        var host = new NightlyHost
        {
            Clock = new FakeTimeProvider(NightlyFixture.DefaultNow), Git = git.Run, Launcher = new RecordingLauncher(), Processes = new FakeProcessTable(),
            Probe = new FakeHostProbe(), Out = output, Error = TextWriter.Null, Trace = TextWriter.Null,
        };
        var requests = new List<string>();
        var exit = DaemonBuildCheck.Run(DaemonBuildCheckOptions.Parse(arguments, "C:/repo"), host, (method, uri, headers, body) =>
        {
            requests.Add(method + " " + uri);
            body.ShouldBeNull();
            return answer(uri);
        });
        return new CheckRun(exit, output.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries), requests);
    }

    private static NightlyReportHttpResponse Build(string sha, string extra = "") => new(200, "{\"build\":{\"commitSha\":\"" + sha + "\"" + extra + "}}");

    [Test]
    public void A_daemon_that_is_down_refuses_or_reports_no_commit_is_named_and_skipped()
    {
        var git = new FakeGit();
        var down = Run(uri => uri.Contains(":17204", StringComparison.Ordinal) ? throw new HttpRequestException("refused") : new NightlyReportHttpResponse(404, "{}"), git);
        down.Exit.ShouldBe(0);
        down.Requests.ShouldBe(["GET http://localhost:17204/capabilities", "GET http://localhost:17208/capabilities"]);
        down.Lines.ShouldBe([
            "  session-runner: build identity unavailable (daemon is down or predates /capabilities)",
            "  fake-gateway: build identity unavailable (daemon is down or predates /capabilities)",
        ]);

        var silent = Run(uri => uri.Contains(":17204", StringComparison.Ordinal) ? new NightlyReportHttpResponse(200, "not json") : Build("  "), git);
        silent.Lines.ShouldBe([
            "  session-runner: build identity unavailable (daemon did not report a commit SHA)",
            "  fake-gateway: build identity unavailable (daemon did not report a commit SHA)",
        ]);
        git.Calls.ShouldBeEmpty();
    }

    [Test]
    public void Unrelated_history_and_a_failed_comparison_are_named()
    {
        var git = new FakeGit("", 0)
            .Reply(arguments => arguments.Contains("merge-base") && arguments.Contains(Runner), new NightlyGitResult(1, ""))
            .Reply("log", new NightlyGitResult(128, "fatal: bad revision"));
        var run = Run(uri => uri.Contains(":17204", StringComparison.Ordinal) ? Build(Runner) : Build(Gateway), git, "--fail-on-stale");
        run.Exit.ShouldBe(0);
        run.Lines.ShouldBe([
            "  session-runner: build " + Runner + " is not an ancestor of HEAD; not comparing unrelated history",
            "  fake-gateway: could not compare build " + Gateway + " to HEAD",
        ]);
    }

    [Test]
    public void A_current_daemon_reports_its_short_build_and_the_comparison_is_its_project_closure()
    {
        var git = new FakeGit("", 0);
        var run = Run(_ => Build(Runner), git);
        run.Exit.ShouldBe(0);
        run.Lines.ShouldBe([
            "  session-runner: build 1111111 has no newer source changes in its project closure",
            "  fake-gateway: build 1111111 has no newer source changes in its project closure",
        ]);
        var calls = git.Calls;
        calls.ShouldAllBe(call => call.WorkingDirectory == "C:/repo");
        calls[0].Arguments.ShouldBe(["merge-base", "--is-ancestor", Runner, "HEAD"]);
        calls[1].Arguments.ShouldBe(new[] { "log", "--oneline", Runner + "..HEAD", "--" }.Concat(DaemonBuildCheck.Daemons[0].Paths));
        calls[3].Arguments.ShouldBe(["log", "--oneline", Runner + "..HEAD", "--", "src/Antiphon.Messaging.FakeGateway", "src/Antiphon.Messaging.Gateway", "src/Antiphon.Messaging"]);
    }

    [Test]
    public void A_stale_daemon_warns_with_at_most_five_changes_and_the_fix()
    {
        var changes = string.Concat(Enumerable.Range(1, 7).Select(i => "c0ffee" + i + " change " + i + Environment.NewLine));
        var git = new FakeGit("", 0).Reply("log", new NightlyGitResult(0, changes));
        NightlyReportHttpResponse Answer(string uri) => uri.Contains(":17204", StringComparison.Ordinal)
            ? Build(Runner, ",\"assemblyWriteTimeUtc\":\"2026-11-20T10:00:00.0000000Z\"")
            : new NightlyReportHttpResponse(503, "{}");
        var run = Run(Answer, git);
        run.Exit.ShouldBe(0, "advisory by default");
        run.Lines.ShouldBe([
            "WARNING: session-runner is stale for 7 source change(s).",
            "  build " + Runner + "; built 2026-11-20T10:00:00.0000000Z; running since unknown start time",
            "  c0ffee1 change 1", "  c0ffee2 change 2", "  c0ffee3 change 3", "  c0ffee4 change 4", "  c0ffee5 change 5",
            "  Fix when it is safe to restart: scripts/antiphon restart-session-runner",
            "  fake-gateway: build identity unavailable (daemon is down or predates /capabilities)",
        ]);
        Run(Answer, git, "--fail-on-stale").Exit.ShouldBe(1);
        Should.Throw<NightlyUsageException>(() => DaemonBuildCheckOptions.Parse(["-FailOnStale"], "C:/repo")).Message.ShouldBe("unexpected argument '-FailOnStale'");
    }

    [Test]
    public void The_runner_paths_cover_the_runner_project_reference_closure()
    {
        var root = NightlyPlanCases.Repo;
        var runner = DaemonBuildCheck.Daemons.Single(daemon => daemon.Name == "session-runner");
        runner.Port.ShouldBe(17204);
        foreach (var project in ProjectClosure(root, "src/Antiphon.SessionRunner/Antiphon.SessionRunner.csproj"))
        {
            var relative = Path.GetRelativePath(root, Path.GetDirectoryName(project)!).Replace('\\', '/');
            runner.Paths.ShouldContain(relative, $"daemon check omits runner dependency {relative}");
        }
        File.ReadAllBytes(Path.Combine(root, "scripts", "lib", "Antiphon.Nightly", "DaemonBuildCheck.cs")).ShouldAllBe(b => b < 128);
    }

    private static IReadOnlyCollection<string> ProjectClosure(string root, string entryProject)
    {
        var pending = new Queue<string>();
        var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        pending.Enqueue(Path.GetFullPath(Path.Combine(root, entryProject)));
        while (pending.TryDequeue(out var project))
        {
            if (!found.Add(project)) continue;
            foreach (var reference in XDocument.Load(project).Descendants("ProjectReference"))
            {
                var include = reference.Attribute("Include")?.Value;
                if (!string.IsNullOrWhiteSpace(include))
                    pending.Enqueue(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(project)!, include.Replace('\\', Path.DirectorySeparatorChar))));
            }
        }
        return found;
    }
}
