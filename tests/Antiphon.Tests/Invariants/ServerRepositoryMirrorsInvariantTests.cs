using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using System.Runtime.InteropServices;
using Antiphon.Nightly;
using Antiphon.Tests.Infrastructure;
using Antiphon.Tests.Scripts.Nightly;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;
using Antiphon.Tests.Scripts;

namespace Antiphon.Tests.Invariants;

[Category("Unit")]
[Category("RepositoryInvariant")]
public sealed class ServerRepositoryMirrorsInvariantTests
{
    [Test]
    public void Retired_repository_mirrors_script_is_absent()
    {
        File.Exists(Path.Combine(DockerStackDocuments.RepoRoot, "scripts", "server2-repository-mirrors.ps1")).ShouldBeFalse();
    }
}
