using System.Diagnostics;
using System.Text.RegularExpressions;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Infrastructure.Git;
using Antiphon.Server.Infrastructure.GitHub;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Infrastructure;

/// <summary>
/// CARD-1776. The Linux server image bakes the runner's non-secret credential helper,
/// and the land-credential compose fragment names the read-only mounts. These witnesses
/// read files. They do not start a daemon, a container, or a database, and they do not
/// read a token.
/// </summary>
[Category("Unit")]
public sealed class ServerImagePushCredentialTests
{
    private const string ComposePath = "docker-compose.server2-land-credential.yml";
    private const string ScriptPath = "scripts/server2-repository-mirrors.ps1";
    private const string GitConfigPath = "docker/server/gitconfig";

    [Test]
    public void Server_image_bakes_the_runner_credential_helper()
    {
        var dockerfile = DockerStackDocuments.Read("Dockerfile");
        var runtime = DockerStackDocuments.Stages(dockerfile).Single(stage => stage.Name == "runtime");
        runtime.Body.ShouldContain(
            "COPY docker/session-runner-grok/github-credential.sh /usr/local/bin/antiphon-github-credential");
        runtime.Body.ShouldContain(
            "COPY docker/session-runner-grok/github_known_hosts /etc/antiphon/github_known_hosts");
        runtime.Body.ShouldContain("COPY docker/server/gitconfig /etc/gitconfig");
        runtime.Body.ShouldContain("sh -n /usr/local/bin/antiphon-github-credential");
        runtime.Body.ShouldContain("chmod 0755 /usr/local/bin/antiphon-github-credential");
        dockerfile.Contains("GH_TOKEN", StringComparison.Ordinal).ShouldBeFalse();
        dockerfile.Contains("GITHUB_TOKEN", StringComparison.Ordinal).ShouldBeFalse();
        dockerfile.Contains("ghp_", StringComparison.Ordinal).ShouldBeFalse();
    }

    [Test]
    public void Server_gitconfig_names_the_helper_and_not_an_ssh_push()
    {
        var path = Path.Combine(DockerStackDocuments.RepoRoot, GitConfigPath.Replace('/', Path.DirectorySeparatorChar));
        File.Exists(path).ShouldBeTrue(path);
        var text = File.ReadAllText(path);
        text.ShouldContain("helper = /usr/local/bin/antiphon-github-credential");
        text.ShouldContain("useHttpPath = true");
        text.ShouldContain("username = x-access-token");
        text.ShouldContain("[credential \"https://github.com\"]");
        text.Contains("pushInsteadOf", StringComparison.Ordinal).ShouldBeFalse();
        text.Contains("GH_TOKEN", StringComparison.Ordinal).ShouldBeFalse();
        text.Contains("GITHUB_TOKEN", StringComparison.Ordinal).ShouldBeFalse();
    }

    [Test]
    public void Land_credential_compose_mounts_the_token_directory_and_allow_list_by_variable()
    {
        var path = Path.Combine(DockerStackDocuments.RepoRoot, ComposePath);
        File.Exists(path).ShouldBeTrue(path);
        var text = DockerStackDocuments.Read(ComposePath);
        text.ShouldContain(
            "${RUNNER_GITHUB_TOKEN_DIR:?RUNNER_GITHUB_TOKEN_DIR is required}:/run/antiphon/github-token:ro");
        text.ShouldContain(
            "${SERVER_PUSH_ALLOW_LIST:?SERVER_PUSH_ALLOW_LIST is required}:/run/antiphon/push-allow-list:ro");
        text.ShouldContain("Git__PushCredentialTokenFile: /run/antiphon/github-token/token");
        text.ShouldContain("Git__PushCredentialPolicyPath: /run/antiphon/push-allow-list");
        text.ShouldContain("Git__IdentityFile: /run/antiphon/gitconfig");
        text.ShouldContain("GIT_CONFIG_GLOBAL: /run/antiphon/gitconfig");
        text.ShouldContain(
            "${RUNNER_GIT_IDENTITY_FILE:?RUNNER_GIT_IDENTITY_FILE is required}:/run/antiphon/gitconfig:ro");
        text.Contains("GH_TOKEN", StringComparison.Ordinal).ShouldBeFalse();
        // The required mount variable RUNNER_GITHUB_TOKEN_DIR contains the letters
        // GITHUB_TOKEN. Remove that required spelling, then refuse every other occurrence.
        text.Replace("RUNNER_GITHUB_TOKEN_DIR", "", StringComparison.Ordinal)
            .Contains("GITHUB_TOKEN", StringComparison.Ordinal)
            .ShouldBeFalse();
        text.Contains("privileged: true", StringComparison.Ordinal).ShouldBeFalse();
        text.Contains("docker.sock", StringComparison.Ordinal).ShouldBeFalse();
        text.Contains("ghp_", StringComparison.Ordinal).ShouldBeFalse();
        DockerStackDocuments.Service(text, "server").ShouldContain("/run/antiphon/github-token:ro");
    }

