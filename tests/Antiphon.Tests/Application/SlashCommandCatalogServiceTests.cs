using System.IO.Abstractions.TestingHelpers;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// Unit tests for <see cref="SlashCommandCatalogService"/> driven through <c>GetForDirsAsync</c> over an
/// in-memory <see cref="MockFileSystem"/> (no database), with a <see cref="FakeTimeProvider"/> for the cache.
/// </summary>
[Category("Unit")]
public class SlashCommandCatalogServiceTests
{
    private const string UserDir = @"C:\Users\test\.claude";
    private const string ProjectDir = @"C:\proj\.claude";

    private sealed class FakeConfigDir(string dir) : IClaudeConfigDirProvider
    {
        public string Resolve() => dir;
    }

    private static SlashCommandCatalogService Build(MockFileSystem fs, TimeProvider? time = null) =>
        new(
            scopeFactory: null!, // GetForDirsAsync does not touch the DB scope
            new FakeConfigDir(UserDir),
            fs,
            time ?? new FakeTimeProvider(),
            NullLogger<SlashCommandCatalogService>.Instance);

    private static void AddFile(MockFileSystem fs, string path, string content) =>
        fs.AddFile(path, new MockFileData(content));

    [Test]
    public async Task Built_ins_are_always_present()
    {
        var fs = new MockFileSystem();
        var service = Build(fs);

        var result = await service.GetForDirsAsync(UserDir, ProjectDir, CancellationToken.None);

        result.ShouldContain(c => c.Name == "/help" && c.Source == "builtin" && c.Scope == "builtin");
        result.ShouldContain(c => c.Name == "/model" && c.Source == "builtin");
    }

    [Test]
    public async Task Missing_dirs_yield_only_builtins_without_throwing()
    {
        var fs = new MockFileSystem();
        var service = Build(fs);

        var result = await service.GetForDirsAsync(UserDir, ProjectDir, CancellationToken.None);

        result.ShouldAllBe(c => c.Source == "builtin");
    }

    [Test]
    public async Task Enumerates_commands_with_subfolder_namespacing()
    {
        var fs = new MockFileSystem();
        AddFile(fs, @"C:\proj\.claude\commands\hello.md", "Say hello");
        AddFile(fs, @"C:\proj\.claude\commands\git\commit.md", "---\ndescription: Make a commit\n---\nbody");
        AddFile(fs, @"C:\proj\.claude\commands\a\b\c.md", "deep");
        var service = Build(fs);

        var result = await service.GetForDirsAsync(UserDir, ProjectDir, CancellationToken.None);

        result.ShouldContain(c => c.Name == "/hello" && c.Source == "command" && c.Scope == "project");
        var commit = result.Single(r => r.Name == "/git:commit");
        commit.Description.ShouldBe("Make a commit");
        result.ShouldContain(c => c.Name == "/a:b:c");
    }

    [Test]
    public async Task Skill_name_comes_from_folder_with_and_without_frontmatter()
    {
        var fs = new MockFileSystem();
        AddFile(fs, @"C:\proj\.claude\skills\antiphon-run\SKILL.md",
            "# antiphon-run — Dev Stack Manager\n\nManages the local dev stack.");
        AddFile(fs, @"C:\proj\.claude\skills\bmad-architect\SKILL.md",
            "---\nname: architect\ndescription: Designs the architecture.\n---\nbody");
        var service = Build(fs);

        var result = await service.GetForDirsAsync(UserDir, ProjectDir, CancellationToken.None);

        // Folder name, not frontmatter `name`, drives the invocation token.
        result.ShouldContain(c => c.Name == "/bmad-architect" && c.Source == "skill" && c.Scope == "project");
        var run = result.Single(r => r.Name == "/antiphon-run");
        run.Description.ShouldBe("antiphon-run — Dev Stack Manager"); // body fallback, '#' stripped
    }

    [Test]
    public async Task Project_command_overrides_user_command_of_same_name()
    {
        var fs = new MockFileSystem();
        AddFile(fs, @"C:\Users\test\.claude\commands\deploy.md", "user deploy");
        AddFile(fs, @"C:\proj\.claude\commands\deploy.md", "---\ndescription: project deploy\n---\nx");
        var service = Build(fs);

        var result = await service.GetForDirsAsync(UserDir, ProjectDir, CancellationToken.None);

        var deploy = result.Single(r => r.Name == "/deploy");
        deploy.Scope.ShouldBe("project");
        deploy.Description.ShouldBe("project deploy");
    }

