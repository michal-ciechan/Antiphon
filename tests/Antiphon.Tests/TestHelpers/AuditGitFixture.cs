namespace Antiphon.Tests.TestHelpers;

// The path can only originate from this fixture's own temp-directory creation.
internal sealed class AuditGitFixture
{
    public string Root { get; } = Directory.CreateTempSubdirectory("antiphon-audit-").FullName;

    public static implicit operator string(AuditGitFixture fixture) => fixture.Root;

    public async Task RunAsync(Func<Task> body)
    {
        try { await body(); }
        finally { Directory.Delete(Root, recursive: true); }
    }
}