    [Test]
    public void Credentials_doc_records_server_container_custody_and_mirror_layout()
    {
        var credentials = DockerStackDocuments.Read("docs/agent-credentials.md");
        credentials.ShouldContain("### Linux server land credential (CARD-1776)");
        credentials.ShouldContain("RUNNER_GITHUB_TOKEN_DIR");
        credentials.ShouldContain("/run/antiphon/github-token:ro");
        credentials.ShouldContain("/usr/local/bin/antiphon-github-credential");
        credentials.ShouldContain("push_credential_absent");
        credentials.ShouldContain("RUNNER_GIT_IDENTITY_FILE");
        credentials.ShouldContain("GIT_CONFIG_GLOBAL");
        credentials.ShouldContain("/run/antiphon/gitconfig:ro");
        credentials.ShouldContain("user.name");
        credentials.ShouldContain("user.email");
        credentials.ShouldContain("0644");
        credentials.ShouldContain("git_identity_absent");
        credentials.ShouldContain("/work/repos/<name>");
        credentials.ShouldContain("gitRepositoryUrl");
        credentials.ShouldContain("before column");
        var sectionStart = credentials.IndexOf("### Linux server land credential (CARD-1776)", StringComparison.Ordinal);
        sectionStart.ShouldBeGreaterThanOrEqualTo(0);
        var section = credentials[sectionStart..];
        var nextHeading = section.IndexOf("\n### ", 4, StringComparison.Ordinal);
        if (nextHeading > 0)
            section = section[..nextHeading];
        section.Contains("GH_TOKEN=", StringComparison.Ordinal).ShouldBeFalse();
        section.Contains("GITHUB_TOKEN=", StringComparison.Ordinal).ShouldBeFalse();
        Regex.IsMatch(credentials, @"ghp_[A-Za-z0-9]{20,}").ShouldBeFalse();
        var stack = DockerStackDocuments.Read("docs/docker-stack.md");
        stack.ShouldContain("/work/repos/<name>");
        stack.ShouldContain("CARD-1776");
        stack.ShouldContain("docker-compose.server2-land-credential.yml");
        var land = stack[stack.IndexOf("### Linux land credential and repository mirrors (CARD-1776)", StringComparison.Ordinal)..];
        var landNext = land.IndexOf("\n### ", 4, StringComparison.Ordinal);
        if (landNext > 0)
            land = land[..landNext];
        land.ShouldContain("${RUNNER_GIT_IDENTITY_FILE}");
        land.ShouldContain("GIT_CONFIG_GLOBAL");
        land.ShouldContain("git_identity_absent");
    }

    [Test]
    public async Task Identity_free_rebase_fails_without_git_config_global_and_passes_with_it()
    {
        var root = NewRoot();
        try
        {
            var home = Path.Combine(root, "home");
            var repo = Path.Combine(root, "repo");
            var identity = Path.Combine(root, "gitconfig");
            Directory.CreateDirectory(home);
            Directory.CreateDirectory(repo);
            await File.WriteAllTextAsync(identity,
                "[user]\n\tname = Card 1776 Witness\n\temail = card-1776-witness@example.invalid\n");

            await SetupCommitAsync(repo, home, "init", "-q", "-b", "master");
            await SetupCommitAsync(repo, home, "commit", "-q", "--allow-empty", "-m", "base");
            await SetupCommitAsync(repo, home, "checkout", "-q", "-b", "topic");
            await SetupCommitAsync(repo, home, "commit", "-q", "--allow-empty", "-m", "topic");
            await SetupCommitAsync(repo, home, "checkout", "-q", "master");
            await SetupCommitAsync(repo, home, "commit", "-q", "--allow-empty", "-m", "advance");
            await SetupCommitAsync(repo, home, "checkout", "-q", "topic");

            var bare = await RebaseAsync(repo, home, identityFile: null);
            bare.Exit.ShouldBe(128);
            bare.Error.ShouldContain("unable to auto-detect email address");
            // The identity failure stops inside the rebase and leaves rebase-merge behind.
            await SetupCommitAsync(repo, home, "rebase", "--abort");

            var withIdentity = await RebaseAsync(repo, home, identity);
            withIdentity.Exit.ShouldBe(0);
            withIdentity.Error.ShouldContain("Successfully rebased");
        }
        finally { DeleteRoot(root); }
    }

