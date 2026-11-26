using System.IO.Abstractions;
using System.Text.Json;
using Antiphon.Nightly;
using Antiphon.Tests.Infrastructure;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;
using Antiphon.Tests.Scripts;

namespace Antiphon.Tests.Invariants;

[Category("Integration")]
[Category("RepositoryInvariant")]
public sealed class CheckpointTaskScriptInvariantTests
{
    [Test]
    public void Checkpoint_task_command_is_ascii()
    {
        File.ReadAllBytes(Path.Combine(DockerStackDocuments.RepoRoot, "scripts", "lib", "Antiphon.Nightly", "CheckpointTask.cs")).ShouldAllBe(b => b < 128);
    }
}
