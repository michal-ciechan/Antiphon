using Xunit;

namespace Antiphon.Tests.TestHelpers;

/// <summary>
/// Collection definition that shares a single TestDbFixture across all database integration tests.
/// </summary>
[CollectionDefinition("Database")]
public class DatabaseCollection : ICollectionFixture<TestDbFixture>;
