using System.Diagnostics;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Infrastructure.Git;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Invariants;

[Category("Integration")]
[Category("RepositoryInvariant")]
public sealed class RepositoryMutationLeaseInvariantTests
{
    // Every journal reader shares Delete, so the owner's replace is never refused by the share mode.
    // Test readers of a live journal go through the one helper that also shares ReadWrite.
    [Test]
    public void C1076_JournalReadersShareDeleteWithTheReplacingOwner()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Antiphon.sln"))) dir = dir.Parent;
        dir.ShouldNotBeNull();
        var violations = new List<string>();
        foreach (var relative in new[] { "server/Infrastructure/Git/RepositoryChildJournal.cs", "server/Infrastructure/Git/RepositoryChildJournalInspector.cs" })
            foreach (var line in File.ReadAllLines(Path.Combine(dir.FullName, relative)))
                if (line.Contains("new FileStream(") && line.Contains("FileAccess.Read,") && !line.Contains("FileShare.Delete"))
                    violations.Add($"{relative}: {line.Trim()}");
        const string deserialize = "ChildRecord>(";
        const string unsharedRead = "File.ReadAllText";
        foreach (var relative in new[] { "tests/Antiphon.Tests/Infrastructure/LandingGitTests.cs", "tests/Antiphon.Tests/Infrastructure/RepositoryMutationLeaseTests.cs" })
            foreach (var line in File.ReadAllLines(Path.Combine(dir.FullName, relative)))
                if (line.Contains(deserialize) && line.Contains(unsharedRead))
                    violations.Add($"{relative}: {line.Trim()}");
        var helper = Path.Combine(dir.FullName, "tests/Antiphon.Tests/TestHelpers/RepositoryChildJournalReader.cs");
        if (!File.Exists(helper)) violations.Add("missing tests/Antiphon.Tests/TestHelpers/RepositoryChildJournalReader.cs");
        else if (!File.ReadAllText(helper).Contains("FileShare.ReadWrite | FileShare.Delete"))
            violations.Add("RepositoryChildJournalReader does not share ReadWrite | Delete");
        violations.ShouldBeEmpty();
    }
}
