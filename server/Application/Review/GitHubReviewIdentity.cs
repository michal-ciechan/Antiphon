using System.Globalization;
using System.Text.RegularExpressions;
using Antiphon.Server.Application.Exceptions;

namespace Antiphon.Server.Application.Review;

internal static partial class GitHubReviewIdentity
{
    public static (string Repo, int Number) Parse(string input, int? prNumber)
    {
        var match = PullRequest().Match(input);
        var repo = match.Groups["repo"].Value;
        if (!match.Success || repo.Length > 256 || repo.Split('/').Any(part => part is "." or ".."))
            throw new ValidationException("repo", "Use a GitHub owner/name, owner/name/number or github.com pull request URL.");

        var numberText = match.Groups["number"].Value;
        if (numberText.Length > 0)
        {
            if (!int.TryParse(numberText, NumberStyles.None, CultureInfo.InvariantCulture, out var embeddedNumber) || embeddedNumber <= 0)
                throw new ValidationException("prNumber", "A PR number is positive.");
            if (prNumber is { } supplied && supplied != embeddedNumber)
                throw new ValidationException("prNumber", "PR number must match the repository URL or path.");
            prNumber = embeddedNumber;
        }

        if (prNumber is null)
            throw new ValidationException("prNumber", "A GitHub review needs a PR number.");
        return (repo.ToLowerInvariant(), prNumber.Value);
    }

    [GeneratedRegex(@"\A(?:(?i:https?://github\.com/))?(?<repo>[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+)(?:/(?:(?i:pull)/)?(?<number>[0-9]+))?/?(?:[?#].*)?\z")]
    private static partial Regex PullRequest();
}
