using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.ApiKeys;

/// <summary>
/// CARD-0106 S1 — the store and its CRUD contract, against the real Postgres schema (the two
/// FILTERED unique indexes are the point, and an in-memory provider would not have them).
///
/// <para>Every assertion here is scoped to rows this test made: the assembly shares one database
/// and other suites are writing throughout, so a count over an unscoped query would also be
/// asserting "nobody else has a key right now".</para>
/// </summary>
public class ApiKeyStoreTests
{
    private static CancellationToken Ct => CancellationToken.None;

    [Test]
    public async Task a_project_key_and_a_global_key_may_share_a_name()
    {
        // This is not a tolerated collision — it IS the override feature. If the unique index were
        // one composite over (ProjectId, Name), Postgres would treat every NULL ProjectId as
        // distinct and the GLOBAL scope would lose its uniqueness instead.
        await using var db = NewDb();
        var service = NewService(db);
        var project = await AddProjectAsync(db);
        var name = NewName();

        var global = await service.PutAsync(name, null, "global-value", Ct);
        var scoped = await service.PutAsync(name, project.Id, "project-value", Ct);

        global.Id.ShouldNotBe(scoped.Id);
        global.ProjectId.ShouldBeNull();
        scoped.ProjectId.ShouldBe(project.Id);
        scoped.ProjectName.ShouldBe(project.Name);
    }

    [Test]
    public async Task two_global_keys_cannot_share_a_name_the_second_write_replaces_the_first()
    {
        await using var db = NewDb();
        var service = NewService(db);
        var name = NewName();

        var first = await service.PutAsync(name, null, "one", Ct);
        var second = await service.PutAsync(name, null, "two", Ct);

        // Upsert, not a second row: the id is stable, which is what keeps the ciphertext readable
        // (the purpose chain is keyed on it).
        second.Id.ShouldBe(first.Id);
        (await db.ApiKeys.CountAsync(k => k.Name == name, Ct)).ShouldBe(1);
    }

    [Test]
    public async Task a_duplicate_global_row_inserted_behind_the_service_is_refused_by_the_index()
    {
        // The filtered index proven directly rather than through the upsert that avoids it — this
        // is what stops two racing writers from creating a name that resolves ambiguously.
        await using var db = NewDb();
        var service = NewService(db);
        var name = NewName();
        await service.PutAsync(name, null, "one", Ct);

        await using var second = NewDb();
        second.ApiKeys.Add(NewRow(name, projectId: null));

        await Should.ThrowAsync<DbUpdateException>(second.SaveChangesAsync(Ct));
    }

    [Test]
    public async Task a_duplicate_key_within_one_project_is_refused_by_the_index()
    {
        await using var db = NewDb();
        var service = NewService(db);
        var project = await AddProjectAsync(db);
        var name = NewName();
        await service.PutAsync(name, project.Id, "one", Ct);

        await using var second = NewDb();
        second.ApiKeys.Add(NewRow(name, project.Id));

        await Should.ThrowAsync<DbUpdateException>(second.SaveChangesAsync(Ct));
    }

    [Test]
    public async Task the_same_name_under_two_different_projects_is_legal()
    {
        await using var db = NewDb();
        var service = NewService(db);
        var one = await AddProjectAsync(db);
        var two = await AddProjectAsync(db);
        var name = NewName();

        var a = await service.PutAsync(name, one.Id, "a", Ct);
        var b = await service.PutAsync(name, two.Id, "b", Ct);

        a.Id.ShouldNotBe(b.Id);
    }

    [Test]
    public async Task deleting_a_project_deletes_its_keys_and_leaves_the_globals_standing()
    {
        await using var db = NewDb();
        var service = NewService(db);
        var project = await AddProjectAsync(db);
        var name = NewName();
        var scoped = await service.PutAsync(name, project.Id, "project-value", Ct);
        var global = await service.PutAsync(name, null, "global-value", Ct);

        await using (var delete = NewDb())
        {
            delete.Projects.Remove(await delete.Projects.SingleAsync(p => p.Id == project.Id, Ct));
            await delete.SaveChangesAsync(Ct);
        }

        await using var verify = NewDb();
        (await verify.ApiKeys.AnyAsync(k => k.Id == scoped.Id, Ct))
            .ShouldBeFalse("the project's own keys cascade with it");
        (await verify.ApiKeys.AnyAsync(k => k.Id == global.Id, Ct))
            .ShouldBeTrue("a global key belongs to the installation, not to any project");
    }

