using System.Diagnostics;
using System.Text.Json;
using System.Xml.Linq;
using Antiphon.Agents.Pty;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Shouldly;
using TUnit.Core;
using TUnit.Core.Exceptions;
using Antiphon.Tests.TestHelpers;

namespace Antiphon.Tests.Invariants;

[Category("Integration")]
[Category("RepositoryInvariant")]
public sealed class TestDbFixtureLazyInitializationInvariantTests
{
    [Test]
    public void Worker_selection_reaches_the_shared_store()
    {
        var selected = typeof(Antiphon.Tests.Application.CheckCompactionCrashTests).GetMethod(
            nameof(Antiphon.Tests.Application.CheckCompactionCrashTests.Resume_reservation_survives_crash_without_a_second_launch))
            ?? throw new InvalidOperationException("selected store-reaching test is missing");
        SharedStoreWarmup.ReachesDefaultStore(selected).ShouldBeTrue("the child selection must reach the shared store");
    }
}
