using Antiphon.SessionRunner;
using Antiphon.SessionRunner.Contracts;
using Shouldly;
using TUnit.Core;
using Antiphon.TestSupport;

namespace Antiphon.SessionRunner.Tests;

public sealed partial class CompletedCardWorkspaceRemovalTests
{
    [NativeLinuxOnly("Unix executable-bit witness requires Unix filesystem modes")]
    [Test]
    [Arguments("add-executable")]
    [Arguments("remove-executable")]
    public async Task C1032_InspectionR3Mode(string variant)
    {
        if (OperatingSystem.IsWindows()) Skip.Test("Unix executable-bit witness requires Unix filesystem modes");
        await using var f = new CompletedCardRunnerFixture();
        var path = Path.Combine(f.Checkout, "source");
        if (variant == "remove-executable")
        {
            CompletedCardRunnerFixture.Git(f.Checkout, "update-index", "--chmod=+x", "source");
            CompletedCardRunnerFixture.Git(f.Checkout, "-c", "user.name=c1032", "-c", "user.email=c1032@localhost", "commit", "-m", "executable source");
        }
        CompletedCardRunnerFixture.Git(f.Checkout, "config", "core.fileMode", "false");
        File.SetUnixFileMode(path, variant == "add-executable"
            ? UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
            : UnixFileMode.UserRead | UnixFileMode.UserWrite);
        CompletedCardRunnerFixture.Git(f.Checkout, "status", "--porcelain").ShouldBeEmpty("hidden mode fixture");
        var before = InspectionDigest(f.Root);
        var result = await new RunnerCompletedCardWorkspaceRemoval(f.Workspace(false, "main")).InspectAsync(new(1, f.Binding), CancellationToken.None);
        result.Reason.ShouldBe("dirty_source", variant);
        result.Eligible.ShouldBeFalse();
        InspectionDigest(f.Root).ShouldBe(before);
        Directory.Exists(f.State).ShouldBeFalse();
    }

    [Test]
    [Arguments("missing-head")]
    [Arguments("missing-objects")]
    [Arguments("missing-refs")]
    [Arguments("directory-head")]
    [Arguments("case-head")]
    [Arguments("case-objects")]
    [Arguments("case-refs")]
    public async Task C1032_InspectionR3Bare(string variant)
    {
        await using var f = new CompletedCardRunnerFixture();
        File.AppendAllText(Path.Combine(f.Binding.CommonDirectory, "info", "exclude"), "\nbin/\n");
        var nested = Path.Combine(f.Checkout, "bin", "damaged.git");
        Directory.CreateDirectory(nested);
        CompletedCardRunnerFixture.Git(nested, "init", "--bare");
        var part = variant.EndsWith("head") ? "HEAD" : variant.EndsWith("objects") ? "objects" : "refs";
        var path = Path.Combine(nested, part);
        if (variant.StartsWith("case-"))
        {
            var temporary = path + "-rename";
            if (part == "HEAD") { File.Move(path, temporary); File.Move(temporary, Path.Combine(nested, "head")); }
            else { Directory.Move(path, temporary); Directory.Move(temporary, Path.Combine(nested, part.ToUpperInvariant())); }
        }
        else
        {
            if (part == "HEAD") File.Delete(path); else Directory.Delete(path, true);
            if (variant == "directory-head") Directory.CreateDirectory(path);
        }
        var before = InspectionDigest(f.Root);
        var result = await new RunnerCompletedCardWorkspaceRemoval(f.Workspace(false, "main")).InspectAsync(new(1, f.Binding), CancellationToken.None);
        result.Reason.ShouldBe("nested_repository", variant);
        result.Eligible.ShouldBeFalse();
        InspectionDigest(f.Root).ShouldBe(before);
        Directory.Exists(f.State).ShouldBeFalse();
    }

