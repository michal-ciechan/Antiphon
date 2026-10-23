using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.TestHelpers;

[Category("Integration")]
[Category("Slow")]
[NotInParallel("C1514Choke")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class TestTimingChokePointTests
{
    [Test]
    [Timeout(300_000)]
    public async Task C1514T_DbHostGit(CancellationToken cancellationToken)
    {
        var root = C1514Support.FreshRoot();
        using var session = C1514Support.Open(root);
        var testId = TestContext.Current!.Metadata.TestDetails.TestId;
        using (TestTiming.Install(session))
        {
            session.BeginCase(testId, "TestTimingChokePointTests", "C1514T_DbHostGit", [], session.NowTicks());
            // The assembly hook already started the shared store. Own a cold lifecycle so
            // start and migrate run after this session is installed.
            var lifecycle = new TestDbFixtureLifecycle();
            try
            {
                await using (var schema = await lifecycle.CreateIsolatedSchemaAsync())
                {
                    schema.ShouldNotBeNull();
                }

                await using (var host = new AntiphonWebAppFactory())
                {
                    using var client = host.CreateClient();
                    using var response = await client.GetAsync("/health", cancellationToken);
                    response.IsSuccessStatusCode.ShouldBeTrue();
                    (await response.Content.ReadAsStringAsync(cancellationToken)).ShouldBe("Healthy");
                }

                await using var git = new LandingGitFixture();
                var scratch = Path.Combine(git.Root, "scratch");
                Directory.CreateDirectory(scratch);
                var init = await git.Git.RunAsync(scratch, ["init", "-b", "master"], cancellationToken);
                init.Succeeded.ShouldBeTrue(init.Diagnostic);
                var rev = await git.Git.RunAsync(scratch, ["rev-parse", "--is-inside-work-tree"], cancellationToken);
                rev.Succeeded.ShouldBeTrue(rev.Diagnostic);
                session.EndCase(testId, "Passed", session.NowTicks());
            }
            catch
            {
                session.EndCase(testId, "Failed", session.NowTicks());
                throw;
            }
            finally
            {
                await lifecycle.DisposeAsync();
            }
        }

        session.Dispose();
        var bytes = File.ReadAllText(session.ShardPath!);
        bytes.ShouldNotContain("Password=", Case.Sensitive);
        bytes.ShouldNotContain("rev-parse", Case.Sensitive);
        bytes.ShouldNotContain("db-snapshot", Case.Sensitive);
        bytes.ShouldNotContain("db-restore", Case.Sensitive);
        foreach (var token in new[]
                 {
                     "db-create", "db-migrate", "db-cleanup", "host-startup", "host-shutdown",
                     "\"operation\":\"queue\"", "\"operation\":\"clone\"", "\"operation\":\"drop\"",
                     "\"operation\":\"spawn\"", "\"operation\":\"wait\"",
                 })
            bytes.ShouldContain(token, Case.Sensitive);
    }
}
