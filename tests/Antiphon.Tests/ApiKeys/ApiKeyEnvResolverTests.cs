using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.ApiKeys;

/// <summary>
/// CARD-0106 S2 — resolution and its precedence rule.
///
/// <para>Project overrides global, and a launch with no project resolves global keys ONLY. That
/// second half is the pool-delegate rule: a delegate spawned by the dispatcher has no board and
/// therefore no project, and deriving one from its working directory was rejected as unreliable
/// (worktrees are sibling directories, so a prefix match would silently mis-scope a secret). A
/// pinned standing agent WITH a board gets that project's keys, which is what makes the
/// "delegate on a project-specific credential" case actually work.</para>
/// </summary>
[Category("Integration")]
public class ApiKeyEnvResolverTests
{
    private static CancellationToken Ct => CancellationToken.None;

    [Test]
    public async Task a_global_key_resolves_for_a_launch_with_no_project()
    {
        await using var db = NewDb();
        var sut = NewResolver(db);
        var name = await AddKeyAsync(db, projectId: null, value: "sk-global");

        var resolved = await sut.ResolveAsync(Env(("ANTHROPIC_API_KEY", $"{{{{key:{name}}}}}")),
            projectId: null, subject: "test", ct: Ct);

        resolved["ANTHROPIC_API_KEY"].ShouldBe("sk-global");
    }

    [Test]
    public async Task a_project_key_wins_over_a_global_key_of_the_same_name()
    {
        // The override feature. The narrower scope wins — the same rule every other override in this
        // codebase follows (an agent's compaction overrides beat the installation settings).
        await using var db = NewDb();
        var sut = NewResolver(db);
        var project = await AddProjectAsync(db);
        var name = NewName();
        await AddKeyAsync(db, projectId: null, value: "sk-global", name: name);
        await AddKeyAsync(db, projectId: project.Id, value: "sk-project", name: name);

        var resolved = await sut.ResolveAsync(Env(("K", $"{{{{key:{name}}}}}")), project.Id, "test", Ct);

        resolved["K"].ShouldBe("sk-project");
    }

    [Test]
    public async Task a_project_launch_falls_back_to_the_global_key_when_the_project_has_none()
    {
        await using var db = NewDb();
        var sut = NewResolver(db);
        var project = await AddProjectAsync(db);
        var name = await AddKeyAsync(db, projectId: null, value: "sk-global");

        var resolved = await sut.ResolveAsync(Env(("K", $"{{{{key:{name}}}}}")), project.Id, "test", Ct);

        resolved["K"].ShouldBe("sk-global");
    }

    [Test]
    public async Task a_pool_delegate_with_no_project_never_sees_another_projects_key()
    {
        // This is the rule stated as a NEGATIVE, which is the half that matters: a delegate with no
        // board must not reach a project key by any route, or a secret scoped to one repository
        // would leak into work on another.
        await using var db = NewDb();
        var sut = NewResolver(db);
        var project = await AddProjectAsync(db);
        var name = await AddKeyAsync(db, projectId: project.Id, value: "sk-project");

        var ex = await Should.ThrowAsync<ConflictException>(
            sut.ResolveAsync(Env(("K", $"{{{{key:{name}}}}}")), projectId: null, "pool delegate", Ct));

        ex.Code.ShouldBe("api_key_not_found");
        ex.Message.ShouldContain("the global scope");
        ex.Message.ShouldNotContain("sk-project");
    }

    [Test]
    public async Task a_key_scoped_to_a_DIFFERENT_project_is_not_found_either()
    {
        await using var db = NewDb();
        var sut = NewResolver(db);
        var mine = await AddProjectAsync(db);
        var theirs = await AddProjectAsync(db);
        var name = await AddKeyAsync(db, projectId: theirs.Id, value: "sk-theirs");

        var ex = await Should.ThrowAsync<ConflictException>(
            sut.ResolveAsync(Env(("K", $"{{{{key:{name}}}}}")), mine.Id, "test", Ct));

        ex.Message.ShouldContain(name);
        ex.Message.ShouldNotContain("sk-theirs");
    }

    [Test]
    public async Task an_unknown_key_names_the_key_and_the_scopes_searched_and_carries_no_value()
    {
        await using var db = NewDb();
        var sut = NewResolver(db);
        var project = await AddProjectAsync(db);
        // A real key exists under a different name, so a message that leaked "the nearest value"
        // would have something to leak.
        await AddKeyAsync(db, projectId: null, value: "sk-canary-unknown");

        var ex = await Should.ThrowAsync<ConflictException>(
            sut.ResolveAsync(Env(("K", "{{key:nobody-stored-this}}")), project.Id, "agent 'x'", Ct));

        ex.StatusCode.ShouldBe(409);
        ex.Code.ShouldBe("api_key_not_found");
        ex.Message.ShouldContain("nobody-stored-this");
        ex.Message.ShouldContain(project.Id.ToString("D"));
        ex.Message.ShouldContain("global");
        ex.Message.ShouldContain("agent 'x'");
        ex.Message.ShouldNotContain("sk-canary-unknown");
    }

