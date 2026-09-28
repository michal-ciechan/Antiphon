using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
public sealed class ProjectWorkerWorkspaceDefaultTests
{
    [Test]
    [Arguments(null, WorkspaceMode.Shared)]
    [Arguments(null, WorkspaceMode.Worktree)]
    [Arguments("Shared", WorkspaceMode.Shared)]
    [Arguments("Shared", WorkspaceMode.Worktree)]
    [Arguments("Worktree", WorkspaceMode.Shared)]
    [Arguments("Worktree", WorkspaceMode.Worktree)]
    public async Task C458_CreateStoresAndReportsProjectDefault(string? selected, WorkspaceMode global)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = NewDb(schema);
        var created = await Service(db, global).CreateAsync(Create(selected), CancellationToken.None);
        created.DefaultWorkerWorkspace.ShouldBe(selected);
        created.GlobalDefaultWorkerWorkspace.ShouldBe(global.ToString());
        created.EffectiveWorkerWorkspace.ShouldBe(selected ?? global.ToString());
        created.DispatchHonorsWorkspaceDefault.ShouldBeFalse();
        await using var fresh = NewDb(schema);
        var stored = await fresh.Projects.SingleAsync(p => p.Id == created.Id);
        string? persisted = stored.DefaultWorkerWorkspace?.ToString();
        persisted.ShouldBe(selected);
    }

    [Test]
    [Arguments("Shared")]
    [Arguments("Worktree")]
    public async Task C458_UpdateNullPreservesStoredDefault(string stored)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = NewDb(schema);
        var service = Service(db);
        var created = await service.CreateAsync(Create(stored), CancellationToken.None);
        var put = Update(null);
        var afterNull = await service.UpdateAsync(created.Id, put, CancellationToken.None);
        afterNull.DefaultWorkerWorkspace.ShouldBe(stored);
        var afterOmission = await service.UpdateAsync(created.Id, put, CancellationToken.None);
        afterOmission.DefaultWorkerWorkspace.ShouldBe(stored);
        afterOmission.BaseBranch.ShouldBe("release");
    }

    [Test]
    [Arguments(WorkspaceMode.Shared, "Worktree")]
    [Arguments(WorkspaceMode.Worktree, "Shared")]
    public async Task C458_UpdateInheritClearsStoredDefault(WorkspaceMode global, string stored)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = NewDb(schema);
        var service = Service(db, global);
        var created = await service.CreateAsync(Create(stored), CancellationToken.None);
        var updated = await service.UpdateAsync(created.Id, Update("Inherit"), CancellationToken.None);
        updated.DefaultWorkerWorkspace.ShouldBeNull();
        updated.EffectiveWorkerWorkspace.ShouldBe(global.ToString());
        await using var fresh = NewDb(schema);
        (await fresh.Projects.SingleAsync(p => p.Id == created.Id)).DefaultWorkerWorkspace.ShouldBeNull();
    }

    [Test]
    public async Task C458_UpdateRejectsReadOnlyWithoutChangingProject()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = NewDb(schema);
        var service = Service(db);
        var created = await service.CreateAsync(Create("Shared"), CancellationToken.None);
        foreach (var bad in new[] { "ReadOnly", "Unknown", "1" })
        {
            var error = await Should.ThrowAsync<ValidationException>(() =>
                service.UpdateAsync(created.Id, Update(bad), CancellationToken.None));
            error.Errors.ShouldContainKey("DefaultWorkerWorkspace");
            await using var fresh = NewDb(schema);
            var row = await fresh.Projects.SingleAsync(p => p.Id == created.Id);
            row.DefaultWorkerWorkspace.ShouldBe(WorkspaceMode.Shared);
            row.BaseBranch.ShouldBe("release");
        }
        await Should.ThrowAsync<ValidationException>(() =>
            service.CreateAsync(Create("ReadOnly"), CancellationToken.None));
    }

    [Test]
    [Arguments(null, WorkspaceMode.Shared)]
    [Arguments(null, WorkspaceMode.Worktree)]
    [Arguments("Shared", WorkspaceMode.Shared)]
    [Arguments("Shared", WorkspaceMode.Worktree)]
    [Arguments("Worktree", WorkspaceMode.Shared)]
    [Arguments("Worktree", WorkspaceMode.Worktree)]
    public async Task C458_ListAndDetailResolveEffectiveDefault(string? selected, WorkspaceMode global)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = NewDb(schema);
        var service = Service(db, global);
        var created = await service.CreateAsync(Create(selected), CancellationToken.None);
        var detail = await service.GetByIdAsync(created.Id, CancellationToken.None);
        var listed = (await service.GetAllAsync(CancellationToken.None)).Single(p => p.Id == created.Id);
        foreach (var dto in new[] { detail, listed })
        {
            dto.DefaultWorkerWorkspace.ShouldBe(selected);
            dto.GlobalDefaultWorkerWorkspace.ShouldBe(global.ToString());
            dto.EffectiveWorkerWorkspace.ShouldBe(selected ?? global.ToString());
            dto.DispatchHonorsWorkspaceDefault.ShouldBeFalse();
        }
    }

    private static AppDbContext NewDb(IsolatedTestSchema schema) =>
        new(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));

    private static ProjectService Service(AppDbContext db, WorkspaceMode global = WorkspaceMode.Worktree) =>
        new(db, new StubHttpClientFactory(), Options.Create(new GithubSettings()),
            NullLogger<ProjectService>.Instance,
            delegation: Options.Create(new DelegationSettings { DefaultWorkerWorkspace = global }));

    private static CreateProjectRequest Create(string? workspace) => new(
        $"C458-{Guid.NewGuid():N}", "https://example.test/repo.git", "AGENTS.md",
        false, false, null, "release", DefaultWorkerWorkspace: workspace);

    private static UpdateProjectRequest Update(string? workspace) => new(
        $"C458-{Guid.NewGuid():N}", "https://example.test/repo.git", "AGENTS.md",
        false, false, null, "release", DefaultWorkerWorkspace: workspace);

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }
}
