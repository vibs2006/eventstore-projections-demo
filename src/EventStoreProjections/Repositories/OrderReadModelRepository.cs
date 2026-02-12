using Dapper;
using EventStoreProjections.Models;
using Npgsql;

namespace EventStoreProjections.Repositories;

public interface IOrderReadModelRepository
{
    Task SaveOrderAsync(OrderReadModel order, CancellationToken cancellationToken = default);
    Task UpdateOrderStatusAsync(Guid orderId, string status, DateTime? completedAt = null, DateTime? cancelledAt = null, string? cancellationReason = null, CancellationToken cancellationToken = default);
    Task<OrderReadModel?> GetOrderAsync(Guid orderId, CancellationToken cancellationToken = default);
    Task<IEnumerable<OrderReadModel>> GetAllOrdersAsync(CancellationToken cancellationToken = default);
}

public class OrderReadModelRepository : IOrderReadModelRepository
{
    private readonly string _connectionString;

    public OrderReadModelRepository(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task SaveOrderAsync(OrderReadModel order, CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        const string sql = @"
            INSERT INTO orders_read_model (order_id, customer_name, amount, status, created_at)
            VALUES (@OrderId, @CustomerName, @Amount, @Status, @CreatedAt)
            ON CONFLICT (order_id) 
            DO UPDATE SET 
                customer_name = @CustomerName,
                amount = @Amount,
                status = @Status";

        await connection.ExecuteAsync(sql, order);
    }

    public async Task UpdateOrderStatusAsync(
        Guid orderId, 
        string status, 
        DateTime? completedAt = null, 
        DateTime? cancelledAt = null, 
        string? cancellationReason = null,
        CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        const string sql = @"
            UPDATE orders_read_model 
            SET status = @Status,
                completed_at = @CompletedAt,
                cancelled_at = @CancelledAt,
                cancellation_reason = @CancellationReason
            WHERE order_id = @OrderId";

        await connection.ExecuteAsync(sql, new
        {
            OrderId = orderId,
            Status = status,
            CompletedAt = completedAt,
            CancelledAt = cancelledAt,
            CancellationReason = cancellationReason
        });
    }

    public async Task<OrderReadModel?> GetOrderAsync(Guid orderId, CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        const string sql = @"
            SELECT order_id as OrderId, 
                   customer_name as CustomerName, 
                   amount as Amount, 
                   status as Status, 
                   created_at as CreatedAt,
                   completed_at as CompletedAt,
                   cancelled_at as CancelledAt,
                   cancellation_reason as CancellationReason
            FROM orders_read_model 
            WHERE order_id = @OrderId";

        return await connection.QuerySingleOrDefaultAsync<OrderReadModel>(
            sql, 
            new { OrderId = orderId });
    }

    public async Task<IEnumerable<OrderReadModel>> GetAllOrdersAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        const string sql = @"
            SELECT order_id as OrderId, 
                   customer_name as CustomerName, 
                   amount as Amount, 
                   status as Status, 
                   created_at as CreatedAt,
                   completed_at as CompletedAt,
                   cancelled_at as CancelledAt,
                   cancellation_reason as CancellationReason
            FROM orders_read_model
            ORDER BY created_at DESC";

        return await connection.QueryAsync<OrderReadModel>(sql);
    }
}