    [Test]
    public async Task Configured_identity_file_rebases_when_the_child_environment_has_no_other_identity()
    {
        var root = NewRoot();
        try
        {
            var home = Path.Combine(root, "home");
            var repo = Path.Combine(root, "repo");
            var identity = Path.Combine(root, "gitconfig");
            Directory.CreateDirectory(home);
            Directory.CreateDirectory(repo);
            await File.WriteAllTextAsync(identity,
                "[user]\n\tname = Card 1776 Witness\n\temail = card-1776-witness@example.invalid\n");
            await SetupCommitAsync(repo, home, "init", "-q", "-b", "master");
            await SetupCommitAsync(repo, home, "commit", "-q", "--allow-empty", "-m", "base");
            await SetupCommitAsync(repo, home, "checkout", "-q", "-b", "topic");
            await SetupCommitAsync(repo, home, "commit", "-q", "--allow-empty", "-m", "topic");
            await SetupCommitAsync(repo, home, "checkout", "-q", "master");
            await SetupCommitAsync(repo, home, "commit", "-q", "--allow-empty", "-m", "advance");
            await SetupCommitAsync(repo, home, "checkout", "-q", "topic");

            var git = new IdentityFreeGit(home, Options.Create(new GitSettings { IdentityFile = identity }));
            var result = await git.RunAsync(repo,
                ["-c", "rebase.autoStash=false", "-c", "rebase.updateRefs=false", "rebase", "master"],
                CancellationToken.None);
            result.Succeeded.ShouldBeTrue(result.Diagnostic);
            result.RebaseHeadSha.ShouldNotBeNullOrWhiteSpace();
            git.Starts.ShouldBeGreaterThan(0);
        }
        finally { DeleteRoot(root); }
    }

    [Test]
    public async Task Configured_unusable_identity_refuses_before_git_starts()
    {
        var root = NewRoot();
        try
        {
            var missing = new StartCountingGit(Options.Create(new GitSettings
            {
                IdentityFile = Path.Combine(root, "missing"),
            }));
            var refused = await missing.RunAsync(root, ["rebase", "HEAD"], CancellationToken.None);
            refused.Diagnostic.ShouldBe(ServerGitIdentity.AbsentReason);
            refused.Succeeded.ShouldBeFalse();
            missing.Starts.ShouldBe(0);

            var incomplete = Path.Combine(root, "partial");
            await File.WriteAllTextAsync(incomplete, "[user]\n\tname = only-name\n");
            ServerGitIdentity.Refusal(incomplete).ShouldBe(ServerGitIdentity.AbsentReason);
            var directory = Path.Combine(root, "dir");
            Directory.CreateDirectory(directory);
            ServerGitIdentity.Refusal(directory).ShouldBe(ServerGitIdentity.AbsentReason);
            ServerGitIdentity.Refusal("relative-identity").ShouldBe(ServerGitIdentity.AbsentReason);
            ServerGitIdentity.Refusal("").ShouldBeNull();

            var good = Path.Combine(root, "good");
            await File.WriteAllTextAsync(good,
                "[user]\n\tname = Card 1776 Witness\n\temail = card-1776-witness@example.invalid\n");
            var link = Path.Combine(root, "link");
            File.CreateSymbolicLink(link, good);
            ServerGitIdentity.Refusal(good).ShouldBeNull();
            ServerGitIdentity.Refusal(link).ShouldBe(ServerGitIdentity.AbsentReason);

            var desktop = new StartCountingGit(Options.Create(new GitSettings()));
            var desktopResult = await CaptureAsync(desktop);
            desktopResult.Diagnostic.ShouldNotBe(ServerGitIdentity.AbsentReason);
        }
        finally { DeleteRoot(root); }
    }

    // CARD-1787. Each case is a usable [user] identity plus one line the allow-list refuses, so the
    // only defect in the file is the refused key or section.
    private const string UsableUser = "[user]\n\tname = Card 1787 Witness\n\temail = card-1787-witness@example.invalid\n";

