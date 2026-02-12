using System.Text.Json;
using EventStore.Client;
using EventStoreProjections.Models;
using EventStoreProjections.Repositories;

namespace EventStoreProjections.Services;

/// <summary>
/// Background service that subscribes to EventStore and projects events to a read model.
/// </summary>
public class OrderProjectionService : BackgroundService
{
    private readonly EventStoreClient _eventStoreClient;
    private readonly ICheckpointRepository _checkpointRepository;
    private readonly IOrderReadModelRepository _orderRepository;
    private readonly ILogger<OrderProjectionService> _logger;
    private const string ProjectionName = "OrderProjection";
    private const int CheckpointInterval = 100;
    private ulong _currentPosition;
    private int _eventsSinceLastCheckpoint;

    public OrderProjectionService(
        EventStoreClient eventStoreClient,
        ICheckpointRepository checkpointRepository,
        IOrderReadModelRepository orderRepository,
        ILogger<OrderProjectionService> logger)
    {
        _eventStoreClient = eventStoreClient;
        _checkpointRepository = checkpointRepository;
        _orderRepository = orderRepository;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            _logger.LogInformation("Starting Order Projection Service");

            // Load checkpoint
            var checkpoint = await _checkpointRepository.GetCheckpointAsync(ProjectionName, stoppingToken);
            var fromPosition = checkpoint.HasValue ? FromAll.After(new Position(checkpoint.Value, checkpoint.Value)) : FromAll.Start;

            _logger.LogInformation("Resuming from checkpoint: {Checkpoint}", checkpoint?.ToString() ?? "Start");

            // Subscribe to all events
            await foreach (var resolvedEvent in _eventStoreClient.SubscribeToAll(fromPosition, cancellationToken: stoppingToken))
            {
                try
                {
                    // Skip system events
                    if (resolvedEvent.Event.EventType.StartsWith("$"))
                        continue;

                    // Update current position
                    _currentPosition = resolvedEvent.Event.Position.CommitPosition;

                    // Process the event
                    await ProcessEventAsync(resolvedEvent, stoppingToken);

                    // Increment counter
                    _eventsSinceLastCheckpoint++;

                    // Save checkpoint every CheckpointInterval events
                    if (_eventsSinceLastCheckpoint >= CheckpointInterval)
                    {
                        await SaveCheckpointAsync(stoppingToken);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing event {EventType} at position {Position}",
                        resolvedEvent.Event.EventType,
                        resolvedEvent.Event.Position.CommitPosition);
                    throw;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fatal error in Order Projection Service");
            throw;
        }
    }

    private async Task ProcessEventAsync(ResolvedEvent resolvedEvent, CancellationToken cancellationToken)
    {
        var eventType = resolvedEvent.Event.EventType;
        var eventData = resolvedEvent.Event.Data.ToArray();

        _logger.LogDebug("Processing event {EventType} at position {Position}",
            eventType,
            resolvedEvent.Event.Position.CommitPosition);

        switch (eventType)
        {
            case "OrderCreated":
                await HandleOrderCreatedAsync(eventData, cancellationToken);
                break;

            case "OrderCompleted":
                await HandleOrderCompletedAsync(eventData, cancellationToken);
                break;

            case "OrderCancelled":
                await HandleOrderCancelledAsync(eventData, cancellationToken);
                break;

            default:
                // Ignore unknown event types
                _logger.LogDebug("Ignoring unknown event type: {EventType}", eventType);
                break;
        }
    }

    private async Task HandleOrderCreatedAsync(byte[] eventData, CancellationToken cancellationToken)
    {
        var orderCreated = JsonSerializer.Deserialize<OrderCreatedEvent>(eventData);
        if (orderCreated == null)
        {
            _logger.LogWarning("Failed to deserialize OrderCreatedEvent");
            return;
        }

        _logger.LogInformation("Creating order read model for OrderId: {OrderId}", orderCreated.OrderId);

        var readModel = new OrderReadModel(
            orderCreated.OrderId,
            orderCreated.CustomerName,
            orderCreated.Amount,
            "Created",
            orderCreated.CreatedAt);

        await _orderRepository.SaveOrderAsync(readModel, cancellationToken);
    }

    private async Task HandleOrderCompletedAsync(byte[] eventData, CancellationToken cancellationToken)
    {
        var orderCompleted = JsonSerializer.Deserialize<OrderCompletedEvent>(eventData);
        if (orderCompleted == null)
        {
            _logger.LogWarning("Failed to deserialize OrderCompletedEvent");
            return;
        }

        _logger.LogInformation("Updating order status to Completed for OrderId: {OrderId}", orderCompleted.OrderId);

        await _orderRepository.UpdateOrderStatusAsync(
            orderCompleted.OrderId,
            "Completed",
            completedAt: orderCompleted.CompletedAt,
            cancellationToken: cancellationToken);
    }

    private async Task HandleOrderCancelledAsync(byte[] eventData, CancellationToken cancellationToken)
    {
        var orderCancelled = JsonSerializer.Deserialize<OrderCancelledEvent>(eventData);
        if (orderCancelled == null)
        {
            _logger.LogWarning("Failed to deserialize OrderCancelledEvent");
            return;
        }

        _logger.LogInformation("Updating order status to Cancelled for OrderId: {OrderId}", orderCancelled.OrderId);

        await _orderRepository.UpdateOrderStatusAsync(
            orderCancelled.OrderId,
            "Cancelled",
            cancelledAt: orderCancelled.CancelledAt,
            cancellationReason: orderCancelled.Reason,
            cancellationToken: cancellationToken);
    }

    private async Task SaveCheckpointAsync(CancellationToken cancellationToken)
    {
        await _checkpointRepository.SaveCheckpointAsync(ProjectionName, _currentPosition, cancellationToken);
        _logger.LogInformation("Saved checkpoint at position {Position}", _currentPosition);
        _eventsSinceLastCheckpoint = 0;
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Stopping Order Projection Service");

        // Save final checkpoint
        if (_eventsSinceLastCheckpoint > 0)
        {
            await SaveCheckpointAsync(cancellationToken);
        }

        await base.StopAsync(cancellationToken);
    }
}
