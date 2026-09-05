using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>CARD-0327: <c>tracker.operator_logins</c> accepts a list, a scalar, or absence.</summary>
[Category("Unit")]
public class IssueTrackerConfigParserTests
{
    [Test]
    public void Operator_logins_list_form_is_parsed()
    {
        IssueTrackerConfigParser.TryParse(BoardWith("""
            ---
            tracker:
              kind: github
              repository: acme/app
              operator_logins: [michal-ciechan, other]
            ---
            body
            """), out var config, out var error).ShouldBeTrue(error);
        config!.OperatorLogins.ShouldBe(["michal-ciechan", "other"]);
    }

    [Test]
    public void Operator_logins_scalar_form_is_parsed()
    {
        IssueTrackerConfigParser.TryParse(BoardWith("""
            ---
            tracker:
              kind: github
              repository: acme/app
              operator_logins: michal-ciechan, other
            ---
            body
            """), out var config, out var error).ShouldBeTrue(error);
        config!.OperatorLogins.ShouldBe(["michal-ciechan", "other"]);
    }

    [Test]
    public void Operator_logins_absent_is_an_empty_list()
    {
        IssueTrackerConfigParser.TryParse(BoardWith("""
            ---
            tracker:
              kind: github
              repository: acme/app
            ---
            body
            """), out var config, out var error).ShouldBeTrue(error);
        config!.OperatorLogins.ShouldNotBeNull();
        config.OperatorLogins.ShouldBeEmpty();
    }

    private static Board BoardWith(string yaml)
    {
        var board = new Board { TrackerKind = TrackerKind.GitHubIssues };
        board.WorkflowDefinitions.Add(new BoardWorkflowDefinition
        {
            IsActive = true,
            Version = 1,
            Content = yaml,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        return board;
    }
}
