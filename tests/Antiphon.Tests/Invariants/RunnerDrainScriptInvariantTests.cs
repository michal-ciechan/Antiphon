using System.Text.Json;
using Antiphon.Nightly;
using Antiphon.Tests.Infrastructure;
using Shouldly;
using TUnit.Core;
using Antiphon.Tests.Scripts;

namespace Antiphon.Tests.Invariants;

[Category("Integration")]
[Category("RepositoryInvariant")]
public sealed class RunnerDrainScriptInvariantTests
{
    [Test]
    public void Runner_drain_command_is_ascii()
    {
        File.ReadAllBytes(Path.Combine(DockerStackDocuments.RepoRoot, "scripts", "lib", "Antiphon.Nightly", "RunnerDrain.cs")).ShouldAllBe(b => b < 128);
    }
}
