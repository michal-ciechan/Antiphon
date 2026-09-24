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

    // Review 9a0c7fb8: a protected name wins over a disposable directory rule anywhere in the tree.
    // The disposable sibling proves the directory rule itself still applies, so the refusal comes
    // from the name and the whole directory is no longer wholly disposable.
    [Test]
    [Arguments("server/bin-c665/appsettings.Development.json")]
    [Arguments("server/bin/Debug/net10.0/appsettings.Production.json")]
    [Arguments("client/node_modules/pkg/.claude/settings.local.json")]
    [Arguments("tests/Antiphon.Tests/obj/Antiphon.Tests.csproj.user")]
    [Arguments("scripts/bin-x/nightly-watchdog.local.json")]
    [Arguments("logs/bin/a.log")]
    [Arguments(".antiphon/deliverables/ab12cd34/bin/x.dll")]
    public void Protected_name_inside_disposable_directory_is_protected(string path)
    {
        var result = Defaults().Classify([path, "server/bin-c665/a.dll"]);
        Same(result.Protected, path);
        Same(result.Disposable, "server/bin-c665/a.dll");
        result.Evidence.ShouldBeEmpty();
    }

    // Review 5b79328d item 2: a `.antiphon` segment anywhere is protected unless one of the explicit
    // `.antiphon` patterns names it, so a nested spill tree under a build directory is not disposable
    // (and nested report spills are not evidence) just because its ancestor is.
    [Test]
    [Arguments("server/bin-x/.antiphon/other.json")]
    [Arguments("server/bin-x/.antiphon/inbox/1e3f.md")]
    [Arguments("server/bin-x/.antiphon/task-ab12cd34-brief.md")]
    [Arguments("server/bin-x/.antiphon/task-ab12cd34.md")]
    [Arguments("client/node_modules/pkg/.antiphon/c665-checkpoints/run.trx")]
    [Arguments("server/obj/.Antiphon/x")]
    public void Nested_antiphon_segment_is_protected(string path)
    {
        var result = Defaults().Classify([path, "server/bin-x/a.dll"]);
        Same(result.Protected, path);
        Same(result.Disposable, "server/bin-x/a.dll");
        result.Evidence.ShouldBeEmpty();
    }

    // The explicit root patterns keep their buckets, and a name that only starts with `.antiphon`
    // is not a `.antiphon` segment.
    [Test]
    [Arguments(".antiphon/inbox/1e3f.md", "disposable")]
    [Arguments(".antiphon/task-ab12cd34-brief.md", "disposable")]
    [Arguments(".antiphon-cache/x.bin", "disposable")]
    [Arguments("server/bin-x/.antiphon-cache/x.bin", "disposable")]
    [Arguments(".antiphon/task-ab12cd34.md", "evidence")]
    [Arguments(".antiphon/results/unit.trx", "evidence")]
    public void Explicit_antiphon_patterns_keep_their_bucket(string path, string bucket)
    {
        var result = Defaults().Classify([path]);
        Same(bucket == "evidence" ? result.Evidence : result.Disposable, path);
        (bucket == "evidence" ? result.Disposable : result.Evidence).ShouldBeEmpty();
        result.Protected.ShouldBeEmpty();
    }

    // Only a pattern that itself names a `.antiphon` segment grants a nested one.
    [Test]
    public void Configured_antiphon_pattern_is_explicit()
    {
        var classifier = new WorktreeIgnoredContentClassifier(Options.Create(new WorktreeCleanupSettings
        {
            DisposableIgnored = ["**/bin-*/**", "**/bin-*/.antiphon/scratch/**"],
        }));
        var result = classifier.Classify(["bin-x/.antiphon/scratch/a.txt", "bin-x/.antiphon/other.txt", "bin-x/a.dll"]);
        Same(result.Disposable, "bin-x/.antiphon/scratch/a.txt", "bin-x/a.dll");
        Same(result.Protected, "bin-x/.antiphon/other.txt");
    }

    [Test]
    public void Protected_precedes_evidence_on_overlap()
    {
        // Matches .antiphon/**/*.trx (evidence) and .antiphon/deliverables/** (protected).
        var both = ".antiphon/deliverables/ab12cd34/run.trx";
        var result = Defaults().Classify([both, ".antiphon/results/unit.trx"]);
        Same(result.Protected, both);
        Same(result.Evidence, ".antiphon/results/unit.trx");
        result.Disposable.ShouldBeEmpty();
    }

    [Test]
    public void Configured_protected_names_extend_but_never_replace_the_defaults()
    {
        var classifier = new WorktreeIgnoredContentClassifier(Options.Create(new WorktreeCleanupSettings
        {
            DisposableIgnored = ["**/bin-*/**"],
            ProtectedIgnored = ["**/keep.me"],
        }));
        var result = classifier.Classify(["bin-x/keep.me", "bin-x/appsettings.Development.json", "bin-x/a.dll"]);
        Same(result.Protected, "bin-x/appsettings.Development.json", "bin-x/keep.me");
        Same(result.Disposable, "bin-x/a.dll");

        var emptied = new WorktreeIgnoredContentClassifier(Options.Create(new WorktreeCleanupSettings
        {
            DisposableIgnored = ["**/bin-*/**"],
            ProtectedIgnored = [],
        })).Classify(["bin-x/.claude/settings.local.json", "bin-x/a.dll"]);
        Same(emptied.Protected, "bin-x/.claude/settings.local.json");
        Same(emptied.Disposable, "bin-x/a.dll");
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
