using Ecomads.WebApplication.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace Ecomads.WebApplication.Tests.Integration;

public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("ecomads_tests")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
    }

    public async Task DisposeAsync()
    {
        await _container.DisposeAsync();
    }

    public async Task<EcomadsDbContext> CreateMigratedDbContextAsync()
    {
        var connectionString = await CreateDatabaseConnectionStringAsync();
        var dbContext = CreateDbContext(connectionString);
        await dbContext.Database.MigrateAsync();

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

        await using var connection = new NpgsqlConnection(_container.GetConnectionString());
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = $"""CREATE DATABASE "{databaseName}";""";
        await command.ExecuteNonQueryAsync();

        return BuildConnectionString(databaseName);
    }

    private string BuildConnectionString(string databaseName)
    {
        var builder = new NpgsqlConnectionStringBuilder(_container.GetConnectionString())
        {
            Database = databaseName,
            IncludeErrorDetail = true
        };

        return builder.ConnectionString;
    }
}

[CollectionDefinition(Name)]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "postgres";
}
