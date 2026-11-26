using Antiphon.Tests.Application;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Invariants;

/// <summary>
/// CARD-0254 keeps the universal instruction file inside Codex's default project-document budget.
/// The file is a routing index, so every local owner it names must continue to exist.
/// </summary>
[Category("RepositoryInvariant")]
[Category("Unit")]
public class AgentContextContractTests
{
    private const int AgentContextByteLimit = 24_576;

    [Test]
    public void the_universal_agent_context_stays_within_the_raw_utf8_byte_budget()
    {
        var bytes = File.ReadAllBytes(Path.Combine(FindRepoRoot(), "AGENTS.md"));

        bytes.Length.ShouldBeLessThanOrEqualTo(
            AgentContextByteLimit,
            "AGENTS.md is loaded by every project-facing agent; measure raw UTF-8 bytes, not characters");
    }

    [Test]
    public void every_local_owner_document_named_by_the_routing_index_exists()
    {
        var root = FindRepoRoot();
        var core = File.ReadAllText(Path.Combine(root, "AGENTS.md"));
        var owners = System.Text.RegularExpressions.Regex.Matches(core, @"\]\((docs/[^)]+\.md)\)")
            .Select(match => match.Groups[1].Value.Replace('/', Path.DirectorySeparatorChar))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        owners.ShouldNotBeEmpty("the universal core must route a reader to living owner documents");
        var missing = owners
            .Where(owner => !File.Exists(Path.Combine(root, owner)))
            .ToArray();

        missing.ShouldBeEmpty("every document named by AGENTS.md's routing index must exist");
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Antiphon.sln")))
            directory = directory.Parent;

        return directory?.FullName
            ?? throw new DirectoryNotFoundException("Could not locate the Antiphon repository root.");
    }
}
