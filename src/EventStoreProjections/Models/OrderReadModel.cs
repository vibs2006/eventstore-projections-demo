namespace EventStoreProjections.Models;

public record OrderReadModel(
    Guid OrderId,
    string CustomerName,
    decimal Amount,
    string Status,
    DateTime CreatedAt,
    DateTime? CompletedAt = null,
    DateTime? CancelledAt = null,
    string? CancellationReason = null);
