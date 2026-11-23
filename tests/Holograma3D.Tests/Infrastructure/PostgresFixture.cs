using Microsoft.Extensions.Configuration;
using Npgsql;

namespace Holograma3D.Tests.Infrastructure;

/// <summary>
/// Una base "holograma3d_test_*" en tu PostgreSQL local para toda la corrida, con la cadena de la Api
/// (user-secrets o variable de entorno). Los tests no chocan porque cada uno usa usuarios nuevos.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private const string Prefix = "holograma3d_test_";

    // ponytail: una sola base por corrida; en Windows cada DROP DATABASE fuerza un checkpoint (varios segundos).
    public string ConnectionString { get; private set; } = "";

    public Task InitializeAsync()
    {
        var configuration = new ConfigurationBuilder()
            .AddUserSecrets(typeof(Program).Assembly, optional: true)
            .AddEnvironmentVariables()
            .Build();
        var local = configuration.GetConnectionString("Holograma3D")
            ?? throw new InvalidOperationException(
                "Los tests de integración usan tu PostgreSQL local: guarda la cadena con dotnet user-secrets (ver README).");
        // EF la crea al migrar (el rol necesita CREATEDB).
        ConnectionString = new NpgsqlConnectionStringBuilder(local) { Database = Prefix + Guid.NewGuid().ToString("N") }.ConnectionString;
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        if (ConnectionString == "")
            return;
        NpgsqlConnection.ClearAllPools();
        await using var connection = new NpgsqlConnection(
            new NpgsqlConnectionStringBuilder(ConnectionString) { Database = "postgres", CommandTimeout = 120 }.ConnectionString);
        await connection.OpenAsync();

        // También borra las que dejó una corrida interrumpida.
        var names = new List<string>();
        await using (var list = new NpgsqlCommand($"SELECT datname FROM pg_database WHERE starts_with(datname, '{Prefix}')", connection))
        await using (var reader = await list.ExecuteReaderAsync())
            while (await reader.ReadAsync())
                names.Add(reader.GetString(0));

        foreach (var name in names)
        {
            await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{name}\" WITH (FORCE)", connection);
            await drop.ExecuteNonQueryAsync();
        }
    }
}

[CollectionDefinition("db")]
public sealed class DbCollection : ICollectionFixture<PostgresFixture>;
