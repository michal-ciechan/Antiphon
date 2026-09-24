using System.Diagnostics;
using System.Text;
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
        // CARD-0660 (amended): the third is `ensure_runner_codex_home`, which creates and owns the
        // host Codex home for uid 1654 -- a chown the host user mc cannot do. It refuses off the
        // host lane before any of its sudo lines.
        var codexHome = Block(text, "ensure_runner_codex_home");
        foreach (var line in sudoLines)
            (EnsureDirsBody(text).Contains(line, StringComparison.Ordinal)
                || containment.Contains(line, StringComparison.Ordinal)
                || codexHome.Contains(line, StringComparison.Ordinal))
                .ShouldBeTrue("sudo outside ensure_dirs' host branch, the Codex home or the containment case: " + line);
        EnsureDirsBody(text).ShouldContain("if [ \"$LANE\" = \"host\" ]; then");
        var codexCommands = Commands(codexHome);
        codexCommands[1].ShouldBe("if [ \"$LANE\" != \"host\" ]; then", "the Codex home refuses off the host lane first");
        codexCommands[2].ShouldBe("write_result false CodexHomeHostLaneOnly 2");

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

    [Test]
    public void Custody_residue_counts_subdirectories_only()
    {
        var block = Block(Remote(), "case_custody_containment");
        foreach (var controller in new[] { "pids", "freezer" })
        {
            var root = "/sys/fs/cgroup/" + controller + "/antiphon-custody";
            block.ShouldContain("find " + root + " -mindepth 1 -maxdepth 1 -type d");
            block.ShouldNotContain("ls -1 " + root);
        }
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
            // CARD-0660: `find ... -prune` (ensure_dirs skipping the Codex home) is a filesystem
            // walk, not a daemon. Only that exact primary on a find line is set aside; every other
            // occurrence of the word still fails.
            if (trimmed.StartsWith("sudo -n find ", StringComparison.Ordinal))
                trimmed = trimmed.Replace(" -prune -o ", " ", StringComparison.Ordinal);
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

    // CARD-0631 D-9 (amended). The runner's git identity is a file on server2 beside the deploy
    // key and the Claude token. deploy-parent creates it when missing, from stack.env's values or
    // the defaults, and never rewrites one that exists: the operator may have edited it.
    [Test]
    public void Deploy_parent_creates_the_git_identity_file_without_overwriting()
    {
        var text = Remote();
        text.ShouldContain("GIT_IDENTITY_PATH=\"$SERVER2_ROOT/secrets/gitconfig\"");
        text.ShouldContain("GIT_IDENTITY_DEFAULT_NAME=\"antiphon-server2-runner\"");
        text.ShouldContain("GIT_IDENTITY_DEFAULT_EMAIL=\"antiphon-server2-runner@users.noreply.github.com\"");
        text.ShouldContain("GIT_IDENTITY_MOUNT=\"/run/antiphon/gitconfig\"");
        Block(text, "compose_host").ShouldContain("RUNNER_GIT_IDENTITY_FILE=\"$GIT_IDENTITY_PATH\" \\");

        var ensure = Block(text, "ensure_runner_git_identity");
        ensure.ShouldContain("name=\"$(stack_env_value RUNNER_GIT_USER_NAME)\"");
        ensure.ShouldContain("email=\"$(stack_env_value RUNNER_GIT_USER_EMAIL)\"");
        ensure.ShouldContain("RUNNER_GIT_USER_NAME=\"${name:-$GIT_IDENTITY_DEFAULT_NAME}\"");
        ensure.ShouldContain("RUNNER_GIT_USER_EMAIL=\"${email:-$GIT_IDENTITY_DEFAULT_EMAIL}\"");

        // Create only when missing: every write to the path sits inside the `! -e` branch, and the
        // final move cannot clobber a file that appeared meanwhile.
        const string create = "if [ ! -e \"$GIT_IDENTITY_PATH\" ]; then";
        var lines = Commands(ensure);
        var open = lines.FindIndex(line => line == create);
        open.ShouldBeGreaterThanOrEqualTo(0, "the file is created only when it does not exist");
        var close = lines.FindIndex(open + 1, line => line == "else" || line == "fi");
        close.ShouldBeGreaterThan(open);
        var writes = lines
            .Select((line, index) => (line, index))
            .Where(item => item.line.Contains("$GIT_IDENTITY_PATH", StringComparison.Ordinal))
            .Where(item => !item.line.Contains("--get", StringComparison.Ordinal)
                && !item.line.StartsWith("if [ -L ", StringComparison.Ordinal)
                && !item.line.StartsWith("if [ -d ", StringComparison.Ordinal)
                && !item.line.StartsWith("rmdir ", StringComparison.Ordinal)
                && item.line != create)
            .ToList();
        writes.ShouldNotBeEmpty();
        writes.ShouldAllBe(item => item.index > open && item.index < close,
            "an existing identity file is never rewritten");
        ensure.ShouldContain("mv -n \"$GIT_IDENTITY_PATH.tmp\" \"$GIT_IDENTITY_PATH\"");
        ensure.ShouldContain("git config --file \"$GIT_IDENTITY_PATH.tmp\" user.name \"$RUNNER_GIT_USER_NAME\"");
        ensure.ShouldContain("git config --file \"$GIT_IDENTITY_PATH.tmp\" user.email \"$RUNNER_GIT_USER_EMAIL\"");
        // uid 1654 reads the bind mount itself, so a NEW file is made readable -- on the temporary
        // file, never the destination; an incomplete file refuses rather than booting blind.
        ensure.ShouldContain("&& chmod 0644 \"$GIT_IDENTITY_PATH.tmp\" \\");
        Executable(ensure, "chmod").ShouldAllBe(line => line.Contains("\"$GIT_IDENTITY_PATH.tmp\"", StringComparison.Ordinal));
        ensure.ShouldContain("write_result false GitIdentityIncomplete 2");
        // A directory a premature bind mount left behind is removed only when empty.
        ensure.ShouldContain("rmdir \"$GIT_IDENTITY_PATH\"");
        ensure.ShouldNotContain("rm -rf");

        var deploy = Block(text, "case_deploy_parent");
        // Before compose binds it, and before stack.env (its seed values' source) is rewritten.
        Order(deploy, "ensure_runner_git_identity", "cat > \"$SERVER2_ENV\"").ShouldBeTrue();
        Order(deploy, "ensure_runner_git_identity", "compose_host up -d").ShouldBeTrue();
        deploy.ShouldContain("RUNNER_GIT_IDENTITY_FILE=$GIT_IDENTITY_PATH\n");
        deploy.ShouldContain("RUNNER_GIT_USER_NAME=$RUNNER_GIT_USER_NAME\n");
        deploy.ShouldContain("RUNNER_GIT_USER_EMAIL=$RUNNER_GIT_USER_EMAIL\n");

        // And the mounted file is what uid 1654 resolves, before anything is retired or accepted.
        var effective = Block(text, "verify_runner_git_identity");
        effective.ShouldContain("docker exec -u 1654:1654 \"$container\" git -C \"$where\" config --show-origin --get user.email");
        effective.ShouldContain("file:%s\\t%s' \"$GIT_IDENTITY_MOUNT\"");
        effective.ShouldContain("write_result false GitIdentityNotEffective 2");
        deploy.ShouldContain("verify_runner_git_identity \"$container\" /");
        Order(deploy, "verify_runner_git_identity", "retire_superseded_server2_images").ShouldBeTrue();
        Order(deploy, "verify_runner_git_identity", "write_result true").ShouldBeTrue();
    }

    // CARD-0631 V-7, D-10. deploy-parent accepted a runner whose repository did not exist, so the
    // first mirror crashed with Win32Exception(2). It now verifies the checkout the runner is
    // configured with, as uid 1654, INSIDE the container, and every failure refuses by name before
    // acceptance. (The fresh-volume seed that precedes it is guarded separately below.)
    [Test]
    public void Deploy_parent_seeds_or_verifies_runner_checkout()
    {
        var text = Remote();
        var verify = Block(text, "verify_runner_checkout");
        var commands = Commands(verify);

        // The runner's own repository setting, with the runner's own default and clone source.
        verify.ShouldContain("${PhoneHome__RunnerRepository:-}");
        verify.ShouldContain("repo=\"${repo:-$RUNNER_CHECKOUT_DEFAULT}\"");
        text.ShouldContain("RUNNER_CHECKOUT_DEFAULT=\"" + new global::Antiphon.SessionRunner.PhoneHomeSettings().RunnerRepository + "\"");
        text.ShouldContain("RUNNER_CHECKOUT_ORIGIN=\"" + global::Antiphon.SessionRunner.RunnerWorkspaceService.DefaultCloneSource + "\"");

        // Every probe of the checkout runs in the container as the runner uid.
        var probes = commands.Where(line => line.Contains("\"$repo", StringComparison.Ordinal)
            && line.Contains("docker exec", StringComparison.Ordinal)).ToList();
        probes.Count.ShouldBeGreaterThanOrEqualTo(5);
        probes.ShouldAllBe(line => line.Contains("docker exec -u 1654:1654 ", StringComparison.Ordinal));
        // Never the host's identically named checkout, never a child project or volume.
        verify.ShouldNotContain("$CHECKOUT");
        verify.ShouldNotContain("CHILD_PROJECT");
        verify.ShouldNotContain("docker volume");
        commands.Where(line => line.Contains("git -C", StringComparison.Ordinal))
            .ShouldAllBe(line => line.Contains("docker exec -u 1654:1654 ", StringComparison.Ordinal));

        // Missing, invalid, foreign origin and a failed anonymous fetch each refuse by name.
        Refuses(commands, "test -e \"$repo/.git\"", "RunnerCheckoutMissing");
        Refuses(commands, "rev-parse --show-toplevel", "RunnerCheckoutInvalid");
        verify.ShouldContain("if [ \"$top\" != \"$repo\" ]; then");
        Refuses(commands, "remote get-url origin", "RunnerCheckoutOriginMismatch");
        verify.ShouldContain("if [ \"$origin\" != \"$RUNNER_CHECKOUT_ORIGIN\" ]; then");
        var fetch = commands.Single(line => line.Contains(" fetch ", StringComparison.Ordinal));
        fetch.ShouldContain("-e GIT_TERMINAL_PROMPT=0");
        fetch.ShouldContain("timeout --kill-after=5s 120s git -C \"$repo\" fetch --no-tags origin \"$BRANCH\"");
        fetch.ShouldContain("|| write_result false RunnerCheckoutFetchFailed 2");
        Refuses(commands, "rev-parse FETCH_HEAD", "RunnerCheckoutFetchFailed");
        verify.ShouldNotContain("|| true");
        Executable(verify, "write_result").ShouldAllBe(line => line.Contains("write_result false ", StringComparison.Ordinal));

        // A repository-local identity (a stopgap) would outrank the mounted file: it is removed,
        // and the mount is then proven effective inside the checkout itself.
        verify.ShouldContain("config --local --unset-all \"$k\"");
        verify.ShouldContain("write_result false GitIdentityOverrideNotRemoved 2");
        verify.ShouldContain("verify_runner_git_identity \"$container\" \"$repo\"");

        // The receipt: the verified path and FETCH_HEAD, no credentials.
        verify.ShouldContain("fetch_head=%s");
        verify.ShouldContain("runner-checkout.txt");

        var deploy = Block(text, "case_deploy_parent");
        deploy.ShouldContain("verify_runner_checkout \"$container\"");
        Order(deploy, "RunnerUnhealthy", "verify_runner_checkout").ShouldBeTrue("the runner is up before it is probed");
        Order(deploy, "verify_runner_checkout", "retire_superseded_server2_images").ShouldBeTrue();
        Order(deploy, "verify_runner_checkout", "write_result true").ShouldBeTrue();
    }

    // CARD-0631 Review 012e6357 (1). A first deploy on an empty work volume always refused
    // RunnerCheckoutMissing: phone-home may still be disabled on the server at that gate, so the
    // runner's lazy clone never runs. The checkout is now seeded before the runner starts (no
    // mirror can be in flight), as uid 1654, anonymously, with RunnerWorkspaceService's command,
    // and only into an absent or empty destination; the named verification still follows.
    [Test]
    [ParallelLimiter<ProcessSpawnLimit>]
    public void Deploy_parent_seeds_a_fresh_runner_checkout_before_starting_the_runner()
    {
        var text = Remote();
        var seed = Block(text, "seed_runner_checkout");
        var commands = Commands(seed);

        // state-init owns the fresh volume for uid 1654 first, then a one-off of the runner image.
        var init = commands.Single(line => line.Contains("run --rm --no-deps -T state-init", StringComparison.Ordinal));
        init.ShouldContain("|| write_result false StateInitFailed 2");
        var oneOff = commands.Single(line => line.StartsWith("compose_host run --rm --no-deps -T --user ", StringComparison.Ordinal));
        oneOff.ShouldBe("compose_host run --rm --no-deps -T --user 1654:1654 -e GIT_TERMINAL_PROMPT=0 --entrypoint /bin/sh session-runner -c '");
        commands.IndexOf(init).ShouldBeLessThan(commands.IndexOf(oneOff));
        Order(seed, "--user 1654:1654", "git clone").ShouldBeTrue("the clone runs inside the uid-1654 one-off");
        seed.ShouldContain("git clone --filter=blob:none --no-checkout \"$2\" \"$repo\"");
        seed.ShouldContain("antiphon-seed \"$RUNNER_CHECKOUT_DEFAULT\" \"$RUNNER_CHECKOUT_ORIGIN\"");
        seed.ShouldContain("|| write_result false RunnerCheckoutSeedFailed 2");
        // Never the host's checkout, never a credential.
        seed.ShouldNotContain("$CHECKOUT");
        seed.ShouldNotContain("ssh");
        seed.ShouldNotContain("|| true");

        // Before the runner exists, after both images exist, and still verified by name.
        var deploy = Block(text, "case_deploy_parent");
        deploy.ShouldContain("\n    seed_runner_checkout\n");
        Order(deploy, "StateInitBuildFailed", "seed_runner_checkout").ShouldBeTrue("the images are built first");
        Order(deploy, "seed_runner_checkout", "compose_host up -d").ShouldBeTrue("seeded before the runner starts");
        Order(deploy, "compose_host up -d", "verify_runner_checkout \"$container\"").ShouldBeTrue();

        // The in-container body, run for real: an empty volume is seeded from the given origin and
        // an existing or occupied destination is never touched.
        var body = System.Text.RegularExpressions.Regex.Match(seed, "-c '(?<body>[^']*)'").Groups["body"].Value;
        body.ShouldContain("repo=\"${PhoneHome__RunnerRepository:-$1}\"");
        var output = LinuxShell("SEED='" + body + "'\n" + """
            root="$(mktemp -d)"
            trap 'rm -rf "$root"' EXIT
            cd "$root"
            git init -q "$root/origin"
            git -C "$root/origin" -c user.name=t -c user.email=t@t commit -q --allow-empty -m init
            seed() { sh -c "$SEED" antiphon-seed "$1" "$root/origin" 2>/dev/null; echo "exit=$?"; }
            unset PhoneHome__RunnerRepository
            printf 'fresh %s\n' "$(seed "$root/work/repos/antiphon" | tr '\n' ' ')"
            printf 'origin=%s\n' "$(git -C "$root/work/repos/antiphon" remote get-url origin | sed "s#^$root#ROOT#")"
            printf 'again %s\n' "$(seed "$root/work/repos/antiphon" | tr '\n' ' ')"
            mkdir -p "$root/occupied" && touch "$root/occupied/x"
            printf 'occupied %s\n' "$(seed "$root/occupied" | tr '\n' ' ')"
            [ -e "$root/occupied/.git" ] || echo occupied-untouched
            export PhoneHome__RunnerRepository="$root/configured"
            printf 'configured %s\n' "$(seed "$root/ignored" | sed "s#$root#ROOT#" | tr '\n' ' ')"
            [ -e "$root/configured/.git" ] && [ ! -e "$root/ignored" ] && echo configured-used
            """);
        output.ShouldContain("fresh seeded path=");
        output.ShouldContain("origin=ROOT/origin\n");
        output.ShouldContain("again present path=");
        output.ShouldContain("occupied occupied path=");
        output.ShouldContain("occupied-untouched");
        output.ShouldContain("configured seeded path=ROOT/configured exit=0");
        output.ShouldContain("configured-used");
    }

    // CARD-0631 Review 012e6357 (2). persistent-restart stopped a runner deployed before the
    // identity mount existed; the start and the recovery then both failed on the new required
    // mount and the standing runner stayed down. The file is ensured while the runner is still up.
    [Test]
    [ParallelLimiter<ProcessSpawnLimit>]
    public void Persistent_restart_ensures_the_identity_file_before_stopping_an_older_runner()
    {
        var text = Remote();
        var restart = Block(text, "case_persistent_restart");
        Order(restart, "ensure_runner_git_identity", "compose_host stop").ShouldBeTrue();

        // The migration case, run for real: an older stack.env with no identity values and no
        // file. The compose stub refuses `up` exactly as the required bind mount would.
        var output = LinuxShell(IdentityHarness(text, "case_persistent_restart", "ensure_runner_codex_home") + CodexHomeLines(text) + """
            C604_SERVER_ORIGIN=http://127.0.0.1:9
            printf 'SOURCE_SHA12=0123456789ab\n' > "$SERVER2_ENV"
            require_lane() { :; }
            runner_container() { echo antiphon-runner-session-runner-1; }
            sleep() { :; }
            curl() { return 0; }
            docker() {
                case "$*" in
                    *runner-store-id*) echo store-1 ;;
                    *"images -q"*) echo image-1 ;;
                esac
                return 0
            }
            compose_host() {
                case "$1" in
                    stop)
                        if [ -f "$GIT_IDENTITY_PATH" ]; then echo "stop identity=present"; else echo "stop identity=missing"; fi >> "$CASE_DIR/calls.txt"
                        if [ -d "$CODEX_HOME_PATH" ] && [ "$(stat -c %a "$CODEX_HOME_PATH")" = 700 ]; then echo "stop codex-home=present"; else echo "stop codex-home=missing"; fi >> "$CASE_DIR/calls.txt"
                        ;;
                    up)
                        if [ ! -f "$GIT_IDENTITY_PATH" ] || [ -L "$GIT_IDENTITY_PATH" ]; then echo "up mount-missing" >> "$CASE_DIR/calls.txt"; return 1; fi
                        echo "up ok" >> "$CASE_DIR/calls.txt"
                        ;;
                esac
            }
            ( set -euo pipefail; case_persistent_restart )
            echo "exit=$?"
            cat "$CASE_DIR/calls.txt"
            stat -c 'identity=%a %F' "$GIT_IDENTITY_PATH"
            git config --file "$GIT_IDENTITY_PATH" --get user.name
            """);
        output.ShouldContain("stop identity=present");
        output.ShouldContain("stop codex-home=present", customMessage: "the Codex home is ensured before the older runner stops");
        output.ShouldNotContain("up mount-missing");
        output.ShouldContain("up ok");
        output.ShouldContain("RESULT accepted=true diagnosis=\n");
        output.ShouldContain("exit=0");
        output.ShouldContain("identity=644 regular file");
        output.ShouldContain("antiphon-server2-runner\n");
    }

    // CARD-0631 Review 012e6357 (3). `chmod 0644` on the identity path followed an existing
    // symlink and made its target -- possibly an adjacent 0600 secret -- world-readable. A symlink
    // now refuses before anything touches it, and 0644 is applied only to a newly created file.
    [Test]
    [ParallelLimiter<ProcessSpawnLimit>]
    public void Deploy_parent_refuses_a_git_identity_symlink_and_leaves_its_target_mode()
    {
        var text = Remote();
        var ensure = Commands(Block(text, "ensure_runner_git_identity"));
        ensure[2].ShouldBe("if [ -L \"$GIT_IDENTITY_PATH\" ]; then", "a symlink refuses before the path is touched");
        ensure[3].ShouldBe("write_result false GitIdentityPathIsSymlink 2");

        var output = LinuxShell(IdentityHarness(text) + """
            secret="$SERVER2_ROOT/secrets/phone-home"
            printf 'secret-bytes\n' > "$secret"
            chmod 0600 "$secret"
            ln -s phone-home "$GIT_IDENTITY_PATH"
            ( set -euo pipefail; ensure_runner_git_identity )
            echo "symlink exit=$?"
            stat -c 'target=%a' "$secret"
            printf 'target-content=%s\n' "$(cat "$secret")"
            [ -L "$GIT_IDENTITY_PATH" ] && echo link-left

            rm "$GIT_IDENTITY_PATH"
            umask 077
            ( set -euo pipefail; ensure_runner_git_identity )
            echo "fresh exit=$?"
            stat -c 'created=%a %F' "$GIT_IDENTITY_PATH"

            chmod 0600 "$GIT_IDENTITY_PATH"
            ( set -euo pipefail; ensure_runner_git_identity )
            echo "existing exit=$?"
            stat -c 'kept=%a' "$GIT_IDENTITY_PATH"
            """);
        output.ShouldContain("RESULT accepted=false diagnosis=GitIdentityPathIsSymlink\nsymlink exit=2");
        output.ShouldContain("target=600\n");
        output.ShouldContain("target-content=secret-bytes\n");
        output.ShouldContain("link-left");
        output.ShouldContain("fresh exit=0");
        output.ShouldContain("created=644 regular file");
        output.ShouldContain("existing exit=0");
        output.ShouldContain("kept=600\n", customMessage: "an operator's existing file keeps its mode");
    }

    // CARD-0660 (amended). The runner's Codex home is a DIRECTORY on server2 beside the identity
    // files, bind-mounted read-write as CODEX_HOME. deploy-parent creates it for uid 1654 at 0700
    // before anything binds it, and never reads, lists or copies what it holds; the evidence is
    // presence only. persistent-restart ensures it while an older runner is still up.
    [Test]
    public void Deploy_parent_creates_the_codex_home_directory_without_reading_it()
    {
        var text = Remote();
        text.ShouldContain("CODEX_HOME_PATH=\"$SERVER2_ROOT/secrets/codex\"\n");
        text.ShouldContain("CODEX_HOME_OWNER=\"1654:1654\"\n");
        Block(text, "compose_host").ShouldContain("RUNNER_CODEX_HOME_DIR=\"$CODEX_HOME_PATH\" \\");

        var ensure = Block(text, "ensure_runner_codex_home");
        var lines = Commands(ensure);
        // A symlink refuses before anything touches the path (an install, chown or chmod through
        // it would land on its target), and so does anything that is not a directory.
        var symlink = lines.IndexOf("if [ -L \"$CODEX_HOME_PATH\" ]; then");
        symlink.ShouldBeGreaterThan(0);
        lines[symlink + 1].ShouldBe("write_result false CodexHomePathIsSymlink 2");
        ensure.ShouldContain("write_result false CodexHomePathIsNotDirectory 2");
        var firstTouch = lines.FindIndex(line => line.StartsWith("sudo -n install ", StringComparison.Ordinal)
            || line.StartsWith("sudo -n chown ", StringComparison.Ordinal) || line.StartsWith("sudo -n chmod ", StringComparison.Ordinal));
        firstTouch.ShouldBeGreaterThan(symlink);

        // Created only when missing, owned by the runner uid at 0700 in one step.
        var create = lines.IndexOf("if [ ! -e \"$CODEX_HOME_PATH\" ]; then");
        create.ShouldBeGreaterThan(symlink);
        lines[create + 1].ShouldBe(
            "sudo -n install -d -o \"${CODEX_HOME_OWNER%:*}\" -g \"${CODEX_HOME_OWNER#*:}\" -m 0700 \"$CODEX_HOME_PATH\" 2>> \"$CASE_DIR/command.log\" || write_result false CodexHomeCreateFailed 2");
        // Owner and mode are re-asserted on the directory itself only: never recursive, never a
        // symlink's target.
        ensure.ShouldContain("sudo -n chown -h \"$CODEX_HOME_OWNER\" \"$CODEX_HOME_PATH\"");
        ensure.ShouldContain("sudo -n chmod 0700 \"$CODEX_HOME_PATH\"");
        ensure.ShouldNotContain(" -R ");
        ensure.ShouldNotContain("rm ");

        // Presence only, as evidence and as a warning. Nothing in the script reads the home.
        ensure.ShouldContain("printf 'true\\n' > \"$CASE_DIR/codex-home-present.txt\"");
        ensure.ShouldContain("if sudo -n test -e \"$CODEX_HOME_PATH/auth.json\"; then");
        ensure.ShouldContain("codex-auth-present.txt");
        ensure.ShouldContain("WARN CodexAuthAbsent");
        foreach (var line in Commands(text).Where(line => line.Contains("$CODEX_HOME_PATH", StringComparison.Ordinal)))
            System.Text.RegularExpressions.Regex.IsMatch(line,
                    @"(^|[\s;|&(])(cat|cp|mv|ls|head|tail|less|more|grep|sed|awk|tar|rsync|sha256sum|md5sum|base64|xxd|od|strings|scp)\s|<\s*""\$CODEX_HOME_PATH")
                .ShouldBeFalse("the Codex home is never read: " + line);

        // Before state-init binds it (seed_runner_checkout runs state-init) and before compose up;
        // stack.env names it for every later compose call.
        var deploy = Block(text, "case_deploy_parent");
        Order(deploy, "ensure_runner_codex_home", "seed_runner_checkout").ShouldBeTrue();
        Order(deploy, "ensure_runner_codex_home", "compose_host up -d").ShouldBeTrue();
        deploy.ShouldContain("RUNNER_CODEX_HOME_DIR=$CODEX_HOME_PATH\n");

        var restart = Block(text, "case_persistent_restart");
        Order(restart, "ensure_runner_codex_home", "compose_host stop").ShouldBeTrue();
    }

    // The same function, run for real over a throwaway server2 root with sudo reduced to a plain
    // call and the owner reduced to the current uid. The sentinel is not a credential.
    [Test]
    [ParallelLimiter<ProcessSpawnLimit>]
    public void Deploy_parent_codex_home_refuses_a_symlink_or_file_and_keeps_contents()
    {
        var text = Remote();
        var output = LinuxShell(CodexHomeHarness(text) + """
            ( set -euo pipefail; ensure_runner_codex_home ) 2>&1
            echo "fresh exit=$?"
            [ "$(stat -c '%u:%g' "$CODEX_HOME_PATH")" = "$CODEX_HOME_OWNER" ] && echo fresh-owner-ok
            stat -c 'fresh=%a %F' "$CODEX_HOME_PATH"
            printf 'fresh-state=%s auth=%s\n' "$(cat "$CASE_DIR/codex-home-state.txt")" "$(cat "$CASE_DIR/codex-auth-present.txt")"

            printf 'c660-sentinel-not-a-credential\n' > "$CODEX_HOME_PATH/auth.json"
            chmod 0640 "$CODEX_HOME_PATH/auth.json"
            chmod 0755 "$CODEX_HOME_PATH"
            ( set -euo pipefail; ensure_runner_codex_home )
            echo "existing exit=$?"
            stat -c 'existing=%a' "$CODEX_HOME_PATH"
            stat -c 'sentinel=%a' "$CODEX_HOME_PATH/auth.json"
            printf 'sentinel-bytes=%s\n' "$(cat "$CODEX_HOME_PATH/auth.json")"
            printf 'existing-state=%s home=%s auth=%s\n' "$(cat "$CASE_DIR/codex-home-state.txt")" \
                "$(cat "$CASE_DIR/codex-home-present.txt")" "$(cat "$CASE_DIR/codex-auth-present.txt")"
            grep -rq c660-sentinel "$CASE_DIR" || echo evidence-holds-no-contents

            mv "$CODEX_HOME_PATH" "$root/elsewhere"
            chmod 0755 "$root/elsewhere"
            chmod 0755 "$root/elsewhere"
            ln -s "$root/elsewhere" "$CODEX_HOME_PATH"
            ( set -euo pipefail; ensure_runner_codex_home )
            echo "symlink exit=$?"
            stat -c 'target=%a' "$root/elsewhere"
            [ -L "$CODEX_HOME_PATH" ] && echo link-left

            rm "$CODEX_HOME_PATH"
            printf 'not-a-directory\n' > "$CODEX_HOME_PATH"
            chmod 0644 "$CODEX_HOME_PATH"
            ( set -euo pipefail; ensure_runner_codex_home )
            echo "file exit=$?"
            stat -c 'file=%a %F' "$CODEX_HOME_PATH"

            rm "$CODEX_HOME_PATH"
            ( set -euo pipefail; LANE=nested; ensure_runner_codex_home )
            echo "nested exit=$?"
            [ -e "$CODEX_HOME_PATH" ] || echo nested-created-nothing
            """);
        output.ShouldContain("fresh exit=0");
        output.ShouldContain("fresh-owner-ok");
        output.ShouldContain("fresh=700 directory\n");
        output.ShouldContain("fresh-state=created auth=false\n");
        output.ShouldContain("WARN CodexAuthAbsent");
        output.ShouldContain("existing exit=0");
        output.ShouldContain("existing=700\n");
        output.ShouldContain("sentinel=640\n", customMessage: "the home's contents are never re-moded");
        output.ShouldContain("sentinel-bytes=c660-sentinel-not-a-credential\n");
        output.ShouldContain("existing-state=kept home=true auth=true\n");
        output.ShouldContain("evidence-holds-no-contents");
        output.ShouldContain("RESULT accepted=false diagnosis=CodexHomePathIsSymlink\nsymlink exit=2");
        output.ShouldContain("target=755\n", customMessage: "a symlink's target is never chowned or re-moded");
        output.ShouldContain("link-left");
        output.ShouldContain("RESULT accepted=false diagnosis=CodexHomePathIsNotDirectory\nfile exit=2");
        output.ShouldContain("file=644 regular file\n");
        output.ShouldContain("RESULT accepted=false diagnosis=CodexHomeHostLaneOnly\nnested exit=2");
        output.ShouldContain("nested-created-nothing");
    }

    // ensure_dirs resets the host-lane trees to mc on every host case. Recursing into the Codex home
    // would hand the runner's live sign-in to mc (and read every entry under it): the reset now
    // prunes it, run for real with chown swapped for a print of what it would visit.
    [Test]
    [ParallelLimiter<ProcessSpawnLimit>]
    public void Host_lane_ownership_reset_never_enters_the_codex_home()
    {
        var text = Remote();
        var ensure = Commands(EnsureDirsBody(text));
        ensure.ShouldNotContain(line => line.Contains("chown -R", StringComparison.Ordinal)
            && line.Contains("$SERVER2_ROOT", StringComparison.Ordinal), "a recursive chown over the server2 root");
        var reset = ensure.Single(line => line.Contains("-prune", StringComparison.Ordinal));
        reset.ShouldBe("sudo -n find \"$SERVER2_ROOT\" -path \"$CODEX_HOME_PATH\" -prune -o -exec chown -h mc:mc {} +");

        var visit = reset.Replace("sudo -n ", "", StringComparison.Ordinal)
            .Replace("-exec chown -h mc:mc {} +", "-exec printf 'visit %s\\n' {} +", StringComparison.Ordinal);
        var output = LinuxShell(string.Join('\n',
            "root=\"$(mktemp -d)\"",
            "trap 'rm -rf \"$root\"' EXIT",
            "SERVER2_ROOT=\"$root/s2\"",
            text.Replace("\r\n", "\n").Split('\n').Single(line => line.StartsWith("CODEX_HOME_PATH=", StringComparison.Ordinal)),
            "mkdir -p \"$CODEX_HOME_PATH/sessions\"",
            "touch \"$SERVER2_ROOT/secrets/gitconfig\" \"$CODEX_HOME_PATH/auth.json\"",
            visit,
            "") + "\n").Replace("\r\n", "\n");
        output.ShouldContain("/s2\n");
        output.ShouldContain("/s2/secrets\n");
        output.ShouldContain("/s2/secrets/gitconfig\n");
        output.ShouldNotContain("/s2/secrets/codex");
    }

    // The real Codex-home variables and function over a throwaway server2 root. sudo is a plain
    // call and the owner is the current uid, so the harness needs no privilege.
    private static string CodexHomeHarness(string text) =>
        string.Join('\n',
            "root=\"$(mktemp -d)\"",
            "trap 'rm -rf \"$root\"' EXIT",
            "cd \"$root\"",
            "CASE_DIR=\"$root/case\"; mkdir -p \"$CASE_DIR\"",
            "SERVER2_ROOT=\"$root/server2\"; mkdir -p \"$SERVER2_ROOT/secrets\"",
            "write_result() { printf 'RESULT accepted=%s diagnosis=%s\\n' \"$1\" \"$2\"; exit \"$3\"; }",
            Block(text, "ensure_runner_codex_home"))
        + "\n" + CodexHomeLines(text);

    // The script's own CODEX_HOME_ variables (after SERVER2_ROOT), then the host lane, a plain
    // sudo and the current uid as owner.
    private static string CodexHomeLines(string text) =>
        string.Join('\n', text.Replace("\r\n", "\n").Split('\n')
            .Where(line => line.StartsWith("CODEX_HOME_", StringComparison.Ordinal))
            .Concat(new[]
            {
                "LANE=host",
                "sudo() { if [ \"$1\" = -n ]; then shift; fi; \"$@\"; }",
                "CODEX_HOME_OWNER=\"$(id -u):$(id -g)\"",
            }))
        + "\n";

    // The real identity variables and functions, over a throwaway server2 root, with write_result
    // reduced to a printed verdict. Extra functions are extracted from the script verbatim.
    private static string IdentityHarness(string text, params string[] functions)
    {
        var variables = text.Replace("\r\n", "\n").Split('\n')
            .Where(line => line.StartsWith("GIT_IDENTITY_", StringComparison.Ordinal));
        return string.Join('\n', new[]
        {
            "root=\"$(mktemp -d)\"",
            "trap 'rm -rf \"$root\"' EXIT",
            // Not the inherited cwd: under WSL that is this worktree, whose .git names a C:/ path
            // Linux git cannot resolve, so every git call there would fail for the wrong reason.
            "cd \"$root\"",
            "CASE_DIR=\"$root/case\"; mkdir -p \"$CASE_DIR\"",
            "SERVER2_ROOT=\"$root/server2\"; mkdir -p \"$SERVER2_ROOT/secrets\"",
            "SERVER2_ENV=\"$SERVER2_ROOT/stack.env\"",
            "write_result() { printf 'RESULT accepted=%s diagnosis=%s\\n' \"$1\" \"$2\"; exit \"$3\"; }",
        }
            .Concat(variables)
            .Concat(new[] { "stack_env_value", "ensure_runner_git_identity" }.Concat(functions).Select(f => Block(text, f))))
            + "\n";
    }

    // The remote script only ever runs under Linux bash, and these defects are behaviour (a
    // symlink's target mode, a restart's ordering), not text. On Windows the Linux shell is WSL:
    // Git Bash can neither create a symlink without privilege nor keep a 0600 mode.
    internal static string LinuxShell(string script)
    {
        ProcessStartInfo start;
        if (OperatingSystem.IsWindows())
        {
            var wsl = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "wsl.exe");
            if (!File.Exists(wsl))
                Skip.Test("No Linux shell: wsl.exe is not installed, and c590-remote.sh only runs under Linux bash.");
            start = new ProcessStartInfo(wsl) { ArgumentList = { "-e", "bash", "-s" } };
        }
        else
        {
            start = new ProcessStartInfo("bash") { ArgumentList = { "-s" } };
        }

        start.RedirectStandardInput = true;
        start.RedirectStandardOutput = true;
        start.RedirectStandardError = true;
        start.UseShellExecute = false;
        start.StandardInputEncoding = new UTF8Encoding(false);
        start.StandardOutputEncoding = Encoding.UTF8;
        start.StandardErrorEncoding = Encoding.UTF8;
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        process.StandardInput.Write(script.Replace("\r\n", "\n"));
        process.StandardInput.Close();
        if (!process.WaitForExit(60_000))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException("the Linux shell harness did not finish in 60s");
        }

        process.WaitForExit();
        var output = stdout.Result;
        Console.WriteLine(output);
        Console.WriteLine(stderr.Result);
        return output;
    }

    private static void Refuses(IReadOnlyList<string> commands, string probe, string diagnosis)
    {
        var line = commands.Single(command => command.Contains(probe, StringComparison.Ordinal));
        (line.Contains("write_result false " + diagnosis + " 2", StringComparison.Ordinal)
            || commands.SkipWhile(command => command != line).Skip(1).FirstOrDefault()
                == "write_result false " + diagnosis + " 2")
            .ShouldBeTrue(probe + " refuses with " + diagnosis);
    }

    // Executable lines with backslash continuations joined, comments dropped.
    private static List<string> Commands(string text)
    {
        var commands = new List<string>();
        var pending = "";
        foreach (var raw in text.Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw.Trim();
            if (pending.Length == 0 && (line.Length == 0 || line[0] == '#'))
                continue;
            if (line.EndsWith('\\'))
            {
                pending += line[..^1].TrimEnd() + " ";
                continue;
            }

            commands.Add(pending + line);
            pending = "";
        }

        return commands;
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
