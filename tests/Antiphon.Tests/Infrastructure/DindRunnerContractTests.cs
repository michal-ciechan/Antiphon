using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Infrastructure;

// CARD-0604 S1. Text guards over the files the persistent server2 runner boots from: the
// fail-together entrypoint (D-3), the nested daemon configuration (D-4) and the baked, non-secret
// git/ssh custody configuration (D-8). None of these are exercised by a build; they are only ever
// read by a root process on server2, so the guard is the file's own text.
[Category("Unit")]
public sealed class DindRunnerContractTests
{
    [Test]
    public void Entrypoint_refuses_missing_deploy_key()
    {
        var text = Entrypoint();
        text.ShouldContain("DeployKeyMissing");
        Refusal(text, "DeployKeyMissing").ShouldBeTrue("a missing or empty deploy key file refuses before dockerd starts");
        Order(text, "DeployKeyMissing", "dockerd --config-file").ShouldBeTrue("the key check precedes dockerd");
    }

    [Test]
    public void Entrypoint_refuses_missing_phone_home_secret()
    {
        var text = Entrypoint();
        Refusal(text, "PhoneHomeSecretMissing").ShouldBeTrue("a missing or empty phone-home secret refuses");
        Order(text, "PhoneHomeSecretMissing", "dockerd --config-file").ShouldBeTrue("the secret check precedes dockerd");
    }

    // CARD-0604 D-1. This assertion used to read the other way round -- "the phone-home secret is
    // never copied" -- and that is precisely the bug it was locking in. A compose secret file
    // arrives owned by the HOST uid at 0600; the entrypoint drops to uid 1654, which can never
    // open it. The standing server2 runner registered zero times in 304 attempts, every one an
    // UnauthorizedAccessException on /run/secrets/phone-home, while the container reported
    // healthy. The secret must be staged onto the app-owned tmpfs exactly as the deploy key is.
    [Test]
    public void Entrypoint_stages_the_phone_home_secret_for_the_app_uid()
    {
        var text = Entrypoint();

        // Source and target are distinct: reading the destination as the source would stage the
        // file onto itself and restore the unreadable original.
        text.ShouldContain("PHONE_HOME_SECRET_SOURCE=\"${ANTIPHON_PHONE_HOME_SECRET_SOURCE:-/run/secrets/phone-home}\"");
        text.ShouldContain("PHONE_HOME_SECRET_TARGET=\"$RUNTIME_DIR/phone-home\"");

        // Staged the same way, and with the same ownership, as the deploy key already is.
        text.ShouldContain(
            "install -m 0400 -o \"$APP_UID\" -g \"$APP_GID\" \"$PHONE_HOME_SECRET_SOURCE\" \"$PHONE_HOME_SECRET_TARGET\"");

        // And the runner is pointed at the staged copy, not at the mount it cannot read.
        text.ShouldContain("export PhoneHome__SecretPath=\"$PHONE_HOME_SECRET_TARGET\"");
        Order(text, "install -m 0400 -o \"$APP_UID\" -g \"$APP_GID\" \"$PHONE_HOME_SECRET_SOURCE\"", "setpriv --reuid=")
            .ShouldBeTrue("the secret is staged before the runner is started");
    }

    // CARD-0604 D-1. Staged is not the same as readable: an ownership or mode regression must
    // refuse at boot, not loop forever behind a green /health.
    [Test]
    public void Entrypoint_proves_the_app_uid_can_read_the_staged_secret()
    {
        var text = Entrypoint();
        Refusal(text, "PhoneHomeSecretUnreadable").ShouldBeTrue("an unreadable staged secret is a named refusal");
        text.ShouldContain("setpriv --reuid=\"$APP_UID\" --regid=\"$APP_GID\" --clear-groups");
        text.ShouldContain("head -c 1 \"$1\" >/dev/null 2>&1");
        Order(text, "PhoneHomeSecretUnreadable", "dockerd --config-file")
            .ShouldBeTrue("the readability probe precedes dockerd");
    }

    // CARD-0604 D-1/D-2. The compose file must mount the secret at the source the entrypoint
    // stages FROM and point the runner at the staged copy -- the two have to agree, or the
    // runner is silently handed back the file it cannot open.
    [Test]
    public void Server2_compose_reads_the_staged_phone_home_secret()
    {
        var runner = DockerStackDocuments.Service(Server2Compose(), "session-runner");
        DockerStackDocuments.Env(runner, "PhoneHome__SecretPath").ShouldBe("/run/antiphon/phone-home");
        DockerStackDocuments.Env(runner, "ANTIPHON_PHONE_HOME_SECRET_SOURCE").ShouldBe("/run/secrets/phone-home");
        DockerStackDocuments.List(runner, "tmpfs").ShouldContain("/run/antiphon");
        DockerStackDocuments.List(runner, "secrets").ShouldContain("phone-home");
    }

