using Antiphon.Server.Application.Exceptions;
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
using Shouldly;
using TUnit.Core;
using Antiphon.Tests.Application;

namespace Antiphon.Tests.Invariants;

[Category("Integration")]
[Category("RepositoryInvariant")]
public sealed class WorktreeBaseSelectionInvariantTests
{
    [Test]
    public void Base_selection_migration_has_a_predecessor()
    {
        var migrations = MigrationTestDatabase.Names();
        var cut = Array.FindIndex(migrations, m => m.EndsWith("_AddWorktreeBaseSelection", StringComparison.Ordinal));
        cut.ShouldBeGreaterThan(0);
    }
}
