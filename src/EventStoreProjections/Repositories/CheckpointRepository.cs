using Dapper;
using Npgsql;

namespace EventStoreProjections.Repositories;

public interface ICheckpointRepository
{
    Task<ulong?> GetCheckpointAsync(string projectionName, CancellationToken cancellationToken = default);
    Task SaveCheckpointAsync(string projectionName, ulong position, CancellationToken cancellationToken = default);
}

public class CheckpointRepository : ICheckpointRepository
{
    private readonly string _connectionString;

    public CheckpointRepository(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task<ulong?> GetCheckpointAsync(string projectionName, CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        const string sql = @"
            SELECT checkpoint_position 
            FROM event_checkpoints 
            WHERE projection_name = @ProjectionName";

        var result = await connection.QuerySingleOrDefaultAsync<long?>(
            sql, 
            new { ProjectionName = projectionName });

        return result.HasValue ? (ulong)result.Value : null;
    }

    public async Task SaveCheckpointAsync(string projectionName, ulong position, CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        const string sql = @"
            INSERT INTO event_checkpoints (projection_name, checkpoint_position, last_updated)
            VALUES (@ProjectionName, @Position, @LastUpdated)
            ON CONFLICT (projection_name) 
            DO UPDATE SET 
                checkpoint_position = @Position,
                last_updated = @LastUpdated";

        await connection.ExecuteAsync(
            sql,
            new
            {
                ProjectionName = projectionName,
                Position = (long)position,
                LastUpdated = DateTime.UtcNow
            });
    }
}