    // CARD-0604 D-2. /health and `docker info` both answer yes on a runner that has never once
    // registered. The healthcheck has to include the one thing that was actually broken.
    [Test]
    public void Server2_healthcheck_covers_phone_home_secret_readability()
    {
        var runner = DockerStackDocuments.Service(Server2Compose(), "session-runner");
        var line = runner.Replace("\r\n", "\n").Split('\n')
            .Select(l => l.Trim())
            .SingleOrDefault(l => l.StartsWith("test:", StringComparison.Ordinal));
        line.ShouldNotBeNull("the session-runner service declares exactly one healthcheck test");
        line.ShouldContain("/health");
        line.ShouldContain("docker info");
        line.ShouldContain("setpriv --reuid=1654");
        line.ShouldContain("PhoneHome__SecretPath");
    }

    [Test]
    public void Entrypoint_drops_to_app_uid()
    {
        var text = Entrypoint();
        text.ShouldContain("APP_UID=1654");
        text.ShouldContain("APP_GID=1654");
        text.ShouldContain("NESTED_SOCKET_GID=1656");
        text.ShouldContain("setpriv --reuid=\"$APP_UID\" --regid=\"$APP_GID\" --groups=\"$NESTED_SOCKET_GID\" \"$@\"");
        Order(text, "dockerd --config-file", "setpriv --reuid=").ShouldBeTrue("the daemon is up before the runner starts");
    }

    [Test]
    public void Entrypoint_exits_when_either_process_exits()
    {
        var text = Entrypoint();
        text.ShouldContain("wait -n \"$DOCKERD_PID\" \"$RUNNER_PID\"");
        text.ShouldContain("trap forward_term TERM INT");
        text.ShouldContain("exit \"$first_status\"");
        // Both survivors are terminated: neither child may be left running alone.
        text.ShouldContain("kill -TERM \"$DOCKERD_PID\"");
        text.ShouldContain("kill -TERM \"$RUNNER_PID\"");
        text.Contains("exec setpriv", StringComparison.Ordinal)
            .ShouldBeFalse("exec would leave a healthy-looking runner behind a dead daemon");
    }

    [Test]
    public void Entrypoint_checks_iptables_before_dockerd()
    {
        var text = Entrypoint();
        Refusal(text, "IptablesUnusable").ShouldBeTrue();
        text.ShouldContain("iptables -nL");
        Order(text, "iptables -nL", "dockerd --config-file").ShouldBeTrue("a wrong iptables backend is a named refusal, not a hang");
    }

    [Test]
    public void Entrypoint_never_prints_the_key()
    {
        var text = Entrypoint();
        foreach (var line in text.Replace("\r\n", "\n").Split('\n'))
        {
            var trimmed = line.Trim();
            if (!trimmed.StartsWith("echo", StringComparison.Ordinal)
                && !trimmed.StartsWith("printf", StringComparison.Ordinal)
                && !trimmed.StartsWith("cat ", StringComparison.Ordinal))
                continue;
            foreach (var secret in new[]
                     {
                         "DEPLOY_KEY_SOURCE", "DEPLOY_KEY_TARGET",
                         "PHONE_HOME_SECRET_SOURCE", "PHONE_HOME_SECRET_TARGET",
                     })
                trimmed.Contains(secret, StringComparison.Ordinal)
                    .ShouldBeFalse("entrypoint line prints a secret path's contents: " + trimmed);
        }

        foreach (var hasher in new[] { "sha256sum", "md5sum", "sha1sum", "openssl dgst" })
            text.Contains(hasher, StringComparison.Ordinal).ShouldBeFalse("entrypoint hashes a secret with " + hasher);
    }

    [Test]
    public void Entrypoint_prepares_custody_root_on_both_cgroup_versions()
    {
        var text = Entrypoint();
        text.ShouldContain("CUSTODY_ROOT=antiphon-custody");
        text.ShouldContain("/sys/fs/cgroup/cgroup.controllers");
        text.ShouldContain("/sys/fs/cgroup/$CUSTODY_ROOT");
        text.ShouldContain("/sys/fs/cgroup/pids/$CUSTODY_ROOT");
        text.ShouldContain("/sys/fs/cgroup/freezer/$CUSTODY_ROOT");
        text.ShouldContain("cgroup.subtree_control");
        // Nothing under the custody root may be handed to the app uid: the child must not be able
        // to leave the cgroup it is placed in.
        foreach (var line in text.Replace("\r\n", "\n").Split('\n'))
            if (line.Contains("chown", StringComparison.Ordinal))
                line.Contains("cgroup", StringComparison.Ordinal)
                    .ShouldBeFalse("the custody root is never chowned: " + line.Trim());
    }

