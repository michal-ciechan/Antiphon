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

        // sudo is the host lane's alone: the nested lane runs as uid 1654, whose ONLY sudo grant
        // is the two custody helpers (CARD-0604 D-17), so a general sudo invocation from nested
        // shell would fail anyway and a new one is an unconditional failure here. Merely naming
        // the path (an assertion that the runtime image does NOT carry sudo) is the opposite of
        // an invocation and must not trip this.
        //
        // Two places may invoke it. `ensure_dirs`' host branch elevates on the server2 HOST to
        // create its own directories. `case_custody_containment` is a host-lane case whose sudo
        // is inside a `docker exec -u 1654` payload -- it is executed by the container as the app
        // uid, against the allow-listed grant, which is the very thing that case measures.
        var sudoLines = Executable(text, "sudo")
            .Where(line => System.Text.RegularExpressions.Regex.IsMatch(line, @"(^|[;&|(]\s*)sudo\s"))
            .ToList();
        sudoLines.ShouldNotBeEmpty("the host lane still elevates to create its own directories");
        var containment = Block(text, "case_custody_containment");
        foreach (var line in sudoLines)
            (EnsureDirsBody(text).Contains(line, StringComparison.Ordinal)
                || containment.Contains(line, StringComparison.Ordinal))
                .ShouldBeTrue("sudo outside ensure_dirs' host branch or the containment case: " + line);
        EnsureDirsBody(text).ShouldContain("if [ \"$LANE\" = \"host\" ]; then");

        // And the containment case's sudo only READS the grant; it never runs a helper directly
        // (the probe does that, as uid 1654) and never elevates anything else.
        foreach (var line in sudoLines.Where(l => containment.Contains(l, StringComparison.Ordinal)))
            line.ShouldContain("sudo -n -l");
        containment.ShouldContain("docker exec -u 1654");
    }

    // CARD-0604 S12 / R-5, G-40 (Cut B). The fence inverts with the cut: the session-testing
    // image IS now a supported producer, so the payload case must prove the custody mechanism is
    // present and root-owned -- and must still refuse any trace of the WINDOWS backend, which on
    // a Linux image could only ever be a fabricated claim. The runtime and receipt-probe targets
    // keep carrying none of it.
    [Test]
    public void Testing_payload_expects_linux_backend_never_windows()
    {
        var payload = Block(Remote(), "case_testing_payload");

        payload.ShouldContain("test -x /usr/local/bin/antiphon-custody-enter");
        payload.ShouldContain("test -x /usr/local/bin/antiphon-custody-kill");
        payload.ShouldContain("test -f /etc/sudoers.d/antiphon-custody");

        // Root-owned and 0755/0440, checked on the real image rather than trusted from the
        // Dockerfile: a COPY whose ownership is wrong hands uid 1654 the shim itself.
        payload.ShouldContain("stat -c %U:%G:%a /usr/local/bin/antiphon-custody-enter)\" = \"root:root:755");
        payload.ShouldContain("stat -c %U:%G:%a /usr/local/bin/antiphon-custody-kill)\" = \"root:root:755");
        payload.ShouldContain("stat -c %U:%G:%a /etc/sudoers.d/antiphon-custody)\" = \"root:root:440");

        // The Windows backend must not appear anywhere the runner would read it.
        payload.ShouldContain("! grep -a -q windows-job-v1 /etc/sudoers.d/antiphon-custody");

        // And the Cut A claim that this image advertises NO custody at all is gone: leaving it
        // standing would fail the moment the cut it is guarding actually lands.
        payload.Contains("! grep -a -q VerificationCustodyV1", StringComparison.Ordinal)
            .ShouldBeFalse("the no-custody assertion is superseded by Cut B");
    }

    // CARD-0604 V-28 (cgroup v1 half). The measurement is a case, not a comment: it runs the
    // same probe the Docker Desktop harness runs on v2, as uid 1654 through docker exec, and
    // refuses if it reads v2 -- which would mean the freezer path is still unmeasured.
    [Test]
    public void Custody_containment_case_measures_the_v1_path_as_the_app_uid()
    {
        var block = Block(Remote(), "case_custody_containment");

        block.ShouldContain("docker exec -u 1654");
        block.ShouldContain("antiphon-custody-containment-probe");
        block.ShouldContain("containment=ok");
        block.ShouldContain("cgroup_version=v1");
        block.ShouldContain("ExpectedCgroupV1");
        block.ShouldContain("CustodyNotAdvertised");
        block.ShouldContain("WindowsBackendAdvertisedOnLinux");
        block.ShouldContain("CustodyHelpersNotRootOwned");
        block.ShouldContain("CustodyResidue");
        // Never as root: running the probe as root would measure a mechanism nobody uses.
        block.Contains("docker exec -u 0", StringComparison.Ordinal).ShouldBeFalse();
        block.Contains("docker exec \"$container\" /usr/local/bin/antiphon-custody-containment-probe",
            StringComparison.Ordinal).ShouldBeFalse("the probe must be run as uid 1654");
    }

    // CP-15 dispatches `verify-docker-stack.ps1 -Case custody-containment`, which reaches the
    // remote ONLY if the case is in c590-real.ps1's live roster. Omitted, it falls through to the
    // default arm and reports "RealCasePending" -- a red checkpoint for a routing reason, with
    // the v1 freezer path never measured and nothing on server2 touched.
    [Test]
    public void Custody_containment_is_routed_to_the_remote_and_not_left_pending()
    {
        var live = System.IO.File.ReadAllText(System.IO.Path.Combine(
            Infrastructure.DockerStackDocuments.RepoRoot, "scripts", "c590-real.ps1"));
        var stack = System.IO.File.ReadAllText(System.IO.Path.Combine(
            Infrastructure.DockerStackDocuments.RepoRoot, "scripts", "verify-docker-stack.ps1"));

        live.ShouldContain("'custody-containment'");
        // The stub boundary exists too, and refuses the same things the remote refuses first.
        stack.ShouldContain("'custody-containment' {");
        stack.ShouldContain("ExpectedCgroupV1");
        stack.ShouldContain("CustodyHelpersNotRootOwned");
        stack.ShouldContain("CustodyResidue");
        // Every host-lane case the remote script implements must be routed, or the row is a stub.
        var remote = Remote();
        foreach (var hostCase in new[] { "custody-containment", "deploy-parent", "nested-residue" })
            live.Contains("'" + hostCase + "'", StringComparison.Ordinal)
                .ShouldBeTrue(hostCase + " is implemented in c590-remote.sh but never routed there");
        remote.ShouldContain("custody-containment) case_custody_containment");
    }

    // V-29 / R-5 on the live runner: the linux-custody case REQUIRES the Linux backend and a
    // store id, and names the Windows backend only to refuse it.
    [Test]
    public void Linux_custody_case_requires_the_linux_backend()
    {
        var text = System.IO.File.ReadAllText(System.IO.Path.Combine(
            Infrastructure.DockerStackDocuments.RepoRoot, "scripts", "verify-docker-stack.ps1"));

        text.ShouldContain("'linux-custody'");
        text.ShouldContain("CustodyNotAdvertised");
        text.ShouldContain("WindowsBackendAdvertisedOnLinux");
        text.ShouldContain("RunnerStoreIdMissing");
        text.ShouldContain("-ne 'linux-cgroup-v1'");
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
            // Comments explain the rule; only executable lines are bound by it.
            if (trimmed.StartsWith("#", StringComparison.Ordinal))
                continue;
            if (!trimmed.Contains("down -v", StringComparison.Ordinal))
                continue;
            (trimmed.StartsWith("compose_child ", StringComparison.Ordinal)
                || trimmed.Contains("-p \"$project\"", StringComparison.Ordinal))
                .ShouldBeTrue("down -v aimed at something other than the child or a retired c590 project: " + trimmed);
        }
    }

    [Test]
    public void Host_compose_uses_the_deployed_tag_not_this_runs_sha()
    {
        var text = Remote();
        // A case that only restarts or inspects the standing runner must compose the image that is
        // actually deployed. Deriving the tag from this run's sha made `up -d --no-build` look for
        // a tag that was never built, try to PULL it, and leave the runner stopped.
        Block(text, "compose_host").ShouldContain("sha12=\"$(deployed_sha12)\"");
        Block(text, "compose_host").ShouldNotContain("${SHA:0:12}");
        Block(text, "deployed_sha12").ShouldContain("SERVER2_ENV");

        // deploy-parent writes that file before it composes, so it deploys its own sha.
        var deploy = Block(text, "case_deploy_parent");
        Order(deploy, "SOURCE_SHA12=${SHA:0:12}", "compose_host up -d")
            .ShouldBeTrue("the deploy pins the env before it composes");

        // The restart case owns bringing the runner back before it refuses.
        var restart = Block(text, "case_persistent_restart");
        Order(restart, "compose_host stop", "RestartFailed").ShouldBeTrue();
        // Two bring-ups: the one that failed, and the recovery that runs before the refusal.
        System.Text.RegularExpressions.Regex
            .Matches(restart, @"compose_host up -d --no-build").Count
            .ShouldBe(2, "the failure path brings the runner back before refusing");
        var recovery = restart.IndexOf("compose_host up -d --no-build >> \"$CASE_DIR/command.log\" 2>&1 || true", StringComparison.Ordinal);
        recovery.ShouldBeGreaterThan(0, "the recovery bring-up tolerates its own failure");
        recovery.ShouldBeLessThan(restart.IndexOf("RestartFailed", StringComparison.Ordinal));
    }

    [Test]
    public void Retirement_is_anchored_on_the_run_scoped_prefix()
    {
        // D-9: the host daemon is shared with am-service, traefik, windmill and schoolrevision-*.
        // Every retirement pattern is anchored so it can only ever name a c590 run's own leftovers.
        var retire = Block(Remote(), "retire_c590_leftovers");
        retire.ShouldContain("c590[0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f]");
        retire.ShouldContain("'^antiphon-c590-'");
        retire.ShouldContain("'^c590[0-9a-f]{12}_'");
        // The inventory is written before anything is removed: an untraceable retirement is worse
        // than a leftover.
        Order(retire, "inventory-containers.txt", "down -v").ShouldBeTrue();
        Order(retire, "inventory-volumes.txt", "volume rm").ShouldBeTrue();
        retire.ShouldNotContain("prune");
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
    public void Lane_detection_separates_a_bare_host_from_the_runner()
    {
        var detect = Block(Remote(), "detect_lane");

        // A bare host's daemon reports the HOST's own hostname, exactly as the nested daemon
        // reports the runner container's - so "Name equals hostname" alone says nothing about
        // which lane this is, and CP-5 self-identified as nested on server2's shell because of it.
        detect.ShouldContain("/.dockerenv");
        Order(detect, "!= \"$own\"", "/.dockerenv")
            .ShouldBeTrue("a sibling daemon is ruled out before the container check");

        // All four outcomes are named; a sibling is neither lane, not silently one of them.
        foreach (var lane in new[] { "none", "sibling", "nested", "host" })
            detect.ShouldContain("LANE=\"" + lane + "\"");
    }

    [Test]
    public void Every_case_declares_a_lane()
    {
        var text = Remote();
        text.ShouldContain("detect_lane > /dev/null");
        text.ShouldContain("WrongLane want=$want lane=$LANE");
        // The three cases that change standing state are host-lane, and the nested roster is not.
        foreach (var name in new[] { "case_deploy_parent", "case_nested_residue", "case_persistent_restart",
                     "case_handoff", "case_custody_containment" })
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

    // CARD-0604 D-2. deploy-parent accepted a runner that had never once registered: /health and
    // `docker info` were the whole verdict, and the standing runner sat green through 304
    // consecutive phone-home failures. Both new probes must refuse, by name.
    [Test]
    public void Deploy_parent_probes_the_phone_home_secret_as_the_app_uid()
    {
        var deploy = Block(Remote(), "case_deploy_parent");

        // Read as uid 1654, at whatever path the runner is actually configured to read.
        deploy.ShouldContain("printenv PhoneHome__SecretPath");
        deploy.ShouldContain("docker exec -u 1654:1654 \"$container\" head -c 1 \"$phone_home_secret_path\"");
        deploy.ShouldContain("write_result false PhoneHomeSecretUnreadable 2");
        deploy.ShouldContain("write_result false PhoneHomeSecretPathUnset 2");

        // The probe's one byte goes to /dev/null: the secret is never captured into evidence.
        Executable(deploy, "head -c 1 \"$phone_home_secret_path\"")
            .ShouldAllBe(line => line.Contains("> /dev/null", StringComparison.Ordinal));

        // It happens while the case can still refuse, not after the accept.
        Order(deploy, "PhoneHomeSecretUnreadable", "write_result true").ShouldBeTrue();
    }

    [Test]
    public void Deploy_parent_refuses_a_runner_whose_phone_home_keeps_failing()
    {
        var deploy = Block(Remote(), "case_deploy_parent");

        // The window opens after the health wait (i.e. past the compose start_period), so a
        // single cold-start reconnect is not a verdict, and it is long enough for the 15s
        // backoff to leave more than one mark if the loop can only fail.
        deploy.ShouldContain("phone_home_since=\"$(date -u +%Y-%m-%dT%H:%M:%S)\"");
        deploy.ShouldContain("docker logs --since \"$phone_home_since\" \"$container\"");
        deploy.ShouldContain("Phone-home connection ended; reconnecting");
        deploy.ShouldContain("UnauthorizedAccessException");
        deploy.ShouldContain("write_result false PhoneHomeUnreachable 2");

        // "Repeated", not "any": one is a reconnect, two in the window is a loop that cannot win.
        deploy.ShouldContain("-ge 2 ]");

        // CP-6a is what enables phone-home on the production server, and it runs AFTER this case.
        // A server still answering phone_home_disabled is therefore recorded, not blamed on the
        // deployment -- otherwise this refusal would make the plan's own order unsatisfiable.
        // Every other cause (a permission fault, a rejected secret, any other conflict) still
        // refuses, which is the entire reason the probe exists.
        deploy.ShouldContain("/api/session-runners/register");
        deploy.ShouldContain("phone_home_disabled");
        deploy.ShouldContain("phone-home-server-state.txt\")\" != \"disabled\" ]");
        // The probe carries no secret: it separates "disabled" from "enabled" and nothing else.
        Block(Remote(), "case_deploy_parent")
            .Split('\n')
            .Where(l => l.Contains("register-probe", StringComparison.Ordinal))
            .ShouldAllBe(l => !l.Contains("SecretHeader", StringComparison.Ordinal)
                && !l.Contains("PHONE_HOME_SECRET", StringComparison.Ordinal));

        // Runner output can carry a token; the window is scrubbed like every other evidence file.
        deploy.ShouldContain("scrub_file \"$CASE_DIR/phone-home-window.log\"");
        Order(deploy, "write_result false PhoneHomeUnreachable 2", "write_result true").ShouldBeTrue();
    }

    // CARD-0604 D-5. deploy-parent builds a ~2.2 GB image pair every round and used to leave every
    // superseded pair on a SHARED host daemon. Its own build products must be retired, and only
    // its own: prune stays forbidden and no foreign repository may be reachable by the pattern.
    [Test]
    public void Deploy_parent_retires_its_own_superseded_images()
    {
        var text = Remote();
        var retire = Block(text, "retire_superseded_server2_images");
        retire.ShouldContain("^antiphon-server2/(server|session-testing):");
        retire.ShouldContain("docker image rm \"$image\"");
        // The tag being deployed is kept; everything older goes.
        retire.ShouldContain("antiphon-server2/server:$keep");
        retire.ShouldContain("antiphon-server2/session-testing:$keep");
        retire.ShouldNotContain("prune");

        var deploy = Block(text, "case_deploy_parent");
        deploy.ShouldContain("retire_superseded_server2_images \"${SHA:0:12}\"");
        // Only once the new deployment is proven: an image still in use cannot be removed, and a
        // failed deploy must leave the pair that is actually running alone.
        Order(deploy, "compose_host up -d", "retire_superseded_server2_images").ShouldBeTrue();
        Order(deploy, "PhoneHomeUnreachable", "retire_superseded_server2_images").ShouldBeTrue();
        deploy.ShouldContain("inventory-images-after.txt");
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
