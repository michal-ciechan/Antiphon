using System.Collections.Immutable;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

// CARD-0665 V-1. Inputs are worktree-relative, '/'-separated, exactly as `git ls-files -z` emits
// them on every OS. Each case asserts all three buckets so a path cannot land in two.
[Category("Unit")]
public sealed class WorktreeIgnoredContentClassifierTests
{
    [Test]
    [Arguments("server/obj/project.assets.json")]
    [Arguments("client/node_modules/x/y.js")]
    [Arguments("tests/Antiphon.Tests/bin-c665/a.dll")]
    [Arguments("server/bin/Debug/net10.0/Antiphon.Server.dll")]
    [Arguments("client/dist/assets/index.js")]
    [Arguments(".antiphon/inbox/1e3f.md")]
    [Arguments(".antiphon/task-ab12cd34-brief.md")]
    [Arguments(".antiphon/task-ab12cd34-refinement-20260924T0100.md")]
    public void Default_disposable_paths_are_disposable(string path)
    {
        var result = Defaults().Classify([path]);
        Same(result.Disposable, path);
        result.Evidence.ShouldBeEmpty();
        result.Protected.ShouldBeEmpty();
    }

    [Test]
    [Arguments(".antiphon/task-ab12cd34.md")]
    [Arguments(".antiphon/c665-checkpoints/CP-1-x/run.trx")]
    [Arguments(".antiphon/results/unit.trx")]
    public void Default_evidence_paths_are_evidence(string path)
    {
        var result = Defaults().Classify([path]);
        Same(result.Evidence, path);
        result.Disposable.ShouldBeEmpty();
        result.Protected.ShouldBeEmpty();
    }

    [Test]
    [Arguments(".claude/settings.local.json")]
    [Arguments("server/appsettings.Development.json")]
    [Arguments("x.user")]
    [Arguments(".antiphon/report.md")]
    [Arguments(".antiphon/inbox/photo.png")]
    [Arguments(".antiphon/deliverables/ab12cd34/x.md")]
    [Arguments("logs/a.log")]
    public void Default_protected_paths_are_protected(string path)
    {
        var result = Defaults().Classify([path]);
        Same(result.Protected, path);
        result.Disposable.ShouldBeEmpty();
        result.Evidence.ShouldBeEmpty();
    }

    [Test]
    public void Unknown_path_is_protected()
    {
        // Mixed with a disposable sibling so an all-protected classifier cannot pass.
        var result = Defaults().Classify(["docs/private-notes.txt", "server/obj/x.json"]);
        Same(result.Protected, "docs/private-notes.txt");
        Same(result.Disposable, "server/obj/x.json");
        result.Evidence.ShouldBeEmpty();
    }

    [Test]
    public void Evidence_precedes_disposable_on_overlap()
    {
        // Matches both **/bin/** (disposable) and .antiphon/*checkpoints*/** (evidence).
        var both = ".antiphon/c665-checkpoints/bin/run.trx";
        var result = Defaults().Classify([both, "bin/a.dll"]);
        Same(result.Evidence, both);
        Same(result.Disposable, "bin/a.dll");
        result.Protected.ShouldBeEmpty();
    }

    [Test]
    [Arguments("/abs/bin/a.dll")]
    [Arguments("C:/work/bin/a.dll")]
    [Arguments("server/../bin/a.dll")]
    public void Rooted_or_parent_segment_is_protected(string path)
    {
        var result = Defaults().Classify([path, "server/obj/y.json"]);
        Same(result.Protected, path);
        Same(result.Disposable, "server/obj/y.json");
        result.Evidence.ShouldBeEmpty();
    }

    [Test]
    public void Empty_lists_protect_everything()
    {
        var classifier = new WorktreeIgnoredContentClassifier(Options.Create(new WorktreeCleanupSettings
        {
            DisposableIgnored = [],
            RetainedIgnored = [],
        }));
        var result = classifier.Classify(["server/obj/x.json", ".antiphon/task-ab12cd34.md"]);
        Same(result.Protected, ".antiphon/task-ab12cd34.md", "server/obj/x.json");
        result.Disposable.ShouldBeEmpty();
        result.Evidence.ShouldBeEmpty();
        // The defaults are what makes the same inputs removable.
        Same(Defaults().Classify(["server/obj/x.json"]).Disposable, "server/obj/x.json");
    }

    private static void Same(ImmutableArray<string> actual, params string[] expected) =>
        actual.ToArray().ShouldBe(expected);

    private static WorktreeIgnoredContentClassifier Defaults() =>
        new(Options.Create(new WorktreeCleanupSettings()));
}