    [Test]
    public void Nested_daemon_uses_private_address_pools()
    {
        var text = Daemon();
        text.ShouldContain("\"bip\": \"10.200.0.1/24\"");
        text.ShouldContain("\"base\": \"10.201.0.0/16\"");
        text.ShouldContain("\"data-root\": \"/var/lib/docker\"");
        text.ShouldContain("\"storage-driver\": \"overlay2\"");
        text.ShouldContain("\"iptables\": true");
        text.ShouldContain("\"live-restore\": false");
    }

    [Test]
    public void Nested_daemon_socket_group_is_docker_nested()
    {
        Daemon().ShouldContain("\"group\": \"docker-nested\"");
        Daemon().ShouldContain("unix:///var/run/docker.sock");
        // The nested socket group is its own gid, never the app's own group: a tracked session is
        // fenced off the daemon by dropping the supplementary group alone (D-18).
        Daemon().Contains("\"group\": \"1654\"", StringComparison.Ordinal).ShouldBeFalse();
    }

    [Test]
    public void Ssh_config_pins_identity_and_known_hosts()
    {
        var text = SshConfig();
        text.ShouldContain("IdentityFile /run/antiphon/deploy-key");
        text.ShouldContain("IdentitiesOnly yes");
        text.ShouldContain("StrictHostKeyChecking yes");
        text.ShouldContain("UserKnownHostsFile /etc/antiphon/github_known_hosts");
        text.ShouldContain("BatchMode yes");
    }

    [Test]
    public void Ssh_config_uses_port_443()
    {
        var text = SshConfig();
        text.ShouldContain("HostName ssh.github.com");
        text.ShouldContain("Port 443");
        text.ShouldContain("User git");
    }

    [Test]
    public void Gitconfig_pushes_over_ssh_only()
    {
        var text = GitConfig();
        text.ShouldContain("sshCommand = ssh -F /etc/antiphon/ssh_config");
        text.ShouldContain("pushInsteadOf = https://github.com/");
        // insteadOf (without "push") would send anonymous fetches over SSH too.
        text.Replace("pushInsteadOf", "", StringComparison.Ordinal)
            .Contains("insteadOf", StringComparison.Ordinal).ShouldBeFalse("fetches stay anonymous HTTPS");
    }

    [Test]
    public void Known_hosts_carry_github_keys()
    {
        var lines = KnownHosts().Replace("\r\n", "\n").Split('\n')
            .Where(line => line.Length > 0 && line[0] != '#')
            .ToList();
        lines.ShouldNotBeEmpty();
        // ssh_config connects to ssh.github.com:443, which is the name ssh verifies against.
        lines.Any(line => line.StartsWith("[ssh.github.com]:443 ", StringComparison.Ordinal))
            .ShouldBeTrue("known_hosts covers the [ssh.github.com]:443 form ssh actually checks");
        lines.Any(line => line.Contains(" ssh-ed25519 ", StringComparison.Ordinal)).ShouldBeTrue();
        lines.Any(line => line.Contains(" ssh-rsa ", StringComparison.Ordinal)).ShouldBeTrue();
        lines.All(line => line.Split(' ').Length == 3).ShouldBeTrue("every entry is host, type, key");
    }

    private static bool Refusal(string text, string diagnosis)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n');
        return lines.Any(line => line.Trim() == "refuse " + diagnosis)
            && text.Contains("echo \"C604_ENTRYPOINT_REFUSED $1\" >&2", StringComparison.Ordinal)
            && text.Contains("exit 3", StringComparison.Ordinal);
    }

    private static bool Order(string text, string first, string second)
    {
        var a = text.IndexOf(first, StringComparison.Ordinal);
        var b = text.IndexOf(second, StringComparison.Ordinal);
        return a >= 0 && b >= 0 && a < b;
    }

    private static string Entrypoint() => Read("docker/session-runner-grok/dind-entrypoint.sh");

    private static string Server2Compose() => Read("docker-compose.server2-runner.yml");

    private static string Daemon() => Read("docker/session-runner-grok/daemon.json");

    private static string SshConfig() => Read("docker/session-runner-grok/ssh_config");

    private static string GitConfig() => Read("docker/session-runner-grok/gitconfig");

    private static string KnownHosts() => Read("docker/session-runner-grok/github_known_hosts");

    private static string Read(string relative) => DockerStackDocuments.Read(relative);
}