    [Test]
    public async Task a_key_deleted_after_it_was_referenced_fails_the_next_launch_loudly()
    {
        // Deleting a project cascades its keys; an agent still referencing one must fail, not launch
        // with the literal token or with the variable quietly dropped.
        await using var db = NewDb();
        var sut = NewResolver(db);
        var name = await AddKeyAsync(db, projectId: null, value: "sk-going-away");
        var env = Env(("K", $"{{{{key:{name}}}}}"));
        (await sut.ResolveAsync(env, null, "test", Ct))["K"].ShouldBe("sk-going-away");

        db.ApiKeys.RemoveRange(await db.ApiKeys.Where(k => k.Name == name).ToListAsync(Ct));
        await db.SaveChangesAsync(Ct);

        await using var fresh = NewDb();
        await Should.ThrowAsync<ConflictException>(
            NewResolver(fresh).ResolveAsync(env, null, "test", Ct));
    }

    [Test]
    public async Task several_tokens_in_one_value_and_several_values_all_resolve()
    {
        await using var db = NewDb();
        var sut = NewResolver(db);
        var a = await AddKeyAsync(db, null, "AAA");
        var b = await AddKeyAsync(db, null, "BBB");

        var resolved = await sut.ResolveAsync(
            Env(("PAIR", $"{{{{key:{a}}}}}:{{{{key:{b}}}}}"),
                ("REPEAT", $"{{{{key:{a}}}}}/{{{{key:{a}}}}}"),
                ("PLAIN", "untouched")),
            null, "test", Ct);

        resolved["PAIR"].ShouldBe("AAA:BBB");
        resolved["REPEAT"].ShouldBe("AAA/AAA");
        resolved["PLAIN"].ShouldBe("untouched");
    }

    [Test]
    public async Task an_environment_with_no_placeholder_is_returned_untouched_and_hits_no_database()
    {
        // Every launch in the installation until somebody writes a placeholder takes this path, so
        // it must not cost a query — and it must not rewrite the dictionary either.
        await using var db = NewDb();
        var sut = NewResolver(db);
        var env = Env(("A", "1"), ("B", "{not a placeholder}"));

        var resolved = await sut.ResolveAsync(env, null, "test", Ct);

        resolved.ShouldBeSameAs(env);
    }

    [Test]
    public async Task a_malformed_placeholder_names_the_variable_it_was_typed_into()
    {
        await using var db = NewDb();
        var sut = NewResolver(db);

        var ex = await Should.ThrowAsync<ConflictException>(
            sut.ResolveAsync(Env(("BROKEN", "{{key:has space}}")), null, "test", Ct));

        ex.Code.ShouldBe("api_key_placeholder_malformed");
        ex.Message.ShouldContain("BROKEN");
    }

    [Test]
    public async Task a_placeholder_in_a_launch_ARGUMENT_is_refused_rather_than_resolved()
    {
        await using var db = NewDb();
        var sut = NewResolver(db);
        var name = await AddKeyAsync(db, null, "sk-should-never-reach-argv");
        var spec = NewSpec(args: ["--header", $"Bearer {{{{key:{name}}}}}"]);

        var ex = await Should.ThrowAsync<InvalidOperationException>(
            sut.ResolveSpecAsync(spec, projectId: null, "agent 'x'", Ct));

        ex.Message.ShouldContain("environment VALUES only");
        ex.Message.ShouldNotContain("sk-should-never-reach-argv");
    }

    [Test]
    public async Task the_project_scope_comes_from_the_agents_board()
    {
        await using var db = NewDb();
        var sut = NewResolver(db);
        var project = await AddProjectAsync(db);
        var board = await AddBoardAsync(db, project.Id);
        var name = NewName();
        await AddKeyAsync(db, projectId: null, value: "sk-global", name: name);
        await AddKeyAsync(db, projectId: project.Id, value: "sk-project", name: name);
        var agent = new Agent { Id = Guid.NewGuid(), Name = "pinned", BoardId = board.Id };

        (await sut.ResolveProjectIdAsync(agent.BoardId, Ct)).ShouldBe(project.Id);
        var spec = await sut.ResolveSpecAsync(
            NewSpec(env: Env(("K", $"{{{{key:{name}}}}}"))), agent, "agent 'pinned'", Ct);

        spec.Env["K"].ShouldBe("sk-project", "a pinned agent with a board resolves ITS project's key");
    }

