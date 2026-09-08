using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Infrastructure.Git;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Infrastructure;

// Production inspection with deterministic Git replies. The companion service matrix
// exercises these decisions against real repositories and verifies retained remote refs.
[Category("Unit")]
public sealed class LandingIdentityControlTests
{
    [Test]
    [Arguments("common")]
    [Arguments("registration-head")]
    [Arguments("registration-path")]
    [Arguments("ambiguous")]
    [Arguments("registration-error")]
    [Arguments("branch-head")]
    [Arguments("ref-result-head")]
    [Arguments("registration-branch")]
    [Arguments("symbolic-branch")]
    [Arguments("locked")]
    [Arguments("prunable")]
    [Arguments("ref-error")]
    [Arguments("status-error")]
    [Arguments("ignored-error")]
    [Arguments("second-reading")]
    public async Task C448_V03_IndependentIdentityDecisionsRefuse(string change)
    {
        using var fixture = new InspectionFixture();
        (await fixture.Git.InspectAsync(fixture.Coordinates, default)).Accepted.ShouldBeTrue();
        fixture.Git.Change = change;
        fixture.Git.StatusRead = false;
        var result = await fixture.Git.InspectAsync(fixture.Coordinates, default);
        result.Accepted.ShouldBeFalse("the mismatched identity/query component must not authorize a source snapshot: " + change);
        File.ReadAllText(fixture.Sentinel).ShouldBe("private work");
    }

    [Test]
    [Arguments("M  keep.txt\0")]
    [Arguments(" M keep.txt\0")]
    [Arguments("?? new.txt\0")]
    [Arguments(" m nested\0")]
    [Arguments("?? nested/\0")]
    public async Task C448_V04_EachStatusClassRefuses(string status)
    {
        using var fixture = new InspectionFixture();
        (await fixture.Git.InspectAsync(fixture.Coordinates, default)).Accepted.ShouldBeTrue();
        fixture.Git.Status = status;
        var result = await fixture.Git.InspectAsync(fixture.Coordinates, default);
        result.Accepted.ShouldBeFalse("nonempty staged, unstaged, untracked or nested state cannot authorize removal");
        File.ReadAllText(fixture.Sentinel).ShouldBe("private work");
    }

    [Test]
    [Arguments("MERGE_HEAD")]
    [Arguments("CHERRY_PICK_HEAD")]
    [Arguments("REVERT_HEAD")]
    [Arguments("rebase-merge")]
    [Arguments("rebase-apply")]
    [Arguments("sequencer")]
    public async Task C448_V04_EachSequencerStateRefuses(string state)
    {
        using var fixture = new InspectionFixture();
        (await fixture.Git.InspectAsync(fixture.Coordinates, default)).Accepted.ShouldBeTrue();
        var path = Path.Combine(fixture.Admin, state);
        if (state is "rebase-merge" or "rebase-apply" or "sequencer") Directory.CreateDirectory(path);
        else File.WriteAllText(path, "resolution belongs to its writer");
        var result = await fixture.Git.InspectAsync(fixture.Coordinates, default);
        result.Accepted.ShouldBeFalse("an existing operation cannot be adopted or erased: " + state);
        (File.Exists(path) || Directory.Exists(path)).ShouldBeTrue();
        File.ReadAllText(fixture.Sentinel).ShouldBe("private work");
    }

    private sealed class InspectionFixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "c448-inspection-" + Guid.NewGuid().ToString("N"));
        public string Source => Path.Combine(Root, "source");
        public string Admin => Path.Combine(Root, "admin");
        public string Sentinel => Path.Combine(Source, "keep.txt");
        public LandSourceCoordinates Coordinates { get; }
        public Replies Git { get; }
        public InspectionFixture()
        {
            foreach (var path in new[] { Root, Source, Admin, Path.Combine(Root, "foreign") }) Directory.CreateDirectory(path);
            File.WriteAllText(Sentinel, "private work");
            Coordinates = new(Guid.NewGuid(), Root, Source, "refs/heads/source", "refs/heads/master");
            Git = new(this);
        }
        public void Dispose() => Directory.Delete(Root, recursive: true);
    }

    private sealed class Replies(InspectionFixture fixture) : LandingGit
    {
        private static readonly string Sha = new('a', 40);
        private static readonly string Other = new('b', 40);
        public string? Change { get; set; }
        public string Status { get; set; } = "";
        public bool StatusRead { get; set; }
        public override Task<LandingGitResult> RunAsync(string repository, IReadOnlyList<string> args, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            LandingGitResult Ok(string output = "") => new(0, output, "");
            LandingGitResult result;
            if (args[0] == "check-ref-format") result = Ok();
            else if (args.Contains("--git-common-dir")) result = Ok(Change == "common" && repository == fixture.Source ? Path.Combine(fixture.Root, "foreign") : fixture.Root);
            else if (args[0] == "worktree" && args[1] == "list")
            {
                var row = $"worktree {(Change == "registration-path" ? Path.Combine(fixture.Root, "foreign") : fixture.Source)}\0HEAD {(Change == "registration-head" ? Other : Sha)}\0branch {(Change == "registration-branch" ? "refs/heads/other" : fixture.Coordinates.SourceFullRef)}\0{(Change is "locked" or "prunable" ? Change + "\0" : "")}\0";
                result = new(Change == "registration-error" ? 128 : 0, Change == "ambiguous" ? row + row : row, "");
            }
            else if (args[0] == "symbolic-ref") result = Ok(Change == "symbolic-branch" ? "refs/heads/other" : fixture.Coordinates.SourceFullRef);
            else if (args[0] == "show-ref" && args.Contains("--exists")) result = Change == "ref-error" ? new(1, "", "ref error") : Ok();
            else if (args[0] == "show-ref") result = Ok(Change == "ref-result-head" ? Other : Sha);
            else if (args.Contains("--absolute-git-dir")) result = Ok(fixture.Admin);
            else if (args[0] == "rev-parse") result = Ok(Change == "branch-head" && args.Contains(fixture.Coordinates.SourceFullRef + "^{commit}") || Change == "second-reading" && StatusRead ? Other : Sha);
            else if (args[0] == "status") { StatusRead = true; result = Change == "status-error" ? new(1, "", "status error") : Ok(Status); }
            else if (args[0] == "ls-files") result = Change == "ignored-error" ? new(1, "", "ignored error") : Ok();
            else throw new InvalidOperationException("Unexpected command in read-only identity fixture: " + args[0]);
            return Task.FromResult(result);
        }
    }
}
