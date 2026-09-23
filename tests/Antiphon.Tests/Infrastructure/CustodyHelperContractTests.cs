using System.Text.RegularExpressions;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Infrastructure;

// CARD-0604 S9 / D-17. Text guards over the three files that make up the Linux custody
// mechanism: the root-owned placement shim, the root-owned termination helper and the sudoers
// drop-in that grants uid 1654 exactly those two commands.
//
// These files never run on Windows and are never exercised by a build. They are read by a root
// process inside the persistent server2 runner, so the only thing that can hold their security
// properties in place on this side of the fence is their own text. The live behaviour -- that a
// double-forked, setsid'd, nohup'd descendant is still in the tree, and that the child cannot
// sudo out of it -- is measured separately on both cgroup versions (V-28, CP-14/CP-15) before
// the backend is trusted.
[Category("Unit")]
public sealed class CustodyHelperContractTests
{
    // CARD-0598's containment requirement in one line: PR_SET_NO_NEW_PRIVS is what makes the
    // cgroup non-escapable, because it renders sudo and every other setuid binary inert for the
    // whole descendant tree. Without it the tracked child could simply call the sudo grant this
    // very mechanism installs and move itself out of its own accounting. (G-34)
    [Test]
    public void Shim_sets_no_new_privs()
    {
        var text = Enter();
        var exec = ExecLine(text);
        exec.ShouldContain("--no-new-privs");
        exec.ShouldStartWith("exec setpriv ");
        exec.ShouldEndWith("-- \"$@\"");

        // Placement happens before the exec, not after: the child inherits the tree at fork, so
        // there is never a window in which a started process is outside it.
        Order(text, "enter_tree_v2 \"$EXECUTION_ID\"", exec).ShouldBeTrue("v2 placement precedes the exec");
        Order(text, "enter_tree_v1 \"$EXECUTION_ID\"", exec).ShouldBeTrue("v1 placement precedes the exec");
    }

    // D-18: clearing the supplementary groups drops docker-nested (gid 1656), so a tracked
    // Mutation session cannot reach the nested daemon's socket at all.
    [Test]
    public void Shim_clears_groups()
    {
        var exec = ExecLine(Enter());
        exec.ShouldContain("--clear-groups");
        exec.ShouldContain("--reuid=\"$APP_UID\"");
        exec.ShouldContain("--regid=\"$APP_GID\"");
        Enter().ShouldContain("APP_UID=1654");
        Enter().ShouldContain("APP_GID=1654");
    }

    [Test]
    public void Shim_validates_guid_and_empty_cgroup()
    {
        var text = Enter();
        // The execution id is validated as a GUID before it ever becomes a path component.
        text.ShouldContain(GuidPattern);
        text.ShouldContain("refuse InvalidExecutionId");
        text.ShouldContain("require_guid \"$EXECUTION_ID\"");
        text.ShouldContain("refuse CustodyTreeNotEmpty");
        text.ShouldContain("require_under_root");

        // Both hierarchies get the same emptiness check on v1; neither may be skipped.
        Occurrences(text, "refuse CustodyTreeNotEmpty").ShouldBe(2);
        Order(text, "require_guid \"$EXECUTION_ID\"", "prepare_tree_v2 \"$EXECUTION_ID\"")
            .ShouldBeTrue("the id is validated before any tree is created");
    }

    // G-35. The whole grant is two commands for one uid. A third command line, a second user,
    // an ALL command or a missing env_reset all fail here.
    [Test]
    public void Sudoers_names_only_the_helpers()
    {
        var text = Sudoers();
        var rules = Rules(text).Where(line => !line.StartsWith("Defaults", StringComparison.Ordinal)).ToArray();
        rules.Length.ShouldBe(1, "exactly one user specification: " + string.Join(" | ", rules));
        rules[0].ShouldBe(
            "#1654 ALL=(root) NOPASSWD: /usr/local/bin/antiphon-custody-enter, /usr/local/bin/antiphon-custody-kill");

        Rules(text).Count(line => line.Contains("NOPASSWD", StringComparison.Ordinal)).ShouldBe(1);
        Rules(text).ShouldContain("Defaults:#1654 env_reset");

        // No path outside the two helpers may appear in an effective line, and no rule may
        // grant every command or target a user other than uid 1654.
        foreach (var line in Rules(text))
        {
            foreach (var command in Regex.Matches(line, @"/\S+").Select(m => m.Value))
                command.ShouldBeOneOf("/usr/local/bin/antiphon-custody-enter", "/usr/local/bin/antiphon-custody-kill");
            line.ShouldNotContain("NOPASSWD: ALL");
            line.ShouldNotContain("(ALL)");
        }
    }

