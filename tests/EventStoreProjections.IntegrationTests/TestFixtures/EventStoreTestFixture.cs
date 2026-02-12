using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using EventStore.Client;
using Npgsql;
using Xunit;

namespace EventStoreProjections.IntegrationTests.TestFixtures;

/// <summary>
/// Test fixture for managing EventStore and PostgreSQL containers.
/// </summary>
public class EventStoreTestFixture : IAsyncLifetime
{
    private IContainer? _eventStoreContainer;
    private IContainer? _postgresContainer;

    public EventStoreClient EventStoreClient { get; private set; } = null!;
    public string PostgresConnectionString { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        // Start EventStore container
        _eventStoreContainer = new ContainerBuilder()
            .WithImage("eventstore/eventstore:23.10.0-bookworm-slim")
            .WithPortBinding(2113, true)
            .WithEnvironment("EVENTSTORE_CLUSTER_SIZE", "1")
            .WithEnvironment("EVENTSTORE_RUN_PROJECTIONS", "All")
            .WithEnvironment("EVENTSTORE_START_STANDARD_PROJECTIONS", "true")
            .WithEnvironment("EVENTSTORE_INSECURE", "true")
            .WithEnvironment("EVENTSTORE_ENABLE_ATOM_PUB_OVER_HTTP", "true")
            .WithWaitStrategy(Wait.ForUnixContainer().UntilPortIsAvailable(2113))
            .Build();

        await _eventStoreContainer.StartAsync();

        // Start PostgreSQL container
        _postgresContainer = new ContainerBuilder()
            .WithImage("postgres:16-alpine")
            .WithPortBinding(5432, true)
            .WithEnvironment("POSTGRES_DB", "projections")
            .WithEnvironment("POSTGRES_USER", "postgres")
            .WithEnvironment("POSTGRES_PASSWORD", "postgres")
            .WithWaitStrategy(Wait.ForUnixContainer().UntilCommandIsCompleted("pg_isready"))
            .Build();

        await _postgresContainer.StartAsync();

        // Configure EventStore client
        var eventStorePort = _eventStoreContainer.GetMappedPublicPort(2113);
        var eventStoreSettings = EventStoreClientSettings.Create($"esdb://localhost:{eventStorePort}?tls=false");
        EventStoreClient = new EventStoreClient(eventStoreSettings);

        // Configure PostgreSQL connection
        var postgresPort = _postgresContainer.GetMappedPublicPort(5432);
        PostgresConnectionString = $"Host=localhost;Port={postgresPort};Database=projections;Username=postgres;Password=postgres";

        // Initialize database schema
        await InitializeDatabaseAsync();
    }

    private async Task InitializeDatabaseAsync()
    {
        await using var connection = new NpgsqlConnection(PostgresConnectionString);
        await connection.OpenAsync();

        const string sql = @"
            -- Create event checkpoints table
            CREATE TABLE IF NOT EXISTS event_checkpoints (
                projection_name VARCHAR(255) PRIMARY KEY,
                checkpoint_position BIGINT NOT NULL,
                last_updated TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP
            );

            -- Create orders read model table
            CREATE TABLE IF NOT EXISTS orders_read_model (
                order_id UUID PRIMARY KEY,
                customer_name VARCHAR(255) NOT NULL,
                amount DECIMAL(18, 2) NOT NULL,
                status VARCHAR(50) NOT NULL,
                created_at TIMESTAMP NOT NULL,
                completed_at TIMESTAMP NULL,
                cancelled_at TIMESTAMP NULL,
                cancellation_reason TEXT NULL
            );

            -- Create indexes for performance
            CREATE INDEX IF NOT EXISTS idx_orders_status ON orders_read_model(status);
            CREATE INDEX IF NOT EXISTS idx_orders_created_at ON orders_read_model(created_at DESC);
            CREATE INDEX IF NOT EXISTS idx_orders_customer_name ON orders_read_model(customer_name);";

        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    public async Task DisposeAsync()
    {
        EventStoreClient?.Dispose();

        if (_eventStoreContainer != null)
            await _eventStoreContainer.DisposeAsync();

        if (_postgresContainer != null)
            await _postgresContainer.DisposeAsync();
    }
}
