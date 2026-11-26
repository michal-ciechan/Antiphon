using System.Reflection;
using System.Text.Json;
using Antiphon.SessionRunner;
using Antiphon.SessionRunner.Contracts;
using Antiphon.TestSupport.LiveClaude;
using Microsoft.Extensions.Logging.Abstractions;
using Antiphon.Tests.Agents;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Enums;
using Microsoft.Extensions.Options;
using TUnit.Core;
using Antiphon.Tests.Scripts;
using static Antiphon.Tests.Scripts.LiveLaneIsolationTests;

namespace Antiphon.Tests.Invariants;

[Category("Unit")]
[Category("RepositoryInvariant")]
[Timeout(30_000)]
[NotInParallel]
public sealed class LiveLaneIsolationInvariantTests
{
    [Test]
    public void Canary_callers_match_the_declared_profile() =>
        LiveClaudeProfileAudit.C680Callers(Build,ClaudeDialogWatchdogCanaryTests.ProjectProfile);
}
