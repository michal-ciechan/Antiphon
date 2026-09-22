using Antiphon.Tests.Application;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

// CARD-0604 S3. Text guards over the remote script's two lanes. The script only ever executes on
// server2 (host lane) or inside the persistent runner (nested lane), so these are the only guards
// that run on Windows at all; CP-5 through CP-13 are the live rows.
[Category("Unit")]
public sealed class RemoteScriptContractTests
{
    [Test]
    public void Nested_lane_never_uses_sudo_or_python()
    {
        var text = Remote();

        // The runner image has no python3 at all: every JSON write, every count and every context
        // check is shell. One `python3` reintroduced anywhere kills the whole nested lane.
        Executable(text, "python3").ShouldBeEmpty("nested lane invokes python3");
        Executable(text, "python ").ShouldBeEmpty("nested lane invokes python");

        // sudo is the host lane's alone, and only inside the lane guard: the nested lane runs as
        // uid 1654 with no sudoers rule, so a sudo outside that branch is an unconditional failure.
        var sudoLines = Executable(text, "sudo");
        sudoLines.ShouldNotBeEmpty("the host lane still elevates to create its own directories");
        foreach (var line in sudoLines)
            EnsureDirsBody(text).Contains(line, StringComparison.Ordinal)
                .ShouldBeTrue("sudo outside ensure_dirs' host branch: " + line);
        EnsureDirsBody(text).ShouldContain("if [ \"$LANE\" = \"host\" ]; then");
    }

    [Test]
    public void Scrub_covers_github_token_prefixes()
    {
        var scrub = Block(Remote(), "scrub_file");
        // Every GitHub token prefix, not only the gho_ the CARD-0590 pattern covered.
        foreach (var prefix in new[] { "ghp_", "gho_", "ghu_", "ghs_", "ghr_" })
            Matches(scrub, prefix).ShouldBeTrue("scrub pattern does not cover " + prefix);
        Matches(scrub, "github_pat_11ABCDEFG0aBcDeFgHiJkL").ShouldBeTrue("scrub pattern does not cover github_pat_");
        scrub.ShouldContain("PRIVATE KEY");
    }

    [Test]
    public void Smoke_deletes_its_branch()
    {
        var smoke = Block(Remote(), "case_git_smoke");
        smoke.ShouldContain("throwaway/c604-credential-smoke-$RUN");
        smoke.ShouldContain("git -C \"$work\" push origin --delete \"$branch\"");
        smoke.ShouldContain("SmokeBranchNotDeleted");
        // Deletion is verified against origin, not assumed from the push's exit code.
        smoke.ShouldContain("ls-remote --exit-code --heads origin \"$branch\"");
        smoke.ShouldContain("SmokeBranchSurvived");
        Order(smoke, "git push origin \"HEAD:$branch\"", "push origin --delete").ShouldBeTrue();
        // The credential is the deploy key on its tmpfs, never a desktop-sourced token.
        smoke.ShouldContain("/run/antiphon/deploy-key");
        smoke.ShouldNotContain("gh-token");
        smoke.ShouldNotContain("GIT_ASKPASS");
    }

    [Test]
    public void Child_project_is_run_scoped_and_distinct()
    {
        var text = Remote();
        text.ShouldContain("HOST_PROJECT=\"antiphon-runner\"");
        text.ShouldContain("CHILD_PROJECT=\"c604${RUN}\"");
        // The nested child never composes the persistent project, and the host lane never
        // composes the run-scoped one: a collision would take the standing runner down.
        Block(text, "compose_child").ShouldContain("COMPOSE_PROJECT_NAME=\"$CHILD_PROJECT\"");
        Block(text, "compose_child").ShouldNotContain("HOST_PROJECT");
        Block(text, "compose_host").ShouldContain("-p \"$HOST_PROJECT\"");
        Block(text, "compose_host").ShouldNotContain("CHILD_PROJECT");
        // `down -v` is only ever aimed at the child, never at the persistent project.
        foreach (var line in text.Replace("\r\n", "\n").Split('\n'))
        {
            var trimmed = line.Trim();
            if (!trimmed.Contains("down -v", StringComparison.Ordinal))
                continue;
            (trimmed.StartsWith("compose_child ", StringComparison.Ordinal)
                || trimmed.Contains("-p \"$project\"", StringComparison.Ordinal))
                .ShouldBeTrue("down -v aimed at something other than the child or a retired c590 project: " + trimmed);
        }
    }

    [Test]
    public void Host_daemon_is_never_pruned()
    {
        // D-9: prune inside the nested daemon is permitted; prune on the host daemon is forbidden
        // because am-service, traefik, windmill and schoolrevision-* share it.
        foreach (var line in Remote().Replace("\r\n", "\n").Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("#", StringComparison.Ordinal))
                continue;
            trimmed.Contains("prune", StringComparison.Ordinal)
                .ShouldBeFalse("remote script prunes a daemon: " + trimmed);
        }
    }

    [Test]
    public void Every_case_declares_a_lane()
    {
        var text = Remote();
        text.ShouldContain("detect_lane > /dev/null");
        text.ShouldContain("WrongLane want=$want lane=$LANE");
        // The three cases that change standing state are host-lane, and the nested roster is not.
        foreach (var name in new[] { "case_deploy_parent", "case_nested_residue", "case_persistent_restart", "case_handoff" })
            Block(text, name).ShouldContain("require_lane host");
        foreach (var name in new[] { "case_throwaway", "case_deployment_state", "case_git_smoke", "case_dotnet", "case_client_tests_body" })
            Block(text, name).ShouldContain("require_lane nested");
    }

    private static bool Matches(string pattern, string sample)
    {
        // The scrub is a sed -E expression; check the sample against the same alternation.
        foreach (var expression in new[]
                 {
                     @"gh[pousr]_[A-Za-z0-9_]+",
                     @"github_pat_[A-Za-z0-9_]+",
                 })
        {
            if (!pattern.Contains(expression, StringComparison.Ordinal))
                continue;
            if (System.Text.RegularExpressions.Regex.IsMatch(sample + "XXXXXXXX", expression))
                return true;
        }

        return false;
    }

    private static bool Order(string text, string first, string second)
    {
        var a = text.IndexOf(first, StringComparison.Ordinal);
        var b = text.IndexOf(second, StringComparison.Ordinal);
        return a >= 0 && b >= 0 && a < b;
    }

    private static IReadOnlyList<string> Executable(string text, string token) =>
        text.Replace("\r\n", "\n").Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && line[0] != '#')
            .Where(line => line.Contains(token, StringComparison.Ordinal))
            .ToList();

    private static string EnsureDirsBody(string text) => Block(text, "ensure_dirs");

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
        File.ReadAllText(Path.Combine(DelegateScriptRunner.RepoRoot, "scripts", "c590-remote.sh"));
}