    [Test]
    [Arguments("credential", UsableUser + "[credential]\n\thelper = /tmp/card-1787-steal\n")]
    [Arguments("credential-url", UsableUser + "[credential \"https://github.com\"]\n\thelper = /tmp/card-1787-steal\n")]
    [Arguments("include", UsableUser + "[include]\n\tpath = /tmp/card-1787-other\n")]
    [Arguments("include-if", UsableUser + "[includeIf \"gitdir:/work/\"]\n\tpath = /tmp/card-1787-other\n")]
    [Arguments("core", UsableUser + "[core]\n\tsshCommand = /tmp/card-1787-ssh\n")]
    [Arguments("core-hooks", UsableUser + "[core]\n\thooksPath = /tmp/card-1787-hooks\n")]
    [Arguments("alias", UsableUser + "[alias]\n\tpush = !/tmp/card-1787-alias\n")]
    [Arguments("url", UsableUser + "[url \"https://card-1787.example.invalid/\"]\n\tinsteadOf = https://github.com/\n")]
    [Arguments("user-extra-key", UsableUser + "\tsigningKey = card-1787\n")]
    [Arguments("user-bare-key", UsableUser + "\tuseConfigOnly\n")]
    [Arguments("user-subsection", UsableUser + "[user \"card-1787\"]\n\tname = Other\n")]
    [Arguments("user-dotted-subsection", UsableUser + "[user.card-1787]\n\tname = Other\n")]
    [Arguments("key-before-section", "name = Card 1787 Witness\n" + UsableUser)]
    [Arguments("key-on-header-line", "[user] name = Card 1787 Witness\n\temail = card-1787-witness@example.invalid\n")]
    [Arguments("continuation", "[user]\n\tname = Card 1787 \\\n\temail = card-1787-witness@example.invalid\n")]
    [Arguments("unclosed-header", UsableUser + "[credential\n\thelper = /tmp/card-1787-steal\n")]
    public async Task Identity_file_key_outside_user_name_and_email_is_invalid_before_git_starts(string label, string text)
    {
        var root = NewRoot();
        try
        {
            var path = Path.Combine(root, "gitconfig");
            await File.WriteAllTextAsync(path, text);
            ServerGitIdentity.Refusal(path).ShouldBe("git_identity_invalid", label);

            var git = new StartCountingGit(Options.Create(new GitSettings { IdentityFile = path }));
            var refused = await git.RunAsync(root, ["commit", "--allow-empty", "-m", "card-1787"], CancellationToken.None);
            refused.Succeeded.ShouldBeFalse(label);
            refused.Diagnostic.ShouldBe("git_identity_invalid", label);
            git.Starts.ShouldBe(0, label);
        }
        finally { DeleteRoot(root); }
    }

    [Test]
    public async Task Quoted_user_subsection_is_not_read_as_the_user_section()
    {
        var root = NewRoot();
        try
        {
            // git reads these keys as user.card-1787.name and user.card-1787.email, so the file has no
            // user.name or user.email at all.
            var path = Path.Combine(root, "gitconfig");
            await File.WriteAllTextAsync(path,
                "[user \"card-1787\"]\n\tname = Card 1787 Witness\n\temail = card-1787-witness@example.invalid\n");
            var (exit, error) = await RunGitAsync(root, root, new Dictionary<string, string?>(),
                "config", "--file", path, "--get", "user.name");
            exit.ShouldBe(1, error);
            ServerGitIdentity.Refusal(path).ShouldBe("git_identity_invalid");
        }
        finally { DeleteRoot(root); }
    }

    [Test]
    public async Task Identity_file_written_by_git_config_and_case_variants_stay_usable()
    {
        var root = NewRoot();
        try
        {
            // The deploy-parent path: git config --file writes the identity, quoting a value it must.
            var written = Path.Combine(root, "written");
            var (nameExit, nameError) = await RunGitAsync(root, root, new Dictionary<string, string?>(),
                "config", "--file", written, "user.name", "Card 1787; Witness");
            nameExit.ShouldBe(0, nameError);
            var (emailExit, emailError) = await RunGitAsync(root, root, new Dictionary<string, string?>(),
                "config", "--file", written, "user.email", "card-1787-witness@example.invalid");
            emailExit.ShouldBe(0, emailError);
            ServerGitIdentity.Refusal(written).ShouldBeNull();

            var variants = Path.Combine(root, "variants");
            await File.WriteAllTextAsync(variants,
                "# CARD-1787 identity\r\n; operator note\r\n[User] # trailing comment\r\n\tName = Card 1787 Witness\r\n\r\n"
                + "[USER]\r\n\tEMAIL=card-1787-witness@example.invalid ; inline\r\n");
            ServerGitIdentity.Refusal(variants).ShouldBeNull();

            var incomplete = Path.Combine(root, "incomplete");
            await File.WriteAllTextAsync(incomplete, "[user]\n\tname = Card 1787 Witness\n");
            ServerGitIdentity.Refusal(incomplete).ShouldBe(ServerGitIdentity.AbsentReason);
        }
        finally { DeleteRoot(root); }
    }

