namespace Antiphon.Resilience;

/// <summary>Named read clients. Command, stream and unnamed clients are not in this set.</summary>
public static class ResilienceClientNames
{
    public const string RunnerRead = "Resilience.RunnerRead";
    public const string GitHubRead = "Resilience.GitHubRead";
    public const string GitHubIssuesRead = "Resilience.GitHubIssuesRead";
    public const string JiraRead = "Resilience.JiraRead";
    public const string ProviderProbeRead = "Resilience.ProviderProbeRead";
    public const string GitConnectivityRead = "Resilience.GitConnectivityRead";

    public static readonly IReadOnlyList<string> All =
    [
        RunnerRead,
        GitHubRead,
        GitHubIssuesRead,
        JiraRead,
        ProviderProbeRead,
        GitConnectivityRead,
    ];

    public static bool IsReadClient(string? name) =>
        name is not null && All.Contains(name, StringComparer.Ordinal);
}

/// <summary>Bounded metric labels. Host names and operation ids are not labels.</summary>
public static class ResilienceDependencies
{
    public const string Runner = "runner";
    public const string GitHub = "github";
    public const string GitHubIssues = "github-issues";
    public const string Jira = "jira";
    public const string ProviderProbe = "provider-probe";
    public const string GitConnectivity = "git-connectivity";
    public const string Database = "database";

    public static string ForClient(string clientName) => clientName switch
    {
        ResilienceClientNames.RunnerRead => Runner,
        ResilienceClientNames.GitHubRead => GitHub,
        ResilienceClientNames.GitHubIssuesRead => GitHubIssues,
        ResilienceClientNames.JiraRead => Jira,
        ResilienceClientNames.ProviderProbeRead => ProviderProbe,
        ResilienceClientNames.GitConnectivityRead => GitConnectivity,
        _ => "unknown",
    };

    /// <summary>Authorities for these dependencies are capped and evicted.</summary>
    public static bool IsDynamic(string dependency) =>
        dependency is ProviderProbe or GitConnectivity or Jira or GitHubIssues;
}

public static class ResilienceProfiles
{
    public const string RunnerList = "runner-list";
    public const string RunnerCapability = "runner-capability";
    public const string GitConnectivity = "git-connectivity";

    public static readonly IReadOnlyDictionary<string, TimeSpan> OwnerCaps =
        new Dictionary<string, TimeSpan>(StringComparer.Ordinal)
        {
            [RunnerList] = TimeSpan.FromSeconds(3),
            [RunnerCapability] = TimeSpan.FromSeconds(5),
            [GitConnectivity] = TimeSpan.FromSeconds(10),
        };

    public static bool IsKnown(string name) => OwnerCaps.ContainsKey(name);
}

public static class ResilienceOperations
{
    public const string RunnerCapabilities = "runner.get-capabilities";
    public const string RunnerHealth = "runner.get-health";
    public const string RunnerList = "runner.list";
    public const string RunnerGet = "runner.get";
    public const string RunnerBuffer = "runner.get-buffer";
    public const string RunnerSnapshot = "runner.get-snapshot";
    public const string RunnerTranscript = "runner.get-transcript";
    public const string RunnerCompactionObservation = "runner.observe-compaction";
    public const string RunnerVerificationCustody = "runner.read-verification-custody";
    public const string RunnerInspectHerdrPane = "runner.inspect-herdr-pane";
    public const string RunnerHerdrDisposal = "runner.get-herdr-pane-disposal";
    public const string RunnerHerdrDisposalPreview = "runner.get-herdr-pane-disposal-preview";

    public const string GitHubPullRequestComments = "github.pull-request-comments";
    public const string GitHubCombinedStatus = "github.combined-status";
    public const string GitHubCheckRuns = "github.check-runs";
    public const string GitHubPullRequest = "github.pull-request";
    public const string GitHubUser = "github.user";
    public const string GitHubRepositories = "github.repositories";
    public const string GitHubBranches = "github.branches";
    public const string GitHubPullRequestByBranch = "github.pull-request-by-branch";

