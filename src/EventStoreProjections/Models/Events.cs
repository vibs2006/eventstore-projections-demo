namespace EventStoreProjections.Models;

public record OrderCreatedEvent(Guid OrderId, string CustomerName, decimal Amount, DateTime CreatedAt);

public record OrderCompletedEvent(Guid OrderId, DateTime CompletedAt);

public record OrderCancelledEvent(Guid OrderId, string Reason, DateTime CancelledAt);