    [Test]
    public async Task the_stored_ciphertext_is_never_the_value_and_the_dto_never_carries_one()
    {
        await using var db = NewDb();
        var service = NewService(db);
        var name = NewName();

        var dto = await service.PutAsync(name, null, "sk-canary-0106", Ct);

        var row = await db.ApiKeys.AsNoTracking().SingleAsync(k => k.Id == dto.Id, Ct);
        row.Ciphertext.ShouldNotContain("sk-canary-0106");
        row.ProtectionVersion.ShouldNotBeNullOrWhiteSpace();
        // The DTO is the ONLY shape the API ever returns for a key, and it is a record of six
        // metadata fields — there is no property a value could hide in.
        dto.ToString().ShouldNotContain("sk-canary-0106");
    }

    [Test]
    public async Task listing_returns_metadata_only_and_never_the_ciphertext()
    {
        await using var db = NewDb();
        var service = NewService(db);
        var name = NewName();
        await service.PutAsync(name, null, "sk-canary-list", Ct);

        var all = await service.ListAsync(Ct);

        all.Single(k => k.Name == name).ProjectId.ShouldBeNull();
        string.Join("|", all.Select(k => k.ToString())).ShouldNotContain("sk-canary-list");
    }

    [Test]
    public async Task a_projects_list_is_its_own_keys_not_the_globals_it_also_resolves_against()
    {
        await using var db = NewDb();
        var service = NewService(db);
        var project = await AddProjectAsync(db);
        var scopedName = NewName();
        var globalName = NewName();
        await service.PutAsync(scopedName, project.Id, "p", Ct);
        await service.PutAsync(globalName, null, "g", Ct);

        var listed = await service.ListForProjectAsync(project.Id, Ct);

        listed.Select(k => k.Name).ShouldBe([scopedName]);
    }

    [Test]
    public async Task a_value_over_the_environment_ceiling_is_refused_at_write_time()
    {
        // 4000 is the same ceiling AgentTuiLaunchResolver enforces on a managed environment value.
        // Refused HERE so the operator hears it while typing, rather than at a launch weeks later.
        await using var db = NewDb();
        var service = NewService(db);

        var ex = await Should.ThrowAsync<ValidationException>(service.PutAsync(
            NewName(), null, new string('x', ApiKeyNaming.MaxValueLength + 1), Ct));

        ex.StatusCode.ShouldBe(422);
        ex.Errors.Values.SelectMany(e => e).ShouldContain(e => e.Contains("4000"));
        // The length is the operator's own input; the value itself never appears.
        ex.Errors.Values.SelectMany(e => e).ShouldNotContain(e => e.Contains("xxxxxxxxxx"));
    }

    [Test]
    public async Task a_value_exactly_at_the_ceiling_is_accepted()
    {
        await using var db = NewDb();
        var service = NewService(db);

        var dto = await service.PutAsync(
            NewName(), null, new string('x', ApiKeyNaming.MaxValueLength), Ct);

        dto.Id.ShouldNotBe(Guid.Empty);
    }

    [Test]
    public async Task an_empty_value_is_refused()
    {
        await using var db = NewDb();
        var service = NewService(db);

        await Should.ThrowAsync<ValidationException>(service.PutAsync(NewName(), null, "", Ct));
    }

    [Test]
    [Arguments("has space")]
    [Arguments("has/slash")]
    [Arguments("{{key:nested}}")]
    [Arguments("")]
    public async Task a_name_that_no_placeholder_could_spell_is_refused(string name)
    {
        // A name storable here but unspellable in {{key:NAME}} would be a key nothing can ever
        // reference — it would save cleanly and then never resolve.
        await using var db = NewDb();
        var service = NewService(db);

        await Should.ThrowAsync<ValidationException>(service.PutAsync(name, null, "value", Ct));
    }