    [Test]
    public async Task Configured_identity_outranks_inherited_git_identity_and_config_variables()
    {
        var root = NewRoot();
        try
        {
            var (repo, home, identity) = await IdentityRepoAsync(root);
            var git = new InheritingGit(home, Options.Create(new GitSettings { IdentityFile = identity }));
            var result = await git.RunAsync(repo, ["commit", "-q", "--allow-empty", "-m", "card-1787"], CancellationToken.None);
            result.Succeeded.ShouldBeTrue(result.Diagnostic);
            git.Starts.ShouldBe(1);
            (await HeadIdentityAsync(repo, home)).ShouldBe(
                "Card 1787 Witness|card-1787-witness@example.invalid|Card 1787 Witness|card-1787-witness@example.invalid");

            var child = git.Environment!;
            foreach (var name in InheritingGit.Planted.Keys.Where(name => !name.StartsWith("GIT_CONFIG_", StringComparison.Ordinal)))
                child.ContainsKey(name).ShouldBeFalse(name);
            child.ContainsKey("GIT_CONFIG_PARAMETERS").ShouldBeFalse();
            // Only the fixed maintenance entries remain, renumbered from zero.
            child["GIT_CONFIG_COUNT"].ShouldBe("2");
            child["GIT_CONFIG_KEY_0"].ShouldBe("gc.auto");
            child["GIT_CONFIG_KEY_1"].ShouldBe("maintenance.auto");
            child.Keys.Count(name => name.StartsWith("GIT_CONFIG_KEY_", StringComparison.Ordinal)).ShouldBe(2);
            child.Keys.Count(name => name.StartsWith("GIT_CONFIG_VALUE_", StringComparison.Ordinal)).ShouldBe(2);
            child["GIT_CONFIG_GLOBAL"].ShouldBe(identity);
        }
        finally { DeleteRoot(root); }
    }

    [Test]
    public async Task Desktop_without_an_identity_file_keeps_the_inherited_git_environment()
    {
        var root = NewRoot();
        try
        {
            var (repo, home, _) = await IdentityRepoAsync(root);
            var git = new InheritingGit(home, Options.Create(new GitSettings()));
            var result = await git.RunAsync(repo, ["commit", "-q", "--allow-empty", "-m", "card-1787"], CancellationToken.None);
            result.Succeeded.ShouldBeTrue(result.Diagnostic);
            (await HeadIdentityAsync(repo, home)).ShouldBe(
                "Inherited Author|inherited-author@example.invalid|Inherited Committer|inherited-committer@example.invalid");
            var child = git.Environment!;
            child["GIT_CONFIG_PARAMETERS"].ShouldBe(InheritingGit.Planted["GIT_CONFIG_PARAMETERS"]);
            child["GIT_CONFIG_KEY_0"].ShouldBe("user.name");
            child["GIT_CONFIG_COUNT"].ShouldBe("3");
        }
        finally { DeleteRoot(root); }
    }

    [Test]
    public void Credentials_doc_records_the_identity_allow_list_and_environment_scrub()
    {
        var credentials = DockerStackDocuments.Read("docs/agent-credentials.md");
        var start = credentials.IndexOf("### Linux server land credential (CARD-1776)", StringComparison.Ordinal);
        start.ShouldBeGreaterThanOrEqualTo(0);
        var section = credentials[start..];
        var next = section.IndexOf("\n### ", 4, StringComparison.Ordinal);
        if (next > 0)
            section = section[..next];
        section.ShouldContain("CARD-1787");
        section.ShouldContain("git_identity_invalid");
        section.ShouldContain("credential.*");
        section.ShouldContain("include");
        section.ShouldContain("GIT_CONFIG_PARAMETERS");
        section.ShouldContain("GIT_CONFIG_COUNT");
        section.ShouldContain("GIT_AUTHOR_*");
        section.ShouldContain("GIT_COMMITTER_*");
    }

    private static async Task<(string Repo, string Home, string Identity)> IdentityRepoAsync(string root)
    {
        var home = Path.Combine(root, "home");
        var repo = Path.Combine(root, "repo");
        var identity = Path.Combine(root, "gitconfig");
        Directory.CreateDirectory(home);
        Directory.CreateDirectory(repo);
        await File.WriteAllTextAsync(identity, UsableUser);
        await SetupCommitAsync(repo, home, "init", "-q", "-b", "master");
        await SetupCommitAsync(repo, home, "commit", "-q", "--allow-empty", "-m", "base");
        return (repo, home, identity);
    }

    private static async Task<string> HeadIdentityAsync(string repo, string home)
    {
        var start = new ProcessStartInfo("git")
        {
            WorkingDirectory = repo, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
        };
        start.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
        start.Environment["HOME"] = home;
        start.Environment["GIT_CONFIG_GLOBAL"] = "/dev/null";
        foreach (var arg in new[] { "log", "-1", "--format=%an|%ae|%cn|%ce" })
            start.ArgumentList.Add(arg);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("git_start_failed");
        var stdout = await process.StandardOutput.ReadToEndAsync();
        var stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        process.ExitCode.ShouldBe(0, stderr);
        return stdout.Trim();
    }

