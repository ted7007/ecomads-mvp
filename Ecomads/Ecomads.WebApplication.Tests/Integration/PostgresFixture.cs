using Ecomads.WebApplication.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace Ecomads.WebApplication.Tests.Integration;

public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly string? externalConnection = Environment.GetEnvironmentVariable("ECOMADS_TEST_POSTGRES");
    private PostgreSqlContainer? container;
    private string ConnectionString => externalConnection ?? container!.GetConnectionString();

    public async Task InitializeAsync()
    {
        if (externalConnection != null) return;
        container = new PostgreSqlBuilder("postgres:16-alpine")
            .WithDatabase("ecomads_tests").WithUsername("postgres").WithPassword("postgres").Build();
        await container.StartAsync();
    }

    public async Task DisposeAsync()
    {
        if (container != null) await container.DisposeAsync();
    }

    public async Task<EcomadsDbContext> CreateMigratedDbContextAsync()
    {
        var connectionString = await CreateDatabaseConnectionStringAsync();
        var dbContext = CreateDbContext(connectionString);
        if (externalConnection == null) await dbContext.Database.MigrateAsync();
        else
        {
            // The isolated server may be reached through SSH. Apply the same migration SQL
            // as one batch, avoiding a network round trip for every migration bookkeeping step.
            // Tests of upgrades from older schemas still use IMigrator/MigrateAsync explicitly.
            var script = dbContext.GetService<IMigrator>().GenerateScript();
            await dbContext.Database.ExecuteSqlRawAsync(script);
        }

        return dbContext;
    }

    public EcomadsDbContext CreateDbContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<EcomadsDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new EcomadsDbContext(options);
    }

    public async Task<string> CreateDatabaseConnectionStringAsync()
    {
        var databaseName = $"ecomads_test_{Guid.NewGuid():N}";

        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = $"""CREATE DATABASE "{databaseName}";""";
        await command.ExecuteNonQueryAsync();

        return BuildConnectionString(databaseName);
    }

    private string BuildConnectionString(string databaseName)
    {
        var builder = new NpgsqlConnectionStringBuilder(ConnectionString)
        {
            Database = databaseName,
            IncludeErrorDetail = true
        };
        if (externalConnection != null)
        {
            builder.MaxPoolSize = 10;
            builder.ConnectionIdleLifetime = 15;
            builder.ConnectionPruningInterval = 5;
        }

        return builder.ConnectionString;
    }
}

[CollectionDefinition(Name)]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "postgres";
}