    [Test]
    public async Task a_key_written_against_a_project_that_does_not_exist_is_a_404_not_a_global_key()
    {
        // Falling back to global here would silently widen a secret's scope.
        await using var db = NewDb();
        var service = NewService(db);

        await Should.ThrowAsync<NotFoundException>(
            service.PutAsync(NewName(), Guid.NewGuid(), "value", Ct));
    }

    [Test]
    public async Task deleting_a_key_removes_it_and_deleting_an_unknown_id_is_a_404()
    {
        await using var db = NewDb();
        var service = NewService(db);
        var dto = await service.PutAsync(NewName(), null, "value", Ct);

        await service.DeleteAsync(dto.Id, Ct);

        (await db.ApiKeys.AnyAsync(k => k.Id == dto.Id, Ct)).ShouldBeFalse();
        await Should.ThrowAsync<NotFoundException>(service.DeleteAsync(Guid.NewGuid(), Ct));
    }

    [Test]
    public async Task a_protection_failure_reports_the_key_name_and_never_the_value()
    {
        await using var db = NewDb();
        var service = new ApiKeyService(
            db, new ThrowingApiKeyProtector(), NullLogger<ApiKeyService>.Instance);
        var name = NewName();

        var ex = await Should.ThrowAsync<ServiceUnavailableException>(
            service.PutAsync(name, null, "sk-canary-protect", Ct));

        ex.StatusCode.ShouldBe(503);
        ex.Message.ShouldContain(name);
        ex.Message.ShouldNotContain("sk-canary-protect");
        (await db.ApiKeys.AnyAsync(k => k.Name == name, Ct))
            .ShouldBeFalse("nothing is stored when the value could not be encrypted");
    }

    private static string NewName() => $"test-{Guid.NewGuid():N}";

    private static AppDbContext NewDb() => new(TestDbFixture.CreateDbContextOptions());

    private static ApiKeyService NewService(AppDbContext db) =>
        new(db, new FakeApiKeyProtector(), NullLogger<ApiKeyService>.Instance);

    private static ApiKey NewRow(string name, Guid? projectId) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        ProjectId = projectId,
        Ciphertext = "x",
        ProtectionVersion = "v1",
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow,
    };

    private static async Task<Project> AddProjectAsync(AppDbContext db)
    {
        var now = DateTime.UtcNow;
        var project = new Project
        {
            Id = Guid.NewGuid(),
            Name = $"ApiKey Project {Guid.NewGuid():N}",
            GitRepositoryUrl = "https://example.test/repo.git",
            BaseBranch = "main",
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.Projects.Add(project);
        await db.SaveChangesAsync(Ct);
        return project;
    }

    /// <summary>Reversible stand-in for the real protector — the crypto itself has its own tests.</summary>
    internal sealed class FakeApiKeyProtector : IApiKeyProtector
    {
        public string Protect(Guid keyId, string plaintext) =>
            $"{keyId:N}:{Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(plaintext))}";

        public string Unprotect(Guid keyId, string protectedValue)
        {
            var prefix = $"{keyId:N}:";
            if (!protectedValue.StartsWith(prefix, StringComparison.Ordinal))
            {
                throw new System.Security.Cryptography.CryptographicException(
                    "API key payload is invalid.");
            }

            return System.Text.Encoding.UTF8.GetString(
                Convert.FromBase64String(protectedValue[prefix.Length..]));
        }
    }

    private sealed class ThrowingApiKeyProtector : IApiKeyProtector
    {
        public string Protect(Guid keyId, string plaintext) =>
            throw new System.Security.Cryptography.CryptographicException("not ready");

        public string Unprotect(Guid keyId, string protectedValue) =>
            throw new System.Security.Cryptography.CryptographicException("not ready");
    }
}
