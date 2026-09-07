using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Unit")]
public class CardFilePrivacyDocumentationTests
{
    [Test]
    public void Owner_contracts_preserve_note_read_prohibition_and_backup_ignore()
    {
        var root = DelegateScriptRunner.RepoRoot;
        var ignore = File.ReadAllLines(Path.Combine(root, ".gitignore"));
        ignore.ShouldContain("backups/");
        var bundle = File.ReadAllText(Path.Combine(root, "server/Bundles/board-api.md")).Replace("\r", "").Replace("\n", " ");
        bundle.ShouldContain("agents must not read the notes route");
        bundle.ShouldContain("unless the card's own brief explicitly instructs");
        bundle.ShouldContain("must never quote note text into a report, card body, commit message, or chat message");
        bundle.ShouldContain("camelCase");
        bundle.ShouldContain("ConcurrencyToken");
        foreach (var owner in new[] { "orchestration-loop", "agent-card-lifecycle", "ops-http", "antiphon-api", "bootstrap" })
            File.ReadAllText(Path.Combine(root, "docs", owner + ".md")).ShouldContain("card-file-privacy.md");
        var guidance = File.ReadAllText(Path.Combine(root, "docs/card-file-privacy.md"));
        guidance.ShouldContain("/docs/cards/");
        guidance.ShouldContain("CARD-0409");
        guidance.ShouldContain("backups/");
    }
}