    [Test]
    public async Task Project_skill_overrides_builtin_of_same_name()
    {
        var fs = new MockFileSystem();
        AddFile(fs, @"C:\proj\.claude\skills\review\SKILL.md", "---\ndescription: Custom review\n---\nx");
        var service = Build(fs);

        var result = await service.GetForDirsAsync(UserDir, ProjectDir, CancellationToken.None);

        var review = result.Single(r => r.Name == "/review");
        review.Source.ShouldBe("skill");
        review.Scope.ShouldBe("project");
    }

    [Test]
    public async Task Builtin_beats_user_skill_of_same_name()
    {
        var fs = new MockFileSystem();
        AddFile(fs, @"C:\Users\test\.claude\skills\model\SKILL.md", "---\ndescription: a user skill\n---\nx");
        var service = Build(fs);

        var result = await service.GetForDirsAsync(UserDir, ProjectDir, CancellationToken.None);

        var model = result.Single(r => r.Name == "/model");
        model.Source.ShouldBe("builtin");
    }

    [Test]
    public async Task Caches_within_ttl_and_refreshes_after()
    {
        var fs = new MockFileSystem();
        AddFile(fs, @"C:\proj\.claude\commands\one.md", "first");
        var time = new FakeTimeProvider();
        var service = Build(fs, time);

        var first = await service.GetForDirsAsync(UserDir, ProjectDir, CancellationToken.None);
        first.ShouldContain(c => c.Name == "/one");

        // Add a file, re-query within the 10s TTL → stale cached result (no /two yet).
        AddFile(fs, @"C:\proj\.claude\commands\two.md", "second");
        time.Advance(TimeSpan.FromSeconds(5));
        var withinTtl = await service.GetForDirsAsync(UserDir, ProjectDir, CancellationToken.None);
        withinTtl.ShouldNotContain(c => c.Name == "/two");

        // Past the TTL → cache refreshes and now sees both.
        time.Advance(TimeSpan.FromSeconds(11));
        var afterTtl = await service.GetForDirsAsync(UserDir, ProjectDir, CancellationToken.None);
        afterTtl.ShouldContain(c => c.Name == "/one");
        afterTtl.ShouldContain(c => c.Name == "/two");
    }

    [Test]
    public async Task Slugifies_skill_folder_names_with_spaces_and_caps()
    {
        var fs = new MockFileSystem();
        AddFile(fs, @"C:\proj\.claude\skills\Object Type Router\SKILL.md", "---\ndescription: Routes objects\n---\nx");
        var service = Build(fs);

        var result = await service.GetForDirsAsync(UserDir, ProjectDir, CancellationToken.None);

        result.ShouldContain(c => c.Name == "/object-type-router" && c.Source == "skill");
        result.ShouldNotContain(c => c.Name.Contains(' ') || c.Name.Any(char.IsUpper));
    }

    [Test]
    public async Task Enumerates_installed_plugin_skills_and_commands()
    {
        var fs = new MockFileSystem();
        const string installPath = @"C:\Users\test\.claude\plugins\cache\mp\myplugin\1.0";
        var manifest = "{\"plugins\":{\"myplugin@mp\":[{\"installPath\":\"" + installPath.Replace("\\", "\\\\") + "\"}]}}";
        AddFile(fs, @"C:\Users\test\.claude\plugins\installed_plugins.json", manifest);
        AddFile(fs, installPath + @"\skills\cool-skill\SKILL.md", "---\ndescription: A cool plugin skill\n---\nx");
        AddFile(fs, installPath + @"\commands\do-thing.md", "---\ndescription: A plugin command\n---\nx");
        var service = Build(fs);

        var result = await service.GetForDirsAsync(UserDir, ProjectDir, CancellationToken.None);

        result.ShouldContain(c => c.Name == "/cool-skill" && c.Source == "skill" && c.Scope == "plugin");
        result.ShouldContain(c => c.Name == "/do-thing" && c.Source == "command" && c.Scope == "plugin");
    }

    [Test]
    public async Task Null_project_dir_skips_project_scope()
    {
        var fs = new MockFileSystem();
        AddFile(fs, @"C:\Users\test\.claude\commands\useronly.md", "user only");
        var service = Build(fs);

        var result = await service.GetForDirsAsync(UserDir, projectClaudeDir: null, CancellationToken.None);

        result.ShouldContain(c => c.Name == "/useronly" && c.Scope == "user");
    }
}
