using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;
using Xunit;

namespace PharmaTrack.Tests.Support;

/// <summary>Runs the real API against a throwaway PostgreSQL database that is dropped afterwards.</summary>
public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string AdminPhone = "+998900000001";
    public const string AdminPassword = "Admin-Test-123";

    private readonly string _databaseName = $"pharmatrack_test_{Guid.NewGuid():N}";
    private readonly string _serverConnection;

    public ApiFactory()
    {
        _serverConnection = Environment.GetEnvironmentVariable("TEST_DB_CONNECTION")
            ?? "Host=localhost;Port=5432;Username=postgres;Password=postgres";

        var database = new NpgsqlConnectionStringBuilder(_serverConnection) { Database = _databaseName };
        Environment.SetEnvironmentVariable("ConnectionStrings__Default", database.ConnectionString);
        Environment.SetEnvironmentVariable("Jwt__SigningKey", "integration-tests-only-signing-key-0123456789");
        Environment.SetEnvironmentVariable("Seed__AdminPhone", AdminPhone);
        Environment.SetEnvironmentVariable("Seed__AdminPassword", AdminPassword);
        Environment.SetEnvironmentVariable("RateLimit__LoginPerMinute", "100000");
        Environment.SetEnvironmentVariable("Alerts__Enabled", "false");
        Environment.SetEnvironmentVariable("Swagger__Enabled", "true");
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder) => builder.UseEnvironment("Testing");

    public async Task InitializeAsync()
    {
        await using var connection = new NpgsqlConnection(_serverConnection);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"CREATE DATABASE {_databaseName}", connection);
        await command.ExecuteNonQueryAsync();
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await DisposeAsync();
        NpgsqlConnection.ClearAllPools();

        await using var connection = new NpgsqlConnection(_serverConnection);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"DROP DATABASE IF EXISTS {_databaseName} WITH (FORCE)", connection);
        await command.ExecuteNonQueryAsync();
    }
}

[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<ApiFactory>
{
    public const string Name = "api";
}
