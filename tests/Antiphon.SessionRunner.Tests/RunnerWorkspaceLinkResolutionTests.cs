using Shouldly;
using TUnit.Core;

namespace Antiphon.SessionRunner.Tests;

/// <summary>
/// rc/2026-11-04 Windows group-1 C1677 symlinked-leaf: on Windows <see cref="FileSystemInfo.ResolveLinkTarget"/>
/// throws <see cref="UnauthorizedAccessException"/> for a file link to a directory (ERROR_ACCESS_DENIED on the final
/// open), and <c>TryResolveFinal</c> let it escape every caller instead of answering "cannot resolve". Linux never
/// throws there, so the resolver seam stands in for the Windows failure.
/// </summary>
public sealed class RunnerWorkspaceLinkResolutionTests
{
    [Test]
    [Arguments("access-denied")]
    [Arguments("io")]
    public void An_unresolvable_link_is_refused_not_thrown(string failure)
    {
        using var scratch = new LinkScratch();
        Func<FileSystemInfo, FileSystemInfo?> resolver = failure == "access-denied"
            ? _ => throw new UnauthorizedAccessException("Access to the path is denied.")
            : _ => throw new IOException("The file or directory cannot be accessed.");

        RunnerWorkspaceService.TryResolveFinal(scratch.Link, resolver).ShouldBeNull();
        RunnerWorkspaceService.TryResolveFinal(Path.Combine(scratch.Link, "child"), resolver).ShouldBeNull();
    }

    [Test]
    public void A_resolvable_link_resolves_to_its_target()
    {
        using var scratch = new LinkScratch();

        RunnerWorkspaceService.TryResolveFinal(scratch.Link).ShouldBe(RunnerWorkspaceService.TryResolveFinal(scratch.Target));
    }

    private sealed class LinkScratch : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "c1677-link-" + Guid.NewGuid().ToString("N"));

        public LinkScratch()
        {
            Target = Directory.CreateDirectory(Path.Combine(_root, "real", "task-a1677001")).FullName;
            Link = Path.Combine(Directory.CreateDirectory(Path.Combine(_root, "worktrees")).FullName, "task-a1677001");
            Directory.CreateSymbolicLink(Link, Target);
        }

        public string Target { get; }

        public string Link { get; }

        public void Dispose() => Directory.Delete(_root, recursive: true);
    }
}