    public const string GitHubIssuesByStates = "github-issues.fetch-by-states";
    public const string GitHubIssuesByIds = "github-issues.fetch-by-ids";
    public const string GitHubIssuesComments = "github-issues.fetch-comments";

    public const string JiraSearch = "jira.search";

    public const string ProviderProbeOpenAi = "provider-probe.openai-models";
    public const string ProviderProbeOllama = "provider-probe.ollama-tags";

    public const string GitConnectivity = "git-connectivity.info-refs";

    public const string LlmProvidersList = "database.llm-providers.list";
    public const string LlmProvidersGet = "database.llm-providers.get";
    public const string WorkflowTemplatesList = "database.workflow-templates.list";
    public const string WorkflowTemplatesGet = "database.workflow-templates.get";

    private static readonly Dictionary<(string Dependency, string Operation), AdmittedOperation> Catalog = Build();

    public static bool TryAdmit(string dependency, string? operation, HttpMethod method, out AdmittedOperation admitted)
    {
        admitted = default;
        if (string.IsNullOrEmpty(operation))
            return false;
        return Catalog.TryGetValue((dependency, operation), out admitted)
            && string.Equals(admitted.Method, method.Method, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsAdmittedDatabaseRead(string? operation) =>
        operation is LlmProvidersList or LlmProvidersGet or WorkflowTemplatesList or WorkflowTemplatesGet;

    private static Dictionary<(string, string), AdmittedOperation> Build()
    {
        var map = new Dictionary<(string, string), AdmittedOperation>();
        void Add(string dependency, string operation, string method, string? profile = null) =>
            map[(dependency, operation)] = new AdmittedOperation(operation, method, profile);

        Add(ResilienceDependencies.Runner, RunnerCapabilities, "GET");
        Add(ResilienceDependencies.Runner, RunnerHealth, "GET");
        Add(ResilienceDependencies.Runner, RunnerList, "GET", ResilienceProfiles.RunnerList);
        Add(ResilienceDependencies.Runner, RunnerGet, "GET");
        Add(ResilienceDependencies.Runner, RunnerBuffer, "GET");
        Add(ResilienceDependencies.Runner, RunnerSnapshot, "GET");
        Add(ResilienceDependencies.Runner, RunnerTranscript, "GET");
        Add(ResilienceDependencies.Runner, RunnerCompactionObservation, "GET");
        Add(ResilienceDependencies.Runner, RunnerVerificationCustody, "GET");
        Add(ResilienceDependencies.Runner, RunnerInspectHerdrPane, "GET");
        Add(ResilienceDependencies.Runner, RunnerHerdrDisposal, "GET");
        Add(ResilienceDependencies.Runner, RunnerHerdrDisposalPreview, "GET");

        Add(ResilienceDependencies.GitHub, GitHubPullRequestComments, "GET");
        Add(ResilienceDependencies.GitHub, GitHubCombinedStatus, "GET");
        Add(ResilienceDependencies.GitHub, GitHubCheckRuns, "GET");
        Add(ResilienceDependencies.GitHub, GitHubPullRequest, "GET");
        Add(ResilienceDependencies.GitHub, GitHubUser, "GET");
        Add(ResilienceDependencies.GitHub, GitHubRepositories, "GET");
        Add(ResilienceDependencies.GitHub, GitHubBranches, "GET");
        Add(ResilienceDependencies.GitHub, GitHubPullRequestByBranch, "GET");

        Add(ResilienceDependencies.GitHubIssues, GitHubIssuesByStates, "GET");
        Add(ResilienceDependencies.GitHubIssues, GitHubIssuesByIds, "GET");
        Add(ResilienceDependencies.GitHubIssues, GitHubIssuesComments, "GET");

        Add(ResilienceDependencies.Jira, JiraSearch, "GET");

        Add(ResilienceDependencies.ProviderProbe, ProviderProbeOpenAi, "GET");
        Add(ResilienceDependencies.ProviderProbe, ProviderProbeOllama, "GET");

        Add(ResilienceDependencies.GitConnectivity, GitConnectivity, "GET", ResilienceProfiles.GitConnectivity);
        return map;
    }
}

public readonly record struct AdmittedOperation(string Name, string Method, string? Profile);
