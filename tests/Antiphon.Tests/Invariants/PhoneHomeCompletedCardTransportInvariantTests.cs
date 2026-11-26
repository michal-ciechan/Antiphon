using System.Reflection;
using System.Text.Json;
using Antiphon.SessionRunner;
using Antiphon.SessionRunner.Contracts;
using Antiphon.SessionRunner.Tests;
using Antiphon.Server.Infrastructure.Agents.SessionRunner;
using Antiphon.Tests.TestHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;
using TUnit.Core.Exceptions;
using Antiphon.TestSupport;
using Antiphon.Tests.Application;

namespace Antiphon.Tests.Invariants;

[Category("Integration")]
[Category("RepositoryInvariant")]
public sealed class PhoneHomeCompletedCardTransportInvariantTests
{
    [Test]
    public void Phone_home_client_exposes_both_completed_card_operations()
    {
        typeof(PhoneHomeRunnerClient).GetMethod("RemoveCompletedCardWorkspaceAsync").ShouldNotBeNull("S6 server typed removal transport seam");
        typeof(PhoneHomeRunnerClient).GetMethod("InspectCompletedCardWorkspaceAsync").ShouldNotBeNull("S6 server typed inspection transport seam");
    }
}
