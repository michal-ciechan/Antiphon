using System.Text.Json;
using Antiphon.Messaging;
using Antiphon.Messaging.Client;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Agents;
using Antiphon.Server.Infrastructure.Agents.SessionRunner;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.Application;
using Antiphon.Tests.TestHelpers;
using Hangfire;
using Hangfire.InMemory;
using Hangfire.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;
using Antiphon.Tests.Infrastructure;

namespace Antiphon.Tests.Invariants;

[Category("Integration")]
[Category("RepositoryInvariant")]
[NotInParallel]
public sealed class ExpectationWatchdogJobInvariantTests
{
    [Test]
    public void Watchdog_declares_the_expectations_queue()
    {
        typeof(ExpectationWatchdogJob).GetMethod(nameof(ExpectationWatchdogJob.ExecuteAsync))!
            .GetCustomAttributes(typeof(QueueAttribute), true).OfType<QueueAttribute>()
            .ShouldHaveSingleItem().Queue.ShouldBe("expectations");
    }
}