    [Test]
    public async Task Task_progress_and_github_push_use_the_configured_credential_probe()
    {
        var root = NewRoot();
        try
        {
            var settings = Options.Create(new GitSettings
            {
                PushCredentialTokenFile = Path.Combine(root, "missing-token"),
                PushCredentialPolicyPath = Path.Combine(root, "missing-policy"),
            });
            var progress = new CountingProgress(settings);
            var listed = await progress.RunAsync(root, ["ls-remote", "https://github.com/example/antiphon.git"], CancellationToken.None);
            listed.Diagnostic.ShouldBe(ServerPushCredential.AbsentReason);
            progress.Starts.ShouldBe(0);

            using var http = new HttpClient();
            var github = new GitHubService(
                http,
                Options.Create(new GithubSettings { BaseUrl = "https://api.github.test", PersonalAccessToken = "token-marker" }),
                NullLogger<GitHubService>.Instance,
                git: settings);
            var pushed = await Should.ThrowAsync<InvalidOperationException>(
                () => github.PushBranchAsync(root, "topic", CancellationToken.None));
            pushed.Message.ShouldBe(ServerPushCredential.AbsentReason);
        }
        finally { DeleteRoot(root); }
    }

    [Test]
    public void Mirror_script_defaults_to_whatif_and_refuses_a_missing_credential()
    {
        var path = Path.Combine(DockerStackDocuments.RepoRoot, ScriptPath.Replace('/', Path.DirectorySeparatorChar));
        File.Exists(path).ShouldBeTrue(path);
        var text = File.ReadAllText(path);
        text.ShouldContain("Default is -WhatIf");
        text.ShouldContain("credential-absent");
        text.ShouldContain("remote-unrecorded");
        text.ShouldContain("ambiguous-mode");
        text.ShouldContain("volume-forbidden");
        text.ShouldContain("secret-in-table");
        text.ShouldContain("path-forbidden");
        text.ShouldContain("/work/repos/");
        text.ShouldContain("git clone --");
        text.ShouldContain("GIT_TERMINAL_PROMPT");
        text.ShouldContain("rollback restores the before column");
        text.Contains("GH_TOKEN", StringComparison.Ordinal).ShouldBeFalse();
        text.Contains("GITHUB_TOKEN", StringComparison.Ordinal).ShouldBeFalse();
        text.All(character => character <= 127).ShouldBeTrue();
    }

    [Test]
    public async Task Absent_credential_refuses_land_push_before_git_starts()
    {
        var root = NewRoot();
        try
        {
            var token = Path.Combine(root, "token");
            var policy = Path.Combine(root, "allow");
            File.WriteAllText(token, "CANARY-TOKEN-VALUE-1776");
            var git = new StartCountingGit(Options.Create(new GitSettings
            {
                PushCredentialTokenFile = token,
                PushCredentialPolicyPath = policy,
            }));
            var started = 0;
            var result = await git.PushOwnedAsync(Path.Combine(root, "missing-repo"), Destination(), Sha(),
                (_, _, _) =>
                {
                    started++;
                    return Task.CompletedTask;
                }, CancellationToken.None);
            result.Succeeded.ShouldBeFalse();
            result.Diagnostic.ShouldBe(ServerPushCredential.AbsentReason);
            result.Output.ShouldBeEmpty();
            result.Diagnostic.ShouldNotContain("CANARY");
            started.ShouldBe(0);
            git.Starts.ShouldBe(0);
        }
        finally { DeleteRoot(root); }
    }

    [Test]
    public async Task Present_credential_does_not_refuse_and_unconfigured_desktop_does_not()
    {
        var root = NewRoot();
        try
        {
            var token = Path.Combine(root, "token");
            var policy = Path.Combine(root, "allow");
            await File.WriteAllTextAsync(token, "CANARY-TOKEN-VALUE-1776\n");
            await File.WriteAllTextAsync(policy, "https://github.com/example/antiphon.git\n");
            var armed = new StartCountingGit(Options.Create(new GitSettings
            {
                PushCredentialTokenFile = token,
                PushCredentialPolicyPath = policy,
            }));
            var armedResult = await CaptureAsync(armed);
            armedResult.Diagnostic.ShouldNotBe(ServerPushCredential.AbsentReason);
            armedResult.Output.ShouldNotContain("CANARY-TOKEN-VALUE-1776");

            var desktop = new StartCountingGit(Options.Create(new GitSettings()));
            var desktopResult = await CaptureAsync(desktop);
            desktopResult.Diagnostic.ShouldNotBe(ServerPushCredential.AbsentReason);
        }
        finally { DeleteRoot(root); }
    }

