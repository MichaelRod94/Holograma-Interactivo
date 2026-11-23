using Holograma3D.Infrastructure;
using Npgsql;

namespace Holograma3D.Tests.Infrastructure;

/// <summary>
/// Una base "holograma3d_test_*" en tu PostgreSQL local para toda la corrida, con la cadena de la Api
/// (variable de entorno o .env). Los tests no chocan porque cada uno usa usuarios nuevos.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private const string Prefix = "holograma3d_test_";

    // ponytail: una sola base por corrida; en Windows cada DROP DATABASE fuerza un checkpoint (varios segundos).
    public string ConnectionString { get; private set; } = "";

    public Task InitializeAsync()
    {
        // Se lee el .env sin cargarlo al entorno del proceso: así no se cuela su proveedor de IA en los tests.
        const string key = "ConnectionStrings__Holograma3D";
        var local = Environment.GetEnvironmentVariable(key)
            ?? (DotEnv.Find() is { } path ? DotEnv.Read(path).GetValueOrDefault(key) : null)
            ?? throw new InvalidOperationException(
                $"Los tests de integración usan tu PostgreSQL local: pon {key} en el .env de la raíz del repo (ver README).");
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