    [Test]
    [Arguments("replace")]
    [Arguments("custom-replace")]
    [Arguments("grafts")]
    public async Task C1032_InspectionR3History(string variant)
    {
        await using var f = new CompletedCardRunnerFixture();
        CompletedCardRunnerFixture.Git(f.Checkout, "-c", "user.name=c1032", "-c", "user.email=c1032@localhost", "commit", "--allow-empty", "-m", "unpublished");
        var actual = CompletedCardRunnerFixture.Git(f.Checkout, "rev-parse", "HEAD").Trim();
        var tree = CompletedCardRunnerFixture.Git(f.Checkout, "rev-parse", "HEAD^{tree}").Trim();
        var forged = CompletedCardRunnerFixture.Git(f.Checkout, "-c", "user.name=c1032", "-c", "user.email=c1032@localhost", "commit-tree", tree, "-p", actual, "-m", "forged ancestry").Trim();
        var graft = Path.Combine(f.Binding.CommonDirectory, "info", "grafts");
        if (variant == "grafts") File.WriteAllText(graft, f.Sha + " " + actual + "\n");
        else CompletedCardRunnerFixture.Git(f.Checkout, "update-ref", (variant == "replace" ? "refs/replace/" : "refs/inspection-replace/") + f.Sha, forged);
        var service = f.Workspace(false, "main");
        if (variant == "custom-replace") f.BeforeGitStart = psi => psi.Environment["GIT_REPLACE_REF_BASE"] = "refs/inspection-replace/";
        var before = InspectionDigest(f.Root);
        var proof = f.Command().Proof with { ExpectedHeadSha = actual, PreservedSourceSha = actual, DestinationFingerprint = f.Binding.RepositoryIdentity };
        (await new RunnerCompletedCardWorkspaceRemoval(service).CheckProofAsync(f.Binding, proof, CancellationToken.None)).ShouldBe("source_not_preserved", variant);
        InspectionDigest(f.Root).ShouldBe(before);
        Directory.Exists(f.State).ShouldBeFalse();
    }

    [Test]
    [Arguments("changed")]
    [Arguments("unchanged")]
    public async Task C1032_InspectionR3Filter(string variant)
    {
        await using var f = new CompletedCardRunnerFixture();
        File.WriteAllText(Path.Combine(f.Checkout, ".gitattributes"), "source filter=guard diff=guard\n");
        CompletedCardRunnerFixture.Git(f.Checkout, "add", ".gitattributes");
        CompletedCardRunnerFixture.Git(f.Checkout, "-c", "user.name=c1032", "-c", "user.email=c1032@localhost", "commit", "-m", "attributes");
        var marker = Path.Combine(f.Root, "filter-wrote");
        var script = Path.Combine(f.Root, "filter.sh");
        File.WriteAllText(script, "#!/bin/sh\ncat >/dev/null\nprintf invoked > '" + marker.Replace("'", "'\\''") + "'\nprintf source\n");
        var command = "sh '" + script.Replace("'", "'\\''") + "'";
        foreach (var key in new[] { "filter.guard.clean", "filter.guard.smudge", "diff.guard.textconv", "diff.external", "core.fsmonitor" })
            CompletedCardRunnerFixture.Git(f.Checkout, "config", key, command);
        if (variant == "changed") File.WriteAllText(Path.Combine(f.Checkout, "source"), "hidden changed source");
        File.SetLastWriteTimeUtc(Path.Combine(f.Checkout, "source"), DateTime.UtcNow.AddDays(-1));
        var before = InspectionDigest(f.Root);
        var result = await new RunnerCompletedCardWorkspaceRemoval(f.Workspace(false, "main")).InspectAsync(new(1, f.Binding), CancellationToken.None);
        File.Exists(marker).ShouldBeFalse("inspection must never execute configured filter/fsmonitor/textconv commands");
        result.Reason.ShouldBe(variant == "changed" ? "dirty_source" : null, variant);
        result.Eligible.ShouldBe(variant == "unchanged");
        InspectionDigest(f.Root).ShouldBe(before);
        if (variant == "unchanged")
        {
            // A repository-configured foreign remote helper must not participate in proof reads.
            CompletedCardRunnerFixture.Git(f.Checkout, "config", "remote.origin.vcs", "inspection-forbidden-helper");
            var proofBefore = InspectionDigest(f.Root);
            var actual = CompletedCardRunnerFixture.Git(f.Checkout, "rev-parse", "HEAD").Trim();
            var proof = f.Command().Proof with { ExpectedHeadSha = actual, PreservedSourceSha = actual,
                DestinationFingerprint = f.Binding.RepositoryIdentity };
            (await new RunnerCompletedCardWorkspaceRemoval(f.Workspace(false, "main")).CheckProofAsync(f.Binding, proof,
                CancellationToken.None)).ShouldBe("source_not_preserved", "configured remote helper bypassed; actual unpublished source refused");
            File.Exists(marker).ShouldBeFalse();
            InspectionDigest(f.Root).ShouldBe(proofBefore);
        }
        Directory.Exists(f.State).ShouldBeFalse();
    }
}
