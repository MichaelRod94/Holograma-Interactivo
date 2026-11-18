using DotNet.Testcontainers.Builders;
using Testcontainers.PostgreSql;

namespace Holograma3D.Tests.Infrastructure;

/// <summary>Un PostgreSQL real en Docker para toda la colección "db"; cada ApiFactory crea su propia base.</summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private PostgreSqlContainer? _container;

    public string ConnectionString => _container!.GetConnectionString();

    public async Task InitializeAsync()
    {
        try
        {
            _container = new PostgreSqlBuilder("postgres:18-alpine").Build();
            await _container.StartAsync();
        }
        catch (DockerUnavailableException ex)
        {
            throw new InvalidOperationException("Inicia Docker Desktop: los tests de integración usan Testcontainers.", ex);
        }
    }

    public async Task DisposeAsync()
    {
        if (_container is not null)
            await _container.DisposeAsync();
    }
}

[CollectionDefinition("db")]
public sealed class DbCollection : ICollectionFixture<PostgresFixture>;