    [Test]
    public void Probe_fails_closed_on_empty_directory_symlink_and_relative_paths()
    {
        var root = NewRoot();
        try
        {
            var token = Path.Combine(root, "token");
            var policy = Path.Combine(root, "allow");
            var directory = Path.Combine(root, "dir");
            var link = Path.Combine(root, "link");
            File.WriteAllText(token, "CANARY-TOKEN-VALUE-1776");
            Directory.CreateDirectory(directory);
            File.CreateSymbolicLink(link, token);
            ServerPushCredential.Refusal(token, policy).ShouldBe(ServerPushCredential.AbsentReason);
            var empty = Path.Combine(root, "empty");
            File.WriteAllText(empty, "");
            File.WriteAllText(policy, "https://github.com/example/antiphon.git\n");
            ServerPushCredential.Refusal(empty, policy).ShouldBe(ServerPushCredential.AbsentReason);
            ServerPushCredential.Refusal(token, policy).ShouldBeNull();
            ServerPushCredential.Refusal("", "").ShouldBeNull();
            ServerPushCredential.Refusal(directory, policy).ShouldBe(ServerPushCredential.AbsentReason);
            ServerPushCredential.Refusal(link, policy).ShouldBe(ServerPushCredential.AbsentReason);
            ServerPushCredential.Refusal("relative-token", policy).ShouldBe(ServerPushCredential.AbsentReason);
            ServerPushCredential.Refusal(token, "relative-policy").ShouldBe(ServerPushCredential.AbsentReason);
        }
        finally { DeleteRoot(root); }
    }

    [Test]
    public async Task Direct_remote_command_refuses_without_starting_git()
    {
        var root = NewRoot();
        try
        {
            var git = new StartCountingGit(Options.Create(new GitSettings
            {
                PushCredentialTokenFile = Path.Combine(root, "missing-token"),
                PushCredentialPolicyPath = Path.Combine(root, "missing-policy"),
            }));
            var result = await git.RunAsync(root, ["ls-remote", "https://github.com/example/antiphon.git"], CancellationToken.None);
            result.Diagnostic.ShouldBe(ServerPushCredential.AbsentReason);
            git.Starts.ShouldBe(0);
            ServerPushCredential.IsRemoteCommand(["status"]).ShouldBeFalse();
            ServerPushCredential.IsRemoteCommand(["push", "origin", "master"]).ShouldBeTrue();
        }
        finally { DeleteRoot(root); }
    }

    private static async Task<LandingGitResult> CaptureAsync(StartCountingGit git)
    {
        try
        {
            return await git.PushOwnedAsync(Path.Combine(Path.GetTempPath(), "card-1776-absent-repo"), Destination(), Sha(),
                (_, _, _) => Task.CompletedTask, CancellationToken.None);
        }
        catch (InvalidOperationException ex) when (ex.Message == "git_must_not_start")
        {
            return new LandingGitResult(1, "", "git_started");
        }
    }

    private static async Task SetupCommitAsync(string repo, string home, params string[] args)
    {
        var (exit, error) = await RunGitAsync(repo, home, new Dictionary<string, string?>
        {
            ["GIT_AUTHOR_NAME"] = "Card 1776 Setup",
            ["GIT_AUTHOR_EMAIL"] = "card-1776-setup@example.invalid",
            ["GIT_COMMITTER_NAME"] = "Card 1776 Setup",
            ["GIT_COMMITTER_EMAIL"] = "card-1776-setup@example.invalid",
            ["GIT_CONFIG_GLOBAL"] = "/dev/null",
        }, args);
        exit.ShouldBe(0, error);
    }

    private static Task<(int Exit, string Error)> RebaseAsync(string repo, string home, string? identityFile)
    {
        var extra = new Dictionary<string, string?>();
        if (identityFile is not null)
            extra["GIT_CONFIG_GLOBAL"] = identityFile;
        return RunGitAsync(repo, home, extra, "-c", "rebase.autoStash=false", "-c", "rebase.updateRefs=false", "rebase", "master");
    }

