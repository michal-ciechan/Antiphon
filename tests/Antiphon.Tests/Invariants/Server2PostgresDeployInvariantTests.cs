using Antiphon.Nightly;
using Antiphon.Tests.Infrastructure;
using Shouldly;
using TUnit.Core;
using Antiphon.Tests.Scripts;

namespace Antiphon.Tests.Invariants;

[Category("Unit")]
[Category("RepositoryInvariant")]
public sealed class Server2PostgresDeployInvariantTests
{
    [Test]
    public void Retired_postgres_deploy_wrapper_is_absent()
    {
        File.Exists(Path.Combine(DockerStackDocuments.RepoRoot, "scripts", "deploy-server2-postgres.ps1")).ShouldBeFalse();
    }
}
