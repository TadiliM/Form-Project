using Testcontainers.PostgreSql;

namespace backend.Tests;

/// <summary>
/// Starts a SINGLE PostgreSQL container for the whole test suite.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    public PostgreSqlContainer Container { get; } = new PostgreSqlBuilder("postgres:16")
        .WithDatabase("formproject_tests")
        .WithUsername("test")
        .WithPassword("test")
        .Build();

    public Task InitializeAsync() => Container.StartAsync();

    public Task DisposeAsync() => Container.DisposeAsync().AsTask();
}

/// <summary>
/// Declares the "postgres" collection: every test class marked with
/// [Collection("postgres")] shares this <see cref="PostgresFixture"/> instance
/// (the container starts only once per dotnet test run).
/// </summary>
[CollectionDefinition("postgres")]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>
{
}