    private static async Task<(int Exit, string Error)> RunGitAsync(
        string repo, string home, IReadOnlyDictionary<string, string?> extra, params string[] args)
    {
        var start = new ProcessStartInfo("git")
        {
            WorkingDirectory = repo,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        start.Environment["GIT_TERMINAL_PROMPT"] = "0";
        start.Environment["GIT_OPTIONAL_LOCKS"] = "0";
        start.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
        start.Environment["HOME"] = home;
        foreach (var name in new[]
        {
            "GIT_AUTHOR_NAME", "GIT_AUTHOR_EMAIL", "GIT_COMMITTER_NAME", "GIT_COMMITTER_EMAIL", "GIT_CONFIG_GLOBAL",
        })
            start.Environment.Remove(name);
        foreach (var pair in extra)
        {
            if (pair.Value is null)
                start.Environment.Remove(pair.Key);
            else
                start.Environment[pair.Key] = pair.Value;
        }
        foreach (var arg in args)
            start.ArgumentList.Add(arg);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("git_start_failed");
        var stdout = await process.StandardOutput.ReadToEndAsync();
        var stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return (process.ExitCode, stderr + stdout);
    }

    private static string NewRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "card-1776-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void DeleteRoot(string root)
    {
        if (Directory.Exists(root))
            Directory.Delete(root, recursive: true);
    }

    private static string Sha() => new('a', 40);

    private static LandingDestination Destination() =>
        new("origin", "refs/heads/master", new string('b', 64));

    private sealed class StartCountingGit : LandingGit
    {
        public int Starts;
        public StartCountingGit(IOptions<GitSettings>? git) : base(null, git) { }

        protected override Process? StartProcess(ProcessStartInfo start)
        {
            Starts++;
            throw new InvalidOperationException("git_must_not_start");
        }
    }

    private sealed class IdentityFreeGit : LandingGit
    {
        private readonly string _home;
        public int Starts;
        public IdentityFreeGit(string home, IOptions<GitSettings>? git) : base(null, git) => _home = home;

        protected override void ConfigureProcess(ProcessStartInfo start)
        {
            start.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
            start.Environment["HOME"] = _home;
            foreach (var name in new[]
            {
                "GIT_AUTHOR_NAME", "GIT_AUTHOR_EMAIL", "GIT_COMMITTER_NAME", "GIT_COMMITTER_EMAIL", "GIT_CONFIG_GLOBAL",
            })
                start.Environment.Remove(name);
        }

        protected override Process? StartProcess(ProcessStartInfo start)
        {
            Starts++;
            return base.StartProcess(start);
        }
    }

    /// <summary>CARD-1787. Plants the git identity and config variables a server process could inherit
    /// (the same names the server's own environment would hand the child) and records the environment
    /// the child actually starts with.</summary>
    private sealed class InheritingGit : LandingGit
    {
        public static readonly IReadOnlyDictionary<string, string> Planted = new Dictionary<string, string>
        {
            ["GIT_AUTHOR_NAME"] = "Inherited Author",
            ["GIT_AUTHOR_EMAIL"] = "inherited-author@example.invalid",
            ["GIT_AUTHOR_DATE"] = "2001-02-03T04:05:06Z",
            ["GIT_COMMITTER_NAME"] = "Inherited Committer",
            ["GIT_COMMITTER_EMAIL"] = "inherited-committer@example.invalid",
            ["GIT_COMMITTER_DATE"] = "2001-02-03T04:05:06Z",
            ["GIT_CONFIG_PARAMETERS"] = "'user.email'='inherited-parameters@example.invalid'",
            ["GIT_CONFIG_COUNT"] = "1",
            ["GIT_CONFIG_KEY_0"] = "user.name",
            ["GIT_CONFIG_VALUE_0"] = "Inherited Count",
            // Left behind past the count: still scrubbed, so no renumbered entry can pick it up.
            ["GIT_CONFIG_KEY_7"] = "credential.helper",
            ["GIT_CONFIG_VALUE_7"] = "/tmp/card-1787-steal",
        };

        private readonly string _home;
        public int Starts;
        public IReadOnlyDictionary<string, string?>? Environment;
        public InheritingGit(string home, IOptions<GitSettings>? git) : base(null, git) => _home = home;

        protected override void ConfigureProcess(ProcessStartInfo start)
        {
            start.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
            start.Environment["HOME"] = _home;
            start.Environment.Remove("GIT_CONFIG_GLOBAL");
            foreach (var pair in Planted)
                start.Environment[pair.Key] = pair.Value;
        }

        protected override Process? StartProcess(ProcessStartInfo start)
        {
            if (start.ArgumentList.Contains("commit"))
            {
                Starts++;
                Environment = new Dictionary<string, string?>(start.Environment, StringComparer.Ordinal);
            }
            return base.StartProcess(start);
        }
    }

    private sealed class CountingProgress : TaskProgressGit
    {
        public int Starts;
        public CountingProgress(IOptions<GitSettings> git) : base(null, git) { }

        protected override Process? StartProcess(ProcessStartInfo start)
        {
            Starts++;
            throw new InvalidOperationException("git_must_not_start");
        }
    }
}