    // G-39. The id is the only caller-supplied path component and it is checked twice: as a
    // GUID before concatenation, and as a resolved path under the custody root afterwards.
    // Neither alone is enough -- the first rejects traversal, the second a symlinked root.
    [Test]
    public void Kill_helper_validates_execution_path()
    {
        var text = Kill();
        text.ShouldContain(GuidPattern);
        text.ShouldContain("refuse InvalidExecutionId");
        text.ShouldContain("require_guid \"$EXECUTION_ID\"");
        text.ShouldContain("refuse ExecutionPathOutsideCustodyRoot");
        text.ShouldContain("realpath -m -- \"$candidate\"");

        // Every kill path validates the directory it is about to empty.
        text.ShouldContain("require_under_root \"$dir\" \"$V2_ROOT\"");
        text.ShouldContain("require_under_root \"$pids_dir\" \"$V1_PIDS_ROOT\"");
        text.ShouldContain("require_under_root \"$freezer_dir\" \"$V1_FREEZER_ROOT\"");
        Order(text, "require_guid \"$EXECUTION_ID\"", "kill_v2 \"$EXECUTION_ID\"")
            .ShouldBeTrue("the id is validated before any tree is emptied");

        // The helper takes the id and nothing else, so a second argument cannot smuggle a path.
        text.ShouldContain("refuse UsageExactlyOneExecutionId");
    }

    // Every signalled pid comes from the validated tree's own cgroup.procs. A name match, a
    // process-group signal or kill -1 would reach processes outside custody, including the
    // observing pty-host and the nested daemon.
    [Test]
    public void Kill_helper_never_kills_outside_tree()
    {
        var text = Kill();
        text.ShouldContain("for pid in $(tree_pids \"$pids_dir/tree/cgroup.procs\" || true); do");
        text.ShouldContain("kill -KILL \"$pid\"");
        Occurrences(text, "kill -KILL").ShouldBe(1, "exactly one signalling site");

        foreach (var forbidden in new[] { "pkill", "killall", "kill -9 -1", "kill -KILL -1", "kill -- -", "-KILL -1" })
            text.ShouldNotContain(forbidden);

        // An unreadable or missing procs file is unknown, never an empty tree: the caller must
        // see Draining rather than a fabricated zero.
        text.ShouldContain("[ -f \"$procs\" ] || return 1");
        text.ShouldContain("C604_CUSTODY_KILL_DRAINING");
        text.ShouldContain("DRAIN_SECONDS=30");
    }

    // server2 is kernel 4.15 (cgroup v1, freezer + SIGKILL); Docker Desktop is cgroup v2
    // (cgroup.kill). The containment measurement runs on both (V-28) precisely because the two
    // code paths are different, so both must exist in both helpers.
    [Test]
    public void Helpers_branch_on_cgroup_version()
    {
        foreach (var text in new[] { Enter(), Kill() })
        {
            // v2 is detected by the unified root's own controllers file.
            text.ShouldContain("[ -f /sys/fs/cgroup/cgroup.controllers ]");
            text.ShouldContain("V2_ROOT=\"/sys/fs/cgroup/$CUSTODY_ROOT\"");
            text.ShouldContain("V1_PIDS_ROOT=\"/sys/fs/cgroup/pids/$CUSTODY_ROOT\"");
            text.ShouldContain("V1_FREEZER_ROOT=\"/sys/fs/cgroup/freezer/$CUSTODY_ROOT\"");
            text.ShouldContain("CUSTODY_ROOT=antiphon-custody");
            text.ShouldContain("--probe");
        }

        var kill = Kill();
        kill.ShouldContain("cgroup.kill");
        kill.ShouldContain("echo FROZEN > \"$freezer_dir/tree/freezer.state\"");
        kill.ShouldContain("echo THAWED > \"$freezer_dir/tree/freezer.state\"");
        Order(kill, "echo FROZEN", "kill -KILL").ShouldBeTrue("the tree is frozen before it is killed, so a fork cannot outrun the kill");
        Order(kill, "kill -KILL", "echo THAWED").ShouldBeTrue("the tree is thawed after the kill");

        // v1 has two hierarchies and the shim must enter both, or one of them accounts nothing.
        Enter().ShouldContain("for hierarchy in \"$V1_PIDS_ROOT\" \"$V1_FREEZER_ROOT\"; do");
    }

