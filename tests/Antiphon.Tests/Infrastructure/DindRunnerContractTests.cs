using System.Diagnostics;
using System.Text.Json.Nodes;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Infrastructure;

// CARD-0604 S1. Text guards over the files the persistent server2 runner boots from: the
// fail-together entrypoint (D-3), the nested daemon configuration (D-4) and the baked, non-secret
// git/ssh custody configuration (D-8). None of these are exercised by a build; they are only ever
// read by a root process on server2, so the guard is the file's own text. The runner's git
// identity is not baked (CARD-0631): it is a mounted server2 file, guarded below.
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
        Order(text, "install -m 0400 -o \"$APP_UID\" -g \"$APP_GID\" \"$PHONE_HOME_SECRET_SOURCE\"",
            "--groups=\"$NESTED_SOCKET_GID\" \"$@\"")
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
    public void Temp_override_keeps_the_base_healthcheck_and_secret_path()
    {
        var baseRunner = DockerStackDocuments.Service(Server2Compose(), "session-runner");
        var tempRunner = DockerStackDocuments.Service(Read("docker-compose.server2-runner.temp.yml"), "session-runner");
        baseRunner.ShouldContain("healthcheck:");
        baseRunner.ShouldContain("PhoneHome__SecretPath: /run/antiphon/phone-home");
        baseRunner.ShouldContain("ANTIPHON_PHONE_HOME_SECRET_SOURCE: /run/secrets/phone-home");
        tempRunner.ShouldNotContain("healthcheck:");
        tempRunner.ShouldNotContain("PhoneHome__SecretPath");
        tempRunner.ShouldNotContain("ANTIPHON_PHONE_HOME_SECRET_SOURCE");
        tempRunner.ShouldContain("PhoneHome__RunnerId: server2-temp");
    }

    [Test]
    public void Entrypoint_drops_to_app_uid()
    {
        var text = Entrypoint();
        text.ShouldContain("APP_UID=1654");
        text.ShouldContain("APP_GID=1654");
        text.ShouldContain("NESTED_SOCKET_GID=1656");
        text.ShouldContain("setpriv --reuid=\"$APP_UID\" --regid=\"$APP_GID\" --groups=\"$NESTED_SOCKET_GID\" \"$@\"");
        Order(text, "dockerd --config-file", "--groups=\"$NESTED_SOCKET_GID\" \"$@\"")
            .ShouldBeTrue("the daemon is up before the runner starts");
    }

    // CARD-0628 D-1 (Round D): the token file is the only source. Exported after dockerd starts so
    // only the runner (and its pty children) inherit it; missing or empty is not a refusal.
    [Test]
    public void Entrypoint_exports_the_claude_token_from_the_mounted_file()
    {
        var text = Entrypoint();
        text.ShouldContain("CLAUDE_OAUTH_TOKEN_SOURCE=\"$RUNTIME_DIR/claude-oauth-token\"");
        text.ShouldContain("unset CLAUDE_CODE_OAUTH_TOKEN");
        text.ShouldContain("if [ -f \"$CLAUDE_OAUTH_TOKEN_SOURCE\" ] && [ -s \"$CLAUDE_OAUTH_TOKEN_SOURCE\" ]; then");
        text.ShouldContain("claude_oauth_token=\"$(tr -d '[:space:]' < \"$CLAUDE_OAUTH_TOKEN_SOURCE\")\"");
        text.ShouldContain("export CLAUDE_CODE_OAUTH_TOKEN=\"$claude_oauth_token\"");
        Order(text, "dockerd --config-file", "unset CLAUDE_CODE_OAUTH_TOKEN")
            .ShouldBeTrue("dockerd never inherits the token");
        Order(text, "export CLAUDE_CODE_OAUTH_TOKEN=", "--groups=\"$NESTED_SOCKET_GID\" \"$@\"")
            .ShouldBeTrue("exported before the runner starts");
        Refusal(text, "ClaudeOAuthTokenMissing").ShouldBeFalse("an absent token is signed out, not a refusal");
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
                         "CLAUDE_OAUTH_TOKEN_SOURCE", "CLAUDE_CODE_OAUTH_TOKEN", "claude_oauth_token",
                         "GITHUB_TOKEN_SOURCE",
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
    [ParallelLimiter<ProcessSpawnLimit>]
    public async Task Gitconfig_pushes_antiphon_over_ssh_and_other_repositories_over_https()
    {
        var text = GitConfig();
        text.ShouldContain("sshCommand = ssh -F /etc/antiphon/ssh_config");
        const string primary = "https://github.com/michal-ciechan/Antiphon.git";
        text.ShouldContain("[url \"git@github.com:michal-ciechan/Antiphon.git\"]");
        text.ShouldContain("pushInsteadOf = " + primary + "\n");
        Read("src/Antiphon.SessionRunner/RunnerWorkspaceService.cs")
            .ShouldContain("DefaultCloneSource = \"" + primary + "\"");
        text.ShouldContain("[credential \"https://github.com\"]");
        text.ShouldContain("helper = /usr/local/bin/antiphon-github-credential");
        text.ShouldContain("useHttpPath = true");
        text.ShouldContain("username = x-access-token");
        // insteadOf (without "push") would send anonymous fetches over SSH too.
        text.Replace("pushInsteadOf", "", StringComparison.Ordinal)
            .Contains("insteadOf", StringComparison.Ordinal).ShouldBeFalse("fetches stay anonymous HTTPS");
        var root = Directory.CreateTempSubdirectory("c0817-rewrite-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(root, "empty.gitconfig"), "");
            await Git(root, "init");
            await Git(root, "remote", "add", "origin", primary);
            foreach (var (url, expected) in new[]
            {
                (primary, "git@github.com:michal-ciechan/Antiphon.git"),
                ("https://github.com/michal-ciechan/antiphon.git", "https://github.com/michal-ciechan/antiphon.git"),
                ("https://github.com/michal-ciechan/markdown-package.git", "https://github.com/michal-ciechan/markdown-package.git")
            })
            {
                await Git(root, "remote", "set-url", "origin", url);
                (await Git(root, "remote", "get-url", "--push", "origin")).Trim().ShouldBe(expected);
                (await Git(root, "remote", "get-url", "origin")).Trim().ShouldBe(url);
            }
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Test]
    public void Entrypoint_notes_github_token_presence_without_printing_it()
    {
        var text = Entrypoint();
        text.ShouldContain("GITHUB_TOKEN_SOURCE=\"$RUNTIME_DIR/github-token/token\"");
        text.ShouldContain("if [ -s \"$GITHUB_TOKEN_SOURCE\" ]; then");
        text.ShouldContain("C604_ENTRYPOINT_NOTE GithubTokenPresent");
        text.ShouldContain("C604_ENTRYPOINT_NOTE GithubTokenAbsent");
        text.ShouldNotContain("refuse GithubToken");
        Order(text, "GitIdentityUnusable", "C604_ENTRYPOINT_NOTE GithubTokenPresent").ShouldBeTrue();
        foreach (var line in text.Split('\n').Where(line => line.Contains("GITHUB_TOKEN_SOURCE", StringComparison.Ordinal)))
            System.Text.RegularExpressions.Regex.IsMatch(line, @"\b(cat|tr|install|export)\b").ShouldBeFalse();
        text.All(c => c <= 127).ShouldBeTrue();
        var helper = Read("docker/session-runner-grok/github-credential.sh");
        helper.All(c => c <= 127).ShouldBeTrue();
        var image = DockerStackDocuments.Stages(Read("docker/session-runner-grok/Dockerfile"))
            .Single(stage => stage.Name == "session-testing").Body;
        image.ShouldContain("COPY docker/session-runner-grok/github-credential.sh /usr/local/bin/antiphon-github-credential");
        image.ShouldContain("chown root:root /usr/local/bin/antiphon-github-credential");
        image.ShouldContain("chmod 0755 /usr/local/bin/antiphon-github-credential");
        image.ShouldContain("sh -n /usr/local/bin/antiphon-github-credential");
    }

    [Test]
    public void Server2_compose_binds_the_github_token_directory_read_only()
    {
        var compose = Server2Compose();
        var runner = DockerStackDocuments.Service(compose, "session-runner");
        DockerStackDocuments.List(runner, "volumes").ShouldContain(
            "${RUNNER_GITHUB_TOKEN_DIR:?RUNNER_GITHUB_TOKEN_DIR is required}:/run/antiphon/github-token:ro");
        DockerStackDocuments.Env(runner, "PhoneHome__PushCredentialPolicyPath").ShouldBe("/run/antiphon/push-allow-list");
        DockerStackDocuments.Service(compose, "state-init").ShouldNotContain("RUNNER_GITHUB_TOKEN_DIR");
        var temp = Read("docker-compose.server2-runner.temp.yml");
        temp.ShouldNotContain("RUNNER_GITHUB_TOKEN_DIR");
        temp.ShouldNotContain("PhoneHome__PushCredentialPolicyPath");
        Read("docker/stack.env.example").ShouldContain("RUNNER_GITHUB_TOKEN_DIR=/home/mc/antiphon-server2/secrets/github-token");
        foreach (var yaml in new[] { compose, temp })
            System.Text.RegularExpressions.Regex.IsMatch(yaml, @"(?m)^\s*(GH_TOKEN|GITHUB_TOKEN)\s*[:=]").ShouldBeFalse();
    }

    private static async Task<string> Git(string root, params string[] args)
    {
        var start = new ProcessStartInfo("git")
        {
            WorkingDirectory = root, UseShellExecute = false,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        foreach (var arg in args) start.ArgumentList.Add(arg);
        start.Environment.Remove("GIT_CONFIG_NOSYSTEM");
        start.Environment["GIT_CONFIG_SYSTEM"] = Path.Combine(DockerStackDocuments.RepoRoot, "docker/session-runner-grok/gitconfig");
        start.Environment["GIT_CONFIG_GLOBAL"] = Path.Combine(root, "empty.gitconfig");
        start.Environment["GIT_CONFIG_COUNT"] = "0";
        start.Environment["GIT_TERMINAL_PROMPT"] = "0";
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); throw; }
        process.ExitCode.ShouldBe(0, await stderr);
        return await stdout;
    }

    // CARD-0631 V-6, D-9 as amended by the operator: the runner's commit identity is a FILE on
    // server2, mounted read-only and named by GIT_CONFIG_GLOBAL for uid 1654. It is never baked
    // into the image, where changing it would need a rebuild and every image would carry it.
    [Test]
    public void Gitconfig_sets_runner_identity_from_the_mounted_file()
    {
        // Never baked: the image's system gitconfig has no identity (comments excluded) and does not
        // include the mount either -- a system-level include would lose to any user-level stopgap.
        var baked = GitConfig().Replace("\r\n", "\n").Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && line[0] != '#' && line[0] != ';')
            .ToList();
        baked.ShouldNotContain("[user]", "no identity section is baked into /etc/gitconfig");
        baked.Any(line => line.StartsWith("name", StringComparison.Ordinal) || line.StartsWith("email", StringComparison.Ordinal))
            .ShouldBeFalse("no user.name/user.email key is baked");
        baked.Any(line => line.StartsWith("[include", StringComparison.Ordinal)).ShouldBeFalse();
        var dockerfile = Read("docker/session-runner-grok/Dockerfile");
        foreach (var token in new[] { "user.name", "user.email", "GIT_CONFIG_GLOBAL", "GIT_AUTHOR_", "GIT_COMMITTER_" })
            dockerfile.Contains(token, StringComparison.Ordinal).ShouldBeFalse("the Dockerfile bakes " + token);

        // Mounted: compose requires the host file and binds it read-only where the entrypoint looks.
        var runner = DockerStackDocuments.Service(Server2Compose(), "session-runner");
        DockerStackDocuments.List(runner, "volumes")
            .ShouldContain("${RUNNER_GIT_IDENTITY_FILE:?RUNNER_GIT_IDENTITY_FILE is required}:/run/antiphon/gitconfig:ro");
        DockerStackDocuments.Env(runner, "GIT_CONFIG_GLOBAL").ShouldBe("/run/antiphon/gitconfig",
            "docker exec probes resolve the same identity the runner does");

        // Pointed at: the entrypoint refuses a missing or unusable file, then exports it for the runner.
        var text = Entrypoint();
        text.ShouldContain("GIT_IDENTITY_SOURCE=\"$RUNTIME_DIR/gitconfig\"");
        text.ShouldContain("RUNTIME_DIR=/run/antiphon");
        Refusal(text, "GitIdentityMissing").ShouldBeTrue("a missing or empty identity file refuses");
        Refusal(text, "GitIdentityUnusable").ShouldBeTrue("an identity uid 1654 cannot read, or without both keys, refuses");
        text.ShouldContain("git config --file \"$1\" --get user.name >/dev/null && git config --file \"$1\" --get user.email >/dev/null");
        Order(text, "GitIdentityUnusable", "export GIT_CONFIG_GLOBAL=\"$GIT_IDENTITY_SOURCE\"").ShouldBeTrue();
        Order(text, "export GIT_CONFIG_GLOBAL=\"$GIT_IDENTITY_SOURCE\"", "--groups=\"$NESTED_SOCKET_GID\" \"$@\"")
            .ShouldBeTrue("exported before the runner starts");
        Order(text, "GitIdentityMissing", "dockerd --config-file").ShouldBeTrue("the identity check precedes dockerd");
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

    // CARD-0628 G-3a. The interactive-login fallback needs a writable store owned by the app uid
    // before the runner starts; init-state is the only root step that can chown it.
    [Test]
    public void Init_state_creates_the_claude_home()
    {
        var text = Read("docker/stack/init-state.sh").Replace("\r\n", "\n");
        var loop = text[text.IndexOf("for d in \\", StringComparison.Ordinal)..text.IndexOf("\ndo\n", StringComparison.Ordinal)];
        loop.Split([' ', '\n', '\\'], StringSplitOptions.RemoveEmptyEntries)
            .ShouldContain("/runner-state/claude", "the mkdir loop names the Claude store");
        text.ShouldContain("chown -R \"$uid:$gid\" /state /work /runner-state");
    }

    // CARD-0628: the entrypoint merges the keys the CLI reads before dockerd, and the script
    // does that merge without replacing an existing credential file.
    [Test]
    public void Entrypoint_seeds_claude_onboarding_before_dockerd()
    {
        var text = Entrypoint();
        text.ShouldContain("node /usr/local/bin/antiphon-seed-claude-onboarding.mjs");
        text.ShouldContain("CLAUDE_CONFIG_DIR=/state/claude");
        Refusal(text, "ClaudeOnboardingSeedFailed").ShouldBeTrue();
        Order(text, "node /usr/local/bin/antiphon-seed-claude-onboarding.mjs", "dockerd --config-file")
            .ShouldBeTrue("a seed failure refuses before dockerd starts");
        var dockerfile = Read("docker/session-runner-grok/Dockerfile");
        dockerfile.ShouldContain(
            "COPY docker/session-runner-grok/seed-claude-onboarding.mjs /usr/local/bin/antiphon-seed-claude-onboarding.mjs");
        var seed = Read("docker/session-runner-grok/seed-claude-onboarding.mjs");
        seed.ShouldContain("hasCompletedOnboarding");
        seed.ShouldContain("hasTrustDialogAccepted");
        seed.ShouldContain("hasCompletedProjectOnboarding");
        seed.ShouldContain("skipDangerousModePermissionPrompt");
        seed.ShouldContain("\"/work/worktrees\"");
        seed.ShouldContain("\"/work/repos/antiphon\"");
        var version = System.Text.RegularExpressions.Regex.Match(
            dockerfile, @"ARG CLAUDE_CODE_VERSION=(\d+\.\d+\.\d+)");
        version.Success.ShouldBeTrue();
        seed.ShouldContain("\"" + version.Groups[1].Value + "\"");
    }

    [Test]
    public async Task Seed_script_merges_onboarding_and_trust_and_keeps_credentials()
    {
        var root = Directory.CreateTempSubdirectory("c628-claude-seed");
        try
        {
            var configDir = Path.Combine(root.FullName, "claude");
            Directory.CreateDirectory(configDir);
            var credentials = Path.Combine(configDir, ".credentials.json");
            await File.WriteAllTextAsync(credentials, "{\"keep\":true}");
            await File.WriteAllTextAsync(
                Path.Combine(configDir, ".claude.json"),
                "{\"numStartups\":3,\"theme\":\"light\"}");
            await File.WriteAllTextAsync(
                Path.Combine(configDir, "settings.json"),
                "{\"model\":\"opus\"}");

            var script = Path.Combine(
                DockerStackDocuments.RepoRoot, "docker", "session-runner-grok", "seed-claude-onboarding.mjs");
            var first = await RunNodeAsync(script, configDir);
            first.ExitCode.ShouldBe(0, first.Error);
            var again = await RunNodeAsync(script, configDir);
            again.ExitCode.ShouldBe(0, again.Error);

            var config = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(configDir, ".claude.json")))!;
            config["hasCompletedOnboarding"]!.GetValue<bool>().ShouldBeTrue();
            config["lastOnboardingVersion"]!.GetValue<string>().ShouldBe("2.1.280");
            config["theme"]!.GetValue<string>().ShouldBe("light");
            config["numStartups"]!.GetValue<int>().ShouldBe(3);
            foreach (var key in new[] { "/work/worktrees", "/work/repos/antiphon" })
            {
                var project = config["projects"]![key]!;
                project["hasTrustDialogAccepted"]!.GetValue<bool>().ShouldBeTrue();
                project["hasCompletedProjectOnboarding"]!.GetValue<bool>().ShouldBeTrue();
            }

            var settings = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(configDir, "settings.json")))!;
            settings["skipDangerousModePermissionPrompt"]!.GetValue<bool>().ShouldBeTrue();
            settings["model"]!.GetValue<string>().ShouldBe("opus");
            (await File.ReadAllTextAsync(credentials)).ShouldBe("{\"keep\":true}");
        }
        finally
        {
            Directory.Delete(root.FullName, recursive: true);
        }
    }

    private static async Task<(int ExitCode, string Error)> RunNodeAsync(string script, string configDir)
    {
        var start = new ProcessStartInfo
        {
            FileName = "node",
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        start.ArgumentList.Add(script);
        start.Environment["CLAUDE_CONFIG_DIR"] = configDir;
        using var process = Process.Start(start);
        process.ShouldNotBeNull();
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        await Task.WhenAll(stdout, stderr);
        return (process.ExitCode, await stderr);
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
