using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Options;
using Npgsql;
using Shouldly;
using TUnit.Core;
using Antiphon.Tests.Application;

namespace Antiphon.Tests.Invariants;

[Category("Integration")]
[Category("RepositoryInvariant")]
public sealed class DispatchConcurrencyMigrationInvariantTests
{
    [Test]
    public void Dispatch_concurrency_migration_has_a_predecessor()
    {
        var migrations = MigrationTestDatabase.Names();
        var index = Array.FindIndex(migrations, name => name.EndsWith("_AddDispatchConcurrencySettings", StringComparison.Ordinal));
        index.ShouldBeGreaterThan(0);
    }
}
