using System.Diagnostics;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;
using TUnit.Core.Exceptions;

namespace Antiphon.Tests.Infrastructure;

[Category("Unit")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class GithubCredentialHelperTests
{
    [Test]
    public async Task Helper_answers_an_admitted_github_path_with_the_mounted_token()
    {
        using var fixture = new HelperFixture();
        await fixture.AssertAnswer("get", "github.com", "michal-ciechan/markdown-package.git", "password=inert-c0817-token\n");
        // A directory bind sees an atomically replaced token on the next invocation.
        var replacement = fixture.TokenPath + ".new";
        File.WriteAllText(replacement, "inert-c0817-rotated\n");
        File.Move(replacement, fixture.TokenPath, overwrite: true);
        await fixture.AssertAnswer("get", "github.com", "michal-ciechan/markdown-package.git", "password=inert-c0817-rotated\n");
    }

    [Test]
    public async Task Helper_answers_nothing_for_another_host()
    {
        using var fixture = new HelperFixture();
        await fixture.AssertAnswer("get", "gitlab.com", "michal-ciechan/markdown-package.git", "");
        await fixture.AssertAnswer("get", "github.com.evil.invalid", "michal-ciechan/markdown-package.git", "");
        await fixture.AssertAnswer("get", "github.com", "michal-ciechan/markdown-package.git", "", protocol: "http");
    }

    [Test]
    public async Task Helper_answers_nothing_for_a_path_outside_the_allow_list()
    {
        using var fixture = new HelperFixture();
        foreach (var path in new[] { "some-org/repo.git", "michal-ciechan-fork/repo.git", "michal-ciechan/../some-org/repo.git", "michal-ciechan//repo.git", "michal-ciechan" })
            await fixture.AssertAnswer("get", "github.com", path, "");
        await fixture.AssertAnswer("get", "github.com", "Michal-Ciechan/Repo", "password=inert-c0817-token\n");
        File.WriteAllText(fixture.PolicyPath, "https://github.com/michal-ciechan/one.git\n");
        await fixture.AssertAnswer("get", "github.com", "michal-ciechan/one", "password=inert-c0817-token\n");
        await fixture.AssertAnswer("get", "github.com", "michal-ciechan/one.git-extra", "");
        await fixture.AssertAnswer("get", "github.com", "michal-ciechan/two.git", "");
    }

    [Test]
    public async Task Helper_answers_nothing_when_the_policy_file_is_missing()
    {
        using var fixture = new HelperFixture();
        File.Delete(fixture.PolicyPath);
        await fixture.AssertAnswer("get", "github.com", "michal-ciechan/markdown-package.git", "");
    }

    [Test]
    public async Task Helper_answers_nothing_when_the_token_file_is_missing_or_empty()
    {
        using var fixture = new HelperFixture();
        File.Delete(fixture.TokenPath);
        await fixture.AssertAnswer("get", "github.com", "michal-ciechan/markdown-package.git", "");
        File.WriteAllText(fixture.TokenPath, " \t\r\n");
        await fixture.AssertAnswer("get", "github.com", "michal-ciechan/markdown-package.git", "");
    }

    [Test]
    public async Task Helper_ignores_store_and_erase_and_writes_nothing()
    {
        using var fixture = new HelperFixture();
        var files = Directory.GetFiles(fixture.Root).Order().ToArray();
        var token = File.ReadAllText(fixture.TokenPath);
        var policy = File.ReadAllText(fixture.PolicyPath);
        foreach (var verb in new[] { "store", "erase", "unknown" })
            await fixture.AssertAnswer(verb, "github.com", "michal-ciechan/markdown-package.git", "");
        await fixture.AssertAnswer("get", "github.com", "michal-ciechan/markdown-package.git", "password=inert-c0817-token\n");
        Directory.GetFiles(fixture.Root).Order().ShouldBe(files);
        File.ReadAllText(fixture.TokenPath).ShouldBe(token);
        File.ReadAllText(fixture.PolicyPath).ShouldBe(policy);
    }

    private sealed class HelperFixture : IDisposable
    {
        public string Root { get; }
        public string TokenPath => Path.Combine(Root, "token");
        public string PolicyPath => Path.Combine(Root, "policy");

        public HelperFixture()
        {
            if (!OperatingSystem.IsLinux()) throw new SkipTestException("The POSIX credential helper runs on Linux.");
            Root = Directory.CreateTempSubdirectory("c0817-helper-").FullName;
            File.WriteAllText(TokenPath, " \tinert-c0817-token\r\n");
            File.WriteAllText(PolicyPath, "https://github.com/michal-ciechan/antiphon.git\nhttps://github.com/michal-ciechan/\n");
        }

        public async Task AssertAnswer(string verb, string host, string path, string expected, string protocol = "https")
        {
            var start = new ProcessStartInfo("sh")
            {
                UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true,
                RedirectStandardError = true, WorkingDirectory = Root
            };
            start.ArgumentList.Add(Path.Combine(DockerStackDocuments.RepoRoot, "docker/session-runner-grok/github-credential.sh"));
            start.ArgumentList.Add(verb);
            start.Environment["ANTIPHON_GITHUB_TOKEN_FILE"] = TokenPath;
            start.Environment["ANTIPHON_PUSH_ALLOW_LIST"] = PolicyPath;
            using var process = Process.Start(start)!;
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            await process.StandardInput.WriteAsync($"protocol={protocol}\nhost={host}\npath={path}\nusername=x-access-token\npassword=inert-c0817-input\n\n");
            process.StandardInput.Close();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            try { await process.WaitForExitAsync(timeout.Token); }
            catch { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); throw; }
            process.ExitCode.ShouldBe(0);
            (await error).ShouldBeEmpty();
            (await output).ShouldBe(expected);
        }

        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}
