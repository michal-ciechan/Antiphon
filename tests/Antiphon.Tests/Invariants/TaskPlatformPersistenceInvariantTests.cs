using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Shouldly;
using TUnit.Core;
using Antiphon.Tests.Application;

namespace Antiphon.Tests.Invariants;

[Category("Integration")]
[Category("RepositoryInvariant")]
public sealed class TaskPlatformPersistenceInvariantTests
{
    [Test]
    public void Task_platform_migration_has_a_predecessor()
    {
        var migrations = MigrationTestDatabase.Names();
        var platform = Array.FindIndex(migrations, name => name.EndsWith("_AddTaskPlatformPlacement", StringComparison.Ordinal));
        platform.ShouldBeGreaterThan(0);
    }
}
