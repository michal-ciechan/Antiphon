using Xunit;

namespace Antiphon.E2E;

/// <summary>
/// Forces all E2E test classes into a single xUnit collection so they run
/// sequentially rather than in parallel. Running multiple Docker containers
/// and Playwright instances simultaneously causes port contention and
/// resource exhaustion in CI.
/// </summary>
[CollectionDefinition("E2E", DisableParallelization = true)]
public class E2ECollectionDefinition;
