using System.IO.Abstractions.TestingHelpers;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Infrastructure.Git;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.TestHelpers.FakeGit;

[Category("Unit")]
public class TestGitBackendTests
{
    [Test]
    [Arguments(null)]
    [Arguments("0")]
    public async Task UnsetAndZeroSelectFake(string? value)
    {
        await using var git = new TestGitBackendHost(() => value).Create();
        git.IsReal.ShouldBeFalse();
        git.Storage.ShouldBe("mock");
        git.Files.ShouldBeOfType<MockFileSystem>();
        git.GitLaunches.ShouldBe(0);
    }

    [Test]
    public async Task ExactOneSelectsIsolatedRealBackend()
    {
        await using var a = new TestGitBackendHost(() => "1").Create();
        await using var b = new TestGitBackendHost(() => "1").Create();
        a.IsReal.ShouldBeTrue(); b.IsReal.ShouldBeTrue();
        a.Storage.ShouldBe("physical");
        a.RepoPath.ShouldNotBe(b.RepoPath);
        a.HomePath.ShouldNotBe(b.HomePath);
        a.HomePath.ShouldNotBeNull();
        File.Exists(Path.Combine(a.HomePath, "config")).ShouldBeFalse();
        a.ConstructRealExecutor().ShouldBeAssignableTo<IGitCommandExecutor>();
    }

    [Test]
    [Arguments("")]
    [Arguments("true")]
    [Arguments("01")]
    [Arguments(" 1")]
    [Arguments("2")]
    public void InvalidSwitchValuesRefuse(string value) =>
        Should.Throw<ArgumentException>(() => new TestGitBackendHost(() => value));

    [Test]
    public async Task BackendChoiceIsFrozenForHost()
    {
        string? value = "0";
        var host = new TestGitBackendHost(() => value);
        value = "1";
        await using var oldChoice = host.Create();
        await using var newChoice = new TestGitBackendHost(() => value).Create();
        oldChoice.IsReal.ShouldBeFalse();
        newChoice.IsReal.ShouldBeTrue();
    }

    [Test]
    public async Task FixturesNeverShareRepositoryState()
    {
        var host = new TestGitBackendHost(() => "0");
        var a = host.Create();
        await using var b = host.Create();
        await a.InitializeAsync(); await b.InitializeAsync();
        await a.Files.File.WriteAllTextAsync(Path.Combine(a.RepoPath, "same.txt"), "A");
        await b.Files.File.WriteAllTextAsync(Path.Combine(b.RepoPath, "same.txt"), "B");
        await a.RequiredAsync("add", "same.txt"); await b.RequiredAsync("add", "same.txt");
        await a.RequiredAsync("commit", "-m", "A"); await b.RequiredAsync("commit", "-m", "B");
        (await a.RequiredAsync("show", "HEAD:same.txt")).ShouldBe("A");
        (await b.RequiredAsync("show", "HEAD:same.txt")).ShouldBe("B");
        await a.DisposeAsync();
        (await b.RequiredAsync("show", "HEAD:same.txt")).ShouldBe("B");
    }

    [Test]
    [Arguments("construction")]
    [Arguments("execution")]
    public async Task RealExecutorEscapeFailsFixture(string mode)
    {
        var git = new TestGitBackendHost(() => "0").Create();
        if (mode == "construction")
            Should.Throw<InvalidOperationException>(() => git.ConstructRealExecutor());
        else
            await Should.ThrowAsync<InvalidOperationException>(() => git.ExecuteRealAsync("status"));
        Should.Throw<InvalidOperationException>(() => git.AssertHealthy());
        await Should.ThrowAsync<InvalidOperationException>(async () => await git.DisposeAsync());
        git.GitLaunches.ShouldBe(0);
    }

    [Test]
    [Arguments("verb")]
    [Arguments("option")]
    [Arguments("quoted-command-string")]
    public async Task UnsupportedVectorPoisonsFixtureEvenWhenCaught(string mode)
    {
        var git = new TestGitBackendHost(() => "0").Create();
        await git.InitializeAsync();
        string[] vector = mode switch
        {
            "verb" => ["imaginary"],
            "option" => ["commit", "--unknown"],
            _ => ["checkout -b 'whole command'"]
        };
        await Should.ThrowAsync<NotSupportedException>(async () => await git.RunAsync(vector));
        Should.Throw<InvalidOperationException>(() => git.AssertHealthy());
        await Should.ThrowAsync<InvalidOperationException>(async () => await git.DisposeAsync());
        git.GitLaunches.ShouldBe(0);
    }

    [Test]
    public async Task ProductionCompositionAlwaysUsesCli()
    {
        // The production constructor has a real CLI default even when this process requests fake tests.
        var service = new GitService(Microsoft.Extensions.Logging.Abstractions.NullLogger<GitService>.Instance);
        var field = typeof(GitService).GetField("_executor", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        field.ShouldNotBeNull();
        field.GetValue(service).ShouldBeOfType<CliGitCommandExecutor>();
        await Task.CompletedTask;
    }
}