    // The entrypoint creates the root the helpers work under, and never hands it to uid 1654:
    // a child that could chown or mkdir there would not need the shim at all.
    [Test]
    public void Entrypoint_keeps_the_custody_root_root_owned()
    {
        var text = DockerStackDocuments.Read("docker/session-runner-grok/dind-entrypoint.sh");
        text.ShouldContain("CUSTODY_ROOT=antiphon-custody");
        text.ShouldContain("mkdir -p \"/sys/fs/cgroup/$CUSTODY_ROOT\"");
        text.ShouldContain("mkdir -p \"/sys/fs/cgroup/pids/$CUSTODY_ROOT\" \"/sys/fs/cgroup/freezer/$CUSTODY_ROOT\"");

        foreach (var line in text.Replace("\r\n", "\n").Split('\n'))
            if (line.Contains("chown", StringComparison.Ordinal))
                line.ShouldNotContain("cgroup");
    }

    // The two helpers and the sudoers file are installed root-owned with the modes D-17 names,
    // and `visudo -c` runs at build time so a syntax error cannot ship.
    [Test]
    public void Image_installs_the_helpers_root_owned()
    {
        var stage = Stage("session-testing");
        stage.ShouldContain("COPY docker/session-runner-grok/antiphon-custody-enter.sh /usr/local/bin/antiphon-custody-enter");
        stage.ShouldContain("COPY docker/session-runner-grok/antiphon-custody-kill.sh /usr/local/bin/antiphon-custody-kill");
        stage.ShouldContain("COPY docker/session-runner-grok/sudoers-antiphon-custody /etc/sudoers.d/antiphon-custody");
        stage.ShouldContain("chown 0:0 /usr/local/bin/antiphon-custody-enter /usr/local/bin/antiphon-custody-kill");
        stage.ShouldContain("chmod 0755 /usr/local/bin/antiphon-custody-enter /usr/local/bin/antiphon-custody-kill");
        stage.ShouldContain("chmod 0440 /etc/sudoers.d/antiphon-custody");
        stage.ShouldContain("visudo -c");
        stage.ShouldContain("setpriv --help | grep -F -- '--no-new-privs'");
    }

    private const string GuidPattern =
        "^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$";

    private static string ExecLine(string text) => text.Replace("\r\n", "\n").Split('\n')
        .Single(line => line.StartsWith("exec setpriv", StringComparison.Ordinal)).Trim();

    private static IReadOnlyList<string> Rules(string text) => text.Replace("\r\n", "\n").Split('\n')
        .Select(line => line.Trim())
        .Where(line => line.Length != 0 && line[0] != '#' || line.StartsWith("#1654", StringComparison.Ordinal))
        .ToArray();

    private static int Occurrences(string text, string needle)
    {
        var count = 0;
        for (var i = text.IndexOf(needle, StringComparison.Ordinal); i >= 0;
             i = text.IndexOf(needle, i + needle.Length, StringComparison.Ordinal)) count++;
        return count;
    }

    private static bool Order(string text, string first, string second)
    {
        var a = text.IndexOf(first, StringComparison.Ordinal);
        var b = text.IndexOf(second, StringComparison.Ordinal);
        return a >= 0 && b > a;
    }

    private static string Stage(string name) => DockerStackDocuments
        .Stages(DockerStackDocuments.Read("docker/session-runner-grok/Dockerfile"))
        .Single(s => s.Name == name).Body;

    private static string Enter() => DockerStackDocuments.Read("docker/session-runner-grok/antiphon-custody-enter.sh");
    private static string Kill() => DockerStackDocuments.Read("docker/session-runner-grok/antiphon-custody-kill.sh");
    private static string Sudoers() => DockerStackDocuments.Read("docker/session-runner-grok/sudoers-antiphon-custody");
}
