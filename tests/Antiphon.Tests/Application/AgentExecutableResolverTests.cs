using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Services;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// Pins executable resolution for agent launches. The claude.cmd incident: the configured npm shim
/// (claude.cmd) disappeared when Claude moved to the native installer (claude.exe) and every agent
/// start silently failed. The resolver must find sibling flavors and reject unresolvable names with
/// a clear error BEFORE any launch state is created.
/// </summary>
public class AgentExecutableResolverTests
{
    [Test]
    public void Resolves_exact_name_from_search_path()
    {
        using var dir = new TempToolDir("claude.exe");
        var resolver = new AgentExecutableResolver(dir.Path);

        var resolved = resolver.TryResolve("claude.exe");

        resolved.ShouldBe(System.IO.Path.Combine(dir.Path, "claude.exe"));
    }

    [Test]
    public void Resolves_sibling_flavor_when_configured_one_is_gone()
    {
        // The incident, in miniature: config says claude.cmd, disk only has claude.exe.
        using var dir = new TempToolDir("claude.exe");
        var resolver = new AgentExecutableResolver(dir.Path);

        var resolved = resolver.TryResolve("claude.cmd");

        resolved.ShouldBe(System.IO.Path.Combine(dir.Path, "claude.exe"));
    }

    [Test]
    public void Rooted_path_resolves_only_when_the_file_exists()
    {
        using var dir = new TempToolDir("claude.exe");
        var resolver = new AgentExecutableResolver(dir.Path);
        var rooted = System.IO.Path.Combine(dir.Path, "claude.exe");

        resolver.TryResolve(rooted).ShouldBe(rooted);
        resolver.TryResolve(System.IO.Path.Combine(dir.Path, "missing.exe")).ShouldBeNull();
    }

    [Test]
    public void Unresolvable_name_returns_null_and_EnsureSpawnable_throws_with_clear_message()
    {
        var resolver = new AgentExecutableResolver(System.IO.Path.GetTempPath());

        resolver.TryResolve("antiphon-no-such-tool-77c2.cmd").ShouldBeNull();

        var ex = Should.Throw<ConflictException>(
            () => resolver.EnsureSpawnable("antiphon-no-such-tool-77c2.cmd"));
        ex.Message.ShouldContain("antiphon-no-such-tool-77c2.cmd");
        ex.Message.ShouldContain("PATH");
    }

    [Test]
    public void Relative_path_with_directory_separator_is_passed_through_unresolved()
    {
        var resolver = new AgentExecutableResolver(System.IO.Path.GetTempPath());

        resolver.TryResolve(@"tools\claude.exe").ShouldBeNull();
    }

    private sealed class TempToolDir : IDisposable
    {
        public string Path { get; }

        public TempToolDir(params string[] files)
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), $"antiphon-resolver-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
            foreach (var file in files)
                File.WriteAllText(System.IO.Path.Combine(Path, file), "@echo stub");
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch
            {
                // Best-effort temp cleanup.
            }
        }
    }
}
