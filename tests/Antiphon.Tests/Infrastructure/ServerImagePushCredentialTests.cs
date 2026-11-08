using System.Diagnostics;
using System.Text.RegularExpressions;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Infrastructure.Git;
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

            var withIdentity = await RebaseAsync(repo, home, identity);
            withIdentity.Exit.ShouldBe(0);
            withIdentity.Error.ShouldContain("Successfully rebased");
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
}