    [Test]
    public async Task an_agent_with_no_board_resolves_global_only()
    {
        await using var db = NewDb();
        var sut = NewResolver(db);
        var poolDelegate = new Agent { Id = Guid.NewGuid(), Name = "delegate", BoardId = null };

        (await sut.ResolveProjectIdAsync(poolDelegate.BoardId, Ct)).ShouldBeNull();
    }

    [Test]
    public async Task a_decrypt_failure_is_a_503_naming_the_key_and_not_the_ciphertext()
    {
        await using var db = NewDb();
        var name = NewName();
        // Ciphertext this protector cannot read: written under a different row id.
        db.ApiKeys.Add(new ApiKey
        {
            Id = Guid.NewGuid(),
            Name = name,
            Ciphertext = $"{Guid.NewGuid():N}:c2stbm90LW1pbmU=",
            ProtectionVersion = "v1",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync(Ct);

        var ex = await Should.ThrowAsync<ServiceUnavailableException>(
            NewResolver(db).ResolveAsync(Env(("K", $"{{{{key:{name}}}}}")), null, "agent 'x'", Ct));

        ex.StatusCode.ShouldBe(503);
        ex.Code.ShouldBe("api_key_protection_unavailable");
        ex.Message.ShouldContain(name);
    }

    [Test]
    public async Task a_resolved_value_that_would_not_fit_an_environment_variable_is_refused_by_length()
    {
        await using var db = NewDb();
        var sut = NewResolver(db);
        var name = await AddKeyAsync(db, null, new string('k', 3900));

        var ex = await Should.ThrowAsync<ConflictException>(sut.ResolveAsync(
            Env(("K", $"prefix-{new string('p', 200)}{{{{key:{name}}}}}")), null, "test", Ct));

        ex.Code.ShouldBe("api_key_value_too_long");
        ex.Message.ShouldContain("K");
        ex.Message.ShouldNotContain("kkkkkkkkkk");
    }

    // ---- helpers ---------------------------------------------------------------------------------

    private static string NewName() => $"res-{Guid.NewGuid():N}";

    private static AppDbContext NewDb() => new(TestDbFixture.CreateDbContextOptions());

    private static ApiKeyEnvResolver NewResolver(AppDbContext db) =>
        new(db, new ApiKeyStoreTests.FakeApiKeyProtector(), NullLogger<ApiKeyEnvResolver>.Instance);

    private static IReadOnlyDictionary<string, string> Env(params (string Name, string Value)[] pairs) =>
        pairs.ToDictionary(p => p.Name, p => p.Value, StringComparer.Ordinal);

    private static async Task<string> AddKeyAsync(
        AppDbContext db, Guid? projectId, string value, string? name = null)
    {
        var keyName = name ?? NewName();
        var id = Guid.NewGuid();
        db.ApiKeys.Add(new ApiKey
        {
            Id = id,
            Name = keyName,
            ProjectId = projectId,
            Ciphertext = new ApiKeyStoreTests.FakeApiKeyProtector().Protect(id, value),
            ProtectionVersion = "v1",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync(Ct);
        return keyName;
    }

    private static async Task<Project> AddProjectAsync(AppDbContext db)
    {
        var now = DateTime.UtcNow;
        var project = new Project
        {
            Id = Guid.NewGuid(),
            Name = $"Resolver Project {Guid.NewGuid():N}",
            GitRepositoryUrl = "https://example.test/repo.git",
            BaseBranch = "main",
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.Projects.Add(project);
        await db.SaveChangesAsync(Ct);
        return project;
    }

    private static async Task<Board> AddBoardAsync(AppDbContext db, Guid projectId)
    {
        var now = DateTime.UtcNow;
        var board = new Board
        {
            Id = Guid.NewGuid(),
            ProjectId = projectId,
            Name = $"Board {Guid.NewGuid():N}",
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.Boards.Add(board);
        await db.SaveChangesAsync(Ct);
        return board;
    }

    private static AgentLaunchSpec NewSpec(
        IReadOnlyDictionary<string, string>? env = null,
        IReadOnlyList<string>? args = null) =>
        new(
            DefinitionName: "test",
            Kind: AgentKind.ClaudeCode,
            Exe: "claude.exe",
            Args: args ?? [],
            Env: env ?? new Dictionary<string, string>(),
            Cwd: "C:\\tmp",
            Cols: 120,
            Rows: 30);
}
